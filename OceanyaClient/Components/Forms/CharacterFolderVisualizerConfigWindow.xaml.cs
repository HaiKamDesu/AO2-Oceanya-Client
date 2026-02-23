using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Configures view presets for the character folder visualizer.
    /// </summary>
    public partial class CharacterFolderVisualizerConfigWindow : Window
    {
        private readonly FolderVisualizerConfig workingConfig;
        private readonly List<ColumnEditorItem> columnEditorItems = new List<ColumnEditorItem>();

        private bool suppressControlEvents;

        /// <summary>
        /// Gets the resulting config if the dialog returns true.
        /// </summary>
        public FolderVisualizerConfig ResultConfig { get; private set; } = new FolderVisualizerConfig();

        public CharacterFolderVisualizerConfigWindow(FolderVisualizerConfig sourceConfig)
        {
            InitializeComponent();
            workingConfig = CloneConfig(sourceConfig);

            BindPresetList();
        }

        private void BindPresetList()
        {
            suppressControlEvents = true;

            PresetListBox.ItemsSource = null;
            PresetListBox.ItemsSource = workingConfig.Presets;

            FolderVisualizerViewPreset? selected = workingConfig.Presets.FirstOrDefault(p =>
                string.Equals(p.Id, workingConfig.SelectedPresetId, StringComparison.OrdinalIgnoreCase));

            selected ??= workingConfig.Presets.FirstOrDefault();
            if (selected != null)
            {
                workingConfig.SelectedPresetId = selected.Id;
            }

            PresetListBox.SelectedItem = selected;
            suppressControlEvents = false;

            UpdateEditorForSelection();
        }

        private FolderVisualizerViewPreset? GetSelectedPreset()
        {
            return PresetListBox.SelectedItem as FolderVisualizerViewPreset;
        }

        private void UpdateEditorForSelection()
        {
            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            suppressControlEvents = true;

            PresetNameTextBox.Text = selected.Name;
            PresetModeCombo.SelectedIndex = selected.Mode == FolderVisualizerLayoutMode.Table ? 1 : 0;

            TileWidthSlider.Value = selected.Normal.TileWidth;
            TileHeightSlider.Value = selected.Normal.TileHeight;
            InternalTilePaddingSlider.Value = selected.Normal.InternalTilePadding;
            IconSizeSlider.Value = selected.Normal.IconSize;
            NameFontSizeSlider.Value = selected.Normal.NameFontSize;
            TilePaddingSlider.Value = selected.Normal.TilePadding;
            ScrollSpeedSlider.Value = selected.Normal.ScrollWheelStep;

            TableFontSizeSlider.Value = selected.Table.FontSize;

            RefreshNormalValueText();
            RefreshTableValueText();
            RebuildColumnsEditor(selected);
            UpdateModePanels(selected.Mode);

            suppressControlEvents = false;
        }

        private void RebuildColumnsEditor(FolderVisualizerViewPreset preset)
        {
            columnEditorItems.Clear();

            foreach (FolderVisualizerTableColumnConfig column in preset.Table.Columns.OrderBy(c => c.Order))
            {
                columnEditorItems.Add(new ColumnEditorItem
                {
                    Key = column.Key,
                    DisplayName = GetColumnDisplayName(column.Key),
                    IsVisible = column.IsVisible
                });
            }

            ColumnsListBox.ItemsSource = null;
            ColumnsListBox.ItemsSource = columnEditorItems;
            ColumnsListBox.SelectedIndex = columnEditorItems.Count > 0 ? 0 : -1;
        }

        private static string GetColumnDisplayName(FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.Icon => "Folder Icon",
                FolderVisualizerTableColumnKey.RowNumber => "ID",
                FolderVisualizerTableColumnKey.IconType => "Icon Type",
                FolderVisualizerTableColumnKey.Name => "Character Name",
                FolderVisualizerTableColumnKey.Tags => "Tags",
                FolderVisualizerTableColumnKey.DirectoryPath => "Folder Path",
                FolderVisualizerTableColumnKey.PreviewPath => "Idle Sprite Path",
                FolderVisualizerTableColumnKey.LastModified => "Last Modified",
                FolderVisualizerTableColumnKey.EmoteCount => "Emote Count",
                FolderVisualizerTableColumnKey.Size => "Folder Size",
                FolderVisualizerTableColumnKey.IntegrityFailures => "Integrity Failures",
                FolderVisualizerTableColumnKey.OpenCharIni => "Open char.ini",
                FolderVisualizerTableColumnKey.Readme => "Readme",
                _ => key.ToString()
            };
        }

        private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected != null)
            {
                workingConfig.SelectedPresetId = selected.Id;
                workingConfig.SelectedPresetName = selected.Name;
            }

            UpdateEditorForSelection();
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            int next = workingConfig.Presets.Count + 1;
            FolderVisualizerViewPreset newPreset = new FolderVisualizerViewPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"Custom {next}",
                Mode = FolderVisualizerLayoutMode.Normal,
                Normal = new FolderVisualizerNormalViewConfig(),
                Table = new FolderVisualizerTableViewConfig
                {
                    Columns = new List<FolderVisualizerTableColumnConfig>
                    {
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.RowNumber, IsVisible = true, Order = 0, Width = 56 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Icon, IsVisible = true, Order = 1, Width = 30 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.IconType, IsVisible = false, Order = 2, Width = 150 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Name, IsVisible = true, Order = 3, Width = 320 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Tags, IsVisible = true, Order = 4, Width = 260 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.DirectoryPath, IsVisible = false, Order = 5, Width = 460 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.PreviewPath, IsVisible = false, Order = 6, Width = 460 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.LastModified, IsVisible = true, Order = 7, Width = 170 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.EmoteCount, IsVisible = true, Order = 8, Width = 110 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Size, IsVisible = true, Order = 9, Width = 110 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.IntegrityFailures, IsVisible = true, Order = 10, Width = 420 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.OpenCharIni, IsVisible = true, Order = 11, Width = 120 },
                        new FolderVisualizerTableColumnConfig { Key = FolderVisualizerTableColumnKey.Readme, IsVisible = true, Order = 12, Width = 120 }
                    }
                }
            };

            workingConfig.Presets.Add(newPreset);
            BindPresetList();
            PresetListBox.SelectedItem = newPreset;
        }

        private void RemovePresetButton_Click(object sender, RoutedEventArgs e)
        {
            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            if (workingConfig.Presets.Count <= 1)
            {
                OceanyaMessageBox.Show(this,
                    "At least one view preset must remain.",
                    "Remove Preset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            workingConfig.Presets.Remove(selected);
            if (string.Equals(workingConfig.SelectedPresetId, selected.Id, StringComparison.OrdinalIgnoreCase))
            {
                workingConfig.SelectedPresetId = workingConfig.Presets[0].Id;
                workingConfig.SelectedPresetName = workingConfig.Presets[0].Name;
            }

            BindPresetList();
        }

        private void PresetNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            selected.Name = string.IsNullOrWhiteSpace(PresetNameTextBox.Text)
                ? "View"
                : PresetNameTextBox.Text.Trim();

            PresetListBox.Items.Refresh();
        }

        private void PresetModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            selected.Mode = PresetModeCombo.SelectedIndex == 1
                ? FolderVisualizerLayoutMode.Table
                : FolderVisualizerLayoutMode.Normal;

            UpdateModePanels(selected.Mode);
        }

        private void UpdateModePanels(FolderVisualizerLayoutMode mode)
        {
            bool isTable = mode == FolderVisualizerLayoutMode.Table;
            NormalPanel.Visibility = isTable ? Visibility.Collapsed : Visibility.Visible;
            TablePanel.Visibility = isTable ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NormalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            selected.Normal.TileWidth = TileWidthSlider.Value;
            selected.Normal.TileHeight = TileHeightSlider.Value;
            selected.Normal.InternalTilePadding = InternalTilePaddingSlider.Value;
            selected.Normal.IconSize = IconSizeSlider.Value;
            selected.Normal.NameFontSize = NameFontSizeSlider.Value;
            selected.Normal.TilePadding = TilePaddingSlider.Value;
            selected.Normal.ScrollWheelStep = ScrollSpeedSlider.Value;

            RefreshNormalValueText();
        }

        private void RefreshNormalValueText()
        {
            TileWidthValueText.Text = ((int)Math.Round(TileWidthSlider.Value)).ToString(CultureInfo.InvariantCulture);
            TileHeightValueText.Text = ((int)Math.Round(TileHeightSlider.Value)).ToString(CultureInfo.InvariantCulture);
            InternalTilePaddingValueText.Text = ((int)Math.Round(InternalTilePaddingSlider.Value)).ToString(CultureInfo.InvariantCulture);
            IconSizeValueText.Text = ((int)Math.Round(IconSizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
            NameFontSizeValueText.Text = ((int)Math.Round(NameFontSizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
            TilePaddingValueText.Text = ((int)Math.Round(TilePaddingSlider.Value)).ToString(CultureInfo.InvariantCulture);
            ScrollSpeedValueText.Text = ((int)Math.Round(ScrollSpeedSlider.Value)).ToString(CultureInfo.InvariantCulture);
        }

        private void TableSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            selected.Table.FontSize = TableFontSizeSlider.Value;

            RefreshTableValueText();
        }

        private void RefreshTableValueText()
        {
            TableFontSizeValueText.Text = ((int)Math.Round(TableFontSizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        }

        private void ColumnVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SyncColumnsToPreset();
        }

        private void ColumnsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No-op: just used by move buttons.
        }

        private void MoveColumnUpButton_Click(object sender, RoutedEventArgs e)
        {
            int index = ColumnsListBox.SelectedIndex;
            if (index <= 0)
            {
                return;
            }

            (columnEditorItems[index - 1], columnEditorItems[index]) = (columnEditorItems[index], columnEditorItems[index - 1]);
            ColumnsListBox.Items.Refresh();
            ColumnsListBox.SelectedIndex = index - 1;
            SyncColumnsToPreset();
        }

        private void MoveColumnDownButton_Click(object sender, RoutedEventArgs e)
        {
            int index = ColumnsListBox.SelectedIndex;
            if (index < 0 || index >= columnEditorItems.Count - 1)
            {
                return;
            }

            (columnEditorItems[index], columnEditorItems[index + 1]) = (columnEditorItems[index + 1], columnEditorItems[index]);
            ColumnsListBox.Items.Refresh();
            ColumnsListBox.SelectedIndex = index + 1;
            SyncColumnsToPreset();
        }

        private void SyncColumnsToPreset()
        {
            if (suppressControlEvents)
            {
                return;
            }

            FolderVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            Dictionary<FolderVisualizerTableColumnKey, double> widthByColumn = selected.Table.Columns
                .GroupBy(column => column.Key)
                .ToDictionary(group => group.Key, group => group.First().Width);

            selected.Table.Columns.Clear();

            for (int i = 0; i < columnEditorItems.Count; i++)
            {
                ColumnEditorItem editor = columnEditorItems[i];

                selected.Table.Columns.Add(new FolderVisualizerTableColumnConfig
                {
                    Key = editor.Key,
                    IsVisible = editor.IsVisible,
                    Order = i,
                    Width = widthByColumn.TryGetValue(editor.Key, out double width) ? width : 140
                });
            }

            ColumnsListBox.Items.Refresh();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (FolderVisualizerViewPreset preset in workingConfig.Presets)
            {
                preset.Name = string.IsNullOrWhiteSpace(preset.Name)
                    ? "View"
                    : preset.Name.Trim();
            }

            ResultConfig = CloneConfig(workingConfig);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private static FolderVisualizerConfig CloneConfig(FolderVisualizerConfig source)
        {
            FolderVisualizerConfig clone = new FolderVisualizerConfig
            {
                SelectedPresetId = source.SelectedPresetId,
                SelectedPresetName = source.SelectedPresetName,
                Presets = new List<FolderVisualizerViewPreset>()
            };

            foreach (FolderVisualizerViewPreset preset in source.Presets)
            {
                clone.Presets.Add(CharacterFolderVisualizerWindow.ClonePreset(preset));
            }

            if (clone.Presets.Count == 0)
            {
                clone.Presets.Add(new FolderVisualizerViewPreset { Name = "View" });
                clone.SelectedPresetId = clone.Presets[0].Id;
            }

            if (string.IsNullOrWhiteSpace(clone.SelectedPresetId)
                || !clone.Presets.Any(p => string.Equals(p.Id, clone.SelectedPresetId, StringComparison.OrdinalIgnoreCase)))
            {
                clone.SelectedPresetId = clone.Presets[0].Id;
                clone.SelectedPresetName = clone.Presets[0].Name;
            }
            else
            {
                FolderVisualizerViewPreset selected = clone.Presets.First(p =>
                    string.Equals(p.Id, clone.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
                clone.SelectedPresetName = selected.Name;
            }

            return clone;
        }
    }

    public sealed class ColumnEditorItem
    {
        public FolderVisualizerTableColumnKey Key { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
    }
}
