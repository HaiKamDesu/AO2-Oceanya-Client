using System;
using System.IO;
using System.Security.Policy;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NAudio.Wave;
using OceanyaClient.AdvancedFeatures;
using OceanyaClient.Components;
using OceanyaClient.Utilities;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace OceanyaClient
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<ToggleButton, AOClient> clients = new Dictionary<ToggleButton, AOClient>();
        private AOClient? currentClient;
        private AOClient? singleInternalClient;
        private AOClient? boundSingleClientProfile;
        private readonly bool useSingleInternalClient = SaveFile.Data.UseSingleInternalClient;
        private bool debug = false;
        private readonly HashSet<AOClient> areaListBootstrapCompletedClients = new HashSet<AOClient>();
        private readonly Brush areaFreeBrush = new SolidColorBrush(Color.FromRgb(77, 77, 77));
        private readonly Brush areaLfpBrush = new SolidColorBrush(Color.FromRgb(76, 112, 63));
        private readonly Brush areaCasingBrush = new SolidColorBrush(Color.FromRgb(113, 92, 53));
        private readonly Brush areaRecessBrush = new SolidColorBrush(Color.FromRgb(84, 84, 110));
        private readonly Brush areaRpBrush = new SolidColorBrush(Color.FromRgb(108, 69, 116));
        private readonly Brush areaGamingBrush = new SolidColorBrush(Color.FromRgb(58, 116, 116));
        private readonly Brush areaLockedBrush = new SolidColorBrush(Color.FromRgb(106, 54, 54));
        private bool isLoadingDreddOverlaySelection;
        private bool isDreddFeatureEnabled;
        private const string DreddNoneOverlayName = "none";
        private static readonly Key[] KonamiCodeSequence = new[]
        {
            Key.Up,
            Key.Up,
            Key.Down,
            Key.Down,
            Key.Left,
            Key.Right,
            Key.Left,
            Key.Right,
            Key.B,
            Key.A
        };
        private int konamiProgress;
        private string lastDreddOverlayContextKey = string.Empty;
        private string lastUnknownOverlayPromptKey = string.Empty;

        private sealed class DreddOverlaySelectionItem
        {
            public string Name { get; set; } = string.Empty;
            public string DisplayText { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public bool IsNone { get; set; }
            public bool IsTransient { get; set; }
        }

        List<ToggleButton> objectionModifiers;
        public MainWindow()
        {
            AudioPlayer.PlayEmbeddedSound("Resources/ApertureScienceJingleHD.mp3", 0.5f);

            InitializeComponent();
            WindowHelper.AddWindow(this);

            objectionModifiers = new List<ToggleButton> { HoldIt, Objection, TakeThat, Custom };
            // Set grid mode and size
            EmoteGrid.SetScrollMode(PageButtonGrid.ScrollMode.Vertical);
            EmoteGrid.SetPageSize(4, 1);
            OOCLogControl.IsEnabled = false;
            ICLogControl.IsEnabled = false;
            ICMessageSettingsControl.IsEnabled = false;
            OOCLogControl.LogKeyResolver = ResolveLogClientKey;
            ICLogControl.LogKeyResolver = ResolveLogClientKey;

            OOCLogControl.OnSendOOCMessage += async (showName, message) =>
            {
                SaveFile.Data.OOCName = showName;
                SaveFile.Save();

                AOClient? networkClient = GetTargetClientForNetwork(currentClient);
                if (networkClient == null)
                {
                    return;
                }
                if (useSingleInternalClient)
                {
                    if (currentClient != null)
                    {
                        currentClient.OOCShowname = showName;
                        ApplyProfileToSingleInternalClient(currentClient);
                    }
                }

                await networkClient.SendOOCMessage(showName, message);
            };

            ICMessageSettingsControl.OnSendICMessage += async (message) =>
            {
                // Split the message to get the client name and the actual message
                var splitMessage = message.Split(new[] { ':' }, 2);
                AOClient? client = null;
                var sendMessage = message;
                if (splitMessage.Length == 2)
                {
                    var clientName = splitMessage[0].Trim();
                    var actualMessage = splitMessage[1].Trim();

                    // Find the client by name (case insensitive)
                    var targetClient = clients.Values.FirstOrDefault(bot => string.Equals(bot.clientName, clientName, StringComparison.OrdinalIgnoreCase));

                    if (targetClient != null)
                    {
                        sendMessage = actualMessage;
                        client = targetClient;
                        SelectClient(client);
                    }
                    else
                    {
                        client = currentClient;
                    }
                }
                else
                {
                    client = currentClient;
                }

                if (!string.IsNullOrWhiteSpace(sendMessage))
                {
                    sendMessage = sendMessage.Trim();
                }
                else if(sendMessage == "")
                {
                    sendMessage = " ";
                }

                if (client == null)
                {
                    return;
                }

                client.shoutModifiers = ICMessage.ShoutModifiers.Nothing;

                if (HoldIt.IsChecked == true)
                {
                    client.shoutModifiers = ICMessage.ShoutModifiers.HoldIt;
                }
                else if (Objection.IsChecked == true)
                {
                    client.shoutModifiers = ICMessage.ShoutModifiers.Objection;
                }
                else if (TakeThat.IsChecked == true)
                {
                    client.shoutModifiers = ICMessage.ShoutModifiers.TakeThat;
                }
                else if (Custom.IsChecked == true)
                {
                    client.shoutModifiers = ICMessage.ShoutModifiers.Custom;
                }


                void OnICMessageReceivedHandler(ICMessage icMessage)
                {
                    AOClient? targetNetworkClient = GetTargetClientForNetwork(client);
                    if (targetNetworkClient == null)
                    {
                        return;
                    }
                    if (icMessage.CharId == targetNetworkClient.iniPuppetID &&
                    (icMessage.Message == "~"+sendMessage+"~" || icMessage.Message == sendMessage || icMessage.Message == sendMessage+"~"))
                    {
                        // Message was received by server.
                        ICMessageSettingsControl.Dispatcher.Invoke(() =>
                        {
                            ICMessageSettingsControl.txtICMessage.Text = "";

                            if (!ICMessageSettingsControl.stickyEffects)
                            {
                                ICMessageSettingsControl.ResetMessageEffects();
                            }
                        });

                        // Unsubscribe from the event
                        targetNetworkClient.OnICMessageReceived -= OnICMessageReceivedHandler;
                    }
                }

                AOClient? networkClient = GetTargetClientForNetwork(client);
                if (networkClient == null)
                {
                    return;
                }
                if (useSingleInternalClient)
                {
                    ApplyProfileToSingleInternalClient(client);
                }

                networkClient.OnICMessageReceived -= OnICMessageReceivedHandler;
                // Subscribe to the event
                networkClient.OnICMessageReceived += OnICMessageReceivedHandler;
                await networkClient.SendICMessage(sendMessage);
            };

            ICMessageSettingsControl.OnResetMessageEffects += () =>
            {
                HoldIt.IsChecked = false;
                Objection.IsChecked = false;
                TakeThat.IsChecked = false;
                Custom.IsChecked = false;
            };

            OOCLogControl.txtOOCShowname.Text = SaveFile.Data.OOCName;
            chkPosOnIniSwap.IsChecked = SaveFile.Data.SwitchPosOnIniSwap;
            chkSticky.IsChecked = SaveFile.Data.StickyEffect;
            chkInvertLog.IsChecked = SaveFile.Data.InvertICLog;
            InitializeDreddFeatureUi();

            btnDebug.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
            RefreshAreaNavigatorForCurrentClient();
        }
        private void RenameClient(AOClient bot)
        {
            // Show an input dialog to the user
            string newClientName = InputDialog.Show("Enter new Client name:", "New Client Name", bot.clientName); ;

            if (!string.IsNullOrWhiteSpace(newClientName))
            {
                bot.clientName = newClientName;
                UpdateClientTooltip(bot);

                if (bot == currentClient)
                {
                    OOCLogControl.UpdateStreamLabel(bot);
                }
            }
        }
        private void UpdateClientTooltip(AOClient bot)
        {
            var button = clients.Where(x => x.Value == bot).FirstOrDefault().Key;
            if (button == null)
            {
                return;
            }

            string characterName = string.IsNullOrWhiteSpace(bot.iniPuppetName)
                ? bot.currentINI?.Name ?? "Unknown"
                : bot.iniPuppetName;

            button.ToolTip = $"[{bot.playerID}] {characterName} (\"{bot.clientName}\")";
        }

        private AOClient? GetTargetClientForNetwork(AOClient? profileClient)
        {
            return useSingleInternalClient ? singleInternalClient : profileClient;
        }

        private AOClient? ResolveLogClientKey(AOClient profileClient)
        {
            if (profileClient == null)
            {
                return null;
            }

            if (!useSingleInternalClient)
            {
                return profileClient;
            }

            return singleInternalClient ?? profileClient;
        }

        private AOClient? GetClientForIncomingMessages()
        {
            if (!useSingleInternalClient)
            {
                return currentClient;
            }

            return boundSingleClientProfile ?? currentClient;
        }

        private AOClient? GetSingleModeLogTarget(AOClient? profileClient = null, AOClient? networkClient = null)
        {
            if (!useSingleInternalClient)
            {
                return profileClient ?? currentClient;
            }

            return GetClientForIncomingMessages()
                ?? boundSingleClientProfile
                ?? currentClient
                ?? singleInternalClient
                ?? networkClient
                ?? profileClient;
        }

        private void RefreshAreaNavigatorForCurrentClient()
        {
            AOClient? profileClient = currentClient;
            AOClient? networkClient = profileClient == null ? null : GetTargetClientForNetwork(profileClient);

            if (networkClient == null)
            {
                txtCurrentArea.Text = "Current: Unknown";
                lstAreas.ItemsSource = null;
                btnAreaNavigator.IsEnabled = false;
                btnGoToArea.IsEnabled = false;
                return;
            }

            btnAreaNavigator.IsEnabled = true;
            btnGoToArea.IsEnabled = true;

            string visibleArea = string.IsNullOrWhiteSpace(networkClient.CurrentArea) ? "Unknown" : networkClient.CurrentArea;
            txtCurrentArea.Text = $"Current: {visibleArea}";
            List<AreaNavigatorListItem> areaItems = networkClient.AvailableAreaInfos
                .Select(areaInfo => CreateAreaNavigatorListItem(areaInfo))
                .ToList();

            if (!string.IsNullOrWhiteSpace(networkClient.CurrentArea)
                && !areaItems.Any(item => string.Equals(item.Name, networkClient.CurrentArea, StringComparison.OrdinalIgnoreCase)))
            {
                areaItems.Insert(0, CreateCurrentAreaListItem(networkClient.CurrentArea));
            }

            lstAreas.ItemsSource = areaItems;
        }

        private AreaNavigatorListItem CreateAreaNavigatorListItem(AreaInfo areaInfo)
        {
            string status = string.IsNullOrWhiteSpace(areaInfo.Status) ? "Unknown" : areaInfo.Status;
            string caseManager = string.IsNullOrWhiteSpace(areaInfo.CaseManager) ? "Unknown" : areaInfo.CaseManager;
            string lockState = string.IsNullOrWhiteSpace(areaInfo.LockState) ? "Unknown" : areaInfo.LockState;

            string statusAndCmLine = status;
            if (!string.Equals(caseManager, "FREE", StringComparison.OrdinalIgnoreCase))
            {
                statusAndCmLine += $" | CM: {caseManager}";
            }

            string playersAndLockLine = lockState;
            if (areaInfo.Players != -1)
            {
                playersAndLockLine = $"{areaInfo.Players} users | {lockState}";
            }

            return new AreaNavigatorListItem
            {
                Name = areaInfo.Name,
                StatusAndCmLine = statusAndCmLine,
                PlayersAndLockLine = playersAndLockLine,
                RowBackground = GetAreaBrush(status, lockState),
            };
        }

        private Brush GetAreaBrush(string status, string lockState)
        {
            if (string.Equals(lockState, "LOCKED", StringComparison.OrdinalIgnoreCase))
            {
                return areaLockedBrush;
            }

            if (string.Equals(status, "LOOKING-FOR-PLAYERS", StringComparison.OrdinalIgnoreCase))
            {
                return areaLfpBrush;
            }

            if (string.Equals(status, "CASING", StringComparison.OrdinalIgnoreCase))
            {
                return areaCasingBrush;
            }

            if (string.Equals(status, "RECESS", StringComparison.OrdinalIgnoreCase))
            {
                return areaRecessBrush;
            }

            if (string.Equals(status, "RP", StringComparison.OrdinalIgnoreCase))
            {
                return areaRpBrush;
            }

            if (string.Equals(status, "GAMING", StringComparison.OrdinalIgnoreCase))
            {
                return areaGamingBrush;
            }

            return areaFreeBrush;
        }

        private AreaNavigatorListItem CreateCurrentAreaListItem(string areaName)
        {
            return new AreaNavigatorListItem
            {
                Name = areaName,
                StatusAndCmLine = "CURRENT AREA",
                PlayersAndLockLine = "Not listed by server area refresh",
                RowBackground = areaFreeBrush,
            };
        }

        private async Task BootstrapAreaNavigatorAsync(AOClient networkClient)
        {
            if (areaListBootstrapCompletedClients.Contains(networkClient))
            {
                return;
            }

            areaListBootstrapCompletedClients.Add(networkClient);

            try
            {
                await networkClient.RequestAreaList();
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to initialize area list on initial join.", ex);
            }
        }

        private void InitializeDreddFeatureUi()
        {
            isDreddFeatureEnabled = SaveFile.Data.AdvancedFeatures.IsEnabled(AdvancedFeatureIds.DreddBackgroundOverlayOverride);
            DreddStickyOverlayCheckBox.IsChecked = SaveFile.Data.DreddBackgroundOverlayOverride.StickyOverlay;
            Height = isDreddFeatureEnabled ? 690 : 658;
            imgScienceBlur.Height = isDreddFeatureEnabled ? 670 : 638;
            imgScienceBlur_darken.Height = isDreddFeatureEnabled ? 670 : 638;

            RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
            UpdateDreddFeatureVisibility();
            UpdateDreddFeatureEnabledState();
        }

        private void UpdateDreddFeatureVisibility()
        {
            bool enabled = isDreddFeatureEnabled;
            Visibility visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            FeatureRowBackground.Visibility = visibility;
            DreddFeatureLabel.Visibility = visibility;
            DreddOverlaySelector.Visibility = visibility;
            DreddStickyOverlayCheckBox.Visibility = visibility;
            DreddOverlayConfigButton.Visibility = visibility;
            DreddViewChangesButton.Visibility = visibility;

            double verticalOffset = enabled ? 30 : 0;
            Canvas.SetTop(BottomStatusBar, 603 + verticalOffset);
            Canvas.SetTop(chkSticky, 607 + verticalOffset);
            Canvas.SetTop(btnRefreshCharacters, 603 + verticalOffset);
            Canvas.SetTop(chkPosOnIniSwap, 607 + verticalOffset);
            Canvas.SetTop(btnDebug, 607 + verticalOffset);
            Canvas.SetTop(chkInvertLog, 607 + verticalOffset);
            Canvas.SetTop(btnAreaNavigator, 603 + verticalOffset);

            UpdateDreddFeatureEnabledState();
        }

        private void UpdateDreddFeatureEnabledState()
        {
            bool enabledForClient = isDreddFeatureEnabled && currentClient != null;
            DreddOverlaySelector.IsEnabled = enabledForClient;
            DreddOverlayDropButton.IsEnabled = enabledForClient;
            DreddStickyOverlayCheckBox.IsEnabled = enabledForClient;
            DreddFeatureLabel.Opacity = enabledForClient ? 1.0 : 0.65;

            // Always keep these available for configuration/review.
            DreddOverlayConfigButton.IsEnabled = isDreddFeatureEnabled;
            DreddViewChangesButton.IsEnabled = isDreddFeatureEnabled;
        }

        private static string GetOverlayDisplayName(string overlayReference)
        {
            string value = overlayReference?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return DreddNoneOverlayName;
            }

            string normalized = value.Replace('\\', '/').TrimEnd('/');
            string fileName = Path.GetFileNameWithoutExtension(normalized);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }

            fileName = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(fileName) ? value : fileName;
        }

        private void RefreshDreddOverlayForCurrentContext(bool promptForUnknownOverlay)
        {
            isLoadingDreddOverlaySelection = true;
            try
            {
                List<DreddOverlaySelectionItem> overlays = SaveFile.Data.DreddBackgroundOverlayOverride.OverlayDatabase
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new DreddOverlaySelectionItem
                    {
                        Name = entry.Name,
                        DisplayText = entry.Name,
                        FilePath = entry.FilePath,
                        IsNone = false
                    })
                    .ToList();
                DreddOverlaySelectionItem noneItem = new DreddOverlaySelectionItem
                {
                    Name = DreddNoneOverlayName,
                    DisplayText = DreddNoneOverlayName,
                    IsNone = true,
                    FilePath = string.Empty
                };
                overlays.Insert(0, noneItem);

                DreddOverlaySelectionItem selectedItem = noneItem;

                if (currentClient != null
                    && DreddBackgroundOverlayOverrideService.TryGetCurrentOverlayValue(
                        currentClient,
                        out bool hasEntry,
                        out string currentOverlayValue,
                        out string designIniPath,
                        out string positionKey,
                        out string backgroundDirectory,
                        out _))
                {
                    string currentContextKey = $"{designIniPath}|{positionKey}";
                    if (!string.Equals(lastDreddOverlayContextKey, currentContextKey, StringComparison.OrdinalIgnoreCase))
                    {
                        lastDreddOverlayContextKey = currentContextKey;
                        lastUnknownOverlayPromptKey = string.Empty;
                    }

                    if (hasEntry)
                    {
                        DreddOverlaySelectionItem? matched = overlays.FirstOrDefault(item =>
                            !item.IsNone
                            && DreddBackgroundOverlayOverrideService.OverlayReferencesMatch(
                                designIniPath,
                                backgroundDirectory,
                                item.FilePath,
                                currentOverlayValue));

                        if (matched == null)
                        {
                            string unknownDisplayName = GetOverlayDisplayName(currentOverlayValue);
                            string promptKey = $"{currentContextKey}|{currentOverlayValue}";
                            bool shouldPrompt = promptForUnknownOverlay
                                && !string.Equals(lastUnknownOverlayPromptKey, promptKey, StringComparison.OrdinalIgnoreCase);
                            if (shouldPrompt)
                            {
                                MessageBoxResult addResult = OceanyaMessageBox.Show(
                                    $"Current overlay '{unknownDisplayName}' is not in your database. Add it now?",
                                    "Unknown Overlay",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question);

                                lastUnknownOverlayPromptKey = promptKey;
                                if (addResult == MessageBoxResult.Yes)
                                {
                                    string overlayPathToStore = currentOverlayValue;
                                    if (DreddBackgroundOverlayOverrideService.TryResolveOverlayPathToAbsolute(
                                        currentOverlayValue,
                                        designIniPath,
                                        backgroundDirectory,
                                        out string resolvedOverlayAbsolutePath))
                                    {
                                        overlayPathToStore = resolvedOverlayAbsolutePath;
                                    }

                                    SaveFile.Data.DreddBackgroundOverlayOverride.OverlayDatabase.Add(new DreddOverlayEntry
                                    {
                                        Name = unknownDisplayName,
                                        FilePath = overlayPathToStore
                                    });
                                    SaveFile.Save();

                                    // Rebuild once so newly added entry is included and selected.
                                    RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
                                    return;
                                }
                            }

                            matched = new DreddOverlaySelectionItem
                            {
                                Name = unknownDisplayName,
                                DisplayText = unknownDisplayName,
                                FilePath = currentOverlayValue,
                                IsNone = false,
                                IsTransient = true
                            };
                            overlays.Add(matched);
                        }

                        selectedItem = matched;
                    }

                    DreddBackgroundOverlayOverrideService.TryGetOriginalOverlayValue(
                        designIniPath,
                        positionKey,
                        hasEntry,
                        currentOverlayValue,
                        out bool originalHasEntry,
                        out string originalValue);

                    DreddOverlaySelectionItem? originalItem = originalHasEntry
                        ? overlays.FirstOrDefault(item =>
                            !item.IsNone
                            && DreddBackgroundOverlayOverrideService.OverlayReferencesMatch(
                                designIniPath,
                                backgroundDirectory,
                                item.FilePath,
                                originalValue))
                        : noneItem;

                    if (originalItem != null)
                    {
                        originalItem.DisplayText = $"{originalItem.DisplayText} (original)";
                    }
                }

                DreddOverlayListBox.ItemsSource = overlays;
                DreddOverlayListBox.DisplayMemberPath = nameof(DreddOverlaySelectionItem.DisplayText);
                DreddOverlayListBox.SelectedItem = selectedItem;
                DreddOverlaySelectedText.Text = selectedItem.DisplayText;
            }
            finally
            {
                isLoadingDreddOverlaySelection = false;
            }
        }

        private DreddOverlaySelectionItem? GetSelectedDreddOverlayEntry()
        {
            return DreddOverlayListBox.SelectedItem as DreddOverlaySelectionItem;
        }

        private void HookClientForDreddOverlay(AOClient client)
        {
            client.OnBGChange += (_) =>
            {
                Dispatcher.Invoke(() =>
                {
                    HandleDreddFeatureContextChanged(client);
                });
            };

            client.OnSideChange += (_) =>
            {
                Dispatcher.Invoke(() =>
                {
                    HandleDreddFeatureContextChanged(client);
                });
            };
        }

        private void HandleDreddFeatureContextChanged(AOClient sourceClient)
        {
            if (!isDreddFeatureEnabled)
            {
                return;
            }

            if (currentClient == null || sourceClient != currentClient)
            {
                return;
            }

            if (DreddStickyOverlayCheckBox.IsChecked == true)
            {
                ApplyStoredDreddStickyOverlay(showFeedbackOnFailure: false);
            }

            RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: true);
            UpdateDreddFeatureEnabledState();
        }

        private void ApplyStoredDreddStickyOverlay(bool showFeedbackOnFailure)
        {
            if (currentClient == null)
            {
                return;
            }

            string stickySelectionName = SaveFile.Data.DreddBackgroundOverlayOverride.SelectedOverlayName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(stickySelectionName))
            {
                return;
            }

            if (string.Equals(stickySelectionName, DreddNoneOverlayName, StringComparison.OrdinalIgnoreCase))
            {
                bool cleared = DreddBackgroundOverlayOverrideService.TryClearOverlay(currentClient, out string clearError);
                if (!cleared && showFeedbackOnFailure && !string.IsNullOrWhiteSpace(clearError))
                {
                    OceanyaMessageBox.Show(
                        $"Could not apply sticky overlay: {clearError}",
                        "Overlay Apply Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            DreddOverlayEntry? stickyEntry = SaveFile.Data.DreddBackgroundOverlayOverride.OverlayDatabase
                .FirstOrDefault(entry => string.Equals(entry.Name, stickySelectionName, StringComparison.OrdinalIgnoreCase));
            if (stickyEntry == null)
            {
                return;
            }

            bool applied = DreddBackgroundOverlayOverrideService.TryApplyOverlay(currentClient, stickyEntry, out string error);
            if (!applied && showFeedbackOnFailure && !string.IsNullOrWhiteSpace(error))
            {
                OceanyaMessageBox.Show(
                    $"Could not apply sticky overlay: {error}",
                    "Overlay Apply Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void TryApplySelectedDreddOverlayToCurrentContext(bool showFeedbackOnFailure)
        {
            if (!isDreddFeatureEnabled)
            {
                return;
            }

            DreddOverlaySelectionItem? selectedOverlay = GetSelectedDreddOverlayEntry();
            if (selectedOverlay == null || currentClient == null)
            {
                return;
            }

            bool applied;
            string error;
            if (selectedOverlay.IsNone)
            {
                applied = DreddBackgroundOverlayOverrideService.TryClearOverlay(currentClient, out error);
            }
            else
            {
                applied = DreddBackgroundOverlayOverrideService.TryApplyOverlay(
                    currentClient,
                    new DreddOverlayEntry
                    {
                        Name = selectedOverlay.Name,
                        FilePath = selectedOverlay.FilePath
                    },
                    out error);
            }

            if (!applied && showFeedbackOnFailure && !string.IsNullOrWhiteSpace(error))
            {
                OceanyaMessageBox.Show(
                    $"Could not apply overlay: {error}",
                    "Overlay Apply Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (applied)
            {
                RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
            }
        }

        private void OpenDreddOverlayConfigDialog()
        {
            DreddOverlayDatabaseWindow window = new DreddOverlayDatabaseWindow
            {
                Owner = this
            };
            bool? result = window.ShowDialog();
            if (result == true)
            {
                RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
            }
        }

        private void ApplyProfileToSingleInternalClient(AOClient profileClient)
        {
            if (!useSingleInternalClient || singleInternalClient == null || profileClient == null)
            {
                return;
            }

            boundSingleClientProfile = profileClient;
            singleInternalClient.clientName = profileClient.clientName;

            if (profileClient.currentINI != null)
            {
                singleInternalClient.SetCharacter(profileClient.currentINI);
            }

            if (profileClient.currentEmote != null)
            {
                singleInternalClient.SetEmote(profileClient.currentEmote.DisplayID);
            }

            singleInternalClient.SetICShowname(profileClient.ICShowname);
            singleInternalClient.OOCShowname = profileClient.OOCShowname;
            singleInternalClient.curSFX = profileClient.curSFX;
            singleInternalClient.deskMod = profileClient.deskMod;
            singleInternalClient.emoteMod = profileClient.emoteMod;
            singleInternalClient.shoutModifiers = profileClient.shoutModifiers;
            singleInternalClient.flip = profileClient.flip;
            singleInternalClient.effect = profileClient.effect;
            singleInternalClient.screenshake = profileClient.screenshake;
            singleInternalClient.textColor = profileClient.textColor;
            singleInternalClient.Immediate = profileClient.Immediate;
            singleInternalClient.Additive = profileClient.Additive;
            singleInternalClient.SelfOffset = profileClient.SelfOffset;
            singleInternalClient.switchPosWhenChangingINI = profileClient.switchPosWhenChangingINI;

            if (!string.IsNullOrWhiteSpace(profileClient.curPos))
            {
                singleInternalClient.SetPos(profileClient.curPos, false);
            }
        }

        private void SyncSingleClientStatusToProfile(AOClient profileClient)
        {
            if (!useSingleInternalClient || singleInternalClient == null || profileClient == null)
            {
                return;
            }

            profileClient.playerID = singleInternalClient.playerID;
            profileClient.iniPuppetID = singleInternalClient.iniPuppetID;
            profileClient.curBG = singleInternalClient.curBG;
            profileClient.SetPos(singleInternalClient.curPos);
        }

        private void InitializeCommonClientEvents(AOClient profileClient, AOClient networkClient)
        {
            networkClient.OnWebsocketDisconnect += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(profileClient, networkClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    ICLogControl.AddMessage(
                        targetClient,
                        "Oceanya Client",
                        "Websocket Disconnected.",
                        true,
                        ICMessage.TextColors.Red
                    );
                    OOCLogControl.AddMessage(targetClient, "Oceanya Client", "Websocket Disconnected.", true);
                });
            };

            networkClient.OnReconnectionAttempt += (int attemptCount) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(profileClient, networkClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    string message = $"Reconnecting...{(attemptCount != 1 ? $" (Attempt {attemptCount})" : "")}";
                    ICLogControl.AddMessage(targetClient, "Oceanya Client", message, true, ICMessage.TextColors.Yellow);
                    OOCLogControl.AddMessage(targetClient, "Oceanya Client", message, true);
                });
            };

            networkClient.OnReconnectionAttemptFailed += (int attemptCount) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(profileClient, networkClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    string message = $"Attempt {attemptCount} failed.";
                    ICLogControl.AddMessage(targetClient, "Oceanya Client", message, true, ICMessage.TextColors.Yellow);
                    OOCLogControl.AddMessage(targetClient, "Oceanya Client", message, true);
                });
            };

            networkClient.OnReconnect += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(profileClient, networkClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    SyncSingleClientStatusToProfile(targetClient);
                    UpdateClientTooltip(targetClient);
                    ICLogControl.AddMessage(targetClient, "Oceanya Client", "Reconnected to server.", true, ICMessage.TextColors.Green);
                    OOCLogControl.AddMessage(targetClient, "Oceanya Client", "Reconnected to server.", true);
                });
            };

            networkClient.OnINIPuppetChange += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(profileClient, networkClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    SyncSingleClientStatusToProfile(targetClient);
                    if (targetClient == currentClient)
                    {
                        OOCLogControl.UpdateStreamLabel(targetClient);
                    }
                    UpdateClientTooltip(targetClient);
                });
            };

            networkClient.OnCurrentAreaChanged += (string _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshAreaNavigatorForCurrentClient();
                });
            };

            networkClient.OnAvailableAreasUpdated += (IReadOnlyList<string> _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshAreaNavigatorForCurrentClient();
                });
            };

            networkClient.OnAvailableAreaInfosUpdated += (IReadOnlyList<AreaInfo> _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    RefreshAreaNavigatorForCurrentClient();
                });
            };
        }

        private async Task EnsureSingleInternalClientConnectedAsync()
        {
            if (singleInternalClient != null)
            {
                return;
            }

            singleInternalClient = new AOClient(Globals.GetSelectedServerEndpoint());
            singleInternalClient.clientName = "InternalClient";

            singleInternalClient.OnICMessageReceived += (ICMessage icMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(singleInternalClient, singleInternalClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    bool isSentFromSelf = icMessage.CharId == singleInternalClient.iniPuppetID;
                    ICLogControl.AddMessage(targetClient, icMessage.ShowName, icMessage.Message, isSentFromSelf, icMessage.TextColor);

                    targetClient.curBG = singleInternalClient.curBG;
                    targetClient.iniPuppetID = singleInternalClient.iniPuppetID;
                });
            };

            singleInternalClient.OnOOCMessageReceived += (string showName, string message, bool isFromServer) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(singleInternalClient, singleInternalClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    OOCLogControl.AddMessage(targetClient, showName, message, isFromServer);
                });
            };

            singleInternalClient.OnBGChange += (string newBg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(singleInternalClient, singleInternalClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    targetClient.curBG = newBg;
                    targetClient.OnBGChange?.Invoke(newBg);
                });
            };

            singleInternalClient.OnSideChange += (string newPos) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(singleInternalClient, singleInternalClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    targetClient.SetPos(newPos);
                });
            };

            InitializeCommonClientEvents(singleInternalClient, singleInternalClient);
            await singleInternalClient.Connect();
            await BootstrapAreaNavigatorAsync(singleInternalClient);
        }
        private void AddClient(string clientName)
        {
            _ = AddClientAsync(clientName);
        }
        private async Task AddClientAsync(string clientName)
        {
            IsEnabled = false;  
            await WaitForm.ShowFormAsync("Connecting client...", this);

            try
            {
                AOClient bot = new AOClient(Globals.GetSelectedServerEndpoint());
                bot.clientName = clientName;
                HookClientForDreddOverlay(bot);

                if (useSingleInternalClient)
                {
                    if (boundSingleClientProfile == null)
                    {
                        boundSingleClientProfile = bot;
                    }

                    await EnsureSingleInternalClientConnectedAsync();
                }
                else
                {
                    bot.OnICMessageReceived += (ICMessage icMessage) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            bool isSentFromSelf = clients.Select(x => x.Value.iniPuppetID).Contains(icMessage.CharId);

                            ICLogControl.AddMessage(bot, icMessage.ShowName,
                                icMessage.Message,
                                isSentFromSelf, icMessage.TextColor);
                        });
                    };

                    bot.OnOOCMessageReceived += (string showName, string message, bool isFromServer) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            OOCLogControl.AddMessage(bot, showName, message, isFromServer);
                        });
                    };
                }

                if (useSingleInternalClient)
                {
                    if (singleInternalClient != null && singleInternalClient.currentINI != null)
                    {
                        bot.SetCharacter(singleInternalClient.currentINI);
                    }
                    else if (CharacterFolder.FullList.Any())
                    {
                        bot.SetCharacter(CharacterFolder.FullList.First());
                    }

                    if (singleInternalClient != null)
                    {
                        bot.playerID = singleInternalClient.playerID;
                        bot.iniPuppetID = singleInternalClient.iniPuppetID;
                        bot.curBG = singleInternalClient.curBG;
                        bot.SetPos(singleInternalClient.curPos);
                    }
                }

                bot.SetICShowname(clientName);
                bot.OOCShowname = clientName;
                bot.switchPosWhenChangingINI = chkPosOnIniSwap.IsChecked == true;

                ToggleButton toggleBtn = new ToggleButton
                {
                    Width = 40,
                    Height = 40
                };
                
                toggleBtn.Checked += ClientToggleButton_Checked;
                toggleBtn.Unchecked += ClientToggleButton_Unchecked;

                #region Create Context Menu
                ContextMenu contextMenu = new ContextMenu();
                MenuItem renameMenuItem = new MenuItem { Header = "Rename Client" };
                renameMenuItem.Click += (sender, args) => RenameClient(bot);
                contextMenu.Items.Add(renameMenuItem);

                MenuItem iniPuppetChange = new MenuItem { Header = "Select INIPuppet (Automatic)" };
                iniPuppetChange.Click += async (sender, args) =>
                {
                    AOClient? targetNetworkClient = GetTargetClientForNetwork(bot);
                    if (targetNetworkClient == null)
                    {
                        return;
                    }
                    await targetNetworkClient.SelectFirstAvailableINIPuppet(false);

                    if (useSingleInternalClient)
                    {
                        SyncSingleClientStatusToProfile(bot);
                    }
                };
                contextMenu.Items.Add(iniPuppetChange);

                MenuItem manualIniPuppetChange = new MenuItem { Header = "Select INIPuppet (Manual)" };
                manualIniPuppetChange.Click += async (sender, args) =>
                {
                    // Show an input dialog to the user
                    string newClientName = ShowInputDialog("Enter INIPuppet name:");

                    if (!string.IsNullOrWhiteSpace(newClientName))
                    {
                        try
                        {
                            AOClient? targetNetworkClient = GetTargetClientForNetwork(bot);
                            if (targetNetworkClient == null)
                            {
                                return;
                            }
                            await targetNetworkClient.SelectIniPuppet(newClientName, false);
                            if (useSingleInternalClient)
                            {
                                SyncSingleClientStatusToProfile(bot);
                            }
                        }
                        catch(Exception e)
                        {
                            OceanyaMessageBox.Show(e.Message, "INIPuppet Selection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                };
                contextMenu.Items.Add(manualIniPuppetChange);

                MenuItem reconnectMenuItem = new MenuItem { Header = "Reconnect" };
                reconnectMenuItem.Click += async (sender, args) =>
                {
                    AOClient? targetNetworkClient = GetTargetClientForNetwork(bot);
                    if (targetNetworkClient == null)
                    {
                        return;
                    }
                    await targetNetworkClient.DisconnectWebsocket();
                };
                contextMenu.Items.Add(reconnectMenuItem);


                toggleBtn.ContextMenu = contextMenu;
                #endregion
                // Subscribe to OnChangedCharacter event
                bot.OnChangedCharacter += (CharacterFolder newCharacter) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Path to the normal and selected images
                        string normalImagePath = newCharacter.CharIconPath;
                        string selectedImagePath = normalImagePath; // Since you want the same image darkened

                        if (System.IO.File.Exists(normalImagePath))
                        {
                            // Create the ControlTemplate dynamically
                            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
                            FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid));
                            FrameworkElementFactory imageFactory = new FrameworkElementFactory(typeof(Image));
                            imageFactory.Name = "ButtonImage";
                            imageFactory.SetValue(Image.WidthProperty, 40.0);
                            imageFactory.SetValue(Image.HeightProperty, 40.0);
                            imageFactory.SetValue(Image.SourceProperty, new BitmapImage(new Uri(normalImagePath, UriKind.Absolute)));

                            gridFactory.AppendChild(imageFactory);
                            template.VisualTree = gridFactory;

                            // Add the trigger for toggled state (darken image)
                            Trigger trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                            trigger.Setters.Add(new Setter
                            {
                                Property = Image.SourceProperty,
                                TargetName = "ButtonImage",
                                Value = new BitmapImage(new Uri(selectedImagePath, UriKind.Absolute))
                            });
                            trigger.Setters.Add(new Setter
                            {
                                Property = Image.OpacityProperty,
                                TargetName = "ButtonImage",
                                Value = 0.6 // Darkening effect
                            });

                            template.Triggers.Add(trigger);
                            toggleBtn.Template = template;
                        }
                        else
                        {
                            // No image exists, create a gray image programmatically with text
                            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
                            FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid));
                            FrameworkElementFactory imageFactory = new FrameworkElementFactory(typeof(Image));
                            imageFactory.Name = "ButtonImage";
                            imageFactory.SetValue(Image.WidthProperty, 40.0);
                            imageFactory.SetValue(Image.HeightProperty, 40.0);

                            // Create a gray bitmap
                            int width = 40;
                            int height = 40;
                            WriteableBitmap grayBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                            byte[] pixels = new byte[width * height * 4];
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                pixels[i] = 225;     // Blue
                                pixels[i + 1] = 225; // Green
                                pixels[i + 2] = 225; // Red
                                pixels[i + 3] = 255; // Alpha
                            }
                            grayBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

                            imageFactory.SetValue(Image.SourceProperty, grayBitmap);

                            FrameworkElementFactory textFactory = new FrameworkElementFactory(typeof(TextBlock));
                            textFactory.SetValue(TextBlock.TextProperty, newCharacter.Name);
                            textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
                            textFactory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Arial"));
                            textFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
                            textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
                            textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
                            textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                            textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

                            gridFactory.AppendChild(imageFactory);
                            gridFactory.AppendChild(textFactory);
                            template.VisualTree = gridFactory;

                            // Add the trigger for toggled state (darken image)
                            Trigger trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                            trigger.Setters.Add(new Setter
                            {
                                Property = Image.OpacityProperty,
                                TargetName = "ButtonImage",
                                Value = 0.6 // Darkening effect
                            });

                            template.Triggers.Add(trigger);
                            toggleBtn.Template = template;
                        }
                    });
                };
                if (!useSingleInternalClient)
                {
                    InitializeCommonClientEvents(bot, bot);
                    await bot.Connect();
                    await BootstrapAreaNavigatorAsync(bot);
                }

                bot.OnDisconnect += () =>
                {
                    if (useSingleInternalClient)
                    {
                        return;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        var button = clients.FirstOrDefault(x => x.Value == bot).Key;
                        areaListBootstrapCompletedClients.Remove(bot);

                        clients.Remove(button);
                        EmoteGrid.DeleteElement(button);

                        OceanyaMessageBox.Show($"Client {bot.clientName} has disconnected.", "Client Disconnected", MessageBoxButton.OK, MessageBoxImage.Information);

                        if(clients.Count == 0)
                        {
                            //Clear the form entirely.
                            ICMessageSettingsControl.ClearSettings();
                            OOCLogControl.ClearAllLogs();
                            ICLogControl.ClearAllLogs();
                            OOCLogControl.IsEnabled = false;
                            ICLogControl.IsEnabled = false;
                            ICMessageSettingsControl.IsEnabled = false;
            OOCLogControl.UpdateStreamLabel(null);
                            currentClient = null;
                            RefreshAreaNavigatorForCurrentClient();
                            RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
                            UpdateDreddFeatureEnabledState();
                        }
                        else
                        {
                            var newClient = clients.Values.FirstOrDefault();
                            if (newClient != null)
                            {
                                SelectClient(newClient);
                            }
                        }
                            
                    });
                };
                if (bot.currentINI != null)
                {
                    bot.SetCharacter(bot.currentINI);
                }

                toggleBtn.Focusable = false;
                toggleBtn.IsTabStop = false;

                clients.Add(toggleBtn, bot);

                EmoteGrid.AddElement(toggleBtn);

                toggleBtn.IsChecked = true;
                UpdateClientTooltip(bot);

                if (clients.Count == 1)
                {
                    OOCLogControl.IsEnabled = true;
                    ICLogControl.IsEnabled = true;
                    ICMessageSettingsControl.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                WaitForm.CloseForm();
                OceanyaMessageBox.Show($"Error connecting client: {ex.Message}", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                WaitForm.CloseForm();
            }

            IsEnabled = true;
        }

        private void SelectClient(AOClient client)
        {
            foreach (var button in clients.Keys)
            {
                if (clients[button] == client)
                {
                    button.IsChecked = true;
                    EmoteGrid.SetPageToElement(button);
                    break;
                }
            }

            if (useSingleInternalClient)
            {
                ApplyProfileToSingleInternalClient(client);
                SyncSingleClientStatusToProfile(client);
            }

            currentClient = client;
            ICMessageSettingsControl.SetClient(currentClient);
            OOCLogControl.SetCurrentClient(currentClient);
            ICLogControl.SetCurrentClient(currentClient);
            RefreshAreaNavigatorForCurrentClient();

            if (isDreddFeatureEnabled && DreddStickyOverlayCheckBox.IsChecked == true)
            {
                ApplyStoredDreddStickyOverlay(showFeedbackOnFailure: false);
            }

            RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: true);
            UpdateDreddFeatureEnabledState();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            #region Create the bot and connect to the server
            AOClient bot = new AOClient(Globals.GetSelectedServerEndpoint());
            await bot.Connect();
            #endregion

            #region Start the GPT Client
            string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new Exception("OpenAI API key is not set in the environment variables.");
            }
            GPTClient gptClient = new GPTClient(apiKey);
            gptClient.SetSystemInstructions(new List<string> { Globals.AI_SYSTEM_PROMPT });
            gptClient.systemVariables = new Dictionary<string, string>()
            {
                { "[[[current_character]]]", bot.currentINI?.Name ?? string.Empty },
                { "[[[current_emote]]]", bot.currentEmote?.DisplayID ?? string.Empty }
            };
            #endregion

            ChatLogManager chatLog = new ChatLogManager(MaxChatHistory: 20);

            bot.OnMessageReceived += async (string chatLogType, string characterName, string showName, string message, int iniPuppetID) =>
            {
                chatLog.AddMessage(chatLogType, characterName, showName, message);

                if (!Globals.UseOpenAIAPI) return;

                switch (chatLogType)
                {
                    case "IC":
                        if (showName == bot.ICShowname && characterName == bot.currentINI?.Name && iniPuppetID == bot.iniPuppetID)
                        {
                            return;
                        }
                        break;
                    case "OOC":
                        if (showName == bot.ICShowname)
                        {
                            return;
                        }
                        break;
                }


                int maxRetries = 3; // Prevent infinite loops
                int attempt = 0;
                bool success = false;

                while (attempt < maxRetries && !success)
                {
                    attempt++;
                    if (Globals.DebugMode) CustomConsole.WriteLine($"Prompting AI..." + (attempt > 0 ? " (Attempt {attempt})" : ""));
                    string response = await gptClient.GetResponseAsync(chatLog.GetFormattedChatHistory());
                    if (Globals.DebugMode) CustomConsole.WriteLine("Received AI response: " + response);

                    success = await ValidateJsonResponse(bot, response);
                }

                if (!success)
                {
                    CustomConsole.WriteLine("ERROR: AI failed to return a valid response after multiple attempts.");
                }
            };

            await Task.Delay(-1);
        }

        private static async Task<bool> ValidateJsonResponse(AOClient bot, string response)
        {
            bool success = false;

            // Ensure response is not empty and not SYSTEM_WAIT()
            if (string.IsNullOrWhiteSpace(response) || response == "SYSTEM_WAIT()")
                return success;

            try
            {
                var responseJson = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
                if (responseJson == null)
                {
                    CustomConsole.WriteLine("ERROR: AI response is not valid JSON. Retrying...");
                    return success;
                }

                // Validate required fields
                if (!responseJson.ContainsKey("message") || !responseJson.ContainsKey("chatlog") ||
                    !responseJson.ContainsKey("showname") || !responseJson.ContainsKey("current_character") ||
                    !responseJson.ContainsKey("modifiers"))
                {
                    CustomConsole.WriteLine("ERROR: AI response is missing required fields. Retrying...");
                    return success;
                }

                string botMessage = responseJson["message"].ToString() ?? string.Empty;
                string chatlogType = responseJson["chatlog"].ToString() ?? string.Empty;
                string newShowname = responseJson["showname"].ToString() ?? string.Empty;
                string newCharacter = responseJson["current_character"].ToString() ?? string.Empty;

                // Ensure chatlogType is either "IC" or "OOC"
                if (chatlogType != "IC" && chatlogType != "OOC")
                {
                    CustomConsole.WriteLine($"ERROR: Invalid chatlog type '{chatlogType}', retrying...");
                    return success;
                }

                // Apply showname change if valid
                if (!string.IsNullOrEmpty(newShowname))
                {
                    bot.SetICShowname(newShowname);
                }

                // Apply character switch if valid
                if (!string.IsNullOrEmpty(newCharacter))
                {
                    bot.SetCharacter(newCharacter);
                }

                // Apply modifiers with strict validation
                if (responseJson["modifiers"] is JsonElement modifiersElement)
                {
                    var modifiers = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modifiersElement.GetRawText());

                    if (modifiers != null)
                    {
                        if (modifiers.ContainsKey("deskMod") && modifiers["deskMod"].TryGetInt32(out int deskModValue))
                            bot.deskMod = (ICMessage.DeskMods)deskModValue;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid deskMod value. Retrying...");
                            return success;
                        }

                        if (modifiers.ContainsKey("emoteMod") && modifiers["emoteMod"].TryGetInt32(out int emoteModValue))
                            bot.emoteMod = (ICMessage.EmoteModifiers)emoteModValue;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid emoteMod value. Retrying...");
                            return success;
                        }

                        if (modifiers.ContainsKey("shoutModifiers") && modifiers["shoutModifiers"].TryGetInt32(out int shoutModValue))
                            bot.shoutModifiers = (ICMessage.ShoutModifiers)shoutModValue;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid shoutModifiers value. Retrying...");
                            return success;
                        }

                        if (modifiers.ContainsKey("flip") && modifiers["flip"].TryGetInt32(out int flipValue))
                            bot.flip = flipValue == 1;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid flip value. Retrying...");
                            return success;
                        }

                        //if (modifiers.ContainsKey("realization") && modifiers["realization"].TryGetInt32(out int realizationValue))
                        //    bot.effect = realizationValue == 1;
                        //else
                        //{
                        //    CustomConsole.WriteLine("ERROR: Invalid realization value. Retrying...");
                        //    return success;
                        //}

                        if (modifiers.ContainsKey("textColor") && modifiers["textColor"].TryGetInt32(out int textColorValue))
                            bot.textColor = (ICMessage.TextColors)textColorValue;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid textColor value. Retrying...");
                            return success;
                        }

                        if (modifiers.ContainsKey("immediate") && modifiers["immediate"].TryGetInt32(out int immediateValue))
                            bot.Immediate = immediateValue == 1;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid immediate value. Retrying...");
                            return success;
                        }

                        if (modifiers.ContainsKey("additive") && modifiers["additive"].TryGetInt32(out int additiveValue))
                            bot.Additive = additiveValue == 1;
                        else
                        {
                            CustomConsole.WriteLine("ERROR: Invalid additive value. Retrying...");
                            return success;
                        }
                    }
                    else
                    {
                        CustomConsole.WriteLine("ERROR: AI response modifiers section is invalid. Retrying...");
                        return success;
                    }
                }

                // Send response based on chatlog type
                if (!string.IsNullOrEmpty(botMessage))
                {
                    switch (chatlogType)
                    {
                        case "IC":
                            await bot.SendICMessage(botMessage);
                            break;
                        case "OOC":
                            await bot.SendOOCMessage(bot.ICShowname, botMessage);
                            break;
                    }
                }
                else
                {
                    CustomConsole.WriteLine("ERROR: AI response message is empty. Retrying...");
                    return success;
                }

                success = true; // If we reach here, response was valid and handled successfully
            }
            catch (Exception ex)
            {
                CustomConsole.WriteLine($"ERROR: Exception while processing AI response - {ex.Message}. Retrying...");
            }

            return success;
        }

        private void btnAddClient_Click(object sender, RoutedEventArgs e)
        {
            // Show an input dialog to the user
            string newClientName = ShowInputDialog("Enter client name:");

            if (!string.IsNullOrWhiteSpace(newClientName))
            {
                AddClient(newClientName);
            }
        }

        private string ShowInputDialog(string prompt)
        {
            return InputDialog.Show(prompt, "Client Name");
        }


        private async void btnRemoveClient_Click(object sender, RoutedEventArgs e)
        {
            var clientToRemove = currentClient;
            if (clientToRemove == null) return;

            if (!useSingleInternalClient)
            {
                await clientToRemove.Disconnect();
                return;
            }

            var button = clients.FirstOrDefault(x => x.Value == clientToRemove).Key;
            if (button != null)
            {
                clients.Remove(button);
                EmoteGrid.DeleteElement(button);
            }

            if (clients.Count == 0)
            {
                if (singleInternalClient != null)
                {
                    areaListBootstrapCompletedClients.Remove(singleInternalClient);
                    await singleInternalClient.Disconnect();
                    singleInternalClient = null;
                    boundSingleClientProfile = null;
                }

                ICMessageSettingsControl.ClearSettings();
                OOCLogControl.ClearAllLogs();
                ICLogControl.ClearAllLogs();
                OOCLogControl.IsEnabled = false;
                ICLogControl.IsEnabled = false;
                ICMessageSettingsControl.IsEnabled = false;
                OOCLogControl.UpdateStreamLabel(null);
                currentClient = null;
                RefreshAreaNavigatorForCurrentClient();
                RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
                UpdateDreddFeatureEnabledState();
            }
            else
            {
                SelectClient(clients.Values.First());
            }
        }

        private void ClientToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            ToggleButton? clickedButton = sender as ToggleButton;
            if (clickedButton == null || !clients.ContainsKey(clickedButton))
            {
                return;
            }

            foreach (var button in clients.Keys)
            {
                if (button != clickedButton)
                {
                    button.Unchecked -= ClientToggleButton_Unchecked;
                    button.IsChecked = false;
                    button.Unchecked += ClientToggleButton_Unchecked;
                }
            }

            SelectClient(clients[clickedButton]);
        }
        private void ClientToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleButton? clickedButton = sender as ToggleButton;
            if (clickedButton == null)
            {
                return;
            }

            if (clickedButton.IsChecked == false)
            {
                clickedButton.IsChecked = true;
            }
        }
        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            ToggleButton? clickedButton = sender as ToggleButton;
            if (clickedButton == null)
            {
                return;
            }

            foreach (var button in objectionModifiers)
            {
                if (button != clickedButton)
                {
                    button.IsChecked = false; // Uncheck other buttons
                }
            }
        }

        private void chkStickyEffects_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                ICMessageSettingsControl.stickyEffects = checkBox.IsChecked == true;

                SaveFile.Data.StickyEffect = checkBox.IsChecked == true;
                SaveFile.Save();
            }
        }
        private void chkPosOnIniSwap_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                foreach (var client in clients.Values)
                {
                    client.switchPosWhenChangingINI = checkBox.IsChecked == true;
                }

                SaveFile.Data.SwitchPosOnIniSwap = checkBox.IsChecked == true;
                SaveFile.Save();
            }
        }
        

        private async void btnRefreshCharacters_Click(object sender, RoutedEventArgs e)
        {
            var result = OceanyaMessageBox.Show("Are you sure you want to refresh your client assets? (This process may take a while)", "Refresh all Assets", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes) return;
            
            await WaitForm.ShowFormAsync("Refreshing character and background info...", this);

            Globals.UpdateConfigINI(Globals.PathToConfigINI);
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
            AOBot_Testing.Structures.Background.RefreshCache(
                onChangedMountPath: (path) =>
                {
                    WaitForm.SetSubtitle("Indexed background mount path: " + path);
                });
            WaitForm.CloseForm();
            ICMessageSettingsControl.ReinitializeSettings();
            if (currentClient == null)
            {
                return;
            }
            SelectClient(currentClient);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (useSingleInternalClient)
            {
                if (singleInternalClient != null)
                {
                    await singleInternalClient.DisconnectWebsocket();
                }
                return;
            }

            foreach (var item in clients.Values)
            {
                await item.DisconnectWebsocket();
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (useSingleInternalClient)
            {
                if (singleInternalClient != null)
                {
                    await singleInternalClient.Disconnect();
                }
            }
            else
            {
                foreach (var item in clients.Values)
                {
                    await item.Disconnect();
                }
            }

            var config = new InitialConfigurationWindow();
            config.Activate();
            config.Show();

            this.Close();
        }

        private void chkInvertLog_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                ICLogControl.SetInvertOnClientLogs(checkBox.IsChecked == true);
                SaveFile.Data.InvertICLog = checkBox.IsChecked == true;
                SaveFile.Save();
            }
        }

        private void DreddOverlayDropButton_Click(object sender, RoutedEventArgs e)
        {
            if (!DreddOverlaySelector.IsEnabled)
            {
                return;
            }

            DreddOverlayPopup.IsOpen = !DreddOverlayPopup.IsOpen;
        }

        private void DreddOverlayListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingDreddOverlaySelection)
            {
                return;
            }

            if (currentClient == null)
            {
                return;
            }

            DreddOverlaySelectionItem? selectedOverlay = GetSelectedDreddOverlayEntry();
            if (selectedOverlay == null)
            {
                return;
            }

            DreddOverlaySelectedText.Text = selectedOverlay.DisplayText;
            SaveFile.Data.DreddBackgroundOverlayOverride.SelectedOverlayName = selectedOverlay?.Name?.Trim() ?? string.Empty;
            SaveFile.Save();

            TryApplySelectedDreddOverlayToCurrentContext(showFeedbackOnFailure: true);
            DreddOverlayPopup.IsOpen = false;
        }

        private void DreddStickyOverlayCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            bool sticky = DreddStickyOverlayCheckBox.IsChecked == true;
            SaveFile.Data.DreddBackgroundOverlayOverride.StickyOverlay = sticky;
            SaveFile.Save();

            if (sticky)
            {
                ApplyStoredDreddStickyOverlay(showFeedbackOnFailure: false);
                RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
            }
        }

        private void DreddOverlayConfigButton_Click(object sender, RoutedEventArgs e)
        {
            OpenDreddOverlayConfigDialog();
        }

        private void DreddViewChangesButton_Click(object sender, RoutedEventArgs e)
        {
            DreddOverlayChangesWindow window = new DreddOverlayChangesWindow
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void THEDINGBUTTON_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayer.PlayEmbeddedSound("Resources/BellDing.mp3", 0.25f);
        }

        private void btnAreaNavigator_Click(object sender, RoutedEventArgs e)
        {
            RefreshAreaNavigatorForCurrentClient();
            AreaNavigatorPopup.IsOpen = true;
        }

        private async void btnGoToArea_Click(object sender, RoutedEventArgs e)
        {
            await JoinSelectedAreaAsync();
        }

        private async void lstAreas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await JoinSelectedAreaAsync();
        }

        private async Task JoinSelectedAreaAsync()
        {
            AOClient? profileClient = currentClient;
            if (profileClient == null)
            {
                return;
            }

            if (lstAreas.SelectedItem is not AreaNavigatorListItem selectedAreaItem
                || string.IsNullOrWhiteSpace(selectedAreaItem.Name))
            {
                return;
            }

            AOClient? networkClient = GetTargetClientForNetwork(profileClient);
            if (networkClient == null)
            {
                return;
            }

            if (useSingleInternalClient)
            {
                ApplyProfileToSingleInternalClient(profileClient);
            }

            await networkClient.SetArea(selectedAreaItem.Name);
            RefreshAreaNavigatorForCurrentClient();
        }

        private bool _altGrActive = false;

        private class AreaNavigatorListItem
        {
            public string Name { get; set; } = string.Empty;
            public string StatusAndCmLine { get; set; } = string.Empty;
            public string PlayersAndLockLine { get; set; } = string.Empty;
            public Brush RowBackground { get; set; } = Brushes.Transparent;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            HandleKonamiCode(e);

            // When RightAlt is pressed, mark AltGr as active.
            if (e.Key == Key.RightAlt)
            {
                _altGrActive = true;
            }

            // Determine if AltGr is active either because our flag is set
            // or because the RightAlt key is physically down.
            bool isAltGrActive = _altGrActive || ((Keyboard.GetKeyStates(Key.RightAlt) & KeyStates.Down) == KeyStates.Down);

            // If the Control modifier is pressed but AltGr is active, skip processing.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && !isAltGrActive)
            {
                int index = -1;

                // Check if the pressed key is a digit on the main keyboard.
                if (e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    index = e.Key - Key.D1;
                }
                else if (e.Key == Key.D0)
                {
                    index = 9;
                }
                // Or a digit on the numpad.
                else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
                {
                    index = e.Key - Key.NumPad1;
                }
                else if (e.Key == Key.NumPad0)
                {
                    index = 9;
                }

                // If a valid digit was pressed and a corresponding client exists, process the selection.
                if (index >= 0 && index < clients.Count)
                {
                    AOClient client = clients.Values.ElementAt(index);
                    SelectClient(client);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Tab)
            {
                if (Keyboard.FocusedElement is TextBox focusedTextBox)
                {
                    switch (focusedTextBox.Name)
                    {
                        case "txtICMessage":
                            OOCLogControl.txtOOCMessage.Focus();
                            e.Handled = true;
                            break;
                        case "txtOOCMessage":
                            ICMessageSettingsControl.txtICMessage.Focus();
                            e.Handled = true; // Prevent default tab behavior
                            break;
                        case "txtICShowname":
                            OOCLogControl.txtOOCShowname.Focus();
                            e.Handled = true; // Prevent default tab behavior
                            break;
                        case "txtOOCShowname":
                            ICMessageSettingsControl.txtICShowname.Focus();
                            e.Handled = true; // Prevent default tab behavior
                            break;
                    }
                }
            }

            base.OnPreviewKeyDown(e);
        }

        private void HandleKonamiCode(KeyEventArgs e)
        {
            if (e.IsRepeat)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            Key expectedKey = KonamiCodeSequence[konamiProgress];

            if (key == expectedKey)
            {
                konamiProgress++;
                if (konamiProgress >= KonamiCodeSequence.Length)
                {
                    konamiProgress = 0;
                    OpenDoomWindow();
                }

                return;
            }

            konamiProgress = key == KonamiCodeSequence[0] ? 1 : 0;
        }

        private void OpenDoomWindow()
        {
            DoomWindow doomWindow = new DoomWindow
            {
                Owner = this
            };
            doomWindow.Show();
            doomWindow.Activate();
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            // When the RightAlt key is released, clear the AltGr flag.
            if (e.Key == Key.RightAlt)
            {
                _altGrActive = false;
            }
            base.OnPreviewKeyUp(e);
        }
    }
}
