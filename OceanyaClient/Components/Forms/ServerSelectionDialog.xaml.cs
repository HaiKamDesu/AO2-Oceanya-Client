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
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Interaction logic for ServerSelectionDialog.xaml
    /// </summary>
    public partial class ServerSelectionDialog : OceanyaWindowContentControl
    {
        /// <inheritdoc/>
        public override string HeaderText => "SELECT SERVER";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

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
            Title = "Select Server";
            Icon = new BitmapImage(new Uri("pack://application:,,,/OceanyaClient;component/Resources/OceanyaO.ico"));
            this.configIniPath = configIniPath;
            this.initiallySelectedEndpoint = initiallySelectedEndpoint ?? string.Empty;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshServersAsync(selectEndpoint: initiallySelectedEndpoint);
            MarkAutomationReady();
        }

        private async void RefreshPollButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshServersAsync(selectEndpoint: GetCurrentSelectedEndpoint());
        }

        private async Task RefreshServersAsync(string selectEndpoint)
        {
            // Cancel any in-progress load or background probe from a prior refresh.
            refreshCancellationTokenSource?.Cancel();
            refreshCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = refreshCancellationTokenSource.Token;

            RefreshPollButton.IsEnabled = false;
            SelectButton.IsEnabled = false;
            StatusTextBlock.Text = "Loading server list...";

            // Phase 1: Load the list (fast — no probing). The wait form is only shown for this.
            try
            {
                await WaitForm.ShowFormAsync("Loading servers...", this);
                List<ServerEndpointDefinition> loadedEntries = await ServerEndpointCatalog.LoadListAsync(configIniPath, cancellationToken);
                allEntries.Clear();
                allEntries.AddRange(loadedEntries);

                ApplyCurrentFilter();
                SelectByEndpoint(selectEndpoint);

                int total = allEntries.Count;
                int pollCount = allEntries.Count(item => item.Source == ServerEndpointSource.AoServerPoll);
                int favoriteCount = allEntries.Count(item => item.Source == ServerEndpointSource.Favorites);
                int defaultCount = allEntries.Count(item => item.Source == ServerEndpointSource.Defaults);
                StatusTextBlock.Text = $"Loaded {total} servers (Defaults: {defaultCount}, AO Poll: {pollCount}, Favorites: {favoriteCount}). Checking status...";
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Server list refresh was canceled.";
                RefreshPollButton.IsEnabled = true;
                UpdateSelectionState();
                await WaitForm.CloseFormAsync();
                return;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to load server entries.", ex);
                StatusTextBlock.Text = "Failed to load server list.";
                RefreshPollButton.IsEnabled = true;
                UpdateSelectionState();
                await WaitForm.CloseFormAsync();
                return;
            }
            finally
            {
                // Always re-enable Refresh and update selection state before probing starts.
                RefreshPollButton.IsEnabled = true;
                UpdateSelectionState();
                await WaitForm.CloseFormAsync();
            }

            // Phase 2: Probe all servers in the background. No wait form — the UI is fully interactive.
            // Each probe result updates the server's PingStatus on the UI thread via INPC, so rows
            // change from yellow → green/red as probes complete.
            _ = ProbeServersAsync(
                allEntries.ToList(),
                cancellationToken,
                "No servers to probe.",
                probedServers =>
                {
                    int onlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Online);
                    int offlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Offline);
                    int incompatibleCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.IncompatibleClient);
                    return $"Status check complete. Online: {onlineCount}, Offline: {offlineCount}, Incompatible: {incompatibleCount}.";
                });
        }

        /// <summary>
        /// Probes all servers in parallel (up to AoProbeConcurrency at a time) on background threads,
        /// dispatching each result back to the UI thread so the ListView updates live via INPC.
        /// </summary>
        private async Task ProbeServersAsync(
            List<ServerEndpointDefinition> servers,
            CancellationToken cancellationToken,
            string noServersStatusText,
            Func<List<ServerEndpointDefinition>, string> completionStatusTextFactory)
        {
            List<EndpointProbeBatch> batches = ServerEndpointCatalog.CreateProbeBatches(servers);
            if (batches.Count == 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    StatusTextBlock.Text = noServersStatusText;
                    UpdateSelectionState();
                });
                return;
            }

            int probeConcurrency = 12;
            List<Task> tasks = new List<Task>();
            using SemaphoreSlim semaphore = new SemaphoreSlim(probeConcurrency);

            foreach (EndpointProbeBatch batch in batches)
            {
                EndpointProbeBatch capturedBatch = batch;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        // Mark batch as pinging on the UI thread before starting the probe.
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (ServerEndpointDefinition server in capturedBatch.Servers)
                            {
                                server.OnlinePlayers = null;
                                server.MaxPlayers = null;
                                server.LatencyMilliseconds = null;
                                server.PingStatus = ServerPingStatus.Pinging;
                            }
                        });

                        Stopwatch stopwatch = Stopwatch.StartNew();
                        (bool success, int? players, int? maxPlayers, bool incompatibleClient) =
                            await ServerEndpointCatalog.ProbeEndpointAsync(capturedBatch.Endpoint, cancellationToken);
                        stopwatch.Stop();

                        ServerPingStatus newStatus = incompatibleClient
                            ? ServerPingStatus.IncompatibleClient
                            : success
                                ? ServerPingStatus.Online
                                : ServerPingStatus.Offline;
                        int? newPlayers = success ? players : null;
                        int? newMax = success ? maxPlayers : null;
                        int? latency = (success || incompatibleClient)
                            ? Math.Max(0, (int)Math.Round(stopwatch.Elapsed.TotalMilliseconds))
                            : null;

                        // Apply result on the UI thread so INPC updates the ListView rows.
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (ServerEndpointDefinition server in capturedBatch.Servers)
                            {
                                server.OnlinePlayers = newPlayers;
                                server.MaxPlayers = newMax;
                                server.LatencyMilliseconds = latency;
                                server.PingStatus = newStatus;
                            }

                            // Refresh selection state so the SELECT button enables as soon as
                            // the currently selected server's probe completes.
                            UpdateSelectionState();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        // Refresh was cancelled — leave remaining servers in Unknown/Pinging state.
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Normal on refresh cancel.
                return;
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Background server probe encountered errors.", ex);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = completionStatusTextFactory(servers);
                UpdateSelectionState();
            });
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
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || !IsSelectableForCurrentMode(selected))
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

                if (!TryParseDirectConnectionEndpoint(
                        selectedServer.Endpoint,
                        out string selectedAddress,
                        out int selectedPort,
                        out bool selectedLegacy,
                        out bool selectedSecure))
                {
                    OceanyaMessageBox.Show(
                        "Only direct-connect TCP or WebSocket servers can be added to favorites.",
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
                        Legacy = selectedLegacy,
                        Secure = selectedSecure
                    });

                await RefreshFavoritesOnlyAsync(
                    selectEndpoint: selectedServer.Endpoint,
                    switchToFavoritesTab: true,
                    affectedEndpoints: new[] { selectedServer.Endpoint });
                return;
            }

            if (!TryShowFavoriteEditor(
                    "Add Favorite Server",
                    "ADD FAVORITE",
                    out string name,
                    out string endpoint,
                    out string description))
            {
                return;
            }

            if (!TryParseDirectConnectionEndpoint(endpoint, out string address, out int port, out bool legacy, out bool secure))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use tcp://, ws://, or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (TrySelectExistingFavorite(endpoint.Trim(), excludedFavoriteIndex: null))
            {
                OceanyaMessageBox.Show(
                    "That endpoint is already saved in your favorites.",
                    "Duplicate Favorite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                    Legacy = legacy,
                    Secure = secure
                });

            await RefreshFavoritesOnlyAsync(
                selectEndpoint: endpoint.Trim(),
                switchToFavoritesTab: true,
                affectedEndpoints: new[] { endpoint.Trim() });
        }

        private async void EditFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || selected.Source != ServerEndpointSource.Favorites)
            {
                return;
            }

            if (!TryShowFavoriteEditor(
                    "Edit Favorite Server",
                    "SAVE",
                    out string name,
                    out string endpoint,
                    out string description,
                    selected.Name,
                    selected.Endpoint,
                    selected.Description))
            {
                return;
            }

            if (!TryParseDirectConnectionEndpoint(endpoint, out string address, out int port, out bool legacy, out bool secure))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use tcp://, ws://, or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!ServerEndpointCatalog.TryGetFavoriteIndex(selected, out int favoriteIndex))
            {
                OceanyaMessageBox.Show(
                    "Selected favorite was not found. Please refresh and try again.",
                    "Favorite Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (TrySelectExistingFavorite(endpoint.Trim(), favoriteIndex))
            {
                OceanyaMessageBox.Show(
                    "Another favorite already uses that endpoint.",
                    "Duplicate Favorite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                    Legacy = legacy,
                    Secure = secure
                });

            await RefreshFavoritesOnlyAsync(
                selectEndpoint: endpoint.Trim(),
                switchToFavoritesTab: true,
                affectedEndpoints: new[] { selected.Endpoint, endpoint.Trim() });
        }

        private async void DuplicateFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || selected.Source != ServerEndpointSource.Favorites)
            {
                return;
            }

            if (!TryShowFavoriteEditor(
                    "Duplicate Favorite Server",
                    "ADD FAVORITE",
                    out string name,
                    out string endpoint,
                    out string description,
                    $"{selected.Name} Copy",
                    selected.Endpoint,
                    selected.Description))
            {
                return;
            }

            if (!TryParseDirectConnectionEndpoint(endpoint, out string address, out int port, out bool legacy, out bool secure))
            {
                OceanyaMessageBox.Show(
                    "Invalid endpoint format. Use tcp://, ws://, or wss:// and include host/port.",
                    "Invalid Endpoint",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (TrySelectExistingFavorite(endpoint.Trim(), excludedFavoriteIndex: null))
            {
                OceanyaMessageBox.Show(
                    "That endpoint is already saved in your favorites.",
                    "Duplicate Favorite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                    Legacy = legacy,
                    Secure = secure
                });

            await RefreshFavoritesOnlyAsync(
                selectEndpoint: endpoint.Trim(),
                switchToFavoritesTab: true,
                affectedEndpoints: new[] { endpoint.Trim() });
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

            if (!ServerEndpointCatalog.TryGetFavoriteIndex(selected, out int favoriteIndex))
            {
                return;
            }

            ServerEndpointCatalog.RemoveFavorite(configIniPath, favoriteIndex);
            await RefreshFavoritesOnlyAsync(
                selectEndpoint: string.Empty,
                switchToFavoritesTab: true,
                affectedEndpoints: new[] { selected.Endpoint });
        }

        private async void RefreshServerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            List<ServerEndpointDefinition> selectedServers = GetCurrentSelections();
            if (selectedServers.Count == 0)
            {
                return;
            }

            string refreshTargetLabel = selectedServers.Count == 1
                ? $"'{selectedServers[0].Name}'"
                : $"{selectedServers.Count} selected servers";
            StatusTextBlock.Text = $"Refreshing {refreshTargetLabel}...";
            await ProbeServersAsync(
                selectedServers,
                CancellationToken.None,
                "Selected servers cannot be refreshed.",
                probedServers =>
                {
                    if (probedServers.Count == 1)
                    {
                        ServerEndpointDefinition server = probedServers[0];
                        return $"Refreshed '{server.Name}'. {server.AvailabilityText}.";
                    }

                    int onlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Online);
                    int offlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Offline);
                    int incompatibleCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.IncompatibleClient);
                    return $"Refreshed {probedServers.Count} selected servers. Online: {onlineCount}, Offline: {offlineCount}, Incompatible: {incompatibleCount}.";
                });
        }

        private async void RefreshCurrentTabMenuItem_Click(object sender, RoutedEventArgs e)
        {
            IEnumerable<ServerEndpointDefinition> currentEntries = GetCurrentListView().ItemsSource as IEnumerable<ServerEndpointDefinition>
                ?? Enumerable.Empty<ServerEndpointDefinition>();
            List<ServerEndpointDefinition> targets = currentEntries.ToList();
            string tabName = GetCurrentTabHeaderText();

            StatusTextBlock.Text = $"Refreshing {tabName}...";
            await ProbeServersAsync(
                targets,
                CancellationToken.None,
                $"No {tabName} servers to refresh.",
                probedServers =>
                {
                    int onlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Online);
                    int offlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Offline);
                    int incompatibleCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.IncompatibleClient);
                    return $"{tabName} refreshed. Online: {onlineCount}, Offline: {offlineCount}, Incompatible: {incompatibleCount}.";
                });
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected || !IsSelectableForCurrentMode(selected))
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
            if (e.OriginalSource is DependencyObject source)
            {
                for (DependencyObject? current = source; current != null;)
                {
                    if (current.GetType().Name.Contains("Button", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (current is FrameworkElement element)
                    {
                        current = element.Parent ?? element.TemplatedParent as DependencyObject;
                    }
                    else
                    {
                        break;
                    }
                }
            }

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

            // Default order: preserve original list order from AO2 (ListIndex).
            // DisplayId is re-assigned sequentially after ordering so the ID column
            // always reflects position in the current view.
            List<ServerEndpointDefinition> ordered = entries
                .OrderBy(item => item.ListIndex)
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
                "Ping" => SortByPing(entries, ascending),
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

        private static List<ServerEndpointDefinition> SortByPing(IEnumerable<ServerEndpointDefinition> entries, bool ascending)
        {
            IEnumerable<ServerEndpointDefinition> withPing = entries.Where(item => item.LatencyMilliseconds.HasValue);
            IEnumerable<ServerEndpointDefinition> withoutPing = entries.Where(item => !item.LatencyMilliseconds.HasValue);
            IEnumerable<ServerEndpointDefinition> sortedWithPing = ascending
                ? withPing.OrderBy(item => item.LatencyMilliseconds!.Value)
                : withPing.OrderByDescending(item => item.LatencyMilliseconds!.Value);

            return sortedWithPing.Concat(withoutPing).ToList();
        }

        private void GridHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader header || header.Content is not string columnName)
            {
                return;
            }

            if (!string.Equals(columnName, "ID", StringComparison.Ordinal)
                && !string.Equals(columnName, "Name", StringComparison.Ordinal)
                && !string.Equals(columnName, "Players", StringComparison.Ordinal)
                && !string.Equals(columnName, "Ping", StringComparison.Ordinal))
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

        private async Task RefreshFavoritesOnlyAsync(
            string selectEndpoint,
            bool switchToFavoritesTab,
            IEnumerable<string>? affectedEndpoints = null)
        {
            await WaitForm.ShowFormAsync("Updating favorites...", this);
            List<ServerEndpointDefinition> newFavorites;
            try
            {
                newFavorites = ServerEndpointCatalog.LoadFavorites(configIniPath);

                // Copy any already-known status from currently loaded entries so the
                // newly loaded favorites don't flash Unknown needlessly when we already
                // have fresh probe data for that endpoint.
                Dictionary<string, ServerEndpointDefinition> knownByEndpoint = allEntries
                    .Where(s => s.PingStatus != ServerPingStatus.Unknown && s.PingStatus != ServerPingStatus.Pinging)
                    .GroupBy(s => s.Endpoint, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (ServerEndpointDefinition favorite in newFavorites)
                {
                    if (!knownByEndpoint.TryGetValue(favorite.Endpoint, out ServerEndpointDefinition? known))
                    {
                        continue;
                    }

                    favorite.PingStatus = known.PingStatus;
                    favorite.OnlinePlayers = known.OnlinePlayers;
                    favorite.MaxPlayers = known.MaxPlayers;
                }

                allEntries.RemoveAll(server => server.Source == ServerEndpointSource.Favorites);
                allEntries.AddRange(newFavorites);

                ApplyCurrentFilter();
                FavoritesListView.Items.Refresh();

                if (switchToFavoritesTab)
                {
                    ServerTabs.SelectedIndex = 2;
                }

                if (!string.IsNullOrWhiteSpace(selectEndpoint))
                {
                    SelectByEndpoint(selectEndpoint, ServerEndpointSource.Favorites, switchToMatchedTab: false);
                }

                int favoriteCount = allEntries.Count(item => item.Source == ServerEndpointSource.Favorites);
                StatusTextBlock.Text = $"Favorites updated ({favoriteCount} total). Checking affected servers...";
                UpdateSelectionState();
            }
            finally
            {
                await WaitForm.CloseFormAsync();
            }

            // Probe only the affected endpoints in the background so the user immediately
            // sees status for the server they just added or edited.
            HashSet<string> affectedSet = new HashSet<string>(
                affectedEndpoints?.Where(ep => !string.IsNullOrWhiteSpace(ep)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (affectedSet.Count > 0)
            {
                List<ServerEndpointDefinition> targets = allEntries
                    .Where(s => affectedSet.Contains(s.Endpoint))
                    .ToList();

                CancellationToken probeCancellation = refreshCancellationTokenSource?.Token ?? CancellationToken.None;
                _ = ProbeServersAsync(
                    targets,
                    probeCancellation,
                    "No affected favorite servers to probe.",
                    probedServers =>
                    {
                        int onlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Online);
                        int offlineCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.Offline);
                        int incompatibleCount = probedServers.Count(s => s.PingStatus == ServerPingStatus.IncompatibleClient);
                        return $"Favorite server refresh complete. Online: {onlineCount}, Offline: {offlineCount}, Incompatible: {incompatibleCount}.";
                    });
            }
        }

        private bool TryShowFavoriteEditor(
            string windowTitle,
            string actionText,
            out string name,
            out string endpoint,
            out string description,
            string defaultName = "My Favorite",
            string defaultEndpoint = "ws://127.0.0.1:27016",
            string defaultDescription = "")
        {
            return FavoriteServerEditorDialog.ShowDialog(
                this,
                out name,
                out endpoint,
                out description,
                windowTitle,
                actionText,
                defaultName,
                defaultEndpoint,
                defaultDescription);
        }

        private void ServerListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView listView)
            {
                return;
            }

            ListViewItem? item = FindParentListViewItem(e.OriginalSource as DependencyObject);
            if (item == null)
            {
                e.Handled = true;
                return;
            }

            if (!item.IsSelected)
            {
                listView.SelectedItems.Clear();
                item.IsSelected = true;
            }

            listView.Focus();
        }

        private void ServerListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not ListView)
            {
                return;
            }

            if (FindParentListViewItem(Mouse.DirectlyOver as DependencyObject) == null)
            {
                e.Handled = true;
            }
        }

        private void CopyIpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentSelection() is not ServerEndpointDefinition selected)
            {
                return;
            }

            string textToCopy = selected.Endpoint;
            if (Uri.TryCreate(selected.Endpoint, UriKind.Absolute, out Uri? uri) && uri != null)
            {
                textToCopy = uri.IsDefaultPort
                    ? uri.Host
                    : $"{uri.Host}:{uri.Port}";
            }

            Clipboard.SetText(textToCopy);
            StatusTextBlock.Text = $"Copied '{textToCopy}' to clipboard.";
        }

        private IEnumerable<ListView> GetAllListViews()
        {
            yield return DefaultsListView;
            yield return AoPollListView;
            yield return FavoritesListView;
        }

        private List<ServerEndpointDefinition> GetCurrentSelections()
        {
            return GetCurrentListView().SelectedItems
                .OfType<ServerEndpointDefinition>()
                .ToList();
        }

        private string GetCurrentTabHeaderText()
        {
            if (ServerTabs.SelectedItem is TabItem selectedTab && selectedTab.Header is string header)
            {
                return header;
            }

            return GetCurrentSource().ToString();
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

            bool canSelect = hasSelection && IsSelectableForCurrentMode(selected!);
            SelectButton.IsEnabled = canSelect;

            if (!hasSelection)
            {
                SelectionSummaryTextBlock.Text = "Select a server to inspect details.";
                SelectedEndpointTextBlock.Text = string.Empty;
                NotSelectableReasonTextBlock.Text = string.Empty;
                SetDescriptionWithLinks("No description provided.");
                return;
            }

            bool isSelectable = IsSelectableForCurrentMode(selected!);

            string selectionState = isSelectable
                ? "Selectable"
                : "Not selectable";

            SelectionSummaryTextBlock.Text = $"{selected.SourceDisplayName} | {selected.AvailabilityText} | {selectionState}";
            SelectedEndpointTextBlock.Text = selected.Endpoint;
            NotSelectableReasonTextBlock.Text = isSelectable
                ? string.Empty
                : ServerEndpointCatalog.GetNotSelectableReason(selected);
            SetDescriptionWithLinks(string.IsNullOrWhiteSpace(selected.Description)
                ? "No description provided."
                : selected.Description);
        }

        private static bool IsSelectableForCurrentMode(ServerEndpointDefinition selected)
        {
            if (selected.IsSelectable)
            {
                return true;
            }

            return OceanyaTestMode.Current.IsEnabled
                && OceanyaTestMode.Current.SkipServerValidation
                && selected.SupportsDirectConnection;
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

        private bool TrySelectExistingFavorite(string endpoint, int? excludedFavoriteIndex)
        {
            ServerEndpointDefinition? existingFavorite = allEntries
                .Where(server => server.Source == ServerEndpointSource.Favorites)
                .FirstOrDefault(server =>
                    !string.IsNullOrWhiteSpace(server.Endpoint)
                    && string.Equals(server.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase)
                    && (!ServerEndpointCatalog.TryGetFavoriteIndex(server, out int serverIndex)
                        || !excludedFavoriteIndex.HasValue
                        || serverIndex != excludedFavoriteIndex.Value));

            if (existingFavorite == null)
            {
                return false;
            }

            ServerTabs.SelectedIndex = 2;
            SelectByEndpoint(existingFavorite.Endpoint, ServerEndpointSource.Favorites, switchToMatchedTab: false);
            UpdateSelectionState();
            return true;
        }

        private static bool TryParseDirectConnectionEndpoint(
            string endpoint,
            out string address,
            out int port,
            out bool legacy,
            out bool secure)
        {
            return ServerEndpointCatalog.TryParseDirectEndpoint(endpoint, out address, out port, out legacy, out secure);
        }

        private static bool Contains(string source, string value)
        {
            return source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ListViewItem? FindParentListViewItem(DependencyObject? source)
        {
            while (source != null && source is not ListViewItem)
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }

            return source as ListViewItem;
        }
    }
}
