using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Common;

namespace AOBot_Testing.Structures
{
    [Serializable]
    public class CharacterFolder
    {
        #region Static methods
        private const int CacheVersion = 3;
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

        public static void RefreshCharacterList(
            Action<CharacterFolder>? onParsedCharacter = null,
            Action<string>? onChangedMountPath = null,
            Action<CharacterFolder, int, int>? onParsedCharacterProgress = null)
        {
            EnsureCacheFilePath();

            List<(string DirectoryPath, string IniFilePath, string FolderName)> candidates =
                new List<(string DirectoryPath, string IniFilePath, string FolderName)>();

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
                    candidates.Add((directory, iniFilePath, folderName));
                }
            }

            CharacterFolder?[] parsedCharacters = new CharacterFolder?[candidates.Count];
            int parsedCharacterCount = 0;
            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = AssetRefreshParallelism.GetDegreeOfParallelism(candidates.Count)
            };

            Parallel.For(0, candidates.Count, options, index =>
            {
                (string directoryPath, string iniFilePath, _) = candidates[index];
                try
                {
                    CharacterFolder parsedCharacter = Structures.CharacterFolder.Create(iniFilePath);
                    parsedCharacters[index] = parsedCharacter;
                    int currentCount = Interlocked.Increment(ref parsedCharacterCount);
                    onParsedCharacter?.Invoke(parsedCharacter);
                    onParsedCharacterProgress?.Invoke(parsedCharacter, currentCount, candidates.Count);
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning(
                        $"Skipping broken character folder '{directoryPath}' due to parse/validation failure.");
                    CustomConsole.Error("Character parsing error", ex);
                }
            });

            List<CharacterFolder> refreshedCharacters = new List<CharacterFolder>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < candidates.Count; index++)
            {
                CharacterFolder? parsedCharacter = parsedCharacters[index];
                if (parsedCharacter == null)
                {
                    continue;
                }

                string folderName = candidates[index].FolderName;
                if (!seenNames.Add(folderName))
                {
                    continue;
                }

                refreshedCharacters.Add(parsedCharacter);
            }
            characterConfigs = refreshedCharacters;
            SaveToJson(cacheFile, characterConfigs);
            CustomConsole.Info("Character list saved to cache.");
        }

        public static bool TryUpsertCharacterFolderInCache(
            string targetCharacterDirectoryPath,
            string? previousCharacterDirectoryPath,
            out CharacterFolder? upsertedCharacter,
            out string errorMessage)
        {
            upsertedCharacter = null;
            errorMessage = string.Empty;

            try
            {
                string targetDirectory = NormalizePathForCompare(targetCharacterDirectoryPath);
                if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
                {
                    errorMessage = "Target character directory was not found on disk.";
                    return false;
                }

                string charIniPath = ResolveCharacterIniPath(targetDirectory);
                if (string.IsNullOrWhiteSpace(charIniPath) || !File.Exists(charIniPath))
                {
                    errorMessage = "char.ini was not found in the target character directory.";
                    return false;
                }

                EnsureCacheFilePath();
                _ = FullList;

                upsertedCharacter = Create(charIniPath);
                string upsertedCharacterName = upsertedCharacter.Name;
                string normalizedPreviousDirectory = NormalizePathForCompare(previousCharacterDirectoryPath ?? string.Empty);

                characterConfigs.RemoveAll(existing =>
                    string.Equals(NormalizePathForCompare(existing.DirectoryPath), targetDirectory, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(normalizedPreviousDirectory)
                        && string.Equals(
                            NormalizePathForCompare(existing.DirectoryPath),
                            normalizedPreviousDirectory,
                            StringComparison.OrdinalIgnoreCase))
                    || string.Equals(existing.Name, upsertedCharacterName, StringComparison.OrdinalIgnoreCase));

                characterConfigs.Add(upsertedCharacter);
                SaveToJson(cacheFile, characterConfigs);
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to upsert character folder in cache.", ex);
                errorMessage = ex.Message;
                upsertedCharacter = null;
                return false;
            }
        }

        public static bool TryRemoveCharacterFolderFromCache(
            string? targetCharacterDirectoryPath,
            string? characterName,
            out bool removedAny,
            out string errorMessage)
        {
            removedAny = false;
            errorMessage = string.Empty;

            try
            {
                EnsureCacheFilePath();
                _ = FullList;

                string normalizedTargetDirectory = NormalizePathForCompare(targetCharacterDirectoryPath ?? string.Empty);
                string normalizedCharacterName = (characterName ?? string.Empty).Trim();

                int removedCount = characterConfigs.RemoveAll(existing =>
                    (!string.IsNullOrWhiteSpace(normalizedTargetDirectory)
                        && string.Equals(
                            NormalizePathForCompare(existing.DirectoryPath),
                            normalizedTargetDirectory,
                            StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(normalizedCharacterName)
                        && string.Equals(existing.Name, normalizedCharacterName, StringComparison.OrdinalIgnoreCase)));

                removedAny = removedCount > 0;
                if (removedAny)
                {
                    SaveToJson(cacheFile, characterConfigs);
                }

                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to remove character folder from cache.", ex);
                errorMessage = ex.Message;
                removedAny = false;
                return false;
            }
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

        private static string BuildSourceSignature()
        {
            List<string> entries = new List<string>();

            foreach (string characterFolder in CharacterFolders)
            {
                if (!Directory.Exists(characterFolder))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(characterFolder);
                }
                catch
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    string iniFilePath = Path.Combine(directory, "char.ini");
                    if (!File.Exists(iniFilePath))
                    {
                        continue;
                    }

                    long lastWriteTicksUtc = File.GetLastWriteTimeUtc(iniFilePath).Ticks;
                    entries.Add(Path.GetFileName(directory) + "|" + lastWriteTicksUtc.ToString());
                }
            }

            entries.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(";", entries);
        }

        private static string ResolveCharacterIniPath(string characterDirectoryPath)
        {
            string rootIni = Path.Combine(characterDirectoryPath, "char.ini");
            if (File.Exists(rootIni))
            {
                return rootIni;
            }

            string[] iniFiles = Directory.GetFiles(characterDirectoryPath, "char.ini", SearchOption.AllDirectories);
            return iniFiles.FirstOrDefault() ?? string.Empty;
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

        static void SaveToJson(string filePath, List<CharacterFolder> characters)
        {
            CharacterCacheContainer container = new CharacterCacheContainer
            {
                Version = CacheVersion,
                ConfigPath = Globals.PathToConfigINI,
                BaseFolders = new List<string>(Globals.BaseFolders),
                SourceSignature = BuildSourceSignature(),
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

            if (!string.Equals(
                    container.SourceSignature ?? string.Empty,
                    BuildSourceSignature(),
                    StringComparison.Ordinal))
            {
                return false;
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
        public string NeedsShowName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string Blips { get; set; } = string.Empty;
        public string EffectsFolder { get; set; } = string.Empty;
        public string Realization { get; set; } = string.Empty;
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
            IniDocument document = IniDocument.Load(configINIPath);

            ShowName = document.GetLatestValueOrDefault("Options", "showname");
            NeedsShowName = document.GetLatestValueOrDefault("Options", "needs_showname");
            Gender = document.GetLatestValueOrDefault("Options", "gender");
            Side = document.GetLatestValueOrDefault("Options", "side");
            Blips = document.GetLatestValueOrDefault("Options", "blips");
            EffectsFolder = document.GetLatestValueOrDefault("Options", "effects");
            Realization = document.GetLatestValueOrDefault("Options", "realization");

            if (int.TryParse(document.GetLatestValueOrDefault("Time", "preanim"), out int preanimTime))
            {
                PreAnimationTime = preanimTime;
            }

            if (int.TryParse(document.GetLatestValueOrDefault("Emotions", "number"), out int emotionCount))
            {
                EmotionsCount = emotionCount;
            }

            foreach (IniEntry entry in document.GetEntries("Emotions"))
            {
                if (string.Equals(entry.Key, "number", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!int.TryParse(entry.Key, out int emotionId))
                {
                    continue;
                }

                maxEmotionEntryId = Math.Max(maxEmotionEntryId, emotionId);
                if (!Emotions.ContainsKey(emotionId))
                {
                    Emotions[emotionId] = new Emote(emotionId);
                }

                Emotions[emotionId] = Emote.ParseEmoteLine(entry.Value);
                Emotions[emotionId].ID = emotionId;
            }

            foreach (IniEntry entry in document.GetEntries("SoundN"))
            {
                if (!int.TryParse(entry.Key, out int soundId))
                {
                    continue;
                }

                if (!Emotions.ContainsKey(soundId))
                {
                    Emotions[soundId] = new Emote(soundId);
                }

                Emotions[soundId].sfxName = string.IsNullOrEmpty(entry.Value) ? "1" : entry.Value;
            }

            foreach (IniEntry entry in document.GetEntries("SoundT"))
            {
                if (!int.TryParse(entry.Key, out int soundTimeId)
                    || !int.TryParse(entry.Value, out int timeValue))
                {
                    continue;
                }

                if (!Emotions.ContainsKey(soundTimeId))
                {
                    Emotions[soundTimeId] = new Emote(soundTimeId);
                }

                Emotions[soundTimeId].sfxDelay = timeValue;
            }

            foreach (IniEntry entry in document.GetEntries("SoundL"))
            {
                if (!int.TryParse(entry.Key, out int soundLoopId))
                {
                    continue;
                }

                if (!Emotions.ContainsKey(soundLoopId))
                {
                    Emotions[soundLoopId] = new Emote(soundLoopId);
                }

                Emotions[soundLoopId].sfxLooping = string.IsNullOrWhiteSpace(entry.Value) ? "0" : entry.Value;
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

    internal sealed class IniDocument
    {
        public Dictionary<string, List<IniEntry>> Sections { get; } =
            new Dictionary<string, List<IniEntry>>(StringComparer.OrdinalIgnoreCase);

        public static IniDocument Load(string path)
        {
            IniDocument document = new IniDocument();
            string currentSection = string.Empty;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = (rawLine ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    _ = document.GetEntries(currentSection);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                document.GetEntries(currentSection).Add(new IniEntry(key, value));
            }

            return document;
        }

        public List<IniEntry> GetEntries(string sectionName)
        {
            string key = (sectionName ?? string.Empty).Trim();
            if (!Sections.TryGetValue(key, out List<IniEntry>? entries))
            {
                entries = new List<IniEntry>();
                Sections[key] = entries;
            }

            return entries;
        }

        public string GetLatestValueOrDefault(string sectionName, string key)
        {
            List<IniEntry> entries = GetEntries(sectionName);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entries[i].Value;
                }
            }

            return string.Empty;
        }
    }

    internal readonly struct IniEntry
    {
        public IniEntry(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Key { get; }

        public string Value { get; }
    }

    internal sealed class CharacterCacheContainer
    {
        public int Version { get; set; }
        public string ConfigPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
        public string SourceSignature { get; set; } = string.Empty;
        public List<CharacterFolder> Characters { get; set; } = new List<CharacterFolder>();
    }
}
