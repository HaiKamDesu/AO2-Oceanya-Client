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
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using AOBot_Testing.Structures;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Displays a configurable Windows-like visualizer for local AO character folders.
    /// </summary>
    public partial class CharacterFolderVisualizerWindow : Window
    {
        private const string FallbackFolderPackUri =
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png";

        private static readonly ImageSource FallbackFolderImage = LoadEmbeddedImage(FallbackFolderPackUri);
        private const int VisualizerDiskCacheVersion = 1;
        private const int FolderTagCacheVersion = 1;
        private const string UntaggedFilterToken = "(none)";
        private static readonly JsonSerializerOptions CacheJsonOptions = new JsonSerializerOptions { WriteIndented = false };
        private static readonly JsonSerializerOptions FolderTagCacheJsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private static string cachedSignature = string.Empty;
        private static List<FolderVisualizerItem> cachedItems = new List<FolderVisualizerItem>();
        private static string diskCacheFilePath = string.Empty;
        private static bool diskCachePathInitialized;
        private static string folderTagCacheFilePath = string.Empty;
        private static bool folderTagCachePathInitialized;

        private readonly Action? onAssetsRefreshed;
        private readonly Func<FolderVisualizerItem, bool>? canSetCharacterInClient;
        private readonly Action<FolderVisualizerItem>? setCharacterInClient;
        private readonly Dictionary<string, ImageSource> imageCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly object imageCacheLock = new object();
        private readonly List<FolderVisualizerItem> allItems = new List<FolderVisualizerItem>();
        private CancellationTokenSource? progressiveImageLoadCancellation;

        private bool hasLoaded;
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
        private FolderVisualizerTableColumnKey? currentSortColumnKey;
        private ListSortDirection currentSortDirection = ListSortDirection.Ascending;
        private readonly Dictionary<GridViewColumn, FolderVisualizerTableColumnConfig> tableColumnMap =
            new Dictionary<GridViewColumn, FolderVisualizerTableColumnConfig>();
        private readonly Dictionary<GridViewColumn, EventHandler> columnWidthHandlers =
            new Dictionary<GridViewColumn, EventHandler>();

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

        /// <summary>
        /// Initializes a new visualizer window.
        /// </summary>
        public CharacterFolderVisualizerWindow(
            Action? onAssetsRefreshed,
            Func<FolderVisualizerItem, bool>? canSetCharacterInClient = null,
            Action<FolderVisualizerItem>? setCharacterInClient = null)
        {
            InitializeComponent();
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
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWorkAreaMaxBounds();

            if (hasLoaded)
            {
                return;
            }

            hasLoaded = true;
            await LoadCharacterItemsAsync();
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

            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
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

            if (!forceRebuild && cachedItems.Count > 0 && string.Equals(cachedSignature, signature, StringComparison.Ordinal))
            {
                await WaitForm.ShowFormAsync("Loading character folder visualizer...", this);
                try
                {
                    WaitForm.SetSubtitle("Using cached character index...");
                allItems.Clear();
                allItems.AddRange(cachedItems);
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

            if (!forceRebuild && TryLoadProjectedItemsFromDisk(signature, out List<FolderVisualizerItem>? diskCachedItems))
            {
                await WaitForm.ShowFormAsync("Loading character folder visualizer...", this);
                try
                {
                    WaitForm.SetSubtitle("Loading indexed data from disk cache...");
                    cachedSignature = signature;
                    cachedItems = diskCachedItems;
                    allItems.Clear();
                    allItems.AddRange(diskCachedItems);
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

                cachedSignature = signature;
                cachedItems = projected;
                SaveProjectedItemsToDisk(signature, projected);

                allItems.Clear();
                allItems.AddRange(projected);
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
            HashCode hashCode = new HashCode();
            hashCode.Add(characters.Count);

            foreach (CharacterFolder folder in characters)
            {
                hashCode.Add(folder.Name, StringComparer.OrdinalIgnoreCase);
                hashCode.Add(folder.DirectoryPath, StringComparer.OrdinalIgnoreCase);
                hashCode.Add(folder.CharIconPath, StringComparer.OrdinalIgnoreCase);
                hashCode.Add(folder.ViewportIdleSpritePath, StringComparer.OrdinalIgnoreCase);
                if (TryGetFolderPreviewOverrideEmoteId(folder.DirectoryPath, out int overrideEmoteId))
                {
                    hashCode.Add(overrideEmoteId);
                }
            }

            return hashCode.ToHashCode().ToString();
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
                    Name = characterFolder.Name,
                    DirectoryPath = characterFolder.DirectoryPath ?? string.Empty,
                    IconPath = iconPath,
                    PreviewPath = previewPath,
                    CharIniPath = charIniPath,
                    LastModified = lastModified,
                    LastModifiedText = lastModified.ToString("yyyy-MM-dd HH:mm"),
                    EmoteCount = emoteCount,
                    EmoteCountText = emoteCount.ToString(),
                    SizeBytes = sizeBytes,
                    SizeText = FormatBytes(sizeBytes),
                    ReadmePath = readmePath,
                    HasReadme = !string.IsNullOrWhiteSpace(readmePath),
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
            UpdateSummaryText();
        }

        private void ApplyTablePreset(FolderVisualizerViewPreset preset)
        {
            FolderVisualizerTableViewConfig table = preset.Table;
            FolderListView.ItemTemplate = null;
            FolderListView.ItemsPanel = (ItemsPanelTemplate)FindResource("DetailsItemsPanelTemplate");
            FolderListView.ItemContainerStyle = BuildDetailsContainerStyle(table);
            FolderListView.View = BuildDetailsGridView(preset);
            ScrollViewer.SetHorizontalScrollBarVisibility(FolderListView, ScrollBarVisibility.Auto);
            FolderListView.ItemsSource = null;
            FolderListView.ItemsSource = GetOrCreateItemsView();
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
                    FolderVisualizerTableColumnKey.Name => item.Name,
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
            style.Setters.Add(new Setter(HeightProperty, table.RowHeight));
            return style;
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
                    gridColumn.DisplayMemberBinding = new Binding(GetColumnBindingPath(column.Key));
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
                FolderVisualizerTableColumnKey.Icon => string.Empty,
                FolderVisualizerTableColumnKey.Name => "Name",
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
            return key != FolderVisualizerTableColumnKey.Icon
                && key != FolderVisualizerTableColumnKey.OpenCharIni
                && key != FolderVisualizerTableColumnKey.Readme;
        }

        private static string GetColumnBindingPath(FolderVisualizerTableColumnKey key)
        {
            return key switch
            {
                FolderVisualizerTableColumnKey.Name => nameof(FolderVisualizerItem.Name),
                FolderVisualizerTableColumnKey.DirectoryPath => nameof(FolderVisualizerItem.DirectoryPath),
                FolderVisualizerTableColumnKey.PreviewPath => nameof(FolderVisualizerItem.PreviewPath),
                FolderVisualizerTableColumnKey.LastModified => nameof(FolderVisualizerItem.LastModifiedText),
                FolderVisualizerTableColumnKey.EmoteCount => nameof(FolderVisualizerItem.EmoteCountText),
                FolderVisualizerTableColumnKey.Size => nameof(FolderVisualizerItem.SizeText),
                FolderVisualizerTableColumnKey.IntegrityFailures => nameof(FolderVisualizerItem.IntegrityFailureMessages),
                _ => nameof(FolderVisualizerItem.Name)
            };
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

            if (header.Content is not FolderVisualizerGridHeaderInfo info)
            {
                return;
            }

            ContextMenu contextMenu = new ContextMenu();
            MenuItem bestFitItem = new MenuItem
            {
                Header = "Best Fit"
            };
            bestFitItem.Click += (_, _) => BestFitColumn(header.Column, info.Key, info.Text);
            contextMenu.Items.Add(bestFitItem);
            contextMenu.IsOpen = true;
            e.Handled = true;
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

            if (FolderListView.View == null || currentSortColumnKey == null)
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
                FolderVisualizerTableColumnKey.Name => nameof(FolderVisualizerItem.Name),
                FolderVisualizerTableColumnKey.DirectoryPath => nameof(FolderVisualizerItem.DirectoryPath),
                FolderVisualizerTableColumnKey.PreviewPath => nameof(FolderVisualizerItem.PreviewPath),
                FolderVisualizerTableColumnKey.LastModified => nameof(FolderVisualizerItem.LastModified),
                FolderVisualizerTableColumnKey.EmoteCount => nameof(FolderVisualizerItem.EmoteCount),
                FolderVisualizerTableColumnKey.Size => nameof(FolderVisualizerItem.SizeBytes),
                FolderVisualizerTableColumnKey.IntegrityFailures => nameof(FolderVisualizerItem.IntegrityFailureMessages),
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

                imageCache.Clear();
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
            }));
        }

        private void FolderListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedItemForTagging = FolderListView.SelectedItem as FolderVisualizerItem;
            RefreshSelectedFolderTagPanel();
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

            TagFilterSelectionWindow selectionWindow = new TagFilterSelectionWindow(
                selectableFilters,
                activeIncludeTagFilters,
                activeExcludeTagFilters,
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

            await RefreshItemsAfterTagFilterChangeAsync();
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
        }

        private async Task RefreshItemsAfterTagFilterChangeAsync()
        {
            UpdateActiveTagFiltersText();
            QueueTagStateSave();
            await WaitForm.ShowFormAsync("Applying tag filters...", this);
            try
            {
                WaitForm.SetSubtitle("Refreshing visible folders...");
                await Dispatcher.Yield(DispatcherPriority.Background);
                ICollectionView view = GetOrCreateItemsView();
                view.Refresh();
                UpdateSummaryText();
                await Dispatcher.Yield(DispatcherPriority.Background);
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
            ActiveTagFiltersText.Text = $"Include: {includeText} | Exclude: {excludeText}";
        }

        private void UpdateSummaryText()
        {
            int total = allItems.Count;
            int visible = FolderListView.Items.Count;
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

            FolderVisualizerItem? cachedItem = cachedItems.FirstOrDefault(item =>
                string.Equals(NormalizeFolderOverrideKey(item.DirectoryPath), key, StringComparison.OrdinalIgnoreCase));
            if (cachedItem != null)
            {
                cachedItem.PreviewPath = previewPath;
                cachedItem.PreviewImage = previewImage;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            progressiveImageLoadCancellation?.Cancel();
            tagSaveDebounceTimer.Stop();
            UntrackTableColumnWidth();
            PersistVisualizerConfig();
            PersistTagState(saveToDisk: true);
            if (!applyingSavedWindowState)
            {
                SaveFile.Data.FolderVisualizerWindowState = CaptureWindowState();
                SaveFile.Save();
            }
            base.OnClosed(e);
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

                imageCache.Clear();
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
            cachedSignature = string.Empty;
            cachedItems = new List<FolderVisualizerItem>();
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
                    IconImage = LoadImage(item.IconPath ?? string.Empty, 48),
                    PreviewImage = LoadImage(item.PreviewPath ?? string.Empty, 220)
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
            string cacheKey = decodePixelWidth.ToString() + "|" + normalizedPath;

            lock (imageCacheLock)
            {
                if (imageCache.TryGetValue(cacheKey, out ImageSource? cachedImage))
                {
                    return cachedImage;
                }
            }

            ImageSource loadedImage = FallbackFolderImage;
            try
            {
                if (!string.IsNullOrWhiteSpace(normalizedPath) && File.Exists(normalizedPath))
                {
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.UriSource = new Uri(normalizedPath, UriKind.Absolute);
                    if (decodePixelWidth > 0)
                    {
                        bitmapImage.DecodePixelWidth = decodePixelWidth;
                    }

                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    loadedImage = bitmapImage;
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Unable to load character visualizer image '{normalizedPath}'.", ex);
            }

            lock (imageCacheLock)
            {
                imageCache[cacheKey] = loadedImage;
            }
            return loadedImage;
        }

        private void StartProgressiveImageLoading()
        {
            progressiveImageLoadCancellation?.Cancel();
            progressiveImageLoadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = progressiveImageLoadCancellation.Token;
            List<FolderVisualizerItem> snapshot = allItems.ToList();

            _ = Task.Run(async () =>
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    FolderVisualizerItem item = snapshot[i];
                    ImageSource iconImage = LoadImage(item.IconPath, 48);
                    ImageSource previewImage = LoadImage(item.PreviewPath, 220);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        item.IconImage = iconImage;
                        item.PreviewImage = previewImage;
                    }, DispatcherPriority.Background, cancellationToken);

                    if ((i + 1) % 24 == 0)
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
            }, cancellationToken);
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
        private string previewPath = string.Empty;
        private bool integrityHasFailures;
        private int integrityFailureCount;
        private string integrityFailureMessages = string.Empty;
        private ImageSource iconImage = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");
        private ImageSource previewImage = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");

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
        public DateTime LastModified { get; set; }
        public string LastModifiedText { get; set; } = string.Empty;
        public int EmoteCount { get; set; }
        public string EmoteCountText { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeText { get; set; } = string.Empty;
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
