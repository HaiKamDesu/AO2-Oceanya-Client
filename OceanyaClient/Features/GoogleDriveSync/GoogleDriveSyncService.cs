using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveSyncService
    {
        private const int MaxParallelDownloads = 6;
        private readonly GoogleDriveSessionFactory sessionFactory;
        private readonly GoogleDriveOAuthService oauthService;

        public GoogleDriveSyncService(GoogleDriveSessionFactory? sessionFactory = null)
        {
            this.sessionFactory = sessionFactory ?? new GoogleDriveSessionFactory();
            oauthService = new GoogleDriveOAuthService();
        }

        public async Task<GoogleDriveUserInfo> SignInAsync(
            GoogleDriveSyncSettings settings,
            bool forceAccountSelection,
            CancellationToken cancellationToken)
        {
            return await sessionFactory.SignInAsync(settings, forceAccountSelection, cancellationToken);
        }

        public void SignOut(GoogleDriveSyncSettings settings)
        {
            sessionFactory.SignOut(settings);
        }

        public async Task VerifyClientConfigurationAsync(
            GoogleDriveOAuthClientConfiguration configuration,
            CancellationToken cancellationToken)
        {
            await oauthService.VerifyClientConfigurationAsync(configuration, cancellationToken);
        }

        public async Task<string> GetRemoteFolderNameAsync(
            GoogleDriveSyncSettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await client.GetFolderNameAsync(settings.RemoteFolderId, cancellationToken);
        }

        public async Task<GoogleDriveSyncFolderEntry> CreateRemoteFolderAsync(
            GoogleDriveSyncSettings settings,
            string folderName,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await client.CreateFolderAsync(null, folderName, cancellationToken);
        }

        public async Task<GoogleDriveSyncSummary> PullFromDriveAsync(
            GoogleDriveSyncSettings settings,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await PullFromDriveAsync(client, settings, progress, cancellationToken);
        }

        public async Task<GoogleDriveSyncSummary> PushLocalFolderAsync(
            GoogleDriveSyncSettings settings,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await PushLocalFolderAsync(client, settings, progress, cancellationToken);
        }

        public async Task<GoogleDriveSyncSummary> MergeWithDriveAsync(
            GoogleDriveSyncSettings settings,
            GoogleDriveLocalMirrorState localBaseline,
            GoogleDriveLocalMirrorState remoteBaseline,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await MergeWithDriveAsync(
                client,
                settings,
                localBaseline,
                remoteBaseline,
                progress,
                cancellationToken);
        }

        public async Task<GoogleDriveSyncSummary> PullFromDriveAsync(
            IGoogleDriveRemoteClient client,
            GoogleDriveSyncSettings settings,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            ValidateSyncSettings(settings);

            Directory.CreateDirectory(settings.LocalFolderPath);
            GoogleDriveManagedLocalFolderMarkerService.EnsureMarkerIfNeeded(settings);
            progress?.Invoke("Reading Google Drive folder structure...");
            GoogleDriveSyncSnapshot remoteSnapshot = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(
                await client.GetSnapshotAsync(settings.RemoteFolderId, cancellationToken));

            progress?.Invoke("Scanning local sync folder...");
            GoogleDriveSyncSnapshot localSnapshot = GoogleDriveLocalSnapshotBuilder.Build(settings.LocalFolderPath);
            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildPullPlan(remoteSnapshot, localSnapshot, settings.MirrorDeletes);

            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary
            {
                FilesSkipped = Math.Max(0, remoteSnapshot.Files.Count - plan.Operations.Count(operation =>
                    operation.Kind == GoogleDriveSyncOperationKind.DownloadFile))
            };
            summary.FinalRemoteSnapshot = CloneSnapshot(remoteSnapshot);

            List<GoogleDriveSyncOperation> directoryOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.EnsureLocalDirectory)
                .ToList();
            List<GoogleDriveSyncOperation> downloadOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.DownloadFile)
                .ToList();
            List<GoogleDriveSyncOperation> deleteOperations = plan.Operations
                .Where(operation =>
                    operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalFile
                    || operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalDirectory)
                .ToList();

            foreach (GoogleDriveSyncOperation operation in directoryOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);

                progress?.Invoke("Ensuring local folder: " + operation.RelativePath);
                Directory.CreateDirectory(localPath);
                summary.DirectoriesCreated++;
            }

            if (downloadOperations.Count > 0)
            {
                int totalDownloads = downloadOperations.Count;
                int completedDownloads = 0;
                int maxConcurrency = Math.Min(MaxParallelDownloads, Math.Max(1, totalDownloads));
                using SemaphoreSlim throttler = new SemaphoreSlim(maxConcurrency, maxConcurrency);

                async Task DownloadOperationAsync(GoogleDriveSyncOperation operation)
                {
                    await throttler.WaitAsync(cancellationToken);
                    try
                    {
                        string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                        int startingIndex = Math.Min(totalDownloads, Volatile.Read(ref completedDownloads) + 1);
                        progress?.Invoke($"Downloading {startingIndex}/{totalDownloads}: {operation.RelativePath}");
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? settings.LocalFolderPath);
                        await client.DownloadFileAsync(operation.RemoteItemId, localPath, cancellationToken);
                        int finishedCount = Interlocked.Increment(ref completedDownloads);
                        progress?.Invoke($"Downloaded {finishedCount}/{totalDownloads}: {operation.RelativePath}");
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }

                await Task.WhenAll(downloadOperations.Select(DownloadOperationAsync));
                summary.FilesDownloaded += downloadOperations.Count;
                foreach (GoogleDriveSyncOperation operation in downloadOperations)
                {
                    summary.LocalChanges.RecordAddedOrUpdated(operation.RelativePath);
                }
            }

            foreach (GoogleDriveSyncOperation operation in deleteOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);

                switch (operation.Kind)
                {
                    case GoogleDriveSyncOperationKind.DeleteLocalFile:
                        progress?.Invoke("Deleting local file: " + operation.RelativePath);
                        if (File.Exists(localPath))
                        {
                            File.Delete(localPath);
                            summary.LocalFilesDeleted++;
                        }

                        summary.LocalChanges.RecordDeleted(operation.RelativePath);

                        break;
                    case GoogleDriveSyncOperationKind.DeleteLocalDirectory:
                        progress?.Invoke("Deleting local folder: " + operation.RelativePath);
                        if (Directory.Exists(localPath))
                        {
                            Directory.Delete(localPath, true);
                            summary.LocalDirectoriesDeleted++;
                        }

                        summary.LocalChanges.RecordDeleted(operation.RelativePath);

                        break;
                }
            }

            settings.LastSyncUtc = DateTimeOffset.UtcNow;
            return summary;
        }

        public async Task<GoogleDriveSyncSummary> PushLocalFolderAsync(
            IGoogleDriveRemoteClient client,
            GoogleDriveSyncSettings settings,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            ValidateSyncSettings(settings);
            Directory.CreateDirectory(settings.LocalFolderPath);
            GoogleDriveManagedLocalFolderMarkerService.EnsureMarkerIfNeeded(settings);

            progress?.Invoke("Scanning local sync folder...");
            GoogleDriveSyncSnapshot localSnapshot = GoogleDriveLocalSnapshotBuilder.Build(settings.LocalFolderPath);

            progress?.Invoke("Reading Google Drive folder structure...");
            GoogleDriveSyncSnapshot remoteSnapshot = await client.GetSnapshotAsync(settings.RemoteFolderId, cancellationToken);
            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildPushPlan(localSnapshot, remoteSnapshot, settings.MirrorDeletes);

            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary
            {
                FilesSkipped = Math.Max(0, localSnapshot.Files.Count - plan.Operations.Count(operation =>
                    operation.Kind == GoogleDriveSyncOperationKind.UploadFile))
            };
            GoogleDriveSyncSnapshot finalRemoteSnapshot = CloneSnapshot(remoteSnapshot);
            summary.FinalRemoteSnapshot = finalRemoteSnapshot;

            Dictionary<string, GoogleDriveSyncFolderEntry> remoteFolders = finalRemoteSnapshot.Folders;
            List<GoogleDriveSyncOperation> uploadOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.UploadFile)
                .ToList();
            int totalUploads = uploadOperations.Count;
            int completedUploads = 0;

            foreach (GoogleDriveSyncOperation operation in plan.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (operation.Kind)
                {
                    case GoogleDriveSyncOperationKind.EnsureRemoteDirectory:
                    {
                        if (remoteFolders.ContainsKey(operation.RelativePath))
                        {
                            break;
                        }

                        string? parentFolderId = ResolveParentFolderId(settings.RemoteFolderId, remoteFolders, operation.ParentRelativePath);
                        progress?.Invoke("Creating Drive folder: " + operation.RelativePath);
                        GoogleDriveSyncFolderEntry created = await client.CreateFolderAsync(
                            parentFolderId,
                            Path.GetFileName(operation.RelativePath),
                            cancellationToken);
                        created.RelativePath = operation.RelativePath;
                        remoteFolders[operation.RelativePath] = created;
                        summary.DirectoriesCreated++;
                        if (!string.IsNullOrWhiteSpace(created.ItemId))
                        {
                            summary.KnownRemoteItemIds.Add(created.ItemId.Trim());
                        }
                        break;
                    }
                    case GoogleDriveSyncOperationKind.UploadFile:
                    {
                        string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                        string fileName = Path.GetFileName(localPath);
                        string? parentFolderId = ResolveParentFolderId(settings.RemoteFolderId, remoteFolders, operation.ParentRelativePath);
                        progress?.Invoke($"Uploading {completedUploads + 1}/{Math.Max(1, totalUploads)}: {operation.RelativePath}");
                        string uploadedItemId = await client.UploadFileAsync(
                            parentFolderId ?? settings.RemoteFolderId,
                            fileName,
                            localPath,
                            string.IsNullOrWhiteSpace(operation.RemoteItemId) ? null : operation.RemoteItemId,
                            cancellationToken);
                        UpsertRemoteFile(
                            finalRemoteSnapshot,
                            operation.RelativePath,
                            uploadedItemId,
                            parentFolderId ?? settings.RemoteFolderId,
                            localPath);
                        completedUploads++;
                        progress?.Invoke($"Uploaded {completedUploads}/{Math.Max(1, totalUploads)}: {operation.RelativePath}");
                        summary.FilesUploaded++;
                        summary.LocalChanges.RecordAddedOrUpdated(operation.RelativePath);
                        if (!string.IsNullOrWhiteSpace(uploadedItemId))
                        {
                            summary.KnownRemoteItemIds.Add(uploadedItemId.Trim());
                        }
                        break;
                    }
                    case GoogleDriveSyncOperationKind.DeleteRemoteFile:
                        progress?.Invoke("Deleting Drive file: " + operation.RelativePath);
                        await client.DeleteItemAsync(operation.RemoteItemId, cancellationToken);
                        RemoveRemotePath(finalRemoteSnapshot, operation.RelativePath, removeDescendants: false);
                        summary.RemoteFilesDeleted++;
                        summary.LocalChanges.RecordDeleted(operation.RelativePath);
                        break;
                    case GoogleDriveSyncOperationKind.DeleteRemoteDirectory:
                        progress?.Invoke("Deleting Drive folder: " + operation.RelativePath);
                        await client.DeleteItemAsync(operation.RemoteItemId, cancellationToken);
                        RemoveRemotePath(finalRemoteSnapshot, operation.RelativePath, removeDescendants: true);
                        summary.RemoteDirectoriesDeleted++;
                        summary.LocalChanges.RecordDeleted(operation.RelativePath);
                        break;
                }
            }

            settings.LastSyncUtc = DateTimeOffset.UtcNow;
            return summary;
        }

        public async Task<GoogleDriveSyncSummary> MergeWithDriveAsync(
            IGoogleDriveRemoteClient client,
            GoogleDriveSyncSettings settings,
            GoogleDriveLocalMirrorState localBaseline,
            GoogleDriveLocalMirrorState remoteBaseline,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            ValidateSyncSettings(settings);
            Directory.CreateDirectory(settings.LocalFolderPath);
            GoogleDriveManagedLocalFolderMarkerService.EnsureMarkerIfNeeded(settings);

            progress?.Invoke("Scanning local sync folder...");
            GoogleDriveSyncSnapshot localSnapshot = GoogleDriveLocalSnapshotBuilder.Build(settings.LocalFolderPath);

            progress?.Invoke("Reading Google Drive folder structure...");
            GoogleDriveSyncSnapshot remoteSnapshot = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(
                await client.GetSnapshotAsync(settings.RemoteFolderId, cancellationToken));

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildBidirectionalPlan(
                localBaseline,
                remoteBaseline,
                localSnapshot,
                remoteSnapshot,
                settings.MirrorDeletes);

            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary();
            GoogleDriveSyncSnapshot finalRemoteSnapshot = CloneSnapshot(remoteSnapshot);
            summary.FinalRemoteSnapshot = finalRemoteSnapshot;
            Dictionary<string, GoogleDriveSyncFolderEntry> remoteFolders = finalRemoteSnapshot.Folders;

            List<GoogleDriveSyncOperation> localFileDeleteOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalFile)
                .ToList();
            List<GoogleDriveSyncOperation> localDirectoryDeleteOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalDirectory)
                .ToList();
            List<GoogleDriveSyncOperation> remoteFileDeleteOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteFile)
                .ToList();
            List<GoogleDriveSyncOperation> remoteDirectoryDeleteOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteDirectory)
                .ToList();
            List<GoogleDriveSyncOperation> ensureLocalDirectoryOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.EnsureLocalDirectory)
                .ToList();
            List<GoogleDriveSyncOperation> ensureRemoteDirectoryOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.EnsureRemoteDirectory)
                .ToList();
            List<GoogleDriveSyncOperation> downloadOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.DownloadFile)
                .ToList();
            List<GoogleDriveSyncOperation> uploadOperations = plan.Operations
                .Where(operation => operation.Kind == GoogleDriveSyncOperationKind.UploadFile)
                .ToList();

            foreach (GoogleDriveSyncOperation operation in localFileDeleteOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                progress?.Invoke("Deleting local file: " + operation.RelativePath);
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                    summary.LocalFilesDeleted++;
                }

                summary.LocalChanges.RecordDeleted(operation.RelativePath);
            }

            foreach (GoogleDriveSyncOperation operation in localDirectoryDeleteOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                progress?.Invoke("Deleting local folder: " + operation.RelativePath);
                if (Directory.Exists(localPath))
                {
                    Directory.Delete(localPath, true);
                    summary.LocalDirectoriesDeleted++;
                }

                summary.LocalChanges.RecordDeleted(operation.RelativePath);
            }

            foreach (GoogleDriveSyncOperation operation in remoteFileDeleteOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke("Deleting Drive file: " + operation.RelativePath);
                await client.DeleteItemAsync(operation.RemoteItemId, cancellationToken);
                RemoveRemotePath(finalRemoteSnapshot, operation.RelativePath, removeDescendants: false);
                summary.RemoteFilesDeleted++;
                summary.LocalChanges.RecordDeleted(operation.RelativePath);
            }

            foreach (GoogleDriveSyncOperation operation in remoteDirectoryDeleteOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke("Deleting Drive folder: " + operation.RelativePath);
                await client.DeleteItemAsync(operation.RemoteItemId, cancellationToken);
                RemoveRemotePath(finalRemoteSnapshot, operation.RelativePath, removeDescendants: true);
                summary.RemoteDirectoriesDeleted++;
                summary.LocalChanges.RecordDeleted(operation.RelativePath);
            }

            foreach (GoogleDriveSyncOperation operation in ensureLocalDirectoryOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                progress?.Invoke("Ensuring local folder: " + operation.RelativePath);
                Directory.CreateDirectory(localPath);
                summary.DirectoriesCreated++;
            }

            foreach (GoogleDriveSyncOperation operation in ensureRemoteDirectoryOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remoteFolders.ContainsKey(operation.RelativePath))
                {
                    continue;
                }

                string? parentFolderId = ResolveParentFolderId(settings.RemoteFolderId, remoteFolders, operation.ParentRelativePath);
                progress?.Invoke("Creating Drive folder: " + operation.RelativePath);
                GoogleDriveSyncFolderEntry created = await client.CreateFolderAsync(
                    parentFolderId,
                    Path.GetFileName(operation.RelativePath),
                    cancellationToken);
                created.RelativePath = operation.RelativePath;
                remoteFolders[operation.RelativePath] = created;
                summary.DirectoriesCreated++;
                if (!string.IsNullOrWhiteSpace(created.ItemId))
                {
                    summary.KnownRemoteItemIds.Add(created.ItemId.Trim());
                }
            }

            int totalDownloads = downloadOperations.Count;
            int completedDownloads = 0;
            foreach (GoogleDriveSyncOperation operation in downloadOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                progress?.Invoke($"Downloading {completedDownloads + 1}/{Math.Max(1, totalDownloads)}: {operation.RelativePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? settings.LocalFolderPath);
                await client.DownloadFileAsync(operation.RemoteItemId, localPath, cancellationToken);
                completedDownloads++;
                progress?.Invoke($"Downloaded {completedDownloads}/{Math.Max(1, totalDownloads)}: {operation.RelativePath}");
                summary.FilesDownloaded++;
                summary.LocalChanges.RecordAddedOrUpdated(operation.RelativePath);
            }

            int totalUploads = uploadOperations.Count;
            int completedUploads = 0;
            foreach (GoogleDriveSyncOperation operation in uploadOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string localPath = ResolveLocalPath(settings.LocalFolderPath, operation.RelativePath);
                string fileName = Path.GetFileName(localPath);
                string? parentFolderId = ResolveParentFolderId(settings.RemoteFolderId, remoteFolders, operation.ParentRelativePath);
                progress?.Invoke($"Uploading {completedUploads + 1}/{Math.Max(1, totalUploads)}: {operation.RelativePath}");
                string uploadedItemId = await client.UploadFileAsync(
                    parentFolderId ?? settings.RemoteFolderId,
                    fileName,
                    localPath,
                    string.IsNullOrWhiteSpace(operation.RemoteItemId) ? null : operation.RemoteItemId,
                    cancellationToken);
                UpsertRemoteFile(
                    finalRemoteSnapshot,
                    operation.RelativePath,
                    uploadedItemId,
                    parentFolderId ?? settings.RemoteFolderId,
                    localPath);
                completedUploads++;
                progress?.Invoke($"Uploaded {completedUploads}/{Math.Max(1, totalUploads)}: {operation.RelativePath}");
                summary.FilesUploaded++;
                summary.LocalChanges.RecordAddedOrUpdated(operation.RelativePath);
                if (!string.IsNullOrWhiteSpace(uploadedItemId))
                {
                    summary.KnownRemoteItemIds.Add(uploadedItemId.Trim());
                }
            }

            settings.LastSyncUtc = DateTimeOffset.UtcNow;
            return summary;
        }

        public async Task<GoogleDriveConnectionRuntimeState> BuildRuntimeStateAfterSyncAsync(
            string connectionId,
            GoogleDriveSyncSettings settings,
            GoogleDriveSyncSummary summary,
            CancellationToken cancellationToken)
        {
            IGoogleDriveRemoteClient client = await sessionFactory.CreateAuthorizedClientAsync(settings, cancellationToken);
            return await BuildRuntimeStateAfterSyncAsync(
                client,
                connectionId,
                settings,
                summary,
                cancellationToken);
        }

        public async Task<GoogleDriveConnectionRuntimeState> BuildRuntimeStateAfterSyncAsync(
            IGoogleDriveRemoteClient client,
            string connectionId,
            GoogleDriveSyncSettings settings,
            GoogleDriveSyncSummary summary,
            CancellationToken cancellationToken)
        {
            ValidateSyncSettings(settings);
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            GoogleDriveSyncSnapshot remoteSnapshot = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(
                summary.FinalRemoteSnapshot
                ?? await client.GetSnapshotAsync(settings.RemoteFolderId, cancellationToken));
            string changePageToken = await client.GetStartPageTokenAsync(cancellationToken);
            GoogleDriveConnectionRuntimeState runtimeState = GoogleDriveRemoteChangeTracker.BuildRuntimeState(
                connectionId,
                settings.RemoteFolderId,
                remoteSnapshot,
                changePageToken);
            runtimeState.LocalMirrorState = GoogleDriveLocalMirrorStateSupport.CaptureExact(settings.LocalFolderPath);
            runtimeState.HasRemoteMirrorState = true;
            runtimeState = GoogleDriveRemoteChangeTracker.MergeKnownRemoteItemIds(runtimeState, summary.KnownRemoteItemIds);
            runtimeState.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
            runtimeState.LastErrorMessage = string.Empty;
            return GoogleDriveConnectionRuntimeStateStore.Normalize(runtimeState);
        }

        private static void ValidateSyncSettings(GoogleDriveSyncSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.RemoteFolderId))
            {
                throw new InvalidOperationException("A Google Drive folder ID is required.");
            }

            if (string.IsNullOrWhiteSpace(settings.LocalFolderPath))
            {
                throw new InvalidOperationException("A local sync folder is required.");
            }
        }

        private static string ResolveLocalPath(string localRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return localRoot;
            }

            string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(localRoot, normalizedRelativePath);
        }

        private static string? ResolveParentFolderId(
            string rootFolderId,
            IReadOnlyDictionary<string, GoogleDriveSyncFolderEntry> remoteFolders,
            string parentRelativePath)
        {
            if (string.IsNullOrWhiteSpace(parentRelativePath))
            {
                return rootFolderId;
            }

            return remoteFolders.TryGetValue(parentRelativePath, out GoogleDriveSyncFolderEntry? folder)
                ? folder.ItemId
                : rootFolderId;
        }

        private static GoogleDriveSyncSnapshot CloneSnapshot(GoogleDriveSyncSnapshot snapshot)
        {
            GoogleDriveSyncSnapshot clone = new GoogleDriveSyncSnapshot();
            foreach (KeyValuePair<string, GoogleDriveSyncFolderEntry> pair in snapshot?.Folders
                ?? new Dictionary<string, GoogleDriveSyncFolderEntry>(StringComparer.OrdinalIgnoreCase))
            {
                clone.Folders[pair.Key] = new GoogleDriveSyncFolderEntry
                {
                    RelativePath = pair.Value.RelativePath,
                    ItemId = pair.Value.ItemId
                };
            }

            foreach (KeyValuePair<string, GoogleDriveSyncFileEntry> pair in snapshot?.Files
                ?? new Dictionary<string, GoogleDriveSyncFileEntry>(StringComparer.OrdinalIgnoreCase))
            {
                clone.Files[pair.Key] = new GoogleDriveSyncFileEntry
                {
                    RelativePath = pair.Value.RelativePath,
                    ItemId = pair.Value.ItemId,
                    ParentId = pair.Value.ParentId,
                    Size = pair.Value.Size,
                    Hash = pair.Value.Hash
                };
            }

            return clone;
        }

        private static void UpsertRemoteFile(
            GoogleDriveSyncSnapshot snapshot,
            string relativePath,
            string itemId,
            string parentFolderId,
            string localFilePath)
        {
            string normalizedRelativePath = GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(relativePath);
            snapshot.Files[normalizedRelativePath] = new GoogleDriveSyncFileEntry
            {
                RelativePath = normalizedRelativePath,
                ItemId = itemId?.Trim() ?? string.Empty,
                ParentId = parentFolderId?.Trim() ?? string.Empty,
                Size = new FileInfo(localFilePath).Length,
                Hash = GoogleDriveLocalSnapshotBuilder.ComputeMd5(localFilePath)
            };
            snapshot.Folders.Remove(normalizedRelativePath);
        }

        private static void RemoveRemotePath(
            GoogleDriveSyncSnapshot snapshot,
            string relativePath,
            bool removeDescendants)
        {
            string normalizedRelativePath = GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(relativePath);
            snapshot.Files.Remove(normalizedRelativePath);
            snapshot.Folders.Remove(normalizedRelativePath);

            if (!removeDescendants || string.IsNullOrWhiteSpace(normalizedRelativePath))
            {
                return;
            }

            string nestedPrefix = normalizedRelativePath + "/";
            foreach (string nestedFilePath in snapshot.Files.Keys
                .Where(path => path.StartsWith(nestedPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                snapshot.Files.Remove(nestedFilePath);
            }

            foreach (string nestedFolderPath in snapshot.Folders.Keys
                .Where(path => path.StartsWith(nestedPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                snapshot.Folders.Remove(nestedFolderPath);
            }
        }
    }
}
