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
using System.Windows.Threading;

namespace OceanyaClient.Components
{
    internal sealed class CharacterPairingStudioWindow : OceanyaWindowContentControl
    {
        private readonly AOClient profileClient;
        private readonly AOClient networkClient;
        private readonly AOClient previewSceneClient;
        private readonly AO2ViewportControl viewport;
        private readonly ICMessage previewMessage;
        private readonly IReadOnlyList<AOClient> peerClients;
        private readonly List<PairCandidate> allCandidates;
        private readonly ListBox pairList;
        private readonly TextBox myXTextBox;
        private readonly TextBox myYTextBox;
        private readonly TextBox partnerXTextBox;
        private readonly TextBox partnerYTextBox;
        private readonly ToggleButton meFrontButton;
        private readonly ToggleButton partnerFrontButton;
        private readonly TextBlock selfLineText;
        private readonly StackPanel pairingStepsPanel;
        private readonly Button sendBlankpostButton;
        private readonly Button matchPartnerPositionButton;
        private readonly StackPanel layerOrderSection;
        private readonly StackPanel partnerPreviewSection;
        private readonly DispatcherTimer keyboardNudgeTimer;
        private readonly Dictionary<int, PartnerPreviewState> partnerPreviewStates = new();
        private PairCandidate? selectedCandidate;
        private (int Horizontal, int Vertical) myOffset;
        private (int Horizontal, int Vertical) partnerPreviewOffset;
        private bool partnerPreviewFlip;
        private bool updatingText;
        private bool keyboardNudgeActive;
        private Window? keyboardHostWindow;

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
            previewSceneClient = new AOClient("ws://localhost:1")
            {
                curBG = networkClient.curBG,
                curPos = networkClient.curPos
            };
            viewport.AttachClient(previewSceneClient, null, null, null);

