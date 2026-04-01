using Microsoft.Win32;
using OceanyaClient.Features.FileHivemind;
using OceanyaClient.Features.GoogleDriveSync;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OceanyaClient
{
    public partial class OceanyanFileHivemindWindow : OceanyaWindowContentControl, Features.Startup.IStartupFunctionalityWindow
    {
        private enum StatusLogLevel
        {
            Info,
            Action,
            Success,
            Warning,
            Error
        }

        private readonly GoogleDriveSyncService syncService;
        private readonly GoogleDriveRemoteChangeTracker remoteChangeTracker;
        private readonly GoogleDriveConnectionRuntimeStateStore runtimeStateStore;
        private readonly FileHivemindBackgroundAgentLauncher backgroundAgentLauncher;
        private readonly FileHivemindBackgroundLogStore backgroundLogStore;
        private readonly DispatcherTimer backgroundLogTimer;
        private long backgroundLogReadPosition;
        private bool backgroundLogHistoryLoaded;
        private bool suppressBackgroundAgentCheckboxEvents;
        private bool hasRaisedFinishedLoading;
        public event Action? FinishedLoading;

        public OceanyanFileHivemindWindow(GoogleDriveSyncService? syncService = null)
        {
            InitializeComponent();
            this.syncService = syncService ?? new GoogleDriveSyncService();
            remoteChangeTracker = new GoogleDriveRemoteChangeTracker();
            runtimeStateStore = new GoogleDriveConnectionRuntimeStateStore();
            backgroundAgentLauncher = new FileHivemindBackgroundAgentLauncher();
            backgroundLogStore = new FileHivemindBackgroundLogStore();
            backgroundLogTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            backgroundLogTimer.Tick += BackgroundLogTimer_Tick;
            Title = "The Oceanyan File Hivemind";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Loaded += OceanyanFileHivemindWindow_Loaded;
            Unloaded += OceanyanFileHivemindWindow_Unloaded;
            RefreshConnections();
            RefreshBackgroundAgentControls();
            AppendStatus("The Oceanyan File Hivemind loaded.", StatusLogLevel.Success);
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

            var connections = SaveFile.Data.FileHivemind.Connections
                .OrderBy(connection => connection.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ConnectionsListBox.ItemsSource = null;
            ConnectionsListBox.ItemsSource = connections;
            FileHivemindConnectionProfile? selectedConnection = connections.FirstOrDefault(connection =>
                string.Equals(connection.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? connections.FirstOrDefault();
            ConnectionsListBox.SelectedItem = selectedConnection;
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
        }

        private FileHivemindConnectionProfile? GetSelectedConnection()
        {
            return ConnectionsListBox.SelectedItem as FileHivemindConnectionProfile;
        }

        private void AppendStatus(string message, StatusLogLevel level = StatusLogLevel.Info, string? connectionName = null)
        {
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0)
            };
            paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130))
            });
            paragraph.Inlines.Add(new Run("[" + level.ToString().ToUpperInvariant() + "] ")
            {
                Foreground = GetSeverityBrush(level),
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(connectionName))
            {
                paragraph.Inlines.Add(new Run("[" + connectionName.Trim() + "] ")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 180, 255)),
                    FontWeight = FontWeights.SemiBold
                });
            }

            paragraph.Inlines.Add(new Run(message)
            {
                Foreground = GetMessageBrush(level)
            });

            StatusRichTextBox.Document.Blocks.Add(paragraph);
            StatusRichTextBox.ScrollToEnd();
        }

        private static Brush GetSeverityBrush(StatusLogLevel level)
        {
            return level switch
            {
                StatusLogLevel.Action => new SolidColorBrush(Color.FromRgb(91, 192, 255)),
                StatusLogLevel.Success => new SolidColorBrush(Color.FromRgb(118, 224, 141)),
                StatusLogLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 196, 92)),
                StatusLogLevel.Error => new SolidColorBrush(Color.FromRgb(255, 116, 116)),
                _ => new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
        }

        private static Brush GetMessageBrush(StatusLogLevel level)
        {
            return level switch
            {
                StatusLogLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 224, 168)),
                StatusLogLevel.Error => new SolidColorBrush(Color.FromRgb(255, 198, 198)),
                _ => new SolidColorBrush(Color.FromRgb(231, 231, 231))
            };
        }

        private void RefreshBackgroundAgentControls()
        {
            suppressBackgroundAgentCheckboxEvents = true;
            RunAtStartupCheckBox.IsChecked = SaveFile.Data.FileHivemind.RunAgentAtStartup;
            RemotePollIntervalTextBox.Text = SaveFile.Data.FileHivemind.RemotePollIntervalSeconds.ToString();
            suppressBackgroundAgentCheckboxEvents = false;

            bool runAtStartup = SaveFile.Data.FileHivemind.RunAgentAtStartup;
            bool agentRunning = backgroundAgentLauncher.IsAgentRunning();
            bool registered = backgroundAgentLauncher.IsRegistered();
            int pollIntervalSeconds = SaveFile.Data.FileHivemind.RemotePollIntervalSeconds;
            BackgroundAgentStatusTextBlock.Text = runAtStartup
                ? agentRunning
                    ? $"Background sync agent is running for this Windows session and polling Google Drive every {pollIntervalSeconds} seconds."
                    : registered
                        ? $"Background sync is enabled and will launch automatically after Windows sign-in. Current poll interval: {pollIntervalSeconds} seconds."
                        : $"Background sync is enabled, but the Windows startup entry is not registered yet. Current poll interval: {pollIntervalSeconds} seconds."
                : agentRunning
                    ? $"Background sync agent is still running in this session, but Windows auto-start is disabled. Current poll interval: {pollIntervalSeconds} seconds."
                    : $"Background sync agent is disabled. Current poll interval: {pollIntervalSeconds} seconds.";
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

        private void PersistConnection(FileHivemindConnectionProfile connection)
        {
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
                Window waitOwner = ResolveOwnerWindow();
                await WaitForm.ShowFormAsync(title, waitOwner);
                try
                {
                    EnsureMountedIfEnabled(connection);
                    GoogleDriveSyncSummary summary = await operation(connection.GoogleDrive);
                    AppendStatus("Refreshing only the AO assets touched by this sync.", StatusLogLevel.Action, connection.EffectiveDisplayName);
                    RefreshMountedAssets(connection, summary.LocalChanges);
                    await TryRefreshRuntimeStateAsync(connection, summary.KnownRemoteItemIds);
                    SaveFile.Save();
                    UpdateSelectedConnectionDetails(connection);
                    AppendStatus(BuildSummaryMessage(summary), StatusLogLevel.Success, connection.EffectiveDisplayName);
                    return true;
                }
                finally
                {
                    await WaitForm.CloseFormAsync();
                }
            }
            catch (Exception ex)
            {
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
            IEnumerable<string>? additionalKnownItemIds = null)
        {
            try
            {
                GoogleDriveConnectionRuntimeState runtimeState = await remoteChangeTracker.CaptureRuntimeStateAsync(
                    connection.Id,
                    connection.GoogleDrive,
                    CancellationToken.None);
                runtimeState = GoogleDriveRemoteChangeTracker.MergeKnownRemoteItemIds(runtimeState, additionalKnownItemIds);
                runtimeState.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
                runtimeState.LastErrorMessage = string.Empty;
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

                MessageBoxResult signInDecision = OceanyaMessageBox.Show(
                    owner,
                    "Oceanya will import this shared connection, open Google sign-in, create the local mirror automatically, and sync it once. Continue?",
                    "Import Hivemind Connection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (signInDecision != MessageBoxResult.Yes)
                {
                    AppendStatus("Connection import was canceled before Google sign-in.", StatusLogLevel.Warning);
                    return;
                }

                AppendStatus("Opening browser for Google Drive sign-in to finish the imported connection.", StatusLogLevel.Action);
                GoogleDriveUserInfo user = await syncService.SignInAsync(importConnection.GoogleDrive, CancellationToken.None);
                BringHostWindowToFront();
                PersistConnection(importConnection);
                AppendStatus(
                    "Imported connection signed in as "
                    + (string.IsNullOrWhiteSpace(user.EmailAddress) ? user.DisplayName : user.EmailAddress)
                    + ".",
                    StatusLogLevel.Success,
                    importConnection.EffectiveDisplayName);

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

                string fileText = FileHivemindConnectionExchangeSerializer.Serialize(connection);
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
                "Delete the selected hivemind connection and clear its saved Google token?\n\n" + connection.EffectiveDisplayName,
                "Delete Connection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            syncService.SignOut(connection.GoogleDrive);
            SaveFile.Data.FileHivemind.Connections.Remove(connection);
            SaveFile.Data.FileHivemind.SelectedConnectionId = SaveFile.Data.FileHivemind.Connections.FirstOrDefault()?.Id ?? string.Empty;
            SaveFile.Save();
            runtimeStateStore.Delete(connection.Id);
            RefreshConnections();
            TryApplyBackgroundAgentPreferences(ensureCurrentSessionAgent: true);
            AppendStatus("Deleted the saved connection and cleared its local token.", StatusLogLevel.Warning, connection.EffectiveDisplayName);
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

        private void SelectedConnectionLocalTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                TryOpenSelectedLocalFolder();
            }
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
            imported.GoogleDrive.TokenStoreKey = string.IsNullOrWhiteSpace(existingConnection.GoogleDrive.TokenStoreKey)
                ? Guid.NewGuid().ToString("N")
                : existingConnection.GoogleDrive.TokenStoreKey.Trim();
            if (!string.IsNullOrWhiteSpace(existingConnection.GoogleDrive.LocalFolderPath))
            {
                imported.GoogleDrive.LocalFolderPath = existingConnection.GoogleDrive.LocalFolderPath.Trim();
            }

            return imported;
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
                        WaitForm.SetSubtitle(connection.EffectiveDisplayName + ": preparing sync...");
                        EnsureMountedIfEnabled(connection);
                        GoogleDriveSyncSummary summary = await operation(connection);
                        AppendStatus("Refreshing only the AO assets touched by this sync.", StatusLogLevel.Action, connection.EffectiveDisplayName);
                        RefreshMountedAssets(connection, summary.LocalChanges);
                        await TryRefreshRuntimeStateAsync(connection, summary.KnownRemoteItemIds);
                        AppendStatus(BuildSummaryMessage(summary), StatusLogLevel.Success, connection.EffectiveDisplayName);
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

        private void OceanyanFileHivemindWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshBackgroundAgentControls();
            EnsureBackgroundLogFeedRunning();

            if (hasRaisedFinishedLoading)
            {
                return;
            }

            hasRaisedFinishedLoading = true;
            FinishedLoading?.Invoke();
        }

        private void OceanyanFileHivemindWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            backgroundLogTimer.Stop();
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
            AppendStatus(
                "[Agent] " + entry.Message,
                MapBackgroundLogLevel(entry.Level),
                string.IsNullOrWhiteSpace(entry.ConnectionName) ? null : entry.ConnectionName);
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
