using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OceanyaClient.Components;
using OceanyaClient.Features.Chat;

namespace OceanyaClient.Components.Forms
{
    public partial class FindInAllLogsWindow : OceanyaWindowContentControl
    {
        public sealed class ResultRow
        {
            internal ResultRow(AllLogSearchResult result)
            {
                Result = result;
            }

            internal AllLogSearchResult Result { get; }
            public string DisplayName => Result.DisplayName;
            public string MatchCountText => Result.MatchCount.ToString("N0", CultureInfo.InvariantCulture);
            public string DetailText =>
                $"{Result.LastWriteTime:g} | {FormatBytes(Result.FileSizeBytes)}";
        }

        private sealed record LargeFilePreview(string Text, IReadOnlyList<LogTextOffsetMatch> Matches, int FirstLocalMatchIndex);

        private const long FullFilePreviewLimitBytes = 32L * 1024L * 1024L;
        private const int LargeFilePreviewMatchWindow = 240;
        private readonly AllLogSearchService searchService = new AllLogSearchService();
        private readonly string logRoot;
        private readonly ObservableCollection<ResultRow> results = new ObservableCollection<ResultRow>();
        private CancellationTokenSource? searchCancellation;
        private CancellationTokenSource? loadCancellation;
        private IReadOnlyList<LogTextOffsetMatch> selectedFileMatches = Array.Empty<LogTextOffsetMatch>();
        private int selectedFileMatchIndex = -1;
        private int currentGlobalMatchIndex = -1;
        private int pendingSelectionMatchIndex = -1;
        private int selectedPreviewFirstLocalMatchIndex;
        private AllLogSearchOptions? currentOptions;

        public override string HeaderText => "Find in All Logs";
        public override bool IsUserResizeEnabled => true;

        public ObservableCollection<ResultRow> Results => results;

        public FindInAllLogsWindow(string logRoot)
        {
            this.logRoot = logRoot;
            InitializeComponent();
            DataContext = this;
            Title = "Find in All Logs";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            Loaded += (_, _) => TxtSearch.Focus();
            Closed += (_, _) =>
            {
                searchCancellation?.Cancel();
                loadCancellation?.Cancel();
            };
            TxtStatus.Text = string.IsNullOrWhiteSpace(logRoot)
                ? "No log folder can be resolved."
                : logRoot;
            TxtSearchSummary.Text = "Logs: 0 total | Lines: 0 | Search: not run";
        }

        private async void BtnFind_Click(object sender, RoutedEventArgs e)
        {
            await RunSearchAsync();
        }

        private async Task RunSearchAsync()
        {
            string searchText = TxtSearch.Text ?? string.Empty;
            currentOptions = new AllLogSearchOptions(
                searchText,
                ChkSearchIC.IsChecked == true,
                ChkSearchOOC.IsChecked == true,
                ChkMatchCase.IsChecked == true,
                ChkWholeWord.IsChecked == true,
                ChkRegex.IsChecked == true);

            searchCancellation?.Cancel();
            loadCancellation?.Cancel();
            selectedFileMatches = Array.Empty<LogTextOffsetMatch>();
            selectedFileMatchIndex = -1;
            currentGlobalMatchIndex = -1;
            pendingSelectionMatchIndex = -1;
            selectedPreviewFirstLocalMatchIndex = 0;
            results.Clear();
            FilePreview.Document = BuildDocument(string.Empty);
            TxtSelectedFile.Text = "No file selected";
            TxtSelectedFileDetail.Text = string.Empty;
            TxtSearchSummary.Text = "Logs: 0 total | Lines: 0 | Search: not run";
            UpdateCounts();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                TxtStatus.Text = "Enter search text.";
                return;
            }

            if (!currentOptions.IncludeIc && !currentOptions.IncludeOoc)
            {
                TxtStatus.Text = "Select IC, OOC, or both.";
                return;
            }

