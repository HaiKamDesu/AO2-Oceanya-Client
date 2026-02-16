using AOBot_Testing.Structures;
using Microsoft.Win32;
using OceanyaClient.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for InitialConfigurationWindow.xaml
    /// </summary>
    public partial class InitialConfigurationWindow : Window
    {
        private sealed class ServerEndpointOption
        {
            private ServerEndpointOption(string name, string endpoint, string displayName, bool isPreset, bool isHeader)
            {
                Name = name;
                Endpoint = endpoint;
                DisplayName = displayName;
                IsPreset = isPreset;
                IsHeader = isHeader;
            }

            public static ServerEndpointOption Header(string displayName)
            {
                return new ServerEndpointOption(displayName, string.Empty, displayName, false, true);
            }

            public static ServerEndpointOption Entry(string name, string endpoint, bool isPreset)
            {
                string displayName = $"{name} ({endpoint})";
                return new ServerEndpointOption(name, endpoint, displayName, isPreset, false);
            }

            public string Name { get; }
            public string Endpoint { get; }
            public string DisplayName { get; }
            public bool IsPreset { get; }
            public bool IsHeader { get; }
            public bool IsSelectable => !IsHeader;

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private readonly List<ServerEndpointOption> presetServerOptions = new List<ServerEndpointOption>();
        private readonly List<ServerEndpointOption> customServerOptions = new List<ServerEndpointOption>();
        private bool isUpdatingSelection;
        private string lastSelectedEndpoint = string.Empty;

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
            string configIniPath = ConfigINIPathTextBox.Text;
            ServerEndpointOption? selectedServerOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            string selectedServerEndpoint = selectedServerOption?.Endpoint ?? string.Empty;

            if (string.IsNullOrWhiteSpace(configIniPath))
            {
                OceanyaMessageBox.Show("Please provide the config.ini path.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedServerEndpoint))
            {
                OceanyaMessageBox.Show("Please select a server endpoint.",
                                "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(configIniPath))
            {
                OceanyaMessageBox.Show("File not found: " + configIniPath);
                return;
            }

            if (Path.GetFileName(configIniPath).ToLower() != "config.ini")
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
                OceanyaMessageBox.Show("Error updating base folders: " + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SaveConfiguration(
                configIniPath,
                UseSingleClientCheckBox.IsChecked != false,
                selectedServerEndpoint,
                customServerOptions.Select(option => new CustomServerEntry
                {
                    Name = option.Name,
                    Endpoint = option.Endpoint
                }).ToList());

            if (RefreshInfoCheckBox.IsChecked == true)
            {
                await WaitForm.ShowFormAsync("Refreshing character and background info...", this);
                CharacterFolder.RefreshCharacterList
                    (
                        onParsedCharacter:
                        (ini) =>
                        {
                            WaitForm.SetSubtitle("Parsed Character: " + ini.Name);
                        },
                        onChangedMountPath:
                        (path) =>
                        {
                            WaitForm.SetSubtitle("Changed mount path: " + path);
                        }
                    );
                WaitForm.CloseForm();
            }

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();

            this.Close();
        }

        private void AddServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AddServerDialog.ShowDialog(
                    this,
                    out string serverName,
                    out string endpointInput,
                    windowTitle: "Add Custom Server",
                    actionText: "ADD SERVER"))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(serverName))
            {
                OceanyaMessageBox.Show(
                    "Please provide a server name.",
                    "Invalid Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string endpoint = endpointInput.Trim();
            if (!IsValidServerEndpoint(endpoint))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use ws:// or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ServerEndpointOption? existingOption = FindServerOption(endpoint);
            if (existingOption != null)
            {
                SelectServerEndpoint(existingOption.Endpoint);
                return;
            }

            ServerEndpointOption newOption = ServerEndpointOption.Entry(serverName, endpoint, isPreset: false);
            customServerOptions.Add(newOption);
            RefreshServerEndpointComboBox(endpoint);
        }

        private void EditServerButton_Click(object sender, RoutedEventArgs e)
        {
            ServerEndpointOption? selectedOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            if (selectedOption == null || selectedOption.IsPreset || selectedOption.IsHeader)
            {
                return;
            }

            if (!AddServerDialog.ShowDialog(
                    this,
                    out string serverName,
                    out string endpointInput,
                    windowTitle: "Edit Custom Server",
                    actionText: "SAVE",
                    defaultName: selectedOption.Name,
                    defaultEndpoint: selectedOption.Endpoint))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(serverName))
            {
                OceanyaMessageBox.Show(
                    "Please provide a server name.",
                    "Invalid Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string endpoint = endpointInput.Trim();
            if (!IsValidServerEndpoint(endpoint))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use ws:// or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ServerEndpointOption? conflictOption = FindServerOption(endpoint);
            if (conflictOption != null && !string.Equals(conflictOption.Endpoint, selectedOption.Endpoint, StringComparison.OrdinalIgnoreCase))
            {
                OceanyaMessageBox.Show(
                    "That endpoint is already present in the list.",
                    "Duplicate Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                SelectServerEndpoint(conflictOption.Endpoint);
                return;
            }

            int index = customServerOptions.FindIndex(option =>
                string.Equals(option.Endpoint, selectedOption.Endpoint, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return;
            }

            customServerOptions[index] = ServerEndpointOption.Entry(serverName.Trim(), endpoint, isPreset: false);
            RefreshServerEndpointComboBox(endpoint);
        }

        private void RemoveServerButton_Click(object sender, RoutedEventArgs e)
        {
            ServerEndpointOption? selectedOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            if (selectedOption == null || selectedOption.IsPreset)
            {
                return;
            }

            customServerOptions.RemoveAll(option =>
                string.Equals(option.Endpoint, selectedOption.Endpoint, StringComparison.OrdinalIgnoreCase));

            RefreshServerEndpointComboBox(Globals.GetDefaultServerEndpoint());
        }

        private void ServerEndpointComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingSelection)
            {
                return;
            }

            ServerEndpointOption? selectedOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            if (selectedOption != null && selectedOption.IsHeader)
            {
                isUpdatingSelection = true;
                if (!string.IsNullOrWhiteSpace(lastSelectedEndpoint))
                {
                    SelectServerEndpoint(lastSelectedEndpoint);
                }
                else
                {
                    SelectFirstSelectableOption();
                }
                isUpdatingSelection = false;
                selectedOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            }

            if (selectedOption != null && selectedOption.IsSelectable)
            {
                lastSelectedEndpoint = selectedOption.Endpoint;
            }

            UpdateServerActionButtonsState();
        }

        private void LoadSavefile()
        {
            try
            {
                ConfigINIPathTextBox.Text = SaveFile.Data.ConfigIniPath;
                UseSingleClientCheckBox.IsChecked = SaveFile.Data.UseSingleInternalClient;

                InitializeServerEndpointOptions();
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Error loading configuration: " + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeServerEndpointOptions()
        {
            presetServerOptions.Clear();
            customServerOptions.Clear();

            HashSet<string> knownEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<Globals.Servers, string> server in Globals.IPs.OrderBy(entry => entry.Key))
            {
                if (string.IsNullOrWhiteSpace(server.Value))
                {
                    continue;
                }

                string endpoint = server.Value.Trim();
                if (!knownEndpoints.Add(endpoint))
                {
                    continue;
                }

                string presetName = GetPresetDisplayName(server.Key);
                presetServerOptions.Add(ServerEndpointOption.Entry(presetName, endpoint, isPreset: true));
            }

            List<CustomServerEntry> savedCustomEntries = SaveFile.Data.CustomServerEntries ?? new List<CustomServerEntry>();
            if (savedCustomEntries.Count == 0 && SaveFile.Data.CustomServerEndpoints != null)
            {
                int index = 1;
                foreach (string legacyEndpoint in SaveFile.Data.CustomServerEndpoints)
                {
                    savedCustomEntries.Add(new CustomServerEntry
                    {
                        Name = $"Custom {index}",
                        Endpoint = legacyEndpoint
                    });
                    index++;
                }
            }

            foreach (CustomServerEntry customEntry in savedCustomEntries)
            {
                if (string.IsNullOrWhiteSpace(customEntry?.Endpoint))
                {
                    continue;
                }

                string endpoint = customEntry.Endpoint.Trim();
                string name = string.IsNullOrWhiteSpace(customEntry.Name)
                    ? "Custom Server"
                    : customEntry.Name.Trim();

                if (!knownEndpoints.Add(endpoint))
                {
                    continue;
                }

                customServerOptions.Add(ServerEndpointOption.Entry(name, endpoint, isPreset: false));
            }

            string savedSelectedEndpoint = SaveFile.Data.SelectedServerEndpoint?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedSelectedEndpoint))
            {
                savedSelectedEndpoint = Globals.GetDefaultServerEndpoint();
            }

            RefreshServerEndpointComboBox(savedSelectedEndpoint);

            ServerEndpointOption? selectedOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            string activeEndpoint = selectedOption?.Endpoint ?? Globals.GetDefaultServerEndpoint();
            Globals.SetSelectedServerEndpoint(activeEndpoint);
            lastSelectedEndpoint = activeEndpoint;
        }

        private void RefreshServerEndpointComboBox(string selectedEndpoint)
        {
            List<ServerEndpointOption> options = new List<ServerEndpointOption>();
            options.Add(ServerEndpointOption.Header("=== Default Servers ==="));
            options.AddRange(presetServerOptions);
            options.Add(ServerEndpointOption.Header("=== Custom Servers ==="));
            options.AddRange(customServerOptions);

            isUpdatingSelection = true;
            ServerEndpointComboBox.ItemsSource = options;

            if (!string.IsNullOrWhiteSpace(selectedEndpoint))
            {
                SelectServerEndpoint(selectedEndpoint);
            }
            else
            {
                SelectFirstSelectableOption();
            }

            ServerEndpointOption? currentSelection = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            if (currentSelection == null || !currentSelection.IsSelectable)
            {
                SelectFirstSelectableOption();
                currentSelection = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            }
            isUpdatingSelection = false;

            if (currentSelection != null && currentSelection.IsSelectable)
            {
                lastSelectedEndpoint = currentSelection.Endpoint;
            }

            UpdateServerActionButtonsState();
        }

        private void SelectServerEndpoint(string endpoint)
        {
            ServerEndpointOption? optionToSelect = FindServerOption(endpoint);
            if (optionToSelect != null)
            {
                ServerEndpointComboBox.SelectedItem = optionToSelect;
            }
        }

        private void SelectFirstSelectableOption()
        {
            ServerEndpointOption? selectableOption = (ServerEndpointComboBox.ItemsSource as IEnumerable<ServerEndpointOption>)
                ?.FirstOrDefault(option => option.IsSelectable);
            if (selectableOption != null)
            {
                ServerEndpointComboBox.SelectedItem = selectableOption;
            }
        }

        private ServerEndpointOption? FindServerOption(string endpoint)
        {
            List<ServerEndpointOption> options = new List<ServerEndpointOption>();
            options.AddRange(presetServerOptions);
            options.AddRange(customServerOptions);

            return options.FirstOrDefault(option =>
                string.Equals(option.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateServerActionButtonsState()
        {
            ServerEndpointOption? selectedOption = ServerEndpointComboBox.SelectedItem as ServerEndpointOption;
            bool canEditOrRemove = selectedOption != null && selectedOption.IsSelectable && !selectedOption.IsPreset;
            EditServerButton.IsEnabled = canEditOrRemove;
            RemoveServerButton.IsEnabled = canEditOrRemove;
        }

        private static bool IsValidServerEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (uri == null)
            {
                return false;
            }

            bool validScheme = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);

            return validScheme && !string.IsNullOrWhiteSpace(uri.Host);
        }

        private static string GetPresetDisplayName(Globals.Servers server)
        {
            return server switch
            {
                Globals.Servers.ChillAndDices => "Chill and Dices",
                Globals.Servers.CaseCafe => "Case Cafe",
                Globals.Servers.Vanilla => "Vanilla",
                _ => server.ToString()
            };
        }

        private void SaveConfiguration(
            string configIniPath,
            bool useSingleInternalClient,
            string selectedServerEndpoint,
            List<CustomServerEntry> customServerEntries)
        {
            try
            {
                SaveFile.Data.ConfigIniPath = configIniPath;
                SaveFile.Data.UseSingleInternalClient = useSingleInternalClient;
                SaveFile.Data.SelectedServerEndpoint = selectedServerEndpoint;
                SaveFile.Data.CustomServerEntries = customServerEntries;
                SaveFile.Data.CustomServerEndpoints = customServerEntries
                    .Select(entry => entry.Endpoint)
                    .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
                    .ToList();
                SaveFile.Save();
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Error saving configuration: " + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // The FadeIn animation is triggered automatically by the EventTrigger in XAML
        }

        private bool _isClosing = false;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing)
            {
                return;
            }

            e.Cancel = true;
            _isClosing = true;

            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, _) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    _isClosing = true;
                    this.Close();
                });
            };
            fadeOut.Begin(this);
        }
    }
}
