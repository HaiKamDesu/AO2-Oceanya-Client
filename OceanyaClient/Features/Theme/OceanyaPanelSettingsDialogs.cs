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
            ComboBox familyBox = StyledComboBox();
            familyBox.Items.Add(DefaultOption);
            foreach (string family in Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase))
            {
                familyBox.Items.Add(family);
            }

            familyBox.SelectedItem = string.IsNullOrWhiteSpace(state.FontFamily) ? DefaultOption : state.FontFamily;
            if (familyBox.SelectedItem == null)
            {
                familyBox.Items.Add(state.FontFamily);
                familyBox.SelectedItem = state.FontFamily;
            }

            TextBox sizeBox = StyledTextBox(
                state.FontSize > 0 ? state.FontSize.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty,
                "Empty uses the control's own size. The panel height follows this size.");

            CheckBox boldBox = StyledCheckBox("Bold", state.IsBold);
            CheckBox italicBox = StyledCheckBox("Italic", state.IsItalic);
            CheckBox underlineBox = StyledCheckBox("Underline", state.IsUnderlined);
            StackPanel styleRow = new StackPanel { Orientation = Orientation.Horizontal };
            styleRow.Children.Add(boldBox);
            styleRow.Children.Add(italicBox);
            styleRow.Children.Add(underlineBox);

            ColorPickerRow colorRow = CreateColorPickerRow(owner, state.TextColor, "Text colour");

            Grid body = CreateFieldGrid();
            AddField(body, "Font", familyBox);
            AddField(body, "Size (px)", sizeBox);
            AddField(body, "Style", styleRow);
            AddField(body, "Colour", colorRow.Content);

            if (!ShowDialog(owner, $"Font settings - {panelName}", body, 400))
            {
                return false;
            }

            string selectedFamily = familyBox.SelectedItem as string ?? DefaultOption;
            state.FontFamily = string.Equals(selectedFamily, DefaultOption, StringComparison.Ordinal) ? string.Empty : selectedFamily;
            state.FontSize = ParsePositiveDouble(sizeBox.Text);
            state.IsBold = boldBox.IsChecked == true;
            state.IsItalic = italicBox.IsChecked == true;
            state.IsUnderlined = underlineBox.IsChecked == true;
            state.TextColor = colorRow.SelectedColor;
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
            TextBox pathBox = StyledTextBox(state.ImagePath, "Empty keeps the built-in artwork.");
            pathBox.Margin = new Thickness(0);
            Button browse = StyledButton("Browse...");
            browse.Margin = new Thickness(6, 0, 0, 0);
            browse.MinWidth = 86;
            browse.Click += (_, _) =>
            {
                Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick the panel image",
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
                };
                if (dialog.ShowDialog() == true)
                {
                    pathBox.Text = dialog.FileName;
                }
            };

            Grid imageRow = new Grid();
            imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(pathBox, 0);
            Grid.SetColumn(browse, 1);
            imageRow.Children.Add(pathBox);
            imageRow.Children.Add(browse);

            ComboBox scalingBox = StyledComboBox();
            foreach (string option in new[] { DefaultOption, "Fill", "Uniform", "UniformToFill", "None" })
            {
                scalingBox.Items.Add(option);
            }

            scalingBox.SelectedItem = string.IsNullOrWhiteSpace(state.ImageScaling) ? DefaultOption : state.ImageScaling;

            (StackPanel opacityRow, Slider opacitySlider) = CreateOpacityRow(state.Opacity);
            ColorPickerRow backgroundRow = CreateColorPickerRow(owner, state.BackgroundColor, "Background");

            Grid body = CreateFieldGrid();
            AddField(body, "Image", imageRow);
            AddField(body, "Scaling", scalingBox);
            AddField(body, "Opacity", opacityRow);
            AddField(body, "Background", backgroundRow.Content);

            if (!ShowDialog(owner, $"Image settings - {panelName}", body, 470))
            {
                return false;
            }

            state.ImagePath = pathBox.Text?.Trim() ?? string.Empty;
            string selectedScaling = scalingBox.SelectedItem as string ?? DefaultOption;
            state.ImageScaling = string.Equals(selectedScaling, DefaultOption, StringComparison.Ordinal) ? string.Empty : selectedScaling;
            state.Opacity = Math.Round(opacitySlider.Value / 100d, 2);
            state.BackgroundColor = backgroundRow.SelectedColor;
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
            TextBox itemSizeBox = StyledTextBox(
                state.ItemSize > 0 ? state.ItemSize.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty,
                "Size of one item including spacing. Empty restores the default.");

            Grid body = CreateFieldGrid();
            AddField(body, "Item size (px)", itemSizeBox);
            AddHint(body, "How many items fit is derived from this size and the panel's size.");

            if (!ShowDialog(owner, $"Grid settings - {panelName}", body, 380))
            {
                return false;
            }

            state.ItemSize = ParsePositiveDouble(itemSizeBox.Text);
            return true;
        }

        /// <summary>Option text meaning "leave the control's own value alone".</summary>
        private const string DefaultOption = "(default)";

        /// <summary>A colour field: the row of controls plus the colour currently chosen.</summary>
        private sealed class ColorPickerRow
        {
            public required FrameworkElement Content { get; init; }

            public required Func<string> Resolve { get; init; }

            public string SelectedColor => Resolve();
        }

        /// <summary>
        /// Builds the two-column label/control grid every settings dialog uses.
        /// </summary>
        /// <returns>An empty field grid.</returns>
        private static Grid CreateFieldGrid()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        /// <summary>
        /// Adds a labelled row to a field grid.
        /// </summary>
        /// <param name="grid">Grid being filled.</param>
        /// <param name="label">Row label.</param>
        /// <param name="field">Control for the row.</param>
        private static void AddField(Grid grid, string label, FrameworkElement field)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock caption = new TextBlock
            {
                Text = label,
                Style = FindDialogStyle("OceanyaDialogLabel"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 10)
            };
            Grid.SetRow(caption, row);
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            field.Margin = new Thickness(0, 0, 0, 10);
            field.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }

        /// <summary>
        /// Adds a full-width hint line to a field grid.
        /// </summary>
        /// <param name="grid">Grid being filled.</param>
        /// <param name="text">Hint text.</param>
        private static void AddHint(Grid grid, string text)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock hint = new TextBlock { Text = text, Style = FindDialogStyle("OceanyaDialogHint") };
            Grid.SetRow(hint, row);
            Grid.SetColumn(hint, 1);
            grid.Children.Add(hint);
        }

        /// <summary>
        /// Builds a colour field: a swatch, a Pick button and a Clear button.
        /// </summary>
        /// <param name="owner">Owner window for the colour picker.</param>
        /// <param name="initialColor">Currently stored colour, or empty.</param>
        /// <param name="pickerTitle">Title used when picking.</param>
        /// <returns>The colour field.</returns>
        private static ColorPickerRow CreateColorPickerRow(Window? owner, string initialColor, string pickerTitle)
        {
            string current = initialColor ?? string.Empty;
            Border swatch = new Border
            {
                Width = 34,
                Height = 22,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Background = OceanyaPanelStyleApplier.TryParseBrush(current) ?? Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock valueText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(current) ? DefaultOption : current,
                Style = FindDialogStyle("OceanyaDialogHint"),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            Button pick = StyledButton("Pick...");
            pick.MinWidth = 72;
            pick.Click += (_, _) =>
            {
                Color seed = (OceanyaPanelStyleApplier.TryParseBrush(current) as SolidColorBrush)?.Color ?? Colors.White;
                Color? picked = AOCharacterFileCreatorWindow.ShowSolidColorPickerDialog(owner, seed);
                if (picked == null)
                {
                    return;
                }

                current = picked.Value.ToString();
                swatch.Background = new SolidColorBrush(picked.Value);
                valueText.Text = current;
            };

            Button clear = StyledButton("Clear");
            clear.MinWidth = 64;
            clear.Margin = new Thickness(0);
            clear.ToolTip = pickerTitle + ": use the control's own colour";
            clear.Click += (_, _) =>
            {
                current = string.Empty;
                swatch.Background = Brushes.Transparent;
                valueText.Text = DefaultOption;
            };

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(valueText);
            row.Children.Add(pick);
            row.Children.Add(clear);

            return new ColorPickerRow { Content = row, Resolve = () => current };
        }

        /// <summary>
        /// Builds an opacity field: a slider plus a live percentage readout.
        /// </summary>
        /// <param name="storedOpacity">Stored opacity, 0 to 1; zero means untouched.</param>
        /// <returns>The row and its slider.</returns>
        private static (StackPanel Row, Slider Slider) CreateOpacityRow(double storedOpacity)
        {
            Slider slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Width = 190,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Value = storedOpacity > 0 ? storedOpacity * 100 : 100,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock readout = new TextBlock
            {
                Style = FindDialogStyle("OceanyaDialogHint"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"{slider.Value:0}%"
            };
            slider.ValueChanged += (_, _) => readout.Text = $"{slider.Value:0}%";

            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(slider);
            row.Children.Add(readout);
            return (row, slider);
        }

        private static double ParsePositiveDouble(string? text)
        {
            return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0
                ? parsed
                : 0;
        }

        private static TextBox StyledTextBox(string text, string toolTip)
        {
            return new TextBox
            {
                Text = text,
                ToolTip = toolTip,
                Style = FindDialogStyle("DarkTextBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private static ComboBox StyledComboBox()
        {
            return new ComboBox
            {
                Style = FindDialogStyle("DarkComboBox"),
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private static CheckBox StyledCheckBox(string content, bool isChecked)
        {
            return new CheckBox
            {
                Content = content,
                IsChecked = isChecked,
                Style = FindDialogStyle("OceanyaDialogCheckBox")
            };
        }

        private static Button StyledButton(string caption)
        {
            return new Button
            {
                Content = caption,
                Style = FindDialogStyle("ModernButton"),
                MinWidth = 92,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }

        /// <summary>
        /// Resolves one of the shared dialog styles from the application resources.
        /// </summary>
        /// <param name="key">Style resource key.</param>
        /// <returns>The style, or null when the application resources are unavailable (tests).</returns>
        private static Style? FindDialogStyle(string key)
        {
            return Application.Current?.TryFindResource(key) as Style;
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
        private static bool ShowDialog(Window? owner, string title, FrameworkElement body, double width)
        {
            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(body, 0);
            root.Children.Add(body);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
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
                // The shell sizes itself to the content, so the dialog has no dead space at the bottom.
                SizeToContent = SizeToContent.Height,
                MinWidth = Math.Min(width, 300),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = false,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyContent = root
            };

            Button apply = StyledButton("Apply");
            apply.Click += (_, _) =>
            {
                accepted = true;
                dialog.Close();
            };
            Button cancel = StyledButton("Cancel");
            cancel.Margin = new Thickness(0);
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);

            dialog.ShowDialog();
            return accepted;
        }
    }
}
