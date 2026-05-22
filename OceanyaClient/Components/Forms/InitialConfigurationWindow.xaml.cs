using Microsoft.Win32;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.Updates;
using OceanyaClient.Features.Startup;
using OceanyaClient.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private const double MultiClientWindowHeight = 384;
        private const double CharacterViewerWindowHeight = 312;
        private static Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult>? testMessageBoxOverride = null;
        private static Func<Window, Task>? testRefreshCharactersAndBackgroundsAsyncOverride = null;
        private static Func<Window, TargetedAssetRefreshPlan, Task>? testRefreshTargetedAssetsAsyncOverride = null;

        private ServerEndpointDefinition? selectedServer;
        private bool ignoreStartupFunctionalitySelectionChanged;
        private bool autoLaunchQueued;
        private bool updateCheckStarted;
        private readonly UpdateCheckService updateCheckService = new UpdateCheckService();
        private UpdateRelease? availableUpdate;

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

        private void SkipLoadingScreenCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            SaveFile.Data.SkipLoadingScreen = SkipLoadingScreenCheckBox.IsChecked == true;
            SaveFile.Save();
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
            await ExecuteOkButtonClickAsync();
        }

        private async Task ExecuteOkButtonClickAsync()
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
                if (startupLaunchOwner == null || OceanyaTestMode.Current.DisableWaitForms)
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
                StartupTimingLogger.MarkLaunchClick();
                await EnsureLaunchWaitFormAsync("Checking startup requirements...");

                if (selectedFunctionality.RequiresServerEndpoint)
                {
                    if (!OceanyaTestMode.Current.SkipServerValidation
                        && !ShouldDeferLaunchServerProbe(selectedFunctionality, selectedServerEndpoint))
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
                }

                string forcedRefreshReason = await Task.Run(() =>
                {
                    WaitForm.SetSubtitle("Loading configuration...");
                    Globals.UpdateConfigINI(configIniPath);
                    if (selectedFunctionality.RequiresServerEndpoint)
                    {
                        Globals.SetSelectedServerEndpoint(selectedServerEndpoint);
                    }

                    if (refreshRequested)
                    {
                        return string.Empty;
                    }

                    WaitForm.SetSubtitle("Verifying asset cache...");
                    string computedForcedRefreshReason =
                        ClientAssetRefreshService.GetRefreshRequirementReasonForCurrentEnvironment();

                    if (string.IsNullOrWhiteSpace(computedForcedRefreshReason)
                        && string.Equals(
                            selectedFunctionality.Id,
                            StartupFunctionalityIds.CharacterDatabaseViewer,
                            StringComparison.OrdinalIgnoreCase)
                        && CharacterFolder.FullList.Count == 0)
                    {
                        return "The character database viewer does not currently have a loaded character/background index.";
                    }

                    StartupTimingLogger.Log("preflight_check_done",
                        string.IsNullOrWhiteSpace(computedForcedRefreshReason) ? "ok" : "forced_refresh");
                    return computedForcedRefreshReason;
                });

                bool shouldRefreshAssets = refreshRequested || !string.IsNullOrWhiteSpace(forcedRefreshReason);

                if (OceanyaTestMode.Current.SkipAssetRefreshPrompts)
                {
                    shouldRefreshAssets = false;
                    forcedRefreshReason = string.Empty;
                }

                if (!refreshRequested && !string.IsNullOrWhiteSpace(forcedRefreshReason))
                {
                    await CloseLaunchWaitFormAsync();

                    MessageBoxResult refreshDecision = ShowMessageBox(
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

                SaveConfiguration(
                    configIniPath,
                    selectedFunctionality.Id,
                    UseSingleClientCheckBox.IsChecked != false,
                    selectedServerEndpoint,
                    selectedServerName);

                if (shouldRefreshAssets)
                {
                    Window? refreshOwner = HostWindow ?? Application.Current?.MainWindow;
                    if (refreshOwner == null)
                    {
                        return;
                    }

                    await CloseLaunchWaitFormAsync();
                    await RefreshCharactersAndBackgroundsAsync(refreshOwner);
                    RefreshInfoCheckBox.IsChecked = false;
                }

                await EnsureLaunchWaitFormAsync("Creating window...");
                StartupTimingLogger.Log("main_window_creating");

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
                StartupTimingLogger.Log("finished_loading");
                StartupTimingLogger.WriteLog();
                await CloseLaunchWaitFormAsync();
                TryPlayStartupFunctionalityJingle();
                // Detect and refresh only changed assets in the background — this scan is
                // deferred off the launch critical path so startup feels instant.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        StartupTimingLogger.Log("background_tracked_check_begin");
                        TargetedAssetRefreshPlan plan =
                            ClientAssetRefreshService.GetTrackedChangePlanForCurrentEnvironment();
                        StartupTimingLogger.Log("background_tracked_check_end",
                            plan.HasAnyWork ? "changes_found" : "no_changes");
                        StartupTimingLogger.WriteLog();
                        if (plan.HasAnyWork)
                        {
                            await ClientAssetRefreshService.RefreshTargetedAssetsInBackgroundAsync(plan);
                        }
                    }
                    catch (Exception ex)
                    {
                        CustomConsole.Warning("Background asset change check failed.", ex);
                    }
                });
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
                    PingStatus = selectedServer.PingStatus,
                    OnlinePlayers = selectedServer.OnlinePlayers,
                    MaxPlayers = selectedServer.MaxPlayers,
                    ListIndex = selectedServer.ListIndex
                }
                : new ServerEndpointDefinition
                {
                    Name = string.IsNullOrWhiteSpace(selectedServerName) ? selectedServerEndpoint : selectedServerName,
                    Endpoint = selectedServerEndpoint,
                    Description = "Previously selected endpoint.",
                    Source = ServerEndpointSource.Defaults,
                    IsLegacy = ServerEndpointCatalog.IsLegacyEndpoint(selectedServerEndpoint),
                    ListIndex = 0
                };

            if (!validationTarget.SupportsDirectConnection)
            {
                validationTarget.PingStatus = ServerPingStatus.Offline;
                validationTarget.OnlinePlayers = null;
                validationTarget.MaxPlayers = null;
                return validationTarget;
            }

            // Skip probe if already confirmed reachable — avoids triggering tsuCC's 5-second
            // reconnect cooldown immediately before the actual client connection.
            if (validationTarget.PingStatus == ServerPingStatus.Online ||
                validationTarget.PingStatus == ServerPingStatus.IncompatibleClient)
            {
                return validationTarget;
            }

            (bool success, int? players, int? maxPlayers, bool incompatibleClient) =
                await ServerEndpointCatalog.ProbeEndpointAsync(validationTarget.Endpoint, CancellationToken.None);

            validationTarget.OnlinePlayers = success ? players : null;
            validationTarget.MaxPlayers = success ? maxPlayers : null;
            validationTarget.PingStatus = incompatibleClient
                ? ServerPingStatus.IncompatibleClient
                : success
                    ? ServerPingStatus.Online
                    : ServerPingStatus.Offline;

            return validationTarget;
        }

        private static bool ShouldDeferLaunchServerProbe(
            StartupFunctionalityOption selectedFunctionality,
            string selectedServerEndpoint)
        {
            if (!selectedFunctionality.RequiresServerEndpoint)
            {
                return false;
            }

            if (!IsValidServerEndpoint(selectedServerEndpoint))
            {
                return false;
            }

            return string.Equals(
                    selectedFunctionality.Id,
                    StartupFunctionalityIds.GmMultiClient,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    selectedFunctionality.Id,
                    StartupFunctionalityIds.Ao2AiBot,
                    StringComparison.OrdinalIgnoreCase);
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
                string configIniPath = ResolveMovedConfigIniPathForCurrentInstall(
                    SaveFile.Data.ConfigIniPath,
                    AppContext.BaseDirectory,
                    Environment.CurrentDirectory);
                if (!string.Equals(configIniPath, SaveFile.Data.ConfigIniPath, StringComparison.OrdinalIgnoreCase))
                {
                    SaveFile.Data.ConfigIniPath = configIniPath;
                    SaveFile.Save();
                }

                ConfigINIPathTextBox.Text = configIniPath;
                UseSingleClientCheckBox.IsChecked = SaveFile.Data.UseSingleInternalClient;
                SkipLoadingScreenCheckBox.IsChecked = SaveFile.Data.SkipLoadingScreen;
                CleanupLegacyCustomServerData();
                BindStartupFunctionalitySelection();
                ApplyTestStartupOverrides();

                selectedServer = ResolveInitialSelectedServer();
                selectedServer = ApplyTestSelectedServerOverride(selectedServer);
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

        private static string ResolveMovedConfigIniPathForCurrentInstall(
            string? savedConfigIniPath,
            string? appBaseDirectory,
            string? currentDirectory)
        {
            string savedPath = savedConfigIniPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savedPath) || File.Exists(savedPath))
            {
                return savedPath;
            }

            if (!string.Equals(Path.GetFileName(savedPath), "config.ini", StringComparison.OrdinalIgnoreCase))
            {
                return savedPath;
            }

            string parentName = Path.GetFileName(Path.GetDirectoryName(savedPath) ?? string.Empty);
            List<string> candidateDirectories = new List<string>();
            AddCandidateDirectory(candidateDirectories, appBaseDirectory);
            AddCandidateDirectory(candidateDirectories, currentDirectory);

            foreach (string candidateDirectory in candidateDirectories)
            {
                string directCandidate = Path.Combine(candidateDirectory, "config.ini");
                if (File.Exists(directCandidate))
                {
                    return directCandidate;
                }

                if (!string.IsNullOrWhiteSpace(parentName))
                {
                    string sameParentNameCandidate = Path.Combine(candidateDirectory, parentName, "config.ini");
                    if (File.Exists(sameParentNameCandidate))
                    {
                        return sameParentNameCandidate;
                    }
                }

                string baseCandidate = Path.Combine(candidateDirectory, "base", "config.ini");
                if (File.Exists(baseCandidate))
                {
                    return baseCandidate;
                }
            }

            return savedPath;
        }

        private static void AddCandidateDirectory(List<string> candidateDirectories, string? path)
        {
            string candidate = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            try
            {
                candidate = Path.GetFullPath(candidate);
            }
            catch
            {
                return;
            }

            if (!candidateDirectories.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                candidateDirectories.Add(candidate);
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
                SaveFile.Data.SkipLoadingScreen = SkipLoadingScreenCheckBox.IsChecked == true;
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
            MarkAutomationReady();
            StartUpdateCheckOnce();

            if (!autoLaunchQueued && OceanyaTestMode.Current.IsEnabled && OceanyaTestMode.Current.AutoLaunchStartupFunctionality)
            {
                autoLaunchQueued = true;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => OkButton_Click(OkButton, new RoutedEventArgs(Button.ClickEvent, OkButton))));
            }
        }

        private void StartUpdateCheckOnce()
        {
            if (updateCheckStarted || OceanyaTestMode.Current.IsEnabled)
            {
                return;
            }

            updateCheckStarted = true;
            _ = CheckForUpdatesAfterStartupAsync();
        }

        private async Task CheckForUpdatesAfterStartupAsync()
        {
            UpdateRelease? release = await updateCheckService.CheckForUpdateAsync(interactive: false, CancellationToken.None);
            if (release == null)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                availableUpdate = release;
                UpdateAvailableLinkTextBlock.Text = BuildUpdateLinkText(release);
                UpdateAvailableLinkTextBlock.Visibility = Visibility.Visible;
            });

            if (!updateCheckService.IsSkipped(release))
            {
                await Dispatcher.InvokeAsync(() => ShowUpdatePrompt(release, explicitUserAction: false));
            }
        }

        private void UpdateAvailableLinkText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (availableUpdate != null)
            {
                ShowUpdatePrompt(availableUpdate, explicitUserAction: true);
                return;
            }

            _ = CheckForUpdatesFromLinkAsync();
        }

        private async Task CheckForUpdatesFromLinkAsync()
        {
            try
            {
                UpdateRelease? release = await updateCheckService.CheckForUpdateAsync(interactive: true, CancellationToken.None);
                if (release == null)
                {
                    OceanyaMessageBox.Show(
                        HostWindow,
                        $"No newer {updateCheckService.Environment.ChannelName} update is available.",
                        "Update Check",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                availableUpdate = release;
                UpdateAvailableLinkTextBlock.Text = BuildUpdateLinkText(release);
                UpdateAvailableLinkTextBlock.Visibility = Visibility.Visible;
                ShowUpdatePrompt(release, explicitUserAction: true);
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Could not check for updates:\n" + ex.Message,
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ShowUpdatePrompt(UpdateRelease release, bool explicitUserAction)
        {
            UpdateAvailableDialogResult result = UpdateAvailableWindow.Show(HostWindow, release);
            if (result == UpdateAvailableDialogResult.Skip)
            {
                updateCheckService.Skip(release);
                return;
            }

            if (result == UpdateAvailableDialogResult.Update)
            {
                _ = StartUpdateAsync(release, explicitUserAction);
            }
        }

        private static string BuildUpdateLinkText(UpdateRelease release)
        {
            bool isTest = string.Equals(release.Manifest.Channel, "test", StringComparison.OrdinalIgnoreCase);
            return isTest
                ? "Test update to " + release.Manifest.Tag
                : "Update to " + release.Manifest.Tag;
        }

        private async Task StartUpdateAsync(UpdateRelease release, bool explicitUserAction)
        {
            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            try
            {
                if (owner != null && !OceanyaTestMode.Current.DisableWaitForms)
                {
                    await WaitForm.ShowFormAsync("Downloading update...", owner);
                    WaitForm.SetSubtitle("Starting download...");
                }

                Progress<double> progress = new Progress<double>(value =>
                {
                    int percent = (int)Math.Round(Math.Clamp(value, 0d, 1d) * 100d);
                    WaitForm.SetSubtitle($"Downloading update... {percent}%");
                });
                UpdateStagingResult staging = await updateCheckService.StageUpdateAsync(
                    release,
                    progress,
                    CancellationToken.None);

                if (owner != null && !OceanyaTestMode.Current.DisableWaitForms)
                {
                    WaitForm.SetSubtitle("Applying update... Oceanya will close and reopen automatically.");
                }

                updateCheckService.LaunchUpdaterAndExit(release, staging);
            }
            catch (UnauthorizedAccessException ex)
            {
                if (owner != null && !OceanyaTestMode.Current.DisableWaitForms)
                {
                    await WaitForm.CloseFormAsync();
                }

                MessageBoxResult openDecision = OceanyaMessageBox.Show(
                    owner,
                    ex.Message + "\n\nOpen the GitHub release page for a manual update?",
                    "Manual Update Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (openDecision == MessageBoxResult.Yes)
                {
                    OpenReleasePage(release.HtmlUrl);
                }
            }
            catch (Exception ex)
            {
                if (owner != null && !OceanyaTestMode.Current.DisableWaitForms)
                {
                    await WaitForm.CloseFormAsync();
                }

                if (explicitUserAction)
                {
                    OceanyaMessageBox.Show(
                        owner,
                        "Update failed before Oceanya could hand off to the updater:\n"
                        + ex.Message
                        + "\n\nOceanya is still open. Check the updater logs and handoff files under the Updates folder.",
                        "Update Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private static void OpenReleasePage(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }

        private void ApplyTestStartupOverrides()
        {
            OceanyaTestModeOptions options = OceanyaTestMode.Current;
            if (!options.IsEnabled)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.ConfigIniPath))
            {
                ConfigINIPathTextBox.Text = options.ConfigIniPath.Trim();
            }

            if (!string.IsNullOrWhiteSpace(options.StartupFunctionalityId))
            {
                StartupFunctionalityComboBox.SelectedValue =
                    StartupFunctionalityCatalog.GetByIdOrDefault(options.StartupFunctionalityId).Id;
            }
        }

        private ServerEndpointDefinition? ApplyTestSelectedServerOverride(ServerEndpointDefinition? resolvedServer)
        {
            OceanyaTestModeOptions options = OceanyaTestMode.Current;
            if (!options.IsEnabled || string.IsNullOrWhiteSpace(options.ServerEndpoint))
            {
                return resolvedServer;
            }

            string endpoint = options.ServerEndpoint.Trim();
            return new ServerEndpointDefinition
            {
                Name = endpoint,
                Endpoint = endpoint,
                Description = "Test mode endpoint override.",
                Source = ServerEndpointSource.Defaults,
                IsLegacy = ServerEndpointCatalog.IsLegacyEndpoint(endpoint)
            };
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
                AudioPlayer.PlayEmbeddedSound("Resources/ApertureScienceJingleHD.mp3", AudioSettings.ScaleEmbeddedSfxVolume(0.5f));
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to play the startup functionality jingle.", ex);
            }
        }

        private static string BuildForcedRefreshPrompt(string forcedRefreshReason)
        {
            string trimmedReason = forcedRefreshReason?.Trim().TrimEnd('.') ?? string.Empty;
            string normalizedReason = string.IsNullOrWhiteSpace(trimmedReason)
                ? "something in the asset environment changed"
                : char.ToLowerInvariant(trimmedReason[0]) + trimmedReason[1..];
            return "A full asset refresh is required because " + normalizedReason
                + ". This may take a long time. Do you want to continue?";
        }

        private static MessageBoxResult ShowMessageBox(
            string messageBoxText,
            string caption,
            MessageBoxButton buttons,
            MessageBoxImage icon)
        {
            Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult>? overrideHandler =
                testMessageBoxOverride;
            if (overrideHandler != null)
            {
                return overrideHandler(messageBoxText, caption, buttons, icon);
            }

            return OceanyaMessageBox.Show(messageBoxText, caption, buttons, icon);
        }

        private static Task RefreshCharactersAndBackgroundsAsync(Window owner)
        {
            Func<Window, Task>? overrideHandler = testRefreshCharactersAndBackgroundsAsyncOverride;
            if (overrideHandler != null)
            {
                return overrideHandler(owner);
            }

            return ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(owner);
        }

        private static Task RefreshTargetedAssetsAsync(Window owner, TargetedAssetRefreshPlan plan)
        {
            Func<Window, TargetedAssetRefreshPlan, Task>? overrideHandler = testRefreshTargetedAssetsAsyncOverride;
            if (overrideHandler != null)
            {
                return overrideHandler(owner, plan);
            }

            return ClientAssetRefreshService.RefreshTargetedAssetsAsync(owner, plan);
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
