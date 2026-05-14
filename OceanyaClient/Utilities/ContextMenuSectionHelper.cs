using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Shared formatting helper for Oceanya custom context menus.
    /// </summary>
    public static class ContextMenuSectionHelper
    {
        /// <summary>
        /// Adds the standard section title used by custom context menus.
        /// </summary>
        public static void AddHeader(ContextMenu menu, string text, bool addLeadingSeparator)
        {
            if (addLeadingSeparator && menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            TextBlock label = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(4, 2, 4, 1)
            };

            menu.Items.Add(new MenuItem
            {
                Header = label,
                IsEnabled = false,
                StaysOpenOnClick = true
            });
        }
    }
}
