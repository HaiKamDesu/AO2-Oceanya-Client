using OceanyaClient.Features.Updates;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    public enum UpdateAvailableDialogResult
    {
        None = 0,
        Skip = 1,
        Update = 2
    }

    public partial class UpdateAvailableWindow : OceanyaWindowContentControl
    {
        private UpdateAvailableDialogResult result = UpdateAvailableDialogResult.None;

        public UpdateAvailableWindow()
        {
            InitializeComponent();
        }

        public override string HeaderText => "UPDATE AVAILABLE";

        public override bool IsUserResizeEnabled => true;

        public static UpdateAvailableDialogResult Show(Window? owner, UpdateRelease release)
        {
            UpdateAvailableWindow content = new UpdateAvailableWindow();
            string displayVersion = release.Manifest.Tag;
            bool isTest = string.Equals(release.Manifest.Channel, "test", StringComparison.OrdinalIgnoreCase);
            content.TitleTextBlock.Text = isTest
                ? "Test update to " + displayVersion
                : "Update to " + displayVersion;
            string published = release.PublishedAt.HasValue
                ? release.PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "Unknown publish date";
            content.SubtitleTextBlock.Text = isTest
                ? $"TEST CHANNEL  |  {release.Name}  |  {published}"
                : $"{release.Name}  |  {published}";
            content.ReleaseNotesViewer.Document = ReleaseNotesMarkdownRenderer.BuildDocument(release.Body);

            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = isTest ? "Test Update Available" : "Update Available",
                HeaderText = content.HeaderText,
                Width = 720,
                Height = 520,
                MinWidth = 620,
                MinHeight = 420,
                IsUserResizeEnabled = true,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"))
            };

            _ = OceanyaWindowManager.ShowDialog(content, options);
            return content.result;
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            result = UpdateAvailableDialogResult.Skip;
            RequestHostClose(true);
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            result = UpdateAvailableDialogResult.Update;
            RequestHostClose(true);
        }
    }
}
