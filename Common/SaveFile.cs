using System;
using System.IO;
using System.Text.Json;

namespace OceanyaClient
{
    public class SaveData
    {
        //Initial Configuration
        public string ConfigIniPath { get; set; } = "";
        public string ConnectionPath { get; set; } = "";
        public bool UseSingleInternalClient { get; set; } = true;


        public string OOCName { get; set; } = "";
        public bool StickyEffect { get; set; } = false;
        public bool SwitchPosOnIniSwap { get; set; } = false;
        public bool InvertICLog { get; set; } = false;
        public int LogMaxMessages { get; set; } = 0;
    }

    public static class SaveFile
    {
        private static readonly string saveFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OceanyaClient",
            "savefile.json"
        );

        private static SaveData _data;

        static SaveFile()
        {
            Load();
        }

        public static SaveData Data
        {
            get => _data;
            set
            {
                _data = value;
                Save();
            }
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(saveFilePath))
                {
                    var json = File.ReadAllText(saveFilePath);
                    _data = JsonSerializer.Deserialize<SaveData>(json);
                }
                else
                {
                    _data = new SaveData();
                    Save();
                }
            }
            catch (Exception ex)
            {
                _data = new SaveData(); // Defaults if corrupted
                Console.WriteLine($"Error loading savefile: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath));
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(saveFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving savefile: {ex.Message}");
            }
        }
    }

    
}
