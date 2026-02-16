using System.IO;
using System.Text.Json;
using Common;

namespace AOBot_Testing.Structures
{
    [Serializable]
    public class CharacterFolder
    {
        #region Static methods
        public static List<string> CharacterFolders => Globals.BaseFolders.Select(x => Path.Combine(x, "characters")).ToList();
        static string cacheFile = Path.Combine(Path.GetTempPath(), "characters.json");
        static List<CharacterFolder> characterConfigs = new List<CharacterFolder>();
        public static List<CharacterFolder> FullList
        {
            get
            {
                if (characterConfigs.Count == 0)
                {
                    // If JSON cache exists, load from it instead of parsing INI files
                    if (File.Exists(cacheFile))
                    {
                        characterConfigs = LoadFromJson(cacheFile);
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
            foreach (var CharacterFolder in CharacterFolders)
            {
                onChangedMountPath?.Invoke(CharacterFolder);
                var directories = Directory.GetDirectories(CharacterFolder);

                foreach (var directory in directories)
                {
                    var iniFilePath = Path.Combine(directory, "char.ini");
                    if (File.Exists(iniFilePath))
                    {
                        var folderName = Path.GetFileName(directory);

                        //If there are none with same name
                        if(!characterConfigs.Any(x => x.Name == folderName))
                        {
                            //Add new character
                            var config = Structures.CharacterFolder.Create(iniFilePath);
                            CustomConsole.Debug($"Parsed Character: {config.Name} ({CharacterFolder})");
                            characterConfigs.Add(config);
                            onParsedCharacter?.Invoke(config);
                        }
                        //If there is one with same name, and the path is the same, update it.
                        else if(characterConfigs.Any(x => x.PathToConfigIni == iniFilePath))
                        {
                            var config = characterConfigs.First(x => x.PathToConfigIni == iniFilePath);
                            config.Update(iniFilePath, false);

                            onParsedCharacter?.Invoke(config);
                        }
                    }
                }
            }

            // Save to JSON file for fast future loading
            SaveToJson(cacheFile, characterConfigs);
            CustomConsole.Info("Character list saved to cache.");
        }

        static JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        // **Save all characters to a single JSON file**
        static void SaveToJson(string filePath, List<CharacterFolder> characters)
        {
            var json = JsonSerializer.Serialize(characters, jsonOptions);
            File.WriteAllText(filePath, json);
        }

        // **Load all characters from a single JSON file**
        static List<CharacterFolder> LoadFromJson(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<CharacterFolder>>(json) ?? new List<CharacterFolder>();
        }
        #endregion



        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string PathToConfigIni { get; set; } = string.Empty;
        public string CharIconPath { get; set; } = string.Empty;
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

            foreach (var extension in Globals.AllowedImageExtensions)
            {
                string curPath = Path.Combine(DirectoryPath, "char_icon." + extension);
                if (File.Exists(curPath))
                {
                    CharIconPath = curPath;
                }
            }

            if (string.IsNullOrEmpty(CharIconPath))
            {
                CharIconPath = Path.Combine(DirectoryPath, "char_icon.png");
            }

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

            return folder;
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

                foreach (var extension in Globals.AllowedImageExtensions)
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
                }
            }
            #endregion

            #region Correct emotes based on config file
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

                Emotions = Emotions.Take(EmotionsCount).ToDictionary(x => x.Key, x => x.Value);
            }
            #endregion
        }
    }
}
