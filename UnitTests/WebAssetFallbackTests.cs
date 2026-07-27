using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Common.WebAssets;
using NUnit.Framework;

namespace UnitTests
{
    /// <summary>
    /// Phase 1 coverage for the webAO-style asset fallback pipeline: URL/VPath normalization, mirror
    /// mount ordering, learned formats, negative caching, manifest suppression, and the fetch queue's
    /// download/atomic-write behavior against a stub HTTP server.
    /// </summary>
    [TestFixture]
    public class WebAssetFallbackTests
    {
        private readonly List<string> temporaryDirectories = new List<string>();

        [TearDown]
        public void TearDown()
        {
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

        // ── Source / path normalization ─────────────────────────────────────────

        [Test]
        public void NormalizeUrl_AddsTrailingSlashAndRejectsNonHttp()
        {
            Assert.That(WebAssetSource.NormalizeUrl("https://example.com/base"), Is.EqualTo("https://example.com/base/"));
            Assert.That(WebAssetSource.NormalizeUrl("https://example.com/base/"), Is.EqualTo("https://example.com/base/"));
            Assert.That(WebAssetSource.NormalizeUrl("http://example.com/base"), Is.EqualTo("http://example.com/base/"));
            Assert.That(WebAssetSource.NormalizeUrl("  https://example.com/base  "), Is.EqualTo("https://example.com/base/"));

            Assert.That(WebAssetSource.NormalizeUrl(null), Is.Empty);
            Assert.That(WebAssetSource.NormalizeUrl(""), Is.Empty);
            Assert.That(WebAssetSource.NormalizeUrl("ftp://example.com/base"), Is.Empty);
            Assert.That(WebAssetSource.NormalizeUrl("not a url"), Is.Empty);
        }

        [Test]
        public void NormalizeVPath_LowercasesSlashesAndBlocksTraversal()
        {
            Assert.That(WebAssetSource.NormalizeVPath(@"Characters\Phoenix\(a)Normal.WEBP"),
                Is.EqualTo("characters/phoenix/(a)normal.webp"));
            Assert.That(WebAssetSource.NormalizeVPath("/background/court/"), Is.EqualTo("background/court"));

            Assert.That(WebAssetSource.NormalizeVPath("../../secrets.txt"), Is.Empty,
                "Traversal must be rejected so a malicious VFS path cannot escape the mirror.");
            Assert.That(WebAssetSource.NormalizeVPath("characters/../../etc/passwd"), Is.Empty);
        }

        [Test]
        public void BuildUrl_EscapesEachSegmentButKeepsSeparators()
        {
            WebAssetSource source = CreateSource("https://example.com/base/");
            string url = source.BuildUrl(WebAssetSource.NormalizeVPath("characters/Mr Fake/(a)normal.png"));

            Assert.That(url, Is.EqualTo("https://example.com/base/characters/mr%20fake/(a)normal.png"));
        }

        [Test]
        public void IsWebAsset_OnlyMatchesPathsUnderAKnownMirror()
        {
            WebAssetSource source = CreateSource("https://example.com/base/");
            string inside = source.BuildLocalPath("characters/phoenix/(a)normal.webp");

            Assert.That(WebAssetSource.IsWebAsset(inside), Is.True);
            Assert.That(WebAssetSource.IsWebAsset(@"C:\AO2\base\characters\phoenix\(a)normal.webp"), Is.False);
            Assert.That(WebAssetSource.IsWebAsset(null), Is.False);
            Assert.That(WebAssetSource.IsWebAsset("   "), Is.False);
        }

        [Test]
        public void TryGetRemoteUrl_RoundTripsAMirrorPathBackToItsOrigin()
        {
            WebAssetSource source = CreateSource("https://example.com/base/");
            string local = source.BuildLocalPath("characters/phoenix/(a)normal.webp");

            Assert.That(source.TryGetRemoteUrl(local),
                Is.EqualTo("https://example.com/base/characters/phoenix/(a)normal.webp"));
            Assert.That(source.TryGetRemoteUrl(@"C:\AO2\base\characters\phoenix\(a)normal.webp"), Is.Empty);
        }

        [Test]
        public void DifferentAssetUrls_GetDisjointMirrors()
        {
            string first = WebAssetSource.BuildMirrorRoot("https://a.example.com/base/");
            string second = WebAssetSource.BuildMirrorRoot("https://b.example.com/base/");

            Assert.That(first, Is.Not.EqualTo(second),
                "Two servers must never share a mirror; one could otherwise poison the other's assets.");
        }

        // ── Mount ordering ──────────────────────────────────────────────────────

        [Test]
        public void EnsureMounted_AppendsMirrorLastSoLocalFilesAlwaysWin()
        {
            List<string> original = Globals.BaseFolders;
            try
            {
                Globals.BaseFolders = new List<string> { @"C:\packs\extra", @"C:\AO2\base" };
                string mirror = CreateTempDirectory();

                WebAssetService.EnsureMounted(mirror);

                Assert.That(Globals.BaseFolders.Last(), Is.EqualTo(mirror),
                    "The mirror must be the lowest-priority mount.");
                Assert.That(Globals.BaseFolders.Count, Is.EqualTo(3));

                // Idempotent: mounting twice must not duplicate or reorder.
                WebAssetService.EnsureMounted(mirror);
                Assert.That(Globals.BaseFolders.Count, Is.EqualTo(3));
                Assert.That(Globals.BaseFolders.Last(), Is.EqualTo(mirror));

                WebAssetService.EnsureUnmounted(mirror);
                Assert.That(Globals.BaseFolders, Does.Not.Contain(mirror));
                Assert.That(Globals.BaseFolders.Count, Is.EqualTo(2));
            }
            finally
            {
                Globals.BaseFolders = original;
            }
        }

        // ── Format memory ───────────────────────────────────────────────────────

        [Test]
        public void FormatMemory_MovesTheLearnedExtensionToTheFrontWithoutDroppingFallbacks()
        {
            WebAssetFormatMemory memory = new WebAssetFormatMemory();
            IReadOnlyList<string> configured = new[] { ".webp", ".apng", ".gif", ".png" };

            Assert.That(memory.BuildProbeOrder(WebAssetKind.CharacterSprite, configured),
                Is.EqualTo(configured), "With nothing learned the configured order is used verbatim.");

            memory.RecordSuccess(WebAssetKind.CharacterSprite, ".png");

            IReadOnlyList<string> learnedOrder = memory.BuildProbeOrder(WebAssetKind.CharacterSprite, configured);
            Assert.That(learnedOrder.First(), Is.EqualTo(".png"),
                "The learned format must be probed first so the steady state is one request.");
            Assert.That(learnedOrder, Is.EquivalentTo(configured),
                "Reordering must never drop a fallback, so mixed-format packs still resolve.");
        }

        [Test]
        public void FormatMemory_LearnedFormatIsScopedPerKind()
        {
            WebAssetFormatMemory memory = new WebAssetFormatMemory();
            memory.RecordSuccess(WebAssetKind.CharacterIcon, ".png");
            memory.RecordSuccess(WebAssetKind.CharacterSprite, ".webp");

            Assert.That(memory.GetLearned(WebAssetKind.CharacterIcon), Is.EqualTo(".png"));
            Assert.That(memory.GetLearned(WebAssetKind.CharacterSprite), Is.EqualTo(".webp"));
            Assert.That(memory.GetLearned(WebAssetKind.Background), Is.Null);
        }

        [Test]
        public void FormatMemory_NegativeCacheExpiresWithItsTtl()
        {
            WebAssetFormatMemory live = new WebAssetFormatMemory(null, TimeSpan.FromHours(1));
            live.RecordMiss("CharacterSprite|characters/ghost/(a)normal");
            Assert.That(live.IsKnownMiss("CharacterSprite|characters/ghost/(a)normal"), Is.True);
            Assert.That(live.IsKnownMiss("CharacterSprite|characters/other/(a)normal"), Is.False);

            live.ClearMiss("CharacterSprite|characters/ghost/(a)normal");
            Assert.That(live.IsKnownMiss("CharacterSprite|characters/ghost/(a)normal"), Is.False);

            WebAssetFormatMemory expired = new WebAssetFormatMemory(null, TimeSpan.Zero);
            expired.RecordMiss("CharacterSprite|characters/ghost/(a)normal");
            Assert.That(expired.IsKnownMiss("CharacterSprite|characters/ghost/(a)normal"), Is.True,
                "TimeSpan.Zero falls back to the default TTL rather than expiring instantly.");
        }

        [Test]
        public void FormatMemory_PersistsLearnedFormatsAndMissesAcrossReload()
        {
            string mirror = CreateTempDirectory();

            WebAssetFormatMemory first = new WebAssetFormatMemory(mirror, TimeSpan.FromHours(24));
            first.RecordSuccess(WebAssetKind.CharacterSprite, ".webp");
            first.RecordMiss("Background|background/ghost/court");
            first.Flush();

            WebAssetFormatMemory reloaded = new WebAssetFormatMemory(mirror, TimeSpan.FromHours(24));
            Assert.That(reloaded.GetLearned(WebAssetKind.CharacterSprite), Is.EqualTo(".webp"));
            Assert.That(reloaded.IsKnownMiss("Background|background/ghost/court"), Is.True);
        }

        // ── Manifest ────────────────────────────────────────────────────────────

        [Test]
        public async Task Manifest_ParsesListsAndExtensionOverrides()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddJson("characters.json", "[\"Phoenix\", \"Edgeworth\"]");
            server.AddJson("backgrounds.json", "[\"court\"]");
            server.AddJson("evidence.json", "[\"empty.png\"]");
            server.AddJson(
                "extensions.json",
                "{\"emote_extensions\":[\".png\",\".webp\",\".webp.static\"],\"charicon_extensions\":[\"png\"]}");

            using HttpClient client = new HttpClient();
            WebAssetSource source = CreateSource(server.BaseUrl);
            WebAssetManifest manifest = await WebAssetManifest.FetchAsync(client, source);

            Assert.That(manifest.HasCharacterList, Is.True);
            Assert.That(manifest.Characters, Is.EquivalentTo(new[] { "phoenix", "edgeworth" }));
            Assert.That(manifest.Backgrounds, Is.EquivalentTo(new[] { "court" }));
            Assert.That(manifest.Evidence, Is.EquivalentTo(new[] { "empty.png" }));

            Assert.That(manifest.ExtensionsFor(WebAssetKind.CharacterSprite),
                Is.EqualTo(new[] { ".png", ".webp", ".webp.static" }),
                ".webp.static is preserved: it is a URL-shape marker (drop the (a)/(b) prefix), so "
                + "collapsing it into .webp loses a real candidate.");
            Assert.That(manifest.ExtensionsFor(WebAssetKind.CharacterIcon),
                Is.EqualTo(new[] { ".png" }),
                "Bare extension names must be normalized with a leading dot.");
            Assert.That(manifest.ExtensionsFor(WebAssetKind.Background),
                Is.EqualTo(WebAssetExtensions.Image),
                "An undeclared type keeps the default probe order.");
        }

