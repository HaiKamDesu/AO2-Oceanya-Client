using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.GoogleDriveSync;
using OceanyaClient.Features.WebAssets;

namespace OceanyaClient
{
    /// <summary>
    /// Provides workflows for rebuilding client-side asset indexes.
    /// </summary>
    public static class ClientAssetRefreshService
    {
        private const int RefreshMarkerSchemaVersion = 1;

        /// <summary>
        /// Raised on a background thread after any asset refresh completes, naming what changed.
        /// </summary>
        /// <remarks>
        /// Refreshes happen from several places that the UI cannot see - the deferred startup scan, the
        /// live asset watcher, Drive sync - and before this event existed the newly parsed assets simply
        /// sat in the cache until something happened to rebuild a dropdown. That is why a character added
        /// mid-session "showed up way later" rather than when it was refreshed. Subscribers must marshal
        /// to their own thread.
        /// </remarks>
        public static event Action<AssetRefreshCompletedEventArgs>? AssetsRefreshed;

        /// <summary>Raises <see cref="AssetsRefreshed"/>, never letting a subscriber break the refresh.</summary>
        private static void RaiseAssetsRefreshed(AssetRefreshCompletedEventArgs args)
        {
            try
            {
                // A refresh can add characters that the on-demand probe previously recorded as missing,
                // and can change which button art a character has, so both negative caches are dropped.
                CharacterFolder.ClearOnDemandMissCache();
                WebCharacterIconResolver.ResetPrefetchedCharacters();
                AssetsRefreshed?.Invoke(args);
            }
            catch (Exception ex)
            {
                CustomConsole.Error("An asset refresh subscriber threw.", ex, CustomConsole.LogCategory.System);
            }
        }

        // The startup background asset scan (GetTrackedChangePlanForCurrentEnvironment → CaptureCurrentAssetStateSnapshot)
        // does heavy single-threaded disk I/O over every character folder. If it runs concurrently with the GM
        // snapshot restore's server connect, it starves that connect's own disk reads and the connect balloons from
        // ~1s to ~15s on a local server (measured), leaving the window built-but-disabled. This gate lets the launch
        // path defer the scan until the critical connect/restore has finished so they do not fight for the disk.
        private static readonly object startupGateLock = new object();
        private static TaskCompletionSource<bool> startupCriticalPathGate =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Re-arms the launch-critical gate for a new launch.
        /// </summary>
        /// <remarks>
        /// Closing the GM window reopens the configuration window IN THE SAME PROCESS, so a session can
        /// launch several times. The gate used to be a single one-shot completion source, which meant every
        /// launch after the first saw it already completed and started its deferred asset work immediately -
        /// straight back into contention with that launch's window creation and server connect (measured:
        /// the full refresh began 635 ms BEFORE the connect finished). Each launch must arm its own gate.
        /// </remarks>
        public static void BeginStartupCriticalPath()
        {
            lock (startupGateLock)
            {
                if (!startupCriticalPathGate.Task.IsCompleted)
                {
                    return;
                }

                startupCriticalPathGate =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        /// <summary>
        /// Signals that the launch-critical work (e.g. GM snapshot restore + server connect) has finished, so the
        /// deferred startup asset scan may run. Safe to call multiple times / from any startup path.
        /// </summary>
        public static void SignalStartupCriticalPathComplete()
        {
            lock (startupGateLock)
            {
                startupCriticalPathGate.TrySetResult(true);
            }
        }

        /// <summary>
        /// Waits until <see cref="SignalStartupCriticalPathComplete"/> is called, or the timeout elapses (safety
        /// fallback so the scan still runs if the signal never arrives).
        /// </summary>
        public static async Task WaitForStartupCriticalPathAsync(int timeoutMs)
        {
            Task gateTask;
            lock (startupGateLock)
            {
                gateTask = startupCriticalPathGate.Task;
            }

            await Task.WhenAny(gateTask, Task.Delay(timeoutMs));
        }

        /// <summary>
        /// Refreshes all supported client-side asset caches without blocking the UI.
        /// </summary>
        /// <remarks>
        /// This deliberately does NOT show <see cref="WaitForm"/> any more. A full refresh can take tens of
        /// seconds on a large install, and locking the whole client behind a modal "Refreshing all assets"
        /// form for that long was the single most disruptive thing about adding assets. The work still runs
        /// off the UI thread, callers can still await completion, and <see cref="AssetsRefreshed"/> lets the
        /// live UI fold the result in whenever it lands. Progress is reported to the debug console.
        /// </remarks>
        /// <param name="owner">Unused; kept so every call site keeps its owner-window contract.</param>
        public static Task RefreshCharactersAndBackgroundsAsync(Window owner)
        {
            _ = owner;
            return Task.Run(() => RefreshAllAssets(ReportRefreshProgress));
        }

        /// <summary>Minimum gap between progress lines written to the debug console.</summary>
        private const int RefreshProgressLogIntervalMs = 1000;

        private static readonly object refreshProgressLock = new object();
        private static long lastRefreshProgressTicks;

        /// <summary>
        /// Routes refresh progress to the debug console instead of a blocking wait form, throttled.
        /// </summary>
        /// <remarks>
        /// Progress fires once per character (twice, plus an integrity line, on a full refresh), and a
        /// large install has thousands of characters. Logging each one measured at 15,932 of 16,488 lines
        /// in a two-minute session - 96% of the log - and every one of those costs a string format, a
        /// Console.WriteLine, a Debug.WriteLine, an event dispatch and a file write, on top of the refresh
        /// it is supposed to be reporting. One line per second is enough to see that it is progressing.
        /// </remarks>
        private static void ReportRefreshProgress(string subtitle)
        {
            if (string.IsNullOrWhiteSpace(subtitle))
            {
                return;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            lock (refreshProgressLock)
            {
                if (nowTicks - lastRefreshProgressTicks < RefreshProgressLogIntervalMs * TimeSpan.TicksPerMillisecond)
                {
                    return;
                }

                lastRefreshProgressTicks = nowTicks;
            }

            CustomConsole.Info("[ASSET-REFRESH] " + subtitle, CustomConsole.LogCategory.System);
        }

        /// <summary>Logs a refresh milestone unconditionally, bypassing the progress throttle.</summary>
        private static void ReportRefreshMilestone(string message)
        {
            lock (refreshProgressLock)
            {
                lastRefreshProgressTicks = DateTime.UtcNow.Ticks;
            }

            CustomConsole.Info("[ASSET-REFRESH] " + message, CustomConsole.LogCategory.System);
        }

        /// <summary>
        /// Refreshes a single character while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        public static async Task RefreshCharacterAsync(Window owner, string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
            {
                return;
            }

            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan();
            plan.CharacterNames.Add(characterName.Trim());
            await RefreshTargetedAssetsAsync(owner, plan);
        }

        /// <summary>
        /// Refreshes all character caches while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        public static async Task RefreshAllCharactersAsync(Window owner)
        {
            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan
            {
                RequiresFullCharacterRefresh = true
            };
            await RefreshTargetedAssetsAsync(owner, plan);
        }

        /// <summary>
        /// Refreshes only the supplied asset scope without blocking the UI.
        /// </summary>
        /// <param name="owner">Unused; kept so every call site keeps its owner-window contract.</param>
        /// <param name="plan">Scope to refresh.</param>
        internal static Task RefreshTargetedAssetsAsync(Window owner, TargetedAssetRefreshPlan plan)
        {
            _ = owner;
            if (plan == null || !plan.HasAnyWork)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() => RefreshAssets(plan, ReportRefreshProgress));
        }

        /// <summary>
        /// Refreshes only the supplied asset scope without blocking the launching window.
        /// </summary>
        internal static Task RefreshTargetedAssetsInBackgroundAsync(TargetedAssetRefreshPlan plan)
        {
            if (plan == null || !plan.HasAnyWork)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                try
                {
                    RefreshAssets(plan, progress: null);
                }
                catch (Exception ex)
                {
                    CustomConsole.Error("Background targeted asset refresh failed.", ex, CustomConsole.LogCategory.System);
                }
            });
        }

