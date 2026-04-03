using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public static class GoogleDriveSyncPlanner
    {
        public static GoogleDriveSyncPlan BuildBidirectionalPlan(
            GoogleDriveLocalMirrorState localBaseline,
            GoogleDriveLocalMirrorState remoteBaseline,
            GoogleDriveSyncSnapshot localSnapshot,
            GoogleDriveSyncSnapshot remoteSnapshot,
            bool mirrorDeletes)
        {
            GoogleDriveLocalMirrorState normalizedLocalBaseline = GoogleDriveLocalMirrorStateSupport.Normalize(localBaseline);
            GoogleDriveLocalMirrorState normalizedRemoteBaseline = GoogleDriveLocalMirrorStateSupport.Normalize(remoteBaseline);
            GoogleDriveLocalMirrorState currentLocal = GoogleDriveLocalMirrorStateSupport.FromSnapshot(localSnapshot);
            GoogleDriveLocalMirrorState currentRemote = GoogleDriveLocalMirrorStateSupport.FromSnapshot(remoteSnapshot);

            Dictionary<string, GoogleDriveLocalMirrorFileState> localBaselineFiles = BuildFileDictionary(normalizedLocalBaseline);
            Dictionary<string, GoogleDriveLocalMirrorFileState> remoteBaselineFiles = BuildFileDictionary(normalizedRemoteBaseline);
            Dictionary<string, GoogleDriveLocalMirrorFileState> currentLocalFiles = BuildFileDictionary(currentLocal);
            Dictionary<string, GoogleDriveLocalMirrorFileState> currentRemoteFiles = BuildFileDictionary(currentRemote);

            Dictionary<string, GoogleDriveLocalMirrorFileState> desiredLocalFiles =
                new Dictionary<string, GoogleDriveLocalMirrorFileState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, GoogleDriveLocalMirrorFileState> desiredRemoteFiles =
                new Dictionary<string, GoogleDriveLocalMirrorFileState>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> desiredLocalDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> desiredRemoteDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string> allFilePaths = localBaselineFiles.Keys
                .Concat(remoteBaselineFiles.Keys)
                .Concat(currentLocalFiles.Keys)
                .Concat(currentRemoteFiles.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string relativePath in allFilePaths)
            {
                localBaselineFiles.TryGetValue(relativePath, out GoogleDriveLocalMirrorFileState? baselineLocalFile);
                remoteBaselineFiles.TryGetValue(relativePath, out GoogleDriveLocalMirrorFileState? baselineRemoteFile);
                currentLocalFiles.TryGetValue(relativePath, out GoogleDriveLocalMirrorFileState? currentLocalFile);
                currentRemoteFiles.TryGetValue(relativePath, out GoogleDriveLocalMirrorFileState? currentRemoteFile);

                SyncEntryChangeKind localChangeKind = DetermineFileChangeKind(baselineLocalFile, currentLocalFile);
                SyncEntryChangeKind remoteChangeKind = DetermineFileChangeKind(baselineRemoteFile, currentRemoteFile);

                if (localChangeKind != SyncEntryChangeKind.None && remoteChangeKind != SyncEntryChangeKind.None)
                {
                    if (currentRemoteFile != null)
                    {
                        AddDesiredFile(desiredLocalFiles, currentRemoteFile);
                        AddDesiredFile(desiredRemoteFiles, currentRemoteFile);
                    }

                    continue;
                }

                if (remoteChangeKind != SyncEntryChangeKind.None)
                {
                    if (currentRemoteFile != null)
                    {
                        AddDesiredFile(desiredLocalFiles, currentRemoteFile);
                        AddDesiredFile(desiredRemoteFiles, currentRemoteFile);
                    }
                    else if (!mirrorDeletes && currentLocalFile != null)
                    {
                        AddDesiredFile(desiredLocalFiles, currentLocalFile);
                    }

                    continue;
                }

                if (localChangeKind != SyncEntryChangeKind.None)
                {
                    if (currentLocalFile != null)
                    {
                        AddDesiredFile(desiredLocalFiles, currentLocalFile);
                        AddDesiredFile(desiredRemoteFiles, currentLocalFile);
                    }
                    else if (!mirrorDeletes && currentRemoteFile != null)
                    {
                        AddDesiredFile(desiredRemoteFiles, currentRemoteFile);
                    }

                    continue;
                }

                if (currentLocalFile != null)
                {
                    AddDesiredFile(desiredLocalFiles, currentLocalFile);
                }

                if (currentRemoteFile != null)
                {
                    AddDesiredFile(desiredRemoteFiles, currentRemoteFile);
                }
            }

            AddAncestorDirectories(desiredLocalDirectories, desiredLocalFiles.Keys);
            AddAncestorDirectories(desiredRemoteDirectories, desiredRemoteFiles.Keys);

            HashSet<string> baselineLocalDirectories = new HashSet<string>(
                normalizedLocalBaseline.DirectoryPaths,
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> baselineRemoteDirectories = new HashSet<string>(
                normalizedRemoteBaseline.DirectoryPaths,
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> currentLocalDirectories = new HashSet<string>(
                currentLocal.DirectoryPaths,
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> currentRemoteDirectories = new HashSet<string>(
                currentRemote.DirectoryPaths,
                StringComparer.OrdinalIgnoreCase);

            List<string> allDirectoryPaths = baselineLocalDirectories
                .Concat(baselineRemoteDirectories)
                .Concat(currentLocalDirectories)
                .Concat(currentRemoteDirectories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string relativePath in allDirectoryPaths)
            {
                if (desiredLocalDirectories.Contains(relativePath) || desiredRemoteDirectories.Contains(relativePath))
                {
                    continue;
                }

                bool baselineLocalExists = baselineLocalDirectories.Contains(relativePath);
                bool baselineRemoteExists = baselineRemoteDirectories.Contains(relativePath);
                bool currentLocalExists = currentLocalDirectories.Contains(relativePath);
                bool currentRemoteExists = currentRemoteDirectories.Contains(relativePath);

                SyncEntryChangeKind localChangeKind = DetermineDirectoryChangeKind(baselineLocalExists, currentLocalExists);
                SyncEntryChangeKind remoteChangeKind = DetermineDirectoryChangeKind(baselineRemoteExists, currentRemoteExists);

                if (localChangeKind != SyncEntryChangeKind.None && remoteChangeKind != SyncEntryChangeKind.None)
                {
                    if (currentRemoteExists)
                    {
                        desiredLocalDirectories.Add(relativePath);
                        desiredRemoteDirectories.Add(relativePath);
                    }

                    continue;
                }

                if (remoteChangeKind != SyncEntryChangeKind.None)
                {
                    if (currentRemoteExists)
                    {
                        desiredLocalDirectories.Add(relativePath);
                        desiredRemoteDirectories.Add(relativePath);
                    }
                    else if (!mirrorDeletes && currentLocalExists)
                    {
                        desiredLocalDirectories.Add(relativePath);
                    }

                    continue;
                }

                if (localChangeKind != SyncEntryChangeKind.None)
                {
                    if (currentLocalExists)
                    {
                        desiredLocalDirectories.Add(relativePath);
                        desiredRemoteDirectories.Add(relativePath);
                    }
                    else if (!mirrorDeletes && currentRemoteExists)
                    {
                        desiredRemoteDirectories.Add(relativePath);
                    }

                    continue;
                }

                if (currentLocalExists)
                {
                    desiredLocalDirectories.Add(relativePath);
                }

                if (currentRemoteExists)
                {
                    desiredRemoteDirectories.Add(relativePath);
                }
            }

            return BuildBidirectionalOperations(
                currentLocalFiles,
                currentRemoteFiles,
                currentLocalDirectories,
                currentRemoteDirectories,
                desiredLocalFiles,
                desiredRemoteFiles,
                desiredLocalDirectories,
                desiredRemoteDirectories,
                remoteSnapshot);
        }

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

        private static GoogleDriveSyncPlan BuildBidirectionalOperations(
            IReadOnlyDictionary<string, GoogleDriveLocalMirrorFileState> currentLocalFiles,
            IReadOnlyDictionary<string, GoogleDriveLocalMirrorFileState> currentRemoteFiles,
            IReadOnlySet<string> currentLocalDirectories,
            IReadOnlySet<string> currentRemoteDirectories,
            IReadOnlyDictionary<string, GoogleDriveLocalMirrorFileState> desiredLocalFiles,
            IReadOnlyDictionary<string, GoogleDriveLocalMirrorFileState> desiredRemoteFiles,
            IReadOnlySet<string> desiredLocalDirectories,
            IReadOnlySet<string> desiredRemoteDirectories,
            GoogleDriveSyncSnapshot remoteSnapshot)
        {
            GoogleDriveSyncPlan plan = new GoogleDriveSyncPlan();

            foreach (string relativePath in currentLocalFiles.Keys
                .Where(path => !desiredLocalFiles.ContainsKey(path))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.DeleteLocalFile,
                    RelativePath = relativePath
                });
            }

            foreach (string relativePath in currentLocalDirectories
                .Where(path => !desiredLocalDirectories.Contains(path))
                .OrderByDescending(path => GetPathDepth(path))
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.DeleteLocalDirectory,
                    RelativePath = relativePath
                });
            }

            foreach (string relativePath in currentRemoteFiles.Keys
                .Where(path => !desiredRemoteFiles.ContainsKey(path))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!remoteSnapshot.Files.TryGetValue(relativePath, out GoogleDriveSyncFileEntry? currentRemoteFile))
                {
                    continue;
                }

                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.DeleteRemoteFile,
                    RelativePath = relativePath,
                    RemoteItemId = currentRemoteFile.ItemId
                });
            }

            foreach (string relativePath in currentRemoteDirectories
                .Where(path => !desiredRemoteDirectories.Contains(path))
                .OrderByDescending(path => GetPathDepth(path))
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!remoteSnapshot.Folders.TryGetValue(relativePath, out GoogleDriveSyncFolderEntry? currentRemoteFolder))
                {
                    continue;
                }

                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.DeleteRemoteDirectory,
                    RelativePath = relativePath,
                    RemoteItemId = currentRemoteFolder.ItemId
                });
            }

            foreach (string relativePath in desiredLocalDirectories
                .Where(path => !currentLocalDirectories.Contains(path))
                .OrderBy(path => GetPathDepth(path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.EnsureLocalDirectory,
                    RelativePath = relativePath
                });
            }

            foreach (string relativePath in desiredRemoteDirectories
                .Where(path => !currentRemoteDirectories.Contains(path))
                .OrderBy(path => GetPathDepth(path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.EnsureRemoteDirectory,
                    RelativePath = relativePath,
                    ParentRelativePath = GetParentRelativePath(relativePath)
                });
            }

            foreach (KeyValuePair<string, GoogleDriveLocalMirrorFileState> pair in desiredLocalFiles
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (currentLocalFiles.TryGetValue(pair.Key, out GoogleDriveLocalMirrorFileState? currentLocalFile)
                    && MirrorFilesMatch(pair.Value, currentLocalFile))
                {
                    continue;
                }

                if (!remoteSnapshot.Files.TryGetValue(pair.Key, out GoogleDriveSyncFileEntry? currentRemoteFile))
                {
                    continue;
                }

                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.DownloadFile,
                    RelativePath = pair.Key,
                    RemoteItemId = currentRemoteFile.ItemId
                });
            }

            foreach (KeyValuePair<string, GoogleDriveLocalMirrorFileState> pair in desiredRemoteFiles
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (currentRemoteFiles.TryGetValue(pair.Key, out GoogleDriveLocalMirrorFileState? currentRemoteFile)
                    && MirrorFilesMatch(pair.Value, currentRemoteFile))
                {
                    continue;
                }

                string remoteItemId = remoteSnapshot.Files.TryGetValue(pair.Key, out GoogleDriveSyncFileEntry? remoteFile)
                    ? remoteFile.ItemId
                    : string.Empty;
                plan.Operations.Add(new GoogleDriveSyncOperation
                {
                    Kind = GoogleDriveSyncOperationKind.UploadFile,
                    RelativePath = pair.Key,
                    RemoteItemId = remoteItemId,
                    ParentRelativePath = GetParentRelativePath(pair.Key)
                });
            }

            return plan;
        }

        private static Dictionary<string, GoogleDriveLocalMirrorFileState> BuildFileDictionary(
            GoogleDriveLocalMirrorState state)
        {
            return state.Files.ToDictionary(file => file.RelativePath, CloneFileState, StringComparer.OrdinalIgnoreCase);
        }

        private static GoogleDriveLocalMirrorFileState CloneFileState(GoogleDriveLocalMirrorFileState file)
        {
            return new GoogleDriveLocalMirrorFileState
            {
                RelativePath = file.RelativePath,
                Size = file.Size,
                LastWriteUtcTicks = file.LastWriteUtcTicks,
                ContentHash = file.ContentHash
            };
        }

        private static void AddDesiredFile(
            IDictionary<string, GoogleDriveLocalMirrorFileState> target,
            GoogleDriveLocalMirrorFileState file)
        {
            target[file.RelativePath] = CloneFileState(file);
        }

        private static void AddAncestorDirectories(
            ISet<string> target,
            IEnumerable<string> filePaths)
        {
            foreach (string filePath in filePaths)
            {
                string parentRelativePath = GetParentRelativePath(filePath);
                while (!string.IsNullOrWhiteSpace(parentRelativePath))
                {
                    target.Add(parentRelativePath);
                    parentRelativePath = GetParentRelativePath(parentRelativePath);
                }
            }
        }

        private static SyncEntryChangeKind DetermineFileChangeKind(
            GoogleDriveLocalMirrorFileState? baseline,
            GoogleDriveLocalMirrorFileState? current)
        {
            if (baseline == null && current == null)
            {
                return SyncEntryChangeKind.None;
            }

            if (baseline == null)
            {
                return SyncEntryChangeKind.AddedOrUpdated;
            }

            if (current == null)
            {
                return SyncEntryChangeKind.Deleted;
            }

            return MirrorFilesMatch(baseline, current)
                ? SyncEntryChangeKind.None
                : SyncEntryChangeKind.AddedOrUpdated;
        }

        private static SyncEntryChangeKind DetermineDirectoryChangeKind(bool baselineExists, bool currentExists)
        {
            if (baselineExists == currentExists)
            {
                return SyncEntryChangeKind.None;
            }

            return currentExists ? SyncEntryChangeKind.AddedOrUpdated : SyncEntryChangeKind.Deleted;
        }

        private static bool MirrorFilesMatch(
            GoogleDriveLocalMirrorFileState left,
            GoogleDriveLocalMirrorFileState right)
        {
            if (left.Size != right.Size)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(left.ContentHash)
                && !string.IsNullOrWhiteSpace(right.ContentHash))
            {
                return string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);
            }

            return left.LastWriteUtcTicks == right.LastWriteUtcTicks;
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

        private enum SyncEntryChangeKind
        {
            None = 0,
            AddedOrUpdated = 1,
            Deleted = 2
        }
    }
}
