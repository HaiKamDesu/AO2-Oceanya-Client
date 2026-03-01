using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OceanyaClient
{
    /// <summary>
    /// Base type for controls hosted inside <see cref="GenericOceanyaWindow"/>.
    /// </summary>
    public abstract class OceanyaWindowContentControl : UserControl
    {
        private Window? hostWindow;
        private Window? pendingOwner;
        private bool suppressHostCloseNotification;
        private double pendingLeft = double.NaN;
        private double pendingTop = double.NaN;
        private WindowState? pendingWindowState;
        private string title = "Oceanya";
        private bool topmost;
        private bool showInTaskbar = true;
        private WindowStartupLocation windowStartupLocation = WindowStartupLocation.CenterOwner;

        /// <summary>
        /// Raised when hosted content requests the host window to close.
        /// </summary>
        public event EventHandler<OceanyaWindowCloseRequestedEventArgs>? CloseRequested;

        /// <summary>
        /// Raised when the host window is closing.
        /// </summary>
        public event CancelEventHandler? Closing;

        /// <summary>
        /// Raised when the host window state changes.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Raised when the host source is initialized.
        /// </summary>
        public event EventHandler? SourceInitialized;

        /// <summary>
        /// Raised when the host window is closed.
        /// </summary>
        public event EventHandler? Closed;

        /// <summary>
        /// Gets the header text to display in the shared generic window.
        /// </summary>
        public virtual string HeaderText => "Oceanya";

        /// <summary>
        /// Gets a value indicating whether the host window is user-resizable.
        /// </summary>
        public virtual bool IsUserResizeEnabled => false;

        /// <summary>
        /// Gets a value indicating whether the host window can be moved by dragging the header.
        /// </summary>
        public virtual bool IsUserMoveEnabled => true;

        /// <summary>
        /// Gets a value indicating whether the host close button is visible.
        /// </summary>
        public virtual bool IsCloseButtonVisible => true;

        /// <summary>
        /// Gets the body content margin used by the shared generic window.
        /// </summary>
        public virtual Thickness BodyMargin => new Thickness(0);

        /// <summary>
        /// Gets the current host window instance, if attached.
        /// </summary>
        public Window? HostWindow => hostWindow ?? Window.GetWindow(this);

        /// <summary>
        /// Gets or sets the window title used by the host.
        /// </summary>
        public string Title
        {
            get => HostWindow?.Title ?? title;
            set
            {
                title = value;
                if (HostWindow != null)
                {
                    HostWindow.Title = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the owner window used when shown modally.
        /// </summary>
        public Window? Owner
        {
            get => HostWindow?.Owner ?? pendingOwner;
            set
            {
                pendingOwner = value;
                if (HostWindow != null)
                {
                    HostWindow.Owner = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the window state for the host.
        /// </summary>
        public WindowState WindowState
        {
            get => HostWindow?.WindowState ?? pendingWindowState ?? WindowState.Normal;
            set
            {
                pendingWindowState = value;
                if (HostWindow != null)
                {
                    HostWindow.WindowState = value;
                }
            }
        }

        /// <summary>
        /// Gets the host restore bounds.
        /// </summary>
        public Rect RestoreBounds => HostWindow?.RestoreBounds ?? new Rect(Left, Top, Width, Height);

        /// <summary>
        /// Gets or sets the host left position.
        /// </summary>
        public double Left
        {
            get => HostWindow?.Left ?? pendingLeft;
            set
            {
                pendingLeft = value;
                if (HostWindow != null)
                {
                    HostWindow.Left = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the host top position.
        /// </summary>
        public double Top
        {
            get => HostWindow?.Top ?? pendingTop;
            set
            {
                pendingTop = value;
                if (HostWindow != null)
                {
                    HostWindow.Top = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the host window is topmost.
        /// </summary>
        public bool Topmost
        {
            get => HostWindow?.Topmost ?? topmost;
            set
            {
                topmost = value;
                if (HostWindow != null)
                {
                    HostWindow.Topmost = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the host window is visible in taskbar.
        /// </summary>
        public bool ShowInTaskbar
        {
            get => HostWindow?.ShowInTaskbar ?? showInTaskbar;
            set
            {
                showInTaskbar = value;
                if (HostWindow != null)
                {
                    HostWindow.ShowInTaskbar = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the host icon.
        /// </summary>
        public ImageSource? Icon
        {
            get => HostWindow?.Icon;
            set
            {
                if (HostWindow != null)
                {
                    HostWindow.Icon = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the startup location used by the host window.
        /// </summary>
        public WindowStartupLocation WindowStartupLocation
        {
            get => HostWindow?.WindowStartupLocation ?? windowStartupLocation;
            set
            {
                windowStartupLocation = value;
                if (HostWindow != null)
                {
                    HostWindow.WindowStartupLocation = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the host dialog result when shown modally.
        /// </summary>
        public bool? DialogResult
        {
            get => HostWindow?.DialogResult;
            set
            {
                if (HostWindow != null)
                {
                    try
                    {
                        HostWindow.DialogResult = value;
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // Falls back to close request for non-modal or unavailable dialog states.
                    }
                }

                RequestHostClose(value);
            }
        }

        /// <summary>
        /// Shows this content in a hosted generic Oceanya window.
        /// </summary>
        public void Show()
        {
            if (HostWindow != null)
            {
                HostWindow.Show();
                return;
            }

            _ = OceanyaWindowManager.Show(this, BuildDefaultPresentationOptions(false));
        }

        /// <summary>
        /// Shows this content as a modal dialog in a hosted generic Oceanya window.
        /// </summary>
        public bool? ShowDialog()
        {
            if (HostWindow != null)
            {
                return HostWindow.ShowDialog();
            }

            return OceanyaWindowManager.ShowDialog(this, BuildDefaultPresentationOptions(true));
        }

        /// <summary>
        /// Closes the host window.
        /// </summary>
        public void Close()
        {
            if (HostWindow == null)
            {
                return;
            }

            suppressHostCloseNotification = true;
            HostWindow.Close();
        }

        /// <summary>
        /// Hides the host window.
        /// </summary>
        public void Hide()
        {
            HostWindow?.Hide();
        }

        /// <summary>
        /// Activates the host window.
        /// </summary>
        public bool Activate()
        {
            return HostWindow?.Activate() ?? false;
        }

        /// <summary>
        /// Performs a host window drag move operation.
        /// </summary>
        public void DragMove()
        {
            HostWindow?.DragMove();
        }

        internal void AttachHost(Window window)
        {
            hostWindow = window;
            hostWindow.Closing += HostWindow_Closing;
            hostWindow.StateChanged += HostWindow_StateChanged;
            hostWindow.SourceInitialized += HostWindow_SourceInitialized;
            hostWindow.Closed += HostWindow_Closed;

            hostWindow.Title = title;
            hostWindow.Topmost = topmost;
            hostWindow.ShowInTaskbar = showInTaskbar;
            if (pendingOwner != null)
            {
                hostWindow.Owner = pendingOwner;
            }

            if (!double.IsNaN(pendingLeft))
            {
                hostWindow.Left = pendingLeft;
            }

            if (!double.IsNaN(pendingTop))
            {
                hostWindow.Top = pendingTop;
            }

            if (pendingWindowState.HasValue)
            {
                hostWindow.WindowState = pendingWindowState.Value;
            }
        }

        internal void DetachHost(Window window)
        {
            if (hostWindow == window)
            {
                hostWindow.Closing -= HostWindow_Closing;
                hostWindow.StateChanged -= HostWindow_StateChanged;
                hostWindow.SourceInitialized -= HostWindow_SourceInitialized;
                hostWindow.Closed -= HostWindow_Closed;
                hostWindow = null;
            }
        }

        /// <summary>
        /// Requests the host window to close.
        /// </summary>
        /// <param name="dialogResult">Optional dialog result value to set when hosted modally.</param>
        protected void RequestHostClose(bool? dialogResult = null)
        {
            CloseRequested?.Invoke(this, new OceanyaWindowCloseRequestedEventArgs(dialogResult));
        }

        private void HostWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!suppressHostCloseNotification)
            {
                Closing?.Invoke(sender, e);
            }

            suppressHostCloseNotification = false;
        }

        private void HostWindow_StateChanged(object? sender, EventArgs e)
        {
            StateChanged?.Invoke(sender, e);
        }

        private void HostWindow_SourceInitialized(object? sender, EventArgs e)
        {
            SourceInitialized?.Invoke(sender, e);
        }

        private void HostWindow_Closed(object? sender, EventArgs e)
        {
            Closed?.Invoke(sender, e);
        }

        private OceanyaWindowPresentationOptions BuildDefaultPresentationOptions(bool modal)
        {
            WindowStartupLocation startupLocation = modal
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;

            return new OceanyaWindowPresentationOptions
            {
                Owner = pendingOwner,
                Title = title,
                HeaderText = HeaderText,
                Width = Width > 0 ? Width : 800,
                Height = Height > 0 ? Height : 600,
                MinWidth = MinWidth,
                MinHeight = MinHeight,
                MaxWidth = MaxWidth,
                MaxHeight = MaxHeight,
                Topmost = topmost,
                ShowInTaskbar = showInTaskbar,
                IsUserResizeEnabled = IsUserResizeEnabled,
                IsUserMoveEnabled = IsUserMoveEnabled,
                IsCloseButtonVisible = IsCloseButtonVisible,
                BodyMargin = BodyMargin,
                WindowStartupLocation = windowStartupLocation == WindowStartupLocation.CenterOwner
                    || windowStartupLocation == WindowStartupLocation.CenterScreen
                    || windowStartupLocation == WindowStartupLocation.Manual
                    ? windowStartupLocation
                    : startupLocation
            };
        }

        /// <summary>
        /// Converts hosted content to its current host window when needed by existing APIs.
        /// </summary>
        public static implicit operator Window?(OceanyaWindowContentControl? control)
        {
            return control?.HostWindow;
        }
    }

    /// <summary>
    /// Close request payload raised by <see cref="OceanyaWindowContentControl"/>.
    /// </summary>
    public sealed class OceanyaWindowCloseRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OceanyaWindowCloseRequestedEventArgs"/> class.
        /// </summary>
        /// <param name="dialogResult">Optional dialog result to apply before close.</param>
        public OceanyaWindowCloseRequestedEventArgs(bool? dialogResult)
        {
            DialogResult = dialogResult;
        }

        /// <summary>
        /// Gets the dialog result value requested for the host.
        /// </summary>
        public bool? DialogResult { get; }
    }
}
