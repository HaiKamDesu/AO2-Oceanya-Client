namespace AO2AIBot.Controller
{
    /// <summary>
    /// Snapshot of the controls and state exposed to the AI agent.
    /// </summary>
    public sealed class AOClientControlSnapshot
    {
        /// <summary>
        /// Gets or sets the client nickname shown in the multi-client UI.
        /// </summary>
        public string ClientName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the backing network client is connected.
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Gets or sets the current IC showname.
        /// </summary>
        public string IcShowname { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current OOC showname.
        /// </summary>
        public string OocShowname { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current selected character.
        /// </summary>
        public string CurrentCharacter { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current selected emote.
        /// </summary>
        public string CurrentEmote { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current area.
        /// </summary>
        public string CurrentArea { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current position.
        /// </summary>
        public string CurrentPosition { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current background token.
        /// </summary>
        public string CurrentBackground { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current server character slot name.
        /// </summary>
        public string CurrentIniPuppetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current server character slot index.
        /// </summary>
        public int CurrentIniPuppetId { get; set; } = -1;

        /// <summary>
        /// Gets or sets the selected SFX token.
        /// </summary>
        public string CurrentSfx { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current desk modifier.
        /// </summary>
        public string DeskMod { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current emote modifier.
        /// </summary>
        public string EmoteModifier { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current shout modifier.
        /// </summary>
        public string ShoutModifier { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current effect.
        /// </summary>
        public string Effect { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current text color.
        /// </summary>
        public string TextColor { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether flip is enabled.
        /// </summary>
        public bool Flip { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether preanimation is enabled.
        /// </summary>
        public bool PreanimEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether additive mode is enabled.
        /// </summary>
        public bool Additive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether immediate mode is enabled.
        /// </summary>
        public bool Immediate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether screenshake is enabled.
        /// </summary>
        public bool Screenshake { get; set; }

        /// <summary>
        /// Gets or sets the horizontal self offset.
        /// </summary>
        public int SelfOffsetHorizontal { get; set; }

        /// <summary>
        /// Gets or sets the vertical self offset.
        /// </summary>
        public int SelfOffsetVertical { get; set; }

        /// <summary>
        /// Gets or sets the available character names.
        /// </summary>
        public IReadOnlyList<string> AvailableCharacters { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the available emotes for the current character.
        /// </summary>
        public IReadOnlyList<string> AvailableEmotes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the available positions for the current background.
        /// </summary>
        public IReadOnlyList<string> AvailablePositions { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the areas reported by the server.
        /// </summary>
        public IReadOnlyList<string> AvailableAreas { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the available SFX tokens for the current character.
        /// </summary>
        public IReadOnlyList<string> AvailableSfx { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the currently known server features.
        /// </summary>
        public IReadOnlyList<string> ServerFeatures { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the server character availability map.
        /// </summary>
        public IReadOnlyDictionary<string, bool> AvailableIniPuppets { get; set; } =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }
}
