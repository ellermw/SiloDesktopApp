using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAutoscanPage : Page
{
    public IReadOnlyList<string> ActivityViews { get; } = ["Scans", "Polls"];
    public IReadOnlyList<string> ScanActivityStatuses { get; } = ["All statuses", "Queued", "Running", "Completed", "Failed", "Cancelled"];
    public IReadOnlyList<string> PollActivityStatuses { get; } = ["All statuses", "Running", "Success", "Unresolved", "Error"];
    public IReadOnlyList<int> ActivityPageSizes { get; } = [25, 50, 100];
    private bool _loaded;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _activitySearchTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<string, AdminScanRun> _liveScans = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _eventSubscription;
    private EventChannelClient? _eventChannel;
    private static readonly JsonSerializerOptions ScanJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public AdminAutoscanViewModel ViewModel { get; } = App.Services.GetRequiredService<AdminAutoscanViewModel>();
    public AdminAutoscanPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _activitySearchTimer.Tick += async (_, _) =>
        {
            _activitySearchTimer.Stop();
            if (_loaded && ViewModel.SelectedTabIndex == 1) await ViewModel.ApplyActivityFiltersAsync();
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (ViewModel.SelectedTabIndex == 1 && !ViewModel.IsBusy)
                await ViewModel.RefreshActivityAsync();
        };
    }
    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width);
        AdminPageContent.Width = Math.Min(1640, width);
        var horizontalPadding = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
        var verticalPadding = width >= 1024 ? 32 : 16;
        AdminPageContent.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, 56);
        var contentWidth = Math.Max(0, width - (horizontalPadding * 2));
        var wrapHeader = contentWidth < 820;
        Grid.SetColumn(PageHeaderCopy, 0);
        Grid.SetColumnSpan(PageHeaderCopy, wrapHeader ? 2 : 1);
        Grid.SetColumn(PageHeaderActions, wrapHeader ? 0 : 1);
        Grid.SetColumnSpan(PageHeaderActions, wrapHeader ? 2 : 1);
        Grid.SetRow(PageHeaderActions, wrapHeader ? 1 : 0);
        PageHeaderActions.HorizontalAlignment = wrapHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PageTitleText.FontSize = contentWidth < 640 ? 30 : 36;
    }
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        UpdateTabVisuals();
        UpdateEnabledBadge();
        UpdateActivityViewVisuals();
        await ViewModel.LoadAsync();
        _loaded = true;
        AutoscanToggle.IsEnabled = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage);
        if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
            App.Services.GetRequiredService<ToastService>().Error(ViewModel.ErrorMessage);
        AttachScanEvents();
        if (ViewModel.SelectedTabIndex == 1) await ViewModel.EnsureActivityLoadedAsync();
        else if (ViewModel.SelectedTabIndex == 2) await ViewModel.EnsureRequestIntegrationsLoadedAsync();
        UpdateTabVisuals();
        UpdateEnabledBadge();
        UpdateActivityViewVisuals();
        _refreshTimer.Start();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loaded = false;
        _refreshTimer.Stop();
        _activitySearchTimer.Stop();
        ViewModel.Cancel();
        if (_eventChannel is not null)
        {
            _eventChannel.SnapshotReceived -= OnScanSnapshotReceived;
            _eventChannel.EventReceived -= OnScanEventReceived;
        }
        _eventSubscription?.Dispose();
        _eventSubscription = null;
        _eventChannel = null;
        base.OnNavigatedFrom(e);
    }
    private async void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        AutoscanToggle.IsEnabled = false;
        try { await ViewModel.SaveSettingsAsync(((ToggleSwitch)sender).IsOn); UpdateEnabledBadge(); }
        finally { AutoscanToggle.IsEnabled = true; }
    }
    private async void SourceEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || sender is not ToggleSwitch { Tag: AutoscanSource source } toggle || source.Enabled == toggle.IsOn) return;
        source.Enabled = toggle.IsOn;
        await ViewModel.SaveSourceAsync(source);
    }
    private async void SourceLabel_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded || sender is not TextBox { Tag: AutoscanSource source } text) return;
        var next = text.Text.Trim();
        if (string.Equals(source.Label, next, StringComparison.Ordinal)) return;
        source.Label = next;
        await ViewModel.SaveSourceAsync(source);
    }
    private async void SourceInterval_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded || sender is not NumberBox { Tag: AutoscanSource source } number) return;
        int? next = double.IsNaN(number.Value) || string.IsNullOrWhiteSpace(number.Text) ? null : Math.Max(1, (int)number.Value);
        if (source.PollIntervalSeconds == next) return;
        source.PollIntervalSeconds = next;
        await ViewModel.SaveSourceAsync(source);
    }
    private async void SourceConnection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || sender is not ComboBox { Tag: AutoscanSource source } combo) return;
        var next = (combo.SelectedItem as AutoscanConnectionOption)?.Id;
        if (string.Equals(source.ConnectionId, next, StringComparison.OrdinalIgnoreCase)) return;
        source.ConnectionId = next;
        await ViewModel.SaveSourceAsync(source);
    }
    private async void UseConfiguredLibraries_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || sender is not Button { Tag: AutoscanSource source }) return;
        await ViewModel.EnsureLibrariesLoadedAsync();
        static bool IsMovie(SiloPlayer.Core.Models.Catalog.Library library) => library.Type.Contains("movie", StringComparison.OrdinalIgnoreCase) || library.Type.Contains("mixed", StringComparison.OrdinalIgnoreCase);
        static bool IsTv(SiloPlayer.Core.Models.Catalog.Library library) => new[] { "series", "show", "tv", "mixed" }.Any(kind => library.Type.Contains(kind, StringComparison.OrdinalIgnoreCase));
        var next = new Dictionary<string, string>(source.SourceConfig)
        {
            ["movie_flat_paths"] = string.Join(Environment.NewLine, ViewModel.Libraries.Where(IsMovie).SelectMany(library => library.Paths).Distinct(StringComparer.OrdinalIgnoreCase)),
            ["tv_flat_paths"] = string.Join(Environment.NewLine, ViewModel.Libraries.Where(IsTv).SelectMany(library => library.Paths).Distinct(StringComparer.OrdinalIgnoreCase)),
        };
        source.SourceConfig = next;
        await ViewModel.SaveSourceAsync(source);
    }
    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        RunNowButton.IsEnabled = false;
        RunNowText.Text = "Triggering…";
        try { await ViewModel.TriggerAsync(); }
        finally { RunNowText.Text = "Run now"; RunNowButton.IsEnabled = true; }
    }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveSettingsAsync();
    private async void SettingsNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loaded) await ViewModel.SaveSettingsAsync();
    }
    private async void RefreshActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshActivityAsync();
    private async void ActivityFilter_Changed(object sender, SelectionChangedEventArgs e) { if (_loaded) await ViewModel.ApplyActivityFiltersAsync(); }
    private void ActivitySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || ViewModel.SelectedTabIndex != 1) return;
        _activitySearchTimer.Stop();
        _activitySearchTimer.Start();
    }
    private async void PreviousActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.ChangeActivityPageAsync(-1);
    private async void NextActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.ChangeActivityPageAsync(1);
    private async void FirstActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.GoToActivityPageAsync(0);
    private async void LastActivity_Click(object sender, RoutedEventArgs e) => await ViewModel.GoToActivityPageAsync(int.MaxValue);
    private async void ResetActivity_Click(object sender, RoutedEventArgs e)
    {
        _activitySearchTimer.Stop();
        await ViewModel.ResetActivityFiltersAsync();
    }
    private async void ActivityView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string view } || string.Equals(ViewModel.ActivityView, view, StringComparison.Ordinal)) return;
        ViewModel.ActivityView = view;
        ActivityStatusCombo.ItemsSource = view == "Scans" ? ScanActivityStatuses : PollActivityStatuses;
        ActivityStatusCombo.SelectedIndex = 0;
        UpdateActivityViewVisuals();
        await ViewModel.RefreshActivityAsync();
    }
    private async void ActivityPageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || sender is not ComboBox { SelectedItem: int pageSize } || pageSize == ViewModel.ActivityPageSize) return;
        await ViewModel.SetActivityPageSizeAsync(pageSize);
    }
    private async void Tab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string raw || !int.TryParse(raw, out var index)) return;
        ViewModel.SelectedTabIndex = index;
        UpdateTabVisuals();
        if (index == 1) await ViewModel.EnsureActivityLoadedAsync();
        else if (index == 2) await ViewModel.EnsureRequestIntegrationsLoadedAsync();
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
    private void UpdateActivityViewVisuals()
    {
        if (ScansActivityButton is null || PollsActivityButton is null) return;
        var scans = ViewModel.ActivityView == "Scans";
        var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ScansActivityButton.Background = scans ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"] : transparent;
        PollsActivityButton.Background = scans ? transparent : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"];
        if (ActivitySearchBox is not null) ActivitySearchBox.PlaceholderText = scans ? "Search scan history" : "Search poll log";
    }

    private void AttachScanEvents()
    {
        try
        {
            _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
            _eventChannel.SnapshotReceived += OnScanSnapshotReceived;
            _eventChannel.EventReceived += OnScanEventReceived;
            _eventSubscription = _eventChannel.Subscribe("scans");
            if (_eventChannel.TryGetLatestSnapshot("scans", out var cached))
                OnScanSnapshotReceived("scans", cached);
        }
        catch { }
    }

    private void OnScanSnapshotReceived(string channel, JsonElement data)
    {
        if (channel != "scans" || data.ValueKind != JsonValueKind.Array) return;
        try
        {
            var scans = data.Deserialize<List<AdminScanRun>>(ScanJsonOptions) ?? [];
            DispatcherQueue.TryEnqueue(() =>
            {
                _liveScans.Clear();
                foreach (var scan in scans) _liveScans[scan.Id] = scan;
                ViewModel.ApplyActiveScanSnapshot(_liveScans.Values);
            });
        }
        catch { }
    }

    private void OnScanEventReceived(string channel, string eventName, JsonElement data)
    {
        if (channel != "scans") return;
        if (eventName == "snapshot")
        {
            OnScanSnapshotReceived(channel, data);
            return;
        }
        if (data.ValueKind != JsonValueKind.Object) return;
        try
        {
            var scan = data.Deserialize<AdminScanRun>(ScanJsonOptions);
            if (scan is null || string.IsNullOrWhiteSpace(scan.Id)) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                _liveScans[scan.Id] = scan;
                ViewModel.ApplyActiveScanSnapshot(_liveScans.Values);
            });
        }
        catch { }
    }

    private async void CancelActiveScan_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AutoscanScan scan)
            await ViewModel.CancelActiveScansAsync(scan.LibraryId);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loaded) return;
        var toasts = App.Services.GetRequiredService<ToastService>();
        if (e.PropertyName == nameof(ViewModel.StatusMessage) && !string.IsNullOrWhiteSpace(ViewModel.StatusMessage))
            toasts.Success(ViewModel.StatusMessage);
        else if (e.PropertyName == nameof(ViewModel.ErrorMessage) && !string.IsNullOrWhiteSpace(ViewModel.ErrorMessage))
            toasts.Error(ViewModel.ErrorMessage);
    }

    private async void AddConnection_Click(object sender, RoutedEventArgs e) => await ShowConnectionDialogAsync(null);
    private async void EditConnection_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is AutoscanConnection c) await ShowConnectionDialogAsync(c); }
    private async Task ShowConnectionDialogAsync(AutoscanConnection? existing)
    {
        await ViewModel.EnsureRequestIntegrationsLoadedAsync();
        var mode = new ComboBox { Header = "Connection type", ItemsSource = new[] { "Enter own credentials", "Reuse from Requests" }, SelectedIndex = existing?.RequestIntegrationId is not null ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Visibility = existing is null ? Visibility.Visible : Visibility.Collapsed;
        var integration = new ComboBox { Header = "Requests integration", ItemsSource = ViewModel.RequestIntegrations, DisplayMemberPath = "Name", SelectedItem = ViewModel.RequestIntegrations.FirstOrDefault(i => i.Id == existing?.RequestIntegrationId), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = "Name", PlaceholderText = "My Sonarr", Text = existing?.Name ?? "" };
        var kind = new ComboBox { Header = "Kind", ItemsSource = new[] { "sonarr", "radarr" }, SelectedItem = existing?.Kind ?? "sonarr", HorizontalAlignment = HorizontalAlignment.Stretch };
        var url = new TextBox { Header = "Base URL", PlaceholderText = "http://localhost:8989", Text = existing?.BaseUrl ?? "" };
        var key = new PasswordBox { Header = existing?.HasApiKey == true ? "API key (leave blank to keep current)" : "API key", PlaceholderText = "Enter API key" };
        var testResult = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var test = new Button { Content = "Test connection", HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Reuse a server you already configured in Requests (Sonarr/Radarr), or enter your own credentials.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
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
            var result = await ViewModel.TestConnectionAsync(body);
            testResult.Text = result?.Ok == true ? $"Connected{(string.IsNullOrWhiteSpace(result.Version) ? "" : $" (v{result.Version})")}" : result?.Error ?? ViewModel.ErrorMessage ?? "Connection failed";
            testResult.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[result?.Ok == true ? "SuccessBrush" : "ErrorBrush"];
        };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = existing is null ? "Add connection" : "Edit connection", Content = panel, PrimaryButtonText = existing is null ? "Add connection" : "Save changes", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        void UpdateCanSave()
        {
            dialog.IsPrimaryButtonEnabled = mode.SelectedIndex == 1
                ? integration.SelectedItem is not null
                : !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(url.Text)
                    && (!string.IsNullOrWhiteSpace(key.Password) || existing?.HasApiKey == true);
        }
        mode.SelectionChanged += (_, _) => UpdateCanSave();
        integration.SelectionChanged += (_, _) => UpdateCanSave();
        name.TextChanged += (_, _) => UpdateCanSave();
        url.TextChanged += (_, _) => UpdateCanSave();
        key.PasswordChanged += (_, _) => UpdateCanSave();
        UpdateCanSave();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.SaveConnectionAsync(existing, Input());
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
        var webhookHelp = new TextBlock { Text = "Webhook sources receive Sonarr/Radarr events through a generated URL and do not use a polling connection or interval.", TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"], Visibility = Visibility.Collapsed };
        void UpdatePluginFields()
        {
            var isWebhook = plugin.SelectedItem is AutoscanAvailableSource selected && selected.PluginId.Equals("silo.autoscan.arr-webhook", StringComparison.OrdinalIgnoreCase);
            connection.Visibility = interval.Visibility = isWebhook ? Visibility.Collapsed : Visibility.Visible;
            webhookHelp.Visibility = isWebhook ? Visibility.Visible : Visibility.Collapsed;
        }
        plugin.SelectionChanged += (_, _) => UpdatePluginFields();
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(plugin); panel.Children.Add(webhookHelp); panel.Children.Add(connection); panel.Children.Add(interval);
        UpdatePluginFields();
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
            {
                var config = new Dictionary<string, string>(source.SourceConfig)
                {
                    ["movie_flat_paths"] = moviePaths.Text.Trim(),
                    ["tv_flat_paths"] = tvPaths.Text.Trim(),
                    ["exclusions"] = exclusions.Text.Trim(),
                };
                source.SourceConfig = config;
            }
            await ViewModel.SaveSourceAsync(source);
        }
    }
    private async void DeleteSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AutoscanSource s && await ConfirmAsync("Delete source?", "Its polling/webhook configuration and history association will be removed.", "Delete")) await ViewModel.DeleteSourceAsync(s);
    }
    private async void EditRewrites_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AutoscanSource source)
            await ShowPathRewritesDialogAsync(source);
    }

    private async Task ShowPathRewritesDialogAsync(AutoscanSource source)
    {
        var working = source.PathRewrites.Select(item => new AutoscanPathRewrite { From = item.From, To = item.To }).ToList();
        var rows = new StackPanel { Spacing = 8 };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        void RenderRows()
        {
            rows.Children.Clear();
            if (working.Count == 0)
            {
                rows.Children.Add(new TextBlock { Text = "No path rewrites. Map remote paths (from the scan source) to local library paths.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
                return;
            }
            for (var index = 0; index < working.Count; index++)
            {
                var rewrite = working[index];
                var grid = new Grid { ColumnSpacing = 8 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var from = new TextBox { Header = "From", PlaceholderText = "/remote/media", Text = rewrite.From };
                var to = new TextBox { Header = "To", PlaceholderText = "/media", Text = rewrite.To };
                from.TextChanged += (_, _) => rewrite.From = from.Text;
                to.TextChanged += (_, _) => rewrite.To = to.Text;
                Grid.SetColumn(to, 1);
                var remove = new Button { Content = "×", VerticalAlignment = VerticalAlignment.Bottom, Width = 36, Height = 34, Padding = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                remove.Click += (_, _) => { working.Remove(rewrite); RenderRows(); };
                ToolTipService.SetToolTip(remove, $"Remove rewrite {index + 1}");
                Grid.SetColumn(remove, 2);
                grid.Children.Add(from); grid.Children.Add(to); grid.Children.Add(remove);
                rows.Children.Add(grid);
            }
        }

        var add = new Button { Content = "Add rewrite" };
        add.Click += (_, _) => { working.Add(new AutoscanPathRewrite()); RenderRows(); };
        var sync = new Button { Content = "Sync from server", IsEnabled = !string.IsNullOrWhiteSpace(source.ConnectionId) };
        ToolTipService.SetToolTip(sync, sync.IsEnabled ? "Fetch root-folder mappings from the connected server" : "Bind a connection first");
        var suggestions = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        sync.Click += async (_, _) =>
        {
            sync.IsEnabled = false;
            sync.Content = "Syncing…";
            suggestions.Children.Clear();
            try
            {
                var preview = await ViewModel.GetRewriteSuggestionsAsync(source);
                suggestions.Visibility = Visibility.Visible;
                suggestions.Children.Add(new TextBlock { Text = "Proposed", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                var selected = new List<(CheckBox Check, AutoscanProposedRewrite Proposal)>();
                if (preview.Proposed.Count == 0)
                    suggestions.Children.Add(new TextBlock { Text = "No proposed rewrites.", FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
                foreach (var proposal in preview.Proposed)
                {
                    var check = new CheckBox { IsChecked = true, Content = $"{proposal.From} → {proposal.To}  ·  {(proposal.MatchDepth >= 2 ? $"{proposal.MatchDepth} segments" : "1 segment — weak")}" };
                    selected.Add((check, proposal));
                    suggestions.Children.Add(check);
                }
                AddSuggestionSummary(suggestions, "No Silo match", preview.Unmatched);
                AddSuggestionSummary(suggestions, "Ambiguous", preview.Ambiguous.Select(item => $"{item.Root} → {string.Join(", ", item.Candidates)}"));
                AddSuggestionSummary(suggestions, "Already mapped", preview.Covered);
                if (selected.Count > 0)
                {
                    var apply = new Button { Content = "Apply selected", HorizontalAlignment = HorizontalAlignment.Left };
                    apply.Click += (_, _) =>
                    {
                        foreach (var selection in selected.Where(item => item.Check.IsChecked == true))
                        {
                            var proposal = selection.Proposal;
                            var existing = working.FirstOrDefault(item => item.From.Equals(proposal.From, StringComparison.OrdinalIgnoreCase));
                            if (existing is null) working.Add(new AutoscanPathRewrite { From = proposal.From, To = proposal.To });
                            else existing.To = proposal.To;
                        }
                        RenderRows();
                        suggestions.Visibility = Visibility.Collapsed;
                    };
                    suggestions.Children.Add(apply);
                }
            }
            catch (Exception ex)
            {
                suggestions.Visibility = Visibility.Visible;
                suggestions.Children.Add(new TextBlock { Text = ex.Message, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap });
            }
            finally
            {
                sync.Content = "Sync from server";
                sync.IsEnabled = !string.IsNullOrWhiteSpace(source.ConnectionId);
            }
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(add); actions.Children.Add(sync);
        var panel = new StackPanel { Spacing = 12, MinWidth = 620 };
        panel.Children.Add(new TextBlock { Text = "Map remote paths reported by the scan source to local Silo library paths.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
        panel.Children.Add(rows); panel.Children.Add(error); panel.Children.Add(actions); panel.Children.Add(suggestions);
        RenderRows();
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Path rewrites", Content = panel, PrimaryButtonText = "Save rewrites", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var invalid = working.Any(item => string.IsNullOrWhiteSpace(item.From) || string.IsNullOrWhiteSpace(item.To));
            var duplicate = working.Where(item => !string.IsNullOrWhiteSpace(item.From)).GroupBy(item => item.From.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);
            if (invalid || duplicate)
            {
                args.Cancel = true;
                error.Text = invalid ? "Both From and To are required for every rewrite." : "Each From path can only be mapped once.";
                error.Visibility = Visibility.Visible;
                return;
            }
            var deferral = args.GetDeferral();
            try
            {
                source.PathRewrites = working.Select(item => new AutoscanPathRewrite { From = item.From.Trim(), To = item.To.Trim() }).ToList();
                await ViewModel.SaveSourceAsync(source);
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
    }

    private static void AddSuggestionSummary(StackPanel panel, string title, IEnumerable<string> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return;
        panel.Children.Add(new TextBlock { Text = $"{title} ({list.Count})", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) });
        panel.Children.Add(new TextBlock { Text = string.Join(Environment.NewLine, list), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
    }

    private async void EditRewritesLegacy_Click(object sender, RoutedEventArgs e)
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
        var urlBox = new TextBox { Header = "Webhook URL", Text = url, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        var copy = new Button { Content = "Copy webhook URL", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = source.WebhookConfigured && !string.IsNullOrWhiteSpace(source.WebhookUrl) };
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(source.WebhookUrl ?? "");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            App.Services.GetRequiredService<ToastService>().Success("Webhook URL copied");
        };
        var setupHelp = new TextBlock { Text = "Paste into Sonarr/Radarr → Settings → Connect → Webhook (On Import, On Rename, On File Delete).", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] };
        var providerValue = source.SourceConfig.TryGetValue("webhook_provider", out var currentProvider) && !string.IsNullOrWhiteSpace(currentProvider) ? currentProvider : "auto";
        var provider = new ComboBox { Header = "Provider", ItemsSource = new[] { "Auto", "Sonarr", "Radarr" }, SelectedItem = char.ToUpperInvariant(providerValue[0]) + providerValue[1..], Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        var providerHelp = new TextBlock { Text = "Auto infers Sonarr vs Radarr from each payload.", FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(urlBox);
        if (source.WebhookConfigured) content.Children.Add(copy);
        content.Children.Add(setupHelp);
        content.Children.Add(provider);
        content.Children.Add(providerHelp);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Webhook endpoint", Content = content, PrimaryButtonText = source.WebhookConfigured ? "Rotate URL" : "Generate webhook URL", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
        var result = await dialog.ShowAsync();
        var nextProvider = ((string?)provider.SelectedItem ?? "Auto").ToLowerInvariant();
        var savedProvider = source.SourceConfig.TryGetValue("webhook_provider", out currentProvider) ? currentProvider : "auto";
        if (!string.Equals(nextProvider, savedProvider, StringComparison.OrdinalIgnoreCase))
        {
            source.SourceConfig = new Dictionary<string, string>(source.SourceConfig) { ["webhook_provider"] = nextProvider };
            await ViewModel.SaveSourceAsync(source);
        }
        if (result == ContentDialogResult.Primary)
        {
            if (source.WebhookConfigured && await ConfirmAsync("Rotate webhook URL?", "The current URL will stop working immediately.", "Rotate")) await ViewModel.RotateWebhookAsync(source);
            else if (!source.WebhookConfigured) await ViewModel.CreateWebhookAsync(source);
        }
    }
    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        var d = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task MessageAsync(string title, string content) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "OK" }.ShowAsync();
}
