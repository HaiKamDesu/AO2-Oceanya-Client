using System;
using System.IO;

namespace Common
{
    /// <summary>
    /// Deletes abandoned per-config-hash cache files left behind in a shared cache directory
    /// (e.g. "characters_&lt;hash&gt;.json") so the directory does not grow forever as config.ini/mount
    /// paths change across sessions. Retention is age-based (not "keep only the current hash") so
    /// switching between a handful of regularly-used configs does not repeatedly destroy each other's cache.
    /// </summary>
    public static class CacheFilePruner
    {
        private static readonly TimeSpan DefaultRetentionAge = TimeSpan.FromDays(30);

        /// <summary>
        /// Deletes files in <paramref name="cacheRoot"/> matching "<paramref name="prefix"/>*.json" whose
        /// last-write time is older than <paramref name="retentionAge"/> (default 30 days), always
        /// preserving <paramref name="currentFilePath"/>. Best-effort: failures to delete an individual
        /// file (locked, in use, permissions) are swallowed so pruning never blocks a cache load.
        /// </summary>
        public static void PruneStaleCacheFiles(
            string cacheRoot,
            string prefix,
            string currentFilePath,
            TimeSpan? retentionAge = null)
        {
            try
            {
                if (!Directory.Exists(cacheRoot))
                {
                    return;
                }

                DateTime cutoffUtc = DateTime.UtcNow - (retentionAge ?? DefaultRetentionAge);

                foreach (string candidate in Directory.EnumerateFiles(cacheRoot, $"{prefix}*.json"))
                {
                    if (string.Equals(candidate, currentFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        if (File.GetLastWriteTimeUtc(candidate) >= cutoffUtc)
                        {
                            continue;
                        }

                        File.Delete(candidate);
                    }
                    catch
                    {
                        // Best-effort; a locked/in-use stale file will simply be retried next time.
                    }
                }
            }
            catch
            {
                // Pruning is best-effort and must never block a cache load.
            }
        }
    }
}
