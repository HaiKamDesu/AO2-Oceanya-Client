using System;
using System.ComponentModel;
using System.Windows;

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
                BodyMargin = content.BodyMargin
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
                BodyContent = content
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
            }

            private void OnContentSizeChanged(object? sender, EventArgs e)
            {
                if (suppressContentToWindowSync)
                {
                    return;
                }

                ApplyContentSizeToWindow();
            }

            private void OnContentConstraintsChanged(object? sender, EventArgs e)
            {
                ApplyContentConstraintsToWindow();
                ApplyContentSizeToWindow();
                ApplyWindowSizeToContent();
            }

            private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
            {
                if (suppressWindowToContentSync)
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

                (double horizontalOffset, double verticalOffset) = GetChromeOffsets();

                if (IsFinite(content.Width) && content.Width > 0)
                {
                    double desiredWidth = content.Width + horizontalOffset;
                    window.Width = Clamp(desiredWidth, window.MinWidth, window.MaxWidth);
                }

                if (IsFinite(content.Height) && content.Height > 0)
                {
                    double desiredHeight = content.Height + verticalOffset;
                    window.Height = Clamp(desiredHeight, window.MinHeight, window.MaxHeight);
                }
            }

            private void ApplyWindowSizeToContent()
            {
                if (window.ActualWidth <= 0 || window.ActualHeight <= 0)
                {
                    return;
                }

                (double horizontalOffset, double verticalOffset) = GetChromeOffsets();
                double contentWidth = Math.Max(0, window.ActualWidth - horizontalOffset);
                double contentHeight = Math.Max(0, window.ActualHeight - verticalOffset);

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

                double minWidth = Math.Max(0, content.MinWidth) + horizontalOffset;
                double minHeight = Math.Max(0, content.MinHeight) + verticalOffset;
                double maxWidth = double.IsPositiveInfinity(content.MaxWidth)
                    ? double.PositiveInfinity
                    : Math.Max(0, content.MaxWidth) + horizontalOffset;
                double maxHeight = double.IsPositiveInfinity(content.MaxHeight)
                    ? double.PositiveInfinity
                    : Math.Max(0, content.MaxHeight) + verticalOffset;

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

            private (double HorizontalOffset, double VerticalOffset) GetChromeOffsets()
            {
                Thickness bodyMargin = window.BodyMargin;
                double horizontalOffset =
                    (GenericOceanyaWindow.SharedFrameBorderThickness * 2)
                    + bodyMargin.Left
                    + bodyMargin.Right;
                double verticalOffset =
                    GenericOceanyaWindow.SharedHeaderHeight
                    + (GenericOceanyaWindow.SharedFrameBorderThickness * 2)
                    + bodyMargin.Top
                    + bodyMargin.Bottom;

                return (horizontalOffset, verticalOffset);
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
    }
}
