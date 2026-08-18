using System;
using System.ComponentModel;
using System.Windows;
using Common;
using static Common.CustomConsole;

namespace OceanyaClient
{
    /// <summary>
    /// Hosts <see cref="OceanyaWindowContentControl"/> instances inside <see cref="GenericOceanyaWindow"/>.
    /// </summary>
    public static class OceanyaWindowManager
    {
        /// <summary>
        /// Creates a modeless host window for content without showing it.
        /// </summary>
        /// <param name="content">Content control to host.</param>
        /// <param name="options">Window presentation options.</param>
        /// <returns>The created host window instance.</returns>
        public static Window CreateWindow(OceanyaWindowContentControl content, OceanyaWindowPresentationOptions options)
        {
            GenericOceanyaWindow window = CreateHostedWindow(content, options);
            AttachHostedLifecycle(content, window);
            return window;
        }

        /// <summary>
        /// Creates a modeless host window for content without showing it, using default options from content state.
        /// </summary>
        /// <param name="content">Content control to host.</param>
        /// <returns>The created host window instance.</returns>
        public static Window CreateWindow(OceanyaWindowContentControl content)
        {
            OceanyaWindowPresentationOptions options = new OceanyaWindowPresentationOptions
            {
                Owner = content.Owner,
                Title = content.Title,
                HeaderText = content.HeaderText,
                Width = content.Width > 0 ? content.Width : 800,
                Height = content.Height > 0 ? content.Height : 600,
                MinWidth = content.MinWidth,
                MinHeight = content.MinHeight,
                MaxWidth = content.MaxWidth,
                MaxHeight = content.MaxHeight,
                WindowStartupLocation = content.WindowStartupLocation,
                Topmost = content.Topmost,
                ShowInTaskbar = content.ShowInTaskbar,
                IsUserResizeEnabled = content.IsUserResizeEnabled,
                IsUserMoveEnabled = content.IsUserMoveEnabled,
                IsCloseButtonVisible = content.IsCloseButtonVisible,
                BodyMargin = content.BodyMargin,
                IsResizeScalingEnabled = content.IsResizeScalingEnabled
            };

            return CreateWindow(content, options);
        }

        /// <summary>
        /// Shows hosted content as a modal dialog.
        /// </summary>
        /// <param name="content">Content control to host.</param>
        /// <param name="options">Window presentation options.</param>
        /// <returns>Dialog result returned by the hosted window.</returns>
        public static bool? ShowDialog(OceanyaWindowContentControl content, OceanyaWindowPresentationOptions options)
        {
            GenericOceanyaWindow window = CreateHostedWindow(content, options);
            content.AttachHost(window);

            void OnCloseRequested(object? sender, OceanyaWindowCloseRequestedEventArgs eventArgs)
            {
                if (eventArgs.DialogResult.HasValue)
                {
                    try
                    {
                        window.DialogResult = eventArgs.DialogResult;
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // If modal state is unavailable, fall through to a normal close.
                    }
                }

                window.Close();
            }

            content.CloseRequested += OnCloseRequested;

            // The wait form lives on its own STA thread, so WPF cannot keep this dialog above it.
            // Hiding it for the lifetime of the dialog is the reliable fix and covers every Oceanya
            // modal at once, because they all come through here.
            using IDisposable waitFormSuspension = WaitForm.SuspendForDialog();
            try
            {
                return window.ShowDialog();
            }
            finally
            {
                content.CloseRequested -= OnCloseRequested;
                content.DetachHost(window);
            }
        }

        /// <summary>
        /// Shows hosted content modelessly.
        /// </summary>
        /// <param name="content">Content control to host.</param>
        /// <param name="options">Window presentation options.</param>
        /// <returns>The created host window instance.</returns>
        public static Window Show(OceanyaWindowContentControl content, OceanyaWindowPresentationOptions options)
        {
            GenericOceanyaWindow window = CreateHostedWindow(content, options);
            AttachHostedLifecycle(content, window);
            window.Show();
            return window;
        }

