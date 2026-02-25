using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.CharacterCreator;
using OceanyaClient.Features.Startup;

namespace OceanyaClient
{
    public partial class AOCharacterFileCreatorWindow : Window, IStartupFunctionalityWindow
    {
        // AO2 hard-codes interjection maximum visual frame duration to 1500 ms (courtroom.h shout_max_time).
        private const int Ao2ShoutVisualCutoffMs = 1500;

        public event Action? FinishedLoading;

        private readonly ObservableCollection<CharacterCreationEmoteViewModel> emotes =
            new ObservableCollection<CharacterCreationEmoteViewModel>();
        private readonly ObservableCollection<string> assetFolders = new ObservableCollection<string>();
        private readonly ObservableCollection<AdvancedEntryViewModel> advancedEntries =
            new ObservableCollection<AdvancedEntryViewModel>();
        private readonly ObservableCollection<string> sideOptions = new ObservableCollection<string>();
        private readonly ObservableCollection<string> blipOptions = new ObservableCollection<string>();
        private readonly ObservableCollection<string> chatOptions = new ObservableCollection<string>();
        private readonly ObservableCollection<string> effectsFolderOptions = new ObservableCollection<string>();
        private readonly ObservableCollection<string> scalingOptions = new ObservableCollection<string>();
        private readonly ObservableCollection<string> booleanOptions = new ObservableCollection<string>();
        private readonly Dictionary<string, string> selectedShoutVisualSourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> selectedShoutSfxSourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly AO2BlipPreviewPlayer blipPreviewPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer shoutSfxPreviewPlayer = new AO2BlipPreviewPlayer();
        private readonly AO2BlipPreviewPlayer realizationSfxPreviewPlayer = new AO2BlipPreviewPlayer();
        private readonly Dictionary<string, IAnimationPlayer> shoutVisualPlayers = new Dictionary<string, IAnimationPlayer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DispatcherTimer> shoutVisualCutoffTimers = new Dictionary<string, DispatcherTimer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> shoutDefaultVisualPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool isUpdatingEmoteEditor;
        private string activeSection = "setup";
        private string customBlipOptionText = string.Empty;
        private string selectedCustomBlipSourcePath = string.Empty;
        private string selectedRealizationSourcePath = string.Empty;
        private CancellationTokenSource? blipPreviewCancellation;

        public AOCharacterFileCreatorWindow()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);

            EmoteListBox.ItemsSource = emotes;
            AdvancedEntriesListBox.ItemsSource = advancedEntries;
            SideComboBox.ItemsSource = sideOptions;
            GenderBlipsDropdown.ItemsSource = blipOptions;
            ChatDropdown.ItemsSource = chatOptions;
            EffectsDropdown.ItemsSource = effectsFolderOptions;
            ScalingDropdown.ItemsSource = scalingOptions;
            StretchDropdown.ItemsSource = booleanOptions;
            NeedsShownameDropdown.ItemsSource = booleanOptions;

            FrameTargetComboBox.ItemsSource = Enum.GetValues(typeof(CharacterFrameTarget));
            FrameTypeComboBox.ItemsSource = Enum.GetValues(typeof(CharacterFrameEventType));
            FrameTargetComboBox.SelectedItem = CharacterFrameTarget.PreAnimation;
            FrameTypeComboBox.SelectedItem = CharacterFrameEventType.Sfx;

            PopulateMountPaths();
            PopulateDefaultSideBlipChatAndEffectsOptions();
            InitializeCharacterDefaults();
            InitializeShoutPreviewDefaults();
            AddDefaultEmote();
            SetActiveSection("setup");
            ApplySavedWindowState();
            ApplySavedPreviewVolume();
            RefreshChatPreview();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWorkAreaMaxBounds();
            RefreshChatPreview();
            FinishedLoading?.Invoke();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        private void PopulateMountPaths()
        {
            List<string> mountPaths = new List<string>(Globals.BaseFolders ?? new List<string>());
            if (mountPaths.Count == 0)
            {
                string fallback = Path.GetDirectoryName(Globals.PathToConfigINI ?? string.Empty) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    mountPaths.Add(fallback);
                }
            }

            MountPathComboBox.ItemsSource = mountPaths;
            if (mountPaths.Count > 0)
            {
                MountPathComboBox.SelectedIndex = 0;
            }

