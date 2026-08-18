using System;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Controls;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// The GM bottom status bar: option checkboxes plus the refresh, settings, area, music and
    /// viewport buttons.
    /// </summary>
    /// <remarks>
    /// Self-contained panel for the Oceanya theme system: it owns the bar's visuals and raises events,
    /// while the host keeps every action's logic. The area and music buttons are exposed as popup
    /// anchors because WPF popups need a visual to position against. See
    /// <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class BottomStatusBarPanel : UserControl
    {
        private bool suppressCheckBoxEvents;

        /// <summary>
        /// Initializes a new instance of the <see cref="BottomStatusBarPanel"/> class.
        /// </summary>
        public BottomStatusBarPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Hands the bar's individually placeable controls over to the host surface.
        /// </summary>
        /// <remarks>
        /// Reparenting keeps this control's handlers and state wiring intact; only the layout parent
        /// changes. Coordinates are relative to this panel's own canvas.
        /// </remarks>
        /// <returns>Placeable child controls keyed by their stable panel id.</returns>
        public IReadOnlyDictionary<string, FrameworkElement> ExtractPlaceableControls()
        {
            Dictionary<string, FrameworkElement> placeable = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
            {
                ["bar_check_sticky"] = chkSticky,
                ["bar_check_switchpos"] = chkPosOnIniSwap,
                ["bar_check_invertlog"] = chkInvertLog,
                ["bar_button_editlayout"] = btnEditLayout,
                ["bar_button_refresh"] = btnRefreshCharacters,
                ["bar_button_viewport"] = btnViewport,
                ["bar_button_area"] = btnAreaNavigator,
                ["bar_button_debug"] = btnDebug,
                ["bar_button_music"] = btnMusicList,
                ["bar_button_settings"] = btnSettings
            };

            foreach (FrameworkElement element in placeable.Values)
            {
                PanelCanvas.Children.Remove(element);
            }

            return placeable;
        }

        /// <summary>Raised when the refresh characters button is pressed.</summary>
        public event EventHandler? RefreshCharactersRequested;

        /// <summary>Raised when the settings button is pressed.</summary>
        public event EventHandler? SettingsRequested;

        /// <summary>Raised when the debug button is pressed.</summary>
        public event EventHandler? DebugRequested;

        /// <summary>Raised when the area navigator button is pressed.</summary>
        public event EventHandler? AreaNavigatorRequested;

        /// <summary>Raised when the music list button is pressed.</summary>
        public event EventHandler? MusicListRequested;

        /// <summary>Raised when the viewport button is pressed.</summary>
        public event EventHandler? ViewportRequested;

        /// <summary>Raised when the layout edit mode toggle changes.</summary>
        public event EventHandler<bool>? EditLayoutModeChanged;

        /// <summary>Raised when the sticky effects checkbox changes.</summary>
        public event EventHandler<bool>? StickyEffectsChanged;

        /// <summary>Raised when the switch-position-on-INI-swap checkbox changes.</summary>
        public event EventHandler<bool>? SwitchPositionOnIniSwapChanged;

        /// <summary>Raised when the invert IC log checkbox changes.</summary>
        public event EventHandler<bool>? InvertIcLogChanged;

        /// <summary>Gets the element the area navigator popup anchors to.</summary>
        public UIElement AreaNavigatorAnchor => btnAreaNavigator;

        /// <summary>Gets the element the music list popup anchors to.</summary>
        public UIElement MusicListAnchor => btnMusicList;

        /// <summary>
        /// Gets or sets the sticky effects checkbox state without raising <see cref="StickyEffectsChanged"/>.
        /// </summary>
        public bool IsStickyEffectsChecked
        {
            get => chkSticky.IsChecked == true;
            set => SetCheckBoxSilently(chkSticky, value);
        }

        /// <summary>
        /// Gets or sets the switch-position checkbox state without raising
        /// <see cref="SwitchPositionOnIniSwapChanged"/>.
        /// </summary>
        public bool IsSwitchPositionOnIniSwapChecked
        {
            get => chkPosOnIniSwap.IsChecked == true;
            set => SetCheckBoxSilently(chkPosOnIniSwap, value);
        }

        /// <summary>
        /// Gets or sets the invert IC log checkbox state without raising <see cref="InvertIcLogChanged"/>.
        /// </summary>
        public bool IsInvertIcLogChecked
        {
            get => chkInvertLog.IsChecked == true;
            set => SetCheckBoxSilently(chkInvertLog, value);
        }

        /// <summary>
        /// Shows or hides the option checkboxes, which stay hidden until the GM UI is ready for them.
        /// </summary>
        /// <param name="isVisible">Whether the option checkboxes are shown.</param>
        public void SetOptionCheckBoxesVisible(bool isVisible)
        {
            Visibility visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            chkSticky.Visibility = visibility;
            chkPosOnIniSwap.Visibility = visibility;
            chkInvertLog.Visibility = visibility;
        }

        /// <summary>
        /// Sets the layout edit toggle state without raising <see cref="EditLayoutModeChanged"/>.
        /// </summary>
        /// <param name="isActive">Whether layout edit mode is active.</param>
        public void SetEditLayoutModeActive(bool isActive)
        {
            suppressCheckBoxEvents = true;
            try
            {
                btnEditLayout.IsChecked = isActive;
            }
            finally
            {
                suppressCheckBoxEvents = false;
            }
        }

        /// <summary>
        /// Shows or hides the debug button, which is developer-only.
        /// </summary>
        /// <param name="isVisible">Whether the debug button is shown.</param>
        public void SetDebugButtonVisible(bool isVisible)
        {
            btnDebug.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetCheckBoxSilently(CheckBox checkBox, bool isChecked)
        {
            suppressCheckBoxEvents = true;
            try
            {
                checkBox.IsChecked = isChecked;
            }
            finally
            {
                suppressCheckBoxEvents = false;
            }
        }

        private void RefreshCharactersButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshCharactersRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            DebugRequested?.Invoke(this, EventArgs.Empty);
        }

        private void AreaNavigatorButton_Click(object sender, RoutedEventArgs e)
        {
            AreaNavigatorRequested?.Invoke(this, EventArgs.Empty);
        }

        private void MusicListButton_Click(object sender, RoutedEventArgs e)
        {
            MusicListRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ViewportButton_Click(object sender, RoutedEventArgs e)
        {
            ViewportRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EditLayoutToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!suppressCheckBoxEvents)
            {
                EditLayoutModeChanged?.Invoke(this, btnEditLayout.IsChecked == true);
            }
        }

        private void StickyEffectsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!suppressCheckBoxEvents)
            {
                StickyEffectsChanged?.Invoke(this, chkSticky.IsChecked == true);
            }
        }

        private void SwitchPosCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!suppressCheckBoxEvents)
            {
                SwitchPositionOnIniSwapChanged?.Invoke(this, chkPosOnIniSwap.IsChecked == true);
            }
        }

        private void InvertLogCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!suppressCheckBoxEvents)
            {
                InvertIcLogChanged?.Invoke(this, chkInvertLog.IsChecked == true);
            }
        }
    }
}
