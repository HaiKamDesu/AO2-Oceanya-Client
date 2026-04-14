using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.FileHivemind;
using OceanyaClient.Features.GoogleDriveSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    public class FileHivemindBackgroundAgentLauncherTests
    {
        [Test]
        public void EnsureRunningForCurrentSession_RegistersAndLaunchesWhenEnabled()
        {
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar();
            string launchedArgument = string.Empty;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                argument =>
                {
                    launchedArgument = argument;
                    return true;
                },
                () => false);
            FileHivemindSettings settings = new FileHivemindSettings
            {
                RunAgentAtStartup = true,
                Connections = new List<FileHivemindConnectionProfile>
                {
                    new FileHivemindConnectionProfile
                    {
                        ProviderId = FileHivemindProviderIds.GoogleDrive,
                        GoogleDrive = new GoogleDriveSyncSettings
                        {
                            OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                            OAuthClientSecret = "desktop-client-secret",
                            RemoteFolderId = "folder-id",
                            LocalFolderPath = @"C:\sync",
                            TokenStoreKey = "token-key",
                            LastSignedInEmail = "tester@example.com"
                        }
                    }
                }
            };

            bool launched = launcher.EnsureRunningForCurrentSession(settings);

            Assert.That(launched, Is.True);
            Assert.That(registrar.LastEnabledValue, Is.True);
            Assert.That(launchedArgument, Is.EqualTo(FileHivemindBackgroundAgentCommandLine.AgentArgument));
        }

        [Test]
        public void EnsureRunningForCurrentSession_DoesNotLaunchWhenDisabled()
        {
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar();
            bool launched = false;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                _ =>
                {
                    launched = true;
                    return true;
                });
            FileHivemindSettings settings = new FileHivemindSettings
            {
                RunAgentAtStartup = false,
                Connections = new List<FileHivemindConnectionProfile>
                {
                    new FileHivemindConnectionProfile
                    {
                        ProviderId = FileHivemindProviderIds.GoogleDrive,
                        GoogleDrive = new GoogleDriveSyncSettings
                        {
                            OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                            OAuthClientSecret = "desktop-client-secret",
                            RemoteFolderId = "folder-id",
                            LocalFolderPath = @"C:\sync",
                            TokenStoreKey = "token-key",
                            LastSignedInEmail = "tester@example.com"
                        }
                    }
                }
            };

            bool result = launcher.EnsureRunningForCurrentSession(settings);

            Assert.That(result, Is.False);
            Assert.That(launched, Is.False);
            Assert.That(registrar.LastEnabledValue, Is.False);
        }

        [Test]
        public void EnsureRunningForCurrentSession_DoesNotLaunchWhenNoEligibleConnectionsExist()
        {
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar();
            bool launched = false;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                _ =>
                {
                    launched = true;
                    return true;
                });
            FileHivemindSettings settings = new FileHivemindSettings
            {
                RunAgentAtStartup = true,
                Connections = new List<FileHivemindConnectionProfile>
                {
                    new FileHivemindConnectionProfile
                    {
                        ProviderId = FileHivemindProviderIds.GoogleDrive,
                        GoogleDrive = new GoogleDriveSyncSettings
                        {
                            RemoteFolderId = "folder-id",
                            LocalFolderPath = @"C:\sync"
                        }
                    }
                }
            };

            bool result = launcher.EnsureRunningForCurrentSession(settings);

            Assert.That(result, Is.False);
            Assert.That(launched, Is.False);
            Assert.That(registrar.LastEnabledValue, Is.False);
        }

        [Test]
        public void IsEligibleConnection_ReturnsFalseWhenAutoSyncIsDisabled()
        {
            FileHivemindConnectionProfile connection = new FileHivemindConnectionProfile
            {
                AutoSyncEnabled = false,
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                GoogleDrive = new GoogleDriveSyncSettings
                {
                    OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                    OAuthClientSecret = "desktop-client-secret",
                    RemoteFolderId = "folder-id",
                    LocalFolderPath = @"C:\sync",
                    TokenStoreKey = "token-key",
                    LastSignedInEmail = "tester@example.com"
                }
            };

            bool eligible = FileHivemindBackgroundAgentLauncher.IsEligibleConnection(connection);

            Assert.That(eligible, Is.False);
        }

        [Test]
        public void EnsureRunningForCurrentSession_DoesNotLaunchWhenAgentAlreadyRunning()
        {
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar();
            bool launched = false;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                _ =>
                {
                    launched = true;
                    return true;
                },
                () => true);
            FileHivemindSettings settings = new FileHivemindSettings
            {
                RunAgentAtStartup = true,
                Connections = new List<FileHivemindConnectionProfile>
                {
                    new FileHivemindConnectionProfile
                    {
                        ProviderId = FileHivemindProviderIds.GoogleDrive,
                        GoogleDrive = new GoogleDriveSyncSettings
                        {
                            OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                            OAuthClientSecret = "desktop-client-secret",
                            RemoteFolderId = "folder-id",
                            LocalFolderPath = @"C:\sync",
                            TokenStoreKey = "token-key",
                            LastSignedInEmail = "tester@example.com"
                        }
                    }
                }
            };

            bool result = launcher.EnsureRunningForCurrentSession(settings);

            Assert.That(result, Is.True);
            Assert.That(launched, Is.False);
            Assert.That(registrar.LastEnabledValue, Is.True);
        }

        [Test]
        public void StartForCurrentSession_LaunchesWhenEligibleConnectionExistsEvenIfAutoStartIsDisabled()
        {
            FakeAutoStartRegistrar registrar = new FakeAutoStartRegistrar();
            string launchedArgument = string.Empty;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                registrar,
                argument =>
                {
                    launchedArgument = argument;
                    return true;
                },
                () => false);
            FileHivemindSettings settings = new FileHivemindSettings
            {
                RunAgentAtStartup = false,
                Connections = new List<FileHivemindConnectionProfile>
                {
                    new FileHivemindConnectionProfile
                    {
                        ProviderId = FileHivemindProviderIds.GoogleDrive,
                        GoogleDrive = new GoogleDriveSyncSettings
                        {
                            OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                            OAuthClientSecret = "desktop-client-secret",
                            RemoteFolderId = "folder-id",
                            LocalFolderPath = @"C:\sync",
                            TokenStoreKey = "token-key",
                            LastSignedInEmail = "tester@example.com"
                        }
                    }
                }
            };

            bool launched = launcher.StartForCurrentSession(settings);

            Assert.That(launched, Is.True);
            Assert.That(registrar.LastEnabledValue, Is.False);
            Assert.That(launchedArgument, Is.EqualTo(FileHivemindBackgroundAgentCommandLine.AgentArgument));
        }

        [Test]
        public void RequestStopForCurrentSession_SignalsStopWhenAgentIsRunning()
        {
            int stopRequests = 0;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                new FakeAutoStartRegistrar(),
                _ => true,
                () => true,
                () =>
                {
                    stopRequests++;
                    return true;
                });

            bool stopped = launcher.RequestStopForCurrentSession();

            Assert.That(stopped, Is.True);
            Assert.That(stopRequests, Is.EqualTo(1));
        }

        [Test]
        public void RequestStopForCurrentSession_DoesNothingWhenAgentIsNotRunning()
        {
            int stopRequests = 0;
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher(
                new FakeAutoStartRegistrar(),
                _ => true,
                () => false,
                () =>
                {
                    stopRequests++;
                    return true;
                });

            bool stopped = launcher.RequestStopForCurrentSession();

            Assert.That(stopped, Is.False);
            Assert.That(stopRequests, Is.EqualTo(0));
        }

        [Test]
        public void ResolveAgentExecutablePath_PrefersCompanionAgentExecutable()
        {
            string root = Path.Combine(Path.GetTempPath(), "hivemind_agent_path_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(root);
                string mainExecutablePath = Path.Combine(root, FileHivemindBackgroundAgentCommandLine.MainApplicationExecutableFileName);
                string agentExecutablePath = Path.Combine(root, FileHivemindBackgroundAgentCommandLine.AgentExecutableFileName);
                File.WriteAllText(mainExecutablePath, string.Empty);
                File.WriteAllText(agentExecutablePath, string.Empty);

                string resolved = FileHivemindBackgroundAgentCommandLine.ResolveAgentExecutablePath(mainExecutablePath);

                Assert.That(resolved, Is.EqualTo(agentExecutablePath));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void ResolveMainApplicationExecutablePath_PrefersCompanionMainExecutable()
        {
            string root = Path.Combine(Path.GetTempPath(), "hivemind_main_path_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(root);
                string mainExecutablePath = Path.Combine(root, FileHivemindBackgroundAgentCommandLine.MainApplicationExecutableFileName);
                string agentExecutablePath = Path.Combine(root, FileHivemindBackgroundAgentCommandLine.AgentExecutableFileName);
                File.WriteAllText(mainExecutablePath, string.Empty);
                File.WriteAllText(agentExecutablePath, string.Empty);

                string resolved = FileHivemindBackgroundAgentCommandLine.ResolveMainApplicationExecutablePath(agentExecutablePath);

                Assert.That(resolved, Is.EqualTo(mainExecutablePath));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void ConnectionExecutionLock_BlocksSecondAcquisitionUntilReleased()
        {
            string connectionId = Guid.NewGuid().ToString("N");

            using FileHivemindConnectionExecutionLock? firstLock =
                FileHivemindConnectionExecutionLock.TryAcquire(connectionId, TimeSpan.Zero);
            FileHivemindConnectionExecutionLock? secondLock = null;
            Thread workerThread = new Thread(() =>
            {
                secondLock = FileHivemindConnectionExecutionLock.TryAcquire(connectionId, TimeSpan.Zero);
            });
            workerThread.SetApartmentState(ApartmentState.STA);
            workerThread.Start();
            workerThread.Join();

            Assert.That(firstLock, Is.Not.Null);
            Assert.That(secondLock, Is.Null);
            secondLock?.Dispose();
        }

        private sealed class FakeAutoStartRegistrar : IFileHivemindAutoStartRegistrar
        {
            public bool LastEnabledValue { get; private set; }

            public bool IsRegistered()
            {
                return LastEnabledValue;
            }

            public void SetRegistered(bool enabled)
            {
                LastEnabledValue = enabled;
            }
        }
    }

    [TestFixture]
    public class FileHivemindBackgroundAgentNotificationTextTests
    {
        [Test]
        public void ShouldNotifyActionStart_OnlyReturnsTrueForActualDetectedChangeReasons()
        {
            Assert.That(FileHivemindBackgroundSyncAgent.ShouldNotifyActionStart("local mirror changes", isPull: false), Is.True);
            Assert.That(FileHivemindBackgroundSyncAgent.ShouldNotifyActionStart("remote Google Drive changes", isPull: true), Is.True);
            Assert.That(FileHivemindBackgroundSyncAgent.ShouldNotifyActionStart("initial sync", isPull: true), Is.False);
            Assert.That(FileHivemindBackgroundSyncAgent.ShouldNotifyActionStart("change-token reset", isPull: true), Is.False);
        }

        [Test]
        public void BuildActionStartNotificationMessage_UsesExpectedUserFacingText()
        {
            Assert.That(
                FileHivemindBackgroundSyncAgent.BuildActionStartNotificationMessage(isPull: false),
                Is.EqualTo("Detected a local change, pushing to Drive..."));
            Assert.That(
                FileHivemindBackgroundSyncAgent.BuildActionStartNotificationMessage(isPull: true),
                Is.EqualTo("Detected a remote change, pulling from Drive..."));
            Assert.That(
                FileHivemindBackgroundSyncAgent.BuildMergeActionStartNotificationMessage(),
                Is.EqualTo("Detected local and remote changes, reconciling with Drive..."));
        }

        [Test]
        public void DecideAutomaticRemoteChangeHandling_MergesWhenBothSidesChanged()
        {
            GoogleDriveRemoteChangeCheckResult result = new GoogleDriveRemoteChangeCheckResult
            {
                HasRelevantChanges = true
            };

            FileHivemindBackgroundSyncAgent.AutomaticRemoteChangeHandling handling =
                FileHivemindBackgroundSyncAgent.DecideAutomaticRemoteChangeHandling(
                    hasUnsyncedLocalChanges: true,
                    result);

            Assert.That(
                handling,
                Is.EqualTo(FileHivemindBackgroundSyncAgent.AutomaticRemoteChangeHandling.MergeBidirectional));
        }

        [Test]
        public void DecideAutomaticRemoteChangeHandling_PullsWhenOnlyRemoteChanged()
        {
            GoogleDriveRemoteChangeCheckResult result = new GoogleDriveRemoteChangeCheckResult
            {
                HasRelevantChanges = true
            };

            FileHivemindBackgroundSyncAgent.AutomaticRemoteChangeHandling handling =
                FileHivemindBackgroundSyncAgent.DecideAutomaticRemoteChangeHandling(
                    hasUnsyncedLocalChanges: false,
                    result);

            Assert.That(
                handling,
                Is.EqualTo(FileHivemindBackgroundSyncAgent.AutomaticRemoteChangeHandling.PullFromDrive));
        }

        [Test]
        public void DecideAutomaticRemoteChangeHandling_BlocksWhenBidirectionalBaselineIsUnavailable()
        {
            GoogleDriveRemoteChangeCheckResult result = new GoogleDriveRemoteChangeCheckResult
            {
                HasRelevantChanges = true
            };

            FileHivemindBackgroundSyncAgent.AutomaticRemoteChangeHandling handling =
                FileHivemindBackgroundSyncAgent.DecideAutomaticRemoteChangeHandling(
                    hasUnsyncedLocalChanges: true,
                    result,
                    canMergeBidirectionally: false);

            Assert.That(
                handling,
                Is.EqualTo(FileHivemindBackgroundSyncAgent.AutomaticRemoteChangeHandling.BlockAutomaticSync));
        }

        [Test]
        public void BuildAutomaticSyncConflictMessage_DistinguishesTokenResetCase()
        {
            string normalConflict = FileHivemindBackgroundSyncAgent.BuildAutomaticSyncConflictMessage(
                requiresFullResync: false);
            string tokenResetConflict = FileHivemindBackgroundSyncAgent.BuildAutomaticSyncConflictMessage(
                requiresFullResync: true);

            Assert.That(normalConflict, Does.Contain("enough baseline data"));
            Assert.That(tokenResetConflict, Does.Contain("change token"));
        }

        [Test]
        public void BuildCompletionNotificationMessage_UsesShortUploadSummary()
        {
            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary
            {
                FilesUploaded = 3
            };

            string message = FileHivemindBackgroundSyncAgent.BuildCompletionNotificationMessage(summary, isPull: false);

            Assert.That(message, Is.EqualTo("Synced with Drive (Uploaded 3 files)"));
        }

        [Test]
        public void BuildCompletionNotificationMessage_UsesShortDownloadDeleteSummary()
        {
            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary
            {
                FilesDownloaded = 2,
                LocalFilesDeleted = 1,
                LocalDirectoriesDeleted = 1
            };

            string message = FileHivemindBackgroundSyncAgent.BuildCompletionNotificationMessage(summary, isPull: true);

            Assert.That(message, Is.EqualTo("Synced with Drive (Downloaded 2 files, deleted 2 items)"));
        }

        [Test]
        public void BuildMergeCompletionNotificationMessage_IncludesUploadsDownloadsAndDeletes()
        {
            GoogleDriveSyncSummary summary = new GoogleDriveSyncSummary
            {
                FilesDownloaded = 2,
                FilesUploaded = 1,
                LocalFilesDeleted = 1
            };

            string message = FileHivemindBackgroundSyncAgent.BuildMergeCompletionNotificationMessage(summary);

            Assert.That(message, Is.EqualTo("Synced with Drive (Downloaded 2 files, Uploaded 1 files, Deleted 1 items)"));
        }

        [Test]
        public void ParseOperationProgress_ExtractsFractionFromReadableProgressText()
        {
            FileHivemindBackgroundSyncAgent.ParsedOperationProgress parsed =
                FileHivemindBackgroundSyncAgent.ParseOperationProgress("Uploaded 3/5: characters/test/char.ini");

            Assert.That(parsed.Detail, Is.EqualTo("Uploaded 3/5: characters/test/char.ini"));
            Assert.That(parsed.ProgressFraction, Is.EqualTo(0.6d).Within(0.0001d));
        }

        [Test]
        public void ParseOperationProgress_ReturnsNullFractionForNonCountMessage()
        {
            FileHivemindBackgroundSyncAgent.ParsedOperationProgress parsed =
                FileHivemindBackgroundSyncAgent.ParseOperationProgress("Reading Google Drive folder structure...");

            Assert.That(parsed.Detail, Is.EqualTo("Reading Google Drive folder structure..."));
            Assert.That(parsed.ProgressFraction, Is.Null);
        }
    }

    [TestFixture]
    public class GoogleDriveRemoteChangeTrackerTests
    {
        [Test]
        public async Task CaptureRuntimeStateAsync_StoresKnownItemIdsAndCurrentPageToken()
        {
            FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
            client.AddFolder("characters", "folder-characters");
            client.AddFolder("characters/phoenix", "folder-phoenix");
            client.AddFile("characters/phoenix/char.ini", "file-char", "name=Phoenix");
            client.StartPageToken = "token-123";
            GoogleDriveRemoteChangeTracker tracker = new GoogleDriveRemoteChangeTracker();

            GoogleDriveConnectionRuntimeState state = await tracker.CaptureRuntimeStateAsync(
                client,
                "connection-1",
                client.RootFolderId,
                CancellationToken.None);

            Assert.That(state.ConnectionId, Is.EqualTo("connection-1"));
            Assert.That(state.RootFolderId, Is.EqualTo(client.RootFolderId));
            Assert.That(state.ChangePageToken, Is.EqualTo("token-123"));
            Assert.That(state.KnownRemoteItemIds, Contains.Item(client.RootFolderId));
            Assert.That(state.KnownRemoteItemIds, Contains.Item("folder-characters"));
            Assert.That(state.KnownRemoteItemIds, Contains.Item("file-char"));
        }

        [Test]
        public async Task CaptureRuntimeStateAsync_IgnoresManagedFolderMarkerFilesInKnownItemIds()
        {
            FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
            client.AddFile("desktop.ini", "desktop-id", "marker");
            client.AddFile(GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName, "icon-id", "icon");
            client.AddFolder("characters", "folder-characters");
            client.AddFolder("characters/phoenix", "folder-phoenix");
            client.AddFile("characters/phoenix/char.ini", "file-char", "name=Phoenix");
            client.StartPageToken = "token-123";
            GoogleDriveRemoteChangeTracker tracker = new GoogleDriveRemoteChangeTracker();

            GoogleDriveConnectionRuntimeState state = await tracker.CaptureRuntimeStateAsync(
                client,
                "connection-1",
                client.RootFolderId,
                CancellationToken.None);

            Assert.That(state.KnownRemoteItemIds, Does.Not.Contain("desktop-id"));
            Assert.That(state.KnownRemoteItemIds, Does.Not.Contain("icon-id"));
            Assert.That(state.KnownRemoteItemIds, Contains.Item("file-char"));
        }

        [Test]
        public async Task CheckForRelevantChangesAsync_ReturnsRelevantWhenChangedItemHasKnownParent()
        {
            FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
            client.QueueChangePage(new GoogleDriveChangePage
            {
                NewStartPageToken = "token-2",
                Changes = new List<GoogleDriveChangeEntry>
                {
                    new GoogleDriveChangeEntry
                    {
                        ItemId = "new-file",
                        ParentIds = new List<string> { "known-folder" }
                    }
                }
            });
            GoogleDriveRemoteChangeTracker tracker = new GoogleDriveRemoteChangeTracker();
            GoogleDriveConnectionRuntimeState existingState = new GoogleDriveConnectionRuntimeState
            {
                ConnectionId = "connection-1",
                RootFolderId = client.RootFolderId,
                ChangePageToken = "token-1",
                KnownRemoteItemIds = new List<string> { client.RootFolderId, "known-folder" }
            };

            GoogleDriveRemoteChangeCheckResult result = await tracker.CheckForRelevantChangesAsync(
                client,
                "connection-1",
                client.RootFolderId,
                existingState,
                CancellationToken.None);

            Assert.That(result.HasRelevantChanges, Is.True);
            Assert.That(result.RequiresFullResync, Is.False);
            Assert.That(result.UpdatedState.ChangePageToken, Is.EqualTo("token-2"));
        }

        [Test]
        public async Task CheckForRelevantChangesAsync_RequestsFullResyncWhenTokenIsMissing()
        {
            FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
            GoogleDriveRemoteChangeTracker tracker = new GoogleDriveRemoteChangeTracker();

            GoogleDriveRemoteChangeCheckResult result = await tracker.CheckForRelevantChangesAsync(
                client,
                "connection-1",
                client.RootFolderId,
                new GoogleDriveConnectionRuntimeState
                {
                    ConnectionId = "connection-1",
                    RootFolderId = client.RootFolderId
                },
                CancellationToken.None);

            Assert.That(result.RequiresFullResync, Is.True);
            Assert.That(result.HasRelevantChanges, Is.False);
        }

        private sealed class FakeGoogleDriveRemoteClient : IGoogleDriveRemoteClient
        {
            private readonly Dictionary<string, string> fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> folderIdsToRelativePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Queue<GoogleDriveChangePage> queuedChangePages = new Queue<GoogleDriveChangePage>();

            public FakeGoogleDriveRemoteClient()
            {
                RootFolderId = "root";
                folderIdsToRelativePaths[RootFolderId] = string.Empty;
            }

            public string RootFolderId { get; }
            public string StartPageToken { get; set; } = "token-0";
            public GoogleDriveSyncSnapshot Snapshot { get; } = new GoogleDriveSyncSnapshot();

            public void AddFolder(string relativePath, string folderId)
            {
                Snapshot.Folders[relativePath] = new GoogleDriveSyncFolderEntry
                {
                    RelativePath = relativePath,
                    ItemId = folderId
                };
                folderIdsToRelativePaths[folderId] = relativePath;
            }

            public void AddFile(string relativePath, string fileId, string content)
            {
                string parentRelativePath = relativePath.Contains('/')
                    ? relativePath[..relativePath.LastIndexOf('/')]
                    : string.Empty;
                string parentId = string.IsNullOrWhiteSpace(parentRelativePath)
                    ? RootFolderId
                    : Snapshot.Folders[parentRelativePath].ItemId;
                string tempPath = Path.GetTempFileName();

                try
                {
                    File.WriteAllText(tempPath, content);
                    Snapshot.Files[relativePath] = new GoogleDriveSyncFileEntry
                    {
                        RelativePath = relativePath,
                        ItemId = fileId,
                        ParentId = parentId,
                        Size = new FileInfo(tempPath).Length,
                        Hash = GoogleDriveLocalSnapshotBuilder.ComputeMd5(tempPath)
                    };
                }
                finally
                {
                    File.Delete(tempPath);
                }

                fileContents[relativePath] = content;
            }

            public void QueueChangePage(GoogleDriveChangePage page)
            {
                queuedChangePages.Enqueue(page);
            }

            public Task<GoogleDriveUserInfo> GetCurrentUserAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new GoogleDriveUserInfo());
            }

            public Task<string> GetFolderNameAsync(string folderId, CancellationToken cancellationToken)
            {
                return Task.FromResult("Folder");
            }

            public Task<string> GetStartPageTokenAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(StartPageToken);
            }

            public Task<GoogleDriveChangePage> GetChangesAsync(string pageToken, CancellationToken cancellationToken)
            {
                if (queuedChangePages.Count == 0)
                {
                    return Task.FromResult(new GoogleDriveChangePage
                    {
                        NewStartPageToken = pageToken
                    });
                }

                return Task.FromResult(queuedChangePages.Dequeue());
            }

            public Task<GoogleDriveSyncSnapshot> GetSnapshotAsync(string rootFolderId, CancellationToken cancellationToken)
            {
                GoogleDriveSyncSnapshot clone = new GoogleDriveSyncSnapshot();
                foreach (KeyValuePair<string, GoogleDriveSyncFolderEntry> pair in Snapshot.Folders)
                {
                    clone.Folders[pair.Key] = new GoogleDriveSyncFolderEntry
                    {
                        RelativePath = pair.Value.RelativePath,
                        ItemId = pair.Value.ItemId
                    };
                }

                foreach (KeyValuePair<string, GoogleDriveSyncFileEntry> pair in Snapshot.Files)
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

                return Task.FromResult(clone);
            }

            public Task<GoogleDriveSyncFolderEntry> CreateFolderAsync(string? parentFolderId, string folderName, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<string> UploadFileAsync(string parentFolderId, string fileName, string localFilePath, string? existingFileId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task DeleteItemAsync(string itemId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }

    [TestFixture]
    public class GoogleDriveConnectionRuntimeStateStoreTests
    {
        [Test]
        public void MergeKnownRemoteItemIds_AddsAdditionalIdsToRuntimeState()
        {
            GoogleDriveConnectionRuntimeState state = new GoogleDriveConnectionRuntimeState
            {
                ConnectionId = "connection-1",
                RootFolderId = "root-folder",
                KnownRemoteItemIds = new List<string> { "root-folder" }
            };

            GoogleDriveConnectionRuntimeState merged = GoogleDriveRemoteChangeTracker.MergeKnownRemoteItemIds(
                state,
                new[] { "file-1", "folder-2", "file-1" });

            Assert.That(merged.KnownRemoteItemIds, Contains.Item("root-folder"));
            Assert.That(merged.KnownRemoteItemIds, Contains.Item("file-1"));
            Assert.That(merged.KnownRemoteItemIds, Contains.Item("folder-2"));
            Assert.That(merged.KnownRemoteItemIds.Count(item => item == "file-1"), Is.EqualTo(1));
        }

        [Test]
        public void RuntimeStateStore_Normalize_PreservesLastStatusMessageFields()
        {
            GoogleDriveConnectionRuntimeState state = GoogleDriveConnectionRuntimeStateStore.Normalize(
                new GoogleDriveConnectionRuntimeState
                {
                    ConnectionId = "connection-1",
                    LastStatusLevel = " Action ",
                    LastStatusMessage = " Pull started (remote Google Drive changes). "
                });

            Assert.That(state.LastStatusLevel, Is.EqualTo("Action"));
            Assert.That(state.LastStatusMessage, Is.EqualTo("Pull started (remote Google Drive changes)."));
        }

        [Test]
        public void BackgroundLogStore_AppendAndRead_RoundTripsEntries()
        {
            string root = Path.Combine(Path.GetTempPath(), "file_hivemind_log_" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(root, "background-agent.log");

            try
            {
                FileHivemindBackgroundLogStore store = new FileHivemindBackgroundLogStore(filePath);
                store.Append(new FileHivemindBackgroundLogEntry
                {
                    Level = "Action",
                    ConnectionId = "connection-1",
                    ConnectionName = "Campaign",
                    Message = "Polling Google Drive for remote changes."
                });

                FileHivemindBackgroundLogReadResult result = store.ReadFrom(0);

                Assert.That(result.Entries.Count, Is.EqualTo(1));
                Assert.That(result.Entries[0].Level, Is.EqualTo("Action"));
                Assert.That(result.Entries[0].ConnectionName, Is.EqualTo("Campaign"));
                Assert.That(result.Entries[0].Message, Is.EqualTo("Polling Google Drive for remote changes."));
                Assert.That(result.NextPosition, Is.GreaterThan(0));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        /// <summary>
        /// R-015 — concurrent background-agent.log writers must not throw IOException or
        /// lose entries when they hit the same file at the same time.
        /// </summary>
        [Test]
        public void BackgroundLogStore_ConcurrentAppends_DoNotThrowOrLoseEntries()
        {
            string root = Path.Combine(Path.GetTempPath(), "file_hivemind_log_" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(root, "background-agent.log");

            try
            {
                FileHivemindBackgroundLogStore storeA = new FileHivemindBackgroundLogStore(filePath);
                FileHivemindBackgroundLogStore storeB = new FileHivemindBackgroundLogStore(filePath);
                List<Exception> failures = new List<Exception>();
                using ManualResetEventSlim startGate = new ManualResetEventSlim(false);

                Task writerA = Task.Run(() =>
                {
                    try
                    {
                        startGate.Wait();
                        for (int index = 0; index < 25; index++)
                        {
                            storeA.Append(new FileHivemindBackgroundLogEntry
                            {
                                Level = "Action",
                                ConnectionId = "connection-a",
                                ConnectionName = "Campaign A",
                                Message = "writer-a-" + index
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (failures)
                        {
                            failures.Add(ex);
                        }
                    }
                });

                Task writerB = Task.Run(() =>
                {
                    try
                    {
                        startGate.Wait();
                        for (int index = 0; index < 25; index++)
                        {
                            storeB.Append(new FileHivemindBackgroundLogEntry
                            {
                                Level = "Action",
                                ConnectionId = "connection-b",
                                ConnectionName = "Campaign B",
                                Message = "writer-b-" + index
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (failures)
                        {
                            failures.Add(ex);
                        }
                    }
                });

                startGate.Set();
                Task.WaitAll(writerA, writerB);

                FileHivemindBackgroundLogReadResult result = new FileHivemindBackgroundLogStore(filePath).ReadFrom(0);

                Assert.Multiple(() =>
                {
                    Assert.That(failures, Is.Empty, "Concurrent log writers must not throw");
                    Assert.That(result.Entries.Count, Is.EqualTo(50), "All concurrent log entries must be preserved");
                    Assert.That(result.Entries.Count(entry => entry.ConnectionId == "connection-a"), Is.EqualTo(25));
                    Assert.That(result.Entries.Count(entry => entry.ConnectionId == "connection-b"), Is.EqualTo(25));
                });
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void LocalMirrorStateSupport_HasDifferences_DetectsLocalDeletion()
        {
            string root = Path.Combine(Path.GetTempPath(), "google_drive_local_state_" + Guid.NewGuid().ToString("N"));

            try
            {
                string charactersPath = Path.Combine(root, "characters");
                Directory.CreateDirectory(charactersPath);
                File.WriteAllText(Path.Combine(charactersPath, "char.ini"), "name=Phoenix");

                GoogleDriveLocalMirrorState baseline = GoogleDriveLocalMirrorStateSupport.Capture(root);
                File.Delete(Path.Combine(charactersPath, "char.ini"));

                bool hasDifferences = GoogleDriveLocalMirrorStateSupport.HasDifferences(baseline, root);

                Assert.That(hasDifferences, Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void LocalMirrorStateSupport_HasDifferences_ReturnsFalseForUnchangedMirror()
        {
            string root = Path.Combine(Path.GetTempPath(), "google_drive_local_state_" + Guid.NewGuid().ToString("N"));

            try
            {
                string charactersPath = Path.Combine(root, "characters");
                Directory.CreateDirectory(charactersPath);
                File.WriteAllText(Path.Combine(charactersPath, "char.ini"), "name=Phoenix");

                GoogleDriveLocalMirrorState baseline = GoogleDriveLocalMirrorStateSupport.Capture(root);
                bool hasDifferences = GoogleDriveLocalMirrorStateSupport.HasDifferences(baseline, root);

                Assert.That(hasDifferences, Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void LocalMirrorStateSupport_HasDifferences_IgnoresTimestampOnlyChangesWhenHashesMatch()
        {
            string root = Path.Combine(Path.GetTempPath(), "google_drive_local_state_" + Guid.NewGuid().ToString("N"));

            try
            {
                string charactersPath = Path.Combine(root, "characters");
                string charPath = Path.Combine(charactersPath, "char.ini");
                Directory.CreateDirectory(charactersPath);
                File.WriteAllText(charPath, "name=Phoenix");

                GoogleDriveLocalMirrorState baseline = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);
                File.SetLastWriteTimeUtc(charPath, DateTime.UtcNow.AddMinutes(5));
                GoogleDriveLocalMirrorState current = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);

                bool hasDifferences = GoogleDriveLocalMirrorStateSupport.HasDifferences(baseline, current);

                Assert.That(hasDifferences, Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void DestructiveDeletionProtection_DetectsMissingOrEmptyMirrorAgainstLargeBaseline()
        {
            FileHivemindConnectionProfile connection = new FileHivemindConnectionProfile
            {
                DisplayName = "Campaign",
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                GoogleDrive = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = @"C:\missing-mirror"
                }
            };
            GoogleDriveLocalMirrorState baseline = new GoogleDriveLocalMirrorState
            {
                DirectoryPaths = Enumerable.Range(0, 8).Select(index => "characters/char" + index).ToList(),
                Files = Enumerable.Range(0, 8)
                    .Select(index => new GoogleDriveLocalMirrorFileState
                    {
                        RelativePath = $"characters/char{index}/char.ini",
                        Size = 10,
                        LastWriteUtcTicks = 100 + index
                    })
                    .ToList()
            };
            GoogleDriveLocalMirrorState current = new GoogleDriveLocalMirrorState();

            object?[] parameters = { connection, baseline, current, null! };
            bool blocked = InvokeDestructiveDeletionProtection(parameters);

            Assert.That(blocked, Is.True);
            Assert.That(parameters[3], Is.TypeOf<string>());
            string blockingReason = (string)parameters[3]!;
            Assert.That(blockingReason, Does.Contain("blocked"));
            Assert.That(blockingReason, Does.Contain("destructive local delete"));
        }

        [Test]
        public void DestructiveDeletionProtection_DoesNotBlockSmallBaseline()
        {
            string root = Path.Combine(Path.GetTempPath(), "google_drive_local_state_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(root);
                FileHivemindConnectionProfile connection = new FileHivemindConnectionProfile
                {
                    DisplayName = "Campaign",
                    ProviderId = FileHivemindProviderIds.GoogleDrive,
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        LocalFolderPath = root
                    }
                };
                GoogleDriveLocalMirrorState baseline = new GoogleDriveLocalMirrorState
                {
                    Files = new List<GoogleDriveLocalMirrorFileState>
                    {
                        new GoogleDriveLocalMirrorFileState
                        {
                            RelativePath = "characters/char.ini",
                            Size = 10,
                            LastWriteUtcTicks = 1
                        }
                    }
                };
                GoogleDriveLocalMirrorState current = new GoogleDriveLocalMirrorState();

                object?[] parameters = { connection, baseline, current, null! };
                bool blocked = InvokeDestructiveDeletionProtection(parameters);

                Assert.That(blocked, Is.False);
                Assert.That(parameters[3], Is.EqualTo(string.Empty).Or.Null);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void SaveAndLoad_RoundTripsRuntimeState()
        {
            string root = Path.Combine(Path.GetTempPath(), "google_drive_runtime_state_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveConnectionRuntimeStateStore store = new GoogleDriveConnectionRuntimeStateStore(root);
                GoogleDriveConnectionRuntimeState state = new GoogleDriveConnectionRuntimeState
                {
                    ConnectionId = "connection-1",
                    RootFolderId = "root-folder",
                    ChangePageToken = "token-5",
                    KnownRemoteItemIds = new List<string> { "root-folder", "file-1", "folder-2" },
                    LocalMirrorState = new GoogleDriveLocalMirrorState
                    {
                        DirectoryPaths = new List<string> { "characters" },
                        Files = new List<GoogleDriveLocalMirrorFileState>
                        {
                            new GoogleDriveLocalMirrorFileState
                            {
                                RelativePath = "characters/char.ini",
                                Size = 18,
                                LastWriteUtcTicks = 12345
                            }
                        }
                    },
                    RemoteMirrorState = new GoogleDriveLocalMirrorState
                    {
                        DirectoryPaths = new List<string> { "characters" },
                        Files = new List<GoogleDriveLocalMirrorFileState>
                        {
                            new GoogleDriveLocalMirrorFileState
                            {
                                RelativePath = "characters/char.ini",
                                Size = 18,
                                ContentHash = "hash-1"
                            }
                        }
                    },
                    HasRemoteMirrorState = true,
                    LastSuccessfulSyncUtc = DateTimeOffset.UtcNow
                };

                store.Save(state);
                GoogleDriveConnectionRuntimeState? loaded = store.Load("connection-1");

                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.ConnectionId, Is.EqualTo("connection-1"));
                Assert.That(loaded.ChangePageToken, Is.EqualTo("token-5"));
                Assert.That(loaded.KnownRemoteItemIds, Contains.Item("file-1"));
                Assert.That(loaded.LocalMirrorState.DirectoryPaths, Contains.Item("characters"));
                Assert.That(loaded.LocalMirrorState.Files.Count, Is.EqualTo(1));
                Assert.That(loaded.LocalMirrorState.Files[0].RelativePath, Is.EqualTo("characters/char.ini"));
                Assert.That(loaded.RemoteMirrorState.DirectoryPaths, Contains.Item("characters"));
                Assert.That(loaded.RemoteMirrorState.Files.Count, Is.EqualTo(1));
                Assert.That(loaded.RemoteMirrorState.Files[0].ContentHash, Is.EqualTo("hash-1"));
                Assert.That(loaded.HasRemoteMirrorState, Is.True);
                Assert.That(loaded.LastSuccessfulSyncUtc, Is.EqualTo(state.LastSuccessfulSyncUtc));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void SaveAndLoad_CanOverlapAcrossThreadsWithoutFileLockErrors()
        {
            string root = Path.Combine(Path.GetTempPath(), "google_drive_runtime_state_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveConnectionRuntimeStateStore store = new GoogleDriveConnectionRuntimeStateStore(root);
                GoogleDriveConnectionRuntimeState state = new GoogleDriveConnectionRuntimeState
                {
                    ConnectionId = "connection-1",
                    RootFolderId = "root-folder",
                    ChangePageToken = "token-1"
                };
                Exception? backgroundFailure = null;
                using ManualResetEventSlim writerStarted = new ManualResetEventSlim(false);

                Task writer = Task.Run(() =>
                {
                    try
                    {
                        writerStarted.Set();
                        for (int index = 0; index < 50; index++)
                        {
                            state.ChangePageToken = "token-" + index;
                            store.Save(state);
                        }
                    }
                    catch (Exception ex)
                    {
                        backgroundFailure = ex;
                    }
                });

                writerStarted.Wait();
                for (int index = 0; index < 50; index++)
                {
                    GoogleDriveConnectionRuntimeState? loaded = store.Load("connection-1");
                    if (loaded != null)
                    {
                        Assert.That(loaded.ConnectionId, Is.EqualTo("connection-1"));
                    }
                }

                writer.Wait();
                Assert.That(backgroundFailure, Is.Null);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static bool InvokeDestructiveDeletionProtection(object?[] parameters)
        {
            MethodInfo? method = typeof(FileHivemindBackgroundSyncAgent).GetMethod(
                "TryGetSuspiciousMassDeletionMessage",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            object? result = method!.Invoke(null, parameters);
            Assert.That(result, Is.TypeOf<bool>());
            return (bool)result!;
        }
    }
}
