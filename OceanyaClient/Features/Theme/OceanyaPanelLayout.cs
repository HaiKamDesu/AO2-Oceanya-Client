using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// The visual elements that make up one panel on the GM main window surface.
    /// </summary>
    /// <param name="Element">The panel's own control.</param>
    /// <param name="Backdrop">Optional background element drawn behind the panel.</param>
    public readonly record struct OceanyaPanelElements(FrameworkElement Element, FrameworkElement? Backdrop = null);

    /// <summary>
    /// Applies <see cref="OceanyaPanelCatalog"/> placements to the controls on the main window canvas.
    /// </summary>
    /// <remarks>
    /// Placement used to live as Canvas.Left/Top/Width/Height attributes in MainWindow.xaml. Driving it
    /// from the catalog instead is what lets a theme (and later a dock host) move these regions without
    /// touching XAML. Applying the catalog's default placements reproduces the historic layout exactly.
    /// </remarks>
    public static class OceanyaPanelLayout
    {
        /// <summary>
        /// Applies the default catalog placement to every supplied panel.
        /// </summary>
        /// <param name="panels">Panel elements keyed by their stable panel id.</param>
        /// <returns>The number of panels that were placed.</returns>
        public static int ApplyDefaultPlacements(IReadOnlyDictionary<string, OceanyaPanelElements> panels)
        {
            if (panels == null)
            {
                throw new ArgumentNullException(nameof(panels));
            }

            int placedCount = 0;
            foreach (KeyValuePair<string, OceanyaPanelElements> pair in panels)
            {
                OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(pair.Key);
                if (descriptor == null)
                {
                    continue;
                }

                ApplyPlacement(pair.Value.Element, descriptor.Placement);
                if (pair.Value.Backdrop != null && descriptor.BackdropPlacement.HasValue)
                {
                    ApplyPlacement(pair.Value.Backdrop, descriptor.BackdropPlacement.Value);
                }

                placedCount++;
            }

            return placedCount;
        }

        /// <summary>
        /// Applies the catalog defaults, then any user-adjusted placements from a saved theme layout.
        /// </summary>
        /// <param name="panels">Panel elements keyed by their stable panel id.</param>
        /// <param name="savedLayout">Saved layout; panels missing from it keep their catalog default.</param>
        public static void ApplyLayout(
            IReadOnlyDictionary<string, OceanyaPanelElements> panels,
            OceanyaThemeLayoutState? savedLayout)
        {
            ApplyDefaultPlacements(panels);
            if (savedLayout?.Panels == null)
            {
                return;
            }

            foreach (KeyValuePair<string, OceanyaPanelElements> pair in panels)
            {
                OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(pair.Key);
                if (descriptor == null
                    || !savedLayout.Panels.TryGetValue(pair.Key, out OceanyaPanelPlacementState? savedPlacement)
                    || savedPlacement == null)
                {
                    continue;
                }

                OceanyaPanelPlacement placement = SanitizePlacement(
                    new OceanyaPanelPlacement(
                        savedPlacement.Left,
                        savedPlacement.Top,
                        savedPlacement.Width,
                        savedPlacement.Height),
                    descriptor);

                if (!descriptor.AllowsVerticalResize && savedPlacement.FontSize > 0)
                {
                    // Text-like panels take their height from the font, not from a stored height.
                    placement = new OceanyaPanelPlacement(
                        placement.Left,
                        placement.Top,
                        placement.Width,
                        OceanyaPanelStyleApplier.ResolveTextPanelHeight(savedPlacement.FontSize));
                }

                ApplyPlacement(pair.Value.Element, placement);
                OceanyaPanelStyleApplier.Apply(descriptor, pair.Value.Element, savedPlacement);
                if (pair.Value.Backdrop != null && descriptor.BackdropPlacement.HasValue)
                {
                    // Keep the backdrop's historic offset and size delta relative to the panel.
                    OceanyaPanelPlacement defaultPlacement = descriptor.Placement;
                    OceanyaPanelPlacement defaultBackdrop = descriptor.BackdropPlacement.Value;
                    ApplyPlacement(
                        pair.Value.Backdrop,
                        new OceanyaPanelPlacement(
                            placement.Left + (defaultBackdrop.Left - defaultPlacement.Left),
                            placement.Top + (defaultBackdrop.Top - defaultPlacement.Top),
                            Math.Max(0, placement.Width + (defaultBackdrop.Width - defaultPlacement.Width)),
                            Math.Max(0, placement.Height + (defaultBackdrop.Height - defaultPlacement.Height))));
                }
            }
        }

        /// <summary>
        /// Calculates the bounding box covering every panel and backdrop on the surface.
        /// </summary>
        /// <param name="panels">Panel elements keyed by their stable panel id.</param>
        /// <returns>The union of all panel rectangles, or an empty rect when nothing is placed.</returns>
        public static Rect CalculateBounds(IReadOnlyDictionary<string, OceanyaPanelElements> panels)
        {
            Rect bounds = Rect.Empty;
            foreach (OceanyaPanelElements elements in panels.Values)
            {
                if (elements.Element.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                bounds.Union(ToRect(CapturePlacement(elements.Element)));
                if (elements.Backdrop != null && elements.Backdrop.Visibility != Visibility.Collapsed)
                {
                    bounds.Union(ToRect(CapturePlacement(elements.Backdrop)));
                }
            }

            return bounds;
        }

        /// <summary>
        /// Shifts every panel (and backdrop) by a delta, used when the surface grows left or upwards.
        /// </summary>
        /// <param name="panels">Panel elements keyed by their stable panel id.</param>
        /// <param name="deltaX">Horizontal shift.</param>
        /// <param name="deltaY">Vertical shift.</param>
        public static void OffsetAll(IReadOnlyDictionary<string, OceanyaPanelElements> panels, double deltaX, double deltaY)
        {
            if (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01)
            {
                return;
            }

            foreach (OceanyaPanelElements elements in panels.Values)
            {
                OffsetElement(elements.Element, deltaX, deltaY);
                if (elements.Backdrop != null)
                {
                    OffsetElement(elements.Backdrop, deltaX, deltaY);
                }
            }
        }

        private static void OffsetElement(FrameworkElement element, double deltaX, double deltaY)
        {
            OceanyaPanelPlacement placement = CapturePlacement(element);
            Canvas.SetLeft(element, placement.Left + deltaX);
            Canvas.SetTop(element, placement.Top + deltaY);
        }

        private static Rect ToRect(OceanyaPanelPlacement placement)
        {
            return new Rect(
                placement.Left,
                placement.Top,
                Math.Max(0, placement.Width),
                Math.Max(0, placement.Height));
        }

        /// <summary>
        /// Captures the current placement of a panel element.
        /// </summary>
        /// <param name="element">Panel element to read.</param>
        /// <returns>The element's placement on the canvas.</returns>
        public static OceanyaPanelPlacement CapturePlacement(FrameworkElement element)
        {
            double left = Canvas.GetLeft(element);
            double top = Canvas.GetTop(element);
            return new OceanyaPanelPlacement(
                double.IsNaN(left) ? 0 : left,
                double.IsNaN(top) ? 0 : top,
                double.IsNaN(element.Width) ? element.ActualWidth : element.Width,
                double.IsNaN(element.Height) ? element.ActualHeight : element.Height);
        }

        /// <summary>
        /// Clamps a placement to the panel's minimum size and keeps it at non-negative coordinates.
        /// </summary>
        /// <param name="placement">Requested placement.</param>
        /// <param name="descriptor">Panel descriptor supplying the minimums.</param>
        /// <returns>A usable placement.</returns>
        public static OceanyaPanelPlacement SanitizePlacement(
            OceanyaPanelPlacement placement,
            OceanyaPanelDescriptor descriptor)
        {
            double width = Math.Max(descriptor.MinimumWidth, IsUsable(placement.Width) ? placement.Width : descriptor.Placement.Width);
            double height = Math.Max(descriptor.MinimumHeight, IsUsable(placement.Height) ? placement.Height : descriptor.Placement.Height);
            double left = IsFinite(placement.Left) ? placement.Left : descriptor.Placement.Left;
            double top = IsFinite(placement.Top) ? placement.Top : descriptor.Placement.Top;
            return new OceanyaPanelPlacement(left, top, width, height);
        }

        private static bool IsUsable(double value)
        {
            return IsFinite(value) && value > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Applies a single placement to an element hosted on a canvas.
        /// </summary>
        /// <param name="element">Element to place.</param>
        /// <param name="placement">Placement to apply.</param>
        public static void ApplyPlacement(FrameworkElement element, OceanyaPanelPlacement placement)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            Canvas.SetLeft(element, placement.Left);
            Canvas.SetTop(element, placement.Top);
            element.Width = placement.Width;
            element.Height = placement.Height;
        }
    }
}
