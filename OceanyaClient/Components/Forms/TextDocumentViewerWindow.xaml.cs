using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    public partial class TextDocumentViewerWindow : OceanyaWindowContentControl
    {
        private readonly string headerText;

        public TextDocumentViewerWindow(string title, string content)
        {
            InitializeComponent();
            headerText = string.IsNullOrWhiteSpace(title) ? "TEXT VIEWER" : title.Trim().ToUpperInvariant();
            Title = string.IsNullOrWhiteSpace(title) ? "Text Viewer" : title.Trim();
            ContentTextBox.Text = content ?? string.Empty;
        }

        public override string HeaderText => headerText;

        public override bool IsUserResizeEnabled => true;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            RequestHostClose(true);
        }

        public static void ShowViewer(Window? owner, string title, string content)
        {
            TextDocumentViewerWindow viewer = new TextDocumentViewerWindow(title, content);
            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = title,
                HeaderText = viewer.HeaderText,
                Width = 900,
                Height = 650,
                MinWidth = 520,
                MinHeight = 360,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                IsUserResizeEnabled = true,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            _ = OceanyaWindowManager.ShowDialog(viewer, options);
        }
    }
}
