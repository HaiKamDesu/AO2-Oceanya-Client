using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Common;

namespace OceanyaClient
{
    public partial class CustomMusicCommandEditorWindow : OceanyaWindowContentControl
    {
        private readonly string? _editId;

        /// <summary>New blank command.</summary>
        public CustomMusicCommandEditorWindow()
        {
            InitializeComponent();
            Setup(null, null, null);
        }

        /// <summary>Edit an existing command.</summary>
        public CustomMusicCommandEditorWindow(CustomMusicCommand existing)
        {
            _editId = existing.Id;
            InitializeComponent();
            Setup(existing.Name, existing.Command, existing.CategoryPath);
        }

        /// <summary>Pre-fill from another music item ("Add to Custom Commands").</summary>
        public CustomMusicCommandEditorWindow(string prefillName, string prefillCommand)
        {
            InitializeComponent();
            Setup(prefillName, prefillCommand, null);
        }

        public override string HeaderText => _editId != null ? "EDIT CUSTOM COMMAND" : "NEW CUSTOM COMMAND";

        private void Setup(string? name, string? command, string? categoryPath)
        {
            Title = _editId != null ? "Edit Custom Command" : "New Custom Command";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));

            var existingPaths = SaveFile.Data.CustomMusicCommands
                .Where(c => !string.IsNullOrWhiteSpace(c.CategoryPath))
                .Select(c => c.CategoryPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            cboFolder.Items.Clear();
            foreach (string path in existingPaths)
            {
                cboFolder.Items.Add(path);
            }

            string resolvedPath = categoryPath ?? SaveFile.Data.LastCustomCommandCategoryPath;
            cboFolder.Text = resolvedPath;

            txtName.Text = name ?? string.Empty;
            txtCommand.Text = command ?? string.Empty;

            Loaded += (_, _) => txtName.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            string command = (txtCommand.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                MessageBox.Show("Command is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCommand.Focus();
                return;
            }

            string categoryPath = (cboFolder.Text ?? string.Empty).Trim().Trim('/');

            if (_editId != null)
            {
                CustomMusicCommand? existing = SaveFile.Data.CustomMusicCommands
                    .FirstOrDefault(c => string.Equals(c.Id, _editId, StringComparison.Ordinal));
                if (existing != null)
                {
                    existing.Name = name;
                    existing.Command = command;
                    existing.CategoryPath = categoryPath;
                }
            }
            else
            {
                SaveFile.Data.CustomMusicCommands.Add(new CustomMusicCommand
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    Command = command,
                    CategoryPath = categoryPath,
                });
            }

            SaveFile.Data.LastCustomCommandCategoryPath = categoryPath;
            SaveFile.Save();

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
