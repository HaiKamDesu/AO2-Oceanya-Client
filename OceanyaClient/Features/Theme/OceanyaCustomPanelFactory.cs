using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Common;
using OceanyaClient.Utilities;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Creates the visuals for user-added panels (pictures and flat colour blocks) and keeps their
    /// definitions registered with <see cref="OceanyaPanelCatalog"/>.
    /// </summary>
    public static class OceanyaCustomPanelFactory
    {
        /// <summary>Custom panel kind for a picture.</summary>
        public const string ImageKind = "image";

        /// <summary>Custom panel kind for a flat colour block.</summary>
        public const string ColorKind = "color";

        /// <summary>
        /// Builds a new definition for a user-added panel.
        /// </summary>
        /// <param name="kind">Either <see cref="ImageKind"/> or <see cref="ColorKind"/>.</param>
        /// <param name="placement">Where the panel starts out.</param>
        /// <param name="imagePath">Picture path, for image panels.</param>
        /// <param name="color">Colour string, for colour panels.</param>
        /// <returns>The new definition, with a generated id.</returns>
        /// <param name="id">Explicit id, for panels a theme import needs to recreate deterministically.</param>
        /// <param name="displayName">Explicit display name.</param>
        /// <param name="isLocked">True to create the panel locked in place.</param>
        /// <param name="isClickThrough">True to let clicks pass through the panel.</param>
        /// <param name="zOrder">Stacking order; zero leaves the default.</param>
        public static OceanyaCustomPanelDefinition CreateDefinition(
            string kind,
            OceanyaPanelPlacement placement,
            string imagePath = "",
            string color = "#FF3F8CD8",
            string? id = null,
            string? displayName = null,
            bool isLocked = false,
            int zOrder = 0,
            bool isClickThrough = false)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            bool isImage = string.Equals(kind, ImageKind, StringComparison.OrdinalIgnoreCase);
            return new OceanyaCustomPanelDefinition
            {
                Id = id ?? $"custom_{kind}_{suffix}",
                DisplayName = displayName ?? (isImage
                    ? (string.IsNullOrWhiteSpace(imagePath) ? "Image" : Path.GetFileNameWithoutExtension(imagePath))
                    : "Colour Block"),
                Kind = isImage ? ImageKind : ColorKind,
                ImagePath = imagePath,
                Color = color,
                Placement = new OceanyaPanelPlacementState
                {
                    Left = placement.Left,
                    Top = placement.Top,
                    Width = placement.Width,
                    Height = placement.Height,
                    IsLocked = isLocked,
                    ZOrder = zOrder,
                    IsClickThrough = isClickThrough
                }
            };
        }

        /// <summary>
        /// Creates the element for a definition and registers its descriptor.
        /// </summary>
        /// <param name="definition">Definition to realise.</param>
        /// <returns>The element to place on the surface.</returns>
        public static FrameworkElement CreateElement(OceanyaCustomPanelDefinition definition)
        {
            OceanyaPanelCatalog.RegisterCustomPanel(new OceanyaPanelDescriptor(
                definition.Id,
                definition.DisplayName,
                new OceanyaPanelPlacement(
                    definition.Placement.Left,
                    definition.Placement.Top,
                    definition.Placement.Width,
                    definition.Placement.Height),
                minimumWidth: 8,
                minimumHeight: 8,
                kind: OceanyaPanelKind.Static));

            FrameworkElement element = string.Equals(definition.Kind, ImageKind, StringComparison.OrdinalIgnoreCase)
                ? CreateImageElement(definition)
                : CreateColorElement(definition);

            element.Tag = definition.Id;
            return element;
        }

        private static FrameworkElement CreateImageElement(OceanyaCustomPanelDefinition definition)
        {
            Image image = new Image { Stretch = Stretch.Fill };
            if (!string.IsNullOrWhiteSpace(definition.ImagePath) && File.Exists(definition.ImagePath))
            {
                try
                {
                    image.Source = BitmapFileLoader.LoadFrozen(definition.ImagePath);
                }
                catch (Exception exception)
                {
                    // A missing or unreadable picture must not take the whole layout down.
                    CustomConsole.Warning($"Custom theme panel image could not be loaded: {definition.ImagePath}", exception);
                }
            }

            return image;
        }

        private static FrameworkElement CreateColorElement(OceanyaCustomPanelDefinition definition)
        {
            Brush fill = Brushes.CornflowerBlue;
            try
            {
                object? converted = ColorConverter.ConvertFromString(definition.Color);
                if (converted is Color color)
                {
                    fill = new SolidColorBrush(color);
                }
            }
            catch (FormatException)
            {
                // Keep the fallback colour for an unparseable value.
            }

            return new Border { Background = fill };
        }
    }
}
