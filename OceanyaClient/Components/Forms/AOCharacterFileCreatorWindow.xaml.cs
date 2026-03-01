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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.CharacterCreator;
using OceanyaClient.Features.Startup;

namespace OceanyaClient
{
    public partial class AOCharacterFileCreatorWindow : OceanyaWindowContentControl, IStartupFunctionalityWindow
    {
        // AO2 hard-codes interjection maximum visual frame duration to 1500 ms (courtroom.h shout_max_time).
        private const int Ao2ShoutVisualCutoffMs = 1500;
        private const int Ao2TimingTickMs = 40;
        private const double MinTileWidth = 320;
        private const double MaxTileWidth = 760;
        private const double MinTileHeight = 330;
        private const double MaxTileHeight = 820;
        private const int EmotePreviewDecodePixelWidth = 256;
        private const string FileOrganizationDebugLogName = "oceanya_character_creator_fileorg_debug.log";
        private static readonly string[] StandardAssetRootFolders = { "Images/", "Sounds/" };
        private static readonly object FileOrganizationDebugLogSync = new object();

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
        private readonly ObservableCollection<FileOrganizationEntryViewModel> fileOrganizationEntries =
            new ObservableCollection<FileOrganizationEntryViewModel>();
        private readonly ObservableCollection<ExternalOrganizationEntry> externalOrganizationEntries =
            new ObservableCollection<ExternalOrganizationEntry>();
        private readonly Dictionary<string, string> generatedOrganizationOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> suppressedGeneratedAssetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> stagedTextAssetSourcePathsByRelativePath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileOrganizationEntryViewModel> allFileOrganizationEntries = new List<FileOrganizationEntryViewModel>();
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
        private readonly Dictionary<string, AO2BlipPreviewPlayer> shoutSfxPreviewPlayers =
            new Dictionary<string, AO2BlipPreviewPlayer>(StringComparer.OrdinalIgnoreCase);
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
        private BitmapSource? generatedCharacterIconImage;
        private CancellationTokenSource? blipPreviewCancellation;
        private Point fileOrganizationDragStartPoint;
        private Point emoteTileDragStartPoint;
        private EmoteTileEntryViewModel? pendingTileDragEntry;
        private EmoteTileEntryViewModel? currentDragTargetEntry;
        private EmoteTileEntryViewModel? activeTileDragEntry;
        private FrameworkElement? activeTileDragContainer;
        private Popup? emoteDragGhostPopup;
        private bool isInternalEmoteReorderDragInProgress;
        private Point emoteDragGhostPointerOffset;
        private readonly Dictionary<UIElement, TranslateTransform> emoteTileAnimationTransforms =
            new Dictionary<UIElement, TranslateTransform>();
        private bool suppressEmoteTileSelectionChanged;
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
        private string currentFileOrganizationPath = string.Empty;
        private FileOrganizationEntryViewModel? fileOrganizationContextEntry;
        private readonly Dictionary<string, IAnimationPlayer> fileOrganizationPreviewPlayers = new Dictionary<string, IAnimationPlayer>(StringComparer.OrdinalIgnoreCase);
        private readonly AO2BlipPreviewPlayer fileOrganizationAudioPreviewPlayer = new AO2BlipPreviewPlayer();
        private PlayStopProgressButton? activeFileOrganizationAudioButton;
        private readonly List<FileOrganizationClipboardEntry> fileOrganizationClipboardEntries = new List<FileOrganizationClipboardEntry>();
        private bool fileOrganizationClipboardIsCut;
        private bool isEditMode;
        private bool editApplyCompleted;
        private bool hasLoadedExistingFileOrganizationState;
        private string originalEditCharacterDirectoryPath = string.Empty;
        private string originalEditCharacterFolderName = string.Empty;
        private string loadedSourceCharacterDirectoryPath = string.Empty;
        private string lastAppliedCharacterDirectoryPath = string.Empty;
        private string previousAppliedCharacterDirectoryPath = string.Empty;
        private readonly Dictionary<string, CutSelectionState> savedCutSelectionByEmoteKey =
            new Dictionary<string, CutSelectionState>(StringComparer.OrdinalIgnoreCase);
        private Thumb? activeFileOrganizationRowResizeThumb;
        private DataGridRow? activeFileOrganizationRowResizeRow;
        private bool fileOrganizationRowResizeFromDivider;
        private double fileOrganizationRowResizeStartHeight;
        private double fileOrganizationRowResizePreviewHeight = 74;
        private double fileOrganizationRowResizeGuideStartY;
        private AdornerLayer? fileOrganizationRowResizeAdornerLayer;
        private RowResizeGuideAdorner? fileOrganizationRowResizeGuideAdorner;
        private DateTime suppressFolderOpenFromRenameUntilUtc = DateTime.MinValue;
        private string suppressFolderOpenFromRenamePath = string.Empty;
        private string lastCommittedStateFingerprint = string.Empty;
        private bool skipCloseConfirmationOnce;
        private bool preserveFileOrganizationMultiSelectionForDrag;
        private FileOrganizationEntryViewModel[] fileOrganizationDragSelectionSnapshot = Array.Empty<FileOrganizationEntryViewModel>();

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

        public bool EditApplyCompleted => editApplyCompleted;
        public bool CharacterGenerationCompleted => !string.IsNullOrWhiteSpace(lastAppliedCharacterDirectoryPath);
        public string LastAppliedCharacterDirectoryPath => lastAppliedCharacterDirectoryPath;
        public string PreviousAppliedCharacterDirectoryPath => previousAppliedCharacterDirectoryPath;

        public AOCharacterFileCreatorWindow()
        {
            InitializeComponent();
            Title = "AO Character File Creator";
            SourceInitialized += Window_SourceInitialized;
            StateChanged += Window_StateChanged;
            Closing += Window_Closing;

            EmoteTilesListBox.ItemsSource = emoteTiles;
            AdvancedEntriesListBox.ItemsSource = advancedEntries;
            FileOrganizationListView.ItemsSource = fileOrganizationEntries;
            FileOrganizationListView.RowHeight = fileOrganizationRowResizePreviewHeight;
            SideComboBox.ItemsSource = sideOptions;
            GenderBlipsDropdown.ItemsSource = blipOptions;
            ChatDropdown.ItemsSource = chatOptions;
            EffectsDropdown.ItemsSource = effectsFolderOptions;
            ScalingDropdown.ItemsSource = scalingOptions;
            StretchDropdown.ItemsSource = booleanOptions;
            NeedsShownameDropdown.ItemsSource = booleanOptions;
            SideComboBox.IsTextReadOnly = true;
            ChatDropdown.IsTextReadOnly = true;
            EffectsDropdown.IsTextReadOnly = true;
            ScalingDropdown.IsTextReadOnly = true;
            StretchDropdown.IsTextReadOnly = true;
            NeedsShownameDropdown.IsTextReadOnly = true;

            FrameTargetComboBox.ItemsSource = FrameTargetOptionNames;
            FrameTypeComboBox.ItemsSource = FrameEventTypeOptionNames;
            FrameTargetComboBox.IsTextReadOnly = true;
            FrameTypeComboBox.IsTextReadOnly = true;
            ButtonIconsApplyScopeDropdown.ItemsSource = ButtonIconsApplyScopeOptionNames;
            ButtonIconsApplyScopeDropdown.IsTextReadOnly = true;
            ButtonIconsBackgroundDropdown.ItemsSource = GetAutomaticBackgroundOptions();
            ButtonIconsEffectsDropdown.ItemsSource = ButtonEffectsGenerationOptionNames;
            ButtonIconsBackgroundDropdown.IsTextReadOnly = true;
            ButtonIconsEffectsDropdown.IsTextReadOnly = true;
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
            LoadPersistedCutSelections();
            InitializeEffectsFieldContextMenus();
            RefreshEmoteTiles();
            UpdateFolderAvailabilityStatus();
            lastCommittedStateFingerprint = ComputeCurrentStateFingerprint();
        }

        public AOCharacterFileCreatorWindow(string characterDirectoryPathToEdit) : this()
        {
            if (!string.IsNullOrWhiteSpace(characterDirectoryPathToEdit))
            {
                _ = TryLoadCharacterFolderForEditing(characterDirectoryPathToEdit, out _);
            }
        }

        /// <inheritdoc/>
        public override string HeaderText => "AO CHARACTER FILE CREATOR";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        private void LoadPersistedCutSelections()
        {
            savedCutSelectionByEmoteKey.Clear();
            Dictionary<string, CharacterCreatorCutSelectionState>? persisted = SaveFile.Data.CharacterCreatorCutSelections;
            if (persisted == null)
            {
                return;
            }

            foreach (KeyValuePair<string, CharacterCreatorCutSelectionState> pair in persisted)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                Rect normalized = new Rect(pair.Value.X, pair.Value.Y, pair.Value.Width, pair.Value.Height);
                if (normalized.Width <= 0 || normalized.Height <= 0)
                {
                    continue;
                }

                savedCutSelectionByEmoteKey[pair.Key.Trim()] = new CutSelectionState
                {
                    SourcePath = pair.Value.SourcePath?.Trim() ?? string.Empty,
                    NormalizedSelection = normalized
                };
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWorkAreaMaxBounds();
            RefreshChatPreview();
            FinishedLoading?.Invoke();
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            Window? host = HostWindow;
            if (host == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(host).Handle;
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
            generatedCharacterIconImage = null;
            CharacterIconPreviewImage.Source = null;
            CharacterIconEmptyText.Visibility = Visibility.Visible;
            SideComboBox.Text = "wit";
            string? femaleDefault = blipOptions.FirstOrDefault(option =>
                string.Equals(option, "female", StringComparison.OrdinalIgnoreCase));
            GenderBlipsDropdown.Text = femaleDefault ?? blipOptions.FirstOrDefault() ?? string.Empty;
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
            UpdateCharacterIconFromEmoteButtonVisibility();
            UpdateFolderAvailabilityStatus();
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
            if (ReferenceEquals(sender, CharacterFolderNameTextBox))
            {
                UpdateFolderAvailabilityStatus();
            }
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

            UpdateFolderAvailabilityStatus();
        }

        private static string BuildCharactersDirectoryDisplayPath(string mountPath)
        {
            string path = Path.Combine(mountPath ?? string.Empty, "characters");
            path = path.Replace('\\', '/').TrimEnd('/');
            return path + "/";
        }

        private string ResolveSelectedMountPathForCreation()
        {
            string selected = (MountPathComboBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return selected;
            }

            if (mountPathOptions.Count > 0)
            {
                return mountPathOptions[0];
            }

            return string.Empty;
        }

        public bool TryLoadCharacterFolderForEditing(string characterDirectoryPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForEditing start input={characterDirectoryPath}");
                string normalizedDirectoryPath = Path.GetFullPath((characterDirectoryPath ?? string.Empty).Trim());
                if (string.IsNullOrWhiteSpace(normalizedDirectoryPath) || !Directory.Exists(normalizedDirectoryPath))
                {
                    errorMessage = "Character folder was not found on disk.";
                    WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForEditing failed missing-folder path={normalizedDirectoryPath}");
                    return false;
                }

                string charIniPath = ResolveCharacterIniPath(normalizedDirectoryPath);
                if (string.IsNullOrWhiteSpace(charIniPath) || !File.Exists(charIniPath))
                {
                    errorMessage = "char.ini was not found in this character folder.";
                    WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForEditing failed missing-charini root={normalizedDirectoryPath}");
                    return false;
                }

                CharacterFolder sourceFolder = CharacterFolder.Create(charIniPath);
                IniDocument iniDocument = IniDocument.Load(charIniPath);
                string inferredMountPath = Path.GetDirectoryName(Path.GetDirectoryName(normalizedDirectoryPath) ?? string.Empty) ?? string.Empty;

                isEditMode = true;
                editApplyCompleted = false;
                originalEditCharacterDirectoryPath = normalizedDirectoryPath;
                originalEditCharacterFolderName = Path.GetFileName(normalizedDirectoryPath);
                loadedSourceCharacterDirectoryPath = normalizedDirectoryPath;
                if (!string.IsNullOrWhiteSpace(inferredMountPath) &&
                    !mountPathOptions.Contains(inferredMountPath, StringComparer.OrdinalIgnoreCase))
                {
                    mountPathOptions.Add(inferredMountPath);
                }

                if (!string.IsNullOrWhiteSpace(inferredMountPath))
                {
                    MountPathComboBox.Text = inferredMountPath;
                }

                CharacterFolderNameTextBox.Text = originalEditCharacterFolderName;
                ShowNameTextBox.Text = GetIniValueOrDefault(iniDocument, "Options", "showname", sourceFolder.configINI.ShowName);
                SideComboBox.Text = GetIniValueOrDefault(iniDocument, "Options", "side", sourceFolder.configINI.Side);

                string blipsValue = GetIniValueOrDefault(
                    iniDocument,
                    "Options",
                    "blips",
                    GetIniValueOrDefault(iniDocument, "Options", "gender", sourceFolder.configINI.Gender));
                SetLoadedBlipsValue(blipsValue, normalizedDirectoryPath);

                ChatDropdown.Text = GetIniValueOrDefault(iniDocument, "Options", "chat", "default");
                EffectsDropdown.Text = GetIniValueOrDefault(iniDocument, "Options", "effects", string.Empty);
                ScalingDropdown.Text = GetIniValueOrDefault(iniDocument, "Options", "scaling", string.Empty);
                StretchDropdown.Text = GetIniValueOrDefault(iniDocument, "Options", "stretch", string.Empty);
                NeedsShownameDropdown.Text = GetIniValueOrDefault(iniDocument, "Options", "needs_showname", string.Empty);
                CustomShoutNameTextBox.Text = GetIniValueOrDefault(iniDocument, "Shouts", "custom_name", string.Empty);

                string realizationValue = GetIniValueOrDefault(iniDocument, "Options", "realization", string.Empty);
                string? resolvedRealizationPath = ResolveAudioTokenPathWithinCharacterDirectory(normalizedDirectoryPath, realizationValue);
                if (!string.IsNullOrWhiteSpace(resolvedRealizationPath))
                {
                    selectedRealizationSourcePath = resolvedRealizationPath;
                    RealizationTextBox.Text = Path.GetFileName(resolvedRealizationPath);
                }
                else
                {
                    selectedRealizationSourcePath = string.Empty;
                    RealizationTextBox.Text = realizationValue;
                }

                selectedCharacterIconSourcePath = sourceFolder.CharIconPath?.Trim() ?? string.Empty;
                generatedCharacterIconImage = null;
                if (!string.IsNullOrWhiteSpace(selectedCharacterIconSourcePath) && File.Exists(selectedCharacterIconSourcePath))
                {
                    CharacterIconPreviewImage.Source = TryLoadPreviewImage(selectedCharacterIconSourcePath);
                    CharacterIconEmptyText.Visibility = CharacterIconPreviewImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    CharacterIconPreviewImage.Source = null;
                    CharacterIconEmptyText.Visibility = Visibility.Visible;
                }

                LoadShoutAssetsFromFolder(normalizedDirectoryPath);
                LoadEmotesFromCharacter(sourceFolder, iniDocument, normalizedDirectoryPath);
                LoadAdvancedEntriesFromIni(iniDocument);
                InitializeFileOrganizationFromExistingFolder(normalizedDirectoryPath);
                WriteFileOrganizationDebugLog(
                    $"TryLoadCharacterFolderForEditing loaded root={normalizedDirectoryPath} mode=edit generatedOverrides={generatedOrganizationOverrides.Count} suppressed={suppressedGeneratedAssetKeys.Count} external={externalOrganizationEntries.Count}");

                ApplyEditModeUiState();
                RefreshChatPreview();
                RefreshEmoteTiles();
                SetActiveSection("setup");
                lastCommittedStateFingerprint = ComputeCurrentStateFingerprint();
                StatusTextBlock.Text = $"Loaded character folder for editing: {originalEditCharacterFolderName}";
                WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForEditing success root={normalizedDirectoryPath}");
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to load character folder for editing.", ex);
                errorMessage = ex.Message;
                WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForEditing exception input={characterDirectoryPath} error={ex.Message}");
                return false;
            }
        }

