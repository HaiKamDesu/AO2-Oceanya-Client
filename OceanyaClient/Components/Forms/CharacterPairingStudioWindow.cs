using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.Viewport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OceanyaClient.Components
{
    internal sealed class CharacterPairingStudioWindow : OceanyaWindowContentControl
    {
        private readonly AOClient profileClient;
        private readonly AOClient networkClient;
        private readonly AO2ViewportControl viewport;
        private readonly ICMessage previewMessage;
        private readonly IReadOnlyList<AOClient> peerClients;
        private readonly List<PairCandidate> allCandidates;
        private readonly ListBox pairList;
        private readonly TextBox searchBox;
        private readonly TextBox myXTextBox;
        private readonly TextBox myYTextBox;
        private readonly TextBox partnerXTextBox;
        private readonly TextBox partnerYTextBox;
        private readonly ToggleButton meFrontButton;
        private readonly ToggleButton partnerFrontButton;
        private readonly CheckBox partnerFlipCheckBox;
        private readonly TextBlock statusText;
        private readonly TextBlock selectedText;
        private PairCandidate? selectedCandidate;
        private (int Horizontal, int Vertical) myOffset;
        private (int Horizontal, int Vertical) partnerPreviewOffset;
        private bool updatingText;
        private Point? dragStart;
        private (int Horizontal, int Vertical) dragStartMyOffset;
        private (int Horizontal, int Vertical) dragStartPartnerOffset;

        private CharacterPairingStudioWindow(AOClient profileClient, AOClient networkClient, IReadOnlyList<AOClient>? peerClients)
        {
            if (profileClient.currentINI == null)
            {
                throw new InvalidOperationException("A character must be selected before editing pairing.");
            }

            this.profileClient = profileClient;
            this.networkClient = networkClient;
            this.peerClients = peerClients ?? Array.Empty<AOClient>();
            CustomConsole.Info(
                $"[PAIR] Studio open. profile={profileClient.clientName} profileIniPuppetID={profileClient.iniPuppetID} network={networkClient.clientName} networkIniPuppetID={networkClient.iniPuppetID} networkIniPuppet=\"{networkClient.iniPuppetName}\" currentArea=\"{networkClient.CurrentArea}\" curPos=\"{networkClient.curPos}\" serverChars={networkClient.ServerCharacterAvailability.Count} currentAreaPlayers={networkClient.CurrentAreaPlayers.Count} lastParse={networkClient.LastGetAreaParseSucceeded} connected={networkClient.IsTransportConnected} sameClient={ReferenceEquals(profileClient, networkClient)} peers={this.peerClients.Count}",
                CustomConsole.LogCategory.PairingStudio);
            myOffset = profileClient.SelfOffset;
            partnerPreviewOffset = GuessPartnerPreviewOffset(myOffset);
            previewMessage = ICMessageSettings.BuildPreviewICMessage(
                profileClient.currentEmote ?? profileClient.currentINI.configINI.Emotions.Values.FirstOrDefault() ?? new Emote(0),
                profileClient);
            previewMessage.Message = "Pairing preview";
            previewMessage.SelfOffset = myOffset;

            viewport = new AO2ViewportControl();
            AOClient syntheticClient = new AOClient("ws://localhost:1")
            {
                curBG = networkClient.curBG,
                curPos = networkClient.curPos
            };
            viewport.AttachClient(syntheticClient, null, null, null);

            Width = 860;
            Height = 640;
            MinWidth = 720;
            MinHeight = 520;
            Title = "Pairing Studio";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            allCandidates = BuildCandidates(networkClient, this.peerClients);
            pairList = CreatePairList();
            searchBox = CreateSearchBox();
            myXTextBox = CreateOffsetTextBox("Pairing.MyX", "Your horizontal offset. Positive values move right.");
            myYTextBox = CreateOffsetTextBox("Pairing.MyY", "Your vertical offset. Positive values move down.");
            partnerXTextBox = CreateOffsetTextBox("Pairing.PartnerX", "Preview-only partner horizontal offset. Your partner must set their own offset on their client.");
            partnerYTextBox = CreateOffsetTextBox("Pairing.PartnerY", "Preview-only partner vertical offset. Your partner must set their own offset on their client.");
            meFrontButton = CreateSegmentButton("Me in front", "Your character appears over the paired character when you speak.");
            partnerFrontButton = CreateSegmentButton("Partner in front", "The paired character appears over your character when you speak.");
            partnerFlipCheckBox = new CheckBox
            {
                Content = "Flip partner preview",
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Preview only. The live paired character uses your partner's own flip setting from their latest IC message."
            };
            partnerFlipCheckBox.Checked += (_, _) => RefreshPreview();
            partnerFlipCheckBox.Unchecked += (_, _) => RefreshPreview();
            statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(196, 211, 220)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0)
            };
            selectedText = new TextBlock
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            Content = BuildContent();
            networkClient.OnCurrentAreaPlayersUpdated += Client_OnCurrentAreaPlayersUpdated;
            Unloaded += (_, _) => networkClient.OnCurrentAreaPlayersUpdated -= Client_OnCurrentAreaPlayersUpdated;
            Loaded += async (_, _) =>
            {
                RefreshCandidateList();
                SelectInitialCandidate();
                UpdateOffsetText();
                RefreshPreview();
                MarkAutomationReady();
                await RefreshAreaPlayersAsync();
            };
        }

        public override string HeaderText => "PAIRING STUDIO";

        public override bool IsUserResizeEnabled => true;

        public PairingStudioResult? Result { get; private set; }

        public static PairingStudioResult? ShowDialog(Window? owner, AOClient profileClient, AOClient networkClient, IReadOnlyList<AOClient>? peerClients = null)
        {
            CharacterPairingStudioWindow content = new CharacterPairingStudioWindow(profileClient, networkClient, peerClients)
            {
                Owner = owner
            };

            bool? result = OceanyaWindowManager.ShowDialog(content, new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = "Pairing Studio",
                HeaderText = content.HeaderText,
                Width = content.Width,
                Height = content.Height,
                MinWidth = content.MinWidth,
                MinHeight = content.MinHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = true,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyMargin = new Thickness(0)
            });

            return result == true ? content.Result : null;
        }

        private Grid BuildContent()
        {
            Grid root = new Grid
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(13, 16, 19),
                    Color.FromRgb(31, 38, 43),
                    90),
                Focusable = true
            };
            root.MouseDown += (_, _) => root.Focus();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

            Border pickerPanel = CreatePanel();
            pickerPanel.Margin = new Thickness(14, 14, 7, 10);
            pickerPanel.Child = BuildPickerPanel();
            Grid.SetColumn(pickerPanel, 0);
            root.Children.Add(pickerPanel);

            Border previewPanel = CreatePanel();
            previewPanel.Margin = new Thickness(7, 14, 7, 10);
            previewPanel.Child = BuildPreviewPanel();
            Grid.SetColumn(previewPanel, 1);
            root.Children.Add(previewPanel);

            Border controlsPanel = CreatePanel();
            controlsPanel.Margin = new Thickness(7, 14, 14, 10);
            controlsPanel.Child = BuildControlsPanel();
            Grid.SetColumn(controlsPanel, 2);
            root.Children.Add(controlsPanel);

            Border commandPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(18, 23, 27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 75, 84)),
                BorderThickness = new Thickness(1, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 12),
                Child = BuildCommandRow()
            };
            Grid.SetRow(commandPanel, 1);
            Grid.SetColumnSpan(commandPanel, 3);
            root.Children.Add(commandPanel);

            return root;
        }

        private StackPanel BuildPickerPanel()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(CreatePanelTitle("Partner"));
            panel.Children.Add(searchBox);
            panel.Children.Add(pairList);
            return panel;
        }

        private Grid BuildPreviewPanel()
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            TextBlock title = CreatePanelTitle("Live Preview");
            title.ToolTip = "Left-drag the preview to move your character. Hold Shift while dragging to move the partner preview.";
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            Grid previewHost = new Grid
            {
                Background = Brushes.Black,
                ClipToBounds = true,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Viewbox viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(12),
                Child = viewport
            };
            Border dragLayer = new Border
            {
                Background = Brushes.Transparent,
                ToolTip = "Drag to move your character. Hold Shift and drag to adjust the partner preview offset."
            };
            dragLayer.MouseLeftButtonDown += PreviewDragLayer_MouseLeftButtonDown;
            dragLayer.MouseMove += PreviewDragLayer_MouseMove;
            dragLayer.MouseLeftButtonUp += PreviewDragLayer_MouseLeftButtonUp;
            previewHost.Children.Add(viewbox);
            previewHost.Children.Add(dragLayer);
            Grid.SetRow(previewHost, 1);
            grid.Children.Add(previewHost);
            return grid;
        }

        private StackPanel BuildControlsPanel()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(CreatePanelTitle("Setup"));
            panel.Children.Add(selectedText);

            panel.Children.Add(CreateSectionLabel("My position"));
            panel.Children.Add(CreateOffsetRow(myXTextBox, myYTextBox));

            StackPanel presetRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            presetRow.Children.Add(CreateSmallButton("Left", "Move yourself left and preview your partner on the right.", () => ApplyPreset((-18, 0), (18, 0))));
            presetRow.Children.Add(CreateSmallButton("Right", "Move yourself right and preview your partner on the left.", () => ApplyPreset((18, 0), (-18, 0))));
            presetRow.Children.Add(CreateSmallButton("Stack", "Keep both characters centered and use layer order for overlap.", () => ApplyPreset((0, 0), (0, 0))));
            panel.Children.Add(presetRow);

            panel.Children.Add(CreateSectionLabel("Layer order"));
            StackPanel orderRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };
            orderRow.Children.Add(meFrontButton);
            orderRow.Children.Add(partnerFrontButton);
            panel.Children.Add(orderRow);

            panel.Children.Add(CreateSectionLabel("Partner preview"));
            panel.Children.Add(CreateOffsetRow(partnerXTextBox, partnerYTextBox));
            panel.Children.Add(partnerFlipCheckBox);
            panel.Children.Add(statusText);
            return panel;
        }

        private UIElement BuildCommandRow()
        {
            DockPanel row = new DockPanel { LastChildFill = true };

            TextBlock hint = new TextBlock
            {
                Text = "Pairing becomes visible after both players select each other and send IC from the same position.",
                Foreground = new SolidColorBrush(Color.FromRgb(154, 171, 181)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            Button cancelButton = CreateCommandButton("Cancel");
            cancelButton.Click += (_, _) => RequestHostClose(false);

            Button clearButton = CreateCommandButton("Clear Pair");
            clearButton.Margin = new Thickness(0, 0, 8, 0);
            clearButton.ToolTip = "Stop sending pair target data in future IC messages.";
            clearButton.Click += (_, _) =>
            {
                Result = new PairingStudioResult(-1, string.Empty, 0, myOffset);
                RequestHostClose(true);
            };

            Button saveButton = CreateCommandButton("Save Pair");
            saveButton.Margin = new Thickness(0, 0, 8, 0);
            saveButton.Background = new SolidColorBrush(Color.FromRgb(36, 75, 60));
            saveButton.BorderBrush = new SolidColorBrush(Color.FromRgb(89, 151, 122));
            saveButton.ToolTip = "Save this pair target and your offset for future IC messages.";
            saveButton.Click += (_, _) =>
            {
                int targetId = selectedCandidate?.CharacterId ?? -1;
                string targetName = selectedCandidate?.Name ?? string.Empty;
                Result = new PairingStudioResult(targetId, targetName, CurrentLayerOrder, myOffset);
                RequestHostClose(true);
            };

            DockPanel.SetDock(cancelButton, Dock.Right);
            DockPanel.SetDock(clearButton, Dock.Right);
            DockPanel.SetDock(saveButton, Dock.Right);
            row.Children.Add(cancelButton);
            row.Children.Add(clearButton);
            row.Children.Add(saveButton);
            row.Children.Add(hint);
            return row;
        }

        private void RefreshCandidateList()
        {
            string filter = searchBox.Text?.Trim() ?? string.Empty;
            IEnumerable<PairCandidate> filtered = allCandidates;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                filtered = filtered.Where(candidate =>
                    candidate.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            List<PairCandidate> visibleCandidates = filtered.ToList();
            pairList.ItemsSource = visibleCandidates;
            CustomConsole.Info(
                $"[PAIR] Candidate list refreshed. total={allCandidates.Count} visible={visibleCandidates.Count} filter=\"{filter}\" selected={(selectedCandidate == null ? "<none>" : $"[{selectedCandidate.CharacterId}] {selectedCandidate.Name}")}",
                CustomConsole.LogCategory.PairingStudio);
        }

        private async Task RefreshAreaPlayersAsync()
        {
            if (!networkClient.IsTransportConnected)
            {
                CustomConsole.Warning(
                    $"[PAIR] Skipping internal /getarea refresh because transport is not connected. profile={profileClient.clientName} network={networkClient.clientName}",
                    category: CustomConsole.LogCategory.PairingStudio);
                return;
            }

            statusText.Text = "Refreshing current area players...";
            CustomConsole.Info(
                $"[PAIR] Requesting current-area players. profile={profileClient.clientName} network={networkClient.clientName} currentArea=\"{networkClient.CurrentArea}\" serverChars={networkClient.ServerCharacterAvailability.Count}",
                CustomConsole.LogCategory.PairingStudio);
            await networkClient.RequestCurrentAreaPlayersRefreshAsync();
            CustomConsole.Info(
                $"[PAIR] Returned from /getarea send. lastParse={networkClient.LastGetAreaParseSucceeded} currentAreaPlayers={networkClient.CurrentAreaPlayers.Count}",
                CustomConsole.LogCategory.PairingStudio);
            RebuildCandidatesFromClient();
        }

        private void Client_OnCurrentAreaPlayersUpdated(IReadOnlyList<Player> players, bool parsedSuccessfully)
        {
            CustomConsole.Info(
                $"[PAIR] Area player update event. parsed={parsedSuccessfully} players={players.Count} names=\"{string.Join(", ", players.Select(player => $"[{player.CharacterId}] {player.ICCharacterName}"))}\"",
                CustomConsole.LogCategory.PairingStudio);
            Dispatcher.Invoke(RebuildCandidatesFromClient);
        }

        private void RebuildCandidatesFromClient()
        {
            allCandidates.Clear();
            allCandidates.AddRange(BuildCandidates(networkClient, peerClients));
            CustomConsole.Info(
                $"[PAIR] Rebuilt candidates. candidates={allCandidates.Count} currentAreaPlayers={networkClient.CurrentAreaPlayers.Count} lastParse={networkClient.LastGetAreaParseSucceeded} serverChars={networkClient.ServerCharacterAvailability.Count}",
                CustomConsole.LogCategory.PairingStudio);
            if (allCandidates.Count > 0)
            {
                CustomConsole.Info(
                    "[PAIR] Candidates: " + string.Join(", ", allCandidates.Select(candidate => $"[{candidate.CharacterId}] {candidate.Name} enabled={candidate.CanSelect} status=\"{candidate.Status}\"")),
                    CustomConsole.LogCategory.PairingStudio);
            }
            else
            {
                CustomConsole.Warning(
                    "[PAIR] Candidate rebuild produced zero entries.",
                    category: CustomConsole.LogCategory.PairingStudio);
            }

            RefreshCandidateList();
            SelectInitialCandidate();
            RefreshStatus();
        }

        private void SelectInitialCandidate()
        {
            PairCandidate? initial = allCandidates.FirstOrDefault(candidate => candidate.CharacterId == profileClient.PairTargetCharId)
                ?? allCandidates.FirstOrDefault(candidate => string.Equals(candidate.Name, profileClient.PairTargetCharacterName, StringComparison.OrdinalIgnoreCase));
            if (initial != null)
            {
                pairList.SelectedItem = initial;
            }
            else
            {
                UpdateSelectedCandidate(null);
            }

            SetLayerOrder(Math.Clamp(profileClient.PairLayerOrder, 0, 1));
        }

        private void UpdateSelectedCandidate(PairCandidate? candidate)
        {
            selectedCandidate = candidate;
            selectedText.Text = candidate == null
                ? "No partner selected"
                : $"{candidate.Name}  ·  slot {candidate.CharacterId}";
            RefreshStatus();
            RefreshPreview();
        }

        private void RefreshStatus()
        {
            bool supportsPairing = networkClient.ServerFeatures.Contains("CCCC_IC_SUPPORT", StringComparer.OrdinalIgnoreCase);
            bool supportsOrder = networkClient.ServerFeatures.Contains("EFFECTS", StringComparer.OrdinalIgnoreCase);
            if (!supportsPairing)
            {
                statusText.Text = "This server does not advertise CCCC pairing. Save is allowed, but outgoing IC will not include pair data until the server supports it.";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(235, 182, 112));
                return;
            }

            if (selectedCandidate == null)
            {
                statusText.Text = "Choose a partner, then save. Ask them to select your character too.";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(196, 211, 220));
                return;
            }

            if (!selectedCandidate.CanSelect)
            {
                statusText.Text = selectedCandidate.Tooltip;
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(235, 136, 124));
                return;
            }

            if (!networkClient.ServerFeatures.Contains("Y_OFFSET", StringComparer.OrdinalIgnoreCase)
                && myOffset.Vertical != 0)
            {
                statusText.Text = "This server does not advertise vertical offsets. Your Y offset may be ignored.";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(235, 182, 112));
                return;
            }

            if (selectedCandidate.PositionKnown
                && !string.Equals(selectedCandidate.Position, networkClient.curPos, StringComparison.OrdinalIgnoreCase))
            {
                statusText.Text = "Different position: pairing will not appear until both clients share the same AO2 position.";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(235, 182, 112));
                return;
            }

            string orderNote = supportsOrder
                ? "Layer order will be sent."
                : "This server does not advertise effect support, so layer order may be ignored.";
            string artNote = selectedCandidate.HasLocalArt
                ? "Local preview art found."
                : "Local preview art is missing; live viewers with the files will still render it.";
            string handshakeNote = FindInternalPartner(selectedCandidate) == null
                ? "Waiting for partner to select you."
                : "Internal partner found. Send one IC sync line from each client to make the pair visible.";
            statusText.Text = $"{handshakeNote} {artNote} {orderNote}";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(146, 220, 176));
        }

        private void RefreshPreview()
        {
            previewMessage.SelfOffset = myOffset;
            if (selectedCandidate == null || selectedCandidate.CharacterId < 0)
            {
                previewMessage.OtherCharId = -1;
                previewMessage.OtherCharIdRaw = string.Empty;
                previewMessage.OtherName = string.Empty;
                previewMessage.OtherEmote = string.Empty;
                previewMessage.OtherOffset = 0;
                previewMessage.OtherOffsetVertical = 0;
            }
            else
            {
                previewMessage.OtherCharId = selectedCandidate.CharacterId;
                previewMessage.OtherCharIdRaw = $"{selectedCandidate.CharacterId}^{CurrentLayerOrder}";
                previewMessage.OtherName = selectedCandidate.Name;
                previewMessage.OtherEmote = ResolvePreviewEmoteName(selectedCandidate.Name);
                previewMessage.OtherOffset = partnerPreviewOffset.Horizontal;
                previewMessage.OtherOffsetVertical = partnerPreviewOffset.Vertical;
                previewMessage.OtherFlip = partnerFlipCheckBox.IsChecked == true;
            }

            viewport.PreviewMessage(previewMessage);
        }

        private void ApplyPreset((int Horizontal, int Vertical) mine, (int Horizontal, int Vertical) partner)
        {
            myOffset = mine;
            partnerPreviewOffset = partner;
            UpdateOffsetText();
            RefreshPreview();
        }

        private void SetLayerOrder(int order)
        {
            meFrontButton.IsChecked = order == 0;
            partnerFrontButton.IsChecked = order == 1;
            RefreshStatus();
            RefreshPreview();
        }

        private int CurrentLayerOrder => partnerFrontButton.IsChecked == true ? 1 : 0;

        private void OffsetTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (updatingText)
            {
                return;
            }

            if (!TryParseTextBox(myXTextBox, out int myX)
                || !TryParseTextBox(myYTextBox, out int myY)
                || !TryParseTextBox(partnerXTextBox, out int partnerX)
                || !TryParseTextBox(partnerYTextBox, out int partnerY))
            {
                return;
            }

            myOffset = (myX, myY);
            partnerPreviewOffset = (partnerX, partnerY);
            RefreshPreview();
        }

        private void UpdateOffsetText()
        {
            updatingText = true;
            myXTextBox.Text = myOffset.Horizontal.ToString(CultureInfo.InvariantCulture);
            myYTextBox.Text = myOffset.Vertical.ToString(CultureInfo.InvariantCulture);
            partnerXTextBox.Text = partnerPreviewOffset.Horizontal.ToString(CultureInfo.InvariantCulture);
            partnerYTextBox.Text = partnerPreviewOffset.Vertical.ToString(CultureInfo.InvariantCulture);
            updatingText = false;
        }

        private void PreviewDragLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement element)
            {
                return;
            }

            dragStart = e.GetPosition(element);
            dragStartMyOffset = myOffset;
            dragStartPartnerOffset = partnerPreviewOffset;
            element.CaptureMouse();
        }

        private void PreviewDragLayer_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragStart == null || sender is not FrameworkElement element || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(element);
            double width = Math.Max(1, element.ActualWidth);
            double height = Math.Max(1, element.ActualHeight);
            int deltaX = (int)Math.Round((current.X - dragStart.Value.X) / width * 100.0);
            int deltaY = (int)Math.Round((current.Y - dragStart.Value.Y) / height * 100.0);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                partnerPreviewOffset = (dragStartPartnerOffset.Horizontal + deltaX, dragStartPartnerOffset.Vertical + deltaY);
            }
            else
            {
                myOffset = (dragStartMyOffset.Horizontal + deltaX, dragStartMyOffset.Vertical + deltaY);
            }

            UpdateOffsetText();
            RefreshPreview();
        }

        private void PreviewDragLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            dragStart = null;
            if (sender is UIElement element)
            {
                element.ReleaseMouseCapture();
            }
        }

        private static List<PairCandidate> BuildCandidates(AOClient client, IReadOnlyList<AOClient> peerClients)
        {
            IReadOnlyDictionary<string, bool> serverCharacters = client.ServerCharacterAvailability;
            List<PairCandidate> candidates = new List<PairCandidate>();
            Dictionary<int, AOClient> internalBySlot = peerClients
                .Where(peer => peer != null && !ReferenceEquals(peer, client) && peer.iniPuppetID >= 0)
                .GroupBy(peer => peer.iniPuppetID)
                .ToDictionary(group => group.Key, group => group.First());

            if (client.LastGetAreaParseSucceeded && client.CurrentAreaPlayers.Count > 0)
            {
                CustomConsole.Info(
                    $"[PAIR] Building candidates from current-area players. count={client.CurrentAreaPlayers.Count}",
                    CustomConsole.LogCategory.PairingStudio);
                foreach (Player player in client.CurrentAreaPlayers)
                {
                    int serverCharacterId = ResolveServerCharacterId(client, player.ICCharacterName, player.CharacterId);
                    candidates.Add(BuildCandidate(
                        client,
                        internalBySlot,
                        serverCharacterId,
                        player.ICCharacterName,
                        displayName: string.IsNullOrWhiteSpace(player.OOCShowname)
                            ? player.ICCharacterName
                            : player.OOCShowname.Trim(),
                        rawLine: string.IsNullOrWhiteSpace(player.RawGetAreaLine)
                            ? $"[{player.CharacterId}] {player.ICCharacterName}"
                            : player.RawGetAreaLine,
                        available: true,
                        fromCurrentArea: true));
                }
            }
            else
            {
                CustomConsole.Warning(
                    $"[PAIR] Building candidates from server roster fallback. lastParse={client.LastGetAreaParseSucceeded} currentAreaPlayers={client.CurrentAreaPlayers.Count} serverChars={serverCharacters.Count}",
                    category: CustomConsole.LogCategory.PairingStudio);
                foreach ((KeyValuePair<string, bool> entry, int index) in serverCharacters.Select((entry, index) => (entry, index)))
                {
                    candidates.Add(BuildCandidate(
                        client,
                        internalBySlot,
                        index,
                        entry.Key,
                        displayName: entry.Key,
                        rawLine: $"[{index}] {entry.Key}",
                        available: entry.Value,
                        fromCurrentArea: false));
                }
            }

            return candidates
                .GroupBy(candidate => candidate.CharacterId)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.SortRank)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int ResolveServerCharacterId(AOClient client, string characterName, int fallbackId)
        {
            int index = 0;
            foreach (string serverCharacterName in client.ServerCharacterAvailability.Keys)
            {
                if (string.Equals(serverCharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                {
                    if (fallbackId != index)
                    {
                        CustomConsole.Info(
                            $"[PAIR] Resolved /getarea row id to server character id. rowId={fallbackId} character=\"{characterName}\" serverCharId={index}",
                            CustomConsole.LogCategory.PairingStudio);
                    }

                    return index;
                }

                index++;
            }

            CustomConsole.Warning(
                $"[PAIR] Could not map /getarea character \"{characterName}\" to server roster; using row id {fallbackId} as fallback.",
                category: CustomConsole.LogCategory.PairingStudio);
            return fallbackId;
        }

        private static PairCandidate BuildCandidate(
            AOClient client,
            IReadOnlyDictionary<int, AOClient> internalBySlot,
            int characterId,
            string name,
            string displayName,
            string rawLine,
            bool available,
            bool fromCurrentArea)
        {
            CharacterFolder? local = ResolveLocalCharacter(name);
            internalBySlot.TryGetValue(characterId, out AOClient? internalPeer);
            bool isSelf = characterId == client.iniPuppetID;
            bool samePosition = internalPeer == null
                || string.Equals(internalPeer.curPos, client.curPos, StringComparison.OrdinalIgnoreCase);
            bool canSelect = !isSelf && available && characterId >= 0 && samePosition;
            string status = string.IsNullOrWhiteSpace(rawLine)
                ? $"[{characterId}] {name}"
                : rawLine;
            if (internalPeer != null)
            {
                status += " · internal client " + internalPeer.clientName;
            }

            if (!available)
            {
                status += " · unavailable";
            }

            if (!samePosition)
            {
                status += " · different position";
            }

            if (local == null)
            {
                status += " · no local art";
            }

            string tooltip = isSelf
                ? "You cannot pair a client with itself through the normal AO2 mechanism."
                : !available
                    ? "This server slot is unavailable in the fallback roster."
                    : !samePosition
                        ? "Different position: pairing will not appear until both clients share the same AO2 position."
                        : "Select this character as your AO2 pair target.";

            return new PairCandidate(
                characterId,
                name,
                displayName,
                status,
                local != null,
                canSelect,
                tooltip,
                internalPeer?.curPos ?? string.Empty,
                internalPeer != null,
                internalPeer != null ? 0 : fromCurrentArea ? 1 : 2);
        }

        private AOClient? FindInternalPartner(PairCandidate candidate)
        {
            return peerClients.FirstOrDefault(peer =>
                peer != null
                && !ReferenceEquals(peer, profileClient)
                && peer.iniPuppetID == candidate.CharacterId);
        }

        private static CharacterFolder? ResolveLocalCharacter(string name)
        {
            return CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(character.configINI?.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolvePreviewEmoteName(string characterName)
        {
            CharacterFolder? character = ResolveLocalCharacter(characterName);
            return character?.configINI?.Emotions?.Values.FirstOrDefault()?.Animation ?? string.Empty;
        }

        private static (int Horizontal, int Vertical) GuessPartnerPreviewOffset((int Horizontal, int Vertical) current)
        {
            return current.Horizontal == 0 && current.Vertical == 0
                ? (18, 0)
                : (-current.Horizontal, current.Vertical);
        }

        private ListBox CreatePairList()
        {
            ListBox list = new ListBox
            {
                Margin = new Thickness(0, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(19, 24, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 94, 104)),
                Foreground = Brushes.White,
                MinHeight = 360
            };
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(list, true);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            AutomationProperties.SetName(list, "Pair target list");
            AutomationProperties.SetAutomationId(list, "Pairing.TargetList");
            list.SelectionChanged += (_, _) => UpdateSelectedCandidate(list.SelectedItem as PairCandidate);
            list.ItemTemplate = BuildPairCandidateTemplate();
            list.ItemContainerStyle = BuildPairCandidateItemStyle();
            return list;
        }

        private TextBox CreateSearchBox()
        {
            TextBox box = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(26, 32, 37)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 94, 104)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Filter the server character list."
            };
            AutomationProperties.SetAutomationId(box, "Pairing.Search");
            box.TextChanged += (_, _) => RefreshCandidateList();
            return box;
        }

        private TextBox CreateOffsetTextBox(string automationId, string tooltip)
        {
            TextBox box = new TextBox
            {
                Width = 52,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(26, 32, 37)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 94, 104)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = tooltip
            };
            AutomationProperties.SetAutomationId(box, automationId);
            box.TextChanged += OffsetTextBox_TextChanged;
            box.LostFocus += (_, _) => UpdateOffsetText();
            return box;
        }

        private ToggleButton CreateSegmentButton(string text, string tooltip)
        {
            ToggleButton button = new ToggleButton
            {
                Content = text,
                Height = 30,
                MinWidth = 110,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(31, 39, 45)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 94, 104)),
                ToolTip = tooltip
            };
            button.Checked += (_, _) =>
            {
                if (ReferenceEquals(button, meFrontButton))
                {
                    partnerFrontButton.IsChecked = false;
                }
                else if (ReferenceEquals(button, partnerFrontButton))
                {
                    meFrontButton.IsChecked = false;
                }

                if (meFrontButton != null && partnerFrontButton != null && meFrontButton.IsChecked != true && partnerFrontButton.IsChecked != true)
                {
                    meFrontButton.IsChecked = true;
                }

                RefreshPreview();
            };
            return button;
        }

        private static Button CreateSmallButton(string text, string tooltip, Action action)
        {
            Button button = CreateCommandButton(text);
            button.MinWidth = 58;
            button.Height = 28;
            button.Margin = new Thickness(0, 0, 8, 0);
            button.ToolTip = tooltip;
            button.Click += (_, _) => action();
            return button;
        }

        private static StackPanel CreateOffsetRow(TextBox xTextBox, TextBox yTextBox)
        {
            StackPanel row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };
            row.Children.Add(CreateTinyLabel("X"));
            row.Children.Add(xTextBox);
            row.Children.Add(CreateTinyLabel("Y"));
            row.Children.Add(yTextBox);
            return row;
        }

        private static TextBlock CreateTinyLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 171, 181)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }

        private static TextBlock CreatePanelTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold
            };
        }

        private static TextBlock CreateSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 171, 181)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 16, 0, 0)
            };
        }

        private static Border CreatePanel()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(236, 24, 30, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 90, 101)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12)
            };
        }

        private static Button CreateCommandButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 86,
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(35, 43, 49)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(83, 101, 112)),
                Foreground = Brushes.White
            };
        }

        private static DataTemplate BuildPairCandidateTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.MarginProperty, new Thickness(8, 6, 8, 6));

            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PairCandidate.DisplayName)));
            name.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            root.AppendChild(name);

            FrameworkElementFactory status = new FrameworkElementFactory(typeof(TextBlock));
            status.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PairCandidate.Status)));
            status.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(154, 171, 181)));
            status.SetValue(TextBlock.FontSizeProperty, 11.0);
            root.AppendChild(status);

            return new DataTemplate
            {
                VisualTree = root
            };
        }

        private static Style BuildPairCandidateItemStyle()
        {
            Style style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(PairCandidate.CanSelect))));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(PairCandidate.Tooltip))));
            return style;
        }

        private static bool TryParseTextBox(TextBox textBox, out int value)
        {
            return int.TryParse(
                textBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        internal sealed record PairingStudioResult(
            int TargetCharId,
            string TargetCharacterName,
            int LayerOrder,
            (int Horizontal, int Vertical) SelfOffset);

        private sealed record PairCandidate(
            int CharacterId,
            string Name,
            string DisplayName,
            string Status,
            bool HasLocalArt,
            bool CanSelect,
            string Tooltip,
            string Position,
            bool PositionKnown,
            int SortRank)
        {
            public override string ToString()
            {
                return Name;
            }
        }
    }
}
