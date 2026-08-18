using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OceanyaClient.Components;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Applies per-panel style settings that depend on the panel's control kind.
    /// </summary>
    /// <remarks>
    /// Each control kind is customizable in its own terms: image buttons expose image scaling, text
    /// boxes and dropdowns expose a font size (which also drives their height, because stretching text
    /// controls vertically only adds dead space), and item grids expose an item size that decides how
    /// many entries fit. See <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public static class OceanyaPanelStyleApplier
    {
        private static readonly Dictionary<string, List<PanelVisualBaseline>> Baselines =
            new Dictionary<string, List<PanelVisualBaseline>>(StringComparer.Ordinal);

        /// <summary>
        /// Applies a panel's saved style settings to its element.
        /// </summary>
        /// <param name="descriptor">Panel descriptor supplying the kind.</param>
        /// <param name="element">Panel element to style.</param>
        /// <param name="state">Saved state, or null when the panel uses its defaults.</param>
        public static void Apply(
            OceanyaPanelDescriptor descriptor,
            FrameworkElement element,
            OceanyaPanelPlacementState? state)
        {
            if (state == null)
            {
                return;
            }

            CaptureBaseline(descriptor.Id, element);
            ApplyZOrder(element, state.ZOrder);
            ApplyOpacity(element, state.Opacity);
            ApplyBackgroundColor(element, state.BackgroundColor);

            switch (descriptor.Kind)
            {
                case OceanyaPanelKind.TextInput:
                case OceanyaPanelKind.Dropdown:
                case OceanyaPanelKind.TextToggle:
                    ApplyFont(element, state);
                    break;
                case OceanyaPanelKind.ImageButton:
                    ApplyImageScaling(element, state.ImageScaling);
                    ApplyImageOverride(element, state.ImagePath);
                    break;
                case OceanyaPanelKind.ItemGrid:
                    ApplyItemSize(element, state.ItemSize);
                    break;
            }
        }

        /// <summary>
        /// Records a panel's untouched font and image state, once, so it can be restored later.
        /// </summary>
        /// <param name="panelId">Panel id the baseline belongs to.</param>
        /// <param name="element">Panel element to capture.</param>
        public static void CaptureBaseline(string panelId, FrameworkElement element)
        {
            if (Baselines.ContainsKey(panelId))
            {
                return;
            }

            List<PanelVisualBaseline> captured = new List<PanelVisualBaseline> { PanelVisualBaseline.From(element) };
            CaptureBaselineRecursive(element, captured);
            Baselines[panelId] = captured;
        }

        /// <summary>
        /// Restores a panel's fonts, images and stacking to the state captured before it was styled.
        /// </summary>
        /// <param name="panelId">Panel id to restore.</param>
        /// <param name="element">Panel element being restored.</param>
        public static void RestoreBaseline(string panelId, FrameworkElement element)
        {
            if (!Baselines.TryGetValue(panelId, out List<PanelVisualBaseline>? captured))
            {
                return;
            }

            foreach (PanelVisualBaseline baseline in captured)
            {
                baseline.Restore();
            }

            Baselines.Remove(panelId);
        }

        private static void CaptureBaselineRecursive(DependencyObject element, List<PanelVisualBaseline> captured)
        {
            if (element is Control or TextBlock or Image)
            {
                captured.Add(PanelVisualBaseline.From(element));
            }


            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                CaptureBaselineRecursive(VisualTreeHelper.GetChild(element, i), captured);
            }
        }

        /// <summary>
        /// A single element's pre-styling appearance, recorded as *local* values.
        /// </summary>
        /// <remarks>
        /// Reading the effective value and writing it back later turns a style-provided value into a
        /// local one, and writing back a captured null wipes the style's value entirely - which is how
        /// buttons ended up with no background after a reset. Recording whether each property had a
        /// local value, and clearing it when it did not, restores the original state exactly.
        /// </remarks>
        private sealed class PanelVisualBaseline
        {
            private static readonly DependencyProperty[] TrackedProperties =
            {
                Control.FontSizeProperty,
                Control.FontFamilyProperty,
                Control.FontWeightProperty,
                Control.FontStyleProperty,
                Control.ForegroundProperty,
                Control.BackgroundProperty,
                TextBlock.FontSizeProperty,
                TextBlock.FontFamilyProperty,
                TextBlock.FontWeightProperty,
                TextBlock.FontStyleProperty,
                TextBlock.ForegroundProperty,
                TextBlock.TextDecorationsProperty,
                Image.SourceProperty,
                Image.StretchProperty,
                FrameworkElement.HeightProperty,
                UIElement.OpacityProperty,
                Panel.ZIndexProperty
            };

            private readonly DependencyObject target;
            private readonly Dictionary<DependencyProperty, object?> localValues = new Dictionary<DependencyProperty, object?>();

            private PanelVisualBaseline(DependencyObject target)
            {
                this.target = target;
            }

            /// <summary>
            /// Captures which of the tracked properties currently carry a local value.
            /// </summary>
            /// <param name="element">Element to capture.</param>
            /// <returns>The captured baseline.</returns>
            public static PanelVisualBaseline From(DependencyObject element)
            {
                PanelVisualBaseline baseline = new PanelVisualBaseline(element);
                foreach (DependencyProperty property in TrackedProperties)
                {
                    if (!IsApplicable(element, property))
                    {
                        continue;
                    }

                    // Keep UnsetValue as the marker for "no local value": storing null instead cannot be
                    // told apart from a local Background="{x:Null}", and clearing that gave the control
                    // WPF's default (white) background back.
                    baseline.localValues[property] = element.ReadLocalValue(property);
                }

                return baseline;
            }

            /// <summary>
            /// Puts every tracked property back: the original local value, or cleared when it had none.
            /// </summary>
            public void Restore()
            {
                foreach (KeyValuePair<DependencyProperty, object?> pair in localValues)
                {
                    if (pair.Value == DependencyProperty.UnsetValue)
                    {
                        target.ClearValue(pair.Key);
                    }
                    else
                    {
                        target.SetValue(pair.Key, pair.Value);
                    }
                }
            }

            private static bool IsApplicable(DependencyObject element, DependencyProperty property)
            {
                return element switch
                {
                    Image => property == Image.SourceProperty
                        || property == Image.StretchProperty
                        || property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty,
                    TextBlock => property != Control.BackgroundProperty && property != Image.SourceProperty && property != Image.StretchProperty,
                    Control => property != TextBlock.TextDecorationsProperty && property != Image.SourceProperty && property != Image.StretchProperty,
                    _ => property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty
                };
            }
        }

        /// <summary>
        /// Resolves the height a text-like panel should have for a font size, so its box hugs the text.
        /// </summary>
        /// <param name="fontSize">Font size in device independent pixels.</param>
        /// <returns>The panel height to use.</returns>
        public static double ResolveTextPanelHeight(double fontSize)
        {
            return Math.Round(fontSize * 1.45) + 4;
        }

        private static void ApplyFont(FrameworkElement element, OceanyaPanelPlacementState state)
        {
            FontFamily? family = null;
            if (!string.IsNullOrWhiteSpace(state.FontFamily))
            {
                try
                {
                    family = new FontFamily(state.FontFamily);
                }
                catch (Exception)
                {
                    // An unavailable family falls back to the control's own font.
                }
            }

            FontWeight? weight = state.IsBold ? FontWeights.Bold : null;
            FontStyle? style = state.IsItalic ? FontStyles.Italic : null;
            Brush? foreground = TryParseBrush(state.TextColor);
            bool underline = state.IsUnderlined;
            if (state.FontSize <= 0 && family == null && !state.IsBold && !state.IsItalic && !underline && foreground == null)
            {
                return;
            }

            SetFontRecursive(element, state.FontSize, family, weight, style, foreground, underline);
            if (state.FontSize > 0)
            {
                element.Height = ResolveTextPanelHeight(state.FontSize);
            }
        }

        private static void SetFontRecursive(
            DependencyObject element,
            double fontSize,
            FontFamily? family,
            FontWeight? weight,
            FontStyle? style,
            Brush? foreground,
            bool underline)
        {
            if (element is Control control)
            {
                if (style.HasValue)
                {
                    control.FontStyle = style.Value;
                }

                if (foreground != null)
                {
                    control.Foreground = foreground;
                }

                if (fontSize > 0)
                {
                    control.FontSize = fontSize;
                }

                if (family != null)
                {
                    control.FontFamily = family;
                }

                if (weight.HasValue)
                {
                    control.FontWeight = weight.Value;
                }
            }
            else if (element is TextBlock textBlock)
            {
                if (fontSize > 0)
                {
                    textBlock.FontSize = fontSize;
                }

                if (family != null)
                {
                    textBlock.FontFamily = family;
                }

                if (weight.HasValue)
                {
                    textBlock.FontWeight = weight.Value;
                }

                if (style.HasValue)
                {
                    textBlock.FontStyle = style.Value;
                }

                if (foreground != null)
                {
                    textBlock.Foreground = foreground;
                }

                if (underline)
                {
                    textBlock.TextDecorations = TextDecorations.Underline;
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                SetFontRecursive(VisualTreeHelper.GetChild(element, i), fontSize, family, weight, style, foreground, underline);
            }
        }

        /// <summary>
        /// Applies a stacking order so overlapping panels draw in a predictable order.
        /// </summary>
        /// <param name="element">Panel element.</param>
        /// <param name="zOrder">Stacking order; zero leaves the default.</param>
        public static void ApplyZOrder(FrameworkElement element, int zOrder)
        {
            if (zOrder != 0)
            {
                Panel.SetZIndex(element, zOrder);
            }
        }

        /// <summary>
        /// Applies a panel opacity, letting a picture or colour block be faded.
        /// </summary>
        /// <param name="element">Panel element.</param>
        /// <param name="opacity">Opacity between 0 and 1; zero leaves it untouched.</param>
        private static void ApplyOpacity(FrameworkElement element, double opacity)
        {
            if (opacity > 0 && opacity <= 1)
            {
                element.Opacity = opacity;
            }
        }

        /// <summary>
        /// Applies a background colour behind a panel's content.
        /// </summary>
        /// <param name="element">Panel element.</param>
        /// <param name="backgroundColor">Colour string, or empty to leave it alone.</param>
        private static void ApplyBackgroundColor(FrameworkElement element, string backgroundColor)
        {
            Brush? brush = TryParseBrush(backgroundColor);
            if (brush == null)
            {
                return;
            }

            switch (element)
            {
                case Control control:
                    control.Background = brush;
                    break;
                case Panel panel:
                    panel.Background = brush;
                    break;
                case Border border:
                    border.Background = brush;
                    break;
                case System.Windows.Shapes.Shape shape:
                    shape.Fill = brush;
                    break;
            }
        }

        /// <summary>
        /// Parses a colour string into a frozen brush.
        /// </summary>
        /// <param name="color">Colour in #AARRGGBB or #RRGGBB form, or a named colour.</param>
        /// <returns>The brush, or null when the value is empty or unparseable.</returns>
        public static Brush? TryParseBrush(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return null;
            }

            try
            {
                if (ColorConverter.ConvertFromString(color) is Color parsed)
                {
                    SolidColorBrush brush = new SolidColorBrush(parsed);
                    brush.Freeze();
                    return brush;
                }
            }
            catch (FormatException)
            {
                // An unparseable colour leaves the control's own brush in place.
            }

            return null;
        }

        private static void ApplyImageOverride(FrameworkElement element, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
            {
                return;
            }

            // The face lives in the control template, so it is only present once rendered.
            element.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ImageSource source = OceanyaClient.Utilities.BitmapFileLoader.LoadFrozen(imagePath);
                    SetImageSourceRecursive(element, source);
                }
                catch (Exception exception)
                {
                    Common.CustomConsole.Warning($"Panel image override could not be loaded: {imagePath}", exception);
                }
            }));
        }

        private static void SetImageSourceRecursive(DependencyObject element, ImageSource source)
        {
            if (element is Image image)
            {
                image.Source = source;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                SetImageSourceRecursive(VisualTreeHelper.GetChild(element, i), source);
            }
        }

        private static void ApplyImageScaling(FrameworkElement element, string imageScaling)
        {
            if (string.IsNullOrWhiteSpace(imageScaling)
                || !Enum.TryParse(imageScaling, ignoreCase: true, out Stretch stretch))
            {
                return;
            }

            // The face of an image button lives inside its control template, so it is found by walking
            // the rendered tree rather than by name.
            element.Dispatcher.BeginInvoke(new Action(() => SetImageStretchRecursive(element, stretch)));
        }

        private static void SetImageStretchRecursive(DependencyObject element, Stretch stretch)
        {
            if (element is Image image)
            {
                image.Stretch = stretch;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                SetImageStretchRecursive(VisualTreeHelper.GetChild(element, i), stretch);
            }
        }

        private static void ApplyItemSize(FrameworkElement element, double itemSize)
        {
            if (itemSize <= 0)
            {
                return;
            }

            PageButtonGrid? grid = element as PageButtonGrid ?? FindGrid(element);
            grid?.EnableAutomaticPageSize(itemSize);
        }

        private static PageButtonGrid? FindGrid(DependencyObject element)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, i);
                if (child is PageButtonGrid grid)
                {
                    return grid;
                }

                PageButtonGrid? nested = FindGrid(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
