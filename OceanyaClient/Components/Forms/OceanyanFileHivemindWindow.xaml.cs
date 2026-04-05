using Microsoft.Win32;
using OceanyaClient.Features.FileHivemind;
using OceanyaClient.Features.GoogleDriveSync;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OceanyaClient
{
    public partial class OceanyanFileHivemindWindow : OceanyaWindowContentControl, Features.Startup.IStartupFunctionalityWindow
    {
        private sealed class ConnectionListEntry
        {
            public ConnectionListEntry(
                FileHivemindConnectionProfile connection,
                Brush primaryForeground,
                Brush secondaryForeground,
                string statusText)
            {
                Connection = connection;
                PrimaryForeground = primaryForeground;
                SecondaryForeground = secondaryForeground;
                StatusText = statusText;
            }

            public FileHivemindConnectionProfile Connection { get; }

            public Brush PrimaryForeground { get; }

            public Brush SecondaryForeground { get; }

            public string StatusText { get; }
        }

        private enum StatusLogLevel
        {
            Info,
            Action,
            Success,
            Warning,
            Error
        }

        private readonly GoogleDriveSyncService syncService;
        private readonly GoogleDriveConnectionRuntimeStateStore runtimeStateStore;
        private readonly GoogleDriveSecureClientCredentialStore credentialStore;
        private readonly FileHivemindBackgroundAgentLauncher backgroundAgentLauncher;
        private readonly FileHivemindBackgroundLogStore backgroundLogStore;
        private readonly DispatcherTimer backgroundLogTimer;
        private readonly HashSet<string> activeConnectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<OceanyanFileHivemindStatusLogEntry> statusLogEntries =
            new List<OceanyanFileHivemindStatusLogEntry>();
        private readonly bool manageStartupWaitForm;
        private long backgroundLogReadPosition;
        private bool backgroundLogHistoryLoaded;
        private bool suppressBackgroundAgentCheckboxEvents;
        private bool suppressSelectedConnectionSettingEvents;
        private bool hasRaisedFinishedLoading;
        private bool hasStartedInitialization;
        private bool hasFinishedInitialization;
        private Window? statusLogHostWindow;
        private OceanyanFileHivemindStatusLogWindow? statusLogWindow;
        public event Action? FinishedLoading;

        public OceanyanFileHivemindWindow(
            GoogleDriveSyncService? syncService = null,
            bool manageStartupWaitForm = true)
        {
            InitializeComponent();
            this.syncService = syncService ?? new GoogleDriveSyncService();
            runtimeStateStore = new GoogleDriveConnectionRuntimeStateStore();
            credentialStore = new GoogleDriveSecureClientCredentialStore();
            GoogleDriveSignedInAccountManager.MigrateLegacyConnectionSelections(
                SaveFile.Data.FileHivemind,
                SaveFile.Data.FileHivemind.Connections,
                credentialStore);
            backgroundAgentLauncher = new FileHivemindBackgroundAgentLauncher();
            backgroundLogStore = new FileHivemindBackgroundLogStore();
            this.manageStartupWaitForm = manageStartupWaitForm;
            backgroundLogTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            backgroundLogTimer.Tick += BackgroundLogTimer_Tick;
            Title = "The Oceanyan File Hivemind";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Loaded += OceanyanFileHivemindWindow_Loaded;
            Unloaded += OceanyanFileHivemindWindow_Unloaded;
        }

        /// <inheritdoc/>
        public override string HeaderText => "THE OCEANYAN FILE HIVEMIND";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        private void RefreshConnections(string? preferredConnectionId = null)
        {
            string? selectedId = preferredConnectionId?.Trim();
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedId = SaveFile.Data.FileHivemind.SelectedConnectionId?.Trim();
            }

            List<ConnectionListEntry> connections = SaveFile.Data.FileHivemind.Connections
                .OrderBy(connection => connection.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.Id, StringComparer.OrdinalIgnoreCase)
                .Select(BuildConnectionListEntry)
                .ToList();

            ConnectionsListBox.ItemsSource = null;
            ConnectionsListBox.ItemsSource = connections;
            ConnectionListEntry? selectedEntry = connections.FirstOrDefault(connection =>
                string.Equals(connection.Connection.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? connections.FirstOrDefault();
            ConnectionsListBox.SelectedItem = selectedEntry;
            FileHivemindConnectionProfile? selectedConnection = selectedEntry?.Connection;
            SaveFile.Data.FileHivemind.SelectedConnectionId = selectedConnection?.Id ?? string.Empty;
            UpdateSelectedConnectionDetails(selectedConnection);
            RefreshBackgroundAgentControls();
        }

        private void UpdateSelectedConnectionDetails(FileHivemindConnectionProfile? connection = null)
        {
            connection ??= GetSelectedConnection();
            if (connection == null)
            {
                SelectedConnectionNameTextBlock.Text = "No connection selected.";
                SelectedConnectionProviderTextBlock.Text = "-";
                SelectedConnectionAccountTextBlock.Text = "-";
                SelectedConnectionRemoteTextBlock.Text = "-";
                SelectedConnectionLocalTextBlock.Text = "-";
                SelectedConnectionLastSyncTextBlock.Text = "-";
                suppressSelectedConnectionSettingEvents = true;
                SelectedConnectionAutoSyncCheckBox.IsChecked = false;
                SelectedConnectionAutoSyncCheckBox.IsEnabled = false;
                suppressSelectedConnectionSettingEvents = false;
                return;
            }

            SelectedConnectionNameTextBlock.Text = connection.EffectiveDisplayName;
            SelectedConnectionProviderTextBlock.Text = connection.ProviderDisplayName;
            SelectedConnectionAccountTextBlock.Text = string.IsNullOrWhiteSpace(connection.AccountDisplayName)
                ? "Not signed in."
                : connection.AccountDisplayName;
            SelectedConnectionRemoteTextBlock.Text = string.IsNullOrWhiteSpace(connection.RemoteDisplayName)
                ? "No folder configured."
                : connection.RemoteDisplayName;
            SelectedConnectionLocalTextBlock.Text = string.IsNullOrWhiteSpace(connection.GoogleDrive.LocalFolderPath)
                ? "No local mirror folder selected."
                : connection.GoogleDrive.LocalFolderPath;
            GoogleDriveConnectionRuntimeState? runtimeState = runtimeStateStore.Load(connection.Id);
            DateTimeOffset? lastSyncUtc = runtimeState?.LastSuccessfulSyncUtc ?? connection.GoogleDrive.LastSyncUtc;
            SelectedConnectionLastSyncTextBlock.Text = lastSyncUtc.HasValue
                ? lastSyncUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "Never";
            suppressSelectedConnectionSettingEvents = true;
            SelectedConnectionAutoSyncCheckBox.IsChecked = connection.AutoSyncEnabled;
            SelectedConnectionAutoSyncCheckBox.IsEnabled = true;
            suppressSelectedConnectionSettingEvents = false;
        }

        private FileHivemindConnectionProfile? GetSelectedConnection()
        {
            return (ConnectionsListBox.SelectedItem as ConnectionListEntry)?.Connection;
        }

        private void AppendStatus(string message, StatusLogLevel level = StatusLogLevel.Info, string? connectionName = null)
        {
            OceanyanFileHivemindStatusLogEntry entry = new OceanyanFileHivemindStatusLogEntry
            {
                Timestamp = DateTime.Now,
                Level = level.ToString(),
                Message = message ?? string.Empty,
                ConnectionName = connectionName?.Trim() ?? string.Empty
            };
            statusLogEntries.Add(entry);
            statusLogWindow?.AppendEntry(entry);
        }

        private ConnectionListEntry BuildConnectionListEntry(FileHivemindConnectionProfile connection)
        {
            (Brush primaryBrush, Brush secondaryBrush, string statusText) = ResolveConnectionListPresentation(connection);
            return new ConnectionListEntry(connection, primaryBrush, secondaryBrush, statusText);
        }

        private (Brush PrimaryBrush, Brush SecondaryBrush, string StatusText) ResolveConnectionListPresentation(
            FileHivemindConnectionProfile connection)
        {
            if (!connection.AutoSyncEnabled)
            {
                return (
                    new SolidColorBrush(Color.FromRgb(255, 184, 184)),
                    new SolidColorBrush(Color.FromRgb(244, 166, 166)),
                    "Auto-sync disabled");
            }

            GoogleDriveConnectionRuntimeState? runtimeState = runtimeStateStore.Load(connection.Id);
            if (!string.IsNullOrWhiteSpace(runtimeState?.LastErrorMessage))
            {
                return (
                    new SolidColorBrush(Color.FromRgb(255, 198, 198)),
                    new SolidColorBrush(Color.FromRgb(236, 178, 178)),
                    runtimeState.LastErrorMessage);
            }

            if (!string.IsNullOrWhiteSpace(runtimeState?.LastStatusMessage))
            {
                return (runtimeState.LastStatusLevel?.Trim() ?? string.Empty).ToUpperInvariant() switch
                {
                    "ACTION" => (
                        new SolidColorBrush(Color.FromRgb(255, 226, 155)),
                        new SolidColorBrush(Color.FromRgb(240, 210, 142)),
                        runtimeState.LastStatusMessage),
                    "WARNING" => (
                        new SolidColorBrush(Color.FromRgb(255, 218, 156)),
                        new SolidColorBrush(Color.FromRgb(245, 198, 134)),
                        runtimeState.LastStatusMessage),
                    "SUCCESS" => (
                        new SolidColorBrush(Color.FromRgb(176, 240, 188)),
                        new SolidColorBrush(Color.FromRgb(154, 224, 170)),
                        runtimeState.LastStatusMessage),
                    _ => (
                        new SolidColorBrush(Color.FromRgb(232, 232, 232)),
                        new SolidColorBrush(Color.FromRgb(214, 214, 214)),
                        runtimeState.LastStatusMessage)
                };
            }

            DateTimeOffset? lastSuccessfulSyncUtc = runtimeState?.LastSuccessfulSyncUtc ?? connection.GoogleDrive.LastSyncUtc;
            if (lastSuccessfulSyncUtc.HasValue)
            {
                return (
                    new SolidColorBrush(Color.FromRgb(176, 240, 188)),
                    new SolidColorBrush(Color.FromRgb(154, 224, 170)),
                    "Synced " + lastSuccessfulSyncUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }

            return (
                new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                new SolidColorBrush(Color.FromRgb(212, 212, 212)),
                "Waiting for first sync check");
        }

        private void RefreshBackgroundAgentControls()
        {
            suppressBackgroundAgentCheckboxEvents = true;
            RunAtStartupCheckBox.IsChecked = SaveFile.Data.FileHivemind.RunAgentAtStartup;
            DesktopToastsCheckBox.IsChecked = SaveFile.Data.FileHivemind.ShowDesktopToasts;
            RemotePollIntervalTextBox.Text = SaveFile.Data.FileHivemind.RemotePollIntervalSeconds.ToString();
            suppressBackgroundAgentCheckboxEvents = false;

            bool runAtStartup = SaveFile.Data.FileHivemind.RunAgentAtStartup;
            bool agentRunning = backgroundAgentLauncher.IsAgentRunning();
            bool registered = backgroundAgentLauncher.IsRegistered();
            int pollIntervalSeconds = SaveFile.Data.FileHivemind.RemotePollIntervalSeconds;
            int eligibleConnections = SaveFile.Data.FileHivemind.Connections.Count(FileHivemindBackgroundAgentLauncher.IsEligibleConnection);
            string toastStatus = SaveFile.Data.FileHivemind.ShowDesktopToasts ? "desktop toasts enabled" : "desktop toasts disabled";
            BackgroundAgentSessionButton.Content = agentRunning ? "Stop Agent Now" : "Launch Agent Now";
            BackgroundAgentSessionButton.IsEnabled = agentRunning || eligibleConnections > 0;
            BackgroundAgentSessionButton.ToolTip = agentRunning
                ? "Ask the hidden Hivemind agent to stop for this Windows session. Your saved startup preference stays the same."
                : eligibleConnections > 0
                    ? "Launch the hidden Hivemind agent now for this Windows session without waiting for Windows startup."
                    : "Sign into and fully configure at least one connection before launching the hidden Hivemind agent.";
            BackgroundAgentStatusTextBlock.Text = eligibleConnections == 0
                ? $"Background sync is idle because there are no signed-in, fully configured connections yet. Current poll interval: {pollIntervalSeconds} seconds, {toastStatus}."
                : runAtStartup
                    ? agentRunning
                        ? $"Background sync agent is running for this Windows session and polling Google Drive every {pollIntervalSeconds} seconds across {eligibleConnections} connection(s), {toastStatus}."
                        : registered
                            ? $"Background sync is enabled and will launch automatically after Windows sign-in for {eligibleConnections} connection(s). Current poll interval: {pollIntervalSeconds} seconds, {toastStatus}."
                            : $"Background sync is enabled, but the Windows startup entry is not registered yet. Current poll interval: {pollIntervalSeconds} seconds, {toastStatus}."
                    : agentRunning
                        ? $"Background sync agent is still running in this session, but Windows auto-start is disabled. Current poll interval: {pollIntervalSeconds} seconds, {toastStatus}."
                        : $"Background sync agent is disabled. Current poll interval: {pollIntervalSeconds} seconds, {toastStatus}.";
        }

        private void ApplyBackgroundAgentPreferences(bool ensureCurrentSessionAgent)
        {
            if (ensureCurrentSessionAgent)
            {
                backgroundAgentLauncher.EnsureRunningForCurrentSession(SaveFile.Data.FileHivemind);
            }
            else
            {
                backgroundAgentLauncher.ApplyRegistration(SaveFile.Data.FileHivemind);
            }

            RefreshBackgroundAgentControls();
        }

        private void TryApplyBackgroundAgentPreferences(bool ensureCurrentSessionAgent)
        {
            try
            {
                ApplyBackgroundAgentPreferences(ensureCurrentSessionAgent);
            }
            catch (Exception ex)
            {
                AppendStatus("Background agent setup failed: " + ex.Message, StatusLogLevel.Error);
            }
        }

        private void MarkConnectionUpdating(FileHivemindConnectionProfile connection)
        {
            if (connection == null)
            {
                return;
            }

            activeConnectionIds.Add(connection.Id);
            RefreshConnections(connection.Id);
        }

        private void ClearConnectionUpdating(FileHivemindConnectionProfile connection)
        {
            if (connection == null)
            {
                return;
            }

            activeConnectionIds.Remove(connection.Id);
            RefreshConnections(connection.Id);
        }

        private void PersistConnection(FileHivemindConnectionProfile connection)
        {
            GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(connection.GoogleDrive, credentialStore);
            bool hadAnyConnections = SaveFile.Data.FileHivemind.Connections.Count > 0;
            FileHivemindConnectionProfile? existingConnection = SaveFile.Data.FileHivemind.Connections.FirstOrDefault(existing =>
                string.Equals(existing.Id, connection.Id, StringComparison.OrdinalIgnoreCase));
            if (existingConnection == null)
            {
                SaveFile.Data.FileHivemind.Connections.Add(connection);
            }
            else if (!ReferenceEquals(existingConnection, connection))
            {
                int existingIndex = SaveFile.Data.FileHivemind.Connections.IndexOf(existingConnection);
                SaveFile.Data.FileHivemind.Connections[existingIndex] = connection;
            }

            if (!hadAnyConnections && !SaveFile.Data.FileHivemind.BackgroundStartupPreferenceConfigured)
            {
                SaveFile.Data.FileHivemind.RunAgentAtStartup = true;
                SaveFile.Data.FileHivemind.BackgroundStartupPreferenceConfigured = true;
            }

            SaveFile.Data.FileHivemind.SelectedConnectionId = connection.Id;
            SaveFile.Save();
            RefreshConnections(connection.Id);
            TryApplyBackgroundAgentPreferences(ensureCurrentSessionAgent: true);
        }

        private void ShowConnectionEditor(FileHivemindConnectionProfile connection, bool isDraftConnection)
        {
            GoogleDriveSyncWindow editor = new GoogleDriveSyncWindow(
                connection,
                PersistConnection,
                syncService,
                isDraftConnection)
            {
                Owner = ResolveOwnerWindow(),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            Window dialog = OceanyaWindowManager.CreateWindow(editor);
            dialog.Owner = ResolveOwnerWindow();
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _ = dialog.ShowDialog();
            RefreshConnections(connection.Id);
        }

        private async Task<bool> ExecuteConnectionSyncAsync(
            string title,
            FileHivemindConnectionProfile connection,
            Func<GoogleDriveSyncSettings, Task<GoogleDriveSyncSummary>> operation)
        {
            using FileHivemindConnectionExecutionLock? executionLock =
                FileHivemindConnectionExecutionLock.TryAcquire(connection.Id, TimeSpan.Zero);
            if (executionLock == null)
            {
                AppendStatus(
                    "Skipped because this connection is already being synced by another Oceanya process.",
                    StatusLogLevel.Warning,
                    connection.EffectiveDisplayName);
                return false;
            }

            try
            {
                AppendStatus("Starting sync operation.", StatusLogLevel.Action, connection.EffectiveDisplayName);
                MarkConnectionUpdating(connection);
                Window waitOwner = ResolveOwnerWindow();
                await WaitForm.ShowFormAsync(title, waitOwner);
                try
                {
                    EnsureMountedIfEnabled(connection);
                    GoogleDriveSyncSummary summary = await operation(connection.GoogleDrive);
                    AppendStatus("Refreshing only the AO assets touched by this sync.", StatusLogLevel.Action, connection.EffectiveDisplayName);
                    RefreshMountedAssets(connection, summary.LocalChanges);
                    await TryRefreshRuntimeStateAsync(connection, summary);
                    SaveFile.Save();
                    UpdateSelectedConnectionDetails(connection);
                    AppendStatus(BuildSummaryMessage(summary), StatusLogLevel.Success, connection.EffectiveDisplayName);
                    return true;
                }
                finally
                {
                    await WaitForm.CloseFormAsync();
                    ClearConnectionUpdating(connection);
                }
            }
            catch (Exception ex)
            {
                ClearConnectionUpdating(connection);
                AppendStatus("Sync failed: " + ex.Message, StatusLogLevel.Error, connection.EffectiveDisplayName);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Hivemind sync failed:\n" + ex.Message,
                    "The Oceanyan File Hivemind",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        private void EnsureMountedIfEnabled(FileHivemindConnectionProfile connection)
        {
            if (!connection.GoogleDrive.AutoAddMountPath)
            {
                return;
            }

            string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
            GoogleDriveClientAssetIntegration.EnsureMounted(
                configIniPath,
                connection.GoogleDrive,
                connection.GoogleDrive.UseExistingMountPath,
                message =>
                {
                    WaitForm.SetSubtitle(message);
                    AppendStatus(message, StatusLogLevel.Info, connection.EffectiveDisplayName);
                });
        }

        private void RefreshMountedAssets(
            FileHivemindConnectionProfile connection,
            GoogleDriveSyncLocalChangeSet? localChanges)
        {
            string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
            GoogleDriveClientAssetIntegration.RefreshMountedAssets(
                configIniPath,
                connection.GoogleDrive,
                localChanges,
                    subtitle => WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": " + subtitle));
        }

        private async Task TryRefreshRuntimeStateAsync(
            FileHivemindConnectionProfile connection,
            GoogleDriveSyncSummary summary)
        {
            try
            {
                GoogleDriveConnectionRuntimeState runtimeState = await syncService.BuildRuntimeStateAfterSyncAsync(
                    connection.Id,
                    connection.GoogleDrive,
                    summary,
                    CancellationToken.None);
                runtimeStateStore.Save(runtimeState);
            }
            catch (Exception ex)
            {
                AppendStatus(
                    "Updated the connection data, but background change tracking state could not be refreshed: " + ex.Message,
                    StatusLogLevel.Warning,
                    connection.EffectiveDisplayName);
            }
        }

        private static string BuildSummaryMessage(GoogleDriveSyncSummary summary)
        {
            return "Sync complete. "
                + $"Directories created: {summary.DirectoriesCreated}; "
                + $"downloaded: {summary.FilesDownloaded}; "
                + $"uploaded: {summary.FilesUploaded}; "
                + $"local deletions: {summary.LocalFilesDeleted}; "
                + $"remote deletions: {summary.RemoteFilesDeleted}; "
                + $"skipped: {summary.FilesSkipped}.";
        }

        private Window ResolveOwnerWindow()
        {
            return HostWindow
                ?? Application.Current?.MainWindow
                ?? throw new InvalidOperationException("No available owner window was found.");
        }

        private void ConnectionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FileHivemindConnectionProfile? selectedConnection = GetSelectedConnection();
            SaveFile.Data.FileHivemind.SelectedConnectionId = selectedConnection?.Id ?? string.Empty;
            SaveFile.Save();
            UpdateSelectedConnectionDetails(selectedConnection);
        }

        private async void BackgroundAgentSessionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isRunning = backgroundAgentLauncher.IsAgentRunning();
                if (isRunning)
                {
                    bool stopRequested = backgroundAgentLauncher.RequestStopForCurrentSession();
                    AppendStatus(
                        stopRequested
                            ? "Requested the hidden Hivemind background agent to stop for this Windows session."
                            : "The hidden Hivemind background agent is not running.",
                        stopRequested ? StatusLogLevel.Info : StatusLogLevel.Warning);
                }
                else
                {
                    if (!FileHivemindBackgroundAgentLauncher.HasEligibleConnections(SaveFile.Data.FileHivemind))
                    {
                        AppendStatus(
                            "At least one signed-in, fully configured connection is required before launching the hidden Hivemind agent.",
                            StatusLogLevel.Warning);
                        RefreshBackgroundAgentControls();
                        return;
                    }

                    bool started = backgroundAgentLauncher.StartForCurrentSession(SaveFile.Data.FileHivemind);
                    AppendStatus(
                        started
                            ? "Launched the hidden Hivemind background agent for this Windows session."
                            : "The hidden Hivemind background agent could not be launched.",
                        started ? StatusLogLevel.Success : StatusLogLevel.Warning);
                }

                RefreshBackgroundAgentControls();
                await Task.Delay(900);
                RefreshBackgroundAgentControls();
            }
            catch (Exception ex)
            {
                AppendStatus("Background agent session control failed: " + ex.Message, StatusLogLevel.Error);
            }
        }

        private void RunAtStartupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (suppressBackgroundAgentCheckboxEvents)
            {
                return;
            }

            SaveFile.Data.FileHivemind.RunAgentAtStartup = RunAtStartupCheckBox.IsChecked == true;
            SaveFile.Data.FileHivemind.BackgroundStartupPreferenceConfigured = true;
            SaveFile.Save();
            TryApplyBackgroundAgentPreferences(ensureCurrentSessionAgent: true);
            AppendStatus(
                SaveFile.Data.FileHivemind.RunAgentAtStartup
                    ? "Enabled hidden Hivemind background sync at Windows startup."
                    : "Disabled hidden Hivemind background sync at Windows startup.",
                StatusLogLevel.Info);
        }

        private void DesktopToastsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (suppressBackgroundAgentCheckboxEvents)
            {
                return;
            }

            SaveFile.Data.FileHivemind.ShowDesktopToasts = DesktopToastsCheckBox.IsChecked == true;
            SaveFile.Data.FileHivemind.DesktopToastPreferenceConfigured = true;
            SaveFile.Save();
            RefreshBackgroundAgentControls();
            AppendStatus(
                SaveFile.Data.FileHivemind.ShowDesktopToasts
                    ? "Enabled desktop toast popups for important Hivemind background sync events."
                    : "Disabled desktop toast popups for Hivemind background sync events.",
                StatusLogLevel.Info);
        }

        private void SelectedConnectionAutoSyncCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (suppressSelectedConnectionSettingEvents)
            {
                return;
            }

            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null)
            {
                return;
            }

            connection.AutoSyncEnabled = SelectedConnectionAutoSyncCheckBox.IsChecked == true;
            SaveFile.Save();
            RefreshConnections(connection.Id);
            TryApplyBackgroundAgentPreferences(ensureCurrentSessionAgent: true);
            AppendStatus(
                connection.AutoSyncEnabled
                    ? "Enabled automatic background syncing for this connection."
                    : "Disabled automatic background syncing for this connection.",
                StatusLogLevel.Info,
                connection.EffectiveDisplayName);
        }

        private void RemotePollIntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyRemotePollIntervalFromUi(showFeedback: true);
        }

        private void RemotePollIntervalTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            ApplyRemotePollIntervalFromUi(showFeedback: true);
            Keyboard.ClearFocus();
        }

        private void ApplyRemotePollIntervalFromUi(bool showFeedback)
        {
            if (suppressBackgroundAgentCheckboxEvents)
            {
                return;
            }

            string rawValue = RemotePollIntervalTextBox.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(rawValue, out int parsedSeconds))
            {
                RemotePollIntervalTextBox.Text = SaveFile.Data.FileHivemind.RemotePollIntervalSeconds.ToString();
                if (showFeedback)
                {
                    AppendStatus("Remote poll interval must be a whole number of seconds.", StatusLogLevel.Warning);
                }

                return;
            }

            int normalizedSeconds = Math.Clamp(parsedSeconds, 5, 3600);
            RemotePollIntervalTextBox.Text = normalizedSeconds.ToString();
            if (SaveFile.Data.FileHivemind.RemotePollIntervalSeconds == normalizedSeconds)
            {
                RefreshBackgroundAgentControls();
                return;
            }

            SaveFile.Data.FileHivemind.RemotePollIntervalSeconds = normalizedSeconds;
            SaveFile.Save();
            TryApplyBackgroundAgentPreferences(ensureCurrentSessionAgent: true);
            if (showFeedback)
            {
                AppendStatus(
                    $"Background Google Drive polling interval set to {normalizedSeconds} seconds.",
                    StatusLogLevel.Info);
            }
        }

        private void AddDriveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            FileHivemindConnectionProfile draftConnection = FileHivemindConnectionProfile.CreateGoogleDriveProfile();
            AppendStatus("Opening a new Google Drive connection editor.", StatusLogLevel.Action);
            ShowConnectionEditor(draftConnection, isDraftConnection: true);
        }

        private async void ImportConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import The Oceanyan File Hivemind connection",
                Filter = "Oceanyan Hivemind connection (*.oceanyahive.json)|*.oceanyahive.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                Window owner = ResolveOwnerWindow();
                string fileText = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                FileHivemindConnectionProfile parsedConnection = FileHivemindConnectionExchangeSerializer.Parse(fileText);
                FileHivemindConnectionProfile importConnection = BuildImportedConnection(parsedConnection);

                GoogleDriveSignedInAccount? selectedAccount = PromptForImportedConnectionAccount(owner, importConnection);
                if (selectedAccount == null)
                {
                    AppendStatus("Connection import was canceled before selecting a Google account.", StatusLogLevel.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(importConnection.GoogleDrive.LastSignedInEmail))
                {
                    AppendStatus("Opening browser for Google Drive sign-in to finish the imported connection.", StatusLogLevel.Action);
                    GoogleDriveUserInfo user = await syncService.SignInAsync(
                        importConnection.GoogleDrive,
                        forceAccountSelection: true,
                        CancellationToken.None);
                    GoogleDriveSignedInAccountManager.RegisterSignedInAccount(
                        SaveFile.Data.FileHivemind,
                        importConnection.GoogleDrive,
                        user,
                        credentialStore);
                    BringHostWindowToFront();
                    AppendStatus(
                        "Imported connection signed in as "
                        + (string.IsNullOrWhiteSpace(user.EmailAddress) ? user.DisplayName : user.EmailAddress)
                        + ".",
                        StatusLogLevel.Success,
                        importConnection.EffectiveDisplayName);
                }

                PersistConnection(importConnection);

                bool syncCompleted = await ExecuteConnectionSyncAsync(
                    "Importing and syncing from Google Drive...",
                    importConnection,
                    settings => syncService.PullFromDriveAsync(
                        settings,
                        subtitle => WaitForm.SetSubtitle(importConnection.EffectiveDisplayName + ": " + subtitle),
                        CancellationToken.None));
                if (!syncCompleted)
                {
                    return;
                }

                RefreshConnections(importConnection.Id);
                OceanyaMessageBox.Show(
                    owner,
                    "The shared connection was imported, signed in, and synced successfully.",
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                BringHostWindowToFront();
                AppendStatus("Connection import failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not import the shared connection:\n" + ex.Message,
                    "Import Hivemind Connection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ExportConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null)
            {
                AppendStatus("No connection is selected to export.", StatusLogLevel.Warning);
                return;
            }

            try
            {
                SaveFileDialog dialog = new SaveFileDialog
                {
                    Title = "Export The Oceanyan File Hivemind connection",
                    Filter = "Oceanyan Hivemind connection (*.oceanyahive.json)|*.oceanyahive.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = BuildExportFileName(connection),
                    AddExtension = true,
                    DefaultExt = ".oceanyahive.json",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                string fileText = FileHivemindConnectionExchangeSerializer.Serialize(connection, credentialStore);
                File.WriteAllText(dialog.FileName, fileText, new UTF8Encoding(false));
                AppendStatus("Exported a shareable connection file.", StatusLogLevel.Success, connection.EffectiveDisplayName);
            }
            catch (Exception ex)
            {
                AppendStatus("Connection export failed: " + ex.Message, StatusLogLevel.Error, connection.EffectiveDisplayName);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not export the shared connection:\n" + ex.Message,
                    "Export Hivemind Connection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void EditConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null)
            {
                AppendStatus("No connection is selected to edit.", StatusLogLevel.Warning);
                return;
            }

            AppendStatus("Opening the selected connection editor.", StatusLogLevel.Action, connection.EffectiveDisplayName);
            ShowConnectionEditor(connection, isDraftConnection: false);
        }

        private void DeleteConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null)
            {
                AppendStatus("No connection is selected to delete.", StatusLogLevel.Warning);
                return;
            }

            MessageBoxResult result = OceanyaMessageBox.Show(
                ResolveOwnerWindow(),
                "Delete the selected hivemind connection. Its saved Google account will only be removed if no other connections use it.\n\n"
                    + connection.EffectiveDisplayName,
                "Delete Connection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            string tokenStoreKey = connection.GoogleDrive.TokenStoreKey?.Trim() ?? string.Empty;
            GoogleDriveSignedInAccountManager.ClearConnectionSelection(connection.GoogleDrive);
            GoogleDriveConnectionCredentialSupport.DeleteStoredSecret(connection.GoogleDrive, credentialStore);
            SaveFile.Data.FileHivemind.Connections.Remove(connection);
            TryDeleteUnusedGoogleAccount(tokenStoreKey);
            SaveFile.Data.FileHivemind.SelectedConnectionId = SaveFile.Data.FileHivemind.Connections.FirstOrDefault()?.Id ?? string.Empty;
            SaveFile.Save();
            runtimeStateStore.Delete(connection.Id);
            RefreshConnections();
            TryApplyBackgroundAgentPreferences(ensureCurrentSessionAgent: true);
            AppendStatus("Deleted the saved connection.", StatusLogLevel.Warning, connection.EffectiveDisplayName);
        }

        private bool TryOpenSelectedLocalFolder()
        {
            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null || string.IsNullOrWhiteSpace(connection.GoogleDrive.LocalFolderPath))
            {
                AppendStatus("No local mirror folder is available to open.", StatusLogLevel.Warning);
                return false;
            }

            if (!Directory.Exists(connection.GoogleDrive.LocalFolderPath))
            {
                AppendStatus("The selected local mirror folder does not exist yet.", StatusLogLevel.Warning, connection.EffectiveDisplayName);
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = connection.GoogleDrive.LocalFolderPath,
                UseShellExecute = true
            });
            AppendStatus("Opened the local mirror folder.", StatusLogLevel.Info, connection.EffectiveDisplayName);
            return true;
        }

        private void EnsureMountPathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FileHivemindConnectionProfile? connection = GetSelectedConnection();
                if (connection == null)
                {
                    AppendStatus("No connection is selected for mount path verification.", StatusLogLevel.Warning);
                    return;
                }

                EnsureMountedIfEnabled(connection);
                AppendStatus("AO mount path configuration is ready.", StatusLogLevel.Success, connection.EffectiveDisplayName);
            }
            catch (Exception ex)
            {
                AppendStatus("Mount path update failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not update config.ini mount paths:\n" + ex.Message,
                    "Mount Path Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void PullSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null)
            {
                AppendStatus("No connection is selected to sync.", StatusLogLevel.Warning);
                return;
            }

            await ExecuteConnectionSyncAsync(
                "Syncing from Google Drive...",
                connection,
                settings => syncService.PullFromDriveAsync(
                    settings,
                    subtitle => WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": " + subtitle),
                    CancellationToken.None));
        }

        private async void PushSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            FileHivemindConnectionProfile? connection = GetSelectedConnection();
            if (connection == null)
            {
                AppendStatus("No connection is selected to publish.", StatusLogLevel.Warning);
                return;
            }

            await ExecuteConnectionSyncAsync(
                "Publishing local folder to Google Drive...",
                connection,
                settings => syncService.PushLocalFolderAsync(
                    settings,
                    subtitle => WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": " + subtitle),
                    CancellationToken.None));
        }

        private async void PullAllButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAllConnectionsAsync(
                "Syncing all hivemind connections from Google Drive...",
                "Starting Sync All From Drive for every saved connection.",
                connection => syncService.PullFromDriveAsync(
                    connection.GoogleDrive,
                    subtitle => WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": " + subtitle),
                    CancellationToken.None));
        }

        private async void PushAllButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteAllConnectionsAsync(
                "Publishing all hivemind connections to Google Drive...",
                "Starting Push All To Drive for every saved connection.",
                connection => syncService.PushLocalFolderAsync(
                    connection.GoogleDrive,
                    subtitle => WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": " + subtitle),
                    CancellationToken.None));
        }

        private async void RefreshAssetsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configIniPath))
                {
                    return;
                }

                Globals.UpdateConfigINI(configIniPath);
                AppendStatus("Starting a full AO asset refresh.", StatusLogLevel.Action);
                await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(ResolveOwnerWindow());
                AppendStatus("AO asset indexes were fully refreshed.", StatusLogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendStatus("AO asset refresh failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not refresh AO assets:\n" + ex.Message,
                    "Refresh AO Assets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            RequestHostClose(false);
        }

        private void SelectedConnectionLocalTextBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TryOpenSelectedLocalFolder();
        }

        private FileHivemindConnectionProfile BuildImportedConnection(FileHivemindConnectionProfile parsedConnection)
        {
            FileHivemindConnectionProfile imported = FileHivemindConnectionExchangeSerializer.CreateImportReadyProfile(parsedConnection);
            FileHivemindConnectionProfile? existingConnection = SaveFile.Data.FileHivemind.Connections.FirstOrDefault(connection =>
                string.Equals(connection.ProviderId, imported.ProviderId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(connection.GoogleDrive.RemoteFolderId, imported.GoogleDrive.RemoteFolderId, StringComparison.OrdinalIgnoreCase));
            if (existingConnection == null)
            {
                return imported;
            }

            imported.Id = existingConnection.Id;
            imported.GoogleDrive.OAuthClientId = string.IsNullOrWhiteSpace(imported.GoogleDrive.OAuthClientId)
                ? existingConnection.GoogleDrive.OAuthClientId?.Trim() ?? string.Empty
                : imported.GoogleDrive.OAuthClientId.Trim();
            imported.GoogleDrive.OAuthClientSecretStoreKey = string.IsNullOrWhiteSpace(existingConnection.GoogleDrive.OAuthClientSecretStoreKey)
                ? imported.GoogleDrive.OAuthClientSecretStoreKey
                : existingConnection.GoogleDrive.OAuthClientSecretStoreKey.Trim();
            imported.GoogleDrive.TokenStoreKey = string.IsNullOrWhiteSpace(existingConnection.GoogleDrive.TokenStoreKey)
                ? Guid.NewGuid().ToString("N")
                : existingConnection.GoogleDrive.TokenStoreKey.Trim();
            if (!string.IsNullOrWhiteSpace(existingConnection.GoogleDrive.LocalFolderPath))
            {
                imported.GoogleDrive.LocalFolderPath = existingConnection.GoogleDrive.LocalFolderPath.Trim();
            }
            imported.GoogleDrive.IsOceanyaManagedLocalFolder = existingConnection.GoogleDrive.IsOceanyaManagedLocalFolder;

            return imported;
        }

        private GoogleDriveSignedInAccount? PromptForImportedConnectionAccount(
            Window owner,
            FileHivemindConnectionProfile importConnection)
        {
            List<GoogleDriveSignedInAccount> compatibleAccounts = GoogleDriveSignedInAccountManager.GetCompatibleAccounts(
                SaveFile.Data.FileHivemind,
                importConnection.GoogleDrive,
                credentialStore);
            if (compatibleAccounts.Count == 0)
            {
                MessageBoxResult signInDecision = OceanyaMessageBox.Show(
                    owner,
                    "Oceanya will import this shared connection, open Google sign-in, create the local mirror automatically, and sync it once. Continue?",
                    "Import Hivemind Connection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                return signInDecision == MessageBoxResult.Yes ? new GoogleDriveSignedInAccount() : null;
            }

            GoogleDriveSignedInAccountManager.ApplyDefaultAccountToConnection(
                SaveFile.Data.FileHivemind,
                importConnection.GoogleDrive,
                credentialStore);
            GoogleDriveAccountSelectionWindow picker = new GoogleDriveAccountSelectionWindow(
                compatibleAccounts,
                importConnection.GoogleDrive.TokenStoreKey)
            {
                Owner = owner
            };
            if (picker.ShowDialog() != true || picker.SelectedAccount == null)
            {
                return null;
            }

            GoogleDriveSignedInAccountManager.ApplyAccountToConnection(
                SaveFile.Data.FileHivemind,
                importConnection.GoogleDrive,
                picker.SelectedAccount);
            return picker.SelectedAccount;
        }

        private void TryDeleteUnusedGoogleAccount(string tokenStoreKey)
        {
            string normalizedTokenStoreKey = tokenStoreKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedTokenStoreKey))
            {
                return;
            }

            bool stillUsed = SaveFile.Data.FileHivemind.Connections.Any(existing =>
                string.Equals(
                    existing.GoogleDrive.TokenStoreKey?.Trim() ?? string.Empty,
                    normalizedTokenStoreKey,
                    StringComparison.OrdinalIgnoreCase));
            if (!stillUsed)
            {
                GoogleDriveSignedInAccountManager.SignOutAccount(
                    SaveFile.Data.FileHivemind,
                    normalizedTokenStoreKey);
            }
        }

        private static string BuildExportFileName(FileHivemindConnectionProfile connection)
        {
            string sourceName = connection.EffectiveDisplayName;
            StringBuilder builder = new StringBuilder();
            foreach (char character in sourceName)
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
                {
                    builder.Append(character);
                }
                else if (char.IsWhiteSpace(character))
                {
                    builder.Append('_');
                }
            }

            string fileName = builder.Length == 0 ? "oceanyan_file_hivemind_connection" : builder.ToString();
            return fileName + ".oceanyahive.json";
        }

        private void AppendCredentialWarningsForInvalidConnections()
        {
            foreach (FileHivemindConnectionProfile savedConnection in SaveFile.Data.FileHivemind.Connections)
            {
                if (!string.Equals(savedConnection.ProviderId, FileHivemindProviderIds.GoogleDrive, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                        savedConnection.GoogleDrive,
                        out _,
                        out _,
                        credentialStore,
                        allowLegacyFallback: false))
                {
                    continue;
                }

                string credentialMessage = GoogleDriveConnectionCredentialSupport.BuildStatusMessage(
                    savedConnection.GoogleDrive,
                    credentialStore,
                    allowLegacyFallback: false);
                AppendStatus(credentialMessage, StatusLogLevel.Warning, savedConnection.EffectiveDisplayName);
            }
        }

        private async Task ExecuteAllConnectionsAsync(
            string waitTitle,
            string startMessage,
            Func<FileHivemindConnectionProfile, Task<GoogleDriveSyncSummary>> operation)
        {
            var connections = SaveFile.Data.FileHivemind.Connections.ToList();
            if (connections.Count == 0)
            {
                AppendStatus("There are no saved connections to sync.", StatusLogLevel.Warning);
                return;
            }

            try
            {
                AppendStatus(startMessage, StatusLogLevel.Action);
                Window waitOwner = ResolveOwnerWindow();
                await WaitForm.ShowFormAsync(waitTitle, waitOwner);
                try
                {
                    foreach (FileHivemindConnectionProfile connection in connections)
                    {
                        using FileHivemindConnectionExecutionLock? executionLock =
                            FileHivemindConnectionExecutionLock.TryAcquire(connection.Id, TimeSpan.Zero);
                        if (executionLock == null)
                        {
                            AppendStatus(
                                "Skipped because this connection is already being synced by another Oceanya process.",
                                StatusLogLevel.Warning,
                                connection.EffectiveDisplayName);
                            continue;
                        }

                        AppendStatus("Preparing sync.", StatusLogLevel.Action, connection.EffectiveDisplayName);
                        MarkConnectionUpdating(connection);
                        WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": preparing sync...");
                        try
                        {
                            EnsureMountedIfEnabled(connection);
                            GoogleDriveSyncSummary summary = await operation(connection);
                            AppendStatus("Refreshing only the AO assets touched by this sync.", StatusLogLevel.Action, connection.EffectiveDisplayName);
                            RefreshMountedAssets(connection, summary.LocalChanges);
                            await TryRefreshRuntimeStateAsync(connection, summary);
                            AppendStatus(BuildSummaryMessage(summary), StatusLogLevel.Success, connection.EffectiveDisplayName);
                        }
                        finally
                        {
                            ClearConnectionUpdating(connection);
                        }
                    }

                    SaveFile.Save();
                    UpdateSelectedConnectionDetails();
                }
                finally
                {
                    await WaitForm.CloseFormAsync();
                }
            }
            catch (Exception ex)
            {
                AppendStatus("Bulk sync failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Bulk sync failed:\n" + ex.Message,
                    "The Oceanyan File Hivemind",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BringHostWindowToFront()
        {
            Window? window = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            if (window == null)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
            window.Focus();
            window.Topmost = true;
            window.Topmost = false;

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private async void OceanyanFileHivemindWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (hasFinishedInitialization)
            {
                return;
            }

            if (hasStartedInitialization)
            {
                return;
            }

            hasStartedInitialization = true;

            Window owner = ResolveOwnerWindow();
            IsEnabled = false;
            bool ownsWaitForm = false;

            try
            {
                if (manageStartupWaitForm)
                {
                    await WaitForm.ShowFormAsync("Opening The Oceanyan File Hivemind...", owner);
                    ownsWaitForm = true;
                }

                WaitForm.SetSubtitle("Loading saved connections...");

                // Yield once so the host window and wait form are both visible before doing startup work.
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

                RefreshConnections();

                WaitForm.SetSubtitle("Preparing background sync status...");
                RefreshBackgroundAgentControls();

                WaitForm.SetSubtitle("Checking saved Google Cloud credentials...");
                AppendCredentialWarningsForInvalidConnections();

                WaitForm.SetSubtitle("Loading recent background agent activity...");
                EnsureBackgroundLogFeedRunning();

                AppendStatus("The Oceanyan File Hivemind loaded.", StatusLogLevel.Success);
                hasFinishedInitialization = true;

                if (!hasRaisedFinishedLoading)
                {
                    hasRaisedFinishedLoading = true;
                    FinishedLoading?.Invoke();
                }
            }
            catch (Exception ex)
            {
                AppendStatus("Hivemind startup failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    owner,
                    "The Oceanyan File Hivemind could not finish loading:\n" + ex.Message,
                    "The Oceanyan File Hivemind",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RequestHostClose(false);
            }
            finally
            {
                IsEnabled = true;
                if (ownsWaitForm)
                {
                    await WaitForm.CloseFormAsync();
                }
            }
        }

        private void OceanyanFileHivemindWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            backgroundLogTimer.Stop();
        }

        private void OpenStatusLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (statusLogHostWindow != null && statusLogHostWindow.IsLoaded)
            {
                statusLogHostWindow.Activate();
                return;
            }

            statusLogWindow = new OceanyanFileHivemindStatusLogWindow
            {
                Owner = ResolveOwnerWindow(),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            statusLogWindow.LoadEntries(statusLogEntries);

            statusLogHostWindow = OceanyaWindowManager.CreateWindow(statusLogWindow);
            statusLogHostWindow.Owner = ResolveOwnerWindow();
            statusLogHostWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            statusLogHostWindow.Closed += (_, _) =>
            {
                statusLogHostWindow = null;
                statusLogWindow = null;
            };
            statusLogHostWindow.Show();
        }

        private void EnsureBackgroundLogFeedRunning()
        {
            if (!backgroundLogHistoryLoaded)
            {
                foreach (FileHivemindBackgroundLogEntry entry in backgroundLogStore.ReadRecent(80))
                {
                    AppendBackgroundLogEntry(entry);
                }

                backgroundLogHistoryLoaded = true;
                backgroundLogReadPosition = backgroundLogStore.ReadFrom(long.MaxValue).NextPosition;
            }

            if (!backgroundLogTimer.IsEnabled)
            {
                backgroundLogTimer.Start();
            }
        }

        private void BackgroundLogTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                FileHivemindBackgroundLogReadResult result = backgroundLogStore.ReadFrom(backgroundLogReadPosition);
                backgroundLogReadPosition = result.NextPosition;
                foreach (FileHivemindBackgroundLogEntry entry in result.Entries)
                {
                    AppendBackgroundLogEntry(entry);
                }
            }
            catch
            {
            }
        }

        private void AppendBackgroundLogEntry(FileHivemindBackgroundLogEntry entry)
        {
            UpdateConnectionActivityFromBackgroundLog(entry);
            AppendStatus(
                "[Agent] " + entry.Message,
                MapBackgroundLogLevel(entry.Level),
                string.IsNullOrWhiteSpace(entry.ConnectionName) ? null : entry.ConnectionName);
        }

        private void UpdateConnectionActivityFromBackgroundLog(FileHivemindBackgroundLogEntry entry)
        {
            string connectionId = entry.ConnectionId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            FileHivemindConnectionProfile? selectedConnection = GetSelectedConnection();
            RefreshConnections(selectedConnection?.Id ?? connectionId);
        }

        private static StatusLogLevel MapBackgroundLogLevel(string? level)
        {
            return (level?.Trim() ?? string.Empty).ToUpperInvariant() switch
            {
                "ACTION" => StatusLogLevel.Action,
                "SUCCESS" => StatusLogLevel.Success,
                "WARNING" => StatusLogLevel.Warning,
                "ERROR" => StatusLogLevel.Error,
                _ => StatusLogLevel.Info
            };
        }
    }
}
