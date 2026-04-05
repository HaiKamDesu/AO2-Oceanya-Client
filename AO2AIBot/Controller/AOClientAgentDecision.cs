using AOBot_Testing.Structures;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Parsed AI decision describing how to control the AO client.
    /// </summary>
    public sealed class AOClientAgentDecision
    {
        /// <summary>
        /// Gets or sets a value indicating whether the model decided to act.
        /// </summary>
        public bool ShouldAct { get; set; }

        /// <summary>
        /// Gets or sets the target chat channel for the outgoing message.
        /// </summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the outgoing message text.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the IC showname override.
        /// </summary>
        public string IcShowname { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the OOC showname override.
        /// </summary>
        public string OocShowname { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested character name.
        /// </summary>
        public string Character { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested emote id.
        /// </summary>
        public string Emote { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested area name.
        /// </summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested position.
        /// </summary>
        public string Position { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested server character slot.
        /// </summary>
        public string IniPuppetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested SFX token.
        /// </summary>
        public string Sfx { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the requested desk modifier.
        /// </summary>
        public ICMessage.DeskMods? DeskMod { get; set; }

        /// <summary>
        /// Gets or sets the requested emote modifier.
        /// </summary>
        public ICMessage.EmoteModifiers? EmoteModifier { get; set; }

        /// <summary>
        /// Gets or sets the requested shout modifier.
        /// </summary>
        public ICMessage.ShoutModifiers? ShoutModifier { get; set; }

        /// <summary>
        /// Gets or sets the requested text color.
        /// </summary>
        public ICMessage.TextColors? TextColor { get; set; }

        /// <summary>
        /// Gets or sets the requested message effect.
        /// </summary>
        public ICMessage.Effects? Effect { get; set; }

        /// <summary>
        /// Gets or sets the requested preanimation flag.
        /// </summary>
        public bool? PreanimEnabled { get; set; }

        /// <summary>
        /// Gets or sets the requested flip flag.
        /// </summary>
        public bool? Flip { get; set; }

        /// <summary>
        /// Gets or sets the requested additive flag.
        /// </summary>
        public bool? Additive { get; set; }

        /// <summary>
        /// Gets or sets the requested immediate flag.
        /// </summary>
        public bool? Immediate { get; set; }

        /// <summary>
        /// Gets or sets the requested screenshake flag.
        /// </summary>
        public bool? Screenshake { get; set; }

        /// <summary>
        /// Gets or sets the requested horizontal self offset.
        /// </summary>
        public int? SelfOffsetHorizontal { get; set; }

        /// <summary>
        /// Gets or sets the requested vertical self offset.
        /// </summary>
        public int? SelfOffsetVertical { get; set; }

        /// <summary>
        /// Gets a value indicating whether any non-message state mutations were requested.
        /// </summary>
        public bool HasStateChanges =>
            !string.IsNullOrWhiteSpace(IcShowname)
            || !string.IsNullOrWhiteSpace(OocShowname)
            || !string.IsNullOrWhiteSpace(Character)
            || !string.IsNullOrWhiteSpace(Emote)
            || !string.IsNullOrWhiteSpace(Area)
            || !string.IsNullOrWhiteSpace(Position)
            || !string.IsNullOrWhiteSpace(IniPuppetName)
            || !string.IsNullOrWhiteSpace(Sfx)
            || DeskMod.HasValue
            || EmoteModifier.HasValue
            || ShoutModifier.HasValue
            || TextColor.HasValue
            || Effect.HasValue
            || PreanimEnabled.HasValue
            || Flip.HasValue
            || Additive.HasValue
            || Immediate.HasValue
            || Screenshake.HasValue
            || SelfOffsetHorizontal.HasValue
            || SelfOffsetVertical.HasValue;
    }
}
