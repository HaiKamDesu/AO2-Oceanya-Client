using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Common;
using Microsoft.Win32;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient
{
    /// <summary>
    /// Modal editor for one callword notification rule.
    /// </summary>
    public partial class CallwordRuleEditorWindow : OceanyaWindowContentControl
    {
        private readonly AO2BlipPreviewPlayer previewPlayer = new AO2BlipPreviewPlayer();

        public CallwordRuleEditorWindow(CallwordRule? source)
        {
            InitializeComponent();
            Title = "Callword Rule";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Closed += (_, _) => previewPlayer.Dispose();
            Rule = source == null ? null : Clone(source);
            if (Rule != null)
            {
                CallwordTextBox.Text = Rule.Word;
                SoundPathTextBox.Text = Rule.SoundPath;
            }
        }

        public override string HeaderText => "CALLWORD RULE";

        public CallwordRule? Rule { get; private set; }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select callword SFX",
                Filter = "AO2-compatible audio (*.opus;*.ogg;*.mp3;*.wav)|*.opus;*.ogg;*.mp3;*.wav|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                SoundPathTextBox.Text = dialog.FileName;
                StopPreview();
            }
        }

        private void UseDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            SoundPathTextBox.Text = string.Empty;
            StopPreview();
        }

        private void PreviewButton_PlayRequested(object sender, EventArgs e)
        {
            string? path = ResolvePreviewPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                PreviewButton.IsPlaying = false;
                return;
            }

            previewPlayer.Stop();
            if (!previewPlayer.TrySetBlip(path))
            {
                PreviewButton.IsPlaying = false;
                return;
            }

            PreviewButton.DurationMs = Math.Max(160, previewPlayer.GetLoadedDurationMs());
            PreviewButton.IsPlaying = true;
            _ = previewPlayer.PlayBlip();
        }

        private void PreviewButton_StopRequested(object sender, EventArgs e)
        {
            StopPreview();
        }

        private void PreviewButton_PlaybackCompleted(object sender, EventArgs e)
        {
            StopPreview();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string word = CallwordTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            Rule = new CallwordRule
            {
                Word = word,
                SoundPath = SoundPathTextBox.Text?.Trim() ?? string.Empty,
                IsEnabled = true
            };
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

        private string? ResolvePreviewPath()
        {
            string customPath = SoundPathTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                return customPath;
            }

            return AO2ViewportAudioResolver.ResolveSfxPath("word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("sfx-word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("modcall");
        }

        private void StopPreview()
        {
            previewPlayer.Stop();
            PreviewButton.IsPlaying = false;
        }

        private static CallwordRule Clone(CallwordRule rule)
        {
            return new CallwordRule
            {
                Word = rule.Word,
                SoundPath = rule.SoundPath,
                IsEnabled = rule.IsEnabled
            };
        }
    }
}
