using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.ChatPreview;
using OceanyaClient.Utilities;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// AO2-native 256x192 viewport surface for rendering background, characters, effects, desks, and chat.
    /// </summary>
    public partial class AO2ViewportControl : UserControl
    {
        private AOClient? sceneClient;
        private AOClient? messageSourceClient;
        private Func<ICMessage, bool>? messageFilter;
        private Func<string, bool>? actionFilter;
        private DispatcherTimer? pendingMessageTimer;
        private DispatcherTimer? chatTextTimer;
        private DispatcherTimer? screenShakeTimer;
        private int messageSequence;
        private int chatTextCrawlMilliseconds = DefaultChatTextCrawlMilliseconds;
        private int chatBlipRate = DefaultBlipRate;
        private bool chatBlankBlipEnabled;
        private bool chatboxOverlapsViewport;
        private AO2ViewportThemeLayout? currentThemeLayout;
        private AO2ViewportBlipPlaybackRules.BlipCrawlState? chatBlipState;
        private string chatFullText = string.Empty;
        private string chatPrefixText = string.Empty;
        private string additivePreviousText = string.Empty;
        private bool currentChatAdditive;
        private bool chatRevealStartedForCurrentMessage;
        private bool immediatePreAnimActive;
        private string currentChatBlipToken = string.Empty;
        private string currentChatArrowMiscToken = string.Empty;
        private CharacterFolder? currentChatCharacter;
        private string currentChatEmote = string.Empty;
        private int currentChatSequence;
        private string[] chatMarkupStart = Array.Empty<string>();
        private string[] chatMarkupEnd = Array.Empty<string>();
        private bool[] chatMarkupRemove = Array.Empty<bool>();
        private readonly Dictionary<Image, IAnimationPlayer> animationPlayers = new Dictionary<Image, IAnimationPlayer>();
        private readonly Dictionary<Image, PlacedImageState> placedImageStates = new Dictionary<Image, PlacedImageState>();
        private readonly AO2ViewportAudioManager audioManager = new AO2ViewportAudioManager();
        private readonly Random screenShakeRandom = new Random();
        private readonly TranslateTransform backgroundShakeTransform = new TranslateTransform();
        private readonly TranslateTransform characterShakeTransform = new TranslateTransform();
        private readonly TranslateTransform pairCharacterShakeTransform = new TranslateTransform();
        private readonly TranslateTransform chatShakeTransform = new TranslateTransform();
        private readonly Dictionary<Image, CancellationTokenSource> pendingAsyncLoads = new Dictionary<Image, CancellationTokenSource>();
        private string currentRenderPosition = string.Empty;
        private CharacterFolder? currentRenderCharacter;
        private bool testimonyVisible;
        private IAnimationPlayer? chatArrowPlayer;
        private const int DefaultChatTextCrawlMilliseconds = 40;
        private const int DefaultBlipRate = 2;
        private const int ScreenShakeDurationMilliseconds = 300;
        private const int ScreenShakeIntervalMilliseconds = 20;

        public event EventHandler? SurfaceLayoutChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="AO2ViewportControl"/> class.
        /// </summary>
        public AO2ViewportControl()
        {
            InitializeComponent();
            BackgroundImage.RenderTransform = backgroundShakeTransform;
            ChatPreview.RenderTransform = chatShakeTransform;
            audioManager.RefreshVolumes();
            chatboxOverlapsViewport = SaveFile.Data.GMViewportChatboxOverlapsViewport;
            ApplyThemeLayout();
            ApplySavedChatBackground();
            IsVisibleChanged += OnIsVisibleChanged;
            Unloaded += (_, _) => audioManager.Dispose();
        }

        public int SurfaceWidth => currentThemeLayout?.SurfaceWidth ?? AO2ViewportAssetResolver.ViewportToolWidth;

        public int SurfaceHeight => currentThemeLayout?.SurfaceHeight ?? AO2ViewportAssetResolver.ViewportToolHeight;

        public bool ChatboxOverlapsViewport
        {
            get => chatboxOverlapsViewport;
            set
            {
                if (chatboxOverlapsViewport == value)
                {
                    return;
                }

                chatboxOverlapsViewport = value;
                SaveFile.Data.GMViewportChatboxOverlapsViewport = value;
                SaveFile.Save();
                ApplyThemeLayout();
                ReapplyScenePlacementAfterLayoutChange();
                ChatPreview.RefreshPreview();
            }
        }

        /// <summary>
        /// Applies current saved volume settings to all active audio players.
        /// </summary>
        internal void RefreshVolumes()
        {
            audioManager.RefreshVolumes();
        }

        internal void ReloadThemeLayout()
        {
            ApplyThemeLayout();
            ApplySavedChatBackground();
            ReapplyScenePlacementAfterLayoutChange();
            ChatPreview.RefreshPreview();
            if (ChatArrowImage.Visibility == Visibility.Visible)
            {
                ApplyChatArrowBounds();
            }
        }

        /// <summary>
        /// Current background token rendered by this viewport.
        /// </summary>
        public string CurrentBackgroundName => ResolveCurrentBackgroundName();

        /// <summary>
        /// Current speaker/character folder rendered by this viewport.
        /// </summary>
        public CharacterFolder? CurrentCharacter => currentRenderCharacter ?? sceneClient?.currentINI;

        /// <summary>
        /// Current chatbox token resolved for the rendered character.
        /// </summary>
        public string CurrentChatboxName =>
            !string.IsNullOrWhiteSpace(ChatPreview.ChatToken)
                ? ChatPreview.ChatToken
                : AO2ViewportAssetResolver.ResolveCharacterChatToken(CurrentCharacter);

        internal void PickChatBackgroundColor()
        {
            Color initialColor = TryParseSavedChatBackgroundColor(SaveFile.Data.GMViewportChatBackgroundColor)
                ?? Color.FromArgb(180, 0, 0, 0);

            Color? selected = AOCharacterFileCreatorWindow.ShowSolidColorPickerDialog(Window.GetWindow(this), initialColor);
            if (selected.HasValue)
            {
                SetChatBackgroundColor(selected.Value);
            }
        }

        private void ApplySavedChatBackground()
        {
            SetChatBackgroundBrush(TryParseSavedChatBackgroundColor(SaveFile.Data.GMViewportChatBackgroundColor));
        }

        private void ApplyThemeLayout(string? chatTokenOverride = null, bool? hasShownameOverride = null)
        {
            string chatToken = !string.IsNullOrWhiteSpace(chatTokenOverride)
                ? chatTokenOverride
                : !string.IsNullOrWhiteSpace(ChatPreview.ChatToken)
                ? ChatPreview.ChatToken
                : AO2ViewportAssetResolver.ResolveCharacterChatToken(CurrentCharacter);
            bool hasShowname = hasShownameOverride ?? !string.IsNullOrWhiteSpace(ChatPreview.PreviewShowname);
            AO2ViewportThemeLayout layout = AO2ChatPreviewResolver.ResolveViewportLayout(
                chatToken,
                hasShowname,
                chatboxOverlapsViewport);

            int oldWidth = SurfaceWidth;
            int oldHeight = SurfaceHeight;
            currentThemeLayout = layout;

            Width = layout.SurfaceWidth;
            Height = layout.SurfaceHeight;
            ViewportRoot.Width = layout.SurfaceWidth;
            ViewportRoot.Height = layout.SurfaceHeight;
            ChatArrowCanvas.Width = layout.SurfaceWidth;
            ChatArrowCanvas.Height = layout.SurfaceHeight;

            Canvas.SetLeft(ViewportCanvas, layout.ViewportLeft);
            Canvas.SetTop(ViewportCanvas, layout.ViewportTop);
            SetViewportLayerSize(layout.ViewportBounds.Width, layout.ViewportBounds.Height);

            Canvas.SetLeft(ChatPreview, layout.ChatboxLeft);
            Canvas.SetTop(ChatPreview, layout.ChatboxTop);
            ChatPreview.Width = layout.ChatboxBounds.Width;
            ChatPreview.Height = layout.ChatboxBounds.Height;

            AO2ViewportAssetResolver.SetViewportSurfaceDimensions(
                layout.ViewportBounds.Width,
                layout.ViewportBounds.Height,
                layout.SurfaceWidth,
                layout.SurfaceHeight,
                chatboxOverlapsViewport ? 0 : layout.ChatboxBounds.Height);

            if (oldWidth != layout.SurfaceWidth || oldHeight != layout.SurfaceHeight)
            {
                SurfaceLayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SetViewportLayerSize(int width, int height)
        {
            ViewportCanvas.Width = width;
            ViewportCanvas.Height = height;
            ViewportFallbackImage.Width = width;
            ViewportFallbackImage.Height = height;
            BackgroundImage.Width = width;
            BackgroundImage.Height = height;
            SpeedlinesImage.Width = width;
            SpeedlinesImage.Height = height;
            CharacterImage.Width = width;
            CharacterImage.Height = height;
            PairCharacterImage.Width = width;
            PairCharacterImage.Height = height;
            DeskImage.Width = width;
            DeskImage.Height = height;
            EffectImage.Width = width;
            EffectImage.Height = height;
            ShoutOverlayImage.Width = width;
            ShoutOverlayImage.Height = height;
            StickerImage.Width = width;
            StickerImage.Height = height;
            TestimonyImage.Width = width;
            TestimonyImage.Height = height;
            WtceImage.Width = width;
            WtceImage.Height = height;
            FlashOverlay.Width = width;
            FlashOverlay.Height = height;
        }

        private void ReapplyScenePlacementAfterLayoutChange()
        {
            string backgroundName = ResolveCurrentBackgroundName();
            string position = !string.IsNullOrWhiteSpace(currentRenderPosition)
                ? currentRenderPosition
                : sceneClient != null
                    ? ResolveCurrentOrDefaultViewportPosition(sceneClient)
                    : string.Empty;

            AO2ViewportAssetResolver.ViewportDisplayOptions displayOptions =
                AO2ViewportAssetResolver.ResolveDisplayOptions(backgroundName);
            RenderOptions.SetBitmapScalingMode(BackgroundImage, displayOptions.ScalingMode);
            RenderOptions.SetBitmapScalingMode(DeskImage, displayOptions.ScalingMode);

            AO2ViewportAssetResolver.ViewportImagePlacement backgroundPlacement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement(backgroundName, position);
            SetPlacedAnimatedImage(
                BackgroundImage,
                backgroundPlacement,
                !string.IsNullOrWhiteSpace(backgroundPlacement.ImagePath),
                displayOptions.StretchMode);

            bool deskWasVisible = DeskImage.Visibility == Visibility.Visible && DeskImage.Source != null;
            AO2ViewportAssetResolver.ViewportImagePlacement deskPlacement = deskWasVisible
                ? AO2ViewportAssetResolver.ResolveDeskPlacement(backgroundName, position)
                : new AO2ViewportAssetResolver.ViewportImagePlacement(
                    null,
                    0,
                    0,
                    AO2ViewportAssetResolver.ViewportWidth,
                    AO2ViewportAssetResolver.ViewportHeight);
            SetPlacedAnimatedImage(DeskImage, deskPlacement, deskWasVisible, displayOptions.StretchMode);

            ApplyHeightBasedCharacterGeometry(CharacterImage);
            ApplyHeightBasedCharacterGeometry(PairCharacterImage);

            if (ChatArrowImage.Visibility == Visibility.Visible)
            {
                ApplyChatArrowBounds();
            }
        }

        internal void SetChatBackgroundColor(Color? color)
        {
            SaveFile.Data.GMViewportChatBackgroundColor = color.HasValue
                ? $"#{color.Value.A:X2}{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}"
                : string.Empty;
            SaveFile.Save();
            SetChatBackgroundBrush(color);
        }

        private void SetChatBackgroundBrush(Color? color)
        {
            ChatPreview.ChatSectionBackground = color.HasValue
                ? new SolidColorBrush(color.Value)
                : Brushes.Transparent;
            ChatPreview.RefreshPreview();
        }

        internal void ReleaseCharacterAssetsForDeletedFolder(string normalizedCharacterDirectory)
        {
            CharacterFolder? character = CurrentCharacter;
            if (character == null || !IsSameDirectory(character.DirectoryPath, normalizedCharacterDirectory))
            {
                return;
            }

            ClearScene();
        }

        private static bool IsSameDirectory(string? candidatePath, string normalizedTarget)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(candidatePath.Trim()),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase);
        }

        private static Color? TryParseSavedChatBackgroundColor(string? value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            try
            {
                object? converted = ColorConverter.ConvertFromString(normalized);
                return converted is Color color ? color : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Attaches this viewport to a GM client profile and refreshes the scene immediately.
        /// </summary>
        public void AttachClient(AOClient? client)
        {
            AttachClient(client, client);
        }

        /// <summary>
        /// Attaches this viewport to a GM profile while listening to the AO2 client that receives server echoes.
        /// </summary>
        public void AttachClient(AOClient? client, AOClient? incomingMessageClient)
        {
            AttachClient(client, incomingMessageClient, null, null);
        }

        /// <summary>
        /// Attaches this viewport to a GM profile while listening through optional incoming-message filters.
        /// </summary>
        public void AttachClient(
            AOClient? client,
            AOClient? incomingMessageClient,
            Func<ICMessage, bool>? messageFilter,
            Func<string, bool>? actionFilter)
        {
            if (ReferenceEquals(sceneClient, client)
                && ReferenceEquals(messageSourceClient, incomingMessageClient)
                && ReferenceEquals(this.messageFilter, messageFilter)
                && ReferenceEquals(this.actionFilter, actionFilter))
            {
                return;
            }

            bool hadClient = sceneClient != null || messageSourceClient != null;
            bool sameMessageSource = ReferenceEquals(messageSourceClient, incomingMessageClient ?? client);
            DetachClientEvents();
            sceneClient = client;
            messageSourceClient = incomingMessageClient ?? client;
            this.messageFilter = messageFilter;
            this.actionFilter = actionFilter;
            AttachClientEvents();
            if (sceneClient == null && messageSourceClient == null)
            {
                ClearScene();
                return;
            }

            if (!hadClient || !sameMessageSource)
            {
                RenderBackgroundOnly();
            }
        }

        /// <summary>
        /// Directly renders a preview IC message without a live client connection.
        /// Call after <see cref="AttachClient"/> to show the initial scene, then call this to trigger the emote.
        /// </summary>
        internal void PreviewMessage(ICMessage message)
        {
            Dispatcher.Invoke(() => RenderMessage(message));
        }

        private void AttachClientEvents()
        {
            if (sceneClient != null)
            {
                sceneClient.OnBGChange += OnBackgroundChanged;
            }

            if (messageSourceClient != null)
            {
                messageSourceClient.OnICMessageReceived += OnICMessageReceived;
                messageSourceClient.OnIcActionReceived += OnIcActionReceived;
                messageSourceClient.OnMusicChanged += OnMusicChanged;
                messageSourceClient.OnRtReceived += OnRtReceived;
            }
        }

        private void DetachClientEvents()
        {
            if (sceneClient != null)
            {
                sceneClient.OnBGChange -= OnBackgroundChanged;
            }

            if (messageSourceClient != null)
            {
                messageSourceClient.OnICMessageReceived -= OnICMessageReceived;
                messageSourceClient.OnIcActionReceived -= OnIcActionReceived;
                messageSourceClient.OnMusicChanged -= OnMusicChanged;
                messageSourceClient.OnRtReceived -= OnRtReceived;
            }
        }

        private void OnICMessageReceived(ICMessage message)
        {
            if (messageFilter != null && !messageFilter(message))
            {
                return;
            }

            Dispatcher.Invoke(() => RenderMessage(message));
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                audioManager.RefreshVolumes();
                return;
            }

            StopScreenShake();
            FlashOverlay.BeginAnimation(OpacityProperty, null);
            FlashOverlay.Visibility = Visibility.Collapsed;
            FlashOverlay.Opacity = 0;
            audioManager.StopAll();
        }

        private void OnIcActionReceived(string showName, string action, bool isSentFromSelf, ICMessage.TextColors textColor)
        {
        }

        private void OnMusicChanged(string showName, string? songPath, bool loop, int channel, int effectFlags)
        {
            if (channel != 0)
            {
                if (actionFilter != null && !actionFilter(showName))
                {
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (!IsVisible)
                    {
                        return;
                    }

                    audioManager.PlayAmbientMusic(channel, songPath, loop);
                });
                return;
            }

            if (actionFilter != null && !actionFilter(showName))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (!IsVisible)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(songPath))
                {
                    audioManager.StopMusic(effectFlags);
                    return;
                }

                audioManager.PlayMusic(songPath, loop, effectFlags);
            });
        }

        private void OnBackgroundChanged(string background)
        {
            Dispatcher.Invoke(() =>
            {
                StopScreenShake();
                audioManager.StopSfxAndBlips();
                RenderBackgroundOnly();
            });
        }

        private void RenderBackgroundOnly()
        {
            if (sceneClient == null)
            {
                ClearScene();
                return;
            }

            StopChatTextTimer();
            StopScreenShake();
            audioManager.StopSfxAndBlips();
            FlashOverlay.BeginAnimation(OpacityProperty, null);
            FlashOverlay.Visibility = Visibility.Collapsed;
            ApplyThemeLayout();
            string backgroundName = ResolveCurrentBackgroundName();
            AO2ViewportAssetResolver.ViewportDisplayOptions displayOptions =
                AO2ViewportAssetResolver.ResolveDisplayOptions(backgroundName);
            string backgroundOnlyPosition = ResolveCurrentOrDefaultViewportPosition(sceneClient);
            AO2ViewportAssetResolver.ViewportImagePlacement backgroundPlacement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement(
                    backgroundName,
                    backgroundOnlyPosition);
            RenderOptions.SetBitmapScalingMode(BackgroundImage, displayOptions.ScalingMode);
            RenderOptions.SetBitmapScalingMode(DeskImage, displayOptions.ScalingMode);
            SetPlacedAnimatedImage(BackgroundImage, backgroundPlacement, true, displayOptions.StretchMode);
            SetAnimatedImage(CharacterImage, null, false);
            SetAnimatedImage(PairCharacterImage, null, false);
            SetPlacedImage(
                DeskImage,
                new AO2ViewportAssetResolver.ViewportImagePlacement(
                    null,
                    0,
                    0,
                    AO2ViewportAssetResolver.ViewportWidth,
                    AO2ViewportAssetResolver.ViewportHeight),
                false,
                Stretch.Fill);
            SetAnimatedImage(EffectImage, null, false);
            SetAnimatedImage(SpeedlinesImage, null, false);
            SetAnimatedImage(ShoutOverlayImage, null, false);
            StopChatArrow();
            testimonyVisible = false;
            TestimonyImage.Source = null;
            TestimonyImage.Visibility = Visibility.Collapsed;
            HideWtceOverlay();
            StopAnimation(StickerImage);
            StickerImage.Visibility = Visibility.Collapsed;
            StopAnimation(EvidenceImage);
            EvidenceImage.Visibility = Visibility.Collapsed;
            currentRenderPosition = backgroundOnlyPosition;
            currentRenderCharacter = sceneClient.currentINI;
            ChatPreview.PreviewText = string.Empty;
            ChatPreview.Visibility = Visibility.Collapsed;
            additivePreviousText = string.Empty;
            chatPrefixText = string.Empty;
            currentChatAdditive = false;
            chatRevealStartedForCurrentMessage = false;
            currentChatBlipToken = string.Empty;
            currentChatArrowMiscToken = string.Empty;
        }

        private static string ResolveCurrentOrDefaultViewportPosition(AOClient client)
        {
            if (!string.IsNullOrWhiteSpace(client.curPos))
            {
                return client.curPos;
            }

            return client.currentINI?.configINI?.Side ?? string.Empty;
        }

        private string ResolveCurrentBackgroundName()
        {
            if (!string.IsNullOrWhiteSpace(messageSourceClient?.curBG))
            {
                return messageSourceClient.curBG;
            }

            return sceneClient?.curBG ?? string.Empty;
        }

        private void RenderMessage(ICMessage message)
        {
            messageSequence++;
            StopChatArrow();
            chatRevealStartedForCurrentMessage = false;
            immediatePreAnimActive = false;
            StopPendingMessageTimer();
            StopScreenShake();
            CharacterFolder? character = AO2ViewportAssetResolver.ResolveCharacter(message.Character);
            string background = ResolveCurrentBackgroundName();
            string position = message.Side?.Trim() ?? string.Empty;
            string showname = string.IsNullOrWhiteSpace(message.ShowName)
                ? character?.configINI.ShowName ?? message.Character
                : message.ShowName;

            int sequence = messageSequence;
            Action renderSpeaking = () =>
            {
                if (sequence != messageSequence)
                {
                    return;
                }

                RenderSpeakingScene(background, position, character, showname, message, startTextReveal: true);
            };
            Action renderAo2Message = () =>
            {
                if (sequence != messageSequence)
                {
                    return;
                }

                bool hasPreAnimation = !string.IsNullOrWhiteSpace(
                    AO2ViewportAssetResolver.ResolveCharacterPreAnimation(character, message.PreAnim));
                bool shouldBlockForPreAnimation =
                    AO2ViewportAssetResolver.ShouldBlockForPreAnimation(message.EmoteModifier);
                bool shouldPlayImmediatePreAnimation = AO2ViewportAssetResolver.ShouldPlayImmediatePreAnimation(
                    message.EmoteModifier,
                    message.NonInterruptingPreAnim);
                if (hasPreAnimation && (shouldBlockForPreAnimation || shouldPlayImmediatePreAnimation))
                {
                    RenderPreAnimationThenSpeaking(
                        background,
                        position,
                        character,
                        showname,
                        message,
                        renderSpeaking,
                        shouldPlayImmediatePreAnimation);
                    return;
                }

                renderSpeaking();
            };

            if (message.ShoutModifier != ICMessage.ShoutModifiers.Nothing)
            {
                ShowShoutOverlay(message);
                ScheduleContinuation(AO2ViewportAssetResolver.GetShoutDuration(), renderAo2Message);
                return;
            }

            renderAo2Message();
        }

        private void RenderPreAnimationThenSpeaking(
            string background,
            string position,
            CharacterFolder? character,
            string showname,
            ICMessage message,
            Action renderSpeaking,
            bool immediate)
        {
            string messageText = Globals.ReplaceTextForSymbols(message.Message);

            if (immediate)
            {
                renderSpeaking = () => RenderSpeakingScene(
                    background,
                    position,
                    character,
                    showname,
                    message,
                    startTextReveal: false);
            }

            // Build the continuation handler before RenderScene so it can be passed as the
            // onCharacterPlayerReady callback.  SetCharacterAnimatedImageAsync calls it
            // synchronously when frames are already cached, or on the dispatcher once an
            // async decode finishes — whichever comes first.  The `continued` guard ensures
            // renderSpeaking is called exactly once even if the callback fires multiple times.
            bool continued = false;
            Action<IAnimationPlayer?> handlePreAnimationPlayer = preAnimationPlayer =>
            {
                if (preAnimationPlayer != null)
                {
                    if (immediate)
                    {
                        immediatePreAnimActive = true;
                    }

                    preAnimationPlayer.PlaybackFinished += () =>
                    {
                        if (continued)
                        {
                            return;
                        }

                        continued = true;
                        immediatePreAnimActive = false;
                        renderSpeaking();
                    };
                    return;
                }

                if (continued)
                {
                    return;
                }

                if (immediate)
                {
                    continued = true;
                    renderSpeaking();
                    return;
                }

                ScheduleContinuation(AO2ViewportAssetResolver.GetPreAnimationDuration(character, message.PreAnim), () =>
                {
                    if (continued)
                    {
                        return;
                    }

                    continued = true;
                    renderSpeaking();
                });
            };

            RenderScene(
                background,
                position,
                character,
                message.Emote,
                message.DeskMod,
                message.EffectString,
                message.Effect,
                message.Flip,
                showChat: immediate && !string.IsNullOrWhiteSpace(message.Message),
                showname: Globals.ReplaceTextForSymbols(showname),
                messageText: messageText,
                message: message,
                phase: ViewportPhase.PreAnimation,
                startTextReveal: immediate,
                onCharacterPlayerReady: handlePreAnimationPlayer);
        }

        private void RenderSpeakingScene(
            string background,
            string position,
            CharacterFolder? character,
            string showname,
            ICMessage message,
            bool startTextReveal)
        {
            RenderScene(
                background,
                position,
                character,
                message.Emote,
                message.DeskMod,
                message.EffectString,
                message.Effect,
                message.Flip,
                showChat: !string.IsNullOrWhiteSpace(message.Message),
                showname: Globals.ReplaceTextForSymbols(showname),
                messageText: Globals.ReplaceTextForSymbols(message.Message),
                message: message,
                phase: ViewportPhase.Speaking,
                startTextReveal: startTextReveal);
        }

        private void RenderScene(
            string? backgroundName,
            string? position,
            CharacterFolder? character,
            string? emoteName,
            ICMessage.DeskMods deskMod,
            string? effectString,
            ICMessage.Effects effect,
            bool flip,
            bool showChat,
            string? showname,
            string? messageText,
            ICMessage? message = null,
            ViewportPhase phase = ViewportPhase.Speaking,
            bool startTextReveal = true,
            Action<IAnimationPlayer?>? onCharacterPlayerReady = null)
        {
            string chatToken = AO2ViewportAssetResolver.ResolveCharacterChatToken(character);
            bool hasShowname = !string.IsNullOrWhiteSpace(showname);
            ApplyThemeLayout(chatToken, hasShowname);
            AO2ViewportAssetResolver.ViewportDisplayOptions displayOptions =
                AO2ViewportAssetResolver.ResolveDisplayOptions(backgroundName);
            currentRenderCharacter = character;
            AO2ViewportAssetResolver.ViewportImagePlacement backgroundPlacement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement(backgroundName, position);
            CustomConsole.Debug(
                $"Render scene bg=\"{backgroundName}\" pos=\"{position}\" bgImage=\"{backgroundPlacement.ImagePath ?? "(null)"}\" left={backgroundPlacement.Left:0.##} top={backgroundPlacement.Top:0.##} size={backgroundPlacement.Width:0.##}x{backgroundPlacement.Height:0.##} viewport={AO2ViewportAssetResolver.ViewportWidth}x{AO2ViewportAssetResolver.ViewportHeight}",
                CustomConsole.LogCategory.Viewport);
            RenderOptions.SetBitmapScalingMode(BackgroundImage, displayOptions.ScalingMode);
            RenderOptions.SetBitmapScalingMode(DeskImage, displayOptions.ScalingMode);
            double bgOldLeft = Canvas.GetLeft(BackgroundImage);
            double deskOldLeft = Canvas.GetLeft(DeskImage);
            SetPlacedAnimatedImage(BackgroundImage, backgroundPlacement, true, displayOptions.StretchMode);

            bool isPreAnimation = phase == ViewportPhase.PreAnimation;
            bool isZoom = message != null && AO2ViewportAssetResolver.IsZoomEmote(message.EmoteModifier);
            bool centerAndHidePair = message != null
                && (isPreAnimation
                    ? AO2ViewportAssetResolver.ShouldCenterAndHidePairDuringPreAnimation(message.DeskMod)
                    : AO2ViewportAssetResolver.ShouldCenterAndHidePairDuringSpeaking(message.DeskMod, message.EmoteModifier));
            AO2ChatPreviewStyle talkingStyle = AO2ChatPreviewResolver.Resolve(
                chatToken,
                !string.IsNullOrWhiteSpace(showname),
                preferViewportTheme: true);
            bool useTalkingSprite = !isPreAnimation
                && showChat
                && message != null
                && !string.IsNullOrWhiteSpace(message.Message)
                && talkingStyle.ChatMarkupTalking[(int)message.TextColor];
            AO2ViewportAssetResolver.ResolvedCharacterAnimation resolvedCharacterAnimation = isPreAnimation
                ? AO2ViewportAssetResolver.ResolveCharacterPreAnimationDetails(character, message?.PreAnim)
                : AO2ViewportAssetResolver.ResolveCharacterDialogAnimationDetails(character, emoteName, useTalkingSprite);
            CustomConsole.Debug(
                $"Render character char=\"{character?.configINI?.Name ?? "(null)"}\" emote=\"{emoteName}\" talking={useTalkingSprite} assetPath=\"{resolvedCharacterAnimation.AssetPath ?? "(null)"}\"",
                CustomConsole.LogCategory.Viewport);
            Action<int>? frameHandler = message == null
                ? null
                : BuildFrameEffectHandler(
                    message,
                    character,
                    resolvedCharacterAnimation.ResolvedToken,
                    messageSequence);
            SetCharacterAnimatedImageAsync(
                CharacterImage,
                resolvedCharacterAnimation.AssetPath,
                !string.IsNullOrWhiteSpace(resolvedCharacterAnimation.AssetPath),
                loop: !isPreAnimation,
                onFrameChanged: frameHandler,
                onPlayerReady: onCharacterPlayerReady);
            CharacterImage.RenderTransformOrigin = new Point(0.5, 0.5);
            CharacterImage.RenderTransform = BuildCharacterTransform(flip, characterShakeTransform);
            ApplyCharacterOffset(CharacterImage, centerAndHidePair ? (0, 0) : message?.SelfOffset ?? (0, 0));

            bool shouldShowDesk = isZoom && !isPreAnimation
                ? false
                : isPreAnimation
                    ? AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(deskMod, position)
                    : AO2ViewportAssetResolver.ShouldShowDesk(deskMod, position);
            AO2ViewportAssetResolver.ViewportImagePlacement deskPlacement = shouldShowDesk
                ? AO2ViewportAssetResolver.ResolveDeskPlacement(backgroundName, position)
                : new AO2ViewportAssetResolver.ViewportImagePlacement(null, 0, 0, AO2ViewportAssetResolver.ViewportWidth, AO2ViewportAssetResolver.ViewportHeight);
            SetPlacedAnimatedImage(DeskImage, deskPlacement, shouldShowDesk, displayOptions.StretchMode);

            RenderPairCharacter(message, character, centerAndHidePair || isZoom || isPreAnimation);
            RenderSpeedlines(message, position, character, phase == ViewportPhase.Speaking && isZoom);
            if (phase == ViewportPhase.Speaking)
            {
                RenderEffect(effectString, effect, character, flip, message?.SelfOffset ?? (0, 0));
            }
            else
            {
                StopAnimation(EffectImage);
                EffectImage.Visibility = Visibility.Collapsed;
            }

            RenderShoutOverlay(message, phase);

            ChatPreview.MessageColorIndex = message == null ? 0 : (int)message.TextColor;
            ChatPreview.MessageColorOverride = ResolveViewportMessageColor(message);
            ChatPreview.PreviewShowname = showname ?? string.Empty;
            bool isAdditiveMessage = message?.Additive == true;
            string additivePrefix = isAdditiveMessage ? additivePreviousText : string.Empty;
            if (!isAdditiveMessage)
            {
                additivePreviousText = string.Empty;
            }

            bool shouldStartTextReveal = showChat
                && startTextReveal
                && (phase == ViewportPhase.Speaking || phase == ViewportPhase.PreAnimation);
            if (shouldStartTextReveal)
            {
                ChatPreview.PreviewText = string.Empty;
            }
            else if (!chatRevealStartedForCurrentMessage)
            {
                ChatPreview.PreviewText = additivePrefix + (messageText ?? string.Empty);
            }
            ChatPreview.ChatToken = chatToken;
            ChatPreview.ShowShowname = hasShowname;
            ChatPreview.ShowMessage = showChat;
            ChatPreview.Visibility = showChat ? Visibility.Visible : Visibility.Collapsed;
            if (message != null && IsVisible)
            {
                bool hasPreAnimation = !string.IsNullOrWhiteSpace(
                    AO2ViewportAssetResolver.ResolveCharacterPreAnimation(character, message.PreAnim));
                if (phase == ViewportPhase.PreAnimation || !hasPreAnimation)
                {
                    bool shakeAtSfxTime = phase == ViewportPhase.PreAnimation
                        || AO2ViewportAssetResolver.ShouldBlockForPreAnimation(message.EmoteModifier);
                    ScheduleMessageSfxPlayback(message, messageSequence, shakeAtSfxTime);
                }

                if (phase == ViewportPhase.Speaking)
                {
                    PlayEffectSfx(message, character);
                    PlayLegacyRealization(message, character);
                }

                if (ShouldApplyImmediateScreenShake(message, phase))
                {
                    StartScreenShake(messageSequence);
                }
            }

            if (showChat)
            {
                currentChatBlipToken = ResolveViewportBlipToken(character, message);
                audioManager.PrepareBlip(currentChatBlipToken, character?.Name, showname);
                ChatPreview.RefreshPreview();
                if (shouldStartTextReveal)
                {
                    StartChatTextReveal(messageText ?? string.Empty, additivePrefix, character, message);
                }
            }
            else
            {
                StopChatTextTimer();
                currentChatBlipToken = string.Empty;
            }

            // Track position for slide transitions
            string newPosition = position ?? string.Empty;
            bool positionChanged = !string.Equals(currentRenderPosition, newPosition, StringComparison.OrdinalIgnoreCase);
            bool shouldSlide = message?.Slide == true && positionChanged && phase == ViewportPhase.Speaking;
            currentRenderPosition = newPosition;
            if (shouldSlide)
            {
                AnimateBackgroundSlide(bgOldLeft, deskOldLeft);
            }

            // Show character sticker for this message
            if (phase == ViewportPhase.Speaking && character != null)
            {
                ShowSticker(message?.Character, AO2ViewportAssetResolver.ResolveCharacterChatToken(character));
            }
            else if (phase != ViewportPhase.Speaking)
            {
                StopAnimation(StickerImage);
                StickerImage.Visibility = Visibility.Collapsed;
            }

            // Show evidence presentation overlay if packet has evidence
            if (phase == ViewportPhase.Speaking && message != null
                && int.TryParse((message.EvidenceID ?? string.Empty).Trim(), out int evidenceId)
                && evidenceId > 0)
            {
                ShowEvidenceOverlay(evidenceId, position ?? string.Empty, message);
            }
            else if (phase == ViewportPhase.Speaking)
            {
                StopAnimation(EvidenceImage);
                EvidenceImage.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowShoutOverlay(ICMessage message)
        {
            RenderShoutOverlay(message, ViewportPhase.Shout);
            if (IsVisible)
            {
                string shoutToken = ResolveShoutSfxToken(message);
                string miscToken = AO2ViewportAssetResolver.ResolveCharacterChatToken(
                    AO2ViewportAssetResolver.ResolveCharacter(message.Character));
                audioManager.PlayShoutSfx(shoutToken, message.Character, miscToken, message.ShowName);
            }

            StopChatTextTimer();
            ChatPreview.Visibility = Visibility.Collapsed;
            ChatPreview.ShowMessage = false;
        }

        private void ClearScene()
        {
            StopChatTextTimer();
            StopScreenShake();
            BackgroundImage.Source = null;
            CharacterImage.Source = null;
            DeskImage.Source = null;
            EffectImage.Source = null;
            PairCharacterImage.Source = null;
            SpeedlinesImage.Source = null;
            ShoutOverlayImage.Source = null;
            FlashOverlay.BeginAnimation(OpacityProperty, null);
            FlashOverlay.Visibility = Visibility.Collapsed;
            FlashOverlay.Opacity = 0;
            StopAllAnimations();
            StopChatArrow();
            HideTestimonyOverlay();
            HideWtceOverlay();
            StopAnimation(StickerImage);
            StickerImage.Visibility = Visibility.Collapsed;
            StopAnimation(EvidenceImage);
            EvidenceImage.Visibility = Visibility.Collapsed;
            CancelPendingLoad(BackgroundImage);
            CancelPendingLoad(DeskImage);
            CancelPendingLoad(CharacterImage);
            CancelPendingLoad(PairCharacterImage);
            audioManager.StopSfxAndBlips();
            ChatPreview.PreviewText = string.Empty;
            ChatPreview.Visibility = Visibility.Collapsed;
            additivePreviousText = string.Empty;
            chatPrefixText = string.Empty;
            currentChatAdditive = false;
            chatRevealStartedForCurrentMessage = false;
            immediatePreAnimActive = false;
            currentChatBlipToken = string.Empty;
            currentRenderCharacter = null;
            CharacterImage.Visibility = Visibility.Collapsed;
            PairCharacterImage.Visibility = Visibility.Collapsed;
            DeskImage.Visibility = Visibility.Collapsed;
            EffectImage.Visibility = Visibility.Collapsed;
            SpeedlinesImage.Visibility = Visibility.Collapsed;
            ShoutOverlayImage.Visibility = Visibility.Collapsed;
        }

        internal static bool MessageRequestsScreenShake(ICMessage? message)
        {
            return message?.ScreenShake == true;
        }

        private static bool ShouldApplyImmediateScreenShake(ICMessage? message, ViewportPhase phase)
        {
            if (phase != ViewportPhase.Speaking || !MessageRequestsScreenShake(message))
            {
                return false;
            }

            AO2ViewportAssetResolver.NormalizedEmoteModifier modifier =
                AO2ViewportAssetResolver.NormalizeEmoteModifier(message!.EmoteModifier);
            return modifier == AO2ViewportAssetResolver.NormalizedEmoteModifier.Idle
                || modifier == AO2ViewportAssetResolver.NormalizedEmoteModifier.Zoom;
        }

        private void StartScreenShake(int sequence)
        {
            if (!IsVisible || !AO2ViewportAssetResolver.GetScreenShakeEnabled())
            {
                return;
            }

            StopScreenShake();

            ApplyScreenShakeOffset();
            DateTime startedAtUtc = DateTime.UtcNow;

            screenShakeTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(ScreenShakeIntervalMilliseconds)
            };
            screenShakeTimer.Tick += (_, _) =>
            {
                if (sequence != messageSequence || !IsVisible)
                {
                    StopScreenShake();
                    return;
                }

                double elapsedMs = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;
                if (elapsedMs >= ScreenShakeDurationMilliseconds)
                {
                    StopScreenShake();
                    return;
                }

                ApplyScreenShakeOffset();
            };
            screenShakeTimer.Start();
        }

        private void ApplyScreenShakeOffset()
        {
            int deviation = Math.Max(1, (int)(7.0 * (AO2ViewportAssetResolver.ViewportHeight / 192.0)));
            ApplyIndependentShakeOffset(backgroundShakeTransform, deviation);
            ApplyIndependentShakeOffset(characterShakeTransform, deviation);
            ApplyIndependentShakeOffset(pairCharacterShakeTransform, deviation);
            ApplyIndependentShakeOffset(chatShakeTransform, deviation);
        }

        private void ApplyIndependentShakeOffset(TranslateTransform transform, int deviation)
        {
            transform.X = screenShakeRandom.Next(-deviation, deviation);
            transform.Y = screenShakeRandom.Next(-deviation, deviation);
        }

        private void StopScreenShake()
        {
            if (screenShakeTimer != null)
            {
                screenShakeTimer.Stop();
                screenShakeTimer = null;
            }

            ResetShakeTransform(backgroundShakeTransform);
            ResetShakeTransform(characterShakeTransform);
            ResetShakeTransform(pairCharacterShakeTransform);
            ResetShakeTransform(chatShakeTransform);
        }

        private static void ResetShakeTransform(TranslateTransform transform)
        {
            transform.X = 0;
            transform.Y = 0;
        }

        private void CancelPendingLoad(Image image)
        {
            if (!pendingAsyncLoads.TryGetValue(image, out CancellationTokenSource? cts))
            {
                return;
            }

            pendingAsyncLoads.Remove(image);
            cts.Cancel();
            cts.Dispose();
        }

        private IAnimationPlayer? SetAnimatedImage(
            Image image,
            string? path,
            bool visible,
            bool loop = true,
            Action<int>? onFrameChanged = null)
        {
            StopAnimation(image);
            if (!visible || string.IsNullOrWhiteSpace(path))
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                return null;
            }

            if (Ao2AnimationPreview.TryCreateAnimationPlayer(
                    path,
                    loop,
                    out IAnimationPlayer? player,
                    usePreviewLimits: false,
                    maxDimensionOverride: null)
                && player != null)
            {
                animationPlayers[image] = player;
                player.FrameChanged += frame => image.Source = frame;
                if (onFrameChanged != null)
                {
                    player.FrameIndexChanged += onFrameChanged;
                }

                image.Source = player.CurrentFrame;
                image.Visibility = Visibility.Visible;
                onFrameChanged?.Invoke(player.CurrentFrameIndex);
                return player;
            }

            ImageSource? source = AO2ViewportAssetResolver.LoadImage(path, decodePixelWidth: 0);
            image.Source = source;
            image.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
            return null;
        }

        /// <summary>
        /// Loads an animated or static image into <paramref name="image"/>.
        /// When frames are already cached the player is assigned synchronously and
        /// <paramref name="onPlayerReady"/> fires before this method returns.
        /// When the animation has not yet been decoded, decoding runs on a background
        /// thread and <paramref name="onPlayerReady"/> is invoked on the dispatcher once
        /// it completes.  A stale decode is silently dropped if the image slot is
        /// re-assigned (e.g. the user sends another IC message) before it finishes.
        /// </summary>
        private void SetCharacterAnimatedImageAsync(
            Image image,
            string? path,
            bool visible,
            bool loop = true,
            Action<int>? onFrameChanged = null,
            Action<IAnimationPlayer?>? onPlayerReady = null)
        {
            CancelPendingLoad(image);

            if (!visible || string.IsNullOrWhiteSpace(path))
            {
                StopAnimation(image);
                image.Source = null;
                ResetCharacterImageGeometry(image);
                image.Visibility = Visibility.Collapsed;
                onPlayerReady?.Invoke(null);
                return;
            }

            string? resolvedPath = Ao2AnimationPreview.ResolveAo2ImagePath(path);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                IAnimationPlayer? syncPlayer = SetAnimatedImage(image, path, visible, loop, onFrameChanged);
                if (syncPlayer != null)
                {
                    syncPlayer.FrameChanged += _ => ApplyHeightBasedCharacterGeometry(image);
                }
                ApplyHeightBasedCharacterGeometry(image);
                onPlayerReady?.Invoke(syncPlayer);
                return;
            }

            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(resolvedPath);
            bool isAnimated = Ao2AnimationPreview.IsPotentialAnimatedPath(resolvedPath);
            int characterDecodeTargetHeight = AO2ViewportAssetResolver.ViewportHeight;
            bool isCached = isAnimated && Ao2AnimationPreview.IsAnimationCached(
                resolvedPath,
                lastWriteTimeUtc,
                characterDecodeTargetHeight,
                cacheDimensionIsTargetHeight: true);

            // Fast path: static image or viewport-sized frames already cached — assign synchronously.
            if (!isAnimated)
            {
                IAnimationPlayer? syncPlayer = SetAnimatedImage(image, resolvedPath, visible, loop, onFrameChanged);
                if (syncPlayer != null)
                {
                    syncPlayer.FrameChanged += _ => ApplyHeightBasedCharacterGeometry(image);
                }
                ApplyHeightBasedCharacterGeometry(image);
                onPlayerReady?.Invoke(syncPlayer);
                return;
            }

            if (isCached)
            {
                StopAnimation(image);
                if (Ao2AnimationPreview.TryCreateAnimationPlayerFromCachedTargetHeight(
                        resolvedPath,
                        loop,
                        characterDecodeTargetHeight,
                        out IAnimationPlayer? cachedPlayer)
                    && cachedPlayer != null)
                {
                    animationPlayers[image] = cachedPlayer;
                    cachedPlayer.FrameChanged += frame =>
                    {
                        image.Source = frame;
                        ApplyHeightBasedCharacterGeometry(image);
                    };
                    if (onFrameChanged != null)
                    {
                        cachedPlayer.FrameIndexChanged += onFrameChanged;
                    }

                    image.Source = cachedPlayer.CurrentFrame;
                    ApplyHeightBasedCharacterGeometry(image);
                    image.Visibility = Visibility.Visible;
                    onFrameChanged?.Invoke(cachedPlayer.CurrentFrameIndex);
                    onPlayerReady?.Invoke(cachedPlayer);
                    return;
                }
            }

            // Slow path: animated and not yet decoded.
            // Clear the slot immediately so the old sprite does not linger, then decode
            // on a background thread (mirrors AO2's AnimationLoader async approach).
            StopAnimation(image);
            image.Source = null;
            image.Visibility = Visibility.Collapsed;

            CancellationTokenSource cts = new CancellationTokenSource();
            pendingAsyncLoads[image] = cts;
            string capturedPath = resolvedPath;
            bool capturedLoop = loop;
            Action<int>? capturedOnFrameChanged = onFrameChanged;
            Action<IAnimationPlayer?>? capturedOnPlayerReady = onPlayerReady;
            int capturedTargetHeight = characterDecodeTargetHeight;

            string capturedExt = Path.GetExtension(capturedPath).ToLowerInvariant();
            if (capturedExt == ".webp" || capturedExt == ".gif")
            {
                // Streaming path: show frame 0 the moment it is decoded and add
                // remaining frames incrementally — matches AO2's AnimationLoader behaviour.
                BitmapFrameAnimationPlayer streamPlayer = BitmapFrameAnimationPlayer.CreateForStreaming(capturedLoop);
                bool firstFrameDispatched = false;

                Task.Run(() =>
                {
                    void HandleFrame(BitmapSource frame, TimeSpan duration)
                    {
                        if (cts.Token.IsCancellationRequested)
                        {
                            return;
                        }

                        streamPlayer.EnqueueStreamedFrame(frame, duration);

                        if (!firstFrameDispatched)
                        {
                            firstFrameDispatched = true;
                            Dispatcher.BeginInvoke(() =>
                            {
                                if (!pendingAsyncLoads.TryGetValue(image, out CancellationTokenSource? currentCts)
                                    || !ReferenceEquals(currentCts, cts))
                                {
                                    return;
                                }

                                StopAnimation(image);
                                animationPlayers[image] = streamPlayer;
                                streamPlayer.FrameChanged += f =>
                                {
                                    image.Source = f;
                                    ApplyHeightBasedCharacterGeometry(image);
                                };
                                if (capturedOnFrameChanged != null)
                                {
                                    streamPlayer.FrameIndexChanged += capturedOnFrameChanged;
                                }

                                streamPlayer.BeginStreamedPlayback();
                                image.Source = streamPlayer.CurrentFrame;
                                ApplyHeightBasedCharacterGeometry(image);
                                image.Visibility = Visibility.Visible;
                                capturedOnFrameChanged?.Invoke(streamPlayer.CurrentFrameIndex);
                                capturedOnPlayerReady?.Invoke(streamPlayer);
                            });
                        }
                    }

                    _ = capturedExt == ".webp"
                        ? Ao2AnimationPreview.TryStreamWebPFrames(
                            capturedPath,
                            HandleFrame,
                            cts.Token,
                            capturedTargetHeight)
                        : Ao2AnimationPreview.TryStreamGifFrames(
                            capturedPath,
                            HandleFrame,
                            cts.Token,
                            capturedTargetHeight);

                    streamPlayer.SignalStreamingComplete();

                    bool dispatched = firstFrameDispatched;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (pendingAsyncLoads.TryGetValue(image, out CancellationTokenSource? currentCts)
                            && ReferenceEquals(currentCts, cts))
                        {
                            pendingAsyncLoads.Remove(image);
                            cts.Dispose();
                        }

                        if (!dispatched)
                        {
                            // Frame 0 never arrived (file unreadable or single-frame) — fall back to static load.
                            ImageSource? source = AO2ViewportAssetResolver.LoadImage(capturedPath, decodePixelWidth: 0);
                            image.Source = source;
                            ApplyHeightBasedCharacterGeometry(image);
                            image.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
                            capturedOnPlayerReady?.Invoke(null);
                        }
                    });
                });
            }
            else
            {
                // Non-WebP (GIF / APNG): decode all frames first, then show — existing behaviour.
                Task.Run(() =>
                {
                    Ao2AnimationPreview.TryCreateAnimationPlayer(
                        capturedPath,
                        capturedLoop,
                        out IAnimationPlayer? p,
                        usePreviewLimits: false,
                        maxDimensionOverride: null);
                    return p;
                }).ContinueWith(task =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!pendingAsyncLoads.TryGetValue(image, out CancellationTokenSource? currentCts)
                            || !ReferenceEquals(currentCts, cts))
                        {
                            return;
                        }

                        pendingAsyncLoads.Remove(image);
                        cts.Dispose();

                        if (task.IsFaulted || task.IsCanceled)
                        {
                            capturedOnPlayerReady?.Invoke(null);
                            return;
                        }

                        StopAnimation(image);
                        IAnimationPlayer? player = task.Result;
                        if (player != null)
                        {
                            animationPlayers[image] = player;
                            player.FrameChanged += frame =>
                            {
                                image.Source = frame;
                                ApplyHeightBasedCharacterGeometry(image);
                            };
                            if (capturedOnFrameChanged != null)
                            {
                                player.FrameIndexChanged += capturedOnFrameChanged;
                            }

                            image.Source = player.CurrentFrame;
                            ApplyHeightBasedCharacterGeometry(image);
                            image.Visibility = Visibility.Visible;
                            capturedOnFrameChanged?.Invoke(player.CurrentFrameIndex);
                        }
                        else
                        {
                            ImageSource? source = AO2ViewportAssetResolver.LoadImage(capturedPath, decodePixelWidth: 0);
                            image.Source = source;
                            ApplyHeightBasedCharacterGeometry(image);
                            image.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
                        }

                        capturedOnPlayerReady?.Invoke(player);
                    });
                }, TaskScheduler.Default);
            }
        }

        private static void SetPlacedImage(
            Image image,
            AO2ViewportAssetResolver.ViewportImagePlacement placement,
            bool visible,
            Stretch stretch)
        {
            ImageSource? source = AO2ViewportAssetResolver.LoadImage(placement.ImagePath, decodePixelWidth: 0);
            image.BeginAnimation(Canvas.LeftProperty, null);
            image.BeginAnimation(Canvas.TopProperty, null);
            image.Source = source;
            image.Width = placement.Width;
            image.Height = placement.Height;
            image.Stretch = stretch;
            Canvas.SetLeft(image, placement.Left);
            Canvas.SetTop(image, placement.Top);
            image.Visibility = visible && source != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private IAnimationPlayer? SetPlacedAnimatedImage(
            Image image,
            AO2ViewportAssetResolver.ViewportImagePlacement placement,
            bool visible,
            Stretch stretch)
        {
            image.BeginAnimation(Canvas.LeftProperty, null);
            image.BeginAnimation(Canvas.TopProperty, null);
            image.Width = placement.Width;
            image.Height = placement.Height;
            image.Stretch = stretch;
            Canvas.SetLeft(image, placement.Left);
            Canvas.SetTop(image, placement.Top);

            string path = placement.ImagePath ?? string.Empty;
            if (!visible || string.IsNullOrWhiteSpace(path))
            {
                CancelPendingLoad(image);
                placedImageStates.Remove(image);
                return SetAnimatedImage(image, null, false);
            }

            DateTime lastWriteTimeUtc = File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
            if (placedImageStates.TryGetValue(image, out PlacedImageState? current)
                && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase)
                && current.LastWriteTimeUtc == lastWriteTimeUtc
                && image.Source != null)
            {
                image.Visibility = Visibility.Visible;
                return animationPlayers.TryGetValue(image, out IAnimationPlayer? existingPlayer)
                    ? existingPlayer
                    : null;
            }

            CancelPendingLoad(image);
            placedImageStates.Remove(image);

            // For animated assets that are not already decoded, offload to a background thread
            // to avoid blocking the UI thread (mirrors AO2's AnimationLoader async approach).
            bool isAnimated = Ao2AnimationPreview.IsPotentialAnimatedPath(path);
            bool isCached = isAnimated && Ao2AnimationPreview.IsAnimationCached(path, lastWriteTimeUtc);
            if (isAnimated && !isCached)
            {
                string capturedPath = path;
                DateTime capturedWrite = lastWriteTimeUtc;
                double capturedLeft = placement.Left;
                double capturedTop = placement.Top;
                double capturedWidth = placement.Width;
                double capturedHeight = placement.Height;
                Stretch capturedStretch = stretch;

                CancellationTokenSource cts = new CancellationTokenSource();
                pendingAsyncLoads[image] = cts;

                Task.Run(() =>
                {
                    if (cts.Token.IsCancellationRequested)
                    {
                        return (IAnimationPlayer?)null;
                    }

                    Ao2AnimationPreview.TryCreateAnimationPlayer(
                        capturedPath,
                        true,
                        out IAnimationPlayer? p,
                        usePreviewLimits: false,
                        maxDimensionOverride: null);
                    return p;
                }).ContinueWith(task =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!pendingAsyncLoads.TryGetValue(image, out CancellationTokenSource? currentCts)
                            || !ReferenceEquals(currentCts, cts))
                        {
                            return;
                        }

                        pendingAsyncLoads.Remove(image);
                        cts.Dispose();

                        if (task.IsFaulted || task.IsCanceled)
                        {
                            return;
                        }

                        image.Width = capturedWidth;
                        image.Height = capturedHeight;
                        image.Stretch = capturedStretch;
                        Canvas.SetLeft(image, capturedLeft);
                        Canvas.SetTop(image, capturedTop);

                        StopAnimation(image);
                        IAnimationPlayer? player = task.Result;
                        if (player != null)
                        {
                            animationPlayers[image] = player;
                            player.FrameChanged += frame => image.Source = frame;
                            image.Source = player.CurrentFrame;
                            image.Visibility = Visibility.Visible;
                            placedImageStates[image] = new PlacedImageState(capturedPath, capturedWrite);
                        }
                        else
                        {
                            ImageSource? source = AO2ViewportAssetResolver.LoadImage(capturedPath, decodePixelWidth: 0);
                            image.Source = source;
                            image.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
                            if (image.Source != null)
                            {
                                placedImageStates[image] = new PlacedImageState(capturedPath, capturedWrite);
                            }
                        }
                    });
                }, TaskScheduler.Default);

                return null;
            }

            // Sync path: already cached or non-animated (instant decode).
            IAnimationPlayer? syncPlayer = SetAnimatedImage(image, path, true);
            if (image.Source != null)
            {
                placedImageStates[image] = new PlacedImageState(path, lastWriteTimeUtc);
            }

            return syncPlayer;
        }

        private static void ApplyOffset(Image image, (int Horizontal, int Vertical) offset)
        {
            Canvas.SetLeft(image, AO2ViewportAssetResolver.ViewportWidth * offset.Horizontal / 100.0);
            Canvas.SetTop(image, AO2ViewportAssetResolver.ViewportHeight * offset.Vertical / 100.0);
        }

        private (int Horizontal, int Vertical) characterPendingOffset = (0, 0);
        private (int Horizontal, int Vertical) pairCharacterPendingOffset = (0, 0);

        private void ApplyCharacterOffset(Image image, (int Horizontal, int Vertical) offset)
        {
            if (ReferenceEquals(image, CharacterImage))
                characterPendingOffset = offset;
            else if (ReferenceEquals(image, PairCharacterImage))
                pairCharacterPendingOffset = offset;
            ApplyCharacterOffsetNow(image, offset);
        }

        private static void ApplyCharacterOffsetNow(Image image, (int Horizontal, int Vertical) offset)
        {
            double width = double.IsNaN(image.Width) || image.Width <= 0
                ? AO2ViewportAssetResolver.ViewportWidth
                : image.Width;
            Canvas.SetLeft(
                image,
                ((AO2ViewportAssetResolver.ViewportWidth - width) / 2.0)
                    + AO2ViewportAssetResolver.ViewportWidth * offset.Horizontal / 100.0);
            Canvas.SetTop(image, AO2ViewportAssetResolver.ViewportHeight * offset.Vertical / 100.0);
        }

        private void ReapplyStoredCharacterOffset(Image image)
        {
            if (ReferenceEquals(image, CharacterImage))
                ApplyCharacterOffsetNow(image, characterPendingOffset);
            else if (ReferenceEquals(image, PairCharacterImage))
                ApplyCharacterOffsetNow(image, pairCharacterPendingOffset);
        }

        private void ApplyHeightBasedCharacterGeometry(Image image)
        {
            if (image.Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                ResetCharacterImageGeometry(image);
                ReapplyStoredCharacterOffset(image);
                return;
            }

            double scale = (double)AO2ViewportAssetResolver.ViewportHeight / bitmap.PixelHeight;
            image.Width = bitmap.PixelWidth * scale;
            image.Height = AO2ViewportAssetResolver.ViewportHeight;
            image.Stretch = Stretch.Fill;
            ReapplyStoredCharacterOffset(image);
        }

        private static void ResetCharacterImageGeometry(Image image)
        {
            image.Width = AO2ViewportAssetResolver.ViewportWidth;
            image.Height = AO2ViewportAssetResolver.ViewportHeight;
            image.Stretch = Stretch.Fill;
        }

        private static Transform BuildCharacterTransform(bool flipped, TranslateTransform shakeTransform)
        {
            TransformGroup group = new TransformGroup();
            if (flipped)
            {
                group.Children.Add(new ScaleTransform(-1, 1));
            }

            group.Children.Add(shakeTransform);
            return group;
        }

        private void RenderPairCharacter(ICMessage? message, CharacterFolder? fallbackCharacter, bool hidden)
        {
            if (message == null || hidden || message.OtherCharId < 0 || string.IsNullOrWhiteSpace(message.OtherName))
            {
                StopAnimation(PairCharacterImage);
                PairCharacterImage.Visibility = Visibility.Collapsed;
                return;
            }

            CharacterFolder? pairCharacter = AO2ViewportAssetResolver.ResolveCharacter(message.OtherName) ?? fallbackCharacter;
            string? pairPath = AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(
                pairCharacter,
                message.OtherEmote,
                talking: false);
            SetCharacterAnimatedImageAsync(PairCharacterImage, pairPath, !string.IsNullOrWhiteSpace(pairPath));
            PairCharacterImage.RenderTransformOrigin = new Point(0.5, 0.5);
            PairCharacterImage.RenderTransform = BuildCharacterTransform(message.OtherFlip, pairCharacterShakeTransform);
            ApplyCharacterOffset(PairCharacterImage, (message.OtherOffset, message.OtherOffsetVertical));

            bool pairInFront = AO2ViewportAssetResolver.GetPairOrdering(message.OtherCharIdRaw)
                == AO2ViewportAssetResolver.PairOrdering.PairInFront;
            Panel.SetZIndex(CharacterImage, pairInFront ? 3 : 4);
            Panel.SetZIndex(PairCharacterImage, pairInFront ? 4 : 3);
        }

        private void RenderSpeedlines(ICMessage? message, string? position, CharacterFolder? character, bool isZoom)
        {
            if (message == null || !isZoom)
            {
                StopAnimation(SpeedlinesImage);
                SpeedlinesImage.Visibility = Visibility.Collapsed;
                return;
            }

            string? speedlinesPath = AO2ViewportAssetResolver.ResolveSpeedlinesImage(position, character);
            SetAnimatedImage(SpeedlinesImage, speedlinesPath, !string.IsNullOrWhiteSpace(speedlinesPath));
        }

        private void RenderEffect(
            string? effectString,
            ICMessage.Effects effect,
            CharacterFolder? character,
            bool flip,
            (int Horizontal, int Vertical) selfOffset)
        {
            AO2ViewportAssetResolver.ViewportEffect resolvedEffect =
                AO2ViewportAssetResolver.ResolveEffect(effectString, effect, character, flip);
            if (IsRealizationEffect(effectString, effect))
            {
                DoFlash();
            }

            // Cull: stop any currently playing effect before starting the new one.
            if (resolvedEffect.Cull && EffectImage.Visibility == Visibility.Visible)
            {
                StopAnimation(EffectImage);
                EffectImage.Visibility = Visibility.Collapsed;
            }

            IAnimationPlayer? effectPlayer = SetAnimatedImage(EffectImage, resolvedEffect.ImagePath, !string.IsNullOrWhiteSpace(resolvedEffect.ImagePath), loop: resolvedEffect.Loop);
            if (effectPlayer != null && !resolvedEffect.Loop)
            {
                effectPlayer.PlaybackFinished += () => EffectImage.Visibility = Visibility.Collapsed;
            }

            // MaxDurationMs: auto-stop a looping effect after the configured time.
            if (resolvedEffect.MaxDurationMs.HasValue && resolvedEffect.Loop)
            {
                int sequence = messageSequence;
                int durationMs = resolvedEffect.MaxDurationMs.Value;
                DispatcherTimer maxDurationTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(durationMs)
                };
                maxDurationTimer.Tick += (_, _) =>
                {
                    maxDurationTimer.Stop();
                    if (sequence != messageSequence)
                    {
                        return;
                    }

                    StopAnimation(EffectImage);
                    EffectImage.Visibility = Visibility.Collapsed;
                };
                maxDurationTimer.Start();
            }

            EffectImage.Stretch = resolvedEffect.Stretch ? Stretch.Fill : Stretch.Uniform;
            EffectImage.RenderTransformOrigin = new Point(0.5, 0.5);
            EffectImage.RenderTransform = resolvedEffect.RespectFlip ? new ScaleTransform(-1, 1) : Transform.Identity;
            ApplyOffset(EffectImage, resolvedEffect.RespectOffset ? selfOffset : (0, 0));

            int zIndex = resolvedEffect.Layer switch
            {
                AO2ViewportAssetResolver.EffectLayer.BehindCharacter => 2,
                AO2ViewportAssetResolver.EffectLayer.Character => 4,
                AO2ViewportAssetResolver.EffectLayer.Over => 7,
                _ => 8
            };
            Panel.SetZIndex(EffectImage, zIndex);
        }

        private void RenderShoutOverlay(ICMessage? message, ViewportPhase phase)
        {
            if (message == null || phase != ViewportPhase.Shout)
            {
                StopAnimation(ShoutOverlayImage);
                ShoutOverlayImage.Visibility = Visibility.Collapsed;
                return;
            }

            string? shoutPath = AO2ViewportAssetResolver.ResolveShoutOverlayImage(
                message.ShoutModifier,
                message.Character,
                AO2ViewportAssetResolver.ResolveCharacterChatToken(AO2ViewportAssetResolver.ResolveCharacter(message.Character)));
            SetAnimatedImage(ShoutOverlayImage, shoutPath, !string.IsNullOrWhiteSpace(shoutPath), loop: false);
            ApplyHeightBasedShoutScaling();
            Panel.SetZIndex(ShoutOverlayImage, 9);
        }

        // AO2 parity: shout overlays scale so the image height fills the viewport height exactly,
        // preserving aspect ratio. Width may exceed the viewport and is clipped by the canvas.
        // This matches AO2's AnimationLayer::calculateFrameGeometry() height-based scale formula.
        private void ApplyHeightBasedShoutScaling()
        {
            if (ShoutOverlayImage.Source is not BitmapSource bitmap
                || bitmap.PixelHeight <= 0 || bitmap.PixelWidth <= 0)
            {
                ShoutOverlayImage.Width = AO2ViewportAssetResolver.ViewportWidth;
                ShoutOverlayImage.Height = AO2ViewportAssetResolver.ViewportHeight;
                ShoutOverlayImage.Stretch = Stretch.Uniform;
                Canvas.SetLeft(ShoutOverlayImage, 0);
                Canvas.SetTop(ShoutOverlayImage, 0);
                return;
            }

            double scale = (double)AO2ViewportAssetResolver.ViewportHeight / bitmap.PixelHeight;
            double scaledWidth = bitmap.PixelWidth * scale;
            ShoutOverlayImage.Width = scaledWidth;
            ShoutOverlayImage.Height = AO2ViewportAssetResolver.ViewportHeight;
            ShoutOverlayImage.Stretch = Stretch.Fill;
            Canvas.SetLeft(ShoutOverlayImage, (AO2ViewportAssetResolver.ViewportWidth - scaledWidth) / 2.0);
            Canvas.SetTop(ShoutOverlayImage, 0);
        }

        private static string ResolveShoutSfxToken(ICMessage message)
        {
            return message.ShoutModifier switch
            {
                ICMessage.ShoutModifiers.HoldIt => "holdit",
                ICMessage.ShoutModifiers.Objection => "objection",
                ICMessage.ShoutModifiers.TakeThat => "takethat",
                ICMessage.ShoutModifiers.Custom => "custom",
                _ => string.Empty
            };
        }

        private static bool IsRealizationEffect(string? effectString, ICMessage.Effects effect)
        {
            if (effect == ICMessage.Effects.Realization)
            {
                return true;
            }

            string token = (effectString ?? string.Empty).Split('|')[0].Trim();
            return string.Equals(token, "realization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "flash", StringComparison.OrdinalIgnoreCase);
        }

        private void StartChatTextReveal(string text, string prefixText, CharacterFolder? character, ICMessage? message)
        {
            StopChatTextTimer();
            chatRevealStartedForCurrentMessage = true;
            chatFullText = text ?? string.Empty;
            chatPrefixText = prefixText ?? string.Empty;
            currentChatAdditive = message?.Additive == true;
            currentChatCharacter = character;
            currentChatEmote = message?.Emote ?? string.Empty;
            currentChatSequence = messageSequence;
            currentChatArrowMiscToken = ChatPreview.ChatToken;
            chatTextCrawlMilliseconds = AO2ViewportAssetResolver.GetTextCrawlMilliseconds();
            chatBlipRate = AO2ViewportAssetResolver.GetBlipRate();
            chatBlankBlipEnabled = AO2ViewportAssetResolver.GetBlankBlipEnabled();
            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve(
                ChatPreview.ChatToken,
                !string.IsNullOrWhiteSpace(ChatPreview.PreviewShowname),
                preferViewportTheme: true);
            chatMarkupStart = style.ChatMarkupStart;
            chatMarkupEnd = style.ChatMarkupEnd;
            chatMarkupRemove = style.ChatMarkupRemove;
            chatBlipState = AO2ViewportBlipPlaybackRules.CreateState(
                chatFullText,
                chatTextCrawlMilliseconds,
                chatBlipRate,
                chatBlankBlipEnabled,
                chatMarkupStart,
                chatMarkupEnd,
                chatMarkupRemove);
            ChatPreview.PreviewText = chatPrefixText;

            if (chatFullText.Length == 0)
            {
                CompleteChatTextReveal();
                return;
            }

            chatTextTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            chatTextTimer.Interval = TimeSpan.Zero;
            chatTextTimer.Tick += OnChatTextTimerTick;
            chatTextTimer.Start();
        }

        private void AdvanceChatTextReveal()
        {
            if (chatBlipState == null || chatBlipState.Position >= chatFullText.Length)
            {
                CompleteChatTextReveal();
                return;
            }

            int delay = GetNextDisplayedTextElement(
                out string textElement,
                out bool shouldPlayBlip,
                out bool triggerScreenShake,
                out int blipGateDelay);
            if (triggerScreenShake)
            {
                StartScreenShake(messageSequence);
            }

            if (!string.IsNullOrEmpty(textElement))
            {
                ChatPreview.PreviewText = chatPrefixText + chatFullText.Substring(0, chatBlipState.Position);

                if (shouldPlayBlip
                    && IsVisible
                    && AO2ViewportBlipPlaybackRules.ShouldPlayBlipForTextElement(chatBlipState, textElement, blipGateDelay))
                {
                    audioManager.PlayBlip();
                }
            }

            if (chatBlipState.Position >= chatFullText.Length)
            {
                CompleteChatTextReveal();
                return;
            }

            if (chatTextTimer == null)
            {
                return;
            }

            chatTextTimer.Stop();
            chatTextTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, delay));
            chatTextTimer.Tick -= OnChatTextTimerTick;
            chatTextTimer.Tick += OnChatTextTimerTick;
            chatTextTimer.Start();
        }

        private void OnChatTextTimerTick(object? sender, EventArgs e)
        {
            AdvanceChatTextReveal();
        }

        private void CompleteChatTextReveal()
        {
            StopChatTextTimer();
            ShowChatArrow(currentChatArrowMiscToken);
            additivePreviousText = chatPrefixText + chatFullText;
            chatPrefixText = string.Empty;
            currentChatAdditive = false;
            chatBlipState = null;
            RenderPostMessageCharacterAnimation(currentChatCharacter, currentChatEmote, currentChatSequence);
            currentChatCharacter = null;
            currentChatEmote = string.Empty;
            currentChatSequence = 0;
            currentChatArrowMiscToken = string.Empty;
        }

        private void RenderPostMessageCharacterAnimation(CharacterFolder? character, string emoteName, int sequence)
        {
            if (character == null || sequence != messageSequence || string.IsNullOrWhiteSpace(emoteName))
            {
                return;
            }

            if (immediatePreAnimActive)
            {
                return;
            }

            string? postPath = AO2ViewportAssetResolver.ResolveCharacterPostAnimation(character, emoteName);
            if (!string.IsNullOrWhiteSpace(postPath))
            {
                SetCharacterAnimatedImageAsync(
                    CharacterImage,
                    postPath,
                    true,
                    loop: false,
                    onPlayerReady: postPlayer =>
                    {
                        if (postPlayer != null)
                        {
                            postPlayer.PlaybackFinished += () =>
                            {
                                if (sequence == messageSequence)
                                {
                                    RenderIdleCharacterAnimation(character, emoteName);
                                }
                            };
                            return;
                        }

                        if (sequence == messageSequence)
                        {
                            RenderIdleCharacterAnimation(character, emoteName);
                        }
                    });
                return;
            }

            RenderIdleCharacterAnimation(character, emoteName);
        }

        private void RenderIdleCharacterAnimation(CharacterFolder character, string emoteName)
        {
            string? idlePath = AO2ViewportAssetResolver.ResolveCharacterDialogAnimation(character, emoteName, talking: false);
            SetCharacterAnimatedImageAsync(CharacterImage, idlePath, !string.IsNullOrWhiteSpace(idlePath));
        }

        private int GetNextDisplayedTextElement(
            out string textElement,
            out bool shouldPlayBlip,
            out bool triggerScreenShake,
            out int blipGateDelay)
        {
            if (chatBlipState == null)
            {
                textElement = string.Empty;
                shouldPlayBlip = false;
                triggerScreenShake = false;
                blipGateDelay = 0;
                return 0;
            }

            int delay = AO2ViewportBlipPlaybackRules.GetNextDisplayedTextElement(
                chatBlipState,
                out textElement,
                out shouldPlayBlip,
                out triggerScreenShake,
                out bool triggerFlash,
                out blipGateDelay);
            if (triggerFlash)
            {
                DoFlash();
            }

            return delay;
        }

        internal static string ResolveViewportBlipToken(CharacterFolder? character, ICMessage? message)
        {
            if (!string.IsNullOrWhiteSpace(message?.Blips))
            {
                string packetBlipToken = message.Blips.Trim();
                if (!string.Equals(packetBlipToken, "0", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(AO2ViewportAudioResolver.ResolveBlipPath(packetBlipToken)))
                {
                    return packetBlipToken;
                }
            }

            string characterToken = AO2ViewportAssetResolver.ResolveCharacterBlipToken(character, message?.Emote);
            if (!string.IsNullOrWhiteSpace(characterToken))
            {
                return characterToken;
            }

            return string.Empty;
        }

        private static Color? ResolveViewportMessageColor(ICMessage? message)
        {
            if (message == null)
            {
                return null;
            }

            System.Drawing.Color packetColor = ICMessage.GetColorFromTextColor(message.TextColor);
            return Color.FromArgb(packetColor.A, packetColor.R, packetColor.G, packetColor.B);
        }

        private void ScheduleMessageSfxPlayback(ICMessage message, int sequence, bool shakeAtSfxTime)
        {
            if (!IsVisible)
            {
                return;
            }

            string? sfxPath = AO2ViewportAudioResolver.ResolveSfxPath(message.SfxName);
            bool needsShake = message.ScreenShake;
            if (string.IsNullOrWhiteSpace(sfxPath) && !needsShake)
            {
                return;
            }

            TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(0, message.SfxDelay) * 40);
            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = delay
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (sequence != messageSequence || !IsVisible)
                {
                    return;
                }

                if (needsShake)
                {
                    StartScreenShake(sequence);
                }

                if (!string.IsNullOrWhiteSpace(sfxPath))
                {
                    audioManager.PlaySfx(message.SfxName, message.Character, message.ShowName);
                }
            };
            timer.Start();
        }

        private void PlayEffectSfx(ICMessage message, CharacterFolder? character)
        {
            string token = AO2ViewportAssetResolver.ResolveEffectSoundToken(message.EffectString, message.Effect, character);
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            audioManager.PlayEffectSfx(token, message.Character, message.ShowName);
        }

        private void PlayLegacyRealization(ICMessage message, CharacterFolder? character)
        {
            // Legacy realization checkbox only — new effects field handles both visual and SFX via
            // RenderEffect + PlayEffectSfx, so DoFlash here would be redundant and cause stuck white.
            if (!message.Realization || message.Effect != ICMessage.Effects.None)
            {
                return;
            }

            DoFlash();

            string token = string.IsNullOrWhiteSpace(character?.configINI?.Realization)
                ? "sfx-realization"
                : character.configINI.Realization;
            audioManager.PlayEffectSfx(token, message.Character, message.ShowName);
        }

        private void DoFlash()
        {
            if (!IsVisible)
            {
                return;
            }

            FlashOverlay.BeginAnimation(OpacityProperty, null);
            FlashOverlay.Visibility = Visibility.Visible;
            FlashOverlay.Opacity = 1;
            DoubleAnimation fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                FillBehavior = FillBehavior.Stop
            };
            fade.Completed += (_, _) =>
            {
                FlashOverlay.Opacity = 0;
                FlashOverlay.Visibility = Visibility.Collapsed;
            };
            FlashOverlay.BeginAnimation(OpacityProperty, fade);
        }

        private void StopAnimation(Image image)
        {
            placedImageStates.Remove(image);
            if (!animationPlayers.TryGetValue(image, out IAnimationPlayer? player))
            {
                return;
            }

            player.Stop();
            animationPlayers.Remove(image);
        }

        private void StopAllAnimations()
        {
            foreach (IAnimationPlayer player in animationPlayers.Values.ToArray())
            {
                player.Stop();
            }

            animationPlayers.Clear();
            placedImageStates.Clear();
        }

        private void ScheduleContinuation(TimeSpan interval, Action continuation)
        {
            StopPendingMessageTimer();
            pendingMessageTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = interval
            };
            pendingMessageTimer.Tick += (_, _) =>
            {
                StopPendingMessageTimer();
                continuation();
            };
            pendingMessageTimer.Start();
        }

        private void StopPendingMessageTimer()
        {
            if (pendingMessageTimer == null)
            {
                return;
            }

            pendingMessageTimer.Stop();
            pendingMessageTimer = null;
        }

        private void StopChatTextTimer()
        {
            chatBlipState = null;
            if (chatTextTimer == null)
            {
                return;
            }

            chatTextTimer.Stop();
            chatTextTimer.Tick -= OnChatTextTimerTick;
            chatTextTimer = null;
        }

        private Action<int>? BuildFrameEffectHandler(
            ICMessage message,
            CharacterFolder? character,
            string? resolvedAnimationToken,
            int sequence)
        {
            string token = (resolvedAnimationToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            Dictionary<int, List<string>> shakeFrames = ResolveFrameEffects(
                message.FramesShake,
                character,
                token,
                "_FrameScreenshake");
            Dictionary<int, List<string>> realizationFrames = ResolveFrameEffects(
                message.FramesRealization,
                character,
                token,
                "_FrameRealization");
            Dictionary<int, List<string>> sfxFrames = ResolveFrameEffects(
                message.FramesSfx,
                character,
                token,
                "_FrameSFX");
            if (shakeFrames.Count == 0 && realizationFrames.Count == 0 && sfxFrames.Count == 0)
            {
                return null;
            }

            HashSet<string> processedEffectKeys = new HashSet<string>(StringComparer.Ordinal);
            return frameIndex =>
            {
                if (sequence != messageSequence || !IsVisible)
                {
                    return;
                }

                FireFrameShakeEffects(frameIndex, shakeFrames, processedEffectKeys, sequence);
                FireFrameRealizationEffects(frameIndex, realizationFrames, processedEffectKeys);
                FireFrameSfxEffects(frameIndex, sfxFrames, processedEffectKeys, message);
            };
        }

        private void FireFrameShakeEffects(
            int frameIndex,
            IReadOnlyDictionary<int, List<string>> frameMap,
            ISet<string> processedEffectKeys,
            int sequence)
        {
            foreach (KeyValuePair<int, List<string>> entry in frameMap)
            {
                if (entry.Key != frameIndex)
                {
                    continue;
                }

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    string uniqueKey = "shake:"
                        + frameIndex.ToString(CultureInfo.InvariantCulture)
                        + ":"
                        + i.ToString(CultureInfo.InvariantCulture);
                    if (!processedEffectKeys.Add(uniqueKey))
                    {
                        continue;
                    }

                    StartScreenShake(sequence);
                }
            }
        }

        private void FireFrameRealizationEffects(
            int frameIndex,
            IReadOnlyDictionary<int, List<string>> frameMap,
            ISet<string> processedEffectKeys)
        {
            foreach (KeyValuePair<int, List<string>> entry in frameMap)
            {
                if (entry.Key != frameIndex)
                {
                    continue;
                }

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    string uniqueKey = "realization:"
                        + frameIndex.ToString(CultureInfo.InvariantCulture)
                        + ":"
                        + i.ToString(CultureInfo.InvariantCulture);
                    if (!processedEffectKeys.Add(uniqueKey))
                    {
                        continue;
                    }

                    DoFlash();
                }
            }
        }

        private void FireFrameSfxEffects(
            int frameIndex,
            IReadOnlyDictionary<int, List<string>> frameMap,
            ISet<string> processedEffectKeys,
            ICMessage message)
        {
            foreach (KeyValuePair<int, List<string>> entry in frameMap)
            {
                if (entry.Key != frameIndex)
                {
                    continue;
                }

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    string uniqueKey = "sfx:"
                        + frameIndex.ToString(CultureInfo.InvariantCulture)
                        + ":"
                        + i.ToString(CultureInfo.InvariantCulture);
                    if (!processedEffectKeys.Add(uniqueKey))
                    {
                        continue;
                    }

                    string token = entry.Value[i];
                    if (string.IsNullOrWhiteSpace(token) || string.Equals(token, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    audioManager.PlaySfx(token, message.Character, message.ShowName);
                }
            }
        }

        private static Dictionary<int, List<string>> ResolveFrameEffects(
            string? packetValue,
            CharacterFolder? character,
            string resolvedAnimationToken,
            string sectionSuffix)
        {
            Dictionary<int, List<string>> packetEffects = ParsePacketFrameEffects(packetValue, resolvedAnimationToken);
            if (packetEffects.Count > 0)
            {
                return packetEffects;
            }

            Dictionary<int, List<string>> result = new Dictionary<int, List<string>>();
            foreach (AO2ViewportAssetResolver.ViewportFrameEffect effect in
                     AO2ViewportAssetResolver.ResolveCharacterFrameEffects(character, resolvedAnimationToken, sectionSuffix))
            {
                if (!result.TryGetValue(effect.FrameNumber, out List<string>? values))
                {
                    values = new List<string>();
                    result[effect.FrameNumber] = values;
                }

                values.Add(effect.Value);
            }

            return result;
        }

        private static Dictionary<int, List<string>> ParsePacketFrameEffects(string? packetValue, string resolvedAnimationToken)
        {
            Dictionary<int, List<string>> result = new Dictionary<int, List<string>>();
            foreach (string rawGroup in (packetValue ?? string.Empty).Split('^', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = rawGroup.Split('|', StringSplitOptions.None);
                if (parts.Length == 0)
                {
                    continue;
                }

                string groupToken = parts[0].Trim();
                if (!string.Equals(groupToken, resolvedAnimationToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int i = 1; i < parts.Length; i++)
                {
                    string frameEntry = parts[i].Trim();
                    if (string.IsNullOrWhiteSpace(frameEntry))
                    {
                        continue;
                    }

                    string[] frameParts = frameEntry.Split('=', 2);
                    if (!int.TryParse(frameParts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int frameNumber))
                    {
                        continue;
                    }

                    string value = frameParts.Length > 1 ? frameParts[1].Trim() : "1";
                    if (!result.TryGetValue(frameNumber, out List<string>? values))
                    {
                        values = new List<string>();
                        result[frameNumber] = values;
                    }

                    values.Add(value);
                }
            }

            return result;
        }

        private void OnRtReceived(string content, int variant)
        {
            Dispatcher.Invoke(() => HandleRtPacket(content, variant));
        }

        private void HandleRtPacket(string content, int variant)
        {
            if (string.Equals(content, "testimony1", StringComparison.OrdinalIgnoreCase))
            {
                ShowTestimonyOverlay();
            }
            else if (string.Equals(content, "testimony2", StringComparison.OrdinalIgnoreCase))
            {
                testimonyVisible = false;
                TestimonyImage.Source = null;
                TestimonyImage.Visibility = Visibility.Collapsed;
                ShowWtceOverlay("crossexamination_bubble");
            }
            else if (string.Equals(content, "judgeruling", StringComparison.OrdinalIgnoreCase))
            {
                testimonyVisible = false;
                TestimonyImage.Source = null;
                TestimonyImage.Visibility = Visibility.Collapsed;
                string overlayName = variant == 0 ? "notguilty_bubble" : "guilty_bubble";
                ShowWtceOverlay(overlayName, TimeSpan.FromMilliseconds(3000));
            }
        }

        private void ShowTestimonyOverlay()
        {
            string background = sceneClient?.curBG ?? string.Empty;
            string? path = AO2ViewportAssetResolver.ResolveTestimonyOverlayImage(background);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            audioManager.PlayCourtSfx("testimony1");
            testimonyVisible = true;
            SetAnimatedImage(TestimonyImage, path, true, loop: true);
            TestimonyImage.Visibility = Visibility.Visible;
        }

        private void HideTestimonyOverlay()
        {
            testimonyVisible = false;
            StopAnimation(TestimonyImage);
            TestimonyImage.Source = null;
            TestimonyImage.Visibility = Visibility.Collapsed;
        }

        private void ShowWtceOverlay(string assetStem, TimeSpan? staticDuration = null)
        {
            string background = sceneClient?.curBG ?? string.Empty;
            string? path = AO2ViewportAssetResolver.ResolveWtceOverlayImage(assetStem, background);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string sfxKey = assetStem switch
            {
                "crossexamination_bubble" => "testimony2",
                "notguilty_bubble" => "notguilty",
                "guilty_bubble" => "guilty",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(sfxKey))
            {
                audioManager.PlayCourtSfx(sfxKey);
            }

            IAnimationPlayer? player = SetAnimatedImage(WtceImage, path, true, loop: false);
            WtceImage.Visibility = Visibility.Visible;
            if (player != null)
            {
                player.PlaybackFinished += () => WtceImage.Visibility = Visibility.Collapsed;
            }
            else
            {
                TimeSpan fallback = staticDuration ?? TimeSpan.FromMilliseconds(1500);
                ScheduleContinuation(fallback, () => WtceImage.Visibility = Visibility.Collapsed);
            }
        }

        private void HideWtceOverlay()
        {
            StopAnimation(WtceImage);
            WtceImage.Source = null;
            WtceImage.Visibility = Visibility.Collapsed;
        }

        private void ShowSticker(string? characterName, string? miscName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
            {
                StopAnimation(StickerImage);
                StickerImage.Visibility = Visibility.Collapsed;
                return;
            }

            string? path = AO2ViewportAssetResolver.ResolveStickerImage(characterName, miscName);
            if (string.IsNullOrWhiteSpace(path))
            {
                StopAnimation(StickerImage);
                StickerImage.Visibility = Visibility.Collapsed;
                return;
            }

            SetAnimatedImage(StickerImage, path, true, loop: true);
        }

        private void ShowEvidenceOverlay(int evidenceId, string position, ICMessage message)
        {
            string? imageFile = messageSourceClient?.GetEvidenceImagePath(evidenceId)
                ?? sceneClient?.GetEvidenceImagePath(evidenceId);
            string background = sceneClient?.curBG ?? string.Empty;
            bool leftSide = !string.Equals(position.Trim(), "def", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(position.Trim(), "hlp", StringComparison.OrdinalIgnoreCase);

            // Try animated appear overlay first
            string? appearPath = AO2ViewportAssetResolver.ResolveEvidencePresentationImage(leftSide, background);
            // Fall back to evidence icon
            string? iconPath = AO2ViewportAssetResolver.ResolveEvidenceIconImage(imageFile);
            string? displayPath = appearPath ?? iconPath;
            if (string.IsNullOrWhiteSpace(displayPath))
            {
                StopAnimation(EvidenceImage);
                EvidenceImage.Visibility = Visibility.Collapsed;
                return;
            }

            double evidenceX = leftSide ? 5 : AO2ViewportAssetResolver.ViewportWidth - 53;
            double evidenceY = AO2ViewportAssetResolver.ViewportHeight - 53;
            Canvas.SetLeft(EvidenceImage, evidenceX);
            Canvas.SetTop(EvidenceImage, evidenceY);

            IAnimationPlayer? player = SetAnimatedImage(EvidenceImage, displayPath, true, loop: false);
            EvidenceImage.Visibility = Visibility.Visible;
            if (player != null)
            {
                player.PlaybackFinished += () =>
                {
                    // After appear animation, show the icon if available
                    if (!string.IsNullOrWhiteSpace(iconPath) && iconPath != displayPath)
                    {
                        SetAnimatedImage(EvidenceImage, iconPath, true, loop: true);
                    }
                };
            }
        }

        private void ShowChatArrow(string? miscToken)
        {
            if (!IsVisible)
            {
                return;
            }

            string? path = AO2ViewportAssetResolver.ResolveChatArrowImage(miscToken);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ApplyChatArrowBounds();

            StopChatArrow();
            if (Ao2AnimationPreview.TryCreateAnimationPlayer(path, true, out IAnimationPlayer? player, usePreviewLimits: false)
                && player != null)
            {
                chatArrowPlayer = player;
                player.FrameChanged += frame => ChatArrowImage.Source = frame;
                ChatArrowImage.Source = player.CurrentFrame;
            }
            else
            {
                ImageSource? source = AO2ViewportAssetResolver.LoadImage(path, decodePixelWidth: 0);
                ChatArrowImage.Source = source;
            }

            ChatArrowImage.Visibility = Visibility.Visible;
        }

        private void ApplyChatArrowBounds()
        {
            AO2ChatPreviewBounds arrowBounds = currentThemeLayout?.ChatArrowBounds ?? ChatPreview.GetChatArrowBounds();
            Canvas.SetLeft(ChatArrowImage, arrowBounds.X);
            Canvas.SetTop(ChatArrowImage, arrowBounds.Y);
            ChatArrowImage.Width = arrowBounds.Width;
            ChatArrowImage.Height = arrowBounds.Height;
        }

        private void StopChatArrow()
        {
            if (chatArrowPlayer != null)
            {
                chatArrowPlayer.Stop();
                chatArrowPlayer = null;
            }

            ChatArrowImage.Source = null;
            ChatArrowImage.Visibility = Visibility.Collapsed;
        }

        private void AnimateBackgroundSlide(double bgOldLeft, double deskOldLeft)
        {
            double bgNewLeft = Canvas.GetLeft(BackgroundImage);
            double deskNewLeft = Canvas.GetLeft(DeskImage);

            if (Math.Abs(bgNewLeft - bgOldLeft) < 0.5 && Math.Abs(deskNewLeft - deskOldLeft) < 0.5)
            {
                return;
            }

            System.Windows.Media.Animation.CubicEase easing = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
            };
            TimeSpan duration = TimeSpan.FromMilliseconds(500);

            System.Windows.Media.Animation.DoubleAnimation bgAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = bgOldLeft,
                To = bgNewLeft,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            bgAnim.Completed += (_, _) =>
            {
                BackgroundImage.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(BackgroundImage, bgNewLeft);
            };
            BackgroundImage.BeginAnimation(Canvas.LeftProperty, bgAnim);

            System.Windows.Media.Animation.DoubleAnimation deskAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = deskOldLeft,
                To = deskNewLeft,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            deskAnim.Completed += (_, _) =>
            {
                DeskImage.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(DeskImage, deskNewLeft);
            };
            DeskImage.BeginAnimation(Canvas.LeftProperty, deskAnim);
        }

        private enum ViewportPhase
        {
            Shout,
            PreAnimation,
            Speaking
        }

        private sealed record PlacedImageState(string Path, DateTime LastWriteTimeUtc);
    }
}
