using System;
using System.Collections.Generic;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveOAuthClientConfiguration
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }

    public sealed class GoogleDriveUserInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
    }

    public sealed class GoogleDriveTokenSet
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset AccessTokenExpiresUtc { get; set; }
    }

    public sealed class GoogleDriveInvite
    {
        public string Provider { get; set; } = "google_drive";
        public string FolderId { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
    }

    public sealed class GoogleDriveSyncFolderEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
    }

    public sealed class GoogleDriveSyncFileEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Hash { get; set; } = string.Empty;
    }

    public sealed class GoogleDriveSyncSnapshot
    {
        public Dictionary<string, GoogleDriveSyncFolderEntry> Folders { get; } =
            new Dictionary<string, GoogleDriveSyncFolderEntry>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, GoogleDriveSyncFileEntry> Files { get; } =
            new Dictionary<string, GoogleDriveSyncFileEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public enum GoogleDriveSyncOperationKind
    {
        EnsureLocalDirectory,
        DownloadFile,
        DeleteLocalFile,
        DeleteLocalDirectory,
        EnsureRemoteDirectory,
        UploadFile,
        DeleteRemoteFile,
        DeleteRemoteDirectory
    }

    public sealed class GoogleDriveSyncOperation
    {
        public GoogleDriveSyncOperationKind Kind { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string RemoteItemId { get; set; } = string.Empty;
        public string ParentRelativePath { get; set; } = string.Empty;
    }

    public sealed class GoogleDriveSyncPlan
    {
        public List<GoogleDriveSyncOperation> Operations { get; } = new List<GoogleDriveSyncOperation>();
    }

    public sealed class GoogleDriveSyncSummary
    {
        public int DirectoriesCreated { get; set; }
        public int FilesDownloaded { get; set; }
        public int FilesUploaded { get; set; }
        public int LocalFilesDeleted { get; set; }
        public int RemoteFilesDeleted { get; set; }
        public int LocalDirectoriesDeleted { get; set; }
        public int RemoteDirectoriesDeleted { get; set; }
        public int FilesSkipped { get; set; }
        public GoogleDriveSyncLocalChangeSet LocalChanges { get; } = new GoogleDriveSyncLocalChangeSet();
        public HashSet<string> KnownRemoteItemIds { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class GoogleDriveSyncLocalChangeSet
    {
        public HashSet<string> AddedOrUpdatedPaths { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> DeletedPaths { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasAnyChanges => AddedOrUpdatedPaths.Count > 0 || DeletedPaths.Count > 0;

        public IEnumerable<string> GetAllAffectedPaths()
        {
            return AddedOrUpdatedPaths.Concat(DeletedPaths).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public void RecordAddedOrUpdated(string relativePath)
        {
            string normalizedPath = Normalize(relativePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            AddedOrUpdatedPaths.Add(normalizedPath);
            DeletedPaths.Remove(normalizedPath);
        }

        public void RecordDeleted(string relativePath)
        {
            string normalizedPath = Normalize(relativePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            DeletedPaths.Add(normalizedPath);
            AddedOrUpdatedPaths.Remove(normalizedPath);
        }

        private static string Normalize(string relativePath)
        {
            return (relativePath ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        }
    }

    public sealed class GoogleDriveChangeEntry
    {
        public string ItemId { get; set; } = string.Empty;
        public bool Removed { get; set; }
        public List<string> ParentIds { get; set; } = new List<string>();
    }

    public sealed class GoogleDriveChangePage
    {
        public string NextPageToken { get; set; } = string.Empty;
        public string NewStartPageToken { get; set; } = string.Empty;
        public List<GoogleDriveChangeEntry> Changes { get; set; } = new List<GoogleDriveChangeEntry>();
    }

    public interface IGoogleDriveRemoteClient
    {
        Task<GoogleDriveUserInfo> GetCurrentUserAsync(CancellationToken cancellationToken);

        Task<string> GetFolderNameAsync(string folderId, CancellationToken cancellationToken);

        Task<string> GetStartPageTokenAsync(CancellationToken cancellationToken);

        Task<GoogleDriveChangePage> GetChangesAsync(string pageToken, CancellationToken cancellationToken);

        Task<GoogleDriveSyncSnapshot> GetSnapshotAsync(string rootFolderId, CancellationToken cancellationToken);

        Task<GoogleDriveSyncFolderEntry> CreateFolderAsync(
            string? parentFolderId,
            string folderName,
            CancellationToken cancellationToken);

        Task<string> UploadFileAsync(
            string parentFolderId,
            string fileName,
            string localFilePath,
            string? existingFileId,
            CancellationToken cancellationToken);

        Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken);

        Task DeleteItemAsync(string itemId, CancellationToken cancellationToken);
    }
}
