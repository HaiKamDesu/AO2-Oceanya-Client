using Common;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Photoshop-style square cutout selection surface: zoom/pan image preview, image-bounds overlay,
    /// rule-of-thirds guides, draggable/resizable selection with corner and edge handles, undo/redo,
    /// and a right-click context menu of selection actions.
    /// Used by the AO2 character creator emote cutting dialogs.
    /// </summary>
    public sealed class CutoutSelectionSurface : Grid
    {
        private const double MinZoom = 0.1;
        private const double MaxZoom = 16.0;
        private const double HandleSize = 9.0;
        private const double HandleHitTolerance = 7.0;

        private enum DragMode
        {
            None,
            Create,
            Move,
            ResizeNorth,
            ResizeSouth,
            ResizeEast,
            ResizeWest,
            ResizeNorthEast,
            ResizeNorthWest,
            ResizeSouthEast,
            ResizeSouthWest
        }

        private readonly ScrollViewer scrollViewer;
        private readonly Grid contentRoot;
        private readonly Image imageElement;
        private readonly Canvas overlayCanvas;
        private readonly Path dimPath = new Path();
        private readonly Rectangle ghostRect = new Rectangle();
        private readonly Rectangle selectionBackRect = new Rectangle();
        private readonly Rectangle selectionRect = new Rectangle();
        private readonly Rectangle imageBoundsRect = new Rectangle();
        private readonly List<Line> guideLines = new List<Line>();
        private readonly List<Rectangle> handles = new List<Rectangle>();
        private readonly DispatcherTimer marchingAntsTimer;

        private readonly Slider zoomSlider;
        private readonly TextBlock zoomValueText;
        private readonly CheckBox boundsCheckBox;
        private readonly Button undoButton;
        private readonly Button redoButton;
        private readonly TextBlock selectionInfoText;

        private readonly Stack<PixelSquareSelection?> undoStack = new Stack<PixelSquareSelection?>();
        private readonly Stack<PixelSquareSelection?> redoStack = new Stack<PixelSquareSelection?>();

        private BitmapSource? frame;
        private PixelSquareSelection? selection;
        private PixelSquareSelection? ghostSelection;
        private double zoom = 1.0;
        private bool suppressZoomSliderEvent;

        private DragMode dragMode = DragMode.None;
        private Point dragAnchorPixel;
        private PixelSquareSelection dragStartSelection;
        private Point dragStartPixel;
        private PixelSquareSelection? gestureStartSelection;
        private bool gestureChangedSelection;

        private bool isPanning;
        private bool isOverlayPanning;
        private Point panStartPoint;
        private double panStartHorizontal;
        private double panStartVertical;

        /// <summary>Creates the surface with its toolbar.</summary>
        public CutoutSelectionSurface()
        {
            imageElement = new Image
            {
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(imageElement, BitmapScalingMode.HighQuality);

            overlayCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                ClipToBounds = false
            };

            dimPath.Fill = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0));
            dimPath.IsHitTestVisible = false;
            dimPath.Visibility = Visibility.Collapsed;

            imageBoundsRect.Stroke = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            imageBoundsRect.StrokeThickness = 1.2;
            imageBoundsRect.StrokeDashArray = new DoubleCollection { 6, 3 };
            imageBoundsRect.Fill = Brushes.Transparent;
            imageBoundsRect.IsHitTestVisible = false;
            imageBoundsRect.Visibility = Visibility.Collapsed;

            ghostRect.Stroke = new SolidColorBrush(Color.FromRgb(226, 160, 96));
            ghostRect.StrokeThickness = 1.3;
            ghostRect.StrokeDashArray = new DoubleCollection { 4, 2 };
            ghostRect.Fill = Brushes.Transparent;
            ghostRect.IsHitTestVisible = false;
            ghostRect.Visibility = Visibility.Collapsed;

            selectionBackRect.Stroke = new SolidColorBrush(Color.FromArgb(190, 8, 12, 16));
            selectionBackRect.StrokeThickness = 1.6;
            selectionBackRect.Fill = new SolidColorBrush(Color.FromArgb(28, 96, 182, 226));
            selectionBackRect.IsHitTestVisible = false;
            selectionBackRect.Visibility = Visibility.Collapsed;

            selectionRect.Stroke = new SolidColorBrush(Color.FromRgb(214, 240, 255));
            selectionRect.StrokeThickness = 1.4;
            selectionRect.StrokeDashArray = new DoubleCollection { 4, 4 };
            selectionRect.Fill = Brushes.Transparent;
            selectionRect.IsHitTestVisible = false;
            selectionRect.Visibility = Visibility.Collapsed;

            for (int i = 0; i < 4; i++)
            {
                Line guide = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    SnapsToDevicePixels = true
                };
                guideLines.Add(guide);
            }

            for (int i = 0; i < 8; i++)
            {
                Rectangle handle = new Rectangle
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Fill = new SolidColorBrush(Color.FromRgb(238, 248, 255)),
                    Stroke = new SolidColorBrush(Color.FromRgb(24, 34, 44)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed
                };
                handles.Add(handle);
            }

            overlayCanvas.Children.Add(dimPath);
            overlayCanvas.Children.Add(imageBoundsRect);
            overlayCanvas.Children.Add(ghostRect);
            overlayCanvas.Children.Add(selectionBackRect);
            overlayCanvas.Children.Add(selectionRect);
            foreach (Line guide in guideLines)
            {
                overlayCanvas.Children.Add(guide);
            }

            foreach (Rectangle handle in handles)
            {
                overlayCanvas.Children.Add(handle);
            }

            contentRoot = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            contentRoot.Children.Add(imageElement);
            contentRoot.Children.Add(overlayCanvas);

            scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Content = contentRoot
            };

            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Children.Add(scrollViewer);
            Focusable = true;
            FocusVisualStyle = null;

            marchingAntsTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(90)
            };
            marchingAntsTimer.Tick += (_, _) =>
            {
                selectionRect.StrokeDashOffset = (selectionRect.StrokeDashOffset + 1) % 8;
            };

            zoomSlider = new Slider
            {
                Minimum = MinZoom,
                Maximum = MaxZoom,
                Value = 1.0,
                Width = 130,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Zoom the preview."
            };
            zoomValueText = new TextBlock
            {
                Text = "100%",
                MinWidth = 42,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 221, 232)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            boundsCheckBox = new CheckBox
            {
                Content = "Bounds",
                IsChecked = SaveFile.Data.CharacterCreatorViewImageBounds,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240)),
                ToolTip = "Show the image bounds overlay."
            };
            undoButton = CreateToolbarButton("↶", "Undo selection change (Ctrl+Z).");
            redoButton = CreateToolbarButton("↷", "Redo selection change (Ctrl+Y).");
            selectionInfoText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(186, 202, 218)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = 11
            };

            Toolbar = BuildToolbar();
            WireEvents();
            UpdateZoomDependentLayout();
            UpdateHistoryButtons();
            UpdateSelectionInfoText();
            BuildContextMenu();
        }

        /// <summary>Toolbar row (zoom, fit, bounds, guides, undo/redo). Host it wherever the dialog wants.</summary>
        public FrameworkElement Toolbar { get; }

        /// <summary>Raised whenever the selection changes, including continuously during a drag.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Raised when a selection gesture or discrete action finishes.</summary>
        public event EventHandler? SelectionCommitted;

        /// <summary>Raised when the context menu copy action is used.</summary>
        public event EventHandler? CopySelectionRequested;

        /// <summary>Raised when the context menu paste action is used.</summary>
        public event EventHandler? PasteSelectionRequested;

        /// <summary>Optional predicate telling the context menu whether a paste source exists.</summary>
        public Func<bool>? CanPasteSelection { get; set; }

        /// <summary>Current square selection in source-image pixels, if any.</summary>
        public PixelSquareSelection? Selection => selection;

        /// <summary>True when an undo step is available.</summary>
        public bool CanUndo => undoStack.Count > 0;

        /// <summary>True when a redo step is available.</summary>
        public bool CanRedo => redoStack.Count > 0;

        /// <summary>
        /// Size of the zoomed image/overlay host, in device-independent units. Zero means nothing is drawn —
        /// used by tests to catch the stale-layout bug where a frame swap left the canvas collapsed.
        /// </summary>
        public Size ContentSize => new Size(contentRoot.Width, contentRoot.Height);

        /// <summary>Current zoom factor.</summary>
        public double Zoom
        {
            get => zoom;
            set => ApplyZoom(value, null);
        }

        /// <summary>
        /// Replaces the previewed frame. Selection and zoom survive same-sized frames (animation playback);
        /// a different frame size refits the view and reclamps the selection.
        /// </summary>
        public void SetFrame(BitmapSource? newFrame)
        {
            bool sameSize = frame != null
                && newFrame != null
                && frame.PixelWidth == newFrame.PixelWidth
                && frame.PixelHeight == newFrame.PixelHeight;
            frame = newFrame;
            imageElement.Source = newFrame;
            if (newFrame == null)
            {
                selection = null;
                ghostSelection = null;
                UpdateZoomDependentLayout();
                RefreshOverlay();
                return;
            }

            if (selection.HasValue)
            {
                selection = ClampToFrame(selection.Value);
            }

            // Always re-apply layout: the frame swap resized the host, and ZoomToFit may land on the
            // zoom we already had (which would otherwise leave the canvas at its zero size).
            UpdateZoomDependentLayout();
            RefreshOverlay();
            if (!sameSize)
            {
                ZoomToFit();
            }
        }

        /// <summary>Sets the selection programmatically.</summary>
        public void SetSelection(PixelSquareSelection? value, bool recordUndo)
        {
            PixelSquareSelection? next = value.HasValue && frame != null ? ClampToFrame(value.Value) : null;
            if (SelectionsEqual(next, selection))
            {
                RefreshOverlay();
                return;
            }

            if (recordUndo)
            {
                PushUndo(selection);
            }

            selection = next;
            RefreshOverlay();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (recordUndo)
            {
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Sets the dashed "previously saved" reference rectangle, or null to hide it.</summary>
        public void SetGhostSelection(PixelSquareSelection? value)
        {
            ghostSelection = value;
            RefreshOverlay();
        }

        /// <summary>Clears undo/redo history, e.g. when switching to another emote.</summary>
        public void ResetHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
            UpdateHistoryButtons();
        }

        /// <summary>Undoes the last selection change.</summary>
        public void Undo()
        {
            if (undoStack.Count == 0)
            {
                return;
            }

            redoStack.Push(selection);
            PixelSquareSelection? restored = undoStack.Pop();
            selection = restored.HasValue && frame != null ? ClampToFrame(restored.Value) : null;
            RefreshOverlay();
            UpdateHistoryButtons();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Redoes the last undone selection change.</summary>
        public void Redo()
        {
            if (redoStack.Count == 0)
            {
                return;
            }

            undoStack.Push(selection);
            PixelSquareSelection? restored = redoStack.Pop();
            selection = restored.HasValue && frame != null ? ClampToFrame(restored.Value) : null;
            RefreshOverlay();
            UpdateHistoryButtons();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Fits the whole image inside the visible viewport.</summary>
        public void ZoomToFit()
        {
            ZoomToFit(0);
        }

        private void ZoomToFit(int retryCount)
        {
            if (frame == null)
            {
                return;
            }

            double availableWidth = Math.Max(1, scrollViewer.ViewportWidth > 0 ? scrollViewer.ViewportWidth : ActualWidth);
            double availableHeight = Math.Max(1, scrollViewer.ViewportHeight > 0 ? scrollViewer.ViewportHeight : ActualHeight);
            if (availableWidth <= 1 || availableHeight <= 1)
            {
                // The host may not be measured yet on the first frame of a freshly opened dialog.
                if (retryCount < 5)
                {
                    Dispatcher.BeginInvoke(new Action(() => ZoomToFit(retryCount + 1)), DispatcherPriority.Loaded);
                }

                return;
            }

            double fit = Math.Min(availableWidth / frame.PixelWidth, availableHeight / frame.PixelHeight);
            ApplyZoom(fit, null);
        }

        /// <summary>
        /// Handles keyboard shortcuts (Ctrl+Z/Ctrl+Y/Ctrl+Shift+Z undo-redo, arrow-key nudge).
        /// Returns true when the key was consumed.
        /// </summary>
        public bool HandleKey(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            if (ctrl && e.Key == Key.Z)
            {
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }

                return true;
            }

            if (ctrl && e.Key == Key.Y)
            {
                Redo();
                return true;
            }

            // Space is the pan modifier while the pointer is over the image; swallow it so a focused
            // dialog button is not "clicked" mid-pan.
            if (e.Key == Key.Space && IsMouseOver)
            {
                overlayCanvas.Cursor = Cursors.ScrollAll;
                return true;
            }

            // Nudge/resize keys must not steal arrow-key focus navigation from the rest of the dialog.
            if (frame == null || !selection.HasValue || !(IsMouseOver || IsKeyboardFocusWithin))
            {
                return false;
            }

            int step = shift ? 10 : 1;
            switch (e.Key)
            {
                case Key.Left:
                    NudgeSelection(-step, 0);
                    return true;
                case Key.Right:
                    NudgeSelection(step, 0);
                    return true;
                case Key.Up:
                    NudgeSelection(0, -step);
                    return true;
                case Key.Down:
                    NudgeSelection(0, step);
                    return true;
                case Key.OemPlus:
                case Key.Add:
                    if (ctrl)
                    {
                        ApplyZoom(zoom * 1.25, null);
                    }
                    else
                    {
                        ResizeSelectionBy(step);
                    }

                    return true;
                case Key.OemMinus:
                case Key.Subtract:
                    if (ctrl)
                    {
                        ApplyZoom(zoom / 1.25, null);
                    }
                    else
                    {
                        ResizeSelectionBy(-step);
                    }

                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Moves the selection by the given pixel offsets, recording an undo step.</summary>
        public void NudgeSelection(int deltaX, int deltaY)
        {
            if (!selection.HasValue)
            {
                return;
            }

            PixelSquareSelection current = selection.Value;
            SetSelection(new PixelSquareSelection(current.X + deltaX, current.Y + deltaY, current.Size), recordUndo: true);
        }

        /// <summary>Grows or shrinks the selection square by the given pixel amount, recording an undo step.</summary>
        public void ResizeSelectionBy(int delta)
        {
            if (!selection.HasValue)
            {
                return;
            }

            PixelSquareSelection current = selection.Value;
            SetSelection(
                new PixelSquareSelection(current.X, current.Y, Math.Max(1, current.Size + delta)),
                recordUndo: true);
        }

        /// <summary>Centers the selection over the image without changing its size.</summary>
        public void CenterSelectionToImage()
        {
            if (frame == null || !selection.HasValue)
            {
                return;
            }

            PixelSquareSelection current = selection.Value;
            int x = (int)Math.Round((frame.PixelWidth - current.Size) / 2.0);
            int y = (int)Math.Round((frame.PixelHeight - current.Size) / 2.0);
            SetSelection(new PixelSquareSelection(x, y, current.Size), recordUndo: true);
        }

        /// <summary>Selects the largest centered square that fits the image.</summary>
        public void FitSelectionToImage()
        {
            if (frame == null)
            {
                return;
            }

            int size = Math.Min(frame.PixelWidth, frame.PixelHeight);
            int x = (int)Math.Round((frame.PixelWidth - size) / 2.0);
            int y = (int)Math.Round((frame.PixelHeight - size) / 2.0);
            SetSelection(new PixelSquareSelection(x, y, size), recordUndo: true);
        }

        /// <summary>Snaps the selection to the square bounding box of the frame's non-transparent pixels.</summary>
        public void SnapSelectionToOpaqueContent()
        {
            if (frame == null || !TryComputeOpaqueBounds(frame, out Int32Rect bounds))
            {
                return;
            }

            int size = Math.Max(1, Math.Max(bounds.Width, bounds.Height));
            size = Math.Min(size, Math.Min(frame.PixelWidth, frame.PixelHeight));
            int centerX = bounds.X + (bounds.Width / 2);
            int centerY = bounds.Y + (bounds.Height / 2);
            SetSelection(
                new PixelSquareSelection(centerX - (size / 2), centerY - (size / 2), size),
                recordUndo: true);
        }

        private static Button CreateToolbarButton(string content, string tooltip)
        {
            return new Button
            {
                Content = content,
                Width = 28,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = tooltip,
                Padding = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240)),
                Background = new SolidColorBrush(Color.FromArgb(150, 34, 44, 56)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 92, 112)),
                BorderThickness = new Thickness(1),
                FontSize = 13
            };
        }

        private FrameworkElement BuildToolbar()
        {
            Button zoomOutButton = CreateToolbarButton("−", "Zoom out.");
            Button zoomResetButton = CreateToolbarButton("1:1", "Zoom to 100%.");
            zoomResetButton.Width = 38;
            Button zoomFitButton = CreateToolbarButton("⤢", "Fit the image in the view.");

            zoomOutButton.Click += (_, _) => ApplyZoom(zoom / 1.25, null);
            zoomResetButton.Click += (_, _) => ApplyZoom(1.0, null);
            zoomFitButton.Click += (_, _) => ZoomToFit();
            undoButton.Click += (_, _) => Undo();
            redoButton.Click += (_, _) => Redo();

            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };
            panel.Children.Add(zoomOutButton);
            panel.Children.Add(zoomResetButton);
            panel.Children.Add(zoomFitButton);
            panel.Children.Add(zoomSlider);
            panel.Children.Add(zoomValueText);
            panel.Children.Add(boundsCheckBox);
            panel.Children.Add(undoButton);
            panel.Children.Add(redoButton);
            panel.Children.Add(selectionInfoText);
            zoomSlider.Margin = new Thickness(8, 0, 0, 0);
            return panel;
        }

        private void WireEvents()
        {
            zoomSlider.ValueChanged += (_, _) =>
            {
                if (suppressZoomSliderEvent)
                {
                    return;
                }

                ApplyZoom(zoomSlider.Value, null);
            };
            boundsCheckBox.Checked += (_, _) =>
            {
                SaveFile.Data.CharacterCreatorViewImageBounds = true;
                SaveFile.Save();
                RefreshOverlay();
            };
            boundsCheckBox.Unchecked += (_, _) =>
            {
                SaveFile.Data.CharacterCreatorViewImageBounds = false;
                SaveFile.Save();
                RefreshOverlay();
            };

            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            scrollViewer.SizeChanged += (_, _) => RefreshOverlay();

            overlayCanvas.MouseLeftButtonDown += OnOverlayMouseLeftButtonDown;
            overlayCanvas.MouseMove += OnOverlayMouseMove;
            overlayCanvas.MouseLeftButtonUp += OnOverlayMouseLeftButtonUp;
            overlayCanvas.LostMouseCapture += (_, _) =>
            {
                isOverlayPanning = false;
                EndGesture();
            };

            scrollViewer.PreviewMouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle)
                {
                    return;
                }

                isPanning = true;
                panStartPoint = e.GetPosition(scrollViewer);
                panStartHorizontal = scrollViewer.HorizontalOffset;
                panStartVertical = scrollViewer.VerticalOffset;
                scrollViewer.CaptureMouse();
                scrollViewer.Cursor = Cursors.SizeAll;
                e.Handled = true;
            };
            scrollViewer.PreviewMouseMove += (_, e) =>
            {
                if (!isPanning)
                {
                    return;
                }

                Point current = e.GetPosition(scrollViewer);
                Vector delta = current - panStartPoint;
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, panStartHorizontal - delta.X));
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, panStartVertical - delta.Y));
                e.Handled = true;
            };
            scrollViewer.PreviewMouseUp += (_, e) =>
            {
                if (!isPanning || e.ChangedButton != MouseButton.Middle)
                {
                    return;
                }

                isPanning = false;
                scrollViewer.ReleaseMouseCapture();
                scrollViewer.Cursor = Cursors.Arrow;
                e.Handled = true;
            };
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            Point anchorPixel = DisplayToPixelPoint(e.GetPosition(overlayCanvas));
            Point viewportPoint = e.GetPosition(scrollViewer);
            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            ApplyZoom(zoom * factor, (anchorPixel, viewportPoint));
            e.Handled = true;
        }

        private void ApplyZoom(double requestedZoom, (Point pixel, Point viewportPoint)? anchor)
        {
            double next = Math.Clamp(requestedZoom, MinZoom, MaxZoom);
            if (Math.Abs(next - zoom) < 0.0001)
            {
                // Same zoom can still mean stale layout (frame just swapped), so refresh instead of bailing.
                UpdateZoomDependentLayout();
                RefreshOverlay();
                return;
            }

            zoom = next;
            suppressZoomSliderEvent = true;
            zoomSlider.Value = zoom;
            suppressZoomSliderEvent = false;
            zoomValueText.Text = $"{Math.Round(zoom * 100):0}%";
            RenderOptions.SetBitmapScalingMode(
                imageElement,
                zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            UpdateZoomDependentLayout();
            RefreshOverlay();

            if (anchor.HasValue)
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        scrollViewer.UpdateLayout();
                        Point target = PixelToDisplayPoint(anchor.Value.pixel);
                        Point currentViewportPoint = overlayCanvas.TranslatePoint(target, scrollViewer);
                        scrollViewer.ScrollToHorizontalOffset(
                            scrollViewer.HorizontalOffset + (currentViewportPoint.X - anchor.Value.viewportPoint.X));
                        scrollViewer.ScrollToVerticalOffset(
                            scrollViewer.VerticalOffset + (currentViewportPoint.Y - anchor.Value.viewportPoint.Y));
                    }),
                    DispatcherPriority.Render);
            }
        }

        private void UpdateZoomDependentLayout()
        {
            if (frame == null)
            {
                contentRoot.Width = 0;
                contentRoot.Height = 0;
                overlayCanvas.Width = 0;
                overlayCanvas.Height = 0;
                return;
            }

            double width = Math.Max(1, frame.PixelWidth * zoom);
            double height = Math.Max(1, frame.PixelHeight * zoom);
            contentRoot.Width = width;
            contentRoot.Height = height;
            imageElement.Width = width;
            imageElement.Height = height;
            overlayCanvas.Width = width;
            overlayCanvas.Height = height;
        }

        private Point DisplayToPixelPoint(Point displayPoint)
        {
            return new Point(displayPoint.X / Math.Max(0.0001, zoom), displayPoint.Y / Math.Max(0.0001, zoom));
        }

        private Point PixelToDisplayPoint(Point pixelPoint)
        {
            return new Point(pixelPoint.X * zoom, pixelPoint.Y * zoom);
        }

        private Rect SelectionDisplayRect(PixelSquareSelection value)
        {
            return new Rect(value.X * zoom, value.Y * zoom, value.Size * zoom, value.Size * zoom);
        }

        private PixelSquareSelection ClampToFrame(PixelSquareSelection value)
        {
            if (frame == null)
            {
                return value;
            }

            int maxSize = Math.Min(frame.PixelWidth, frame.PixelHeight);
            int size = Math.Clamp(value.Size, 1, Math.Max(1, maxSize));
            int x = Math.Clamp(value.X, 0, Math.Max(0, frame.PixelWidth - size));
            int y = Math.Clamp(value.Y, 0, Math.Max(0, frame.PixelHeight - size));
            return new PixelSquareSelection(x, y, size);
        }

        private static bool SelectionsEqual(PixelSquareSelection? left, PixelSquareSelection? right)
        {
            if (!left.HasValue || !right.HasValue)
            {
                return left.HasValue == right.HasValue;
            }

            return left.Value.X == right.Value.X
                && left.Value.Y == right.Value.Y
                && left.Value.Size == right.Value.Size;
        }

        private void PushUndo(PixelSquareSelection? previous)
        {
            undoStack.Push(previous);
            if (undoStack.Count > 100)
            {
                PixelSquareSelection?[] retained = undoStack.ToArray();
                undoStack.Clear();
                for (int i = retained.Length - 2; i >= 0; i--)
                {
                    undoStack.Push(retained[i]);
                }
            }

            redoStack.Clear();
            UpdateHistoryButtons();
        }

        private void UpdateHistoryButtons()
        {
            undoButton.IsEnabled = CanUndo;
            redoButton.IsEnabled = CanRedo;
        }

        private void UpdateSelectionInfoText()
        {
            selectionInfoText.Text = selection.HasValue
                ? $"{selection.Value.Size}×{selection.Value.Size} px @ {selection.Value.X},{selection.Value.Y}"
                : "No selection";
        }

        private void RefreshOverlay()
        {
            UpdateSelectionInfoText();
            if (frame == null)
            {
                dimPath.Visibility = Visibility.Collapsed;
                selectionRect.Visibility = Visibility.Collapsed;
                selectionBackRect.Visibility = Visibility.Collapsed;
                ghostRect.Visibility = Visibility.Collapsed;
                imageBoundsRect.Visibility = Visibility.Collapsed;
                SetGuidesVisibility(false);
                SetHandlesVisibility(false);
                marchingAntsTimer.Stop();
                return;
            }

            double canvasWidth = frame.PixelWidth * zoom;
            double canvasHeight = frame.PixelHeight * zoom;

            if (SaveFile.Data.CharacterCreatorViewImageBounds)
            {
                Canvas.SetLeft(imageBoundsRect, 0);
                Canvas.SetTop(imageBoundsRect, 0);
                imageBoundsRect.Width = canvasWidth;
                imageBoundsRect.Height = canvasHeight;
                imageBoundsRect.Visibility = Visibility.Visible;
            }
            else
            {
                imageBoundsRect.Visibility = Visibility.Collapsed;
            }

            if (ghostSelection.HasValue)
            {
                Rect ghost = SelectionDisplayRect(ghostSelection.Value);
                Canvas.SetLeft(ghostRect, ghost.X);
                Canvas.SetTop(ghostRect, ghost.Y);
                ghostRect.Width = ghost.Width;
                ghostRect.Height = ghost.Height;
                ghostRect.Visibility = ghost.Width >= 2 && ghost.Height >= 2 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                ghostRect.Visibility = Visibility.Collapsed;
            }

            if (!selection.HasValue)
            {
                dimPath.Visibility = Visibility.Collapsed;
                selectionRect.Visibility = Visibility.Collapsed;
                selectionBackRect.Visibility = Visibility.Collapsed;
                SetGuidesVisibility(false);
                SetHandlesVisibility(false);
                marchingAntsTimer.Stop();
                return;
            }

            Rect bounds = SelectionDisplayRect(selection.Value);
            Canvas.SetLeft(selectionBackRect, bounds.X);
            Canvas.SetTop(selectionBackRect, bounds.Y);
            selectionBackRect.Width = bounds.Width;
            selectionBackRect.Height = bounds.Height;
            selectionBackRect.Visibility = Visibility.Visible;

            Canvas.SetLeft(selectionRect, bounds.X);
            Canvas.SetTop(selectionRect, bounds.Y);
            selectionRect.Width = bounds.Width;
            selectionRect.Height = bounds.Height;
            selectionRect.Visibility = Visibility.Visible;
            if (!marchingAntsTimer.IsEnabled)
            {
                marchingAntsTimer.Start();
            }

            if (SaveFile.Data.CharacterCreatorCutoutDimOutside)
            {
                GeometryGroup dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
                dim.Children.Add(new RectangleGeometry(new Rect(0, 0, canvasWidth, canvasHeight)));
                dim.Children.Add(new RectangleGeometry(bounds));
                dimPath.Data = dim;
                dimPath.Visibility = Visibility.Visible;
            }
            else
            {
                dimPath.Visibility = Visibility.Collapsed;
            }

            UpdateGuides(bounds);
            UpdateHandles(bounds);
        }

        private void SetGuidesVisibility(bool visible)
        {
            foreach (Line guide in guideLines)
            {
                guide.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetHandlesVisibility(bool visible)
        {
            foreach (Rectangle handle in handles)
            {
                handle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateGuides(Rect bounds)
        {
            if (!SaveFile.Data.CharacterCreatorCutoutShowGuides || bounds.Width < 12 || bounds.Height < 12)
            {
                SetGuidesVisibility(false);
                return;
            }

            double thirdX1 = bounds.X + (bounds.Width / 3.0);
            double thirdX2 = bounds.X + (bounds.Width * 2.0 / 3.0);
            double thirdY1 = bounds.Y + (bounds.Height / 3.0);
            double thirdY2 = bounds.Y + (bounds.Height * 2.0 / 3.0);

            ConfigureGuide(guideLines[0], thirdX1, bounds.Y, thirdX1, bounds.Bottom);
            ConfigureGuide(guideLines[1], thirdX2, bounds.Y, thirdX2, bounds.Bottom);
            ConfigureGuide(guideLines[2], bounds.X, thirdY1, bounds.Right, thirdY1);
            ConfigureGuide(guideLines[3], bounds.X, thirdY2, bounds.Right, thirdY2);
            SetGuidesVisibility(true);
        }

        private static void ConfigureGuide(Line line, double x1, double y1, double x2, double y2)
        {
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
        }

        private void UpdateHandles(Rect bounds)
        {
            if (bounds.Width < 6 || bounds.Height < 6)
            {
                SetHandlesVisibility(false);
                return;
            }

            Point[] positions = GetHandleCenters(bounds);
            for (int i = 0; i < handles.Count; i++)
            {
                Canvas.SetLeft(handles[i], positions[i].X - (HandleSize / 2));
                Canvas.SetTop(handles[i], positions[i].Y - (HandleSize / 2));
                handles[i].Visibility = Visibility.Visible;
            }
        }

        private static Point[] GetHandleCenters(Rect bounds)
        {
            double centerX = bounds.X + (bounds.Width / 2);
            double centerY = bounds.Y + (bounds.Height / 2);
            return new[]
            {
                new Point(bounds.X, bounds.Y),
                new Point(centerX, bounds.Y),
                new Point(bounds.Right, bounds.Y),
                new Point(bounds.Right, centerY),
                new Point(bounds.Right, bounds.Bottom),
                new Point(centerX, bounds.Bottom),
                new Point(bounds.X, bounds.Bottom),
                new Point(bounds.X, centerY)
            };
        }

        private DragMode HitTestSelection(Point displayPoint)
        {
            if (!selection.HasValue)
            {
                return DragMode.Create;
            }

            Rect bounds = SelectionDisplayRect(selection.Value);
            Point[] centers = GetHandleCenters(bounds);
            DragMode[] modes =
            {
                DragMode.ResizeNorthWest,
                DragMode.ResizeNorth,
                DragMode.ResizeNorthEast,
                DragMode.ResizeEast,
                DragMode.ResizeSouthEast,
                DragMode.ResizeSouth,
                DragMode.ResizeSouthWest,
                DragMode.ResizeWest
            };
            for (int i = 0; i < centers.Length; i++)
            {
                if (Math.Abs(displayPoint.X - centers[i].X) <= HandleHitTolerance
                    && Math.Abs(displayPoint.Y - centers[i].Y) <= HandleHitTolerance)
                {
                    return modes[i];
                }
            }

            return bounds.Contains(displayPoint) ? DragMode.Move : DragMode.Create;
        }

        private static Cursor CursorForMode(DragMode mode)
        {
            switch (mode)
            {
                case DragMode.Move:
                    return Cursors.SizeAll;
                case DragMode.ResizeNorth:
                case DragMode.ResizeSouth:
                    return Cursors.SizeNS;
                case DragMode.ResizeEast:
                case DragMode.ResizeWest:
                    return Cursors.SizeWE;
                case DragMode.ResizeNorthWest:
                case DragMode.ResizeSouthEast:
                    return Cursors.SizeNWSE;
                case DragMode.ResizeNorthEast:
                case DragMode.ResizeSouthWest:
                    return Cursors.SizeNESW;
                default:
                    return Cursors.Cross;
            }
        }

        private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            Focus();

            // Photoshop parity: hold Space to drag the view around instead of editing the selection.
            if (Keyboard.IsKeyDown(Key.Space))
            {
                isOverlayPanning = true;
                panStartPoint = e.GetPosition(scrollViewer);
                panStartHorizontal = scrollViewer.HorizontalOffset;
                panStartVertical = scrollViewer.VerticalOffset;
                overlayCanvas.Cursor = Cursors.ScrollAll;
                overlayCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            Point displayPoint = e.GetPosition(overlayCanvas);
            dragMode = HitTestSelection(displayPoint);
            gestureStartSelection = selection;
            gestureChangedSelection = false;
            dragStartPixel = DisplayToPixelPoint(displayPoint);
            if (selection.HasValue)
            {
                dragStartSelection = selection.Value;
            }

            if (dragMode == DragMode.Create)
            {
                dragAnchorPixel = dragStartPixel;
                selection = null;
                RefreshOverlay();
            }

            overlayCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnOverlayMouseMove(object sender, MouseEventArgs e)
        {
            if (frame == null)
            {
                return;
            }

            if (isOverlayPanning)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    EndOverlayPan();
                    return;
                }

                Point currentViewportPoint = e.GetPosition(scrollViewer);
                Vector panDelta = currentViewportPoint - panStartPoint;
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, panStartHorizontal - panDelta.X));
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, panStartVertical - panDelta.Y));
                e.Handled = true;
                return;
            }

            Point displayPoint = e.GetPosition(overlayCanvas);
            if (dragMode == DragMode.None || e.LeftButton != MouseButtonState.Pressed)
            {
                overlayCanvas.Cursor = Keyboard.IsKeyDown(Key.Space)
                    ? Cursors.ScrollAll
                    : CursorForMode(HitTestSelection(displayPoint));
                return;
            }

            bool fromCenter = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            Point pixelPoint = DisplayToPixelPoint(displayPoint);
            PixelSquareSelection? updated = dragMode == DragMode.Create
                ? BuildCreatedSelection(pixelPoint, fromCenter)
                : BuildAdjustedSelection(pixelPoint, fromCenter);
            if (updated.HasValue)
            {
                PixelSquareSelection clamped = ClampToFrame(updated.Value);
                if (!SelectionsEqual(clamped, selection))
                {
                    selection = clamped;
                    gestureChangedSelection = true;
                    RefreshOverlay();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            e.Handled = true;
        }

        private void OnOverlayMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isOverlayPanning)
            {
                EndOverlayPan();
                e.Handled = true;
                return;
            }

            if (dragMode == DragMode.None)
            {
                return;
            }

            if (overlayCanvas.IsMouseCaptured)
            {
                overlayCanvas.ReleaseMouseCapture();
            }
            else
            {
                EndGesture();
            }

            e.Handled = true;
        }

        private void EndOverlayPan()
        {
            if (!isOverlayPanning)
            {
                return;
            }

            isOverlayPanning = false;
            if (overlayCanvas.IsMouseCaptured)
            {
                overlayCanvas.ReleaseMouseCapture();
            }

            overlayCanvas.Cursor = Cursors.Cross;
        }

        private void EndGesture()
        {
            if (dragMode == DragMode.None)
            {
                return;
            }

            DragMode finishedMode = dragMode;
            dragMode = DragMode.None;

            // A bare click outside the selection must not destroy it — only a real drag creates a new square.
            if (finishedMode == DragMode.Create && (!selection.HasValue || selection.Value.Size <= 1))
            {
                selection = gestureStartSelection;
                RefreshOverlay();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!gestureChangedSelection && SelectionsEqual(selection, gestureStartSelection))
            {
                return;
            }

            undoStack.Push(gestureStartSelection);
            redoStack.Clear();
            UpdateHistoryButtons();
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
        }

        private PixelSquareSelection? BuildCreatedSelection(Point pixelPoint, bool fromCenter)
        {
            double dx = pixelPoint.X - dragAnchorPixel.X;
            double dy = pixelPoint.Y - dragAnchorPixel.Y;
            double size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (size < 1)
            {
                return null;
            }

            if (fromCenter)
            {
                // Alt: the press point is the center and the square grows outward from it.
                return SquareFromCenter(dragAnchorPixel.X, dragAnchorPixel.Y, size);
            }

            double directionX = dx < 0 ? -1 : 1;
            double directionY = dy < 0 ? -1 : 1;
            double left = directionX > 0 ? dragAnchorPixel.X : dragAnchorPixel.X - size;
            double top = directionY > 0 ? dragAnchorPixel.Y : dragAnchorPixel.Y - size;
            return new PixelSquareSelection(
                (int)Math.Round(left),
                (int)Math.Round(top),
                Math.Max(1, (int)Math.Round(size)));
        }

        private PixelSquareSelection? BuildAdjustedSelection(Point pixelPoint, bool fromCenter)
        {
            if (frame == null)
            {
                return null;
            }

            double left = dragStartSelection.X;
            double top = dragStartSelection.Y;
            double size = dragStartSelection.Size;
            double right = left + size;
            double bottom = top + size;
            double frameWidth = frame.PixelWidth;
            double frameHeight = frame.PixelHeight;

            if (fromCenter && dragMode != DragMode.Move)
            {
                // Alt: resize around the square's own center instead of anchoring the opposite side.
                double centerX = left + (size / 2);
                double centerY = top + (size / 2);
                double half;
                switch (dragMode)
                {
                    case DragMode.ResizeEast:
                    case DragMode.ResizeWest:
                        half = Math.Abs(pixelPoint.X - centerX);
                        break;
                    case DragMode.ResizeNorth:
                    case DragMode.ResizeSouth:
                        half = Math.Abs(pixelPoint.Y - centerY);
                        break;
                    default:
                        half = Math.Max(Math.Abs(pixelPoint.X - centerX), Math.Abs(pixelPoint.Y - centerY));
                        break;
                }

                return SquareFromCenter(centerX, centerY, half * 2);
            }

            switch (dragMode)
            {
                case DragMode.Move:
                {
                    double offsetX = pixelPoint.X - dragStartPixel.X;
                    double offsetY = pixelPoint.Y - dragStartPixel.Y;
                    return new PixelSquareSelection(
                        (int)Math.Round(left + offsetX),
                        (int)Math.Round(top + offsetY),
                        dragStartSelection.Size);
                }

                case DragMode.ResizeSouthEast:
                    return SquareFromAnchoredCorner(left, top, pixelPoint, 1, 1, frameWidth, frameHeight);
                case DragMode.ResizeSouthWest:
                    return SquareFromAnchoredCorner(right, top, pixelPoint, -1, 1, frameWidth, frameHeight);
                case DragMode.ResizeNorthEast:
                    return SquareFromAnchoredCorner(left, bottom, pixelPoint, 1, -1, frameWidth, frameHeight);
                case DragMode.ResizeNorthWest:
                    return SquareFromAnchoredCorner(right, bottom, pixelPoint, -1, -1, frameWidth, frameHeight);

                case DragMode.ResizeEast:
                {
                    double newSize = Math.Max(1, pixelPoint.X - left);
                    return SquareFromAnchoredEdge(left, top + (size / 2), newSize, horizontalAnchorIsLeft: true, frameWidth, frameHeight);
                }

                case DragMode.ResizeWest:
                {
                    double newSize = Math.Max(1, right - pixelPoint.X);
                    return SquareFromAnchoredEdge(right, top + (size / 2), newSize, horizontalAnchorIsLeft: false, frameWidth, frameHeight);
                }

                case DragMode.ResizeSouth:
                {
                    double newSize = Math.Max(1, pixelPoint.Y - top);
                    return SquareFromAnchoredEdgeVertical(top, left + (size / 2), newSize, verticalAnchorIsTop: true, frameWidth, frameHeight);
                }

                case DragMode.ResizeNorth:
                {
                    double newSize = Math.Max(1, bottom - pixelPoint.Y);
                    return SquareFromAnchoredEdgeVertical(bottom, left + (size / 2), newSize, verticalAnchorIsTop: false, frameWidth, frameHeight);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Builds a square of the requested size centered on the given pixel point, shrinking it as needed
        /// so it stays fully inside the frame (Alt / resize-from-center behavior).
        /// </summary>
        private PixelSquareSelection SquareFromCenter(double centerX, double centerY, double requestedSize)
        {
            double frameWidth = frame?.PixelWidth ?? requestedSize;
            double frameHeight = frame?.PixelHeight ?? requestedSize;
            double maxHalf = Math.Min(
                Math.Min(centerX, frameWidth - centerX),
                Math.Min(centerY, frameHeight - centerY));
            double half = Math.Clamp(requestedSize / 2, 0.5, Math.Max(0.5, maxHalf));
            int size = Math.Max(1, (int)Math.Round(half * 2));
            return new PixelSquareSelection(
                (int)Math.Round(centerX - (size / 2.0)),
                (int)Math.Round(centerY - (size / 2.0)),
                size);
        }

        private static PixelSquareSelection SquareFromAnchoredCorner(
            double anchorX,
            double anchorY,
            Point pixelPoint,
            int directionX,
            int directionY,
            double frameWidth,
            double frameHeight)
        {
            double size = Math.Max(Math.Abs(pixelPoint.X - anchorX), Math.Abs(pixelPoint.Y - anchorY));
            double maxX = directionX > 0 ? frameWidth - anchorX : anchorX;
            double maxY = directionY > 0 ? frameHeight - anchorY : anchorY;
            size = Math.Clamp(size, 1, Math.Max(1, Math.Min(maxX, maxY)));
            double left = directionX > 0 ? anchorX : anchorX - size;
            double top = directionY > 0 ? anchorY : anchorY - size;
            return new PixelSquareSelection((int)Math.Round(left), (int)Math.Round(top), Math.Max(1, (int)Math.Round(size)));
        }

        private static PixelSquareSelection SquareFromAnchoredEdge(
            double anchorX,
            double centerY,
            double size,
            bool horizontalAnchorIsLeft,
            double frameWidth,
            double frameHeight)
        {
            double maxHorizontal = horizontalAnchorIsLeft ? frameWidth - anchorX : anchorX;
            size = Math.Clamp(size, 1, Math.Max(1, Math.Min(maxHorizontal, frameHeight)));
            double left = horizontalAnchorIsLeft ? anchorX : anchorX - size;
            double top = Math.Clamp(centerY - (size / 2), 0, Math.Max(0, frameHeight - size));
            return new PixelSquareSelection((int)Math.Round(left), (int)Math.Round(top), Math.Max(1, (int)Math.Round(size)));
        }

        private static PixelSquareSelection SquareFromAnchoredEdgeVertical(
            double anchorY,
            double centerX,
            double size,
            bool verticalAnchorIsTop,
            double frameWidth,
            double frameHeight)
        {
            double maxVertical = verticalAnchorIsTop ? frameHeight - anchorY : anchorY;
            size = Math.Clamp(size, 1, Math.Max(1, Math.Min(maxVertical, frameWidth)));
            double top = verticalAnchorIsTop ? anchorY : anchorY - size;
            double left = Math.Clamp(centerX - (size / 2), 0, Math.Max(0, frameWidth - size));
            return new PixelSquareSelection((int)Math.Round(left), (int)Math.Round(top), Math.Max(1, (int)Math.Round(size)));
        }

        private static bool TryComputeOpaqueBounds(BitmapSource source, out Int32Rect bounds)
        {
            bounds = default;
            try
            {
                FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                int minX = width;
                int minY = height;
                int maxX = -1;
                int maxY = -1;
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[rowStart + (x * 4) + 3] <= 8)
                        {
                            continue;
                        }

                        if (x < minX)
                        {
                            minX = x;
                        }

                        if (x > maxX)
                        {
                            maxX = x;
                        }

                        if (y < minY)
                        {
                            minY = y;
                        }

                        if (y > maxY)
                        {
                            maxY = y;
                        }
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    return false;
                }

                bounds = new Int32Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Opened += (_, _) => RebuildContextMenuItems(menu);
            overlayCanvas.ContextMenu = menu;
            ContextMenu = menu;
        }

        private void RebuildContextMenuItems(ContextMenu menu)
        {
            menu.Items.Clear();
            bool hasFrame = frame != null;
            bool hasSelection = selection.HasValue;

            ContextMenuSectionHelper.AddHeader(menu, "Selection", addLeadingSeparator: false);
            menu.Items.Add(CreateMenuItem("Center to image", hasFrame && hasSelection, CenterSelectionToImage));
            menu.Items.Add(CreateMenuItem("Fit largest square to image", hasFrame, FitSelectionToImage));
            menu.Items.Add(CreateMenuItem("Snap to visible content", hasFrame, SnapSelectionToOpaqueContent));
            menu.Items.Add(CreateMenuItem("Center horizontally", hasFrame && hasSelection, () =>
            {
                if (frame == null || !selection.HasValue)
                {
                    return;
                }

                PixelSquareSelection current = selection.Value;
                SetSelection(
                    new PixelSquareSelection((int)Math.Round((frame.PixelWidth - current.Size) / 2.0), current.Y, current.Size),
                    recordUndo: true);
            }));
            menu.Items.Add(CreateMenuItem("Center vertically", hasFrame && hasSelection, () =>
            {
                if (frame == null || !selection.HasValue)
                {
                    return;
                }

                PixelSquareSelection current = selection.Value;
                SetSelection(
                    new PixelSquareSelection(current.X, (int)Math.Round((frame.PixelHeight - current.Size) / 2.0), current.Size),
                    recordUndo: true);
            }));
            menu.Items.Add(CreateMenuItem("Clear selection", hasSelection, () => SetSelection(null, recordUndo: true)));

            ContextMenuSectionHelper.AddHeader(menu, "Clipboard", addLeadingSeparator: true);
            menu.Items.Add(CreateMenuItem("Copy cutout square", hasSelection, () => CopySelectionRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(CreateMenuItem(
                "Paste cutout square",
                hasFrame && (CanPasteSelection?.Invoke() ?? false),
                () => PasteSelectionRequested?.Invoke(this, EventArgs.Empty)));

            ContextMenuSectionHelper.AddHeader(menu, "History", addLeadingSeparator: true);
            menu.Items.Add(CreateMenuItem("Undo (Ctrl+Z)", CanUndo, Undo));
            menu.Items.Add(CreateMenuItem("Redo (Ctrl+Y)", CanRedo, Redo));

            ContextMenuSectionHelper.AddHeader(menu, "View", addLeadingSeparator: true);
            menu.Items.Add(CreateMenuItem("Zoom in", hasFrame, () => ApplyZoom(zoom * 1.25, null)));
            menu.Items.Add(CreateMenuItem("Zoom out", hasFrame, () => ApplyZoom(zoom / 1.25, null)));
            menu.Items.Add(CreateMenuItem("Zoom 100%", hasFrame, () => ApplyZoom(1.0, null)));
            menu.Items.Add(CreateMenuItem("Fit image in view", hasFrame, ZoomToFit));
            menu.Items.Add(CreateCheckableMenuItem(
                "Show image bounds",
                SaveFile.Data.CharacterCreatorViewImageBounds,
                () => boundsCheckBox.IsChecked = SaveFile.Data.CharacterCreatorViewImageBounds != true));
            menu.Items.Add(CreateCheckableMenuItem(
                "Show thirds guides",
                SaveFile.Data.CharacterCreatorCutoutShowGuides,
                () =>
                {
                    SaveFile.Data.CharacterCreatorCutoutShowGuides = !SaveFile.Data.CharacterCreatorCutoutShowGuides;
                    SaveFile.Save();
                    RefreshOverlay();
                }));
            menu.Items.Add(CreateCheckableMenuItem(
                "Dim outside selection",
                SaveFile.Data.CharacterCreatorCutoutDimOutside,
                () =>
                {
                    SaveFile.Data.CharacterCreatorCutoutDimOutside = !SaveFile.Data.CharacterCreatorCutoutDimOutside;
                    SaveFile.Save();
                    RefreshOverlay();
                }));
        }

        private static MenuItem CreateMenuItem(string header, bool enabled, Action action)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                IsEnabled = enabled
            };
            item.Click += (_, _) => action();
            return item;
        }

        private static MenuItem CreateCheckableMenuItem(string header, bool isChecked, Action action)
        {
            MenuItem item = new MenuItem
            {
                Header = header,
                IsCheckable = false,
                IsChecked = isChecked
            };
            item.Click += (_, _) => action();
            return item;
        }
    }
}
