using Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        // Use a concurrent queue to buffer messages during initialization
        private ConcurrentQueue<string> _pendingMessages = new ConcurrentQueue<string>();

        // Document objects
        private FlowDocument _consoleDocument = null!;
        private Paragraph _currentParagraph = null!;

        // Buffer settings
        private const int MAX_DOCUMENT_LINES = 5000; // Adjust based on memory constraints
        private int _lineCount = 0;

        // Batch update control
        private DispatcherTimer? _updateTimer;
        private const int UPDATE_INTERVAL_MS = 100; // Adjust based on UI responsiveness needs

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

            // Initialize document
            InitializeConsoleDocument();

            // Start update timer for batched updates
            InitializeUpdateTimer();

            // Subscribe to console messages
            CustomConsole.OnWriteLine += QueueMessage;
        }

        private void InitializeConsoleDocument()
        {
            _consoleDocument = new FlowDocument();
            _consoleDocument.PageWidth = double.NaN; // Auto width

            // Critical: Set document background to transparent
            _consoleDocument.Background = Brushes.Transparent;
            _consoleDocument.Foreground = Brushes.LightGray;
            _consoleDocument.FontFamily = new FontFamily("Consolas");
            _consoleDocument.FontSize = 12;
            _consoleDocument.TextAlignment = TextAlignment.Left;

            // Create initial paragraph with transparent background
            _currentParagraph = new Paragraph();
            _currentParagraph.Background = Brushes.Transparent;
            _currentParagraph.LineHeight = 1.0;
            _currentParagraph.Margin = new Thickness(0);
            _consoleDocument.Blocks.Add(_currentParagraph);

            // Apply document to RichTextBox
            ConsoleTextBox.Document = _consoleDocument;
        }

        private void InitializeUpdateTimer()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(UPDATE_INTERVAL_MS);
            _updateTimer.Tick += (s, e) => ProcessPendingMessages();
            _updateTimer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a snapshot of the current lines to avoid modification during enumeration
                List<string> linesCopy;

                try
                {
                    linesCopy = new List<string>(CustomConsole.lines);
                }
                catch (Exception ex)
                {
                    AddMessageToDocument($"Error copying console lines: {ex.Message}");
                    linesCopy = new List<string>();
                }

                // Bulk add all existing messages
                if (linesCopy.Count > 0)
                {
                    // For very large history, we might want to only load the last N messages
                    int startIndex = Math.Max(0, linesCopy.Count - MAX_DOCUMENT_LINES);
                    for (int i = startIndex; i < linesCopy.Count; i++)
                    {
                        AddMessageToDocument(linesCopy[i]);
                    }

                    // Force scroll to end after initial load
                    ScrollToBottom();
                }

                // Process any messages that came in during initialization
                ProcessPendingMessages();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading console history: {ex.Message}", "Debug Console Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Stop processing updates
            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer = null;
            }

            // Unsubscribe from events
            CustomConsole.OnWriteLine -= QueueMessage;

            lock (_instanceLock)
            {
                _instance = null;
            }
        }

        private void ProcessPendingMessages()
        {
            if (_pendingMessages.IsEmpty)
                return;

            // Process pending messages in batches
            int batchSize = 100; // Process up to 100 messages per tick
            int processed = 0;

            while (_pendingMessages.TryDequeue(out string? message) && processed < batchSize)
            {
                if (message != null)
                {
                    AddMessageToDocument(message);
                    processed++;
                }
            }

            // If we processed any messages and we're at the bottom, scroll down
            if (processed > 0 && IsScrolledToBottom())
            {
                ScrollToBottom();
            }
        }

        // Segment brushes
        private static readonly Brush TimestampBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x66));
        private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
        private static readonly Brush DebugBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x33));
        private static readonly Brush AiPrefixBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0xD9, 0xE8));
        private static readonly Brush LevelIndicatorBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private static readonly Brush ContinuationBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x77, 0x77));

        private void AddMessageToDocument(string text)
        {
            if (text == null) return;

            if (_lineCount >= MAX_DOCUMENT_LINES)
            {
                TrimDocument();
            }

            AppendColorizedLine(text);
            _lineCount++;
        }

        private void AppendColorizedLine(string text)
        {
            string remaining = text;

            // Extract timestamp prefix "[YYYY-MM-DD HH:mm:ss]"
            if (remaining.Length > 2 && remaining[0] == '[')
            {
                int closeBracket = remaining.IndexOf(']', 1);
                if (closeBracket > 0 && closeBracket <= 22)
                {
                    AppendRun(remaining.Substring(0, closeBracket + 1), TimestampBrush);
                    remaining = remaining.Substring(closeBracket + 1);
                }
            }

            // Detect log level from leading emoji pattern " ❌", " ⚠️", " 🔍", " ℹ️"
            Brush bodyBrush;
            string emojiPrefix;

            if (remaining.StartsWith(" ❌"))
            {
                emojiPrefix = " ❌";
                bodyBrush = ErrorBrush;
            }
            else if (remaining.StartsWith(" ⚠️"))
            {
                emojiPrefix = " ⚠️";
                bodyBrush = WarningBrush;
            }
            else if (remaining.StartsWith(" 🔍"))
            {
                emojiPrefix = " 🔍";
                bodyBrush = DebugBrush;
            }
            else if (remaining.StartsWith(" ℹ️"))
            {
                emojiPrefix = " ℹ️";
                bodyBrush = InfoBrush;
            }
            else
            {
                // Exception detail lines ("   Exception:", "   Message:", etc.) or plain lines
                AppendRun(remaining + Environment.NewLine, ContinuationBrush);
                return;
            }

            AppendRun(emojiPrefix, LevelIndicatorBrush);
            remaining = remaining.Substring(emojiPrefix.Length);

            // Check for [AI:name] prefix after the emoji
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
            // Remove older lines when we hit the limit
            // This is more efficient than removing one line at a time
            int linesToRemove = MAX_DOCUMENT_LINES / 5; // Remove 20% of max lines

            // Since we're using a single paragraph, we need to remove the first N runs
            int runCount = _currentParagraph.Inlines.Count;
            int removeCount = Math.Min(linesToRemove * 2, runCount - 10); // Each line is text + newline

            if (removeCount > 0)
            {
                for (int i = 0; i < removeCount; i++)
                {
                    if (_currentParagraph.Inlines.FirstInline != null)
                    {
                        _currentParagraph.Inlines.Remove(_currentParagraph.Inlines.FirstInline);
                    }
                }

                _lineCount -= removeCount / 2; // Account for newlines in the count
            }
        }

        private void QueueMessage(string text)
        {
            // Queue the message for processing in the UI thread
            if (!string.IsNullOrEmpty(text))
            {
                _pendingMessages.Enqueue(text);
            }
        }

        private void ScrollToBottom()
        {
            if (ConsoleTextBox != null)
            {
                ConsoleTextBox.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        ConsoleTextBox.ScrollToEnd();
                    }
                    catch (Exception)
                    {
                        // Silently handle scroll errors
                    }
                }, DispatcherPriority.Background);
            }
        }

        private bool IsScrolledToBottom()
        {
            try
            {
                return ConsoleTextBox.VerticalOffset >= ConsoleTextBox.ExtentHeight - ConsoleTextBox.ViewportHeight - 10;
            }
            catch (Exception)
            {
                return true; // Default to scrolling if we can't determine the position
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (current is FrameworkElement element)
                    {
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch (Exception)
                {
                    // Ignore drag exceptions
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
