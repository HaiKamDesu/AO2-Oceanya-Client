using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using OceanyaClient.Components.Forms;
using OceanyaClient.Features.ChatPreview;

namespace OceanyaClient.Components
{
    public partial class ICLog : UserControl, ILogFindTarget
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

        private TextRange? activeHighlight;
        private FindInLogWindow? findWindow;
        private readonly List<TextRange> activeSearchHighlights = new List<TextRange>();
        private IReadOnlyList<LogTextMatch> activeSearchMatches = Array.Empty<LogTextMatch>();
        private int activeSearchMatchIndex = -1;
        public Func<IReadOnlyList<ILogFindTarget>>? FindTargetsProvider { get; set; }
        public string FindScopeName => "IC";

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
            IReadOnlyList<LogMessageActionLink>? messageLinks = null,
            bool useAo2Formatting = false)
        {
            AddMessageCore(
                client,
                showName,
                message,
                isSentFromSelf,
                textColor,
                nameLinks,
                messageLinks,
                useAo2Formatting,
                transientHandle: null);
        }

        public void AddActionMessage(
            AOClient? client,
            string showName,
            string action,
            string message = "",
            bool isSentFromSelf = false,
            ICMessage.TextColors textColor = ICMessage.TextColors.White)
        {
            AddActionMessageCore(client, showName, action, message, isSentFromSelf, textColor, transientHandle: null);
        }

        public LogMessageHandle AddTransientMessage(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromSelf = false,
            ICMessage.TextColors textColor = ICMessage.TextColors.White,
            IReadOnlyList<LogMessageActionLink>? nameLinks = null,
            IReadOnlyList<LogMessageActionLink>? messageLinks = null,
            bool useAo2Formatting = false)
        {
            LogMessageHandle handle = new LogMessageHandle();
            AddMessageCore(
                client,
                showName,
                message,
                isSentFromSelf,
                textColor,
                nameLinks,
                messageLinks,
                useAo2Formatting,
                handle);
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
            IReadOnlyList<LogMessageActionLink>? messageLinks = null,
            bool useAo2Formatting = false)
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
                AddMessageCore(
                    client,
                    showName,
                    message,
                    isSentFromSelf,
                    textColor,
                    nameLinks,
                    messageLinks,
                    useAo2Formatting,
                    handle);
                return;
            }

            PopulateParagraph(
                paragraph,
                showName,
                message,
                isSentFromSelf,
                textColor,
                nameLinks,
                messageLinks,
                useAo2Formatting);

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
            bool useAo2Formatting,
            LogMessageHandle? transientHandle)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                return;
            }

            LogState state = EnsureLogState(logClient);
            bool shouldScroll = IsScrolledToBottom();
            Paragraph paragraph = CreateParagraph(
                showName,
                message,
                isSentFromSelf,
                textColor,
                nameLinks,
                messageLinks,
                useAo2Formatting);

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

        private void AddActionMessageCore(
            AOClient? client,
            string showName,
            string action,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor,
            LogMessageHandle? transientHandle)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                return;
            }

            LogState state = EnsureLogState(logClient);
            bool shouldScroll = IsScrolledToBottom();
            Paragraph paragraph = CreateActionParagraph(showName, action, message, isSentFromSelf, textColor);

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
            IReadOnlyList<LogMessageActionLink>? messageLinks,
            bool useAo2Formatting)
        {
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2),
                LineHeight = 2
            };
            PopulateParagraph(
                paragraph,
                showName,
                message,
                isSentFromSelf,
                textColor,
                nameLinks,
                messageLinks,
                useAo2Formatting);
            return paragraph;
        }

        private Paragraph CreateActionParagraph(
            string showName,
            string action,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor)
        {
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2),
                LineHeight = 2
            };
            PopulateActionParagraph(paragraph, showName, action, message, isSentFromSelf, textColor);
            return paragraph;
        }

        private void PopulateParagraph(
            Paragraph paragraph,
            string showName,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks,
            bool useAo2Formatting)
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

            foreach (Inline inline in FormatMessageText(message ?? string.Empty, textColor, useAo2Formatting))
            {
                paragraph.Inlines.Add(inline);
            }

            AppendActionLinks(paragraph, messageLinks);
        }

        private void PopulateActionParagraph(
            Paragraph paragraph,
            string showName,
            string action,
            string message,
            bool isSentFromSelf,
            ICMessage.TextColors textColor)
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

            paragraph.Inlines.Add(new Run(showName ?? string.Empty)
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush
            });

            paragraph.Inlines.Add(new Run(" ")
            {
                Foreground = Brushes.White
            });

            paragraph.Inlines.Add(new Run(action ?? string.Empty)
            {
                Foreground = Brushes.White
            });

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            paragraph.Inlines.Add(new Run(" ")
            {
                Foreground = Brushes.White
            });

            bool emphasizeMessage = string.Equals(action, "shouts", StringComparison.OrdinalIgnoreCase);
            foreach (Inline inline in FormatPlainText(message ?? string.Empty, textColor))
            {
                if (emphasizeMessage)
                {
                    inline.FontWeight = FontWeights.Bold;
                }

                paragraph.Inlines.Add(inline);
            }
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

        private List<Inline> FormatMessageText(
            string message,
            ICMessage.TextColors defaultColor,
            bool useAo2Formatting)
        {
            return useAo2Formatting
                ? FormatAo2MessageText(message, defaultColor)
                : FormatPlainText(message, defaultColor);
        }

        private List<Inline> FormatPlainText(string message, ICMessage.TextColors defaultColor)
        {
            Brush brush = GetFallbackBrush(defaultColor);
            return new List<Inline>
            {
                new Run(message ?? string.Empty)
                {
                    Foreground = brush
                }
            };
        }

        private List<Inline> FormatAo2MessageText(string message, ICMessage.TextColors defaultColor)
        {
            List<Inline> formattedRuns = new List<Inline>();
            AO2ChatPreviewStyle style = AO2ChatPreviewResolver.Resolve(
                "default",
                hasShowname: true,
                preferViewportTheme: true);
            int defaultColorIndex = Math.Clamp((int)defaultColor, 0, style.ChatColors.Length - 1);
            Color fallbackDefaultColor = ToMediaColor(ICMessage.GetColorFromTextColor(defaultColor));
            foreach (AO2FormattedTextSegment segment in AO2ChatTextFormatter.EnumerateFormattedTextSegments(
                style,
                message ?? string.Empty,
                defaultColorIndex,
                fallbackDefaultColor))
            {
                if (string.IsNullOrEmpty(segment.Text))
                {
                    continue;
                }

                formattedRuns.Add(new Run(segment.Text) { Foreground = new SolidColorBrush(segment.Color) });
            }

            return formattedRuns;
        }

        private Brush GetFallbackBrush(ICMessage.TextColors color)
        {
            return color == ICMessage.TextColors.White
                ? Brushes.White
                : formatRules.FirstOrDefault(rule => rule.Name == color)?.ColorBrush ?? Brushes.White;
        }

        private static Color ToMediaColor(System.Drawing.Color color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public IReadOnlyList<LogTextMatch> FindInCurrentDocument(
            string searchText, bool matchCase, bool wholeWord, bool useRegex)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                return Array.Empty<LogTextMatch>();
            }

            return LogDocumentSearch.Find(LogBox.Document, searchText, matchCase, wholeWord, useRegex);
        }

        public LogDocumentSearch.DocumentTextIndex CreateFindIndex()
        {
            return LogDocumentSearch.CreateIndex(LogBox.Document);
        }

        public IReadOnlyList<LogTextMatch> ResolveFindMatches(
            LogDocumentSearch.DocumentTextIndex index,
            IReadOnlyList<LogTextOffsetMatch> matches)
        {
            return LogDocumentSearch.ResolveMatches(index, matches);
        }

        internal void ScrollAndHighlightRange(TextPointer start, TextPointer end)
        {
            HighlightMatches(new[] { new LogTextMatch(start, end) }, 0);
        }

        public void HighlightMatches(IReadOnlyList<LogTextMatch> matches, int activeMatchIndex)
        {
            if (AreSameMatches(activeSearchMatches, matches))
            {
                UpdateActiveHighlight(activeMatchIndex);
                return;
            }

            ClearHighlight();
            activeSearchMatches = matches.ToArray();
            activeSearchMatchIndex = -1;

            for (int i = 0; i < matches.Count; i++)
            {
                try
                {
                    TextRange range = new TextRange(matches[i].Start, matches[i].End);
                    bool isActive = i == activeMatchIndex;
                    range.ApplyPropertyValue(
                        TextElement.BackgroundProperty,
                        isActive ? LogFindHighlightBrushes.ActiveMatch : LogFindHighlightBrushes.Match);
                    activeSearchHighlights.Add(range);

                    if (isActive)
                    {
                        activeHighlight = range;
                        activeSearchMatchIndex = i;
                        matches[i].Start.Paragraph?.BringIntoView();
                    }
                }
                catch
                {
                }
            }
        }

        public async Task HighlightMatchesAsync(
            IReadOnlyList<LogTextMatch> matches,
            int activeMatchIndex,
            CancellationToken cancellationToken)
        {
            if (AreSameMatches(activeSearchMatches, matches))
            {
                UpdateActiveHighlight(activeMatchIndex);
                return;
            }

            ClearHighlight();
            activeSearchMatches = matches.ToArray();
            activeSearchMatchIndex = -1;

            for (int i = 0; i < matches.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    TextRange range = new TextRange(matches[i].Start, matches[i].End);
                    bool isActive = i == activeMatchIndex;
                    range.ApplyPropertyValue(
                        TextElement.BackgroundProperty,
                        isActive ? LogFindHighlightBrushes.ActiveMatch : LogFindHighlightBrushes.Match);
                    activeSearchHighlights.Add(range);

                    if (isActive)
                    {
                        activeHighlight = range;
                        activeSearchMatchIndex = i;
                        matches[i].Start.Paragraph?.BringIntoView();
                    }
                }
                catch
                {
                }

                if ((i + 1) % 50 == 0)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
        }

        private void UpdateActiveHighlight(int activeMatchIndex)
        {
            if (activeSearchMatchIndex == activeMatchIndex)
            {
                if (activeMatchIndex >= 0 && activeMatchIndex < activeSearchMatches.Count)
                {
                    activeSearchMatches[activeMatchIndex].Start.Paragraph?.BringIntoView();
                }
                return;
            }

            ApplyHighlightBrush(activeSearchMatchIndex, LogFindHighlightBrushes.Match);
            ApplyHighlightBrush(activeMatchIndex, LogFindHighlightBrushes.ActiveMatch);
            activeSearchMatchIndex = activeMatchIndex;
            activeHighlight = activeMatchIndex >= 0 && activeMatchIndex < activeSearchHighlights.Count
                ? activeSearchHighlights[activeMatchIndex]
                : null;

            if (activeMatchIndex >= 0 && activeMatchIndex < activeSearchMatches.Count)
            {
                activeSearchMatches[activeMatchIndex].Start.Paragraph?.BringIntoView();
            }
        }

        private void ApplyHighlightBrush(int index, Brush brush)
        {
            if (index < 0 || index >= activeSearchHighlights.Count)
            {
                return;
            }

            try
            {
                activeSearchHighlights[index].ApplyPropertyValue(TextElement.BackgroundProperty, brush);
            }
            catch
            {
            }
        }

        private static bool AreSameMatches(IReadOnlyList<LogTextMatch> left, IReadOnlyList<LogTextMatch> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Start.CompareTo(right[i].Start) != 0
                    || left[i].End.CompareTo(right[i].End) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        public void ClearHighlight()
        {
            foreach (TextRange range in activeSearchHighlights)
            {
                try
                {
                    range.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                }
                catch { }
            }

            activeSearchHighlights.Clear();
            activeSearchMatches = Array.Empty<LogTextMatch>();
            activeSearchMatchIndex = -1;
            activeHighlight = null;
        }

        private void MenuItemFindInLog_Click(object sender, RoutedEventArgs e)
        {
            if (findWindow?.HostWindow?.IsVisible == true)
            {
                findWindow.HostWindow.Activate();
                return;
            }

            IReadOnlyList<ILogFindTarget> targets = FindTargetsProvider?.Invoke() ?? new ILogFindTarget[] { this };
            findWindow = new FindInLogWindow(targets, this);
            Window hostWindow = OceanyaWindowManager.CreateWindow(findWindow);
            hostWindow.Owner = Window.GetWindow(this);
            hostWindow.Closed += (_, _) =>
            {
                foreach (ILogFindTarget target in targets)
                {
                    target.ClearHighlight();
                }
                findWindow = null;
            };
            hostWindow.Show();
        }
    }
}
