using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OceanyaClient
{
    public partial class LoadingScreen : Window
    {
        private bool _isClosing = false;

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(LoadingScreen),
                new PropertyMetadata(0.0, OnProgressChanged));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }
        public LoadingScreen()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            // Set initial clip rect with height 0 (nothing showing)
            UpdateProgressDisplay();

            this.Loaded += (s, e) => PlayFadeInAnimation();
            this.Closing += LoadingScreen_Closing;
            this.Closed += (s, e) =>
            {
                // Once closed, let manager know we no longer have an instance
                LoadingScreenManager._instance = null;
            };
        }



        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var screen = (LoadingScreen)d;
            screen.UpdateProgressDisplay();
        }

        private void UpdateProgressDisplay()
        {
            // This is the key method that was missing implementation
            // We need to adjust the clip rectangle based on progress

            // Get the full height of the logo container
            double logoHeight = ProgressFillContainer.ActualHeight > 0 ?
                ProgressFillContainer.ActualHeight : 178;

            // Get the full width of the logo container
            double logoWidth = ProgressFillContainer.ActualWidth > 0 ?
                ProgressFillContainer.ActualWidth : 310;

            // Calculate the height that should be revealed based on progress
            // For progress 0: show nothing (height=0)
            // For progress 1: show full height
            double revealHeight = logoHeight * Progress;

            // We reveal from bottom to top, so start Y is (logoHeight - revealHeight)
            double startY = logoHeight - revealHeight;

            // Update the clip rectangle
            ProgressClip.Rect = new Rect(0, startY, logoWidth, revealHeight);
        }

        private void LoadingScreen_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing) return;

            // Cancel the close and fade out
            e.Cancel = true;
            CloseWithAnimation();
        }

        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;

            Storyboard fadeOut = (Storyboard)FindResource("FadeOut");
            fadeOut.Completed += (s, _) => this.Dispatcher.Invoke(() =>
            {
                // Actually close after fade
                _isClosing = true;
                base.Close(); // Use base.Close() to avoid recursive calls
            });
            fadeOut.Begin(this);
        }

        private void PlayFadeInAnimation()
        {
            Storyboard fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin(this);
        }

        // If you want to set a "subtitle" in the UI:
        public void SetSubtitle(string text)
        {
            Dispatcher.Invoke(() =>
            {
                SubtitleText.Text = text;
            });
        }

        // Animate progress nicely
        public void AnimateProgressTo(double targetProgress, double durationMs = 300)
        {
            targetProgress = Math.Clamp(targetProgress, 0.0, 1.0);

            Dispatcher.Invoke(() =>
            {
                var animation = new DoubleAnimation
                {
                    From = Progress,
                    To = targetProgress,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                animation.Completed += (s, e) => Progress = targetProgress;

                Storyboard sb = new Storyboard();
                sb.Children.Add(animation);
                Storyboard.SetTarget(animation, this);
                Storyboard.SetTargetProperty(animation, new PropertyPath("Progress"));
                sb.Begin(this);
            });
        }

        public void CloseScreen()
        {
            // Let the manager or any external code request a close
            Dispatcher.Invoke(() => CloseWithAnimation());
        }
    }

    /// <summary>
    /// Static helper class that replicates the WaitForm pattern
    /// (window living on a dedicated UI thread).
    /// </summary>
    public static class LoadingScreenManager
    {
        // Fields that mirror your WaitForm approach
        internal static LoadingScreen? _instance;
        private static Thread? _uiThread;
        private static Dispatcher? _formDispatcher;
        private static bool _threadRunning;
        private static readonly object _lock = new object();
        private static TaskCompletionSource<bool>? _initializationTcs;

        /// <summary>Ensure the dedicated UI thread is started</summary>
        private static void StartFormOnNewThread()
        {
            lock (_lock)
            {
                // If it's already running, do nothing except ensure the TCS is set
                if (_threadRunning)
                {
                    _initializationTcs ??= new TaskCompletionSource<bool>();
                    _initializationTcs.TrySetResult(true);
                    return;
                }

                _threadRunning = true;
                _initializationTcs = new TaskCompletionSource<bool>();

                _uiThread = new Thread(() =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(
                            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                        _formDispatcher = Dispatcher.CurrentDispatcher;
                        _initializationTcs?.SetResult(true);

                        Dispatcher.Run(); // Pump messages
                    }
                    finally
                    {
                        _threadRunning = false;
                    }
                });

                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.IsBackground = true;
                _uiThread.Start();

                // Wait for the dispatcher to be ready before returning
                _initializationTcs?.Task.Wait();
            }
        }

        /// <summary>
        /// Show the loading screen on its dedicated UI thread
        /// </summary>
        /// <param name="subtitle">Initial subtitle message</param>
        public static async Task ShowFormAsync(string subtitle = "Loading...")
        {
            // Start (or ensure started) the dedicated thread
            StartFormOnNewThread();

            if (_formDispatcher == null)
            {
                return;
            }

            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance == null)
                {
                    _instance = new LoadingScreen();
                }

                // If you want to set some initial progress or an initial subtitle
                _instance.SetSubtitle(subtitle);

                if (!_instance.IsVisible)
                {
                    _instance.Show();
                }

                // Optionally start progress at a small fraction
                _instance.AnimateProgressTo(0.05, 200);
            });
        }

        /// <summary>
        /// Close the form with fade-out. 
        /// </summary>
        public static async Task CloseFormAsync()
        {
            if (_formDispatcher == null) return;

            await _formDispatcher.InvokeAsync(() =>
            {
                if (_instance != null && _instance.IsVisible)
                {
                    // Animate to full progress before closing
                    _instance.AnimateProgressTo(1.0, 200);

                    // Wait a little for user to see 100%, then fade out
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        _formDispatcher?.Invoke(() =>
                        {
                            _instance?.CloseScreen(); // triggers fade-out, then closes
                        });
                    });
                }
            });

            // Wait for the fade to complete
            await Task.Delay(800);
        }

        /// <summary>
        /// Convenience sync version that calls CloseFormAsync.
        /// </summary>
        public static void CloseForm()
        {
            _ = CloseFormAsync();
        }

        /// <summary>
        /// Update progress from anywhere (ensuring we're on the dedicated thread).
        /// </summary>
        public static void SetProgress(double progress, double animationDuration = 300)
        {
            if (_formDispatcher == null) return;

            _formDispatcher.Invoke(() =>
            {
                if (_instance != null)
                {
                    _instance.AnimateProgressTo(progress, animationDuration);
                }
            });
        }

        /// <summary>
        /// Update the subtitle text in the loading screen.
        /// </summary>
        public static void SetSubtitle(string subtitle)
        {
            if (_formDispatcher == null) return;

            _formDispatcher.Invoke(() =>
            {
                if (_instance != null)
                {
                    _instance.SetSubtitle(subtitle);
                }
            });
        }

        /// <summary>
        /// If you ever want to kill the dispatcher thread for good.
        /// </summary>
        public static void ShutdownThread()
        {
            if (_formDispatcher != null && !_formDispatcher.HasShutdownStarted)
            {
                _formDispatcher.InvokeShutdown();
            }
        }
    }
}
