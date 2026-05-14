using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Common;
using OceanyaClient.Features.ChatPreview;

namespace OceanyaClient
{
    /// <summary>
    /// AO2_SYNC_CHECK:
    /// This control is intended to be the reusable AO2-style chat preview surface.
    /// If AO2 chatbox rendering behavior changes, compare this control + AO2ChatPreviewResolver against AO2 courtroom chat rendering.
    /// </summary>
    public partial class AO2ChatPreviewControl : UserControl
    {
        private AO2ChatPreviewStyle? activeStyle;
        public static readonly DependencyProperty ChatTokenProperty = DependencyProperty.Register(
            nameof(ChatToken),
            typeof(string),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata("default", OnPreviewInputChanged));

        public static readonly DependencyProperty PreviewShownameProperty = DependencyProperty.Register(
            nameof(PreviewShowname),
            typeof(string),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata("Preview Name", OnPreviewInputChanged));

        public static readonly DependencyProperty PreviewTextProperty = DependencyProperty.Register(
            nameof(PreviewText),
            typeof(string),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata("Preview message.", OnPreviewInputChanged));

        public static readonly DependencyProperty ShowMessageProperty = DependencyProperty.Register(
            nameof(ShowMessage),
            typeof(bool),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata(true, OnPreviewInputChanged));

        public static readonly DependencyProperty ShowShownameProperty = DependencyProperty.Register(
            nameof(ShowShowname),
            typeof(bool),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata(true, OnPreviewInputChanged));

        public static readonly DependencyProperty ShowPreviewHeaderProperty = DependencyProperty.Register(
            nameof(ShowPreviewHeader),
            typeof(bool),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata(true, OnPreviewInputChanged));

        public static readonly DependencyProperty UseNativeViewportLayoutProperty = DependencyProperty.Register(
            nameof(UseNativeViewportLayout),
            typeof(bool),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata(false, OnPreviewInputChanged));

        public static readonly DependencyProperty ChatSectionBackgroundProperty = DependencyProperty.Register(
            nameof(ChatSectionBackground),
            typeof(Brush),
            typeof(AO2ChatPreviewControl),
            new PropertyMetadata(Brushes.Transparent, OnPreviewInputChanged));

        public AO2ChatPreviewControl()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshPreview();
        }

        public string ChatToken
        {
            get => (string)GetValue(ChatTokenProperty);
            set => SetValue(ChatTokenProperty, value);
        }

        public string PreviewShowname
        {
            get => (string)GetValue(PreviewShownameProperty);
            set => SetValue(PreviewShownameProperty, value);
        }

        public string PreviewText
        {
            get => (string)GetValue(PreviewTextProperty);
            set => SetValue(PreviewTextProperty, value);
        }

        public bool ShowMessage
        {
            get => (bool)GetValue(ShowMessageProperty);
            set => SetValue(ShowMessageProperty, value);
        }

        public bool ShowShowname
        {
            get => (bool)GetValue(ShowShownameProperty);
            set => SetValue(ShowShownameProperty, value);
        }

        public bool ShowPreviewHeader
        {
            get => (bool)GetValue(ShowPreviewHeaderProperty);
            set => SetValue(ShowPreviewHeaderProperty, value);
        }

        public bool UseNativeViewportLayout
        {
            get => (bool)GetValue(UseNativeViewportLayoutProperty);
            set => SetValue(UseNativeViewportLayoutProperty, value);
        }

        public Brush ChatSectionBackground
        {
            get => (Brush)GetValue(ChatSectionBackgroundProperty);
            set => SetValue(ChatSectionBackgroundProperty, value);
        }

        public Color? MessageColorOverride { get; set; }

        public int MessageColorIndex { get; set; }

