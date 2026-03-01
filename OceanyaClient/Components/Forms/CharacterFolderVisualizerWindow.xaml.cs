using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AOBot_Testing.Structures;
using Common;
using OceanyaClient.Features.Startup;

namespace OceanyaClient
{
    /// <summary>
    /// Displays a configurable Windows-like visualizer for local AO character folders.
    /// </summary>
    public partial class CharacterFolderVisualizerWindow : OceanyaWindowContentControl, IStartupFunctionalityWindow
    {
        public event Action? FinishedLoading;
        private const string FallbackFolderPackUri =
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png";

        private static readonly ImageSource FallbackFolderImage = LoadEmbeddedImage(FallbackFolderPackUri);
        private const int VisualizerDiskCacheVersion = 1;
        private const int FolderTagCacheVersion = 1;
        private const string UntaggedFilterToken = "(none)";
        private const int ViewportRetentionRows = 8;
        private static readonly bool EnablePreviewDebugLog = true;
        private static readonly JsonSerializerOptions CacheJsonOptions = new JsonSerializerOptions { WriteIndented = false };
        private static readonly JsonSerializerOptions FolderTagCacheJsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly string PreviewDebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OceanyaClient",
            "debug",
            "folder_preview_debug.log");

        private static string diskCacheFilePath = string.Empty;
        private static bool diskCachePathInitialized;
        private static string folderTagCacheFilePath = string.Empty;
        private static bool folderTagCachePathInitialized;

        private readonly Action? onAssetsRefreshed;
        private readonly Func<FolderVisualizerItem, bool>? canSetCharacterInClient;
        private readonly Action<FolderVisualizerItem>? setCharacterInClient;
        private readonly object progressiveLoadKeyLock = new object();
        private readonly List<FolderVisualizerItem> allItems = new List<FolderVisualizerItem>();
        private CancellationTokenSource? progressiveImageLoadCancellation;
        private readonly HashSet<string> progressiveLoadedItemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer progressiveLoadReprioritizeTimer;
        private ScrollViewer? folderListScrollViewer;

        private bool hasLoaded;
        private bool hasRaisedFinishedLoading;
        private bool applyingSavedWindowState;
        private bool suppressViewSelectionChanged;
        private FolderVisualizerConfig visualizerConfig = new FolderVisualizerConfig();
        private ICollectionView? itemsView;
        private string searchText = string.Empty;
        private string pendingSearchText = string.Empty;
        private readonly DispatcherTimer searchDebounceTimer;
        private readonly DispatcherTimer tagSaveDebounceTimer;
        private bool hasPendingTagSave;
        private FolderVisualizerItem? contextMenuTargetItem;
        private FolderVisualizerItem? selectedItemForTagging;
        private bool suppressTagInputAutocomplete;
        private readonly Dictionary<string, HashSet<string>> folderTagsByDirectory =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeIncludeTagFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeExcludeTagFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<string> selectedFolderTags = new ObservableCollection<string>();
        private readonly ObservableCollection<string> allKnownTags = new ObservableCollection<string>();
        private readonly VisualizerWindowState tagFilterWindowState = new VisualizerWindowState
        {
            Width = 500,
            Height = 560,
            IsMaximized = false
        };
        private double savedTagPanelWidth = 260;
        private bool tagPanelCollapsed;
        private double normalScrollWheelStep = 90;
        private FolderVisualizerTableColumnKey? currentSortColumnKey = FolderVisualizerTableColumnKey.RowNumber;
        private ListSortDirection currentSortDirection = ListSortDirection.Ascending;
        private FolderFilterRule activeFilterRoot = FolderFilterRule.CreateGroup(FolderFilterConnector.And);
        private readonly Dictionary<GridViewColumn, FolderVisualizerTableColumnConfig> tableColumnMap =
            new Dictionary<GridViewColumn, FolderVisualizerTableColumnConfig>();
        private readonly Dictionary<GridViewColumn, EventHandler> columnWidthHandlers =
            new Dictionary<GridViewColumn, EventHandler>();
        private Thumb? activeRowResizeThumb;
        private double rowResizeStartHeight;
        private double rowResizePreviewHeight;
        private double rowResizeGuideStartY;
        private AdornerLayer? rowResizeAdornerLayer;
        private RowResizeGuideAdorner? rowResizeGuideAdorner;
        private bool preserveTagInputFocusOnSelection;

        /// <summary>
        /// Gets the currently projected folder items.
        /// </summary>
        internal IReadOnlyList<FolderVisualizerItem> FolderItems => allItems;

        /// <summary>
        /// Gets the current list of configured view presets.
        /// </summary>
        internal IReadOnlyList<FolderVisualizerViewPreset> ViewPresets => visualizerConfig.Presets;

        public static readonly DependencyProperty TileWidthProperty = DependencyProperty.Register(
            nameof(TileWidth), typeof(double), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(170d));

        public static readonly DependencyProperty TileHeightProperty = DependencyProperty.Register(
            nameof(TileHeight), typeof(double), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(182d));

