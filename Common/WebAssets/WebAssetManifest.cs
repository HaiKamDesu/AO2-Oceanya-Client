using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Common.WebAssets
{
    /// <summary>
    /// The four root manifests a webAO-style asset server publishes: <c>characters.json</c>,
    /// <c>backgrounds.json</c>, <c>evidence.json</c>, and <c>extensions.json</c>
    /// (webAO <c>webAO/client/fetchLists.ts</c>).
    /// </summary>
    /// <remarks>
    /// Fetching these four small files at connect is what keeps a large server cheap: knowing the
    /// universe of characters and backgrounds up front turns most "does this exist?" questions into a
    /// dictionary lookup with no HTTP request at all. Manifests are optional - a server without them
    /// simply falls back to probing.
    /// </remarks>
    public sealed class WebAssetManifest
    {
        private readonly HashSet<string> characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> backgrounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> evidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<WebAssetKind, IReadOnlyList<string>> extensionOverrides = new();

        /// <summary>Whether <c>characters.json</c> was published, making character lookups authoritative.</summary>
        public bool HasCharacterList { get; private set; }

        /// <summary>Whether <c>backgrounds.json</c> was published.</summary>
        public bool HasBackgroundList { get; private set; }

        /// <summary>Character folder names the server publishes.</summary>
        public IReadOnlyCollection<string> Characters => characters;

        /// <summary>Background folder names the server publishes.</summary>
        public IReadOnlyCollection<string> Backgrounds => backgrounds;

        /// <summary>Evidence image names the server publishes.</summary>
        public IReadOnlyCollection<string> Evidence => evidence;

        /// <summary>
        /// Returns the configured extension probe order for <paramref name="kind"/>, preferring the
        /// server's <c>extensions.json</c> when it declared one.
        /// </summary>
        public IReadOnlyList<string> ExtensionsFor(WebAssetKind kind)
            => extensionOverrides.TryGetValue(kind, out IReadOnlyList<string>? overrides)
                ? overrides
                : WebAssetExtensions.DefaultFor(kind);

        /// <summary>
        /// Reports whether the manifests prove <paramref name="normalizedVPath"/> cannot exist, so the
        /// pipeline can skip probing entirely.
        /// </summary>
        /// <remarks>
        /// Only ever answers <c>true</c> for a path under a manifest-covered root whose owning folder
        /// is absent from a manifest the server actually published. Anything unknown returns
        /// <c>false</c> so probing still happens.
        /// </remarks>
        public bool IsKnownAbsent(string normalizedVPath)
        {
            if (string.IsNullOrEmpty(normalizedVPath))
            {
                return false;
            }

            if (HasCharacterList && TryGetFolderUnder(normalizedVPath, "characters/", out string characterFolder))
            {
                return !characters.Contains(characterFolder);
            }

            if (HasBackgroundList && TryGetFolderUnder(normalizedVPath, "background/", out string backgroundFolder))
            {
                return !backgrounds.Contains(backgroundFolder);
            }

            return false;
        }

        /// <summary>
        /// Downloads and parses all four manifests. Every one is optional; failures are logged and
        /// leave the corresponding list unpopulated so the pipeline falls back to probing.
        /// </summary>
        public static async Task<WebAssetManifest> FetchAsync(
            HttpClient httpClient,
            WebAssetSource source,
            WebAssetStats? stats = null,
            CancellationToken cancellationToken = default)
        {
            WebAssetManifest manifest = new WebAssetManifest();

            Task<string?> characterTask = TryGetStringAsync(httpClient, source, "characters.json", stats, cancellationToken);
            Task<string?> backgroundTask = TryGetStringAsync(httpClient, source, "backgrounds.json", stats, cancellationToken);
            Task<string?> evidenceTask = TryGetStringAsync(httpClient, source, "evidence.json", stats, cancellationToken);
            Task<string?> extensionTask = TryGetStringAsync(httpClient, source, "extensions.json", stats, cancellationToken);

            await Task.WhenAll(characterTask, backgroundTask, evidenceTask, extensionTask).ConfigureAwait(false);

            manifest.HasCharacterList = manifest.PopulateNameList(characterTask.Result, manifest.characters);
            manifest.HasBackgroundList = manifest.PopulateNameList(backgroundTask.Result, manifest.backgrounds);
            manifest.PopulateNameList(evidenceTask.Result, manifest.evidence);
            manifest.PopulateExtensions(extensionTask.Result);

            CustomConsole.Info(
                $"[WEB] Manifests for {source.BaseUrl}: characters={(manifest.HasCharacterList ? manifest.characters.Count.ToString() : "none")} "
                + $"backgrounds={(manifest.HasBackgroundList ? manifest.backgrounds.Count.ToString() : "none")} "
                + $"evidence={manifest.evidence.Count} extensionOverrides={manifest.extensionOverrides.Count}",
                CustomConsole.LogCategory.WebAssets);

            return manifest;
        }

        private static async Task<string?> TryGetStringAsync(
            HttpClient httpClient,
            WebAssetSource source,
            string fileName,
            WebAssetStats? stats,
            CancellationToken cancellationToken)
        {
            try
            {
                stats?.CountHttpRequest();
                using HttpResponseMessage response = await httpClient
                    .GetAsync(source.BaseUrl + fileName, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CustomConsole.Debug(
                    $"[WEB] Manifest {fileName} unavailable: {ex.Message}",
                    CustomConsole.LogCategory.WebAssets);
                return null;
            }
        }

        private bool PopulateNameList(string? json, HashSet<string> target)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                List<string>? names = JsonConvert.DeserializeObject<List<string>>(json);
                if (names == null)
                {
                    return false;
                }

                foreach (string name in names)
                {
                    string normalized = WebAssetSource.NormalizeVPath(name);
                    if (normalized.Length > 0)
                    {
                        target.Add(normalized);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Debug(
                    $"[WEB] Manifest list could not be parsed: {ex.Message}",
                    CustomConsole.LogCategory.WebAssets);
                return false;
            }
        }

        private void PopulateExtensions(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                JObject? root = JsonConvert.DeserializeObject<JObject>(json);
                if (root == null)
                {
                    return;
                }

                foreach (KeyValuePair<string, JToken?> property in root)
                {
                    IReadOnlyList<WebAssetKind> kinds = WebAssetExtensions.KindsForManifestKey(property.Key);
                    if (kinds.Count == 0 || property.Value is not JArray array)
                    {
                        continue;
                    }

                    List<string> normalized = NormalizeExtensionList(array);
                    if (normalized.Count == 0)
                    {
                        continue;
                    }

                    foreach (WebAssetKind kind in kinds)
                    {
                        // Two manifest keys can map to the same kind (charicon_extensions and
                        // emotions_extensions both describe icon-shaped art), so merge rather than let
                        // whichever key parses last silently win.
                        extensionOverrides[kind] =
                            extensionOverrides.TryGetValue(kind, out IReadOnlyList<string>? existing)
                                ? existing.Concat(normalized)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList()
                                : normalized;
                    }
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Debug(
                    $"[WEB] extensions.json could not be parsed: {ex.Message}",
                    CustomConsole.LogCategory.WebAssets);
            }
        }

        /// <summary>
        /// Cleans a raw <c>extensions.json</c> array into candidate extensions.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>.webp.static</c> is preserved verbatim, NOT collapsed to <c>.webp</c>. It looks like a
        /// decode hint but webAO's <c>client/setEmote.ts</c> uses it as a URL-shape marker: for that
        /// entry it builds <c>&lt;emote&gt;.webp</c> with the <c>(a)</c>/<c>(b)</c> prefix REMOVED,
        /// i.e. the single-sprite layout where one file serves both idle and talking.
        /// </para>
        /// <para>
        /// Collapsing it was a real bug. A server whose <c>emote_extensions</c> is
        /// <c>[".webp", ".webp.static"]</c> deduped down to a single <c>.webp</c> candidate, so every
        /// single-sprite character resolved to nothing and the viewport showed placeholders while
        /// webAO, side by side, displayed them fine.
        /// </para>
        /// </remarks>
        public static List<string> NormalizeExtensionList(IEnumerable<JToken> values)
        {
            List<string> normalized = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JToken value in values)
            {
                string raw = (value.Type == JTokenType.String ? value.Value<string>() : null)?.Trim() ?? string.Empty;
                if (raw.Length == 0)
                {
                    continue;
                }

                if (!raw.StartsWith(".", StringComparison.Ordinal))
                {
                    raw = "." + raw;
                }

                if (raw.Length > 1 && seen.Add(raw))
                {
                    normalized.Add(raw.ToLowerInvariant());
                }
            }

            return normalized;
        }

        private static bool TryGetFolderUnder(string normalizedVPath, string prefix, out string folder)
        {
            folder = string.Empty;
            if (!normalizedVPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string remainder = normalizedVPath.Substring(prefix.Length);
            int separator = remainder.IndexOf('/');
            if (separator <= 0)
            {
                return false;
            }

            folder = remainder.Substring(0, separator);
            return folder.Length > 0;
        }
    }
}
