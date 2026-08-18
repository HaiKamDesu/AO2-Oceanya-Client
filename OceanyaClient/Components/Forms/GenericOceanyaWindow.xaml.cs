using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using OceanyaClient.Features.Ui;

namespace OceanyaClient
{
    /// <summary>
    /// Reusable branded shell window for Oceanya popups.
    /// </summary>
    public partial class GenericOceanyaWindow : Window
    {
        private const int HeaderPriorityCloseButton = 1;
        private const int HeaderPriorityBranding = 2;
        private const int HeaderPriorityTitle = 3;

        /// <summary>
        /// Shared header height used by the generic shell.
        /// </summary>
        public const double SharedHeaderHeight = 30d;

        /// <summary>
        /// Shared frame border thickness used by the generic shell.
        /// </summary>
        public const double SharedFrameBorderThickness = 1d;

        /// <summary>
        /// Resize grip thickness used by the generic shell at scale 1.0.
        /// </summary>
        private const double BaseResizeBorderThickness = 6d;

        /// <summary>
        /// Gets the client version text shown in the shared header.
        /// </summary>
        public static string ClientVersionDisplayText { get; } = ResolveClientVersionDisplayText();

        /// <summary>
        /// Current UI scale applied to this shell's header and hosted body content.
        /// </summary>
        public static readonly DependencyProperty ContentScaleProperty = DependencyProperty.Register(
            nameof(ContentScale),
            typeof(double),
            typeof(GenericOceanyaWindow),
            new PropertyMetadata(1d, OnContentScaleChanged));

        /// <summary>
        /// Raised after <see cref="ContentScale"/> changed and the shell chrome was re-applied.
        /// </summary>
        public event EventHandler? ContentScaleChanged;

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
            UpdateAutomationReadyMarker(OceanyaWindowContentControl.AutomationReadyStateLoading);
            ContentScale = UiScaleMath.ClampScale(UiScaleManager.GlobalScale);
            UiScaleManager.ScaleChanged += OnGlobalUiScaleChanged;
            Closed += (_, _) => UiScaleManager.ScaleChanged -= OnGlobalUiScaleChanged;
            LocationChanged += (_, _) => RefreshContentScaleFromSettings();
        }