            bool autoSelected = mountPaths.Count == 1;
            MountPathAutoPanel.Visibility = autoSelected ? Visibility.Visible : Visibility.Collapsed;
            MountPathSelectPanel.Visibility = autoSelected ? Visibility.Collapsed : Visibility.Visible;
            MountPathResolvedTextBlock.Text = autoSelected
                ? "The character folder will be created in: " + BuildCharactersDirectoryDisplayPath(mountPaths[0])
                : "Select where the new character folder should be created.";
            if (mountPaths.Count == 0)
            {
                StatusTextBlock.Text = "No mount path was detected automatically. Select one manually.";
            }
        }

        private void PopulateDefaultSideBlipChatAndEffectsOptions()
        {
            string[] defaultsSides =
            {
                "def",
                "pro",
                "wit",
                "jud",
                "hld",
                "jur",
                "sea",
                "gallery"
            };
            sideOptions.Clear();
            foreach (string side in defaultsSides)
            {
                sideOptions.Add(side);
            }

            blipOptions.Clear();
            foreach (string blipFromAssets in BlipCatalog.GetBlips())
            {
                if (!blipOptions.Contains(blipFromAssets, StringComparer.OrdinalIgnoreCase))
                {
                    blipOptions.Add(blipFromAssets);
                }
            }

            chatOptions.Clear();
            foreach (string chatFromAssets in ChatCatalog.GetChats())
            {
                if (!chatOptions.Contains(chatFromAssets, StringComparer.OrdinalIgnoreCase))
                {
                    chatOptions.Add(chatFromAssets);
                }
            }

            effectsFolderOptions.Clear();
            foreach (string effectsFolderFromAssets in EffectsFolderCatalog.GetEffectFolders())
            {
                if (!effectsFolderOptions.Contains(effectsFolderFromAssets, StringComparer.OrdinalIgnoreCase))
                {
                    effectsFolderOptions.Add(effectsFolderFromAssets);
                }
            }

            scalingOptions.Clear();
            scalingOptions.Add("smooth");
            scalingOptions.Add("pixel");
            scalingOptions.Add("fast");

            booleanOptions.Clear();
            booleanOptions.Add("true");
            booleanOptions.Add("false");
        }

        private void InitializeCharacterDefaults()
        {
            CharacterFolderNameTextBox.Text = "new_character";
            ShowNameTextBox.Text = "New Character";
            SideComboBox.Text = "wit";
            GenderBlipsDropdown.Text = blipOptions.FirstOrDefault() ?? string.Empty;
            // Match AO2 defaults by leaving these unset in char.ini unless the user explicitly chooses values.
            ScalingDropdown.Text = string.Empty;
            StretchDropdown.Text = string.Empty;
            NeedsShownameDropdown.Text = string.Empty;
            ChatDropdown.Text = "default";
            EffectsDropdown.Text = string.Empty;
            CustomShoutNameTextBox.Text = string.Empty;
            FrameNumberForDelayTextBox.Text = "1";
            FramesPerSecondTextBox.Text = "60";
            FrameNumberTextBox.Text = "1";
            FrameValueTextBox.Text = "1";
            CustomFrameTargetTextBox.Text = "anim/custom";
        }

        private void AddDefaultEmote()
        {
            CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
            {
                Name = "Normal",
                PreAnimation = "-",
                Animation = "normal",
                EmoteModifier = 0,
                DeskModifier = 1,
                SfxName = "1",
                SfxDelayMs = 1,
                SfxLooping = false
            };
            emotes.Add(emote);
            RefreshEmoteLabels();
            EmoteListBox.SelectedItem = emote;
        }

        private void CharacterField_TextChanged(object sender, TextChangedEventArgs e)
        {
            StatusTextBlock.Text = "Character metadata updated.";
        }

        private void CharacterSelectionField_Changed(object sender, SelectionChangedEventArgs e)
        {
            StatusTextBlock.Text = "Character metadata updated.";
        }

        private void CharacterAutoCompleteField_TextValueChanged(object? sender, EventArgs e)
        {
            StatusTextBlock.Text = "Character metadata updated.";
        }

        private void ChatDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            StatusTextBlock.Text = "Character metadata updated.";
            RefreshChatPreview();
        }

        private void MountPathComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string mountPath = (MountPathComboBox.SelectedItem as string) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mountPath))
            {
                MountPathResolvedTextBlock.Text = "The character folder will be created in: " + BuildCharactersDirectoryDisplayPath(mountPath);
            }
        }

        private static string BuildCharactersDirectoryDisplayPath(string mountPath)
        {
            string path = Path.Combine(mountPath ?? string.Empty, "characters");
            path = path.Replace('\\', '/').TrimEnd('/');
            return path + "/";
        }

        private void SectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            string section = button.Tag?.ToString()?.Trim()?.ToLowerInvariant() ?? "setup";
            SetActiveSection(section);
        }

        private void SetActiveSection(string section)
        {
            activeSection = section;
            SetupSectionPanel.Visibility = string.Equals(section, "setup", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            EffectsSectionPanel.Visibility = string.Equals(section, "effects", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmotesSectionPanel.Visibility = string.Equals(section, "emotes", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            AdvancedSectionPanel.Visibility = Visibility.Collapsed;
            ApplySectionButtonStyles();

            if (string.Equals(section, "setup", StringComparison.OrdinalIgnoreCase))
            {
                StatusTextBlock.Text = "Step 1: Configure base character metadata.";
            }
            else if (string.Equals(section, "effects", StringComparison.OrdinalIgnoreCase))
            {
                StatusTextBlock.Text = "Step 2: Configure optional effects and shout assets.";
            }
            else if (string.Equals(section, "emotes", StringComparison.OrdinalIgnoreCase))
            {
                StatusTextBlock.Text = "Step 3: Add and tune emotes.";
            }
            else
            {
                StatusTextBlock.Text = "Configure the character and generate a new AO folder.";
            }
        }

        private void ApplySectionButtonStyles()
        {
            ApplySectionButtonStyle(
                SectionSetupButton,
                string.Equals(activeSection, "setup", StringComparison.OrdinalIgnoreCase));
            ApplySectionButtonStyle(
                SectionEffectsButton,
                string.Equals(activeSection, "effects", StringComparison.OrdinalIgnoreCase));
            ApplySectionButtonStyle(
                SectionEmotesButton,
                string.Equals(activeSection, "emotes", StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplySectionButtonStyle(Button button, bool isActive)
        {
            if (isActive)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(56, 90, 120));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(115, 171, 214));
                button.Foreground = Brushes.White;
            }
            else
            {
                button.Background = new SolidColorBrush(Color.FromRgb(31, 31, 31));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 62));
                button.Foreground = new SolidColorBrush(Color.FromRgb(236, 236, 236));
            }
        }

        private void DefaultTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(activeSection, "setup", StringComparison.OrdinalIgnoreCase))
            {
                ResetSetupToDefaults();
                StatusTextBlock.Text = "Initial Character Setup tab reset to defaults.";
                return;
            }

            if (string.Equals(activeSection, "effects", StringComparison.OrdinalIgnoreCase))
            {
                ResetEffectsToDefaults();
                StatusTextBlock.Text = "Effects tab reset to defaults.";
                return;
            }

            if (string.Equals(activeSection, "emotes", StringComparison.OrdinalIgnoreCase))
            {
                ResetEmotesToDefaults();
                StatusTextBlock.Text = "Emotes tab reset to defaults.";
            }
        }

        private void DefaultAllButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSetupToDefaults();
            ResetEffectsToDefaults();
            ResetEmotesToDefaults();
            ResetFrameAndAdvancedToDefaults();
            StatusTextBlock.Text = "All tabs reset to defaults.";
        }

        private void ResetSetupToDefaults()
        {
            CharacterFolderNameTextBox.Text = "new_character";
            ShowNameTextBox.Text = "New Character";
            SideComboBox.Text = "wit";
            SetBlipText(blipOptions.FirstOrDefault() ?? string.Empty);
        }

        private void ResetEmotesToDefaults()
        {
            SaveSelectedEmoteEditorValues();
            emotes.Clear();
            AddDefaultEmote();
        }

        private void ResetFrameAndAdvancedToDefaults()
        {
            advancedEntries.Clear();
            FrameTargetComboBox.SelectedItem = CharacterFrameTarget.PreAnimation;
            FrameTypeComboBox.SelectedItem = CharacterFrameEventType.Sfx;
            FrameNumberForDelayTextBox.Text = "1";
            FramesPerSecondTextBox.Text = "60";
            FrameNumberTextBox.Text = "1";
            FrameValueTextBox.Text = "1";
            CustomFrameTargetTextBox.Text = "anim/custom";
            FrameToDelayResultTextBlock.Text = string.Empty;
        }

        private void EmoteListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveSelectedEmoteEditorValues();
            LoadSelectedEmoteEditorValues();
        }

        private void EmoteEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingEmoteEditor)
            {
                return;
            }

            SaveSelectedEmoteEditorValues();
        }

        private void SfxLoopingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isUpdatingEmoteEditor)
            {
                return;
            }

            SaveSelectedEmoteEditorValues();
        }

        private CharacterCreationEmoteViewModel? GetSelectedEmote()
        {
            return EmoteListBox.SelectedItem as CharacterCreationEmoteViewModel;
        }

        private void LoadSelectedEmoteEditorValues()
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            isUpdatingEmoteEditor = true;
            try
            {
                bool enabled = selected != null;
                SetEmoteEditorEnabled(enabled);
                if (!enabled)
                {
                    EmoteNameTextBox.Text = string.Empty;
                    PreAnimationTextBox.Text = string.Empty;
                    AnimationTextBox.Text = string.Empty;
                    EmoteModifierTextBox.Text = string.Empty;
                    DeskModifierTextBox.Text = string.Empty;
                    SfxNameTextBox.Text = string.Empty;
                    SfxDelayTextBox.Text = string.Empty;
                    PreAnimationDurationTextBox.Text = string.Empty;
                    StayTimeTextBox.Text = string.Empty;
                    BlipsOverrideTextBox.Text = string.Empty;
                    FrameEventListBox.ItemsSource = null;
                    return;
                }

                CharacterCreationEmoteViewModel safeSelected = selected!;
                EmoteNameTextBox.Text = safeSelected.Name;
                PreAnimationTextBox.Text = safeSelected.PreAnimation;
                AnimationTextBox.Text = safeSelected.Animation;
                EmoteModifierTextBox.Text = safeSelected.EmoteModifier.ToString();
                DeskModifierTextBox.Text = safeSelected.DeskModifier.ToString();
                SfxNameTextBox.Text = safeSelected.SfxName;
                SfxDelayTextBox.Text = safeSelected.SfxDelayMs.ToString();
                SfxLoopingCheckBox.IsChecked = safeSelected.SfxLooping;
                PreAnimationDurationTextBox.Text = safeSelected.PreAnimationDurationMs?.ToString() ?? string.Empty;
                StayTimeTextBox.Text = safeSelected.StayTimeMs?.ToString() ?? string.Empty;
                BlipsOverrideTextBox.Text = safeSelected.BlipsOverride;
                FrameEventListBox.ItemsSource = safeSelected.FrameEvents;
            }
            finally
            {
                isUpdatingEmoteEditor = false;
            }
        }

        private void SetEmoteEditorEnabled(bool enabled)
        {
            EmoteNameTextBox.IsEnabled = enabled;
            PreAnimationTextBox.IsEnabled = enabled;
            AnimationTextBox.IsEnabled = enabled;
            EmoteModifierTextBox.IsEnabled = enabled;
            DeskModifierTextBox.IsEnabled = enabled;
            SfxNameTextBox.IsEnabled = enabled;
            SfxDelayTextBox.IsEnabled = enabled;
            SfxLoopingCheckBox.IsEnabled = enabled;
            PreAnimationDurationTextBox.IsEnabled = enabled;
            StayTimeTextBox.IsEnabled = enabled;
            BlipsOverrideTextBox.IsEnabled = enabled;
            FrameTargetComboBox.IsEnabled = enabled;
            FrameTypeComboBox.IsEnabled = enabled;
            FrameNumberTextBox.IsEnabled = enabled;
            FrameValueTextBox.IsEnabled = enabled;
            CustomFrameTargetTextBox.IsEnabled = enabled;
            FrameEventListBox.IsEnabled = enabled;
        }

        private void SaveSelectedEmoteEditorValues()
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            selected.Name = (EmoteNameTextBox.Text ?? string.Empty).Trim();
            selected.PreAnimation = (PreAnimationTextBox.Text ?? string.Empty).Trim();
            selected.Animation = (AnimationTextBox.Text ?? string.Empty).Trim();
            selected.EmoteModifier = ParseIntOrDefault(EmoteModifierTextBox.Text, selected.EmoteModifier);
            selected.DeskModifier = ParseIntOrDefault(DeskModifierTextBox.Text, selected.DeskModifier);
            selected.SfxName = (SfxNameTextBox.Text ?? string.Empty).Trim();
            selected.SfxDelayMs = Math.Max(0, ParseIntOrDefault(SfxDelayTextBox.Text, selected.SfxDelayMs));
            selected.SfxLooping = SfxLoopingCheckBox.IsChecked == true;
            selected.PreAnimationDurationMs = ParseNullableInt(PreAnimationDurationTextBox.Text);
            selected.StayTimeMs = ParseNullableInt(StayTimeTextBox.Text);
            selected.BlipsOverride = (BlipsOverrideTextBox.Text ?? string.Empty).Trim();
            selected.RefreshDisplayName();
            RefreshEmoteLabels();
        }

        private static int ParseIntOrDefault(string? raw, int fallback)
        {
            if (int.TryParse(raw?.Trim(), out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static int? ParseNullableInt(string? raw)
        {
            if (int.TryParse(raw?.Trim(), out int parsed))
            {
                return Math.Max(0, parsed);
            }

            return null;
        }

        private void RefreshEmoteLabels()
        {
            for (int i = 0; i < emotes.Count; i++)
            {
                emotes[i].Index = i + 1;
                emotes[i].RefreshDisplayName();
            }
        }

        private void AddEmoteButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedEmoteEditorValues();
            CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
            {
                Name = "Emote " + (emotes.Count + 1),
                PreAnimation = "-",
                Animation = "normal",
                EmoteModifier = 0,
                DeskModifier = 1,
                SfxName = "1",
                SfxDelayMs = 1
            };
            emotes.Add(emote);
            RefreshEmoteLabels();
            EmoteListBox.SelectedItem = emote;
            StatusTextBlock.Text = "Emote added.";
        }

        private void RemoveEmoteButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            int index = EmoteListBox.SelectedIndex;
            emotes.Remove(selected);
            RefreshEmoteLabels();
            if (emotes.Count > 0)
            {
                EmoteListBox.SelectedIndex = Math.Clamp(index, 0, emotes.Count - 1);
            }
            StatusTextBlock.Text = "Emote removed.";
        }

        private void MoveEmoteUpButton_Click(object sender, RoutedEventArgs e)
        {
            int index = EmoteListBox.SelectedIndex;
            if (index <= 0)
            {
                return;
            }

            CharacterCreationEmoteViewModel item = emotes[index];
            emotes.RemoveAt(index);
            emotes.Insert(index - 1, item);
            RefreshEmoteLabels();
            EmoteListBox.SelectedIndex = index - 1;
        }

        private void MoveEmoteDownButton_Click(object sender, RoutedEventArgs e)
        {
            int index = EmoteListBox.SelectedIndex;
            if (index < 0 || index >= emotes.Count - 1)
            {
                return;
            }

            CharacterCreationEmoteViewModel item = emotes[index];
            emotes.RemoveAt(index);
            emotes.Insert(index + 1, item);
            RefreshEmoteLabels();
            EmoteListBox.SelectedIndex = index + 1;
        }

        private void AddFrameEventButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            CharacterFrameTarget target = FrameTargetComboBox.SelectedItem is CharacterFrameTarget selectedTarget
                ? selectedTarget
                : CharacterFrameTarget.PreAnimation;
            CharacterFrameEventType eventType = FrameTypeComboBox.SelectedItem is CharacterFrameEventType selectedType
                ? selectedType
                : CharacterFrameEventType.Sfx;

            int frame = Math.Max(1, ParseIntOrDefault(FrameNumberTextBox.Text, 1));
            string value = (FrameValueTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                value = "1";
            }

            FrameEventViewModel frameEvent = new FrameEventViewModel
            {
                Target = target,
                EventType = eventType,
                Frame = frame,
                Value = value,
                CustomTargetPath = (CustomFrameTargetTextBox.Text ?? string.Empty).Trim()
            };

            selected.FrameEvents.Add(frameEvent);
            FrameEventListBox.SelectedItem = frameEvent;
            StatusTextBlock.Text = "Frame event added.";
        }

        private void RemoveFrameEventButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null || FrameEventListBox.SelectedItem is not FrameEventViewModel eventView)
            {
                return;
            }

            selected.FrameEvents.Remove(eventView);
            StatusTextBlock.Text = "Frame event removed.";
        }

        private void FrameToDelayButton_Click(object sender, RoutedEventArgs e)
        {
            int frame = ParseIntOrDefault(FrameNumberForDelayTextBox.Text, 1);
            if (!double.TryParse(FramesPerSecondTextBox.Text?.Trim(), out double fps) || fps <= 0)
            {
                fps = 60;
            }

            int milliseconds = AOCharacterFileCreatorBuilder.ConvertFrameToMilliseconds(frame, fps);
            FrameToDelayResultTextBlock.Text = $"~{milliseconds} ms";
            SfxDelayTextBox.Text = milliseconds.ToString();
            SaveSelectedEmoteEditorValues();
            StatusTextBlock.Text = "Applied frame timing conversion to selected emote SFX delay.";
        }

        private void AddAssetFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Asset subfolders are intentionally hidden for now.
        }

        private void RemoveAssetFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Asset subfolders are intentionally hidden for now.
        }

        private void AddAdvancedEntryButton_Click(object sender, RoutedEventArgs e)
        {
            string section = (AdvancedSectionTextBox.Text ?? string.Empty).Trim();
            string key = (AdvancedKeyTextBox.Text ?? string.Empty).Trim();
            string value = (AdvancedValueTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            advancedEntries.Add(new AdvancedEntryViewModel
            {
                Section = section,
                Key = key,
                Value = value
            });
            AdvancedValueTextBox.Clear();
        }

        private void PreviewVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }

            ApplyPreviewVolume(e.NewValue / 100.0, persist: true);
        }

        private void BlipFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select custom blip audio file",
                Filter = "AO2-compatible audio (*.opus;*.ogg;*.mp3;*.wav)|*.opus;*.ogg;*.mp3;*.wav|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedCustomBlipSourcePath = dialog.FileName;
            string fileName = Path.GetFileName(dialog.FileName);
            customBlipOptionText = "Custom file: " + fileName;

            if (!blipOptions.Contains(customBlipOptionText, StringComparer.OrdinalIgnoreCase))
            {
                blipOptions.Add(customBlipOptionText);
            }

            SetBlipText(customBlipOptionText);
            StatusTextBlock.Text = "Custom blip imported. It will be copied into this character folder on generate.";
        }

        private void InitializeShoutPreviewDefaults()
        {
            shoutDefaultVisualPaths["holdit"] = ResolveDefaultShoutVisualPath("holdit");
            shoutDefaultVisualPaths["objection"] = ResolveDefaultShoutVisualPath("objection");
            shoutDefaultVisualPaths["takethat"] = ResolveDefaultShoutVisualPath("takethat");
            shoutDefaultVisualPaths["custom"] = string.Empty;

            HoldItVisualFileTextBlock.Text = string.IsNullOrWhiteSpace(shoutDefaultVisualPaths["holdit"]) ? "No default visual found" : "Default visual";
            ObjectionVisualFileTextBlock.Text = string.IsNullOrWhiteSpace(shoutDefaultVisualPaths["objection"]) ? "No default visual found" : "Default visual";
            TakeThatVisualFileTextBlock.Text = string.IsNullOrWhiteSpace(shoutDefaultVisualPaths["takethat"]) ? "No default visual found" : "Default visual";
            CustomVisualFileTextBlock.Text = "No image";

            HoldItSfxFileTextBlock.Text = "Default sfx";
            ObjectionSfxFileTextBlock.Text = "Default sfx";
            TakeThatSfxFileTextBlock.Text = "Default sfx";
            CustomSfxFileTextBlock.Text = "Default sfx";

            UpdateShoutTilePreview("holdit");
            UpdateShoutTilePreview("objection");
            UpdateShoutTilePreview("takethat");
            UpdateShoutTilePreview("custom");
        }

        private string ResolveDefaultShoutVisualPath(string key)
        {
            string stem = key switch
            {
                "holdit" => "holdit_bubble",
                "objection" => "objection_bubble",
                "takethat" => "takethat_bubble",
                _ => string.Empty
            };
            if (string.IsNullOrWhiteSpace(stem))
            {
                return string.Empty;
            }

            string bundledPath = Path.Combine(AppContext.BaseDirectory, "Resources", "ShoutDefaults", stem + ".gif");
            if (File.Exists(bundledPath))
            {
                return bundledPath;
            }

            string[] themePriority = { "CC", "CCDefault", "CCBig", "CC1080p" };
            string[] suffixes = { ".gif", ".webp", ".apng", ".png" };
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                foreach (string theme in themePriority)
                {
                    string pathNoExt = Path.Combine(baseFolder, "themes", theme, stem);
                    foreach (string suffix in suffixes)
                    {
                        string candidate = pathNoExt + suffix;
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private void UpdateShoutTilePreview(string key)
        {
            StopShoutVisualPlayer(key);

            string path = ResolveShoutVisualPathForPreview(key);
            Image? imageControl = GetShoutImageControl(key);
            TextBlock? noImageText = GetShoutNoImageTextControl(key);
            if (imageControl == null || noImageText == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                imageControl.Source = null;
                noImageText.Visibility = Visibility.Visible;
                return;
            }

            if (TryLoadFirstFrame(path, out ImageSource? firstFrame, out _))
            {
                imageControl.Source = firstFrame;
                noImageText.Visibility = Visibility.Collapsed;
                return;
            }

            BitmapImage staticImage = new BitmapImage();
            staticImage.BeginInit();
            staticImage.CacheOption = BitmapCacheOption.OnLoad;
            staticImage.UriSource = new Uri(path, UriKind.Absolute);
            staticImage.EndInit();
            if (staticImage.CanFreeze)
            {
                staticImage.Freeze();
            }

            imageControl.Source = staticImage;
            noImageText.Visibility = Visibility.Collapsed;
        }

        private static bool TryLoadFirstFrame(string path, out ImageSource? initialFrame, out double estimatedDurationMs)
        {
            initialFrame = null;
            estimatedDurationMs = 0;
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                BitmapFrame? frame = decoder.Frames.FirstOrDefault();
                if (frame == null)
                {
                    return false;
                }

                initialFrame = frame;
                estimatedDurationMs = EstimateDecoderDurationMs(decoder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double EstimateDecoderDurationMs(BitmapDecoder decoder)
        {
            if (decoder.Frames.Count <= 1)
            {
                return 0;
            }

            double total = 0;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                double delayMs = 90;
                try
                {
                    if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/grctlext/Delay"))
                    {
                        object delayQuery = meta.GetQuery("/grctlext/Delay");
                        if (delayQuery is ushort delay)
                        {
                            delayMs = Math.Max(20, delay * 10d);
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                total += delayMs;
            }

            return total;
        }

        private string ResolveShoutVisualPathForPreview(string key)
        {
            if (selectedShoutVisualSourcePaths.TryGetValue(key, out string? selectedPath)
                && !string.IsNullOrWhiteSpace(selectedPath))
            {
                return selectedPath;
            }

            return shoutDefaultVisualPaths.TryGetValue(key, out string? defaultPath)
                ? defaultPath ?? string.Empty
                : string.Empty;
        }

        private Image? GetShoutImageControl(string key)
        {
            return key switch
            {
                "holdit" => HoldItPreviewImage,
                "objection" => ObjectionPreviewImage,
                "takethat" => TakeThatPreviewImage,
                "custom" => CustomPreviewImage,
                _ => null
            };
        }

        private TextBlock? GetShoutNoImageTextControl(string key)
        {
            return key switch
            {
                "holdit" => HoldItNoImageText,
                "objection" => ObjectionNoImageText,
                "takethat" => TakeThatNoImageText,
                "custom" => CustomNoImageText,
                _ => null
            };
        }

        private void StopShoutVisualPlayer(string key)
        {
            StopShoutVisualCutoffTimer(key);

            if (!shoutVisualPlayers.TryGetValue(key, out IAnimationPlayer? player))
            {
                return;
            }

            try
            {
                player.Stop();
            }
            catch
            {
                // ignored
            }

            shoutVisualPlayers.Remove(key);
        }

        private void StartShoutVisualCutoffTimer(string key)
        {
            StopShoutVisualCutoffTimer(key);

            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Ao2ShoutVisualCutoffMs)
            };

            timer.Tick += (_, _) =>
            {
                StopShoutVisualCutoffTimer(key);
                StopShoutVisualPlayer(key);
            };

            shoutVisualCutoffTimers[key] = timer;
            timer.Start();
        }

        private void StopShoutVisualCutoffTimer(string key)
        {
            if (!shoutVisualCutoffTimers.TryGetValue(key, out DispatcherTimer? timer))
            {
                return;
            }

            timer.Stop();
            shoutVisualCutoffTimers.Remove(key);
        }

        private void ShoutVisualFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            string key = (element.Tag?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select shout visual file",
                Filter = "AO2-compatible image/animation (*.webp;*.apng;*.gif;*.png)|*.webp;*.apng;*.gif;*.png|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedShoutVisualSourcePaths[key] = dialog.FileName;
            SetShoutVisualFileNameText(key, Path.GetFileName(dialog.FileName));
            UpdateShoutTilePreview(key);
            StatusTextBlock.Text = $"Selected {key} visual file.";
        }

        private void ShoutSfxFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            string key = (element.Tag?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select shout sound file",
                Filter = "AO2-compatible audio (*.opus;*.ogg;*.mp3;*.wav)|*.opus;*.ogg;*.mp3;*.wav|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedShoutSfxSourcePaths[key] = dialog.FileName;
            SetShoutSfxFileNameText(key, Path.GetFileName(dialog.FileName));
            StatusTextBlock.Text = $"Selected {key} shout sound file.";
        }

        private void ShoutDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            string key = (element.Tag?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            ResetSingleShoutToDefault(key);
            StatusTextBlock.Text = $"Reset {key} shout to default values.";
        }

        private void ResetEffectsButton_Click(object sender, RoutedEventArgs e)
        {
            ResetEffectsToDefaults();
            StatusTextBlock.Text = "Effects configuration reset to defaults.";
        }

        private void RealizationFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select realization sound file",
                Filter = "AO2-compatible audio (*.opus;*.ogg;*.mp3;*.wav)|*.opus;*.ogg;*.mp3;*.wav|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedRealizationSourcePath = dialog.FileName;
            RealizationTextBox.Text = Path.GetFileName(dialog.FileName);
            StatusTextBlock.Text = "Selected realization sound file.";
        }

        private void SetShoutVisualFileNameText(string key, string fileName)
        {
            switch (key)
            {
                case "holdit":
                    HoldItVisualFileTextBlock.Text = fileName;
                    break;
                case "objection":
                    ObjectionVisualFileTextBlock.Text = fileName;
                    break;
                case "takethat":
                    TakeThatVisualFileTextBlock.Text = fileName;
                    break;
                case "custom":
                    CustomVisualFileTextBlock.Text = fileName;
                    break;
            }
        }

        private void SetShoutSfxFileNameText(string key, string fileName)
        {
            switch (key)
            {
                case "holdit":
                    HoldItSfxFileTextBlock.Text = fileName;
                    break;
                case "objection":
                    ObjectionSfxFileTextBlock.Text = fileName;
                    break;
                case "takethat":
                    TakeThatSfxFileTextBlock.Text = fileName;
                    break;
                case "custom":
                    CustomSfxFileTextBlock.Text = fileName;
                    break;
            }
        }

        private void ResetSingleShoutToDefault(string key)
        {
            StopShoutPreview(key, resetImage: false);
            selectedShoutVisualSourcePaths.Remove(key);
            selectedShoutSfxSourcePaths.Remove(key);

            bool hasDefaultVisual = shoutDefaultVisualPaths.TryGetValue(key, out string? defaultPath)
                && !string.IsNullOrWhiteSpace(defaultPath);
            SetShoutVisualFileNameText(key, hasDefaultVisual ? "Default visual" : "No image");
            SetShoutSfxFileNameText(key, "Default sfx");
            UpdateShoutTilePreview(key);
        }

        private void ResetEffectsToDefaults()
        {
            StopBlipPreview();
            shoutSfxPreviewPlayer.Stop();
            realizationSfxPreviewPlayer.Stop();
            RealizationPlayButton.IsPlaying = false;

            ChatDropdown.Text = "default";
            EffectsDropdown.Text = string.Empty;
            CustomShoutNameTextBox.Text = string.Empty;
            selectedRealizationSourcePath = string.Empty;
            RealizationTextBox.Text = string.Empty;

            // Match AO2 defaults by leaving these unset in char.ini unless the user explicitly chooses values.
            ScalingDropdown.Text = string.Empty;
            StretchDropdown.Text = string.Empty;
            NeedsShownameDropdown.Text = string.Empty;

            ResetSingleShoutToDefault("holdit");
            ResetSingleShoutToDefault("objection");
            ResetSingleShoutToDefault("takethat");
            ResetSingleShoutToDefault("custom");
        }

        private void ShoutPlayButton_PlayRequested(object sender, EventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            string key = (element.Tag?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            StartShoutPreview(key);
        }

        private void ShoutPlayButton_StopRequested(object sender, EventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            string key = (element.Tag?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            StopShoutPreview(key, resetImage: true);
        }

        private void ShoutPlayButton_PlaybackCompleted(object sender, EventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            string key = (element.Tag?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            StopShoutPreview(key, resetImage: true);
        }

        private void RealizationPlayButton_PlayRequested(object sender, EventArgs e)
        {
            string? path = ResolveRealizationPathForPreview();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                OceanyaMessageBox.Show(
                    this,
                    "Could not resolve a playable realization sound.",
                    "Realization Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!realizationSfxPreviewPlayer.TrySetBlip(path))
            {
                OceanyaMessageBox.Show(
                    this,
                    "Could not initialize AO2 realization playback for this file.\n" + realizationSfxPreviewPlayer.LastErrorMessage,
                    "Realization Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            double durationMs = realizationSfxPreviewPlayer.GetLoadedDurationMs();
            RealizationPlayButton.DurationMs = durationMs > 0 ? durationMs : 900;
            RealizationPlayButton.IsPlaying = true;
            _ = realizationSfxPreviewPlayer.PlayBlip();
        }

        private void RealizationPlayButton_StopRequested(object sender, EventArgs e)
        {
            realizationSfxPreviewPlayer.Stop();
            RealizationPlayButton.IsPlaying = false;
        }

        private void RealizationPlayButton_PlaybackCompleted(object sender, EventArgs e)
        {
            realizationSfxPreviewPlayer.Stop();
            RealizationPlayButton.IsPlaying = false;
        }

        private string? ResolveRealizationPathForPreview()
        {
            string token = (RealizationTextBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(selectedRealizationSourcePath)
                && string.Equals(token, Path.GetFileName(selectedRealizationSourcePath), StringComparison.OrdinalIgnoreCase))
            {
                return selectedRealizationSourcePath;
            }

            return ResolveAo2SfxPath(token);
        }

        private string? ResolveShoutSfxPathForPreview(string key)
        {
            if (selectedShoutSfxSourcePaths.TryGetValue(key, out string? sourcePath)
                && !string.IsNullOrWhiteSpace(sourcePath)
                && File.Exists(sourcePath))
            {
                return sourcePath;
            }

            return ResolveAo2SfxPath(key);
        }

        private void StartShoutPreview(string key)
        {
            StopShoutPreview(key, resetImage: false);

            double visualDurationMs = 0;
            bool visualStarted = false;
            string visualPath = ResolveShoutVisualPathForPreview(key);
            if (!string.IsNullOrWhiteSpace(visualPath) && File.Exists(visualPath))
            {
                StartShoutVisualAnimation(key, visualPath, out visualDurationMs);
                visualStarted = shoutVisualPlayers.ContainsKey(key);
                if (visualStarted)
                {
                    StartShoutVisualCutoffTimer(key);
                }
            }

            double sfxDurationMs = 0;
            string? sfxPath = ResolveShoutSfxPathForPreview(key);
            if (!string.IsNullOrWhiteSpace(sfxPath) && File.Exists(sfxPath))
            {
                if (shoutSfxPreviewPlayer.TrySetBlip(sfxPath))
                {
                    sfxDurationMs = shoutSfxPreviewPlayer.GetLoadedDurationMs();
                    _ = shoutSfxPreviewPlayer.PlayBlip();
                }
            }

            PlayStopProgressButton? button = GetShoutPlayButton(key);
            if (button != null)
            {
                double visualCutoffDurationMs = visualStarted
                    ? (visualDurationMs > 0 ? Math.Min(visualDurationMs, Ao2ShoutVisualCutoffMs) : Ao2ShoutVisualCutoffMs)
                    : 0;
                button.DurationMs = Math.Max(350, Math.Max(visualCutoffDurationMs, sfxDurationMs <= 0 ? 0 : sfxDurationMs));
                button.IsPlaying = true;
            }
        }

        private void StopShoutPreview(string key, bool resetImage)
        {
            StopShoutVisualCutoffTimer(key);
            StopShoutVisualPlayer(key);
            shoutSfxPreviewPlayer.Stop();

            PlayStopProgressButton? button = GetShoutPlayButton(key);
            if (button != null)
            {
                button.IsPlaying = false;
            }

            if (resetImage)
            {
                UpdateShoutTilePreview(key);
            }
        }

        private PlayStopProgressButton? GetShoutPlayButton(string key)
        {
            return key switch
            {
                "holdit" => HoldItPlayButton,
                "objection" => ObjectionPlayButton,
                "takethat" => TakeThatPlayButton,
                "custom" => CustomPlayButton,
                _ => null
            };
        }

        private void StartShoutVisualAnimation(string key, string visualPath, out double durationMs)
        {
            durationMs = 0;
            Image? imageControl = GetShoutImageControl(key);
            TextBlock? noImageText = GetShoutNoImageTextControl(key);
            if (imageControl == null || noImageText == null)
            {
                return;
            }

            if (TryLoadFirstFrame(visualPath, out ImageSource? firstFrame, out double estimatedDurationFromDecoder))
            {
                durationMs = estimatedDurationFromDecoder;
                imageControl.Source = firstFrame;
                noImageText.Visibility = Visibility.Collapsed;
            }

            if (BitmapFrameAnimationPlayer.TryCreate(visualPath, loop: false, out BitmapFrameAnimationPlayer? bitmapPlayer)
                && bitmapPlayer != null)
            {
                durationMs = Math.Max(durationMs, 500);
                bitmapPlayer.FrameChanged += frame => Dispatcher.Invoke(() =>
                {
                    imageControl.Source = frame;
                    noImageText.Visibility = Visibility.Collapsed;
                });
                shoutVisualPlayers[key] = bitmapPlayer;
                return;
            }

            try
            {
                GifAnimationPlayer gifPlayer = new GifAnimationPlayer(visualPath, loop: false);
                durationMs = Math.Max(durationMs, 500);
                gifPlayer.FrameChanged += frame => Dispatcher.Invoke(() =>
                {
                    imageControl.Source = frame;
                    noImageText.Visibility = Visibility.Collapsed;
                });
                shoutVisualPlayers[key] = gifPlayer;
            }
            catch
            {
                // Static-only fallback already applied.
            }
        }

        private string? ResolveAo2SfxPath(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string normalized = token.Trim().Replace('\\', '/');
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                string[] directRoots =
                {
                    Path.Combine(baseFolder, "sounds"),
                    Path.Combine(baseFolder, "misc", "AA"),
                    Path.Combine(baseFolder, "misc", "AA", "sounds")
                };

                foreach (string root in directRoots)
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    string? resolved = ResolveWithAo2SuffixOrder(root, normalized);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                string soundsRoot = Path.Combine(baseFolder, "sounds");
                if (Directory.Exists(soundsRoot))
                {
                    string? resolvedGeneral = ResolveWithAo2SuffixOrder(soundsRoot, "general/" + normalized);
                    if (!string.IsNullOrWhiteSpace(resolvedGeneral))
                    {
                        return resolvedGeneral;
                    }

                    string? resolvedLegacy = ResolveWithAo2SuffixOrder(soundsRoot, "sfx-" + normalized);
                    if (!string.IsNullOrWhiteSpace(resolvedLegacy))
                    {
                        return resolvedLegacy;
                    }
                }
            }

            return null;
        }

        private void SetBlipText(string value)
        {
            GenderBlipsDropdown.Text = value;
            StatusTextBlock.Text = "Character metadata updated.";
        }

        private void RefreshChatPreview()
        {
            if (!IsLoaded)
            {
                return;
            }

            ChatPreviewControl.ChatToken = (ChatDropdown.Text ?? string.Empty).Trim();
            ChatPreviewControl.PreviewShowname = (ShowNameTextBox.Text ?? string.Empty).Trim();
            ChatPreviewControl.PreviewText = string.Empty;
            ChatPreviewControl.RefreshPreview();
        }

        private async void BlipPreviewPlayButton_PlayRequested(object sender, EventArgs e)
        {
            string previewToken = (GenderBlipsDropdown.Text ?? string.Empty).Trim();
            string? audioPath = ResolveBlipPreviewPath(previewToken);
            if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            {
                OceanyaMessageBox.Show(
                    this,
                    "Could not resolve a playable blip file for this value.",
                    "Blip Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            const string phrase = "Hello, these are my blips. This is Scorpio2 and i approve this message";
            try
            {
                await StartBlipPreviewAsync(audioPath, phrase);
            }
            catch (Exception ex)
            {
                StopBlipPreview();
                OceanyaMessageBox.Show(
                    this,
                    "Blip preview failed.\n\n" + ex.Message,
                    "Blip Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BlipPreviewPlayButton_StopRequested(object sender, EventArgs e)
        {
            StopBlipPreview();
        }

        private async Task StartBlipPreviewAsync(string audioPath, string phrase)
        {
            StopBlipPreview();

            blipPreviewCancellation = new CancellationTokenSource();

            List<int> playFrames = BuildAoStyleBlipFrames(phrase, blipRate: 2, blankBlip: false);
            double durationMs = Math.Max(700, phrase.Length * 40);
            BlipPreviewPlayButton.DurationMs = durationMs;
            BlipPreviewPlayButton.IsPlaying = true;
            if (!blipPreviewPlayer.TrySetBlip(audioPath))
            {
                string details = string.IsNullOrWhiteSpace(blipPreviewPlayer.LastErrorMessage)
                    ? "Unknown reason."
                    : blipPreviewPlayer.LastErrorMessage;
                OceanyaMessageBox.Show(
                    this,
                    "Could not initialize AO2 blip playback for this file.\n" +
                    details,
                    "Blip Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StopBlipPreview();
                return;
            }

            CancellationToken token = blipPreviewCancellation.Token;
            try
            {
                for (int i = 0; i < phrase.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (playFrames.Contains(i))
                    {
                        _ = blipPreviewPlayer.PlayBlip();
                    }

                    await Task.Delay(40, token);
                }
            }
            catch (NotSupportedException ex)
            {
                OceanyaMessageBox.Show(
                    this,
                    ex.Message,
                    "Blip Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation.
            }
            finally
            {
                StopBlipPreview();
            }
        }

        private static List<int> BuildAoStyleBlipFrames(string phrase, int blipRate, bool blankBlip)
        {
            List<int> frames = new List<int>();
            int ticker = 0;
            for (int i = 0; i < phrase.Length; i++)
            {
                char c = phrase[i];
                bool shouldPlay = false;
                if (blipRate <= 0)
                {
                    shouldPlay = ticker < 1 && (blankBlip || !char.IsWhiteSpace(c));
                }
                else if (ticker % blipRate == 0 && (blankBlip || !char.IsWhiteSpace(c)))
                {
                    shouldPlay = true;
                }

                if (shouldPlay)
                {
                    frames.Add(i);
                }

                if (!char.IsControl(c))
                {
                    ticker++;
                }
            }

            return frames;
        }

        private void StopBlipPreview()
        {
            if (blipPreviewCancellation != null)
            {
                try
                {
                    blipPreviewCancellation.Cancel();
                }
                catch
                {
                    // ignored
                }

                blipPreviewCancellation.Dispose();
                blipPreviewCancellation = null;
            }

            blipPreviewPlayer.Stop();
            BlipPreviewPlayButton.IsPlaying = false;
        }

        private void ApplySavedPreviewVolume()
        {
            double saved = Math.Clamp(SaveFile.Data.CharacterCreatorPreviewVolume, 0.0, 1.0);
            PreviewVolumeSlider.Value = saved * 100.0;
            ApplyPreviewVolume(saved, persist: false);
        }

        private void ApplyPreviewVolume(double normalizedVolume, bool persist)
        {
            double clamped = Math.Clamp(normalizedVolume, 0.0, 1.0);
            blipPreviewPlayer.Volume = (float)clamped;
            shoutSfxPreviewPlayer.Volume = (float)clamped;
            realizationSfxPreviewPlayer.Volume = (float)clamped;
            PreviewVolumePercentTextBlock.Text = $"{Math.Round(clamped * 100.0):0}%";

            if (persist)
            {
                SaveFile.Data.CharacterCreatorPreviewVolume = clamped;
                SaveFile.Save();
            }
        }

        private string? ResolveBlipPreviewPath(string token)
        {
            if (!string.IsNullOrWhiteSpace(selectedCustomBlipSourcePath)
                && !string.IsNullOrWhiteSpace(customBlipOptionText)
                && string.Equals(token, customBlipOptionText, StringComparison.OrdinalIgnoreCase))
            {
                return selectedCustomBlipSourcePath;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string normalized = token.Trim().Replace('\\', '/');
            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                string soundsRoot = Path.Combine(baseFolder, "sounds");
                if (!Directory.Exists(soundsRoot))
                {
                    continue;
                }

                string? resolved = ResolveWithAo2SuffixOrder(soundsRoot, normalized);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }

                if (!normalized.StartsWith("blips/", StringComparison.OrdinalIgnoreCase))
                {
                    resolved = ResolveWithAo2SuffixOrder(soundsRoot, "blips/" + normalized);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }

                resolved = ResolveWithAo2SuffixOrder(soundsRoot, "sfx-blip" + normalized);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        private static string? ResolveWithAo2SuffixOrder(string soundsRoot, string relativeToken)
        {
            string relative = relativeToken.Trim().Replace('/', Path.DirectorySeparatorChar);
            string direct = Path.Combine(soundsRoot, relative);
            string extension = Path.GetExtension(direct);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return File.Exists(direct) ? direct : null;
            }

            string[] suffixOrder = { ".opus", ".ogg", ".mp3", ".wav" };
            foreach (string suffix in suffixOrder)
            {
                string candidate = direct + suffix;
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void CopyShoutAssets(string characterDirectory)
        {
            CopyShoutVisual(characterDirectory, "holdit", "holdit_bubble");
            CopyShoutVisual(characterDirectory, "objection", "objection_bubble");
            CopyShoutVisual(characterDirectory, "takethat", "takethat_bubble");
            CopyShoutVisual(characterDirectory, "custom", "custom");

            CopyShoutSfx(characterDirectory, "holdit", "holdit");
            CopyShoutSfx(characterDirectory, "objection", "objection");
            CopyShoutSfx(characterDirectory, "takethat", "takethat");
            CopyShoutSfx(characterDirectory, "custom", "custom");
        }

        private void CopyShoutVisual(string characterDirectory, string key, string targetBaseName)
        {
            if (!selectedShoutVisualSourcePaths.TryGetValue(key, out string? sourcePath)
                || string.IsNullOrWhiteSpace(sourcePath)
                || !File.Exists(sourcePath))
            {
                return;
            }

            string extension = Path.GetExtension(sourcePath);
            string destinationPath = Path.Combine(characterDirectory, targetBaseName + extension);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private void CopyShoutSfx(string characterDirectory, string key, string targetBaseName)
        {
            if (!selectedShoutSfxSourcePaths.TryGetValue(key, out string? sourcePath)
                || string.IsNullOrWhiteSpace(sourcePath)
                || !File.Exists(sourcePath))
            {
                return;
            }

            string extension = Path.GetExtension(sourcePath);
            string destinationPath = Path.Combine(characterDirectory, targetBaseName + extension);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private static string SanitizeFolderNameForRelativePath(string rawFolderName)
        {
            string value = (rawFolderName ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private static void AddOrUpdateShoutsCustomName(string charIniPath, string customName)
        {
            string valueToWrite = string.IsNullOrWhiteSpace(customName) ? "Custom" : customName.Trim();
            AddOrUpdateIniValue(charIniPath, "Shouts", "custom_name", valueToWrite);
        }

        private static void AddOrUpdateOptionsValue(string charIniPath, string key, string value)
        {
            AddOrUpdateIniValue(charIniPath, "Options", key, value);
        }

        private static void AddOrUpdateIniValue(string charIniPath, string section, string key, string value)
        {
            List<string> lines = File.ReadAllLines(charIniPath).ToList();
            string targetSectionHeader = "[" + section + "]";
            int sectionStart = lines.FindIndex(line =>
                string.Equals(line.Trim(), targetSectionHeader, StringComparison.OrdinalIgnoreCase));

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(targetSectionHeader);
                lines.Add($"{key}={value}");
                File.WriteAllLines(charIniPath, lines, Encoding.UTF8);
                return;
            }

            int sectionEnd = lines.Count;
            for (int i = sectionStart + 1; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    sectionEnd = i;
                    break;
                }
            }

            int keyLineIndex = -1;
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    keyLineIndex = i;
                    break;
                }
            }

            if (keyLineIndex >= 0)
            {
                lines[keyLineIndex] = $"{key}={value}";
            }
            else
            {
                lines.Insert(sectionEnd, $"{key}={value}");
            }

            File.WriteAllLines(charIniPath, lines, Encoding.UTF8);
        }

        private void RemoveAdvancedEntryButton_Click(object sender, RoutedEventArgs e)
        {
            if (AdvancedEntriesListBox.SelectedItem is not AdvancedEntryViewModel entry)
            {
                return;
            }

            advancedEntries.Remove(entry);
        }

        private void ApplySavedWindowState()
        {
            VisualizerWindowState state = SaveFile.Data.CharacterCreatorWindowState ?? new VisualizerWindowState
            {
                Width = 1220,
                Height = 760,
                IsMaximized = false
            };
            Width = Math.Max(MinWidth, state.Width);
            Height = Math.Max(MinHeight, state.Height);
            if (state.Left.HasValue && state.Top.HasValue)
            {
                Left = state.Left.Value;
                Top = state.Top.Value;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }

            if (state.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private VisualizerWindowState CaptureWindowState()
        {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            double capturedWidth = bounds.Width > 0 ? bounds.Width : Width;
            double capturedHeight = bounds.Height > 0 ? bounds.Height : Height;

            return new VisualizerWindowState
            {
                Width = Math.Max(MinWidth, capturedWidth),
                Height = Math.Max(MinHeight, capturedHeight),
                Left = bounds.X,
                Top = bounds.Y,
                IsMaximized = WindowState == WindowState.Maximized
            };
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            ApplyWorkAreaMaxBounds();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            StopBlipPreview();
            blipPreviewPlayer.Dispose();
            shoutSfxPreviewPlayer.Dispose();
            realizationSfxPreviewPlayer.Dispose();
            foreach (string key in shoutVisualPlayers.Keys.ToList())
            {
                StopShoutVisualPlayer(key);
            }
            SaveFile.Data.CharacterCreatorWindowState = CaptureWindowState();
            SaveFile.Save();
        }

        private void ApplyWorkAreaMaxBounds()
        {
            WindowChrome? chrome = WindowChrome.GetWindowChrome(this);
            if (WindowState == WindowState.Maximized)
            {
                if (chrome != null)
                {
                    chrome.ResizeBorderThickness = new Thickness(0);
                }

                WindowFrameBorder.BorderThickness = new Thickness(0);
                WindowFrameBorder.CornerRadius = new CornerRadius(0);
                return;
            }

            if (chrome != null)
            {
                chrome.ResizeBorderThickness = new Thickness(6);
            }

            WindowFrameBorder.BorderThickness = new Thickness(1);
            WindowFrameBorder.CornerRadius = new CornerRadius(5);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            CreatorMonitorInfo monitorInfo = new CreatorMonitorInfo();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            CreatorMinMaxInfo mmi = Marshal.PtrToStructure<CreatorMinMaxInfo>(lParam);
            CreatorRect workArea = monitorInfo.rcWork;
            CreatorRect monitorArea = monitorInfo.rcMonitor;

            mmi.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
            mmi.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
            mmi.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
            mmi.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            await WaitForm.ShowFormAsync("Generating character folder...", this);
            try
            {
                WaitForm.SetSubtitle("Validating setup...");
                SaveSelectedEmoteEditorValues();

                if (emotes.Count == 0)
                {
                    OceanyaMessageBox.Show(this, "Add at least one emote before generating.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string folderName = (CharacterFolderNameTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    OceanyaMessageBox.Show(this, "Character folder name is required.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string mountPath = (MountPathComboBox.SelectedItem as string) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(mountPath) && MountPathComboBox.Items.Count == 1)
                {
                    mountPath = MountPathComboBox.Items[0]?.ToString() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(mountPath))
                {
                    OceanyaMessageBox.Show(this, "No valid mount path is available.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                CharacterCreationProject project = new CharacterCreationProject
                {
                    MountPath = mountPath,
                    CharacterFolderName = folderName,
                    Name = folderName,
                    ShowName = (ShowNameTextBox.Text ?? string.Empty).Trim(),
                    Side = (SideComboBox.Text ?? string.Empty).Trim(),
                    Gender = (GenderBlipsDropdown.Text ?? string.Empty).Trim(),
                    Blips = (GenderBlipsDropdown.Text ?? string.Empty).Trim(),
                    Category = string.Empty,
                    Chat = (ChatDropdown.Text ?? string.Empty).Trim(),
                    Shouts = string.Empty,
                    Realization = (RealizationTextBox.Text ?? string.Empty).Trim(),
                    Effects = (EffectsDropdown.Text ?? string.Empty).Trim(),
                    Scaling = (ScalingDropdown.Text ?? string.Empty).Trim(),
                    Stretch = (StretchDropdown.Text ?? string.Empty).Trim(),
                    NeedsShowName = (NeedsShownameDropdown.Text ?? string.Empty).Trim(),
                    AssetFolders = assetFolders.ToList(),
                    AdvancedEntries = advancedEntries.Select(entry => new CharacterCreationAdvancedEntry
                    {
                        Section = entry.Section,
                        Key = entry.Key,
                        Value = entry.Value
                    }).ToList(),
                    Emotes = emotes.Select(static emote => emote.ToModel()).ToList()
                };

                bool usingCustomBlipFile = !string.IsNullOrWhiteSpace(selectedCustomBlipSourcePath)
                    && !string.IsNullOrWhiteSpace(customBlipOptionText)
                    && string.Equals((GenderBlipsDropdown.Text ?? string.Empty).Trim(), customBlipOptionText, StringComparison.OrdinalIgnoreCase);
                if (usingCustomBlipFile)
                {
                    string blipBaseName = Path.GetFileNameWithoutExtension(selectedCustomBlipSourcePath);
                    project.Blips = $"blips/{blipBaseName}";
                    project.Gender = project.Blips;
                }

                WaitForm.SetSubtitle("Writing char.ini and folder structure...");
                string characterDirectory = AOCharacterFileCreatorBuilder.CreateCharacterFolder(project);

                WaitForm.SetSubtitle("Copying shout assets...");
                CopyShoutAssets(characterDirectory);

                if (usingCustomBlipFile)
                {
                    WaitForm.SetSubtitle("Copying custom blip file...");
                    string extension = Path.GetExtension(selectedCustomBlipSourcePath);
                    string sourceFileName = Path.GetFileNameWithoutExtension(selectedCustomBlipSourcePath);
                    string blipsDirectory = Path.Combine(characterDirectory, "blips");
                    Directory.CreateDirectory(blipsDirectory);
                    string destinationPath = Path.Combine(blipsDirectory, sourceFileName + extension);
                    File.Copy(selectedCustomBlipSourcePath, destinationPath, overwrite: true);
                }

                bool hasCustomShoutFiles = selectedShoutVisualSourcePaths.ContainsKey("custom")
                    || selectedShoutSfxSourcePaths.ContainsKey("custom");
                string customShoutName = (CustomShoutNameTextBox.Text ?? string.Empty).Trim();
                if (hasCustomShoutFiles || !string.IsNullOrWhiteSpace(customShoutName))
                {
                    WaitForm.SetSubtitle("Adding custom shout settings...");
                    AddOrUpdateShoutsCustomName(Path.Combine(characterDirectory, "char.ini"), customShoutName);
                }

                if (!string.IsNullOrWhiteSpace(selectedRealizationSourcePath)
                    && string.Equals(
                        (RealizationTextBox.Text ?? string.Empty).Trim(),
                        Path.GetFileName(selectedRealizationSourcePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    WaitForm.SetSubtitle("Copying realization sound...");
                    string extension = Path.GetExtension(selectedRealizationSourcePath);
                    string destinationPath = Path.Combine(characterDirectory, "realization" + extension);
                    File.Copy(selectedRealizationSourcePath, destinationPath, overwrite: true);

                    string sanitizedFolder = SanitizeFolderNameForRelativePath(folderName);
                    string realizationToken = $"../../characters/{sanitizedFolder}/realization";
                    AddOrUpdateOptionsValue(Path.Combine(characterDirectory, "char.ini"), "realization", realizationToken);
                }

                WaitForm.SetSubtitle("Refreshing character index...");
                try
                {
                    CharacterFolder.RefreshCharacterList();
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning("Character folder creation succeeded, but character refresh failed.", ex);
                }

                StatusTextBlock.Text = "Created character folder: " + characterDirectory;
                OceanyaMessageBox.Show(
                    this,
                    "Character folder created successfully:\n" + characterDirectory,
                    "AO Character File Creator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to create AO character folder.", ex);
                OceanyaMessageBox.Show(
                    this,
                    "Failed to create character folder:\n" + ex.Message,
                    "Creation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                StopBlipPreview();
                Close();
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref CreatorMonitorInfo lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreatorPoint
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreatorMinMaxInfo
    {
        public CreatorPoint ptReserved;
        public CreatorPoint ptMaxSize;
        public CreatorPoint ptMaxPosition;
        public CreatorPoint ptMinTrackSize;
        public CreatorPoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreatorRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct CreatorMonitorInfo
    {
        public int cbSize;
        public CreatorRect rcMonitor;
        public CreatorRect rcWork;
        public int dwFlags;
    }

    public sealed class CharacterCreationEmoteViewModel : INotifyPropertyChanged
    {
        private int index;
        private string name = "Normal";

        public int Index
        {
            get => index;
            set
            {
                index = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string Name
        {
            get => name;
            set
            {
                name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string PreAnimation { get; set; } = "-";
        public string Animation { get; set; } = "normal";
        public int EmoteModifier { get; set; }
        public int DeskModifier { get; set; } = 1;
        public string SfxName { get; set; } = "1";
        public int SfxDelayMs { get; set; } = 1;
        public bool SfxLooping { get; set; }
        public int? PreAnimationDurationMs { get; set; }
        public int? StayTimeMs { get; set; }
        public string BlipsOverride { get; set; } = string.Empty;
        public ObservableCollection<FrameEventViewModel> FrameEvents { get; } = new ObservableCollection<FrameEventViewModel>();

        public string DisplayName => $"{Index}. {(string.IsNullOrWhiteSpace(Name) ? "Emote" : Name)}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshDisplayName()
        {
            OnPropertyChanged(nameof(DisplayName));
        }

        public CharacterCreationEmote ToModel()
        {
            return new CharacterCreationEmote
            {
                Name = Name?.Trim() ?? string.Empty,
                PreAnimation = (PreAnimation ?? string.Empty).Trim(),
                Animation = (Animation ?? string.Empty).Trim(),
                EmoteModifier = EmoteModifier,
                DeskModifier = DeskModifier,
                SfxName = (SfxName ?? string.Empty).Trim(),
                SfxDelayMs = Math.Max(0, SfxDelayMs),
                SfxLooping = SfxLooping,
                PreAnimationDurationMs = PreAnimationDurationMs,
                StayTimeMs = StayTimeMs,
                BlipsOverride = (BlipsOverride ?? string.Empty).Trim(),
                FrameEvents = FrameEvents.Select(static frameEvent => frameEvent.ToModel()).ToList()
            };
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FrameEventViewModel
    {
        public CharacterFrameTarget Target { get; set; } = CharacterFrameTarget.PreAnimation;
        public CharacterFrameEventType EventType { get; set; } = CharacterFrameEventType.Sfx;
        public int Frame { get; set; } = 1;
        public string Value { get; set; } = "1";
        public string CustomTargetPath { get; set; } = string.Empty;

        public string DisplayText
        {
            get
            {
                string targetText = Target == CharacterFrameTarget.Custom
                    ? "custom:" + (string.IsNullOrWhiteSpace(CustomTargetPath) ? "(empty)" : CustomTargetPath)
                    : Target.ToString();
                return $"{EventType} @ {Frame} [{targetText}] = {Value}";
            }
        }

        public CharacterCreationFrameEvent ToModel()
        {
            return new CharacterCreationFrameEvent
            {
                Target = Target,
                EventType = EventType,
                Frame = Math.Max(1, Frame),
                Value = Value?.Trim() ?? string.Empty,
                CustomTargetPath = CustomTargetPath?.Trim() ?? string.Empty
            };
        }
    }

    public sealed class AdvancedEntryViewModel
    {
        public string Section { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string DisplayText => $"[{Section}] {Key}={Value}";
    }
}
