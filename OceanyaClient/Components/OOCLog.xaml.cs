using AOBot_Testing.Agents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        /// <summary>Live OOC log controls, for the periodic memory sample.</summary>
        private static readonly List<WeakReference<OOCLog>> LiveLogs = new List<WeakReference<OOCLog>>();
        private static readonly object LiveLogsLock = new object();
        private readonly List<TextRange> activeSearchHighlights = new List<TextRange>();
        private IReadOnlyList<LogTextMatch> activeSearchMatches = Array.Empty<LogTextMatch>();
        private int activeSearchMatchIndex = -1;
        private FindInLogWindow? findWindow;

        private AOClient? currentClient = null;
        private ScrollViewer? ScrollViewer;

        // URL detection regex pattern
        private static readonly Regex UrlRegex = new Regex(@"(https?:\/\/[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Hands the OOC input controls over to the host surface so they can be placed independently.
        /// </summary>
        /// <remarks>
        /// AO2 themes position `ooc_chat_message` and `ooc_chat_name` as separate widgets, which is
        /// impossible while they live inside this control's dock panel. Reparenting keeps every handler
        /// and field here working; only the layout parent changes. The log itself keeps the panel.
        /// </remarks>
        /// <returns>Placeable child controls keyed by their stable panel id.</returns>
        public IReadOnlyDictionary<string, FrameworkElement> ExtractPlaceableControls()
        {
            Dictionary<string, FrameworkElement> placeable = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
            {
                ["ooc_message"] = grdOOCMessage,
                ["ooc_showname"] = grdOOCShowname,
                ["ooc_server_console"] = btnServerConsole,
                ["ooc_chat"] = LogBox,
                ["ooc_stream_backdrop"] = rectStreamBackdrop,
                ["ooc_stream_text"] = lblStream
            };

            if (grdOOCMessage.Parent is Panel messageParent)
            {
                messageParent.Children.Remove(grdOOCMessage);
            }

            if (grdOOCShowname.Parent is Panel shownameParent)
            {
                shownameParent.Children.Remove(grdOOCShowname);
            }

            if (btnServerConsole.Parent is Panel consoleParent)
            {
                consoleParent.Children.Remove(btnServerConsole);
            }

            if (LogBox.Parent is Panel logParent)
            {
                logParent.Children.Remove(LogBox);
                logParent.Children.Remove(rectStreamBackdrop);
                logParent.Children.Remove(lblStream);
            }

            // Everything visible was handed over; this control only carries the code-behind now.
            dockInputSection.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            return placeable;
        }

        /// <summary>
        /// Reports how much OOC transcript is being held, for the periodic memory sample.
        /// </summary>
        /// <remarks>
        /// The OOC log has NO trim at all - unlike the IC log it does not even consult
        /// <c>log_maximum</c> - so on a long session it only ever grows. Worth measuring before assuming a
        /// leak is somewhere more exotic.
        /// </remarks>
        public static string GetTranscriptDiagnostics()
        {
            int documents = 0;
            int totalBlocks = 0;
            int maxBlocks = 0;

            lock (LiveLogsLock)
            {
                LiveLogs.RemoveAll(reference => !reference.TryGetTarget(out _));
                foreach (WeakReference<OOCLog> reference in LiveLogs)
                {
                    if (!reference.TryGetTarget(out OOCLog? log))
                    {
                        continue;
                    }

                    foreach (LogState state in log.clientLogs.Values)
                    {
                        documents++;
                        int blocks = state.Document.Blocks.Count;
                        totalBlocks += blocks;
                        maxBlocks = Math.Max(maxBlocks, blocks);
                    }
                }
            }

            return $"oocLogDocs={documents} oocLogBlocks={totalBlocks} oocLogMaxBlocks={maxBlocks}";
        }

        public OOCLog()
        {
            InitializeComponent();
            LogBox.Document = new FlowDocument();

            lock (LiveLogsLock)
            {
                LiveLogs.RemoveAll(reference => !reference.TryGetTarget(out _));
                LiveLogs.Add(new WeakReference<OOCLog>(this));
            }

            SizeChanged += OOCLog_SizeChanged;

            // Watch the VIEWPORT, not the label: whether the name fits, how far it scrolls and where the
            // edge fade sits are all measured from the clipped area, and its size settles a layout pass
            // after the label's. Measuring too early pinned the fade partway across the widget.
            StreamTextViewport.SizeChanged += (_, _) => RestartStreamTextScroll();
            nowPlayingText = NothingPlayingText;
            txtStreamText.Text = NothingPlayingText;

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
                return;
            }

            LogState state = EnsureLogState(logClient);

            LogBox.Document = state.Document;
            RefreshBottomAnchor(state);
            ScrollToBottom();
        }

        /// <summary>
        /// Shows the currently playing song, the way AO2's music display does.
        /// </summary>
        /// <remarks>
        /// AO2's counterpart of this bar is `music_display` with `music_name` on it, and that label is a
        /// <c>ScrollText</c> (`AO2-Client/src/scrolltext.cpp`): it shows "None" until something plays, and
        /// when the name is wider than the widget it loops continuously - text plus a `"   ---   "`
        /// separator, 2px every 50ms, faded at both edges - rather than trimming or bouncing.
        /// </remarks>
        /// <param name="songName">Song to show, or empty for nothing playing.</param>
        /// <summary>
        /// Applies the colours a theme set for this log, or clears them.
        /// </summary>
        /// <param name="colors">Colours to use, or null to go back to the built-in ones.</param>
        public void SetThemeColors(LogThemeColors? colors)
        {
            themeColors = colors != null && !colors.IsEmpty ? colors : null;
            LogRunRecolorer.Apply(
                clientLogs.Values.Select(state => state.Document),
                themeColors,
                OocFallbackColors);
        }

        /// <summary>The colours a run goes back to when the theme does not set one.</summary>
        private static readonly Dictionary<LogRunRole, Brush> OocFallbackColors = new Dictionary<LogRunRole, Brush>
        {
            [LogRunRole.SenderName] = Brushes.DarkBlue,
            [LogRunRole.ServerName] = new SolidColorBrush(Color.FromArgb(0xFF, 0x5F, 0x5F, 0x00))
        };

        /// <summary>Colours a theme set for this log, or null for the built-in ones.</summary>
        private LogThemeColors? themeColors;

        public void SetNowPlaying(string? songName)
        {
            string text = (songName ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                text = NothingPlayingText;
            }

            if (string.Equals(text, nowPlayingText, StringComparison.Ordinal))
            {
                return;
            }

            nowPlayingText = text;
            RestartStreamTextScroll();
        }

        /// <summary>
        /// Starts, restarts or stops the marquee depending on whether the text fits.
        /// </summary>
        private void RestartStreamTextScroll()
        {
            // Re-entrancy guard: this rewrites the text, which resizes the track, which can raise the very
            // size change that called it - the label flickered between "None" and its scrolling form.
            if (isRestartingStreamScroll)
            {
                return;
            }

            isRestartingStreamScroll = true;
            try
            {
                RestartStreamTextScrollCore();
            }
            finally
            {
                isRestartingStreamScroll = false;
            }
        }

        private void RestartStreamTextScrollCore()
        {
            StreamTextOffset.BeginAnimation(TranslateTransform.XProperty, null);

            // AO2's ScrollText insets the text by a third of the widget height on both sides, and only
            // scrolls when the name does not fit inside what is left.
            double height = lblStream.ActualHeight > 0 ? lblStream.ActualHeight : lblStream.Height;
            double margin = double.IsNaN(height) || height <= 0 ? 8 : Math.Round(height / 3);
            double available = StreamTextViewport.ActualWidth - (margin * 2);

            // Measured from the raw name: the separator is only added once we know it scrolls.
            txtStreamText.Text = nowPlayingText;
            txtStreamText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = txtStreamText.DesiredSize.Width;
            bool scrolls = available > 0 && textWidth > available;

            txtStreamText.Text = scrolls ? nowPlayingText + ScrollSeparator : nowPlayingText;
            txtStreamTextRepeat.Text = scrolls ? nowPlayingText + ScrollSeparator : string.Empty;
            StreamTextOffset.X = margin;

            // The canvas does not lay its children out, so the track is centred by hand.
            StreamTextTrack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double trackHeight = StreamTextTrack.DesiredSize.Height;
            Canvas.SetTop(StreamTextTrack, Math.Max(0, (StreamTextViewport.ActualHeight - trackHeight) / 2));
            StreamTextViewport.OpacityMask = scrolls ? BuildEdgeFadeMask(StreamTextViewport.ActualWidth) : null;

            if (!scrolls)
            {
                return;
            }

            txtStreamText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double loopWidth = txtStreamText.DesiredSize.Width;

            // AO2 holds the start still for its first 64 steps, then loops one full copy width forever.
            DoubleAnimationUsingKeyFrames marquee = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            TimeSpan hold = TimeSpan.FromMilliseconds(64 / ScrollPixelsPerStep * ScrollIntervalMilliseconds);
            TimeSpan loop = TimeSpan.FromMilliseconds(loopWidth / ScrollPixelsPerStep * ScrollIntervalMilliseconds);
            marquee.KeyFrames.Add(new LinearDoubleKeyFrame(margin, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            marquee.KeyFrames.Add(new LinearDoubleKeyFrame(margin, KeyTime.FromTimeSpan(hold)));
            marquee.KeyFrames.Add(new LinearDoubleKeyFrame(margin - loopWidth, KeyTime.FromTimeSpan(hold + loop)));
            StreamTextOffset.BeginAnimation(TranslateTransform.XProperty, marquee);
        }

        /// <summary>
        /// Builds the soft edge AO2 fades its scrolling text against.
        /// </summary>
        /// <param name="width">Width of the visible area.</param>
        /// <returns>The mask, or null when the area is too narrow to fade.</returns>
        private static Brush? BuildEdgeFadeMask(double width)
        {
            // AO2 fades a fixed 15px, which it can afford on a 224px-wide music display. A theme can make
            // this label half that, where 30px of fade is most of the text - so the fade is capped to a
            // fraction of the width and only ever eats a sliver of a narrow label.
            double fadeWidth = Math.Min(EdgeFadeWidth, width * MaximumEdgeFadeFraction);
            if (width <= fadeWidth * 2 || fadeWidth < 1)
            {
                return null;
            }

            // ABSOLUTE mapping, measured from the element's own origin. A relative gradient is mapped onto
            // the bounding box of what is rendered, which includes the translated text - so the fade slid
            // along with the scroll instead of staying at the edges of the widget, the way AO2's alpha
            // channel does (it is painted over fixed 15px strips at each side, with the text moving under).
            LinearGradientBrush mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(width, 0)
            };

            double fade = fadeWidth / width;
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, fade));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - fade));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            mask.Freeze();
            return mask;
        }

        /// <summary>The song name as given, without the marquee separator appended to it.</summary>
        private string nowPlayingText = NothingPlayingText;

        /// <summary>Guards the marquee rebuild against the size change it causes itself.</summary>
        private bool isRestartingStreamScroll;

        /// <summary>Text AO2 shows while nothing is playing.</summary>
        private const string NothingPlayingText = "None";

        /// <summary>Separator AO2 puts between the repeats of a scrolling name.</summary>
        private const string ScrollSeparator = "   ---   ";

        /// <summary>Pixels AO2 advances the marquee per tick.</summary>
        private const double ScrollPixelsPerStep = 2;

        /// <summary>Milliseconds between AO2's marquee ticks.</summary>
        private const double ScrollIntervalMilliseconds = 50;

        /// <summary>Width of the fade AO2 draws at each edge of a scrolling name.</summary>
        private const double EdgeFadeWidth = 15;

        /// <summary>Most of the width one edge fade may take on a narrow label.</summary>
        private const double MaximumEdgeFadeFraction = 0.06;

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

            // AO2 colours an OOC name by where the message came from (`Courtroom::append_server_chatmessage`):
            // a plain player message carries colour "0" and uses `ms_chatlog_sender_color`, a server one
            // carries "1" and uses `server_chatlog_sender_color`. The body is left to the log's own font
            // colour. All of it arrives here because our runs carry explicit brushes.
            Brush nameBrush = isSentFromServer
                ? themeColors?.ServerName ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x5F, 0x5F, 0x00))
                : themeColors?.SenderName ?? Brushes.DarkBlue;
            LogRunRole nameRole = isSentFromServer ? LogRunRole.ServerName : LogRunRole.SenderName;
            Run nameRun = new Run(showName ?? string.Empty)
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush,

                // Tagged so a theme applied later repaints this line too, instead of leaving the log in two
                // colour schemes at once.
                Tag = nameRole
            };
            paragraph.Inlines.Add(nameRun);
            AppendActionLinks(paragraph, nameLinks);
            paragraph.Inlines.Add(new Run(": ")
            {
                FontWeight = FontWeights.Bold,
                Foreground = nameBrush,
                Tag = nameRole
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
