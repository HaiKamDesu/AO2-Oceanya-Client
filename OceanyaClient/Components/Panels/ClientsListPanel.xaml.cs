using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// The GM client strip: the "Clients" header, the add/remove buttons, and the paged grid of client
    /// toggle buttons.
    /// </summary>
    /// <remarks>
    /// Self-contained panel for the Oceanya theme system: it owns the strip's visuals and raises events,
    /// while the host owns the clients themselves and the toggle buttons it hands over. Note the grid is
    /// a <see cref="PageButtonGrid"/> holding CLIENT buttons - the emote grid lives inside
    /// <c>ICMessageSettings</c>. See <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class ClientsListPanel : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClientsListPanel"/> class.
        /// </summary>
        public ClientsListPanel()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the add-client button is pressed.</summary>
        public event EventHandler? AddClientRequested;

        /// <summary>Raised when the remove-client button is pressed.</summary>
        public event EventHandler? RemoveClientRequested;

        /// <summary>
        /// Gets the paged grid holding the client toggle buttons.
        /// </summary>
        public PageButtonGrid ButtonGrid => ClientButtonGrid;

        /// <summary>
        /// Assigns the shared context menu used by the strip's header elements.
        /// </summary>
        /// <param name="menuFactory">Factory invoked once per header element, so each gets its own menu instance.</param>
        public void SetHeaderContextMenuFactory(Func<ContextMenu> menuFactory)
        {
            if (menuFactory == null)
            {
                throw new ArgumentNullException(nameof(menuFactory));
            }

            lblClients.ContextMenu = menuFactory();
            btnAddClient.ContextMenu = menuFactory();
            btnRemoveClient.ContextMenu = menuFactory();
        }

        /// <summary>
        /// Configures the grid paging and scroll behaviour used by the client strip.
        /// </summary>
        /// <param name="rowCount">Rows per page.</param>
        /// <param name="columnCount">Columns per page.</param>
        /// <param name="scrollMode">Scroll direction for the paging controls.</param>
        public void ConfigureGrid(int rowCount, int columnCount, PageButtonGrid.ScrollMode scrollMode)
        {
            ClientButtonGrid.SetScrollMode(scrollMode);
            ClientButtonGrid.SetPageSize(rowCount, columnCount);
        }

        /// <summary>
        /// Applies the navigation button colors of the underlying grid.
        /// </summary>
        /// <param name="background">Navigation button background.</param>
        /// <param name="foreground">Navigation button foreground.</param>
        public void SetGridNavigationColors(Brush background, Brush foreground)
        {
            ClientButtonGrid.SetNavigationButtonColors(background, foreground);
        }

        private void btnAddClient_Click(object sender, RoutedEventArgs e)
        {
            AddClientRequested?.Invoke(this, EventArgs.Empty);
        }

        private void btnRemoveClient_Click(object sender, RoutedEventArgs e)
        {
            RemoveClientRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