            if (string.IsNullOrWhiteSpace(logRoot) || !Directory.Exists(logRoot))
            {
                TxtStatus.Text = "The log folder does not exist yet.";
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            searchCancellation = cancellation;
            BtnFind.IsEnabled = false;
            BtnCancel.IsEnabled = true;
            TxtStatus.Text = "Searching...";
            TxtSearchSummary.Text = "Scanning logs...";

            Progress<AllLogSearchProgress> progress = new Progress<AllLogSearchProgress>(value =>
            {
                string fileName = string.IsNullOrWhiteSpace(value.CurrentFile)
                    ? string.Empty
                    : Path.GetFileName(value.CurrentFile);
                TxtStatus.Text = value.TotalFiles == 0
                    ? "Searching..."
                    : $"Scanned {value.FilesScanned:N0} of {value.TotalFiles:N0} files {fileName}";
            });

            try
            {
                AllLogSearchSummary summary = await searchService.SearchAsync(
                    logRoot,
                    currentOptions,
                    progress,
                    cancellation.Token);
                IReadOnlyList<AllLogSearchResult> found = summary.Results;

                foreach (AllLogSearchResult result in found)
                {
                    results.Add(new ResultRow(result));
                }

                UpdateCounts();
                TxtStatus.Text = found.Count == 0
                    ? "No matches found."
                    : $"Found {found.Sum(result => result.MatchCount):N0} matches in {found.Count:N0} files.";
                TxtSearchSummary.Text = FormatSearchSummary(summary);

                if (results.Count > 0)
                {
                    pendingSelectionMatchIndex = 0;
                    ResultsList.SelectedIndex = 0;
                }
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Search canceled.";
            }
            finally
            {
                if (ReferenceEquals(searchCancellation, cancellation))
                {
                    searchCancellation = null;
                }

                cancellation.Dispose();
                BtnFind.IsEnabled = true;
                BtnCancel.IsEnabled = false;
                UpdateCounts();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            searchCancellation?.Cancel();
            loadCancellation?.Cancel();
        }

        private async void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsList.SelectedItem is not ResultRow row)
            {
                return;
            }

            int localMatchIndex = pendingSelectionMatchIndex >= 0 ? pendingSelectionMatchIndex : 0;
            pendingSelectionMatchIndex = -1;
            await LoadSelectedFileAsync(row, localMatchIndex);
        }

