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

        public PageButtonGrid()
        {
            InitializeComponent();
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

            for (int i = 0; i < rows; i++)
                TestingGrid.RowDefinitions.Add(new RowDefinition());

            for (int i = 0; i < columns; i++)
                TestingGrid.ColumnDefinitions.Add(new ColumnDefinition());
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

            Grid grid = (Grid)UpButton.Parent; // Get the parent Grid

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
