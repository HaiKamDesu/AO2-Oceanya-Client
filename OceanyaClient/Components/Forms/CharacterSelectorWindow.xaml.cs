using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AOBot_Testing.Structures;
using Common;
using Common.WebAssets;

namespace OceanyaClient
{
    /// <summary>
    /// Character selector based on the server's available character list.
    /// Shows a Frequently Used section followed by per-category sections with icon grids.
    /// </summary>
    public partial class CharacterSelectorWindow : OceanyaWindowContentControl
    {
        private const double BaseCardWidth = 82;
        private const double BaseCardHeight = 94;
        private const double BaseIconSize = 60;

        // Normal selectable card colors
        private static readonly Color NormalBg     = Color.FromRgb(0x1E, 0x1E, 0x1E);
        private static readonly Color NormalBorder  = Color.FromRgb(0x44, 0x44, 0x44);
        private static readonly Color HoverBorder   = Color.FromRgb(0x88, 0x88, 0x88);
        private static readonly Color SelectedBg    = Color.FromRgb(0x1A, 0x32, 0x4A);
        private static readonly Color SelectedBorder = Color.FromRgb(0x5A, 0x9A, 0xD8);
        private static readonly Color CurrentBg = Color.FromRgb(0x2F, 0x2A, 0x12);
        private static readonly Color CurrentBorder = Color.FromRgb(0xD8, 0xB4, 0x5A);

        // Taken (server slot occupied) — dark red
        private static readonly Color TakenBg     = Color.FromRgb(0x3D, 0x0F, 0x0F);
        private static readonly Color TakenBorder  = Color.FromRgb(0x5A, 0x1A, 0x1A);

        // Not installed locally — very dark, muted
        private static readonly Color NotLocalBg    = Color.FromRgb(0x14, 0x14, 0x14);
        private static readonly Color NotLocalBorder = Color.FromRgb(0x2E, 0x2E, 0x2E);

        private readonly IReadOnlyDictionary<string, bool> serverCharacterAvailability;

        /// <summary>Mutable local copy so "Remove from Frequently Used" can modify it live.</summary>
        private readonly Dictionary<string, int> frequentlyUsedCounts;

        private readonly bool showClientNameField;

        private string? selectedCharacterName;
        private string? firstSelectableAutomationCharacterName;
        private Border? selectedCardBorder;
        private double currentIconScale;
        private bool suppressIconScaleChange;

        private readonly List<SectionData> sections = new();

