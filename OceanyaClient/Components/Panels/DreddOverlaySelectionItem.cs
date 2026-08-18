namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// One entry in the Dredd background overlay selector.
    /// </summary>
    public sealed class DreddOverlaySelectionItem
    {
        /// <summary>Gets or sets the overlay name as stored in the savefile.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the text shown in the selector and its dropdown.</summary>
        public string DisplayText { get; set; } = string.Empty;

        /// <summary>Gets or sets the resolved overlay file path.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether this entry means "no overlay".</summary>
        public bool IsNone { get; set; }

        /// <summary>Gets or sets a value indicating whether this entry only exists for the current session.</summary>
        public bool IsTransient { get; set; }
    }
}
