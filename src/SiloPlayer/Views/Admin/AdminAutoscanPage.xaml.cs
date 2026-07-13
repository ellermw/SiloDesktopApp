using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAutoscanPage : Page
{
    public IReadOnlyList<string> ActivityViews { get; } = ["Scans", "Polls"];
    public IReadOnlyList<string> ActivityStatuses { get; } = ["All statuses", "Queued", "Running", "Completed", "Failed", "Cancelled", "Success", "Unresolved", "Error"];
    private bool _loaded;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    public AdminAutoscanViewModel ViewModel { get; } = App.Services.GetRequiredService<AdminAutoscanViewModel>();
    public AdminAutoscanPage() { InitializeComponent(); _refreshTimer.Tick += async (_, _) => { if (ViewModel.SelectedTabIndex == 1 && !ViewModel.IsBusy) await ViewModel.RefreshActivityAsync(); }; }
    private async void Page_Loaded(object sender, RoutedEventArgs e) { await ViewModel.LoadAsync(); _loaded = true; UpdateTabVisuals(); UpdateEnabledBadge(); _refreshTimer.Start(); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _refreshTimer.Stop(); ViewModel.Cancel(); base.OnNavigatedFrom(e); }
    private async void Enabled_Toggled(object sender, RoutedEventArgs e) { if (_loaded) { await ViewModel.SaveSettingsAsync(((ToggleSwitch)sender).IsOn); UpdateEnabledBadge(); } }
    private async void SourceEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || sender is not ToggleSwitch { Tag: AutoscanSource source } toggle || source.Enabled == toggle.IsOn) return;
        source.Enabled = toggle.IsOn;
        await ViewModel.SaveSourceAsync(source);
    }
    private async void RunNow_Click(object sender, RoutedEventArgs e) => await ViewModel.TriggerAsync();
    private async void SaveSettings_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveSettingsAsync();
    private async void RefreshActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshActivityAsync();
    private async void ActivityFilter_Changed(object sender, SelectionChangedEventArgs e) { if (_loaded) await ViewModel.ApplyActivityFiltersAsync(); }
    private async void ActivitySearch_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) await ViewModel.ApplyActivityFiltersAsync(); }
    private async void PreviousActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.ChangeActivityPageAsync(-1);
    private async void NextActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.ChangeActivityPageAsync(1);
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string raw || !int.TryParse(raw, out var index)) return;
        ViewModel.SelectedTabIndex = index;
        UpdateTabVisuals();
    }
    private void UpdateTabVisuals()
    {
        var panels = new FrameworkElement[] { SourcesPanel, ActivityPanel, ConnectionsPanel, SettingsPanel };
        var bars = new FrameworkElement[] { SourcesTabBar, ActivityTabBar, ConnectionsTabBar, SettingsTabBar };
        var buttons = new Button[] { SourcesTab, ActivityTab, ConnectionsTab, SettingsTab };
        for (var i = 0; i < panels.Length; i++)
        {
            var active = i == ViewModel.SelectedTabIndex;
            panels[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            bars[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            buttons[i].Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[active ? "PrimaryTextBrush" : "SecondaryTextBrush"];
        }
    }
    private void UpdateEnabledBadge()
    {
        EnabledBadgeText.Text = ViewModel.Enabled ? "Enabled" : "Disabled";
        EnabledBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[ViewModel.Enabled ? "AccentBrush" : "SecondaryTextBrush"];
    }

    private async void AddConnection_Click(object sender, RoutedEventArgs e) => await ShowConnectionDialogAsync(null);
    private async void EditConnection_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is AutoscanConnection c) await ShowConnectionDialogAsync(c); }
    private async Task ShowConnectionDialogAsync(AutoscanConnection? existing)
    {
        var mode = new ComboBox { Header = "Connection type", ItemsSource = new[] { "Enter own credentials", "Reuse from Requests" }, SelectedIndex = existing?.RequestIntegrationId is not null ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var integration = new ComboBox { Header = "Requests integration", ItemsSource = ViewModel.RequestIntegrations, DisplayMemberPath = "Name", SelectedItem = ViewModel.RequestIntegrations.FirstOrDefault(i => i.Id == existing?.RequestIntegrationId), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = "Name", PlaceholderText = "My Sonarr", Text = existing?.Name ?? "" };
        var kind = new ComboBox { Header = "Kind", ItemsSource = new[] { "sonarr", "radarr" }, SelectedItem = existing?.Kind ?? "sonarr", HorizontalAlignment = HorizontalAlignment.Stretch };
        var url = new TextBox { Header = "Base URL", PlaceholderText = "http://localhost:8989", Text = existing?.BaseUrl ?? "" };
        var key = new PasswordBox { Header = existing?.HasApiKey == true ? "API key (leave blank to keep current)" : "API key", PlaceholderText = "Enter API key" };
        var testResult = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var test = new Button { Content = "Test connection", HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(mode); panel.Children.Add(integration); panel.Children.Add(name); panel.Children.Add(kind); panel.Children.Add(url); panel.Children.Add(key); panel.Children.Add(test); panel.Children.Add(testResult);
        void UpdateMode() { var own = mode.SelectedIndex == 0; integration.Visibility = own ? Visibility.Collapsed : Visibility.Visible; name.Visibility = kind.Visibility = url.Visibility = key.Visibility = own ? Visibility.Visible : Visibility.Collapsed; }
        mode.SelectionChanged += (_, _) => UpdateMode(); UpdateMode();
        AutoscanConnectionInput Input()
        {
            if (mode.SelectedIndex == 1 && integration.SelectedItem is RequestIntegration linked)
                return new() { Name = linked.Name, Kind = AdminAutoscanViewModel.IntegrationKind(linked), RequestIntegrationId = linked.Id };
            return new() { Name = name.Text.Trim(), Kind = (string?)kind.SelectedItem ?? "sonarr", BaseUrl = url.Text.Trim(), ApiKeyRef = string.IsNullOrWhiteSpace(key.Password) ? null : key.Password };
        }
        test.Click += async (_, _) =>
        {
            object body = existing is not null && string.IsNullOrWhiteSpace(key.Password)
                ? new Dictionary<string, object?> { ["connection_id"] = existing.Id }
                : mode.SelectedIndex == 1 && integration.SelectedItem is RequestIntegration linked
                    ? new Dictionary<string, object?> { ["request_integration_id"] = linked.Id }
                    : new Dictionary<string, object?> { ["base_url"] = url.Text.Trim(), ["api_key_ref"] = string.IsNullOrWhiteSpace(key.Password) ? null : key.Password };
            var result = await ViewModel.TestConnectionAsync(body); testResult.Text = result?.Ok == true ? $"Connected{(string.IsNullOrWhiteSpace(result.Version) ? "" : $" · {result.Version}")}" : result?.Error ?? ViewModel.ErrorMessage ?? "Connection failed";
        };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = existing is null ? "Add connection" : "Edit connection", Content = panel, PrimaryButtonText = existing is null ? "Add" : "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && (mode.SelectedIndex == 1 ? integration.SelectedItem is not null : !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(url.Text))) await ViewModel.SaveConnectionAsync(existing, Input());
    }
    private async void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AutoscanConnection c) return;
        if (await ConfirmAsync("Delete connection?", $"Delete {c.Name}? Sources using it will lose their binding.", "Delete")) await ViewModel.DeleteConnectionAsync(c);
    }

    private async void AddSource_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.AvailableSources.Count == 0) { await MessageAsync("No scan-source plugins", "Install a scan-source plugin from Admin · Plugins first."); return; }
        var plugin = new ComboBox { Header = "Plugin", ItemsSource = ViewModel.AvailableSources, DisplayMemberPath = "DisplayName", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var connection = new ComboBox { Header = "Connection (optional)", ItemsSource = ViewModel.Connections, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        var interval = new NumberBox { Header = "Poll interval (seconds, blank = default)", Minimum = 1 };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(plugin); panel.Children.Add(connection); panel.Children.Add(interval);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Add scan source", Content = panel, PrimaryButtonText = "Add source", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && plugin.SelectedItem is AutoscanAvailableSource p)
            await ViewModel.AddSourceAsync(p, connection.SelectedItem as AutoscanConnection, double.IsNaN(interval.Value) ? null : (int)interval.Value);
    }
    private async void EditSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AutoscanSource source) return;
        var label = new TextBox { Header = "Custom label", Text = source.Label };
        var enabled = new ToggleSwitch { Header = "Enabled", IsOn = source.Enabled };
        var mode = new ComboBox { Header = "Delivery mode", ItemsSource = new[] { "poll", "webhook" }, SelectedItem = source.DeliveryMode, HorizontalAlignment = HorizontalAlignment.Stretch };
        var connection = new ComboBox { Header = "Connection", ItemsSource = ViewModel.Connections, DisplayMemberPath = "Name", SelectedItem = ViewModel.Connections.FirstOrDefault(c => c.Id == source.ConnectionId), HorizontalAlignment = HorizontalAlignment.Stretch };
        var interval = new NumberBox { Header = "Poll interval (seconds)", Minimum = 1, Value = source.PollIntervalSeconds ?? double.NaN };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(label); panel.Children.Add(enabled); panel.Children.Add(mode); panel.Children.Add(connection); panel.Children.Add(interval);
        TextBox? moviePaths = null, tvPaths = null, exclusions = null;
        if (source.PluginId == "silo.autoscan.cephfs" || source.CapabilityId == "cephfs")
        {
            string Config(string key) => source.SourceConfig.TryGetValue(key, out var value) ? value : "";
            moviePaths = new TextBox { Header = "Movie library roots", AcceptsReturn = true, MinHeight = 70, Text = Config("movie_flat_paths"), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
            tvPaths = new TextBox { Header = "TV library roots", AcceptsReturn = true, MinHeight = 70, Text = Config("tv_flat_paths"), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
            exclusions = new TextBox { Header = "Ignored paths", AcceptsReturn = true, MinHeight = 100, Text = string.IsNullOrWhiteSpace(Config("exclusions")) ? "*.partial\n*.tmp\n@eaDir\n#recycle\n.downloads\n.recyclebin\nvolumes" : Config("exclusions"), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
            var useLibraries = new Button { Content = "Use configured libraries", HorizontalAlignment = HorizontalAlignment.Left };
            useLibraries.Click += (_, _) =>
            {
                moviePaths.Text = string.Join(Environment.NewLine, ViewModel.Libraries.Where(l => l.Type.Contains("movie", StringComparison.OrdinalIgnoreCase) || l.Type.Contains("mixed", StringComparison.OrdinalIgnoreCase)).SelectMany(l => l.Paths).Distinct());
                tvPaths.Text = string.Join(Environment.NewLine, ViewModel.Libraries.Where(l => new[] { "series", "show", "tv", "mixed" }.Any(k => l.Type.Contains(k, StringComparison.OrdinalIgnoreCase))).SelectMany(l => l.Paths).Distinct());
            };
            panel.Children.Add(new TextBlock { Text = "CephFS paths & ignores", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }); panel.Children.Add(useLibraries); panel.Children.Add(moviePaths); panel.Children.Add(tvPaths); panel.Children.Add(exclusions);
        }
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Edit scan source", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            source.Label = label.Text.Trim(); source.Enabled = enabled.IsOn; source.DeliveryMode = (string?)mode.SelectedItem ?? "poll"; source.ConnectionId = (connection.SelectedItem as AutoscanConnection)?.Id; source.PollIntervalSeconds = double.IsNaN(interval.Value) ? null : (int)interval.Value;
            if (moviePaths is not null && tvPaths is not null && exclusions is not null)
                source.SourceConfig = new Dictionary<string, string> { ["movie_flat_paths"] = moviePaths.Text.Trim(), ["tv_flat_paths"] = tvPaths.Text.Trim(), ["exclusions"] = exclusions.Text.Trim() };
            await ViewModel.SaveSourceAsync(source);
        }
    }
    private async void DeleteSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AutoscanSource s && await ConfirmAsync("Delete source?", "Its polling/webhook configuration and history association will be removed.", "Delete")) await ViewModel.DeleteSourceAsync(s);
    }
    private async void EditRewrites_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AutoscanSource source) return;
        var rows = new TextBox
        {
            Header = "Path rewrites — one “remote => local” mapping per line",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 180,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Text = string.Join(Environment.NewLine, source.PathRewrites.Select(r => $"{r.From} => {r.To}")),
        };
        var suggestions = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] };
        var sync = new Button { Content = "Suggest from connected service", HorizontalAlignment = HorizontalAlignment.Left };
        sync.Click += async (_, _) =>
        {
            try
            {
                var result = await ViewModel.GetRewriteSuggestionsAsync(source);
                suggestions.Text = result.Proposed.Count == 0 ? "No new mappings suggested." : string.Join(Environment.NewLine, result.Proposed.Select(r => $"{r.From} → {r.To}"));
                if (result.Proposed.Count > 0 && string.IsNullOrWhiteSpace(rows.Text)) rows.Text = string.Join(Environment.NewLine, result.Proposed.Select(r => $"{r.From} => {r.To}"));
            }
            catch (Exception ex) { suggestions.Text = ex.Message; }
        };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(rows); panel.Children.Add(sync); panel.Children.Add(suggestions);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Path rewrites", Content = panel, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        source.PathRewrites = rows.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split("=>", 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            .Select(parts => new AutoscanPathRewrite { From = parts[0], To = parts[1] }).ToList();
        await ViewModel.SaveSourceAsync(source);
    }
    private async void ManageWebhook_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AutoscanSource source) return;
        var url = string.IsNullOrWhiteSpace(source.WebhookUrl) ? "No webhook URL has been generated." : source.WebhookUrl;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Webhook endpoint", Content = new TextBlock { Text = url, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") }, PrimaryButtonText = source.WebhookConfigured ? "Rotate URL" : "Create URL", SecondaryButtonText = source.WebhookConfigured ? "Delete URL" : "", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (source.WebhookConfigured && await ConfirmAsync("Rotate webhook URL?", "The current URL will stop working immediately.", "Rotate")) await ViewModel.RotateWebhookAsync(source);
            else if (!source.WebhookConfigured) await ViewModel.CreateWebhookAsync(source);
        }
        else if (result == ContentDialogResult.Secondary && await ConfirmAsync("Delete webhook URL?", "Incoming deliveries to the current URL will stop working.", "Delete")) await ViewModel.DeleteWebhookAsync(source);
    }
    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        var d = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task MessageAsync(string title, string content) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "OK" }.ShowAsync();
}
