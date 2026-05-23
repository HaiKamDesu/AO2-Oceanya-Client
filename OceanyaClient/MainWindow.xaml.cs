using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
using System.Windows.Threading;
using AO2AIBot.Chat;
using AO2AIBot.Clients;
using AO2AIBot.Controller;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using NAudio.Wave;
using OceanyaClient.AdvancedFeatures;
using OceanyaClient.Components;
using OceanyaClient.Features.Chat;
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
        private static readonly IReadOnlyDictionary<string, string> EmptyMusicIndex =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public event Action? FinishedLoading;

        private readonly Dictionary<ToggleButton, AOClient> clients = new Dictionary<ToggleButton, AOClient>();
        private readonly List<AOClient> clientOrder = new List<AOClient>();
        private readonly Dictionary<AOClient, string> profileIniPuppetNames = new Dictionary<AOClient, string>();
        private readonly Dictionary<AOClient, AOClientAgentController> aiControllers = new Dictionary<AOClient, AOClientAgentController>();
        private readonly Dictionary<AOClient, List<PendingAiOriginResponse>> pendingAiOriginResponses = new Dictionary<AOClient, List<PendingAiOriginResponse>>();
        private readonly Dictionary<AOClient, bool> aiOriginResponseVisibility = new Dictionary<AOClient, bool>();
        private readonly IAiChatCompletionService aiCompletionService = new AiChatCompletionService();
        private readonly CallwordAudioNotifier callwordAudioNotifier = new CallwordAudioNotifier();
        private AOClient? currentClient;
        private AOClient? singleInternalClient;
        private AOClient? boundSingleClientProfile;
        private Window? viewportWindow;
        private Window? pictureInPictureViewportWindow;
        private HwndSource? hostWindowSource;
        private HwndSource? viewportWindowSource;
        private HwndSource? pictureInPictureViewportWindowSource;
        private DispatcherTimer? viewportTaskbarPreviewRefreshTimer;
        private DispatcherTimer? viewportAltTabFocusRedirectTimer;
        private DispatcherTimer? viewportAltTabExitPreparationTimer;
        private DispatcherTimer? viewportExternalForegroundTrackingTimer;
        private DispatcherTimer? viewportAltTabHeldReinjectTimer;
        private DateTime? viewportAltTabExitAltReleasedAt;
        private Window? settingsWindow;
        private SettingsWindow? settingsContent;
        private AO2ViewportWindowContent? viewportContent;
        private AO2ViewportWindowContent? pictureInPictureViewportContent;
        private IntPtr viewportAltTabKeyboardHook;
        private IntPtr lastViewportPreviewExternalForegroundHwnd;
        private LowLevelKeyboardProc? viewportAltTabKeyboardHookProc;
        private bool viewportAltTabHookPendingSuppressedQuickSwitch;
        private bool viewportAltTabHeldPreActivated;
        private bool isRestoringViewportWindow;
        private bool isRestoringPictureInPictureViewportWindow;
        private bool isSynchronizingWindowState;
        private bool isHostWindowHiddenByViewportMinimize;
        private bool isHostWindowHiddenByViewportAltTabExit;
        private bool isClosingPictureInPictureViewportWindow;
        private bool isUpdatingPictureInPictureViewportToggle;
        private bool isPictureInPictureViewportEnabled;
        private bool isMainWindowClosing;
        private bool viewportPreviewInputProxyActive;
        private bool viewportPreviewInputProxyFailureLogged;
        private bool pendingMainInputRestoreAfterActivation;
        private TextBox? viewportPreviewProxyVisualTarget;
        private double? lastViewportWindowWidth;
        private double? lastViewportWindowHeight;
        private double? lastPictureInPictureViewportWindowWidth;
        private double? lastPictureInPictureViewportWindowHeight;
        private double? pendingPictureInPictureViewportRestoreWidth;
        private double? pendingPictureInPictureViewportRestoreHeight;
        private double? pendingPictureInPictureViewportRestoreLeft;
        private double? pendingPictureInPictureViewportRestoreTop;
        private bool hasHookedHostWindowClosing;
        private bool cleanCloseInProgress;
        private bool hasAppliedMainWindowState;
        private bool hasAttemptedSnapshotRestore;
        private bool isRestoringSnapshot;
        private bool suppressSnapshotCapture;
        private bool suppressOocShownameTextChanged;
        private IInputElement? lastMainWindowFocusedElement;
        private readonly bool useSingleInternalClient = SaveFile.Data.UseSingleInternalClient;
        private readonly bool aiModeEnabled;
        private bool debug = false;
        private readonly HashSet<AOClient> areaListBootstrapCompletedClients = new HashSet<AOClient>();
        private readonly Brush areaFreeBrush = CreateFrozenBrush(Color.FromRgb(77, 77, 77));
        private readonly Brush areaLfpBrush = CreateFrozenBrush(Color.FromRgb(76, 112, 63));
        private readonly Brush areaCasingBrush = CreateFrozenBrush(Color.FromRgb(113, 92, 53));
        private readonly Brush areaRecessBrush = CreateFrozenBrush(Color.FromRgb(84, 84, 110));
        private readonly Brush areaRpBrush = CreateFrozenBrush(Color.FromRgb(108, 69, 116));
        private readonly Brush areaGamingBrush = CreateFrozenBrush(Color.FromRgb(58, 116, 116));
        private readonly Brush areaLockedBrush = CreateFrozenBrush(Color.FromRgb(106, 54, 54));
        private readonly Brush musicCategoryBrush = CreateFrozenBrush(Color.FromRgb(28, 36, 42));
        private readonly Brush musicFoundBrush = CreateFrozenBrush(Color.FromRgb(24, 46, 31));
        private readonly Brush musicMissingBrush = CreateFrozenBrush(Color.FromRgb(54, 27, 30));
        private readonly Brush musicCurrentBrush = CreateFrozenBrush(Color.FromRgb(35, 55, 45));
        private readonly Brush musicCommandBrush = CreateFrozenBrush(Color.FromRgb(18, 34, 62));
        private readonly AO2ViewportAudioManager mainMusicAudioManager = new AO2ViewportAudioManager();
        private readonly Ao2TextLogWriter ao2TextLogWriter = new Ao2TextLogWriter();
        private readonly Dictionary<string, string> displayMusicPathCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<MusicAssetEntry>? localMusicAssetsCache;
        private Dictionary<string, string>? localMusicIndex;
        private Task? localMusicAssetsScanTask;
        private CancellationTokenSource? areaNavigatorRefreshCts;
        private CancellationTokenSource? musicListRefreshCts;
        private int areaNavigatorRefreshVersion;
        private int musicListRefreshVersion;
        private bool isLoadingDreddOverlaySelection;
        private bool isDreddFeatureEnabled;
        private int MusicEffectFlags =>
            (SaveFile.Data.MusicFlagFadeIn ? 1 : 0) |
            (SaveFile.Data.MusicFlagFadeOut ? 2 : 0) |
            (SaveFile.Data.MusicFlagSync ? 4 : 0);
        private string currentMusicToken = string.Empty;
        private string currentMusicPlaylist = string.Empty;
        private const string DreddNoneOverlayName = "none";
        private const double ConnectionInfoBarHeight = 24;
        private const double MainWindowBodyHeight = 628;
        private const double DreddFeatureRowHeight = 30;
        private const double MainWindowHeight = MainWindowBodyHeight + ConnectionInfoBarHeight;
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
        private const int WmGetMinMaxInfo = 0x0024;
        private const int WmMouseActivate = 0x0021;
        private const int WmSizing = 0x0214;
        private const double MinimumViewportContentWidth = 160;
        private const double MinimumViewportContentHeight = 120;
        private const int WmDwmSendIconicThumbnail = 0x0323;
        private const int WmDwmSendIconicLivePreviewBitmap = 0x0326;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int VkTab = 0x09;
        private const int VkShift = 0x10;
        private const int VkMenu = 0x12;
        private const int WhKeyboardLl = 13;
        private const int LlkHfInjected = 0x10;
        private const uint KeyEventFKeyUp = 0x0002;
        private const uint GwOwner = 4;
        private const uint GwHwndNext = 2;
        private const int SwRestore = 9;
        private const int SwShownoactivate = 4;
        private const int MaNoActivate = 3;
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNoactivate = 0x0010;
        private const uint SwpShowwindow = 0x0040;

        private static Brush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
        private const int WmSize = 0x0005;
        private const int WmExitSizeMove = 0x0232;
        private const int WmszLeft = 1;
        private const int WmszRight = 2;
        private const int WmszTop = 3;
        private const int WmszTopLeft = 4;
        private const int WmszTopRight = 5;
        private const int WmszBottom = 6;
        private const int WmszBottomLeft = 7;
        private const int WmszBottomRight = 8;

        private enum SnapshotConflictDecision
        {
            Delete,
            SelectIniPuppet
        }

        private enum SnapshotPuppetConflictReason
        {
            MissingPuppet,
            NotOnServer,
            Taken,
            MissingLocal,
            ReservedBySnapshot
        }

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
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;
            AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(MainWindow_GotKeyboardFocus), true);
            AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(MainWindow_PreviewMouseDown), true);

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

                if (currentClient != null)
                {
                    currentClient.OOCShowname = showName;
                }

                if (useSingleInternalClient)
                {
                    if (currentClient != null)
                    {
                        ApplyProfileToSingleInternalClient(currentClient);
                    }
                }

                await networkClient.SendOOCMessage(showName, message);
                CaptureGmMultiClientSnapshot();
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
                    CustomConsole.Warning("IC send ignored because no GM client is selected.", category: CustomConsole.LogCategory.IC);
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
                    CustomConsole.Warning(
                        $"IC send ignored for \"{client.clientName}\" because no network client is available.",
                        category: CustomConsole.LogCategory.IC);
                    return;
                }

                if (useSingleInternalClient)
                {
                    CustomConsole.Debug(
                        $"Applying profile before IC send. profile=\"{client.clientName}\" profileCharacter=\"{client.currentINI?.Name ?? "(null)"}\" profileEmote=\"{client.currentEmote?.DisplayID ?? "(null)"}\" networkIniPuppet=\"{networkClient.iniPuppetName}\" networkIniPuppetId={networkClient.iniPuppetID}",
                        CustomConsole.LogCategory.IC);
                    await EnsureSingleInternalClientProfileSelectionAsync(client);
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
                networkClient.OnICMessageReceived += OnICMessageReceivedHandler;
                CustomConsole.Info(
                    $"IC send requested. profile=\"{client.clientName}\" network=\"{networkClient.clientName}\" connected={networkClient.IsTransportConnected} iniPuppet=\"{networkClient.iniPuppetName}\" iniPuppetId={networkClient.iniPuppetID} character=\"{networkClient.currentINI?.Name ?? "(null)"}\" emote=\"{networkClient.currentEmote?.DisplayID ?? "(null)"}\" messageLength={sendMessage.Length}",
                    CustomConsole.LogCategory.IC);
                try
                {
                    await networkClient.SendICMessage(sendMessage);
                    SyncPairSendStateFromNetworkClient(client, networkClient);
                    CaptureGmMultiClientSnapshot();
                }
                catch (Exception ex)
                {
                    CustomConsole.Error("IC send failed before packet write completed.", ex, CustomConsole.LogCategory.IC);
                    networkClient.OnICMessageReceived -= OnICMessageReceivedHandler;
                    throw;
                }
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
            ICMessageSettingsControl.OnRefreshBackgroundRequested += async backgroundName =>
            {
                await RefreshBackgroundAssetsAsync(backgroundName);
            };
            ICMessageSettingsControl.OnRefreshAllAssetsRequested += async () =>
            {
                await RefreshCharacterAssetsAsync(null, refreshAllCharacters: false, refreshAllAssets: true);
            };
            ICMessageSettingsControl.OnRefreshAllCharactersRequested += async () =>
            {
                await RefreshCharacterAssetsAsync(null, refreshAllCharacters: true, refreshAllAssets: false);
            };
            ICMessageSettingsControl.OnNewCharacterFolderRequested += async () =>
            {
                await OpenNewCharacterInEditorAsync();
            };
            ICMessageSettingsControl.OnOpenInCharacterEditorRequested += async characterDirectory =>
            {
                await OpenCharacterInEditorAsync(characterDirectory);
            };
            ICMessageSettingsControl.OnDuplicateInCharacterEditorRequested += async characterDirectory =>
            {
                await DuplicateCharacterInEditorAsync(characterDirectory);
            };
            ICMessageSettingsControl.OnOpenInCharacterEmoteVisualizerRequested += characterDirectory =>
            {
                OpenCharacterInEmoteVisualizer(characterDirectory);
            };
            ICMessageSettingsControl.OnOpenInCharacterFolderVisualizerRequested += characterDirectory =>
            {
                OpenCharacterInFolderVisualizer(characterDirectory);
            };
            ICMessageSettingsControl.OnDeleteCharacterFolderRequested += DeleteCharacterFolderFromContextAsync;
            ICMessageSettingsControl.OnPositionConfirmed += async (profileClient, position) =>
            {
                AOClient? networkClient = GetTargetClientForNetwork(profileClient);
                if (networkClient == null)
                {
                    return;
                }

                try
                {
                    await networkClient.SetServerPositionAsync(position);
                    if (useSingleInternalClient && !ReferenceEquals(networkClient, profileClient))
                    {
                        profileClient.SetPos(networkClient.curPos, true);
                    }
                }
                catch (Exception ex)
                {
                    CustomConsole.Error(
                        $"Failed to set server position \"{position}\".",
                        ex,
                        CustomConsole.LogCategory.Network);
                }
            };
            ICMessageSettingsControl.OnClientStateChanged += CaptureGmMultiClientSnapshot;
            ICMessageSettingsControl.PairingClientProvider = () =>
                useSingleInternalClient
                    ? Array.Empty<AOClient>()
                    : clientOrder.ToList();
            ICMessageSettingsControl.PairingNetworkClientProvider = profileClient =>
            {
                if (useSingleInternalClient)
                {
                    ApplyProfileToSingleInternalClient(profileClient);
                }

                return GetTargetClientForNetwork(profileClient);
            };

            OOCLogControl.txtOOCShowname.Text = SaveFile.Data.OOCName;
            OOCLogControl.txtOOCShowname.TextChanged += (_, _) => HandleOocShownameTextChanged();
            ICLogControl.FindTargetsProvider = GetCurrentLogFindTargets;
            OOCLogControl.FindTargetsProvider = GetCurrentLogFindTargets;
            chkPosOnIniSwap.IsChecked = SaveFile.Data.SwitchPosOnIniSwap;
            chkSticky.IsChecked = SaveFile.Data.StickyEffect;
            chkInvertLog.IsChecked = SaveFile.Data.InvertICLog;
            if (OceanyaTestMode.Current.IsEnabled)
            {
                chkSticky.Visibility = Visibility.Visible;
                chkPosOnIniSwap.Visibility = Visibility.Visible;
            }
            ApplySavedPopupSettings();
            ApplySavedClientSettingsToRuntime();
            InitializeDreddFeatureUi();

            btnDebug.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
            RefreshAreaNavigatorForCurrentClient();
            RefreshMusicListForCurrentClient();
            UpdateConnectionInfoBar();
        }

        /// <inheritdoc/>
        public override string HeaderText => "OCEANYA ONLINE";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        private void HandleOocShownameTextChanged()
        {
            if (suppressOocShownameTextChanged || currentClient == null)
            {
                return;
            }

            currentClient.OOCShowname = OOCLogControl.txtOOCShowname.Text?.Trim() ?? string.Empty;
            CaptureGmMultiClientSnapshot();
        }

        private void SetOocShownameTextForCurrentClient(string? showname)
        {
            suppressOocShownameTextChanged = true;
            try
            {
                OOCLogControl.txtOOCShowname.Text = showname ?? string.Empty;
            }
            finally
            {
                suppressOocShownameTextChanged = false;
            }
        }

        private void MainWindow_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is DependencyObject dependencyObject
                && IsAncestorOf(dependencyObject)
                && viewportContent?.IsAncestorOf(dependencyObject) != true)
            {
                lastMainWindowFocusedElement = e.NewFocus;
                if (IsViewportUsingWindowsPreview())
                {
                    if (e.NewFocus is TextBox textBox && IsProxyEligibleMainInput(textBox))
                    {
                        SetViewportPreviewInputProxyTarget(textBox, "main keyboard focus target");
                    }

                    Dispatcher.BeginInvoke(
                        new Action(() => EnsureViewportIsForegroundShellRepresentative("main keyboard focus returned to viewport shell")),
                        DispatcherPriority.Input);
                }
            }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.OriginalSource is not DependencyObject source)
                {
                    return;
                }

                TextBox? textBox = FindAncestor<TextBox>(source);
                if (IsProxyEligibleMainInput(textBox))
                {
                    SetViewportPreviewInputProxyTarget(textBox, "main mouse target");
                    Dispatcher.BeginInvoke(
                        new Action(() => EnsureViewportIsForegroundShellRepresentative("main mouse target returned to viewport shell")),
                        DispatcherPriority.Input);
                    LogViewportPreviewState("main mouse target=" + textBox!.Name);
                }
                else if (pendingMainInputRestoreAfterActivation
                         && lastMainWindowFocusedElement is TextBox lastTextBox
                         && IsProxyEligibleMainInput(lastTextBox))
                {
                    Dispatcher.BeginInvoke(
                        new Action(() => RestoreMainInputFocusTarget(lastTextBox)),
                        DispatcherPriority.ApplicationIdle);
                }
                else if (IsViewportUsingWindowsPreview()
                         && lastMainWindowFocusedElement is TextBox proxyTextBox
                         && IsProxyEligibleMainInput(proxyTextBox))
                {
                    viewportPreviewInputProxyActive = true;
                    SetViewportPreviewInputProxyTarget(proxyTextBox, "main mouse target retained proxy");
                    Dispatcher.BeginInvoke(
                        new Action(() => EnsureViewportIsForegroundShellRepresentative(
                            "main non-input mouse target returned to viewport shell",
                            allowExternalForegroundOverride: true)),
                        DispatcherPriority.Input);
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Debug(
                    "[VPT-ALT] main mouse target tracking skipped: " + ex.Message,
                    CustomConsole.LogCategory.Viewport);
            }
        }

        private void MarkViewportAltTabKeyIfNeeded(int message, IntPtr wParam)
        {
            // Attempts 19-22 used WndProc/timer based Alt-Tab handoff.  The input-proxy
            // architecture intentionally leaves native Alt-Tab alone so Windows starts from
            // the real foreground shell HWND.
            if (!IsViewportUsingWindowsPreview()
                || (message != WmKeyDown && message != WmSysKeyDown))
            {
                return;
            }

            int virtualKey = wParam.ToInt32();
            if (virtualKey == VkMenu || (virtualKey == VkTab && IsAltKeyDown()))
            {
                EnsureViewportIsForegroundShellRepresentative("alt-tab key preparation");
            }
        }

        private bool TryHandleMainWindowAltTabQuickSwitch(int message, IntPtr wParam)
        {
            return false;
        }

        private bool TryActivatePreviousExternalTopLevelWindow()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hostHwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = viewportWindow == null ? IntPtr.Zero : new WindowInteropHelper(viewportWindow).Handle;
            int currentProcessId = Process.GetCurrentProcess().Id;

            for (IntPtr hwnd = GetTopWindow(IntPtr.Zero);
                 hwnd != IntPtr.Zero;
                 hwnd = GetWindow(hwnd, GwHwndNext))
            {
                if (!IsEligibleAltTabSwitchTarget(hwnd, hostHwnd, viewportHwnd, currentProcessId))
                {
                    continue;
                }

                return RestoreAndActivateWindow(hwnd);
            }

            return false;
        }

        private static bool RestoreAndActivateWindow(IntPtr hwnd)
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SwRestore);
            }

            return SetForegroundWindow(hwnd);
        }

        private static bool IsEligibleAltTabSwitchTarget(
            IntPtr hwnd,
            IntPtr hostHwnd,
            IntPtr viewportHwnd,
            int currentProcessId)
        {
            if (hwnd == IntPtr.Zero || hwnd == hostHwnd || hwnd == viewportHwnd || !IsWindowVisible(hwnd))
            {
                return false;
            }

            _ = GetWindowThreadProcessId(hwnd, out int processId);
            if (processId == currentProcessId)
            {
                return false;
            }

            if (IsWindowCloaked(hwnd))
            {
                return false;
            }

            const int GWL_EXSTYLE = -20;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_APPWINDOW = 0x00040000;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
            bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
            if (isToolWindow && !isAppWindow)
            {
                return false;
            }

            IntPtr owner = GetWindow(hwnd, GwOwner);
            return owner == IntPtr.Zero || isAppWindow;
        }

        private static bool IsWindowCloaked(IntPtr hwnd)
        {
            const int DWMWA_CLOAKED = 14;
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, Marshal.SizeOf<int>()) == 0
                && cloaked != 0;
        }

        private void HideHostWindowForViewportAltTabExit()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (hostWindow?.IsVisible == true)
            {
                isHostWindowHiddenByViewportAltTabExit = true;
                hostWindow.Hide();
            }
        }

        private void FocusMainWindowFromViewportPreview()
        {
            if (!IsViewportUsingWindowsPreview())
            {
                viewportWindow?.Activate();
                return;
            }

            viewportPreviewInputProxyActive = true;
            UpdateViewportPreviewProxyVisual("input proxy active");
            EnsureViewportIsForegroundShellRepresentative("main focus restore skipped; viewport shell retained");
            LogViewportPreviewState("main focus restore skipped; input proxy active");
        }

        private static void SetWindowOwner(Window window, IntPtr ownerHwnd)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            const int GWLP_HWNDPARENT = -8;
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, ownerHwnd);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HookHostWindowClosing();
            ApplySavedMainWindowState();
            if (hasRaisedFinishedLoading)
            {
                return;
            }

            hasRaisedFinishedLoading = true;
            StartupTimingLogger.Log("main_window_loaded");
            MarkAutomationReady();
            EnsureLocalMusicAssetsScanStarted();
            if (!OceanyaTestMode.Current.DisableViewportWindowPersistence
                && SaveFile.Data.GMViewportWindowState?.IsVisible == true)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OpenViewportWindow();
                }));
            }

            if (!hasAttemptedSnapshotRestore)
            {
                hasAttemptedSnapshotRestore = true;
                _ = RestoreGmMultiClientSnapshotAsync();
            }

            FinishedLoading?.Invoke();
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            hostWindowSource = HwndSource.FromHwnd(hwnd);
            hostWindowSource?.AddHook(MainWindowHost_WndProc);
            ApplyViewportTaskbarPriority();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            StopViewportTaskbarPreviewRefreshTimer();
            StopViewportAltTabFocusRedirectTimer();
            StopViewportAltTabExitPreparationTimer(restoreNoActivate: false);
            StopViewportExternalForegroundTrackingTimer();
            UninstallViewportAltTabKeyboardHook();
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ViewportThumbnailCompositor.Deactivate(hwnd);
            }

            hostWindowSource?.RemoveHook(MainWindowHost_WndProc);
            hostWindowSource = null;
            pictureInPictureViewportWindowSource?.RemoveHook(PictureInPictureViewportWindow_WndProc);
            pictureInPictureViewportWindowSource = null;
        }

        private IntPtr MainWindowHost_WndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (!IsViewportUsingWindowsPreview())
            {
                return IntPtr.Zero;
            }

            if (TryHandleMainWindowAltTabQuickSwitch(message, wParam))
            {
                handled = true;
                return IntPtr.Zero;
            }

            MarkViewportAltTabKeyIfNeeded(message, wParam);

            if (message == WmMouseActivate)
            {
                RestoreMainWindowVisualForViewportReturn(forceForegroundRepresentative: true);
                LogViewportPreviewState("main WM_MOUSEACTIVATE no-activate; viewport shell foreground requested");
                handled = true;
                return new IntPtr(MaNoActivate);
            }

            return IntPtr.Zero;
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
            hostWindow.Activated += HostWindow_Activated;
            hostWindow.Deactivated += HostWindow_Deactivated;
            hostWindow.Closing += async (_, e) =>
            {
                if (cleanCloseInProgress)
                {
                    return;
                }

                e.Cancel = true;
                cleanCloseInProgress = true;
                IsEnabled = false;
                isMainWindowClosing = true;
                CaptureViewportWindowState();
                CapturePictureInPictureViewportWindowState("shutdown");
                viewportContent?.AttachClient(null, null);
                pictureInPictureViewportContent?.AttachClient(null, null);
                viewportWindow?.Close();
                pictureInPictureViewportWindow?.Close();
                areaNavigatorRefreshCts?.Cancel();
                musicListRefreshCts?.Cancel();
                callwordAudioNotifier.Dispose();
                ao2TextLogWriter.ResetSession();
                mainMusicAudioManager.Dispose();
                await DisconnectAllClientsForShutdownAsync();
                IsEnabled = true;
                try
                {
                    hostWindow.Close();
                }
                catch (InvalidOperationException)
                {
                    // Window was already closed or being destroyed during async cleanup.
                }
            };
        }

        private void HostWindow_Activated(object? sender, EventArgs e)
        {
            RestoreMainInputFocusAfterExternalActivation();
            EnsureViewportVisibleWithMainWindow();
        }

        private void HostWindow_Deactivated(object? sender, EventArgs e)
        {
        }

        private void RestoreMainInputFocusAfterExternalActivation()
        {
            if (lastMainWindowFocusedElement is not TextBox textBox || !IsProxyEligibleMainInput(textBox))
            {
                return;
            }

            if (IsViewportUsingWindowsPreview())
            {
                pendingMainInputRestoreAfterActivation = false;
                viewportPreviewInputProxyActive = true;
                SetViewportPreviewInputProxyTarget(textBox, "main window activated in viewport preview mode");
                Dispatcher.BeginInvoke(
                    new Action(() => EnsureViewportIsForegroundShellRepresentative(
                        "main activation returned to viewport shell for input proxy",
                        allowExternalForegroundOverride: true)),
                    DispatcherPriority.Input);
                return;
            }

            pendingMainInputRestoreAfterActivation = true;
            SetViewportPreviewProxyVisualTarget(null, "main window activated");
            Dispatcher.BeginInvoke(
                new Action(() => RestoreMainInputFocusTarget(textBox)),
                DispatcherPriority.ContextIdle);
        }

        private void RestoreMainInputFocusTarget(TextBox textBox)
        {
            if (!textBox.IsVisible || !textBox.IsEnabled || !IsProxyEligibleMainInput(textBox))
            {
                pendingMainInputRestoreAfterActivation = false;
                return;
            }

            textBox.Focus();
            Keyboard.Focus(textBox);
            pendingMainInputRestoreAfterActivation = false;
        }

        private async Task DisconnectAllClientsForShutdownAsync()
        {
            List<AOClient> clientsToDisconnect = clientOrder
                .Where(client => clients.Values.Contains(client))
                .Concat(singleInternalClient != null ? new[] { singleInternalClient } : Array.Empty<AOClient>())
                .Distinct()
                .ToList();

            foreach (AOClient client in clientsToDisconnect)
            {
                try
                {
                    await client.CloseForShutdownAsync();
                }
                catch (Exception ex)
                {
                    CustomConsole.Error(
                        $"Failed to cleanly disconnect client \"{client.clientName}\".",
                        ex,
                        CustomConsole.LogCategory.Network);
                }
            }
        }

        private void ApplySavedMainWindowState()
        {
            if (hasAppliedMainWindowState)
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            VisualizerWindowState? state = SaveFile.Data.GMMainWindowState;
            if (hostWindow == null || state == null)
            {
                return;
            }

            hasAppliedMainWindowState = true;
            if (state.Left.HasValue
                && state.Top.HasValue
                && IsFinite(state.Left.Value)
                && IsFinite(state.Top.Value))
            {
                hostWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                Rect virtualBounds = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
                double width = Math.Max(hostWindow.MinWidth, hostWindow.Width);
                double height = Math.Max(hostWindow.MinHeight, hostWindow.Height);
                hostWindow.Left = Clamp(state.Left.Value, virtualBounds.Left, virtualBounds.Right - width);
                hostWindow.Top = Clamp(state.Top.Value, virtualBounds.Top, virtualBounds.Bottom - height);
            }

            hostWindow.LocationChanged += (_, _) => CaptureMainWindowState();
        }

        private void CaptureMainWindowState()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (hostWindow == null || hostWindow.WindowState != WindowState.Normal)
            {
                return;
            }

            SaveFile.Data.GMMainWindowState = new VisualizerWindowState
            {
                Width = Math.Max(510, hostWindow.Width),
                Height = Math.Max(GetMainWindowTargetHeight(isDreddFeatureEnabled), hostWindow.Height),
                Left = hostWindow.Left,
                Top = hostWindow.Top,
                IsMaximized = false
            };
            SaveFile.Save();
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

                CaptureGmMultiClientSnapshot();
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

        private IReadOnlyList<ILogFindTarget> GetCurrentLogFindTargets()
        {
            return new ILogFindTarget[] { ICLogControl, OOCLogControl };
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
            AddLoggedIcMessageWithContext(profileClient, showName, message, isSentFromSelf, textColor, null);
        }

        private void AddLoggedIcMessageWithContext(
            AOClient profileClient,
            string showName,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor,
            ICMessage? sourceMessage = null)
        {
            if (!isSentFromSelf)
            {
                if (callwordAudioNotifier.TryNotify(new CallwordNotificationContext(message, showName, sourceMessage)))
                {
                    FlashTaskbar();
                }
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
                nameLinks: nameLinks,
                useAo2Formatting: true);
            ao2TextLogWriter.AppendIcMessage(sourceMessage, showName, ICMessage.StripFormattingCodes(message));
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
            ao2TextLogWriter.AppendIcAction(showName, action, message);
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
                if (callwordAudioNotifier.TryNotify(new CallwordNotificationContext(message, showName, null)))
                {
                    FlashTaskbar();
                }
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
            ao2TextLogWriter.AppendServerMessage(showName, message);
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
                    SetOocShownameTextForCurrentClient(profileClient.OOCShowname);
                    RefreshAreaNavigatorForCurrentClient();
                    RefreshMusicListForCurrentClient();
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
                UpdateConnectionInfoBar();
                return;
            }

            btnAreaNavigator.IsEnabled = true;
            btnGoToArea.IsEnabled = true;

            string visibleArea = string.IsNullOrWhiteSpace(networkClient.CurrentArea) ? "Unknown" : networkClient.CurrentArea;
            txtCurrentArea.Text = $"Current: {visibleArea}";
            UpdateConnectionInfoBar();

            ScheduleAreaNavigatorRefresh(networkClient.AvailableAreaInfos.ToList(), networkClient.CurrentArea);
        }

        private void ScheduleAreaNavigatorRefresh(List<AreaInfo> areaInfos, string currentArea)
        {
            areaNavigatorRefreshCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            areaNavigatorRefreshCts = cts;
            int version = ++areaNavigatorRefreshVersion;
            string visibleArea = currentArea?.Trim() ?? string.Empty;
            var totalSw = Stopwatch.StartNew();

            Task.Run(() =>
            {
                var buildSw = Stopwatch.StartNew();
                List<AreaNavigatorListItem> areaItems = areaInfos
                    .Select(CreateAreaNavigatorListItem)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(visibleArea)
                    && !areaItems.Any(item => string.Equals(item.Name, visibleArea, StringComparison.OrdinalIgnoreCase)))
                {
                    areaItems.Insert(0, CreateCurrentAreaListItem(visibleArea));
                }

                buildSw.Stop();
                return (Items: areaItems, BuildMs: buildSw.ElapsedMilliseconds);
            }, cts.Token).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested || version != areaNavigatorRefreshVersion)
                    {
                        cts.Dispose();
                        return;
                    }

                    areaNavigatorRefreshCts = null;
                    cts.Dispose();
                    if (task.IsCanceled)
                    {
                        return;
                    }

                    if (task.IsFaulted)
                    {
                        CustomConsole.Warning(
                            "Area navigator async rebuild failed.",
                            task.Exception,
                            CustomConsole.LogCategory.AreaVisualizer);
                        return;
                    }

                    totalSw.Stop();
                    lstAreas.ItemsSource = task.Result.Items;
                    CustomConsole.Debug(
                        $"Area navigator rebuilt {task.Result.Items.Count} rows off-thread in {task.Result.BuildMs}ms; total={totalSw.ElapsedMilliseconds}ms",
                        CustomConsole.LogCategory.AreaVisualizer);
                }, DispatcherPriority.Background);
            }, TaskScheduler.Default);
        }

        private void RefreshMusicListForCurrentClient()
        {
            AOClient? profileClient = currentClient;
            AOClient? networkClient = profileClient == null ? null : GetTargetClientForNetwork(profileClient);

            if (networkClient == null)
            {
                btnMusicList.IsEnabled = false;
                treeMusic.ItemsSource = null;
                btnStopMusic.IsEnabled = false;
                btnRefreshMusicList.IsEnabled = false;
                UpdateCurrentMusicDisplay(null);
                return;
            }

            btnMusicList.IsEnabled = true;
            btnStopMusic.IsEnabled = true;
            btnRefreshMusicList.IsEnabled = true;
            EnsureLocalMusicAssetsScanStarted();
            string filter = txtMusicSearch?.Text?.Trim() ?? string.Empty;
            ScheduleMusicListRefresh(CreateMusicListBuildSnapshot(networkClient, filter));
            UpdateCurrentMusicDisplay(networkClient);
        }

        private MusicListBuildSnapshot CreateMusicListBuildSnapshot(AOClient networkClient, string filter)
        {
            return new MusicListBuildSnapshot(
                networkClient.AvailableMusic.ToList(),
                filter?.Trim() ?? string.Empty,
                SaveFile.Data.MusicListShowAssetPaths,
                GetMusicCollapsedCategoryKeys(),
                new Dictionary<string, int>(SaveFile.Data.FrequentlyUsedMusic, StringComparer.OrdinalIgnoreCase),
                SaveFile.Data.CustomMusicCommands.ToList(),
                new Dictionary<string, string>(SaveFile.Data.MusicCustomNames, StringComparer.OrdinalIgnoreCase),
                SaveFile.Data.MusicSectionOrder.ToList(),
                currentMusicToken,
                localMusicAssetsCache ?? Array.Empty<MusicAssetEntry>(),
                localMusicIndex ?? EmptyMusicIndex);
        }

        private void ScheduleMusicListRefresh(MusicListBuildSnapshot snapshot)
        {
            musicListRefreshCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            musicListRefreshCts = cts;
            int version = ++musicListRefreshVersion;
            var totalSw = Stopwatch.StartNew();

            Task.Run(() =>
            {
                var buildSw = Stopwatch.StartNew();
                List<MusicListItem> items = BuildMusicListItems(snapshot);
                buildSw.Stop();
                int flattenedCount = FlattenMusicItems(items).Count();
                return (Items: items, BuildMs: buildSw.ElapsedMilliseconds, FlattenedCount: flattenedCount);
            }, cts.Token).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested || version != musicListRefreshVersion)
                    {
                        cts.Dispose();
                        return;
                    }

                    musicListRefreshCts = null;
                    cts.Dispose();
                    if (task.IsCanceled)
                    {
                        return;
                    }

                    if (task.IsFaulted)
                    {
                        CustomConsole.Warning(
                            "Music list async rebuild failed.",
                            task.Exception,
                            CustomConsole.LogCategory.MusicList);
                        return;
                    }

                    totalSw.Stop();
                    treeMusic.ItemsSource = task.Result.Items;
                    CustomConsole.Debug(
                        $"Music list rebuilt {task.Result.FlattenedCount} rows off-thread in {task.Result.BuildMs}ms; total={totalSw.ElapsedMilliseconds}ms; server={snapshot.MusicEntries.Count}; local={snapshot.LocalMusicAssets.Count}; filter=\"{snapshot.Filter}\"",
                        CustomConsole.LogCategory.MusicList);
                }, DispatcherPriority.Background);
            }, TaskScheduler.Default);
        }

        private List<MusicListItem> BuildMusicListItems(MusicListBuildSnapshot snapshot)
        {
            List<MusicListItem> items = new List<MusicListItem>();

            MusicListItem frequentRoot = CreateMusicCategoryItem("FREQUENTLY USED", "frequent", snapshot.CollapsedKeys);
            frequentRoot.IsRootCategory = true;
            MusicListItem customRoot = CreateMusicCategoryItem("CUSTOM COMMANDS", "custom", snapshot.CollapsedKeys);
            customRoot.IsRootCategory = true;
            MusicListItem serverRoot = CreateMusicCategoryItem("SERVER LIST", "server", snapshot.CollapsedKeys);
            serverRoot.IsRootCategory = true;
            MusicListItem localRoot = CreateMusicCategoryItem("LOCAL FILES", "local", snapshot.CollapsedKeys);
            localRoot.IsRootCategory = true;

            BuildFrequentlyUsedMusicItems(frequentRoot, snapshot);
            BuildCustomCommandItems(customRoot, snapshot);
            BuildServerMusicItems(serverRoot, snapshot);
            BuildLocalMusicItems(localRoot, snapshot);

            PropagateRedCategoryState(frequentRoot);
            PropagateRedCategoryState(customRoot);
            PropagateRedCategoryState(serverRoot);
            PropagateRedCategoryState(localRoot);

            Dictionary<string, MusicListItem> rootsByName = new Dictionary<string, MusicListItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["FREQUENTLY USED"] = frequentRoot,
                ["CUSTOM COMMANDS"] = customRoot,
                ["SERVER LIST"] = serverRoot,
                ["LOCAL FILES"] = localRoot,
            };

            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sectionName in snapshot.SectionOrder)
            {
                if (!rootsByName.TryGetValue(sectionName, out MusicListItem? root)) continue;
                if (ShouldIncludeMusicCategory(root, snapshot.Filter))
                    items.Add(root);
                added.Add(sectionName);
            }

            // Safety net: add any root not present in the saved order.
            foreach (KeyValuePair<string, MusicListItem> pair in rootsByName)
            {
                if (!added.Contains(pair.Key) && ShouldIncludeMusicCategory(pair.Value, snapshot.Filter))
                    items.Add(pair.Value);
            }

            return items;
        }

        private void BuildFrequentlyUsedMusicItems(
            MusicListItem frequentRoot,
            MusicListBuildSnapshot snapshot)
        {
            System.Collections.Generic.Dictionary<string, CustomMusicCommand> customById =
                snapshot.CustomCommands.ToDictionary(c => c.Id, StringComparer.Ordinal);

            var sorted = snapshot.FrequentlyUsedMusic
                .Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, NaturalStringComparer.Instance);

            foreach (KeyValuePair<string, int> pair in sorted)
            {
                string token = pair.Key;
                int count = pair.Value;
                string tooltip = $"Played {count} time{(count == 1 ? "" : "s")}";

                if (customById.TryGetValue(token, out CustomMusicCommand? cmd))
                {
                    string displayName = "[C] " + GetMusicItemCustomName(token, cmd.Name, snapshot);
                    string assetPath = cmd.Command;
                    if (!string.IsNullOrWhiteSpace(snapshot.Filter)
                        && !displayName.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                        && !assetPath.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    frequentRoot.Children.Add(new MusicListItem
                    {
                        DisplayName = displayName,
                        Token = token,
                        PlayToken = cmd.Command,
                        AssetPath = assetPath,
                        Playlist = "FREQUENTLY USED",
                        IsCategory = false,
                        IsPlayable = true,
                        IsCustomCommand = true,
                        CustomCommandId = token,
                        Tooltip = tooltip,
                        RowBackground = musicCommandBrush,
                        TitleBrush = Brushes.Gainsboro,
                        Padding = new Thickness(7, 4, 7, 4),
                        AssetPathVisibility = snapshot.ShowAssetPaths ? Visibility.Visible : Visibility.Collapsed,
                    });
                    continue;
                }

                string musicDisplayName = GetMusicItemCustomName(token, GetMusicDisplayName(token), snapshot);
                bool localFileExists = !string.IsNullOrWhiteSpace(ResolveMusicPathForBuild(token, snapshot));
                string musicAssetPath = localFileExists
                    ? ResolveMusicDisplayPathForBuild(token, snapshot)
                    : GetExpectedMusicDisplayPath(token);

                if (!string.IsNullOrWhiteSpace(snapshot.Filter)
                    && !musicDisplayName.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    && !token.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    && !musicAssetPath.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isCurrent = MusicTokenMatchesCurrent(token, snapshot.CurrentMusicToken);
                if (!localFileExists)
                {
                    tooltip += "\nNot found in local AO2 installation";
                }

                frequentRoot.Children.Add(new MusicListItem
                {
                    DisplayName = musicDisplayName,
                    Token = token,
                    PlayToken = token,
                    AssetPath = musicAssetPath,
                    Playlist = "FREQUENTLY USED",
                    IsCategory = false,
                    IsPlayable = true,
                    Tooltip = tooltip,
                    RowBackground = isCurrent ? musicCurrentBrush : localFileExists ? musicFoundBrush : musicMissingBrush,
                    TitleBrush = isCurrent ? Brushes.LightGreen : Brushes.Gainsboro,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Padding = new Thickness(7, 4, 7, 4),
                    AssetPathVisibility = snapshot.ShowAssetPaths ? Visibility.Visible : Visibility.Collapsed,
                });
            }
        }

        private void BuildCustomCommandItems(
            MusicListItem customRoot,
            MusicListBuildSnapshot snapshot)
        {
            foreach (CustomMusicCommand cmd in snapshot.CustomCommands)
            {
                string displayName = GetMusicItemCustomName(cmd.Id, cmd.Name, snapshot);
                string assetPath = cmd.Command;

                if (!string.IsNullOrWhiteSpace(snapshot.Filter)
                    && !displayName.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    && !assetPath.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MusicListItem parent = EnsureCustomCommandCategory(customRoot, cmd.CategoryPath, snapshot.CollapsedKeys);
                parent.Children.Add(new MusicListItem
                {
                    DisplayName = displayName,
                    Token = cmd.Id,
                    PlayToken = cmd.Command,
                    AssetPath = assetPath,
                    Playlist = "CUSTOM COMMANDS",
                    IsCategory = false,
                    IsPlayable = true,
                    IsCustomCommand = true,
                    CustomCommandId = cmd.Id,
                    Tooltip = cmd.Command,
                    RowBackground = musicCommandBrush,
                    TitleBrush = Brushes.Gainsboro,
                    Padding = new Thickness(parent == customRoot ? 7 : 18, 4, 7, 4),
                    AssetPathVisibility = snapshot.ShowAssetPaths ? Visibility.Visible : Visibility.Collapsed,
                });
            }

            SortMusicChildrenNatural(customRoot);
        }

        private MusicListItem EnsureCustomCommandCategory(
            MusicListItem root,
            string categoryPath,
            HashSet<string> collapsedKeys)
        {
            if (string.IsNullOrWhiteSpace(categoryPath))
            {
                return root;
            }

            MusicListItem current = root;
            string keyPrefix = "custom";
            foreach (string part in categoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                keyPrefix = CreateMusicCategoryKey(keyPrefix, part);
                MusicListItem? existing = current.Children
                    .FirstOrDefault(c => c.IsCategory
                        && string.Equals(c.DisplayName, part, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = CreateMusicCategoryItem(part, keyPrefix, collapsedKeys);
                    current.Children.Add(existing);
                }

                current = existing;
            }

            return current;
        }

        private void BuildServerMusicItems(
            MusicListItem serverRoot,
            MusicListBuildSnapshot snapshot)
        {
            MusicListItem? currentCategory = null;
            List<MusicListItem> uncategorizedSongs = new List<MusicListItem>();

            foreach (string entry in snapshot.MusicEntries)
            {
                string token = entry.Trim();
                if (string.IsNullOrWhiteSpace(token)
                    || string.Equals(token, "~stop.mp3", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token.Trim('='), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isSong = LooksLikeMusicEntry(token);
                string displayName = isSong
                    ? GetMusicItemCustomName(token, GetMusicDisplayName(token), snapshot)
                    : token;
                string assetPath = isSong ? ResolveMusicDisplayPathForBuild(token, snapshot) : string.Empty;
                bool matchesFilter = string.IsNullOrWhiteSpace(snapshot.Filter)
                    || displayName.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    || token.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    || assetPath.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    || (currentCategory?.DisplayName.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase) == true);

                if (!string.IsNullOrWhiteSpace(snapshot.Filter) && !matchesFilter)
                {
                    if (!isSong)
                    {
                        currentCategory = CreateMusicCategoryItem(token, CreateMusicCategoryKey("server", token), snapshot.CollapsedKeys);
                    }

                    continue;
                }

                if (!isSong)
                {
                    currentCategory = CreateMusicCategoryItem(token, CreateMusicCategoryKey("server", token), snapshot.CollapsedKeys);
                    serverRoot.Children.Add(currentCategory);
                    continue;
                }

                bool isCurrent = MusicTokenMatchesCurrent(token, snapshot.CurrentMusicToken);
                bool localFileExists = !string.IsNullOrWhiteSpace(ResolveMusicPathForBuild(token, snapshot));
                string displayAssetPath = localFileExists
                    ? assetPath
                    : GetExpectedMusicDisplayPath(token);
                MusicListItem songItem = new MusicListItem
                {
                    DisplayName = displayName,
                    Token = token,
                    PlayToken = token,
                    AssetPath = displayAssetPath,
                    Playlist = currentCategory?.DisplayName ?? string.Empty,
                    IsCategory = false,
                    IsPlayable = true,
                    Tooltip = localFileExists ? null : "Not found in local AO2 installation",
                    RowBackground = isCurrent ? musicCurrentBrush : localFileExists ? musicFoundBrush : musicMissingBrush,
                    TitleBrush = isCurrent ? Brushes.LightGreen : Brushes.Gainsboro,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Padding = new Thickness(currentCategory == null ? 7 : 18, 4, 7, 4),
                    AssetPathVisibility = snapshot.ShowAssetPaths ? Visibility.Visible : Visibility.Collapsed,
                };

                if (currentCategory == null)
                {
                    uncategorizedSongs.Add(songItem);
                }
                else
                {
                    if (!serverRoot.Children.Contains(currentCategory))
                    {
                        serverRoot.Children.Add(currentCategory);
                    }

                    currentCategory.Children.Add(songItem);
                }
            }

            // Uncategorized songs go after all categories (folders-first ordering).
            foreach (MusicListItem song in uncategorizedSongs)
            {
                serverRoot.Children.Add(song);
            }
        }

        private void BuildLocalMusicItems(
            MusicListItem localRoot,
            MusicListBuildSnapshot snapshot)
        {
            // Build a lookup: lowercase base name (no extension) → server token, for local→server matching
            Dictionary<string, string> serverByToken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> serverByBaseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in snapshot.MusicEntries)
            {
                if (!LooksLikeMusicEntry(entry))
                {
                    continue;
                }

                string strippedEntry = StripCustomPrefix(entry);
                serverByToken.TryAdd(strippedEntry, entry);
                serverByToken.TryAdd(entry, entry);
                string entryBase = Path.GetFileNameWithoutExtension(strippedEntry);
                serverByBaseName.TryAdd(entryBase, entry);
            }

            List<MusicListItem> uncategorizedSongs = new List<MusicListItem>();

            foreach (MusicAssetEntry asset in snapshot.LocalMusicAssets)
            {
                string token = asset.Token;
                string displayName = GetMusicItemCustomName(token, GetMusicDisplayName(token), snapshot);
                string assetPath = asset.FullPath;
                if (!string.IsNullOrWhiteSpace(snapshot.Filter)
                    && !displayName.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    && !token.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase)
                    && !assetPath.Contains(snapshot.Filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isCurrent = MusicTokenMatchesCurrent(token, snapshot.CurrentMusicToken);
                MusicListItem parent = EnsureLocalMusicCategory(localRoot, token, snapshot.CollapsedKeys);

                // Find best server-side token to actually send in MC packet
                string tokenBase = Path.GetFileNameWithoutExtension(token);
                string playToken = serverByToken.TryGetValue(token, out string? exactMatch) ? exactMatch
                    : serverByBaseName.TryGetValue(tokenBase, out string? baseMatch) ? baseMatch
                    : token;

                MusicListItem songItem = new MusicListItem
                {
                    DisplayName = displayName,
                    Token = token,
                    PlayToken = playToken,
                    AssetPath = assetPath,
                    Playlist = "LOCAL FILES",
                    IsCategory = false,
                    IsPlayable = true,
                    RowBackground = isCurrent ? musicCurrentBrush : musicFoundBrush,
                    TitleBrush = isCurrent ? Brushes.LightGreen : Brushes.Gainsboro,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Padding = new Thickness(parent == localRoot ? 7 : 18, 4, 7, 4),
                    AssetPathVisibility = snapshot.ShowAssetPaths ? Visibility.Visible : Visibility.Collapsed,
                };

                if (parent == localRoot)
                {
                    uncategorizedSongs.Add(songItem);
                }
                else
                {
                    parent.Children.Add(songItem);
                }
            }

            // Sort category nodes (and their children recursively) by Windows natural order,
            // then append uncategorized songs (also naturally sorted) at the bottom.
            SortMusicChildrenNatural(localRoot);
            foreach (MusicListItem song in uncategorizedSongs.OrderBy(s => s.DisplayName, NaturalStringComparer.Instance))
            {
                localRoot.Children.Add(song);
            }
        }

        private static void SortMusicChildrenNatural(MusicListItem node)
        {
            if (node.Children.Count == 0)
            {
                return;
            }

            List<MusicListItem> categories = node.Children
                .Where(c => c.IsCategory)
                .OrderBy(c => c.DisplayName, NaturalStringComparer.Instance)
                .ToList();
            List<MusicListItem> songs = node.Children
                .Where(c => !c.IsCategory)
                .OrderBy(c => c.DisplayName, NaturalStringComparer.Instance)
                .ToList();

            node.Children.Clear();
            foreach (MusicListItem cat in categories)
            {
                SortMusicChildrenNatural(cat);
                node.Children.Add(cat);
            }

            foreach (MusicListItem song in songs)
            {
                node.Children.Add(song);
            }
        }

        private bool PropagateRedCategoryState(MusicListItem node)
        {
            if (node.Children.Count == 0)
            {
                return !node.IsCategory && node.RowBackground == musicMissingBrush;
            }

            bool allMissing = node.Children.All(child => PropagateRedCategoryState(child));
            if (node.IsCategory && allMissing)
            {
                node.RowBackground = musicMissingBrush;
            }

            return allMissing;
        }

        private MusicListItem EnsureLocalMusicCategory(
            MusicListItem localRoot,
            string token,
            HashSet<string> collapsedKeys)
        {
            string directory = Path.GetDirectoryName(token.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
            {
                return localRoot;
            }

            MusicListItem current = localRoot;
            string key = "local";
            foreach (string part in directory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                key = CreateMusicCategoryKey(key, part);
                MusicListItem? child = current.Children.FirstOrDefault(item =>
                    item.IsCategory && string.Equals(item.DisplayName, part, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = CreateMusicCategoryItem(part, key, collapsedKeys);
                    child.Padding = new Thickness(current == localRoot ? 14 : 22, 5, 7, 5);
                    current.Children.Add(child);
                }

                current = child;
            }

            return current;
        }

        private MusicListItem CreateMusicCategoryItem(
            string name,
            string categoryKey,
            HashSet<string> collapsedKeys)
        {
            return new MusicListItem
            {
                DisplayName = name,
                CategoryKey = categoryKey,
                IsCategory = true,
                IsPlayable = false,
                IsExpanded = !collapsedKeys.Contains(categoryKey),
                RowBackground = musicCategoryBrush,
                TitleBrush = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(7, 5, 7, 5),
                AssetPathVisibility = Visibility.Collapsed
            };
        }

        private bool ShouldIncludeMusicCategory(MusicListItem item, string filter)
        {
            return string.IsNullOrWhiteSpace(filter)
                || item.Children.Count > 0
                || item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateCurrentMusicDisplay(AOClient? networkClient)
        {
            if (networkClient == null || string.IsNullOrWhiteSpace(currentMusicToken))
            {
                txtCurrentMusicTitle.Text = "None";
                txtCurrentMusicPath.Text = "No active music packet.";
                txtCurrentMusicPath.Visibility = SaveFile.Data.MusicListShowAssetPaths ? Visibility.Visible : Visibility.Collapsed;
                txtCurrentMusicPlaylist.Text = "Playlist: Not provided by server";
                txtCurrentMusicPlaylist.Visibility = Visibility.Collapsed;
                return;
            }

            txtCurrentMusicTitle.Text = GetMusicDisplayName(currentMusicToken);
            txtCurrentMusicPath.Text = ResolveFastMusicDisplayPath(currentMusicToken);
            txtCurrentMusicPath.Visibility = SaveFile.Data.MusicListShowAssetPaths ? Visibility.Visible : Visibility.Collapsed;
            string playlist = string.IsNullOrWhiteSpace(currentMusicPlaylist)
                ? ResolveMusicPlaylist(networkClient.GetAvailableMusicSnapshot(), currentMusicToken)
                : currentMusicPlaylist;
            txtCurrentMusicPlaylist.Visibility = string.IsNullOrWhiteSpace(playlist) ? Visibility.Collapsed : Visibility.Visible;
            txtCurrentMusicPlaylist.Text = string.IsNullOrWhiteSpace(playlist) ? string.Empty : "Playlist: " + playlist;
        }

        private static string ResolveMusicPlaylist(IReadOnlyList<string> musicEntries, string musicToken)
        {
            // Strip "custom/" prefix so tsuserverCC echoes still resolve to the right playlist.
            string normalizedTarget = StripCustomPrefix(musicToken.Trim());
            string currentCategory = string.Empty;
            foreach (string entry in musicEntries)
            {
                string token = entry.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (!LooksLikeMusicEntry(token))
                {
                    currentCategory = token;
                    continue;
                }

                if (string.Equals(token, normalizedTarget, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, musicToken, StringComparison.OrdinalIgnoreCase))
                {
                    return currentCategory;
                }
            }

            return string.Empty;
        }

        private static string GetMusicDisplayName(string token)
        {
            string fileName = Path.GetFileNameWithoutExtension(token);
            return string.IsNullOrWhiteSpace(fileName) ? token : fileName;
        }

        private static string GetMusicItemCustomName(string nameKey, string fallback)
        {
            return SaveFile.Data.MusicCustomNames.TryGetValue(nameKey, out string? custom)
                && !string.IsNullOrWhiteSpace(custom)
                ? custom : fallback;
        }

        private static string GetMusicItemCustomName(string nameKey, string fallback, MusicListBuildSnapshot snapshot)
        {
            return snapshot.CustomNames.TryGetValue(nameKey, out string? custom)
                && !string.IsNullOrWhiteSpace(custom)
                ? custom
                : fallback;
        }

        private static bool LooksLikeMusicEntry(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateMusicCategoryKey(string parentKey, string name)
        {
            string normalized = (name ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            return string.IsNullOrWhiteSpace(parentKey)
                ? normalized
                : parentKey.TrimEnd('/') + "/" + normalized;
        }

        /// <summary>
        /// Returns true when <paramref name="token"/> refers to the same track as
        /// <paramref name="currentToken"/>, accounting for servers that echo music
        /// packets with a leading "custom/" prefix (e.g. tsuserverCC).
        /// </summary>
        private static bool MusicTokenMatchesCurrent(string token, string currentToken)
        {
            if (string.Equals(token, currentToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string strippedCurrent = StripCustomPrefix(currentToken);
            return !string.Equals(strippedCurrent, currentToken, StringComparison.OrdinalIgnoreCase)
                && string.Equals(token, strippedCurrent, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripCustomPrefix(string token)
        {
            const string prefix = "custom/";
            return token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? token.Substring(prefix.Length)
                : token;
        }

        private static HashSet<string> GetMusicCollapsedCategoryKeys()
        {
            return new HashSet<string>(
                SaveFile.Data.MusicListCollapsedCategoryKeys ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string ResolveMusicPathForBuild(string token, MusicListBuildSnapshot snapshot)
        {
            string strippedToken = StripCustomPrefix(token);
            return snapshot.LocalMusicIndex.TryGetValue(token, out string? indexedPath)
                ? indexedPath
                : snapshot.LocalMusicIndex.TryGetValue(strippedToken, out string? strippedPath)
                    ? strippedPath
                    : string.Empty;
        }

        private static string ResolveMusicDisplayPathForBuild(string token, MusicListBuildSnapshot snapshot)
        {
            string strippedToken = StripCustomPrefix(token);
            return snapshot.LocalMusicIndex.TryGetValue(token, out string? indexedPath)
                ? indexedPath
                : snapshot.LocalMusicIndex.TryGetValue(strippedToken, out string? strippedPath)
                    ? strippedPath
                    : GetExpectedMusicDisplayPath(token);
        }

        private string ResolveFastMusicDisplayPath(string token)
        {
            if (displayMusicPathCache.TryGetValue(token, out string? cachedPath))
            {
                return cachedPath;
            }

            string strippedToken = StripCustomPrefix(token);
            if (localMusicIndex != null && localMusicIndex.TryGetValue(token, out string? indexedPath))
            {
                displayMusicPathCache[token] = indexedPath;
                return indexedPath;
            }

            if (localMusicIndex != null && localMusicIndex.TryGetValue(strippedToken, out string? strippedPath))
            {
                displayMusicPathCache[token] = strippedPath;
                return strippedPath;
            }

            return GetExpectedMusicDisplayPath(token);
        }

        private static string GetExpectedMusicDisplayPath(string token)
        {
            return $"base/sounds/music/{StripCustomPrefix(token)}";
        }

        private void ClearMusicPathCaches()
        {
            displayMusicPathCache.Clear();
        }

        private void EnsureLocalMusicAssetsScanStarted()
        {
            if (localMusicAssetsCache != null || localMusicAssetsScanTask != null)
            {
                return;
            }

            localMusicAssetsScanTask = RefreshLocalMusicAssetsAndRefreshAsync();
        }

        private async Task RefreshLocalMusicAssetsAndRefreshAsync()
        {
            var scanSw = Stopwatch.StartNew();
            IReadOnlyList<MusicAssetEntry> assets;
            try
            {
                assets = await Task.Run(AO2ViewportAudioResolver.EnumerateLocalMusicAssets);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    localMusicAssetsScanTask = null;
                    CustomConsole.Warning(
                        "Music list local-file scan failed.",
                        ex,
                        CustomConsole.LogCategory.MusicList);
                }, DispatcherPriority.Background);
                return;
            }

            Dictionary<string, string> index = await Task.Run(() =>
            {
                Dictionary<string, string> built = new Dictionary<string, string>(assets.Count, StringComparer.OrdinalIgnoreCase);
                foreach (MusicAssetEntry entry in assets)
                {
                    built.TryAdd(entry.Token, entry.FullPath);
                }

                return built;
            });
            scanSw.Stop();
            await Dispatcher.InvokeAsync(() =>
            {
                localMusicAssetsCache = assets;
                localMusicIndex = index;
                localMusicAssetsScanTask = null;
                CustomConsole.Debug(
                    $"Music list local-file scan indexed {assets.Count} tracks in {scanSw.ElapsedMilliseconds}ms",
                    CustomConsole.LogCategory.MusicList);
                RefreshMusicListForCurrentClient();
            }, DispatcherPriority.Background);
        }

        private void ResetLocalMusicAssetCache()
        {
            localMusicAssetsCache = null;
            localMusicIndex = null;
            localMusicAssetsScanTask = null;
        }

        private void UpdateConnectionInfoBar()
        {
            AOClient? networkClient = currentClient == null ? null : GetTargetClientForNetwork(currentClient);
            string server = SaveFile.Data.SelectedServerName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(server))
            {
                server = Globals.GetSelectedServerEndpoint();
            }

            if (string.IsNullOrWhiteSpace(server))
            {
                server = "Disconnected";
            }

            string area = networkClient == null || string.IsNullOrWhiteSpace(networkClient.CurrentArea)
                ? "Unknown"
                : networkClient.CurrentArea;
            AreaInfo? areaInfo = FindCurrentAreaInfo(networkClient);
            int users = areaInfo?.Players ?? -1;
            string caseManager = NormalizeAreaMetric(areaInfo?.CaseManager, "FREE");
            string status = NormalizeAreaMetric(areaInfo?.Status, "IDLE");
            string lockState = NormalizeAreaMetric(areaInfo?.LockState, "FREE");

            txtSelectedServerInfo.Text = server;
            txtSelectedAreaInfo.Text = area;
            txtSelectedAreaUsersInfo.Text = users >= 0 ? users.ToString() : "-";
            txtSelectedServerInfo.ToolTip = server;
            txtSelectedAreaInfo.ToolTip = area;
            txtSelectedAreaUsersInfo.ToolTip = users >= 0 ? $"{users} users" : "Unknown users";

            // STATUS chip — hidden when IDLE (the boring default state)
            bool showStatus = !string.Equals(status, "IDLE", StringComparison.OrdinalIgnoreCase);
            if (showStatus)
            {
                txtSelectedAreaStatusInfo.Text = status;
                StatusChip.Background = GetStatusChipBrush(status);
                StatusChip.Visibility = Visibility.Visible;
                txtSelectedAreaStatusInfo.ToolTip = $"Area status: {status}";
            }
            else
            {
                StatusChip.Visibility = Visibility.Collapsed;
            }

            // LOCK chip — hidden when FREE (normal unlocked state)
            bool showLock = !string.Equals(lockState, "FREE", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(lockState, "OPEN", StringComparison.OrdinalIgnoreCase);
            if (showLock)
            {
                txtSelectedAreaLockInfo.Text = lockState;
                LockChip.Visibility = Visibility.Visible;
                txtSelectedAreaLockInfo.ToolTip = $"Lock: {lockState}";
            }
            else
            {
                LockChip.Visibility = Visibility.Collapsed;
            }

            // CM chip — hidden when FREE (no active case manager)
            bool showCm = !string.Equals(caseManager, "FREE", StringComparison.OrdinalIgnoreCase);
            if (showCm)
            {
                txtSelectedAreaMetaInfo.Text = caseManager;
                CmChip.Visibility = Visibility.Visible;
                txtSelectedAreaMetaInfo.ToolTip = $"Case manager: {caseManager}";
            }
            else
            {
                CmChip.Visibility = Visibility.Collapsed;
            }
        }

        private static Brush GetStatusChipBrush(string status)
        {
            if (string.Equals(status, "LOOKING-FOR-PLAYERS", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x12, 0x23, 0x18));
            if (string.Equals(status, "CASING", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x0E));
            if (string.Equals(status, "RECESS", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x2A));
            if (string.Equals(status, "RP", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x22, 0x14, 0x28));
            if (string.Equals(status, "GAMING", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x10, 0x24, 0x24));
            return new SolidColorBrush(Color.FromRgb(0x18, 0x15, 0x20));
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
            singleInternalClient.ApplyCharacterAvailabilityForTests(CharacterFolder.FullList.Select(character => character.Name));
        }

        private void InitializeDreddFeatureUi()
        {
            isDreddFeatureEnabled = SaveFile.Data.AdvancedFeatures.IsEnabled(AdvancedFeatureIds.DreddBackgroundOverlayOverride);
            DreddStickyOverlayCheckBox.IsChecked = SaveFile.Data.DreddBackgroundOverlayOverride.StickyOverlay;
            Height = GetMainWindowTargetHeight(isDreddFeatureEnabled);
            imgScienceBlur.Height = GetMainWindowBodyHeight(isDreddFeatureEnabled);
            imgScienceBlur_darken.Height = GetMainWindowBodyHeight(isDreddFeatureEnabled);

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

            double verticalOffset = enabled ? GetDreddFeatureRowHeight() : 0;
            Canvas.SetTop(BottomStatusBar, 603 + verticalOffset);
            Canvas.SetTop(chkSticky, 607 + verticalOffset);
            Canvas.SetTop(btnRefreshCharacters, 603 + verticalOffset);
            Canvas.SetTop(chkPosOnIniSwap, 607 + verticalOffset);
            Canvas.SetTop(btnDebug, 607 + verticalOffset);
            Canvas.SetTop(chkInvertLog, 607 + verticalOffset);
            Canvas.SetTop(btnAreaNavigator, 603 + verticalOffset);
            Canvas.SetTop(btnMusicList, 603 + verticalOffset);
            Canvas.SetTop(btnViewport, 603 + verticalOffset);
            Canvas.SetTop(btnSettings, 603 + verticalOffset);

            UpdateDreddFeatureEnabledState();
        }

        private double GetMainWindowTargetHeight(bool includeDreddFeatureRow)
        {
            double topBarHeight = ConnectionInfoBar.ActualHeight > 0
                ? ConnectionInfoBar.ActualHeight
                : ConnectionInfoBarHeight;
            return GetMainWindowBodyHeight(includeDreddFeatureRow) + topBarHeight;
        }

        private void ConnectionInfoBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.HeightChanged)
            {
                return;
            }

            Height = GetMainWindowTargetHeight(isDreddFeatureEnabled);
        }

        private double GetMainWindowBodyHeight(bool includeDreddFeatureRow)
        {
            return MainWindowBodyHeight + (includeDreddFeatureRow ? GetDreddFeatureRowHeight() : 0);
        }

        private double GetDreddFeatureRowHeight()
        {
            return FeatureRowBackground.Height > 0 ? FeatureRowBackground.Height : DreddFeatureRowHeight;
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
            if (!isDreddFeatureEnabled)
            {
                lastDreddOverlayContextKey = string.Empty;
                lastUnknownOverlayPromptKey = string.Empty;
                DreddOverlayListBox.ItemsSource = Array.Empty<DreddOverlaySelectionItem>();
                DreddOverlaySelectedText.Text = DreddNoneOverlayName;
                return;
            }

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
            singleInternalClient.PairTargetCharId = profileClient.PairTargetCharId;
            singleInternalClient.PairTargetCharacterName = profileClient.PairTargetCharacterName;
            singleInternalClient.PairLayerOrder = profileClient.PairLayerOrder;
            singleInternalClient.LastSentPairTargetCharId = profileClient.LastSentPairTargetCharId;
            singleInternalClient.LastSentPairLayerOrder = profileClient.LastSentPairLayerOrder;
            singleInternalClient.LastSentPairPosition = profileClient.LastSentPairPosition;
            singleInternalClient.LastSentPairSelfOffset = profileClient.LastSentPairSelfOffset;
            singleInternalClient.ConfirmedPairTargetCharIds.Clear();
            foreach (int confirmedPairTargetCharId in profileClient.ConfirmedPairTargetCharIds)
            {
                singleInternalClient.ConfirmedPairTargetCharIds.Add(confirmedPairTargetCharId);
            }
            singleInternalClient.KnownPairTargetPositions.Clear();
            foreach (KeyValuePair<int, string> knownPairTargetPosition in profileClient.KnownPairTargetPositions)
            {
                singleInternalClient.KnownPairTargetPositions[knownPairTargetPosition.Key] = knownPairTargetPosition.Value;
            }
            singleInternalClient.switchPosWhenChangingINI = profileClient.switchPosWhenChangingINI;

            if (!string.IsNullOrWhiteSpace(profileClient.curPos))
            {
                singleInternalClient.SetPos(profileClient.curPos, false);
            }
        }

        private async Task EnsureSingleInternalClientProfileSelectionAsync(AOClient profileClient)
        {
            if (!useSingleInternalClient || singleInternalClient == null || profileClient == null)
            {
                return;
            }

            string desiredIniPuppet = GetProfileIniPuppetName(profileClient);
            if (string.IsNullOrWhiteSpace(desiredIniPuppet)
                || string.Equals(singleInternalClient.iniPuppetName, desiredIniPuppet, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                await singleInternalClient.SelectIniPuppet(desiredIniPuppet, false);
                profileClient.iniPuppetID = singleInternalClient.iniPuppetID;
                SetProfileIniPuppetName(profileClient, desiredIniPuppet);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning(
                    $"Could not select snapshot INIPuppet \"{desiredIniPuppet}\" for \"{profileClient.clientName}\".",
                    ex,
                    CustomConsole.LogCategory.IC);
            }
        }

        private string GetProfileIniPuppetName(AOClient profileClient)
        {
            if (profileIniPuppetNames.TryGetValue(profileClient, out string? savedName)
                && !string.IsNullOrWhiteSpace(savedName))
            {
                return savedName.Trim();
            }

            return string.IsNullOrWhiteSpace(profileClient.iniPuppetName)
                ? string.Empty
                : profileClient.iniPuppetName.Trim();
        }

        private void SetProfileIniPuppetName(AOClient profileClient, string? iniPuppetName)
        {
            string normalized = iniPuppetName?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                profileIniPuppetNames.Remove(profileClient);
                return;
            }

            profileIniPuppetNames[profileClient] = normalized;
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

        private void ApplyServerPositionToAllSingleInternalProfiles(string newPos)
        {
            if (!useSingleInternalClient)
            {
                return;
            }

            foreach (AOClient profileClient in clientOrder)
            {
                if (ReferenceEquals(profileClient, singleInternalClient))
                {
                    continue;
                }

                profileClient.SetPos(newPos);
            }

            if (currentClient != null)
            {
                ICMessageSettingsControl.SetClient(currentClient);
            }

            CaptureGmMultiClientSnapshot();
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
                Dispatcher.BeginInvoke(() =>
                {
                    RefreshAreaNavigatorForCurrentClient();
                }, DispatcherPriority.Background);
            };

            networkClient.OnAvailableAreasUpdated += (IReadOnlyList<string> _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    RefreshAreaNavigatorForCurrentClient();
                }, DispatcherPriority.Background);
            };

            networkClient.OnAvailableAreaInfosUpdated += (IReadOnlyList<AreaInfo> _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    RefreshAreaNavigatorForCurrentClient();
                }, DispatcherPriority.Background);
            };

            networkClient.OnAvailableMusicUpdated += (IReadOnlyList<string> _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ClearMusicPathCaches();
                    RefreshMusicListForCurrentClient();
                }, DispatcherPriority.Background);
            };

            networkClient.OnMusicChanged += (string _, string? songPath, bool loop, int channel, int effectFlags) =>
            {
                if (channel != 0)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (viewportWindow == null || viewportWindow.Visibility != Visibility.Visible)
                        {
                            mainMusicAudioManager.PlayAmbientMusic(channel, songPath, loop);
                        }
                    }, DispatcherPriority.Background);
                    return;
                }

                Dispatcher.BeginInvoke(() =>
                {
                    currentMusicToken = songPath?.Trim() ?? string.Empty;
                    currentMusicPlaylist = string.IsNullOrWhiteSpace(currentMusicToken)
                        ? string.Empty
                        : ResolveMusicPlaylist(networkClient.GetAvailableMusicSnapshot(), currentMusicToken);
                    if (string.IsNullOrWhiteSpace(currentMusicToken))
                    {
                        mainMusicAudioManager.StopMusic(effectFlags);
                    }
                    else if (viewportWindow == null || viewportWindow.Visibility != Visibility.Visible)
                    {
                        mainMusicAudioManager.PlayMusic(currentMusicToken, loop, effectFlags);
                    }

                    RefreshMusicListForCurrentClient();
                }, DispatcherPriority.Background);
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
                    AddLoggedIcMessageWithContext(
                        targetClient,
                        icMessage.ShowName,
                        icMessage.Message,
                        isSentFromSelf,
                        icMessage.TextColor,
                        icMessage);

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
                    if (targetClient == null || ReferenceEquals(targetClient, singleInternalClient))
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
                    if (targetClient == null || ReferenceEquals(targetClient, singleInternalClient))
                    {
                        return;
                    }

                    targetClient.SetPos(newPos);
                });
            };

            singleInternalClient.OnServerPositionReceived += (string newPos) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ApplyServerPositionToAllSingleInternalProfiles(newPos);
                });
            };

            singleInternalClient.FrequencyHintsProvider = () => SaveFile.Data.FrequentlyUsedIniPuppets;
            InitializeCommonClientEvents(singleInternalClient, singleInternalClient);
            await ConnectClientAsync(singleInternalClient, autoSelectCharacter: false);
            await BootstrapAreaNavigatorAsync(singleInternalClient);
            RefreshViewportAttachment();
        }
        private void AddClient(string? clientName = null)
        {
            _ = AddClientAsync(clientName);
        }

        private Task AddClientAsync(string? clientName = null)
        {
            return AddClientInternalAsync(clientName, null, suppressInitialCharacterPrompt: false);
        }

        /// <summary>
        /// Shows the character selector for <paramref name="bot"/>.
        /// Returns (true, selectedClientName) if the user confirmed, (false, null) if cancelled.
        /// Pass <paramref name="defaultClientName"/> to show the embedded client-name field (add-client flow).
        /// </summary>
        private async Task<(bool confirmed, string? selectedClientName)> ShowCharacterSelectorForClientAsync(
            AOClient bot,
            string? defaultClientName = null,
            IReadOnlyCollection<string>? additionalUnavailableCharacters = null,
            bool preserveLocalCharacter = false)
        {
            ClientCharacterSelectionResult result = await ShowCharacterSelectorForClientSelectionAsync(
                bot,
                defaultClientName,
                additionalUnavailableCharacters);
            if (!result.Confirmed || string.IsNullOrWhiteSpace(result.SelectedCharacterName))
            {
                return (false, null);
            }

            _ = ApplyCharacterSelectionAsync(bot, result.SelectedCharacterName, preserveLocalCharacter);
            return (true, result.SelectedClientName);
        }

        private async Task<ClientCharacterSelectionResult> ShowCharacterSelectorForClientSelectionAsync(
            AOClient bot,
            string? defaultClientName = null,
            IReadOnlyCollection<string>? additionalUnavailableCharacters = null,
            bool skipWaitForm = false)
        {
            AOClient? networkClient = GetTargetClientForNetwork(bot);
            if (networkClient == null)
            {
                return ClientCharacterSelectionResult.Cancelled;
            }

            Window? owner = HostWindow ?? Application.Current?.MainWindow;
            bool waitFormShown = false;
            if (!skipWaitForm && owner != null && !OceanyaTestMode.Current.DisableWaitForms)
            {
                await WaitForm.ShowFormAsync("Loading characters...", owner);
                waitFormShown = true;
                WaitForm.SetSubtitle("Building character list...");
                // Yield so the WaitForm renders before the UI thread is blocked by BuildSections().
                await Task.Yield();
            }

            CharacterSelectorWindow selector = new CharacterSelectorWindow(
                BuildCharacterSelectorAvailability(networkClient.ServerCharacterAvailability, additionalUnavailableCharacters),
                SaveFile.Data.FrequentlyUsedIniPuppets,
                networkClient.iniPuppetName,
                defaultClientName)
            {
                Owner = HostWindow ?? Application.Current?.MainWindow
            };

            if (waitFormShown)
            {
                await WaitForm.CloseFormAsync();
            }

            if (selector.ShowDialog() != true || string.IsNullOrWhiteSpace(selector.SelectedCharacterName))
            {
                return ClientCharacterSelectionResult.Cancelled;
            }

            string charName = selector.SelectedCharacterName;
            return new ClientCharacterSelectionResult(true, charName, selector.SelectedClientName);
        }

        private static IReadOnlyDictionary<string, bool> BuildCharacterSelectorAvailability(
            IReadOnlyDictionary<string, bool> baseAvailability,
            IReadOnlyCollection<string>? additionalUnavailableCharacters)
        {
            Dictionary<string, bool> availability = new Dictionary<string, bool>(
                baseAvailability,
                StringComparer.OrdinalIgnoreCase);

            if (additionalUnavailableCharacters == null)
            {
                return availability;
            }

            foreach (string characterName in additionalUnavailableCharacters)
            {
                string normalized = characterName?.Trim() ?? string.Empty;
                if (normalized.Length == 0 || !availability.ContainsKey(normalized))
                {
                    continue;
                }

                availability[normalized] = false;
            }

            return availability;
        }

        private Task ApplyCharacterSelectionAsync(AOClient bot, string charName)
        {
            return ApplyCharacterSelectionAsync(bot, charName, preserveLocalCharacter: false);
        }

        private async Task ApplyCharacterSelectionAsync(AOClient bot, string charName, bool preserveLocalCharacter)
        {
            try
            {
                AOClient? networkClient = GetTargetClientForNetwork(bot);
                if (networkClient == null)
                {
                    return;
                }

                CharacterFolder? previousCharacter = bot.currentINI;
                Emote? previousEmote = bot.currentEmote;
                string previousICShowname = bot.ICShowname;
                string previousPosition = bot.curPos;

                await networkClient.SelectIniPuppet(charName, !preserveLocalCharacter);
                SetProfileIniPuppetName(bot, charName);

                if (useSingleInternalClient && !ReferenceEquals(networkClient, bot))
                {
                    if (!preserveLocalCharacter && networkClient.currentINI != null)
                    {
                        bot.SetCharacter(networkClient.currentINI);
                    }
                    bot.iniPuppetID = networkClient.iniPuppetID;
                }

                if (preserveLocalCharacter)
                {
                    bot.SetCharacter(previousCharacter);
                    if (previousEmote != null)
                    {
                        bot.SetEmote(previousEmote.DisplayID);
                    }

                    bot.SetICShowname(previousICShowname);
                    bot.SetPos(previousPosition);
                    if (ReferenceEquals(bot, currentClient))
                    {
                        ICMessageSettingsControl.SetClient(bot);
                    }
                }

                if (!SaveFile.Data.FrequentlyUsedIniPuppets.ContainsKey(charName))
                {
                    SaveFile.Data.FrequentlyUsedIniPuppets[charName] = 0;
                }
                SaveFile.Data.FrequentlyUsedIniPuppets[charName]++;
                SaveFile.Save();
                CaptureGmMultiClientSnapshot();
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Character selection failed for \"{charName}\"", ex);
            }
        }

        private static void SyncPairSendStateFromNetworkClient(AOClient profileClient, AOClient networkClient)
        {
            profileClient.LastSentPairTargetCharId = networkClient.LastSentPairTargetCharId;
            profileClient.LastSentPairLayerOrder = networkClient.LastSentPairLayerOrder;
            profileClient.LastSentPairPosition = networkClient.LastSentPairPosition;
            profileClient.LastSentPairSelfOffset = networkClient.LastSentPairSelfOffset;

            foreach (int confirmedPairTargetCharId in networkClient.ConfirmedPairTargetCharIds)
            {
                profileClient.ConfirmedPairTargetCharIds.Add(confirmedPairTargetCharId);
            }

            foreach (KeyValuePair<int, string> knownPairTargetPosition in networkClient.KnownPairTargetPositions)
            {
                profileClient.KnownPairTargetPositions[knownPairTargetPosition.Key] = knownPairTargetPosition.Value;
            }
        }

        private async Task ConnectClientAsync(AOClient bot, bool autoSelectCharacter = true)
        {
            if (testConnectClientAsyncOverride != null)
            {
                await testConnectClientAsyncOverride(bot);
                ao2TextLogWriter.RefreshSession();
                return;
            }

            try
            {
                await bot.Connect(
                    betweenAreasAndIniPuppet: autoSelectCharacter ? 1000 : 0,
                    finalDelay: 0,
                    autoSelectCharacter: autoSelectCharacter);
                ao2TextLogWriter.RefreshSession();
            }
            catch (TimeoutException ex) when (IsHandshakeTimeout(ex))
            {
                CustomConsole.Warning(
                    $"Retrying client connection after handshake timeout for \"{bot.clientName}\".",
                    category: CustomConsole.LogCategory.System);
                await bot.DisconnectWebsocket();
                await Task.Delay(750);
                await bot.Connect(
                    betweenAreasAndIniPuppet: autoSelectCharacter ? 1000 : 0,
                    finalDelay: 0,
                    autoSelectCharacter: autoSelectCharacter);
                ao2TextLogWriter.RefreshSession();
            }
        }

        private static bool IsHandshakeTimeout(Exception ex)
        {
            return ex.Message.IndexOf("Timed out waiting for handshake packet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplySnapshotStateToClient(AOClient client, GmMultiClientSnapshotClient state)
        {
            client.clientName = string.IsNullOrWhiteSpace(state.ClientName)
                ? $"Client{clients.Count + 1}"
                : state.ClientName.Trim();
            SetProfileIniPuppetName(client, state.IniPuppetName);

            string localCharacterName = !string.IsNullOrWhiteSpace(state.LocalCharacterName)
                ? state.LocalCharacterName
                : state.IniPuppetName;
            if (!string.IsNullOrWhiteSpace(localCharacterName))
            {
                client.SetCharacter(localCharacterName);
            }

            if (!string.IsNullOrWhiteSpace(state.EmoteDisplayId))
            {
                client.SetEmote(state.EmoteDisplayId);
            }

            client.SetICShowname(state.ICShowname);
            client.OOCShowname = string.IsNullOrWhiteSpace(state.OOCShowname)
                ? client.clientName
                : state.OOCShowname;
            client.iniPuppetID = state.IniPuppetId;
            client.curBG = state.Background;
            client.curSFX = state.Sfx;
            client.deskMod = Enum.IsDefined(typeof(ICMessage.DeskMods), state.DeskMod)
                ? (ICMessage.DeskMods)state.DeskMod
                : client.deskMod;
            client.emoteMod = Enum.IsDefined(typeof(ICMessage.EmoteModifiers), state.EmoteMod)
                ? (ICMessage.EmoteModifiers)state.EmoteMod
                : client.emoteMod;
            client.shoutModifiers = Enum.IsDefined(typeof(ICMessage.ShoutModifiers), state.ShoutModifier)
                ? (ICMessage.ShoutModifiers)state.ShoutModifier
                : ICMessage.ShoutModifiers.Nothing;
            client.flip = state.Flip;
            client.effect = Enum.IsDefined(typeof(ICMessage.Effects), state.Effect)
                ? (ICMessage.Effects)state.Effect
                : ICMessage.Effects.None;
            client.screenshake = state.Screenshake;
            client.textColor = Enum.IsDefined(typeof(ICMessage.TextColors), state.TextColor)
                ? (ICMessage.TextColors)state.TextColor
                : ICMessage.TextColors.White;
            client.PreanimEnabled = state.PreanimEnabled;
            client.Immediate = state.Immediate;
            client.Additive = state.Additive;
            client.switchPosWhenChangingINI = state.SwitchPosWhenChangingIni;

            if (!string.IsNullOrWhiteSpace(state.Position))
            {
                client.SetPos(state.Position, false);
            }
        }

        private GmMultiClientSnapshotClient CreateSnapshotState(AOClient client)
        {
            return new GmMultiClientSnapshotClient
            {
                ClientName = client.clientName?.Trim() ?? string.Empty,
                IniPuppetName = GetProfileIniPuppetName(client),
                IniPuppetId = client.iniPuppetID,
                LocalCharacterName = client.currentINI?.Name ?? string.Empty,
                EmoteDisplayId = client.currentEmote?.DisplayID ?? string.Empty,
                ICShowname = client.ICShowname?.Trim() ?? string.Empty,
                OOCShowname = client.OOCShowname?.Trim() ?? string.Empty,
                Position = client.curPos?.Trim() ?? string.Empty,
                Background = client.curBG?.Trim() ?? string.Empty,
                Sfx = client.curSFX?.Trim() ?? string.Empty,
                DeskMod = (int)client.deskMod,
                EmoteMod = (int)client.emoteMod,
                ShoutModifier = (int)client.shoutModifiers,
                Flip = client.flip,
                Effect = (int)client.effect,
                Screenshake = client.screenshake,
                TextColor = (int)client.textColor,
                PreanimEnabled = client.PreanimEnabled,
                Immediate = client.Immediate,
                Additive = client.Additive,
                SwitchPosWhenChangingIni = client.switchPosWhenChangingINI
            };
        }

        private static string NormalizeAreaMetric(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static AreaInfo? FindCurrentAreaInfo(AOClient? networkClient)
        {
            if (networkClient == null || string.IsNullOrWhiteSpace(networkClient.CurrentArea))
            {
                return null;
            }

            return networkClient.AvailableAreaInfos.FirstOrDefault(areaInfo =>
                string.Equals(areaInfo.Name, networkClient.CurrentArea, StringComparison.OrdinalIgnoreCase));
        }

        private void CaptureGmMultiClientSnapshot()
        {
            if (suppressSnapshotCapture || isRestoringSnapshot || OceanyaTestMode.Current.DisableGmSnapshotPersistence)
            {
                return;
            }

            List<AOClient> clientList = clientOrder
                .Where(client => clients.Values.Contains(client))
                .ToList();
            int selectedIndex = currentClient == null ? -1 : clientList.IndexOf(currentClient);
            SaveFile.Data.GMMultiClientSnapshot = new GmMultiClientSnapshot
            {
                Clients = clientList.Select(CreateSnapshotState).ToList(),
                SelectedClientIndex = selectedIndex,
                SelectedClientName = currentClient?.clientName?.Trim() ?? string.Empty,
                UseSingleInternalClient = useSingleInternalClient
            };
            SaveFile.Save();
        }

        private void MoveClient(AOClient client, int offset)
        {
            ToggleButton? button = clients.FirstOrDefault(pair => ReferenceEquals(pair.Value, client)).Key;
            if (button == null || !EmoteGrid.MoveElement(button, offset))
            {
                return;
            }

            int index = clientOrder.FindIndex(existing => ReferenceEquals(existing, client));
            int targetIndex = index + offset;
            if (index < 0 || targetIndex < 0 || targetIndex >= clientOrder.Count)
            {
                return;
            }

            clientOrder.RemoveAt(index);
            clientOrder.Insert(targetIndex, client);

            SelectClient(client);
            CaptureGmMultiClientSnapshot();
        }

        private async Task RestoreGmMultiClientSnapshotAsync()
        {
            if (OceanyaTestMode.Current.DisableGmSnapshotPersistence)
            {
                return;
            }

            GmMultiClientSnapshot? snapshot = SaveFile.Data.GMMultiClientSnapshot;
            if (snapshot?.Clients == null || snapshot.Clients.Count == 0 || clients.Count > 0)
            {
                return;
            }

            StartupTimingLogger.Log("snapshot_restore_begin", $"clients={snapshot.Clients.Count}");
            isRestoringSnapshot = true;
            suppressSnapshotCapture = true;
            IsEnabled = false;
            try
            {
                List<GmMultiClientSnapshotClient> statesToRestore = snapshot.Clients
                    .Where(state => state != null)
                    .ToList();
                if (statesToRestore.Count == 0)
                {
                    return;
                }

                List<GmMultiClientSnapshotClient> resolvedStates;
                if (useSingleInternalClient)
                {
                    if (OceanyaTestMode.Current.IsEnabled)
                    {
                        EnsureSingleInternalClientConnectedForTests();
                    }
                    else
                    {
                        await EnsureSingleInternalClientConnectedAsync();
                    }

                    if (singleInternalClient == null)
                    {
                        return;
                    }

                    resolvedStates = ResolveSnapshotCharacterConflictsForSingleInternalClient(
                        statesToRestore,
                        singleInternalClient.ServerCharacterAvailability);
                    if (resolvedStates.Count == 0)
                    {
                        return;
                    }

                    await SelectInitialSnapshotPuppetForSingleInternalClientAsync(resolvedStates[0]);
                }
                else
                {
                    AOClient firstConnectedClient = new AOClient(Globals.GetSelectedServerEndpoint());
                    firstConnectedClient.FrequencyHintsProvider = () => SaveFile.Data.FrequentlyUsedIniPuppets;
                    await ConnectClientAsync(firstConnectedClient, autoSelectCharacter: false);
                    resolvedStates = ResolveSnapshotCharacterConflicts(
                        statesToRestore,
                        firstConnectedClient.ServerCharacterAvailability);
                    if (resolvedStates.Count == 0)
                    {
                        await firstConnectedClient.Disconnect();
                        return;
                    }

                    statesToRestore = resolvedStates;
                    await RestoreResolvedDirectSnapshotClientsAsync(snapshot, statesToRestore, firstConnectedClient);
                    return;
                }

                await RestoreResolvedSnapshotClientsAsync(resolvedStates);

                AOClient? preferredClient = FindRestoredSelectedClient(snapshot, resolvedStates);
                if (preferredClient != null)
                {
                    SelectClient(preferredClient);
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to restore GM multi-client snapshot.", ex, CustomConsole.LogCategory.System);
                ShowMainWindowMessage(
                    $"Could not restore the previous GM client snapshot: {ex.Message}",
                    "Snapshot Restore Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                StartupTimingLogger.Log("snapshot_restore_complete", $"clients={clients.Count}");
                StartupTimingLogger.WriteLog();
                IsEnabled = true;
                suppressSnapshotCapture = false;
                isRestoringSnapshot = false;
                CaptureGmMultiClientSnapshot();
            }
        }

        private async Task RestoreResolvedDirectSnapshotClientsAsync(
            GmMultiClientSnapshot snapshot,
            List<GmMultiClientSnapshotClient> resolvedStates,
            AOClient firstConnectedClient)
        {
            try
            {
                await RestoreResolvedSnapshotClientsAsync(resolvedStates, firstConnectedClient);

                AOClient? preferredClient = FindRestoredSelectedClient(snapshot, resolvedStates);
                if (preferredClient != null)
                {
                    SelectClient(preferredClient);
                }
            }
            finally
            {
                if (!clients.Values.Contains(firstConnectedClient))
                {
                    await firstConnectedClient.Disconnect();
                }
            }
        }

        private async Task RestoreResolvedSnapshotClientsAsync(
            IReadOnlyList<GmMultiClientSnapshotClient> resolvedStates,
            AOClient? firstConnectedClient = null)
        {
            Window? restoreWaitOwner = HostWindow ?? Application.Current?.MainWindow;
            bool restoreWaitShown = false;
            if (restoreWaitOwner != null && !OceanyaTestMode.Current.DisableWaitForms)
            {
                await WaitForm.ShowFormAsync("Restoring saved clients...", restoreWaitOwner);
                restoreWaitShown = true;
            }

            try
            {
                for (int i = 0; i < resolvedStates.Count; i++)
                {
                    GmMultiClientSnapshotClient state = resolvedStates[i];
                    if (restoreWaitShown)
                    {
                        WaitForm.SetSubtitle($"Connecting {i + 1}/{resolvedStates.Count}: {state.ClientName}");
                    }

                    await AddClientInternalAsync(
                        state.ClientName,
                        state,
                        suppressInitialCharacterPrompt: true,
                        showWaitForm: false,
                        preconnectedClient: i == 0 ? firstConnectedClient : null);
                }
            }
            finally
            {
                if (restoreWaitShown)
                {
                    await WaitForm.CloseFormAsync();
                }
            }
        }

        private List<GmMultiClientSnapshotClient> ResolveSnapshotCharacterConflicts(
            List<GmMultiClientSnapshotClient> states,
            IReadOnlyDictionary<string, bool> serverAvailability)
        {
            List<GmMultiClientSnapshotClient> resolved = new List<GmMultiClientSnapshotClient>();
            HashSet<string> acceptedPuppets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < states.Count; i++)
            {
                GmMultiClientSnapshotClient state = states[i];
                string requestedPuppet = ResolveSnapshotRequestedPuppet(state, serverAvailability);
                state.IniPuppetName = requestedPuppet;
                HashSet<string> unavailablePuppets = BuildReservedSnapshotPuppets(states, i + 1, acceptedPuppets);

                if (CanUseSnapshotPuppet(requestedPuppet, serverAvailability, unavailablePuppets))
                {
                    resolved.Add(state);
                    acceptedPuppets.Add(requestedPuppet);
                    continue;
                }

                SnapshotPuppetConflictReason conflictReason =
                    GetSnapshotPuppetConflictReason(requestedPuppet, serverAvailability, unavailablePuppets);
                SnapshotConflictDecision decision = ShowSnapshotConflictDialog(state, requestedPuppet, conflictReason);
                if (decision == SnapshotConflictDecision.Delete)
                {
                    continue;
                }

                string? selectedName = ShowCharacterSelectorForAvailability(
                    serverAvailability,
                    requestedPuppet,
                    unavailablePuppets);
                if (string.IsNullOrWhiteSpace(selectedName))
                {
                    continue;
                }

                if (!SaveFile.Data.FrequentlyUsedIniPuppets.ContainsKey(selectedName))
                {
                    SaveFile.Data.FrequentlyUsedIniPuppets[selectedName] = 0;
                }

                SaveFile.Data.FrequentlyUsedIniPuppets[selectedName]++;
                state.IniPuppetName = selectedName;
                resolved.Add(state);
                acceptedPuppets.Add(selectedName);
            }

            return resolved;
        }

        private List<GmMultiClientSnapshotClient> ResolveSnapshotCharacterConflictsForSingleInternalClient(
            List<GmMultiClientSnapshotClient> states,
            IReadOnlyDictionary<string, bool> serverAvailability)
        {
            List<GmMultiClientSnapshotClient> resolved = new List<GmMultiClientSnapshotClient>();
            bool hasSelectedNetworkPuppet = false;

            foreach (GmMultiClientSnapshotClient state in states)
            {
                if (hasSelectedNetworkPuppet)
                {
                    resolved.Add(state);
                    continue;
                }

                string requestedPuppet = ResolveSnapshotRequestedPuppet(state, serverAvailability);
                state.IniPuppetName = requestedPuppet;
                if (CanUseSnapshotPuppet(requestedPuppet, serverAvailability, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                {
                    resolved.Add(state);
                    hasSelectedNetworkPuppet = true;
                    continue;
                }

                SnapshotPuppetConflictReason conflictReason = GetSnapshotPuppetConflictReason(
                    requestedPuppet,
                    serverAvailability,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                SnapshotConflictDecision decision = ShowSnapshotConflictDialog(state, requestedPuppet, conflictReason);
                if (decision == SnapshotConflictDecision.Delete)
                {
                    continue;
                }

                string? selectedName = ShowCharacterSelectorForAvailability(
                    serverAvailability,
                    requestedPuppet,
                    Array.Empty<string>());
                if (string.IsNullOrWhiteSpace(selectedName))
                {
                    continue;
                }

                if (!SaveFile.Data.FrequentlyUsedIniPuppets.ContainsKey(selectedName))
                {
                    SaveFile.Data.FrequentlyUsedIniPuppets[selectedName] = 0;
                }

                SaveFile.Data.FrequentlyUsedIniPuppets[selectedName]++;
                state.IniPuppetName = selectedName;
                resolved.Add(state);
                hasSelectedNetworkPuppet = true;
            }

            return resolved;
        }

        private static HashSet<string> BuildReservedSnapshotPuppets(
            IReadOnlyList<GmMultiClientSnapshotClient> states,
            int startIndex,
            IEnumerable<string> alreadyAcceptedPuppets)
        {
            HashSet<string> reservedPuppets = new HashSet<string>(
                alreadyAcceptedPuppets.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);

            for (int i = startIndex; i < states.Count; i++)
            {
                string plannedPuppet = states[i].IniPuppetName?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(plannedPuppet))
                {
                    reservedPuppets.Add(plannedPuppet);
                }
            }

            return reservedPuppets;
        }

        private static string ResolveSnapshotRequestedPuppet(
            GmMultiClientSnapshotClient state,
            IReadOnlyDictionary<string, bool> serverAvailability)
        {
            string savedPuppet = state.IniPuppetName?.Trim() ?? string.Empty;
            string localCharacter = state.LocalCharacterName?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(savedPuppet)
                && !string.Equals(savedPuppet, localCharacter, StringComparison.OrdinalIgnoreCase))
            {
                return savedPuppet;
            }

            if (state.IniPuppetId >= 0 && state.IniPuppetId < serverAvailability.Count)
            {
                return serverAvailability.Keys.ElementAt(state.IniPuppetId).Trim();
            }

            return savedPuppet;
        }

        private async Task SelectInitialSnapshotPuppetForSingleInternalClientAsync(GmMultiClientSnapshotClient firstState)
        {
            if (singleInternalClient == null)
            {
                return;
            }

            string requestedPuppet = firstState.IniPuppetName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestedPuppet))
            {
                return;
            }

            await singleInternalClient.SelectIniPuppet(requestedPuppet, false);
            firstState.IniPuppetId = singleInternalClient.iniPuppetID;
        }

        private string? ShowCharacterSelectorForAvailability(
            IReadOnlyDictionary<string, bool> serverAvailability,
            string currentSelectedCharName,
            IReadOnlyCollection<string> additionalUnavailableCharacters)
        {
            CharacterSelectorWindow selector = new CharacterSelectorWindow(
                BuildCharacterSelectorAvailability(serverAvailability, additionalUnavailableCharacters),
                SaveFile.Data.FrequentlyUsedIniPuppets,
                currentSelectedCharName)
            {
                Owner = HostWindow ?? Application.Current?.MainWindow
            };

            if (selector.ShowDialog() != true || string.IsNullOrWhiteSpace(selector.SelectedCharacterName))
            {
                return null;
            }

            return selector.SelectedCharacterName.Trim();
        }

        private static bool CanUseSnapshotPuppet(
            string requestedPuppet,
            IReadOnlyDictionary<string, bool> serverAvailability,
            IReadOnlySet<string> reservedPuppets)
        {
            return !string.IsNullOrWhiteSpace(requestedPuppet)
                && serverAvailability.TryGetValue(requestedPuppet, out bool isAvailable)
                && isAvailable
                && !reservedPuppets.Contains(requestedPuppet)
                && CharacterFolder.FullList.Any(character =>
                    string.Equals(character.Name, requestedPuppet, StringComparison.OrdinalIgnoreCase));
        }

        private static SnapshotPuppetConflictReason GetSnapshotPuppetConflictReason(
            string requestedPuppet,
            IReadOnlyDictionary<string, bool> serverAvailability,
            IReadOnlySet<string> reservedPuppets)
        {
            if (string.IsNullOrWhiteSpace(requestedPuppet))
            {
                return SnapshotPuppetConflictReason.MissingPuppet;
            }

            if (!serverAvailability.TryGetValue(requestedPuppet, out bool isAvailable))
            {
                return SnapshotPuppetConflictReason.NotOnServer;
            }

            if (!isAvailable)
            {
                return SnapshotPuppetConflictReason.Taken;
            }

            if (reservedPuppets.Contains(requestedPuppet))
            {
                return SnapshotPuppetConflictReason.ReservedBySnapshot;
            }

            bool hasMatchingLocalFolder = CharacterFolder.FullList.Any(character =>
                string.Equals(character.Name, requestedPuppet, StringComparison.OrdinalIgnoreCase));
            return hasMatchingLocalFolder
                ? SnapshotPuppetConflictReason.Taken
                : SnapshotPuppetConflictReason.MissingLocal;
        }

        private SnapshotConflictDecision ShowSnapshotConflictDialog(
            GmMultiClientSnapshotClient state,
            string requestedPuppet,
            SnapshotPuppetConflictReason conflictReason)
        {
            Window? owner = HostWindow ?? Application.Current?.MainWindow;
            string clientName = string.IsNullOrWhiteSpace(state.ClientName) ? "Client" : state.ClientName.Trim();
            string message = BuildSnapshotConflictMessage(clientName, requestedPuppet, conflictReason);
            MessageBoxResult result = OceanyaMessageBox.Show(
                owner,
                message,
                "Snapshot INIPuppet Conflict",
                new[]
                {
                    new OceanyaMessageBoxButtonOption("Select INIPuppet", MessageBoxResult.Yes, isDefault: true),
                    new OceanyaMessageBoxButtonOption("Delete Client", MessageBoxResult.No, isCancel: true)
                },
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes
                ? SnapshotConflictDecision.SelectIniPuppet
                : SnapshotConflictDecision.Delete;
        }

        private static string BuildSnapshotConflictMessage(
            string clientName,
            string requestedPuppet,
            SnapshotPuppetConflictReason conflictReason)
        {
            string puppet = string.IsNullOrWhiteSpace(requestedPuppet) ? "an INIPuppet" : requestedPuppet.Trim();
            return conflictReason switch
            {
                SnapshotPuppetConflictReason.MissingPuppet =>
                    $"{clientName} did not have a saved INIPuppet. Select one or delete the client?",
                SnapshotPuppetConflictReason.NotOnServer =>
                    $"{clientName} was using {puppet} as INIPuppet, but this server does not have it. Select a different INIPuppet or delete the client?",
                SnapshotPuppetConflictReason.MissingLocal =>
                    $"{clientName} was using {puppet} as INIPuppet, but the matching local character folder is missing. Select a different INIPuppet or delete the client?",
                SnapshotPuppetConflictReason.ReservedBySnapshot =>
                    $"{clientName} was using {puppet} as INIPuppet, but another restored client already reserved it. Select a different INIPuppet or delete the client?",
                _ =>
                    $"{clientName} was using {puppet} as INIPuppet, but it is currently taken. Select a different INIPuppet or delete the client?"
            };
        }

        private AOClient? FindRestoredSelectedClient(
            GmMultiClientSnapshot snapshot,
            IReadOnlyList<GmMultiClientSnapshotClient> restoredStates)
        {
            if (clients.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SelectedClientName))
            {
                AOClient? byName = clientOrder.FirstOrDefault(client =>
                    clients.Values.Contains(client)
                    && string.Equals(client.clientName, snapshot.SelectedClientName, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    return byName;
                }
            }

            if (snapshot.SelectedClientIndex >= 0 && snapshot.SelectedClientIndex < restoredStates.Count)
            {
                string clientName = restoredStates[snapshot.SelectedClientIndex].ClientName;
                return clientOrder.FirstOrDefault(client =>
                    clients.Values.Contains(client)
                    && string.Equals(client.clientName, clientName, StringComparison.OrdinalIgnoreCase));
            }

            return clientOrder.FirstOrDefault(client => clients.Values.Contains(client));
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

                    AddLoggedIcMessageWithContext(
                        bot,
                        icMessage.ShowName,
                        icMessage.Message,
                        isSentFromSelf,
                        icMessage.TextColor,
                        icMessage);
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
        private async Task AddClientInternalAsync(
            string? clientName = null,
            GmMultiClientSnapshotClient? restoredState = null,
            bool suppressInitialCharacterPrompt = false,
            bool showWaitForm = true,
            AOClient? preconnectedClient = null)
        {
            IsEnabled = false;
            bool waitFormOpen = false;
            try
            {
            Window? waitOwner = HostWindow ?? Application.Current?.MainWindow;
            if (waitOwner == null) return;

            if (showWaitForm)
            {
                await WaitForm.ShowFormAsync("Connecting client...", waitOwner);
                waitFormOpen = true;
            }

            try
            {
                string defaultClientName = !string.IsNullOrWhiteSpace(restoredState?.ClientName)
                    ? restoredState.ClientName
                    : !string.IsNullOrWhiteSpace(clientName)
                    ? clientName
                    : $"Client{clients.Count + 1}";

                AOClient bot = preconnectedClient ?? new AOClient(Globals.GetSelectedServerEndpoint());
                bot.clientName = defaultClientName;
                HookClientForDreddOverlay(bot);
                if (aiModeEnabled)
                {
                    EnsureAiController(bot);
                    aiOriginResponseVisibility[bot] = true;
                }

                bool isNewInternalConnection = !useSingleInternalClient || singleInternalClient == null;

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

                bot.SetICShowname(bot.clientName);
                bot.OOCShowname = bot.clientName;
                bot.switchPosWhenChangingINI = chkPosOnIniSwap.IsChecked == true;
                bot.FrequencyHintsProvider = () => SaveFile.Data.FrequentlyUsedIniPuppets;
                if (restoredState != null)
                {
                    ApplySnapshotStateToClient(bot, restoredState);
                }

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
                ContextMenuSectionHelper.AddHeader(contextMenu, "Client", addLeadingSeparator: false);
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
                    CharacterFolder? previousCharacter = bot.currentINI;
                    Emote? previousEmote = bot.currentEmote;
                    string previousICShowname = bot.ICShowname;
                    string previousPosition = bot.curPos;

                    await targetNetworkClient.SelectFirstAvailableINIPuppet(false);
                    SetProfileIniPuppetName(bot, targetNetworkClient.iniPuppetName);
                    bot.SetCharacter(previousCharacter);
                    if (previousEmote != null)
                    {
                        bot.SetEmote(previousEmote.DisplayID);
                    }
                    bot.SetICShowname(previousICShowname);
                    bot.SetPos(previousPosition);

                    if (useSingleInternalClient)
                    {
                        SyncSingleClientStatusToProfile(bot);
                    }
                    if (ReferenceEquals(bot, currentClient))
                    {
                        ICMessageSettingsControl.SetClient(bot);
                    }
                };
                contextMenu.Items.Add(iniPuppetChange);

                MenuItem manualIniPuppetChange = new MenuItem { Header = "Select INIPuppet (Manual)" };
                manualIniPuppetChange.Click += async (sender, args) =>
                {
                    Window? owner = HostWindow ?? Application.Current?.MainWindow;
                    bool waitFormShown = false;
                    try
                    {
                        if (owner != null && !OceanyaTestMode.Current.DisableWaitForms)
                        {
                            await WaitForm.ShowFormAsync("Refreshing character list...", owner);
                            waitFormShown = true;
                        }

                        AOClient? networkClient = GetTargetClientForNetwork(bot);
                        if (networkClient != null)
                        {
                            WaitForm.SetSubtitle("Fetching from server...");
                            try
                            {
                                await networkClient.RequestFreshCharacterListAsync();
                            }
                            catch (Exception ex)
                            {
                                CustomConsole.Warning("Failed to refresh character list from server.", ex);
                            }
                        }

                        WaitForm.SetSubtitle("Building character list...");
                        await Task.Yield();

                        ClientCharacterSelectionResult result =
                            await ShowCharacterSelectorForClientSelectionAsync(bot, skipWaitForm: true);
                        if (result.Confirmed && !string.IsNullOrWhiteSpace(result.SelectedCharacterName))
                        {
                            _ = ApplyCharacterSelectionAsync(bot, result.SelectedCharacterName, preserveLocalCharacter: true);
                        }
                    }
                    finally
                    {
                        if (waitFormShown)
                        {
                            await WaitForm.CloseFormAsync();
                        }
                    }
                };
                contextMenu.Items.Add(manualIniPuppetChange);

                ContextMenuSectionHelper.AddHeader(contextMenu, "Order", addLeadingSeparator: true);
                MenuItem moveUpMenuItem = new MenuItem { Header = "Move UP" };
                moveUpMenuItem.Click += (sender, args) => MoveClient(bot, -1);
                contextMenu.Items.Add(moveUpMenuItem);

                MenuItem moveDownMenuItem = new MenuItem { Header = "Move DOWN" };
                moveDownMenuItem.Click += (sender, args) => MoveClient(bot, 1);
                contextMenu.Items.Add(moveDownMenuItem);

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

                ContextMenuSectionHelper.AddHeader(contextMenu, "Connection", addLeadingSeparator: true);
                MenuItem disconnectMenuItem = new MenuItem { Header = "Disconnect" };
                disconnectMenuItem.Click += async (sender, args) =>
                {
                    CustomConsole.Info(
                        $"Disconnect requested from client context menu. profile=\"{bot.clientName}\"",
                        CustomConsole.LogCategory.System);
                    await RemoveClientAsync(bot);
                };
                contextMenu.Items.Add(disconnectMenuItem);

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
                            imageFactory.SetValue(Image.SourceProperty, BitmapFileLoader.LoadFrozen(normalImagePath));

                            gridFactory.AppendChild(imageFactory);
                            template.VisualTree = gridFactory;

                            // Add the trigger for toggled state (darken image)
                            Trigger trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                            trigger.Setters.Add(new Setter
                            {
                                Property = Image.SourceProperty,
                                TargetName = "ButtonImage",
                                Value = BitmapFileLoader.LoadFrozen(selectedImagePath)
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
                    if (preconnectedClient == null)
                    {
                        await ConnectClientAsync(bot, autoSelectCharacter: false);
                    }
                    await BootstrapAreaNavigatorAsync(bot);
                    if (restoredState != null && !string.IsNullOrWhiteSpace(restoredState.IniPuppetName))
                    {
                        await bot.SelectIniPuppet(restoredState.IniPuppetName, false);
                    }
                }

                if (isNewInternalConnection && !suppressInitialCharacterPrompt)
                {
                    if (showWaitForm && waitFormOpen)
                    {
                        await WaitForm.CloseFormAsync();
                        waitFormOpen = false;
                    }

                    ClientCharacterSelectionResult selection = await ShowCharacterSelectorForClientSelectionAsync(bot, defaultClientName);
                    if (!selection.Confirmed || string.IsNullOrWhiteSpace(selection.SelectedCharacterName))
                    {
                        // User cancelled — tear down whatever connection was just established
                        if (useSingleInternalClient)
                        {
                            if (singleInternalClient != null)
                            {
                                await singleInternalClient.Disconnect();
                                singleInternalClient = null;
                            }
                            boundSingleClientProfile = null;
                        }
                        else
                        {
                            await bot.Disconnect();
                        }
                        ClearAiClientState(bot);
                        if (aiControllers.Remove(bot, out AOClientAgentController? controller))
                        {
                            controller.Dispose();
                        }
                        return;
                    }

                    if (showWaitForm)
                    {
                        await WaitForm.ShowFormAsync("Loading client UI...", waitOwner);
                        waitFormOpen = true;
                        WaitForm.SetSubtitle("Applying character selection...");
                    }

                    await ApplyCharacterSelectionAsync(bot, selection.SelectedCharacterName);

                    // Apply name the user typed in the selector (falls back to default if empty)
                    if (!string.IsNullOrWhiteSpace(selection.SelectedClientName))
                    {
                        bot.clientName = selection.SelectedClientName;
                        bot.SetICShowname(bot.clientName);
                        bot.OOCShowname = bot.clientName;
                        AutomationProperties.SetName(toggleBtn, bot.clientName);
                    }
                }
                else if (restoredState != null)
                {
                    AutomationProperties.SetName(toggleBtn, bot.clientName);
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
                        viewportContent?.RemoveClient(bot);
                        profileIniPuppetNames.Remove(bot);
                        ClearAiClientState(bot);
                        if (aiControllers.Remove(bot, out AOClientAgentController? controller))
                        {
                            controller.Dispose();
                        }

                        clients.Remove(button);
                        clientOrder.Remove(bot);
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
                        CaptureGmMultiClientSnapshot();
                    }
                    else
                    {
                            var newClient = clientOrder.FirstOrDefault(client => clients.Values.Contains(client));
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
                clientOrder.Add(bot);

                EmoteGrid.AddElement(toggleBtn);

                toggleBtn.IsChecked = true;
                UpdateClientTooltip(bot);
                CaptureGmMultiClientSnapshot();

                if (showWaitForm && waitFormOpen)
                {
                    WaitForm.SetSubtitle("Finishing client UI...");
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                }

                if (clients.Count == 1)
                {
                    OOCLogControl.IsEnabled = true;
                    ICLogControl.IsEnabled = true;
                    ICMessageSettingsControl.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                if (showWaitForm && waitFormOpen)
                {
                    await WaitForm.CloseFormAsync();
                    waitFormOpen = false;
                }
                ShowMainWindowMessage($"Error connecting client: {ex.Message}", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (showWaitForm && waitFormOpen)
                {
                    await WaitForm.CloseFormAsync();
                    waitFormOpen = false;
                }
            }

            } // end outer try
            finally
            {
                IsEnabled = true;
            }
        }

        private readonly struct ClientCharacterSelectionResult
        {
            public static readonly ClientCharacterSelectionResult Cancelled = new ClientCharacterSelectionResult(false, null, null);

            public ClientCharacterSelectionResult(bool confirmed, string? selectedCharacterName, string? selectedClientName)
            {
                Confirmed = confirmed;
                SelectedCharacterName = selectedCharacterName;
                SelectedClientName = selectedClientName;
            }

            public bool Confirmed { get; }
            public string? SelectedCharacterName { get; }
            public string? SelectedClientName { get; }
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
            }

            currentClient = client;
            ICMessageSettingsControl.SetClient(currentClient);
            OOCLogControl.SetCurrentClient(currentClient);
            SetOocShownameTextForCurrentClient(currentClient.OOCShowname);
            ICLogControl.SetCurrentClient(currentClient);
            RefreshViewportAttachment();
            RefreshAreaNavigatorForCurrentClient();
            RefreshMusicListForCurrentClient();

            if (isDreddFeatureEnabled && DreddStickyOverlayCheckBox.IsChecked == true)
            {
                ApplyStoredDreddStickyOverlay(showFeedbackOnFailure: false);
            }

            RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: true);
            UpdateDreddFeatureEnabledState();
        }

        private void btnAddClient_Click(object sender, RoutedEventArgs e)
        {
            AddClient();
        }

        private void btnViewport_Click(object sender, RoutedEventArgs e)
        {
            if (viewportWindow?.IsVisible == true)
            {
                DisablePictureInPictureViewportMode();
                CaptureViewportWindowState();
                if (!OceanyaTestMode.Current.DisableViewportWindowPersistence)
                {
                    SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
                    SaveFile.Data.GMViewportWindowState.IsVisible = false;
                    SaveFile.Save();
                }
                viewportWindow.Hide();
                ApplyViewportTaskbarPriority();
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
                RefreshViewportAttachment();
                NormalizeVisibleViewportWindowSize(preferWidth: false);
                ShowViewportWindowAfterRestore();
                return;
            }

            RefreshViewportAttachment();

            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            ViewportWindowState savedState = SaveFile.Data.GMViewportWindowState ?? new ViewportWindowState();
            (double initialWidth, double initialHeight, double? restoredLeft, double? restoredTop) =
                ResolveViewportWindowRestoreState(savedState);
            (double restoreContentWidth, double restoreContentHeight) = ResolveViewportContentRestoreSize(savedState);
            viewportContent!.SetCurrentValue(FrameworkElement.WidthProperty, restoreContentWidth);
            viewportContent.SetCurrentValue(FrameworkElement.HeightProperty, restoreContentHeight);

            viewportWindow = OceanyaWindowManager.CreateWindow(
                viewportContent!,
                new OceanyaWindowPresentationOptions
                {
                    Owner = null,
                    Title = "AO2 Viewport",
                    HeaderText = "Viewport",
                    Width = initialWidth,
                    Height = initialHeight,
                    MinWidth = GetViewportMinimumWindowWidth(),
                    MinHeight = GetViewportMinimumWindowHeight(),
                    ShowInTaskbar = false,
                    IsUserResizeEnabled = true,
                    IsUserMoveEnabled = true,
                    IsCloseButtonVisible = true,
                    WindowStartupLocation = WindowStartupLocation.Manual
                });
            RememberViewportWindowSize(viewportWindow.Width, viewportWindow.Height);

            if (restoredLeft.HasValue && restoredTop.HasValue)
            {
                viewportWindow.Left = restoredLeft.Value;
                viewportWindow.Top = restoredTop.Value;
            }
            else
            {
                CenterViewportWindowNearOwner(owner, initialWidth, initialHeight);
            }

            viewportWindow.SourceInitialized += ViewportWindow_SourceInitialized;
            viewportWindow.Activated += ViewportWindow_Activated;
            viewportWindow.PreviewKeyDown += ViewportWindow_PreviewKeyDown;
            viewportWindow.TextInput += ViewportWindow_TextInput;
            viewportWindow.SizeChanged += ViewportWindow_SizeChanged;
            viewportWindow.LocationChanged += ViewportWindow_LocationChanged;
            viewportWindow.Closing += (sender, eventArgs) =>
            {
                if (isMainWindowClosing)
                {
                    return;
                }

                eventArgs.Cancel = true;
                DisablePictureInPictureViewportMode();
                CaptureViewportWindowState();
                if (!OceanyaTestMode.Current.DisableViewportWindowPersistence)
                {
                    SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
                    SaveFile.Data.GMViewportWindowState.IsVisible = false;
                    SaveFile.Save();
                }
                viewportWindow?.Hide();
                ApplyViewportTaskbarPriority();
            };
            viewportWindow.Closed += (_, _) =>
            {
                viewportWindow.SourceInitialized -= ViewportWindow_SourceInitialized;
                viewportWindow.Activated -= ViewportWindow_Activated;
                viewportWindow.PreviewKeyDown -= ViewportWindow_PreviewKeyDown;
                viewportWindow.TextInput -= ViewportWindow_TextInput;
                viewportWindow.SizeChanged -= ViewportWindow_SizeChanged;
                viewportWindow.LocationChanged -= ViewportWindow_LocationChanged;
                viewportWindowSource?.RemoveHook(ViewportWindow_WndProc);
                viewportWindowSource = null;
                TeardownViewportSynchronizedMove();
                TeardownViewportWindowStateSync();
                lastViewportWindowWidth = null;
                lastViewportWindowHeight = null;
                viewportWindow = null;
            };
            ShowViewportWindowAfterRestore();
        }

        private void ShowViewportWindowAfterRestore()
        {
            if (viewportWindow == null)
            {
                return;
            }

            isRestoringViewportWindow = true;
            viewportWindow.Opacity = 0;
            if (!viewportWindow.IsVisible)
            {
                viewportWindow.Show();
            }

            ApplyViewportTaskbarPriority();
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        NormalizeVisibleViewportWindowSize(preferWidth: false);
                        if (!OceanyaTestMode.Current.DisableViewportWindowPersistence)
                        {
                            SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
                            SaveFile.Data.GMViewportWindowState.IsVisible = true;
                            SaveFile.Save();
                        }
                    }
                    finally
                    {
                        isRestoringViewportWindow = false;
                        if (viewportWindow != null)
                        {
                            viewportWindow.Opacity = 1;
                        }
                        if (isPictureInPictureViewportEnabled)
                        {
                            OpenPictureInPictureViewportWindow();
                        }
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OpenSettingsWindow(SettingsWindowPage initialPage = SettingsWindowPage.Audio)
        {
            if (settingsWindow != null)
            {
                settingsContent?.SelectPage(initialPage);
                settingsWindow.Activate();
                return;
            }

            SettingsWindow content = new SettingsWindow(initialPage);
            settingsContent = content;
            content.SettingsSaved += ApplySavedClientSettingsToRuntime;
            content.SettingsSaved += ao2TextLogWriter.RefreshSession;
            Action liveVolumeRefresh = () =>
            {
                viewportContent?.RefreshVolumes();
                mainMusicAudioManager.RefreshVolumes();
            };
            content.VolumeLiveChanged += liveVolumeRefresh;
            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            settingsWindow = OceanyaWindowManager.CreateWindow(
                content,
                new OceanyaWindowPresentationOptions
                {
                    Owner = owner,
                    Title = "Settings",
                    HeaderText = "Settings",
                    Width = 860,
                    Height = 620,
                    MinWidth = 720,
                    MinHeight = 520,
                    ShowInTaskbar = false,
                    IsUserResizeEnabled = true,
                    IsUserMoveEnabled = true,
                    IsCloseButtonVisible = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
            settingsWindow.Closed += (_, _) =>
            {
                content.SettingsSaved -= ApplySavedClientSettingsToRuntime;
                content.SettingsSaved -= ao2TextLogWriter.RefreshSession;
                content.VolumeLiveChanged -= liveVolumeRefresh;
                settingsWindow = null;
                if (ReferenceEquals(settingsContent, content))
                {
                    settingsContent = null;
                }
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
            foreach (AOClient client in clientOrder.Where(client => clients.Values.Contains(client)))
            {
                client.switchPosWhenChangingINI = SaveFile.Data.SwitchPosOnIniSwap;
            }

            ICLogControl.SetInvertOnClientLogs(SaveFile.Data.InvertICLog);
            viewportContent?.RefreshVolumes();
            if (viewportContent != null)
            {
                viewportContent.UseAsWindowsPreview = SaveFile.Data.GMViewportWindowPreviewPriority;
                viewportContent.ChatboxOverlapsViewport = SaveFile.Data.GMViewportChatboxOverlapsViewport;
                viewportContent.ReloadThemeLayout();
            }

            mainMusicAudioManager.RefreshVolumes();
        }

        private void RefreshViewportAttachment()
        {
            EnsureViewportContent();
            if (viewportContent == null)
            {
                return;
            }

            foreach (AOClient client in clientOrder.Where(client => clients.Values.Contains(client)))
            {
                AOClient? incomingMessageClient = GetTargetClientForNetwork(client) ?? client;
                viewportContent.EnsureClient(
                    client,
                    incomingMessageClient,
                    CreateViewportMessageFilter(client),
                    CreateViewportActionFilter(client));
            }

            AOClient? currentIncomingMessageClient = GetTargetClientForNetwork(currentClient) ?? currentClient;
            viewportContent.AttachClient(
                currentClient,
                currentIncomingMessageClient,
                currentClient == null ? null : CreateViewportMessageFilter(currentClient),
                currentClient == null ? null : CreateViewportActionFilter(currentClient));

            if (pictureInPictureViewportWindow?.IsVisible == true)
            {
                RefreshPictureInPictureViewportAttachment();
            }
        }

        private void EnsureViewportContent()
        {
            if (viewportContent != null)
            {
                return;
            }

            viewportContent = new AO2ViewportWindowContent();
            viewportContent.UseAsWindowsPreviewChanged += (_, _) => ApplyViewportTaskbarPriority();
            viewportContent.PictureInPictureViewportChanged += (_, _) => ApplyPictureInPictureViewportMode();
            viewportContent.ViewportSurfaceLayoutChanged += (_, _) => OnViewportSurfaceLayoutChanged();
            viewportContent.ChangeViewportThemeRequested += () => OpenSettingsWindow(SettingsWindowPage.Viewport);
            viewportContent.NewCharacterFolderRequested += OpenNewCharacterInEditorAsync;
            viewportContent.OpenCharacterInEditorRequested += OpenCharacterInEditorAsync;
            viewportContent.DuplicateCharacterInEditorRequested += DuplicateCharacterInEditorAsync;
            viewportContent.OpenCharacterInFolderVisualizerRequested += OpenCharacterInFolderVisualizer;
            viewportContent.DeleteCharacterFolderRequested += DeleteCharacterFolderFromContextAsync;
            viewportContent.RefreshCharacterRequested += characterName =>
                RefreshCharacterAssetsAsync(characterName, refreshAllCharacters: false, refreshAllAssets: false);
            viewportContent.RefreshAllAssetsRequested += () =>
                RefreshCharacterAssetsAsync(null, refreshAllCharacters: false, refreshAllAssets: true);
            viewportContent.RefreshAllCharactersRequested += () =>
                RefreshCharacterAssetsAsync(null, refreshAllCharacters: true, refreshAllAssets: false);
            viewportContent.RefreshBackgroundRequested += RefreshBackgroundAssetsAsync;
        }

        private void EnsurePictureInPictureViewportContent()
        {
            if (pictureInPictureViewportContent != null)
            {
                return;
            }

            pictureInPictureViewportContent = new AO2ViewportWindowContent();
            pictureInPictureViewportContent.IsViewportContextMenuEnabled = false;
            pictureInPictureViewportContent.RenderAudioEnabled = false;
            pictureInPictureViewportContent.ViewportSurfaceLayoutChanged += (_, _) => OnPictureInPictureViewportSurfaceLayoutChanged();
        }

        private void ApplyPictureInPictureViewportMode()
        {
            if (isUpdatingPictureInPictureViewportToggle)
            {
                return;
            }

            isPictureInPictureViewportEnabled = viewportContent?.PictureInPictureViewport == true;
            SyncPictureInPictureViewportMenuToggle();
            if (isPictureInPictureViewportEnabled)
            {
                OpenPictureInPictureViewportWindow();
                return;
            }

            ClosePictureInPictureViewportWindow();
        }

        private void DisablePictureInPictureViewportMode()
        {
            if (!isPictureInPictureViewportEnabled && pictureInPictureViewportWindow == null)
            {
                return;
            }

            SetPictureInPictureViewportEnabled(false);
            ClosePictureInPictureViewportWindow();
        }

        private void SetPictureInPictureViewportEnabled(bool enabled)
        {
            isPictureInPictureViewportEnabled = enabled;
            SyncPictureInPictureViewportMenuToggle();
        }

        private void OpenPictureInPictureViewportWindow()
        {
            if (viewportWindow?.IsVisible != true || isMainWindowClosing)
            {
                DisablePictureInPictureViewportMode();
                return;
            }

            EnsurePictureInPictureViewportContent();
            ICMessage? lastViewportMessage = viewportContent?.GetActiveLastRenderedMessage();
            RefreshPictureInPictureViewportAttachment();
            if (lastViewportMessage != null)
            {
                pictureInPictureViewportContent?.ReplayMessageForActiveClient(lastViewportMessage);
                LogPictureInPictureViewport("render-state replayed");
            }
            if (pictureInPictureViewportWindow != null)
            {
                ShowPictureInPictureViewportWindowAfterRestore();
                return;
            }

            ViewportWindowState savedState = SaveFile.Data.GMPictureInPictureViewportState ?? new ViewportWindowState
            {
                Width = 192,
                Height = 222
            };
            (double initialWidth, double initialHeight, double? restoredLeft, double? restoredTop) =
                ResolveViewportWindowRestoreState(savedState);
            (initialWidth, initialHeight) = NormalizeViewportWindowSize(initialWidth, initialHeight, preferWidth: false);
            SetPictureInPictureViewportRestoreTarget(initialWidth, initialHeight, restoredLeft, restoredTop);
            (double restoreContentWidth, double restoreContentHeight) = ResolveViewportContentRestoreSize(savedState);
            pictureInPictureViewportContent!.SetCurrentValue(FrameworkElement.WidthProperty, restoreContentWidth);
            pictureInPictureViewportContent.SetCurrentValue(FrameworkElement.HeightProperty, restoreContentHeight);

            pictureInPictureViewportWindow = OceanyaWindowManager.CreateWindow(
                pictureInPictureViewportContent,
                new OceanyaWindowPresentationOptions
                {
                    Owner = null,
                    Title = "AO2 Viewport PiP",
                    HeaderText = "Viewport",
                    Width = initialWidth,
                    Height = initialHeight,
                    MinWidth = GetViewportMinimumWindowWidth(),
                    MinHeight = GetViewportMinimumWindowHeight(),
                    ShowInTaskbar = false,
                    IsUserResizeEnabled = true,
                    IsUserMoveEnabled = true,
                    IsCloseButtonVisible = true,
                    WindowStartupLocation = WindowStartupLocation.Manual
                });
            RememberPictureInPictureViewportWindowSize(
                pictureInPictureViewportWindow.Width,
                pictureInPictureViewportWindow.Height);
            pictureInPictureViewportWindow.Topmost = true;
            pictureInPictureViewportWindow.ShowActivated = false;
            pictureInPictureViewportWindow.Opacity = 0;

            if (restoredLeft.HasValue && restoredTop.HasValue)
            {
                pictureInPictureViewportWindow.Left = restoredLeft.Value;
                pictureInPictureViewportWindow.Top = restoredTop.Value;
            }
            else
            {
                Rect workArea = SystemParameters.WorkArea;
                pictureInPictureViewportWindow.Left = workArea.Right - initialWidth - 24;
                pictureInPictureViewportWindow.Top = workArea.Bottom - initialHeight - 48;
            }

            pictureInPictureViewportWindow.SourceInitialized += PictureInPictureViewportWindow_SourceInitialized;
            pictureInPictureViewportWindow.SizeChanged += PictureInPictureViewportWindow_SizeChanged;
            pictureInPictureViewportWindow.LocationChanged += PictureInPictureViewportWindow_LocationChanged;
            pictureInPictureViewportWindow.Closing += (_, eventArgs) =>
            {
                if (isMainWindowClosing || isClosingPictureInPictureViewportWindow)
                {
                    return;
                }

                CapturePictureInPictureViewportWindowState("close");
                SetPictureInPictureViewportEnabled(false);
                LogPictureInPictureViewport("closed by user");
            };
            pictureInPictureViewportWindow.Closed += (_, _) =>
            {
                pictureInPictureViewportWindow.SourceInitialized -= PictureInPictureViewportWindow_SourceInitialized;
                pictureInPictureViewportWindow.SizeChanged -= PictureInPictureViewportWindow_SizeChanged;
                pictureInPictureViewportWindow.LocationChanged -= PictureInPictureViewportWindow_LocationChanged;
                pictureInPictureViewportWindowSource?.RemoveHook(PictureInPictureViewportWindow_WndProc);
                pictureInPictureViewportWindowSource = null;
                pictureInPictureViewportContent?.AttachClient(null, null);
                pictureInPictureViewportContent = null;
                pictureInPictureViewportWindow = null;
                lastPictureInPictureViewportWindowWidth = null;
                lastPictureInPictureViewportWindowHeight = null;
                ClearPictureInPictureViewportRestoreTarget();
                isClosingPictureInPictureViewportWindow = false;
            };

            LogPictureInPictureViewport("created");
            ShowPictureInPictureViewportWindowAfterRestore();
        }

        private void ShowPictureInPictureViewportWindowAfterRestore()
        {
            if (pictureInPictureViewportWindow == null)
            {
                return;
            }

            isRestoringPictureInPictureViewportWindow = true;
            pictureInPictureViewportWindow.Opacity = 0;
            if (!pictureInPictureViewportWindow.IsVisible)
            {
                pictureInPictureViewportWindow.Show();
            }

            pictureInPictureViewportWindow.Topmost = true;
            LogPictureInPictureViewport("shown");
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (pictureInPictureViewportWindow != null && pictureInPictureViewportWindow.WindowState == WindowState.Normal)
                    {
                        (double desiredWidth, double desiredHeight) =
                            ResolvePictureInPictureViewportRestoreOrCurrentSize(preferWidth: false);
                        pictureInPictureViewportWindow.Width = desiredWidth;
                        pictureInPictureViewportWindow.Height = desiredHeight;
                        if (pendingPictureInPictureViewportRestoreLeft.HasValue)
                        {
                            pictureInPictureViewportWindow.Left = pendingPictureInPictureViewportRestoreLeft.Value;
                        }

                        if (pendingPictureInPictureViewportRestoreTop.HasValue)
                        {
                            pictureInPictureViewportWindow.Top = pendingPictureInPictureViewportRestoreTop.Value;
                        }

                        RememberPictureInPictureViewportWindowSize(desiredWidth, desiredHeight);
                        pictureInPictureViewportWindow.Opacity = 1;
                    }
                    isRestoringPictureInPictureViewportWindow = false;
                    ClearPictureInPictureViewportRestoreTarget();
                }),
                DispatcherPriority.Loaded);
        }

        private void ClosePictureInPictureViewportWindow()
        {
            if (pictureInPictureViewportWindow == null)
            {
                pictureInPictureViewportContent?.AttachClient(null, null);
                pictureInPictureViewportContent = null;
                return;
            }

            CapturePictureInPictureViewportWindowState("close");
            LogPictureInPictureViewport("closing");
            isClosingPictureInPictureViewportWindow = true;
            pictureInPictureViewportWindow.Close();
        }

        private void RefreshPictureInPictureViewportAttachment()
        {
            EnsurePictureInPictureViewportContent();
            if (pictureInPictureViewportContent == null)
            {
                return;
            }

            foreach (AOClient client in clientOrder.Where(client => clients.Values.Contains(client)))
            {
                AOClient? incomingMessageClient = GetTargetClientForNetwork(client) ?? client;
                pictureInPictureViewportContent.EnsureClient(
                    client,
                    incomingMessageClient,
                    CreateViewportMessageFilter(client),
                    CreateViewportActionFilter(client));
            }

            AOClient? currentIncomingMessageClient = GetTargetClientForNetwork(currentClient) ?? currentClient;
            pictureInPictureViewportContent.AttachClient(
                currentClient,
                currentIncomingMessageClient,
                currentClient == null ? null : CreateViewportMessageFilter(currentClient),
                currentClient == null ? null : CreateViewportActionFilter(currentClient));
            LogPictureInPictureViewport("render-state attached");
        }

        private void PictureInPictureViewportWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (isRestoringPictureInPictureViewportWindow)
            {
                return;
            }

            RememberPictureInPictureViewportWindowSize(e.NewSize.Width, e.NewSize.Height);
            CapturePictureInPictureViewportWindowState("resize");
        }

        private void PictureInPictureViewportWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (isRestoringPictureInPictureViewportWindow)
            {
                return;
            }

            CapturePictureInPictureViewportWindowState("move");
        }

        private void PictureInPictureViewportWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (pictureInPictureViewportWindow == null)
            {
                return;
            }

            pictureInPictureViewportWindowSource = HwndSource.FromHwnd(new WindowInteropHelper(pictureInPictureViewportWindow).Handle);
            pictureInPictureViewportWindowSource?.AddHook(PictureInPictureViewportWindow_WndProc);
            ApplyPictureInPictureNoActivateStyle();
        }

        private IntPtr PictureInPictureViewportWindow_WndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmGetMinMaxInfo)
            {
                ApplyViewportMinMaxInfo(lParam, pictureInPictureViewportWindow);
                handled = true;
            }
            else if (message == WmSizing)
            {
                ApplyViewportSizingRect((int)wParam, lParam, pictureInPictureViewportWindow);
                handled = true;
            }
            else if (message == WmMouseActivate)
            {
                handled = true;
                return new IntPtr(MaNoActivate);
            }
            else if (message == WmExitSizeMove)
            {
                Dispatcher.BeginInvoke(
                    new Action(() => CapturePictureInPictureViewportWindowState("exit size/move")),
                    DispatcherPriority.Background);
            }

            return IntPtr.Zero;
        }

        private void SyncPictureInPictureViewportMenuToggle()
        {
            if (viewportContent == null || viewportContent.PictureInPictureViewport == isPictureInPictureViewportEnabled)
            {
                return;
            }

            isUpdatingPictureInPictureViewportToggle = true;
            try
            {
                viewportContent.PictureInPictureViewport = isPictureInPictureViewportEnabled;
            }
            finally
            {
                isUpdatingPictureInPictureViewportToggle = false;
            }
        }

        private void ApplyPictureInPictureNoActivateStyle()
        {
            if (pictureInPictureViewportWindow == null)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(pictureInPictureViewportWindow).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            const int GWL_EXSTYLE = -20;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_APPWINDOW = 0x00040000;
            const int WS_EX_NOACTIVATE = 0x08000000;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newExStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
            if (newExStyle != exStyle)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, newExStyle);
            }
        }

        private void LogPictureInPictureViewport(string action)
        {
            CustomConsole.Debug(
                $"[VPT-PIP] {action}; visible={pictureInPictureViewportWindow?.IsVisible == true} bounds={DescribeViewportWindowState(SaveFile.Data.GMPictureInPictureViewportState)}",
                CustomConsole.LogCategory.Viewport);
        }

        private void OnViewportSurfaceLayoutChanged()
        {
            if (viewportWindow != null && viewportContent != null)
            {
                viewportWindow.MinWidth = GetViewportMinimumWindowWidth();
                viewportWindow.MinHeight = GetViewportMinimumWindowHeight();
                if (ShouldResizeViewportToNativeSurface(
                        SaveFile.Data.GMViewportWindowState,
                        viewportContent.CurrentSurfaceWidth,
                        viewportContent.CurrentSurfaceHeight))
                {
                    ResizeViewportWindowToContent(
                        viewportContent.CurrentSurfaceWidth,
                        viewportContent.CurrentSurfaceHeight);
                    return;
                }

                NormalizeVisibleViewportWindowSize(preferWidth: false);
            }
        }

        private void OnPictureInPictureViewportSurfaceLayoutChanged()
        {
            if (pictureInPictureViewportWindow == null || pictureInPictureViewportWindow.WindowState != WindowState.Normal)
            {
                return;
            }

            pictureInPictureViewportWindow.MinWidth = GetViewportMinimumWindowWidth();
            pictureInPictureViewportWindow.MinHeight = GetViewportMinimumWindowHeight();
            (double desiredWidth, double desiredHeight) =
                ResolvePictureInPictureViewportRestoreOrCurrentSize(preferWidth: false);
            pictureInPictureViewportWindow.Width = desiredWidth;
            pictureInPictureViewportWindow.Height = desiredHeight;
            if (!isRestoringPictureInPictureViewportWindow)
            {
                RememberPictureInPictureViewportWindowSize(desiredWidth, desiredHeight);
            }
        }

        private (double Width, double Height) ResolvePictureInPictureViewportRestoreOrCurrentSize(bool preferWidth)
        {
            if (pendingPictureInPictureViewportRestoreWidth.HasValue
                && pendingPictureInPictureViewportRestoreHeight.HasValue)
            {
                return NormalizeViewportWindowSize(
                    pendingPictureInPictureViewportRestoreWidth.Value,
                    pendingPictureInPictureViewportRestoreHeight.Value,
                    preferWidth);
            }

            if (pictureInPictureViewportWindow == null)
            {
                return (
                    GetViewportWindowWidthFromContentWidth(AO2ViewportAssetResolver.ViewportToolWidth),
                    GetViewportWindowHeightFromContentHeight(AO2ViewportAssetResolver.ViewportToolHeight));
            }

            return NormalizeViewportWindowSize(
                ResolveCapturedWindowWidth(pictureInPictureViewportWindow, lastPictureInPictureViewportWindowWidth),
                ResolveCapturedWindowHeight(pictureInPictureViewportWindow, lastPictureInPictureViewportWindowHeight),
                preferWidth);
        }

        private void SetPictureInPictureViewportRestoreTarget(
            double width,
            double height,
            double? left,
            double? top)
        {
            pendingPictureInPictureViewportRestoreWidth = IsFinite(width) && width > 0 ? width : null;
            pendingPictureInPictureViewportRestoreHeight = IsFinite(height) && height > 0 ? height : null;
            pendingPictureInPictureViewportRestoreLeft = left.HasValue && IsFinite(left.Value) ? left.Value : null;
            pendingPictureInPictureViewportRestoreTop = top.HasValue && IsFinite(top.Value) ? top.Value : null;
        }

        private void ClearPictureInPictureViewportRestoreTarget()
        {
            pendingPictureInPictureViewportRestoreWidth = null;
            pendingPictureInPictureViewportRestoreHeight = null;
            pendingPictureInPictureViewportRestoreLeft = null;
            pendingPictureInPictureViewportRestoreTop = null;
        }

        private void EnsureViewportVisibleWithMainWindow()
        {
            if (SaveFile.Data.GMViewportWindowState?.IsVisible != true || isMainWindowClosing)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (viewportWindow == null)
                    {
                        OpenViewportWindow();
                        return;
                    }

                    if (!viewportWindow.IsVisible)
                    {
                        viewportWindow.Show();
                    }

                    if (viewportWindow.WindowState == WindowState.Minimized)
                    {
                        viewportWindow.WindowState = WindowState.Normal;
                    }

                    SetViewportBesideMainWindowInZOrder();
                }),
                DispatcherPriority.Background);
        }

        private void SetViewportBesideMainWindowInZOrder()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (hostWindow == null || viewportWindow == null)
            {
                return;
            }

            IntPtr hostHwnd = new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = new WindowInteropHelper(viewportWindow).Handle;
            if (hostHwnd == IntPtr.Zero || viewportHwnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(viewportHwnd, hostHwnd, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        }

        private Func<ICMessage, bool>? CreateViewportMessageFilter(AOClient profileClient)
        {
            if (!useSingleInternalClient)
            {
                return null;
            }

            return message =>
                IsViewportMessageForProfile(profileClient, message) ||
                clientOrder.All(c => !IsViewportMessageForProfile(c, message));
        }

        private Func<string, bool>? CreateViewportActionFilter(AOClient profileClient)
        {
            if (!useSingleInternalClient)
            {
                return null;
            }

            return showName =>
            {
                if (string.IsNullOrWhiteSpace(profileClient.ICShowname))
                    return true;
                if (string.Equals(showName?.Trim(), profileClient.ICShowname.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                // Pass through if no profile claims this showname — it's from another real player.
                return clientOrder.All(c =>
                    string.IsNullOrWhiteSpace(c.ICShowname) ||
                    !string.Equals(showName?.Trim(), c.ICShowname?.Trim(), StringComparison.OrdinalIgnoreCase));
            };
        }

        private bool IsViewportMessageForProfile(AOClient profileClient, ICMessage message)
        {
            if (message.CharId >= 0 && profileClient.iniPuppetID >= 0 && message.CharId == profileClient.iniPuppetID)
            {
                return true;
            }

            bool sameCharacter = string.Equals(
                message.Character?.Trim(),
                profileClient.currentINI?.Name?.Trim(),
                StringComparison.OrdinalIgnoreCase);
            bool sameShowname = string.Equals(
                message.ShowName?.Trim(),
                profileClient.ICShowname?.Trim(),
                StringComparison.OrdinalIgnoreCase);
            return sameCharacter && sameShowname;
        }

        private void ViewportWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (viewportWindow == null)
            {
                return;
            }

            viewportWindowSource = HwndSource.FromHwnd(new WindowInteropHelper(viewportWindow).Handle);
            viewportWindowSource?.AddHook(ViewportWindow_WndProc);
            SetupViewportSynchronizedMove();
            SetupViewportWindowStateSync();
        }

        private void ViewportWindow_Activated(object? sender, EventArgs e)
        {
            if (IsViewportUsingWindowsPreview())
            {
                RestoreMainWindowVisualForViewportReturn();
                viewportPreviewInputProxyActive = true;
                UpdateViewportPreviewProxyVisual("viewport activated");
                EnsureViewportIsForegroundShellRepresentative("viewport activated");
                LogViewportPreviewState("viewport activated; main visual restored and input proxy active");
            }
        }

        private void ViewportWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RememberViewportWindowSize(e.NewSize.Width, e.NewSize.Height);
            CaptureViewportWindowState();
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

        private void ResizeViewportWindowToContent(double contentWidth, double contentHeight)
        {
            if (viewportWindow == null || viewportWindow.WindowState != WindowState.Normal)
            {
                return;
            }

            (double width, double height) = NormalizeViewportContentSize(contentWidth, contentHeight);
            viewportWindow.Width = GetViewportWindowWidthFromContentWidth(width);
            viewportWindow.Height = GetViewportWindowHeightFromContentHeight(height);
            RememberViewportWindowSize(viewportWindow.Width, viewportWindow.Height);
        }

        private IntPtr ViewportWindow_WndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            MarkViewportAltTabKeyIfNeeded(message, wParam);

            if (message == WmGetMinMaxInfo)
            {
                ApplyViewportMinMaxInfo(lParam, viewportWindow);
                handled = true;
            }
            else if (message == WmSizing)
            {
                ApplyViewportSizingRect((int)wParam, lParam, viewportWindow);
                handled = true;
            }
            else if (message == WmSize)
            {
                CaptureViewportWindowState();
            }
            else if (message == WmExitSizeMove)
            {
                Dispatcher.BeginInvoke(new Action(CaptureViewportWindowState), DispatcherPriority.Background);
            }
            else if (message == WmMouseActivate && IsViewportUsingWindowsPreview())
            {
                StopViewportAltTabFocusRedirectTimer();
                viewportPreviewInputProxyActive = true;
                UpdateViewportPreviewProxyVisual("viewport mouse activation");
                LogViewportPreviewState("viewport WM_MOUSEACTIVATE default activation");
            }

            return IntPtr.Zero;
        }

        private void SetupViewportSynchronizedMove()
        {
            Window hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (viewportWindow is GenericOceanyaWindow viewportGenericWindow)
            {
                viewportGenericWindow.SynchronizedMovePartner = hostWindow;
            }

            if (hostWindow is GenericOceanyaWindow hostGenericWindow)
            {
                hostGenericWindow.SynchronizedMovePartner = viewportWindow;
            }
        }

        private void TeardownViewportSynchronizedMove()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (hostWindow is GenericOceanyaWindow hostGenericWindow)
            {
                hostGenericWindow.SynchronizedMovePartner = null;
            }

            if (viewportWindow is GenericOceanyaWindow viewportGenericWindow)
            {
                viewportGenericWindow.SynchronizedMovePartner = null;
            }
        }

        private void SetupViewportWindowStateSync()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (viewportWindow != null)
            {
                viewportWindow.StateChanged += ViewportWindow_StateChanged;
            }

            if (hostWindow != null)
            {
                hostWindow.StateChanged += HostWindow_StateChanged;
            }
        }

        private void TeardownViewportWindowStateSync()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (viewportWindow != null)
            {
                viewportWindow.StateChanged -= ViewportWindow_StateChanged;
            }

            if (hostWindow != null)
            {
                hostWindow.StateChanged -= HostWindow_StateChanged;
            }
        }

        private void ViewportWindow_StateChanged(object? sender, EventArgs e)
        {
            if (isSynchronizingWindowState || viewportWindow == null)
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (hostWindow == null)
            {
                return;
            }

            // Use Hide/Show instead of WindowState = Minimized: setting the owner window to
            // Minimized causes Windows to automatically hide the owned viewport window, removing
            // its taskbar icon. Hiding the host avoids that owned-window cascade entirely.
            isSynchronizingWindowState = true;
            try
            {
                if (viewportWindow.WindowState == WindowState.Minimized)
                {
                    if (hostWindow.IsVisible)
                    {
                        isHostWindowHiddenByViewportMinimize = true;
                        hostWindow.Hide();
                    }
                }
                else if (isHostWindowHiddenByViewportMinimize)
                {
                    isHostWindowHiddenByViewportMinimize = false;
                    hostWindow.Show();
                }
            }
            finally
            {
                isSynchronizingWindowState = false;
            }
        }

        private void HostWindow_StateChanged(object? sender, EventArgs e)
        {
            if (isSynchronizingWindowState || viewportWindow?.IsVisible != true)
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this);
            if (hostWindow == null)
            {
                return;
            }

            isSynchronizingWindowState = true;
            try
            {
                if (hostWindow.WindowState == WindowState.Minimized
                    && viewportWindow.WindowState != WindowState.Minimized)
                {
                    viewportWindow.WindowState = WindowState.Minimized;
                }
                else if (hostWindow.WindowState != WindowState.Minimized
                    && viewportWindow.WindowState == WindowState.Minimized)
                {
                    viewportWindow.WindowState = WindowState.Normal;
                }
            }
            finally
            {
                isSynchronizingWindowState = false;
            }
        }

        private void ApplyViewportTaskbarPriority()
        {
            bool useViewport = IsViewportUsingWindowsPreview();
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hostHwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = viewportWindow == null ? IntPtr.Zero : new WindowInteropHelper(viewportWindow).Handle;

            StopViewportTaskbarPreviewRefreshTimer();
            if (!useViewport)
            {
                StopViewportAltTabFocusRedirectTimer();
                StopViewportAltTabExitPreparationTimer(restoreNoActivate: true);
                StopViewportExternalForegroundTrackingTimer();
                UninstallViewportAltTabKeyboardHook();
                lastViewportPreviewExternalForegroundHwnd = IntPtr.Zero;
                viewportPreviewInputProxyActive = false;
                SetViewportPreviewProxyVisualTarget(null, "viewport preview disabled");
                viewportPreviewInputProxyFailureLogged = false;
                if (hostWindow != null && isHostWindowHiddenByViewportAltTabExit && !hostWindow.IsVisible)
                {
                    isHostWindowHiddenByViewportAltTabExit = false;
                    hostWindow.Show();
                }
            }

            if (hostHwnd != IntPtr.Zero)
            {
                ViewportThumbnailCompositor.Deactivate(hostHwnd);
            }

            if (viewportWindow != null)
            {
                viewportWindow.ShowInTaskbar = useViewport;
                SetWindowShellVisibility(viewportWindow, forceTaskbar: useViewport, showInAltTab: useViewport);
                SetWindowNoActivate(viewportWindow, noActivate: false);
            }

            if (hostWindow != null)
            {
                SetWindowOwner(hostWindow, IntPtr.Zero);
                hostWindow.ShowInTaskbar = !useViewport;
                SetWindowShellVisibility(hostWindow, forceTaskbar: !useViewport, showInAltTab: !useViewport);
                SetWindowNoActivate(hostWindow, useViewport);
            }

            if (useViewport)
            {
                StartViewportExternalForegroundTrackingTimer();
                viewportPreviewInputProxyActive = true;
                UpdateViewportPreviewProxyVisual("viewport preview shell mode applied");
                EnsureViewportIsForegroundShellRepresentative("viewport preview shell mode applied");
                LogViewportPreviewState("viewport preview shell mode applied");
            }
        }

        private bool IsViewportUsingWindowsPreview()
        {
            return viewportWindow?.IsVisible == true && viewportContent?.UseAsWindowsPreview == true;
        }

        private void CenterViewportWindowNearOwner(Window owner, double initialWidth, double initialHeight)
        {
            if (viewportWindow == null)
            {
                return;
            }

            double ownerWidth = IsFinite(owner.ActualWidth) && owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
            double ownerHeight = IsFinite(owner.ActualHeight) && owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
            if (!IsFinite(owner.Left) || !IsFinite(owner.Top) || !IsFinite(ownerWidth) || !IsFinite(ownerHeight))
            {
                Rect workArea = SystemParameters.WorkArea;
                viewportWindow.Left = workArea.Left + Math.Max(0, (workArea.Width - initialWidth) / 2);
                viewportWindow.Top = workArea.Top + Math.Max(0, (workArea.Height - initialHeight) / 2);
                return;
            }

            viewportWindow.Left = owner.Left + ((ownerWidth - initialWidth) / 2);
            viewportWindow.Top = owner.Top + ((ownerHeight - initialHeight) / 2);
        }

        private void StartViewportTaskbarPreviewRefreshTimer(IntPtr hostHwnd)
        {
            if (viewportTaskbarPreviewRefreshTimer != null)
            {
                return;
            }

            viewportTaskbarPreviewRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            viewportTaskbarPreviewRefreshTimer.Tick += (_, _) =>
            {
                if (!IsViewportUsingWindowsPreview())
                {
                    StopViewportTaskbarPreviewRefreshTimer();
                    return;
                }

                ViewportThumbnailCompositor.Invalidate(hostHwnd);
            };
            viewportTaskbarPreviewRefreshTimer.Start();
        }

        private void StopViewportTaskbarPreviewRefreshTimer()
        {
            if (viewportTaskbarPreviewRefreshTimer == null)
            {
                return;
            }

            viewportTaskbarPreviewRefreshTimer.Stop();
            viewportTaskbarPreviewRefreshTimer = null;
        }

        private void RestoreMainWindowVisualForViewportReturn(bool forceForegroundRepresentative = false)
        {
            if (!IsViewportUsingWindowsPreview())
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (hostWindow == null || viewportWindow == null)
            {
                return;
            }

            IntPtr hostHwnd = new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = new WindowInteropHelper(viewportWindow).Handle;
            if (hostHwnd == IntPtr.Zero || viewportHwnd == IntPtr.Zero)
            {
                return;
            }

            bool wasHiddenForAltTab = isHostWindowHiddenByViewportAltTabExit;
            if (wasHiddenForAltTab)
            {
                isHostWindowHiddenByViewportAltTabExit = false;
            }

            bool hostVisibleBefore = IsWindowVisible(hostHwnd);
            bool hostMinimizedBefore = IsIconic(hostHwnd);
            bool hostCloakedBefore = IsWindowCloaked(hostHwnd);
            bool viewportVisibleBefore = IsWindowVisible(viewportHwnd);
            bool showResult = true;
            bool restackResult = true;
            bool viewportTopResult = true;

            try
            {
                SetWindowNoActivate(hostWindow, noActivate: true);
                if (!hostWindow.IsVisible)
                {
                    hostWindow.Show();
                }

                if (hostMinimizedBefore || !hostVisibleBefore || hostCloakedBefore || wasHiddenForAltTab)
                {
                    showResult = ShowWindow(hostHwnd, SwShownoactivate);
                }

                SetWindowShellVisibility(hostWindow, forceTaskbar: false, showInAltTab: false);

                const uint restoreFlags = SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow;
                restackResult = SetWindowPos(hostHwnd, viewportHwnd, 0, 0, 0, 0, restoreFlags);
                viewportTopResult = SetWindowPos(viewportHwnd, HwndTop, 0, 0, 0, 0, restoreFlags);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("[VPT-ALT] Failed to restore main GM visual without activation.", ex, CustomConsole.LogCategory.Viewport);
                return;
            }

            TextBox? target = GetViewportPreviewInputProxyTarget();
            if (target != null)
            {
                lastMainWindowFocusedElement = target;
            }

            viewportPreviewInputProxyActive = true;
            SetViewportPreviewInputProxyTarget(target, "main visual return restore");
            EnsureViewportIsForegroundShellRepresentative(
                forceForegroundRepresentative
                    ? "main visual return restore from main mouse activation"
                    : "main visual return restore",
                allowExternalForegroundOverride: forceForegroundRepresentative);
            LogViewportPreviewState(
                $"main visual return restore: hiddenForAltTab={wasHiddenForAltTab} mainVisibleBefore={hostVisibleBefore} mainMinimizedBefore={hostMinimizedBefore} mainCloakedBefore={hostCloakedBefore} viewportVisibleBefore={viewportVisibleBefore} showResult={showResult} restackResult={restackResult} viewportTopResult={viewportTopResult} target={target?.Name ?? "(none)"}");
        }

        private void ViewportWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!TryRouteViewportPreviewKey(e))
            {
                LogViewportPreviewState("input proxy key not routed key=" + (e.Key == Key.System ? e.SystemKey : e.Key));
            }
        }

        private void ViewportWindow_TextInput(object sender, TextCompositionEventArgs e)
        {
            if (!viewportPreviewInputProxyActive || !IsViewportUsingWindowsPreview())
            {
                return;
            }

            if (string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
            {
                return;
            }

            try
            {
                TextBox? target = GetViewportPreviewInputProxyTarget();
                if (target == null)
                {
                    LogViewportPreviewInputProxyFailure("text target missing");
                    return;
                }

                SetViewportPreviewInputProxyTarget(target, "text route target");
                InsertTextIntoTextBox(target, e.Text);
                e.Handled = true;
                LogViewportPreviewState("input proxy text routed target=" + target.Name + " length=" + e.Text.Length);
            }
            catch (Exception ex)
            {
                DisableViewportPreviewInputProxyAfterFailure("text route failed", ex);
            }
        }

        private bool TryRouteViewportPreviewKey(KeyEventArgs e)
        {
            if (!viewportPreviewInputProxyActive || !IsViewportUsingWindowsPreview())
            {
                return false;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt or Key.System
                || (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return false;
            }

            try
            {
                TextBox? target = GetViewportPreviewInputProxyTarget();
                if (target == null)
                {
                    LogViewportPreviewInputProxyFailure("key target missing");
                    return false;
                }

                SetViewportPreviewInputProxyTarget(target, "key route target");
                bool routed = RouteViewportPreviewKeyToTextBox(target, key);
                if (routed)
                {
                    e.Handled = true;
                    UpdateViewportPreviewProxyVisual("input proxy key routed");
                    LogViewportPreviewState("input proxy key routed target=" + target.Name + " key=" + key);
                }

                return routed;
            }
            catch (Exception ex)
            {
                DisableViewportPreviewInputProxyAfterFailure("key route failed", ex);
                return false;
            }
        }

        private TextBox? GetViewportPreviewInputProxyTarget()
        {
            if (lastMainWindowFocusedElement is TextBox focusedTextBox
                && IsProxyEligibleMainInput(focusedTextBox))
            {
                return focusedTextBox;
            }

            if (ICMessageSettingsControl?.txtICMessage?.IsEnabled == true)
            {
                lastMainWindowFocusedElement = ICMessageSettingsControl.txtICMessage;
                return ICMessageSettingsControl.txtICMessage;
            }

            if (OOCLogControl?.txtOOCMessage?.IsEnabled == true)
            {
                lastMainWindowFocusedElement = OOCLogControl.txtOOCMessage;
                return OOCLogControl.txtOOCMessage;
            }

            return null;
        }

        private bool IsProxyEligibleMainInput(TextBox? textBox)
        {
            return textBox != null
                && textBox.IsEnabled
                && !textBox.IsReadOnly
                && (ReferenceEquals(textBox, ICMessageSettingsControl?.txtICMessage)
                    || ReferenceEquals(textBox, OOCLogControl?.txtOOCMessage)
                    || ReferenceEquals(textBox, ICMessageSettingsControl?.txtICShowname)
                    || ReferenceEquals(textBox, OOCLogControl?.txtOOCShowname));
        }

        private bool RouteViewportPreviewKeyToTextBox(TextBox target, Key key)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            bool control = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (control)
            {
                return RouteViewportPreviewControlShortcut(target, key);
            }

            switch (key)
            {
                case Key.Back:
                    BackspaceTextBox(target);
                    return true;
                case Key.Delete:
                    DeleteTextBoxSelectionOrNextChar(target);
                    return true;
                case Key.Enter:
                    SubmitViewportPreviewTextBox(target);
                    return true;
                case Key.Tab:
                    SelectNextViewportPreviewInputTarget(target);
                    return true;
                case Key.Left:
                    MoveTextBoxCaret(target, -1, shift);
                    return true;
                case Key.Right:
                    MoveTextBoxCaret(target, 1, shift);
                    return true;
                case Key.Home:
                    SetTextBoxCaret(target, 0, shift);
                    return true;
                case Key.End:
                    SetTextBoxCaret(target, target.Text?.Length ?? 0, shift);
                    return true;
                case Key.Escape:
                    return false;
                default:
                    return false;
            }
        }

        private bool RouteViewportPreviewControlShortcut(TextBox target, Key key)
        {
            switch (key)
            {
                case Key.A:
                    target.SelectAll();
                    return true;
                case Key.C:
                    if (!string.IsNullOrEmpty(target.SelectedText))
                    {
                        ClipboardUtilities.TrySetText(target.SelectedText);
                    }
                    return true;
                case Key.X:
                    if (!string.IsNullOrEmpty(target.SelectedText) && ClipboardUtilities.TrySetText(target.SelectedText))
                    {
                        target.SelectedText = string.Empty;
                    }
                    return true;
                case Key.V:
                    if (ClipboardUtilities.TryGetText(out string text))
                    {
                        InsertTextIntoTextBox(target, text);
                    }
                    return true;
                case Key.Z:
                    if (target.CanUndo)
                    {
                        target.Undo();
                    }
                    return true;
                case Key.Y:
                    if (ApplicationCommands.Redo.CanExecute(null, target))
                    {
                        ApplicationCommands.Redo.Execute(null, target);
                    }
                    return true;
                default:
                    return false;
            }
        }

        private static void InsertTextIntoTextBox(TextBox target, string text)
        {
            int insertionStart = target.SelectionStart;
            target.SelectedText = text;
            target.CaretIndex = Math.Min((target.Text?.Length ?? 0), insertionStart + text.Length);
            EnsureTextBoxCaretVisible(target);
        }

        private static void BackspaceTextBox(TextBox target)
        {
            if (target.SelectionLength > 0)
            {
                target.SelectedText = string.Empty;
                return;
            }

            if (target.CaretIndex <= 0)
            {
                return;
            }

            int index = target.CaretIndex - 1;
            target.Text = target.Text.Remove(index, 1);
            target.CaretIndex = index;
            EnsureTextBoxCaretVisible(target);
        }

        private static void DeleteTextBoxSelectionOrNextChar(TextBox target)
        {
            if (target.SelectionLength > 0)
            {
                target.SelectedText = string.Empty;
                return;
            }

            if (target.CaretIndex >= target.Text.Length)
            {
                return;
            }

            int index = target.CaretIndex;
            target.Text = target.Text.Remove(index, 1);
            target.CaretIndex = index;
            EnsureTextBoxCaretVisible(target);
        }

        private static void MoveTextBoxCaret(TextBox target, int delta, bool extendSelection)
        {
            int next = Math.Clamp(target.CaretIndex + delta, 0, target.Text.Length);
            SetTextBoxCaret(target, next, extendSelection);
        }

        private static void SetTextBoxCaret(TextBox target, int next, bool extendSelection)
        {
            next = Math.Clamp(next, 0, target.Text.Length);
            if (!extendSelection)
            {
                target.CaretIndex = next;
                target.SelectionLength = 0;
                EnsureTextBoxCaretVisible(target);
                return;
            }

            int anchor = target.SelectionLength > 0 ? target.SelectionStart : target.CaretIndex;
            int start = Math.Min(anchor, next);
            int length = Math.Abs(next - anchor);
            target.Select(start, length);
            EnsureTextBoxCaretVisible(target);
        }

        private void SelectNextViewportPreviewInputTarget(TextBox current)
        {
            TextBox next = ReferenceEquals(current, ICMessageSettingsControl.txtICMessage)
                ? OOCLogControl.txtOOCMessage
                : ReferenceEquals(current, OOCLogControl.txtOOCMessage)
                    ? ICMessageSettingsControl.txtICMessage
                    : ReferenceEquals(current, ICMessageSettingsControl.txtICShowname)
                        ? OOCLogControl.txtOOCShowname
                        : ICMessageSettingsControl.txtICShowname;
            lastMainWindowFocusedElement = next;
            SetViewportPreviewInputProxyTarget(next, "tab target switch");
            next.CaretIndex = next.Text?.Length ?? 0;
            EnsureTextBoxCaretVisible(next);
        }

        private static void EnsureTextBoxCaretVisible(TextBox target)
        {
            target.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    if (!target.IsVisible)
                    {
                        return;
                    }

                    ScrollViewer? scrollViewer = FindDescendant<ScrollViewer>(target);
                    if (scrollViewer == null)
                    {
                        target.BringIntoView();
                        return;
                    }

                    Rect caretRect = GetTextBoxCaretRect(target);
                    if (caretRect.IsEmpty
                        || double.IsNaN(caretRect.X)
                        || double.IsNaN(caretRect.Y))
                    {
                        return;
                    }

                    const double padding = 12;
                    if (caretRect.Right > scrollViewer.ViewportWidth - padding)
                    {
                        scrollViewer.ScrollToHorizontalOffset(
                            scrollViewer.HorizontalOffset + caretRect.Right - scrollViewer.ViewportWidth + padding);
                    }
                    else if (caretRect.Left < padding)
                    {
                        scrollViewer.ScrollToHorizontalOffset(
                            Math.Max(0, scrollViewer.HorizontalOffset + caretRect.Left - padding));
                    }

                    if (caretRect.Bottom > scrollViewer.ViewportHeight - padding)
                    {
                        scrollViewer.ScrollToVerticalOffset(
                            scrollViewer.VerticalOffset + caretRect.Bottom - scrollViewer.ViewportHeight + padding);
                    }
                    else if (caretRect.Top < padding)
                    {
                        scrollViewer.ScrollToVerticalOffset(
                            Math.Max(0, scrollViewer.VerticalOffset + caretRect.Top - padding));
                    }
                }));
        }

        private static Rect GetTextBoxCaretRect(TextBox target)
        {
            int textLength = target.Text?.Length ?? 0;
            int caretIndex = Math.Clamp(target.CaretIndex, 0, textLength);
            try
            {
                if (caretIndex > 0)
                {
                    Rect trailingPrevious = target.GetRectFromCharacterIndex(caretIndex - 1, true);
                    if (!trailingPrevious.IsEmpty)
                    {
                        return trailingPrevious;
                    }
                }

                Rect leadingCurrent = target.GetRectFromCharacterIndex(caretIndex, false);
                if (!leadingCurrent.IsEmpty)
                {
                    return leadingCurrent;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            return Rect.Empty;
        }

        private void SubmitViewportPreviewTextBox(TextBox target)
        {
            if (ReferenceEquals(target, ICMessageSettingsControl.txtICMessage))
            {
                ICMessageSettingsControl.OnSendICMessage?.Invoke(target.Text);
                return;
            }

            if (ReferenceEquals(target, OOCLogControl.txtOOCMessage))
            {
                if (string.IsNullOrWhiteSpace(OOCLogControl.txtOOCShowname.Text))
                {
                    OOCLogControl.AddMessage(currentClient, "Oceanya Client", "You must set a showname before sending a message!", true);
                    return;
                }

                if (currentClient == null)
                {
                    OOCLogControl.AddMessage(currentClient, "Oceanya Client", "No client selected. Please select a client first.", true);
                    return;
                }

                string message = target.Text;
                target.Clear();
                OOCLogControl.OnSendOOCMessage?.Invoke(OOCLogControl.txtOOCShowname.Text, message);
            }
        }

        private void SetViewportPreviewInputProxyTarget(TextBox? target, string reason)
        {
            if (target != null)
            {
                lastMainWindowFocusedElement = target;
            }

            UpdateViewportPreviewProxyVisual(reason);
        }

        private void UpdateViewportPreviewProxyVisual(string reason)
        {
            TextBox? target = null;
            if (viewportPreviewInputProxyActive
                && IsViewportUsingWindowsPreview()
                && IsForegroundOwnedByCurrentProcess())
            {
                target = GetViewportPreviewInputProxyTarget();
            }

            SetViewportPreviewProxyVisualTarget(target, reason);
        }

        private void SetViewportPreviewProxyVisualTarget(TextBox? target, string reason)
        {
            if (ReferenceEquals(viewportPreviewProxyVisualTarget, target))
            {
                return;
            }

            if (viewportPreviewProxyVisualTarget != null)
            {
                ProxyKeyboardFocusVisual.SetIsProxyKeyboardFocusTarget(viewportPreviewProxyVisualTarget, false);
            }

            viewportPreviewProxyVisualTarget = target;
            if (viewportPreviewProxyVisualTarget != null)
            {
                ProxyKeyboardFocusVisual.SetIsProxyKeyboardFocusTarget(viewportPreviewProxyVisualTarget, true);
            }

            LogViewportPreviewState("proxy visual target=" + (target?.Name ?? "(none)") + " reason=" + reason);
        }

        private void DisableViewportPreviewInputProxyAfterFailure(string reason, Exception ex)
        {
            viewportPreviewInputProxyActive = false;
            SetViewportPreviewProxyVisualTarget(null, "input proxy disabled after failure");
            if (!viewportPreviewInputProxyFailureLogged)
            {
                viewportPreviewInputProxyFailureLogged = true;
                CustomConsole.Warning("[VPT-ALT] Disabled viewport preview input proxy: " + reason, ex, CustomConsole.LogCategory.Viewport);
            }
        }

        private void LogViewportPreviewInputProxyFailure(string reason)
        {
            if (!viewportPreviewInputProxyFailureLogged)
            {
                viewportPreviewInputProxyFailureLogged = true;
                CustomConsole.Warning("[VPT-ALT] Viewport preview input proxy could not route: " + reason, category: CustomConsole.LogCategory.Viewport);
            }
        }

        private void EnsureViewportIsForegroundShellRepresentative(string reason, bool allowExternalForegroundOverride = false)
        {
            if (!IsViewportUsingWindowsPreview() || viewportWindow == null)
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hostHwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = new WindowInteropHelper(viewportWindow).Handle;
            if (viewportHwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(foregroundHwnd, out int foregroundProcessId);
                if (foregroundProcessId != Process.GetCurrentProcess().Id && !allowExternalForegroundOverride)
                {
                    UpdateViewportPreviewProxyVisual("external foreground during shell representative ensure");
                    LogViewportPreviewState("viewport shell representative skipped external foreground reason=" + reason);
                    return;
                }
            }

            const int GWL_EXSTYLE = -20;
            const int WS_EX_NOACTIVATE = 0x08000000;
            int viewportStyleBefore = GetWindowLong(viewportHwnd, GWL_EXSTYLE);
            bool restoreNoActivate = (viewportStyleBefore & WS_EX_NOACTIVATE) != 0;
            bool setForegroundResult = true;
            IntPtr activeResult = IntPtr.Zero;

            try
            {
                if (restoreNoActivate)
                {
                    SetWindowNoActivate(viewportWindow, noActivate: false);
                }

                if (GetForegroundWindow() != viewportHwnd)
                {
                    setForegroundResult = SetForegroundWindow(viewportHwnd);
                }

                activeResult = SetActiveWindow(viewportHwnd);
            }
            finally
            {
                if (restoreNoActivate)
                {
                    SetWindowNoActivate(viewportWindow, noActivate: true);
                }
            }

            UpdateViewportPreviewProxyVisual("viewport foreground shell representative ensured");
            LogViewportPreviewState(
                $"viewport shell representative ensured reason={reason} setForeground={setForegroundResult} setActivePrevious=0x{activeResult.ToInt64():X} mainHwnd=0x{hostHwnd.ToInt64():X} restoreNoActivate={restoreNoActivate}");
        }

        private void LogViewportPreviewState(string reason)
        {
            if (!IsViewportUsingWindowsPreview())
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hostHwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = viewportWindow == null ? IntPtr.Zero : new WindowInteropHelper(viewportWindow).Handle;
            IntPtr foregroundHwnd = GetForegroundWindow();
            IntPtr activeHwnd = GetActiveWindow();
            const int GWL_EXSTYLE = -20;
            int hostStyle = hostHwnd == IntPtr.Zero ? 0 : GetWindowLong(hostHwnd, GWL_EXSTYLE);
            int viewportStyle = viewportHwnd == IntPtr.Zero ? 0 : GetWindowLong(viewportHwnd, GWL_EXSTYLE);
            bool hostVisible = hostHwnd != IntPtr.Zero && IsWindowVisible(hostHwnd);
            bool hostMinimized = hostHwnd != IntPtr.Zero && IsIconic(hostHwnd);
            bool hostCloaked = hostHwnd != IntPtr.Zero && IsWindowCloaked(hostHwnd);
            bool viewportVisible = viewportHwnd != IntPtr.Zero && IsWindowVisible(viewportHwnd);
            IntPtr shellRepresentativeHwnd = viewportHwnd != IntPtr.Zero && foregroundHwnd == viewportHwnd
                ? viewportHwnd
                : foregroundHwnd;
            bool mainForeground = hostHwnd != IntPtr.Zero && foregroundHwnd == hostHwnd;
            bool mainActive = hostHwnd != IntPtr.Zero && activeHwnd == hostHwnd;
            bool viewportForeground = viewportHwnd != IntPtr.Zero && foregroundHwnd == viewportHwnd;
            bool viewportActive = viewportHwnd != IntPtr.Zero && activeHwnd == viewportHwnd;
            bool altTabPreparationActive = viewportAltTabExitPreparationTimer != null
                || viewportAltTabHeldReinjectTimer != null
                || IsAltKeyDown();
            CustomConsole.Debug(
                $"[VPT-ALT] {reason}; foreground=0x{foregroundHwnd.ToInt64():X} active=0x{activeHwnd.ToInt64():X} shell=0x{shellRepresentativeHwnd.ToInt64():X} main=0x{hostHwnd.ToInt64():X}/ex=0x{hostStyle:X8}/visible={hostVisible}/minimized={hostMinimized}/cloaked={hostCloaked}/foreground={mainForeground}/active={mainActive} viewport=0x{viewportHwnd.ToInt64():X}/ex=0x{viewportStyle:X8}/visible={viewportVisible}/foreground={viewportForeground}/active={viewportActive} proxy={viewportPreviewInputProxyActive} proxyVisual={viewportPreviewProxyVisualTarget?.Name ?? "(none)"} altTabPrep={altTabPreparationActive}",
                CustomConsole.LogCategory.Viewport);
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T match)
                {
                    return match;
                }

                source = GetDependencyObjectParent(source);
            }

            return null;
        }

        private static T? FindDescendant<T>(DependencyObject? source) where T : DependencyObject
        {
            if (source == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(source);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(source, index);
                if (child is T match)
                {
                    return match;
                }

                T? nested = FindDescendant<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static DependencyObject? GetDependencyObjectParent(DependencyObject source)
        {
            if (source is Visual || source is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
            }

            if (source is FrameworkContentElement contentElement)
            {
                return contentElement.Parent ?? LogicalTreeHelper.GetParent(contentElement);
            }

            if (source is FrameworkElement frameworkElement)
            {
                return frameworkElement.Parent ?? LogicalTreeHelper.GetParent(frameworkElement);
            }

            return LogicalTreeHelper.GetParent(source);
        }

        private void StartViewportExternalForegroundTrackingTimer()
        {
            if (viewportExternalForegroundTrackingTimer != null)
            {
                return;
            }

            viewportExternalForegroundTrackingTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            viewportExternalForegroundTrackingTimer.Tick += (_, _) =>
            {
                if (!IsViewportUsingWindowsPreview())
                {
                    StopViewportExternalForegroundTrackingTimer();
                    return;
                }

                RememberExternalForegroundWindow();
            };
            viewportExternalForegroundTrackingTimer.Start();
            RememberExternalForegroundWindow();
        }

        private void StopViewportExternalForegroundTrackingTimer()
        {
            if (viewportExternalForegroundTrackingTimer == null)
            {
                return;
            }

            viewportExternalForegroundTrackingTimer.Stop();
            viewportExternalForegroundTrackingTimer = null;
        }

        private void StartViewportAltTabHeldReinjectTimer()
        {
            if (viewportAltTabHeldReinjectTimer != null)
            {
                return;
            }

            viewportAltTabHeldReinjectTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(160)
            };
            viewportAltTabHeldReinjectTimer.Tick += (_, _) =>
            {
                StopViewportAltTabHeldReinjectTimer();
                if (!viewportAltTabHookPendingSuppressedQuickSwitch || !IsAltKeyDown())
                {
                    return;
                }

                viewportAltTabHookPendingSuppressedQuickSwitch = false;

                // Pre-activate the viewport so the injected Tab fires from the viewport HWND.
                // Without this, the main window (WS_EX_TOOLWINDOW, hidden from Alt-Tab) is
                // still foreground and Windows cycles through the viewport as the current
                // Oceanya entry before reaching the external app, producing a double switch.
                if (viewportWindow != null && IsViewportUsingWindowsPreview())
                {
                    IntPtr viewportHwnd = new WindowInteropHelper(viewportWindow).Handle;
                    if (viewportHwnd != IntPtr.Zero)
                    {
                        viewportAltTabHeldPreActivated = true;
                        SetWindowNoActivate(viewportWindow, noActivate: false);
                        SetForegroundWindow(viewportHwnd);
                    }
                }

                keybd_event((byte)VkTab, 0, 0, UIntPtr.Zero);
                keybd_event((byte)VkTab, 0, KeyEventFKeyUp, UIntPtr.Zero);
            };
            viewportAltTabHeldReinjectTimer.Start();
        }

        private void StopViewportAltTabHeldReinjectTimer()
        {
            if (viewportAltTabHeldReinjectTimer == null)
            {
                return;
            }

            viewportAltTabHeldReinjectTimer.Stop();
            viewportAltTabHeldReinjectTimer = null;
        }

        private void RememberExternalForegroundWindow()
        {
            IntPtr foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero)
            {
                return;
            }

            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hostHwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = viewportWindow == null ? IntPtr.Zero : new WindowInteropHelper(viewportWindow).Handle;
            int currentProcessId = Process.GetCurrentProcess().Id;
            if (IsEligibleAltTabSwitchTarget(foregroundHwnd, hostHwnd, viewportHwnd, currentProcessId))
            {
                lastViewportPreviewExternalForegroundHwnd = foregroundHwnd;
                SetViewportPreviewProxyVisualTarget(null, "external foreground");
                return;
            }

            if (IsForegroundOwnedByCurrentProcess())
            {
                UpdateViewportPreviewProxyVisual("foreground tracking current process");
            }
        }

        private void InstallViewportAltTabKeyboardHook()
        {
            if (viewportAltTabKeyboardHook != IntPtr.Zero)
            {
                return;
            }

            viewportAltTabKeyboardHookProc = ViewportAltTabKeyboardHookCallback;
            viewportAltTabKeyboardHook = SetWindowsHookEx(
                WhKeyboardLl,
                viewportAltTabKeyboardHookProc,
                GetModuleHandle(null),
                0);
        }

        private void UninstallViewportAltTabKeyboardHook()
        {
            if (viewportAltTabKeyboardHook == IntPtr.Zero)
            {
                return;
            }

            StopViewportAltTabHeldReinjectTimer();
            UnhookWindowsHookEx(viewportAltTabKeyboardHook);
            viewportAltTabKeyboardHook = IntPtr.Zero;
            viewportAltTabKeyboardHookProc = null;
            viewportAltTabHookPendingSuppressedQuickSwitch = false;
            viewportAltTabHeldPreActivated = false;
        }

        private IntPtr ViewportAltTabKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsViewportUsingWindowsPreview())
            {
                int message = wParam.ToInt32();
                int vkCode = Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                bool isInjected = (flags & LlkHfInjected) != 0;
                if ((message == WmKeyDown || message == WmSysKeyDown)
                    && vkCode == VkTab
                    && IsAltKeyDown()
                    && !IsShiftKeyDown()
                    && IsForegroundOwnedByCurrentProcess()
                    && !isInjected)
                {
                    viewportAltTabHookPendingSuppressedQuickSwitch = true;
                    Dispatcher.BeginInvoke(
                        new Action(StartViewportAltTabHeldReinjectTimer),
                        DispatcherPriority.Input);
                    return new IntPtr(1);
                }
                else if ((message == WmKeyUp || message == WmSysKeyUp) && vkCode == VkMenu)
                {
                    bool shouldCorrectQuickSwitch = viewportAltTabHookPendingSuppressedQuickSwitch
                        && IsForegroundOwnedByCurrentProcess();
                    viewportAltTabHookPendingSuppressedQuickSwitch = false;
                    bool wasHeldPreActivated = viewportAltTabHeldPreActivated;
                    viewportAltTabHeldPreActivated = false;
                    Dispatcher.BeginInvoke(
                        new Action(StopViewportAltTabHeldReinjectTimer),
                        DispatcherPriority.Input);
                    if (wasHeldPreActivated && IsViewportUsingWindowsPreview() && viewportWindow != null)
                    {
                        // Restore NoActivate that the held-reinject path removed.
                        // Also clear the activation redirect suppress so the next return
                        // to Oceanya via the viewport is handled normally.
                        SetWindowNoActivate(viewportWindow, noActivate: true);
                    }

                    if (shouldCorrectQuickSwitch && TryActivateRememberedExternalForegroundWindow())
                    {
                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(viewportAltTabKeyboardHook, nCode, wParam, lParam);
        }

        private static bool IsForegroundOwnedByCurrentProcess()
        {
            IntPtr foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(foregroundHwnd, out int processId);
            return processId == Process.GetCurrentProcess().Id;
        }

        private bool TryActivateRememberedExternalForegroundWindow()
        {
            Window? hostWindow = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            IntPtr hostHwnd = hostWindow == null ? IntPtr.Zero : new WindowInteropHelper(hostWindow).Handle;
            IntPtr viewportHwnd = viewportWindow == null ? IntPtr.Zero : new WindowInteropHelper(viewportWindow).Handle;
            int currentProcessId = Process.GetCurrentProcess().Id;
            if (IsEligibleAltTabSwitchTarget(
                    lastViewportPreviewExternalForegroundHwnd,
                    hostHwnd,
                    viewportHwnd,
                    currentProcessId))
            {
                return RestoreAndActivateWindow(lastViewportPreviewExternalForegroundHwnd);
            }

            return TryActivatePreviousExternalTopLevelWindow();
        }

        private void ScheduleViewportAltTabFocusRedirect()
        {
            if (viewportAltTabFocusRedirectTimer != null)
            {
                return;
            }

            viewportAltTabFocusRedirectTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            viewportAltTabFocusRedirectTimer.Tick += (_, _) =>
            {
                if (!IsViewportUsingWindowsPreview())
                {
                    StopViewportAltTabFocusRedirectTimer();
                    return;
                }

                if (IsAltKeyDown())
                {
                    return;
                }

                StopViewportAltTabFocusRedirectTimer();
                FocusMainWindowFromViewportPreview();
            };
            viewportAltTabFocusRedirectTimer.Start();
        }

        private void StopViewportAltTabFocusRedirectTimer()
        {
            if (viewportAltTabFocusRedirectTimer == null)
            {
                return;
            }

            viewportAltTabFocusRedirectTimer.Stop();
            viewportAltTabFocusRedirectTimer = null;
        }

        private void PrepareViewportForAltTabExit()
        {
            if (!IsViewportUsingWindowsPreview() || viewportWindow == null)
            {
                return;
            }

            IntPtr viewportHwnd = new WindowInteropHelper(viewportWindow).Handle;
            if (viewportHwnd == IntPtr.Zero || GetForegroundWindow() == viewportHwnd)
            {
                return;
            }

            viewportAltTabExitAltReleasedAt = null;
            StopViewportAltTabFocusRedirectTimer();
            SetWindowNoActivate(viewportWindow, noActivate: false);
            SetForegroundWindow(viewportHwnd);

            StartViewportAltTabExitPreparationTimer(viewportHwnd);
        }

        private void StartViewportAltTabExitPreparationTimer(IntPtr viewportHwnd)
        {
            if (viewportAltTabExitPreparationTimer != null)
            {
                return;
            }

            viewportAltTabExitPreparationTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            viewportAltTabExitPreparationTimer.Tick += (_, _) =>
            {
                if (IsAltKeyDown())
                {
                    if (IsTabKeyDown())
                    {
                        HideHostWindowForViewportAltTabExit();
                    }

                    viewportAltTabExitAltReleasedAt = null;
                    return;
                }

                viewportAltTabExitAltReleasedAt ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - viewportAltTabExitAltReleasedAt.Value).TotalMilliseconds < 75)
                {
                    return;
                }

                bool isStillForeground = GetForegroundWindow() == viewportHwnd;
                StopViewportAltTabExitPreparationTimer(restoreNoActivate: true);
                if (isStillForeground && IsViewportUsingWindowsPreview())
                {
                    FocusMainWindowFromViewportPreview();
                }
            };
            viewportAltTabExitPreparationTimer.Start();
        }

        private void StopViewportAltTabExitPreparationTimer(bool restoreNoActivate)
        {
            if (viewportAltTabExitPreparationTimer != null)
            {
                viewportAltTabExitPreparationTimer.Stop();
                viewportAltTabExitPreparationTimer = null;
            }

            viewportAltTabExitAltReleasedAt = null;
            if (restoreNoActivate && IsViewportUsingWindowsPreview() && viewportWindow != null)
            {
                SetWindowNoActivate(viewportWindow, noActivate: true);
            }
        }

        private static bool IsAltKeyDown()
        {
            return (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        }

        private static bool IsTabKeyDown()
        {
            return (GetAsyncKeyState(VkTab) & 0x8000) != 0;
        }

        private static bool IsShiftKeyDown()
        {
            return (GetAsyncKeyState(VkShift) & 0x8000) != 0;
        }

        private static void SetWindowShellVisibility(Window window, bool forceTaskbar, bool showInAltTab)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            const int GWL_EXSTYLE = -20;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_APPWINDOW = 0x00040000;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOOWNERZORDER = 0x0200;
            const uint SWP_FRAMECHANGED = 0x0020;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newExStyle = forceTaskbar
                ? exStyle | WS_EX_APPWINDOW
                : exStyle & ~WS_EX_APPWINDOW;
            newExStyle = showInAltTab
                ? newExStyle & ~WS_EX_TOOLWINDOW
                : newExStyle | WS_EX_TOOLWINDOW;

            if (newExStyle != exStyle)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, newExStyle);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
            }
        }

        private static void SetWindowNoActivate(Window window, bool noActivate)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            const int GWL_EXSTYLE = -20;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOOWNERZORDER = 0x0200;
            const uint SWP_FRAMECHANGED = 0x0020;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newExStyle = noActivate
                ? exStyle | WS_EX_NOACTIVATE
                : exStyle & ~WS_EX_NOACTIVATE;

            if (newExStyle != exStyle)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, newExStyle);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
            }
        }

        private void ApplyViewportMinMaxInfo(IntPtr lParam, Window? targetWindow)
        {
            ViewportMinMaxInfo minMaxInfo = Marshal.PtrToStructure<ViewportMinMaxInfo>(lParam);
            (double scaleX, double scaleY) = GetViewportDpiScale(targetWindow);
            minMaxInfo.ptMinTrackSize.X = (int)Math.Ceiling(GetViewportMinimumWindowWidth() * scaleX);
            minMaxInfo.ptMinTrackSize.Y = (int)Math.Ceiling(GetViewportMinimumWindowHeight() * scaleY);
            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        private void ApplyViewportSizingRect(int sizingEdge, IntPtr lParam, Window? targetWindow)
        {
            ViewportNativeRect rect = Marshal.PtrToStructure<ViewportNativeRect>(lParam);
            (double scaleX, double scaleY) = GetViewportDpiScale(targetWindow);
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
                height = ((width - horizontalOffset) / GetViewportContentAspectRatio()) + verticalOffset;
                if (height < minHeight)
                {
                    height = minHeight;
                    width = ((height - verticalOffset) * GetViewportContentAspectRatio()) + horizontalOffset;
                }
            }
            else
            {
                width = ((height - verticalOffset) * GetViewportContentAspectRatio()) + horizontalOffset;
                if (width < minWidth)
                {
                    width = minWidth;
                    height = ((width - horizontalOffset) / GetViewportContentAspectRatio()) + verticalOffset;
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
            double minWidth = GetViewportMinimumWindowWidth();
            double minHeight = GetViewportMinimumWindowHeight();
            double clampedWidth = Math.Max(minWidth, width);
            double clampedHeight = Math.Max(minHeight, height);

            if (preferWidth)
            {
                double contentWidth = Math.Max(
                    MinimumViewportContentWidth,
                    clampedWidth - GetViewportWindowHorizontalOffset());
                double contentHeight = contentWidth / GetViewportContentAspectRatio();
                if (contentHeight < MinimumViewportContentHeight)
                {
                    contentHeight = MinimumViewportContentHeight;
                    contentWidth = contentHeight * GetViewportContentAspectRatio();
                }

                return (
                    GetViewportWindowWidthFromContentWidth(contentWidth),
                    GetViewportWindowHeightFromContentHeight(contentHeight));
            }

            double heightDrivenContentHeight = Math.Max(
                MinimumViewportContentHeight,
                clampedHeight - GetViewportWindowVerticalOffset());
            double heightDrivenContentWidth = heightDrivenContentHeight * GetViewportContentAspectRatio();
            if (heightDrivenContentWidth < MinimumViewportContentWidth)
            {
                heightDrivenContentWidth = MinimumViewportContentWidth;
                heightDrivenContentHeight = heightDrivenContentWidth / GetViewportContentAspectRatio();
            }

            return (
                GetViewportWindowWidthFromContentWidth(heightDrivenContentWidth),
                GetViewportWindowHeightFromContentHeight(heightDrivenContentHeight));
        }

        internal static (double Width, double Height, double? Left, double? Top) ResolveViewportWindowRestoreState(
            ViewportWindowState? savedState)
        {
            (double contentWidth, double contentHeight) = ResolveViewportContentRestoreSize(savedState);
            double? left = savedState?.Left.HasValue == true && IsFinite(savedState.Left.Value)
                ? savedState.Left.Value
                : null;
            double? top = savedState?.Top.HasValue == true && IsFinite(savedState.Top.Value)
                ? savedState.Top.Value
                : null;

            return (
                GetViewportWindowWidthFromContentWidth(contentWidth),
                GetViewportWindowHeightFromContentHeight(contentHeight),
                left,
                top);
        }

        internal static (double Width, double Height) ResolveViewportContentRestoreSize(ViewportWindowState? savedState)
        {
            if (savedState == null)
            {
                return (AO2ViewportAssetResolver.ViewportToolWidth, AO2ViewportAssetResolver.ViewportToolHeight);
            }

            double contentWidth = IsFinite(savedState.Width) && savedState.Width > 0
                ? savedState.Width
                : AO2ViewportAssetResolver.ViewportToolWidth;
            double contentHeight = IsFinite(savedState.Height) && savedState.Height > 0
                ? savedState.Height
                : AO2ViewportAssetResolver.ViewportToolHeight;

            double legacyOuterContentWidth = savedState.Width - GetViewportWindowHorizontalOffset();
            double legacyOuterContentHeight = savedState.Height - GetViewportWindowVerticalOffset();
            if (IsViewportContentAspectRatio(legacyOuterContentWidth, legacyOuterContentHeight))
            {
                contentWidth = legacyOuterContentWidth;
                contentHeight = legacyOuterContentHeight;
            }

            return NormalizeViewportContentSize(contentWidth, contentHeight);
        }

        private void CaptureViewportWindowState()
        {
            if (OceanyaTestMode.Current.DisableViewportWindowPersistence)
            {
                return;
            }

            if (viewportWindow == null || viewportWindow.WindowState != WindowState.Normal || isRestoringViewportWindow)
            {
                return;
            }

            SaveFile.Data.GMViewportWindowState = CreateViewportWindowStateFromHostBounds(
                ResolveCapturedWindowWidth(viewportWindow, lastViewportWindowWidth),
                ResolveCapturedWindowHeight(viewportWindow, lastViewportWindowHeight),
                viewportWindow.Left,
                viewportWindow.Top,
                viewportWindow.IsVisible,
                viewportContent?.CurrentSurfaceWidth ?? AO2ViewportAssetResolver.ViewportToolWidth,
                viewportContent?.CurrentSurfaceHeight ?? AO2ViewportAssetResolver.ViewportToolHeight);
            SaveFile.Save();
        }

        private void CapturePictureInPictureViewportWindowState(string reason)
        {
            if (OceanyaTestMode.Current.DisableViewportWindowPersistence)
            {
                return;
            }

            if (pictureInPictureViewportWindow == null
                || pictureInPictureViewportWindow.WindowState != WindowState.Normal
                || isRestoringPictureInPictureViewportWindow)
            {
                return;
            }

            SaveFile.Data.GMPictureInPictureViewportState = CreateViewportWindowStateFromHostBounds(
                ResolveCapturedWindowWidth(pictureInPictureViewportWindow, lastPictureInPictureViewportWindowWidth),
                ResolveCapturedWindowHeight(pictureInPictureViewportWindow, lastPictureInPictureViewportWindowHeight),
                pictureInPictureViewportWindow.Left,
                pictureInPictureViewportWindow.Top,
                pictureInPictureViewportWindow.IsVisible,
                pictureInPictureViewportContent?.CurrentSurfaceWidth ?? AO2ViewportAssetResolver.ViewportToolWidth,
                pictureInPictureViewportContent?.CurrentSurfaceHeight ?? AO2ViewportAssetResolver.ViewportToolHeight);
            SaveFile.Save();
            LogPictureInPictureViewport(reason);
        }

        internal static ViewportWindowState CreateViewportWindowStateFromHostBounds(
            double windowWidth,
            double windowHeight,
            double? left,
            double? top,
            bool isVisible,
            int surfaceWidth = 0,
            int surfaceHeight = 0)
        {
            double contentWidth = windowWidth - GetViewportWindowHorizontalOffset();
            double contentHeight = windowHeight - GetViewportWindowVerticalOffset();
            (double width, double height) = NormalizeViewportContentSize(contentWidth, contentHeight);
            int capturedSurfaceWidth = surfaceWidth > 0
                ? surfaceWidth
                : AO2ViewportAssetResolver.ViewportToolWidth;
            int capturedSurfaceHeight = surfaceHeight > 0
                ? surfaceHeight
                : AO2ViewportAssetResolver.ViewportToolHeight;

            return new ViewportWindowState
            {
                Width = width,
                Height = height,
                SurfaceWidth = capturedSurfaceWidth,
                SurfaceHeight = capturedSurfaceHeight,
                Left = left.HasValue && IsFinite(left.Value) ? left.Value : null,
                Top = top.HasValue && IsFinite(top.Value) ? top.Value : null,
                IsVisible = isVisible
            };
        }

        private void RememberViewportWindowSize(double width, double height)
        {
            if (IsFinite(width) && width > 0)
            {
                lastViewportWindowWidth = width;
            }

            if (IsFinite(height) && height > 0)
            {
                lastViewportWindowHeight = height;
            }
        }

        private void RememberPictureInPictureViewportWindowSize(double width, double height)
        {
            if (IsFinite(width) && width > 0)
            {
                lastPictureInPictureViewportWindowWidth = width;
            }

            if (IsFinite(height) && height > 0)
            {
                lastPictureInPictureViewportWindowHeight = height;
            }
        }

        private static double ResolveCapturedWindowWidth(Window window, double? rememberedWidth)
        {
            if (rememberedWidth.HasValue && IsFinite(rememberedWidth.Value) && rememberedWidth.Value > 0)
            {
                return rememberedWidth.Value;
            }

            if (IsFinite(window.ActualWidth) && window.ActualWidth > 0)
            {
                return window.ActualWidth;
            }

            return IsFinite(window.Width) && window.Width > 0
                ? window.Width
                : GetViewportWindowWidthFromContentWidth(AO2ViewportAssetResolver.ViewportToolWidth);
        }

        private static double ResolveCapturedWindowHeight(Window window, double? rememberedHeight)
        {
            if (rememberedHeight.HasValue && IsFinite(rememberedHeight.Value) && rememberedHeight.Value > 0)
            {
                return rememberedHeight.Value;
            }

            if (IsFinite(window.ActualHeight) && window.ActualHeight > 0)
            {
                return window.ActualHeight;
            }

            return IsFinite(window.Height) && window.Height > 0
                ? window.Height
                : GetViewportWindowHeightFromContentHeight(AO2ViewportAssetResolver.ViewportToolHeight);
        }

        private static string DescribeViewportWindowState(ViewportWindowState? state)
        {
            if (state == null)
            {
                return "(null)";
            }

            return $"left={state.Left?.ToString("0.##") ?? "(null)"} top={state.Top?.ToString("0.##") ?? "(null)"} "
                + $"width={state.Width:0.##} height={state.Height:0.##} visible={state.IsVisible} "
                + $"surface={state.SurfaceWidth}x{state.SurfaceHeight}";
        }

        private static (double Width, double Height) NormalizeViewportContentSize(double width, double height)
        {
            double contentWidth = IsFinite(width) && width > 0 ? width : AO2ViewportAssetResolver.ViewportToolWidth;
            double contentHeight = IsFinite(height) && height > 0 ? height : AO2ViewportAssetResolver.ViewportToolHeight;
            contentWidth = Math.Max(MinimumViewportContentWidth, contentWidth);
            contentHeight = Math.Max(MinimumViewportContentHeight, contentHeight);

            if (IsViewportContentAspectRatio(contentWidth, contentHeight))
            {
                return (contentWidth, contentHeight);
            }

            double widthDrivenHeight = contentWidth / GetViewportContentAspectRatio();
            if (widthDrivenHeight >= MinimumViewportContentHeight)
            {
                return (contentWidth, widthDrivenHeight);
            }

            contentHeight = MinimumViewportContentHeight;
            return (contentHeight * GetViewportContentAspectRatio(), contentHeight);
        }

        private static bool IsViewportContentAspectRatio(double width, double height)
        {
            return IsFinite(width)
                && IsFinite(height)
                && width > 0
                && height > 0
                && Math.Abs((width / height) - GetViewportContentAspectRatio()) < 0.01;
        }

        private static bool ShouldResizeViewportToNativeSurface(
            ViewportWindowState? savedState,
            int surfaceWidth,
            int surfaceHeight)
        {
            if (savedState == null)
            {
                return true;
            }

            return savedState.SurfaceWidth != surfaceWidth
                || savedState.SurfaceHeight != surfaceHeight;
        }

        private static double GetViewportContentAspectRatio()
        {
            return (double)AO2ViewportAssetResolver.ViewportToolWidth / AO2ViewportAssetResolver.ViewportToolHeight;
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
            return GetViewportWindowWidthFromContentWidth(MinimumViewportContentWidth);
        }

        private static double GetViewportMinimumWindowHeight()
        {
            return GetViewportWindowHeightFromContentHeight(MinimumViewportContentHeight);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (maximum < minimum)
            {
                return minimum;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private (double ScaleX, double ScaleY) GetViewportDpiScale(Window? targetWindow)
        {
            if (targetWindow == null)
            {
                return (1.0, 1.0);
            }

            PresentationSource? source = PresentationSource.FromVisual(targetWindow);
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
            await RemoveClientAsync(currentClient);
        }

        private async Task RemoveClientAsync(AOClient? clientToRemove)
        {
            if (clientToRemove == null)
            {
                return;
            }

            if (!useSingleInternalClient)
            {
                viewportContent?.RemoveClient(clientToRemove);
                await clientToRemove.Disconnect();
                return;
            }

            var button = clients.FirstOrDefault(x => x.Value == clientToRemove).Key;
            if (button != null)
            {
                ClearAiClientState(clientToRemove);
                viewportContent?.RemoveClient(clientToRemove);
                profileIniPuppetNames.Remove(clientToRemove);
                if (aiControllers.Remove(clientToRemove, out AOClientAgentController? controller))
                {
                    controller.Dispose();
                }
                clients.Remove(button);
                clientOrder.Remove(clientToRemove);
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
                RefreshMusicListForCurrentClient();
                RefreshDreddOverlayForCurrentContext(promptForUnknownOverlay: false);
                UpdateDreddFeatureEnabledState();
                CaptureGmMultiClientSnapshot();
            }
            else
            {
                if (ReferenceEquals(currentClient, clientToRemove))
                {
                    AOClient nextClient = clientOrder.First(client => clients.Values.Contains(client));
                    SelectClient(nextClient);
                }
                else
                {
                    CaptureGmMultiClientSnapshot();
                }
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
            CaptureGmMultiClientSnapshot();
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
            MessageBoxResult confirm = OceanyaMessageBox.Show(
                "Opening the Character Folder Visualizer scans your entire AO2 characters directory and may take a while.\n\nContinue?",
                "Character Folder Visualizer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            CharacterFolderVisualizerWindow visualizerWindow = new CharacterFolderVisualizerWindow(
                OnAssetsRefreshedFromVisualizer,
                CanSetVisualizerCharacter,
                SetVisualizerCharacterInClient,
                suppressInitialLoadWaitForm: false)
            {
                Owner = HostWindow
            };
            visualizerWindow.Show();
        }

        private void OpenCharacterInFolderVisualizer(string characterDirectoryOrName)
        {
            if (string.IsNullOrWhiteSpace(characterDirectoryOrName))
            {
                return;
            }

            CharacterFolderVisualizerWindow visualizerWindow = new CharacterFolderVisualizerWindow(
                OnAssetsRefreshedFromVisualizer,
                CanSetVisualizerCharacter,
                SetVisualizerCharacterInClient,
                suppressInitialLoadWaitForm: false)
            {
                Owner = HostWindow
            };
            visualizerWindow.SelectCharacterWhenReady(characterDirectoryOrName);
            visualizerWindow.Show();
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

        private async Task RefreshBackgroundAssetsAsync(string? backgroundName)
        {
            if (string.IsNullOrWhiteSpace(backgroundName))
            {
                return;
            }

            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner == null)
            {
                return;
            }

            TargetedAssetRefreshPlan plan = new TargetedAssetRefreshPlan();
            plan.BackgroundNames.Add(backgroundName.Trim());
            await ClientAssetRefreshService.RefreshTargetedAssetsAsync(owner, plan);

            if (currentClient != null)
            {
                SelectClient(currentClient);
            }
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

        private async Task DeleteCharacterFolderFromContextAsync(string characterName, string characterDirectory)
        {
            string targetPath = characterDirectory?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetPath) || !System.IO.Directory.Exists(targetPath))
            {
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Character folder was not found on disk.",
                    "Delete Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(characterName)
                ? System.IO.Path.GetFileName(targetPath)
                : characterName.Trim();
            MessageBoxResult confirmationResult = OceanyaMessageBox.Show(
                HostWindow,
                "Are you sure you want to delete " + displayName + " from your AO?\n\n"
                + targetPath
                + "\n\nThis process is not reversible.",
                "Delete Character Folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmationResult != MessageBoxResult.Yes)
            {
                return;
            }

            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            await WaitForm.ShowFormAsync("Deleting character folder...", owner);
            bool deleted = false;
            try
            {
                WaitForm.SetSubtitle("Releasing character from active views...");
                ReleaseCharacterFolderBeforeDelete(targetPath);
                await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                WaitForm.SetSubtitle("Deleting folder: " + displayName);
                await Task.Run(() => System.IO.Directory.Delete(targetPath, true));
                deleted = true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to delete character folder.", ex);
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Could not delete the character folder:\n\n"
                    + ex.Message
                    + "\n\nOceanya released its own active references first. If this keeps happening, another program may still be using files in that folder.",
                    "Delete Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                WaitForm.CloseForm();
            }

            if (deleted)
            {
                await RefreshCharacterAssetsAsync(displayName, refreshAllCharacters: false, refreshAllAssets: false);
            }
        }

        private void ReleaseCharacterFolderBeforeDelete(string targetPath)
        {
            string normalizedTarget = System.IO.Path.GetFullPath(targetPath.Trim());
            List<AOClient> clientsToUpdate = clientOrder
                .Where(client => clients.Values.Contains(client))
                .Concat(singleInternalClient != null ? new[] { singleInternalClient } : Array.Empty<AOClient>())
                .Distinct()
                .Where(client => IsCharacterFolderPath(client.currentINI?.DirectoryPath, normalizedTarget))
                .ToList();

            CharacterFolder? replacement = CharacterFolder.FullList.FirstOrDefault(character =>
                !IsCharacterFolderPath(character.DirectoryPath, normalizedTarget));

            foreach (AOClient client in clientsToUpdate)
            {
                if (replacement != null)
                {
                    client.SetCharacter(replacement);
                }
                else
                {
                    client.ClearCharacter();
                }
            }

            viewportContent?.ReleaseCharacterAssetsForDeletedFolder(normalizedTarget);
            ICMessageSettingsControl.ClearSettings();
            if (currentClient != null && clientsToUpdate.Contains(currentClient) && replacement != null)
            {
                ICMessageSettingsControl.SetClient(currentClient);
            }
        }

        private static bool IsCharacterFolderPath(string? candidatePath, string normalizedTarget)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            string normalizedCandidate = System.IO.Path.GetFullPath(candidatePath.Trim());
            return string.Equals(normalizedCandidate, normalizedTarget, StringComparison.OrdinalIgnoreCase);
        }

        private async Task OpenNewCharacterInEditorAsync()
        {
            Window owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
            AOCharacterFileCreatorWindow creator = new AOCharacterFileCreatorWindow();
            Window creatorWindow = OceanyaWindowManager.CreateWindow(creator);
            creatorWindow.Owner = owner;
            _ = creatorWindow.ShowDialog();
            if (creator.CharacterGenerationCompleted)
            {
                await RefreshCharacterAssetsAsync(null, refreshAllCharacters: true, refreshAllAssets: false);
            }
        }

        private async Task DuplicateCharacterInEditorAsync(string characterDirectory)
        {
            if (string.IsNullOrWhiteSpace(characterDirectory) || !System.IO.Directory.Exists(characterDirectory))
            {
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Character folder was not found on disk.",
                    "Duplicate Character Folder",
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
                WaitForm.SetSubtitle("Loading duplicate template...");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                loadedSuccessfully = creator.TryLoadCharacterFolderForDuplication(characterDirectory, out errorMessage);
            }
            finally
            {
                WaitForm.CloseForm();
            }

            if (!loadedSuccessfully)
            {
                OceanyaMessageBox.Show(
                    HostWindow,
                    "Could not load the selected character for duplication.\n"
                    + (string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error." : errorMessage),
                    "Duplicate Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Window editorWindow = OceanyaWindowManager.CreateWindow(creator);
            editorWindow.Owner = owner;
            _ = editorWindow.ShowDialog();
        }

        private void OpenCharacterInEmoteVisualizer(string characterDirectory)
        {
            CharacterFolder? character = CharacterFolder.FullList.FirstOrDefault(folder =>
                string.Equals(folder.DirectoryPath, characterDirectory, StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                return;
            }

            CharacterEmoteVisualizerWindow emoteVisualizerWindow = new CharacterEmoteVisualizerWindow(character)
            {
                Owner = HostWindow ?? Window.GetWindow(this)
            };
            emoteVisualizerWindow.ShowDialog();
        }


        private void RebindClientsToRefreshedCharacters()
        {
            List<AOClient> clientsToRebind = clientOrder
                .Where(client => clients.Values.Contains(client))
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

        private async void btnAreaNavigator_Click(object sender, RoutedEventArgs e)
        {
            RefreshAreaNavigatorForCurrentClient();
            AreaNavigatorPopup.IsOpen = true;
            AOClient? networkClient = currentClient == null ? null : GetTargetClientForNetwork(currentClient);
            if (networkClient != null && networkClient.IsTransportConnected)
            {
                try
                {
                    await networkClient.RequestAreaList();
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning("Failed to refresh area list before opening navigator.", ex);
                }
            }

            RefreshAreaNavigatorForCurrentClient();
        }

        private void btnMusicList_Click(object sender, RoutedEventArgs e)
        {
            MusicListPopup.IsOpen = true;
            _ = Dispatcher.BeginInvoke(new Action(RefreshMusicListForCurrentClient), DispatcherPriority.Background);
            _ = RefreshMusicListFromServerAndRefreshAsync();
        }

        private async void btnRefreshMusicList_Click(object sender, RoutedEventArgs e)
        {
            ResetLocalMusicAssetCache();
            EnsureLocalMusicAssetsScanStarted();
            await RefreshMusicListFromServerAsync();
            RefreshMusicListForCurrentClient();
        }

        private void txtMusicSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshMusicListForCurrentClient();
        }

        private async Task RefreshMusicListFromServerAsync()
        {
            AOClient? networkClient = currentClient == null ? null : GetTargetClientForNetwork(currentClient);
            if (networkClient == null || !networkClient.IsTransportConnected)
            {
                return;
            }

            try
            {
                await networkClient.RequestMusicList();
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to refresh music list before opening selector.", ex);
            }
        }

        private async Task RefreshMusicListFromServerAndRefreshAsync()
        {
            await RefreshMusicListFromServerAsync();
            await Dispatcher.InvokeAsync(RefreshMusicListForCurrentClient, DispatcherPriority.Background);
        }

        private async void treeMusic_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await PlaySelectedMusicAsync();
        }

        private async void btnStopMusic_Click(object sender, RoutedEventArgs e)
        {
            await StopMusicAsync();
        }

        private async void MusicStopMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await StopMusicAsync();
        }

        private async void MusicRandomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AOClient? profileClient = currentClient;
            AOClient? networkClient = profileClient == null ? null : GetTargetClientForNetwork(profileClient);
            if (networkClient == null)
            {
                return;
            }

            List<MusicListItem> songs = FlattenMusicItems(treeMusic.Items.OfType<MusicListItem>())
                .Where(item => item.IsPlayable)
                .ToList();
            if (songs.Count == 0)
            {
                return;
            }

            MusicListItem selectedSong = songs[Random.Shared.Next(songs.Count)];
            await PlayMusicItemAsync(selectedSong);
        }

        private void MusicExpandAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetMusicCategoryExpansion(isExpanded: true);
        }

        private void MusicCollapseAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetMusicCategoryExpansion(isExpanded: false);
        }

        private static void AddMusicMenuCategoryHeader(ContextMenu menu, string text, bool addLeadingSeparator)
        {
            if (addLeadingSeparator && menu.Items.Count > 0)
                menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock
                {
                    Text = text,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Margin = new Thickness(4, 2, 4, 1),
                },
                IsEnabled = false,
                StaysOpenOnClick = true,
            });
        }

        private void treeMusic_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? obj = e.OriginalSource as DependencyObject;
            while (obj != null)
            {
                if (obj is TreeViewItem tvi)
                {
                    tvi.IsSelected = true;
                    break;
                }

                obj = VisualTreeHelper.GetParent(obj);
            }
        }

        private void MusicContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            menu.Items.Clear();

            MusicListItem? item = treeMusic.SelectedItem as MusicListItem;
            bool hasLeafSelection = item != null && !item.IsCategory;
            bool isFrequent = item?.Playlist == "FREQUENTLY USED";
            bool isCustomCommand = item?.IsCustomCommand == true;
            bool isRootCategory = item?.IsRootCategory == true;
            bool musicEffectsSupported = !hasLeafSelection
                || TryResolveServerRecognizedMusicToken(item, out _);

            // PLAYBACK
            AddMusicMenuCategoryHeader(menu, "PLAYBACK", addLeadingSeparator: false);
            var stopItem = new MenuItem { Header = "Stop Current Song" };
            stopItem.Click += async (_, _) => await StopMusicAsync();
            menu.Items.Add(stopItem);
            var randomItem = new MenuItem { Header = "Play Random Song" };
            randomItem.Click += MusicRandomMenuItem_Click;
            menu.Items.Add(randomItem);

            // NAVIGATION
            AddMusicMenuCategoryHeader(menu, "NAVIGATION", addLeadingSeparator: true);
            var expandItem = new MenuItem { Header = "Expand All Categories" };
            expandItem.Click += MusicExpandAllMenuItem_Click;
            menu.Items.Add(expandItem);
            var collapseItem = new MenuItem { Header = "Collapse All Categories" };
            collapseItem.Click += MusicCollapseAllMenuItem_Click;
            menu.Items.Add(collapseItem);
            if (isRootCategory)
            {
                var moveUpItem = new MenuItem { Header = "Move Section Up" };
                moveUpItem.Click += MusicSectionMoveUpMenuItem_Click;
                menu.Items.Add(moveUpItem);
                var moveDownItem = new MenuItem { Header = "Move Section Down" };
                moveDownItem.Click += MusicSectionMoveDownMenuItem_Click;
                menu.Items.Add(moveDownItem);
            }

            // OPTIONS
            AddMusicMenuCategoryHeader(menu, "OPTIONS", addLeadingSeparator: true);
            var showAssetPathsItem = new MenuItem
            {
                Header = "Show Asset Paths",
                IsCheckable = true,
                IsChecked = SaveFile.Data.MusicListShowAssetPaths,
                StaysOpenOnClick = true,
            };
            showAssetPathsItem.Click += (_, _) =>
            {
                SaveFile.Data.MusicListShowAssetPaths = !SaveFile.Data.MusicListShowAssetPaths;
                SaveFile.Save();
                RefreshMusicListForCurrentClient();
            };
            menu.Items.Add(showAssetPathsItem);
            var fadeOutItem = new MenuItem
            {
                Header = "Fade Out Previous",
                ToolTip = GetMusicEffectTooltip(
                    musicEffectsSupported,
                    "When you start another song, the current song slowly gets quieter instead of stopping instantly."),
                IsCheckable = true,
                IsChecked = SaveFile.Data.MusicFlagFadeOut,
                IsEnabled = musicEffectsSupported,
                StaysOpenOnClick = true,
            };
            ToolTipService.SetShowOnDisabled(fadeOutItem, true);
            fadeOutItem.Click += (_, _) => { SaveFile.Data.MusicFlagFadeOut = !SaveFile.Data.MusicFlagFadeOut; SaveFile.Save(); };
            menu.Items.Add(fadeOutItem);
            var fadeInItem = new MenuItem
            {
                Header = "Fade In",
                ToolTip = GetMusicEffectTooltip(
                    musicEffectsSupported,
                    "The new song starts quiet and quickly becomes normal volume."),
                IsCheckable = true,
                IsChecked = SaveFile.Data.MusicFlagFadeIn,
                IsEnabled = musicEffectsSupported,
                StaysOpenOnClick = true,
            };
            ToolTipService.SetShowOnDisabled(fadeInItem, true);
            fadeInItem.Click += (_, _) => { SaveFile.Data.MusicFlagFadeIn = !SaveFile.Data.MusicFlagFadeIn; SaveFile.Save(); };
            menu.Items.Add(fadeInItem);
            var syncItem = new MenuItem
            {
                Header = "Synchronize",
                ToolTip = GetMusicEffectTooltip(
                    musicEffectsSupported,
                    "The new song starts at the same time position as the song that was already playing."),
                IsCheckable = true,
                IsChecked = SaveFile.Data.MusicFlagSync,
                IsEnabled = musicEffectsSupported,
                StaysOpenOnClick = true,
            };
            ToolTipService.SetShowOnDisabled(syncItem, true);
            syncItem.Click += (_, _) => { SaveFile.Data.MusicFlagSync = !SaveFile.Data.MusicFlagSync; SaveFile.Save(); };
            menu.Items.Add(syncItem);

            // SELECTION (only when a leaf item is selected)
            if (hasLeafSelection)
            {
                AddMusicMenuCategoryHeader(menu, "SELECTION", addLeadingSeparator: true);
                var renameItem = new MenuItem { Header = "Rename" };
                renameItem.Click += MusicRenameMenuItem_Click;
                menu.Items.Add(renameItem);
                if (!isCustomCommand)
                {
                    var addToCustomItem = new MenuItem { Header = "Add to Custom Commands" };
                    addToCustomItem.Click += MusicAddToCustomMenuItem_Click;
                    menu.Items.Add(addToCustomItem);
                }
            }

            // CUSTOM COMMANDS
            AddMusicMenuCategoryHeader(menu, "CUSTOM COMMANDS", addLeadingSeparator: true);
            var newCustomItem = new MenuItem { Header = "New Custom Command" };
            newCustomItem.Click += MusicNewCustomCommandMenuItem_Click;
            menu.Items.Add(newCustomItem);
            if (isCustomCommand)
            {
                var editCustomItem = new MenuItem { Header = "Edit Custom Command" };
                editCustomItem.Click += MusicEditCustomMenuItem_Click;
                menu.Items.Add(editCustomItem);
                var removeCustomItem = new MenuItem { Header = "Remove Custom Command" };
                removeCustomItem.Click += MusicRemoveCustomMenuItem_Click;
                menu.Items.Add(removeCustomItem);
            }

            // FREQUENTLY USED (only when selected item is from that section)
            if (isFrequent)
            {
                AddMusicMenuCategoryHeader(menu, "FREQUENTLY USED", addLeadingSeparator: true);
                var removeFrequentItem = new MenuItem { Header = "Remove from Frequently Used" };
                removeFrequentItem.Click += MusicRemoveFrequentMenuItem_Click;
                menu.Items.Add(removeFrequentItem);
            }
        }

        private static string GetMusicEffectTooltip(bool isSupported, string supportedTooltip)
        {
            if (isSupported)
            {
                return supportedTooltip;
            }

            return "Only works for songs the server recognizes. This selected item plays through /play or a custom command, so AO2 effect flags cannot be sent.";
        }

        private bool TryResolveServerRecognizedMusicToken(MusicListItem? item, out string musicToken)
        {
            musicToken = string.Empty;
            if (item == null || item.IsCategory || item.IsCustomCommand)
            {
                return false;
            }

            AOClient? profileClient = currentClient;
            AOClient? networkClient = profileClient == null ? null : GetTargetClientForNetwork(profileClient);
            return TryResolveServerRecognizedMusicToken(
                item,
                networkClient?.GetAvailableMusicSnapshot() ?? Array.Empty<string>(),
                out musicToken);
        }

        private static bool TryResolveServerRecognizedMusicToken(
            MusicListItem item,
            IReadOnlyList<string> musicEntries,
            out string musicToken)
        {
            musicToken = string.Empty;
            if (item.IsCategory || item.IsCustomCommand)
            {
                return false;
            }

            string[] candidates =
            {
                item.PlayToken?.Trim() ?? string.Empty,
                item.Token?.Trim() ?? string.Empty,
                StripCustomPrefix(item.PlayToken?.Trim() ?? string.Empty),
                StripCustomPrefix(item.Token?.Trim() ?? string.Empty),
            };

            foreach (string entryRaw in musicEntries)
            {
                string entry = entryRaw.Trim();
                if (!LooksLikeMusicEntry(entry))
                {
                    continue;
                }

                foreach (string candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        continue;
                    }

                    if (string.Equals(entry, candidate, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(entry, StripCustomPrefix(candidate), StringComparison.OrdinalIgnoreCase))
                    {
                        musicToken = entry;
                        return true;
                    }
                }
            }

            return false;
        }

        private void MusicRenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || item.IsCategory)
            {
                return;
            }

            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var dialog = new MusicRenameDialog(item.DisplayName) { Owner = owner };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string newName = dialog.ResultName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
            {
                SaveFile.Data.MusicCustomNames.Remove(item.Token);
            }
            else
            {
                SaveFile.Data.MusicCustomNames[item.Token] = newName;
            }

            SaveFile.Save();
            RefreshMusicListForCurrentClient();
        }

        private void MusicNewCustomCommandMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var editor = new CustomMusicCommandEditorWindow { Owner = owner };
            editor.ShowDialog();
            RefreshMusicListForCurrentClient();
        }

        private void MusicAddToCustomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || item.IsCategory || item.IsCustomCommand)
            {
                return;
            }

            string prefillName = item.DisplayName;
            string prefillCommand = "/play " + item.PlayToken;

            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var editor = new CustomMusicCommandEditorWindow(prefillName, prefillCommand) { Owner = owner };
            editor.ShowDialog();
            RefreshMusicListForCurrentClient();
        }

        private void MusicEditCustomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || !item.IsCustomCommand)
            {
                return;
            }

            CustomMusicCommand? cmd = SaveFile.Data.CustomMusicCommands
                .FirstOrDefault(c => string.Equals(c.Id, item.CustomCommandId, StringComparison.Ordinal));
            if (cmd == null)
            {
                return;
            }

            Window? owner = HostWindow ?? Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var editor = new CustomMusicCommandEditorWindow(cmd) { Owner = owner };
            editor.ShowDialog();
            RefreshMusicListForCurrentClient();
        }

        private void MusicRemoveCustomMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || !item.IsCustomCommand)
            {
                return;
            }

            SaveFile.Data.CustomMusicCommands.RemoveAll(
                c => string.Equals(c.Id, item.CustomCommandId, StringComparison.Ordinal));
            SaveFile.Save();
            RefreshMusicListForCurrentClient();
        }

        private void MusicRemoveFrequentMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || item.Playlist != "FREQUENTLY USED")
            {
                return;
            }

            SaveFile.Data.FrequentlyUsedMusic.Remove(item.Token);
            SaveFile.Save();
            RefreshMusicListForCurrentClient();
        }

        private void MusicTreeItemExpansionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem treeViewItem
                || treeViewItem.DataContext is not MusicListItem item
                || !item.IsCategory
                || string.IsNullOrWhiteSpace(item.CategoryKey))
            {
                return;
            }

            HashSet<string> collapsedKeys = GetMusicCollapsedCategoryKeys();
            if (treeViewItem.IsExpanded)
            {
                collapsedKeys.Remove(item.CategoryKey);
            }
            else
            {
                collapsedKeys.Add(item.CategoryKey);
            }

            SaveFile.Data.MusicListCollapsedCategoryKeys = collapsedKeys
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            SaveFile.Save();
            e.Handled = true;
        }

        private void MusicSectionMoveUpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || !item.IsRootCategory) return;
            List<string> order = SaveFile.Data.MusicSectionOrder;
            int idx = order.IndexOf(item.DisplayName);
            if (idx <= 0) return;
            order.RemoveAt(idx);
            order.Insert(idx - 1, item.DisplayName);
            SaveFile.Save();
            RefreshMusicListForCurrentClient();
        }

        private void MusicSectionMoveDownMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (treeMusic.SelectedItem is not MusicListItem item || !item.IsRootCategory) return;
            List<string> order = SaveFile.Data.MusicSectionOrder;
            int idx = order.IndexOf(item.DisplayName);
            if (idx < 0 || idx >= order.Count - 1) return;
            order.RemoveAt(idx);
            order.Insert(idx + 1, item.DisplayName);
            SaveFile.Save();
            RefreshMusicListForCurrentClient();
        }

        private async Task PlaySelectedMusicAsync()
        {
            if (treeMusic.SelectedItem is MusicListItem selectedItem)
            {
                await PlayMusicItemAsync(selectedItem);
            }
        }

        private async Task PlayMusicItemAsync(MusicListItem selectedItem)
        {
            if (selectedItem.IsCategory || string.IsNullOrWhiteSpace(selectedItem.Token))
            {
                return;
            }

            AOClient? profileClient = currentClient;
            AOClient? networkClient = profileClient == null ? null : GetTargetClientForNetwork(profileClient);
            if (networkClient == null)
            {
                return;
            }

            if (useSingleInternalClient && profileClient != null)
            {
                ApplyProfileToSingleInternalClient(profileClient);
            }

            if (selectedItem.IsCustomCommand)
            {
                CustomConsole.Info($"[MUSIC] Custom command OOC: {selectedItem.PlayToken}", CustomConsole.LogCategory.MusicList);
                await networkClient.SendOOCMessage(selectedItem.PlayToken);
                string cmdKey = selectedItem.CustomCommandId;
                if (!SaveFile.Data.FrequentlyUsedMusic.ContainsKey(cmdKey))
                {
                    SaveFile.Data.FrequentlyUsedMusic[cmdKey] = 0;
                }

                SaveFile.Data.FrequentlyUsedMusic[cmdKey]++;
                SaveFile.Save();
                RefreshMusicListForCurrentClient();
                MusicListPopup.IsOpen = false;
                return;
            }

            string playToken = string.IsNullOrWhiteSpace(selectedItem.PlayToken) ? selectedItem.Token : selectedItem.PlayToken;
            bool isUrl = AO2ViewportAudioResolver.IsStreamingUrl(playToken);
            bool canSendEffects = TryResolveServerRecognizedMusicToken(
                selectedItem,
                networkClient.GetAvailableMusicSnapshot(),
                out string serverRecognizedToken);
            bool useOocPlay = !canSendEffects
                && (selectedItem.Playlist == "LOCAL FILES" || selectedItem.Playlist == "FREQUENTLY USED");

            if (useOocPlay)
            {
                // Local/frequently-used tracks bypass server validation via OOC /play.
                // tsuserverCC uses shlex.split(), so the argument must be quoted to handle
                // spaces in song names. It also prepends "custom/" to non-URL tokens, so
                // "../token" resolves back to sounds/music/. URLs are sent as-is (no prefix).
                // tsuserver3 takes the raw arg string and needs no quoting.
                string oocMessage;
                if (networkClient.IsTsuServerCC)
                {
                    string oocArg = isUrl ? playToken : "../" + playToken;
                    oocMessage = $"/play \"{oocArg}\"";
                }
                else
                {
                    oocMessage = "/play " + playToken;
                }

                CustomConsole.Info(
                    $"[MUSIC] OOC play → {oocMessage} (server={networkClient.ServerSoftware}, isUrl={isUrl})",
                    CustomConsole.LogCategory.MusicList);
                await networkClient.SendOOCMessage(oocMessage);
            }
            else
            {
                if (canSendEffects)
                {
                    playToken = serverRecognizedToken;
                }

                CustomConsole.Info(
                    $"[MUSIC] MC packet play: token={playToken}, effectFlags={MusicEffectFlags}, isUrl={isUrl}",
                    CustomConsole.LogCategory.MusicList);
                await networkClient.PlayMusic(playToken, MusicEffectFlags);
            }

            // Track play count under the resolved token.
            if (!SaveFile.Data.FrequentlyUsedMusic.ContainsKey(playToken))
            {
                SaveFile.Data.FrequentlyUsedMusic[playToken] = 0;
            }

            SaveFile.Data.FrequentlyUsedMusic[playToken]++;
            SaveFile.Save();
            RefreshMusicListForCurrentClient();
            MusicListPopup.IsOpen = false;
        }

        private async Task StopMusicAsync()
        {
            AOClient? profileClient = currentClient;
            AOClient? networkClient = profileClient == null ? null : GetTargetClientForNetwork(profileClient);
            if (networkClient == null)
            {
                return;
            }

            if (useSingleInternalClient && profileClient != null)
            {
                ApplyProfileToSingleInternalClient(profileClient);
            }

            CustomConsole.Info($"[MUSIC] Stop: effectFlags={MusicEffectFlags}", CustomConsole.LogCategory.MusicList);
            await networkClient.StopMusic(MusicEffectFlags);
            currentMusicToken = string.Empty;
            currentMusicPlaylist = string.Empty;
            mainMusicAudioManager.StopMusic(MusicEffectFlags);
            RefreshMusicListForCurrentClient();
            MusicListPopup.IsOpen = false;
        }

        private void ApplySavedPopupSettings()
        {
            AreaNavigatorPopupSurface.Width = SaveFile.Data.AreaNavigatorPopupWidth;
            AreaNavigatorPopupSurface.Height = SaveFile.Data.AreaNavigatorPopupHeight;
            MusicListPopupSurface.Width = SaveFile.Data.MusicListPopupWidth;
            MusicListPopupSurface.Height = SaveFile.Data.MusicListPopupHeight;
            // MusicListShowAssetPaths state is read dynamically when the context menu opens.
        }

        private void AreaNavigatorRightResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizePopupSurface(AreaNavigatorPopupSurface, e.HorizontalChange, 0, 220, 220, 900, 900);
        }

        private void AreaNavigatorTopResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizePopupSurface(AreaNavigatorPopupSurface, 0, -e.VerticalChange, 220, 220, 900, 900);
        }

        private void AreaNavigatorTopRightResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizePopupSurface(AreaNavigatorPopupSurface, e.HorizontalChange, -e.VerticalChange, 220, 220, 900, 900);
        }

        private void MusicListRightResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizePopupSurface(MusicListPopupSurface, e.HorizontalChange, 0, 260, 300, 1000, 1000);
        }

        private void MusicListTopResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizePopupSurface(MusicListPopupSurface, 0, -e.VerticalChange, 260, 300, 1000, 1000);
        }

        private void MusicListTopRightResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            ResizePopupSurface(MusicListPopupSurface, e.HorizontalChange, -e.VerticalChange, 260, 300, 1000, 1000);
        }

        private void PopupResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SavePopupSurfaceSizes();
        }

        private void SavePopupSurfaceSizes()
        {
            SaveFile.Data.AreaNavigatorPopupWidth = AreaNavigatorPopupSurface.Width;
            SaveFile.Data.AreaNavigatorPopupHeight = AreaNavigatorPopupSurface.Height;
            SaveFile.Data.MusicListPopupWidth = MusicListPopupSurface.Width;
            SaveFile.Data.MusicListPopupHeight = MusicListPopupSurface.Height;
            SaveFile.Save();
        }

        private static void ResizePopupSurface(
            FrameworkElement surface,
            double horizontalChange,
            double verticalChange,
            double minWidth,
            double minHeight,
            double maxWidth,
            double maxHeight)
        {
            double currentWidth = double.IsNaN(surface.Width) || surface.Width <= 0 ? surface.ActualWidth : surface.Width;
            double currentHeight = double.IsNaN(surface.Height) || surface.Height <= 0 ? surface.ActualHeight : surface.Height;
            surface.Width = Math.Clamp(currentWidth + horizontalChange, minWidth, maxWidth);
            surface.Height = Math.Clamp(currentHeight + verticalChange, minHeight, maxHeight);
        }

        private static IEnumerable<MusicListItem> FlattenMusicItems(IEnumerable<MusicListItem> items)
        {
            foreach (MusicListItem item in items)
            {
                yield return item;
                foreach (MusicListItem child in FlattenMusicItems(item.Children))
                {
                    yield return child;
                }
            }
        }

        private void SetMusicCategoryExpansion(bool isExpanded)
        {
            foreach (MusicListItem item in FlattenMusicItems(treeMusic.Items.OfType<MusicListItem>()))
            {
                if (item.IsCategory)
                {
                    item.IsExpanded = isExpanded;
                }
            }

            SaveFile.Data.MusicListCollapsedCategoryKeys = isExpanded
                ? new List<string>()
                : FlattenMusicItems(treeMusic.Items.OfType<MusicListItem>())
                    .Where(item => item.IsCategory && !string.IsNullOrWhiteSpace(item.CategoryKey))
                    .Select(item => item.CategoryKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            SaveFile.Save();
            treeMusic.Items.Refresh();
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
                    networkClient.AvailableAreaInfos.Select(areaInfo => areaInfo.Name).ToList());
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

        private class MusicListItem
        {
            public string DisplayName { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
            public string AssetPath { get; set; } = string.Empty;
            public string Playlist { get; set; } = string.Empty;
            public string CategoryKey { get; set; } = string.Empty;
            public string? Tooltip { get; set; }
            public bool IsCategory { get; set; }
            public bool IsRootCategory { get; set; }
            public bool IsPlayable { get; set; }
            public bool IsCustomCommand { get; set; }
            public string CustomCommandId { get; set; } = string.Empty;
            public bool IsExpanded { get; set; } = true;
            public ObservableCollection<MusicListItem> Children { get; } = new ObservableCollection<MusicListItem>();
            public Brush RowBackground { get; set; } = Brushes.Transparent;
            public Brush TitleBrush { get; set; } = Brushes.Gainsboro;
            public FontWeight FontWeight { get; set; } = FontWeights.Normal;
            public Thickness Padding { get; set; } = new Thickness(7, 4, 7, 4);
            public Visibility AssetPathVisibility { get; set; } = Visibility.Collapsed;
            public string PlayToken { get; set; } = string.Empty;
        }

        private sealed record MusicListBuildSnapshot(
            List<string> MusicEntries,
            string Filter,
            bool ShowAssetPaths,
            HashSet<string> CollapsedKeys,
            Dictionary<string, int> FrequentlyUsedMusic,
            List<CustomMusicCommand> CustomCommands,
            Dictionary<string, string> CustomNames,
            List<string> SectionOrder,
            string CurrentMusicToken,
            IReadOnlyList<MusicAssetEntry> LocalMusicAssets,
            IReadOnlyDictionary<string, string> LocalMusicIndex);

        private sealed class NaturalStringComparer : IComparer<string>
        {
            public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

            private NaturalStringComparer() { }

            public int Compare(string? x, string? y)
            {
                if (x is null && y is null) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return StrCmpLogicalW(x, y);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        private void FlashTaskbar()
        {
            Window? hostWindow = HostWindow;
            if (hostWindow == null || hostWindow.IsActive || viewportWindow?.IsActive == true)
            {
                return;
            }

            Window flashWindow = IsViewportUsingWindowsPreview() && viewportWindow != null
                ? viewportWindow
                : hostWindow;
            IntPtr flashHandle = new WindowInteropHelper(flashWindow).Handle;
            if (flashHandle == IntPtr.Zero)
            {
                return;
            }

            FLASHWINFO fi = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = flashHandle,
                dwFlags = 0x00000002 | 0x0000000C, // FLASHW_TRAY | FLASHW_TIMERNOFG
                uCount = 5,
                dwTimeout = 0
            };
            FlashWindowEx(ref fi);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hwnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hwnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong(hwnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

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
            Key normalizedKey = e.Key == Key.System ? e.SystemKey : e.Key;
            // If the Control modifier is pressed but AltGr is active, skip processing.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && !isAltGrActive)
            {
                if (TryHandleTextBoxClipboardShortcut(e))
                {
                    base.OnPreviewKeyDown(e);
                    return;
                }

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
                    AOClient client = clientOrder
                        .Where(existing => clients.Values.Contains(existing))
                        .ElementAt(index);
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

        private static bool TryHandleTextBoxClipboardShortcut(KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is not TextBox textBox || textBox.IsReadOnly)
            {
                return false;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.X)
            {
                string selection = textBox.SelectedText ?? string.Empty;
                if (selection.Length == 0)
                {
                    return false;
                }

                e.Handled = true;
                if (ClipboardUtilities.TrySetText(selection))
                {
                    textBox.SelectedText = string.Empty;
                }

                return true;
            }

            if (key == Key.C)
            {
                string selection = textBox.SelectedText ?? string.Empty;
                if (selection.Length == 0)
                {
                    return false;
                }

                e.Handled = true;
                ClipboardUtilities.TrySetText(selection);
                return true;
            }

            if (key == Key.V)
            {
                e.Handled = true;
                if (ClipboardUtilities.TryGetText(out string text))
                {
                    int caretIndex = textBox.SelectionStart;
                    textBox.SelectedText = text;
                    textBox.CaretIndex = caretIndex + text.Length;
                }

                return true;
            }

            return false;
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
