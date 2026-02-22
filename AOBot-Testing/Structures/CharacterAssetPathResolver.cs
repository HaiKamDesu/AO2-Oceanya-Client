using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Common;

namespace AOBot_Testing.Structures
{
    /// <summary>
    /// Resolves character asset paths from character directory and emote data.
    /// </summary>
    public static class CharacterAssetPathResolver
    {
        private static string NormalizeCandidate(string input)
        {
            string value = (input ?? string.Empty).Trim();
            if (value.Length >= 2)
            {
                bool doubleQuoted = value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal);
                bool singleQuoted = value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal);
                if (doubleQuoted || singleQuoted)
                {
                    value = value.Substring(1, value.Length - 2).Trim();
                }
            }

            while (value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            return value;
        }

        public static string ResolveCharacterAssetPath(string characterDirectory, string candidate)
        {
            string normalizedCandidate = NormalizeCandidate(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(normalizedCandidate) && File.Exists(normalizedCandidate))
            {
                return normalizedCandidate;
            }

            string normalizedDirectoryCandidate = normalizedCandidate
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string candidateFromDirectory = Path.Combine(characterDirectory ?? string.Empty, normalizedDirectoryCandidate);

            if (Path.HasExtension(normalizedCandidate))
            {
                return File.Exists(candidateFromDirectory) ? candidateFromDirectory : string.Empty;
            }

            foreach (string extension in Globals.AllowedImageExtensions)
            {
                string pathWithExtension = candidateFromDirectory + "." + extension;
                if (File.Exists(pathWithExtension))
                {
                    return pathWithExtension;
                }
            }

            return string.Empty;
        }

        public static string ResolveCharacterAnimationPath(string characterDirectory, string animationName, bool includePlaceholder = true)
        {
            string normalizedAnimationName = NormalizeCandidate(animationName);
            if (string.IsNullOrWhiteSpace(normalizedAnimationName) || normalizedAnimationName == "-")
            {
                return string.Empty;
            }

            List<string> orderedCandidates = new List<string>
            {
                "(a)" + normalizedAnimationName,
                "(a)/" + normalizedAnimationName,
                normalizedAnimationName,
                "(b)" + normalizedAnimationName,
                "(b)/" + normalizedAnimationName,
                "(c)" + normalizedAnimationName,
                "(c)/" + normalizedAnimationName
            };

            if (includePlaceholder)
            {
                orderedCandidates.Add("placeholder");
            }

            foreach (string candidate in orderedCandidates)
            {
                string resolved = ResolveCharacterAssetPath(characterDirectory, candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return string.Empty;
        }

        public static string ResolveIdleSpritePath(string characterDirectory, string animationName)
        {
            string normalizedAnimationName = NormalizeCandidate(animationName);
            if (string.IsNullOrWhiteSpace(normalizedAnimationName) || normalizedAnimationName == "-")
            {
                return string.Empty;
            }

            string[] orderedIdleCandidates =
            {
                "(a)" + normalizedAnimationName,
                "(a)/" + normalizedAnimationName,
                normalizedAnimationName,
                "placeholder"
            };

            foreach (string candidate in orderedIdleCandidates)
            {
                string resolvedCandidate = ResolveCharacterAssetPath(characterDirectory, candidate);
                if (!string.IsNullOrWhiteSpace(resolvedCandidate))
                {
                    return resolvedCandidate;
                }
            }

            return string.Empty;
        }

        public static string ResolveFirstCharacterIdleSpritePath(CharacterFolder folder)
        {
            if (folder?.configINI == null)
            {
                return folder?.CharIconPath ?? string.Empty;
            }

            CharacterConfigINI config = folder.configINI;
            string characterDirectory = folder.DirectoryPath ?? string.Empty;

            for (int emotionId = 1; emotionId <= config.EmotionsCount; emotionId++)
            {
                if (!config.Emotions.TryGetValue(emotionId, out Emote? emote) || emote == null)
                {
                    continue;
                }

                string resolved = ResolveIdleSpritePath(characterDirectory, emote.Animation);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            foreach (KeyValuePair<int, Emote> emotion in config.Emotions.OrderBy(pair => pair.Key))
            {
                string resolved = ResolveIdleSpritePath(characterDirectory, emotion.Value.Animation);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return folder.CharIconPath ?? string.Empty;
        }
    }
}
