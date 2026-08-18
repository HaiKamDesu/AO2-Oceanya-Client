using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Small reusable settings popups for panel styling: one per kind of thing being styled.
    /// </summary>
    /// <remarks>
    /// Panel styling used to live as long lists of literal values in the right-click menu ("Font size
    /// 8", "Font size 10", ...), which does not scale and cannot express combinations. Each family of
    /// settings gets one generic dialog instead, reused by every panel of that kind.
    /// </remarks>
    public static class OceanyaPanelSettingsDialogs
    {
        /// <summary>
        /// Shows the font settings for a text-like panel.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <param name="panelName">Panel display name, shown in the title.</param>
        /// <param name="state">State mutated when the user accepts.</param>
        /// <returns>True when the user accepted the dialog.</returns>
        public static bool ShowFontSettings(Window? owner, string panelName, OceanyaPanelPlacementState state)
        {
            ComboBox familyBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            familyBox.Items.Add("(default)");
            foreach (string family in Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase))
            {
                familyBox.Items.Add(family);
            }

            familyBox.SelectedItem = string.IsNullOrWhiteSpace(state.FontFamily) ? "(default)" : state.FontFamily;
            if (familyBox.SelectedItem == null)
            {
                familyBox.Items.Add(state.FontFamily);
                familyBox.SelectedItem = state.FontFamily;
            }

            TextBox sizeBox = new TextBox
            {
                Text = state.FontSize > 0 ? state.FontSize.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty,
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Leave empty to use the control's own size. The panel height follows this size."
            };

            CheckBox boldBox = new CheckBox
            {
                Content = "Bold",
                IsChecked = state.IsBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel body = new StackPanel();
            body.Children.Add(CreateLabel("Font family"));
            body.Children.Add(familyBox);
            body.Children.Add(CreateLabel("Font size (px)"));
            body.Children.Add(sizeBox);
            body.Children.Add(boldBox);

            if (!ShowDialog(owner, $"Font settings - {panelName}", body, 340, 260))
            {
                return false;
            }

            string selectedFamily = familyBox.SelectedItem as string ?? "(default)";
            state.FontFamily = string.Equals(selectedFamily, "(default)", StringComparison.Ordinal) ? string.Empty : selectedFamily;
            state.FontSize = double.TryParse(sizeBox.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedSize)
                && parsedSize > 0
                    ? parsedSize
                    : 0;
            state.IsBold = boldBox.IsChecked == true;
            return true;
        }

        /// <summary>
        /// Shows the image settings for an image-button panel.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <param name="panelName">Panel display name, shown in the title.</param>
        /// <param name="state">State mutated when the user accepts.</param>
        /// <returns>True when the user accepted the dialog.</returns>
        public static bool ShowImageSettings(Window? owner, string panelName, OceanyaPanelPlacementState state)
        {
            TextBox pathBox = new TextBox
            {
                Text = state.ImagePath,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Leave empty to keep the built-in artwork."
            };

            Button browse = new Button
            {
                Content = "Browse...",
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            browse.Click += (_, _) =>
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick the button image",
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
                };
                if (dialog.ShowDialog() == true)
                {
                    pathBox.Text = dialog.FileName;
                }
            };

            ComboBox scalingBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (string option in new[] { "(default)", "Fill", "Uniform", "UniformToFill", "None" })
            {
                scalingBox.Items.Add(option);
            }

            scalingBox.SelectedItem = string.IsNullOrWhiteSpace(state.ImageScaling) ? "(default)" : state.ImageScaling;

            StackPanel body = new StackPanel();
            body.Children.Add(CreateLabel("Image file"));
            body.Children.Add(pathBox);
            body.Children.Add(browse);
            body.Children.Add(CreateLabel("Scaling"));
            body.Children.Add(scalingBox);

            if (!ShowDialog(owner, $"Image settings - {panelName}", body, 420, 260))
            {
                return false;
            }

            state.ImagePath = pathBox.Text?.Trim() ?? string.Empty;
            string selectedScaling = scalingBox.SelectedItem as string ?? "(default)";
            state.ImageScaling = string.Equals(selectedScaling, "(default)", StringComparison.Ordinal) ? string.Empty : selectedScaling;
            return true;
        }

        /// <summary>
        /// Shows the grid settings for an item-grid panel.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <param name="panelName">Panel display name, shown in the title.</param>
        /// <param name="state">State mutated when the user accepts.</param>
        /// <returns>True when the user accepted the dialog.</returns>
        public static bool ShowGridSettings(Window? owner, string panelName, OceanyaPanelPlacementState state)
        {
            TextBox itemSizeBox = new TextBox
            {
                Text = state.ItemSize > 0 ? state.ItemSize.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty,
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Size of one item including spacing. Leave empty to restore the default."
            };

            StackPanel body = new StackPanel();
            body.Children.Add(CreateLabel("Item size (px)"));
            body.Children.Add(itemSizeBox);
            body.Children.Add(new TextBlock
            {
                Text = "How many items fit is derived from this size and the panel's size.",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });

            if (!ShowDialog(owner, $"Grid settings - {panelName}", body, 340, 220))
            {
                return false;
            }

            state.ItemSize = double.TryParse(itemSizeBox.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                && parsed > 0
                    ? parsed
                    : 0;
            return true;
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            };
        }

        /// <summary>
        /// Hosts a settings body in the shared Oceanya shell with OK/Cancel buttons.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="body">Settings content.</param>
        /// <param name="width">Requested width.</param>
        /// <param name="height">Requested height.</param>
        /// <returns>True when the user accepted.</returns>
        private static bool ShowDialog(Window? owner, string title, FrameworkElement body, double width, double height)
        {
            Grid root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer scroll = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 0);
            root.Children.Add(scroll);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            bool accepted = false;
            GenericOceanyaWindow dialog = new GenericOceanyaWindow
            {
                Owner = owner,
                Title = title,
                HeaderText = title,
                Width = width,
                Height = height,
                MinWidth = Math.Min(width, 300),
                MinHeight = Math.Min(height, 200),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = true,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyContent = root
            };

            Button apply = new Button { Content = "Apply", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0) };
            apply.Click += (_, _) =>
            {
                accepted = true;
                dialog.Close();
            };
            Button cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 3, 12, 3) };
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);

            dialog.ShowDialog();
            return accepted;
        }
    }
}
