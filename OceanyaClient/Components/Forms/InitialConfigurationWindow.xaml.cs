using AOBot_Testing.Structures;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for InitialConfigurationWindow.xaml
    /// </summary>
    public partial class InitialConfigurationWindow : Window
    {
        private ServerEndpointDefinition? selectedServer;
        private bool hasMigratedLegacyCustomEntries;

        public InitialConfigurationWindow()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            LoadSavefile();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Config files (*.ini)|*.ini",
                Title = "Select base config.ini"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ConfigINIPathTextBox.Text = openFileDialog.FileName;
            }
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string configIniPath = ConfigINIPathTextBox.Text?.Trim() ?? string.Empty;
            string selectedServerEndpoint = selectedServer?.Endpoint?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(configIniPath))
            {
                OceanyaMessageBox.Show(
                    "Please provide the config.ini path.",
                    "Invalid Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedServerEndpoint))
            {
                OceanyaMessageBox.Show(
                    "Please select a server endpoint.",
                    "Invalid Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(configIniPath))
            {
                OceanyaMessageBox.Show("File not found: " + configIniPath);
                return;
            }

            if (!string.Equals(Path.GetFileName(configIniPath), "config.ini", StringComparison.OrdinalIgnoreCase))
            {
                OceanyaMessageBox.Show("The filepath does not point to config.ini! " + configIniPath);
                return;
            }

            try
            {
                Globals.UpdateConfigINI(configIniPath);
                Globals.SetSelectedServerEndpoint(selectedServerEndpoint);
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show(
                    "Error updating base folders: " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            SaveConfiguration(
                configIniPath,
                UseSingleClientCheckBox.IsChecked != false,
                selectedServerEndpoint,
                selectedServer?.Name ?? string.Empty);

            if (RefreshInfoCheckBox.IsChecked == true)
            {
                await WaitForm.ShowFormAsync("Refreshing character and background info...", this);
                CharacterFolder.RefreshCharacterList(
                    onParsedCharacter: (ini) =>
                    {
                        WaitForm.SetSubtitle("Parsed Character: " + ini.Name);
                    },
                    onChangedMountPath: (path) =>
                    {
                        WaitForm.SetSubtitle("Changed mount path: " + path);
                    });

                AOBot_Testing.Structures.Background.RefreshCache(
                    onChangedMountPath: (path) =>
                    {
                        WaitForm.SetSubtitle("Indexed background mount path: " + path);
                    });

                WaitForm.CloseForm();
            }

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void SelectServerButton_Click(object sender, RoutedEventArgs e)
        {
            string configIniPath = ConfigINIPathTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configIniPath) || !File.Exists(configIniPath))
            {
                OceanyaMessageBox.Show(
                    "Select a valid config.ini path before opening server selection.",
                    "Missing Config",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(Path.GetFileName(configIniPath), "config.ini", StringComparison.OrdinalIgnoreCase))
            {
                OceanyaMessageBox.Show(
                    "The filepath does not point to config.ini! " + configIniPath,
                    "Invalid Config",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MigrateLegacyCustomEntriesToFavoritesIfNeeded(configIniPath);

            string currentEndpoint = selectedServer?.Endpoint
                ?? SaveFile.Data.SelectedServerEndpoint
                ?? Globals.GetDefaultServerEndpoint();

            ServerSelectionDialog dialog = new ServerSelectionDialog(configIniPath, currentEndpoint)
            {
                Owner = this
            };

            bool? result = dialog.ShowDialog();
            if (result != true || dialog.SelectedServer == null)
            {
                return;
            }

            selectedServer = dialog.SelectedServer;
            UpdateSelectedServerDisplay();
        }

        private void LoadSavefile()
        {
            try
            {
                ConfigINIPathTextBox.Text = SaveFile.Data.ConfigIniPath;
                UseSingleClientCheckBox.IsChecked = SaveFile.Data.UseSingleInternalClient;

                selectedServer = ResolveInitialSelectedServer();
                if (selectedServer != null)
                {
                    Globals.SetSelectedServerEndpoint(selectedServer.Endpoint);
                }

                UpdateSelectedServerDisplay();
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show(
                    "Error loading configuration: " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private ServerEndpointDefinition? ResolveInitialSelectedServer()
        {
            string savedEndpoint = SaveFile.Data.SelectedServerEndpoint?.Trim() ?? string.Empty;
            List<ServerEndpointDefinition> defaults = ServerEndpointCatalog.LoadDefaultServers();
            string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;

            List<ServerEndpointDefinition> favorites = new List<ServerEndpointDefinition>();
            if (!string.IsNullOrWhiteSpace(configIniPath) && File.Exists(configIniPath))
            {
                favorites = ServerEndpointCatalog.LoadFavorites(configIniPath);
            }

            List<ServerEndpointDefinition> knownServers = new List<ServerEndpointDefinition>();
            knownServers.AddRange(defaults);
            knownServers.AddRange(favorites);

            if (!string.IsNullOrWhiteSpace(savedEndpoint))
            {
                ServerEndpointDefinition? knownMatch = knownServers.FirstOrDefault(server =>
                    string.Equals(server.Endpoint, savedEndpoint, StringComparison.OrdinalIgnoreCase));
                if (knownMatch != null)
                {
                    return knownMatch;
                }

                string savedName = SaveFile.Data.SelectedServerName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(savedName))
                {
                    savedName = savedEndpoint;
                }

                return new ServerEndpointDefinition
                {
                    Name = savedName,
                    Endpoint = savedEndpoint,
                    Description = "Previously selected endpoint.",
                    Source = ServerEndpointSource.Defaults,
                    IsLegacy = false
                };
            }

            return defaults.FirstOrDefault();
        }

        private void MigrateLegacyCustomEntriesToFavoritesIfNeeded(string configIniPath)
        {
            if (hasMigratedLegacyCustomEntries)
            {
                return;
            }

            List<CustomServerEntry> legacyEntries = SaveFile.Data.CustomServerEntries ?? new List<CustomServerEntry>();
            if (legacyEntries.Count == 0)
            {
                hasMigratedLegacyCustomEntries = true;
                return;
            }

            List<FavoriteServerEntry> existingFavorites = FavoriteServerStore.LoadFavorites(
                Path.Combine(Path.GetDirectoryName(configIniPath) ?? string.Empty, "favorite_servers.ini"));

            foreach (CustomServerEntry legacyEntry in legacyEntries)
            {
                if (legacyEntry == null || string.IsNullOrWhiteSpace(legacyEntry.Endpoint))
                {
                    continue;
                }

                if (!TryParseEndpoint(legacyEntry.Endpoint, out string address, out int port))
                {
                    continue;
                }

                bool alreadyExists = existingFavorites.Any(favorite =>
                    string.Equals(favorite.Address, address, StringComparison.OrdinalIgnoreCase)
                    && favorite.Port == port);
                if (alreadyExists)
                {
                    continue;
                }

                FavoriteServerEntry migratedEntry = new FavoriteServerEntry
                {
                    Name = string.IsNullOrWhiteSpace(legacyEntry.Name) ? "Migrated Favorite" : legacyEntry.Name.Trim(),
                    Address = address,
                    Port = port,
                    Description = "Migrated from Oceanya custom endpoint.",
                    Legacy = false
                };

                ServerEndpointCatalog.AddFavorite(configIniPath, migratedEntry);
                existingFavorites.Add(migratedEntry);
            }

            hasMigratedLegacyCustomEntries = true;
        }

        private void UpdateSelectedServerDisplay()
        {
            string serverNameText = selectedServer?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverNameText))
            {
                serverNameText = "No server selected.";
            }

            SelectedServerTextBox.Text = serverNameText;
        }

        private void SaveConfiguration(
            string configIniPath,
            bool useSingleInternalClient,
            string selectedServerEndpoint,
            string selectedServerName)
        {
            try
            {
                SaveFile.Data.ConfigIniPath = configIniPath;
                SaveFile.Data.UseSingleInternalClient = useSingleInternalClient;
                SaveFile.Data.SelectedServerEndpoint = selectedServerEndpoint;
                SaveFile.Data.SelectedServerName = selectedServerName?.Trim() ?? string.Empty;
                SaveFile.Save();
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show(
                    "Error saving configuration: " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public static bool IsValidServerEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return false;
            }

            bool validScheme = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);

            return validScheme && !string.IsNullOrWhiteSpace(uri.Host);
        }

        private static bool TryParseEndpoint(string endpoint, out string address, out int port)
        {
            address = string.Empty;
            port = 0;

            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return false;
            }

            bool validScheme = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            if (!validScheme)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            {
                return false;
            }

            address = uri.Host;
            port = uri.Port;
            return true;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private bool isClosing;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isClosing)
            {
                return;
            }

            e.Cancel = true;
            isClosing = true;

            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    isClosing = true;
                    Close();
                });
            };
            fadeOut.Begin(this);
        }
    }
}
