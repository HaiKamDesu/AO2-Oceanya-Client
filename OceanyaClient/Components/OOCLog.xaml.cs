using AOBot_Testing.Agents;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using OceanyaClient.Components.Forms;
using OceanyaClient.Features.Chat;

namespace OceanyaClient.Components
{
    public partial class OOCLog : UserControl, ILogFindTarget
    {
        private sealed class LogState
        {
            public FlowDocument Document { get; }
            public Border BottomSpacer { get; }
            public Dictionary<Guid, Paragraph> TransientEntries { get; } = new Dictionary<Guid, Paragraph>();

            public LogState()
            {
                Document = new FlowDocument();
                BottomSpacer = new Border { Height = 0 };
                Document.Blocks.Add(new BlockUIContainer(BottomSpacer));
            }
        }

        public static int OOCShownameLengthLimit = 30;
        public Action<string, string>? OnSendOOCMessage;
        private FindInAllLogsWindow? findInAllLogsWindow;
        public Func<AOClient, AOClient?>? LogKeyResolver { get; set; }
        public Func<IReadOnlyList<ILogFindTarget>>? FindTargetsProvider { get; set; }
        public string FindScopeName => "OOC";

        private Dictionary<AOClient, LogState> clientLogs = new Dictionary<AOClient, LogState>();
        private readonly List<TextRange> activeSearchHighlights = new List<TextRange>();
        private IReadOnlyList<LogTextMatch> activeSearchMatches = Array.Empty<LogTextMatch>();
        private int activeSearchMatchIndex = -1;
        private FindInLogWindow? findWindow;

        private AOClient? currentClient = null;
        private ScrollViewer? ScrollViewer;

        // URL detection regex pattern
        private static readonly Regex UrlRegex = new Regex(@"(https?:\/\/[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public OOCLog()
        {
            InitializeComponent();
            LogBox.Document = new FlowDocument();
            SizeChanged += OOCLog_SizeChanged;

            Loaded += OOCLog_Loaded;
            txtOOCShowname.MaxLength = OOCShownameLengthLimit;
        }

        private void OOCLog_Loaded(object sender, RoutedEventArgs e)
        {
            ScrollViewer = GetScrollViewer(LogBox);
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

        private void OOCLog_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshBottomAnchorForCurrentClient();
        }

        private LogState EnsureLogState(AOClient client)
        {
            if (!clientLogs.TryGetValue(client, out LogState? state))
            {
                state = new LogState();
                clientLogs[client] = state;
            }

            return state;
        }

        private void RefreshBottomAnchorForCurrentClient()
        {
            AOClient? logClient = ResolveLogClient(currentClient);
            if (logClient == null)
            {
                return;
            }

            if (!clientLogs.TryGetValue(logClient, out LogState? state))
            {
                return;
            }

            RefreshBottomAnchor(state);
        }

        private void RefreshBottomAnchor(LogState state)
        {
            if (ScrollViewer == null)
            {
                return;
            }

            state.BottomSpacer.Height = 0;
            LogBox.UpdateLayout();

            double freeSpace = ScrollViewer.ViewportHeight - ScrollViewer.ExtentHeight;
            state.BottomSpacer.Height = freeSpace > 0 ? freeSpace : 0;
            LogBox.UpdateLayout();
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
                UpdateStreamLabel(client);
                return;
            }

            LogState state = EnsureLogState(logClient);

            LogBox.Document = state.Document;
            RefreshBottomAnchor(state);
            UpdateStreamLabel(client);
            ScrollToBottom();
        }

        public void UpdateStreamLabel(AOClient? client)
        {
            if (client == null)
            {
                lblStream.Content = "[STREAM]";
                return;
            }

            string characterName = string.IsNullOrWhiteSpace(client.iniPuppetName)
                ? client.currentINI?.Name ?? "Unknown"
                : client.iniPuppetName;

            lblStream.Content = $"[{client.playerID}] {characterName} (\"{client.clientName}\")";
        }

