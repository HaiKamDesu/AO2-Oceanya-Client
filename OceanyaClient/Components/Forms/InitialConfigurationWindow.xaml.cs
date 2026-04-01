using Microsoft.Win32;
using AOBot_Testing.Structures;
using OceanyaClient.Features.Startup;
using OceanyaClient.Utilities;
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
    public partial class InitialConfigurationWindow : OceanyaWindowContentControl
    {
        private const double MultiClientWindowHeight = 358;
        private const double CharacterViewerWindowHeight = 286;

        private ServerEndpointDefinition? selectedServer;
        private bool ignoreStartupFunctionalitySelectionChanged;

        public InitialConfigurationWindow()
        {
            InitializeComponent();
            Title = "Initial Configuration";
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Closing += Window_Closing;
            LoadSavefile();
        }

        /// <inheritdoc/>
        public override string HeaderText => "INITIAL CONFIGURATION";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

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
            StartupFunctionalityOption selectedFunctionality = GetSelectedStartupFunctionality();
            string selectedServerEndpoint = selectedServer?.Endpoint?.Trim() ?? string.Empty;
            string selectedServerName = selectedServer?.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(configIniPath))
            {
                OceanyaMessageBox.Show(
                    "Please provide the config.ini path.",
                    "Invalid Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (selectedFunctionality.RequiresServerEndpoint && string.IsNullOrWhiteSpace(selectedServerEndpoint))
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
                if (selectedFunctionality.RequiresServerEndpoint)
                {
                    Globals.SetSelectedServerEndpoint(selectedServerEndpoint);
                }
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
                selectedFunctionality.Id,
                UseSingleClientCheckBox.IsChecked != false,
                selectedServerEndpoint,
                selectedServerName);

            bool refreshRequested = RefreshInfoCheckBox.IsChecked == true;
            string forcedRefreshReason = string.Empty;
            TargetedAssetRefreshPlan trackedChangePlan = new TargetedAssetRefreshPlan();
            if (!refreshRequested)
            {
                forcedRefreshReason = ClientAssetRefreshService.GetRefreshRequirementReasonForCurrentEnvironment();
            }

            if (string.IsNullOrWhiteSpace(forcedRefreshReason)
                && !refreshRequested
                && string.Equals(
                    selectedFunctionality.Id,
                    StartupFunctionalityIds.CharacterDatabaseViewer,
                    StringComparison.OrdinalIgnoreCase)
                && CharacterFolder.FullList.Count == 0)
            {
                forcedRefreshReason =
                    "The character database viewer does not currently have a loaded character/background index.";
            }

            if (!refreshRequested && string.IsNullOrWhiteSpace(forcedRefreshReason))
            {
                trackedChangePlan = ClientAssetRefreshService.GetTrackedChangePlanForCurrentEnvironment();
            }

            bool shouldRefreshAssets = refreshRequested || !string.IsNullOrWhiteSpace(forcedRefreshReason);
            bool shouldRunTargetedRefresh = !refreshRequested
                && string.IsNullOrWhiteSpace(forcedRefreshReason)
                && trackedChangePlan.HasAnyWork;

            if (!refreshRequested && !string.IsNullOrWhiteSpace(forcedRefreshReason))
            {
                MessageBoxResult refreshDecision = OceanyaMessageBox.Show(
                    BuildForcedRefreshPrompt(forcedRefreshReason),
                    "Refresh Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (refreshDecision != MessageBoxResult.Yes)
                {
                    return;
                }

                shouldRefreshAssets = true;
            }
            else if (shouldRunTargetedRefresh)
            {
                MessageBoxResult refreshDecision = OceanyaMessageBox.Show(
                    BuildTrackedRefreshPrompt(),
                    "Refresh Changed Assets",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                shouldRunTargetedRefresh = refreshDecision == MessageBoxResult.Yes;
            }

            if (shouldRefreshAssets)
            {
                Window? refreshOwner = HostWindow ?? Application.Current?.MainWindow;
                if (refreshOwner == null)
                {
                    return;
                }

                await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(refreshOwner);
            }
            else if (shouldRunTargetedRefresh)
            {
                Window? refreshOwner = HostWindow ?? Application.Current?.MainWindow;
                if (refreshOwner == null)
                {
                    return;
                }

                await ClientAssetRefreshService.RefreshTargetedAssetsAsync(refreshOwner, trackedChangePlan);
            }

            Window startupWindow = StartupWindowLauncher.CreateStartupWindow(
                selectedFunctionality.Id,
                onFunctionalityReady: PlayStartupFunctionalityJingle,
                onFunctionalityClosed: ReopenConfigurationWindow);
            startupWindow.Show();
            Hide();
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

            string currentEndpoint = selectedServer?.Endpoint
                ?? SaveFile.Data.SelectedServerEndpoint
                ?? Globals.GetDefaultServerEndpoint();

            ServerSelectionDialog dialog = new ServerSelectionDialog(configIniPath, currentEndpoint)
            {
                Owner = HostWindow
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
                CleanupLegacyCustomServerData();
                BindStartupFunctionalitySelection();

                selectedServer = ResolveInitialSelectedServer();
                if (selectedServer != null)
                {
                    Globals.SetSelectedServerEndpoint(selectedServer.Endpoint);
                }

                UpdateSelectedServerDisplay();
                ApplySelectedFunctionalityUi(animate: false);
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

        private static void CleanupLegacyCustomServerData()
        {
            bool hadLegacyCustomServers = (SaveFile.Data.CustomServerEntries?.Count ?? 0) > 0
                || (SaveFile.Data.CustomServerEndpoints?.Count ?? 0) > 0;
            if (!hadLegacyCustomServers)
            {
                return;
            }

            SaveFile.Data.CustomServerEntries = new List<CustomServerEntry>();
            SaveFile.Data.CustomServerEndpoints = new List<string>();
            SaveFile.Save();
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
            string startupFunctionalityId,
            bool useSingleInternalClient,
            string selectedServerEndpoint,
            string selectedServerName)
        {
            try
            {
                SaveFile.Data.ConfigIniPath = configIniPath;
                SaveFile.Data.StartupFunctionalityId = startupFunctionalityId;
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

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (current is FrameworkElement element)
                    {
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AdvancedFeatureFlaggingText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AdvancedFeatureFlagsWindow window = new AdvancedFeatureFlagsWindow
            {
                Owner = HostWindow
            };
            window.ShowDialog();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySelectedFunctionalityUi(animate: false);
        }

        private void BindStartupFunctionalitySelection()
        {
            ignoreStartupFunctionalitySelectionChanged = true;
            try
            {
                StartupFunctionalityComboBox.ItemsSource = StartupFunctionalityCatalog.Options;
                StartupFunctionalityComboBox.SelectedValue =
                    StartupFunctionalityCatalog.GetByIdOrDefault(SaveFile.Data.StartupFunctionalityId).Id;
            }
            finally
            {
                ignoreStartupFunctionalitySelectionChanged = false;
            }
        }

        private StartupFunctionalityOption GetSelectedStartupFunctionality()
        {
            object selectedValue = StartupFunctionalityComboBox.SelectedValue;
            string selectedId = selectedValue?.ToString() ?? string.Empty;
            return StartupFunctionalityCatalog.GetByIdOrDefault(selectedId);
        }

        private void StartupFunctionalityComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ignoreStartupFunctionalitySelectionChanged)
            {
                return;
            }

            ApplySelectedFunctionalityUi(animate: true);
        }

        private void ApplySelectedFunctionalityUi(bool animate)
        {
            StartupFunctionalityOption selectedFunctionality = GetSelectedStartupFunctionality();
            bool showMultiClientSettings = selectedFunctionality.RequiresServerEndpoint;
            MultiClientServerSettingsPanel.Visibility = showMultiClientSettings ? Visibility.Visible : Visibility.Collapsed;
            MultiClientSingleClientPanel.Visibility = showMultiClientSettings ? Visibility.Visible : Visibility.Collapsed;

            double targetHeight = showMultiClientSettings ? MultiClientWindowHeight : CharacterViewerWindowHeight;
            ResizeWindow(targetHeight, animate);
        }

        private void ResizeWindow(double targetHeight, bool animate)
        {
            BeginAnimation(HeightProperty, null);
            if (!animate)
            {
                Height = targetHeight;
                return;
            }

            DoubleAnimation animation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(170),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(HeightProperty, animation);
        }

        private static void PlayStartupFunctionalityJingle()
        {
            AudioPlayer.PlayEmbeddedSound("Resources/ApertureScienceJingleHD.mp3", 0.5f);
        }

        private static string BuildForcedRefreshPrompt(string forcedRefreshReason)
        {
            string trimmedReason = forcedRefreshReason?.Trim().TrimEnd('.') ?? string.Empty;
            if (trimmedReason.Contains("Oceanya version changed", StringComparison.OrdinalIgnoreCase))
            {
                return "The Oceanya version changed, and it's necessary to refresh the assets. This may take a long time. Do you want to continue?";
            }

            string normalizedReason = string.IsNullOrWhiteSpace(trimmedReason)
                ? "something in the asset environment changed"
                : char.ToLowerInvariant(trimmedReason[0]) + trimmedReason[1..];
            return "A full asset refresh is required because " + normalizedReason
                + ". This may take a long time. Do you want to continue?";
        }

        private static string BuildTrackedRefreshPrompt()
        {
            return "Asset files changed since the last refresh. Oceanya can refresh only the affected items. Do you want to continue?";
        }

        private void ReopenConfigurationWindow()
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                Activate();
                Focus();
            });
        }

        private bool isClosing;

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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
