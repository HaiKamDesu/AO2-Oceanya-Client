using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

namespace OceanyaClient
{
    public partial class DreddOverlayEntryDialog : Window
    {
        public string OverlayName { get; private set; } = string.Empty;
        public string OverlayPath { get; private set; } = string.Empty;

        public DreddOverlayEntryDialog(string name = "", string path = "")
        {
            InitializeComponent();
            NameTextBox.Text = name;
            PathTextBox.Text = path;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.apng|All files|*.*",
                Title = "Select overlay file"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                PathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NameTextBox.Text?.Trim() ?? string.Empty;
            string path = PathTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                OceanyaMessageBox.Show("Overlay name cannot be empty.", "Invalid Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                OceanyaMessageBox.Show("Overlay path cannot be empty.", "Invalid Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OverlayName = name;
            OverlayPath = path;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TitleCloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