        private async Task LoadSelectedFileAsync(ResultRow row, int localMatchIndex)
        {
            loadCancellation?.Cancel();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            loadCancellation = cancellation;
            CancellationToken cancellationToken = cancellation.Token;

            TxtSelectedFile.Text = row.DisplayName;
            TxtSelectedFileDetail.Text = "Loading...";
            FilePreview.Document = BuildDocument(string.Empty);
            selectedFileMatches = Array.Empty<LogTextOffsetMatch>();
            selectedFileMatchIndex = -1;
            selectedPreviewFirstLocalMatchIndex = 0;
            UpdateCounts();

            try
            {
                AllLogSearchOptions? options = currentOptions;
                if (options != null && row.Result.FileSizeBytes > FullFilePreviewLimitBytes)
                {
                    LargeFilePreview preview = await Task.Run(
                        () => BuildLargeFileMatchPreview(row.Result.FilePath, row.DisplayName, options, localMatchIndex, cancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    FilePreview.Document = BuildDocument(preview.Text);
                    selectedFileMatches = preview.Matches;
                    selectedPreviewFirstLocalMatchIndex = preview.FirstLocalMatchIndex;
                    TxtSelectedFileDetail.Text = $"{selectedFileMatches.Count:N0} matches in matching-line preview";
                }
                else
                {
                    string text = await Task.Run(() => AllLogSearchService.ReadFileText(row.Result.FilePath), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    FlowDocument document = BuildDocument(text);
                    FilePreview.Document = document;

                    selectedFileMatches = await Task.Run(
                        () => options == null
                            ? Array.Empty<LogTextOffsetMatch>()
                            : AllLogSearchService.FindOffsetsInText(text, options, cancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    selectedPreviewFirstLocalMatchIndex = 0;
                    TxtSelectedFileDetail.Text = $"{selectedFileMatches.Count:N0} matches in this file";
                }

                selectedFileMatchIndex = selectedFileMatches.Count == 0
                    ? -1
                    : Math.Clamp(localMatchIndex - selectedPreviewFirstLocalMatchIndex, 0, selectedFileMatches.Count - 1);
                currentGlobalMatchIndex = CalculateGlobalMatchIndex(row, selectedFileMatchIndex);
                ApplyPreviewHighlights();
                UpdateCounts();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FilePreview.Document = BuildDocument("Could not read this log file:\r\n" + ex.Message);
                TxtSelectedFileDetail.Text = "Read failed";
                selectedFileMatches = Array.Empty<LogTextOffsetMatch>();
                selectedFileMatchIndex = -1;
                UpdateCounts();
            }
            finally
            {
                if (ReferenceEquals(loadCancellation, cancellation))
                {
                    loadCancellation = null;
                }

                cancellation.Dispose();
            }
        }

        private void NavigateTo(int globalMatchIndex)
        {
            int totalMatches = results.Sum(row => row.Result.MatchCount);
            if (totalMatches == 0)
            {
                return;
            }

            int normalizedIndex = ((globalMatchIndex % totalMatches) + totalMatches) % totalMatches;
            int cursor = 0;
            for (int i = 0; i < results.Count; i++)
            {
                ResultRow row = results[i];
                int nextCursor = cursor + row.Result.MatchCount;
                if (normalizedIndex < nextCursor)
                {
                    int localIndex = normalizedIndex - cursor;
                    currentGlobalMatchIndex = normalizedIndex;
                    if (ResultsList.SelectedIndex == i)
                    {
                        if (row.Result.FileSizeBytes > FullFilePreviewLimitBytes
                            && (localIndex < selectedPreviewFirstLocalMatchIndex
                                || localIndex >= selectedPreviewFirstLocalMatchIndex + selectedFileMatches.Count))
                        {
                            selectedFileMatchIndex = -1;
                            _ = LoadSelectedFileAsync(row, localIndex);
                        }
                        else
                        {
                            selectedFileMatchIndex = localIndex - selectedPreviewFirstLocalMatchIndex;
                            ApplyPreviewHighlights();
                        }

                        UpdateCounts();
                    }
                    else
                    {
                        pendingSelectionMatchIndex = localIndex;
                        ResultsList.SelectedIndex = i;
                    }

                    ResultsList.ScrollIntoView(row);
                    return;
                }

                cursor = nextCursor;
            }
        }

        private void ApplyPreviewHighlights()
        {
            FlowDocument document = FilePreview.Document;
            LogDocumentSearch.DocumentTextIndex index = LogDocumentSearch.CreateIndex(document);
            IReadOnlyList<LogTextMatch> matches = LogDocumentSearch.ResolveMatches(index, selectedFileMatches);

            foreach (LogTextMatch match in matches)
            {
                new TextRange(match.Start, match.End)
                    .ApplyPropertyValue(TextElement.BackgroundProperty, LogFindHighlightBrushes.Match);
            }

            if (selectedFileMatchIndex >= 0 && selectedFileMatchIndex < matches.Count)
            {
                LogTextMatch active = matches[selectedFileMatchIndex];
                TextRange activeRange = new TextRange(active.Start, active.End);
                activeRange.ApplyPropertyValue(TextElement.BackgroundProperty, LogFindHighlightBrushes.ActiveMatch);
                FilePreview.Selection.Select(active.Start, active.End);
                FilePreview.Focus();
            }
        }

        private static FlowDocument BuildDocument(string text)
        {
            FlowDocument document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13
            };
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0)
            };
            paragraph.Inlines.Add(new Run(text));
            document.Blocks.Add(paragraph);
            return document;
        }

        private static LargeFilePreview BuildLargeFileMatchPreview(
            string filePath,
            string displayName,
            AllLogSearchOptions options,
            int targetLocalMatchIndex,
            CancellationToken cancellationToken)
        {
            int firstLocalMatchIndex = Math.Max(0, targetLocalMatchIndex - (LargeFilePreviewMatchWindow / 2));
            int lastLocalMatchIndex = firstLocalMatchIndex + LargeFilePreviewMatchWindow - 1;
            LogTextMatcher matcher = LogTextMatcher.Create(
                options.SearchText,
                options.MatchCase,
                options.WholeWord,
                options.UseRegex);
            StringBuilder preview = new StringBuilder();
            List<LogTextOffsetMatch> previewMatches = new List<LogTextOffsetMatch>();
            preview.Append("Large log preview: showing matches ");
            preview.Append((firstLocalMatchIndex + 1).ToString("N0", CultureInfo.InvariantCulture));
            preview.Append(" through ");
            preview.Append((lastLocalMatchIndex + 1).ToString("N0", CultureInfo.InvariantCulture));
            preview.AppendLine(" around the selected match.");
            preview.AppendLine(displayName);
            preview.AppendLine();

            using FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

            int lineNumber = 0;
            int matchCursor = 0;
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;
                if (!ShouldSearchPreviewLine(line, options))
                {
                    continue;
                }

                IReadOnlyList<LogTextOffsetMatch> lineMatches = matcher.FindOffsets(line, cancellationToken);
                if (lineMatches.Count == 0)
                {
                    continue;
                }

                int lineFirstMatchIndex = matchCursor;
                int lineLastMatchIndex = matchCursor + lineMatches.Count - 1;
                matchCursor += lineMatches.Count;
                if (lineLastMatchIndex < firstLocalMatchIndex)
                {
                    continue;
                }

                if (lineFirstMatchIndex > lastLocalMatchIndex)
                {
                    break;
                }

                preview.Append("--- line ");
                preview.Append(lineNumber.ToString(CultureInfo.InvariantCulture));
                preview.AppendLine(" ---");
                int linePreviewStart = preview.Length;
                preview.AppendLine(line);
                for (int i = 0; i < lineMatches.Count; i++)
                {
                    int localMatchIndex = lineFirstMatchIndex + i;
                    if (localMatchIndex < firstLocalMatchIndex || localMatchIndex > lastLocalMatchIndex)
                    {
                        continue;
                    }

                    LogTextOffsetMatch match = lineMatches[i];
                    previewMatches.Add(new LogTextOffsetMatch(linePreviewStart + match.StartIndex, match.Length));
                }
            }

            return new LargeFilePreview(preview.ToString(), previewMatches, firstLocalMatchIndex);
        }

        private static bool ShouldSearchPreviewLine(string line, AllLogSearchOptions options)
        {
            bool isOoc = line.StartsWith("[OOC]", StringComparison.OrdinalIgnoreCase);
            return isOoc ? options.IncludeOoc : options.IncludeIc;
        }

        private int CalculateGlobalMatchIndex(ResultRow selectedRow, int localMatchIndex)
        {
            if (localMatchIndex < 0)
            {
                return -1;
            }

            int cursor = 0;
            foreach (ResultRow row in results)
            {
                if (ReferenceEquals(row, selectedRow))
                {
                    return cursor + localMatchIndex;
                }

                cursor += row.Result.MatchCount;
            }

            return -1;
        }

        private void UpdateCounts()
        {
            int totalFiles = results.Count;
            int totalMatches = results.Sum(row => row.Result.MatchCount);
            TxtFileCount.Text = totalFiles == 0 ? string.Empty : $"{totalFiles:N0} matching";
            bool hasResults = totalMatches > 0;
            BtnPrev.IsEnabled = hasResults;
            BtnNext.IsEnabled = hasResults;
            TxtResultCount.Text = hasResults && currentGlobalMatchIndex >= 0
                ? $"{currentGlobalMatchIndex + 1:N0} of {totalMatches:N0} total matches"
                : hasResults
                    ? $"{totalMatches:N0} total matches"
                    : string.Empty;
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(currentGlobalMatchIndex - 1);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(currentGlobalMatchIndex + 1);
        }

        private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null && current is not ListBoxItem)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void OpenFileLocationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsList.SelectedItem is not ResultRow row)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + row.Result.FilePath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Could not open the file location:\n" + ex.Message);
            }
        }

        private void OpenFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsList.SelectedItem is not ResultRow row)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = "\"" + row.Result.FilePath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OceanyaMessageBox.Show("Could not open the log file:\n" + ex.Message);
            }
        }

        private async void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    NavigateTo(currentGlobalMatchIndex - 1);
                }
                else if (results.Count > 0)
                {
                    NavigateTo(currentGlobalMatchIndex + 1);
                }
                else
                {
                    await RunSearchAsync();
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HostWindow?.Close();
            }
        }

        private void FindWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F3)
            {
                NavigateTo(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? currentGlobalMatchIndex - 1
                    : currentGlobalMatchIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.G && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                NavigateTo(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? currentGlobalMatchIndex - 1
                    : currentGlobalMatchIndex + 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HostWindow?.Close();
                e.Handled = true;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }

            return suffix == 0
                ? bytes.ToString("N0", CultureInfo.InvariantCulture) + " B"
                : value.ToString("N1", CultureInfo.InvariantCulture) + " " + suffixes[suffix];
        }

        private static string FormatSearchSummary(AllLogSearchSummary summary)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Logs: {0:N0} total | Lines: {1:N0} | Search: {2}",
                summary.TotalLogFiles,
                summary.TotalTextLines,
                FormatElapsed(summary.SearchElapsed));
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMilliseconds < 1000)
            {
                return elapsed.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture) + " ms";
            }

            if (elapsed.TotalMinutes < 1)
            {
                return elapsed.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture) + " s";
            }

            return ((int)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                + "m "
                + elapsed.Seconds.ToString(CultureInfo.InvariantCulture)
                + "s";
        }
    }
}
