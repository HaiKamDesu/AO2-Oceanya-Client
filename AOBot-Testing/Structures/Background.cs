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
            // Step 1: Iterate over bgImages  
            return bgImages.GroupBy(f =>
            {
                // Step 2: Get the file name without extension and convert to lower case  
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(f).ToLower();

                // Step 3: Check if the file name is in the posToBGName dictionary  
                var posKey = posToBGName.FirstOrDefault(p => fileNameWithoutExtension == p.Value).Key;

                // Step 4: If the key is found, return the key, otherwise return the file name  
                return posKey ?? fileNameWithoutExtension;
            })
            .Where(g => g.Count() == 1) // Ignore repeated key values  
            .ToDictionary(g => g.Key, g => g.First());
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
