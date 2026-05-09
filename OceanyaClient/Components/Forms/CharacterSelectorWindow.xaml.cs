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
using AOBot_Testing.Structures;
using Common;

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
        private Border? selectedCardBorder;
        private double currentIconScale;
        private bool suppressIconScaleChange;

        private readonly List<SectionData> sections = new();

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
        }

        public override string HeaderText => "SELECT CHARACTER";

        public override bool IsUserResizeEnabled => true;

        /// <summary>The character name the user confirmed, or null if cancelled.</summary>
        public string? SelectedCharacterName => selectedCharacterName;

        /// <summary>The client name entered by the user. Only populated when the name field is shown (add-client flow).</summary>
        public string? SelectedClientName => showClientNameField ? ClientNameBox.Text?.Trim() : null;

        private void OnWindowClosed(object? sender, EventArgs e)
        {
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
            SectionsPanel.Children.Clear();
            sections.Clear();

            var localByName = CharacterFolder.FullList
                .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

            var allEntries = serverCharacterAvailability
                .Select(kvp =>
                {
                    localByName.TryGetValue(kvp.Key, out CharacterFolder? local);
                    string category = local?.configINI?.Category ?? string.Empty;
                    string iconPath = local?.CharIconPath ?? string.Empty;
                    return new CharEntry(kvp.Key, kvp.Value, iconPath, local != null, category);
                })
                .ToList();

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

            // Tag the first selectable card so UI-automation tests can click it without knowing the char name
            var firstSelectable = sections
                .SelectMany(s => s.Cards)
                .FirstOrDefault(c => c.Entry.IsAvailable && c.Entry.IsLocal);
            if (firstSelectable.Card != null)
                AutomationProperties.SetAutomationId(firstSelectable.Card, "CharacterSelector.FirstSelectableCard");

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
                            if (entry.IsAvailable && entry.IsLocal)
                                ApplySelection(card, entry.Name);
                            found = true;
                            break;
                        }
                    }
                }
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
            bool isNotLocal = !entry.IsLocal;
            bool isSelectable = !isTaken && !isNotLocal;

            Color bgColor     = isNotLocal ? NotLocalBg     : isTaken ? TakenBg     : NormalBg;
            Color borderColor = isNotLocal ? NotLocalBorder : isTaken ? TakenBorder : NormalBorder;

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

            if (!string.IsNullOrWhiteSpace(entry.LocalIconPath) && File.Exists(entry.LocalIconPath))
            {
                try
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(entry.LocalIconPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    img.Source = bmp;
                }
                catch
                {
                    img.Source = BuildPlaceholderIcon();
                }
            }
            else
            {
                img.Source = BuildPlaceholderIcon();
            }

            TextBlock nameLabel = new TextBlock
            {
                Text = entry.Name,
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
                        card.BorderBrush = new SolidColorBrush(NormalBorder);
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
                    ? $"UNSELECTABLE: {entry.Name} does not exist in your AO2 installation"
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

        private static BitmapSource BuildPlaceholderIcon()
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

        private sealed record CharEntry(string Name, bool IsAvailable, string LocalIconPath, bool IsLocal, string Category);

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
