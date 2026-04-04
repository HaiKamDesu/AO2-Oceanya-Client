using Common;
using System.Drawing;
using System.Globalization;

namespace AOBot_Testing.Structures
{
    public class ICMessage
    {
        public sealed class SerializationOptions
        {
            public bool IncludeCcccIcSupport { get; set; }

            public bool IncludeLoopingSfx { get; set; }

            public bool IncludeAdditive { get; set; }

            public bool IncludeEffects { get; set; }

            public bool IncludeCustomBlips { get; set; }

            public bool IncludeVerticalOffset { get; set; } = true;

            public bool IncludeSlide { get; set; } = true;
        }

        private const int MinimumFieldCount = 15;
        private const int DeskModIndex = 0;
        private const int PreAnimIndex = 1;
        private const int CharacterIndex = 2;
        private const int EmoteIndex = 3;
        private const int MessageIndex = 4;
        private const int SideIndex = 5;
        private const int SfxNameIndex = 6;
        private const int EmoteModifierIndex = 7;
        private const int CharIdIndex = 8;
        private const int SfxDelayIndex = 9;
        private const int ShoutModifierIndex = 10;
        private const int EvidenceIdIndex = 11;
        private const int FlipIndex = 12;
        private const int RealizationIndex = 13;
        private const int TextColorIndex = 14;
        private const int ShowNameIndex = 15;
        private const int OtherCharIdIndex = 16;
        private const int OtherNameIndex = 17;
        private const int OtherEmoteIndex = 18;
        private const int SelfOffsetIndex = 19;
        private const int OtherOffsetIndex = 20;
        private const int OtherFlipIndex = 21;
        private const int ImmediateIndex = 22;
        private const int SfxLoopingIndex = 23;
        private const int ScreenShakeIndex = 24;
        private const int FramesShakeIndex = 25;
        private const int FramesRealizationIndex = 26;
        private const int FramesSfxIndex = 27;
        private const int AdditiveIndex = 28;
        private const int EffectIndex = 29;
        private const int BlipsIndex = 30;
        private const int SlideIndex = 31;

        private string effectString = string.Empty;

        public DeskMods DeskMod { get; set; }
        public string PreAnim { get; set; }
        public string Character { get; set; }
        public string Emote { get; set; }
        public string Message { get; set; }
        public string Side { get; set; }
        public string SfxName { get; set; }
        public EmoteModifiers EmoteModifier { get; set; }
        public int CharId { get; set; }
        public int SfxDelay { get; set; }
        public ShoutModifiers ShoutModifier { get; set; }
        public string EvidenceID { get; set; }
        public bool Flip { get; set; }
        public string FlipFieldRaw { get; set; }
        public bool Realization { get; set; }
        public TextColors TextColor { get; set; }
        public string ShowName { get; set; }
        public int OtherCharId { get; set; }
        public string OtherName { get; set; }
        public string OtherEmote { get; set; }
        public (int Horizontal, int Vertical) SelfOffset { get; set; }
        public int OtherOffset { get; set; }
        public bool OtherFlip { get; set; }
        public bool NonInterruptingPreAnim { get; set; } // Changed to bool
        public bool SfxLooping { get; set; }
        public bool ScreenShake { get; set; }
        public string FramesShake { get; set; }
        public string FramesRealization { get; set; }
        public string FramesSfx { get; set; }
        public bool Additive { get; set; }
        public Effects Effect { get; set; }
        public string EffectString
        {
            get
            {
                if (!string.IsNullOrEmpty(effectString))
                {
                    return effectString;
                }

                return BuildDefaultEffectString(Effect);
            }
            set
            {
                effectString = value ?? string.Empty;
                Effect = ParseEffect(effectString);
            }
        }
        public string Blips { get; set; }
        public bool Slide { get; set; }
        public string OriginalCommand { get; set; }

        #region Enums
        public enum DeskMods
        {
            Unspecified = -1,
            Hidden = 0, // desk is hidden
            Shown = 1, // desk is shown
            HiddenDuringPreanimShownAfter = 2, // desk is hidden during preanim, shown when it ends
            ShownDuringPreanimHiddenAfter = 3, // desk is shown during preanim, hidden when it ends
            HiddenDuringPreanimCenteredAfter = 4, // desk is hidden during preanim, character is centered and pairing is ignored, when it ends desk is shown and pairing is restored
            ShownDuringPreanimCenteredAfter = 5, // desk is shown during preanim, when it ends character is centered and pairing is ignored
            Chat = 99 // chat mode. AKA depends on your position.
        }

