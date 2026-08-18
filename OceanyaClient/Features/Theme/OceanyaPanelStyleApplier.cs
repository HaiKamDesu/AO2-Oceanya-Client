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

            List<PanelVisualBaseline> captured = new List<PanelVisualBaseline>();
            CaptureBaselineRecursive(element, captured);
            captured.Add(new PanelVisualBaseline(element)
            {
                Height = element.Height,
                ZIndex = Panel.GetZIndex(element)
            });
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
        /// A single element's pre-styling appearance.
        /// </summary>
        private sealed class PanelVisualBaseline
        {
            private readonly DependencyObject target;

            public PanelVisualBaseline(DependencyObject target)
            {
                this.target = target;
            }

            public double FontSize { get; init; }

            public FontFamily? FontFamily { get; init; }

            public FontWeight? FontWeight { get; init; }

            public ImageSource? ImageSource { get; init; }

            public Stretch? ImageStretch { get; init; }

            public double? Height { get; init; }

            public int? ZIndex { get; init; }

            public static PanelVisualBaseline From(DependencyObject element)
            {
                if (element is Control control)
                {
                    return new PanelVisualBaseline(element)
                    {
                        FontSize = control.FontSize,
                        FontFamily = control.FontFamily,
                        FontWeight = control.FontWeight
                    };
                }

                if (element is TextBlock textBlock)
                {
                    return new PanelVisualBaseline(element)
                    {
                        FontSize = textBlock.FontSize,
                        FontFamily = textBlock.FontFamily,
                        FontWeight = textBlock.FontWeight
                    };
                }

                Image image = (Image)element;
                return new PanelVisualBaseline(element)
                {
                    ImageSource = image.Source,
                    ImageStretch = image.Stretch
                };
            }

            public void Restore()
            {
                switch (target)
                {
                    case Control control:
                        if (FontSize > 0)
                        {
                            control.FontSize = FontSize;
                        }

                        if (FontFamily != null)
                        {
                            control.FontFamily = FontFamily;
                        }

                        if (FontWeight.HasValue)
                        {
                            control.FontWeight = FontWeight.Value;
                        }

                        break;
                    case TextBlock textBlock:
                        if (FontSize > 0)
                        {
                            textBlock.FontSize = FontSize;
                        }

                        if (FontFamily != null)
                        {
                            textBlock.FontFamily = FontFamily;
                        }

                        if (FontWeight.HasValue)
                        {
                            textBlock.FontWeight = FontWeight.Value;
                        }

                        break;
                    case Image image:
                        image.Source = ImageSource;
                        if (ImageStretch.HasValue)
                        {
                            image.Stretch = ImageStretch.Value;
                        }

                        break;
                }

                if (target is FrameworkElement frameworkElement && Height.HasValue)
                {
                    frameworkElement.Height = Height.Value;
                }

                if (target is UIElement uiElement && ZIndex.HasValue)
                {
                    Panel.SetZIndex(uiElement, ZIndex.Value);
                }
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
            if (state.FontSize <= 0 && family == null && !state.IsBold)
            {
                return;
            }

            SetFontRecursive(element, state.FontSize, family, weight);
            if (state.FontSize > 0)
            {
                element.Height = ResolveTextPanelHeight(state.FontSize);
            }
        }

        private static void SetFontRecursive(DependencyObject element, double fontSize, FontFamily? family, FontWeight? weight)
        {
            if (element is Control control)
            {
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
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                SetFontRecursive(VisualTreeHelper.GetChild(element, i), fontSize, family, weight);
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
