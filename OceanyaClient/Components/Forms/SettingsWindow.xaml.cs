using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Client settings window for Oceanya savefile settings and selected AO2 config.ini values.
    /// </summary>
    public partial class SettingsWindow : OceanyaWindowContentControl
    {
        private readonly ObservableCollection<CallwordRule> callwordRules = new ObservableCollection<CallwordRule>();
        private readonly ObservableCollection<ExtraAudioRule> audioRules = new ObservableCollection<ExtraAudioRule>();
        private readonly ObservableCollection<ConfigEntry> configEntries = new ObservableCollection<ConfigEntry>();
        private readonly Dictionary<string, string> configValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool suppressControlEvents;

        public event Action? SettingsSaved;

        /// <inheritdoc/>
        public override string HeaderText => "SETTINGS";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        public SettingsWindow()
        {
            InitializeComponent();
            Title = "Settings";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            AudioRuleKindComboBox.ItemsSource = Enum.GetValues(typeof(ExtraAudioRuleKind));
            AudioRuleTargetComboBox.ItemsSource = Enum.GetValues(typeof(ExtraAudioRuleTarget));
            AudioRuleKindComboBox.SelectedItem = ExtraAudioRuleKind.Blip;
            AudioRuleTargetComboBox.SelectedItem = ExtraAudioRuleTarget.Character;
            CallwordRulesListBox.ItemsSource = callwordRules;
            AudioRulesListBox.ItemsSource = audioRules;
            ConfigEntriesListBox.ItemsSource = configEntries;
            LoadSettings();
        }

        private void LoadSettings()
        {
            suppressControlEvents = true;
            configValues.Clear();
            foreach (KeyValuePair<string, string> pair in Ao2ConfigIniSettings.Load())
            {
                configValues[pair.Key] = pair.Value;
            }
            RefreshConfigEntries();

            double musicPercent = GetConfigPercentOrSavefile("default_music", AudioSettings.ScalarToPercent(AudioSettings.MusicVolume));
            double sfxPercent = GetConfigPercentOrSavefile("default_sfx", AudioSettings.ScalarToPercent(AudioSettings.SfxVolume));
            double blipPercent = GetConfigPercentOrSavefile("default_blip", AudioSettings.ScalarToPercent(AudioSettings.BlipVolume));
            MusicVolumeSlider.Value = musicPercent;
            SfxVolumeSlider.Value = sfxPercent;
            BlipVolumeSlider.Value = blipPercent;

            StickyEffectsCheckBox.IsChecked = SaveFile.Data.StickyEffect;
            SwitchPosOnIniSwapCheckBox.IsChecked = SaveFile.Data.SwitchPosOnIniSwap;
            InvertIcLogsCheckBox.IsChecked = SaveFile.Data.InvertICLog;
            OpenViewportOnStartupCheckBox.IsChecked = SaveFile.Data.GMViewportWindowState?.IsVisible == true;

            ConfigPathTextBlock.Text = string.IsNullOrWhiteSpace(Ao2ConfigIniSettings.ConfigPath)
                ? "No config.ini selected."
                : Ao2ConfigIniSettings.ConfigPath;
            ShakeCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "shake", true);
            BlankBlipCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "blank_blip", false);
            TextCrawlTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "text_crawl", 40).ToString(CultureInfo.InvariantCulture);
            BlipRateTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "blip_rate", 2).ToString(CultureInfo.InvariantCulture);
            ChatRateLimitTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "chat_ratelimit", 0).ToString(CultureInfo.InvariantCulture);
            StayTimeTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "stay_time", 200).ToString(CultureInfo.InvariantCulture);
            LogMaximumTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "log_maximum", 0).ToString(CultureInfo.InvariantCulture);

            callwordRules.Clear();
            foreach (CallwordRule rule in SaveFile.Data.CallwordRules)
            {
                callwordRules.Add(Clone(rule));
            }

            audioRules.Clear();
            foreach (ExtraAudioRule rule in SaveFile.Data.ExtraAudioRules)
            {
                audioRules.Add(Clone(rule));
            }

            RefreshValueText();
            suppressControlEvents = false;
        }

        private double GetConfigPercentOrSavefile(string key, double fallbackPercent)
        {
            return Math.Clamp(Ao2ConfigIniSettings.GetInt(configValues, key, (int)Math.Round(fallbackPercent)), 0, 100);
        }

        private void RefreshValueText()
        {
            MusicVolumeValueText.Text = $"{Math.Round(MusicVolumeSlider.Value):0}%";
            SfxVolumeValueText.Text = $"{Math.Round(SfxVolumeSlider.Value):0}%";
            BlipVolumeValueText.Text = $"{Math.Round(BlipVolumeSlider.Value):0}%";
        }

        private void PageButton_Checked(object sender, RoutedEventArgs e)
        {
            if (AudioPage == null)
            {
                return;
            }

            AudioPage.Visibility = AudioPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ClientPage.Visibility = ClientPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ConfigPage.Visibility = ConfigPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CallwordsPage.Visibility = CallwordsPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RefreshValueText();
        }

        private void AddCallwordButton_Click(object sender, RoutedEventArgs e)
        {
            string word = CallwordTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            callwordRules.Add(new CallwordRule
            {
                Word = word,
                SoundPath = CallwordSoundPathTextBox.Text?.Trim() ?? string.Empty,
                IsEnabled = true
            });
            CallwordTextBox.Text = string.Empty;
            CallwordSoundPathTextBox.Text = string.Empty;
        }

        private void RemoveCallwordButton_Click(object sender, RoutedEventArgs e)
        {
            if (CallwordRulesListBox.SelectedItem is CallwordRule selected)
            {
                callwordRules.Remove(selected);
            }
        }

        private void AddAudioRuleButton_Click(object sender, RoutedEventArgs e)
        {
            ExtraAudioRuleKind kind = AudioRuleKindComboBox.SelectedItem is ExtraAudioRuleKind selectedKind
                ? selectedKind
                : ExtraAudioRuleKind.Blip;
            ExtraAudioRuleTarget target = AudioRuleTargetComboBox.SelectedItem is ExtraAudioRuleTarget selectedTarget
                ? selectedTarget
                : ExtraAudioRuleTarget.Character;
            int volumePercent = TryParseInt(AudioRuleVolumeTextBox.Text, 100);
            string match = AudioRuleMatchTextBox.Text?.Trim() ?? string.Empty;
            if (target != ExtraAudioRuleTarget.Any && string.IsNullOrWhiteSpace(match))
            {
                return;
            }

            audioRules.Add(new ExtraAudioRule
            {
                Name = string.IsNullOrWhiteSpace(AudioRuleNameTextBox.Text) ? "Audio rule" : AudioRuleNameTextBox.Text.Trim(),
                Kind = kind,
                Target = target,
                Match = match,
                VolumePercent = Math.Clamp(volumePercent, 0, 200),
                IsEnabled = true
            });
        }

        private void RemoveAudioRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (AudioRulesListBox.SelectedItem is ExtraAudioRule selected)
            {
                audioRules.Remove(selected);
            }
        }

        private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (KeyValuePair<string, string> pair in Ao2ConfigIniSettings.Defaults)
            {
                configValues[pair.Key] = pair.Value;
            }

            LoadConfigControlsFromDictionary();
            RefreshConfigEntries();
        }

        private void ConfigEntriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConfigEntriesListBox.SelectedItem is not ConfigEntry selected)
            {
                return;
            }

            ConfigKeyTextBox.Text = selected.Key;
            ConfigValueTextBox.Text = selected.Value;
        }

        private void SetConfigEntryButton_Click(object sender, RoutedEventArgs e)
        {
            string key = ConfigKeyTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            configValues[key] = ConfigValueTextBox.Text?.Trim() ?? string.Empty;
            RefreshConfigEntries();
        }

        private void LoadConfigControlsFromDictionary()
        {
            MusicVolumeSlider.Value = Ao2ConfigIniSettings.GetInt(configValues, "default_music", 50);
            SfxVolumeSlider.Value = Ao2ConfigIniSettings.GetInt(configValues, "default_sfx", 100);
            BlipVolumeSlider.Value = Ao2ConfigIniSettings.GetInt(configValues, "default_blip", 50);
            ShakeCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "shake", true);
            BlankBlipCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "blank_blip", false);
            TextCrawlTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "text_crawl", 40).ToString(CultureInfo.InvariantCulture);
            BlipRateTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "blip_rate", 2).ToString(CultureInfo.InvariantCulture);
            ChatRateLimitTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "chat_ratelimit", 0).ToString(CultureInfo.InvariantCulture);
            StayTimeTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "stay_time", 200).ToString(CultureInfo.InvariantCulture);
            LogMaximumTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "log_maximum", 0).ToString(CultureInfo.InvariantCulture);
            RefreshValueText();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFile.Data.AudioMusicVolume = AudioSettings.PercentToScalar(MusicVolumeSlider.Value);
            SaveFile.Data.AudioSfxVolume = AudioSettings.PercentToScalar(SfxVolumeSlider.Value);
            SaveFile.Data.AudioBlipVolume = AudioSettings.PercentToScalar(BlipVolumeSlider.Value);
            SaveFile.Data.StickyEffect = StickyEffectsCheckBox.IsChecked == true;
            SaveFile.Data.SwitchPosOnIniSwap = SwitchPosOnIniSwapCheckBox.IsChecked == true;
            SaveFile.Data.InvertICLog = InvertIcLogsCheckBox.IsChecked == true;
            SaveFile.Data.GMViewportWindowState ??= new ViewportWindowState();
            SaveFile.Data.GMViewportWindowState.IsVisible = OpenViewportOnStartupCheckBox.IsChecked == true;
            SaveFile.Data.CallwordRules = new List<CallwordRule>(callwordRules);
            SaveFile.Data.ExtraAudioRules = new List<ExtraAudioRule>(audioRules);

            Ao2ConfigIniSettings.SetPercent(configValues, "default_music", MusicVolumeSlider.Value);
            Ao2ConfigIniSettings.SetPercent(configValues, "default_sfx", SfxVolumeSlider.Value);
            Ao2ConfigIniSettings.SetPercent(configValues, "default_blip", BlipVolumeSlider.Value);
            configValues["shake"] = (ShakeCheckBox.IsChecked == true).ToString().ToLowerInvariant();
            configValues["blank_blip"] = (BlankBlipCheckBox.IsChecked == true).ToString().ToLowerInvariant();
            configValues["text_crawl"] = TryParseInt(TextCrawlTextBox.Text, 40).ToString(CultureInfo.InvariantCulture);
            configValues["blip_rate"] = TryParseInt(BlipRateTextBox.Text, 2).ToString(CultureInfo.InvariantCulture);
            configValues["chat_ratelimit"] = TryParseInt(ChatRateLimitTextBox.Text, 0).ToString(CultureInfo.InvariantCulture);
            configValues["stay_time"] = TryParseInt(StayTimeTextBox.Text, 200).ToString(CultureInfo.InvariantCulture);
            configValues["log_maximum"] = TryParseInt(LogMaximumTextBox.Text, 0).ToString(CultureInfo.InvariantCulture);
            foreach (ConfigEntry entry in configEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    configValues[entry.Key] = entry.Value;
                }
            }

            Ao2ConfigIniSettings.Save(configValues);
            SaveFile.Save();
            SettingsSaved?.Invoke();
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

        private static int TryParseInt(string? value, int fallback)
        {
            return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
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

        private static ExtraAudioRule Clone(ExtraAudioRule rule)
        {
            return new ExtraAudioRule
            {
                Name = rule.Name,
                Kind = rule.Kind,
                Target = rule.Target,
                Match = rule.Match,
                VolumePercent = rule.VolumePercent,
                IsEnabled = rule.IsEnabled
            };
        }

        private void RefreshConfigEntries()
        {
            string selectedKey = (ConfigEntriesListBox.SelectedItem as ConfigEntry)?.Key ?? ConfigKeyTextBox?.Text ?? string.Empty;
            configEntries.Clear();
            foreach (KeyValuePair<string, string> pair in configValues.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                ConfigEntry entry = new ConfigEntry(pair.Key, pair.Value);
                configEntries.Add(entry);
                if (string.Equals(pair.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigEntriesListBox.SelectedItem = entry;
                }
            }
        }

        private sealed class ConfigEntry
        {
            public ConfigEntry(string key, string value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; }

            public string Value { get; }

            public string DisplayText => $"{Key}={Value}";
        }
    }
}
