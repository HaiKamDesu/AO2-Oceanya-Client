using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OceanyaClient
{
    /// <summary>
    /// Reusable play/stop circular control with optional progress ring.
    /// </summary>
    public partial class PlayStopProgressButton : UserControl
    {
        private readonly DispatcherTimer progressTimer = new DispatcherTimer();
        private DateTime startedAtUtc;

        public event EventHandler? PlayRequested;
        public event EventHandler? StopRequested;
        public event EventHandler? PlaybackCompleted;

        public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(false, OnIsPlayingChanged));

        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(0d, OnProgressChanged));

        public static readonly DependencyProperty ProgressEnabledProperty = DependencyProperty.Register(
            nameof(ProgressEnabled),
            typeof(bool),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(true, OnProgressChanged));

        public static readonly DependencyProperty AutoProgressProperty = DependencyProperty.Register(
            nameof(AutoProgress),
            typeof(bool),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(false));

        public static readonly DependencyProperty DurationMsProperty = DependencyProperty.Register(
            nameof(DurationMs),
            typeof(double),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(1000d));

        public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
            nameof(GlyphBrush),
            typeof(Brush),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB8B8B8"))));

        public static readonly DependencyProperty BaseRingBrushProperty = DependencyProperty.Register(
            nameof(BaseRingBrush),
            typeof(Brush),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8C8C8C"))));

        public static readonly DependencyProperty ProgressRingBrushProperty = DependencyProperty.Register(
            nameof(ProgressRingBrush),
            typeof(Brush),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0"))));

        public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
            nameof(RingThickness),
            typeof(double),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(2d));

        public static readonly DependencyProperty ToolTipTextProperty = DependencyProperty.Register(
            nameof(ToolTipText),
            typeof(string),
            typeof(PlayStopProgressButton),
            new PropertyMetadata(string.Empty));

        public PlayStopProgressButton()
        {
            InitializeComponent();
            progressTimer.Interval = TimeSpan.FromMilliseconds(30);
            progressTimer.Tick += ProgressTimer_Tick;
            UpdateGlyph();
            UpdateProgressPath();
        }

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public bool ProgressEnabled
        {
            get => (bool)GetValue(ProgressEnabledProperty);
            set => SetValue(ProgressEnabledProperty, value);
        }

        public bool AutoProgress
        {
            get => (bool)GetValue(AutoProgressProperty);
            set => SetValue(AutoProgressProperty, value);
        }

        public double DurationMs
        {
            get => (double)GetValue(DurationMsProperty);
            set => SetValue(DurationMsProperty, value);
        }

        public Brush GlyphBrush
        {
            get => (Brush)GetValue(GlyphBrushProperty);
            set => SetValue(GlyphBrushProperty, value);
        }

        public Brush BaseRingBrush
        {
            get => (Brush)GetValue(BaseRingBrushProperty);
            set => SetValue(BaseRingBrushProperty, value);
        }

        public Brush ProgressRingBrush
        {
            get => (Brush)GetValue(ProgressRingBrushProperty);
            set => SetValue(ProgressRingBrushProperty, value);
        }

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        public string ToolTipText
        {
            get => (string)GetValue(ToolTipTextProperty);
            set => SetValue(ToolTipTextProperty, value);
        }

        private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PlayStopProgressButton control)
            {
                return;
            }

            bool isPlaying = e.NewValue is bool flag && flag;
            control.UpdateGlyph();
            if (isPlaying && control.AutoProgress && control.DurationMs > 0)
            {
                control.startedAtUtc = DateTime.UtcNow;
                control.Progress = 0;
                control.progressTimer.Start();
            }
            else
            {
                control.progressTimer.Stop();
                if (!isPlaying)
                {
                    control.Progress = 0;
                }
            }
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PlayStopProgressButton control)
            {
                control.UpdateProgressPath();
            }
        }

        private void MainButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsPlaying)
            {
                StopRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                PlayRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsPlaying || !AutoProgress || DurationMs <= 0)
            {
                progressTimer.Stop();
                return;
            }

            double elapsed = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;
            double value = Math.Clamp(elapsed / DurationMs, 0, 1);
            Progress = value;

            if (value >= 1)
            {
                progressTimer.Stop();
                IsPlaying = false;
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateGlyph()
        {
            GlyphText.Text = IsPlaying ? "■" : "▶";
        }

        private void UpdateProgressPath()
        {
            if (!ProgressEnabled || Progress <= 0.001)
            {
                ProgressPath.Data = null;
                return;
            }

            double angle = Math.Clamp(Progress, 0, 1) * 359.9;
            double radius = Math.Max(1, (Math.Min(ActualWidth, ActualHeight) / 2.0) - RingThickness - 1);
            if (radius <= 1)
            {
                radius = 15;
            }

            Point center = new Point(ActualWidth > 0 ? ActualWidth / 2.0 : 18, ActualHeight > 0 ? ActualHeight / 2.0 : 18);
            Point start = new Point(center.X, center.Y - radius);
            double radians = (Math.PI / 180.0) * (angle - 90);
            Point end = new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));

            PathFigure figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                IsLargeArc = angle > 180,
                SweepDirection = SweepDirection.Clockwise
            });

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            ProgressPath.Data = geometry;
        }
    }
}
