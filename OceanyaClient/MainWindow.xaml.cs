using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using AO2AIBot.Chat;
using AO2AIBot.Clients;
using AO2AIBot.Controller;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NAudio.Wave;
using OceanyaClient.AdvancedFeatures;
using OceanyaClient.Components;
using OceanyaClient.Features.Startup;
using OceanyaClient.Features.Viewport;
using OceanyaClient.Utilities;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace OceanyaClient
{
    public partial class MainWindow : OceanyaWindowContentControl, IStartupFunctionalityWindow
    {
        private static Func<AOClient, Task>? testConnectClientAsyncOverride;
        private static Action<Window?, string, string, MessageBoxButton, MessageBoxImage>? testMessageBoxOverride;
        public event Action? FinishedLoading;

        private readonly Dictionary<ToggleButton, AOClient> clients = new Dictionary<ToggleButton, AOClient>();
        private readonly Dictionary<AOClient, AOClientAgentController> aiControllers = new Dictionary<AOClient, AOClientAgentController>();
        private readonly Dictionary<AOClient, List<PendingAiOriginResponse>> pendingAiOriginResponses = new Dictionary<AOClient, List<PendingAiOriginResponse>>();
        private readonly Dictionary<AOClient, bool> aiOriginResponseVisibility = new Dictionary<AOClient, bool>();
        private readonly IAiChatCompletionService aiCompletionService = new AiChatCompletionService();
        private readonly CallwordAudioNotifier callwordAudioNotifier = new CallwordAudioNotifier();
        private AOClient? currentClient;
        private AOClient? singleInternalClient;
        private AOClient? boundSingleClientProfile;
        private Window? viewportWindow;
        private HwndSource? viewportWindowSource;
        private Window? settingsWindow;
        private AO2ViewportWindowContent? viewportContent;
        private bool isMainWindowClosing;
        private bool hasHookedHostWindowClosing;
        private readonly bool useSingleInternalClient = SaveFile.Data.UseSingleInternalClient;
        private readonly bool aiModeEnabled;
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
        private bool hasRaisedFinishedLoading;
        private static readonly TimeSpan PendingAiOriginRetention = TimeSpan.FromMinutes(2);
        private const double ViewportContentAspectRatio =
            (double)AO2ViewportAssetResolver.ViewportWidth / AO2ViewportAssetResolver.ViewportToolHeight;
        private const int WmGetMinMaxInfo = 0x0024;
        private const int WmSizing = 0x0214;
        private const int WmSize = 0x0005;
        private const int WmszLeft = 1;
        private const int WmszRight = 2;
        private const int WmszTop = 3;
        private const int WmszTopLeft = 4;
        private const int WmszTopRight = 5;
        private const int WmszBottom = 6;
        private const int WmszBottomLeft = 7;
        private const int WmszBottomRight = 8;

        private sealed class DreddOverlaySelectionItem
        {
            public string Name { get; set; } = string.Empty;
            public string DisplayText { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public bool IsNone { get; set; }
            public bool IsTransient { get; set; }
        }

        private sealed class PendingAiOriginResponse
        {
            public string Channel { get; set; } = string.Empty;

            public string ShowName { get; set; } = string.Empty;

            public string Message { get; set; } = string.Empty;

            public string RawResponse { get; set; } = string.Empty;

            public DateTime CreatedUtc { get; set; }
        }

        List<ToggleButton> objectionModifiers;
        public MainWindow(bool aiModeEnabled = false)
        {
            this.aiModeEnabled = aiModeEnabled;
            InitializeComponent();
            Title = aiModeEnabled ? "Oceanya Online - AO2 AI Bot" : "Oceanya Online";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Loaded += MainWindow_Loaded;

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

                if (OceanyaTestMode.Current.IsEnabled && !networkClient.IsTransportConnected)
                {
                    ICMessageSettingsControl.Dispatcher.Invoke(() =>
                    {
                        ICMessageSettingsControl.txtICMessage.Text = string.Empty;

                        if (!ICMessageSettingsControl.stickyEffects)
                        {
                            ICMessageSettingsControl.ResetMessageEffects();
                        }
                    });
                    return;
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
            ICMessageSettingsControl.OnRefreshCharacterRequested += async characterName =>
            {
                await RefreshCharacterAssetsAsync(characterName, refreshAllCharacters: false, refreshAllAssets: false);
            };
            ICMessageSettingsControl.OnRefreshAllAssetsRequested += async () =>
            {
                await RefreshCharacterAssetsAsync(null, refreshAllCharacters: false, refreshAllAssets: true);
            };
            ICMessageSettingsControl.OnRefreshAllCharactersRequested += async () =>
            {
                await RefreshCharacterAssetsAsync(null, refreshAllCharacters: true, refreshAllAssets: false);
            };
            ICMessageSettingsControl.OnOpenInCharacterEditorRequested += async characterDirectory =>
            {
                await OpenCharacterInEditorAsync(characterDirectory);
            };

            OOCLogControl.txtOOCShowname.Text = SaveFile.Data.OOCName;
            chkPosOnIniSwap.IsChecked = SaveFile.Data.SwitchPosOnIniSwap;
            chkSticky.IsChecked = SaveFile.Data.StickyEffect;
            chkInvertLog.IsChecked = SaveFile.Data.InvertICLog;
            ApplySavedClientSettingsToRuntime();
            InitializeDreddFeatureUi();

            btnDebug.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
            RefreshAreaNavigatorForCurrentClient();
        }

        /// <inheritdoc/>
        public override string HeaderText => "OCEANYA ONLINE";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HookHostWindowClosing();
            if (hasRaisedFinishedLoading)
            {
                return;
            }

            hasRaisedFinishedLoading = true;
            MarkAutomationReady();
            if (SaveFile.Data.GMViewportWindowState?.IsVisible == true)
            {
                Dispatcher.BeginInvoke(new Action(OpenViewportWindow));
            }
            FinishedLoading?.Invoke();
        }

        private void HookHostWindowClosing()
        {
            if (hasHookedHostWindowClosing)
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (hostWindow == null)
            {
                return;
            }

            hasHookedHostWindowClosing = true;
            hostWindow.Closing += (_, _) =>
            {
                isMainWindowClosing = true;
                viewportContent?.AttachClient(null, null);
                viewportWindow?.Close();
                callwordAudioNotifier.Dispose();
            };
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

            string aiSuffix = IsAiEnabled(bot) ? " | AI: ON" : string.Empty;
            button.ToolTip = $"[{bot.playerID}] {characterName} (\"{bot.clientName}\"){aiSuffix}";
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

        private AOClientAgentController EnsureAiController(AOClient profileClient)
        {
            if (aiControllers.TryGetValue(profileClient, out AOClientAgentController? existingController))
            {
                return existingController;
            }

            AOClientAgentController controller = new AOClientAgentController(
                aiCompletionService,
                BuildAiSettings,
                () => BuildAiSnapshot(profileClient),
                (agentResponse, snapshot, cancellationToken) => ExecuteAgentResponseAsync(profileClient, agentResponse, snapshot, cancellationToken));
            controller.StatusChanged += update =>
            {
                Dispatcher.Invoke(() =>
                {
                    HandleAiControllerStatusChanged(profileClient, update);
                });
            };

            aiControllers[profileClient] = controller;
            return controller;
        }

        private AiChatProviderSettings BuildAiSettings()
        {
            string provider = SaveFile.Data.AO2AiBot.Provider?.Trim() ?? string.Empty;
            return new AiChatProviderSettings
            {
                Provider = string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
                    ? AiProviderKind.OpenAI
                    : AiProviderKind.Ollama,
                OllamaEndpoint = SaveFile.Data.AO2AiBot.OllamaEndpoint,
                OllamaModel = SaveFile.Data.AO2AiBot.OllamaModel,
                OpenAIModel = SaveFile.Data.AO2AiBot.OpenAIModel,
                OpenAIApiKeyEnvironmentVariable = SaveFile.Data.AO2AiBot.OpenAIApiKeyEnvironmentVariable,
                Temperature = SaveFile.Data.AO2AiBot.Temperature,
                MaxTokens = SaveFile.Data.AO2AiBot.MaxTokens,
                MaxPromptMessages = SaveFile.Data.AO2AiBot.MaxPromptMessages,
                PersonalityPrompt = SaveFile.Data.AO2AiBot.PersonalityPrompt,
                OllamaContextSize = SaveFile.Data.AO2AiBot.OllamaContextSize,
                UseOllamaJsonSchema = SaveFile.Data.AO2AiBot.UseOllamaJsonSchema
            };
        }

        private AOClientControlSnapshot BuildAiSnapshot(AOClient profileClient)
        {
            AOClient? networkClient = GetTargetClientForNetwork(profileClient);
            return AOClientControlSnapshotBuilder.Build(profileClient, networkClient, SaveFile.Data.SelectedServerName);
        }

        private async Task<string> ExecuteAgentResponseAsync(
            AOClient profileClient,
            AgentResponse agentResponse,
            AOClientControlSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (profileClient == null)
            {
                return "AI action skipped because no profile client was provided.";
            }

            AOClient? networkClient = GetTargetClientForNetwork(profileClient);
            if (networkClient == null)
            {
                return "AI action skipped because no network client is available.";
            }

            List<string> appliedChanges = new List<string>();

            // Save transient state for restore after speak actions.
            string previousSfx = profileClient.curSFX;
            ICMessage.ShoutModifiers previousShoutModifier = profileClient.shoutModifiers;
            ICMessage.Effects previousEffect = profileClient.effect;
            bool previousScreenshake = profileClient.screenshake;
            bool previousPreanimEnabled = profileClient.PreanimEnabled;
            bool previousImmediate = profileClient.Immediate;
            bool previousAdditive = profileClient.Additive;

            // Execute actions in order.
            foreach (AgentAction action in agentResponse.Actions)
            {
                await ExecuteSingleActionAsync(
                    profileClient, networkClient, snapshot, action, appliedChanges,
                    previousSfx, previousShoutModifier, previousEffect, previousScreenshake,
                    previousPreanimEnabled, previousImmediate, previousAdditive,
                    cancellationToken);
            }

            RestoreAiTransientState(
                profileClient,
                previousSfx,
                previousShoutModifier,
                previousEffect,
                previousScreenshake,
                previousPreanimEnabled,
                previousImmediate,
                previousAdditive);
            RefreshUiAfterAiStateChanged(profileClient);
            return appliedChanges.Count == 0
                ? "AI action completed with no visible changes."
                : "AI action completed: " + string.Join(", ", appliedChanges) + ".";
        }

        private async Task ExecuteSingleActionAsync(
            AOClient profileClient,
            AOClient networkClient,
            AOClientControlSnapshot snapshot,
            AgentAction action,
            List<string> appliedChanges,
            string previousSfx,
            ICMessage.ShoutModifiers previousShoutModifier,
            ICMessage.Effects previousEffect,
            bool previousScreenshake,
            bool previousPreanimEnabled,
            bool previousImmediate,
            bool previousAdditive,
            CancellationToken cancellationToken)
        {
            switch (action.Type)
            {
                case AgentActionType.SetIcShowname:
                    profileClient.SetICShowname(action.Value.Trim());
                    appliedChanges.Add("updated IC showname to " + action.Value);
                    break;

                case AgentActionType.SetOocShowname:
                    profileClient.OOCShowname = action.Value.Trim();
                    appliedChanges.Add("updated OOC showname to " + action.Value);
                    break;

                case AgentActionType.SetCharacter:
                    if (!string.Equals(profileClient.currentINI?.Name, action.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        profileClient.SetCharacter(action.Value);
                        appliedChanges.Add("switched character to " + action.Value);
                    }

                    break;

                case AgentActionType.SetEmote:
                    ApplyEmote(profileClient, action.Value, appliedChanges);
                    break;

                case AgentActionType.SetPosition:
                    profileClient.SetPos(action.Value);
                    appliedChanges.Add("set position to " + action.Value);
                    break;

                case AgentActionType.SetTextColor:
                    if (Enum.TryParse(action.Value, ignoreCase: true, out ICMessage.TextColors textColor))
                    {
                        profileClient.textColor = textColor;
                        appliedChanges.Add("set text color to " + action.Value);
                    }

                    break;

                case AgentActionType.SetFlip:
                    if (action.BoolValue.HasValue)
                    {
                        profileClient.flip = action.BoolValue.Value;
                        appliedChanges.Add("set flip to " + action.BoolValue.Value);
                    }

                    break;

                case AgentActionType.SetAdditive:
                    if (action.BoolValue.HasValue)
                    {
                        profileClient.Additive = action.BoolValue.Value;
                        appliedChanges.Add("set additive to " + action.BoolValue.Value);
                    }

                    break;

                case AgentActionType.SetImmediate:
                    if (action.BoolValue.HasValue)
                    {
                        profileClient.Immediate = action.BoolValue.Value;
                        appliedChanges.Add("set immediate to " + action.BoolValue.Value);
                    }

                    break;

                case AgentActionType.SetPreanimEnabled:
                    if (action.BoolValue.HasValue)
                    {
                        profileClient.PreanimEnabled = action.BoolValue.Value;
                        appliedChanges.Add("set preanimation to " + action.BoolValue.Value);
                    }

                    break;

                case AgentActionType.SetScreenshake:
                    if (action.BoolValue.HasValue)
                    {
                        profileClient.screenshake = action.BoolValue.Value;
                        appliedChanges.Add("set screenshake to " + action.BoolValue.Value);
                    }

                    break;

                case AgentActionType.SetSfx:
                    profileClient.curSFX = action.Value.Trim();
                    appliedChanges.Add("set SFX to " + action.Value);
                    break;

                case AgentActionType.SetDeskMod:
                    if (Enum.TryParse(action.Value, ignoreCase: true, out ICMessage.DeskMods deskMod))
                    {
                        profileClient.deskMod = deskMod;
                        appliedChanges.Add("set desk mod to " + action.Value);
                    }

                    break;

                case AgentActionType.SetShoutModifier:
                    if (Enum.TryParse(action.Value, ignoreCase: true, out ICMessage.ShoutModifiers shoutMod))
                    {
                        profileClient.shoutModifiers = shoutMod;
                        appliedChanges.Add("set shout modifier to " + action.Value);
                    }

                    break;

                case AgentActionType.SetEffect:
                    if (Enum.TryParse(action.Value, ignoreCase: true, out ICMessage.Effects effect))
                    {
                        profileClient.effect = effect;
                        appliedChanges.Add("set effect to " + action.Value);
                    }

                    break;

                case AgentActionType.SetEmoteModifier:
                    if (Enum.TryParse(action.Value, ignoreCase: true, out ICMessage.EmoteModifiers emoteMod))
                    {
                        profileClient.emoteMod = emoteMod;
                        appliedChanges.Add("set emote modifier to " + action.Value);
                    }

                    break;

                case AgentActionType.SetOffset:
                    profileClient.SelfOffset = (
                        action.Horizontal ?? profileClient.SelfOffset.Horizontal,
                        action.Vertical ?? profileClient.SelfOffset.Vertical);
                    appliedChanges.Add("set offset to " + profileClient.SelfOffset.Horizontal + "," + profileClient.SelfOffset.Vertical);
                    break;

                case AgentActionType.SetArea:
                    if (useSingleInternalClient)
                    {
                        ApplyProfileToSingleInternalClient(profileClient);
                        networkClient = singleInternalClient ?? profileClient;
                    }

                    await networkClient.SetArea(action.Value);
                    appliedChanges.Add("moved to area " + action.Value);
                    break;

                case AgentActionType.SetIniPuppet:
                    if (useSingleInternalClient)
                    {
                        ApplyProfileToSingleInternalClient(profileClient);
                        networkClient = singleInternalClient ?? profileClient;
                    }

                    await networkClient.SelectIniPuppet(action.Value, false);
                    if (useSingleInternalClient)
                    {
                        SyncSingleClientStatusToProfile(profileClient);
                    }

                    appliedChanges.Add("selected INI puppet " + action.Value);
                    break;

                case AgentActionType.Speak:
                    await ExecuteSpeakActionAsync(
                        profileClient, networkClient, action, appliedChanges,
                        previousSfx, previousShoutModifier, previousEffect, previousScreenshake,
                        previousPreanimEnabled, previousImmediate, previousAdditive);
                    break;
            }
        }

        private async Task ExecuteSpeakActionAsync(
            AOClient profileClient,
            AOClient networkClient,
            AgentAction action,
            List<string> appliedChanges,
            string previousSfx,
            ICMessage.ShoutModifiers previousShoutModifier,
            ICMessage.Effects previousEffect,
            bool previousScreenshake,
            bool previousPreanimEnabled,
            bool previousImmediate,
            bool previousAdditive)
        {
            ICMessage.TextColors previousSpeakTextColor = profileClient.textColor;

            // Apply speak-level emote before sending.
            if (!string.IsNullOrWhiteSpace(action.Emote))
            {
                ApplyEmote(profileClient, action.Emote, appliedChanges);
            }

            // Apply transient speak fields.
            if (!string.IsNullOrWhiteSpace(action.TextColor)
                && Enum.TryParse(action.TextColor, ignoreCase: true, out ICMessage.TextColors speakColor))
            {
                profileClient.textColor = speakColor;
            }

            if (!string.IsNullOrWhiteSpace(action.ShoutModifier)
                && Enum.TryParse(action.ShoutModifier, ignoreCase: true, out ICMessage.ShoutModifiers speakShout))
            {
                profileClient.shoutModifiers = speakShout;
            }

            if (!string.IsNullOrWhiteSpace(action.Effect)
                && Enum.TryParse(action.Effect, ignoreCase: true, out ICMessage.Effects speakEffect))
            {
                profileClient.effect = speakEffect;
            }

            if (action.Screenshake.HasValue)
            {
                profileClient.screenshake = action.Screenshake.Value;
            }

            if (!string.IsNullOrWhiteSpace(action.Sfx))
            {
                profileClient.curSFX = action.Sfx.Trim();
            }

            if (useSingleInternalClient)
            {
                ApplyProfileToSingleInternalClient(profileClient);
                networkClient = singleInternalClient ?? profileClient;
            }

            string outgoingMessage = action.Message?.TrimEnd('\r', '\n') ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(outgoingMessage))
            {
                string channel = string.IsNullOrWhiteSpace(action.Channel) ? "IC" : action.Channel.Trim();
                outgoingMessage = NormalizeAiOutgoingMessage(channel, outgoingMessage, out bool truncated);
                if (truncated)
                {
                    appliedChanges.Add("trimmed message for AO compatibility");
                }

                if (string.Equals(channel, "OOC", StringComparison.OrdinalIgnoreCase))
                {
                    string oocShowname = string.IsNullOrWhiteSpace(profileClient.OOCShowname)
                        ? profileClient.clientName
                        : profileClient.OOCShowname;
                    await networkClient.SendOOCMessage(oocShowname, outgoingMessage);
                    appliedChanges.Add("sent OOC message");
                }
                else
                {
                    await networkClient.SendICMessage(outgoingMessage);
                    appliedChanges.Add("sent IC message");
                }

                RestoreAiTransientState(
                    profileClient,
                    previousSfx,
                    previousShoutModifier,
                    previousEffect,
                    previousScreenshake,
                    previousPreanimEnabled,
                    previousImmediate,
                    previousAdditive);

                profileClient.textColor = previousSpeakTextColor;
                if (useSingleInternalClient)
                {
                    ApplyProfileToSingleInternalClient(profileClient);
                }
            }
        }

        private void RestoreAiTransientState(
            AOClient profileClient,
            string previousSfx,
            ICMessage.ShoutModifiers previousShoutModifier,
            ICMessage.Effects previousEffect,
            bool previousScreenshake,
            bool previousPreanimEnabled,
            bool previousImmediate,
            bool previousAdditive)
        {
            profileClient.curSFX = previousSfx;
            profileClient.shoutModifiers = previousShoutModifier;
            profileClient.effect = previousEffect;
            profileClient.screenshake = previousScreenshake;
            profileClient.PreanimEnabled = previousPreanimEnabled;
            profileClient.Immediate = previousImmediate;
            profileClient.Additive = previousAdditive;

            if (useSingleInternalClient)
            {
                ApplyProfileToSingleInternalClient(profileClient);
            }
        }

        private static void ApplyEmote(AOClient profileClient, string emoteName, List<string> appliedChanges)
        {
            Emote? resolvedEmote = profileClient.currentINI?.configINI?.Emotions?.Values
                .FirstOrDefault(e =>
                    string.Equals(e.Name, emoteName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.DisplayID, emoteName, StringComparison.OrdinalIgnoreCase));
            if (resolvedEmote != null)
            {
                profileClient.SetEmote(resolvedEmote.DisplayID);
                appliedChanges.Add("set emote to " + resolvedEmote.Name);
            }
        }

        private static string NormalizeAiOutgoingMessage(string channel, string message, out bool truncated)
        {
            truncated = false;

            int maxLength = string.Equals(channel, "OOC", StringComparison.OrdinalIgnoreCase) ? 240 : 220;
            string normalized = (message ?? string.Empty).Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            truncated = true;
            return normalized.Substring(0, Math.Max(0, maxLength - 1)).TrimEnd() + "…";
        }

        private void HandleAiControllerStatusChanged(AOClient profileClient, AOClientAgentStatusUpdate update)
        {
            if (profileClient == null || update == null)
            {
                return;
            }

            string prefix = $"[AI:{GetAiClientDisplayName(profileClient)}]";

            switch (update.Kind)
            {
                case AOClientAgentStatusKind.TransientPrimary:
                    // Log evaluation start / retry to debug console only — not the IC log
                    CustomConsole.Info($"{prefix} {update.Message}");
                    break;

                case AOClientAgentStatusKind.TransientPreview:
                    // Streaming preview is too frequent to log usefully — skip
                    break;

                case AOClientAgentStatusKind.ClearTransient:
                    // Nothing to clear from IC log since we never add transient entries there
                    break;

                case AOClientAgentStatusKind.WaitDecision:
                    CustomConsole.Info($"{prefix} SYSTEM_WAIT — decided to stay silent.");
                    break;

                case AOClientAgentStatusKind.FinalMessage:
                    HandleAiFinalMessage(profileClient, update, prefix);
                    break;

                default:
                    break;
            }

            UpdateClientTooltip(profileClient);
        }

        private void HandleAiFinalMessage(AOClient profileClient, AOClientAgentStatusUpdate update, string consolePrefix)
        {
            if (update.IsError)
            {
                // Log full details to debug console
                CustomConsole.Error($"{consolePrefix} {update.Message}");
                if (!string.IsNullOrWhiteSpace(update.RawResponse))
                {
                    CustomConsole.Info($"{consolePrefix} Raw response:\n{update.RawResponse}");
                }

                // Show a minimal error entry in the IC log so the user sees something went wrong
                IReadOnlyList<LogMessageActionLink>? errorLinks = BuildRawResponseLinks(
                    profileClient,
                    string.IsNullOrWhiteSpace(update.RawResponse) ? null : update.RawResponse,
                    "AI Error",
                    "(see details)");
                ICLogControl.AddMessage(
                    profileClient,
                    GetAiClientDisplayName(profileClient),
                    "[AI Error] Could not generate a valid response.",
                    true,
                    ICMessage.TextColors.Red,
                    messageLinks: errorLinks);
            }
            else
            {
                // Success — log to debug console only; the actual AO2 echo will appear in the IC log
                // with an (AI) hyperlink via QueuePendingAiOriginResponse below.
                CustomConsole.Info($"{consolePrefix} {update.Message}");
                if (!string.IsNullOrWhiteSpace(update.RawResponse))
                {
                    CustomConsole.Info($"{consolePrefix} Raw response:\n{update.RawResponse}");
                }
            }

            // Queue the pending origin record so the echoed AO2 message gets an (AI) hyperlink
            if (!update.IsError && !string.IsNullOrWhiteSpace(update.RawResponse))
            {
                QueuePendingAiOriginResponse(profileClient, update.RawResponse);
            }
        }

        /// <summary>
        /// Returns the display name to use for this AI client in logs and IC entries.
        /// </summary>
        private static string GetAiClientDisplayName(AOClient profileClient)
        {
            return string.IsNullOrWhiteSpace(profileClient?.ICShowname)
                ? (profileClient?.clientName ?? "AI")
                : profileClient.ICShowname;
        }

        private IReadOnlyList<LogMessageActionLink>? BuildRawResponseLinks(
            AOClient profileClient,
            string? rawResponse,
            string titleSuffix,
            string linkText = "(See AI response)")
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return null;
            }

            return new[]
            {
                new LogMessageActionLink(
                    linkText,
                    () => ShowAiResponseDialog(profileClient, titleSuffix, rawResponse),
                    "Open the raw AI response")
            };
        }

        private void ShowAiResponseDialog(AOClient profileClient, string titleSuffix, string rawResponse)
        {
            string clientName = profileClient?.clientName?.Trim() ?? "Unknown Client";
            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            string dialogTitle = "AI Response - " + titleSuffix + " (" + clientName + ")";
            TextDocumentViewerWindow.ShowViewer(owner, dialogTitle, rawResponse ?? string.Empty);
        }

        private void QueuePendingAiOriginResponse(
            AOClient profileClient,
            string rawResponse)
        {
            // Use current client state to determine expected show name for origin matching.
            // The executor already applied all actions, so profileClient reflects the final state.
            string icShowname = profileClient.ICShowname?.Trim() ?? string.Empty;
            string oocShowname = string.IsNullOrWhiteSpace(profileClient.OOCShowname)
                ? profileClient.clientName
                : profileClient.OOCShowname;

            // Queue both IC and OOC variants since we don't track which channel was used at this level.
            List<PendingAiOriginResponse> pendingResponses = GetPendingAiOriginResponses(profileClient);
            PrunePendingAiOriginResponses(pendingResponses);
            pendingResponses.Add(new PendingAiOriginResponse
            {
                Channel = "IC",
                ShowName = icShowname,
                Message = string.Empty,
                RawResponse = rawResponse,
                CreatedUtc = DateTime.UtcNow
            });
            pendingResponses.Add(new PendingAiOriginResponse
            {
                Channel = "OOC",
                ShowName = oocShowname?.Trim() ?? string.Empty,
                Message = string.Empty,
                RawResponse = rawResponse,
                CreatedUtc = DateTime.UtcNow
            });
        }

        private List<PendingAiOriginResponse> GetPendingAiOriginResponses(AOClient profileClient)
        {
            if (!pendingAiOriginResponses.TryGetValue(profileClient, out List<PendingAiOriginResponse>? pendingResponses))
            {
                pendingResponses = new List<PendingAiOriginResponse>();
                pendingAiOriginResponses[profileClient] = pendingResponses;
            }

            return pendingResponses;
        }

        private static void PrunePendingAiOriginResponses(List<PendingAiOriginResponse> pendingResponses)
        {
            DateTime cutoffUtc = DateTime.UtcNow - PendingAiOriginRetention;
            pendingResponses.RemoveAll(response => response.CreatedUtc < cutoffUtc);
        }

        private bool TryTakePendingAiOriginResponse(
            AOClient profileClient,
            string channel,
            string showName,
            string message,
            out PendingAiOriginResponse? pendingResponse)
        {
            pendingResponse = null;
            if (!pendingAiOriginResponses.TryGetValue(profileClient, out List<PendingAiOriginResponse>? pendingResponses))
            {
                return false;
            }

            PrunePendingAiOriginResponses(pendingResponses);
            string normalizedChannel = channel?.Trim() ?? string.Empty;
            string normalizedShowName = showName?.Trim() ?? string.Empty;
            string normalizedMessage = message?.TrimEnd('\r', '\n') ?? string.Empty;

            for (int index = 0; index < pendingResponses.Count; index++)
            {
                PendingAiOriginResponse candidate = pendingResponses[index];
                if (!string.Equals(candidate.Channel, normalizedChannel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Match by message content if available, otherwise match by showname + channel only.
                if (!string.IsNullOrWhiteSpace(candidate.Message)
                    && !string.Equals(candidate.Message, normalizedMessage, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidate.ShowName)
                    && !string.Equals(candidate.ShowName, normalizedShowName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pendingResponses.RemoveAt(index);
                pendingResponse = candidate;
                return true;
            }

            return false;
        }

        private bool IsAiOriginResponseVisible(AOClient profileClient)
        {
            return aiOriginResponseVisibility.TryGetValue(profileClient, out bool isVisible) && isVisible;
        }

        private void SetAiOriginResponseVisibility(AOClient profileClient, bool isVisible)
        {
            aiOriginResponseVisibility[profileClient] = isVisible;
        }

        private void ClearAiClientState(AOClient profileClient)
        {
            pendingAiOriginResponses.Remove(profileClient);
            aiOriginResponseVisibility.Remove(profileClient);
        }

        private void AddLoggedIcMessage(
            AOClient profileClient,
            string showName,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor)
        {
            if (!isSentFromSelf)
            {
                callwordAudioNotifier.TryNotify(message);
            }

            IReadOnlyList<LogMessageActionLink>? nameLinks = null;
            if (isSentFromSelf
                && TryTakePendingAiOriginResponse(profileClient, "IC", showName, message, out PendingAiOriginResponse? pendingResponse)
                && pendingResponse != null)
            {
                nameLinks = BuildRawResponseLinks(profileClient, pendingResponse.RawResponse, "AI Response", "(AI)");
            }

            ICLogControl.AddMessage(
                profileClient,
                showName,
                message,
                isSentFromSelf,
                textColor,
                nameLinks: nameLinks);
        }

        private void AddLoggedIcActionMessage(
            AOClient profileClient,
            string showName,
            string action,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor)
        {
            ICLogControl.AddActionMessage(
                profileClient,
                showName,
                action,
                message,
                isSentFromSelf,
                textColor);
        }

        private void AppendAo2ActionLog(
            AOClient profileClient,
            string showName,
            string combinedAction,
            bool isSentFromSelf,
            ICMessage.TextColors textColor)
        {
            string normalized = (combinedAction ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            const string shoutPrefix = "shouts ";
            const string evidencePrefix = "has presented evidence ";
            const string musicPrefix = "has played a song ";

            if (normalized.StartsWith(shoutPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddLoggedIcActionMessage(profileClient, showName, "shouts", normalized.Substring(shoutPrefix.Length), isSentFromSelf, textColor);
                return;
            }

            if (normalized.StartsWith(evidencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddLoggedIcActionMessage(profileClient, showName, "has presented evidence", normalized.Substring(evidencePrefix.Length), isSentFromSelf, textColor);
                return;
            }

            if (normalized.StartsWith(musicPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddLoggedIcActionMessage(profileClient, showName, "has played a song", normalized.Substring(musicPrefix.Length), isSentFromSelf, textColor);
                return;
            }

            if (string.Equals(normalized, "has stopped the music.", StringComparison.OrdinalIgnoreCase))
            {
                AddLoggedIcActionMessage(profileClient, showName, "has stopped the music.", string.Empty, isSentFromSelf, textColor);
                return;
            }

            AddLoggedIcMessage(profileClient, showName, normalized, isSentFromSelf, textColor);
        }

        private void AddLoggedOocMessage(
            AOClient profileClient,
            string showName,
            string message,
            bool isFromServer)
        {
            bool isSentFromSelf = !isFromServer
                && !string.IsNullOrWhiteSpace(profileClient.OOCShowname)
                && string.Equals(showName, profileClient.OOCShowname, StringComparison.OrdinalIgnoreCase);
            if (!isSentFromSelf)
            {
                callwordAudioNotifier.TryNotify(message);
            }

            IReadOnlyList<LogMessageActionLink>? nameLinks = null;
            if (isSentFromSelf
                && TryTakePendingAiOriginResponse(profileClient, "OOC", showName, message, out PendingAiOriginResponse? pendingResponse)
                && pendingResponse != null)
            {
                nameLinks = BuildRawResponseLinks(profileClient, pendingResponse.RawResponse, "AI Response", "(AI)");
            }

            OOCLogControl.AddMessage(
                profileClient,
                showName,
                message,
                isFromServer,
                nameLinks: nameLinks);
        }

        private void RecordAiMessageForClient(
            AOClient profileClient,
            AOClient networkClient,
            string chatLogType,
            string characterName,
            string showName,
            string message,
            int iniPuppetId,
            bool isFromServer)
        {
            if (!aiModeEnabled)
            {
                return;
            }

            AOClientAgentController controller = EnsureAiController(profileClient);
            ChatLogEntry entry = new ChatLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                ChatLogType = chatLogType,
                CharacterName = characterName,
                ShowName = showName,
                Message = message,
                IniPuppetId = iniPuppetId,
                IsFromSelf = IsMessageFromSelf(profileClient, networkClient, chatLogType, characterName, showName, iniPuppetId),
                IsFromServer = isFromServer
            };
            controller.RecordMessage(entry);
        }

        private void BroadcastAiMessageFromSingleClient(
            string chatLogType,
            string characterName,
            string showName,
            string message,
            int iniPuppetId,
            bool isFromServer)
        {
            if (!aiModeEnabled || singleInternalClient == null)
            {
                return;
            }

            foreach (AOClient profileClient in aiControllers.Keys.ToList())
            {
                RecordAiMessageForClient(
                    profileClient,
                    singleInternalClient,
                    chatLogType,
                    characterName,
                    showName,
                    message,
                    iniPuppetId,
                    isFromServer);
            }
        }

        private static bool IsMessageFromSelf(
            AOClient profileClient,
            AOClient networkClient,
            string chatLogType,
            string characterName,
            string showName,
            int iniPuppetId)
        {
            if (string.Equals(chatLogType, "OOC", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(profileClient.OOCShowname)
                    && string.Equals(showName, profileClient.OOCShowname, StringComparison.OrdinalIgnoreCase);
            }

            bool sameCharacter = string.Equals(
                characterName?.Trim(),
                profileClient.currentINI?.Name?.Trim(),
                StringComparison.OrdinalIgnoreCase);
            bool sameShowname = string.Equals(
                showName?.Trim(),
                profileClient.ICShowname?.Trim(),
                StringComparison.OrdinalIgnoreCase);
            return iniPuppetId == networkClient.iniPuppetID || (sameCharacter && sameShowname);
        }

        private void RefreshUiAfterAiStateChanged(AOClient profileClient)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateClientTooltip(profileClient);

                if (currentClient == profileClient)
                {
                    ICMessageSettingsControl.SetClient(profileClient);
                    OOCLogControl.SetCurrentClient(profileClient);
                    OOCLogControl.txtOOCShowname.Text = profileClient.OOCShowname;
                    RefreshAreaNavigatorForCurrentClient();
                    RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
                    UpdateDreddFeatureEnabledState();
                }
            });
        }

        private static bool TryResolveStringChoice(
            IEnumerable<string> availableValues,
            string requestedValue,
            out string resolvedValue)
        {
            resolvedValue = string.Empty;
            string normalizedRequestedValue = requestedValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedRequestedValue))
            {
                return false;
            }

            List<string> snapshotValues = (availableValues ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            // 1. Exact case-insensitive match.
            foreach (string availableValue in snapshotValues)
            {
                if (string.Equals(availableValue.Trim(), normalizedRequestedValue, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedValue = availableValue;
                    return true;
                }
            }

            // 2. Substring fallback: available value contains request, or request contains available value.
            //    Only use if exactly one candidate matches to avoid ambiguous results.
            List<string> candidates = snapshotValues
                .Where(v =>
                    v.IndexOf(normalizedRequestedValue, StringComparison.OrdinalIgnoreCase) >= 0
                    || normalizedRequestedValue.IndexOf(v.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (candidates.Count == 1)
            {
                resolvedValue = candidates[0];
                return true;
            }

            return false;
        }

        private bool IsAiEnabled(AOClient profileClient)
        {
            return aiModeEnabled
                && aiControllers.TryGetValue(profileClient, out AOClientAgentController? controller)
                && controller.IsEnabled;
        }

        private void ToggleAiAutopilot(AOClient profileClient)
        {
            AOClientAgentController controller = EnsureAiController(profileClient);
            controller.SetEnabled(!controller.IsEnabled);
            RefreshUiAfterAiStateChanged(profileClient);
        }

        private async Task TriggerAiThinkingAsync(AOClient profileClient)
        {
            AOClientAgentController controller = EnsureAiController(profileClient);
            await controller.TriggerManualEvaluationAsync();
        }

        private void ToggleAiOriginResponseTags(AOClient profileClient)
        {
            SetAiOriginResponseVisibility(profileClient, !IsAiOriginResponseVisible(profileClient));
        }

        private void SetAiProvider(AiProviderKind provider)
        {
            SaveFile.Data.AO2AiBot.Provider = provider == AiProviderKind.OpenAI ? "OpenAI" : "Ollama";
            SaveFile.Save();
        }

        private void ConfigureAiModel()
        {
            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            AiChatProviderSettings settings = BuildAiSettings();
            bool useOpenAi = settings.Provider == AiProviderKind.OpenAI;
            string currentValue = useOpenAi ? SaveFile.Data.AO2AiBot.OpenAIModel : SaveFile.Data.AO2AiBot.OllamaModel;
            string prompt = useOpenAi ? "Enter the OpenAI model name:" : "Enter the Ollama model name:";
            string value = InputDialog.Show(owner, prompt, "AI Model", currentValue);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (useOpenAi)
            {
                SaveFile.Data.AO2AiBot.OpenAIModel = value.Trim();
            }
            else
            {
                SaveFile.Data.AO2AiBot.OllamaModel = value.Trim();
            }

            SaveFile.Save();
        }

        private void ConfigureAiOllamaEndpoint()
        {
            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            string value = InputDialog.Show(
                owner,
                "Enter the Ollama base URL:",
                "Ollama Endpoint",
                SaveFile.Data.AO2AiBot.OllamaEndpoint);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            SaveFile.Data.AO2AiBot.OllamaEndpoint = value.Trim();
            SaveFile.Save();
        }

        private void ConfigureAiPersonality()
        {
            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            string currentValue = SaveFile.Data.AO2AiBot.PersonalityPrompt;
            string? value = MultilineInputDialog.Show(
                owner,
                "Enter a personality or role description for the AI.\nThis is appended to the system prompt and lets you define who the AI is and how it should behave.\n\nLeave blank to use default behavior.",
                "AI Personality",
                currentValue);

            if (value == null)
            {
                return;
            }

            SaveFile.Data.AO2AiBot.PersonalityPrompt = value.Trim();
            SaveFile.Save();
        }

        private void AttachAiContextMenuItems(ContextMenu contextMenu, AOClient profileClient)
        {
            if (!aiModeEnabled)
            {
                return;
            }

            contextMenu.Items.Add(new Separator());

            MenuItem aiRootMenu = new MenuItem
            {
                Header = "AO2 AI Bot"
            };

            MenuItem toggleAutopilotItem = new MenuItem();
            toggleAutopilotItem.Click += (_, _) => ToggleAiAutopilot(profileClient);
            aiRootMenu.Items.Add(toggleAutopilotItem);

            MenuItem thinkNowItem = new MenuItem
            {
                Header = "Think Now"
            };
            thinkNowItem.Click += async (_, _) => await TriggerAiThinkingAsync(profileClient);
            aiRootMenu.Items.Add(thinkNowItem);

            MenuItem showAiOriginTagsItem = new MenuItem
            {
                Header = "Show (AI) Response Tags",
                IsCheckable = true
            };
            showAiOriginTagsItem.Click += (_, _) => ToggleAiOriginResponseTags(profileClient);
            aiRootMenu.Items.Add(showAiOriginTagsItem);

            aiRootMenu.Items.Add(new Separator());

            MenuItem useOllamaItem = new MenuItem
            {
                Header = "Use Ollama"
            };
            useOllamaItem.Click += (_, _) => SetAiProvider(AiProviderKind.Ollama);
            aiRootMenu.Items.Add(useOllamaItem);

            MenuItem useOpenAiItem = new MenuItem
            {
                Header = "Use OpenAI"
            };
            useOpenAiItem.Click += (_, _) => SetAiProvider(AiProviderKind.OpenAI);
            aiRootMenu.Items.Add(useOpenAiItem);

            MenuItem setModelItem = new MenuItem
            {
                Header = "Set Active Model..."
            };
            setModelItem.Click += (_, _) => ConfigureAiModel();
            aiRootMenu.Items.Add(setModelItem);

            MenuItem setOllamaEndpointItem = new MenuItem
            {
                Header = "Set Ollama Endpoint..."
            };
            setOllamaEndpointItem.Click += (_, _) => ConfigureAiOllamaEndpoint();
            aiRootMenu.Items.Add(setOllamaEndpointItem);

            aiRootMenu.Items.Add(new Separator());

            MenuItem setPersonalityItem = new MenuItem
            {
                Header = "Set AI Personality..."
            };
            setPersonalityItem.Click += (_, _) => ConfigureAiPersonality();
            aiRootMenu.Items.Add(setPersonalityItem);

            aiRootMenu.SubmenuOpened += (_, _) =>
            {
                bool enabled = IsAiEnabled(profileClient);
                toggleAutopilotItem.Header = enabled ? "Disable Autopilot" : "Enable Autopilot";

                AiChatProviderSettings settings = BuildAiSettings();
                useOllamaItem.IsCheckable = true;
                useOpenAiItem.IsCheckable = true;
                useOllamaItem.IsChecked = settings.Provider == AiProviderKind.Ollama;
                useOpenAiItem.IsChecked = settings.Provider == AiProviderKind.OpenAI;
                showAiOriginTagsItem.IsChecked = IsAiOriginResponseVisible(profileClient);
            };

            contextMenu.Items.Add(aiRootMenu);
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

        private void EnsureSingleInternalClientConnectedForTests()
        {
            if (singleInternalClient != null)
            {
                return;
            }

            singleInternalClient = new AOClient(Globals.GetSelectedServerEndpoint());
            singleInternalClient.clientName = "InternalClient";
            singleInternalClient.ApplyAreaStateForTests(
                "Lobby",
                new[]
                {
                    "Lobby",
                    "Courtroom 2",
                    "Detention Center"
                });
        }

        private void InitializeDreddFeatureUi()
        {
            isDreddFeatureEnabled = SaveFile.Data.AdvancedFeatures.IsEnabled(AdvancedFeatureIds.DreddBackgroundOverlayOverride);
            DreddStickyOverlayCheckBox.IsChecked = SaveFile.Data.DreddBackgroundOverlayOverride.StickyOverlay;
            Height = isDreddFeatureEnabled ? 658 : 628;
            imgScienceBlur.Height = isDreddFeatureEnabled ? 658 : 628;
            imgScienceBlur_darken.Height = isDreddFeatureEnabled ? 658 : 628;

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
            Canvas.SetTop(btnViewport, 603 + verticalOffset);

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
                Owner = HostWindow
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
            singleInternalClient.PreanimEnabled = profileClient.PreanimEnabled;
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
                        "Connection disconnected.",
                        true,
                        ICMessage.TextColors.Red
                    );
                    OOCLogControl.AddMessage(targetClient, "Oceanya Client", "Connection disconnected.", true);
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
                    AddLoggedIcMessage(targetClient, icMessage.ShowName, ICMessage.StripFormattingCodes(icMessage.Message), isSentFromSelf, icMessage.TextColor);

                    targetClient.curBG = singleInternalClient.curBG;
                    targetClient.iniPuppetID = singleInternalClient.iniPuppetID;
                });
            };
            singleInternalClient.OnIcActionReceived += (string showName, string action, bool isSentFromSelf, ICMessage.TextColors textColor) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AOClient? targetClient = GetSingleModeLogTarget(singleInternalClient, singleInternalClient);
                    if (targetClient == null)
                    {
                        return;
                    }

                    AppendAo2ActionLog(targetClient, showName, action, isSentFromSelf, textColor);
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

                    AddLoggedOocMessage(targetClient, showName, message, isFromServer);
                });
            };

            singleInternalClient.OnMessageReceived += (string chatLogType, string characterName, string showName, string message, int iniPuppetId, bool isFromServer) =>
            {
                BroadcastAiMessageFromSingleClient(chatLogType, characterName, showName, message, iniPuppetId, isFromServer);
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
            RefreshViewportAttachment();
        }
        private void AddClient(string clientName)
        {
            _ = AddClientAsync(clientName);
        }

        private static async Task ConnectClientAsync(AOClient bot)
        {
            if (testConnectClientAsyncOverride != null)
            {
                await testConnectClientAsyncOverride(bot);
                return;
            }

            await bot.Connect();
        }

        private void ShowMainWindowMessage(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            Action<Window?, string, string, MessageBoxButton, MessageBoxImage>? overrideHandler = testMessageBoxOverride;
            if (overrideHandler != null)
            {
                overrideHandler(this, message, title, buttons, image);
                return;
            }

            OceanyaMessageBox.Show(message, title, buttons, image);
        }

        private void AttachDirectClientMessageHandlers(AOClient bot)
        {
            bot.OnICMessageReceived += (ICMessage icMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    bool isSentFromSelf = clients.Select(x => x.Value.iniPuppetID).Contains(icMessage.CharId);

                    AddLoggedIcMessage(bot, icMessage.ShowName, ICMessage.StripFormattingCodes(icMessage.Message), isSentFromSelf, icMessage.TextColor);
                });
            };
            bot.OnIcActionReceived += (string showName, string action, bool isSentFromSelf, ICMessage.TextColors textColor) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AppendAo2ActionLog(bot, showName, action, isSentFromSelf, textColor);
                });
            };

            bot.OnOOCMessageReceived += (string showName, string message, bool isFromServer) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddLoggedOocMessage(bot, showName, message, isFromServer);
                });
            };

            bot.OnMessageReceived += (string chatLogType, string characterName, string showName, string message, int iniPuppetId, bool isFromServer) =>
            {
                RecordAiMessageForClient(bot, bot, chatLogType, characterName, showName, message, iniPuppetId, isFromServer);
            };
        }
        private async Task AddClientAsync(string clientName)
        {
            IsEnabled = false;  
            Window? waitOwner = HostWindow ?? Application.Current?.MainWindow;
            if (waitOwner == null)
            {
                return;
            }

            await WaitForm.ShowFormAsync("Connecting client...", waitOwner);

            try
            {
                AOClient bot = new AOClient(Globals.GetSelectedServerEndpoint());
                bot.clientName = clientName;
                HookClientForDreddOverlay(bot);
                if (aiModeEnabled)
                {
                    EnsureAiController(bot);
                    aiOriginResponseVisibility[bot] = true;
                }

                if (useSingleInternalClient)
                {
                    if (boundSingleClientProfile == null)
                    {
                        boundSingleClientProfile = bot;
                    }

                    if (OceanyaTestMode.Current.IsEnabled)
                    {
                        EnsureSingleInternalClientConnectedForTests();
                    }
                    else
                    {
                        await EnsureSingleInternalClientConnectedAsync();
                    }
                }
                else
                {
                    AttachDirectClientMessageHandlers(bot);
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
                AutomationProperties.SetAutomationId(toggleBtn, "Main.Client." + (clients.Count + 1).ToString());
                AutomationProperties.SetName(toggleBtn, bot.clientName);
                
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
                AttachAiContextMenuItems(contextMenu, bot);


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
                    await ConnectClientAsync(bot);
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
                        ClearAiClientState(bot);
                        if (aiControllers.Remove(bot, out AOClientAgentController? controller))
                        {
                            controller.Dispose();
                        }

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
                ShowMainWindowMessage($"Error connecting client: {ex.Message}", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            OOCLogControl.txtOOCShowname.Text = currentClient.OOCShowname;
            ICLogControl.SetCurrentClient(currentClient);
            RefreshViewportAttachment();
            RefreshAreaNavigatorForCurrentClient();

            if (isDreddFeatureEnabled && DreddStickyOverlayCheckBox.IsChecked == true)
            {
                ApplyStoredDreddStickyOverlay(showFeedbackOnFailure: false);
            }

            RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: true);
            UpdateDreddFeatureEnabledState();
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

        private void btnViewport_Click(object sender, RoutedEventArgs e)
        {
            if (viewportWindow?.IsVisible == true)
            {
                SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
                SaveFile.Data.GMViewportWindowState.IsVisible = false;
                SaveFile.Save();
                viewportWindow.Hide();
                return;
            }

            OpenViewportWindow();
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        private void OpenViewportWindow()
        {
            EnsureViewportContent();
            if (viewportWindow != null)
            {
                if (!viewportWindow.IsVisible)
                {
                    viewportWindow.Show();
                }

                viewportWindow.Activate();
                RefreshViewportAttachment();
                SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
                SaveFile.Data.GMViewportWindowState.IsVisible = true;
                SaveFile.Save();
                return;
            }

            RefreshViewportAttachment();

            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            ViewportWindowState savedState = SaveFile.Data.GMViewportWindowState ?? new ViewportWindowState();
            double defaultWidth = GetViewportWindowWidthFromContentWidth(AO2ViewportAssetResolver.ViewportWidth);
            double defaultHeight = GetViewportWindowHeightFromContentHeight(AO2ViewportAssetResolver.ViewportToolHeight);
            double initialWidth = Math.Max(defaultWidth, savedState.Width);
            double initialHeight = Math.Max(defaultHeight, savedState.Height);
            (initialWidth, initialHeight) = NormalizeViewportWindowSize(initialWidth, initialHeight, preferWidth: true);

            viewportWindow = OceanyaWindowManager.CreateWindow(
                viewportContent!,
                new OceanyaWindowPresentationOptions
                {
                    Owner = owner,
                    Title = "AO2 Viewport",
                    HeaderText = "Viewport",
                    Width = initialWidth,
                    Height = initialHeight,
                    MinWidth = defaultWidth,
                    MinHeight = defaultHeight,
                    ShowInTaskbar = false,
                    IsUserResizeEnabled = true,
                    IsUserMoveEnabled = true,
                    IsCloseButtonVisible = true,
                    WindowStartupLocation = savedState.Left.HasValue && savedState.Top.HasValue
                        ? WindowStartupLocation.Manual
                        : WindowStartupLocation.CenterOwner
                });

            if (savedState.Left.HasValue && savedState.Top.HasValue)
            {
                viewportWindow.Left = savedState.Left.Value;
                viewportWindow.Top = savedState.Top.Value;
            }

            viewportWindow.SourceInitialized += ViewportWindow_SourceInitialized;
            viewportWindow.LocationChanged += ViewportWindow_LocationChanged;
            viewportWindow.Closing += (sender, eventArgs) =>
            {
                CaptureViewportWindowState();
                if (isMainWindowClosing)
                {
                    return;
                }

                eventArgs.Cancel = true;
                SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
                SaveFile.Data.GMViewportWindowState.IsVisible = false;
                SaveFile.Save();
                viewportWindow?.Hide();
            };
            viewportWindow.Closed += (_, _) =>
            {
                viewportWindow.SourceInitialized -= ViewportWindow_SourceInitialized;
                viewportWindow.LocationChanged -= ViewportWindow_LocationChanged;
                viewportWindowSource?.RemoveHook(ViewportWindow_WndProc);
                viewportWindowSource = null;
                viewportWindow = null;
            };
            viewportWindow.Show();
            viewportWindow.Activate();
            NormalizeVisibleViewportWindowSize(preferWidth: true);
            CaptureViewportWindowState();
            SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
            SaveFile.Data.GMViewportWindowState.IsVisible = true;
            SaveFile.Save();
        }

        private void OpenSettingsWindow()
        {
            if (settingsWindow != null)
            {
                settingsWindow.Activate();
                return;
            }

            SettingsWindow settingsContent = new SettingsWindow();
            settingsContent.SettingsSaved += ApplySavedClientSettingsToRuntime;
            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            settingsWindow = OceanyaWindowManager.CreateWindow(
                settingsContent,
                new OceanyaWindowPresentationOptions
                {
                    Owner = owner,
                    Title = "Settings",
                    HeaderText = "Settings",
                    Width = 560,
                    Height = 430,
                    MinWidth = 520,
                    MinHeight = 400,
                    ShowInTaskbar = false,
                    IsUserResizeEnabled = true,
                    IsUserMoveEnabled = true,
                    IsCloseButtonVisible = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
            settingsWindow.Closed += (_, _) =>
            {
                settingsContent.SettingsSaved -= ApplySavedClientSettingsToRuntime;
                settingsWindow = null;
            };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void ApplySavedClientSettingsToRuntime()
        {
            ICMessageSettingsControl.stickyEffects = SaveFile.Data.StickyEffect;
            chkSticky.IsChecked = SaveFile.Data.StickyEffect;
            chkPosOnIniSwap.IsChecked = SaveFile.Data.SwitchPosOnIniSwap;
            chkInvertLog.IsChecked = SaveFile.Data.InvertICLog;
            foreach (AOClient client in clients.Values)
            {
                client.switchPosWhenChangingINI = SaveFile.Data.SwitchPosOnIniSwap;
            }

            ICLogControl.SetInvertOnClientLogs(SaveFile.Data.InvertICLog);
        }

        private void RefreshViewportAttachment()
        {
            EnsureViewportContent();
            AOClient? incomingMessageClient = GetTargetClientForNetwork(currentClient) ?? currentClient;
            viewportContent?.AttachClient(currentClient, incomingMessageClient);
        }

        private void EnsureViewportContent()
        {
            viewportContent ??= new AO2ViewportWindowContent();
        }

        private void ViewportWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (viewportWindow == null)
            {
                return;
            }

            viewportWindowSource = HwndSource.FromHwnd(new WindowInteropHelper(viewportWindow).Handle);
            viewportWindowSource?.AddHook(ViewportWindow_WndProc);
        }

        private void ViewportWindow_LocationChanged(object? sender, EventArgs e)
        {
            CaptureViewportWindowState();
        }

        private void NormalizeVisibleViewportWindowSize(bool preferWidth)
        {
            if (viewportWindow == null || viewportWindow.WindowState != WindowState.Normal)
            {
                return;
            }

            (double desiredWidth, double desiredHeight) =
                NormalizeViewportWindowSize(viewportWindow.Width, viewportWindow.Height, preferWidth);
            if (Math.Abs(viewportWindow.Width - desiredWidth) < 0.5
                && Math.Abs(viewportWindow.Height - desiredHeight) < 0.5)
            {
                return;
            }

            viewportWindow.Width = desiredWidth;
            viewportWindow.Height = desiredHeight;
        }

        private IntPtr ViewportWindow_WndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmGetMinMaxInfo)
            {
                ApplyViewportMinMaxInfo(lParam);
                handled = true;
            }
            else if (message == WmSizing)
            {
                ApplyViewportSizingRect((int)wParam, lParam);
                handled = true;
            }
            else if (message == WmSize)
            {
                CaptureViewportWindowState();
            }

            return IntPtr.Zero;
        }

        private void ApplyViewportMinMaxInfo(IntPtr lParam)
        {
            ViewportMinMaxInfo minMaxInfo = Marshal.PtrToStructure<ViewportMinMaxInfo>(lParam);
            (double scaleX, double scaleY) = GetViewportDpiScale();
            minMaxInfo.ptMinTrackSize.X = (int)Math.Ceiling(GetViewportMinimumWindowWidth() * scaleX);
            minMaxInfo.ptMinTrackSize.Y = (int)Math.Ceiling(GetViewportMinimumWindowHeight() * scaleY);
            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        private void ApplyViewportSizingRect(int sizingEdge, IntPtr lParam)
        {
            ViewportNativeRect rect = Marshal.PtrToStructure<ViewportNativeRect>(lParam);
            (double scaleX, double scaleY) = GetViewportDpiScale();
            double horizontalOffset = GetViewportWindowHorizontalOffset() * scaleX;
            double verticalOffset = GetViewportWindowVerticalOffset() * scaleY;
            double minWidth = GetViewportMinimumWindowWidth() * scaleX;
            double minHeight = GetViewportMinimumWindowHeight() * scaleY;

            double width = Math.Max(minWidth, rect.Right - rect.Left);
            double height = Math.Max(minHeight, rect.Bottom - rect.Top);
            bool preferWidth = sizingEdge is WmszLeft or WmszRight or WmszTopLeft or WmszTopRight
                or WmszBottomLeft or WmszBottomRight;

            if (preferWidth)
            {
                height = ((width - horizontalOffset) / ViewportContentAspectRatio) + verticalOffset;
                if (height < minHeight)
                {
                    height = minHeight;
                    width = ((height - verticalOffset) * ViewportContentAspectRatio) + horizontalOffset;
                }
            }
            else
            {
                width = ((height - verticalOffset) * ViewportContentAspectRatio) + horizontalOffset;
                if (width < minWidth)
                {
                    width = minWidth;
                    height = ((width - horizontalOffset) / ViewportContentAspectRatio) + verticalOffset;
                }
            }

            ResizeViewportNativeRect(ref rect, sizingEdge, (int)Math.Round(width), (int)Math.Round(height));
            Marshal.StructureToPtr(rect, lParam, true);
        }

        private static void ResizeViewportNativeRect(
            ref ViewportNativeRect rect,
            int sizingEdge,
            int width,
            int height)
        {
            switch (sizingEdge)
            {
                case WmszLeft:
                    rect.Left = rect.Right - width;
                    rect.Bottom = rect.Top + height;
                    break;
                case WmszRight:
                    rect.Right = rect.Left + width;
                    rect.Bottom = rect.Top + height;
                    break;
                case WmszTop:
                    rect.Top = rect.Bottom - height;
                    rect.Right = rect.Left + width;
                    break;
                case WmszTopLeft:
                    rect.Left = rect.Right - width;
                    rect.Top = rect.Bottom - height;
                    break;
                case WmszTopRight:
                    rect.Right = rect.Left + width;
                    rect.Top = rect.Bottom - height;
                    break;
                case WmszBottom:
                    rect.Bottom = rect.Top + height;
                    rect.Right = rect.Left + width;
                    break;
                case WmszBottomLeft:
                    rect.Left = rect.Right - width;
                    rect.Bottom = rect.Top + height;
                    break;
                case WmszBottomRight:
                default:
                    rect.Right = rect.Left + width;
                    rect.Bottom = rect.Top + height;
                    break;
            }
        }

        private static (double Width, double Height) NormalizeViewportWindowSize(
            double width,
            double height,
            bool preferWidth)
        {
            double minWidth = GetViewportWindowWidthFromContentWidth(AO2ViewportAssetResolver.ViewportWidth);
            double minHeight = GetViewportWindowHeightFromContentHeight(AO2ViewportAssetResolver.ViewportToolHeight);
            double clampedWidth = Math.Max(minWidth, width);
            double clampedHeight = Math.Max(minHeight, height);

            if (preferWidth)
            {
                double contentWidth = Math.Max(
                    AO2ViewportAssetResolver.ViewportWidth,
                    clampedWidth - GetViewportWindowHorizontalOffset());
                double contentHeight = contentWidth / ViewportContentAspectRatio;
                return (
                    GetViewportWindowWidthFromContentWidth(contentWidth),
                    GetViewportWindowHeightFromContentHeight(contentHeight));
            }

            double heightDrivenContentHeight = Math.Max(
                AO2ViewportAssetResolver.ViewportToolHeight,
                clampedHeight - GetViewportWindowVerticalOffset());
            double heightDrivenContentWidth = heightDrivenContentHeight * ViewportContentAspectRatio;
            return (
                GetViewportWindowWidthFromContentWidth(heightDrivenContentWidth),
                GetViewportWindowHeightFromContentHeight(heightDrivenContentHeight));
        }

        private void CaptureViewportWindowState()
        {
            if (viewportWindow == null || viewportWindow.WindowState != WindowState.Normal)
            {
                return;
            }

            SaveFile.Data.GMViewportWindowState = new ViewportWindowState
            {
                Width = Math.Max(GetViewportMinimumWindowWidth(), viewportWindow.Width),
                Height = Math.Max(GetViewportMinimumWindowHeight(), viewportWindow.Height),
                Left = viewportWindow.Left,
                Top = viewportWindow.Top,
                IsVisible = viewportWindow.IsVisible
            };
            SaveFile.Save();
        }

        private static double GetViewportWindowWidthFromContentWidth(double contentWidth)
        {
            return contentWidth + GetViewportWindowHorizontalOffset();
        }

        private static double GetViewportWindowHeightFromContentHeight(double contentHeight)
        {
            return contentHeight + GetViewportWindowVerticalOffset();
        }

        private static double GetViewportWindowHorizontalOffset()
        {
            return GenericOceanyaWindow.SharedFrameBorderThickness * 2;
        }

        private static double GetViewportWindowVerticalOffset()
        {
            return GenericOceanyaWindow.SharedHeaderHeight + (GenericOceanyaWindow.SharedFrameBorderThickness * 2);
        }

        private static double GetViewportMinimumWindowWidth()
        {
            return GetViewportWindowWidthFromContentWidth(AO2ViewportAssetResolver.ViewportWidth);
        }

        private static double GetViewportMinimumWindowHeight()
        {
            return GetViewportWindowHeightFromContentHeight(AO2ViewportAssetResolver.ViewportToolHeight);
        }

        private (double ScaleX, double ScaleY) GetViewportDpiScale()
        {
            if (viewportWindow == null)
            {
                return (1.0, 1.0);
            }

            PresentationSource? source = PresentationSource.FromVisual(viewportWindow);
            if (source?.CompositionTarget == null)
            {
                return (1.0, 1.0);
            }

            Matrix transform = source.CompositionTarget.TransformToDevice;
            return (transform.M11, transform.M22);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ViewportNativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ViewportMinMaxInfo
        {
            public ViewportNativePoint ptReserved;
            public ViewportNativePoint ptMaxSize;
            public ViewportNativePoint ptMaxPosition;
            public ViewportNativePoint ptMinTrackSize;
            public ViewportNativePoint ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ViewportNativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
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
                ClearAiClientState(clientToRemove);
                if (aiControllers.Remove(clientToRemove, out AOClientAgentController? controller))
                {
                    controller.Dispose();
                }
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
        

        private void btnCharacterFolderVisualizer_Click(object sender, RoutedEventArgs e)
        {
            CharacterFolderVisualizerWindow visualizerWindow = new CharacterFolderVisualizerWindow(
                OnAssetsRefreshedFromVisualizer,
                CanSetVisualizerCharacter,
                SetVisualizerCharacterInClient)
            {
                Owner = HostWindow
            };
            visualizerWindow.ShowDialog();
        }

        private bool CanSetVisualizerCharacter(FolderVisualizerItem item)
        {
            if (currentClient == null || !ICMessageSettingsControl.IsEnabled)
            {
                return false;
            }

            CharacterFolder? target = ResolveCharacterForVisualizerItem(item);
            if (target == null)
            {
                return false;
            }

            return currentClient.currentINI != target;
        }

        private void SetVisualizerCharacterInClient(FolderVisualizerItem item)
        {
            if (currentClient == null)
            {
                return;
            }

            CharacterFolder? target = ResolveCharacterForVisualizerItem(item);
            if (target == null)
            {
                return;
            }

            currentClient.SetCharacter(target);
            ICMessageSettingsControl.SetClient(currentClient);
            UpdateClientTooltip(currentClient);
        }

        private static CharacterFolder? ResolveCharacterForVisualizerItem(FolderVisualizerItem item)
        {
            string targetDirectory = item.DirectoryPath?.Trim() ?? string.Empty;
            string targetName = item.Name?.Trim() ?? string.Empty;

            CharacterFolder? byDirectory = CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.DirectoryPath, targetDirectory, StringComparison.OrdinalIgnoreCase));
            if (byDirectory != null)
            {
                return byDirectory;
            }

            return CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.Name, targetName, StringComparison.OrdinalIgnoreCase));
        }

        private void OnAssetsRefreshedFromVisualizer()
        {
            ICMessageSettingsControl.ReinitializeSettings();
            if (currentClient == null)
            {
                return;
            }

            SelectClient(currentClient);
        }

        private async Task RefreshCharacterAssetsAsync(
            string? characterName,
            bool refreshAllCharacters,
            bool refreshAllAssets)
        {
            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner == null)
            {
                return;
            }

            if (refreshAllAssets)
            {
                await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(owner);
            }
            else if (refreshAllCharacters)
            {
                await ClientAssetRefreshService.RefreshAllCharactersAsync(owner);
            }
            else if (!string.IsNullOrWhiteSpace(characterName))
            {
                await ClientAssetRefreshService.RefreshCharacterAsync(owner, characterName);
            }
            else
            {
                return;
            }

            RebindClientsToRefreshedCharacters();
            OnAssetsRefreshedFromVisualizer();
        }

        private async Task OpenCharacterInEditorAsync(string characterDirectory)
        {
            if (string.IsNullOrWhiteSpace(characterDirectory) || !System.IO.Directory.Exists(characterDirectory))
            {
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Character folder was not found on disk.",
                    "Open in Character Editor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            await WaitForm.ShowFormAsync("Opening character editor...", owner);
            AOCharacterFileCreatorWindow creator = new AOCharacterFileCreatorWindow();
            bool loadedSuccessfully;
            string errorMessage;
            try
            {
                WaitForm.SetSubtitle("Loading character folder...");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                loadedSuccessfully = creator.TryLoadCharacterFolderForEditing(characterDirectory, out errorMessage);
            }
            finally
            {
                WaitForm.CloseForm();
            }

            if (!loadedSuccessfully)
            {
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Could not open the selected character in the AO Character File Creator.\n"
                    + (string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error." : errorMessage),
                    "Open in Character Editor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Window editorWindow = OceanyaWindowManager.CreateWindow(creator);
            editorWindow.Owner = owner;
            _ = editorWindow.ShowDialog();
        }


        private void RebindClientsToRefreshedCharacters()
        {
            List<AOClient> clientsToRebind = clients.Values
                .Concat(singleInternalClient != null ? new[] { singleInternalClient } : Array.Empty<AOClient>())
                .Distinct()
                .ToList();

            foreach (AOClient client in clientsToRebind)
            {
                CharacterFolder? currentCharacter = client.currentINI;
                if (currentCharacter == null)
                {
                    continue;
                }

                CharacterFolder? refreshedCharacter = CharacterFolder.FullList.FirstOrDefault(character =>
                    string.Equals(character.DirectoryPath, currentCharacter.DirectoryPath, StringComparison.OrdinalIgnoreCase))
                    ?? CharacterFolder.FullList.FirstOrDefault(character =>
                        string.Equals(character.Name, currentCharacter.Name, StringComparison.OrdinalIgnoreCase));
                if (refreshedCharacter == null)
                {
                    continue;
                }

                string currentPosition = client.curPos;
                string currentEmoteDisplayId = client.currentEmote?.DisplayID ?? string.Empty;

                client.SetCharacter(refreshedCharacter);

                if (!string.IsNullOrWhiteSpace(currentEmoteDisplayId)
                    && refreshedCharacter.configINI.Emotions.Values.Any(emote =>
                        string.Equals(emote.DisplayID, currentEmoteDisplayId, StringComparison.OrdinalIgnoreCase)))
                {
                    client.SetEmote(currentEmoteDisplayId);
                }

                if (!string.IsNullOrWhiteSpace(currentPosition))
                {
                    client.SetPos(currentPosition);
                }
            }
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

            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (AOClient client in aiControllers.Keys.ToList())
            {
                ClearAiClientState(client);
            }

            foreach (AOClientAgentController controller in aiControllers.Values.ToList())
            {
                controller.Dispose();
            }
            aiControllers.Clear();

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
                Owner = HostWindow
            };
            window.ShowDialog();
        }

        private void THEDINGBUTTON_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayer.PlayEmbeddedSound("Resources/BellDing.mp3", AudioSettings.ScaleEmbeddedSfxVolume(0.25f));
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

            if (OceanyaTestMode.Current.IsEnabled && !networkClient.IsTransportConnected)
            {
                networkClient.ApplyAreaStateForTests(
                    selectedAreaItem.Name,
                    networkClient.AvailableAreaInfos.Select(areaInfo => areaInfo.Name));
                RefreshAreaNavigatorForCurrentClient();
                return;
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
                Owner = HostWindow
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

        internal static void ResetTestHooks()
        {
            testConnectClientAsyncOverride = null;
            testMessageBoxOverride = null;
        }
    }
}
