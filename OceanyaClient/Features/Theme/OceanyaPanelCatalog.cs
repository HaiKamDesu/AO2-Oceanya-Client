using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Placement of a panel inside the GM main window surface, in unscaled layout units.
    /// </summary>
    /// <param name="Left">Distance from the surface's left edge.</param>
    /// <param name="Top">Distance from the surface's top edge.</param>
    /// <param name="Width">Panel width.</param>
    /// <param name="Height">Panel height.</param>
    public readonly record struct OceanyaPanelPlacement(double Left, double Top, double Width, double Height);

    /// <summary>
    /// Describes one dockable region of the GM main window.
    /// </summary>
    /// <remarks>
    /// Panel ids are part of the shared Oceanya theme file, so they are stable strings: renaming one
    /// breaks every theme that references it and requires a theme compatibility bump. See
    /// <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public sealed class OceanyaPanelDescriptor
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OceanyaPanelDescriptor"/> class.
        /// </summary>
        /// <param name="id">Stable panel id used by theme files.</param>
        /// <param name="displayName">Human readable name shown in the theme editor.</param>
        /// <param name="placement">Placement matching the pre-theme fixed layout.</param>
        /// <param name="minimumWidth">Smallest width the panel stays usable at.</param>
        /// <param name="minimumHeight">Smallest height the panel stays usable at.</param>
        /// <param name="backdropPlacement">Placement of the panel's backdrop element, when it has one.</param>
        public OceanyaPanelDescriptor(
            string id,
            string displayName,
            OceanyaPanelPlacement placement,
            double minimumWidth,
            double minimumHeight,
            OceanyaPanelPlacement? backdropPlacement = null)
        {
            Id = id;
            DisplayName = displayName;
            Placement = placement;
            MinimumWidth = minimumWidth;
            MinimumHeight = minimumHeight;
            BackdropPlacement = backdropPlacement;
        }

        /// <summary>Gets the stable panel id used by theme files.</summary>
        public string Id { get; }

        /// <summary>Gets the human readable panel name.</summary>
        public string DisplayName { get; }

        /// <summary>Gets the default placement, which reproduces the pre-theme fixed layout.</summary>
        public OceanyaPanelPlacement Placement { get; }

        /// <summary>Gets the smallest width the panel stays usable at.</summary>
        public double MinimumWidth { get; }

        /// <summary>Gets the smallest height the panel stays usable at.</summary>
        public double MinimumHeight { get; }

        /// <summary>Gets the backdrop placement, for panels drawn on top of a separate background element.</summary>
        public OceanyaPanelPlacement? BackdropPlacement { get; }
    }

    /// <summary>
    /// Registry of the GM main window's dockable panels.
    /// </summary>
    /// <remarks>
    /// This is the first step of the Oceanya theme system: placement moves out of MainWindow.xaml and
    /// into data, so a later dock host (and the theme file) can drive it. The values here reproduce the
    /// historic fixed layout exactly, so registering a panel changes nothing visually.
    /// </remarks>
    public static class OceanyaPanelCatalog
    {
        /// <summary>Panel id for the IC chat log.</summary>
        public const string IcLogPanelId = "ic_log";

        /// <summary>Panel id for the OOC chat log.</summary>
        public const string OocLogPanelId = "ooc_log";

        /// <summary>
        /// Panel id for the client strip (header, add/remove buttons and the paged client buttons).
        /// </summary>
        /// <remarks>
        /// The control behind it is a <c>PageButtonGrid</c> historically named "EmoteGrid" in
        /// MainWindow.xaml, but it holds CLIENT buttons; the emote grid lives inside ICMessageSettings.
        /// </remarks>
        public const string ClientsListPanelId = "clients_list";

        /// <summary>Panel id for the IC message settings strip.</summary>
        public const string IcSettingsPanelId = "ic_settings";

        /// <summary>Panel id for the shout modifier row.</summary>
        public const string ShoutRowPanelId = "shout_row";

        /// <summary>Panel id for the optional Dredd background overlay row.</summary>
        public const string DreddFeatureRowPanelId = "dredd_row";

        private static readonly IReadOnlyList<OceanyaPanelDescriptor> PanelList = new[]
        {
            new OceanyaPanelDescriptor(
                IcLogPanelId,
                "IC Log",
                new OceanyaPanelPlacement(59, 0, 228, 298),
                minimumWidth: 120,
                minimumHeight: 80,
                backdropPlacement: new OceanyaPanelPlacement(55, 0, 232, 323)),
            new OceanyaPanelDescriptor(
                OocLogPanelId,
                "OOC Log",
                new OceanyaPanelPlacement(287, 0, 222, 298),
                minimumWidth: 120,
                minimumHeight: 80,
                backdropPlacement: new OceanyaPanelPlacement(287, 0, 222, 333)),
            new OceanyaPanelDescriptor(
                ClientsListPanelId,
                "Clients",
                new OceanyaPanelPlacement(0, 0, 54, 298),
                minimumWidth: 54,
                minimumHeight: 120),
            new OceanyaPanelDescriptor(
                ShoutRowPanelId,
                "Shouts",
                new OceanyaPanelPlacement(2, 296, 428, 42),
                minimumWidth: 200,
                minimumHeight: 42),
            new OceanyaPanelDescriptor(
                IcSettingsPanelId,
                "IC Message Settings",
                new OceanyaPanelPlacement(0, 343, 509, 260),
                minimumWidth: 320,
                minimumHeight: 160),
            new OceanyaPanelDescriptor(
                DreddFeatureRowPanelId,
                "Dredd Overlay Row",
                new OceanyaPanelPlacement(0, 603, 509, 30),
                minimumWidth: 320,
                minimumHeight: 30)
        };

        /// <summary>
        /// Gets every registered panel.
        /// </summary>
        public static IReadOnlyList<OceanyaPanelDescriptor> Panels => PanelList;

        /// <summary>
        /// Gets a panel descriptor by its stable id.
        /// </summary>
        /// <param name="id">Panel id.</param>
        /// <returns>The descriptor.</returns>
        /// <exception cref="KeyNotFoundException">The id is not registered.</exception>
        public static OceanyaPanelDescriptor Get(string id)
        {
            return TryGet(id) ?? throw new KeyNotFoundException($"Unknown Oceanya panel id '{id}'.");
        }

        /// <summary>
        /// Finds a panel descriptor by its stable id.
        /// </summary>
        /// <param name="id">Panel id.</param>
        /// <returns>The descriptor, or null when the id is not registered.</returns>
        public static OceanyaPanelDescriptor? TryGet(string id)
        {
            return PanelList.FirstOrDefault(panel => string.Equals(panel.Id, id, StringComparison.Ordinal));
        }
    }
}