        /// <summary>
        /// One shared, frozen placeholder for every card without an icon.
        /// </summary>
        /// <remarks>
        /// This used to be rebuilt per card - a 60x60 <see cref="WriteableBitmap"/> plus a 14 KB pixel
        /// array each time. On a server with a couple thousand characters that alone was thousands of
        /// bitmaps and tens of megabytes of throwaway allocation before the window could even appear.
        /// </remarks>
        private static readonly Lazy<BitmapSource> SharedPlaceholderIcon =
            new Lazy<BitmapSource>(CreatePlaceholderIcon, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Decoded character icons shared across every selector instance, keyed by absolute path and
        /// validated by last-write-time so an edited icon still refreshes.
        /// </summary>
        /// <remarks>
        /// Icons are frozen, so they can be decoded on a worker thread and handed straight to the UI.
        /// Reopening the selector, or rebuilding it after a "Remove from Frequently Used", costs no
        /// disk work at all for icons already seen.
        /// </remarks>
        private static readonly ConcurrentDictionary<string, (BitmapSource Image, DateTime WriteTimeUtc)> IconCache =
            new ConcurrentDictionary<string, (BitmapSource, DateTime)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Upper bound on <see cref="IconCache"/> before it is dropped wholesale.</summary>
        private const int IconCacheCapacity = 4000;

        /// <summary>Icon decodes dispatched per UI batch while filling cards in the background.</summary>
        private const int IconBatchSize = 48;

        /// <summary>Cancels in-flight background icon loading when the list is rebuilt or closed.</summary>
        private CancellationTokenSource? iconLoadCancellation;

        /// <summary>Web-only characters already warmed, so scrolling back does not re-request them.</summary>
        private readonly HashSet<string> warmedWebCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Debounce for scroll-driven warm-up passes.</summary>
        private const int VisibleWarmDebounceMilliseconds = 120;

        private System.Windows.Threading.DispatcherTimer? visibleWarmTimer;

        public CharacterSelectorWindow(
            IReadOnlyDictionary<string, bool> serverCharacterAvailability,
            IReadOnlyDictionary<string, int> frequentlyUsedCounts,
            string? currentSelectedCharName,
            string? defaultClientName = null)
        {
            InitializeComponent();
            Title = "Select Character";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            this.serverCharacterAvailability = serverCharacterAvailability;
            this.frequentlyUsedCounts = new Dictionary<string, int>(frequentlyUsedCounts, StringComparer.OrdinalIgnoreCase);
            selectedCharacterName = currentSelectedCharName;

            showClientNameField = defaultClientName != null;
            if (showClientNameField)
                ClientNameBox.Text = defaultClientName;
            else
                ClientNameRow.Visibility = Visibility.Collapsed;

            // Restore saved window size
            double savedW = SaveFile.Data.CharacterSelectorWindowWidth;
            double savedH = SaveFile.Data.CharacterSelectorWindowHeight;
            if (savedW >= MinWidth) Width = savedW;
            if (savedH >= MinHeight) Height = savedH;

            // Load saved icon scale before building sections so cards are sized correctly from the start
            currentIconScale = Math.Clamp(SaveFile.Data.CharacterSelectorIconScale, 0.5, 3.0);

            BuildSections();

            // Apply slider without triggering a redundant ApplyIconScale (cards already built at the right scale)
            suppressIconScaleChange = true;
            IconSizeSlider.Value = currentIconScale;
            IconSizeLabel.Text = $"{currentIconScale:F1}×";
            suppressIconScaleChange = false;

            Closed += OnWindowClosed;
            WebAssetService.AnyMaterialized += OnWebAssetMaterialized;
            // The first warm-up needs real layout: card positions relative to the scroll viewport are
            // meaningless until the window has measured and arranged.
            Loaded += (_, _) => ScheduleVisibleWebCardWarm();
            MarkAutomationReady();
        }

        public override string HeaderText => "SELECT CHARACTER";

        public override bool IsUserResizeEnabled => true;

        /// <summary>The character name the user confirmed, or null if cancelled.</summary>
        public string? SelectedCharacterName => selectedCharacterName;

        /// <summary>The client name entered by the user. Only populated when the name field is shown (add-client flow).</summary>
        public string? SelectedClientName => showClientNameField ? ClientNameBox.Text?.Trim() : null;

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            CancelIconLoading();
            WebAssetService.AnyMaterialized -= OnWebAssetMaterialized;
            visibleWarmTimer?.Stop();
            visibleWarmTimer = null;

            Window? hw = HostWindow;
            if (hw != null && hw.WindowState != WindowState.Maximized)
            {
                SaveFile.Data.CharacterSelectorWindowWidth = hw.Width;
                SaveFile.Data.CharacterSelectorWindowHeight = hw.Height;
            }
            SaveFile.Data.CharacterSelectorIconScale = currentIconScale;
            SaveFile.Save();
        }

        private void BuildSections()
        {
            Stopwatch buildStopwatch = Stopwatch.StartNew();
            double lastLapMs = 0;
            double Lap()
            {
                double now = buildStopwatch.Elapsed.TotalMilliseconds;
                double delta = now - lastLapMs;
                lastLapMs = now;
                return delta;
            }

            CancelIconLoading();
            SectionsPanel.Children.Clear();
            CharacterAutomationButtonsPanel.Children.Clear();
            sections.Clear();

            var localByName = CharacterFolder.FullList
                .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);
            double msLocalIndex = Lap();

