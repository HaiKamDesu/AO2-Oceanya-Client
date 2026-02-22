using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        private static string cachedSignature = string.Empty;
        private static List<FolderVisualizerItem> cachedItems = new List<FolderVisualizerItem>();

        private readonly Action? onAssetsRefreshed;
        private readonly Func<FolderVisualizerItem, bool>? canSetCharacterInClient;
        private readonly Action<FolderVisualizerItem>? setCharacterInClient;
        private readonly Dictionary<string, ImageSource> imageCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FolderVisualizerItem> allItems = new List<FolderVisualizerItem>();

        private bool hasLoaded;
        private bool applyingSavedWindowState;
        private bool suppressViewSelectionChanged;
        private FolderVisualizerConfig visualizerConfig = new FolderVisualizerConfig();
        private ICollectionView? itemsView;
        private string searchText = string.Empty;
        private string pendingSearchText = string.Empty;
        private readonly DispatcherTimer searchDebounceTimer;
        private FolderVisualizerItem? contextMenuTargetItem;
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
            nameof(InternalTileRowPadding), typeof(Thickness), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(new Thickness(0, 2, 0, 2)));

        public static readonly DependencyProperty TilePaddingProperty = DependencyProperty.Register(
            nameof(TilePadding), typeof(Thickness), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(new Thickness(8)));

        public static readonly DependencyProperty TileMarginProperty = DependencyProperty.Register(
            nameof(TileMargin), typeof(Thickness), typeof(CharacterFolderVisualizerWindow), new PropertyMetadata(new Thickness(4)));

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

            visualizerConfig = CloneConfig(SaveFile.Data.FolderVisualizer);
            ApplySavedWindowState();
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
                allItems.Clear();
                allItems.AddRange(cachedItems);
                itemsView = null;
                SummaryText.Text = $"Characters indexed: {allItems.Count}";
                ApplySelectedViewPreset();
                return;
            }

            await WaitForm.ShowFormAsync("Loading character folder visualizer...", this);
            try
            {
                List<FolderVisualizerItem> projected = await Task.Run(() => BuildCharacterItems(characters));

                cachedSignature = signature;
                cachedItems = projected;

                allItems.Clear();
                allItems.AddRange(projected);
                itemsView = null;
                SummaryText.Text = $"Characters indexed: {allItems.Count}";
                WaitForm.SetSubtitle("Rendering selected view...");
                await Dispatcher.Yield(DispatcherPriority.Background);
                ApplySelectedViewPreset();
                await Dispatcher.Yield(DispatcherPriority.Background);
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

                string previewPath = !string.IsNullOrWhiteSpace(characterFolder.ViewportIdleSpritePath)
                    ? characterFolder.ViewportIdleSpritePath
                    : ResolveFirstCharacterIdleSpritePath(characterFolder);

                string iconPath = characterFolder.CharIconPath ?? string.Empty;
                ImageSource iconImage = LoadImage(iconPath, 48);
                ImageSource previewImage = LoadImage(previewPath, 220);
                string charIniPath = characterFolder.PathToConfigIni ?? string.Empty;
                DateTime lastModified = File.Exists(charIniPath)
                    ? File.GetLastWriteTime(charIniPath)
                    : Directory.GetLastWriteTime(characterFolder.DirectoryPath ?? string.Empty);
                int emoteCount = characterFolder.configINI?.EmotionsCount ?? 0;
                long sizeBytes = GetDirectorySizeSafe(characterFolder.DirectoryPath ?? string.Empty);
                string readmePath = ResolveCharacterReadmePath(characterFolder.DirectoryPath ?? string.Empty);

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
                    IconImage = iconImage,
                    PreviewImage = previewImage
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

            string query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.DirectoryPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.PreviewPath.Contains(query, StringComparison.OrdinalIgnoreCase);
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
                cachedSignature = string.Empty;
                cachedItems = new List<FolderVisualizerItem>();

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
            }));
        }

        private void FolderListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            contextMenuTargetItem = ResolveItemFromOriginalSource(e.OriginalSource as DependencyObject);
            if (contextMenuTargetItem != null)
            {
                FolderListView.SelectedItem = contextMenuTargetItem;
            }
        }

        private void FolderListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            FolderVisualizerItem? target = contextMenuTargetItem ?? ResolveItemFromOriginalSource(e.OriginalSource as DependencyObject);
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            UntrackTableColumnWidth();
            PersistVisualizerConfig();
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
                cachedSignature = string.Empty;
                cachedItems = new List<FolderVisualizerItem>();
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

            if (imageCache.TryGetValue(cacheKey, out ImageSource? cachedImage))
            {
                return cachedImage;
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

            imageCache[cacheKey] = loadedImage;
            return loadedImage;
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
            SummaryText.Text = $"Characters indexed: {allItems.Count}";
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
                    InternalTilePadding = preset?.Normal?.InternalTilePadding ?? 2,
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
    public sealed class FolderVisualizerItem
    {
        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public string PreviewPath { get; set; } = string.Empty;
        public string CharIniPath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string LastModifiedText { get; set; } = string.Empty;
        public int EmoteCount { get; set; }
        public string EmoteCountText { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeText { get; set; } = string.Empty;
        public string ReadmePath { get; set; } = string.Empty;
        public bool HasReadme { get; set; }

        public ImageSource IconImage { get; set; } = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");

        public ImageSource PreviewImage { get; set; } = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");
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
