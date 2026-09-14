using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;

namespace OceanyaClient.Features.Assets
{
    /// <summary>
    /// Watches the mounted AO asset folders and folds any change into the asset caches in the background.
    /// </summary>
    /// <remarks>
    /// Oceanya used to learn about new assets only from an explicit "Refresh all assets" run or from the
    /// deferred startup scan, so dropping a character folder into the AO install mid-session did nothing
    /// until the user manually refreshed - and a large extraction that was still in flight during that
    /// refresh was simply missed. This watcher closes that gap: every physical mount is observed, changes
    /// are coalesced behind a quiet period (so a multi-file extraction is handled once, after it settles),
    /// and the resulting targeted plan is applied off the UI thread. No wait form, no user action.
    ///
    /// Only <see cref="Globals.PhysicalBaseFolders"/> is watched. The web asset mirror is deliberately
    /// excluded: it materializes files constantly while streaming and already has its own notification
    /// path, so watching it would produce a storm of pointless refreshes.
    /// </remarks>
    public sealed class LiveAssetWatcher : IDisposable
    {
        /// <summary>
        /// Quiet period after the last observed change before the pending plan is applied. Long enough that
        /// extracting an archive of a few hundred files lands as one refresh instead of one per file.
        /// </summary>
        private const int ChangeQuietPeriodMilliseconds = 1500;

        /// <summary>Upper bound on how long changes may keep deferring the apply, so a slow continuous
        /// copy still gets folded in periodically instead of waiting for the copy to finish entirely.</summary>
        private const int MaximumChangeDeferralMilliseconds = 15000;

        private static readonly object InstanceLock = new object();
        private static LiveAssetWatcher? current;

        private readonly object stateLock = new object();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly HashSet<string> pendingCharacterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingBackgroundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer applyTimer;

        private bool pendingBlips;
        private bool pendingChats;
        private bool pendingEffects;
        private bool pendingFullCharacterRefresh;
        private bool pendingFullBackgroundRefresh;
        private DateTime firstPendingChangeUtc = DateTime.MinValue;
        private bool applyInProgress;
        private bool disposed;

