using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OceanyaClient.Features.CharacterCreator
{
    internal sealed class GeneratedAssetPathCollisionCandidate
    {
        public string AssetKey { get; set; } = string.Empty;
        public string DefaultRelativePath { get; set; } = string.Empty;
        public string PreferredRelativePath { get; set; } = string.Empty;
        public string SourceIdentity { get; set; } = string.Empty;
        public bool HasExplicitOverride { get; set; }
    }

    internal static class GeneratedAssetPathCollisionResolver
    {
        public static Dictionary<string, string> Resolve(
            IReadOnlyList<GeneratedAssetPathCollisionCandidate> candidates)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (candidates == null || candidates.Count == 0)
            {
                return result;
            }

            foreach (IGrouping<string, GeneratedAssetPathCollisionCandidate> pathGroup in candidates
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.AssetKey))
                .GroupBy(static candidate => candidate.PreferredRelativePath, StringComparer.OrdinalIgnoreCase))
            {
                List<IGrouping<string, GeneratedAssetPathCollisionCandidate>> sourceGroups = pathGroup
                    .GroupBy(static candidate => candidate.SourceIdentity, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static group => group.Any(static candidate => candidate.HasExplicitOverride))
                    .ThenBy(static group => group.Min(static candidate => candidate.AssetKey), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (sourceGroups.Count <= 1)
                {
                    foreach (GeneratedAssetPathCollisionCandidate candidate in pathGroup)
                    {
                        result[candidate.AssetKey] = candidate.PreferredRelativePath;
                    }

                    continue;
                }

                string preferredPath = pathGroup.Key;
                string directory = Path.GetDirectoryName(preferredPath.Replace('/', Path.DirectorySeparatorChar))?
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Trim('/') ?? string.Empty;
                string fileName = Path.GetFileName(preferredPath);
                string fileStem = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);

                for (int sourceIndex = 0; sourceIndex < sourceGroups.Count; sourceIndex++)
                {
                    IGrouping<string, GeneratedAssetPathCollisionCandidate> sourceGroup = sourceGroups[sourceIndex];
                    string assignedPath = sourceIndex == 0
                        ? preferredPath
                        : BuildSuffixedPath(directory, fileStem, extension, sourceIndex);

                    foreach (GeneratedAssetPathCollisionCandidate candidate in sourceGroup)
                    {
                        result[candidate.AssetKey] = assignedPath;
                    }
                }
            }

            return result;
        }

        private static string BuildSuffixedPath(string directory, string fileStem, string extension, int suffixIndex)
        {
            string fileName = $"{fileStem}-{suffixIndex}{extension}";
            if (string.IsNullOrWhiteSpace(directory))
            {
                return fileName;
            }

            return directory + "/" + fileName;
        }
    }
}
