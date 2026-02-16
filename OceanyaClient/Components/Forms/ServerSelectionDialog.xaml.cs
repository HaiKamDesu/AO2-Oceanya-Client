using Common;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for ServerSelectionDialog.xaml
    /// </summary>
    public partial class ServerSelectionDialog : Window
    {
        private sealed class SortState
        {
            public required string Column { get; init; }
            public required bool Ascending { get; init; }
        }

        private readonly string configIniPath;
        private readonly string initiallySelectedEndpoint;
        private readonly List<ServerEndpointDefinition> allEntries = new List<ServerEndpointDefinition>();
        private readonly Dictionary<ServerEndpointSource, SortState> sortStates = new Dictionary<ServerEndpointSource, SortState>();
        private CancellationTokenSource? refreshCancellationTokenSource;

        internal ServerEndpointDefinition? SelectedServer { get; private set; }

        public ServerSelectionDialog(string configIniPath, string initiallySelectedEndpoint)
        {
            InitializeComponent();
            WindowHelper.AddWindow(this);
            this.configIniPath = configIniPath;
            this.initiallySelectedEndpoint = initiallySelectedEndpoint ?? string.Empty;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshServersAsync(selectEndpoint: initiallySelectedEndpoint);
        }

        private async void RefreshPollButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshServersAsync(selectEndpoint: GetCurrentSelectedEndpoint());
        }

        private async Task RefreshServersAsync(string selectEndpoint)
        {
            refreshCancellationTokenSource?.Cancel();
            refreshCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = refreshCancellationTokenSource.Token;

            RefreshPollButton.IsEnabled = false;
            SelectButton.IsEnabled = false;
            StatusTextBlock.Text = "Loading server list...";

            try
            {
                await WaitForm.ShowFormAsync("Loading servers...", this);
                List<ServerEndpointDefinition> loadedEntries = await ServerEndpointCatalog.LoadAsync(configIniPath, cancellationToken);
                allEntries.Clear();
                allEntries.AddRange(loadedEntries);

                ApplyCurrentFilter();
                SelectByEndpoint(selectEndpoint);

                int pollCount = allEntries.Count(item => item.Source == ServerEndpointSource.AoServerPoll);
                int favoriteCount = allEntries.Count(item => item.Source == ServerEndpointSource.Favorites);
                int defaultCount = allEntries.Count(item => item.Source == ServerEndpointSource.Defaults);
                StatusTextBlock.Text = $"Loaded {allEntries.Count} entries (Defaults: {defaultCount}, AO Poll: {pollCount}, Favorites: {favoriteCount}).";
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Server list refresh was canceled.";
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to load server entries.", ex);
                StatusTextBlock.Text = "Failed to load server list.";
            }
            finally
            {
                RefreshPollButton.IsEnabled = true;
                UpdateSelectionState();
                await WaitForm.CloseFormAsync();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyCurrentFilter();
        }

        private void ServerTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl)
            {
                return;
            }

            ApplyCurrentFilter();
            UpdateSelectionState();
        }

        private void ServerListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListView listView)
            {
                return;
            }

            if (listView != GetCurrentListView())
            {
                return;
            }

            UpdateSelectionState();
        }

        private void ServerListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || !selected.IsSelectable)
            {
                return;
            }

            SelectedServer = selected;
            DialogResult = true;
            Close();
        }

        private async void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            bool isFavoritesTab = ServerTabs.SelectedIndex == 2;
            if (!isFavoritesTab)
            {
                if (GetCurrentSelection() is not ServerEndpointDefinition selectedServer)
                {
                    return;
                }

                if (!TryParseWebsocketEndpoint(selectedServer.Endpoint, out string selectedAddress, out int selectedPort))
                {
                    OceanyaMessageBox.Show(
                        "Only WebSocket servers can be added to favorites.",
                        "Invalid Favorite",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                bool alreadyFavorite = allEntries.Any(server =>
                    server.Source == ServerEndpointSource.Favorites
                    && string.Equals(server.Endpoint, selectedServer.Endpoint, StringComparison.OrdinalIgnoreCase));
                if (alreadyFavorite)
                {
                    ServerTabs.SelectedIndex = 2;
                    SelectByEndpoint(selectedServer.Endpoint, ServerEndpointSource.Favorites, switchToMatchedTab: false);
                    UpdateSelectionState();
                    return;
                }

                ServerEndpointCatalog.AddFavorite(
                    configIniPath,
                    new FavoriteServerEntry
                    {
                        Name = selectedServer.Name,
                        Address = selectedAddress,
                        Port = selectedPort,
                        Description = selectedServer.Description,
                        Legacy = false
                    });

                await RefreshFavoritesOnlyAsync(selectedServer.Endpoint, switchToFavoritesTab: true);
                return;
            }

            if (!FavoriteServerEditorDialog.ShowDialog(
                    this,
                    out string name,
                    out string endpoint,
                    out string description,
                    windowTitle: "Add Favorite Server",
                    actionText: "ADD FAVORITE"))
            {
                return;
            }

            if (!TryParseWebsocketEndpoint(endpoint, out string address, out int port))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use ws:// or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ServerEndpointCatalog.AddFavorite(
                configIniPath,
                new FavoriteServerEntry
                {
                    Name = name,
                    Address = address,
                    Port = port,
                    Description = description,
                    Legacy = false
                });

            await RefreshFavoritesOnlyAsync(endpoint.Trim(), switchToFavoritesTab: true);
        }

        private async void EditFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || selected.Source != ServerEndpointSource.Favorites)
            {
                return;
            }

            if (!FavoriteServerEditorDialog.ShowDialog(
                    this,
                    out string name,
                    out string endpoint,
                    out string description,
                    windowTitle: "Edit Favorite Server",
                    actionText: "SAVE",
                    defaultName: selected.Name,
                    defaultEndpoint: selected.Endpoint,
                    defaultDescription: selected.Description))
            {
                return;
            }

            if (!TryParseWebsocketEndpoint(endpoint, out string address, out int port))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use ws:// or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            int favoriteIndex = ServerEndpointCatalog.FindFavoriteIndexByEndpoint(configIniPath, selected.Endpoint);
            if (favoriteIndex < 0)
            {
                OceanyaMessageBox.Show(
                    "Selected favorite was not found. Please refresh and try again.",
                    "Favorite Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ServerEndpointCatalog.UpdateFavorite(
                configIniPath,
                favoriteIndex,
                new FavoriteServerEntry
                {
                    Name = name,
                    Address = address,
                    Port = port,
                    Description = description,
                    Legacy = false
                });

            await RefreshFavoritesOnlyAsync(endpoint.Trim(), switchToFavoritesTab: true);
        }

        private async void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || selected.Source != ServerEndpointSource.Favorites)
            {
                return;
            }

            MessageBoxResult confirmation = OceanyaMessageBox.Show(
                $"Remove favorite '{selected.Name}'?",
                "Remove Favorite",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            int favoriteIndex = ServerEndpointCatalog.FindFavoriteIndexByEndpoint(configIniPath, selected.Endpoint);
            if (favoriteIndex < 0)
            {
                return;
            }

            ServerEndpointCatalog.RemoveFavorite(configIniPath, favoriteIndex);
            await RefreshFavoritesOnlyAsync(selectEndpoint: string.Empty, switchToFavoritesTab: true);
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || !selected.IsSelectable)
            {
                return;
            }

            SelectedServer = selected;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ApplyCurrentFilter()
        {
            string filter = SearchTextBox.Text?.Trim() ?? string.Empty;

            DefaultsListView.ItemsSource = FilterEntries(ServerEndpointSource.Defaults, filter);
            AoPollListView.ItemsSource = FilterEntries(ServerEndpointSource.AoServerPoll, filter);
            FavoritesListView.ItemsSource = FilterEntries(ServerEndpointSource.Favorites, filter);
        }

        private List<ServerEndpointDefinition> FilterEntries(ServerEndpointSource source, string filter)
        {
            IEnumerable<ServerEndpointDefinition> entries = allEntries.Where(item => item.Source == source);

            if (!string.IsNullOrWhiteSpace(filter))
            {
                entries = entries.Where(item =>
                    Contains(item.Name, filter)
                    || Contains(item.Endpoint, filter)
                    || Contains(item.Description, filter));
            }

            List<ServerEndpointDefinition> ordered = entries
                .OrderByDescending(item => item.IsSelectable)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int index = 0; index < ordered.Count; index++)
            {
                ordered[index].DisplayId = index + 1;
            }

            if (sortStates.TryGetValue(source, out SortState? sortState))
            {
                return ApplySort(ordered, sortState.Column, sortState.Ascending);
            }

            return ordered;
        }

        private static List<ServerEndpointDefinition> ApplySort(
            IEnumerable<ServerEndpointDefinition> entries,
            string column,
            bool ascending)
        {
            return column switch
            {
                "ID" => ascending
                    ? entries.OrderBy(item => item.DisplayId).ToList()
                    : entries.OrderByDescending(item => item.DisplayId).ToList(),
                "Name" => ascending
                    ? entries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    : entries.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                "Players" => SortByPlayers(entries, ascending),
                _ => entries.ToList()
            };
        }

        private static List<ServerEndpointDefinition> SortByPlayers(IEnumerable<ServerEndpointDefinition> entries, bool ascending)
        {
            IEnumerable<ServerEndpointDefinition> withPlayers = entries
                .Where(item => item.OnlinePlayers.HasValue && item.MaxPlayers.HasValue);
            IEnumerable<ServerEndpointDefinition> withoutPlayers = entries
                .Where(item => !item.OnlinePlayers.HasValue || !item.MaxPlayers.HasValue);

            IEnumerable<ServerEndpointDefinition> sortedWithPlayers = ascending
                ? withPlayers
                    .OrderBy(item => item.OnlinePlayers!.Value)
                    .ThenBy(item => item.MaxPlayers!.Value)
                : withPlayers
                    .OrderByDescending(item => item.OnlinePlayers!.Value)
                    .ThenByDescending(item => item.MaxPlayers!.Value);

            return sortedWithPlayers.Concat(withoutPlayers).ToList();
        }

        private void GridHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader header || header.Content is not string columnName)
            {
                return;
            }

            if (!string.Equals(columnName, "ID", StringComparison.Ordinal)
                && !string.Equals(columnName, "Name", StringComparison.Ordinal)
                && !string.Equals(columnName, "Players", StringComparison.Ordinal))
            {
                return;
            }

            ServerEndpointSource source = GetCurrentSource();
            bool ascending = true;
            if (sortStates.TryGetValue(source, out SortState? existing)
                && string.Equals(existing.Column, columnName, StringComparison.Ordinal))
            {
                ascending = !existing.Ascending;
            }

            sortStates[source] = new SortState
            {
                Column = columnName,
                Ascending = ascending
            };

            string selectedEndpoint = GetCurrentSelectedEndpoint();
            ApplyCurrentFilter();
            SelectByEndpoint(selectedEndpoint, source, switchToMatchedTab: false);
            UpdateSelectionState();
        }

        private void SelectByEndpoint(
            string endpoint,
            ServerEndpointSource? preferredSource = null,
            bool switchToMatchedTab = true)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return;
            }

            if (preferredSource.HasValue)
            {
                ListView preferredList = GetListViewBySource(preferredSource.Value);
                if (SelectInList(preferredList, endpoint))
                {
                    if (switchToMatchedTab)
                    {
                        ServerTabs.SelectedIndex = GetTabIndex(preferredSource.Value);
                    }
                    return;
                }

                if (!switchToMatchedTab)
                {
                    return;
                }
            }

            foreach (ListView listView in GetAllListViews())
            {
                if (!SelectInList(listView, endpoint))
                {
                    continue;
                }

                if (switchToMatchedTab)
                {
                    ServerEndpointSource listSource = GetSourceByListView(listView);
                    ServerTabs.SelectedIndex = GetTabIndex(listSource);
                }

                return;
            }
        }

        private string GetCurrentSelectedEndpoint()
        {
            if (GetCurrentSelection() is ServerEndpointDefinition selected)
            {
                return selected.Endpoint;
            }

            return string.Empty;
        }

        private ServerEndpointDefinition? GetCurrentSelection()
        {
            return GetCurrentListView().SelectedItem as ServerEndpointDefinition;
        }

        private ListView GetCurrentListView()
        {
            return ServerTabs.SelectedIndex switch
            {
                0 => DefaultsListView,
                1 => AoPollListView,
                2 => FavoritesListView,
                _ => DefaultsListView
            };
        }

        private ListView GetListViewBySource(ServerEndpointSource source)
        {
            return source switch
            {
                ServerEndpointSource.Defaults => DefaultsListView,
                ServerEndpointSource.AoServerPoll => AoPollListView,
                ServerEndpointSource.Favorites => FavoritesListView,
                _ => DefaultsListView
            };
        }

        private static int GetTabIndex(ServerEndpointSource source)
        {
            return source switch
            {
                ServerEndpointSource.Defaults => 0,
                ServerEndpointSource.AoServerPoll => 1,
                ServerEndpointSource.Favorites => 2,
                _ => 0
            };
        }

        private ServerEndpointSource GetCurrentSource()
        {
            return ServerTabs.SelectedIndex switch
            {
                0 => ServerEndpointSource.Defaults,
                1 => ServerEndpointSource.AoServerPoll,
                2 => ServerEndpointSource.Favorites,
                _ => ServerEndpointSource.Defaults
            };
        }

        private static ServerEndpointSource GetSourceByListView(ListView listView)
        {
            return listView.Name switch
            {
                "DefaultsListView" => ServerEndpointSource.Defaults,
                "AoPollListView" => ServerEndpointSource.AoServerPoll,
                "FavoritesListView" => ServerEndpointSource.Favorites,
                _ => ServerEndpointSource.Defaults
            };
        }

        private static bool SelectInList(ListView listView, string endpoint)
        {
            IEnumerable<ServerEndpointDefinition> listItems = listView.ItemsSource as IEnumerable<ServerEndpointDefinition>
                ?? Enumerable.Empty<ServerEndpointDefinition>();

            ServerEndpointDefinition? match = listItems.FirstOrDefault(item =>
                string.Equals(item.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return false;
            }

            listView.SelectedItem = match;
            listView.ScrollIntoView(match);
            return true;
        }

        private async Task RefreshFavoritesOnlyAsync(string selectEndpoint, bool switchToFavoritesTab)
        {
            List<ServerEndpointDefinition> favorites = ServerEndpointCatalog.LoadFavorites(configIniPath);

            foreach (ServerEndpointDefinition favorite in favorites)
            {
                ServerEndpointDefinition? known = allEntries.FirstOrDefault(server =>
                    server.Source != ServerEndpointSource.Favorites
                    && string.Equals(server.Endpoint, favorite.Endpoint, StringComparison.OrdinalIgnoreCase));

                if (known == null)
                {
                    continue;
                }

                favorite.IsOnline = known.IsOnline;
                favorite.OnlinePlayers = known.OnlinePlayers;
                favorite.MaxPlayers = known.MaxPlayers;
            }

            allEntries.RemoveAll(server => server.Source == ServerEndpointSource.Favorites);
            allEntries.AddRange(favorites);

            ApplyCurrentFilter();

            if (switchToFavoritesTab)
            {
                ServerTabs.SelectedIndex = 2;
            }

            if (!string.IsNullOrWhiteSpace(selectEndpoint))
            {
                SelectByEndpoint(selectEndpoint, ServerEndpointSource.Favorites, switchToMatchedTab: false);
            }

            int favoriteCount = allEntries.Count(item => item.Source == ServerEndpointSource.Favorites);
            StatusTextBlock.Text = $"Favorites updated. Total favorites: {favoriteCount}.";
            UpdateSelectionState();
            await Task.CompletedTask;
        }

        private IEnumerable<ListView> GetAllListViews()
        {
            yield return DefaultsListView;
            yield return AoPollListView;
            yield return FavoritesListView;
        }

        private void UpdateSelectionState()
        {
            ServerEndpointDefinition? selected = GetCurrentSelection();
            bool hasSelection = selected != null;

            bool isFavoritesTab = ServerTabs.SelectedIndex == 2;
            if (isFavoritesTab)
            {
                AddFavoriteButton.Content = "ADD FAVORITE";
                AddFavoriteButton.IsEnabled = true;
            }
            else
            {
                AddFavoriteButton.Content = "ADD TO FAVORITES";
                AddFavoriteButton.IsEnabled = hasSelection;
            }

            bool canManageFavorite = isFavoritesTab && hasSelection && selected!.Source == ServerEndpointSource.Favorites;
            EditFavoriteButton.IsEnabled = canManageFavorite;
            RemoveFavoriteButton.IsEnabled = canManageFavorite;

            bool canSelect = hasSelection && selected!.IsSelectable;
            SelectButton.IsEnabled = canSelect;

            if (!hasSelection)
            {
                SelectionSummaryTextBlock.Text = "Select a server to inspect details.";
                SelectedEndpointTextBlock.Text = string.Empty;
                NotSelectableReasonTextBlock.Text = string.Empty;
                SetDescriptionWithLinks("No description provided.");
                return;
            }

            string selectionState = selected!.IsSelectable
                ? "Selectable"
                : "Not selectable";

            SelectionSummaryTextBlock.Text = $"{selected.SourceDisplayName} | {selected.AvailabilityText} | {selectionState}";
            SelectedEndpointTextBlock.Text = selected.Endpoint;
            NotSelectableReasonTextBlock.Text = selected.IsSelectable
                ? string.Empty
                : GetNotSelectableReason(selected);
            SetDescriptionWithLinks(string.IsNullOrWhiteSpace(selected.Description)
                ? "No description provided."
                : selected.Description);
        }

        private static string GetNotSelectableReason(ServerEndpointDefinition server)
        {
            List<string> reasons = new List<string>();

            if (!server.IsOnline)
            {
                reasons.Add("Server appears offline.");
            }

            if (server.IsOnline && (!server.OnlinePlayers.HasValue || !server.MaxPlayers.HasValue))
            {
                reasons.Add("Could not retrieve AO player counts.");
            }

            if (server.IsLegacy)
            {
                reasons.Add("Legacy TCP server (no WebSocket support).");
            }

            if (!server.IsAoClientCompatible)
            {
                reasons.Add("Server rejected AO2-compatible client probe.");
            }

            if (!InitialConfigurationWindow.IsValidServerEndpoint(server.Endpoint))
            {
                reasons.Add("Invalid WebSocket endpoint.");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("Unavailable for connection.");
            }

            return "Reason: " + string.Join(" ", reasons);
        }

        private void SetDescriptionWithLinks(string description)
        {
            DescriptionTextBlock.Inlines.Clear();
            string input = description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                DescriptionTextBlock.Inlines.Add(new Run("No description provided."));
                return;
            }

            Regex urlRegex = new Regex(@"https?://\S+", RegexOptions.IgnoreCase);
            int currentIndex = 0;
            foreach (Match match in urlRegex.Matches(input))
            {
                if (match.Index > currentIndex)
                {
                    DescriptionTextBlock.Inlines.Add(new Run(input.Substring(currentIndex, match.Index - currentIndex)));
                }

                string rawUrl = match.Value;
                string cleanUrl = rawUrl.TrimEnd('.', ',', ';', ')', ']', '}', '!', '?');

                if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out Uri? uri))
                {
                    Hyperlink hyperlink = new Hyperlink(new Run(cleanUrl))
                    {
                        NavigateUri = uri
                    };
                    hyperlink.TextDecorations = TextDecorations.Underline;
                    hyperlink.Click += DescriptionLink_Click;
                    DescriptionTextBlock.Inlines.Add(hyperlink);

                    if (rawUrl.Length > cleanUrl.Length)
                    {
                        DescriptionTextBlock.Inlines.Add(new Run(rawUrl.Substring(cleanUrl.Length)));
                    }
                }
                else
                {
                    DescriptionTextBlock.Inlines.Add(new Run(rawUrl));
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < input.Length)
            {
                DescriptionTextBlock.Inlines.Add(new Run(input.Substring(currentIndex)));
            }
        }

        private void DescriptionLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Hyperlink hyperlink || hyperlink.NavigateUri == null)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(hyperlink.NavigateUri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Failed to open server description link.", ex);
            }
        }

        private static bool TryParseWebsocketEndpoint(string endpoint, out string address, out int port)
        {
            address = string.Empty;
            port = 0;

            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return false;
            }

            bool validScheme = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            if (!validScheme)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
            {
                return false;
            }

            address = uri.Host;
            port = uri.Port;
            return true;
        }

        private static bool Contains(string source, string value)
        {
            return source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
