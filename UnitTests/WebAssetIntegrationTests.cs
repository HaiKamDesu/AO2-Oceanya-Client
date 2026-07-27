using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AOBot_Testing.Structures;
using Common;
using Common.WebAssets;
using NUnit.Framework;
using OceanyaClient.Utilities;

namespace UnitTests
{
    /// <summary>
    /// Coverage for the parts of the webAO fallback that plug into existing systems: mount priority,
    /// cache-signature isolation, VFS mapping back from physical paths, and context-menu provenance.
    /// </summary>
    [TestFixture]
    public class WebAssetIntegrationTests
    {
        private readonly List<string> temporaryDirectories = new List<string>();
        private List<string> originalBaseFolders = new List<string>();

        [SetUp]
        public void SetUp()
        {
            originalBaseFolders = Globals.BaseFolders;
        }

        [TearDown]
        public void TearDown()
        {
            Globals.BaseFolders = originalBaseFolders;
            WebAssetService.Uninstall();

            foreach (string directory in temporaryDirectories)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // Temp cleanup failures must not fail a test run.
                }
            }

            temporaryDirectories.Clear();
        }

        // ── Mount priority: the whole point of the mirror-as-last-mount design ──

        [Test]
        public void LocalFileAlwaysWinsOverTheSameAssetInTheMirror()
        {
            string localMount = CreateTempDirectory();
            string mirror = CreateTempDirectory();
            WebAssetSource.RegisterMirrorRootForProvenance(mirror);

            WriteFile(localMount, "characters/phoenix/(a)normal.png", "LOCAL");
            WriteFile(mirror, "characters/phoenix/(a)normal.png", "WEB");

            Globals.BaseFolders = new List<string> { localMount };
            WebAssetService.EnsureMounted(mirror);

            string? resolved = ResolveFirstHit("characters/phoenix/(a)normal.png");

            Assert.That(resolved, Is.Not.Null);
            Assert.That(File.ReadAllText(resolved!), Is.EqualTo("LOCAL"),
                "The user's own file must win; the mirror is only ever a fallback.");
        }

        [Test]
        public void MirrorServesAnAssetTheUserDoesNotHave()
        {
            string localMount = CreateTempDirectory();
            string mirror = CreateTempDirectory();
            WebAssetSource.RegisterMirrorRootForProvenance(mirror);
            WriteFile(mirror, "characters/edgeworth/(a)normal.png", "WEB");

            Globals.BaseFolders = new List<string> { localMount };
            WebAssetService.EnsureMounted(mirror);

            string? resolved = ResolveFirstHit("characters/edgeworth/(a)normal.png");

            Assert.That(resolved, Is.Not.Null);
            Assert.That(File.ReadAllText(resolved!), Is.EqualTo("WEB"));
        }

        // ── Cache-signature isolation: the highest-risk item in the design ──

        [Test]
        public void PhysicalBaseFolders_ExcludesTheMirrorButKeepsEveryRealMount()
        {
            string localMount = CreateTempDirectory();
            string secondMount = CreateTempDirectory();
            string mirror = CreateTempDirectory();
            WebAssetSource.RegisterMirrorRootForProvenance(mirror);

            Globals.BaseFolders = new List<string> { localMount, secondMount };
            WebAssetService.EnsureMounted(mirror);

            Assert.That(Globals.BaseFolders, Has.Count.EqualTo(3), "Lookup still sees the mirror.");
            Assert.That(Globals.PhysicalBaseFolders, Is.EqualTo(new[] { localMount, secondMount }),
                "Cache signatures must not see the mirror, or connecting would invalidate the character "
                + "cache and force the multi-second cold rescan.");
        }

        [Test]
        public void PhysicalBaseFolders_IsUnchangedByConnectingAndDisconnecting()
        {
            string localMount = CreateTempDirectory();
            string mirror = CreateTempDirectory();
            WebAssetSource.RegisterMirrorRootForProvenance(mirror);
            Globals.BaseFolders = new List<string> { localMount };

            List<string> beforeConnect = Globals.PhysicalBaseFolders;
            WebAssetService.EnsureMounted(mirror);
            List<string> whileConnected = Globals.PhysicalBaseFolders;
            WebAssetService.EnsureUnmounted(mirror);
            List<string> afterDisconnect = Globals.PhysicalBaseFolders;

            Assert.That(whileConnected, Is.EqualTo(beforeConnect));
            Assert.That(afterDisconnect, Is.EqualTo(beforeConnect));
        }

        // ── Physical path -> VFS mapping (how directory-rooted resolvers reach the web) ──

        [Test]
        public void TryGetVPathForAbsolutePath_MapsAMountedPathBackToItsVfsPath()
        {
            string mount = CreateTempDirectory();
            Globals.BaseFolders = new List<string> { mount };

            bool mapped = WebAssetSource.TryGetVPathForAbsolutePath(
                Path.Combine(mount, "background", "court"),
                out string vpath);

            Assert.That(mapped, Is.True);
            Assert.That(vpath, Is.EqualTo("background/court"));
        }

        [Test]
        public void TryGetVPathForAbsolutePath_PrefersTheLongestMatchingMount()
        {
            string parent = CreateTempDirectory();
            string nested = Path.Combine(parent, "base");
            Directory.CreateDirectory(nested);
            Globals.BaseFolders = new List<string> { parent, nested };

            bool mapped = WebAssetSource.TryGetVPathForAbsolutePath(
                Path.Combine(nested, "background", "court"),
                out string vpath);

            Assert.That(mapped, Is.True);
            Assert.That(vpath, Is.EqualTo("background/court"),
                "Nested mounts are normal in AO installs; the deepest one owns the path.");
        }

        [Test]
        public void TryGetVPathForAbsolutePath_RejectsAPathOutsideEveryMount()
        {
            Globals.BaseFolders = new List<string> { CreateTempDirectory() };

            Assert.That(
                WebAssetSource.TryGetVPathForAbsolutePath(Path.Combine(Path.GetTempPath(), "elsewhere"), out _),
                Is.False);
            Assert.That(WebAssetSource.TryGetVPathForAbsolutePath(null, out _), Is.False);
        }

        // ── Provenance UX ──

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MenuDecorator_RewritesAFilesystemItemOnlyForAWebAsset()
        {
            string mirror = CreateTempDirectory();
            WebAssetSource.RegisterMirrorRootForProvenance(mirror);
            string localPath = CreateTempDirectory();

            System.Windows.Controls.MenuItem webItem = new System.Windows.Controls.MenuItem
            {
                Header = "Open in file explorer",
                IsEnabled = true
            };
            System.Windows.Controls.MenuItem localItem = new System.Windows.Controls.MenuItem
            {
                Header = "Open in file explorer",
                IsEnabled = true
            };

            bool rewroteWeb = WebAssetMenuDecorator.ApplyProvenance(
                webItem,
                Path.Combine(mirror, "characters", "phoenix"));
            bool rewroteLocal = WebAssetMenuDecorator.ApplyProvenance(localItem, localPath);

            Assert.That(rewroteWeb, Is.True);
            Assert.That(webItem.Header, Is.EqualTo(WebAssetMenuDecorator.StreamedItemHeader));
            Assert.That(webItem.IsEnabled, Is.False);
            Assert.That(webItem.ToolTip, Is.Not.Null);

            Assert.That(rewroteLocal, Is.False, "A local asset's menu must be untouched.");
            Assert.That(localItem.Header, Is.EqualTo("Open in file explorer"));
            Assert.That(localItem.IsEnabled, Is.True);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MenuDecorator_AddsNothingForALocalAsset()
        {
            System.Windows.Controls.ContextMenu menu = new System.Windows.Controls.ContextMenu();
            WebAssetMenuDecorator.AddProvenanceSection(menu, CreateTempDirectory());

            Assert.That(menu.Items.Count, Is.Zero,
                "Local content must look exactly as it did before this feature existed.");
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MenuDecorator_AddsACopyableUrlForAWebAsset()
        {
            Globals.BaseFolders = new List<string> { CreateTempDirectory() };
            WebAssetService? service = WebAssetService.Install("https://example.com/base/");
            Assert.That(service, Is.Not.Null);
            temporaryDirectories.Add(service!.Source.MirrorRoot);

            System.Windows.Controls.ContextMenu menu = new System.Windows.Controls.ContextMenu();
            WebAssetMenuDecorator.AddProvenanceSection(
                menu,
                service.Source.BuildLocalPath("characters/phoenix/char.ini"));

            Assert.That(menu.Items.Count, Is.EqualTo(2), "Expected a section header plus one action.");
            System.Windows.Controls.MenuItem copyItem =
                menu.Items.OfType<System.Windows.Controls.MenuItem>().Last();
            Assert.That(copyItem.Header, Is.EqualTo("Copy asset URL"));
            Assert.That(copyItem.IsEnabled, Is.True);
        }

        // ── Prefetch stem construction (what actually gets requested per message) ──

        [Test]
        public void MessagePrefetch_IsANoOpWhenFallbackIsNotInstalled()
        {
            WebAssetService.Uninstall();
            ICMessage message = new ICMessage { Character = "Phoenix", Emote = "normal" };

            Assert.DoesNotThrow(() =>
                OceanyaClient.Features.WebAssets.WebAssetPrefetcher.PrefetchMessageAssets(message, "court"));
        }

        [Test]
        public void MessagePrefetch_QueuesBothHalvesOfTheEmoteAndTheBackground()
        {
            Globals.BaseFolders = new List<string> { CreateTempDirectory() };
            // Point at a host that cannot answer: the requests are still recorded by the stats counters,
            // which is what this test asserts, without depending on any network round trip succeeding.
            WebAssetService? service = WebAssetService.Install("http://127.0.0.1:1/base/");
            Assert.That(service, Is.Not.Null);
            temporaryDirectories.Add(service!.Source.MirrorRoot);

            ICMessage message = new ICMessage
            {
                Character = "Phoenix",
                Emote = "normal",
                Side = "wit",
                PreAnim = "-",
                SfxName = "1"
            };

            OceanyaClient.Features.WebAssets.WebAssetPrefetcher.PrefetchMessageAssets(message, "court");

            long requests = service.Stats.Snapshot().Requests;
            Assert.That(requests, Is.GreaterThanOrEqualTo(4),
                "Expected at least (a)/(b) sprites plus the background's design.ini and wit image.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Mirrors the first-hit-wins mount walk every Oceanya resolver performs, so these tests assert
        /// the ordering contract itself rather than one resolver's implementation of it.
        /// </summary>
        private static string? ResolveFirstHit(string vpath)
        {
            foreach (string mount in Globals.BaseFolders)
            {
                string candidate = Path.Combine(mount, vpath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void WriteFile(string root, string relativePath, string content)
        {
            string full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        private string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "OceanyaWebAssetIntegration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            temporaryDirectories.Add(path);
            return path;
        }
    }
}
