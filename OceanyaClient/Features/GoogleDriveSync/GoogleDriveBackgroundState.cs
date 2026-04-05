using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveConnectionRuntimeState
    {
        public int FormatVersion { get; set; } = 2;
        public string ConnectionId { get; set; } = string.Empty;
        public string RootFolderId { get; set; } = string.Empty;
        public string ChangePageToken { get; set; } = string.Empty;
        public List<string> KnownRemoteItemIds { get; set; } = new List<string>();
        public GoogleDriveLocalMirrorState LocalMirrorState { get; set; } = new GoogleDriveLocalMirrorState();
        public GoogleDriveLocalMirrorState RemoteMirrorState { get; set; } = new GoogleDriveLocalMirrorState();
        public bool HasRemoteMirrorState { get; set; }
        public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }
        public string LastErrorMessage { get; set; } = string.Empty;
        public string LastStatusLevel { get; set; } = string.Empty;
        public string LastStatusMessage { get; set; } = string.Empty;
        public DateTimeOffset? LastStatusUtc { get; set; }
    }

    public sealed class GoogleDriveRemoteChangeCheckResult
    {
        public bool HasRelevantChanges { get; set; }
        public bool RequiresFullResync { get; set; }
        public GoogleDriveConnectionRuntimeState UpdatedState { get; set; } = new GoogleDriveConnectionRuntimeState();
    }

    public sealed class GoogleDriveConnectionRuntimeStateStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string rootDirectory;

        public GoogleDriveConnectionRuntimeStateStore(string? rootDirectory = null)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OceanyaClient",
                    "google_drive_runtime")
                : rootDirectory;
        }

        public GoogleDriveConnectionRuntimeState? Load(string connectionId)
        {
            string filePath = GetFilePath(connectionId);
            return PersistentFileStoreSupport.RunWithPathMutex(
                filePath,
                () =>
                {
                    if (!File.Exists(filePath))
                    {
                        return null;
                    }

                    using FileStream stream = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    GoogleDriveConnectionRuntimeState? state =
                        JsonSerializer.Deserialize<GoogleDriveConnectionRuntimeState>(stream);
                    return state == null ? null : Normalize(state);
                });
        }

        public void Save(GoogleDriveConnectionRuntimeState state)
        {
            GoogleDriveConnectionRuntimeState normalized = Normalize(state);
            string filePath = GetFilePath(normalized.ConnectionId);
            PersistentFileStoreSupport.RunWithPathMutex(
                filePath,
                () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? rootDirectory);
                    string tempPath = filePath + ".tmp";
                    string json = JsonSerializer.Serialize(normalized, JsonOptions);
                    try
                    {
                        using FileStream stream = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        using StreamWriter writer = new StreamWriter(stream);
                        writer.Write(json);
                    }
                    catch
                    {
                        PersistentFileStoreSupport.DeleteFileIfPresent(tempPath);
                        throw;
                    }

                    try
                    {
                        PersistentFileStoreSupport.ReplaceFile(tempPath, filePath);
                    }
                    catch
                    {
                        PersistentFileStoreSupport.DeleteFileIfPresent(tempPath);
                        throw;
                    }
                });
        }

        public void Delete(string connectionId)
        {
            string filePath = GetFilePath(connectionId);
            PersistentFileStoreSupport.RunWithPathMutex(
                filePath,
                () => PersistentFileStoreSupport.DeleteFileIfPresent(filePath));
        }

        public string GetFilePath(string connectionId)
        {
            string trimmedConnectionId = connectionId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedConnectionId))
            {
                trimmedConnectionId = "default";
            }

            return Path.Combine(rootDirectory, trimmedConnectionId + ".json");
        }

        public static GoogleDriveConnectionRuntimeState Normalize(GoogleDriveConnectionRuntimeState state)
        {
            state.ConnectionId = state.ConnectionId?.Trim() ?? string.Empty;
            state.RootFolderId = state.RootFolderId?.Trim() ?? string.Empty;
            state.ChangePageToken = state.ChangePageToken?.Trim() ?? string.Empty;
            state.LocalMirrorState = GoogleDriveLocalMirrorStateSupport.Normalize(state.LocalMirrorState);
            state.RemoteMirrorState = GoogleDriveLocalMirrorStateSupport.Normalize(state.RemoteMirrorState);
            if (!state.HasRemoteMirrorState
                && (state.RemoteMirrorState.DirectoryPaths.Count > 0 || state.RemoteMirrorState.Files.Count > 0))
            {
                state.HasRemoteMirrorState = true;
            }
            state.LastErrorMessage = state.LastErrorMessage?.Trim() ?? string.Empty;
            state.LastStatusLevel = state.LastStatusLevel?.Trim() ?? string.Empty;
            state.LastStatusMessage = state.LastStatusMessage?.Trim() ?? string.Empty;
            state.KnownRemoteItemIds = (state.KnownRemoteItemIds ?? new List<string>())
                .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
                .Select(itemId => itemId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(itemId => itemId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return state;
        }
    }

    public sealed class GoogleDriveRemoteChangeTracker
    {
        private readonly GoogleDriveSessionFactory sessionFactory;

        public GoogleDriveRemoteChangeTracker(GoogleDriveSessionFactory? sessionFactory = null)
        {
            this.sessionFactory = sessionFactory ?? new GoogleDriveSessionFactory();
        }

        public async Task<GoogleDriveConnectionRuntimeState> CaptureRuntimeStateAsync(
            string connectionId,
            GoogleDriveSyncSettings settings,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            GoogleDriveConnectionRuntimeState state = await CaptureRuntimeStateAsync(
                client,
                connectionId,
                settings.RemoteFolderId,
                cancellationToken);
            state.LocalMirrorState = GoogleDriveLocalMirrorStateSupport.CaptureExact(settings.LocalFolderPath);
            state.HasRemoteMirrorState = true;
            return GoogleDriveConnectionRuntimeStateStore.Normalize(state);
        }

        public static GoogleDriveConnectionRuntimeState MergeKnownRemoteItemIds(
            GoogleDriveConnectionRuntimeState state,
            IEnumerable<string>? additionalKnownItemIds)
        {
            GoogleDriveConnectionRuntimeState normalized = GoogleDriveConnectionRuntimeStateStore.Normalize(state);
            foreach (string itemId in additionalKnownItemIds ?? Array.Empty<string>())
            {
                string trimmedItemId = itemId?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(trimmedItemId))
                {
                    normalized.KnownRemoteItemIds.Add(trimmedItemId);
                }
            }

            return GoogleDriveConnectionRuntimeStateStore.Normalize(normalized);
        }

        public async Task<GoogleDriveConnectionRuntimeState> CaptureRuntimeStateAsync(
            IGoogleDriveRemoteClient client,
            string connectionId,
            string rootFolderId,
            CancellationToken cancellationToken)
        {
            GoogleDriveSyncSnapshot snapshot = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(
                await client.GetSnapshotAsync(rootFolderId, cancellationToken));
            string changePageToken = await client.GetStartPageTokenAsync(cancellationToken);
            return BuildRuntimeState(connectionId, rootFolderId, snapshot, changePageToken);
        }

        public async Task<GoogleDriveLocalMirrorState> CaptureRemoteMirrorStateAsync(
            GoogleDriveSyncSettings settings,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            GoogleDriveSyncSnapshot snapshot = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(
                await client.GetSnapshotAsync(settings.RemoteFolderId, cancellationToken));
            return GoogleDriveLocalMirrorStateSupport.FromSnapshot(snapshot);
        }

        public async Task<GoogleDriveRemoteChangeCheckResult> CheckForRelevantChangesAsync(
            string connectionId,
            GoogleDriveSyncSettings settings,
            GoogleDriveConnectionRuntimeState? existingState,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await CheckForRelevantChangesAsync(
                client,
                connectionId,
                settings.RemoteFolderId,
                existingState,
                cancellationToken);
        }

        public async Task<GoogleDriveRemoteChangeCheckResult> CheckForRelevantChangesAsync(
            IGoogleDriveRemoteClient client,
            string connectionId,
            string rootFolderId,
            GoogleDriveConnectionRuntimeState? existingState,
            CancellationToken cancellationToken)
        {
            GoogleDriveConnectionRuntimeState state = GoogleDriveConnectionRuntimeStateStore.Normalize(existingState
                ?? new GoogleDriveConnectionRuntimeState
                {
                    ConnectionId = connectionId,
                    RootFolderId = rootFolderId
                });
            state.ConnectionId = connectionId?.Trim() ?? string.Empty;
            state.RootFolderId = rootFolderId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(state.RootFolderId)
                || !string.Equals(state.RootFolderId, rootFolderId?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(state.ChangePageToken))
            {
                state.RootFolderId = rootFolderId?.Trim() ?? string.Empty;
                state.ChangePageToken = string.Empty;
                state.KnownRemoteItemIds = new List<string>();
                return new GoogleDriveRemoteChangeCheckResult
                {
                    RequiresFullResync = true,
                    UpdatedState = state
                };
            }

            try
            {
                string currentPageToken = state.ChangePageToken;
                bool hasRelevantChanges = false;
                HashSet<string> knownIds = new HashSet<string>(
                    (state.KnownRemoteItemIds ?? new List<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                while (!string.IsNullOrWhiteSpace(currentPageToken))
                {
                    GoogleDriveChangePage page = await client.GetChangesAsync(currentPageToken, cancellationToken);
                    if ((page.Changes ?? new List<GoogleDriveChangeEntry>()).Any(change =>
                            IsRelevantChange(rootFolderId, knownIds, change)))
                    {
                        hasRelevantChanges = true;
                    }

                    if (string.IsNullOrWhiteSpace(page.NextPageToken))
                    {
                        state.ChangePageToken = string.IsNullOrWhiteSpace(page.NewStartPageToken)
                            ? currentPageToken
                            : page.NewStartPageToken.Trim();
                        break;
                    }

                    currentPageToken = page.NextPageToken.Trim();
                }

                return new GoogleDriveRemoteChangeCheckResult
                {
                    HasRelevantChanges = hasRelevantChanges,
                    UpdatedState = state
                };
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                state.ChangePageToken = string.Empty;
                state.KnownRemoteItemIds = new List<string>();
                return new GoogleDriveRemoteChangeCheckResult
                {
                    RequiresFullResync = true,
                    UpdatedState = state
                };
            }
        }

        public static GoogleDriveConnectionRuntimeState BuildRuntimeState(
            string connectionId,
            string rootFolderId,
            GoogleDriveSyncSnapshot snapshot,
            string changePageToken)
        {
            GoogleDriveSyncSnapshot filteredSnapshot = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(snapshot);
            List<string> knownItemIds = new List<string>();
            if (!string.IsNullOrWhiteSpace(rootFolderId))
            {
                knownItemIds.Add(rootFolderId.Trim());
            }

            knownItemIds.AddRange(filteredSnapshot.Folders.Values
                .Select(folder => folder.ItemId?.Trim() ?? string.Empty)
                .Where(itemId => !string.IsNullOrWhiteSpace(itemId)));
            knownItemIds.AddRange(filteredSnapshot.Files.Values
                .Select(file => file.ItemId?.Trim() ?? string.Empty)
                .Where(itemId => !string.IsNullOrWhiteSpace(itemId)));

            return GoogleDriveConnectionRuntimeStateStore.Normalize(new GoogleDriveConnectionRuntimeState
            {
                ConnectionId = connectionId?.Trim() ?? string.Empty,
                RootFolderId = rootFolderId?.Trim() ?? string.Empty,
                ChangePageToken = changePageToken?.Trim() ?? string.Empty,
                KnownRemoteItemIds = knownItemIds,
                RemoteMirrorState = GoogleDriveLocalMirrorStateSupport.FromSnapshot(filteredSnapshot),
                HasRemoteMirrorState = true
            });
        }

        public static bool IsRelevantChange(
            string rootFolderId,
            IEnumerable<string>? knownRemoteItemIds,
            GoogleDriveChangeEntry? change)
        {
            if (change == null)
            {
                return false;
            }

            string itemId = change.ItemId?.Trim() ?? string.Empty;
            string trimmedRootFolderId = rootFolderId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            HashSet<string> knownIds = knownRemoteItemIds as HashSet<string>
                ?? new HashSet<string>(
                    (knownRemoteItemIds ?? Array.Empty<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim()),
                    StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(trimmedRootFolderId)
                && string.Equals(itemId, trimmedRootFolderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (knownIds.Contains(itemId))
            {
                return true;
            }

            return (change.ParentIds ?? new List<string>()).Any(parentId =>
                !string.IsNullOrWhiteSpace(parentId)
                && (knownIds.Contains(parentId.Trim())
                    || (!string.IsNullOrWhiteSpace(trimmedRootFolderId)
                        && string.Equals(parentId.Trim(), trimmedRootFolderId, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
