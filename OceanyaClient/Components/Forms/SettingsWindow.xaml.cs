using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Common;
using OceanyaClient.Components.Forms;
using OceanyaClient.Features.Chat;
using OceanyaClient.Features.Ui;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient
{
    public enum SettingsWindowPage
    {
        Audio,
        Client,
        Interface,
        Viewport,
        Logging,
        Ao2Config,
        Callwords
    }

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
        private double originalMusicVolume;
        private double originalSfxVolume;
        private double originalBlipVolume;
        private SettingsWindowPage initialPage;
        private double lastCommittedUiScalePercent = 100;
        private bool lastCommittedUiScaleWasManual;

        public event Action? SettingsSaved;

        /// <summary>
        /// Fired whenever a volume slider changes, so the host can apply the new level in real time.
        /// On cancel the host receives another firing after the originals are restored.
        /// </summary>
        public event Action? VolumeLiveChanged;

        /// <inheritdoc/>
        public override string HeaderText => "SETTINGS";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        public SettingsWindow(SettingsWindowPage initialPage = SettingsWindowPage.Audio)
        {
            this.initialPage = initialPage;
            InitializeComponent();
            Title = "Settings";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            CallwordRulesListBox.ItemsSource = callwordRules;
            AudioRulesListBox.ItemsSource = audioRules;
            ConfigEntriesListBox.ItemsSource = configEntries;
            LoadSettings();
            SelectPage(initialPage);
        }

        public void SelectPage(SettingsWindowPage page)
        {
            initialPage = page;
            switch (page)
            {
                case SettingsWindowPage.Client:
                    ClientPageButton.IsChecked = true;
                    break;
                case SettingsWindowPage.Interface:
                    InterfacePageButton.IsChecked = true;
                    break;
                case SettingsWindowPage.Viewport:
                    ViewportPageButton.IsChecked = true;
                    break;
                case SettingsWindowPage.Logging:
                    LoggingPageButton.IsChecked = true;
                    break;
                case SettingsWindowPage.Ao2Config:
                    ConfigPageButton.IsChecked = true;
                    break;
                case SettingsWindowPage.Callwords:
                    CallwordsPageButton.IsChecked = true;
                    break;
                default:
                    AudioPageButton.IsChecked = true;
                    break;
            }

            PageButton_Checked(this, new RoutedEventArgs());
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
            double unfocusedPercent = 100 - Math.Clamp(Ao2ConfigIniSettings.GetInt(configValues, "suppress_audio", 0), 0, 100);
            MusicVolumeSlider.Value = musicPercent;
            SfxVolumeSlider.Value = sfxPercent;
            BlipVolumeSlider.Value = blipPercent;
            UnfocusedVolumeSlider.Value = unfocusedPercent;

            originalMusicVolume = SaveFile.Data.AudioMusicVolume;
            originalSfxVolume = SaveFile.Data.AudioSfxVolume;
            originalBlipVolume = SaveFile.Data.AudioBlipVolume;

            MusicFadeOutCheckBox.IsChecked = SaveFile.Data.MusicEffectFadeOut;
            MusicFadeInCheckBox.IsChecked = SaveFile.Data.MusicEffectFadeIn;
            MusicSyncPosCheckBox.IsChecked = SaveFile.Data.MusicEffectSyncPos;

            StickyEffectsCheckBox.IsChecked = SaveFile.Data.StickyEffect;
            SwitchPosOnIniSwapCheckBox.IsChecked = SaveFile.Data.SwitchPosOnIniSwap;
            UiScaleAutomaticRadioButton.IsChecked = SaveFile.Data.UiScaleMode == UiScaleMode.Automatic;
            UiScaleManualRadioButton.IsChecked = SaveFile.Data.UiScaleMode == UiScaleMode.Manual;
            UiScaleSlider.Value = Math.Round(UiScaleMath.ClampScale(SaveFile.Data.UiScaleFactor) * 100);
            lastCommittedUiScalePercent = UiScaleSlider.Value;
            lastCommittedUiScaleWasManual = SaveFile.Data.UiScaleMode == UiScaleMode.Manual;
            RefreshUiScaleControls();
            ViewportPreviewCheckBox.IsChecked = SaveFile.Data.GMViewportWindowPreviewPriority;
            ViewportOverlapCheckBox.IsChecked = SaveFile.Data.GMViewportChatboxOverlapsViewport;
            RefreshViewportThemeControls();
            InvertIcLogsCheckBox.IsChecked = SaveFile.Data.InvertICLog;
            AutomaticTextLoggingCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "automatic_logging_enabled", true);
            DemoLoggingCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "demo_logging_enabled", true);
            RefreshLogFolderPathText();

            ConfigPathTextBlock.Text = string.IsNullOrWhiteSpace(Ao2ConfigIniSettings.ConfigPath)
                ? "No config.ini selected."
                : Ao2ConfigIniSettings.ConfigPath;
            ShakeCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "shake", true);
            BlankBlipCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "blank_blip", false);
            TextCrawlTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "text_crawl", 40).ToString(CultureInfo.InvariantCulture);
            BlipRateTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "blip_rate", 2).ToString(CultureInfo.InvariantCulture);
            ChatRateLimitTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "chat_ratelimit", 0).ToString(CultureInfo.InvariantCulture);
            StayTimeTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "stay_time", 200).ToString(CultureInfo.InvariantCulture);
            LogMaximumTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "log_maximum", 200).ToString(CultureInfo.InvariantCulture);

            callwordRules.Clear();
            HashSet<string> seenCallwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CallwordRule rule in LoadConfigIniCallwords())
            {
                callwordRules.Add(rule);
                seenCallwords.Add(BuildCallwordRuleKey(rule));
            }

            foreach (CallwordRule rule in SaveFile.Data.CallwordRules)
            {
                if (seenCallwords.Add(BuildCallwordRuleKey(rule)))
                {
                    callwordRules.Add(Clone(rule));
                }
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
            UnfocusedVolumeValueText.Text = $"{Math.Round(UnfocusedVolumeSlider.Value):0}%";
        }

        private void PageButton_Checked(object sender, RoutedEventArgs e)
        {
            if (AudioPage == null)
            {
                return;
            }

            AudioPage.Visibility = AudioPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ClientPage.Visibility = ClientPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            InterfacePage.Visibility = InterfacePageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewportPage.Visibility = ViewportPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LoggingPage.Visibility = LoggingPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ConfigPage.Visibility = ConfigPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CallwordsPage.Visibility = CallwordsPageButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UiScaleModeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RefreshUiScaleControls();
            CommitUiScaleSelection(force: true);
        }

        /// <summary>
        /// Slider drags only update the readout. Rescaling mid-drag would move the slider out from
        /// under the pointer and make the value jump again, so the scale is applied once the gesture
        /// finishes.
        /// </summary>
        private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RefreshUiScaleControls();
        }

        private void UiScaleSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CommitUiScaleSelection();
        }

        private void UiScaleSlider_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CommitUiScaleSelection();
        }

        private void UiScaleSlider_KeyUp(object sender, KeyEventArgs e)
        {
            CommitUiScaleSelection();
        }

        private void UiScaleValueTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CommitUiScaleFromTextBox();
        }

        private void UiScaleValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitUiScaleFromTextBox();
        }

        /// <summary>
        /// Reads the typed percentage, clamps it into the supported range, and applies it.
        /// </summary>
        private void CommitUiScaleFromTextBox()
        {
            if (suppressControlEvents)
            {
                return;
            }

            int fallbackPercent = (int)Math.Round(lastCommittedUiScalePercent);
            double parsedPercent = TryParseInt(UiScaleValueTextBox.Text, fallbackPercent);
            double clampedPercent = Math.Round(UiScaleMath.ClampScale(parsedPercent / 100d) * 100);

            suppressControlEvents = true;
            try
            {
                UiScaleSlider.Value = clampedPercent;
            }
            finally
            {
                suppressControlEvents = false;
            }

            RefreshUiScaleControls();
            CommitUiScaleSelection();
        }

        /// <summary>
        /// Publishes the selected scale as a live preview, but only when it actually changed, so a
        /// stray mouse-up or arrow key does not rescale every window for nothing.
        /// </summary>
        /// <param name="force">When true, applies even if the percentage did not change (mode switch).</param>
        private void CommitUiScaleSelection(bool force = false)
        {
            if (suppressControlEvents)
            {
                return;
            }

            bool isManual = UiScaleManualRadioButton.IsChecked == true;
            if (!force
                && Math.Abs(UiScaleSlider.Value - lastCommittedUiScalePercent) < 0.01
                && isManual == lastCommittedUiScaleWasManual)
            {
                return;
            }

            lastCommittedUiScalePercent = UiScaleSlider.Value;
            lastCommittedUiScaleWasManual = isManual;
            ApplyUiScaleLivePreview();
        }

        /// <summary>
        /// Refreshes the UI scale value text, slider availability, and the effective-scale hint.
        /// </summary>
        private void RefreshUiScaleControls()
        {
            if (UiScaleSlider == null || UiScaleValueTextBox == null || UiScaleEffectiveTextBlock == null)
            {
                return;
            }

            bool isManual = UiScaleManualRadioButton.IsChecked == true;
            UiScaleSlider.IsEnabled = isManual;
            UiScaleValueTextBox.IsEnabled = isManual;
            string percentText = $"{Math.Round(UiScaleSlider.Value):0}";
            if (!string.Equals(UiScaleValueTextBox.Text, percentText, StringComparison.Ordinal))
            {
                bool previousSuppress = suppressControlEvents;
                suppressControlEvents = true;
                try
                {
                    UiScaleValueTextBox.Text = percentText;
                }
                finally
                {
                    suppressControlEvents = previousSuppress;
                }
            }

            if (isManual)
            {
                UiScaleEffectiveTextBlock.Text = "Manual scale applies to every Oceanya window on every monitor.";
                return;
            }

            double automaticScale = UiScaleManager.ResolveScaleForWindow(HostWindow ?? Window.GetWindow(this));
            UiScaleEffectiveTextBlock.Text =
                $"This monitor resolves to {Math.Round(automaticScale * 100):0}% automatically.";
        }

        /// <summary>
        /// Publishes the currently selected scale settings as an unsaved live preview.
        /// </summary>
        private void ApplyUiScaleLivePreview()
        {
            UiScaleMode mode = UiScaleManualRadioButton.IsChecked == true ? UiScaleMode.Manual : UiScaleMode.Automatic;
            UiScaleManager.SetLivePreview(mode, UiScaleSlider.Value / 100d);
        }

        private void RefreshViewportThemeControls()
        {
            string configuredTheme = AO2ThemeCatalog.GetConfiguredThemeName(configValues);
            IReadOnlyList<string> themes = AO2ThemeCatalog.GetThemes();
            List<string> themeItems = new List<string>(themes);
            if (!themeItems.Any(theme => string.Equals(theme, configuredTheme, StringComparison.OrdinalIgnoreCase)))
            {
                themeItems.Insert(0, configuredTheme);
            }

            ViewportThemeComboBox.ItemsSource = themeItems;
            ViewportThemeComboBox.SelectedItem = themeItems.FirstOrDefault(theme => string.Equals(theme, configuredTheme, StringComparison.OrdinalIgnoreCase))
                ?? themeItems.FirstOrDefault();
            RefreshViewportSubthemeControls(AO2ThemeCatalog.GetConfiguredSubthemeValue(configValues));

            string folders = string.Join(", ", AO2ThemeCatalog.GetAo2ThemeScanFolders());
            ViewportThemePathTextBlock.Text = string.IsNullOrWhiteSpace(folders)
                ? "No AO2 theme folders can be resolved until a config.ini is selected."
                : "Scanned folders: " + folders;
        }

        private void RefreshViewportSubthemeControls(string? preferredValue = null)
        {
            string theme = ViewportThemeComboBox.SelectedItem as string
                ?? AO2ThemeCatalog.GetConfiguredThemeName(configValues);
            IReadOnlyList<AO2SubthemeOption> subthemes = AO2ThemeCatalog.GetSubthemes(theme);
            ViewportSubthemeComboBox.ItemsSource = subthemes;
            string selectedValue = string.IsNullOrWhiteSpace(preferredValue)
                ? AO2ThemeCatalog.ServerSubthemeValue
                : preferredValue.Trim();

            AO2SubthemeOption? selected = subthemes.FirstOrDefault(option =>
                string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
                ?? subthemes.FirstOrDefault();
            ViewportSubthemeComboBox.SelectedItem = selected;
        }

        private void ViewportThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RefreshViewportSubthemeControls(AO2ThemeCatalog.ServerSubthemeValue);
        }

        private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressControlEvents)
            {
                return;
            }

            RefreshValueText();
            SaveFile.Data.AudioMusicVolume = AudioSettings.PercentToScalar(MusicVolumeSlider.Value);
            SaveFile.Data.AudioSfxVolume = AudioSettings.PercentToScalar(SfxVolumeSlider.Value);
            SaveFile.Data.AudioBlipVolume = AudioSettings.PercentToScalar(BlipVolumeSlider.Value);
            // config.ini wins over the savefile, and it is only written on Save, so the unsaved slider
            // values have to be published explicitly for the live preview to be audible.
            AudioSettings.SetLivePreviewVolumes(
                AudioSettings.PercentToScalar(MusicVolumeSlider.Value),
                AudioSettings.PercentToScalar(SfxVolumeSlider.Value),
                AudioSettings.PercentToScalar(BlipVolumeSlider.Value));
            VolumeLiveChanged?.Invoke();
        }

        private void AddCallwordButton_Click(object sender, RoutedEventArgs e)
        {
            CallwordRuleEditorWindow editor = new CallwordRuleEditorWindow(null) { Owner = HostWindow };
            if (editor.ShowDialog() == true && editor.Rule != null)
            {
                callwordRules.Add(editor.Rule);
            }
        }

        private void EditCallwordButton_Click(object sender, RoutedEventArgs e)
        {
            if (CallwordRulesListBox.SelectedItem is not CallwordRule selected)
            {
                return;
            }

            int index = callwordRules.IndexOf(selected);
            CallwordRuleEditorWindow editor = new CallwordRuleEditorWindow(Clone(selected)) { Owner = HostWindow };
            if (editor.ShowDialog() == true && editor.Rule != null && index >= 0)
            {
                callwordRules[index] = editor.Rule;
            }
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
            ExtraAudioRuleEditorWindow editor = new ExtraAudioRuleEditorWindow(null) { Owner = HostWindow };
            if (editor.ShowDialog() == true && editor.Rule != null)
            {
                audioRules.Add(editor.Rule);
            }
        }

        private void EditAudioRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (AudioRulesListBox.SelectedItem is not ExtraAudioRule selected)
            {
                return;
            }

            int index = audioRules.IndexOf(selected);
            ExtraAudioRuleEditorWindow editor = new ExtraAudioRuleEditorWindow(Clone(selected)) { Owner = HostWindow };
            if (editor.ShowDialog() == true && editor.Rule != null && index >= 0)
            {
                audioRules[index] = editor.Rule;
            }
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
            UnfocusedVolumeSlider.Value = 100 - Math.Clamp(Ao2ConfigIniSettings.GetInt(configValues, "suppress_audio", 0), 0, 100);
            ShakeCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "shake", true);
            BlankBlipCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "blank_blip", false);
            TextCrawlTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "text_crawl", 40).ToString(CultureInfo.InvariantCulture);
            BlipRateTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "blip_rate", 2).ToString(CultureInfo.InvariantCulture);
            ChatRateLimitTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "chat_ratelimit", 0).ToString(CultureInfo.InvariantCulture);
            StayTimeTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "stay_time", 200).ToString(CultureInfo.InvariantCulture);
            LogMaximumTextBox.Text = Ao2ConfigIniSettings.GetInt(configValues, "log_maximum", 200).ToString(CultureInfo.InvariantCulture);
            AutomaticTextLoggingCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "automatic_logging_enabled", true);
            DemoLoggingCheckBox.IsChecked = Ao2ConfigIniSettings.GetBool(configValues, "demo_logging_enabled", true);
            RefreshLogFolderPathText();
            RefreshValueText();
        }

        private void RefreshLogFolderPathText()
        {
            string logRoot = Ao2TextLogWriter.ResolveLogRootDirectory();
            LogFolderPathTextBlock.Text = string.IsNullOrWhiteSpace(logRoot)
                ? "No log folder can be resolved until a config.ini is selected."
                : logRoot;
        }

        private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string logRoot = Ao2TextLogWriter.ResolveLogRootDirectory();
            if (string.IsNullOrWhiteSpace(logRoot))
            {
                OceanyaMessageBox.Show("No log folder can be resolved until a config.ini is selected.");
                return;
            }

            try
            {
                Directory.CreateDirectory(logRoot);
                Process.Start(new ProcessStartInfo
                {
                    FileName = logRoot,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Could not open the log folder:\n" + ex.Message);
            }
        }

        private void FindInAllLogsButton_Click(object sender, RoutedEventArgs e)
        {
            string logRoot = Ao2TextLogWriter.ResolveLogRootDirectory();
            if (string.IsNullOrWhiteSpace(logRoot))
            {
                OceanyaMessageBox.Show("No log folder can be resolved until a config.ini is selected.");
                return;
            }

            FindInAllLogsWindow finder = new FindInAllLogsWindow(logRoot) { Owner = HostWindow };
            Window finderWindow = OceanyaWindowManager.CreateWindow(finder);
            finderWindow.Show();
            finderWindow.Activate();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // The saved values below become authoritative, so the preview override must go.
            AudioSettings.ClearLivePreviewVolumes();
            SaveFile.Data.AudioMusicVolume = AudioSettings.PercentToScalar(MusicVolumeSlider.Value);
            SaveFile.Data.AudioSfxVolume = AudioSettings.PercentToScalar(SfxVolumeSlider.Value);
            SaveFile.Data.AudioBlipVolume = AudioSettings.PercentToScalar(BlipVolumeSlider.Value);
            SaveFile.Data.MusicEffectFadeOut = MusicFadeOutCheckBox.IsChecked == true;
            SaveFile.Data.MusicEffectFadeIn = MusicFadeInCheckBox.IsChecked == true;
            SaveFile.Data.MusicEffectSyncPos = MusicSyncPosCheckBox.IsChecked == true;
            SaveFile.Data.StickyEffect = StickyEffectsCheckBox.IsChecked == true;
            SaveFile.Data.SwitchPosOnIniSwap = SwitchPosOnIniSwapCheckBox.IsChecked == true;
            SaveFile.Data.GMViewportWindowPreviewPriority = ViewportPreviewCheckBox.IsChecked == true;
            UiScaleManager.ApplySettings(
                UiScaleManualRadioButton.IsChecked == true ? UiScaleMode.Manual : UiScaleMode.Automatic,
                UiScaleSlider.Value / 100d);
            SaveFile.Data.GMViewportChatboxOverlapsViewport = ViewportOverlapCheckBox.IsChecked == true;
            SaveFile.Data.InvertICLog = InvertIcLogsCheckBox.IsChecked == true;
            SaveFile.Data.CallwordRules = new List<CallwordRule>(callwordRules);
            SaveFile.Data.ExtraAudioRules = new List<ExtraAudioRule>(audioRules);

            foreach (ConfigEntry entry in configEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    configValues[entry.Key] = entry.Value;
                }
            }
            Ao2ConfigIniSettings.SetPercent(configValues, "default_music", MusicVolumeSlider.Value);
            Ao2ConfigIniSettings.SetPercent(configValues, "default_sfx", SfxVolumeSlider.Value);
            Ao2ConfigIniSettings.SetPercent(configValues, "default_blip", BlipVolumeSlider.Value);
            Ao2ConfigIniSettings.SetPercent(configValues, "suppress_audio", 100 - UnfocusedVolumeSlider.Value);
            configValues["shake"] = (ShakeCheckBox.IsChecked == true).ToString().ToLowerInvariant();
            configValues["blank_blip"] = (BlankBlipCheckBox.IsChecked == true).ToString().ToLowerInvariant();
            configValues["text_crawl"] = TryParseInt(TextCrawlTextBox.Text, 40).ToString(CultureInfo.InvariantCulture);
            configValues["blip_rate"] = TryParseInt(BlipRateTextBox.Text, 2).ToString(CultureInfo.InvariantCulture);
            configValues["chat_ratelimit"] = TryParseInt(ChatRateLimitTextBox.Text, 0).ToString(CultureInfo.InvariantCulture);
            configValues["stay_time"] = TryParseInt(StayTimeTextBox.Text, 200).ToString(CultureInfo.InvariantCulture);
            configValues["log_maximum"] = TryParseInt(LogMaximumTextBox.Text, 200).ToString(CultureInfo.InvariantCulture);
            configValues["automatic_logging_enabled"] = (AutomaticTextLoggingCheckBox.IsChecked == true).ToString().ToLowerInvariant();
            configValues["demo_logging_enabled"] = (DemoLoggingCheckBox.IsChecked == true).ToString().ToLowerInvariant();
            if (ViewportThemeComboBox.SelectedItem is string selectedTheme && !string.IsNullOrWhiteSpace(selectedTheme))
            {
                configValues["theme"] = selectedTheme.Trim();
            }

            if (ViewportSubthemeComboBox.SelectedItem is AO2SubthemeOption selectedSubtheme)
            {
                configValues["subtheme"] = selectedSubtheme.Value;
            }

            configValues["callwords"] = string.Join(
                ", ",
                callwordRules
                    .Where(rule => rule.TriggerType == CallwordTriggerType.Ao2Callword)
                    .Select(rule => string.IsNullOrWhiteSpace(rule.Match) ? rule.Word : rule.Match)
                    .Where(word => !string.IsNullOrWhiteSpace(word)));

            Ao2ConfigIniSettings.Save(configValues);
            SaveFile.Save();
            SettingsSaved?.Invoke();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            AudioSettings.ClearLivePreviewVolumes();
            UiScaleManager.ClearLivePreview();
            SaveFile.Data.AudioMusicVolume = originalMusicVolume;
            SaveFile.Data.AudioSfxVolume = originalSfxVolume;
            SaveFile.Data.AudioBlipVolume = originalBlipVolume;
            VolumeLiveChanged?.Invoke();
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
                TriggerType = rule.TriggerType,
                Match = rule.Match,
                CharacterName = rule.CharacterName,
                EmoteName = rule.EmoteName,
                SoundPath = rule.SoundPath,
                VolumePercent = rule.VolumePercent,
                WholeWord = rule.WholeWord,
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
                IsEnabled = rule.IsEnabled,
                IsCaseSensitive = rule.IsCaseSensitive
            };
        }

        private IEnumerable<CallwordRule> LoadConfigIniCallwords()
        {
            if (!configValues.TryGetValue("callwords", out string? raw) || string.IsNullOrWhiteSpace(raw))
            {
                yield break;
            }

            foreach (string word in raw.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = word.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                yield return new CallwordRule
                {
                    Word = trimmed,
                    TriggerType = CallwordTriggerType.Ao2Callword,
                    Match = trimmed,
                    SoundPath = string.Empty,
                    WholeWord = false,
                    IsEnabled = true
                };
            }
        }

        private static string BuildCallwordRuleKey(CallwordRule rule)
        {
            return string.Join(
                "|",
                (int)rule.TriggerType,
                rule.Match ?? string.Empty,
                rule.CharacterName ?? string.Empty,
                rule.EmoteName ?? string.Empty,
                rule.WholeWord.ToString());
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
