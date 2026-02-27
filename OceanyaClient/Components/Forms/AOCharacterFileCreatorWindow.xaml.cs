using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private const int Ao2TimingTickMs = 40;
        private const double MinTileWidth = 320;
        private const double MaxTileWidth = 760;
        private const double MinTileHeight = 330;
        private const double MaxTileHeight = 820;

        public event Action? FinishedLoading;

        public static readonly DependencyProperty EmoteTileWidthProperty = DependencyProperty.Register(
            nameof(EmoteTileWidth),
            typeof(double),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(420d));

        public static readonly DependencyProperty EmoteTileHeightProperty = DependencyProperty.Register(
            nameof(EmoteTileHeight),
            typeof(double),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(430d));

        public static readonly DependencyProperty ResizePreviewWidthProperty = DependencyProperty.Register(
            nameof(ResizePreviewWidth),
            typeof(double),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(420d));

        public static readonly DependencyProperty ResizePreviewHeightProperty = DependencyProperty.Register(
            nameof(ResizePreviewHeight),
            typeof(double),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(430d));
        public static readonly DependencyProperty ResizeGuideLeftProperty = DependencyProperty.Register(
            nameof(ResizeGuideLeft),
            typeof(double),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(0d));
        public static readonly DependencyProperty ResizeGuideTopProperty = DependencyProperty.Register(
            nameof(ResizeGuideTop),
            typeof(double),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(0d));
        public static readonly DependencyProperty IsGlobalResizeGuideVisibleProperty = DependencyProperty.Register(
            nameof(IsGlobalResizeGuideVisible),
            typeof(bool),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(false));
        public static readonly DependencyProperty HasAnyEmotesProperty = DependencyProperty.Register(
            nameof(HasAnyEmotes),
            typeof(bool),
            typeof(AOCharacterFileCreatorWindow),
            new PropertyMetadata(false));

        private readonly ObservableCollection<CharacterCreationEmoteViewModel> emotes =
            new ObservableCollection<CharacterCreationEmoteViewModel>();
        private readonly ObservableCollection<EmoteTileEntryViewModel> emoteTiles =
            new ObservableCollection<EmoteTileEntryViewModel>();
        private readonly ObservableCollection<string> assetFolders = new ObservableCollection<string>();
        private readonly ObservableCollection<AdvancedEntryViewModel> advancedEntries =
            new ObservableCollection<AdvancedEntryViewModel>();
        private readonly ObservableCollection<string> mountPathOptions = new ObservableCollection<string>();
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
        private readonly Dictionary<string, IAnimationPlayer> emotePreviewPlayers = new Dictionary<string, IAnimationPlayer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DispatcherTimer> emotePreanimCutoffTimers = new Dictionary<string, DispatcherTimer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> shoutDefaultVisualPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyList<NamedIntOption> EmoteModifierOptions = new List<NamedIntOption>
        {
            new NamedIntOption(0, "Idle"),
            new NamedIntOption(1, "Preanimation"),
            new NamedIntOption(5, "Zoom"),
            new NamedIntOption(6, "Preanimation Zoom"),
            new NamedIntOption(2, "Legacy Objection Preanim"),
            new NamedIntOption(3, "Legacy Unknown"),
            new NamedIntOption(4, "Legacy Zoom Preanim Fix")
        };
        private static readonly IReadOnlyList<NamedIntOption> DeskModifierOptions = new List<NamedIntOption>
        {
            new NamedIntOption(0, "Hide Desk"),
            new NamedIntOption(1, "Show Desk"),
            new NamedIntOption(2, "Show Desk on Emote Only"),
            new NamedIntOption(3, "Show Desk on Preanim Only"),
            new NamedIntOption(4, "Expanded: Emote Only"),
            new NamedIntOption(5, "Expanded: Preanim Only")
        };
        private static readonly IReadOnlyList<string> FrameTargetOptionNames = Enum
            .GetValues(typeof(CharacterFrameTarget))
            .Cast<CharacterFrameTarget>()
            .Select(static value => value.ToString())
            .ToArray();
        private static readonly IReadOnlyList<string> FrameEventTypeOptionNames = Enum
            .GetValues(typeof(CharacterFrameEventType))
            .Cast<CharacterFrameEventType>()
            .Select(static value => value.ToString())
            .ToArray();
        private static readonly IReadOnlyList<string> ButtonIconModeOptionNames = new List<string>
        {
            "Single image",
            "Two images",
            "Automatic"
        };
        private static readonly IReadOnlyList<string> ButtonEffectsGenerationOptionNames = new List<string>
        {
            "Use asset as both versions",
            "Reduce opacity",
            "Darken",
            "Overlay"
        };
        private static readonly IReadOnlyDictionary<string, string> ButtonBackgroundPresetAssetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Oceanya Logo (preset)"] = "pack://application:,,,/OceanyaClient;component/Resources/Logo_O.png",
            ["Oceanya Full Logo (preset)"] = "pack://application:,,,/OceanyaClient;component/Resources/OceanyaFullLogo.png"
        };
        private const int EmoteModIdle = 0;
        private const int EmoteModPreanim = 1;
        private const int EmoteModZoom = 5;
        private const int EmoteModPreanimZoom = 6;
        private static readonly IReadOnlyList<string> ButtonIconsApplyScopeOptionNames = new List<string>
        {
            "All emotes",
            "Missing button_on or button_off",
            "No button config assigned"
        };
        private string activeSection = "setup";
        private string customBlipOptionText = string.Empty;
        private string selectedCustomBlipSourcePath = string.Empty;
        private string selectedRealizationSourcePath = string.Empty;
        private string selectedCharacterIconSourcePath = string.Empty;
        private CancellationTokenSource? blipPreviewCancellation;
        private Point emoteTileDragStartPoint;
        private EmoteTileEntryViewModel? pendingTileDragEntry;
        private EmoteTileEntryViewModel? currentDragTargetEntry;
        private bool suppressEmoteTileSelectionChanged;
        private CharacterCreationEmoteViewModel? activeResizeEmote;
        private FrameworkElement? activeResizeContainer;
        private string activeResizeMode = string.Empty;
        private double resizeStartWidth;
        private double resizeStartHeight;
        private DateTime suppressFieldSingleClickUntilUtc = DateTime.MinValue;
        private CharacterCreationEmoteViewModel? contextMenuTargetEmote;
        private string contextMenuTargetZone = string.Empty;
        private readonly Dictionary<Guid, BitmapSource> bulkButtonCutoutByEmoteId = new Dictionary<Guid, BitmapSource>();
        private ButtonIconGenerationConfig bulkButtonIconConfig = new ButtonIconGenerationConfig
        {
            Mode = ButtonIconMode.Automatic,
            EffectsMode = ButtonEffectsGenerationMode.Darken,
            DarknessPercent = 50,
            OpacityPercent = 75,
            AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.None
        };
        private string bulkButtonBackgroundUploadPath = string.Empty;
        private string bulkButtonOverlayUploadPath = string.Empty;

        private bool ShouldLoopEmotePreviews => LoopAnimationPreviewsCheckBox?.IsChecked != false;

        public double EmoteTileWidth
        {
            get => (double)GetValue(EmoteTileWidthProperty);
            set => SetValue(EmoteTileWidthProperty, value);
        }

        public double EmoteTileHeight
        {
            get => (double)GetValue(EmoteTileHeightProperty);
            set => SetValue(EmoteTileHeightProperty, value);
        }

        public double ResizePreviewWidth
        {
            get => (double)GetValue(ResizePreviewWidthProperty);
            set => SetValue(ResizePreviewWidthProperty, value);
        }

        public double ResizePreviewHeight
        {
            get => (double)GetValue(ResizePreviewHeightProperty);
            set => SetValue(ResizePreviewHeightProperty, value);
        }

        public double ResizeGuideLeft
        {
            get => (double)GetValue(ResizeGuideLeftProperty);
            set => SetValue(ResizeGuideLeftProperty, value);
        }

        public double ResizeGuideTop
        {
            get => (double)GetValue(ResizeGuideTopProperty);
            set => SetValue(ResizeGuideTopProperty, value);
        }

        public bool IsGlobalResizeGuideVisible
        {
            get => (bool)GetValue(IsGlobalResizeGuideVisibleProperty);
            set => SetValue(IsGlobalResizeGuideVisibleProperty, value);
        }

        public bool HasAnyEmotes
        {
            get => (bool)GetValue(HasAnyEmotesProperty);
            set => SetValue(HasAnyEmotesProperty, value);
        }

        public AOCharacterFileCreatorWindow()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);

            EmoteTilesListBox.ItemsSource = emoteTiles;
            AdvancedEntriesListBox.ItemsSource = advancedEntries;
            SideComboBox.ItemsSource = sideOptions;
            GenderBlipsDropdown.ItemsSource = blipOptions;
            ChatDropdown.ItemsSource = chatOptions;
            EffectsDropdown.ItemsSource = effectsFolderOptions;
            ScalingDropdown.ItemsSource = scalingOptions;
            StretchDropdown.ItemsSource = booleanOptions;
            NeedsShownameDropdown.ItemsSource = booleanOptions;

            FrameTargetComboBox.ItemsSource = FrameTargetOptionNames;
            FrameTypeComboBox.ItemsSource = FrameEventTypeOptionNames;
            ButtonIconsApplyScopeDropdown.ItemsSource = ButtonIconsApplyScopeOptionNames;
            ButtonIconsBackgroundDropdown.ItemsSource = GetAutomaticBackgroundOptions();
            ButtonIconsEffectsDropdown.ItemsSource = ButtonEffectsGenerationOptionNames;
            FrameTargetComboBox.Text = CharacterFrameTarget.PreAnimation.ToString();
            FrameTypeComboBox.Text = CharacterFrameEventType.Sfx.ToString();
            ButtonIconsApplyScopeDropdown.Text = ButtonIconsApplyScopeOptionNames[1];
            ButtonIconsBackgroundDropdown.Text = "None";
            ButtonIconsEffectsDropdown.Text = "Darken";
            ButtonIconsSolidColorPreview.Background = new SolidColorBrush(Colors.Transparent);
            ButtonIconsSolidColorHexTextBlock.Text = ToHexColor(Colors.Transparent);
            ButtonIconsDarkenSlider.Value = 50;
            ButtonIconsDarkenValueText.Text = "Darkness: 50%";
            ButtonIconsOpacitySlider.Value = 75;
            ButtonIconsOpacityValueText.Text = "Opacity: 75%";
            ButtonIconsBackgroundDropdown.TextValueChanged += ButtonIconsBackgroundDropdown_TextValueChanged;
            ButtonIconsEffectsDropdown.TextValueChanged += ButtonIconsEffectsDropdown_TextValueChanged;
            EmoteTileWidth = Math.Clamp(SaveFile.Data.CharacterCreatorEmoteTileWidth, MinTileWidth, MaxTileWidth);
            EmoteTileHeight = Math.Clamp(SaveFile.Data.CharacterCreatorEmoteTileHeight, MinTileHeight, MaxTileHeight);
            ResizePreviewWidth = EmoteTileWidth;
            ResizePreviewHeight = EmoteTileHeight;

            PopulateMountPaths();
            PopulateDefaultSideBlipChatAndEffectsOptions();
            InitializeCharacterDefaults();
            InitializeShoutPreviewDefaults();
            SetActiveSection("setup");
            ApplySavedWindowState();
            ApplySavedPreviewVolume();
            RefreshChatPreview();
            ResetBulkButtonIconsConfigToDefaults();
            RefreshEmoteTiles();
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
            mountPathOptions.Clear();
            List<string> mountPaths = new List<string>(Globals.BaseFolders ?? new List<string>());
            if (mountPaths.Count == 0)
            {
                string fallback = Path.GetDirectoryName(Globals.PathToConfigINI ?? string.Empty) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    mountPaths.Add(fallback);
                }
            }

            foreach (string mountPath in mountPaths)
            {
                mountPathOptions.Add(mountPath);
            }

            MountPathComboBox.ItemsSource = mountPathOptions;
            if (mountPathOptions.Count > 0)
            {
                MountPathComboBox.Text = mountPathOptions[0];
            }

            bool autoSelected = mountPathOptions.Count == 1;
            MountPathAutoPanel.Visibility = autoSelected ? Visibility.Visible : Visibility.Collapsed;
            MountPathSelectPanel.Visibility = autoSelected ? Visibility.Collapsed : Visibility.Visible;
            MountPathResolvedTextBlock.Text = autoSelected
                ? "The character folder will be created in: " + BuildCharactersDirectoryDisplayPath(mountPathOptions[0])
                : "Select where the new character folder should be created.";
            if (mountPathOptions.Count == 0)
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
            selectedCharacterIconSourcePath = string.Empty;
            CharacterIconPreviewImage.Source = null;
            CharacterIconEmptyText.Visibility = Visibility.Visible;
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
            SelectEmoteTile(emote);
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

        private void MountPathComboBox_TextValueChanged(object? sender, EventArgs e)
        {
            string mountPath = (MountPathComboBox.Text ?? string.Empty).Trim();
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
            ButtonIconsSectionPanel.Visibility = string.Equals(section, "buttonicons", StringComparison.OrdinalIgnoreCase)
                && IsButtonIconsSectionRequired()
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
            else if (string.Equals(section, "buttonicons", StringComparison.OrdinalIgnoreCase))
            {
                StatusTextBlock.Text = "Step 4: Bulk-generate missing button icons.";
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
            ApplySectionButtonStyle(
                SectionButtonIconsButton,
                string.Equals(activeSection, "buttonicons", StringComparison.OrdinalIgnoreCase));
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

        private bool IsButtonIconsSectionRequired()
        {
            IReadOnlyList<CharacterCreationEmoteViewModel> eligibleEmotes = GetEligibleEmotesForButtonIcons();
            if (eligibleEmotes.Count == 0)
            {
                return false;
            }

            foreach (CharacterCreationEmoteViewModel emote in eligibleEmotes)
            {
                if (!HasUsableButtonPair(emote))
                {
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<CharacterCreationEmoteViewModel> GetEligibleEmotesForButtonIcons()
        {
            return emotes.Where(static emote => HasAnyRealAssetSet(emote)).ToList();
        }

        private static bool HasAnyRealAssetSet(CharacterCreationEmoteViewModel emote)
        {
            return !string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.SfxAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonIconAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonSingleImageAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonTwoImagesOnAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonTwoImagesOffAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonEffectsOverlayAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonAutomaticBackgroundUploadAssetSourcePath)
                || emote.ButtonAutomaticCutEmoteImage != null;
        }

        private static bool HasUsableButtonPair(CharacterCreationEmoteViewModel emote)
        {
            ButtonIconGenerationConfig config = BuildButtonIconGenerationConfig(emote);
            return TryBuildButtonIconPair(config, out BitmapSource? onImage, out BitmapSource? offImage, out _)
                && onImage != null
                && offImage != null;
        }

        private void UpdateButtonIconsSectionVisibility()
        {
            bool required = IsButtonIconsSectionRequired();
            SectionButtonIconsButton.Visibility = required ? Visibility.Visible : Visibility.Collapsed;
            if (!required)
            {
                ButtonIconsSectionPanel.Visibility = Visibility.Collapsed;
                if (string.Equals(activeSection, "buttonicons", StringComparison.OrdinalIgnoreCase))
                {
                    SetActiveSection("emotes");
                }
            }
            else
            {
                ButtonIconsSectionPanel.Visibility = string.Equals(activeSection, "buttonicons", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
                return;
            }

            if (string.Equals(activeSection, "buttonicons", StringComparison.OrdinalIgnoreCase))
            {
                ResetBulkButtonIconsConfigToDefaults();
                StatusTextBlock.Text = "Button Icons tab reset to defaults.";
            }
        }

        private void DefaultAllButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSetupToDefaults();
            ResetEffectsToDefaults();
            ResetEmotesToDefaults();
            ResetBulkButtonIconsConfigToDefaults();
            ResetFrameAndAdvancedToDefaults();
            StatusTextBlock.Text = "All tabs reset to defaults.";
        }

        private void ResetSetupToDefaults()
        {
            CharacterFolderNameTextBox.Text = "new_character";
            ShowNameTextBox.Text = "New Character";
            selectedCharacterIconSourcePath = string.Empty;
            CharacterIconPreviewImage.Source = null;
            CharacterIconEmptyText.Visibility = Visibility.Visible;
            SideComboBox.Text = "wit";
            SetBlipText(blipOptions.FirstOrDefault() ?? string.Empty);
        }

        private void ResetEmotesToDefaults()
        {
            SaveSelectedEmoteEditorValues();
            StopAllEmotePreviewPlayers();
            emotes.Clear();
            bulkButtonCutoutByEmoteId.Clear();
            RenderBulkCutoutPreviewTiles(Array.Empty<CharacterCreationEmoteViewModel>());
            RefreshEmoteLabels();
        }

        private void ResetFrameAndAdvancedToDefaults()
        {
            advancedEntries.Clear();
            FrameTargetComboBox.Text = CharacterFrameTarget.PreAnimation.ToString();
            FrameTypeComboBox.Text = CharacterFrameEventType.Sfx.ToString();
            FrameNumberForDelayTextBox.Text = "1";
            FramesPerSecondTextBox.Text = "60";
            FrameNumberTextBox.Text = "1";
            FrameValueTextBox.Text = "1";
            CustomFrameTargetTextBox.Text = "anim/custom";
            FrameToDelayResultTextBlock.Text = string.Empty;
        }

        private void ResetBulkButtonIconsConfigToDefaults()
        {
            bulkButtonIconConfig = new ButtonIconGenerationConfig
            {
                Mode = ButtonIconMode.Automatic,
                EffectsMode = ButtonEffectsGenerationMode.Darken,
                DarknessPercent = 50,
                OpacityPercent = 75,
                AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.None,
                AutomaticBackgroundPreset = ButtonBackgroundPresetAssetMap.Keys.FirstOrDefault() ?? "Oceanya Logo (preset)",
                AutomaticSolidColor = Colors.Transparent
            };
            bulkButtonBackgroundUploadPath = string.Empty;
            bulkButtonOverlayUploadPath = string.Empty;
            bulkButtonCutoutByEmoteId.Clear();
            ButtonIconsApplyScopeDropdown.Text = ButtonIconsApplyScopeOptionNames[1];
            ButtonIconsBackgroundDropdown.Text = "None";
            ButtonIconsEffectsDropdown.Text = "Darken";
            UpdateBulkButtonIconsPanels();
            RenderBulkCutoutPreviewTiles(Array.Empty<CharacterCreationEmoteViewModel>());
        }

        private void UpdateBulkButtonIconsPanels()
        {
            string backgroundSelection = (ButtonIconsBackgroundDropdown.Text ?? string.Empty).Trim();
            ApplyAutomaticBackgroundSelection(backgroundSelection, bulkButtonIconConfig);
            bulkButtonIconConfig.AutomaticBackgroundUploadPath = string.IsNullOrWhiteSpace(bulkButtonBackgroundUploadPath)
                ? null
                : bulkButtonBackgroundUploadPath;

            string effectsSelection = (ButtonIconsEffectsDropdown.Text ?? string.Empty).Trim();
            bulkButtonIconConfig.EffectsMode = ParseButtonEffectsGenerationMode(effectsSelection);
            bulkButtonIconConfig.OverlayImagePath = string.IsNullOrWhiteSpace(bulkButtonOverlayUploadPath)
                ? null
                : bulkButtonOverlayUploadPath;

            ButtonIconsPresetPreviewCard.Visibility = bulkButtonIconConfig.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.PresetList
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (ButtonIconsPresetPreviewCard.Visibility == Visibility.Visible)
            {
                string path = ResolveBackgroundPresetPath(bulkButtonIconConfig.AutomaticBackgroundPreset);
                if (TryLoadButtonBitmap(path, out BitmapSource? presetImage, out _) && presetImage != null)
                {
                    ButtonIconsPresetPreviewImage.Source = presetImage;
                }
            }

            ButtonIconsSolidColorPanel.Visibility = bulkButtonIconConfig.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.SolidColor
                ? Visibility.Visible
                : Visibility.Collapsed;
            ButtonIconsUploadPanel.Visibility = bulkButtonIconConfig.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.Upload
                ? Visibility.Visible
                : Visibility.Collapsed;

            ButtonIconsSolidColorPreview.Background = new SolidColorBrush(bulkButtonIconConfig.AutomaticSolidColor);
            ButtonIconsSolidColorHexTextBlock.Text = ToHexColor(bulkButtonIconConfig.AutomaticSolidColor);
            ButtonIconsBackgroundUploadTextBlock.Text = string.IsNullOrWhiteSpace(bulkButtonBackgroundUploadPath)
                ? "No file selected"
                : Path.GetFileName(bulkButtonBackgroundUploadPath);

            ButtonIconsDarkenPanel.Visibility = bulkButtonIconConfig.EffectsMode == ButtonEffectsGenerationMode.Darken
                ? Visibility.Visible
                : Visibility.Collapsed;
            ButtonIconsOpacityPanel.Visibility = bulkButtonIconConfig.EffectsMode == ButtonEffectsGenerationMode.ReduceOpacity
                ? Visibility.Visible
                : Visibility.Collapsed;
            ButtonIconsOverlayPanel.Visibility = bulkButtonIconConfig.EffectsMode == ButtonEffectsGenerationMode.Overlay
                ? Visibility.Visible
                : Visibility.Collapsed;
            ButtonIconsDarkenSlider.Value = Math.Clamp(bulkButtonIconConfig.DarknessPercent, 0, 100);
            ButtonIconsOpacitySlider.Value = Math.Clamp(bulkButtonIconConfig.OpacityPercent, 0, 100);
            ButtonIconsDarkenValueText.Text = $"Darkness: {Math.Clamp(bulkButtonIconConfig.DarknessPercent, 0, 100)}%";
            ButtonIconsOpacityValueText.Text = $"Opacity: {Math.Clamp(bulkButtonIconConfig.OpacityPercent, 0, 100)}%";
            ButtonIconsOverlayPathTextBlock.Text = string.IsNullOrWhiteSpace(bulkButtonOverlayUploadPath)
                ? "No overlay selected"
                : Path.GetFileName(bulkButtonOverlayUploadPath);
        }

        private void ButtonIconsBackgroundDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            UpdateBulkButtonIconsPanels();
        }

        private void ButtonIconsEffectsDropdown_TextValueChanged(object? sender, EventArgs e)
        {
            UpdateBulkButtonIconsPanels();
        }

        private void ButtonIconsDarkenSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            bulkButtonIconConfig.DarknessPercent = (int)Math.Round(ButtonIconsDarkenSlider.Value);
            ButtonIconsDarkenValueText.Text = $"Darkness: {bulkButtonIconConfig.DarknessPercent}%";
        }

        private void ButtonIconsOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            bulkButtonIconConfig.OpacityPercent = (int)Math.Round(ButtonIconsOpacitySlider.Value);
            ButtonIconsOpacityValueText.Text = $"Opacity: {bulkButtonIconConfig.OpacityPercent}%";
        }

        private void ButtonIconsPickColorButton_Click(object sender, RoutedEventArgs e)
        {
            Color? picked = ShowAdvancedColorPickerDialog(bulkButtonIconConfig.AutomaticSolidColor);
            if (!picked.HasValue)
            {
                return;
            }

            bulkButtonIconConfig.AutomaticSolidColor = picked.Value;
            UpdateBulkButtonIconsPanels();
        }

        private void ButtonIconsBackgroundUploadButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog picker = new OpenFileDialog
            {
                Title = "Select automatic background image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
            };
            if (picker.ShowDialog() != true)
            {
                return;
            }

            bulkButtonBackgroundUploadPath = picker.FileName;
            bulkButtonIconConfig.AutomaticBackgroundUploadPath = bulkButtonBackgroundUploadPath;
            UpdateBulkButtonIconsPanels();
        }

        private void ButtonIconsOverlayUploadButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog picker = new OpenFileDialog
            {
                Title = "Select overlay image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
            };
            if (picker.ShowDialog() != true)
            {
                return;
            }

            bulkButtonOverlayUploadPath = picker.FileName;
            bulkButtonIconConfig.OverlayImagePath = bulkButtonOverlayUploadPath;
            UpdateBulkButtonIconsPanels();
        }

        private IReadOnlyList<CharacterCreationEmoteViewModel> ResolveBulkButtonTargetEmotes()
        {
            string scope = (ButtonIconsApplyScopeDropdown.Text ?? string.Empty).Trim();
            IReadOnlyList<CharacterCreationEmoteViewModel> eligibleEmotes = GetEligibleEmotesForButtonIcons();
            if (string.Equals(scope, "All emotes", StringComparison.OrdinalIgnoreCase))
            {
                return eligibleEmotes;
            }

            if (string.Equals(scope, "No button config assigned", StringComparison.OrdinalIgnoreCase))
            {
                return eligibleEmotes.Where(static emote => !HasAnyButtonConfigAssigned(emote)).ToList();
            }

            return eligibleEmotes.Where(static emote => !HasUsableButtonPair(emote)).ToList();
        }

        private static bool HasAnyButtonConfigAssigned(CharacterCreationEmoteViewModel emote)
        {
            return !string.IsNullOrWhiteSpace(emote.ButtonSingleImageAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonTwoImagesOnAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonTwoImagesOffAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.ButtonIconAssetSourcePath)
                || emote.ButtonAutomaticCutEmoteImage != null
                || !string.IsNullOrWhiteSpace(emote.ButtonAutomaticBackgroundUploadAssetSourcePath);
        }

        private void ButtonIconsBulkCuttingButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<CharacterCreationEmoteViewModel> targets = ResolveBulkButtonTargetEmotes();
            if (targets.Count == 0)
            {
                OceanyaMessageBox.Show(
                    this,
                    "No emotes matched the selected scope.",
                    "Button Icons",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            BitmapSource? lastCutout = null;
            foreach (CharacterCreationEmoteViewModel emote in targets)
            {
                bulkButtonCutoutByEmoteId.TryGetValue(emote.Id, out BitmapSource? existingCutout);
                BitmapSource? cutout = ShowEmoteCuttingDialog(emote, existingCutout ?? lastCutout);
                if (cutout == null)
                {
                    continue;
                }

                bulkButtonCutoutByEmoteId[emote.Id] = cutout;
                lastCutout = cutout;
            }

            RenderBulkCutoutPreviewTiles(targets);
            StatusTextBlock.Text = "Bulk emote cutting updated.";
        }

        private void RenderBulkCutoutPreviewTiles(IReadOnlyList<CharacterCreationEmoteViewModel> targets)
        {
            ButtonIconsCutoutPreviewWrapPanel.Children.Clear();
            foreach (CharacterCreationEmoteViewModel emote in targets)
            {
                if (!bulkButtonCutoutByEmoteId.TryGetValue(emote.Id, out BitmapSource? cutout) || cutout == null)
                {
                    continue;
                }

                Border tile = new Border
                {
                    Width = 64,
                    Height = 64,
                    Margin = new Thickness(0, 0, 6, 6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(72, 92, 112)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb(110, 20, 20, 20)),
                    ToolTip = emote.EmoteHeader + " - " + emote.Name
                };
                tile.Child = new Image
                {
                    Source = cutout,
                    Stretch = Stretch.Uniform
                };
                ButtonIconsCutoutPreviewWrapPanel.Children.Add(tile);
            }
        }

        private void ButtonIconsApplyButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<CharacterCreationEmoteViewModel> targets = ResolveBulkButtonTargetEmotes();
            if (targets.Count == 0)
            {
                OceanyaMessageBox.Show(
                    this,
                    "No emotes matched the selected scope.",
                    "Button Icons",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int appliedCount = 0;
            foreach (CharacterCreationEmoteViewModel emote in targets)
            {
                ButtonIconGenerationConfig config = new ButtonIconGenerationConfig
                {
                    Mode = ButtonIconMode.Automatic,
                    EffectsMode = bulkButtonIconConfig.EffectsMode,
                    OpacityPercent = bulkButtonIconConfig.OpacityPercent,
                    DarknessPercent = bulkButtonIconConfig.DarknessPercent,
                    OverlayImagePath = bulkButtonIconConfig.OverlayImagePath,
                    AutomaticBackgroundMode = bulkButtonIconConfig.AutomaticBackgroundMode,
                    AutomaticBackgroundPreset = bulkButtonIconConfig.AutomaticBackgroundPreset,
                    AutomaticSolidColor = bulkButtonIconConfig.AutomaticSolidColor,
                    AutomaticBackgroundUploadPath = bulkButtonIconConfig.AutomaticBackgroundUploadPath,
                    AutomaticCutEmoteImage = bulkButtonCutoutByEmoteId.TryGetValue(emote.Id, out BitmapSource? cutout)
                        ? CloneBitmapSource(cutout)
                        : null
                };

                ApplyButtonIconGenerationConfig(emote, config);
                if (TryBuildButtonIconPair(config, out BitmapSource? onImage, out _, out _)
                    && onImage != null)
                {
                    emote.ButtonIconPreview = onImage;
                    emote.ButtonIconAssetSourcePath = ResolveRepresentativeButtonSourcePath(config);
                    emote.ButtonIconToken = $"button_{emote.Index}";
                    appliedCount++;
                }
            }

            UpdateButtonIconsSectionVisibility();
            StatusTextBlock.Text = $"Applied automatic button config to {appliedCount} emotes.";
            RefreshEmoteTiles(GetSelectedEmote());
        }

        private void EmoteTilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressEmoteTileSelectionChanged)
            {
                return;
            }

            SaveSelectedEmoteEditorValues();

            if (EmoteTilesListBox.SelectedItem is not EmoteTileEntryViewModel selectedEntry)
            {
                UpdateTileSelectionStates(null);
                LoadSelectedEmoteEditorValues();
                UpdateSelectedEmoteHeader();
                return;
            }

            UpdateTileSelectionStates(selectedEntry.Emote);
            RestartEmotePreviewPlayers(selectedEntry.Emote);
            LoadSelectedEmoteEditorValues();
            UpdateSelectedEmoteHeader();
        }

        private CharacterCreationEmoteViewModel? GetSelectedEmote()
        {
            return EmoteTilesListBox.SelectedItem is EmoteTileEntryViewModel selectedEntry
                ? selectedEntry.Emote
                : null;
        }

        private void LoadSelectedEmoteEditorValues()
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            bool enabled = selected != null;
            SetEmoteEditorEnabled(enabled);
            FrameEventListBox.ItemsSource = selected?.FrameEvents;
        }

        private void SetEmoteEditorEnabled(bool enabled)
        {
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
            if (selected != null)
            {
                selected.Name = (selected.Name ?? string.Empty).Trim();
                selected.PreAnimation = (selected.PreAnimation ?? string.Empty).Trim();
                selected.Animation = (selected.Animation ?? string.Empty).Trim();
                selected.SfxName = (selected.SfxName ?? string.Empty).Trim();
                selected.BlipsOverride = (selected.BlipsOverride ?? string.Empty).Trim();
                selected.RefreshDisplayName();
                selected.RefreshSfxSummary();
            }
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

        private static CharacterFrameTarget ParseFrameTargetOrDefault(string? raw, CharacterFrameTarget fallback)
        {
            return Enum.TryParse((raw ?? string.Empty).Trim(), ignoreCase: true, out CharacterFrameTarget parsed)
                ? parsed
                : fallback;
        }

        private static CharacterFrameEventType ParseFrameEventTypeOrDefault(string? raw, CharacterFrameEventType fallback)
        {
            return Enum.TryParse((raw ?? string.Empty).Trim(), ignoreCase: true, out CharacterFrameEventType parsed)
                ? parsed
                : fallback;
        }

        private void RefreshEmoteLabels()
        {
            for (int i = 0; i < emotes.Count; i++)
            {
                emotes[i].Index = i + 1;
                emotes[i].RefreshDisplayName();
            }

            RefreshEmoteTiles();
        }

        private void AddEmoteButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedEmoteEditorValues();
            CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
            {
                Name = "Emote " + (emotes.Count + 1),
                PreAnimation = "-",
                Animation = string.Empty,
                EmoteModifier = 0,
                DeskModifier = 1,
                SfxName = "1",
                SfxDelayMs = 1
            };
            emotes.Add(emote);
            RefreshEmoteLabels();
            SelectEmoteTile(emote);
            StatusTextBlock.Text = "Emote added.";
        }

        private void RemoveEmoteButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            int index = emotes.IndexOf(selected);
            StopEmotePreviewPlayers(selected);
            emotes.Remove(selected);
            RefreshEmoteLabels();
            if (emotes.Count > 0)
            {
                SelectEmoteTile(emotes[Math.Clamp(index, 0, emotes.Count - 1)]);
            }
            StatusTextBlock.Text = "Emote removed.";
        }

        private void MoveEmoteUpButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            int index = emotes.IndexOf(selected);
            if (index <= 0)
            {
                return;
            }

            CharacterCreationEmoteViewModel item = emotes[index];
            emotes.RemoveAt(index);
            emotes.Insert(index - 1, item);
            RefreshEmoteLabels();
            SelectEmoteTile(item);
        }

        private void MoveEmoteDownButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            int index = emotes.IndexOf(selected);
            if (index < 0 || index >= emotes.Count - 1)
            {
                return;
            }

            CharacterCreationEmoteViewModel item = emotes[index];
            emotes.RemoveAt(index);
            emotes.Insert(index + 1, item);
            RefreshEmoteLabels();
            SelectEmoteTile(item);
        }

        private void AddFrameEventButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected == null)
            {
                return;
            }

            CharacterFrameTarget target = ParseFrameTargetOrDefault(FrameTargetComboBox.Text, CharacterFrameTarget.PreAnimation);
            CharacterFrameEventType eventType = ParseFrameEventTypeOrDefault(FrameTypeComboBox.Text, CharacterFrameEventType.Sfx);

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
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            if (selected != null)
            {
                selected.SfxDelayMs = milliseconds;
                selected.RefreshSfxSummary();
            }
            SaveSelectedEmoteEditorValues();
            StatusTextBlock.Text = "Applied frame timing conversion to selected emote SFX delay.";
        }

        private void RefreshEmoteTiles(CharacterCreationEmoteViewModel? preferredSelection = null)
        {
            CharacterCreationEmoteViewModel? selectedEmote = preferredSelection ?? GetSelectedEmote();
            suppressEmoteTileSelectionChanged = true;
            try
            {
                emoteTiles.Clear();
                foreach (CharacterCreationEmoteViewModel emote in emotes)
                {
                    emoteTiles.Add(new EmoteTileEntryViewModel
                    {
                        Emote = emote
                    });
                }

                if (selectedEmote != null)
                {
                    SelectEmoteTile(selectedEmote);
                }
                else if (emotes.Count > 0)
                {
                    SelectEmoteTile(emotes[0]);
                }
                else
                {
                    EmoteTilesListBox.SelectedItem = null;
                    UpdateTileSelectionStates(null);
                }

                HasAnyEmotes = emotes.Count > 0;
                UpdateButtonIconsSectionVisibility();
            }
            finally
            {
                suppressEmoteTileSelectionChanged = false;
            }

            UpdateSelectedEmoteHeader();
        }

        private void SelectEmoteTile(CharacterCreationEmoteViewModel? emote)
        {
            if (emote == null)
            {
                EmoteTilesListBox.SelectedItem = null;
                UpdateTileSelectionStates(null);
                return;
            }

            EmoteTileEntryViewModel? entry = emoteTiles.FirstOrDefault(tile => tile.Emote == emote);
            bool previousSuppression = suppressEmoteTileSelectionChanged;
            suppressEmoteTileSelectionChanged = true;
            EmoteTilesListBox.SelectedItem = entry;
            EmoteTilesListBox.ScrollIntoView(entry);
            suppressEmoteTileSelectionChanged = previousSuppression;
            UpdateTileSelectionStates(emote);
        }

        private void UpdateTileSelectionStates(CharacterCreationEmoteViewModel? selected)
        {
            foreach (EmoteTileEntryViewModel tile in emoteTiles)
            {
                if (tile.Emote != null)
                {
                    tile.Emote.IsSelected = ReferenceEquals(tile.Emote, selected);
                }
            }
        }

        private void UpdateSelectedEmoteHeader()
        {
            CharacterCreationEmoteViewModel? selected = GetSelectedEmote();
            SelectedEmoteHeaderTextBlock.Text = selected == null
                ? "No emote selected"
                : $"Selected: {selected.EmoteHeader} - {(string.IsNullOrWhiteSpace(selected.Name) ? "(unnamed)" : selected.Name)}";
        }

        private static void ClearDropZoneHighlights(CharacterCreationEmoteViewModel emote)
        {
            emote.IsPreDropActive = false;
            emote.IsAnimationDropActive = false;
            emote.IsButtonDropActive = false;
        }

        private static void SetDropZoneHighlight(CharacterCreationEmoteViewModel emote, string zone, bool isActive)
        {
            if (string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                emote.IsPreDropActive = isActive;
            }
            else if (string.Equals(zone, "anim", StringComparison.OrdinalIgnoreCase))
            {
                emote.IsAnimationDropActive = isActive;
            }
            else if (string.Equals(zone, "button", StringComparison.OrdinalIgnoreCase))
            {
                emote.IsButtonDropActive = isActive;
            }
        }

        private void AddEmoteTileBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            AddEmoteButton_Click(sender, new RoutedEventArgs());
        }

        private void AddEmoteToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            AddEmoteButton_Click(sender, e);
        }

        private void DeleteEmoteToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveEmoteButton_Click(sender, e);
        }

        private void MoveUpEmoteToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            MoveEmoteUpButton_Click(sender, e);
        }

        private void MoveDownEmoteToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            MoveEmoteDownButton_Click(sender, e);
        }

        private void EmoteTilesListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            emoteTileDragStartPoint = e.GetPosition(EmoteTilesListBox);

            DependencyObject source = e.OriginalSource as DependencyObject ?? EmoteTilesListBox;
            if (FindAncestor<TextBox>(source) != null
                || FindAncestor<Button>(source) != null
                || FindAncestor<Thumb>(source) != null)
            {
                pendingTileDragEntry = null;
                return;
            }

            ListBoxItem? item = FindAncestor<ListBoxItem>(source);
            if (item?.DataContext is EmoteTileEntryViewModel entry && !entry.IsAddTile)
            {
                pendingTileDragEntry = entry;
            }
            else
            {
                pendingTileDragEntry = null;
            }
        }

        private void EmoteTilesListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || pendingTileDragEntry == null)
            {
                return;
            }

            Point currentPosition = e.GetPosition(EmoteTilesListBox);
            if (Math.Abs(currentPosition.X - emoteTileDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(currentPosition.Y - emoteTileDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            EmoteTileEntryViewModel dragEntry = pendingTileDragEntry;
            pendingTileDragEntry = null;
            DragDrop.DoDragDrop(EmoteTilesListBox, dragEntry, DragDropEffects.Move);
            ClearCurrentDragTarget();
        }

        private void EmoteTilesListBox_DragOver(object sender, DragEventArgs e)
        {
            if (TryGetDroppedFilePaths(e, out IReadOnlyList<string>? droppedPaths)
                && droppedPaths.Any(IsImageAsset))
            {
                SetCurrentDragTarget(null);
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(typeof(EmoteTileEntryViewModel)))
            {
                SetCurrentDragTarget(null);
                e.Effects = DragDropEffects.None;
                return;
            }

            EmoteTileEntryViewModel? target = ResolveTileEntryFromEvent(e);
            SetCurrentDragTarget(target);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void EmoteTilesListBox_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (TryGetDroppedFilePaths(e, out IReadOnlyList<string>? droppedPaths))
                {
                    List<string> imagePaths = droppedPaths.Where(IsImageAsset).ToList();
                    if (imagePaths.Count > 0)
                    {
                        AddEmotesFromDroppedImages(imagePaths);
                        e.Handled = true;
                        return;
                    }
                }

                if (!e.Data.GetDataPresent(typeof(EmoteTileEntryViewModel)))
                {
                    return;
                }

                EmoteTileEntryViewModel? sourceEntry = e.Data.GetData(typeof(EmoteTileEntryViewModel)) as EmoteTileEntryViewModel;
                if (sourceEntry?.Emote == null)
                {
                    return;
                }

                EmoteTileEntryViewModel? targetEntry = ResolveTileEntryFromEvent(e);
                MoveEmoteByDragAndDrop(sourceEntry.Emote, targetEntry);
            }
            finally
            {
                ClearCurrentDragTarget();
            }
        }

        private void AddEmoteFromDroppedImage(string sourcePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
            {
                Name = string.IsNullOrWhiteSpace(fileName) ? $"Emote {emotes.Count + 1}" : fileName,
                PreAnimation = "-",
                Animation = fileName,
                EmoteModifier = 0,
                DeskModifier = 1,
                SfxName = "1",
                SfxDelayMs = 1,
                AnimationAssetSourcePath = sourcePath,
                AnimationPreview = TryLoadPreviewImage(sourcePath)
            };

            emotes.Add(emote);
            RefreshEmoteLabels();
            SelectEmoteTile(emote);
            UpdateAnimatedPreviewPlayer(emote, "anim", emote.AnimationAssetSourcePath);
            RestartEmotePreviewPlayers(emote);
            StatusTextBlock.Text = "Emote created from dropped image.";
        }

        private void AddEmotesFromDroppedImages(IReadOnlyList<string> sourcePaths)
        {
            List<CharacterCreationEmoteViewModel> created = new List<CharacterCreationEmoteViewModel>();
            foreach (string sourcePath in sourcePaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(sourcePath);
                CharacterCreationEmoteViewModel emote = new CharacterCreationEmoteViewModel
                {
                    Name = string.IsNullOrWhiteSpace(fileName) ? $"Emote {emotes.Count + created.Count + 1}" : fileName,
                    PreAnimation = "-",
                    Animation = fileName,
                    EmoteModifier = 0,
                    DeskModifier = 1,
                    SfxName = "1",
                    SfxDelayMs = 1,
                    AnimationAssetSourcePath = sourcePath,
                    AnimationPreview = TryLoadPreviewImage(sourcePath)
                };

                emotes.Add(emote);
                created.Add(emote);
            }

            if (created.Count == 0)
            {
                return;
            }

            RefreshEmoteLabels();
            foreach (CharacterCreationEmoteViewModel emote in created)
            {
                UpdateAnimatedPreviewPlayer(emote, "anim", emote.AnimationAssetSourcePath);
            }

            CharacterCreationEmoteViewModel selected = created[^1];
            SelectEmoteTile(selected);
            RestartEmotePreviewPlayers(selected);
            StatusTextBlock.Text = created.Count == 1
                ? "Emote created from dropped image."
                : $"Created {created.Count} emotes from dropped images.";
        }

        private void MoveEmoteByDragAndDrop(CharacterCreationEmoteViewModel sourceEmote, EmoteTileEntryViewModel? targetEntry)
        {
            int sourceIndex = emotes.IndexOf(sourceEmote);
            if (sourceIndex < 0)
            {
                return;
            }

            int targetIndex = targetEntry?.Emote != null
                ? emotes.IndexOf(targetEntry.Emote)
                : emotes.Count - 1;
            if (targetEntry?.IsAddTile == true)
            {
                targetIndex = 0;
            }

            if (targetIndex < 0)
            {
                targetIndex = emotes.Count - 1;
            }

            if (sourceIndex == targetIndex)
            {
                return;
            }

            CharacterCreationEmoteViewModel emote = emotes[sourceIndex];
            emotes.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            targetIndex = Math.Clamp(targetIndex, 0, emotes.Count);
            emotes.Insert(targetIndex, emote);
            RefreshEmoteLabels();
            SelectEmoteTile(emote);
            StatusTextBlock.Text = "Emote reordered.";
        }

        private EmoteTileEntryViewModel? ResolveTileEntryFromEvent(DragEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject ?? EmoteTilesListBox;
            ListBoxItem? item = FindAncestor<ListBoxItem>(source);
            return item?.DataContext as EmoteTileEntryViewModel;
        }

        private void SetCurrentDragTarget(EmoteTileEntryViewModel? target)
        {
            if (ReferenceEquals(currentDragTargetEntry, target))
            {
                return;
            }

            if (currentDragTargetEntry?.Emote != null)
            {
                currentDragTargetEntry.Emote.IsDragTarget = false;
            }

            currentDragTargetEntry = target;
            if (currentDragTargetEntry?.Emote != null)
            {
                currentDragTargetEntry.Emote.IsDragTarget = true;
            }
        }

        private void ClearCurrentDragTarget()
        {
            if (currentDragTargetEntry?.Emote != null)
            {
                currentDragTargetEntry.Emote.IsDragTarget = false;
            }

            currentDragTargetEntry = null;
        }

        private void EmoteTile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element
                && element.DataContext is EmoteTileEntryViewModel entry
                && entry.Emote != null)
            {
                string zone = ResolveContextZoneFromSource(e.OriginalSource as DependencyObject);
                contextMenuTargetEmote = entry.Emote;
                contextMenuTargetZone = zone;
                SelectEmoteTile(entry.Emote);
            }
        }

        private void EmoteTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element
                && element.DataContext is EmoteTileEntryViewModel entry
                && entry.Emote != null)
            {
                contextMenuTargetEmote = entry.Emote;
                contextMenuTargetZone = string.Empty;
                SelectEmoteTile(entry.Emote);
                RestartEmotePreviewPlayers(entry.Emote);
            }
        }

        private void EmoteDropZone_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement zone
                || zone.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            contextMenuTargetEmote = entry.Emote;
            contextMenuTargetZone = zone.Tag?.ToString() ?? string.Empty;
            SelectEmoteTile(entry.Emote);
        }

        private void EmoteTile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            CharacterCreationEmoteViewModel? targetEmote = contextMenuTargetEmote ?? GetSelectedEmote();
            if (targetEmote == null || sender is not Border border)
            {
                e.Handled = true;
                return;
            }

            string zone = ResolveContextZoneFromSource(e.Source as DependencyObject);
            if (string.IsNullOrWhiteSpace(zone))
            {
                zone = contextMenuTargetZone;
            }

            ContextMenu menu = BuildEmoteTileContextMenu(targetEmote, zone);
            border.ContextMenu = menu;
            border.ContextMenu.IsOpen = true;
            contextMenuTargetZone = string.Empty;
            e.Handled = true;
        }

        private ContextMenu BuildEmoteTileContextMenu(CharacterCreationEmoteViewModel emote, string targetZone)
        {
            ContextMenu menu = new ContextMenu();
            AddContextCategoryHeader(menu, "Emote", addLeadingSeparator: false);
            menu.Items.Add(CreateContextMenuItem("Rename", () => BeginEmoteRename(emote)));
            menu.Items.Add(CreateContextMenuItem("Delete Emote", () =>
            {
                SelectEmoteTile(emote);
                RemoveEmoteButton_Click(this, new RoutedEventArgs());
            }));
            menu.Items.Add(CreateContextMenuItem("Set Emote To Default", () => ResetEmoteToDefault(emote)));
            MenuItem hideDeskMenuItem = new MenuItem
            {
                Header = "Hide Desk",
                IsCheckable = true,
                IsChecked = emote.DeskModifier == 0
            };
            hideDeskMenuItem.Click += (_, _) =>
            {
                emote.DeskModifier = hideDeskMenuItem.IsChecked ? 0 : 1;
                StatusTextBlock.Text = hideDeskMenuItem.IsChecked ? "Desk hidden for this emote." : "Desk shown for this emote.";
            };
            menu.Items.Add(hideDeskMenuItem);

            AddContextCategoryHeader(menu, "Field", addLeadingSeparator: true);
            bool canClearField = CanClearField(emote, targetZone);
            MenuItem clearFieldItem = CreateContextMenuItem("Clear Field", () => ClearField(emote, targetZone));
            clearFieldItem.IsEnabled = canClearField;
            menu.Items.Add(clearFieldItem);

            AddContextCategoryHeader(menu, "Config", addLeadingSeparator: true);
            menu.Items.Add(CreateContextMenuItem("Preanim Config...", () => ShowPreAnimationConfigDialog(emote)));
            menu.Items.Add(CreateContextMenuItem("Final Anim Config...", () => ShowFinalAnimationConfigDialog(emote)));
            menu.Items.Add(CreateContextMenuItem("Button Icon Configuration...", () => ShowButtonIconConfigDialog(emote)));
            menu.Items.Add(CreateContextMenuItem("Emote Options Config...", () => ShowEmoteOptionsConfigDialog(emote)));
            menu.Items.Add(CreateContextMenuItem("SFX Config...", () => ShowSfxConfigDialog(emote, emote.SfxAssetSourcePath)));
            menu.Items.Add(CreateContextMenuItem("Emote Mods Config...", () => ShowEmoteModsConfigDialog(emote)));

            AddContextCategoryHeader(menu, "Order", addLeadingSeparator: true);
            menu.Items.Add(CreateContextMenuItem("Move Up", () =>
            {
                SelectEmoteTile(emote);
                MoveEmoteUpButton_Click(this, new RoutedEventArgs());
            }));
            menu.Items.Add(CreateContextMenuItem("Move Down", () =>
            {
                SelectEmoteTile(emote);
                MoveEmoteDownButton_Click(this, new RoutedEventArgs());
            }));

            return menu;
        }

        private static void AddContextCategoryHeader(ContextMenu menu, string text, bool addLeadingSeparator)
        {
            if (addLeadingSeparator && menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            TextBlock headerText = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.Black
            };

            MenuItem header = new MenuItem
            {
                Header = headerText,
                IsEnabled = true,
                IsHitTestVisible = false,
                Focusable = false,
                Opacity = 1.0
            };
            menu.Items.Add(header);
        }

        private static MenuItem CreateContextMenuItem(string header, Action action)
        {
            MenuItem item = new MenuItem
            {
                Header = header
            };
            item.Click += (_, _) => action();
            return item;
        }

        private static bool CanClearField(CharacterCreationEmoteViewModel emote, string zone)
        {
            return zone switch
            {
                "preanim" => emote.HasPreAnimationValue || !string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath),
                "anim" => emote.HasAnimationValue || !string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath),
                "button" => emote.HasButtonIconValue,
                "sfx" => !string.IsNullOrWhiteSpace(emote.SfxAssetSourcePath)
                    || (!string.IsNullOrWhiteSpace(emote.SfxName) && !string.Equals(emote.SfxName, "1", StringComparison.Ordinal))
                    || emote.SfxDelayMs != 1
                    || emote.SfxLooping,
                _ => false
            };
        }

        private void ClearField(CharacterCreationEmoteViewModel emote, string zone)
        {
            if (string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                StopEmotePreviewPlayer(emote, "preanim");
                emote.PreAnimation = "-";
                emote.PreAnimationAssetSourcePath = null;
                emote.PreAnimationPreview = null;
            }
            else if (string.Equals(zone, "anim", StringComparison.OrdinalIgnoreCase))
            {
                StopEmotePreviewPlayer(emote, "anim");
                StopEmotePreviewPlayer(emote, "preanim");
                emote.Animation = string.Empty;
                emote.AnimationAssetSourcePath = null;
                emote.AnimationPreview = null;
                emote.PreAnimation = "-";
                emote.PreAnimationAssetSourcePath = null;
                emote.PreAnimationPreview = null;
            }
            else if (string.Equals(zone, "button", StringComparison.OrdinalIgnoreCase))
            {
                emote.ResetButtonIconConfiguration();
                emote.ButtonIconToken = string.Empty;
                emote.ButtonIconAssetSourcePath = null;
                emote.ButtonIconPreview = null;
            }
            else if (string.Equals(zone, "sfx", StringComparison.OrdinalIgnoreCase))
            {
                emote.SfxAssetSourcePath = null;
                emote.SfxName = "1";
                emote.SfxDelayMs = 1;
                emote.SfxLooping = false;
                emote.RefreshSfxSummary();
            }

            UpdateButtonIconsSectionVisibility();
        }

        private void ResetEmoteToDefault(CharacterCreationEmoteViewModel emote)
        {
            StopEmotePreviewPlayers(emote);
            emote.Name = $"Emote {emote.Index}";
            emote.PreAnimation = "-";
            emote.Animation = string.Empty;
            emote.PreAnimationDurationMs = null;
            emote.StayTimeMs = null;
            emote.BlipsOverride = string.Empty;
            emote.EmoteModifier = 0;
            emote.DeskModifier = 1;
            emote.SfxName = "1";
            emote.SfxDelayMs = 1;
            emote.SfxLooping = false;
            emote.PreAnimationAssetSourcePath = null;
            emote.AnimationAssetSourcePath = null;
            emote.ButtonIconAssetSourcePath = null;
            emote.SfxAssetSourcePath = null;
            emote.PreAnimationPreview = null;
            emote.AnimationPreview = null;
            emote.ButtonIconPreview = null;
            emote.ResetButtonIconConfiguration();
            emote.ButtonIconToken = string.Empty;
            emote.FrameEvents.Clear();
            emote.RefreshDisplayName();
            emote.RefreshSfxSummary();
            UpdateButtonIconsSectionVisibility();
            StatusTextBlock.Text = "Emote reset to defaults.";
        }

        private void EmoteNameTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
            {
                return;
            }

            if (sender is FrameworkElement element
                && element.DataContext is EmoteTileEntryViewModel entry
                && entry.Emote != null)
            {
                BeginEmoteRename(entry.Emote);
                e.Handled = true;
            }
        }

        private void BeginEmoteRename(CharacterCreationEmoteViewModel emote)
        {
            if (string.IsNullOrWhiteSpace(emote.Name))
            {
                emote.Name = emote.EmoteHeader;
            }

            emote.RenameDraft = emote.Name;
            emote.IsRenaming = true;
            SelectEmoteTile(emote);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ListBoxItem? container =
                    EmoteTilesListBox.ItemContainerGenerator.ContainerFromItem(
                        emoteTiles.FirstOrDefault(tile => tile.Emote == emote)) as ListBoxItem;
                TextBox? textBox = FindDescendantByName<TextBox>(container, "EmoteNameEditTextBox");
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }), DispatcherPriority.Background);
        }

        private void EndEmoteRename(CharacterCreationEmoteViewModel emote)
        {
            string draft = (emote.RenameDraft ?? string.Empty).Trim();
            emote.Name = string.IsNullOrWhiteSpace(draft) ? emote.EmoteHeader : draft;
            emote.IsRenaming = false;
            emote.RefreshDisplayName();
            StatusTextBlock.Text = "Emote name updated.";
        }

        private void CancelEmoteRename(CharacterCreationEmoteViewModel emote)
        {
            emote.RenameDraft = emote.Name;
            emote.IsRenaming = false;
        }

        private void EmoteNameEditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement element
                || element.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                EndEmoteRename(entry.Emote);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEmoteRename(entry.Emote);
                e.Handled = true;
            }
        }

        private void EmoteNameEditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element
                && element.DataContext is EmoteTileEntryViewModel entry
                && entry.Emote != null
                && entry.Emote.IsRenaming)
            {
                EndEmoteRename(entry.Emote);
            }
        }

        private void CommitRenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element
                && element.DataContext is EmoteTileEntryViewModel entry
                && entry.Emote != null)
            {
                EndEmoteRename(entry.Emote);
            }
        }

        private void SfxSummaryTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element
                || element.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select emote SFX asset",
                Filter = "AO2-compatible audio (*.opus;*.ogg;*.mp3;*.wav)|*.opus;*.ogg;*.mp3;*.wav|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ShowSfxConfigDialog(entry.Emote, dialog.FileName);
        }

        private void EmoteDropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement captureTarget)
            {
                captureTarget.CaptureMouse();
            }

            if (e.ClickCount < 2
                || sender is not FrameworkElement zone
                || zone.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            suppressFieldSingleClickUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
            string zoneTag = zone.Tag?.ToString() ?? string.Empty;
            if (string.Equals(zoneTag, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                ShowPreAnimationConfigDialog(entry.Emote);
            }
        }

        private void EmoteDropZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement captureTarget && captureTarget.IsMouseCaptured)
            {
                captureTarget.ReleaseMouseCapture();
            }

            if (DateTime.UtcNow <= suppressFieldSingleClickUntilUtc)
            {
                return;
            }

            if (e.ClickCount != 1
                || sender is not FrameworkElement zone
                || zone.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            SelectEmoteTile(entry.Emote);
            string zoneTag = zone.Tag?.ToString() ?? string.Empty;
            if (string.Equals(zoneTag, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                ImportImageAssetForZone(entry.Emote, zoneTag);
            }
            else
            {
                ImportImageAssetForZone(entry.Emote, zoneTag);
            }

            e.Handled = true;
        }

        private void EmoteDropZone_DragEnter(object sender, DragEventArgs e)
        {
            EmoteDropZone_DragOver(sender, e);
        }

        private void EmoteDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement zone
                || zone.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            string zoneTag = zone.Tag?.ToString() ?? string.Empty;
            ClearDropZoneHighlights(entry.Emote);

            if (!TryGetSingleDroppedFilePath(e, out string? filePath))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            bool valid = IsImageAsset(filePath) || IsAudioAsset(filePath);
            if (valid)
            {
                SetDropZoneHighlight(entry.Emote, zoneTag, true);
            }

            e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void EmoteDropZone_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement zone
                && zone.DataContext is EmoteTileEntryViewModel entry
                && entry.Emote != null)
            {
                ClearDropZoneHighlights(entry.Emote);
            }
        }

        private void EmoteDropZone_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement zone
                || zone.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            try
            {
                if (!TryGetSingleDroppedFilePath(e, out string? filePath))
                {
                    return;
                }

                string zoneTag = zone.Tag?.ToString() ?? string.Empty;
                if (IsAudioAsset(filePath))
                {
                    ShowSfxConfigDialog(entry.Emote, filePath);
                    StatusTextBlock.Text = "SFX asset attached to emote.";
                }
                else if (IsImageAsset(filePath))
                {
                    ApplyImageAssetToEmote(entry.Emote, zoneTag, filePath);
                }
            }
            finally
            {
                ClearDropZoneHighlights(entry.Emote);
                e.Handled = true;
            }
        }

        private void ApplyImageAssetToEmote(CharacterCreationEmoteViewModel emote, string zone, string sourcePath)
        {
            string token = Path.GetFileNameWithoutExtension(sourcePath);
            ImageSource? preview = TryLoadPreviewImage(sourcePath);
            if (string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                emote.PreAnimation = token;
                emote.PreAnimationAssetSourcePath = sourcePath;
                emote.PreAnimationPreview = preview;
                UpdateAnimatedPreviewPlayer(emote, "preanim", sourcePath);
                EnsurePreanimEmoteModifierForEmote(emote);
                StatusTextBlock.Text = "Preanimation asset imported.";
            }
            else if (string.Equals(zone, "anim", StringComparison.OrdinalIgnoreCase))
            {
                emote.Animation = token;
                emote.AnimationAssetSourcePath = sourcePath;
                emote.FinalAnimationIdleAssetSourcePath = null;
                emote.FinalAnimationTalkingAssetSourcePath = null;
                emote.AnimationPreview = preview;
                UpdateAnimatedPreviewPlayer(emote, "anim", sourcePath);
                StatusTextBlock.Text = "Final animation asset imported.";
            }
            else if (string.Equals(zone, "button", StringComparison.OrdinalIgnoreCase))
            {
                emote.ButtonIconMode = ButtonIconMode.SingleImage;
                emote.ButtonSingleImageAssetSourcePath = sourcePath;
                emote.ButtonEffectsGenerationMode = ButtonEffectsGenerationMode.Darken;
                emote.ButtonEffectsDarknessPercent = 50;
                emote.ButtonIconAssetSourcePath = sourcePath;
                emote.ButtonIconPreview = preview;
                emote.ButtonIconToken = token;
                StatusTextBlock.Text = "Button icon asset imported.";
            }

            RestartEmotePreviewPlayers(emote);
            emote.RefreshDisplayName();
            UpdateButtonIconsSectionVisibility();
        }

        private void EnsurePreanimEmoteModifierForEmote(CharacterCreationEmoteViewModel emote)
        {
            bool hasPreanim = !string.IsNullOrWhiteSpace(emote.PreAnimation)
                && !string.Equals(emote.PreAnimation, "-", StringComparison.Ordinal);
            bool hasSfx = !string.IsNullOrWhiteSpace(emote.SfxName)
                && !string.Equals(emote.SfxName, "1", StringComparison.Ordinal);
            if (!hasPreanim && !hasSfx)
            {
                return;
            }

            if (emote.EmoteModifier == EmoteModIdle)
            {
                emote.EmoteModifier = EmoteModPreanim;
            }
            else if (emote.EmoteModifier == EmoteModZoom)
            {
                emote.EmoteModifier = EmoteModPreanimZoom;
            }
            else if (hasPreanim && emote.EmoteModifier != EmoteModPreanim && emote.EmoteModifier != EmoteModPreanimZoom)
            {
                emote.EmoteModifier = EmoteModPreanim;
            }
        }

        private void LoopAnimationPreviewsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            foreach (IAnimationPlayer player in emotePreviewPlayers.Values.ToArray())
            {
                player.SetLoop(ShouldLoopEmotePreviews);
            }

            foreach (CharacterCreationEmoteViewModel emote in emotes)
            {
                StartEmotePreanimCutoffTimer(emote);
            }
        }

        private void RestartEmotePreviewPlayers(CharacterCreationEmoteViewModel? emote)
        {
            if (emote == null)
            {
                return;
            }

            foreach (string zone in new[] { "preanim", "anim" })
            {
                string key = GetEmotePreviewPlayerKey(emote, zone);
                if (emotePreviewPlayers.TryGetValue(key, out IAnimationPlayer? player))
                {
                    player.Restart();
                    if (string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase))
                    {
                        StartEmotePreanimCutoffTimer(emote);
                    }
                }
                else
                {
                    string? sourcePath = string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase)
                        ? emote.PreAnimationAssetSourcePath
                        : emote.AnimationAssetSourcePath;
                    UpdateAnimatedPreviewPlayer(emote, zone, sourcePath);
                }
            }
        }

        private void StopAllEmotePreviewPlayers()
        {
            foreach (IAnimationPlayer player in emotePreviewPlayers.Values.ToArray())
            {
                player.Stop();
            }

            emotePreviewPlayers.Clear();
            StopAllEmotePreanimCutoffTimers();
        }

        private void StopEmotePreviewPlayers(CharacterCreationEmoteViewModel emote)
        {
            StopEmotePreviewPlayer(emote, "preanim");
            StopEmotePreviewPlayer(emote, "anim");
        }

        private void StopEmotePreviewPlayer(CharacterCreationEmoteViewModel emote, string zone)
        {
            string key = GetEmotePreviewPlayerKey(emote, zone);
            StopEmotePreanimCutoffTimer(key);
            if (!emotePreviewPlayers.TryGetValue(key, out IAnimationPlayer? player))
            {
                return;
            }

            player.Stop();
            emotePreviewPlayers.Remove(key);
        }

        private void UpdateAnimatedPreviewPlayer(CharacterCreationEmoteViewModel emote, string zone, string? sourcePath)
        {
            StopEmotePreviewPlayer(emote, zone);
            string? resolvedSourcePath = Ao2AnimationPreview.ResolveAo2ImagePath(sourcePath);
            if (string.IsNullOrWhiteSpace(resolvedSourcePath))
            {
                return;
            }

            string extension = Path.GetExtension(resolvedSourcePath).ToLowerInvariant();
            if (extension != ".gif" && extension != ".webp" && extension != ".apng")
            {
                return;
            }

            _ = Ao2AnimationPreview.TryCreateAnimationPlayer(resolvedSourcePath, ShouldLoopEmotePreviews, out IAnimationPlayer? player);

            if (player == null)
            {
                return;
            }

            player.FrameChanged += frame => Dispatcher.Invoke(() => ApplyAnimatedFrame(emote, zone, frame));
            ApplyAnimatedFrame(emote, zone, player.CurrentFrame);
            string key = GetEmotePreviewPlayerKey(emote, zone);
            emotePreviewPlayers[key] = player;
            if (string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                StartEmotePreanimCutoffTimer(emote);
            }
        }

        private static string? ResolveAo2PreviewImagePath(string? sourcePath) => Ao2AnimationPreview.ResolveAo2ImagePath(sourcePath);

        private static string GetEmotePreviewPlayerKey(CharacterCreationEmoteViewModel emote, string zone)
        {
            return emote.Id.ToString("N") + ":" + zone;
        }

        private void StartEmotePreanimCutoffTimer(CharacterCreationEmoteViewModel emote)
        {
            string key = GetEmotePreviewPlayerKey(emote, "preanim");
            StopEmotePreanimCutoffTimer(key);
            if (!emote.PreAnimationDurationMs.HasValue || emote.PreAnimationDurationMs.Value <= 0)
            {
                return;
            }

            if (!emotePreviewPlayers.TryGetValue(key, out IAnimationPlayer? player))
            {
                return;
            }

            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, emote.PreAnimationDurationMs.Value))
            };
            timer.Tick += (_, _) =>
            {
                if (ShouldLoopEmotePreviews)
                {
                    if (emotePreviewPlayers.TryGetValue(key, out IAnimationPlayer? activePlayer))
                    {
                        activePlayer.Restart();
                        return;
                    }
                }

                timer.Stop();
                emotePreanimCutoffTimers.Remove(key);
                StopEmotePreviewPlayer(emote, "preanim");
            };

            emotePreanimCutoffTimers[key] = timer;
            timer.Start();
        }

        private void StopEmotePreanimCutoffTimer(string key)
        {
            if (!emotePreanimCutoffTimers.TryGetValue(key, out DispatcherTimer? timer))
            {
                return;
            }

            timer.Stop();
            emotePreanimCutoffTimers.Remove(key);
        }

        private void StopAllEmotePreanimCutoffTimers()
        {
            foreach (DispatcherTimer timer in emotePreanimCutoffTimers.Values.ToArray())
            {
                timer.Stop();
            }

            emotePreanimCutoffTimers.Clear();
        }

        private static void ApplyAnimatedFrame(CharacterCreationEmoteViewModel emote, string zone, ImageSource frame)
        {
            if (string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase))
            {
                emote.PreAnimationPreview = frame;
            }
            else if (string.Equals(zone, "anim", StringComparison.OrdinalIgnoreCase))
            {
                emote.AnimationPreview = frame;
            }
        }

        private static bool TryGetSingleDroppedFilePath(DragEventArgs e, out string filePath)
        {
            filePath = string.Empty;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return false;
            }

            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            if (paths.Length != 1 || string.IsNullOrWhiteSpace(paths[0]) || !File.Exists(paths[0]))
            {
                return false;
            }

            filePath = paths[0];
            return true;
        }

        private static bool TryGetDroppedFilePaths(DragEventArgs e, out IReadOnlyList<string> filePaths)
        {
            filePaths = Array.Empty<string>();
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return false;
            }

            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            List<string> existing = paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .ToList();
            if (existing.Count == 0)
            {
                return false;
            }

            filePaths = existing;
            return true;
        }

        private static bool IsImageAsset(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".webp" || ext == ".apng";
        }

        private static bool IsAudioAsset(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".opus" || ext == ".ogg" || ext == ".mp3" || ext == ".wav";
        }

        private static ImageSource? TryLoadPreviewImage(string path)
        {
            return Ao2AnimationPreview.LoadStaticPreviewImage(path, decodePixelWidth: 0);
        }

        private void ImportImageAssetForZone(CharacterCreationEmoteViewModel emote, string zoneTag)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import emote image asset",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ApplyImageAssetToEmote(emote, zoneTag, dialog.FileName);
        }

        private void EmoteResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is not Thumb thumb
                || thumb.DataContext is not EmoteTileEntryViewModel entry
                || entry.Emote == null)
            {
                return;
            }

            foreach (CharacterCreationEmoteViewModel emote in emotes)
            {
                emote.IsResizeGuideVisible = false;
            }

            activeResizeEmote = entry.Emote;
            activeResizeContainer = FindAncestor<Border>(thumb) as FrameworkElement
                ?? FindAncestor<ListBoxItem>(thumb);
            activeResizeMode = thumb.Tag?.ToString() ?? string.Empty;
            resizeStartWidth = EmoteTileWidth;
            resizeStartHeight = EmoteTileHeight;
            ResizePreviewWidth = EmoteTileWidth;
            ResizePreviewHeight = EmoteTileHeight;
            if (activeResizeContainer != null)
            {
                GeneralTransform transform = activeResizeContainer.TransformToAncestor(EmoteTilesOverlayRoot);
                Point topLeft = transform.Transform(new Point(0, 0));
                ResizeGuideLeft = topLeft.X;
                ResizeGuideTop = topLeft.Y;
            }

            IsGlobalResizeGuideVisible = true;
        }

        private void EmoteResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (activeResizeEmote == null)
            {
                return;
            }

            Point pointer = activeResizeContainer != null
                ? Mouse.GetPosition(activeResizeContainer)
                : new Point(resizeStartWidth + e.HorizontalChange, resizeStartHeight + e.VerticalChange);

            double nextWidth = ResizePreviewWidth;
            double nextHeight = ResizePreviewHeight;

            if (string.Equals(activeResizeMode, "right", StringComparison.OrdinalIgnoreCase)
                || string.Equals(activeResizeMode, "corner", StringComparison.OrdinalIgnoreCase))
            {
                nextWidth = Math.Clamp(pointer.X, MinTileWidth, MaxTileWidth);
            }

            if (string.Equals(activeResizeMode, "bottom", StringComparison.OrdinalIgnoreCase)
                || string.Equals(activeResizeMode, "corner", StringComparison.OrdinalIgnoreCase))
            {
                nextHeight = Math.Clamp(pointer.Y, MinTileHeight, MaxTileHeight);
            }

            ResizePreviewWidth = nextWidth;
            ResizePreviewHeight = nextHeight;
        }

        private void EmoteResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (activeResizeEmote == null)
            {
                return;
            }

            EmoteTileWidth = ResizePreviewWidth;
            EmoteTileHeight = ResizePreviewHeight;
            SaveFile.Data.CharacterCreatorEmoteTileWidth = EmoteTileWidth;
            SaveFile.Data.CharacterCreatorEmoteTileHeight = EmoteTileHeight;
            IsGlobalResizeGuideVisible = false;
            activeResizeEmote = null;
            activeResizeContainer = null;
            activeResizeMode = string.Empty;
            SaveFile.Save();
        }

        private void RenameEmoteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                return;
            }

            BeginEmoteRename(emote!);
        }

        private void DeleteEmoteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                SelectEmoteTile(emote);
                RemoveEmoteButton_Click(sender, e);
            }
        }

        private void MoveEmoteUpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                SelectEmoteTile(emote);
                MoveEmoteUpButton_Click(sender, e);
            }
        }

        private void MoveEmoteDownMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                SelectEmoteTile(emote);
                MoveEmoteDownButton_Click(sender, e);
            }
        }

        private void OpenPreAnimationConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                ShowPreAnimationConfigDialog(emote!);
            }
        }

        private void OpenAnimationConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                ShowEmoteOptionsConfigDialog(emote!);
            }
        }

        private void OpenButtonIconConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                SelectEmoteTile(emote);
                ShowButtonIconConfigDialog(emote!);
            }
        }

        private void OpenSfxConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                ShowSfxConfigDialog(emote!, emote!.SfxAssetSourcePath);
            }
        }

        private void OpenEmoteModsConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetMenuEmote(sender, out CharacterCreationEmoteViewModel? emote))
            {
                ShowEmoteModsConfigDialog(emote!);
            }
        }

        private static bool TryGetMenuEmote(object sender, out CharacterCreationEmoteViewModel? emote)
        {
            emote = null;
            if (sender is MenuItem menuItem && menuItem.CommandParameter is CharacterCreationEmoteViewModel viewModel)
            {
                emote = viewModel;
                return true;
            }

            return false;
        }

        private void ShowPreAnimationConfigDialog(CharacterCreationEmoteViewModel emote)
        {
            Window dialog = CreateEmoteDialog("Preanimation Config", 700, 520);
            Grid grid = BuildDialogGrid(2);
            TextBox durationTextBox = CreateDialogTextBox(
                emote.PreAnimationDurationMs?.ToString() ?? string.Empty,
                "Optional explicit preanimation duration in milliseconds.");
            CheckBox durationEnabledCheckBox = AddOptionalDialogFieldContainer(
                grid,
                0,
                "Preanimation Duration (ms)",
                "Optional explicit preanimation duration in milliseconds.",
                durationTextBox,
                emote.PreAnimationDurationMs.HasValue);

            Border previewBorder = new Border
            {
                Height = 200,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                Padding = new Thickness(6)
            };
            Grid previewGrid = new Grid();
            Image previewImage = new Image
            {
                Stretch = Stretch.Uniform
            };
            TextBlock noPreanimText = new TextBlock
            {
                Text = "No preanim",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(172, 182, 194))
            };
            previewGrid.Children.Add(previewImage);
            previewGrid.Children.Add(noPreanimText);
            previewBorder.Child = previewGrid;

            Button playPauseButton = CreateTimelineSymbolButton("▶", "Play/pause preview.");
            Button prevFrameButton = CreateTimelineSymbolButton("⏮", "Go back one frame.");
            Button nextFrameButton = CreateTimelineSymbolButton("⏭", "Go forward one frame.");
            Slider timelineSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1000,
                Value = 0,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            CheckBox loopCheckBox = new CheckBox
            {
                Content = "Loop",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Loop animation preview."
            };
            ApplyDialogCheckBoxStyle(loopCheckBox);

            Grid timelineControls = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(playPauseButton, 0);
            Grid.SetColumn(prevFrameButton, 1);
            Grid.SetColumn(nextFrameButton, 2);
            Grid.SetColumn(timelineSlider, 3);
            Grid.SetColumn(loopCheckBox, 4);
            timelineControls.Children.Add(playPauseButton);
            timelineControls.Children.Add(prevFrameButton);
            timelineControls.Children.Add(nextFrameButton);
            timelineControls.Children.Add(timelineSlider);
            timelineControls.Children.Add(loopCheckBox);

            AnimationTimelinePreviewController? previewController = null;
            Button cutPreanimButton = CreateDialogButton("Cut preanim here", isPrimary: true);
            cutPreanimButton.HorizontalAlignment = HorizontalAlignment.Center;
            cutPreanimButton.Margin = new Thickness(0, 8, 0, 0);
            cutPreanimButton.Width = 170;
            cutPreanimButton.Click += (_, _) =>
            {
                if (previewController == null)
                {
                    return;
                }

                durationEnabledCheckBox.IsChecked = true;
                durationTextBox.Text = Math.Max(0, (int)Math.Round(previewController.CurrentPositionMs)).ToString();
            };

            StackPanel previewPanel = new StackPanel();
            previewPanel.Children.Add(previewBorder);
            previewPanel.Children.Add(timelineControls);
            previewPanel.Children.Add(cutPreanimButton);
            AddDialogFieldContainer(
                grid,
                1,
                "Preanimation Preview",
                "Preview with AO2-style cut timing.",
                previewPanel);

            bool suppressTimelineSeek = false;
            if (AnimationTimelinePreviewController.TryCreate(ResolveAo2PreviewImagePath(emote.PreAnimationAssetSourcePath), out AnimationTimelinePreviewController? createdController))
            {
                previewController = createdController;
                previewImage.Source = createdController.CurrentFrame;
                noPreanimText.Visibility = Visibility.Collapsed;
            }
            else if (emote.PreAnimationPreview != null)
            {
                previewImage.Source = emote.PreAnimationPreview;
                noPreanimText.Visibility = Visibility.Collapsed;
            }

            void ApplyDurationCutoffToPreview()
            {
                if (previewController == null)
                {
                    return;
                }

                int? cutoffMs = durationEnabledCheckBox.IsChecked == true
                    ? ParseNullableInt(durationTextBox.Text)
                    : null;
                previewController.SetCutoffDurationMs(cutoffMs);
                timelineSlider.Maximum = Math.Max(1, previewController.EffectiveDurationMs);
                if (timelineSlider.Value > timelineSlider.Maximum)
                {
                    timelineSlider.Value = timelineSlider.Maximum;
                }
            }

            if (previewController != null)
            {
                timelineSlider.Maximum = Math.Max(1, previewController.EffectiveDurationMs);
                previewController.SetLoop(loopCheckBox.IsChecked == true);
                ApplyDurationCutoffToPreview();
                previewController.PlaybackStateChanged += isPlaying => Dispatcher.Invoke(() =>
                {
                    playPauseButton.Content = isPlaying ? "⏸" : "▶";
                });
                previewController.PositionChanged += (frame, positionMs) => Dispatcher.Invoke(() =>
                {
                    previewImage.Source = frame;
                    suppressTimelineSeek = true;
                    timelineSlider.Maximum = Math.Max(1, previewController.EffectiveDurationMs);
                    timelineSlider.Value = Math.Clamp(positionMs, timelineSlider.Minimum, timelineSlider.Maximum);
                    suppressTimelineSeek = false;
                });
            }

            bool interactiveTimeline = previewController?.HasTimeline == true;
            playPauseButton.IsEnabled = interactiveTimeline;
            prevFrameButton.IsEnabled = interactiveTimeline;
            nextFrameButton.IsEnabled = interactiveTimeline;
            timelineSlider.IsEnabled = interactiveTimeline;
            loopCheckBox.IsEnabled = interactiveTimeline;
            cutPreanimButton.IsEnabled = interactiveTimeline;
            if (!interactiveTimeline && noPreanimText.Visibility != Visibility.Visible)
            {
                noPreanimText.Text = "No animated preanim";
                noPreanimText.Visibility = Visibility.Visible;
            }

            playPauseButton.Click += (_, _) =>
            {
                if (previewController == null)
                {
                    return;
                }

                if (previewController.IsPlaying)
                {
                    previewController.Pause();
                }
                else
                {
                    previewController.Play();
                }
            };
            prevFrameButton.Click += (_, _) => previewController?.StepFrame(-1);
            nextFrameButton.Click += (_, _) => previewController?.StepFrame(1);
            timelineSlider.ValueChanged += (_, _) =>
            {
                if (!suppressTimelineSeek)
                {
                    previewController?.Seek(timelineSlider.Value);
                }
            };
            loopCheckBox.Checked += (_, _) => previewController?.SetLoop(true);
            loopCheckBox.Unchecked += (_, _) => previewController?.SetLoop(false);
            durationEnabledCheckBox.Checked += (_, _) => ApplyDurationCutoffToPreview();
            durationEnabledCheckBox.Unchecked += (_, _) => ApplyDurationCutoffToPreview();
            durationTextBox.TextChanged += (_, _) => ApplyDurationCutoffToPreview();

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

            bool? result;
            try
            {
                result = dialog.ShowDialog();
            }
            finally
            {
                previewController?.Dispose();
            }

            if (result != true)
            {
                return;
            }

            emote.PreAnimationDurationMs = durationEnabledCheckBox.IsChecked == true
                ? ParseNullableInt(durationTextBox.Text)
                : null;
            emote.RefreshDisplayName();
            RestartEmotePreviewPlayers(emote);
            StatusTextBlock.Text = "Preanimation config saved.";
        }

        private void ShowEmoteOptionsConfigDialog(CharacterCreationEmoteViewModel emote)
        {
            Window dialog = CreateEmoteDialog("Emote Options Config", 620, 300);
            Grid grid = BuildDialogGrid(2);
            TextBox stayTextBox = CreateDialogTextBox(
                emote.StayTimeMs?.ToString() ?? string.Empty,
                "AO2 text hold duration in milliseconds ([stay_time], keyed by this emote preanimation token).");
            CheckBox stayEnabledCheckBox = AddOptionalDialogFieldContainer(
                grid,
                0,
                "Stay Time (ms)",
                "AO2 text hold duration in milliseconds ([stay_time], keyed by this emote preanimation token).",
                stayTextBox,
                emote.StayTimeMs.HasValue);

            AutoCompleteDropdownField blipsDropdown = CreateDialogAutoCompleteField(
                blipOptions,
                emote.BlipsOverride,
                "AO2 per-emote blip override via [OptionsN] -> [OptionsX]/blips.",
                isReadOnly: false);
            CheckBox blipsEnabledCheckBox = AddOptionalDialogFieldContainer(
                grid,
                1,
                "Blips Override",
                "AO2 per-emote blip override via [OptionsN] -> [OptionsX]/blips.",
                blipsDropdown,
                !string.IsNullOrWhiteSpace(emote.BlipsOverride));

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            emote.StayTimeMs = stayEnabledCheckBox.IsChecked == true
                ? ParseNullableInt(stayTextBox.Text)
                : null;
            emote.BlipsOverride = blipsEnabledCheckBox.IsChecked == true
                ? (blipsDropdown.Text ?? string.Empty).Trim()
                : string.Empty;
            emote.RefreshDisplayName();
            StatusTextBlock.Text = "Emote options config saved.";
        }

        private void ShowFinalAnimationConfigDialog(CharacterCreationEmoteViewModel emote)
        {
            Window dialog = CreateEmoteDialog("Final Animation Config", 860, 560);
            Grid grid = BuildDialogGrid(1);

            string sharedPath = emote.AnimationAssetSourcePath ?? string.Empty;
            string idlePath = emote.FinalAnimationIdleAssetSourcePath ?? string.Empty;
            string talkingPath = emote.FinalAnimationTalkingAssetSourcePath ?? string.Empty;
            bool splitMode = !string.IsNullOrWhiteSpace(idlePath) || !string.IsNullOrWhiteSpace(talkingPath);

            bool loopPreview = true;
            List<IAnimationPlayer> activePlayers = new List<IAnimationPlayer>();
            Grid hostGrid = new Grid();

            OpenFileDialog BuildImagePicker() => new OpenFileDialog
            {
                Title = "Select final animation asset",
                Filter = "AO2-compatible image/animation (*.webp;*.apng;*.gif;*.png)|*.webp;*.apng;*.gif;*.png|All files (*.*)|*.*"
            };

            void StopAllPlayers()
            {
                foreach (IAnimationPlayer player in activePlayers.ToArray())
                {
                    player.Stop();
                }

                activePlayers.Clear();
            }

            bool PickPath(out string pickedPath)
            {
                pickedPath = string.Empty;
                OpenFileDialog picker = BuildImagePicker();
                if (picker.ShowDialog() != true)
                {
                    return false;
                }

                pickedPath = picker.FileName;
                return true;
            }

            Border CreateFinalAnimCard(string title, Func<string> getPath, Action<string> setPath)
            {
                Border card = new Border
                {
                    Width = 290,
                    Height = 300,
                    Margin = new Thickness(8, 0, 8, 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                    Padding = new Thickness(8),
                    Cursor = Cursors.Hand
                };

                Grid body = new Grid();
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock titleText = new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(Color.FromRgb(196, 208, 222)),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    Cursor = Cursors.Hand,
                    ToolTip = "Replay preview"
                };
                Grid.SetRow(titleText, 0);
                body.Children.Add(titleText);

                Grid previewGrid = new Grid
                {
                    ClipToBounds = true
                };
                Image previewImage = new Image
                {
                    Stretch = Stretch.UniformToFill
                };
                TextBlock plusIcon = new TextBlock
                {
                    Text = "+",
                    FontSize = 46,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(182, 198, 214)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(previewGrid, 1);
                previewGrid.Children.Add(previewImage);
                previewGrid.Children.Add(plusIcon);
                body.Children.Add(previewGrid);

                TextBlock fileText = new TextBlock
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                Grid.SetRow(fileText, 2);
                body.Children.Add(fileText);

                IAnimationPlayer? player = null;
                void RefreshCardVisual()
                {
                    player?.Stop();
                    player = null;
                    string path = getPath();
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        previewImage.Source = null;
                        plusIcon.Visibility = Visibility.Visible;
                        fileText.Text = "+";
                        return;
                    }

                    fileText.Text = Path.GetFileName(path);
                    plusIcon.Visibility = Visibility.Collapsed;
                    previewImage.Source = TryLoadPreviewImage(path);
                    if (Ao2AnimationPreview.TryCreateAnimationPlayer(path, loopPreview, out IAnimationPlayer? createdPlayer)
                        && createdPlayer != null)
                    {
                        player = createdPlayer;
                        activePlayers.Add(createdPlayer);
                        previewImage.Source = createdPlayer.CurrentFrame;
                        createdPlayer.FrameChanged += frame => Dispatcher.Invoke(() =>
                        {
                            previewImage.Source = frame;
                        });
                    }
                }

                titleText.MouseLeftButtonUp += (_, e) =>
                {
                    if (player != null)
                    {
                        player.Restart();
                    }

                    e.Handled = true;
                };

                card.MouseLeftButtonUp += (_, _) =>
                {
                    if (!PickPath(out string selectedPath))
                    {
                        return;
                    }

                    setPath(selectedPath);
                    RefreshDialogBody();
                };

                RefreshCardVisual();
                card.Child = body;
                return card;
            }

            void RefreshDialogBody()
            {
                StopAllPlayers();
                hostGrid.Children.Clear();

                StackPanel contentPanel = new StackPanel();
                Grid topControls = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 10)
                };
                topControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                topControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                topControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Button splitToggleButton = CreateDialogButton(
                    splitMode ? "Use one shared final anim" : "Use separate talking/idle",
                    isPrimary: false);
                splitToggleButton.Width = 220;
                splitToggleButton.Click += (_, _) =>
                {
                    if (!splitMode)
                    {
                        idlePath = sharedPath;
                        talkingPath = sharedPath;
                        splitMode = true;
                    }
                    else
                    {
                        sharedPath = !string.IsNullOrWhiteSpace(idlePath) ? idlePath : talkingPath;
                        idlePath = string.Empty;
                        talkingPath = string.Empty;
                        splitMode = false;
                    }

                    RefreshDialogBody();
                };
                Grid.SetColumn(splitToggleButton, 0);
                topControls.Children.Add(splitToggleButton);

                CheckBox loopCheckBox = new CheckBox
                {
                    Content = "Loop preview",
                    IsChecked = loopPreview,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                ApplyDialogCheckBoxStyle(loopCheckBox);
                loopCheckBox.Checked += (_, _) =>
                {
                    loopPreview = true;
                    foreach (IAnimationPlayer player in activePlayers)
                    {
                        player.SetLoop(true);
                    }
                };
                loopCheckBox.Unchecked += (_, _) =>
                {
                    loopPreview = false;
                    foreach (IAnimationPlayer player in activePlayers)
                    {
                        player.SetLoop(false);
                    }
                };
                Grid.SetColumn(loopCheckBox, 1);
                topControls.Children.Add(loopCheckBox);
                contentPanel.Children.Add(topControls);

                if (!splitMode)
                {
                    StackPanel row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    row.Children.Add(CreateFinalAnimCard("Final anim", () => sharedPath, value => sharedPath = value));
                    contentPanel.Children.Add(row);
                }
                else
                {
                    StackPanel row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    row.Children.Add(CreateFinalAnimCard("Idle anim", () => idlePath, value => idlePath = value));
                    row.Children.Add(CreateFinalAnimCard("Talking anim", () => talkingPath, value => talkingPath = value));
                    contentPanel.Children.Add(row);
                }

                hostGrid.Children.Add(contentPanel);
            }

            RefreshDialogBody();

            AddDialogFieldContainer(
                grid,
                0,
                "Final Animation",
                "Configure one shared final animation or separate idle/talking animations.",
                hostGrid);

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            bool? result = dialog.ShowDialog();
            StopAllPlayers();
            if (result != true)
            {
                return;
            }

            emote.FinalAnimationIdleAssetSourcePath = string.IsNullOrWhiteSpace(idlePath) ? null : idlePath;
            emote.FinalAnimationTalkingAssetSourcePath = string.IsNullOrWhiteSpace(talkingPath) ? null : talkingPath;

            string selectedReferencePath = splitMode
                ? (!string.IsNullOrWhiteSpace(idlePath) ? idlePath : talkingPath)
                : sharedPath;
            if (!string.IsNullOrWhiteSpace(selectedReferencePath) && File.Exists(selectedReferencePath))
            {
                emote.AnimationAssetSourcePath = selectedReferencePath;
                emote.Animation = Path.GetFileNameWithoutExtension(selectedReferencePath);
                emote.AnimationPreview = TryLoadPreviewImage(selectedReferencePath);
                UpdateAnimatedPreviewPlayer(emote, "anim", selectedReferencePath);
                RestartEmotePreviewPlayers(emote);
            }

            StatusTextBlock.Text = "Final animation config saved.";
        }

        private void ShowSfxConfigDialog(CharacterCreationEmoteViewModel emote, string? suggestedSourcePath)
        {
            Window dialog = CreateEmoteDialog("Emote SFX Config", 760, 620);
            Grid grid = BuildDialogGrid(3);

            string selectedSfxPath = suggestedSourcePath ?? emote.SfxAssetSourcePath ?? string.Empty;
            AO2BlipPreviewPlayer dialogSfxPreviewPlayer = new AO2BlipPreviewPlayer
            {
                Volume = (float)Math.Clamp(PreviewVolumeSlider?.Value / 100.0 ?? 0.35, 0, 1)
            };

            TextBox sourcePathTextBox = CreateDialogTextBox(
                selectedSfxPath,
                "Optional local SFX file source. AO2-compatible: opus, ogg, mp3, wav.");
            sourcePathTextBox.IsReadOnly = true;

            PlayStopProgressButton sfxPreviewPlayButton = new PlayStopProgressButton
            {
                Width = 36,
                Height = 36,
                ProgressEnabled = true,
                AutoProgress = true,
                ToolTipText = "Preview this SFX file."
            };
            sfxPreviewPlayButton.PlayRequested += (_, _) =>
            {
                string path = sourcePathTextBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    sfxPreviewPlayButton.IsPlaying = false;
                    return;
                }

                dialogSfxPreviewPlayer.Stop();
                if (!dialogSfxPreviewPlayer.TrySetBlip(path))
                {
                    sfxPreviewPlayButton.IsPlaying = false;
                    return;
                }

                double durationMs = Math.Max(120, dialogSfxPreviewPlayer.GetLoadedDurationMs());
                sfxPreviewPlayButton.DurationMs = durationMs;
                sfxPreviewPlayButton.IsPlaying = true;
                _ = dialogSfxPreviewPlayer.PlayBlip();
            };
            sfxPreviewPlayButton.StopRequested += (_, _) =>
            {
                dialogSfxPreviewPlayer.Stop();
                sfxPreviewPlayButton.IsPlaying = false;
            };

            Button fromFileButton = CreateDialogButton("From file", isPrimary: true);
            fromFileButton.Width = 100;
            fromFileButton.Click += (_, _) =>
            {
                OpenFileDialog picker = new OpenFileDialog
                {
                    Title = "Select emote SFX asset",
                    Filter = "AO2-compatible audio (*.opus;*.ogg;*.mp3;*.wav)|*.opus;*.ogg;*.mp3;*.wav|All files (*.*)|*.*"
                };

                if (picker.ShowDialog() != true)
                {
                    return;
                }

                selectedSfxPath = picker.FileName;
                sourcePathTextBox.Text = selectedSfxPath;
                dialogSfxPreviewPlayer.Stop();
                sfxPreviewPlayButton.IsPlaying = false;
            };

            Grid sourceRow = new Grid();
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(sourcePathTextBox, 0);
            sourceRow.Children.Add(sourcePathTextBox);
            Grid.SetColumn(sfxPreviewPlayButton, 1);
            sfxPreviewPlayButton.Margin = new Thickness(8, 0, 8, 0);
            sourceRow.Children.Add(sfxPreviewPlayButton);
            Grid.SetColumn(fromFileButton, 2);
            sourceRow.Children.Add(fromFileButton);
            AddDialogFieldContainer(
                grid,
                0,
                "SFX Source File",
                "Optional local SFX file source. AO2-compatible: opus, ogg, mp3, wav.",
                sourceRow);

            TextBox delayTextBox = CreateDialogTextBox(
                (Math.Max(0, emote.SfxDelayMs) * Ao2TimingTickMs).ToString(),
                "Delay before SFX playback after emote starts (milliseconds; AO2 stores this in 40ms ticks).");
            CheckBox loopCheckBox = new CheckBox
            {
                Content = "Loop",
                Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240)),
                IsChecked = emote.SfxLooping,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "If enabled, AO2 will loop this emote SFX."
            };
            ApplyDialogCheckBoxStyle(loopCheckBox);

            Grid delayRow = new Grid();
            delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(delayTextBox, 0);
            delayRow.Children.Add(delayTextBox);
            Grid.SetColumn(loopCheckBox, 1);
            delayRow.Children.Add(loopCheckBox);
            AddDialogFieldContainer(
                grid,
                1,
                "SFX Delay (ms)",
                "Delay before SFX playback after emote starts.",
                delayRow);

            string animationSourcePath = !string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath)
                ? emote.PreAnimationAssetSourcePath!
                : emote.AnimationAssetSourcePath ?? string.Empty;
            Border previewBorder = new Border
            {
                Height = 200,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                Padding = new Thickness(6)
            };
            Grid previewGrid = new Grid();
            Image previewImage = new Image
            {
                Stretch = Stretch.Uniform
            };
            TextBlock noAnimationText = new TextBlock
            {
                Text = "No animation",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(172, 182, 194))
            };
            previewGrid.Children.Add(previewImage);
            previewGrid.Children.Add(noAnimationText);
            previewBorder.Child = previewGrid;

            Button playPauseButton = CreateTimelineSymbolButton("▶", "Play/pause animation preview.");
            Button prevFrameButton = CreateTimelineSymbolButton("⏮", "Go back one frame.");
            Button nextFrameButton = CreateTimelineSymbolButton("⏭", "Go forward one frame.");
            CheckBox previewLoopCheckBox = new CheckBox
            {
                Content = "Loop preview",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Loop animation preview."
            };
            ApplyDialogCheckBoxStyle(previewLoopCheckBox);
            Slider timelineSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1000,
                Value = 0,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid timelineSliderHost = new Grid
            {
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas timelineMarkerCanvas = new Canvas
            {
                IsHitTestVisible = false
            };
            Border sfxDelayMarker = new Border
            {
                Width = 8,
                Height = 8,
                Background = new SolidColorBrush(Color.FromRgb(215, 70, 70)),
                CornerRadius = new CornerRadius(4),
                Visibility = Visibility.Visible
            };
            timelineSliderHost.Children.Add(timelineSlider);
            timelineMarkerCanvas.Children.Add(sfxDelayMarker);
            timelineSliderHost.Children.Add(timelineMarkerCanvas);

            Grid timelineControls = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(playPauseButton, 0);
            Grid.SetColumn(prevFrameButton, 1);
            Grid.SetColumn(nextFrameButton, 2);
            Grid.SetColumn(timelineSliderHost, 3);
            Grid.SetColumn(previewLoopCheckBox, 4);
            timelineControls.Children.Add(playPauseButton);
            timelineControls.Children.Add(prevFrameButton);
            timelineControls.Children.Add(nextFrameButton);
            timelineControls.Children.Add(timelineSliderHost);
            timelineControls.Children.Add(previewLoopCheckBox);

            AnimationTimelinePreviewController? animationPreviewController = null;
            Button setSfxDelayToCurrentButton = CreateDialogButton("Set SFX to play here", isPrimary: true);
            setSfxDelayToCurrentButton.HorizontalAlignment = HorizontalAlignment.Center;
            setSfxDelayToCurrentButton.Margin = new Thickness(0, 8, 8, 0);
            setSfxDelayToCurrentButton.Width = 180;
            CheckBox muteSfxCheckBox = new CheckBox
            {
                Content = "Mute SFX",
                IsChecked = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 8, 0, 0),
                ToolTip = "Mute SFX during timeline preview playback."
            };
            ApplyDialogCheckBoxStyle(muteSfxCheckBox);
            setSfxDelayToCurrentButton.Click += (_, _) =>
            {
                if (animationPreviewController == null)
                {
                    return;
                }

                delayTextBox.Text = Math.Max(0, (int)Math.Round(animationPreviewController.CurrentPositionMs)).ToString();
            };
            Grid actionRow = new Grid();
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(setSfxDelayToCurrentButton, 1);
            Grid.SetColumn(muteSfxCheckBox, 2);
            actionRow.Children.Add(setSfxDelayToCurrentButton);
            actionRow.Children.Add(muteSfxCheckBox);

            StackPanel previewPanel = new StackPanel();
            previewPanel.Children.Add(previewBorder);
            previewPanel.Children.Add(timelineControls);
            previewPanel.Children.Add(actionRow);
            AddDialogFieldContainer(
                grid,
                2,
                "Animation/SFX Preview",
                "Preview animation timeline and SFX sync using current delay.",
                previewPanel);

            bool suppressTimelineSeek = false;
            bool sfxTriggeredInCycle = false;
            double previousPositionMs = 0;
            if (AnimationTimelinePreviewController.TryCreate(ResolveAo2PreviewImagePath(animationSourcePath), out AnimationTimelinePreviewController? createdController))
            {
                animationPreviewController = createdController;
                previewImage.Source = createdController.CurrentFrame;
                noAnimationText.Visibility = Visibility.Collapsed;
            }
            else if (emote.PreAnimationPreview != null)
            {
                previewImage.Source = emote.PreAnimationPreview;
                noAnimationText.Visibility = Visibility.Collapsed;
            }
            else if (emote.AnimationPreview != null)
            {
                previewImage.Source = emote.AnimationPreview;
                noAnimationText.Visibility = Visibility.Collapsed;
            }

            int ReadDelayMs()
            {
                return Math.Max(0, ParseIntOrDefault(delayTextBox.Text, Math.Max(0, emote.SfxDelayMs) * Ao2TimingTickMs));
            }

            void UpdateSfxDelayMarker()
            {
                double duration = Math.Max(1, timelineSlider.Maximum);
                double delayMs = Math.Clamp(ReadDelayMs(), timelineSlider.Minimum, duration);
                if (timelineSlider.ActualWidth <= 1)
                {
                    return;
                }

                double ratio = delayMs / duration;
                double x = ratio * timelineSlider.ActualWidth;
                Canvas.SetLeft(sfxDelayMarker, Math.Max(0, Math.Min(timelineSlider.ActualWidth - sfxDelayMarker.Width, x - (sfxDelayMarker.Width / 2.0))));
                Canvas.SetTop(sfxDelayMarker, Math.Max(0, (timelineSlider.ActualHeight / 2.0) - 12));
            }

            bool TryPlayConfiguredSfx()
            {
                if (muteSfxCheckBox.IsChecked == true)
                {
                    return false;
                }

                string path = sourcePathTextBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return false;
                }

                dialogSfxPreviewPlayer.Volume = (float)Math.Clamp(PreviewVolumeSlider?.Value / 100.0 ?? 0.35, 0, 1);
                if (!dialogSfxPreviewPlayer.TrySetBlip(path))
                {
                    return false;
                }

                return dialogSfxPreviewPlayer.PlayBlip();
            }

            if (animationPreviewController != null)
            {
                animationPreviewController.SetLoop(previewLoopCheckBox.IsChecked == true);
                timelineSlider.Maximum = Math.Max(1, animationPreviewController.EffectiveDurationMs);
                UpdateSfxDelayMarker();
                animationPreviewController.PlaybackStateChanged += isPlaying => Dispatcher.Invoke(() =>
                {
                    playPauseButton.Content = isPlaying ? "⏸" : "▶";
                });
                animationPreviewController.PositionChanged += (frame, positionMs) => Dispatcher.Invoke(() =>
                {
                    previewImage.Source = frame;
                    if (positionMs < previousPositionMs)
                    {
                        sfxTriggeredInCycle = false;
                    }

                    previousPositionMs = positionMs;
                    if (!sfxTriggeredInCycle && positionMs >= ReadDelayMs())
                    {
                        if (TryPlayConfiguredSfx())
                        {
                            sfxTriggeredInCycle = true;
                        }
                    }

                    suppressTimelineSeek = true;
                    timelineSlider.Maximum = Math.Max(1, animationPreviewController.EffectiveDurationMs);
                    timelineSlider.Value = Math.Clamp(positionMs, timelineSlider.Minimum, timelineSlider.Maximum);
                    suppressTimelineSeek = false;
                    UpdateSfxDelayMarker();
                });
            }

            bool interactiveTimeline = animationPreviewController?.HasTimeline == true;
            playPauseButton.IsEnabled = interactiveTimeline;
            prevFrameButton.IsEnabled = interactiveTimeline;
            nextFrameButton.IsEnabled = interactiveTimeline;
            timelineSlider.IsEnabled = interactiveTimeline;
            setSfxDelayToCurrentButton.IsEnabled = interactiveTimeline;
            previewLoopCheckBox.IsEnabled = interactiveTimeline;
            muteSfxCheckBox.IsEnabled = interactiveTimeline;
            timelineSlider.SizeChanged += (_, _) => UpdateSfxDelayMarker();
            delayTextBox.TextChanged += (_, _) => UpdateSfxDelayMarker();
            if (!interactiveTimeline && noAnimationText.Visibility != Visibility.Visible)
            {
                noAnimationText.Text = "No animated preview";
                noAnimationText.Visibility = Visibility.Visible;
            }

            playPauseButton.Click += (_, _) =>
            {
                if (animationPreviewController == null)
                {
                    return;
                }

                sfxTriggeredInCycle = false;
                previousPositionMs = animationPreviewController.CurrentPositionMs;
                if (animationPreviewController.IsPlaying)
                {
                    animationPreviewController.Pause();
                }
                else
                {
                    if (ReadDelayMs() == 0)
                    {
                        sfxTriggeredInCycle = TryPlayConfiguredSfx();
                    }

                    animationPreviewController.Play();
                }
            };
            prevFrameButton.Click += (_, _) => animationPreviewController?.StepFrame(-1);
            nextFrameButton.Click += (_, _) => animationPreviewController?.StepFrame(1);
            timelineSlider.ValueChanged += (_, _) =>
            {
                if (!suppressTimelineSeek)
                {
                    animationPreviewController?.Seek(timelineSlider.Value);
                    sfxTriggeredInCycle = false;
                    previousPositionMs = animationPreviewController?.CurrentPositionMs ?? 0;
                }
            };
            previewLoopCheckBox.Checked += (_, _) => animationPreviewController?.SetLoop(true);
            previewLoopCheckBox.Unchecked += (_, _) => animationPreviewController?.SetLoop(false);

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

            bool? result = dialog.ShowDialog();
            animationPreviewController?.Dispose();
            dialogSfxPreviewPlayer.Stop();
            dialogSfxPreviewPlayer.Dispose();
            if (result != true)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedSfxPath) && File.Exists(selectedSfxPath))
            {
                emote.SfxName = Path.GetFileNameWithoutExtension(selectedSfxPath);
            }

            int savedDelayMs = Math.Max(0, ParseIntOrDefault(delayTextBox.Text, Math.Max(0, emote.SfxDelayMs) * Ao2TimingTickMs));
            emote.SfxDelayMs = (int)Math.Round(savedDelayMs / (double)Ao2TimingTickMs, MidpointRounding.AwayFromZero);
            emote.SfxLooping = loopCheckBox.IsChecked == true;
            emote.SfxAssetSourcePath = string.IsNullOrWhiteSpace(selectedSfxPath) ? null : selectedSfxPath;
            EnsurePreanimEmoteModifierForEmote(emote);

            emote.RefreshSfxSummary();
            StatusTextBlock.Text = "SFX config saved.";
        }

        private void ShowEmoteModsConfigDialog(CharacterCreationEmoteViewModel emote)
        {
            Window dialog = CreateEmoteDialog("Emote Mods Config", 620, 360);
            Grid grid = BuildDialogGrid(3);

            Border warningBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(164, 110, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(96, 82, 52, 19)),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6)
            };
            warningBorder.Child = new TextBlock
            {
                Text = "Warning: Emote Mods are advanced or extra AO2 options. Do not change these unless you know what you are doing.",
                Foreground = new SolidColorBrush(Color.FromRgb(246, 223, 184)),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(warningBorder, 0);
            grid.Children.Add(warningBorder);

            string selectedEmoteModName = EmoteModifierOptions.FirstOrDefault(option => option.Value == emote.EmoteModifier)?.Name
                ?? EmoteModifierOptions.First().Name;
            AutoCompleteDropdownField emoteModDropdown = AddDialogAutoCompleteField(
                grid,
                1,
                "Emote Mod",
                "Controls AO2 behavior mode for this emote animation flow.",
                EmoteModifierOptions.Select(option => option.Name),
                selectedEmoteModName,
                isReadOnly: true);

            string selectedDeskModName = DeskModifierOptions.FirstOrDefault(option => option.Value == emote.DeskModifier)?.Name
                ?? DeskModifierOptions.First().Name;
            AutoCompleteDropdownField deskModDropdown = AddDialogAutoCompleteField(
                grid,
                2,
                "Desk Mod",
                "Controls how AO2 desk visibility behaves for this emote.",
                DeskModifierOptions.Select(option => option.Name),
                selectedDeskModName,
                isReadOnly: true);

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string selectedEmoteModText = (emoteModDropdown.Text ?? string.Empty).Trim();
            string selectedDeskModText = (deskModDropdown.Text ?? string.Empty).Trim();
            NamedIntOption? emoteMod = EmoteModifierOptions.FirstOrDefault(option =>
                string.Equals(option.Name, selectedEmoteModText, StringComparison.OrdinalIgnoreCase));
            NamedIntOption? deskMod = DeskModifierOptions.FirstOrDefault(option =>
                string.Equals(option.Name, selectedDeskModText, StringComparison.OrdinalIgnoreCase));
            emote.EmoteModifier = emoteMod?.Value ?? emote.EmoteModifier;
            emote.DeskModifier = deskMod?.Value ?? emote.DeskModifier;
            StatusTextBlock.Text = "Emote mods updated.";
        }

        private void ShowButtonIconConfigDialog(CharacterCreationEmoteViewModel emote)
        {
            Window dialog = CreateEmoteDialog("Button Icon Configuration", 980, 760);
            Grid grid = BuildDialogGrid(3);

            ButtonIconGenerationConfig config = BuildButtonIconGenerationConfig(emote);

            AutoCompleteDropdownField modeDropdown = CreateDialogAutoCompleteField(
                ButtonIconModeOptionNames,
                GetButtonIconModeName(config.Mode),
                "Choose how button_on / button_off are generated.",
                isReadOnly: true);
            TextBlock modeDescriptionText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(202, 214, 228)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            StackPanel modePanel = new StackPanel();
            modePanel.Children.Add(modeDropdown);
            modePanel.Children.Add(modeDescriptionText);
            AddDialogFieldContainer(
                grid,
                0,
                "Mode",
                "Mode-driven setup keeps this popup focused and avoids irrelevant controls.",
                modePanel);

            Border modeFieldsHost = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(72, 12, 18, 24)),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(modeFieldsHost, 1);
            grid.Children.Add(modeFieldsHost);

            Image onPreviewImage = new Image { Stretch = Stretch.Uniform };
            Image offPreviewImage = new Image { Stretch = Stretch.Uniform };
            TextBlock onPreviewEmpty = CreatePreviewEmptyText("button_on preview");
            TextBlock offPreviewEmpty = CreatePreviewEmptyText("button_off preview");
            TextBlock previewHintText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(188, 204, 220)),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };

            StackPanel previewRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            previewRow.Children.Add(CreateButtonPreviewCard("button_on", onPreviewImage, onPreviewEmpty));
            previewRow.Children.Add(CreateButtonPreviewCard("button_off", offPreviewImage, offPreviewEmpty));

            StackPanel previewPanel = new StackPanel();
            previewPanel.Children.Add(previewRow);
            previewPanel.Children.Add(previewHintText);
            AddDialogFieldContainer(
                grid,
                2,
                "Button previews",
                "Live preview updates immediately as you edit settings.",
                previewPanel);

            bool PickImageAsset(string title, out string selectedPath)
            {
                selectedPath = string.Empty;
                OpenFileDialog picker = new OpenFileDialog
                {
                    Title = title,
                    Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
                };

                if (picker.ShowDialog() != true)
                {
                    return false;
                }

                selectedPath = picker.FileName;
                return true;
            }

            Border CreateStaticAssetCard(
                string title,
                Func<string?> getPath,
                Action<string?> setPath,
                bool disallowAnimated,
                string uploadTitle,
                bool isReadOnly = false)
            {
                Border card = new Border
                {
                    Width = 250,
                    Height = 270,
                    Margin = new Thickness(8, 0, 8, 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                    Padding = new Thickness(8),
                    Cursor = Cursors.Hand
                };

                Grid body = new Grid();
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock titleText = new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(Color.FromRgb(196, 208, 222)),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(titleText, 0);
                body.Children.Add(titleText);

                Grid previewGrid = new Grid();
                Image previewImage = new Image { Stretch = Stretch.UniformToFill };
                TextBlock plusText = new TextBlock
                {
                    Text = "+",
                    FontSize = 44,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(186, 202, 218)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                previewGrid.Children.Add(previewImage);
                previewGrid.Children.Add(plusText);
                Grid.SetRow(previewGrid, 1);
                body.Children.Add(previewGrid);

                TextBlock fileText = new TextBlock
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextAlignment = TextAlignment.Center
                };
                Grid.SetRow(fileText, 2);
                body.Children.Add(fileText);

                void RefreshCard()
                {
                    string path = getPath()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        previewImage.Source = null;
                        plusText.Visibility = Visibility.Visible;
                        fileText.Text = "Click to upload";
                        return;
                    }

                    if (!TryLoadButtonBitmap(path, out BitmapSource? bitmap, out bool isAnimated) || bitmap == null)
                    {
                        previewImage.Source = null;
                        plusText.Visibility = Visibility.Visible;
                        fileText.Text = "Unsupported image";
                        return;
                    }

                    previewImage.Source = bitmap;
                    plusText.Visibility = Visibility.Collapsed;
                    fileText.Text = Path.GetFileName(path) + (isAnimated ? " (animated)" : string.Empty);
                }

                card.MouseLeftButtonUp += (_, _) =>
                {
                    if (isReadOnly)
                    {
                        return;
                    }

                    if (!PickImageAsset(uploadTitle, out string selectedPath))
                    {
                        return;
                    }

                    if (disallowAnimated
                        && TryLoadButtonBitmap(selectedPath, out _, out bool isAnimated)
                        && isAnimated)
                    {
                        OceanyaMessageBox.Show(
                            dialog,
                            "This mode requires a non-animated image. Please choose a static image.",
                            "Invalid Asset",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    setPath(selectedPath);
                    RefreshModeFields();
                };

                card.Child = body;
                RefreshCard();
                return card;
            }

            TextBlock BuildModeDescription(ButtonIconMode mode)
            {
                return mode switch
                {
                    ButtonIconMode.SingleImage => new TextBlock
                    {
                        Text = "Single image: upload one non-animated base image, then generate button_off using effects.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    ButtonIconMode.TwoImages => new TextBlock
                    {
                        Text = "Two images: upload button_on and button_off directly for complete manual control.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    _ => new TextBlock
                    {
                        Text = "Automatic: build from background + cut emote layer, then apply effects to generate button_off.",
                        TextWrapping = TextWrapping.Wrap
                    }
                };
            }

            UIElement BuildEffectsSection()
            {
                StackPanel panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                AutoCompleteDropdownField effectsDropdown = CreateDialogAutoCompleteField(
                    ButtonEffectsGenerationOptionNames,
                    GetButtonEffectsGenerationName(config.EffectsMode),
                    "How button_off is generated from button_on.",
                    isReadOnly: true);
                effectsDropdown.TextValueChanged += (_, _) =>
                {
                    config.EffectsMode = ParseButtonEffectsGenerationMode(effectsDropdown.Text);
                    RefreshModeFields();
                };
                AddSimpleField(panel, "Effects generation", effectsDropdown);

                if (config.EffectsMode == ButtonEffectsGenerationMode.ReduceOpacity)
                {
                    Slider slider = new Slider
                    {
                        Minimum = 0,
                        Maximum = 100,
                        Value = Math.Clamp(config.OpacityPercent, 0, 100),
                        TickFrequency = 5,
                        IsSnapToTickEnabled = true
                    };
                    TextBlock valueText = new TextBlock
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                        Margin = new Thickness(0, 2, 0, 0),
                        Text = $"{Math.Clamp(config.OpacityPercent, 0, 100)}%"
                    };
                    slider.ValueChanged += (_, _) =>
                    {
                        config.OpacityPercent = (int)Math.Round(slider.Value);
                        valueText.Text = $"{config.OpacityPercent}%";
                        RefreshPreviews();
                    };
                    StackPanel row = new StackPanel();
                    row.Children.Add(slider);
                    row.Children.Add(valueText);
                    AddSimpleField(panel, "Off opacity (0-100%)", row);
                }
                else if (config.EffectsMode == ButtonEffectsGenerationMode.Darken)
                {
                    Slider slider = new Slider
                    {
                        Minimum = 0,
                        Maximum = 100,
                        Value = Math.Clamp(config.DarknessPercent, 0, 100),
                        TickFrequency = 5,
                        IsSnapToTickEnabled = true
                    };
                    TextBlock valueText = new TextBlock
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                        Margin = new Thickness(0, 2, 0, 0),
                        Text = $"{Math.Clamp(config.DarknessPercent, 0, 100)}%"
                    };
                    slider.ValueChanged += (_, _) =>
                    {
                        config.DarknessPercent = (int)Math.Round(slider.Value);
                        valueText.Text = $"{config.DarknessPercent}%";
                        RefreshPreviews();
                    };
                    StackPanel row = new StackPanel();
                    row.Children.Add(slider);
                    row.Children.Add(valueText);
                    AddSimpleField(panel, "Darkness (0-100%)", row);
                }
                else if (config.EffectsMode == ButtonEffectsGenerationMode.Overlay)
                {
                    Border overlayCard = CreateStaticAssetCard(
                        "Overlay asset",
                        () => config.OverlayImagePath,
                        value => config.OverlayImagePath = value,
                        disallowAnimated: false,
                        uploadTitle: "Select overlay asset");
                    AddSimpleField(panel, "Overlay image", overlayCard);
                }

                return panel;
            }

            void RefreshModeFields()
            {
                modeDescriptionText.Text = BuildModeDescription(config.Mode).Text;

                StackPanel host = new StackPanel();
                if (config.Mode == ButtonIconMode.SingleImage)
                {
                    StackPanel cardRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    cardRow.Children.Add(CreateStaticAssetCard(
                        "Base image (non-animated)",
                        () => config.SingleImagePath,
                        value => config.SingleImagePath = value,
                        disallowAnimated: true,
                        uploadTitle: "Select base button image"));
                    host.Children.Add(cardRow);
                    host.Children.Add(BuildEffectsSection());
                }
                else if (config.Mode == ButtonIconMode.TwoImages)
                {
                    StackPanel cardRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    cardRow.Children.Add(CreateStaticAssetCard(
                        "button_on",
                        () => config.TwoImagesOnPath,
                        value => config.TwoImagesOnPath = value,
                        disallowAnimated: false,
                        uploadTitle: "Select button_on image"));
                    cardRow.Children.Add(CreateStaticAssetCard(
                        "button_off",
                        () => config.TwoImagesOffPath,
                        value => config.TwoImagesOffPath = value,
                        disallowAnimated: false,
                        uploadTitle: "Select button_off image"));
                    host.Children.Add(cardRow);
                }
                else
                {
                    TabControl automaticTabs = new TabControl
                    {
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    if (TryFindResource("DialogTabControlStyle") is Style dialogTabControlStyle)
                    {
                        automaticTabs.Style = dialogTabControlStyle;
                    }
                    Style? dialogTabItemStyle = TryFindResource("DialogTabItemStyle") as Style;

                    StackPanel backgroundTabContent = new StackPanel();
                    AutoCompleteDropdownField backgroundDropdown = CreateDialogAutoCompleteField(
                        GetAutomaticBackgroundOptions(),
                        GetAutomaticBackgroundSelectionName(config),
                        "Background for automatic generation.",
                        isReadOnly: true);
                    backgroundDropdown.TextValueChanged += (_, _) =>
                    {
                        ApplyAutomaticBackgroundSelection(backgroundDropdown.Text, config);
                        RefreshModeFields();
                    };
                    AddSimpleField(backgroundTabContent, "Background config", backgroundDropdown);

                    if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.PresetList)
                    {
                        string presetPath = ResolveBackgroundPresetPath(config.AutomaticBackgroundPreset);
                        Border presetPreviewCard = CreateStaticAssetCard(
                            "Preset background",
                            () => presetPath,
                            _ => { },
                            disallowAnimated: false,
                            uploadTitle: "Preset",
                            isReadOnly: true);
                        presetPreviewCard.Cursor = Cursors.Arrow;
                        AddSimpleField(backgroundTabContent, "Preset preview", presetPreviewCard);
                    }
                    else if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.SolidColor)
                    {
                        Grid solidGrid = new Grid();
                        solidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        solidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        solidGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        Button pickColorButton = CreateDialogButton("Pick color...", isPrimary: false);
                        pickColorButton.Width = 130;
                        pickColorButton.Click += (_, _) =>
                        {
                            Color? picked = ShowAdvancedColorPickerDialog(config.AutomaticSolidColor);
                            if (!picked.HasValue)
                            {
                                return;
                            }

                            config.AutomaticSolidColor = picked.Value;
                            RefreshModeFields();
                        };
                        Grid.SetColumn(pickColorButton, 0);
                        solidGrid.Children.Add(pickColorButton);

                        Border colorPreview = new Border
                        {
                            Width = 48,
                            Height = 30,
                            Margin = new Thickness(8, 0, 8, 0),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(84, 104, 126)),
                            BorderThickness = new Thickness(1),
                            Background = new SolidColorBrush(config.AutomaticSolidColor)
                        };
                        Grid.SetColumn(colorPreview, 1);
                        solidGrid.Children.Add(colorPreview);

                        TextBlock colorValue = new TextBlock
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                            Text = ToHexColor(config.AutomaticSolidColor)
                        };
                        Grid.SetColumn(colorValue, 2);
                        solidGrid.Children.Add(colorValue);
                        AddSimpleField(backgroundTabContent, "Solid color", solidGrid);
                    }
                    else if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.Upload)
                    {
                        Border uploadCard = CreateStaticAssetCard(
                            "Background upload",
                            () => config.AutomaticBackgroundUploadPath,
                            value => config.AutomaticBackgroundUploadPath = value,
                            disallowAnimated: false,
                            uploadTitle: "Select automatic background image");
                        AddSimpleField(backgroundTabContent, "Background image", uploadCard);
                    }

                    StackPanel emoteTabContent = new StackPanel();
                    Button cutButton = CreateDialogButton("Open Emote Cutting...", isPrimary: false);
                    cutButton.Width = 190;
                    cutButton.Click += (_, _) =>
                    {
                        BitmapSource? cutout = ShowEmoteCuttingDialog(emote, config.AutomaticCutEmoteImage);
                        if (cutout != null)
                        {
                            config.AutomaticCutEmoteImage = cutout;
                            RefreshPreviews();
                            RefreshModeFields();
                        }
                    };
                    TextBlock cutStatus = new TextBlock
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(186, 202, 218)),
                        Margin = new Thickness(0, 6, 0, 0),
                        Text = config.AutomaticCutEmoteImage == null
                            ? "No cut emote selected yet."
                            : $"Cut emote ready ({config.AutomaticCutEmoteImage.PixelWidth}x{config.AutomaticCutEmoteImage.PixelHeight})"
                    };
                    StackPanel cutPanel = new StackPanel();
                    cutPanel.Children.Add(cutButton);
                    cutPanel.Children.Add(cutStatus);
                    AddSimpleField(emoteTabContent, "Emote cutting", cutPanel);

                    StackPanel effectTabContent = new StackPanel();
                    effectTabContent.Children.Add(BuildEffectsSection());

                    automaticTabs.Items.Add(new TabItem
                    {
                        Header = "Background",
                        Content = backgroundTabContent,
                        Style = dialogTabItemStyle
                    });
                    automaticTabs.Items.Add(new TabItem
                    {
                        Header = "Emote",
                        Content = emoteTabContent,
                        Style = dialogTabItemStyle
                    });
                    automaticTabs.Items.Add(new TabItem
                    {
                        Header = "Effect",
                        Content = effectTabContent,
                        Style = dialogTabItemStyle
                    });
                    host.Children.Add(automaticTabs);
                }

                modeFieldsHost.Child = host;
                RefreshPreviews();
            }

            void RefreshPreviews()
            {
                if (TryBuildButtonIconPair(config, out BitmapSource? onImage, out BitmapSource? offImage, out string? validation))
                {
                    onPreviewImage.Source = onImage;
                    offPreviewImage.Source = offImage;
                    onPreviewEmpty.Visibility = onImage == null ? Visibility.Visible : Visibility.Collapsed;
                    offPreviewEmpty.Visibility = offImage == null ? Visibility.Visible : Visibility.Collapsed;
                    previewHintText.Text = "Live previews update immediately.";
                    previewHintText.Foreground = new SolidColorBrush(Color.FromRgb(188, 204, 220));
                }
                else
                {
                    onPreviewImage.Source = null;
                    offPreviewImage.Source = null;
                    onPreviewEmpty.Visibility = Visibility.Visible;
                    offPreviewEmpty.Visibility = Visibility.Visible;
                    previewHintText.Text = string.IsNullOrWhiteSpace(validation) ? "Configure assets to preview button generation." : validation;
                    previewHintText.Foreground = new SolidColorBrush(Color.FromRgb(236, 190, 152));
                }
            }

            modeDropdown.TextValueChanged += (_, _) =>
            {
                config.Mode = ParseButtonIconMode(modeDropdown.Text);
                RefreshModeFields();
            };

            RefreshModeFields();

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (!TryBuildButtonIconPair(config, out BitmapSource? savedOn, out _, out string? validationMessage) || savedOn == null)
            {
                OceanyaMessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(validationMessage)
                        ? "Button icon configuration is incomplete."
                        : validationMessage,
                    "Button Icon Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ApplyButtonIconGenerationConfig(emote, config);
            emote.ButtonIconPreview = savedOn;
            emote.ButtonIconAssetSourcePath = ResolveRepresentativeButtonSourcePath(config);
            emote.ButtonIconToken = $"button_{emote.Index}";
            UpdateButtonIconsSectionVisibility();
            StatusTextBlock.Text = "Button icon configuration saved.";
        }

        private BitmapSource? ShowEmoteCuttingDialog(CharacterCreationEmoteViewModel emote, BitmapSource? existingCutout)
        {
            List<CutSourceOption> sourceOptions = new List<CutSourceOption>();
            if (!string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath))
            {
                sourceOptions.Add(new CutSourceOption("Preanim", emote.PreAnimationAssetSourcePath));
            }

            if (!string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath))
            {
                sourceOptions.Add(new CutSourceOption("Idle", emote.FinalAnimationIdleAssetSourcePath));
            }
            else if (!string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath))
            {
                sourceOptions.Add(new CutSourceOption("Idle (shared)", emote.AnimationAssetSourcePath));
            }

            if (!string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath))
            {
                sourceOptions.Add(new CutSourceOption("Talking", emote.FinalAnimationTalkingAssetSourcePath));
            }

            sourceOptions = sourceOptions
                .Where(static option => !string.IsNullOrWhiteSpace(option.Path) && File.Exists(option.Path))
                .GroupBy(static option => option.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToList();
            if (sourceOptions.Count == 0)
            {
                OceanyaMessageBox.Show(
                    this,
                    "No emote image sources are available yet. Add preanim/idle/talking assets first.",
                    "Emote Cutting",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return null;
            }

            Window dialog = CreateEmoteDialog("Emote Cutting", 940, 700);
            Grid grid = BuildDialogGrid(3);

            AutoCompleteDropdownField sourceDropdown = CreateDialogAutoCompleteField(
                sourceOptions.Select(static option => option.Name),
                sourceOptions[0].Name,
                "Choose emote source image for cutting.",
                isReadOnly: true);
            AddDialogFieldContainer(grid, 0, "Source", "Current emote assets used for cutout extraction.", sourceDropdown);

            Border previewBorder = new Border
            {
                Height = 390,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                Padding = new Thickness(6)
            };
            Grid previewGrid = new Grid();
            Image previewImage = new Image { Stretch = Stretch.Uniform };
            Canvas selectionCanvas = new Canvas { Background = Brushes.Transparent };
            System.Windows.Shapes.Rectangle selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(148, 220, 255)),
                Fill = new SolidColorBrush(Color.FromArgb(42, 96, 182, 226)),
                StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed
            };
            selectionCanvas.Children.Add(selectionRect);
            previewGrid.Children.Add(previewImage);
            previewGrid.Children.Add(selectionCanvas);
            previewBorder.Child = previewGrid;

            Button playPauseButton = CreateTimelineSymbolButton("▶", "Play/pause preview.");
            Button prevFrameButton = CreateTimelineSymbolButton("⏮", "Go back one frame.");
            Button nextFrameButton = CreateTimelineSymbolButton("⏭", "Go forward one frame.");
            Slider timelineSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1000,
                Value = 0,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            CheckBox loopCheckBox = new CheckBox
            {
                Content = "Loop",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ApplyDialogCheckBoxStyle(loopCheckBox);

            Grid timelineControls = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timelineControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(playPauseButton, 0);
            Grid.SetColumn(prevFrameButton, 1);
            Grid.SetColumn(nextFrameButton, 2);
            Grid.SetColumn(timelineSlider, 3);
            Grid.SetColumn(loopCheckBox, 4);
            timelineControls.Children.Add(playPauseButton);
            timelineControls.Children.Add(prevFrameButton);
            timelineControls.Children.Add(nextFrameButton);
            timelineControls.Children.Add(timelineSlider);
            timelineControls.Children.Add(loopCheckBox);

            TextBlock tipText = new TextBlock
            {
                Text = "Drag to draw a square selection. Use the timeline for animated assets before cropping.",
                Foreground = new SolidColorBrush(Color.FromRgb(186, 202, 218)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };

            StackPanel previewPanel = new StackPanel();
            previewPanel.Children.Add(previewBorder);
            previewPanel.Children.Add(timelineControls);
            previewPanel.Children.Add(tipText);
            AddDialogFieldContainer(grid, 1, "Frame + Crop", "Select a frame and draw the crop region.", previewPanel);

            Border lastCutoutBorder = new Border
            {
                Width = 96,
                Height = 96,
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 92, 112)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(110, 20, 20, 20)),
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Image lastCutoutImage = new Image
            {
                Stretch = Stretch.Uniform,
                Source = existingCutout
            };
            lastCutoutBorder.Child = lastCutoutImage;

            Border currentCutoutBorder = new Border
            {
                Width = 96,
                Height = 96,
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 92, 112)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(110, 20, 20, 20)),
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Image currentCutoutImage = new Image
            {
                Stretch = Stretch.Uniform
            };
            currentCutoutBorder.Child = currentCutoutImage;

            StackPanel cutoutPreviewRow = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            StackPanel lastColumn = new StackPanel();
            lastColumn.Children.Add(new TextBlock
            {
                Text = "Last cutout",
                Foreground = new SolidColorBrush(Color.FromRgb(188, 204, 220)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            lastColumn.Children.Add(lastCutoutBorder);
            StackPanel currentColumn = new StackPanel();
            currentColumn.Children.Add(new TextBlock
            {
                Text = "Current cutout",
                Foreground = new SolidColorBrush(Color.FromRgb(188, 204, 220)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            currentColumn.Children.Add(currentCutoutBorder);
            cutoutPreviewRow.Children.Add(lastColumn);
            cutoutPreviewRow.Children.Add(currentColumn);
            AddDialogFieldContainer(grid, 2, "Cutout previews", "Last saved cutout and the current in-progress cutout.", cutoutPreviewRow);

            AnimationTimelinePreviewController? previewController = null;
            BitmapSource? currentFrame = null;
            bool suppressTimelineSeek = false;
            Point dragStart;
            bool dragging = false;
            Rect selectionBounds = Rect.Empty;
            BitmapSource? currentSelectionCutout = null;

            Rect ComputeImageViewport(BitmapSource frame)
            {
                double hostWidth = Math.Max(1, selectionCanvas.ActualWidth);
                double hostHeight = Math.Max(1, selectionCanvas.ActualHeight);
                double frameWidth = Math.Max(1, frame.PixelWidth);
                double frameHeight = Math.Max(1, frame.PixelHeight);
                double scale = Math.Min(hostWidth / frameWidth, hostHeight / frameHeight);
                double drawWidth = frameWidth * scale;
                double drawHeight = frameHeight * scale;
                double x = (hostWidth - drawWidth) * 0.5;
                double y = (hostHeight - drawHeight) * 0.5;
                return new Rect(x, y, drawWidth, drawHeight);
            }

            void UpdateSelectionVisual()
            {
                if (selectionBounds.IsEmpty || selectionBounds.Width < 2 || selectionBounds.Height < 2)
                {
                    selectionRect.Visibility = Visibility.Collapsed;
                    currentCutoutImage.Source = null;
                    currentSelectionCutout = null;
                    return;
                }

                selectionRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(selectionRect, selectionBounds.X);
                Canvas.SetTop(selectionRect, selectionBounds.Y);
                selectionRect.Width = selectionBounds.Width;
                selectionRect.Height = selectionBounds.Height;
            }

            void UpdateCurrentCutoutPreview()
            {
                currentSelectionCutout = null;
                currentCutoutImage.Source = null;
                if (currentFrame == null)
                {
                    return;
                }

                Rect viewport = ComputeImageViewport(currentFrame);
                Rect selection = Rect.Intersect(selectionBounds, viewport);
                if (selection.IsEmpty || selection.Width < 4 || selection.Height < 4)
                {
                    return;
                }

                double normalizedX = (selection.X - viewport.X) / viewport.Width;
                double normalizedY = (selection.Y - viewport.Y) / viewport.Height;
                double normalizedSize = selection.Width / viewport.Width;
                int cropX = Math.Max(0, (int)Math.Round(normalizedX * currentFrame.PixelWidth));
                int cropY = Math.Max(0, (int)Math.Round(normalizedY * currentFrame.PixelHeight));
                int cropSize = Math.Max(1, (int)Math.Round(normalizedSize * currentFrame.PixelWidth));
                cropSize = Math.Min(cropSize, Math.Min(currentFrame.PixelWidth - cropX, currentFrame.PixelHeight - cropY));
                if (cropSize <= 0)
                {
                    return;
                }

                CroppedBitmap cropped = new CroppedBitmap(currentFrame, new Int32Rect(cropX, cropY, cropSize, cropSize));
                cropped.Freeze();
                currentSelectionCutout = cropped;
                currentCutoutImage.Source = cropped;
            }

            void LoadSource(string path)
            {
                previewController?.Dispose();
                previewController = null;
                currentFrame = null;
                selectionBounds = Rect.Empty;
                UpdateSelectionVisual();

                if (AnimationTimelinePreviewController.TryCreate(ResolveAo2PreviewImagePath(path), out AnimationTimelinePreviewController? createdController))
                {
                    previewController = createdController;
                    currentFrame = createdController.CurrentFrame as BitmapSource;
                    previewImage.Source = createdController.CurrentFrame;
                    timelineSlider.Maximum = Math.Max(1, createdController.EffectiveDurationMs);
                    createdController.SetLoop(loopCheckBox.IsChecked == true);
                    createdController.PlaybackStateChanged += isPlaying => Dispatcher.Invoke(() =>
                    {
                        playPauseButton.Content = isPlaying ? "⏸" : "▶";
                    });
                    createdController.PositionChanged += (frame, positionMs) => Dispatcher.Invoke(() =>
                    {
                        previewImage.Source = frame;
                        currentFrame = frame as BitmapSource;
                        suppressTimelineSeek = true;
                        timelineSlider.Value = Math.Clamp(positionMs, timelineSlider.Minimum, timelineSlider.Maximum);
                        suppressTimelineSeek = false;
                    });
                }
                else if (TryLoadButtonBitmap(path, out BitmapSource? bitmap, out _))
                {
                    currentFrame = bitmap;
                    previewImage.Source = bitmap;
                    timelineSlider.Value = 0;
                    timelineSlider.Maximum = 1;
                }

                bool interactive = previewController?.HasTimeline == true;
                playPauseButton.IsEnabled = interactive;
                prevFrameButton.IsEnabled = interactive;
                nextFrameButton.IsEnabled = interactive;
                timelineSlider.IsEnabled = interactive;
                loopCheckBox.IsEnabled = interactive;
            }

            sourceDropdown.TextValueChanged += (_, _) =>
            {
                CutSourceOption? selected = sourceOptions.FirstOrDefault(option =>
                    string.Equals(option.Name, sourceDropdown.Text, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                {
                    LoadSource(selected.Path);
                }
            };

            selectionCanvas.MouseLeftButtonDown += (_, e) =>
            {
                if (currentFrame == null)
                {
                    return;
                }

                dragging = true;
                dragStart = e.GetPosition(selectionCanvas);
                selectionBounds = new Rect(dragStart, dragStart);
                UpdateSelectionVisual();
                selectionCanvas.CaptureMouse();
            };
            selectionCanvas.MouseMove += (_, e) =>
            {
                if (!dragging)
                {
                    return;
                }

                Point current = e.GetPosition(selectionCanvas);
                double dx = current.X - dragStart.X;
                double dy = current.Y - dragStart.Y;
                double size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                double width = Math.Sign(dx) * size;
                double height = Math.Sign(dy) * size;
                Point end = new Point(dragStart.X + width, dragStart.Y + height);
                selectionBounds = new Rect(dragStart, end);
                UpdateSelectionVisual();
            };
            selectionCanvas.MouseLeftButtonUp += (_, _) =>
            {
                dragging = false;
                if (selectionCanvas.IsMouseCaptured)
                {
                    selectionCanvas.ReleaseMouseCapture();
                }

                UpdateCurrentCutoutPreview();
            };

            playPauseButton.Click += (_, _) =>
            {
                if (previewController == null)
                {
                    return;
                }

                if (previewController.IsPlaying)
                {
                    previewController.Pause();
                }
                else
                {
                    previewController.Play();
                }
            };
            prevFrameButton.Click += (_, _) => previewController?.StepFrame(-1);
            nextFrameButton.Click += (_, _) => previewController?.StepFrame(1);
            timelineSlider.ValueChanged += (_, _) =>
            {
                if (!suppressTimelineSeek)
                {
                    previewController?.Seek(timelineSlider.Value);
                }
            };
            loopCheckBox.Checked += (_, _) => previewController?.SetLoop(true);
            loopCheckBox.Unchecked += (_, _) => previewController?.SetLoop(false);

            CutSourceOption initial = sourceOptions[0];
            sourceDropdown.Text = initial.Name;
            LoadSource(initial.Path);

            DockPanel buttons = BuildDialogButtons(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            bool? result = dialog.ShowDialog();
            previewController?.Dispose();
            if (result != true)
            {
                return null;
            }

            if (currentFrame == null)
            {
                return null;
            }

            Rect viewport = ComputeImageViewport(currentFrame);
            Rect effectiveSelection = selectionBounds;
            if ((effectiveSelection.IsEmpty || effectiveSelection.Width < 2 || effectiveSelection.Height < 2)
                && selectionRect.Visibility == Visibility.Visible)
            {
                effectiveSelection = new Rect(
                    Canvas.GetLeft(selectionRect),
                    Canvas.GetTop(selectionRect),
                    selectionRect.Width,
                    selectionRect.Height);
            }

            Rect selection = Rect.Intersect(effectiveSelection, viewport);
            if (selection.IsEmpty || selection.Width < 4 || selection.Height < 4)
            {
                OceanyaMessageBox.Show(
                    this,
                    "Draw a square selection over the emote image before saving the cutout.",
                    "Emote Cutting",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            if (currentSelectionCutout != null)
            {
                return currentSelectionCutout;
            }

            double normalizedX = (selection.X - viewport.X) / viewport.Width;
            double normalizedY = (selection.Y - viewport.Y) / viewport.Height;
            double normalizedSize = selection.Width / viewport.Width;
            int cropX = Math.Max(0, (int)Math.Round(normalizedX * currentFrame.PixelWidth));
            int cropY = Math.Max(0, (int)Math.Round(normalizedY * currentFrame.PixelHeight));
            int cropSize = Math.Max(1, (int)Math.Round(normalizedSize * currentFrame.PixelWidth));
            cropSize = Math.Min(cropSize, Math.Min(currentFrame.PixelWidth - cropX, currentFrame.PixelHeight - cropY));
            if (cropSize <= 0)
            {
                return null;
            }

            CroppedBitmap cropped = new CroppedBitmap(currentFrame, new Int32Rect(cropX, cropY, cropSize, cropSize));
            cropped.Freeze();
            return cropped;
        }

        private Color? ShowAdvancedColorPickerDialog(Color initialColor)
        {
            Window dialog = CreateEmoteDialog("Solid Color Picker", 760, 680);
            Grid root = BuildDialogGrid(5);

            byte startingAlpha = initialColor.A == 0 ? (byte)255 : initialColor.A;
            Color seeded = Color.FromArgb(startingAlpha, initialColor.R, initialColor.G, initialColor.B);
            ColorToHsv(seeded, out double hue, out double saturation, out double value);
            byte alpha = startingAlpha;

            const double wheelSize = 320;
            const double ringThickness = 30;
            const double squareSize = 206;
            double center = wheelSize / 2.0;
            double ringOuterRadius = wheelSize / 2.0;
            double ringInnerRadius = ringOuterRadius - ringThickness;

            Grid pickerHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = wheelSize,
                Height = wheelSize
            };

            Image hueWheelImage = new Image
            {
                Width = wheelSize,
                Height = wheelSize,
                Source = CreateHueWheelBitmap((int)wheelSize, (int)ringThickness),
                Stretch = Stretch.Fill
            };
            pickerHost.Children.Add(hueWheelImage);

            Border squareBorder = new Border
            {
                Width = squareSize,
                Height = squareSize,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 102, 126)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            Grid squareGrid = new Grid();
            System.Windows.Shapes.Rectangle hueRect = new System.Windows.Shapes.Rectangle();
            System.Windows.Shapes.Rectangle whiteOverlay = new System.Windows.Shapes.Rectangle
            {
                Fill = new LinearGradientBrush(
                    Colors.White,
                    Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0.5),
                    new Point(1, 0.5))
            };
            System.Windows.Shapes.Rectangle blackOverlay = new System.Windows.Shapes.Rectangle
            {
                Fill = new LinearGradientBrush(
                    Color.FromArgb(0, 0, 0, 0),
                    Colors.Black,
                    new Point(0.5, 0),
                    new Point(0.5, 1))
            };
            squareGrid.Children.Add(hueRect);
            squareGrid.Children.Add(whiteOverlay);
            squareGrid.Children.Add(blackOverlay);

            Canvas overlayCanvas = new Canvas
            {
                Width = wheelSize,
                Height = wheelSize,
                Background = Brushes.Transparent
            };
            System.Windows.Shapes.Ellipse squareMarker = new System.Windows.Shapes.Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = Brushes.White,
                Fill = Brushes.Transparent,
                StrokeThickness = 2
            };
            System.Windows.Shapes.Ellipse ringMarker = new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = Brushes.White,
                Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
                StrokeThickness = 2
            };
            overlayCanvas.Children.Add(squareMarker);
            overlayCanvas.Children.Add(ringMarker);

            squareBorder.Child = squareGrid;
            pickerHost.Children.Add(squareBorder);
            pickerHost.Children.Add(overlayCanvas);
            AddDialogFieldContainer(root, 0, "Color", "Use the hue wheel + center square.", pickerHost);

            Grid rgbRow = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            rgbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rgbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rgbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox rBox = CreateDialogTextBox("255", "Red (0-255)");
            TextBox gBox = CreateDialogTextBox("255", "Green (0-255)");
            TextBox bBox = CreateDialogTextBox("255", "Blue (0-255)");
            rBox.Width = 86;
            gBox.Width = 86;
            bBox.Width = 86;
            rBox.Margin = new Thickness(0, 0, 8, 0);
            gBox.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(rBox, 0);
            Grid.SetColumn(gBox, 1);
            Grid.SetColumn(bBox, 2);
            rgbRow.Children.Add(rBox);
            rgbRow.Children.Add(gBox);
            rgbRow.Children.Add(bBox);
            AddDialogFieldContainer(root, 1, "R / G / B", "Enter numeric RGB values (0-255).", rgbRow);

            TextBox hexTextBox = CreateDialogTextBox(ToHexColor(seeded), "Paste #RRGGBB or #AARRGGBB.");
            AddDialogFieldContainer(root, 2, "Hex", "Hex input is supported.", hexTextBox);

            Grid alphaRow = new Grid();
            alphaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            alphaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            alphaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock alphaMinText = new TextBlock
            {
                Text = "0",
                Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Slider alphaSlider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                Value = alpha,
                TickFrequency = 5,
                IsSnapToTickEnabled = false
            };
            TextBlock alphaMaxText = new TextBlock
            {
                Text = "255",
                Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(alphaMinText, 0);
            Grid.SetColumn(alphaSlider, 1);
            Grid.SetColumn(alphaMaxText, 2);
            alphaRow.Children.Add(alphaMinText);
            alphaRow.Children.Add(alphaSlider);
            alphaRow.Children.Add(alphaMaxText);
            AddDialogFieldContainer(root, 3, "Alpha", "Opacity from transparent (0) to fully opaque (255).", alphaRow);

            Border preview = new Border
            {
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                BorderBrush = new SolidColorBrush(Color.FromRgb(84, 104, 126)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            AddDialogFieldContainer(root, 4, "Preview", "Final color preview.", preview);

            bool internalUpdate = false;
            string dragMode = string.Empty;
            double squareLeft = (wheelSize - squareSize) / 2.0;
            double squareTop = (wheelSize - squareSize) / 2.0;

            Color CurrentColor() => HsvToColor(hue, saturation, value, alpha);

            void SetFromRgbColor(Color color)
            {
                ColorToHsv(color, out double nextHue, out double nextSat, out double nextVal);
                hue = nextHue;
                saturation = nextSat;
                value = nextVal;
                alpha = color.A;
            }

            void UpdateVisuals()
            {
                internalUpdate = true;
                Color color = CurrentColor();
                hueRect.Fill = new SolidColorBrush(HsvToColor(hue, 1, 1, 255));
                preview.Background = new SolidColorBrush(color);
                rBox.Text = color.R.ToString();
                gBox.Text = color.G.ToString();
                bBox.Text = color.B.ToString();
                hexTextBox.Text = ToHexColor(color);
                alphaSlider.Value = alpha;

                double sqX = squareLeft + (saturation * squareSize);
                double sqY = squareTop + ((1.0 - value) * squareSize);
                Canvas.SetLeft(squareMarker, sqX - (squareMarker.Width / 2.0));
                Canvas.SetTop(squareMarker, sqY - (squareMarker.Height / 2.0));

                double ringRadius = ringInnerRadius + (ringThickness / 2.0);
                double radians = (hue * Math.PI / 180.0);
                double ringX = center + Math.Cos(radians) * ringRadius;
                double ringY = center - Math.Sin(radians) * ringRadius;
                Canvas.SetLeft(ringMarker, ringX - (ringMarker.Width / 2.0));
                Canvas.SetTop(ringMarker, ringY - (ringMarker.Height / 2.0));
                internalUpdate = false;
            }

            bool TryParseByteText(TextBox box, out byte valueOut)
            {
                valueOut = 0;
                if (!byte.TryParse((box.Text ?? string.Empty).Trim(), out byte parsed))
                {
                    return false;
                }

                valueOut = parsed;
                return true;
            }

            void ApplyRgbTextBoxes()
            {
                if (!TryParseByteText(rBox, out byte rr)
                    || !TryParseByteText(gBox, out byte gg)
                    || !TryParseByteText(bBox, out byte bb))
                {
                    UpdateVisuals();
                    return;
                }

                SetFromRgbColor(Color.FromArgb(alpha, rr, gg, bb));
                UpdateVisuals();
            }

            void ApplyHexTextBox()
            {
                if (!TryParseColor(hexTextBox.Text, out Color parsed))
                {
                    UpdateVisuals();
                    return;
                }

                SetFromRgbColor(parsed);
                UpdateVisuals();
            }

            void UpdateHueFromPoint(Point point)
            {
                double dx = point.X - center;
                double dy = center - point.Y;
                double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
                hue = (angle + 360.0) % 360.0;
                UpdateVisuals();
            }

            void UpdateSvFromPoint(Point point)
            {
                double sx = Math.Clamp((point.X - squareLeft) / squareSize, 0.0, 1.0);
                double sy = Math.Clamp((point.Y - squareTop) / squareSize, 0.0, 1.0);
                saturation = sx;
                value = 1.0 - sy;
                UpdateVisuals();
            }

            overlayCanvas.MouseLeftButtonDown += (_, e) =>
            {
                Point point = e.GetPosition(overlayCanvas);
                double dx = point.X - center;
                double dy = point.Y - center;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                Rect squareRect = new Rect(squareLeft, squareTop, squareSize, squareSize);
                if (distance >= ringInnerRadius && distance <= ringOuterRadius)
                {
                    dragMode = "hue";
                    UpdateHueFromPoint(point);
                }
                else if (squareRect.Contains(point))
                {
                    dragMode = "sv";
                    UpdateSvFromPoint(point);
                }
                else
                {
                    dragMode = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(dragMode))
                {
                    overlayCanvas.CaptureMouse();
                }
            };
            overlayCanvas.MouseMove += (_, e) =>
            {
                if (!overlayCanvas.IsMouseCaptured || string.IsNullOrWhiteSpace(dragMode))
                {
                    return;
                }

                Point point = e.GetPosition(overlayCanvas);
                if (string.Equals(dragMode, "hue", StringComparison.Ordinal))
                {
                    UpdateHueFromPoint(point);
                }
                else
                {
                    UpdateSvFromPoint(point);
                }
            };
            overlayCanvas.MouseLeftButtonUp += (_, e) =>
            {
                if (!overlayCanvas.IsMouseCaptured)
                {
                    return;
                }

                Point point = e.GetPosition(overlayCanvas);
                if (string.Equals(dragMode, "hue", StringComparison.Ordinal))
                {
                    UpdateHueFromPoint(point);
                }
                else if (string.Equals(dragMode, "sv", StringComparison.Ordinal))
                {
                    UpdateSvFromPoint(point);
                }

                dragMode = string.Empty;
                overlayCanvas.ReleaseMouseCapture();
            };

            alphaSlider.ValueChanged += (_, _) =>
            {
                if (internalUpdate)
                {
                    return;
                }

                alpha = (byte)Math.Round(alphaSlider.Value);
                UpdateVisuals();
            };

            rBox.LostFocus += (_, _) =>
            {
                if (!internalUpdate)
                {
                    ApplyRgbTextBoxes();
                }
            };
            gBox.LostFocus += (_, _) =>
            {
                if (!internalUpdate)
                {
                    ApplyRgbTextBoxes();
                }
            };
            bBox.LostFocus += (_, _) =>
            {
                if (!internalUpdate)
                {
                    ApplyRgbTextBoxes();
                }
            };
            rBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyRgbTextBoxes();
                    e.Handled = true;
                }
            };
            gBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyRgbTextBoxes();
                    e.Handled = true;
                }
            };
            bBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyRgbTextBoxes();
                    e.Handled = true;
                }
            };
            hexTextBox.LostFocus += (_, _) =>
            {
                if (!internalUpdate)
                {
                    ApplyHexTextBox();
                }
            };
            hexTextBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyHexTextBox();
                    e.Handled = true;
                }
            };

            UpdateVisuals();
            DockPanel buttons = BuildDialogButtonsCentered(dialog);
            dialog.Content = BuildStyledDialogContent(dialog, dialog.Title, root, buttons);
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return CurrentColor();
        }

        private Window CreateEmoteDialog(string title, double width, double height)
        {
            Window dialog = new Window
            {
                Owner = this,
                Title = title,
                Width = width,
                Height = height,
                MinWidth = Math.Min(width, 520),
                MinHeight = Math.Min(height, 240),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                ShowInTaskbar = false
            };
            WindowChrome.SetWindowChrome(
                dialog,
                new WindowChrome
                {
                    CaptionHeight = 30,
                    ResizeBorderThickness = new Thickness(6),
                    CornerRadius = new CornerRadius(0),
                    GlassFrameThickness = new Thickness(0),
                    UseAeroCaptionButtons = false
                });
            return dialog;
        }

        private static Grid BuildDialogGrid(int rowCount)
        {
            Grid grid = new Grid
            {
                Margin = new Thickness(12, 8, 12, 2)
            };

            for (int i = 0; i < rowCount; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            return grid;
        }

        private static TextBlock CreateDialogLabel(string text, string? toolTip = null)
        {
            return new TextBlock
            {
                Text = text,
                ToolTip = toolTip,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 245)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 2)
            };
        }

        private TextBox AddDialogTextField(Grid grid, int row, string label, string value, string? toolTip)
        {
            TextBox textBox = CreateDialogTextBox(value, toolTip);
            AddDialogFieldContainer(grid, row, label, toolTip, textBox);
            return textBox;
        }

        private TextBox CreateDialogTextBox(string value, string? toolTip)
        {
            TextBox textBox = new TextBox
            {
                Text = value ?? string.Empty,
                ToolTip = toolTip,
                Margin = new Thickness(0, 0, 0, 1),
                Height = 30
            };
            ApplyDialogTextBoxStyle(textBox);
            return textBox;
        }

        private Button CreateTimelineSymbolButton(string symbol, string toolTip)
        {
            Button button = CreateDialogButton(symbol, isPrimary: false);
            button.Width = 36;
            button.Height = 30;
            button.Margin = new Thickness(0, 0, 4, 0);
            button.FontSize = 14;
            button.FontWeight = FontWeights.SemiBold;
            button.ToolTip = toolTip;
            return button;
        }

        private AutoCompleteDropdownField AddDialogAutoCompleteField(
            Grid grid,
            int row,
            string label,
            string? toolTip,
            IEnumerable<string> options,
            string value,
            bool isReadOnly)
        {
            AutoCompleteDropdownField field = CreateDialogAutoCompleteField(options, value, toolTip, isReadOnly);
            AddDialogFieldContainer(grid, row, label, toolTip, field);
            return field;
        }

        private AutoCompleteDropdownField CreateDialogAutoCompleteField(
            IEnumerable<string> options,
            string value,
            string? toolTip,
            bool isReadOnly)
        {
            AutoCompleteDropdownField field = new AutoCompleteDropdownField
            {
                Text = value ?? string.Empty,
                ItemsSource = options?.ToArray() ?? Array.Empty<string>(),
                IsTextReadOnly = isReadOnly,
                ToolTip = toolTip,
                Margin = new Thickness(0, 0, 0, 2)
            };
            return field;
        }

        private CheckBox AddOptionalDialogFieldContainer(
            Grid grid,
            int row,
            string label,
            string? toolTip,
            FrameworkElement field,
            bool enabledInitially)
        {
            CheckBox enableCheckBox = new CheckBox
            {
                IsChecked = enabledInitially,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                LayoutTransform = new ScaleTransform(1.15, 1.15),
                ToolTip = "Enable/disable this optional setting."
            };

            StackPanel container = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            container.Children.Add(CreateDialogFieldHeader(label, toolTip, enableCheckBox));
            container.Children.Add(field);
            Grid.SetRow(container, row);
            grid.Children.Add(container);

            ApplyOptionalFieldState(field, enableCheckBox.IsChecked == true);
            enableCheckBox.Checked += (_, _) => ApplyOptionalFieldState(field, true);
            enableCheckBox.Unchecked += (_, _) => ApplyOptionalFieldState(field, false);
            return enableCheckBox;
        }

        private void AddDialogFieldContainer(Grid grid, int row, string label, string? toolTip, FrameworkElement field)
        {
            StackPanel container = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            container.Children.Add(CreateDialogFieldHeader(label, toolTip));
            container.Children.Add(field);
            Grid.SetRow(container, row);
            grid.Children.Add(container);
        }

        private FrameworkElement CreateDialogFieldHeader(string label, string? toolTip, CheckBox? enableCheckBox = null)
        {
            DockPanel header = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 0, 0, 4)
            };

            Button help = new Button
            {
                Content = "(?)",
                ToolTip = toolTip ?? string.Empty
            };
            if (TryFindResource("HelpBadgeStyle") is Style helpStyle)
            {
                help.Style = helpStyle;
            }
            else
            {
                help.Foreground = Brushes.White;
                help.Background = Brushes.Transparent;
                help.BorderBrush = Brushes.Transparent;
                help.BorderThickness = new Thickness(0);
                help.Padding = new Thickness(0);
                help.Height = 18;
                help.Margin = new Thickness(0, 0, 8, 0);
            }
            header.Children.Add(help);

            if (enableCheckBox != null)
            {
                header.Children.Add(enableCheckBox);
            }

            TextBlock labelText = new TextBlock
            {
                Text = label,
                ToolTip = toolTip ?? string.Empty
            };
            if (TryFindResource("FieldTitleStyle") is Style titleStyle)
            {
                labelText.Style = titleStyle;
            }
            else
            {
                labelText.Foreground = new SolidColorBrush(Color.FromRgb(227, 227, 227));
                labelText.FontSize = 12;
                labelText.FontWeight = FontWeights.SemiBold;
                labelText.VerticalAlignment = VerticalAlignment.Center;
            }
            header.Children.Add(labelText);

            return header;
        }

        private DockPanel BuildDialogButtons(Window dialog)
        {
            DockPanel panel = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(12, 2, 12, 4)
            };

            Button saveButton = CreateDialogButton("Save", isPrimary: true);
            saveButton.Width = 110;
            saveButton.Margin = new Thickness(0, 0, 12, 0);
            saveButton.IsDefault = true;
            saveButton.Click += (_, _) => dialog.DialogResult = true;

            Button cancelButton = CreateDialogButton("Cancel", isPrimary: false);
            cancelButton.Width = 110;
            cancelButton.IsCancel = true;
            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            StackPanel buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttonRow.Children.Add(saveButton);
            buttonRow.Children.Add(cancelButton);
            panel.Children.Add(buttonRow);

            return panel;
        }

        private DockPanel BuildDialogButtonsCentered(Window dialog)
        {
            DockPanel panel = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(12, 2, 12, 4)
            };

            Button saveButton = CreateDialogButton("Save", isPrimary: true);
            saveButton.Width = 110;
            saveButton.Margin = new Thickness(0, 0, 12, 0);
            saveButton.IsDefault = true;
            saveButton.Click += (_, _) => dialog.DialogResult = true;

            Button cancelButton = CreateDialogButton("Cancel", isPrimary: false);
            cancelButton.Width = 110;
            cancelButton.IsCancel = true;
            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            StackPanel buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttonRow.Children.Add(saveButton);
            buttonRow.Children.Add(cancelButton);
            panel.Children.Add(buttonRow);
            return panel;
        }

        private UIElement BuildStyledDialogContent(Window dialog, string title, UIElement body, UIElement buttons)
        {
            Border outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };

            Grid root = new Grid
            {
                Margin = new Thickness(0, 0, -1, -1)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Fill = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/scoienceblur.jpg")),
                    Opacity = 0.5,
                    Stretch = Stretch.UniformToFill
                }
            });
            root.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(127, 0, 0, 0))
            });
            root.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Margin = new Thickness(0, 30, 0, 44),
                Fill = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/Logo_O.png")),
                    Opacity = 0.08,
                    Stretch = Stretch.Uniform
                }
            });

            Border topBar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0))
            };
            topBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
                    {
                        return;
                    }

                    try
                    {
                        dialog.DragMove();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            };
            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            Grid topBarGrid = new Grid();
            topBar.Child = topBarGrid;
            topBarGrid.Children.Add(new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 129,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/Logo_Oceanya.png"))
                }
            });
            topBarGrid.Children.Add(new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 157,
                Height = 23,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(129, 6, 0, 0),
                Fill = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/Logo_Laboratories.png")),
                    Stretch = Stretch.Uniform
                }
            });
            topBarGrid.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(218, 218, 218)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            Button closeButton = new Button
            {
                Width = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                Content = "✕"
            };
            if (TryFindResource("CloseButtonStyle") is Style closeStyle)
            {
                closeButton.Style = closeStyle;
            }
            closeButton.Click += (_, _) => dialog.Close();
            topBarGrid.Children.Add(closeButton);

            Border panel = new Border
            {
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(160, 16, 16, 16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(47, 74, 94)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10)
            };
            Grid.SetRow(panel, 1);
            root.Children.Add(panel);

            Grid panelGrid = new Grid();
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer contentScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body,
                Margin = new Thickness(0, 0, 0, 2)
            };
            Grid.SetRow(contentScroll, 0);
            panelGrid.Children.Add(contentScroll);

            Grid.SetRow(buttons, 1);
            panelGrid.Children.Add(buttons);

            panel.Child = panelGrid;
            outerBorder.Child = root;
            return outerBorder;
        }

        private Button CreateDialogButton(string text, bool isPrimary)
        {
            Button button = new Button
            {
                Content = text,
                Height = 30
            };
            if (TryFindResource("ModernButton") is Style style)
            {
                button.Style = style;
            }
            else
            {
                button.Foreground = Brushes.White;
                button.Background = new SolidColorBrush(Color.FromRgb(38, 55, 72));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(86, 116, 146));
                button.BorderThickness = new Thickness(1);
            }

            return button;
        }

        private void ApplyDialogTextBoxStyle(TextBox textBox)
        {
            if (TryFindResource("ModernTextBox") is Style style)
            {
                textBox.Style = style;
                return;
            }

            textBox.Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));
            textBox.Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238));
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(66, 66, 66));
            textBox.BorderThickness = new Thickness(1);
            textBox.Padding = new Thickness(8, 4, 8, 4);
        }

        private static void ApplyOptionalFieldState(FrameworkElement field, bool enabled)
        {
            field.IsEnabled = enabled;
            field.Opacity = enabled ? 1.0 : 0.35;
        }

        private static void ApplyDialogCheckBoxStyle(CheckBox checkBox)
        {
            checkBox.Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240));
        }

        private static Border CreateButtonPreviewCard(string title, Image previewImage, TextBlock emptyText)
        {
            Border card = new Border
            {
                Width = 220,
                Height = 190,
                Margin = new Thickness(8, 0, 8, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                Padding = new Thickness(8)
            };

            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock titleText = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(196, 208, 222)),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(titleText, 0);
            cardGrid.Children.Add(titleText);
            Grid previewGrid = new Grid();
            previewGrid.Children.Add(previewImage);
            previewGrid.Children.Add(emptyText);
            Grid.SetRow(previewGrid, 1);
            cardGrid.Children.Add(previewGrid);
            card.Child = cardGrid;
            return card;
        }

        private static TextBlock CreatePreviewEmptyText(string text)
        {
            return new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(172, 182, 194))
            };
        }

        private static void AddSimpleField(Panel parent, string label, UIElement content)
        {
            StackPanel container = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(227, 227, 227)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            container.Children.Add(content);
            parent.Children.Add(container);
        }

        private static string GetButtonIconModeName(ButtonIconMode mode)
        {
            return mode switch
            {
                ButtonIconMode.SingleImage => "Single image",
                ButtonIconMode.TwoImages => "Two images",
                _ => "Automatic"
            };
        }

        private static ButtonIconMode ParseButtonIconMode(string text)
        {
            if (string.Equals(text?.Trim(), "Two images", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonIconMode.TwoImages;
            }

            if (string.Equals(text?.Trim(), "Automatic", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonIconMode.Automatic;
            }

            return ButtonIconMode.SingleImage;
        }

        private static string GetButtonEffectsGenerationName(ButtonEffectsGenerationMode mode)
        {
            return mode switch
            {
                ButtonEffectsGenerationMode.ReduceOpacity => "Reduce opacity",
                ButtonEffectsGenerationMode.Darken => "Darken",
                ButtonEffectsGenerationMode.Overlay => "Overlay",
                _ => "Use asset as both versions"
            };
        }

        private static ButtonEffectsGenerationMode ParseButtonEffectsGenerationMode(string text)
        {
            if (string.Equals(text?.Trim(), "Reduce opacity", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonEffectsGenerationMode.ReduceOpacity;
            }

            if (string.Equals(text?.Trim(), "Darken", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonEffectsGenerationMode.Darken;
            }

            if (string.Equals(text?.Trim(), "Overlay", StringComparison.OrdinalIgnoreCase))
            {
                return ButtonEffectsGenerationMode.Overlay;
            }

            return ButtonEffectsGenerationMode.UseAssetAsBothVersions;
        }

        private static IReadOnlyList<string> GetAutomaticBackgroundOptions()
        {
            List<string> options = new List<string>
            {
                "None",
                "Solid Color",
                "Upload"
            };
            options.AddRange(ButtonBackgroundPresetAssetMap.Keys);
            return options;
        }

        private static string GetAutomaticBackgroundSelectionName(ButtonIconGenerationConfig config)
        {
            return config.AutomaticBackgroundMode switch
            {
                ButtonAutomaticBackgroundMode.SolidColor => "Solid Color",
                ButtonAutomaticBackgroundMode.Upload => "Upload",
                ButtonAutomaticBackgroundMode.PresetList => string.IsNullOrWhiteSpace(config.AutomaticBackgroundPreset)
                    ? ButtonBackgroundPresetAssetMap.Keys.First()
                    : config.AutomaticBackgroundPreset,
                _ => "None"
            };
        }

        private static void ApplyAutomaticBackgroundSelection(string selectionText, ButtonIconGenerationConfig config)
        {
            string selected = (selectionText ?? string.Empty).Trim();
            if (string.Equals(selected, "Solid Color", StringComparison.OrdinalIgnoreCase))
            {
                config.AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.SolidColor;
                return;
            }

            if (string.Equals(selected, "Upload", StringComparison.OrdinalIgnoreCase))
            {
                config.AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.Upload;
                return;
            }

            if (string.Equals(selected, "None", StringComparison.OrdinalIgnoreCase))
            {
                config.AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.None;
                return;
            }

            if (ButtonBackgroundPresetAssetMap.ContainsKey(selected))
            {
                config.AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.PresetList;
                config.AutomaticBackgroundPreset = selected;
                return;
            }

            config.AutomaticBackgroundMode = ButtonAutomaticBackgroundMode.None;
        }

        private static string ResolveBackgroundPresetPath(string? presetName)
        {
            if (!string.IsNullOrWhiteSpace(presetName)
                && ButtonBackgroundPresetAssetMap.TryGetValue(presetName, out string? mapped))
            {
                return mapped ?? string.Empty;
            }

            return ButtonBackgroundPresetAssetMap.Values.FirstOrDefault() ?? string.Empty;
        }

        private static string ToHexColor(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static bool TryParseColor(string? text, out Color color)
        {
            color = Colors.Transparent;
            string raw = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (!raw.StartsWith("#", StringComparison.Ordinal))
            {
                raw = "#" + raw;
            }

            if (raw.Length != 7 && raw.Length != 9)
            {
                return false;
            }

            try
            {
                if (ColorConverter.ConvertFromString(raw) is Color parsed)
                {
                    color = parsed;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static Color HsvToColor(double hue, double saturation, double value, byte alpha)
        {
            double clampedHue = ((hue % 360) + 360) % 360;
            double clampedSaturation = Math.Clamp(saturation, 0.0, 1.0);
            double clampedValue = Math.Clamp(value, 0.0, 1.0);

            double c = clampedValue * clampedSaturation;
            double x = c * (1 - Math.Abs(((clampedHue / 60.0) % 2) - 1));
            double m = clampedValue - c;
            (double r, double g, double b) = clampedHue switch
            {
                >= 0 and < 60 => (c, x, 0d),
                >= 60 and < 120 => (x, c, 0d),
                >= 120 and < 180 => (0d, c, x),
                >= 180 and < 240 => (0d, x, c),
                >= 240 and < 300 => (x, 0d, c),
                _ => (c, 0d, x)
            };

            return Color.FromArgb(
                alpha,
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        private static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            if (delta == 0)
            {
                hue = 0;
            }
            else if (Math.Abs(max - r) < double.Epsilon)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < double.Epsilon)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }

            if (hue < 0)
            {
                hue += 360;
            }

            saturation = max == 0 ? 0 : delta / max;
            value = max;
        }

        private static BitmapSource CreateHueWheelBitmap(int size, int ringThickness)
        {
            int wheelSize = Math.Max(32, size);
            int thickness = Math.Clamp(ringThickness, 4, wheelSize / 2);
            double center = (wheelSize - 1) / 2.0;
            double outer = wheelSize / 2.0;
            double inner = outer - thickness;

            int stride = wheelSize * 4;
            byte[] pixels = new byte[wheelSize * stride];
            for (int y = 0; y < wheelSize; y++)
            {
                for (int x = 0; x < wheelSize; x++)
                {
                    double dx = x - center;
                    double dy = center - y;
                    double distance = Math.Sqrt((dx * dx) + (dy * dy));
                    if (distance < inner || distance > outer)
                    {
                        continue;
                    }

                    double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
                    double hue = (angle + 360.0) % 360.0;
                    Color color = HsvToColor(hue, 1.0, 1.0, 255);
                    int offset = (y * stride) + (x * 4);
                    pixels[offset + 0] = color.B;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R;
                    pixels[offset + 3] = 255;
                }
            }

            WriteableBitmap bitmap = new WriteableBitmap(wheelSize, wheelSize, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, wheelSize, wheelSize), pixels, stride, 0);
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapSource? CloneBitmapSource(BitmapSource? source)
        {
            if (source == null)
            {
                return null;
            }

            BitmapSource clone = source.Clone();
            clone.Freeze();
            return clone;
        }

        private static bool TryLoadButtonBitmap(string path, [NotNullWhen(true)] out BitmapSource? bitmap, out bool isAnimated)
        {
            bitmap = null;
            isAnimated = false;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                BitmapDecoder decoder;
                if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                {
                    BitmapImage packImage = new BitmapImage();
                    packImage.BeginInit();
                    packImage.UriSource = new Uri(path, UriKind.Absolute);
                    packImage.CacheOption = BitmapCacheOption.OnLoad;
                    packImage.EndInit();
                    packImage.Freeze();
                    bitmap = packImage;
                    isAnimated = false;
                    return true;
                }

                if (!File.Exists(path))
                {
                    return false;
                }

                using FileStream stream = File.OpenRead(path);
                decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return false;
                }

                isAnimated = decoder.Frames.Count > 1;
                FormatConvertedBitmap converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = decoder.Frames[0];
                converted.DestinationFormat = PixelFormats.Pbgra32;
                converted.EndInit();
                converted.Freeze();
                bitmap = converted;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DrawImageContain(DrawingContext drawingContext, BitmapSource bitmap, Rect bounds, double opacity = 1.0)
        {
            double sourceWidth = Math.Max(1, bitmap.PixelWidth);
            double sourceHeight = Math.Max(1, bitmap.PixelHeight);
            double scale = Math.Min(bounds.Width / sourceWidth, bounds.Height / sourceHeight);
            double drawWidth = sourceWidth * scale;
            double drawHeight = sourceHeight * scale;
            Rect drawRect = new Rect(
                bounds.X + (bounds.Width - drawWidth) * 0.5,
                bounds.Y + (bounds.Height - drawHeight) * 0.5,
                drawWidth,
                drawHeight);

            if (opacity < 1.0)
            {
                drawingContext.PushOpacity(Math.Clamp(opacity, 0.0, 1.0));
                drawingContext.DrawImage(bitmap, drawRect);
                drawingContext.Pop();
                return;
            }

            drawingContext.DrawImage(bitmap, drawRect);
        }

        private static BitmapSource RenderBitmap(int pixelWidth, int pixelHeight, Action<DrawingContext> drawAction)
        {
            int width = Math.Max(1, pixelWidth);
            int height = Math.Max(1, pixelHeight);
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext drawingContext = visual.RenderOpen())
            {
                drawAction(drawingContext);
            }

            RenderTargetBitmap rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            rendered.Freeze();
            return rendered;
        }

        private static BitmapSource ApplyEffectsToOffBitmap(BitmapSource onImage, ButtonIconGenerationConfig config)
        {
            if (config.EffectsMode == ButtonEffectsGenerationMode.UseAssetAsBothVersions)
            {
                return CloneBitmapSource(onImage)!;
            }

            return RenderBitmap(onImage.PixelWidth, onImage.PixelHeight, drawingContext =>
            {
                Rect bounds = new Rect(0, 0, onImage.PixelWidth, onImage.PixelHeight);
                if (config.EffectsMode == ButtonEffectsGenerationMode.ReduceOpacity)
                {
                    double opacity = Math.Clamp(config.OpacityPercent, 0, 100) / 100.0;
                    DrawImageContain(drawingContext, onImage, bounds, opacity);
                }
                else if (config.EffectsMode == ButtonEffectsGenerationMode.Darken)
                {
                    DrawImageContain(drawingContext, onImage, bounds);
                    byte alpha = (byte)Math.Round(Math.Clamp(config.DarknessPercent, 0, 100) * 255 / 100.0);
                    drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), null, bounds);
                }
                else if (config.EffectsMode == ButtonEffectsGenerationMode.Overlay
                    && TryLoadButtonBitmap(config.OverlayImagePath ?? string.Empty, out BitmapSource? overlayImage, out _)
                    && overlayImage != null)
                {
                    DrawImageContain(drawingContext, onImage, bounds);
                    DrawImageContain(drawingContext, overlayImage, bounds);
                }
                else
                {
                    DrawImageContain(drawingContext, onImage, bounds);
                }
            });
        }

        private static bool TryBuildButtonIconPair(
            ButtonIconGenerationConfig config,
            out BitmapSource? onImage,
            out BitmapSource? offImage,
            out string? validationMessage)
        {
            onImage = null;
            offImage = null;
            validationMessage = null;

            if (config.Mode == ButtonIconMode.SingleImage)
            {
                if (!TryLoadButtonBitmap(config.SingleImagePath ?? string.Empty, out BitmapSource? baseImage, out bool isAnimated)
                    || baseImage == null)
                {
                    validationMessage = "Single image mode requires one uploaded base image.";
                    return false;
                }

                if (isAnimated)
                {
                    validationMessage = "Single image mode only accepts non-animated images.";
                    return false;
                }

                if (config.EffectsMode == ButtonEffectsGenerationMode.Overlay
                    && string.IsNullOrWhiteSpace(config.OverlayImagePath))
                {
                    validationMessage = "Overlay effect requires an overlay image.";
                    return false;
                }

                onImage = CloneBitmapSource(baseImage);
                offImage = ApplyEffectsToOffBitmap(baseImage, config);
                return true;
            }

            if (config.Mode == ButtonIconMode.TwoImages)
            {
                if (!TryLoadButtonBitmap(config.TwoImagesOnPath ?? string.Empty, out BitmapSource? twoOn, out _)
                    || twoOn == null)
                {
                    validationMessage = "Two images mode requires button_on.";
                    return false;
                }

                if (!TryLoadButtonBitmap(config.TwoImagesOffPath ?? string.Empty, out BitmapSource? twoOff, out _)
                    || twoOff == null)
                {
                    validationMessage = "Two images mode requires button_off.";
                    return false;
                }

                onImage = CloneBitmapSource(twoOn);
                offImage = CloneBitmapSource(twoOff);
                return true;
            }

            int width = 128;
            int height = 128;
            if (config.AutomaticCutEmoteImage != null)
            {
                width = Math.Max(width, config.AutomaticCutEmoteImage.PixelWidth);
                height = Math.Max(height, config.AutomaticCutEmoteImage.PixelHeight);
            }
            else if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.PresetList
                && TryLoadButtonBitmap(ResolveBackgroundPresetPath(config.AutomaticBackgroundPreset), out BitmapSource? presetImage, out _)
                && presetImage != null)
            {
                width = Math.Max(width, presetImage.PixelWidth);
                height = Math.Max(height, presetImage.PixelHeight);
            }
            else if (TryLoadButtonBitmap(config.AutomaticBackgroundUploadPath ?? string.Empty, out BitmapSource? bgImage, out _)
                && bgImage != null)
            {
                width = Math.Max(width, bgImage.PixelWidth);
                height = Math.Max(height, bgImage.PixelHeight);
            }

            BitmapSource autoOn = RenderBitmap(width, height, drawingContext =>
            {
                Rect bounds = new Rect(0, 0, width, height);
                if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.PresetList)
                {
                    string presetPath = ResolveBackgroundPresetPath(config.AutomaticBackgroundPreset);
                    if (TryLoadButtonBitmap(presetPath, out BitmapSource? presetBackground, out _) && presetBackground != null)
                    {
                        DrawImageContain(drawingContext, presetBackground, bounds);
                    }
                }
                else if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.SolidColor)
                {
                    drawingContext.DrawRectangle(new SolidColorBrush(config.AutomaticSolidColor), null, bounds);
                }
                else if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.Upload
                    && TryLoadButtonBitmap(config.AutomaticBackgroundUploadPath ?? string.Empty, out BitmapSource? uploadedBackground, out _)
                    && uploadedBackground != null)
                {
                    DrawImageContain(drawingContext, uploadedBackground, bounds);
                }

                if (config.AutomaticCutEmoteImage != null)
                {
                    DrawImageContain(drawingContext, config.AutomaticCutEmoteImage, bounds);
                }
            });

            if (config.EffectsMode == ButtonEffectsGenerationMode.Overlay
                && string.IsNullOrWhiteSpace(config.OverlayImagePath))
            {
                validationMessage = "Overlay effect requires an overlay image.";
                return false;
            }

            onImage = autoOn;
            offImage = ApplyEffectsToOffBitmap(autoOn, config);
            return true;
        }

        private static ButtonIconGenerationConfig BuildButtonIconGenerationConfig(CharacterCreationEmoteViewModel emote)
        {
            return new ButtonIconGenerationConfig
            {
                Mode = emote.ButtonIconMode,
                EffectsMode = emote.ButtonEffectsGenerationMode,
                OpacityPercent = Math.Clamp(emote.ButtonEffectsOpacityPercent, 0, 100),
                DarknessPercent = Math.Clamp(emote.ButtonEffectsDarknessPercent, 0, 100),
                SingleImagePath = emote.ButtonSingleImageAssetSourcePath,
                TwoImagesOnPath = emote.ButtonTwoImagesOnAssetSourcePath,
                TwoImagesOffPath = emote.ButtonTwoImagesOffAssetSourcePath,
                OverlayImagePath = emote.ButtonEffectsOverlayAssetSourcePath,
                AutomaticBackgroundMode = emote.ButtonAutomaticBackgroundMode,
                AutomaticBackgroundPreset = emote.ButtonAutomaticBackgroundPreset,
                AutomaticSolidColor = emote.ButtonAutomaticSolidColor,
                AutomaticBackgroundUploadPath = emote.ButtonAutomaticBackgroundUploadAssetSourcePath,
                AutomaticCutEmoteImage = CloneBitmapSource(emote.ButtonAutomaticCutEmoteImage)
            };
        }

        private static void ApplyButtonIconGenerationConfig(CharacterCreationEmoteViewModel emote, ButtonIconGenerationConfig config)
        {
            emote.ButtonIconMode = config.Mode;
            emote.ButtonEffectsGenerationMode = config.EffectsMode;
            emote.ButtonEffectsOpacityPercent = Math.Clamp(config.OpacityPercent, 0, 100);
            emote.ButtonEffectsDarknessPercent = Math.Clamp(config.DarknessPercent, 0, 100);
            emote.ButtonSingleImageAssetSourcePath = config.SingleImagePath;
            emote.ButtonTwoImagesOnAssetSourcePath = config.TwoImagesOnPath;
            emote.ButtonTwoImagesOffAssetSourcePath = config.TwoImagesOffPath;
            emote.ButtonEffectsOverlayAssetSourcePath = config.OverlayImagePath;
            emote.ButtonAutomaticBackgroundMode = config.AutomaticBackgroundMode;
            emote.ButtonAutomaticBackgroundPreset = config.AutomaticBackgroundPreset;
            emote.ButtonAutomaticSolidColor = config.AutomaticSolidColor;
            emote.ButtonAutomaticBackgroundUploadAssetSourcePath = config.AutomaticBackgroundUploadPath;
            emote.ButtonAutomaticCutEmoteImage = CloneBitmapSource(config.AutomaticCutEmoteImage);
        }

        private static string? ResolveRepresentativeButtonSourcePath(ButtonIconGenerationConfig config)
        {
            if (config.Mode == ButtonIconMode.SingleImage)
            {
                return config.SingleImagePath;
            }

            if (config.Mode == ButtonIconMode.TwoImages)
            {
                return !string.IsNullOrWhiteSpace(config.TwoImagesOnPath)
                    ? config.TwoImagesOnPath
                    : config.TwoImagesOffPath;
            }

            if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.Upload
                && !string.IsNullOrWhiteSpace(config.AutomaticBackgroundUploadPath))
            {
                return config.AutomaticBackgroundUploadPath;
            }

            return null;
        }

        private static void SaveBitmapAsPng(BitmapSource bitmap, string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream stream = File.Create(path);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T typed)
                {
                    return typed;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static string ResolveContextZoneFromSource(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element
                    && element.Tag is string zone
                    && IsKnownContextZone(zone))
                {
                    return zone;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return string.Empty;
        }

        private static bool IsKnownContextZone(string zone)
        {
            return string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase)
                || string.Equals(zone, "anim", StringComparison.OrdinalIgnoreCase)
                || string.Equals(zone, "button", StringComparison.OrdinalIgnoreCase)
                || string.Equals(zone, "sfx", StringComparison.OrdinalIgnoreCase);
        }

        private static T? FindDescendantByName<T>(DependencyObject? source, string name) where T : FrameworkElement
        {
            if (source == null)
            {
                return null;
            }

            int children = VisualTreeHelper.GetChildrenCount(source);
            for (int i = 0; i < children; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(source, i);
                if (child is T frameworkElement
                    && string.Equals(frameworkElement.Name, name, StringComparison.Ordinal))
                {
                    return frameworkElement;
                }

                T? descendant = FindDescendantByName<T>(child, name);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
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

        private void CharacterIconFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select character icon image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            selectedCharacterIconSourcePath = dialog.FileName;
            CharacterIconPreviewImage.Source = TryLoadPreviewImage(dialog.FileName);
            CharacterIconEmptyText.Visibility = CharacterIconPreviewImage.Source == null
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatusTextBlock.Text = "Character icon selected.";
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

            imageControl.Source = Ao2AnimationPreview.LoadStaticPreviewImage(path, decodePixelWidth: 0);
            noImageText.Visibility = Visibility.Collapsed;
        }

        private static bool TryLoadFirstFrame(string path, out ImageSource? initialFrame, out double estimatedDurationMs)
            => Ao2AnimationPreview.TryLoadFirstFrame(path, out initialFrame, out estimatedDurationMs);

        private sealed class AnimationTimelinePreviewController : IDisposable
        {
            private readonly List<ImageSource> frames;
            private readonly List<double> frameStartMs;
            private readonly List<double> frameDurationMs;
            private readonly DispatcherTimer timer;
            private bool disposed;
            private bool loop = true;
            private bool isPlaying;
            private double currentPositionMs;
            private double? cutDurationMs;
            private DateTime playStartedUtc;

            public event Action<ImageSource, double>? PositionChanged;
            public event Action<bool>? PlaybackStateChanged;

            public ImageSource CurrentFrame => frames[Math.Clamp(GetFrameIndexFromPosition(currentPositionMs), 0, frames.Count - 1)];
            public bool IsPlaying => isPlaying;
            public bool HasTimeline => frames.Count > 1;
            public double CurrentPositionMs => currentPositionMs;
            public double EffectiveDurationMs => cutDurationMs.HasValue
                ? Math.Min(cutDurationMs.Value, TotalDurationMs)
                : TotalDurationMs;

            private double TotalDurationMs => frameDurationMs.Sum();

            private AnimationTimelinePreviewController(List<ImageSource> frames, List<double> frameDurationMs)
            {
                this.frames = frames;
                this.frameDurationMs = frameDurationMs;
                frameStartMs = new List<double>(frameDurationMs.Count);
                double cumulative = 0;
                foreach (double frameLength in frameDurationMs)
                {
                    frameStartMs.Add(cumulative);
                    cumulative += frameLength;
                }

                timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(15)
                };
                timer.Tick += Timer_Tick;
                RaisePositionChanged();
            }

            public static bool TryCreate(string? path, [NotNullWhen(true)] out AnimationTimelinePreviewController? controller)
            {
                controller = null;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return false;
                }

                try
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
                        && TryCreateGifTimeline(path, out AnimationTimelinePreviewController? gifController))
                    {
                        controller = gifController;
                        return true;
                    }

                    BitmapDecoder decoder = BitmapDecoder.Create(
                        new Uri(path, UriKind.Absolute),
                        BitmapCreateOptions.None,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                    {
                        return false;
                    }

                    List<ImageSource> decodedFrames = new List<ImageSource>(decoder.Frames.Count);
                    List<double> decodedDurations = new List<double>(decoder.Frames.Count);
                    foreach (BitmapFrame frame in decoder.Frames)
                    {
                        BitmapSource source = frame;
                        if (source.CanFreeze)
                        {
                            source.Freeze();
                        }

                        decodedFrames.Add(source);
                        decodedDurations.Add(ReadDelay(frame.Metadata));
                    }

                    controller = new AnimationTimelinePreviewController(decodedFrames, decodedDurations);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryCreateGifTimeline(string gifPath, [NotNullWhen(true)] out AnimationTimelinePreviewController? controller)
            {
                controller = null;
                try
                {
                    using System.Drawing.Image gifImage = System.Drawing.Image.FromFile(gifPath);
                    int frameCount = gifImage.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    if (frameCount <= 0)
                    {
                        return false;
                    }

                    List<double> durations = ReadGifFrameDelays(gifImage, frameCount);
                    List<ImageSource> decodedFrames = new List<ImageSource>(frameCount);
                    for (int i = 0; i < frameCount; i++)
                    {
                        gifImage.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, i);
                        using System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(
                            gifImage.Width,
                            gifImage.Height,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(System.Drawing.Color.Transparent);
                            graphics.DrawImage(gifImage, 0, 0, gifImage.Width, gifImage.Height);
                        }

                        IntPtr hBitmap = bitmap.GetHbitmap();
                        try
                        {
                            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            if (source.CanFreeze)
                            {
                                source.Freeze();
                            }

                            decodedFrames.Add(source);
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }

                    controller = new AnimationTimelinePreviewController(decodedFrames, durations);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static List<double> ReadGifFrameDelays(System.Drawing.Image image, int frameCount)
            {
                List<double> delays = new List<double>(frameCount);
                const int PropertyTagFrameDelay = 0x5100;
                try
                {
                    System.Drawing.Imaging.PropertyItem? property = image.PropertyItems.FirstOrDefault(item => item.Id == PropertyTagFrameDelay);
                    if (property?.Value != null && property.Value.Length >= frameCount * 4)
                    {
                        for (int i = 0; i < frameCount; i++)
                        {
                            int delayUnits = BitConverter.ToInt32(property.Value, i * 4);
                            double milliseconds = Math.Max(20, delayUnits * 10d);
                            delays.Add(milliseconds);
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                while (delays.Count < frameCount)
                {
                    delays.Add(90);
                }

                return delays;
            }

            public void Play()
            {
                if (disposed || !HasTimeline)
                {
                    return;
                }

                if (currentPositionMs >= EffectiveDurationMs)
                {
                    currentPositionMs = 0;
                    RaisePositionChanged();
                }

                playStartedUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Math.Max(0, currentPositionMs));
                isPlaying = true;
                timer.Start();
                PlaybackStateChanged?.Invoke(true);
            }

            public void Pause()
            {
                if (disposed || !isPlaying)
                {
                    return;
                }

                isPlaying = false;
                timer.Stop();
                PlaybackStateChanged?.Invoke(false);
            }

            public void Seek(double positionMs)
            {
                if (disposed)
                {
                    return;
                }

                currentPositionMs = Math.Clamp(positionMs, 0, Math.Max(0, EffectiveDurationMs));
                if (isPlaying)
                {
                    playStartedUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(currentPositionMs);
                }

                RaisePositionChanged();
            }

            public void StepFrame(int frameDelta)
            {
                if (disposed || !HasTimeline)
                {
                    return;
                }

                int index = Math.Clamp(GetFrameIndexFromPosition(currentPositionMs) + frameDelta, 0, frames.Count - 1);
                Seek(frameStartMs[Math.Clamp(index, 0, frameStartMs.Count - 1)]);
            }

            public void SetLoop(bool shouldLoop)
            {
                loop = shouldLoop;
            }

            public void SetCutoffDurationMs(int? durationMs)
            {
                cutDurationMs = durationMs.HasValue && durationMs.Value > 0
                    ? durationMs.Value
                    : null;
                if (currentPositionMs > EffectiveDurationMs)
                {
                    currentPositionMs = EffectiveDurationMs;
                }

                RaisePositionChanged();
            }

            private void Timer_Tick(object? sender, EventArgs e)
            {
                if (disposed || !isPlaying)
                {
                    return;
                }

                double elapsedMs = (DateTime.UtcNow - playStartedUtc).TotalMilliseconds;
                double durationMs = Math.Max(1, EffectiveDurationMs);
                if (loop && durationMs > 0)
                {
                    currentPositionMs = elapsedMs % durationMs;
                    RaisePositionChanged();
                    return;
                }

                currentPositionMs = Math.Clamp(elapsedMs, 0, durationMs);
                RaisePositionChanged();
                if (elapsedMs >= durationMs)
                {
                    Pause();
                }
            }

            private void RaisePositionChanged()
            {
                PositionChanged?.Invoke(CurrentFrame, currentPositionMs);
            }

            private int GetFrameIndexFromPosition(double positionMs)
            {
                double clamped = Math.Clamp(positionMs, 0, Math.Max(0, EffectiveDurationMs));
                for (int i = frameStartMs.Count - 1; i >= 0; i--)
                {
                    if (clamped >= frameStartMs[i])
                    {
                        return i;
                    }
                }

                return 0;
            }

            private static double ReadDelay(ImageMetadata? metadata)
            {
                try
                {
                    if (metadata is BitmapMetadata bitmapMetadata && bitmapMetadata.ContainsQuery("/grctlext/Delay"))
                    {
                        object query = bitmapMetadata.GetQuery("/grctlext/Delay");
                        if (query is ushort delayValue)
                        {
                            return Math.Max(20, delayValue * 10d);
                        }
                    }
                }
                catch
                {
                    // ignored
                }

                return 90;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                timer.Stop();
                timer.Tick -= Timer_Tick;
                PositionChanged = null;
                PlaybackStateChanged = null;
            }

            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr hObject);
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

            if (Ao2AnimationPreview.TryCreateAnimationPlayer(visualPath, loop: false, out IAnimationPlayer? previewPlayer)
                && previewPlayer != null)
            {
                durationMs = Math.Max(durationMs, 500);
                previewPlayer.FrameChanged += frame => Dispatcher.Invoke(() =>
                {
                    imageControl.Source = frame;
                    noImageText.Visibility = Visibility.Collapsed;
                });
                shoutVisualPlayers[key] = previewPlayer;
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

        private void CopyEmoteAssets(string characterDirectory)
        {
            string imagesDirectory = Path.Combine(characterDirectory, "Images");
            string soundsDirectory = Path.Combine(characterDirectory, "Sounds");
            string emotionsDirectory = Path.Combine(characterDirectory, "emotions");
            Directory.CreateDirectory(imagesDirectory);
            Directory.CreateDirectory(soundsDirectory);
            Directory.CreateDirectory(emotionsDirectory);

            for (int i = 0; i < emotes.Count; i++)
            {
                CharacterCreationEmoteViewModel emote = emotes[i];
                int emoteId = i + 1;

                string preanimTokenForCopy = !string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath)
                    ? Path.GetFileNameWithoutExtension(emote.PreAnimationAssetSourcePath)
                    : emote.PreAnimation;
                string animationTokenForCopy = !string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath)
                    ? Path.GetFileNameWithoutExtension(emote.AnimationAssetSourcePath)
                    : emote.Animation;
                CopyEmoteImageAsset(emote.PreAnimationAssetSourcePath, imagesDirectory, preanimTokenForCopy);
                CopyEmoteImageAsset(emote.AnimationAssetSourcePath, imagesDirectory, animationTokenForCopy);

                string splitBaseName = !string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath)
                    ? Path.GetFileNameWithoutExtension(emote.FinalAnimationIdleAssetSourcePath)
                    : (!string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath)
                        ? Path.GetFileNameWithoutExtension(emote.FinalAnimationTalkingAssetSourcePath)
                        : string.Empty);
                if (!string.IsNullOrWhiteSpace(splitBaseName))
                {
                    if (!string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath))
                    {
                        CopyEmoteImageAsset(emote.FinalAnimationIdleAssetSourcePath, imagesDirectory, "(a)/" + splitBaseName);
                    }

                    if (!string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath))
                    {
                        CopyEmoteImageAsset(emote.FinalAnimationTalkingAssetSourcePath, imagesDirectory, "(b)/" + splitBaseName);
                    }

                    if (!string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath))
                    {
                        CopyEmoteImageAsset(emote.AnimationAssetSourcePath, imagesDirectory, splitBaseName);
                    }
                }

                ButtonIconGenerationConfig buttonConfig = BuildButtonIconGenerationConfig(emote);
                if (TryBuildButtonIconPair(buttonConfig, out BitmapSource? onImage, out BitmapSource? offImage, out _)
                    && onImage != null
                    && offImage != null)
                {
                    string onPath = Path.Combine(emotionsDirectory, $"button{emoteId}_on.png");
                    string offPath = Path.Combine(emotionsDirectory, $"button{emoteId}_off.png");
                    SaveBitmapAsPng(onImage, onPath);
                    SaveBitmapAsPng(offImage, offPath);
                }

                if (!string.IsNullOrWhiteSpace(emote.SfxAssetSourcePath) && File.Exists(emote.SfxAssetSourcePath))
                {
                    string extension = Path.GetExtension(emote.SfxAssetSourcePath);
                    string baseName = Path.GetFileNameWithoutExtension(emote.SfxAssetSourcePath);
                    string destinationPath = Path.Combine(soundsDirectory, baseName + extension);
                    File.Copy(emote.SfxAssetSourcePath, destinationPath, overwrite: true);
                }
            }
        }

        private static void CopyEmoteImageAsset(string? sourcePath, string imagesDirectory, string token)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)
                || !File.Exists(sourcePath)
                || string.IsNullOrWhiteSpace(token)
                || string.Equals(token, "-", StringComparison.Ordinal))
            {
                return;
            }

            string extension = Path.GetExtension(sourcePath);
            string normalizedToken = token.Replace('\\', '/');
            string relativePath = normalizedToken;
            if (relativePath.StartsWith("Images/", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Substring("Images/".Length);
            }

            string destinationPath = Path.Combine(imagesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar) + extension);
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private void CopyCharacterIconAsset(string characterDirectory)
        {
            if (string.IsNullOrWhiteSpace(selectedCharacterIconSourcePath) || !File.Exists(selectedCharacterIconSourcePath))
            {
                return;
            }

            string extension = Path.GetExtension(selectedCharacterIconSourcePath);
            string destinationPath = Path.Combine(characterDirectory, "char_icon" + extension);
            File.Copy(selectedCharacterIconSourcePath, destinationPath, overwrite: true);
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

        private static async Task SetGenerateSubtitleAsync(string subtitle)
        {
            WaitForm.SetSubtitle(subtitle);
            await Task.Delay(20);
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
            StopAllEmotePreviewPlayers();
            blipPreviewPlayer.Dispose();
            shoutSfxPreviewPlayer.Dispose();
            realizationSfxPreviewPlayer.Dispose();
            foreach (string key in shoutVisualPlayers.Keys.ToList())
            {
                StopShoutVisualPlayer(key);
            }
            SaveFile.Data.CharacterCreatorWindowState = CaptureWindowState();
            SaveFile.Data.CharacterCreatorEmoteTileWidth = EmoteTileWidth;
            SaveFile.Data.CharacterCreatorEmoteTileHeight = EmoteTileHeight;
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
            string? successCharacterDirectory = null;
            Exception? generationException = null;
            try
            {
                await SetGenerateSubtitleAsync("Validating setup and emote data...");
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

                string mountPath = (MountPathComboBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(mountPath) && mountPathOptions.Count == 1)
                {
                    mountPath = mountPathOptions[0];
                }

                if (string.IsNullOrWhiteSpace(mountPath))
                {
                    OceanyaMessageBox.Show(this, "No valid mount path is available.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string sanitizedFolderToken = SanitizeFolderNameForRelativePath(folderName);
                List<CharacterCreationEmote> generatedEmotes = emotes.Select(viewModel =>
                {
                    CharacterCreationEmote model = viewModel.ToModel();
                    if (!string.IsNullOrWhiteSpace(viewModel.PreAnimationAssetSourcePath)
                        && File.Exists(viewModel.PreAnimationAssetSourcePath)
                        && !string.IsNullOrWhiteSpace(model.PreAnimation)
                        && !string.Equals(model.PreAnimation, "-", StringComparison.Ordinal))
                    {
                        string preanimBaseName = Path.GetFileNameWithoutExtension(viewModel.PreAnimationAssetSourcePath);
                        model.PreAnimation = "Images/" + preanimBaseName;
                    }

                    if (!string.IsNullOrWhiteSpace(viewModel.AnimationAssetSourcePath)
                        && File.Exists(viewModel.AnimationAssetSourcePath)
                        && !string.IsNullOrWhiteSpace(model.Animation))
                    {
                        string animBaseName = Path.GetFileNameWithoutExtension(viewModel.AnimationAssetSourcePath);
                        if (!string.IsNullOrWhiteSpace(viewModel.FinalAnimationIdleAssetSourcePath)
                            && File.Exists(viewModel.FinalAnimationIdleAssetSourcePath))
                        {
                            animBaseName = Path.GetFileNameWithoutExtension(viewModel.FinalAnimationIdleAssetSourcePath);
                        }
                        else if (!string.IsNullOrWhiteSpace(viewModel.FinalAnimationTalkingAssetSourcePath)
                            && File.Exists(viewModel.FinalAnimationTalkingAssetSourcePath))
                        {
                            animBaseName = Path.GetFileNameWithoutExtension(viewModel.FinalAnimationTalkingAssetSourcePath);
                        }

                        model.Animation = "Images/" + animBaseName;
                    }
                    else if (!string.IsNullOrWhiteSpace(viewModel.FinalAnimationIdleAssetSourcePath)
                        && File.Exists(viewModel.FinalAnimationIdleAssetSourcePath))
                    {
                        model.Animation = "Images/" + Path.GetFileNameWithoutExtension(viewModel.FinalAnimationIdleAssetSourcePath);
                    }
                    else if (!string.IsNullOrWhiteSpace(viewModel.FinalAnimationTalkingAssetSourcePath)
                        && File.Exists(viewModel.FinalAnimationTalkingAssetSourcePath))
                    {
                        model.Animation = "Images/" + Path.GetFileNameWithoutExtension(viewModel.FinalAnimationTalkingAssetSourcePath);
                    }

                    if (!string.IsNullOrWhiteSpace(viewModel.SfxAssetSourcePath) && File.Exists(viewModel.SfxAssetSourcePath))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(viewModel.SfxAssetSourcePath);
                        model.SfxName = $"../../characters/{sanitizedFolderToken}/Sounds/{baseName}";
                    }

                    bool hasPreanim = !string.IsNullOrWhiteSpace(model.PreAnimation)
                        && !string.Equals(model.PreAnimation, "-", StringComparison.Ordinal);
                    bool hasSfx = !string.IsNullOrWhiteSpace(model.SfxName)
                        && !string.Equals(model.SfxName, "1", StringComparison.Ordinal);
                    if (hasPreanim || hasSfx)
                    {
                        if (model.EmoteModifier == EmoteModIdle)
                        {
                            model.EmoteModifier = EmoteModPreanim;
                        }
                        else if (model.EmoteModifier == EmoteModZoom)
                        {
                            model.EmoteModifier = EmoteModPreanimZoom;
                        }
                        else if (hasPreanim && model.EmoteModifier != EmoteModPreanim && model.EmoteModifier != EmoteModPreanimZoom)
                        {
                            model.EmoteModifier = EmoteModPreanim;
                        }
                    }

                    return model;
                }).ToList();

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
                    Emotes = generatedEmotes
                };

                bool usingCustomBlipFile = !string.IsNullOrWhiteSpace(selectedCustomBlipSourcePath)
                    && !string.IsNullOrWhiteSpace(customBlipOptionText)
                    && string.Equals((GenderBlipsDropdown.Text ?? string.Empty).Trim(), customBlipOptionText, StringComparison.OrdinalIgnoreCase);
                if (usingCustomBlipFile)
                {
                    string blipBaseName = Path.GetFileNameWithoutExtension(selectedCustomBlipSourcePath);
                    project.Blips = $"../../characters/{sanitizedFolderToken}/blips/{blipBaseName}";
                    project.Gender = project.Blips;
                }

                await SetGenerateSubtitleAsync("Creating character folder and writing char.ini...");
                string characterDirectory = AOCharacterFileCreatorBuilder.CreateCharacterFolder(project);

                await SetGenerateSubtitleAsync("Copying emote assets (Images/Sounds/emotions)...");
                CopyEmoteAssets(characterDirectory);
                await SetGenerateSubtitleAsync("Copying character icon...");
                CopyCharacterIconAsset(characterDirectory);
                await SetGenerateSubtitleAsync("Copying shout assets (root special case)...");
                CopyShoutAssets(characterDirectory);

                if (usingCustomBlipFile)
                {
                    await SetGenerateSubtitleAsync("Copying custom blip file...");
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
                    await SetGenerateSubtitleAsync("Updating custom shout settings...");
                    AddOrUpdateShoutsCustomName(Path.Combine(characterDirectory, "char.ini"), customShoutName);
                }

                if (!string.IsNullOrWhiteSpace(selectedRealizationSourcePath)
                    && string.Equals(
                        (RealizationTextBox.Text ?? string.Empty).Trim(),
                        Path.GetFileName(selectedRealizationSourcePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    await SetGenerateSubtitleAsync("Copying realization sound (root special case)...");
                    string extension = Path.GetExtension(selectedRealizationSourcePath);
                    string destinationPath = Path.Combine(characterDirectory, "realization" + extension);
                    File.Copy(selectedRealizationSourcePath, destinationPath, overwrite: true);

                    string sanitizedFolder = SanitizeFolderNameForRelativePath(folderName);
                    string realizationToken = $"../../characters/{sanitizedFolder}/realization";
                    AddOrUpdateOptionsValue(Path.Combine(characterDirectory, "char.ini"), "realization", realizationToken);
                }

                await SetGenerateSubtitleAsync("Refreshing character index...");
                try
                {
                    CharacterFolder.RefreshCharacterList();
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning("Character folder creation succeeded, but character refresh failed.", ex);
                }

                StatusTextBlock.Text = "Created character folder: " + characterDirectory;
                successCharacterDirectory = characterDirectory;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to create AO character folder.", ex);
                generationException = ex;
            }
            finally
            {
                WaitForm.CloseForm();
            }

            if (generationException != null)
            {
                OceanyaMessageBox.Show(this, "Failed to create character folder:\n" + generationException.Message, "Creation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrWhiteSpace(successCharacterDirectory))
            {
                OceanyaMessageBox.Show(
                    this,
                    "Character folder created successfully:\n" + successCharacterDirectory,
                    "AO Character File Creator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
        private string preAnimation = "-";
        private string animation = "normal";
        private int emoteModifier;
        private int deskModifier = 1;
        private string sfxName = "1";
        private int sfxDelayMs = 1;
        private bool sfxLooping;
        private int? preAnimationDurationMs;
        private int? stayTimeMs;
        private string blipsOverride = string.Empty;
        private bool isSelected;
        private bool isDragTarget;
        private bool isPreDropActive;
        private bool isAnimationDropActive;
        private bool isButtonDropActive;
        private bool isRenaming;
        private string renameDraft = string.Empty;
        private string buttonIconToken = string.Empty;
        private string? preAnimationAssetSourcePath;
        private string? animationAssetSourcePath;
        private string? finalAnimationIdleAssetSourcePath;
        private string? finalAnimationTalkingAssetSourcePath;
        private string? buttonIconAssetSourcePath;
        private ButtonIconMode buttonIconMode = ButtonIconMode.SingleImage;
        private ButtonEffectsGenerationMode buttonEffectsGenerationMode = ButtonEffectsGenerationMode.Darken;
        private int buttonEffectsOpacityPercent = 75;
        private int buttonEffectsDarknessPercent = 50;
        private string? buttonSingleImageAssetSourcePath;
        private string? buttonTwoImagesOnAssetSourcePath;
        private string? buttonTwoImagesOffAssetSourcePath;
        private string? buttonEffectsOverlayAssetSourcePath;
        private ButtonAutomaticBackgroundMode buttonAutomaticBackgroundMode = ButtonAutomaticBackgroundMode.None;
        private string buttonAutomaticBackgroundPreset = "Oceanya Logo (preset)";
        private Color buttonAutomaticSolidColor = Colors.Transparent;
        private string? buttonAutomaticBackgroundUploadAssetSourcePath;
        private BitmapSource? buttonAutomaticCutEmoteImage;
        private string? sfxAssetSourcePath;
        private ImageSource? preAnimationPreview;
        private ImageSource? animationPreview;
        private ImageSource? buttonIconPreview;
        private bool isResizeGuideVisible;

        public Guid Id { get; } = Guid.NewGuid();
        public ObservableCollection<FrameEventViewModel> FrameEvents { get; } = new ObservableCollection<FrameEventViewModel>();
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index
        {
            get => index;
            set
            {
                if (SetField(ref index, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(EmoteHeader));
                }
            }
        }

        public string Name
        {
            get => name;
            set
            {
                if (SetField(ref name, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string PreAnimation
        {
            get => preAnimation;
            set
            {
                if (SetField(ref preAnimation, value))
                {
                    OnPropertyChanged(nameof(HasPreAnimationValue));
                    OnPropertyChanged(nameof(PreAnimationDisplayText));
                }
            }
        }

        public string Animation
        {
            get => animation;
            set
            {
                if (SetField(ref animation, value))
                {
                    OnPropertyChanged(nameof(HasAnimationValue));
                    OnPropertyChanged(nameof(ShowPreAnimationField));
                    OnPropertyChanged(nameof(AnimationDisplayText));
                }
            }
        }

        public int EmoteModifier
        {
            get => emoteModifier;
            set => SetField(ref emoteModifier, value);
        }

        public int DeskModifier
        {
            get => deskModifier;
            set => SetField(ref deskModifier, value);
        }

        public string SfxName
        {
            get => sfxName;
            set
            {
                if (SetField(ref sfxName, value))
                {
                    OnPropertyChanged(nameof(SfxSummaryText));
                }
            }
        }

        public int SfxDelayMs
        {
            get => sfxDelayMs;
            set
            {
                if (SetField(ref sfxDelayMs, value))
                {
                    OnPropertyChanged(nameof(SfxSummaryText));
                }
            }
        }

        public bool SfxLooping
        {
            get => sfxLooping;
            set
            {
                if (SetField(ref sfxLooping, value))
                {
                    OnPropertyChanged(nameof(SfxSummaryText));
                }
            }
        }

        public int? PreAnimationDurationMs
        {
            get => preAnimationDurationMs;
            set => SetField(ref preAnimationDurationMs, value);
        }

        public int? StayTimeMs
        {
            get => stayTimeMs;
            set => SetField(ref stayTimeMs, value);
        }

        public string BlipsOverride
        {
            get => blipsOverride;
            set => SetField(ref blipsOverride, value);
        }

        public bool IsSelected
        {
            get => isSelected;
            set => SetField(ref isSelected, value);
        }

        public bool IsDragTarget
        {
            get => isDragTarget;
            set => SetField(ref isDragTarget, value);
        }

        public bool IsPreDropActive
        {
            get => isPreDropActive;
            set => SetField(ref isPreDropActive, value);
        }

        public bool IsAnimationDropActive
        {
            get => isAnimationDropActive;
            set => SetField(ref isAnimationDropActive, value);
        }

        public bool IsButtonDropActive
        {
            get => isButtonDropActive;
            set => SetField(ref isButtonDropActive, value);
        }

        public bool IsRenaming
        {
            get => isRenaming;
            set => SetField(ref isRenaming, value);
        }

        public string RenameDraft
        {
            get => renameDraft;
            set => SetField(ref renameDraft, value);
        }

        public string ButtonIconToken
        {
            get => buttonIconToken;
            set
            {
                if (SetField(ref buttonIconToken, value))
                {
                    OnPropertyChanged(nameof(HasButtonIconValue));
                }
            }
        }

        public string? PreAnimationAssetSourcePath
        {
            get => preAnimationAssetSourcePath;
            set
            {
                if (SetField(ref preAnimationAssetSourcePath, value))
                {
                    OnPropertyChanged(nameof(HasPreAnimationValue));
                    OnPropertyChanged(nameof(PreAnimationDisplayText));
                }
            }
        }

        public string? AnimationAssetSourcePath
        {
            get => animationAssetSourcePath;
            set
            {
                if (SetField(ref animationAssetSourcePath, value))
                {
                    OnPropertyChanged(nameof(HasAnimationValue));
                    OnPropertyChanged(nameof(ShowPreAnimationField));
                    OnPropertyChanged(nameof(AnimationDisplayText));
                }
            }
        }

        public string? FinalAnimationIdleAssetSourcePath
        {
            get => finalAnimationIdleAssetSourcePath;
            set => SetField(ref finalAnimationIdleAssetSourcePath, value);
        }

        public string? FinalAnimationTalkingAssetSourcePath
        {
            get => finalAnimationTalkingAssetSourcePath;
            set => SetField(ref finalAnimationTalkingAssetSourcePath, value);
        }

        public string? ButtonIconAssetSourcePath
        {
            get => buttonIconAssetSourcePath;
            set
            {
                if (SetField(ref buttonIconAssetSourcePath, value))
                {
                    OnPropertyChanged(nameof(HasButtonIconValue));
                }
            }
        }

        public ButtonIconMode ButtonIconMode
        {
            get => buttonIconMode;
            set => SetField(ref buttonIconMode, value);
        }

        public ButtonEffectsGenerationMode ButtonEffectsGenerationMode
        {
            get => buttonEffectsGenerationMode;
            set => SetField(ref buttonEffectsGenerationMode, value);
        }

        public int ButtonEffectsOpacityPercent
        {
            get => buttonEffectsOpacityPercent;
            set => SetField(ref buttonEffectsOpacityPercent, Math.Clamp(value, 0, 100));
        }

        public int ButtonEffectsDarknessPercent
        {
            get => buttonEffectsDarknessPercent;
            set => SetField(ref buttonEffectsDarknessPercent, Math.Clamp(value, 0, 100));
        }

        public string? ButtonSingleImageAssetSourcePath
        {
            get => buttonSingleImageAssetSourcePath;
            set => SetField(ref buttonSingleImageAssetSourcePath, value);
        }

        public string? ButtonTwoImagesOnAssetSourcePath
        {
            get => buttonTwoImagesOnAssetSourcePath;
            set => SetField(ref buttonTwoImagesOnAssetSourcePath, value);
        }

        public string? ButtonTwoImagesOffAssetSourcePath
        {
            get => buttonTwoImagesOffAssetSourcePath;
            set => SetField(ref buttonTwoImagesOffAssetSourcePath, value);
        }

        public string? ButtonEffectsOverlayAssetSourcePath
        {
            get => buttonEffectsOverlayAssetSourcePath;
            set => SetField(ref buttonEffectsOverlayAssetSourcePath, value);
        }

        public ButtonAutomaticBackgroundMode ButtonAutomaticBackgroundMode
        {
            get => buttonAutomaticBackgroundMode;
            set => SetField(ref buttonAutomaticBackgroundMode, value);
        }

        public string ButtonAutomaticBackgroundPreset
        {
            get => buttonAutomaticBackgroundPreset;
            set => SetField(ref buttonAutomaticBackgroundPreset, value ?? string.Empty);
        }

        public Color ButtonAutomaticSolidColor
        {
            get => buttonAutomaticSolidColor;
            set => SetField(ref buttonAutomaticSolidColor, value);
        }

        public string? ButtonAutomaticBackgroundUploadAssetSourcePath
        {
            get => buttonAutomaticBackgroundUploadAssetSourcePath;
            set => SetField(ref buttonAutomaticBackgroundUploadAssetSourcePath, value);
        }

        public BitmapSource? ButtonAutomaticCutEmoteImage
        {
            get => buttonAutomaticCutEmoteImage;
            set => SetField(ref buttonAutomaticCutEmoteImage, value);
        }

        public string? SfxAssetSourcePath
        {
            get => sfxAssetSourcePath;
            set => SetField(ref sfxAssetSourcePath, value);
        }

        public ImageSource? PreAnimationPreview
        {
            get => preAnimationPreview;
            set
            {
                if (SetField(ref preAnimationPreview, value))
                {
                    OnPropertyChanged(nameof(HasPreAnimationValue));
                }
            }
        }

        public ImageSource? AnimationPreview
        {
            get => animationPreview;
            set
            {
                if (SetField(ref animationPreview, value))
                {
                    OnPropertyChanged(nameof(HasAnimationValue));
                    OnPropertyChanged(nameof(ShowPreAnimationField));
                }
            }
        }

        public ImageSource? ButtonIconPreview
        {
            get => buttonIconPreview;
            set
            {
                if (SetField(ref buttonIconPreview, value))
                {
                    OnPropertyChanged(nameof(HasButtonIconValue));
                }
            }
        }

        public bool IsResizeGuideVisible
        {
            get => isResizeGuideVisible;
            set => SetField(ref isResizeGuideVisible, value);
        }

        public string DisplayName => $"{Index}. {(string.IsNullOrWhiteSpace(Name) ? "Emote" : Name)}";
        public string EmoteHeader => $"Emote {Index}";
        public bool HasAnimationValue => !string.IsNullOrWhiteSpace(Animation);
        public bool ShowPreAnimationField => HasAnimationValue;
        public bool HasPreAnimationValue => !string.IsNullOrWhiteSpace(PreAnimation)
            && !string.Equals(PreAnimation, "-", StringComparison.Ordinal);
        public string AnimationDisplayText => $"Idle anim ({ResolveDisplayAssetName(AnimationAssetSourcePath, Animation, "none")})";
        public string PreAnimationDisplayText => $"Preanim ({ResolveDisplayAssetName(PreAnimationAssetSourcePath, PreAnimation, "none")})";
        public bool HasButtonIconValue => ButtonIconPreview != null
            || !string.IsNullOrWhiteSpace(ButtonIconToken)
            || !string.IsNullOrWhiteSpace(ButtonIconAssetSourcePath);
        public string SfxSummaryText => string.IsNullOrWhiteSpace(SfxName) || string.Equals(SfxName, "1", StringComparison.Ordinal)
            ? "No emote SFX set"
            : $"SFX: {SfxName} ({Math.Max(0, SfxDelayMs) * 40} ms{(SfxLooping ? ", loop" : string.Empty)})";

        public void RefreshDisplayName()
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(EmoteHeader));
        }

        public void RefreshSfxSummary()
        {
            OnPropertyChanged(nameof(SfxSummaryText));
        }

        public void ResetButtonIconConfiguration()
        {
            ButtonIconMode = ButtonIconMode.SingleImage;
            ButtonEffectsGenerationMode = ButtonEffectsGenerationMode.Darken;
            ButtonEffectsOpacityPercent = 75;
            ButtonEffectsDarknessPercent = 50;
            ButtonSingleImageAssetSourcePath = null;
            ButtonTwoImagesOnAssetSourcePath = null;
            ButtonTwoImagesOffAssetSourcePath = null;
            ButtonEffectsOverlayAssetSourcePath = null;
            ButtonAutomaticBackgroundMode = ButtonAutomaticBackgroundMode.None;
            ButtonAutomaticBackgroundPreset = "Oceanya Logo (preset)";
            ButtonAutomaticSolidColor = Colors.Transparent;
            ButtonAutomaticBackgroundUploadAssetSourcePath = null;
            ButtonAutomaticCutEmoteImage = null;
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

        private static string ResolveDisplayAssetName(string? sourcePath, string? token, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                return Path.GetFileName(sourcePath);
            }

            string trimmed = (token ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !string.Equals(trimmed, "-", StringComparison.Ordinal))
            {
                return trimmed;
            }

            return fallback;
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class EmoteTileEntryViewModel
    {
        public bool IsAddTile { get; set; }
        public CharacterCreationEmoteViewModel? Emote { get; set; }
    }

    public sealed class NamedIntOption
    {
        public NamedIntOption(int value, string name)
        {
            Value = value;
            Name = name;
        }

        public int Value { get; }
        public string Name { get; }
    }

    public enum ButtonIconMode
    {
        SingleImage = 0,
        TwoImages = 1,
        Automatic = 2
    }

    public enum ButtonEffectsGenerationMode
    {
        UseAssetAsBothVersions = 0,
        ReduceOpacity = 1,
        Darken = 2,
        Overlay = 3
    }

    public enum ButtonAutomaticBackgroundMode
    {
        PresetList = 0,
        SolidColor = 1,
        Upload = 2,
        None = 3
    }

    public sealed class ButtonIconGenerationConfig
    {
        public ButtonIconMode Mode { get; set; } = ButtonIconMode.SingleImage;
        public ButtonEffectsGenerationMode EffectsMode { get; set; } = ButtonEffectsGenerationMode.Darken;
        public int OpacityPercent { get; set; } = 75;
        public int DarknessPercent { get; set; } = 50;
        public string? SingleImagePath { get; set; }
        public string? TwoImagesOnPath { get; set; }
        public string? TwoImagesOffPath { get; set; }
        public string? OverlayImagePath { get; set; }
        public ButtonAutomaticBackgroundMode AutomaticBackgroundMode { get; set; } = ButtonAutomaticBackgroundMode.None;
        public string AutomaticBackgroundPreset { get; set; } = "Oceanya Logo (preset)";
        public Color AutomaticSolidColor { get; set; } = Colors.Transparent;
        public string? AutomaticBackgroundUploadPath { get; set; }
        public BitmapSource? AutomaticCutEmoteImage { get; set; }
    }

    internal sealed class CutSourceOption
    {
        public CutSourceOption(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }
        public string Path { get; }
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
