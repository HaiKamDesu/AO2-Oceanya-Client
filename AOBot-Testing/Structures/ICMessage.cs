using Common;
using System.Drawing;

namespace AOBot_Testing.Structures
{
    public class ICMessage
    {
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
                string effect = "";
                switch (Effect)
                {
                    default:
                    case Effects.None:
                        effect = "||";
                        break;
                    case Effects.Realization:
                        effect = "realization||sfx-realization";
                        break;
                    case Effects.Hearts:
                        effect = "hearts||sfx-squee";
                        break;
                    case Effects.Reaction:
                        effect = "reaction||sfx-reactionding";
                        break;
                    case Effects.Impact:
                        effect = "impact||sfx-fan";
                        break;
                }
                return effect;
            }
            set
            {
                switch (value)
                {
                    default:
                    case "":
                        Effect = Effects.None;
                        break;
                    case "realization||sfx-realization":
                        Effect = Effects.Realization;
                        break;
                    case "hearts||sfx-squee":
                        Effect = Effects.Hearts;
                        break;
                    case "reaction||sfx-reactionding":
                        Effect = Effects.Reaction;
                        break;
                    case "impact||sfx-fan":
                        Effect = Effects.Impact;
                        break;
                }
            }
        }
        public string Blips { get; set; }
        public string OriginalCommand { get; set; }

        #region Enums
        public enum DeskMods
        {
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
            Blips = "";
            OriginalCommand = "";
        }

        public static ICMessage? FromConsoleLine(string message)
        {
            if (!message.StartsWith("MS#"))
            {
                CustomConsole.Warning($"Invalid IC message format: {message}");
                return null;
            }

            string[] parts = message.Split('#');
            if (parts.Length < 28) // Compact packets generated by this client contain 28 parts.
            {
                CustomConsole.Warning($"Incomplete IC message received. Expected at least 28 parts, got {parts.Length}");
                CustomConsole.Debug($"Message content: {message}");
                return null;
            }

            try
            {
                bool isFullPacket = parts.Length >= 31;

                // Handle SelfOffset safely, ensuring it properly parses even with special characters
                var selfOffset = (Horizontal: 0, Vertical: 0);
                try {
                    string selfOffsetPart = isFullPacket ? parts[20] : parts[18];
                    if (selfOffsetPart.Contains("<and>")) 
                    {
                        var offsetParts = selfOffsetPart.Split(new[] { "<and>" }, StringSplitOptions.None);
                        selfOffset = (
                            Horizontal: int.TryParse(offsetParts[0], out int h) ? h : 0,
                            Vertical: int.TryParse(offsetParts[1], out int v) ? v : 0
                        );
                    }
                    else if (selfOffsetPart.Contains("<") && selfOffsetPart.Contains(">"))
                    {
                        var selfOffsetParts = selfOffsetPart.Split('<', '>');
                        selfOffset = (
                            Horizontal: int.TryParse(selfOffsetParts[0], out int horizontal) ? horizontal : 0,
                            Vertical: int.TryParse(selfOffsetParts[1], out int vertical) ? vertical : 0
                        );
                    }
                } 
                catch (Exception ex) 
                {
                    string selfOffsetPart = isFullPacket ? parts[20] : parts[18];
                    CustomConsole.Warning($"Failed to parse SelfOffset '{selfOffsetPart}'", ex);
                }

                DeskMods deskMod;
                if (parts[1] == "chat")
                {
                    deskMod = DeskMods.Chat;
                }
                else
                {
                    deskMod = int.TryParse(parts[1], out int deskModifier) ? (DeskMods)deskModifier : DeskMods.Hidden;
                }

                return new ICMessage
                {
                    DeskMod = deskMod,
                    PreAnim = parts[2],
                    Character = parts[3],
                    Emote = parts[4],
                    Message = Globals.ReplaceTextForSymbols(parts[5]),
                    Side = parts[6],
                    SfxName = parts[7],
                    EmoteModifier = int.TryParse(parts[8], out int emoteModifier) ? (EmoteModifiers)emoteModifier : EmoteModifiers.NoPreanimation,
                    CharId = int.TryParse(parts[9], out int charId) ? charId : -1,
                    SfxDelay = int.TryParse(parts[10], out int sfxDelay) ? sfxDelay : 0,
                    ShoutModifier = int.TryParse(parts[11], out int shoutModifier) ? (ShoutModifiers)shoutModifier : ShoutModifiers.Nothing,
                    EvidenceID = parts[12],
                    Flip = parts[13] == "1",
                    Realization = parts[14] == "1",
                    TextColor = int.TryParse(parts[15], out int textColor) ? (TextColors)textColor : TextColors.White,
                    ShowName = string.IsNullOrEmpty(parts[16]) ? 
                        CharacterFolder.FullList.FirstOrDefault(ini => ini.Name == parts[3])?.configINI?.ShowName ?? parts[3] : 
                        Globals.ReplaceTextForSymbols(parts[16]),
                    OtherCharId = int.TryParse(parts[17], out int otherCharId) ? otherCharId : -1,
                    OtherName = isFullPacket ? parts[18] : "",
                    OtherEmote = isFullPacket ? parts[19] : "",
                    SelfOffset = selfOffset,
                    OtherOffset = isFullPacket && int.TryParse(parts[21], out int otherOffset) ? otherOffset : 0,
                    OtherFlip = isFullPacket && parts[22] == "1",
                    NonInterruptingPreAnim = isFullPacket ? parts[23] == "1" : parts[19] == "1",
                    SfxLooping = isFullPacket ? parts[24] == "1" : parts[20] == "1",
                    ScreenShake = isFullPacket ? parts[25] == "1" : parts[21] == "1",
                    FramesShake = isFullPacket ? parts[26] : parts[22],
                    FramesRealization = isFullPacket ? parts[27] : parts[23],
                    FramesSfx = isFullPacket ? parts[28] : parts[24],
                    Additive = isFullPacket ? parts[29] == "1" : parts[25] == "1",
                    EffectString = isFullPacket ? parts[30] : parts[26],
                    Blips = isFullPacket
                        ? (parts.Length > 31 ? parts[31].TrimEnd('%') : "")
                        : (parts.Length > 27 ? parts[27].TrimEnd('%') : ""),
                    OriginalCommand = message
                };
            }
            catch (Exception ex)
            {
                // Use the enhanced logging with detailed error info
                CustomConsole.Error($"Failed to parse IC message", ex);
                CustomConsole.Debug($"Message content: {message}");
                return null;
            }
        }

        public static string GetCommand(ICMessage message)
        {
            string encodedBlips = Globals.ReplaceSymbolsForText(message.Blips ?? string.Empty);
            string blipsSegment = string.IsNullOrEmpty(encodedBlips) ? "%" : $"{encodedBlips}#%";

            return $"MS#" +
                    $"{(message.DeskMod == DeskMods.Chat ? "chat" : ((int)message.DeskMod).ToString())}#" +
                    $"{message.PreAnim}#" +
                    $"{message.Character}#" +
                    $"{message.Emote}#" +
                    $"{Globals.ReplaceSymbolsForText(message.Message)}#" +
                    $"{message.Side}#" +
                    $"{message.SfxName}#" +
                    $"{(int)message.EmoteModifier}#" +
                    $"{message.CharId}#" +
                    $"{message.SfxDelay}#" +
                    $"{(int)message.ShoutModifier}#" +
                    $"{message.EvidenceID}#" +
                    $"{(message.Flip ? "1" : "0")}#" +
                    $"{(message.Realization ? "1" : "0")}#" +
                    $"{(int)message.TextColor}#" +
                    $"{Globals.ReplaceSymbolsForText(message.ShowName)}#" +
                    $"{message.OtherCharId}#" +
                    $"{message.SelfOffset.Horizontal}<and>{message.SelfOffset.Vertical}#" +
                    $"{(message.NonInterruptingPreAnim ? "1" : "0")}#" + // Changed to bool
                    $"{(message.SfxLooping ? "1" : "0")}#" +
                    $"{(message.ScreenShake ? "1" : "0")}#" +
                    $"{message.FramesShake}#" +
                    $"{message.FramesRealization}#" +
                    $"{message.FramesSfx}#" +
                    $"{(message.Additive ? "1" : "0")}#" +
                    $"{message.EffectString}#" +
                    $"{blipsSegment}";
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
