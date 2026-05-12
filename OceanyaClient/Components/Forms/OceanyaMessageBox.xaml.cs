using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Defines a custom button shown by <see cref="OceanyaMessageBox"/>.
    /// </summary>
    public sealed class OceanyaMessageBoxButtonOption
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OceanyaMessageBoxButtonOption"/> class.
        /// </summary>
        public OceanyaMessageBoxButtonOption(
            string text,
            MessageBoxResult result,
            bool isDefault = false,
            bool isCancel = false)
        {
            Text = text;
            Result = result;
            IsDefault = isDefault;
            IsCancel = isCancel;
        }

        /// <summary>
        /// Gets the visible button text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the message-box result returned when the button is pressed.
        /// </summary>
        public MessageBoxResult Result { get; }

        /// <summary>
        /// Gets a value indicating whether this button is the default action.
        /// </summary>
        public bool IsDefault { get; }

        /// <summary>
        /// Gets a value indicating whether this button is the cancel action.
        /// </summary>
        public bool IsCancel { get; }
    }

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
            MarkAutomationReady();
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

        private void ConfigureCustomButtons(IReadOnlyList<OceanyaMessageBoxButtonOption> buttons)
        {
            ButtonsPanel.Children.Clear();

            foreach (OceanyaMessageBoxButtonOption option in buttons)
            {
                Button button = new Button
                {
                    Content = option.Text,
                    Style = (Style)FindResource("ModernButton"),
                    IsDefault = option.IsDefault,
                    IsCancel = option.IsCancel,
                    MinWidth = 100
                };
                AutomationProperties.SetAutomationId(button, "MessageBox.Custom." + SanitizeAutomationId(option.Text));
                button.Click += (_, _) =>
                {
                    result = option.Result;
                    RequestHostClose(true);
                };
                ButtonsPanel.Children.Add(button);
            }
        }

        private static string SanitizeAutomationId(string value)
        {
            string normalized = new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray());
            return normalized.Length == 0 ? "Button" : normalized;
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
            return Show(ResolveDefaultOwner(), messageBoxText, caption, buttons, icon);
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
        /// Displays a message box with custom buttons and returns the selected result.
        /// </summary>
        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            IEnumerable<OceanyaMessageBoxButtonOption> buttons,
            MessageBoxImage icon = MessageBoxImage.None)
        {
            return Show(ResolveDefaultOwner(), messageBoxText, caption, buttons, icon);
        }

        /// <summary>
        /// Displays a message box with owner and custom buttons and returns the selected result.
        /// </summary>
        public static MessageBoxResult Show(
            Window? owner,
            string messageBoxText,
            string caption,
            IEnumerable<OceanyaMessageBoxButtonOption> buttons,
            MessageBoxImage icon = MessageBoxImage.None)
        {
            List<OceanyaMessageBoxButtonOption> buttonList = buttons?.ToList()
                ?? new List<OceanyaMessageBoxButtonOption>();
            if (buttonList.Count == 0)
            {
                buttonList.Add(new OceanyaMessageBoxButtonOption("OK", MessageBoxResult.OK, isDefault: true));
            }

            OceanyaMessageBox content = new OceanyaMessageBox();
            content.headerText = string.IsNullOrWhiteSpace(caption) ? "MESSAGE" : caption.ToUpperInvariant();
            content.MessageTextBlock.Text = content.FormatMessage(messageBoxText, icon);
            content.result = buttonList.FirstOrDefault(button => button.IsCancel)?.Result
                ?? buttonList.FirstOrDefault(button => button.IsDefault)?.Result
                ?? buttonList[0].Result;
            content.ConfigureCustomButtons(buttonList);

            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = caption,
                HeaderText = content.headerText,
                Width = 440,
                Height = 210,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                IsUserResizeEnabled = false,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            _ = OceanyaWindowManager.ShowDialog(content, options);
            return content.result;
        }

        private static Window? ResolveDefaultOwner()
        {
            if (Application.Current == null)
            {
                return null;
            }

            Window? activeWindow = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive && window.IsVisible);
            if (activeWindow != null)
            {
                return activeWindow;
            }

            Window? mainWindow = Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsVisible)
            {
                return mainWindow;
            }

            return Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible);
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
