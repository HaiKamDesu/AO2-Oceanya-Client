using Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OceanyaClient
{
    public partial class DebugConsoleWindow : OceanyaWindowContentControl
    {
        private static DebugConsoleWindow? _instance;
        private static readonly object _instanceLock = new object();

        /// <inheritdoc/>
        public override string HeaderText => "DEBUG CONSOLE";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        private ConcurrentQueue<CustomConsole.LogEntry> _pendingEntries = new ConcurrentQueue<CustomConsole.LogEntry>();

        private FlowDocument _consoleDocument = null!;
        private Paragraph _currentParagraph = null!;

        private const int MAX_DOCUMENT_LINES = 5000;
        private int _lineCount = 0;

        private DispatcherTimer? _updateTimer;
        private const int UPDATE_INTERVAL_MS = 100;

        // Categories currently enabled for display (from SaveFile)
        private HashSet<string> _enabledCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Whether we are in the middle of a filter-rebuild (suppresses saves)
        private bool _suppressCategoryChangeSave = false;

        public static void ShowWindow()
        {
            lock (_instanceLock)
            {
                if (_instance == null)
                {
                    _instance = new DebugConsoleWindow();
                    _instance.Activate();
                    _instance.Show();
                    _instance.Focus();
                }
                else
                {
                    _instance.Dispatcher.InvokeAsync(() => {
                        _instance.Activate();
                        _instance.Focus();
                    }, DispatcherPriority.Normal);
                }
            }
        }

        public DebugConsoleWindow()
        {
            InitializeComponent();
            Title = "Debug Console";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));

            InitializeConsoleDocument();
            InitializeUpdateTimer();

            CustomConsole.OnLogEntry += QueueEntry;
        }

        private void InitializeConsoleDocument()
        {
            _consoleDocument = new FlowDocument();
            _consoleDocument.PageWidth = double.NaN;
            _consoleDocument.Background = Brushes.Transparent;
            _consoleDocument.Foreground = Brushes.LightGray;
            _consoleDocument.FontFamily = new FontFamily("Consolas");
            _consoleDocument.FontSize = 12;
            _consoleDocument.TextAlignment = TextAlignment.Left;

            _currentParagraph = new Paragraph();
            _currentParagraph.Background = Brushes.Transparent;
            _currentParagraph.LineHeight = 1.0;
            _currentParagraph.Margin = new Thickness(0);
            _consoleDocument.Blocks.Add(_currentParagraph);

            ConsoleTextBox.Document = _consoleDocument;
        }

        private void InitializeUpdateTimer()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(UPDATE_INTERVAL_MS);
            _updateTimer.Tick += (s, e) => ProcessPendingEntries();
            _updateTimer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadCategoryFilterState();

                List<CustomConsole.LogEntry> entriesCopy;
                try
                {
                    entriesCopy = new List<CustomConsole.LogEntry>(CustomConsole.logEntries);
                }
                catch (Exception ex)
                {
                    AddEntryToDocument(new CustomConsole.LogEntry(
                        $"Error copying console entries: {ex.Message}",
                        CustomConsole.LogLevel.Error,
                        CustomConsole.LogCategory.System,
                        DateTime.Now));
                    entriesCopy = new List<CustomConsole.LogEntry>();
                }

                if (entriesCopy.Count > 0)
                {
                    int startIndex = Math.Max(0, entriesCopy.Count - MAX_DOCUMENT_LINES);
                    for (int i = startIndex; i < entriesCopy.Count; i++)
                    {
                        if (IsEntryVisible(entriesCopy[i]))
                            AddEntryToDocument(entriesCopy[i]);
                    }
                    ScrollToBottom();
                }

                ProcessPendingEntries();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading console history: {ex.Message}", "Debug Console Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _updateTimer?.Stop();
            _updateTimer = null;

            CustomConsole.OnLogEntry -= QueueEntry;

            lock (_instanceLock)
            {
                _instance = null;
            }
        }

        // ── Category filter ──────────────────────────────────────────────────────

        private void LoadCategoryFilterState()
        {
            _suppressCategoryChangeSave = true;
            try
            {
                _enabledCategories = new HashSet<string>(
                    SaveFile.Data.EnabledLogCategories,
                    StringComparer.OrdinalIgnoreCase);

                FilterSystem.IsChecked  = _enabledCategories.Contains("System");
                FilterNetwork.IsChecked = _enabledCategories.Contains("Network");
                FilterIC.IsChecked      = _enabledCategories.Contains("IC");
                FilterOOC.IsChecked     = _enabledCategories.Contains("OOC");
                FilterViewport.IsChecked = _enabledCategories.Contains("Viewport");
                FilterMusicList.IsChecked = _enabledCategories.Contains("MusicList");
                FilterAreaVisualizer.IsChecked = _enabledCategories.Contains("AreaVisualizer");
                FilterSFX.IsChecked     = _enabledCategories.Contains("SFX");

                UpdateFilterSummary();
            }
            finally
            {
                _suppressCategoryChangeSave = false;
            }
        }

        private void CategoryFilter_Changed(object sender, RoutedEventArgs e)
        {
            _enabledCategories.Clear();
            if (FilterSystem.IsChecked == true)   _enabledCategories.Add("System");
            if (FilterNetwork.IsChecked == true)  _enabledCategories.Add("Network");
            if (FilterIC.IsChecked == true)       _enabledCategories.Add("IC");
            if (FilterOOC.IsChecked == true)      _enabledCategories.Add("OOC");
            if (FilterViewport.IsChecked == true) _enabledCategories.Add("Viewport");
            if (FilterMusicList.IsChecked == true) _enabledCategories.Add("MusicList");
            if (FilterAreaVisualizer.IsChecked == true) _enabledCategories.Add("AreaVisualizer");
            if (FilterSFX.IsChecked == true)      _enabledCategories.Add("SFX");

            UpdateFilterSummary();

            if (!_suppressCategoryChangeSave)
            {
                SaveFile.Data.EnabledLogCategories = _enabledCategories.ToList();
                SaveFile.Save();
            }

            RebuildDocument();
        }

        private void UpdateFilterSummary()
        {
            if (_enabledCategories.Count == Enum.GetValues(typeof(CustomConsole.LogCategory)).Length)
            {
                FilterSummaryLabel.Text = "All categories shown";
            }
            else if (_enabledCategories.Count == 0)
            {
                FilterSummaryLabel.Text = "No categories shown";
            }
            else
            {
                FilterSummaryLabel.Text = $"Showing: {string.Join(", ", _enabledCategories.Order())}";
            }
        }

        private bool IsEntryVisible(CustomConsole.LogEntry entry)
            => _enabledCategories.Contains(entry.Category.ToString());

        private void RebuildDocument()
        {
            _currentParagraph.Inlines.Clear();
            _lineCount = 0;

            List<CustomConsole.LogEntry> snapshot;
            try { snapshot = new List<CustomConsole.LogEntry>(CustomConsole.logEntries); }
            catch { return; }

            int startIndex = Math.Max(0, snapshot.Count - MAX_DOCUMENT_LINES);
            for (int i = startIndex; i < snapshot.Count; i++)
            {
                if (IsEntryVisible(snapshot[i]))
                    AddEntryToDocument(snapshot[i]);
            }

            ScrollToBottom();
        }

        // ── Message processing ───────────────────────────────────────────────────

        private void ProcessPendingEntries()
        {
            if (_pendingEntries.IsEmpty) return;

            int batchSize = 100;
            int processed = 0;

            while (_pendingEntries.TryDequeue(out CustomConsole.LogEntry? entry) && processed < batchSize)
            {
                if (entry != null && IsEntryVisible(entry))
                {
                    AddEntryToDocument(entry);
                    processed++;
                }
            }

            if (processed > 0 && IsScrolledToBottom())
                ScrollToBottom();
        }

        // ── Brush definitions ────────────────────────────────────────────────────

        private static readonly Brush TimestampBrush     = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush InfoBrush          = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        private static readonly Brush ErrorBrush         = new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x66));
        private static readonly Brush WarningBrush       = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
        private static readonly Brush DebugBrush         = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x33));
        private static readonly Brush AiPrefixBrush      = new SolidColorBrush(Color.FromRgb(0x4D, 0xD9, 0xE8));
        private static readonly Brush LevelIndicatorBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private static readonly Brush ContinuationBrush  = new SolidColorBrush(Color.FromRgb(0x88, 0x77, 0x77));

        // Category badge colors
        private static readonly Brush CatSystemBrush  = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
        private static readonly Brush CatNetworkBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0xA6, 0xFF));
        private static readonly Brush CatICBrush      = new SolidColorBrush(Color.FromRgb(0xA8, 0x7F, 0xFF));
        private static readonly Brush CatOOCBrush     = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x66));
        private static readonly Brush CatViewportBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xD9, 0x99));
        private static readonly Brush CatMusicListBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xA4, 0x5A));
        private static readonly Brush CatAreaVisualizerBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0xC6, 0xCF));

        private static Brush GetCategoryBrush(CustomConsole.LogCategory category) => category switch
        {
            CustomConsole.LogCategory.Network => CatNetworkBrush,
            CustomConsole.LogCategory.IC      => CatICBrush,
            CustomConsole.LogCategory.OOC     => CatOOCBrush,
            CustomConsole.LogCategory.Viewport => CatViewportBrush,
            CustomConsole.LogCategory.MusicList => CatMusicListBrush,
            CustomConsole.LogCategory.AreaVisualizer => CatAreaVisualizerBrush,
            _                                 => CatSystemBrush
        };

        private static string GetCategoryBadge(CustomConsole.LogCategory category) => category switch
        {
            CustomConsole.LogCategory.Network => "[NET]",
            CustomConsole.LogCategory.IC      => "[IC] ",
            CustomConsole.LogCategory.OOC     => "[OOC]",
            CustomConsole.LogCategory.Viewport => "[VPT]",
            CustomConsole.LogCategory.MusicList => "[MUS]",
            CustomConsole.LogCategory.AreaVisualizer => "[ARA]",
            _                                 => "[SYS]"
        };

        private void AddEntryToDocument(CustomConsole.LogEntry entry)
        {
            if (_lineCount >= MAX_DOCUMENT_LINES)
                TrimDocument();

            AppendColorizedEntry(entry);
            _lineCount++;
        }

        private void AppendColorizedEntry(CustomConsole.LogEntry entry)
        {
            string text = entry.Text;

            // Timestamp
            AppendRun($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}]", TimestampBrush);

            // Category badge
            Brush catBrush = GetCategoryBrush(entry.Category);
            AppendRun($" {GetCategoryBadge(entry.Category)}", catBrush);

            string remaining = text;

            // Detect log level emoji
            Brush bodyBrush;
            string emojiPrefix;

            if (remaining.StartsWith(" ❌") || remaining.StartsWith("❌"))
            {
                emojiPrefix = remaining.StartsWith(" ❌") ? " ❌" : "❌";
                bodyBrush = ErrorBrush;
            }
            else if (remaining.StartsWith(" ⚠️") || remaining.StartsWith("⚠️"))
            {
                emojiPrefix = remaining.StartsWith(" ⚠️") ? " ⚠️" : "⚠️";
                bodyBrush = WarningBrush;
            }
            else if (remaining.StartsWith(" 🔍") || remaining.StartsWith("🔍"))
            {
                emojiPrefix = remaining.StartsWith(" 🔍") ? " 🔍" : "🔍";
                bodyBrush = DebugBrush;
            }
            else if (remaining.StartsWith(" ℹ️") || remaining.StartsWith("ℹ️"))
            {
                emojiPrefix = remaining.StartsWith(" ℹ️") ? " ℹ️" : "ℹ️";
                bodyBrush = InfoBrush;
            }
            else
            {
                // Exception detail lines or plain continuation text
                AppendRun(" " + remaining + Environment.NewLine, ContinuationBrush);
                return;
            }

            AppendRun(emojiPrefix, LevelIndicatorBrush);
            remaining = remaining.Substring(emojiPrefix.Length);

            // [AI:name] prefix
            if (remaining.StartsWith(" [AI:"))
            {
                int aiClose = remaining.IndexOf(']', 5);
                if (aiClose > 0)
                {
                    AppendRun(" ", bodyBrush);
                    AppendRun(remaining.Substring(1, aiClose), AiPrefixBrush);
                    remaining = remaining.Substring(aiClose + 1);
                }
            }

            AppendRun(remaining + Environment.NewLine, bodyBrush);
        }

        private void AppendRun(string text, Brush foreground)
        {
            Run run = new Run(text);
            run.Foreground = foreground;
            run.Background = Brushes.Transparent;
            _currentParagraph.Inlines.Add(run);
        }

        private void TrimDocument()
        {
            int linesToRemove = MAX_DOCUMENT_LINES / 5;
            int runCount = _currentParagraph.Inlines.Count;
            int removeCount = Math.Min(linesToRemove * 2, runCount - 10);

            if (removeCount > 0)
            {
                for (int i = 0; i < removeCount; i++)
                {
                    if (_currentParagraph.Inlines.FirstInline != null)
                        _currentParagraph.Inlines.Remove(_currentParagraph.Inlines.FirstInline);
                }
                _lineCount -= removeCount / 2;
            }
        }

        private void QueueEntry(CustomConsole.LogEntry entry)
        {
            _pendingEntries.Enqueue(entry);
        }

        private void ScrollToBottom()
        {
            ConsoleTextBox.Dispatcher.InvokeAsync(() =>
            {
                try { ConsoleTextBox.ScrollToEnd(); }
                catch { }
            }, DispatcherPriority.Background);
        }

        private bool IsScrolledToBottom()
        {
            try
            {
                return ConsoleTextBox.VerticalOffset >= ConsoleTextBox.ExtentHeight - ConsoleTextBox.ViewportHeight - 10;
            }
            catch
            {
                return true;
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                        return;

                    if (current is FrameworkElement element)
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    else
                        break;
                }
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); }
                catch { }
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _currentParagraph.Inlines.Clear();
            _lineCount = 0;
        }

        private void BtnExportLog_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Debug Console Log",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"OceanyaDebugLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                DefaultExt = ".txt",
                AddExtension = true,
                OverwritePrompt = true,
            };

            bool? result = dialog.ShowDialog(HostWindow);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            TextRange visibleText = new TextRange(ConsoleTextBox.Document.ContentStart, ConsoleTextBox.Document.ContentEnd);
            File.WriteAllText(dialog.FileName, visibleText.Text);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