        private static GenericOceanyaWindow CreateHostedWindow(OceanyaWindowContentControl content, OceanyaWindowPresentationOptions options)
        {
            GenericOceanyaWindow window = new GenericOceanyaWindow
            {
                Title = options.Title,
                HeaderText = options.HeaderText ?? content.HeaderText,
                Width = options.Width,
                Height = options.Height,
                MinWidth = options.MinWidth,
                MinHeight = options.MinHeight,
                MaxWidth = options.MaxWidth,
                MaxHeight = options.MaxHeight,
                WindowStartupLocation = options.WindowStartupLocation,
                Topmost = options.Topmost,
                ShowInTaskbar = options.ShowInTaskbar,
                IsUserResizeEnabled = options.IsUserResizeEnabled ?? content.IsUserResizeEnabled,
                IsUserMoveEnabled = options.IsUserMoveEnabled ?? content.IsUserMoveEnabled,
                IsCloseButtonVisible = options.IsCloseButtonVisible ?? content.IsCloseButtonVisible,
                BodyMargin = options.BodyMargin ?? content.BodyMargin,
                BodyContent = content,
                IsContentScaleEnabled = options.IsContentScaleEnabled,
                IsResizeScalingEnabled = options.IsResizeScalingEnabled
            };

            if (options.Owner != null)
            {
                window.Owner = options.Owner;
            }

            if (options.Icon != null)
            {
                window.Icon = options.Icon;
            }

            ConfigureHostedContentLayout(content);
            HostedSizingSyncController sizingSyncController = new HostedSizingSyncController(content, window);
            window.Closed += (_, _) => sizingSyncController.Dispose();

            return window;
        }

        private static void ConfigureHostedContentLayout(OceanyaWindowContentControl content)
        {
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.VerticalAlignment = VerticalAlignment.Stretch;
        }

        private static void AttachHostedLifecycle(OceanyaWindowContentControl content, GenericOceanyaWindow window)
        {
            content.AttachHost(window);

            void OnCloseRequested(object? sender, OceanyaWindowCloseRequestedEventArgs eventArgs)
            {
                window.Close();
            }

            content.CloseRequested += OnCloseRequested;
            window.Closed += (_, _) =>
            {
                content.CloseRequested -= OnCloseRequested;
                content.DetachHost(window);
            };
        }

        private sealed class HostedSizingSyncController : IDisposable
        {
            private readonly OceanyaWindowContentControl content;
            private readonly GenericOceanyaWindow window;
            private readonly DependencyPropertyDescriptor? contentWidthDescriptor;
            private readonly DependencyPropertyDescriptor? contentHeightDescriptor;
            private readonly DependencyPropertyDescriptor? contentMinWidthDescriptor;
            private readonly DependencyPropertyDescriptor? contentMinHeightDescriptor;
            private readonly DependencyPropertyDescriptor? contentMaxWidthDescriptor;
            private readonly DependencyPropertyDescriptor? contentMaxHeightDescriptor;
            private readonly DependencyPropertyDescriptor? bodyMarginDescriptor;
            private bool isDisposed;
            private bool suppressContentToWindowSync;
            private bool suppressWindowToContentSync;
            private bool isDrivingWindowSize;