            // With web asset fallback active the server's own characters are all obtainable, exactly as
            // they are in webAO: anything not installed locally streams from the server's asset URL on
            // selection. Without it, only physically installed characters are selectable.
            bool webFallbackActive = WebAssetService.IsActive;
            WebAssetManifest? manifest = WebAssetService.Current?.Manifest;

            var allEntries = serverCharacterAvailability
                .Select(kvp =>
                {
                    localByName.TryGetValue(kvp.Key, out CharacterFolder? local);
                    string category = local?.configINI?.Category ?? string.Empty;
                    string iconPath = local?.CharIconPath ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(iconPath) && webFallbackActive)
                    {
                        // CharacterFolder resolves its icon path once, at Create time. A character
                        // registered from the mirror before its icon finished downloading is therefore
                        // stuck with an empty path, so look the icon up in the mirror directly.
                        iconPath = WebAssetService.FindInMirrorIfActive(
                            "characters/" + WebAssetSource.NormalizeVPath(kvp.Key) + "/char_icon",
                            WebAssetKind.CharacterIcon) ?? string.Empty;
                    }

                    bool obtainableFromWeb = local == null
                        && webFallbackActive
                        && !IsProvenAbsentOnServer(manifest, kvp.Key);
                    return new CharEntry(kvp.Key, kvp.Value, iconPath, local != null, category, obtainableFromWeb);
                })
                .ToList();
            double msEntries = Lap();

            // Frequently used section — only entries with count > 0
            var frequentEntries = allEntries
                .Where(e => frequentlyUsedCounts.TryGetValue(e.Name, out int c) && c > 0)
                .OrderByDescending(e => frequentlyUsedCounts[e.Name])
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (frequentEntries.Count > 0)
            {
                AddSection("Frequently Used", frequentEntries, isFrequentlyUsed: true);
            }

