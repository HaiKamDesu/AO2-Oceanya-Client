using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for InputDialog.xaml
    /// </summary>
    public partial class InputDialog : Window
    {
        public string UserInput { get; private set; } = string.Empty;

        private bool gotResult = false;
        public InputDialog(string prompt, string title = "Input Required", string defaultText = "")
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            // Set dialog properties
            this.Title = title;
            PromptTextBlock.Text = prompt;
            InputTextBox.Text = defaultText;

            // Focus the textbox when loaded
            InputTextBox.Dispatcher.BeginInvoke(new Action(() => {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            }));
        }

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
            }
            else if (e.Key == Key.Escape)
            {
                CancelDialog();
            }
        }

        private void AcceptInput()
        {
            if (gotResult) return;
            gotResult = true;
            UserInput = InputTextBox.Text;
            DialogResult = true;
            CloseWithAnimation();
        }

        private void CancelDialog()
        {
            UserInput = string.Empty;
            DialogResult = false;
            CloseWithAnimation();
        }

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
            UserInput = string.Empty;
            DialogResult = false;
            CloseWithAnimation();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // The FadeIn animation is triggered automatically by the EventTrigger in XAML
        }

        private bool _isClosing = false;
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

            // Store the current DialogResult value to preserve it
            bool? currentResult = this.DialogResult;

            // Play the fade out animation
            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, _) =>
            {
                // When animation completes, actually close the window
                this.Dispatcher.Invoke(() =>
                {
                    _isClosing = true;

                    // Restore the DialogResult before closing
                    this.DialogResult = currentResult;
                    

                    this.Close();
                });
            };
            fadeOut.Begin(this);
        }

        #endregion

        /// <summary>
        /// Static helper method to show the dialog and get input
        /// </summary>
        public static string Show(string prompt, string title = "Input Required", string defaultText = "")
        {
            InputDialog dialog = new InputDialog(prompt, title, defaultText);
            var result = dialog.ShowDialog();
            if (result == true)
            {
                return dialog.UserInput;
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
