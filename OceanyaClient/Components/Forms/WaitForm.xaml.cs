using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private static bool _threadRunning = false;

        private WaitForm()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            Opacity = 0; // Start fully transparent
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
                // Clear old references
                _instance = null;

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
            _currentTitle = message;
            _currentSubtitle = string.Empty;
            _ownerWindow = owner;

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
                        if (!TryCenterRelativeToOwner(_instance, owner))
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

                    // Fade in animation
                    DoubleAnimation fadeInAnimation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5));
                    _instance.BeginAnimation(Window.OpacityProperty, fadeInAnimation);
                }
            });
        }

        public static async Task CloseFormAsync()
        {
            if (_formDispatcher == null) return;

            // Use the form's dispatcher to close it
            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance != null && _instance.IsVisible)
                {
                    DoubleAnimation fadeOutAnimation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5));
                    fadeOutAnimation.Completed += (s, e) =>
                    {
                        _instance?.Close();
                        Showing = false;
                    };
                    _instance.BeginAnimation(Window.OpacityProperty, fadeOutAnimation);
                }
            });

            // Allow some time for animation to complete
            await Task.Delay(600);

            if (_formDispatcher == null)
            {
                return;
            }

            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance != null)
                {
                    try
                    {
                        _instance.Close();
                    }
                    catch
                    {
                        // Best-effort cleanup if the fade animation path did not close the wait form.
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

        public static async Task SetSubtitleAsync(string subtitle)
        {
            if (_formDispatcher == null) return;

            _currentSubtitle = subtitle;

            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance != null && _instance.IsVisible)
                {
                    if (!string.IsNullOrWhiteSpace(subtitle))
                    {
                        _instance.lblSubtitle.Text = subtitle;
                        _instance.lblSubtitle.Visibility = Visibility.Visible;

                        // Re-measure every subtitle change because long sync messages can outgrow
                        // the existing width/height even when the subtitle is already visible.
                        _instance.ResizeWindow();
                        if (_ownerWindow != null && _ownerWindow.IsVisible)
                        {
                            TryCenterRelativeToOwner(_instance, _ownerWindow);
                        }
                    }
                    else
                    {
                        _instance.lblSubtitle.Visibility = Visibility.Collapsed;
                        _instance.ResizeWindow();
                        if (_ownerWindow != null && _ownerWindow.IsVisible)
                        {
                            TryCenterRelativeToOwner(_instance, _ownerWindow);
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
            if (!TryGetOwnerBounds(owner, out Rect ownerBounds))
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

        private static bool TryGetOwnerBounds(Window owner, out Rect bounds)
        {
            bounds = Rect.Empty;
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
                return false;
            }

            bounds = resolvedBounds;
            return bounds.Width > 0 && bounds.Height > 0;
        }

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
