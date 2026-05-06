using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Client settings window with persisted audio sliders.
    /// </summary>
    public partial class SettingsWindow : OceanyaWindowContentControl
    {
        private bool suppressControlEvents;

        /// <inheritdoc/>
        public override string HeaderText => "SETTINGS";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        public SettingsWindow()
        {
            InitializeComponent();
            Title = "Settings";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            LoadSettings();
        }

        private void LoadSettings()
        {
            suppressControlEvents = true;
            MusicVolumeSlider.Value = AudioSettings.ScalarToPercent(AudioSettings.MusicVolume);
            SfxVolumeSlider.Value = AudioSettings.ScalarToPercent(AudioSettings.SfxVolume);
            BlipVolumeSlider.Value = AudioSettings.ScalarToPercent(AudioSettings.BlipVolume);
            RefreshValueText();
            suppressControlEvents = false;
        }

        private void RefreshValueText()
        {
            MusicVolumeValueText.Text = $"{Math.Round(MusicVolumeSlider.Value):0}%";
            SfxVolumeValueText.Text = $"{Math.Round(SfxVolumeSlider.Value):0}%";
            BlipVolumeValueText.Text = $"{Math.Round(BlipVolumeSlider.Value):0}%";
        }

        private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RefreshValueText();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFile.Data.AudioMusicVolume = AudioSettings.PercentToScalar(MusicVolumeSlider.Value);
            SaveFile.Data.AudioSfxVolume = AudioSettings.PercentToScalar(SfxVolumeSlider.Value);
            SaveFile.Data.AudioBlipVolume = AudioSettings.PercentToScalar(BlipVolumeSlider.Value);
            SaveFile.Save();
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
    }
}