        /// <summary>
        /// Refreshes only the assets affected by the supplied local sync changes.
        /// </summary>
        public static void RefreshChangedAssets(
            string configIniPath,
            string localRootPath,
            GoogleDriveSyncLocalChangeSet localChanges,
            Action<string>? progress = null)
        {
            if (localChanges == null || !localChanges.HasAnyChanges)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(configIniPath))
            {
                throw new InvalidOperationException("A valid config.ini path is required before refreshing assets.");
            }

            if (string.IsNullOrWhiteSpace(localRootPath))
            {
                throw new InvalidOperationException("A valid local sync folder is required before refreshing assets.");
            }

            Globals.UpdateConfigINI(configIniPath);

            TargetedAssetRefreshPlan plan = BuildTargetedPlan(localChanges);
            RefreshAssets(plan, progress);
        }

        /// <summary>
        /// Returns true when character/background refresh should be forced for the current app/config state.
        /// </summary>
        public static bool RequiresRefreshForCurrentEnvironment()
        {
            return !string.IsNullOrWhiteSpace(GetRefreshRequirementReasonForCurrentEnvironment());
        }

        /// <summary>
        /// Returns a human-readable reason when a full refresh is required for the current app/config state.
        /// </summary>
        public static string GetRefreshRequirementReasonForCurrentEnvironment()
        {
            try
            {
                string markerPath = GetRefreshMarkerPath();
                if (!File.Exists(markerPath))
                {
                    return "No prior asset refresh marker was found for this app/config combination.";
                }

                string json = File.ReadAllText(markerPath);
                AssetRefreshMarker? marker = JsonSerializer.Deserialize<AssetRefreshMarker>(json);
                if (marker == null)
                {
                    return "The saved asset refresh marker could not be read.";
                }

                return EvaluateRefreshRequirementReason(
                    marker,
                    GetAppVersion(),
                    Globals.PathToConfigINI ?? string.Empty,
                    // Physical mounts only: a web mirror appearing on connect is not a user mount change
                    // and must not trigger the "refresh all assets?" prompt.
                    Globals.PhysicalBaseFolders);
            }
            catch
            {
                return "Oceanya could not verify the previous asset refresh marker.";
            }
        }

        /// <summary>
        /// Returns a targeted refresh plan for tracked asset-file changes when the environment itself still matches the saved refresh marker.
        /// </summary>
        internal static TargetedAssetRefreshPlan GetTrackedChangePlanForCurrentEnvironment()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(GetRefreshRequirementReasonForCurrentEnvironment()))
                {
                    return new TargetedAssetRefreshPlan();
                }

                AssetRefreshMarker? marker = LoadRefreshMarker();
                AssetRefreshStateSnapshot? previousState = NormalizeAssetState(marker?.AssetState);
                if (previousState == null)
                {
                    return new TargetedAssetRefreshPlan();
                }

                Globals.UpdateConfigINI(Globals.PathToConfigINI);
                AssetRefreshStateSnapshot currentState = CaptureCurrentAssetStateSnapshot();
                return BuildTrackedChangePlan(previousState, currentState);
            }
            catch
            {
                return new TargetedAssetRefreshPlan();
            }
        }

        internal static string EvaluateRefreshRequirementReason(
            AssetRefreshMarker marker,
            string currentAppVersion,
            string currentConfigIniPath,
            IReadOnlyList<string> currentResolvedBaseFolders)
        {
            if (marker == null)
            {
                return "The saved asset refresh marker could not be read.";
            }

            if (marker.SchemaVersion != RefreshMarkerSchemaVersion)
            {
                return "The saved asset refresh marker uses an older schema.";
            }

            string markerAppVersion = marker.AppVersion?.Trim() ?? string.Empty;
            string normalizedCurrentAppVersion = currentAppVersion?.Trim() ?? string.Empty;
            if (!string.Equals(markerAppVersion, normalizedCurrentAppVersion, StringComparison.OrdinalIgnoreCase))
            {
                return "the Oceanya version changed since the last full asset refresh"
                    + $" (previous: {FormatReasonValue(markerAppVersion)}, current: {FormatReasonValue(normalizedCurrentAppVersion)}).";
            }

            string markerConfigIniPath = NormalizePathForComparison(marker.ConfigIniPath);
            string normalizedCurrentConfigIniPath = NormalizePathForComparison(currentConfigIniPath);
            bool configPathChanged = !string.Equals(
                markerConfigIniPath,
                normalizedCurrentConfigIniPath,
                StringComparison.OrdinalIgnoreCase);
            List<string> currentConfiguredBaseFolders = BuildConfiguredBaseFolderSignature(currentConfigIniPath);
            if (IsEquivalentFolderSequence(marker.BaseFolders, currentConfiguredBaseFolders))
            {
                return string.Empty;
            }

            // Legacy compatibility: older markers stored the currently resolved directories.
            if (IsEquivalentFolderSequence(marker.BaseFolders, currentResolvedBaseFolders))
            {
                return string.Empty;
            }

            // Another legacy compatibility path: stale generated folders that no longer exist
            // should not force refresh forever after the mount configuration has otherwise stabilized.
            List<string> existingMarkerBaseFolders = (marker.BaseFolders ?? new List<string>())
                .Where(path =>
                {
                    try
                    {
                        return Directory.Exists(path);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
            if (existingMarkerBaseFolders.Count > 0
                && IsEquivalentFolderSequence(existingMarkerBaseFolders, currentResolvedBaseFolders))
            {
                return string.Empty;
            }

            if (configPathChanged)
            {
                return "the selected AO config.ini path changed and the AO mount/base-folder list no longer matches"
                    + " the last full asset refresh"
                    + $" (previous config.ini: {FormatReasonValue(markerConfigIniPath)},"
                    + $" current config.ini: {FormatReasonValue(normalizedCurrentConfigIniPath)}).";
            }

            return "the AO mount/base-folder list in config.ini changed since the last full asset refresh"
                + $" (previous folders: {FormatReasonFolderList(marker.BaseFolders)},"
                + $" current folders: {FormatReasonFolderList(currentConfiguredBaseFolders)}).";
        }

        internal static TargetedAssetRefreshPlan BuildTargetedPlan(GoogleDriveSyncLocalChangeSet localChanges)
        {
            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan();
            foreach (string rawPath in localChanges.GetAllAffectedPaths())
            {
                string normalizedPath = NormalizeRelativePath(rawPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    continue;
                }

                string first = segments[0];
                if (string.Equals(first, "characters", StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Length < 2)
                    {
                        plan.RequiresFullCharacterRefresh = true;
                    }
                    else
                    {
                        plan.CharacterNames.Add(segments[1]);
                    }

                    continue;
                }

                if (string.Equals(first, "background", StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Length < 2)
                    {
                        plan.RequiresFullBackgroundRefresh = true;
                    }
                    else
                    {
                        plan.BackgroundNames.Add(segments[1]);
                    }

                    continue;
                }

                if (string.Equals(first, "sounds", StringComparison.OrdinalIgnoreCase)
                    && segments.Length >= 2
                    && string.Equals(segments[1], "blips", StringComparison.OrdinalIgnoreCase))
                {
                    plan.RefreshBlips = true;
                    continue;
                }

                if (string.Equals(first, "misc", StringComparison.OrdinalIgnoreCase))
                {
                    plan.RefreshChats = true;
                    plan.RefreshEffects = true;
                }
            }

            return plan;
        }

        internal static TargetedAssetRefreshPlan BuildTrackedChangePlan(
            AssetRefreshStateSnapshot? previousState,
            AssetRefreshStateSnapshot? currentState)
        {
            AssetRefreshStateSnapshot normalizedPrevious = NormalizeAssetState(previousState) ?? new AssetRefreshStateSnapshot();
            AssetRefreshStateSnapshot normalizedCurrent = NormalizeAssetState(currentState) ?? new AssetRefreshStateSnapshot();
            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan();

            foreach (string characterName in normalizedPrevious.Characters.Keys
                .Union(normalizedCurrent.Characters.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                bool hadPrevious = normalizedPrevious.Characters.TryGetValue(characterName, out AssetTrackedFolderState? previousCharacter);
                bool hasCurrent = normalizedCurrent.Characters.TryGetValue(characterName, out AssetTrackedFolderState? currentCharacter);
                if (!hadPrevious
                    || !hasCurrent
                    || !string.Equals(previousCharacter?.Signature, currentCharacter?.Signature, StringComparison.Ordinal))
                {
                    plan.CharacterNames.Add(characterName);
                }
            }

            foreach (string backgroundName in normalizedPrevious.Backgrounds.Keys
                .Union(normalizedCurrent.Backgrounds.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                bool hadPrevious = normalizedPrevious.Backgrounds.TryGetValue(backgroundName, out AssetTrackedFolderState? previousBackground);
                bool hasCurrent = normalizedCurrent.Backgrounds.TryGetValue(backgroundName, out AssetTrackedFolderState? currentBackground);
                if (!hadPrevious
                    || !hasCurrent
                    || !string.Equals(previousBackground?.Signature, currentBackground?.Signature, StringComparison.Ordinal))
                {
                    plan.BackgroundNames.Add(backgroundName);
                }
            }

            plan.RefreshBlips = !string.Equals(
                normalizedPrevious.BlipsSignature,
                normalizedCurrent.BlipsSignature,
                StringComparison.Ordinal);
            plan.RefreshChats = !string.Equals(
                normalizedPrevious.ChatsSignature,
                normalizedCurrent.ChatsSignature,
                StringComparison.Ordinal);
            plan.RefreshEffects = !string.Equals(
                normalizedPrevious.EffectsSignature,
                normalizedCurrent.EffectsSignature,
                StringComparison.Ordinal);

            return plan;
        }

        private static void RefreshAllAssets(Action<string>? progress)
        {
            System.Diagnostics.Stopwatch refreshStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ReportRefreshMilestone("Full asset refresh started.");
            Globals.UpdateConfigINI(Globals.PathToConfigINI);
            RefreshAllCharacters(progress);
            RefreshAllBackgrounds(progress);

            List<string> failures = new List<string>();
            progress?.Invoke("Indexing blip files...");
            RunCatalogRefresh("blips", () => BlipCatalog.Refresh(), failures);
            progress?.Invoke("Indexing chat profiles...");
            RunCatalogRefresh("chat profiles", () => ChatCatalog.Refresh(), failures);
            progress?.Invoke("Indexing effects folders...");
            RunCatalogRefresh("effects folders", () => EffectsFolderCatalog.Refresh(), failures);

            long beforeMarkerMs = refreshStopwatch.ElapsedMilliseconds;
            PersistRefreshMarker(forceFullStateCapture: true);
            ReportRefreshMilestone(
                $"Full asset refresh finished in {refreshStopwatch.ElapsedMilliseconds}ms"
                + $" (marker capture {refreshStopwatch.ElapsedMilliseconds - beforeMarkerMs}ms,"
                + $" characters={CharacterFolder.CachedCharacterCount}, failures={failures.Count}).");
            RaiseAssetsRefreshed(AssetRefreshCompletedEventArgs.ForFullRefresh());
            ThrowIfAnyRefreshFailed(failures);
        }

        private static void RefreshAssets(TargetedAssetRefreshPlan plan, Action<string>? progress)
        {
            if (plan == null || !plan.HasAnyWork)
            {
                return;
            }

            bool performedWork = false;
            // One unreadable character/background must not abort the rest of the batch, and must not
            // escape as an unhandled exception. Failures are collected and reported once at the end.
            List<string> failures = new List<string>();

            if (plan.RequiresFullCharacterRefresh)
            {
                RefreshAllCharacters(progress);
                performedWork = true;
            }
            else
            {
                foreach (string characterName in plan.CharacterNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        RefreshCharacter(characterName, progress);
                    }
                    catch (Exception ex)
                    {
                        CustomConsole.Error($"Character refresh failed for '{characterName}'.", ex, CustomConsole.LogCategory.System);
                        failures.Add($"character '{characterName}': {ex.Message}");
                    }

                    performedWork = true;
                }
            }

            if (plan.RequiresFullBackgroundRefresh)
            {
                RefreshAllBackgrounds(progress);
                performedWork = true;
            }
            else
            {
                foreach (string backgroundName in plan.BackgroundNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        RefreshBackground(backgroundName, progress);
                    }
                    catch (Exception ex)
                    {
                        CustomConsole.Error($"Background refresh failed for '{backgroundName}'.", ex, CustomConsole.LogCategory.System);
                        failures.Add($"background '{backgroundName}': {ex.Message}");
                    }

                    performedWork = true;
                }
            }

            if (plan.RefreshBlips)
            {
                progress?.Invoke("Indexing blip files...");
                RunCatalogRefresh("blips", () => BlipCatalog.Refresh(), failures);
                performedWork = true;
            }

            if (plan.RefreshChats)
            {
                progress?.Invoke("Indexing chat profiles...");
                RunCatalogRefresh("chat profiles", () => ChatCatalog.Refresh(), failures);
                performedWork = true;
            }

            if (plan.RefreshEffects)
            {
                progress?.Invoke("Indexing effects folders...");
                RunCatalogRefresh("effects folders", () => EffectsFolderCatalog.Refresh(), failures);
                performedWork = true;
            }

            if (performedWork)
            {
                System.Diagnostics.Stopwatch markerStopwatch = System.Diagnostics.Stopwatch.StartNew();
                PersistRefreshMarker(plan);
                ReportRefreshMilestone(
                    $"Targeted refresh applied (characters={plan.CharacterNames.Count}"
                    + $"{(plan.RequiresFullCharacterRefresh ? "+all" : string.Empty)},"
                    + $" backgrounds={plan.BackgroundNames.Count}"
                    + $"{(plan.RequiresFullBackgroundRefresh ? "+all" : string.Empty)},"
                    + $" failures={failures.Count}); marker capture {markerStopwatch.ElapsedMilliseconds}ms.");
                RaiseAssetsRefreshed(AssetRefreshCompletedEventArgs.ForPlan(plan));
            }

            ThrowIfAnyRefreshFailed(failures);
        }

        /// <summary>Runs one catalog refresh, recording (not rethrowing) a failure.</summary>
        private static void RunCatalogRefresh(string catalogName, Action refresh, List<string> failures)
        {
            try
            {
                refresh();
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to index {catalogName}.", ex, CustomConsole.LogCategory.System);
                failures.Add($"{catalogName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Reports collected refresh failures as a single exception so the caller can show them, after
        /// every other asset in the batch has already been refreshed.
        /// </summary>
        private static void ThrowIfAnyRefreshFailed(List<string> failures)
        {
            if (failures.Count == 0)
            {
                return;
            }

            const int maxReportedFailures = 5;
            string reported = string.Join(
                Environment.NewLine,
                failures.Take(maxReportedFailures));
            if (failures.Count > maxReportedFailures)
            {
                reported += Environment.NewLine + $"(+{failures.Count - maxReportedFailures} more; see the debug console)";
            }

            throw new InvalidOperationException(
                "Some assets could not be refreshed:" + Environment.NewLine + reported);
        }

        /// <summary>
        /// Rebuilds the character index.
        /// </summary>
        /// <remarks>
        /// This deliberately does NOT run <see cref="CharacterIntegrityVerifier"/>. The verifier walks every
        /// file of every character folder and writes a report; measured at ~17 s of a 18.2 s full refresh on
        /// a 2627-character install, i.e. 94% of the work, for a diagnostic that only the Character Database
        /// Viewer displays. It now runs there, in the background, on the characters actually being shown.
        /// </remarks>
        private static void RefreshAllCharacters(Action<string>? progress)
        {
            // Only the indexed callback is wired: onParsedCharacter reports the same event and doubled the
            // progress volume for nothing.
            CharacterFolder.RefreshCharacterList(
                onParsedCharacterProgress: (character, currentIndex, totalCharacters) =>
                {
                    progress?.Invoke($"Parsed character ({currentIndex}/{totalCharacters}): {character.Name}");
                },
                onChangedMountPath: path =>
                {
                    progress?.Invoke("Changed mount path: " + path);
                });

        }

        private static void RefreshAllBackgrounds(Action<string>? progress)
        {
            Background.RefreshCache(
                onChangedMountPath: path =>
                {
                    progress?.Invoke("Indexed background mount path: " + path);
                });
        }

        private static void RefreshCharacter(string characterName, Action<string>? progress)
        {
            string? existingDirectory = CharacterFolder.FindIndexEntryByName(characterName)?.DirectoryPath;

            string resolvedDirectory = ResolveEffectiveMountedDirectory("characters", characterName);
            if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                progress?.Invoke("Refreshing character: " + characterName);
                if (!CharacterFolder.TryUpsertCharacterFolderInCache(
                        resolvedDirectory,
                        existingDirectory,
                        out CharacterFolder? character,
                        out string errorMessage))
                {
                    throw new InvalidOperationException(
                        "Failed to refresh character '" + characterName + "': " + errorMessage);
                }

                return;
            }

            progress?.Invoke("Removing character: " + characterName);
            if (!CharacterFolder.TryRemoveCharacterFolderFromCache(existingDirectory, characterName, out _, out string removeError))
            {
                throw new InvalidOperationException(
                    "Failed to remove character '" + characterName + "' from cache: " + removeError);
            }

            if (!string.IsNullOrWhiteSpace(existingDirectory))
            {
                DeleteIntegrityReport(existingDirectory);
            }
        }

        private static void RefreshBackground(string backgroundName, Action<string>? progress)
        {
            Background? existingBackground = Background.FromBGPath(backgroundName);
            string? existingDirectory = existingBackground?.PathToFile;
            string resolvedDirectory = ResolveEffectiveMountedDirectory("background", backgroundName);
            if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                progress?.Invoke("Refreshing background: " + backgroundName);
                if (!Background.TryUpsertBackgroundInCache(
                        resolvedDirectory,
                        out _,
                        out string errorMessage))
                {
                    throw new InvalidOperationException(
                        "Failed to refresh background '" + backgroundName + "': " + errorMessage);
                }

                return;
            }

            progress?.Invoke("Removing background: " + backgroundName);
            if (!Background.TryRemoveBackgroundFromCache(existingDirectory, backgroundName, out _, out string removeError))
            {
                throw new InvalidOperationException(
                    "Failed to remove background '" + backgroundName + "' from cache: " + removeError);
            }
        }

        private static string ResolveEffectiveMountedDirectory(string categoryFolderName, string entryName)
        {
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string candidateDirectory = Path.Combine(baseFolder, categoryFolderName, entryName);
                if (!Directory.Exists(candidateDirectory))
                {
                    continue;
                }

                if (string.Equals(categoryFolderName, "characters", StringComparison.OrdinalIgnoreCase))
                {
                    string charIniPath = Path.Combine(candidateDirectory, "char.ini");
                    if (!File.Exists(charIniPath))
                    {
                        continue;
                    }
                }

                return candidateDirectory;
            }

            return string.Empty;
        }

        private static void DeleteIntegrityReport(string characterDirectory)
        {
            try
            {
                string reportPath = CharacterIntegrityVerifier.GetReportFilePath(characterDirectory);
                if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
                {
                    File.Delete(reportPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static void PersistRefreshMarker(
            TargetedAssetRefreshPlan? changedPlan = null,
            bool forceFullStateCapture = false)
        {
            try
            {
                string markerPath = GetRefreshMarkerPath();
                string markerDirectory = Path.GetDirectoryName(markerPath) ?? string.Empty;
                Directory.CreateDirectory(markerDirectory);

                AssetRefreshMarker marker = LoadRefreshMarker() ?? new AssetRefreshMarker();
                marker.SchemaVersion = RefreshMarkerSchemaVersion;
                marker.AppVersion = GetAppVersion();
                marker.ConfigIniPath = NormalizePathForComparison(Globals.PathToConfigINI);
                marker.BaseFolders = BuildConfiguredBaseFolderSignature(Globals.PathToConfigINI);

                if (marker.BaseFolders.Count == 0)
                {
                    marker.BaseFolders = NormalizeFolderSequence(Globals.PhysicalBaseFolders);
                }

                marker.AssetState = forceFullStateCapture || changedPlan == null
                    ? CaptureCurrentAssetStateSnapshot()
                    : UpdateTrackedAssetState(marker.AssetState, changedPlan);

                string json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(markerPath, json);
            }
            catch
            {
                // Marker persistence is best-effort; refresh still completed.
            }
        }

        internal static string GetRefreshMarkerPath()
        {
            string saveFileDirectory = Path.GetDirectoryName(SaveFile.CurrentStoragePath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(saveFileDirectory))
            {
                return Path.Combine(saveFileDirectory, "cache", "asset_refresh_marker.json");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache",
                "asset_refresh_marker.json");
        }

        private static string GetAppVersion()
        {
            return AppVersionInfo.AssemblyVersion;
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        }

        private static AssetRefreshMarker? LoadRefreshMarker()
        {
            string markerPath = GetRefreshMarkerPath();
            if (!File.Exists(markerPath))
            {
                return null;
            }

            string json = File.ReadAllText(markerPath);
            return JsonSerializer.Deserialize<AssetRefreshMarker>(json);
        }

        private static AssetRefreshStateSnapshot CaptureCurrentAssetStateSnapshot()
        {
            System.Diagnostics.Stopwatch captureStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                return CaptureCurrentAssetStateSnapshotCore();
            }
            finally
            {
                CustomConsole.Info(
                    $"[ASSET-REFRESH] Asset state snapshot captured in {captureStopwatch.ElapsedMilliseconds}ms.",
                    CustomConsole.LogCategory.System);
            }
        }

        private static AssetRefreshStateSnapshot CaptureCurrentAssetStateSnapshotCore()
        {
            return NormalizeAssetState(new AssetRefreshStateSnapshot
            {
                Characters = CaptureTrackedFolderStates("characters", requirePrimaryCharacterIni: true),
                Backgrounds = CaptureTrackedFolderStates("background", requirePrimaryCharacterIni: false),
                BlipsSignature = ComputeSequenceSignature(CaptureBlipEntries()),
                ChatsSignature = ComputeSequenceSignature(CaptureChatEntries()),
                EffectsSignature = ComputeSequenceSignature(CaptureEffectsEntries())
            }) ?? new AssetRefreshStateSnapshot();
        }

        private static AssetRefreshStateSnapshot UpdateTrackedAssetState(
            AssetRefreshStateSnapshot? existingState,
            TargetedAssetRefreshPlan changedPlan)
        {
            AssetRefreshStateSnapshot? normalizedExisting = NormalizeAssetState(existingState);
            if (normalizedExisting == null
                || normalizedExisting.FormatVersion != AssetRefreshStateSnapshot.CurrentFormatVersion)
            {
                return CaptureCurrentAssetStateSnapshot();
            }

            AssetRefreshStateSnapshot normalized = normalizedExisting;
            if (changedPlan == null || !changedPlan.HasAnyWork)
            {
                return normalized;
            }

            if (changedPlan.RequiresFullCharacterRefresh)
            {
                normalized.Characters = CaptureTrackedFolderStates("characters", requirePrimaryCharacterIni: true);
            }
            else
            {
                foreach (string characterName in changedPlan.CharacterNames)
                {
                    UpdateTrackedFolderState(
                        normalized.Characters,
                        "characters",
                        characterName,
                        requirePrimaryCharacterIni: true);
                }
            }

            if (changedPlan.RequiresFullBackgroundRefresh)
            {
                normalized.Backgrounds = CaptureTrackedFolderStates("background", requirePrimaryCharacterIni: false);
            }
            else
            {
                foreach (string backgroundName in changedPlan.BackgroundNames)
                {
                    UpdateTrackedFolderState(
                        normalized.Backgrounds,
                        "background",
                        backgroundName,
                        requirePrimaryCharacterIni: false);
                }
            }

            if (changedPlan.RefreshBlips)
            {
                normalized.BlipsSignature = ComputeSequenceSignature(CaptureBlipEntries());
            }

            if (changedPlan.RefreshChats)
            {
                normalized.ChatsSignature = ComputeSequenceSignature(CaptureChatEntries());
            }

            if (changedPlan.RefreshEffects)
            {
                normalized.EffectsSignature = ComputeSequenceSignature(CaptureEffectsEntries());
            }

            return NormalizeAssetState(normalized) ?? new AssetRefreshStateSnapshot();
        }

        private static Dictionary<string, AssetTrackedFolderState> CaptureTrackedFolderStates(
            string categoryFolderName,
            bool requirePrimaryCharacterIni)
        {
            Dictionary<string, AssetTrackedFolderState> states =
                new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.PhysicalBaseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string categoryRoot = Path.Combine(baseFolder, categoryFolderName);
                if (!Directory.Exists(categoryRoot))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(categoryRoot);
                }
                catch
                {
                    continue;
                }

                // Signature computation is per-folder disk work with no shared state, so it runs across
                // cores instead of one folder at a time. Mount order still decides which duplicate name
                // wins, so candidates are filtered in order first and only the hashing is parallel.
                List<(string EntryName, string DirectoryPath)> candidates =
                    new List<(string EntryName, string DirectoryPath)>();
                foreach (string directoryPath in directories)
                {
                    string entryName = Path.GetFileName(directoryPath);
                    if (string.IsNullOrWhiteSpace(entryName) || states.ContainsKey(entryName))
                    {
                        continue;
                    }

                    if (requirePrimaryCharacterIni
                        && !File.Exists(Path.Combine(directoryPath, "char.ini")))
                    {
                        continue;
                    }

                    states[entryName] = new AssetTrackedFolderState
                    {
                        Name = entryName,
                        DirectoryPath = NormalizePathForComparison(directoryPath)
                    };
                    candidates.Add((entryName, directoryPath));
                }

                ParallelOptions signatureOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = AssetRefreshParallelism.GetDegreeOfParallelism(candidates.Count)
                };
                Parallel.ForEach(candidates, signatureOptions, candidate =>
                {
                    string signature = ComputeDirectorySignature(candidate.DirectoryPath);
                    lock (states)
                    {
                        if (states.TryGetValue(candidate.EntryName, out AssetTrackedFolderState? state))
                        {
                            state.Signature = signature;
                        }
                    }
                });
            }

            return states;
        }

        private static void UpdateTrackedFolderState(
            Dictionary<string, AssetTrackedFolderState> states,
            string categoryFolderName,
            string entryName,
            bool requirePrimaryCharacterIni)
        {
            string normalizedEntryName = (entryName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedEntryName))
            {
                return;
            }

            string resolvedDirectory = ResolveEffectiveMountedDirectory(categoryFolderName, normalizedEntryName);
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                states.Remove(normalizedEntryName);
                return;
            }

            if (requirePrimaryCharacterIni
                && !File.Exists(Path.Combine(resolvedDirectory, "char.ini")))
            {
                states.Remove(normalizedEntryName);
                return;
            }

            states[normalizedEntryName] = new AssetTrackedFolderState
            {
                Name = normalizedEntryName,
                DirectoryPath = NormalizePathForComparison(resolvedDirectory),
                Signature = ComputeDirectorySignature(resolvedDirectory)
            };
        }

        private static IEnumerable<string> CaptureBlipEntries()
        {
            string[] allowedExtensions = { ".opus", ".ogg", ".mp3", ".wav" };
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.PhysicalBaseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string blipsRoot = Path.Combine(baseFolder, "sounds", "blips");
                if (!Directory.Exists(blipsRoot))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(blipsRoot, "*", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string filePath in files)
                {
                    if (!allowedExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relative = Path.ChangeExtension(
                        Path.GetRelativePath(blipsRoot, filePath).Replace('\\', '/').Trim('/'),
                        null) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(relative))
                    {
                        values.Add(relative);
                    }
                }
            }

            return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> CaptureChatEntries()
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "default"
            };
            foreach (string baseFolder in Globals.PhysicalBaseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string miscRoot = Path.Combine(baseFolder, "misc");
                if (!Directory.Exists(miscRoot))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(miscRoot, "*", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string directoryPath in directories)
                {
                    if (!File.Exists(Path.Combine(directoryPath, "config.ini")))
                    {
                        continue;
                    }

                    string relative = NormalizeRelativePath(Path.GetRelativePath(miscRoot, directoryPath));
                    if (!string.IsNullOrWhiteSpace(relative))
                    {
                        values.Add(relative);
                    }
                }
            }

            return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> CaptureEffectsEntries()
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseFolder in Globals.PhysicalBaseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string miscRoot = Path.Combine(baseFolder, "misc");
                if (!Directory.Exists(miscRoot))
                {
                    continue;
                }

                IEnumerable<string> effectFiles;
                try
                {
                    effectFiles = Directory.EnumerateFiles(miscRoot, "effects.ini", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string effectsIniPath in effectFiles)
                {
                    string folderPath = Path.GetDirectoryName(effectsIniPath) ?? string.Empty;
                    string relative = NormalizeRelativePath(Path.GetRelativePath(miscRoot, folderPath));
                    if (!string.IsNullOrWhiteSpace(relative))
                    {
                        values.Add(relative);
                    }
                }
            }

            return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ComputeDirectorySignature(string directoryPath)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            string normalizedDirectory = NormalizePathForComparison(directoryPath);
            if (string.IsNullOrWhiteSpace(normalizedDirectory) || !Directory.Exists(normalizedDirectory))
            {
                AppendHashLine(hash, "missing");
                return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            try
            {
                // One enumeration, and the size/timestamp come from the enumeration's own directory data.
                //
                // This used to be two full recursive walks (directories, then files) plus a `new FileInfo`
                // and a `File.GetLastWriteTimeUtc` per file - two extra syscalls each, on every file of
                // every character, on every launch. Measured at 16.3 seconds of solid disk churn for 2627
                // characters before the client was usable. EnumerateFileSystemInfos carries Length and
                // LastWriteTimeUtc in the entry it already read, so the same signature costs one walk and
                // no per-file stat.
                List<string> signatureLines = new List<string>();
                foreach (FileSystemInfo entry in new DirectoryInfo(normalizedDirectory)
                    .EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    string relativePath = NormalizeRelativePath(
                        Path.GetRelativePath(normalizedDirectory, entry.FullName));
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    try
                    {
                        if (entry is FileInfo fileEntry)
                        {
                            if (GoogleDriveLocalSnapshotBuilder.IsReservedSupportFile(relativePath))
                            {
                                continue;
                            }

                            signatureLines.Add(
                                "F|" + relativePath + "|" + fileEntry.Length + "|" + fileEntry.LastWriteTimeUtc.Ticks);
                        }
                        else
                        {
                            signatureLines.Add("D|" + relativePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        signatureLines.Add("E|" + relativePath + "|" + ex.GetType().Name);
                    }
                }

                signatureLines.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string signatureLine in signatureLines)
                {
                    AppendHashLine(hash, signatureLine);
                }
            }
            catch (Exception ex)
            {
                AppendHashLine(hash, "ERR|" + ex.GetType().Name);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        private static string ComputeSequenceSignature(IEnumerable<string>? values)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string value in (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                AppendHashLine(hash, value);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        private static void AppendHashLine(IncrementalHash hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\n");
            hash.AppendData(bytes);
        }

        private static AssetRefreshStateSnapshot? NormalizeAssetState(AssetRefreshStateSnapshot? state)
        {
            if (state == null)
            {
                return null;
            }

            AssetRefreshStateSnapshot normalized = new AssetRefreshStateSnapshot
            {
                FormatVersion = state.FormatVersion <= 0 ? AssetRefreshStateSnapshot.CurrentFormatVersion : state.FormatVersion,
                BlipsSignature = state.BlipsSignature?.Trim() ?? string.Empty,
                ChatsSignature = state.ChatsSignature?.Trim() ?? string.Empty,
                EffectsSignature = state.EffectsSignature?.Trim() ?? string.Empty,
                Characters = NormalizeTrackedFolderStates(state.Characters),
                Backgrounds = NormalizeTrackedFolderStates(state.Backgrounds)
            };
            return normalized;
        }

        private static Dictionary<string, AssetTrackedFolderState> NormalizeTrackedFolderStates(
            Dictionary<string, AssetTrackedFolderState>? states)
        {
            Dictionary<string, AssetTrackedFolderState> normalized =
                new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AssetTrackedFolderState> pair in states
                ?? new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase))
            {
                string key = (pair.Key ?? string.Empty).Trim();
                AssetTrackedFolderState? value = pair.Value;
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                normalized[key] = new AssetTrackedFolderState
                {
                    Name = string.IsNullOrWhiteSpace(value.Name) ? key : value.Name.Trim(),
                    DirectoryPath = NormalizePathForComparison(value.DirectoryPath),
                    Signature = value.Signature?.Trim() ?? string.Empty
                };
            }

            return normalized;
        }

        internal static List<string> BuildConfiguredBaseFolderSignature(string configIniPath)
        {
            string normalizedConfigIniPath = NormalizePathForComparison(configIniPath);
            if (string.IsNullOrWhiteSpace(normalizedConfigIniPath) || !File.Exists(normalizedConfigIniPath))
            {
                return new List<string>();
            }

            string configDirectory = NormalizePathForComparison(Path.GetDirectoryName(normalizedConfigIniPath));
            string configMountParentDirectory = NormalizePathForComparison(
                Path.GetDirectoryName(configDirectory));

            List<string> configuredEntries = new List<string> { configDirectory };
            configuredEntries.AddRange(ReadConfiguredMountPathEntries(normalizedConfigIniPath));
            configuredEntries.Reverse();

            List<string> signatures = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configuredEntry in configuredEntries)
            {
                string normalizedEntry = string.Equals(
                        NormalizePathForComparison(configuredEntry),
                        configDirectory,
                        StringComparison.OrdinalIgnoreCase)
                    ? configDirectory
                    : NormalizeConfiguredMountEntry(configuredEntry, configMountParentDirectory);
                if (string.IsNullOrWhiteSpace(normalizedEntry))
                {
                    continue;
                }

                if (seenPaths.Add(normalizedEntry))
                {
                    signatures.Add(normalizedEntry);
                }
            }

            return signatures;
        }

        private static List<string> ReadConfiguredMountPathEntries(string configIniPath)
        {
            foreach (string line in File.ReadLines(configIniPath))
            {
                if (!line.StartsWith("mount_paths=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string raw = line["mount_paths=".Length..].Trim();
                if (string.IsNullOrWhiteSpace(raw)
                    || string.Equals(raw, "@Invalid()", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string>();
                }

                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            return new List<string>();
        }

        private static string NormalizeConfiguredMountEntry(string configuredEntry, string configMountParentDirectory)
        {
            string trimmedEntry = configuredEntry?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedEntry))
            {
                return string.Empty;
            }

            try
            {
                string candidate = trimmedEntry;
                if (!Path.IsPathRooted(candidate) && !string.IsNullOrWhiteSpace(configMountParentDirectory))
                {
                    candidate = Path.Combine(configMountParentDirectory, candidate);
                }

                return NormalizePathForComparison(candidate);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsEquivalentFolderSequence(IEnumerable<string>? first, IEnumerable<string>? second)
        {
            List<string> normalizedFirst = NormalizeFolderSequence(first);
            List<string> normalizedSecond = NormalizeFolderSequence(second);
            if (normalizedFirst.Count != normalizedSecond.Count)
            {
                return false;
            }

            for (int i = 0; i < normalizedFirst.Count; i++)
            {
                if (!string.Equals(normalizedFirst[i], normalizedSecond[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> NormalizeFolderSequence(IEnumerable<string>? paths)
        {
            List<string> normalizedPaths = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths ?? Enumerable.Empty<string>())
            {
                string normalizedPath = NormalizePathForComparison(path);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (seenPaths.Add(normalizedPath))
                {
                    normalizedPaths.Add(normalizedPath);
                }
            }

            return normalizedPaths;
        }

        private static string FormatReasonFolderList(IEnumerable<string>? paths)
        {
            List<string> normalizedPaths = NormalizeFolderSequence(paths);
            if (normalizedPaths.Count == 0)
            {
                return "(none recorded)";
            }

            const int maxDisplayedPaths = 3;
            List<string> displayedPaths = normalizedPaths
                .Take(maxDisplayedPaths)
                .Select(FormatReasonValue)
                .ToList();
            int remainingPathCount = normalizedPaths.Count - displayedPaths.Count;
            if (remainingPathCount > 0)
            {
                displayedPaths.Add($"+{remainingPathCount} more");
            }

            return string.Join(", ", displayedPaths);
        }

        private static string FormatReasonValue(string? value)
        {
            string trimmedValue = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedValue))
            {
                return "(not recorded)";
            }

            return "\"" + trimmedValue + "\"";
        }

        private static string NormalizePathForComparison(string? path)
        {
            string trimmedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(trimmedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return trimmedPath
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }

    /// <summary>Describes what an asset refresh changed.</summary>
    public sealed class AssetRefreshCompletedEventArgs : EventArgs
    {
        private AssetRefreshCompletedEventArgs(
            bool refreshedAllCharacters,
            bool refreshedAllBackgrounds,
            IReadOnlyCollection<string> characterNames,
            IReadOnlyCollection<string> backgroundNames)
        {
            RefreshedAllCharacters = refreshedAllCharacters;
            RefreshedAllBackgrounds = refreshedAllBackgrounds;
            CharacterNames = characterNames;
            BackgroundNames = backgroundNames;
        }

        /// <summary>Every character was reparsed, so no individual names are listed.</summary>
        public bool RefreshedAllCharacters { get; }

        /// <summary>Every background was reindexed, so no individual names are listed.</summary>
        public bool RefreshedAllBackgrounds { get; }

        /// <summary>Characters that were refreshed when <see cref="RefreshedAllCharacters"/> is false.</summary>
        public IReadOnlyCollection<string> CharacterNames { get; }

        /// <summary>Backgrounds that were refreshed when <see cref="RefreshedAllBackgrounds"/> is false.</summary>
        public IReadOnlyCollection<string> BackgroundNames { get; }

        internal static AssetRefreshCompletedEventArgs ForFullRefresh()
        {
            return new AssetRefreshCompletedEventArgs(
                refreshedAllCharacters: true,
                refreshedAllBackgrounds: true,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        internal static AssetRefreshCompletedEventArgs ForPlan(TargetedAssetRefreshPlan plan)
        {
            return new AssetRefreshCompletedEventArgs(
                plan.RequiresFullCharacterRefresh,
                plan.RequiresFullBackgroundRefresh,
                plan.CharacterNames.ToList(),
                plan.BackgroundNames.ToList());
        }
    }

    internal sealed class TargetedAssetRefreshPlan
    {
        public HashSet<string> CharacterNames { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> BackgroundNames { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool RequiresFullCharacterRefresh { get; set; }
        public bool RequiresFullBackgroundRefresh { get; set; }
        public bool RefreshBlips { get; set; }
        public bool RefreshChats { get; set; }
        public bool RefreshEffects { get; set; }

        public bool HasAnyWork =>
            RequiresFullCharacterRefresh
            || RequiresFullBackgroundRefresh
            || RefreshBlips
            || RefreshChats
            || RefreshEffects
            || CharacterNames.Count > 0
            || BackgroundNames.Count > 0;
    }

    internal sealed class AssetRefreshMarker
    {
        public int SchemaVersion { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public string ConfigIniPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
        public AssetRefreshStateSnapshot? AssetState { get; set; }
    }

    internal sealed class AssetRefreshStateSnapshot
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public Dictionary<string, AssetTrackedFolderState> Characters { get; set; } =
            new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, AssetTrackedFolderState> Backgrounds { get; set; } =
            new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase);
        public string BlipsSignature { get; set; } = string.Empty;
        public string ChatsSignature { get; set; } = string.Empty;
        public string EffectsSignature { get; set; } = string.Empty;
    }

    internal sealed class AssetTrackedFolderState
    {
        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }
}
