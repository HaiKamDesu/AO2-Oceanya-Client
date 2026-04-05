using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Content control for multiline text input prompts.
    /// </summary>
    public partial class MultilineInputDialog : OceanyaWindowContentControl
    {
        private readonly string headerText;
        private bool gotResult;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultilineInputDialog"/> class.
        /// </summary>
        public MultilineInputDialog(string prompt, string title = "Input Required", string defaultText = "")
        {
            InitializeComponent();
            headerText = title.ToUpperInvariant();
            PromptTextBlock.Text = prompt;
            InputTextBox.Text = defaultText;

            Loaded += (_, _) =>
            {
                InputTextBox.Focus();
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
            };
        }

        /// <summary>
        /// Gets the resulting text entered by the user, or <c>null</c> if cancelled.
        /// </summary>
        public string? UserInput { get; private set; }

        /// <inheritdoc/>
        public override string HeaderText => headerText;

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            AcceptInput();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDialog();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Clear();
            InputTextBox.Focus();
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
            UserInput = null;
            RequestHostClose(false);
        }

        /// <summary>
        /// Shows the multiline input dialog and returns the entered text, or <c>null</c> if cancelled.
        /// </summary>
        public static string? Show(Window? owner, string prompt, string title = "Input Required", string defaultText = "")
        {
            MultilineInputDialog content = new MultilineInputDialog(prompt, title, defaultText);
            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = title,
                HeaderText = title.ToUpperInvariant(),
                Width = 560,
                Height = 420,
                MinWidth = 400,
                MinHeight = 280,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                IsUserResizeEnabled = true,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            bool? result = OceanyaWindowManager.ShowDialog(content, options);
            return result == true ? content.UserInput : null;
        }
    }
}
