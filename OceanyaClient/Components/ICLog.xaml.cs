using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OceanyaClient.Components
{
    public partial class ICLog : UserControl
    {
        private static int LogMaxMessages => Globals.LogMaxMessages;
        public Func<AOClient, AOClient?>? LogKeyResolver { get; set; }

        private Dictionary<AOClient, (FlowDocument log, bool inverted)> clientLogs = new();
        private AOClient? currentClient = null;
        static bool InvertICLog { get; set; } = false;

        private readonly List<FormatRule> formatRules = new()
        {
            new FormatRule { Name = ICMessage.TextColors.Green,    Start = '`', End = '`', ColorBrush = new SolidColorBrush(Color.FromRgb(0, 247, 0)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Red,      Start = '~', End = '~', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 0, 57)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Orange,   Start = '|', End = '|', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 115, 57)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Blue,     Start = '(', End = ')', ColorBrush = new SolidColorBrush(Color.FromRgb(107, 198, 247)), Remove = false },
            new FormatRule { Name = ICMessage.TextColors.Yellow,   Start = 'º', End = 'º', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 247, 0)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Magenta,  Start = '№', End = '№', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 115, 247)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Cyan,     Start = '√', End = '√', ColorBrush = new SolidColorBrush(Color.FromRgb(128, 247, 247)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Gray,     Start = '[', End = ']', ColorBrush = new SolidColorBrush(Color.FromRgb(160, 181, 205)), Remove = false }
        };
        private class FormatRule
        {
            public ICMessage.TextColors Name { get; set; }
            public char Start { get; set; }
            public char End { get; set; }
            public SolidColorBrush ColorBrush { get; set; } = new SolidColorBrush(Colors.White);
            public bool Remove { get; set; }
        }
        public ICLog()
        {
            InitializeComponent();
            LogBox.Document.Blocks.Clear();
        }

        private AOClient? ResolveLogClient(AOClient? client)
        {
            if (client == null)
            {
                return null;
            }

            if (LogKeyResolver == null)
            {
                return client;
            }

            AOClient? resolvedClient = LogKeyResolver(client);
            return resolvedClient ?? client;
        }

        private bool IsCurrentLogStream(AOClient? client)
        {
            AOClient? currentLogClient = ResolveLogClient(currentClient);
            AOClient? messageLogClient = ResolveLogClient(client);
            return ReferenceEquals(currentLogClient, messageLogClient);
        }

        public void SetCurrentClient(AOClient? client)
        {
            currentClient = client;
            AOClient? logClient = ResolveLogClient(client);

            if (logClient == null)
            {
                LogBox.Document = new FlowDocument();
                return;
            }

            if (!clientLogs.ContainsKey(logClient))
            {
                clientLogs[logClient] = (new FlowDocument(), InvertICLog);
            }

            LogBox.Document = clientLogs[logClient].log;
            LogBox.ScrollToEnd();
        }

        public void AddMessage(AOClient? client, string showName, string message, bool isSentFromSelf = false, ICMessage.TextColors textColor = ICMessage.TextColors.White)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                return;
            }

            if (!clientLogs.ContainsKey(logClient))
            {
                clientLogs[logClient] = (new FlowDocument(), InvertICLog);
            }

            bool shouldScroll = IsScrolledToBottom();

            FlowDocument log = clientLogs[logClient].log;

            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2),
                LineHeight = 2
            };

            if (isSentFromSelf)
            {
                Run gmTag = new Run("[GM] ") { FontWeight = FontWeights.Bold };
                gmTag.Foreground = formatRules.First(x => x.Name == ICMessage.TextColors.Gray).ColorBrush;
                paragraph.Inlines.Add(gmTag);

                Run nameRun = new Run($"{showName}: ") { FontWeight = FontWeights.Bold };
                nameRun.Foreground = new SolidColorBrush(Color.FromArgb(255, 154, 220, 225));
                paragraph.Inlines.Add(nameRun);
            }
            else
            {
                Run nameRun = new Run($"{showName}: ") { FontWeight = FontWeights.Bold };
                nameRun.Foreground = Brushes.White;
                paragraph.Inlines.Add(nameRun);
            }

            paragraph.Inlines.AddRange(FormatMessageText(message, textColor));

            // Add the new paragraph based on whether the log is inverted.
            if (clientLogs[logClient].inverted)
            {
                if (log.Blocks.Count == 0)
                    log.Blocks.Add(paragraph);
                else
                    log.Blocks.InsertBefore(log.Blocks.FirstBlock, paragraph);

                // For inverted logs, remove the oldest block (at the bottom) if limit is exceeded.
                if (LogMaxMessages != 0 && log.Blocks.Count > LogMaxMessages)
                {
                    while (log.Blocks.Count > LogMaxMessages)
                    {
                        log.Blocks.Remove(log.Blocks.LastBlock);
                    }
                }
            }
            else
            {
                log.Blocks.Add(paragraph);

                // For non-inverted logs, remove the oldest block (at the top) if limit is exceeded.
                if (LogMaxMessages != 0 && log.Blocks.Count > LogMaxMessages)
                {
                    while (log.Blocks.Count > LogMaxMessages)
                    {
                        log.Blocks.Remove(log.Blocks.FirstBlock);
                    }
                }
            }

            if (IsCurrentLogStream(client) && shouldScroll)
            {
                ScrollToEndRespectingInversion();
            }
        }

        private bool IsScrolledToBottom()
        {
            if (ScrollViewer == null) return true;

            if (InvertICLog)
                return ScrollViewer.VerticalOffset == 0; // Top for inverted
            else
                return ScrollViewer.VerticalOffset >= ScrollViewer.ScrollableHeight - 10; // Bottom for normal
        }

        private void ScrollToEndRespectingInversion()
        {
            if (ScrollViewer == null) return;

            ScrollViewer.Dispatcher.InvokeAsync(() =>
            {
                if (InvertICLog)
                    ScrollViewer.ScrollToTop();
                else
                    ScrollViewer.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private class MessageSection
        {
            public string content;
            public ICMessage.TextColors color;
            public bool finished;

            public MessageSection(string content, ICMessage.TextColors color, bool finished)
            {
                this.content = content;
                this.color = color;
                this.finished = finished;
            }
        }

        private List<Inline> FormatMessageText(string message, ICMessage.TextColors defaultColor)
        {
            var formattedRuns = new List<Inline>();

            // Final output
            List<(string Text, ICMessage.TextColors Color)> segments = new();

            // Stack to track nested colors and their end markers
            Stack<(ICMessage.TextColors Color, char EndMarker)> colorStack = new();
            colorStack.Push((defaultColor, '\0')); // Default color has no end marker

            StringBuilder currentText = new();

            int i = 0;
            while (i < message.Length)
            {
                char c = message[i];
                bool isProcessed = false;

                // Determine if this character is a format marker
                var markerRule = formatRules.FirstOrDefault(r => r.Start == c || r.End == c);

                if (markerRule != null)
                {
                    // Check if this is an END marker for the CURRENT top color
                    if (colorStack.Count > 1 && c == colorStack.Peek().EndMarker)
                    {
                        // Complete current segment with current color
                        if (currentText.Length > 0)
                        {
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        // Include the marker in output if not removed
                        if (!markerRule.Remove)
                        {
                            currentText.Append(c);
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        // Pop the color stack
                        colorStack.Pop();

                        isProcessed = true;
                    }
                    // Check if this is a START marker for a new color
                    else if (c == markerRule.Start)
                    {
                        // Complete current segment with current color
                        if (currentText.Length > 0)
                        {
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        // Push new color onto stack
                        colorStack.Push((markerRule.Name, markerRule.End));

                        // Include the marker in output if not removed
                        if (!markerRule.Remove)
                        {
                            currentText.Append(c);
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        isProcessed = true;
                    }
                }

                // If not processed as a marker, add as regular text
                if (!isProcessed)
                {
                    currentText.Append(c);
                }

                i++;
            }

            // Add any remaining text
            if (currentText.Length > 0)
            {
                segments.Add((currentText.ToString(), colorStack.Peek().Color));
            }

            // Convert segments to runs
            foreach (var segment in segments)
            {
                if (!string.IsNullOrEmpty(segment.Text))
                {
                    Brush brush;
                    if (segment.Color == ICMessage.TextColors.White)
                    {
                        brush = Brushes.White;
                    }
                    else
                    {
                        brush = formatRules.First(r => r.Name == segment.Color).ColorBrush;
                    }

                    formattedRuns.Add(new Run(segment.Text) { Foreground = brush });
                }
            }

            return formattedRuns;
        }

        public void ClearClientLog(AOClient? client)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient != null && clientLogs.ContainsKey(logClient))
            {
                clientLogs[logClient] = (new FlowDocument(), InvertICLog);

                if (IsCurrentLogStream(client))
                    LogBox.Document = clientLogs[logClient].log;
            }
        }

        public void ClearAllLogs()
        {
            clientLogs.Clear();
            LogBox.Document.Blocks.Clear();
        }

        public AOClient? GetCurrentClient()
        {
            return currentClient;
        }

        public void SetInvertOnClientLogs(bool isInverted)
        {
            InvertICLog = isInverted;
            foreach (var client in clientLogs.Keys.ToList())
            {
                if (clientLogs[client].inverted != isInverted)
                {
                    FlowDocument log = clientLogs[client].log;
                    var blocks = log.Blocks.ToList();
                    log.Blocks.Clear();

                    for (int i = blocks.Count - 1; i >= 0; i--)
                        log.Blocks.Add(blocks[i]);

                    clientLogs[client] = (log, isInverted);

                    if (IsCurrentLogStream(client))
                    {
                        LogBox.Document = log;
                        ScrollToEndRespectingInversion();
                    }
                }
            }
        }
    }

    
}