        [Test]
        public async Task Manifest_MissingFilesLeaveEverythingProbeable()
        {
            using StubAssetServer server = new StubAssetServer();
            using HttpClient client = new HttpClient();
            WebAssetSource source = CreateSource(server.BaseUrl);

            WebAssetManifest manifest = await WebAssetManifest.FetchAsync(client, source);

            Assert.That(manifest.HasCharacterList, Is.False);
            Assert.That(manifest.IsKnownAbsent("characters/anyone/(a)normal.webp"), Is.False,
                "Without manifests nothing may be declared absent - the pipeline must still probe.");
        }

        [Test]
        public async Task Manifest_KnownAbsentOnlyFiresForCoveredRoots()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddJson("characters.json", "[\"Phoenix\"]");

            using HttpClient client = new HttpClient();
            WebAssetManifest manifest = await WebAssetManifest.FetchAsync(client, CreateSource(server.BaseUrl));

            Assert.That(manifest.IsKnownAbsent("characters/edgeworth/(a)normal.webp"), Is.True);
            Assert.That(manifest.IsKnownAbsent("characters/phoenix/(a)normal.webp"), Is.False);
            Assert.That(manifest.IsKnownAbsent("background/court/wit.png"), Is.False,
                "backgrounds.json was not published, so backgrounds stay probeable.");
            Assert.That(manifest.IsKnownAbsent("sounds/general/sfx-realization.opus"), Is.False);
        }

