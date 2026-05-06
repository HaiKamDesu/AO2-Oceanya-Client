using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

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

    public class AO2AiBotSettings
    {
        public string Provider { get; set; } = "Ollama";
        public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
        public string OllamaModel { get; set; } = "llama3.1:8b";
        public string OpenAIModel { get; set; } = "gpt-4o-mini";
        public string OpenAIApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";
        public double Temperature { get; set; } = 0.2;
        public int MaxTokens { get; set; } = 450;
        public int MaxPromptMessages { get; set; }
        public string PersonalityPrompt { get; set; } = string.Empty;
        public int OllamaContextSize { get; set; } = 16384;
        public bool UseOllamaJsonSchema { get; set; } = false;
    }

    public class CustomServerEntry
    {
        public string Name { get; set; } = "";
        public string Endpoint { get; set; } = "";
    }

    public enum FolderVisualizerLayoutMode
    {
        Normal,
        Table
    }

    public enum FolderVisualizerTableColumnKey
    {
        RowNumber,
        Icon,
        IconType,
        Name,
        Tags,
        DirectoryPath,
        PreviewPath,
        LastModified,
        EmoteCount,
        Size,
        IntegrityFailures,
        OpenCharIni,
        Readme
    }

    public enum EmoteVisualizerTableColumnKey
    {
        Icon,
        Id,
        Name,
        IconDimensions,
        PreAnimationPreview,
        PreAnimationDimensions,
        AnimationPreview,
        AnimationDimensions,
        PreAnimationPath,
        AnimationPath
    }

    public class FolderVisualizerNormalViewConfig
    {
        public double TileWidth { get; set; } = 170;
        public double TileHeight { get; set; } = 182;
        public double IconSize { get; set; } = 18;
        public double NameFontSize { get; set; } = 12;
        public double InternalTilePadding { get; set; } = 0;
        public double ScrollWheelStep { get; set; } = 90;
        public double TilePadding { get; set; } = 8;
        public double TileMargin { get; set; } = 4;
    }

    public class FolderVisualizerTableColumnConfig
    {
        public FolderVisualizerTableColumnKey Key { get; set; } = FolderVisualizerTableColumnKey.Name;
        public bool IsVisible { get; set; } = true;
        public int Order { get; set; }
        public double Width { get; set; } = 200;
    }

    public class FolderVisualizerTableViewConfig
    {
        public double RowHeight { get; set; } = 34;
        public double FontSize { get; set; } = 13;
        public List<FolderVisualizerTableColumnConfig> Columns { get; set; } = new List<FolderVisualizerTableColumnConfig>();
    }

    public class FolderVisualizerViewPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New View";
        public FolderVisualizerLayoutMode Mode { get; set; } = FolderVisualizerLayoutMode.Normal;
        public FolderVisualizerNormalViewConfig Normal { get; set; } = new FolderVisualizerNormalViewConfig();
        public FolderVisualizerTableViewConfig Table { get; set; } = new FolderVisualizerTableViewConfig();
    }

    public class FolderVisualizerConfig
    {
        public string SelectedPresetId { get; set; } = string.Empty;
        public string SelectedPresetName { get; set; } = string.Empty;
        public List<FolderVisualizerViewPreset> Presets { get; set; } = new List<FolderVisualizerViewPreset>();
    }

    public class EmoteVisualizerTableColumnConfig
    {
        public EmoteVisualizerTableColumnKey Key { get; set; } = EmoteVisualizerTableColumnKey.Name;
        public bool IsVisible { get; set; } = true;
        public int Order { get; set; }
        public double Width { get; set; } = 200;
    }

    public class EmoteVisualizerTableViewConfig
    {
        public double RowHeight { get; set; } = 58;
        public double FontSize { get; set; } = 13;
        public List<EmoteVisualizerTableColumnConfig> Columns { get; set; } = new List<EmoteVisualizerTableColumnConfig>();
    }

    public class EmoteVisualizerViewPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New View";
        public FolderVisualizerLayoutMode Mode { get; set; } = FolderVisualizerLayoutMode.Normal;
        public FolderVisualizerNormalViewConfig Normal { get; set; } = new FolderVisualizerNormalViewConfig();
        public EmoteVisualizerTableViewConfig Table { get; set; } = new EmoteVisualizerTableViewConfig();
    }

    public class EmoteVisualizerConfig
    {
        public string SelectedPresetId { get; set; } = string.Empty;
        public string SelectedPresetName { get; set; } = string.Empty;
        public List<EmoteVisualizerViewPreset> Presets { get; set; } = new List<EmoteVisualizerViewPreset>();
    }

    public class VisualizerWindowState
    {
        public double Width { get; set; } = 980;
        public double Height { get; set; } = 690;
        public double? Left { get; set; }
        public double? Top { get; set; }
        public bool IsMaximized { get; set; }
    }

    public class CharacterCreatorCutSelectionState
    {
        public string SourcePath { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double SquareSize { get; set; }
        public bool UsesSquareCoordinates { get; set; }
    }

    public enum CharacterCreatorButtonBackgroundPresetMode
    {
        SolidColor = 0,
        Upload = 1,
        Gradient = 2
    }

    public enum CharacterCreatorButtonBackgroundGradientDirection
    {
        Horizontal = 0,
        Vertical = 1,
        DiagonalTopLeftToBottomRight = 2,
        DiagonalBottomLeftToTopRight = 3,
        Radial = 4
    }

    public class CharacterCreatorButtonBackgroundGradientStop
    {
        public string Color { get; set; } = "#FFFFFFFF";
        public double Position { get; set; }
    }

    public class CharacterCreatorButtonBackgroundPreset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public CharacterCreatorButtonBackgroundPresetMode Mode { get; set; } =
            CharacterCreatorButtonBackgroundPresetMode.SolidColor;
        public string SolidColor { get; set; } = "#00000000";
        public string UploadPath { get; set; } = string.Empty;
        public CharacterCreatorButtonBackgroundGradientDirection GradientDirection { get; set; } =
            CharacterCreatorButtonBackgroundGradientDirection.Horizontal;
        public List<CharacterCreatorButtonBackgroundGradientStop> GradientStops { get; set; } =
            new List<CharacterCreatorButtonBackgroundGradientStop>();
        public List<double> GradientMidpoints { get; set; } = new List<double>();
        public bool AddBorder { get; set; }
        public string BorderColor { get; set; } = "#FF000000";
        public int BorderWidth { get; set; } = 5;
    }

    public enum CharacterCreatorButtonEffectPresetMode
    {
        None = 0,
        ReduceOpacity = 1,
        Darken = 2,
        Overlay = 3
    }

    public class CharacterCreatorButtonEffectPreset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public CharacterCreatorButtonEffectPresetMode Mode { get; set; } =
            CharacterCreatorButtonEffectPresetMode.Darken;
        public int OpacityPercent { get; set; } = 75;
        public int DarknessPercent { get; set; } = 50;
        public string OverlayPath { get; set; } = string.Empty;
        public bool AddBorder { get; set; }
        public string BorderColor { get; set; } = "#FF000000";
        public int BorderWidth { get; set; } = 5;
    }

    public class CharacterCreatorButtonEffectSnapshot
    {
        public CharacterCreatorButtonEffectPresetMode Mode { get; set; } =
            CharacterCreatorButtonEffectPresetMode.None;
        public int OpacityPercent { get; set; } = 75;
        public int DarknessPercent { get; set; } = 50;
        public string OverlayPath { get; set; } = string.Empty;
        public bool AddBorder { get; set; }
        public string BorderColor { get; set; } = "#FF000000";
        public int BorderWidth { get; set; } = 5;
        public string CustomPresetId { get; set; } = string.Empty;
    }

    public class CharacterCreatorLastBulkButtonIconConfig
    {
        public string ApplyScope { get; set; } = "Missing button_on or button_off";

        // Background snapshot (mirrors CharacterCreatorButtonBackgroundPreset fields).
        public CharacterCreatorButtonBackgroundPresetMode BackgroundMode { get; set; } =
            CharacterCreatorButtonBackgroundPresetMode.SolidColor;
        public bool BackgroundUsesBuiltInPreset { get; set; }
        public string BackgroundBuiltInPresetName { get; set; } = "Oceanya BG";
        public bool BackgroundUsesNone { get; set; } = true;
        public string BackgroundCustomPresetId { get; set; } = string.Empty;
        public string SolidColor { get; set; } = "#00000000";
        public string BackgroundUploadPath { get; set; } = string.Empty;
        public CharacterCreatorButtonBackgroundGradientDirection GradientDirection { get; set; } =
            CharacterCreatorButtonBackgroundGradientDirection.Horizontal;
        public List<CharacterCreatorButtonBackgroundGradientStop> GradientStops { get; set; } =
            new List<CharacterCreatorButtonBackgroundGradientStop>();
        public List<double> GradientMidpoints { get; set; } = new List<double>();
        public bool BackgroundAddBorder { get; set; }
        public string BackgroundBorderColor { get; set; } = "#FF000000";
        public int BackgroundBorderWidth { get; set; } = 5;

        // Effects.
        public CharacterCreatorButtonEffectSnapshot OnEffect { get; set; } =
            new CharacterCreatorButtonEffectSnapshot();
        public CharacterCreatorButtonEffectSnapshot OffEffect { get; set; } =
            new CharacterCreatorButtonEffectSnapshot
            {
                Mode = CharacterCreatorButtonEffectPresetMode.Darken,
                DarknessPercent = 50
            };
    }

    public class SaveData
    {
        //Initial Configuration
        public string ConfigIniPath { get; set; } = "";
        public string StartupFunctionalityId { get; set; } = "gm_multi_client";
        public bool UseSingleInternalClient { get; set; } = true;
        public string SelectedServerEndpoint { get; set; } = "";
        public string SelectedServerName { get; set; } = "";
        // Legacy list kept for migration from endpoint-only storage.
        public List<string> CustomServerEndpoints { get; set; } = new List<string>();
        public List<CustomServerEntry> CustomServerEntries { get; set; } = new List<CustomServerEntry>();

        // Advanced feature flags and configs.
        public AdvancedFeatureFlagStore AdvancedFeatures { get; set; } = new AdvancedFeatureFlagStore();
        public DreddBackgroundOverlayOverrideConfig DreddBackgroundOverlayOverride { get; set; } = new DreddBackgroundOverlayOverrideConfig();
        public AO2AiBotSettings AO2AiBot { get; set; } = new AO2AiBotSettings();
        public FileHivemindSettings FileHivemind { get; set; } = new FileHivemindSettings();
        public GoogleDriveSyncSettings GoogleDriveSync { get; set; } = new GoogleDriveSyncSettings();


        public string OOCName { get; set; } = "";
        public bool StickyEffect { get; set; } = false;
        public bool SwitchPosOnIniSwap { get; set; } = false;
        public bool InvertICLog { get; set; } = false;
        public int LogMaxMessages { get; set; } = 0;
        public FolderVisualizerConfig FolderVisualizer { get; set; } = new FolderVisualizerConfig();
        public EmoteVisualizerConfig EmoteVisualizer { get; set; } = new EmoteVisualizerConfig();
        public VisualizerWindowState FolderVisualizerWindowState { get; set; } = new VisualizerWindowState();
        public VisualizerWindowState EmoteVisualizerWindowState { get; set; } = new VisualizerWindowState();
        public VisualizerWindowState CharacterCreatorWindowState { get; set; } = new VisualizerWindowState
        {
            Width = 1220,
            Height = 760,
            IsMaximized = false
        };
        public double CharacterCreatorPreviewVolume { get; set; } = 1.0;
        public double AudioMusicVolume { get; set; } = 0.5;
        public double AudioSfxVolume { get; set; } = 1.0;
        public double AudioBlipVolume { get; set; } = 0.5;
        public double CharacterCreatorEmoteTileWidth { get; set; } = 420;
        public double CharacterCreatorEmoteTileHeight { get; set; } = 430;
        public double CharacterCreatorCuttingPreviewHeight { get; set; } = 170;
        public bool CharacterCreatorViewImageBounds { get; set; } = false;
        public bool LoopEmoteVisualizerAnimations { get; set; } = true;
        public bool ViewFolderIntegrityVerifierResults { get; set; }
        public Dictionary<string, int> CharacterFolderPreviewEmoteOverrides { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> CharacterFolderTags { get; set; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public List<string> CharacterFolderActiveTagFilters { get; set; } = new List<string>();
        public double CharacterFolderTagPanelWidth { get; set; } = 260;
        public bool CharacterFolderTagPanelCollapsed { get; set; }
        public Dictionary<string, VisualizerWindowState> PopupWindowStates { get; set; } =
            new Dictionary<string, VisualizerWindowState>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CharacterCreatorCutSelectionState> CharacterCreatorCutSelections { get; set; } =
            new Dictionary<string, CharacterCreatorCutSelectionState>(StringComparer.OrdinalIgnoreCase);
        public List<CharacterCreatorButtonBackgroundPreset> CharacterCreatorButtonBackgroundPresets { get; set; } =
            new List<CharacterCreatorButtonBackgroundPreset>();
        public List<CharacterCreatorButtonEffectPreset> CharacterCreatorButtonEffectPresets { get; set; } =
            new List<CharacterCreatorButtonEffectPreset>();
        public CharacterCreatorLastBulkButtonIconConfig? CharacterCreatorLastBulkButtonIconConfig { get; set; }
    }

    public static class SaveFile
    {
        private static readonly string defaultSaveFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OceanyaClient",
            "savefile.json"
        );
        private static string saveFilePath = defaultSaveFilePath;

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

        public static string CurrentStoragePath => saveFilePath;

        public static void ConfigureStoragePathForTests(string path)
        {
            saveFilePath = string.IsNullOrWhiteSpace(path) ? defaultSaveFilePath : path.Trim();
            Load();
        }

        public static void ResetForTests(SaveData? seed = null, bool persist = true)
        {
            _data = seed ?? new SaveData();
            NormalizeLoadedData(_data);
            if (persist)
            {
                Save();
            }
        }

        public static void ReloadFromDiskForTests()
        {
            Load();
        }

        public static SaveData LoadSnapshotFromDisk()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(saveFilePath))
                    {
                        SaveData empty = new SaveData();
                        NormalizeLoadedData(empty);
                        return empty;
                    }

                    string json = File.ReadAllText(saveFilePath);
                    SaveData snapshot = JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData();
                    NormalizeLoadedData(snapshot);
                    return snapshot;
                }
                catch when (attempt < 2)
                {
                    Thread.Sleep(50);
                }
            }

            SaveData fallback = new SaveData();
            NormalizeLoadedData(fallback);
            return fallback;
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
            data.StartupFunctionalityId = data.StartupFunctionalityId?.Trim() ?? string.Empty;
            if (string.Equals(data.StartupFunctionalityId, "google_drive_sync", StringComparison.OrdinalIgnoreCase))
            {
                data.StartupFunctionalityId = "oceanyan_file_hivemind";
            }

            if (!string.Equals(data.StartupFunctionalityId, "gm_multi_client", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(data.StartupFunctionalityId, "ao2_ai_bot", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(data.StartupFunctionalityId, "character_database_viewer", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(data.StartupFunctionalityId, "character_file_creator", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(data.StartupFunctionalityId, "oceanyan_file_hivemind", StringComparison.OrdinalIgnoreCase))
            {
                data.StartupFunctionalityId = "gm_multi_client";
            }

            data.CustomServerEndpoints ??= new List<string>();
            data.CustomServerEntries ??= new List<CustomServerEntry>();
            data.AdvancedFeatures ??= new AdvancedFeatureFlagStore();
            data.AdvancedFeatures.EnabledFeatures = data.AdvancedFeatures.EnabledFeatures == null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(data.AdvancedFeatures.EnabledFeatures, StringComparer.OrdinalIgnoreCase);
            data.DreddBackgroundOverlayOverride ??= new DreddBackgroundOverlayOverrideConfig();
            data.AO2AiBot ??= new AO2AiBotSettings();
            data.FileHivemind ??= new FileHivemindSettings();
            data.GoogleDriveSync ??= new GoogleDriveSyncSettings();
            data.DreddBackgroundOverlayOverride.OverlayDatabase ??= new List<DreddOverlayEntry>();
            data.DreddBackgroundOverlayOverride.MutationCache ??= new List<DreddOverlayMutationRecord>();
            data.DreddBackgroundOverlayOverride.SelectedOverlayName ??= string.Empty;
            NormalizeAO2AiBotSettings(data.AO2AiBot);
            NormalizeGoogleDriveSettings(data.GoogleDriveSync);
            NormalizeFileHivemindSettings(data);

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

            data.FolderVisualizerWindowState ??= new VisualizerWindowState();
            data.EmoteVisualizerWindowState ??= new VisualizerWindowState();
            data.CharacterCreatorWindowState ??= new VisualizerWindowState
            {
                Width = 1220,
                Height = 760,
                IsMaximized = false
            };
            ClampWindowState(data.FolderVisualizerWindowState);
            ClampWindowState(data.EmoteVisualizerWindowState);
            ClampWindowState(data.CharacterCreatorWindowState);
            data.CharacterFolderPreviewEmoteOverrides ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            data.CharacterFolderPreviewEmoteOverrides = data.CharacterFolderPreviewEmoteOverrides
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            data.CharacterFolderTags ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            data.CharacterFolderTags = data.CharacterFolderTags
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => NormalizeTagList(pair.Value),
                    StringComparer.OrdinalIgnoreCase);
            data.CharacterFolderTags = data.CharacterFolderTags
                .Where(pair => pair.Value.Count > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            data.CharacterFolderActiveTagFilters = NormalizeTagList(data.CharacterFolderActiveTagFilters);
            data.CharacterFolderTagPanelWidth = Math.Clamp(data.CharacterFolderTagPanelWidth, 180, 520);
            data.CharacterCreatorPreviewVolume = Math.Clamp(data.CharacterCreatorPreviewVolume, 0.0, 1.0);
            data.AudioMusicVolume = Math.Clamp(data.AudioMusicVolume, 0.0, 1.0);
            data.AudioSfxVolume = Math.Clamp(data.AudioSfxVolume, 0.0, 1.0);
            data.AudioBlipVolume = Math.Clamp(data.AudioBlipVolume, 0.0, 1.0);
            data.CharacterCreatorEmoteTileWidth = Math.Clamp(data.CharacterCreatorEmoteTileWidth, 320, 760);
            data.CharacterCreatorEmoteTileHeight = Math.Clamp(data.CharacterCreatorEmoteTileHeight, 330, 820);
            data.CharacterCreatorCuttingPreviewHeight = Math.Clamp(data.CharacterCreatorCuttingPreviewHeight, 120, 520);
            data.PopupWindowStates ??= new Dictionary<string, VisualizerWindowState>(StringComparer.OrdinalIgnoreCase);
            data.PopupWindowStates = data.PopupWindowStates
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair =>
                    {
                        ClampWindowState(pair.Value);
                        return pair.Value;
                    },
                    StringComparer.OrdinalIgnoreCase);
            data.CharacterCreatorCutSelections ??=
                new Dictionary<string, CharacterCreatorCutSelectionState>(StringComparer.OrdinalIgnoreCase);
            data.CharacterCreatorCutSelections = data.CharacterCreatorCutSelections
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair =>
                    {
                        CharacterCreatorCutSelectionState state = pair.Value;
                        state.SourcePath = state.SourcePath?.Trim() ?? string.Empty;
                        state.X = Math.Clamp(state.X, 0, 1);
                        state.Y = Math.Clamp(state.Y, 0, 1);
                        state.Width = Math.Clamp(state.Width, 0, 1);
                        state.Height = Math.Clamp(state.Height, 0, 1);
                        state.SquareSize = Math.Clamp(state.SquareSize, 0, 1);
                        if (state.UsesSquareCoordinates && state.SquareSize <= 0)
                        {
                            state.SquareSize = state.Width > 0 ? state.Width : state.Height;
                        }

                        return state;
                    },
                    StringComparer.OrdinalIgnoreCase);
            data.CharacterCreatorCutSelections = data.CharacterCreatorCutSelections
                .Where(pair => pair.Value.Width > 0 && pair.Value.Height > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            data.CharacterCreatorButtonBackgroundPresets ??= new List<CharacterCreatorButtonBackgroundPreset>();
            data.CharacterCreatorButtonBackgroundPresets = NormalizeButtonBackgroundPresets(
                data.CharacterCreatorButtonBackgroundPresets);

            data.CharacterCreatorButtonEffectPresets ??= new List<CharacterCreatorButtonEffectPreset>();
            data.CharacterCreatorButtonEffectPresets = NormalizeButtonEffectPresets(
                data.CharacterCreatorButtonEffectPresets);

            NormalizeLastBulkButtonIconConfig(data);

            data.FolderVisualizer ??= new FolderVisualizerConfig();
            data.FolderVisualizer.Presets ??= new List<FolderVisualizerViewPreset>();

            if (data.FolderVisualizer.Presets.Count == 0)
            {
                data.FolderVisualizer.Presets = CreateDefaultVisualizerPresets();
            }

            for (int i = 0; i < data.FolderVisualizer.Presets.Count; i++)
            {
                FolderVisualizerViewPreset preset = data.FolderVisualizer.Presets[i] ?? new FolderVisualizerViewPreset();
                preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id.Trim();
                preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? $"View {i + 1}" : preset.Name.Trim();
                preset.Normal ??= new FolderVisualizerNormalViewConfig();
                preset.Table ??= new FolderVisualizerTableViewConfig();
                preset.Table.Columns ??= new List<FolderVisualizerTableColumnConfig>();
                EnsureDefaultTableColumns(preset.Table.Columns);
                ClampPresetValues(preset);
                data.FolderVisualizer.Presets[i] = preset;
            }

            bool selectedIdValid = !string.IsNullOrWhiteSpace(data.FolderVisualizer.SelectedPresetId)
                && data.FolderVisualizer.Presets.Any(p =>
                    string.Equals(p.Id, data.FolderVisualizer.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
            if (!selectedIdValid)
            {
                FolderVisualizerViewPreset? preferredByName = data.FolderVisualizer.Presets.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(data.FolderVisualizer.SelectedPresetName)
                    && string.Equals(p.Name, data.FolderVisualizer.SelectedPresetName, StringComparison.OrdinalIgnoreCase));

                FolderVisualizerViewPreset preferred =
                    preferredByName
                    ??
                    data.FolderVisualizer.Presets.FirstOrDefault(p =>
                        string.Equals(p.Name, "Grid", StringComparison.OrdinalIgnoreCase))
                    ?? data.FolderVisualizer.Presets[0];
                data.FolderVisualizer.SelectedPresetId = preferred.Id;
                data.FolderVisualizer.SelectedPresetName = preferred.Name;
            }
            else
            {
                FolderVisualizerViewPreset selected = data.FolderVisualizer.Presets.First(p =>
                    string.Equals(p.Id, data.FolderVisualizer.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
                data.FolderVisualizer.SelectedPresetName = selected.Name;
            }

            data.EmoteVisualizer ??= new EmoteVisualizerConfig();
            data.EmoteVisualizer.Presets ??= new List<EmoteVisualizerViewPreset>();

            if (data.EmoteVisualizer.Presets.Count == 0)
            {
                data.EmoteVisualizer.Presets = CreateDefaultEmoteVisualizerPresets();
            }

            for (int i = 0; i < data.EmoteVisualizer.Presets.Count; i++)
            {
                EmoteVisualizerViewPreset preset = data.EmoteVisualizer.Presets[i] ?? new EmoteVisualizerViewPreset();
                preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id.Trim();
                preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? $"View {i + 1}" : preset.Name.Trim();
                preset.Normal ??= new FolderVisualizerNormalViewConfig();
                preset.Table ??= new EmoteVisualizerTableViewConfig();
                preset.Table.Columns ??= new List<EmoteVisualizerTableColumnConfig>();
                EnsureDefaultEmoteTableColumns(preset.Table.Columns);
                ClampEmotePresetValues(preset);
                data.EmoteVisualizer.Presets[i] = preset;
            }

            bool selectedEmoteIdValid = !string.IsNullOrWhiteSpace(data.EmoteVisualizer.SelectedPresetId)
                && data.EmoteVisualizer.Presets.Any(p =>
                    string.Equals(p.Id, data.EmoteVisualizer.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
            if (!selectedEmoteIdValid)
            {
                EmoteVisualizerViewPreset? preferredByName = data.EmoteVisualizer.Presets.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(data.EmoteVisualizer.SelectedPresetName)
                    && string.Equals(p.Name, data.EmoteVisualizer.SelectedPresetName, StringComparison.OrdinalIgnoreCase));

                EmoteVisualizerViewPreset preferred =
                    preferredByName
                    ??
                    data.EmoteVisualizer.Presets.FirstOrDefault(p =>
                        string.Equals(p.Name, "Normal", StringComparison.OrdinalIgnoreCase))
                    ?? data.EmoteVisualizer.Presets[0];
                data.EmoteVisualizer.SelectedPresetId = preferred.Id;
                data.EmoteVisualizer.SelectedPresetName = preferred.Name;
            }
            else
            {
                EmoteVisualizerViewPreset selected = data.EmoteVisualizer.Presets.First(p =>
                    string.Equals(p.Id, data.EmoteVisualizer.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
                data.EmoteVisualizer.SelectedPresetName = selected.Name;
            }
        }

        private static void NormalizeAO2AiBotSettings(AO2AiBotSettings settings)
        {
            settings.Provider = string.Equals(settings.Provider?.Trim(), "OpenAI", StringComparison.OrdinalIgnoreCase)
                ? "OpenAI"
                : "Ollama";
            settings.OllamaEndpoint = string.IsNullOrWhiteSpace(settings.OllamaEndpoint)
                ? "http://127.0.0.1:11434"
                : settings.OllamaEndpoint.Trim();
            settings.OllamaModel = string.IsNullOrWhiteSpace(settings.OllamaModel)
                ? "llama3.1:8b"
                : settings.OllamaModel.Trim();
            settings.OpenAIModel = string.IsNullOrWhiteSpace(settings.OpenAIModel)
                ? "gpt-4o-mini"
                : settings.OpenAIModel.Trim();
            settings.OpenAIApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(settings.OpenAIApiKeyEnvironmentVariable)
                ? "OPENAI_API_KEY"
                : settings.OpenAIApiKeyEnvironmentVariable.Trim();
            settings.Temperature = Math.Clamp(settings.Temperature, 0.0, 2.0);
            settings.MaxTokens = Math.Clamp(settings.MaxTokens, 64, 4096);
            settings.MaxPromptMessages = Math.Clamp(settings.MaxPromptMessages, 0, 1000);
            settings.PersonalityPrompt ??= string.Empty;
        }

        private static void ClampPresetValues(FolderVisualizerViewPreset preset)
        {
            preset.Normal.TileWidth = Math.Clamp(preset.Normal.TileWidth, 100, 420);
            preset.Normal.TileHeight = Math.Clamp(preset.Normal.TileHeight, 120, 480);
            preset.Normal.IconSize = Math.Clamp(preset.Normal.IconSize, 12, 48);
            preset.Normal.NameFontSize = Math.Clamp(preset.Normal.NameFontSize, 9, 30);
            preset.Normal.InternalTilePadding = Math.Clamp(preset.Normal.InternalTilePadding, 0, 24);
            preset.Normal.ScrollWheelStep = Math.Clamp(preset.Normal.ScrollWheelStep, 20, 420);
            preset.Normal.TilePadding = Math.Clamp(preset.Normal.TilePadding, 2, 28);
            preset.Normal.TileMargin = Math.Clamp(preset.Normal.TileMargin, 0, 20);

            preset.Table.RowHeight = Math.Clamp(preset.Table.RowHeight, 22, 96);
            preset.Table.FontSize = Math.Clamp(preset.Table.FontSize, 9, 30);

            foreach (FolderVisualizerTableColumnConfig column in preset.Table.Columns)
            {
                column.Width = Math.Clamp(column.Width, 40, 900);
            }
        }

        private static void EnsureDefaultTableColumns(List<FolderVisualizerTableColumnConfig> existingColumns)
        {
            existingColumns.RemoveAll(column => column == null);

            var defaults = new List<FolderVisualizerTableColumnConfig>
            {
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.RowNumber,
                    IsVisible = true,
                    Order = 0,
                    Width = 56
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Icon,
                    IsVisible = true,
                    Order = 1,
                    Width = 30
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.IconType,
                    IsVisible = false,
                    Order = 2,
                    Width = 150
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Name,
                    IsVisible = true,
                    Order = 3,
                    Width = 320
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Tags,
                    IsVisible = true,
                    Order = 4,
                    Width = 260
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.DirectoryPath,
                    IsVisible = false,
                    Order = 5,
                    Width = 460
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.PreviewPath,
                    IsVisible = false,
                    Order = 6,
                    Width = 460
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.LastModified,
                    IsVisible = true,
                    Order = 7,
                    Width = 170
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.EmoteCount,
                    IsVisible = true,
                    Order = 8,
                    Width = 110
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Size,
                    IsVisible = true,
                    Order = 9,
                    Width = 110
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.IntegrityFailures,
                    IsVisible = true,
                    Order = 10,
                    Width = 420
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.OpenCharIni,
                    IsVisible = true,
                    Order = 11,
                    Width = 120
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Readme,
                    IsVisible = true,
                    Order = 12,
                    Width = 120
                }
            };

            foreach (FolderVisualizerTableColumnConfig defaultColumn in defaults)
            {
                FolderVisualizerTableColumnConfig? existing = existingColumns.FirstOrDefault(column => column.Key == defaultColumn.Key);
                if (existing == null)
                {
                    existingColumns.Add(defaultColumn);
                    continue;
                }

                if (existing.Width <= 0)
                {
                    existing.Width = defaultColumn.Width;
                }
            }

            List<FolderVisualizerTableColumnConfig> ordered = existingColumns
                .OrderBy(column => column.Order)
                .ThenBy(column => column.Key)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
            }

            existingColumns.Clear();
            existingColumns.AddRange(ordered);
        }

        private static List<FolderVisualizerViewPreset> CreateDefaultVisualizerPresets()
        {
            return new List<FolderVisualizerViewPreset>
            {
                new FolderVisualizerViewPreset
                {
                    Id = "details",
                    Name = "Details",
                    Mode = FolderVisualizerLayoutMode.Table,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 170,
                        TileHeight = 182,
                        IconSize = 18,
                        NameFontSize = 12,
                        InternalTilePadding = 2,
                        ScrollWheelStep = 90,
                        TilePadding = 8,
                        TileMargin = 4
                    },
                    Table = new FolderVisualizerTableViewConfig
                    {
                        RowHeight = 49.66765578635016,
                        FontSize = 17.29970326409495,
                        Columns = new List<FolderVisualizerTableColumnConfig>
                        {
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.RowNumber,
                                IsVisible = true,
                                Order = 0,
                                Width = 56
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Icon,
                                IsVisible = true,
                                Order = 1,
                                Width = 61
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.IconType,
                                IsVisible = false,
                                Order = 2,
                                Width = 150
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Name,
                                IsVisible = true,
                                Order = 3,
                                Width = 311.4083086053413
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Tags,
                                IsVisible = true,
                                Order = 4,
                                Width = 260
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.DirectoryPath,
                                IsVisible = false,
                                Order = 5,
                                Width = 480
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.PreviewPath,
                                IsVisible = false,
                                Order = 6,
                                Width = 480
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.EmoteCount,
                                IsVisible = true,
                                Order = 7,
                                Width = 93
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Size,
                                IsVisible = true,
                                Order = 8,
                                Width = 110
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.LastModified,
                                IsVisible = true,
                                Order = 9,
                                Width = 170
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.IntegrityFailures,
                                IsVisible = true,
                                Order = 10,
                                Width = 900
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.OpenCharIni,
                                IsVisible = true,
                                Order = 11,
                                Width = 120
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Readme,
                                IsVisible = true,
                                Order = 12,
                                Width = 120
                            }
                        }
                    }
                },
                new FolderVisualizerViewPreset
                {
                    Id = "large",
                    Name = "Grid",
                    Mode = FolderVisualizerLayoutMode.Normal,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 299.8635014836795,
                        TileHeight = 295.1394658753709,
                        IconSize = 39.76261127596439,
                        NameFontSize = 14.80712166172107,
                        InternalTilePadding = 0,
                        ScrollWheelStep = 200.385756676558,
                        TilePadding = 10,
                        TileMargin = 4
                    },
                    Table = new FolderVisualizerTableViewConfig
                    {
                        RowHeight = 34,
                        FontSize = 13,
                        Columns = new List<FolderVisualizerTableColumnConfig>
                        {
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.RowNumber, IsVisible = true, Order = 0, Width = 56 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Icon, IsVisible = true, Order = 1, Width = 42 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.IconType, IsVisible = false, Order = 2, Width = 150 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Name, IsVisible = true, Order = 3, Width = 320 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Tags, IsVisible = true, Order = 4, Width = 260 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.DirectoryPath, IsVisible = false, Order = 5, Width = 460 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.PreviewPath, IsVisible = false, Order = 6, Width = 460 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.LastModified, IsVisible = true, Order = 7, Width = 170 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.EmoteCount, IsVisible = true, Order = 8, Width = 110 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Size, IsVisible = true, Order = 9, Width = 110 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.IntegrityFailures, IsVisible = true, Order = 10, Width = 120 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.OpenCharIni, IsVisible = true, Order = 11, Width = 120 },
                            new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Readme, IsVisible = true, Order = 12, Width = 120 }
                        }
                    }
                }
            };
        }

        private static void ClampEmotePresetValues(EmoteVisualizerViewPreset preset)
        {
            preset.Normal.TileWidth = Math.Clamp(preset.Normal.TileWidth, 160, 480);
            preset.Normal.TileHeight = Math.Clamp(preset.Normal.TileHeight, 150, 420);
            preset.Normal.IconSize = Math.Clamp(preset.Normal.IconSize, 12, 48);
            preset.Normal.NameFontSize = Math.Clamp(preset.Normal.NameFontSize, 9, 30);
            preset.Normal.InternalTilePadding = Math.Clamp(preset.Normal.InternalTilePadding, 0, 24);
            preset.Normal.ScrollWheelStep = Math.Clamp(preset.Normal.ScrollWheelStep, 20, 420);
            preset.Normal.TilePadding = Math.Clamp(preset.Normal.TilePadding, 2, 28);
            preset.Normal.TileMargin = Math.Clamp(preset.Normal.TileMargin, 0, 20);

            preset.Table.RowHeight = Math.Clamp(preset.Table.RowHeight, 30, 140);
            preset.Table.FontSize = Math.Clamp(preset.Table.FontSize, 9, 30);

            foreach (EmoteVisualizerTableColumnConfig column in preset.Table.Columns)
            {
                column.Width = Math.Clamp(column.Width, 40, 900);
            }
        }

        private static void EnsureDefaultEmoteTableColumns(List<EmoteVisualizerTableColumnConfig> existingColumns)
        {
            existingColumns.RemoveAll(column => column == null);

            var defaults = new List<EmoteVisualizerTableColumnConfig>
            {
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Icon, IsVisible = true, Order = 0, Width = 34 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Id, IsVisible = true, Order = 1, Width = 54 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Name, IsVisible = true, Order = 2, Width = 230 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.IconDimensions, IsVisible = true, Order = 3, Width = 96 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPreview, IsVisible = true, Order = 4, Width = 110 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationDimensions, IsVisible = true, Order = 5, Width = 110 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPreview, IsVisible = true, Order = 6, Width = 110 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationDimensions, IsVisible = true, Order = 7, Width = 110 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPath, IsVisible = false, Order = 8, Width = 320 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPath, IsVisible = false, Order = 9, Width = 320 }
            };

            foreach (EmoteVisualizerTableColumnConfig defaultColumn in defaults)
            {
                EmoteVisualizerTableColumnConfig? existing = existingColumns.FirstOrDefault(column => column.Key == defaultColumn.Key);
                if (existing == null)
                {
                    existingColumns.Add(defaultColumn);
                    continue;
                }

                if (existing.Width <= 0)
                {
                    existing.Width = defaultColumn.Width;
                }
            }

            List<EmoteVisualizerTableColumnConfig> ordered = existingColumns
                .OrderBy(column => column.Order)
                .ThenBy(column => column.Key)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
            }

            existingColumns.Clear();
            existingColumns.AddRange(ordered);
        }

        private static List<EmoteVisualizerViewPreset> CreateDefaultEmoteVisualizerPresets()
        {
            return new List<EmoteVisualizerViewPreset>
            {
                new EmoteVisualizerViewPreset
                {
                    Id = "detailed",
                    Name = "Detailed",
                    Mode = FolderVisualizerLayoutMode.Table,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 170,
                        TileHeight = 182,
                        IconSize = 18,
                        NameFontSize = 12,
                        InternalTilePadding = 2,
                        ScrollWheelStep = 90,
                        TilePadding = 8,
                        TileMargin = 4
                    },
                    Table = new EmoteVisualizerTableViewConfig
                    {
                        RowHeight = 69.19881305637982,
                        FontSize = 24.952522255192875,
                        Columns = new List<EmoteVisualizerTableColumnConfig>
                        {
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Id, IsVisible = true, Order = 0, Width = 59.831157270029664 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Icon, IsVisible = true, Order = 1, Width = 81 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Name, IsVisible = true, Order = 2, Width = 367.53234421364976 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.IconDimensions, IsVisible = true, Order = 3, Width = 86 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPreview, IsVisible = true, Order = 4, Width = 101 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationDimensions, IsVisible = false, Order = 5, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPreview, IsVisible = false, Order = 6, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationDimensions, IsVisible = true, Order = 7, Width = 110 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPath, IsVisible = false, Order = 8, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPath, IsVisible = false, Order = 9, Width = 320 }
                        }
                    }
                },
                new EmoteVisualizerViewPreset
                {
                    Id = "normal",
                    Name = "Normal",
                    Mode = FolderVisualizerLayoutMode.Normal,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 400.2225519287834,
                        TileHeight = 357.15133531157267,
                        IconSize = 48,
                        NameFontSize = 18.293768545994062,
                        InternalTilePadding = 0,
                        ScrollWheelStep = 90,
                        TilePadding = 2,
                        TileMargin = 4
                    },
                    Table = new EmoteVisualizerTableViewConfig
                    {
                        RowHeight = 58,
                        FontSize = 13,
                        Columns = new List<EmoteVisualizerTableColumnConfig>
                        {
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Icon, IsVisible = true, Order = 0, Width = 40 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Id, IsVisible = true, Order = 1, Width = 230 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Name, IsVisible = true, Order = 2, Width = 110 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.IconDimensions, IsVisible = true, Order = 3, Width = 110 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPreview, IsVisible = false, Order = 4, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationDimensions, IsVisible = false, Order = 5, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPreview, IsVisible = false, Order = 6, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationDimensions, IsVisible = true, Order = 7, Width = 110 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPath, IsVisible = false, Order = 8, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPath, IsVisible = false, Order = 9, Width = 320 }
                        }
                    }
                }
            };
        }

        private static void NormalizeGoogleDriveSettings(GoogleDriveSyncSettings settings)
        {
            settings.OAuthClientId = settings.OAuthClientId?.Trim() ?? string.Empty;
            settings.OAuthClientSecret = string.Empty;
            settings.OAuthClientSecretStoreKey = string.IsNullOrWhiteSpace(settings.OAuthClientSecretStoreKey)
                ? Guid.NewGuid().ToString("N")
                : settings.OAuthClientSecretStoreKey.Trim();
            settings.TokenStoreKey = string.IsNullOrWhiteSpace(settings.TokenStoreKey)
                ? Guid.NewGuid().ToString("N")
                : settings.TokenStoreKey.Trim();
            settings.LastSignedInEmail = settings.LastSignedInEmail?.Trim() ?? string.Empty;
            settings.LastSignedInDisplayName = settings.LastSignedInDisplayName?.Trim() ?? string.Empty;
            settings.RemoteFolderId = settings.RemoteFolderId?.Trim() ?? string.Empty;
            settings.RemoteFolderName = settings.RemoteFolderName?.Trim() ?? string.Empty;
            settings.LocalFolderPath = settings.LocalFolderPath?.Trim() ?? string.Empty;
            settings.IsOceanyaManagedLocalFolder = settings.IsOceanyaManagedLocalFolder
                || IsManagedGoogleDriveLocalFolderPath(settings.LocalFolderPath);
        }

        private static void NormalizeFileHivemindSettings(SaveData data)
        {
            data.FileHivemind.Connections ??= new List<FileHivemindConnectionProfile>();
            data.FileHivemind.GoogleDriveAccounts ??= new List<GoogleDriveSignedInAccount>();
            data.FileHivemind.LastSelectedGoogleDriveAccountTokenStoreKey =
                data.FileHivemind.LastSelectedGoogleDriveAccountTokenStoreKey?.Trim() ?? string.Empty;
            data.FileHivemind.SelectedConnectionId = data.FileHivemind.SelectedConnectionId?.Trim() ?? string.Empty;
            data.FileHivemind.RemotePollIntervalSeconds = Math.Clamp(data.FileHivemind.RemotePollIntervalSeconds <= 0
                ? 20
                : data.FileHivemind.RemotePollIntervalSeconds, 5, 3600);
            if (!data.FileHivemind.DesktopToastPreferenceConfigured)
            {
                data.FileHivemind.ShowDesktopToasts = true;
                data.FileHivemind.DesktopToastPreferenceConfigured = true;
            }

            bool migratedLegacyProfile = false;

            if (data.FileHivemind.Connections.Count == 0 && HasMeaningfulGoogleDriveSyncSettings(data.GoogleDriveSync))
            {
                FileHivemindConnectionProfile migratedProfile = FileHivemindConnectionProfile.CreateGoogleDriveProfile();
                migratedProfile.DisplayName = BuildDefaultConnectionDisplayName(data.GoogleDriveSync);
                migratedProfile.GoogleDrive = CloneGoogleDriveSettings(data.GoogleDriveSync);
                data.FileHivemind.Connections.Add(migratedProfile);
                migratedLegacyProfile = true;
            }

            data.FileHivemind.Connections = data.FileHivemind.Connections
                .Where(connection => connection != null)
                .Select(connection =>
                {
                    connection.Id = string.IsNullOrWhiteSpace(connection.Id)
                        ? Guid.NewGuid().ToString("N")
                        : connection.Id.Trim();
                    connection.ProviderId = string.IsNullOrWhiteSpace(connection.ProviderId)
                        ? FileHivemindProviderIds.GoogleDrive
                        : connection.ProviderId.Trim();
                    connection.DisplayName = connection.DisplayName?.Trim() ?? string.Empty;
                    connection.GoogleDrive ??= new GoogleDriveSyncSettings();
                    NormalizeGoogleDriveSettings(connection.GoogleDrive);
                    if (string.IsNullOrWhiteSpace(connection.DisplayName))
                    {
                        connection.DisplayName = BuildDefaultConnectionDisplayName(connection.GoogleDrive);
                    }

                    return connection;
                })
                .ToList();

            data.FileHivemind.GoogleDriveAccounts = data.FileHivemind.GoogleDriveAccounts
                .Where(account => account != null)
                .Select(account =>
                {
                    account.TokenStoreKey = account.TokenStoreKey?.Trim() ?? string.Empty;
                    account.Email = account.Email?.Trim() ?? string.Empty;
                    account.DisplayName = account.DisplayName?.Trim() ?? string.Empty;
                    account.CredentialFingerprint = account.CredentialFingerprint?.Trim() ?? string.Empty;
                    return account;
                })
                .Where(account => !string.IsNullOrWhiteSpace(account.TokenStoreKey)
                    && !string.IsNullOrWhiteSpace(account.CredentialFingerprint)
                    && (!string.IsNullOrWhiteSpace(account.Email) || !string.IsNullOrWhiteSpace(account.DisplayName)))
                .GroupBy(account => account.TokenStoreKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(account => account.LastUsedUtc ?? DateTimeOffset.MinValue)
                    .ThenByDescending(account => account.LastSignedInUtc ?? DateTimeOffset.MinValue)
                    .First())
                .ToList();

            HashSet<string> seenConnectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            data.FileHivemind.Connections = data.FileHivemind.Connections
                .Where(connection => seenConnectionIds.Add(connection.Id))
                .ToList();

            if (!data.FileHivemind.Connections.Any(connection =>
                    string.Equals(connection.Id, data.FileHivemind.SelectedConnectionId, StringComparison.OrdinalIgnoreCase)))
            {
                data.FileHivemind.SelectedConnectionId = data.FileHivemind.Connections.FirstOrDefault()?.Id ?? string.Empty;
            }

            if (!data.FileHivemind.BackgroundStartupPreferenceConfigured
                && data.FileHivemind.Connections.Count > 0)
            {
                data.FileHivemind.RunAgentAtStartup = true;
                data.FileHivemind.BackgroundStartupPreferenceConfigured = true;
            }

            if (migratedLegacyProfile || data.FileHivemind.Connections.Count > 0)
            {
                data.GoogleDriveSync = new GoogleDriveSyncSettings();
                NormalizeGoogleDriveSettings(data.GoogleDriveSync);
            }
        }

        private static bool HasMeaningfulGoogleDriveSyncSettings(GoogleDriveSyncSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings.LastSignedInEmail)
                || !string.IsNullOrWhiteSpace(settings.LastSignedInDisplayName)
                || !string.IsNullOrWhiteSpace(settings.RemoteFolderId)
                || !string.IsNullOrWhiteSpace(settings.RemoteFolderName)
                || !string.IsNullOrWhiteSpace(settings.LocalFolderPath);
        }

        private static string BuildDefaultConnectionDisplayName(GoogleDriveSyncSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.RemoteFolderName))
            {
                return settings.RemoteFolderName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(settings.LastSignedInEmail))
            {
                return "Drive Connection (" + settings.LastSignedInEmail.Trim() + ")";
            }

            if (!string.IsNullOrWhiteSpace(settings.RemoteFolderId))
            {
                return "Drive " + settings.RemoteFolderId.Trim();
            }

            return "Google Drive Connection";
        }

        private static GoogleDriveSyncSettings CloneGoogleDriveSettings(GoogleDriveSyncSettings settings)
        {
            return new GoogleDriveSyncSettings
            {
                OAuthClientId = settings.OAuthClientId,
                OAuthClientSecret = string.Empty,
                OAuthClientSecretStoreKey = settings.OAuthClientSecretStoreKey,
                TokenStoreKey = settings.TokenStoreKey,
                LastSignedInEmail = settings.LastSignedInEmail,
                LastSignedInDisplayName = settings.LastSignedInDisplayName,
                RemoteFolderId = settings.RemoteFolderId,
                RemoteFolderName = settings.RemoteFolderName,
                LocalFolderPath = settings.LocalFolderPath,
                IsOceanyaManagedLocalFolder = settings.IsOceanyaManagedLocalFolder,
                AutoAddMountPath = settings.AutoAddMountPath,
                MirrorDeletes = settings.MirrorDeletes,
                UseExistingMountPath = settings.UseExistingMountPath,
                LastSyncUtc = settings.LastSyncUtc
            };
        }

        private static bool IsManagedGoogleDriveLocalFolderPath(string path)
        {
            string trimmedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return false;
            }

            try
            {
                string managedRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OceanyaClient",
                    "GoogleDriveSync");
                string normalizedPath = Path.GetFullPath(trimmedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedManagedRoot = Path.GetFullPath(managedRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(normalizedPath, normalizedManagedRoot, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(
                        normalizedManagedRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ClampWindowState(VisualizerWindowState state)
        {
            state.Width = Math.Clamp(state.Width, 760, 6000);
            state.Height = Math.Clamp(state.Height, 520, 4000);
            if (state.Left.HasValue && (double.IsInfinity(state.Left.Value) || double.IsNaN(state.Left.Value)))
            {
                state.Left = null;
            }

            if (state.Top.HasValue && (double.IsInfinity(state.Top.Value) || double.IsNaN(state.Top.Value)))
            {
                state.Top = null;
            }
        }

        private static List<string> NormalizeTagList(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<CharacterCreatorButtonBackgroundPreset> NormalizeButtonBackgroundPresets(
            IEnumerable<CharacterCreatorButtonBackgroundPreset>? presets)
        {
            List<CharacterCreatorButtonBackgroundPreset> normalized = new List<CharacterCreatorButtonBackgroundPreset>();
            HashSet<string> usedNameModeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterCreatorButtonBackgroundPreset? raw in presets ?? Array.Empty<CharacterCreatorButtonBackgroundPreset>())
            {
                if (raw == null)
                {
                    continue;
                }

                CharacterCreatorButtonBackgroundPreset preset = new CharacterCreatorButtonBackgroundPreset
                {
                    Id = string.IsNullOrWhiteSpace(raw.Id) ? Guid.NewGuid().ToString("N") : raw.Id.Trim(),
                    Name = raw.Name?.Trim() ?? string.Empty,
                    Mode = raw.Mode,
                    SolidColor = string.IsNullOrWhiteSpace(raw.SolidColor) ? "#00000000" : raw.SolidColor.Trim(),
                    UploadPath = raw.UploadPath?.Trim() ?? string.Empty,
                    GradientDirection = raw.GradientDirection,
                    AddBorder = raw.AddBorder,
                    BorderColor = string.IsNullOrWhiteSpace(raw.BorderColor) ? "#FF000000" : raw.BorderColor.Trim(),
                    BorderWidth = Math.Clamp(raw.BorderWidth, 1, 32),
                    GradientStops = (raw.GradientStops ?? new List<CharacterCreatorButtonBackgroundGradientStop>())
                        .Where(stop => stop != null)
                        .Select(stop => new CharacterCreatorButtonBackgroundGradientStop
                        {
                            Color = string.IsNullOrWhiteSpace(stop.Color) ? "#FFFFFFFF" : stop.Color.Trim(),
                            Position = Math.Clamp(stop.Position, 0.0, 1.0)
                        })
                        .OrderBy(stop => stop.Position)
                        .ToList(),
                    GradientMidpoints = (raw.GradientMidpoints ?? new List<double>())
                        .Select(value => Math.Clamp(value, 0.0, 1.0))
                        .ToList()
                };

                if (string.IsNullOrWhiteSpace(preset.Name))
                {
                    continue;
                }

                if (preset.GradientStops.Count < 2)
                {
                    preset.GradientStops = new List<CharacterCreatorButtonBackgroundGradientStop>
                    {
                        new CharacterCreatorButtonBackgroundGradientStop { Color = "#FFFFFFFF", Position = 0.0 },
                        new CharacterCreatorButtonBackgroundGradientStop { Color = "#FF000000", Position = 1.0 }
                    };
                }

                List<double> normalizedMidpoints = new List<double>();
                for (int i = 0; i < preset.GradientStops.Count - 1; i++)
                {
                    double left = preset.GradientStops[i].Position;
                    double right = preset.GradientStops[i + 1].Position;
                    double midpoint = i < preset.GradientMidpoints.Count
                        ? preset.GradientMidpoints[i]
                        : left + ((right - left) * 0.5);
                    normalizedMidpoints.Add(Math.Clamp(midpoint, left, right));
                }
                preset.GradientMidpoints = normalizedMidpoints;

                string baseName = preset.Name;
                int suffix = 2;
                string uniquenessKey = $"{preset.Name}|{preset.Mode}";
                while (!usedNameModeKeys.Add(uniquenessKey))
                {
                    preset.Name = $"{baseName} ({suffix})";
                    uniquenessKey = $"{preset.Name}|{preset.Mode}";
                    suffix++;
                }

                normalized.Add(preset);
            }

            return normalized;
        }

        private static List<CharacterCreatorButtonEffectPreset> NormalizeButtonEffectPresets(
            IEnumerable<CharacterCreatorButtonEffectPreset>? presets)
        {
            List<CharacterCreatorButtonEffectPreset> normalized = new List<CharacterCreatorButtonEffectPreset>();
            HashSet<string> usedNameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterCreatorButtonEffectPreset? raw in presets ?? Array.Empty<CharacterCreatorButtonEffectPreset>())
            {
                if (raw == null)
                {
                    continue;
                }

                CharacterCreatorButtonEffectPreset preset = new CharacterCreatorButtonEffectPreset
                {
                    Id = string.IsNullOrWhiteSpace(raw.Id) ? Guid.NewGuid().ToString("N") : raw.Id.Trim(),
                    Name = raw.Name?.Trim() ?? string.Empty,
                    Mode = raw.Mode,
                    OpacityPercent = Math.Clamp(raw.OpacityPercent, 0, 100),
                    DarknessPercent = Math.Clamp(raw.DarknessPercent, 0, 100),
                    OverlayPath = raw.OverlayPath?.Trim() ?? string.Empty,
                    AddBorder = raw.AddBorder,
                    BorderColor = string.IsNullOrWhiteSpace(raw.BorderColor) ? "#FF000000" : raw.BorderColor.Trim(),
                    BorderWidth = Math.Clamp(raw.BorderWidth, 1, 32)
                };

                if (string.IsNullOrWhiteSpace(preset.Name))
                {
                    continue;
                }

                string baseName = preset.Name;
                int suffix = 2;
                string uniquenessKey = $"{preset.Name}|{preset.Mode}";
                while (!usedNameKeys.Add(uniquenessKey))
                {
                    preset.Name = $"{baseName} ({suffix})";
                    uniquenessKey = $"{preset.Name}|{preset.Mode}";
                    suffix++;
                }

                normalized.Add(preset);
            }

            return normalized;
        }

        private static void NormalizeLastBulkButtonIconConfig(SaveData data)
        {
            CharacterCreatorLastBulkButtonIconConfig? config = data.CharacterCreatorLastBulkButtonIconConfig;
            if (config == null)
            {
                return;
            }

            config.ApplyScope = string.IsNullOrWhiteSpace(config.ApplyScope) ? "Missing button_on or button_off" : config.ApplyScope.Trim();
            config.BackgroundBuiltInPresetName = string.IsNullOrWhiteSpace(config.BackgroundBuiltInPresetName)
                ? "Oceanya BG"
                : config.BackgroundBuiltInPresetName.Trim();
            config.BackgroundCustomPresetId = config.BackgroundCustomPresetId?.Trim() ?? string.Empty;
            config.SolidColor = string.IsNullOrWhiteSpace(config.SolidColor) ? "#00000000" : config.SolidColor.Trim();
            config.BackgroundUploadPath = config.BackgroundUploadPath?.Trim() ?? string.Empty;
            config.BackgroundBorderColor = string.IsNullOrWhiteSpace(config.BackgroundBorderColor)
                ? "#FF000000"
                : config.BackgroundBorderColor.Trim();
            config.BackgroundBorderWidth = Math.Clamp(config.BackgroundBorderWidth, 1, 32);
            config.GradientStops ??= new List<CharacterCreatorButtonBackgroundGradientStop>();
            config.GradientStops = config.GradientStops
                .Where(stop => stop != null)
                .Select(stop => new CharacterCreatorButtonBackgroundGradientStop
                {
                    Color = string.IsNullOrWhiteSpace(stop.Color) ? "#FFFFFFFF" : stop.Color.Trim(),
                    Position = Math.Clamp(stop.Position, 0.0, 1.0)
                })
                .OrderBy(stop => stop.Position)
                .ToList();
            config.GradientMidpoints ??= new List<double>();
            config.GradientMidpoints = config.GradientMidpoints
                .Select(value => Math.Clamp(value, 0.0, 1.0))
                .ToList();

            config.OnEffect ??= new CharacterCreatorButtonEffectSnapshot();
            config.OffEffect ??= new CharacterCreatorButtonEffectSnapshot
            {
                Mode = CharacterCreatorButtonEffectPresetMode.Darken,
                DarknessPercent = 50
            };

            NormalizeEffectSnapshot(config.OnEffect);
            NormalizeEffectSnapshot(config.OffEffect);
        }

        private static void NormalizeEffectSnapshot(CharacterCreatorButtonEffectSnapshot snapshot)
        {
            snapshot.OpacityPercent = Math.Clamp(snapshot.OpacityPercent, 0, 100);
            snapshot.DarknessPercent = Math.Clamp(snapshot.DarknessPercent, 0, 100);
            snapshot.OverlayPath = snapshot.OverlayPath?.Trim() ?? string.Empty;
            snapshot.BorderColor = string.IsNullOrWhiteSpace(snapshot.BorderColor)
                ? "#FF000000"
                : snapshot.BorderColor.Trim();
            snapshot.BorderWidth = Math.Clamp(snapshot.BorderWidth, 1, 32);
            snapshot.CustomPresetId = snapshot.CustomPresetId?.Trim() ?? string.Empty;
        }
    }


}
