using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// The optional Dredd background overlay override row shown under the GM main window body.
    /// </summary>
    /// <remarks>
    /// Self-contained panel for the Oceanya theme system: it owns the row's visuals and raises events,
    /// while the host keeps the overlay logic and savefile handling. See
    /// <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class DreddFeatureRowPanel : UserControl
    {
        private bool suppressSelectionEvents;

        /// <summary>
        /// Initializes a new instance of the <see cref="DreddFeatureRowPanel"/> class.
        /// </summary>
        public DreddFeatureRowPanel()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user picks an overlay from the dropdown.</summary>
        public event EventHandler<DreddOverlaySelectionItem>? OverlaySelected;

        /// <summary>Raised when the sticky overlay checkbox is toggled.</summary>
        public event EventHandler<bool>? StickyOverlayChanged;

        /// <summary>Raised when the Config button is pressed.</summary>
        public event EventHandler? ConfigRequested;

        /// <summary>Raised when the View Changes button is pressed.</summary>
        public event EventHandler? ViewChangesRequested;

        /// <summary>
        /// Gets or sets a value indicating whether the sticky overlay checkbox is checked.
        /// Setting it does not raise <see cref="StickyOverlayChanged"/>.
        /// </summary>
        public bool IsStickyOverlayChecked
        {
            get => DreddStickyOverlayCheckBox.IsChecked == true;
            set
            {
                suppressSelectionEvents = true;
                try
                {
                    DreddStickyOverlayCheckBox.IsChecked = value;
                }
                finally
                {
                    suppressSelectionEvents = false;
                }
            }
        }

        /// <summary>
        /// Gets the overlay entry currently selected in the dropdown.
        /// </summary>
        public DreddOverlaySelectionItem? SelectedOverlay => DreddOverlayListBox.SelectedItem as DreddOverlaySelectionItem;

        /// <summary>
        /// Replaces the overlay list and the selected entry without raising selection events.
        /// </summary>
        /// <param name="overlays">Overlay entries to offer.</param>
        /// <param name="selectedOverlay">Entry to select, or null to leave the selection empty.</param>
        public void SetOverlays(IEnumerable<DreddOverlaySelectionItem> overlays, DreddOverlaySelectionItem? selectedOverlay)
        {
            suppressSelectionEvents = true;
            try
            {
                DreddOverlayListBox.ItemsSource = overlays;
                DreddOverlayListBox.DisplayMemberPath = nameof(DreddOverlaySelectionItem.DisplayText);
                DreddOverlayListBox.SelectedItem = selectedOverlay;
                DreddOverlaySelectedText.Text = selectedOverlay?.DisplayText ?? string.Empty;
            }
            finally
            {
                suppressSelectionEvents = false;
            }
        }

        /// <summary>
        /// Clears the overlay list and shows a placeholder in the selector.
        /// </summary>
        /// <param name="placeholderText">Text shown while no overlays are available.</param>
        public void ClearOverlays(string placeholderText)
        {
            suppressSelectionEvents = true;
            try
            {
                DreddOverlayListBox.ItemsSource = Array.Empty<DreddOverlaySelectionItem>();
                DreddOverlaySelectedText.Text = placeholderText;
            }
            finally
            {
                suppressSelectionEvents = false;
            }
        }

        /// <summary>
        /// Sets the selector text without touching the list selection.
        /// </summary>
        /// <param name="displayText">Text to show in the collapsed selector.</param>
        public void SetSelectedOverlayText(string displayText)
        {
            DreddOverlaySelectedText.Text = displayText;
        }

        /// <summary>
        /// Applies the enabled state of the row's controls.
        /// </summary>
        /// <param name="isEnabledForClient">Whether a client context exists to apply overlays to.</param>
        /// <param name="isFeatureEnabled">Whether the advanced feature itself is enabled.</param>
        public void SetInteractionEnabled(bool isEnabledForClient, bool isFeatureEnabled)
        {
            DreddOverlaySelector.IsEnabled = isEnabledForClient;
            DreddOverlayDropButton.IsEnabled = isEnabledForClient;
            DreddStickyOverlayCheckBox.IsEnabled = isEnabledForClient;
            DreddFeatureLabel.Opacity = isEnabledForClient ? 1.0 : 0.65;

            // Always keep these available for configuration/review.
            DreddOverlayConfigButton.IsEnabled = isFeatureEnabled;
            DreddViewChangesButton.IsEnabled = isFeatureEnabled;
        }

        /// <summary>
        /// Closes the overlay dropdown, if it is open.
        /// </summary>
        public void CloseOverlayDropdown()
        {
            DreddOverlayPopup.IsOpen = false;
        }

        private void DreddOverlayDropButton_Click(object sender, RoutedEventArgs e)
        {
            if (!DreddOverlaySelector.IsEnabled)
            {
                return;
            }

            DreddOverlayPopup.IsOpen = !DreddOverlayPopup.IsOpen;
        }

        private void DreddOverlayListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionEvents)
            {
                return;
            }

            if (SelectedOverlay is not DreddOverlaySelectionItem selectedOverlay)
            {
                return;
            }

            DreddOverlaySelectedText.Text = selectedOverlay.DisplayText;
            OverlaySelected?.Invoke(this, selectedOverlay);
            DreddOverlayPopup.IsOpen = false;
        }

        private void DreddStickyOverlayCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressSelectionEvents)
            {
                return;
            }

            StickyOverlayChanged?.Invoke(this, DreddStickyOverlayCheckBox.IsChecked == true);
        }

        private void DreddOverlayConfigButton_Click(object sender, RoutedEventArgs e)
        {
            ConfigRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DreddViewChangesButton_Click(object sender, RoutedEventArgs e)
        {
            ViewChangesRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
