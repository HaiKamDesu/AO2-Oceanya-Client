using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace OceanyaClient
{
    /// <summary>
    /// Reusable editable ComboBox autocomplete behavior:
    /// - Opens filtered dropdown while typing
    /// - Supports arrow navigation
    /// - Enter commits selected suggestion
    /// </summary>
    public static class AutoCompleteComboBoxBehavior
    {
        private static readonly TextChangedEventHandler TextChangedHandler = ComboBox_TextChanged;
        private static readonly KeyEventHandler EditableTextBoxPreviewKeyDownHandler = EditableTextBox_PreviewKeyDown;
        private static readonly Dictionary<ComboBox, ComboBoxAutoCompleteState> States =
            new Dictionary<ComboBox, ComboBoxAutoCompleteState>();

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(AutoCompleteComboBoxBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox comboBox)
            {
                return;
            }

            bool isEnabled = e.NewValue is bool flag && flag;
            if (isEnabled)
            {
                comboBox.Loaded += ComboBox_Loaded;
                comboBox.Unloaded += ComboBox_Unloaded;
                comboBox.PreviewKeyDown += ComboBox_PreviewKeyDown;
                comboBox.AddHandler(TextBoxBase.TextChangedEvent, TextChangedHandler);
                Initialize(comboBox);
            }
            else
            {
                Detach(comboBox);
            }
        }

        private static void ComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                Initialize(comboBox);
            }
        }

        private static void ComboBox_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                ResetFilter(comboBox);
            }
        }

        private static void Initialize(ComboBox comboBox)
        {
            comboBox.IsEditable = true;
            comboBox.IsTextSearchEnabled = false;
            comboBox.StaysOpenOnEdit = true;

            if (!States.TryGetValue(comboBox, out ComboBoxAutoCompleteState? state))
            {
                state = new ComboBoxAutoCompleteState();
                States[comboBox] = state;
            }

            object source = comboBox.ItemsSource ?? comboBox.Items;
            ICollectionView? view = CollectionViewSource.GetDefaultView(source);
            if (view == null)
            {
                return;
            }

            state.View = view;
            state.View.Filter = item => FilterItem(state, item);

            TextBox? textBox = FindEditableTextBox(comboBox);
            if (textBox != null && !ReferenceEquals(state.EditableTextBox, textBox))
            {
                if (state.EditableTextBox != null)
                {
                    state.EditableTextBox.PreviewKeyDown -= EditableTextBoxPreviewKeyDownHandler;
                }

                state.EditableTextBox = textBox;
                state.EditableTextBox.PreviewKeyDown += EditableTextBoxPreviewKeyDownHandler;
            }
        }

        private static bool FilterItem(ComboBoxAutoCompleteState state, object item)
        {
            if (item == null)
            {
                return false;
            }

            string searchText = state.SearchText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            string text = item.ToString() ?? string.Empty;
            return text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        private static void ComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox)
            {
                return;
            }

            if (!States.TryGetValue(comboBox, out ComboBoxAutoCompleteState? state))
            {
                return;
            }

            if (state.SuppressEvents)
            {
                return;
            }

            TextBox? textBox = FindEditableTextBox(comboBox);
            if (textBox == null || !ReferenceEquals(e.OriginalSource, textBox))
            {
                return;
            }

            state.SearchText = textBox.Text ?? string.Empty;
            state.View?.Refresh();

            bool hasText = !string.IsNullOrWhiteSpace(state.SearchText);
            bool hasAny = state.View?.Cast<object>().Any() == true;

            comboBox.IsDropDownOpen = hasText && hasAny;
            if (comboBox.IsDropDownOpen)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private static void ComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox comboBox || !States.TryGetValue(comboBox, out ComboBoxAutoCompleteState? state))
            {
                return;
            }

            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                if (!comboBox.IsDropDownOpen)
                {
                    comboBox.IsDropDownOpen = true;
                    if (comboBox.Items.Count > 0 && comboBox.SelectedIndex < 0)
                    {
                        comboBox.SelectedIndex = 0;
                    }
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Enter)
            {
                CommitSelection(comboBox, state);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                comboBox.IsDropDownOpen = false;
                e.Handled = true;
            }
        }

        private static void CommitSelection(ComboBox comboBox, ComboBoxAutoCompleteState state)
        {
            object? selected = comboBox.SelectedItem;
            if (selected == null && comboBox.Items.Count > 0)
            {
                selected = comboBox.Items[0];
            }

            string committed = selected?.ToString() ?? (comboBox.Text ?? string.Empty);
            TextBox? textBox = FindEditableTextBox(comboBox);
            state.SuppressEvents = true;
            comboBox.Text = committed;
            if (textBox != null)
            {
                textBox.Text = committed;
                textBox.SelectionStart = committed.Length;
                textBox.SelectionLength = 0;
            }
            comboBox.IsDropDownOpen = false;
            state.SearchText = committed;
            state.View?.Refresh();
            state.SuppressEvents = false;
        }

        private static void EditableTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ComboBox? comboBox = States
                .Where(pair => ReferenceEquals(pair.Value.EditableTextBox, textBox))
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (comboBox == null || !States.TryGetValue(comboBox, out ComboBoxAutoCompleteState? state))
            {
                return;
            }

            if (key == Key.Enter)
            {
                CommitSelection(comboBox, state);
                e.Handled = true;
                return;
            }

            if (key == Key.Down || key == Key.Up)
            {
                if (!comboBox.IsDropDownOpen)
                {
                    comboBox.IsDropDownOpen = true;
                    if (comboBox.Items.Count > 0 && comboBox.SelectedIndex < 0)
                    {
                        comboBox.SelectedIndex = 0;
                    }
                }

                if (comboBox.Items.Count == 0)
                {
                    e.Handled = true;
                    return;
                }

                int current = comboBox.SelectedIndex;
                int next = key == Key.Down
                    ? (current < 0 ? 0 : Math.Min(current + 1, comboBox.Items.Count - 1))
                    : (current < 0 ? comboBox.Items.Count - 1 : Math.Max(current - 1, 0));
                comboBox.SelectedIndex = next;
                e.Handled = true;
            }
        }

        private static void ResetFilter(ComboBox comboBox)
        {
            if (!States.TryGetValue(comboBox, out ComboBoxAutoCompleteState? state))
            {
                return;
            }

            state.SuppressEvents = true;
            state.SearchText = string.Empty;
            state.View?.Refresh();
            comboBox.IsDropDownOpen = false;
            state.SuppressEvents = false;
        }

        private static TextBox? FindEditableTextBox(ComboBox comboBox)
        {
            return comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;
        }

        private static void Detach(ComboBox comboBox)
        {
            ResetFilter(comboBox);
            comboBox.Loaded -= ComboBox_Loaded;
            comboBox.Unloaded -= ComboBox_Unloaded;
            comboBox.PreviewKeyDown -= ComboBox_PreviewKeyDown;
            comboBox.RemoveHandler(TextBoxBase.TextChangedEvent, TextChangedHandler);
            if (States.TryGetValue(comboBox, out ComboBoxAutoCompleteState? state) && state.EditableTextBox != null)
            {
                state.EditableTextBox.PreviewKeyDown -= EditableTextBoxPreviewKeyDownHandler;
            }
            States.Remove(comboBox);
        }

        private sealed class ComboBoxAutoCompleteState
        {
            public ICollectionView? View { get; set; }

            public string SearchText { get; set; } = string.Empty;

            public bool SuppressEvents { get; set; }

            public TextBox? EditableTextBox { get; set; }
        }
    }
}
