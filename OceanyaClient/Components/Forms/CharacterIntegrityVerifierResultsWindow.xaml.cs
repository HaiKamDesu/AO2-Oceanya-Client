using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OceanyaClient
{
    /// <summary>
    /// Displays persisted and live character integrity verifier results.
    /// </summary>
    public partial class CharacterIntegrityVerifierResultsWindow : OceanyaWindowContentControl
    {
        private readonly string characterDirectoryPath;
        private readonly string characterName;
        private readonly Action<CharacterIntegrityReport>? onReportUpdated;
        private CharacterIntegrityReport report;

        public CharacterIntegrityVerifierResultsWindow(
            CharacterIntegrityReport report,
            string characterDirectoryPath,
            string characterName,
            Action<CharacterIntegrityReport>? onReportUpdated = null)
        {
            InitializeComponent();
            Title = "Integrity Verifier Results";
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            this.characterDirectoryPath = characterDirectoryPath?.Trim() ?? string.Empty;
            this.characterName = characterName?.Trim() ?? string.Empty;
            this.onReportUpdated = onReportUpdated;
            RefreshUiFromReport();
        }

        /// <inheritdoc/>
        public override string HeaderText => "INTEGRITY VERIFIER RESULTS";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        private void RefreshUiFromReport()
        {
            string safeCharacterName = string.IsNullOrWhiteSpace(characterName) ? "(unknown character)" : characterName;
            HeaderTextBlock.Text = safeCharacterName;
            MetaTextBlock.Text =
                "Folder: " + (string.IsNullOrWhiteSpace(characterDirectoryPath) ? "(unknown)" : characterDirectoryPath)
                + " | Generated: " + report.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            List<CharacterIntegrityIssueViewModel> rows = new List<CharacterIntegrityIssueViewModel>();
            foreach (CharacterIntegrityIssue issue in report.Results)
            {
                rows.Add(new CharacterIntegrityIssueViewModel(issue));
            }

            ResultsDataGrid.ItemsSource = rows;
            SummaryTextBlock.Text = report.HasFailures
                ? $"Failed checks: {report.FailureCount} / {report.Results.Count}"
                : $"All checks passed ({report.Results.Count}).";
        }

        private async void RunVerifierButton_Click(object sender, RoutedEventArgs e)
        {
            await WaitForm.ShowFormAsync("Running integrity verifier...", this);
            try
            {
                WaitForm.SetSubtitle("Verifying: " + characterName);
                report = await Task.Run(() => CharacterIntegrityVerifier.RunAndPersist(characterDirectoryPath));
                RefreshUiFromReport();
                onReportUpdated?.Invoke(report);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private async void RunSingleTestMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveIssueFromSender(sender) is not CharacterIntegrityIssueViewModel row)
            {
                return;
            }

            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                contextMenu.IsOpen = false;
            }

            await WaitForm.ShowFormAsync("Re-running test...", this);
            try
            {
                WaitForm.SetSubtitle("Test: " + row.TestName);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                report = await Task.Run(() => CharacterIntegrityVerifier.RerunSingleTest(report, row.Source));
                RefreshUiFromReport();
                onReportUpdated?.Invoke(report);
            }
            finally
            {
                WaitForm.CloseForm();
            }
        }

        private async void SolveErrorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveIssueFromSender(sender) is not CharacterIntegrityIssueViewModel row)
            {
                return;
            }

            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                contextMenu.IsOpen = false;
            }

            if (!row.CanAutoFix)
            {
                return;
            }

            if (!CharacterIntegrityVerifier.TryApplyFix(report, row.Source, out string fixMessage))
            {
                OceanyaMessageBox.Show(
                    this,
                    fixMessage,
                    "Integrity Verifier",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await WaitForm.ShowFormAsync("Applying integrity fix...", this);
            try
            {
                WaitForm.SetSubtitle("Re-running test after fix...");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                report = await Task.Run(() => CharacterIntegrityVerifier.RerunSingleTest(report, row.Source));
            }
            finally
            {
                WaitForm.CloseForm();
            }

            RefreshUiFromReport();
            onReportUpdated?.Invoke(report);

            OceanyaMessageBox.Show(
                this,
                fixMessage,
                "Integrity Verifier",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ResultsDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is not CharacterIntegrityIssueViewModel row)
            {
                e.Row.ContextMenu = null;
                return;
            }

            MenuItem rerunItem = new MenuItem
            {
                Header = "Run test again",
                DataContext = row
            };
            rerunItem.Click += RunSingleTestMenuItem_Click;

            MenuItem solveItem = new MenuItem
            {
                Header = "Solve error",
                IsEnabled = row.CanAutoFix,
                DataContext = row
            };
            solveItem.Click += SolveErrorMenuItem_Click;

            MenuItem viewErrorItem = new MenuItem
            {
                Header = "View error",
                IsEnabled = row.CanViewError,
                DataContext = row
            };
            viewErrorItem.Click += ViewErrorMenuItem_Click;

            ContextMenu contextMenu = new ContextMenu();
            contextMenu.Items.Add(rerunItem);
            contextMenu.Items.Add(solveItem);
            contextMenu.Items.Add(viewErrorItem);
            e.Row.ContextMenu = contextMenu;
        }

        private void ViewErrorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveIssueFromSender(sender) is not CharacterIntegrityIssueViewModel row || !row.CanViewError)
            {
                return;
            }

            CharacterIntegrityIssue issue = row.Source;
            switch (issue.ViewActionType)
            {
                case CharacterIntegrityViewActionType.OpenInExplorer:
                    OpenInExplorer(issue.ViewPath);
                    break;
                case CharacterIntegrityViewActionType.OpenInExplorerSelect:
                    OpenInExplorerSelect(issue.ViewPath);
                    break;
                case CharacterIntegrityViewActionType.OpenPath:
                    OpenBestEffort(issue.ViewPath);
                    break;
                case CharacterIntegrityViewActionType.OpenPathAndCharIni:
                    OpenBestEffort(issue.ViewPath);
                    OpenBestEffort(issue.SecondaryViewPath);
                    break;
            }
        }

        private static void OpenBestEffort(string path)
        {
            string safePath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safePath))
            {
                return;
            }

            if (File.Exists(safePath))
            {
                OpenPath(safePath);
                return;
            }

            if (Directory.Exists(safePath))
            {
                OpenInExplorer(safePath);
                return;
            }

            string? parent = Path.GetDirectoryName(safePath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                OpenInExplorer(parent);
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch
            {
                // ignored
            }
        }

        private static void OpenInExplorer(string directoryPath)
        {
            string safePath = directoryPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safePath))
            {
                return;
            }

            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{safePath}\"",
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch
            {
                // ignored
            }
        }

        private static void OpenInExplorerSelect(string filePath)
        {
            string safePath = filePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safePath))
            {
                return;
            }

            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{safePath}\"",
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch
            {
                string? parent = Path.GetDirectoryName(safePath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    OpenInExplorer(parent);
                }
            }
        }

        private static CharacterIntegrityIssueViewModel? ResolveIssueFromSender(object sender)
        {
            if (sender is not MenuItem menuItem)
            {
                return null;
            }

            if (menuItem.DataContext is CharacterIntegrityIssueViewModel rowFromDataContext)
            {
                return rowFromDataContext;
            }

            if (menuItem.Parent is ContextMenu contextMenu
                && contextMenu.PlacementTarget is FrameworkElement element
                && element.DataContext is CharacterIntegrityIssueViewModel rowFromPlacementTarget)
            {
                return rowFromPlacementTarget;
            }

            return null;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public sealed class CharacterIntegrityIssueViewModel : INotifyPropertyChanged
    {
        public CharacterIntegrityIssueViewModel(CharacterIntegrityIssue source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public CharacterIntegrityIssue Source { get; }
        public string TestName => Source.TestName;
        public string Description => Source.Description;
        public string Message => Source.Message;
        public bool Passed => Source.Passed;
        public bool CanAutoFix => Source.CanAutoFix;
        public bool CanViewError => Source.CanViewError;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
