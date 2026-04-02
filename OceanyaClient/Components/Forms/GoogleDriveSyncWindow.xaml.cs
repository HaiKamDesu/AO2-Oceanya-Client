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
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    public partial class GoogleDriveSyncWindow : OceanyaWindowContentControl, Features.Startup.IStartupFunctionalityWindow
    {
        private enum StatusLogLevel
        {
            Info,
            Action,
            Success,
            Warning,
            Error
        }

        private const string StoredSecretMaskValue = "oceanya_stored_secret";
        private readonly GoogleDriveSyncService syncService;
        private readonly GoogleDriveRemoteChangeTracker remoteChangeTracker;
        private readonly GoogleDriveConnectionRuntimeStateStore runtimeStateStore;
        private readonly GoogleDriveSecureClientCredentialStore credentialStore;
        private readonly FileHivemindConnectionProfile connection;
        private readonly Action<FileHivemindConnectionProfile> persistConnection;
        private readonly bool isDraftConnection;
        private bool suppressSecretPasswordEvents;
        private bool showingStoredSecretPlaceholder;
        private bool hasRaisedFinishedLoading;
        private bool hasPersistedConnection;
        public event Action? FinishedLoading;

        public GoogleDriveSyncWindow(
            FileHivemindConnectionProfile? connection = null,
            Action<FileHivemindConnectionProfile>? persistConnection = null,
            GoogleDriveSyncService? syncService = null,
            bool isDraftConnection = false)
        {
            InitializeComponent();
            this.syncService = syncService ?? new GoogleDriveSyncService();
            remoteChangeTracker = new GoogleDriveRemoteChangeTracker();
            runtimeStateStore = new GoogleDriveConnectionRuntimeStateStore();
            credentialStore = new GoogleDriveSecureClientCredentialStore();
            this.connection = connection ?? FileHivemindConnectionProfile.CreateGoogleDriveProfile();
            this.persistConnection = persistConnection ?? (_ => SaveFile.Save());
            this.isDraftConnection = isDraftConnection;
            Title = "Google Drive Connection";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Loaded += GoogleDriveSyncWindow_Loaded;
            LoadFromSettings();
        }

        /// <inheritdoc/>
        public override string HeaderText => "GOOGLE DRIVE CONNECTION";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        private GoogleDriveSyncSettings Settings => connection.GoogleDrive;

        private void LoadFromSettings()
        {
            ConnectionNameTextBox.Text = connection.DisplayName;
            GoogleCloudClientIdTextBox.Text = Settings.OAuthClientId;
            RestoreStoredSecretPlaceholderIfNeeded();
            RemoteFolderTextBox.Text = Settings.RemoteFolderId;
            LocalFolderTextBox.Text = Settings.LocalFolderPath;
            AutoAddMountPathCheckBox.IsChecked = Settings.AutoAddMountPath;
            MirrorDeletesCheckBox.IsChecked = Settings.MirrorDeletes;
            UseExistingMountPathCheckBox.IsChecked = Settings.UseExistingMountPath;
            UpdateGoogleCloudConfigurationStatus();
            UpdateAccountStatus();
            UpdateRemoteFolderStatus();
            AppendStatus("Google Drive connection window loaded.", StatusLogLevel.Success);
            if (!GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                    Settings,
                    out _,
                    out string errorMessage,
                    credentialStore,
                    allowLegacyFallback: false))
            {
                AppendStatus(errorMessage, StatusLogLevel.Warning);
            }
        }

        private bool PersistSettings(bool forcePersist)
        {
            string previousClientId = Settings.OAuthClientId?.Trim() ?? string.Empty;
            bool hadStoredSecret = HasSavedStoredSecretForCurrentClientId(previousClientId);
            Settings.OAuthClientId = GoogleCloudClientIdTextBox.Text?.Trim() ?? string.Empty;
            GoogleDriveConnectionCredentialSupport.EnsureSecretStoreKey(Settings);

            string typedClientSecret = GetTypedClientSecret();
            bool clientIdChanged = !string.Equals(previousClientId, Settings.OAuthClientId, StringComparison.Ordinal);
            bool credentialsChanged = clientIdChanged;

            if (clientIdChanged && hadStoredSecret && string.IsNullOrWhiteSpace(typedClientSecret))
            {
                GoogleDriveConnectionCredentialSupport.DeleteStoredSecret(Settings, credentialStore);
                hadStoredSecret = false;
            }

            if (string.IsNullOrWhiteSpace(Settings.OAuthClientId) && hadStoredSecret && string.IsNullOrWhiteSpace(typedClientSecret))
            {
                GoogleDriveConnectionCredentialSupport.DeleteStoredSecret(Settings, credentialStore);
                hadStoredSecret = false;
                credentialsChanged = true;
            }

            if (!string.IsNullOrWhiteSpace(typedClientSecret))
            {
                Settings.OAuthClientSecret = typedClientSecret;
                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(Settings, credentialStore);
                credentialsChanged = true;
            }

            RestoreStoredSecretPlaceholderIfNeeded();
            bool hasStoredSecret = HasSavedStoredSecretForCurrentClientId(Settings.OAuthClientId?.Trim() ?? string.Empty);
            if (hadStoredSecret != hasStoredSecret)
            {
                credentialsChanged = true;
            }

            Settings.TokenStoreKey = string.IsNullOrWhiteSpace(Settings.TokenStoreKey)
                ? Guid.NewGuid().ToString("N")
                : Settings.TokenStoreKey.Trim();

            string previousFolderId = Settings.RemoteFolderId?.Trim() ?? string.Empty;
            string previousLocalFolderPath = Settings.LocalFolderPath?.Trim() ?? string.Empty;
            string parsedFolderId = GoogleDriveInviteSerializer.ExtractFolderId(RemoteFolderTextBox.Text);
            Settings.RemoteFolderId = !string.IsNullOrWhiteSpace(parsedFolderId)
                ? parsedFolderId
                : (RemoteFolderTextBox.Text?.Trim() ?? string.Empty);
            if (!string.Equals(previousFolderId, Settings.RemoteFolderId, StringComparison.OrdinalIgnoreCase))
            {
                Settings.RemoteFolderName = string.Empty;
                runtimeStateStore.Delete(connection.Id);
            }

            Settings.LocalFolderPath = LocalFolderTextBox.Text?.Trim() ?? string.Empty;
            if (!string.Equals(previousLocalFolderPath, Settings.LocalFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                runtimeStateStore.Delete(connection.Id);
            }
            if (Settings.IsOceanyaManagedLocalFolder)
            {
                GoogleDriveManagedLocalFolderMarkerService.EnsureMarkerIfNeeded(Settings);
            }

            Settings.AutoAddMountPath = AutoAddMountPathCheckBox.IsChecked != false;
            Settings.MirrorDeletes = MirrorDeletesCheckBox.IsChecked != false;
            Settings.UseExistingMountPath = UseExistingMountPathCheckBox.IsChecked == true;
            connection.ProviderId = FileHivemindProviderIds.GoogleDrive;
            connection.DisplayName = ResolveConnectionDisplayName();
            UpdateGoogleCloudConfigurationStatus();
            UpdateRemoteFolderStatus();

            if (credentialsChanged)
            {
                syncService.SignOut(Settings);
                runtimeStateStore.Delete(connection.Id);
            }

            if (!forcePersist && isDraftConnection && !hasPersistedConnection && !HasMeaningfulConfiguration())
            {
                return false;
            }

            persistConnection(connection);
            hasPersistedConnection = true;
            return true;
        }

        private bool HasMeaningfulConfiguration()
        {
            return !string.IsNullOrWhiteSpace(ConnectionNameTextBox.Text)
                || !string.IsNullOrWhiteSpace(GoogleCloudClientIdTextBox.Text)
                || HasTypedClientSecretInput()
                || GoogleDriveConnectionCredentialSupport.HasStoredSecret(Settings, credentialStore)
                || !string.IsNullOrWhiteSpace(Settings.LastSignedInEmail)
                || !string.IsNullOrWhiteSpace(Settings.LastSignedInDisplayName)
                || !string.IsNullOrWhiteSpace(Settings.RemoteFolderId)
                || !string.IsNullOrWhiteSpace(Settings.RemoteFolderName)
                || !string.IsNullOrWhiteSpace(Settings.LocalFolderPath);
        }

        private string ResolveConnectionDisplayName()
        {
            string enteredName = ConnectionNameTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(enteredName))
            {
                return enteredName;
            }

            if (!string.IsNullOrWhiteSpace(Settings.RemoteFolderName))
            {
                return Settings.RemoteFolderName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Settings.LastSignedInEmail))
            {
                return "Drive Connection (" + Settings.LastSignedInEmail.Trim() + ")";
            }

            if (!string.IsNullOrWhiteSpace(Settings.RemoteFolderId))
            {
                return "Drive " + Settings.RemoteFolderId.Trim();
            }

            return string.Empty;
        }

        private void UpdateGoogleCloudConfigurationStatus()
        {
            string clientId = GoogleCloudClientIdTextBox.Text?.Trim() ?? string.Empty;
            bool hasTypedSecret = HasTypedClientSecretInput();
            bool hasStoredSecret = HasSavedStoredSecretForCurrentClientId(clientId);
            VerifyCloudCredentialsButton.IsEnabled = !string.IsNullOrWhiteSpace(clientId) && (hasTypedSecret || hasStoredSecret);

            if (string.IsNullOrWhiteSpace(clientId))
            {
                SignInButton.IsEnabled = false;
                CloudCredentialStatusTextBlock.Text = hasTypedSecret
                    ? "A client secret is currently typed in, but the Google Cloud client ID is still missing."
                    : GoogleDriveConnectionCredentialSupport.BuildStatusMessage(
                        Settings,
                        credentialStore,
                        allowLegacyFallback: false);
                return;
            }

            SignInButton.IsEnabled = hasTypedSecret || hasStoredSecret;
            if (hasTypedSecret)
            {
                CloudCredentialStatusTextBlock.Text =
                    "A replacement client secret is currently typed in. Save, Verify, or Sign In to store it locally for this connection.";
                return;
            }

            CloudCredentialStatusTextBlock.Text = GoogleDriveConnectionCredentialSupport.BuildStatusMessage(
                Settings,
                credentialStore,
                allowLegacyFallback: false)
                + (hasStoredSecret
                    ? " Exported connection files carry an obfuscated copy of these app credentials for trusted recipients."
                    : string.Empty);
        }

        private void UpdateAccountStatus()
        {
            string email = Settings.LastSignedInEmail?.Trim() ?? string.Empty;
            string displayName = Settings.LastSignedInDisplayName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
            {
                AccountStatusTextBlock.Text = "Not signed in.";
                return;
            }

            string label = string.IsNullOrWhiteSpace(displayName) ? email : displayName + " <" + email + ">";
            string lastSync = Settings.LastSyncUtc.HasValue
                ? " | Last sync: " + Settings.LastSyncUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
            AccountStatusTextBlock.Text = "Signed in as " + label + lastSync;
        }

        private void AppendStatus(string message, StatusLogLevel level = StatusLogLevel.Info)
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

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                EnsureGoogleAuthConfigured();
                AppendStatus("Opening browser for Google Drive sign-in...", StatusLogLevel.Action);
                GoogleDriveUserInfo user = await syncService.SignInAsync(Settings, CancellationToken.None);
                BringHostWindowToFront();
                PersistSettings(forcePersist: true);
                UpdateAccountStatus();
                AppendStatus(
                    "Signed in as " + (string.IsNullOrWhiteSpace(user.EmailAddress) ? user.DisplayName : user.EmailAddress) + ".",
                    StatusLogLevel.Success);
            }
            catch (Exception ex)
            {
                BringHostWindowToFront();
                AppendStatus("Sign-in failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Google Drive sign-in failed:\n" + ex.Message,
                    "Google Drive Sign-In",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            PersistSettings(forcePersist: true);
            syncService.SignOut(Settings);
            runtimeStateStore.Delete(connection.Id);
            PersistSettings(forcePersist: true);
            UpdateAccountStatus();
            AppendStatus("Signed out from Google Drive token storage.", StatusLogLevel.Warning);
        }

        private async void CreateManagedFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                await TryPopulateRemoteFolderNameAsync();

                string managedPath = GoogleDriveClientAssetIntegration.BuildManagedLocalFolderPath(
                    ResolveConnectionDisplayName(),
                    Settings.RemoteFolderName,
                    Settings.RemoteFolderId);
                Directory.CreateDirectory(managedPath);
                Settings.IsOceanyaManagedLocalFolder = true;
                GoogleDriveManagedLocalFolderMarkerService.EnsureMarker(managedPath);
                LocalFolderTextBox.Text = managedPath;
                PersistSettings(forcePersist: true);
                AppendStatus("Selected generated local sync folder: " + managedPath, StatusLogLevel.Action);
            }
            catch (Exception ex)
            {
                AppendStatus("Automatic local folder generation failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not generate the local sync folder:\n" + ex.Message,
                    "Generate Local Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void VerifyRemoteFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                EnsureGoogleAuthConfigured();
                if (string.IsNullOrWhiteSpace(Settings.RemoteFolderId))
                {
                    throw new InvalidOperationException("Enter a Google Drive folder ID or share link first.");
                }

                AppendStatus("Verifying the Google Drive folder...", StatusLogLevel.Action);
                string remoteFolderName = await ReadRemoteFolderNameAsync(forceRefresh: true);
                AppendStatus(
                    "Verified remote folder '" + remoteFolderName + "' (" + Settings.RemoteFolderId + ").",
                    StatusLogLevel.Success);
            }
            catch (Exception ex)
            {
                UpdateRemoteFolderStatus(errorMessage: ex.Message);
                AppendStatus("Remote folder verification failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not verify the Google Drive folder:\n" + ex.Message,
                    "Verify Google Drive Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void VerifyCloudCredentialsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                if (!GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                        Settings,
                        out GoogleDriveOAuthClientConfiguration configuration,
                        out string errorMessage,
                        credentialStore,
                        allowLegacyFallback: false))
                {
                    throw new InvalidOperationException(errorMessage);
                }

                AppendStatus("Verifying Google Cloud credentials...", StatusLogLevel.Action);
                await syncService.VerifyClientConfigurationAsync(configuration, CancellationToken.None);
                CloudCredentialStatusTextBlock.Text =
                    "Verified. Google accepted this Desktop app client configuration. Final end-to-end verification still happens when a user signs into Google.";
                AppendStatus("Google Cloud credentials verified.", StatusLogLevel.Success);
            }
            catch (Exception ex)
            {
                CloudCredentialStatusTextBlock.Text = "Verification failed: " + ex.Message;
                AppendStatus("Google Cloud credential verification failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not verify the Google Cloud credentials:\n" + ex.Message,
                    "Verify Google Cloud Credentials",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BrowseLocalFolderButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "Select Google Drive local sync folder"
            };
            if (dialog.ShowDialog() == true)
            {
                Settings.IsOceanyaManagedLocalFolder = false;
                LocalFolderTextBox.Text = dialog.FolderName;
                PersistSettings(forcePersist: true);
                AppendStatus("Selected local sync folder: " + dialog.FolderName, StatusLogLevel.Action);
            }
        }

        private void OpenLocalFolderButton_Click(object sender, RoutedEventArgs e)
        {
            PersistSettings(forcePersist: true);
            if (string.IsNullOrWhiteSpace(Settings.LocalFolderPath) || !Directory.Exists(Settings.LocalFolderPath))
            {
                AppendStatus("No local sync folder is available to open.", StatusLogLevel.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = Settings.LocalFolderPath,
                UseShellExecute = true
            });
            AppendStatus("Opened the local sync folder in the system file explorer.", StatusLogLevel.Info);
        }

        private void MoveLocalFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                string sourcePath = Settings.LocalFolderPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    AppendStatus("Set a local sync folder before trying to move it.", StatusLogLevel.Warning);
                    return;
                }

                string normalizedSourcePath = Path.GetFullPath(sourcePath);
                if (!Directory.Exists(normalizedSourcePath))
                {
                    AppendStatus("The current local sync folder does not exist.", StatusLogLevel.Warning);
                    return;
                }

                OpenFolderDialog dialog = new OpenFolderDialog
                {
                    Title = "Select destination parent folder for the local mirror"
                };
                if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    return;
                }

                string destinationParent = Path.GetFullPath(dialog.FolderName.Trim());
                string folderName = Path.GetFileName(normalizedSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    folderName = new DirectoryInfo(normalizedSourcePath).Name;
                }

                string destinationPath = Path.Combine(destinationParent, folderName);
                if (string.Equals(
                    normalizedSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    AppendStatus("The local sync folder is already using that destination.", StatusLogLevel.Warning);
                    return;
                }

                string normalizedSourcePrefix = normalizedSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string normalizedDestinationPrefix = destinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (normalizedDestinationPrefix.StartsWith(normalizedSourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AppendStatus("The destination cannot be inside the current local sync folder.", StatusLogLevel.Warning);
                    return;
                }

                if (Directory.Exists(destinationPath) && Directory.EnumerateFileSystemEntries(destinationPath).Any())
                {
                    MessageBoxResult mergeResult = OceanyaMessageBox.Show(
                        ResolveOwnerWindow(),
                        "The destination folder already contains files. Merge into it and overwrite matching files?",
                        "Move Local Folder",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (mergeResult != MessageBoxResult.Yes)
                    {
                        AppendStatus("Local folder move was canceled because the destination already contains files.", StatusLogLevel.Warning);
                        return;
                    }
                }

                AppendStatus("Moving local sync folder...", StatusLogLevel.Action);
                MoveDirectoryWithFallback(normalizedSourcePath, destinationPath);
                if (Settings.IsOceanyaManagedLocalFolder)
                {
                    GoogleDriveManagedLocalFolderMarkerService.EnsureMarker(destinationPath);
                }
                LocalFolderTextBox.Text = destinationPath;
                PersistSettings(forcePersist: true);
                UpdateMountPathAfterLocalFolderMove(normalizedSourcePath, destinationPath);
                AppendStatus("Moved the local sync folder to " + destinationPath + ".", StatusLogLevel.Success);
            }
            catch (Exception ex)
            {
                AppendStatus("Local folder move failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Could not move the local sync folder:\n" + ex.Message,
                    "Move Local Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void EnsureMountPathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                EnsureMounted();
                AppendStatus("AO mount path configuration is ready.", StatusLogLevel.Success);
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

        private async void PullFromDriveButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteSyncOperationAsync(
                "Syncing from Google Drive...",
                async () =>
                {
                    GoogleDriveSyncSummary summary = await syncService.PullFromDriveAsync(
                        Settings,
                        subtitle => WaitForm.SetSubtitle(subtitle),
                        CancellationToken.None);
                    RefreshMountedAssets(summary.LocalChanges);
                    AppendStatus(BuildSummaryMessage("Drive -> Local sync completed", summary), StatusLogLevel.Success);
                    return summary;
                });
        }

        private async void PushToDriveButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteSyncOperationAsync(
                "Publishing local folder to Google Drive...",
                async () =>
                {
                    GoogleDriveSyncSummary summary = await syncService.PushLocalFolderAsync(
                        Settings,
                        subtitle => WaitForm.SetSubtitle(subtitle),
                        CancellationToken.None);
                    RefreshMountedAssets(summary.LocalChanges);
                    AppendStatus(BuildSummaryMessage("Local -> Drive publish completed", summary), StatusLogLevel.Success);
                    return summary;
                });
        }

        private async void RefreshAssetsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PersistSettings(forcePersist: true);
                string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(configIniPath))
                {
                    return;
                }

                Globals.UpdateConfigINI(configIniPath);
                Window refreshOwner = ResolveOwnerWindow();
                await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(refreshOwner);
                AppendStatus("AO asset indexes were refreshed.", StatusLogLevel.Success);
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

        private void SaveConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            PersistSettings(forcePersist: true);
            AppendStatus("Connection settings saved.", StatusLogLevel.Success);
        }

        private async Task ExecuteSyncOperationAsync(string title, Func<Task<GoogleDriveSyncSummary>> action)
        {
            using FileHivemindConnectionExecutionLock? executionLock =
                FileHivemindConnectionExecutionLock.TryAcquire(connection.Id, TimeSpan.Zero);
            if (executionLock == null)
            {
                AppendStatus(
                    "Skipped because this connection is already being synced by another Oceanya process.",
                    StatusLogLevel.Warning);
                return;
            }

            try
            {
                PersistSettings(forcePersist: true);
                ValidateSyncInputs();
                EnsureMounted();
                AppendStatus(title, StatusLogLevel.Action);

                Window waitOwner = ResolveOwnerWindow();
                await WaitForm.ShowFormAsync(title, waitOwner);
                try
                {
                    GoogleDriveSyncSummary summary = await action();
                    await TryRefreshRuntimeStateAsync(summary.KnownRemoteItemIds);
                    PersistSettings(forcePersist: true);
                    UpdateAccountStatus();
                }
                finally
                {
                    await WaitForm.CloseFormAsync();
                }
            }
            catch (Exception ex)
            {
                AppendStatus("Google Drive sync failed: " + ex.Message, StatusLogLevel.Error);
                OceanyaMessageBox.Show(
                    ResolveOwnerWindow(),
                    "Google Drive sync failed:\n" + ex.Message,
                    "Google Drive Sync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task TryRefreshRuntimeStateAsync(IEnumerable<string>? additionalKnownItemIds = null)
        {
            try
            {
                GoogleDriveConnectionRuntimeState runtimeState = await remoteChangeTracker.CaptureRuntimeStateAsync(
                    connection.Id,
                    Settings,
                    CancellationToken.None);
                runtimeState = GoogleDriveRemoteChangeTracker.MergeKnownRemoteItemIds(runtimeState, additionalKnownItemIds);
                runtimeState.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
                runtimeState.LastErrorMessage = string.Empty;
                runtimeStateStore.Save(runtimeState);
            }
            catch (Exception ex)
            {
                AppendStatus(
                    "Background change tracking state could not be refreshed after this sync: " + ex.Message,
                    StatusLogLevel.Warning);
            }
        }

        private void ValidateSyncInputs()
        {
            EnsureGoogleAuthConfigured();

            if (string.IsNullOrWhiteSpace(Settings.RemoteFolderId))
            {
                throw new InvalidOperationException("Enter a Google Drive folder ID or share link first.");
            }

            if (string.IsNullOrWhiteSpace(Settings.LocalFolderPath))
            {
                throw new InvalidOperationException("Select or create a local sync folder first.");
            }
        }

        private void EnsureGoogleAuthConfigured()
        {
            if (!GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                    Settings,
                    out _,
                    out string errorMessage,
                    credentialStore,
                    allowLegacyFallback: false))
            {
                throw new InvalidOperationException(
                    errorMessage + " Setup: create/select a Google Cloud project, enable Google Drive API, " +
                    "create a Desktop app OAuth client, then paste the client ID and client secret into this connection.");
            }
        }

        private void EnsureMounted()
        {
            if (AutoAddMountPathCheckBox.IsChecked == false)
            {
                return;
            }

            string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
            GoogleDriveClientAssetIntegration.EnsureMounted(
                configIniPath,
                Settings,
                Settings.UseExistingMountPath,
                message => AppendStatus(message, StatusLogLevel.Info));
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

        private void RefreshMountedAssets(GoogleDriveSyncLocalChangeSet? localChanges = null)
        {
            string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
            GoogleDriveClientAssetIntegration.RefreshMountedAssets(
                configIniPath,
                Settings,
                localChanges,
                subtitle => WaitForm.SetSubtitle(subtitle));
        }

        private async Task TryPopulateRemoteFolderNameAsync()
        {
            if (!string.IsNullOrWhiteSpace(Settings.RemoteFolderName) || string.IsNullOrWhiteSpace(Settings.RemoteFolderId))
            {
                UpdateRemoteFolderStatus();
                return;
            }

            try
            {
                _ = await ReadRemoteFolderNameAsync(forceRefresh: false);
            }
            catch (Exception ex)
            {
                UpdateRemoteFolderStatus(errorMessage: ex.Message);
                AppendStatus("Could not read the remote Drive folder name. Falling back to the folder ID. " + ex.Message, StatusLogLevel.Warning);
            }
        }

        private async Task<string> ReadRemoteFolderNameAsync(bool forceRefresh)
        {
            if (string.IsNullOrWhiteSpace(Settings.RemoteFolderId))
            {
                UpdateRemoteFolderStatus();
                return string.Empty;
            }

            if (!forceRefresh && !string.IsNullOrWhiteSpace(Settings.RemoteFolderName))
            {
                UpdateRemoteFolderStatus();
                return Settings.RemoteFolderName.Trim();
            }

            string remoteFolderName = await syncService.GetRemoteFolderNameAsync(Settings, CancellationToken.None);
            Settings.RemoteFolderName = remoteFolderName?.Trim() ?? string.Empty;
            PersistSettings(forcePersist: true);
            UpdateRemoteFolderStatus();
            return Settings.RemoteFolderName;
        }

        private void RemoteFolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string enteredFolderId = GetEnteredRemoteFolderId();
            string storedFolderId = Settings.RemoteFolderId?.Trim() ?? string.Empty;
            if (!string.Equals(storedFolderId, enteredFolderId, StringComparison.OrdinalIgnoreCase))
            {
                Settings.RemoteFolderName = string.Empty;
            }

            UpdateRemoteFolderStatus();
        }

        private void GoogleCloudCredentialFields_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsCurrentClientIdMatchingSaved() && showingStoredSecretPlaceholder)
            {
                ClearStoredSecretPlaceholder();
            }
            else if (IsCurrentClientIdMatchingSaved())
            {
                RestoreStoredSecretPlaceholderIfNeeded();
            }

            UpdateGoogleCloudConfigurationStatus();
        }

        private void GoogleCloudClientSecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (suppressSecretPasswordEvents)
            {
                return;
            }

            showingStoredSecretPlaceholder = false;
            UpdateGoogleCloudConfigurationStatus();
        }

        private void GoogleCloudClientSecretPasswordBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
        {
            ClearStoredSecretPlaceholder();
        }

        private void GoogleCloudClientSecretPasswordBox_LostKeyboardFocus(object sender, RoutedEventArgs e)
        {
            RestoreStoredSecretPlaceholderIfNeeded();
            UpdateGoogleCloudConfigurationStatus();
        }

        private bool HasTypedClientSecretInput()
        {
            return !showingStoredSecretPlaceholder
                && !string.IsNullOrWhiteSpace(GoogleCloudClientSecretPasswordBox.Password?.Trim());
        }

        private string GetTypedClientSecret()
        {
            return showingStoredSecretPlaceholder
                ? string.Empty
                : (GoogleCloudClientSecretPasswordBox.Password?.Trim() ?? string.Empty);
        }

        private bool IsCurrentClientIdMatchingSaved()
        {
            string currentClientId = GoogleCloudClientIdTextBox.Text?.Trim() ?? string.Empty;
            string savedClientId = Settings.OAuthClientId?.Trim() ?? string.Empty;
            return string.Equals(currentClientId, savedClientId, StringComparison.Ordinal);
        }

        private bool HasSavedStoredSecretForCurrentClientId(string? currentClientId)
        {
            string savedClientId = Settings.OAuthClientId?.Trim() ?? string.Empty;
            if (!string.Equals(savedClientId, currentClientId?.Trim() ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            return GoogleDriveConnectionCredentialSupport.HasStoredSecret(Settings, credentialStore);
        }

        private void ClearStoredSecretPlaceholder()
        {
            if (!showingStoredSecretPlaceholder)
            {
                return;
            }

            suppressSecretPasswordEvents = true;
            GoogleCloudClientSecretPasswordBox.Password = string.Empty;
            suppressSecretPasswordEvents = false;
            showingStoredSecretPlaceholder = false;
        }

        private void RestoreStoredSecretPlaceholderIfNeeded()
        {
            bool hasStoredSecret = HasSavedStoredSecretForCurrentClientId(GoogleCloudClientIdTextBox.Text);
            if (!hasStoredSecret)
            {
                if (showingStoredSecretPlaceholder)
                {
                    suppressSecretPasswordEvents = true;
                    GoogleCloudClientSecretPasswordBox.Password = string.Empty;
                    suppressSecretPasswordEvents = false;
                    showingStoredSecretPlaceholder = false;
                }

                return;
            }

            if (HasTypedClientSecretInput())
            {
                return;
            }

            suppressSecretPasswordEvents = true;
            GoogleCloudClientSecretPasswordBox.Password = StoredSecretMaskValue;
            suppressSecretPasswordEvents = false;
            showingStoredSecretPlaceholder = true;
        }

        private string GetEnteredRemoteFolderId()
        {
            string parsedFolderId = GoogleDriveInviteSerializer.ExtractFolderId(RemoteFolderTextBox.Text);
            return !string.IsNullOrWhiteSpace(parsedFolderId)
                ? parsedFolderId
                : (RemoteFolderTextBox.Text?.Trim() ?? string.Empty);
        }

        private void UpdateRemoteFolderStatus(string? errorMessage = null)
        {
            string rawInput = RemoteFolderTextBox.Text?.Trim() ?? string.Empty;
            string enteredFolderId = GetEnteredRemoteFolderId();
            string storedFolderId = Settings.RemoteFolderId?.Trim() ?? string.Empty;
            string resolvedFolderId = !string.IsNullOrWhiteSpace(enteredFolderId) ? enteredFolderId : storedFolderId;
            string resolvedFolderName = Settings.RemoteFolderName?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                RemoteFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 198, 198));
                RemoteFolderStatusTextBlock.Text = "Verification failed: " + errorMessage;
                return;
            }

            if (string.IsNullOrWhiteSpace(rawInput))
            {
                RemoteFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190));
                RemoteFolderStatusTextBlock.Text =
                    "Paste a Google Drive folder share link or folder ID, then click Verify.";
                return;
            }

            if (string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                RemoteFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 224, 168));
                RemoteFolderStatusTextBlock.Text =
                    "Oceanya could not parse a valid Google Drive folder ID from that value yet.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(resolvedFolderName)
                && string.Equals(storedFolderId, resolvedFolderId, StringComparison.OrdinalIgnoreCase))
            {
                RemoteFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(118, 224, 141));
                RemoteFolderStatusTextBlock.Text =
                    "Verified folder: " + resolvedFolderName + " (" + resolvedFolderId + ")";
                return;
            }

            RemoteFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190));
            RemoteFolderStatusTextBlock.Text =
                "Parsed folder ID: " + resolvedFolderId + ". Click Verify to confirm access and fetch the real folder name.";
        }

        private void UpdateMountPathAfterLocalFolderMove(string oldPath, string newPath)
        {
            if (!Settings.AutoAddMountPath || Settings.UseExistingMountPath)
            {
                return;
            }

            string configIniPath = SaveFile.Data.ConfigIniPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configIniPath) || !File.Exists(configIniPath))
            {
                return;
            }

            bool changed = GoogleDriveMountPathManager.ReplaceMountedPath(configIniPath, oldPath, newPath);
            if (changed)
            {
                Globals.UpdateConfigINI(configIniPath);
                AppendStatus("Updated config.ini mount paths to the moved local folder.", StatusLogLevel.Info);
            }
        }

        private static void MoveDirectoryWithFallback(string sourcePath, string destinationPath)
        {
            string normalizedSource = Path.GetFullPath(sourcePath);
            string normalizedDestination = Path.GetFullPath(destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(normalizedDestination) ?? normalizedDestination);

            if (!Directory.Exists(normalizedDestination))
            {
                try
                {
                    Directory.Move(normalizedSource, normalizedDestination);
                    return;
                }
                catch (IOException)
                {
                    // Fall back to recursive copy/delete for cross-volume moves.
                }
                catch (UnauthorizedAccessException)
                {
                    // Fall back to recursive copy/delete if the direct move path is blocked.
                }
            }

            CopyDirectoryContents(normalizedSource, normalizedDestination);
            Directory.Delete(normalizedSource, true);
        }

        private static void CopyDirectoryContents(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);

            foreach (string directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
            }

            foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourcePath, filePath);
                string targetPath = Path.Combine(destinationPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? destinationPath);
                File.Copy(filePath, targetPath, overwrite: true);
            }
        }

        private Window ResolveOwnerWindow()
        {
            return HostWindow
                ?? Application.Current?.MainWindow
                ?? throw new InvalidOperationException("No available owner window was found.");
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static string BuildSummaryMessage(string prefix, GoogleDriveSyncSummary summary)
        {
            return prefix
                + $" | dirs:{summary.DirectoriesCreated}"
                + $" downloaded:{summary.FilesDownloaded}"
                + $" uploaded:{summary.FilesUploaded}"
                + $" local_del:{summary.LocalFilesDeleted}"
                + $" remote_del:{summary.RemoteFilesDeleted}"
                + $" skipped:{summary.FilesSkipped}";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            PersistSettings(forcePersist: false);
            RequestHostClose(false);
        }

        private void GoogleDriveSyncWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (hasRaisedFinishedLoading)
            {
                return;
            }

            hasRaisedFinishedLoading = true;
            FinishedLoading?.Invoke();
        }
    }
}
