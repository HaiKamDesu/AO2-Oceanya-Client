using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Common.WebAssets
{
    /// <summary>How urgently a web asset is needed, which decides its concurrency lane.</summary>
    public enum WebAssetPriority
    {
        /// <summary>Speculative warm-up (roster characters, upcoming emote pages).</summary>
        Prefetch = 0,
        /// <summary>Needed by the message currently being displayed.</summary>
        Immediate = 1
    }

    /// <summary>Describes an asset that just landed in the mirror.</summary>
    /// <param name="Stem">Normalized VFS path without extension, as requested.</param>
    /// <param name="VPath">Normalized VFS path of the file that actually resolved, extension included.</param>
    /// <param name="LocalPath">Absolute path inside the mirror.</param>
    /// <param name="Kind">Asset kind.</param>
    public readonly record struct WebAssetMaterializedEventArgs(
        string Stem,
        string VPath,
        string LocalPath,
        WebAssetKind Kind);

    /// <summary>
    /// Downloads missing assets from the server's asset URL into the local mirror, off the UI thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The queue is deliberately the only component that touches the network. Sync resolvers never call
    /// into it except through the fire-and-forget <see cref="Request"/>, so a cold asset costs a
    /// resolver exactly one dictionary lookup and never a socket wait.
    /// </para>
    /// <para>
    /// Probing follows webAO's <c>filesExist.ts</c> (parallel candidates, first hit wins) narrowed by
    /// AsyncAO's learned formats: the remembered extension for a kind is tried first on its own, so the
    /// steady state is a single GET per asset. Only when that guess misses does the remaining chain get
    /// probed in parallel.
    /// </para>
    /// </remarks>
    public sealed class WebAssetFetchQueue : IDisposable
    {
        /// <summary>Concurrent downloads allowed for assets the current message needs.</summary>
        public const int ImmediateConcurrency = 4;

        /// <summary>
        /// Concurrent downloads allowed for speculative prefetches.
        /// </summary>
        /// <remarks>
        /// Wider than the immediate lane because the bulk work that uses it - mirroring a large
        /// server's <c>char.ini</c> files - is many tiny requests where latency, not bandwidth, is the
        /// limit. It still cannot starve on-demand assets: the two lanes have separate gates.
        /// </remarks>
        public const int PrefetchConcurrency = 8;

        /// <summary>Consecutive transport failures before the host is treated as unreachable.</summary>
        public const int TransportFailureThreshold = 3;

        /// <summary>How often the running counters are summarized into the debug console.</summary>
        private const int SummaryLogInterval = 25;

        private readonly WebAssetSource source;
        private readonly WebAssetFormatMemory formatMemory;
        private readonly Func<WebAssetManifest> manifestProvider;
        private readonly WebAssetStats stats;
        private readonly HttpClient httpClient;
        private readonly bool ownsHttpClient;

        private readonly SemaphoreSlim immediateGate = new SemaphoreSlim(ImmediateConcurrency, ImmediateConcurrency);
        private readonly SemaphoreSlim prefetchGate = new SemaphoreSlim(PrefetchConcurrency, PrefetchConcurrency);
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();

        private readonly ConcurrentDictionary<string, Task<string?>> inFlight =
            new ConcurrentDictionary<string, Task<string?>>(StringComparer.OrdinalIgnoreCase);

        private int consecutiveTransportFailures;
        private int completedForSummary;
        private bool disposed;

        /// <summary>Creates a queue for one asset source.</summary>
        /// <param name="manifestProvider">
        /// Supplies the current manifests. A provider rather than a value because the manifests are
        /// fetched asynchronously after install, and probing must be able to start before they land.
        /// </param>
        public WebAssetFetchQueue(
            WebAssetSource source,
            WebAssetFormatMemory formatMemory,
            Func<WebAssetManifest> manifestProvider,
            WebAssetStats stats,
            HttpClient? httpClient = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.formatMemory = formatMemory ?? throw new ArgumentNullException(nameof(formatMemory));
            this.manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
            this.stats = stats ?? throw new ArgumentNullException(nameof(stats));

            ownsHttpClient = httpClient == null;
            this.httpClient = httpClient ?? CreateDefaultHttpClient();
        }

        /// <summary>Raised on a worker thread once an asset has been written into the mirror.</summary>
        public event Action<WebAssetMaterializedEventArgs>? Materialized;

        /// <summary>
        /// False once the host has failed to answer <see cref="TransportFailureThreshold"/> times in a
        /// row, after which every request short-circuits and the client behaves exactly as it did
        /// before this feature existed.
        /// </summary>
        public bool IsHostReachable => Volatile.Read(ref consecutiveTransportFailures) < TransportFailureThreshold;

        /// <summary>Builds the HttpClient used when the caller supplies none.</summary>
        public static HttpClient CreateDefaultHttpClient()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = ImmediateConcurrency + PrefetchConcurrency,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20),
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
        }

        /// <summary>
        /// Fire-and-forget request used from synchronous resolvers. Returns immediately; the asset shows
        /// up through <see cref="Materialized"/>.
        /// </summary>
        public void Request(string stemOrPath, WebAssetKind kind, WebAssetPriority priority = WebAssetPriority.Immediate)
        {
            _ = RequestAsync(stemOrPath, kind, priority);
        }

        /// <summary>
        /// Requests an asset and returns the task that completes with its local mirror path, or
        /// <c>null</c> when the server does not have it. Concurrent callers share one download.
        /// </summary>
        public Task<string?> RequestAsync(
            string stemOrPath,
            WebAssetKind kind,
            WebAssetPriority priority = WebAssetPriority.Immediate)
        {
            if (disposed)
            {
                return Task.FromResult<string?>(null);
            }

            string normalized = WebAssetSource.NormalizeVPath(stemOrPath);
            if (normalized.Length == 0)
            {
                return Task.FromResult<string?>(null);
            }

            stats.CountRequest();

            if (!IsHostReachable)
            {
                return Task.FromResult<string?>(null);
            }

            string key = BuildKey(normalized, kind);

            if (manifestProvider().IsKnownAbsent(normalized))
            {
                stats.CountManifestSuppression();
                return Task.FromResult<string?>(null);
            }

            if (formatMemory.IsKnownMiss(key))
            {
                stats.CountNegativeCacheSuppression();
                return Task.FromResult<string?>(null);
            }

            // A previous session may already have materialized this asset; that is the common steady
            // state and must not cost a network round trip.
            string? alreadyPresent = FindInMirror(normalized, kind);
            if (alreadyPresent != null)
            {
                stats.CountMirrorHit();
                return Task.FromResult<string?>(alreadyPresent);
            }

            return inFlight.GetOrAdd(key, _ => RunAsync(normalized, kind, priority, key));
        }

        /// <summary>
        /// Awaits an asset for at most <paramref name="graceMilliseconds"/>, then gives up and lets the
        /// caller render without it. The download keeps running and surfaces through
        /// <see cref="Materialized"/>.
        /// </summary>
        public async Task<string?> RequestWithGraceAsync(
            string stemOrPath,
            WebAssetKind kind,
            int graceMilliseconds,
            WebAssetPriority priority = WebAssetPriority.Immediate)
        {
            Task<string?> request = RequestAsync(stemOrPath, kind, priority);
            if (request.IsCompleted || graceMilliseconds <= 0)
            {
                return request.IsCompletedSuccessfully ? request.Result : null;
            }

            Task completed = await Task.WhenAny(request, Task.Delay(graceMilliseconds)).ConfigureAwait(false);
            return ReferenceEquals(completed, request) && request.IsCompletedSuccessfully
                ? request.Result
                : null;
        }

        /// <summary>
        /// Returns the mirror path for an asset that has already been downloaded, or <c>null</c>.
        /// Pure filesystem probing - never touches the network.
        /// </summary>
        public string? FindInMirror(string stemOrPath, WebAssetKind kind)
        {
            string normalized = WebAssetSource.NormalizeVPath(stemOrPath);
            if (normalized.Length == 0)
            {
                return null;
            }

            foreach (string candidate in BuildCandidateVPaths(normalized, kind))
            {
                string localPath = source.BuildLocalPath(candidate);
                if (File.Exists(localPath))
                {
                    return localPath;
                }
            }

            return null;
        }

        /// <summary>Builds the ordered candidate VFS paths probed for a stem.</summary>
        public IReadOnlyList<string> BuildCandidateVPaths(string normalizedVPath, WebAssetKind kind)
        {
            if (HasKnownExtension(normalizedVPath))
            {
                return new[] { normalizedVPath };
            }

            IReadOnlyList<string> order = formatMemory.BuildProbeOrder(kind, manifestProvider().ExtensionsFor(kind));
            if (order.Count == 0)
            {
                return new[] { normalizedVPath };
            }

            List<string> candidates = new List<string>(order.Count);
            foreach (string extension in order)
            {
                string candidate = BuildCandidate(normalizedVPath, extension);
                if (candidate.Length > 0 && !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        /// <summary>
        /// Turns one configured extension into a candidate path, honouring webAO's
        /// <see cref="WebAssetExtensions.StaticWebPMarker"/> URL shape.
        /// </summary>
        private static string BuildCandidate(string normalizedVPath, string extension)
        {
            if (!string.Equals(extension, WebAssetExtensions.StaticWebPMarker, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedVPath + extension;
            }

            // webAO's setEmote.ts drops the (a)/(b) prefix for this entry: one .webp file serves both
            // the idle and talking halves of the emote.
            string stripped = StripEmotePrefix(normalizedVPath);
            return stripped.Length == 0 ? string.Empty : stripped + ".webp";
        }

        /// <summary>
        /// Removes a leading AO2 emote prefix (<c>(a)</c> / <c>(b)</c> / <c>(c)</c>) from the final path
        /// segment, or returns an empty string when there is none to remove.
        /// </summary>
        public static string StripEmotePrefix(string normalizedVPath)
        {
            int lastSlash = normalizedVPath.LastIndexOf('/');
            string fileName = lastSlash >= 0 ? normalizedVPath.Substring(lastSlash + 1) : normalizedVPath;
            if (fileName.Length < 4 || fileName[0] != '(' || fileName[2] != ')')
            {
                return string.Empty;
            }

            string remainder = fileName.Substring(3);
            return remainder.Length == 0
                ? string.Empty
                : (lastSlash >= 0 ? normalizedVPath.Substring(0, lastSlash + 1) + remainder : remainder);
        }

        /// <summary>Reports whether the final path segment already carries a recognised extension.</summary>
        public static bool HasKnownExtension(string normalizedVPath)
        {
            int lastSlash = normalizedVPath.LastIndexOf('/');
            string fileName = lastSlash >= 0 ? normalizedVPath.Substring(lastSlash + 1) : normalizedVPath;
            int lastDot = fileName.LastIndexOf('.');
            if (lastDot <= 0)
            {
                return false;
            }

            return WebAssetExtensions.All.Contains(fileName.Substring(lastDot));
        }

        /// <summary>
        /// Builds the dedup / negative-cache key for a request.
        /// </summary>
        /// <remarks>
        /// The probe list is part of the key on purpose. A miss only means "not found among THESE
        /// candidates", so a recorded miss must not suppress a later, different probe list. This bit us
        /// for real: a session where the effective list was a single wrong extension persisted hundreds
        /// of misses, and after the list was corrected the 24 h negative cache kept suppressing assets
        /// that now would have resolved.
        /// </remarks>
        private string BuildKey(string normalizedVPath, WebAssetKind kind)
            => kind.ToString() + "|" + BuildProbeSignature(kind) + "|" + normalizedVPath;

        /// <summary>
        /// A stable signature of the extension SET configured for a kind.
        /// </summary>
        /// <remarks>
        /// Deliberately sorted and independent of learned-format ordering. Order changes what is tried
        /// first, not what is covered, so folding it into the key would make every learn (and every
        /// <c>InvalidateLearned</c> after a total miss) flip the key and re-probe assets that are simply
        /// absent - an endless re-probe loop instead of a negative cache.
        /// </remarks>
        private string BuildProbeSignature(WebAssetKind kind)
        {
            IReadOnlyList<string> configured = manifestProvider().ExtensionsFor(kind);
            if (configured.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", configured.OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase));
        }

        private async Task<string?> RunAsync(
            string normalizedVPath,
            WebAssetKind kind,
            WebAssetPriority priority,
            string key)
        {
            // Yield first so a synchronous resolver never pays the gate wait on its own thread.
            await Task.Yield();

            SemaphoreSlim gate = priority == WebAssetPriority.Immediate ? immediateGate : prefetchGate;
            Stopwatch stopwatch = Stopwatch.StartNew();
            long queueWaitMs;

            try
            {
                await gate.WaitAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                inFlight.TryRemove(key, out _);
                return null;
            }

            queueWaitMs = stopwatch.ElapsedMilliseconds;

            try
            {
                return await DownloadAsync(normalizedVPath, kind, priority, key, stopwatch, queueWaitMs)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<string?> DownloadAsync(
            string normalizedVPath,
            WebAssetKind kind,
            WebAssetPriority priority,
            string key,
            Stopwatch stopwatch,
            long queueWaitMs)
        {
            IReadOnlyList<string> candidates = BuildCandidateVPaths(normalizedVPath, kind);
            int probeCount = 0;

            try
            {
                foreach (string candidate in candidates)
                {
                    if (shutdown.IsCancellationRequested)
                    {
                        return null;
                    }

                    probeCount++;
                    stats.CountHttpRequest();

                    byte[]? payload = await TryGetBytesAsync(source.BuildUrl(candidate)).ConfigureAwait(false);
                    if (payload == null)
                    {
                        continue;
                    }

                    string localPath = source.BuildLocalPath(candidate);
                    WriteAtomically(localPath, payload);

                    Volatile.Write(ref consecutiveTransportFailures, 0);
                    formatMemory.RecordSuccess(kind, GetExtension(candidate));
                    formatMemory.ClearMiss(key);

                    long totalMs = stopwatch.ElapsedMilliseconds;
                    stats.CountMaterialized(payload.LongLength, totalMs);

                    CustomConsole.Debug(
                        $"[WEB] hit {normalizedVPath} kind={kind} ext={GetExtension(candidate)} probes={probeCount} "
                        + $"bytes={payload.LongLength} queueMs={queueWaitMs} totalMs={totalMs} prio={priority}",
                        CustomConsole.LogCategory.WebAssets);

                    RaiseMaterialized(new WebAssetMaterializedEventArgs(normalizedVPath, candidate, localPath, kind));
                    LogSummaryIfDue();
                    return localPath;
                }

                // Every candidate 404'd. If the learned format was among them it is now stale.
                formatMemory.InvalidateLearned(kind);
                formatMemory.RecordMiss(key);
                stats.CountMiss();

                CustomConsole.Debug(
                    $"[WEB] miss {normalizedVPath} kind={kind} tried=[{string.Join(", ", candidates.Select(GetExtension))}] "
                    + $"probes={probeCount} queueMs={queueWaitMs} totalMs={stopwatch.ElapsedMilliseconds}",
                    CustomConsole.LogCategory.WebAssets);

                LogSummaryIfDue();
                return null;
            }
            catch (Exception ex)
            {
                RecordTransportFailure(normalizedVPath, ex);
                return null;
            }
            finally
            {
                inFlight.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Issues one GET and returns its body, or <c>null</c> for a definitive "not found".
        /// A GET rather than a HEAD-then-GET pair keeps a successful probe at a single round trip.
        /// </summary>
        private async Task<byte[]?> TryGetBytesAsync(string url)
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseContentRead, shutdown.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound
                || response.StatusCode == HttpStatusCode.Forbidden
                || response.StatusCode == HttpStatusCode.Gone)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{(int)response.StatusCode} for {url}");
            }

            byte[] payload = await response.Content.ReadAsByteArrayAsync(shutdown.Token).ConfigureAwait(false);
            return payload.Length == 0 ? null : payload;
        }

        /// <summary>
        /// Writes through a uniquely named temp file so a torn download can never be observed as a
        /// valid asset by the resolvers reading the mirror concurrently.
        /// </summary>
        private static void WriteAtomically(string localPath, byte[] payload)
        {
            string directory = Path.GetDirectoryName(localPath) ?? string.Empty;
            if (directory.Length > 0)
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = localPath + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                File.WriteAllBytes(tempPath, payload);
                File.Move(tempPath, localPath, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Best effort cleanup only.
                }

                throw;
            }
        }

        private void RecordTransportFailure(string normalizedVPath, Exception ex)
        {
            // A cancellation caused by OUR shutdown is not the host's fault. A cancellation caused by
            // the HttpClient timeout is exactly the failure this breaker exists for: a host that hangs
            // costs far more than one that refuses, so it must count toward tripping.
            if (ex is OperationCanceledException && shutdown.IsCancellationRequested)
            {
                return;
            }

            int failures = Interlocked.Increment(ref consecutiveTransportFailures);
            CustomConsole.Debug(
                $"[WEB] transport failure {failures}/{TransportFailureThreshold} for {normalizedVPath}: {ex.Message}",
                CustomConsole.LogCategory.WebAssets);

            if (failures == TransportFailureThreshold)
            {
                CustomConsole.Warning(
                    $"[WEB] Asset host {source.BaseUrl} unreachable after {TransportFailureThreshold} consecutive "
                    + "failures; web asset fallback disabled for this session.",
                    null,
                    CustomConsole.LogCategory.WebAssets);
            }
        }

        private void RaiseMaterialized(WebAssetMaterializedEventArgs args)
        {
            try
            {
                Materialized?.Invoke(args);
            }
            catch (Exception ex)
            {
                CustomConsole.Error("A web asset materialization handler threw.", ex, CustomConsole.LogCategory.WebAssets);
            }
        }

        private void LogSummaryIfDue()
        {
            if (Interlocked.Increment(ref completedForSummary) % SummaryLogInterval != 0)
            {
                return;
            }

            CustomConsole.Info(stats.Snapshot().ToSummaryLine(), CustomConsole.LogCategory.WebAssets);
            formatMemory.Flush();
        }

        private static string GetExtension(string candidate)
        {
            int lastDot = candidate.LastIndexOf('.');
            int lastSlash = candidate.LastIndexOf('/');
            return lastDot > lastSlash && lastDot >= 0 ? candidate.Substring(lastDot) : string.Empty;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                shutdown.Cancel();
            }
            catch
            {
                // Cancellation races during shutdown are not actionable.
            }

            formatMemory.Flush();
            CustomConsole.Info(stats.Snapshot().ToSummaryLine(), CustomConsole.LogCategory.WebAssets);

            shutdown.Dispose();
            immediateGate.Dispose();
            prefetchGate.Dispose();

            if (ownsHttpClient)
            {
                httpClient.Dispose();
            }
        }
    }
}