        public enum EmoteModifiers
        {
            NoPreanimation = 0, // do not play preanimation; overridden to 2 by a non-0 objection modifier
            PlayPreanimation = 1, // play preanimation (and sfx)
            PlayPreanimationAndObjection = 2, // play preanimation and play objection
            Unused3 = 3, // unused
            Unused4 = 4, // unused
            NoPreanimationAndZoom = 5, // no preanimation and zoom
            ObjectionAndZoomNoPreanim = 6 // objection and zoom, no preanim
        }

        public enum ShoutModifiers
        {
            Nothing = 0, // nothing
            HoldIt = 1, // "Hold it!"
            Objection = 2, // "Objection!"
            TakeThat = 3, // "Take that!"
            Custom = 4 // custom shout
        }

        public enum TextColors
        {
            White = 0, // white
            Green = 1, // green
            Red = 2, // red
            Orange = 3, // orange
            Blue = 4, // blue (disables talking animation)
            Yellow = 5, // yellow
            Magenta = 6, // previously rainbow (removed in 2.8)
            Cyan = 7,
            Gray = 8,
        }

        public enum Effects
        {
            None = 0,
            Realization = 1,
            Hearts = 2,
            Reaction = 3,
            Impact = 4,
        }
        #endregion

        public ICMessage()
        {
            DeskMod = DeskMods.Chat;
            PreAnim = "";
            Character = "";
            Emote = "";
            Message = "";
            Side = "";
            SfxName = "";
            EmoteModifier = EmoteModifiers.NoPreanimation;
            CharId = -1;
            SfxDelay = 0;
            ShoutModifier = ShoutModifiers.Nothing;
            EvidenceID = "";
            Flip = false;
            FlipFieldRaw = string.Empty;
            Realization = false;
            TextColor = TextColors.White;
            ShowName = "";
            OtherCharId = -1;
            OtherName = "";
            OtherEmote = "";
            SelfOffset = (0, 0);
            OtherOffset = 0;
            OtherFlip = false;
            NonInterruptingPreAnim = false; // Changed to bool
            SfxLooping = false;
            ScreenShake = false;
            FramesShake = "";
            FramesRealization = "";
            FramesSfx = "";
            Additive = false;
            Effect = Effects.None;
            effectString = string.Empty;
            Blips = "";
            Slide = false;
            OriginalCommand = "";
        }

        private static string BuildDefaultEffectString(Effects effect)
        {
            return effect switch
            {
                Effects.Realization => "realization||sfx-realization",
                Effects.Hearts => "hearts||sfx-squee",
                Effects.Reaction => "reaction||sfx-reactionding",
                Effects.Impact => "impact||sfx-fan",
                _ => string.Empty
            };
        }

        private static Effects ParseEffect(string? value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                return Effects.None;
            }

            string effectName = normalized.Split('|')[0].Trim();
            return effectName.ToLowerInvariant() switch
            {
                "realization" => Effects.Realization,
                "hearts" => Effects.Hearts,
                "reaction" => Effects.Reaction,
                "impact" => Effects.Impact,
                _ => Effects.None
            };
        }