        private LiveAssetWatcher()
        {
            applyTimer = new Timer(_ => ApplyPendingChanges(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>The running watcher, or <c>null</c> when live asset watching is not active.</summary>
        public static LiveAssetWatcher? Current
        {
            get
            {
                lock (InstanceLock)
                {
                    return current;
                }
            }
        }

        /// <summary>
        /// Starts (or restarts) watching the currently configured mounts. Safe to call repeatedly; a second
        /// call rebuilds the watchers against the mounts that are configured now.
        /// </summary>
        public static void StartOrRestart()
        {
            lock (InstanceLock)
            {
                current?.Dispose();
                LiveAssetWatcher watcher = new LiveAssetWatcher();
                current = watcher;
                watcher.InstallWatchers();
            }
        }

        /// <summary>Stops watching and releases every watcher.</summary>
        public static void Stop()
        {
            lock (InstanceLock)
            {
                current?.Dispose();
                current = null;
            }
        }

        private void InstallWatchers()
        {
            foreach (string baseFolder in Globals.PhysicalBaseFolders)
            {
                if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
                {
                    continue;
                }

                TryInstallWatcher(baseFolder, "characters");
                TryInstallWatcher(baseFolder, "background");
                TryInstallWatcher(baseFolder, "misc");
                TryInstallWatcher(baseFolder, Path.Combine("sounds", "blips"));
            }

            CustomConsole.Info(
                $"[LIVE-ASSETS] Watching {watchers.Count} asset folder(s) for changes.",
                CustomConsole.LogCategory.System);
        }

        private void TryInstallWatcher(string baseFolder, string relativeFolder)
        {
            string watchedPath = Path.Combine(baseFolder, relativeFolder);
            if (!Directory.Exists(watchedPath))
            {
                return;
            }

            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(watchedPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };

                watcher.Created += (_, args) => OnChanged(baseFolder, args.FullPath);
                watcher.Changed += (_, args) => OnChanged(baseFolder, args.FullPath);
                watcher.Deleted += (_, args) => OnChanged(baseFolder, args.FullPath);
                watcher.Renamed += (_, args) =>
                {
                    OnChanged(baseFolder, args.OldFullPath);
                    OnChanged(baseFolder, args.FullPath);
                };
                watcher.Error += (_, args) => OnWatcherError(watchedPath, args.GetException());
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning(
                    $"[LIVE-ASSETS] Could not watch '{watchedPath}' for changes.",
                    ex,
                    CustomConsole.LogCategory.System);
            }
        }

        /// <summary>
        /// Handles watcher buffer overflow by falling back to a full refresh: the individual events are
        /// gone, so the only correct response is to rescan everything.
        /// </summary>
        private void OnWatcherError(string watchedPath, Exception? exception)
        {
            CustomConsole.Warning(
                $"[LIVE-ASSETS] Change notifications for '{watchedPath}' overflowed; falling back to a full refresh.",
                exception,
                CustomConsole.LogCategory.System);

            lock (stateLock)
            {
                pendingFullCharacterRefresh = true;
                pendingFullBackgroundRefresh = true;
                pendingBlips = true;
                pendingChats = true;
                pendingEffects = true;
                ScheduleApplyLocked();
            }
        }

        private void OnChanged(string baseFolder, string fullPath)
        {
            if (disposed)
            {
                return;
            }

            AssetChangeTarget? target = ClassifyChange(baseFolder, fullPath);
            if (target == null)
            {
                return;
            }

            lock (stateLock)
            {
                switch (target.Category)
                {
                    case AssetChangeCategory.Character when string.IsNullOrEmpty(target.EntryName):
                        pendingFullCharacterRefresh = true;
                        break;
                    case AssetChangeCategory.Character:
                        pendingCharacterNames.Add(target.EntryName);
                        break;
                    case AssetChangeCategory.Background when string.IsNullOrEmpty(target.EntryName):
                        pendingFullBackgroundRefresh = true;
                        break;
                    case AssetChangeCategory.Background:
                        pendingBackgroundNames.Add(target.EntryName);
                        break;
                    case AssetChangeCategory.Blips:
                        pendingBlips = true;
                        break;
                    case AssetChangeCategory.Misc:
                        pendingChats = true;
                        pendingEffects = true;
                        break;
                }

                ScheduleApplyLocked();
            }
        }

        /// <summary>
        /// Maps a changed path to the asset it belongs to, or <c>null</c> when the change is irrelevant.
        /// </summary>
        internal static AssetChangeTarget? ClassifyChange(string baseFolder, string fullPath)
        {
            string relative;
            try
            {
                relative = Path.GetRelativePath(baseFolder, fullPath).Replace('\\', '/').Trim('/');
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            {
                return null;
            }

            string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            // Editor scratch directories and OS/shell junk must never trigger a refresh. (Integrity
            // reports and the character cache are written under AppData, not into the asset folders, so
            // a refresh cannot feed itself back in here.)
            if (segments.Any(IsIgnoredPathSegment))
            {
                return null;
            }

            string category = segments[0];
            if (string.Equals(category, "characters", StringComparison.OrdinalIgnoreCase))
            {
                return new AssetChangeTarget(
                    AssetChangeCategory.Character,
                    segments.Length >= 2 ? segments[1] : string.Empty);
            }

            if (string.Equals(category, "background", StringComparison.OrdinalIgnoreCase))
            {
                return new AssetChangeTarget(
                    AssetChangeCategory.Background,
                    segments.Length >= 2 ? segments[1] : string.Empty);
            }

            if (string.Equals(category, "sounds", StringComparison.OrdinalIgnoreCase)
                && segments.Length >= 2
                && string.Equals(segments[1], "blips", StringComparison.OrdinalIgnoreCase))
            {
                return new AssetChangeTarget(AssetChangeCategory.Blips, string.Empty);
            }

            if (string.Equals(category, "misc", StringComparison.OrdinalIgnoreCase))
            {
                return new AssetChangeTarget(AssetChangeCategory.Misc, string.Empty);
            }

            return null;
        }

        private static bool IsIgnoredPathSegment(string segment)
        {
            return segment.StartsWith(".oceanya_character_edit_staging_", StringComparison.OrdinalIgnoreCase)
                || segment.Contains(".oceanya_edit_backup_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "desktop.ini", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "thumbs.db", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, ".oceanya_folder_icon.ico", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Restarts the quiet-period timer, capped by <see cref="MaximumChangeDeferralMilliseconds"/>.</summary>
        private void ScheduleApplyLocked()
        {
            if (firstPendingChangeUtc == DateTime.MinValue)
            {
                firstPendingChangeUtc = DateTime.UtcNow;
            }

            double elapsedMs = (DateTime.UtcNow - firstPendingChangeUtc).TotalMilliseconds;
            int delay = elapsedMs >= MaximumChangeDeferralMilliseconds ? 0 : ChangeQuietPeriodMilliseconds;
            try
            {
                applyTimer.Change(delay, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // Watcher was torn down while a change was in flight.
            }
        }

        private void ApplyPendingChanges()
        {
            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan();
            lock (stateLock)
            {
                if (disposed || applyInProgress)
                {
                    return;
                }

                if (!HasPendingWorkLocked())
                {
                    firstPendingChangeUtc = DateTime.MinValue;
                    return;
                }

                plan.RequiresFullCharacterRefresh = pendingFullCharacterRefresh;
                plan.RequiresFullBackgroundRefresh = pendingFullBackgroundRefresh;
                plan.RefreshBlips = pendingBlips;
                plan.RefreshChats = pendingChats;
                plan.RefreshEffects = pendingEffects;
                foreach (string characterName in pendingCharacterNames)
                {
                    plan.CharacterNames.Add(characterName);
                }

                foreach (string backgroundName in pendingBackgroundNames)
                {
                    plan.BackgroundNames.Add(backgroundName);
                }

                pendingFullCharacterRefresh = false;
                pendingFullBackgroundRefresh = false;
                pendingBlips = false;
                pendingChats = false;
                pendingEffects = false;
                pendingCharacterNames.Clear();
                pendingBackgroundNames.Clear();
                firstPendingChangeUtc = DateTime.MinValue;
                applyInProgress = true;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    CustomConsole.Info(
                        "[LIVE-ASSETS] Applying detected changes: "
                        + DescribePlan(plan),
                        CustomConsole.LogCategory.System);
                    await ClientAssetRefreshService.RefreshTargetedAssetsInBackgroundAsync(plan);
                }
                catch (Exception ex)
                {
                    CustomConsole.Error("[LIVE-ASSETS] Applying detected asset changes failed.", ex, CustomConsole.LogCategory.System);
                }
                finally
                {
                    bool rescheduleNeeded;
                    lock (stateLock)
                    {
                        applyInProgress = false;
                        rescheduleNeeded = !disposed && HasPendingWorkLocked();
                        if (rescheduleNeeded)
                        {
                            ScheduleApplyLocked();
                        }
                    }
                }
            });
        }

        private bool HasPendingWorkLocked()
        {
            return pendingFullCharacterRefresh
                || pendingFullBackgroundRefresh
                || pendingBlips
                || pendingChats
                || pendingEffects
                || pendingCharacterNames.Count > 0
                || pendingBackgroundNames.Count > 0;
        }

        private static string DescribePlan(TargetedAssetRefreshPlan plan)
        {
            List<string> parts = new List<string>();
            if (plan.RequiresFullCharacterRefresh)
            {
                parts.Add("all characters");
            }
            else if (plan.CharacterNames.Count > 0)
            {
                parts.Add($"{plan.CharacterNames.Count} character(s): {string.Join(", ", plan.CharacterNames.Take(5))}");
            }

            if (plan.RequiresFullBackgroundRefresh)
            {
                parts.Add("all backgrounds");
            }
            else if (plan.BackgroundNames.Count > 0)
            {
                parts.Add($"{plan.BackgroundNames.Count} background(s): {string.Join(", ", plan.BackgroundNames.Take(5))}");
            }

            if (plan.RefreshBlips)
            {
                parts.Add("blips");
            }

            if (plan.RefreshChats)
            {
                parts.Add("chat profiles");
            }

            if (plan.RefreshEffects)
            {
                parts.Add("effects");
            }

            return parts.Count == 0 ? "(nothing)" : string.Join(" | ", parts);
        }

        public void Dispose()
        {
            lock (stateLock)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
            }

            foreach (FileSystemWatcher watcher in watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch
                {
                    // Best-effort teardown.
                }
            }

            watchers.Clear();
            applyTimer.Dispose();
        }
    }

    /// <summary>Which asset cache a detected filesystem change belongs to.</summary>
    internal enum AssetChangeCategory
    {
        Character,
        Background,
        Blips,
        Misc
    }

    /// <summary>One classified filesystem change.</summary>
    /// <param name="Category">Which asset cache the change belongs to.</param>
    /// <param name="EntryName">
    /// The character/background folder name, or empty when the change is at the category root and the whole
    /// category has to be rescanned.
    /// </param>
    internal sealed record AssetChangeTarget(AssetChangeCategory Category, string EntryName);
}