        // ── Fetch queue ─────────────────────────────────────────────────────────

        [Test]
        public async Task Manifest_KeepsWebPStaticAsADistinctCandidate()
        {
            // The exact manifest served by miku.pizza, the server where this surfaced.
            using StubAssetServer server = new StubAssetServer();
            server.AddJson(
                "extensions.json",
                "{\"charicon_extensions\":[\".png\"],\"emote_extensions\":[\".webp\",\".webp.static\"],"
                + "\"emotions_extensions\":[\".png\",\".webp\"],\"background_extensions\":[\".webp\"]}");

            using HttpClient client = new HttpClient();
            WebAssetManifest manifest = await WebAssetManifest.FetchAsync(client, CreateSource(server.BaseUrl));

            Assert.That(manifest.ExtensionsFor(WebAssetKind.CharacterSprite),
                Is.EqualTo(new[] { ".webp", ".webp.static" }),
                ".webp.static is a URL-shape marker (prefix dropped), not a duplicate of .webp. "
                + "Collapsing it left exactly one candidate and broke every single-sprite character.");

            Assert.That(manifest.ExtensionsFor(WebAssetKind.CharacterIcon),
                Is.EquivalentTo(new[] { ".png", ".webp" }),
                "charicon_extensions and emotions_extensions both map to the icon kind and must merge, "
                + "not overwrite each other.");
        }