            // Per-category sections
            var categorized = allEntries
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? string.Empty : e.Category)
                .OrderBy(g => g.Key == string.Empty ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in categorized)
            {
                string sectionName = string.IsNullOrWhiteSpace(group.Key) ? "Uncategorized" : group.Key;
                var entries = group.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                AddSection(sectionName, entries);
            }

            double msCards = Lap();

            // Tag the first selectable card so UI-automation tests can click it without knowing the char name
            var firstSelectable = sections
                .SelectMany(s => s.Cards)
                .FirstOrDefault(c => c.Entry.IsAvailable && c.Entry.IsSelectableSource);
            if (firstSelectable.Card != null)
            {
                firstSelectableAutomationCharacterName = firstSelectable.Entry.Name;
                FirstSelectableAutomationButton.IsEnabled = true;
                FirstSelectableAutomationButton.Visibility = Visibility.Visible;
            }
            else
            {
                firstSelectableAutomationCharacterName = null;
                FirstSelectableAutomationButton.IsEnabled = false;
                FirstSelectableAutomationButton.Visibility = Visibility.Collapsed;
            }

            // One hidden 1x1 button per character exists purely so FlaUI can click a character by name.
            // On a large server that is thousands of extra visual-tree elements for a production user
            // who will never use them, so it is now built only under UI automation.
            int automationButtons = 0;
            if (OceanyaTestMode.IsEnabled)
            {
                foreach (CharEntry entry in sections.SelectMany(s => s.Cards).Select(c => c.Entry)
                             .Where(e => e.IsAvailable && e.IsSelectableSource)
                             .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First()))
                {
                    Button button = new Button
                    {
                        Width = 1,
                        Height = 1,
                        Opacity = 0,
                        Tag = entry.Name
                    };
                    AutomationProperties.SetAutomationId(
                        button,
                        "CharacterSelector.Character." + SanitizeAutomationSegment(entry.Name));
                    button.Click += CharacterAutomationButton_Click;
                    CharacterAutomationButtonsPanel.Children.Add(button);
                    automationButtons++;
                }
            }

            double msAutomation = Lap();

            // Pre-select current character (only if selectable)
            if (!string.IsNullOrWhiteSpace(selectedCharacterName))
            {
                bool found = false;
                foreach (var section in sections)
                {
                    if (found) break;
                    foreach (var (entry, card, _) in section.Cards)
                    {
                        if (string.Equals(entry.Name, selectedCharacterName, StringComparison.OrdinalIgnoreCase))
                        {
                            if ((entry.IsAvailable || string.Equals(entry.Name, selectedCharacterName, StringComparison.OrdinalIgnoreCase))
                                && entry.IsSelectableSource)
                                ApplySelection(card, entry.Name);
                            found = true;
                            break;
                        }
                    }
                }
            }

            double msSelect = Lap();
            int totalCards = sections.Sum(section => section.Cards.Count);
            int webOnly = sections.SelectMany(section => section.Cards).Count(c => c.Entry.IsWebOnly);

            CustomConsole.Info(
                $"[CHARSELECT-TIMING] total={buildStopwatch.Elapsed.TotalMilliseconds:0.0}ms | "
                + $"localIndex={msLocalIndex:0.0} entries={msEntries:0.0} cards={msCards:0.0} "
                + $"automation={msAutomation:0.0} select={msSelect:0.0} | "
                + $"serverChars={serverCharacterAvailability.Count} cards={totalCards} sections={sections.Count} "
                + $"localChars={CharacterFolder.FullList.Count} webOnly={webOnly} "
                + $"automationButtons={automationButtons} webFallback={webFallbackActive}",
                CustomConsole.LogCategory.Viewport);

            StartBackgroundIconLoad();
        }

        /// <summary>
        /// Reports whether the server's manifests prove this character has no assets at all, so it
        /// should still be shown as unobtainable even with web fallback active.
        /// </summary>
        private static bool IsProvenAbsentOnServer(WebAssetManifest? manifest, string characterName)
        {
            if (manifest == null || !manifest.HasCharacterList)
            {
                // No characters.json: the server may still serve the character, so assume it can.
                return false;
            }

            return manifest.IsKnownAbsent(
                "characters/" + WebAssetSource.NormalizeVPath(characterName) + "/char.ini");
        }

        /// <summary>
        /// Decodes card icons off the UI thread and applies them in small batches.
        /// </summary>
        /// <remarks>
        /// Every card previously decoded its icon synchronously inside the constructor, so the whole
        /// dialog waited on hundreds or thousands of disk reads and PNG decodes before it could appear.
        /// Cards now open instantly showing the shared placeholder and fill in as icons arrive; the
        /// batching keeps each UI-thread slice short so the window stays responsive while it fills.
        /// </remarks>
        private void StartBackgroundIconLoad()
        {
            List<(CharEntry Entry, Image Icon)> pending = sections
                .SelectMany(section => section.Cards)
                .Where(card => !string.IsNullOrWhiteSpace(card.Entry.LocalIconPath))
                .Select(card => (card.Entry, card.Icon))
                .ToList();

            WarmVisibleWebCards();

            if (pending.Count == 0)
            {
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            iconLoadCancellation = cancellation;
            CancellationToken token = cancellation.Token;

            _ = Task.Run(
                async () =>
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    int decoded = 0;
                    int cacheHits = 0;
                    List<(Image Icon, BitmapSource Image)> batch = new List<(Image, BitmapSource)>(IconBatchSize);

                    foreach ((CharEntry entry, Image icon) in pending)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        bool wasCached = IconCache.ContainsKey(entry.LocalIconPath);
                        BitmapSource? image = LoadIconCached(entry.LocalIconPath);
                        if (image == null)
                        {
                            continue;
                        }

                        if (wasCached)
                        {
                            cacheHits++;
                        }
                        else
                        {
                            decoded++;
                        }

                        batch.Add((icon, image));
                        if (batch.Count >= IconBatchSize)
                        {
                            await ApplyIconBatchAsync(batch, token).ConfigureAwait(false);
                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                    {
                        await ApplyIconBatchAsync(batch, token).ConfigureAwait(false);
                    }

                    if (!token.IsCancellationRequested)
                    {
                        CustomConsole.Info(
                            $"[CHARSELECT-ICONS] loaded={pending.Count} decoded={decoded} cacheHits={cacheHits} "
                            + $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:0.0} cacheSize={IconCache.Count}",
                            CustomConsole.LogCategory.Viewport);
                    }
                },
                token);
        }

        private async Task ApplyIconBatchAsync(
            List<(Image Icon, BitmapSource Image)> batch,
            CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            (Image Icon, BitmapSource Image)[] snapshot = batch.ToArray();
            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    foreach ((Image icon, BitmapSource image) in snapshot)
                    {
                        icon.Source = image;
                    }
                },
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Fetches config and icon for the cards currently on screen, so a streamed character shows real
        /// art and its proper category instead of a permanent placeholder.
        /// </summary>
        /// <remarks>
        /// Scoped to what is visible on purpose. Requesting the whole roster up front was measured at
        /// 3564 downloads on one server, which saturated the fetch pipeline for minutes and pushed
        /// average asset latency to 13 seconds. Scrolling warms exactly what the user is looking at.
        /// </remarks>
        private void WarmVisibleWebCards()
        {
            if (!WebAssetService.IsActive || SectionsScrollViewer == null)
            {
                return;
            }

            int requested = 0;
            foreach ((CharEntry entry, Border card, Image icon) in sections.SelectMany(section => section.Cards))
            {
                if (!entry.IsWebOnly || !IsCardWithinViewport(card))
                {
                    continue;
                }

                string folder = "characters/" + WebAssetSource.NormalizeVPath(entry.Name);

                // An icon already in the mirror needs no request, just applying.
                string? mirrored = WebAssetService.FindInMirrorIfActive(
                    folder + "/char_icon",
                    WebAssetKind.CharacterIcon);
                if (mirrored != null)
                {
                    ApplyIconFromPath(icon, mirrored);
                    continue;
                }

                if (!warmedWebCards.Add(entry.Name))
                {
                    continue;
                }

                WebAssetService.PrefetchIfActive(folder + "/char_icon", WebAssetKind.CharacterIcon);
                WebAssetService.PrefetchIfActive(folder + "/char.ini", WebAssetKind.Config);
                requested++;
            }

            if (requested > 0)
            {
                CustomConsole.Debug(
                    $"[CHARSELECT-ICONS] warmed {requested} visible web cards (total warmed {warmedWebCards.Count})",
                    CustomConsole.LogCategory.WebAssets);
            }
        }

        /// <summary>Reports whether a card intersects the scroll viewport, plus a one-screen margin.</summary>
        private bool IsCardWithinViewport(Border card)
        {
            if (SectionsScrollViewer == null || !card.IsVisible)
            {
                return false;
            }

            try
            {
                GeneralTransform transform = card.TransformToAncestor(SectionsScrollViewer);
                Rect bounds = transform.TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                double margin = SectionsScrollViewer.ViewportHeight;
                return bounds.Bottom >= -margin && bounds.Top <= SectionsScrollViewer.ViewportHeight + margin;
            }
            catch
            {
                // A card not yet connected to the visual tree simply is not visible yet.
                return false;
            }
        }

        private void SectionsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) < 0.5 && Math.Abs(e.ViewportHeightChange) < 0.5)
            {
                return;
            }

            ScheduleVisibleWebCardWarm();
        }

        /// <summary>
        /// Coalesces scroll-driven warm-ups so a flick through a long list issues one pass, not one per
        /// scroll event.
        /// </summary>
        private void ScheduleVisibleWebCardWarm()
        {
            if (!WebAssetService.IsActive || visibleWarmTimer != null)
            {
                return;
            }

            visibleWarmTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background,
                Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(VisibleWarmDebounceMilliseconds)
            };
            visibleWarmTimer.Tick += (_, _) =>
            {
                visibleWarmTimer?.Stop();
                visibleWarmTimer = null;
                WarmVisibleWebCards();
            };
            visibleWarmTimer.Start();
        }

        /// <summary>
        /// Applies a downloaded character icon to the matching card as soon as it lands in the mirror.
        /// </summary>
        private void OnWebAssetMaterialized(WebAssetMaterializedEventArgs args)
        {
            if (args.Kind != WebAssetKind.CharacterIcon)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    foreach ((CharEntry entry, Border _, Image icon) in sections.SelectMany(section => section.Cards))
                    {
                        string expected = "characters/" + WebAssetSource.NormalizeVPath(entry.Name) + "/";
                        if (args.VPath.StartsWith(expected, StringComparison.Ordinal))
                        {
                            ApplyIconFromPath(icon, args.LocalPath);
                            break;
                        }
                    }
                }));
        }

        private void ApplyIconFromPath(Image icon, string iconPath)
        {
            BitmapSource? image = LoadIconCached(iconPath);
            if (image != null)
            {
                icon.Source = image;
            }
        }

        private void CancelIconLoading()
        {
            iconLoadCancellation?.Cancel();
            iconLoadCancellation?.Dispose();
            iconLoadCancellation = null;
        }

        /// <summary>
        /// Returns a frozen decode of <paramref name="iconPath"/>, reusing the shared cache when the
        /// file has not changed. Safe to call from a worker thread.
        /// </summary>
        private static BitmapSource? LoadIconCached(string iconPath)
        {
            DateTime writeTimeUtc;
            try
            {
                FileInfo info = new FileInfo(iconPath);
                if (!info.Exists)
                {
                    return null;
                }

                writeTimeUtc = info.LastWriteTimeUtc;
            }
            catch
            {
                return null;
            }

            if (IconCache.TryGetValue(iconPath, out (BitmapSource Image, DateTime WriteTimeUtc) cached)
                && cached.WriteTimeUtc == writeTimeUtc)
            {
                return cached.Image;
            }

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // Icons never render larger than the biggest card, so decoding at that size instead of
                // native resolution cuts both decode time and memory for large source art.
                bitmap.DecodePixelWidth = (int)Math.Ceiling(BaseIconSize * 3.0);
                bitmap.EndInit();
                bitmap.Freeze();

                if (IconCache.Count >= IconCacheCapacity)
                {
                    IconCache.Clear();
                }

                IconCache[iconPath] = (bitmap, writeTimeUtc);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void AddSection(string title, List<CharEntry> entries, bool isFrequentlyUsed = false)
        {
            Expander expander = new Expander
            {
                Header = title,
                IsExpanded = true,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };

            WrapPanel wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
            var sectionData = new SectionData(title, expander, wrap, new List<(CharEntry, Border, Image)>());

            foreach (CharEntry entry in entries)
            {
                var (card, img) = BuildCard(entry, isFrequentlyUsed);
                wrap.Children.Add(card);
                sectionData.Cards.Add((entry, card, img));
            }

            expander.Content = wrap;
            SectionsPanel.Children.Add(expander);
            sections.Add(sectionData);
        }

        private (Border card, Image icon) BuildCard(CharEntry entry, bool isFrequentlyUsed = false)
        {
            double scale = currentIconScale;
            double cardW = BaseCardWidth * scale;
            double cardH = BaseCardHeight * scale;
            double iconSz = BaseIconSize * scale;

            bool isTaken    = !entry.IsAvailable;
            // "Not local" now means "cannot be obtained at all". A character the server can stream is
            // treated exactly like an installed one, so the two sources look identical to the user.
            bool isNotLocal = !entry.IsSelectableSource;
            bool isCurrent = !string.IsNullOrWhiteSpace(selectedCharacterName)
                && string.Equals(entry.Name, selectedCharacterName, StringComparison.OrdinalIgnoreCase);
            bool isSelectable = (!isTaken || isCurrent) && !isNotLocal;

            Color bgColor = isCurrent ? CurrentBg : isNotLocal ? NotLocalBg : isTaken ? TakenBg : NormalBg;
            Color borderColor = isCurrent ? CurrentBorder : isNotLocal ? NotLocalBorder : isTaken ? TakenBorder : NormalBorder;

            Border card = new Border
            {
                Width = cardW,
                Height = cardH,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                Cursor = isSelectable ? Cursors.Hand : Cursors.No,
                Opacity = isNotLocal ? 0.55 : 1.0,
                Tag = entry
            };

            Image img = new Image
            {
                Width = iconSz,
                Height = iconSz,
                Stretch = Stretch.Uniform
            };

            // Always start on the shared placeholder. Real icons are decoded off the UI thread by
            // StartBackgroundIconLoad and swapped in, so building a card costs no disk I/O.
            img.Source = SharedPlaceholderIcon.Value;

            TextBlock nameLabel = new TextBlock
            {
                Text = isCurrent ? entry.Name + "\nCurrent" : entry.Name,
                Foreground = new SolidColorBrush(isNotLocal
                    ? Color.FromRgb(0x77, 0x77, 0x77)
                    : Color.FromRgb(0xDC, 0xDC, 0xDC)),
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = cardW - 8,
                MaxHeight = 28
            };

            StackPanel stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3)
            };
            stack.Children.Add(img);
            stack.Children.Add(nameLabel);
            card.Child = stack;

            if (isSelectable)
            {
                card.MouseLeftButtonDown += (_, _) =>
                {
                    ApplySelection(card, entry.Name);
                    DialogResult = true;
                    Close();
                };

                card.MouseEnter += (_, _) =>
                {
                    if (!IsCardSelected(card))
                        card.BorderBrush = new SolidColorBrush(HoverBorder);
                };

                card.MouseLeave += (_, _) =>
                {
                    if (!IsCardSelected(card))
                        card.BorderBrush = new SolidColorBrush(isCurrent ? CurrentBorder : NormalBorder);
                };
            }

            // Build tooltip (stacks frequently-used count + unselectable reason)
            var tooltipLines = new List<string>();
            if (isFrequentlyUsed)
            {
                frequentlyUsedCounts.TryGetValue(entry.Name, out int usageCount);
                tooltipLines.Add($"You've selected this {usageCount} time{(usageCount == 1 ? "" : "s")}");
            }
            if (!isSelectable)
            {
                string reason = isNotLocal
                    ? $"UNSELECTABLE: {entry.Name} is not installed locally and the server does not provide it"
                    : $"UNSELECTABLE: {entry.Name} is taken";
                tooltipLines.Add(reason);
            }
            if (tooltipLines.Count > 0)
                card.ToolTip = string.Join(Environment.NewLine, tooltipLines);

            if (isFrequentlyUsed)
            {
                ContextMenu cm = new ContextMenu();
                MenuItem removeItem = new MenuItem { Header = "Remove from Frequently Used" };
                removeItem.Click += (_, _) =>
                {
                    frequentlyUsedCounts.Remove(entry.Name);
                    SaveFile.Data.FrequentlyUsedIniPuppets.Remove(entry.Name);
                    SaveFile.Save();
                    BuildSections();
                };
                cm.Items.Add(removeItem);
                card.ContextMenu = cm;
            }

            return (card, img);
        }

        /// <summary>Builds the single shared placeholder bitmap. Called once per process.</summary>
        private static BitmapSource CreatePlaceholderIcon()
        {
            int size = 60;
            WriteableBitmap bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[size * size * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x33;
                pixels[i + 1] = 0x33;
                pixels[i + 2] = 0x33;
                pixels[i + 3] = 0xFF;
            }
            bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        private bool IsCardSelected(Border card) => ReferenceEquals(card, selectedCardBorder);

        private void ApplySelection(Border card, string charName)
        {
            if (selectedCardBorder != null)
            {
                selectedCardBorder.BorderBrush = new SolidColorBrush(NormalBorder);
                selectedCardBorder.Background = new SolidColorBrush(NormalBg);
            }

            selectedCardBorder = card;
            selectedCharacterName = charName;
            card.BorderBrush = new SolidColorBrush(SelectedBorder);
            card.Background = new SolidColorBrush(SelectedBg);
        }

        private void ApplyIconScale(double scale)
        {
            double cardW = BaseCardWidth * scale;
            double cardH = BaseCardHeight * scale;
            double iconSz = BaseIconSize * scale;

            foreach (var section in sections)
            {
                foreach (var (_, card, img) in section.Cards)
                {
                    card.Width = cardW;
                    card.Height = cardH;
                    img.Width = iconSz;
                    img.Height = iconSz;

                    if (card.Child is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
                    {
                        tb.MaxWidth = cardW - 8;
                    }
                }
            }
        }

        private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (suppressIconScaleChange || IconSizeLabel is null) return;
            currentIconScale = e.NewValue;
            IconSizeLabel.Text = $"{currentIconScale:F1}×";
            ApplyIconScale(currentIconScale);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = SearchBox.Text?.Trim() ?? string.Empty;
            ApplyFilter(filter);
        }

        private void ApplyFilter(string filter)
        {
            bool showAll = string.IsNullOrWhiteSpace(filter);

            foreach (SectionData section in sections)
            {
                int visibleCount = 0;
                foreach (var (entry, card, _) in section.Cards)
                {
                    bool visible = showAll
                        || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || entry.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);

                    card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    if (visible) visibleCount++;
                }

                section.Expander.Visibility = visibleCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            selectedCharacterName = null;
            DialogResult = false;
            Close();
        }

        private void FirstSelectableAutomationButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(firstSelectableAutomationCharacterName))
            {
                return;
            }

            selectedCharacterName = firstSelectableAutomationCharacterName;
            DialogResult = true;
            Close();
        }

        private void CharacterAutomationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string characterName }
                || string.IsNullOrWhiteSpace(characterName))
            {
                return;
            }

            selectedCharacterName = characterName;
            DialogResult = true;
            Close();
        }

        private static string SanitizeAutomationSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Empty";
            }

            return new string(value.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        /// <summary>One character row in the selector.</summary>
        /// <param name="IsLocal">The user has this character physically installed.</param>
        /// <param name="IsWebOnly">
        /// Not installed locally, but the connected server can stream it. Such a character is fully
        /// selectable - its <c>char.ini</c> is fetched at selection time - which is what makes the
        /// local and streamed halves of the roster indistinguishable, exactly as in webAO.
        /// </param>
        private sealed record CharEntry(
            string Name,
            bool IsAvailable,
            string LocalIconPath,
            bool IsLocal,
            string Category,
            bool IsWebOnly)
        {
            /// <summary>Whether the character's assets can be obtained at all, locally or from the server.</summary>
            public bool IsSelectableSource => IsLocal || IsWebOnly;
        }

        private sealed class SectionData
        {
            public SectionData(string title, Expander expander, WrapPanel wrapPanel, List<(CharEntry, Border, Image)> cards)
            {
                Title = title;
                Expander = expander;
                WrapPanel = wrapPanel;
                Cards = cards;
            }

            public string Title { get; }
            public Expander Expander { get; }
            public WrapPanel WrapPanel { get; }
            public List<(CharEntry Entry, Border Card, Image Icon)> Cards { get; }
        }
    }
}
