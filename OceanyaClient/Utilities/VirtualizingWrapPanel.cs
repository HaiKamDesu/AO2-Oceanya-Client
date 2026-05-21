using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Fixed-cell wrap panel that virtualizes item containers for large icon lists.
    /// </summary>
    public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        private Size extent = Size.Empty;
        private Size viewport = Size.Empty;
        private Point offset;

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth => extent.Width;
        public double ExtentHeight => extent.Height;
        public double ViewportWidth => viewport.Width;
        public double ViewportHeight => viewport.Height;
        public double HorizontalOffset => offset.X;
        public double VerticalOffset => offset.Y;
        public ScrollViewer? ScrollOwner { get; set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            ItemsControl? itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = itemsControl?.HasItems == true ? itemsControl.Items.Count : 0;
            double itemWidth = Math.Max(1, ItemWidth);
            double itemHeight = Math.Max(1, ItemHeight);
            double availableWidth = double.IsInfinity(availableSize.Width) ? itemWidth : Math.Max(itemWidth, availableSize.Width);
            int itemsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / itemWidth));
            int rowCount = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)itemsPerRow);

            viewport = new Size(availableWidth, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
            extent = new Size(availableWidth, rowCount * itemHeight);
            CoerceOffsets();
            ScrollOwner?.InvalidateScrollInfo();

            if (itemCount == 0)
            {
                RemoveInternalChildRange(0, InternalChildren.Count);
                return availableSize;
            }

            int firstVisibleRow = Math.Max(0, (int)Math.Floor(offset.Y / itemHeight));
            int visibleRowCount = Math.Max(1, (int)Math.Ceiling(viewport.Height / itemHeight) + 2);
            int firstIndex = Math.Clamp(firstVisibleRow * itemsPerRow, 0, itemCount - 1);
            int lastIndex = Math.Clamp(((firstVisibleRow + visibleRowCount) * itemsPerRow) - 1, 0, itemCount - 1);

            IItemContainerGenerator? generator = ItemContainerGenerator;
            if (generator == null)
            {
                RemoveInternalChildRange(0, InternalChildren.Count);
                return availableSize;
            }

            try
            {
                GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstIndex);
                int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

                using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
                {
                    for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
                    {
                        bool newlyRealized;
                        UIElement child = (UIElement)generator.GenerateNext(out newlyRealized);
                        if (newlyRealized)
                        {
                            if (childIndex >= InternalChildren.Count)
                            {
                                AddInternalChild(child);
                            }
                            else
                            {
                                InsertInternalChild(childIndex, child);
                            }

                            generator.PrepareItemContainer(child);
                        }

                        child.Measure(new Size(itemWidth, itemHeight));
                    }
                }

                CleanUpItems(firstIndex, lastIndex);
            }
            catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or ArgumentException)
            {
                Common.CustomConsole.Warning("VirtualizingWrapPanel skipped a stale layout pass while its item generator was being rebuilt.", ex);
                RemoveInternalChildRange(0, InternalChildren.Count);
            }
            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double itemWidth = Math.Max(1, ItemWidth);
            double itemHeight = Math.Max(1, ItemHeight);
            int itemsPerRow = Math.Max(1, (int)Math.Floor(Math.Max(itemWidth, finalSize.Width) / itemWidth));

            foreach (UIElement child in InternalChildren)
            {
                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(child), 0));
                if (itemIndex < 0)
                {
                    continue;
                }

                int row = itemIndex / itemsPerRow;
                int column = itemIndex % itemsPerRow;
                Rect bounds = new Rect(
                    column * itemWidth - offset.X,
                    row * itemHeight - offset.Y,
                    itemWidth,
                    itemHeight);
                child.Arrange(bounds);
            }

            return finalSize;
        }

        private void CleanUpItems(int firstIndex, int lastIndex)
        {
            for (int childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
            {
                GeneratorPosition childGeneratorPosition = new GeneratorPosition(childIndex, 0);
                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(childGeneratorPosition);
                if (itemIndex < firstIndex || itemIndex > lastIndex)
                {
                    RemoveInternalChildRange(childIndex, 1);
                    ItemContainerGenerator.Remove(childGeneratorPosition, 1);
                }
            }
        }

        private void SetVerticalOffsetCore(double value)
        {
            offset.Y = Math.Clamp(value, 0, Math.Max(0, extent.Height - viewport.Height));
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        private void CoerceOffsets()
        {
            offset.X = Math.Clamp(offset.X, 0, Math.Max(0, extent.Width - viewport.Width));
            offset.Y = Math.Clamp(offset.Y, 0, Math.Max(0, extent.Height - viewport.Height));
        }

        public void LineUp() => SetVerticalOffset(VerticalOffset - 16);
        public void LineDown() => SetVerticalOffset(VerticalOffset + 16);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 48);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 48);
        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 16);
        public void LineRight() => SetHorizontalOffset(HorizontalOffset + 16);
        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 48);
        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 48);
        public void SetHorizontalOffset(double value)
        {
            offset.X = Math.Clamp(value, 0, Math.Max(0, extent.Width - viewport.Width));
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        public void SetVerticalOffset(double value) => SetVerticalOffsetCore(value);

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            int childIndex = InternalChildren.IndexOf((UIElement)visual);
            if (childIndex < 0)
            {
                return Rect.Empty;
            }

            int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            double itemHeight = Math.Max(1, ItemHeight);
            int itemsPerRow = Math.Max(1, (int)Math.Floor(Math.Max(Math.Max(1, ItemWidth), viewport.Width) / Math.Max(1, ItemWidth)));
            int row = itemIndex / itemsPerRow;
            double itemTop = row * itemHeight;
            if (itemTop < VerticalOffset)
            {
                SetVerticalOffset(itemTop);
            }
            else if (itemTop + itemHeight > VerticalOffset + ViewportHeight)
            {
                SetVerticalOffset(itemTop + itemHeight - ViewportHeight);
            }

            return rectangle;
        }
    }
}
