using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingFrameDimension = System.Drawing.Imaging.FrameDimension;
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
    /// Displays emote previews for a single character folder.
    /// </summary>
    public partial class CharacterEmoteVisualizerWindow : Window
    {
        private const string FallbackFolderPackUri =
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png";

        private static readonly ImageSource FallbackImage =
            CharacterFolderVisualizerWindow.LoadEmbeddedImage(FallbackFolderPackUri);
        private static readonly ImageSource TransparentImage = CreateTransparentPlaceholderImage();

        private CharacterFolder character;
        private readonly Dictionary<string, ImageSource> imageCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly List<EmoteVisualizerItem> allItems = new List<EmoteVisualizerItem>();
        private readonly Dictionary<GridViewColumn, EmoteVisualizerTableColumnConfig> tableColumnMap =
            new Dictionary<GridViewColumn, EmoteVisualizerTableColumnConfig>();
        private readonly Dictionary<GridViewColumn, EventHandler> columnWidthHandlers =
            new Dictionary<GridViewColumn, EventHandler>();
        private readonly List<IAnimationPlayer> animationPlayers = new List<IAnimationPlayer>();
        private bool loopAnimations;

        private bool hasLoaded;
        private bool applyingSavedWindowState;
        private bool suppressViewSelectionChanged;
        private ICollectionView? itemsView;
        private string searchText = string.Empty;
        private EmoteVisualizerConfig visualizerConfig = new EmoteVisualizerConfig();
        private EmoteVisualizerTableColumnKey? currentSortColumnKey;
        private ListSortDirection currentSortDirection = ListSortDirection.Ascending;
        private EmoteVisualizerItem? contextMenuTargetItem;
        private double normalScrollWheelStep = 90;

        internal IReadOnlyList<EmoteVisualizerItem> EmoteItems => allItems;

        public static readonly DependencyProperty TileWidthProperty = DependencyProperty.Register(
            nameof(TileWidth), typeof(double), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(235d));

        public static readonly DependencyProperty TileHeightProperty = DependencyProperty.Register(
            nameof(TileHeight), typeof(double), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(210d));

        public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
            nameof(IconSize), typeof(double), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(18d));

        public static readonly DependencyProperty TileNameFontSizeProperty = DependencyProperty.Register(
            nameof(TileNameFontSize), typeof(double), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(12d));

        public static readonly DependencyProperty InternalTileRowPaddingProperty = DependencyProperty.Register(
            nameof(InternalTileRowPadding), typeof(Thickness), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(new Thickness(0)));

        public static readonly DependencyProperty TilePaddingProperty = DependencyProperty.Register(
            nameof(TilePadding), typeof(Thickness), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(new Thickness(8)));

        public static readonly DependencyProperty TileMarginProperty = DependencyProperty.Register(
            nameof(TileMargin), typeof(Thickness), typeof(CharacterEmoteVisualizerWindow), new PropertyMetadata(new Thickness(4)));

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

        public CharacterEmoteVisualizerWindow(CharacterFolder sourceCharacter)
        {
            InitializeComponent();
            character = EnsureCharacterLoaded(sourceCharacter ?? throw new ArgumentNullException(nameof(sourceCharacter)));
            visualizerConfig = CloneConfig(SaveFile.Data.EmoteVisualizer);
            loopAnimations = SaveFile.Data.LoopEmoteVisualizerAnimations;
            LoopAnimationsCheckBox.IsChecked = loopAnimations;
            ApplySavedWindowState();
            BindViewPresets();
        }

        private static CharacterFolder EnsureCharacterLoaded(CharacterFolder source)
        {
            string iniPath = source.PathToConfigIni?.Trim() ?? string.Empty;
            if (File.Exists(iniPath))
            {
                try
                {
                    CharacterFolder refreshed = CharacterFolder.Create(iniPath);
                    if (refreshed.configINI != null && refreshed.configINI.Emotions.Count > 0)
                    {
                        return refreshed;
                    }
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning($"Unable to refresh character config from '{iniPath}'.", ex);
                }
            }

            return source;
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
            await LoadEmoteItemsAsync();
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
            VisualizerWindowState state = SaveFile.Data.EmoteVisualizerWindowState ?? new VisualizerWindowState();
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

        private void BindViewPresets(bool applySelectedView = true)
        {
            suppressViewSelectionChanged = true;

            ViewModeCombo.ItemsSource = null;
            ViewModeCombo.ItemsSource = visualizerConfig.Presets;

            EmoteVisualizerViewPreset? selectedPreset = visualizerConfig.Presets.FirstOrDefault(p =>
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

        private async Task LoadEmoteItemsAsync()
        {
            await WaitForm.ShowFormAsync("Loading character visualizer...", this);
            try
            {
                WaitForm.SetSubtitle("Parsing emotes for: " + character.Name);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

                StopAndClearAnimationPlayers();
                allItems.Clear();
                allItems.AddRange(BuildEmoteItems(character));

                itemsView = null;
                SummaryText.Text = $"{character.Name} - Emotes indexed: {allItems.Count}";
                UpdateTopButtonsAvailability();
                WaitForm.SetSubtitle("Rendering selected view...");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                ApplySelectedViewPreset();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                ApplyLoopAnimationsSetting(loopAnimations);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        internal List<EmoteVisualizerItem> BuildEmoteItems(CharacterFolder sourceCharacter)
        {
            List<EmoteVisualizerItem> items = new List<EmoteVisualizerItem>();
            CharacterConfigINI? config = sourceCharacter.configINI;
            if (config == null)
            {
                return items;
            }

            string directoryPath = sourceCharacter.DirectoryPath ?? string.Empty;
            int maxId = Math.Max(config.EmotionsCount, config.Emotions.Keys.DefaultIfEmpty(0).Max());

            for (int id = 1; id <= maxId; id++)
            {
                if (!config.Emotions.TryGetValue(id, out Emote? emote) || emote == null)
                {
                    continue;
                }

                string iconPath = ResolveEmoteIconPath(emote, sourceCharacter);
                string preAnimationPath = ResolveEmoteFramePath(directoryPath, emote.PreAnimation);
                string animationPath = ResolveEmoteFramePath(directoryPath, emote.Animation);
                string displayName = string.IsNullOrWhiteSpace(emote.Name) ? $"Emote {id}" : emote.Name.Trim();
                bool hasPreAnimation = !string.IsNullOrWhiteSpace(emote.PreAnimation)
                    && !string.Equals(emote.PreAnimation.Trim(), "-", StringComparison.Ordinal);

                ImageSource iconImage = LoadImage(iconPath, 72);
                (ImageSource preImage, IAnimationPlayer? prePlayer) = LoadPreviewImage(preAnimationPath, 240);
                (ImageSource animationImage, IAnimationPlayer? animationPlayer) = LoadPreviewImage(animationPath, 240);
                if (!hasPreAnimation)
                {
                    preImage = TransparentImage;
                    prePlayer?.Stop();
                    prePlayer = null;
                }

                EmoteVisualizerItem item = new EmoteVisualizerItem
                {
                    Id = id,
                    IdText = id.ToString(),
                    Name = displayName,
                    NameWithId = $"{id}: {displayName}",
                    HasPreAnimation = hasPreAnimation,
                    IconPath = iconPath,
                    IconDimensions = GetImageDimensionsText(iconPath),
                    PreAnimationPath = preAnimationPath,
                    PreAnimationDimensions = hasPreAnimation
                        ? GetImageDimensionsText(preAnimationPath)
                        : "No preanim",
                    AnimationPath = animationPath,
                    AnimationDimensions = GetImageDimensionsText(animationPath),
                    IconImage = iconImage,
                    PreAnimationImage = preImage,
                    AnimationImage = animationImage,
                    PreAnimationPlayer = prePlayer,
                    AnimationPlayer = animationPlayer
                };

                if (prePlayer != null)
                {
                    prePlayer.FrameChanged += frame => item.PreAnimationImage = frame;
                }

                if (animationPlayer != null)
                {
                    animationPlayer.FrameChanged += frame => item.AnimationImage = frame;
                }

                items.Add(item);
            }

            return items;
        }

        private void ViewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressViewSelectionChanged)
            {
                return;
            }

            ApplySelectedViewPreset();
        }

        private void ApplySelectedViewPreset()
        {
            if (EmoteListView == null)
            {
                return;
            }

            EmoteVisualizerViewPreset? selectedPreset = ViewModeCombo.SelectedItem as EmoteVisualizerViewPreset;
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

            EmoteListView.FontSize = normal.NameFontSize;
            EmoteListView.View = null;
            EmoteListView.ItemTemplate = (DataTemplate)FindResource("IconTileTemplate");
            EmoteListView.ItemContainerStyle = (Style)FindResource("VisualizerIconItemStyle");
            EmoteListView.ItemsPanel = (ItemsPanelTemplate)FindResource("IconItemsPanelTemplate");
            ScrollViewer.SetHorizontalScrollBarVisibility(EmoteListView, ScrollBarVisibility.Disabled);
            EmoteListView.ItemsSource = null;
            EmoteListView.ItemsSource = GetOrCreateItemsView();
        }

        private void ApplyTablePreset(EmoteVisualizerViewPreset preset)
        {
            EmoteVisualizerTableViewConfig table = preset.Table;
            EmoteListView.ItemTemplate = null;
            EmoteListView.ItemsPanel = (ItemsPanelTemplate)FindResource("DetailsItemsPanelTemplate");
            EmoteListView.ItemContainerStyle = BuildDetailsContainerStyle(table);
            EmoteListView.View = BuildDetailsGridView(preset);
            ScrollViewer.SetHorizontalScrollBarVisibility(EmoteListView, ScrollBarVisibility.Auto);
            EmoteListView.ItemsSource = null;
            EmoteListView.ItemsSource = GetOrCreateItemsView();
            ApplySortToCurrentView();
        }

        private Style BuildDetailsContainerStyle(EmoteVisualizerTableViewConfig table)
        {
            Style baseStyle = (Style)FindResource("VisualizerDetailsItemStyle");
            Style style = new Style(typeof(ListViewItem), baseStyle);
            style.Setters.Add(new Setter(FontSizeProperty, table.FontSize));
            style.Setters.Add(new Setter(HeightProperty, table.RowHeight));
            return style;
        }

        private GridView BuildDetailsGridView(EmoteVisualizerViewPreset preset)
        {
            EmoteVisualizerTableViewConfig table = preset.Table;
            UntrackTableColumnWidth();
            tableColumnMap.Clear();

            GridView gridView = new GridView
            {
                AllowsColumnReorder = false,
                ColumnHeaderContainerStyle = (Style)FindResource("VisualizerGridHeaderStyle")
            };

            List<EmoteVisualizerTableColumnConfig> orderedColumns = table.Columns
                .Where(column => column.IsVisible)
                .OrderBy(column => column.Order)
                .ToList();

            if (orderedColumns.Count == 0)
            {
                orderedColumns.Add(new EmoteVisualizerTableColumnConfig
                {
                    Key = EmoteVisualizerTableColumnKey.Name,
                    IsVisible = true,
                    Order = 0,
                    Width = 220
                });
            }

            foreach (EmoteVisualizerTableColumnConfig column in orderedColumns)
            {
                GridViewColumn gridColumn = new GridViewColumn
                {
                    Header = CreateHeaderInfo(column.Key),
                    Width = column.Width
                };

                if (column.Key == EmoteVisualizerTableColumnKey.Icon)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsIconTemplate");
                }
                else if (column.Key == EmoteVisualizerTableColumnKey.PreAnimationPreview)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsPrePreviewTemplate");
                }
                else if (column.Key == EmoteVisualizerTableColumnKey.AnimationPreview)
                {
                    gridColumn.CellTemplate = (DataTemplate)FindResource("DetailsAnimationPreviewTemplate");
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

        private EmoteVisualizerGridHeaderInfo CreateHeaderInfo(EmoteVisualizerTableColumnKey key)
        {
            string baseText = GetColumnHeader(key);
            bool sortable = IsColumnSortable(key);

            if (sortable && currentSortColumnKey == key)
            {
                string glyph = currentSortDirection == ListSortDirection.Ascending ? " ▲" : " ▼";
                return new EmoteVisualizerGridHeaderInfo(key, baseText + glyph, true);
            }

            return new EmoteVisualizerGridHeaderInfo(key, baseText, sortable);
        }

        private static string GetColumnHeader(EmoteVisualizerTableColumnKey key)
        {
            return key switch
            {
                EmoteVisualizerTableColumnKey.Icon => string.Empty,
                EmoteVisualizerTableColumnKey.Id => "ID",
                EmoteVisualizerTableColumnKey.Name => "Name",
                EmoteVisualizerTableColumnKey.IconDimensions => "Icon Dimensions",
                EmoteVisualizerTableColumnKey.PreAnimationPreview => "Pre",
                EmoteVisualizerTableColumnKey.PreAnimationDimensions => "Pre Dims",
                EmoteVisualizerTableColumnKey.AnimationPreview => "Final",
                EmoteVisualizerTableColumnKey.AnimationDimensions => "Final Dims",
                EmoteVisualizerTableColumnKey.PreAnimationPath => "Pre Path",
                EmoteVisualizerTableColumnKey.AnimationPath => "Animation Path",
                _ => "Column"
            };
        }

        private static bool IsColumnSortable(EmoteVisualizerTableColumnKey key)
        {
            return key != EmoteVisualizerTableColumnKey.Icon
                && key != EmoteVisualizerTableColumnKey.PreAnimationPreview
                && key != EmoteVisualizerTableColumnKey.AnimationPreview;
        }

        private static string GetColumnBindingPath(EmoteVisualizerTableColumnKey key)
        {
            return key switch
            {
                EmoteVisualizerTableColumnKey.Id => nameof(EmoteVisualizerItem.IdText),
                EmoteVisualizerTableColumnKey.Name => nameof(EmoteVisualizerItem.NameWithId),
                EmoteVisualizerTableColumnKey.IconDimensions => nameof(EmoteVisualizerItem.IconDimensions),
                EmoteVisualizerTableColumnKey.PreAnimationDimensions => nameof(EmoteVisualizerItem.PreAnimationDimensions),
                EmoteVisualizerTableColumnKey.AnimationDimensions => nameof(EmoteVisualizerItem.AnimationDimensions),
                EmoteVisualizerTableColumnKey.PreAnimationPath => nameof(EmoteVisualizerItem.PreAnimationPath),
                EmoteVisualizerTableColumnKey.AnimationPath => nameof(EmoteVisualizerItem.AnimationPath),
                _ => nameof(EmoteVisualizerItem.NameWithId)
            };
        }

        private void GridHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader header || header.Column == null)
            {
                return;
            }

            if (header.Content is not EmoteVisualizerGridHeaderInfo info || !info.IsSortable)
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

            if (header.Content is not EmoteVisualizerGridHeaderInfo info)
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

        private void BestFitColumn(GridViewColumn column, EmoteVisualizerTableColumnKey key, string headerText)
        {
            if (!tableColumnMap.ContainsKey(column))
            {
                return;
            }

            double width = EstimateTextWidth(headerText) + 26;

            if (key == EmoteVisualizerTableColumnKey.Icon)
            {
                width = 34;
            }
            else if (key == EmoteVisualizerTableColumnKey.PreAnimationPreview
                || key == EmoteVisualizerTableColumnKey.AnimationPreview)
            {
                width = 110;
            }
            else
            {
                foreach (EmoteVisualizerItem item in allItems)
                {
                    string text = key switch
                    {
                        EmoteVisualizerTableColumnKey.Id => item.IdText,
                        EmoteVisualizerTableColumnKey.Name => item.NameWithId,
                        EmoteVisualizerTableColumnKey.IconDimensions => item.IconDimensions,
                        EmoteVisualizerTableColumnKey.PreAnimationDimensions => item.PreAnimationDimensions,
                        EmoteVisualizerTableColumnKey.AnimationDimensions => item.AnimationDimensions,
                        EmoteVisualizerTableColumnKey.PreAnimationPath => item.PreAnimationPath,
                        EmoteVisualizerTableColumnKey.AnimationPath => item.AnimationPath,
                        _ => item.NameWithId
                    };

                    width = Math.Max(width, EstimateTextWidth(text) + 20);
                }
            }

            column.Width = Math.Clamp(width, 40, 1000);
        }

        private double EstimateTextWidth(string text)
        {
            string safe = text ?? string.Empty;
            return (safe.Length * Math.Max(7.0, EmoteListView.FontSize * 0.58)) + 8;
        }

        private void ApplySortToCurrentView()
        {
            UpdateSortHeaderGlyphs();

            if (EmoteListView.View == null || currentSortColumnKey == null)
            {
                return;
            }

            string sortProperty = GetSortProperty(currentSortColumnKey.Value);
            if (string.IsNullOrWhiteSpace(sortProperty))
            {
                return;
            }

            ICollectionView view = CollectionViewSource.GetDefaultView(EmoteListView.ItemsSource);
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
            if (EmoteListView.View is not GridView gridView)
            {
                return;
            }

            foreach (GridViewColumn column in gridView.Columns)
            {
                if (column.Header is not EmoteVisualizerGridHeaderInfo info)
                {
                    continue;
                }

                column.Header = CreateHeaderInfo(info.Key);
            }
        }

        private static string GetSortProperty(EmoteVisualizerTableColumnKey key)
        {
            return key switch
            {
                EmoteVisualizerTableColumnKey.Id => nameof(EmoteVisualizerItem.Id),
                EmoteVisualizerTableColumnKey.Name => nameof(EmoteVisualizerItem.NameWithId),
                EmoteVisualizerTableColumnKey.IconDimensions => nameof(EmoteVisualizerItem.IconDimensions),
                EmoteVisualizerTableColumnKey.PreAnimationDimensions => nameof(EmoteVisualizerItem.PreAnimationDimensions),
                EmoteVisualizerTableColumnKey.AnimationDimensions => nameof(EmoteVisualizerItem.AnimationDimensions),
                EmoteVisualizerTableColumnKey.PreAnimationPath => nameof(EmoteVisualizerItem.PreAnimationPath),
                EmoteVisualizerTableColumnKey.AnimationPath => nameof(EmoteVisualizerItem.AnimationPath),
                _ => string.Empty
            };
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
            if (obj is not EmoteVisualizerItem item)
            {
                return false;
            }

            string query = searchText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return item.NameWithId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.IdText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.PreAnimationPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.AnimationPath.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = SearchTextBox.Text ?? string.Empty;
            ICollectionView view = GetOrCreateItemsView();
            view.Refresh();
        }

        private void TrackColumnWidth(GridViewColumn column, EmoteVisualizerTableColumnConfig config)
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

        private static string ResolveEmoteIconPath(Emote emote, CharacterFolder sourceCharacter)
        {
            if (!string.IsNullOrWhiteSpace(emote.PathToImage_off))
            {
                return emote.PathToImage_off;
            }

            if (!string.IsNullOrWhiteSpace(emote.PathToImage_on))
            {
                return emote.PathToImage_on;
            }

            string fallbackFramePath = ResolveEmoteFramePath(sourceCharacter.DirectoryPath ?? string.Empty, emote.Animation);
            if (!string.IsNullOrWhiteSpace(fallbackFramePath))
            {
                return fallbackFramePath;
            }

            return sourceCharacter.CharIconPath ?? string.Empty;
        }

        private static string ResolveEmoteFramePath(string characterDirectory, string animationName)
        {
            string normalizedAnimationName = animationName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedAnimationName) || normalizedAnimationName == "-")
            {
                return string.Empty;
            }

            return CharacterAssetPathResolver.ResolveCharacterAnimationPath(characterDirectory, normalizedAnimationName);
        }

        private ImageSource LoadImage(string path, int decodePixelWidth)
        {
            string normalizedPath = path?.Trim() ?? string.Empty;
            string cacheKey = decodePixelWidth.ToString() + "|" + normalizedPath;

            if (imageCache.TryGetValue(cacheKey, out ImageSource? cachedImage))
            {
                return cachedImage;
            }

            ImageSource loadedImage = FallbackImage;
            try
            {
                if (!string.IsNullOrWhiteSpace(normalizedPath) && File.Exists(normalizedPath))
                {
                    string extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.UriSource = new Uri(normalizedPath, UriKind.Absolute);

                    if (extension == ".gif")
                    {
                        // Keep GIF as a streaming bitmap to allow animation playback in WPF image controls.
                        bitmapImage.CacheOption = BitmapCacheOption.Default;
                    }
                    else
                    {
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        if (decodePixelWidth > 0)
                        {
                            bitmapImage.DecodePixelWidth = decodePixelWidth;
                        }
                    }

                    bitmapImage.EndInit();
                    if (extension != ".gif")
                    {
                        bitmapImage.Freeze();
                    }
                    loadedImage = bitmapImage;
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Unable to load emote visualizer image '{normalizedPath}'.", ex);
            }

            imageCache[cacheKey] = loadedImage;
            return loadedImage;
        }

        private (ImageSource image, IAnimationPlayer? player) LoadPreviewImage(string path, int decodePixelWidth)
        {
            string normalizedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
            {
                return (FallbackImage, null);
            }

            string extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (extension == ".gif")
            {
                try
                {
                    GifAnimationPlayer gifPlayer = new GifAnimationPlayer(normalizedPath, loopAnimations);
                    animationPlayers.Add(gifPlayer);
                    return (gifPlayer.CurrentFrame, gifPlayer);
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning($"Unable to initialize gif player for '{normalizedPath}'.", ex);
                    return (LoadImage(normalizedPath, decodePixelWidth), null);
                }
            }

            if (BitmapFrameAnimationPlayer.TryCreate(normalizedPath, loopAnimations, out BitmapFrameAnimationPlayer? genericPlayer))
            {
                if (genericPlayer != null)
                {
                    animationPlayers.Add(genericPlayer);
                    return (genericPlayer.CurrentFrame, genericPlayer);
                }
            }
            
            return (LoadImage(normalizedPath, decodePixelWidth), null);
        }

        private void StopAndClearAnimationPlayers()
        {
            foreach (IAnimationPlayer player in animationPlayers)
            {
                player.Stop();
            }

            animationPlayers.Clear();
        }

        private void ApplyLoopAnimationsSetting(bool loop)
        {
            loopAnimations = loop;
            foreach (EmoteVisualizerItem item in allItems)
            {
                item.PreAnimationPlayer?.SetLoop(loopAnimations);
                item.AnimationPlayer?.SetLoop(loopAnimations);
            }

            SaveFile.Data.LoopEmoteVisualizerAnimations = loopAnimations;
            SaveFile.Save();
        }

        private void LoopAnimationsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool shouldLoop = LoopAnimationsCheckBox.IsChecked == true;
            ApplyLoopAnimationsSetting(shouldLoop);
        }

        private void EmoteListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmoteListView.SelectedItem is not EmoteVisualizerItem item)
            {
                return;
            }

            item.PreAnimationPlayer?.Restart();
            item.AnimationPlayer?.Restart();
        }

        private void EmoteListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (EmoteListView.SelectedItem is not EmoteVisualizerItem item)
            {
                return;
            }

            item.PreAnimationPlayer?.Restart();
            item.AnimationPlayer?.Restart();
        }

        private void EmoteListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            contextMenuTargetItem = ResolveItemFromOriginalSource(e.OriginalSource as DependencyObject);
            if (contextMenuTargetItem != null)
            {
                EmoteListView.SelectedItem = contextMenuTargetItem;
                EmoteListView.ContextMenu = BuildContextMenuForItem(contextMenuTargetItem);
                EmoteListView.ContextMenu.PlacementTarget = EmoteListView;
                EmoteListView.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void EmoteListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            DependencyObject? originalSource = e.OriginalSource as DependencyObject ?? Mouse.DirectlyOver as DependencyObject;
            EmoteVisualizerItem? target = contextMenuTargetItem
                ?? ResolveItemFromOriginalSource(originalSource)
                ?? EmoteListView.SelectedItem as EmoteVisualizerItem;
            if (target == null)
            {
                e.Handled = true;
                return;
            }

            EmoteListView.ContextMenu = BuildContextMenuForItem(target);
        }

        private void EmoteListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (EmoteListView.View != null)
            {
                return;
            }

            ScrollViewer? scrollViewer = FindDescendant<ScrollViewer>(EmoteListView);
            if (scrollViewer == null)
            {
                return;
            }

            double delta = e.Delta > 0 ? -normalScrollWheelStep : normalScrollWheelStep;
            double targetOffset = Math.Clamp(scrollViewer.VerticalOffset + delta, 0, scrollViewer.ScrollableHeight);
            scrollViewer.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        }

        private void UpdateTopButtonsAvailability()
        {
            string charIniPath = character.PathToConfigIni?.Trim() ?? string.Empty;
            OpenCharacterIniButton.IsEnabled = File.Exists(charIniPath);

            string readmePath = ResolveCharacterReadmePath(character.DirectoryPath ?? string.Empty);
            OpenReadmeButton.IsEnabled = !string.IsNullOrWhiteSpace(readmePath) && File.Exists(readmePath);

            bool hasAnimatedAssets = allItems.Any(item =>
                item.PreAnimationPlayer != null
                || item.AnimationPlayer != null
                || IsPotentialAnimatedPath(item.PreAnimationPath)
                || IsPotentialAnimatedPath(item.AnimationPath));
            LoopAnimationsCheckBox.Visibility = hasAnimatedAssets ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsPotentialAnimatedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".gif" || extension == ".apng" || extension == ".webp";
        }

        private EmoteVisualizerItem? ResolveItemFromOriginalSource(DependencyObject? source)
        {
            if (source != null)
            {
                ListViewItem? container = ItemsControl.ContainerFromElement(EmoteListView, source) as ListViewItem;
                if (container?.DataContext is EmoteVisualizerItem directItem)
                {
                    return directItem;
                }
            }

            DependencyObject? current = source;
            while (current != null)
            {
                if (current is FrameworkElement element && element.DataContext is EmoteVisualizerItem item)
                {
                    return item;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
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

        private ContextMenu BuildContextMenuForItem(EmoteVisualizerItem item)
        {
            ContextMenu menu = new ContextMenu();

            AddContextCategoryHeader(menu, "Character", addLeadingSeparator: false);

            MenuItem refreshCharacterItem = new MenuItem
            {
                Header = "Refresh Character",
                IsEnabled = RefreshCharacterButton.IsEnabled
            };
            refreshCharacterItem.Click += (_, _) => RefreshCharacterButton_Click(RefreshCharacterButton, new RoutedEventArgs(Button.ClickEvent));
            menu.Items.Add(refreshCharacterItem);

            MenuItem openCharIniItem = new MenuItem
            {
                Header = "Open char.ini",
                IsEnabled = OpenCharacterIniButton.IsEnabled
            };
            openCharIniItem.Click += (_, _) => OpenCharacterIniButton_Click(OpenCharacterIniButton, new RoutedEventArgs(Button.ClickEvent));
            menu.Items.Add(openCharIniItem);

            MenuItem openReadmeItem = new MenuItem
            {
                Header = "Open readme",
                IsEnabled = OpenReadmeButton.IsEnabled
            };
            openReadmeItem.Click += (_, _) => OpenReadmeButton_Click(OpenReadmeButton, new RoutedEventArgs(Button.ClickEvent));
            menu.Items.Add(openReadmeItem);

            MenuItem openInExplorerItem = new MenuItem
            {
                Header = "Open in explorer",
                IsEnabled = Directory.Exists(character.DirectoryPath ?? string.Empty)
            };
            openInExplorerItem.Click += (_, _) => ShowInExplorer(character.DirectoryPath ?? string.Empty);
            menu.Items.Add(openInExplorerItem);

            AddContextCategoryHeader(menu, "Emote", addLeadingSeparator: true);

            MenuItem setAsFolderDisplayItem = new MenuItem
            {
                Header = "Set as folder display",
                IsCheckable = true,
                IsChecked = IsFolderDisplayOverride(item),
                IsEnabled = item.Id > 0
            };
            setAsFolderDisplayItem.Click += (_, _) =>
            {
                ApplyFolderDisplayOverride(item, setAsFolderDisplayItem.IsChecked);
            };
            menu.Items.Add(setAsFolderDisplayItem);

            return menu;
        }

        private static void AddContextCategoryHeader(ContextMenu menu, string text, bool addLeadingSeparator)
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

        private bool IsFolderDisplayOverride(EmoteVisualizerItem item)
        {
            string key = CharacterFolderVisualizerWindow.NormalizeFolderOverrideKey(character.DirectoryPath);
            return !string.IsNullOrWhiteSpace(key)
                && SaveFile.Data.CharacterFolderPreviewEmoteOverrides.TryGetValue(key, out int overrideId)
                && overrideId == item.Id;
        }

        private void ApplyFolderDisplayOverride(EmoteVisualizerItem item, bool shouldSetOverride)
        {
            string key = CharacterFolderVisualizerWindow.NormalizeFolderOverrideKey(character.DirectoryPath);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (shouldSetOverride)
            {
                SaveFile.Data.CharacterFolderPreviewEmoteOverrides[key] = item.Id;
            }
            else if (SaveFile.Data.CharacterFolderPreviewEmoteOverrides.TryGetValue(key, out int currentId)
                && currentId == item.Id)
            {
                SaveFile.Data.CharacterFolderPreviewEmoteOverrides.Remove(key);
            }

            SaveFile.Save();
            CharacterFolderVisualizerWindow.InvalidateCachedItems();
            if (Owner is CharacterFolderVisualizerWindow folderWindow)
            {
                _ = folderWindow.ApplyPreviewOverrideForCharacterDirectoryAsync(character.DirectoryPath);
            }
        }

        private void OpenCharacterIniButton_Click(object sender, RoutedEventArgs e)
        {
            string charIniPath = character.PathToConfigIni?.Trim() ?? string.Empty;
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
            string readmePath = ResolveCharacterReadmePath(character.DirectoryPath ?? string.Empty);
            if (!TryOpenPath(readmePath))
            {
                OceanyaMessageBox.Show(this,
                    "Readme was not found for this character.",
                    "Open Readme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private bool TryOpenPath(string path)
        {
            string safePath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safePath) || !File.Exists(safePath))
            {
                return false;
            }

            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = safePath,
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to open path '{safePath}'.", ex);
                return false;
            }
        }

        private void ShowInExplorer(string directoryPath)
        {
            string safePath = directoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safePath) || !Directory.Exists(safePath))
            {
                return;
            }

            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{safePath}\"",
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                CustomConsole.Error($"Failed to open explorer at '{safePath}'.", ex);
            }
        }

        private static string GetImageDimensionsText(string path)
        {
            string normalizedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
            {
                return "-";
            }

            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(normalizedPath, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.None);

                BitmapFrame? firstFrame = decoder.Frames.FirstOrDefault();
                if (firstFrame != null && firstFrame.PixelWidth > 0 && firstFrame.PixelHeight > 0)
                {
                    return firstFrame.PixelWidth + "x" + firstFrame.PixelHeight;
                }
            }
            catch
            {
                // ignored
            }

            return "-";
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
                    string fileName = Path.GetFileName(textFile).ToLowerInvariant();
                    if (fileName == "char.txt" || fileName == "design.txt" || fileName == "soundlist.txt")
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

        private void ConfigureViewsButton_Click(object sender, RoutedEventArgs e)
        {
            CharacterEmoteVisualizerConfigWindow configureWindow =
                new CharacterEmoteVisualizerConfigWindow(CloneConfig(visualizerConfig))
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
            BindViewPresets();
        }

        private async void RefreshCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            string iniPath = character.PathToConfigIni?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
            {
                OceanyaMessageBox.Show(this,
                    "char.ini was not found for this character.",
                    "Refresh Character",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                character = EnsureCharacterLoaded(character);
                imageCache.Clear();
                await LoadEmoteItemsAsync();
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to refresh character emotes in visualizer.", ex);
                OceanyaMessageBox.Show(this,
                    "An error occurred while refreshing this character:\n" + ex.Message,
                    "Refresh Character",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void PersistVisualizerConfig()
        {
            SaveFile.Data.EmoteVisualizer = CloneConfig(visualizerConfig);
            SaveFile.Save();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAndClearAnimationPlayers();
            UntrackTableColumnWidth();
            PersistVisualizerConfig();
            if (!applyingSavedWindowState)
            {
                SaveFile.Data.EmoteVisualizerWindowState = CaptureWindowState();
                SaveFile.Save();
            }
            base.OnClosed(e);
        }

        private static ImageSource CreateTransparentPlaceholderImage()
        {
            const int width = 2;
            const int height = 2;
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[width * height * 4];
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }

        internal static EmoteVisualizerConfig CloneConfig(EmoteVisualizerConfig source)
        {
            EmoteVisualizerConfig clone = new EmoteVisualizerConfig
            {
                SelectedPresetId = source?.SelectedPresetId ?? string.Empty,
                SelectedPresetName = source?.SelectedPresetName ?? string.Empty,
                Presets = new List<EmoteVisualizerViewPreset>()
            };

            if (source?.Presets == null)
            {
                return clone;
            }

            foreach (EmoteVisualizerViewPreset preset in source.Presets)
            {
                clone.Presets.Add(ClonePreset(preset));
            }

            return clone;
        }

        internal static EmoteVisualizerViewPreset ClonePreset(EmoteVisualizerViewPreset preset)
        {
            EmoteVisualizerViewPreset clone = new EmoteVisualizerViewPreset
            {
                Id = preset?.Id ?? Guid.NewGuid().ToString("N"),
                Name = preset?.Name ?? "View",
                Mode = preset?.Mode ?? FolderVisualizerLayoutMode.Normal,
                Normal = new FolderVisualizerNormalViewConfig
                {
                    TileWidth = preset?.Normal?.TileWidth ?? 235,
                    TileHeight = preset?.Normal?.TileHeight ?? 210,
                    IconSize = preset?.Normal?.IconSize ?? 18,
                    NameFontSize = preset?.Normal?.NameFontSize ?? 12,
                    InternalTilePadding = preset?.Normal?.InternalTilePadding ?? 0,
                    ScrollWheelStep = preset?.Normal?.ScrollWheelStep ?? 90,
                    TilePadding = preset?.Normal?.TilePadding ?? 8,
                    TileMargin = preset?.Normal?.TileMargin ?? 4
                },
                Table = new EmoteVisualizerTableViewConfig
                {
                    RowHeight = preset?.Table?.RowHeight ?? 58,
                    FontSize = preset?.Table?.FontSize ?? 13,
                    Columns = new List<EmoteVisualizerTableColumnConfig>()
                }
            };

            List<EmoteVisualizerTableColumnConfig>? columns = preset?.Table?.Columns;
            if (columns != null)
            {
                foreach (EmoteVisualizerTableColumnConfig column in columns)
                {
                    clone.Table.Columns.Add(new EmoteVisualizerTableColumnConfig
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

    public sealed class EmoteVisualizerGridHeaderInfo
    {
        public EmoteVisualizerTableColumnKey Key { get; }
        public string Text { get; }
        public bool IsSortable { get; }

        public EmoteVisualizerGridHeaderInfo(EmoteVisualizerTableColumnKey key, string text, bool isSortable)
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

    public sealed class EmoteVisualizerItem : INotifyPropertyChanged
    {
        private ImageSource preAnimationImage = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");
        private ImageSource animationImage = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");

        public int Id { get; set; }
        public string IdText { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string NameWithId { get; set; } = string.Empty;
        public bool HasPreAnimation { get; set; }
        public string IconPath { get; set; } = string.Empty;
        public string IconDimensions { get; set; } = "-";
        public string PreAnimationPath { get; set; } = string.Empty;
        public string PreAnimationDimensions { get; set; } = "-";
        public string AnimationPath { get; set; } = string.Empty;
        public string AnimationDimensions { get; set; } = "-";
        public ImageSource IconImage { get; set; } = CharacterFolderVisualizerWindow.LoadEmbeddedImage(
            "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");
        public ImageSource PreAnimationImage
        {
            get => preAnimationImage;
            set
            {
                if (ReferenceEquals(preAnimationImage, value))
                {
                    return;
                }

                preAnimationImage = value;
                OnPropertyChanged();
            }
        }

        public ImageSource AnimationImage
        {
            get => animationImage;
            set
            {
                if (ReferenceEquals(animationImage, value))
                {
                    return;
                }

                animationImage = value;
                OnPropertyChanged();
            }
        }
        public IAnimationPlayer? PreAnimationPlayer { get; set; }
        public IAnimationPlayer? AnimationPlayer { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public interface IAnimationPlayer
    {
        event Action<ImageSource>? FrameChanged;
        ImageSource CurrentFrame { get; }
        void SetLoop(bool shouldLoop);
        void Restart();
        void Stop();
    }

    public sealed class BitmapFrameAnimationPlayer : IAnimationPlayer
    {
        private static readonly object SharedTimerLock = new object();
        private static readonly List<BitmapFrameAnimationPlayer> ActivePlayers = new List<BitmapFrameAnimationPlayer>();
        private static DispatcherTimer? sharedTimer;
        private static readonly TimeSpan SharedTickInterval = TimeSpan.FromMilliseconds(15);

        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly List<TimeSpan> frameDurations = new List<TimeSpan>();
        private int frameIndex;
        private bool loop;
        private bool endedWithoutLoop;
        private bool isRunning;
        private DateTime nextFrameAtUtc;

        public event Action<ImageSource>? FrameChanged;

        public ImageSource CurrentFrame => frames.Count > 0
            ? frames[Math.Clamp(frameIndex, 0, frames.Count - 1)]
            : CharacterFolderVisualizerWindow.LoadEmbeddedImage(
                "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");

        private BitmapFrameAnimationPlayer(bool loop)
        {
            this.loop = loop;
        }

        public static bool TryCreate(string path, bool loop, out BitmapFrameAnimationPlayer? player)
        {
            player = null;
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count <= 1)
                {
                    return false;
                }

                BitmapFrameAnimationPlayer candidate = new BitmapFrameAnimationPlayer(loop);
                foreach (BitmapFrame frame in decoder.Frames)
                {
                    BitmapSource source = frame;
                    if (source.CanFreeze)
                    {
                        source.Freeze();
                    }

                    candidate.frames.Add(source);
                    candidate.frameDurations.Add(ReadDelay(frame.Metadata));
                }

                candidate.frameIndex = 0;
                candidate.StartPlayback();
                player = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SetLoop(bool shouldLoop)
        {
            loop = shouldLoop;
            if (loop && endedWithoutLoop)
            {
                endedWithoutLoop = false;
                frameIndex = 0;
                RaiseFrameChanged();
                StartPlayback();
            }
        }

        public void Restart()
        {
            if (frames.Count == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            frameIndex = 0;
            RaiseFrameChanged();
            StartPlayback();
        }

        public void Stop()
        {
            StopPlayback();
            FrameChanged = null;
        }

        private void StartPlayback()
        {
            if (frames.Count == 0 || frameDurations.Count == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            isRunning = true;
            nextFrameAtUtc = DateTime.UtcNow + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            RegisterActivePlayer(this);
        }

        private void StopPlayback()
        {
            isRunning = false;
            UnregisterActivePlayer(this);
        }

        private void Tick(DateTime nowUtc)
        {
            if (!isRunning || frames.Count == 0 || frameDurations.Count == 0 || nowUtc < nextFrameAtUtc)
            {
                return;
            }

            while (isRunning && nowUtc >= nextFrameAtUtc)
            {
                int nextIndex = frameIndex + 1;
                if (nextIndex >= frames.Count)
                {
                    if (!loop)
                    {
                        endedWithoutLoop = true;
                        StopPlayback();
                        return;
                    }

                    nextIndex = 0;
                }

                frameIndex = nextIndex;
                RaiseFrameChanged();
                nextFrameAtUtc = nowUtc + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            }
        }

        private void RaiseFrameChanged()
        {
            FrameChanged?.Invoke(frames[frameIndex]);
        }

        private static void RegisterActivePlayer(BitmapFrameAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                if (!ActivePlayers.Contains(player))
                {
                    ActivePlayers.Add(player);
                }

                EnsureSharedTimer();
                sharedTimer?.Start();
            }
        }

        private static void UnregisterActivePlayer(BitmapFrameAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                ActivePlayers.Remove(player);
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                }
            }
        }

        private static void EnsureSharedTimer()
        {
            if (sharedTimer != null)
            {
                return;
            }

            sharedTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SharedTickInterval
            };
            sharedTimer.Tick += SharedTimer_Tick;
        }

        private static void SharedTimer_Tick(object? sender, EventArgs e)
        {
            BitmapFrameAnimationPlayer[] snapshot;
            lock (SharedTimerLock)
            {
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                    return;
                }

                snapshot = ActivePlayers.ToArray();
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (BitmapFrameAnimationPlayer player in snapshot)
            {
                player.Tick(nowUtc);
            }
        }

        private static TimeSpan ReadDelay(ImageMetadata? metadata)
        {
            try
            {
                if (metadata is BitmapMetadata bitmapMetadata && bitmapMetadata.ContainsQuery("/grctlext/Delay"))
                {
                    object query = bitmapMetadata.GetQuery("/grctlext/Delay");
                    if (query is ushort delayValue)
                    {
                        double milliseconds = Math.Max(20, delayValue * 10d);
                        return TimeSpan.FromMilliseconds(milliseconds);
                    }
                }
            }
            catch
            {
                // ignored
            }

            return TimeSpan.FromMilliseconds(90);
        }
    }

    public sealed class GifAnimationPlayer : IAnimationPlayer
    {
        private static readonly object SharedTimerLock = new object();
        private static readonly List<GifAnimationPlayer> ActivePlayers = new List<GifAnimationPlayer>();
        private static DispatcherTimer? sharedTimer;
        private static readonly TimeSpan SharedTickInterval = TimeSpan.FromMilliseconds(15);

        private readonly DrawingImage gifImage;
        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly List<TimeSpan> frameDurations = new List<TimeSpan>();
        private readonly int frameCount;
        private int frameIndex;
        private bool loop;
        private bool endedWithoutLoop;
        private bool isRunning;
        private DateTime nextFrameAtUtc;

        public event Action<ImageSource>? FrameChanged;

        public ImageSource CurrentFrame => frameCount > 0
            ? frames[Math.Clamp(frameIndex, 0, frameCount - 1)]
            : CharacterFolderVisualizerWindow.LoadEmbeddedImage(
                "pack://application:,,,/OceanyaClient;component/Resources/Buttons/smallFolder.png");

        public GifAnimationPlayer(string gifPath, bool loop)
        {
            this.loop = loop;
            gifImage = DrawingImage.FromFile(gifPath);
            frameCount = gifImage.GetFrameCount(DrawingFrameDimension.Time);

            if (frameCount <= 0)
            {
                throw new InvalidOperationException("Gif has no decodable frames.");
            }

            frameDurations.AddRange(ReadFrameDelays(gifImage, frameCount));
            CacheFrames();
            frameIndex = 0;
            StartPlayback();
        }

        public void SetLoop(bool shouldLoop)
        {
            loop = shouldLoop;
            if (loop && endedWithoutLoop)
            {
                endedWithoutLoop = false;
                frameIndex = 0;
                RaiseFrameChanged();
                StartPlayback();
            }
        }

        public void Restart()
        {
            if (frameCount == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            frameIndex = 0;
            RaiseFrameChanged();
            StartPlayback();
        }

        public void Stop()
        {
            StopPlayback();
            gifImage.Dispose();
            FrameChanged = null;
        }

        private void StartPlayback()
        {
            if (frameCount == 0 || frameDurations.Count == 0)
            {
                return;
            }

            endedWithoutLoop = false;
            isRunning = true;
            nextFrameAtUtc = DateTime.UtcNow + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            RegisterActivePlayer(this);
        }

        private void StopPlayback()
        {
            isRunning = false;
            UnregisterActivePlayer(this);
        }

        private void Tick(DateTime nowUtc)
        {
            if (!isRunning || frameCount == 0 || frameDurations.Count == 0 || nowUtc < nextFrameAtUtc)
            {
                return;
            }

            while (isRunning && nowUtc >= nextFrameAtUtc)
            {
                int nextIndex = frameIndex + 1;
                if (nextIndex >= frameCount)
                {
                    if (!loop)
                    {
                        endedWithoutLoop = true;
                        StopPlayback();
                        return;
                    }

                    nextIndex = 0;
                }

                frameIndex = nextIndex;
                RaiseFrameChanged();
                nextFrameAtUtc = nowUtc + frameDurations[Math.Clamp(frameIndex, 0, frameDurations.Count - 1)];
            }
        }

        private void RaiseFrameChanged()
        {
            FrameChanged?.Invoke(frames[Math.Clamp(frameIndex, 0, frameCount - 1)]);
        }

        private void CacheFrames()
        {
            for (int i = 0; i < frameCount; i++)
            {
                gifImage.SelectActiveFrame(DrawingFrameDimension.Time, i);
                using DrawingBitmap bitmap = new DrawingBitmap(gifImage.Width, gifImage.Height, DrawingPixelFormat.Format32bppArgb);
                using (DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap))
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
                    source.Freeze();
                    frames.Add(source);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        private static void RegisterActivePlayer(GifAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                if (!ActivePlayers.Contains(player))
                {
                    ActivePlayers.Add(player);
                }

                EnsureSharedTimer();
                sharedTimer?.Start();
            }
        }

        private static void UnregisterActivePlayer(GifAnimationPlayer player)
        {
            lock (SharedTimerLock)
            {
                ActivePlayers.Remove(player);
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                }
            }
        }

        private static void EnsureSharedTimer()
        {
            if (sharedTimer != null)
            {
                return;
            }

            sharedTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = SharedTickInterval
            };
            sharedTimer.Tick += SharedTimer_Tick;
        }

        private static void SharedTimer_Tick(object? sender, EventArgs e)
        {
            GifAnimationPlayer[] snapshot;
            lock (SharedTimerLock)
            {
                if (ActivePlayers.Count == 0)
                {
                    sharedTimer?.Stop();
                    return;
                }

                snapshot = ActivePlayers.ToArray();
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (GifAnimationPlayer player in snapshot)
            {
                player.Tick(nowUtc);
            }
        }

        private static List<TimeSpan> ReadFrameDelays(DrawingImage image, int frameCount)
        {
            List<TimeSpan> delays = new List<TimeSpan>(frameCount);
            const int PropertyTagFrameDelay = 0x5100;
            try
            {
                System.Drawing.Imaging.PropertyItem? property = null;
                foreach (System.Drawing.Imaging.PropertyItem candidate in image.PropertyItems)
                {
                    if (candidate.Id == PropertyTagFrameDelay)
                    {
                        property = candidate;
                        break;
                    }
                }

                if (property?.Value != null && property.Value.Length >= frameCount * 4)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        int delayUnits = BitConverter.ToInt32(property.Value, i * 4);
                        double milliseconds = Math.Max(20, delayUnits * 10d);
                        delays.Add(TimeSpan.FromMilliseconds(milliseconds));
                    }
                }
            }
            catch
            {
                // ignored
            }

            while (delays.Count < frameCount)
            {
                delays.Add(TimeSpan.FromMilliseconds(90));
            }

            return delays;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EmoteVisualizerPoint
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EmoteVisualizerMinMaxInfo
    {
        public EmoteVisualizerPoint ptReserved;
        public EmoteVisualizerPoint ptMaxSize;
        public EmoteVisualizerPoint ptMaxPosition;
        public EmoteVisualizerPoint ptMinTrackSize;
        public EmoteVisualizerPoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct EmoteVisualizerMonitorInfo
    {
        public int cbSize;
        public EmoteVisualizerRect rcMonitor;
        public EmoteVisualizerRect rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct EmoteVisualizerRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public partial class CharacterEmoteVisualizerWindow
    {
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref EmoteVisualizerMonitorInfo lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            EmoteVisualizerMonitorInfo monitorInfo = new EmoteVisualizerMonitorInfo();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            EmoteVisualizerMinMaxInfo mmi = Marshal.PtrToStructure<EmoteVisualizerMinMaxInfo>(lParam);
            EmoteVisualizerRect workArea = monitorInfo.rcWork;
            EmoteVisualizerRect monitorArea = monitorInfo.rcMonitor;

            mmi.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
            mmi.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
            mmi.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
            mmi.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);

            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }
}
