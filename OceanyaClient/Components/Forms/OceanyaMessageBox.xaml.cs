using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Hosted content implementation of the custom Oceanya message box.
    /// </summary>
    public partial class OceanyaMessageBox : OceanyaWindowContentControl
    {
        private MessageBoxResult result = MessageBoxResult.None;
        private string headerText = "MESSAGE";

        private OceanyaMessageBox()
        {
            InitializeComponent();
        }

        /// <inheritdoc/>
        public override string HeaderText => headerText;

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        private void ConfigureButtons(MessageBoxButton buttons)
        {
            OKButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;

            OKButton.IsDefault = false;
            YesButton.IsDefault = false;

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    OKButton.Visibility = Visibility.Visible;
                    OKButton.IsDefault = true;
                    break;
                case MessageBoxButton.OKCancel:
                    OKButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    OKButton.IsDefault = true;
                    break;
                case MessageBoxButton.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    YesButton.IsDefault = true;
                    break;
                case MessageBoxButton.YesNoCancel:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    YesButton.IsDefault = true;
                    break;
            }
        }

        private string FormatMessage(string message, MessageBoxImage image)
        {
            return image switch
            {
                MessageBoxImage.Error => "ERROR: " + message,
                MessageBoxImage.Warning => "WARNING: " + message,
                MessageBoxImage.Information => "INFO: " + message,
                MessageBoxImage.Question => "QUESTION: " + message,
                _ => message
            };
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.OK;
            RequestHostClose(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.Cancel;
            RequestHostClose(true);
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.Yes;
            RequestHostClose(true);
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.No;
            RequestHostClose(true);
        }

        private void CopyMessageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string message = MessageTextBlock.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(message) && !ClipboardUtilities.TrySetText(message))
            {
                _ = MessageBox.Show(
                    "Could not access clipboard right now. Try again in a moment.",
                    "Clipboard Busy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Displays a message box with specified text and returns a result.
        /// </summary>
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with specified text and caption and returns a result.
        /// </summary>
        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with specified text, caption, and buttons and returns a result.
        /// </summary>
        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton buttons)
        {
            return Show(messageBoxText, caption, buttons, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with specified text, caption, buttons, and icon and returns a result.
        /// </summary>
        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return Show(null, messageBoxText, caption, buttons, icon);
        }

        /// <summary>
        /// Displays a message box with owner and returns the selected result.
        /// </summary>
        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            OceanyaMessageBox content = new OceanyaMessageBox();
            content.headerText = string.IsNullOrWhiteSpace(caption) ? "MESSAGE" : caption.ToUpperInvariant();
            content.MessageTextBlock.Text = content.FormatMessage(messageBoxText, icon);
            content.ConfigureButtons(buttons);

            content.result = buttons switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                _ => MessageBoxResult.Cancel
            };

            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = caption,
                HeaderText = content.headerText,
                Width = 400,
                Height = 200,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                IsUserResizeEnabled = false,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            _ = OceanyaWindowManager.ShowDialog(content, options);
            return content.result;
        }

        /// <summary>
        /// Displays a message box with owner and text and returns a result.
        /// </summary>
        public static MessageBoxResult Show(Window? owner, string messageBoxText)
        {
            return Show(owner, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with owner, text, and caption and returns a result.
        /// </summary>
        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption)
        {
            return Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with owner, text, caption, and buttons and returns a result.
        /// </summary>
        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton buttons)
        {
            return Show(owner, messageBoxText, caption, buttons, MessageBoxImage.None);
        }
    }
}
