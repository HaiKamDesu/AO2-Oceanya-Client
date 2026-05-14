using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Common;

namespace AOBot_Testing.Structures
{
    public class Background
    {
        private const int CacheVersion = 1;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = false };
        private static string cacheFile = Path.Combine(Path.GetTempPath(), "backgrounds.json");
        private static Dictionary<string, Background> backgroundsByName = new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase);
        private static bool cachePathInitialized;
        private static bool cacheLoaded;

        public static List<string> BackgroundFolders => Globals.BaseFolders.Select(x => Path.Combine(x, "background")).ToList();
        public static Dictionary<string, string> posToBGName = new Dictionary<string, string>()
        {
            {"def","defenseempty"},
            {"hld","helperstand"},
            {"jud","judgestand"},
            {"hlp","prohelperstand"},
            {"pro","prosecutorempty"},
            {"jur","jurystand"},
            {"sea","seancestand"},
            {"wit","witnessempty"},
        };

        private static readonly string[] DefaultAo2Positions =
        {
            "def",
            "hld",
            "jud",
            "hlp",
            "pro",
            "wit",
            "jur",
            "sea"
        };

        private static readonly HashSet<string> NonPositionImageStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "defensedesk",
            "helperdesk",
            "prohelperdesk",
            "prosecutiondesk",
            "stand",
            "judgedesk",
            "jurydesk",
            "seancedesk"
        };


        public string Name { get; set; } = string.Empty;
        public string PathToFile { get; set; } = string.Empty;
        public List<string> bgImages { get; set; } = new List<string>();

        /// <summary>
        /// A position option resolved for the current background.
        /// </summary>
        public sealed record PositionOption(string Name, string ImagePath);

        public static Background? FromBGPath(string curBG)
        {
            EnsureCacheLoaded();
            if (backgroundsByName.TryGetValue(curBG, out Background? cachedBackground))
            {
                return cachedBackground.Clone();
            }

            if (TryResolveBackgroundDirectory(curBG, out string backgroundDirectory))
            {
                Background resolvedBackground = CreateBackgroundFromDirectory(backgroundDirectory);
                if (!string.IsNullOrWhiteSpace(curBG))
                {
                    backgroundsByName[curBG] = resolvedBackground;
                }

                if (!string.IsNullOrWhiteSpace(resolvedBackground.Name)
                    && !backgroundsByName.ContainsKey(resolvedBackground.Name))
                {
                    backgroundsByName[resolvedBackground.Name] = resolvedBackground;
                }

                return resolvedBackground.Clone();
            }

            return null;
        }

        public static void RefreshCache(Action<string>? onChangedMountPath = null)
        {
            EnsureCacheFilePath();

            List<(string DirectoryPath, string FolderName)> candidates = new List<(string DirectoryPath, string FolderName)>();
            foreach (string backgroundFolder in BackgroundFolders)
            {
                onChangedMountPath?.Invoke(backgroundFolder);
                if (!Directory.Exists(backgroundFolder))
                {
                    continue;
                }

                string[] directories = Directory.GetDirectories(backgroundFolder);
                foreach (string directory in directories)
                {
                    string folderName = Path.GetFileName(directory);
                    candidates.Add((directory, folderName));
                }
            }

            Background?[] parsedBackgrounds = new Background?[candidates.Count];
            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = AssetRefreshParallelism.GetDegreeOfParallelism(candidates.Count)
            };

            Parallel.For(0, candidates.Count, options, index =>
            {
                parsedBackgrounds[index] = CreateBackgroundFromDirectory(candidates[index].DirectoryPath);
            });

            Dictionary<string, Background> refreshedBackgrounds = new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < candidates.Count; index++)
            {
                Background? parsedBackground = parsedBackgrounds[index];
                if (parsedBackground == null)
                {
                    continue;
                }

                if (refreshedBackgrounds.ContainsKey(parsedBackground.Name))
                {
                    continue;
                }

                refreshedBackgrounds.Add(parsedBackground.Name, parsedBackground);
            }

            backgroundsByName = refreshedBackgrounds;
            cacheLoaded = true;
            SaveToJson(cacheFile, GetDistinctBackgroundsForCache());
            CustomConsole.Info($"Background list saved to cache. Count: {backgroundsByName.Count}");
        }

        public static bool TryUpsertBackgroundInCache(
            string targetBackgroundDirectoryPath,
            out Background? upsertedBackground,
            out string errorMessage)
        {
            upsertedBackground = null;
            errorMessage = string.Empty;

            try
            {
                string targetDirectory = NormalizePathForCompare(targetBackgroundDirectoryPath);
                if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
                {
                    errorMessage = "Target background directory was not found on disk.";
                    return false;
                }

                EnsureCacheFilePath();
                EnsureCacheLoaded();

                upsertedBackground = CreateBackgroundFromDirectory(targetDirectory);
                string effectiveDirectory = targetDirectory;
                if (!string.IsNullOrWhiteSpace(upsertedBackground.Name)
                    && TryResolveBackgroundDirectory(upsertedBackground.Name, out string resolvedEffectiveDirectory))
                {
                    effectiveDirectory = NormalizePathForCompare(resolvedEffectiveDirectory);
                    if (!string.Equals(effectiveDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        upsertedBackground = CreateBackgroundFromDirectory(effectiveDirectory);
                    }
                }

                string upsertedName = upsertedBackground.Name;
                List<string> keysToRemove = backgroundsByName
                    .Where(pair => string.Equals(
                        NormalizePathForCompare(pair.Value.PathToFile),
                        targetDirectory,
                        StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            NormalizePathForCompare(pair.Value.PathToFile),
                            effectiveDirectory,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(pair.Key, upsertedName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(pair.Value.Name, upsertedName, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (string key in keysToRemove)
                {
                    backgroundsByName.Remove(key);
                }

                backgroundsByName[upsertedBackground.Name] = upsertedBackground;
                SaveToJson(cacheFile, GetDistinctBackgroundsForCache());
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to upsert background in cache.", ex);
                errorMessage = ex.Message;
                upsertedBackground = null;
                return false;
            }
        }

        public static bool TryRemoveBackgroundFromCache(
            string? targetBackgroundDirectoryPath,
            string? backgroundName,
            out bool removedAny,
            out string errorMessage)
        {
            removedAny = false;
            errorMessage = string.Empty;

            try
            {
                EnsureCacheFilePath();
                EnsureCacheLoaded();

                string normalizedTargetDirectory = NormalizePathForCompare(targetBackgroundDirectoryPath ?? string.Empty);
                string normalizedBackgroundName = (backgroundName ?? string.Empty).Trim();

                List<string> namesToRemove = backgroundsByName
                    .Where(pair =>
                        (!string.IsNullOrWhiteSpace(normalizedTargetDirectory)
                            && string.Equals(
                                NormalizePathForCompare(pair.Value.PathToFile),
                                normalizedTargetDirectory,
                                StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(normalizedBackgroundName)
                            && string.Equals(pair.Key, normalizedBackgroundName, StringComparison.OrdinalIgnoreCase)))
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (string name in namesToRemove)
                {
                    removedAny |= backgroundsByName.Remove(name);
                }

                if (removedAny)
                {
                    SaveToJson(cacheFile, GetDistinctBackgroundsForCache());
                }

                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to remove background from cache.", ex);
                errorMessage = ex.Message;
                removedAny = false;
                return false;
            }
        }

        public string? GetBGImage(string pos)
        {
            var imageName = pos;
            if (posToBGName.ContainsKey(pos))
            {
                imageName = posToBGName[pos];
            }

            return bgImages.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLower() == imageName);
        }

        public Dictionary<string, string> GetPossiblePositions()
        {
            Dictionary<string, string> imageByName = bgImages
                .GroupBy(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            string designIniPath = Path.Combine(PathToFile, "design.ini");
            if (File.Exists(designIniPath))
            {
                Dictionary<string, string> positionsFromDesign = ParsePositionsFromDesignIni(designIniPath, imageByName);
                if (positionsFromDesign.Count > 0)
                {
                    return positionsFromDesign;
                }
            }

            return bgImages.GroupBy(f =>
                {
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    string? posKey = posToBGName.FirstOrDefault(p => fileNameWithoutExtension == p.Value).Key;
                    return posKey ?? fileNameWithoutExtension;
                })
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets AO2-style position dropdown entries: default AO2 positions, design.ini positions, then image-backed
        /// undeclared positions.
        /// </summary>
        public IReadOnlyList<PositionOption> GetAo2PositionOptions()
        {
            Dictionary<string, string> imageByName = bgImages
                .GroupBy(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            List<PositionOption> result = new List<PositionOption>();
            HashSet<string> addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string position in DefaultAo2Positions)
            {
                AddPositionIfResolvable(position, PathToFile, imageByName, result, addedNames);
            }

            foreach (string position in GetDesignIniPositions())
            {
                AddPositionIfResolvable(position, PathToFile, imageByName, result, addedNames);
            }

            foreach (string imagePath in bgImages)
            {
                string stem = Path.GetFileNameWithoutExtension(imagePath).Trim();
                if (string.IsNullOrWhiteSpace(stem) || NonPositionImageStems.Contains(stem))
                {
                    continue;
                }

                string? positionName = posToBGName.FirstOrDefault(pair =>
                    string.Equals(pair.Value, stem, StringComparison.OrdinalIgnoreCase)).Key;

                AddPositionIfResolvable(positionName ?? stem, PathToFile, imageByName, result, addedNames);
            }

            return result;
        }

        private static Dictionary<string, string> ParsePositionsFromDesignIni(
            string designIniPath,
            Dictionary<string, string> imageByName)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configuredPosition in ReadDesignIniPositions(designIniPath))
            {
                string realPosition = configuredPosition.Split(':')[0].Trim();
                if (string.IsNullOrWhiteSpace(realPosition))
                {
                    continue;
                }

                if (TryResolvePositionImage(realPosition, imageByName, out string imagePath))
                {
                    result[configuredPosition] = imagePath;
                }
            }

            return result;
        }

        private static IReadOnlyList<string> ReadDesignIniPositions(string designIniPath)
        {
            if (!File.Exists(designIniPath))
            {
                return Array.Empty<string>();
            }

            string positionsRaw = string.Empty;
            bool inGlobalScope = true;
            foreach (string rawLine in File.ReadAllLines(designIniPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                {
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    inGlobalScope = false;
                    continue;
                }

                if (!inGlobalScope)
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                if (string.Equals(key, "positions", StringComparison.OrdinalIgnoreCase))
                {
                    positionsRaw = line.Substring(separator + 1).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(positionsRaw))
            {
                return Array.Empty<string>();
            }

            return positionsRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(position => position.Trim())
                .Where(position => !string.IsNullOrWhiteSpace(position))
                .ToList();
        }

        private IReadOnlyList<string> GetDesignIniPositions()
        {
            string designIniPath = Path.Combine(PathToFile, "design.ini");
            return ReadDesignIniPositions(designIniPath);
        }

        private static void AddPositionIfResolvable(
            string position,
            string backgroundDirectory,
            Dictionary<string, string> imageByName,
            List<PositionOption> result,
            HashSet<string> addedNames)
        {
            string cleanPosition = (position ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanPosition) || !addedNames.Add(cleanPosition))
            {
                return;
            }

            string realPosition = cleanPosition.Split(':')[0].Trim();
            if (string.IsNullOrWhiteSpace(realPosition))
            {
                return;
            }

            if (TryResolvePositionImage(realPosition, imageByName, out string imagePath)
                || TryResolveCourtPositionImage(realPosition, backgroundDirectory, imageByName, out imagePath))
            {
                result.Add(new PositionOption(cleanPosition, imagePath));
            }
        }

        private static bool TryResolveCourtPositionImage(
            string realPosition,
            string backgroundDirectory,
            Dictionary<string, string> imageByName,
            out string imagePath)
        {
            imagePath = string.Empty;
            if (!imageByName.TryGetValue("court", out string? courtImagePath)
                || string.IsNullOrWhiteSpace(courtImagePath))
            {
                return false;
            }

            string designIniPath = Path.Combine(backgroundDirectory, "design.ini");
            if (string.IsNullOrWhiteSpace(ReadDesignIniValue(designIniPath, "court:" + realPosition + "/origin")))
            {
                return false;
            }

            imagePath = courtImagePath;
            return true;
        }

        private static string ReadDesignIniValue(string designIniPath, string key)
        {
            if (!File.Exists(designIniPath))
            {
                return string.Empty;
            }

            foreach (string rawLine in File.ReadAllLines(designIniPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string lineKey = line.Substring(0, separator).Trim();
                if (string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(separator + 1).Trim();
                }
            }

            return string.Empty;
        }

        private static bool TryResolvePositionImage(
            string position,
            Dictionary<string, string> imageByName,
            out string imagePath)
        {
            if (imageByName.TryGetValue(position, out string? directImagePath)
                && !string.IsNullOrWhiteSpace(directImagePath))
            {
                imagePath = directImagePath;
                return true;
            }

            if (posToBGName.TryGetValue(position, out string? mappedImageName)
                && !string.IsNullOrWhiteSpace(mappedImageName)
                && imageByName.TryGetValue(mappedImageName, out string? mappedImagePath)
                && !string.IsNullOrWhiteSpace(mappedImagePath))
            {
                imagePath = mappedImagePath;
                return true;
            }

            imagePath = string.Empty;
            return false;
        }

        private static void EnsureCacheLoaded()
        {
            if (cacheLoaded)
            {
                return;
            }

            EnsureCacheFilePath();
            if (TryLoadFromJson(cacheFile, out List<Background>? cachedBackgrounds))
            {
                backgroundsByName = new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase);
                foreach (Background background in cachedBackgrounds.Where(background =>
                    !string.IsNullOrWhiteSpace(background.Name)))
                {
                    if (!backgroundsByName.ContainsKey(background.Name))
                    {
                        backgroundsByName.Add(background.Name, background);
                    }
                }

                cacheLoaded = true;
                return;
            }

            RefreshCache();
        }

        private static void EnsureCacheFilePath()
        {
            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache");
            Directory.CreateDirectory(cacheRoot);
            string desiredCachePath = Path.Combine(cacheRoot, $"backgrounds_{BuildCacheKey()}.json");
            if (!cachePathInitialized || !string.Equals(cacheFile, desiredCachePath, StringComparison.OrdinalIgnoreCase))
            {
                cacheFile = desiredCachePath;
                backgroundsByName = new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase);
                cacheLoaded = false;
                cachePathInitialized = true;
            }
        }

        private static string BuildCacheKey()
        {
            string payload = $"{Globals.PathToConfigINI}|{string.Join("|", Globals.BaseFolders)}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static Background CreateBackgroundFromDirectory(string backgroundDirectory)
        {
            Background newBackground = new Background
            {
                Name = Path.GetFileName(backgroundDirectory) ?? string.Empty,
                PathToFile = backgroundDirectory
            };

            string[] bgFiles = Directory.GetFiles(backgroundDirectory, "*.*", SearchOption.TopDirectoryOnly);
            List<string> extensions = Globals.AllowedImageExtensions;
            List<string> exclude = new List<string>
            {
                "defensedesk",
                "helperdesk",
                "prohelperdesk",
                "prosecutiondesk",
                "stand",
                "judgedesk",
                "jurydesk",
                "seancedesk"
            };
            List<string> bgFilesFiltered = new List<string>();

            foreach (string file in bgFiles)
            {
                string fileExtension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (!extensions.Contains(fileExtension) || exclude.Contains(fileNameWithoutExtension))
                {
                    continue;
                }

                string? existingFile = bgFilesFiltered
                    .FirstOrDefault(existing => string.Equals(
                        Path.GetFileNameWithoutExtension(existing),
                        Path.GetFileNameWithoutExtension(file),
                        StringComparison.OrdinalIgnoreCase));

                if (existingFile != null)
                {
                    string existingExtension = Path.GetExtension(existingFile).TrimStart('.').ToLowerInvariant();
                    if (extensions.IndexOf(fileExtension) < extensions.IndexOf(existingExtension))
                    {
                        bgFilesFiltered.Remove(existingFile);
                        bgFilesFiltered.Add(file);
                    }
                    continue;
                }

                bgFilesFiltered.Add(file);
            }

            newBackground.bgImages = bgFilesFiltered;
            return newBackground;
        }

        private static List<Background> GetDistinctBackgroundsForCache()
        {
            Dictionary<string, Background> distinct = new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase);
            foreach (Background background in backgroundsByName.Values)
            {
                if (!string.IsNullOrWhiteSpace(background.Name) && !distinct.ContainsKey(background.Name))
                {
                    distinct.Add(background.Name, background);
                }
            }

            return distinct.Values.ToList();
        }

        private static string NormalizePathForCompare(string path)
        {
            string value = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static bool TryResolveBackgroundDirectory(string currentBackgroundValue, out string backgroundDirectory)
        {
            backgroundDirectory = string.Empty;
            string raw = currentBackgroundValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string normalizedRaw = raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string backgroundRelative = normalizedRaw;
            string backgroundPrefix = $"background{Path.DirectorySeparatorChar}";
            if (backgroundRelative.StartsWith(backgroundPrefix, StringComparison.OrdinalIgnoreCase))
            {
                backgroundRelative = backgroundRelative.Substring(backgroundPrefix.Length);
            }

            List<string> candidates = new List<string>();
            if (Path.IsPathRooted(backgroundRelative))
            {
                candidates.Add(backgroundRelative);
            }
            else
            {
                foreach (string baseFolder in Globals.BaseFolders)
                {
                    candidates.Add(Path.Combine(baseFolder, "background", backgroundRelative));
                }
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    string fullCandidate = Path.GetFullPath(candidate);
                    if (Directory.Exists(fullCandidate))
                    {
                        backgroundDirectory = fullCandidate;
                        return true;
                    }
                }
                catch
                {
                    // Ignore malformed paths and continue.
                }
            }

            return false;
        }

        private static void SaveToJson(string filePath, List<Background> backgrounds)
        {
            BackgroundCacheContainer container = new BackgroundCacheContainer
            {
                Version = CacheVersion,
                ConfigPath = Globals.PathToConfigINI,
                BaseFolders = new List<string>(Globals.BaseFolders),
                Backgrounds = backgrounds
            };

            string json = JsonSerializer.Serialize(container, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        private static bool TryLoadFromJson(string filePath, out List<Background> backgrounds)
        {
            backgrounds = new List<Background>();
            if (!File.Exists(filePath))
            {
                return false;
            }

            string json = File.ReadAllText(filePath);
            BackgroundCacheContainer? container = JsonSerializer.Deserialize<BackgroundCacheContainer>(json);
            if (container != null && IsCacheCompatible(container))
            {
                backgrounds = container.Backgrounds ?? new List<Background>();
                return true;
            }

            List<Background>? legacyBackgrounds = JsonSerializer.Deserialize<List<Background>>(json);
            if (legacyBackgrounds != null)
            {
                backgrounds = legacyBackgrounds;
                return true;
            }

            return false;
        }

        private static bool IsCacheCompatible(BackgroundCacheContainer container)
        {
            if (container.Version != CacheVersion)
            {
                return false;
            }

            if (!string.Equals(container.ConfigPath ?? string.Empty, Globals.PathToConfigINI ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            List<string> cachedBaseFolders = container.BaseFolders ?? new List<string>();
            if (cachedBaseFolders.Count != Globals.BaseFolders.Count)
            {
                return false;
            }

            for (int i = 0; i < cachedBaseFolders.Count; i++)
            {
                if (!string.Equals(cachedBaseFolders[i], Globals.BaseFolders[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private Background Clone()
        {
            return new Background
            {
                Name = Name,
                PathToFile = PathToFile,
                bgImages = new List<string>(bgImages)
            };
        }
    }

    internal sealed class BackgroundCacheContainer
    {
        public int Version { get; set; }
        public string ConfigPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
        public List<Background> Backgrounds { get; set; } = new List<Background>();
    }
}
