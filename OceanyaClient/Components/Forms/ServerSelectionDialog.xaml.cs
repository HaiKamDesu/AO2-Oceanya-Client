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
        private readonly string configIniPath;
        private readonly string initiallySelectedEndpoint;
        private readonly List<ServerEndpointDefinition> allEntries = new List<ServerEndpointDefinition>();
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

            ServerTabs.SelectedIndex = 2;
            await RefreshServersAsync(endpoint.Trim());
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

            await RefreshServersAsync(endpoint.Trim());
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
            await RefreshServersAsync(selectEndpoint: string.Empty);
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

            return entries.ToList();
        }

        private void SelectByEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return;
            }

            foreach (ListView listView in GetAllListViews())
            {
                IEnumerable<ServerEndpointDefinition> listItems = listView.ItemsSource as IEnumerable<ServerEndpointDefinition>
                    ?? Enumerable.Empty<ServerEndpointDefinition>();

                ServerEndpointDefinition? match = listItems.FirstOrDefault(item =>
                    string.Equals(item.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    continue;
                }

                ServerTabs.SelectedIndex = match.Source switch
                {
                    ServerEndpointSource.Defaults => 0,
                    ServerEndpointSource.AoServerPoll => 1,
                    ServerEndpointSource.Favorites => 2,
                    _ => 0
                };

                ListView targetList = GetCurrentListView();
                targetList.SelectedItem = match;
                targetList.ScrollIntoView(match);
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
            AddFavoriteButton.IsEnabled = isFavoritesTab;

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

            if (server.IsLegacy)
            {
                reasons.Add("Legacy TCP server (no WebSocket support).");
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
