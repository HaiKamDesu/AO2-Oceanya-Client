using OceanyaClient;
using OceanyaClient.Features.GoogleDriveSync;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using AOBot_Testing.Structures;

namespace UnitTests
{
    [TestFixture]
    public class GoogleDriveInviteSerializerTests
    {
        [Test]
        public void Parse_GoogleDriveFolderUrl_ExtractsFolderId()
        {
            GoogleDriveInvite invite = GoogleDriveInviteSerializer.Parse(
                "https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOpQrStUvWxYz123456?usp=sharing");

            Assert.That(invite.FolderId, Is.EqualTo("1AbCdEfGhIjKlMnOpQrStUvWxYz123456"));
        }

        [Test]
        public void ExtractFolderId_GoogleDriveFolderUrlWithDriveUPath_ExtractsFolderId()
        {
            string folderId = GoogleDriveInviteSerializer.ExtractFolderId(
                "https://drive.google.com/drive/u/0/folders/1AbCdEfGhIjKlMnOpQrStUvWxYz123456?usp=drive_link");

            Assert.That(folderId, Is.EqualTo("1AbCdEfGhIjKlMnOpQrStUvWxYz123456"));
        }

        [Test]
        public void Serialize_RoundTripsInviteJson()
        {
            GoogleDriveInvite original = new GoogleDriveInvite
            {
                FolderId = "1AbCdEfGhIjKlMnOpQrStUvWxYz123456",
                FolderName = "Campaign Assets"
            };

            string serialized = GoogleDriveInviteSerializer.Serialize(original);
            GoogleDriveInvite parsed = GoogleDriveInviteSerializer.Parse(serialized);

            Assert.That(parsed.FolderId, Is.EqualTo(original.FolderId));
            Assert.That(parsed.FolderName, Is.EqualTo(original.FolderName));
        }
    }

