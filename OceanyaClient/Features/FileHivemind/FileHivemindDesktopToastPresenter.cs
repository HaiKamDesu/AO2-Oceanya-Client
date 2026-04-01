using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace OceanyaClient.Features.FileHivemind
{
    internal sealed class FileHivemindDesktopToastPresenter : IDisposable
    {
        private const int ToastGap = 10;
        private const int TemporaryToastDurationMs = 5200;

        private readonly Func<Icon> iconFactory;
        private readonly Image? backgroundImage;
        private readonly Image? watermarkImage;
        private DesktopToastWindow? activeProgressToast;
        private string activeProgressOperationKey = string.Empty;
        private string dismissedProgressOperationKey = string.Empty;
        private DesktopToastWindow? activeTemporaryToast;
        private Forms.Timer? activeTemporaryTimer;
        private bool suppressProgressDismissal;
        private bool disposed;

        public FileHivemindDesktopToastPresenter(Func<Icon> iconFactory)
        {
            this.iconFactory = iconFactory ?? throw new ArgumentNullException(nameof(iconFactory));
            backgroundImage = LoadOptionalImage("Resources", "scoienceblur.jpg");
            watermarkImage = LoadOptionalImage("Resources", "Logo_O_Centered.png");
        }

        public void Show(string title, string message, FileHivemindAgentNotificationSeverity severity)
        {
            if (disposed)
            {
                return;
            }

            CloseTemporaryToast(immediate: false);
            DesktopToastWindow toast = CreateToastWindow(title, message, severity, isProgressToast: false);
            PositionToast(toast, stackIndex: activeProgressToast == null ? 0 : 1);
            toast.FormClosed += TemporaryToast_FormClosed;

            activeTemporaryToast = toast;
            activeTemporaryTimer = new Forms.Timer
            {
                Interval = TemporaryToastDurationMs
            };
            activeTemporaryTimer.Tick += (_, _) => CloseTemporaryToast(immediate: false);
            activeTemporaryTimer.Start();
            toast.Show();
        }

        public void ShowProgress(
            string operationKey,
            string title,
            string message,
            string detail,
            double? progressFraction)
        {
            if (disposed)
            {
                return;
            }

            string normalizedKey = NormalizeOperationKey(operationKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return;
            }

            if (string.Equals(dismissedProgressOperationKey, normalizedKey, StringComparison.Ordinal))
            {
                return;
            }

            if (activeProgressToast != null
                && !string.Equals(activeProgressOperationKey, normalizedKey, StringComparison.Ordinal))
            {
                CloseProgressToast(immediate: true, suppressDismissal: true);
            }

            if (activeProgressToast == null)
            {
                DesktopToastWindow toast = CreateToastWindow(
                    title,
                    message,
                    FileHivemindAgentNotificationSeverity.Info,
                    isProgressToast: true);
                toast.UpdateProgress(detail, progressFraction);
                PositionToast(toast, stackIndex: 0);
                toast.FormClosed += ProgressToast_FormClosed;
                activeProgressToast = toast;
                activeProgressOperationKey = normalizedKey;
                toast.Show();
                RepositionTemporaryToast();
                return;
            }

            activeProgressToast.UpdateContent(title, message);
            activeProgressToast.UpdateProgress(detail, progressFraction);
            activeProgressToast.BringToastToFront();
        }

        public void UpdateProgress(string operationKey, string detail, double? progressFraction)
        {
            if (disposed)
            {
                return;
            }

            string normalizedKey = NormalizeOperationKey(operationKey);
            if (string.IsNullOrWhiteSpace(normalizedKey)
                || activeProgressToast == null
                || !string.Equals(activeProgressOperationKey, normalizedKey, StringComparison.Ordinal))
            {
                return;
            }

            activeProgressToast.UpdateProgress(detail, progressFraction);
        }

        public void CloseProgress(string operationKey)
        {
            if (disposed)
            {
                return;
            }

            string normalizedKey = NormalizeOperationKey(operationKey);
            if (string.IsNullOrWhiteSpace(normalizedKey)
                || activeProgressToast == null
                || !string.Equals(activeProgressOperationKey, normalizedKey, StringComparison.Ordinal))
            {
                if (string.Equals(dismissedProgressOperationKey, normalizedKey, StringComparison.Ordinal))
                {
                    dismissedProgressOperationKey = string.Empty;
                }

                return;
            }

            CloseProgressToast(immediate: false, suppressDismissal: true);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CloseTemporaryToast(immediate: true);
            CloseProgressToast(immediate: true, suppressDismissal: true);
            backgroundImage?.Dispose();
            watermarkImage?.Dispose();
        }

        private static string NormalizeOperationKey(string? operationKey)
        {
            return operationKey?.Trim() ?? string.Empty;
        }

        private DesktopToastWindow CreateToastWindow(
            string title,
            string message,
            FileHivemindAgentNotificationSeverity severity,
            bool isProgressToast)
        {
            Icon icon = iconFactory();
            Color accentColor = ResolveAccentColor(severity);
            return new DesktopToastWindow(
                icon,
                title,
                message,
                accentColor,
                backgroundImage,
                watermarkImage,
                isProgressToast);
        }

        private void CloseTemporaryToast(bool immediate)
        {
            activeTemporaryTimer?.Stop();
            activeTemporaryTimer?.Dispose();
            activeTemporaryTimer = null;

            if (activeTemporaryToast == null)
            {
                return;
            }

            DesktopToastWindow toast = activeTemporaryToast;
            activeTemporaryToast = null;
            if (toast.IsDisposed)
            {
                return;
            }

            if (immediate)
            {
                toast.CloseImmediately();
                return;
            }

            toast.CloseAnimated();
        }

        private void CloseProgressToast(bool immediate, bool suppressDismissal)
        {
            if (activeProgressToast == null)
            {
                if (suppressDismissal)
                {
                    this.suppressProgressDismissal = false;
                }

                return;
            }

            this.suppressProgressDismissal = suppressDismissal;
            DesktopToastWindow toast = activeProgressToast;
            if (toast.IsDisposed)
            {
                activeProgressToast = null;
                activeProgressOperationKey = string.Empty;
                this.suppressProgressDismissal = false;
                return;
            }

            if (immediate)
            {
                toast.CloseImmediately();
                return;
            }

            toast.CloseAnimated();
        }

        private void PositionToast(DesktopToastWindow toast, int stackIndex)
        {
            Rectangle workingArea = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
            int x = Math.Max(workingArea.Left + 12, workingArea.Right - toast.Width - 16);
            int y = Math.Max(
                workingArea.Top + 12,
                workingArea.Bottom - toast.Height - 16 - (stackIndex * (toast.Height + ToastGap)));
            toast.SetRestingLocation(new Point(x, y));
        }

        private void RepositionTemporaryToast()
        {
            if (activeTemporaryToast == null || activeTemporaryToast.IsDisposed)
            {
                return;
            }

            PositionToast(activeTemporaryToast, stackIndex: activeProgressToast == null ? 0 : 1);
        }

        private void TemporaryToast_FormClosed(object? sender, Forms.FormClosedEventArgs e)
        {
            if (sender is not DesktopToastWindow toast)
            {
                return;
            }

            toast.FormClosed -= TemporaryToast_FormClosed;
            toast.DisposeResources();
            if (ReferenceEquals(activeTemporaryToast, toast))
            {
                activeTemporaryToast = null;
            }
        }

        private void ProgressToast_FormClosed(object? sender, Forms.FormClosedEventArgs e)
        {
            if (sender is not DesktopToastWindow toast)
            {
                return;
            }

            toast.FormClosed -= ProgressToast_FormClosed;
            toast.DisposeResources();
            if (ReferenceEquals(activeProgressToast, toast))
            {
                if (!suppressProgressDismissal)
                {
                    dismissedProgressOperationKey = activeProgressOperationKey;
                }

                activeProgressToast = null;
                activeProgressOperationKey = string.Empty;
                suppressProgressDismissal = false;
            }

            RepositionTemporaryToast();
        }

        private static Image? LoadOptionalImage(params string[] segments)
        {
            string path = AppContext.BaseDirectory;
            foreach (string segment in segments)
            {
                path = Path.Combine(path, segment);
            }

            if (!File.Exists(path))
            {
                return null;
            }

            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using Image source = Image.FromStream(stream);
            return new Bitmap(source);
        }

        private static Color ResolveAccentColor(FileHivemindAgentNotificationSeverity severity)
        {
            return severity switch
            {
                FileHivemindAgentNotificationSeverity.Success => Color.FromArgb(118, 224, 141),
                FileHivemindAgentNotificationSeverity.Error => Color.FromArgb(255, 116, 116),
                _ => Color.FromArgb(255, 196, 92)
            };
        }

        private sealed class DesktopToastWindow : Forms.Form
        {
            private const int BorderRadius = 8;
            private const int AccentWidth = 6;
            private const int OuterPadding = 14;
            private const int IconLeftPadding = 12;
            private const int IconSize = 30;
            private const int CloseButtonSize = 22;
            private const int CloseButtonTop = 10;
            private const int ContentTop = 14;
            private const int SlideDistance = 34;
            private const int TransitionFrameIntervalMs = 15;
            private const int ShowDurationMs = 170;
            private const int HideDurationMs = 145;
            private const int DetailTimerVisibilityThresholdSeconds = 5;
            private static readonly Font TitleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            private static readonly Font MessageFont = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            private static readonly Font DetailFont = new Font("Segoe UI", 7.8f, FontStyle.Regular);

            private readonly Icon icon;
            private readonly Bitmap iconBitmap;
            private readonly Image? backgroundImage;
            private readonly Image? watermarkImage;
            private readonly bool isProgressToast;
            private readonly Forms.Timer marqueeTimer;
            private readonly Forms.Timer detailTimer;
            private readonly Forms.Timer transitionTimer;
            private readonly int marqueeSegmentWidth = 90;

            private string titleText;
            private string messageText;
            private string detailText = string.Empty;
            private double? progressFraction;
            private long marqueeOffset;
            private Rectangle closeButtonBounds;
            private bool disposedResources;
            private bool closeCommitted;
            private Point restingLocation;
            private Point transitionStartLocation;
            private Point transitionTargetLocation;
            private double transitionStartOpacity;
            private double transitionTargetOpacity;
            private DateTime transitionStartedUtc;
            private ToastTransitionMode transitionMode;
            private DateTime detailChangedUtc;

            public DesktopToastWindow(
                Icon icon,
                string title,
                string message,
                Color accentColor,
                Image? backgroundImage,
                Image? watermarkImage,
                bool isProgressToast)
            {
                this.icon = icon ?? throw new ArgumentNullException(nameof(icon));
                iconBitmap = this.icon.ToBitmap();
                this.backgroundImage = backgroundImage;
                this.watermarkImage = watermarkImage;
                this.isProgressToast = isProgressToast;
                AccentColor = accentColor;
                titleText = (title ?? string.Empty).Trim();
                messageText = (message ?? string.Empty).Trim();
                detailChangedUtc = DateTime.UtcNow;

                FormBorderStyle = Forms.FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = Forms.FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.Black;
                DoubleBuffered = true;
                Opacity = 0d;
                SetStyle(
                    Forms.ControlStyles.AllPaintingInWmPaint
                    | Forms.ControlStyles.OptimizedDoubleBuffer
                    | Forms.ControlStyles.ResizeRedraw
                    | Forms.ControlStyles.UserPaint,
                    true);

                marqueeTimer = new Forms.Timer
                {
                    Interval = 55
                };
                marqueeTimer.Tick += (_, _) =>
                {
                    marqueeOffset += 12;
                    if (marqueeOffset > 1_000_000)
                    {
                        marqueeOffset = 0;
                    }

                    Invalidate();
                };

                detailTimer = new Forms.Timer
                {
                    Interval = 1000
                };
                detailTimer.Tick += (_, _) =>
                {
                    if (isProgressToast && !string.IsNullOrWhiteSpace(detailText))
                    {
                        Invalidate();
                    }
                };

                transitionTimer = new Forms.Timer
                {
                    Interval = TransitionFrameIntervalMs
                };
                transitionTimer.Tick += (_, _) => AdvanceTransition();

                ClientSize = MeasureToastSize();
                ApplyRoundedRegion();
                UpdateMarqueeState();
                UpdateDetailTimerState();
            }

            private Color AccentColor { get; }

            protected override bool ShowWithoutActivation => true;

            protected override Forms.CreateParams CreateParams
            {
                get
                {
                    Forms.CreateParams createParams = base.CreateParams;
                    createParams.ExStyle |= 0x08000000;
                    createParams.ExStyle |= 0x00000080;
                    return createParams;
                }
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                if (restingLocation == Point.Empty)
                {
                    restingLocation = Location;
                }

                StartShowAnimation();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                ApplyRoundedRegion();
            }

            protected override void OnMouseClick(Forms.MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (closeButtonBounds.Contains(e.Location))
                {
                    CloseAnimated();
                }
            }

            protected override void WndProc(ref Forms.Message m)
            {
                const int WmMouseActivate = 0x0021;
                const int MaNoActivate = 3;

                if (m.Msg == WmMouseActivate)
                {
                    m.Result = (IntPtr)MaNoActivate;
                    return;
                }

                base.WndProc(ref m);
            }

            protected override void OnPaint(Forms.PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle bounds = ClientRectangle;
                Rectangle innerBounds = Rectangle.Inflate(bounds, -1, -1);
                using GraphicsPath path = CreateRoundedPath(innerBounds, BorderRadius);
                graphics.SetClip(path);

                if (backgroundImage != null)
                {
                    graphics.DrawImage(backgroundImage, innerBounds);
                }
                else
                {
                    using SolidBrush fallbackBrush = new SolidBrush(Color.FromArgb(42, 42, 42));
                    graphics.FillRectangle(fallbackBrush, innerBounds);
                }

                using SolidBrush overlayBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                graphics.FillRectangle(overlayBrush, innerBounds);

                if (watermarkImage != null)
                {
                    Size watermarkSize = ScaleToFit(
                        watermarkImage.Size,
                        new Size((int)(innerBounds.Width * 0.62f), (int)(innerBounds.Height * 0.62f)));
                    Rectangle watermarkBounds = new Rectangle(
                        innerBounds.Left + (innerBounds.Width - watermarkSize.Width) / 2,
                        innerBounds.Top + (innerBounds.Height - watermarkSize.Height) / 2,
                        watermarkSize.Width,
                        watermarkSize.Height);
                    DrawImageWithOpacity(graphics, watermarkImage, watermarkBounds, 0.08f);
                }

                using SolidBrush accentBrush = new SolidBrush(AccentColor);
                graphics.FillRectangle(accentBrush, new Rectangle(innerBounds.Left, innerBounds.Top, AccentWidth, innerBounds.Height));

                DrawContent(graphics, innerBounds);

                graphics.ResetClip();
                using Pen borderPen = new Pen(Color.FromArgb(46, 46, 46));
                graphics.DrawPath(borderPen, path);
            }

            public void UpdateContent(string title, string message)
            {
                titleText = (title ?? string.Empty).Trim();
                messageText = (message ?? string.Empty).Trim();
                ClientSize = MeasureToastSize();
                Invalidate();
            }

            public void UpdateProgress(string detail, double? fraction)
            {
                string normalizedDetail = (detail ?? string.Empty).Trim();
                if (!string.Equals(detailText, normalizedDetail, StringComparison.Ordinal))
                {
                    detailText = normalizedDetail;
                    detailChangedUtc = DateTime.UtcNow;
                }
                else
                {
                    detailText = normalizedDetail;
                }

                progressFraction = NormalizeProgress(fraction);
                ClientSize = MeasureToastSize();
                UpdateMarqueeState();
                UpdateDetailTimerState();
                Invalidate();
            }

            public void BringToastToFront()
            {
                if (!IsHandleCreated)
                {
                    return;
                }

                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HwndTopmost,
                    Left,
                    Top,
                    Width,
                    Height,
                    NativeMethods.SetWindowPosFlags.NoActivate
                    | NativeMethods.SetWindowPosFlags.ShowWindow
                    | NativeMethods.SetWindowPosFlags.NoOwnerZOrder);
            }

            public void SetRestingLocation(Point location)
            {
                restingLocation = location;
                if (!Visible || !IsHandleCreated)
                {
                    Location = location;
                    return;
                }

                if (transitionMode == ToastTransitionMode.Showing)
                {
                    transitionTargetLocation = location;
                    return;
                }

                if (transitionMode == ToastTransitionMode.Hidden)
                {
                    Location = location;
                    return;
                }

                if (transitionMode == ToastTransitionMode.None)
                {
                    Location = location;
                    BringToastToFront();
                }
            }

            public void CloseAnimated()
            {
                if (closeCommitted || IsDisposed)
                {
                    return;
                }

                if (!Visible || !IsHandleCreated)
                {
                    CloseImmediately();
                    return;
                }

                if (transitionMode == ToastTransitionMode.Hiding)
                {
                    return;
                }

                StartHideAnimation();
            }

            public void CloseImmediately()
            {
                if (closeCommitted || IsDisposed)
                {
                    return;
                }

                closeCommitted = true;
                transitionTimer.Stop();
                marqueeTimer.Stop();
                detailTimer.Stop();
                base.Close();
            }

            public void DisposeResources()
            {
                if (disposedResources)
                {
                    return;
                }

                disposedResources = true;
                transitionTimer.Stop();
                transitionTimer.Dispose();
                marqueeTimer.Stop();
                marqueeTimer.Dispose();
                detailTimer.Stop();
                detailTimer.Dispose();
                iconBitmap.Dispose();
                icon.Dispose();
            }

            private Size MeasureToastSize()
            {
                const int toastWidth = 404;
                int textX = AccentWidth + IconLeftPadding + IconSize + 16;
                int textWidth = toastWidth - textX - OuterPadding - (CloseButtonSize + 8);
                int titleHeight = Forms.TextRenderer.MeasureText(
                    titleText,
                    TitleFont,
                    new Size(textWidth, 0),
                    Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPadding).Height;
                Size messageSize = Forms.TextRenderer.MeasureText(
                    messageText,
                    MessageFont,
                    new Size(textWidth, 0),
                    Forms.TextFormatFlags.WordBreak | Forms.TextFormatFlags.TextBoxControl);
                int height = isProgressToast ? 126 : 94;
                if (isProgressToast)
                {
                    string detailDisplayText = GetDisplayDetailText();
                    Size detailSize = Forms.TextRenderer.MeasureText(
                        detailDisplayText,
                        DetailFont,
                        new Size(textWidth, 0),
                        Forms.TextFormatFlags.WordBreak | Forms.TextFormatFlags.TextBoxControl);
                    height = Math.Max(
                        height,
                        OuterPadding * 2 + titleHeight + messageSize.Height + detailSize.Height + 30);
                }
                else
                {
                    height = Math.Max(height, OuterPadding * 2 + titleHeight + messageSize.Height + 18);
                }

                return new Size(toastWidth, height);
            }

            private void DrawContent(Graphics graphics, Rectangle bounds)
            {
                int iconX = bounds.Left + AccentWidth + IconLeftPadding;
                int iconY = bounds.Top + (bounds.Height - IconSize) / 2;
                graphics.DrawImage(iconBitmap, new Rectangle(iconX, iconY, IconSize, IconSize));

                closeButtonBounds = new Rectangle(
                    bounds.Right - OuterPadding - CloseButtonSize,
                    bounds.Top + CloseButtonTop,
                    CloseButtonSize,
                    CloseButtonSize);
                DrawCloseButton(graphics, closeButtonBounds);

                int textX = iconX + IconSize + 16;
                int textWidth = bounds.Right - textX - OuterPadding - (CloseButtonSize + 10);
                Rectangle titleBounds = new Rectangle(textX, bounds.Top + ContentTop, textWidth, 22);
                Rectangle messageBounds = new Rectangle(textX, titleBounds.Bottom + 2, textWidth, isProgressToast ? 30 : 36);

                Forms.TextRenderer.DrawText(
                    graphics,
                    titleText,
                    TitleFont,
                    titleBounds,
                    Color.White,
                    Forms.TextFormatFlags.EndEllipsis | Forms.TextFormatFlags.NoPadding | Forms.TextFormatFlags.SingleLine);
                Forms.TextRenderer.DrawText(
                    graphics,
                    messageText,
                    MessageFont,
                    messageBounds,
                    Color.FromArgb(232, 232, 232),
                    Forms.TextFormatFlags.WordBreak | Forms.TextFormatFlags.TextBoxControl);

                if (!isProgressToast)
                {
                    return;
                }

                Rectangle detailBounds = new Rectangle(textX, messageBounds.Bottom + 2, textWidth, 18);
                Forms.TextRenderer.DrawText(
                    graphics,
                    GetDisplayDetailText(),
                    DetailFont,
                    detailBounds,
                    Color.FromArgb(196, 196, 196),
                    Forms.TextFormatFlags.WordBreak | Forms.TextFormatFlags.TextBoxControl | Forms.TextFormatFlags.EndEllipsis);

                Rectangle progressBounds = new Rectangle(textX, detailBounds.Bottom + 6, textWidth, 10);
                DrawProgressBar(graphics, progressBounds);
            }

            private void DrawCloseButton(Graphics graphics, Rectangle bounds)
            {
                using Pen pen = new Pen(Color.FromArgb(228, 228, 228), 1.5f);
                int inset = 6;
                graphics.DrawLine(pen, bounds.Left + inset, bounds.Top + inset, bounds.Right - inset, bounds.Bottom - inset);
                graphics.DrawLine(pen, bounds.Right - inset, bounds.Top + inset, bounds.Left + inset, bounds.Bottom - inset);
            }

            private void DrawProgressBar(Graphics graphics, Rectangle bounds)
            {
                using GraphicsPath path = CreateRoundedPath(bounds, 4);
                using SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(110, 16, 16, 16));
                graphics.FillPath(backgroundBrush, path);

                Rectangle fillBounds;
                if (progressFraction.HasValue)
                {
                    int width = Math.Max(8, (int)Math.Round(bounds.Width * progressFraction.Value));
                    fillBounds = new Rectangle(bounds.Left, bounds.Top, Math.Min(bounds.Width, width), bounds.Height);
                }
                else
                {
                    int travelWidth = Math.Max(1, bounds.Width + marqueeSegmentWidth);
                    int startX = bounds.Left - marqueeSegmentWidth + (int)(marqueeOffset % travelWidth);
                    fillBounds = new Rectangle(startX, bounds.Top, marqueeSegmentWidth, bounds.Height);
                }

                Rectangle clippedFill = Rectangle.Intersect(bounds, fillBounds);
                if (clippedFill.Width > 0 && clippedFill.Height > 0)
                {
                    using GraphicsPath fillPath = CreateRoundedPath(clippedFill, 4);
                    using LinearGradientBrush fillBrush = new LinearGradientBrush(
                        clippedFill,
                        AccentColor,
                        Color.FromArgb(
                            Math.Min(255, AccentColor.R + 20),
                            Math.Min(255, AccentColor.G + 20),
                            Math.Min(255, AccentColor.B + 20)),
                        LinearGradientMode.Horizontal);
                    graphics.FillPath(fillBrush, fillPath);
                }

                using Pen borderPen = new Pen(Color.FromArgb(86, 86, 86));
                graphics.DrawPath(borderPen, path);
            }

            private void UpdateMarqueeState()
            {
                if (!isProgressToast)
                {
                    marqueeTimer.Stop();
                    return;
                }

                if (progressFraction.HasValue)
                {
                    marqueeTimer.Stop();
                    marqueeOffset = 0;
                    return;
                }

                if (!marqueeTimer.Enabled)
                {
                    marqueeTimer.Start();
                }
            }

            private void UpdateDetailTimerState()
            {
                if (!isProgressToast)
                {
                    detailTimer.Stop();
                    return;
                }

                if (!detailTimer.Enabled)
                {
                    detailTimer.Start();
                }
            }

            private void ApplyRoundedRegion()
            {
                using GraphicsPath path = CreateRoundedPath(ClientRectangle, BorderRadius);
                Region = new Region(path);
            }

            private void StartShowAnimation()
            {
                transitionMode = ToastTransitionMode.Showing;
                transitionStartedUtc = DateTime.UtcNow;
                transitionStartLocation = new Point(restingLocation.X + SlideDistance, restingLocation.Y);
                transitionTargetLocation = restingLocation;
                transitionStartOpacity = 0d;
                transitionTargetOpacity = 1d;
                Location = transitionStartLocation;
                Opacity = 0d;
                BringToastToFront();
                transitionTimer.Start();
            }

            private void StartHideAnimation()
            {
                transitionMode = ToastTransitionMode.Hiding;
                transitionStartedUtc = DateTime.UtcNow;
                transitionStartLocation = Location;
                transitionTargetLocation = new Point(restingLocation.X + SlideDistance, restingLocation.Y);
                transitionStartOpacity = Opacity;
                transitionTargetOpacity = 0d;
                transitionTimer.Start();
            }

            private void AdvanceTransition()
            {
                int durationMs = transitionMode == ToastTransitionMode.Hiding ? HideDurationMs : ShowDurationMs;
                double rawProgress = Math.Clamp(
                    (DateTime.UtcNow - transitionStartedUtc).TotalMilliseconds / durationMs,
                    0d,
                    1d);
                double easedProgress = EaseOutCubic(rawProgress);

                Location = new Point(
                    Lerp(transitionStartLocation.X, transitionTargetLocation.X, easedProgress),
                    Lerp(transitionStartLocation.Y, transitionTargetLocation.Y, easedProgress));
                Opacity = Lerp(transitionStartOpacity, transitionTargetOpacity, easedProgress);
                BringToastToFront();

                if (rawProgress < 1d)
                {
                    return;
                }

                transitionTimer.Stop();
                Location = transitionTargetLocation;
                Opacity = transitionTargetOpacity;
                if (transitionMode == ToastTransitionMode.Hiding)
                {
                    closeCommitted = true;
                    base.Close();
                    return;
                }

                transitionMode = ToastTransitionMode.None;
            }

            private string GetDisplayDetailText()
            {
                if (string.IsNullOrWhiteSpace(detailText))
                {
                    return string.Empty;
                }

                int elapsedSeconds = Math.Max(0, (int)(DateTime.UtcNow - detailChangedUtc).TotalSeconds);
                if (elapsedSeconds < DetailTimerVisibilityThresholdSeconds)
                {
                    return detailText;
                }

                return "(" + elapsedSeconds + "s) " + detailText;
            }

            private static int Lerp(int start, int end, double progress)
            {
                return (int)Math.Round(start + ((end - start) * progress));
            }

            private static double Lerp(double start, double end, double progress)
            {
                return start + ((end - start) * progress);
            }

            private static double EaseOutCubic(double progress)
            {
                double inverse = 1d - Math.Clamp(progress, 0d, 1d);
                return 1d - (inverse * inverse * inverse);
            }

            private static double? NormalizeProgress(double? fraction)
            {
                if (!fraction.HasValue)
                {
                    return null;
                }

                return Math.Clamp(fraction.Value, 0d, 1d);
            }

            private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
            {
                int diameter = radius * 2;
                GraphicsPath path = new GraphicsPath();
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    path.AddRectangle(bounds);
                    return path;
                }

                Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
                path.AddArc(arc, 180, 90);
                arc.X = bounds.Right - diameter;
                path.AddArc(arc, 270, 90);
                arc.Y = bounds.Bottom - diameter;
                path.AddArc(arc, 0, 90);
                arc.X = bounds.Left;
                path.AddArc(arc, 90, 90);
                path.CloseFigure();
                return path;
            }

            private static Size ScaleToFit(Size source, Size max)
            {
                if (source.Width <= 0 || source.Height <= 0 || max.Width <= 0 || max.Height <= 0)
                {
                    return Size.Empty;
                }

                float scale = Math.Min((float)max.Width / source.Width, (float)max.Height / source.Height);
                scale = Math.Min(scale, 1f);
                return new Size(
                    Math.Max(1, (int)Math.Round(source.Width * scale)),
                    Math.Max(1, (int)Math.Round(source.Height * scale)));
            }

            private static void DrawImageWithOpacity(Graphics graphics, Image image, Rectangle destination, float opacity)
            {
                using ImageAttributes attributes = new ImageAttributes();
                ColorMatrix matrix = new ColorMatrix
                {
                    Matrix33 = Math.Clamp(opacity, 0f, 1f)
                };
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(
                    image,
                    destination,
                    0,
                    0,
                    image.Width,
                    image.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            private enum ToastTransitionMode
            {
                Hidden = 0,
                Showing = 1,
                None = 2,
                Hiding = 3
            }

            private static class NativeMethods
            {
                public static readonly IntPtr HwndTopmost = new IntPtr(-1);

                [Flags]
                public enum SetWindowPosFlags : uint
                {
                    NoActivate = 0x0010,
                    ShowWindow = 0x0040,
                    NoOwnerZOrder = 0x0200
                }

                [DllImport("user32.dll", SetLastError = true)]
                public static extern bool SetWindowPos(
                    IntPtr hWnd,
                    IntPtr hWndInsertAfter,
                    int x,
                    int y,
                    int cx,
                    int cy,
                    SetWindowPosFlags uFlags);
            }
        }
    }
}