        public bool TryLoadCharacterFolderForDuplication(string characterDirectoryPath, out string errorMessage)
        {
            WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForDuplication start source={characterDirectoryPath}");
            if (!TryLoadCharacterFolderForEditing(characterDirectoryPath, out errorMessage))
            {
                WriteFileOrganizationDebugLog($"TryLoadCharacterFolderForDuplication failed source={characterDirectoryPath} error={errorMessage}");
                return false;
            }

            isEditMode = false;
            editApplyCompleted = false;
            originalEditCharacterDirectoryPath = string.Empty;
            originalEditCharacterFolderName = string.Empty;

            string sourceFolderName = Path.GetFileName((characterDirectoryPath ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            string mountPath = ResolveSelectedMountPathForCreation();
            CharacterFolderNameTextBox.Text = BuildDuplicateFolderName(sourceFolderName, mountPath);

            ApplyCreateModeUiState();
            UpdateFolderAvailabilityStatus();
            StatusTextBlock.Text = "Loaded character folder for duplication.";
            WriteFileOrganizationDebugLog(
                $"TryLoadCharacterFolderForDuplication success source={characterDirectoryPath} folderName={CharacterFolderNameTextBox.Text} loadedSourceRoot={loadedSourceCharacterDirectoryPath} generatedOverrides={generatedOrganizationOverrides.Count} suppressed={suppressedGeneratedAssetKeys.Count}");
            return true;
        }

        private static string ResolveCharacterIniPath(string characterDirectoryPath)
        {
            string rootIni = Path.Combine(characterDirectoryPath, "char.ini");
            if (File.Exists(rootIni))
            {
                return rootIni;
            }

            string[] iniFiles = Directory.GetFiles(characterDirectoryPath, "char.ini", SearchOption.AllDirectories);
            return iniFiles.FirstOrDefault() ?? string.Empty;
        }

        private static string GetIniValueOrDefault(IniDocument document, string section, string key, string fallback)
        {
            if (document.TryGetLatestValue(section, key, out string? value))
            {
                return value ?? string.Empty;
            }

            return fallback ?? string.Empty;
        }

        private void SetLoadedBlipsValue(string rawBlipsValue, string characterDirectoryPath)
        {
            selectedCustomBlipSourcePath = string.Empty;
            customBlipOptionText = string.Empty;
            string value = (rawBlipsValue ?? string.Empty).Trim();
            string? resolvedPath = ResolveAudioTokenPathWithinCharacterDirectory(characterDirectoryPath, value);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                selectedCustomBlipSourcePath = resolvedPath;
                customBlipOptionText = BuildCustomBlipOptionText(Path.GetFileNameWithoutExtension(resolvedPath));
                if (!blipOptions.Contains(customBlipOptionText, StringComparer.OrdinalIgnoreCase))
                {
                    blipOptions.Add(customBlipOptionText);
                }

                SetBlipText(customBlipOptionText);
                return;
            }

            if (!string.IsNullOrWhiteSpace(value) && !blipOptions.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                blipOptions.Add(value);
            }

            SetBlipText(value);
        }

        private static string BuildCustomBlipOptionText(string fileStem)
        {
            string token = string.IsNullOrWhiteSpace(fileStem) ? "custom" : fileStem.Trim();
            return $"custom ({token})";
        }

        private void LoadShoutAssetsFromFolder(string characterDirectoryPath)
        {
            foreach (string key in new[] { "holdit", "objection", "takethat", "custom" })
            {
                selectedShoutVisualSourcePaths.Remove(key);
                selectedShoutSfxSourcePaths.Remove(key);

                string? visualPath = ResolveAssetByBaseName(characterDirectoryPath, key == "holdit"
                    ? "holdit_bubble"
                    : key == "objection"
                        ? "objection_bubble"
                        : key == "takethat"
                            ? "takethat_bubble"
                            : "custom", isAudio: false);
                if (!string.IsNullOrWhiteSpace(visualPath))
                {
                    selectedShoutVisualSourcePaths[key] = visualPath;
                    SetShoutVisualFileNameText(key, Path.GetFileName(visualPath));
                }
                else
                {
                    ResetSingleShoutToDefault(key);
                }

                string? sfxPath = ResolveAssetByBaseName(characterDirectoryPath, key, isAudio: true);
                if (!string.IsNullOrWhiteSpace(sfxPath))
                {
                    selectedShoutSfxSourcePaths[key] = sfxPath;
                    SetShoutSfxFileNameText(key, Path.GetFileName(sfxPath));
                }
                else
                {
                    SetShoutSfxFileNameText(key, "Default sfx");
                }
            }

            foreach (string key in new[] { "holdit", "objection", "takethat", "custom" })
            {
                UpdateShoutTilePreview(key);
            }
        }

        private void LoadEmotesFromCharacter(CharacterFolder sourceFolder, IniDocument iniDocument, string characterDirectoryPath)
        {
            StopAllEmotePreviewPlayers();
            emotes.Clear();
            emoteTiles.Clear();
            bulkButtonCutoutByEmoteId.Clear();

            Dictionary<int, bool> sfxLoopingById = new Dictionary<int, bool>();
            foreach (IniEntry entry in iniDocument.GetEntries("SoundL"))
            {
                if (!int.TryParse(entry.Key, out int id))
                {
                    continue;
                }

                sfxLoopingById[id] = string.Equals((entry.Value ?? string.Empty).Trim(), "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals((entry.Value ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }

            Dictionary<int, string> blipsOverrideByEmoteId = ResolveBlipsOverrides(iniDocument);

            IEnumerable<KeyValuePair<int, Emote>> ordered = sourceFolder.configINI.Emotions
                .OrderBy(static pair => pair.Key);
            foreach (KeyValuePair<int, Emote> pair in ordered)
            {
                int emoteId = pair.Key;
                Emote source = pair.Value ?? new Emote(emoteId);
                CharacterCreationEmoteViewModel viewModel = new CharacterCreationEmoteViewModel
                {
                    Index = emoteId,
                    Name = source.Name?.Trim() ?? $"Emote {emoteId}",
                    PreAnimation = string.IsNullOrWhiteSpace(source.PreAnimation) ? "-" : source.PreAnimation.Trim(),
                    Animation = source.Animation?.Trim() ?? string.Empty,
                    EmoteModifier = (int)source.Modifier,
                    DeskModifier = (int)source.DeskMod,
                    SfxName = source.sfxName?.Trim() ?? "1",
                    SfxDelayMs = Math.Max(0, source.sfxDelay),
                    SfxLooping = sfxLoopingById.TryGetValue(emoteId, out bool loops) && loops,
                    PreAnimationDurationMs = ResolveDurationFromIni(iniDocument, "Time", source.PreAnimation),
                    StayTimeMs = ResolveDurationFromIni(iniDocument, "stay_time", source.PreAnimation),
                    BlipsOverride = blipsOverrideByEmoteId.TryGetValue(emoteId, out string? overrideBlips)
                        ? overrideBlips
                        : string.Empty
                };

                if (!string.IsNullOrWhiteSpace(source.PreAnimation) && !string.Equals(source.PreAnimation, "-", StringComparison.Ordinal))
                {
                    viewModel.PreAnimationAssetSourcePath =
                        ResolveImageTokenPathWithinCharacterDirectory(characterDirectoryPath, source.PreAnimation);
                    viewModel.PreAnimationPreview = TryLoadPreviewImage(viewModel.PreAnimationAssetSourcePath ?? string.Empty);
                }

                viewModel.AnimationAssetSourcePath =
                    ResolveAnimationTokenPathWithinCharacterDirectory(characterDirectoryPath, source.Animation);
                ResolveSplitAnimationAssets(characterDirectoryPath, source.Animation, viewModel);
                viewModel.AnimationPreview = TryLoadPreviewImage(
                    viewModel.AnimationAssetSourcePath
                    ?? viewModel.FinalAnimationIdleAssetSourcePath
                    ?? viewModel.FinalAnimationTalkingAssetSourcePath
                    ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(source.PathToImage_on) && File.Exists(source.PathToImage_on))
                {
                    viewModel.ButtonTwoImagesOnAssetSourcePath = source.PathToImage_on;
                }

                if (!string.IsNullOrWhiteSpace(source.PathToImage_off) && File.Exists(source.PathToImage_off))
                {
                    viewModel.ButtonTwoImagesOffAssetSourcePath = source.PathToImage_off;
                }

                if (!string.IsNullOrWhiteSpace(viewModel.ButtonTwoImagesOnAssetSourcePath)
                    && !string.IsNullOrWhiteSpace(viewModel.ButtonTwoImagesOffAssetSourcePath))
                {
                    viewModel.ButtonIconMode = ButtonIconMode.TwoImages;
                    viewModel.ButtonIconAssetSourcePath = viewModel.ButtonTwoImagesOnAssetSourcePath;
                    viewModel.ButtonIconPreview = TryLoadPreviewImage(viewModel.ButtonTwoImagesOnAssetSourcePath);
                    viewModel.ButtonIconToken = Path.GetFileNameWithoutExtension(viewModel.ButtonTwoImagesOnAssetSourcePath);
                }

                viewModel.SfxAssetSourcePath = ResolveAudioTokenPathWithinCharacterDirectory(characterDirectoryPath, source.sfxName);
                viewModel.FrameEvents.Clear();
                foreach (FrameEventViewModel frameEvent in ResolveFrameEventsForEmote(iniDocument, viewModel))
                {
                    viewModel.FrameEvents.Add(frameEvent);
                }

                emotes.Add(viewModel);
            }

            RefreshEmoteLabels();
            if (emotes.Count > 0)
            {
                EnsureEmotePreviewPlayersInitialized();
                SelectEmoteTile(emotes[0]);
                RestartEmotePreviewPlayers(emotes[0]);
            }
        }

        private void ResolveSplitAnimationAssets(
            string characterDirectoryPath,
            string animationToken,
            CharacterCreationEmoteViewModel viewModel)
        {
            string token = (animationToken ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            string sanitizedToken = token.Trim().TrimStart('/');
            string baseDirectory = (Path.GetDirectoryName(sanitizedToken.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty)
                .Replace('\\', '/')
                .Trim('/');
            string fileName = Path.GetFileName(sanitizedToken);
            string fileStem = Path.GetFileNameWithoutExtension(fileName);
            string canonicalName = StripSplitPrefix(fileStem);
            if (string.IsNullOrWhiteSpace(canonicalName))
            {
                return;
            }

            string[] idleCandidates = BuildSplitAnimationCandidates(baseDirectory, canonicalName, "a");
            string[] talkingCandidates = BuildSplitAnimationCandidates(baseDirectory, canonicalName, "b");

            viewModel.FinalAnimationIdleAssetSourcePath = ResolveFirstExistingImageToken(characterDirectoryPath, idleCandidates);
            viewModel.FinalAnimationTalkingAssetSourcePath = ResolveFirstExistingImageToken(characterDirectoryPath, talkingCandidates);

            if (!string.IsNullOrWhiteSpace(viewModel.AnimationAssetSourcePath))
            {
                if (string.IsNullOrWhiteSpace(viewModel.FinalAnimationIdleAssetSourcePath))
                {
                    viewModel.FinalAnimationIdleAssetSourcePath = ResolveCounterpartSplitPath(
                        characterDirectoryPath,
                        viewModel.AnimationAssetSourcePath,
                        "a");
                }

                if (string.IsNullOrWhiteSpace(viewModel.FinalAnimationTalkingAssetSourcePath))
                {
                    viewModel.FinalAnimationTalkingAssetSourcePath = ResolveCounterpartSplitPath(
                        characterDirectoryPath,
                        viewModel.AnimationAssetSourcePath,
                        "b");
                }
            }

            if (string.IsNullOrWhiteSpace(viewModel.AnimationAssetSourcePath))
            {
                viewModel.AnimationAssetSourcePath = viewModel.FinalAnimationIdleAssetSourcePath
                    ?? viewModel.FinalAnimationTalkingAssetSourcePath;
            }
        }

        private static string[] BuildSplitAnimationCandidates(string baseDirectory, string canonicalName, string channel)
        {
            string safeChannel = string.Equals(channel, "b", StringComparison.OrdinalIgnoreCase) ? "b" : "a";
            string sanitizedDirectory = (baseDirectory ?? string.Empty).Trim().Trim('/').Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(canonicalName))
            {
                return Array.Empty<string>();
            }

            if (string.IsNullOrWhiteSpace(sanitizedDirectory))
            {
                return new[]
                {
                    $"({safeChannel}){canonicalName}",
                    $"({safeChannel})/{canonicalName}",
                    $"{safeChannel}/{canonicalName}",
                    canonicalName
                };
            }

            return new[]
            {
                $"{sanitizedDirectory}/({safeChannel}){canonicalName}",
                $"{sanitizedDirectory}/({safeChannel})/{canonicalName}",
                $"{sanitizedDirectory}/{safeChannel}/{canonicalName}",
                $"({safeChannel}){canonicalName}",
                $"({safeChannel})/{canonicalName}"
            };
        }

        private static string StripSplitPrefix(string animationName)
        {
            string value = (animationName ?? string.Empty).Trim();
            foreach (string prefix in new[] { "(a)/", "(b)/", "(a)", "(b)", "a/", "b/" })
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring(prefix.Length).TrimStart('/');
                }
            }

            return value;
        }

        private static string? ResolveFirstExistingImageToken(string characterDirectoryPath, IEnumerable<string> candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string? resolved = ResolveImageTokenPathWithinCharacterDirectory(characterDirectoryPath, candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        private static string? ResolveCounterpartSplitPath(string characterDirectoryPath, string resolvedAnimationPath, string targetChannel)
        {
            if (string.IsNullOrWhiteSpace(resolvedAnimationPath))
            {
                return null;
            }

            string safeTarget = string.Equals(targetChannel, "b", StringComparison.OrdinalIgnoreCase) ? "b" : "a";
            string fileName = Path.GetFileNameWithoutExtension(resolvedAnimationPath);
            string canonical = StripSplitPrefix(fileName);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                return null;
            }

            string extension = Path.GetExtension(resolvedAnimationPath);
            string directory = Path.GetDirectoryName(resolvedAnimationPath) ?? string.Empty;
            string relativeDirectory;
            try
            {
                relativeDirectory = Path.GetRelativePath(characterDirectoryPath, directory).Replace('\\', '/');
            }
            catch
            {
                relativeDirectory = string.Empty;
            }

            relativeDirectory = relativeDirectory.Trim().Trim('/');
            relativeDirectory = relativeDirectory
                .Replace("/(a)/", $"/({safeTarget})/", StringComparison.OrdinalIgnoreCase)
                .Replace("/(b)/", $"/({safeTarget})/", StringComparison.OrdinalIgnoreCase)
                .Replace("/a/", $"/{safeTarget}/", StringComparison.OrdinalIgnoreCase)
                .Replace("/b/", $"/{safeTarget}/", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(relativeDirectory, "(a)", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativeDirectory, "(b)", StringComparison.OrdinalIgnoreCase))
            {
                relativeDirectory = $"({safeTarget})";
            }
            else if (string.Equals(relativeDirectory, "a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativeDirectory, "b", StringComparison.OrdinalIgnoreCase))
            {
                relativeDirectory = safeTarget;
            }

            IEnumerable<string> baseCandidates = BuildSplitAnimationCandidates(relativeDirectory, canonical, safeTarget);
            List<string> explicitExtensionCandidates = string.IsNullOrWhiteSpace(extension)
                ? new List<string>()
                : baseCandidates.Select(candidate => candidate + extension).ToList();
            return ResolveFirstExistingImageToken(characterDirectoryPath, explicitExtensionCandidates.Concat(baseCandidates));
        }

        private void LoadAdvancedEntriesFromIni(IniDocument iniDocument)
        {
            advancedEntries.Clear();

            HashSet<string> reservedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "version",
                "options",
                "shouts",
                "time",
                "stay_time",
                "emotions",
                "soundn",
                "soundt",
                "soundl",
                "optionsn"
            };

            foreach (string sectionName in iniDocument.Sections.Keys)
            {
                if (reservedSections.Contains(sectionName))
                {
                    continue;
                }

                if (sectionName.StartsWith("options", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (sectionName.EndsWith("_framesfx", StringComparison.OrdinalIgnoreCase)
                    || sectionName.EndsWith("_framescreenshake", StringComparison.OrdinalIgnoreCase)
                    || sectionName.EndsWith("_framerealization", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (IniEntry entry in iniDocument.GetEntries(sectionName))
                {
                    advancedEntries.Add(new AdvancedEntryViewModel
                    {
                        Section = sectionName,
                        Key = entry.Key,
                        Value = entry.Value
                    });
                }
            }
        }

        private void InitializeFileOrganizationFromExistingFolder(string characterDirectoryPath)
        {
            ClearStagedTextAssetEdits();
            generatedOrganizationOverrides.Clear();
            suppressedGeneratedAssetKeys.Clear();
            externalOrganizationEntries.Clear();
            hasLoadedExistingFileOrganizationState = false;
            WriteFileOrganizationDebugLog("----------------------------------------------------------------");
            WriteFileOrganizationDebugLog($"InitializeFileOrganizationFromExistingFolder start root={characterDirectoryPath}");

            string normalizedRoot = NormalizePathForCompare(characterDirectoryPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
            {
                WriteFileOrganizationDebugLog($"InitializeFileOrganizationFromExistingFolder root-missing normalized={normalizedRoot}");
                RefreshFileOrganizationEntries();
                return;
            }

            string[] topLevelDirectories = Directory.GetDirectories(normalizedRoot);
            string[] topLevelFiles = Directory.GetFiles(normalizedRoot);
            WriteFileOrganizationDebugLog(
                $"InitializeFileOrganizationFromExistingFolder root-ready normalized={normalizedRoot} topDirs={topLevelDirectories.Length} topFiles={topLevelFiles.Length} dirs=[{string.Join(", ", topLevelDirectories.Select(Path.GetFileName))}]");

            List<FileOrganizationEntryViewModel> generatedEntries = BuildGeneratedFileOrganizationEntries();
            WriteFileOrganizationDebugLog($"InitializeFileOrganizationFromExistingFolder generatedEntries={generatedEntries.Count}");
            Dictionary<string, string> usedFilePathByAssetKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> generatedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> generatedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> existingFiles = Directory
                .EnumerateFiles(normalizedRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, path), isFolder: false))
                .ToList();
            Dictionary<string, List<string>> existingFilesByName = existingFiles
                .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (FileOrganizationEntryViewModel generated in generatedEntries)
            {
                string generatedRelative = NormalizeRelativePath(generated.RelativePath, generated.IsFolder);
                if (generated.IsFolder)
                {
                    generatedFolderPaths.Add(generatedRelative);
                    string absoluteFolder = Path.Combine(normalizedRoot, generatedRelative.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(absoluteFolder))
                    {
                        usedFolderPaths.Add(generatedRelative);
                    }

                    continue;
                }

                generatedFilePaths.Add(generatedRelative);
                string? resolvedRelative = ResolveExistingFileRelativePathForGeneratedEntry(
                    normalizedRoot,
                    generated,
                    generatedEntries,
                    generatedFilePaths,
                    existingFilesByName);
                if (string.IsNullOrWhiteSpace(resolvedRelative))
                {
                    if (!string.IsNullOrWhiteSpace(generated.AssetKey))
                    {
                        suppressedGeneratedAssetKeys.Add(generated.AssetKey);
                        WriteFileOrganizationDebugLog($"Suppress generated-missing key={generated.AssetKey} default={generated.DefaultRelativePath} relative={generated.RelativePath}");
                    }

                    continue;
                }

                suppressedGeneratedAssetKeys.Remove(generated.AssetKey);
                usedFilePathByAssetKey[generated.AssetKey] = resolvedRelative;
                if (!string.IsNullOrWhiteSpace(generated.AssetKey))
                {
                    // Preserve the exact discovered path mapping so later UI refreshes do not drift back to defaults.
                    generatedOrganizationOverrides[generated.AssetKey] = resolvedRelative;
                    WriteFileOrganizationDebugLog(
                        $"Resolve generated key={generated.AssetKey} resolved={resolvedRelative} default={generated.DefaultRelativePath} source={generated.SourcePath}");
                }
            }

            foreach (string usedRelativePath in usedFilePathByAssetKey.Values)
            {
                string parent = GetParentDirectoryPath(usedRelativePath, isFolder: false);
                while (!string.IsNullOrWhiteSpace(parent))
                {
                    usedFolderPaths.Add(parent);
                    parent = GetParentDirectoryPath(parent, isFolder: true);
                }
            }

            HashSet<string> usedFiles = usedFilePathByAssetKey.Values
                .Select(path => NormalizeRelativePath(path, isFolder: false))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string existingRelative in existingFiles)
            {
                if (usedFiles.Contains(existingRelative))
                {
                    continue;
                }

                string sourcePath = Path.Combine(normalizedRoot, existingRelative.Replace('/', Path.DirectorySeparatorChar));
                externalOrganizationEntries.Add(new ExternalOrganizationEntry
                {
                    SourcePath = sourcePath,
                    RelativePath = existingRelative,
                    IsFolder = false
                });
            }

            List<string> existingFolders = Directory
                .EnumerateDirectories(normalizedRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, path), isFolder: true))
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path.Count(ch => ch == '/'))
                .ToList();

            HashSet<string> trackedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folderPath in generatedFolderPaths)
            {
                trackedFolders.Add(folderPath);
            }

            foreach (string folderPath in usedFolderPaths)
            {
                trackedFolders.Add(folderPath);
            }

            foreach (string existingFolder in existingFolders)
            {
                if (IsPhantomStandardAssetFolder(normalizedRoot, existingFolder))
                {
                    WriteFileOrganizationDebugLog(
                        $"InitializeFileOrganizationFromExistingFolder skip-phantom-folder relative={existingFolder}");
                    continue;
                }

                if (trackedFolders.Contains(existingFolder))
                {
                    continue;
                }

                externalOrganizationEntries.Add(new ExternalOrganizationEntry
                {
                    RelativePath = existingFolder,
                    IsFolder = true
                });
                trackedFolders.Add(existingFolder);
            }

            hasLoadedExistingFileOrganizationState = true;
            WriteFileOrganizationDebugLog(
                $"InitializeFileOrganizationFromExistingFolder done usedFiles={usedFiles.Count} external={externalOrganizationEntries.Count} overrides={generatedOrganizationOverrides.Count} suppressed={suppressedGeneratedAssetKeys.Count}");
            RefreshFileOrganizationEntries();
        }

        private string? ResolveExistingFileRelativePathForGeneratedEntry(
            string characterRoot,
            FileOrganizationEntryViewModel generated,
            IReadOnlyList<FileOrganizationEntryViewModel> generatedEntries,
            ISet<string> generatedFilePaths,
            IReadOnlyDictionary<string, List<string>> existingFilesByName)
        {
            if (generated == null || generated.IsFolder)
            {
                return null;
            }

            string defaultRelative = NormalizeRelativePath(generated.DefaultRelativePath, isFolder: false);
            if (!string.IsNullOrWhiteSpace(defaultRelative))
            {
                string defaultAbsolute = Path.Combine(characterRoot, defaultRelative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(defaultAbsolute))
                {
                    return defaultRelative;
                }
            }

            if (!string.IsNullOrWhiteSpace(generated.SourcePath))
            {
                string normalizedSourcePath = NormalizePathForCompare(generated.SourcePath);
                if (normalizedSourcePath.StartsWith(characterRoot, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(normalizedSourcePath))
                {
                    string rel = NormalizeRelativePath(Path.GetRelativePath(characterRoot, normalizedSourcePath), isFolder: false);
                    return rel;
                }
            }

            string fileName = Path.GetFileName(defaultRelative);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = generated.Name;
            }

            if (!existingFilesByName.TryGetValue(fileName, out List<string>? candidates) || candidates.Count == 0)
            {
                return null;
            }

            List<string> nonConflicting = candidates
                .Where(path => !generatedFilePaths.Contains(path)
                    || string.Equals(path, defaultRelative, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (nonConflicting.Count == 1)
            {
                return nonConflicting[0];
            }

            return candidates.FirstOrDefault();
        }

        private static int? ResolveDurationFromIni(IniDocument iniDocument, string section, string key)
        {
            string trimmedKey = (key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedKey) || string.Equals(trimmedKey, "-", StringComparison.Ordinal))
            {
                return null;
            }

            if (!iniDocument.TryGetLatestValue(section, trimmedKey, out string? value))
            {
                return null;
            }

            if (!int.TryParse(value, out int parsed))
            {
                return null;
            }

            return Math.Max(0, parsed);
        }

        private static Dictionary<int, string> ResolveBlipsOverrides(IniDocument iniDocument)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();
            Dictionary<int, string> optionProfiles = new Dictionary<int, string>();
            foreach (string section in iniDocument.Sections.Keys)
            {
                if (!section.StartsWith("options", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(section, "options", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(section, "optionsn", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string suffix = section.Substring("options".Length).Trim();
                if (!int.TryParse(suffix, out int profileIndex))
                {
                    continue;
                }

                if (iniDocument.TryGetLatestValue(section, "blips", out string? blipsValue))
                {
                    optionProfiles[profileIndex] = blipsValue?.Trim() ?? string.Empty;
                }
            }

            foreach (IniEntry entry in iniDocument.GetEntries("OptionsN"))
            {
                if (!int.TryParse(entry.Key, out int emoteId))
                {
                    continue;
                }

                if (!int.TryParse(entry.Value, out int profileId))
                {
                    continue;
                }

                if (optionProfiles.TryGetValue(profileId, out string? blips))
                {
                    result[emoteId] = blips;
                }
            }

            return result;
        }

        private IEnumerable<FrameEventViewModel> ResolveFrameEventsForEmote(
            IniDocument iniDocument,
            CharacterCreationEmoteViewModel emote)
        {
            List<FrameEventViewModel> result = new List<FrameEventViewModel>();
            string preAnimation = (emote.PreAnimation ?? string.Empty).Trim();
            string animation = (emote.Animation ?? string.Empty).Trim();
            string[] sectionPrefixes =
            {
                preAnimation,
                "(a)/" + animation,
                "(b)/" + animation
            };

            foreach ((CharacterFrameEventType eventType, string suffix) in new[]
            {
                (CharacterFrameEventType.Sfx, "_FrameSFX"),
                (CharacterFrameEventType.Screenshake, "_FrameScreenshake"),
                (CharacterFrameEventType.Realization, "_FrameRealization")
            })
            {
                foreach (string prefix in sectionPrefixes.Where(static value => !string.IsNullOrWhiteSpace(value)))
                {
                    string section = prefix + suffix;
                    foreach (IniEntry entry in iniDocument.GetEntries(section))
                    {
                        if (!int.TryParse(entry.Key, out int frame))
                        {
                            continue;
                        }

                        CharacterFrameTarget target = string.Equals(prefix, preAnimation, StringComparison.OrdinalIgnoreCase)
                            ? CharacterFrameTarget.PreAnimation
                            : (prefix.StartsWith("(a)/", StringComparison.OrdinalIgnoreCase)
                                ? CharacterFrameTarget.AnimationA
                                : CharacterFrameTarget.AnimationB);

                        result.Add(new FrameEventViewModel
                        {
                            Target = target,
                            EventType = eventType,
                            Frame = Math.Max(1, frame),
                            Value = entry.Value,
                            CustomTargetPath = string.Empty
                        });
                    }
                }
            }

            return result;
        }

        private static string? ResolveAssetByBaseName(string characterDirectoryPath, string baseName, bool isAudio)
        {
            string[] allowedExtensions = isAudio
                ? new[] { ".opus", ".ogg", ".mp3", ".wav" }
                : new[] { ".webp", ".apng", ".gif", ".png", ".jpg", ".jpeg", ".bmp" };

            IEnumerable<string> files = Directory.EnumerateFiles(characterDirectoryPath, baseName + ".*", SearchOption.AllDirectories)
                .Where(path => allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
            return files
                .OrderBy(static path => path.Count(static c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .FirstOrDefault();
        }

        private static string? ResolveImageTokenPathWithinCharacterDirectory(string characterDirectoryPath, string token)
            => ResolveTokenPathWithinCharacterDirectory(characterDirectoryPath, token, isAudio: false);

        private static string? ResolveAudioTokenPathWithinCharacterDirectory(string characterDirectoryPath, string token)
            => ResolveTokenPathWithinCharacterDirectory(characterDirectoryPath, token, isAudio: true);

        private static string? ResolveAnimationTokenPathWithinCharacterDirectory(string characterDirectoryPath, string token)
        {
            string normalized = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "-", StringComparison.Ordinal))
            {
                return null;
            }

            string resolved = CharacterAssetPathResolver.ResolveCharacterAnimationPath(
                characterDirectoryPath,
                normalized,
                includePlaceholder: false);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            return ResolveImageTokenPathWithinCharacterDirectory(characterDirectoryPath, normalized);
        }

        private static string? ResolveTokenPathWithinCharacterDirectory(string characterDirectoryPath, string token, bool isAudio)
        {
            string trimmedToken = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedToken) || string.Equals(trimmedToken, "1", StringComparison.Ordinal))
            {
                return null;
            }

            if (Path.IsPathRooted(trimmedToken))
            {
                return File.Exists(trimmedToken) ? trimmedToken : null;
            }

            string normalizedToken = trimmedToken.Replace('\\', '/');
            string relativeToken = normalizedToken;
            const string charactersPrefix = "../../characters/";
            if (relativeToken.StartsWith(charactersPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int nextSlash = relativeToken.IndexOf('/', charactersPrefix.Length);
                if (nextSlash >= 0 && nextSlash + 1 < relativeToken.Length)
                {
                    relativeToken = relativeToken[(nextSlash + 1)..];
                }
            }

            string[] extensions = isAudio
                ? new[] { ".opus", ".ogg", ".mp3", ".wav" }
                : new[] { ".png", ".gif", ".webp", ".apng", ".jpg", ".jpeg", ".bmp" };

            IEnumerable<string> roots = new[]
            {
                characterDirectoryPath,
                Path.Combine(characterDirectoryPath, "Images"),
                Path.Combine(characterDirectoryPath, "Sounds"),
                Path.Combine(characterDirectoryPath, "emotions")
            };

            foreach (string root in roots)
            {
                string candidateBase = Path.Combine(root, relativeToken.Replace('/', Path.DirectorySeparatorChar));
                string? resolved = ResolvePathWithExtensions(candidateBase, extensions);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            string absoluteCandidate = Path.GetFullPath(
                Path.Combine(characterDirectoryPath, normalizedToken.Replace('/', Path.DirectorySeparatorChar)));
            return ResolvePathWithExtensions(absoluteCandidate, extensions);
        }

        private static string? ResolvePathWithExtensions(string pathWithoutAssumedExtension, IReadOnlyList<string> extensions)
        {
            if (File.Exists(pathWithoutAssumedExtension))
            {
                return pathWithoutAssumedExtension;
            }

            if (!string.IsNullOrWhiteSpace(Path.GetExtension(pathWithoutAssumedExtension)))
            {
                return null;
            }

            foreach (string extension in extensions)
            {
                string candidate = pathWithoutAssumedExtension + extension;
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ApplyEditModeUiState()
        {
            Title = "AO Character File Creator (Edit Mode)";
            if (GenerateButton != null)
            {
                GenerateButton.Content = "Edit Character Folder";
            }

            if (MountPathResolvedTextBlock != null)
            {
                MountPathResolvedTextBlock.Text = "Loaded existing character folder for editing.";
            }

            UpdateFolderAvailabilityStatus();
        }

        private void ApplyCreateModeUiState()
        {
            Title = "AO Character File Creator";
            if (GenerateButton != null)
            {
                GenerateButton.Content = "Generate Character Folder";
            }

            string mountPath = ResolveSelectedMountPathForCreation();
            if (MountPathResolvedTextBlock != null)
            {
                MountPathResolvedTextBlock.Text = string.IsNullOrWhiteSpace(mountPath)
                    ? "Select where the new character folder should be created."
                    : "The character folder will be created in: " + BuildCharactersDirectoryDisplayPath(mountPath);
            }
        }

        private static string BuildDuplicateFolderName(string sourceFolderName, string mountPath)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceFolderName)
                ? "new_character"
                : sourceFolderName.Trim();
            string candidate = baseName + " - Copy";
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                return candidate;
            }

            string charactersDirectory = Path.Combine(mountPath, "characters");
            string candidatePath = Path.Combine(charactersDirectory, candidate);
            if (!Directory.Exists(candidatePath))
            {
                return candidate;
            }

            int suffix = 2;
            while (true)
            {
                string indexedCandidate = $"{baseName} - Copy {suffix}";
                string indexedPath = Path.Combine(charactersDirectory, indexedCandidate);
                if (!Directory.Exists(indexedPath))
                {
                    return indexedCandidate;
                }

                suffix++;
            }
        }

        private void UpdateFolderAvailabilityStatus()
        {
            if (CharacterFolderAvailabilityTextBlock == null)
            {
                return;
            }

            string folderName = (CharacterFolderNameTextBox.Text ?? string.Empty).Trim();
            string mountPath = ResolveSelectedMountPathForCreation();
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(mountPath))
            {
                CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(181, 208, 232));
                CharacterFolderAvailabilityTextBlock.Text = isEditMode
                    ? "Choose target folder name and mount path for the edited character."
                    : "Enter a folder name to validate availability.";
                return;
            }

            if (folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 148, 148));
                CharacterFolderAvailabilityTextBlock.Text =
                    $"Folder \"{folderName}\" contains invalid characters and cannot be created.";
                return;
            }

            string fullPath = Path.Combine(mountPath, "characters", folderName);
            if (isEditMode)
            {
                string originalPath = originalEditCharacterDirectoryPath?.Trim() ?? string.Empty;
                bool pointsToOriginal = !string.IsNullOrWhiteSpace(originalPath)
                    && string.Equals(
                        NormalizePathForCompare(fullPath),
                        NormalizePathForCompare(originalPath),
                        StringComparison.OrdinalIgnoreCase);
                if (pointsToOriginal)
                {
                    CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(154, 224, 170));
                    CharacterFolderAvailabilityTextBlock.Text =
                        $"Folder \"{originalEditCharacterFolderName}\" will be rebuilt in-place when you click Edit Character Folder.";
                    return;
                }

                if (Directory.Exists(fullPath))
                {
                    CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 148, 148));
                    CharacterFolderAvailabilityTextBlock.Text =
                        $"Target folder \"{folderName}\" already exists. Choose a different destination to avoid overwrite.";
                    return;
                }

                CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(154, 224, 170));
                CharacterFolderAvailabilityTextBlock.Text =
                    $"The folder \"{originalEditCharacterFolderName}\" will be renamed/moved to \"{folderName}\" when you click Edit Character Folder.";
                return;
            }

