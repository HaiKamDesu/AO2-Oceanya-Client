using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// AO2-native 256x192 viewport surface for rendering background, characters, effects, desks, and chat.
    /// </summary>
    public partial class AO2ViewportControl : UserControl
    {
        private AOClient? sceneClient;
        private AOClient? messageSourceClient;
        private DispatcherTimer? pendingMessageTimer;
        private DispatcherTimer? chatTextTimer;
        private DispatcherTimer? screenShakeTimer;
        private int messageSequence;
        private int chatTextPosition;
        private int chatDisplaySpeed = DefaultChatDisplaySpeed;
        private int chatTextCrawlMilliseconds = DefaultChatTextCrawlMilliseconds;
        private int chatBlipTicker;
        private int chatBlipRate = DefaultBlipRate;
        private bool chatBlankBlipEnabled;
        private string chatFullText = string.Empty;
        private string currentChatBlipToken = string.Empty;
        private readonly Dictionary<Image, IAnimationPlayer> animationPlayers = new Dictionary<Image, IAnimationPlayer>();
        private readonly AO2ViewportAudioManager audioManager = new AO2ViewportAudioManager();
        private readonly Random screenShakeRandom = new Random();
        private readonly TranslateTransform viewportShakeTransform = new TranslateTransform();
        private static readonly double[] ChatDisplayMultipliers = { 0, 0.25, 0.65, 1, 1.25, 1.75, 2.25 };
        private const int DefaultChatTextCrawlMilliseconds = 40;
        private const int DefaultChatDisplaySpeed = 3;
        private const int DefaultBlipRate = 2;
        private const int ChatPauseMilliseconds = 100;
        private const int ScreenShakeDurationMilliseconds = 300;
        private const int ScreenShakeIntervalMilliseconds = 20;

        /// <summary>
        /// Initializes a new instance of the <see cref="AO2ViewportControl"/> class.
        /// </summary>
        public AO2ViewportControl()
        {
            InitializeComponent();
            ViewportCanvas.RenderTransform = viewportShakeTransform;
            audioManager.RefreshVolumes();
            IsVisibleChanged += OnIsVisibleChanged;
            Unloaded += (_, _) => audioManager.Dispose();
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
            if (ReferenceEquals(sceneClient, client) && ReferenceEquals(messageSourceClient, incomingMessageClient))
            {
                return;
            }

            DetachClientEvents();
            sceneClient = client;
            messageSourceClient = incomingMessageClient ?? client;
            AttachClientEvents();
            if (sceneClient == null && messageSourceClient == null)
            {
                ClearScene();
                return;
            }

            RenderBackgroundOnly();
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
            }
        }

        private void OnICMessageReceived(ICMessage message)
        {
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
            audioManager.StopAll();
        }

        private void OnIcActionReceived(string showName, string action, bool isSentFromSelf, ICMessage.TextColors textColor)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsVisible)
                {
                    return;
                }

                string normalized = (action ?? string.Empty).Trim();
                const string musicPrefix = "has played a song ";
                const string stopMusic = "has stopped the music.";

                if (normalized.StartsWith(musicPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    audioManager.PlayMusic(normalized.Substring(musicPrefix.Length));
                    return;
                }

                if (string.Equals(normalized, stopMusic, StringComparison.OrdinalIgnoreCase))
                {
                    audioManager.StopMusic();
                }
            });
        }

        private void OnBackgroundChanged(string background)
        {
            Dispatcher.Invoke(() =>
            {
                StopScreenShake();
                audioManager.StopAll();
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
            audioManager.StopAll();
            AO2ViewportAssetResolver.ViewportImagePlacement backgroundPlacement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement(sceneClient.curBG, sceneClient.curPos);
            SetPlacedImage(BackgroundImage, backgroundPlacement, true, Stretch.Fill);
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
            ChatPreview.PreviewText = string.Empty;
            ChatPreview.Visibility = Visibility.Collapsed;
            currentChatBlipToken = string.Empty;
        }

        private void RenderMessage(ICMessage message)
        {
            messageSequence++;
            StopPendingMessageTimer();
            StopScreenShake();
            CharacterFolder? character = AO2ViewportAssetResolver.ResolveCharacter(message.Character)
                ?? sceneClient?.currentINI
                ?? messageSourceClient?.currentINI;
            string background = messageSourceClient?.curBG ?? sceneClient?.curBG ?? string.Empty;
            string position = string.IsNullOrWhiteSpace(message.Side)
                ? messageSourceClient?.curPos ?? sceneClient?.curPos ?? string.Empty
                : message.Side;
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
                startTextReveal: immediate);

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

            if (animationPlayers.TryGetValue(CharacterImage, out IAnimationPlayer? preAnimationPlayer))
            {
                if (immediate)
                {
                    renderSpeaking();
                    return;
                }

                bool continued = false;
                preAnimationPlayer.PlaybackFinished += () =>
                {
                    if (continued)
                    {
                        return;
                    }

                    continued = true;
                    renderSpeaking();
                };
                return;
            }

            if (immediate)
            {
                renderSpeaking();
                return;
            }

            ScheduleContinuation(AO2ViewportAssetResolver.GetPreAnimationDuration(character, message.PreAnim), renderSpeaking);
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
            bool startTextReveal = true)
        {
            AO2ViewportAssetResolver.ViewportImagePlacement backgroundPlacement =
                AO2ViewportAssetResolver.ResolveBackgroundPlacement(backgroundName, position);
            SetPlacedImage(BackgroundImage, backgroundPlacement, true, Stretch.Fill);

            bool isPreAnimation = phase == ViewportPhase.PreAnimation;
            bool isZoom = message != null && AO2ViewportAssetResolver.IsZoomEmote(message.EmoteModifier);
            bool centerAndHidePair = message != null
                && (isPreAnimation
                    ? AO2ViewportAssetResolver.ShouldCenterAndHidePairDuringPreAnimation(message.DeskMod)
                    : AO2ViewportAssetResolver.ShouldCenterAndHidePairDuringSpeaking(message.DeskMod, message.EmoteModifier));
            bool useTalkingSprite = !isPreAnimation
                && showChat
                && !string.IsNullOrWhiteSpace(message?.Message)
                && AO2ViewportAssetResolver.IsTextColorTalking(message.TextColor);
            AO2ViewportAssetResolver.ResolvedCharacterAnimation resolvedCharacterAnimation = isPreAnimation
                ? AO2ViewportAssetResolver.ResolveCharacterPreAnimationDetails(character, message?.PreAnim)
                : AO2ViewportAssetResolver.ResolveCharacterDialogAnimationDetails(character, emoteName, useTalkingSprite);
            Action<int>? frameHandler = message == null
                ? null
                : BuildFrameEffectHandler(
                    message,
                    character,
                    resolvedCharacterAnimation.ResolvedToken,
                    messageSequence);
            SetAnimatedImage(
                CharacterImage,
                resolvedCharacterAnimation.AssetPath,
                !string.IsNullOrWhiteSpace(resolvedCharacterAnimation.AssetPath),
                loop: !isPreAnimation,
                onFrameChanged: frameHandler);
            CharacterImage.RenderTransformOrigin = new Point(0.5, 0.5);
            CharacterImage.RenderTransform = flip ? new ScaleTransform(-1, 1) : Transform.Identity;
            ApplyOffset(CharacterImage, centerAndHidePair ? (0, 0) : message?.SelfOffset ?? (0, 0));

            bool shouldShowDesk = isZoom && !isPreAnimation
                ? false
                : isPreAnimation
                    ? AO2ViewportAssetResolver.ShouldShowDeskDuringPreAnimation(deskMod, position)
                    : AO2ViewportAssetResolver.ShouldShowDesk(deskMod, position);
            AO2ViewportAssetResolver.ViewportImagePlacement deskPlacement = shouldShowDesk
                ? AO2ViewportAssetResolver.ResolveDeskPlacement(backgroundName, position)
                : new AO2ViewportAssetResolver.ViewportImagePlacement(null, 0, 0, AO2ViewportAssetResolver.ViewportWidth, AO2ViewportAssetResolver.ViewportHeight);
            SetPlacedImage(DeskImage, deskPlacement, shouldShowDesk, Stretch.Fill);

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

            ChatPreview.PreviewShowname = showname ?? string.Empty;
            bool shouldStartTextReveal = showChat
                && startTextReveal
                && (phase == ViewportPhase.Speaking || phase == ViewportPhase.PreAnimation);
            ChatPreview.PreviewText = shouldStartTextReveal
                ? string.Empty
                : messageText ?? string.Empty;
            ChatPreview.ChatToken = AO2ViewportAssetResolver.ResolveCharacterChatToken(character);
            ChatPreview.MessageColorOverride = ResolveViewportMessageColor(message);
            ChatPreview.ShowShowname = !string.IsNullOrWhiteSpace(showname);
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
                }

                if (ShouldApplyImmediateScreenShake(message, phase))
                {
                    StartScreenShake(messageSequence);
                }
            }

            if (showChat)
            {
                currentChatBlipToken = ResolveViewportBlipToken(character, message);
                audioManager.PrepareBlip(currentChatBlipToken);
                ChatPreview.RefreshPreview();
                if (shouldStartTextReveal)
                {
                    StartChatTextReveal(messageText ?? string.Empty);
                }
            }
            else
            {
                StopChatTextTimer();
                currentChatBlipToken = string.Empty;
            }
        }

        private void ShowShoutOverlay(ICMessage message)
        {
            RenderShoutOverlay(message, ViewportPhase.Shout);
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
            StopAllAnimations();
            audioManager.StopAll();
            ChatPreview.PreviewText = string.Empty;
            ChatPreview.Visibility = Visibility.Collapsed;
            currentChatBlipToken = string.Empty;
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
                double remainingFraction = 1.0 - (elapsedMs / ScreenShakeDurationMilliseconds);
                if (remainingFraction <= 0)
                {
                    StopScreenShake();
                    return;
                }

                ApplyScreenShakeOffset(remainingFraction);
            };
            screenShakeTimer.Start();
        }

        private void ApplyScreenShakeOffset(double remainingFraction = 1.0)
        {
            double maxDeviation = 7.0 * (AO2ViewportAssetResolver.ViewportHeight / 192.0) * Math.Clamp(remainingFraction, 0.0, 1.0);
            int deviation = Math.Max(1, (int)Math.Round(maxDeviation, MidpointRounding.AwayFromZero));
            viewportShakeTransform.X = screenShakeRandom.Next(-deviation, deviation + 1);
            viewportShakeTransform.Y = screenShakeRandom.Next(-deviation, deviation + 1);
        }

        private void StopScreenShake()
        {
            if (screenShakeTimer != null)
            {
                screenShakeTimer.Stop();
                screenShakeTimer = null;
            }

            viewportShakeTransform.X = 0;
            viewportShakeTransform.Y = 0;
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

            if (Ao2AnimationPreview.TryCreateAnimationPlayer(path, loop, out IAnimationPlayer? player, usePreviewLimits: false)
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

            ImageSource? source = AO2ViewportAssetResolver.LoadImage(path);
            image.Source = source;
            image.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
            return null;
        }

        private static void SetPlacedImage(
            Image image,
            AO2ViewportAssetResolver.ViewportImagePlacement placement,
            bool visible,
            Stretch stretch)
        {
            ImageSource? source = AO2ViewportAssetResolver.LoadImage(placement.ImagePath);
            image.Source = source;
            image.Width = placement.Width;
            image.Height = placement.Height;
            image.Stretch = stretch;
            Canvas.SetLeft(image, placement.Left);
            Canvas.SetTop(image, placement.Top);
            image.Visibility = visible && source != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ApplyOffset(Image image, (int Horizontal, int Vertical) offset)
        {
            Canvas.SetLeft(image, AO2ViewportAssetResolver.ViewportWidth * offset.Horizontal / 100.0);
            Canvas.SetTop(image, AO2ViewportAssetResolver.ViewportHeight * offset.Vertical / 100.0);
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
            SetAnimatedImage(PairCharacterImage, pairPath, !string.IsNullOrWhiteSpace(pairPath));
            PairCharacterImage.RenderTransformOrigin = new Point(0.5, 0.5);
            PairCharacterImage.RenderTransform = message.OtherFlip ? new ScaleTransform(-1, 1) : Transform.Identity;
            ApplyOffset(PairCharacterImage, (message.OtherOffset, message.OtherOffsetVertical));

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
            SetAnimatedImage(EffectImage, resolvedEffect.ImagePath, !string.IsNullOrWhiteSpace(resolvedEffect.ImagePath));
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

            string? shoutPath = AO2ViewportAssetResolver.ResolveShoutOverlayImage(message.ShoutModifier);
            SetAnimatedImage(ShoutOverlayImage, shoutPath, !string.IsNullOrWhiteSpace(shoutPath), loop: false);
            Panel.SetZIndex(ShoutOverlayImage, 9);
        }

        private void StartChatTextReveal(string text)
        {
            StopChatTextTimer();
            chatFullText = SkipAo2AlignmentPrefix(text ?? string.Empty);
            chatTextPosition = 0;
            chatDisplaySpeed = DefaultChatDisplaySpeed;
            chatTextCrawlMilliseconds = AO2ViewportAssetResolver.GetTextCrawlMilliseconds();
            chatBlipTicker = 0;
            chatBlipRate = AO2ViewportAssetResolver.GetBlipRate();
            chatBlankBlipEnabled = AO2ViewportAssetResolver.GetBlankBlipEnabled();
            ChatPreview.PreviewText = string.Empty;

            if (chatFullText.Length == 0)
            {
                return;
            }

            chatTextTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            chatTextTimer.Interval = TimeSpan.Zero;
            chatTextTimer.Tick += OnChatTextTimerTick;
            chatTextTimer.Start();
        }

        private void AdvanceChatTextReveal()
        {
            if (chatTextPosition >= chatFullText.Length)
            {
                StopChatTextTimer();
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
                ChatPreview.PreviewText += textElement;

                if (shouldPlayBlip && IsVisible && ShouldPlayBlipForTextElement(textElement, blipGateDelay))
                {
                    audioManager.PlayBlip();
                }
            }

            if (chatTextPosition >= chatFullText.Length)
            {
                StopChatTextTimer();
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

        private int GetNextDisplayedTextElement(
            out string textElement,
            out bool shouldPlayBlip,
            out bool triggerScreenShake,
            out int blipGateDelay)
        {
            shouldPlayBlip = false;
            triggerScreenShake = false;
            textElement = GetTextElement(chatFullText, chatTextPosition);
            chatTextPosition += textElement.Length;
            blipGateDelay = 0;

            if (textElement == "{")
            {
                chatDisplaySpeed = Math.Min(ChatDisplayMultipliers.Length - 1, chatDisplaySpeed + 1);
                textElement = string.Empty;
                return 0;
            }

            if (textElement == "}")
            {
                chatDisplaySpeed = Math.Max(0, chatDisplaySpeed - 1);
                textElement = string.Empty;
                return 0;
            }

            if (textElement == "\\" && chatTextPosition < chatFullText.Length)
            {
                string escapedElement = GetTextElement(chatFullText, chatTextPosition);
                chatTextPosition += escapedElement.Length;

                switch (escapedElement)
                {
                    case "n":
                        textElement = Environment.NewLine;
                        return 0;
                    case "p":
                        textElement = string.Empty;
                        return ChatPauseMilliseconds;
                    case "s":
                        textElement = string.Empty;
                        triggerScreenShake = true;
                        return 0;
                    case "f":
                        textElement = string.Empty;
                        return 0;
                    default:
                        textElement = escapedElement;
                        shouldPlayBlip = true;
                        blipGateDelay = GetChatTextDelay(textElement, includePunctuationDelay: false);
                        return GetChatTextDelay(textElement, includePunctuationDelay: true);
                }
            }

            shouldPlayBlip = true;
            blipGateDelay = GetChatTextDelay(textElement, includePunctuationDelay: false);
            return GetChatTextDelay(textElement, includePunctuationDelay: true);
        }

        private bool ShouldPlayBlipForTextElement(string textElement, int delay)
        {
            string firstElement = GetTextElement(textElement, 0);
            bool isWhitespace = string.IsNullOrWhiteSpace(firstElement) || char.IsWhiteSpace(firstElement[0]);
            int effectiveBlipRate = chatBlipRate;
            if (delay > 0 && delay <= 25)
            {
                effectiveBlipRate = Math.Max(
                    effectiveBlipRate,
                    (int)Math.Round(chatTextCrawlMilliseconds / (double)delay, MidpointRounding.AwayFromZero));
            }

            bool shouldPlay = (chatBlipRate <= 0 && chatBlipTicker < 1)
                || (effectiveBlipRate > 0 && chatBlipTicker % effectiveBlipRate == 0);
            if (shouldPlay)
            {
                if (!isWhitespace || chatBlankBlipEnabled)
                {
                    chatBlipTicker++;
                    return true;
                }
            }
            else
            {
                chatBlipTicker++;
            }

            return false;
        }

        private int GetChatTextDelay(string textElement, bool includePunctuationDelay)
        {
            double multiplier = ChatDisplayMultipliers[chatDisplaySpeed];
            int delay = (int)(chatTextCrawlMilliseconds * multiplier);
            if (includePunctuationDelay && chatDisplaySpeed > 1 && ".,?!:;".Contains(textElement, StringComparison.Ordinal))
            {
                int maxDelay = (int)(chatTextCrawlMilliseconds * ChatDisplayMultipliers[6] * 1.5);
                delay = Math.Min(maxDelay, delay * 3);
            }

            return delay;
        }

        private static string GetTextElement(string text, int index)
        {
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text, index);
            return enumerator.MoveNext()
                ? enumerator.GetTextElement()
                : text.Substring(index, 1);
        }

        private static string SkipAo2AlignmentPrefix(string text)
        {
            return text.StartsWith("~~", StringComparison.Ordinal)
                || text.StartsWith("~>", StringComparison.Ordinal)
                || text.StartsWith("<>", StringComparison.Ordinal)
                    ? text.Substring(2)
                    : text;
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
            if (string.IsNullOrWhiteSpace(sfxPath))
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

                if (shakeAtSfxTime && message.ScreenShake)
                {
                    StartScreenShake(sequence);
                }

                audioManager.PlaySfx(message.SfxName);
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

            audioManager.PlaySfx(token);
        }

        private void StopAnimation(Image image)
        {
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
            Dictionary<int, List<string>> sfxFrames = ResolveFrameEffects(
                message.FramesSfx,
                character,
                token,
                "_FrameSFX");
            if (shakeFrames.Count == 0 && sfxFrames.Count == 0)
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
                FireFrameSfxEffects(frameIndex, sfxFrames, processedEffectKeys);
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
                    string uniqueKey = "shake:" + frameIndex.ToString(CultureInfo.InvariantCulture) + ":" + i.ToString(CultureInfo.InvariantCulture);
                    if (!processedEffectKeys.Add(uniqueKey))
                    {
                        continue;
                    }

                    StartScreenShake(sequence);
                }
            }
        }

        private void FireFrameSfxEffects(
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
                    string uniqueKey = "sfx:" + frameIndex.ToString(CultureInfo.InvariantCulture) + ":" + i.ToString(CultureInfo.InvariantCulture);
                    if (!processedEffectKeys.Add(uniqueKey))
                    {
                        continue;
                    }

                    string token = entry.Value[i];
                    if (string.IsNullOrWhiteSpace(token) || string.Equals(token, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    audioManager.PlaySfx(token);
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

        private enum ViewportPhase
        {
            Shout,
            PreAnimation,
            Speaking
        }
    }
}
