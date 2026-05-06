using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Resolves AO2 viewport assets using AO2-style lookup order without copying AO2 client code.
    /// </summary>
    public static class AO2ViewportAssetResolver
    {
        /// <summary>
        /// Native AO2 viewport width in pixels.
        /// </summary>
        public const int ViewportWidth = 256;

        /// <summary>
        /// Native AO2 viewport height in pixels.
        /// </summary>
        public const int ViewportHeight = 192;

        /// <summary>
        /// Native-width chatbox area shown below the viewport in Oceanya.
        /// </summary>
        public const int ChatboxHeight = 104;

        /// <summary>
        /// Total viewport tool content height: AO viewport plus below-viewport chatbox.
        /// </summary>
        public const int ViewportToolHeight = ViewportHeight + ChatboxHeight;

        private static readonly string[] ImageExtensions = { ".webp", ".apng", ".gif", ".png", ".jpg", ".jpeg" };
        private static readonly TimeSpan DefaultShoutDuration = TimeSpan.FromMilliseconds(900);
        private static readonly TimeSpan DefaultPreAnimationDuration = TimeSpan.FromMilliseconds(1000);

        private static readonly Dictionary<string, string> LegacyPositionImageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["def"] = "defenseempty",
            ["hld"] = "helperstand",
            ["jud"] = "judgestand",
            ["hlp"] = "prohelperstand",
            ["pro"] = "prosecutorempty",
            ["wit"] = "witnessempty",
            ["jur"] = "jurystand",
            ["sea"] = "seancestand",
        };

        private static readonly Dictionary<string, string> DeskImageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["def"] = "defensedesk",
            ["hld"] = "defensedesk",
            ["hlp"] = "prosecutiondesk",
            ["pro"] = "prosecutiondesk",
            ["jud"] = "judgedesk",
            ["wit"] = "stand",
        };

        /// <summary>
        /// AO2 effect layer placement from effects.ini.
        /// </summary>
        public enum EffectLayer
        {
            BehindCharacter,
            Character,
            Over,
            Chat
        }

        /// <summary>
        /// Resolved image placement for an AO2 viewport layer.
        /// </summary>
        public sealed record ViewportImagePlacement(string? ImagePath, double Left, double Top, double Width, double Height);

        /// <summary>
        /// Resolved effect metadata used by the viewport renderer.
        /// </summary>
        public sealed record ViewportEffect(
            string? ImagePath,
            EffectLayer Layer,
            bool Stretch,
            bool RespectFlip,
            bool RespectOffset);

        /// <summary>
        /// Pair character ordering relative to the main speaker.
        /// </summary>
        public enum PairOrdering
        {
            PairInFront,
            PairBehind
        }

        /// <summary>
        /// AO2 semantic emote modifier after applying legacy compatibility mappings.
        /// </summary>
        public enum NormalizedEmoteModifier
        {
            Idle,
            PreAnimation,
            Zoom,
            PreAnimationZoom
        }

        /// <summary>
        /// Resolves the current background image for a background/position pair.
        /// </summary>
        public static string? ResolveBackgroundImage(string? backgroundName, string? position)
        {
            return ResolveBackgroundPlacement(backgroundName, position).ImagePath;
        }

        /// <summary>
        /// Resolves the desk image for the current background/position pair.
        /// </summary>
        public static string? ResolveDeskImage(string? backgroundName, string? position)
        {
            return ResolveDeskPlacement(backgroundName, position).ImagePath;
        }

        /// <summary>
        /// Resolves the current background image and AO2 origin-based placement.
        /// </summary>
        public static ViewportImagePlacement ResolveBackgroundPlacement(string? backgroundName, string? position)
        {
            Background? background = ResolveBackground(backgroundName);
            if (background == null)
            {
                return DefaultPlacement(null);
            }

            BackgroundPositionResolution resolution = ResolveBackgroundPosition(background, position);
            string? imagePath = ResolveImageStem(background.PathToFile, resolution.BackgroundStem)
                ?? background.GetBGImage(NormalizePosition(position))
                ?? background.bgImages.FirstOrDefault(File.Exists);

            return BuildPlacement(imagePath, resolution.Origin);
        }

        /// <summary>
        /// Resolves the current desk image and AO2 origin-based placement.
        /// </summary>
        public static ViewportImagePlacement ResolveDeskPlacement(string? backgroundName, string? position)
        {
            Background? background = ResolveBackground(backgroundName);
            if (background == null)
            {
                return DefaultPlacement(null);
            }

            BackgroundPositionResolution resolution = ResolveBackgroundPosition(background, position);
            string? imagePath = ResolveImageStem(background.PathToFile, resolution.DeskStem);
            return BuildPlacement(imagePath, resolution.Origin);
        }

        /// <summary>
        /// Resolves an AO2 character idle/talking animation path for a character emote.
        /// </summary>
        public static string? ResolveCharacterAnimation(CharacterFolder? character, string? emoteName)
        {
            return ResolveCharacterDialogAnimation(character, emoteName, talking: false);
        }

        /// <summary>
        /// Resolves AO2's receive-time dialog animation path for a character emote.
        /// </summary>
        public static string? ResolveCharacterDialogAnimation(CharacterFolder? character, string? emoteName, bool talking)
        {
            if (character == null)
            {
                return null;
            }

            string characterDirectory = GetCharacterDirectory(character);
            if (string.IsNullOrWhiteSpace(characterDirectory))
            {
                return null;
            }

            Emote? emote = ResolveEmote(character, emoteName);
            string animationName = !string.IsNullOrWhiteSpace(emote?.Animation)
                ? emote.Animation
                : string.IsNullOrWhiteSpace(emoteName) ? "normal" : emoteName.Trim();
            string normalizedAnimationName = animationName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedAnimationName) || normalizedAnimationName == "-")
            {
                return null;
            }

            string[] orderedCandidates = talking
                ? new[]
                {
                    "(b)" + normalizedAnimationName,
                    "(b)/" + normalizedAnimationName,
                    normalizedAnimationName,
                    "(a)" + normalizedAnimationName,
                    "(a)/" + normalizedAnimationName,
                    "placeholder"
                }
                : new[]
                {
                    "(a)" + normalizedAnimationName,
                    "(a)/" + normalizedAnimationName,
                    normalizedAnimationName,
                    "placeholder"
                };

            foreach (string candidate in orderedCandidates)
            {
                string resolved = CharacterAssetPathResolver.ResolveCharacterAssetPath(characterDirectory, candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves an AO2 character preanimation path from the packet PRE_EMOTE token.
        /// </summary>
        public static string? ResolveCharacterPreAnimation(CharacterFolder? character, string? preAnimToken)
        {
            if (character == null)
            {
                return null;
            }

            string normalizedPreAnim = (preAnimToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPreAnim) || normalizedPreAnim == "-")
            {
                return null;
            }

            string characterDirectory = GetCharacterDirectory(character);
            if (string.IsNullOrWhiteSpace(characterDirectory))
            {
                return null;
            }

            string resolved = CharacterAssetPathResolver.ResolveCharacterAssetPath(characterDirectory, normalizedPreAnim);
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }

        /// <summary>
        /// Resolves the AO2 effect visual referenced by an IC packet effect field.
        /// </summary>
        public static string? ResolveEffectImage(string? effectString, ICMessage.Effects effect)
        {
            return ResolveEffect(effectString, effect, null, false).ImagePath;
        }

        /// <summary>
        /// Resolves the AO2 effect visual and its effects.ini metadata.
        /// </summary>
        public static ViewportEffect ResolveEffect(
            string? effectString,
            ICMessage.Effects effect,
            CharacterFolder? character,
            bool flip)
        {
            (string effectName, string effectFolder) = ParseEffectParts(effectString);
            if (string.IsNullOrWhiteSpace(effectFolder))
            {
                effectFolder = character?.configINI.EffectsFolder ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(effectName))
            {
                effectName = effect switch
                {
                    ICMessage.Effects.Realization => "realization",
                    ICMessage.Effects.Hearts => "hearts",
                    ICMessage.Effects.Reaction => "reaction",
                    ICMessage.Effects.Impact => "impact",
                    _ => string.Empty
                };
            }

            if (string.IsNullOrWhiteSpace(effectName))
            {
                return new ViewportEffect(null, EffectLayer.Chat, false, false, false);
            }

            string? effectPath = null;
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                foreach (string root in EnumerateEffectRoots(baseFolder, effectFolder))
                {
                    string? resolved = ResolveImageStem(root, effectName);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        effectPath = resolved;
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(effectPath))
                {
                    break;
                }
            }

            string fallbackResourceName = effectName.ToLowerInvariant() switch
            {
                "realization" => "realization",
                "hearts" => "hearts",
                "reaction" => "reaction",
                "impact" => "impact",
                _ => string.Empty
            };

            effectPath ??= string.IsNullOrWhiteSpace(fallbackResourceName)
                ? null
                : "pack://application:,,,/OceanyaClient;component/Resources/Buttons/MessageEffects/" + fallbackResourceName + ".png";

            Dictionary<string, string> properties = ResolveEffectProperties(effectName, effectFolder);
            EffectLayer layer = ParseEffectLayer(GetProperty(properties, "layer"));
            bool stretch = IsTrue(GetProperty(properties, "stretch"));
            bool respectFlip = IsTrue(GetProperty(properties, "respect_flip")) && flip;
            bool respectOffset = IsTrue(GetProperty(properties, "respect_offset"));

            return new ViewportEffect(effectPath, layer, stretch, respectFlip, respectOffset);
        }

        /// <summary>
        /// Determines whether AO2 would show the desk after the preanimation phase.
        /// </summary>
        public static bool ShouldShowDesk(ICMessage.DeskMods deskMod, string? position)
        {
            if (deskMod == ICMessage.DeskMods.Hidden
                || deskMod == ICMessage.DeskMods.ShownDuringPreanimHiddenAfter
                || deskMod == ICMessage.DeskMods.ShownDuringPreanimCenteredAfter)
            {
                return false;
            }

            if (deskMod == ICMessage.DeskMods.Shown
                || deskMod == ICMessage.DeskMods.HiddenDuringPreanimShownAfter
                || deskMod == ICMessage.DeskMods.HiddenDuringPreanimCenteredAfter)
            {
                return true;
            }

            string normalizedPosition = NormalizePosition(position);
            return normalizedPosition is "def" or "hld" or "pro" or "hlp" or "jud" or "wit";
        }

        /// <summary>
        /// Determines whether AO2 shows the desk during the preanimation phase.
        /// </summary>
        public static bool ShouldShowDeskDuringPreAnimation(ICMessage.DeskMods deskMod, string? position)
        {
            if (deskMod == ICMessage.DeskMods.Hidden
                || deskMod == ICMessage.DeskMods.HiddenDuringPreanimShownAfter
                || deskMod == ICMessage.DeskMods.HiddenDuringPreanimCenteredAfter)
            {
                return false;
            }

            if (deskMod == ICMessage.DeskMods.Shown
                || deskMod == ICMessage.DeskMods.ShownDuringPreanimHiddenAfter
                || deskMod == ICMessage.DeskMods.ShownDuringPreanimCenteredAfter)
            {
                return true;
            }

            return ShouldShowDesk(deskMod, position);
        }

        /// <summary>
        /// Determines whether AO2 treats an emote modifier as zoom/speedline mode.
        /// </summary>
        public static bool IsZoomEmote(ICMessage.EmoteModifiers emoteModifier)
        {
            NormalizedEmoteModifier normalized = NormalizeEmoteModifier(emoteModifier);
            return normalized == NormalizedEmoteModifier.Zoom
                || normalized == NormalizedEmoteModifier.PreAnimationZoom;
        }

        /// <summary>
        /// Normalizes AO2 emote modifiers using the same legacy compatibility rules as the client.
        /// </summary>
        public static NormalizedEmoteModifier NormalizeEmoteModifier(ICMessage.EmoteModifiers emoteModifier)
        {
            return emoteModifier switch
            {
                ICMessage.EmoteModifiers.PlayPreanimation => NormalizedEmoteModifier.PreAnimation,
                ICMessage.EmoteModifiers.PlayPreanimationAndObjection => NormalizedEmoteModifier.PreAnimation,
                ICMessage.EmoteModifiers.Unused4 => NormalizedEmoteModifier.PreAnimationZoom,
                ICMessage.EmoteModifiers.NoPreanimationAndZoom => NormalizedEmoteModifier.Zoom,
                ICMessage.EmoteModifiers.ObjectionAndZoomNoPreanim => NormalizedEmoteModifier.PreAnimationZoom,
                ICMessage.EmoteModifiers.NoPreanimation => NormalizedEmoteModifier.Idle,
                _ => NormalizedEmoteModifier.Idle
            };
        }

        /// <summary>
        /// Determines whether AO2 blocks chat text behind a preanimation phase.
        /// </summary>
        public static bool ShouldBlockForPreAnimation(ICMessage.EmoteModifiers emoteModifier)
        {
            NormalizedEmoteModifier normalized = NormalizeEmoteModifier(emoteModifier);
            return normalized == NormalizedEmoteModifier.PreAnimation
                || normalized == NormalizedEmoteModifier.PreAnimationZoom;
        }

        /// <summary>
        /// Determines whether AO2 starts a packet preanimation alongside the chat text.
        /// </summary>
        public static bool ShouldPlayImmediatePreAnimation(ICMessage.EmoteModifiers emoteModifier, bool immediate)
        {
            if (!immediate)
            {
                return false;
            }

            NormalizedEmoteModifier normalized = NormalizeEmoteModifier(emoteModifier);
            return normalized == NormalizedEmoteModifier.Idle
                || normalized == NormalizedEmoteModifier.Zoom;
        }

        /// <summary>
        /// Determines whether AO2 should run a packet preanimation for an emote modifier.
        /// </summary>
        public static bool ShouldPlayPreAnimation(ICMessage.EmoteModifiers emoteModifier)
        {
            return ShouldBlockForPreAnimation(emoteModifier);
        }

        /// <summary>
        /// Determines whether AO2 disables talking sprites for this text color.
        /// </summary>
        public static bool IsTextColorTalking(ICMessage.TextColors textColor)
        {
            return textColor != ICMessage.TextColors.Blue;
        }

        /// <summary>
        /// Determines whether AO2 centers the speaker and hides pair/desk during preanimation.
        /// </summary>
        public static bool ShouldCenterAndHidePairDuringPreAnimation(ICMessage.DeskMods deskMod)
        {
            return deskMod == ICMessage.DeskMods.HiddenDuringPreanimCenteredAfter;
        }

        /// <summary>
        /// Determines whether AO2 centers the speaker and hides pair/desk during speaking.
        /// </summary>
        public static bool ShouldCenterAndHidePairDuringSpeaking(ICMessage.DeskMods deskMod, ICMessage.EmoteModifiers emoteModifier)
        {
            return deskMod == ICMessage.DeskMods.ShownDuringPreanimCenteredAfter
                || IsZoomEmote(emoteModifier);
        }

        /// <summary>
        /// Resolves the native AO2 speedline animation name for zoom messages.
        /// </summary>
        public static string ResolveSpeedlinesName(string? position)
        {
            string normalized = NormalizePosition(position);
            return normalized.StartsWith("pro", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "hlp", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("wit", StringComparison.OrdinalIgnoreCase)
                    ? "prosecution_speedlines"
                    : "defense_speedlines";
        }

        /// <summary>
        /// Resolves a speedline image if present in known AO2 asset roots.
        /// </summary>
        public static string? ResolveSpeedlinesImage(string? position, CharacterFolder? character)
        {
            string name = ResolveSpeedlinesName(position);
            foreach (string root in EnumerateMiscRoots(character?.configINI.EffectsFolder))
            {
                string? resolved = ResolveImageStem(root, name);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the AO2 shout overlay filename for a packet shout modifier.
        /// </summary>
        public static string? ResolveShoutOverlayImage(ICMessage.ShoutModifiers shoutModifier)
        {
            string resourceName = shoutModifier switch
            {
                ICMessage.ShoutModifiers.HoldIt => "holdit_bubble.gif",
                ICMessage.ShoutModifiers.Objection => "objection_bubble.gif",
                ICMessage.ShoutModifiers.TakeThat => "takethat_bubble.gif",
                _ => string.Empty
            };

            return string.IsNullOrWhiteSpace(resourceName)
                ? null
                : "pack://application:,,,/OceanyaClient;component/Resources/ShoutDefaults/" + resourceName;
        }

        /// <summary>
        /// Returns the approximate duration used before continuing after a static shout overlay.
        /// </summary>
        public static TimeSpan GetShoutDuration() => DefaultShoutDuration;

        /// <summary>
        /// Returns the approximate preanimation duration from char.ini, or a fallback for static preview rendering.
        /// </summary>
        public static TimeSpan GetPreAnimationDuration(CharacterFolder? character, string? preAnimToken)
        {
            string? preAnimationPath = ResolveCharacterPreAnimation(character, preAnimToken);
            if (Ao2AnimationPreview.TryEstimateAnimationDuration(preAnimationPath, out TimeSpan animationDuration)
                && animationDuration > TimeSpan.Zero)
            {
                return animationDuration;
            }

            int milliseconds = ReadCharacterPreAnimationMilliseconds(character, "Time", preAnimToken);
            return milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : DefaultPreAnimationDuration;
        }

        /// <summary>
        /// Returns true when AO2 char.ini defines an explicit [Time] value for a packet preanimation.
        /// </summary>
        public static bool TryGetExplicitPreAnimationDuration(
            CharacterFolder? character,
            string? preAnimToken,
            out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            int milliseconds = ReadCharacterPreAnimationMilliseconds(character, "Time", preAnimToken);
            if (milliseconds <= 0)
            {
                return false;
            }

            duration = TimeSpan.FromMilliseconds(milliseconds);
            return true;
        }

        /// <summary>
        /// Returns AO2's best static-preview wait before chat starts after a preanimation.
        /// </summary>
        public static TimeSpan GetPreAnimationWaitDuration(CharacterFolder? character, string? preAnimToken)
        {
            int milliseconds = ReadCharacterPreAnimationMilliseconds(character, "stay_time", preAnimToken);
            return milliseconds >= 0
                ? TimeSpan.FromMilliseconds(milliseconds * 40)
                : GetPreAnimationDuration(character, preAnimToken);
        }

        /// <summary>
        /// Determines whether the pair character should render in front of or behind the main character.
        /// </summary>
        public static PairOrdering GetPairOrdering(string? otherCharIdRaw)
        {
            string[] parts = (otherCharIdRaw ?? string.Empty).Split('^');
            if (parts.Length > 1
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int order)
                && order == 1)
            {
                return PairOrdering.PairInFront;
            }

            return PairOrdering.PairBehind;
        }

        /// <summary>
        /// Resolves a character folder by AO2 packet character name.
        /// </summary>
        public static CharacterFolder? ResolveCharacter(string? characterName)
        {
            string normalized = (characterName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.Name, normalized, StringComparison.OrdinalIgnoreCase))
                ?? CharacterFolder.FullList.FirstOrDefault(character =>
                    string.Equals(character.configINI.ShowName, normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resolves the AO2 custom chatbox token for a character.
        /// </summary>
        public static string ResolveCharacterChatToken(CharacterFolder? character)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.configINI.PathToConfigINI))
            {
                return "default";
            }

            string token = ReadIniSectionValue(character.configINI.PathToConfigINI, "Options", "chat");
            return string.IsNullOrWhiteSpace(token) ? "default" : token.Trim();
        }

        /// <summary>
        /// Resolves an emote by packet value, display id, or current character emote name.
        /// </summary>
        public static Emote? ResolveEmote(CharacterFolder? character, string? emoteName)
        {
            if (character == null)
            {
                return null;
            }

            string normalized = (emoteName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return character.configINI.Emotions.Values.FirstOrDefault();
            }

            return character.configINI.Emotions.Values.FirstOrDefault(emote =>
                string.Equals(emote.Name, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(emote.DisplayID, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(emote.ID.ToString(), normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Loads a bitmap or first animation frame into a WPF image source.
        /// </summary>
        public static ImageSource? LoadImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                return new System.Windows.Media.Imaging.BitmapImage(new Uri(path, UriKind.Absolute));
            }

            if (!File.Exists(path))
            {
                return null;
            }

            return Ao2AnimationPreview.LoadStaticPreviewImage(path, decodePixelWidth: 0, fallback: null);
        }

        private static Background? ResolveBackground(string? backgroundName)
        {
            string normalized = (backgroundName ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : Background.FromBGPath(normalized);
        }

        private static string NormalizePosition(string? position)
        {
            string normalized = (position ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "wit";
            }

            int separator = normalized.IndexOf(':');
            return separator > 0 ? normalized[..separator] : normalized;
        }

        private static string GetCharacterDirectory(CharacterFolder character)
        {
            return character.DirectoryPath
                ?? Path.GetDirectoryName(character.configINI.PathToConfigINI)
                ?? string.Empty;
        }

        private static BackgroundPositionResolution ResolveBackgroundPosition(Background background, string? position)
        {
            string normalizedPosition = NormalizePosition(position);
            string[] splitPosition = normalizedPosition.Split(':');
            string realPosition = splitPosition.Length > 0 ? splitPosition[0] : normalizedPosition;

            int? origin = TryReadInt(ReadDesignValue(background.PathToFile, normalizedPosition + "/origin"));
            if (!origin.HasValue
                && ResolveImageStem(background.PathToFile, "court") != null
                && !string.IsNullOrWhiteSpace(ReadDesignValue(background.PathToFile, "court:" + realPosition + "/origin")))
            {
                normalizedPosition = "court:" + realPosition;
                splitPosition = normalizedPosition.Split(':');
                origin = TryReadInt(ReadDesignValue(background.PathToFile, normalizedPosition + "/origin"));
            }

            string backgroundStem = ResolveImageStem(background.PathToFile, "witnessempty") != null ? "witnessempty" : "wit";
            string deskStem = ResolveImageStem(background.PathToFile, "witnessempty") != null ? "stand" : "wit_overlay";

            if (string.Equals(realPosition, "def", StringComparison.OrdinalIgnoreCase)
                && ResolveImageStem(background.PathToFile, "defenseempty") != null)
            {
                backgroundStem = "defenseempty";
                deskStem = "defensedesk";
            }
            else if (string.Equals(realPosition, "pro", StringComparison.OrdinalIgnoreCase)
                && ResolveImageStem(background.PathToFile, "prosecutorempty") != null)
            {
                backgroundStem = "prosecutorempty";
                deskStem = "prosecutiondesk";
            }
            else if (string.Equals(realPosition, "jud", StringComparison.OrdinalIgnoreCase)
                && ResolveImageStem(background.PathToFile, "judgestand") != null)
            {
                backgroundStem = "judgestand";
                deskStem = "judgedesk";
            }
            else if (string.Equals(realPosition, "sea", StringComparison.OrdinalIgnoreCase)
                && ResolveImageStem(background.PathToFile, "seancestand") != null)
            {
                backgroundStem = "seancestand";
                deskStem = "seancedesk";
            }
            else if (LegacyPositionImageNames.TryGetValue(realPosition, out string? legacyName)
                && ResolveImageStem(background.PathToFile, legacyName) != null)
            {
                backgroundStem = legacyName;
                if (DeskImageNames.TryGetValue(realPosition, out string? mappedDesk))
                {
                    deskStem = mappedDesk;
                }
            }

            string uniqueStem = splitPosition[0];
            if (ResolveImageStem(background.PathToFile, uniqueStem) != null)
            {
                backgroundStem = uniqueStem;
                deskStem = uniqueStem + "_overlay";
            }

            string overlayOverride = ReadDesignValue(background.PathToFile, "overlays/" + backgroundStem);
            if (!string.IsNullOrWhiteSpace(overlayOverride))
            {
                deskStem = overlayOverride;
            }

            return new BackgroundPositionResolution(backgroundStem, deskStem, origin);
        }

        private static ViewportImagePlacement BuildPlacement(string? imagePath, int? origin)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return DefaultPlacement(null);
            }

            if (!origin.HasValue)
            {
                return DefaultPlacement(imagePath);
            }

            (int width, int height) = TryGetImageSize(imagePath);
            if (width <= 0 || height <= 0)
            {
                return DefaultPlacement(imagePath);
            }

            double scale = (double)ViewportHeight / height;
            double scaledWidth = width * scale;
            double scaledHeight = height * scale;
            double left = -(origin.Value * scale - (ViewportWidth / 2.0));
            return new ViewportImagePlacement(imagePath, left, 0, scaledWidth, scaledHeight);
        }

        private static ViewportImagePlacement DefaultPlacement(string? imagePath)
        {
            return new ViewportImagePlacement(imagePath, 0, 0, ViewportWidth, ViewportHeight);
        }

        private static (int Width, int Height) TryGetImageSize(string imagePath)
        {
            try
            {
                ImageSource? source = LoadImage(imagePath);
                if (source is System.Windows.Media.Imaging.BitmapSource bitmap)
                {
                    return (bitmap.PixelWidth, bitmap.PixelHeight);
                }
            }
            catch
            {
                // Static preview is best effort; fall back to native placement.
            }

            return (0, 0);
        }

        private static string ReadDesignValue(string backgroundDirectory, string identifier)
        {
            return ReadIniValue(Path.Combine(backgroundDirectory, "design.ini"), identifier);
        }

        private static string ReadIniValue(string path, string identifier)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(identifier) || !File.Exists(path))
            {
                return string.Empty;
            }

            string currentSection = string.Empty;
            string targetSection = string.Empty;
            string targetKey = identifier;
            int slashIndex = identifier.LastIndexOf('/');
            if (slashIndex >= 0)
            {
                targetSection = identifier[..slashIndex].Trim();
                targetKey = identifier[(slashIndex + 1)..].Trim();
            }

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = (rawLine ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, targetSection, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line[..separator].Trim();
                if (string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    return line[(separator + 1)..].Trim();
                }
            }

            return string.Empty;
        }

        private static string ReadIniSectionValue(string path, string section, string key)
        {
            if (string.IsNullOrWhiteSpace(path)
                || string.IsNullOrWhiteSpace(section)
                || string.IsNullOrWhiteSpace(key)
                || !File.Exists(path))
            {
                return string.Empty;
            }

            string currentSection = string.Empty;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = (rawLine ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (string.Equals(line[..separator].Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return line[(separator + 1)..].Trim();
                }
            }

            return string.Empty;
        }

        private static int ReadCharacterPreAnimationMilliseconds(
            CharacterFolder? character,
            string section,
            string? preAnimToken)
        {
            if (character == null || string.IsNullOrWhiteSpace(preAnimToken))
            {
                return -1;
            }

            string iniPath = character.configINI.PathToConfigINI;
            string value = ReadIniSectionValue(iniPath, section, preAnimToken.Trim());
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : -1;
        }

        private static int? TryReadInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;
        }

        private static string? ResolveImageStem(string directory, string stem)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem))
            {
                return null;
            }

            foreach (string extension in ImageExtensions)
            {
                string candidate = Path.Combine(directory, stem + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateEffectRoots(string baseFolder, string effectFolder)
        {
            yield return Path.Combine(baseFolder, "misc", "default", "effects");

            if (!string.IsNullOrWhiteSpace(effectFolder))
            {
                yield return Path.Combine(baseFolder, "misc", effectFolder, "effects");
            }

            yield return Path.Combine(baseFolder, "themes", "default", "effects");
            yield return Path.Combine(baseFolder, "themes", "CC", "effects");
        }

        private static IEnumerable<string> EnumerateMiscRoots(string? effectFolder)
        {
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                yield return Path.Combine(baseFolder, "misc", "default");
                if (!string.IsNullOrWhiteSpace(effectFolder))
                {
                    yield return Path.Combine(baseFolder, "misc", effectFolder);
                }

                yield return Path.Combine(baseFolder, "themes", "default", "misc");
                yield return Path.Combine(baseFolder, "themes", "CC", "misc");
            }
        }

        private static Dictionary<string, string> ResolveEffectProperties(string effectName, string effectFolder)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string iniPath in EnumerateEffectIniPaths(effectFolder))
            {
                foreach (Dictionary<string, string> group in ReadIniGroups(iniPath))
                {
                    if (group.TryGetValue("name", out string? name)
                        && string.Equals(name, effectName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (KeyValuePair<string, string> pair in group)
                        {
                            if (!result.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                            {
                                result[pair.Key] = pair.Value;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static IEnumerable<string> EnumerateEffectIniPaths(string effectFolder)
        {
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                yield return Path.Combine(baseFolder, "themes", "default", "effects", "effects.ini");
                yield return Path.Combine(baseFolder, "themes", "CC", "effects", "effects.ini");
                if (!string.IsNullOrWhiteSpace(effectFolder))
                {
                    yield return Path.Combine(baseFolder, "misc", effectFolder, "effects.ini");
                    yield return Path.Combine(baseFolder, "misc", effectFolder, "effects", "effects.ini");
                }

                yield return Path.Combine(baseFolder, "misc", "default", "effects.ini");
                yield return Path.Combine(baseFolder, "misc", "default", "effects", "effects.ini");
            }
        }

        private static IEnumerable<Dictionary<string, string>> ReadIniGroups(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                yield break;
            }

            Dictionary<string, string>? currentGroup = null;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = (rawLine ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    if (currentGroup != null)
                    {
                        yield return currentGroup;
                    }

                    currentGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                if (currentGroup == null)
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                currentGroup[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            if (currentGroup != null)
            {
                yield return currentGroup;
            }
        }

        private static string GetProperty(Dictionary<string, string> properties, string key)
        {
            return properties.TryGetValue(key, out string? value) ? value : string.Empty;
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static EffectLayer ParseEffectLayer(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "behind" => EffectLayer.BehindCharacter,
                "character" => EffectLayer.Character,
                "over" => EffectLayer.Over,
                _ => EffectLayer.Chat
            };
        }

        private static (string EffectName, string EffectFolder) ParseEffectParts(string? effectString)
        {
            string[] parts = (effectString ?? string.Empty).Split('|');
            string name = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            string folder = parts.Length > 2 ? parts[1].Trim() : string.Empty;
            return (name, folder);
        }

        private sealed record BackgroundPositionResolution(string BackgroundStem, string DeskStem, int? Origin);
    }
}
