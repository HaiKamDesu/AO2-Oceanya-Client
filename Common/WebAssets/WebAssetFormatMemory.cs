using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Common.WebAssets
{
    /// <summary>
    /// Per-server memory of which extension actually works for each <see cref="WebAssetKind"/>, plus a
    /// negative cache of assets the server does not have.
    /// </summary>
    /// <remarks>
    /// Learned formats come from AsyncAO's ADR-0001: after the first success for a kind, steady-state
    /// resolution is a single perfectly-aimed request instead of walking the whole extension chain.
    /// Unlike AsyncAO the learned extension is only moved to the FRONT of the probe order rather than
    /// replacing it, so mixed-format packs still resolve correctly - it costs one request when the
    /// guess is right and behaves exactly like an unlearned probe when it is wrong.
    /// </remarks>
    public sealed class WebAssetFormatMemory
    {
        private const string FileName = "format-memory.json";

        /// <summary>How long a recorded miss is trusted before the asset is probed again.</summary>
        public static readonly TimeSpan DefaultMissTtl = TimeSpan.FromHours(24);

        private readonly string? filePath;
        private readonly TimeSpan missTtl;
        private readonly ConcurrentDictionary<WebAssetKind, string> learned = new();
        private readonly ConcurrentDictionary<string, long> misses =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object saveLock = new object();
        private int dirty;

        /// <summary>Creates an in-memory-only instance (unit tests).</summary>
        public WebAssetFormatMemory()
            : this(null, DefaultMissTtl)
        {
        }

        /// <summary>Creates an instance persisted beside the mirror it belongs to.</summary>
        /// <param name="mirrorRoot">Mirror directory, or <c>null</c> for in-memory only.</param>
        /// <param name="missTtl">Lifetime of a negative-cache entry.</param>
        public WebAssetFormatMemory(string? mirrorRoot, TimeSpan missTtl)
        {
            this.missTtl = missTtl > TimeSpan.Zero ? missTtl : DefaultMissTtl;
            filePath = string.IsNullOrWhiteSpace(mirrorRoot)
                ? null
                : Path.Combine(mirrorRoot, FileName);
            Load();
        }

        /// <summary>Number of live negative-cache entries (diagnostics).</summary>
        public int MissCount => misses.Count;

        /// <summary>Number of kinds with a learned extension (diagnostics).</summary>
        public int LearnedCount => learned.Count;

        /// <summary>
        /// Returns the probe order for <paramref name="kind"/>, with the learned extension first when one
        /// is known. <paramref name="configured"/> is the manifest-provided or default order.
        /// </summary>
        public IReadOnlyList<string> BuildProbeOrder(WebAssetKind kind, IReadOnlyList<string> configured)
        {
            if (configured.Count == 0)
            {
                return configured;
            }

            if (!learned.TryGetValue(kind, out string? preferred)
                || string.IsNullOrEmpty(preferred)
                || configured.Count == 1)
            {
                return configured;
            }

            int index = -1;
            for (int i = 0; i < configured.Count; i++)
            {
                if (string.Equals(configured[i], preferred, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index <= 0)
            {
                // Already first, or no longer part of the configured order - nothing to reorder.
                return configured;
            }

            List<string> reordered = new List<string>(configured.Count) { configured[index] };
            for (int i = 0; i < configured.Count; i++)
            {
                if (i != index)
                {
                    reordered.Add(configured[i]);
                }
            }

            return reordered;
        }

        /// <summary>Returns the learned extension for <paramref name="kind"/>, if any.</summary>
        public string? GetLearned(WebAssetKind kind)
            => learned.TryGetValue(kind, out string? ext) ? ext : null;

        /// <summary>Records that <paramref name="extension"/> answered for <paramref name="kind"/>.</summary>
        public void RecordSuccess(WebAssetKind kind, string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return;
            }

            if (learned.TryGetValue(kind, out string? existing)
                && string.Equals(existing, extension, StringComparison.OrdinalIgnoreCase))
            {
                return; // no churn on the hot path
            }

            learned[kind] = extension;
            MarkDirty();
        }

        /// <summary>Forgets the learned extension for <paramref name="kind"/> after it started 404ing.</summary>
        public void InvalidateLearned(WebAssetKind kind)
        {
            if (learned.TryRemove(kind, out _))
            {
                MarkDirty();
            }
        }

        /// <summary>Records that no candidate for <paramref name="key"/> exists on the server.</summary>
        public void RecordMiss(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            misses[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            MarkDirty();
        }

        /// <summary>Reports whether <paramref name="key"/> is a live (non-expired) recorded miss.</summary>
        public bool IsKnownMiss(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !misses.TryGetValue(key, out long recordedAt))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - recordedAt <= (long)missTtl.TotalSeconds)
            {
                return true;
            }

            misses.TryRemove(key, out _);
            MarkDirty();
            return false;
        }

        /// <summary>Clears a recorded miss, e.g. after the asset was materialized by another path.</summary>
        public void ClearMiss(string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && misses.TryRemove(key, out _))
            {
                MarkDirty();
            }
        }

        /// <summary>Drops every learned format and recorded miss.</summary>
        public void Clear()
        {
            learned.Clear();
            misses.Clear();
            MarkDirty();
        }

        /// <summary>Writes the memory to disk when it changed since the last save. Cheap no-op otherwise.</summary>
        public void Flush()
        {
            if (filePath == null || System.Threading.Interlocked.Exchange(ref dirty, 0) == 0)
            {
                return;
            }

            lock (saveLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    PersistedState state = new PersistedState
                    {
                        Learned = learned.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
                        Misses = misses.ToDictionary(pair => pair.Key, pair => pair.Value)
                    };
                    File.WriteAllText(filePath, JsonConvert.SerializeObject(state));
                }
                catch (Exception ex)
                {
                    CustomConsole.Error("Failed to persist web asset format memory.", ex);
                }
            }
        }

        private void MarkDirty() => System.Threading.Interlocked.Exchange(ref dirty, 1);

        private void Load()
        {
            if (filePath == null || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                PersistedState? state = JsonConvert.DeserializeObject<PersistedState>(File.ReadAllText(filePath));
                if (state == null)
                {
                    return;
                }

                foreach (KeyValuePair<string, string> entry in state.Learned)
                {
                    if (Enum.TryParse(entry.Key, out WebAssetKind kind) && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        learned[kind] = entry.Value;
                    }
                }

                long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)missTtl.TotalSeconds;
                foreach (KeyValuePair<string, long> entry in state.Misses)
                {
                    if (entry.Value > cutoff)
                    {
                        misses[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to load web asset format memory; starting empty.", ex);
            }
        }

        private sealed class PersistedState
        {
            public Dictionary<string, string> Learned { get; set; } = new Dictionary<string, string>();

            public Dictionary<string, long> Misses { get; set; } = new Dictionary<string, long>();
        }
    }
}
