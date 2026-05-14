using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// Returns true when the token is a direct HTTP/HTTPS/FTP streaming URL.
        /// AO2 parity: AO2 checks startsWith("http") and uses BASS_StreamCreateURL for those tokens.
        /// </summary>
        public static bool IsStreamingUrl(string? token)
        {
            return token != null
                && (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || token.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Parses a music loop sidecar file (&lt;resolvedAudioPath&gt;.txt), if present.
        /// Returns the loop start/end and whether the values are in seconds (vs sample frames).
        /// Returns null when no valid sidecar exists.
        /// AO2 parity: sidecar path = audio file path + ".txt" (e.g. "song.mp3.txt").
        /// </summary>
        public static (bool IsSeconds, double LoopStart, double LoopEnd)? ParseMusicLoopSidecar(string? resolvedAudioPath)
        {
            if (string.IsNullOrWhiteSpace(resolvedAudioPath))
            {
                return null;
            }

            string sidecarPath = resolvedAudioPath + ".txt";
            if (!File.Exists(sidecarPath))
            {
                return null;
            }

            string? startStr = ReadIniValue(sidecarPath, "loop_start");
            string? endStr = ReadIniValue(sidecarPath, "loop_end");
            string? lengthStr = ReadIniValue(sidecarPath, "loop_length");
            string? secondsStr = ReadIniValue(sidecarPath, "seconds");

            bool isSeconds = string.Equals(secondsStr, "true", StringComparison.OrdinalIgnoreCase);

            if (!double.TryParse(startStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double start) || start < 0)
            {
                return null;
            }

            double end;
            if (double.TryParse(endStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double endVal) && endVal > start)
            {
                end = endVal;
            }
            else if (double.TryParse(lengthStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double length) && length > 0)
            {
                end = start + length;
            }
            else
            {
                return null;
            }

            return (isSeconds, start, end);
        }

        /// <summary>
        /// Resolves a local AO2-style music token to a file path.
        /// Uses exact token matching so Oceanya has the same play/no-play behavior as AO2.
        /// Direct HTTP/HTTPS/FTP URLs are returned as-is for URL streaming.
        /// </summary>
        public static string? ResolveMusicPath(string? token)
        {
            if (IsStreamingUrl(token))
                return token;
            return ResolveSoundPath("music", token);
        }

        public static string ResolveMusicDisplayPath(string? token)
        {
            string normalized = token?.Trim().Replace('\\', '/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (IsStreamingUrl(normalized))
            {
                return normalized;
            }

            string? resolved = ResolveMusicPath(normalized);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            string baseFolder = (Globals.BaseFolders ?? new List<string>())
                .FirstOrDefault(folder => !string.IsNullOrWhiteSpace(folder)) ?? string.Empty;
            return string.IsNullOrWhiteSpace(baseFolder)
                ? normalized
                : Path.GetFullPath(Path.Combine(baseFolder, "sounds", "music", normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static IReadOnlyList<MusicAssetEntry> EnumerateLocalMusicAssets()
        {
            Dictionary<string, MusicAssetEntry> entries = new Dictionary<string, MusicAssetEntry>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> supportedExtensions = new HashSet<string>(SuffixOrder, StringComparer.OrdinalIgnoreCase);

            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                string musicRoot = Path.Combine(baseFolder, "sounds", "music");
                if (!Directory.Exists(musicRoot))
                {
                    continue;
                }

                foreach (string filePath in Directory.EnumerateFiles(musicRoot, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(filePath);
                    if (!supportedExtensions.Contains(extension))
                    {
                        continue;
                    }

                    string relativeToken = Path.GetRelativePath(musicRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
                    if (!entries.ContainsKey(relativeToken))
                    {
                        entries[relativeToken] = new MusicAssetEntry(relativeToken, filePath);
                    }
                }
            }

            return entries.Values
                .OrderBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        public static string? ResolveCourtSfxPath(string identifier)
        {
            string key = identifier?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                foreach (string iniPath in EnumerateCourtroomSoundIniPaths(baseFolder))
                {
                    string? token = ReadIniValue(iniPath, key);
                    string? resolved = ResolveSfxPath(token);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }
            }

            return ResolveSfxPath(key);
        }

        /// <summary>
        /// Resolves AO2's character shout SFX lookup used by objection/hold-it/take-that overlays.
        /// </summary>
        public static string? ResolveCharacterShoutPath(string? token, string? characterName, string? miscName)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string normalized = token.Trim().Replace('\\', '/');
            string character = characterName?.Trim() ?? string.Empty;
            string misc = miscName?.Trim() ?? string.Empty;

            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                foreach (string candidateRoot in EnumerateAo2SfxRoots(baseFolder, character, misc, includeSoundsPrefix: true))
                {
                    string? resolved = ResolveWithSuffixOrder(candidateRoot, normalized);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                foreach (string candidateRoot in EnumerateAo2SfxRoots(baseFolder, character, misc, includeSoundsPrefix: false))
                {
                    string? resolved = ResolveWithSuffixOrder(candidateRoot, normalized);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                string? fallback = ResolveWithSuffixOrder(Path.Combine(baseFolder, "sounds", "general"), normalized);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    return fallback;
                }
            }

            return null;
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

        private static IEnumerable<string> EnumerateCourtroomSoundIniPaths(string baseFolder)
        {
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                yield break;
            }

            yield return Path.Combine(baseFolder, "themes", "default", "courtroom_sounds.ini");
            yield return Path.Combine(baseFolder, "themes", "default", "courtroom", "courtroom_sounds.ini");
            yield return Path.Combine(baseFolder, "courtroom_sounds.ini");
        }

        private static string? ReadIniValue(string filePath, string key)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = (rawLine ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(line)
                    || line.StartsWith(";", StringComparison.Ordinal)
                    || line.StartsWith("#", StringComparison.Ordinal)
                    || line.StartsWith("[", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string entryKey = line[..equalsIndex].Trim();
                if (!string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return line[(equalsIndex + 1)..].Trim();
            }

            return null;
        }

        private static IEnumerable<string> EnumerateAo2SfxRoots(
            string baseFolder,
            string character,
            string misc,
            bool includeSoundsPrefix)
        {
            string prefix = includeSoundsPrefix ? "sounds" : string.Empty;
            if (!string.IsNullOrWhiteSpace(character))
            {
                yield return Path.Combine(baseFolder, "characters", character, prefix);
            }

            if (!string.IsNullOrWhiteSpace(misc))
            {
                yield return Path.Combine(baseFolder, "themes", "default", "misc", misc, prefix);
                yield return Path.Combine(baseFolder, "misc", misc, prefix);
            }

            yield return Path.Combine(baseFolder, "themes", "default", prefix);
            yield return Path.Combine(baseFolder, prefix);
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

    internal sealed record MusicAssetEntry(string Token, string FullPath);
}
