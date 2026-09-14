using System.Windows.Media;

namespace OceanyaClient.Components
{
    /// <summary>
    /// What a run in a log is, so its colour can be re-applied after the fact.
    /// </summary>
    /// <remarks>
    /// Messages already in a log were written with the colours in force at the time. Tagging each run with
    /// its role lets a theme change repaint the whole backlog instead of only affecting what arrives next -
    /// otherwise applying a theme leaves a log in two colour schemes at once.
    /// </remarks>
    public enum LogRunRole
    {
        /// <summary>Not coloured by the theme.</summary>
        None = 0,

        /// <summary>Body text that follows the log's own colour.</summary>
        Body = 1,

        /// <summary>Another person's name.</summary>
        SenderName = 2,

        /// <summary>A server message's name.</summary>
        ServerName = 3,

        /// <summary>Your own name.</summary>
        SelfName = 4,

        /// <summary>A timestamp or similar trailing detail.</summary>
        Timestamp = 5
    }

    /// <summary>
    /// Per-log colours a theme can set, matching what AO2 exposes in <c>courtroom_fonts.ini</c>.
    /// </summary>
    /// <remarks>
    /// AO2 gives each log a body colour plus separate colours for names and timestamps
    /// (<c>ic_chatlog_color</c>, <c>ic_chatlog_showname_color</c>, <c>ic_chatlog_selfname_color</c>,
    /// <c>ic_chatlog_timestamp_color</c>, and <c>server_chatlog_color</c> /
    /// <c>server_chatlog_sender_color</c> for the OOC log). Our logs write their runs with explicit
    /// brushes, so a theme colour has to reach them here rather than through inherited Foreground.
    ///
    /// Every field is nullable and null means "keep the control's own colour", which is what makes a
    /// reset back to defaults free: hand the log an empty set.
    /// </remarks>
    public sealed class LogThemeColors
    {
        /// <summary>Body text colour.</summary>
        public Brush? Text { get; init; }

        /// <summary>Colour of other people's names.</summary>
        public Brush? SenderName { get; init; }

        /// <summary>Colour of a server message's name.</summary>
        public Brush? ServerName { get; init; }

        /// <summary>Colour of your own name.</summary>
        public Brush? SelfName { get; init; }

        /// <summary>Colour of timestamps and similar trailing detail.</summary>
        public Brush? Timestamp { get; init; }

        /// <summary>Gets a value indicating whether anything at all is set.</summary>
        public bool IsEmpty =>
            Text == null && SenderName == null && ServerName == null && SelfName == null && Timestamp == null;
    }
}
