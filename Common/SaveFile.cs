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

    public enum FolderVisualizerLayoutMode
    {
        Normal,
        Table
    }

    public enum FolderVisualizerTableColumnKey
    {
        Icon,
        Name,
        DirectoryPath,
        PreviewPath,
        LastModified,
        EmoteCount,
        Size,
        OpenCharIni,
        Readme
    }

    public enum EmoteVisualizerTableColumnKey
    {
        Icon,
        Id,
        Name,
        PreAnimationPreview,
        AnimationPreview,
        PreAnimationPath,
        AnimationPath
    }

    public class FolderVisualizerNormalViewConfig
    {
        public double TileWidth { get; set; } = 170;
        public double TileHeight { get; set; } = 182;
        public double IconSize { get; set; } = 18;
        public double NameFontSize { get; set; } = 12;
        public double InternalTilePadding { get; set; } = 2;
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
        public bool IsMaximized { get; set; }
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
        public FolderVisualizerConfig FolderVisualizer { get; set; } = new FolderVisualizerConfig();
        public EmoteVisualizerConfig EmoteVisualizer { get; set; } = new EmoteVisualizerConfig();
        public VisualizerWindowState FolderVisualizerWindowState { get; set; } = new VisualizerWindowState();
        public VisualizerWindowState EmoteVisualizerWindowState { get; set; } = new VisualizerWindowState();
        public bool LoopEmoteVisualizerAnimations { get; set; } = true;
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

            data.FolderVisualizerWindowState ??= new VisualizerWindowState();
            data.EmoteVisualizerWindowState ??= new VisualizerWindowState();
            ClampWindowState(data.FolderVisualizerWindowState);
            ClampWindowState(data.EmoteVisualizerWindowState);

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
                        string.Equals(p.Name, "Medium", StringComparison.OrdinalIgnoreCase))
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
                        string.Equals(p.Name, "Detailed", StringComparison.OrdinalIgnoreCase))
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

        private static void ClampPresetValues(FolderVisualizerViewPreset preset)
        {
            preset.Normal.TileWidth = Math.Clamp(preset.Normal.TileWidth, 100, 420);
            preset.Normal.TileHeight = Math.Clamp(preset.Normal.TileHeight, 120, 480);
            preset.Normal.IconSize = Math.Clamp(preset.Normal.IconSize, 12, 48);
            preset.Normal.NameFontSize = Math.Clamp(preset.Normal.NameFontSize, 9, 30);
            preset.Normal.InternalTilePadding = Math.Clamp(preset.Normal.InternalTilePadding, 0, 24);
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
                    Key = FolderVisualizerTableColumnKey.Icon,
                    IsVisible = true,
                    Order = 0,
                    Width = 30
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Name,
                    IsVisible = true,
                    Order = 1,
                    Width = 320
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.DirectoryPath,
                    IsVisible = false,
                    Order = 2,
                    Width = 460
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.PreviewPath,
                    IsVisible = false,
                    Order = 3,
                    Width = 460
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.LastModified,
                    IsVisible = true,
                    Order = 4,
                    Width = 170
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.EmoteCount,
                    IsVisible = true,
                    Order = 5,
                    Width = 110
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Size,
                    IsVisible = true,
                    Order = 6,
                    Width = 110
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.OpenCharIni,
                    IsVisible = true,
                    Order = 7,
                    Width = 120
                },
                new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Readme,
                    IsVisible = true,
                    Order = 8,
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
                    Table = new FolderVisualizerTableViewConfig
                    {
                        RowHeight = 34,
                        FontSize = 13,
                        Columns = new List<FolderVisualizerTableColumnConfig>
                        {
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Icon,
                                IsVisible = true,
                                Order = 0,
                                Width = 30
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Name,
                                IsVisible = true,
                                Order = 1,
                                Width = 380
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.DirectoryPath,
                                IsVisible = false,
                                Order = 2,
                                Width = 480
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.PreviewPath,
                                IsVisible = false,
                                Order = 3,
                                Width = 480
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.LastModified,
                                IsVisible = true,
                                Order = 4,
                                Width = 170
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.EmoteCount,
                                IsVisible = true,
                                Order = 5,
                                Width = 110
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Size,
                                IsVisible = true,
                                Order = 6,
                                Width = 110
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.OpenCharIni,
                                IsVisible = true,
                                Order = 7,
                                Width = 120
                            },
                            new FolderVisualizerTableColumnConfig
                            {
                                Key = FolderVisualizerTableColumnKey.Readme,
                                IsVisible = true,
                                Order = 8,
                                Width = 120
                            }
                        }
                    }
                },
                new FolderVisualizerViewPreset
                {
                    Id = "small",
                    Name = "Small",
                    Mode = FolderVisualizerLayoutMode.Normal,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 138,
                        TileHeight = 140,
                        IconSize = 16,
                        NameFontSize = 11,
                        InternalTilePadding = 2,
                        TilePadding = 8,
                        TileMargin = 4
                    }
                },
                new FolderVisualizerViewPreset
                {
                    Id = "medium",
                    Name = "Medium",
                    Mode = FolderVisualizerLayoutMode.Normal,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 170,
                        TileHeight = 182,
                        IconSize = 18,
                        NameFontSize = 12,
                        InternalTilePadding = 2,
                        TilePadding = 8,
                        TileMargin = 4
                    }
                },
                new FolderVisualizerViewPreset
                {
                    Id = "large",
                    Name = "Large",
                    Mode = FolderVisualizerLayoutMode.Normal,
                    Normal = new FolderVisualizerNormalViewConfig
                    {
                        TileWidth = 222,
                        TileHeight = 246,
                        IconSize = 20,
                        NameFontSize = 13,
                        InternalTilePadding = 3,
                        TilePadding = 10,
                        TileMargin = 4
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
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPreview, IsVisible = true, Order = 3, Width = 110 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPreview, IsVisible = true, Order = 4, Width = 110 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPath, IsVisible = false, Order = 5, Width = 320 },
                new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPath, IsVisible = false, Order = 6, Width = 320 }
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
                    Table = new EmoteVisualizerTableViewConfig
                    {
                        RowHeight = 58,
                        FontSize = 13,
                        Columns = new List<EmoteVisualizerTableColumnConfig>
                        {
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Icon, IsVisible = true, Order = 0, Width = 34 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Id, IsVisible = true, Order = 1, Width = 54 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.Name, IsVisible = true, Order = 2, Width = 230 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPreview, IsVisible = true, Order = 3, Width = 110 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPreview, IsVisible = true, Order = 4, Width = 110 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.PreAnimationPath, IsVisible = false, Order = 5, Width = 320 },
                            new EmoteVisualizerTableColumnConfig { Key = EmoteVisualizerTableColumnKey.AnimationPath, IsVisible = false, Order = 6, Width = 320 }
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
                        TileWidth = 235,
                        TileHeight = 210,
                        IconSize = 18,
                        NameFontSize = 12,
                        InternalTilePadding = 2,
                        TilePadding = 8,
                        TileMargin = 4
                    }
                }
            };
        }

        private static void ClampWindowState(VisualizerWindowState state)
        {
            state.Width = Math.Clamp(state.Width, 760, 6000);
            state.Height = Math.Clamp(state.Height, 520, 4000);
        }
    }

    
}
