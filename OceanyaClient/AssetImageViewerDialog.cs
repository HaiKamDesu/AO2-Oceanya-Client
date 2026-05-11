using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Reusable image asset viewer dialog — the same full-featured viewer (zoom, animated timeline,
    /// prev/next navigation, image-bounds overlay) used in the AO2 character creator's file organizer.
    /// Call <see cref="Show"/> from any window to open it for one or more absolute asset paths.
    /// </summary>
    public static class AssetImageViewerDialog
    {
        /// <summary>A single entry shown in the viewer.</summary>
        /// <param name="AbsolutePath">Resolved absolute path to the image file (or null if unresolved).</param>
        /// <param name="Label">Short display name shown in the title row (e.g. file name).</param>
        /// <param name="MetaText">Optional secondary line shown below the label (e.g. relative path).</param>
        /// <param name="FallbackPreview">Optional pre-loaded image used when the path cannot be resolved.</param>
        public record AssetEntry(
            string? AbsolutePath,
            string Label,
            string? MetaText = null,
            ImageSource? FallbackPreview = null);

        /// <summary>
        /// Opens the Image Asset Viewer for the given list of asset entries.
        /// </summary>
        /// <param name="owner">Parent window (used for dialog ownership and centering).</param>
        /// <param name="entries">List of assets to display. Prev/Next navigation is shown when more than one.</param>
        /// <param name="initialIndex">Zero-based index of the entry to show first.</param>
        public static void Show(Window? owner, IReadOnlyList<AssetEntry> entries, int initialIndex = 0)
        {
            if (entries == null || entries.Count == 0) return;
            int currentIndex = Math.Clamp(initialIndex, 0, entries.Count - 1);

            GenericOceanyaWindow dialog = new GenericOceanyaWindow
            {
                Owner = owner,
                Title = "Image Asset Viewer",
                HeaderText = "Image Asset Viewer",
                Width = 1080,
                Height = 760,
                MinWidth = 820,
                MinHeight = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = true,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyMargin = new Thickness(0)
            };

            // — Navigation buttons —
            Button previousButton = MakeNavButton("←");
            Button nextButton = MakeNavButton("→");

            // — Header text —
            TextBlock fileNameText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 4)
            };
            TextBlock fileMetaText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(179, 194, 208)),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            // — Image area —
            ScaleTransform zoomTransform = new ScaleTransform(1, 1);
            Image previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                LayoutTransform = zoomTransform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock emptyText = new TextBlock
            {
                Text = "Preview unavailable",
                Foreground = new SolidColorBrush(Color.FromRgb(192, 205, 218)),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            System.Windows.Shapes.Rectangle imageBoundsRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Canvas imageBoundsCanvas = new Canvas { IsHitTestVisible = false };
            imageBoundsCanvas.Children.Add(imageBoundsRect);

            Grid imageHost = new Grid();
            imageHost.Children.Add(previewImage);
            imageHost.Children.Add(emptyText);
            imageHost.Children.Add(imageBoundsCanvas);

            ScrollViewer imageScrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = imageHost
            };

            // — Zoom —
            bool isMouseWheelZooming = false;
            Slider zoomSlider = new Slider
            {
                Minimum = 0.1,
                Maximum = 8.0,
                Value = 1.0,
                Width = 180,
                TickFrequency = 0.1,
                IsSnapToTickEnabled = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock zoomValueText = new TextBlock
            {
                Text = "100%",
                Foreground = new SolidColorBrush(Color.FromRgb(210, 221, 232)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Button zoomOutButton = MakeSmallButton("-");
            zoomOutButton.Width = 36;
            Button zoomResetButton = MakeSmallButton("100%");
            zoomResetButton.Width = 72;
            Button zoomInButton = MakeSmallButton("+");
            zoomInButton.Width = 36;

            CheckBox viewBoundsCheckBox = new CheckBox
            {
                Content = "View Image Bounds",
                IsChecked = SaveFile.Data.CharacterCreatorViewImageBounds,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 240))
            };

            // — Timeline —
            Button playPauseButton = MakeTimelineButton("▶", "Play or pause the animated preview.");
            Button restartButton = MakeTimelineButton("⟲", "Restart the animated preview.");
            CheckBox loopCheckBox = new CheckBox
            {
                Content = "Loop",
                IsChecked = true,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(214, 224, 235))
            };
            Slider timelineSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Margin = new Thickness(8, 0, 8, 0),
                IsEnabled = false
            };
            Grid timelinePanel = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };
            timelinePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelinePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timelinePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timelinePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(playPauseButton, 0);
            Grid.SetColumn(restartButton, 1);
            Grid.SetColumn(timelineSlider, 2);
            Grid.SetColumn(loopCheckBox, 3);
            timelinePanel.Children.Add(playPauseButton);
            timelinePanel.Children.Add(restartButton);
            timelinePanel.Children.Add(timelineSlider);
            timelinePanel.Children.Add(loopCheckBox);

            // — Preview frame border —
            Border previewFrame = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 84, 106)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 10, 0, 0),
                Child = imageScrollViewer
            };

            // — Layout assembly —
            Grid zoomRow = new Grid();
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zoomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(viewBoundsCheckBox, 0);
            Grid.SetColumn(zoomOutButton, 1);
            Grid.SetColumn(zoomResetButton, 2);
            Grid.SetColumn(zoomInButton, 3);
            Grid.SetColumn(zoomSlider, 4);
            Grid.SetColumn(zoomValueText, 5);
            zoomRow.Children.Add(viewBoundsCheckBox);
            zoomRow.Children.Add(zoomOutButton);
            zoomRow.Children.Add(zoomResetButton);
            zoomRow.Children.Add(zoomInButton);
            zoomRow.Children.Add(zoomSlider);
            zoomRow.Children.Add(zoomValueText);

            StackPanel titleStack = new StackPanel();
            titleStack.Children.Add(fileNameText);
            titleStack.Children.Add(fileMetaText);

            Grid headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(titleStack, 0);
            Grid.SetColumn(zoomRow, 1);
            headerRow.Children.Add(titleStack);
            headerRow.Children.Add(zoomRow);

            Grid centerContent = new Grid();
            centerContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            centerContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            centerContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(headerRow, 0);
            Grid.SetRow(previewFrame, 1);
            Grid.SetRow(timelinePanel, 2);
            centerContent.Children.Add(headerRow);
            centerContent.Children.Add(previewFrame);
            centerContent.Children.Add(timelinePanel);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(previousButton, 0);
            Grid.SetColumn(centerContent, 1);
            Grid.SetColumn(nextButton, 2);
            body.Children.Add(previousButton);
            body.Children.Add(centerContent);
            body.Children.Add(nextButton);

            Border outerPanel = new Border
            {
                Margin = new Thickness(10),
                Background = new SolidColorBrush(Color.FromArgb(160, 16, 16, 16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(47, 74, 94)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Child = body
            };
            dialog.BodyMargin = new Thickness(0);
            dialog.BodyContent = outerPanel;

            // — State + helpers —
            AnimationTimelinePreviewController? activeController = null;
            bool ignoreTimelineValueChange = false;

            void UpdateBoundsOverlay()
            {
                if (viewBoundsCheckBox.IsChecked != true || previewImage.Source is not BitmapSource bmp)
                {
                    imageBoundsRect.Visibility = Visibility.Collapsed;
                    return;
                }

                try
                {
                    double imgW = bmp.PixelWidth;
                    double imgH = bmp.PixelHeight;
                    double elemW = previewImage.ActualWidth;
                    double elemH = previewImage.ActualHeight;
                    if (imgW <= 0 || imgH <= 0 || elemW <= 0 || elemH <= 0)
                    {
                        imageBoundsRect.Visibility = Visibility.Collapsed;
                        return;
                    }

                    double scale = Math.Min(elemW / imgW, elemH / imgH);
                    double contentW = imgW * scale;
                    double contentH = imgH * scale;
                    double offsetXInElem = (elemW - contentW) / 2.0;
                    double offsetYInElem = (elemH - contentH) / 2.0;

                    Point topLeft = previewImage.TranslatePoint(new Point(offsetXInElem, offsetYInElem), imageBoundsCanvas);
                    Point bottomRight = previewImage.TranslatePoint(
                        new Point(offsetXInElem + contentW, offsetYInElem + contentH), imageBoundsCanvas);

                    double rectW = Math.Max(0, bottomRight.X - topLeft.X);
                    double rectH = Math.Max(0, bottomRight.Y - topLeft.Y);
                    if (rectW < 1 || rectH < 1)
                    {
                        imageBoundsRect.Visibility = Visibility.Collapsed;
                        return;
                    }

                    Canvas.SetLeft(imageBoundsRect, topLeft.X);
                    Canvas.SetTop(imageBoundsRect, topLeft.Y);
                    imageBoundsRect.Width = rectW;
                    imageBoundsRect.Height = rectH;
                    imageBoundsRect.Visibility = Visibility.Visible;
                }
                catch
                {
                    imageBoundsRect.Visibility = Visibility.Collapsed;
                }
            }

            void DisposeActiveController()
            {
                activeController?.Dispose();
                activeController = null;
            }

            void RefreshEntry()
            {
                DisposeActiveController();
                AssetEntry entry = entries[currentIndex];
                string? resolvedPath = entry.AbsolutePath;

                AnimationTimelinePreviewController? controller = null;
                bool hasAnimated = !string.IsNullOrWhiteSpace(resolvedPath)
                    && Ao2AnimationPreview.IsPotentialAnimatedPath(resolvedPath)
                    && AnimationTimelinePreviewController.TryCreate(resolvedPath, out controller)
                    && controller != null;

                fileNameText.Text = entry.Label;
                fileMetaText.Text = entry.MetaText ?? resolvedPath ?? string.Empty;
                previousButton.IsEnabled = currentIndex > 0;
                nextButton.IsEnabled = currentIndex < entries.Count - 1;
                previousButton.Visibility = entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                nextButton.Visibility = entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                zoomSlider.Value = 1.0;
                imageScrollViewer.ScrollToHorizontalOffset(0);
                imageScrollViewer.ScrollToVerticalOffset(0);

                if (hasAnimated)
                {
                    activeController = controller;
                    previewImage.Source = controller!.CurrentFrame;
                    emptyText.Visibility = Visibility.Collapsed;
                    timelinePanel.Visibility = Visibility.Visible;
                    timelineSlider.IsEnabled = controller.HasTimeline;
                    timelineSlider.Maximum = Math.Max(1, controller.EffectiveDurationMs);
                    timelineSlider.Value = 0;
                    fileMetaText.Text = (entry.MetaText ?? resolvedPath ?? string.Empty)
                        + $" (Animated — {Math.Round(controller.EffectiveDurationMs):0} ms)";
                    controller.PositionChanged += (frame, positionMs) => dialog.Dispatcher.Invoke(() =>
                    {
                        previewImage.Source = frame;
                        ignoreTimelineValueChange = true;
                        timelineSlider.Value = Math.Clamp(positionMs, 0, timelineSlider.Maximum);
                        ignoreTimelineValueChange = false;
                    });
                    controller.PlaybackStateChanged += isPlaying => dialog.Dispatcher.Invoke(() =>
                    {
                        playPauseButton.Content = isPlaying ? "⏸" : "▶";
                    });
                    controller.SetLoop(loopCheckBox.IsChecked == true);
                    playPauseButton.Content = "▶";
                }
                else
                {
                    previewImage.Source = !string.IsNullOrWhiteSpace(resolvedPath)
                        ? Ao2AnimationPreview.LoadStaticPreviewImage(resolvedPath, decodePixelWidth: 0)
                        : entry.FallbackPreview;
                    emptyText.Visibility = previewImage.Source == null ? Visibility.Visible : Visibility.Collapsed;
                    timelinePanel.Visibility = Visibility.Collapsed;
                    timelineSlider.IsEnabled = false;
                    playPauseButton.Content = "▶";
                }

                dialog.Dispatcher.BeginInvoke(UpdateBoundsOverlay, DispatcherPriority.Background);
            }

            // — Event wiring —
            imageScrollViewer.PreviewMouseWheel += (_, e) =>
            {
                double oldZoom = zoomTransform.ScaleX;
                double delta = e.Delta > 0 ? 0.1 : -0.1;
                double nextZoom = Math.Clamp(oldZoom + delta, 0.1, 8.0);
                Point mousePos = e.GetPosition(imageScrollViewer);
                double oldH = imageScrollViewer.HorizontalOffset;
                double oldV = imageScrollViewer.VerticalOffset;
                zoomTransform.ScaleX = nextZoom;
                zoomTransform.ScaleY = nextZoom;
                zoomValueText.Text = $"{Math.Round(nextZoom * 100):0}%";
                isMouseWheelZooming = true;
                zoomSlider.Value = nextZoom;
                isMouseWheelZooming = false;
                double ratio = nextZoom / oldZoom;
                double newH = (mousePos.X + oldH) * ratio - mousePos.X;
                double newV = (mousePos.Y + oldV) * ratio - mousePos.Y;
                dialog.Dispatcher.BeginInvoke(() =>
                {
                    imageScrollViewer.ScrollToHorizontalOffset(newH);
                    imageScrollViewer.ScrollToVerticalOffset(newV);
                    UpdateBoundsOverlay();
                }, DispatcherPriority.Background);
                e.Handled = true;
            };

            Point panStartPoint = default;
            double panStartH = 0;
            double panStartV = 0;
            bool isPanning = false;
            imageScrollViewer.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (previewImage.Source == null) return;
                isPanning = true;
                panStartPoint = e.GetPosition(imageScrollViewer);
                panStartH = imageScrollViewer.HorizontalOffset;
                panStartV = imageScrollViewer.VerticalOffset;
                imageScrollViewer.Cursor = Cursors.SizeAll;
                imageScrollViewer.CaptureMouse();
                e.Handled = true;
            };
            imageScrollViewer.PreviewMouseMove += (_, e) =>
            {
                if (!isPanning || e.LeftButton != MouseButtonState.Pressed) return;
                Point current = e.GetPosition(imageScrollViewer);
                Vector delta = current - panStartPoint;
                imageScrollViewer.ScrollToHorizontalOffset(Math.Max(0, panStartH - delta.X));
                imageScrollViewer.ScrollToVerticalOffset(Math.Max(0, panStartV - delta.Y));
                e.Handled = true;
            };
            imageScrollViewer.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!isPanning) return;
                isPanning = false;
                imageScrollViewer.ReleaseMouseCapture();
                imageScrollViewer.Cursor = Cursors.Hand;
                e.Handled = true;
            };
            imageScrollViewer.MouseLeave += (_, _) =>
            {
                if (!isPanning) imageScrollViewer.Cursor = Cursors.Hand;
            };

            zoomSlider.ValueChanged += (_, _) =>
            {
                double oldZoom = zoomTransform.ScaleX;
                double zoom = Math.Clamp(zoomSlider.Value, 0.1, 8.0);
                zoomTransform.ScaleX = zoom;
                zoomTransform.ScaleY = zoom;
                zoomValueText.Text = $"{Math.Round(zoom * 100):0}%";
                if (!isMouseWheelZooming)
                {
                    double pivotX = imageScrollViewer.ViewportWidth / 2;
                    double pivotY = imageScrollViewer.ViewportHeight / 2;
                    double oldH = imageScrollViewer.HorizontalOffset;
                    double oldV = imageScrollViewer.VerticalOffset;
                    double ratio = zoom / (oldZoom > 0 ? oldZoom : 1);
                    double newH = (pivotX + oldH) * ratio - pivotX;
                    double newV = (pivotY + oldV) * ratio - pivotY;
                    dialog.Dispatcher.BeginInvoke(() =>
                    {
                        imageScrollViewer.ScrollToHorizontalOffset(newH);
                        imageScrollViewer.ScrollToVerticalOffset(newV);
                        UpdateBoundsOverlay();
                    }, DispatcherPriority.Background);
                }
                else
                {
                    dialog.Dispatcher.BeginInvoke(UpdateBoundsOverlay, DispatcherPriority.Background);
                }
            };

            zoomOutButton.Click += (_, _) => zoomSlider.Value = Math.Max(zoomSlider.Minimum, zoomSlider.Value - 0.1);
            zoomInButton.Click += (_, _) => zoomSlider.Value = Math.Min(zoomSlider.Maximum, zoomSlider.Value + 0.1);
            zoomResetButton.Click += (_, _) => zoomSlider.Value = 1.0;

            viewBoundsCheckBox.Checked += (_, _) =>
            {
                SaveFile.Data.CharacterCreatorViewImageBounds = true;
                SaveFile.Save();
                dialog.Dispatcher.BeginInvoke(UpdateBoundsOverlay, DispatcherPriority.Background);
            };
            viewBoundsCheckBox.Unchecked += (_, _) =>
            {
                SaveFile.Data.CharacterCreatorViewImageBounds = false;
                SaveFile.Save();
                imageBoundsRect.Visibility = Visibility.Collapsed;
            };
            previewImage.SizeChanged += (_, _) =>
                dialog.Dispatcher.BeginInvoke(UpdateBoundsOverlay, DispatcherPriority.Background);

            previousButton.Click += (_, _) =>
            {
                if (currentIndex <= 0) return;
                currentIndex--;
                RefreshEntry();
            };
            nextButton.Click += (_, _) =>
            {
                if (currentIndex >= entries.Count - 1) return;
                currentIndex++;
                RefreshEntry();
            };
            playPauseButton.Click += (_, _) =>
            {
                if (activeController == null) return;
                if (activeController.IsPlaying) activeController.Pause();
                else activeController.Play();
            };
            restartButton.Click += (_, _) =>
            {
                if (activeController == null) return;
                activeController.Seek(0);
                activeController.Play();
            };
            loopCheckBox.Checked += (_, _) => activeController?.SetLoop(true);
            loopCheckBox.Unchecked += (_, _) => activeController?.SetLoop(false);
            timelineSlider.ValueChanged += (_, _) =>
            {
                if (ignoreTimelineValueChange || activeController == null) return;
                activeController.Seek(timelineSlider.Value);
            };

            dialog.Closed += (_, _) => DisposeActiveController();

            RefreshEntry();
            dialog.ShowDialog();
        }

        private static Button MakeNavButton(string text)
        {
            Button btn = new Button
            {
                Content = text,
                Width = 54,
                Height = 54,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(38, 55, 72)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(86, 116, 146)),
                BorderThickness = new Thickness(1)
            };
            return btn;
        }

        private static Button MakeSmallButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 30,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(38, 55, 72)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(86, 116, 146)),
                BorderThickness = new Thickness(1)
            };
        }

        private static Button MakeTimelineButton(string symbol, string toolTip)
        {
            return new Button
            {
                Content = symbol,
                ToolTip = toolTip,
                Width = 36,
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(38, 55, 72)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(86, 116, 146)),
                BorderThickness = new Thickness(1)
            };
        }
    }
}
