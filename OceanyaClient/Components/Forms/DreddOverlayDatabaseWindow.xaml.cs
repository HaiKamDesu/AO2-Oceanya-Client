using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace OceanyaClient
{
    public partial class DreddOverlayDatabaseWindow : Window
    {
        private readonly ObservableCollection<DreddOverlayEntry> overlays = new ObservableCollection<DreddOverlayEntry>();

        public DreddOverlayDatabaseWindow()
        {
            InitializeComponent();
            LoadOverlayDatabase();
        }

        private void LoadOverlayDatabase()
        {
            overlays.Clear();
            foreach (DreddOverlayEntry entry in SaveFile.Data.DreddBackgroundOverlayOverride.OverlayDatabase)
            {
                overlays.Add(new DreddOverlayEntry
                {
                    Name = entry.Name,
                    FilePath = entry.FilePath
                });
            }

            OverlayGrid.ItemsSource = overlays;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            DreddOverlayEntryDialog dialog = new DreddOverlayEntryDialog
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            overlays.Add(new DreddOverlayEntry
            {
                Name = dialog.OverlayName,
                FilePath = dialog.OverlayPath
            });
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (OverlayGrid.SelectedItem is not DreddOverlayEntry selected)
            {
                OceanyaMessageBox.Show("Select an overlay entry to edit.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DreddOverlayEntryDialog dialog = new DreddOverlayEntryDialog(selected.Name, selected.FilePath)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selected.Name = dialog.OverlayName;
            selected.FilePath = dialog.OverlayPath;
            OverlayGrid.Items.Refresh();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (OverlayGrid.SelectedItem is not DreddOverlayEntry selected)
            {
                OceanyaMessageBox.Show("Select an overlay entry to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            overlays.Remove(selected);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateOverlayDatabase(out string error))
            {
                OceanyaMessageBox.Show(error, "Invalid Overlay Database", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFile.Data.DreddBackgroundOverlayOverride.OverlayDatabase = overlays
                .Select(entry => new DreddOverlayEntry
                {
                    Name = entry.Name.Trim(),
                    FilePath = entry.FilePath.Trim()
                })
                .ToList();

            if (!SaveFile.Data.DreddBackgroundOverlayOverride.OverlayDatabase.Any(entry =>
                string.Equals(entry.Name, SaveFile.Data.DreddBackgroundOverlayOverride.SelectedOverlayName, StringComparison.OrdinalIgnoreCase)))
            {
                SaveFile.Data.DreddBackgroundOverlayOverride.SelectedOverlayName = string.Empty;
            }

            SaveFile.Save();
            DialogResult = true;
            Close();
        }

        private bool ValidateOverlayDatabase(out string error)
        {
            error = string.Empty;

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DreddOverlayEntry entry in overlays)
            {
                string name = entry.Name?.Trim() ?? string.Empty;
                string path = entry.FilePath?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name))
                {
                    error = "Overlay name cannot be empty.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    error = $"Overlay '{name}' has an empty path.";
                    return false;
                }

                if (!names.Add(name))
                {
                    error = $"Overlay name '{name}' is duplicated.";
                    return false;
                }
            }

            return true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TitleCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
