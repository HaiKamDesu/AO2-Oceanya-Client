using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OceanyaClient.Components
{
    public partial class ImageComboBox : UserControl
    {
        #region Nested Types
        public class DropdownItem
        {
            public string Name { get; set; } = string.Empty;
            public string ImagePath { get; set; } = string.Empty;
        }
        #endregion

        #region Constants
        private const int MaxVisibleItems = 20000;
        #endregion

        #region Fields
        private ObservableCollection<DropdownItem> allItems = new();
        private TextBox? editableTextBox;
        public event EventHandler<string>? OnConfirm;
        private bool isReadOnly = false;
        private bool isInternalUpdate = false;
        private string lastConfirmedText = string.Empty;
        #endregion

        #region Properties
        public string SelectedText
        {
            get => cboINISelect.Text;
            set
            {
                if (editableTextBox == null)
                {
                    cboINISelect.Text = value;
                    ConfirmSelection(value);
                    return;
                }

                var prevFocusable = editableTextBox.Focusable;
                editableTextBox.Focusable = false;
                cboINISelect.Text = value;
                ConfirmSelection(value);
                editableTextBox.Focusable = prevFocusable;
            }
        }
        #endregion

        #region Constructor
        public ImageComboBox()
        {
            InitializeComponent();

            cboINISelect.Loaded += (s, e) =>
            {
                editableTextBox = cboINISelect.Template.FindName("PART_EditableTextBox", cboINISelect) as TextBox;
                if (editableTextBox != null)
                {
                    editableTextBox.IsReadOnly = isReadOnly;
                    editableTextBox.TextChanged += cboINISelect_TextChanged;
                    editableTextBox.AcceptsTab = false;
                }
            };

            this.PreviewKeyDown += ImageComboBox_PreviewKeyDown;
        }
        #endregion

        #region Event Handlers
        private void ImageComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                isInternalUpdate = true;
                HandleArrowKey(e);
            }
            else if (e.Key == Key.Enter)
            {
                HandleEnterKey();
            }
        }

        private void cboINISelect_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isReadOnly || isInternalUpdate || editableTextBox == null)
                return;

            int selectionStart = editableTextBox.SelectionStart;
            int selectionLength = editableTextBox.SelectionLength;

            FilterDropdown(editableTextBox.Text);

            if (!(selectionLength == lastConfirmedText.Length && editableTextBox.Text.Length == 1))
            {
                editableTextBox.SelectionStart = selectionStart;
                editableTextBox.SelectionLength = selectionLength;
            }
            else
            {
                editableTextBox.SelectionStart = editableTextBox.Text.Length;
                editableTextBox.SelectionLength = 0;
            }
        }

        private void cboINISelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && !isInternalUpdate && cboINISelect.IsDropDownOpen)
            {
                if (e.AddedItems[0] is DropdownItem item)
                {
                    ConfirmSelection(item.Name);
                }
            }
        }

        private void cboINISelect_LostFocus(object sender, RoutedEventArgs e)
        {
            cboINISelect.IsDropDownOpen = false;
            if (!isReadOnly && cboINISelect.Text != lastConfirmedText)
            {
                ConfirmSelection(cboINISelect.Text);
            }
        }

        private void cboINISelect_KeyDown(object sender, KeyEventArgs e)
        {
            if (isReadOnly)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                cboINISelect.IsDropDownOpen = false;

                if (cboINISelect.SelectedItem != null)
                {
                    if (cboINISelect.SelectedItem is DropdownItem selectedItem)
                    {
                        ConfirmSelection(selectedItem.Name);
                    }
                }
                else
                {
                    var match = allItems.FirstOrDefault(item =>
                        item.Name.StartsWith(cboINISelect.Text, StringComparison.OrdinalIgnoreCase));
                    ConfirmSelection(match != null ? match.Name : cboINISelect.Text);
                }
            }
        }

        private void cboINISelect_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (isReadOnly)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (cboINISelect.SelectedItem != null)
                {
                    if (cboINISelect.SelectedItem is DropdownItem selectedItem)
                    {
                        ConfirmSelection(selectedItem.Name);
                    }
                }
                else
                {
                    var match = allItems.FirstOrDefault(item =>
                        item.Name.StartsWith(cboINISelect.Text, StringComparison.OrdinalIgnoreCase));
                    ConfirmSelection(match != null ? match.Name : cboINISelect.Text);
                }
                cboINISelect.IsDropDownOpen = false;
            }
            else if (editableTextBox != null &&
                     !e.Key.IsModifierKey() &&
                     editableTextBox.SelectionLength == editableTextBox.Text.Length &&
                     editableTextBox.SelectionLength > 0)
            {
                if ((e.Key >= Key.A && e.Key <= Key.Z) ||
                    (e.Key >= Key.D0 && e.Key <= Key.D9) ||
                    (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ||
                    e.Key == Key.Space ||
                    e.Key == Key.OemMinus ||
                    e.Key == Key.OemPeriod ||
                    e.Key == Key.OemQuestion ||
                    (e.Key >= Key.Oem1 && e.Key <= Key.OemBackslash))
                {
                    string key = e.Key.ToString();
                    if (key.Length == 1 || (e.Key >= Key.A && e.Key <= Key.Z))
                    {
                        isInternalUpdate = true;
                        if (e.Key >= Key.A && e.Key <= Key.Z)
                        {
                            key = ((char)('a' + (e.Key - Key.A))).ToString();
                            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                            {
                                key = key.ToUpper();
                            }
                        }
                        else if (e.Key >= Key.D0 && e.Key <= Key.D9)
                        {
                            key = ((char)('0' + (e.Key - Key.D0))).ToString();
                        }
                        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                        {
                            key = ((char)('0' + (e.Key - Key.NumPad0))).ToString();
                        }
                        editableTextBox.Text = key;
                        editableTextBox.SelectionStart = 1;
                        editableTextBox.SelectionLength = 0;
                        FilterDropdown(key);
                        isInternalUpdate = false;
                        e.Handled = true;
                    }
                }
            }
        }

        private void EditableTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isReadOnly)
            {
                e.Handled = true;
                cboINISelect.IsDropDownOpen = !cboINISelect.IsDropDownOpen;
            }
        }

        private void EditableTextBox_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (isReadOnly)
            {
                e.Handled = true;
            }
        }
        #endregion

        #region Methods
        public void Add(string name, string imagePath)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                allItems.Add(new DropdownItem { Name = name, ImagePath = imagePath });
                cboINISelect.ItemsSource = allItems;
            });
        }

        private void FilterDropdown(string input)
        {
            if (isInternalUpdate)
                return;

            var filteredItems = allItems
                .Where(item => item.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                .Take(MaxVisibleItems)
                .ToList();

            isInternalUpdate = true;
            string currentText = cboINISelect.Text;
            int selectionStart = editableTextBox?.SelectionStart ?? 0;
            int selectionLength = editableTextBox?.SelectionLength ?? 0;

            cboINISelect.ItemsSource = filteredItems;
            cboINISelect.SelectedIndex = -1;

            if (!string.IsNullOrEmpty(currentText))
            {
                cboINISelect.Text = currentText;
                if (editableTextBox != null)
                {
                    editableTextBox.SelectionStart = selectionStart;
                    editableTextBox.SelectionLength = selectionLength;
                }
            }

            isInternalUpdate = false;
            cboINISelect.IsDropDownOpen = filteredItems.Count > 0;
        }

        private void HandleArrowKey(KeyEventArgs e)
        {

            if (e.Key == Key.Down)
            {
                if (!cboINISelect.IsDropDownOpen)
                {
                    cboINISelect.IsDropDownOpen = true;
                    if (cboINISelect.Items.Count > 0)
                        cboINISelect.SelectedIndex = 0;
                }
                else if (cboINISelect.Items.Count > 0)
                {
                    if (cboINISelect.SelectedIndex == -1)
                        cboINISelect.SelectedIndex = 0;
                    else if (cboINISelect.SelectedIndex < cboINISelect.Items.Count - 1)
                        cboINISelect.SelectedIndex++;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (cboINISelect.IsDropDownOpen && cboINISelect.Items.Count > 0)
                {
                    if (cboINISelect.SelectedIndex == -1)
                        cboINISelect.SelectedIndex = cboINISelect.Items.Count - 1;
                    else if (cboINISelect.SelectedIndex > 0)
                        cboINISelect.SelectedIndex--;
                }
                e.Handled = true;
            }
        }

        private void HandleEnterKey()
        {
            if (cboINISelect.SelectedItem != null)
            {
                if (cboINISelect.SelectedItem is DropdownItem selectedItem)
                {
                    ConfirmSelection(selectedItem.Name);
                }
            }
            else
            {
                var match = allItems.FirstOrDefault(item =>
                    item.Name.StartsWith(cboINISelect.Text, StringComparison.OrdinalIgnoreCase));
                ConfirmSelection(match != null ? match.Name : cboINISelect.Text);
            }
            cboINISelect.IsDropDownOpen = false;
        }

        private void ConfirmSelection(string text)
        {
            isInternalUpdate = true;
            cboINISelect.IsDropDownOpen = false;

            var selectedItem = allItems.FirstOrDefault(item =>
                string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase));

            if (selectedItem == null)
            {
                SetSelectedItemImage("");
                cboINISelect.Text = text;
            }
            else
            {
                SetSelectedItemImage(selectedItem.ImagePath);
                cboINISelect.Text = selectedItem.Name;
            }

            lastConfirmedText = cboINISelect.Text;

            if (!isReadOnly && editableTextBox != null)
            {
                editableTextBox.SelectionStart = editableTextBox.Text.Length;
                editableTextBox.SelectionLength = 0;
            }

            cboINISelect.Dispatcher.BeginInvoke(new Action(() =>
            {
                cboINISelect.SelectedItem = selectedItem;
                isInternalUpdate = false;
                cboINISelect.ItemsSource = allItems;
                OnConfirm?.Invoke(this, cboINISelect.Text);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void Clear()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                allItems.Clear();
                cboINISelect.ItemsSource = null;
            });
        }

        public void SetSelectedItemImage(string imagePath)
        {
            if (cboINISelect.Template.FindName("imgSelected", cboINISelect) is Image imgSelected)
            {
                try
                {
                    Uri imageUri;
                    if (imagePath.StartsWith("pack://application:,,,"))
                        imageUri = new Uri(imagePath, UriKind.Absolute);
                    else
                        imageUri = new Uri(imagePath, UriKind.RelativeOrAbsolute);

                    imgSelected.Source = new BitmapImage(imageUri);
                }
                catch (Exception)
                {
                    imgSelected.Source = null;
                }
            }
        }

        public void SetImageFieldVisible(bool isVisible)
        {
        }

        public void SetComboBoxReadOnly(bool isReadOnly)
        {
            this.isReadOnly = isReadOnly;
            if (editableTextBox != null)
                editableTextBox.IsReadOnly = isReadOnly;

            Focusable = !isReadOnly;
            IsTabStop = !isReadOnly;

            if (!isReadOnly && editableTextBox != null)
                editableTextBox.Focusable = true;
        }
        #endregion
    }

    #region Key Extensions
    public static class KeyExtensions
    {
        public static bool IsModifierKey(this Key key)
        {
            return key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LWin || key == Key.RWin;
        }
    }
    #endregion
}
