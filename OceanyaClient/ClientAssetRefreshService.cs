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
using OceanyaClient.Features.Viewport;

namespace OceanyaClient
{
    /// <summary>
    /// Provides workflows for rebuilding client-side asset indexes.
    /// </summary>
    public static class ClientAssetRefreshService
    {
        private const int RefreshMarkerSchemaVersion = 1;

        /// <summary>
        /// Refreshes all supported client-side asset caches while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        public static async Task RefreshCharactersAndBackgroundsAsync(Window owner)
        {
            await WaitForm.ShowFormAsync("Refreshing character and background info...", owner);

            try
            {
                await Task.Run(() => RefreshAllAssets(subtitle => WaitForm.SetSubtitle(subtitle)));
            }
            finally
            {
                await WaitForm.CloseFormAsync();
            }
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
        /// Refreshes only the supplied asset scope while showing progress in <see cref="WaitForm"/>.
        /// </summary>
        internal static async Task RefreshTargetedAssetsAsync(Window owner, TargetedAssetRefreshPlan plan)
        {
            if (plan == null || !plan.HasAnyWork)
            {
                return;
            }

            await WaitForm.ShowFormAsync("Refreshing changed asset info...", owner);

            try
            {
                await Task.Run(() => RefreshAssets(plan, subtitle => WaitForm.SetSubtitle(subtitle)));
            }
            finally
            {
                await WaitForm.CloseFormAsync();
            }
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
                    Globals.BaseFolders ?? new List<string>());
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

            if (!string.Equals(marker.AppVersion, currentAppVersion, StringComparison.OrdinalIgnoreCase))
            {
                return "The Oceanya version changed since the last full asset refresh.";
            }

            bool configPathChanged = !string.Equals(
                NormalizePathForComparison(marker.ConfigIniPath),
                NormalizePathForComparison(currentConfigIniPath),
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
                return "The configured AO config.ini path changed since the last full asset refresh.";
            }

            return "The AO mount/base-folder configuration changed since the last full asset refresh.";
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
            Globals.UpdateConfigINI(Globals.PathToConfigINI);
            RefreshAllCharacters(progress);
            RefreshAllBackgrounds(progress);
            PrebakeAllBackgroundAnimations(progress);

            progress?.Invoke("Indexing blip files...");
            _ = BlipCatalog.Refresh();
            progress?.Invoke("Indexing chat profiles...");
            _ = ChatCatalog.Refresh();
            progress?.Invoke("Indexing effects folders...");
            _ = EffectsFolderCatalog.Refresh();

            PersistRefreshMarker(forceFullStateCapture: true);
        }

        private static void RefreshAssets(TargetedAssetRefreshPlan plan, Action<string>? progress)
        {
            if (plan == null || !plan.HasAnyWork)
            {
                return;
            }

            bool performedWork = false;

            if (plan.RequiresFullCharacterRefresh)
            {
                RefreshAllCharacters(progress);
                performedWork = true;
            }
            else
            {
                foreach (string characterName in plan.CharacterNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    RefreshCharacter(characterName, progress);
                    performedWork = true;
                }
            }

            if (plan.RequiresFullBackgroundRefresh)
            {
                RefreshAllBackgrounds(progress);
                PrebakeAllBackgroundAnimations(progress);
                performedWork = true;
            }
            else
            {
                foreach (string backgroundName in plan.BackgroundNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    RefreshBackground(backgroundName, progress);
                    PrebakeBackgroundAnimations(backgroundName, progress);
                    performedWork = true;
                }
            }

            if (plan.RefreshBlips)
            {
                progress?.Invoke("Indexing blip files...");
                _ = BlipCatalog.Refresh();
                performedWork = true;
            }

            if (plan.RefreshChats)
            {
                progress?.Invoke("Indexing chat profiles...");
                _ = ChatCatalog.Refresh();
                performedWork = true;
            }

            if (plan.RefreshEffects)
            {
                progress?.Invoke("Indexing effects folders...");
                _ = EffectsFolderCatalog.Refresh();
                performedWork = true;
            }

            if (performedWork)
            {
                PersistRefreshMarker(plan);
            }
        }

        private static void RefreshAllCharacters(Action<string>? progress)
        {
            CharacterFolder.RefreshCharacterList(
                onParsedCharacter: character =>
                {
                    progress?.Invoke("Parsed character: " + character.Name);
                },
                onParsedCharacterProgress: (character, currentIndex, totalCharacters) =>
                {
                    progress?.Invoke($"Parsed character ({currentIndex}/{totalCharacters}): {character.Name}");
                },
                onChangedMountPath: path =>
                {
                    progress?.Invoke("Changed mount path: " + path);
                });

            List<CharacterFolder> characters = CharacterFolder.FullList.ToList();
            int totalCharacters = characters.Count;
            if (totalCharacters == 0)
            {
                return;
            }

            int completedCharacters = 0;
            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = AssetRefreshParallelism.GetDegreeOfParallelism(totalCharacters)
            };

