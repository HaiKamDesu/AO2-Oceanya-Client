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
        private static readonly TimeSpan DefaultShoutDuration = TimeSpan.FromMilliseconds(724);
        private static readonly TimeSpan DefaultPreAnimationDuration = TimeSpan.FromMilliseconds(1000);
        private static readonly Dictionary<string, CachedImageSize> ImageSizeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ParsedDesignIni> DesignIniCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (BackgroundPositionResolution Resolution, DateTime DesignIniWriteTimeUtc)> PositionResolutionCache = new(StringComparer.OrdinalIgnoreCase);

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
            ["hld"] = "helperdesk",
            ["hlp"] = "prohelperdesk",
            ["pro"] = "prosecutiondesk",
            ["jud"] = "judgedesk",
            ["wit"] = "stand",
            ["jur"] = "jurydesk",
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
            bool RespectOffset,
            bool Loop = false);

        /// <summary>
        /// Resolved animation metadata for a viewport character layer.
        /// </summary>
        public sealed record ResolvedCharacterAnimation(string? AssetPath, string ResolvedToken);

        /// <summary>
        /// Resolved AO2 frame effect metadata for a specific animation token.
        /// </summary>
        public sealed record ViewportFrameEffect(int FrameNumber, string Value);

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
        /// Resolves the AO2 [Overlays] key for the current background/position pair.
        /// </summary>
        public static string ResolveBackgroundOverlayKey(string? backgroundName, string? position)
        {
            Background? background = ResolveBackground(backgroundName);
            if (background == null)
            {
                return NormalizePosition(position);
            }

            return ResolveBackgroundPosition(background, position).BackgroundStem;
        }

        /// <summary>
        /// Resolves scaling and stretch display options from the background's design.ini.
        /// </summary>
        public static ViewportDisplayOptions ResolveDisplayOptions(string? backgroundName)
        {
            Background? background = ResolveBackground(backgroundName);
            if (background == null)
            {
                return ViewportDisplayOptions.Default;
            }

            string scalingRaw = ReadDesignValue(background.PathToFile, "scaling").Trim();
            string stretchRaw = ReadDesignValue(background.PathToFile, "stretch").Trim();

            // AO2 parity: "smooth"/"auto" → Qt::SmoothTransformation (bilinear) = WPF Linear.
            // "pixel"/"fast" → Qt::FastTransformation (nearest neighbor).
            // HighQuality/Fant is softer than bilinear and does not match AO2 output.
            BitmapScalingMode scalingMode = scalingRaw.ToLowerInvariant() switch
            {
                "pixel" => BitmapScalingMode.NearestNeighbor,
                "fast" => BitmapScalingMode.NearestNeighbor,
                "smooth" => BitmapScalingMode.Linear,
                _ => BitmapScalingMode.Linear
            };

            bool stretchEnabled = !string.Equals(stretchRaw, "false", StringComparison.OrdinalIgnoreCase)
                && stretchRaw != "0";
            Stretch stretchMode = stretchEnabled ? Stretch.Fill : Stretch.None;

            return new ViewportDisplayOptions(scalingMode, stretchMode);
        }

        /// <summary>
        /// Resolves the current background image and AO2 origin-based placement.
        /// </summary>
        public static ViewportImagePlacement ResolveBackgroundPlacement(string? backgroundName, string? position)
        {
            Background? background = ResolveBackground(backgroundName);
            if (background == null)
            {
                CustomConsole.Warning(
                    $"ResolveBackground(\"{backgroundName}\") returned null. BaseFolders={string.Join(";", Globals.BaseFolders ?? new System.Collections.Generic.List<string>())}",
                    category: CustomConsole.LogCategory.Viewport);
                return DefaultPlacement(null);
            }

            BackgroundPositionResolution resolution = ResolveBackgroundPosition(background, position);
            string? imagePath = ResolveImageStem(background.PathToFile, resolution.BackgroundStem)
                ?? background.GetBGImage(NormalizePosition(position))
                ?? background.bgImages.FirstOrDefault(File.Exists);

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                CustomConsole.Warning(
                    $"No image for bg=\"{backgroundName}\" pos=\"{position}\" stem=\"{resolution.BackgroundStem}\" pathToFile=\"{background.PathToFile}\"",
                    category: CustomConsole.LogCategory.Viewport);
                // AO2 parity: when the named background exists but has no image for this position,
                // fall back to background/default/ — same as AO2's missing-position behavior.
                Background? defaultBg = Background.FromBGPath("default");
                if (defaultBg != null
                    && !string.Equals(defaultBg.PathToFile, background.PathToFile, StringComparison.OrdinalIgnoreCase))
                {
                    BackgroundPositionResolution defaultResolution = ResolveBackgroundPosition(defaultBg, position);
                    string? defaultImagePath = ResolveImageStem(defaultBg.PathToFile, defaultResolution.BackgroundStem)
                        ?? defaultBg.GetBGImage(NormalizePosition(position))
                        ?? defaultBg.bgImages.FirstOrDefault(File.Exists);
                    CustomConsole.Info(
                        $"Default bg fallback: stem=\"{defaultResolution.BackgroundStem}\" imagePath=\"{defaultImagePath}\"",
                        CustomConsole.LogCategory.Viewport);
                    return BuildPlacement(defaultImagePath, defaultResolution.Origin);
                }
            }

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
            string? deskImagePath = ResolveImageStem(background.PathToFile, resolution.DeskStem);

            // AO2 parity: in set_scene(), ui_vp_desk is resized and moved to exactly the same
            // rect as ui_vp_background (scaled_frame_size and scaled_pos are computed from the
            // background image's frame size, then applied to both widgets). The desk image is
            // then stretched to fill that rect — it never drives its own placement dimensions.
            string? bgImagePath = ResolveImageStem(background.PathToFile, resolution.BackgroundStem)
                ?? background.GetBGImage(NormalizePosition(position))
                ?? background.bgImages.FirstOrDefault(File.Exists);
            ViewportImagePlacement bgPlacement = BuildPlacement(bgImagePath, resolution.Origin);

            return new ViewportImagePlacement(deskImagePath, bgPlacement.Left, bgPlacement.Top, bgPlacement.Width, bgPlacement.Height);
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
            return ResolveCharacterDialogAnimationDetails(character, emoteName, talking).AssetPath;
        }

        /// <summary>
        /// Resolves AO2's optional post-message <c>(c)</c> animation for a character emote.
        /// </summary>
        public static string? ResolveCharacterPostAnimation(CharacterFolder? character, string? emoteName)
        {
            return ResolveCharacterPostAnimationDetails(character, emoteName).AssetPath;
        }

        /// <summary>
        /// Resolves AO2's optional post-message <c>(c)</c> animation and the exact animation token it matched.
        /// </summary>
        public static ResolvedCharacterAnimation ResolveCharacterPostAnimationDetails(
            CharacterFolder? character,
            string? emoteName)
        {
            if (!TryResolveDialogAnimationName(character, emoteName, out string characterDirectory, out string animationName))
            {
                return new ResolvedCharacterAnimation(null, string.Empty);
            }

            foreach (string candidate in new[] { "(c)" + animationName, "(c)/" + animationName })
            {
                string resolved = CharacterAssetPathResolver.ResolveCharacterAssetPath(characterDirectory, candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return new ResolvedCharacterAnimation(resolved, candidate);
                }
            }

            return new ResolvedCharacterAnimation(null, string.Empty);
        }

        /// <summary>
        /// Resolves AO2's receive-time dialog animation path and the exact animation token it matched.
        /// </summary>
        public static ResolvedCharacterAnimation ResolveCharacterDialogAnimationDetails(
            CharacterFolder? character,
            string? emoteName,
            bool talking)
        {
            if (!TryResolveDialogAnimationName(character, emoteName, out string characterDirectory, out string normalizedAnimationName))
            {
                // Character not found locally — skip emote resolution and go straight to placeholder.
                foreach (string baseFolder in Globals.BaseFolders ?? Enumerable.Empty<string>())
                {
                    foreach (string ext in ImageExtensions)
                    {
                        string themePlaceholder = Path.Combine(baseFolder, "themes", "default", "placeholder" + ext);
                        if (File.Exists(themePlaceholder))
                        {
                            return new ResolvedCharacterAnimation(themePlaceholder, "placeholder");
                        }
                    }
                }
                return new ResolvedCharacterAnimation(null, string.Empty);
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
                    return new ResolvedCharacterAnimation(resolved, candidate);
                }
            }

            // AO2 parity: if no placeholder found in the character folder, fall back to
            // themes/default/placeholder — same second-tier fallback AO2 uses.
            foreach (string baseFolder in Globals.BaseFolders ?? Enumerable.Empty<string>())
            {
                foreach (string ext in ImageExtensions)
                {
                    string themePlaceholder = Path.Combine(baseFolder, "themes", "default", "placeholder" + ext);
                    if (File.Exists(themePlaceholder))
                    {
                        return new ResolvedCharacterAnimation(themePlaceholder, "placeholder");
                    }
                }
            }

            return new ResolvedCharacterAnimation(null, string.Empty);
        }

        private static bool TryResolveDialogAnimationName(
            CharacterFolder? character,
            string? emoteName,
            out string characterDirectory,
            out string animationName)
        {
            characterDirectory = string.Empty;
            animationName = string.Empty;
            if (character == null)
            {
                return false;
            }

            characterDirectory = GetCharacterDirectory(character);
            if (string.IsNullOrWhiteSpace(characterDirectory))
            {
                return false;
            }

            Emote? emote = ResolveEmote(character, emoteName);
            animationName = !string.IsNullOrWhiteSpace(emote?.Animation)
                ? emote.Animation.Trim()
                : string.IsNullOrWhiteSpace(emoteName) ? "normal" : emoteName.Trim();
            return !string.IsNullOrWhiteSpace(animationName) && animationName != "-";
        }

        /// <summary>
        /// Resolves an AO2 character preanimation path from the packet PRE_EMOTE token.
        /// </summary>
        public static string? ResolveCharacterPreAnimation(CharacterFolder? character, string? preAnimToken)
        {
            return ResolveCharacterPreAnimationDetails(character, preAnimToken).AssetPath;
        }

        /// <summary>
        /// Resolves an AO2 character preanimation path and the exact token it matched.
        /// </summary>
        public static ResolvedCharacterAnimation ResolveCharacterPreAnimationDetails(
            CharacterFolder? character,
            string? preAnimToken)
        {
            if (character == null)
            {
                return new ResolvedCharacterAnimation(null, string.Empty);
            }

            string normalizedPreAnim = (preAnimToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPreAnim) || normalizedPreAnim == "-")
            {
                return new ResolvedCharacterAnimation(null, string.Empty);
            }

            string characterDirectory = GetCharacterDirectory(character);
            if (string.IsNullOrWhiteSpace(characterDirectory))
            {
                return new ResolvedCharacterAnimation(null, string.Empty);
            }

            string resolved = CharacterAssetPathResolver.ResolveCharacterAssetPath(characterDirectory, normalizedPreAnim);
            return string.IsNullOrWhiteSpace(resolved)
                ? new ResolvedCharacterAnimation(null, string.Empty)
                : new ResolvedCharacterAnimation(resolved, normalizedPreAnim);
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
            (string effectName, string effectFolder, _) = ParseEffectParts(effectString);
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

            Dictionary<string, string> properties = ResolveEffectProperties(effectName, effectFolder);
            EffectLayer layer = ParseEffectLayer(GetProperty(properties, "layer"));
            bool stretch = IsTrue(GetProperty(properties, "stretch"));
            bool respectFlip = IsTrue(GetProperty(properties, "respect_flip")) && flip;
            bool respectOffset = IsTrue(GetProperty(properties, "respect_offset"));
            bool loop = IsTrue(GetProperty(properties, "loop"));

            return new ViewportEffect(effectPath, layer, stretch, respectFlip, respectOffset, loop);
        }

        /// <summary>
        /// Resolves the AO2 effect sound token referenced by an IC packet effect field.
        /// </summary>
        public static string ResolveEffectSoundToken(
            string? effectString,
            ICMessage.Effects effect,
            CharacterFolder? character)
        {
            (_, _, string effectSound) = ParseEffectParts(effectString);
            if (!string.IsNullOrWhiteSpace(effectSound))
            {
                return effectSound;
            }

            return effect switch
            {
                ICMessage.Effects.Realization => string.IsNullOrWhiteSpace(character?.configINI?.Realization)
                    ? "sfx-realization"
                    : character.configINI.Realization,
                ICMessage.Effects.Hearts => "sfx-squee",
                ICMessage.Effects.Reaction => "sfx-reactionding",
                ICMessage.Effects.Impact => "sfx-fan",
                _ => string.Empty
            };
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

            if (deskMod == ICMessage.DeskMods.Chat)
            {
                // AO2 legacy "chat" behavior: position-dependent.
                // Hide desk only for jud, hld, hlp; show for all other positions.
                string normalizedPos = NormalizePosition(position);
                return normalizedPos is not ("jud" or "hld" or "hlp");
            }

            if (deskMod == ICMessage.DeskMods.Shown
                || deskMod == ICMessage.DeskMods.HiddenDuringPreanimShownAfter
                || deskMod == ICMessage.DeskMods.HiddenDuringPreanimCenteredAfter)
            {
                return true;
            }

            // AO2 parity: get_pos_path() always produces a desk image stem for every position —
            // unknown positions default to the witness stand desk. Show desk and let
            // ResolveDeskPlacement decide whether the image actually exists.
            return true;
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

            if (deskMod == ICMessage.DeskMods.Chat)
            {
                // AO2 legacy "chat" behavior: position-dependent (same as speaking phase).
                string normalizedPos = NormalizePosition(position);
                return normalizedPos is not ("jud" or "hld" or "hlp");
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
        /// Per AO2: only IDLE/ZOOM emote modifiers respect the immediate flag; PREANIM always blocks.
        /// </summary>
        public static bool ShouldPlayImmediatePreAnimation(ICMessage.EmoteModifiers emoteModifier, bool immediate)
        {
            if (!immediate)
            {
                return false;
            }

            NormalizedEmoteModifier normalized = NormalizeEmoteModifier(emoteModifier);
            return normalized == NormalizedEmoteModifier.Idle || normalized == NormalizedEmoteModifier.Zoom;
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
            return ResolveShoutOverlayImage(shoutModifier, null, null);
        }

        /// <summary>
        /// Resolves the AO2 shout overlay image from configured AO asset roots only.
        /// </summary>
        public static string? ResolveShoutOverlayImage(
            ICMessage.ShoutModifiers shoutModifier,
            string? characterName,
            string? miscName)
        {
            string stem = shoutModifier switch
            {
                ICMessage.ShoutModifiers.HoldIt => "holdit_bubble",
                ICMessage.ShoutModifiers.Objection => "objection_bubble",
                ICMessage.ShoutModifiers.TakeThat => "takethat_bubble",
                ICMessage.ShoutModifiers.Custom => "custom",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(stem))
            {
                return null;
            }

            string character = characterName?.Trim() ?? string.Empty;
            string misc = miscName?.Trim() ?? string.Empty;
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                foreach (string root in EnumerateAo2ImageAssetRoots(baseFolder, character, misc))
                {
                    string? resolved = ResolveImageStem(root, stem);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }
            }

            return null;
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
        /// Returns the configured AO2 chat crawl speed from <c>config.ini</c>.
        /// </summary>
        public static int GetTextCrawlMilliseconds()
        {
            return ReadConfigIniInt("text_crawl", 40, minimum: 0);
        }

        /// <summary>
        /// Returns the configured AO2 blip rate from <c>config.ini</c>.
        /// </summary>
        public static int GetBlipRate()
        {
            return ReadConfigIniInt("blip_rate", 2);
        }

        /// <summary>
        /// Returns whether AO2 blank-blips are enabled in <c>config.ini</c>.
        /// </summary>
        public static bool GetBlankBlipEnabled()
        {
            return ReadConfigIniBool("blank_blip", defaultValue: false);
        }

        /// <summary>
        /// Returns whether AO2 viewport screenshake is enabled in <c>config.ini</c>.
        /// </summary>
        public static bool GetScreenShakeEnabled()
        {
            return ReadConfigIniBool("shake", defaultValue: true);
        }

        /// <summary>
        /// Resolves frame effects for a specific matched animation token by reading the local character <c>char.ini</c>.
        /// </summary>
        public static IReadOnlyList<ViewportFrameEffect> ResolveCharacterFrameEffects(
            CharacterFolder? character,
            string? animationToken,
            string sectionSuffix)
        {
            if (character == null)
            {
                return Array.Empty<ViewportFrameEffect>();
            }

            string resolvedToken = (animationToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedToken))
            {
                return Array.Empty<ViewportFrameEffect>();
            }

            string iniPath = character.configINI.PathToConfigINI;
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
            {
                return Array.Empty<ViewportFrameEffect>();
            }

            string sectionName = resolvedToken + sectionSuffix;
            List<ViewportFrameEffect> result = new List<ViewportFrameEffect>();
            foreach (KeyValuePair<string, string> entry in ReadIniSectionEntries(iniPath, sectionName))
            {
                if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frameNumber))
                {
                    continue;
                }

                result.Add(new ViewportFrameEffect(frameNumber, entry.Value?.Trim() ?? string.Empty));
            }

            return result
                .OrderBy(effect => effect.FrameNumber)
                .ToArray();
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
        /// Resolves the AO2 blip token for a character/emote pair using default and per-emote overrides.
        /// </summary>
        public static string ResolveCharacterBlipToken(CharacterFolder? character, string? emoteName)
        {
            if (character == null)
            {
                return string.Empty;
            }

            Emote? emote = ResolveEmote(character, emoteName);
            if (emote != null)
            {
                string overrideToken = ReadCharacterBlipOverride(character.configINI.PathToConfigINI, emote.ID);
                if (!string.IsNullOrWhiteSpace(overrideToken))
                {
                    return overrideToken.Trim();
                }
            }

            string defaultToken = character.configINI.Blips?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(defaultToken))
            {
                return defaultToken;
            }

            string genderToken = character.configINI.Gender?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(genderToken) ? "male" : genderToken;
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

            if (!File.Exists(path))
            {
                return null;
            }

            return Ao2AnimationPreview.LoadStaticPreviewImage(path, decodePixelWidth: 0, fallback: null);
        }

        private static Background? ResolveBackground(string? backgroundName)
        {
            string normalized = (backgroundName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                Background? defaultResult = Background.FromBGPath("default");
                if (defaultResult == null)
                {
                    CustomConsole.Warning(
                        $"Background.FromBGPath(\"default\") returned null. BaseFolders={string.Join(";", Globals.BaseFolders ?? new System.Collections.Generic.List<string>())}",
                        category: CustomConsole.LogCategory.Viewport);
                }
                return defaultResult;
            }

            Background? background = Background.FromBGPath(normalized);
            if (background != null)
            {
                return background;
            }

            // AO2 parity: when the named background directory is not found, fall back to
            // background/default/ — resolved from the user's AO2 installation mount paths.
            if (!string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase))
            {
                Background? fallback = Background.FromBGPath("default");
                CustomConsole.Info(
                    $"BG \"{normalized}\" not found locally, falling back to default. fallback={fallback?.PathToFile ?? "null"}",
                    CustomConsole.LogCategory.Viewport);
                return fallback;
            }

            return null;
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
            string designIniPath = Path.Combine(background.PathToFile, "design.ini");
            DateTime designIniTime = File.Exists(designIniPath) ? File.GetLastWriteTimeUtc(designIniPath) : DateTime.MinValue;
            string cacheKey = background.PathToFile + "\0" + normalizedPosition;
            if (PositionResolutionCache.TryGetValue(cacheKey, out (BackgroundPositionResolution Resolution, DateTime DesignIniWriteTimeUtc) cachedEntry)
                && cachedEntry.DesignIniWriteTimeUtc == designIniTime)
            {
                return cachedEntry.Resolution;
            }

            string[] splitPosition = normalizedPosition.Split(':');
            string realPosition = splitPosition.Length > 0 ? splitPosition[0] : normalizedPosition;

            if (ResolveImageStem(background.PathToFile, "court") != null
                && !string.IsNullOrWhiteSpace(ReadDesignValue(background.PathToFile, "court:" + realPosition + "/origin")))
            {
                normalizedPosition = "court:" + realPosition;
                splitPosition = normalizedPosition.Split(':');
            }
            int? origin = TryReadInt(ReadDesignValue(background.PathToFile, normalizedPosition + "/origin"));

            bool hasWitnessEmpty = ResolveImageStem(background.PathToFile, "witnessempty") != null;
            string backgroundStem = hasWitnessEmpty ? "witnessempty" : "wit";
            string deskStem = hasWitnessEmpty ? "stand" : "wit_overlay";

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

            var resolution = new BackgroundPositionResolution(backgroundStem, deskStem, origin);
            PositionResolutionCache[cacheKey] = (resolution, designIniTime);
            return resolution;
        }

        private static ViewportImagePlacement BuildPlacement(string? imagePath, int? origin)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return DefaultPlacement(null);
            }

            (int width, int height) = TryGetImageSize(imagePath);
            if (width <= 0 || height <= 0)
            {
                return DefaultPlacement(imagePath);
            }

            double scale = (double)ViewportHeight / height;

            // AO2 parity: QSize::operator*(qreal) applies qRound to each dimension.
            // qRound(x) = floor(x + 0.5) — round half toward +infinity.
            int scaledWidth = (int)Math.Floor(width * scale + 0.5);
            int scaledHeight = (int)Math.Floor(height * scale + 0.5);

            if (!origin.HasValue)
            {
                // AO2 parity: no origin → background widget stays at viewport size (256×192),
                // image is scaled to height 192 (aspect-ratio preserved) and centered via
                // Qt::AlignCenter on the QLabel. The widget clips any overhang left/right.
                // Integer division matches Qt's alignment arithmetic.
                int left = (ViewportWidth - scaledWidth) / 2;
                return new ViewportImagePlacement(imagePath, left, 0, scaledWidth, scaledHeight);
            }

            // AO2 parity: QPoint(double, 0) — C++ double→int conversion truncates toward zero.
            int leftWithOrigin = (int)(-(origin.Value * scale - (ViewportWidth / 2)));
            return new ViewportImagePlacement(imagePath, leftWithOrigin, 0, scaledWidth, scaledHeight);
        }

        private static ViewportImagePlacement DefaultPlacement(string? imagePath)
        {
            return new ViewportImagePlacement(imagePath, 0, 0, ViewportWidth, ViewportHeight);
        }

        private static (int Width, int Height) TryGetImageSize(string imagePath)
        {
            try
            {
                DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(imagePath);
                if (ImageSizeCache.TryGetValue(imagePath, out CachedImageSize? cached)
                    && cached != null
                    && cached.LastWriteTimeUtc == lastWriteTimeUtc)
                {
                    return (cached.Width, cached.Height);
                }

                ImageSource? source = LoadImage(imagePath);
                if (source is System.Windows.Media.Imaging.BitmapSource bitmap)
                {
                    ImageSizeCache[imagePath] = new CachedImageSize(
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        lastWriteTimeUtc);
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

        private static ParsedDesignIni GetOrParseDesignIni(string path)
        {
            DateTime lastWrite = File.GetLastWriteTimeUtc(path);
            if (DesignIniCache.TryGetValue(path, out ParsedDesignIni? cached) && cached.LastWriteTimeUtc == lastWrite)
            {
                return cached;
            }

            var sectionDicts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var sectionLists = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = string.Empty;
            sectionDicts[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            sectionLists[currentSection] = new List<KeyValuePair<string, string>>();

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
                    if (!sectionDicts.ContainsKey(currentSection))
                    {
                        sectionDicts[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        sectionLists[currentSection] = new List<KeyValuePair<string, string>>();
                    }

                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                sectionDicts[currentSection][k] = v;
                sectionLists[currentSection].Add(new KeyValuePair<string, string>(k, v));
            }

            var result = new ParsedDesignIni(lastWrite, sectionDicts, sectionLists);
            DesignIniCache[path] = result;
            return result;
        }

        private static string ReadIniValue(string path, string identifier)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(identifier) || !File.Exists(path))
            {
                return string.Empty;
            }

            string targetSection = string.Empty;
            string targetKey = identifier;
            int slashIndex = identifier.LastIndexOf('/');
            if (slashIndex >= 0)
            {
                targetSection = identifier[..slashIndex].Trim();
                targetKey = identifier[(slashIndex + 1)..].Trim();
            }

            ParsedDesignIni parsed = GetOrParseDesignIni(path);
            return parsed.SectionDicts.TryGetValue(targetSection, out Dictionary<string, string>? section)
                && section.TryGetValue(targetKey, out string? value)
                ? value
                : string.Empty;
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

            ParsedDesignIni parsed = GetOrParseDesignIni(path);
            return parsed.SectionDicts.TryGetValue(section, out Dictionary<string, string>? sectionDict)
                && sectionDict.TryGetValue(key.Trim(), out string? value)
                ? value
                : string.Empty;
        }

        private static IReadOnlyList<KeyValuePair<string, string>> ReadIniSectionEntries(string path, string section)
        {
            if (string.IsNullOrWhiteSpace(path)
                || string.IsNullOrWhiteSpace(section)
                || !File.Exists(path))
            {
                return Array.Empty<KeyValuePair<string, string>>();
            }

            ParsedDesignIni parsed = GetOrParseDesignIni(path);
            return parsed.SectionLists.TryGetValue(section, out List<KeyValuePair<string, string>>? list)
                ? list
                : Array.Empty<KeyValuePair<string, string>>();
        }

        private static int ReadConfigIniInt(string key, int defaultValue, int? minimum = null)
        {
            string value = ReadRawConfigIniValue(key);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return minimum.HasValue ? Math.Max(minimum.Value, parsed) : parsed;
            }

            return defaultValue;
        }

        private static bool ReadConfigIniBool(string key, bool defaultValue)
        {
            string value = ReadRawConfigIniValue(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (bool.TryParse(value, out bool parsedBool))
            {
                return parsedBool;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
            {
                return parsedInt != 0;
            }

            return defaultValue;
        }

        private static string ReadRawConfigIniValue(string key)
        {
            string configPath = Globals.PathToConfigINI;
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return string.Empty;
            }

            try
            {
                foreach (string rawLine in File.ReadLines(configPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0
                        || line.StartsWith(";", StringComparison.Ordinal)
                        || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        continue;
                    }

                    string currentKey = line[..equalsIndex].Trim();
                    if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return line[(equalsIndex + 1)..].Trim();
                }
            }
            catch
            {
                // Fall back to the AO2 defaults if the config cannot be read.
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

        private static string ReadCharacterBlipOverride(string iniPath, int emoteId)
        {
            if (string.IsNullOrWhiteSpace(iniPath) || emoteId <= 0 || !File.Exists(iniPath))
            {
                return string.Empty;
            }

            string optionsProfile = ReadIniSectionValue(
                iniPath,
                "OptionsN",
                emoteId.ToString(CultureInfo.InvariantCulture));
            if (!int.TryParse(optionsProfile, NumberStyles.Integer, CultureInfo.InvariantCulture, out int profileId)
                || profileId <= 0)
            {
                return string.Empty;
            }

            return ReadIniSectionValue(
                iniPath,
                "Options" + profileId.ToString(CultureInfo.InvariantCulture),
                "blips");
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

            string normalizedStem = stem.Trim();

            // AO2 parity: AO2 treats design.ini values as VPaths (relative), so absolute paths
            // (e.g. from mis-authored design.ini files) are never resolved. Match that behavior.
            if (Path.IsPathRooted(normalizedStem))
            {
                return null;
            }

            string directCandidate = Path.Combine(directory, normalizedStem);
            if (Path.HasExtension(normalizedStem) && File.Exists(directCandidate))
            {
                return directCandidate;
            }

            foreach (string extension in ImageExtensions)
            {
                string candidate = Path.Combine(directory, normalizedStem + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateEffectRoots(string baseFolder, string effectFolder)
        {
            // AO2 base: effects live directly under base/effects/ (standard AO2 layout)
            yield return Path.Combine(baseFolder, "effects");

            yield return Path.Combine(baseFolder, "misc", "default");
            yield return Path.Combine(baseFolder, "misc", "default", "effects");

            if (!string.IsNullOrWhiteSpace(effectFolder))
            {
                yield return Path.Combine(baseFolder, "misc", effectFolder);
                yield return Path.Combine(baseFolder, "misc", effectFolder, "effects");
            }

            yield return Path.Combine(baseFolder, "themes", "default", "effects");
            yield return Path.Combine(baseFolder, "themes", "CC", "effects");
        }

        private static IEnumerable<string> EnumerateAo2ImageAssetRoots(string baseFolder, string character, string misc)
        {
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(character))
            {
                yield return Path.Combine(baseFolder, "characters", character);
            }

            if (!string.IsNullOrWhiteSpace(misc))
            {
                yield return Path.Combine(baseFolder, "themes", "default", "misc", misc);
                yield return Path.Combine(baseFolder, "misc", misc);
            }

            yield return Path.Combine(baseFolder, "themes", "default");
            yield return baseFolder;
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

        private static (string EffectName, string EffectFolder, string EffectSound) ParseEffectParts(string? effectString)
        {
            string[] parts = (effectString ?? string.Empty).Split('|');
            string name = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            string folder = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            string sound = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            return (name, folder, sound);
        }

        private sealed record BackgroundPositionResolution(string BackgroundStem, string DeskStem, int? Origin);

        /// <summary>
        /// Display rendering options derived from a background's design.ini.
        /// </summary>
        public sealed record ViewportDisplayOptions(BitmapScalingMode ScalingMode, Stretch StretchMode)
        {
            /// <summary>Default options when no design.ini is present or the background is unknown.</summary>
            public static readonly ViewportDisplayOptions Default = new(BitmapScalingMode.Linear, Stretch.Fill);
        }

        private sealed record CachedImageSize(int Width, int Height, DateTime LastWriteTimeUtc);

        private sealed class ParsedDesignIni
        {
            public ParsedDesignIni(
                DateTime lastWriteTimeUtc,
                Dictionary<string, Dictionary<string, string>> sectionDicts,
                Dictionary<string, List<KeyValuePair<string, string>>> sectionLists)
            {
                LastWriteTimeUtc = lastWriteTimeUtc;
                SectionDicts = sectionDicts;
                SectionLists = sectionLists;
            }

            public DateTime LastWriteTimeUtc { get; }
            public Dictionary<string, Dictionary<string, string>> SectionDicts { get; }
            public Dictionary<string, List<KeyValuePair<string, string>>> SectionLists { get; }
        }
    }
}