        [Test]
        public void StripEmotePrefix_RemovesOnlyALeadingAo2EmotePrefix()
        {
            Assert.That(WebAssetFetchQueue.StripEmotePrefix("characters/reko_hdf/(a)brokentalk"),
                Is.EqualTo("characters/reko_hdf/brokentalk"));
            Assert.That(WebAssetFetchQueue.StripEmotePrefix("characters/reko_hdf/(b)normal"),
                Is.EqualTo("characters/reko_hdf/normal"));

            Assert.That(WebAssetFetchQueue.StripEmotePrefix("characters/reko_hdf/normal"), Is.Empty,
                "Nothing to strip means no extra candidate, not a duplicate of the plain stem.");
            Assert.That(WebAssetFetchQueue.StripEmotePrefix("characters/reko_hdf/(a)"), Is.Empty);
        }

        [Test]
        public async Task FetchQueue_WebPStaticResolvesThePrefixlessSprite()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddJson("extensions.json", "{\"emote_extensions\":[\".webp\",\".webp.static\"]}");
            // Only the prefix-less file exists, exactly like the single-sprite characters on miku.pizza.
            server.AddBinary("characters/reko_hdf/brokentalk.webp", new byte[] { 42 });

            using HttpClient manifestClient = new HttpClient();
            WebAssetSource source = CreateSource(server.BaseUrl);
            WebAssetManifest manifest = await WebAssetManifest.FetchAsync(manifestClient, source);

            using Rig rig = new Rig(this, server, source, manifest);
            string? path = await rig.Queue.RequestAsync(
                "characters/reko_hdf/(a)brokentalk",
                WebAssetKind.CharacterSprite);

