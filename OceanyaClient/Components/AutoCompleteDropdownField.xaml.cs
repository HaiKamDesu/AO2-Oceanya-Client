using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Common;

namespace OceanyaClient
{
    /// <summary>
    /// Reusable searchable dropdown field with keyboard navigation and explicit open-list arrow.
    /// </summary>
    public partial class AutoCompleteDropdownField : UserControl
    {
        private bool suppressTextHandlers;
        private bool navigationSelectionActive;
        private bool forceOpenAll;
        private bool isHovering;
        private bool isFocused;
        private static readonly object TraceLock = new object();
        private static readonly string DropdownTracePath = Path.Combine(Path.GetTempPath(), "oceanya_dropdown_trace.log");
        private static readonly bool EnableDropdownTrace = true;

        public event EventHandler? TextValueChanged;

        public ObservableCollection<string> Suggestions { get; } = new ObservableCollection<string>();

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(AutoCompleteDropdownField),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<string>),
            typeof(AutoCompleteDropdownField),
            new PropertyMetadata(null, OnItemsSourcePropertyChanged));

        public static readonly DependencyProperty SymbolGlyphProperty = DependencyProperty.Register(
            nameof(SymbolGlyph),
            typeof(string),
            typeof(AutoCompleteDropdownField),
            new PropertyMetadata(string.Empty, OnSymbolGlyphPropertyChanged));

        public static readonly DependencyProperty ShowItemSymbolProperty = DependencyProperty.Register(
            nameof(ShowItemSymbol),
            typeof(bool),
            typeof(AutoCompleteDropdownField),
            new PropertyMetadata(false));

        public static readonly DependencyProperty ItemSymbolGlyphProperty = DependencyProperty.Register(
            nameof(ItemSymbolGlyph),
            typeof(string),
            typeof(AutoCompleteDropdownField),
            new PropertyMetadata("\uE8EC"));
        public static readonly DependencyProperty IsTextReadOnlyProperty = DependencyProperty.Register(
            nameof(IsTextReadOnly),
            typeof(bool),
            typeof(AutoCompleteDropdownField),
            new PropertyMetadata(false, OnIsTextReadOnlyPropertyChanged));

        public AutoCompleteDropdownField()
        {
            InitializeComponent();
            Loaded += AutoCompleteDropdownField_Loaded;
            SuggestionsPopup.Closed += SuggestionsPopup_Closed;
            SuggestionsPopup.Opened += SuggestionsPopup_Opened;
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public IEnumerable<string> ItemsSource
        {
            get => (IEnumerable<string>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public string SymbolGlyph
        {
            get => (string)GetValue(SymbolGlyphProperty);
            set => SetValue(SymbolGlyphProperty, value);
        }

        public bool ShowItemSymbol
        {
            get => (bool)GetValue(ShowItemSymbolProperty);
            set => SetValue(ShowItemSymbolProperty, value);
        }

        public string ItemSymbolGlyph
        {
            get => (string)GetValue(ItemSymbolGlyphProperty);
            set => SetValue(ItemSymbolGlyphProperty, value);
        }

        public bool IsTextReadOnly
        {
            get => (bool)GetValue(IsTextReadOnlyProperty);
            set => SetValue(IsTextReadOnlyProperty, value);
        }

        private void AutoCompleteDropdownField_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySymbolVisibility();
            suppressTextHandlers = true;
            InputTextBox.Text = Text ?? string.Empty;
            suppressTextHandlers = false;
            UpdateVisualState();
            ApplyReadOnlyTextAreaState();
            Trace("Loaded");
        }

        private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AutoCompleteDropdownField field)
            {
                return;
            }

            string newValue = e.NewValue as string ?? string.Empty;
            if (string.Equals(field.InputTextBox.Text, newValue, StringComparison.Ordinal))
            {
                return;
            }

            field.suppressTextHandlers = true;
            field.InputTextBox.Text = newValue;
            field.suppressTextHandlers = false;
        }

        private static void OnItemsSourcePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AutoCompleteDropdownField field)
            {
                return;
            }

            field.RefreshSuggestions(field.InputTextBox.Text ?? string.Empty);
        }

        private static void OnSymbolGlyphPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AutoCompleteDropdownField field)
            {
                field.ApplySymbolVisibility();
            }
        }

        private static void OnIsTextReadOnlyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AutoCompleteDropdownField field)
            {
                field.ApplyReadOnlyTextAreaState();
            }
        }

        private void ApplySymbolVisibility()
        {
            bool hasSymbol = !string.IsNullOrWhiteSpace(SymbolGlyph);
            SymbolTextBlock.Visibility = hasSymbol ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyReadOnlyTextAreaState()
        {
            bool isReadOnly = IsTextReadOnly;
            ReadOnlyTextAreaToggleButton.Visibility = isReadOnly ? Visibility.Visible : Visibility.Collapsed;
            InputTextBox.Cursor = isReadOnly ? Cursors.Arrow : Cursors.IBeam;
        }

        private void RootGrid_MouseEnter(object sender, MouseEventArgs e)
        {
            isHovering = true;
            UpdateVisualState();
        }

        private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            isHovering = false;
            UpdateVisualState();
        }

        private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            isFocused = true;
            UpdateVisualState();
        }

        private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            isFocused = false;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            if (isFocused)
            {
                InputBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A"));
                InputBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6E93BB"));
                ArrowPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFECECEC"));
                return;
            }

            if (isHovering)
            {
                InputBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2A2A2A"));
                InputBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5A5A5A"));
                ArrowPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE4E4E4"));
                return;
            }

            InputBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF262626"));
            InputBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF444444"));
            ArrowPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD0D0D0"));
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Trace("InputTextBox_TextChanged");
            if (suppressTextHandlers)
            {
                return;
            }

            navigationSelectionActive = false;
            Text = InputTextBox.Text ?? string.Empty;
            RefreshSuggestions(Text);
            TextValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SuggestionsListBox.SelectedIndex = -1;
                navigationSelectionActive = false;
                SuggestionsPopup.IsOpen = false;
                return;
            }

            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                e.Handled = true;
                NavigateSuggestions(moveDown: e.Key == Key.Down);
                return;
            }

            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CommitFromInput();
        }

        private void ToggleSurface_Click(object sender, RoutedEventArgs e)
        {
            Trace("Toggle_Click_Before");

            if (SuggestionsPopup.IsOpen)
            {
                SuggestionsPopup.IsOpen = false;
                SuggestionsListBox.SelectedIndex = -1;
                navigationSelectionActive = false;
                e.Handled = true;
                Trace("Toggle_Click_Close");
                return;
            }

            if (!IsTextReadOnly)
            {
                InputTextBox.Focus();
            }
            forceOpenAll = true;
            RefreshSuggestions(string.Empty);
            e.Handled = true;
            Trace("Toggle_Click_Open");
        }

        private void SuggestionsPopup_Closed(object? sender, EventArgs e)
        {
            SuggestionsListBox.SelectedIndex = -1;
            navigationSelectionActive = false;
            Trace("Popup_Closed");
        }

        private void SuggestionsPopup_Opened(object? sender, EventArgs e)
        {
            Trace("Popup_Opened");
        }

        private void SuggestionItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not string token)
            {
                Trace("SuggestionItem_Down_NoToken");
                return;
            }

            Trace("SuggestionItem_Down_Commit", token);
            CommitToken(token);
            e.Handled = true;
        }

        private void SuggestionsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Trace("List_Down_Before");
            if (sender is not ListBox listBox)
            {
                return;
            }

            DependencyObject? source = e.OriginalSource as DependencyObject;
            if (source == null)
            {
                return;
            }

            ListBoxItem? item = ItemsControl.ContainerFromElement(listBox, source) as ListBoxItem;
            if (item?.DataContext is string token && !string.IsNullOrWhiteSpace(token))
            {
                Trace("List_Down_Commit", token);
                CommitToken(token);
                e.Handled = true;
            }
        }

        private void SuggestionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Trace("List_SelectionChanged");
            if (sender is not ListBox listBox)
            {
                return;
            }

            if (listBox.SelectedItem is string token && !string.IsNullOrWhiteSpace(token))
            {
                Trace("List_SelectionChanged_Commit", token);
                CommitToken(token);
            }
        }

        private void RefreshSuggestions(string input)
        {
            IEnumerable<string> source = GetOrderedSource();
            string query = forceOpenAll ? string.Empty : (input ?? string.Empty).Trim();

            IEnumerable<string> matches = source
                .Where(option => string.IsNullOrWhiteSpace(query)
                    || option.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(60);

            Suggestions.Clear();
            foreach (string match in matches)
            {
                Suggestions.Add(match);
            }

            bool shouldAutoOpen = !IsTextReadOnly && isFocused && !string.IsNullOrWhiteSpace(query);
            bool open = Suggestions.Count > 0 && (forceOpenAll || shouldAutoOpen);
            SuggestionsPopup.IsOpen = open;
            if (!open)
            {
                SuggestionsListBox.SelectedIndex = -1;
            }

            forceOpenAll = false;
        }

        private IEnumerable<string> GetOrderedSource()
        {
            IEnumerable<string> source = ItemsSource ?? Enumerable.Empty<string>();
            return source
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);
        }

        private void NavigateSuggestions(bool moveDown)
        {
            if (Suggestions.Count == 0)
            {
                navigationSelectionActive = false;
                SuggestionsPopup.IsOpen = false;
                return;
            }

            SuggestionsPopup.IsOpen = true;
            int currentIndex = SuggestionsListBox.SelectedIndex;
            int nextIndex = moveDown
                ? (currentIndex < 0 ? 0 : Math.Min(currentIndex + 1, Suggestions.Count - 1))
                : (currentIndex < 0 ? Suggestions.Count - 1 : Math.Max(currentIndex - 1, 0));
            SuggestionsListBox.SelectedIndex = nextIndex;
            SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
            navigationSelectionActive = nextIndex >= 0;
        }

        private void CommitFromInput()
        {
            string? selectedSuggestion = SuggestionsListBox.SelectedItem as string;
            string candidate = ResolveInputCandidate(
                InputTextBox.Text ?? string.Empty,
                Suggestions,
                navigationSelectionActive ? selectedSuggestion : null);

            navigationSelectionActive = false;
            SuggestionsPopup.IsOpen = false;
            SuggestionsListBox.SelectedIndex = -1;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            CommitToken(candidate);
        }

        private string ResolveInputCandidate(string rawInput, IEnumerable<string> suggestions, string? selectedSuggestion)
        {
            if (!string.IsNullOrWhiteSpace(selectedSuggestion))
            {
                return selectedSuggestion;
            }

            string normalized = (rawInput ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string? exactFromSuggestions = suggestions.FirstOrDefault(item =>
                string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactFromSuggestions))
            {
                return exactFromSuggestions;
            }

            string? exactFromAll = GetOrderedSource().FirstOrDefault(item =>
                string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactFromAll))
            {
                return exactFromAll;
            }

            string? startsWith = suggestions.FirstOrDefault(item =>
                item.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
            return startsWith ?? normalized;
        }

        private void CommitToken(string value)
        {
            Trace("CommitToken", value);
            suppressTextHandlers = true;
            InputTextBox.Text = value;
            suppressTextHandlers = false;
            Text = value;
            SuggestionsPopup.IsOpen = false;
            SuggestionsListBox.SelectedIndex = -1;
            TextValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Trace(string evt, string extra = "")
        {
            if (!EnableDropdownTrace)
            {
                return;
            }

            string id = !string.IsNullOrWhiteSpace(Name) ? Name : ("#" + GetHashCode().ToString());
            string line = $"[DropdownTrace] {DateTime.Now:HH:mm:ss.fff} {id} {evt} " +
                $"PopupOpen={SuggestionsPopup.IsOpen} " +
                $"ReadOnly={IsTextReadOnly} " +
                $"Focused={isFocused} " +
                $"Mouse={Mouse.LeftButton} " +
                $"Items={Suggestions.Count} Sel={SuggestionsListBox.SelectedIndex} " +
                $"Text='{Text}' " +
                (string.IsNullOrWhiteSpace(extra) ? string.Empty : $"Extra='{extra}'");

            CustomConsole.WriteLine(line);
            lock (TraceLock)
            {
                File.AppendAllText(DropdownTracePath, line + Environment.NewLine);
            }
        }
    }
}
