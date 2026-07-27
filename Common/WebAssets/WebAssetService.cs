using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Common.WebAssets
{
    /// <summary>
    /// Entry point for webAO-style asset fallback: owns the active <see cref="WebAssetSource"/>, mounts
    /// its mirror as the lowest-priority entry of <see cref="Globals.BaseFolders"/>, and exposes the
    /// non-blocking request surface the synchronous resolvers call on a local miss.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mirror is mounted LAST so a user's physical files always win. Because every existing
    /// resolver already walks <see cref="Globals.BaseFolders"/> first-hit-wins, no resolver needs to
    /// know this feature exists in order to prefer local content.
    /// </para>
    /// <para>
    /// The service is intentionally only installed for GM Multi-Client sessions. Offline tools such as
    /// the Character Database Viewer keep seeing physical files exclusively.
    /// </para>
    /// </remarks>
    public sealed class WebAssetService : IDisposable
    {
        private static readonly object installLock = new object();
        private static WebAssetService? current;

        private readonly WebAssetFetchQueue queue;
        private WebAssetManifest manifest;
        private bool disposed;

        private WebAssetService(
            WebAssetSource source,
            WebAssetFormatMemory formatMemory,
            WebAssetManifest manifest,
            WebAssetStats stats,
            HttpClient? httpClient)
        {
            Source = source;
            FormatMemory = formatMemory;
            this.manifest = manifest;
            Stats = stats;
            queue = new WebAssetFetchQueue(source, formatMemory, () => Volatile.Read(ref this.manifest), stats, httpClient);
            queue.Materialized += args =>
            {
                Materialized?.Invoke(args);
                AnyMaterialized?.Invoke(args);
            };
        }

        /// <summary>The installed service, or <c>null</c> when web asset fallback is not active.</summary>
        public static WebAssetService? Current => Volatile.Read(ref current);

        /// <summary>Reports whether fallback is active and its host is still answering.</summary>
        public static bool IsActive
        {
            get
            {
                WebAssetService? active = Current;
                return active is { disposed: false } && active.queue.IsHostReachable;
            }
        }

        /// <summary>Asset origin and mirror location for the connected server.</summary>
        public WebAssetSource Source { get; }

        /// <summary>Learned formats and negative cache for this server.</summary>
        public WebAssetFormatMemory FormatMemory { get; }

        /// <summary>
        /// Root manifests published by this server. Starts empty and is replaced once the background
        /// fetch completes, so probing works from the first frame and only gets cheaper afterwards.
        /// </summary>
        public WebAssetManifest Manifest => Volatile.Read(ref manifest);

        /// <summary>Whether the background manifest fetch has completed.</summary>
        public bool ManifestsLoaded { get; private set; }

        /// <summary>Live pipeline counters.</summary>
        public WebAssetStats Stats { get; }

        /// <summary>Raised on a worker thread when an asset lands in the mirror.</summary>
        public event Action<WebAssetMaterializedEventArgs>? Materialized;

        /// <summary>
        /// Raised on a worker thread whenever ANY installed service materializes an asset.
        /// </summary>
        /// <remarks>
        /// UI code subscribes here rather than to <see cref="Materialized"/> so it does not have to
        /// re-subscribe every time the player connects to a different server.
        /// </remarks>
        public static event Action<WebAssetMaterializedEventArgs>? AnyMaterialized;

        /// <summary>
        /// Installs fallback for <paramref name="assetUrl"/>, replacing any previous installation.
        /// The mirror is mounted synchronously so resolvers see it immediately; manifests are fetched
        /// in the background because they only make later probing cheaper.
        /// </summary>
        /// <returns>The installed service, or <c>null</c> when the server published no usable asset URL.</returns>
        public static WebAssetService? Install(string? assetUrl, HttpClient? httpClient = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            WebAssetSource? source = WebAssetSource.TryCreate(assetUrl);
            if (source == null)
            {
                CustomConsole.Info(
                    "[WEB] Server published no usable asset URL; web asset fallback stays off.",
                    CustomConsole.LogCategory.WebAssets);
                Uninstall();
                return null;
            }

            lock (installLock)
            {
                WebAssetService? existing = Volatile.Read(ref current);
                if (existing != null
                    && !existing.disposed
                    && string.Equals(existing.Source.BaseUrl, source.BaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    // Same server reconnecting - keep the warm mirror, learned formats, and manifests.
                    EnsureMounted(existing.Source.MirrorRoot);
                    return existing;
                }

                existing?.Dispose();

                long mirrorReadyMs;
                try
                {
                    Directory.CreateDirectory(source.MirrorRoot);
                    mirrorReadyMs = stopwatch.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    CustomConsole.Error(
                        $"Could not create the web asset mirror at {source.MirrorRoot}; fallback disabled.",
                        ex,
                        CustomConsole.LogCategory.WebAssets);
                    Volatile.Write(ref current, null);
                    return null;
                }

                WebAssetFormatMemory formatMemory =
                    new WebAssetFormatMemory(source.MirrorRoot, WebAssetFormatMemory.DefaultMissTtl);
                WebAssetStats stats = new WebAssetStats();
                WebAssetManifest manifest = new WebAssetManifest();

                WebAssetService service = new WebAssetService(source, formatMemory, manifest, stats, httpClient);
                Volatile.Write(ref current, service);

                EnsureMounted(source.MirrorRoot);
                long mountedMs = stopwatch.ElapsedMilliseconds;

                CustomConsole.Info(
                    $"[WEB] Fallback installed for {source.BaseUrl} | mirror={source.MirrorRoot} "
                    + $"mirrorMs={mirrorReadyMs} mountMs={mountedMs} "
                    + $"learned={formatMemory.LearnedCount} knownMisses={formatMemory.MissCount}",
                    CustomConsole.LogCategory.WebAssets);

                service.StartManifestRefresh(httpClient);
                return service;
            }
        }

        /// <summary>Removes fallback and unmounts the mirror.</summary>
        public static void Uninstall()
        {
            lock (installLock)
            {
                WebAssetService? existing = Volatile.Read(ref current);
                Volatile.Write(ref current, null);
                existing?.Dispose();
            }
        }

        /// <summary>
        /// Re-appends the mirror after <see cref="Globals.BaseFolders"/> was rebuilt (config.ini change).
        /// No-op when fallback is not installed.
        /// </summary>
        public static void ReapplyMount()
        {
            WebAssetService? active = Current;
            if (active is { disposed: false })
            {
                EnsureMounted(active.Source.MirrorRoot);
            }
        }

        /// <summary>
        /// Non-blocking request used by synchronous resolvers on a local miss. Safe to call when the
        /// service is not installed.
        /// </summary>
        public static void RequestIfActive(
            string stemOrPath,
            WebAssetKind kind,
            WebAssetPriority priority = WebAssetPriority.Immediate)
        {
            WebAssetService? active = Current;
            if (active is { disposed: false })
            {
                active.queue.Request(stemOrPath, kind, priority);
            }
        }

        /// <summary>
        /// How long the viewport may hold a message before rendering it, waiting for a cold sprite.
        /// </summary>
        /// <remarks>
        /// Sits well under AO2's own inter-message <c>stay_time</c> pacing, so a download that lands
        /// inside the window produces no visible pop-in and no perceptible delay. If it expires the
        /// message renders anyway and the sprite swaps in when it arrives - never a stall.
        /// </remarks>
        public const int DefaultGraceWindowMilliseconds = 150;

        /// <summary>
        /// Speculative warm-up request. Safe to call when the service is not installed.
        /// </summary>
        public static void PrefetchIfActive(string stemOrPath, WebAssetKind kind)
            => RequestIfActive(stemOrPath, kind, WebAssetPriority.Prefetch);

        /// <summary>
        /// Returns an already-downloaded mirror path, or <c>null</c>. Never touches the network, so it
        /// is safe to call from a synchronous render path.
        /// </summary>
        public static string? FindInMirrorIfActive(string stemOrPath, WebAssetKind kind)
        {
            WebAssetService? active = Current;
            return active is { disposed: false } ? active.FindInMirror(stemOrPath, kind) : null;
        }

        /// <summary>Requests an asset and returns the task that yields its mirror path, or <c>null</c>.</summary>
        public Task<string?> RequestAsync(
            string stemOrPath,
            WebAssetKind kind,
            WebAssetPriority priority = WebAssetPriority.Immediate)
            => disposed ? Task.FromResult<string?>(null) : queue.RequestAsync(stemOrPath, kind, priority);

        /// <summary>
        /// Waits up to <paramref name="graceMilliseconds"/> for an asset before giving up, so a fast
        /// download can complete before the frame that needs it is drawn.
        /// </summary>
        public Task<string?> RequestWithGraceAsync(
            string stemOrPath,
            WebAssetKind kind,
            int graceMilliseconds,
            WebAssetPriority priority = WebAssetPriority.Immediate)
            => disposed
                ? Task.FromResult<string?>(null)
                : queue.RequestWithGraceAsync(stemOrPath, kind, graceMilliseconds, priority);

        /// <summary>Returns an already-downloaded mirror path without touching the network.</summary>
        public string? FindInMirror(string stemOrPath, WebAssetKind kind)
            => disposed ? null : queue.FindInMirror(stemOrPath, kind);

        /// <summary>Writes a counter summary to the debug console and flushes the format memory.</summary>
        public void LogSummary()
        {
            CustomConsole.Info(Stats.Snapshot().ToSummaryLine(), CustomConsole.LogCategory.WebAssets);
            FormatMemory.Flush();
        }

        /// <summary>
        /// Appends <paramref name="mirrorRoot"/> to <see cref="Globals.BaseFolders"/> as the lowest
        /// priority entry, if it is not already the last one.
        /// </summary>
        public static void EnsureMounted(string mirrorRoot)
        {
            if (string.IsNullOrWhiteSpace(mirrorRoot))
            {
                return;
            }

            List<string> folders = Globals.BaseFolders ??= new List<string>();
            lock (folders)
            {
                folders.RemoveAll(folder => string.Equals(folder, mirrorRoot, StringComparison.OrdinalIgnoreCase));
                folders.Add(mirrorRoot);
            }
        }

        /// <summary>Removes <paramref name="mirrorRoot"/> from <see cref="Globals.BaseFolders"/>.</summary>
        public static void EnsureUnmounted(string mirrorRoot)
        {
            List<string>? folders = Globals.BaseFolders;
            if (folders == null || string.IsNullOrWhiteSpace(mirrorRoot))
            {
                return;
            }

            lock (folders)
            {
                folders.RemoveAll(folder => string.Equals(folder, mirrorRoot, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void StartManifestRefresh(HttpClient? httpClient)
        {
            _ = Task.Run(async () =>
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                HttpClient client = httpClient ?? sharedManifestClient.Value;
                try
                {
                    WebAssetManifest fetched = await WebAssetManifest
                        .FetchAsync(client, Source, Stats)
                        .ConfigureAwait(false);
                    Volatile.Write(ref manifest, fetched);
                    ManifestsLoaded = true;
                    CustomConsole.Info(
                        $"[WEB] Manifest refresh for {Source.BaseUrl} took {stopwatch.ElapsedMilliseconds}ms",
                        CustomConsole.LogCategory.WebAssets);
                }
                catch (Exception ex)
                {
                    CustomConsole.Debug(
                        $"[WEB] Manifest refresh failed after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}",
                        CustomConsole.LogCategory.WebAssets);
                }
            });
        }

        private static readonly Lazy<HttpClient> sharedManifestClient =
            new Lazy<HttpClient>(WebAssetFetchQueue.CreateDefaultHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            EnsureUnmounted(Source.MirrorRoot);
            queue.Dispose();
        }
    }
}
