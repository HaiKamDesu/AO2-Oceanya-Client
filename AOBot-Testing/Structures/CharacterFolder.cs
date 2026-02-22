using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Common;

namespace AOBot_Testing.Structures
{
    [Serializable]
    public class CharacterFolder
    {
        #region Static methods
        private const int CacheVersion = 2;
        public static List<string> CharacterFolders => Globals.BaseFolders.Select(x => Path.Combine(x, "characters")).ToList();
        static string cacheFile = Path.Combine(Path.GetTempPath(), "characters.json");
        static List<CharacterFolder> characterConfigs = new List<CharacterFolder>();
        static bool cachePathInitialized;
        public static List<CharacterFolder> FullList
        {
            get
            {
                EnsureCacheFilePath();

                if (characterConfigs.Count == 0)
                {
                    if (TryLoadFromJson(cacheFile, out List<CharacterFolder>? cachedCharacters))
                    {
                        characterConfigs = cachedCharacters;
                        CustomConsole.Info($"Loaded {characterConfigs.Count} characters from cache.");
                    }
                    else
                    {
                        RefreshCharacterList();
                    }
                }

                return characterConfigs;
            }
        }

        public static void RefreshCharacterList(Action<CharacterFolder>? onParsedCharacter = null, Action<string>? onChangedMountPath = null)
        {
            EnsureCacheFilePath();

            List<CharacterFolder> refreshedCharacters = new List<CharacterFolder>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string characterFolder in CharacterFolders)
            {
                onChangedMountPath?.Invoke(characterFolder);
                if (!Directory.Exists(characterFolder))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(characterFolder);
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning($"Failed to enumerate character folder '{characterFolder}'.");
                    CustomConsole.Error("Character folder enumeration error", ex);
                    continue;
                }

                foreach (string directory in directories)
                {
                    string iniFilePath = Path.Combine(directory, "char.ini");
                    if (!File.Exists(iniFilePath))
                    {
                        continue;
                    }

                    string folderName = Path.GetFileName(directory);
                    if (seenNames.Contains(folderName))
                    {
                        continue;
                    }

                    try
                    {
                        CharacterFolder config = Structures.CharacterFolder.Create(iniFilePath);
                        seenNames.Add(folderName);
                        CustomConsole.Debug($"Parsed Character: {config.Name} ({characterFolder})");
                        refreshedCharacters.Add(config);
                        onParsedCharacter?.Invoke(config);
                    }
                    catch (Exception ex)
                    {
                        CustomConsole.Warning(
                            $"Skipping broken character folder '{directory}' due to parse/validation failure.");
                        CustomConsole.Error("Character parsing error", ex);
                    }
                }
            }

