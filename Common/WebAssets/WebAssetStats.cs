using System;
using System.Globalization;
using System.Threading;

namespace Common.WebAssets
{
    /// <summary>
    /// Immutable snapshot of the web asset pipeline's counters, used by the debug console and by
    /// latency regression tests.
    /// </summary>
    public readonly record struct WebAssetStatsSnapshot(
        long Requests,
        long Materialized,
        long Misses,
        long ServedFromMirror,
        long SuppressedByManifest,
        long SuppressedByNegativeCache,
        long HttpRequests,
        long BytesDownloaded,
        long TotalLatencyMs,
        long MaxLatencyMs)
    {
        /// <summary>Mean wall-clock milliseconds from enqueue to materialization.</summary>
        public double AverageLatencyMs => Materialized == 0 ? 0d : (double)TotalLatencyMs / Materialized;

        /// <summary>
        /// Mean HTTP requests per materialized asset. Approaches 1.0 once learned formats settle,
        /// which is the headline number from AsyncAO ADR-0001.
        /// </summary>
        public double RequestsPerAsset => Materialized == 0 ? 0d : (double)HttpRequests / Materialized;

        /// <summary>One-line summary for the <c>[WEB]</c> debug category.</summary>
        public string ToSummaryLine()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[WEB-SUMMARY] materialized={0} misses={1} mirrorHits={2} skipManifest={3} skipNegCache={4} "
                + "http={5} reqPerAsset={6:0.00} bytes={7} avgMs={8:0.0} maxMs={9}",
                Materialized,
                Misses,
                ServedFromMirror,
                SuppressedByManifest,
                SuppressedByNegativeCache,
                HttpRequests,
                RequestsPerAsset,
                BytesDownloaded,
                AverageLatencyMs,
                MaxLatencyMs);
        }
    }

    /// <summary>
    /// Lock-free counters for the web asset pipeline. Every increment is a single interlocked add, so
    /// instrumentation stays cheap enough to leave permanently enabled.
    /// </summary>
    public sealed class WebAssetStats
    {
        private long requests;
        private long materialized;
        private long misses;
        private long servedFromMirror;
        private long suppressedByManifest;
        private long suppressedByNegativeCache;
        private long httpRequests;
        private long bytesDownloaded;
        private long totalLatencyMs;
        private long maxLatencyMs;

        /// <summary>An asset fetch was requested by a resolver.</summary>
        public void CountRequest() => Interlocked.Increment(ref requests);

        /// <summary>A request was answered by a file already present in the mirror.</summary>
        public void CountMirrorHit() => Interlocked.Increment(ref servedFromMirror);

        /// <summary>A request was dropped because the manifest proves the asset does not exist.</summary>
        public void CountManifestSuppression() => Interlocked.Increment(ref suppressedByManifest);

        /// <summary>A request was dropped because of a live negative-cache entry.</summary>
        public void CountNegativeCacheSuppression() => Interlocked.Increment(ref suppressedByNegativeCache);

        /// <summary>One HTTP round trip (probe or download) was issued.</summary>
        public void CountHttpRequest() => Interlocked.Increment(ref httpRequests);

        /// <summary>Every candidate 404'd.</summary>
        public void CountMiss() => Interlocked.Increment(ref misses);

        /// <summary>An asset was downloaded and written into the mirror.</summary>
        public void CountMaterialized(long bytes, long latencyMs)
        {
            Interlocked.Increment(ref materialized);
            Interlocked.Add(ref bytesDownloaded, Math.Max(0, bytes));
            Interlocked.Add(ref totalLatencyMs, Math.Max(0, latencyMs));

            long observed = Math.Max(0, latencyMs);
            long current = Interlocked.Read(ref maxLatencyMs);
            while (observed > current)
            {
                long previous = Interlocked.CompareExchange(ref maxLatencyMs, observed, current);
                if (previous == current)
                {
                    break;
                }

                current = previous;
            }
        }

        /// <summary>Takes a point-in-time snapshot of every counter.</summary>
        public WebAssetStatsSnapshot Snapshot() => new WebAssetStatsSnapshot(
            Interlocked.Read(ref requests),
            Interlocked.Read(ref materialized),
            Interlocked.Read(ref misses),
            Interlocked.Read(ref servedFromMirror),
            Interlocked.Read(ref suppressedByManifest),
            Interlocked.Read(ref suppressedByNegativeCache),
            Interlocked.Read(ref httpRequests),
            Interlocked.Read(ref bytesDownloaded),
            Interlocked.Read(ref totalLatencyMs),
            Interlocked.Read(ref maxLatencyMs));

        /// <summary>Resets every counter (new server connection).</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref requests, 0);
            Interlocked.Exchange(ref materialized, 0);
            Interlocked.Exchange(ref misses, 0);
            Interlocked.Exchange(ref servedFromMirror, 0);
            Interlocked.Exchange(ref suppressedByManifest, 0);
            Interlocked.Exchange(ref suppressedByNegativeCache, 0);
            Interlocked.Exchange(ref httpRequests, 0);
            Interlocked.Exchange(ref bytesDownloaded, 0);
            Interlocked.Exchange(ref totalLatencyMs, 0);
            Interlocked.Exchange(ref maxLatencyMs, 0);
        }
    }
}
