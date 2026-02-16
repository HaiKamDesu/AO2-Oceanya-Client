using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for OceanyaMessageBox.xaml
    /// </summary>
    public partial class OceanyaMessageBox : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;
        private bool _isClosing = false;

        private OceanyaMessageBox()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
        }

        /// <summary>
        /// Configure the message box buttons based on MessageBoxButton enum
        /// </summary>
        private void ConfigureButtons(MessageBoxButton buttons)
        {
            // Hide all buttons by default
            OKButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = Visibility.Collapsed;

            // Reset default button states
            OKButton.IsDefault = false;
            YesButton.IsDefault = false;

            // Show appropriate buttons based on the MessageBoxButton enum
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

        /// <summary>
        /// Format the message based on MessageBoxImage enum
        /// </summary>
        private string FormatMessage(string message, MessageBoxImage image)
        {
            switch (image)
            {
                case MessageBoxImage.Error:
                    return "ERROR: " + message;

                case MessageBoxImage.Warning:
                    return "WARNING: " + message;

                case MessageBoxImage.Information:
                    return "INFO: " + message;

                case MessageBoxImage.Question:
                    return "QUESTION: " + message;

                case MessageBoxImage.None:
                default:
                    return message;
            }
        }

        #region Button Click Event Handlers

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.OK;
            CloseWithAnimation();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
            CloseWithAnimation();
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Yes;
            CloseWithAnimation();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.No;
            CloseWithAnimation();
        }

        #endregion

        #region Window Controls and Animations

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Default to Cancel if user clicks the close button
            _result = MessageBoxResult.Cancel;
            CloseWithAnimation();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // The FadeIn animation is triggered automatically by the EventTrigger in XAML
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If we're already in the process of closing with animation, allow the close
            if (_isClosing)
            {
                return;
            }

            // Otherwise, cancel the default closing and animate first
            e.Cancel = true;
            CloseWithAnimation();
        }

        private void CloseWithAnimation()
        {
            if (_isClosing)
                return;

            _isClosing = true;

            // Play the fade out animation
            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, _) =>
            {
                // When animation completes, close the window
                this.Dispatcher.Invoke(() =>
                {
                    _isClosing = true;
                    this.DialogResult = true;
                    this.Close();
                });
            };
            fadeOut.Begin(this);
        }

        #endregion

        #region Static Show Methods

        /// <summary>
        /// Displays a message box with specified text and returns a result.
        /// </summary>
        /// <param name="messageBoxText">The message to display.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(string messageBoxText)
        {
            return Show(messageBoxText, "", MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with specified text and caption and returns a result.
        /// </summary>
        /// <param name="messageBoxText">The message to display.</param>
        /// <param name="caption">The caption for the message box.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with specified text, caption, and buttons and returns a result.
        /// </summary>
        /// <param name="messageBoxText">The message to display.</param>
        /// <param name="caption">The caption for the message box.</param>
        /// <param name="buttons">The buttons to display in the message box.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton buttons)
        {
            return Show(messageBoxText, caption, buttons, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with specified text, caption, buttons, and icon and returns a result.
        /// </summary>
        /// <param name="messageBoxText">The message to display.</param>
        /// <param name="caption">The caption for the message box.</param>
        /// <param name="buttons">The buttons to display in the message box.</param>
        /// <param name="icon">The icon to display in the message box.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return Show(null, messageBoxText, caption, buttons, icon);
        }

        /// <summary>
        /// Displays a message box with the specified text, caption, buttons, icon, and owner window and returns a result.
        /// </summary>
        /// <param name="owner">The window that owns this message box.</param>
        /// <param name="messageBoxText">The message to display.</param>
        /// <param name="caption">The caption for the message box.</param>
        /// <param name="buttons">The buttons to display in the message box.</param>
        /// <param name="icon">The icon to display in the message box.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            // Create and configure the OceanyaMessageBox
            OceanyaMessageBox messageBox = new OceanyaMessageBox();

            // Set title
            messageBox.Title = caption;
            messageBox.TitleTextBlock.Text = caption;

            // Set message text with appropriate formatting based on icon
            messageBox.MessageTextBlock.Text = messageBox.FormatMessage(messageBoxText, icon);

            // Configure buttons
            messageBox.ConfigureButtons(buttons);

            // Set default result based on buttons (used if user closes window via Alt+F4 or task bar)
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    messageBox._result = MessageBoxResult.OK;
                    break;
                case MessageBoxButton.OKCancel:
                case MessageBoxButton.YesNoCancel:
                    messageBox._result = MessageBoxResult.Cancel;
                    break;
                case MessageBoxButton.YesNo:
                    messageBox._result = MessageBoxResult.No;
                    break;
            }

            // Show the dialog and return the result
            messageBox.ShowDialog();
            return messageBox._result;
        }

        /// <summary>
        /// Displays a message box with the specified owner and text and returns a result.
        /// </summary>
        /// <param name="owner">The window that owns this message box.</param>
        /// <param name="messageBoxText">The message to display.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(Window? owner, string messageBoxText)
        {
            return Show(owner, messageBoxText, "", MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with the specified owner, text, and caption and returns a result.
        /// </summary>
        /// <param name="owner">The window that owns this message box.</param>
        /// <param name="messageBoxText">The message to display.</param>
        /// <param name="caption">The caption for the message box.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption)
        {
            return Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        /// <summary>
        /// Displays a message box with the specified owner, text, caption, and buttons and returns a result.
        /// </summary>
        /// <param name="owner">The window that owns this message box.</param>
        /// <param name="messageBoxText">The message to display.</param>
        /// <param name="caption">The caption for the message box.</param>
        /// <param name="buttons">The buttons to display in the message box.</param>
        /// <returns>A MessageBoxResult value that specifies which button was clicked.</returns>
        public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton buttons)
        {
            return Show(owner, messageBoxText, caption, buttons, MessageBoxImage.None);
        }

        #endregion
    }
}
