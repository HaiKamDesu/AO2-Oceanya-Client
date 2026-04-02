using OceanyaClient;
using OceanyaClient.Features.GoogleDriveSync;
using NUnit.Framework;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    [Category("RequiresCredentials")]
    [Explicit("Runs against a real Google Drive folder using credentials supplied through environment variables.")]
    [NonParallelizable]
    public class GoogleDriveSyncLiveTests
    {
        [Test]
        public async Task LiveDrive_PushThenPull_RoundTripsProbeFile()
        {
            string refreshToken = GetRequiredEnvironmentVariable("OCEANYA_GOOGLE_DRIVE_TEST_REFRESH_TOKEN");
            string testRootFolderId = GetRequiredEnvironmentVariable("OCEANYA_GOOGLE_DRIVE_TEST_FOLDER_ID");

            string tokenRoot = Path.Combine(Path.GetTempPath(), "drive_live_tokens_" + Guid.NewGuid().ToString("N"));
            string localRoot = Path.Combine(Path.GetTempPath(), "drive_live_sync_" + Guid.NewGuid().ToString("N"));
            string tokenStoreKey = Guid.NewGuid().ToString("N");
            HttpClient httpClient = new HttpClient();
            GoogleDriveSecureTokenStore tokenStore = new GoogleDriveSecureTokenStore(tokenRoot, new IdentityProtector());
            GoogleDriveOAuthService oauthService = new GoogleDriveOAuthService(httpClient);
            GoogleDriveSessionFactory sessionFactory = new GoogleDriveSessionFactory(
                httpClient,
                tokenStore,
                credentialStore: null,
                oauthService: oauthService);
            GoogleDriveSyncService service = new GoogleDriveSyncService(sessionFactory);

            GoogleDriveSyncSettings rootSettings = new GoogleDriveSyncSettings
            {
                TokenStoreKey = tokenStoreKey,
                RemoteFolderId = testRootFolderId,
                LocalFolderPath = localRoot,
                MirrorDeletes = true
            };

            GoogleDriveTokenSet storedTokens = new GoogleDriveTokenSet
            {
                RefreshToken = refreshToken,
                AccessToken = string.Empty,
                AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            tokenStore.Save(tokenStoreKey, storedTokens);

            IGoogleDriveRemoteClient rootClient = await sessionFactory.CreateAuthorizedClientAsync(rootSettings, CancellationToken.None);
            string testFolderName = "oceanya_live_" + Guid.NewGuid().ToString("N");
            GoogleDriveSyncFolderEntry createdFolder = await rootClient.CreateFolderAsync(testRootFolderId, testFolderName, CancellationToken.None);

            GoogleDriveSyncSettings testSettings = new GoogleDriveSyncSettings
            {
                TokenStoreKey = tokenStoreKey,
                RemoteFolderId = createdFolder.ItemId,
                LocalFolderPath = localRoot,
                MirrorDeletes = true
            };

            try
            {
                string probeDirectory = Path.Combine(localRoot, "characters", "live_probe");
                Directory.CreateDirectory(probeDirectory);
                string probePath = Path.Combine(probeDirectory, "char.ini");
                File.WriteAllText(probePath, "name=LiveProbe");

                await service.PushLocalFolderAsync(testSettings, _ => { }, CancellationToken.None);

                Directory.Delete(localRoot, true);
                Directory.CreateDirectory(localRoot);

                await service.PullFromDriveAsync(testSettings, _ => { }, CancellationToken.None);

                string restoredPath = Path.Combine(localRoot, "characters", "live_probe", "char.ini");
                Assert.That(File.Exists(restoredPath), Is.True);
                Assert.That(File.ReadAllText(restoredPath), Is.EqualTo("name=LiveProbe"));
            }
            finally
            {
                try
                {
                    await rootClient.DeleteItemAsync(createdFolder.ItemId, CancellationToken.None);
                }
                catch
                {
                }

                if (Directory.Exists(localRoot))
                {
                    Directory.Delete(localRoot, true);
                }

                if (Directory.Exists(tokenRoot))
                {
                    Directory.Delete(tokenRoot, true);
                }
            }
        }

        private static string GetRequiredEnvironmentVariable(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                Assert.Ignore("Missing required Google Drive live-test environment variable: " + name);
            }

            return value!;
        }

        private sealed class IdentityProtector : ISecretProtector
        {
            public byte[] Protect(byte[] value)
            {
                return value;
            }

            public byte[] Unprotect(byte[] value)
            {
                return value;
            }
        }
    }
}