        public static ICMessage? FromConsoleLine(string message)
        {
            if (!message.StartsWith("MS#"))
            {
                CustomConsole.Warning($"Invalid IC message format: {message}");
                return null;
            }

            string[] fields = ExtractPacketFields(message);
            if (fields.Length < MinimumFieldCount)
            {
                CustomConsole.Warning(
                    $"Incomplete IC message received. Expected at least {MinimumFieldCount} fields, got {fields.Length}");
                return null;
            }

            try
            {
                (int Horizontal, int Vertical) selfOffset = ParseOffset(GetField(fields, SelfOffsetIndex));

                return new ICMessage
                {
                    DeskMod = ParseDeskMod(GetField(fields, DeskModIndex)),
                    PreAnim = DecodePacketField(GetField(fields, PreAnimIndex)),
                    Character = DecodePacketField(GetField(fields, CharacterIndex)),
                    Emote = DecodePacketField(GetField(fields, EmoteIndex)),
                    Message = DecodePacketField(GetField(fields, MessageIndex)),
                    Side = DecodePacketField(GetField(fields, SideIndex)),
                    SfxName = DecodePacketField(GetField(fields, SfxNameIndex)),
                    EmoteModifier = ParseEnum(GetField(fields, EmoteModifierIndex), EmoteModifiers.NoPreanimation),
                    CharId = ParseInt(GetField(fields, CharIdIndex), -1),
                    SfxDelay = ParseInt(GetField(fields, SfxDelayIndex), 0),
                    ShoutModifier = ParseEnum(GetField(fields, ShoutModifierIndex), ShoutModifiers.Nothing),
                    EvidenceID = DecodePacketField(GetField(fields, EvidenceIdIndex)),
                    Flip = ParseBoolean(GetField(fields, FlipIndex)),
                    Realization = ParseBoolean(GetField(fields, RealizationIndex)),
                    TextColor = ParseEnum(GetField(fields, TextColorIndex), TextColors.White),
                    ShowName = ResolveShowName(fields),
                    OtherCharId = ParsePairCharId(GetField(fields, OtherCharIdIndex)),
                    OtherName = DecodePacketField(GetField(fields, OtherNameIndex)),
                    OtherEmote = DecodePacketField(GetField(fields, OtherEmoteIndex)),
                    SelfOffset = selfOffset,
                    OtherOffset = ParseInt(GetField(fields, OtherOffsetIndex), 0),
                    OtherFlip = ParseBoolean(GetField(fields, OtherFlipIndex)),
                    NonInterruptingPreAnim = ParseBoolean(GetField(fields, ImmediateIndex)),
                    SfxLooping = ParseBoolean(GetField(fields, SfxLoopingIndex)),
                    ScreenShake = ParseBoolean(GetField(fields, ScreenShakeIndex)),
                    FramesShake = DecodePacketField(GetField(fields, FramesShakeIndex)),
                    FramesRealization = DecodePacketField(GetField(fields, FramesRealizationIndex)),
                    FramesSfx = DecodePacketField(GetField(fields, FramesSfxIndex)),
                    Additive = ParseBoolean(GetField(fields, AdditiveIndex)),
                    EffectString = DecodePacketField(GetField(fields, EffectIndex)),
                    Blips = DecodePacketField(GetField(fields, BlipsIndex)),
                    Slide = ParseBoolean(GetField(fields, SlideIndex)),
                    OriginalCommand = message
                };
            }
            catch (Exception ex)
            {
                // Use the enhanced logging with detailed error info
                CustomConsole.Error($"Failed to parse IC message", ex);
                return null;
            }
        }

        public static string GetCommand(ICMessage message, SerializationOptions? options = null)
        {
            SerializationOptions effectiveOptions = options ?? new SerializationOptions();
            List<string> fields = new List<string>
            {
                message.DeskMod == DeskMods.Chat ? "chat" : ((int)message.DeskMod).ToString(CultureInfo.InvariantCulture),
                message.PreAnim ?? string.Empty,
                message.Character ?? string.Empty,
                message.Emote ?? string.Empty,
                message.Message ?? string.Empty,
                message.Side ?? string.Empty,
                message.SfxName ?? string.Empty,
                ((int)message.EmoteModifier).ToString(CultureInfo.InvariantCulture),
                message.CharId.ToString(CultureInfo.InvariantCulture),
                message.SfxDelay.ToString(CultureInfo.InvariantCulture),
                ((int)message.ShoutModifier).ToString(CultureInfo.InvariantCulture),
                message.EvidenceID ?? string.Empty,
                string.IsNullOrWhiteSpace(message.FlipFieldRaw) ? (message.Flip ? "1" : "0") : message.FlipFieldRaw,
                message.Realization ? "1" : "0",
                ((int)message.TextColor).ToString(CultureInfo.InvariantCulture)
            };

            if (effectiveOptions.IncludeCcccIcSupport)
            {
                fields.Add(message.ShowName ?? string.Empty);
                fields.Add(message.OtherCharId.ToString(CultureInfo.InvariantCulture));
                fields.Add(BuildOffsetField(message.SelfOffset, effectiveOptions.IncludeVerticalOffset));
                fields.Add(message.NonInterruptingPreAnim ? "1" : "0");
            }

            if (effectiveOptions.IncludeLoopingSfx)
            {
                fields.Add(message.SfxLooping ? "1" : "0");
                fields.Add(message.ScreenShake ? "1" : "0");
                fields.Add(message.FramesShake ?? string.Empty);
                fields.Add(message.FramesRealization ?? string.Empty);
                fields.Add(message.FramesSfx ?? string.Empty);
            }

            if (effectiveOptions.IncludeAdditive)
            {
                fields.Add(message.Additive ? "1" : "0");
            }

            if (effectiveOptions.IncludeEffects)
            {
                fields.Add(message.EffectString ?? string.Empty);
            }

            if (effectiveOptions.IncludeCustomBlips)
            {
                fields.Add(message.Blips ?? string.Empty);
                if (effectiveOptions.IncludeSlide)
                {
                    fields.Add(message.Slide ? "1" : "0");
                }
            }

            return "MS#" + string.Join("#", fields.Select(EncodePacketField)) + "#%";
        }