            if (Directory.Exists(fullPath))
            {
                CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(244, 148, 148));
                CharacterFolderAvailabilityTextBlock.Text =
                    $"Folder \"{folderName}\" already exists, change the name or delete the original folder.";
            }
            else
            {
                CharacterFolderAvailabilityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(154, 224, 170));
                CharacterFolderAvailabilityTextBlock.Text = $"Folder \"{folderName}\" is an available folder name.";
            }
        }

        private static string NormalizePathForCompare(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private void ClearStagedTextAssetEdits()
        {
            foreach ((_, string stagedPath) in stagedTextAssetSourcePathsByRelativePath)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(stagedPath) && File.Exists(stagedPath))
                    {
                        File.Delete(stagedPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }

            stagedTextAssetSourcePathsByRelativePath.Clear();
        }

        private void StageTextOrganizationEntryContent(FileOrganizationEntryViewModel entry, string content)
        {
            if (entry == null || entry.IsFolder)
            {
                return;
            }

            string normalizedRelativePath = NormalizeRelativePath(entry.RelativePath, isFolder: false);
            if (string.IsNullOrWhiteSpace(normalizedRelativePath))
            {
                return;
            }

            string extension = Path.GetExtension(normalizedRelativePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".txt";
            }

            string stagedPath = Path.Combine(Path.GetTempPath(), $"oceanya_staged_text_{Guid.NewGuid():N}{extension}");
            File.WriteAllText(stagedPath, content);

            if (stagedTextAssetSourcePathsByRelativePath.TryGetValue(normalizedRelativePath, out string? previousPath)
                && !string.IsNullOrWhiteSpace(previousPath)
                && !string.Equals(previousPath, stagedPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(previousPath))
                    {
                        File.Delete(previousPath);
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }

            stagedTextAssetSourcePathsByRelativePath[normalizedRelativePath] = stagedPath;
            entry.SourcePath = stagedPath;
            foreach (ExternalOrganizationEntry externalEntry in externalOrganizationEntries)
            {
                if (!externalEntry.IsFolder
                    && string.Equals(
                        NormalizeRelativePath(externalEntry.RelativePath, isFolder: false),
                        normalizedRelativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    externalEntry.SourcePath = stagedPath;
                }
            }

            WriteFileOrganizationDebugLog($"StageTextOrganizationEntryContent relative={normalizedRelativePath} staged={stagedPath}");
        }

        private void RemoveStagedTextEditForRelativePath(string relativePath)
        {
            string normalizedRelativePath = NormalizeRelativePath(relativePath, isFolder: false);
            if (!stagedTextAssetSourcePathsByRelativePath.TryGetValue(normalizedRelativePath, out string? stagedPath))
            {
                return;
            }

            stagedTextAssetSourcePathsByRelativePath.Remove(normalizedRelativePath);
            try
            {
                if (!string.IsNullOrWhiteSpace(stagedPath) && File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private void MoveStagedTextEditRelativePath(string oldRelativePath, string newRelativePath, bool isFolder)
        {
            string oldNormalized = NormalizeRelativePath(oldRelativePath, isFolder);
            string newNormalized = NormalizeRelativePath(newRelativePath, isFolder);
            if (string.IsNullOrWhiteSpace(oldNormalized)
                || string.IsNullOrWhiteSpace(newNormalized)
                || string.Equals(oldNormalized, newNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!isFolder)
            {
                if (stagedTextAssetSourcePathsByRelativePath.TryGetValue(oldNormalized, out string? stagedPath))
                {
                    stagedTextAssetSourcePathsByRelativePath.Remove(oldNormalized);
                    stagedTextAssetSourcePathsByRelativePath[newNormalized] = stagedPath;
                }

                return;
            }

            Dictionary<string, string> movedEntries = stagedTextAssetSourcePathsByRelativePath
                .Where(pair => pair.Key.StartsWith(oldNormalized, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            foreach ((string sourceKey, string stagedPath) in movedEntries)
            {
                stagedTextAssetSourcePathsByRelativePath.Remove(sourceKey);
                string suffix = sourceKey.Substring(oldNormalized.Length);
                stagedTextAssetSourcePathsByRelativePath[newNormalized + suffix] = stagedPath;
            }
        }

        private void ApplyStagedTextSourcePathOverride(FileOrganizationEntryViewModel entry)
        {
            if (entry == null || entry.IsFolder || !IsTextOrganizationEntry(entry))
            {
                return;
            }

            string normalizedRelativePath = NormalizeRelativePath(entry.RelativePath, isFolder: false);
            if (stagedTextAssetSourcePathsByRelativePath.TryGetValue(normalizedRelativePath, out string? stagedPath)
                && !string.IsNullOrWhiteSpace(stagedPath)
                && File.Exists(stagedPath))
            {
                entry.SourcePath = stagedPath;
            }
        }

        private static bool IsStandardAssetRootFolder(string relativeFolderPath)
        {
            string normalized = NormalizeRelativePath(relativeFolderPath, isFolder: true);
            foreach (string standardFolder in StandardAssetRootFolders)
            {
                if (string.Equals(normalized, NormalizeRelativePath(standardFolder, isFolder: true), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DirectoryContainsAnyFiles(string folderPath)
        {
            return Directory.Exists(folderPath)
                && Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).Any();
        }

        private static bool IsPhantomStandardAssetFolder(string sourceRoot, string relativeFolderPath)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || !IsStandardAssetRootFolder(relativeFolderPath))
            {
                return false;
            }

            string normalizedFolder = NormalizeRelativePath(relativeFolderPath, isFolder: true);
            string absoluteFolder = Path.Combine(sourceRoot, normalizedFolder.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteFolder))
            {
                return true;
            }

            return !DirectoryContainsAnyFiles(absoluteFolder);
        }

        private static string GetFileOrganizationDebugLogPath()
        {
            return Path.Combine(Path.GetTempPath(), FileOrganizationDebugLogName);
        }

        private static void WriteFileOrganizationDebugLog(string message)
        {
            try
            {
                string path = GetFileOrganizationDebugLogPath();
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (FileOrganizationDebugLogSync)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
                // Debug logging must never affect editor behavior.
            }
        }

        private static string FormatEntryForDebug(FileOrganizationEntryViewModel entry)
        {
            if (entry == null)
            {
                return "<null>";
            }

            string source = string.IsNullOrWhiteSpace(entry.SourcePath) ? "-" : entry.SourcePath;
            string key = string.IsNullOrWhiteSpace(entry.AssetKey) ? "-" : entry.AssetKey;
            return $"key={key} path={entry.RelativePath} default={entry.DefaultRelativePath} folder={entry.IsFolder} locked={entry.IsLocked} external={entry.IsExternal} source={source}";
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
            CommitAnyFileOrganizationRename();
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
            FileOrganizationSectionPanel.Visibility = string.Equals(section, "fileorganization", StringComparison.OrdinalIgnoreCase)
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
            else if (string.Equals(section, "fileorganization", StringComparison.OrdinalIgnoreCase))
            {
                currentFileOrganizationPath = string.Empty;
                if (allFileOrganizationEntries.Count == 0)
                {
                    RefreshFileOrganizationEntries();
                }
                else
                {
                    RefreshFileOrganizationCurrentFolderView();
                }
                StatusTextBlock.Text = "Organize the final generated folder structure.";
            }
            else
            {
                StatusTextBlock.Text = isEditMode
                    ? "Configure the character and apply edits to the loaded AO folder."
                    : "Configure the character and generate a new AO folder.";
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
            ApplySectionButtonStyle(
                SectionFileOrganizationButton,
                string.Equals(activeSection, "fileorganization", StringComparison.OrdinalIgnoreCase));
            UpdateSectionButtonNumbers();
        }

        private void UpdateSectionButtonNumbers()
        {
            bool hasButtonIcons = SectionButtonIconsButton.Visibility == Visibility.Visible;
            SectionSetupButton.Content = "1. Initial Character Setup";
            SectionEffectsButton.Content = "2. Effects";
            SectionEmotesButton.Content = "3. Emotes";
            SectionButtonIconsButton.Content = "4. Button Icons";
            SectionFileOrganizationButton.Content = hasButtonIcons ? "5. File Organization" : "4. File Organization";
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

        private static bool HasAnyCuttableAsset(CharacterCreationEmoteViewModel emote)
        {
            return !string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath)
                || !string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath);
        }

        private void UpdateCharacterIconFromEmoteButtonVisibility()
        {
            if (CharacterIconFromEmoteButton == null)
            {
                return;
            }

            bool hasEligible = emotes.Any(static emote => HasAnyCuttableAsset(emote));
            CharacterIconFromEmoteButton.Visibility = hasEligible ? Visibility.Visible : Visibility.Collapsed;
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

            UpdateSectionButtonNumbers();
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
                return;
            }

            if (string.Equals(activeSection, "fileorganization", StringComparison.OrdinalIgnoreCase))
            {
                ResetFileOrganizationToDefaults();
                StatusTextBlock.Text = "File Organization tab reset to defaults.";
                return;
            }

        }

        private void DefaultAllButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSetupToDefaults();
            ResetEffectsToDefaults();
            ResetEmotesToDefaults();
            ResetBulkButtonIconsConfigToDefaults();
            ClearStagedTextAssetEdits();
            externalOrganizationEntries.Clear();
            generatedOrganizationOverrides.Clear();
            suppressedGeneratedAssetKeys.Clear();
            currentFileOrganizationPath = string.Empty;
            loadedSourceCharacterDirectoryPath = string.Empty;
            ResetFrameAndAdvancedToDefaults();
            StatusTextBlock.Text = "All tabs reset to defaults.";
        }

        private void ResetSetupToDefaults()
        {
            CharacterFolderNameTextBox.Text = "new_character";
            ShowNameTextBox.Text = "New Character";
            selectedCharacterIconSourcePath = string.Empty;
            generatedCharacterIconImage = null;
            CharacterIconPreviewImage.Source = null;
            CharacterIconEmptyText.Visibility = Visibility.Visible;
            SideComboBox.Text = "wit";
            SetBlipText(blipOptions.FirstOrDefault() ?? string.Empty);
            UpdateCharacterIconFromEmoteButtonVisibility();
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

            BitmapSource? carryForwardCutout = null;
            Rect? carryForwardSelection = null;
            foreach (CharacterCreationEmoteViewModel emote in targets)
            {
                bulkButtonCutoutByEmoteId.TryGetValue(emote.Id, out BitmapSource? existingCutout);
                existingCutout ??= carryForwardCutout;
                BitmapSource? cutout = ShowEmoteCuttingDialog(emote, existingCutout, carryForwardSelection);
                if (cutout == null)
                {
                    continue;
                }

                bulkButtonCutoutByEmoteId[emote.Id] = cutout;
                carryForwardCutout = cutout;
                if (TryGetSavedCutSelectionForEmote(emote, out CutSelectionState? state))
                {
                    carryForwardSelection = state.NormalizedSelection;
                }
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
                    ToolTip = emote.EmoteHeader + " - " + emote.Name,
                    Cursor = Cursors.Hand
                };
                tile.Child = new Image
                {
                    Source = cutout,
                    Stretch = Stretch.Uniform
                };
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    SelectEmoteTile(emote);
                    BitmapSource? updated = ShowEmoteCuttingDialog(emote, cutout);
                    if (updated == null)
                    {
                        return;
                    }

                    bulkButtonCutoutByEmoteId[emote.Id] = updated;
                    RenderBulkCutoutPreviewTiles(targets);
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

        private void RefreshFileOrganizationEntries()
        {
            WriteFileOrganizationDebugLog(
                $"RefreshFileOrganizationEntries start external={externalOrganizationEntries.Count} overrides={generatedOrganizationOverrides.Count} suppressed={suppressedGeneratedAssetKeys.Count}");
            StopFileOrganizationAudioPreview();
            ClearFileOrganizationPreviewPlayers();
            allFileOrganizationEntries.Clear();
            allFileOrganizationEntries.AddRange(BuildGeneratedFileOrganizationEntries());

            foreach (ExternalOrganizationEntry external in externalOrganizationEntries)
            {
                FileOrganizationEntryViewModel externalEntry = new FileOrganizationEntryViewModel
                {
                    Name = Path.GetFileName(external.RelativePath.TrimEnd('/', '\\')),
                    RelativePath = external.RelativePath,
                    TypeText = external.IsFolder ? "Folder" : "File",
                    StatusText = "UNUSED USER FILE",
                    IsLocked = false,
                    IsExternal = true,
                    SourcePath = external.SourcePath,
                    IsFolder = external.IsFolder,
                    DefaultRelativePath = external.RelativePath
                };
                InitializeFileOrganizationEntryVisual(externalEntry);
                ApplyStagedTextSourcePathOverride(externalEntry);
                allFileOrganizationEntries.Add(externalEntry);
            }

            EnsureParentFolderEntries(allFileOrganizationEntries);
            PrunePhantomStandardAssetEntries(allFileOrganizationEntries);
            UpdateUnusedFlags(allFileOrganizationEntries);
            if (!string.IsNullOrWhiteSpace(currentFileOrganizationPath)
                && !allFileOrganizationEntries.Any(entry => entry.IsFolder
                    && string.Equals(entry.RelativePath, currentFileOrganizationPath, StringComparison.OrdinalIgnoreCase)))
            {
                currentFileOrganizationPath = string.Empty;
            }

            List<string> topLevelFolders = allFileOrganizationEntries
                .Where(static entry => entry.IsFolder)
                .Select(static entry => NormalizeRelativePath(entry.RelativePath, isFolder: true))
                .Where(path => string.IsNullOrWhiteSpace(GetParentDirectoryPath(path, isFolder: true)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            WriteFileOrganizationDebugLog(
                $"RefreshFileOrganizationEntries end total={allFileOrganizationEntries.Count} topFolders=[{string.Join(", ", topLevelFolders)}]");

            RefreshFileOrganizationCurrentFolderView();
        }

        private void PrunePhantomStandardAssetEntries(List<FileOrganizationEntryViewModel> entries)
        {
            if (!hasLoadedExistingFileOrganizationState || entries.Count == 0)
            {
                return;
            }

            string sourceRoot = !string.IsNullOrWhiteSpace(originalEditCharacterDirectoryPath)
                ? originalEditCharacterDirectoryPath
                : loadedSourceCharacterDirectoryPath;
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                return;
            }

            foreach (string standardFolder in StandardAssetRootFolders)
            {
                string normalizedFolder = NormalizeRelativePath(standardFolder, isFolder: true);
                string absoluteFolder = Path.Combine(sourceRoot, normalizedFolder.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));

                bool hasExternalFilesUnderFolder = entries.Any(entry =>
                    entry.IsExternal
                    && !entry.IsFolder
                    && NormalizeRelativePath(entry.RelativePath, entry.IsFolder).StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase));
                if (hasExternalFilesUnderFolder)
                {
                    WriteFileOrganizationDebugLog($"PrunePhantomStandardAssetEntries keep external-under-folder standard={normalizedFolder}");
                    continue;
                }

                bool folderContainsFiles = DirectoryContainsAnyFiles(absoluteFolder);
                if (folderContainsFiles)
                {
                    WriteFileOrganizationDebugLog(
                        $"PrunePhantomStandardAssetEntries keep folder-has-files standard={normalizedFolder} absolute={absoluteFolder}");
                    continue;
                }

                int removed = entries.RemoveAll(entry =>
                {
                    string normalizedPath = NormalizeRelativePath(entry.RelativePath, entry.IsFolder);
                    return normalizedPath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
                });
                List<ExternalOrganizationEntry> externalToRemove = externalOrganizationEntries
                    .Where(entry => NormalizeRelativePath(entry.RelativePath, entry.IsFolder)
                        .StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                int externalRemoved = externalToRemove.Count;
                foreach (ExternalOrganizationEntry removable in externalToRemove)
                {
                    _ = externalOrganizationEntries.Remove(removable);
                }
                WriteFileOrganizationDebugLog(
                    $"PrunePhantomStandardAssetEntries removed={removed} externalRemoved={externalRemoved} standard={normalizedFolder} absoluteExists={Directory.Exists(absoluteFolder)} folderHasFiles={folderContainsFiles}");
            }
        }

        private List<FileOrganizationEntryViewModel> BuildGeneratedFileOrganizationEntries()
        {
            List<FileOrganizationEntryViewModel> result = new List<FileOrganizationEntryViewModel>();
            AddGeneratedOrganizationFile(result, "charini", "char.ini", sourcePath: null, locked: true);

            if (generatedCharacterIconImage != null)
            {
                AddGeneratedOrganizationFile(result, "charicon", "char_icon.png", sourcePath: null, locked: true);
            }
            else if (!string.IsNullOrWhiteSpace(selectedCharacterIconSourcePath))
            {
                AddGeneratedOrganizationFile(
                    result,
                    "charicon",
                    "char_icon" + Path.GetExtension(selectedCharacterIconSourcePath),
                    selectedCharacterIconSourcePath,
                    locked: true);
            }

            foreach (CharacterCreationEmoteViewModel emote in emotes)
            {
                if (!HasAnyRealAssetSet(emote))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"emote:{emote.Index}:preanim",
                        "Images/" + Path.GetFileName(emote.PreAnimationAssetSourcePath),
                        emote.PreAnimationAssetSourcePath,
                        locked: false);
                }

                if (!string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"emote:{emote.Index}:anim",
                        "Images/" + Path.GetFileName(emote.AnimationAssetSourcePath),
                        emote.AnimationAssetSourcePath,
                        locked: false);
                }

                if (!string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"emote:{emote.Index}:idle",
                        "Images/(a)/" + Path.GetFileName(emote.FinalAnimationIdleAssetSourcePath),
                        emote.FinalAnimationIdleAssetSourcePath,
                        locked: false);
                }

                if (!string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"emote:{emote.Index}:talking",
                        "Images/(b)/" + Path.GetFileName(emote.FinalAnimationTalkingAssetSourcePath),
                        emote.FinalAnimationTalkingAssetSourcePath,
                        locked: false);
                }

                if (!string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath)
                    && (!string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath)
                        || !string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath)))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"emote:{emote.Index}:splitbase",
                        "Images/" + Path.GetFileName(emote.AnimationAssetSourcePath),
                        emote.AnimationAssetSourcePath,
                        locked: false);
                }

                if (!string.IsNullOrWhiteSpace(emote.SfxAssetSourcePath))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"emote:{emote.Index}:sfx",
                        "Sounds/" + Path.GetFileName(emote.SfxAssetSourcePath),
                        emote.SfxAssetSourcePath,
                        locked: false);
                }

                int emoteId = Math.Max(1, emote.Index);
                ButtonIconGenerationConfig buttonConfig = BuildButtonIconGenerationConfig(emote);
                if (TryBuildButtonIconPair(buttonConfig, out _, out _, out _))
                {
                    AddGeneratedOrganizationFile(
                        result,
                        $"button:{emoteId}:on",
                        $"emotions/button{emoteId}_on.png",
                        sourcePath: null,
                        locked: true);
                    AddGeneratedOrganizationFile(
                        result,
                        $"button:{emoteId}:off",
                        $"emotions/button{emoteId}_off.png",
                        sourcePath: null,
                        locked: true);
                }
            }

            AddShoutOrganizationEntries(result);
            AddOptionalGeneratedOrganizationEntries(result);

            List<FileOrganizationEntryViewModel> finalEntries = result
                .Where(entry => string.IsNullOrWhiteSpace(entry.AssetKey)
                    || !suppressedGeneratedAssetKeys.Contains(entry.AssetKey))
                .GroupBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToList();

            WriteFileOrganizationDebugLog(
                $"BuildGeneratedFileOrganizationEntries result={finalEntries.Count} raw={result.Count} suppressedKeys={suppressedGeneratedAssetKeys.Count}");
            foreach (FileOrganizationEntryViewModel entry in finalEntries)
            {
                WriteFileOrganizationDebugLog("GeneratedEntry " + FormatEntryForDebug(entry));
            }

            return finalEntries;
        }

        private void AddGeneratedOrganizationFile(
            List<FileOrganizationEntryViewModel> entries,
            string assetKey,
            string defaultRelativePath,
            string? sourcePath,
            bool locked)
        {
            string effectiveDefaultRelativePath = ResolveLoadedCharacterRelativePathOrDefault(defaultRelativePath, sourcePath);
            string resolved = ResolveOutputPathForAsset(assetKey, effectiveDefaultRelativePath, isFolder: false);
            FileOrganizationEntryViewModel entry = new FileOrganizationEntryViewModel
            {
                Name = Path.GetFileName(resolved),
                RelativePath = resolved,
                DefaultRelativePath = NormalizeRelativePath(effectiveDefaultRelativePath, isFolder: false),
                TypeText = "File",
                StatusText = locked ? "Generated (locked)" : "Generated",
                IsLocked = locked,
                IsExternal = false,
                IsFolder = false,
                SourcePath = sourcePath,
                AssetKey = assetKey
            };
            InitializeFileOrganizationEntryVisual(entry);
            ApplyStagedTextSourcePathOverride(entry);
            entries.Add(entry);
        }

        private string ResolveLoadedCharacterRelativePathOrDefault(string defaultRelativePath, string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return defaultRelativePath;
            }

            string sourceCharacterRoot = !string.IsNullOrWhiteSpace(originalEditCharacterDirectoryPath)
                ? originalEditCharacterDirectoryPath
                : loadedSourceCharacterDirectoryPath;
            if (string.IsNullOrWhiteSpace(sourceCharacterRoot))
            {
                return defaultRelativePath;
            }

            string normalizedRoot = NormalizePathForCompare(sourceCharacterRoot);
            string normalizedSourcePath = NormalizePathForCompare(sourcePath);
            if (string.IsNullOrWhiteSpace(normalizedRoot)
                || string.IsNullOrWhiteSpace(normalizedSourcePath)
                || !File.Exists(normalizedSourcePath)
                || !IsPathInsideRoot(normalizedRoot, normalizedSourcePath))
            {
                return defaultRelativePath;
            }

            string relativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, normalizedSourcePath), isFolder: false);
            return string.IsNullOrWhiteSpace(relativePath) ? defaultRelativePath : relativePath;
        }

        private static bool IsPathInsideRoot(string rootPath, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            string normalizedRoot = NormalizePathForCompare(rootPath);
            string normalizedCandidate = NormalizePathForCompare(candidatePath);
            if (string.IsNullOrWhiteSpace(normalizedRoot) || string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return false;
            }

            if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private void AddGeneratedOrganizationFolder(List<FileOrganizationEntryViewModel> entries, string assetKey, string defaultRelativePath, bool locked)
        {
            string resolved = ResolveOutputPathForAsset(assetKey, defaultRelativePath, isFolder: true);
            FileOrganizationEntryViewModel entry = new FileOrganizationEntryViewModel
            {
                Name = Path.GetFileName(resolved.TrimEnd('/')),
                RelativePath = resolved,
                DefaultRelativePath = NormalizeRelativePath(defaultRelativePath, isFolder: true),
                TypeText = "Folder",
                StatusText = locked ? "Generated (locked)" : "Generated",
                IsLocked = locked,
                IsExternal = false,
                IsFolder = true,
                AssetKey = assetKey
            };
            InitializeFileOrganizationEntryVisual(entry);
            entries.Add(entry);
        }

        private void AddShoutOrganizationEntries(List<FileOrganizationEntryViewModel> entries)
        {
            AddShoutOrganizationEntry(entries, "holdit", "holdit_bubble", isVisual: true);
            AddShoutOrganizationEntry(entries, "objection", "objection_bubble", isVisual: true);
            AddShoutOrganizationEntry(entries, "takethat", "takethat_bubble", isVisual: true);
            AddShoutOrganizationEntry(entries, "custom", "custom", isVisual: true);
            AddShoutOrganizationEntry(entries, "holdit", "holdit", isVisual: false);
            AddShoutOrganizationEntry(entries, "objection", "objection", isVisual: false);
            AddShoutOrganizationEntry(entries, "takethat", "takethat", isVisual: false);
            AddShoutOrganizationEntry(entries, "custom", "custom", isVisual: false);
        }

        private void AddShoutOrganizationEntry(List<FileOrganizationEntryViewModel> entries, string key, string targetBaseName, bool isVisual)
        {
            Dictionary<string, string> sourceMap = isVisual ? selectedShoutVisualSourcePaths : selectedShoutSfxSourcePaths;
            if (!sourceMap.TryGetValue(key, out string? sourcePath)
                || string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            string extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return;
            }

            string assetKey = isVisual ? $"shout:visual:{key}" : $"shout:sfx:{key}";
            AddGeneratedOrganizationFile(entries, assetKey, targetBaseName + extension, sourcePath, locked: true);
        }

        private void AddOptionalGeneratedOrganizationEntries(List<FileOrganizationEntryViewModel> entries)
        {
            bool usingCustomBlipFile = !string.IsNullOrWhiteSpace(selectedCustomBlipSourcePath)
                && !string.IsNullOrWhiteSpace(customBlipOptionText)
                && string.Equals((GenderBlipsDropdown.Text ?? string.Empty).Trim(), customBlipOptionText, StringComparison.OrdinalIgnoreCase);
            if (usingCustomBlipFile)
            {
                string extension = Path.GetExtension(selectedCustomBlipSourcePath);
                string fileName = Path.GetFileNameWithoutExtension(selectedCustomBlipSourcePath);
                AddGeneratedOrganizationFile(entries, "blip:custom", $"blips/{fileName}{extension}", selectedCustomBlipSourcePath, locked: false);
            }

            if (!string.IsNullOrWhiteSpace(selectedRealizationSourcePath)
                && string.Equals((RealizationTextBox.Text ?? string.Empty).Trim(), Path.GetFileName(selectedRealizationSourcePath), StringComparison.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(selectedRealizationSourcePath);
                AddGeneratedOrganizationFile(entries, "realization:custom", $"realization{extension}", selectedRealizationSourcePath, locked: true);
            }
        }

        private static string NormalizeRelativePath(string path, bool isFolder)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
            if (isFolder)
            {
                normalized = normalized.Trim('/');
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    normalized += "/";
                }
            }
            else
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized;
        }

        private static string RemoveExtensionFromRelativePath(string path)
        {
            string normalized = NormalizeRelativePath(path, isFolder: false);
            string extension = Path.GetExtension(normalized);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return normalized;
            }

            return normalized.Substring(0, normalized.Length - extension.Length);
        }

        private string ResolveOutputPathForAsset(string assetKey, string defaultRelativePath, bool isFolder)
        {
            if (generatedOrganizationOverrides.TryGetValue(assetKey, out string? overridePath)
                && !string.IsNullOrWhiteSpace(overridePath))
            {
                return NormalizeRelativePath(overridePath, isFolder);
            }

            return NormalizeRelativePath(defaultRelativePath, isFolder);
        }

        private static string GetParentDirectoryPath(string relativePath, bool isFolder)
        {
            string normalized = NormalizeRelativePath(relativePath, isFolder);
            if (isFolder)
            {
                normalized = normalized.TrimEnd('/');
            }

            string? parent = Path.GetDirectoryName(normalized.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(parent))
            {
                return string.Empty;
            }

            return parent.Replace(Path.DirectorySeparatorChar, '/').Trim('/') + "/";
        }

        private void EnsureParentFolderEntries(List<FileOrganizationEntryViewModel> entries)
        {
            HashSet<string> existingFolders = entries
                .Where(static entry => entry.IsFolder)
                .Select(static entry => NormalizeRelativePath(entry.RelativePath, isFolder: true))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<FileOrganizationEntryViewModel> generatedFolders = new List<FileOrganizationEntryViewModel>();

            foreach (FileOrganizationEntryViewModel entry in entries.ToArray())
            {
                string parent = GetParentDirectoryPath(entry.RelativePath, entry.IsFolder);
                while (!string.IsNullOrWhiteSpace(parent))
                {
                    if (existingFolders.Add(parent))
                    {
                        FileOrganizationEntryViewModel folderEntry = new FileOrganizationEntryViewModel
                        {
                            Name = Path.GetFileName(parent.TrimEnd('/')),
                            RelativePath = parent,
                            DefaultRelativePath = parent,
                            TypeText = "Folder",
                            StatusText = "Generated",
                            IsLocked = string.Equals(parent, "emotions/", StringComparison.OrdinalIgnoreCase),
                            IsExternal = false,
                            IsFolder = true
                        };
                        InitializeFileOrganizationEntryVisual(folderEntry);
                        generatedFolders.Add(folderEntry);
                    }

                    parent = GetParentDirectoryPath(parent, isFolder: true);
                }
            }

            entries.AddRange(generatedFolders);
        }

        private void RefreshFileOrganizationCurrentFolderView()
        {
            string normalizedCurrent = NormalizeRelativePath(currentFileOrganizationPath, isFolder: true);
            currentFileOrganizationPath = normalizedCurrent;
            string mountPath = (MountPathComboBox.Text ?? string.Empty).Trim();
            string folderName = (CharacterFolderNameTextBox.Text ?? "new_character").Trim();
            string basePath;
            if (!string.IsNullOrWhiteSpace(mountPath))
            {
                basePath = Path.Combine(mountPath, "characters", folderName).Replace('\\', '/');
            }
            else
            {
                basePath = $"characters/{folderName}";
            }

            FileOrganizationPathTextBlock.Text = string.IsNullOrWhiteSpace(normalizedCurrent)
                ? basePath + "/"
                : basePath + "/" + normalizedCurrent;
            FileOrganizationGoUpButton.IsEnabled = !string.IsNullOrWhiteSpace(normalizedCurrent);

            fileOrganizationEntries.Clear();
            IEnumerable<FileOrganizationEntryViewModel> visibleEntries = allFileOrganizationEntries
                .Where(entry => string.Equals(
                    GetParentDirectoryPath(entry.RelativePath, entry.IsFolder),
                    normalizedCurrent,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(static entry => entry.IsFolder ? 0 : 1)
                .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);

            foreach (FileOrganizationEntryViewModel entry in visibleEntries)
            {
                fileOrganizationEntries.Add(entry);
            }
        }

        private void InitializeFileOrganizationEntryVisual(FileOrganizationEntryViewModel entry)
        {
            string displayPath = entry.SourcePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(displayPath) && !entry.IsFolder)
            {
                displayPath = entry.RelativePath;
            }

            if (entry.IsFolder)
            {
                entry.EntryKind = FileOrganizationEntryKind.Folder;
                entry.IconGlyph = "\uE8B7";
                entry.TypeDisplayName = "Folder";
                return;
            }

            string extension = Path.GetExtension(displayPath ?? string.Empty).ToLowerInvariant();
            if (IsImageExtension(extension))
            {
                entry.EntryKind = FileOrganizationEntryKind.Image;
                entry.TypeDisplayName = "Image asset";
                if (string.Equals(entry.AssetKey, "charicon", StringComparison.OrdinalIgnoreCase)
                    && generatedCharacterIconImage != null)
                {
                    entry.PreviewImage = generatedCharacterIconImage;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(entry.AssetKey)
                    && entry.AssetKey.StartsWith("button:", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = entry.AssetKey.Split(':');
                    if (parts.Length == 3
                        && int.TryParse(parts[1], out int emoteIndex)
                        && (string.Equals(parts[2], "on", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(parts[2], "off", StringComparison.OrdinalIgnoreCase)))
                    {
                        CharacterCreationEmoteViewModel? emote = emotes.FirstOrDefault(model => model.Index == emoteIndex);
                        if (emote != null)
                        {
                            ButtonIconGenerationConfig config = BuildButtonIconGenerationConfig(emote);
                            if (TryBuildButtonIconPair(config, out BitmapSource? onImage, out BitmapSource? offImage, out _))
                            {
                                entry.PreviewImage = string.Equals(parts[2], "off", StringComparison.OrdinalIgnoreCase)
                                    ? offImage
                                    : onImage;
                                if (entry.PreviewImage != null)
                                {
                                    return;
                                }
                            }
                        }
                    }
                }

                string? resolvedPath = ResolveAo2PreviewImagePath(entry.SourcePath);
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    entry.PreviewImage = Ao2AnimationPreview.LoadStaticPreviewImage(resolvedPath, decodePixelWidth: 120);
                }

                return;
            }

            if (IsAudioExtension(extension))
            {
                entry.EntryKind = FileOrganizationEntryKind.Audio;
                entry.IconGlyph = "\uE189";
                entry.TypeDisplayName = "Sound asset";
                return;
            }

            if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ini", StringComparison.OrdinalIgnoreCase))
            {
                entry.EntryKind = FileOrganizationEntryKind.Text;
                entry.IconGlyph = "\uE8A5";
                entry.TypeDisplayName = "Text asset";
                return;
            }

            entry.EntryKind = FileOrganizationEntryKind.Unknown;
            entry.IconGlyph = "\uE11B";
            entry.TypeDisplayName = "Unknown";
        }

        private static void UpdateUnusedFlags(IReadOnlyList<FileOrganizationEntryViewModel> entries)
        {
            foreach (FileOrganizationEntryViewModel entry in entries)
            {
                entry.IsUnused = entry.IsExternal;
            }

            List<FileOrganizationEntryViewModel> folders = entries
                .Where(static entry => entry.IsFolder)
                .OrderByDescending(static entry => entry.RelativePath.Length)
                .ToList();
            foreach (FileOrganizationEntryViewModel folder in folders)
            {
                string prefix = NormalizeRelativePath(folder.RelativePath, isFolder: true);
                List<FileOrganizationEntryViewModel> descendants = entries
                    .Where(entry => !ReferenceEquals(entry, folder)
                        && NormalizeRelativePath(entry.RelativePath, entry.IsFolder).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (descendants.Count == 0)
                {
                    folder.IsUnused = folder.IsExternal;
                }
                else
                {
                    folder.IsUnused = descendants.All(static descendant => descendant.IsUnused);
                }
            }
        }

        private static bool IsImageExtension(string extension)
        {
            return extension == ".png"
                || extension == ".jpg"
                || extension == ".jpeg"
                || extension == ".bmp"
                || extension == ".gif"
                || extension == ".webp"
                || extension == ".apng";
        }

        private static bool IsAudioExtension(string extension)
        {
            return extension == ".opus"
                || extension == ".ogg"
                || extension == ".mp3"
                || extension == ".wav";
        }

        private void ClearFileOrganizationPreviewPlayers()
        {
            foreach (IAnimationPlayer player in fileOrganizationPreviewPlayers.Values.ToArray())
            {
                try
                {
                    player.Stop();
                }
                catch
                {
                    // ignored
                }
            }

            fileOrganizationPreviewPlayers.Clear();
        }

        private void RefreshFileOrganizationButton_Click(object sender, RoutedEventArgs e)
        {
            if (externalOrganizationEntries.Count > 0
                || generatedOrganizationOverrides.Count > 0
                || suppressedGeneratedAssetKeys.Count > 0
                || stagedTextAssetSourcePathsByRelativePath.Count > 0)
            {
                MessageBoxResult decision = OceanyaMessageBox.Show(
                    this,
                    "Refreshing the file organization list will clear your current unused file/folder organization config. Continue?",
                    "Refresh File Organization",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (decision != MessageBoxResult.Yes)
                {
                    return;
                }

                externalOrganizationEntries.Clear();
                generatedOrganizationOverrides.Clear();
                suppressedGeneratedAssetKeys.Clear();
                ClearStagedTextAssetEdits();
                currentFileOrganizationPath = string.Empty;
            }

            RefreshFileOrganizationEntries();
        }

        private void ResetFileOrganizationButton_Click(object sender, RoutedEventArgs e)
        {
            ResetFileOrganizationToDefaults();
        }

        private void ResetFileOrganizationToDefaults()
        {
            if (externalOrganizationEntries.Count == 0
                && generatedOrganizationOverrides.Count == 0
                && suppressedGeneratedAssetKeys.Count == 0
                && stagedTextAssetSourcePathsByRelativePath.Count == 0)
            {
                currentFileOrganizationPath = string.Empty;
                RefreshFileOrganizationEntries();
                return;
            }

            string message = hasLoadedExistingFileOrganizationState && isEditMode
                ? "This will discard the folder layout loaded from the current character folder and reset organization to Oceanya Client defaults. Continue?"
                : "Reset file organization to defaults? This clears your folder structure edits and unused entries.";
            MessageBoxResult decision = OceanyaMessageBox.Show(
                this,
                message,
                "Reset File Organization",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
            {
                return;
            }

            externalOrganizationEntries.Clear();
            generatedOrganizationOverrides.Clear();
            suppressedGeneratedAssetKeys.Clear();
            ClearStagedTextAssetEdits();
            hasLoadedExistingFileOrganizationState = false;
            currentFileOrganizationPath = string.Empty;
            fileOrganizationClipboardEntries.Clear();
            fileOrganizationClipboardIsCut = false;
            RefreshFileOrganizationEntries();
        }

        private void CreateOrganizationFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string normalized = BuildUniqueExternalPath(currentFileOrganizationPath + "New Folder/", isFolder: true);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            externalOrganizationEntries.Add(new ExternalOrganizationEntry
            {
                RelativePath = normalized,
                IsFolder = true
            });
            RefreshFileOrganizationEntries();
            FileOrganizationEntryViewModel? created = fileOrganizationEntries.FirstOrDefault(entry =>
                entry.IsFolder
                && entry.IsExternal
                && string.Equals(
                    NormalizeRelativePath(entry.RelativePath, isFolder: true),
                    NormalizeRelativePath(normalized, isFolder: true),
                    StringComparison.OrdinalIgnoreCase));
            if (created != null)
            {
                BeginFileOrganizationRename(created);
            }
        }

        private void AddOrganizationFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog picker = new OpenFileDialog
            {
                Title = "Select unused file",
                Filter = "All files (*.*)|*.*"
            };
            if (picker.ShowDialog() != true)
            {
                return;
            }

            string defaultName = Path.GetFileName(picker.FileName);
            string? relativePath = InputDialog.Show("Add File", "File name:", defaultName);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            externalOrganizationEntries.Add(new ExternalOrganizationEntry
            {
                SourcePath = picker.FileName,
                RelativePath = NormalizeRelativePath(currentFileOrganizationPath + relativePath, isFolder: false),
                IsFolder = false
            });
            RefreshFileOrganizationEntries();
        }

        private void CreateReadmeFileButton_Click(object sender, RoutedEventArgs e)
        {
            string? content = ShowTextDocumentDialog(
                "Create Readme",
                "readme.txt",
                "Create a readme file in the current folder. This file is user-added and marked as unused.",
                string.Empty,
                isReadOnly: false);
            if (content == null)
            {
                return;
            }

            string fileName = BuildUniqueReadmeNameInCurrentFolder();
            string tempPath = Path.Combine(Path.GetTempPath(), $"oceanya_readme_{Guid.NewGuid():N}.txt");
            File.WriteAllText(tempPath, content);
            externalOrganizationEntries.Add(new ExternalOrganizationEntry
            {
                SourcePath = tempPath,
                RelativePath = NormalizeRelativePath(currentFileOrganizationPath + fileName, isFolder: false),
                IsFolder = false
            });
            RefreshFileOrganizationEntries();
        }

        private string BuildUniqueReadmeNameInCurrentFolder()
        {
            HashSet<string> occupied = allFileOrganizationEntries
                .Where(entry => string.Equals(
                    GetParentDirectoryPath(entry.RelativePath, entry.IsFolder),
                    NormalizeRelativePath(currentFileOrganizationPath, isFolder: true),
                    StringComparison.OrdinalIgnoreCase))
                .Select(entry => Path.GetFileName(entry.RelativePath.TrimEnd('/', '\\')))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!occupied.Contains("readme.txt"))
            {
                return "readme.txt";
            }

            int index = 1;
            while (true)
            {
                string candidate = $"readme ({index}).txt";
                if (!occupied.Contains(candidate))
                {
                    return candidate;
                }

                index++;
            }
        }

        private void RenameOrganizationEntryButton_Click(object sender, RoutedEventArgs e)
        {
            FileOrganizationEntryViewModel? target = fileOrganizationContextEntry ?? GetSelectedFileOrganizationEntry();
            if (target is not FileOrganizationEntryViewModel selectedEntry)
            {
                return;
            }

            if (selectedEntry.IsInteractionLocked)
            {
                StatusTextBlock.Text = "Selected entry is locked.";
                return;
            }

            BeginFileOrganizationRename(selectedEntry);
        }

        private void BeginFileOrganizationRename(FileOrganizationEntryViewModel entry)
        {
            foreach (FileOrganizationEntryViewModel item in allFileOrganizationEntries)
            {
                if (!ReferenceEquals(item, entry))
                {
                    item.IsRenaming = false;
                }
            }

            entry.RenameDraft = entry.Name;
            entry.IsRenaming = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DataGridRow? row = FileOrganizationListView.ItemContainerGenerator.ContainerFromItem(entry) as DataGridRow;
                TextBox? textBox = FindDescendantByName<TextBox>(row, "FileOrgInlineRenameTextBox");
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }), DispatcherPriority.Background);
        }

        private void CommitFileOrganizationRename(FileOrganizationEntryViewModel entry)
        {
            string newName = (entry.RenameDraft ?? string.Empty).Trim();
            entry.IsRenaming = false;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            string parent = GetParentDirectoryPath(entry.RelativePath, entry.IsFolder);
            string normalized = NormalizeRelativePath(parent + newName + (entry.IsFolder ? "/" : string.Empty), entry.IsFolder);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!UpdateOrganizationPathForEntry(entry, normalized))
            {
                return;
            }

            RefreshFileOrganizationEntries();
        }

        private void CommitAnyFileOrganizationRename()
        {
            FileOrganizationEntryViewModel? active = allFileOrganizationEntries.FirstOrDefault(static item => item.IsRenaming);
            if (active != null)
            {
                CommitFileOrganizationRename(active);
            }
        }

        private void FileOrgNameTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
            {
                return;
            }

            if (sender is TextBlock textBlock && textBlock.DataContext is FileOrganizationEntryViewModel entry)
            {
                if (entry.IsFolder)
                {
                    // Name double-click should rename, while row double-click opens folder.
                    e.Handled = true;
                    suppressFolderOpenFromRenameUntilUtc = DateTime.UtcNow.AddMilliseconds(450);
                    suppressFolderOpenFromRenamePath = entry.RelativePath;
                }

                if (!entry.IsInteractionLocked)
                {
                    BeginFileOrganizationRename(entry);
                }
            }
        }

        private void FileOrgRenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not FileOrganizationEntryViewModel entry)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                CommitFileOrganizationRename(entry);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                entry.IsRenaming = false;
                e.Handled = true;
            }
        }

        private void FileOrgRenameTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is FileOrganizationEntryViewModel entry && entry.IsRenaming)
            {
                CommitFileOrganizationRename(entry);
            }
        }

        private void MoveOrganizationEntryButton_Click(object sender, RoutedEventArgs e)
        {
            FileOrganizationEntryViewModel? target = fileOrganizationContextEntry ?? GetSelectedFileOrganizationEntry();
            if (target is not FileOrganizationEntryViewModel selectedEntry)
            {
                return;
            }

            if (selectedEntry.IsInteractionLocked)
            {
                StatusTextBlock.Text = "Selected entry is locked.";
                return;
            }

            string? targetFolder = InputDialog.Show("Move Entry", "Target folder path:", "extras/");
            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                return;
            }

            string normalizedFolder = targetFolder.Trim().Replace('\\', '/').Trim('/');
            if (!string.IsNullOrWhiteSpace(normalizedFolder))
            {
                normalizedFolder += "/";
            }

            string fileName = Path.GetFileName(selectedEntry.RelativePath.TrimEnd('/', '\\'));
            string destination = normalizedFolder + fileName + (selectedEntry.IsFolder ? "/" : string.Empty);
            if (!UpdateOrganizationPathForEntry(selectedEntry, destination))
            {
                return;
            }
            RefreshFileOrganizationEntries();
        }

        private void RemoveOrganizationEntryButton_Click(object sender, RoutedEventArgs e)
        {
            FileOrganizationEntryViewModel[] selectedEntries = GetSelectedFileOrganizationEntries();
            if (selectedEntries.Length == 0)
            {
                return;
            }

            FileOrganizationEntryViewModel[] removable = selectedEntries
                .Where(static entry => entry.IsExternal && entry.IsUnused && !entry.IsLocked)
                .ToArray();
            if (removable.Length == 0)
            {
                StatusTextBlock.Text = "Only unused entries can be removed.";
                return;
            }

            foreach (FileOrganizationEntryViewModel removableEntry in removable)
            {
                ExternalOrganizationEntry? external = externalOrganizationEntries.FirstOrDefault(entry =>
                    string.Equals(entry.RelativePath, removableEntry.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.SourcePath ?? string.Empty, removableEntry.SourcePath ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (external != null)
                {
                    externalOrganizationEntries.Remove(external);
                }

                if (!removableEntry.IsFolder)
                {
                    RemoveStagedTextEditForRelativePath(removableEntry.RelativePath);
                }
            }

            RefreshFileOrganizationEntries();
        }

        private void RemoveAllExternalOrganizationEntriesButton_Click(object sender, RoutedEventArgs e)
        {
            List<ExternalOrganizationEntry> removable = new List<ExternalOrganizationEntry>();
            foreach (ExternalOrganizationEntry external in externalOrganizationEntries)
            {
                FileOrganizationEntryViewModel? view = allFileOrganizationEntries.FirstOrDefault(entry =>
                    string.Equals(entry.RelativePath, external.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.SourcePath ?? string.Empty, external.SourcePath ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (view != null && view.IsUnused)
                {
                    removable.Add(external);
                }
            }

            if (removable.Count == 0)
            {
                return;
            }

            MessageBoxResult decision = OceanyaMessageBox.Show(
                this,
                "Remove all unused assets from organization config?",
                "Remove Unused Assets",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (ExternalOrganizationEntry external in removable)
            {
                externalOrganizationEntries.Remove(external);
                if (!external.IsFolder)
                {
                    RemoveStagedTextEditForRelativePath(external.RelativePath);
                }
            }
            fileOrganizationClipboardEntries.Clear();
            fileOrganizationClipboardIsCut = false;
            RefreshFileOrganizationEntries();
        }

        private bool UpdateOrganizationPathForEntry(FileOrganizationEntryViewModel selectedEntry, string normalizedPath)
        {
            if (selectedEntry != null && !selectedEntry.IsExternal && !selectedEntry.IsFolder)
            {
                normalizedPath = EnsureGeneratedFileExtension(selectedEntry, normalizedPath);
            }

            string oldNormalized = NormalizeRelativePath(selectedEntry.RelativePath, selectedEntry.IsFolder);
            if (selectedEntry.IsExternal)
            {
                ExternalOrganizationEntry? external = externalOrganizationEntries.FirstOrDefault(entry =>
                    string.Equals(entry.RelativePath, selectedEntry.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.SourcePath ?? string.Empty, selectedEntry.SourcePath ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (external == null)
                {
                    return false;
                }

                external.RelativePath = normalizedPath;
                MoveStagedTextEditRelativePath(oldNormalized, normalizedPath, selectedEntry.IsFolder);
                if (selectedEntry.IsFolder)
                {
                    UpdateFolderDescendants(oldNormalized, normalizedPath, forExternalEntries: true, forGeneratedOverrides: false);
                }
                return true;
            }

            if (string.IsNullOrWhiteSpace(selectedEntry.AssetKey))
            {
                if (!selectedEntry.IsFolder)
                {
                    return false;
                }

                UpdateFolderDescendants(oldNormalized, normalizedPath, forExternalEntries: true, forGeneratedOverrides: false);
                ApplyGeneratedFolderRenameOverrides(oldNormalized, normalizedPath);
                return true;
            }

            if (string.Equals(normalizedPath, selectedEntry.DefaultRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                generatedOrganizationOverrides.Remove(selectedEntry.AssetKey);
            }
            else
            {
                generatedOrganizationOverrides[selectedEntry.AssetKey] = normalizedPath;
            }

            if (selectedEntry.IsFolder)
            {
                UpdateFolderDescendants(oldNormalized, normalizedPath, forExternalEntries: false, forGeneratedOverrides: false);
                ApplyGeneratedFolderRenameOverrides(oldNormalized, normalizedPath);
            }

            return true;
        }

        private static string EnsureGeneratedFileExtension(FileOrganizationEntryViewModel entry, string proposedPath)
        {
            string normalized = NormalizeRelativePath(proposedPath, isFolder: false);
            string expectedExtension = Path.GetExtension(entry.DefaultRelativePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(expectedExtension))
            {
                return normalized;
            }

            string currentExtension = Path.GetExtension(normalized);
            string withoutCurrent = string.IsNullOrWhiteSpace(currentExtension)
                ? normalized
                : normalized.Substring(0, normalized.Length - currentExtension.Length);
            return NormalizeRelativePath(withoutCurrent + expectedExtension, isFolder: false);
        }

        private void ApplyGeneratedFolderRenameOverrides(string oldFolderPath, string newFolderPath)
        {
            string oldPrefix = NormalizeRelativePath(oldFolderPath, isFolder: true);
            string newPrefix = NormalizeRelativePath(newFolderPath, isFolder: true);
            if (string.IsNullOrWhiteSpace(oldPrefix) || string.IsNullOrWhiteSpace(newPrefix))
            {
                return;
            }

            foreach (FileOrganizationEntryViewModel entry in allFileOrganizationEntries)
            {
                if (entry.IsExternal || string.IsNullOrWhiteSpace(entry.AssetKey))
                {
                    continue;
                }

                string normalizedEntryPath = NormalizeRelativePath(entry.RelativePath, entry.IsFolder);
                if (!normalizedEntryPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string suffix = normalizedEntryPath.Substring(oldPrefix.Length);
                string replacementPath = NormalizeRelativePath(newPrefix + suffix, entry.IsFolder);
                if (string.Equals(replacementPath, NormalizeRelativePath(entry.DefaultRelativePath, entry.IsFolder), StringComparison.OrdinalIgnoreCase))
                {
                    generatedOrganizationOverrides.Remove(entry.AssetKey);
                }
                else
                {
                    generatedOrganizationOverrides[entry.AssetKey] = replacementPath;
                }
            }
        }

        private void UpdateFolderDescendants(
            string oldFolderPath,
            string newFolderPath,
            bool forExternalEntries,
            bool forGeneratedOverrides)
        {
            string oldPrefix = NormalizeRelativePath(oldFolderPath, isFolder: true);
            string newPrefix = NormalizeRelativePath(newFolderPath, isFolder: true);
            if (string.IsNullOrWhiteSpace(oldPrefix) || string.IsNullOrWhiteSpace(newPrefix))
            {
                return;
            }

            if (forExternalEntries)
            {
                foreach (ExternalOrganizationEntry external in externalOrganizationEntries)
                {
                    string normalized = NormalizeRelativePath(external.RelativePath, external.IsFolder);
                    if (!normalized.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string suffix = normalized.Substring(oldPrefix.Length);
                    external.RelativePath = NormalizeRelativePath(newPrefix + suffix, external.IsFolder);
                }
            }

            if (forGeneratedOverrides)
            {
                List<string> keys = generatedOrganizationOverrides.Keys.ToList();
                foreach (string key in keys)
                {
                    string normalized = NormalizeRelativePath(
                        generatedOrganizationOverrides[key],
                        isFolder: generatedOrganizationOverrides[key].EndsWith("/", StringComparison.Ordinal));
                    if (!normalized.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string suffix = normalized.Substring(oldPrefix.Length);
                    generatedOrganizationOverrides[key] = NormalizeRelativePath(
                        newPrefix + suffix,
                        isFolder: generatedOrganizationOverrides[key].EndsWith("/", StringComparison.Ordinal));
                }
            }
        }

        private FileOrganizationEntryViewModel? GetSelectedFileOrganizationEntry()
        {
            return FileOrganizationListView.SelectedItem as FileOrganizationEntryViewModel;
        }

        private FileOrganizationEntryViewModel[] GetSelectedFileOrganizationEntries()
        {
            return FileOrganizationListView.SelectedItems
                .OfType<FileOrganizationEntryViewModel>()
                .ToArray();
        }

        private static FileOrganizationEntryViewModel? ResolveFileOrganizationEntryFromSource(object? source)
        {
            DependencyObject? current = source as DependencyObject;
            while (current != null)
            {
                if (current is DataGridRow row && row.Item is FileOrganizationEntryViewModel entry)
                {
                    return entry;
                }

                if (current is FrameworkElement element)
                {
                    current = element.Parent
                        ?? element.TemplatedParent as DependencyObject
                        ?? VisualTreeHelper.GetParent(current);
                }
                else
                {
                    current = VisualTreeHelper.GetParent(current);
                }
            }

            return null;
        }

        private FileOrganizationEntryViewModel? ResolveFileOrganizationEntryFromDropPosition(DragEventArgs e)
        {
            Point point = e.GetPosition(FileOrganizationListView);
            IInputElement? element = FileOrganizationListView.InputHitTest(point);
            FileOrganizationEntryViewModel? fromHit = ResolveFileOrganizationEntryFromSource(element as DependencyObject);
            if (fromHit != null)
            {
                return fromHit;
            }

            return ResolveFileOrganizationEntryFromSource(e.OriginalSource);
        }

        private static FileOrganizationEntryViewModel[] GetDraggedFileOrganizationEntries(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(FileOrganizationEntryViewModel[]))
                && e.Data.GetData(typeof(FileOrganizationEntryViewModel[])) is FileOrganizationEntryViewModel[] manyEntries)
            {
                return manyEntries.Where(static entry => entry != null).ToArray();
            }

            if (e.Data.GetDataPresent(typeof(FileOrganizationEntryViewModel))
                && e.Data.GetData(typeof(FileOrganizationEntryViewModel)) is FileOrganizationEntryViewModel singleEntry)
            {
                return new[] { singleEntry };
            }

            return Array.Empty<FileOrganizationEntryViewModel>();
        }

        private static bool IsInvalidFolderSelfDrop(FileOrganizationEntryViewModel draggedEntry, string destinationPath)
        {
            if (draggedEntry == null || !draggedEntry.IsFolder)
            {
                return false;
            }

            string sourceFolder = NormalizeRelativePath(draggedEntry.RelativePath, isFolder: true);
            string targetFolder = NormalizeRelativePath(destinationPath, isFolder: true);
            if (string.IsNullOrWhiteSpace(sourceFolder) || string.IsNullOrWhiteSpace(targetFolder))
            {
                return false;
            }

            return targetFolder.StartsWith(sourceFolder, StringComparison.OrdinalIgnoreCase);
        }

        private void FileOrganizationContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            OpenFileOrganizationContextMenu(sender, e.OriginalSource);
        }

        private void FileOrganizationContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            OpenFileOrganizationContextMenu(sender, e.OriginalSource);
        }

        private void OpenFileOrganizationContextMenu(object sender, object? originalSource)
        {
            FileOrganizationEntryViewModel? entryFromSource = sender is DataGridRow row
                ? row.Item as FileOrganizationEntryViewModel
                : ResolveFileOrganizationEntryFromSource(originalSource);
            if (entryFromSource != null)
            {
                fileOrganizationContextEntry = entryFromSource;
                if (!FileOrganizationListView.SelectedItems.OfType<FileOrganizationEntryViewModel>().Contains(entryFromSource))
                {
                    FileOrganizationListView.SelectedItems.Clear();
                    FileOrganizationListView.SelectedItem = entryFromSource;
                }
            }
            else
            {
                fileOrganizationContextEntry = GetSelectedFileOrganizationEntry();
            }

            ContextMenu menu = (sender as FrameworkElement)?.ContextMenu ?? new ContextMenu();
            menu.Items.Clear();
            AddContextCategoryHeader(menu, "List", addLeadingSeparator: false);
            menu.Items.Add(CreateContextMenuItem("Refresh List", () => RefreshFileOrganizationButton_Click(this, new RoutedEventArgs())));
            menu.Items.Add(CreateContextMenuItem("Reset File Organization", () => ResetFileOrganizationButton_Click(this, new RoutedEventArgs())));

            MenuItem newSubmenu = new MenuItem
            {
                Header = "New..."
            };
            newSubmenu.Items.Add(CreateContextMenuItem("File", () => AddOrganizationFileButton_Click(this, new RoutedEventArgs())));
            newSubmenu.Items.Add(CreateContextMenuItem("Folder", () => CreateOrganizationFolderButton_Click(this, new RoutedEventArgs())));
            newSubmenu.Items.Add(CreateContextMenuItem("Readme File", () => CreateReadmeFileButton_Click(this, new RoutedEventArgs())));
            menu.Items.Add(newSubmenu);

            AddContextCategoryHeader(menu, "Selection", addLeadingSeparator: true);
            FileOrganizationEntryViewModel? selected = fileOrganizationContextEntry ?? GetSelectedFileOrganizationEntry();
            MenuItem openItem = CreateContextMenuItem("Open", () =>
            {
                FileOrganizationEntryViewModel? target = fileOrganizationContextEntry;
                if (target != null)
                {
                    OpenFileOrganizationEntry(target);
                }
            });
            MenuItem renameItem = CreateContextMenuItem("Rename", () => RenameOrganizationEntryButton_Click(this, new RoutedEventArgs()));
            MenuItem moveItem = CreateContextMenuItem("Move To...", () => MoveOrganizationEntryButton_Click(this, new RoutedEventArgs()));
            MenuItem removeItem = CreateContextMenuItem("Remove (Only if unused)", () => RemoveOrganizationEntryButton_Click(this, new RoutedEventArgs()));
            bool canEditSelected = selected != null
                && !selected.IsInteractionLocked
                && (selected.IsExternal || !string.IsNullOrWhiteSpace(selected.AssetKey) || selected.IsFolder);
            openItem.IsEnabled = selected != null
                && (selected.IsFolder || IsCharIniOrganizationEntry(selected) || IsTextOrganizationEntry(selected));
            menu.Items.Add(openItem);
            renameItem.IsEnabled = canEditSelected;
            moveItem.IsEnabled = canEditSelected;
            removeItem.IsEnabled = selected != null && selected.IsUnused;
            menu.Items.Add(renameItem);
            menu.Items.Add(moveItem);
            menu.Items.Add(removeItem);

            AddContextCategoryHeader(menu, "Clipboard", addLeadingSeparator: true);
            MenuItem copyItem = CreateContextMenuItem("Copy", () => CopySelectedFileOrganizationEntry());
            MenuItem cutItem = CreateContextMenuItem("Cut", () => CutSelectedFileOrganizationEntry());
            MenuItem pasteItem = CreateContextMenuItem("Paste", () => PasteIntoCurrentFileOrganizationPath());
            copyItem.IsEnabled = selected != null;
            cutItem.IsEnabled = selected != null && canEditSelected;
            pasteItem.IsEnabled = fileOrganizationClipboardEntries.Count > 0;
            menu.Items.Add(copyItem);
            menu.Items.Add(cutItem);
            menu.Items.Add(pasteItem);

            AddContextCategoryHeader(menu, "Cleanup", addLeadingSeparator: true);
            MenuItem removeAllItem = CreateContextMenuItem("Remove all unused assets", () => RemoveAllExternalOrganizationEntriesButton_Click(this, new RoutedEventArgs()));
            removeAllItem.IsEnabled = allFileOrganizationEntries.Any(static entry => entry.IsUnused);
            menu.Items.Add(removeAllItem);

            if (sender is FrameworkElement host)
            {
                host.ContextMenu = menu;
            }
        }

        private void FileOrganizationListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (allFileOrganizationEntries.Any(static entry => entry.IsRenaming))
            {
                TextBox? renameTextBox = FindAncestor<TextBox>(e.OriginalSource as DependencyObject);
                if (renameTextBox == null || !string.Equals(renameTextBox.Name, "FileOrgInlineRenameTextBox", StringComparison.Ordinal))
                {
                    CommitAnyFileOrganizationRename();
                }
            }

            preserveFileOrganizationMultiSelectionForDrag = false;
            fileOrganizationDragSelectionSnapshot = Array.Empty<FileOrganizationEntryViewModel>();
            FileOrganizationEntryViewModel? clickedEntry = ResolveFileOrganizationEntryFromSource(e.OriginalSource);
            if (clickedEntry != null
                && Keyboard.Modifiers == ModifierKeys.None
                && FileOrganizationListView.SelectedItems.Count > 1
                && FileOrganizationListView.SelectedItems.OfType<FileOrganizationEntryViewModel>().Contains(clickedEntry))
            {
                preserveFileOrganizationMultiSelectionForDrag = true;
                fileOrganizationDragSelectionSnapshot = GetSelectedFileOrganizationEntries();
                e.Handled = true;
            }

            fileOrganizationDragStartPoint = e.GetPosition(FileOrganizationListView);
        }

        private void FileOrganizationListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (activeFileOrganizationRowResizeThumb != null)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector delta = e.GetPosition(FileOrganizationListView) - fileOrganizationDragStartPoint;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            FileOrganizationEntryViewModel[] selectedEntries = (preserveFileOrganizationMultiSelectionForDrag
                    ? fileOrganizationDragSelectionSnapshot
                    : GetSelectedFileOrganizationEntries())
                .Where(static entry => !entry.IsInteractionLocked
                    && (entry.IsExternal || !string.IsNullOrWhiteSpace(entry.AssetKey) || entry.IsFolder))
                .ToArray();
            if (selectedEntries.Length == 0)
            {
                return;
            }

            DataObject dataObject = new DataObject();
            dataObject.SetData(typeof(FileOrganizationEntryViewModel[]), selectedEntries);
            dataObject.SetData(typeof(FileOrganizationEntryViewModel), selectedEntries[0]);
            DragDrop.DoDragDrop(FileOrganizationListView, dataObject, DragDropEffects.Move);
            preserveFileOrganizationMultiSelectionForDrag = false;
            fileOrganizationDragSelectionSnapshot = Array.Empty<FileOrganizationEntryViewModel>();
        }

        private void FileOrganizationRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGridRow row || e.ClickCount > 1)
            {
                return;
            }

            Point point = e.GetPosition(row);
            if (point.Y < row.ActualHeight - 4)
            {
                return;
            }

            activeFileOrganizationRowResizeRow = row;
            activeFileOrganizationRowResizeThumb = new Thumb();
            fileOrganizationRowResizeFromDivider = true;
            fileOrganizationRowResizeStartHeight = FileOrganizationListView.RowHeight > 0
                ? FileOrganizationListView.RowHeight
                : fileOrganizationRowResizePreviewHeight;
            fileOrganizationRowResizePreviewHeight = fileOrganizationRowResizeStartHeight;
            fileOrganizationRowResizeGuideStartY = Math.Clamp(
                e.GetPosition(FileOrganizationListView).Y,
                0,
                Math.Max(0, FileOrganizationListView.ActualHeight - 1));
            ShowFileOrganizationRowResizeGuide(fileOrganizationRowResizeGuideStartY);
            row.CaptureMouse();
            e.Handled = true;
        }

        private void FileOrganizationRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                Point hoverPoint = e.GetPosition(row);
                if (!fileOrganizationRowResizeFromDivider)
                {
                    row.Cursor = hoverPoint.Y >= row.ActualHeight - 4 ? Cursors.SizeNS : Cursors.Arrow;
                }
            }

            if (!fileOrganizationRowResizeFromDivider || activeFileOrganizationRowResizeRow == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            double mouseY = Mouse.GetPosition(FileOrganizationListView).Y;
            double minGuideY = fileOrganizationRowResizeGuideStartY + (48 - fileOrganizationRowResizeStartHeight);
            double maxGuideY = fileOrganizationRowResizeGuideStartY + (180 - fileOrganizationRowResizeStartHeight);
            double clampedGuideY = Math.Clamp(mouseY, Math.Min(minGuideY, maxGuideY), Math.Max(minGuideY, maxGuideY));
            fileOrganizationRowResizePreviewHeight = Math.Clamp(
                fileOrganizationRowResizeStartHeight + (clampedGuideY - fileOrganizationRowResizeGuideStartY),
                48,
                180);
            UpdateFileOrganizationRowResizeGuide(clampedGuideY);
            e.Handled = true;
        }

        private void FileOrganizationRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!fileOrganizationRowResizeFromDivider || activeFileOrganizationRowResizeRow == null)
            {
                return;
            }

            if (Math.Abs(FileOrganizationListView.RowHeight - fileOrganizationRowResizePreviewHeight) > 0.01)
            {
                FileOrganizationListView.RowHeight = fileOrganizationRowResizePreviewHeight;
            }

            activeFileOrganizationRowResizeRow.ReleaseMouseCapture();
            activeFileOrganizationRowResizeRow = null;
            activeFileOrganizationRowResizeThumb = null;
            fileOrganizationRowResizeFromDivider = false;
            HideFileOrganizationRowResizeGuide();
            e.Handled = true;
        }

        private void FileOrganizationListView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            bool hasInternalEntries = e.Data.GetDataPresent(typeof(FileOrganizationEntryViewModel))
                || e.Data.GetDataPresent(typeof(FileOrganizationEntryViewModel[]));
            if (!hasInternalEntries)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            FileOrganizationEntryViewModel? target = ResolveFileOrganizationEntryFromDropPosition(e);
            bool validTarget = target != null && target.IsFolder && !target.IsInteractionLocked;
            if (validTarget && target != null)
            {
                FileOrganizationEntryViewModel[] draggedEntries = GetDraggedFileOrganizationEntries(e);
                string baseFolder = NormalizeRelativePath(target.RelativePath, isFolder: true);
                foreach (FileOrganizationEntryViewModel draggedEntry in draggedEntries)
                {
                    if (draggedEntry == null)
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(draggedEntry.RelativePath.TrimEnd('/', '\\'));
                    string destination = baseFolder + fileName + (draggedEntry.IsFolder ? "/" : string.Empty);
                    if (IsInvalidFolderSelfDrop(draggedEntry, destination))
                    {
                        validTarget = false;
                        break;
                    }
                }
            }

            e.Effects = validTarget ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void FileOrganizationListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)
                && e.Data.GetData(DataFormats.FileDrop) is string[] droppedPaths
                && droppedPaths.Length > 0)
            {
                foreach (string droppedPath in droppedPaths)
                {
                    AddDroppedPathToCurrentFileOrganization(droppedPath);
                }

                RefreshFileOrganizationEntries();
                StatusTextBlock.Text = "Added dropped assets as unused files.";
                return;
            }

            FileOrganizationEntryViewModel[] draggedEntries = GetDraggedFileOrganizationEntries(e);
            if (draggedEntries.Length == 0)
            {
                return;
            }

            FileOrganizationEntryViewModel? dropTarget = ResolveFileOrganizationEntryFromDropPosition(e);
            if (dropTarget == null || !dropTarget.IsFolder || dropTarget.IsInteractionLocked)
            {
                return;
            }

            string baseFolder = dropTarget.RelativePath.Trim().Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(baseFolder) && !baseFolder.EndsWith("/", StringComparison.Ordinal))
            {
                baseFolder += "/";
            }

            bool movedAny = false;
            foreach (FileOrganizationEntryViewModel draggedEntry in draggedEntries)
            {
                if (draggedEntry == null
                    || draggedEntry.IsInteractionLocked
                    || (!draggedEntry.IsExternal && string.IsNullOrWhiteSpace(draggedEntry.AssetKey) && !draggedEntry.IsFolder))
                {
                    continue;
                }

                string fileName = Path.GetFileName(draggedEntry.RelativePath.TrimEnd('/', '\\'));
                string destination = baseFolder + fileName + (draggedEntry.IsFolder ? "/" : string.Empty);
                if (IsInvalidFolderSelfDrop(draggedEntry, destination))
                {
                    continue;
                }

                movedAny |= UpdateOrganizationPathForEntry(draggedEntry, destination);
            }

            if (!movedAny)
            {
                return;
            }

            RefreshFileOrganizationEntries();
            StatusTextBlock.Text = draggedEntries.Length > 1 ? "Entries moved." : "Entry moved.";
        }

        private void AddDroppedPathToCurrentFileOrganization(string droppedPath)
        {
            if (string.IsNullOrWhiteSpace(droppedPath))
            {
                return;
            }

            if (File.Exists(droppedPath))
            {
                string relative = currentFileOrganizationPath + Path.GetFileName(droppedPath);
                externalOrganizationEntries.Add(new ExternalOrganizationEntry
                {
                    SourcePath = droppedPath,
                    RelativePath = NormalizeRelativePath(relative, isFolder: false),
                    IsFolder = false
                });
                return;
            }

            if (Directory.Exists(droppedPath))
            {
                string relative = currentFileOrganizationPath + Path.GetFileName(droppedPath) + "/";
                externalOrganizationEntries.Add(new ExternalOrganizationEntry
                {
                    SourcePath = droppedPath,
                    RelativePath = NormalizeRelativePath(relative, isFolder: true),
                    IsFolder = true
                });
            }
        }

        private void CopySelectedFileOrganizationEntry()
        {
            FileOrganizationEntryViewModel[] selectedEntries = GetSelectedFileOrganizationEntries();
            if (selectedEntries.Length == 0)
            {
                return;
            }

            fileOrganizationClipboardEntries.Clear();
            foreach (FileOrganizationEntryViewModel selected in selectedEntries)
            {
                fileOrganizationClipboardEntries.Add(new FileOrganizationClipboardEntry
                {
                    Entry = selected
                });
            }
            fileOrganizationClipboardIsCut = false;
            ClearPendingCutState();
            StatusTextBlock.Text = "Copied entry.";
        }

        private void CutSelectedFileOrganizationEntry()
        {
            FileOrganizationEntryViewModel[] selectedEntries = GetSelectedFileOrganizationEntries()
                .Where(static entry => !entry.IsInteractionLocked
                    && (entry.IsExternal || !string.IsNullOrWhiteSpace(entry.AssetKey)))
                .ToArray();
            if (selectedEntries.Length == 0)
            {
                return;
            }

            fileOrganizationClipboardEntries.Clear();
            foreach (FileOrganizationEntryViewModel selected in selectedEntries)
            {
                fileOrganizationClipboardEntries.Add(new FileOrganizationClipboardEntry
                {
                    Entry = selected
                });
            }
            fileOrganizationClipboardIsCut = true;
            ClearPendingCutState();
            foreach (FileOrganizationEntryViewModel selected in selectedEntries)
            {
                selected.IsPendingCut = true;
            }
            StatusTextBlock.Text = "Cut entry.";
        }

        private void PasteIntoCurrentFileOrganizationPath()
        {
            if (fileOrganizationClipboardEntries.Count == 0)
            {
                return;
            }

            if (fileOrganizationClipboardIsCut)
            {
                foreach (FileOrganizationClipboardEntry clipboardEntry in fileOrganizationClipboardEntries)
                {
                    FileOrganizationEntryViewModel? entry = clipboardEntry.Entry;
                    if (entry == null || entry.IsInteractionLocked)
                    {
                        continue;
                    }

                    string destination = NormalizeRelativePath(
                        currentFileOrganizationPath + Path.GetFileName(entry.RelativePath.TrimEnd('/', '\\')) + (entry.IsFolder ? "/" : string.Empty),
                        entry.IsFolder);
                    _ = UpdateOrganizationPathForEntry(entry, destination);
                }

                fileOrganizationClipboardEntries.Clear();
                fileOrganizationClipboardIsCut = false;
                ClearPendingCutState();
                RefreshFileOrganizationEntries();
                StatusTextBlock.Text = "Moved entry.";
                return;
            }

            foreach (FileOrganizationClipboardEntry clipboardEntry in fileOrganizationClipboardEntries)
            {
                FileOrganizationEntryViewModel? entry = clipboardEntry.Entry;
                if (entry == null)
                {
                    continue;
                }

                if (entry.IsExternal)
                {
                    string destination = BuildUniqueExternalPath(
                        currentFileOrganizationPath + Path.GetFileName(entry.RelativePath.TrimEnd('/', '\\')) + (entry.IsFolder ? "/" : string.Empty),
                        entry.IsFolder);
                    externalOrganizationEntries.Add(new ExternalOrganizationEntry
                    {
                        SourcePath = entry.SourcePath,
                        RelativePath = NormalizeRelativePath(destination, entry.IsFolder),
                        IsFolder = entry.IsFolder
                    });
                }
                else if (!string.IsNullOrWhiteSpace(entry.SourcePath) && File.Exists(entry.SourcePath))
                {
                    string destination = BuildUniqueExternalPath(
                        currentFileOrganizationPath + Path.GetFileName(entry.RelativePath.TrimEnd('/', '\\')),
                        isFolder: false);
                    externalOrganizationEntries.Add(new ExternalOrganizationEntry
                    {
                        SourcePath = entry.SourcePath,
                        RelativePath = NormalizeRelativePath(destination, isFolder: false),
                        IsFolder = false
                    });
                }
            }

            RefreshFileOrganizationEntries();
            StatusTextBlock.Text = "Pasted entry.";
        }

        private string BuildUniqueExternalPath(string desiredPath, bool isFolder)
        {
            string normalized = NormalizeRelativePath(desiredPath, isFolder);
            HashSet<string> occupiedPaths = allFileOrganizationEntries
                .Select(entry => NormalizeRelativePath(entry.RelativePath, entry.IsFolder))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!occupiedPaths.Contains(normalized))
            {
                return normalized;
            }

            string name = Path.GetFileNameWithoutExtension(normalized.TrimEnd('/'));
            string extension = isFolder ? string.Empty : Path.GetExtension(normalized);
            string parent = GetParentDirectoryPath(normalized, isFolder);
            int index = 2;
            while (true)
            {
                string candidateName = $"{name} - Copy {index}";
                string candidate = NormalizeRelativePath(parent + candidateName + extension + (isFolder ? "/" : string.Empty), isFolder);
                if (!occupiedPaths.Contains(candidate))
                {
                    return candidate;
                }

                index++;
            }
        }

        private void ClearPendingCutState()
        {
            foreach (FileOrganizationEntryViewModel entry in allFileOrganizationEntries)
            {
                entry.IsPendingCut = false;
            }
        }

        private void FileOrganizationListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (allFileOrganizationEntries.Any(static entry => entry.IsRenaming))
            {
                return;
            }

            FileOrganizationEntryViewModel? entry = ResolveFileOrganizationEntryFromSource(e.OriginalSource)
                ?? GetSelectedFileOrganizationEntry();
            if (entry == null)
            {
                return;
            }

            if (!entry.IsFolder)
            {
                OpenFileOrganizationEntry(entry);
                return;
            }

            if (DateTime.UtcNow <= suppressFolderOpenFromRenameUntilUtc
                && string.Equals(entry.RelativePath, suppressFolderOpenFromRenamePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            currentFileOrganizationPath = NormalizeRelativePath(entry.RelativePath, isFolder: true);
            RefreshFileOrganizationCurrentFolderView();
        }

        private static bool IsCharIniOrganizationEntry(FileOrganizationEntryViewModel entry)
        {
            if (entry == null || entry.IsFolder)
            {
                return false;
            }

            if (string.Equals(entry.AssetKey, "charini", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(
                NormalizeRelativePath(entry.RelativePath, isFolder: false),
                "char.ini",
                StringComparison.OrdinalIgnoreCase);
        }

        private bool OpenFileOrganizationEntry(FileOrganizationEntryViewModel entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (entry.IsFolder)
            {
                currentFileOrganizationPath = NormalizeRelativePath(entry.RelativePath, isFolder: true);
                RefreshFileOrganizationCurrentFolderView();
                return true;
            }

            if (IsCharIniOrganizationEntry(entry))
            {
                OpenCharIniPreviewButton_Click(this, new RoutedEventArgs());
                return true;
            }

            if (!IsTextOrganizationEntry(entry))
            {
                return false;
            }

            if (!TryResolveTextOrganizationEntrySourcePath(entry, out string? sourcePath, out string errorMessage)
                || string.IsNullOrWhiteSpace(sourcePath))
            {
                OceanyaMessageBox.Show(this, errorMessage, "Open Text File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string initialContent;
            try
            {
                initialContent = File.ReadAllText(sourcePath);
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to read text asset for file organization: {sourcePath}", ex);
                OceanyaMessageBox.Show(
                    this,
                    "Could not read this text file.\n" + ex.Message,
                    "Open Text File",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            bool isReadOnly = entry.IsInteractionLocked;
            string? updatedContent = ShowTextDocumentDialog(
                isReadOnly ? "Text File Preview" : "Edit Text File",
                Path.GetFileName(sourcePath),
                isReadOnly
                    ? "Read-only preview for locked/generated text assets."
                    : "Edit the file content and click Save to apply changes.",
                initialContent,
                isReadOnly: isReadOnly);
            if (isReadOnly || updatedContent == null || string.Equals(updatedContent, initialContent, StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                StageTextOrganizationEntryContent(entry, updatedContent);
                StatusTextBlock.Text = $"Staged text file changes: {Path.GetFileName(sourcePath)}";
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to save text asset for file organization: {sourcePath}", ex);
                OceanyaMessageBox.Show(
                    this,
                    "Could not save this text file.\n" + ex.Message,
                    "Edit Text File",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        private static bool IsTextOrganizationEntry(FileOrganizationEntryViewModel entry)
        {
            if (entry == null || entry.IsFolder)
            {
                return false;
            }

            string probePath = !string.IsNullOrWhiteSpace(entry.SourcePath)
                ? entry.SourcePath
                : entry.RelativePath;
            string extension = Path.GetExtension(probePath ?? string.Empty);
            return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ini", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveTextOrganizationEntrySourcePath(
            FileOrganizationEntryViewModel entry,
            out string? sourcePath,
            out string errorMessage)
        {
            sourcePath = null;
            errorMessage = string.Empty;
            if (entry == null || entry.IsFolder)
            {
                errorMessage = "Selected entry is not a text file.";
                return false;
            }

            string normalizedRelativePath = NormalizeRelativePath(entry.RelativePath, isFolder: false);
            if (stagedTextAssetSourcePathsByRelativePath.TryGetValue(normalizedRelativePath, out string? stagedPath)
                && !string.IsNullOrWhiteSpace(stagedPath)
                && File.Exists(stagedPath))
            {
                sourcePath = stagedPath;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entry.SourcePath) && File.Exists(entry.SourcePath))
            {
                sourcePath = entry.SourcePath;
                return true;
            }

            if (IsCharIniOrganizationEntry(entry))
            {
                errorMessage = "char.ini preview is generated in-memory and is not directly editable from this list.";
                return false;
            }

            string mountPath = ResolveSelectedMountPathForCreation();
            string folderName = (CharacterFolderNameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(mountPath) || string.IsNullOrWhiteSpace(folderName))
            {
                errorMessage = "Could not resolve a source file path for this text asset.";
                return false;
            }

            string absoluteCandidate = Path.Combine(
                mountPath,
                "characters",
                folderName,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absoluteCandidate))
            {
                sourcePath = absoluteCandidate;
                return true;
            }

            errorMessage = "Could not locate a physical source file for this text asset.";
            return false;
        }

        private void FileOrgRowResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is not Thumb thumb)
            {
                return;
            }

            activeFileOrganizationRowResizeThumb = thumb;
            fileOrganizationRowResizeStartHeight = FileOrganizationListView.RowHeight > 0
                ? FileOrganizationListView.RowHeight
                : fileOrganizationRowResizePreviewHeight;
            fileOrganizationRowResizePreviewHeight = fileOrganizationRowResizeStartHeight;
            fileOrganizationRowResizeGuideStartY = Math.Clamp(
                Mouse.GetPosition(FileOrganizationListView).Y,
                0,
                Math.Max(0, FileOrganizationListView.ActualHeight - 1));
            ShowFileOrganizationRowResizeGuide(fileOrganizationRowResizeGuideStartY);
            e.Handled = true;
        }

        private void FileOrgRowResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || !ReferenceEquals(thumb, activeFileOrganizationRowResizeThumb))
            {
                return;
            }

            double mouseY = Mouse.GetPosition(FileOrganizationListView).Y;
            double minGuideY = fileOrganizationRowResizeGuideStartY + (48 - fileOrganizationRowResizeStartHeight);
            double maxGuideY = fileOrganizationRowResizeGuideStartY + (180 - fileOrganizationRowResizeStartHeight);
            double clampedGuideY = Math.Clamp(mouseY, Math.Min(minGuideY, maxGuideY), Math.Max(minGuideY, maxGuideY));
            fileOrganizationRowResizePreviewHeight = Math.Clamp(
                fileOrganizationRowResizeStartHeight + (clampedGuideY - fileOrganizationRowResizeGuideStartY),
                48,
                180);
            UpdateFileOrganizationRowResizeGuide(clampedGuideY);
            e.Handled = true;
        }

        private void FileOrgRowResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is not Thumb thumb || !ReferenceEquals(thumb, activeFileOrganizationRowResizeThumb))
            {
                return;
            }

            if (Math.Abs(FileOrganizationListView.RowHeight - fileOrganizationRowResizePreviewHeight) > 0.01)
            {
                FileOrganizationListView.RowHeight = fileOrganizationRowResizePreviewHeight;
            }

            activeFileOrganizationRowResizeThumb = null;
            HideFileOrganizationRowResizeGuide();
            e.Handled = true;
        }

        private void ShowFileOrganizationRowResizeGuide(double verticalOffset)
        {
            HideFileOrganizationRowResizeGuide();
            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(FileOrganizationListView);
            if (layer == null)
            {
                return;
            }

            fileOrganizationRowResizeAdornerLayer = layer;
            fileOrganizationRowResizeGuideAdorner = new RowResizeGuideAdorner(FileOrganizationListView);
            fileOrganizationRowResizeGuideAdorner.SetGuideY(verticalOffset);
            layer.Add(fileOrganizationRowResizeGuideAdorner);
        }

        private void UpdateFileOrganizationRowResizeGuide(double verticalOffset)
        {
            if (fileOrganizationRowResizeGuideAdorner == null)
            {
                return;
            }

            fileOrganizationRowResizeGuideAdorner.SetGuideY(verticalOffset);
        }

        private void HideFileOrganizationRowResizeGuide()
        {
            if (fileOrganizationRowResizeAdornerLayer != null && fileOrganizationRowResizeGuideAdorner != null)
            {
                fileOrganizationRowResizeAdornerLayer.Remove(fileOrganizationRowResizeGuideAdorner);
            }

            fileOrganizationRowResizeGuideAdorner = null;
            fileOrganizationRowResizeAdornerLayer = null;
        }

        private void FileOrganizationGoUpButton_Click(object sender, RoutedEventArgs e)
        {
            CommitAnyFileOrganizationRename();
            if (string.IsNullOrWhiteSpace(currentFileOrganizationPath))
            {
                return;
            }

            currentFileOrganizationPath = GetParentDirectoryPath(currentFileOrganizationPath, isFolder: true);
            RefreshFileOrganizationCurrentFolderView();
        }

        private void FileOrganizationAudioPlayButton_PlayRequested(object sender, EventArgs e)
        {
            if (sender is not PlayStopProgressButton button
                || button.Tag is not FileOrganizationEntryViewModel entry
                || string.IsNullOrWhiteSpace(entry.SourcePath)
                || !File.Exists(entry.SourcePath))
            {
                return;
            }

            if (activeFileOrganizationAudioButton != null && !ReferenceEquals(activeFileOrganizationAudioButton, button))
            {
                activeFileOrganizationAudioButton.IsPlaying = false;
            }

            StopFileOrganizationAudioPreview();
            if (!fileOrganizationAudioPreviewPlayer.TrySetBlip(entry.SourcePath))
            {
                return;
            }

            double durationMs = Math.Max(120, fileOrganizationAudioPreviewPlayer.GetLoadedDurationMs());
            button.DurationMs = durationMs;
            button.IsPlaying = true;
            activeFileOrganizationAudioButton = button;
            _ = fileOrganizationAudioPreviewPlayer.PlayBlip();
        }

        private void FileOrganizationAudioPlayButton_StopRequested(object sender, EventArgs e)
        {
            StopFileOrganizationAudioPreview();
        }

        private void StopFileOrganizationAudioPreview()
        {
            fileOrganizationAudioPreviewPlayer.Stop();
            if (activeFileOrganizationAudioButton != null)
            {
                activeFileOrganizationAudioButton.IsPlaying = false;
                activeFileOrganizationAudioButton = null;
            }
        }

        private void OpenCharIniPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CharacterCreationProject project = BuildProjectForPreview();
                string charIni = AOCharacterFileCreatorBuilder.BuildCharIni(project);
                _ = ShowTextDocumentDialog(
                    "char.ini Preview",
                    "char.ini",
                    "Read-only output preview.",
                    charIni,
                    isReadOnly: true);
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to build char.ini preview.", ex);
                StatusTextBlock.Text = "Could not generate char.ini preview.";
            }
        }

        private string? ShowTextDocumentDialog(
            string title,
            string label,
            string description,
            string initialContent,
            bool isReadOnly)
        {
            Window dialog = CreateEmoteDialog(title, 780, 640);
            Grid grid = BuildDialogGrid(1);
            TextBox box = CreateDialogTextBox(initialContent, description);
            box.AcceptsReturn = true;
            box.AcceptsTab = true;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.IsReadOnly = isReadOnly;
            box.Height = 520;
            AddDialogFieldContainer(grid, 0, label, description, box);
            DockPanel buttons = isReadOnly
                ? BuildDialogButtonsReadOnly(dialog)
                : BuildDialogButtons(dialog);
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            bool? result = dialog.ShowDialog();
            if (result != true)
            {
                return null;
            }

            return box.Text ?? string.Empty;
        }

        private CharacterCreationProject BuildProjectForPreview()
        {
            string folderName = (CharacterFolderNameTextBox.Text ?? string.Empty).Trim();
            List<CharacterCreationEmote> generatedEmotes = emotes.Select(static viewModel => viewModel.ToModel()).ToList();
            return new CharacterCreationProject
            {
                MountPath = (MountPathComboBox.Text ?? string.Empty).Trim(),
                CharacterFolderName = folderName,
                Name = folderName,
                ShowName = (ShowNameTextBox.Text ?? string.Empty).Trim(),
                Side = (SideComboBox.Text ?? string.Empty).Trim(),
                Gender = (GenderBlipsDropdown.Text ?? string.Empty).Trim(),
                Blips = (GenderBlipsDropdown.Text ?? string.Empty).Trim(),
                Chat = (ChatDropdown.Text ?? string.Empty).Trim(),
                Realization = (RealizationTextBox.Text ?? string.Empty).Trim(),
                Effects = (EffectsDropdown.Text ?? string.Empty).Trim(),
                Scaling = (ScalingDropdown.Text ?? string.Empty).Trim(),
                Stretch = (StretchDropdown.Text ?? string.Empty).Trim(),
                NeedsShowName = (NeedsShownameDropdown.Text ?? string.Empty).Trim(),
                AssetFolders = assetFolders.ToList(),
                AdvancedEntries = advancedEntries.Select(static entry => new CharacterCreationAdvancedEntry
                {
                    Section = entry.Section,
                    Key = entry.Key,
                    Value = entry.Value
                }).ToList(),
                Emotes = generatedEmotes
            };
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
                UpdateCharacterIconFromEmoteButtonVisibility();
            }
            finally
            {
                suppressEmoteTileSelectionChanged = false;
            }

            EnsureEmotePreviewPlayersInitialized();
            RestartEmotePreviewPlayers(GetSelectedEmote());
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
                || FindAncestor<ScrollBar>(source) != null)
            {
                pendingTileDragEntry = null;
                return;
            }

            ListBoxItem? item = FindAncestor<ListBoxItem>(source);
            if (item?.DataContext is EmoteTileEntryViewModel entry && !entry.IsAddTile)
            {
                pendingTileDragEntry = entry;
                activeTileDragContainer = item;
            }
            else
            {
                pendingTileDragEntry = null;
                activeTileDragContainer = null;
            }
        }

        private void EmoteTilesListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (isInternalEmoteReorderDragInProgress)
            {
                UpdateInternalEmoteReorderDrag(e.GetPosition(EmoteTilesListBox));
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed || pendingTileDragEntry?.Emote == null)
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
            BeginInternalEmoteReorderDrag(dragEntry, currentPosition);
        }

        private void EmoteTilesListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            pendingTileDragEntry = null;
            activeTileDragContainer = null;
            if (!isInternalEmoteReorderDragInProgress)
            {
                return;
            }

            EndInternalEmoteReorderDrag(commit: true);
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

            SetCurrentDragTarget(null);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void EmoteTilesListBox_Drop(object sender, DragEventArgs e)
        {
            if (TryGetDroppedFilePaths(e, out IReadOnlyList<string>? droppedPaths))
            {
                List<string> imagePaths = droppedPaths.Where(IsImageAsset).ToList();
                if (imagePaths.Count > 0)
                {
                    AddEmotesFromDroppedImages(imagePaths);
                    e.Handled = true;
                }
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

        private void BeginInternalEmoteReorderDrag(EmoteTileEntryViewModel dragEntry, Point pointerPosition)
        {
            if (dragEntry.Emote == null)
            {
                return;
            }

            activeTileDragEntry = dragEntry;
            isInternalEmoteReorderDragInProgress = true;
            EmoteTilesListBox.CaptureMouse();

            if (activeTileDragContainer == null)
            {
                EmoteTileEntryViewModel? refreshedEntry = emoteTiles.FirstOrDefault(tile => tile.Emote == dragEntry.Emote);
                if (refreshedEntry != null)
                {
                    activeTileDragContainer = EmoteTilesListBox.ItemContainerGenerator.ContainerFromItem(refreshedEntry) as FrameworkElement;
                }
            }

            if (activeTileDragContainer != null)
            {
                activeTileDragContainer.Opacity = 0.2;
                Point tileTopLeft = activeTileDragContainer.TranslatePoint(new Point(0, 0), EmoteTilesListBox);
                emoteDragGhostPointerOffset = new Point(
                    pointerPosition.X - tileTopLeft.X,
                    pointerPosition.Y - tileTopLeft.Y);
                CreateOrShowDragGhost(activeTileDragContainer, pointerPosition);
            }
            else
            {
                emoteDragGhostPointerOffset = new Point(16, 16);
            }

            SelectEmoteTile(dragEntry.Emote);
            StatusTextBlock.Text = "Dragging emote to reorder...";
        }

        private void UpdateInternalEmoteReorderDrag(Point pointerPosition)
        {
            if (!isInternalEmoteReorderDragInProgress || activeTileDragEntry?.Emote == null)
            {
                return;
            }

            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                EndInternalEmoteReorderDrag(commit: true);
                return;
            }

            UpdateDragGhostPosition(pointerPosition);
            int targetInsertIndex = ResolveTargetInsertIndex(pointerPosition);
            MoveEmoteToIndexWithAnimation(activeTileDragEntry.Emote, targetInsertIndex);
        }

        private void EndInternalEmoteReorderDrag(bool commit)
        {
            if (!isInternalEmoteReorderDragInProgress)
            {
                return;
            }

            isInternalEmoteReorderDragInProgress = false;
            EmoteTilesListBox.ReleaseMouseCapture();

            if (activeTileDragContainer != null)
            {
                activeTileDragContainer.Opacity = 1.0;
            }

            if (emoteDragGhostPopup != null)
            {
                emoteDragGhostPopup.IsOpen = false;
                emoteDragGhostPopup.Child = null;
                emoteDragGhostPopup = null;
            }

            CharacterCreationEmoteViewModel? draggedEmote = activeTileDragEntry?.Emote;
            activeTileDragEntry = null;
            activeTileDragContainer = null;

            if (!commit || draggedEmote == null)
            {
                return;
            }

            UpdateEmoteIndexesOnly();
            SelectEmoteTile(draggedEmote);
            StatusTextBlock.Text = "Emote reordered.";
        }

        private void CreateOrShowDragGhost(FrameworkElement tileContainer, Point pointerPosition)
        {
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(tileContainer.ActualWidth));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(tileContainer.ActualHeight));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(tileContainer);
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            Border ghostBorder = new Border
            {
                Width = tileContainer.ActualWidth,
                Height = tileContainer.ActualHeight,
                Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill },
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 150, 197, 238)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Opacity = 0.9,
                IsHitTestVisible = false
            };

            emoteDragGhostPopup = new Popup
            {
                PlacementTarget = EmoteTilesListBox,
                Placement = PlacementMode.Relative,
                AllowsTransparency = true,
                IsHitTestVisible = false,
                StaysOpen = true,
                Child = ghostBorder
            };

            UpdateDragGhostPosition(pointerPosition);
            emoteDragGhostPopup.IsOpen = true;
        }

        private void UpdateDragGhostPosition(Point pointerPosition)
        {
            if (emoteDragGhostPopup == null)
            {
                return;
            }

            emoteDragGhostPopup.HorizontalOffset = pointerPosition.X - emoteDragGhostPointerOffset.X;
            emoteDragGhostPopup.VerticalOffset = pointerPosition.Y - emoteDragGhostPointerOffset.Y;
        }

        private int ResolveTargetInsertIndex(Point pointerPosition)
        {
            DependencyObject? hit = EmoteTilesListBox.InputHitTest(pointerPosition) as DependencyObject;
            ListBoxItem? targetContainer = FindAncestor<ListBoxItem>(hit);
            if (targetContainer?.DataContext is EmoteTileEntryViewModel entry && entry.Emote != null)
            {
                int hitIndex = emotes.IndexOf(entry.Emote);
                if (hitIndex < 0)
                {
                    return emotes.Count;
                }

                Point pointWithinTile = pointerPosition;
                if (targetContainer != null)
                {
                    pointWithinTile = EmoteTilesListBox.TranslatePoint(pointerPosition, targetContainer);
                }

                bool insertAfter = pointWithinTile.X >= targetContainer.ActualWidth / 2.0
                    || pointWithinTile.Y >= targetContainer.ActualHeight / 2.0;
                return hitIndex + (insertAfter ? 1 : 0);
            }

            List<(double top, double left, CharacterCreationEmoteViewModel emote)> positioned = GetEmoteTilePositions();
            if (positioned.Count == 0)
            {
                return 0;
            }

            List<(double top, double left, CharacterCreationEmoteViewModel emote)> sorted = positioned
                .OrderBy(static item => item.top)
                .ThenBy(static item => item.left)
                .ToList();
            if (pointerPosition.Y <= sorted[0].top)
            {
                return 0;
            }

            CharacterCreationEmoteViewModel last = sorted[^1].emote;
            int lastIndex = emotes.IndexOf(last);
            return Math.Clamp(lastIndex + 1, 0, emotes.Count);
        }

        private void MoveEmoteToIndexWithAnimation(CharacterCreationEmoteViewModel emote, int targetInsertIndex)
        {
            int sourceIndex = emotes.IndexOf(emote);
            if (sourceIndex < 0)
            {
                return;
            }

            int adjustedInsertIndex = Math.Clamp(targetInsertIndex, 0, emotes.Count);
            if (adjustedInsertIndex > sourceIndex)
            {
                adjustedInsertIndex--;
            }

            if (adjustedInsertIndex == sourceIndex)
            {
                return;
            }

            Dictionary<CharacterCreationEmoteViewModel, Point> beforeLayout = CaptureCurrentTileTopLeftPositions();
            EmoteTileEntryViewModel? dragEntry = emoteTiles.FirstOrDefault(tile => tile.Emote == emote);

            emotes.RemoveAt(sourceIndex);
            emotes.Insert(adjustedInsertIndex, emote);

            if (dragEntry != null)
            {
                emoteTiles.Remove(dragEntry);
                emoteTiles.Insert(adjustedInsertIndex, dragEntry);
            }

            UpdateEmoteIndexesOnly();
            EmoteTilesListBox.UpdateLayout();
            AnimateTileLayoutChanges(beforeLayout);
        }

        private Dictionary<CharacterCreationEmoteViewModel, Point> CaptureCurrentTileTopLeftPositions()
        {
            Dictionary<CharacterCreationEmoteViewModel, Point> positions =
                new Dictionary<CharacterCreationEmoteViewModel, Point>();

            foreach (EmoteTileEntryViewModel entry in emoteTiles)
            {
                if (entry.Emote == null)
                {
                    continue;
                }

                ListBoxItem? container = EmoteTilesListBox.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
                if (container == null)
                {
                    continue;
                }

                Point topLeft = container.TranslatePoint(new Point(0, 0), EmoteTilesListBox);
                positions[entry.Emote] = topLeft;
            }

            return positions;
        }

        private void AnimateTileLayoutChanges(Dictionary<CharacterCreationEmoteViewModel, Point> beforeLayout)
        {
            Duration animationDuration = new Duration(TimeSpan.FromMilliseconds(130));
            CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (KeyValuePair<CharacterCreationEmoteViewModel, Point> pair in beforeLayout)
            {
                EmoteTileEntryViewModel? entry = emoteTiles.FirstOrDefault(tile => tile.Emote == pair.Key);
                if (entry == null)
                {
                    continue;
                }

                ListBoxItem? container = EmoteTilesListBox.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
                if (container == null)
                {
                    continue;
                }

                Point newTopLeft = container.TranslatePoint(new Point(0, 0), EmoteTilesListBox);
                Vector delta = pair.Value - newTopLeft;
                if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5)
                {
                    continue;
                }

                TranslateTransform transform = EnsureTileAnimationTransform(container);
                transform.X = delta.X;
                transform.Y = delta.Y;

                DoubleAnimation xAnimation = new DoubleAnimation(0, animationDuration) { EasingFunction = easing };
                DoubleAnimation yAnimation = new DoubleAnimation(0, animationDuration) { EasingFunction = easing };
                transform.BeginAnimation(TranslateTransform.XProperty, xAnimation, HandoffBehavior.SnapshotAndReplace);
                transform.BeginAnimation(TranslateTransform.YProperty, yAnimation, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private TranslateTransform EnsureTileAnimationTransform(UIElement element)
        {
            if (emoteTileAnimationTransforms.TryGetValue(element, out TranslateTransform? existing))
            {
                return existing;
            }

            TranslateTransform animationTransform = new TranslateTransform();
            if (element.RenderTransform == null || element.RenderTransform == Transform.Identity)
            {
                element.RenderTransform = animationTransform;
            }
            else if (element.RenderTransform is TransformGroup existingGroup)
            {
                existingGroup.Children.Add(animationTransform);
            }
            else
            {
                TransformGroup group = new TransformGroup();
                group.Children.Add(element.RenderTransform);
                group.Children.Add(animationTransform);
                element.RenderTransform = group;
            }

            emoteTileAnimationTransforms[element] = animationTransform;
            return animationTransform;
        }

        private List<(double top, double left, CharacterCreationEmoteViewModel emote)> GetEmoteTilePositions()
        {
            List<(double top, double left, CharacterCreationEmoteViewModel emote)> positions =
                new List<(double top, double left, CharacterCreationEmoteViewModel emote)>();
            foreach (EmoteTileEntryViewModel entry in emoteTiles)
            {
                if (entry.Emote == null)
                {
                    continue;
                }

                ListBoxItem? container = EmoteTilesListBox.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
                if (container == null)
                {
                    continue;
                }

                Point point = container.TranslatePoint(new Point(0, 0), EmoteTilesListBox);
                positions.Add((point.Y, point.X, entry.Emote));
            }

            return positions;
        }

        private void UpdateEmoteIndexesOnly()
        {
            for (int i = 0; i < emotes.Count; i++)
            {
                emotes[i].Index = i + 1;
                emotes[i].RefreshDisplayName();
            }
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

        private void EnsureEmotePreviewPlayersInitialized()
        {
            foreach (CharacterCreationEmoteViewModel emote in emotes)
            {
                foreach (string zone in new[] { "preanim", "anim" })
                {
                    string key = GetEmotePreviewPlayerKey(emote, zone);
                    if (emotePreviewPlayers.ContainsKey(key))
                    {
                        continue;
                    }

                    string? sourcePath = string.Equals(zone, "preanim", StringComparison.OrdinalIgnoreCase)
                        ? emote.PreAnimationAssetSourcePath
                        : emote.AnimationAssetSourcePath;
                    if (string.IsNullOrWhiteSpace(sourcePath))
                    {
                        continue;
                    }

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

            if (!Ao2AnimationPreview.IsPotentialAnimatedPath(resolvedSourcePath))
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
            return Ao2AnimationPreview.LoadStaticPreviewImage(path, decodePixelWidth: EmotePreviewDecodePixelWidth);
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
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

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
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

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
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
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
            bool suppressSfxTriggerUntilPlaybackAdvance = false;
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
                    bool isPlaying = animationPreviewController.IsPlaying;
                    double delayMs = ReadDelayMs();
                    if (positionMs < previousPositionMs)
                    {
                        sfxTriggeredInCycle = false;
                        suppressSfxTriggerUntilPlaybackAdvance = false;
                    }

                    if (suppressSfxTriggerUntilPlaybackAdvance && isPlaying && positionMs > previousPositionMs)
                    {
                        suppressSfxTriggerUntilPlaybackAdvance = false;
                    }

                    bool crossedDelay = previousPositionMs < delayMs && positionMs >= delayMs;
                    previousPositionMs = positionMs;
                    if (isPlaying
                        && !sfxTriggeredInCycle
                        && !suppressSfxTriggerUntilPlaybackAdvance
                        && crossedDelay)
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
                    suppressSfxTriggerUntilPlaybackAdvance = true;
                }
            };
            previewLoopCheckBox.Checked += (_, _) => animationPreviewController?.SetLoop(true);
            previewLoopCheckBox.Unchecked += (_, _) => animationPreviewController?.SetLoop(false);

            DockPanel buttons = BuildDialogButtons(dialog);
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

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
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);

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
            int automaticSelectedTabIndex = 0;

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
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    if (TryFindResource("DialogTabControlStyle") is Style dialogTabControlStyle)
                    {
                        automaticTabs.Style = dialogTabControlStyle;
                    }
                    Style? dialogTabItemStyle = TryFindResource("DialogTabItemStyle") as Style;

                    StackPanel backgroundTabContent = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
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

                    StackPanel emoteTabContent = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
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
                    Border cutPreviewBorder = new Border
                    {
                        Width = 120,
                        Height = 120,
                        Margin = new Thickness(0, 8, 0, 0),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(72, 92, 112)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(110, 20, 20, 20)),
                        Padding = new Thickness(4)
                    };
                    Grid cutPreviewGrid = new Grid();
                    Image cutPreviewImage = new Image
                    {
                        Source = config.AutomaticCutEmoteImage,
                        Stretch = Stretch.Uniform
                    };
                    TextBlock cutPreviewEmptyText = new TextBlock
                    {
                        Text = "No cutout",
                        Foreground = new SolidColorBrush(Color.FromRgb(170, 188, 206)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = config.AutomaticCutEmoteImage == null ? Visibility.Visible : Visibility.Collapsed
                    };
                    cutPreviewGrid.Children.Add(cutPreviewImage);
                    cutPreviewGrid.Children.Add(cutPreviewEmptyText);
                    cutPreviewBorder.Child = cutPreviewGrid;
                    StackPanel cutPanel = new StackPanel();
                    cutPanel.Children.Add(cutButton);
                    cutPanel.Children.Add(cutStatus);
                    cutPanel.Children.Add(cutPreviewBorder);
                    AddSimpleField(emoteTabContent, "Emote cutting", cutPanel);

                    StackPanel effectTabContent = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
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
                    automaticTabs.SelectionChanged += (_, _) =>
                    {
                        automaticSelectedTabIndex = automaticTabs.SelectedIndex;
                    };
                    automaticTabs.SelectedIndex = Math.Clamp(automaticSelectedTabIndex, 0, Math.Max(0, automaticTabs.Items.Count - 1));
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
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
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

        private static string BuildCutSelectionStorageKey(CharacterCreationEmoteViewModel emote)
        {
            string emoteName = (emote.Name ?? string.Empty).Trim();
            string preanimName = Path.GetFileName(emote.PreAnimationAssetSourcePath ?? string.Empty);
            string animationName = Path.GetFileName(emote.AnimationAssetSourcePath ?? string.Empty);
            string idleName = Path.GetFileName(emote.FinalAnimationIdleAssetSourcePath ?? string.Empty);
            string talkingName = Path.GetFileName(emote.FinalAnimationTalkingAssetSourcePath ?? string.Empty);
            return $"{emote.Index}|{emoteName}|{preanimName}|{animationName}|{idleName}|{talkingName}";
        }

        private bool TryGetSavedCutSelectionForEmote(
            CharacterCreationEmoteViewModel emote,
            [NotNullWhen(true)] out CutSelectionState? state)
        {
            string emoteCutSelectionKey = BuildCutSelectionStorageKey(emote);
            return savedCutSelectionByEmoteKey.TryGetValue(emoteCutSelectionKey, out state);
        }

        private void PersistCutSelectionState(string emoteKey, CutSelectionState state)
        {
            SaveFile.Data.CharacterCreatorCutSelections[emoteKey] = new CharacterCreatorCutSelectionState
            {
                SourcePath = state.SourcePath ?? string.Empty,
                X = state.NormalizedSelection.X,
                Y = state.NormalizedSelection.Y,
                Width = state.NormalizedSelection.Width,
                Height = state.NormalizedSelection.Height
            };
            SaveFile.Save();
        }

        private BitmapSource? ShowEmoteCuttingDialog(
            CharacterCreationEmoteViewModel emote,
            BitmapSource? existingCutout,
            Rect? suggestedNormalizedSelection = null)
        {
            string emoteCutSelectionKey = BuildCutSelectionStorageKey(emote);
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
            string emoteCuttingPopupKey = BuildPopupStateKey("EmoteCutting");
            ApplyPopupState(dialog, emoteCuttingPopupKey);
            Grid grid = BuildDialogGrid(3);

            AutoCompleteDropdownField sourceDropdown = CreateDialogAutoCompleteField(
                sourceOptions.Select(static option => option.Name),
                sourceOptions[0].Name,
                "Choose emote source image for cutting.",
                isReadOnly: true);
            AddDialogFieldContainer(grid, 0, "Source", "Current emote assets used for cutout extraction.", sourceDropdown);

            Border previewBorder = new Border
            {
                MinHeight = 120,
                Height = Math.Clamp(SaveFile.Data.CharacterCreatorCuttingPreviewHeight, 120, 520),
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(145, 14, 14, 14)),
                Padding = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid previewGrid = new Grid();
            Image previewImage = new Image { Stretch = Stretch.Uniform };
            Canvas selectionCanvas = new Canvas { Background = Brushes.Transparent };
            System.Windows.Shapes.Rectangle lastSelectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(226, 160, 96)),
                StrokeThickness = 1.3,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                Visibility = Visibility.Collapsed
            };
            System.Windows.Shapes.Rectangle selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(148, 220, 255)),
                Fill = new SolidColorBrush(Color.FromArgb(42, 96, 182, 226)),
                StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed
            };
            selectionCanvas.Children.Add(lastSelectionRect);
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

            Border previewResizeGrip = new Border
            {
                Height = 8,
                Margin = new Thickness(0, 2, 0, 4),
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNS,
                Child = new Border
                {
                    Height = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(92, 90, 115, 138))
                }
            };

            bool resizingPreview = false;
            Point previewResizeStartPoint = default;
            double previewResizeStartHeight = previewBorder.Height;
            previewResizeGrip.MouseLeftButtonDown += (_, e) =>
            {
                resizingPreview = true;
                previewResizeStartPoint = e.GetPosition(dialog);
                previewResizeStartHeight = previewBorder.Height;
                previewResizeGrip.CaptureMouse();
                e.Handled = true;
            };
            previewResizeGrip.MouseMove += (_, e) =>
            {
                if (!resizingPreview || e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                Point current = e.GetPosition(dialog);
                double next = Math.Clamp(previewResizeStartHeight + (current.Y - previewResizeStartPoint.Y), 120, 520);
                previewBorder.Height = next;
                SaveFile.Data.CharacterCreatorCuttingPreviewHeight = next;
                SaveFile.Save();
                e.Handled = true;
            };
            previewResizeGrip.MouseLeftButtonUp += (_, e) =>
            {
                if (!resizingPreview)
                {
                    return;
                }

                resizingPreview = false;
                previewResizeGrip.ReleaseMouseCapture();
                e.Handled = true;
            };

            StackPanel previewPanel = new StackPanel();
            previewPanel.Children.Add(previewBorder);
            previewPanel.Children.Add(previewResizeGrip);
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
                Source = null
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
            Rect normalizedSelectionBounds = Rect.Empty;
            BitmapSource? currentSelectionCutout = null;
            string currentSourceName = sourceOptions[0].Name;
            string currentSourcePath = sourceOptions[0].Path;
            Rect? transientSuggestedSelection = suggestedNormalizedSelection;

            void UpdateLastCutoutPreviewForCurrentSource()
            {
                if (existingCutout == null)
                {
                    lastCutoutImage.Source = null;
                    return;
                }

                bool hasMatchingSavedSelection = savedCutSelectionByEmoteKey.TryGetValue(emoteCutSelectionKey, out CutSelectionState? saved)
                    && string.Equals(saved.SourcePath, currentSourcePath, StringComparison.OrdinalIgnoreCase)
                    && saved.NormalizedSelection.Width > 0
                    && saved.NormalizedSelection.Height > 0;
                lastCutoutImage.Source = hasMatchingSavedSelection ? existingCutout : null;
            }

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

            bool TryGetSavedSelectionForCurrentSource(out Rect normalizedSelection)
            {
                if (savedCutSelectionByEmoteKey.TryGetValue(emoteCutSelectionKey, out CutSelectionState? saved)
                    && string.Equals(saved.SourcePath, currentSourcePath, StringComparison.OrdinalIgnoreCase)
                    && saved.NormalizedSelection.Width > 0
                    && saved.NormalizedSelection.Height > 0)
                {
                    normalizedSelection = saved.NormalizedSelection;
                    return true;
                }

                normalizedSelection = Rect.Empty;
                return false;
            }

            void UpdateLastSelectionVisual(Rect? normalizedSelection)
            {
                if (currentFrame == null || !normalizedSelection.HasValue)
                {
                    lastSelectionRect.Visibility = Visibility.Collapsed;
                    return;
                }

                Rect viewport = ComputeImageViewport(currentFrame);
                Rect normalized = normalizedSelection.Value;
                Rect rect = new Rect(
                    viewport.X + (normalized.X * viewport.Width),
                    viewport.Y + (normalized.Y * viewport.Height),
                    normalized.Width * viewport.Width,
                    normalized.Height * viewport.Height);
                if (rect.Width < 2 || rect.Height < 2)
                {
                    lastSelectionRect.Visibility = Visibility.Collapsed;
                    return;
                }

                Canvas.SetLeft(lastSelectionRect, rect.X);
                Canvas.SetTop(lastSelectionRect, rect.Y);
                lastSelectionRect.Width = rect.Width;
                lastSelectionRect.Height = rect.Height;
                lastSelectionRect.Visibility = Visibility.Visible;
            }

            void ApplySavedSelectionAsCurrent()
            {
                if (currentFrame == null)
                {
                    selectionBounds = Rect.Empty;
                    normalizedSelectionBounds = Rect.Empty;
                    UpdateSelectionVisual();
                    UpdateCurrentCutoutPreview();
                    UpdateLastSelectionVisual(null);
                    UpdateLastCutoutPreviewForCurrentSource();
                    return;
                }

                Rect normalized;
                if (TryGetSavedSelectionForCurrentSource(out Rect persistedSelection))
                {
                    normalized = persistedSelection;
                }
                else if (transientSuggestedSelection.HasValue
                    && transientSuggestedSelection.Value.Width > 0
                    && transientSuggestedSelection.Value.Height > 0)
                {
                    normalized = transientSuggestedSelection.Value;
                }
                else
                {
                    selectionBounds = Rect.Empty;
                    normalizedSelectionBounds = Rect.Empty;
                    UpdateSelectionVisual();
                    UpdateCurrentCutoutPreview();
                    UpdateLastSelectionVisual(null);
                    UpdateLastCutoutPreviewForCurrentSource();
                    return;
                }

                Rect viewport = ComputeImageViewport(currentFrame);
                normalizedSelectionBounds = normalized;
                selectionBounds = new Rect(
                    viewport.X + (normalized.X * viewport.Width),
                    viewport.Y + (normalized.Y * viewport.Height),
                    normalized.Width * viewport.Width,
                    normalized.Height * viewport.Height);
                UpdateSelectionVisual();
                UpdateCurrentCutoutPreview();
                UpdateLastSelectionVisual(normalized);
                UpdateLastCutoutPreviewForCurrentSource();
            }

            void UpdateSelectionBoundsFromNormalized()
            {
                if (currentFrame == null || normalizedSelectionBounds.IsEmpty || normalizedSelectionBounds.Width <= 0 || normalizedSelectionBounds.Height <= 0)
                {
                    selectionBounds = Rect.Empty;
                    UpdateSelectionVisual();
                    UpdateCurrentCutoutPreview();
                    return;
                }

                Rect viewport = ComputeImageViewport(currentFrame);
                selectionBounds = new Rect(
                    viewport.X + (normalizedSelectionBounds.X * viewport.Width),
                    viewport.Y + (normalizedSelectionBounds.Y * viewport.Height),
                    normalizedSelectionBounds.Width * viewport.Width,
                    normalizedSelectionBounds.Height * viewport.Height);
                UpdateSelectionVisual();
                UpdateCurrentCutoutPreview();
            }

            void UpdateNormalizedSelectionFromBounds()
            {
                if (currentFrame == null || selectionBounds.IsEmpty)
                {
                    normalizedSelectionBounds = Rect.Empty;
                    return;
                }

                Rect viewport = ComputeImageViewport(currentFrame);
                Rect selection = Rect.Intersect(selectionBounds, viewport);
                if (selection.IsEmpty || selection.Width < 2 || selection.Height < 2)
                {
                    normalizedSelectionBounds = Rect.Empty;
                    return;
                }

                normalizedSelectionBounds = new Rect(
                    Math.Clamp((selection.X - viewport.X) / viewport.Width, 0, 1),
                    Math.Clamp((selection.Y - viewport.Y) / viewport.Height, 0, 1),
                    Math.Clamp(selection.Width / viewport.Width, 0, 1),
                    Math.Clamp(selection.Height / viewport.Height, 0, 1));
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
                currentSourcePath = path ?? string.Empty;
                previewController?.Dispose();
                previewController = null;
                currentFrame = null;
                selectionBounds = Rect.Empty;
                normalizedSelectionBounds = Rect.Empty;
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
                        UpdateSelectionBoundsFromNormalized();
                        if (TryGetSavedSelectionForCurrentSource(out Rect normalized))
                        {
                            UpdateLastSelectionVisual(normalized);
                        }
                        else
                        {
                            UpdateLastSelectionVisual(null);
                        }
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

                ApplySavedSelectionAsCurrent();

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
                    currentSourceName = selected.Name;
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
                normalizedSelectionBounds = Rect.Empty;
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
                UpdateNormalizedSelectionFromBounds();
                transientSuggestedSelection = null;
                UpdateSelectionVisual();
                UpdateCurrentCutoutPreview();
            };
            selectionCanvas.MouseLeftButtonUp += (_, _) =>
            {
                dragging = false;
                if (selectionCanvas.IsMouseCaptured)
                {
                    selectionCanvas.ReleaseMouseCapture();
                }

                UpdateNormalizedSelectionFromBounds();
                transientSuggestedSelection = null;
                UpdateCurrentCutoutPreview();
            };
            selectionCanvas.SizeChanged += (_, _) =>
            {
                UpdateSelectionBoundsFromNormalized();
                if (TryGetSavedSelectionForCurrentSource(out Rect normalized))
                {
                    UpdateLastSelectionVisual(normalized);
                }
                else
                {
                    UpdateLastSelectionVisual(null);
                }
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
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            bool? result;
            try
            {
                result = dialog.ShowDialog();
            }
            finally
            {
                PersistPopupState(dialog, emoteCuttingPopupKey);
            }
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
                SaveCutSelectionState(emoteCutSelectionKey, currentSourcePath, selection, viewport);
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
            SaveCutSelectionState(emoteCutSelectionKey, currentSourcePath, selection, viewport);
            return cropped;
        }

        private void SaveCutSelectionState(string emoteKey, string sourceName, Rect selection, Rect viewport)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0)
            {
                return;
            }

            Rect normalized = new Rect(
                Math.Clamp((selection.X - viewport.X) / viewport.Width, 0, 1),
                Math.Clamp((selection.Y - viewport.Y) / viewport.Height, 0, 1),
                Math.Clamp(selection.Width / viewport.Width, 0, 1),
                Math.Clamp(selection.Height / viewport.Height, 0, 1));
            if (normalized.Width <= 0 || normalized.Height <= 0)
            {
                return;
            }

            CutSelectionState state = new CutSelectionState
            {
                SourcePath = sourceName,
                NormalizedSelection = normalized
            };
            savedCutSelectionByEmoteKey[emoteKey] = state;
            PersistCutSelectionState(emoteKey, state);
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
            BuildStyledDialogContent(dialog, dialog.Title, root, buttons);
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return CurrentColor();
        }

        private Window CreateEmoteDialog(string title, double width, double height)
        {
            Window? ownerWindow = HostWindow ?? this;
            GenericOceanyaWindow dialog = new GenericOceanyaWindow
            {
                Owner = ownerWindow,
                Title = title,
                HeaderText = title,
                Width = width,
                Height = height,
                MinWidth = Math.Min(width, 520),
                MinHeight = Math.Min(height, 240),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = true,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyMargin = new Thickness(0)
            };

            return dialog;
        }

        private static string BuildPopupStateKey(string id)
        {
            string typeName = typeof(AOCharacterFileCreatorWindow).FullName ?? nameof(AOCharacterFileCreatorWindow);
            return $"{typeName}|{id}";
        }

        private static VisualizerWindowState CapturePopupWindowState(Window window)
        {
            Rect bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.Width, window.Height)
                : window.RestoreBounds;
            double capturedWidth = bounds.Width > 0 ? bounds.Width : window.Width;
            double capturedHeight = bounds.Height > 0 ? bounds.Height : window.Height;
            return new VisualizerWindowState
            {
                Width = Math.Max(window.MinWidth, capturedWidth),
                Height = Math.Max(window.MinHeight, capturedHeight),
                Left = bounds.X,
                Top = bounds.Y,
                IsMaximized = window.WindowState == WindowState.Maximized
            };
        }

        private void ApplyPopupState(Window window, string popupStateKey)
        {
            if (!SaveFile.Data.PopupWindowStates.TryGetValue(popupStateKey, out VisualizerWindowState? state) || state == null)
            {
                return;
            }

            window.Width = Math.Max(window.MinWidth, state.Width);
            window.Height = Math.Max(window.MinHeight, state.Height);
            if (state.Left.HasValue && state.Top.HasValue)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = state.Left.Value;
                window.Top = state.Top.Value;
            }

            if (state.IsMaximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }

        private static void PersistPopupState(Window window, string popupStateKey)
        {
            SaveFile.Data.PopupWindowStates[popupStateKey] = CapturePopupWindowState(window);
            SaveFile.Save();
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

        private DockPanel BuildDialogButtonsReadOnly(Window dialog)
        {
            DockPanel panel = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(12, 2, 12, 4)
            };

            Button closeButton = CreateDialogButton("Close", isPrimary: false);
            closeButton.Width = 120;
            closeButton.IsCancel = true;
            closeButton.IsDefault = true;
            closeButton.Click += (_, _) => dialog.DialogResult = false;

            StackPanel buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttonRow.Children.Add(closeButton);
            panel.Children.Add(buttonRow);
            return panel;
        }

        private void BuildStyledDialogContent(Window dialog, string title, UIElement body, UIElement buttons)
        {
            Border panel = new Border
            {
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(160, 16, 16, 16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(47, 74, 94)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10)
            };

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
            if (dialog is GenericOceanyaWindow genericDialog)
            {
                genericDialog.HeaderText = title;
                genericDialog.BodyMargin = new Thickness(0);
                genericDialog.BodyContent = panel;
                return;
            }

            dialog.Content = panel;
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
            return new List<string>
            {
                "None",
                "Solid color",
                "Upload"
            };
        }

        private static string GetAutomaticBackgroundSelectionName(ButtonIconGenerationConfig config)
        {
            return config.AutomaticBackgroundMode switch
            {
                ButtonAutomaticBackgroundMode.SolidColor => "Solid color",
                ButtonAutomaticBackgroundMode.Upload => "Upload",
                _ => "None"
            };
        }

        private static void ApplyAutomaticBackgroundSelection(string selectionText, ButtonIconGenerationConfig config)
        {
            string selected = (selectionText ?? string.Empty).Trim();
            if (string.Equals(selected, "Solid color", StringComparison.OrdinalIgnoreCase))
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
            generatedCharacterIconImage = null;
            CharacterIconPreviewImage.Source = TryLoadPreviewImage(dialog.FileName);
            CharacterIconEmptyText.Visibility = CharacterIconPreviewImage.Source == null
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatusTextBlock.Text = "Character icon selected.";
        }

        private void CharacterIconPreviewBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CharacterIconFromFileButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        private void CharacterIconPreviewBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            CharacterIconPreviewBorder.Background = new SolidColorBrush(Color.FromArgb(62, 62, 72, 86));
            CharacterIconPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(92, 118, 146));
        }

        private void CharacterIconPreviewBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            CharacterIconPreviewBorder.Background = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
            CharacterIconPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(85, 64, 75, 88));
        }

        private void CharacterIconPreviewBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CharacterIconPreviewBorder.Background = new SolidColorBrush(Color.FromArgb(82, 36, 44, 54));
            CharacterIconPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(115, 141, 166));
        }

        private void CharacterIconFromEmoteButton_Click(object sender, RoutedEventArgs e)
        {
            List<CharacterCreationEmoteViewModel> eligibleEmotes = emotes
                .Where(static emote => HasAnyCuttableAsset(emote))
                .ToList();
            if (eligibleEmotes.Count == 0)
            {
                OceanyaMessageBox.Show(
                    this,
                    "No emotes have source assets available yet. Add preanim/idle/talking assets first.",
                    "Character Icon",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            CharacterCreationEmoteViewModel? selectedEmote = ShowEmoteSelectionDialogForCharacterIcon(eligibleEmotes);
            if (selectedEmote == null)
            {
                return;
            }

            BitmapSource? generated = ShowCharacterIconGeneratorDialogFromEmote(selectedEmote);
            if (generated == null)
            {
                return;
            }

            generatedCharacterIconImage = CloneBitmapSource(generated);
            selectedCharacterIconSourcePath = string.Empty;
            CharacterIconPreviewImage.Source = generatedCharacterIconImage;
            CharacterIconEmptyText.Visibility = CharacterIconPreviewImage.Source == null
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatusTextBlock.Text = "Character icon generated from emote frame.";
        }

        private BitmapSource? ShowCharacterIconGeneratorDialogFromEmote(CharacterCreationEmoteViewModel emote)
        {
            Window dialog = CreateEmoteDialog("Character Icon From Emote", 860, 700);
            Grid grid = BuildDialogGrid(3);

            ButtonIconGenerationConfig config = new ButtonIconGenerationConfig
            {
                Mode = ButtonIconMode.Automatic,
                EffectsMode = ButtonEffectsGenerationMode.Darken,
                DarknessPercent = 50,
                OpacityPercent = 75
            };

            AutoCompleteDropdownField modeDropdown = CreateDialogAutoCompleteField(
                new[] { "Single image", "Automatic" },
                "Automatic",
                "Generate character icon from a single image or automatic emote extraction.",
                isReadOnly: true);
            AddDialogFieldContainer(grid, 0, "Mode", "Character icon generation mode.", modeDropdown);
            Border fieldsHost = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 80, 98)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(72, 12, 18, 24)),
                Padding = new Thickness(10)
            };
            Grid.SetRow(fieldsHost, 1);
            grid.Children.Add(fieldsHost);

            Image previewImage = new Image { Stretch = Stretch.Uniform };
            TextBlock previewEmpty = CreatePreviewEmptyText("char icon preview");
            AddDialogFieldContainer(grid, 2, "Preview", "Live preview for generated character icon.", CreateButtonPreviewCard("char_icon", previewImage, previewEmpty));

            void RefreshPreview()
            {
                if (TryBuildButtonIconPair(config, out BitmapSource? onImage, out _, out _)
                    && onImage != null)
                {
                    previewImage.Source = onImage;
                    previewEmpty.Visibility = Visibility.Collapsed;
                }
                else
                {
                    previewImage.Source = null;
                    previewEmpty.Visibility = Visibility.Visible;
                }
            }

            void RefreshFields()
            {
                StackPanel panel = new StackPanel { Margin = new Thickness(6) };
                if (config.Mode == ButtonIconMode.SingleImage)
                {
                    Grid sourceGrid = new Grid();
                    sourceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    sourceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Button sourceUploadButton = CreateDialogButton("Select image...", isPrimary: false);
                    sourceUploadButton.Width = 130;
                    sourceUploadButton.Click += (_, _) =>
                    {
                        OpenFileDialog picker = new OpenFileDialog
                        {
                            Title = "Select character icon source",
                            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
                        };
                        if (picker.ShowDialog() == true)
                        {
                            config.SingleImagePath = picker.FileName;
                            RefreshFields();
                        }
                    };
                    TextBlock sourcePathText = new TextBlock
                    {
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                        Text = string.IsNullOrWhiteSpace(config.SingleImagePath) ? "No image selected" : Path.GetFileName(config.SingleImagePath)
                    };
                    Grid.SetColumn(sourcePathText, 1);
                    sourceGrid.Children.Add(sourceUploadButton);
                    sourceGrid.Children.Add(sourcePathText);
                    AddSimpleField(panel, "Source image", sourceGrid);
                    AddEffectsFields(panel);
                }
                else
                {
                    TabControl tabs = new TabControl
                    {
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    if (TryFindResource("DialogTabControlStyle") is Style tabControlStyle)
                    {
                        tabs.Style = tabControlStyle;
                    }

                    if (TryFindResource("DialogTabItemStyle") is Style tabItemStyle)
                    {
                        tabs.ItemContainerStyle = tabItemStyle;
                    }

                    TabItem backgroundTab = new TabItem { Header = "Background" };
                    StackPanel backgroundContent = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
                    AutoCompleteDropdownField backgroundDropdown = CreateDialogAutoCompleteField(
                        GetAutomaticBackgroundOptions(),
                        GetAutomaticBackgroundSelectionName(config),
                        "Background config",
                        isReadOnly: true);
                    backgroundDropdown.TextValueChanged += (_, _) =>
                    {
                        ApplyAutomaticBackgroundSelection(backgroundDropdown.Text, config);
                        RefreshFields();
                    };
                    AddSimpleField(backgroundContent, "Background config", backgroundDropdown);

                    if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.SolidColor)
                    {
                        Button color = CreateDialogButton("Pick color...", isPrimary: false);
                        color.Width = 130;
                        color.Click += (_, _) =>
                        {
                            Color? picked = ShowAdvancedColorPickerDialog(config.AutomaticSolidColor);
                            if (picked.HasValue)
                            {
                                config.AutomaticSolidColor = picked.Value;
                                RefreshFields();
                            }
                        };
                        AddSimpleField(backgroundContent, "Solid color", color);
                    }
                    else if (config.AutomaticBackgroundMode == ButtonAutomaticBackgroundMode.Upload)
                    {
                        Grid backgroundUploadGrid = new Grid();
                        backgroundUploadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        backgroundUploadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        Button uploadBackground = CreateDialogButton("Upload background...", isPrimary: false);
                        uploadBackground.Width = 170;
                        uploadBackground.Click += (_, _) =>
                        {
                            OpenFileDialog picker = new OpenFileDialog
                            {
                                Title = "Select background image",
                                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
                            };
                            if (picker.ShowDialog() == true)
                            {
                                config.AutomaticBackgroundUploadPath = picker.FileName;
                                RefreshFields();
                            }
                        };
                        TextBlock backgroundPathText = new TextBlock
                        {
                            Margin = new Thickness(10, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                            Text = string.IsNullOrWhiteSpace(config.AutomaticBackgroundUploadPath)
                                ? "No image selected"
                                : Path.GetFileName(config.AutomaticBackgroundUploadPath)
                        };
                        Grid.SetColumn(backgroundPathText, 1);
                        backgroundUploadGrid.Children.Add(uploadBackground);
                        backgroundUploadGrid.Children.Add(backgroundPathText);
                        AddSimpleField(backgroundContent, "Background image", backgroundUploadGrid);
                    }

                    backgroundTab.Content = backgroundContent;
                    tabs.Items.Add(backgroundTab);

                    TabItem emoteTab = new TabItem { Header = "Emote" };
                    StackPanel emoteContent = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
                    Button cutButton = CreateDialogButton("Open Emote Cutting...", isPrimary: false);
                    cutButton.Width = 180;
                    cutButton.Click += (_, _) =>
                    {
                        BitmapSource? cut = ShowEmoteCuttingDialog(emote, config.AutomaticCutEmoteImage);
                        if (cut != null)
                        {
                            config.AutomaticCutEmoteImage = cut;
                            RefreshPreview();
                            RefreshFields();
                        }
                    };
                    Border cutPreviewCard = new Border
                    {
                        Width = 120,
                        Height = 120,
                        Margin = new Thickness(0, 8, 0, 0),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(72, 92, 112)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(110, 20, 20, 20)),
                        Padding = new Thickness(4),
                        Child = new Image
                        {
                            Source = config.AutomaticCutEmoteImage,
                            Stretch = Stretch.Uniform
                        }
                    };
                    AddSimpleField(emoteContent, "Emote cutting", cutButton);
                    AddSimpleField(emoteContent, "Current cutout", cutPreviewCard);
                    emoteTab.Content = emoteContent;
                    tabs.Items.Add(emoteTab);

                    TabItem effectTab = new TabItem { Header = "Effect" };
                    StackPanel effectContent = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
                    AddEffectsFields(effectContent);
                    effectTab.Content = effectContent;
                    tabs.Items.Add(effectTab);

                    panel.Children.Add(tabs);
                }

                fieldsHost.Child = panel;
                RefreshPreview();
            }

            void AddEffectsFields(Panel panel)
            {
                AutoCompleteDropdownField effectsDropdown = CreateDialogAutoCompleteField(
                    ButtonEffectsGenerationOptionNames,
                    GetButtonEffectsGenerationName(config.EffectsMode),
                    "Effects generation",
                    isReadOnly: true);
                effectsDropdown.TextValueChanged += (_, _) =>
                {
                    config.EffectsMode = ParseButtonEffectsGenerationMode(effectsDropdown.Text);
                    RefreshFields();
                };
                AddSimpleField(panel, "Effects generation", effectsDropdown);

                if (config.EffectsMode == ButtonEffectsGenerationMode.ReduceOpacity)
                {
                    Slider opacitySlider = new Slider
                    {
                        Minimum = 0,
                        Maximum = 100,
                        Value = config.OpacityPercent,
                        TickFrequency = 1,
                        IsSnapToTickEnabled = true
                    };
                    opacitySlider.ValueChanged += (_, _) =>
                    {
                        config.OpacityPercent = (int)Math.Round(opacitySlider.Value);
                        RefreshPreview();
                    };
                    AddSimpleField(panel, "Opacity", opacitySlider);
                }
                else if (config.EffectsMode == ButtonEffectsGenerationMode.Darken)
                {
                    Slider darknessSlider = new Slider
                    {
                        Minimum = 0,
                        Maximum = 100,
                        Value = config.DarknessPercent,
                        TickFrequency = 1,
                        IsSnapToTickEnabled = true
                    };
                    darknessSlider.ValueChanged += (_, _) =>
                    {
                        config.DarknessPercent = (int)Math.Round(darknessSlider.Value);
                        RefreshPreview();
                    };
                    AddSimpleField(panel, "Darkness", darknessSlider);
                }
                else if (config.EffectsMode == ButtonEffectsGenerationMode.Overlay)
                {
                    Grid overlayGrid = new Grid();
                    overlayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    overlayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Button overlayButton = CreateDialogButton("Select overlay...", isPrimary: false);
                    overlayButton.Width = 140;
                    overlayButton.Click += (_, _) =>
                    {
                        OpenFileDialog picker = new OpenFileDialog
                        {
                            Title = "Select overlay image",
                            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.apng|All files (*.*)|*.*"
                        };
                        if (picker.ShowDialog() == true)
                        {
                            config.OverlayImagePath = picker.FileName;
                            RefreshFields();
                        }
                    };
                    TextBlock overlayPathText = new TextBlock
                    {
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 224)),
                        Text = string.IsNullOrWhiteSpace(config.OverlayImagePath)
                            ? "No image selected"
                            : Path.GetFileName(config.OverlayImagePath)
                    };
                    Grid.SetColumn(overlayPathText, 1);
                    overlayGrid.Children.Add(overlayButton);
                    overlayGrid.Children.Add(overlayPathText);
                    AddSimpleField(panel, "Overlay", overlayGrid);
                }
            }

            modeDropdown.TextValueChanged += (_, _) =>
            {
                config.Mode = string.Equals(modeDropdown.Text?.Trim(), "Single image", StringComparison.OrdinalIgnoreCase)
                    ? ButtonIconMode.SingleImage
                    : ButtonIconMode.Automatic;
                RefreshFields();
            };
            RefreshFields();

            DockPanel buttons = BuildDialogButtons(dialog);
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return TryBuildButtonIconPair(config, out BitmapSource? onImage, out _, out _)
                ? onImage
                : null;
        }

        private CharacterCreationEmoteViewModel? ShowEmoteSelectionDialogForCharacterIcon(IReadOnlyList<CharacterCreationEmoteViewModel> eligibleEmotes)
        {
            if (eligibleEmotes.Count == 1)
            {
                return eligibleEmotes[0];
            }

            Window dialog = CreateEmoteDialog("Select Emote", 880, 620);
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock description = new TextBlock
            {
                Text = "Choose the emote source used for character icon extraction.",
                Foreground = new SolidColorBrush(Color.FromRgb(210, 222, 236)),
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(description, 0);
            grid.Children.Add(description);

            ObservableCollection<EmoteTileEntryViewModel> entries = new ObservableCollection<EmoteTileEntryViewModel>(
                eligibleEmotes.Select(static emote => new EmoteTileEntryViewModel
                {
                    IsAddTile = false,
                    Emote = emote
                }));

            ListBox emoteList = new ListBox
            {
                Background = new SolidColorBrush(Color.FromArgb(36, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 74, 94, 114)),
                BorderThickness = new Thickness(1),
                SelectionMode = SelectionMode.Single,
                ItemsSource = entries
            };
            FrameworkElementFactory wrapPanelFactory = new FrameworkElementFactory(typeof(WrapPanel));
            wrapPanelFactory.SetValue(WrapPanel.MarginProperty, new Thickness(6));
            emoteList.ItemsPanel = new ItemsPanelTemplate(wrapPanelFactory);
            Style selectorItemStyle = new Style(typeof(ListBoxItem));
            selectorItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            selectorItemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(6)));
            selectorItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(26, 0, 0, 0))));
            selectorItemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(90, 66, 86, 106))));
            selectorItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            Trigger selectorHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            selectorHover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(58, 50, 70, 90))));
            selectorHover.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(140, 88, 122, 152))));
            selectorItemStyle.Triggers.Add(selectorHover);
            Trigger selectorSelected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectorSelected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(76, 56, 94, 126))));
            selectorSelected.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(182, 128, 178, 220))));
            selectorItemStyle.Triggers.Add(selectorSelected);
            emoteList.ItemContainerStyle = selectorItemStyle;
            emoteList.ItemTemplate = BuildEmoteSelectorTemplate();

            emoteList.SelectedItem = entries[0];
            Grid.SetRow(emoteList, 1);
            grid.Children.Add(emoteList);

            DockPanel buttons = BuildDialogButtons(dialog);
            BuildStyledDialogContent(dialog, dialog.Title, grid, buttons);
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            if (emoteList.SelectedItem is EmoteTileEntryViewModel selectedEntry && selectedEntry.Emote != null)
            {
                return selectedEntry.Emote;
            }

            return eligibleEmotes[0];
        }

        private static DataTemplate BuildEmoteSelectorTemplate()
        {
            FrameworkElementFactory rootBorder = new FrameworkElementFactory(typeof(Border));
            rootBorder.SetValue(Border.WidthProperty, 172d);
            rootBorder.SetValue(Border.HeightProperty, 188d);
            rootBorder.SetValue(Border.PaddingProperty, new Thickness(8));

            FrameworkElementFactory stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            rootBorder.AppendChild(stack);

            FrameworkElementFactory previewBorder = new FrameworkElementFactory(typeof(Border));
            previewBorder.SetValue(Border.HeightProperty, 132d);
            previewBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(34, 0, 0, 0)));
            previewBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(90, 66, 86, 106)));
            previewBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            previewBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            stack.AppendChild(previewBorder);

            FrameworkElementFactory previewGrid = new FrameworkElementFactory(typeof(Grid));
            previewBorder.AppendChild(previewGrid);

            FrameworkElementFactory image = new FrameworkElementFactory(typeof(Image));
            image.SetValue(Image.StretchProperty, Stretch.Uniform);
            image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Emote.AnimationPreview"));
            previewGrid.AppendChild(image);

            FrameworkElementFactory name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetValue(TextBlock.MarginProperty, new Thickness(0, 8, 0, 0));
            name.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(233, 237, 242)));
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Emote.Name"));
            stack.AppendChild(name);

            FrameworkElementFactory emoteNumber = new FrameworkElementFactory(typeof(TextBlock));
            emoteNumber.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
            emoteNumber.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(170, 188, 206)));
            emoteNumber.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Emote.EmoteHeader"));
            stack.AppendChild(emoteNumber);

            return new DataTemplate
            {
                VisualTree = rootBorder
            };
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
            foreach (AO2BlipPreviewPlayer player in shoutSfxPreviewPlayers.Values)
            {
                player.Stop();
            }
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

        private void InitializeEffectsFieldContextMenus()
        {
            AttachSetDefaultContextMenu(ChatDropdown, () => ChatDropdown.Text = "default");
            AttachSetDefaultContextMenu(EffectsDropdown, () => EffectsDropdown.Text = string.Empty);
            AttachSetDefaultContextMenu(ScalingDropdown, () => ScalingDropdown.Text = string.Empty);
            AttachSetDefaultContextMenu(StretchDropdown, () => StretchDropdown.Text = string.Empty);
            AttachSetDefaultContextMenu(NeedsShownameDropdown, () => NeedsShownameDropdown.Text = string.Empty);
            AttachSetDefaultContextMenu(CustomShoutNameTextBox, () => CustomShoutNameTextBox.Text = string.Empty);
            AttachSetDefaultContextMenu(RealizationTextBox, ResetRealizationFieldToDefault);
            AttachSetDefaultContextMenu(RealizationPlayButton, ResetRealizationFieldToDefault);

            AttachShoutSetDefaultContextMenu("holdit", HoldItPlayButton, HoldItPreviewImage, HoldItNoImageText, HoldItVisualFileTextBlock, HoldItSfxFileTextBlock);
            AttachShoutSetDefaultContextMenu("objection", ObjectionPlayButton, ObjectionPreviewImage, ObjectionNoImageText, ObjectionVisualFileTextBlock, ObjectionSfxFileTextBlock);
            AttachShoutSetDefaultContextMenu("takethat", TakeThatPlayButton, TakeThatPreviewImage, TakeThatNoImageText, TakeThatVisualFileTextBlock, TakeThatSfxFileTextBlock);
            AttachShoutSetDefaultContextMenu("custom", CustomPlayButton, CustomPreviewImage, CustomNoImageText, CustomVisualFileTextBlock, CustomSfxFileTextBlock);
        }

        private void AttachShoutSetDefaultContextMenu(string shoutKey, params FrameworkElement[] elements)
        {
            foreach (FrameworkElement element in elements)
            {
                AttachSetDefaultContextMenu(
                    element,
                    () =>
                    {
                        ResetSingleShoutToDefault(shoutKey);
                        StatusTextBlock.Text = $"Reset {shoutKey} shout to default values.";
                    });
            }
        }

        private void AttachSetDefaultContextMenu(FrameworkElement element, Action setDefaultAction)
        {
            if (element == null)
            {
                return;
            }

            ContextMenu menu = new ContextMenu();
            menu.Items.Add(CreateContextMenuItem("Set to default", () =>
            {
                setDefaultAction();
            }));
            element.ContextMenu = menu;
        }

        private void ResetRealizationFieldToDefault()
        {
            realizationSfxPreviewPlayer.Stop();
            RealizationPlayButton.IsPlaying = false;
            selectedRealizationSourcePath = string.Empty;
            RealizationTextBox.Text = string.Empty;
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

        private AO2BlipPreviewPlayer GetOrCreateShoutSfxPlayer(string key)
        {
            if (shoutSfxPreviewPlayers.TryGetValue(key, out AO2BlipPreviewPlayer? existing))
            {
                return existing;
            }

            AO2BlipPreviewPlayer created = new AO2BlipPreviewPlayer
            {
                Volume = (float)Math.Clamp(PreviewVolumeSlider.Value / 100d, 0d, 1d)
            };
            shoutSfxPreviewPlayers[key] = created;
            return created;
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
                AO2BlipPreviewPlayer player = GetOrCreateShoutSfxPlayer(key);
                if (player.TrySetBlip(sfxPath))
                {
                    sfxDurationMs = player.GetLoadedDurationMs();
                    _ = player.PlayBlip();
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
            if (shoutSfxPreviewPlayers.TryGetValue(key, out AO2BlipPreviewPlayer? player))
            {
                player.Stop();
            }

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
            foreach (AO2BlipPreviewPlayer player in shoutSfxPreviewPlayers.Values)
            {
                player.Volume = (float)clamped;
            }
            realizationSfxPreviewPlayer.Volume = (float)clamped;
            fileOrganizationAudioPreviewPlayer.Volume = (float)clamped;
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
            for (int i = 0; i < emotes.Count; i++)
            {
                CharacterCreationEmoteViewModel emote = emotes[i];
                int emoteId = Math.Max(1, emote.Index);

                if (!string.IsNullOrWhiteSpace(emote.PreAnimationAssetSourcePath) && File.Exists(emote.PreAnimationAssetSourcePath))
                {
                    CopyAssetToOrganizedPath(
                        emote.PreAnimationAssetSourcePath,
                        characterDirectory,
                        $"emote:{emoteId}:preanim",
                        "Images/" + Path.GetFileName(emote.PreAnimationAssetSourcePath));
                }

                if (!string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath) && File.Exists(emote.AnimationAssetSourcePath))
                {
                    CopyAssetToOrganizedPath(
                        emote.AnimationAssetSourcePath,
                        characterDirectory,
                        $"emote:{emoteId}:anim",
                        "Images/" + Path.GetFileName(emote.AnimationAssetSourcePath));
                }

                if (!string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath) && File.Exists(emote.FinalAnimationIdleAssetSourcePath))
                {
                    CopyAssetToOrganizedPath(
                        emote.FinalAnimationIdleAssetSourcePath,
                        characterDirectory,
                        $"emote:{emoteId}:idle",
                        "Images/(a)/" + Path.GetFileName(emote.FinalAnimationIdleAssetSourcePath));
                }

                if (!string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath) && File.Exists(emote.FinalAnimationTalkingAssetSourcePath))
                {
                    CopyAssetToOrganizedPath(
                        emote.FinalAnimationTalkingAssetSourcePath,
                        characterDirectory,
                        $"emote:{emoteId}:talking",
                        "Images/(b)/" + Path.GetFileName(emote.FinalAnimationTalkingAssetSourcePath));
                }

                string splitBaseName = !string.IsNullOrWhiteSpace(emote.FinalAnimationIdleAssetSourcePath)
                    ? Path.GetFileNameWithoutExtension(emote.FinalAnimationIdleAssetSourcePath)
                    : (!string.IsNullOrWhiteSpace(emote.FinalAnimationTalkingAssetSourcePath)
                        ? Path.GetFileNameWithoutExtension(emote.FinalAnimationTalkingAssetSourcePath)
                        : string.Empty);
                if (!string.IsNullOrWhiteSpace(splitBaseName))
                {
                    if (!string.IsNullOrWhiteSpace(emote.AnimationAssetSourcePath))
                    {
                        CopyAssetToOrganizedPath(
                            emote.AnimationAssetSourcePath,
                            characterDirectory,
                            $"emote:{emoteId}:splitbase",
                            "Images/" + Path.GetFileName(emote.AnimationAssetSourcePath));
                    }
                }

                ButtonIconGenerationConfig buttonConfig = BuildButtonIconGenerationConfig(emote);
                if (TryBuildButtonIconPair(buttonConfig, out BitmapSource? onImage, out BitmapSource? offImage, out _)
                    && onImage != null
                    && offImage != null)
                {
                    SaveBitmapToOrganizedPath(onImage, characterDirectory, $"button:{emoteId}:on", $"emotions/button{emoteId}_on.png");
                    SaveBitmapToOrganizedPath(offImage, characterDirectory, $"button:{emoteId}:off", $"emotions/button{emoteId}_off.png");
                }

                if (!string.IsNullOrWhiteSpace(emote.SfxAssetSourcePath) && File.Exists(emote.SfxAssetSourcePath))
                {
                    CopyAssetToOrganizedPath(
                        emote.SfxAssetSourcePath,
                        characterDirectory,
                        $"emote:{emoteId}:sfx",
                        "Sounds/" + Path.GetFileName(emote.SfxAssetSourcePath));
                }
            }
        }

        private void CopyAssetToOrganizedPath(string sourcePath, string characterDirectory, string assetKey, string defaultRelativePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }

            string relativePath = ResolveOutputPathForAsset(assetKey, defaultRelativePath, isFolder: false);
            string destinationPath = Path.Combine(characterDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private void SaveBitmapToOrganizedPath(BitmapSource bitmap, string characterDirectory, string assetKey, string defaultRelativePath)
        {
            string relativePath = ResolveOutputPathForAsset(assetKey, defaultRelativePath, isFolder: false);
            string destinationPath = Path.Combine(characterDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            SaveBitmapAsPng(bitmap, destinationPath);
        }

        private static void EnsureDestinationDirectory(string destinationPath)
        {
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }
        }

        private void CopyExternalOrganizationEntries(string characterDirectory)
        {
            foreach (ExternalOrganizationEntry entry in externalOrganizationEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    continue;
                }

                string relative = entry.RelativePath.Trim().Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                string destinationPath = Path.Combine(characterDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                if (entry.IsFolder)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.SourcePath) || !File.Exists(entry.SourcePath))
                {
                    continue;
                }

                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(entry.SourcePath, destinationPath, overwrite: true);
            }
        }

        private void CopyCharacterIconAsset(string characterDirectory)
        {
            if (generatedCharacterIconImage != null)
            {
                SaveBitmapToOrganizedPath(generatedCharacterIconImage, characterDirectory, "charicon", "char_icon.png");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedCharacterIconSourcePath) || !File.Exists(selectedCharacterIconSourcePath))
            {
                return;
            }

            string extension = Path.GetExtension(selectedCharacterIconSourcePath);
            CopyAssetToOrganizedPath(selectedCharacterIconSourcePath, characterDirectory, "charicon", "char_icon" + extension);
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
            string relativePath = ResolveOutputPathForAsset($"shout:visual:{key}", targetBaseName + extension, isFolder: false);
            string destinationPath = Path.Combine(characterDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            EnsureDestinationDirectory(destinationPath);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private void CleanupEmptyStandardAssetFolders(string characterDirectory)
        {
            RemoveStandardFolderIfEmpty(characterDirectory, "Images");
            RemoveStandardFolderIfEmpty(characterDirectory, "Sounds");
            RemoveStandardFolderIfEmpty(characterDirectory, "emotions");
        }

        private void RemoveStandardFolderIfEmpty(string characterDirectory, string folderName)
        {
            string relativeFolder = NormalizeRelativePath(folderName + "/", isFolder: true);
            if (IsFolderExplicitlyConfigured(relativeFolder))
            {
                return;
            }

            string fullPath = Path.Combine(characterDirectory, folderName);
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            RemoveEmptyDirectoryRecursive(fullPath);
        }

        private bool IsFolderExplicitlyConfigured(string relativeFolder)
        {
            string normalized = NormalizeRelativePath(relativeFolder, isFolder: true);
            return externalOrganizationEntries.Any(entry =>
                entry.IsFolder
                && string.Equals(
                    NormalizeRelativePath(entry.RelativePath, isFolder: true),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool RemoveEmptyDirectoryRecursive(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return true;
            }

            foreach (string subDirectory in Directory.GetDirectories(directoryPath))
            {
                _ = RemoveEmptyDirectoryRecursive(subDirectory);
            }

            bool isEmpty = !Directory.EnumerateFileSystemEntries(directoryPath).Any();
            if (isEmpty)
            {
                Directory.Delete(directoryPath, recursive: false);
                return true;
            }

            return false;
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
            string relativePath = ResolveOutputPathForAsset($"shout:sfx:{key}", targetBaseName + extension, isFolder: false);
            string destinationPath = Path.Combine(characterDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            EnsureDestinationDirectory(destinationPath);
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

        private static string CreateStagingMountPath(string targetMountPath)
        {
            string stagingRoot = Path.Combine(targetMountPath, ".oceanya_character_edit_staging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            return stagingRoot;
        }

        private static void ReplaceCharacterFolderFromStaging(
            string stagedCharacterDirectory,
            string originalCharacterDirectory,
            string targetCharacterDirectory)
        {
            string normalizedStaged = NormalizePathForCompare(stagedCharacterDirectory);
            string normalizedOriginal = NormalizePathForCompare(originalCharacterDirectory);
            string normalizedTarget = NormalizePathForCompare(targetCharacterDirectory);

            if (!Directory.Exists(normalizedStaged))
            {
                throw new DirectoryNotFoundException("Staged character directory does not exist: " + normalizedStaged);
            }

            if (!Directory.Exists(normalizedOriginal))
            {
                throw new DirectoryNotFoundException("Original character directory does not exist: " + normalizedOriginal);
            }

            string targetParent = Path.GetDirectoryName(normalizedTarget) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetParent))
            {
                throw new InvalidOperationException("Target character parent directory is invalid.");
            }

            Directory.CreateDirectory(targetParent);
            if (!string.Equals(normalizedTarget, normalizedOriginal, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(normalizedTarget))
            {
                throw new IOException("Target character directory already exists: " + normalizedTarget);
            }

            string backupPath = normalizedOriginal + ".oceanya_edit_backup_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            bool backupMoved = false;
            try
            {
                Directory.Move(normalizedOriginal, backupPath);
                backupMoved = true;

                Directory.Move(normalizedStaged, normalizedTarget);
                Directory.Delete(backupPath, recursive: true);
            }
            catch
            {
                if (backupMoved && !Directory.Exists(normalizedOriginal) && Directory.Exists(backupPath))
                {
                    Directory.Move(backupPath, normalizedOriginal);
                }

                throw;
            }
            finally
            {
                string? stagingMountRoot = Path.GetDirectoryName(Path.GetDirectoryName(normalizedStaged) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(stagingMountRoot)
                    && Directory.Exists(stagingMountRoot)
                    && Path.GetFileName(stagingMountRoot).StartsWith(".oceanya_character_edit_staging_", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Directory.Delete(stagingMountRoot, recursive: true);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
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
            if (skipCloseConfirmationOnce)
            {
                skipCloseConfirmationOnce = false;
            }
            else if (!ConfirmCloseIfUncommitted())
            {
                e.Cancel = true;
                return;
            }

            StopBlipPreview();
            EndInternalEmoteReorderDrag(commit: false);
            StopFileOrganizationAudioPreview();
            ClearFileOrganizationPreviewPlayers();
            StopAllEmotePreviewPlayers();
            blipPreviewPlayer.Dispose();
            foreach (AO2BlipPreviewPlayer player in shoutSfxPreviewPlayers.Values.ToArray())
            {
                player.Dispose();
            }
            shoutSfxPreviewPlayers.Clear();
            realizationSfxPreviewPlayer.Dispose();
            fileOrganizationAudioPreviewPlayer.Dispose();
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
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
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
            lastAppliedCharacterDirectoryPath = string.Empty;
            previousAppliedCharacterDirectoryPath = string.Empty;
            await WaitForm.ShowFormAsync(isEditMode ? "Applying character folder edits..." : "Generating character folder...", this);
            string? successCharacterDirectory = null;
            Exception? generationException = null;
            void ShowBlockingGenerateMessage(string text, string title, MessageBoxImage image)
            {
                WaitForm.CloseForm();
                OceanyaMessageBox.Show(this, text, title, MessageBoxButton.OK, image);
            }
            try
            {
                await SetGenerateSubtitleAsync("Validating setup and emote data...");
                SaveSelectedEmoteEditorValues();

                if (emotes.Count == 0)
                {
                    ShowBlockingGenerateMessage("Add at least one emote before generating.", "Invalid Input", MessageBoxImage.Warning);
                    return;
                }

                string folderName = (CharacterFolderNameTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    ShowBlockingGenerateMessage("Character folder name is required.", "Invalid Input", MessageBoxImage.Warning);
                    return;
                }

                string mountPath = (MountPathComboBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(mountPath) && mountPathOptions.Count == 1)
                {
                    mountPath = mountPathOptions[0];
                }

                if (string.IsNullOrWhiteSpace(mountPath))
                {
                    ShowBlockingGenerateMessage("No valid mount path is available.", "Invalid Input", MessageBoxImage.Warning);
                    return;
                }

                string targetCharacterDirectory = Path.Combine(mountPath, "characters", folderName);
                if (isEditMode)
                {
                    if (string.IsNullOrWhiteSpace(originalEditCharacterDirectoryPath) || !Directory.Exists(originalEditCharacterDirectoryPath))
                    {
                        ShowBlockingGenerateMessage(
                            "The original character folder no longer exists on disk. Reload it from the visualizer first.",
                            "Invalid Edit Source",
                            MessageBoxImage.Warning);
                        return;
                    }

                    bool targetIsOriginal = string.Equals(
                        NormalizePathForCompare(targetCharacterDirectory),
                        NormalizePathForCompare(originalEditCharacterDirectoryPath),
                        StringComparison.OrdinalIgnoreCase);
                    if (!targetIsOriginal && Directory.Exists(targetCharacterDirectory))
                    {
                        ShowBlockingGenerateMessage(
                            $"Target folder already exists:\n{targetCharacterDirectory}\n\nChoose another folder name/mount path.",
                            "Invalid Target",
                            MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (Directory.Exists(targetCharacterDirectory))
                {
                    ShowBlockingGenerateMessage(
                        $"Folder already exists:\n{targetCharacterDirectory}\n\nChoose a different folder name.",
                        "Invalid Target",
                        MessageBoxImage.Warning);
                    return;
                }

                string sanitizedFolderToken = SanitizeFolderNameForRelativePath(folderName);
                List<CharacterCreationEmote> generatedEmotes = new List<CharacterCreationEmote>();
                foreach (CharacterCreationEmoteViewModel viewModel in emotes)
                {
                    CharacterCreationEmote model = viewModel.ToModel();
                    int emoteId = Math.Max(1, viewModel.Index);

                    if (!string.IsNullOrWhiteSpace(viewModel.PreAnimationAssetSourcePath)
                        && File.Exists(viewModel.PreAnimationAssetSourcePath)
                        && !string.IsNullOrWhiteSpace(model.PreAnimation)
                        && !string.Equals(model.PreAnimation, "-", StringComparison.Ordinal))
                    {
                        string preanimPath = ResolveOutputPathForAsset(
                            $"emote:{emoteId}:preanim",
                            "Images/" + Path.GetFileName(viewModel.PreAnimationAssetSourcePath),
                            isFolder: false);
                        model.PreAnimation = RemoveExtensionFromRelativePath(preanimPath);
                    }

                    string? animationPath = null;
                    bool hasSplit = (!string.IsNullOrWhiteSpace(viewModel.FinalAnimationIdleAssetSourcePath)
                        && File.Exists(viewModel.FinalAnimationIdleAssetSourcePath))
                        || (!string.IsNullOrWhiteSpace(viewModel.FinalAnimationTalkingAssetSourcePath)
                            && File.Exists(viewModel.FinalAnimationTalkingAssetSourcePath));
                    if (hasSplit)
                    {
                        string splitDefaultName = !string.IsNullOrWhiteSpace(viewModel.FinalAnimationIdleAssetSourcePath)
                            ? Path.GetFileName(viewModel.FinalAnimationIdleAssetSourcePath)
                            : Path.GetFileName(viewModel.FinalAnimationTalkingAssetSourcePath);
                        animationPath = ResolveOutputPathForAsset(
                            $"emote:{emoteId}:splitbase",
                            "Images/" + splitDefaultName,
                            isFolder: false);
                    }
                    else if (!string.IsNullOrWhiteSpace(viewModel.AnimationAssetSourcePath)
                        && File.Exists(viewModel.AnimationAssetSourcePath))
                    {
                        animationPath = ResolveOutputPathForAsset(
                            $"emote:{emoteId}:anim",
                            "Images/" + Path.GetFileName(viewModel.AnimationAssetSourcePath),
                            isFolder: false);
                    }

                    if (!string.IsNullOrWhiteSpace(animationPath))
                    {
                        model.Animation = RemoveExtensionFromRelativePath(animationPath);
                    }

                    if (!string.IsNullOrWhiteSpace(viewModel.SfxAssetSourcePath) && File.Exists(viewModel.SfxAssetSourcePath))
                    {
                        string sfxPath = ResolveOutputPathForAsset(
                            $"emote:{emoteId}:sfx",
                            "Sounds/" + Path.GetFileName(viewModel.SfxAssetSourcePath),
                            isFolder: false);
                        model.SfxName = $"../../characters/{sanitizedFolderToken}/{RemoveExtensionFromRelativePath(sfxPath)}";
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

                    generatedEmotes.Add(model);
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
                    Emotes = generatedEmotes
                };

                bool usingCustomBlipFile = !string.IsNullOrWhiteSpace(selectedCustomBlipSourcePath)
                    && !string.IsNullOrWhiteSpace(customBlipOptionText)
                    && string.Equals((GenderBlipsDropdown.Text ?? string.Empty).Trim(), customBlipOptionText, StringComparison.OrdinalIgnoreCase);
                if (usingCustomBlipFile)
                {
                    string blipDefault = $"blips/{Path.GetFileName(selectedCustomBlipSourcePath)}";
                    string blipRelative = ResolveOutputPathForAsset("blip:custom", blipDefault, isFolder: false);
                    project.Blips = $"../../characters/{sanitizedFolderToken}/{RemoveExtensionFromRelativePath(blipRelative)}";
                    project.Gender = project.Blips;
                }

                string actualMountPath = project.MountPath;
                string workingDirectory;
                if (isEditMode)
                {
                    actualMountPath = CreateStagingMountPath(mountPath);
                    project.MountPath = actualMountPath;
                }

                await SetGenerateSubtitleAsync(isEditMode
                    ? "Building staged edited folder..."
                    : "Creating character folder and writing char.ini...");
                string characterDirectory = AOCharacterFileCreatorBuilder.CreateCharacterFolder(project);
                workingDirectory = characterDirectory;

                await SetGenerateSubtitleAsync("Copying emote assets (Images/Sounds/emotions)...");
                CopyEmoteAssets(workingDirectory);
                await SetGenerateSubtitleAsync("Copying character icon...");
                CopyCharacterIconAsset(workingDirectory);
                await SetGenerateSubtitleAsync("Copying shout assets (root special case)...");
                CopyShoutAssets(workingDirectory);
                await SetGenerateSubtitleAsync("Applying file organization extras...");
                CopyExternalOrganizationEntries(workingDirectory);
                CleanupEmptyStandardAssetFolders(workingDirectory);

                if (usingCustomBlipFile)
                {
                    await SetGenerateSubtitleAsync("Copying custom blip file...");
                    string blipDefault = $"blips/{Path.GetFileName(selectedCustomBlipSourcePath)}";
                    CopyAssetToOrganizedPath(selectedCustomBlipSourcePath, workingDirectory, "blip:custom", blipDefault);
                }

                bool hasCustomShoutFiles = selectedShoutVisualSourcePaths.ContainsKey("custom")
                    || selectedShoutSfxSourcePaths.ContainsKey("custom");
                string customShoutName = (CustomShoutNameTextBox.Text ?? string.Empty).Trim();
                if (hasCustomShoutFiles || !string.IsNullOrWhiteSpace(customShoutName))
                {
                    await SetGenerateSubtitleAsync("Updating custom shout settings...");
                    AddOrUpdateShoutsCustomName(Path.Combine(workingDirectory, "char.ini"), customShoutName);
                }

                if (!string.IsNullOrWhiteSpace(selectedRealizationSourcePath)
                    && string.Equals(
                        (RealizationTextBox.Text ?? string.Empty).Trim(),
                        Path.GetFileName(selectedRealizationSourcePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    await SetGenerateSubtitleAsync("Copying realization sound (root special case)...");
                    string extension = Path.GetExtension(selectedRealizationSourcePath);
                    string realizationDefault = "realization" + extension;
                    CopyAssetToOrganizedPath(selectedRealizationSourcePath, workingDirectory, "realization:custom", realizationDefault);

                    string sanitizedFolder = SanitizeFolderNameForRelativePath(folderName);
                    string realizationRelative = ResolveOutputPathForAsset("realization:custom", realizationDefault, isFolder: false);
                    string realizationToken = $"../../characters/{sanitizedFolder}/{RemoveExtensionFromRelativePath(realizationRelative)}";
                    AddOrUpdateOptionsValue(Path.Combine(workingDirectory, "char.ini"), "realization", realizationToken);
                }

                if (isEditMode)
                {
                    await SetGenerateSubtitleAsync("Replacing original character folder...");
                    ReplaceCharacterFolderFromStaging(
                        stagedCharacterDirectory: workingDirectory,
                        originalCharacterDirectory: originalEditCharacterDirectoryPath,
                        targetCharacterDirectory: targetCharacterDirectory);
                    editApplyCompleted = true;
                    successCharacterDirectory = targetCharacterDirectory;
                }
                else
                {
                    successCharacterDirectory = workingDirectory;
                }

                await SetGenerateSubtitleAsync("Updating character index...");
                try
                {
                    string? previousDirectory = isEditMode ? originalEditCharacterDirectoryPath : string.Empty;
                    if (!CharacterFolder.TryUpsertCharacterFolderInCache(
                            successCharacterDirectory ?? string.Empty,
                            previousDirectory,
                            out _,
                            out string cacheError))
                    {
                        CustomConsole.Warning(
                            "Character folder creation succeeded, but incremental character cache update failed: "
                            + cacheError);
                    }
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning("Character folder creation succeeded, but character cache update failed.", ex);
                }

                StatusTextBlock.Text = isEditMode
                    ? "Edited character folder: " + successCharacterDirectory
                    : "Created character folder: " + successCharacterDirectory;
            }
            catch (Exception ex)
            {
                CustomConsole.Error(isEditMode
                    ? "Failed to edit AO character folder."
                    : "Failed to create AO character folder.", ex);
                generationException = ex;
            }
            finally
            {
                WaitForm.CloseForm();
            }

            if (generationException != null)
            {
                OceanyaMessageBox.Show(
                    this,
                    (isEditMode ? "Failed to edit character folder:\n" : "Failed to create character folder:\n") + generationException.Message,
                    isEditMode ? "Edit Error" : "Creation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrWhiteSpace(successCharacterDirectory))
            {
                lastAppliedCharacterDirectoryPath = successCharacterDirectory;
                previousAppliedCharacterDirectoryPath = isEditMode
                    ? (originalEditCharacterDirectoryPath ?? string.Empty)
                    : string.Empty;
                lastCommittedStateFingerprint = ComputeCurrentStateFingerprint();
                OceanyaMessageBox.Show(
                    this,
                    (isEditMode ? "Character folder edited successfully:\n" : "Character folder created successfully:\n")
                    + successCharacterDirectory,
                    "AO Character File Creator",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private string ComputeCurrentStateFingerprint()
        {
            try
            {
                StringBuilder builder = new StringBuilder();
                CharacterCreationProject project = BuildProjectForPreview();
                builder.Append(project.MountPath ?? string.Empty).Append('|');
                builder.Append(project.CharacterFolderName ?? string.Empty).Append('|');
                builder.Append(project.ShowName ?? string.Empty).Append('|');
                builder.Append(project.Side ?? string.Empty).Append('|');
                builder.Append(project.Blips ?? string.Empty).Append('|');
                builder.Append(project.Chat ?? string.Empty).Append('|');
                builder.Append(project.Effects ?? string.Empty).Append('|');
                builder.Append(project.Realization ?? string.Empty).Append('|');
                builder.Append(project.Scaling ?? string.Empty).Append('|');
                builder.Append(project.Stretch ?? string.Empty).Append('|');
                builder.Append(project.NeedsShowName ?? string.Empty).Append('|');
                builder.Append(project.Shouts ?? string.Empty).Append('|');
                builder.Append(project.Emotes.Count).Append('|');
                foreach (CharacterCreationEmote emote in project.Emotes)
                {
                    builder.Append(emote.Name ?? string.Empty).Append(',');
                    builder.Append(emote.PreAnimation ?? string.Empty).Append(',');
                    builder.Append(emote.Animation ?? string.Empty).Append(',');
                    builder.Append(emote.SfxName ?? string.Empty).Append(';');
                }

                builder.Append('|');
                foreach (ExternalOrganizationEntry entry in externalOrganizationEntries
                    .OrderBy(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append(entry.RelativePath ?? string.Empty).Append(',');
                    builder.Append(entry.SourcePath ?? string.Empty).Append(',');
                    builder.Append(entry.IsFolder ? "1" : "0").Append(';');
                }

                builder.Append('|');
                foreach ((string key, string value) in generatedOrganizationOverrides
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append(key).Append('=').Append(value).Append(';');
                }

                return builder.ToString();
            }
            catch
            {
                return "fingerprint_error";
            }
        }

        private bool ConfirmCloseIfUncommitted()
        {
            string currentFingerprint = ComputeCurrentStateFingerprint();
            if (string.Equals(currentFingerprint, lastCommittedStateFingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            MessageBoxResult decision = OceanyaMessageBox.Show(
                this,
                "You have uncommitted changes. Close the Character Creator without saving/generating?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return decision == MessageBoxResult.Yes;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (string.Equals(activeSection, "fileorganization", StringComparison.OrdinalIgnoreCase)
                && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.C)
                {
                    CopySelectedFileOrganizationEntry();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.X)
                {
                    CutSelectedFileOrganizationEntry();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.V)
                {
                    PasteIntoCurrentFileOrganizationPath();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape)
            {
                if (isInternalEmoteReorderDragInProgress)
                {
                    EndInternalEmoteReorderDrag(commit: true);
                    e.Handled = true;
                    return;
                }

                if (!ConfirmCloseIfUncommitted())
                {
                    e.Handled = true;
                    return;
                }

                StopBlipPreview();
                skipCloseConfirmationOnce = true;
                Close();
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (current is FrameworkElement element)
                    {
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmCloseIfUncommitted())
            {
                return;
            }

            skipCloseConfirmationOnce = true;
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

    internal sealed class IniDocument
    {
        public Dictionary<string, List<IniEntry>> Sections { get; } =
            new Dictionary<string, List<IniEntry>>(StringComparer.OrdinalIgnoreCase);

        public static IniDocument Load(string path)
        {
            IniDocument document = new IniDocument();
            string currentSection = string.Empty;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    _ = document.GetEntries(currentSection);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                document.GetEntries(currentSection).Add(new IniEntry(key, value));
            }

            return document;
        }

        public List<IniEntry> GetEntries(string sectionName)
        {
            string key = (sectionName ?? string.Empty).Trim();
            if (!Sections.TryGetValue(key, out List<IniEntry>? entries))
            {
                entries = new List<IniEntry>();
                Sections[key] = entries;
            }

            return entries;
        }

        public bool TryGetLatestValue(string sectionName, string key, out string? value)
        {
            value = null;
            List<IniEntry> entries = GetEntries(sectionName);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = entries[i].Value;
                    return true;
                }
            }

            return false;
        }
    }

    internal readonly struct IniEntry
    {
        public IniEntry(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Key { get; }
        public string Value { get; }
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

    internal sealed class RowResizeGuideAdorner : Adorner
    {
        private static readonly Pen GuidePen = CreatePen();
        private double guideY;

        public RowResizeGuideAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void SetGuideY(double y)
        {
            guideY = Math.Clamp(y, 0, Math.Max(0, AdornedElement.RenderSize.Height - 1));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            double width = Math.Max(0, AdornedElement.RenderSize.Width);
            drawingContext.DrawLine(GuidePen, new Point(0, guideY), new Point(width, guideY));
        }

        private static Pen CreatePen()
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(220, 151, 201, 255));
            brush.Freeze();
            Pen pen = new Pen(brush, 2);
            pen.Freeze();
            return pen;
        }
    }

    public sealed class EmoteTileEntryViewModel
    {
        public bool IsAddTile { get; set; }
        public CharacterCreationEmoteViewModel? Emote { get; set; }
    }

    public sealed class FileOrganizationEntryViewModel
        : INotifyPropertyChanged
    {
        private FileOrganizationEntryKind entryKind;
        private string iconGlyph = "\uE11B";
        private ImageSource? previewImage;
        private string baseTypeDisplayName = "Unknown";
        private bool isPendingCut;
        private bool isUnused;
        private bool isRenaming;
        private string renameDraft = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string DefaultRelativePath { get; set; } = string.Empty;
        public string AssetKey { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public bool IsLocked { get; set; }
        public bool IsExternal { get; set; }
        public bool IsFolder { get; set; }
        public string? SourcePath { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public FileOrganizationEntryKind EntryKind
        {
            get => entryKind;
            set
            {
                if (entryKind == value)
                {
                    return;
                }

                entryKind = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsImageKind));
                OnPropertyChanged(nameof(IsAudioKind));
                OnPropertyChanged(nameof(ShowGlyph));
            }
        }

        public string IconGlyph
        {
            get => iconGlyph;
            set
            {
                if (string.Equals(iconGlyph, value, StringComparison.Ordinal))
                {
                    return;
                }

                iconGlyph = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public ImageSource? PreviewImage
        {
            get => previewImage;
            set
            {
                if (ReferenceEquals(previewImage, value))
                {
                    return;
                }

                previewImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowGlyph));
                OnPropertyChanged(nameof(ShowNoImageLabel));
            }
        }

        public string TypeDisplayName
        {
            get => IsUnused ? $"{baseTypeDisplayName} (Unused)" : baseTypeDisplayName;
            set
            {
                if (string.Equals(baseTypeDisplayName, value, StringComparison.Ordinal))
                {
                    return;
                }

                baseTypeDisplayName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public bool IsPendingCut
        {
            get => isPendingCut;
            set
            {
                if (isPendingCut == value)
                {
                    return;
                }

                isPendingCut = value;
                OnPropertyChanged();
            }
        }

        public bool IsRenaming
        {
            get => isRenaming;
            set
            {
                if (isRenaming == value)
                {
                    return;
                }

                isRenaming = value;
                OnPropertyChanged();
            }
        }

        public string RenameDraft
        {
            get => renameDraft;
            set
            {
                if (string.Equals(renameDraft, value, StringComparison.Ordinal))
                {
                    return;
                }

                renameDraft = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public bool IsImageKind => EntryKind == FileOrganizationEntryKind.Image;
        public bool IsAudioKind => EntryKind == FileOrganizationEntryKind.Audio;
        public bool ShowGlyph => !IsImageKind || PreviewImage == null;
        public bool ShowNoImageLabel => IsImageKind && PreviewImage == null;
        public bool IsUnused
        {
            get => isUnused;
            set
            {
                if (isUnused == value)
                {
                    return;
                }

                isUnused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TitleBrush));
                OnPropertyChanged(nameof(SubtitleBrush));
                OnPropertyChanged(nameof(TypeDisplayName));
            }
        }
        public bool IsInteractionLocked => IsLocked;
        public Brush TitleBrush => IsUnused
            ? new SolidColorBrush(Color.FromRgb(248, 236, 192))
            : (IsInteractionLocked
                ? new SolidColorBrush(Color.FromRgb(138, 138, 138))
                : new SolidColorBrush(Color.FromRgb(236, 236, 236)));
        public Brush SubtitleBrush => IsUnused
            ? new SolidColorBrush(Color.FromRgb(224, 214, 168))
            : new SolidColorBrush(Color.FromRgb(159, 176, 194));
        public string UniqueKey => $"{RelativePath}|{SourcePath}|{IsExternal}";

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ExternalOrganizationEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string? SourcePath { get; set; }
        public bool IsFolder { get; set; }
    }

    public sealed class FileOrganizationClipboardEntry
    {
        public FileOrganizationEntryViewModel? Entry { get; set; }
    }

    public sealed class CutSelectionState
    {
        public string SourcePath { get; set; } = string.Empty;
        public Rect NormalizedSelection { get; set; } = Rect.Empty;
    }

    public enum FileOrganizationEntryKind
    {
        Unknown = 0,
        Folder = 1,
        Image = 2,
        Audio = 3,
        Text = 4
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
