using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Content control for adding/editing a server endpoint entry.
    /// </summary>
    public partial class AddServerDialog : OceanyaWindowContentControl
    {
        private readonly string headerText;

        /// <summary>
        /// Initializes a new instance of the <see cref="AddServerDialog"/> class.
        /// </summary>
        public AddServerDialog(
            string windowTitle = "Add Server",
            string actionText = "ADD SERVER",
            string defaultName = "My Server",
            string defaultEndpoint = "ws://192.0.0.1:4455")
        {
            InitializeComponent();
            headerText = windowTitle.ToUpperInvariant();
            ActionButton.Content = actionText;
            ServerNameTextBox.Text = defaultName;
            ServerEndpointTextBox.Text = defaultEndpoint;

            Loaded += (_, _) =>
            {
                ServerNameTextBox.Focus();
                ServerNameTextBox.SelectAll();
            };
        }

        /// <summary>
        /// Gets the resulting server name.
        /// </summary>
        public string ServerName { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the resulting server endpoint.
        /// </summary>
        public string ServerEndpoint { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public override string HeaderText => headerText;

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ServerName = ServerNameTextBox.Text?.Trim() ?? string.Empty;
            ServerEndpoint = ServerEndpointTextBox.Text?.Trim() ?? string.Empty;
            RequestHostClose(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            RequestHostClose(false);
        }

        /// <summary>
        /// Shows the add/edit server dialog with the shared generic window host.
        /// </summary>
        public static bool ShowDialog(
            Window owner,
            out string serverName,
            out string serverEndpoint,
            string windowTitle = "Add Server",
            string actionText = "ADD SERVER",
            string defaultName = "My Server",
            string defaultEndpoint = "ws://192.0.0.1:4455")
        {
            AddServerDialog content = new AddServerDialog(windowTitle, actionText, defaultName, defaultEndpoint);
            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = windowTitle,
                HeaderText = windowTitle.ToUpperInvariant(),
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                IsUserResizeEnabled = false,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            bool? result = OceanyaWindowManager.ShowDialog(content, options);
            if (result == true)
            {
                serverName = content.ServerName;
                serverEndpoint = content.ServerEndpoint;
                return true;
            }

            serverName = string.Empty;
            serverEndpoint = string.Empty;
            return false;
        }
    }
}
