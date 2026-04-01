using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public static class GoogleDriveSyncPlanner
    {
        public static GoogleDriveSyncPlan BuildPullPlan(
            GoogleDriveSyncSnapshot remoteSnapshot,
            GoogleDriveSyncSnapshot localSnapshot,
            bool mirrorDeletes)
        {
            GoogleDriveSyncPlan plan = new GoogleDriveSyncPlan();

            foreach (GoogleDriveSyncFolderEntry remoteFolder in remoteSnapshot.Folders.Values.OrderBy(folder => folder.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!localSnapshot.Folders.ContainsKey(remoteFolder.RelativePath))
                {
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.EnsureLocalDirectory,
                        RelativePath = remoteFolder.RelativePath
                    });
                }
            }

            foreach (GoogleDriveSyncFileEntry remoteFile in remoteSnapshot.Files.Values.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!localSnapshot.Files.TryGetValue(remoteFile.RelativePath, out GoogleDriveSyncFileEntry? localFile)
                    || !FilesMatch(remoteFile, localFile))
                {
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.DownloadFile,
                        RelativePath = remoteFile.RelativePath,
                        RemoteItemId = remoteFile.ItemId
                    });
                }
            }

            if (!mirrorDeletes)
            {
                return plan;
            }

            foreach (GoogleDriveSyncFileEntry localFile in localSnapshot.Files.Values.OrderByDescending(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!remoteSnapshot.Files.ContainsKey(localFile.RelativePath))
                {
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.DeleteLocalFile,
                        RelativePath = localFile.RelativePath
                    });
                }
            }

            foreach (GoogleDriveSyncFolderEntry localFolder in localSnapshot.Folders.Values
                .OrderByDescending(folder => GetPathDepth(folder.RelativePath))
                .ThenByDescending(folder => folder.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!remoteSnapshot.Folders.ContainsKey(localFolder.RelativePath))
                {
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.DeleteLocalDirectory,
                        RelativePath = localFolder.RelativePath
                    });
                }
            }

            return plan;
        }

        public static GoogleDriveSyncPlan BuildPushPlan(
            GoogleDriveSyncSnapshot localSnapshot,
            GoogleDriveSyncSnapshot remoteSnapshot,
            bool mirrorDeletes)
        {
            GoogleDriveSyncPlan plan = new GoogleDriveSyncPlan();

            foreach (GoogleDriveSyncFolderEntry localFolder in localSnapshot.Folders.Values.OrderBy(folder => GetPathDepth(folder.RelativePath)))
            {
                if (!remoteSnapshot.Folders.ContainsKey(localFolder.RelativePath))
                {
                    string parentRelativePath = GetParentRelativePath(localFolder.RelativePath);
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.EnsureRemoteDirectory,
                        RelativePath = localFolder.RelativePath,
                        ParentRelativePath = parentRelativePath
                    });
                }
            }

            foreach (GoogleDriveSyncFileEntry localFile in localSnapshot.Files.Values.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                bool shouldUpload = !remoteSnapshot.Files.TryGetValue(localFile.RelativePath, out GoogleDriveSyncFileEntry? remoteFile)
                    || !FilesMatch(localFile, remoteFile);
                if (!shouldUpload)
                {
                    continue;
                }

                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.UploadFile,
                    RelativePath = localFile.RelativePath,
                    RemoteItemId = remoteFile?.ItemId ?? string.Empty,
                    ParentRelativePath = GetParentRelativePath(localFile.RelativePath)
                });
            }

            if (!mirrorDeletes)
            {
                return plan;
            }

            foreach (GoogleDriveSyncFileEntry remoteFile in remoteSnapshot.Files.Values.OrderByDescending(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!localSnapshot.Files.ContainsKey(remoteFile.RelativePath))
                {
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.DeleteRemoteFile,
                        RelativePath = remoteFile.RelativePath,
                        RemoteItemId = remoteFile.ItemId
                    });
                }
            }

            foreach (GoogleDriveSyncFolderEntry remoteFolder in remoteSnapshot.Folders.Values
                .OrderByDescending(folder => GetPathDepth(folder.RelativePath))
                .ThenByDescending(folder => folder.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!localSnapshot.Folders.ContainsKey(remoteFolder.RelativePath))
                {
                    plan.Operations.Add(new GoogleDriveSyncOperation
                    {
                        Kind = GoogleDriveSyncOperationKind.DeleteRemoteDirectory,
                        RelativePath = remoteFolder.RelativePath,
                        RemoteItemId = remoteFolder.ItemId
                    });
                }
            }

            return plan;
        }

        private static bool FilesMatch(GoogleDriveSyncFileEntry left, GoogleDriveSyncFileEntry right)
        {
            return left.Size == right.Size
                && string.Equals(left.Hash ?? string.Empty, right.Hash ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPathDepth(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return 0;
            }

            return relativePath.Count(character => character == '/');
        }

        private static string GetParentRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            int lastSlash = relativePath.LastIndexOf('/');
            return lastSlash <= 0 ? string.Empty : relativePath[..lastSlash];
        }
    }
}
