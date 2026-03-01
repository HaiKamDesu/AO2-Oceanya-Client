using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Content control for single-line input prompts.
    /// </summary>
    public partial class InputDialog : OceanyaWindowContentControl
    {
        private readonly string headerText;
        private bool gotResult;

        /// <summary>
        /// Initializes a new instance of the <see cref="InputDialog"/> class.
        /// </summary>
        public InputDialog(string prompt, string title = "Input Required", string defaultText = "")
        {
            InitializeComponent();
            headerText = title.ToUpperInvariant();
            PromptTextBlock.Text = prompt;
            InputTextBox.Text = defaultText;

            Loaded += (_, _) =>
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        /// <summary>
        /// Gets the resulting text entered by the user.
        /// </summary>
        public string UserInput { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public override string HeaderText => headerText;

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => false;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            AcceptInput();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDialog();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AcceptInput();
                return;
            }

            if (e.Key == Key.Escape)
            {
                CancelDialog();
            }
        }

        private void AcceptInput()
        {
            if (gotResult)
            {
                return;
            }

            gotResult = true;
            UserInput = InputTextBox.Text ?? string.Empty;
            RequestHostClose(true);
        }

        private void CancelDialog()
        {
            if (gotResult)
            {
                return;
            }

            gotResult = true;
            UserInput = string.Empty;
            RequestHostClose(false);
        }

        private void CopyPromptMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string message = PromptTextBlock.Text ?? string.Empty;
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
        /// Shows the input dialog and returns the entered text.
        /// </summary>
        public static string Show(string prompt, string title = "Input Required", string defaultText = "")
        {
            InputDialog content = new InputDialog(prompt, title, defaultText);
            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Title = title,
                HeaderText = title.ToUpperInvariant(),
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                IsUserResizeEnabled = false,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            bool? result = OceanyaWindowManager.ShowDialog(content, options);
            return result == true ? content.UserInput : string.Empty;
        }
    }
}
