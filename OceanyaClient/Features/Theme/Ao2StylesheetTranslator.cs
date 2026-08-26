using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Common;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Translates an AO2 theme's <c>courtroom_stylesheets.css</c> into Oceanya panel style settings.
    /// </summary>
    /// <remarks>
    /// AO2 styles its courtroom with a Qt stylesheet, which is where CSS-heavy themes keep most of their
    /// colours. WPF has no Qt stylesheet engine, so rather than emulating one this reads the rules and
    /// writes the parts we can represent into the *same per-panel fields the layout editor exposes*
    /// (background colour, text colour, font family/size/bold). That keeps editor parity: importing a
    /// stylesheet only sets things a user could set by hand.
    ///
    /// Supported beyond plain colours and fonts: `image: url(...)` (panel image), the `:hover` state
    /// (hover image) and the `:pressed`/`:checked`/`:on` states (selected image), plus attribute
    /// selectors that target one widget by its geometry (`[x="176"][y="657"]`), which is how CSS-heavy
    /// themes address individual buttons. Geometry matching needs the theme's own widget rectangles, so
    /// it only happens during a theme import; a hand-picked stylesheet still applies its class rules.
    ///
    /// Deliberately ignored: sub-controls (`::drop-down`, `::indicator`), other pseudo-states, borders
    /// and padding. Those have no equivalent field yet; when one is added, extend this.
    /// </remarks>
    public static class Ao2StylesheetTranslator
    {
        /// <summary>
        /// Qt widget class to the Oceanya panel kinds it styles.
        /// </summary>
        private static readonly (string Selector, OceanyaPanelKind[] Kinds)[] SelectorKinds =
        {
            ("QWidget", new[] { OceanyaPanelKind.Static, OceanyaPanelKind.TextInput, OceanyaPanelKind.Dropdown, OceanyaPanelKind.TextToggle, OceanyaPanelKind.ItemGrid }),
            ("QLineEdit", new[] { OceanyaPanelKind.TextInput }),
            ("QTextEdit", new[] { OceanyaPanelKind.Static }),
            ("QPlainTextEdit", new[] { OceanyaPanelKind.Static }),
            ("QComboBox", new[] { OceanyaPanelKind.Dropdown }),
            ("QCheckBox", new[] { OceanyaPanelKind.TextToggle }),
            // A Qt label is used both for text and as a plain image display, which is our two kinds.
            ("QLabel", new[] { OceanyaPanelKind.TextToggle, OceanyaPanelKind.ImageButton }),
            ("QPushButton", new[] { OceanyaPanelKind.ImageButton }),
            ("QSlider", new[] { OceanyaPanelKind.Slider }),

            // Qt styles scrollbars globally, so the colours have to reach every panel that owns one.
            ("QScrollBar", new[] { OceanyaPanelKind.Static, OceanyaPanelKind.Dropdown, OceanyaPanelKind.ItemGrid })

            // Deliberately absent: QListWidget/QListView. AO2's list widgets are its music, area, pair
            // and mute lists - none of which is a panel here - and mapping them onto our ItemGrid kind
            // painted the emote grid and clients list with the pair list's background.
        };

        /// <summary>
        /// Qt sub-controls that map onto a field of ours: a checkbox's tick box, a dropdown's arrow and a
        /// scrollbar's handle.
        /// </summary>
        private static readonly HashSet<string> TranslatableSubControls =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "indicator", "down-arrow", "handle", "groove", "sub-page", "add-page" };

        /// <summary>
        /// Panels a Qt class selector applies to, beyond the kind mapping.
        /// </summary>
        private static readonly (string Selector, string[] PanelIds)[] SelectorPanels =
        {
            ("QTextEdit", new[] { OceanyaPanelCatalog.IcLogPanelId, OceanyaPanelCatalog.OocChatPanelId }),
            ("QPlainTextEdit", new[] { OceanyaPanelCatalog.IcLogPanelId, OceanyaPanelCatalog.OocChatPanelId }),

            // The volume sliders are Static panels, so they need naming: mapping QSlider onto the kind
            // would have hit the logs as well.
            ("QSlider", new[]
            {
                OceanyaPanelCatalog.SliderMusicVolumePanelId,
                OceanyaPanelCatalog.SliderSfxVolumePanelId,
                OceanyaPanelCatalog.SliderBlipVolumePanelId
            })
        };

        /// <summary>
        /// Applies a stylesheet's translatable declarations to a layout.
        /// </summary>
        /// <param name="cssText">Stylesheet contents.</param>
        /// <param name="layout">Layout to update.</param>
        /// <returns>The number of panels whose styling changed.</returns>
        public static int Apply(
            string cssText,
            OceanyaThemeLayoutState layout,
            IReadOnlyList<Ao2WidgetGeometry>? widgets = null,
            IReadOnlyList<string>? assetRoots = null)
        {
            if (string.IsNullOrWhiteSpace(cssText) || layout == null)
            {
                return 0;
            }

            HashSet<string> touched = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string selector, Dictionary<string, string> declarations) in ParseRules(cssText))
            {
                // Qt allows several comma-separated selectors per rule, and they can disagree about
                // which widgets and which state they address, so each one is resolved on its own.
                foreach (string single in selector.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    Ao2SelectorTarget? parsed = ParseSelector(single);
                    if (parsed == null)
                    {
                        continue;
                    }

                    foreach (string panelId in ResolveTargets(parsed, widgets))
                    {
                        if (ApplyDeclarations(layout, panelId, declarations, parsed.State, parsed.SubControl, parsed.QtClass, assetRoots))
                        {
                            touched.Add(panelId);
                        }
                    }
                }
            }

            return touched.Count;
        }

        /// <summary>
        /// One AO2 widget's rectangle, used to resolve the coordinate selectors CSS-heavy themes use.
        /// </summary>
        /// <param name="PanelId">Panel the widget maps to.</param>
        /// <param name="Left">AO2 x coordinate, as written in the design file.</param>
        /// <param name="Top">AO2 y coordinate.</param>
        /// <param name="Width">AO2 width.</param>
        /// <param name="Height">AO2 height.</param>
        public sealed record Ao2WidgetGeometry(string PanelId, double Left, double Top, double Width, double Height);

        /// <summary>Widget state a rule addresses.</summary>
        private enum Ao2SelectorState
        {
            /// <summary>The resting appearance.</summary>
            Normal,

            /// <summary>Pointer over the widget.</summary>
            Hover,

            /// <summary>Pressed, checked or otherwise "on".</summary>
            Selected
        }

        /// <summary>A parsed single selector.</summary>
        private sealed class Ao2SelectorTarget
        {
            /// <summary>Qt class name, empty when the selector only carries attributes.</summary>
            public string QtClass { get; init; } = string.Empty;

            /// <summary>Attribute constraints, keyed by attribute name.</summary>
            public Dictionary<string, double> Geometry { get; init; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            /// <summary>State the rule addresses.</summary>
            public Ao2SelectorState State { get; init; }

            /// <summary>Qt sub-control the rule addresses, empty for the widget itself.</summary>
            public string SubControl { get; init; } = string.Empty;
        }

        /// <summary>
        /// Splits one selector into the parts that decide what it targets, or null when it cannot apply.
        /// </summary>
        /// <param name="selector">Single selector, without commas.</param>
        /// <returns>The parsed selector, or null.</returns>
        private static Ao2SelectorTarget? ParseSelector(string selector)
        {
            string trimmed = (selector ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            string subControl = string.Empty;
            Match subControlMatch = Regex.Match(trimmed, @"::([\w-]+)");
            if (subControlMatch.Success)
            {
                subControl = subControlMatch.Groups[1].Value.ToLowerInvariant();
                if (!TranslatableSubControls.Contains(subControl))
                {
                    // Sub-controls we have no field for (`::drop-down`, `::add-page`, ...).
                    return null;
                }

                trimmed = trimmed.Replace("::" + subControlMatch.Groups[1].Value, string.Empty);
            }

            Dictionary<string, double> geometry = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Regex.Matches(trimmed, @"\[\s*(\w+)\s*=\s*[""']?([-\d]+)[""']?\s*\]"))
            {
                if (double.TryParse(attribute.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    geometry[attribute.Groups[1].Value] = value;
                }
            }

            string withoutAttributes = Regex.Replace(trimmed, @"\[[^\]]*\]", string.Empty);
            string[] segments = withoutAttributes.Split(':', StringSplitOptions.RemoveEmptyEntries);
            string qtClass = withoutAttributes.StartsWith(":", StringComparison.Ordinal)
                ? string.Empty
                : segments.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;

            Ao2SelectorState state = Ao2SelectorState.Normal;
            foreach (string pseudo in withoutAttributes.Split(':', StringSplitOptions.RemoveEmptyEntries).Skip(qtClass.Length > 0 ? 1 : 0))
            {
                switch (pseudo.Trim().ToLowerInvariant())
                {
                    case "hover":
                        state = Ao2SelectorState.Hover;
                        break;
                    case "pressed":
                    case "checked":
                    case "on":
                        state = Ao2SelectorState.Selected;
                        break;
                    case "unchecked":
                    case "vertical":
                    case "horizontal":
                    case "enabled":
                        // Orientation and the resting tick state are the base appearance here.
                        break;
                    default:
                        // `:focus`, `:disabled`, `:!hover` and friends have no field to write into.
                        return null;
                }
            }

            if (qtClass.Length == 0 && geometry.Count == 0)
            {
                return null;
            }

            return new Ao2SelectorTarget
            {
                QtClass = qtClass,
                Geometry = geometry,
                State = state,
                SubControl = subControl
            };
        }

        /// <summary>
        /// Applies the stylesheet a theme ships, when it has one.
        /// </summary>
        /// <param name="themeName">Theme to read.</param>
        /// <param name="layout">Layout to update.</param>
        /// <returns>The number of panels whose styling changed.</returns>
        public static int ApplyThemeStylesheet(
            string themeName,
            OceanyaThemeLayoutState layout,
            IReadOnlyList<Ao2WidgetGeometry>? widgets = null)
        {
            string? path = Ao2ThemeLayoutImporter.ResolveThemeFilePath(themeName, "courtroom_stylesheets.css");
            return path == null ? 0 : ApplyStylesheetFile(path, layout, widgets);
        }

        /// <summary>
        /// Applies a stylesheet file to a layout.
        /// </summary>
        /// <param name="filePath">Stylesheet path.</param>
        /// <param name="layout">Layout to update.</param>
        /// <returns>The number of panels whose styling changed.</returns>
        public static int ApplyStylesheetFile(
            string filePath,
            OceanyaThemeLayoutState layout,
            IReadOnlyList<Ao2WidgetGeometry>? widgets = null)
        {
            try
            {
                return Apply(File.ReadAllText(filePath), layout, widgets, ResolveAssetRoots(filePath));
            }
            catch (IOException exception)
            {
                CustomConsole.Warning($"Theme stylesheet could not be read: {filePath}", exception);
                return 0;
            }
        }

        /// <summary>
        /// Builds the folders a stylesheet's relative image URLs are resolved against.
        /// </summary>
        /// <param name="stylesheetPath">Path of the stylesheet being read.</param>
        /// <returns>Candidate roots, most likely first.</returns>
        private static IReadOnlyList<string> ResolveAssetRoots(string stylesheetPath)
        {
            List<string> roots = new List<string>();
            foreach (string baseFolder in OceanyaClient.Features.Viewport.AO2ThemeCatalog.GetAo2ThemeScanFolders())
            {
                // URLs read `base/themes/<theme>/...`, so the install root (the parent of `base`) is the
                // folder they are relative to.
                string? installRoot = Path.GetDirectoryName(baseFolder.TrimEnd(Path.DirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(installRoot))
                {
                    roots.Add(installRoot);
                }

                roots.Add(baseFolder);
            }

            string? themeFolder = Path.GetDirectoryName(stylesheetPath);
            if (!string.IsNullOrWhiteSpace(themeFolder))
            {
                roots.Add(themeFolder);
            }

            return roots;
        }

        /// <summary>
        /// Splits a Qt stylesheet into selector/declaration pairs, dropping comments.
        /// </summary>
        /// <param name="cssText">Stylesheet contents.</param>
        /// <returns>Rules in file order.</returns>
        public static IEnumerable<(string Selector, Dictionary<string, string> Declarations)> ParseRules(string cssText)
        {
            string withoutComments = Regex.Replace(cssText, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            foreach (Match match in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}", RegexOptions.Singleline))
            {
                string selector = match.Groups[1].Value.Trim();
                Dictionary<string, string> declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in match.Groups[2].Value.Split(';'))
                {
                    int separator = part.IndexOf(':');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string property = part.Substring(0, separator).Trim();
                    string value = part.Substring(separator + 1).Trim();
                    if (property.Length > 0 && value.Length > 0)
                    {
                        declarations[property] = value;
                    }
                }

                if (selector.Length > 0 && declarations.Count > 0)
                {
                    yield return (selector, declarations);
                }
            }
        }

        private static IEnumerable<string> ResolveTargets(
            Ao2SelectorTarget selector,
            IReadOnlyList<Ao2WidgetGeometry>? widgets)
        {
            if (selector.Geometry.Count > 0)
            {
                // A coordinate selector addresses ONE widget, so it needs the theme's own rectangles.
                // Without them the only honest thing is to skip it rather than style every widget of
                // that class.
                return widgets == null
                    ? Array.Empty<string>()
                    : widgets
                        .Where(widget => MatchesGeometry(widget, selector.Geometry))
                        .Select(widget => widget.PanelId)
                        .Where(panelId => IsCompatibleClass(selector.QtClass, panelId))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
            }

            string qtClass = selector.QtClass;
            List<string> targets = new List<string>();
            foreach ((string candidate, OceanyaPanelKind[] kinds) in SelectorKinds)
            {
                if (!string.Equals(candidate, qtClass, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                targets.AddRange(OceanyaPanelCatalog.BuiltInPanels
                    .Where(panel => kinds.Contains(panel.Kind))
                    .Select(panel => panel.Id));
            }

            foreach ((string candidate, string[] panelIds) in SelectorPanels)
            {
                if (string.Equals(candidate, qtClass, StringComparison.OrdinalIgnoreCase))
                {
                    targets.AddRange(panelIds);
                }
            }

            return targets.Distinct(StringComparer.Ordinal);
        }

        /// <summary>
        /// Tests one widget rectangle against a coordinate selector's constraints.
        /// </summary>
        /// <param name="widget">Widget rectangle from the theme.</param>
        /// <param name="geometry">Attribute constraints.</param>
        /// <returns>True when every constraint matches.</returns>
        private static bool MatchesGeometry(Ao2WidgetGeometry widget, IReadOnlyDictionary<string, double> geometry)
        {
            foreach (KeyValuePair<string, double> constraint in geometry)
            {
                double actual = constraint.Key.ToLowerInvariant() switch
                {
                    "x" => widget.Left,
                    "y" => widget.Top,
                    "width" => widget.Width,
                    "height" => widget.Height,
                    _ => double.NaN
                };

                if (double.IsNaN(actual) || Math.Abs(actual - constraint.Value) > 0.5)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether a panel can wear a Qt class's styling.
        /// </summary>
        /// <param name="qtClass">Qt class name, possibly empty.</param>
        /// <param name="panelId">Panel to test.</param>
        /// <returns>True when the class maps to that panel's kind, or carries no class at all.</returns>
        private static bool IsCompatibleClass(string qtClass, string panelId)
        {
            if (string.IsNullOrEmpty(qtClass))
            {
                return true;
            }

            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
            if (descriptor == null)
            {
                return false;
            }

            foreach ((string candidate, OceanyaPanelKind[] kinds) in SelectorKinds)
            {
                if (string.Equals(candidate, qtClass, StringComparison.OrdinalIgnoreCase))
                {
                    return kinds.Contains(descriptor.Kind);
                }
            }

            // An unknown class (QSpinBox, QSlider, ...) has no kind mapping, so it cannot be honoured.
            return false;
        }

        private static bool ApplyDeclarations(
            OceanyaThemeLayoutState layout,
            string panelId,
            IReadOnlyDictionary<string, string> declarations,
            Ao2SelectorState selectorState,
            string subControl,
            string qtClass,
            IReadOnlyList<string>? assetRoots)
        {
            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
            if (descriptor == null)
            {
                return false;
            }

            string imagePath = string.Empty;
            bool hasImage = declarations.TryGetValue("image", out string? image)
                && TryResolveImageUrl(image, assetRoots, out imagePath);

            if (subControl.Length > 0)
            {
                if (!IsSubControlOfClass(subControl, qtClass))
                {
                    // A sub-control only means what its widget class says it means: `::down-arrow` is a
                    // dropdown's arrow on a QComboBox and a scrollbar's arrow on a QScrollBar, and reading
                    // the scrollbar's as a dropdown arrow put scroll arrows on the logs.
                    return false;
                }

                return ApplySubControlDeclarations(layout, panelId, descriptor, declarations, selectorState, subControl, qtClass, hasImage, imagePath);
            }

            if (string.Equals(qtClass, "QScrollBar", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyScrollBarDeclarations(layout, panelId, descriptor, declarations, trackColour: true);
            }

            if (selectorState != Ao2SelectorState.Normal && !hasImage)
            {
                // Colours and fonts of a hover/pressed state have no field of their own, and creating an
                // entry for a rule that writes nothing would leave every panel of that class "themed".
                return false;
            }

            bool changed = false;
            OceanyaPanelPlacementState state = ResolveState(layout, panelId, descriptor);

            if (hasImage)
            {
                switch (selectorState)
                {
                    case Ao2SelectorState.Hover:
                        state.HoverImagePath = imagePath;
                        break;
                    case Ao2SelectorState.Selected:
                        state.CheckedImagePath = imagePath;
                        break;
                    default:
                        state.ImagePath = imagePath;

                        // AO2 scales widget art with Qt::IgnoreAspectRatio, which is Fill here.
                        state.ImageScaling = "Fill";
                        break;
                }

                changed = true;
            }

            if (selectorState != Ao2SelectorState.Normal)
            {
                return changed;
            }

            if (TryResolveBackground(declarations, out string backgroundHex))
            {
                state.BackgroundColor = backgroundHex;
                changed = true;
            }

            if (declarations.TryGetValue("color", out string? foreground)
                && TryConvertColor(foreground, out string foregroundHex))
            {
                state.TextColor = foregroundHex;
                changed = true;
            }

            if (TryResolveBorder(declarations, out string borderColour, out double? borderWidth))
            {
                if (borderColour.Length > 0)
                {
                    state.BorderColor = borderColour;
                }

                if (borderWidth.HasValue)
                {
                    state.BorderThickness = borderWidth.Value;
                }

                changed = true;
            }

            // Qt sizes a checkbox's indicator from its font, so themes that hide the label text
            // ("color: transparent") use a huge font-size purely to grow the indicator image. Taken
            // literally that is enormous text on a panel whose indicator we do not draw from the font at
            // all, so font declarations are dropped for those.
            bool hidesText = declarations.TryGetValue("color", out string? declaredColor)
                && declaredColor.Trim().Equals("transparent", StringComparison.OrdinalIgnoreCase);
            bool fontIsIndicatorSizing = hidesText || descriptor.Kind == OceanyaPanelKind.TextToggle;

            if (!fontIsIndicatorSizing
                && declarations.TryGetValue("font-family", out string? family)
                && !string.IsNullOrWhiteSpace(family))
            {
                state.FontFamily = family.Trim().Trim('"', '\'');
                changed = true;
            }

            if (!fontIsIndicatorSizing
                && declarations.TryGetValue("font-size", out string? fontSize)
                && TryParsePixels(fontSize, out double size))
            {
                state.FontSize = size;
                changed = true;
            }

            if (!fontIsIndicatorSizing && declarations.TryGetValue("font-weight", out string? weight))
            {
                state.IsBold = weight.Trim().Equals("bold", StringComparison.OrdinalIgnoreCase)
                    || (int.TryParse(weight.Trim(), out int numeric) && numeric >= 600);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Gets a value indicating whether a sub-control belongs to the widget class the rule names.
        /// </summary>
        /// <param name="subControl">Sub-control name.</param>
        /// <param name="qtClass">Widget class, possibly empty.</param>
        /// <returns>True when the pair is one we translate.</returns>
        private static bool IsSubControlOfClass(string subControl, string qtClass)
        {
            return subControl.ToLowerInvariant() switch
            {
                "indicator" => qtClass.Length == 0 || qtClass.Equals("QCheckBox", StringComparison.OrdinalIgnoreCase),
                "down-arrow" => qtClass.Equals("QComboBox", StringComparison.OrdinalIgnoreCase),
                "handle" => qtClass.Equals("QScrollBar", StringComparison.OrdinalIgnoreCase)
                    || qtClass.Equals("QSlider", StringComparison.OrdinalIgnoreCase),
                "groove" => qtClass.Equals("QSlider", StringComparison.OrdinalIgnoreCase),
                "sub-page" => qtClass.Equals("QSlider", StringComparison.OrdinalIgnoreCase),
                "add-page" => qtClass.Equals("QSlider", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        /// Applies a rule that addresses a Qt sub-control: a tick box, a dropdown arrow or a scroll handle.
        /// </summary>
        /// <param name="layout">Layout to update.</param>
        /// <param name="panelId">Panel being styled.</param>
        /// <param name="descriptor">Panel descriptor.</param>
        /// <param name="declarations">Declarations of the rule.</param>
        /// <param name="selectorState">State the rule addresses.</param>
        /// <param name="subControl">Sub-control the rule addresses.</param>
        /// <param name="hasImage">True when the rule carries a usable image.</param>
        /// <param name="imagePath">Resolved image path.</param>
        /// <returns>True when something was written.</returns>
        private static bool ApplySubControlDeclarations(
            OceanyaThemeLayoutState layout,
            string panelId,
            OceanyaPanelDescriptor descriptor,
            IReadOnlyDictionary<string, string> declarations,
            Ao2SelectorState selectorState,
            string subControl,
            string qtClass,
            bool hasImage,
            string imagePath)
        {
            if (string.Equals(subControl, "handle", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(qtClass, "QSlider", StringComparison.OrdinalIgnoreCase))
            {
                return ApplyScrollBarDeclarations(layout, panelId, descriptor, declarations, trackColour: false);
            }

            // A slider's two pages are the filled and empty halves of its track, which is the part AO2
            // themes give a colour rather than an image.
            bool isFilledPage = string.Equals(subControl, "sub-page", StringComparison.OrdinalIgnoreCase);
            if (isFilledPage || string.Equals(subControl, "add-page", StringComparison.OrdinalIgnoreCase))
            {
                OceanyaPanelPlacementState pageState = ResolveState(layout, panelId, descriptor);
                bool wrote = false;
                if (TryResolveBackground(declarations, out string pageColour))
                {
                    if (isFilledPage)
                    {
                        pageState.FillColor = pageColour;
                    }
                    else
                    {
                        pageState.BackgroundColor = pageColour;
                    }

                    wrote = true;
                }

                if (TryResolveBorder(declarations, out string pageBorder, out _) && pageBorder.Length > 0)
                {
                    pageState.BorderColor = pageBorder;
                    wrote = true;
                }

                return wrote;
            }

            if (!hasImage || selectorState == Ao2SelectorState.Hover)
            {
                // Only artwork is representable here, and there is no hover slot for an indicator yet.
                return false;
            }

            OceanyaPanelPlacementState state = ResolveState(layout, panelId, descriptor);
            if (string.Equals(subControl, "groove", StringComparison.OrdinalIgnoreCase))
            {
                // A slider's groove is the widget-wide background art; its handle is the small overlay.
                state.ImagePath = imagePath;
                state.ImageScaling = "Fill";
            }
            else if (selectorState == Ao2SelectorState.Selected)
            {
                state.CheckedIndicatorImagePath = imagePath;
            }
            else
            {
                state.IndicatorImagePath = imagePath;
            }

            return true;
        }

        /// <summary>
        /// Applies a `QScrollBar` rule to a panel's scrollbar colours.
        /// </summary>
        /// <param name="layout">Layout to update.</param>
        /// <param name="panelId">Panel being styled.</param>
        /// <param name="descriptor">Panel descriptor.</param>
        /// <param name="declarations">Declarations of the rule.</param>
        /// <param name="trackColour">True for the scrollbar itself, false for its handle.</param>
        /// <returns>True when something was written.</returns>
        private static bool ApplyScrollBarDeclarations(
            OceanyaThemeLayoutState layout,
            string panelId,
            OceanyaPanelDescriptor descriptor,
            IReadOnlyDictionary<string, string> declarations,
            bool trackColour)
        {
            bool hasBackground = TryResolveBackground(declarations, out string backgroundHex);
            bool hasBorder = TryResolveBorder(declarations, out string borderColour, out _) && borderColour.Length > 0;
            if (!hasBackground && !hasBorder)
            {
                return false;
            }

            OceanyaPanelPlacementState state = ResolveState(layout, panelId, descriptor);
            if (hasBackground)
            {
                if (trackColour)
                {
                    state.ScrollbarTrackColor = backgroundHex;
                }
                else
                {
                    state.ScrollbarHandleColor = backgroundHex;
                }
            }

            if (hasBorder)
            {
                state.ScrollbarBorderColor = borderColour;
            }

            return true;
        }

        /// <summary>
        /// Reads a background colour from either the shorthand or the long-hand property.
        /// </summary>
        /// <param name="declarations">Declarations of the rule.</param>
        /// <param name="backgroundHex">Resolved colour.</param>
        /// <returns>True when a usable colour was found.</returns>
        private static bool TryResolveBackground(IReadOnlyDictionary<string, string> declarations, out string backgroundHex)
        {
            backgroundHex = string.Empty;
            if (declarations.TryGetValue("background-color", out string? value)
                || declarations.TryGetValue("background", out value))
            {
                return TryConvertColor(value, out backgroundHex);
            }

            return false;
        }

        /// <summary>
        /// Reads a Qt border declaration into a colour and a width.
        /// </summary>
        /// <remarks>
        /// Only a uniform border is representable, so `border-left: hidden` and friends are read for their
        /// colour but cannot narrow a single side. `hidden`, `none` and `0` all mean "no border", which a
        /// theme genuinely wants: our stock controls draw frames that AO2 widgets do not have.
        /// </remarks>
        /// <param name="declarations">Declarations of the rule.</param>
        /// <param name="colour">Border colour, empty when the rule only sets a width.</param>
        /// <param name="width">Border width in pixels.</param>
        /// <returns>True when the rule describes a border.</returns>
        private static bool TryResolveBorder(
            IReadOnlyDictionary<string, string> declarations,
            out string colour,
            out double? width)
        {
            colour = string.Empty;
            width = null;
            if (!declarations.TryGetValue("border", out string? value))
            {
                // A side declaration (`border-left: 1px solid #282728`) still tells us the colour, even
                // though only a uniform width is representable.
                foreach (string side in new[] { "border-left", "border-right", "border-top", "border-bottom" })
                {
                    if (declarations.TryGetValue(side, out string? sideValue)
                        && TryResolveBorderColour(sideValue, out colour))
                    {
                        return true;
                    }
                }

                return false;
            }

            string declaration = (value ?? string.Empty).Trim();
            if (declaration.Length == 0)
            {
                return false;
            }

            if (declaration.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                || declaration.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                width = 0;
                return true;
            }

            Match widthMatch = Regex.Match(declaration, @"([\d.]+)\s*px");
            width = widthMatch.Success
                && double.TryParse(widthMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    ? parsed
                    : 0;

            TryResolveBorderColour(declaration, out colour);
            return true;
        }

        /// <summary>
        /// Reads the colour out of a Qt border declaration.
        /// </summary>
        /// <param name="declaration">Declaration value, e.g. `1px solid #282728`.</param>
        /// <param name="colour">Resolved colour.</param>
        /// <returns>True when a colour was found.</returns>
        private static bool TryResolveBorderColour(string declaration, out string colour)
        {
            colour = string.Empty;
            string value = (declaration ?? string.Empty).Trim();
            Match colourMatch = Regex.Match(value, @"(#[0-9A-Fa-f]{3,8}|rgba?\([^)]*\))");
            if (colourMatch.Success && TryConvertColor(colourMatch.Groups[1].Value, out string converted))
            {
                colour = converted;
                return true;
            }

            string? named = value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(part => Regex.IsMatch(part, "^[A-Za-z]+$")
                    && !part.Equals("solid", StringComparison.OrdinalIgnoreCase)
                    && !part.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                    && !part.Equals("none", StringComparison.OrdinalIgnoreCase));

            return named != null && TryConvertColor(named, out colour);
        }

        /// <summary>
        /// Converts a CSS colour into the #AARRGGBB form the layout stores.
        /// </summary>
        /// <param name="value">CSS colour: hex, rgb(), rgba() or a named colour.</param>
        /// <param name="color">Converted colour.</param>
        /// <returns>True when the value could be converted.</returns>
        public static bool TryConvertColor(string value, out string color)
        {
            color = string.Empty;
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Match rgb = Regex.Match(trimmed, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$", RegexOptions.IgnoreCase);
            if (rgb.Success)
            {
                int r = int.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture);
                int g = int.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture);
                int b = int.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture);
                int a = 255;
                if (rgb.Groups[4].Success && double.TryParse(rgb.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha))
                {
                    a = alpha <= 1 ? (int)Math.Round(alpha * 255) : (int)Math.Round(alpha);
                }

                color = $"#{Math.Clamp(a, 0, 255):X2}{Math.Clamp(r, 0, 255):X2}{Math.Clamp(g, 0, 255):X2}{Math.Clamp(b, 0, 255):X2}";
                return true;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                string hex = trimmed.Substring(1);
                if (hex.Length == 3)
                {
                    hex = string.Concat(hex.Select(character => new string(character, 2)));
                }

                if (hex.Length == 6)
                {
                    color = "#FF" + hex.ToUpperInvariant();
                    return true;
                }

                if (hex.Length == 8)
                {
                    color = "#" + hex.ToUpperInvariant();
                    return true;
                }

                return false;
            }

            // Named colours are handed to WPF's own converter when the style is applied.
            color = trimmed;
            return Regex.IsMatch(trimmed, "^[A-Za-z]+$");
        }

        /// <summary>
        /// Resolves a Qt `image: url(...)` value into an absolute path on disk.
        /// </summary>
        /// <remarks>
        /// AO2 stylesheet URLs are written relative to the AO installation root (`base/themes/...`), so
        /// they are probed against the install roots and against the stylesheet's own folder.
        /// </remarks>
        /// <param name="value">Declaration value.</param>
        /// <param name="assetRoots">Folders to resolve a relative URL against.</param>
        /// <param name="imagePath">Resolved absolute path.</param>
        /// <returns>True when the file was found.</returns>
        private static bool TryResolveImageUrl(string value, IReadOnlyList<string>? assetRoots, out string imagePath)
        {
            imagePath = string.Empty;
            Match match = Regex.Match(value ?? string.Empty, @"url\(\s*[""']?([^""')]+)[""']?\s*\)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            string relative = match.Groups[1].Value.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) && File.Exists(relative))
            {
                imagePath = relative;
                return true;
            }

            foreach (string root in assetRoots ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    imagePath = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParsePixels(string value, out double pixels)
        {
            Match match = Regex.Match(value ?? string.Empty, @"([\d.]+)");
            return double.TryParse(
                match.Success ? match.Groups[1].Value : string.Empty,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out pixels) && pixels > 0;
        }

        private static OceanyaPanelPlacementState ResolveState(
            OceanyaThemeLayoutState layout,
            string panelId,
            OceanyaPanelDescriptor descriptor)
        {
            if (layout.Panels.TryGetValue(panelId, out OceanyaPanelPlacementState? existing) && existing != null)
            {
                return existing;
            }

            OceanyaPanelPlacementState state = new OceanyaPanelPlacementState
            {
                Left = descriptor.Placement.Left,
                Top = descriptor.Placement.Top,
                Width = descriptor.Placement.Width,
                Height = descriptor.Placement.Height
            };
            layout.Panels[panelId] = state;
            return state;
        }
    }
}
