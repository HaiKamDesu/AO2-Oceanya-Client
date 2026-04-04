using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    public sealed class OceanyanFileHivemindStatusLogEntry
    {
        public DateTime Timestamp { get; set; }

        public string Level { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string ConnectionName { get; set; } = string.Empty;
    }

    public partial class OceanyanFileHivemindStatusLogWindow : OceanyaWindowContentControl
    {
        public OceanyanFileHivemindStatusLogWindow()
        {
            InitializeComponent();
            Title = "The Oceanyan File Hivemind Status Log";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
        }

        public override string HeaderText => "FILE HIVEMIND STATUS LOG";

        public override bool IsUserResizeEnabled => true;

        public void LoadEntries(IEnumerable<OceanyanFileHivemindStatusLogEntry> entries)
        {
            StatusRichTextBox.Document.Blocks.Clear();
            foreach (OceanyanFileHivemindStatusLogEntry entry in entries)
            {
                AppendEntry(entry);
            }
        }

        public void AppendEntry(OceanyanFileHivemindStatusLogEntry entry)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendEntry(entry));
                return;
            }

            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0)
            };
            paragraph.Inlines.Add(new Run($"[{entry.Timestamp:HH:mm:ss}] ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130))
            });
            paragraph.Inlines.Add(new Run("[" + (entry.Level ?? string.Empty).ToUpperInvariant() + "] ")
            {
                Foreground = GetSeverityBrush(entry.Level),
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(entry.ConnectionName))
            {
                paragraph.Inlines.Add(new Run("[" + entry.ConnectionName.Trim() + "] ")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 180, 255)),
                    FontWeight = FontWeights.SemiBold
                });
            }

            paragraph.Inlines.Add(new Run(entry.Message ?? string.Empty)
            {
                Foreground = GetMessageBrush(entry.Level)
            });

            StatusRichTextBox.Document.Blocks.Add(paragraph);
            StatusRichTextBox.ScrollToEnd();
        }

        private static Brush GetSeverityBrush(string? level)
        {
            return (level?.Trim() ?? string.Empty).ToUpperInvariant() switch
            {
                "ACTION" => new SolidColorBrush(Color.FromRgb(91, 192, 255)),
                "SUCCESS" => new SolidColorBrush(Color.FromRgb(118, 224, 141)),
                "WARNING" => new SolidColorBrush(Color.FromRgb(255, 196, 92)),
                "ERROR" => new SolidColorBrush(Color.FromRgb(255, 116, 116)),
                _ => new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
        }

        private static Brush GetMessageBrush(string? level)
        {
            return (level?.Trim() ?? string.Empty).ToUpperInvariant() switch
            {
                "WARNING" => new SolidColorBrush(Color.FromRgb(255, 224, 168)),
                "ERROR" => new SolidColorBrush(Color.FromRgb(255, 198, 198)),
                _ => new SolidColorBrush(Color.FromRgb(231, 231, 231))
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            RequestHostClose(false);
        }
    }
}
