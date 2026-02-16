using AOBot_Testing.Agents;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace OceanyaClient.Components
{
    public partial class OOCLog : UserControl
    {
        private sealed class LogState
        {
            public FlowDocument Document { get; }
            public Border BottomSpacer { get; }

            public LogState()
            {
                Document = new FlowDocument();
                BottomSpacer = new Border { Height = 0 };
                Document.Blocks.Add(new BlockUIContainer(BottomSpacer));
            }
        }

        public static int OOCShownameLengthLimit = 30;
        public Action<string, string>? OnSendOOCMessage;
        public Func<AOClient, AOClient?>? LogKeyResolver { get; set; }

        private Dictionary<AOClient, LogState> clientLogs = new Dictionary<AOClient, LogState>();

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

        public void AddMessage(AOClient? client, string showName, string message, bool isSentFromServer = false)
        {
            AOClient? logClient = ResolveLogClient(client);
            if (logClient == null)
            {
                DisplayMessage("System", "No client selected. Message not stored.", true);
                return;
            }

            LogState state = EnsureLogState(logClient);

            bool shouldScroll = IsScrolledToBottom();

            FlowDocument clientDoc = state.Document;

            Paragraph paragraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 2),
                LineHeight = 2
            };

            Run nameRun = new Run($"{showName}: ") { FontWeight = FontWeights.Bold };
            nameRun.Foreground = isSentFromServer
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0x5F, 0x5F, 0x00))
                : Brushes.DarkBlue;

            paragraph.Inlines.Add(nameRun);

            // Process the message to detect and convert URLs to hyperlinks
            AddTextWithHyperlinks(paragraph, message);

            clientDoc.Blocks.Add(paragraph);
            RefreshBottomAnchor(state);

            if (IsCurrentLogStream(client))
            {
                LogBox.Document = clientDoc;
                if (shouldScroll)
                    ScrollToBottom();
            }
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
            LogBox.Document = new FlowDocument();
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
    }
}
