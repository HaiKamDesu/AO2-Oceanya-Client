using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Content control used to create or edit a favorite server entry.
    /// </summary>
    public partial class FavoriteServerEditorDialog : OceanyaWindowContentControl
    {
        private readonly string headerText;

        /// <summary>
        /// Initializes a new instance of the <see cref="FavoriteServerEditorDialog"/> class.
        /// </summary>
        public FavoriteServerEditorDialog(
            string windowTitle,
            string actionText,
            string defaultName,
            string defaultEndpoint,
            string defaultDescription)
        {
            InitializeComponent();

            headerText = windowTitle.ToUpperInvariant();
            ActionButton.Content = actionText;
            ServerNameTextBox.Text = defaultName;
            ServerEndpointTextBox.Text = defaultEndpoint;
            ServerDescriptionTextBox.Text = defaultDescription;

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

        /// <summary>
        /// Gets the resulting server description.
        /// </summary>
        public string ServerDescription { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public override string HeaderText => headerText;

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string serverName = ServerNameTextBox.Text?.Trim() ?? string.Empty;
            string serverEndpoint = ServerEndpointTextBox.Text?.Trim() ?? string.Empty;
            string serverDescription = ServerDescriptionTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(serverName))
            {
                OceanyaMessageBox.Show("Please provide a server name.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(serverEndpoint))
            {
                OceanyaMessageBox.Show("Please provide a server endpoint.", "Invalid Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ServerName = serverName;
            ServerEndpoint = serverEndpoint;
            ServerDescription = serverDescription;
            RequestHostClose(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            RequestHostClose(false);
        }

        /// <summary>
        /// Shows the favorite server editor with the shared generic window host.
        /// </summary>
        public static bool ShowDialog(
            Window owner,
            out string serverName,
            out string endpoint,
            out string description,
            string windowTitle,
            string actionText,
            string defaultName = "My Favorite",
            string defaultEndpoint = "ws://127.0.0.1:27016",
            string defaultDescription = "")
        {
            FavoriteServerEditorDialog content = new FavoriteServerEditorDialog(
                windowTitle,
                actionText,
                defaultName,
                defaultEndpoint,
                defaultDescription);

            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = windowTitle,
                HeaderText = windowTitle.ToUpperInvariant(),
                Width = 540,
                Height = 360,
                MinWidth = 500,
                MinHeight = 330,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                IsUserResizeEnabled = false,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            bool? result = OceanyaWindowManager.ShowDialog(content, options);
            if (result == true)
            {
                serverName = content.ServerName;
                endpoint = content.ServerEndpoint;
                description = content.ServerDescription;
                return true;
            }

            serverName = string.Empty;
            endpoint = string.Empty;
            description = string.Empty;
            return false;
        }
    }
}
