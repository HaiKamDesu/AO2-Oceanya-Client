using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OceanyaClient.Components
{
    /// <summary>
    /// Interaction logic for PageButtonGrid.xaml
    /// </summary>
    public partial class PageButtonGrid : UserControl
    {
        public enum ScrollMode { Horizontal, Vertical }

        private ScrollMode currentScrollMode = ScrollMode.Horizontal;
        private int rows = 2;
        private int columns = 10;
        private int currentPage = 0;
        private List<UIElement> elements = new();
        private List<object> virtualItems = new();
        private Func<object, UIElement>? virtualElementFactory;
        private double automaticItemWidth;
        private double automaticItemHeight;
        private double automaticSpacingX;
        private double automaticSpacingY;
        private bool pagingControlsExtracted;
        private bool pagingReservation = true;

        public PageButtonGrid()
        {
            InitializeComponent();
            SizeChanged += (_, _) => RefreshAutomaticPageSize();
        }

        /// <summary>
        /// Sizes the page from the available space instead of a fixed row/column count, so a resized
        /// panel shows as many fixed-size items as physically fit.
        /// </summary>
        /// <remarks>
        /// This is AO2's model (`AO2-Client/src/emotes.cpp`): the theme declares a button size and a
        /// spacing, and the widget fits `((area - button) / (spacing + button)) + 1` of them per axis.
        /// The items keep that exact size rather than being stretched, and the paging arrows stop
        /// reserving space inside the area, because in AO2 they are separate widgets of their own.
        /// </remarks>
        /// <param name="itemWidth">Width of one item.</param>
        /// <param name="itemHeight">Height of one item; zero means square.</param>
        /// <param name="spacingX">Horizontal gap between items.</param>
        /// <param name="spacingY">Vertical gap between items.</param>
        public void EnableAutomaticPageSize(double itemWidth, double itemHeight = 0, double spacingX = 0, double spacingY = 0)
        {
            automaticItemWidth = itemWidth > 0 ? itemWidth : 0;
            automaticItemHeight = itemHeight > 0 ? itemHeight : automaticItemWidth;
            automaticSpacingX = Math.Max(0, spacingX);
            automaticSpacingY = Math.Max(0, spacingY);
            RefreshAutomaticPageSize();
        }

        /// <summary>
        /// Hands the paging buttons over so they can be laid out as panels of their own.
        /// </summary>
        /// <remarks>
        /// AO2 themes position and skin the emote arrows independently of the emote area, so they have to
        /// be placeable panels here too. Reparenting keeps their click handlers and fields working; the
        /// grid only stops reserving space for them.
        /// </remarks>
        /// <returns>The previous/next buttons, in that order.</returns>
        public IReadOnlyList<Button> ExtractPagingControls()
        {
            List<Button> extracted = new List<Button> { LeftButton, RightButton, UpButton, DownButton };
            if (pagingControlsExtracted)
            {
                return extracted;
            }

            pagingControlsExtracted = true;

            // The buttons wear this control's implicit Button style, and an implicit style is looked up in
            // the ancestor chain - which they leave when they are reparented. Pinning the style locally
            // keeps their stock appearance exactly as it was inside the grid.
            Style? buttonStyle = TryFindResource(typeof(Button)) as Style;
            foreach (Button button in extracted)
            {
                if (buttonStyle != null && button.ReadLocalValue(StyleProperty) == DependencyProperty.UnsetValue)
                {
                    button.Style = buttonStyle;
                }

                LayoutGrid.Children.Remove(button);
            }

            return extracted;
        }

        /// <summary>
        /// Sets whether the grid keeps the arrow-sized inset the paging buttons used to occupy.
        /// </summary>
        /// <remarks>
        /// Kept by default so the stock layout is pixel-identical to 7.12, where the arrows sat inside the
        /// grid. An AO2 theme places its arrows itself and its `emotes` rectangle is nothing but buttons,
        /// so an import turns the reservation off.
        /// </remarks>
        /// <param name="reserve">True to keep the inset.</param>
        public void SetPagingReservation(bool reserve)
        {
            if (pagingReservation == reserve)
            {
                return;
            }

            pagingReservation = reserve;
            LayoutGrid.Margin = reserve ? new Thickness(5) : new Thickness(0);

            // Spanning the whole grid, rather than resizing its definitions: UpdateButtonVisibility
            // adds and removes rows and columns as the scroll mode changes, so index 1 is not reliably
            // the content cell - zeroing the wrong one made the item area 0px tall and the grid vanished.
            int lastColumn = Math.Max(0, LayoutGrid.ColumnDefinitions.Count - 1);
            int lastRow = Math.Max(0, LayoutGrid.RowDefinitions.Count - 1);
            Grid.SetColumn(GridArea, reserve ? Math.Min(1, lastColumn) : 0);
            Grid.SetColumnSpan(GridArea, reserve ? 1 : LayoutGrid.ColumnDefinitions.Count);
            Grid.SetRow(GridArea, reserve ? Math.Min(1, lastRow) : 0);
            Grid.SetRowSpan(GridArea, reserve ? 1 : LayoutGrid.RowDefinitions.Count);

            RefreshAutomaticPageSize();
        }

        /// <summary>
        /// Captures the paging configuration so it can be restored when a theme is reset.
        /// </summary>
        /// <returns>An opaque snapshot for <see cref="RestorePagingConfiguration"/>.</returns>
        public object CapturePagingConfiguration()
        {
            return new PagingConfiguration(
                automaticItemWidth,
                automaticItemHeight,
                automaticSpacingX,
                automaticSpacingY,
                rows,
                columns,
                pagingReservation);
        }

        /// <summary>
        /// Restores a configuration captured by <see cref="CapturePagingConfiguration"/>.
        /// </summary>
        /// <remarks>
        /// Paging is plain state rather than dependency properties, so the styling baseline cannot put it
        /// back on its own - without this, resetting a theme left the imported item size in place.
        /// </remarks>
        /// <param name="snapshot">Snapshot to restore.</param>
        public void RestorePagingConfiguration(object snapshot)
        {
            if (snapshot is not PagingConfiguration configuration)
            {
                return;
            }

            automaticItemWidth = configuration.ItemWidth;
            automaticItemHeight = configuration.ItemHeight;
            automaticSpacingX = configuration.SpacingX;
            automaticSpacingY = configuration.SpacingY;
            SetPagingReservation(configuration.ReservePaging);
            SetPageSize(configuration.Rows, configuration.Columns);
        }

        private sealed record PagingConfiguration(
            double ItemWidth,
            double ItemHeight,
            double SpacingX,
            double SpacingY,
            int Rows,
            int Columns,
            bool ReservePaging);

        /// <summary>
        /// Recomputes rows and columns from the current size when automatic paging is enabled.
        /// </summary>
        private void RefreshAutomaticPageSize()
        {
            if (automaticItemWidth <= 0)
            {
                return;
            }

            // The grid area excludes the paging buttons (30px each) and the 5px control margin, unless
            // those buttons have been handed over as panels of their own.
            double chrome = pagingReservation ? 10 : 0;
            double navigation = pagingReservation ? 60 : 0;
            double availableWidth = Math.Max(0, ActualWidth - chrome - (currentScrollMode == ScrollMode.Horizontal ? navigation : 0));
            double availableHeight = Math.Max(0, ActualHeight - chrome - (currentScrollMode == ScrollMode.Vertical ? navigation : 0));

            int resolvedColumns = ResolveFittingCount(availableWidth, automaticItemWidth, automaticSpacingX);
            int resolvedRows = ResolveFittingCount(availableHeight, automaticItemHeight, automaticSpacingY);

            if (resolvedRows == rows && resolvedColumns == columns)
            {
                return;
            }

            SetPageSize(resolvedRows, resolvedColumns);
        }

        /// <summary>
        /// Counts how many fixed-size items fit along one axis, the way AO2 does it.
        /// </summary>
        /// <param name="available">Space available along the axis.</param>
        /// <param name="itemLength">Size of one item.</param>
        /// <param name="spacing">Gap between items.</param>
        /// <returns>At least one item.</returns>
        private static int ResolveFittingCount(double available, double itemLength, double spacing)
        {
            if (itemLength <= 0 || available < itemLength)
            {
                return 1;
            }

            return Math.Max(1, (int)((available - itemLength) / (spacing + itemLength)) + 1);
        }


        public int GetCurrentPage() => currentPage;

        public int GetPageCount()
        {
            int elementsPerPage = Math.Max(1, rows * columns);
            return Math.Max(1, (int)Math.Ceiling((double)GetItemCount() / elementsPerPage));
        }

        public void SetCurrentPage(int page)
        {
            currentPage = Math.Clamp(page, 0, GetPageCount() - 1);
            UpdateGridContent();
        }

        public void SetPageSize(int rowCount, int columnCount)
        {
            rows = rowCount;
            columns = columnCount;
            UpdateGridSize();
            UpdateGridContent();
        }

        public void AddElement(UIElement element)
        {
            ClearVirtualizedItems();
            elements.Add(element);
            UpdateGridContent();
        }

        public void SetVirtualizedItems<T>(IEnumerable<T> items, Func<T, UIElement> elementFactory)
        {
            elements.Clear();
            virtualItems = items.Cast<object>().ToList();
            virtualElementFactory = item => elementFactory((T)item);
            currentPage = Math.Clamp(currentPage, 0, GetPageCount() - 1);
            UpdateGridContent();
        }

        public bool SetPageToVirtualizedItem(Predicate<object> predicate)
        {
            int index = virtualItems.FindIndex(predicate);
            if (index < 0)
            {
                return false;
            }

            currentPage = index / Math.Max(1, rows * columns);
            UpdateGridContent();
            return true;
        }

        public void SetPageToElement(UIElement element)
        {
            if(elements.Contains(element))
            {
                int index = elements.IndexOf(element);
                currentPage = index / (rows * columns);
                UpdateGridContent();
            }
            else
            {
                throw new Exception("Element not found in grid.");
            }
        }

        public bool MoveElement(UIElement element, int offset)
        {
            ClearVirtualizedItems();
            int index = elements.IndexOf(element);
            if (index < 0)
            {
                return false;
            }

            int targetIndex = index + offset;
            if (targetIndex < 0 || targetIndex >= elements.Count)
            {
                return false;
            }

            elements.RemoveAt(index);
            elements.Insert(targetIndex, element);
            currentPage = targetIndex / (rows * columns);
            UpdateGridContent();
            return true;
        }

        public void SetNavigationButtonColors(Brush background, Brush foreground)
        {
            UpButton.Background = background;
            UpButton.Foreground = foreground;

            DownButton.Background = background;
            DownButton.Foreground = foreground;

            LeftButton.Background = background;
            LeftButton.Foreground = foreground;

            RightButton.Background = background;
            RightButton.Foreground = foreground;
        }


        private void UpdateGridSize()
        {
            TestingGrid.RowDefinitions.Clear();
            TestingGrid.ColumnDefinitions.Clear();

            // Automatic sizing means the theme decided the item size, so the cells are that size and any
            // leftover space stays empty; the default keeps star sizing, which is the 7.12 look.
            bool fixedCells = automaticItemWidth > 0;
            for (int i = 0; i < rows; i++)
            {
                TestingGrid.RowDefinitions.Add(fixedCells
                    ? new RowDefinition { Height = new GridLength(automaticItemHeight + automaticSpacingY) }
                    : new RowDefinition());
            }

            for (int i = 0; i < columns; i++)
            {
                TestingGrid.ColumnDefinitions.Add(fixedCells
                    ? new ColumnDefinition { Width = new GridLength(automaticItemWidth + automaticSpacingX) }
                    : new ColumnDefinition());
            }
        }


        private void UpdateGridContent()
        {
            if (TestingGrid == null) return;

            TestingGrid.Children.Clear(); // Clear existing grid items

            currentPage = Math.Clamp(currentPage, 0, GetPageCount() - 1);
            int elementsPerPage = Math.Max(1, rows * columns);
            int startIndex = currentPage * elementsPerPage;
            int endIndex = Math.Min(startIndex + elementsPerPage, GetItemCount());

            for (int i = startIndex, row = 0, col = 0; i < endIndex; i++)
            {
                UIElement? element = CreateElementForIndex(i);
                if (element == null)
                {
                    continue;
                }

                // Ensure the element is placed in a valid row and column
                if (row >= TestingGrid.RowDefinitions.Count || col >= TestingGrid.ColumnDefinitions.Count)
                    continue;

                Grid.SetRow(element, row);
                Grid.SetColumn(element, col);
                if (automaticItemWidth > 0 && element is FrameworkElement sized)
                {
                    // AO2 does not stretch its buttons: the item keeps the theme's exact size, the gap
                    // lives in the cell, and any leftover space in the area simply stays empty.
                    sized.Width = automaticItemWidth;
                    sized.Height = automaticItemHeight;
                    sized.Margin = new Thickness(0);
                    sized.HorizontalAlignment = HorizontalAlignment.Left;
                    sized.VerticalAlignment = VerticalAlignment.Top;
                }

                TestingGrid.Children.Add(element);

                // Move to next column
                col++;
                if (col >= columns)
                {
                    col = 0;
                    row++;
                }
            }

            UpdateButtonVisibility();
        }


        /// <summary>
        /// Helper method to find a child of a specific type in the visual tree.
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;

                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }

            return null;
        }

        public void DeleteElement(UIElement element)
        {
            ClearVirtualizedItems();
            if (elements.Remove(element))
            {
                UpdateGridContent();
            }
        }

        public void DeleteElementAt(int index)
        {
            ClearVirtualizedItems();
            if (index >= 0 && index < elements.Count)
            {
                elements.RemoveAt(index);
                UpdateGridContent();
            }
        }

        public void ClearGrid()
        {
            elements.Clear();
            ClearVirtualizedItems();
            UpdateGridContent();
        }

        private int GetItemCount()
        {
            return virtualElementFactory == null ? elements.Count : virtualItems.Count;
        }

        private UIElement? CreateElementForIndex(int index)
        {
            if (virtualElementFactory == null)
            {
                return elements[index];
            }

            if (index < 0 || index >= virtualItems.Count)
            {
                return null;
            }

            return virtualElementFactory(virtualItems[index]);
        }

        private void ClearVirtualizedItems()
        {
            virtualItems.Clear();
            virtualElementFactory = null;
        }

        #region Currently Works
        public void SetScrollMode(ScrollMode mode)
        {
            currentScrollMode = mode;
            UpdateButtonVisibility();
        }
        private void UpdateButtonVisibility()
        {
            int totalPages = (int)Math.Ceiling((double)GetItemCount() / Math.Max(1, rows * columns));

            Grid grid = LayoutGrid;
            if (pagingControlsExtracted)
            {
                // The buttons are panels now: their own placement owns their geometry, and the grid must
                // not add or remove rows and columns for them any more.
                LeftButton.IsEnabled = currentPage > 0;
                RightButton.IsEnabled = currentPage + 1 < totalPages;
                UpButton.IsEnabled = LeftButton.IsEnabled;
                DownButton.IsEnabled = RightButton.IsEnabled;
                return;
            }

            if (currentScrollMode == ScrollMode.Horizontal)
            {
                // Show Left/Right buttons, hide Up/Down buttons
                LeftButton.Visibility = Visibility.Visible;
                RightButton.Visibility = Visibility.Visible;
                UpButton.Visibility = Visibility.Collapsed;
                DownButton.Visibility = Visibility.Collapsed;

                // Disable Left/Right buttons if at page limits
                LeftButton.IsEnabled = currentPage > 0;
                RightButton.IsEnabled = (currentPage + 1 < totalPages);

                // Remove Up/Down button rows if they exist
                if (grid.RowDefinitions.Count == 3)
                {
                    grid.RowDefinitions.RemoveAt(0); // Remove Up button row
                    grid.RowDefinitions.RemoveAt(1); // Remove Down button row
                }

                // Ensure Left/Right columns exist
                if (grid.ColumnDefinitions.Count < 3)
                {
                    grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(30) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                }
            }
            else // Vertical Mode
            {
                // Show Up/Down buttons, hide Left/Right buttons
                UpButton.Visibility = Visibility.Visible;
                DownButton.Visibility = Visibility.Visible;
                LeftButton.Visibility = Visibility.Collapsed;
                RightButton.Visibility = Visibility.Collapsed;

                // Disable Up/Down buttons if at page limits
                UpButton.IsEnabled = currentPage > 0;
                DownButton.IsEnabled = (currentPage + 1 < totalPages);

                // Remove Left/Right button columns if they exist
                if (grid.ColumnDefinitions.Count == 3)
                {
                    grid.ColumnDefinitions.RemoveAt(0); // Remove Left button column
                    grid.ColumnDefinitions.RemoveAt(1); // Remove Right button column
                }

                // Ensure Up/Down rows exist
                if (grid.RowDefinitions.Count < 3)
                {
                    grid.RowDefinitions.Insert(0, new RowDefinition { Height = new GridLength(30) });
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                }
            }
        }


        private void LeftPage_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage > 0)
            {
                currentPage--;
                UpdateGridContent();
            }
        }

        private void RightPage_Click(object sender, RoutedEventArgs e)
        {
            if ((currentPage + 1) * (rows * columns) < GetItemCount())
            {
                currentPage++;
                UpdateGridContent();
            }
        }

        private void UpPage_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage > 0)
            {
                currentPage--;
                UpdateGridContent();
            }
        }

        private void DownPage_Click(object sender, RoutedEventArgs e)
        {
            if ((currentPage + 1) * (rows * columns) < GetItemCount())
            {
                currentPage++;
                UpdateGridContent();
            }
        }
        #endregion

    }
}
