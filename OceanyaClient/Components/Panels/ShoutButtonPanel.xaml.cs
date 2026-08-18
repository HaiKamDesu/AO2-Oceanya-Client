using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AOBot_Testing.Structures;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// A single AO2 shout button (Hold It, Objection, Take That or Custom) as its own placeable panel.
    /// </summary>
    /// <remarks>
    /// Each shout is separately positionable, resizable and re-skinnable, so they are individual panels
    /// rather than one grouped row. <see cref="ShoutSelectionGroup"/> keeps only one checked at a time.
    /// See <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class ShoutButtonPanel : UserControl
    {
        /// <summary>Image shown while the shout is not selected.</summary>
        public static readonly DependencyProperty UncheckedImageProperty = DependencyProperty.Register(
            nameof(UncheckedImage),
            typeof(ImageSource),
            typeof(ShoutButtonPanel),
            new PropertyMetadata(null));

        /// <summary>Image shown while the shout is selected.</summary>
        public static readonly DependencyProperty CheckedImageProperty = DependencyProperty.Register(
            nameof(CheckedImage),
            typeof(ImageSource),
            typeof(ShoutButtonPanel),
            new PropertyMetadata(null));

        private bool suppressChangeEvents;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShoutButtonPanel"/> class.
        /// </summary>
        public ShoutButtonPanel()
        {
            InitializeComponent();
        }

        /// <summary>Raised when this shout is checked or unchecked by the user.</summary>
        public event EventHandler<bool>? CheckedChanged;

        /// <summary>Gets or sets the shout modifier this button represents.</summary>
        public ICMessage.ShoutModifiers ShoutModifier { get; set; } = ICMessage.ShoutModifiers.Nothing;

        /// <summary>Gets or sets the image shown while unselected.</summary>
        public ImageSource? UncheckedImage
        {
            get => (ImageSource?)GetValue(UncheckedImageProperty);
            set => SetValue(UncheckedImageProperty, value);
        }

        /// <summary>Gets or sets the image shown while selected.</summary>
        public ImageSource? CheckedImage
        {
            get => (ImageSource?)GetValue(CheckedImageProperty);
            set => SetValue(CheckedImageProperty, value);
        }

        /// <summary>
        /// Gets or sets whether this shout is selected. Setting it does not raise
        /// <see cref="CheckedChanged"/>.
        /// </summary>
        public bool IsShoutChecked
        {
            get => ShoutToggle.IsChecked == true;
            set
            {
                suppressChangeEvents = true;
                try
                {
                    ShoutToggle.IsChecked = value;
                }
                finally
                {
                    suppressChangeEvents = false;
                }
            }
        }

        /// <summary>
        /// Applies the automation id and name used by UI automation for this shout.
        /// </summary>
        /// <param name="automationId">Automation id to expose.</param>
        /// <param name="automationName">Automation name to expose.</param>
        public void SetAutomationIdentity(string automationId, string automationName)
        {
            AutomationProperties.SetAutomationId(ShoutToggle, automationId);
            AutomationProperties.SetName(ShoutToggle, automationName);
        }

        private void ShoutToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!suppressChangeEvents)
            {
                CheckedChanged?.Invoke(this, ShoutToggle.IsChecked == true);
            }
        }
    }
}
