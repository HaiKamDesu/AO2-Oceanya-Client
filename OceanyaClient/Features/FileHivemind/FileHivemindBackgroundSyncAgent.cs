using Common;
using OceanyaClient.Features.GoogleDriveSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OceanyaClient.Features.FileHivemind
{
    public sealed class FileHivemindBackgroundSyncAgent : IDisposable
    {
        private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LocalChangeDebounce = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan PullWatcherIgnoreDuration = TimeSpan.FromSeconds(3);
        private const int DefaultRemotePollIntervalSeconds = 20;
        private const int DestructiveDeletionProtectionMinimumBaselineFiles = 5;
        private const int DestructiveDeletionProtectionMinimumBaselineItems = 12;

        private readonly GoogleDriveSyncService syncService;
        private readonly GoogleDriveRemoteChangeTracker remoteChangeTracker;
        private readonly GoogleDriveConnectionRuntimeStateStore runtimeStateStore;
        private readonly FileHivemindBackgroundLogStore backgroundLogStore;
        private readonly IFileHivemindAgentNotifier backgroundNotifier;
        private readonly Dictionary<string, ConnectionMonitorState> connectionStates =
            new Dictionary<string, ConnectionMonitorState>(StringComparer.OrdinalIgnoreCase);

        public FileHivemindBackgroundSyncAgent(
            GoogleDriveSyncService? syncService = null,
            GoogleDriveRemoteChangeTracker? remoteChangeTracker = null,
            GoogleDriveConnectionRuntimeStateStore? runtimeStateStore = null,
            FileHivemindBackgroundLogStore? backgroundLogStore = null,
            IFileHivemindAgentNotifier? backgroundNotifier = null)
        {
            this.syncService = syncService ?? new GoogleDriveSyncService();
            this.remoteChangeTracker = remoteChangeTracker ?? new GoogleDriveRemoteChangeTracker();
            this.runtimeStateStore = runtimeStateStore ?? new GoogleDriveConnectionRuntimeStateStore();
            this.backgroundLogStore = backgroundLogStore ?? new FileHivemindBackgroundLogStore();
            this.backgroundNotifier = backgroundNotifier ?? new NullFileHivemindAgentNotifier();
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Mutex? agentMutex = TryAcquireAgentMutex();
            if (agentMutex == null)
            {
                LogInfo("The Oceanyan File Hivemind background agent is already running.");
                return;
            }

            LogInfo("The Oceanyan File Hivemind background agent started.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    SaveData snapshot = SaveFile.LoadSnapshotFromDisk();
                    if (!FileHivemindBackgroundAgentLauncher.ShouldRunForSettings(snapshot.FileHivemind))
                    {
                        LogInfo("The Oceanyan File Hivemind background agent stopped because auto-start sync is disabled or no eligible connections remain.");
                        return;
                    }

                    ReconcileConnections(snapshot.FileHivemind);

                    foreach (ConnectionMonitorState state in connectionStates.Values.ToList())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await ProcessConnectionAsync(state, snapshot.ConfigIniPath, snapshot.FileHivemind, cancellationToken);
                    }

                    await Task.Delay(LoopDelay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            finally
            {
                try
                {
                    agentMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                agentMutex.Dispose();
                backgroundNotifier.ClearStatusText();
                Dispose();
                LogInfo("The Oceanyan File Hivemind background agent stopped.");
            }
        }

        public void Dispose()
        {
            foreach (ConnectionMonitorState state in connectionStates.Values)
            {
                DisposeWatcher(state);
            }

            connectionStates.Clear();
        }

        private static Mutex? TryAcquireAgentMutex()
        {
            Mutex mutex = new Mutex(initiallyOwned: false, FileHivemindBackgroundAgentCommandLine.AgentMutexName);
            bool acquired = false;

            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return mutex;
        }

        private void ReconcileConnections(FileHivemindSettings settings)
        {
            HashSet<string> desiredConnectionIds = new HashSet<string>(
                (settings.Connections ?? new List<FileHivemindConnectionProfile>())
                    .Where(FileHivemindBackgroundAgentLauncher.IsEligibleConnection)
                    .Select(connection => connection.Id),
                StringComparer.OrdinalIgnoreCase);

            foreach (string staleConnectionId in connectionStates.Keys.Except(desiredConnectionIds, StringComparer.OrdinalIgnoreCase).ToList())
            {
                ConnectionMonitorState staleState = connectionStates[staleConnectionId];
                DisposeWatcher(staleState);
                connectionStates.Remove(staleConnectionId);
                runtimeStateStore.Delete(staleConnectionId);
            }

            foreach (FileHivemindConnectionProfile connection in (settings.Connections ?? new List<FileHivemindConnectionProfile>())
                .Where(FileHivemindBackgroundAgentLauncher.IsEligibleConnection))
            {
                if (!connectionStates.TryGetValue(connection.Id, out ConnectionMonitorState? state))
                {
                    state = new ConnectionMonitorState(connection);
                    state.Owner = this;
                    state.RequiresInitialPull = !LocalFolderHasAnyContent(connection.GoogleDrive.LocalFolderPath);
                    connectionStates[connection.Id] = state;
                }

                state.Owner = this;
                state.Connection = connection;
                EnsureWatcher(state);
            }
        }

        private async Task ProcessConnectionAsync(
            ConnectionMonitorState state,
            string configIniPath,
            FileHivemindSettings hivemindSettings,
            CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TimeSpan remotePollInterval = ResolveRemotePollInterval(hivemindSettings);
            if (state.RequiresInitialPull)
            {
                LogAgentMessage("Action", state.Connection, "Local mirror is empty. Starting initial pull.");
                await ExecutePullAsync(state, configIniPath, "initial sync", remotePollInterval, cancellationToken);
                return;
            }

            if (runtimeStateStore.Load(state.Connection.Id) == null)
            {
                LogAgentMessage("Info", state.Connection, "No background runtime state exists yet. Capturing baseline state.");
                await CaptureRuntimeStateOnlyAsync(state, remotePollInterval, cancellationToken);
                return;
            }

            LocalMirrorAssessment localMirrorAssessment = AssessLocalMirror(state);
            if (localMirrorAssessment.Kind == LocalMirrorAssessmentKind.SuspiciousMassDeletion)
            {
                state.ClearPendingLocalPush();

                if (!state.DestructiveDeletionProtectionActive
                    || !string.Equals(
                        state.LastDestructiveDeletionProtectionMessage,
                        localMirrorAssessment.Message,
                        StringComparison.Ordinal))
                {
                    LogAgentMessage(
                        "Error",
                        state.Connection,
                        localMirrorAssessment.Message);
                    backgroundNotifier.ShowNotification(
                        "Hivemind Safety Lock",
                        state.Connection.EffectiveDisplayName + ": " + localMirrorAssessment.Message,
                        FileHivemindAgentNotificationSeverity.Warning);
                }

                state.DestructiveDeletionProtectionActive = true;
                state.LastDestructiveDeletionProtectionMessage = localMirrorAssessment.Message;
                return;
            }

            if (state.DestructiveDeletionProtectionActive)
            {
                state.DestructiveDeletionProtectionActive = false;
                state.LastDestructiveDeletionProtectionMessage = string.Empty;
                LogAgentMessage(
                    "Info",
                    state.Connection,
                    "Local mirror safety lock cleared. Automatic syncing resumed.");
            }

            DateTimeOffset? pendingPushUtc = state.GetPendingLocalPushUtc();
            if (!pendingPushUtc.HasValue && localMirrorAssessment.Kind == LocalMirrorAssessmentKind.NormalChanges)
            {
                state.ScheduleLocalPush(LocalChangeDebounce);
                LogAgentMessage(
                    "Action",
                    state.Connection,
                    "Local mirror differs from the last synced state. Scheduling a push before any remote pull.");
                return;
            }

            if (pendingPushUtc.HasValue)
            {
                if (localMirrorAssessment.Kind == LocalMirrorAssessmentKind.NoChanges)
                {
                    state.ClearPendingLocalPush();
                    LogAgentMessage(
                        "Info",
                        state.Connection,
                        "Scheduled local push was cleared because the mirror matches the last synced state.");
                }
                else
                {
                    if (pendingPushUtc.Value <= now)
                    {
                        LogAgentMessage(
                            "Action",
                            state.Connection,
                            "Local mirror differs from the last synced state. Pushing changes to Drive.");
                        await ExecutePushAsync(state, configIniPath, "local mirror changes", remotePollInterval, cancellationToken);
                    }

                    return;
                }
            }

            if (state.NextRemotePollUtc <= now)
            {
                await PollRemoteChangesAsync(state, configIniPath, remotePollInterval, cancellationToken);
            }
        }

        private async Task PollRemoteChangesAsync(
            ConnectionMonitorState state,
            string configIniPath,
            TimeSpan remotePollInterval,
            CancellationToken cancellationToken)
        {
            GoogleDriveConnectionRuntimeState? runtimeState = runtimeStateStore.Load(state.Connection.Id);

            try
            {
                LogAgentMessage(
                    "Action",
                    state.Connection,
                    "Polling Google Drive for remote changes.");
                GoogleDriveRemoteChangeCheckResult result = await remoteChangeTracker.CheckForRelevantChangesAsync(
                    state.Connection.Id,
                    state.Connection.GoogleDrive,
                    runtimeState,
                    cancellationToken);
                runtimeStateStore.Save(result.UpdatedState);
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + remotePollInterval;

                if (result.RequiresFullResync)
                {
                    LogAgentMessage(
                        "Warning",
                        state.Connection,
                        "The saved change token is no longer usable. Performing a full pull.");
                    await ExecutePullAsync(state, configIniPath, "change-token reset", remotePollInterval, cancellationToken);
                    return;
                }

                if (!result.HasRelevantChanges)
                {
                    LogAgentMessage(
                        "Info",
                        state.Connection,
                        "Remote poll found no relevant Google Drive changes.");
                    return;
                }

                LogAgentMessage(
                    "Action",
                    state.Connection,
                    "Remote poll detected relevant Google Drive changes. Pulling updates.");
                await ExecutePullAsync(state, configIniPath, "remote Google Drive changes", remotePollInterval, cancellationToken);
            }
            catch (Exception ex)
            {
                PersistRuntimeError(state.Connection.Id, runtimeState, ex.Message);
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + ErrorBackoff;
                LogWarning($"Hivemind remote change polling failed for '{state.Connection.EffectiveDisplayName}'.", ex);
                LogAgentMessage(
                    "Error",
                    state.Connection,
                    "Remote change polling failed. Backing off before retry: " + ex.Message);
            }
        }

        private async Task ExecutePullAsync(
            ConnectionMonitorState state,
            string configIniPath,
            string reason,
            TimeSpan remotePollInterval,
            CancellationToken cancellationToken)
        {
            using FileHivemindConnectionExecutionLock? executionLock =
                FileHivemindConnectionExecutionLock.TryAcquire(state.Connection.Id, TimeSpan.Zero);
            if (executionLock == null)
            {
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
                LogAgentMessage(
                    "Warning",
                    state.Connection,
                    "Pull skipped because another Oceanya process is already syncing this connection.");
                return;
            }

            bool showProgressToast = false;
            try
            {
                state.ClearPendingLocalPush();
                state.IgnoreLocalChangesUntilUtc = DateTimeOffset.MaxValue;
                EnsureMountedIfPossible(configIniPath, state.Connection);
                showProgressToast = MaybeShowChangeDetectionNotification(state.Connection, reason, isPull: true);
                UpdateNotifierStatus(state.Connection, "Pulling updates from Google Drive...");
                LogAgentMessage("Action", state.Connection, $"Pull started ({reason}).");
                GoogleDriveSyncSummary summary = await syncService.PullFromDriveAsync(
                    state.Connection.GoogleDrive,
                    message => ReportOperationProgress(state, message, showProgressToast),
                    cancellationToken);
                RefreshMountedAssetsIfPossible(configIniPath, state.Connection, summary.LocalChanges);
                await CaptureRuntimeStateAsync(state, cancellationToken, summary.KnownRemoteItemIds);
                state.RequiresInitialPull = false;
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + remotePollInterval;
                state.IgnoreLocalChangesUntilUtc = DateTimeOffset.UtcNow + PullWatcherIgnoreDuration;
                if (showProgressToast)
                {
                    backgroundNotifier.CloseProgressNotification(state.Connection.Id);
                }
                backgroundNotifier.ShowNotification(
                    state.Connection.EffectiveDisplayName,
                    BuildCompletionNotificationMessage(summary, isPull: true),
                    FileHivemindAgentNotificationSeverity.Success);
                backgroundNotifier.ClearStatusText();
                LogAgentMessage(
                    "Success",
                    state.Connection,
                    $"Pull finished. Downloaded {summary.FilesDownloaded}, deleted {summary.LocalFilesDeleted + summary.LocalDirectoriesDeleted} local items.");
            }
            catch (Exception ex)
            {
                PersistRuntimeError(state.Connection.Id, runtimeStateStore.Load(state.Connection.Id), ex.Message);
                state.RequiresInitialPull = true;
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + ErrorBackoff;
                state.IgnoreLocalChangesUntilUtc = DateTimeOffset.UtcNow + PullWatcherIgnoreDuration;
                if (showProgressToast)
                {
                    backgroundNotifier.CloseProgressNotification(state.Connection.Id);
                }
                backgroundNotifier.ShowNotification(
                    "Hivemind Pull Failed",
                    state.Connection.EffectiveDisplayName + ": " + ex.Message,
                    FileHivemindAgentNotificationSeverity.Error);
                backgroundNotifier.ClearStatusText();
                LogWarning($"Hivemind pull failed for '{state.Connection.EffectiveDisplayName}'.", ex);
                LogAgentMessage("Error", state.Connection, "Pull failed: " + ex.Message);
            }
        }

        private async Task ExecutePushAsync(
            ConnectionMonitorState state,
            string configIniPath,
            string reason,
            TimeSpan remotePollInterval,
            CancellationToken cancellationToken)
        {
            using FileHivemindConnectionExecutionLock? executionLock =
                FileHivemindConnectionExecutionLock.TryAcquire(state.Connection.Id, TimeSpan.Zero);
            if (executionLock == null)
            {
                state.ScheduleLocalPush(LocalChangeDebounce);
                LogAgentMessage(
                    "Warning",
                    state.Connection,
                    "Push skipped because another Oceanya process is already syncing this connection. Will retry.");
                return;
            }

            bool showProgressToast = false;
            try
            {
                state.ClearPendingLocalPush();
                EnsureMountedIfPossible(configIniPath, state.Connection);
                showProgressToast = MaybeShowChangeDetectionNotification(state.Connection, reason, isPull: false);
                UpdateNotifierStatus(state.Connection, "Pushing local changes to Google Drive...");
                LogAgentMessage("Action", state.Connection, $"Push started ({reason}).");
                GoogleDriveSyncSummary summary = await syncService.PushLocalFolderAsync(
                    state.Connection.GoogleDrive,
                    message => ReportOperationProgress(state, message, showProgressToast),
                    cancellationToken);
                RefreshMountedAssetsIfPossible(configIniPath, state.Connection, summary.LocalChanges);
                await CaptureRuntimeStateAsync(state, cancellationToken, summary.KnownRemoteItemIds);
                state.RequiresInitialPull = false;
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + remotePollInterval;
                if (showProgressToast)
                {
                    backgroundNotifier.CloseProgressNotification(state.Connection.Id);
                }
                backgroundNotifier.ShowNotification(
                    state.Connection.EffectiveDisplayName,
                    BuildCompletionNotificationMessage(summary, isPull: false),
                    FileHivemindAgentNotificationSeverity.Success);
                backgroundNotifier.ClearStatusText();
                LogAgentMessage(
                    "Success",
                    state.Connection,
                    $"Push finished. Uploaded {summary.FilesUploaded}, deleted {summary.RemoteFilesDeleted + summary.RemoteDirectoriesDeleted} remote items.");
            }
            catch (Exception ex)
            {
                PersistRuntimeError(state.Connection.Id, runtimeStateStore.Load(state.Connection.Id), ex.Message);
                state.ScheduleLocalPush(ErrorBackoff);
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + ErrorBackoff;
                if (showProgressToast)
                {
                    backgroundNotifier.CloseProgressNotification(state.Connection.Id);
                }
                backgroundNotifier.ShowNotification(
                    "Hivemind Publish Failed",
                    state.Connection.EffectiveDisplayName + ": " + ex.Message,
                    FileHivemindAgentNotificationSeverity.Error);
                backgroundNotifier.ClearStatusText();
                LogWarning($"Hivemind push failed for '{state.Connection.EffectiveDisplayName}'.", ex);
                LogAgentMessage("Error", state.Connection, "Push failed: " + ex.Message);
            }
        }

        private async Task CaptureRuntimeStateOnlyAsync(
            ConnectionMonitorState state,
            TimeSpan remotePollInterval,
            CancellationToken cancellationToken)
        {
            try
            {
                await CaptureRuntimeStateAsync(state, cancellationToken);
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + remotePollInterval;
            }
            catch (Exception ex)
            {
                PersistRuntimeError(state.Connection.Id, runtimeStateStore.Load(state.Connection.Id), ex.Message);
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + ErrorBackoff;
                LogWarning($"Hivemind runtime-state capture failed for '{state.Connection.EffectiveDisplayName}'.", ex);
                LogAgentMessage("Warning", state.Connection, "Runtime state capture failed: " + ex.Message);
            }
        }

        private async Task CaptureRuntimeStateAsync(
            ConnectionMonitorState state,
            CancellationToken cancellationToken,
            IEnumerable<string>? additionalKnownItemIds = null)
        {
            GoogleDriveConnectionRuntimeState runtimeState = await remoteChangeTracker.CaptureRuntimeStateAsync(
                state.Connection.Id,
                state.Connection.GoogleDrive,
                cancellationToken);
            runtimeState = GoogleDriveRemoteChangeTracker.MergeKnownRemoteItemIds(runtimeState, additionalKnownItemIds);
            runtimeState.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
            runtimeState.LastErrorMessage = string.Empty;
            runtimeStateStore.Save(runtimeState);
        }

        private bool HasUnsyncedLocalChanges(ConnectionMonitorState state)
        {
            GoogleDriveConnectionRuntimeState? runtimeState = runtimeStateStore.Load(state.Connection.Id);
            return GoogleDriveLocalMirrorStateSupport.HasDifferences(
                runtimeState?.LocalMirrorState,
                state.Connection.GoogleDrive.LocalFolderPath);
        }

        private LocalMirrorAssessment AssessLocalMirror(ConnectionMonitorState state)
        {
            GoogleDriveConnectionRuntimeState? runtimeState = runtimeStateStore.Load(state.Connection.Id);
            GoogleDriveLocalMirrorState baseline = GoogleDriveLocalMirrorStateSupport.Normalize(runtimeState?.LocalMirrorState);
            GoogleDriveLocalMirrorState current;

            try
            {
                current = GoogleDriveLocalMirrorStateSupport.Capture(state.Connection.GoogleDrive.LocalFolderPath);
            }
            catch
            {
                return new LocalMirrorAssessment(LocalMirrorAssessmentKind.NormalChanges, string.Empty);
            }

            if (!GoogleDriveLocalMirrorStateSupport.HasDifferences(baseline, current))
            {
                return new LocalMirrorAssessment(LocalMirrorAssessmentKind.NoChanges, string.Empty);
            }

            if (TryGetSuspiciousMassDeletionMessage(state.Connection, baseline, current, out string message))
            {
                return new LocalMirrorAssessment(LocalMirrorAssessmentKind.SuspiciousMassDeletion, message);
            }

            return new LocalMirrorAssessment(LocalMirrorAssessmentKind.NormalChanges, string.Empty);
        }

        private static bool TryGetSuspiciousMassDeletionMessage(
            FileHivemindConnectionProfile connection,
            GoogleDriveLocalMirrorState baseline,
            GoogleDriveLocalMirrorState current,
            out string message)
        {
            message = string.Empty;
            GoogleDriveLocalMirrorState normalizedBaseline = GoogleDriveLocalMirrorStateSupport.Normalize(baseline);
            GoogleDriveLocalMirrorState normalizedCurrent = GoogleDriveLocalMirrorStateSupport.Normalize(current);

            int baselineFileCount = normalizedBaseline.Files.Count;
            int baselineItemCount = baselineFileCount + normalizedBaseline.DirectoryPaths.Count;
            if (baselineFileCount < DestructiveDeletionProtectionMinimumBaselineFiles
                && baselineItemCount < DestructiveDeletionProtectionMinimumBaselineItems)
            {
                return false;
            }

            string localFolderPath = connection.GoogleDrive.LocalFolderPath?.Trim() ?? string.Empty;
            bool localFolderMissing = string.IsNullOrWhiteSpace(localFolderPath) || !Directory.Exists(localFolderPath);
            int currentFileCount = normalizedCurrent.Files.Count;
            int currentItemCount = currentFileCount + normalizedCurrent.DirectoryPaths.Count;

            if (localFolderMissing)
            {
                message =
                    "Automatic push was blocked because the local mirror folder is missing. "
                    + "This looks like a destructive local delete, so Oceanya will not propagate it to Drive automatically.";
                return true;
            }

            if (currentItemCount == 0)
            {
                message =
                    "Automatic push was blocked because the local mirror became empty even though it previously contained synced files. "
                    + "This looks like a destructive local delete, so Oceanya will not propagate it to Drive automatically.";
                return true;
            }

            int removedFiles = Math.Max(0, baselineFileCount - currentFileCount);
            int removedItems = Math.Max(0, baselineItemCount - currentItemCount);
            bool removedAlmostEverything = removedFiles >= baselineFileCount * 9 / 10
                && removedItems >= baselineItemCount * 9 / 10;
            bool leftVeryLittle = currentFileCount <= Math.Max(1, baselineFileCount / 10)
                && currentItemCount <= Math.Max(2, baselineItemCount / 10);
            if (removedAlmostEverything && leftVeryLittle)
            {
                message =
                    "Automatic push was blocked because almost the entire local mirror disappeared at once. "
                    + "This looks like a destructive local delete, so Oceanya will not propagate it to Drive automatically.";
                return true;
            }

            return false;
        }

        private void PersistRuntimeError(
            string connectionId,
            GoogleDriveConnectionRuntimeState? existingState,
            string message)
        {
            GoogleDriveConnectionRuntimeState state = GoogleDriveConnectionRuntimeStateStore.Normalize(existingState
                ?? new GoogleDriveConnectionRuntimeState
                {
                    ConnectionId = connectionId
                });
            state.ConnectionId = connectionId?.Trim() ?? string.Empty;
            state.LastErrorMessage = message?.Trim() ?? string.Empty;
            runtimeStateStore.Save(state);
        }

        private void EnsureMountedIfPossible(string configIniPath, FileHivemindConnectionProfile connection)
        {
            if (!connection.GoogleDrive.AutoAddMountPath)
            {
                return;
            }

            string trimmedConfigPath = configIniPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedConfigPath) || !File.Exists(trimmedConfigPath))
            {
                return;
            }

            GoogleDriveClientAssetIntegration.EnsureMounted(
                trimmedConfigPath,
                connection.GoogleDrive,
                connection.GoogleDrive.UseExistingMountPath,
                message => LogInfo("[Hivemind] " + message));
        }

        private void RefreshMountedAssetsIfPossible(
            string configIniPath,
            FileHivemindConnectionProfile connection,
            GoogleDriveSyncLocalChangeSet? localChanges)
        {
            string trimmedConfigPath = configIniPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedConfigPath) || !File.Exists(trimmedConfigPath))
            {
                return;
            }

            GoogleDriveClientAssetIntegration.RefreshMountedAssets(
                trimmedConfigPath,
                connection.GoogleDrive,
                localChanges,
                progress =>
                {
                    if (!string.IsNullOrWhiteSpace(progress))
                    {
                        LogInfo("[Hivemind] " + progress);
                    }
                });
        }

        private void EnsureWatcher(ConnectionMonitorState state)
        {
            string normalizedLocalPath = Path.GetFullPath(state.Connection.GoogleDrive.LocalFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(state.WatchedPath, normalizedLocalPath, StringComparison.OrdinalIgnoreCase))
            {
                DisposeWatcher(state);
                state.RequiresInitialPull = !LocalFolderHasAnyContent(normalizedLocalPath);
            }

            if (state.Watcher != null)
            {
                return;
            }

            Directory.CreateDirectory(normalizedLocalPath);
            FileSystemWatcher watcher = new FileSystemWatcher(normalizedLocalPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, eventArgs) => OnLocalMirrorChanged(state, eventArgs.FullPath);
            watcher.Created += (_, eventArgs) => OnLocalMirrorChanged(state, eventArgs.FullPath);
            watcher.Deleted += (_, eventArgs) => OnLocalMirrorChanged(state, eventArgs.FullPath);
            watcher.Renamed += (_, eventArgs) => OnLocalMirrorChanged(state, eventArgs.FullPath);
            watcher.Error += (_, _) =>
            {
                DisposeWatcher(state);
                state.ScheduleLocalPush(LocalChangeDebounce);
            };

            state.Watcher = watcher;
            state.WatchedPath = normalizedLocalPath;
        }

        private static void DisposeWatcher(ConnectionMonitorState state)
        {
            if (state.Watcher == null)
            {
                return;
            }

            try
            {
                state.Watcher.EnableRaisingEvents = false;
                state.Watcher.Dispose();
            }
            catch
            {
            }
            finally
            {
                state.Watcher = null;
                state.WatchedPath = string.Empty;
            }
        }

        private static void OnLocalMirrorChanged(ConnectionMonitorState state, string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            if (state.IgnoreLocalChangesUntilUtc > DateTimeOffset.UtcNow)
            {
                return;
            }

            state.ScheduleLocalPush(LocalChangeDebounce);
            state.Owner.LogAgentMessage(
                "Info",
                state.Connection,
                "Detected local mirror change. Waiting briefly before deciding whether to push.");
        }

        private static bool LocalFolderHasAnyContent(string localFolderPath)
        {
            string trimmedPath = localFolderPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedPath) || !Directory.Exists(trimmedPath))
            {
                return false;
            }

            try
            {
                return Directory.EnumerateFileSystemEntries(trimmedPath, "*", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return true;
            }
        }

        private void LogInfo(string message)
        {
            CustomConsole.Info(message);
            backgroundLogStore.Append(new FileHivemindBackgroundLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "Info",
                Message = message
            });
        }

        private void LogWarning(string message, Exception? exception = null)
        {
            CustomConsole.Warning(message, exception);
            backgroundLogStore.Append(new FileHivemindBackgroundLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "Warning",
                Message = exception == null ? message : message + " " + exception.Message
            });
        }

        private void LogAgentMessage(string level, FileHivemindConnectionProfile connection, string message)
        {
            backgroundLogStore.Append(new FileHivemindBackgroundLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = level,
                ConnectionId = connection.Id,
                ConnectionName = connection.EffectiveDisplayName,
                Message = message
            });
        }

        private void ReportOperationProgress(ConnectionMonitorState state, string? message, bool showProgressToast)
        {
            string trimmedMessage = message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedMessage))
            {
                return;
            }

            UpdateNotifierStatus(state.Connection, trimmedMessage);
            if (showProgressToast)
            {
                ParsedOperationProgress parsedProgress = ParseOperationProgress(trimmedMessage);
                backgroundNotifier.UpdateProgressNotification(
                    state.Connection.Id,
                    parsedProgress.Detail,
                    parsedProgress.ProgressFraction);
            }

            LogAgentMessage("Info", state.Connection, trimmedMessage);
        }

        private void UpdateNotifierStatus(FileHivemindConnectionProfile connection, string message)
        {
            string trimmedMessage = message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedMessage))
            {
                backgroundNotifier.ClearStatusText();
                return;
            }

            backgroundNotifier.SetStatusText(connection.EffectiveDisplayName + ": " + trimmedMessage);
        }

        private bool MaybeShowChangeDetectionNotification(
            FileHivemindConnectionProfile connection,
            string reason,
            bool isPull)
        {
            if (!ShouldNotifyActionStart(reason, isPull))
            {
                return false;
            }

            backgroundNotifier.ShowProgressNotification(
                connection.Id,
                connection.EffectiveDisplayName,
                BuildActionStartNotificationMessage(isPull),
                "Preparing sync...",
                null);
            return true;
        }

        internal static bool ShouldNotifyActionStart(string? reason, bool isPull)
        {
            string normalizedReason = reason?.Trim() ?? string.Empty;
            if (isPull)
            {
                return string.Equals(normalizedReason, "remote Google Drive changes", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(normalizedReason, "local mirror changes", StringComparison.OrdinalIgnoreCase);
        }

        internal static string BuildActionStartNotificationMessage(bool isPull)
        {
            return isPull
                ? "Detected a remote change, pulling from Drive..."
                : "Detected a local change, pushing to Drive...";
        }

        internal static ParsedOperationProgress ParseOperationProgress(string? message)
        {
            string trimmedMessage = message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedMessage))
            {
                return new ParsedOperationProgress(string.Empty, null);
            }

            double? fraction = TryExtractProgressFraction(trimmedMessage);
            return new ParsedOperationProgress(trimmedMessage, fraction);
        }

        internal static string BuildCompletionNotificationMessage(GoogleDriveSyncSummary summary, bool isPull)
        {
            GoogleDriveSyncSummary normalizedSummary = summary ?? new GoogleDriveSyncSummary();
            int transferredItems = isPull ? normalizedSummary.FilesDownloaded : normalizedSummary.FilesUploaded;
            int deletedItems = isPull
                ? normalizedSummary.LocalFilesDeleted + normalizedSummary.LocalDirectoriesDeleted
                : normalizedSummary.RemoteFilesDeleted + normalizedSummary.RemoteDirectoriesDeleted;

            if (transferredItems > 0 && deletedItems > 0)
            {
                return "Synced with Drive ("
                    + (isPull ? "Downloaded " : "Uploaded ")
                    + transferredItems
                    + " files, deleted "
                    + deletedItems
                    + " items)";
            }

            if (transferredItems > 0)
            {
                return "Synced with Drive ("
                    + (isPull ? "Downloaded " : "Uploaded ")
                    + transferredItems
                    + " files)";
            }

            if (deletedItems > 0)
            {
                return "Synced with Drive (Deleted " + deletedItems + " items)";
            }

            return "Synced with Drive";
        }

        private static double? TryExtractProgressFraction(string message)
        {
            string value = message?.Trim() ?? string.Empty;
            int slashIndex = value.IndexOf('/');
            if (slashIndex <= 0)
            {
                return null;
            }

            int leftEnd = slashIndex - 1;
            while (leftEnd >= 0 && char.IsDigit(value[leftEnd]))
            {
                leftEnd--;
            }

            int rightStart = slashIndex + 1;
            int rightEnd = rightStart;
            while (rightEnd < value.Length && char.IsDigit(value[rightEnd]))
            {
                rightEnd++;
            }

            if (!int.TryParse(value.Substring(leftEnd + 1, slashIndex - leftEnd - 1), out int current)
                || !int.TryParse(value.Substring(rightStart, rightEnd - rightStart), out int total)
                || total <= 0)
            {
                return null;
            }

            return Math.Clamp((double)current / total, 0d, 1d);
        }

        private static TimeSpan ResolveRemotePollInterval(FileHivemindSettings settings)
        {
            int seconds = settings?.RemotePollIntervalSeconds ?? DefaultRemotePollIntervalSeconds;
            seconds = Math.Clamp(seconds <= 0 ? DefaultRemotePollIntervalSeconds : seconds, 5, 3600);
            return TimeSpan.FromSeconds(seconds);
        }

        private sealed class ConnectionMonitorState
        {
            private readonly object stateLock = new object();
            private DateTimeOffset? pendingLocalPushUtc;

            public ConnectionMonitorState(FileHivemindConnectionProfile connection)
            {
                Connection = connection;
            }

            public FileHivemindBackgroundSyncAgent Owner { get; set; } = null!;
            public FileHivemindConnectionProfile Connection { get; set; }
            public FileSystemWatcher? Watcher { get; set; }
            public string WatchedPath { get; set; } = string.Empty;
            public bool RequiresInitialPull { get; set; } = true;
            public bool DestructiveDeletionProtectionActive { get; set; }
            public string LastDestructiveDeletionProtectionMessage { get; set; } = string.Empty;
            public DateTimeOffset NextRemotePollUtc { get; set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset IgnoreLocalChangesUntilUtc { get; set; }

            public void ScheduleLocalPush(TimeSpan delay)
            {
                lock (stateLock)
                {
                    pendingLocalPushUtc = DateTimeOffset.UtcNow + delay;
                }
            }

            public DateTimeOffset? GetPendingLocalPushUtc()
            {
                lock (stateLock)
                {
                    return pendingLocalPushUtc;
                }
            }

            public void ClearPendingLocalPush()
            {
                lock (stateLock)
                {
                    pendingLocalPushUtc = null;
                }
            }
        }

        private readonly struct LocalMirrorAssessment
        {
            public LocalMirrorAssessment(LocalMirrorAssessmentKind kind, string message)
            {
                Kind = kind;
                Message = message ?? string.Empty;
            }

            public LocalMirrorAssessmentKind Kind { get; }
            public string Message { get; }
        }

        private enum LocalMirrorAssessmentKind
        {
            NoChanges = 0,
            NormalChanges = 1,
            SuspiciousMassDeletion = 2
        }

        internal readonly record struct ParsedOperationProgress(string Detail, double? ProgressFraction);
    }
}
