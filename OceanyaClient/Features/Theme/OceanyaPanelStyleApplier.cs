using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        /// <summary>Override key for the hover/checked artwork handlers.</summary>
        private const string StateArtOverrideKey = "stateArt";

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
            element.IsHitTestVisible = !state.IsClickThrough;
            ApplyOpacity(element, state.Opacity);
            ApplyBackgroundColor(element, state.BackgroundColor);
            ApplyBorder(element, state);
            ApplyScrollbarColours(descriptor.Id, element, state);

            switch (descriptor.Kind)
            {
                case OceanyaPanelKind.Static:
                    // Logs are static panels that still show text, and AO2 themes their fonts; backdrops and
                    // decorative label art are static too, and carry an image instead.
                    ApplyFont(element, state, resizeToFont: false);
                    ApplyImageOverride(descriptor.Id, element, state);
                    break;
                case OceanyaPanelKind.Slider:
                    ApplySliderArt(element, state);
                    break;
                case OceanyaPanelKind.TextInput:
                case OceanyaPanelKind.TextToggle:
                    ApplyFont(element, state, resizeToFont: true);
                    ApplyInnerSurface(element, state);
                    ApplyIndicatorArt(descriptor.Id, element, state);
                    break;
                case OceanyaPanelKind.Dropdown:
                    ApplyFont(element, state, resizeToFont: true);
                    ApplyInnerSurface(element, state);
                    ApplyDropdownPopupColours(descriptor.Id, element, state);
                    ApplyIndicatorArt(descriptor.Id, element, state);
                    break;
                case OceanyaPanelKind.ImageButton:
                    ApplyImageScaling(element, state.ImageScaling);
                    ApplyImageOverride(descriptor.Id, element, state);
                    break;
                case OceanyaPanelKind.ItemGrid:
                    ApplyGridMetrics(descriptor.Id, element, state);
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
                // Keep scanning: the first capture happens before the window has rendered, so a panel's
                // control template - and every text element inside it - did not exist yet. Without this,
                // a reset could not restore what was never recorded.
                AppendBaseline(panelId, element);
                return;
            }

            List<PanelVisualBaseline> captured = new List<PanelVisualBaseline> { PanelVisualBaseline.From(element) };
            CaptureBaselineRecursive(element, captured);
            Baselines[panelId] = captured;
        }

        /// <summary>
        /// Adds baselines for elements that appeared after the first capture.
        /// </summary>
        /// <remarks>
        /// A button's face lives in its control template, which does not exist until the panel renders.
        /// Overriding artwork therefore happens on a dispatcher callback, and without re-capturing at that
        /// point the original artwork was never recorded - so a reset could not put it back.
        /// </remarks>
        /// <param name="panelId">Panel id the baseline belongs to.</param>
        /// <param name="element">Panel element to re-scan.</param>
        public static void AppendBaseline(string panelId, FrameworkElement element)
        {
            if (!Baselines.TryGetValue(panelId, out List<PanelVisualBaseline>? captured))
            {
                CaptureBaseline(panelId, element);
                return;
            }

            List<PanelVisualBaseline> found = new List<PanelVisualBaseline>();
            CaptureBaselineRecursive(element, found);
            foreach (PanelVisualBaseline baseline in found)
            {
                if (!captured.Any(existing => existing.TracksSameTarget(baseline)))
                {
                    captured.Add(baseline);
                }
            }
        }

        /// <summary>
        /// Restores a panel's fonts, images and stacking to the state captured before it was styled.
        /// </summary>
        /// <param name="panelId">Panel id to restore.</param>
        /// <param name="element">Panel element being restored.</param>
        public static void RestoreBaseline(string panelId, FrameworkElement element)
        {
            // Runs before the early return: overrides that are not dependency properties are tracked
            // separately, and a panel with nothing in the visual baseline can still have handlers on it.
            PanelStateOverrides.Restore(element);
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
            if (element is Control or TextBlock or Image or System.Windows.Shapes.Shape
                or Components.Panels.ShoutButtonPanel or Border or Panel)
            {
                captured.Add(PanelVisualBaseline.From(element));
            }

            // A dropdown's popup lives outside the visual tree until it is first opened, so it has to be
            // reached through the template or its colours could be changed and never restored.
            if (TryResolvePopupSurface(element) is Border popupSurface)
            {
                captured.Add(PanelVisualBaseline.From(popupSurface));
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
                Control.BorderBrushProperty,
                Control.BorderThicknessProperty,
                Control.TemplateProperty,
                Control.PaddingProperty,
                Border.BackgroundProperty,
                Border.BorderBrushProperty,
                Border.BorderThicknessProperty,
                Panel.BackgroundProperty,
                TextBlock.FontSizeProperty,
                TextBlock.FontFamilyProperty,
                TextBlock.FontWeightProperty,
                TextBlock.FontStyleProperty,
                TextBlock.ForegroundProperty,
                TextBlock.TextDecorationsProperty,
                Image.SourceProperty,
                Image.StretchProperty,
                System.Windows.Shapes.Shape.FillProperty,
                UIElement.OpacityMaskProperty,
                Components.Panels.ShoutButtonPanel.UncheckedImageProperty,
                Components.Panels.ShoutButtonPanel.CheckedImageProperty,
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
            /// Gets a value indicating whether another baseline records the same element.
            /// </summary>
            /// <param name="other">Baseline to compare against.</param>
            /// <returns>True when both track the same element.</returns>
            public bool TracksSameTarget(PanelVisualBaseline other)
            {
                return ReferenceEquals(target, other.target);
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
                    Components.Panels.ShoutButtonPanel => property == Components.Panels.ShoutButtonPanel.UncheckedImageProperty
                        || property == Components.Panels.ShoutButtonPanel.CheckedImageProperty
                        || property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty,
                    Image => property == Image.SourceProperty
                        || property == Image.StretchProperty
                        || property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty,
                    System.Windows.Shapes.Shape => property == System.Windows.Shapes.Shape.FillProperty
                        || property == UIElement.OpacityMaskProperty
                        || property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty,
                    Panel => property == Panel.BackgroundProperty
                        || property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty,
                    Border => property == Border.BackgroundProperty
                        || property == Border.BorderBrushProperty
                        || property == Border.BorderThicknessProperty
                        || property == FrameworkElement.HeightProperty
                        || property == UIElement.OpacityProperty
                        || property == Panel.ZIndexProperty,
                    TextBlock => property != Control.BackgroundProperty
                        && property != Control.BorderBrushProperty
                        && property != Control.BorderThicknessProperty
                        && property != Control.TemplateProperty
                        && property != Image.SourceProperty
                        && property != Image.StretchProperty,
                    // Careful: Control/Border/Panel Background are the SAME dependency property (WPF shares
                    // it through AddOwner), so excluding "Panel.BackgroundProperty" here would drop a
                    // control's background from the baseline entirely.
                    // Visibility and Content are deliberately NOT tracked: both are managed at runtime
                    // (placeholder captions appear and disappear, hosts are filled in code), so restoring a
                    // value captured at some earlier moment fought that code - it put a placeholder caption
                    // back on top of real text. Styling that touches them registers its own undo instead.
                    Control => property != TextBlock.TextDecorationsProperty
                        && property != Image.SourceProperty
                        && property != Image.StretchProperty,
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

        private static void ApplyFont(FrameworkElement element, OceanyaPanelPlacementState state, bool resizeToFont)
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
                DropLabelPaddingRecursive(element);
            }

            if (resizeToFont && state.FontSize > 0)
            {
                // Only single-line text boxes hug their font, and only ever to GROW: in AO2 a widget's
                // geometry comes from the design file and owes nothing to its font, so shrinking a panel to
                // its font size clipped the text of any panel the theme sized deliberately.
                double fontHeight = ResolveTextPanelHeight(state.FontSize);
                double currentHeight = double.IsNaN(element.Height) ? element.ActualHeight : element.Height;
                element.Height = Math.Max(fontHeight, currentHeight);
            }
        }

        /// <summary>
        /// Removes the padding a WPF label adds around its text.
        /// </summary>
        /// <remarks>
        /// A `Label` defaults to 5px padding on every side, which a Qt label does not have. On a panel sized
        /// by the theme that padding eats 10px of height, and with a smaller theme font the bottom half of
        /// the glyphs was clipped - the OOC header being the visible case.
        /// </remarks>
        /// <param name="element">Element to walk.</param>
        private static void DropLabelPaddingRecursive(DependencyObject element)
        {
            if (element is Label label)
            {
                label.Padding = new Thickness(0);
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                DropLabelPaddingRecursive(VisualTreeHelper.GetChild(element, i));
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

        /// <summary>
        /// Applies a panel's border colour and width.
        /// </summary>
        /// <remarks>
        /// AO2 widgets draw no frame of their own unless the theme's stylesheet asks for one, so a theme
        /// needs to be able to both add and remove a border - which is why zero is a real value here and
        /// "leave it alone" is negative.
        /// </remarks>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplyBorder(FrameworkElement element, OceanyaPanelPlacementState state)
        {
            Brush? brush = TryParseBrush(state.BorderColor);
            bool setsWidth = state.BorderThickness >= 0;
            if (brush == null && !setsWidth)
            {
                return;
            }

            element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                ApplyBorderRecursive(element, brush, setsWidth ? state.BorderThickness : null)));
        }

        private static void ApplyBorderRecursive(DependencyObject element, Brush? brush, double? thickness)
        {
            switch (element)
            {
                case Control control:
                    if (brush != null)
                    {
                        control.BorderBrush = brush;
                    }

                    if (thickness.HasValue)
                    {
                        control.BorderThickness = new Thickness(thickness.Value);
                    }

                    break;
                case Border border:
                    if (brush != null)
                    {
                        border.BorderBrush = brush;
                    }

                    if (thickness.HasValue)
                    {
                        border.BorderThickness = new Thickness(thickness.Value);
                    }

                    break;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ApplyBorderRecursive(VisualTreeHelper.GetChild(element, i), brush, thickness);
            }
        }

        /// <summary>
        /// Pushes a text panel's colours onto the control inside it.
        /// </summary>
        /// <remarks>
        /// Text panels are a container plus the real control (a text box with its own background, a
        /// dropdown with its own), so painting only the container left the theme's colour hidden behind
        /// the control's own.
        /// </remarks>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplyInnerSurface(FrameworkElement element, OceanyaPanelPlacementState state)
        {
            Brush? background = TryParseBrush(state.BackgroundColor);
            if (background == null)
            {
                return;
            }

            element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                ApplyInnerSurfaceRecursive(element, background)));
        }

        private static void ApplyInnerSurfaceRecursive(DependencyObject element, Brush background)
        {
            if (element is TextBox or ComboBox or CheckBox)
            {
                ((Control)element).Background = background;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ApplyInnerSurfaceRecursive(VisualTreeHelper.GetChild(element, i), background);
            }
        }

        /// <summary>
        /// Replaces a checkbox's tick box or a dropdown's arrow with the theme's own artwork.
        /// </summary>
        /// <remarks>
        /// AO2 themes skin these through Qt sub-controls (`QCheckBox::indicator`, `QComboBox::down-arrow`),
        /// so both need a field of their own. A checkbox gets a generated template holding the artwork; a
        /// dropdown's arrow button is painted with it and its vector glyph hidden.
        /// </remarks>
        /// <param name="descriptorId">Panel id, for baseline bookkeeping.</param>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplyIndicatorArt(string descriptorId, FrameworkElement element, OceanyaPanelPlacementState state)
        {
            ImageSource? indicator = TryLoadImage(state.IndicatorImagePath);
            if (indicator == null && state.IndicatorWidth <= 0)
            {
                return;
            }

            ImageSource? checkedIndicator = TryLoadImage(state.CheckedIndicatorImagePath) ?? indicator;

            element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                AppendBaseline(descriptorId, element);
                CheckBox? checkBox = element as CheckBox ?? FindDescendant<CheckBox>(element);
                if (checkBox != null)
                {
                    if (indicator != null)
                    {
                        ApplyCheckBoxIndicator(checkBox, indicator, checkedIndicator ?? indicator, TryLoadImage(state.ImagePath));
                    }

                    return;
                }

                ComboBox? comboBox = element as ComboBox ?? FindDescendant<ComboBox>(element);
                if (comboBox != null)
                {
                    ApplyDropdownArrow(element, comboBox, indicator, state.IndicatorWidth);
                }
            }));
        }

        /// <summary>
        /// Gives a checkbox a template whose tick box is the theme's artwork.
        /// </summary>
        /// <param name="checkBox">Checkbox to restyle.</param>
        /// <param name="indicator">Unticked artwork.</param>
        /// <param name="checkedIndicator">Ticked artwork.</param>
        /// <param name="labelArt">Artwork that replaces the label text, or null to keep the text.</param>
        private static void ApplyCheckBoxIndicator(
            CheckBox checkBox,
            ImageSource indicator,
            ImageSource checkedIndicator,
            ImageSource? labelArt)
        {
            // Qt draws these two things LAYERED, not side by side: the widget's own `image` covers the
            // whole widget (caption art that already includes an empty tick box) and `::indicator` paints
            // the interactive box on top of it, at its own size, aligned left. Laying them out side by side
            // showed the symbol twice.
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));

            if (labelArt != null)
            {
                FrameworkElementFactory caption = new FrameworkElementFactory(typeof(Image));
                caption.SetValue(Image.SourceProperty, labelArt);
                caption.SetValue(Image.StretchProperty, Stretch.Fill);
                root.AppendChild(caption);
            }

            FrameworkElementFactory box = new FrameworkElementFactory(typeof(Image), "IndicatorImage");
            box.SetValue(Image.SourceProperty, indicator);
            box.SetValue(Image.StretchProperty, Stretch.Uniform);
            box.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (indicator is BitmapSource sizedIndicator)
            {
                // Its own size, like Qt: stretching it to the widget would cover the caption art.
                box.SetValue(FrameworkElement.WidthProperty, (double)sizedIndicator.PixelWidth);
                box.SetValue(FrameworkElement.HeightProperty, (double)sizedIndicator.PixelHeight);
            }

            root.AppendChild(box);

            if (labelArt == null)
            {
                FrameworkElementFactory label = new FrameworkElementFactory(typeof(ContentPresenter));
                label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                label.SetValue(FrameworkElement.MarginProperty, new Thickness(
                    indicator is BitmapSource offset ? offset.PixelWidth + 4 : 19,
                    0,
                    0,
                    0));
                root.AppendChild(label);
            }

            ControlTemplate template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };
            Trigger ticked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            ticked.Setters.Add(new Setter(Image.SourceProperty, checkedIndicator, "IndicatorImage"));
            template.Triggers.Add(ticked);
            checkBox.Template = template;
        }

        /// <summary>
        /// Paints a dropdown's arrow button with the theme's artwork.
        /// </summary>
        /// <param name="panelRoot">Panel element the undo is recorded against.</param>
        /// <param name="comboBox">Dropdown to restyle.</param>
        /// <param name="arrow">Arrow artwork, or null to only change the width.</param>
        /// <param name="arrowWidth">Width of the arrow button; zero keeps the control's own.</param>
        private static void ApplyDropdownArrow(
            FrameworkElement panelRoot,
            ComboBox comboBox,
            ImageSource? arrow,
            double arrowWidth)
        {
            if (comboBox.Template == null)
            {
                return;
            }

            try
            {
                if (arrow != null && comboBox.Template.FindName("btnDropdown", comboBox) is ToggleButton dropdownButton)
                {
                    dropdownButton.Background = new ImageBrush(arrow) { Stretch = Stretch.Uniform };
                    if (FindDescendant<System.Windows.Shapes.Shape>(dropdownButton) is System.Windows.Shapes.Shape glyph)
                    {
                        Visibility previous = glyph.Visibility;
                        glyph.Visibility = Visibility.Collapsed;
                        PanelStateOverrides.Register(
                            panelRoot,
                            "arrowGlyph",
                            () => glyph.Visibility = previous);
                    }
                }

                if (arrowWidth > 0 && comboBox.Template.FindName("columnDropdown", comboBox) is ColumnDefinition column)
                {
                    // A ColumnDefinition is not part of the element walk the visual baseline does, so its
                    // width needs an undo of its own.
                    GridLength previousWidth = column.Width;
                    column.Width = new GridLength(arrowWidth);
                    PanelStateOverrides.Register(
                        panelRoot,
                        "arrowWidth",
                        () => column.Width = previousWidth);
                }
            }
            catch (InvalidOperationException)
            {
                // The template has not been applied yet.
            }
        }

        /// <summary>
        /// Restyles the scrollbars inside a panel with the theme's colours.
        /// </summary>
        /// <remarks>
        /// AO2 styles `QScrollBar` globally, so the colours have to reach the scrollbars our logs, lists
        /// and dropdowns own. The style and its brushes are put into the panel's own resource scope, which
        /// keeps it per-panel and makes the undo a matter of removing them again.
        /// </remarks>
        /// <param name="descriptorId">Panel id, for restore bookkeeping.</param>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplyScrollbarColours(string descriptorId, FrameworkElement element, OceanyaPanelPlacementState state)
        {
            Brush? track = TryParseBrush(state.ScrollbarTrackColor);
            Brush? handle = TryParseBrush(state.ScrollbarHandleColor);
            if (track == null && handle == null)
            {
                return;
            }

            Style? themedStyle = TryResolveScrollBarStyle();
            if (themedStyle == null)
            {
                return;
            }

            Brush? border = TryParseBrush(state.ScrollbarBorderColor);
            PanelStateOverrides.Register(element, "scrollbars", () =>
            {
                element.Resources.Remove(typeof(ScrollBar));
                element.Resources.Remove(ScrollBarTrackBrushKey);
                element.Resources.Remove(ScrollBarHandleBrushKey);
                element.Resources.Remove(ScrollBarBorderBrushKey);
            });

            element.Resources[ScrollBarTrackBrushKey] = track ?? handle!;
            element.Resources[ScrollBarHandleBrushKey] = handle ?? track!;
            element.Resources[ScrollBarBorderBrushKey] = border ?? handle ?? track!;
            element.Resources[typeof(ScrollBar)] = themedStyle;
        }

        /// <summary>Resource key of the themed scrollbar's track brush.</summary>
        private const string ScrollBarTrackBrushKey = "OceanyaScrollBarTrackBrush";

        /// <summary>Resource key of the themed scrollbar's handle brush.</summary>
        private const string ScrollBarHandleBrushKey = "OceanyaScrollBarHandleBrush";

        /// <summary>Resource key of the themed scrollbar's border brush.</summary>
        private const string ScrollBarBorderBrushKey = "OceanyaScrollBarBorderBrush";

        /// <summary>
        /// Cached themed scrollbar style, per thread: a WPF style belongs to the thread that created it, and
        /// this app runs more than one UI thread (the wait form has its own).
        /// </summary>
        [ThreadStatic]
        private static Style? themedScrollBarStyle;

        /// <summary>
        /// Loads the themed scrollbar style once.
        /// </summary>
        /// <returns>The style, or null when it cannot be loaded.</returns>
        private static Style? TryResolveScrollBarStyle()
        {
            if (themedScrollBarStyle != null)
            {
                return themedScrollBarStyle;
            }

            try
            {
                ResourceDictionary dictionary = new ResourceDictionary
                {
                    Source = new Uri("/OceanyaClient;component/Styles/OceanyaThemedScrollBar.xaml", UriKind.Relative)
                };
                themedScrollBarStyle = dictionary["OceanyaThemedScrollBarStyle"] as Style;
            }
            catch (Exception exception)
            {
                Common.CustomConsole.Warning("The themed scrollbar style could not be loaded.", exception);
            }

            return themedScrollBarStyle;
        }

        /// <summary>
        /// Finds the surface a dropdown's popup list is drawn on.
        /// </summary>
        /// <remarks>
        /// The popup's content is not part of the main visual tree until it is opened for the first time,
        /// so it is reached through the control template instead of by walking children.
        /// </remarks>
        /// <param name="element">Element to inspect.</param>
        /// <param name="popupSurfaceName">Name of the popup part in the template.</param>
        /// <returns>The popup's background border, or null.</returns>
        private static Border? TryResolvePopupSurface(DependencyObject element, string popupSurfaceName = "Popup")
        {
            if (element is not ComboBox comboBox || comboBox.Template == null)
            {
                return null;
            }

            try
            {
                return comboBox.Template.FindName(popupSurfaceName, comboBox) is System.Windows.Controls.Primitives.Popup popup
                    ? popup.Child as Border
                    : null;
            }
            catch (InvalidOperationException)
            {
                // The template has not been applied yet; the deferred pass picks it up.
                return null;
            }
        }

        /// <summary>
        /// Colours a dropdown's popup list to match the panel, so one setting styles the whole control.
        /// </summary>
        /// <remarks>
        /// Our popup surface is opaque white by default while its rows inherit the control's foreground.
        /// A theme that asks for white dropdown text therefore produced white on white and an unusable
        /// list, which is why the popup follows the panel's background whenever one is set.
        /// </remarks>
        /// <param name="descriptorId">Panel id, for baseline bookkeeping.</param>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplyDropdownPopupColours(
            string descriptorId,
            FrameworkElement element,
            OceanyaPanelPlacementState state)
        {
            Brush? background = TryParseBrush(state.BackgroundColor);
            if (background == null)
            {
                return;
            }

            element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                AppendBaseline(descriptorId, element);
                ApplyPopupBackgroundRecursive(element, background);
            }));
        }

        private static void ApplyPopupBackgroundRecursive(DependencyObject element, Brush background)
        {
            if (TryResolvePopupSurface(element) is Border surface)
            {
                surface.Background = background;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ApplyPopupBackgroundRecursive(VisualTreeHelper.GetChild(element, i), background);
            }
        }

        /// <summary>
        /// Applies a grid panel's item metrics and paging inset.
        /// </summary>
        /// <param name="descriptorId">Panel id, for restore bookkeeping.</param>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplyGridMetrics(string descriptorId, FrameworkElement element, OceanyaPanelPlacementState state)
        {
            PageButtonGrid? grid = element as PageButtonGrid ?? FindGrid(element);
            if (grid == null)
            {
                return;
            }

            object snapshot = grid.CapturePagingConfiguration();
            PanelStateOverrides.Register(element, "paging", () => grid.RestorePagingConfiguration(snapshot));

            grid.SetPagingReservation(state.ReservePagingSpace);
            if (state.ItemSize > 0)
            {
                grid.EnableAutomaticPageSize(state.ItemSize, state.ItemHeight, state.ItemSpacingX, state.ItemSpacingY);
            }
        }

        /// <summary>
        /// Remembers how to undo the settings that are plain state rather than dependency properties.
        /// </summary>
        /// <remarks>
        /// The visual baseline can only restore dependency properties. Grid paging and the hover/checked
        /// artwork handlers are ordinary state, so each one registers its own undo the first time it is
        /// applied - captured before the change, so a reset lands on the pre-theme value.
        /// </remarks>
        private static class PanelStateOverrides
        {
            private static readonly Dictionary<DependencyObject, Dictionary<string, Action>> Registered =
                new Dictionary<DependencyObject, Dictionary<string, Action>>();

            /// <summary>
            /// Undoes a previous override of the same kind and records the new one in its place.
            /// </summary>
            /// <remarks>
            /// Needed by anything that attaches event handlers: a layout is applied more than once per
            /// session, and keeping only the first undo left every later set of handlers attached forever -
            /// so a reset stopped working and the handlers stacked up.
            /// </remarks>
            /// <param name="element">Panel element the override belongs to.</param>
            /// <param name="key">Kind of override.</param>
            /// <param name="undo">Action that restores the pre-styling state.</param>
            public static void Replace(DependencyObject element, string key, Action undo)
            {
                if (Registered.TryGetValue(element, out Dictionary<string, Action>? existing)
                    && existing.TryGetValue(key, out Action? previous))
                {
                    previous();
                    existing.Remove(key);
                }

                Register(element, key, undo);
            }

            /// <summary>
            /// Records an undo action for a panel, ignoring repeats of the same kind.
            /// </summary>
            /// <param name="element">Panel element the override belongs to.</param>
            /// <param name="key">Kind of override, so re-applying does not capture a styled value.</param>
            /// <param name="undo">Action that restores the pre-styling state.</param>
            public static void Register(DependencyObject element, string key, Action undo)
            {
                if (!Registered.TryGetValue(element, out Dictionary<string, Action>? actions))
                {
                    actions = new Dictionary<string, Action>(StringComparer.Ordinal);
                    Registered[element] = actions;
                }

                if (!actions.ContainsKey(key))
                {
                    actions[key] = undo;
                }
            }

            /// <summary>
            /// Runs and forgets every override recorded for a panel.
            /// </summary>
            /// <param name="element">Panel element being restored.</param>
            public static void Restore(DependencyObject element)
            {
                if (!Registered.TryGetValue(element, out Dictionary<string, Action>? actions))
                {
                    return;
                }

                Registered.Remove(element);
                foreach (Action undo in actions.Values)
                {
                    undo();
                }
            }
        }

        private static void ApplyImageOverride(string descriptorId, FrameworkElement element, OceanyaPanelPlacementState state)
        {
            string imagePath = state.ImagePath;
            string checkedImagePath = state.CheckedImagePath;
            // Toggle panels carry two images (AO2 ships `<name>` and `<name>_selected`), and they are
            // bound through dependency properties rather than the rendered Image, so the swap survives
            // the checked state changing.
            if (element is Components.Panels.ShoutButtonPanel shoutPanel)
            {
                AppendBaseline(descriptorId, element);
                ImageSource? unchecked_ = TryLoadImage(imagePath);
                ImageSource? checked_ = TryLoadImage(checkedImagePath);
                if (unchecked_ != null)
                {
                    shoutPanel.UncheckedImage = unchecked_;
                    shoutPanel.CheckedImage = checked_ ?? unchecked_;
                }

                ApplyShoutHoverArt(shoutPanel, unchecked_, state);
                return;
            }

            bool hasBase = !string.IsNullOrWhiteSpace(imagePath) && System.IO.File.Exists(imagePath);
            bool hasStates = !string.IsNullOrWhiteSpace(state.HoverImagePath) || !string.IsNullOrWhiteSpace(checkedImagePath);
            if (!hasBase && !hasStates)
            {
                return;
            }

            // The face lives in the control template, which does not exist until the panel has been
            // measured and rendered - hence Loaded priority. At normal priority the callback ran BEFORE
            // the template existed, found no face and silently left the original artwork in place.
            element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                try
                {
                    // Re-capture first: the template's face did not exist at the original capture.
                    AppendBaseline(descriptorId, element);
                    ImageSource? source = hasBase ? OceanyaClient.Utilities.BitmapFileLoader.LoadFrozen(imagePath) : null;
                    if (source != null)
                    {
                        SetFaceArt(element, source);
                        ClearThemedButtonChrome(element, state);

                        // AO2 buttons are pure artwork, so a glyph label like the emote arrows' "<" would
                        // otherwise sit on top of the theme's own art. Tracked in the baseline, so a reset
                        // brings the label back.
                        if (element is ContentControl labelled && labelled.Content is string caption)
                        {
                            labelled.Content = null;
                            PanelStateOverrides.Register(
                                element,
                                "caption",
                                () => labelled.Content = caption);
                        }
                    }

                    ApplyStateArt(element, source, state);
                }
                catch (Exception exception)
                {
                    Common.CustomConsole.Warning($"Panel image override could not be loaded: {imagePath}", exception);
                }
            }));
        }

        /// <summary>
        /// Gives a shout panel its hover artwork, restoring the resting art when the pointer leaves.
        /// </summary>
        /// <param name="shoutPanel">Shout panel to wire up.</param>
        /// <param name="restingArt">Artwork of the resting state, or null to keep the panel's own.</param>
        /// <param name="state">Saved state carrying the hover artwork.</param>
        private static void ApplyShoutHoverArt(
            Components.Panels.ShoutButtonPanel shoutPanel,
            ImageSource? restingArt,
            OceanyaPanelPlacementState state)
        {
            ImageSource? hoverArt = TryLoadImage(state.HoverImagePath);
            if (hoverArt == null)
            {
                return;
            }

            ImageSource? resting = restingArt ?? shoutPanel.UncheckedImage;
            MouseEventHandler enter = (_, _) =>
            {
                // Only the resting art is swapped, so a selected shout keeps its selected art.
                shoutPanel.UncheckedImage = hoverArt;
            };
            MouseEventHandler leave = (_, _) =>
            {
                if (resting != null)
                {
                    shoutPanel.UncheckedImage = resting;
                }
            };

            shoutPanel.MouseEnter += enter;
            shoutPanel.MouseLeave += leave;
            PanelStateOverrides.Replace(shoutPanel, StateArtOverrideKey, () =>
            {
                shoutPanel.MouseEnter -= enter;
                shoutPanel.MouseLeave -= leave;
            });
        }

        /// <summary>
        /// Skins a slider with the theme's groove and handle artwork.
        /// </summary>
        /// <remarks>
        /// AO2 does this through `QSlider::groove` and `QSlider::handle` in its stylesheet, so the two
        /// images arrive in the panel's own image fields. Like the scrollbars, the style and its brushes go
        /// into the panel's resource scope so the undo is removing them again.
        /// </remarks>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state.</param>
        private static void ApplySliderArt(FrameworkElement element, OceanyaPanelPlacementState state)
        {
            if (element is not Slider slider)
            {
                return;
            }

            ImageSource? groove = TryLoadImage(state.ImagePath);
            ImageSource? handle = TryLoadImage(state.IndicatorImagePath);
            Brush? fill = TryParseBrush(state.FillColor);
            Brush? empty = TryParseBrush(state.BackgroundColor);
            Brush? pageBorder = TryParseBrush(state.BorderColor);
            if (groove == null && handle == null && fill == null && empty == null)
            {
                return;
            }

            Style? themedStyle = TryResolveSliderStyle();
            if (themedStyle == null)
            {
                return;
            }

            PanelStateOverrides.Register(element, "sliderArt", () =>
            {
                foreach (object key in new object[]
                {
                    SliderGrooveBrushKey,
                    SliderHandleBrushKey,
                    SliderFillBrushKey,
                    SliderEmptyBrushKey,
                    SliderPageBorderBrushKey,
                    SliderPageHeightKey,
                    SliderHandleWidthKey
                })
                {
                    slider.Resources.Remove(key);
                }

                slider.ClearValue(FrameworkElement.StyleProperty);
            });

            slider.Resources[SliderGrooveBrushKey] = groove == null
                ? Brushes.Transparent
                : new ImageBrush(groove) { Stretch = Stretch.Fill };
            slider.Resources[SliderHandleBrushKey] = handle == null
                ? Brushes.Transparent
                : new ImageBrush(handle) { Stretch = Stretch.Uniform };
            slider.Resources[SliderFillBrushKey] = fill ?? Brushes.Transparent;
            slider.Resources[SliderEmptyBrushKey] = empty ?? Brushes.Transparent;
            slider.Resources[SliderPageBorderBrushKey] = pageBorder ?? Brushes.Transparent;

            // Qt shapes the two pages with margins around a thin bar; a fraction of the widget height is the
            // same idea without needing a margin field per side.
            slider.Resources[SliderPageHeightKey] = Math.Max(3d, Math.Round(slider.Height / 6));
            slider.Resources[SliderHandleWidthKey] = handle is BitmapSource sized ? (double)sized.PixelWidth : 15d;
            slider.Style = themedStyle;
        }

        /// <summary>Resource key of the themed slider's groove brush.</summary>
        private const string SliderGrooveBrushKey = "OceanyaSliderGrooveBrush";

        /// <summary>Resource key of the themed slider's handle brush.</summary>
        private const string SliderHandleBrushKey = "OceanyaSliderHandleBrush";

        /// <summary>Resource key of the filled part of a themed slider.</summary>
        private const string SliderFillBrushKey = "OceanyaSliderFillBrush";

        /// <summary>Resource key of the empty part of a themed slider.</summary>
        private const string SliderEmptyBrushKey = "OceanyaSliderEmptyBrush";

        /// <summary>Resource key of the border drawn around both slider pages.</summary>
        private const string SliderPageBorderBrushKey = "OceanyaSliderPageBorderBrush";

        /// <summary>Resource key of the height of a themed slider's pages.</summary>
        private const string SliderPageHeightKey = "OceanyaSliderPageHeight";

        /// <summary>Resource key of the width of a themed slider's handle.</summary>
        private const string SliderHandleWidthKey = "OceanyaSliderHandleWidth";

        /// <summary>Cached themed slider style, per thread (a WPF style is thread-affine).</summary>
        [ThreadStatic]
        private static Style? themedSliderStyle;

        /// <summary>
        /// Loads the themed slider style once per thread.
        /// </summary>
        /// <returns>The style, or null when it cannot be loaded.</returns>
        private static Style? TryResolveSliderStyle()
        {
            if (themedSliderStyle != null)
            {
                return themedSliderStyle;
            }

            try
            {
                ResourceDictionary dictionary = new ResourceDictionary
                {
                    Source = new Uri("/OceanyaClient;component/Styles/OceanyaThemedSlider.xaml", UriKind.Relative)
                };
                themedSliderStyle = dictionary["OceanyaThemedSliderStyle"] as Style;
            }
            catch (Exception exception)
            {
                Common.CustomConsole.Warning("The themed slider style could not be loaded.", exception);
            }

            return themedSliderStyle;
        }

        /// <summary>
        /// Drops a button's own fill and frame once a theme gives it artwork.
        /// </summary>
        /// <remarks>
        /// An AO2 button has no background of its own: the theme's artwork - often a transparent 1x1 with
        /// the real look painted into the courtroom background - IS the button. Leaving our default fill in
        /// place drew a coloured square behind (or instead of) the theme's art. A background the theme asked
        /// for explicitly is left alone.
        /// </remarks>
        /// <param name="element">Panel element.</param>
        /// <param name="state">Saved state, so an explicit background wins.</param>
        private static void ClearThemedButtonChrome(FrameworkElement element, OceanyaPanelPlacementState state)
        {
            if (!string.IsNullOrWhiteSpace(state.BackgroundColor))
            {
                return;
            }

            if (element is Control control)
            {
                control.Background = Brushes.Transparent;
                if (state.BorderThickness < 0)
                {
                    control.BorderThickness = new Thickness(0);
                }
            }

            ClearThemedBorderChromeRecursive(element, state);
        }

        private static void ClearThemedBorderChromeRecursive(DependencyObject element, OceanyaPanelPlacementState state)
        {
            if (element is Border border)
            {
                border.Background = Brushes.Transparent;
                if (state.BorderThickness < 0)
                {
                    border.BorderThickness = new Thickness(0);
                }
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ClearThemedBorderChromeRecursive(VisualTreeHelper.GetChild(element, i), state);
            }
        }

        /// <summary>
        /// Paints a panel's button face, whichever way that face happens to be drawn.
        /// </summary>
        /// <param name="element">Panel element.</param>
        /// <param name="source">Artwork to paint.</param>
        private static void SetFaceArt(FrameworkElement element, ImageSource source)
        {
            if (SetImageSourceRecursive(element, source))
            {
                return;
            }

            // Some buttons draw their face as a masked Rectangle rather than an Image, so the art goes on
            // as a brush and the glyph mask is dropped, or nothing would show.
            if (SetShapeFillRecursive(element, source))
            {
                return;
            }

            // And some draw it as an icon-font glyph (the settings and refresh buttons), which no amount of
            // image swapping reaches: the art becomes the face's background and the glyph steps aside.
            SetGlyphFaceArt(element, element, source);
        }

        /// <summary>
        /// Paints a glyph-based button face with artwork, hiding the glyph itself.
        /// </summary>
        /// <param name="element">Element to walk.</param>
        /// <param name="source">Artwork to paint.</param>
        /// <returns>True when a face was found.</returns>
        private static bool SetGlyphFaceArt(FrameworkElement panelRoot, DependencyObject element, ImageSource source)
        {
            Panel? face = element as Panel ?? FindDescendant<Panel>(element);
            if (face == null)
            {
                return false;
            }

            // Repainting unconditionally: the face already carries artwork once a base image has been
            // applied, and a hover swap has to be able to replace it.
            face.Background = new ImageBrush(source) { Stretch = Stretch.Uniform };

            List<(TextBlock Glyph, Visibility Previous)> hidden = new List<(TextBlock, Visibility)>();
            HideGlyphText(face, hidden);
            if (hidden.Count > 0)
            {
                // Visibility is not baseline-tracked, because runtime code owns it, so the undo is explicit
                // and lives on the panel - which is what a reset restores.
                PanelStateOverrides.Register(panelRoot, "glyphVisibility", () =>
                {
                    foreach ((TextBlock glyph, Visibility previous) in hidden)
                    {
                        glyph.Visibility = previous;
                    }
                });
            }

            return true;
        }

        /// <summary>
        /// Hides the icon-font glyphs a themed face replaces, recording what they were.
        /// </summary>
        /// <param name="face">Face whose text should step aside.</param>
        /// <param name="hidden">Collects each glyph and its previous visibility.</param>
        private static void HideGlyphText(DependencyObject face, List<(TextBlock Glyph, Visibility Previous)> hidden)
        {
            if (face is TextBlock glyph)
            {
                if (glyph.Visibility != Visibility.Collapsed)
                {
                    hidden.Add((glyph, glyph.Visibility));
                    glyph.Visibility = Visibility.Collapsed;
                }

                return;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(face);
            for (int i = 0; i < childCount; i++)
            {
                HideGlyphText(VisualTreeHelper.GetChild(face, i), hidden);
            }
        }

        /// <summary>
        /// Wires up the hover and checked artwork of an image-button panel.
        /// </summary>
        /// <remarks>
        /// AO2 themes routinely ship a transparent base image, paint the button's resting appearance into
        /// the courtroom background, and give the real artwork only to the hover and pressed states - so a
        /// button that "inverts while pressed" in AO2 is a state image, not a colour effect. WPF template
        /// triggers cannot be rewritten from data, so the swap is done with handlers instead, and each one
        /// registers its undo so a theme reset puts the original behaviour back.
        /// </remarks>
        /// <param name="element">Panel element.</param>
        /// <param name="baseArt">Artwork of the resting state, or null to keep the control's own.</param>
        /// <param name="state">Saved state carrying the hover and checked artwork.</param>
        private static void ApplyStateArt(FrameworkElement element, ImageSource? baseArt, OceanyaPanelPlacementState state)
        {
            ImageSource? hoverArt = TryLoadImage(state.HoverImagePath);
            ImageSource? checkedArt = TryLoadImage(state.CheckedImagePath);
            if (hoverArt == null && checkedArt == null)
            {
                return;
            }

            ImageSource? restingArt = baseArt ?? FindFaceArt(element);
            System.Windows.Controls.Primitives.ToggleButton? toggle = element as System.Windows.Controls.Primitives.ToggleButton
                ?? FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(element);

            void PaintRestingState()
            {
                bool isChecked = toggle?.IsChecked == true;
                ImageSource? art = isChecked ? checkedArt ?? restingArt : restingArt;
                if (art != null)
                {
                    SetFaceArt(element, art);
                }
            }

            MouseEventHandler enter = (_, _) =>
            {
                // AO2 leaves a checked button alone on hover: its selected art stays put. Painting the
                // hover art over it looked like the button un-pressing itself under the pointer.
                if (hoverArt != null && toggle?.IsChecked != true)
                {
                    SetFaceArt(element, hoverArt);
                }
            };

            MouseEventHandler leave = (_, _) => PaintRestingState();
            RoutedEventHandler toggled = (_, _) => PaintRestingState();

            element.MouseEnter += enter;
            element.MouseLeave += leave;
            if (toggle != null)
            {
                toggle.Checked += toggled;
                toggle.Unchecked += toggled;
            }

            PanelStateOverrides.Replace(element, StateArtOverrideKey, () =>
            {
                element.MouseEnter -= enter;
                element.MouseLeave -= leave;
                if (toggle != null)
                {
                    toggle.Checked -= toggled;
                    toggle.Unchecked -= toggled;
                }
            });

            PaintRestingState();
        }

        /// <summary>
        /// Reads the artwork a panel's face currently shows.
        /// </summary>
        /// <param name="element">Panel element.</param>
        /// <returns>The current face artwork, or null when the face is not an image.</returns>
        private static ImageSource? FindFaceArt(DependencyObject element)
        {
            if (element is Image image)
            {
                return image.Source;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ImageSource? found = FindFaceArt(VisualTreeHelper.GetChild(element, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first descendant of a type in the rendered tree.
        /// </summary>
        /// <typeparam name="T">Type to look for.</typeparam>
        /// <param name="element">Element to walk.</param>
        /// <returns>The descendant, or null.</returns>
        private static T? FindDescendant<T>(DependencyObject element) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, i);
                if (child is T typed)
                {
                    return typed;
                }

                T? nested = FindDescendant<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        /// <summary>
        /// Loads an image for a panel override, tolerating a missing or unreadable file.
        /// </summary>
        /// <param name="imagePath">Absolute image path.</param>
        /// <returns>The frozen image, or null.</returns>
        private static ImageSource? TryLoadImage(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
            {
                return null;
            }

            try
            {
                return OceanyaClient.Utilities.BitmapFileLoader.LoadFrozen(imagePath);
            }
            catch (Exception exception)
            {
                Common.CustomConsole.Warning($"Panel image could not be loaded: {imagePath}", exception);
                return null;
            }
        }

        private static bool SetImageSourceRecursive(DependencyObject element, ImageSource source)
        {
            bool applied = false;
            if (element is Image image)
            {
                image.Source = source;
                applied = true;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                applied |= SetImageSourceRecursive(VisualTreeHelper.GetChild(element, i), source);
            }

            return applied;
        }

        /// <summary>
        /// Paints a shape-based button face with the given artwork.
        /// </summary>
        /// <param name="element">Element to walk.</param>
        /// <param name="source">Artwork to paint.</param>
        /// <returns>True when a shape face was found.</returns>
        private static bool SetShapeFillRecursive(DependencyObject element, ImageSource source)
        {
            bool applied = false;
            if (element is System.Windows.Shapes.Shape shape)
            {
                shape.Fill = new ImageBrush(source) { Stretch = Stretch.Fill };
                shape.OpacityMask = null;
                applied = true;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                applied |= SetShapeFillRecursive(VisualTreeHelper.GetChild(element, i), source);
            }

            return applied;
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
            element.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => SetImageStretchRecursive(element, stretch)));
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