        private static string[] ExtractPacketFields(string packet)
        {
            string[] parts = packet.Split('#');
            if (parts.Length <= 1)
            {
                return Array.Empty<string>();
            }

            int endIndex = parts.Length;
            if (string.Equals(parts[^1], "%", StringComparison.Ordinal))
            {
                endIndex--;
            }

            int fieldCount = Math.Max(0, endIndex - 1);
            string[] fields = new string[fieldCount];
            Array.Copy(parts, 1, fields, 0, fieldCount);
            return fields;
        }

        private static string GetField(IReadOnlyList<string> fields, int index)
        {
            return index >= 0 && index < fields.Count ? fields[index] ?? string.Empty : string.Empty;
        }

        private static string DecodePacketField(string value)
        {
            return Globals.ReplaceTextForSymbols(value ?? string.Empty);
        }

        private static string EncodePacketField(string value)
        {
            return Globals.ReplaceSymbolsForText(value ?? string.Empty);
        }

        private static DeskMods ParseDeskMod(string value)
        {
            string normalized = DecodePacketField(value).Trim();
            if (string.Equals(normalized, "chat", StringComparison.OrdinalIgnoreCase))
            {
                return DeskMods.Chat;
            }

            return ParseEnum(normalized, DeskMods.Hidden);
        }

        private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
            where TEnum : struct, Enum
        {
            string normalized = DecodePacketField(value).Trim();
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && Enum.IsDefined(typeof(TEnum), parsed))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), parsed);
            }

            return fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(
                DecodePacketField(value).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
                ? parsed
                : fallback;
        }

        private static bool ParseBoolean(string value)
        {
            return string.Equals(DecodePacketField(value).Trim(), "1", StringComparison.Ordinal);
        }

        private static int ParsePairCharId(string value)
        {
            string normalized = DecodePacketField(value).Trim();
            string pairToken = normalized.Split('^')[0];
            return ParseInt(pairToken, -1);
        }

        private static string ResolveShowName(IReadOnlyList<string> fields)
        {
            string rawShowName = DecodePacketField(GetField(fields, ShowNameIndex));
            if (!string.IsNullOrWhiteSpace(rawShowName))
            {
                return rawShowName;
            }

            string characterName = DecodePacketField(GetField(fields, CharacterIndex));
            return CharacterFolder.FullList.FirstOrDefault(ini => ini.Name == characterName)?.configINI?.ShowName
                ?? characterName;
        }

        private static (int Horizontal, int Vertical) ParseOffset(string value)
        {
            string normalized = DecodePacketField(value).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return (0, 0);
            }

            string[] offsetParts = normalized.Split('&');
            if (offsetParts.Length == 1)
            {
                return (ParseInt(offsetParts[0], 0), 0);
            }

            return (ParseInt(offsetParts[0], 0), ParseInt(offsetParts[1], 0));
        }

        private static string BuildOffsetField((int Horizontal, int Vertical) offset, bool includeVerticalOffset)
        {
            if (!includeVerticalOffset)
            {
                return offset.Horizontal.ToString(CultureInfo.InvariantCulture);
            }

            return $"{offset.Horizontal.ToString(CultureInfo.InvariantCulture)}&{offset.Vertical.ToString(CultureInfo.InvariantCulture)}";
        }

        public static Color GetColorFromTextColor(TextColors textColor)
        {
            return textColor switch
            {
                TextColors.White => Color.FromArgb(247, 247, 247),
                TextColors.Green => Color.FromArgb(0, 247, 0),
                TextColors.Red => Color.FromArgb(247, 0, 57),
                TextColors.Orange => Color.FromArgb(247, 115, 57),
                TextColors.Blue => Color.FromArgb(107, 198, 247),
                TextColors.Yellow => Color.FromArgb(247, 247, 0),
                TextColors.Magenta => Color.FromArgb(247, 115, 247),
                TextColors.Cyan => Color.FromArgb(128, 247, 247),
                TextColors.Gray => Color.FromArgb(160, 181, 205),
                _ => Color.FromArgb(247, 247, 247),
            };
        }
    }
}