            Width = 960;
            Height = 640;
            MinWidth = 820;
            MinHeight = 520;
            Title = "Pairing Studio";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            allCandidates = BuildCandidates(profileClient, networkClient, this.peerClients);
            pairList = CreatePairList();
            myXTextBox = CreateOffsetTextBox("Pairing.MyX", "Your horizontal offset. Positive values move right.");
            myYTextBox = CreateOffsetTextBox("Pairing.MyY", "Your vertical offset. Positive values move down.");
            partnerXTextBox = CreateOffsetTextBox("Pairing.PartnerX", "Preview-only partner horizontal offset. Your partner must set their own offset on their client.");
            partnerYTextBox = CreateOffsetTextBox("Pairing.PartnerY", "Preview-only partner vertical offset. Your partner must set their own offset on their client.");
            partnerXTextBox.IsReadOnly = true;
            partnerYTextBox.IsReadOnly = true;
            ApplyDisabledPreviewFieldStyle(partnerXTextBox);
            ApplyDisabledPreviewFieldStyle(partnerYTextBox);
            meFrontButton = CreateSegmentButton("Me in front", "Your character appears over the paired character when you speak.", new CornerRadius(15, 0, 0, 15));
            partnerFrontButton = CreateSegmentButton("Partner in front", "The paired character appears over your character when you speak.", new CornerRadius(0, 15, 15, 0));
            selfLineText = new TextBlock
            {
                Text = BuildSelfLine(),
                Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 226)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            pairingStepsPanel = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 0)
            };
            sendBlankpostButton = CreateCommandButton("Send IC Blankpost");
            sendBlankpostButton.Height = 30;
            sendBlankpostButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            sendBlankpostButton.Margin = new Thickness(0, 14, 0, 0);
            sendBlankpostButton.ToolTip = "Send a blank IC message so the server receives your current pair target and layer order.";
            sendBlankpostButton.Click += async (_, _) => await SendBlankpostAsync();
            matchPartnerPositionButton = CreateCommandButton("Match partner");
            matchPartnerPositionButton.Height = 30;
            matchPartnerPositionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            matchPartnerPositionButton.Margin = new Thickness(0, 8, 0, 0);
            matchPartnerPositionButton.Visibility = Visibility.Collapsed;
            matchPartnerPositionButton.Click += (_, _) => MatchPartnerPosition();
            layerOrderSection = new StackPanel();
            partnerPreviewSection = new StackPanel();
            keyboardNudgeTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            keyboardNudgeTimer.Tick += (_, _) => ApplyHeldArrowKeyNudge();

            Content = BuildContent();
            networkClient.OnCurrentAreaPlayersUpdated += Client_OnCurrentAreaPlayersUpdated;
            networkClient.OnICMessageReceived += NetworkClient_OnICMessageReceived;
            Unloaded += (_, _) =>
            {
                networkClient.OnCurrentAreaPlayersUpdated -= Client_OnCurrentAreaPlayersUpdated;
                networkClient.OnICMessageReceived -= NetworkClient_OnICMessageReceived;
                DetachHostKeyboardHandler();
            };
            Loaded += async (_, _) =>
            {
                RefreshCandidateList();
                SelectInitialCandidate();
                UpdateOffsetText();
                RefreshPreview();
                AttachHostKeyboardHandler();
                Focus();
                MarkAutomationReady();
                RefreshPairingSteps();
                await RefreshAreaPlayersAsync();
            };
            PreviewKeyDown += CharacterPairingStudioWindow_PreviewKeyDown;
            PreviewKeyUp += CharacterPairingStudioWindow_PreviewKeyUp;
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
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

            Border leftPanel = CreatePanel();
            leftPanel.Margin = new Thickness(14, 14, 7, 10);
            leftPanel.Child = BuildLeftPanel();
            Grid.SetColumn(leftPanel, 0);
            root.Children.Add(leftPanel);

            Border previewPanel = CreatePanel();
            previewPanel.Margin = new Thickness(7, 14, 7, 10);
            previewPanel.Child = BuildPreviewPanel();
            Grid.SetColumn(previewPanel, 1);
            root.Children.Add(previewPanel);

            Border stepsPanel = CreatePanel();
            stepsPanel.Margin = new Thickness(7, 14, 14, 10);
            stepsPanel.Child = BuildStepsPanel();
            Grid.SetColumn(stepsPanel, 2);
            root.Children.Add(stepsPanel);

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

        private Grid BuildLeftPanel()
        {
            Grid panel = new Grid
            {
                Width = 248,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int row = 0;
            void Add(UIElement element)
            {
                Grid.SetRow(element, row++);
                panel.Children.Add(element);
            }

            Add(CreatePanelTitle("Self"));
            Add(selfLineText);
            Add(CreateSectionLabel("Pairing Partner", "AO2 builds this list from the server character list. The selection sends that server character slot in IC."));
            Add(pairList);
            layerOrderSection.Children.Add(CreateSectionLabel("Layer order", "Controls which paired character draws on top after the server confirms the pair."));
            layerOrderSection.Children.Add(BuildLayerOrderControl());
            Add(layerOrderSection);
            partnerPreviewSection.Children.Add(CreateSectionLabel("Partner preview", "Read-only last known partner offset from their most recent IC or confirmed pair echo."));
            partnerPreviewSection.Children.Add(CreateOffsetRow(partnerXTextBox, partnerYTextBox));
            Add(partnerPreviewSection);
            Add(sendBlankpostButton);
            Add(matchPartnerPositionButton);
            return panel;
        }

        private Grid BuildPreviewPanel()
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock title = CreatePanelTitle("Live Preview");
            title.ToolTip = "Use the arrow buttons, X/Y steppers, or keyboard arrows to move your own offset. Replay restarts the preview animation.";
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
            previewHost.Children.Add(viewbox);
            previewHost.Children.Add(BuildDirectionalOverlay());
            Grid.SetRow(previewHost, 1);
            grid.Children.Add(previewHost);

            StackPanel bottom = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 0)
            };
            bottom.Children.Add(BuildInfoRow());
            bottom.Children.Add(BuildSelfOffsetSection());
            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);
            return grid;
        }

        private StackPanel BuildStepsPanel()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(CreatePanelTitle("Pairing Steps"));
            panel.Children.Add(pairingStepsPanel);
            return panel;
        }

        private UIElement BuildLayerOrderControl()
        {
            Border shell = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(19, 24, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(77, 96, 108)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15),
                Height = 32,
                Margin = new Thickness(0, 8, 0, 0),
                ClipToBounds = true
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(meFrontButton, 0);
            Grid.SetColumn(partnerFrontButton, 2);
            Border divider = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(77, 96, 108))
            };
            Grid.SetColumn(divider, 1);
            grid.Children.Add(meFrontButton);
            grid.Children.Add(divider);
            grid.Children.Add(partnerFrontButton);
            shell.Child = grid;
            return shell;
        }

        private UIElement BuildInfoRow()
        {
            DockPanel row = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            Button replayButton = CreateCommandButton("Replay");
            replayButton.Width = 90;
            replayButton.Height = 28;
            replayButton.Margin = new Thickness(0, 0, 10, 0);
            replayButton.HorizontalAlignment = HorizontalAlignment.Left;
            replayButton.Content = "Replay";
            replayButton.ToolTip = "Restart the live preview animation.";
            replayButton.Click += (_, _) => RefreshPreview();

            TextBlock info = new TextBlock
            {
                Text = $"{profileClient.currentINI?.Name ?? "Character"}  ·  {profileClient.currentEmote?.DisplayID ?? "current emote"}",
                Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 226)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            DockPanel.SetDock(replayButton, Dock.Left);
            row.Children.Add(replayButton);
            row.Children.Add(info);
            return row;
        }

        private UIElement BuildDirectionalOverlay()
        {
            Grid overlay = new Grid
            {
                Margin = new Thickness(20),
                IsHitTestVisible = true
            };
            overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            RepeatButton up = CreateArrowButton("▲", "Move up", () => AdjustSelfOffset(0, -1));
            RepeatButton down = CreateArrowButton("▼", "Move down", () => AdjustSelfOffset(0, 1));
            RepeatButton left = CreateArrowButton("◀", "Move left", () => AdjustSelfOffset(-1, 0));
            RepeatButton right = CreateArrowButton("▶", "Move right", () => AdjustSelfOffset(1, 0));

            Grid.SetRow(up, 0);
            Grid.SetColumn(up, 1);
            Grid.SetRow(down, 2);
            Grid.SetColumn(down, 1);
            Grid.SetRow(left, 1);
            Grid.SetColumn(left, 0);
            Grid.SetRow(right, 1);
            Grid.SetColumn(right, 2);

            overlay.Children.Add(up);
            overlay.Children.Add(down);
            overlay.Children.Add(left);
            overlay.Children.Add(right);
            return overlay;
        }

        private UIElement BuildSelfOffsetSection()
        {
            Border section = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 29, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 78, 91)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10)
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FrameworkElement header = CreateHelpedLabel("Offset (Self)", "Your own pair offset. These X/Y values are sent in IC and control where your character appears relative to your pair.");
            header.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetColumnSpan(header, 4);
            grid.Children.Add(header);

            AddOffsetField(grid, "X", myXTextBox, -1, 1, 0, false);
            AddOffsetField(grid, "Y", myYTextBox, -1, 1, 2, false);

            section.Child = grid;
            return section;
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
                profileClient.ConfirmedPairTargetCharIds.Clear();
                networkClient.ConfirmedPairTargetCharIds.Clear();
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
                ApplyCurrentPairConfigToClients();
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
            RefreshCandidateStates();
            List<PairCandidate> visibleCandidates = allCandidates.ToList();
            pairList.ItemsSource = visibleCandidates;
            CustomConsole.Info(
                $"[PAIR] Candidate list refreshed. total={allCandidates.Count} visible={visibleCandidates.Count} selected={(selectedCandidate == null ? "<none>" : $"[{selectedCandidate.CharacterId}] {selectedCandidate.Name}")}",
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
            allCandidates.AddRange(BuildCandidates(profileClient, networkClient, peerClients));
            selfLineText.Text = BuildSelfLine();
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
            if (candidate != null)
            {
                ApplyPartnerPreviewState(candidate, rerender: false);
            }

            RefreshCandidateStates();
            pairList.Items.Refresh();
            RefreshStatus();
            RefreshPreview();
        }

        private void RefreshStatus()
        {
            RefreshPairingSteps();
            RefreshPairingControlVisibility();
            RefreshSendBlankpostButton();
            RefreshMatchPartnerPositionButton();
        }

        private void RefreshPairingControlVisibility()
        {
            bool isPaired = selectedCandidate != null && IsPartnerPairingBack(selectedCandidate);
            layerOrderSection.Visibility = isPaired ? Visibility.Visible : Visibility.Collapsed;
            partnerPreviewSection.Visibility = isPaired ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshPreview()
        {
            previewMessage.SelfOffset = myOffset;
            bool shouldPreviewPair = selectedCandidate != null && IsPartnerPairingBack(selectedCandidate);
            if (!shouldPreviewPair || selectedCandidate == null || selectedCandidate.CharacterId < 0)
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
                previewMessage.OtherFlip = partnerPreviewFlip;
            }

            viewport.PreviewMessage(previewMessage);
        }

        private void SetLayerOrder(int order)
        {
            meFrontButton.IsChecked = order == 0;
            partnerFrontButton.IsChecked = order == 1;
            RefreshStatus();
            viewport.PreviewPairLayerOrder(CurrentLayerOrder);
        }

        private int CurrentLayerOrder => partnerFrontButton.IsChecked == true ? 1 : 0;

        private void OffsetTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (updatingText)
            {
                return;
            }

            if (!TryParseTextBox(myXTextBox, out int myX)
                || !TryParseTextBox(myYTextBox, out int myY))
            {
                return;
            }

            SetSelfOffset((myX, myY), updateText: false);
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

        private void SetSelfOffset((int Horizontal, int Vertical) offset, bool updateText = true)
        {
            myOffset = offset;
            previewMessage.SelfOffset = myOffset;
            if (updateText)
            {
                UpdateOffsetText();
            }

            viewport.PreviewSelfOffset(myOffset);
            RefreshStatus();
        }

        private void AdjustSelfOffset(int horizontalDelta, int verticalDelta)
        {
            SetSelfOffset((myOffset.Horizontal + horizontalDelta, myOffset.Vertical + verticalDelta));
        }

        private void NetworkClient_OnICMessageReceived(ICMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                PairCandidate? byId = allCandidates.FirstOrDefault(candidate => candidate.CharacterId == message.CharId);
                PairCandidate? byName = byId ?? allCandidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, message.Character, StringComparison.OrdinalIgnoreCase));
                int charId = byName?.CharacterId ?? message.CharId;
                bool isSelfEcho = IsSelfPairEcho(message);

                if (isSelfEcho)
                {
                    HandleSelfPairEcho(message);
                    RefreshCandidateList();
                    RefreshStatus();
                    RefreshPreview();
                    return;
                }

                if (charId < 0)
                {
                    return;
                }

                partnerPreviewStates[charId] = new PartnerPreviewState(
                    message.SelfOffset,
                    message.Flip,
                    message.Emote ?? string.Empty,
                    message.Side ?? string.Empty,
                    message.OtherCharId);
                RememberKnownPartnerPosition(charId, message.Side);
                RememberObservedPartnerPairState(charId, message.OtherCharId);

                if (selectedCandidate != null && selectedCandidate.CharacterId == charId)
                {
                    ApplyPartnerPreviewState(selectedCandidate, rerender: false);
                }

                RefreshCandidateList();
                RefreshStatus();
                RefreshPreview();
            });
        }

        private void RememberObservedPartnerPairState(int partnerCharacterId, int observedPairTargetId)
        {
            if (partnerCharacterId < 0)
            {
                return;
            }

            if (observedPairTargetId < 0)
            {
                return;
            }

            if (observedPairTargetId == GetSelfPairTargetId())
            {
                networkClient.ConfirmedPairTargetCharIds.Add(partnerCharacterId);
                profileClient.ConfirmedPairTargetCharIds.Add(partnerCharacterId);
                return;
            }

            networkClient.ConfirmedPairTargetCharIds.Remove(partnerCharacterId);
            profileClient.ConfirmedPairTargetCharIds.Remove(partnerCharacterId);
        }

        private void HandleSelfPairEcho(ICMessage message)
        {
            if (message.OtherCharId < 0)
            {
                if (selectedCandidate != null && IsCurrentPairConfigSent())
                {
                    networkClient.ConfirmedPairTargetCharIds.Remove(selectedCandidate.CharacterId);
                    profileClient.ConfirmedPairTargetCharIds.Remove(selectedCandidate.CharacterId);
                }
                return;
            }

            networkClient.ConfirmedPairTargetCharIds.Add(message.OtherCharId);
            profileClient.ConfirmedPairTargetCharIds.Add(message.OtherCharId);
            partnerPreviewStates[message.OtherCharId] = new PartnerPreviewState(
                (message.OtherOffset, message.OtherOffsetVertical),
                message.OtherFlip,
                message.OtherEmote ?? string.Empty,
                message.Side ?? string.Empty,
                GetSelfPairTargetId());

            if (selectedCandidate != null && selectedCandidate.CharacterId == message.OtherCharId)
            {
                ApplyPartnerPreviewState(selectedCandidate, rerender: false);
            }
        }

        private void ApplyPartnerPreviewState(PairCandidate candidate, bool rerender)
        {
            if (!IsPartnerPairingBack(candidate))
            {
                partnerPreviewOffset = (0, 0);
                partnerPreviewFlip = false;
                UpdateOffsetText();
                viewport.PreviewPairOffset(partnerPreviewOffset);
                viewport.PreviewPairFlip(partnerPreviewFlip);
                return;
            }

            if (partnerPreviewStates.TryGetValue(candidate.CharacterId, out PartnerPreviewState? state))
            {
                partnerPreviewOffset = state.Offset;
                partnerPreviewFlip = state.Flip;
                if (!string.IsNullOrWhiteSpace(state.Emote))
                {
                    previewMessage.OtherEmote = state.Emote;
                }
            }
            else
            {
                partnerPreviewOffset = (0, 0);
                partnerPreviewFlip = false;
            }

            UpdateOffsetText();
            if (rerender)
            {
                RefreshPreview();
                return;
            }

            viewport.PreviewPairOffset(partnerPreviewOffset);
            viewport.PreviewPairFlip(partnerPreviewFlip);
        }

        private void RefreshCandidateStates()
        {
            foreach (PairCandidate candidate in allCandidates)
            {
                bool selected = selectedCandidate != null && selectedCandidate.CharacterId == candidate.CharacterId;
                bool waitingForUs = IsPartnerPairingBack(candidate);
                bool asking = selected && !waitingForUs;
                bool paired = selected && waitingForUs;

                if (paired)
                {
                    candidate.DisplayName = candidate.BaseDisplayName + " (Paired)";
                    candidate.RowBackground = new SolidColorBrush(Color.FromRgb(38, 82, 55));
                    candidate.Tooltip = "This partner is selected and is already targeting your client, so the pair is ready after both clients send IC.";
                }
                else if (asking)
                {
                    candidate.DisplayName = candidate.BaseDisplayName + " (Asking to pair)";
                    candidate.RowBackground = new SolidColorBrush(Color.FromRgb(99, 78, 31));
                    candidate.Tooltip = "You are asking this partner to pair. They still need to select your client and send IC.";
                }
                else if (waitingForUs)
                {
                    candidate.DisplayName = candidate.BaseDisplayName + " (Waiting to pair)";
                    candidate.RowBackground = new SolidColorBrush(Color.FromRgb(99, 78, 31));
                    candidate.Tooltip = "This partner is targeting your client. Select them to complete the pairing request.";
                }
                else
                {
                    candidate.DisplayName = candidate.BaseDisplayName;
                    candidate.RowBackground = new SolidColorBrush(Color.FromRgb(22, 28, 33));
                    candidate.Tooltip = candidate.BaseTooltip;
                }
            }
        }

        private string BuildSelfLine()
        {
            int selfId = GetSelfPairTargetId();
            string selfName = ResolveSelfPairName(profileClient, networkClient);
            Player? player = networkClient.CurrentAreaPlayers.FirstOrDefault(areaPlayer =>
                areaPlayer.CharacterId == selfId
                || string.Equals(areaPlayer.ICCharacterName, selfName, StringComparison.OrdinalIgnoreCase));
            if (player != null)
            {
                return string.IsNullOrWhiteSpace(player.RawGetAreaLine)
                    ? $"[{player.CharacterId}] {player.ICCharacterName}"
                    : player.RawGetAreaLine;
            }

            return $"[{selfId}] {selfName}";
        }

        private static string ResolveSelfPairName(AOClient profileClient, AOClient client)
        {
            if (!string.IsNullOrWhiteSpace(client.iniPuppetName))
            {
                return client.iniPuppetName;
            }

            if (!string.IsNullOrWhiteSpace(profileClient.iniPuppetName))
            {
                return profileClient.iniPuppetName;
            }

            return profileClient.currentINI?.Name ?? "Unknown";
        }

        private int GetSelfPairTargetId()
        {
            if (profileClient.iniPuppetID >= 0)
            {
                return profileClient.iniPuppetID;
            }

            return networkClient.iniPuppetID;
        }

        private bool IsSelfPairEcho(ICMessage message)
        {
            return message.CharId >= 0
                && (message.CharId == profileClient.iniPuppetID
                    || message.CharId == networkClient.iniPuppetID);
        }

        private bool IsPartnerPairingBack(PairCandidate candidate)
        {
            if (networkClient.ConfirmedPairTargetCharIds.Contains(candidate.CharacterId)
                || profileClient.ConfirmedPairTargetCharIds.Contains(candidate.CharacterId))
            {
                return true;
            }

            int selfPairId = GetSelfPairTargetId();
            if (partnerPreviewStates.TryGetValue(candidate.CharacterId, out PartnerPreviewState? state)
                && state.OtherCharId == selfPairId)
            {
                return true;
            }

            return candidate.InternalPeer != null
                && candidate.InternalPeer.PairTargetCharId == selfPairId;
        }

        private void RefreshPairingSteps()
        {
            pairingStepsPanel.Children.Clear();
            bool supportsPairing = networkClient.ServerFeatures.Contains("CCCC_IC_SUPPORT", StringComparer.OrdinalIgnoreCase);
            AddPairingStep("Server supports AO2 pairing", supportsPairing);

            if (selectedCandidate == null)
            {
                AddPairingStep("Choose a pairing partner", false);
                AddPairingStep("Ask partner to pair", false);
                AddPairingStep("Send IC so server receives your pair target", false);
                AddPairingStep("Partner pairs back", false);
                AddPairingStep("Partner sends IC so server receives their pair target", false);
                AddPairingStep("Match positions", false);
                AddPairingStep("Paired", false);
                return;
            }

            string partnerName = selectedCandidate.Name;
            bool askedSaved = profileClient.PairTargetCharId == selectedCandidate.CharacterId;
            bool askedInWindow = selectedCandidate.CanSelect;
            bool partnerPairsBack = IsPartnerPairingBack(selectedCandidate);
            bool partnerAskedFirst = partnerPairsBack && !askedSaved;
            bool positionsMatched = ArePositionsMatched(selectedCandidate);
            bool partnerPositionKnown = !string.IsNullOrWhiteSpace(GetKnownPartnerPosition(selectedCandidate));
            bool pairConfigSent = IsCurrentPairConfigSent();

            if (partnerAskedFirst)
            {
                AddPairingStep($"{partnerName} pairs with you", true);
                AddPairingStep($"Asked {partnerName} to pair", askedInWindow);
            }
            else
            {
                AddPairingStep($"Asked {partnerName} to pair", askedInWindow);
                AddPairingStep($"{partnerName} pairs back", partnerPairsBack);
                AddPairingStep($"{partnerName} sends IC so server receives their pair target", partnerPairsBack);
            }

            if (partnerPositionKnown)
            {
                AddPairingStep($"Matched position with {partnerName}", positionsMatched);
            }

            AddPairingStep("Sent IC so server received your current pair state", pairConfigSent);
            AddPairingStep("Paired", supportsPairing && askedInWindow && pairConfigSent && partnerPairsBack && positionsMatched);
        }

        private void AddPairingStep(string text, bool done)
        {
            TextBlock step = new TextBlock
            {
                Text = (done ? "✓ " : "□ ") + text,
                Foreground = done
                    ? new SolidColorBrush(Color.FromRgb(134, 176, 150))
                    : new SolidColorBrush(Color.FromRgb(218, 226, 232)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                TextDecorations = done ? TextDecorations.Strikethrough : null,
                Opacity = done ? 0.72 : 1.0
            };
            pairingStepsPanel.Children.Add(step);
        }

        private bool ArePositionsMatched(PairCandidate candidate)
        {
            string partnerPosition = GetKnownPartnerPosition(candidate);
            if (string.IsNullOrWhiteSpace(partnerPosition))
            {
                return false;
            }

            return string.Equals(partnerPosition, networkClient.curPos, StringComparison.OrdinalIgnoreCase)
                || string.Equals(partnerPosition, profileClient.curPos, StringComparison.OrdinalIgnoreCase);
        }

        private string GetKnownPartnerPosition(PairCandidate candidate)
        {
            if (partnerPreviewStates.TryGetValue(candidate.CharacterId, out PartnerPreviewState? state)
                && !string.IsNullOrWhiteSpace(state.Position))
            {
                return state.Position;
            }

            if (profileClient.KnownPairTargetPositions.TryGetValue(candidate.CharacterId, out string? profileKnownPosition)
                && !string.IsNullOrWhiteSpace(profileKnownPosition))
            {
                return profileKnownPosition;
            }

            if (networkClient.KnownPairTargetPositions.TryGetValue(candidate.CharacterId, out string? networkKnownPosition)
                && !string.IsNullOrWhiteSpace(networkKnownPosition))
            {
                return networkKnownPosition;
            }

            if (candidate.InternalPeer != null && !string.IsNullOrWhiteSpace(candidate.InternalPeer.curPos))
            {
                return candidate.InternalPeer.curPos;
            }

            return candidate.PositionKnown ? candidate.Position : string.Empty;
        }

        private bool IsCurrentPairConfigSent()
        {
            int targetId = selectedCandidate?.CharacterId ?? -1;
            string currentPosition = ResolveCurrentSendPosition();
            return networkClient.LastSentPairTargetCharId == targetId
                && networkClient.LastSentPairLayerOrder == CurrentLayerOrder
                && string.Equals(networkClient.LastSentPairPosition, currentPosition, StringComparison.OrdinalIgnoreCase)
                && networkClient.LastSentPairSelfOffset == myOffset;
        }

        private void RefreshSendBlankpostButton()
        {
            if (selectedCandidate == null || selectedCandidate.CharacterId < 0)
            {
                sendBlankpostButton.IsEnabled = false;
                sendBlankpostButton.ToolTip = "Choose a pairing partner before sending a blank IC update.";
                return;
            }

            bool knownPositionIsUnmatched = !string.IsNullOrWhiteSpace(GetKnownPartnerPosition(selectedCandidate))
                && !ArePositionsMatched(selectedCandidate);
            if (knownPositionIsUnmatched)
            {
                sendBlankpostButton.IsEnabled = false;
                sendBlankpostButton.ToolTip = "Match the partner position before sending the IC update.";
                return;
            }

            bool needsUpdate = !IsCurrentPairConfigSent();
            sendBlankpostButton.IsEnabled = needsUpdate;
            sendBlankpostButton.ToolTip = needsUpdate
                ? "Send a blank IC message so the server receives your current pair target, layer order, position, and offset."
                : "Your current pair target, layer order, position, and offset were already sent in your last IC update.";
        }

        private async Task SendBlankpostAsync()
        {
            if (selectedCandidate == null || !sendBlankpostButton.IsEnabled)
            {
                return;
            }

            ApplyCurrentPairConfigToClients();
            sendBlankpostButton.IsEnabled = false;
            await networkClient.SendICMessage(" ");
            profileClient.LastSentPairTargetCharId = networkClient.LastSentPairTargetCharId;
            profileClient.LastSentPairLayerOrder = networkClient.LastSentPairLayerOrder;
            profileClient.LastSentPairPosition = networkClient.LastSentPairPosition;
            profileClient.LastSentPairSelfOffset = networkClient.LastSentPairSelfOffset;
            RefreshStatus();
            RefreshPreview();
        }

        private void ApplyCurrentPairConfigToClients()
        {
            int targetId = selectedCandidate?.CharacterId ?? -1;
            string targetName = selectedCandidate?.Name ?? string.Empty;
            profileClient.PairTargetCharId = targetId;
            profileClient.PairTargetCharacterName = targetName;
            profileClient.PairLayerOrder = CurrentLayerOrder;
            profileClient.SelfOffset = myOffset;
            networkClient.PairTargetCharId = targetId;
            networkClient.PairTargetCharacterName = targetName;
            networkClient.PairLayerOrder = CurrentLayerOrder;
            networkClient.SelfOffset = myOffset;
            if (selectedCandidate != null)
            {
                RememberKnownPartnerPosition(selectedCandidate.CharacterId, GetKnownPartnerPosition(selectedCandidate));
            }
        }

        private void RefreshMatchPartnerPositionButton()
        {
            if (selectedCandidate == null)
            {
                matchPartnerPositionButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (!partnerPreviewStates.ContainsKey(selectedCandidate.CharacterId))
            {
                matchPartnerPositionButton.Visibility = Visibility.Collapsed;
                return;
            }

            string partnerPosition = GetKnownPartnerPosition(selectedCandidate);
            if (string.IsNullOrWhiteSpace(partnerPosition))
            {
                matchPartnerPositionButton.Visibility = Visibility.Collapsed;
                return;
            }

            matchPartnerPositionButton.Content = $"Match {selectedCandidate.Name}'s Position";
            matchPartnerPositionButton.ToolTip = $"Set your preview/send position to {partnerPosition}.";
            matchPartnerPositionButton.Visibility = Visibility.Visible;
            matchPartnerPositionButton.IsEnabled = !ArePositionsMatched(selectedCandidate);
        }

        private void MatchPartnerPosition()
        {
            if (selectedCandidate == null)
            {
                return;
            }

            string partnerPosition = GetKnownPartnerPosition(selectedCandidate);
            if (string.IsNullOrWhiteSpace(partnerPosition))
            {
                return;
            }

            profileClient.curPos = partnerPosition;
            networkClient.curPos = partnerPosition;
            previewSceneClient.curPos = partnerPosition;
            RememberKnownPartnerPosition(selectedCandidate.CharacterId, partnerPosition);
            RefreshStatus();
            RefreshPreview();
        }

        private void RememberKnownPartnerPosition(int partnerCharacterId, string? position)
        {
            if (partnerCharacterId < 0 || string.IsNullOrWhiteSpace(position))
            {
                return;
            }

            string normalizedPosition = position.Trim();
            profileClient.KnownPairTargetPositions[partnerCharacterId] = normalizedPosition;
            networkClient.KnownPairTargetPositions[partnerCharacterId] = normalizedPosition;
        }

        private string ResolveCurrentSendPosition()
        {
            return !string.IsNullOrWhiteSpace(networkClient.curPos)
                ? networkClient.curPos
                : profileClient.curPos ?? string.Empty;
        }

        private void CharacterPairingStudioWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyDown(e);
        }

        private void CharacterPairingStudioWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyUp(e);
        }

        private void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyDown(e);
        }

        private void HostWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyUp(e);
        }

        private void HandleOffsetPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                case Key.Down:
                case Key.Up:
                    StartKeyboardNudge();
                    e.Handled = true;
                    break;
            }
        }

        private void HandleOffsetPreviewKeyUp(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                case Key.Down:
                case Key.Up:
                    StopKeyboardNudgeIfNoArrowKeysHeld();
                    e.Handled = true;
                    break;
            }
        }

        private void StartKeyboardNudge()
        {
            if (!keyboardNudgeActive)
            {
                keyboardNudgeActive = true;
                ApplyHeldArrowKeyNudge();
            }

            if (!keyboardNudgeTimer.IsEnabled)
            {
                keyboardNudgeTimer.Start();
            }
        }

        private void StopKeyboardNudgeIfNoArrowKeysHeld()
        {
            if (Keyboard.IsKeyDown(Key.Left)
                || Keyboard.IsKeyDown(Key.Right)
                || Keyboard.IsKeyDown(Key.Up)
                || Keyboard.IsKeyDown(Key.Down))
            {
                return;
            }

            keyboardNudgeActive = false;
            keyboardNudgeTimer.Stop();
        }

        private void ApplyHeldArrowKeyNudge()
        {
            int horizontalDelta = 0;
            int verticalDelta = 0;
            if (Keyboard.IsKeyDown(Key.Left)) horizontalDelta--;
            if (Keyboard.IsKeyDown(Key.Right)) horizontalDelta++;
            if (Keyboard.IsKeyDown(Key.Up)) verticalDelta--;
            if (Keyboard.IsKeyDown(Key.Down)) verticalDelta++;
            if (horizontalDelta == 0 && verticalDelta == 0)
            {
                StopKeyboardNudgeIfNoArrowKeysHeld();
                return;
            }

            AdjustSelfOffset(horizontalDelta, verticalDelta);
        }

        private void AttachHostKeyboardHandler()
        {
            Window? host = HostWindow;
            if (host == null || ReferenceEquals(host, keyboardHostWindow))
            {
                return;
            }

            DetachHostKeyboardHandler();
            keyboardHostWindow = host;
            host.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HostWindow_PreviewKeyDown), true);
            host.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(HostWindow_PreviewKeyUp), true);
        }

        private void DetachHostKeyboardHandler()
        {
            if (keyboardHostWindow == null)
            {
                return;
            }

            keyboardHostWindow.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HostWindow_PreviewKeyDown));
            keyboardHostWindow.RemoveHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(HostWindow_PreviewKeyUp));
            keyboardHostWindow = null;
            keyboardNudgeActive = false;
            keyboardNudgeTimer.Stop();
        }

        private static List<PairCandidate> BuildCandidates(AOClient profileClient, AOClient client, IReadOnlyList<AOClient> peerClients)
        {
            IReadOnlyDictionary<string, bool> serverCharacters = client.ServerCharacterAvailability;
            List<PairCandidate> candidates = new List<PairCandidate>();
            Dictionary<int, AOClient> internalByCharacterSlot = peerClients
                .Where(peer => peer != null && !ReferenceEquals(peer, client) && peer.iniPuppetID >= 0)
                .GroupBy(peer => peer.iniPuppetID)
                .ToDictionary(group => group.Key, group => group.First());
            Dictionary<int, AOClient> internalByPlayerId = peerClients
                .Where(peer => peer != null && !ReferenceEquals(peer, client) && peer.playerID >= 0)
                .GroupBy(peer => peer.playerID)
                .ToDictionary(group => group.Key, group => group.First());

            if (client.LastGetAreaParseSucceeded && client.CurrentAreaPlayers.Count > 0)
            {
                CustomConsole.Info(
                    $"[PAIR] Building candidates from current-area players. count={client.CurrentAreaPlayers.Count}",
                    CustomConsole.LogCategory.PairingStudio);
                foreach (Player player in client.CurrentAreaPlayers)
                {
                    AOClient? internalPeer = ResolveInternalPeerForAreaPlayer(player, internalByPlayerId, internalByCharacterSlot);
                    int pairTargetCharacterId = ResolvePairTargetCharacterId(client, player.ICCharacterName, internalPeer, player.CharacterId);
                    candidates.Add(BuildCandidate(
                        profileClient,
                        client,
                        pairTargetCharacterId,
                        player.CharacterId,
                        player.ICCharacterName,
                        displayName: string.IsNullOrWhiteSpace(player.OOCShowname)
                            ? player.ICCharacterName
                            : player.OOCShowname.Trim(),
                        rawLine: string.IsNullOrWhiteSpace(player.RawGetAreaLine)
                            ? $"[{player.CharacterId}] {player.ICCharacterName}"
                            : player.RawGetAreaLine,
                        available: true,
                        fromCurrentArea: true,
                        internalPeer));
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
                        profileClient,
                        client,
                        index,
                        -1,
                        entry.Key,
                        displayName: entry.Key,
                        rawLine: string.Empty,
                        available: true,
                        fromCurrentArea: false,
                        internalByCharacterSlot.TryGetValue(index, out AOClient? internalPeer) ? internalPeer : null));
                }
            }

            return candidates
                .Where(candidate => !candidate.IsSelf)
                .GroupBy(candidate => candidate.CharacterId)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.SortRank)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AOClient? ResolveInternalPeerForAreaPlayer(
            Player player,
            IReadOnlyDictionary<int, AOClient> internalByPlayerId,
            IReadOnlyDictionary<int, AOClient> internalByCharacterSlot)
        {
            if (internalByPlayerId.TryGetValue(player.CharacterId, out AOClient? byPlayerId))
            {
                return byPlayerId;
            }

            return internalByCharacterSlot.Values.FirstOrDefault(peer =>
                string.Equals(peer.currentINI?.Name, player.ICCharacterName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(peer.iniPuppetName, player.ICCharacterName, StringComparison.OrdinalIgnoreCase));
        }

        private static int ResolvePairTargetCharacterId(AOClient client, string characterName, AOClient? internalPeer, int fallbackAreaPlayerId)
        {
            int index = 0;
            foreach (string serverCharacterName in client.ServerCharacterAvailability.Keys)
            {
                if (string.Equals(serverCharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }

                index++;
            }

            if (internalPeer != null && internalPeer.iniPuppetID >= 0)
            {
                return internalPeer.iniPuppetID;
            }

            return fallbackAreaPlayerId;
        }

        private static PairCandidate BuildCandidate(
            AOClient profileClient,
            AOClient client,
            int characterId,
            int areaPlayerId,
            string name,
            string displayName,
            string rawLine,
            bool available,
            bool fromCurrentArea,
            AOClient? internalPeer)
        {
            CharacterFolder? local = ResolveLocalCharacter(name);
            string selfName = ResolveSelfPairName(profileClient, client);
            bool isSelf = fromCurrentArea
                ? characterId == client.iniPuppetID
                    || characterId == profileClient.iniPuppetID
                    || string.Equals(name, selfName, StringComparison.OrdinalIgnoreCase)
                : characterId == profileClient.iniPuppetID;
            if (isSelf)
            {
                return PairCandidate.Self(characterId, name);
            }

            bool samePosition = internalPeer == null
                || string.Equals(internalPeer.curPos, client.curPos, StringComparison.OrdinalIgnoreCase);
            bool canSelect = available && characterId >= 0 && samePosition;
            string status = fromCurrentArea
                ? string.IsNullOrWhiteSpace(rawLine)
                    ? $"[{characterId}] {name}"
                    : rawLine
                : string.Empty;
            if (fromCurrentArea && internalPeer != null)
            {
                status += " · internal client " + internalPeer.clientName;
            }

            if (fromCurrentArea && !available)
            {
                status += " · unavailable";
            }

            if (fromCurrentArea && !samePosition)
            {
                status += " · different position";
            }

            if (fromCurrentArea && local == null)
            {
                status += " · no local art";
            }

            string tooltip = !available
                    ? "Unavailable: the server reported this character slot as taken in CharsCheck. Pairing fallback ignores CharsCheck, so this should only appear for parsed current-area entries."
                    : characterId < 0
                        ? "Unavailable: this entry did not resolve to a valid server character slot."
                        : !samePosition
                            ? "Unavailable: this internal client is in a different AO2 position. Pairing appears only when both clients use the same position."
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
                internalPeer != null ? 0 : fromCurrentArea ? 1 : 2,
                internalPeer);
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
                MinHeight = 190,
                VerticalAlignment = VerticalAlignment.Stretch
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

        private static void ApplyDisabledPreviewFieldStyle(TextBox box)
        {
            box.IsReadOnly = true;
            box.IsTabStop = false;
            box.Background = new SolidColorBrush(Color.FromRgb(21, 26, 30));
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 68, 77));
            box.Foreground = new SolidColorBrush(Color.FromRgb(126, 145, 156));
            box.CaretBrush = Brushes.Transparent;
            box.Focusable = false;
            box.Template = CreateReadOnlyTextBoxTemplate();
            ToolTipService.SetShowOnDisabled(box, true);
        }

        private static ControlTemplate CreateReadOnlyTextBoxTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            FrameworkElementFactory host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetValue(ScrollViewer.MarginProperty, new Thickness(0));
            border.AppendChild(host);

            return new ControlTemplate(typeof(TextBox))
            {
                VisualTree = border
            };
        }

        private ToggleButton CreateSegmentButton(string text, string tooltip, CornerRadius cornerRadius)
        {
            ToggleButton button = new ToggleButton
            {
                Content = text,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 94, 104)),
                BorderThickness = new Thickness(0),
                ToolTip = tooltip,
                Template = CreateSegmentButtonTemplate(cornerRadius)
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

                viewport.PreviewPairLayerOrder(CurrentLayerOrder);
                RefreshStatus();
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

        private void AddOffsetField(Grid grid, string label, TextBox textBox, int decrement, int increment, int startColumn, bool readOnly)
        {
            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 224, 232)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(startColumn == 0 ? 0 : 14, 0, 6, 0)
            };
            Grid.SetRow(labelBlock, 1);
            Grid.SetColumn(labelBlock, startColumn);
            grid.Children.Add(labelBlock);

            DockPanel editor = new DockPanel
            {
                LastChildFill = true
            };

            if (!readOnly)
            {
                RepeatButton minus = CreateStepperButton("-", () => AdjustSelfOffset(label == "X" ? decrement : 0, label == "Y" ? decrement : 0));
                RepeatButton plus = CreateStepperButton("+", () => AdjustSelfOffset(label == "X" ? increment : 0, label == "Y" ? increment : 0));
                DockPanel.SetDock(minus, Dock.Left);
                DockPanel.SetDock(plus, Dock.Right);
                editor.Children.Add(minus);
                editor.Children.Add(plus);
            }

            editor.Children.Add(textBox);
            Grid.SetRow(editor, 1);
            Grid.SetColumn(editor, startColumn + 1);
            grid.Children.Add(editor);
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

        private static FrameworkElement CreateHelpedLabel(string text, string tooltip)
        {
            DockPanel row = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(0, 8, 0, 0)
            };
            FrameworkElement help = CreateHelpBadge(tooltip);
            DockPanel.SetDock(help, Dock.Right);
            row.Children.Add(help);
            row.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(154, 171, 181)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private static FrameworkElement CreateSectionLabel(string text, string tooltip)
        {
            FrameworkElement row = CreateHelpedLabel(text, tooltip);
            row.Margin = new Thickness(0, 16, 0, 0);
            return row;
        }

        private static FrameworkElement CreateHelpBadge(string tooltip)
        {
            return new TextBlock
            {
                Text = "(?)",
                ToolTip = tooltip,
                Foreground = new SolidColorBrush(Color.FromRgb(182, 205, 218)),
                Width = 26,
                Height = 18,
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
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
            Button button = new Button
            {
                Content = text,
                MinWidth = 86,
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(35, 43, 49)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(83, 101, 112)),
                Foreground = Brushes.White,
                Template = CreateDarkButtonTemplate(
                    Color.FromRgb(45, 55, 63),
                    Color.FromRgb(19, 24, 29),
                    Color.FromRgb(83, 101, 112),
                    Color.FromRgb(101, 121, 134),
                    new CornerRadius(3))
            };
            ToolTipService.SetShowOnDisabled(button, true);
            return button;
        }

        private RepeatButton CreateArrowButton(string content, string tooltip, Action clickAction)
        {
            RepeatButton button = new RepeatButton
            {
                Content = content,
                Width = 42,
                Height = 42,
                Delay = 280,
                Interval = 45,
                Opacity = 0.24,
                Background = new SolidColorBrush(Color.FromArgb(210, 15, 20, 25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(122, 153, 174)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                ToolTip = tooltip,
                Focusable = false,
                Template = CreateDarkButtonTemplate(
                    Color.FromArgb(230, 24, 32, 39),
                    Color.FromArgb(245, 11, 15, 18),
                    Color.FromRgb(122, 153, 174),
                    Color.FromRgb(149, 176, 194),
                    new CornerRadius(3))
            };
            button.Click += (_, _) => clickAction();
            button.MouseEnter += (_, _) => button.Opacity = 0.88;
            button.MouseLeave += (_, _) => button.Opacity = 0.24;
            button.PreviewMouseDown += (_, _) => button.Opacity = 1;
            button.PreviewMouseUp += (_, _) => button.Opacity = 0.88;
            ToolTipService.SetShowOnDisabled(button, true);
            return button;
        }

        private RepeatButton CreateStepperButton(string content, Action clickAction)
        {
            RepeatButton button = new RepeatButton
            {
                Content = content,
                Width = 24,
                Height = 26,
                Delay = 300,
                Interval = 65,
                Background = new SolidColorBrush(Color.FromRgb(30, 37, 43)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(78, 96, 108)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Focusable = false,
                Template = CreateDarkButtonTemplate(
                    Color.FromRgb(40, 49, 57),
                    Color.FromRgb(17, 22, 27),
                    Color.FromRgb(78, 96, 108),
                    Color.FromRgb(95, 118, 132),
                    new CornerRadius(3))
            };
            button.Click += (_, _) => clickAction();
            ToolTipService.SetShowOnDisabled(button, true);
            return button;
        }

        private static ControlTemplate CreateDarkButtonTemplate(
            Color hoverBackground,
            Color pressedBackground,
            Color normalBorder,
            Color hoverBorder,
            CornerRadius cornerRadius)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, cornerRadius);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(ButtonBase))
            {
                VisualTree = border
            };

            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverBackground), "Chrome"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(hoverBorder), "Chrome"));
            template.Triggers.Add(hoverTrigger);

            Trigger pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(pressedBackground), "Chrome"));
            pressedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(normalBorder), "Chrome"));
            template.Triggers.Add(pressedTrigger);

            Trigger disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        private static ControlTemplate CreateSegmentButtonTemplate(CornerRadius cornerRadius)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, cornerRadius);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new Thickness(8, 0, 8, 0));
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(ToggleButton))
            {
                VisualTree = border
            };

            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(37, 46, 53)), "Chrome"));
            template.Triggers.Add(hoverTrigger);

            Trigger checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(55, 96, 80)), "Chrome"));
            template.Triggers.Add(checkedTrigger);

            Trigger disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        private static DataTemplate BuildPairCandidateTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Border));
            root.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(PairCandidate.RowBackground)));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            root.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            root.SetValue(Border.MarginProperty, new Thickness(4, 3, 4, 3));

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));

            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PairCandidate.DisplayName)));
            name.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            stack.AppendChild(name);

            FrameworkElementFactory status = new FrameworkElementFactory(typeof(TextBlock));
            status.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PairCandidate.Status)));
            status.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(154, 171, 181)));
            status.SetValue(TextBlock.FontSizeProperty, 11.0);
            status.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            Style statusStyle = new Style(typeof(TextBlock));
            DataTrigger emptyStatusTrigger = new DataTrigger
            {
                Binding = new System.Windows.Data.Binding(nameof(PairCandidate.Status)),
                Value = string.Empty
            };
            emptyStatusTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            statusStyle.Triggers.Add(emptyStatusTrigger);
            status.SetValue(FrameworkElement.StyleProperty, statusStyle);
            stack.AppendChild(status);
            root.AppendChild(stack);

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

        private sealed class PairCandidate
        {
            public PairCandidate(
                int characterId,
                string name,
                string displayName,
                string status,
                bool hasLocalArt,
                bool canSelect,
                string tooltip,
                string position,
                bool positionKnown,
                int sortRank,
                AOClient? internalPeer)
            {
                CharacterId = characterId;
                Name = name;
                BaseDisplayName = displayName;
                BaseStatus = status;
                DisplayName = displayName;
                Status = status;
                HasLocalArt = hasLocalArt;
                CanSelect = canSelect;
                Tooltip = tooltip;
                BaseTooltip = tooltip;
                Position = position;
                PositionKnown = positionKnown;
                SortRank = sortRank;
                InternalPeer = internalPeer;
            }

            public int CharacterId { get; }
            public string Name { get; }
            public string BaseDisplayName { get; }
            public string BaseStatus { get; }
            public string DisplayName { get; set; }
            public string Status { get; set; }
            public bool HasLocalArt { get; }
            public bool CanSelect { get; }
            public string BaseTooltip { get; }
            public string Tooltip { get; set; }
            public string Position { get; }
            public bool PositionKnown { get; }
            public int SortRank { get; }
            public AOClient? InternalPeer { get; }
            public bool IsSelf { get; private init; }
            public Brush RowBackground { get; set; } = new SolidColorBrush(Color.FromRgb(22, 28, 33));

            public static PairCandidate Self(int characterId, string name)
            {
                return new PairCandidate(
                    characterId,
                    name,
                    name,
                    string.Empty,
                    hasLocalArt: true,
                    canSelect: false,
                    tooltip: string.Empty,
                    position: string.Empty,
                    positionKnown: false,
                    sortRank: int.MaxValue,
                    internalPeer: null)
                {
                    IsSelf = true
                };
            }

            public override string ToString()
            {
                return Name;
            }
        }

        private sealed record PartnerPreviewState(
            (int Horizontal, int Vertical) Offset,
            bool Flip,
            string Emote,
            string Position,
            int OtherCharId);
    }
}
