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
            {"wit","witnessempty"},
        };


        public string Name { get; set; } = string.Empty;
        public string PathToFile { get; set; } = string.Empty;
        public List<string> bgImages { get; set; } = new List<string>();

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

            Dictionary<string, Background> refreshedBackgrounds = new Dictionary<string, Background>(StringComparer.OrdinalIgnoreCase);
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
                    if (refreshedBackgrounds.ContainsKey(folderName))
                    {
                        continue;
                    }

                    Background background = CreateBackgroundFromDirectory(directory);
                    refreshedBackgrounds.Add(background.Name, background);
                }
            }

            backgroundsByName = refreshedBackgrounds;
            cacheLoaded = true;
            SaveToJson(cacheFile, backgroundsByName.Values.ToList());
            CustomConsole.Info($"Background list saved to cache. Count: {backgroundsByName.Count}");
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

        private static Dictionary<string, string> ParsePositionsFromDesignIni(
            string designIniPath,
            Dictionary<string, string> imageByName)
        {
            List<string> lines = File.ReadAllLines(designIniPath).ToList();
            bool inGlobalScope = true;
            string positionsRaw = string.Empty;

            foreach (string rawLine in lines)
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
                if (!string.Equals(key, "positions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                positionsRaw = line.Substring(separator + 1).Trim();
            }

            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(positionsRaw))
            {
                return result;
            }

            string[] configuredPositions = positionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string configuredPositionRaw in configuredPositions)
            {
                string configuredPosition = configuredPositionRaw.Trim();
                if (string.IsNullOrWhiteSpace(configuredPosition))
                {
                    continue;
                }

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
                backgroundsByName = cachedBackgrounds
                    .Where(background => !string.IsNullOrWhiteSpace(background.Name))
                    .ToDictionary(background => background.Name, background => background, StringComparer.OrdinalIgnoreCase);
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
            List<string> exclude = new List<string> { "defensedesk", "prosecutiondesk", "stand", "judgedesk" };
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