        public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
            nameof(IconSize), typeof(double), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(18d));

        public static readonly DependencyProperty TileNameFontSizeProperty = DependencyProperty.Register(
            nameof(TileNameFontSize), typeof(double), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(12d));

        public static readonly DependencyProperty InternalTileRowPaddingProperty = DependencyProperty.Register(
            nameof(InternalTileRowPadding), typeof(Thickness), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(new Thickness(0)));

        public static readonly DependencyProperty TilePaddingProperty = DependencyProperty.Register(
            nameof(TilePadding), typeof(Thickness), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(new Thickness(8)));

        public static readonly DependencyProperty TileMarginProperty = DependencyProperty.Register(
            nameof(TileMargin), typeof(Thickness), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(new Thickness(4)));
        public static readonly DependencyProperty ShowIntegrityVerifierResultsProperty = DependencyProperty.Register(
            nameof(ShowIntegrityVerifierResults), typeof(bool), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(false));
        public static readonly DependencyProperty DetailsRowHeightProperty = DependencyProperty.Register(
            nameof(DetailsRowHeight), typeof(double), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(34d));

        public double TileWidth
        {
            get => (double)GetValue(TileWidthProperty);
            set => SetValue(TileWidthProperty, value);
        }

        public double TileHeight
        {
            get => (double)GetValue(TileHeightProperty);
            set => SetValue(TileHeightProperty, value);
        }

        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        public double TileNameFontSize
        {
            get => (double)GetValue(TileNameFontSizeProperty);
            set => SetValue(TileNameFontSizeProperty, value);
        }

        public Thickness TilePadding
        {
            get => (Thickness)GetValue(TilePaddingProperty);
            set => SetValue(TilePaddingProperty, value);
        }

        public Thickness InternalTileRowPadding
        {
            get => (Thickness)GetValue(InternalTileRowPaddingProperty);
            set => SetValue(InternalTileRowPaddingProperty, value);
        }

        public Thickness TileMargin
        {
            get => (Thickness)GetValue(TileMarginProperty);
            set => SetValue(TileMarginProperty, value);
        }

        public bool ShowIntegrityVerifierResults
        {
            get => (bool)GetValue(ShowIntegrityVerifierResultsProperty);
            set => SetValue(ShowIntegrityVerifierResultsProperty, value);
        }

        public double DetailsRowHeight
        {
            get => (double)GetValue(DetailsRowHeightProperty);
            set => SetValue(DetailsRowHeightProperty, value);
        }

        /// <summary>
        /// Initializes a new visualizer window.
        /// </summary>
        public CharacterFolderVisualizerWindow(
            Action? onAssetsRefreshed,
            Func<FolderVisualizerItem, bool>? canSetCharacterInClient = null,
            Action<FolderVisualizerItem>? setCharacterInClient = null)
        {
            InitializeComponent();
            Title = "Character Folder Visualizer";
            SourceInitialized += Window_SourceInitialized;
            StateChanged += Window_StateChanged;
            Closed += Window_Closed;
            this.onAssetsRefreshed = onAssetsRefreshed;
            this.canSetCharacterInClient = canSetCharacterInClient;
            this.setCharacterInClient = setCharacterInClient;
            searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            tagSaveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(320)
            };
            tagSaveDebounceTimer.Tick += TagSaveDebounceTimer_Tick;
            progressiveLoadReprioritizeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            progressiveLoadReprioritizeTimer.Tick += ProgressiveLoadReprioritizeTimer_Tick;

            visualizerConfig = CloneConfig(SaveFile.Data.FolderVisualizer);
            ShowIntegrityVerifierResults = SaveFile.Data.ViewFolderIntegrityVerifierResults;
            ViewIntegrityVerifierResultsCheckBox.IsChecked = ShowIntegrityVerifierResults;
            SelectedFolderTagsListBox.ItemsSource = selectedFolderTags;
            TagInputComboBox.ItemsSource = allKnownTags;
            LoadTagStateFromCache();
            ApplySavedWindowState();
            ApplyTagPanelState();
            UpdateActiveTagFiltersText();
            RefreshSelectedFolderTagPanel();
            BindViewPresets();
            InitializePreviewDebugLog();
        }

        /// <inheritdoc/>
        public override string HeaderText => "CHARACTER FOLDER VISUALIZER";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

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

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWorkAreaMaxBounds();
            LogPreviewDebug("CharacterFolderVisualizer window loaded.");

            if (hasLoaded)
            {
                return;
            }

            hasLoaded = true;
            await LoadCharacterItemsAsync();
            EnsureFolderListScrollViewerHooked();

            if (!hasRaisedFinishedLoading)
            {
                hasRaisedFinishedLoading = true;
                FinishedLoading?.Invoke();
            }
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            ApplyWorkAreaMaxBounds();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void ApplySavedWindowState()
        {
            VisualizerWindowState state = SaveFile.Data.FolderVisualizerWindowState ?? new VisualizerWindowState();
            applyingSavedWindowState = true;
            try
            {
                Width = Math.Max(MinWidth, state.Width);
                Height = Math.Max(MinHeight, state.Height);

                if (state.IsMaximized)
                {
                    WindowState = WindowState.Maximized;
                }
            }
            finally
            {
                applyingSavedWindowState = false;
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
                IsMaximized = WindowState == WindowState.Maximized
            };
        }

        private void LoadTagStateFromCache()
        {
            folderTagsByDirectory.Clear();
            activeIncludeTagFilters.Clear();
            activeExcludeTagFilters.Clear();
            savedTagPanelWidth = 260;
            tagPanelCollapsed = false;
            tagFilterWindowState.Width = 500;
            tagFilterWindowState.Height = 560;
            tagFilterWindowState.IsMaximized = false;

            FolderTagCacheFileData cacheData = TryLoadFolderTagCacheFromDisk() ?? new FolderTagCacheFileData();
            MergeLegacySaveFileTags(cacheData);

            foreach (KeyValuePair<string, List<string>> pair in cacheData.FolderTags)
            {
                string key = NormalizeTagAssignmentKey(pair.Key);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string tag in pair.Value ?? new List<string>())
                {
                    string normalizedTag = NormalizeTag(tag);
                    if (!string.IsNullOrWhiteSpace(normalizedTag))
                    {
                        tags.Add(normalizedTag);
                    }
                }

                if (tags.Count > 0)
                {
                    folderTagsByDirectory[key] = tags;
                }
            }

            foreach (string tag in cacheData.ActiveIncludeTagFilters ?? new List<string>())
            {
                string normalizedTag = NormalizeTag(tag);
                if (!string.IsNullOrWhiteSpace(normalizedTag))
                {
                    activeIncludeTagFilters.Add(normalizedTag);
                }
            }
            foreach (string tag in cacheData.ActiveExcludeTagFilters ?? new List<string>())
            {
                string normalizedTag = NormalizeTag(tag);
                if (!string.IsNullOrWhiteSpace(normalizedTag))
                {
                    activeExcludeTagFilters.Add(normalizedTag);
                }
            }

            savedTagPanelWidth = Math.Clamp(cacheData.TagPanelWidth, 180, 520);
            tagPanelCollapsed = cacheData.TagPanelCollapsed;
            if (cacheData.TagFilterWindowState != null)
            {
                tagFilterWindowState.Width = Math.Max(380, cacheData.TagFilterWindowState.Width);
                tagFilterWindowState.Height = Math.Max(420, cacheData.TagFilterWindowState.Height);
                tagFilterWindowState.IsMaximized = cacheData.TagFilterWindowState.IsMaximized;
            }

            RefreshKnownTagsCollection();
            PersistTagState(saveToDisk: true);
        }

        private FolderTagCacheFileData? TryLoadFolderTagCacheFromDisk()
        {
            EnsureFolderTagCacheFilePath();
            if (!File.Exists(folderTagCacheFilePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(folderTagCacheFilePath);
                FolderTagCacheFileData? loaded = JsonSerializer.Deserialize<FolderTagCacheFileData>(json, FolderTagCacheJsonOptions);
                if (loaded == null)
                {
                    return null;
                }

                loaded.FolderTags ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                loaded.ActiveIncludeTagFilters ??= new List<string>();
                loaded.ActiveExcludeTagFilters ??= new List<string>();
                if (loaded.ActiveIncludeTagFilters.Count == 0 && loaded.ActiveTagFilters != null)
                {
                    loaded.ActiveIncludeTagFilters = loaded.ActiveTagFilters;
                }
                loaded.TagFilterWindowState ??= new VisualizerWindowState
                {
                    Width = 500,
                    Height = 560,
                    IsMaximized = false
                };

                return loaded;
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Unable to load folder tag cache from disk.", ex);
                return null;
            }
        }

        private void MergeLegacySaveFileTags(FolderTagCacheFileData cacheData)
        {
            if (cacheData == null)
            {
                return;
            }

            Dictionary<string, List<string>> legacyTarget = cacheData.FolderTags
                ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            cacheData.FolderTags = legacyTarget;
            cacheData.ActiveIncludeTagFilters ??= new List<string>();
            cacheData.ActiveExcludeTagFilters ??= new List<string>();
            bool hasCacheEntries = legacyTarget.Count > 0;
            if (hasCacheEntries)
            {
                return;
            }

            foreach (KeyValuePair<string, List<string>> pair in SaveFile.Data.CharacterFolderTags ?? new Dictionary<string, List<string>>())
            {
                string key = NormalizeTagAssignmentKey(pair.Key);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                legacyTarget[key] = (pair.Value ?? new List<string>())
                    .Select(NormalizeTag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (cacheData.ActiveIncludeTagFilters.Count == 0)
            {
                cacheData.ActiveIncludeTagFilters = (SaveFile.Data.CharacterFolderActiveTagFilters ?? new List<string>())
                    .Select(NormalizeTag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (Math.Abs(cacheData.TagPanelWidth) < 0.1)
            {
                cacheData.TagPanelWidth = SaveFile.Data.CharacterFolderTagPanelWidth <= 0
                    ? 260
                    : SaveFile.Data.CharacterFolderTagPanelWidth;
                cacheData.TagPanelCollapsed = SaveFile.Data.CharacterFolderTagPanelCollapsed;
            }
        }

        private void PersistTagState(bool saveToDisk)
        {
            if (!tagPanelCollapsed)
            {
                savedTagPanelWidth = Math.Clamp(TagPanelColumn.ActualWidth, 180, 520);
            }

            if (!saveToDisk)
            {
                return;
            }

            EnsureFolderTagCacheFilePath();
            try
            {
                FolderTagCacheFileData data = new FolderTagCacheFileData
                {
                    Version = FolderTagCacheVersion,
                    FolderTags = folderTagsByDirectory
                        .Where(pair => pair.Value.Count > 0)
                        .ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToList(),
                            StringComparer.OrdinalIgnoreCase),
                    ActiveIncludeTagFilters = activeIncludeTagFilters
                        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    ActiveExcludeTagFilters = activeExcludeTagFilters
                        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    TagPanelWidth = savedTagPanelWidth,
                    TagPanelCollapsed = tagPanelCollapsed,
                    TagFilterWindowState = new VisualizerWindowState
                    {
                        Width = Math.Max(380, tagFilterWindowState.Width),
                        Height = Math.Max(420, tagFilterWindowState.Height),
                        IsMaximized = tagFilterWindowState.IsMaximized
                    }
                };

                string json = JsonSerializer.Serialize(data, FolderTagCacheJsonOptions);
                File.WriteAllText(folderTagCacheFilePath, json);
                hasPendingTagSave = false;
                tagSaveDebounceTimer.Stop();
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Unable to persist folder tag cache.", ex);
            }
        }

        private void QueueTagStateSave()
        {
            PersistTagState(saveToDisk: false);
            hasPendingTagSave = true;
            tagSaveDebounceTimer.Stop();
            tagSaveDebounceTimer.Start();
        }

        private void TagSaveDebounceTimer_Tick(object? sender, EventArgs e)
        {
            tagSaveDebounceTimer.Stop();
            if (!hasPendingTagSave)
            {
                return;
            }

            PersistTagState(saveToDisk: true);
        }

        private void ApplyTagPanelState()
        {
            bool isCollapsed = tagPanelCollapsed;
            double width = Math.Clamp(savedTagPanelWidth, 180, 520);
            TagPanelColumn.MinWidth = isCollapsed ? 0 : 180;
            TagPanelColumn.Width = isCollapsed ? new GridLength(0) : new GridLength(width);
            TagPanelSplitterColumn.Width = isCollapsed ? new GridLength(0) : new GridLength(5);
            TagPanelSplitter.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleTagPanelButton.Content = isCollapsed ? "▶" : "◀";
            ToggleTagPanelButton.ToolTip = isCollapsed ? "Expand tag panel" : "Collapse tag panel";
            ToggleTagPanelTopButton.Content = isCollapsed ? "Show Tags" : "Hide Tags";
        }

        private static string NormalizeTag(string? rawTag)
        {
            string value = rawTag?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value;
        }

        private static void EnsureFolderTagCacheFilePath()
        {
            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache");
            Directory.CreateDirectory(cacheRoot);
            string desiredPath = Path.Combine(cacheRoot, "character_folder_tags.json");
            if (!folderTagCachePathInitialized || !string.Equals(folderTagCacheFilePath, desiredPath, StringComparison.OrdinalIgnoreCase))
            {
                folderTagCacheFilePath = desiredPath;
                folderTagCachePathInitialized = true;
            }
        }

        private static string NormalizeTagAssignmentKey(string? pathOrFolderName)
        {
            string value = pathOrFolderName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                string trimmedPath = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string folderName = Path.GetFileName(trimmedPath);
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    return folderName.Trim();
                }
            }
            catch
            {
                // ignored
            }

            return value;
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

            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            RECT workArea = monitorInfo.rcWork;
            RECT monitorArea = monitorInfo.rcMonitor;

            mmi.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
            mmi.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
            mmi.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
            mmi.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void BindViewPresets(bool applySelectedView = true)
        {
            suppressViewSelectionChanged = true;

            ViewModeCombo.ItemsSource = null;
            ViewModeCombo.ItemsSource = visualizerConfig.Presets;

            FolderVisualizerViewPreset? selectedPreset = visualizerConfig.Presets.FirstOrDefault(p =>
                string.Equals(p.Id, visualizerConfig.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
            if (selectedPreset == null && !string.IsNullOrWhiteSpace(visualizerConfig.SelectedPresetName))
            {
                selectedPreset = visualizerConfig.Presets.FirstOrDefault(p =>
                    string.Equals(p.Name, visualizerConfig.SelectedPresetName, StringComparison.OrdinalIgnoreCase));
            }

            if (selectedPreset == null && visualizerConfig.Presets.Count > 0)
            {
                selectedPreset = visualizerConfig.Presets[0];
                visualizerConfig.SelectedPresetId = selectedPreset.Id;
                visualizerConfig.SelectedPresetName = selectedPreset.Name;
            }

            ViewModeCombo.SelectedItem = selectedPreset;
            suppressViewSelectionChanged = false;
            if (applySelectedView)
            {
                ApplySelectedViewPreset();
            }
        }

        private async Task LoadCharacterItemsAsync(bool forceRebuild = false)
        {
            List<CharacterFolder> characters = CharacterFolder.FullList;
            string signature = BuildCharacterSignature(characters);

            if (!forceRebuild && TryLoadProjectedItemsFromDisk(signature, out List<FolderVisualizerItem>? diskCachedItems))
            {
                await WaitForm.ShowFormAsync("Loading character folder visualizer...", this);
                try
                {
                    WaitForm.SetSubtitle("Loading indexed data from disk cache...");
                    allItems.Clear();
                    lock (progressiveLoadKeyLock)
                    {
                        progressiveLoadedItemKeys.Clear();
                    }
                    allItems.AddRange(diskCachedItems);
                    RecomputeDerivedItemFields();
                    itemsView = null;
                    UpdateSummaryText();
                    PruneTagAssignmentsToExistingItems();
                    RefreshSelectedFolderTagPanel();

                    WaitForm.SetSubtitle("Rendering selected view...");
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    ApplySelectedViewPreset();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    StartProgressiveImageLoading();
                }
                finally
                {
                    WaitForm.CloseForm();
                }

                return;
            }

            await WaitForm.ShowFormAsync("Loading character folder visualizer...", this);
            try
            {
                List<FolderVisualizerItem> projected = await Task.Run(() => BuildCharacterItems(characters));

                SaveProjectedItemsToDisk(signature, projected);

                allItems.Clear();
                lock (progressiveLoadKeyLock)
                {
                    progressiveLoadedItemKeys.Clear();
                }
                allItems.AddRange(projected);
                RecomputeDerivedItemFields();
                itemsView = null;
                UpdateSummaryText();
                PruneTagAssignmentsToExistingItems();
                RefreshSelectedFolderTagPanel();
                WaitForm.SetSubtitle("Rendering selected view...");
                await Dispatcher.Yield(DispatcherPriority.Background);
                ApplySelectedViewPreset();
                await Dispatcher.Yield(DispatcherPriority.Background);
                StartProgressiveImageLoading();
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private string BuildCharacterSignature(IReadOnlyList<CharacterFolder> characters)
        {
            StringBuilder payloadBuilder = new StringBuilder(characters.Count * 96);
            payloadBuilder.Append("v2").Append('|').Append(characters.Count).Append('|');

            IEnumerable<CharacterFolder> orderedCharacters = characters
                .OrderBy(folder => folder.DirectoryPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (CharacterFolder folder in orderedCharacters)
            {
                string normalizedName = (folder.Name ?? string.Empty).Trim().ToLowerInvariant();
                string normalizedDirectoryPath = (folder.DirectoryPath ?? string.Empty).Trim().ToLowerInvariant();
                string normalizedIconPath = (folder.CharIconPath ?? string.Empty).Trim().ToLowerInvariant();
                string normalizedIdlePath = (folder.ViewportIdleSpritePath ?? string.Empty).Trim().ToLowerInvariant();

                payloadBuilder.Append(normalizedName).Append('|');
                payloadBuilder.Append(normalizedDirectoryPath).Append('|');
                payloadBuilder.Append(normalizedIconPath).Append('|');
                payloadBuilder.Append(normalizedIdlePath).Append('|');
                if (TryGetFolderPreviewOverrideEmoteId(folder.DirectoryPath, out int overrideEmoteId))
                {
                    payloadBuilder.Append(overrideEmoteId);
                }

                payloadBuilder.Append(';');
            }

            byte[] signatureBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payloadBuilder.ToString()));
            return Convert.ToHexString(signatureBytes).ToLowerInvariant();
        }

        private List<FolderVisualizerItem> BuildCharacterItems(List<CharacterFolder> sourceCharacters)
        {
            List<CharacterFolder> sortedCharacters = sourceCharacters
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = sortedCharacters.Count;
            List<FolderVisualizerItem> items = new List<FolderVisualizerItem>(total);

            for (int i = 0; i < total; i++)
            {
                CharacterFolder characterFolder = sortedCharacters[i];

                if (i % 24 == 0)
                {
                    WaitForm.SetSubtitle($"Parsed Folder: {characterFolder.Name} ({i + 1}/{total})");
                }

                string previewPath = ResolvePreferredPreviewPath(characterFolder);

                string iconPath = characterFolder.CharIconPath ?? string.Empty;
                string charIniPath = characterFolder.PathToConfigIni ?? string.Empty;
                DateTime lastModified = File.Exists(charIniPath)
                    ? File.GetLastWriteTime(charIniPath)
                    : Directory.GetLastWriteTime(characterFolder.DirectoryPath ?? string.Empty);
                int emoteCount = characterFolder.configINI?.EmotionsCount ?? 0;
                long sizeBytes = GetDirectorySizeSafe(characterFolder.DirectoryPath ?? string.Empty);
                string readmePath = ResolveCharacterReadmePath(characterFolder.DirectoryPath ?? string.Empty);
                CharacterIntegrityReport? integrityReport = null;
                CharacterIntegrityVerifier.TryLoadPersistedReport(characterFolder.DirectoryPath ?? string.Empty, out integrityReport);
                string integrityFailureMessages = BuildIntegrityFailureMessages(integrityReport);

                items.Add(new FolderVisualizerItem
                {
                    Index = i + 1,
                    IndexText = (i + 1).ToString(),
                    RowPositionText = (i + 1).ToString(),
                    Name = characterFolder.Name,
                    DirectoryPath = characterFolder.DirectoryPath ?? string.Empty,
                    IconPath = iconPath,
                    PreviewPath = previewPath,
                    CharIniPath = charIniPath,
                    HasCharIni = !string.IsNullOrWhiteSpace(charIniPath) && File.Exists(charIniPath),
                    LastModified = lastModified,
                    LastModifiedText = lastModified.ToString("yyyy-MM-dd HH:mm"),
                    EmoteCount = emoteCount,
                    EmoteCountText = emoteCount.ToString(),
                    SizeBytes = sizeBytes,
                    SizeText = FormatBytes(sizeBytes),
                    ReadmePath = readmePath,
                    HasReadme = !string.IsNullOrWhiteSpace(readmePath),
                    IconTypeText = ResolveIconType(iconPath),
                    TagsText = string.Empty,
                    IntegrityHasFailures = integrityReport?.HasFailures == true,
                    IntegrityFailureCount = integrityReport?.FailureCount ?? 0,
                    IntegrityFailureMessages = integrityFailureMessages,
                    IconImage = FallbackFolderImage,
                    PreviewImage = FallbackFolderImage
                });
            }

            return items;
        }

        private async void ViewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressViewSelectionChanged)
            {
                return;
            }

            await ApplySelectedViewPresetAsync(showWaitForm: true);
        }

        private void ApplySelectedViewPreset()
        {
            if (FolderListView == null)
            {
                return;
            }

            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset == null && visualizerConfig.Presets.Count > 0)
            {
                selectedPreset = visualizerConfig.Presets[0];
                ViewModeCombo.SelectedItem = selectedPreset;
            }

            if (selectedPreset == null)
            {
                return;
            }

            visualizerConfig.SelectedPresetId = selectedPreset.Id;
            visualizerConfig.SelectedPresetName = selectedPreset.Name;
            PersistVisualizerConfig();

            if (selectedPreset.Mode == FolderVisualizerLayoutMode.Table)
            {
                ApplyTablePreset(selectedPreset);
            }
            else
            {
                ApplyNormalPreset(selectedPreset.Normal);
            }
        }

        private async Task ApplySelectedViewPresetAsync(bool showWaitForm)
        {
            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset == null)
            {
                ApplySelectedViewPreset();
                return;
            }

            bool shouldShowWaitForm = showWaitForm && hasLoaded && allItems.Count > 0;
            if (!shouldShowWaitForm)
            {
                ApplySelectedViewPreset();
                return;
            }

            await WaitForm.ShowFormAsync("Applying visualizer view...", this);
            try
            {
                string modeText = selectedPreset.Mode == FolderVisualizerLayoutMode.Table ? "Table" : "Normal";
                WaitForm.SetSubtitle($"Switching to view: {selectedPreset.Name} ({modeText})");

                FolderListView.ItemsSource = null;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                ApplySelectedViewPreset();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private void ApplyNormalPreset(FolderVisualizerNormalViewConfig normal)
        {
            UntrackTableColumnWidth();
            TileWidth = normal.TileWidth;
            TileHeight = normal.TileHeight;
            IconSize = normal.IconSize;
            TileNameFontSize = normal.NameFontSize;
            InternalTileRowPadding = new Thickness(0, normal.InternalTilePadding, 0, normal.InternalTilePadding);
            normalScrollWheelStep = normal.ScrollWheelStep;
            TilePadding = new Thickness(normal.TilePadding);
            TileMargin = new Thickness(normal.TileMargin);

            FolderListView.FontSize = normal.NameFontSize;
            FolderListView.View = null;
            FolderListView.ItemTemplate = (DataTemplate)FindResource("IconTileTemplate");
            FolderListView.ItemContainerStyle = (Style)FindResource("VisualizerIconItemStyle");
            FolderListView.ItemsPanel = (ItemsPanelTemplate)FindResource("IconItemsPanelTemplate");
            ScrollViewer.SetHorizontalScrollBarVisibility(FolderListView, ScrollBarVisibility.Disabled);
            FolderListView.ItemsSource = null;
            FolderListView.ItemsSource = GetOrCreateItemsView();
            EnsureFolderListScrollViewerHooked();
            UpdateSummaryText();
        }

        private void ApplyTablePreset(FolderVisualizerViewPreset preset)
        {
            FolderVisualizerTableViewConfig table = preset.Table;
            DetailsRowHeight = table.RowHeight;
            FolderListView.ItemTemplate = null;
            FolderListView.ItemsPanel = (ItemsPanelTemplate)FindResource("DetailsItemsPanelTemplate");
            FolderListView.ItemContainerStyle = BuildDetailsContainerStyle(table);
            FolderListView.View = BuildDetailsGridView(preset);
            ScrollViewer.SetHorizontalScrollBarVisibility(FolderListView, ScrollBarVisibility.Auto);
            FolderListView.ItemsSource = null;
            FolderListView.ItemsSource = GetOrCreateItemsView();
            EnsureFolderListScrollViewerHooked();
            ApplySortToCurrentView();
            UpdateSummaryText();
        }

        private ICollectionView GetOrCreateItemsView()
        {
            if (itemsView == null || !ReferenceEquals(itemsView.SourceCollection, allItems))
            {
                itemsView = CollectionViewSource.GetDefaultView(allItems);
                itemsView.Filter = FilterItemBySearch;
            }

            return itemsView;
        }

        private bool FilterItemBySearch(object obj)
        {
            if (obj is not FolderVisualizerItem item)
            {
                return false;
            }

            if (activeIncludeTagFilters.Count > 0 || activeExcludeTagFilters.Count > 0)
            {
                HashSet<string> itemTagSet = GetTagsForItem(item).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool includesUntaggedOnly = activeIncludeTagFilters.Contains(UntaggedFilterToken);
                if (includesUntaggedOnly)
                {
                    if (itemTagSet.Count != 0)
                    {
                        return false;
                    }
                }
                else
                {
                    List<string> requiredTags = activeIncludeTagFilters.ToList();
                    if (requiredTags.Count > 0 && !requiredTags.All(tag => itemTagSet.Contains(tag)))
                    {
                        return false;
                    }
                }

                List<string> excludedTags = activeExcludeTagFilters
                    .Where(tag => !string.Equals(tag, UntaggedFilterToken, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (excludedTags.Any(tag => itemTagSet.Contains(tag)))
                {
                    return false;
                }
            }

            if (!MatchesAdvancedFilters(item))
            {
                return false;
            }

            string query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            IEnumerable<string> searchableValues = GetSearchableValues(item);
            foreach (string value in searchableValues)
            {
                if (!string.IsNullOrWhiteSpace(value)
                    && value.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesAdvancedFilters(FolderVisualizerItem item)
        {
            if (activeFilterRoot == null || !activeFilterRoot.IsGroup || activeFilterRoot.Children.Count == 0)
            {
                return true;
            }

            return EvaluateGroupRule(activeFilterRoot, item);
        }

        private static bool EvaluateGroupRule(FolderFilterRule groupRule, FolderVisualizerItem item)
        {
            if (!groupRule.IsActive)
            {
                return true;
            }

            List<FolderFilterRule> activeChildren = groupRule.Children
                .Where(child => child != null && child.IsActive)
                .ToList();
            if (activeChildren.Count == 0)
            {
                return true;
            }

            bool result = EvaluateRuleNode(activeChildren[0], item);
            for (int i = 1; i < activeChildren.Count; i++)
            {
                bool current = EvaluateRuleNode(activeChildren[i], item);
                result = groupRule.Connector == FolderFilterConnector.Or
                    ? (result || current)
                    : (result && current);
            }

            return result;
        }

        private static bool EvaluateRuleNode(FolderFilterRule rule, FolderVisualizerItem item)
        {
            if (rule.IsGroup)
            {
                return EvaluateGroupRule(rule, item);
            }

            object? value = GetFilterValue(item, rule.ColumnKey);
            if (rule.UsesEnumSelection)
            {
                List<string> selectedValues = rule.EnumOptions
                    .Where(option => option.IsSelected)
                    .Select(option => option.Value)
                    .ToList();
                if (selectedValues.Count == 0)
                {
                    return true;
                }

                string actual = value switch
                {
                    bool boolEnumValue => boolEnumValue ? "True" : "False",
                    _ => value?.ToString() ?? string.Empty
                };

                bool contains = selectedValues.Any(selected =>
                    string.Equals(selected, actual, StringComparison.OrdinalIgnoreCase));

                return rule.Operator == FolderFilterOperator.NotEquals ? !contains : contains;
            }

            if (value is DateTime dateValue)
            {
                return EvaluateDateRule(rule, dateValue);
            }

            if (value is long longValue)
            {
                return EvaluateLongRule(rule, longValue);
            }

            if (value is int intValue)
            {
                return EvaluateLongRule(rule, intValue);
            }

            if (value is bool boolValue)
            {
                return EvaluateBoolRule(rule, boolValue);
            }

            string textValue = value?.ToString() ?? string.Empty;
            return EvaluateTextRule(rule, textValue);
        }

        private static object? GetFilterValue(FolderVisualizerItem item, FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.RowNumber => item.Index,
                FolderVisualizerTableColumnKey.IconType => item.IconTypeText,
                FolderVisualizerTableColumnKey.Name => item.Name,
                FolderVisualizerTableColumnKey.Tags => item.TagsText,
                FolderVisualizerTableColumnKey.DirectoryPath => item.DirectoryPath,
                FolderVisualizerTableColumnKey.PreviewPath => item.PreviewPath,
                FolderVisualizerTableColumnKey.LastModified => item.LastModified,
                FolderVisualizerTableColumnKey.EmoteCount => item.EmoteCount,
                FolderVisualizerTableColumnKey.Size => item.SizeBytes,
                FolderVisualizerTableColumnKey.IntegrityFailures => item.IntegrityFailureMessages,
                FolderVisualizerTableColumnKey.OpenCharIni => item.HasCharIni,
                FolderVisualizerTableColumnKey.Readme => item.HasReadme,
                _ => string.Empty
            };
        }

        private static bool EvaluateTextRule(FolderFilterRule rule, string value)
        {
            string left = value ?? string.Empty;
            string right = rule.Value ?? string.Empty;
            string rightSecond = rule.SecondValue ?? string.Empty;

            return rule.Operator switch
            {
                FolderFilterOperator.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                FolderFilterOperator.DoesNotContain => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
                FolderFilterOperator.Equals => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                FolderFilterOperator.NotEquals => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                FolderFilterOperator.StartsWith => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
                FolderFilterOperator.EndsWith => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
                FolderFilterOperator.InList => SplitFilterList(right).Any(entry => string.Equals(entry, left, StringComparison.OrdinalIgnoreCase)),
                FolderFilterOperator.NotInList => SplitFilterList(right).All(entry => !string.Equals(entry, left, StringComparison.OrdinalIgnoreCase)),
                FolderFilterOperator.Between => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) >= 0
                    && string.Compare(left, rightSecond, StringComparison.OrdinalIgnoreCase) <= 0,
                _ => true
            };
        }

        private static bool EvaluateLongRule(FolderFilterRule rule, long value)
        {
            if (!long.TryParse(rule.Value, out long first))
            {
                first = 0;
            }

            if (!long.TryParse(rule.SecondValue, out long second))
            {
                second = first;
            }

            return rule.Operator switch
            {
                FolderFilterOperator.Equals => value == first,
                FolderFilterOperator.NotEquals => value != first,
                FolderFilterOperator.GreaterThan => value > first,
                FolderFilterOperator.GreaterThanOrEqual => value >= first,
                FolderFilterOperator.LessThan => value < first,
                FolderFilterOperator.LessThanOrEqual => value <= first,
                FolderFilterOperator.Between => value >= Math.Min(first, second) && value <= Math.Max(first, second),
                _ => true
            };
        }

        private static bool EvaluateDateRule(FolderFilterRule rule, DateTime value)
        {
            if (!DateTime.TryParse(rule.Value, out DateTime first))
            {
                first = DateTime.MinValue;
            }

            if (!DateTime.TryParse(rule.SecondValue, out DateTime second))
            {
                second = first;
            }

            DateTime safeValue = value.Date;
            DateTime safeFirst = first.Date;
            DateTime safeSecond = second.Date;

            return rule.Operator switch
            {
                FolderFilterOperator.Equals => safeValue == safeFirst,
                FolderFilterOperator.NotEquals => safeValue != safeFirst,
                FolderFilterOperator.GreaterThan => safeValue > safeFirst,
                FolderFilterOperator.GreaterThanOrEqual => safeValue >= safeFirst,
                FolderFilterOperator.LessThan => safeValue < safeFirst,
                FolderFilterOperator.LessThanOrEqual => safeValue <= safeFirst,
                FolderFilterOperator.Between => safeValue >= (safeFirst <= safeSecond ? safeFirst : safeSecond)
                    && safeValue <= (safeFirst <= safeSecond ? safeSecond : safeFirst),
                _ => true
            };
        }

        private static bool EvaluateBoolRule(FolderFilterRule rule, bool value)
        {
            bool expected = true;
            if (!bool.TryParse(rule.Value, out expected))
            {
                expected = string.Equals(rule.Value, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.Value, "1", StringComparison.OrdinalIgnoreCase);
            }

            return rule.Operator switch
            {
                FolderFilterOperator.Equals => value == expected,
                FolderFilterOperator.NotEquals => value != expected,
                _ => true
            };
        }

        private static IEnumerable<string> SplitFilterList(string input)
        {
            return (input ?? string.Empty)
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value));
        }

        private IEnumerable<string> GetSearchableValues(FolderVisualizerItem item)
        {
            if (FolderListView.View == null)
            {
                yield return item.Name;
                yield return item.DirectoryPath;
                yield return item.PreviewPath;
                yield return item.IntegrityFailureMessages;
                foreach (string tag in GetTagsForItem(item))
                {
                    yield return tag;
                }
                yield break;
            }

            foreach (FolderVisualizerTableColumnKey key in GetVisibleSearchColumnKeys())
            {
                string value = key switch
                {
                    FolderVisualizerTableColumnKey.RowNumber => item.IndexText,
                    FolderVisualizerTableColumnKey.IconType => item.IconTypeText,
                    FolderVisualizerTableColumnKey.Name => item.Name,
                    FolderVisualizerTableColumnKey.Tags => item.TagsText,
                    FolderVisualizerTableColumnKey.DirectoryPath => item.DirectoryPath,
                    FolderVisualizerTableColumnKey.PreviewPath => item.PreviewPath,
                    FolderVisualizerTableColumnKey.LastModified => item.LastModifiedText,
                    FolderVisualizerTableColumnKey.EmoteCount => item.EmoteCountText,
                    FolderVisualizerTableColumnKey.Size => item.SizeText,
                    FolderVisualizerTableColumnKey.IntegrityFailures => item.IntegrityFailureMessages,
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
            foreach (string tag in GetTagsForItem(item))
            {
                yield return tag;
            }
        }

        private IEnumerable<FolderVisualizerTableColumnKey> GetVisibleSearchColumnKeys()
        {
            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset == null || selectedPreset.Mode != FolderVisualizerLayoutMode.Table)
            {
                yield return FolderVisualizerTableColumnKey.Name;
                yield break;
            }

            List<FolderVisualizerTableColumnConfig> columns = selectedPreset.Table.Columns
                .Where(column => column.IsVisible)
                .OrderBy(column => column.Order)
                .ToList();

            if (ShowIntegrityVerifierResults && columns.All(column => column.Key != FolderVisualizerTableColumnKey.IntegrityFailures))
            {
                yield return FolderVisualizerTableColumnKey.IntegrityFailures;
            }

            foreach (FolderVisualizerTableColumnConfig column in columns)
            {
                yield return column.Key;
            }
        }

        private IEnumerable<string> GetTagsForItem(FolderVisualizerItem item)
        {
            if (item == null)
            {
                return Enumerable.Empty<string>();
            }

            HashSet<string> resolvedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in GetTagLookupKeysForItem(item))
            {
                if (!folderTagsByDirectory.TryGetValue(key, out HashSet<string>? tags) || tags.Count == 0)
                {
                    continue;
                }

                foreach (string tag in tags)
                {
                    resolvedTags.Add(tag);
                }
            }

            return resolvedTags;
        }

        private void RecomputeDerivedItemFields()
        {
            for (int i = 0; i < allItems.Count; i++)
            {
                FolderVisualizerItem item = allItems[i];
                item.Index = i + 1;
                item.IndexText = (i + 1).ToString();
                item.RowPositionText = (i + 1).ToString();
                item.IconTypeText = ResolveIconType(item.IconPath);
                item.TagsText = string.Join(", ", GetTagsForItem(item).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            }
        }

        private void UpdateVisibleRowPositions()
        {
            if (FolderListView.Items == null)
            {
                return;
            }

            for (int i = 0; i < FolderListView.Items.Count; i++)
            {
                if (FolderListView.Items[i] is FolderVisualizerItem item)
                {
                    item.RowPositionText = (i + 1).ToString();
                }
            }
        }

        private void RefreshItemTagTexts()
        {
            foreach (FolderVisualizerItem item in allItems)
            {
                item.TagsText = string.Join(", ", GetTagsForItem(item).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            }
        }

        private static string ResolveIconType(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                return "Placeholder";
            }

            string fileName = Path.GetFileNameWithoutExtension(iconPath) ?? string.Empty;
            if (fileName.Contains("char", StringComparison.OrdinalIgnoreCase)
                && fileName.Contains("icon", StringComparison.OrdinalIgnoreCase))
            {
                return "Character Icon";
            }

            return "Button Fallback";
        }

        private static IEnumerable<string> GetTagLookupKeysForItem(FolderVisualizerItem item)
        {
            string directoryKey = NormalizeTagAssignmentKey(item.DirectoryPath);
            if (!string.IsNullOrWhiteSpace(directoryKey))
            {
                yield return directoryKey;
            }

            string nameKey = NormalizeTagAssignmentKey(item.Name);
            if (!string.IsNullOrWhiteSpace(nameKey) && !string.Equals(nameKey, directoryKey, StringComparison.OrdinalIgnoreCase))
            {
                yield return nameKey;
            }
        }

        private static string GetPrimaryTagKeyForItem(FolderVisualizerItem item)
        {
            foreach (string key in GetTagLookupKeysForItem(item))
            {
                return key;
            }

            return string.Empty;
        }

        private Style BuildDetailsContainerStyle(FolderVisualizerTableViewConfig table)
        {
            Style baseStyle = (Style)FindResource("VisualizerDetailsItemStyle");
            Style style = new Style(typeof(ListViewItem), baseStyle);
            style.Setters.Add(new Setter(FontSizeProperty, table.FontSize));
            Binding rowHeightBinding = new Binding(nameof(DetailsRowHeight))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(CharacterFolderVisualizerWindow), 1)
            };
            style.Setters.Add(new Setter(HeightProperty, rowHeightBinding));
            return style;
        }

        private void DetailsRowResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (FolderListView.View == null || sender is not Thumb thumb || !ReferenceEquals(thumb, activeRowResizeThumb))
            {
                return;
            }

            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset == null || selectedPreset.Mode != FolderVisualizerLayoutMode.Table)
            {
                return;
            }

            double mouseY = Mouse.GetPosition(FolderListView).Y;
            double minGuideY = rowResizeGuideStartY + (22 - rowResizeStartHeight);
            double maxGuideY = rowResizeGuideStartY + (140 - rowResizeStartHeight);
            double clampedGuideY = Math.Clamp(mouseY, Math.Min(minGuideY, maxGuideY), Math.Max(minGuideY, maxGuideY));

            rowResizePreviewHeight = Math.Clamp(rowResizeStartHeight + (clampedGuideY - rowResizeGuideStartY), 22, 140);
            UpdateRowResizeGuide(clampedGuideY);
            e.Handled = true;
        }

        private void DetailsRowResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (FolderListView.View == null || sender is not Thumb thumb)
            {
                return;
            }

            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset == null || selectedPreset.Mode != FolderVisualizerLayoutMode.Table)
            {
                return;
            }

            activeRowResizeThumb = thumb;
            rowResizeStartHeight = selectedPreset.Table.RowHeight;
            rowResizePreviewHeight = rowResizeStartHeight;
            rowResizeGuideStartY = Math.Clamp(Mouse.GetPosition(FolderListView).Y, 0, Math.Max(0, FolderListView.ActualHeight - 1));
            ShowRowResizeGuide(rowResizeGuideStartY);
            e.Handled = true;
        }

        private void DetailsRowResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is not Thumb thumb || !ReferenceEquals(thumb, activeRowResizeThumb))
            {
                return;
            }

            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset != null && selectedPreset.Mode == FolderVisualizerLayoutMode.Table)
            {
                if (Math.Abs(selectedPreset.Table.RowHeight - rowResizePreviewHeight) > 0.01)
                {
                    selectedPreset.Table.RowHeight = rowResizePreviewHeight;
                    DetailsRowHeight = rowResizePreviewHeight;
                }
            }

            activeRowResizeThumb = null;
            HideRowResizeGuide();
            PersistVisualizerConfig();
            e.Handled = true;
        }

        private void ShowRowResizeGuide(double verticalOffset)
        {
            HideRowResizeGuide();
            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(FolderListView);
            if (layer == null)
            {
                return;
            }

            rowResizeAdornerLayer = layer;
            rowResizeGuideAdorner = new RowResizeGuideAdorner(FolderListView);
            rowResizeGuideAdorner.SetGuideY(verticalOffset);
            layer.Add(rowResizeGuideAdorner);
        }

        private void UpdateRowResizeGuide(double verticalOffset)
        {
            if (rowResizeGuideAdorner == null)
            {
                return;
            }

            rowResizeGuideAdorner.SetGuideY(verticalOffset);
        }

        private void HideRowResizeGuide()
        {
            if (rowResizeAdornerLayer != null && rowResizeGuideAdorner != null)
            {
                rowResizeAdornerLayer.Remove(rowResizeGuideAdorner);
            }

            rowResizeGuideAdorner = null;
            rowResizeAdornerLayer = null;
        }

        private GridView BuildDetailsGridView(FolderVisualizerViewPreset preset)
        {
            FolderVisualizerTableViewConfig table = preset.Table;
            UntrackTableColumnWidth();
            tableColumnMap.Clear();

            GridView gridView = new GridView
            {
                AllowsColumnReorder = false,
                ColumnHeaderContainerStyle = (Style)FindResource("VisualizerGridHeaderStyle")
            };

            GridViewColumn rowHeaderColumn = new GridViewColumn
            {
                Header = "#",
                Width = 56,
                CellTemplate = (DataTemplate)FindResource("DetailsRowHeaderTemplate")
            };
            gridView.Columns.Add(rowHeaderColumn);

            List<FolderVisualizerTableColumnConfig> orderedColumns = table.Columns
                .Where(column => column.IsVisible)
                .OrderBy(column => column.Order)
                .ToList();

            if (ShowIntegrityVerifierResults)
            {
                FolderVisualizerTableColumnConfig? integrityColumn = table.Columns.FirstOrDefault(
                    column => column.Key == FolderVisualizerTableColumnKey.IntegrityFailures);
                if (integrityColumn == null)
                {
                    integrityColumn = new FolderVisualizerTableColumnConfig
                    {
                        Key = FolderVisualizerTableColumnKey.IntegrityFailures,
                        IsVisible = true,
                        Order = orderedColumns.Count,
                        Width = 420
                    };
                }

                if (orderedColumns.All(column => column.Key != FolderVisualizerTableColumnKey.IntegrityFailures))
                {
                    orderedColumns.Add(integrityColumn);
                    orderedColumns = orderedColumns.OrderBy(column => column.Order).ToList();
                }
            }

            if (orderedColumns.Count == 0)
            {
                orderedColumns.Add(new FolderVisualizerTableColumnConfig
                {
                    Key = FolderVisualizerTableColumnKey.Name,
                    IsVisible = true,
                    Order = 0,
                    Width = 320
                });
            }

            foreach (FolderVisualizerTableColumnConfig column in orderedColumns)
            {
                GridViewColumn gridColumn = new GridViewColumn
                {
                    Header = CreateHeaderInfo(column.Key),
                    Width = column.Width
                };

                if (column.Key == FolderVisualizerTableColumnKey.Icon)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsIconTemplate");
                }
                else if (column.Key == FolderVisualizerTableColumnKey.RowNumber)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsRowNumberTemplate");
                }
                else if (column.Key == FolderVisualizerTableColumnKey.Tags)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsTagsTemplate");
                }
                else if (column.Key == FolderVisualizerTableColumnKey.EmoteCount)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsEmoteCountCenteredTemplate");
                }
                else if (column.Key == FolderVisualizerTableColumnKey.Size)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsSizeCenteredTemplate");
                }
                else if (column.Key == FolderVisualizerTableColumnKey.OpenCharIni)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("OpenCharIniTemplate");
                }
                else if (column.Key == FolderVisualizerTableColumnKey.Readme)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("OpenReadmeTemplate");
                }
                else
                {
                    string bindingPath = GetColumnBindingPath(column.Key);
                    gridColumn.CellTemplate = CreateTextCellTemplate(
                        bindingPath,
                        column.Key == FolderVisualizerTableColumnKey.EmoteCount
                            || column.Key == FolderVisualizerTableColumnKey.Size
                            || column.Key == FolderVisualizerTableColumnKey.RowNumber);
                }

                gridView.Columns.Add(gridColumn);
                tableColumnMap[gridColumn] = column;
                TrackColumnWidth(gridColumn, column);
            }

            UpdateSortHeaderGlyphs();

            return gridView;
        }

        private FolderVisualizerGridHeaderInfo CreateHeaderInfo(FolderVisualizerTableColumnKey key)
        {
            string baseText = GetColumnHeader(key);
            bool sortable = IsColumnSortable(key);

            if (sortable && currentSortColumnKey == key)
            {
                string glyph = currentSortDirection == ListSortDirection.Ascending ? " ▲" : " ▼";
                return new FolderVisualizerGridHeaderInfo(key, baseText + glyph, true);
            }

            return new FolderVisualizerGridHeaderInfo(key, baseText, sortable);
        }

        private static string GetColumnHeader(FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.RowNumber => "ID",
                FolderVisualizerTableColumnKey.Icon => string.Empty,
                FolderVisualizerTableColumnKey.IconType => "Icon Type",
                FolderVisualizerTableColumnKey.Name => "Name",
                FolderVisualizerTableColumnKey.Tags => "Tags",
                FolderVisualizerTableColumnKey.DirectoryPath => "Folder Path",
                FolderVisualizerTableColumnKey.PreviewPath => "Idle Sprite",
                FolderVisualizerTableColumnKey.LastModified => "Last Modified",
                FolderVisualizerTableColumnKey.EmoteCount => "Emotes",
                FolderVisualizerTableColumnKey.Size => "Size",
                FolderVisualizerTableColumnKey.IntegrityFailures => "Integrity Failures",
                FolderVisualizerTableColumnKey.OpenCharIni => "Open Char INI",
                FolderVisualizerTableColumnKey.Readme => "Readme",
                _ => "Column"
            };
        }

        private static bool IsColumnSortable(FolderVisualizerTableColumnKey key)
        {
            return key != FolderVisualizerTableColumnKey.Icon;
        }

        private static string GetColumnBindingPath(FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.RowNumber => nameof(FolderVisualizerItem.IndexText),
                FolderVisualizerTableColumnKey.Name => nameof(FolderVisualizerItem.Name),
                FolderVisualizerTableColumnKey.Tags => nameof(FolderVisualizerItem.TagsText),
                FolderVisualizerTableColumnKey.IconType => nameof(FolderVisualizerItem.IconTypeText),
                FolderVisualizerTableColumnKey.DirectoryPath => nameof(FolderVisualizerItem.DirectoryPath),
                FolderVisualizerTableColumnKey.PreviewPath => nameof(FolderVisualizerItem.PreviewPath),
                FolderVisualizerTableColumnKey.LastModified => nameof(FolderVisualizerItem.LastModifiedText),
                FolderVisualizerTableColumnKey.EmoteCount => nameof(FolderVisualizerItem.EmoteCountText),
                FolderVisualizerTableColumnKey.Size => nameof(FolderVisualizerItem.SizeText),
                FolderVisualizerTableColumnKey.IntegrityFailures => nameof(FolderVisualizerItem.IntegrityFailureMessages),
                _ => nameof(FolderVisualizerItem.Name)
            };
        }

        private static DataTemplate CreateTextCellTemplate(string bindingPath, bool centerHorizontally)
        {
            FrameworkElementFactory textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            textBlockFactory.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            textBlockFactory.SetValue(TextBlock.TextAlignmentProperty, centerHorizontally ? TextAlignment.Center : TextAlignment.Left);
            textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, centerHorizontally ? HorizontalAlignment.Center : HorizontalAlignment.Left);
            textBlockFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);

            DataTemplate template = new DataTemplate
            {
                VisualTree = textBlockFactory
            };
            return template;
        }

        private void GridHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader header || header.Column == null)
            {
                return;
            }

            if (header.Content is not FolderVisualizerGridHeaderInfo info || !info.IsSortable)
            {
                return;
            }

            if (currentSortColumnKey == info.Key)
            {
                currentSortDirection = currentSortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            else
            {
                currentSortColumnKey = info.Key;
                currentSortDirection = ListSortDirection.Ascending;
            }

            ApplySortToCurrentView();
        }

        private void GridHeader_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not GridViewColumnHeader header || header.Column == null)
            {
                return;
            }

            ContextMenu contextMenu = new ContextMenu();
            AddContextCategoryHeader(contextMenu, "Table", addLeadingSeparator: false);

            MenuItem bestFitColumnsItem = new MenuItem
            {
                Header = "Best Fit Columns"
            };
            bestFitColumnsItem.Click += (_, _) => BestFitAllColumns();
            contextMenu.Items.Add(bestFitColumnsItem);

            MenuItem filtersAndSortingItem = new MenuItem
            {
                Header = "Filters & Sorting"
            };
            filtersAndSortingItem.Click += (_, _) => SelectTagsButton_Click(this, new RoutedEventArgs());
            contextMenu.Items.Add(filtersAndSortingItem);

            AddContextCategoryHeader(contextMenu, "Column", addLeadingSeparator: true);

            if (header.Content is FolderVisualizerGridHeaderInfo info)
            {
                MenuItem bestFitColumnItem = new MenuItem
                {
                    Header = "Best Fit Column"
                };
                bestFitColumnItem.Click += (_, _) => BestFitColumn(header.Column, info.Key, info.Text);
                contextMenu.Items.Add(bestFitColumnItem);

                MenuItem hideColumnItem = new MenuItem
                {
                    Header = "Hide Column",
                    IsEnabled = CanHideColumn(header.Column)
                };
                hideColumnItem.Click += (_, _) => HideColumn(header.Column);
                contextMenu.Items.Add(hideColumnItem);

                MenuItem sortMenuItem = BuildSortSubmenu(info.Key);
                contextMenu.Items.Add(sortMenuItem);
            }

            contextMenu.IsOpen = true;
            e.Handled = true;
        }

        private MenuItem BuildSortSubmenu(FolderVisualizerTableColumnKey key)
        {
            bool sortable = IsColumnSortable(key);
            MenuItem sortMenu = new MenuItem
            {
                Header = "Sort",
                IsEnabled = sortable
            };

            MenuItem sortAsc = new MenuItem
            {
                Header = "Sort Asc",
                IsCheckable = true,
                IsChecked = sortable
                    && currentSortColumnKey == key
                    && currentSortDirection == ListSortDirection.Ascending
            };
            sortAsc.Click += (_, _) =>
            {
                currentSortColumnKey = key;
                currentSortDirection = ListSortDirection.Ascending;
                ApplySortToCurrentView();
            };
            sortMenu.Items.Add(sortAsc);

            MenuItem sortDesc = new MenuItem
            {
                Header = "Sort Desc",
                IsCheckable = true,
                IsChecked = sortable
                    && currentSortColumnKey == key
                    && currentSortDirection == ListSortDirection.Descending
            };
            sortDesc.Click += (_, _) =>
            {
                currentSortColumnKey = key;
                currentSortDirection = ListSortDirection.Descending;
                ApplySortToCurrentView();
            };
            sortMenu.Items.Add(sortDesc);

            return sortMenu;
        }

        private void BestFitAllColumns()
        {
            if (FolderListView.View is not GridView gridView)
            {
                return;
            }

            foreach (GridViewColumn column in gridView.Columns)
            {
                if (column.Header is FolderVisualizerGridHeaderInfo info)
                {
                    BestFitColumn(column, info.Key, info.Text);
                }
                else if (column.Header is string)
                {
                    column.Width = 56;
                }
            }
        }

        private bool CanHideColumn(GridViewColumn column)
        {
            if (!tableColumnMap.TryGetValue(column, out FolderVisualizerTableColumnConfig? config))
            {
                return false;
            }

            FolderVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as FolderVisualizerViewPreset;
            if (selectedPreset == null)
            {
                return false;
            }

            int currentlyVisible = selectedPreset.Table.Columns.Count(entry => entry.IsVisible);
            return currentlyVisible > 1 && config.IsVisible;
        }

        private void HideColumn(GridViewColumn column)
        {
            if (!tableColumnMap.TryGetValue(column, out FolderVisualizerTableColumnConfig? config))
            {
                return;
            }

            config.IsVisible = false;
            PersistVisualizerConfig();
            ApplySelectedViewPreset();
        }

        private void BestFitColumn(GridViewColumn column, FolderVisualizerTableColumnKey key, string headerText)
        {
            if (!tableColumnMap.ContainsKey(column))
            {
                return;
            }

            double width = EstimateTextWidth(headerText) + 26;

            if (key == FolderVisualizerTableColumnKey.Icon)
            {
                width = 30;
            }
            else if (key == FolderVisualizerTableColumnKey.RowNumber)
            {
                width = 56;
            }
            else if (key == FolderVisualizerTableColumnKey.OpenCharIni)
            {
                width = 108;
            }
            else if (key == FolderVisualizerTableColumnKey.Readme)
            {
                width = 108;
            }
            else if (key == FolderVisualizerTableColumnKey.IntegrityFailures)
            {
                width = Math.Max(width, 320);
                foreach (FolderVisualizerItem item in allItems)
                {
                    width = Math.Max(width, EstimateTextWidth(item.IntegrityFailureMessages) + 20);
                }
            }
            else
            {
                foreach (FolderVisualizerItem item in allItems)
                {
                    string text = key switch
                    {
                        FolderVisualizerTableColumnKey.Name => item.Name,
                        FolderVisualizerTableColumnKey.Tags => item.TagsText,
                        FolderVisualizerTableColumnKey.IconType => item.IconTypeText,
                        FolderVisualizerTableColumnKey.RowNumber => item.IndexText,
                        FolderVisualizerTableColumnKey.DirectoryPath => item.DirectoryPath,
                        FolderVisualizerTableColumnKey.PreviewPath => item.PreviewPath,
                        FolderVisualizerTableColumnKey.LastModified => item.LastModifiedText,
                        FolderVisualizerTableColumnKey.EmoteCount => item.EmoteCountText,
                        FolderVisualizerTableColumnKey.Size => item.SizeText,
                        FolderVisualizerTableColumnKey.IntegrityFailures => item.IntegrityFailureMessages,
                        _ => item.Name
                    };

                    width = Math.Max(width, EstimateTextWidth(text) + 20);
                }
            }

            column.Width = Math.Clamp(width, 40, 1000);
        }

        private double EstimateTextWidth(string text)
        {
            string safe = text ?? string.Empty;
            return (safe.Length * Math.Max(7.0, FolderListView.FontSize * 0.58)) + 8;
        }

        private void ApplySortToCurrentView()
        {
            UpdateSortHeaderGlyphs();

            if (currentSortColumnKey == null)
            {
                return;
            }

            string sortProperty = GetSortProperty(currentSortColumnKey.Value);
            if (string.IsNullOrWhiteSpace(sortProperty))
            {
                return;
            }

            ICollectionView view = CollectionViewSource.GetDefaultView(FolderListView.ItemsSource);
            if (view == null)
            {
                return;
            }

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortProperty, currentSortDirection));
            view.Refresh();
            UpdateVisibleRowPositions();
        }

        private void UpdateSortHeaderGlyphs()
        {
            if (FolderListView.View is not GridView gridView)
            {
                return;
            }

            foreach (GridViewColumn column in gridView.Columns)
            {
                if (column.Header is not FolderVisualizerGridHeaderInfo info)
                {
                    continue;
                }

                column.Header = CreateHeaderInfo(info.Key);
            }
        }

        private static string GetSortProperty(FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.RowNumber => nameof(FolderVisualizerItem.Index),
                FolderVisualizerTableColumnKey.Name => nameof(FolderVisualizerItem.Name),
                FolderVisualizerTableColumnKey.Tags => nameof(FolderVisualizerItem.TagsText),
                FolderVisualizerTableColumnKey.IconType => nameof(FolderVisualizerItem.IconTypeText),
                FolderVisualizerTableColumnKey.DirectoryPath => nameof(FolderVisualizerItem.DirectoryPath),
                FolderVisualizerTableColumnKey.PreviewPath => nameof(FolderVisualizerItem.PreviewPath),
                FolderVisualizerTableColumnKey.LastModified => nameof(FolderVisualizerItem.LastModified),
                FolderVisualizerTableColumnKey.EmoteCount => nameof(FolderVisualizerItem.EmoteCount),
                FolderVisualizerTableColumnKey.Size => nameof(FolderVisualizerItem.SizeBytes),
                FolderVisualizerTableColumnKey.IntegrityFailures => nameof(FolderVisualizerItem.IntegrityFailureMessages),
                FolderVisualizerTableColumnKey.OpenCharIni => nameof(FolderVisualizerItem.HasCharIni),
                FolderVisualizerTableColumnKey.Readme => nameof(FolderVisualizerItem.HasReadme),
                _ => string.Empty
            };
        }

        private void TrackColumnWidth(GridViewColumn column, FolderVisualizerTableColumnConfig config)
        {
            DependencyPropertyDescriptor descriptor =
                DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
            EventHandler handler = (_, _) =>
            {
                if (column.Width > 0)
                {
                    config.Width = Math.Clamp(column.Width, 40, 1000);
                }
            };

            descriptor.AddValueChanged(column, handler);
            columnWidthHandlers[column] = handler;
        }

        private void UntrackTableColumnWidth()
        {
            DependencyPropertyDescriptor descriptor =
                DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));

            foreach (KeyValuePair<GridViewColumn, EventHandler> pair in columnWidthHandlers)
            {
                descriptor.RemoveValueChanged(pair.Key, pair.Value);
            }

            columnWidthHandlers.Clear();
            tableColumnMap.Clear();
        }

        private async void RefreshAssetsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirmationResult = OceanyaMessageBox.Show(
                "Are you sure you want to refresh your client assets? (This process may take a while)",
                "Refresh all Assets",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirmationResult != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await ClientAssetRefreshService.RefreshCharactersAndBackgroundsAsync(this);

                InvalidateCachedItems();

                await LoadCharacterItemsAsync(forceRebuild: true);
                onAssetsRefreshed?.Invoke();
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Character folder visualizer refresh failed.", ex);
                OceanyaMessageBox.Show(
                    this,
                    "An error occurred while refreshing client assets:\n" + ex.Message,
                    "Refresh Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ConfigureViewsButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterFolderVisualizerConfigWindow configureWindow =
                new CharacterFolderVisualizerConfigWindow(CloneConfig(visualizerConfig))
                {
                    Owner = this
                };

            bool? result = configureWindow.ShowDialog();
            if (result != true)
            {
                return;
            }

            visualizerConfig = configureWindow.ResultConfig;
            PersistVisualizerConfig();
            BindViewPresets(applySelectedView: false);
            await WaitForm.ShowFormAsync("Applying updated view configuration...", this);
            try
            {
                WaitForm.SetSubtitle("Rebuilding selected view...");
                await ApplySelectedViewPresetAsync(showWaitForm: false);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private void PersistVisualizerConfig()
        {
            SaveFile.Data.FolderVisualizer = CloneConfig(visualizerConfig);
            SaveFile.Save();
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

            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            pendingSearchText = SearchTextBox.Text ?? string.Empty;
            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            searchDebounceTimer.Stop();

            searchText = pendingSearchText;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                ICollectionView view = GetOrCreateItemsView();
                view.Refresh();
                UpdateSummaryText();
                UpdateViewportImageResidency();
                RequestProgressiveImageLoadReprioritization();
            }));
        }

        private void FolderListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedItemForTagging = FolderListView.SelectedItem as FolderVisualizerItem;
            RefreshSelectedFolderTagPanel();

            if (!preserveTagInputFocusOnSelection)
            {
                return;
            }

            preserveTagInputFocusOnSelection = false;
            if (tagPanelCollapsed)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                TextBox? editable = FindEditableTagInputTextBox();
                if (editable == null)
                {
                    TagInputComboBox.Focus();
                    return;
                }

                editable.Focus();
                editable.SelectionStart = editable.Text?.Length ?? 0;
                editable.SelectionLength = 0;
            }));
        }

        private void FolderListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            preserveTagInputFocusOnSelection = false;

            if (tagPanelCollapsed || TagPanelColumn.ActualWidth <= 0)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                return;
            }

            TextBox? editable = FindEditableTagInputTextBox();
            bool tagFieldHasFocus = TagInputComboBox.IsKeyboardFocusWithin || (editable?.IsKeyboardFocusWithin == true);
            if (!tagFieldHasFocus)
            {
                return;
            }

            preserveTagInputFocusOnSelection = true;
        }

        private void ToggleTagPanelButton_Click(object sender, RoutedEventArgs e)
        {
            bool currentlyCollapsed = tagPanelCollapsed;
            if (currentlyCollapsed)
            {
                double width = Math.Clamp(savedTagPanelWidth, 180, 520);
                TagPanelColumn.MinWidth = 180;
                TagPanelColumn.Width = new GridLength(width);
                TagPanelSplitterColumn.Width = new GridLength(5);
                TagPanelSplitter.Visibility = Visibility.Visible;
                tagPanelCollapsed = false;
            }
            else
            {
                savedTagPanelWidth = Math.Clamp(TagPanelColumn.ActualWidth, 180, 520);
                TagPanelColumn.MinWidth = 0;
                TagPanelColumn.Width = new GridLength(0);
                TagPanelSplitterColumn.Width = new GridLength(0);
                TagPanelSplitter.Visibility = Visibility.Collapsed;
                tagPanelCollapsed = true;
            }

            bool isCollapsed = tagPanelCollapsed;
            ToggleTagPanelButton.Content = isCollapsed ? "▶" : "◀";
            ToggleTagPanelButton.ToolTip = isCollapsed ? "Expand tag panel" : "Collapse tag panel";
            ToggleTagPanelTopButton.Content = isCollapsed ? "Show Tags" : "Hide Tags";
            PersistTagState(saveToDisk: true);
        }

        private void TagInputComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressTagInputAutocomplete)
            {
                return;
            }

            TextBox? editableTextBox = FindEditableTagInputTextBox();
            if (editableTextBox == null || !editableTextBox.IsKeyboardFocusWithin)
            {
                return;
            }

            string input = editableTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            string? match = allKnownTags.FirstOrDefault(tag =>
                tag.StartsWith(input, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match) || string.Equals(match, input, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            suppressTagInputAutocomplete = true;
            editableTextBox.Text = match;
            editableTextBox.SelectionStart = input.Length;
            editableTextBox.SelectionLength = match.Length - input.Length;
            suppressTagInputAutocomplete = false;
        }

        private void TagInputComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddTagFromInput();
            }
        }

        private async void SelectTagsButton_Click(object sender, RoutedEventArgs e)
        {
            List<string> selectableFilters = new List<string> { UntaggedFilterToken };
            selectableFilters.AddRange(allKnownTags);
            Dictionary<string, int> tagUsageCounts = BuildTagUsageCounts();

            TagFilterSelectionWindow selectionWindow = new TagFilterSelectionWindow(
                selectableFilters,
                activeIncludeTagFilters,
                activeExcludeTagFilters,
                activeFilterRoot,
                currentSortColumnKey,
                currentSortDirection,
                tagUsageCounts,
                tagFilterWindowState)
            {
                Owner = this
            };

            bool? result = selectionWindow.ShowDialog();
            VisualizerWindowState capturedState = selectionWindow.CaptureWindowState();
            tagFilterWindowState.Width = capturedState.Width;
            tagFilterWindowState.Height = capturedState.Height;
            tagFilterWindowState.IsMaximized = capturedState.IsMaximized;
            QueueTagStateSave();
            if (result != true)
            {
                return;
            }

            activeIncludeTagFilters.Clear();
            activeExcludeTagFilters.Clear();
            foreach (string tag in selectionWindow.IncludedTags)
            {
                activeIncludeTagFilters.Add(tag);
            }
            foreach (string tag in selectionWindow.ExcludedTags)
            {
                activeExcludeTagFilters.Add(tag);
            }
            activeFilterRoot = selectionWindow.FilterRoot.Clone();
            currentSortColumnKey = selectionWindow.SortColumn;
            currentSortDirection = selectionWindow.SortDirection;
            ApplySortToCurrentView();

            await RefreshItemsAfterTagFilterChangeAsync();
        }

        private Dictionary<string, int> BuildTagUsageCounts()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (FolderVisualizerItem item in allItems)
            {
                List<string> tags = GetTagsForItem(item).ToList();
                if (tags.Count == 0)
                {
                    counts[UntaggedFilterToken] = counts.TryGetValue(UntaggedFilterToken, out int existingUntagged)
                        ? existingUntagged + 1
                        : 1;
                    continue;
                }

                foreach (string tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    counts[tag] = counts.TryGetValue(tag, out int existing) ? existing + 1 : 1;
                }
            }

            return counts;
        }

        private async void SelectedFolderTagsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedFolderTagsListBox.SelectedItem is not string selectedTag)
            {
                return;
            }

            string normalizedTag = NormalizeTag(selectedTag);
            if (string.IsNullOrWhiteSpace(normalizedTag))
            {
                return;
            }

            activeIncludeTagFilters.Clear();
            activeExcludeTagFilters.Clear();
            activeIncludeTagFilters.Add(normalizedTag);

            await RefreshItemsAfterTagFilterChangeAsync();
        }

        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not string tag)
            {
                return;
            }

            RemoveTagFromSelectedItem(tag);
        }

        private void RenameTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFolderTagsListBox.SelectedItem is not string existingTag)
            {
                return;
            }

            string replacementRaw = TagInputComboBox.Text ?? string.Empty;
            string replacementTag = NormalizeTag(replacementRaw);
            if (string.IsNullOrWhiteSpace(replacementTag))
            {
                return;
            }

            if (string.Equals(existingTag, replacementTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (selectedItemForTagging == null)
            {
                return;
            }

            string primaryKey = GetPrimaryTagKeyForItem(selectedItemForTagging);
            if (string.IsNullOrWhiteSpace(primaryKey))
            {
                return;
            }

            bool removedExisting = false;
            foreach (string lookupKey in GetTagLookupKeysForItem(selectedItemForTagging))
            {
                if (!folderTagsByDirectory.TryGetValue(lookupKey, out HashSet<string>? lookupTags))
                {
                    continue;
                }

                if (lookupTags.Remove(existingTag))
                {
                    removedExisting = true;
                    if (lookupTags.Count == 0)
                    {
                        folderTagsByDirectory.Remove(lookupKey);
                    }
                }
            }

            if (!folderTagsByDirectory.TryGetValue(primaryKey, out HashSet<string>? tags))
            {
                tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                folderTagsByDirectory[primaryKey] = tags;
            }

            if (removedExisting || !tags.Contains(replacementTag))
            {
                tags.Add(replacementTag);
            }

            RefreshAfterTagMutation(refreshKnownTags: true);
            TagInputComboBox.Text = string.Empty;
        }

        private void RemoveSelectedTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFolderTagsListBox.SelectedItem is not string selectedTag)
            {
                return;
            }

            RemoveTagFromSelectedItem(selectedTag);
        }

        private void ClearFolderTagsButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItemForTagging == null)
            {
                return;
            }

            bool changed = false;
            foreach (string lookupKey in GetTagLookupKeysForItem(selectedItemForTagging).ToList())
            {
                changed |= folderTagsByDirectory.Remove(lookupKey);
            }

            if (changed)
            {
                RefreshAfterTagMutation(refreshKnownTags: true);
            }
        }

        private void AddTagFromInput()
        {
            if (selectedItemForTagging == null)
            {
                return;
            }

            string rawInput = TagInputComboBox.Text ?? string.Empty;
            string normalizedInput = NormalizeTag(rawInput);
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return;
            }

            string resolvedTag = allKnownTags.FirstOrDefault(tag =>
                string.Equals(tag, normalizedInput, StringComparison.OrdinalIgnoreCase))
                ?? normalizedInput;

            string key = GetPrimaryTagKeyForItem(selectedItemForTagging);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!folderTagsByDirectory.TryGetValue(key, out HashSet<string>? tags))
            {
                tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                folderTagsByDirectory[key] = tags;
            }

            if (tags.Add(resolvedTag))
            {
                RefreshAfterTagMutation(refreshKnownTags: true);
            }

            suppressTagInputAutocomplete = true;
            TagInputComboBox.Text = string.Empty;
            suppressTagInputAutocomplete = false;
            TagInputComboBox.IsDropDownOpen = false;
            TagInputComboBox.Focus();
        }

        private void RemoveTagFromSelectedItem(string tag)
        {
            if (selectedItemForTagging == null)
            {
                return;
            }

            bool removed = false;
            foreach (string lookupKey in GetTagLookupKeysForItem(selectedItemForTagging))
            {
                if (!folderTagsByDirectory.TryGetValue(lookupKey, out HashSet<string>? tags))
                {
                    continue;
                }

                if (!tags.Remove(tag))
                {
                    continue;
                }

                removed = true;
                if (tags.Count == 0)
                {
                    folderTagsByDirectory.Remove(lookupKey);
                }
            }

            if (!removed)
            {
                return;
            }

            RefreshAfterTagMutation(refreshKnownTags: true);
        }

        private void RefreshAfterTagMutation(bool refreshKnownTags)
        {
            if (refreshKnownTags)
            {
                RefreshKnownTagsCollection();
            }

            RefreshSelectedFolderTagPanel();
            RefreshItemTagTexts();
            QueueTagStateSave();

            bool shouldRefreshView = activeIncludeTagFilters.Count > 0
                || activeExcludeTagFilters.Count > 0
                || !string.IsNullOrWhiteSpace(searchText)
                || !string.IsNullOrWhiteSpace(pendingSearchText);
            if (!shouldRefreshView)
            {
                return;
            }

            ICollectionView view = GetOrCreateItemsView();
            view.Refresh();
            UpdateSummaryText();
            UpdateViewportImageResidency();
            RequestProgressiveImageLoadReprioritization();
        }

        private async Task RefreshItemsAfterTagFilterChangeAsync()
        {
            UpdateActiveTagFiltersText();
            QueueTagStateSave();
            await WaitForm.ShowFormAsync("Applying filters & sorting...", this);
            try
            {
                WaitForm.SetSubtitle("Rebuilding filter and sort results...");
                await Dispatcher.Yield(DispatcherPriority.Background);
                ICollectionView view = GetOrCreateItemsView();
                view.Refresh();
                UpdateSummaryText();
                await Dispatcher.Yield(DispatcherPriority.Background);
                UpdateViewportImageResidency();
                RequestProgressiveImageLoadReprioritization();
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private void RefreshSelectedFolderTagPanel()
        {
            selectedFolderTags.Clear();
            if (selectedItemForTagging == null)
            {
                SelectedFolderLabel.Text = "No folder selected";
                RenameTagButton.IsEnabled = false;
                RemoveSelectedTagButton.IsEnabled = false;
                ClearFolderTagsButton.IsEnabled = false;
                return;
            }

            SelectedFolderLabel.Text = selectedItemForTagging.Name;
            IEnumerable<string> lookupKeys = GetTagLookupKeysForItem(selectedItemForTagging).ToList();
            HashSet<string> combinedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string lookupKey in lookupKeys)
            {
                if (!folderTagsByDirectory.TryGetValue(lookupKey, out HashSet<string>? existingTags))
                {
                    continue;
                }

                foreach (string tag in existingTags)
                {
                    combinedTags.Add(tag);
                }
            }

            if (combinedTags.Count == 0)
            {
                RenameTagButton.IsEnabled = false;
                RemoveSelectedTagButton.IsEnabled = false;
                ClearFolderTagsButton.IsEnabled = false;
                return;
            }

            foreach (string tag in combinedTags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))
            {
                selectedFolderTags.Add(tag);
            }

            bool hasTags = selectedFolderTags.Count > 0;
            RenameTagButton.IsEnabled = hasTags;
            RemoveSelectedTagButton.IsEnabled = hasTags;
            ClearFolderTagsButton.IsEnabled = hasTags;
        }

        private void RefreshKnownTagsCollection()
        {
            allKnownTags.Clear();
            IEnumerable<string> orderedTags = folderTagsByDirectory.Values
                .SelectMany(value => value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

            foreach (string tag in orderedTags)
            {
                allKnownTags.Add(tag);
            }
        }

        private void PruneTagAssignmentsToExistingItems()
        {
            RefreshKnownTagsCollection();
        }

        private void UpdateActiveTagFiltersText()
        {
            if (activeIncludeTagFilters.Count == 0 && activeExcludeTagFilters.Count == 0)
            {
                ActiveTagFiltersText.Text = "Active tags: none";
                return;
            }
            string includeText = activeIncludeTagFilters.Count == 0
                ? "none"
                : string.Join(", ", activeIncludeTagFilters.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            string excludeText = activeExcludeTagFilters.Count == 0
                ? "none"
                : string.Join(", ", activeExcludeTagFilters.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            int advancedCount = activeFilterRoot.CountActiveConditions();
            ActiveTagFiltersText.Text = $"Include: {includeText} | Exclude: {excludeText} | Rules: {advancedCount}";
        }

        private void UpdateSummaryText()
        {
            int total = allItems.Count;
            int visible = FolderListView.Items.Count;
            UpdateVisibleRowPositions();
            SummaryText.Text = $"Characters indexed: {total} | Showing: {visible}";
        }

        private TextBox? FindEditableTagInputTextBox()
        {
            return TagInputComboBox.Template?.FindName("PART_EditableTextBox", TagInputComboBox) as TextBox;
        }

        private async void ViewIntegrityVerifierResultsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ShowIntegrityVerifierResults = ViewIntegrityVerifierResultsCheckBox.IsChecked == true;
            SaveFile.Data.ViewFolderIntegrityVerifierResults = ShowIntegrityVerifierResults;
            SaveFile.Save();

            if (FolderListView.View != null)
            {
                await WaitForm.ShowFormAsync("Applying integrity verifier view...", this);
                try
                {
                    WaitForm.SetSubtitle("Refreshing columns and row styling...");
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    ApplySelectedViewPreset();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    RefreshVisibleItems();
                }
                finally
                {
                    WaitForm.CloseForm();
                }

                return;
            }

            FolderListView.InvalidateVisual();
        }

        private void RefreshVisibleItems()
        {
            ICollectionView view = GetOrCreateItemsView();
            view.Refresh();
            FolderListView.Items.Refresh();
            UpdateSummaryText();
            UpdateViewportImageResidency();
            RequestProgressiveImageLoadReprioritization();
        }

        private void FolderListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            contextMenuTargetItem = ResolveItemFromOriginalSource(e.OriginalSource as DependencyObject);
            if (contextMenuTargetItem != null)
            {
                FolderListView.SelectedItem = contextMenuTargetItem;
                FolderListView.ContextMenu = BuildContextMenuForItem(contextMenuTargetItem);
                FolderListView.ContextMenu.PlacementTarget = FolderListView;
                FolderListView.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void FolderListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            DependencyObject? originalSource = e.OriginalSource as DependencyObject ?? Mouse.DirectlyOver as DependencyObject;
            FolderVisualizerItem? target = contextMenuTargetItem
                ?? ResolveItemFromOriginalSource(originalSource)
                ?? FolderListView.SelectedItem as FolderVisualizerItem;
            if (target == null)
            {
                e.Handled = true;
                return;
            }

            FolderListView.ContextMenu = BuildContextMenuForItem(target);
        }

        private void FolderListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsWithinControl<Button>(e.OriginalSource as DependencyObject))
            {
                return;
            }

            FolderVisualizerItem? item = ResolveItemFromOriginalSource(e.OriginalSource as DependencyObject);
            if (item == null)
            {
                return;
            }

            CharacterFolder? character = ResolveCharacterFolderForItem(item);
            if (character == null)
            {
                return;
            }

            CharacterEmoteVisualizerWindow emoteVisualizerWindow = new CharacterEmoteVisualizerWindow(character)
            {
                Owner = this
            };
            emoteVisualizerWindow.ShowDialog();
        }

        private void FolderListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (FolderListView.View != null)
            {
                RequestProgressiveImageLoadReprioritization();
                return;
            }

            ScrollViewer? scrollViewer = FindDescendant<ScrollViewer>(FolderListView);
            if (scrollViewer == null)
            {
                return;
            }

            double delta = e.Delta > 0 ? -normalScrollWheelStep : normalScrollWheelStep;
            double targetOffset = Math.Clamp(scrollViewer.VerticalOffset + delta, 0, scrollViewer.ScrollableHeight);
            scrollViewer.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
            RequestProgressiveImageLoadReprioritization();
        }

        private static bool IsWithinControl<TControl>(DependencyObject? source) where TControl : DependencyObject
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is TControl)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static TChild? FindDescendant<TChild>(DependencyObject? root)
            where TChild : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is TChild typedChild)
                {
                    return typedChild;
                }

                TChild? nested = FindDescendant<TChild>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private sealed class RowResizeGuideAdorner : Adorner
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

        private static CharacterFolder? ResolveCharacterFolderForItem(FolderVisualizerItem item)
        {
            string directoryPath = item.DirectoryPath?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                CharacterFolder? byDirectory = CharacterFolder.FullList.FirstOrDefault(character =>
                    string.Equals(character.DirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase));
                if (byDirectory != null)
                {
                    return byDirectory;
                }
            }

            string characterName = item.Name?.Trim() ?? string.Empty;
            return CharacterFolder.FullList.FirstOrDefault(character =>
                string.Equals(character.Name, characterName, StringComparison.OrdinalIgnoreCase));
        }

        private FolderVisualizerItem? ResolveItemFromOriginalSource(DependencyObject? source)
        {
            if (source != null)
            {
                ListViewItem? container = ItemsControl.ContainerFromElement(FolderListView, source) as ListViewItem;
                if (container?.DataContext is FolderVisualizerItem directItem)
                {
                    return directItem;
                }
            }

            DependencyObject? current = source;
            while (current != null)
            {
                if (current is FrameworkElement element && element.DataContext is FolderVisualizerItem item)
                {
                    return item;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private ContextMenu BuildContextMenuForItem(FolderVisualizerItem item)
        {
            ContextMenu menu = new ContextMenu();

            AddContextCategoryHeader(menu, "Oceanya Client", addLeadingSeparator: false);

            MenuItem setCharacterMenuItem = new MenuItem
            {
                Header = "Set character in client",
                IsEnabled = canSetCharacterInClient?.Invoke(item) == true
            };
            setCharacterMenuItem.Click += (_, _) => setCharacterInClient?.Invoke(item);
            menu.Items.Add(setCharacterMenuItem);

            MenuItem editCharacterFolderMenuItem = new MenuItem
            {
                Header = "Edit character folder",
                IsEnabled = !string.IsNullOrWhiteSpace(item.DirectoryPath)
                    && Directory.Exists(item.DirectoryPath)
                    && File.Exists(item.CharIniPath)
            };
            editCharacterFolderMenuItem.Click += async (_, _) => await OpenCharacterFolderInCreatorAsync(item);
            menu.Items.Add(editCharacterFolderMenuItem);

            AddContextCategoryHeader(menu, "Character View", addLeadingSeparator: true);

            MenuItem openCharIniMenuItem = new MenuItem
            {
                Header = "Open Char.ini",
                IsEnabled = !string.IsNullOrWhiteSpace(item.CharIniPath) && File.Exists(item.CharIniPath)
            };
            openCharIniMenuItem.Click += (_, _) => TryOpenPath(item.CharIniPath);
            menu.Items.Add(openCharIniMenuItem);

            MenuItem openReadmeMenuItem = new MenuItem
            {
                Header = "Open Readme",
                IsEnabled = item.HasReadme && !string.IsNullOrWhiteSpace(item.ReadmePath) && File.Exists(item.ReadmePath)
            };
            openReadmeMenuItem.Click += (_, _) => TryOpenPath(item.ReadmePath);
            menu.Items.Add(openReadmeMenuItem);

            MenuItem showInExplorerMenuItem = new MenuItem
            {
                Header = "Show in explorer",
                IsEnabled = !string.IsNullOrWhiteSpace(item.DirectoryPath) && Directory.Exists(item.DirectoryPath)
            };
            showInExplorerMenuItem.Click += (_, _) => ShowInExplorer(item.DirectoryPath);
            menu.Items.Add(showInExplorerMenuItem);

            AddContextCategoryHeader(menu, "Integrity verifier", addLeadingSeparator: true);

            MenuItem runVerifierMenuItem = new MenuItem
            {
                Header = "Run Verifier",
                IsEnabled = !string.IsNullOrWhiteSpace(item.DirectoryPath) && Directory.Exists(item.DirectoryPath)
            };
            runVerifierMenuItem.Click += async (_, _) => await RunIntegrityVerifierForItemAsync(item, openResultsAfterRun: false);
            menu.Items.Add(runVerifierMenuItem);

            MenuItem viewVerifierResultsMenuItem = new MenuItem
            {
                Header = "View Results",
                IsEnabled = !string.IsNullOrWhiteSpace(item.DirectoryPath) && Directory.Exists(item.DirectoryPath)
            };
            viewVerifierResultsMenuItem.Click += async (_, _) => await OpenIntegrityVerifierResultsAsync(item);
            menu.Items.Add(viewVerifierResultsMenuItem);

            AddContextCategoryHeader(menu, "Attorney Online", addLeadingSeparator: true);

            MenuItem deleteCharacterFolderMenuItem = new MenuItem
            {
                Header = "Delete character folder",
                IsEnabled = !string.IsNullOrWhiteSpace(item.DirectoryPath) && Directory.Exists(item.DirectoryPath)
            };
            deleteCharacterFolderMenuItem.Click += async (_, _) => await DeleteCharacterFolderAsync(item);
            menu.Items.Add(deleteCharacterFolderMenuItem);

            return menu;
        }

        private async Task OpenCharacterFolderInCreatorAsync(FolderVisualizerItem item)
        {
            if (item == null)
            {
                return;
            }

            string directoryPath = item.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                OceanyaMessageBox.Show(
                    this,
                    "Character folder was not found on disk.",
                    "Edit Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await WaitForm.ShowFormAsync("Opening character editor...", this);
            AOCharacterFileCreatorWindow creator = new AOCharacterFileCreatorWindow();
            bool loadedSuccessfully;
            string errorMessage;
            try
            {
                WaitForm.SetSubtitle("Loading character folder: " + item.Name);
                await Dispatcher.Yield(DispatcherPriority.Background);
                loadedSuccessfully = creator.TryLoadCharacterFolderForEditing(directoryPath, out errorMessage);
            }
            finally
            {
                WaitForm.CloseForm();
            }

            if (!loadedSuccessfully)
            {
                OceanyaMessageBox.Show(
                    this,
                    "Could not open the selected character in the AO Character File Creator.\n"
                    + (string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error." : errorMessage),
                    "Edit Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Window editorWindow = OceanyaWindowManager.CreateWindow(creator);
            editorWindow.Owner = this;
            _ = editorWindow.ShowDialog();

            if (creator.EditApplyCompleted)
            {
                await LoadCharacterItemsAsync(forceRebuild: true);
                onAssetsRefreshed?.Invoke();
            }
        }

        private void AddContextCategoryHeader(ContextMenu menu, string text, bool addLeadingSeparator)
        {
            if (addLeadingSeparator && menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            TextBlock label = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(4, 2, 4, 1)
            };

            MenuItem header = new MenuItem
            {
                Header = label,
                IsEnabled = false,
                StaysOpenOnClick = true
            };

            menu.Items.Add(header);
        }

        private async Task RunIntegrityVerifierForItemAsync(FolderVisualizerItem item, bool openResultsAfterRun)
        {
            if (item == null)
            {
                return;
            }

            string directoryPath = item.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            await WaitForm.ShowFormAsync("Running integrity verifier...", this);
            CharacterIntegrityReport report;
            try
            {
                WaitForm.SetSubtitle("Verifying folder: " + item.Name);
                report = await Task.Run(() =>
                    CharacterIntegrityVerifier.RunAndPersist(directoryPath, item.CharIniPath, item.Name));
            }
            finally
            {
                WaitForm.CloseForm();
            }

            ApplyIntegrityReportToItem(item, report);
            RefreshVisibleItems();

            if (openResultsAfterRun)
            {
                OpenIntegrityResultsWindow(item, report);
            }
        }

        private async Task OpenIntegrityVerifierResultsAsync(FolderVisualizerItem item)
        {
            if (item == null)
            {
                return;
            }

            CharacterIntegrityReport? report;
            if (!CharacterIntegrityVerifier.TryLoadPersistedReport(item.DirectoryPath, out report) || report == null)
            {
                await RunIntegrityVerifierForItemAsync(item, openResultsAfterRun: true);
                return;
            }

            OpenIntegrityResultsWindow(item, report);
        }

        private void OpenIntegrityResultsWindow(FolderVisualizerItem item, CharacterIntegrityReport report)
        {
            CharacterIntegrityVerifierResultsWindow resultsWindow = new CharacterIntegrityVerifierResultsWindow(
                report,
                item.DirectoryPath,
                item.Name,
                updatedReport =>
                {
                    ApplyIntegrityReportToItem(item, updatedReport);
                    RefreshVisibleItems();
                })
            {
                Owner = this
            };
            resultsWindow.ShowDialog();
        }

        private static void ApplyIntegrityReportToItem(FolderVisualizerItem item, CharacterIntegrityReport report)
        {
            item.IntegrityHasFailures = report.HasFailures;
            item.IntegrityFailureCount = report.FailureCount;
            item.IntegrityFailureMessages = BuildIntegrityFailureMessages(report);
        }

        private static string BuildIntegrityFailureMessages(CharacterIntegrityReport? report)
        {
            if (report == null)
            {
                return string.Empty;
            }

            List<string> messages = report.Results
                .Where(result => !result.Passed && !string.IsNullOrWhiteSpace(result.Message))
                .Select(result => result.Message.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return string.Join(" | ", messages);
        }

        internal async Task ApplyPreviewOverrideForCharacterDirectoryAsync(string? directoryPath)
        {
            string key = NormalizeFolderOverrideKey(directoryPath);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            CharacterFolder? characterFolder = CharacterFolder.FullList.FirstOrDefault(folder =>
                string.Equals(NormalizeFolderOverrideKey(folder.DirectoryPath), key, StringComparison.OrdinalIgnoreCase));
            if (characterFolder == null)
            {
                return;
            }

            string previewPath = ResolvePreferredPreviewPath(characterFolder);
            ImageSource previewImage = await Task.Run(() => LoadImage(previewPath, 220));

            FolderVisualizerItem? currentItem = allItems.FirstOrDefault(item =>
                string.Equals(NormalizeFolderOverrideKey(item.DirectoryPath), key, StringComparison.OrdinalIgnoreCase));
            if (currentItem != null)
            {
                currentItem.PreviewPath = previewPath;
                currentItem.PreviewImage = previewImage;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            progressiveImageLoadCancellation?.Cancel();
            progressiveLoadReprioritizeTimer.Stop();
            if (folderListScrollViewer != null)
            {
                folderListScrollViewer.ScrollChanged -= FolderListScrollViewer_ScrollChanged;
                folderListScrollViewer = null;
            }
            tagSaveDebounceTimer.Stop();
            UntrackTableColumnWidth();
            PersistVisualizerConfig();
            PersistTagState(saveToDisk: true);
            if (!applyingSavedWindowState)
            {
                SaveFile.Data.FolderVisualizerWindowState = CaptureWindowState();
                SaveFile.Save();
            }
        }

        private void OpenCharIniButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not FolderVisualizerItem item)
            {
                return;
            }

            string charIniPath = item.CharIniPath?.Trim() ?? string.Empty;
            if (!TryOpenPath(charIniPath))
            {
                OceanyaMessageBox.Show(this,
                    "char.ini was not found for this character.",
                    "Open char.ini",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OpenReadmeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not FolderVisualizerItem item)
            {
                return;
            }

            string readmePath = item.ReadmePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(readmePath) || !File.Exists(readmePath))
            {
                return;
            }

            TryOpenPath(readmePath);
        }

        private bool TryOpenPath(string path)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to open path '{path}'.", ex);
                return false;
            }
        }

        private void ShowInExplorer(string directoryPath)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directoryPath}\"",
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to open explorer at '{directoryPath}'.", ex);
            }
        }

        private async Task DeleteCharacterFolderAsync(FolderVisualizerItem item)
        {
            string targetPath = item.DirectoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
            {
                OceanyaMessageBox.Show(this,
                    "Character folder was not found on disk.",
                    "Delete Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmationResult = OceanyaMessageBox.Show(
                this,
                "You are about to permanently delete this character folder:\n\n"
                + item.Name + "\n" + targetPath
                + "\n\nThis cannot be undone. Do you want to continue?",
                "Delete Character Folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmationResult != MessageBoxResult.Yes)
            {
                return;
            }

            await WaitForm.ShowFormAsync("Deleting character folder...", this);
            try
            {
                WaitForm.SetSubtitle("Deleting folder: " + item.Name);
                await Task.Run(() => Directory.Delete(targetPath, true));

                WaitForm.SetSubtitle("Refreshing character index...");
                await Task.Run(() =>
                    CharacterFolder.RefreshCharacterList(
                        onParsedCharacter: character => WaitForm.SetSubtitle("Parsed Folder: " + character.Name),
                        onChangedMountPath: path => WaitForm.SetSubtitle("Changed mount path: " + path)));

                InvalidateCachedItems();
                itemsView = null;

                await LoadCharacterItemsAsync(forceRebuild: true);
                onAssetsRefreshed?.Invoke();
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to delete character folder.", ex);
                OceanyaMessageBox.Show(
                    this,
                    "An error occurred while deleting the character folder:\n" + ex.Message,
                    "Delete Character Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        internal static string ResolveFirstCharacterIdleSpritePath(CharacterFolder folder)
        {
            return CharacterAssetPathResolver.ResolveFirstCharacterIdleSpritePath(folder);
        }

        internal static void InvalidateCachedItems()
        {
            EnsureDiskCacheFilePath();
            try
            {
                if (File.Exists(diskCacheFilePath))
                {
                    File.Delete(diskCacheFilePath);
                }
            }
            catch
            {
                // ignored
            }
        }

        internal static string NormalizeFolderOverrideKey(string? directoryPath)
        {
            string value = directoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static bool TryGetFolderPreviewOverrideEmoteId(string? directoryPath, out int emoteId)
        {
            string key = NormalizeFolderOverrideKey(directoryPath);
            if (!string.IsNullOrWhiteSpace(key)
                && SaveFile.Data.CharacterFolderPreviewEmoteOverrides.TryGetValue(key, out int storedId)
                && storedId > 0)
            {
                emoteId = storedId;
                return true;
            }

            emoteId = 0;
            return false;
        }

        private static string ResolvePreferredPreviewPath(CharacterFolder folder)
        {
            string directoryPath = folder.DirectoryPath ?? string.Empty;
            if (TryGetFolderPreviewOverrideEmoteId(directoryPath, out int overrideEmoteId))
            {
                Emote? overrideEmote = null;
                if (folder.configINI?.Emotions != null)
                {
                    folder.configINI.Emotions.TryGetValue(overrideEmoteId, out overrideEmote);
                }

                if (overrideEmote != null)
                {
                    string overridePath = CharacterAssetPathResolver.ResolveIdleSpritePath(
                        directoryPath,
                        overrideEmote.Animation);
                    if (!string.IsNullOrWhiteSpace(overridePath))
                    {
                        return overridePath;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(folder.ViewportIdleSpritePath))
            {
                return folder.ViewportIdleSpritePath;
            }

            return ResolveFirstCharacterIdleSpritePath(folder);
        }

        private static void EnsureDiskCacheFilePath()
        {
            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OceanyaClient",
                "cache");
            Directory.CreateDirectory(cacheRoot);
            string cacheKeyPayload = $"{Globals.PathToConfigINI}|{string.Join("|", Globals.BaseFolders)}";
            string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKeyPayload))).ToLowerInvariant();
            string desiredPath = Path.Combine(cacheRoot, $"folder_visualizer_{cacheKey}.json");
            if (!diskCachePathInitialized || !string.Equals(diskCacheFilePath, desiredPath, StringComparison.OrdinalIgnoreCase))
            {
                diskCacheFilePath = desiredPath;
                diskCachePathInitialized = true;
            }
        }

        private bool TryLoadProjectedItemsFromDisk(string signature, out List<FolderVisualizerItem> projectedItems)
        {
            projectedItems = new List<FolderVisualizerItem>();
            EnsureDiskCacheFilePath();

            if (!File.Exists(diskCacheFilePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(diskCacheFilePath);
                FolderVisualizerDiskCacheContainer? container =
                    JsonSerializer.Deserialize<FolderVisualizerDiskCacheContainer>(json, CacheJsonOptions);
                if (container == null
                    || container.Version != VisualizerDiskCacheVersion
                    || !string.Equals(container.Signature, signature, StringComparison.Ordinal))
                {
                    return false;
                }

                if (container.Items == null || container.Items.Count == 0)
                {
                    return false;
                }

                projectedItems = container.Items.Select(item => new FolderVisualizerItem
                {
                    Name = item.Name ?? string.Empty,
                    DirectoryPath = item.DirectoryPath ?? string.Empty,
                    IconPath = item.IconPath ?? string.Empty,
                    PreviewPath = item.PreviewPath ?? string.Empty,
                    CharIniPath = item.CharIniPath ?? string.Empty,
                    HasCharIni = item.HasCharIni,
                    LastModified = item.LastModifiedUtc == default ? DateTime.MinValue : item.LastModifiedUtc.ToLocalTime(),
                    LastModifiedText = item.LastModifiedText ?? string.Empty,
                    EmoteCount = item.EmoteCount,
                    EmoteCountText = item.EmoteCountText ?? string.Empty,
                    SizeBytes = item.SizeBytes,
                    SizeText = item.SizeText ?? string.Empty,
                    ReadmePath = item.ReadmePath ?? string.Empty,
                    HasReadme = item.HasReadme,
                    IntegrityHasFailures = item.IntegrityHasFailures,
                    IntegrityFailureCount = item.IntegrityFailureCount,
                    IntegrityFailureMessages = item.IntegrityFailureMessages ?? string.Empty,
                    IconImage = FallbackFolderImage,
                    PreviewImage = FallbackFolderImage
                }).ToList();

                return projectedItems.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SaveProjectedItemsToDisk(string signature, IReadOnlyList<FolderVisualizerItem> items)
        {
            EnsureDiskCacheFilePath();
            try
            {
                FolderVisualizerDiskCacheContainer container = new FolderVisualizerDiskCacheContainer
                {
                    Version = VisualizerDiskCacheVersion,
                    Signature = signature,
                    Items = items.Select(item => new FolderVisualizerDiskCacheItem
                    {
                        Name = item.Name,
                        DirectoryPath = item.DirectoryPath,
                        IconPath = item.IconPath,
                        PreviewPath = item.PreviewPath,
                        CharIniPath = item.CharIniPath,
                        HasCharIni = item.HasCharIni,
                        LastModifiedUtc = item.LastModified == DateTime.MinValue
                            ? DateTime.MinValue
                            : item.LastModified.ToUniversalTime(),
                        LastModifiedText = item.LastModifiedText,
                        EmoteCount = item.EmoteCount,
                        EmoteCountText = item.EmoteCountText,
                        SizeBytes = item.SizeBytes,
                        SizeText = item.SizeText,
                        ReadmePath = item.ReadmePath,
                        HasReadme = item.HasReadme,
                        IntegrityHasFailures = item.IntegrityHasFailures,
                        IntegrityFailureCount = item.IntegrityFailureCount,
                        IntegrityFailureMessages = item.IntegrityFailureMessages
                    }).ToList()
                };

                string json = JsonSerializer.Serialize(container, CacheJsonOptions);
                File.WriteAllText(diskCacheFilePath, json);
            }
            catch
            {
                // ignored
            }
        }

        private static long GetDirectorySizeSafe(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return 0;
            }

            try
            {
                return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Sum(file =>
                    {
                        try
                        {
                            return new FileInfo(file).Length;
                        }
                        catch
                        {
                            return 0;
                        }
                    });
            }
            catch
            {
                return 0;
            }
        }

        private static string ResolveCharacterReadmePath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return string.Empty;
            }

            try
            {
                IEnumerable<string> textFiles = Directory.EnumerateFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly);
                foreach (string textFile in textFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsReadmeCandidate(textFile))
                    {
                        continue;
                    }

                    return textFile;
                }
            }
            catch
            {
                // ignored
            }

            return string.Empty;
        }

        private static bool IsReadmeCandidate(string textFile)
        {
            string fileName = Path.GetFileName(textFile).ToLowerInvariant();
            string[] excludedNames =
            {
                "char.txt",
                "design.txt",
                "soundlist.txt"
            };

            if (excludedNames.Contains(fileName))
            {
                return false;
            }

            try
            {
                string[] lines = File.ReadLines(textFile).Take(40).ToArray();
                int configLikeLines = lines.Count(line =>
                {
                    string trimmed = line.Trim();
                    return trimmed.StartsWith("[", StringComparison.Ordinal)
                        || trimmed.Contains('=');
                });

                // Heuristic: mostly key/value or section-like files are treated as config-like, not readme.
                return configLikeLines < Math.Max(4, lines.Length / 2);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            double value = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int unitIndex = -1;

            do
            {
                value /= 1024d;
                unitIndex++;
            } while (value >= 1024d && unitIndex < units.Length - 1);

            return $"{value:0.##} {units[unitIndex]}";
        }

        private ImageSource LoadImage(string path, int decodePixelWidth)
        {
            string normalizedPath = path?.Trim() ?? string.Empty;
            Stopwatch stopwatch = Stopwatch.StartNew();
            ImageSource image = Ao2AnimationPreview.LoadStaticPreviewImage(
                normalizedPath,
                decodePixelWidth,
                FallbackFolderImage);
            if (stopwatch.ElapsedMilliseconds > 120)
            {
                string extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
                LogPreviewDebug($"Slow image load: {stopwatch.ElapsedMilliseconds}ms ext={extension} path='{normalizedPath}'");
            }

            return image;
        }

        private void StartProgressiveImageLoading()
        {
            progressiveImageLoadCancellation?.Cancel();
            progressiveImageLoadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = progressiveImageLoadCancellation.Token;
            UpdateViewportImageResidency();
            List<FolderVisualizerItem> snapshot = BuildPrioritizedImageLoadSnapshot();
            if (snapshot.Count == 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    FolderVisualizerItem item = snapshot[i];
                    Stopwatch itemStopwatch = Stopwatch.StartNew();
                    ImageSource iconImage = LoadImage(item.IconPath, 48);
                    ImageSource previewImage = LoadImage(item.PreviewPath, 220);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        string itemKey = BuildProgressiveLoadItemKey(item);
                        lock (progressiveLoadKeyLock)
                        {
                            if (progressiveLoadedItemKeys.Contains(itemKey))
                            {
                                return;
                            }

                            progressiveLoadedItemKeys.Add(itemKey);
                        }

                        item.IconImage = iconImage;
                        item.PreviewImage = previewImage;
                    }, DispatcherPriority.Background, cancellationToken);
                    LogPreviewDebug($"Loaded item '{item.Name}' in {itemStopwatch.ElapsedMilliseconds}ms");

                    if ((i + 1) % 24 == 0)
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
            }, cancellationToken);
        }

        private void EnsureFolderListScrollViewerHooked()
        {
            ScrollViewer? discovered = FindDescendant<ScrollViewer>(FolderListView);
            if (ReferenceEquals(discovered, folderListScrollViewer))
            {
                return;
            }

            if (folderListScrollViewer != null)
            {
                folderListScrollViewer.ScrollChanged -= FolderListScrollViewer_ScrollChanged;
            }

            folderListScrollViewer = discovered;
            if (folderListScrollViewer != null)
            {
                folderListScrollViewer.ScrollChanged += FolderListScrollViewer_ScrollChanged;
            }
        }

        private void FolderListScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) < 0.01 && Math.Abs(e.HorizontalChange) < 0.01)
            {
                return;
            }

            UpdateViewportImageResidency();
            RequestProgressiveImageLoadReprioritization();
        }

        private void RequestProgressiveImageLoadReprioritization()
        {
            if (!hasLoaded || allItems.Count == 0)
            {
                return;
            }

            progressiveLoadReprioritizeTimer.Stop();
            progressiveLoadReprioritizeTimer.Start();
        }

        private void ProgressiveLoadReprioritizeTimer_Tick(object? sender, EventArgs e)
        {
            progressiveLoadReprioritizeTimer.Stop();
            StartProgressiveImageLoading();
        }

        private List<FolderVisualizerItem> BuildPrioritizedImageLoadSnapshot()
        {
            EnsureFolderListScrollViewerHooked();

            List<FolderVisualizerItem> orderedItems = new List<FolderVisualizerItem>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FolderVisualizerItem retainedItem in GetViewportRetainedItems())
            {
                TryAddItemForProgressiveLoad(retainedItem, orderedItems, seenKeys);
            }

            return orderedItems;
        }

        private void UpdateViewportImageResidency()
        {
            IReadOnlyList<FolderVisualizerItem> retainedItems = GetViewportRetainedItems();
            HashSet<string> retainedKeys = retainedItems
                .Select(BuildProgressiveLoadItemKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (FolderVisualizerItem item in allItems)
            {
                string key = BuildProgressiveLoadItemKey(item);
                if (retainedKeys.Contains(key))
                {
                    continue;
                }

                item.IconImage = FallbackFolderImage;
                item.PreviewImage = FallbackFolderImage;
                lock (progressiveLoadKeyLock)
                {
                    progressiveLoadedItemKeys.Remove(key);
                }
            }
        }

        private static void LogPreviewDebug(string message)
        {
            if (!EnablePreviewDebugLog)
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(PreviewDebugLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {message}";
                File.AppendAllText(PreviewDebugLogPath, line + Environment.NewLine);
            }
            catch
            {
                // ignored
            }
        }

        private static void InitializePreviewDebugLog()
        {
            if (!EnablePreviewDebugLog)
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(PreviewDebugLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                FileInfo info = new FileInfo(PreviewDebugLogPath);
                if (info.Exists && info.Length > 1_500_000)
                {
                    info.Delete();
                }

                File.AppendAllText(
                    PreviewDebugLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] --- session start ---{Environment.NewLine}");
            }
            catch
            {
                // ignored
            }
        }

        private IReadOnlyList<FolderVisualizerItem> GetViewportRetainedItems()
        {
            IReadOnlyList<FolderVisualizerItem> currentItems = GetCurrentViewItemsInOrder();
            if (currentItems.Count == 0)
            {
                return Array.Empty<FolderVisualizerItem>();
            }

            List<FolderVisualizerItem> visibleItems = GetVisibleItemsOrderedTopToBottom();
            if (visibleItems.Count == 0)
            {
                return currentItems.Take(Math.Min(currentItems.Count, ViewportRetentionRows * 2)).ToList();
            }

            Dictionary<string, int> indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < currentItems.Count; i++)
            {
                string key = BuildProgressiveLoadItemKey(currentItems[i]);
                if (!indexByKey.ContainsKey(key))
                {
                    indexByKey[key] = i;
                }
            }

            int minIndex = currentItems.Count - 1;
            int maxIndex = 0;
            bool foundAny = false;
            foreach (FolderVisualizerItem visibleItem in visibleItems)
            {
                if (!indexByKey.TryGetValue(BuildProgressiveLoadItemKey(visibleItem), out int visibleIndex))
                {
                    continue;
                }

                foundAny = true;
                if (visibleIndex < minIndex)
                {
                    minIndex = visibleIndex;
                }

                if (visibleIndex > maxIndex)
                {
                    maxIndex = visibleIndex;
                }
            }

            if (!foundAny)
            {
                return currentItems.Take(Math.Min(currentItems.Count, ViewportRetentionRows * 2)).ToList();
            }

            int startIndex = Math.Max(0, minIndex - ViewportRetentionRows);
            int endIndex = Math.Min(currentItems.Count - 1, maxIndex + ViewportRetentionRows);
            List<FolderVisualizerItem> retained = new List<FolderVisualizerItem>(endIndex - startIndex + 1);
            for (int i = startIndex; i <= endIndex; i++)
            {
                retained.Add(currentItems[i]);
            }

            return retained;
        }

        private IReadOnlyList<FolderVisualizerItem> GetCurrentViewItemsInOrder()
        {
            ICollectionView view = GetOrCreateItemsView();
            List<FolderVisualizerItem> ordered = new List<FolderVisualizerItem>();
            foreach (object item in view)
            {
                if (item is FolderVisualizerItem typedItem)
                {
                    ordered.Add(typedItem);
                }
            }

            return ordered;
        }

        private void TryAddItemForProgressiveLoad(
            FolderVisualizerItem item,
            ICollection<FolderVisualizerItem> targetItems,
            ISet<string> seenKeys)
        {
            string key = BuildProgressiveLoadItemKey(item);
            if (!seenKeys.Add(key))
            {
                return;
            }

            lock (progressiveLoadKeyLock)
            {
                if (progressiveLoadedItemKeys.Contains(key))
                {
                    return;
                }
            }

            targetItems.Add(item);
        }

        private List<FolderVisualizerItem> GetVisibleItemsOrderedTopToBottom()
        {
            if (folderListScrollViewer == null)
            {
                return new List<FolderVisualizerItem>();
            }

            List<(double top, FolderVisualizerItem item)> visibleItems = new List<(double top, FolderVisualizerItem item)>();
            foreach (ListViewItem container in FindVisualChildren<ListViewItem>(FolderListView))
            {
                if (container.DataContext is not FolderVisualizerItem item
                    || container.ActualHeight <= 0
                    || container.ActualWidth <= 0)
                {
                    continue;
                }

                try
                {
                    Rect bounds = container.TransformToAncestor(folderListScrollViewer)
                        .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                    if (bounds.Bottom < 0 || bounds.Top > folderListScrollViewer.ViewportHeight)
                    {
                        continue;
                    }

                    visibleItems.Add((bounds.Top, item));
                }
                catch (InvalidOperationException)
                {
                    // The container may no longer be connected during virtualization churn.
                }
            }

            return visibleItems
                .OrderBy(entry => entry.top)
                .Select(entry => entry.item)
                .ToList();
        }

        private static IEnumerable<TChild> FindVisualChildren<TChild>(DependencyObject root)
            where TChild : DependencyObject
        {
            if (root == null)
            {
                yield break;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is TChild typed)
                {
                    yield return typed;
                }

                foreach (TChild nested in FindVisualChildren<TChild>(child))
                {
                    yield return nested;
                }
            }
        }

        private static string BuildProgressiveLoadItemKey(FolderVisualizerItem item)
        {
            return string.Concat(
                NormalizeFolderOverrideKey(item.DirectoryPath),
                "|",
                item.IconPath ?? string.Empty,
                "|",
                item.PreviewPath ?? string.Empty);
        }

        internal static ImageSource LoadEmbeddedImage(string uri)
        {
            try
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(uri, UriKind.Absolute);
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Unable to load embedded visualizer resource '{uri}'.", ex);
                return CreateSolidPlaceholderImage();
            }
        }

        private static ImageSource CreateSolidPlaceholderImage()
        {
            const int width = 16;
            const int height = 16;
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[width * height * 4];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 82;
                pixels[i + 1] = 93;
                pixels[i + 2] = 108;
                pixels[i + 3] = 255;
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }

        internal void LoadCharacterItemsForTests()
        {
            List<FolderVisualizerItem> projectedItems = BuildCharacterItems(CharacterFolder.FullList);
            allItems.Clear();
            allItems.AddRange(projectedItems);
            RecomputeDerivedItemFields();
            UpdateSummaryText();
            PruneTagAssignmentsToExistingItems();
            RefreshSelectedFolderTagPanel();
            ApplySelectedViewPreset();
        }

        private static FolderVisualizerConfig CloneConfig(FolderVisualizerConfig source)
        {
            FolderVisualizerConfig clone = new FolderVisualizerConfig
            {
                SelectedPresetId = source?.SelectedPresetId ?? string.Empty,
                SelectedPresetName = source?.SelectedPresetName ?? string.Empty,
                Presets = new List<FolderVisualizerViewPreset>()
            };

            if (source?.Presets == null)
            {
                return clone;
            }

            foreach (FolderVisualizerViewPreset preset in source.Presets)
            {
                clone.Presets.Add(ClonePreset(preset));
            }

            return clone;
        }

        internal static FolderVisualizerViewPreset ClonePreset(FolderVisualizerViewPreset preset)
        {
            FolderVisualizerViewPreset clone = new FolderVisualizerViewPreset
            {
                Id = preset?.Id ?? Guid.NewGuid().ToString("N"),
                Name = preset?.Name ?? "View",
                Mode = preset?.Mode ?? FolderVisualizerLayoutMode.Normal,
                Normal = new FolderVisualizerNormalViewConfig
                {
                    TileWidth = preset?.Normal?.TileWidth ?? 170,
                    TileHeight = preset?.Normal?.TileHeight ?? 182,
                    IconSize = preset?.Normal?.IconSize ?? 18,
                    NameFontSize = preset?.Normal?.NameFontSize ?? 12,
                    InternalTilePadding = preset?.Normal?.InternalTilePadding ?? 0,
                    ScrollWheelStep = preset?.Normal?.ScrollWheelStep ?? 90,
                    TilePadding = preset?.Normal?.TilePadding ?? 8,
                    TileMargin = preset?.Normal?.TileMargin ?? 4
                },
                Table = new FolderVisualizerTableViewConfig
                {
                    RowHeight = preset?.Table?.RowHeight ?? 34,
                    FontSize = preset?.Table?.FontSize ?? 13,
                    Columns = new List<FolderVisualizerTableColumnConfig>()
                }
            };

            List<FolderVisualizerTableColumnConfig>? columns = preset?.Table?.Columns;
            if (columns != null)
            {
                foreach (FolderVisualizerTableColumnConfig column in columns)
                {
                    clone.Table.Columns.Add(new FolderVisualizerTableColumnConfig
                    {
                        Key = column.Key,
                        IsVisible = column.IsVisible,
                        Order = column.Order,
                        Width = column.Width
                    });
                }
            }

            return clone;
        }
    }

    /// <summary>
    /// Represents a folder entry in the visualizer.
    /// </summary>
    public sealed class FolderVisualizerItem : INotifyPropertyChanged
    {
        private string indexText = string.Empty;
        private string rowPositionText = string.Empty;
        private string previewPath = string.Empty;
        private string tagsText = string.Empty;
        private string iconTypeText = string.Empty;
        private bool integrityHasFailures;
        private int integrityFailureCount;
        private string integrityFailureMessages = string.Empty;
        private ImageSource iconImage = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");
        private ImageSource previewImage = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");

        public int Index { get; set; }
        public string IndexText
        {
            get => indexText;
            set
            {
                string safeValue = value ?? string.Empty;
                if (string.Equals(indexText, safeValue, StringComparison.Ordinal))
                {
                    return;
                }

                indexText = safeValue;
                OnPropertyChanged();
            }
        }
        public string RowPositionText
        {
            get => rowPositionText;
            set
            {
                string safeValue = value ?? string.Empty;
                if (string.Equals(rowPositionText, safeValue, StringComparison.Ordinal))
                {
                    return;
                }

                rowPositionText = safeValue;
                OnPropertyChanged();
            }
        }
        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string PreviewPath
        {
            get => previewPath;
            set
            {
                if (string.Equals(previewPath, value, StringComparison.Ordinal))
                {
                    return;
                }

                previewPath = value;
                OnPropertyChanged();
            }
        }
        public string CharIniPath { get; set; } = string.Empty;
        public bool HasCharIni { get; set; }
        public DateTime LastModified { get; set; }
        public string LastModifiedText { get; set; } = string.Empty;
        public int EmoteCount { get; set; }
        public string EmoteCountText { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeText { get; set; } = string.Empty;
        public string TagsText
        {
            get => tagsText;
            set
            {
                string safeValue = value ?? string.Empty;
                if (string.Equals(tagsText, safeValue, StringComparison.Ordinal))
                {
                    return;
                }

                tagsText = safeValue;
                OnPropertyChanged();
            }
        }
        public string IconTypeText
        {
            get => iconTypeText;
            set
            {
                string safeValue = value ?? string.Empty;
                if (string.Equals(iconTypeText, safeValue, StringComparison.Ordinal))
                {
                    return;
                }

                iconTypeText = safeValue;
                OnPropertyChanged();
            }
        }
        public string ReadmePath { get; set; } = string.Empty;
        public bool HasReadme { get; set; }
        public bool IntegrityHasFailures
        {
            get => integrityHasFailures;
            set
            {
                if (integrityHasFailures == value)
                {
                    return;
                }

                integrityHasFailures = value;
                OnPropertyChanged();
            }
        }

        public int IntegrityFailureCount
        {
            get => integrityFailureCount;
            set
            {
                if (integrityFailureCount == value)
                {
                    return;
                }

                integrityFailureCount = value;
                OnPropertyChanged();
            }
        }

        public string IntegrityFailureMessages
        {
            get => integrityFailureMessages;
            set
            {
                string safeValue = value ?? string.Empty;
                if (string.Equals(integrityFailureMessages, safeValue, StringComparison.Ordinal))
                {
                    return;
                }

                integrityFailureMessages = safeValue;
                OnPropertyChanged();
            }
        }

        public ImageSource IconImage
        {
            get => iconImage;
            set
            {
                if (ReferenceEquals(iconImage, value))
                {
                    return;
                }

                iconImage = value;
                OnPropertyChanged();
            }
        }

        public ImageSource PreviewImage
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
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FolderVisualizerGridHeaderInfo
    {
        public FolderVisualizerTableColumnKey Key { get; }
        public string Text { get; }
        public bool IsSortable { get; }

        public FolderVisualizerGridHeaderInfo(FolderVisualizerTableColumnKey key, string text, bool isSortable)
        {
            Key = key;
            Text = text;
            IsSortable = isSortable;
        }

        public override string ToString()
        {
            return Text;
        }
    }

    public enum FolderFilterConnector
    {
        And,
        Or
    }

    public enum FolderFilterOperator
    {
        Contains,
        DoesNotContain,
        Equals,
        NotEquals,
        StartsWith,
        EndsWith,
        InList,
        NotInList,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Between
    }

    public sealed class FolderFilterListValue : INotifyPropertyChanged
    {
        private string text = string.Empty;
        private FolderFilterRule? owner;

        public FolderFilterRule? Owner
        {
            get => owner;
            set
            {
                if (ReferenceEquals(owner, value))
                {
                    return;
                }

                owner = value;
                OnPropertyChanged();
            }
        }

        public string Text
        {
            get => text;
            set
            {
                string safe = value ?? string.Empty;
                if (string.Equals(text, safe, StringComparison.Ordinal))
                {
                    return;
                }

                text = safe;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FolderFilterEnumOption : INotifyPropertyChanged
    {
        private string value = string.Empty;
        private bool isSelected;
        private FolderFilterRule? owner;

        public FolderFilterRule? Owner
        {
            get => owner;
            set
            {
                if (ReferenceEquals(owner, value))
                {
                    return;
                }

                owner = value;
                OnPropertyChanged();
            }
        }

        public string Value
        {
            get => value;
            set
            {
                string safe = value ?? string.Empty;
                if (string.Equals(this.value, safe, StringComparison.Ordinal))
                {
                    return;
                }

                this.value = safe;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                Owner?.OnEnumSelectionChanged();
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class FolderFilterRule : INotifyPropertyChanged
    {
        private bool isGroup;
        private FolderVisualizerTableColumnKey columnKey = FolderVisualizerTableColumnKey.Name;
        private FolderFilterOperator filterOperator = FolderFilterOperator.Contains;
        private string value = string.Empty;
        private string secondValue = string.Empty;
        private FolderFilterConnector connector = FolderFilterConnector.And;
        private bool isActive = true;
        private FolderFilterRule? parent;
        private readonly ObservableCollection<FolderFilterRule> children = new ObservableCollection<FolderFilterRule>();
        private readonly ObservableCollection<FolderFilterListValue> listValues = new ObservableCollection<FolderFilterListValue>();
        private readonly ObservableCollection<FolderFilterEnumOption> enumOptions = new ObservableCollection<FolderFilterEnumOption>();
        private bool suppressListSync;

        public static FolderFilterRule CreateGroup(FolderFilterConnector connector = FolderFilterConnector.And)
        {
            return new FolderFilterRule
            {
                IsGroup = true,
                Connector = connector,
                IsActive = true
            };
        }

        public static FolderFilterRule CreateCondition()
        {
            FolderFilterRule rule = new FolderFilterRule
            {
                IsGroup = false,
                ColumnKey = FolderVisualizerTableColumnKey.Name,
                Operator = FolderFilterOperator.Contains,
                IsActive = true
            };
            rule.EnsureListTail();
            return rule;
        }

        public bool IsGroup
        {
            get => isGroup;
            set
            {
                if (isGroup == value)
                {
                    return;
                }

                isGroup = value;
                OnPropertyChanged();
            }
        }

        public FolderFilterRule? Parent
        {
            get => parent;
            set
            {
                if (ReferenceEquals(parent, value))
                {
                    return;
                }

                parent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRemove));
            }
        }

        public bool CanRemove => Parent != null;

        public ObservableCollection<FolderFilterRule> Children => children;
        public ObservableCollection<FolderFilterListValue> ListValues => listValues;
        public ObservableCollection<FolderFilterEnumOption> EnumOptions => enumOptions;
        public bool UsesListValues => Operator == FolderFilterOperator.InList || Operator == FolderFilterOperator.NotInList;
        public bool UsesEnumSelection => IsEnumColumn(ColumnKey) && (Operator == FolderFilterOperator.Equals || Operator == FolderFilterOperator.NotEquals);
        public bool UsesDateInput => IsDateColumn(ColumnKey);
        public bool UsesSecondValue => Operator == FolderFilterOperator.Between;
        public DateTime? ValueDate
        {
            get => ParseDateValue(Value);
            set => Value = value?.ToString("yyyy-MM-dd") ?? string.Empty;
        }
        public DateTime? SecondValueDate
        {
            get => ParseDateValue(SecondValue);
            set => SecondValue = value?.ToString("yyyy-MM-dd") ?? string.Empty;
        }
        public string SelectedEnumSummary => BuildEnumSelectionSummary();
        public IReadOnlyList<FolderFilterOperator> AvailableOperators => GetAllowedOperators(ColumnKey);

        public FolderVisualizerTableColumnKey ColumnKey
        {
            get => columnKey;
            set
            {
                if (columnKey == value)
                {
                    return;
                }

                columnKey = value;
                EnsureOperatorStillValid();
                EnsureEnumOptions();
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvailableOperators));
                OnPropertyChanged(nameof(UsesEnumSelection));
                OnPropertyChanged(nameof(UsesDateInput));
                OnPropertyChanged(nameof(SelectedEnumSummary));
                OnPropertyChanged(nameof(ValueDate));
                OnPropertyChanged(nameof(SecondValueDate));
            }
        }

        public FolderFilterOperator Operator
        {
            get => filterOperator;
            set
            {
                if (filterOperator == value)
                {
                    return;
                }

                filterOperator = value;
                if (!UsesSecondValue)
                {
                    SecondValue = string.Empty;
                }
                EnsureListTail();
                OnPropertyChanged();
                OnPropertyChanged(nameof(UsesListValues));
                OnPropertyChanged(nameof(UsesSecondValue));
                OnPropertyChanged(nameof(UsesEnumSelection));
                OnPropertyChanged(nameof(UsesDateInput));
                OnPropertyChanged(nameof(SelectedEnumSummary));
            }
        }

        public string Value
        {
            get => value;
            set
            {
                string safe = value ?? string.Empty;
                if (string.Equals(this.value, safe, StringComparison.Ordinal))
                {
                    return;
                }

                this.value = safe;
                if (!suppressListSync)
                {
                    SyncListValuesFromValue();
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValueDate));
            }
        }

        public string SecondValue
        {
            get => secondValue;
            set
            {
                string safe = value ?? string.Empty;
                if (string.Equals(secondValue, safe, StringComparison.Ordinal))
                {
                    return;
                }

                secondValue = safe;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecondValueDate));
            }
        }

        public FolderFilterConnector Connector
        {
            get => connector;
            set
            {
                if (connector == value)
                {
                    return;
                }

                connector = value;
                OnPropertyChanged();
            }
        }

        public bool IsActive
        {
            get => isActive;
            set
            {
                if (isActive == value)
                {
                    return;
                }

                isActive = value;
                OnPropertyChanged();
            }
        }

        public FolderFilterRule Clone()
        {
            FolderFilterRule clone = new FolderFilterRule
            {
                IsGroup = IsGroup,
                ColumnKey = ColumnKey,
                Operator = Operator,
                Value = Value,
                SecondValue = SecondValue,
                Connector = Connector,
                IsActive = IsActive
            };

            foreach (FolderFilterRule child in Children)
            {
                FolderFilterRule clonedChild = child.Clone();
                clonedChild.Parent = clone;
                clone.Children.Add(clonedChild);
            }

            clone.suppressListSync = true;
            clone.ListValues.Clear();
            foreach (FolderFilterListValue listValue in ListValues)
            {
                clone.ListValues.Add(new FolderFilterListValue
                {
                    Owner = clone,
                    Text = listValue.Text
                });
            }
            clone.suppressListSync = false;
            clone.SyncValueFromListValues();
            clone.EnsureListTail();
            clone.EnsureEnumOptions();
            foreach (FolderFilterEnumOption option in clone.EnumOptions)
            {
                option.IsSelected = EnumOptions.Any(original =>
                    string.Equals(original.Value, option.Value, StringComparison.OrdinalIgnoreCase)
                    && original.IsSelected);
            }
            clone.OnEnumSelectionChanged();

            return clone;
        }

        public int CountActiveConditions()
        {
            if (!IsActive)
            {
                return 0;
            }

            if (!IsGroup)
            {
                return 1;
            }

            int count = 0;
            foreach (FolderFilterRule child in Children)
            {
                count += child.CountActiveConditions();
            }

            return count;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnListValueChanged()
        {
            SyncValueFromListValues();
            EnsureListTail();
        }

        public void OnEnumSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedEnumSummary));
            if (!UsesEnumSelection)
            {
                return;
            }

            string joined = string.Join(", ", EnumOptions
                .Where(option => option.IsSelected)
                .Select(option => option.Value));
            Value = joined;
        }

        private void EnsureOperatorStillValid()
        {
            IReadOnlyList<FolderFilterOperator> allowed = AvailableOperators;
            if (allowed.Count == 0)
            {
                return;
            }

            if (!allowed.Contains(Operator))
            {
                Operator = allowed[0];
            }
        }

        private static IReadOnlyList<FolderFilterOperator> GetAllowedOperators(FolderVisualizerTableColumnKey key)
        {
            if (IsEnumColumn(key))
            {
                return new[]
                {
                    FolderFilterOperator.Equals,
                    FolderFilterOperator.NotEquals
                };
            }

            if (key == FolderVisualizerTableColumnKey.LastModified)
            {
                return new[]
                {
                    FolderFilterOperator.Equals,
                    FolderFilterOperator.NotEquals,
                    FolderFilterOperator.GreaterThan,
                    FolderFilterOperator.GreaterThanOrEqual,
                    FolderFilterOperator.LessThan,
                    FolderFilterOperator.LessThanOrEqual,
                    FolderFilterOperator.Between
                };
            }

            if (key == FolderVisualizerTableColumnKey.RowNumber
                || key == FolderVisualizerTableColumnKey.EmoteCount
                || key == FolderVisualizerTableColumnKey.Size)
            {
                return new[]
                {
                    FolderFilterOperator.Equals,
                    FolderFilterOperator.NotEquals,
                    FolderFilterOperator.GreaterThan,
                    FolderFilterOperator.GreaterThanOrEqual,
                    FolderFilterOperator.LessThan,
                    FolderFilterOperator.LessThanOrEqual,
                    FolderFilterOperator.Between
                };
            }

            return new[]
            {
                FolderFilterOperator.Contains,
                FolderFilterOperator.DoesNotContain,
                FolderFilterOperator.Equals,
                FolderFilterOperator.NotEquals,
                FolderFilterOperator.StartsWith,
                FolderFilterOperator.EndsWith,
                FolderFilterOperator.InList,
                FolderFilterOperator.NotInList
            };
        }

        private static bool IsEnumColumn(FolderVisualizerTableColumnKey key)
        {
            return key == FolderVisualizerTableColumnKey.OpenCharIni
                || key == FolderVisualizerTableColumnKey.Readme
                || key == FolderVisualizerTableColumnKey.IconType;
        }

        private static bool IsDateColumn(FolderVisualizerTableColumnKey key)
        {
            return key == FolderVisualizerTableColumnKey.LastModified;
        }

        private static DateTime? ParseDateValue(string raw)
        {
            if (DateTime.TryParse(raw, out DateTime parsed))
            {
                return parsed.Date;
            }

            return null;
        }

        private static IReadOnlyList<string> GetEnumValues(FolderVisualizerTableColumnKey key)
        {
            if (key == FolderVisualizerTableColumnKey.OpenCharIni || key == FolderVisualizerTableColumnKey.Readme)
            {
                return new[] { "True", "False" };
            }

            if (key == FolderVisualizerTableColumnKey.IconType)
            {
                return new[] { "Character Icon", "Placeholder", "Button Fallback" };
            }

            return Array.Empty<string>();
        }

        private void EnsureEnumOptions()
        {
            List<string> currentSelections = EnumOptions
                .Where(option => option.IsSelected)
                .Select(option => option.Value)
                .ToList();

            EnumOptions.Clear();
            foreach (string enumValue in GetEnumValues(ColumnKey))
            {
                EnumOptions.Add(new FolderFilterEnumOption
                {
                    Owner = this,
                    Value = enumValue,
                    IsSelected = currentSelections.Any(selected => string.Equals(selected, enumValue, StringComparison.OrdinalIgnoreCase))
                });
            }

            OnPropertyChanged(nameof(SelectedEnumSummary));
        }

        private string BuildEnumSelectionSummary()
        {
            if (EnumOptions.Count == 0)
            {
                return "Select values";
            }

            List<string> selected = EnumOptions
                .Where(option => option.IsSelected)
                .Select(option => option.Value)
                .ToList();
            if (selected.Count == 0)
            {
                return "Select values";
            }

            return string.Join(", ", selected);
        }

        private void SyncListValuesFromValue()
        {
            if (!UsesListValues)
            {
                return;
            }

            suppressListSync = true;
            List<string> values = (value ?? string.Empty)
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();

            ListValues.Clear();
            foreach (string entry in values)
            {
                ListValues.Add(new FolderFilterListValue
                {
                    Owner = this,
                    Text = entry
                });
            }

            suppressListSync = false;
            EnsureListTail();
        }

        private void SyncValueFromListValues()
        {
            if (suppressListSync || !UsesListValues)
            {
                return;
            }

            string joined = string.Join(", ", ListValues
                .Select(entry => entry.Text?.Trim() ?? string.Empty)
                .Where(entry => !string.IsNullOrWhiteSpace(entry)));

            suppressListSync = true;
            Value = joined;
            suppressListSync = false;
        }

        private void EnsureListTail()
        {
            if (!UsesListValues)
            {
                return;
            }

            for (int i = ListValues.Count - 2; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(ListValues[i].Text) && string.IsNullOrWhiteSpace(ListValues[i + 1].Text))
                {
                    ListValues.RemoveAt(i + 1);
                }
            }

            if (ListValues.Count == 0)
            {
                ListValues.Add(new FolderFilterListValue
                {
                    Owner = this,
                    Text = string.Empty
                });
                return;
            }

            FolderFilterListValue last = ListValues[ListValues.Count - 1];
            if (!string.IsNullOrWhiteSpace(last.Text))
            {
                ListValues.Add(new FolderFilterListValue
                {
                    Owner = this,
                    Text = string.Empty
                });
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class FolderVisualizerDiskCacheContainer
    {
        public int Version { get; set; }
        public string Signature { get; set; } = string.Empty;
        public List<FolderVisualizerDiskCacheItem> Items { get; set; } = new List<FolderVisualizerDiskCacheItem>();
    }

    internal sealed class FolderVisualizerDiskCacheItem
    {
        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string PreviewPath { get; set; } = string.Empty;
        public string CharIniPath { get; set; } = string.Empty;
        public bool HasCharIni { get; set; }
        public DateTime LastModifiedUtc { get; set; }
        public string LastModifiedText { get; set; } = string.Empty;
        public int EmoteCount { get; set; }
        public string EmoteCountText { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeText { get; set; } = string.Empty;
        public string ReadmePath { get; set; } = string.Empty;
        public bool HasReadme { get; set; }
        public bool IntegrityHasFailures { get; set; }
        public int IntegrityFailureCount { get; set; }
        public string IntegrityFailureMessages { get; set; } = string.Empty;
    }

    internal sealed class FolderTagCacheFileData
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, List<string>> FolderTags { get; set; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public List<string>? ActiveTagFilters { get; set; } // legacy v0/v1 include-only field
        public List<string> ActiveIncludeTagFilters { get; set; } = new List<string>();
        public List<string> ActiveExcludeTagFilters { get; set; } = new List<string>();
        public double TagPanelWidth { get; set; } = 260;
        public bool TagPanelCollapsed { get; set; }
        public VisualizerWindowState? TagFilterWindowState { get; set; } = new VisualizerWindowState
        {
            Width = 500,
            Height = 560,
            IsMaximized = false
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public partial class CharacterFolderVisualizerWindow
    {
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);
    }
}
