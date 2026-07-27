using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Common.WebAssets
{
    /// <summary>
    /// One connected server's asset origin: the normalized asset URL from the AO2 <c>ASS#</c> packet
    /// plus the local mirror directory that web assets materialize into.
    /// </summary>
    /// <remarks>
    /// The mirror is laid out exactly like an AO2 <c>base/</c> folder and gets appended as the LAST
    /// entry of <see cref="Globals.BaseFolders"/>, so every existing first-hit-wins resolver keeps
    /// preferring the user's physical files and only falls through to web content on a local miss.
    /// </remarks>
    public sealed class WebAssetSource
    {
        /// <summary>Mirror roots seen this process, so <see cref="IsWebAsset"/> keeps working after unmount.</summary>
        private static readonly ConcurrentDictionary<string, byte> knownMirrorRoots =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private WebAssetSource(string baseUrl, string mirrorRoot)
        {
            BaseUrl = baseUrl;
            MirrorRoot = mirrorRoot;
            knownMirrorRoots.TryAdd(mirrorRoot, 0);
        }

        /// <summary>Normalized asset URL, always ending in a single '/'.</summary>
        public string BaseUrl { get; }

        /// <summary>Absolute path of the local mirror directory for this asset URL.</summary>
        public string MirrorRoot { get; }

        /// <summary>
        /// Builds a source for <paramref name="assetUrl"/>, or returns <c>null</c> when the server
        /// advertised no usable HTTP(S) asset URL.
        /// </summary>
        public static WebAssetSource? TryCreate(string? assetUrl)
        {
            string normalized = NormalizeUrl(assetUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            return new WebAssetSource(normalized, BuildMirrorRoot(normalized));
        }

        /// <summary>
        /// Trims, validates, and canonicalizes an asset URL to a trailing-slash HTTP(S) origin.
        /// Returns an empty string when the value cannot be used.
        /// </summary>
        public static string NormalizeUrl(string? assetUrl)
        {
            string trimmed = (assetUrl ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed))
            {
                return string.Empty;
            }

            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string canonical = parsed.GetLeftPart(UriPartial.Path);
            return canonical.EndsWith("/", StringComparison.Ordinal) ? canonical : canonical + "/";
        }

        /// <summary>
        /// Resolves the mirror directory for a normalized asset URL under the active cache root, so
        /// production, dev, and unit-test runs never share a mirror.
        /// </summary>
        public static string BuildMirrorRoot(string normalizedUrl)
        {
            return Path.Combine(CacheEnvironment.GetCacheRoot(), "webassets", HashUrl(normalizedUrl));
        }

        /// <summary>Short stable directory name for an asset URL.</summary>
        public static string HashUrl(string normalizedUrl)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl.ToLowerInvariant()));
            StringBuilder builder = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Normalizes an AO2 VFS path (<c>characters/phoenix/(a)normal.webp</c>) to the lowercase,
        /// forward-slash form the mirror and the remote server both use.
        /// </summary>
        /// <remarks>
        /// Lowercasing matches webAO, which lowercases every path segment, and keeps the mirror
        /// matching <c>ResolvePathCaseInsensitive</c>'s Windows fast path.
        /// </remarks>
        public static string NormalizeVPath(string? vpath)
        {
            string normalized = (vpath ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            // Reject traversal outright: a VFS path never legitimately contains "..".
            return normalized.Contains("..", StringComparison.Ordinal)
                ? string.Empty
                : normalized.ToLowerInvariant();
        }

        /// <summary>Builds the remote URL for a normalized VFS path, escaping each segment like webAO.</summary>
        public string BuildUrl(string normalizedVPath)
        {
            if (string.IsNullOrEmpty(normalizedVPath))
            {
                return string.Empty;
            }

            string escaped = string.Join(
                "/",
                normalizedVPath.Split('/').Select(EscapeSegmentLikeEncodeUri));
            return BaseUrl + escaped;
        }

        /// <summary>
        /// Characters JavaScript's <c>encodeURI</c> leaves untouched but
        /// <see cref="Uri.EscapeDataString"/> percent-encodes.
        /// </summary>
        /// <remarks>
        /// webAO builds every asset URL with <c>encodeURI</c>, and AO2 emote filenames are full of
        /// parentheses (<c>(a)normal</c>, <c>(b)normal</c>). Encoding those as <c>%28</c>/<c>%29</c>
        /// is technically equivalent, but only for a host that decodes before matching - some static
        /// file hosts and CDNs match the raw path and would 404 every sprite. Matching webAO's output
        /// byte for byte removes that whole class of failure.
        /// </remarks>
        private static readonly (string Encoded, string Literal)[] EncodeUriPassthrough =
        {
            ("%21", "!"), ("%24", "$"), ("%26", "&"), ("%27", "'"), ("%28", "("), ("%29", ")"),
            ("%2A", "*"), ("%2B", "+"), ("%2C", ","), ("%3A", ":"), ("%3B", ";"), ("%3D", "="),
            ("%40", "@"), ("%7E", "~")
        };

        /// <summary>Percent-escapes one path segment with the same character set as <c>encodeURI</c>.</summary>
        private static string EscapeSegmentLikeEncodeUri(string segment)
        {
            string escaped = Uri.EscapeDataString(segment);
            foreach ((string encoded, string literal) in EncodeUriPassthrough)
            {
                if (escaped.Contains(encoded, StringComparison.OrdinalIgnoreCase))
                {
                    escaped = escaped.Replace(encoded, literal, StringComparison.OrdinalIgnoreCase);
                }
            }

            return escaped;
        }

        /// <summary>Builds the absolute mirror path a normalized VFS path materializes to.</summary>
        public string BuildLocalPath(string normalizedVPath)
        {
            if (string.IsNullOrEmpty(normalizedVPath))
            {
                return string.Empty;
            }

            return Path.Combine(MirrorRoot, normalizedVPath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Reports whether <paramref name="absolutePath"/> lives inside any mirror this process has
        /// created. Used by context menus to hide filesystem actions for server-streamed assets.
        /// </summary>
        public static bool IsWebAsset(string? absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(absolutePath);
            }
            catch
            {
                return false;
            }

            foreach (string root in knownMirrorRoots.Keys)
            {
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Maps an absolute mirror path back to the remote URL it came from, for
        /// "Copy asset URL" context-menu actions. Returns an empty string for non-mirror paths.
        /// </summary>
        public string TryGetRemoteUrl(string? absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            string full;
            try
            {
                full = Path.GetFullPath(absolutePath);
            }
            catch
            {
                return string.Empty;
            }

            if (!full.StartsWith(MirrorRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string relative = full.Substring(MirrorRoot.Length).Replace('\\', '/').Trim('/');
            return BuildUrl(NormalizeVPath(relative));
        }

        /// <summary>
        /// Maps an absolute path under any configured mount back to its AO2 VFS path, so a resolver
        /// that only knows a physical directory can still ask the web for the same logical asset.
        /// </summary>
        /// <remarks>
        /// Longest matching mount wins, because mounts can nest (an AO install's <c>base/</c> inside its
        /// own parent). Returns <c>false</c> for a path outside every mount.
        /// </remarks>
        public static bool TryGetVPathForAbsolutePath(string? absolutePath, out string vpath)
        {
            vpath = string.Empty;
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(absolutePath);
            }
            catch
            {
                return false;
            }

            string bestMount = string.Empty;
            foreach (string mount in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(mount) || mount.Length <= bestMount.Length)
                {
                    continue;
                }

                string normalizedMount = mount.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (full.Length > normalizedMount.Length
                    && full.StartsWith(normalizedMount, StringComparison.OrdinalIgnoreCase)
                    && (full[normalizedMount.Length] == Path.DirectorySeparatorChar
                        || full[normalizedMount.Length] == Path.AltDirectorySeparatorChar))
                {
                    bestMount = normalizedMount;
                }
            }

            if (bestMount.Length == 0)
            {
                return false;
            }

            vpath = NormalizeVPath(full.Substring(bestMount.Length));
            return vpath.Length > 0;
        }

        /// <summary>Registers a mirror root for <see cref="IsWebAsset"/> without creating a source (tests).</summary>
        public static void RegisterMirrorRootForProvenance(string mirrorRoot)
        {
            if (!string.IsNullOrWhiteSpace(mirrorRoot))
            {
                knownMirrorRoots.TryAdd(Path.GetFullPath(mirrorRoot), 0);
            }
        }
    }
}
