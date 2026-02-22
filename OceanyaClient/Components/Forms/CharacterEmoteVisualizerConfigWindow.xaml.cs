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
    /// Configures view presets for the character emote visualizer.
    /// </summary>
    public partial class CharacterEmoteVisualizerConfigWindow : Window
    {
        private readonly EmoteVisualizerConfig workingConfig;
        private readonly List<EmoteColumnEditorItem> columnEditorItems = new List<EmoteColumnEditorItem>();

        private bool suppressControlEvents;

        /// <summary>
        /// Gets the resulting config if the dialog returns true.
        /// </summary>
        public EmoteVisualizerConfig ResultConfig { get; private set; } = new EmoteVisualizerConfig();

        public CharacterEmoteVisualizerConfigWindow(EmoteVisualizerConfig sourceConfig)
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

            EmoteVisualizerViewPreset? selected = workingConfig.Presets.FirstOrDefault(p =>
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

        private EmoteVisualizerViewPreset? GetSelectedPreset()
        {
            return PresetListBox.SelectedItem as EmoteVisualizerViewPreset;
        }

        private void UpdateEditorForSelection()
        {
            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
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

            TableRowHeightSlider.Value = selected.Table.RowHeight;
            TableFontSizeSlider.Value = selected.Table.FontSize;

            RefreshNormalValueText();
            RefreshTableValueText();
            RebuildColumnsEditor(selected);
            UpdateModePanels(selected.Mode);

            suppressControlEvents = false;
        }

        private void RebuildColumnsEditor(EmoteVisualizerViewPreset preset)
        {
            columnEditorItems.Clear();

            foreach (EmoteVisualizerTableColumnConfig column in preset.Table.Columns.OrderBy(c => c.Order))
            {
                columnEditorItems.Add(new EmoteColumnEditorItem
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

        private static string GetColumnDisplayName(EmoteVisualizerTableColumnKey key)
        {
            return key switch
            {
                EmoteVisualizerTableColumnKey.Icon => "Emote Icon",
                EmoteVisualizerTableColumnKey.Id => "ID",
                EmoteVisualizerTableColumnKey.Name => "Emote Name",
                EmoteVisualizerTableColumnKey.PreAnimationPreview => "Pre Animation Preview",
                EmoteVisualizerTableColumnKey.AnimationPreview => "Final Animation Preview",
                EmoteVisualizerTableColumnKey.PreAnimationPath => "Pre Animation Path",
                EmoteVisualizerTableColumnKey.AnimationPath => "Final Animation Path",
                _ => key.ToString()
            };
        }

        private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
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
            EmoteVisualizerViewPreset newPreset = new EmoteVisualizerViewPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"Custom {next}",
                Mode = FolderVisualizerLayoutMode.Normal,
                Normal = new FolderVisualizerNormalViewConfig(),
                Table = new EmoteVisualizerTableViewConfig
                {
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
            };

            workingConfig.Presets.Add(newPreset);
            BindPresetList();
            PresetListBox.SelectedItem = newPreset;
        }

        private void RemovePresetButton_Click(object sender, RoutedEventArgs e)
        {
            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
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

            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
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

            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
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

            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
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
        }

        private void TableSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            selected.Table.RowHeight = TableRowHeightSlider.Value;
            selected.Table.FontSize = TableFontSizeSlider.Value;

            RefreshTableValueText();
        }

        private void RefreshTableValueText()
        {
            TableRowHeightValueText.Text = ((int)Math.Round(TableRowHeightSlider.Value)).ToString(CultureInfo.InvariantCulture);
            TableFontSizeValueText.Text = ((int)Math.Round(TableFontSizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        }

        private void ColumnVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SyncColumnsToPreset();
        }

        private void ColumnsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No-op.
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

            EmoteVisualizerViewPreset? selected = GetSelectedPreset();
            if (selected == null)
            {
                return;
            }

            Dictionary<EmoteVisualizerTableColumnKey, double> widthByColumn = selected.Table.Columns
                .GroupBy(column => column.Key)
                .ToDictionary(group => group.Key, group => group.First().Width);

            selected.Table.Columns.Clear();

            for (int i = 0; i < columnEditorItems.Count; i++)
            {
                EmoteColumnEditorItem editor = columnEditorItems[i];

                selected.Table.Columns.Add(new EmoteVisualizerTableColumnConfig
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
            foreach (EmoteVisualizerViewPreset preset in workingConfig.Presets)
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

        private static EmoteVisualizerConfig CloneConfig(EmoteVisualizerConfig source)
        {
            EmoteVisualizerConfig clone = new EmoteVisualizerConfig
            {
                SelectedPresetId = source.SelectedPresetId,
                SelectedPresetName = source.SelectedPresetName,
                Presets = new List<EmoteVisualizerViewPreset>()
            };

            foreach (EmoteVisualizerViewPreset preset in source.Presets)
            {
                clone.Presets.Add(CharacterEmoteVisualizerWindow.ClonePreset(preset));
            }

            if (clone.Presets.Count == 0)
            {
                clone.Presets.Add(new EmoteVisualizerViewPreset { Name = "View" });
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
                EmoteVisualizerViewPreset selected = clone.Presets.First(p =>
                    string.Equals(p.Id, clone.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
                clone.SelectedPresetName = selected.Name;
            }

            return clone;
        }
    }

    public sealed class EmoteColumnEditorItem
    {
        public EmoteVisualizerTableColumnKey Key { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
    }
}
