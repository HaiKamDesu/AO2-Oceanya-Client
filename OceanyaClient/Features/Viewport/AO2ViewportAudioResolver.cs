using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Common;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Resolves AO2-style audio tokens to local files for the viewport.
    /// </summary>
    internal static class AO2ViewportAudioResolver
    {
        private static readonly string[] SuffixOrder = { ".opus", ".ogg", ".mp3", ".wav" };

        /// <summary>
        /// Resolves a local AO2-style music token.
        /// </summary>
        public static string? ResolveMusicPath(string? token)
        {
            return ResolveSoundPath("music", token);
        }

        /// <summary>
        /// Resolves a local AO2-style SFX token.
        /// </summary>
        public static string? ResolveSfxPath(string? token)
        {
            // AO2 standard SFX tokens (e.g. "sfx-realization", "1.wav") live in sounds/general/.
            // Fall back to sounds/ root for non-standard layouts.
            return ResolveSoundPath("general", token, includeLegacySfxPrefixes: true)
                ?? ResolveSoundPath(string.Empty, token, includeLegacySfxPrefixes: false);
        }

        /// <summary>
        /// Resolves a local AO2-style blip token.
        /// </summary>
        public static string? ResolveBlipPath(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string normalized = token.Trim().Replace('\\', '/');
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
                {
                    continue;
                }

                string? direct = ResolveWithinBaseFolder(baseFolder, "sounds/general/" + normalized);
                if (!string.IsNullOrWhiteSpace(direct))
                {
                    return direct;
                }

                string? blipPrefix = ResolveWithinBaseFolder(baseFolder, "sounds/general/../blips/" + normalized);
                if (!string.IsNullOrWhiteSpace(blipPrefix))
                {
                    return blipPrefix;
                }

                string? legacyPrefix = ResolveWithinBaseFolder(baseFolder, "sounds/general/sfx-blip" + normalized);
                if (!string.IsNullOrWhiteSpace(legacyPrefix))
                {
                    return legacyPrefix;
                }
            }

            return null;
        }

        private static string? ResolveSoundPath(string subFolder, string? token, bool includeLegacySfxPrefixes = false)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string normalized = token.Trim().Replace('\\', '/');
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                string soundsRoot = Path.Combine(baseFolder, "sounds");
                if (!Directory.Exists(soundsRoot))
                {
                    continue;
                }

                string rootFolder = string.IsNullOrWhiteSpace(subFolder)
                    ? soundsRoot
                    : Path.Combine(soundsRoot, subFolder);

                if (Directory.Exists(rootFolder))
                {
                    string? direct = ResolveWithSuffixOrder(rootFolder, normalized);
                    if (!string.IsNullOrWhiteSpace(direct))
                    {
                        return direct;
                    }
                }

                if (includeLegacySfxPrefixes)
                {
                    string? legacySfx = ResolveWithSuffixOrder(soundsRoot, "sfx-" + normalized);
                    if (!string.IsNullOrWhiteSpace(legacySfx))
                    {
                        return legacySfx;
                    }

                    string miscRoot = Path.Combine(baseFolder, "misc", "AA");
                    string miscSoundsRoot = Path.Combine(miscRoot, "sounds");
                    foreach (string legacyRoot in new[] { miscRoot, miscSoundsRoot })
                    {
                        if (!Directory.Exists(legacyRoot))
                        {
                            continue;
                        }

                        string? resolved = ResolveWithSuffixOrder(legacyRoot, normalized);
                        if (!string.IsNullOrWhiteSpace(resolved))
                        {
                            return resolved;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(subFolder))
                {
                    string? prefixed = ResolveWithSuffixOrder(soundsRoot, $"{subFolder}/{normalized}");
                    if (!string.IsNullOrWhiteSpace(prefixed))
                    {
                        return prefixed;
                    }
                }
            }

            return null;
        }

        private static string? ResolveWithSuffixOrder(string rootFolder, string relativeToken)
        {
            string relative = relativeToken.Trim().Replace('/', Path.DirectorySeparatorChar);
            string direct = Path.Combine(rootFolder, relative);
            string extension = Path.GetExtension(direct);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return File.Exists(direct) ? direct : null;
            }

            foreach (string suffix in SuffixOrder)
            {
                string candidate = direct + suffix;
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string? ResolveWithinBaseFolder(string baseFolder, string relativeToken)
        {
            string normalizedBase = Path.GetFullPath(baseFolder);
            string relative = relativeToken.Trim().Replace('/', Path.DirectorySeparatorChar);
            string direct = Path.GetFullPath(Path.Combine(normalizedBase, relative));
            if (!direct.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string extension = Path.GetExtension(direct);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return File.Exists(direct) ? direct : null;
            }

            foreach (string suffix in SuffixOrder)
            {
                string candidate = direct + suffix;
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
