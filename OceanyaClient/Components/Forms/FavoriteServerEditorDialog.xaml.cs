using System;
using System.Windows;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for FavoriteServerEditorDialog.xaml
    /// </summary>
    public partial class FavoriteServerEditorDialog : Window
    {
        public string ServerName { get; private set; } = string.Empty;
        public string ServerEndpoint { get; private set; } = string.Empty;
        public string ServerDescription { get; private set; } = string.Empty;

        public FavoriteServerEditorDialog(
            string windowTitle,
            string actionText,
            string defaultName,
            string defaultEndpoint,
            string defaultDescription)
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);

            Title = windowTitle;
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
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

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
            FavoriteServerEditorDialog dialog = new FavoriteServerEditorDialog(
                windowTitle,
                actionText,
                defaultName,
                defaultEndpoint,
                defaultDescription)
            {
                Owner = owner
            };

            bool? result = dialog.ShowDialog();
            if (result == true)
            {
                serverName = dialog.ServerName;
                endpoint = dialog.ServerEndpoint;
                description = dialog.ServerDescription;
                return true;
            }

            serverName = string.Empty;
            endpoint = string.Empty;
            description = string.Empty;
            return false;
        }
    }
}