            public HostedSizingSyncController(OceanyaWindowContentControl content, GenericOceanyaWindow window)
            {
                this.content = content;
                this.window = window;

                contentWidthDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.WidthProperty, typeof(FrameworkElement));
                contentHeightDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.HeightProperty, typeof(FrameworkElement));
                contentMinWidthDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.MinWidthProperty, typeof(FrameworkElement));
                contentMinHeightDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.MinHeightProperty, typeof(FrameworkElement));
                contentMaxWidthDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.MaxWidthProperty, typeof(FrameworkElement));
                contentMaxHeightDescriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.MaxHeightProperty, typeof(FrameworkElement));
                bodyMarginDescriptor = DependencyPropertyDescriptor.FromProperty(
                    GenericOceanyaWindow.BodyMarginProperty,
                    typeof(GenericOceanyaWindow));

                contentWidthDescriptor?.AddValueChanged(content, OnContentSizeChanged);
                contentHeightDescriptor?.AddValueChanged(content, OnContentSizeChanged);
                contentMinWidthDescriptor?.AddValueChanged(content, OnContentConstraintsChanged);
                contentMinHeightDescriptor?.AddValueChanged(content, OnContentConstraintsChanged);
                contentMaxWidthDescriptor?.AddValueChanged(content, OnContentConstraintsChanged);
                contentMaxHeightDescriptor?.AddValueChanged(content, OnContentConstraintsChanged);
                bodyMarginDescriptor?.AddValueChanged(window, OnContentConstraintsChanged);

                window.SizeChanged += OnWindowSizeChanged;
                window.StateChanged += OnWindowStateChanged;
                window.ContentScaleChanged += OnWindowContentScaleChanged;
                window.Loaded += OnWindowLoaded;
                window.ContentRendered += OnWindowContentRendered;
                window.IsVisibleChanged += OnWindowIsVisibleChanged;

                ApplyContentConstraintsToWindow();
                ApplyContentSizeToWindow();
                ApplyWindowSizeToContent();
            }

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
                contentWidthDescriptor?.RemoveValueChanged(content, OnContentSizeChanged);
                contentHeightDescriptor?.RemoveValueChanged(content, OnContentSizeChanged);
                contentMinWidthDescriptor?.RemoveValueChanged(content, OnContentConstraintsChanged);
                contentMinHeightDescriptor?.RemoveValueChanged(content, OnContentConstraintsChanged);
                contentMaxWidthDescriptor?.RemoveValueChanged(content, OnContentConstraintsChanged);
                contentMaxHeightDescriptor?.RemoveValueChanged(content, OnContentConstraintsChanged);
                bodyMarginDescriptor?.RemoveValueChanged(window, OnContentConstraintsChanged);
                window.SizeChanged -= OnWindowSizeChanged;
                window.StateChanged -= OnWindowStateChanged;
                window.ContentScaleChanged -= OnWindowContentScaleChanged;
                window.Loaded -= OnWindowLoaded;
                window.ContentRendered -= OnWindowContentRendered;
                window.IsVisibleChanged -= OnWindowIsVisibleChanged;
            }

            /// <summary>
            /// The first sizing pass runs before the window exists on screen, so scale, monitor, and
            /// content sizes are only provisional there. Re-applying once the window is actually laid
            /// out is what a scale change would have done anyway, and is why "it fixes itself as soon
            /// as I change the scale" was the reported symptom.
            /// </summary>
            private void OnWindowLoaded(object sender, RoutedEventArgs e)
            {
                ReapplySizing();
            }

            private void OnWindowContentRendered(object? sender, EventArgs e)
            {
                ReapplySizing();
            }

            /// <summary>
            /// Windows that are hidden and shown again (the launcher hides the initial configuration
            /// window while a functionality runs) never raise Loaded or ContentRendered a second time,
            /// so the scale that changed while they were hidden has to be re-applied on re-show.
            /// </summary>
            private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
            {
                if (e.NewValue is bool isVisible && isVisible)
                {
                    ReapplySizing();
                }
            }

            private void ReapplySizing()
            {
                if (isDisposed || window.IsInteractiveResizeScaling)
                {
                    return;
                }

                window.RefreshContentScaleFromSettings();
                ApplyContentConstraintsToWindow();
                ApplyContentSizeToWindow();
            }

            private void OnWindowContentScaleChanged(object? sender, EventArgs e)
            {
                // Content keeps its logical (unscaled) size; the host window grows or shrinks around it.
                // Do NOT sync window -> content here: the window has not been laid out yet, so its
                // ActualWidth/ActualHeight still describe the previous scale. Dividing that stale size
                // by the new scale inflated (or shrank) the content on every scale change, which then
                // fed back into the next window size - the "window comes back super tall" ratchet.
                double contentWidth = content.Width;
                double contentHeight = content.Height;
                ApplyContentConstraintsToWindow();
                ApplyContentSizeToWindow();
                double requestedWidth = window.Width;
                double requestedHeight = window.Height;

                // A requested size the OS refuses (for example a window taller than the monitor) shows
                // up here as actual != requested, which is the first thing to check on a scaling report.
                window.Dispatcher.BeginInvoke(new Action(() => CustomConsole.Debug(
                    $"[UISCALE] scale={GetContentScale():0.00} content={contentWidth:0}x{contentHeight:0} "
                    + $"requestedWindow={requestedWidth:0}x{requestedHeight:0} "
                    + $"actualWindow={window.ActualWidth:0}x{window.ActualHeight:0} "
                    + $"windowMin={window.MinWidth:0}x{window.MinHeight:0} windowMax={window.MaxWidth:0}x{window.MaxHeight:0} "
                    + $"title=\"{window.Title}\"",
                    CustomConsole.LogCategory.System)));
            }

            private void OnContentSizeChanged(object? sender, EventArgs e)
            {
                if (suppressContentToWindowSync)
                {
                    return;
                }

                // New content size can change what still fits the monitor, so re-resolve the clamp
                // before sizing the window to it.
                window.RefreshContentScaleFromSettings();
                ApplyContentSizeToWindow();
            }

            private void OnContentConstraintsChanged(object? sender, EventArgs e)
            {
                // Content stays authoritative here for the same staleness reason as a scale change.
                ApplyContentConstraintsToWindow();
                ApplyContentSizeToWindow();
            }

            private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
            {
                // isDrivingWindowSize means this resize is the echo of our own content -> window write.
                // Feeding it back would let an OS-clamped size (work area, max track size) rewrite the
                // content size and ratchet the layout on every scale change.
                if (suppressWindowToContentSync || isDrivingWindowSize)
                {
                    return;
                }

                ApplyWindowSizeToContent();
            }

            private void OnWindowStateChanged(object? sender, EventArgs e)
            {
                ApplyWindowSizeToContent();
            }

            private void ApplyContentSizeToWindow()
            {
                if (window.WindowState != WindowState.Normal)
                {
                    return;
                }

                // Writing Width/Height mid-drag would snap the window away from the pointer.
                if (window.IsInteractiveResizeScaling)
                {
                    return;
                }

                (double horizontalOffset, double verticalOffset) = GetChromeOffsets();
                double scale = GetContentScale();

                if (IsFinite(content.Width) && content.Width > 0)
                {
                    double desiredWidth = (content.Width * scale) + horizontalOffset;
                    window.Width = Clamp(desiredWidth, window.MinWidth, window.MaxWidth);
                }

                if (IsFinite(content.Height) && content.Height > 0)
                {
                    double desiredHeight = (content.Height * scale) + verticalOffset;
                    window.Height = Clamp(desiredHeight, window.MinHeight, window.MaxHeight);
                }

                BeginDrivingWindowSize();
            }

            private void ApplyWindowSizeToContent()
            {
                if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
                {
                    return;
                }

                // In resize-scaling mode the window size means "scale", not "content size": the shell
                // already converted the drag into a ContentScale, so the content keeps its logical size.
                if (window.IsResizeScalingEnabled)
                {
                    return;
                }

                (double horizontalOffset, double verticalOffset) = GetChromeOffsets();
                double scale = GetContentScale();
                double contentWidth = Math.Max(0, window.ActualWidth - horizontalOffset) / scale;
                double contentHeight = Math.Max(0, window.ActualHeight - verticalOffset) / scale;

                suppressContentToWindowSync = true;
                try
                {
                    if (!AreClose(content.Width, contentWidth))
                    {
                        content.SetCurrentValue(FrameworkElement.WidthProperty, contentWidth);
                    }

                    if (!AreClose(content.Height, contentHeight))
                    {
                        content.SetCurrentValue(FrameworkElement.HeightProperty, contentHeight);
                    }
                }
                finally
                {
                    suppressContentToWindowSync = false;
                }
            }

            private void ApplyContentConstraintsToWindow()
            {
                (double horizontalOffset, double verticalOffset) = GetChromeOffsets();
                double scale = GetContentScale();

                double minWidth = (Math.Max(0, content.MinWidth) * scale) + horizontalOffset;
                double minHeight = (Math.Max(0, content.MinHeight) * scale) + verticalOffset;
                double maxWidth = double.IsPositiveInfinity(content.MaxWidth)
                    ? double.PositiveInfinity
                    : (Math.Max(0, content.MaxWidth) * scale) + horizontalOffset;
                double maxHeight = double.IsPositiveInfinity(content.MaxHeight)
                    ? double.PositiveInfinity
                    : (Math.Max(0, content.MaxHeight) * scale) + verticalOffset;

                if (IsFinite(maxWidth))
                {
                    maxWidth = Math.Max(maxWidth, minWidth);
                }

                if (IsFinite(maxHeight))
                {
                    maxHeight = Math.Max(maxHeight, minHeight);
                }

                suppressWindowToContentSync = true;
                try
                {
                    window.MinWidth = minWidth;
                    window.MinHeight = minHeight;
                    window.MaxWidth = maxWidth;
                    window.MaxHeight = maxHeight;
                }
                finally
                {
                    suppressWindowToContentSync = false;
                }
            }

            /// <summary>
            /// Marks the window resize we just requested as self-inflicted until layout settles, so the
            /// resulting SizeChanged does not write back into the content size.
            /// </summary>
            private void BeginDrivingWindowSize()
            {
                if (isDrivingWindowSize)
                {
                    return;
                }

                isDrivingWindowSize = true;
                window.Dispatcher.BeginInvoke(
                    new Action(() => isDrivingWindowSize = false),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            private (double HorizontalOffset, double VerticalOffset) GetChromeOffsets()
            {
                return GenericOceanyaWindow.GetChromeOffsets(window.BodyMargin, GetContentScale());
            }

            private double GetContentScale()
            {
                return UiScaleMath.ClampScale(window.ContentScale);
            }

            private static bool IsFinite(double value)
            {
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }

            private static double Clamp(double value, double min, double max)
            {
                double normalizedMin = IsFinite(min) ? min : 0;
                if (!IsFinite(max))
                {
                    return Math.Max(value, normalizedMin);
                }

                return Math.Max(normalizedMin, Math.Min(value, max));
            }

            private static bool AreClose(double left, double right)
            {
                if (double.IsNaN(left) && double.IsNaN(right))
                {
                    return true;
                }

                if (!IsFinite(left) || !IsFinite(right))
                {
                    return false;
                }

                return Math.Abs(left - right) < 0.5d;
            }
        }
    }

    /// <summary>
    /// Presentation options for hosted generic Oceanya windows.
    /// </summary>
    public sealed class OceanyaWindowPresentationOptions
    {
        /// <summary>
        /// Gets or sets the owner window.
        /// </summary>
        public Window? Owner { get; set; }

        /// <summary>
        /// Gets or sets the window title.
        /// </summary>
        public string Title { get; set; } = "Oceanya";

        /// <summary>
        /// Gets or sets the optional header text override.
        /// </summary>
        public string? HeaderText { get; set; }

        /// <summary>
        /// Gets or sets the startup location.
        /// </summary>
        public WindowStartupLocation WindowStartupLocation { get; set; } = WindowStartupLocation.CenterOwner;

        /// <summary>
        /// Gets or sets the window icon.
        /// </summary>
        public System.Windows.Media.ImageSource? Icon { get; set; }

        /// <summary>
        /// Gets or sets the initial window width.
        /// </summary>
        public double Width { get; set; } = 800;

        /// <summary>
        /// Gets or sets the initial window height.
        /// </summary>
        public double Height { get; set; } = 600;

        /// <summary>
        /// Gets or sets the minimum width.
        /// </summary>
        public double MinWidth { get; set; } = 0;

        /// <summary>
        /// Gets or sets the minimum height.
        /// </summary>
        public double MinHeight { get; set; } = 0;

        /// <summary>
        /// Gets or sets the maximum width.
        /// </summary>
        public double MaxWidth { get; set; } = double.PositiveInfinity;

        /// <summary>
        /// Gets or sets the maximum height.
        /// </summary>
        public double MaxHeight { get; set; } = double.PositiveInfinity;

        /// <summary>
        /// Gets or sets a value indicating whether the hosted window should be topmost.
        /// </summary>
        public bool Topmost { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the window appears in the taskbar.
        /// </summary>
        public bool ShowInTaskbar { get; set; } = true;

        /// <summary>
        /// Gets or sets an optional override for host resize behavior.
        /// </summary>
        public bool? IsUserResizeEnabled { get; set; }

        /// <summary>
        /// Gets or sets an optional override for host move behavior.
        /// </summary>
        public bool? IsUserMoveEnabled { get; set; }

        /// <summary>
        /// Gets or sets an optional override for host close-button visibility.
        /// </summary>
        public bool? IsCloseButtonVisible { get; set; }

        /// <summary>
        /// Gets or sets an optional override for host body margin.
        /// </summary>
        public Thickness? BodyMargin { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether dragging the hosted window's edges rescales the
        /// content instead of stretching its layout.
        /// </summary>
        public bool IsResizeScalingEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the hosted window participates in global UI scaling.
        /// Resolution-driven surfaces such as the AO2 viewport opt out.
        /// </summary>
        public bool IsContentScaleEnabled { get; set; } = true;
    }
}