        private static void OnPreviewInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AO2ChatPreviewControl control)
            {
                if (e.Property == PreviewTextProperty && control.IsLoaded)
                {
                    control.ApplyPreviewTextOnly(e.NewValue as string ?? string.Empty);
                    return;
                }

                control.RefreshPreview();
            }
        }

        /// <summary>
        /// Returns the resolved chat arrow position and size from the active style, or the FullChar default if no style is loaded.
        /// Coordinates are relative to the chatbox canvas top-left (same coordinate system as AO2's courtroom_design.ini).
        /// </summary>
        public AO2ChatPreviewBounds GetChatArrowBounds()
        {
            return activeStyle?.ChatArrowBounds ?? new AO2ChatPreviewBounds(245, 84, 11, 9);
        }

        public void RefreshPreview()
        {
            string showname = string.IsNullOrWhiteSpace(PreviewShowname) ? "Preview Name" : PreviewShowname.Trim();
            string text = UseNativeViewportLayout
                ? PreviewText ?? string.Empty
                : string.IsNullOrWhiteSpace(PreviewText)
                    ? "Preview message."
                    : PreviewText.Trim();
            bool hasShowname = !string.IsNullOrWhiteSpace(PreviewShowname);
            HeaderTextBlock.Visibility = ShowPreviewHeader ? Visibility.Visible : Visibility.Collapsed;
            HeaderRow.Height = ShowPreviewHeader ? GridLength.Auto : new GridLength(0);

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve(ChatToken, hasShowname, UseNativeViewportLayout);
            activeStyle = style;
            ApplyLayout(style);
            ApplyTextStyle(style, showname, text);
            ApplyDynamicShownameLayout(style, showname);
            ApplyChatboxImage(style.ChatboxImagePath);
        }

        private void ApplyLayout(AO2ChatPreviewStyle style)
        {
            if (UseNativeViewportLayout)
            {
                RootBorder.Padding = new Thickness(0);
                RootBorder.BorderThickness = new Thickness(0);
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.Background = Brushes.Transparent;
                PreviewFrame.BorderThickness = new Thickness(0);
                PreviewFrame.CornerRadius = new CornerRadius(0);
                PreviewFrame.Background = Brushes.Transparent;
                FallbackBackground.Background = Brushes.Transparent;
            }
            else
            {
                RootBorder.Padding = new Thickness(10);
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.CornerRadius = new CornerRadius(4);
                RootBorder.Background = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0));
                PreviewFrame.BorderThickness = new Thickness(1);
                PreviewFrame.CornerRadius = new CornerRadius(4);
                PreviewFrame.Background = new SolidColorBrush(Color.FromArgb(0x1B, 0, 0, 0));
                FallbackBackground.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x10, 0x10));
            }

            AO2ChatPreviewBounds chatbox = style.ChatboxBounds;
            ChatboxCanvas.Width = chatbox.Width;
            ChatboxCanvas.Height = chatbox.Height;
            ChatboxImage.Width = chatbox.Width;
            ChatboxImage.Height = chatbox.Height;
            FallbackBackground.Width = chatbox.Width;
            FallbackBackground.Height = chatbox.Height;
            ChatboxCanvas.Background = ChatSectionBackground;

            ApplyBounds(ShownameTextBlock, style.ShownameBounds);
            ApplyBounds(MessageContainer, style.MessageBounds);
        }

        private static void ApplyBounds(FrameworkElement element, AO2ChatPreviewBounds bounds)
        {
            Canvas.SetLeft(element, bounds.X);
            Canvas.SetTop(element, bounds.Y);
            element.Width = bounds.Width;
            element.Height = bounds.Height;
        }

        private void ApplyTextStyle(AO2ChatPreviewStyle style, string showname, string text)
        {
            ShownameTextBlock.Text = showname;
            ShownameTextBlock.Visibility = ShowShowname ? Visibility.Visible : Visibility.Collapsed;
            MessageContainer.Visibility = ShowMessage ? Visibility.Visible : Visibility.Collapsed;

            ShownameTextBlock.Foreground = new SolidColorBrush(style.ShownameColor);
            MessageTextBox.Foreground = new SolidColorBrush(GetMessageColor(style, MessageColorIndex));

            ShownameTextBlock.FontSize = style.ShownameFontSize;
            MessageTextBox.FontSize = style.MessageFontSize;

            ShownameTextBlock.FontWeight = style.ShownameBold ? FontWeights.Bold : FontWeights.Normal;
            MessageTextBox.FontWeight = style.MessageBold ? FontWeights.Bold : FontWeights.Normal;

            ShownameTextBlock.FontFamily = TryCreateFont(style.ShownameFontFamily) ?? new FontFamily("Arial");
            MessageTextBox.FontFamily = TryCreateFont(style.MessageFontFamily) ?? new FontFamily("Arial");
            ShownameTextBlock.TextAlignment = style.ShownameTextAlignment;
            ApplyFormattedMessageText(style, text);
            ScrollMessageToCurrentTextEnd();

            if (style.ShownameOutlined)
            {
                ShownameTextBlock.Effect = new DropShadowEffect
                {
                    Color = style.ShownameOutlineColor,
                    ShadowDepth = 0,
                    BlurRadius = Math.Max(0, style.ShownameOutlineWidth),
                    Opacity = 1.0
                };
            }
            else
            {
                ShownameTextBlock.Effect = null;
            }
        }

        private void ApplyPreviewTextOnly(string rawText)
        {
            string text = UseNativeViewportLayout
                ? rawText ?? string.Empty
                : string.IsNullOrWhiteSpace(rawText)
                    ? "Preview message."
                    : rawText.Trim();

            ApplyFormattedMessageText(
                activeStyle ?? AO2ChatPreviewResolver.Resolve(
                    ChatToken,
                    !string.IsNullOrWhiteSpace(PreviewShowname),
                    UseNativeViewportLayout),
                text);
            ScrollMessageToCurrentTextEnd();
        }

        private void ScrollMessageToCurrentTextEnd()
        {
            MessageTextBox.CaretPosition = MessageTextBox.Document.ContentEnd;
            MessageTextBox.ScrollToEnd();
            _ = MessageTextBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() =>
                {
                    MessageTextBox.CaretPosition = MessageTextBox.Document.ContentEnd;
                    MessageTextBox.ScrollToEnd();
                }));
        }

        private void ApplyFormattedMessageText(AO2ChatPreviewStyle style, string rawText)
        {
            MessageTextBox.Document.Blocks.Clear();
            MessageTextBox.Document.PagePadding = new Thickness(4);
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                TextAlignment = AO2ChatTextFormatter.ResolveMessageAlignment(rawText, out string text)
            };

            foreach (AO2FormattedTextSegment segment in AO2ChatTextFormatter.EnumerateFormattedTextSegments(
                style,
                text,
                MessageColorIndex,
                MessageColorOverride))
            {
                paragraph.Inlines.Add(new Run(segment.Text)
                {
                    Foreground = new SolidColorBrush(segment.Color)
                });
            }

            MessageTextBox.Document.Blocks.Add(paragraph);
        }

        private Color GetMessageColor(AO2ChatPreviewStyle style, int colorIndex)
        {
            return AO2ChatTextFormatter.GetMessageColor(style, colorIndex, MessageColorIndex, MessageColorOverride);
        }

        private void ApplyDynamicShownameLayout(AO2ChatPreviewStyle style, string showname)
        {
            if (!ShowShowname || string.IsNullOrWhiteSpace(showname) || style.ShownameExtraWidth <= 0)
            {
                return;
            }

            double measuredWidth = MeasureShownameWidth(showname, style);
            AO2ChatPreviewBounds defaultBounds = style.ShownameBounds;
            string? baseImagePath = style.ChatboxImagePath;
            if (measuredWidth > defaultBounds.Width)
            {
                string? mediumImagePath = AO2ChatPreviewResolver.ResolveSiblingImageVariant(baseImagePath, "med");
                if (!string.IsNullOrWhiteSpace(mediumImagePath))
                {
                    style.ChatboxImagePath = mediumImagePath;
                    ShownameTextBlock.Width = defaultBounds.Width + style.ShownameExtraWidth;
                }
            }

            if (measuredWidth > ShownameTextBlock.Width)
            {
                string? bigImagePath = AO2ChatPreviewResolver.ResolveSiblingImageVariant(baseImagePath, "big");
                if (!string.IsNullOrWhiteSpace(bigImagePath))
                {
                    style.ChatboxImagePath = bigImagePath;
                    ShownameTextBlock.Width = defaultBounds.Width + (style.ShownameExtraWidth * 2);
                }
            }
        }

        private double MeasureShownameWidth(string showname, AO2ChatPreviewStyle style)
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Typeface typeface = new Typeface(
                ShownameTextBlock.FontFamily,
                ShownameTextBlock.FontStyle,
                style.ShownameBold ? FontWeights.Bold : FontWeights.Normal,
                ShownameTextBlock.FontStretch);
            FormattedText formattedText = new FormattedText(
                showname,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                style.ShownameFontSize,
                Brushes.White,
                pixelsPerDip);
            return formattedText.WidthIncludingTrailingWhitespace;
        }

        private void ApplyChatboxImage(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                ChatboxImage.Source = null;
                FallbackBackground.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(imagePath, UriKind.Absolute);
                image.EndInit();
                if (image.CanFreeze)
                {
                    image.Freeze();
                }

                ChatboxImage.Source = image;
                FallbackBackground.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ChatboxImage.Source = null;
                FallbackBackground.Visibility = Visibility.Visible;
                CustomConsole.Warning("Could not load chat preview image: " + imagePath, ex);
            }
        }

        private static FontFamily? TryCreateFont(string? fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
            {
                return null;
            }

            try
            {
                return new FontFamily(fontName.Trim());
            }
            catch
            {
                return null;
            }
        }
    }
}