    [TestFixture]
    public class FileHivemindConnectionExchangeSerializerTests
    {
        [Test]
        public void SerializeAndParse_StripsSensitiveFieldsButKeepsShareableSettings()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_connection_export_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());
                FileHivemindConnectionProfile original = new FileHivemindConnectionProfile
                {
                    Id = "existing-id",
                    DisplayName = "Shared Campaign",
                    ProviderId = FileHivemindProviderIds.GoogleDrive,
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        OAuthClientId = "app-client-id",
                        OAuthClientSecret = "app-client-secret",
                        OAuthClientSecretStoreKey = "oauth-key",
                        TokenStoreKey = "token-key",
                        LastSignedInEmail = "tester@example.com",
                        LastSignedInDisplayName = "Tester",
                        RemoteFolderId = "1AbCdEfGhIjKlMnOpQrStUvWxYz123456",
                        RemoteFolderName = "Campaign Assets",
                        LocalFolderPath = @"C:\Private\Mirror",
                        AutoAddMountPath = true,
                        MirrorDeletes = false,
                        UseExistingMountPath = true,
                        LastSyncUtc = DateTimeOffset.UtcNow
                    }
                };

                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(original.GoogleDrive, credentialStore);

                string serialized = FileHivemindConnectionExchangeSerializer.Serialize(original, credentialStore);
                FileHivemindConnectionProfile parsed = FileHivemindConnectionExchangeSerializer.Parse(serialized);

                Assert.That(serialized, Does.Not.Contain("tester@example.com"));
                Assert.That(serialized, Does.Not.Contain("token-key"));
                Assert.That(serialized, Does.Not.Contain("Private\\Mirror"));
                Assert.That(serialized, Does.Not.Contain("app-client-secret"));
                Assert.That(parsed.DisplayName, Is.EqualTo("Shared Campaign"));
                Assert.That(parsed.ProviderId, Is.EqualTo(FileHivemindProviderIds.GoogleDrive));
                Assert.That(parsed.GoogleDrive.RemoteFolderId, Is.EqualTo("1AbCdEfGhIjKlMnOpQrStUvWxYz123456"));
                Assert.That(parsed.GoogleDrive.RemoteFolderName, Is.EqualTo("Campaign Assets"));
                Assert.That(parsed.GoogleDrive.AutoAddMountPath, Is.True);
                Assert.That(parsed.GoogleDrive.MirrorDeletes, Is.False);
                Assert.That(parsed.GoogleDrive.OAuthClientId, Is.EqualTo("app-client-id"));
                Assert.That(parsed.GoogleDrive.OAuthClientSecret, Is.EqualTo("app-client-secret"));
                Assert.That(parsed.GoogleDrive.TokenStoreKey, Is.Empty);
                Assert.That(parsed.GoogleDrive.LastSignedInEmail, Is.Empty);
                Assert.That(parsed.GoogleDrive.LastSignedInDisplayName, Is.Empty);
                Assert.That(parsed.GoogleDrive.LocalFolderPath, Is.Empty);
                Assert.That(parsed.GoogleDrive.UseExistingMountPath, Is.False);
                Assert.That(parsed.GoogleDrive.LastSyncUtc, Is.Null);
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
        public void CreateImportReadyProfile_AssignsNewIdsAndManagedLocalPath()
        {
            FileHivemindConnectionProfile parsed = new FileHivemindConnectionProfile
            {
                DisplayName = "Campaign Assets",
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                GoogleDrive = new GoogleDriveSyncSettings
                {
                    OAuthClientId = "shared-client-id",
                    OAuthClientSecret = "shared-client-secret",
                    RemoteFolderId = "1AbCdEfGhIjKlMnOpQrStUvWxYz123456",
                    RemoteFolderName = "Campaign Assets",
                    AutoAddMountPath = true,
                    MirrorDeletes = true
                }
            };

            FileHivemindConnectionProfile imported =
                FileHivemindConnectionExchangeSerializer.CreateImportReadyProfile(parsed);

            Assert.That(imported.Id, Is.Not.Empty);
            Assert.That(imported.GoogleDrive.OAuthClientId, Is.EqualTo("shared-client-id"));
            Assert.That(imported.GoogleDrive.OAuthClientSecret, Is.EqualTo("shared-client-secret"));
            Assert.That(imported.GoogleDrive.OAuthClientSecretStoreKey, Is.Not.Empty);
            Assert.That(imported.GoogleDrive.TokenStoreKey, Is.Not.Empty);
            Assert.That(
                imported.GoogleDrive.LocalFolderPath,
                Is.EqualTo(GoogleDriveClientAssetIntegration.BuildManagedLocalFolderPath(
                    "Campaign Assets",
                    "Campaign Assets",
                    "1AbCdEfGhIjKlMnOpQrStUvWxYz123456")));
            Assert.That(imported.GoogleDrive.IsOceanyaManagedLocalFolder, Is.True);
            Assert.That(imported.GoogleDrive.UseExistingMountPath, Is.False);
            Assert.That(imported.GoogleDrive.LastSignedInEmail, Is.Empty);
            Assert.That(imported.GoogleDrive.LastSignedInDisplayName, Is.Empty);
        }

        [Test]
        public void ExportImportRoundTrip_RestoresConnectionCredentialsWithoutExtraCloudSetup()
        {
            string exportRoot = Path.Combine(Path.GetTempPath(), "drive_export_store_" + Guid.NewGuid().ToString("N"));
            string importRoot = Path.Combine(Path.GetTempPath(), "drive_import_store_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore exportStore =
                    new GoogleDriveSecureClientCredentialStore(exportRoot, new ObfuscatingProtector());
                GoogleDriveSecureClientCredentialStore importStore =
                    new GoogleDriveSecureClientCredentialStore(importRoot, new ObfuscatingProtector());
                FileHivemindConnectionProfile original = new FileHivemindConnectionProfile
                {
                    DisplayName = "Shared Campaign",
                    ProviderId = FileHivemindProviderIds.GoogleDrive,
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                        OAuthClientSecret = "desktop-client-secret",
                        OAuthClientSecretStoreKey = "source-oauth-key",
                        RemoteFolderId = "1AbCdEfGhIjKlMnOpQrStUvWxYz123456",
                        RemoteFolderName = "Campaign Assets"
                    }
                };

                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(original.GoogleDrive, exportStore);

                string serialized = FileHivemindConnectionExchangeSerializer.Serialize(original, exportStore);
                FileHivemindConnectionProfile parsed = FileHivemindConnectionExchangeSerializer.Parse(serialized);
                FileHivemindConnectionProfile imported = FileHivemindConnectionExchangeSerializer.CreateImportReadyProfile(parsed);
                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(imported.GoogleDrive, importStore);

                bool success = GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                    imported.GoogleDrive,
                    out GoogleDriveOAuthClientConfiguration configuration,
                    out string errorMessage,
                    importStore,
                    allowLegacyFallback: false);

                Assert.That(success, Is.True);
                Assert.That(errorMessage, Is.Empty);
                Assert.That(configuration.ClientId, Is.EqualTo("desktop-client-id.apps.googleusercontent.com"));
                Assert.That(configuration.ClientSecret, Is.EqualTo("desktop-client-secret"));
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, true);
                }

                if (Directory.Exists(importRoot))
                {
                    Directory.Delete(importRoot, true);
                }
            }
        }

        [Test]
        public void BuildManagedLocalFolderPath_UsesConnectionNameDriveFolderNameAndDriveId()
        {
            string path = GoogleDriveClientAssetIntegration.BuildManagedLocalFolderPath(
                "Session Assets",
                "Campaign Drive Folder",
                "folder123");

            Assert.That(Path.GetFileName(path), Is.EqualTo("Session Assets - Campaign Drive Folder (folder123)"));
        }

        [Test]
        public void BuildLocalSnapshot_IgnoresManagedFolderMarkerFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_snapshot_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "characters", "phoenix"));
                File.WriteAllText(Path.Combine(root, "characters", "phoenix", "char.ini"), "name=Phoenix");
                File.WriteAllText(Path.Combine(root, "desktop.ini"), "marker");
                File.WriteAllText(Path.Combine(root, GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName), "icon");

                GoogleDriveSyncSnapshot snapshot = GoogleDriveLocalSnapshotBuilder.Build(root);

                Assert.That(snapshot.Files.ContainsKey("characters/phoenix/char.ini"), Is.True);
                Assert.That(snapshot.Files.ContainsKey("desktop.ini"), Is.False);
                Assert.That(snapshot.Files.ContainsKey(GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName), Is.False);
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
        public void FilterReservedSupportFiles_RemovesManagedFolderMarkerFilesFromSnapshot()
        {
            GoogleDriveSyncSnapshot snapshot = new GoogleDriveSyncSnapshot();
            snapshot.Files["desktop.ini"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "desktop.ini",
                ItemId = "desktop-id"
            };
            snapshot.Files[GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName] = new GoogleDriveSyncFileEntry
            {
                RelativePath = GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName,
                ItemId = "icon-id"
            };
            snapshot.Files["characters/phoenix/char.ini"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "characters/phoenix/char.ini",
                ItemId = "char-id"
            };

            GoogleDriveSyncSnapshot filtered = GoogleDriveLocalSnapshotBuilder.FilterReservedSupportFiles(snapshot);

            Assert.That(filtered.Files.ContainsKey("desktop.ini"), Is.False);
            Assert.That(filtered.Files.ContainsKey(GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName), Is.False);
            Assert.That(filtered.Files.ContainsKey("characters/phoenix/char.ini"), Is.True);
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }

            public byte[] Unprotect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }
    }

    [TestFixture]
    public class GoogleDriveSignedInAccountManagerTests
    {
        [Test]
        public void ApplyDefaultAccountToConnection_PrefersLastSelectedCompatibleAccount()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_account_select_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());
                FileHivemindSettings fileHivemind = new FileHivemindSettings
                {
                    GoogleDriveAccounts = new List<GoogleDriveSignedInAccount>
                    {
                        new GoogleDriveSignedInAccount
                        {
                            TokenStoreKey = "token-a",
                            Email = "alpha@example.com",
                            DisplayName = "Alpha",
                            CredentialFingerprint = "placeholder",
                            LastUsedUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                        },
                        new GoogleDriveSignedInAccount
                        {
                            TokenStoreKey = "token-b",
                            Email = "beta@example.com",
                            DisplayName = "Beta",
                            CredentialFingerprint = "placeholder",
                            LastUsedUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                        }
                    },
                    LastSelectedGoogleDriveAccountTokenStoreKey = "token-a"
                };
                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                    OAuthClientSecret = "desktop-client-secret",
                    OAuthClientSecretStoreKey = "oauth-key"
                };

                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(settings, credentialStore);
                string fingerprint = GoogleDriveSignedInAccountManager.GetCredentialFingerprint(settings, credentialStore);
                foreach (GoogleDriveSignedInAccount account in fileHivemind.GoogleDriveAccounts)
                {
                    account.CredentialFingerprint = fingerprint;
                }

                GoogleDriveSignedInAccount? selectedAccount = GoogleDriveSignedInAccountManager.ApplyDefaultAccountToConnection(
                    fileHivemind,
                    settings,
                    credentialStore);

                Assert.That(selectedAccount, Is.Not.Null);
                Assert.That(selectedAccount!.TokenStoreKey, Is.EqualTo("token-a"));
                Assert.That(settings.TokenStoreKey, Is.EqualTo("token-a"));
                Assert.That(settings.LastSignedInEmail, Is.EqualTo("alpha@example.com"));
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
        public void SignOutAccount_ClearsEveryConnectionUsingThatAccount()
        {
            string tokenRoot = Path.Combine(Path.GetTempPath(), "unused_" + Guid.NewGuid().ToString("N"));
            FileHivemindSettings fileHivemind = new FileHivemindSettings
            {
                GoogleDriveAccounts = new List<GoogleDriveSignedInAccount>
                {
                    new GoogleDriveSignedInAccount
                    {
                        TokenStoreKey = "shared-token",
                        Email = "tester@example.com",
                        DisplayName = "Tester",
                        CredentialFingerprint = "fingerprint"
                    }
                },
                LastSelectedGoogleDriveAccountTokenStoreKey = "shared-token"
            };
            List<FileHivemindConnectionProfile> connections = new List<FileHivemindConnectionProfile>
            {
                new FileHivemindConnectionProfile
                {
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        TokenStoreKey = "shared-token",
                        LastSignedInEmail = "tester@example.com",
                        LastSignedInDisplayName = "Tester"
                    }
                },
                new FileHivemindConnectionProfile
                {
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        TokenStoreKey = "shared-token",
                        LastSignedInEmail = "tester@example.com",
                        LastSignedInDisplayName = "Tester"
                    }
                }
            };

            try
            {
                int affectedConnections = GoogleDriveSignedInAccountManager.SignOutAccount(
                    fileHivemind,
                    "shared-token",
                    connections,
                    new GoogleDriveSecureTokenStore(tokenRoot, new ObfuscatingProtector()));

                Assert.That(affectedConnections, Is.EqualTo(2));
                Assert.That(fileHivemind.GoogleDriveAccounts, Is.Empty);
                Assert.That(fileHivemind.LastSelectedGoogleDriveAccountTokenStoreKey, Is.Empty);
                Assert.That(connections.All(connection => string.IsNullOrWhiteSpace(connection.GoogleDrive.TokenStoreKey)), Is.True);
                Assert.That(connections.All(connection => string.IsNullOrWhiteSpace(connection.GoogleDrive.LastSignedInEmail)), Is.True);
            }
            finally
            {
                if (Directory.Exists(tokenRoot))
                {
                    Directory.Delete(tokenRoot, true);
                }
            }
        }

        [Test]
        public void RegisterSignedInAccount_AllowsMultipleSavedAccountsWithoutOverwritingThePreviousTokenKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_account_register_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());
                GoogleDriveSecureTokenStore tokenStore =
                    new GoogleDriveSecureTokenStore(root, new ObfuscatingProtector());
                FileHivemindSettings fileHivemind = new FileHivemindSettings();
                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                    OAuthClientSecret = "desktop-client-secret",
                    OAuthClientSecretStoreKey = "oauth-key",
                    TokenStoreKey = "token-a"
                };

                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(settings, credentialStore);
                GoogleDriveSignedInAccountManager.RegisterSignedInAccount(
                    fileHivemind,
                    settings,
                    new GoogleDriveUserInfo
                    {
                        DisplayName = "Alpha",
                        EmailAddress = "alpha@example.com"
                    },
                    credentialStore,
                    tokenStore);

                settings.TokenStoreKey = "token-b";
                GoogleDriveSignedInAccountManager.RegisterSignedInAccount(
                    fileHivemind,
                    settings,
                    new GoogleDriveUserInfo
                    {
                        DisplayName = "Beta",
                        EmailAddress = "beta@example.com"
                    },
                    credentialStore,
                    tokenStore);

                Assert.That(fileHivemind.GoogleDriveAccounts.Count, Is.EqualTo(2));
                Assert.That(
                    fileHivemind.GoogleDriveAccounts.Select(account => account.TokenStoreKey),
                    Is.EquivalentTo(new[] { "token-a", "token-b" }));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }

            public byte[] Unprotect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }
    }

    [TestFixture]
    public class GoogleDriveConnectionEditorSaveSupportTests
    {
        [Test]
        public void PersistStoredSecrets_SavesTypedSecretWithoutClearingSelectedAccountData()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_editor_save_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());
                FileHivemindConnectionProfile previousPersisted = new FileHivemindConnectionProfile
                {
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        OAuthClientId = string.Empty,
                        OAuthClientSecretStoreKey = "oauth-key"
                    }
                };
                FileHivemindConnectionProfile draft = new FileHivemindConnectionProfile
                {
                    Id = "connection-1",
                    DisplayName = "Campaign",
                    ProviderId = FileHivemindProviderIds.GoogleDrive,
                    AutoSyncEnabled = true,
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                        OAuthClientSecret = "desktop-client-secret",
                        OAuthClientSecretStoreKey = "oauth-key",
                        TokenStoreKey = "token-a",
                        LastSignedInEmail = "tester@example.com",
                        LastSignedInDisplayName = "Tester"
                    }
                };
                FileHivemindConnectionProfile target = FileHivemindConnectionProfile.CreateGoogleDriveProfile();

                GoogleDriveConnectionEditorSaveSupport.PersistStoredSecrets(previousPersisted, draft, credentialStore);
                GoogleDriveConnectionEditorSaveSupport.CopyConnectionProfile(draft, target);

                Assert.That(GoogleDriveConnectionCredentialSupport.LoadSecret(draft.GoogleDrive, credentialStore), Is.EqualTo("desktop-client-secret"));
                Assert.That(GoogleDriveConnectionCredentialSupport.LoadSecret(target.GoogleDrive, credentialStore), Is.EqualTo("desktop-client-secret"));
                Assert.That(draft.GoogleDrive.OAuthClientSecret, Is.Empty);
                Assert.That(target.GoogleDrive.OAuthClientSecret, Is.Empty);
                Assert.That(target.GoogleDrive.TokenStoreKey, Is.EqualTo("token-a"));
                Assert.That(target.GoogleDrive.LastSignedInEmail, Is.EqualTo("tester@example.com"));
                Assert.That(target.GoogleDrive.LastSignedInDisplayName, Is.EqualTo("Tester"));
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
        public void PersistStoredSecrets_DeletesOldStoredSecretWhenClientIdIsCleared()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_editor_delete_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());
                FileHivemindConnectionProfile previousPersisted = new FileHivemindConnectionProfile
                {
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                        OAuthClientSecret = "desktop-client-secret",
                        OAuthClientSecretStoreKey = "oauth-key"
                    }
                };
                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(previousPersisted.GoogleDrive, credentialStore);

                FileHivemindConnectionProfile draft = new FileHivemindConnectionProfile
                {
                    GoogleDrive = new GoogleDriveSyncSettings
                    {
                        OAuthClientId = string.Empty,
                        OAuthClientSecretStoreKey = "oauth-key"
                    }
                };

                GoogleDriveConnectionEditorSaveSupport.PersistStoredSecrets(previousPersisted, draft, credentialStore);

                Assert.That(GoogleDriveConnectionCredentialSupport.HasStoredSecret(previousPersisted.GoogleDrive, credentialStore), Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }

            public byte[] Unprotect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }
    }

    [TestFixture]
    public class GoogleDriveMountPathManagerTests
    {
        [Test]
        public void EnsureMounted_AppendsMountPathOnlyOnce()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_mount_test_" + Guid.NewGuid().ToString("N"));
            string configDirectory = Path.Combine(root, "config");
            string mountPath = Path.Combine(root, "sync");
            string configPath = Path.Combine(configDirectory, "config.ini");

            try
            {
                Directory.CreateDirectory(configDirectory);
                Directory.CreateDirectory(mountPath);
                File.WriteAllText(configPath, "mount_paths=@Invalid()" + Environment.NewLine + "log_maximum=20");

                bool changedFirst = GoogleDriveMountPathManager.EnsureMounted(configPath, mountPath);
                bool changedSecond = GoogleDriveMountPathManager.EnsureMounted(configPath, mountPath);

                string text = File.ReadAllText(configPath);
                Assert.That(changedFirst, Is.True);
                Assert.That(changedSecond, Is.False);
                string expectedMountValue = GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(mountPath);
                Assert.That(text, Does.Contain(expectedMountValue));
                Assert.That(text.Split(expectedMountValue).Length - 1, Is.EqualTo(1));
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
        public void EnsureMounted_DoesNotDuplicateEquivalentRelativeMountPath()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_mount_relative_test_" + Guid.NewGuid().ToString("N"));
            string configDirectory = Path.Combine(root, "base", "config");
            string mountPath = Path.Combine(root, "base", "sync");
            string configPath = Path.Combine(configDirectory, "config.ini");

            try
            {
                Directory.CreateDirectory(configDirectory);
                Directory.CreateDirectory(mountPath);
                File.WriteAllText(configPath, "mount_paths=sync" + Environment.NewLine + "log_maximum=20");

                bool changed = GoogleDriveMountPathManager.EnsureMounted(configPath, mountPath);

                Assert.That(changed, Is.False);
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
        public void NormalizeMountPathForConfigValue_ConvertsBackslashesToForwardSlashes()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows-specific mount path serialization.");
            }

            string normalized = GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(@"C:\AO\sync\folder\");

            Assert.That(normalized, Is.EqualTo("C:/AO/sync/folder"));
        }

        [Test]
        public void ReplaceMountedPath_RewritesExistingMountEntry()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_mount_replace_test_" + Guid.NewGuid().ToString("N"));
            string configDirectory = Path.Combine(root, "config");
            string oldMountPath = Path.Combine(root, "sync_old");
            string newMountPath = Path.Combine(root, "sync_new");
            string configPath = Path.Combine(configDirectory, "config.ini");

            try
            {
                Directory.CreateDirectory(configDirectory);
                Directory.CreateDirectory(oldMountPath);
                Directory.CreateDirectory(newMountPath);
                File.WriteAllText(
                    configPath,
                    "mount_paths="
                    + GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(oldMountPath)
                    + Environment.NewLine
                    + "log_maximum=20");

                bool changed = GoogleDriveMountPathManager.ReplaceMountedPath(configPath, oldMountPath, newMountPath);

                string text = File.ReadAllText(configPath);
                Assert.That(changed, Is.True);
                Assert.That(text, Does.Contain(GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(newMountPath)));
                Assert.That(text, Does.Not.Contain(GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(oldMountPath)));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }

            public byte[] Unprotect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }
    }

    [TestFixture]
    public class GoogleDriveSecureTokenStoreTests
    {
        [Test]
        public void SaveAndLoad_RoundTripsEncryptedTokens()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_token_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                ObfuscatingProtector protector = new ObfuscatingProtector();
                GoogleDriveSecureTokenStore tokenStore = new GoogleDriveSecureTokenStore(root, protector);
                GoogleDriveTokenSet tokens = new GoogleDriveTokenSet
                {
                    AccessToken = "access-token",
                    RefreshToken = "refresh-token",
                    AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                };

                tokenStore.Save("abc123", tokens);

                string filePath = tokenStore.GetFilePath("abc123");
                string rawText = File.ReadAllText(filePath, Encoding.UTF8);
                GoogleDriveTokenSet? loaded = tokenStore.Load("abc123");

                Assert.That(rawText, Does.Not.Contain("refresh-token"));
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.AccessToken, Is.EqualTo(tokens.AccessToken));
                Assert.That(loaded.RefreshToken, Is.EqualTo(tokens.RefreshToken));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }

            public byte[] Unprotect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }
    }

    [TestFixture]
    public class GoogleDriveSecureClientCredentialStoreTests
    {
        [Test]
        public void SaveAndLoad_RoundTripsEncryptedClientSecret()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_oauth_store_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());

                credentialStore.Save("oauth-key", "desktop-client-secret");

                string filePath = credentialStore.GetFilePath("oauth-key");
                string rawText = File.ReadAllText(filePath, Encoding.UTF8);
                string loadedSecret = credentialStore.Load("oauth-key");

                Assert.That(rawText, Does.Not.Contain("desktop-client-secret"));
                Assert.That(loadedSecret, Is.EqualTo("desktop-client-secret"));
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
        public void TryBuildConfiguration_UsesStoredPerConnectionCredentials()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_oauth_config_" + Guid.NewGuid().ToString("N"));

            try
            {
                GoogleDriveSecureClientCredentialStore credentialStore =
                    new GoogleDriveSecureClientCredentialStore(root, new ObfuscatingProtector());
                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                    OAuthClientSecret = "desktop-client-secret",
                    OAuthClientSecretStoreKey = "oauth-key"
                };

                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(settings, credentialStore);

                bool success = GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                    settings,
                    out GoogleDriveOAuthClientConfiguration configuration,
                    out string errorMessage,
                    credentialStore,
                    allowLegacyFallback: false);

                Assert.That(success, Is.True);
                Assert.That(errorMessage, Is.Empty);
                Assert.That(configuration.ClientId, Is.EqualTo("desktop-client-id.apps.googleusercontent.com"));
                Assert.That(configuration.ClientSecret, Is.EqualTo("desktop-client-secret"));
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
        public void TryBuildConfiguration_FailsWhenClientSecretIsMissing()
        {
            GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
            {
                OAuthClientId = "desktop-client-id.apps.googleusercontent.com",
                OAuthClientSecretStoreKey = "missing-secret"
            };

            bool success = GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                settings,
                out _,
                out string errorMessage,
                new GoogleDriveSecureClientCredentialStore(
                    Path.Combine(Path.GetTempPath(), "drive_oauth_missing_" + Guid.NewGuid().ToString("N")),
                    new ObfuscatingProtector()),
                allowLegacyFallback: false);

            Assert.That(success, Is.False);
            Assert.That(errorMessage, Does.Contain("client secret is missing"));
        }

        private sealed class ObfuscatingProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }

            public byte[] Unprotect(byte[] value)
            {
                return value.Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }
    }

    [TestFixture]
    [NonParallelizable]
    public class GoogleDriveAppOAuthConfigurationTests
    {
        [Test]
        public void TryLoadInstalledConfiguration_ReturnsNullWhenNoLocalJsonPresent()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "oceanya_google_oauth_empty_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempRoot);
                GoogleDriveOAuthClientConfiguration? configuration =
                    GoogleDriveAppOAuthConfiguration.TryLoadInstalledConfiguration(tempRoot);

                Assert.That(configuration, Is.Null);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void Create_UsesEnvironmentOverrideValues()
        {
            const string clientIdVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_ID";
            const string clientSecretVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_SECRET";
            const string clientJsonPathVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_JSON_PATH";
            string? previousClientId = Environment.GetEnvironmentVariable(clientIdVariable);
            string? previousClientSecret = Environment.GetEnvironmentVariable(clientSecretVariable);
            string? previousClientJsonPath = Environment.GetEnvironmentVariable(clientJsonPathVariable);

            try
            {
                Environment.SetEnvironmentVariable(clientIdVariable, "test-client-id.apps.googleusercontent.com");
                Environment.SetEnvironmentVariable(clientSecretVariable, "test-client-secret");
                Environment.SetEnvironmentVariable(clientJsonPathVariable, null);

                GoogleDriveOAuthClientConfiguration configuration = GoogleDriveAppOAuthConfiguration.Create();

                Assert.That(GoogleDriveAppOAuthConfiguration.IsConfigured, Is.True);
                Assert.That(configuration.ClientId, Is.EqualTo("test-client-id.apps.googleusercontent.com"));
                Assert.That(configuration.ClientSecret, Is.EqualTo("test-client-secret"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(clientIdVariable, previousClientId);
                Environment.SetEnvironmentVariable(clientSecretVariable, previousClientSecret);
                Environment.SetEnvironmentVariable(clientJsonPathVariable, previousClientJsonPath);
            }
        }

        [Test]
        public void Create_UsesInstalledClientJsonWhenPresent()
        {
            const string clientIdVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_ID";
            const string clientSecretVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_SECRET";
            const string clientJsonPathVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_JSON_PATH";
            string? previousClientId = Environment.GetEnvironmentVariable(clientIdVariable);
            string? previousClientSecret = Environment.GetEnvironmentVariable(clientSecretVariable);
            string? previousClientJsonPath = Environment.GetEnvironmentVariable(clientJsonPathVariable);
            string tempJsonPath = Path.Combine(Path.GetTempPath(), "oceanya_google_oauth_" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                File.WriteAllText(
                    tempJsonPath,
                    """
                    {
                      "installed": {
                        "client_id": "json-client-id.apps.googleusercontent.com",
                        "client_secret": "json-client-secret"
                      }
                    }
                    """);
                Environment.SetEnvironmentVariable(clientIdVariable, null);
                Environment.SetEnvironmentVariable(clientSecretVariable, null);
                Environment.SetEnvironmentVariable(clientJsonPathVariable, tempJsonPath);

                GoogleDriveOAuthClientConfiguration configuration = GoogleDriveAppOAuthConfiguration.Create();

                Assert.That(configuration.ClientId, Is.EqualTo("json-client-id.apps.googleusercontent.com"));
                Assert.That(configuration.ClientSecret, Is.EqualTo("json-client-secret"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(clientIdVariable, previousClientId);
                Environment.SetEnvironmentVariable(clientSecretVariable, previousClientSecret);
                Environment.SetEnvironmentVariable(clientJsonPathVariable, previousClientJsonPath);
                if (File.Exists(tempJsonPath))
                {
                    File.Delete(tempJsonPath);
                }
            }
        }

        [Test]
        public void TryLoadInstalledConfiguration_UsesDefaultFeaturesSubdirectoryPath()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "oceanya_google_oauth_dir_" + Guid.NewGuid().ToString("N"));
            string featuresDirectory = Path.Combine(tempRoot, "Features", "GoogleDriveSync");
            string jsonPath = Path.Combine(featuresDirectory, "google-drive-oauth.local.json");

            try
            {
                Directory.CreateDirectory(featuresDirectory);
                File.WriteAllText(
                    jsonPath,
                    """
                    {
                      "installed": {
                        "client_id": "default-dir-client-id.apps.googleusercontent.com",
                        "client_secret": "default-dir-client-secret"
                      }
                    }
                    """);

                GoogleDriveOAuthClientConfiguration? configuration =
                    GoogleDriveAppOAuthConfiguration.TryLoadInstalledConfiguration(tempRoot);

                Assert.That(configuration, Is.Not.Null);
                Assert.That(configuration!.ClientId, Is.EqualTo("default-dir-client-id.apps.googleusercontent.com"));
                Assert.That(configuration.ClientSecret, Is.EqualTo("default-dir-client-secret"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }

    [TestFixture]
    public class FileHivemindSaveMigrationTests
    {
        [Test]
        public void NormalizeLoadedData_MigratesLegacyGoogleDriveProfileIntoFileHivemind()
        {
            SaveData data = new SaveData
            {
                StartupFunctionalityId = "google_drive_sync",
                GoogleDriveSync = new GoogleDriveSyncSettings
                {
                    RemoteFolderId = "drive-folder-id",
                    RemoteFolderName = "Campaign Assets",
                    LocalFolderPath = "/tmp/hivemind",
                    LastSignedInEmail = "tester@example.com"
                },
                FileHivemind = new FileHivemindSettings()
            };

            MethodInfo? normalizeMethod = typeof(SaveFile).GetMethod(
                "NormalizeLoadedData",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(normalizeMethod, Is.Not.Null);

            normalizeMethod!.Invoke(null, new object[] { data });

            Assert.That(data.StartupFunctionalityId, Is.EqualTo("oceanyan_file_hivemind"));
            Assert.That(data.FileHivemind.Connections.Count, Is.EqualTo(1));
            Assert.That(data.FileHivemind.Connections[0].ProviderId, Is.EqualTo(FileHivemindProviderIds.GoogleDrive));
            Assert.That(data.FileHivemind.Connections[0].GoogleDrive.RemoteFolderId, Is.EqualTo("drive-folder-id"));
            Assert.That(data.FileHivemind.SelectedConnectionId, Is.EqualTo(data.FileHivemind.Connections[0].Id));
        }

        [Test]
        public void NormalizeLoadedData_DefaultsDesktopToastsOnWhenPreferenceWasNeverConfigured()
        {
            SaveData data = new SaveData
            {
                FileHivemind = new FileHivemindSettings
                {
                    ShowDesktopToasts = false,
                    DesktopToastPreferenceConfigured = false
                }
            };

            MethodInfo? normalizeMethod = typeof(SaveFile).GetMethod(
                "NormalizeLoadedData",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(normalizeMethod, Is.Not.Null);

            normalizeMethod!.Invoke(null, new object[] { data });

            Assert.That(data.FileHivemind.ShowDesktopToasts, Is.True);
            Assert.That(data.FileHivemind.DesktopToastPreferenceConfigured, Is.True);
        }
    }

    [TestFixture]
    public class GoogleDriveApiClientTimeoutTests
    {
        [Test]
        public void GetFolderNameAsync_WhenRequestStalls_ThrowsHelpfulTimeout()
        {
            using HttpClient httpClient = new HttpClient(new NeverRespondingHandler());
            GoogleDriveApiClient client = new GoogleDriveApiClient(
                httpClient,
                "access-token",
                TimeSpan.FromMilliseconds(50));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.GetFolderNameAsync("folder-id", CancellationToken.None))!;

            Assert.That(exception.Message, Does.Contain("timed out"));
            Assert.That(exception.Message, Does.Contain("network may have stalled or disconnected"));
        }

        private sealed class NeverRespondingHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The request should have been cancelled before completion.");
            }
        }
    }

    [TestFixture]
    public class GoogleDriveSyncPlannerTests
    {
        [Test]
        public void BuildPullPlan_DownloadsMissingFilesAndDeletesStaleLocalEntries()
        {
            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();
            remote.Folders["characters"] = new GoogleDriveSyncFolderEntry { RelativePath = "characters", ItemId = "folder-characters" };
            remote.Files["characters/test.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "characters/test.txt",
                ItemId = "file-1",
                Size = 10,
                Hash = "aaa"
            };

            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();
            local.Folders["obsolete"] = new GoogleDriveSyncFolderEntry { RelativePath = "obsolete" };
            local.Files["obsolete/old.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "obsolete/old.txt",
                Size = 5,
                Hash = "bbb"
            };

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildPullPlan(remote, local, mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.EnsureLocalDirectory
                && operation.RelativePath == "characters"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DownloadFile
                && operation.RelativePath == "characters/test.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalFile
                && operation.RelativePath == "obsolete/old.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalDirectory
                && operation.RelativePath == "obsolete"), Is.True);
        }

        [Test]
        public void BuildPushPlan_CreatesFoldersUploadsChangedFilesAndDeletesMissingRemoteEntries()
        {
            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();
            local.Folders["background"] = new GoogleDriveSyncFolderEntry { RelativePath = "background" };
            local.Files["background/scene.png"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "background/scene.png",
                Size = 20,
                Hash = "ccc"
            };

            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();
            remote.Folders["obsolete"] = new GoogleDriveSyncFolderEntry { RelativePath = "obsolete", ItemId = "folder-obsolete" };
            remote.Files["obsolete/old.png"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "obsolete/old.png",
                ItemId = "file-obsolete",
                Size = 30,
                Hash = "ddd"
            };

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildPushPlan(local, remote, mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.EnsureRemoteDirectory
                && operation.RelativePath == "background"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.UploadFile
                && operation.RelativePath == "background/scene.png"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteFile
                && operation.RelativePath == "obsolete/old.png"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteDirectory
                && operation.RelativePath == "obsolete"), Is.True);
        }

        [Test]
        public void BuildPullPlan_DownloadsChangedFilesWhenHashesDiffer()
        {
            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();
            remote.Files["characters/phoenix/char.ini"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "characters/phoenix/char.ini",
                ItemId = "remote-char",
                Size = 128,
                Hash = "remote-hash"
            };

            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();
            local.Files["characters/phoenix/char.ini"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "characters/phoenix/char.ini",
                ItemId = "local-char",
                Size = 128,
                Hash = "local-hash"
            };

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildPullPlan(remote, local, mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DownloadFile
                && operation.RelativePath == "characters/phoenix/char.ini"), Is.True);
        }

        [Test]
        public void BuildBidirectionalPlan_ReconcilesDisjointChangesWithoutCrossDeleting()
        {
            GoogleDriveLocalMirrorState localBaseline = new GoogleDriveLocalMirrorState();
            localBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "shared.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveLocalMirrorState remoteBaseline = new GoogleDriveLocalMirrorState();
            remoteBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "shared.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();
            local.Files["shared.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "shared.txt",
                Size = 4,
                Hash = "base"
            };
            local.Files["local-only.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "local-only.txt",
                Size = 5,
                Hash = "local"
            };

            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();
            remote.Files["shared.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "shared.txt",
                ItemId = "shared-id",
                Size = 4,
                Hash = "base"
            };
            remote.Files["remote-only.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "remote-only.txt",
                ItemId = "remote-id",
                Size = 6,
                Hash = "remote"
            };

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildBidirectionalPlan(
                localBaseline,
                remoteBaseline,
                local,
                remote,
                mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.UploadFile
                && operation.RelativePath == "local-only.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DownloadFile
                && operation.RelativePath == "remote-only.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalFile
                || operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteFile), Is.False);
        }

        [Test]
        public void BuildBidirectionalPlan_PrefersRemoteVersionForDirectFileConflict()
        {
            GoogleDriveLocalMirrorState localBaseline = new GoogleDriveLocalMirrorState();
            localBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "shared.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveLocalMirrorState remoteBaseline = new GoogleDriveLocalMirrorState();
            remoteBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "shared.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();
            local.Files["shared.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "shared.txt",
                Size = 5,
                Hash = "local"
            };

            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();
            remote.Files["shared.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "shared.txt",
                ItemId = "shared-id",
                Size = 6,
                Hash = "remote"
            };

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildBidirectionalPlan(
                localBaseline,
                remoteBaseline,
                local,
                remote,
                mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DownloadFile
                && operation.RelativePath == "shared.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.UploadFile
                && operation.RelativePath == "shared.txt"), Is.False);
        }

        [Test]
        public void BuildBidirectionalPlan_PropagatesLocalDeletionWhilePreservingUnrelatedRemoteAddition()
        {
            GoogleDriveLocalMirrorState localBaseline = new GoogleDriveLocalMirrorState();
            localBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "delete-me.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveLocalMirrorState remoteBaseline = new GoogleDriveLocalMirrorState();
            remoteBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "delete-me.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();

            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();
            remote.Files["delete-me.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "delete-me.txt",
                ItemId = "delete-id",
                Size = 4,
                Hash = "base"
            };
            remote.Files["remote-only.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "remote-only.txt",
                ItemId = "remote-id",
                Size = 6,
                Hash = "remote"
            };

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildBidirectionalPlan(
                localBaseline,
                remoteBaseline,
                local,
                remote,
                mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteFile
                && operation.RelativePath == "delete-me.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DownloadFile
                && operation.RelativePath == "remote-only.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalFile
                && operation.RelativePath == "remote-only.txt"), Is.False);
        }

        [Test]
        public void BuildBidirectionalPlan_PropagatesRemoteDeletionWhilePreservingUnrelatedLocalAddition()
        {
            GoogleDriveLocalMirrorState localBaseline = new GoogleDriveLocalMirrorState();
            localBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "delete-me.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveLocalMirrorState remoteBaseline = new GoogleDriveLocalMirrorState();
            remoteBaseline.Files.Add(new GoogleDriveLocalMirrorFileState
            {
                RelativePath = "delete-me.txt",
                Size = 4,
                ContentHash = "base"
            });

            GoogleDriveSyncSnapshot local = new GoogleDriveSyncSnapshot();
            local.Files["delete-me.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "delete-me.txt",
                Size = 4,
                Hash = "base"
            };
            local.Files["local-only.txt"] = new GoogleDriveSyncFileEntry
            {
                RelativePath = "local-only.txt",
                Size = 5,
                Hash = "local"
            };

            GoogleDriveSyncSnapshot remote = new GoogleDriveSyncSnapshot();

            GoogleDriveSyncPlan plan = GoogleDriveSyncPlanner.BuildBidirectionalPlan(
                localBaseline,
                remoteBaseline,
                local,
                remote,
                mirrorDeletes: true);

            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteLocalFile
                && operation.RelativePath == "delete-me.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.UploadFile
                && operation.RelativePath == "local-only.txt"), Is.True);
            Assert.That(plan.Operations.Any(operation =>
                operation.Kind == GoogleDriveSyncOperationKind.DeleteRemoteFile
                && operation.RelativePath == "local-only.txt"), Is.False);
        }
    }

    [TestFixture]
    public class GoogleDriveSyncServiceTests
    {
        [Test]
        public async Task PullFromDriveAsync_DownloadsRemoteFilesToLocalFolder()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_pull_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");
                client.AddFile("characters/phoenix/char.ini", "file-char", "name=Phoenix");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PullFromDriveAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                string charIniPath = Path.Combine(root, "characters", "phoenix", "char.ini");
                Assert.That(File.Exists(charIniPath), Is.True);
                Assert.That(File.ReadAllText(charIniPath), Is.EqualTo("name=Phoenix"));
                Assert.That(summary.FilesDownloaded, Is.EqualTo(1));
                Assert.That(summary.LocalChanges.AddedOrUpdatedPaths, Contains.Item("characters/phoenix/char.ini"));
                Assert.That(settings.LastSyncUtc.HasValue, Is.True);
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
        public async Task PullFromDriveAsync_IgnoresRemoteManagedFolderMarkerFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_pull_marker_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFile("desktop.ini", "desktop-id", "marker");
                client.AddFile(GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName, "icon-id", "icon");
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");
                client.AddFile("characters/phoenix/char.ini", "file-char", "name=Phoenix");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PullFromDriveAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                Assert.That(File.Exists(Path.Combine(root, "desktop.ini")), Is.False);
                Assert.That(File.Exists(Path.Combine(root, GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(root, "characters", "phoenix", "char.ini")), Is.True);
                Assert.That(summary.FilesDownloaded, Is.EqualTo(1));
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
        public async Task PullFromDriveAsync_CapturesFinalRemoteSnapshotForPostSyncRuntimeState()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_pull_snapshot_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");
                client.AddFile("characters/phoenix/char.ini", "file-char", "name=Phoenix");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PullFromDriveAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                Assert.That(summary.FinalRemoteSnapshot, Is.Not.Null);
                Assert.That(summary.FinalRemoteSnapshot!.Folders.ContainsKey("characters"), Is.True);
                Assert.That(summary.FinalRemoteSnapshot.Files.ContainsKey("characters/phoenix/char.ini"), Is.True);
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
        public async Task PushLocalFolderAsync_UploadsFilesAndDeletesObsoleteRemoteEntries()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_push_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                string backgroundDirectory = Path.Combine(root, "background");
                Directory.CreateDirectory(backgroundDirectory);
                File.WriteAllText(Path.Combine(backgroundDirectory, "scene.png"), "updated-scene");

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFolder("obsolete", "folder-obsolete");
                client.AddFile("obsolete/old.txt", "file-old", "old");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PushLocalFolderAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                Assert.That(client.Snapshot.Folders.ContainsKey("background"), Is.True);
                Assert.That(client.Snapshot.Files.ContainsKey("background/scene.png"), Is.True);
                Assert.That(client.GetFileText("background/scene.png"), Is.EqualTo("updated-scene"));
                Assert.That(client.Snapshot.Files.ContainsKey("obsolete/old.txt"), Is.False);
                Assert.That(summary.FilesUploaded, Is.EqualTo(1));
                Assert.That(summary.RemoteFilesDeleted, Is.EqualTo(1));
                Assert.That(summary.LocalChanges.AddedOrUpdatedPaths, Contains.Item("background/scene.png"));
                Assert.That(summary.LocalChanges.DeletedPaths, Contains.Item("obsolete/old.txt"));
                Assert.That(summary.KnownRemoteItemIds.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(summary.KnownRemoteItemIds, Has.Some.StartsWith("folder-"));
                Assert.That(summary.KnownRemoteItemIds, Has.Some.StartsWith("file-"));
                Assert.That(summary.FinalRemoteSnapshot, Is.Not.Null);
                Assert.That(summary.FinalRemoteSnapshot!.Folders.ContainsKey("background"), Is.True);
                Assert.That(summary.FinalRemoteSnapshot.Files.ContainsKey("background/scene.png"), Is.True);
                Assert.That(summary.FinalRemoteSnapshot.Files.ContainsKey("obsolete/old.txt"), Is.False);
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
        public async Task BuildRuntimeStateAfterSyncAsync_UsesSummarySnapshotWithoutRequestingAnotherRemoteTreeRead()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_runtime_state_after_sync_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");
                client.AddFile("characters/phoenix/char.ini", "file-char", "name=Phoenix");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PullFromDriveAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                client.ResetCallCounters();

                GoogleDriveConnectionRuntimeState runtimeState = await service.BuildRuntimeStateAfterSyncAsync(
                    client,
                    "connection-1",
                    settings,
                    summary,
                    CancellationToken.None);

                Assert.That(client.GetSnapshotCallCount, Is.EqualTo(0));
                Assert.That(client.GetStartPageTokenCallCount, Is.EqualTo(1));
                Assert.That(runtimeState.KnownRemoteItemIds, Contains.Item("folder-characters"));
                Assert.That(runtimeState.KnownRemoteItemIds, Contains.Item("file-char"));
                Assert.That(runtimeState.LocalMirrorState.Files.Any(file =>
                    string.Equals(file.RelativePath, "characters/phoenix/char.ini", StringComparison.OrdinalIgnoreCase)), Is.True);
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
        public async Task MergeWithDriveAsync_ReconcilesDisjointLocalAndRemoteChanges()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_merge_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "shared.txt"), "base");

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFile("shared.txt", "file-shared", "base");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveLocalMirrorState localBaseline = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);
                GoogleDriveLocalMirrorState remoteBaseline = GoogleDriveLocalMirrorStateSupport.FromSnapshot(client.Snapshot);

                File.WriteAllText(Path.Combine(root, "local-only.txt"), "local");
                client.AddFile("remote-only.txt", "file-remote", "remote");

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.MergeWithDriveAsync(
                    client,
                    settings,
                    localBaseline,
                    remoteBaseline,
                    _ => { },
                    CancellationToken.None);

                Assert.That(File.ReadAllText(Path.Combine(root, "local-only.txt")), Is.EqualTo("local"));
                Assert.That(File.ReadAllText(Path.Combine(root, "remote-only.txt")), Is.EqualTo("remote"));
                Assert.That(client.Snapshot.Files.ContainsKey("local-only.txt"), Is.True);
                Assert.That(client.GetFileText("local-only.txt"), Is.EqualTo("local"));
                Assert.That(summary.FilesUploaded, Is.EqualTo(1));
                Assert.That(summary.FilesDownloaded, Is.EqualTo(1));
                Assert.That(summary.RemoteFilesDeleted, Is.EqualTo(0));
                Assert.That(summary.LocalFilesDeleted, Is.EqualTo(0));
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
        public async Task MergeWithDriveAsync_RemoteVersionWinsWhenBothSidesEditSameFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_merge_conflict_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string sharedPath = Path.Combine(root, "shared.txt");
                File.WriteAllText(sharedPath, "base");

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFile("shared.txt", "file-shared", "base");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveLocalMirrorState localBaseline = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);
                GoogleDriveLocalMirrorState remoteBaseline = GoogleDriveLocalMirrorStateSupport.FromSnapshot(client.Snapshot);

                File.WriteAllText(sharedPath, "local");
                client.AddFile("shared.txt", "file-shared", "remote");

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.MergeWithDriveAsync(
                    client,
                    settings,
                    localBaseline,
                    remoteBaseline,
                    _ => { },
                    CancellationToken.None);

                Assert.That(File.ReadAllText(sharedPath), Is.EqualTo("remote"));
                Assert.That(client.GetFileText("shared.txt"), Is.EqualTo("remote"));
                Assert.That(summary.FilesDownloaded, Is.EqualTo(1));
                Assert.That(summary.FilesUploaded, Is.EqualTo(0));
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
        public async Task MergeWithDriveAsync_RemoteDeletionWinsWhenBothSidesChangeSameFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_merge_delete_conflict_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string sharedPath = Path.Combine(root, "shared.txt");
                File.WriteAllText(sharedPath, "base");

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFile("shared.txt", "file-shared", "base");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveLocalMirrorState localBaseline = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);
                GoogleDriveLocalMirrorState remoteBaseline = GoogleDriveLocalMirrorStateSupport.FromSnapshot(client.Snapshot);

                File.WriteAllText(sharedPath, "local");
                await client.DeleteItemAsync("file-shared", CancellationToken.None);

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.MergeWithDriveAsync(
                    client,
                    settings,
                    localBaseline,
                    remoteBaseline,
                    _ => { },
                    CancellationToken.None);

                Assert.That(File.Exists(sharedPath), Is.False);
                Assert.That(client.Snapshot.Files.ContainsKey("shared.txt"), Is.False);
                Assert.That(summary.LocalFilesDeleted, Is.EqualTo(1));
                Assert.That(summary.FilesUploaded, Is.EqualTo(0));
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
        public async Task MergeWithDriveAsync_PropagatesLocalDeletionWithoutMirroringUnrelatedRemoteAddition()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_merge_local_delete_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string deletePath = Path.Combine(root, "delete-me.txt");
                File.WriteAllText(deletePath, "base");

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFile("delete-me.txt", "file-delete", "base");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveLocalMirrorState localBaseline = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);
                GoogleDriveLocalMirrorState remoteBaseline = GoogleDriveLocalMirrorStateSupport.FromSnapshot(client.Snapshot);

                File.Delete(deletePath);
                client.AddFile("remote-only.txt", "file-remote", "remote");

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.MergeWithDriveAsync(
                    client,
                    settings,
                    localBaseline,
                    remoteBaseline,
                    _ => { },
                    CancellationToken.None);

                Assert.That(File.Exists(deletePath), Is.False);
                Assert.That(client.Snapshot.Files.ContainsKey("delete-me.txt"), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(root, "remote-only.txt")), Is.EqualTo("remote"));
                Assert.That(client.Snapshot.Files.ContainsKey("remote-only.txt"), Is.True);
                Assert.That(summary.RemoteFilesDeleted, Is.EqualTo(1));
                Assert.That(summary.FilesDownloaded, Is.EqualTo(1));
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
        public async Task MergeWithDriveAsync_PropagatesRemoteDeletionWithoutMirroringUnrelatedLocalAddition()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_merge_remote_delete_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string deletePath = Path.Combine(root, "delete-me.txt");
                File.WriteAllText(deletePath, "base");

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient();
                client.AddFile("delete-me.txt", "file-delete", "base");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveLocalMirrorState localBaseline = GoogleDriveLocalMirrorStateSupport.CaptureExact(root);
                GoogleDriveLocalMirrorState remoteBaseline = GoogleDriveLocalMirrorStateSupport.FromSnapshot(client.Snapshot);

                File.WriteAllText(Path.Combine(root, "local-only.txt"), "local");
                await client.DeleteItemAsync("file-delete", CancellationToken.None);

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.MergeWithDriveAsync(
                    client,
                    settings,
                    localBaseline,
                    remoteBaseline,
                    _ => { },
                    CancellationToken.None);

                Assert.That(File.Exists(deletePath), Is.False);
                Assert.That(client.Snapshot.Files.ContainsKey("delete-me.txt"), Is.False);
                Assert.That(client.GetFileText("local-only.txt"), Is.EqualTo("local"));
                Assert.That(summary.LocalFilesDeleted, Is.EqualTo(1));
                Assert.That(summary.FilesUploaded, Is.EqualTo(1));
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
        public async Task PullFromDriveAsync_DownloadsMultipleFilesInParallel()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_pull_parallel_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient
                {
                    DownloadDelay = TimeSpan.FromMilliseconds(60)
                };
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");
                for (int i = 0; i < 6; i++)
                {
                    client.AddFile($"characters/phoenix/file{i}.txt", "file-" + i, "content-" + i);
                }

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PullFromDriveAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                Assert.That(summary.FilesDownloaded, Is.EqualTo(6));
                Assert.That(client.MaxConcurrentDownloads, Is.GreaterThan(1));
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
        /// R-016 — a cancelled pull must surface cancellation promptly instead of hanging
        /// indefinitely after a mid-transfer interruption.
        /// </summary>
        [Test]
        public void PullFromDriveAsync_CancellationDuringDownload_ThrowsWithoutHanging()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_pull_cancel_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient
                {
                    DownloadDelay = TimeSpan.FromMilliseconds(300)
                };
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");
                for (int index = 0; index < 6; index++)
                {
                    client.AddFile($"characters/phoenix/file{index}.txt", "file-" + index, "content-" + index);
                }

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(75));
                DateTime utcStart = DateTime.UtcNow;

                Assert.That(
                    async () => await service.PullFromDriveAsync(
                        client,
                        settings,
                        _ => { },
                        cancellationTokenSource.Token),
                    Throws.InstanceOf<OperationCanceledException>());

                TimeSpan elapsed = DateTime.UtcNow - utcStart;
                Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                    "Cancelled Drive pull must unblock promptly instead of hanging");
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
        public async Task PushLocalFolderAsync_UploadsMultipleFilesInParallel()
        {
            string root = Path.Combine(Path.GetTempPath(), "drive_push_parallel_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "characters", "phoenix"));
                for (int i = 0; i < 6; i++)
                {
                    File.WriteAllText(Path.Combine(root, "characters", "phoenix", $"file{i}.txt"), "content-" + i);
                }

                FakeGoogleDriveRemoteClient client = new FakeGoogleDriveRemoteClient
                {
                    UploadDelay = TimeSpan.FromMilliseconds(60)
                };
                client.AddFolder("characters", "folder-characters");
                client.AddFolder("characters/phoenix", "folder-phoenix");

                GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings
                {
                    LocalFolderPath = root,
                    RemoteFolderId = client.RootFolderId,
                    MirrorDeletes = true
                };

                GoogleDriveSyncService service = new GoogleDriveSyncService();
                GoogleDriveSyncSummary summary = await service.PushLocalFolderAsync(
                    client,
                    settings,
                    _ => { },
                    CancellationToken.None);

                Assert.That(summary.FilesUploaded, Is.EqualTo(6));
                Assert.That(client.MaxConcurrentUploads, Is.GreaterThan(1));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private sealed class FakeGoogleDriveRemoteClient : IGoogleDriveRemoteClient
        {
            private readonly Dictionary<string, string> fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> folderIdsToRelativePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Queue<GoogleDriveChangePage> changePages = new Queue<GoogleDriveChangePage>();
            private int nextId = 1;
            private int currentConcurrentDownloads;
            private int currentConcurrentUploads;

            public FakeGoogleDriveRemoteClient()
            {
                RootFolderId = "root";
                folderIdsToRelativePaths[RootFolderId] = string.Empty;
            }

            public string RootFolderId { get; }
            public string StartPageToken { get; set; } = "token-0";
            public TimeSpan DownloadDelay { get; set; } = TimeSpan.Zero;
            public TimeSpan UploadDelay { get; set; } = TimeSpan.Zero;
            public int MaxConcurrentDownloads { get; private set; }
            public int MaxConcurrentUploads { get; private set; }
            public int GetSnapshotCallCount { get; private set; }
            public int GetStartPageTokenCallCount { get; private set; }

            public GoogleDriveSyncSnapshot Snapshot { get; } = new GoogleDriveSyncSnapshot();

            public void ResetCallCounters()
            {
                GetSnapshotCallCount = 0;
                GetStartPageTokenCallCount = 0;
            }

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
                string parentRelativePath = GetParentRelativePath(relativePath);
                string parentId = string.IsNullOrWhiteSpace(parentRelativePath)
                    ? RootFolderId
                    : Snapshot.Folders[parentRelativePath].ItemId;
                string tempFile = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(tempFile, content);
                    Snapshot.Files[relativePath] = new GoogleDriveSyncFileEntry
                    {
                        RelativePath = relativePath,
                        ItemId = fileId,
                        ParentId = parentId,
                        Size = new FileInfo(tempFile).Length,
                        Hash = GoogleDriveLocalSnapshotBuilder.ComputeMd5(tempFile)
                    };
                }
                finally
                {
                    File.Delete(tempFile);
                }

                fileContents[relativePath] = content;
            }

            public string GetFileText(string relativePath)
            {
                return fileContents[relativePath];
            }

            public void QueueChangePage(GoogleDriveChangePage page)
            {
                changePages.Enqueue(page);
            }

            public Task<GoogleDriveUserInfo> GetCurrentUserAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new GoogleDriveUserInfo
                {
                    DisplayName = "Tester",
                    EmailAddress = "tester@example.com"
                });
            }

            public Task<string> GetFolderNameAsync(string folderId, CancellationToken cancellationToken)
            {
                if (string.Equals(folderId, RootFolderId, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult("Root Folder");
                }

                string relativePath = folderIdsToRelativePaths.TryGetValue(folderId, out string? value)
                    ? value
                    : string.Empty;
                string folderName = string.IsNullOrWhiteSpace(relativePath)
                    ? "Root Folder"
                    : Path.GetFileName(relativePath.Replace('/', Path.DirectorySeparatorChar));
                return Task.FromResult(folderName);
            }

            public Task<string> GetStartPageTokenAsync(CancellationToken cancellationToken)
            {
                GetStartPageTokenCallCount++;
                return Task.FromResult(StartPageToken);
            }

            public Task<GoogleDriveChangePage> GetChangesAsync(string pageToken, CancellationToken cancellationToken)
            {
                if (changePages.Count == 0)
                {
                    return Task.FromResult(new GoogleDriveChangePage
                    {
                        NewStartPageToken = pageToken
                    });
                }

                return Task.FromResult(changePages.Dequeue());
            }

            public Task<GoogleDriveSyncSnapshot> GetSnapshotAsync(string rootFolderId, CancellationToken cancellationToken)
            {
                GetSnapshotCallCount++;
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
                string parentRelativePath = string.IsNullOrWhiteSpace(parentFolderId)
                    ? string.Empty
                    : folderIdsToRelativePaths[parentFolderId];
                string relativePath = string.IsNullOrWhiteSpace(parentRelativePath)
                    ? folderName
                    : parentRelativePath + "/" + folderName;
                string id = "folder-" + nextId++;

                AddFolder(relativePath, id);
                return Task.FromResult(new GoogleDriveSyncFolderEntry
                {
                    RelativePath = relativePath,
                    ItemId = id
                });
            }

            public Task<string> UploadFileAsync(string parentFolderId, string fileName, string localFilePath, string? existingFileId, CancellationToken cancellationToken)
            {
                return UploadFileCoreAsync(parentFolderId, fileName, localFilePath, existingFileId, cancellationToken);
            }

            public Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken)
            {
                string relativePath = Snapshot.Files.First(pair => string.Equals(pair.Value.ItemId, fileId, StringComparison.OrdinalIgnoreCase)).Key;
                return DownloadFileCoreAsync(relativePath, destinationPath, cancellationToken);
            }

            public Task DeleteItemAsync(string itemId, CancellationToken cancellationToken)
            {
                KeyValuePair<string, GoogleDriveSyncFileEntry>? fileMatch = Snapshot.Files
                    .FirstOrDefault(pair => string.Equals(pair.Value.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(fileMatch?.Key))
                {
                    fileContents.Remove(fileMatch.Value.Key);
                    Snapshot.Files.Remove(fileMatch.Value.Key);
                    return Task.CompletedTask;
                }

                KeyValuePair<string, GoogleDriveSyncFolderEntry>? folderMatch = Snapshot.Folders
                    .FirstOrDefault(pair => string.Equals(pair.Value.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(folderMatch?.Key))
                {
                    folderIdsToRelativePaths.Remove(folderMatch.Value.Value.ItemId);
                    Snapshot.Folders.Remove(folderMatch.Value.Key);
                }

                return Task.CompletedTask;
            }

            private static string GetParentRelativePath(string relativePath)
            {
                int lastSlash = relativePath.LastIndexOf('/');
                return lastSlash < 0 ? string.Empty : relativePath[..lastSlash];
            }

            private async Task DownloadFileCoreAsync(string relativePath, string destinationPath, CancellationToken cancellationToken)
            {
                int concurrentDownloads = Interlocked.Increment(ref currentConcurrentDownloads);
                if (concurrentDownloads > MaxConcurrentDownloads)
                {
                    MaxConcurrentDownloads = concurrentDownloads;
                }

                try
                {
                    if (DownloadDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(DownloadDelay, cancellationToken);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? string.Empty);
                    await File.WriteAllTextAsync(destinationPath, fileContents[relativePath], cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref currentConcurrentDownloads);
                }
            }

            private async Task<string> UploadFileCoreAsync(
                string parentFolderId,
                string fileName,
                string localFilePath,
                string? existingFileId,
                CancellationToken cancellationToken)
            {
                int concurrentUploads = Interlocked.Increment(ref currentConcurrentUploads);
                if (concurrentUploads > MaxConcurrentUploads)
                {
                    MaxConcurrentUploads = concurrentUploads;
                }

                try
                {
                    if (UploadDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(UploadDelay, cancellationToken);
                    }

                    string parentRelativePath = folderIdsToRelativePaths[parentFolderId];
                    string relativePath = string.IsNullOrWhiteSpace(parentRelativePath)
                        ? fileName
                        : parentRelativePath + "/" + fileName;
                    string itemId = string.IsNullOrWhiteSpace(existingFileId) ? "file-" + nextId++ : existingFileId;
                    string content = await File.ReadAllTextAsync(localFilePath, cancellationToken);
                    fileContents[relativePath] = content;
                    Snapshot.Files[relativePath] = new GoogleDriveSyncFileEntry
                    {
                        RelativePath = relativePath,
                        ItemId = itemId,
                        ParentId = parentFolderId,
                        Size = new FileInfo(localFilePath).Length,
                        Hash = GoogleDriveLocalSnapshotBuilder.ComputeMd5(localFilePath)
                    };

                    return itemId;
                }
                finally
                {
                    Interlocked.Decrement(ref currentConcurrentUploads);
                }
            }
        }
    }

    [TestFixture]
    public class ClientAssetRefreshServiceTests
    {
        private string tempRoot = string.Empty;
        private string configDirectory = string.Empty;
        private string syncRoot = string.Empty;
        private List<string> originalBaseFolders = new List<string>();
        private string originalConfigPath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            originalBaseFolders = new List<string>(Globals.BaseFolders);
            originalConfigPath = Globals.PathToConfigINI;

            tempRoot = Path.Combine(Path.GetTempPath(), "client_refresh_test_" + Guid.NewGuid().ToString("N"));
            configDirectory = Path.Combine(tempRoot, "config");
            syncRoot = Path.Combine(tempRoot, "sync");
            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(syncRoot);

            string configPath = Path.Combine(configDirectory, "config.ini");
            File.WriteAllText(
                configPath,
                "mount_paths=" + syncRoot + Environment.NewLine + "log_maximum=20");

            CreateCharacter(syncRoot, "Phoenix", "Phoenix");
            CreateBackground(syncRoot, "Courtroom");

            Globals.UpdateConfigINI(configPath);
            ResetCharacterCache();
            ResetBackgroundCache();
            CharacterFolder.RefreshCharacterList();
            Background.RefreshCache();
        }

        [TearDown]
        public void TearDown()
        {
            Globals.BaseFolders = originalBaseFolders;
            Globals.PathToConfigINI = originalConfigPath;
            ResetCharacterCache();
            ResetBackgroundCache();

            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public void BuildTargetedPlan_GroupsChangedPathsByAssetType()
        {
            GoogleDriveSyncLocalChangeSet changes = new GoogleDriveSyncLocalChangeSet();
            changes.RecordAddedOrUpdated("characters/Phoenix/char.ini");
            changes.RecordAddedOrUpdated("background/Courtroom/defenseempty.png");
            changes.RecordAddedOrUpdated("sounds/blips/ping.ogg");
            changes.RecordDeleted("misc/chatbox/config.ini");

            TargetedAssetRefreshPlan plan = ClientAssetRefreshService.BuildTargetedPlan(changes);

            Assert.That(plan.CharacterNames, Contains.Item("Phoenix"));
            Assert.That(plan.BackgroundNames, Contains.Item("Courtroom"));
            Assert.That(plan.RefreshBlips, Is.True);
            Assert.That(plan.RefreshChats, Is.True);
            Assert.That(plan.RefreshEffects, Is.True);
            Assert.That(plan.RequiresFullCharacterRefresh, Is.False);
            Assert.That(plan.RequiresFullBackgroundRefresh, Is.False);
        }

        [Test]
        public void BuildConfiguredBaseFolderSignature_UsesConfiguredMountEntriesWithoutRequiringThemToExist()
        {
            string configPath = Path.Combine(configDirectory, "config.ini");
            string existingMount = Path.Combine(tempRoot, "mounted_existing");
            string missingMount = Path.Combine(tempRoot, "mounted_missing");
            Directory.CreateDirectory(existingMount);
            File.WriteAllText(
                configPath,
                "mount_paths="
                + GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(existingMount)
                + ","
                + GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(missingMount)
                + Environment.NewLine
                + "log_maximum=20");

            List<string> signature = ClientAssetRefreshService.BuildConfiguredBaseFolderSignature(configPath);

            Assert.That(
                signature,
                Is.EqualTo(new[]
                {
                    NormalizeComparisonPath(missingMount),
                    NormalizeComparisonPath(existingMount),
                    NormalizeComparisonPath(configDirectory)
                }));
        }

        [Test]
        public void BuildTrackedChangePlan_DetectsOnlyChangedAssetScopes()
        {
            AssetRefreshStateSnapshot previous = new AssetRefreshStateSnapshot
            {
                Characters = new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Phoenix"] = new AssetTrackedFolderState
                    {
                        Name = "Phoenix",
                        DirectoryPath = @"C:\sync\characters\Phoenix",
                        Signature = "char-old"
                    },
                    ["Maya"] = new AssetTrackedFolderState
                    {
                        Name = "Maya",
                        DirectoryPath = @"C:\sync\characters\Maya",
                        Signature = "maya-same"
                    }
                },
                Backgrounds = new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Courtroom"] = new AssetTrackedFolderState
                    {
                        Name = "Courtroom",
                        DirectoryPath = @"C:\sync\background\Courtroom",
                        Signature = "bg-same"
                    }
                },
                BlipsSignature = "blips-old",
                ChatsSignature = "chats-same",
                EffectsSignature = "effects-same"
            };
            AssetRefreshStateSnapshot current = new AssetRefreshStateSnapshot
            {
                Characters = new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Phoenix"] = new AssetTrackedFolderState
                    {
                        Name = "Phoenix",
                        DirectoryPath = @"C:\sync\characters\Phoenix",
                        Signature = "char-new"
                    },
                    ["Maya"] = new AssetTrackedFolderState
                    {
                        Name = "Maya",
                        DirectoryPath = @"C:\sync\characters\Maya",
                        Signature = "maya-same"
                    }
                },
                Backgrounds = new Dictionary<string, AssetTrackedFolderState>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Courtroom"] = new AssetTrackedFolderState
                    {
                        Name = "Courtroom",
                        DirectoryPath = @"C:\sync\background\Courtroom",
                        Signature = "bg-same"
                    }
                },
                BlipsSignature = "blips-new",
                ChatsSignature = "chats-same",
                EffectsSignature = "effects-same"
            };

            TargetedAssetRefreshPlan plan = ClientAssetRefreshService.BuildTrackedChangePlan(previous, current);

            Assert.That(plan.CharacterNames, Contains.Item("Phoenix"));
            Assert.That(plan.CharacterNames, Does.Not.Contain("Maya"));
            Assert.That(plan.BackgroundNames, Is.Empty);
            Assert.That(plan.RefreshBlips, Is.True);
            Assert.That(plan.RefreshChats, Is.False);
            Assert.That(plan.RefreshEffects, Is.False);
        }

        [Test]
        public void EvaluateRefreshRequirementReason_DoesNotRequireRefreshForLegacyMarkerWithMissingManagedMount()
        {
            string configPath = Path.Combine(configDirectory, "config.ini");
            string existingMount = Path.Combine(tempRoot, "legacy_managed_existing");
            string missingMount = Path.Combine(tempRoot, "legacy_managed_missing");
            Directory.CreateDirectory(existingMount);
            File.WriteAllText(
                configPath,
                "mount_paths="
                + GoogleDriveMountPathManager.NormalizeMountPathForConfigValue(existingMount)
                + Environment.NewLine
                + "log_maximum=20");

            AssetRefreshMarker marker = new AssetRefreshMarker
            {
                SchemaVersion = 1,
                AppVersion = AppVersionInfo.AssemblyVersion,
                ConfigIniPath = configPath,
                BaseFolders = new List<string>
                {
                    existingMount,
                    missingMount,
                    configDirectory
                }
            };

            string reason = ClientAssetRefreshService.EvaluateRefreshRequirementReason(
                marker,
                AppVersionInfo.AssemblyVersion,
                configPath,
                new List<string>
                {
                    existingMount,
                    configDirectory
                });

            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void EvaluateRefreshRequirementReason_DoesNotRequireRefreshWhenConfigPathDiffersButBaseFoldersMatch()
        {
            string oldConfigDirectory = Path.Combine(tempRoot, "old_config");
            string oldConfigPath = Path.Combine(oldConfigDirectory, "config.ini");
            Directory.CreateDirectory(oldConfigDirectory);
            File.WriteAllText(oldConfigPath, "mount_paths=" + syncRoot + Environment.NewLine + "log_maximum=20");

            string currentConfigPath = Path.Combine(configDirectory, "config.ini");
            AssetRefreshMarker marker = new AssetRefreshMarker
            {
                SchemaVersion = 1,
                AppVersion = AppVersionInfo.AssemblyVersion,
                ConfigIniPath = oldConfigPath,
                BaseFolders = ClientAssetRefreshService.BuildConfiguredBaseFolderSignature(currentConfigPath)
            };

            string reason = ClientAssetRefreshService.EvaluateRefreshRequirementReason(
                marker,
                AppVersionInfo.AssemblyVersion,
                currentConfigPath,
                new List<string>(Globals.BaseFolders));

            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void RefreshChangedAssets_UpsertsChangedCharacterAndRemovesDeletedBackground()
        {
            string configPath = Path.Combine(configDirectory, "config.ini");
            string characterIniPath = Path.Combine(syncRoot, "characters", "Phoenix", "char.ini");
            File.WriteAllText(
                characterIniPath,
                "[Options]\nshowname=Phoenix Updated\ngender=unknown\nside=def\n[Time]\npreanim=0\n[Emotions]\nnumber=1\n1=normal#normal#normal#0#99\n");

            string backgroundDirectory = Path.Combine(syncRoot, "background", "Courtroom");
            Directory.Delete(backgroundDirectory, true);

            GoogleDriveSyncLocalChangeSet changes = new GoogleDriveSyncLocalChangeSet();
            changes.RecordAddedOrUpdated("characters/Phoenix/char.ini");
            changes.RecordDeleted("background/Courtroom");

            ClientAssetRefreshService.RefreshChangedAssets(configPath, syncRoot, changes, _ => { });

            CharacterFolder character = CharacterFolder.FullList.Single(item => item.Name == "Phoenix");
            Background? removedBackground = Background.FromBGPath("Courtroom");

            Assert.That(character.configINI.ShowName, Is.EqualTo("Phoenix Updated"));
            Assert.That(removedBackground, Is.Null);
        }

        private static void CreateCharacter(string root, string folderName, string showName)
        {
            string characterDirectory = Path.Combine(root, "characters", folderName);
            Directory.CreateDirectory(characterDirectory);
            File.WriteAllText(
                Path.Combine(characterDirectory, "char.ini"),
                "[Options]\nshowname=" + showName + "\ngender=unknown\nside=def\n[Time]\npreanim=0\n[Emotions]\nnumber=1\n1=normal#normal#normal#0#99\n");
        }

        private static void CreateBackground(string root, string backgroundName)
        {
            string backgroundDirectory = Path.Combine(root, "background", backgroundName);
            Directory.CreateDirectory(backgroundDirectory);
            File.WriteAllBytes(Path.Combine(backgroundDirectory, "defenseempty.png"), new byte[] { 1, 2, 3, 4 });
        }

        private static void ResetCharacterCache()
        {
            Type type = typeof(CharacterFolder);
            FieldInfo? configsField = type.GetField("characterConfigs", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo? cacheFileField = type.GetField("cacheFile", BindingFlags.NonPublic | BindingFlags.Static);

            configsField?.SetValue(null, new List<CharacterFolder>());

            string? cacheFile = cacheFileField?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(cacheFile) && File.Exists(cacheFile))
            {
                try
                {
                    File.Delete(cacheFile);
                }
                catch
                {
                    // Ignore cleanup errors in tests.
                }
            }
        }

        private static void ResetBackgroundCache()
        {
            Type type = typeof(Background);
            FieldInfo? cacheFileField = type.GetField("cacheFile", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo? cacheLoadedField = type.GetField("cacheLoaded", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo? backgroundsField = type.GetField("backgroundsByName", BindingFlags.NonPublic | BindingFlags.Static);

            backgroundsField?.SetValue(null, new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase));
            cacheLoadedField?.SetValue(null, false);

            string? cacheFile = cacheFileField?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(cacheFile) && File.Exists(cacheFile))
            {
                try
                {
                    File.Delete(cacheFile);
                }
                catch
                {
                    // Ignore cleanup errors in tests.
                }
            }
        }

        private static string NormalizeComparisonPath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
