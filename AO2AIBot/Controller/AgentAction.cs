using AOBot_Testing.Structures;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Known action types the AI agent can emit.
    /// </summary>
    public static class AgentActionType
    {
        public const string Speak = "speak";
        public const string SetIcShowname = "set_ic_showname";
        public const string SetOocShowname = "set_ooc_showname";
        public const string SetCharacter = "set_character";
        public const string SetPosition = "set_position";
        public const string SetTextColor = "set_text_color";
        public const string SetFlip = "set_flip";
        public const string SetAdditive = "set_additive";
        public const string SetImmediate = "set_immediate";
        public const string SetPreanimEnabled = "set_preanim_enabled";
        public const string SetArea = "set_area";
        public const string SetIniPuppet = "set_ini_puppet";
        public const string SetOffset = "set_offset";
        public const string SetEmote = "set_emote";
        public const string SetSfx = "set_sfx";
        public const string SetDeskMod = "set_desk_mod";
        public const string SetEffect = "set_effect";
        public const string SetScreenshake = "set_screenshake";
        public const string SetShoutModifier = "set_shout_modifier";
        public const string SetEmoteModifier = "set_emote_modifier";

        /// <summary>
        /// All known action type strings.
        /// </summary>
        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            Speak, SetIcShowname, SetOocShowname, SetCharacter, SetPosition,
            SetTextColor, SetFlip, SetAdditive, SetImmediate, SetPreanimEnabled,
            SetArea, SetIniPuppet, SetOffset, SetEmote, SetSfx, SetDeskMod,
            SetEffect, SetScreenshake, SetShoutModifier, SetEmoteModifier
        };
    }

    /// <summary>
    /// A single ordered action emitted by the AI agent.
    /// Each action corresponds to one state mutation or outbound message.
    /// </summary>
    public sealed class AgentAction
    {
        /// <summary>
        /// Gets or sets the action type. Must be one of <see cref="AgentActionType"/> constants.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the string value for single-value actions (showname, character, position, area, etc.).
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the boolean value for flag actions (flip, additive, immediate, etc.).
        /// </summary>
        public bool? BoolValue { get; set; }

        /// <summary>
        /// Gets or sets the integer value for numeric actions.
        /// </summary>
        public int? IntValue { get; set; }

        /// <summary>
        /// Gets or sets the horizontal offset for set_offset actions.
        /// </summary>
        public int? Horizontal { get; set; }

        /// <summary>
        /// Gets or sets the vertical offset for set_offset actions.
        /// </summary>
        public int? Vertical { get; set; }

        // === Speak-specific fields ===

        /// <summary>
        /// Gets or sets the channel for speak actions ("IC" or "OOC").
        /// </summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the message text for speak actions.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the emote for IC speak actions. Required when channel is IC.
        /// </summary>
        public string Emote { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the text color override for this speak action.
        /// </summary>
        public string TextColor { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the shout modifier for this speak action.
        /// </summary>
        public string ShoutModifier { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the visual effect for this speak action.
        /// </summary>
        public string Effect { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether screenshake is enabled for this speak action.
        /// </summary>
        public bool? Screenshake { get; set; }

        /// <summary>
        /// Gets or sets the SFX for this speak action.
        /// </summary>
        public string Sfx { get; set; } = string.Empty;
    }

    /// <summary>
    /// The complete parsed response from the AI agent following the strict action-array contract.
    /// </summary>
    public sealed class AgentResponse
    {
        /// <summary>
        /// Gets or sets whether the agent decided to respond.
        /// </summary>
        public bool ShouldRespond { get; set; }

        /// <summary>
        /// Gets or sets the ordered list of actions to execute.
        /// </summary>
        public List<AgentAction> Actions { get; set; } = new List<AgentAction>();
    }
}
