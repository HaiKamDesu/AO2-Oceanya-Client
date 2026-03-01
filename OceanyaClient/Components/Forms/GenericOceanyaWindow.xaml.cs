using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace OceanyaClient
{
    /// <summary>
    /// Reusable branded shell window for Oceanya popups.
    /// </summary>
    public partial class GenericOceanyaWindow : Window
    {
        /// <summary>
        /// Shared header height used by the generic shell.
        /// </summary>
        public const double SharedHeaderHeight = 30d;

        /// <summary>
        /// Shared frame border thickness used by the generic shell.
        /// </summary>
        public const double SharedFrameBorderThickness = 1d;

        /// <summary>
        /// Enables Generic Oceanya shared chrome behavior on any <see cref="Window"/> using the shared template.
        /// </summary>
        public static readonly DependencyProperty EnableSharedChromeBehaviorProperty = DependencyProperty.RegisterAttached(
            "EnableSharedChromeBehavior",
            typeof(bool),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(false, OnEnableSharedChromeBehaviorChanged));

        private static readonly DependencyProperty SharedChromeControllerProperty = DependencyProperty.RegisterAttached(
            "SharedChromeController",
            typeof(SharedChromeBehaviorController),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(null));

        /// <summary>
        /// Header text shown in the top bar.
        /// </summary>
        public static readonly DependencyProperty HeaderTextProperty = DependencyProperty.Register(
            nameof(HeaderText),
            typeof(string),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata("Oceanya", OnHeaderTextChanged));

        /// <summary>
        /// Content hosted in the main body area.
        /// </summary>
        public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
            nameof(BodyContent),
            typeof(object),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(null));

        /// <summary>
        /// Margin used by the hosted body content.
        /// </summary>
        public static readonly DependencyProperty BodyMarginProperty = DependencyProperty.Register(
            nameof(BodyMargin),
            typeof(Thickness),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(new Thickness(0)));

        /// <summary>
        /// Controls whether the user can resize this window. Enabled by default.
        /// </summary>
        public static readonly DependencyProperty IsUserResizeEnabledProperty = DependencyProperty.Register(
            nameof(IsUserResizeEnabled),
            typeof(bool),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(true, OnWindowInteractionSettingsChanged));

        /// <summary>
        /// Controls whether the user can drag this window by its header. Enabled by default.
        /// </summary>
        public static readonly DependencyProperty IsUserMoveEnabledProperty = DependencyProperty.Register(
            nameof(IsUserMoveEnabled),
            typeof(bool),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(true, OnWindowInteractionSettingsChanged));

        /// <summary>
        /// Controls whether the close button is shown.
        /// </summary>
        public static readonly DependencyProperty IsCloseButtonVisibleProperty = DependencyProperty.Register(
            nameof(IsCloseButtonVisible),
            typeof(bool),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(true, OnWindowInteractionSettingsChanged));

        /// <summary>
        /// Initializes a new instance of the <see cref="GenericOceanyaWindow"/> class.
        /// </summary>
        public GenericOceanyaWindow()
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            Loaded += OnWindowLoaded;
        }

        /// <inheritdoc/>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
        }

        /// <summary>
        /// Gets or sets the header text shown in the title area.
        /// </summary>
        public string HeaderText
        {
            get => (string)GetValue(HeaderTextProperty);
            set => SetValue(HeaderTextProperty, value);
        }

        /// <summary>
        /// Gets or sets the content hosted by the shell.
        /// </summary>
        public object? BodyContent
        {
            get => GetValue(BodyContentProperty);
            set => SetValue(BodyContentProperty, value);
        }

        /// <summary>
        /// Gets or sets the margin for the content area.
        /// </summary>
        public Thickness BodyMargin
        {
            get => (Thickness)GetValue(BodyMarginProperty);
            set => SetValue(BodyMarginProperty, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether window resizing is enabled.
        /// </summary>
        public bool IsUserResizeEnabled
        {
            get => (bool)GetValue(IsUserResizeEnabledProperty);
            set => SetValue(IsUserResizeEnabledProperty, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether dragging the window header is enabled.
        /// </summary>
        public bool IsUserMoveEnabled
        {
            get => (bool)GetValue(IsUserMoveEnabledProperty);
            set => SetValue(IsUserMoveEnabledProperty, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the close button is visible.
        /// </summary>
        public bool IsCloseButtonVisible
        {
            get => (bool)GetValue(IsCloseButtonVisibleProperty);
            set => SetValue(IsCloseButtonVisibleProperty, value);
        }

        /// <summary>
        /// Gets a value indicating whether Generic Oceanya shared chrome behavior is enabled on a window.
        /// </summary>
        public static bool GetEnableSharedChromeBehavior(DependencyObject dependencyObject)
        {
            return (bool)dependencyObject.GetValue(EnableSharedChromeBehaviorProperty);
        }

        /// <summary>
        /// Sets a value indicating whether Generic Oceanya shared chrome behavior is enabled on a window.
        /// </summary>
        public static void SetEnableSharedChromeBehavior(DependencyObject dependencyObject, bool value)
        {
            dependencyObject.SetValue(EnableSharedChromeBehaviorProperty, value);
        }

        private static void OnWindowInteractionSettingsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is GenericOceanyaWindow window)
            {
                window.ApplyInteractionSettings();
            }
        }

        private static void OnEnableSharedChromeBehaviorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not Window window || window is GenericOceanyaWindow)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                SharedChromeBehaviorController controller = new SharedChromeBehaviorController(window);
                window.SetValue(SharedChromeControllerProperty, controller);
                controller.Attach();
                return;
            }

            if (window.GetValue(SharedChromeControllerProperty) is SharedChromeBehaviorController existingController)
            {
                existingController.Detach();
                window.ClearValue(SharedChromeControllerProperty);
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            ApplyInteractionSettings();
            ApplyWindowFrameForState();
            UpdateHeaderCollisionOpacity();
        }

        private void ApplyInteractionSettings()
        {
            ResizeMode = IsUserResizeEnabled ? ResizeMode.CanResize : ResizeMode.NoResize;
            CloseButton.Visibility = IsCloseButtonVisible ? Visibility.Visible : Visibility.Collapsed;

            WindowChrome? chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null)
            {
                chrome.ResizeBorderThickness = IsUserResizeEnabled && WindowState != WindowState.Maximized
                    ? new Thickness(6)
                    : new Thickness(0);
                chrome.CaptionHeight = IsUserMoveEnabled ? 30 : 0;
            }

            ApplyWindowFrameForState();
            UpdateHeaderCollisionOpacity();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (!IsUserMoveEnabled || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (current is FrameworkElement element)
                    {
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            ApplyWindowFrameForState();
            ApplyInteractionSettings();
        }

        private void ApplyWindowFrameForState()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowFrameBorder.BorderThickness = new Thickness(0);
                WindowFrameBorder.CornerRadius = new CornerRadius(0);
                return;
            }

            WindowFrameBorder.BorderThickness = new Thickness(1);
            WindowFrameBorder.CornerRadius = new CornerRadius(5);
        }

        private static void OnHeaderTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is GenericOceanyaWindow window)
            {
                window.Dispatcher.BeginInvoke(new Action(window.UpdateHeaderCollisionOpacity));
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHeaderCollisionOpacity();
        }

        private void UpdateHeaderCollisionOpacity()
        {
            if (!IsLoaded)
            {
                return;
            }

            Rect oceanyaBounds = GetBoundsInHeader(OceanyaLogoRectangle);
            Rect laboratoriesBounds = GetBoundsInHeader(LaboratoriesLogoRectangle);
            Rect titleBounds = GetBoundsInHeader(HeaderTitleTextBlock);
            Rect closeBounds = GetBoundsInHeader(CloseButton);

            bool hideTitle = Intersects(titleBounds, oceanyaBounds) || Intersects(titleBounds, laboratoriesBounds);
            bool hideLaboratoriesLogo = Intersects(laboratoriesBounds, closeBounds);
            bool hideOceanyaLogo = Intersects(oceanyaBounds, closeBounds);

            FadeElementTo(HeaderTitleTextBlock, hideTitle ? 0 : 1);
            FadeElementTo(LaboratoriesLogoRectangle, hideLaboratoriesLogo ? 0 : 1);
            FadeElementTo(OceanyaLogoRectangle, hideOceanyaLogo ? 0 : 1);
        }

        private Rect GetBoundsInHeader(FrameworkElement element)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0 || !element.IsVisible)
            {
                return Rect.Empty;
            }

            GeneralTransform transform = element.TransformToAncestor(HeaderGrid);
            return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        private static bool Intersects(Rect left, Rect right)
        {
            return !left.IsEmpty && !right.IsEmpty && left.IntersectsWith(right);
        }

        private static void FadeElementTo(UIElement element, double targetOpacity)
        {
            if (Math.Abs(element.Opacity - targetOpacity) < 0.01)
            {
                return;
            }

            DoubleAnimation fadeAnimation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(OpacityProperty, fadeAnimation);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            GenericMonitorInfo monitorInfo = new GenericMonitorInfo();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return;
            }

            GenericMinMaxInfo mmi = Marshal.PtrToStructure<GenericMinMaxInfo>(lParam);
            GenericRect workArea = monitorInfo.rcWork;
            GenericRect monitorArea = monitorInfo.rcMonitor;

            mmi.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
            mmi.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
            mmi.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
            mmi.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref GenericMonitorInfo lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct GenericPoint
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GenericRect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GenericMinMaxInfo
        {
            public GenericPoint ptReserved;
            public GenericPoint ptMaxSize;
            public GenericPoint ptMaxPosition;
            public GenericPoint ptMinTrackSize;
            public GenericPoint ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct GenericMonitorInfo
        {
            public int cbSize;
            public GenericRect rcMonitor;
            public GenericRect rcWork;
            public int dwFlags;
        }

        private sealed class SharedChromeBehaviorController
        {
            private readonly Window window;
            private Border? headerDragSurface;
            private FrameworkElement? headerGrid;
            private FrameworkElement? oceanyaLogoRectangle;
            private FrameworkElement? laboratoriesLogoRectangle;
            private FrameworkElement? headerTitleTextBlock;
            private Button? closeButton;
            private bool isHookAttached;

            public SharedChromeBehaviorController(Window window)
            {
                this.window = window;
            }

            public void Attach()
            {
                window.Loaded += Window_Loaded;
                window.Closed += Window_Closed;
                window.SizeChanged += Window_SizeChanged;
                window.StateChanged += Window_StateChanged;
                window.SourceInitialized += Window_SourceInitialized;
                ResolveTemplateParts();
                UpdateHeaderCollisionOpacity();
            }

            public void Detach()
            {
                window.Loaded -= Window_Loaded;
                window.Closed -= Window_Closed;
                window.SizeChanged -= Window_SizeChanged;
                window.StateChanged -= Window_StateChanged;
                window.SourceInitialized -= Window_SourceInitialized;

                if (headerDragSurface != null)
                {
                    headerDragSurface.MouseLeftButtonDown -= HeaderDragSurface_MouseLeftButtonDown;
                }

            }

            private void Window_Loaded(object sender, RoutedEventArgs e)
            {
                ResolveTemplateParts();
                UpdateHeaderCollisionOpacity();
            }

            private void Window_Closed(object? sender, EventArgs e)
            {
                Detach();
            }

            private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
            {
                UpdateHeaderCollisionOpacity();
            }

            private void Window_StateChanged(object? sender, EventArgs e)
            {
                UpdateHeaderCollisionOpacity();
            }

            private void Window_SourceInitialized(object? sender, EventArgs e)
            {
                if (isHookAttached)
                {
                    return;
                }

                IntPtr handle = new WindowInteropHelper(window).Handle;
                HwndSource? source = HwndSource.FromHwnd(handle);
                source?.AddHook(WndProc);
                isHookAttached = true;
            }

            private void ResolveTemplateParts()
            {
                if (window.Template == null)
                {
                    return;
                }

                headerDragSurface = window.Template.FindName("HeaderDragSurface", window) as Border;
                headerGrid = window.Template.FindName("HeaderGrid", window) as FrameworkElement;
                oceanyaLogoRectangle = window.Template.FindName("OceanyaLogoRectangle", window) as FrameworkElement;
                laboratoriesLogoRectangle = window.Template.FindName("LaboratoriesLogoRectangle", window) as FrameworkElement;
                headerTitleTextBlock = window.Template.FindName("HeaderTitleTextBlock", window) as FrameworkElement;
                closeButton = window.Template.FindName("CloseButton", window) as Button;

                if (headerDragSurface != null)
                {
                    headerDragSurface.MouseLeftButtonDown -= HeaderDragSurface_MouseLeftButtonDown;
                    headerDragSurface.MouseLeftButtonDown += HeaderDragSurface_MouseLeftButtonDown;
                }

            }

            private void HeaderDragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }

                if (e.OriginalSource is DependencyObject source)
                {
                    for (DependencyObject? current = source; current != null;)
                    {
                        if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                        {
                            return;
                        }

                        if (current is FrameworkElement element)
                        {
                            current = element.Parent ?? element.TemplatedParent as DependencyObject;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                window.DragMove();
            }

            private void UpdateHeaderCollisionOpacity()
            {
                if (!window.IsLoaded
                    || headerGrid == null
                    || oceanyaLogoRectangle == null
                    || laboratoriesLogoRectangle == null
                    || headerTitleTextBlock == null
                    || closeButton == null)
                {
                    return;
                }

                Rect oceanyaBounds = GetBoundsInHeader(oceanyaLogoRectangle);
                Rect laboratoriesBounds = GetBoundsInHeader(laboratoriesLogoRectangle);
                Rect titleBounds = GetBoundsInHeader(headerTitleTextBlock);
                Rect closeBounds = GetBoundsInHeader(closeButton);

                bool hideTitle = Intersects(titleBounds, oceanyaBounds) || Intersects(titleBounds, laboratoriesBounds);
                bool hideLaboratoriesLogo = Intersects(laboratoriesBounds, closeBounds);
                bool hideOceanyaLogo = Intersects(oceanyaBounds, closeBounds);

                FadeElementTo(headerTitleTextBlock, hideTitle ? 0 : 1);
                FadeElementTo(laboratoriesLogoRectangle, hideLaboratoriesLogo ? 0 : 1);
                FadeElementTo(oceanyaLogoRectangle, hideOceanyaLogo ? 0 : 1);
            }

            private Rect GetBoundsInHeader(FrameworkElement element)
            {
                if (headerGrid == null || element.ActualWidth <= 0 || element.ActualHeight <= 0 || !element.IsVisible)
                {
                    return Rect.Empty;
                }

                GeneralTransform transform = element.TransformToAncestor(headerGrid);
                return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }

            private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                const int WM_GETMINMAXINFO = 0x0024;
                if (msg == WM_GETMINMAXINFO)
                {
                    WmGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                }

                return IntPtr.Zero;
            }
        }
    }
}
