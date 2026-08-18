using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OceanyaClient.Utilities;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// One host-supplied entry in a panel's right-click menu.
    /// </summary>
    /// <param name="Header">Menu text.</param>
    /// <param name="Action">Invoked when the entry is clicked.</param>
    /// <param name="IsChecked">Set for a checkable entry; null for a plain command.</param>
    public readonly record struct OceanyaPanelMenuEntry(string Header, Action Action, bool? IsChecked = null);

    /// <summary>
    /// How far and which way a panel moves in the stacking order.
    /// </summary>
    public enum OceanyaPanelOrderChange
    {
        /// <summary>Move above every other panel.</summary>
        ToFront,

        /// <summary>Move one step towards the front.</summary>
        Forward,

        /// <summary>Move one step towards the back.</summary>
        Backward,

        /// <summary>Move below every other panel.</summary>
        ToBack
    }

    /// <summary>
    /// Lets the user move and resize the GM main window's panels directly on the surface.
    /// </summary>
    /// <remarks>
    /// This is the first user-facing step of the Oceanya theme system: an interactive layout editor
    /// over the existing canvas placement, persisting to <c>SaveData.OceanyaThemeLayout</c>. The dock
    /// host arrives later and will read the same panel ids. While edit mode is active an overlay sits
    /// on top of every panel, so panel input (buttons, logs) cannot fire by accident during a drag.
    /// </remarks>
    public sealed class OceanyaPanelEditModeController
    {
        private const double ResizeGripSize = 5;

        private readonly Canvas surface;
        private readonly IDictionary<string, OceanyaPanelElements> panels;
        private readonly Action onLayoutPersisted;
        private readonly Dictionary<string, Border> overlays = new Dictionary<string, Border>(StringComparer.Ordinal);
        private readonly HashSet<string> hiddenPanelIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, OceanyaPanelPlacementState> panelStyleStates =
            new Dictionary<string, OceanyaPanelPlacementState>(StringComparer.Ordinal);

        private Border? editToolbar;
        private Button? hiddenControlsButton;
        private Button? addPanelButton;
        private string? activePanelId;
        private Point dragStartPoint;
        private OceanyaPanelPlacement dragStartPlacement;
        private bool isResizing;

        /// <summary>
        /// Initializes a new instance of the <see cref="OceanyaPanelEditModeController"/> class.
        /// </summary>
        /// <param name="surface">Canvas hosting the panels.</param>
        /// <param name="panels">Panel elements keyed by their stable panel id.</param>
        /// <param name="onLayoutPersisted">Invoked after a layout change is saved.</param>
        public OceanyaPanelEditModeController(
            Canvas surface,
            IDictionary<string, OceanyaPanelElements> panels,
            Action onLayoutPersisted)
        {
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            this.panels = panels ?? throw new ArgumentNullException(nameof(panels));
            this.onLayoutPersisted = onLayoutPersisted ?? throw new ArgumentNullException(nameof(onLayoutPersisted));
        }

        /// <summary>
        /// Gets or sets a provider of extra, host-specific menu entries for a panel (header, action).
        /// </summary>
        public Func<string, IEnumerable<OceanyaPanelMenuEntry>>? ExtraPanelMenuItemsProvider { get; set; }

        /// <summary>Gets a value indicating whether edit mode is currently active.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Raised when edit mode is turned on or off.</summary>
        public event EventHandler<bool>? ActiveChanged;

        /// <summary>
        /// Turns edit mode on or off.
        /// </summary>
        /// <returns>The new active state.</returns>
        public bool Toggle()
        {
            if (IsActive)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }

            return IsActive;
        }

        /// <summary>
        /// Turns edit mode on and shows the panel overlays.
        /// </summary>
        public void Activate()
        {
            if (IsActive)
            {
                return;
            }

            IsActive = true;
            hiddenPanelIds.Clear();
            panelStyleStates.Clear();
            if (SaveFile.Data.OceanyaThemeLayout?.Panels != null)
            {
                foreach (KeyValuePair<string, OceanyaPanelPlacementState> pair in SaveFile.Data.OceanyaThemeLayout.Panels)
                {
                    panelStyleStates[pair.Key] = pair.Value;
                }
            }

            foreach (string panelId in panels.Keys)
            {
                if (IsHiddenByLayout(panelId))
                {
                    hiddenPanelIds.Add(panelId);
                }
            }

            foreach (KeyValuePair<string, OceanyaPanelElements> pair in panels)
            {
                OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(pair.Key);
                if (descriptor == null)
                {
                    continue;
                }

                if (pair.Value.Element.Visibility != Visibility.Visible)
                {
                    // Hidden panels get no overlay at all: they are restored from the toolbar's
                    // "Hidden controls" list instead of being ghosted in place.
                    continue;
                }

                Border overlay = CreateOverlay(pair.Key, descriptor.DisplayName, isHidden: false);
                overlays[pair.Key] = overlay;
                surface.Children.Add(overlay);
                Panel.SetZIndex(overlay, ResolveOverlayZIndex(pair.Key));
                SyncOverlayToPanel(pair.Key);
            }

            ShowEditToolbar();
            surface.MouseRightButtonUp += Surface_MouseRightButtonUp;
            Window? hostWindow = Window.GetWindow(surface);
            if (hostWindow != null)
            {
                hostWindow.PreviewKeyDown += HostWindow_PreviewKeyDown;
            }

            ActiveChanged?.Invoke(this, true);
        }

        /// <summary>
        /// Turns edit mode off, removes the overlays and saves the layout.
        /// </summary>
        public void Deactivate()
        {
            if (!IsActive)
            {
                return;
            }

            foreach (Border overlay in overlays.Values)
            {
                surface.Children.Remove(overlay);
            }

            overlays.Clear();
            if (editToolbar != null)
            {
                surface.Children.Remove(editToolbar);
                editToolbar = null;
            }

            surface.MouseRightButtonUp -= Surface_MouseRightButtonUp;
            Window? hostWindow = Window.GetWindow(surface);
            if (hostWindow != null)
            {
                hostWindow.PreviewKeyDown -= HostWindow_PreviewKeyDown;
            }

            activePanelId = null;
            IsActive = false;
            PersistLayout();
            ActiveChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Restores every panel to its catalog default placement and clears the saved layout.
        /// </summary>
        public void ResetLayout()
        {
            // Styling (fonts, swapped images, item sizes) is applied straight onto the live controls, so
            // clearing the saved layout is not enough: each panel is restored from the baseline captured
            // before its first restyle.
            foreach (KeyValuePair<string, OceanyaPanelElements> pair in panels)
            {
                OceanyaPanelStyleApplier.RestoreBaseline(pair.Key, pair.Value.Element);
            }

            List<OceanyaCustomPanelDefinition> customPanels =
                SaveFile.Data.OceanyaThemeLayout?.CustomPanels?.ToList() ?? new List<OceanyaCustomPanelDefinition>();
            SaveFile.Data.OceanyaThemeLayout = new OceanyaThemeLayoutState { CustomPanels = customPanels };
            panelStyleStates.Clear();
            hiddenPanelIds.Clear();
            foreach (OceanyaPanelElements elements in panels.Values)
            {
                elements.Element.Visibility = Visibility.Visible;
                if (elements.Backdrop != null)
                {
                    elements.Backdrop.Visibility = Visibility.Visible;
                }
            }

            OceanyaPanelLayout.ApplyDefaultPlacements(new Dictionary<string, OceanyaPanelElements>(panels));
            SaveFile.Save();
            foreach (string panelId in overlays.Keys.ToList())
            {
                SyncOverlayToPanel(panelId);
            }

            RefreshHiddenControlsButton();
            onLayoutPersisted();
        }

        /// <summary>
        /// Re-syncs every overlay to its panel, after the host moved panels around (surface growth).
        /// </summary>
        public void RefreshOverlays()
        {
            if (!IsActive)
            {
                return;
            }

            foreach (string panelId in overlays.Keys)
            {
                SyncOverlayToPanel(panelId);
            }

            PositionEditToolbar();
        }

        /// <summary>
        /// Writes the current panel placements into the savefile.
        /// </summary>
        public void PersistLayout()
        {
            OceanyaThemeLayoutState layout = new OceanyaThemeLayoutState();
            foreach (KeyValuePair<string, OceanyaPanelElements> pair in panels)
            {
                if (OceanyaPanelCatalog.TryGet(pair.Key) == null)
                {
                    continue;
                }

                OceanyaPanelPlacement placement = OceanyaPanelLayout.CapturePlacement(pair.Value.Element);
                panelStyleStates.TryGetValue(pair.Key, out OceanyaPanelPlacementState? style);
                layout.Panels[pair.Key] = new OceanyaPanelPlacementState
                {
                    Left = placement.Left,
                    Top = placement.Top,
                    Width = placement.Width,
                    Height = placement.Height,
                    IsHidden = hiddenPanelIds.Contains(pair.Key),
                    FontSize = style?.FontSize ?? 0,
                    ImageScaling = style?.ImageScaling ?? string.Empty,
                    ItemSize = style?.ItemSize ?? 0,
                    FontFamily = style?.FontFamily ?? string.Empty,
                    IsBold = style?.IsBold ?? false,
                    ImagePath = style?.ImagePath ?? string.Empty,
                    ZOrder = style?.ZOrder ?? 0
                };
            }

            layout.CustomPanels = SaveFile.Data.OceanyaThemeLayout?.CustomPanels ?? new List<OceanyaCustomPanelDefinition>();
            foreach (OceanyaCustomPanelDefinition definition in layout.CustomPanels)
            {
                if (layout.Panels.TryGetValue(definition.Id, out OceanyaPanelPlacementState? state) && state != null)
                {
                    definition.Placement = state;
                }
            }

            SaveFile.Data.OceanyaThemeLayout = layout;
            SaveFile.Save();
            onLayoutPersisted();
        }

        private void Surface_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // WPF opens a ContextMenu on MouseRightButtonUp, so marking this handled for a click that
            // landed on a panel overlay would swallow that panel's own menu - which is exactly what
            // made every right-click show the "Add panel" menu instead.
            if (IsOverlayElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            ShowAddPanelMenu(null, e.GetPosition(surface));
            e.Handled = true;
        }

        /// <summary>
        /// Gets a value indicating whether an element belongs to one of the panel overlays.
        /// </summary>
        /// <param name="element">Element that was clicked.</param>
        /// <returns>True when the element is an overlay or sits inside one.</returns>
        private bool IsOverlayElement(DependencyObject? element)
        {
            for (DependencyObject? current = element; current != null;)
            {
                if (current is Border border && overlays.ContainsValue(border))
                {
                    return true;
                }

                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : (current as FrameworkContentElement)?.Parent;
            }

            return false;
        }

        private void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Deactivate();
            }
        }

        /// <summary>
        /// Adds the floating edit-mode toolbar. It sits above the panel overlays because those cover
        /// the bottom bar (including the button that turned edit mode on), which would otherwise leave
        /// no way out.
        /// </summary>
        private void ShowEditToolbar()
        {
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(CreateToolbarButton("Done", () => Deactivate()));
            buttons.Children.Add(CreateToolbarButton("Reset all", ResetLayout));
            hiddenControlsButton = CreateToolbarButton("Hidden controls", ShowHiddenControlsMenu);
            buttons.Children.Add(hiddenControlsButton);
            addPanelButton = CreateToolbarButton("Add panel", () => ShowAddPanelMenu(addPanelButton, null));
            buttons.Children.Add(addPanelButton);

            editToolbar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x11, 0x14, 0x18)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Child = buttons
            };

            surface.Children.Add(editToolbar);
            Panel.SetZIndex(editToolbar, 10001);
            RefreshHiddenControlsButton();
            PositionEditToolbar();
        }

        /// <summary>
        /// Opens the list of hidden panels so they can be restored.
        /// </summary>
        private void ShowHiddenControlsMenu()
        {
            if (hiddenControlsButton == null)
            {
                return;
            }

            ContextMenu menu = new ContextMenu { PlacementTarget = hiddenControlsButton };
            ContextMenuSectionHelper.AddHeader(menu, "Hidden controls", addLeadingSeparator: false);

            List<string> hidden = hiddenPanelIds
                .Where(panelId => OceanyaPanelCatalog.TryGet(panelId) != null)
                .OrderBy(panelId => OceanyaPanelCatalog.Get(panelId).DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hidden.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "Nothing is hidden", IsEnabled = false });
            }
            else
            {
                foreach (string panelId in hidden)
                {
                    MenuItem restore = new MenuItem { Header = "Show " + OceanyaPanelCatalog.Get(panelId).DisplayName };
                    string capturedId = panelId;
                    restore.Click += (_, _) => SetPanelHidden(capturedId, false);
                    menu.Items.Add(restore);
                }

                ContextMenuSectionHelper.AddHeader(menu, "All", addLeadingSeparator: true);
                MenuItem restoreAll = new MenuItem { Header = "Show all hidden controls" };
                restoreAll.Click += (_, _) =>
                {
                    foreach (string panelId in hidden)
                    {
                        SetPanelHidden(panelId, false);
                    }
                };
                menu.Items.Add(restoreAll);
            }

            menu.IsOpen = true;
        }

        /// <summary>
        /// Updates the toolbar button caption with how many panels are currently hidden.
        /// </summary>
        private void RefreshHiddenControlsButton()
        {
            if (hiddenControlsButton != null)
            {
                int hiddenCount = hiddenPanelIds.Count;
                hiddenControlsButton.Content = hiddenCount == 0 ? "Hidden controls" : $"Hidden controls ({hiddenCount})";
                PositionEditToolbar();
            }
        }

        private static Button CreateToolbarButton(string caption, Action onClick)
        {
            Button button = new Button
            {
                Content = caption,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)),
                FontSize = 11
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        /// <summary>
        /// Keeps the toolbar pinned to the top-right of whatever area the panels currently occupy.
        /// </summary>
        private void PositionEditToolbar()
        {
            if (editToolbar == null)
            {
                return;
            }

            Rect bounds = OceanyaPanelLayout.CalculateBounds(new Dictionary<string, OceanyaPanelElements>(panels));
            editToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double toolbarWidth = editToolbar.DesiredSize.Width;
            double right = bounds.IsEmpty ? surface.ActualWidth : bounds.Right;
            Canvas.SetLeft(editToolbar, Math.Max(0, right - toolbarWidth - 4));
            Canvas.SetTop(editToolbar, Math.Max(0, (bounds.IsEmpty ? 0 : bounds.Top) + 4));
        }

        /// <summary>
        /// Gets a value indicating whether a panel is hidden because the saved theme layout hides it.
        /// </summary>
        /// <param name="panelId">Panel to test.</param>
        /// <returns>True when the layout marks the panel hidden.</returns>
        private static bool IsHiddenByLayout(string panelId)
        {
            return SaveFile.Data.OceanyaThemeLayout?.Panels != null
                && SaveFile.Data.OceanyaThemeLayout.Panels.TryGetValue(panelId, out OceanyaPanelPlacementState? state)
                && state?.IsHidden == true;
        }

        private Border CreateOverlay(string panelId, string displayName, bool isHidden)
        {
            // Outline only, no filled tint: stacked translucent fills made overlapping panels unreadable
            // and hid the controls underneath. Transparent (not null) keeps the overlay hit-testable.
            Border overlay = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.SizeAll,
                Tag = panelId,
                ContextMenu = CreatePanelContextMenu(panelId)
            };

            overlay.MouseEnter += (_, _) =>
            {
                overlay.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
                overlay.BorderThickness = new Thickness(2);
            };
            overlay.MouseLeave += (_, _) =>
            {
                overlay.Background = Brushes.Transparent;
                overlay.BorderThickness = new Thickness(1);
            };

            // The name is a tooltip rather than painted text: with many small overlapping panels the
            // labels were noise, and they hid the control underneath.
            overlay.ToolTip = displayName;

            Grid content = new Grid();
            content.Children.Add(new Rectangle
            {
                Width = ResizeGripSize,
                Height = ResizeGripSize,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                IsHitTestVisible = false
            });
            overlay.Child = content;

            overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            overlay.MouseMove += Overlay_MouseMove;
            overlay.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;
            return overlay;
        }

        /// <summary>
        /// Builds the right-click menu offering per-panel and global default restores.
        /// </summary>
        /// <param name="panelId">Panel the menu belongs to.</param>
        /// <returns>The context menu.</returns>
        private ContextMenu CreatePanelContextMenu(string panelId)
        {
            OceanyaPanelDescriptor descriptor = OceanyaPanelCatalog.Get(panelId);
            ContextMenu menu = new ContextMenu();
            ContextMenuSectionHelper.AddHeader(menu, $"Panel ({descriptor.DisplayName})", addLeadingSeparator: false);

            MenuItem defaultPosition = new MenuItem { Header = "Set default position" };
            defaultPosition.Click += (_, _) => RestoreDefault(panelId, restorePosition: true, restoreSize: false);
            menu.Items.Add(defaultPosition);

            MenuItem defaultSize = new MenuItem { Header = "Set default size" };
            defaultSize.Click += (_, _) => RestoreDefault(panelId, restorePosition: false, restoreSize: true);
            menu.Items.Add(defaultSize);

            MenuItem defaultBoth = new MenuItem { Header = "Set default position and size" };
            defaultBoth.Click += (_, _) => RestoreDefault(panelId, restorePosition: true, restoreSize: true);
            menu.Items.Add(defaultBoth);

            IEnumerable<OceanyaPanelMenuEntry>? extraItems = ExtraPanelMenuItemsProvider?.Invoke(panelId);
            if (extraItems != null)
            {
                foreach (OceanyaPanelMenuEntry entry in extraItems)
                {
                    MenuItem item = new MenuItem { Header = entry.Header };
                    if (entry.IsChecked.HasValue)
                    {
                        item.IsCheckable = true;
                        item.IsChecked = entry.IsChecked.Value;
                    }

                    Action action = entry.Action;
                    item.Click += (_, _) => action();
                    menu.Items.Add(item);
                }
            }

            AppendKindOptions(menu, descriptor);

            if (OceanyaPanelCatalog.IsCustomPanel(panelId))
            {
                ContextMenuSectionHelper.AddHeader(menu, "Added panel", addLeadingSeparator: true);
                MenuItem delete = new MenuItem { Header = "Delete this panel" };
                delete.Click += (_, _) => DeleteCustomPanel(panelId);
                menu.Items.Add(delete);
            }

            bool isHidden = IsHiddenByLayout(panelId);
            MenuItem visibility = new MenuItem { Header = isHidden ? "Show panel" : "Hide panel" };
            visibility.Click += (_, _) => SetPanelHidden(panelId, !isHidden);
            menu.Items.Add(visibility);

            ContextMenuSectionHelper.AddHeader(menu, "Layout", addLeadingSeparator: true);
            MenuItem resetAll = new MenuItem { Header = "Set all panels to default" };
            resetAll.Click += (_, _) => ResetLayout();
            menu.Items.Add(resetAll);

            return menu;
        }

        /// <summary>
        /// Opens the menu for adding a picture or colour panel.
        /// </summary>
        /// <param name="placementTarget">Element the menu is anchored to, when opened from the toolbar.</param>
        /// <param name="dropPoint">Surface point to drop the new panel at, when opened from empty space.</param>
        public void ShowAddPanelMenu(UIElement? placementTarget, Point? dropPoint)
        {
            ContextMenu menu = new ContextMenu();
            if (placementTarget != null)
            {
                menu.PlacementTarget = placementTarget;
            }

            ContextMenuSectionHelper.AddHeader(menu, "Add panel", addLeadingSeparator: false);

            MenuItem addImage = new MenuItem { Header = "Image panel..." };
            addImage.Click += (_, _) => AddImagePanel(dropPoint);
            menu.Items.Add(addImage);

            MenuItem addColor = new MenuItem { Header = "Solid colour panel..." };
            addColor.Click += (_, _) => AddColorPanel(dropPoint);
            menu.Items.Add(addColor);

            menu.IsOpen = true;
        }

        /// <summary>
        /// Asks for a picture and adds it as a new panel.
        /// </summary>
        /// <param name="dropPoint">Where to place it, or null for the default spot.</param>
        private void AddImagePanel(Point? dropPoint)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick an image for the new panel",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            AddCustomPanel(OceanyaCustomPanelFactory.CreateDefinition(
                OceanyaCustomPanelFactory.ImageKind,
                ResolveNewPanelPlacement(dropPoint, 160, 120),
                imagePath: dialog.FileName));
        }

        /// <summary>
        /// Asks for a colour and adds it as a new panel.
        /// </summary>
        /// <param name="dropPoint">Where to place it, or null for the default spot.</param>
        private void AddColorPanel(Point? dropPoint)
        {
            Color? picked = AOCharacterFileCreatorWindow.ShowSolidColorPickerDialog(
                Window.GetWindow(surface),
                Color.FromRgb(0x3F, 0x8C, 0xD8));
            if (picked == null)
            {
                return;
            }

            AddCustomPanel(OceanyaCustomPanelFactory.CreateDefinition(
                OceanyaCustomPanelFactory.ColorKind,
                ResolveNewPanelPlacement(dropPoint, 120, 80),
                color: picked.Value.ToString()));
        }

        private OceanyaPanelPlacement ResolveNewPanelPlacement(Point? dropPoint, double width, double height)
        {
            Point origin = dropPoint ?? new Point(20, 20);
            return new OceanyaPanelPlacement(origin.X, origin.Y, width, height);
        }

        private void AddCustomPanel(OceanyaCustomPanelDefinition definition)
        {
            SaveFile.Data.OceanyaThemeLayout ??= new OceanyaThemeLayoutState();
            SaveFile.Data.OceanyaThemeLayout.CustomPanels.Add(definition);

            FrameworkElement element = OceanyaCustomPanelFactory.CreateElement(definition);
            OceanyaPanelLayout.ApplyPlacement(
                element,
                new OceanyaPanelPlacement(
                    definition.Placement.Left,
                    definition.Placement.Top,
                    definition.Placement.Width,
                    definition.Placement.Height));
            surface.Children.Add(element);
            panels[definition.Id] = new OceanyaPanelElements(element);

            if (IsActive)
            {
                Border overlay = CreateOverlay(definition.Id, definition.DisplayName, isHidden: false);
                overlays[definition.Id] = overlay;
                surface.Children.Add(overlay);
                Panel.SetZIndex(overlay, ResolveOverlayZIndex(definition.Id));
                SyncOverlayToPanel(definition.Id);
            }

            PersistLayout();
        }

        /// <summary>
        /// Deletes a user-added panel and forgets its definition.
        /// </summary>
        /// <param name="panelId">Panel to delete.</param>
        private void DeleteCustomPanel(string panelId)
        {
            if (!OceanyaPanelCatalog.IsCustomPanel(panelId) || !panels.TryGetValue(panelId, out OceanyaPanelElements elements))
            {
                return;
            }

            surface.Children.Remove(elements.Element);
            if (overlays.TryGetValue(panelId, out Border? overlay))
            {
                surface.Children.Remove(overlay);
                overlays.Remove(panelId);
            }

            panels.Remove(panelId);
            OceanyaPanelCatalog.UnregisterCustomPanel(panelId);
            SaveFile.Data.OceanyaThemeLayout?.CustomPanels.RemoveAll(definition =>
                string.Equals(definition.Id, panelId, StringComparison.Ordinal));
            PersistLayout();
        }

        /// <summary>
        /// Adds the style options that make sense for a panel's control kind.
        /// </summary>
        /// <param name="menu">Menu being built.</param>
        /// <param name="descriptor">Panel descriptor supplying the kind.</param>
        private void AppendKindOptions(ContextMenu menu, OceanyaPanelDescriptor descriptor)
        {
            switch (descriptor.Kind)
            {
                case OceanyaPanelKind.TextInput:
                case OceanyaPanelKind.Dropdown:
                case OceanyaPanelKind.TextToggle:
                    AddMenuItem(menu, "Font settings...", () => EditPanelSettings(
                        descriptor.Id,
                        state => OceanyaPanelSettingsDialogs.ShowFontSettings(
                            Window.GetWindow(surface),
                            descriptor.DisplayName,
                            state)));
                    break;

                case OceanyaPanelKind.ImageButton:
                    AddMenuItem(menu, "Image settings...", () => EditPanelSettings(
                        descriptor.Id,
                        state => OceanyaPanelSettingsDialogs.ShowImageSettings(
                            Window.GetWindow(surface),
                            descriptor.DisplayName,
                            state)));
                    break;

                case OceanyaPanelKind.ItemGrid:
                    AddMenuItem(menu, "Grid settings...", () => EditPanelSettings(
                        descriptor.Id,
                        state => OceanyaPanelSettingsDialogs.ShowGridSettings(
                            Window.GetWindow(surface),
                            descriptor.DisplayName,
                            state)));
                    break;
            }

            ContextMenuSectionHelper.AddHeader(menu, "Order", addLeadingSeparator: true);
            AddMenuItem(menu, "Bring to front", () => ChangePanelOrder(descriptor.Id, OceanyaPanelOrderChange.ToFront));
            AddMenuItem(menu, "Bring forward", () => ChangePanelOrder(descriptor.Id, OceanyaPanelOrderChange.Forward));
            AddMenuItem(menu, "Send backward", () => ChangePanelOrder(descriptor.Id, OceanyaPanelOrderChange.Backward));
            AddMenuItem(menu, "Send to back", () => ChangePanelOrder(descriptor.Id, OceanyaPanelOrderChange.ToBack));
        }

        private static void AddMenuItem(ContextMenu menu, string header, Action onClick)
        {
            MenuItem item = new MenuItem { Header = header };
            item.Click += (_, _) => onClick();
            menu.Items.Add(item);
        }

        /// <summary>
        /// Opens a settings popup for a panel and applies whatever it changed.
        /// </summary>
        /// <param name="panelId">Panel being styled.</param>
        /// <param name="showDialog">Dialog to show; returns true when the user accepted.</param>
        private void EditPanelSettings(string panelId, Func<OceanyaPanelPlacementState, bool> showDialog)
        {
            OceanyaPanelPlacementState state = ResolvePanelState(panelId);
            if (!showDialog(state))
            {
                return;
            }

            ApplyResolvedPanelState(panelId, state);
        }

        /// <summary>
        /// Moves a panel within the stacking order.
        /// </summary>
        /// <param name="panelId">Panel to move.</param>
        /// <param name="change">How far and which way to move it.</param>
        private void ChangePanelOrder(string panelId, OceanyaPanelOrderChange change)
        {
            OceanyaPanelPlacementState state = ResolvePanelState(panelId);
            int[] usedOrders = panels.Keys
                .Select(id => ResolvePanelState(id).ZOrder)
                .ToArray();
            int minimum = usedOrders.Length == 0 ? 0 : usedOrders.Min();
            int maximum = usedOrders.Length == 0 ? 0 : usedOrders.Max();

            state.ZOrder = change switch
            {
                OceanyaPanelOrderChange.ToFront => maximum + 1,
                OceanyaPanelOrderChange.ToBack => minimum - 1,
                OceanyaPanelOrderChange.Forward => state.ZOrder + 1,
                _ => state.ZOrder - 1
            };

            ApplyResolvedPanelState(panelId, state);
        }

        /// <summary>
        /// Gets (creating if needed) the saved state for a panel.
        /// </summary>
        /// <param name="panelId">Panel id.</param>
        /// <returns>The panel's saved state.</returns>
        private OceanyaPanelPlacementState ResolvePanelState(string panelId)
        {
            SaveFile.Data.OceanyaThemeLayout ??= new OceanyaThemeLayoutState();
            if (SaveFile.Data.OceanyaThemeLayout.Panels.TryGetValue(panelId, out OceanyaPanelPlacementState? existing) && existing != null)
            {
                panelStyleStates[panelId] = existing;
                return existing;
            }

            OceanyaPanelPlacement current = panels.TryGetValue(panelId, out OceanyaPanelElements elements)
                ? OceanyaPanelLayout.CapturePlacement(elements.Element)
                : OceanyaPanelCatalog.Get(panelId).Placement;

            OceanyaPanelPlacementState state = new OceanyaPanelPlacementState
            {
                Left = current.Left,
                Top = current.Top,
                Width = current.Width,
                Height = current.Height
            };
            SaveFile.Data.OceanyaThemeLayout.Panels[panelId] = state;
            panelStyleStates[panelId] = state;
            return state;
        }

        private void ApplyResolvedPanelState(string panelId, OceanyaPanelPlacementState state)
        {
            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
            if (descriptor == null || !panels.TryGetValue(panelId, out OceanyaPanelElements elements))
            {
                return;
            }

            panelStyleStates[panelId] = state;
            OceanyaPanelStyleApplier.Apply(descriptor, elements.Element, state);
            if (overlays.TryGetValue(panelId, out Border? overlay))
            {
                Panel.SetZIndex(overlay, ResolveOverlayZIndex(panelId));
            }

            SyncOverlayToPanel(panelId);
            PersistLayout();
        }

        /// <summary>
        /// Resolves the z-index for a panel's overlay so overlays stack in the same order as the panels
        /// they belong to, and always above the panels themselves.
        /// </summary>
        /// <param name="panelId">Panel id.</param>
        /// <returns>The overlay z-index.</returns>
        private int ResolveOverlayZIndex(string panelId)
        {
            int panelOrder = panelStyleStates.TryGetValue(panelId, out OceanyaPanelPlacementState? state) ? state.ZOrder : 0;
            return 10000 + panelOrder;
        }

        /// <summary>
        /// Hides or shows a panel and remembers the choice in the theme layout.
        /// </summary>
        /// <param name="panelId">Panel to hide or show.</param>
        /// <param name="isHidden">True to hide the panel.</param>
        public void SetPanelHidden(string panelId, bool isHidden)
        {
            if (!panels.TryGetValue(panelId, out OceanyaPanelElements elements))
            {
                return;
            }

            hiddenPanelIds.Remove(panelId);
            if (isHidden)
            {
                hiddenPanelIds.Add(panelId);
            }

            elements.Element.Visibility = isHidden ? Visibility.Collapsed : Visibility.Visible;
            if (elements.Backdrop != null)
            {
                elements.Backdrop.Visibility = elements.Element.Visibility;
            }

            if (overlays.TryGetValue(panelId, out Border? existingOverlay))
            {
                surface.Children.Remove(existingOverlay);
                overlays.Remove(panelId);
            }

            if (!isHidden && IsActive)
            {
                Border restored = CreateOverlay(panelId, OceanyaPanelCatalog.Get(panelId).DisplayName, isHidden: false);
                overlays[panelId] = restored;
                surface.Children.Add(restored);
                Panel.SetZIndex(restored, ResolveOverlayZIndex(panelId));
                SyncOverlayToPanel(panelId);
            }

            PersistLayout();
            RefreshHiddenControlsButton();
        }

        /// <summary>
        /// Applies the hidden panels recorded in a saved layout.
        /// </summary>
        /// <param name="panels">Panel elements keyed by their stable panel id.</param>
        /// <param name="savedLayout">Saved layout carrying the hidden flags.</param>
        public static void ApplyHiddenPanels(
            IReadOnlyDictionary<string, OceanyaPanelElements> panels,
            OceanyaThemeLayoutState? savedLayout)
        {
            if (savedLayout?.Panels == null)
            {
                return;
            }

            foreach (KeyValuePair<string, OceanyaPanelElements> pair in panels)
            {
                if (!savedLayout.Panels.TryGetValue(pair.Key, out OceanyaPanelPlacementState? state) || state?.IsHidden != true)
                {
                    continue;
                }

                pair.Value.Element.Visibility = Visibility.Collapsed;
                if (pair.Value.Backdrop != null)
                {
                    pair.Value.Backdrop.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Restores one panel's default position, size, or both.
        /// </summary>
        /// <param name="panelId">Panel to restore.</param>
        /// <param name="restorePosition">Whether to restore the default position.</param>
        /// <param name="restoreSize">Whether to restore the default size.</param>
        public void RestoreDefault(string panelId, bool restorePosition, bool restoreSize)
        {
            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(panelId);
            if (descriptor == null || !panels.TryGetValue(panelId, out OceanyaPanelElements elements))
            {
                return;
            }

            OceanyaPanelPlacement current = OceanyaPanelLayout.CapturePlacement(elements.Element);
            OceanyaPanelPlacement target = new OceanyaPanelPlacement(
                restorePosition ? descriptor.Placement.Left : current.Left,
                restorePosition ? descriptor.Placement.Top : current.Top,
                restoreSize ? descriptor.Placement.Width : current.Width,
                restoreSize ? descriptor.Placement.Height : current.Height);

            ApplyPlacementWithBackdrop(elements, current, target);
            SyncOverlayToPanel(panelId);
            PersistLayout();
        }

        /// <summary>
        /// Applies a placement to a panel and drags its backdrop along by the same delta.
        /// </summary>
        /// <param name="elements">Panel elements.</param>
        /// <param name="previous">Placement before the change.</param>
        /// <param name="target">Placement to apply.</param>
        private static void ApplyPlacementWithBackdrop(
            OceanyaPanelElements elements,
            OceanyaPanelPlacement previous,
            OceanyaPanelPlacement target)
        {
            OceanyaPanelLayout.ApplyPlacement(elements.Element, target);
            if (elements.Backdrop == null)
            {
                return;
            }

            OceanyaPanelPlacement backdrop = OceanyaPanelLayout.CapturePlacement(elements.Backdrop);
            OceanyaPanelLayout.ApplyPlacement(
                elements.Backdrop,
                new OceanyaPanelPlacement(
                    backdrop.Left + (target.Left - previous.Left),
                    backdrop.Top + (target.Top - previous.Top),
                    Math.Max(0, backdrop.Width + (target.Width - previous.Width)),
                    Math.Max(0, backdrop.Height + (target.Height - previous.Height))));
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border overlay || overlay.Tag is not string panelId)
            {
                return;
            }

            activePanelId = panelId;
            dragStartPoint = e.GetPosition(surface);
            dragStartPlacement = OceanyaPanelLayout.CapturePlacement(panels[panelId].Element);

            Point localPoint = e.GetPosition(overlay);
            isResizing = localPoint.X >= overlay.ActualWidth - ResizeGripSize
                && localPoint.Y >= overlay.ActualHeight - ResizeGripSize;

            overlay.CaptureMouse();
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (activePanelId == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            OceanyaPanelDescriptor? descriptor = OceanyaPanelCatalog.TryGet(activePanelId);
            if (descriptor == null)
            {
                return;
            }

            Point current = e.GetPosition(surface);
            double deltaX = current.X - dragStartPoint.X;
            double deltaY = current.Y - dragStartPoint.Y;

            OceanyaPanelPlacement requested = isResizing
                ? new OceanyaPanelPlacement(
                    dragStartPlacement.Left,
                    dragStartPlacement.Top,
                    descriptor.AllowsHorizontalResize ? dragStartPlacement.Width + deltaX : dragStartPlacement.Width,
                    descriptor.AllowsVerticalResize ? dragStartPlacement.Height + deltaY : dragStartPlacement.Height)
                : new OceanyaPanelPlacement(
                    dragStartPlacement.Left + deltaX,
                    dragStartPlacement.Top + deltaY,
                    dragStartPlacement.Width,
                    dragStartPlacement.Height);

            OceanyaPanelPlacement resolved = OceanyaPanelLayout.SanitizePlacement(requested, descriptor);
            OceanyaPanelElements elements = panels[activePanelId];
            OceanyaPanelPlacement previous = OceanyaPanelLayout.CapturePlacement(elements.Element);
            ApplyPlacementWithBackdrop(elements, previous, resolved);

            SyncOverlayToPanel(activePanelId);
            e.Handled = true;
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border overlay)
            {
                overlay.ReleaseMouseCapture();
            }

            if (activePanelId != null)
            {
                activePanelId = null;
                isResizing = false;
                PersistLayout();
            }

            e.Handled = true;
        }

        private void SyncOverlayToPanel(string panelId)
        {
            if (!overlays.TryGetValue(panelId, out Border? overlay) || !panels.TryGetValue(panelId, out OceanyaPanelElements elements))
            {
                return;
            }

            OceanyaPanelPlacement placement = OceanyaPanelLayout.CapturePlacement(elements.Element);
            Canvas.SetLeft(overlay, placement.Left);
            Canvas.SetTop(overlay, placement.Top);
            overlay.Width = placement.Width;
            overlay.Height = placement.Height;
        }
    }
}
