using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AOBot_Testing.Structures;
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
        private readonly List<CharacterSelectorOption> characters;
        private readonly IReadOnlyList<TriggerTypeOption> triggerTypeOptions;
        private bool suppressRefresh;
        private int currentVolumePercent = 100;
        private bool suppressVolumeTextEvents;

        public CallwordRuleEditorWindow(CallwordRule? source)
        {
            InitializeComponent();
            Title = "Callword Rule";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Closed += (_, _) => previewPlayer.Dispose();
            characters = BuildCharacterOptions();
            triggerTypeOptions = BuildTriggerTypeOptions();
            PopulateCharacterComboBox();
            TriggerTypeDropdown.ItemsSource = triggerTypeOptions.Select(option => option.DisplayName).ToList();
            Rule = source == null ? null : Clone(source);
            LoadSourceRule(Rule);
        }

        public override string HeaderText => "CALLWORD RULE";

        public CallwordRule? Rule { get; private set; }

        private void LoadSourceRule(CallwordRule? rule)
        {
            suppressRefresh = true;
            CallwordTriggerType triggerType = rule?.TriggerType ?? CallwordTriggerType.Ao2Callword;
            TriggerTypeDropdown.Text = triggerTypeOptions
                .First(option => option.TriggerType == triggerType)
                .DisplayName;
            TextValueTextBox.Text = string.IsNullOrWhiteSpace(rule?.Match) ? rule?.Word ?? string.Empty : rule.Match;
            WholeWordCheckBox.IsChecked = rule?.WholeWord == true;
            CharacterComboBox.SelectedText = rule?.CharacterName ?? characters.FirstOrDefault()?.Name ?? string.Empty;
            PopulateEmoteComboBox(CharacterComboBox.SelectedText);
            EmoteComboBox.SelectedText = rule?.EmoteName ?? string.Empty;
            SoundPathTextBox.Text = string.IsNullOrWhiteSpace(rule?.SoundPath)
                ? ResolveDefaultNotificationPath() ?? string.Empty
                : rule.SoundPath;
            SetVolumePercent(rule?.VolumePercent ?? 100, updateSlider: true, updateText: true);
            suppressRefresh = false;
            RefreshEditorForTriggerType();
        }

        private void TriggerTypeDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            if (!suppressRefresh)
            {
                RefreshEditorForTriggerType();
            }
        }

        private void CharacterComboBox_OnConfirm(object? sender, string value)
        {
            CharacterComboBox.SelectedText = value?.Trim() ?? string.Empty;
            PopulateEmoteComboBox(CharacterComboBox.SelectedText);
        }

        private void EmoteComboBox_OnConfirm(object? sender, string value)
        {
            EmoteComboBox.SelectedText = value?.Trim() ?? string.Empty;
        }

        private void CharacterLookupButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterSelectorOption? selected = null;
            CharacterFolderVisualizerWindow selector = new CharacterFolderVisualizerWindow(
                null,
                suppressInitialLoadWaitForm: false,
                characterSelectionMode: true,
                selectCharacterForDialog: item =>
                {
                    selected = characters.FirstOrDefault(option =>
                        string.Equals(option.DirectoryPath, item.DirectoryPath, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(option.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                })
            {
                Owner = HostWindow,
                WindowState = WindowState.Maximized
            };
            if (selector.ShowDialog() == true && selected != null)
            {
                CharacterComboBox.SelectedText = selected.Name;
                PopulateEmoteComboBox(selected.Name);
            }
        }

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
            SoundPathTextBox.Text = ResolveDefaultNotificationPath() ?? string.Empty;
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

            previewPlayer.Volume = (float)(AudioSettings.SfxVolume * currentVolumePercent / 100.0);
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
            CallwordTriggerType triggerType = GetSelectedTriggerType();
            string match = ResolveTextMatch(triggerType);
            string characterName = CharacterComboBox.SelectedText?.Trim() ?? string.Empty;
            string emoteName = EmoteComboBox.SelectedText?.Trim() ?? string.Empty;
            if (!IsValid(triggerType, match, characterName, emoteName))
            {
                return;
            }

            Rule = new CallwordRule
            {
                TriggerType = triggerType,
                Word = triggerType == CallwordTriggerType.Ao2Callword ? match : string.Empty,
                Match = match,
                CharacterName = UsesCharacter(triggerType) ? characterName : string.Empty,
                EmoteName = triggerType == CallwordTriggerType.CharacterEmoteUsed ? emoteName : string.Empty,
                SoundPath = UsesCustomSound(triggerType) ? SoundPathTextBox.Text?.Trim() ?? string.Empty : string.Empty,
                VolumePercent = UsesCustomSound(triggerType) ? currentVolumePercent : 100,
                WholeWord = SupportsWholeWord(triggerType) && WholeWordCheckBox.IsChecked == true,
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

        private void RefreshEditorForTriggerType()
        {
            CallwordTriggerType triggerType = GetSelectedTriggerType();
            bool usesText = UsesTextMatch(triggerType);
            bool usesCharacter = UsesCharacter(triggerType);
            bool usesEmote = triggerType == CallwordTriggerType.CharacterEmoteUsed;
            bool supportsWholeWord = SupportsWholeWord(triggerType);
            TextValueLabelRow.Visibility = usesText ? Visibility.Visible : Visibility.Collapsed;
            TextValueTextBox.Visibility = usesText ? Visibility.Visible : Visibility.Collapsed;
            WholeWordCheckBox.Visibility = supportsWholeWord ? Visibility.Visible : Visibility.Collapsed;
            CharacterLabelRow.Visibility = usesCharacter ? Visibility.Visible : Visibility.Collapsed;
            CharacterRow.Visibility = usesCharacter ? Visibility.Visible : Visibility.Collapsed;
            EmoteLabelRow.Visibility = usesEmote ? Visibility.Visible : Visibility.Collapsed;
            EmoteComboBox.Visibility = usesEmote ? Visibility.Visible : Visibility.Collapsed;
            SoundSection.Visibility = UsesCustomSound(triggerType) ? Visibility.Visible : Visibility.Collapsed;

            TextValueLabel.Text = triggerType switch
            {
                CallwordTriggerType.Ao2Callword => "Callword",
                CallwordTriggerType.MessageStartsWith => "Message starts with",
                CallwordTriggerType.PlayerShownameSpeaks => "Showname",
                _ => "Message contains"
            };
            TextValueHelpGlyph.ToolTip = triggerType switch
            {
                CallwordTriggerType.Ao2Callword => "AO2-compatible callword text. Saved into config.ini callwords for AO2 and Oceanya.",
                CallwordTriggerType.MessageStartsWith => "Incoming message text must start with this value. Case-insensitive.",
                CallwordTriggerType.PlayerShownameSpeaks => "Incoming IC/OOC showname must match this value. Case-insensitive.",
                _ => "Incoming message text must contain this value. Case-insensitive."
            };
        }

        private void PopulateCharacterComboBox()
        {
            CharacterComboBox.Clear();
            foreach (CharacterSelectorOption option in characters)
            {
                CharacterComboBox.Add(option.Name, option.IconPath, option.Name);
            }
        }

        private void PopulateEmoteComboBox(string? characterName)
        {
            EmoteComboBox.Clear();
            CharacterFolder? character = CharacterFolder.FullList.FirstOrDefault(folder =>
                string.Equals(folder.Name, characterName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (character == null)
            {
                return;
            }

            foreach (Emote emote in character.configINI.Emotions.Values.OrderBy(emote => emote.ID))
            {
                EmoteComboBox.Add(emote.DisplayID, emote.PathToImage_off, emote.DisplayID);
            }
        }

        private string ResolveTextMatch(CallwordTriggerType triggerType)
        {
            return UsesTextMatch(triggerType) ? TextValueTextBox.Text?.Trim() ?? string.Empty : string.Empty;
        }

        private CallwordTriggerType GetSelectedTriggerType()
        {
            string displayName = TriggerTypeDropdown.Text?.Trim() ?? string.Empty;
            return triggerTypeOptions
                .FirstOrDefault(option => string.Equals(option.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                ?.TriggerType ?? CallwordTriggerType.Ao2Callword;
        }

        private static bool IsValid(CallwordTriggerType triggerType, string match, string characterName, string emoteName)
        {
            if (UsesTextMatch(triggerType) && string.IsNullOrWhiteSpace(match))
            {
                return false;
            }

            if (UsesCharacter(triggerType) && string.IsNullOrWhiteSpace(characterName))
            {
                return false;
            }

            return triggerType != CallwordTriggerType.CharacterEmoteUsed || !string.IsNullOrWhiteSpace(emoteName);
        }

        private string? ResolvePreviewPath()
        {
            string customPath = SoundPathTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                return customPath;
            }

            return ResolveDefaultNotificationPath();
        }

        internal static string? ResolveDefaultNotificationPath()
        {
            return AO2ViewportAudioResolver.ResolveCourtSfxPath("word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("sfx-word_call")
                ?? AO2ViewportAudioResolver.ResolveSfxPath("modcall");
        }

        private void StopPreview()
        {
            previewPlayer.Stop();
            PreviewButton.IsPlaying = false;
        }

        private void RefreshVolumeText()
        {
            if (VolumeValueTextBox != null)
            {
                suppressVolumeTextEvents = true;
                VolumeValueTextBox.Text = currentVolumePercent.ToString(CultureInfo.InvariantCulture) + "%";
                suppressVolumeTextEvents = false;
            }
        }

        private void SetVolumePercent(int value, bool updateSlider, bool updateText)
        {
            currentVolumePercent = Math.Max(0, value);
            if (updateSlider && VolumeSlider != null)
            {
                VolumeSlider.Value = Math.Clamp(currentVolumePercent, 0, 200);
            }

            if (updateText)
            {
                RefreshVolumeText();
            }
        }

        private static bool TryParsePositivePercent(string? text, out int value)
        {
            string normalized = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= 0)
            {
                value = parsed;
                return true;
            }

            value = 0;
            return false;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SetVolumePercent((int)e.NewValue, updateSlider: false, updateText: true);
        }

        private void VolumeValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressVolumeTextEvents)
            {
                return;
            }

            if (TryParsePositivePercent(VolumeValueTextBox.Text, out int parsed))
            {
                SetVolumePercent(parsed, updateSlider: true, updateText: false);
            }
        }

        private void VolumeValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            RefreshVolumeText();
        }

        private static bool UsesTextMatch(CallwordTriggerType triggerType)
        {
            return triggerType == CallwordTriggerType.Ao2Callword
                || triggerType == CallwordTriggerType.MessageContains
                || triggerType == CallwordTriggerType.MessageStartsWith
                || triggerType == CallwordTriggerType.PlayerShownameSpeaks;
        }

        private static bool UsesCharacter(CallwordTriggerType triggerType)
        {
            return triggerType == CallwordTriggerType.CharacterSpeaks
                || triggerType == CallwordTriggerType.CharacterEmoteUsed;
        }

        private static bool UsesCustomSound(CallwordTriggerType triggerType)
        {
            return triggerType != CallwordTriggerType.Ao2Callword;
        }

        private static bool SupportsWholeWord(CallwordTriggerType triggerType)
        {
            return triggerType == CallwordTriggerType.Ao2Callword
                || triggerType == CallwordTriggerType.MessageContains
                || triggerType == CallwordTriggerType.MessageStartsWith;
        }

        private static List<CharacterSelectorOption> BuildCharacterOptions()
        {
            return CharacterFolder.FullList
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .Select(character => new CharacterSelectorOption(
                    character.Name,
                    character.configINI.ShowName,
                    character.DirectoryPath,
                    character.CharIconPath))
                .ToList();
        }

        private static IReadOnlyList<TriggerTypeOption> BuildTriggerTypeOptions()
        {
            return new[]
            {
                new TriggerTypeOption(CallwordTriggerType.Ao2Callword, "AO2 Callword"),
                new TriggerTypeOption(CallwordTriggerType.MessageContains, "Message contains"),
                new TriggerTypeOption(CallwordTriggerType.MessageStartsWith, "Message starts with"),
                new TriggerTypeOption(CallwordTriggerType.CharacterSpeaks, "Character speaks"),
                new TriggerTypeOption(CallwordTriggerType.PlayerShownameSpeaks, "Player with showname speaks"),
                new TriggerTypeOption(CallwordTriggerType.CharacterEmoteUsed, "Character emote is used")
            };
        }

        private static CallwordRule Clone(CallwordRule rule)
        {
            string match = string.IsNullOrWhiteSpace(rule.Match) ? rule.Word : rule.Match;
            return new CallwordRule
            {
                Word = rule.Word,
                TriggerType = rule.TriggerType,
                Match = match,
                CharacterName = rule.CharacterName,
                EmoteName = rule.EmoteName,
                SoundPath = rule.SoundPath,
                WholeWord = rule.WholeWord,
                IsEnabled = rule.IsEnabled
            };
        }

        private sealed class TriggerTypeOption
        {
            public TriggerTypeOption(CallwordTriggerType triggerType, string displayName)
            {
                TriggerType = triggerType;
                DisplayName = displayName;
            }

            public CallwordTriggerType TriggerType { get; }

            public string DisplayName { get; }
        }
    }
}