            Parallel.ForEach(characters, options, character =>
            {
                CharacterIntegrityVerifier.RunAndPersist(character);
                int verifiedCount = Interlocked.Increment(ref completedCharacters);
                progress?.Invoke($"Integrity verified {verifiedCount}/{totalCharacters}: {character.Name}");
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

        /// <summary>
        /// Decodes all position-variant animations for every known background and writes them to the
        /// disk animation cache.  After this runs once, the viewport can load any background position
        /// instantly from disk on every subsequent session without full decoder work.
        /// All paths across all backgrounds are collected first, deduplicated, then decoded in
        /// parallel — same pattern as character integrity verification, with no nested parallelism.
        /// </summary>
        private static void PrebakeAllBackgroundAnimations(Action<string>? progress)
        {
            List<string> backgroundNames = GetAllKnownBackgroundNames();
            if (backgroundNames.Count == 0)
            {
                return;
            }

            // Collect every unique animated asset path across all backgrounds and all position variants.
            // Deduplication avoids re-decoding files shared by multiple backgrounds.
            string[] positions = { string.Empty, "def", "hld", "jud", "hlp", "pro", "wit", "jur", "sea" };
            var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            progress?.Invoke($"Collecting animation assets for {backgroundNames.Count} backgrounds...");
            foreach (string bgName in backgroundNames)
            {
                foreach (string pos in positions)
                {
                    string? bgPath = AO2ViewportAssetResolver.ResolveBackgroundPlacement(bgName, pos).ImagePath;
                    if (!string.IsNullOrWhiteSpace(bgPath) && Ao2AnimationPreview.IsPotentialAnimatedPath(bgPath))
                    {
                        allPaths.Add(bgPath);
                    }

                    string? deskPath = AO2ViewportAssetResolver.ResolveDeskPlacement(bgName, pos).ImagePath;
                    if (!string.IsNullOrWhiteSpace(deskPath) && Ao2AnimationPreview.IsPotentialAnimatedPath(deskPath))
                    {
                        allPaths.Add(deskPath);
                    }
                }
            }

            List<string> pathList = allPaths.ToList();
            int total = pathList.Count;
            if (total == 0)
            {
                return;
            }

            int completed = 0;
            progress?.Invoke($"Pre-baking {total} animation assets across {backgroundNames.Count} backgrounds...");

            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = AssetRefreshParallelism.GetDegreeOfParallelism(total)
            };

            Parallel.ForEach(pathList, options, assetPath =>
            {
                DateTime lastWrite = File.Exists(assetPath) ? File.GetLastWriteTimeUtc(assetPath) : DateTime.MinValue;
                if (Ao2AnimationPreview.IsAnimationCached(assetPath, lastWrite))
                {
                    Interlocked.Increment(ref completed);
                    return;
                }

                Ao2AnimationPreview.TryCreateAnimationPlayer(
                    assetPath,
                    loop: true,
                    out IAnimationPlayer? player,
                    usePreviewLimits: false,
                    maxDimensionOverride: null);
                player?.Stop();

                int done = Interlocked.Increment(ref completed);
                progress?.Invoke($"Pre-baking animations ({done}/{total}): {Path.GetFileName(assetPath)}");
            });
        }

        private static void PrebakeBackgroundAnimations(string backgroundName, Action<string>? progress)
        {
            if (string.IsNullOrWhiteSpace(backgroundName))
            {
                return;
            }

            progress?.Invoke($"Pre-baking viewport animations: {backgroundName}");
            AO2ViewportAssetResolver.PrefetchBackground(backgroundName);
        }

        private static List<string> GetAllKnownBackgroundNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string bgFolder in Background.BackgroundFolders)
            {
                if (!Directory.Exists(bgFolder))
                {
                    continue;
                }

                try
                {
                    foreach (string dir in Directory.GetDirectories(bgFolder))
                    {
                        string name = Path.GetFileName(dir);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            names.Add(name);
                        }
                    }
                }
                catch
                {
                    // Non-fatal: skip inaccessible folders
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void RefreshCharacter(string characterName, Action<string>? progress)
        {
            string? existingDirectory = CharacterFolder.FullList
                .FirstOrDefault(character => string.Equals(character.Name, characterName, StringComparison.OrdinalIgnoreCase))
                ?.DirectoryPath;

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

                if (character != null)
                {
                    progress?.Invoke("Integrity verify: " + character.Name);
                    _ = CharacterIntegrityVerifier.RunAndPersist(character);
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
                    marker.BaseFolders = NormalizeFolderSequence(Globals.BaseFolders ?? new List<string>());
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

        private static string GetRefreshMarkerPath()
        {
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
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
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
                        DirectoryPath = NormalizePathForComparison(directoryPath),
                        Signature = ComputeDirectorySignature(directoryPath)
                    };
                }
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
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
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
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
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
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
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
                foreach (string childDirectory in Directory.EnumerateDirectories(normalizedDirectory, "*", SearchOption.AllDirectories)
                    .Select(path => NormalizeRelativePath(Path.GetRelativePath(normalizedDirectory, path)))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    AppendHashLine(hash, "D|" + childDirectory);
                }

                foreach (string filePath in Directory.EnumerateFiles(normalizedDirectory, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string relativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedDirectory, filePath));
                    if (GoogleDriveLocalSnapshotBuilder.IsReservedSupportFile(relativePath))
                    {
                        continue;
                    }

                    try
                    {
                        FileInfo info = new FileInfo(filePath);
                        AppendHashLine(
                            hash,
                            "F|" + relativePath + "|" + info.Length + "|" + File.GetLastWriteTimeUtc(filePath).Ticks);
                    }
                    catch (Exception ex)
                    {
                        AppendHashLine(hash, "E|" + relativePath + "|" + ex.GetType().Name);
                    }
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