        /// <inheritdoc/>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
            RefreshContentScaleFromSettings();
        }

        /// <inheritdoc/>
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            RefreshContentScaleFromSettings();
        }

        /// <summary>
        /// Gets or sets the UI scale applied to the shell header and hosted body content.
        /// </summary>
        public double ContentScale
        {
            get => (double)GetValue(ContentScaleProperty);
            set => SetValue(ContentScaleProperty, value);
        }

        /// <summary>
        /// Computes the non-content chrome padding a hosted window adds around its body content.
        /// </summary>
        /// <param name="bodyMargin">Body margin applied inside the scaled shell.</param>
        /// <param name="scale">Active shell content scale.</param>
        /// <returns>Horizontal and vertical chrome offsets in window device independent pixels.</returns>
        public static (double HorizontalOffset, double VerticalOffset) GetChromeOffsets(Thickness bodyMargin, double scale)
        {
            double normalizedScale = UiScaleMath.ClampScale(scale);
            double horizontalOffset =
                (SharedFrameBorderThickness * 2)
                + ((bodyMargin.Left + bodyMargin.Right) * normalizedScale);
            double verticalOffset =
                (SharedHeaderHeight * normalizedScale)
                + (SharedFrameBorderThickness * 2)
                + ((bodyMargin.Top + bodyMargin.Bottom) * normalizedScale);

            return (horizontalOffset, verticalOffset);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this shell participates in global UI scaling.
        /// Windows whose body content is already resolution-driven (the AO2 viewport surfaces) opt out.
        /// </summary>
        public bool IsContentScaleEnabled
        {
            get => isContentScaleEnabled;
            set
            {
                isContentScaleEnabled = value;
                RefreshContentScaleFromSettings();
            }
        }

        private bool isContentScaleEnabled = true;

        /// <summary>
        /// Gets or sets a value indicating whether dragging this window's edges rescales its content
        /// instead of stretching the layout. Opt-in per window; the resulting scale is published as
        /// the global manual scale when the drag finishes.
        /// </summary>
        public bool IsResizeScalingEnabled { get; set; }

        /// <summary>
        /// Gets a value indicating whether an interactive rescale drag is in progress, so the hosted
        /// sizing controller must not fight the drag by re-applying the content size.
        /// </summary>
        public bool IsInteractiveResizeScaling { get; private set; }

        /// <summary>
        /// Splits this shell's chrome into the part that scales with the content and the part that
        /// never scales (the frame border drawn outside the scaled shell).
        /// </summary>
        /// <returns>Scaled and fixed chrome lengths per axis, in device independent pixels.</returns>
        public (double ScaledWidth, double ScaledHeight, double FixedWidth, double FixedHeight) GetChromeParts()
        {
            Thickness bodyMargin = BodyMargin;
            double scaledWidth = bodyMargin.Left + bodyMargin.Right;
            double scaledHeight = SharedHeaderHeight + bodyMargin.Top + bodyMargin.Bottom;
            double fixedLength = SharedFrameBorderThickness * 2;
            return (scaledWidth, scaledHeight, fixedLength, fixedLength);
        }

        /// <summary>
        /// Clamps a requested scale so the resulting window still fits the monitor's work area.
        /// Without this a large manual scale can grow a window past the screen edges, taking its own
        /// settings entry point out of reach.
        /// </summary>
        /// <param name="requestedScale">Scale the settings resolved to.</param>
        /// <returns>The requested scale, reduced when it would not fit.</returns>
        public double ClampScaleToMonitor(double requestedScale)
        {
            if (BodyContent is not FrameworkElement bodyElement)
            {
                return requestedScale;
            }

            double contentWidth = IsUsableLength(bodyElement.Width) ? bodyElement.Width : bodyElement.ActualWidth;
            double contentHeight = IsUsableLength(bodyElement.Height) ? bodyElement.Height : bodyElement.ActualHeight;
            if (!IsUsableLength(contentWidth) || !IsUsableLength(contentHeight))
            {
                return requestedScale;
            }

            (double scaledWidth, double scaledHeight, double fixedWidth, double fixedHeight) = GetChromeParts();
            (double availableWidth, double availableHeight) = UiScaleManager.GetLogicalWorkAreaSize(this);

            double widthFit = UiScaleMath.ResolveMaximumFittingScale(contentWidth, scaledWidth, fixedWidth, availableWidth);
            double heightFit = UiScaleMath.ResolveMaximumFittingScale(contentHeight, scaledHeight, fixedHeight, availableHeight);
            double fitScale = Math.Min(widthFit, heightFit);
            if (double.IsInfinity(fitScale) || fitScale <= 0)
            {
                return requestedScale;
            }

            return Math.Min(requestedScale, fitScale);
        }

        private static bool IsUsableLength(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        /// <summary>
        /// Re-resolves this shell's scale from the current scale settings and monitor.
        /// </summary>
        public void RefreshContentScaleFromSettings()
        {
            double resolvedScale = isContentScaleEnabled
                ? UiScaleMath.ClampScale(ClampScaleToMonitor(UiScaleManager.ResolveScaleForWindow(this)))
                : 1d;
            if (Math.Abs(resolvedScale - ContentScale) < 0.0001d)
            {
                return;
            }

            ContentScale = resolvedScale;
        }

        private void OnGlobalUiScaleChanged(object? sender, EventArgs e)
        {
            RefreshContentScaleFromSettings();
        }

        private static void OnContentScaleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is GenericOceanyaWindow window)
            {
                window.ApplyContentScale();
            }
        }

        private void ApplyContentScale()
        {
            double scale = UiScaleMath.ClampScale(ContentScale);
            ShellContentScaleTransform.ScaleX = scale;
            ShellContentScaleTransform.ScaleY = scale;
            HeaderOffsetBackdropRectangle.Margin = new Thickness(0, SharedHeaderHeight * scale, 0, 0);
            ApplyInteractionSettings();
            ContentScaleChanged?.Invoke(this, EventArgs.Empty);
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
        /// Updates the hidden UI automation ready marker hosted by this shell window.
        /// </summary>
        /// <param name="state">Ready-state string to expose to UI automation.</param>
        public void UpdateAutomationReadyMarker(string state)
        {
            string normalizedState = string.IsNullOrWhiteSpace(state)
                ? OceanyaWindowContentControl.AutomationReadyStateLoading
                : state.Trim();
            AutomationReadyMarker.Text = normalizedState;
            AutomationProperties.SetName(AutomationReadyMarker, normalizedState);
            AutomationProperties.SetHelpText(AutomationReadyMarker, normalizedState);
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
            // Content sizes are only real once layout ran, so the fit-to-monitor clamp is re-resolved
            // here; before this point auto-sized content reports 0 and cannot constrain anything.
            RefreshContentScaleFromSettings();
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
                double scale = UiScaleMath.ClampScale(ContentScale);
                chrome.ResizeBorderThickness = IsUserResizeEnabled && WindowState != WindowState.Maximized
                    ? new Thickness(BaseResizeBorderThickness * scale)
                    : new Thickness(0);
                chrome.CaptionHeight = IsUserMoveEnabled ? SharedHeaderHeight * scale : 0;
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

            HeaderCollisionTarget[] targets = new HeaderCollisionTarget[]
            {
                new HeaderCollisionTarget(CloseButton, HeaderPriorityCloseButton),
                new HeaderCollisionTarget(OceanyaLogoRectangle, HeaderPriorityBranding),
                new HeaderCollisionTarget(LaboratoriesLogoRectangle, HeaderPriorityBranding),
                new HeaderCollisionTarget(HeaderVersionTextBlock, HeaderPriorityBranding),
                new HeaderCollisionTarget(HeaderTitleTextBlock, HeaderPriorityTitle)
            };

            ApplyCollisionPriorityFade(targets, GetBoundsInHeader);
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

        private static void ApplyCollisionPriorityFade(
            IReadOnlyList<HeaderCollisionTarget> targets,
            Func<FrameworkElement, Rect> boundsResolver)
        {
            int targetCount = targets.Count;
            bool[] shouldHide = new bool[targetCount];
            Rect[] bounds = new Rect[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                bounds[i] = boundsResolver(targets[i].Element);
            }

            for (int leftIndex = 0; leftIndex < targetCount; leftIndex++)
            {
                for (int rightIndex = leftIndex + 1; rightIndex < targetCount; rightIndex++)
                {
                    if (!Intersects(bounds[leftIndex], bounds[rightIndex]))
                    {
                        continue;
                    }

                    int leftPriority = targets[leftIndex].Priority;
                    int rightPriority = targets[rightIndex].Priority;

                    if (leftPriority == rightPriority)
                    {
                        continue;
                    }

                    if (leftPriority < rightPriority)
                    {
                        shouldHide[rightIndex] = true;
                    }
                    else
                    {
                        shouldHide[leftIndex] = true;
                    }
                }
            }

            for (int i = 0; i < targetCount; i++)
            {
                FadeElementTo(targets[i].Element, shouldHide[i] ? 0d : 1d);
            }
        }

        private static string ResolveClientVersionDisplayText()
        {
            return AppVersionInfo.DisplayVersionWithPrefix;
        }

        /// <summary>
        /// Gets or sets the window that is moved in sync when the user holds Ctrl while dragging this window.
        /// </summary>
        public Window? SynchronizedMovePartner { get; set; }

        private double contentScaleAtGestureStart = 1d;
        private GenericRect? synchronizedMoveStartRect;
        private GenericRect? synchronizedMovePartnerStartRect;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            const int WM_SIZING = 0x0214;
            const int WM_MOVING = 0x0216;
            const int WM_ENTERSIZEMOVE = 0x0231;
            const int WM_EXITSIZEMOVE = 0x0232;

            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(this, hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_SIZING && IsResizeScalingEnabled && isContentScaleEnabled)
            {
                ApplyResizeScalingSizingRect(wParam.ToInt32(), lParam);
                handled = true;
                return new IntPtr(1);
            }
            else if (msg == WM_MOVING)
            {
                HandleWindowMovingSynchronize(hwnd, lParam);
            }
            else if (msg == WM_ENTERSIZEMOVE)
            {
                InitializeSynchronizedMoveTracking(hwnd);
                IsInteractiveResizeScaling = IsResizeScalingEnabled && isContentScaleEnabled;
                contentScaleAtGestureStart = ContentScale;
            }
            else if (msg == WM_EXITSIZEMOVE)
            {
                ResetSynchronizedMoveTracking();
                if (IsInteractiveResizeScaling)
                {
                    IsInteractiveResizeScaling = false;

                    // ENTERSIZEMOVE/EXITSIZEMOVE also bracket a plain window MOVE, so publishing
                    // unconditionally turned "I dragged the window by its header" into "switch the whole
                    // app to manual scale at this window's clamped value".
                    if (Math.Abs(ContentScale - contentScaleAtGestureStart) >= 0.0001d)
                    {
                        PublishResizeScaleToSettings();
                    }
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Rewrites a drag-resize rectangle so the window keeps the hosted content's aspect ratio and
        /// applies the matching content scale live, the way the AO2 viewport window resizes.
        /// </summary>
        /// <param name="sizingEdge">WMSZ_* edge being dragged.</param>
        /// <param name="lParam">Pointer to the proposed window rectangle, in device pixels.</param>
        private void ApplyResizeScalingSizingRect(int sizingEdge, IntPtr lParam)
        {
            if (BodyContent is not FrameworkElement bodyElement)
            {
                return;
            }

            double contentWidth = IsUsableLength(bodyElement.Width) ? bodyElement.Width : bodyElement.ActualWidth;
            double contentHeight = IsUsableLength(bodyElement.Height) ? bodyElement.Height : bodyElement.ActualHeight;
            if (!IsUsableLength(contentWidth) || !IsUsableLength(contentHeight))
            {
                return;
            }

            GenericRect rect = Marshal.PtrToStructure<GenericRect>(lParam);
            (double dpiScaleX, double dpiScaleY) = GetWindowDeviceScale(this);
            double proposedWidth = (rect.right - rect.left) / dpiScaleX;
            double proposedHeight = (rect.bottom - rect.top) / dpiScaleY;

            (double scaledWidth, double scaledHeight, double fixedWidth, double fixedHeight) = GetChromeParts();
            double widthDrivenScale = UiScaleMath.ResolveScaleFromWindowLength(
                proposedWidth, contentWidth, scaledWidth, fixedWidth);
            double heightDrivenScale = UiScaleMath.ResolveScaleFromWindowLength(
                proposedHeight, contentHeight, scaledHeight, fixedHeight);

            // Side handles drive their own axis; corners follow whichever axis the user pulled further.
            bool isHorizontalEdge = sizingEdge is WmszLeft or WmszRight;
            bool isVerticalEdge = sizingEdge is WmszTop or WmszBottom;
            double requestedScale;
            if (isHorizontalEdge)
            {
                requestedScale = widthDrivenScale;
            }
            else if (isVerticalEdge)
            {
                requestedScale = heightDrivenScale;
            }
            else
            {
                requestedScale = Math.Max(widthDrivenScale, heightDrivenScale);
            }

            double resolvedScale = UiScaleMath.ClampScale(requestedScale);
            double windowWidth = (contentWidth + scaledWidth) * resolvedScale + fixedWidth;
            double windowHeight = (contentHeight + scaledHeight) * resolvedScale + fixedHeight;

            ResizeNativeRect(
                ref rect,
                sizingEdge,
                (int)Math.Round(windowWidth * dpiScaleX),
                (int)Math.Round(windowHeight * dpiScaleY));
            Marshal.StructureToPtr(rect, lParam, true);

            if (Math.Abs(resolvedScale - ContentScale) >= 0.0001d)
            {
                ContentScale = resolvedScale;
            }
        }

        /// <summary>
        /// Persists the scale reached by a drag resize as the global manual scale, so every other
        /// Oceanya window follows and the choice survives a restart.
        /// </summary>
        private void PublishResizeScaleToSettings()
        {
            UiScaleManager.ApplySettings(UiScaleMode.Manual, ContentScale);
        }

        private static void ResizeNativeRect(ref GenericRect rect, int sizingEdge, int width, int height)
        {
            switch (sizingEdge)
            {
                case WmszLeft:
                    rect.left = rect.right - width;
                    rect.bottom = rect.top + height;
                    break;
                case WmszRight:
                    rect.right = rect.left + width;
                    rect.bottom = rect.top + height;
                    break;
                case WmszTop:
                    rect.top = rect.bottom - height;
                    rect.right = rect.left + width;
                    break;
                case WmszTopLeft:
                    rect.left = rect.right - width;
                    rect.top = rect.bottom - height;
                    break;
                case WmszTopRight:
                    rect.right = rect.left + width;
                    rect.top = rect.bottom - height;
                    break;
                case WmszBottom:
                    rect.bottom = rect.top + height;
                    rect.right = rect.left + width;
                    break;
                case WmszBottomLeft:
                    rect.left = rect.right - width;
                    rect.bottom = rect.top + height;
                    break;
                case WmszBottomRight:
                default:
                    rect.right = rect.left + width;
                    rect.bottom = rect.top + height;
                    break;
            }
        }

        private const int WmszLeft = 1;
        private const int WmszRight = 2;
        private const int WmszTop = 3;
        private const int WmszTopLeft = 4;
        private const int WmszTopRight = 5;
        private const int WmszBottom = 6;
        private const int WmszBottomLeft = 7;
        private const int WmszBottomRight = 8;

        private void HandleWindowMovingSynchronize(IntPtr hwnd, IntPtr lParam)
        {
            if (SynchronizedMovePartner == null)
            {
                return;
            }

            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            {
                ResetSynchronizedMoveTracking();
                return;
            }

            GenericRect rect = Marshal.PtrToStructure<GenericRect>(lParam);
            if (!synchronizedMoveStartRect.HasValue || !synchronizedMovePartnerStartRect.HasValue)
            {
                InitializeSynchronizedMoveTracking(hwnd);
            }

            if (!synchronizedMoveStartRect.HasValue || !synchronizedMovePartnerStartRect.HasValue)
            {
                return;
            }

            WindowInteropHelper partnerInterop = new WindowInteropHelper(SynchronizedMovePartner);
            IntPtr partnerHandle = partnerInterop.Handle;
            if (partnerHandle == IntPtr.Zero)
            {
                return;
            }

            int dx = rect.left - synchronizedMoveStartRect.Value.left;
            int dy = rect.top - synchronizedMoveStartRect.Value.top;
            GenericRect partnerStart = synchronizedMovePartnerStartRect.Value;
            SetWindowPos(
                partnerHandle,
                IntPtr.Zero,
                partnerStart.left + dx,
                partnerStart.top + dy,
                0,
                0,
                SetWindowPosFlags.NoSize | SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate | SetWindowPosFlags.NoOwnerZOrder);
        }

        private void InitializeSynchronizedMoveTracking(IntPtr hwnd)
        {
            if (SynchronizedMovePartner == null)
            {
                ResetSynchronizedMoveTracking();
                return;
            }

            WindowInteropHelper partnerInterop = new WindowInteropHelper(SynchronizedMovePartner);
            IntPtr partnerHandle = partnerInterop.Handle;
            if (hwnd == IntPtr.Zero
                || partnerHandle == IntPtr.Zero
                || !GetWindowRect(hwnd, out GenericRect currentRect)
                || !GetWindowRect(partnerHandle, out GenericRect partnerRect))
            {
                ResetSynchronizedMoveTracking();
                return;
            }

            synchronizedMoveStartRect = currentRect;
            synchronizedMovePartnerStartRect = partnerRect;
        }

        private void ResetSynchronizedMoveTracking()
        {
            synchronizedMoveStartRect = null;
            synchronizedMovePartnerStartRect = null;
        }

        private static void WmGetMinMaxInfo(Window window, IntPtr hwnd, IntPtr lParam)
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
            (double scaleX, double scaleY) = GetWindowDeviceScale(window);
            mmi.ptMinTrackSize.x = Math.Max(mmi.ptMinTrackSize.x, ToDevicePixels(window.MinWidth, scaleX));
            mmi.ptMinTrackSize.y = Math.Max(mmi.ptMinTrackSize.y, ToDevicePixels(window.MinHeight, scaleY));

            if (!double.IsInfinity(window.MaxWidth) && !double.IsNaN(window.MaxWidth) && window.MaxWidth > 0)
            {
                mmi.ptMaxTrackSize.x = Math.Max(mmi.ptMinTrackSize.x, ToDevicePixels(window.MaxWidth, scaleX));
            }
            else
            {
                mmi.ptMaxTrackSize.x = Math.Max(mmi.ptMaxTrackSize.x, ToDevicePixels(SystemParameters.VirtualScreenWidth, scaleX));
            }

            if (!double.IsInfinity(window.MaxHeight) && !double.IsNaN(window.MaxHeight) && window.MaxHeight > 0)
            {
                mmi.ptMaxTrackSize.y = Math.Max(mmi.ptMinTrackSize.y, ToDevicePixels(window.MaxHeight, scaleY));
            }
            else
            {
                // Windows defaults MaxTrackSize to roughly one monitor, which silently clipped scaled
                // windows taller than the screen (width grew, height did not). Allow the whole desktop.
                mmi.ptMaxTrackSize.y = Math.Max(mmi.ptMaxTrackSize.y, ToDevicePixels(SystemParameters.VirtualScreenHeight, scaleY));
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private static (double ScaleX, double ScaleY) GetWindowDeviceScale(Window window)
        {
            PresentationSource? source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget == null)
            {
                return (1d, 1d);
            }

            Matrix transform = source.CompositionTarget.TransformToDevice;
            return (transform.M11 > 0 ? transform.M11 : 1d, transform.M22 > 0 ? transform.M22 : 1d);
        }

        private static int ToDevicePixels(double value, double scale)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Ceiling(value * scale));
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref GenericMonitorInfo lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out GenericRect lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags flags);

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

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            NoSize = 0x0001,
            NoZOrder = 0x0004,
            NoActivate = 0x0010,
            NoOwnerZOrder = 0x0200
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

        private readonly struct HeaderCollisionTarget
        {
            public HeaderCollisionTarget(FrameworkElement element, int priority)
            {
                Element = element;
                Priority = priority;
            }

            public FrameworkElement Element { get; }

            public int Priority { get; }
        }

        private sealed class SharedChromeBehaviorController
        {
            private readonly Window window;
            private Border? headerDragSurface;
            private FrameworkElement? headerGrid;
            private FrameworkElement? oceanyaLogoRectangle;
            private FrameworkElement? laboratoriesLogoRectangle;
            private FrameworkElement? headerVersionTextBlock;
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
                headerVersionTextBlock = window.Template.FindName("HeaderVersionTextBlock", window) as FrameworkElement;
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
                    || headerVersionTextBlock == null
                    || headerTitleTextBlock == null
                    || closeButton == null)
                {
                    return;
                }

                HeaderCollisionTarget[] targets = new HeaderCollisionTarget[]
                {
                    new HeaderCollisionTarget(closeButton, HeaderPriorityCloseButton),
                    new HeaderCollisionTarget(oceanyaLogoRectangle, HeaderPriorityBranding),
                    new HeaderCollisionTarget(laboratoriesLogoRectangle, HeaderPriorityBranding),
                    new HeaderCollisionTarget(headerVersionTextBlock, HeaderPriorityBranding),
                    new HeaderCollisionTarget(headerTitleTextBlock, HeaderPriorityTitle)
                };

                ApplyCollisionPriorityFade(targets, GetBoundsInHeader);
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
                    WmGetMinMaxInfo(window, hwnd, lParam);
                    handled = true;
                }

                return IntPtr.Zero;
            }
        }
    }
}
