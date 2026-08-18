using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AOBot_Testing.Structures;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// The AO2 shout modifier row: Hold It, Objection, Take That and Custom.
    /// </summary>
    /// <remarks>
    /// Self-contained panel for the Oceanya theme system. The buttons behave as a radio group: checking
    /// one unchecks the others, and clicking the checked button again clears the selection. The host
    /// reads and writes <see cref="SelectedShoutModifier"/> instead of touching the buttons. See
    /// <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class ShoutRowPanel : UserControl
    {
        private readonly List<ToggleButton> shoutButtons;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShoutRowPanel"/> class.
        /// </summary>
        public ShoutRowPanel()
        {
            InitializeComponent();
            shoutButtons = new List<ToggleButton> { HoldIt, Objection, TakeThat, Custom };
        }

        /// <summary>
        /// Gets or sets the selected shout modifier.
        /// </summary>
        public ICMessage.ShoutModifiers SelectedShoutModifier
        {
            get
            {
                if (HoldIt.IsChecked == true)
                {
                    return ICMessage.ShoutModifiers.HoldIt;
                }

                if (Objection.IsChecked == true)
                {
                    return ICMessage.ShoutModifiers.Objection;
                }

                if (TakeThat.IsChecked == true)
                {
                    return ICMessage.ShoutModifiers.TakeThat;
                }

                if (Custom.IsChecked == true)
                {
                    return ICMessage.ShoutModifiers.Custom;
                }

                return ICMessage.ShoutModifiers.Nothing;
            }

            set
            {
                HoldIt.IsChecked = value == ICMessage.ShoutModifiers.HoldIt;
                Objection.IsChecked = value == ICMessage.ShoutModifiers.Objection;
                TakeThat.IsChecked = value == ICMessage.ShoutModifiers.TakeThat;
                Custom.IsChecked = value == ICMessage.ShoutModifiers.Custom;
            }
        }

        /// <summary>
        /// Clears the shout selection.
        /// </summary>
        public void ClearSelection()
        {
            SelectedShoutModifier = ICMessage.ShoutModifiers.Nothing;
        }

        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clickedButton)
            {
                return;
            }

            foreach (ToggleButton button in shoutButtons)
            {
                if (button != clickedButton)
                {
                    button.IsChecked = false; // Uncheck other buttons
                }
            }
        }
    }
}
