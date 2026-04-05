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
        private sealed class LogState
        {
            public FlowDocument Document { get; } = new FlowDocument();

            public bool Inverted { get; set; }

            public Dictionary<Guid, Paragraph> TransientEntries { get; } = new Dictionary<Guid, Paragraph>();
        }

        private static int LogMaxMessages => Globals.LogMaxMessages;
        public Func<AOClient, AOClient?>? LogKeyResolver { get; set; }

        private readonly Dictionary<AOClient, LogState> clientLogs = new Dictionary<AOClient, LogState>();
        private AOClient? currentClient;
        private static bool InvertICLog { get; set; }

        private readonly List<FormatRule> formatRules = new()
        {
            new FormatRule { Name = ICMessage.TextColors.Green, Start = '`', End = '`', ColorBrush = new SolidColorBrush(Color.FromRgb(0, 247, 0)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Red, Start = '~', End = '~', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 0, 57)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Orange, Start = '|', End = '|', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 115, 57)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Blue, Start = '(', End = ')', ColorBrush = new SolidColorBrush(Color.FromRgb(107, 198, 247)), Remove = false },
            new FormatRule { Name = ICMessage.TextColors.Yellow, Start = 'º', End = 'º', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 247, 0)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Magenta, Start = '№', End = '№', ColorBrush = new SolidColorBrush(Color.FromRgb(247, 115, 247)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Cyan, Start = '√', End = '√', ColorBrush = new SolidColorBrush(Color.FromRgb(128, 247, 247)), Remove = true },
            new FormatRule { Name = ICMessage.TextColors.Gray, Start = '[', End = ']', ColorBrush = new SolidColorBrush(Color.FromRgb(160, 181, 205)), Remove = false }
        };

        private sealed class FormatRule
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

        public void SetCurrentClient(AOClient? client)
        {
            currentClient = client;
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                LogBox.Document = new FlowDocument();
                return;
            }

            LogBox.Document = EnsureLogState(logClient).Document;
            LogBox.ScrollToEnd();
        }

        public void AddMessage(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromSelf = false,
            ICMessage.TextColors textColor = ICMessage.TextColors.White,
            IReadOnlyList<LogMessageActionLink>? nameLinks = null,
            IReadOnlyList<LogMessageActionLink>? messageLinks = null)
        {
            AddMessageCore(client, showName, message, isSentFromSelf, textColor, nameLinks, messageLinks, transientHandle: null);
        }

        public LogMessageHandle AddTransientMessage(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromSelf = false,
            ICMessage.TextColors textColor = ICMessage.TextColors.White,
            IReadOnlyList<LogMessageActionLink>? nameLinks = null,
            IReadOnlyList<LogMessageActionLink>? messageLinks = null)
        {
            LogMessageHandle handle = new LogMessageHandle();
            AddMessageCore(client, showName, message, isSentFromSelf, textColor, nameLinks, messageLinks, handle);
            return handle;
        }

        public void UpdateTransientMessage(
            AOClient? client,
            LogMessageHandle handle,
            string showName,
            string message,
            bool isSentFromSelf = false,
            ICMessage.TextColors textColor = ICMessage.TextColors.White,
            IReadOnlyList<LogMessageActionLink>? nameLinks = null,
            IReadOnlyList<LogMessageActionLink>? messageLinks = null)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                return;
            }

            LogState state = EnsureLogState(logClient);
            if (!state.TransientEntries.TryGetValue(handle.Id, out Paragraph? paragraph))
            {
                AddMessageCore(client, showName, message, isSentFromSelf, textColor, nameLinks, messageLinks, handle);
                return;
            }

            PopulateParagraph(paragraph, showName, message, isSentFromSelf, textColor, nameLinks, messageLinks);

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = state.Document;
                ScrollToEndRespectingInversion();
            }
        }

        public void RemoveTransientMessage(AOClient? client, LogMessageHandle? handle)
        {
            if (handle == null)
            {
                return;
            }

            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null || !clientLogs.TryGetValue(logClient, out LogState? state))
            {
                return;
            }

            if (!state.TransientEntries.TryGetValue(handle.Id, out Paragraph? paragraph))
            {
                return;
            }

            state.TransientEntries.Remove(handle.Id);
            state.Document.Blocks.Remove(paragraph);

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = state.Document;
                ScrollToEndRespectingInversion();
            }
        }

        public void ClearClientLog(AOClient? client)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                return;
            }

            clientLogs[logClient] = new LogState
            {
                Inverted = InvertICLog
            };

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = clientLogs[logClient].Document;
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
            foreach (AOClient client in clientLogs.Keys.ToList())
            {
                LogState state = clientLogs[client];
                if (state.Inverted == isInverted)
                {
                    continue;
                }

                FlowDocument log = state.Document;
                List<Block> blocks = log.Blocks.ToList();
                log.Blocks.Clear();

                for (int index = blocks.Count - 1; index >= 0; index--)
                {
                    log.Blocks.Add(blocks[index]);
                }

                state.Inverted = isInverted;

                if (IsCurrentLogStream(client))
                {
                    LogBox.Document = log;
                    ScrollToEndRespectingInversion();
                }
            }
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

        private LogState EnsureLogState(AOClient client)
        {
            if (!clientLogs.TryGetValue(client, out LogState? state))
            {
                state = new LogState
                {
                    Inverted = InvertICLog
                };
                clientLogs[client] = state;
            }

            return state;
        }

        private bool IsCurrentLogStream(AOClient? client)
        {
            AOClient? currentLogClient = ResolveLogClient(currentClient);
            AOClient? messageLogClient = ResolveLogClient(client);
            return ReferenceEquals(currentLogClient, messageLogClient);
        }

        private void AddMessageCore(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks,
            LogMessageHandle? transientHandle)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                return;
            }

            LogState state = EnsureLogState(logClient);
            bool shouldScroll = IsScrolledToBottom();
            Paragraph paragraph = CreateParagraph(showName, message, isSentFromSelf, textColor, nameLinks, messageLinks);

            if (state.Inverted)
            {
                if (state.Document.Blocks.Count == 0)
                {
                    state.Document.Blocks.Add(paragraph);
                }
                else
                {
                    state.Document.Blocks.InsertBefore(state.Document.Blocks.FirstBlock, paragraph);
                }
            }
            else
            {
                state.Document.Blocks.Add(paragraph);
            }

            if (transientHandle != null)
            {
                state.TransientEntries[transientHandle.Id] = paragraph;
            }

            TrimLog(state);

            if (IsCurrentLogStream(client) && shouldScroll)
            {
                LogBox.Document = state.Document;
                ScrollToEndRespectingInversion();
            }
        }

        private Paragraph CreateParagraph(
            string showName,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks)
        {
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2),
                LineHeight = 2
            };
            PopulateParagraph(paragraph, showName, message, isSentFromSelf, textColor, nameLinks, messageLinks);
            return paragraph;
        }

        private void PopulateParagraph(
            Paragraph paragraph,
            string showName,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks)
        {
            paragraph.Inlines.Clear();

            Brush nameBrush;
            if (isSentFromSelf)
            {
                Run gmTag = new Run("[GM] ")
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = formatRules.First(rule => rule.Name == ICMessage.TextColors.Gray).ColorBrush
                };
                paragraph.Inlines.Add(gmTag);
                nameBrush = new SolidColorBrush(Color.FromArgb(255, 154, 220, 225));
            }
            else
            {
                nameBrush = Brushes.White;
            }

            Run nameRun = new Run(showName ?? string.Empty)
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush
            };
            paragraph.Inlines.Add(nameRun);
            AppendActionLinks(paragraph, nameLinks);

            Run suffixRun = new Run(": ")
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush
            };
            paragraph.Inlines.Add(suffixRun);

            foreach (Inline inline in FormatMessageText(message ?? string.Empty, textColor))
            {
                paragraph.Inlines.Add(inline);
            }

            AppendActionLinks(paragraph, messageLinks);
        }

        private void AppendActionLinks(Paragraph paragraph, IReadOnlyList<LogMessageActionLink>? links)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            foreach (LogMessageActionLink link in links)
            {
                paragraph.Inlines.Add(new Run(" "));
                Hyperlink hyperlink = new Hyperlink(new Run(link.Text))
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 206, 255)),
                    TextDecorations = TextDecorations.Underline,
                    ToolTip = string.IsNullOrWhiteSpace(link.ToolTip) ? null : link.ToolTip
                };
                hyperlink.Click += (_, _) => link.OnClick();
                paragraph.Inlines.Add(hyperlink);
            }
        }

        private void TrimLog(LogState state)
        {
            if (LogMaxMessages == 0 || state.Document.Blocks.Count <= LogMaxMessages)
            {
                return;
            }

            while (state.Document.Blocks.Count > LogMaxMessages)
            {
                Block? blockToRemove = state.Inverted
                    ? state.Document.Blocks.LastBlock
                    : state.Document.Blocks.FirstBlock;
                if (blockToRemove == null)
                {
                    return;
                }

                RemoveTrackedBlock(state, blockToRemove);
            }
        }

        private void RemoveTrackedBlock(LogState state, Block block)
        {
            if (block is Paragraph paragraph)
            {
                Guid? trackedId = state.TransientEntries
                    .FirstOrDefault(pair => ReferenceEquals(pair.Value, paragraph))
                    .Key;
                if (trackedId.HasValue && trackedId.Value != Guid.Empty)
                {
                    state.TransientEntries.Remove(trackedId.Value);
                }
            }

            state.Document.Blocks.Remove(block);
        }

        private bool IsScrolledToBottom()
        {
            if (ScrollViewer == null)
            {
                return true;
            }

            return InvertICLog
                ? ScrollViewer.VerticalOffset == 0
                : ScrollViewer.VerticalOffset >= ScrollViewer.ScrollableHeight - 10;
        }

        private void ScrollToEndRespectingInversion()
        {
            if (ScrollViewer == null)
            {
                return;
            }

            ScrollViewer.Dispatcher.InvokeAsync(() =>
            {
                if (InvertICLog)
                {
                    ScrollViewer.ScrollToTop();
                }
                else
                {
                    ScrollViewer.ScrollToEnd();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private List<Inline> FormatMessageText(string message, ICMessage.TextColors defaultColor)
        {
            List<Inline> formattedRuns = new List<Inline>();
            List<(string Text, ICMessage.TextColors Color)> segments = new List<(string Text, ICMessage.TextColors Color)>();
            Stack<(ICMessage.TextColors Color, char EndMarker)> colorStack = new Stack<(ICMessage.TextColors Color, char EndMarker)>();
            colorStack.Push((defaultColor, '\0'));

            StringBuilder currentText = new StringBuilder();

            int index = 0;
            while (index < message.Length)
            {
                char current = message[index];
                bool isProcessed = false;
                FormatRule? markerRule = formatRules.FirstOrDefault(rule => rule.Start == current || rule.End == current);

                if (markerRule != null)
                {
                    if (colorStack.Count > 1 && current == colorStack.Peek().EndMarker)
                    {
                        if (currentText.Length > 0)
                        {
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        if (!markerRule.Remove)
                        {
                            currentText.Append(current);
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        colorStack.Pop();
                        isProcessed = true;
                    }
                    else if (current == markerRule.Start)
                    {
                        if (currentText.Length > 0)
                        {
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        colorStack.Push((markerRule.Name, markerRule.End));
                        if (!markerRule.Remove)
                        {
                            currentText.Append(current);
                            segments.Add((currentText.ToString(), colorStack.Peek().Color));
                            currentText.Clear();
                        }

                        isProcessed = true;
                    }
                }

                if (!isProcessed)
                {
                    currentText.Append(current);
                }

                index++;
            }

            if (currentText.Length > 0)
            {
                segments.Add((currentText.ToString(), colorStack.Peek().Color));
            }

            foreach ((string Text, ICMessage.TextColors Color) segment in segments)
            {
                if (string.IsNullOrEmpty(segment.Text))
                {
                    continue;
                }

                Brush brush = segment.Color == ICMessage.TextColors.White
                    ? Brushes.White
                    : formatRules.First(rule => rule.Name == segment.Color).ColorBrush;
                formattedRuns.Add(new Run(segment.Text) { Foreground = brush });
            }

            return formattedRuns;
        }
    }
}
