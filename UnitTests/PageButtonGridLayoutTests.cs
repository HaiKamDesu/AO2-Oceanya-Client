using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NUnit.Framework;
using OceanyaClient.Components;

namespace UnitTests
{
    /// <summary>
    /// Covers how the paged button grid lays out items, which is what an AO2 theme's emote metrics drive.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class PageButtonGridLayoutTests
    {
        [Test]
        public void AutomaticPageSize_FitsFixedSizeItemsTheWayAo2Does()
        {
            PageButtonGrid grid = CreateMeasuredGrid(width: 400, height: 120, reservePaging: false, itemSize: 40);
            grid.SetVirtualizedItems(Enumerable.Range(0, 60).ToList(), _ => new ToggleButton());
            grid.UpdateLayout();

            Grid itemArea = FindItemArea(grid);

            // AO2: ((400 - 40) / (0 + 40)) + 1 = 10 columns, ((120 - 40) / 40) + 1 = 3 rows.
            Assert.That(itemArea.ColumnDefinitions, Has.Count.EqualTo(10));
            Assert.That(itemArea.RowDefinitions, Has.Count.EqualTo(3));
            Assert.That(itemArea.Children, Has.Count.EqualTo(30));

            // The item area must actually have room: zeroing the wrong row definition used to leave it 0px
            // tall, which made the whole emote grid invisible after an import.
            Assert.That(itemArea.ActualHeight, Is.GreaterThan(100));
            Assert.That(itemArea.ActualWidth, Is.GreaterThan(390));

            FrameworkElement firstItem = (FrameworkElement)itemArea.Children[0];
            Assert.That(firstItem.Width, Is.EqualTo(40), "AO2 keeps items at the theme's size instead of stretching them.");
            Assert.That(firstItem.Height, Is.EqualTo(40));
        }

        [Test]
        public void ExtractedPagingControls_KeepTheirOwnAppearance()
        {
            PageButtonGrid grid = new PageButtonGrid();
            IReadOnlyList<Button> paging = grid.ExtractPagingControls();

            // An implicit style is resolved through the ancestor chain, which these buttons leave when they
            // are reparented onto the main canvas - so the style has to travel with them.
            Assert.That(paging, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(paging[0].Style, Is.Not.Null);
            Assert.That(paging[0].Template, Is.Not.Null);
        }

        [Test]
        public void PagingReservation_RestoresTheStockContentCell()
        {
            PageButtonGrid grid = CreateMeasuredGrid(width: 400, height: 120, reservePaging: false, itemSize: 40);
            grid.SetPagingReservation(true);
            grid.UpdateLayout();

            Grid itemArea = FindItemArea(grid);

            // With the inset back, fewer items fit: the arrows' 30px columns and the 5px margin return.
            Assert.That(itemArea.ColumnDefinitions, Has.Count.EqualTo(8));

            // And the content cell still has room, which is what the reservation used to destroy.
            Assert.That(itemArea.ActualHeight, Is.GreaterThanOrEqualTo(80));
        }

        private static PageButtonGrid CreateMeasuredGrid(double width, double height, bool reservePaging, double itemSize)
        {
            PageButtonGrid grid = new PageButtonGrid { Width = width, Height = height };
            Window host = new Window { Width = width + 40, Height = height + 40, Content = grid, ShowActivated = false };
            host.Show();
            grid.ExtractPagingControls();
            grid.SetPagingReservation(reservePaging);
            grid.EnableAutomaticPageSize(itemSize, itemSize, 0, 0);
            grid.UpdateLayout();
            return grid;
        }

        private static Grid FindItemArea(PageButtonGrid grid)
        {
            Grid? found = FindDescendant<Grid>(grid, "TestingGrid");
            Assert.That(found, Is.Not.Null, "The item area must exist.");
            return found!;
        }

        private static T? FindDescendant<T>(DependencyObject element, string name) where T : FrameworkElement
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
                if (child is T typed && typed.Name == name)
                {
                    return typed;
                }

                T? nested = FindDescendant<T>(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
