using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for WaitForm.xaml
    /// </summary>
    public partial class WaitForm : Window
    {
        public static bool Showing = false;
        private static WaitForm? _instance;
        private static Thread? _uiThread;
        private static Dispatcher? _formDispatcher;
        private static readonly object _lock = new object();
        private static TaskCompletionSource<bool>? _initializationTcs;
        private static string _currentTitle = "";
        private static string _currentSubtitle = "";
        private static Window? _ownerWindow;
        private static IntPtr _ownerHandle = IntPtr.Zero;
        private static Rect _ownerBounds = Rect.Empty;
        private static bool _threadRunning = false;
        private static int _suspendCount;

        private WaitForm()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; // Default to center screen

            // Set initial values
            lblMessage.Text = _currentTitle;
            lblSubtitle.Text = _currentSubtitle;
            lblSubtitle.Visibility = Visibility.Collapsed;

            // Fixed size window
            Width = 300;
            Height = 120;
            MinWidth = 300;
            MinHeight = 120;

            // Set up window close event
            Closed += (s, e) =>
            {
                Showing = false;
                _instance = null;
            };
        }

        private static void StartFormOnNewThread()
        {
            lock (_lock)
            {
                // If thread is already running, just signal completion
                if (_threadRunning)
                {
                    _initializationTcs = new TaskCompletionSource<bool>();
                    _initializationTcs.SetResult(true);
                    return;
                }

                _initializationTcs = new TaskCompletionSource<bool>();

                // Create and start a new UI thread
                _uiThread = new Thread(() =>
                {
                    try
                    {
                        _threadRunning = true;

                        // Create STA thread for WPF
                        SynchronizationContext.SetSynchronizationContext(
                            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                        _formDispatcher = Dispatcher.CurrentDispatcher;

                        // Signal that initialization is complete
                        _initializationTcs?.SetResult(true);

                        // Start dispatcher
                        Dispatcher.Run();
                    }
                    finally
                    {
                        _threadRunning = false;
                    }
                });

                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.IsBackground = true;
                _uiThread.Start();

                // Wait for initialization to complete
                _initializationTcs?.Task.Wait();
            }
        }

        public static async Task ShowFormAsync(string message, Window owner)
        {
            if (OceanyaTestMode.Current.DisableWaitForms)
            {
                Showing = false;
                return;
            }

            _currentTitle = message;
            _currentSubtitle = string.Empty;
            _ownerWindow = owner;
            _ownerHandle = ResolveOwnerHandle(owner);
            _ownerBounds = ResolveOwnerBounds(owner);

            // Start the UI thread if needed
            StartFormOnNewThread();

            // Use the form's dispatcher to show it
            if (_formDispatcher == null)
            {
                return;
            }

            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance == null)
                {
                    _instance = new WaitForm();
                }

                _instance.BeginAnimation(Window.OpacityProperty, null);
                _instance.Opacity = 1;
                _instance.lblMessage.Text = message;
                _instance.lblSubtitle.Text = string.Empty;
                _instance.lblSubtitle.Visibility = Visibility.Collapsed;

                // Adjust the window size based on the new message length
                _instance.ResizeWindow();

                // Try to position relative to owner if possible
                if (owner != null && owner.IsVisible)
                {
                    _instance.Owner = null; // Can't own windows across threads

                    try
                    {
                        if (!TryCenterRelativeToBounds(_instance, _ownerBounds)
                            && !TryCenterRelativeToOwner(_instance, owner))
                        {
                            _instance.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        }
                    }
                    catch
                    {
                        _instance.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }
                }

                if (!_instance.IsVisible)
                {
                    _instance.Show();
                    Showing = true;
                }

                _instance.Activate();
                _instance.Focus();
                _instance.UpdateLayout();
            });

            await _formDispatcher.InvokeAsync(() =>
            {
                _instance?.UpdateLayout();
            }, DispatcherPriority.Render);
        }

        public static async Task CloseFormAsync()
        {
            if (OceanyaTestMode.Current.DisableWaitForms)
            {
                Showing = false;
                return;
            }

            if (_formDispatcher == null) return;

            // Use the form's dispatcher to close it
            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance != null)
                {
                    _instance.BeginAnimation(Window.OpacityProperty, null);
                    _instance.Opacity = 1;

                    // Hand activation back to the owner BEFORE the form goes away. The wait form is a
                    // top-level window on its own thread with no Win32 owner, so when it closes while it
                    // holds the foreground, Windows picks the next window in the global Z-order - often a
                    // different process, or one of WPF's zero-sized per-thread helper windows. The app then
                    // drops behind everything, which reads to the user as "it minimized itself".
                    TryRestoreOwnerForeground();

                    try
                    {
                        _instance.Close();
                    }
                    catch
                    {
                        // Best-effort cleanup if the close path runs while the form is already disposing.
                    }
                }

                Showing = false;
            });
        }

        public static void CloseForm()
        {
            // For backward compatibility
            _ = CloseFormAsync();
        }

        /// <summary>
        /// Reports whether the wait form is currently hidden behind one or more suspensions.
        /// </summary>
        public static bool IsSuspended => Volatile.Read(ref _suspendCount) > 0;

        /// <summary>
        /// Hides the wait form for as long as a modal dialog is on screen, then restores it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The wait form runs on its own STA thread, so WPF cannot own it from the main window and
        /// cannot keep a main-thread modal dialog above it. It is also explicitly activated when shown.
        /// The result was a wait form sitting on top of the dialog it was supposed to yield to - most
        /// visibly the snapshot INI-puppet conflict prompt during "Connecting to server and restoring
        /// clients...", which the user could not read or reach.
        /// </para>
        /// <para>
        /// Rather than fight z-order across threads, the form simply steps aside: it hides while any
        /// dialog is up and comes back afterwards if the operation is still running. Suspensions nest,
        /// so a dialog opened from inside another dialog restores correctly.
        /// </para>
        /// </remarks>
        /// <returns>A scope that restores the wait form when disposed.</returns>
        public static IDisposable SuspendForDialog() => new WaitFormSuspension();

        /// <summary>
        /// Subscribes to the calling thread's modal-loop notifications so ANY modal dialog on that
        /// thread hides the wait form, not just the ones routed through <c>OceanyaWindowManager</c>.
        /// </summary>
        /// <remarks>
        /// WPF raises these around every <c>Window.ShowDialog()</c> and <c>MessageBox.Show(...)</c>,
        /// which is the only practical way to cover the couple hundred direct dialog call sites in this
        /// codebase (and any added later) without touching each one.
        /// </remarks>
        public static void HookThreadModalDialogs()
        {
            ComponentDispatcher.EnterThreadModal += OnEnterThreadModal;
            ComponentDispatcher.LeaveThreadModal += OnLeaveThreadModal;
        }

        private static void OnEnterThreadModal(object? sender, EventArgs e) => Suspend();

        private static void OnLeaveThreadModal(object? sender, EventArgs e) => Resume();

        private static void Suspend()
        {
            if (OceanyaTestMode.Current.DisableWaitForms || _formDispatcher == null)
            {
                Interlocked.Increment(ref _suspendCount);
                return;
            }

            if (Interlocked.Increment(ref _suspendCount) != 1)
            {
                return; // Already hidden by an outer dialog.
            }

            try
            {
                // Non-blocking on purpose: this runs from inside a modal-entry callback on the main UI
                // thread, and a blocking Invoke into another UI thread from there is a deadlock waiting
                // to happen. Ordering is still guaranteed because hide and show queue on the same
                // dispatcher.
                _formDispatcher.InvokeAsync(() =>
                {
                    if (_instance != null && _instance.IsVisible)
                    {
                        // Same foreground-void problem as the close path: hiding the form while it holds
                        // the foreground lets Windows hand activation to whatever is behind the app.
                        TryRestoreOwnerForeground();
                        _instance.Hide();
                    }
                });
            }
            catch
            {
                // A dispatcher shutting down mid-suspend is not actionable; the dialog still shows.
            }
        }

        private static void Resume()
        {
            if (Interlocked.Decrement(ref _suspendCount) != 0)
            {
                return; // An outer dialog is still open.
            }

            if (OceanyaTestMode.Current.DisableWaitForms || _formDispatcher == null)
            {
                return;
            }

            try
            {
                _formDispatcher.InvokeAsync(() =>
                {
                    // Only restore if the operation is still running: CloseFormAsync may have run while
                    // the dialog was up, in which case the form must stay gone. Re-checked here rather
                    // than at suspend time because the close can land at any point during the dialog.
                    if (_instance == null || !Showing || IsSuspended)
                    {
                        return;
                    }

                    _instance.Show();
                    _instance.Activate();
                });
            }
            catch
            {
                // Same as above - a torn-down dispatcher just means there is nothing to restore.
            }
        }

        /// <summary>Scope object returned by <see cref="SuspendForDialog"/>.</summary>
        private sealed class WaitFormSuspension : IDisposable
        {
            private bool disposed;

            public WaitFormSuspension() => Suspend();

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Resume();
            }
        }

        public static async Task SetSubtitleAsync(string subtitle)
        {
            if (OceanyaTestMode.Current.DisableWaitForms)
            {
                return;
            }

            if (_formDispatcher == null) return;

            _currentSubtitle = subtitle;

            await _formDispatcher.InvokeAsync(() =>
            {
                // Deliberately not gated on IsVisible: while a dialog has the form suspended it is
                // hidden but still live, and the subtitle must be current when it comes back.
                if (_instance != null)
                {
                    if (!string.IsNullOrWhiteSpace(subtitle))
                    {
                        _instance.lblSubtitle.Text = subtitle;
                        _instance.lblSubtitle.Visibility = Visibility.Visible;

                        // Re-measure every subtitle change because long sync messages can outgrow
                        // the existing width/height even when the subtitle is already visible.
                        _instance.ResizeWindow();
                        if (TryCenterRelativeToBounds(_instance, _ownerBounds))
                        {
                            return;
                        }
                    }
                    else
                    {
                        _instance.lblSubtitle.Visibility = Visibility.Collapsed;
                        _instance.ResizeWindow();
                        if (TryCenterRelativeToBounds(_instance, _ownerBounds))
                        {
                            return;
                        }
                    }
                }
            });
        }

        private void CopyMainMessageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string message = lblMessage.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(message) && !ClipboardUtilities.TrySetText(message))
            {
                _ = MessageBox.Show(
                    _instance ?? Application.Current?.MainWindow,
                    "Could not access clipboard right now. Try again in a moment.",
                    "Clipboard Busy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void CopySubtitleMessageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string message = lblSubtitle.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(message) && !ClipboardUtilities.TrySetText(message))
            {
                _ = MessageBox.Show(
                    _instance ?? Application.Current?.MainWindow,
                    "Could not access clipboard right now. Try again in a moment.",
                    "Clipboard Busy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        public static void SetSubtitle(string subtitle)
        {
            // For backward compatibility
            _ = SetSubtitleAsync(subtitle);
        }

        // Clean up resources when application exits
        public static void ShutdownThread()
        {
            if (_formDispatcher != null && !_formDispatcher.HasShutdownStarted)
            {
                try
                {
                    _formDispatcher.InvokeShutdown();
                }
                catch
                {
                    // Ignore exceptions during shutdown
                }
            }
        }

        private void ResizeWindow()
        {
            double horizontalPadding = 72;
            double baseVerticalSpace = 95;
            double minWidth = 300;
            double maxWidth = 760;

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            double availableWidth = maxWidth - horizontalPadding;
            double newWidth = 0;
            double newHeight = 0;

            // Measure title text size with wrapping
            FormattedText titleText = new FormattedText(
                lblMessage.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(lblMessage.FontFamily, lblMessage.FontStyle, lblMessage.FontWeight, lblMessage.FontStretch),
                lblMessage.FontSize,
                Brushes.Black,
                pixelsPerDip
            )
            {
                MaxTextWidth = availableWidth
            };

            newWidth = Math.Max(titleText.Width, minWidth); // Take the wider of minWidth or actual text width
            newHeight += titleText.Height; // Account for wrapped height

            // Measure subtitle text if visible
            double subtitleHeight = 0;
            if (lblSubtitle.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(lblSubtitle.Text))
            {
                FormattedText subtitleText = new FormattedText(
                    lblSubtitle.Text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(lblSubtitle.FontFamily, lblSubtitle.FontStyle, lblSubtitle.FontWeight, lblSubtitle.FontStretch),
                    lblSubtitle.FontSize,
                    Brushes.Black,
                    pixelsPerDip
                )
                {
                    MaxTextWidth = availableWidth // Allow wrapping
                };

                subtitleHeight = subtitleText.Height;
                newHeight += subtitleHeight;
            }

            // Apply final sizes
            this.Width = Math.Min(Math.Max(newWidth + horizontalPadding, minWidth), maxWidth);
            this.Height = Math.Max(MinHeight, newHeight + baseVerticalSpace);
        }

        private static bool TryCenterRelativeToOwner(Window waitWindow, Window owner)
        {
            Rect ownerBounds = ResolveOwnerBounds(owner);
            if (ownerBounds == Rect.Empty)
            {
                return false;
            }

            _ownerBounds = ownerBounds;
            return TryCenterRelativeToBounds(waitWindow, ownerBounds);
        }

        private static bool TryCenterRelativeToBounds(Window waitWindow, Rect ownerBounds)
        {
            if (ownerBounds == Rect.Empty || ownerBounds.Width <= 0 || ownerBounds.Height <= 0)
            {
                return false;
            }

            waitWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            Rect virtualBounds = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            double desiredLeft = ownerBounds.Left + ((ownerBounds.Width - waitWindow.Width) / 2);
            double desiredTop = ownerBounds.Top + ((ownerBounds.Height - waitWindow.Height) / 2);
            waitWindow.Left = Clamp(desiredLeft, virtualBounds.Left, virtualBounds.Right - waitWindow.Width);
            waitWindow.Top = Clamp(desiredTop, virtualBounds.Top, virtualBounds.Bottom - waitWindow.Height);
            return true;
        }

        private static Rect ResolveOwnerBounds(Window? owner)
        {
            if (owner == null)
            {
                return Rect.Empty;
            }

            Rect resolvedBounds = Rect.Empty;

            try
            {
                owner.Dispatcher.Invoke(() =>
                {
                    owner.UpdateLayout();
                    double width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
                    double height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
                    Point topLeft = new Point(owner.Left, owner.Top);

                    try
                    {
                        Point screenPoint = owner.PointToScreen(new Point(0, 0));
                        PresentationSource? source = PresentationSource.FromVisual(owner);
                        if (source?.CompositionTarget != null)
                        {
                            Matrix transform = source.CompositionTarget.TransformFromDevice;
                            topLeft = transform.Transform(screenPoint);
                        }
                        else
                        {
                            topLeft = screenPoint;
                        }
                    }
                    catch
                    {
                        Rect fallbackBounds = owner.WindowState == WindowState.Normal
                            ? new Rect(owner.Left, owner.Top, width, height)
                            : owner.RestoreBounds;
                        topLeft = new Point(fallbackBounds.Left, fallbackBounds.Top);
                        width = fallbackBounds.Width > 0 ? fallbackBounds.Width : width;
                        height = fallbackBounds.Height > 0 ? fallbackBounds.Height : height;
                    }

                    if (width > 0 && height > 0)
                    {
                        resolvedBounds = new Rect(topLeft.X, topLeft.Y, width, height);
                    }
                });
            }
            catch
            {
                return Rect.Empty;
            }

            return resolvedBounds.Width > 0 && resolvedBounds.Height > 0
                ? resolvedBounds
                : Rect.Empty;
        }

        /// <summary>
        /// Captures the owner window handle on the owner's own thread.
        /// </summary>
        /// <param name="owner">Owner window, possibly living on another dispatcher.</param>
        /// <returns>The owner window handle, or <see cref="IntPtr.Zero"/> when it cannot be resolved.</returns>
        private static IntPtr ResolveOwnerHandle(Window? owner)
        {
            if (owner == null)
            {
                return IntPtr.Zero;
            }

            try
            {
                return owner.Dispatcher.Invoke(() => new WindowInteropHelper(owner).Handle);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Gives the foreground back to the owner window while the wait form still holds it.
        /// </summary>
        /// <remarks>
        /// Only acts when the wait form is actually the foreground window, so it can never steal activation
        /// from a dialog or from another application the user switched to on purpose. Cross-thread
        /// <c>SetForegroundWindow</c> is permitted here because the calling process owns the foreground
        /// window at that moment.
        /// </remarks>
        private static void TryRestoreOwnerForeground()
        {
            try
            {
                if (_instance == null || _ownerHandle == IntPtr.Zero)
                {
                    return;
                }

                IntPtr waitFormHandle = new WindowInteropHelper(_instance).Handle;
                if (waitFormHandle == IntPtr.Zero || GetForegroundWindow() != waitFormHandle)
                {
                    return;
                }

                if (!IsWindow(_ownerHandle) || !IsWindowVisible(_ownerHandle) || IsIconic(_ownerHandle))
                {
                    return;
                }

                _ = SetForegroundWindow(_ownerHandle);
            }
            catch
            {
                // Activation is best-effort; a failure only means the previous focus behavior applies.
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (maximum < minimum)
            {
                return minimum;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