        public void AddMessage(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromServer = false,
            IReadOnlyList<LogMessageActionLink>? nameLinks = null,
            IReadOnlyList<LogMessageActionLink>? messageLinks = null)
        {
            AddMessageCore(client, showName, message, isSentFromServer, nameLinks, messageLinks, transientHandle: null);
        }

        public LogMessageHandle AddTransientMessage(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromServer = false,
            IReadOnlyList<LogMessageActionLink>? nameLinks = null,
            IReadOnlyList<LogMessageActionLink>? messageLinks = null)
        {
            LogMessageHandle handle = new LogMessageHandle();
            AddMessageCore(client, showName, message, isSentFromServer, nameLinks, messageLinks, handle);
            return handle;
        }

        public void UpdateTransientMessage(
            AOClient? client,
            LogMessageHandle handle,
            string showName,
            string message,
            bool isSentFromServer = false,
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
                AddMessageCore(client, showName, message, isSentFromServer, nameLinks, messageLinks, handle);
                return;
            }

            PopulateParagraph(paragraph, showName, message, isSentFromServer, nameLinks, messageLinks);
            RefreshBottomAnchor(state);

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = state.Document;
                ScrollToBottom();
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
            RefreshBottomAnchor(state);

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = state.Document;
                ScrollToBottom();
            }
        }

        private void AddMessageCore(
            AOClient? client,
            string showName,
            string message,
            bool isSentFromServer,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks,
            LogMessageHandle? transientHandle)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                DisplayMessage("System", "No client selected. Message not stored.", true);
                return;
            }

            LogState state = EnsureLogState(logClient);
            bool shouldScroll = IsScrolledToBottom();
            Paragraph paragraph = CreateParagraph(showName, message, isSentFromServer, nameLinks, messageLinks);

            state.Document.Blocks.InsertBefore(state.Document.Blocks.LastBlock, paragraph);
            if (transientHandle != null)
            {
                state.TransientEntries[transientHandle.Id] = paragraph;
            }

            RefreshBottomAnchor(state);

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = state.Document;
                if (shouldScroll)
                {
                    ScrollToBottom();
                }
            }
        }

        private Paragraph CreateParagraph(
            string showName,
            string message,
            bool isSentFromServer,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks)
        {
            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2),
                LineHeight = 2
            };
            PopulateParagraph(paragraph, showName, message, isSentFromServer, nameLinks, messageLinks);
            return paragraph;
        }

        private void PopulateParagraph(
            Paragraph paragraph,
            string showName,
            string message,
            bool isSentFromServer,
            IReadOnlyList<LogMessageActionLink>? nameLinks,
            IReadOnlyList<LogMessageActionLink>? messageLinks)
        {
            paragraph.Inlines.Clear();

            Brush nameBrush = isSentFromServer
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x5F, 0x5F, 0x00))
                : Brushes.DarkBlue;
            Run nameRun = new Run(showName ?? string.Empty)
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush
            };
            paragraph.Inlines.Add(nameRun);
            AppendActionLinks(paragraph, nameLinks);
            paragraph.Inlines.Add(new Run(": ")
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush
            });
            AddTextWithHyperlinks(paragraph, message ?? string.Empty);
            AppendActionLinks(paragraph, messageLinks);
        }

        private void AddTextWithHyperlinks(Paragraph paragraph, string text)
        {
            // Find all URLs in the text
            var matches = UrlRegex.Matches(text);

            if (matches.Count == 0)
            {
                // No URLs, just add the text
                paragraph.Inlines.Add(new Run(text));
                return;
            }

            int lastIndex = 0;
            foreach (Match match in matches)
            {
                // Add text before the URL
                if (match.Index > lastIndex)
                {
                    string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                    paragraph.Inlines.Add(new Run(beforeText));
                }

                // Create and add the hyperlink
                string url = match.Value;
                Hyperlink hyperlink = new Hyperlink(new Run(url))
                {
                    NavigateUri = new Uri(url)
                };
                hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                paragraph.Inlines.Add(hyperlink);

                lastIndex = match.Index + match.Length;
            }

            // Add any remaining text after the last URL
            if (lastIndex < text.Length)
            {
                string afterText = text.Substring(lastIndex);
                paragraph.Inlines.Add(new Run(afterText));
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
                    Foreground = new SolidColorBrush(Color.FromRgb(46, 124, 191)),
                    TextDecorations = TextDecorations.Underline,
                    ToolTip = string.IsNullOrWhiteSpace(link.ToolTip) ? null : link.ToolTip
                };
                hyperlink.Click += (_, _) => link.OnClick();
                paragraph.Inlines.Add(hyperlink);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Open the URL in the default browser
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                DisplayMessage("System", $"Failed to open URL: {ex.Message}", true);
            }
        }

        private ScrollViewer? GetScrollViewer(DependencyObject dep)
        {
            if (dep is ScrollViewer scrollViewer) return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = VisualTreeHelper.GetChild(dep, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void DisplayMessage(string showName, string message, bool isSentFromServer)
        {
            if (currentClient == null) return;
            AddMessage(currentClient, showName, message, isSentFromServer);
        }

        public void ClearClientLog(AOClient? client)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient != null && clientLogs.ContainsKey(logClient))
            {
                clientLogs[logClient] = new LogState();
                LogState state = clientLogs[logClient];

                if (IsCurrentLogStream(client))
                {
                    LogBox.Document = state.Document;
                    RefreshBottomAnchor(state);
                }
            }
        }

        public void ClearAllLogs()
        {
            clientLogs.Clear();
            LogBox.Document = new LogState().Document;
        }

        public void ScrollToBottom()
        {
            if (ScrollViewer != null)
            {
                ScrollViewer.Dispatcher.InvokeAsync(() => ScrollViewer.ScrollToEnd(),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private bool IsScrolledToBottom()
        {
            if (ScrollViewer == null) return true;
            return ScrollViewer.VerticalOffset >= ScrollViewer.ScrollableHeight - 10; // 10px tolerance
        }

        public IReadOnlyList<LogTextMatch> FindInCurrentDocument(
            string searchText,
            bool matchCase,
            bool wholeWord,
            bool useRegex)
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
        }

        private void txtOOCMessage_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtOOCMessage_Placeholder.Visibility = string.IsNullOrWhiteSpace(txtOOCMessage.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void txtOOCShowname_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtOOCShowname_Placeholder.Visibility = string.IsNullOrWhiteSpace(txtOOCShowname.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void txtOOCMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                if (string.IsNullOrWhiteSpace(txtOOCShowname.Text))
                {
                    AddMessage(currentClient, "Oceanya Client", "You must set a showname before sending a message!", true);
                    return;
                }

                if (currentClient == null)
                {
                    AddMessage(currentClient, "Oceanya Client", "No client selected. Please select a client first.", true);
                    return;
                }

                string message = txtOOCMessage.Text;
                txtOOCMessage.Clear();
                OnSendOOCMessage?.Invoke(txtOOCShowname.Text, message);
            }
        }

        private void btnServerConsole_Click(object sender, RoutedEventArgs e)
        {
            DebugConsoleWindow.ShowWindow();
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

        private void MenuItemFindInLogFolder_Click(object sender, RoutedEventArgs e)
        {
            if (findInAllLogsWindow?.HostWindow?.IsVisible == true)
            {
                findInAllLogsWindow.HostWindow.Activate();
                return;
            }

            string logRoot = Ao2TextLogWriter.ResolveLogRootDirectory();
            if (string.IsNullOrWhiteSpace(logRoot))
            {
                OceanyaMessageBox.Show(Window.GetWindow(this), "No log folder can be resolved until a config.ini is selected.");
                return;
            }

            findInAllLogsWindow = new FindInAllLogsWindow(logRoot);
            Window hostWindow = OceanyaWindowManager.CreateWindow(findInAllLogsWindow);
            hostWindow.Owner = Window.GetWindow(this);
            hostWindow.Closed += (_, _) => findInAllLogsWindow = null;
            hostWindow.Show();
        }
    }
}
