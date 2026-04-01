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

        public GoogleDriveSyncService(GoogleDriveSessionFactory? sessionFactory = null)
        {
            this.sessionFactory = sessionFactory ?? new GoogleDriveSessionFactory();
        }

        public async Task<GoogleDriveUserInfo> SignInAsync(
            GoogleDriveSyncSettings settings,
            CancellationToken cancellationToken)
        {
            return await sessionFactory.SignInAsync(settings, cancellationToken);
        }

        public void SignOut(GoogleDriveSyncSettings settings)
        {
            sessionFactory.SignOut(settings);
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

        public async Task<GoogleDriveSyncSummary> PullFromDriveAsync(
            IGoogleDriveRemoteClient client,
            GoogleDriveSyncSettings settings,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            ValidateSyncSettings(settings);

            Directory.CreateDirectory(settings.LocalFolderPath);
            progress?.Invoke("Reading Google Drive folder structure...");
            GoogleDriveSyncSnapshot remoteSnapshot = await client.GetSnapshotAsync(settings.RemoteFolderId, cancellationToken);

            progress?.Invoke("Scanning local sync folder...");
            GoogleDriveSyncSnapshot localSnapshot = GoogleDriveLocalSnapshotBuilder.Build(settings.LocalFolderPath);
            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildPullPlan(remoteSnapshot, localSnapshot, settings.MirrorDeletes);

            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary
            {
                FilesSkipped = Math.Max(0, remoteSnapshot.Files.Count - plan.Operations.Count(operation =>
                    operation.Kind == GoogleDriveSyncOperationKind.DownloadFile))
            };

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
                        progress?.Invoke("Downloading: " + operation.RelativePath);
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

            Dictionary<string, GoogleDriveSyncFolderEntry> remoteFolders = remoteSnapshot.Folders
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

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
                        progress?.Invoke("Uploading: " + operation.RelativePath);
                        string uploadedItemId = await client.UploadFileAsync(
                            parentFolderId ?? settings.RemoteFolderId,
                            fileName,
                            localPath,
                            string.IsNullOrWhiteSpace(operation.RemoteItemId) ? null : operation.RemoteItemId,
                            cancellationToken);
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
                        summary.RemoteFilesDeleted++;
                        summary.LocalChanges.RecordDeleted(operation.RelativePath);
                        break;
                    case GoogleDriveSyncOperationKind.DeleteRemoteDirectory:
                        progress?.Invoke("Deleting Drive folder: " + operation.RelativePath);
                        await client.DeleteItemAsync(operation.RemoteItemId, cancellationToken);
                        summary.RemoteDirectoriesDeleted++;
                        summary.LocalChanges.RecordDeleted(operation.RelativePath);
                        break;
                }
            }

            settings.LastSyncUtc = DateTimeOffset.UtcNow;
            return summary;
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
    }
}