            Assert.That(path, Is.Not.Null,
                "(a)brokentalk.webp 404s; .webp.static must produce brokentalk.webp as a second candidate.");
            Assert.That(File.ReadAllBytes(path!), Is.EqualTo(new byte[] { 42 }));
        }

        [Test]
        public async Task FetchQueue_NegativeCacheDoesNotOutliveAProbeListChange()
        {
            using StubAssetServer server = new StubAssetServer();
            WebAssetSource source = CreateSource(server.BaseUrl);
            WebAssetFormatMemory memory = new WebAssetFormatMemory();
            WebAssetStats stats = new WebAssetStats();
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Start with a narrow list, exactly the situation that poisoned the cache in the field.
            WebAssetManifest narrow = ManifestWithSpriteExtensions("[\".webp\"]");
            WebAssetManifest wide = ManifestWithSpriteExtensions("[\".webp\",\".png\"]");
            WebAssetManifest active = narrow;

            using WebAssetFetchQueue queue =
                new WebAssetFetchQueue(source, memory, () => active, stats, client);

            Assert.That(await queue.RequestAsync("characters/x/(a)normal", WebAssetKind.CharacterSprite), Is.Null);
            server.ResetRequestLog();

            Assert.That(await queue.RequestAsync("characters/x/(a)normal", WebAssetKind.CharacterSprite), Is.Null);
            Assert.That(server.RequestCount, Is.Zero, "Same probe list: the negative cache must suppress.");

            // Widen the list; the recorded miss no longer describes what would be tried.
            active = wide;
            server.AddBinary("characters/x/(a)normal.png", new byte[] { 1 });
            server.ResetRequestLog();

            string? path = await queue.RequestAsync("characters/x/(a)normal", WebAssetKind.CharacterSprite);

            Assert.That(path, Is.Not.Null,
                "A miss means 'absent among THOSE candidates'. A wider list must be probed, not suppressed.");
        }

        /// <summary>Builds a manifest whose sprite extension list is the given JSON array.</summary>
        private static WebAssetManifest ManifestWithSpriteExtensions(string jsonArray)
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddJson("extensions.json", "{\"emote_extensions\":" + jsonArray + "}");
            using HttpClient client = new HttpClient();
            WebAssetSource source = WebAssetSource.TryCreate(server.BaseUrl)!;
            return WebAssetManifest.FetchAsync(client, source).GetAwaiter().GetResult();
        }

        [Test]
        public void HasKnownExtension_DistinguishesAStemFromAFilename()
        {
            Assert.That(WebAssetFetchQueue.HasKnownExtension("characters/phoenix/char.ini"), Is.True);
            Assert.That(WebAssetFetchQueue.HasKnownExtension("characters/phoenix/(a)normal.webp"), Is.True);
            Assert.That(WebAssetFetchQueue.HasKnownExtension("characters/phoenix/(a)normal"), Is.False);
            Assert.That(WebAssetFetchQueue.HasKnownExtension("background/court"), Is.False);
        }

        [Test]
        public async Task FetchQueue_DownloadsTheFirstExistingCandidateIntoTheMirror()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("characters/phoenix/(a)normal.png", new byte[] { 1, 2, 3, 4 });

            using Rig rig = new Rig(this, server);
            List<WebAssetMaterializedEventArgs> materialized = new List<WebAssetMaterializedEventArgs>();
            rig.Queue.Materialized += args => { lock (materialized) { materialized.Add(args); } };

            string? path = await rig.Queue.RequestAsync("characters/phoenix/(a)normal", WebAssetKind.CharacterSprite);

            Assert.That(path, Is.Not.Null);
            Assert.That(File.Exists(path!), Is.True);
            Assert.That(File.ReadAllBytes(path!), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(path, Does.EndWith(Path.Combine("characters", "phoenix", "(a)normal.png")));

            Assert.That(materialized, Has.Count.EqualTo(1));
            Assert.That(materialized[0].Stem, Is.EqualTo("characters/phoenix/(a)normal"));
            Assert.That(materialized[0].VPath, Is.EqualTo("characters/phoenix/(a)normal.png"));
            Assert.That(materialized[0].Kind, Is.EqualTo(WebAssetKind.CharacterSprite));

            Assert.That(rig.FormatMemory.GetLearned(WebAssetKind.CharacterSprite), Is.EqualTo(".png"),
                "The working extension must be learned so the next asset costs one request.");
        }

        [Test]
        public async Task FetchQueue_LearnedFormatCollapsesTheSecondAssetToASingleRequest()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("characters/phoenix/(a)normal.png", new byte[] { 1 });
            server.AddBinary("characters/phoenix/(b)normal.png", new byte[] { 2 });

            using Rig rig = new Rig(this, server);

            await rig.Queue.RequestAsync("characters/phoenix/(a)normal", WebAssetKind.CharacterSprite);
            int requestsAfterFirst = server.RequestCount;
            Assert.That(requestsAfterFirst, Is.GreaterThan(1),
                "The first asset walks the default order until .png answers.");

            server.ResetRequestLog();
            await rig.Queue.RequestAsync("characters/phoenix/(b)normal", WebAssetKind.CharacterSprite);

            Assert.That(server.RequestCount, Is.EqualTo(1),
                "Once .png is learned the next sprite must cost exactly one request (AsyncAO ADR-0001).");
        }

        [Test]
        public async Task FetchQueue_WarmMirrorServesWithoutAnyHttpRequest()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("background/court/wit.png", new byte[] { 9 });

            using Rig rig = new Rig(this, server);
            string? first = await rig.Queue.RequestAsync("background/court/wit", WebAssetKind.Background);
            Assert.That(first, Is.Not.Null);

            server.ResetRequestLog();
            string? second = await rig.Queue.RequestAsync("background/court/wit", WebAssetKind.Background);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(server.RequestCount, Is.Zero,
                "A previously materialized asset is a plain file now; it must never hit the network again.");
            Assert.That(rig.Stats.Snapshot().ServedFromMirror, Is.EqualTo(1));
        }

        [Test]
        public async Task FetchQueue_TotalMissIsNegativeCachedAndProbedOnlyOnce()
        {
            using StubAssetServer server = new StubAssetServer();
            using Rig rig = new Rig(this, server);

            string? first = await rig.Queue.RequestAsync("characters/ghost/(a)normal", WebAssetKind.CharacterSprite);
            Assert.That(first, Is.Null);
            int probes = server.RequestCount;
            Assert.That(probes, Is.EqualTo(WebAssetExtensions.Image.Count),
                "A cold miss walks the whole configured chain exactly once.");

            server.ResetRequestLog();
            string? second = await rig.Queue.RequestAsync("characters/ghost/(a)normal", WebAssetKind.CharacterSprite);

            Assert.That(second, Is.Null);
            Assert.That(server.RequestCount, Is.Zero, "The negative cache must suppress the re-probe.");
            Assert.That(rig.Stats.Snapshot().SuppressedByNegativeCache, Is.EqualTo(1));
        }

        [Test]
        public async Task FetchQueue_ManifestSuppressesProbingForAnUnlistedCharacter()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddJson("characters.json", "[\"Phoenix\"]");

            using HttpClient manifestClient = new HttpClient();
            WebAssetSource source = CreateSource(server.BaseUrl);
            WebAssetManifest manifest = await WebAssetManifest.FetchAsync(manifestClient, source);

            using Rig rig = new Rig(this, server, source, manifest);
            server.ResetRequestLog();

            string? path = await rig.Queue.RequestAsync("characters/edgeworth/(a)normal", WebAssetKind.CharacterSprite);

            Assert.That(path, Is.Null);
            Assert.That(server.RequestCount, Is.Zero,
                "characters.json proves the folder is absent, so probing is pure waste.");
            Assert.That(rig.Stats.Snapshot().SuppressedByManifest, Is.EqualTo(1));
        }

        [Test]
        public async Task FetchQueue_ExactFilenameSkipsExtensionProbing()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("characters/phoenix/char.ini", Encoding.UTF8.GetBytes("[Options]"));

            using Rig rig = new Rig(this, server);
            string? path = await rig.Queue.RequestAsync("characters/phoenix/char.ini", WebAssetKind.Config);

            Assert.That(path, Is.Not.Null);
            Assert.That(server.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FetchQueue_ConcurrentRequestsForOneAssetShareASingleDownload()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("characters/phoenix/(a)normal.webp", new byte[] { 7 });
            server.ResponseDelay = TimeSpan.FromMilliseconds(120);

            using Rig rig = new Rig(this, server);

            Task<string?>[] requests = Enumerable
                .Range(0, 8)
                .Select(_ => rig.Queue.RequestAsync("characters/phoenix/(a)normal", WebAssetKind.CharacterSprite))
                .ToArray();

            string?[] results = await Task.WhenAll(requests);

            Assert.That(results.Distinct().Count(), Is.EqualTo(1));
            Assert.That(results[0], Is.Not.Null);
            Assert.That(server.RequestCount, Is.EqualTo(1),
                "Eight callers wanting the same sprite must produce one download, not eight.");
        }

        [Test]
        public async Task FetchQueue_RequestWithGraceGivesUpButKeepsDownloading()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("characters/phoenix/(a)normal.webp", new byte[] { 5 });
            server.ResponseDelay = TimeSpan.FromMilliseconds(400);

            using Rig rig = new Rig(this, server);
            using ManualResetEventSlim landed = new ManualResetEventSlim(false);
            rig.Queue.Materialized += _ => landed.Set();

            string? withinGrace = await rig.Queue.RequestWithGraceAsync(
                "characters/phoenix/(a)normal", WebAssetKind.CharacterSprite, graceMilliseconds: 50);

            Assert.That(withinGrace, Is.Null, "The grace window must expire rather than block the frame.");
            Assert.That(landed.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "The download must continue after the grace window so the asset still appears.");
        }

        [Test]
        public async Task FetchQueue_ReturnsImmediatelyWhenTheGraceWindowIsAlreadySatisfied()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("background/court/wit.png", new byte[] { 3 });

            using Rig rig = new Rig(this, server);
            await rig.Queue.RequestAsync("background/court/wit", WebAssetKind.Background);

            string? warm = await rig.Queue.RequestWithGraceAsync(
                "background/court/wit", WebAssetKind.Background, graceMilliseconds: 150);

            Assert.That(warm, Is.Not.Null, "A warm asset must satisfy the grace window instantly.");
        }

        [Test]
        public async Task FetchQueue_LeavesNoTempFilesBehind()
        {
            using StubAssetServer server = new StubAssetServer();
            server.AddBinary("characters/phoenix/(a)normal.png", new byte[] { 1, 2 });

            using Rig rig = new Rig(this, server);
            await rig.Queue.RequestAsync("characters/phoenix/(a)normal", WebAssetKind.CharacterSprite);

            string[] leftovers = Directory
                .GetFiles(rig.Source.MirrorRoot, "*.tmp-*", SearchOption.AllDirectories);
            Assert.That(leftovers, Is.Empty,
                "Downloads write through a temp file; a torn write must never be visible as an asset.");
        }

        [Test]
        public async Task FetchQueue_UnreachableHostTripsTheCircuitBreaker()
        {
            // Port 1 is reserved and refuses connections, standing in for a dead asset host.
            WebAssetSource source = CreateSource("http://127.0.0.1:1/base/");
            WebAssetStats stats = new WebAssetStats();
            WebAssetFormatMemory memory = new WebAssetFormatMemory();
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using WebAssetFetchQueue queue =
                new WebAssetFetchQueue(source, memory, () => new WebAssetManifest(), stats, client);

            for (int i = 0; i < WebAssetFetchQueue.TransportFailureThreshold; i++)
            {
                await queue.RequestAsync($"characters/ghost{i}/(a)normal", WebAssetKind.CharacterSprite);
            }

            Assert.That(queue.IsHostReachable, Is.False,
                "A dead host must be abandoned rather than costing a timeout on every asset.");

            long httpBefore = stats.Snapshot().HttpRequests;
            await queue.RequestAsync("characters/another/(a)normal", WebAssetKind.CharacterSprite);
            Assert.That(stats.Snapshot().HttpRequests, Is.EqualTo(httpBefore),
                "Once the breaker is open no further requests may be issued.");
        }

        [Test]
        public void FetchQueue_RejectsTraversalWithoutTouchingTheNetwork()
        {
            using StubAssetServer server = new StubAssetServer();
            using Rig rig = new Rig(this, server);

            Assert.That(rig.Queue.FindInMirror("../../secrets.txt", WebAssetKind.Config), Is.Null);
            Assert.That(rig.Queue.RequestAsync("../../secrets.txt", WebAssetKind.Config).Result, Is.Null);
            Assert.That(server.RequestCount, Is.Zero);
        }

        // ── Stats ───────────────────────────────────────────────────────────────

        [Test]
        public void Stats_TrackRequestsPerAssetAndPeakLatency()
        {
            WebAssetStats stats = new WebAssetStats();
            stats.CountHttpRequest();
            stats.CountHttpRequest();
            stats.CountHttpRequest();
            stats.CountMaterialized(1000, 40);
            stats.CountMaterialized(2000, 120);

            WebAssetStatsSnapshot snapshot = stats.Snapshot();
            Assert.That(snapshot.Materialized, Is.EqualTo(2));
            Assert.That(snapshot.BytesDownloaded, Is.EqualTo(3000));
            Assert.That(snapshot.RequestsPerAsset, Is.EqualTo(1.5d).Within(0.001));
            Assert.That(snapshot.AverageLatencyMs, Is.EqualTo(80d).Within(0.001));
            Assert.That(snapshot.MaxLatencyMs, Is.EqualTo(120));
            Assert.That(snapshot.ToSummaryLine(), Does.StartWith("[WEB-SUMMARY]"));
        }

        // ── Service install ─────────────────────────────────────────────────────

        [Test]
        public void Install_WithoutAnAssetUrlLeavesFallbackOff()
        {
            Assert.That(WebAssetService.Install(null), Is.Null);
            Assert.That(WebAssetService.Current, Is.Null);
            Assert.That(WebAssetService.IsActive, Is.False);
        }

        [Test]
        public void Install_MountsTheMirrorAndUninstallRemovesIt()
        {
            List<string> original = Globals.BaseFolders;
            try
            {
                Globals.BaseFolders = new List<string> { @"C:\AO2\base" };

                WebAssetService? service = WebAssetService.Install("https://example.com/base/");
                Assert.That(service, Is.Not.Null);
                temporaryDirectories.Add(service!.Source.MirrorRoot);

                Assert.That(Directory.Exists(service.Source.MirrorRoot), Is.True);
                Assert.That(Globals.BaseFolders.Last(), Is.EqualTo(service.Source.MirrorRoot));
                Assert.That(WebAssetService.Current, Is.SameAs(service));

                WebAssetService.Uninstall();

                Assert.That(WebAssetService.Current, Is.Null);
                Assert.That(Globals.BaseFolders, Does.Not.Contain(service.Source.MirrorRoot));
            }
            finally
            {
                Globals.BaseFolders = original;
            }
        }

        [Test]
        public void Install_SameServerTwiceKeepsTheWarmMirror()
        {
            List<string> original = Globals.BaseFolders;
            try
            {
                Globals.BaseFolders = new List<string> { @"C:\AO2\base" };

                WebAssetService? first = WebAssetService.Install("https://example.com/base");
                WebAssetService? second = WebAssetService.Install("https://example.com/base/");

                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.SameAs(first),
                    "A reconnect to the same server must not throw away learned formats or the mirror.");
                temporaryDirectories.Add(first!.Source.MirrorRoot);
            }
            finally
            {
                Globals.BaseFolders = original;
            }
        }

        [Test]
        public void ReapplyMount_RestoresTheMirrorAfterBaseFoldersAreRebuilt()
        {
            List<string> original = Globals.BaseFolders;
            try
            {
                Globals.BaseFolders = new List<string> { @"C:\AO2\base" };
                WebAssetService? service = WebAssetService.Install("https://example.com/base/");
                Assert.That(service, Is.Not.Null);
                temporaryDirectories.Add(service!.Source.MirrorRoot);

                // Simulate Globals.UpdateConfigINI rebuilding the mount list from config.ini.
                Globals.BaseFolders = new List<string> { @"C:\AO2\base", @"C:\packs\extra" };
                WebAssetService.ReapplyMount();

                Assert.That(Globals.BaseFolders.Last(), Is.EqualTo(service.Source.MirrorRoot));
            }
            finally
            {
                Globals.BaseFolders = original;
            }
        }

        [Test]
        public void RequestIfActive_IsASafeNoOpWhenFallbackIsNotInstalled()
        {
            WebAssetService.Uninstall();
            Assert.DoesNotThrow(() =>
                WebAssetService.RequestIfActive("characters/phoenix/(a)normal", WebAssetKind.CharacterSprite));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private WebAssetSource CreateSource(string assetUrl)
        {
            WebAssetSource? source = WebAssetSource.TryCreate(assetUrl);
            Assert.That(source, Is.Not.Null, $"Expected {assetUrl} to be a usable asset URL.");
            temporaryDirectories.Add(source!.MirrorRoot);

            // Every test starts from a cold mirror so warm-cache behavior is explicit, not incidental.
            if (Directory.Exists(source.MirrorRoot))
            {
                Directory.Delete(source.MirrorRoot, recursive: true);
            }

            return source;
        }

        private string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "OceanyaWebAssetTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            temporaryDirectories.Add(path);
            return path;
        }

        /// <summary>Bundles a source, format memory, stats, and queue over one stub server.</summary>
        private sealed class Rig : IDisposable
        {
            public Rig(
                WebAssetFallbackTests owner,
                StubAssetServer server,
                WebAssetSource? source = null,
                WebAssetManifest? manifest = null)
            {
                Source = source ?? owner.CreateSource(server.BaseUrl);
                FormatMemory = new WebAssetFormatMemory();
                Stats = new WebAssetStats();
                HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                WebAssetManifest effective = manifest ?? new WebAssetManifest();
                Queue = new WebAssetFetchQueue(Source, FormatMemory, () => effective, Stats, HttpClient);
            }

            public WebAssetSource Source { get; }

            public WebAssetFormatMemory FormatMemory { get; }

            public WebAssetStats Stats { get; }

            public WebAssetFetchQueue Queue { get; }

            private HttpClient HttpClient { get; }

            public void Dispose()
            {
                Queue.Dispose();
                HttpClient.Dispose();
            }
        }

        /// <summary>
        /// Minimal in-process HTTP asset server: serves registered paths, 404s everything else, and
        /// records how many requests it received so probe economy can be asserted.
        /// </summary>
        private sealed class StubAssetServer : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            private readonly ConcurrentDictionary<string, (byte[] Body, string ContentType)> files =
                new ConcurrentDictionary<string, (byte[], string)>(StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentQueue<string> requestLog = new ConcurrentQueue<string>();
            private readonly CancellationTokenSource shutdown = new CancellationTokenSource();

            public StubAssetServer()
            {
                int port = GetFreePort();
                BaseUrl = $"http://127.0.0.1:{port}/base/";
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                _ = Task.Run(AcceptLoopAsync);
            }

            public string BaseUrl { get; }

            /// <summary>Artificial per-response latency, for grace-window and dedup tests.</summary>
            public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

            public int RequestCount => requestLog.Count;

            public void AddBinary(string relativePath, byte[] body)
                => files[relativePath.ToLowerInvariant()] = (body, "application/octet-stream");

            public void AddJson(string relativePath, string json)
                => files[relativePath.ToLowerInvariant()] = (Encoding.UTF8.GetBytes(json), "application/json");

            public void ResetRequestLog()
            {
                while (requestLog.TryDequeue(out _))
                {
                }
            }

            private async Task AcceptLoopAsync()
            {
                while (!shutdown.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        return; // Listener stopped.
                    }

                    _ = Task.Run(() => HandleAsync(context));
                }
            }

            private async Task HandleAsync(HttpListenerContext context)
            {
                try
                {
                    string relative = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath)
                        .TrimStart('/');
                    if (relative.StartsWith("base/", StringComparison.OrdinalIgnoreCase))
                    {
                        relative = relative.Substring("base/".Length);
                    }

                    requestLog.Enqueue(relative);

                    if (ResponseDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(ResponseDelay).ConfigureAwait(false);
                    }

                    if (files.TryGetValue(relative, out (byte[] Body, string ContentType) file))
                    {
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = file.ContentType;
                        context.Response.ContentLength64 = file.Body.Length;
                        await context.Response.OutputStream.WriteAsync(file.Body).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                }
                catch
                {
                    // A dropped stub response only ever surfaces as a test failure elsewhere.
                }
                finally
                {
                    try
                    {
                        context.Response.Close();
                    }
                    catch
                    {
                        // Already closed.
                    }
                }
            }

            private static int GetFreePort()
            {
                System.Net.Sockets.TcpListener probe =
                    new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }

            public void Dispose()
            {
                shutdown.Cancel();
                try
                {
                    listener.Stop();
                    listener.Close();
                }
                catch
                {
                    // Shutdown races are not actionable.
                }

                shutdown.Dispose();
            }
        }
    }
}
