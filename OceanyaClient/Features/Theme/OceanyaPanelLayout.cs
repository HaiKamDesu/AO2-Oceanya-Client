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
