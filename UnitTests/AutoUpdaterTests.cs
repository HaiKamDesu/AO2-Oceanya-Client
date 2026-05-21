using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Features.Updates;
using OceanyaUpdater;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    public sealed class AutoUpdaterTests
    {
        [Test]
        public void UpdateVersion_OnlyGreaterVersionsCompareAsNewer()
        {
            Assert.That(UpdateVersion.TryParse("v6.4", out UpdateVersion current), Is.True);
            Assert.That(UpdateVersion.TryParse("6.4.0", out UpdateVersion same), Is.True);
            Assert.That(UpdateVersion.TryParse("v6.5", out UpdateVersion newer), Is.True);
            Assert.That(UpdateVersion.TryParse("v6.3.9", out UpdateVersion older), Is.True);

            Assert.That(same.CompareTo(current), Is.EqualTo(0));
            Assert.That(newer > current, Is.True);
            Assert.That(older < current, Is.True);
            Assert.That(UpdateVersion.TryParse("v6.4-beta", out _), Is.False);
            Assert.That(UpdateVersion.TryParseForChannel("test-v6.5.1", UpdateChannel.Test, out UpdateVersion testPrefix), Is.True);
            Assert.That(UpdateVersion.TryParseForChannel("v6.5.1-test.2", UpdateChannel.Test, out UpdateVersion testSuffix), Is.True);
            Assert.That(testPrefix.CompareTo(testSuffix), Is.EqualTo(0));
        }

        [Test]
        public void Manifest_ParsesValidStableWindowsX64Manifest()
        {
            string json = ValidManifestJson();

            bool parsed = UpdateManifest.TryParse(json, out UpdateManifest manifest, out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(manifest.Tag, Is.EqualTo("v6.5"));
            Assert.That(manifest.AssetName, Is.EqualTo("Oceanya.Client.win-x64.v6.5.zip"));
            Assert.That(manifest.Sha256, Is.EqualTo(new string('a', 64)));
        }

        [Test]
        public void Manifest_RejectsMalformedOrUnsafeManifests()
        {
            Assert.That(UpdateManifest.TryParse(ValidManifestJson().Replace(new string('a', 64), ""), out _, out _), Is.False);
            Assert.That(UpdateManifest.TryParse(ValidManifestJson().Replace("\"os\":\"win\"", "\"os\":\"linux\""), out _, out _), Is.False);
            Assert.That(UpdateManifest.TryParse(ValidManifestJson().Replace("Oceanya.Client.win-x64.v6.5.zip", "../evil.zip"), out _, out _), Is.False);
            Assert.That(UpdateManifest.TryParse(ValidManifestJson().Replace("\"channel\":\"stable\"", "\"channel\":\"beta\""), out _, out _), Is.False);
        }

        [Test]
        public void Manifest_ChannelValidationSeparatesStableAndTest()
        {
            Assert.That(UpdateManifest.TryParse(TestManifestJson(), UpdateEnvironment.Test, out UpdateManifest testManifest, out string testError), Is.True, testError);
            Assert.That(testManifest.Tag, Is.EqualTo("test-v6.5.1"));
            Assert.That(UpdateManifest.TryParse(TestManifestJson(), UpdateEnvironment.Stable, out _, out _), Is.False);
            Assert.That(UpdateManifest.TryParse(ValidManifestJson(), UpdateEnvironment.Test, out _, out _), Is.False);
        }

        [Test]
        public void SaveFile_UpdaterSkippedVersionPersistsInSaveData()
        {
            SaveData data = new SaveData();
            SaveFile.ResetForTests(data, persist: false);

            SaveFile.Data.Updater.SkippedReleaseTag = " v6.5 ";
            SaveFile.Data.Updater.SkippedReleaseVersion = " 6.5 ";
            SaveFile.Data.Updater.Test.SkippedReleaseTag = " test-v6.5.1 ";
            SaveFile.ResetForTests(SaveFile.Data, persist: false);

            Assert.That(SaveFile.Data.Updater.Stable.SkippedReleaseTag, Is.EqualTo("v6.5"));
            Assert.That(SaveFile.Data.Updater.Stable.SkippedReleaseVersion, Is.EqualTo("6.5"));
            Assert.That(SaveFile.Data.Updater.Test.SkippedReleaseTag, Is.EqualTo("test-v6.5.1"));
        }

        [Test]
        public async Task GitHubClient_ParsesReleaseWithManifestAndSelectsAsset()
        {
            string manifest = ValidManifestJson();
            using HttpClient httpClient = new HttpClient(new FakeHandler(request =>
            {
                if (request.RequestUri?.ToString() == "https://example.invalid/update-manifest.json")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(manifest)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));

            GitHubUpdateClient client = new GitHubUpdateClient(httpClient);
            Assert.That(UpdateVersion.TryParse("6.4", out UpdateVersion current), Is.True);

            UpdateRelease? release = await client.ParseReleaseAsync(ReleaseJson(), current, CancellationToken.None);

            Assert.That(release, Is.Not.Null);
            Assert.That(release!.Manifest.Tag, Is.EqualTo("v6.5"));
            Assert.That(release.PackageAsset.Name, Is.EqualTo("Oceanya.Client.win-x64.v6.5.zip"));
        }

        [Test]
        public async Task GitHubClient_StableIgnoresPrereleaseAndTestAcceptsPrerelease()
        {
            using HttpClient httpClient = new HttpClient(new FakeHandler(request =>
            {
                if (request.RequestUri?.ToString() == "https://example.invalid/update-manifest.json")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(TestManifestJson())
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));

            GitHubUpdateClient client = new GitHubUpdateClient(httpClient);
            Assert.That(UpdateVersion.TryParse("6.4", out UpdateVersion current), Is.True);

            Assert.That(await client.ParseReleaseAsync(TestReleaseJson(), UpdateEnvironment.Stable, current, CancellationToken.None), Is.Null);
            UpdateRelease? release = await client.ParseReleaseAsync(TestReleaseJson(), UpdateEnvironment.Test, current, CancellationToken.None);

            Assert.That(release, Is.Not.Null);
            Assert.That(release!.Manifest.Channel, Is.EqualTo("test"));
            Assert.That(release.PackageAsset.Name, Is.EqualTo("Oceanya.Client.win-x64.test-v6.5.1.zip"));
        }

        [Test]
        public void UpdateEnvironment_ReleaseBuildIgnoresDeveloperOverride()
        {
            UpdateEnvironment releaseEnvironment = UpdateEnvironment.ResolveForTests(
                isDeveloperBuild: false,
                debuggerAttached: true,
                args: new[] { "OceanyaClient.exe", "--update-channel=test" });
            UpdateEnvironment debugEnvironment = UpdateEnvironment.ResolveForTests(
                isDeveloperBuild: true,
                debuggerAttached: false,
                args: Array.Empty<string>());

            Assert.That(releaseEnvironment.Channel, Is.EqualTo(UpdateChannel.Stable));
            Assert.That(debugEnvironment.Channel, Is.EqualTo(UpdateChannel.Test));
        }

        [Test]
        public void UpdateStoragePaths_SeparateStableAndTestRoots()
        {
            UpdateStoragePaths stable = new UpdateStoragePaths(UpdateEnvironment.Stable);
            UpdateStoragePaths test = new UpdateStoragePaths(UpdateEnvironment.Test);

            Assert.That(stable.Root, Does.Contain("OceanyaClient"));
            Assert.That(test.Root, Does.Contain("OceanyaClientDev"));
            Assert.That(test.Root, Is.Not.EqualTo(stable.Root));
        }

        [Test]
        public async Task GitHubClient_RejectsDigestMismatch()
        {
            string manifest = ValidManifestJson();
            using HttpClient httpClient = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest)
            }));

            string releaseJson = ReleaseJson().Replace("sha256:" + new string('a', 64), "sha256:" + new string('b', 64));
            GitHubUpdateClient client = new GitHubUpdateClient(httpClient);
            Assert.That(UpdateVersion.TryParse("6.4", out UpdateVersion current), Is.True);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.ParseReleaseAsync(releaseJson, current, CancellationToken.None))!;
            Assert.That(ex.Message, Does.Contain("digest"));
        }

        [Test]
        public void ReleaseNotesSanitizer_RemovesHtmlAndKeepsPlainText()
        {
            string sanitized = ReleaseNotesSanitizer.ToSafePlainText("# Title\n<script>alert(1)</script>\n[Diff](https://github.com/x/y)");

            Assert.That(sanitized, Does.Contain("Title"));
            Assert.That(sanitized, Does.Not.Contain("<script>"));
            Assert.That(sanitized, Does.Contain("Diff (https://github.com/x/y)"));
        }

        [Test]
        public void ZipValidator_ExtractsSafeSingleRootPackage()
        {
            string root = CreateTempDir();
            string zip = Path.Combine(root, "safe.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                archive.CreateEntry("Oceanya Client v6.5/OceanyaClient.exe").WriteText("exe");
                archive.CreateEntry("Oceanya Client v6.5/data.txt").WriteText("data");
            }

            string packageRoot = UpdateZipValidator.ExtractValidatedPackage(zip, Path.Combine(root, "staged"));

            Assert.That(File.Exists(Path.Combine(packageRoot, "OceanyaClient.exe")), Is.True);
        }

        [Test]
        public void ZipValidator_RejectsTraversalAndAdsPaths()
        {
            string root = CreateTempDir();
            string traversalZip = Path.Combine(root, "traversal.zip");
            using (ZipArchive archive = ZipFile.Open(traversalZip, ZipArchiveMode.Create))
            {
                archive.CreateEntry("Oceanya Client v6.5/OceanyaClient.exe").WriteText("exe");
                archive.CreateEntry("../evil.txt").WriteText("evil");
            }

            Assert.Throws<InvalidOperationException>(() =>
                UpdateZipValidator.ExtractValidatedPackage(traversalZip, Path.Combine(root, "bad1")));

            string adsZip = Path.Combine(root, "ads.zip");
            using (ZipArchive archive = ZipFile.Open(adsZip, ZipArchiveMode.Create))
            {
                archive.CreateEntry("Oceanya Client v6.5/OceanyaClient.exe:ads").WriteText("evil");
            }

            Assert.Throws<InvalidOperationException>(() =>
                UpdateZipValidator.ExtractValidatedPackage(adsZip, Path.Combine(root, "bad2")));
        }

        [Test]
        public void UpdaterArguments_ValidateRequiredSafeArguments()
        {
            bool parsed = UpdaterArguments.TryParse(new[]
            {
                "--source", @"C:\Temp\source",
                "--install", @"C:\Temp\install",
                "--backup", @"C:\Temp\backup",
                "--parent-pid", "123",
                "--entry-exe", "OceanyaClient.exe",
                "--log", @"C:\Temp\updater.log"
            }, out UpdaterArguments args, out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(args.EntryExe, Is.EqualTo("OceanyaClient.exe"));
            Assert.That(UpdaterArguments.TryParse(new[] { "--entry-exe", @"..\evil.exe" }, out _, out _), Is.False);
        }

        private static string ValidManifestJson()
        {
            return "{"
                + "\"version\":\"6.5\","
                + "\"tag\":\"v6.5\","
                + "\"channel\":\"stable\","
                + "\"os\":\"win\","
                + "\"arch\":\"x64\","
                + "\"assetName\":\"Oceanya.Client.win-x64.v6.5.zip\","
                + "\"sha256\":\"" + new string('a', 64) + "\","
                + "\"minimumSupportedVersion\":\"\","
                + "\"entryExe\":\"OceanyaClient.exe\","
                + "\"releaseNotesSource\":\"github_release_body\""
                + "}";
        }

        private static string TestManifestJson()
        {
            return "{"
                + "\"version\":\"6.5.1\","
                + "\"tag\":\"test-v6.5.1\","
                + "\"channel\":\"test\","
                + "\"os\":\"win\","
                + "\"arch\":\"x64\","
                + "\"assetName\":\"Oceanya.Client.win-x64.test-v6.5.1.zip\","
                + "\"sha256\":\"" + new string('c', 64) + "\","
                + "\"minimumSupportedVersion\":\"\","
                + "\"entryExe\":\"OceanyaClient.exe\","
                + "\"releaseNotesSource\":\"github_release_body\""
                + "}";
        }

        private static string ReleaseJson()
        {
            return "{"
                + "\"tag_name\":\"v6.5\","
                + "\"name\":\"Oceanya Client v6.5\","
                + "\"html_url\":\"https://github.com/HaiKamDesu/AO2-Oceanya-Client/releases/tag/v6.5\","
                + "\"body\":\"# Notes\","
                + "\"draft\":false,"
                + "\"prerelease\":false,"
                + "\"published_at\":\"2026-05-21T00:00:00Z\","
                + "\"assets\":["
                + "{\"name\":\"update-manifest.json\",\"browser_download_url\":\"https://example.invalid/update-manifest.json\",\"size\":100},"
                + "{\"name\":\"Oceanya.Client.win-x64.v6.5.zip\",\"browser_download_url\":\"https://github.com/HaiKamDesu/AO2-Oceanya-Client/releases/download/v6.5/Oceanya.Client.win-x64.v6.5.zip\",\"digest\":\"sha256:" + new string('a', 64) + "\",\"size\":123}"
                + "]"
                + "}";
        }

        private static string TestReleaseJson()
        {
            return "{"
                + "\"tag_name\":\"test-v6.5.1\","
                + "\"name\":\"Oceanya Client test-v6.5.1\","
                + "\"html_url\":\"https://github.com/HaiKamDesu/AO2-Oceanya-Client/releases/tag/test-v6.5.1\","
                + "\"body\":\"# Test Notes\","
                + "\"draft\":false,"
                + "\"prerelease\":true,"
                + "\"published_at\":\"2026-05-21T00:00:00Z\","
                + "\"assets\":["
                + "{\"name\":\"update-manifest.json\",\"browser_download_url\":\"https://example.invalid/update-manifest.json\",\"size\":100},"
                + "{\"name\":\"Oceanya.Client.win-x64.test-v6.5.1.zip\",\"browser_download_url\":\"https://github.com/HaiKamDesu/AO2-Oceanya-Client/releases/download/test-v6.5.1/Oceanya.Client.win-x64.test-v6.5.1.zip\",\"digest\":\"sha256:" + new string('c', 64) + "\",\"size\":123}"
                + "]"
                + "}";
        }

        private static string CreateTempDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "OceanyaUpdaterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                this.handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(handler(request));
            }
        }
    }

    internal static class ZipArchiveEntryTestExtensions
    {
        public static void WriteText(this ZipArchiveEntry entry, string text)
        {
            using Stream stream = entry.Open();
            using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(text);
        }
    }
}
