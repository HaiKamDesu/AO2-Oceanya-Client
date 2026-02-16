using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for AddServerDialog.xaml
    /// </summary>
    public partial class AddServerDialog : Window
    {
        public string ServerName { get; private set; } = string.Empty;
        public string ServerEndpoint { get; private set; } = string.Empty;

        private bool isClosing;

        public AddServerDialog(
            string windowTitle = "Add Server",
            string actionText = "ADD SERVER",
            string defaultName = "My Server",
            string defaultEndpoint = "ws://192.0.0.1:4455")
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            Title = windowTitle;
            ActionButton.Content = actionText;
            ServerNameTextBox.Text = defaultName;
            ServerEndpointTextBox.Text = defaultEndpoint;

            ServerNameTextBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                ServerNameTextBox.Focus();
                ServerNameTextBox.SelectAll();
            }));
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ServerName = ServerNameTextBox.Text?.Trim() ?? string.Empty;
            ServerEndpoint = ServerEndpointTextBox.Text?.Trim() ?? string.Empty;
            DialogResult = true;
            CloseWithAnimation();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            CloseWithAnimation();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            CloseWithAnimation();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isClosing)
            {
                return;
            }

            e.Cancel = true;
            CloseWithAnimation();
        }

        private void CloseWithAnimation()
        {
            if (isClosing)
            {
                return;
            }

            isClosing = true;
            bool? currentResult = DialogResult;
            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    isClosing = true;
                    DialogResult = currentResult;
                    Close();
                });
            };
            fadeOut.Begin(this);
        }

        public static bool ShowDialog(
            Window owner,
            out string serverName,
            out string serverEndpoint,
            string windowTitle = "Add Server",
            string actionText = "ADD SERVER",
            string defaultName = "My Server",
            string defaultEndpoint = "ws://192.0.0.1:4455")
        {
            AddServerDialog dialog = new AddServerDialog(windowTitle, actionText, defaultName, defaultEndpoint)
            {
                Owner = owner
            };

            bool? result = dialog.ShowDialog();
            if (result == true)
            {
                serverName = dialog.ServerName;
                serverEndpoint = dialog.ServerEndpoint;
                return true;
            }

            serverName = string.Empty;
            serverEndpoint = string.Empty;
            return false;
        }
    }
}