            characterConfigs = refreshedCharacters;
            SaveToJson(cacheFile, characterConfigs);
            CustomConsole.Info("Character list saved to cache.");
        }

        static JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = false };

        private static void EnsureCacheFilePath()
        {
            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache");
            Directory.CreateDirectory(cacheRoot);
            string desiredCachePath = Path.Combine(cacheRoot, $"characters_{BuildCacheKey()}.json");

            if (!cachePathInitialized || !string.Equals(cacheFile, desiredCachePath, StringComparison.OrdinalIgnoreCase))
            {
                cacheFile = desiredCachePath;
                characterConfigs = new List<CharacterFolder>();
                cachePathInitialized = true;
            }
        }

        private static string BuildCacheKey()
        {
            string payload = $"{Globals.PathToConfigINI}|{string.Join("|", Globals.BaseFolders)}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        static void SaveToJson(string filePath, List<CharacterFolder> characters)
        {
            CharacterCacheContainer container = new CharacterCacheContainer
            {
                Version = CacheVersion,
                ConfigPath = Globals.PathToConfigINI,
                BaseFolders = new List<string>(Globals.BaseFolders),
                Characters = characters
            };

            var json = JsonSerializer.Serialize(container, jsonOptions);
            File.WriteAllText(filePath, json);
        }

        static bool TryLoadFromJson(string filePath, out List<CharacterFolder> characters)
        {
            characters = new List<CharacterFolder>();
            if (!File.Exists(filePath))
            {
                return false;
            }

            string json = File.ReadAllText(filePath);

            CharacterCacheContainer? container = JsonSerializer.Deserialize<CharacterCacheContainer>(json);
            if (container != null && IsCacheCompatible(container))
            {
                characters = container.Characters ?? new List<CharacterFolder>();
                return true;
            }

            List<CharacterFolder>? legacyCharacters = JsonSerializer.Deserialize<List<CharacterFolder>>(json);
            if (legacyCharacters != null)
            {
                characters = legacyCharacters;
                return true;
            }

            return false;
        }

        private static bool IsCacheCompatible(CharacterCacheContainer container)
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

        private static string ResolveCharacterIconPath(string directoryPath, CharacterConfigINI? parsedConfig = null)
        {
            string explicitCharacterIcon = FindFirstExistingFile(directoryPath, "char_icon");
            if (!string.IsNullOrWhiteSpace(explicitCharacterIcon))
            {
                return explicitCharacterIcon;
            }

            if (parsedConfig != null)
            {
                for (int i = 1; i <= parsedConfig.EmotionsCount; i++)
                {
                    if (!parsedConfig.Emotions.TryGetValue(i, out Emote? emote))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_off))
                    {
                        return emote.PathToImage_off;
                    }

                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_on))
                    {
                        return emote.PathToImage_on;
                    }
                }

                foreach (Emote emote in parsedConfig.Emotions.Values)
                {
                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_off))
                    {
                        return emote.PathToImage_off;
                    }

                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_on))
                    {
                        return emote.PathToImage_on;
                    }
                }
            }

            return string.Empty;
        }

        private static string FindFirstExistingFile(string directoryPath, string baseFileName)
        {
            foreach (string extension in Globals.AllowedImageExtensions)
            {
                string curPath = Path.Combine(directoryPath, baseFileName + "." + extension);
                if (File.Exists(curPath))
                {
                    return curPath;
                }
            }

            return string.Empty;
        }
        #endregion



        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string PathToConfigIni { get; set; } = string.Empty;
        public string CharIconPath { get; set; } = string.Empty;
        public string ViewportIdleSpritePath { get; set; } = string.Empty;
        public string SoundListPath { get; set; } = string.Empty;

        public CharacterConfigINI configINI { get; set; } = new CharacterConfigINI(string.Empty);

        public void Update(string configINIPath, bool updateConfigINI)
        {
            UpdatePaths(configINIPath);

            if(updateConfigINI)
            {
                configINI.PathToConfigINI = configINIPath;
                configINI.Update();
            }
        }
        private void UpdatePaths(string configINIPath)
        {
            DirectoryPath = Path.GetDirectoryName(configINIPath) ?? string.Empty;
            CharIconPath = ResolveCharacterIconPath(DirectoryPath);
            ViewportIdleSpritePath = string.Empty;

            SoundListPath = Path.Combine(DirectoryPath, "soundlist.ini");
            PathToConfigIni = configINIPath;

            Name = Path.GetFileName(DirectoryPath) ?? string.Empty;
        }
        public static CharacterFolder Create(string configINIPath)
        {
            var folder = new CharacterFolder();
            folder.UpdatePaths(configINIPath);

            folder.configINI = new CharacterConfigINI(configINIPath);
            folder.configINI.Update();
            folder.CharIconPath = ResolveCharacterIconPath(folder.DirectoryPath, folder.configINI);
            folder.ViewportIdleSpritePath = ResolveViewportIdleSpritePath(folder.DirectoryPath, folder.configINI);

            return folder;
        }

        private static string ResolveViewportIdleSpritePath(string directoryPath, CharacterConfigINI? parsedConfig)
        {
            if (parsedConfig == null)
            {
                return string.Empty;
            }

            for (int i = 1; i <= parsedConfig.EmotionsCount; i++)
            {
                if (!parsedConfig.Emotions.TryGetValue(i, out Emote? emote))
                {
                    continue;
                }

                string resolved = CharacterAssetPathResolver.ResolveIdleSpritePath(directoryPath, emote.Animation);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            foreach (var pair in parsedConfig.Emotions.OrderBy(x => x.Key))
            {
                string resolved = CharacterAssetPathResolver.ResolveIdleSpritePath(directoryPath, pair.Value.Animation);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return string.Empty;
        }
    }

    [Serializable]
    public class CharacterConfigINI
    {
        public CharacterConfigINI(string pathToConfigINI)
        {
            PathToConfigINI = pathToConfigINI;
        }

        public string PathToConfigINI { get; set; }
        public string ShowName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public int PreAnimationTime { get; set; }
        public int EmotionsCount { get; set; }
        public Dictionary<int, Emote> Emotions { get; set; } = new();

        public void Update()
        {
            string configINIPath = PathToConfigINI;
            EmotionsCount = 0;
            Emotions.Clear();
            int maxEmotionEntryId = 0;

            #region Config Parsing
            var lines = File.ReadAllLines(configINIPath);
            string section = "";
            foreach (var line in lines.Select(l => l.Trim()))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Trim('[', ']').ToLower();
                    continue;
                }

                var split = line.Split('=', 2);
                if (split.Length != 2) continue;

                string key = split[0].Trim().ToLower();
                string value = split[1].Trim();

                switch (section)
                {
                    case "options":
                        if (key == "showname") ShowName = value;
                        else if (key == "gender") Gender = value;
                        else if (key == "side") Side = value;
                        break;

                    case "time":
                        if (key == "preanim" && int.TryParse(value, out int preanimTime))
                            PreAnimationTime = preanimTime;
                        break;

                    case "emotions":
                        if (key == "number" && int.TryParse(value, out int emotionCount))
                            EmotionsCount = emotionCount;
                        else if (int.TryParse(key, out int emotionId))
                        {
                            maxEmotionEntryId = Math.Max(maxEmotionEntryId, emotionId);
                            if (!Emotions.ContainsKey(emotionId))
                                Emotions[emotionId] = new Emote(emotionId);
                            Emotions[emotionId] = Emote.ParseEmoteLine(value);
                            Emotions[emotionId].ID = emotionId;
                        }
                        break;

                    case "soundn":
                        if (int.TryParse(key, out int soundId))
                        {
                            if (!Emotions.ContainsKey(soundId))
                                Emotions[soundId] = new Emote(soundId);
                            Emotions[soundId].sfxName = string.IsNullOrEmpty(value) ? "1" : value;
                        }
                        break;

                    case "soundt":
                        if (int.TryParse(key, out int soundTimeId) && int.TryParse(value, out int timeValue))
                        {
                            if (!Emotions.ContainsKey(soundTimeId))
                                Emotions[soundTimeId] = new Emote(soundTimeId);
                            Emotions[soundTimeId].sfxDelay = timeValue;
                        }
                        break;
                }
            }
            #endregion

            #region Gather Button Paths
            string iniDirectory = Path.GetDirectoryName(PathToConfigINI) ?? string.Empty;
            string buttonPath = Path.Combine(iniDirectory, "Emotions");

            foreach (var item in Emotions)
            {
                int id = item.Key;

                foreach (string extension in Globals.AllowedImageExtensions)
                {
                    string currentButtonPath_off = Path.Combine(buttonPath, $"button{id}_off." + extension);
                    if (File.Exists(currentButtonPath_off) && string.IsNullOrEmpty(item.Value.PathToImage_off))
                    {
                        item.Value.PathToImage_off = currentButtonPath_off;
                    }

                    string currentButtonPath_on = Path.Combine(buttonPath, $"button{id}_on." + extension);
                    if (File.Exists(currentButtonPath_on) && string.IsNullOrEmpty(item.Value.PathToImage_on))
                    {
                        item.Value.PathToImage_on = currentButtonPath_on;
                    }

                    if (!string.IsNullOrEmpty(item.Value.PathToImage_off) &&
                        !string.IsNullOrEmpty(item.Value.PathToImage_on))
                    {
                        break;
                    }
                }
            }
            #endregion

            #region Correct emotes based on config file
            if (EmotionsCount <= 0 && maxEmotionEntryId > 0)
            {
                EmotionsCount = maxEmotionEntryId;
                CustomConsole.Warning(
                    $"Character INI '{configINIPath}' has missing/invalid [Emotions] number. " +
                    $"Inferred emotion count as {EmotionsCount} from highest emotion entry.");
            }

            if (EmotionsCount != Emotions.Count)
            {
                for (int i = 1; i <= EmotionsCount; i++)
                {
                    if (!Emotions.ContainsKey(i))
                    {
                        //add an empty emote, since this is how the AO client works.
                        Emotions.Add(i, new Emote(i));
                    }
                }

                Emotions = Emotions
                    .OrderBy(x => x.Key)
                    .Take(EmotionsCount)
                    .ToDictionary(x => x.Key, x => x.Value);
            }
            #endregion
        }
    }

    internal sealed class CharacterCacheContainer
    {
        public int Version { get; set; }
        public string ConfigPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
        public List<CharacterFolder> Characters { get; set; } = new List<CharacterFolder>();
    }
}
