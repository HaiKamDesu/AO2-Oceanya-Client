using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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

        private static void OnPreviewInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AO2ChatPreviewControl control)
            {
                control.RefreshPreview();
            }
        }

        public void RefreshPreview()
        {
            string showname = string.IsNullOrWhiteSpace(PreviewShowname) ? "Preview Name" : PreviewShowname.Trim();
            string text = string.IsNullOrWhiteSpace(PreviewText) ? "Preview message." : PreviewText.Trim();
            bool hasShowname = !string.IsNullOrWhiteSpace(PreviewShowname);

            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve(ChatToken, hasShowname);
            ApplyTextStyle(style, showname, text);
            ApplyChatboxImage(style.ChatboxImagePath);
        }

        private void ApplyTextStyle(AO2ChatPreviewStyle style, string showname, string text)
        {
            ShownameTextBlock.Text = showname;
            MessageTextBlock.Text = text;
            ShownameTextBlock.Visibility = ShowShowname ? Visibility.Visible : Visibility.Collapsed;
            MessageContainer.Visibility = ShowMessage ? Visibility.Visible : Visibility.Collapsed;

            ShownameTextBlock.Foreground = new SolidColorBrush(style.ShownameColor);
            MessageTextBlock.Foreground = new SolidColorBrush(style.MessageColor);

            ShownameTextBlock.FontSize = style.ShownameFontSize;
            MessageTextBlock.FontSize = style.MessageFontSize;

            ShownameTextBlock.FontWeight = style.ShownameBold ? FontWeights.Bold : FontWeights.Normal;
            MessageTextBlock.FontWeight = style.MessageBold ? FontWeights.Bold : FontWeights.Normal;

            ShownameTextBlock.FontFamily = TryCreateFont(style.ShownameFontFamily) ?? new FontFamily("Arial");
            MessageTextBlock.FontFamily = TryCreateFont(style.MessageFontFamily) ?? new FontFamily("Arial");

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
