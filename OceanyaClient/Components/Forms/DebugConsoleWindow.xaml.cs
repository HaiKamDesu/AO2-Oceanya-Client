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
using System.Windows.Threading;

namespace OceanyaClient
{
    public partial class DebugConsoleWindow : Window
    {
        private static DebugConsoleWindow? _instance;
        private static readonly object _instanceLock = new object();

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

        private void AddMessageToDocument(string text)
        {
            if (text == null) return;

            // Limit document size by removing old lines if needed
            if (_lineCount >= MAX_DOCUMENT_LINES)
            {
                TrimDocument();
            }

            // Create a new Run with the text and transparent background
            Run run = new Run(text + Environment.NewLine);
            run.Background = Brushes.Transparent; // Critical: ensure run has transparent background

            // Apply color based on message content
            if (text.Contains("ERROR") || text.Contains("EXCEPTION"))
            {
                run.Foreground = Brushes.Red;
            }
            else if (text.Contains("WARNING"))
            {
                run.Foreground = Brushes.Yellow;
            }
            else if (text.Contains("SUCCESS"))
            {
                run.Foreground = Brushes.LightGreen;
            }
            else if (text.StartsWith("[") && text.Contains("]")) // Timestamped messages
            {
                run.Foreground = Brushes.LightCyan;
            }

            _currentParagraph.Inlines.Add(run);
            _lineCount++;
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
