using Microsoft.Win32;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.Startup;
using OceanyaClient.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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
            bool refreshRequested = RefreshInfoCheckBox.IsChecked == true;
            Window? startupLaunchOwner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            bool launchWaitFormShown = false;
            bool launchWaitFormClosed = true;
            string launchTitle = "Opening " + selectedFunctionality.DisplayName + "...";

            async Task EnsureLaunchWaitFormAsync(string subtitle)
            {
                if (startupLaunchOwner == null)
                {
                    return;
                }

                await WaitForm.ShowFormAsync(launchTitle, startupLaunchOwner);
                launchWaitFormShown = true;
                launchWaitFormClosed = false;
                WaitForm.SetSubtitle(subtitle);
            }

            async Task CloseLaunchWaitFormAsync()
            {
                if (!launchWaitFormShown || launchWaitFormClosed)
                {
                    return;
                }

                launchWaitFormClosed = true;
                await WaitForm.CloseFormAsync();
            }

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
                await EnsureLaunchWaitFormAsync("Checking startup requirements...");

                if (selectedFunctionality.RequiresServerEndpoint)
                {
                    ServerEndpointDefinition validatedServer = await ValidateSelectedServerForLaunchAsync(
                        selectedServer,
                        selectedServerName,
                        selectedServerEndpoint);
                    if (!validatedServer.IsSelectable)
                    {
                        await CloseLaunchWaitFormAsync();

                        OceanyaMessageBox.Show(
                            $"The selected server '{validatedServer.Name}' is not available.\n\n{ServerEndpointCatalog.GetNotSelectableReason(validatedServer)}",
                            "Server Unavailable",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    selectedServer = validatedServer;
                    selectedServerEndpoint = validatedServer.Endpoint.Trim();
                    selectedServerName = validatedServer.Name.Trim();
                    UpdateSelectedServerDisplay();
                }

                (string forcedRefreshReason, TargetedAssetRefreshPlan trackedChangePlan) preflightResult = await Task.Run(() =>
                {
                    Globals.UpdateConfigINI(configIniPath);
                    if (selectedFunctionality.RequiresServerEndpoint)
                    {
                        Globals.SetSelectedServerEndpoint(selectedServerEndpoint);
                    }

                    string computedForcedRefreshReason = string.Empty;
                    TargetedAssetRefreshPlan computedTrackedChangePlan = new TargetedAssetRefreshPlan();
                    if (!refreshRequested)
                    {
                        computedForcedRefreshReason =
                            ClientAssetRefreshService.GetRefreshRequirementReasonForCurrentEnvironment();
                    }

                    if (string.IsNullOrWhiteSpace(computedForcedRefreshReason)
                        && !refreshRequested
                        && string.Equals(
                            selectedFunctionality.Id,
                            StartupFunctionalityIds.CharacterDatabaseViewer,
                            StringComparison.OrdinalIgnoreCase)
                        && CharacterFolder.FullList.Count == 0)
                    {
                        computedForcedRefreshReason =
                            "The character database viewer does not currently have a loaded character/background index.";
                    }

                    if (!refreshRequested && string.IsNullOrWhiteSpace(computedForcedRefreshReason))
                    {
                        computedTrackedChangePlan =
                            ClientAssetRefreshService.GetTrackedChangePlanForCurrentEnvironment();
                    }

                    return (computedForcedRefreshReason, computedTrackedChangePlan);
                });

                string forcedRefreshReason = preflightResult.forcedRefreshReason;
                TargetedAssetRefreshPlan trackedChangePlan = preflightResult.trackedChangePlan;

                SaveConfiguration(
                    configIniPath,
                    selectedFunctionality.Id,
                    UseSingleClientCheckBox.IsChecked != false,
                    selectedServerEndpoint,
                    selectedServerName);

                bool shouldRefreshAssets = refreshRequested || !string.IsNullOrWhiteSpace(forcedRefreshReason);
                bool shouldRunTargetedRefresh = !refreshRequested
                    && string.IsNullOrWhiteSpace(forcedRefreshReason)
                    && trackedChangePlan.HasAnyWork;

                if (!refreshRequested && !string.IsNullOrWhiteSpace(forcedRefreshReason))
                {
                    await CloseLaunchWaitFormAsync();

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
                    await CloseLaunchWaitFormAsync();

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

                    await CloseLaunchWaitFormAsync();
                    await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(refreshOwner);
                    RefreshInfoCheckBox.IsChecked = false;
                }
                else if (shouldRunTargetedRefresh)
                {
                    Window? refreshOwner = HostWindow ?? Application.Current?.MainWindow;
                    if (refreshOwner == null)
                    {
                        return;
                    }

                    await CloseLaunchWaitFormAsync();
                    await ClientAssetRefreshService.RefreshTargetedAssetsAsync(refreshOwner, trackedChangePlan);
                    RefreshInfoCheckBox.IsChecked = false;
                }

                await EnsureLaunchWaitFormAsync("Creating window...");

                // Yield once so the wait form can paint before the heavy startup window is constructed.
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                await CloseLaunchWaitFormAsync();

                OceanyaMessageBox.Show(
                    "Error updating base folders: " + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            async Task HandleStartupFunctionalityReadyAsync()
            {
                await CloseLaunchWaitFormAsync();
                TryPlayStartupFunctionalityJingle();
            }

            async Task HandleStartupFunctionalityClosedAsync()
            {
                await CloseLaunchWaitFormAsync();
                ReopenConfigurationWindow();
            }

            void HandleStartupFunctionalityReady()
            {
                _ = HandleStartupFunctionalityReadyAsync();
            }

            void HandleStartupFunctionalityClosed()
            {
                _ = HandleStartupFunctionalityClosedAsync();
            }

            try
            {
                Window startupWindow = StartupWindowLauncher.CreateStartupWindow(
                    selectedFunctionality.Id,
                    onFunctionalityReady: HandleStartupFunctionalityReady,
                    onFunctionalityClosed: HandleStartupFunctionalityClosed,
                    useSharedStartupWaitForm: true);

                startupWindow.Show();
                if (launchWaitFormShown)
                {
                    WaitForm.SetSubtitle("Loading startup tasks...");
                }

                Hide();
            }
            catch (Exception ex)
            {
                await CloseLaunchWaitFormAsync();

                OceanyaMessageBox.Show(
                    "The selected functionality could not be opened:\n" + ex.Message,
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static async Task<ServerEndpointDefinition> ValidateSelectedServerForLaunchAsync(
            ServerEndpointDefinition? selectedServer,
            string selectedServerName,
            string selectedServerEndpoint)
        {
            ServerEndpointDefinition validationTarget = selectedServer != null
                ? new ServerEndpointDefinition
                {
                    Name = selectedServer.Name,
                    Endpoint = selectedServer.Endpoint,
                    Description = selectedServer.Description,
                    Source = selectedServer.Source,
                    IsLegacy = selectedServer.IsLegacy,
                    FavoriteStoreIndex = selectedServer.FavoriteStoreIndex,
                    IsAoClientCompatible = selectedServer.IsAoClientCompatible,
                    IsOnline = selectedServer.IsOnline,
                    OnlinePlayers = selectedServer.OnlinePlayers,
                    MaxPlayers = selectedServer.MaxPlayers
                }
                : new ServerEndpointDefinition
                {
                    Name = string.IsNullOrWhiteSpace(selectedServerName) ? selectedServerEndpoint : selectedServerName,
                    Endpoint = selectedServerEndpoint,
                    Description = "Previously selected endpoint.",
                    Source = ServerEndpointSource.Defaults,
                    IsLegacy = ServerEndpointCatalog.IsLegacyEndpoint(selectedServerEndpoint)
                };

            await ServerEndpointCatalog.PopulateSupplementalStatusAsync(
                new[] { validationTarget },
                CancellationToken.None);

            return validationTarget;
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
                    IsLegacy = ServerEndpointCatalog.IsLegacyEndpoint(savedEndpoint)
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

            bool validScheme = string.Equals(uri.Scheme, "tcp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);

            return validScheme
                && !string.IsNullOrWhiteSpace(uri.Host)
                && uri.Port > 0;
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

        private static void TryPlayStartupFunctionalityJingle()
        {
            try
            {
                AudioPlayer.PlayEmbeddedSound("Resources/ApertureScienceJingleHD.mp3", 0.5f);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to play the startup functionality jingle.", ex);
            }
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
