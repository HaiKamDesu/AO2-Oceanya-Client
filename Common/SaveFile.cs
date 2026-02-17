using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OceanyaClient
{
    public static class AdvancedFeatureIds
    {
        public const string DreddBackgroundOverlayOverride = "dredd_background_overlay_override";
    }

    public class AdvancedFeatureFlagStore
    {
        public Dictionary<string, bool> EnabledFeatures { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled(string featureId)
        {
            if (string.IsNullOrWhiteSpace(featureId))
            {
                return false;
            }

            return EnabledFeatures.TryGetValue(featureId, out bool enabled) && enabled;
        }

        public void SetEnabled(string featureId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(featureId))
            {
                return;
            }

            EnabledFeatures[featureId] = enabled;
        }
    }

    public class DreddOverlayEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public class DreddOverlayMutationRecord
    {
        public string DesignIniPath { get; set; } = string.Empty;
        public string PositionKey { get; set; } = string.Empty;
        public bool FileExisted { get; set; }
        public bool OverlaysSectionExisted { get; set; }
        public bool EntryExisted { get; set; }
        public string OriginalValue { get; set; } = string.Empty;
    }

    public class DreddBackgroundOverlayOverrideConfig
    {
        public List<DreddOverlayEntry> OverlayDatabase { get; set; } = new List<DreddOverlayEntry>();
        public string SelectedOverlayName { get; set; } = string.Empty;
        public bool StickyOverlay { get; set; }
        public List<DreddOverlayMutationRecord> MutationCache { get; set; } = new List<DreddOverlayMutationRecord>();
    }

    public class CustomServerEntry
    {
        public string Name { get; set; } = "";
        public string Endpoint { get; set; } = "";
    }

    public class SaveData
    {
        //Initial Configuration
        public string ConfigIniPath { get; set; } = "";
        public bool UseSingleInternalClient { get; set; } = true;
        public string SelectedServerEndpoint { get; set; } = "";
        public string SelectedServerName { get; set; } = "";
        // Legacy list kept for migration from endpoint-only storage.
        public List<string> CustomServerEndpoints { get; set; } = new List<string>();
        public List<CustomServerEntry> CustomServerEntries { get; set; } = new List<CustomServerEntry>();

        // Advanced feature flags and configs.
        public AdvancedFeatureFlagStore AdvancedFeatures { get; set; } = new AdvancedFeatureFlagStore();
        public DreddBackgroundOverlayOverrideConfig DreddBackgroundOverlayOverride { get; set; } = new DreddBackgroundOverlayOverrideConfig();


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

        private static SaveData _data = new SaveData();

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
                    _data = JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData();
                    NormalizeLoadedData(_data);
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
                NormalizeLoadedData(_data);
                string directory = Path.GetDirectoryName(saveFilePath) ?? string.Empty;
                Directory.CreateDirectory(directory);
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(saveFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving savefile: {ex.Message}");
            }
        }

        private static void NormalizeLoadedData(SaveData data)
        {
            data.CustomServerEndpoints ??= new List<string>();
            data.CustomServerEntries ??= new List<CustomServerEntry>();
            data.AdvancedFeatures ??= new AdvancedFeatureFlagStore();
            data.AdvancedFeatures.EnabledFeatures = data.AdvancedFeatures.EnabledFeatures == null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(data.AdvancedFeatures.EnabledFeatures, StringComparer.OrdinalIgnoreCase);
            data.DreddBackgroundOverlayOverride ??= new DreddBackgroundOverlayOverrideConfig();
            data.DreddBackgroundOverlayOverride.OverlayDatabase ??= new List<DreddOverlayEntry>();
            data.DreddBackgroundOverlayOverride.MutationCache ??= new List<DreddOverlayMutationRecord>();
            data.DreddBackgroundOverlayOverride.SelectedOverlayName ??= string.Empty;

            // Cleanup malformed overlay entries.
            data.DreddBackgroundOverlayOverride.OverlayDatabase = data.DreddBackgroundOverlayOverride.OverlayDatabase
                .Where(entry => entry != null)
                .Select(entry => new DreddOverlayEntry
                {
                    Name = entry.Name?.Trim() ?? string.Empty,
                    FilePath = entry.FilePath?.Trim() ?? string.Empty
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToList();

            data.DreddBackgroundOverlayOverride.MutationCache = data.DreddBackgroundOverlayOverride.MutationCache
                .Where(record => record != null
                    && !string.IsNullOrWhiteSpace(record.DesignIniPath)
                    && !string.IsNullOrWhiteSpace(record.PositionKey))
                .Select(record => new DreddOverlayMutationRecord
                {
                    DesignIniPath = record.DesignIniPath?.Trim() ?? string.Empty,
                    PositionKey = record.PositionKey?.Trim() ?? string.Empty,
                    FileExisted = record.FileExisted,
                    OverlaysSectionExisted = record.OverlaysSectionExisted,
                    EntryExisted = record.EntryExisted,
                    OriginalValue = record.OriginalValue ?? string.Empty
                })
                .ToList();
        }
    }

    
}
