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

        private readonly GoogleDriveSyncService syncService;
        private readonly GoogleDriveRemoteChangeTracker remoteChangeTracker;
        private readonly GoogleDriveConnectionRuntimeStateStore runtimeStateStore;
        private readonly FileHivemindBackgroundLogStore backgroundLogStore;
        private readonly Dictionary<string, ConnectionMonitorState> connectionStates =
            new Dictionary<string, ConnectionMonitorState>(StringComparer.OrdinalIgnoreCase);

        public FileHivemindBackgroundSyncAgent(
            GoogleDriveSyncService? syncService = null,
            GoogleDriveRemoteChangeTracker? remoteChangeTracker = null,
            GoogleDriveConnectionRuntimeStateStore? runtimeStateStore = null,
            FileHivemindBackgroundLogStore? backgroundLogStore = null)
        {
            this.syncService = syncService ?? new GoogleDriveSyncService();
            this.remoteChangeTracker = remoteChangeTracker ?? new GoogleDriveRemoteChangeTracker();
            this.runtimeStateStore = runtimeStateStore ?? new GoogleDriveConnectionRuntimeStateStore();
            this.backgroundLogStore = backgroundLogStore ?? new FileHivemindBackgroundLogStore();
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

            DateTimeOffset? pendingPushUtc = state.GetPendingLocalPushUtc();
            if (pendingPushUtc.HasValue)
            {
                if (!HasUnsyncedLocalChanges(state))
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

            try
            {
                state.ClearPendingLocalPush();
                state.IgnoreLocalChangesUntilUtc = DateTimeOffset.MaxValue;
                EnsureMountedIfPossible(configIniPath, state.Connection);
                LogAgentMessage("Action", state.Connection, $"Pull started ({reason}).");
                GoogleDriveSyncSummary summary = await syncService.PullFromDriveAsync(
                    state.Connection.GoogleDrive,
                    _ => { },
                    cancellationToken);
                RefreshMountedAssetsIfPossible(configIniPath, state.Connection, summary.LocalChanges);
                await CaptureRuntimeStateAsync(state, cancellationToken, summary.KnownRemoteItemIds);
                state.RequiresInitialPull = false;
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + remotePollInterval;
                state.IgnoreLocalChangesUntilUtc = DateTimeOffset.UtcNow + PullWatcherIgnoreDuration;
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

            try
            {
                state.ClearPendingLocalPush();
                EnsureMountedIfPossible(configIniPath, state.Connection);
                LogAgentMessage("Action", state.Connection, $"Push started ({reason}).");
                GoogleDriveSyncSummary summary = await syncService.PushLocalFolderAsync(
                    state.Connection.GoogleDrive,
                    _ => { },
                    cancellationToken);
                RefreshMountedAssetsIfPossible(configIniPath, state.Connection, summary.LocalChanges);
                await CaptureRuntimeStateAsync(state, cancellationToken, summary.KnownRemoteItemIds);
                state.RequiresInitialPull = false;
                state.NextRemotePollUtc = DateTimeOffset.UtcNow + remotePollInterval;
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
    }
}
