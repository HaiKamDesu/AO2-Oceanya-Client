using System.Collections.Generic;
using System.Linq;
using AOBot_Testing.Structures;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// Keeps a set of independently placed <see cref="ShoutButtonPanel"/>s behaving as one radio group.
    /// </summary>
    /// <remarks>
    /// The shout buttons used to be siblings inside a single row control, which made "only one checked"
    /// a layout detail. They are separate panels now, so the grouping lives here instead.
    /// </remarks>
    public sealed class ShoutSelectionGroup
    {
        private readonly IReadOnlyList<ShoutButtonPanel> buttons;
        private bool isSynchronizing;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShoutSelectionGroup"/> class.
        /// </summary>
        /// <param name="buttons">Shout buttons that belong to the group.</param>
        public ShoutSelectionGroup(params ShoutButtonPanel[] buttons)
        {
            this.buttons = buttons;
            foreach (ShoutButtonPanel button in buttons)
            {
                button.CheckedChanged += (sender, isChecked) => OnButtonCheckedChanged(sender as ShoutButtonPanel, isChecked);
            }
        }

        /// <summary>
        /// Gets or sets the selected shout modifier.
        /// </summary>
        public ICMessage.ShoutModifiers SelectedShoutModifier
        {
            get
            {
                ShoutButtonPanel? checkedButton = buttons.FirstOrDefault(button => button.IsShoutChecked);
                return checkedButton?.ShoutModifier ?? ICMessage.ShoutModifiers.Nothing;
            }

            set
            {
                foreach (ShoutButtonPanel button in buttons)
                {
                    button.IsShoutChecked = button.ShoutModifier == value;
                }
            }
        }

        /// <summary>
        /// Clears the shout selection.
        /// </summary>
        public void ClearSelection()
        {
            SelectedShoutModifier = ICMessage.ShoutModifiers.Nothing;
        }

        private void OnButtonCheckedChanged(ShoutButtonPanel? changedButton, bool isChecked)
        {
            if (isSynchronizing || changedButton == null || !isChecked)
            {
                return;
            }

            isSynchronizing = true;
            try
            {
                foreach (ShoutButtonPanel button in buttons)
                {
                    if (!ReferenceEquals(button, changedButton))
                    {
                        button.IsShoutChecked = false;
                    }
                }
            }
            finally
            {
                isSynchronizing = false;
            }
        }
    }
}
