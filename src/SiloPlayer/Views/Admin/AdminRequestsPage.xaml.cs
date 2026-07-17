using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels.Admin;
using Windows.UI;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRequestsPage : Page
{
    public AdminRequestsViewModel ViewModel { get; }
    private readonly RequestsApi _requestsApi;
    private readonly AdminApi _adminApi;
    private readonly PluginsApi _pluginsApi;
    private readonly ToastService _toasts;
    private bool _ready;
    private RequestSettings? _settings;
    private List<RequestIntegration> _integrations = [];
    private List<PluginInstallation> _pluginInstallations = [];
    private List<AdminUser> _users = [];
    private RequestUserLimit? _selectedLimit;
    private bool _loaded;
    private string _activeTab = "queue";

    public AdminRequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminRequestsViewModel>();
        _requestsApi = App.Services.GetRequiredService<RequestsApi>();
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _pluginsApi = App.Services.GetRequiredService<PluginsApi>();
        _toasts = App.Services.GetRequiredService<ToastService>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        SizeChanged += AdminRequestsPage_SizeChanged;
    }

    private void AdminRequestsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 760;
        var gutter = e.NewSize.Width < 600 ? 16 : compact ? 24 : 40;
        RequestsPageShell.Padding = new Thickness(gutter, compact ? 24 : 32, gutter, 40);
        QueueFilterBar.Orientation = e.NewSize.Width < 560 ? Orientation.Vertical : Orientation.Horizontal;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        _ready = true;
        SetActiveTab(_activeTab);
        switch (_activeTab)
        {
            case "settings": await LoadSettingsAsync(); break;
            case "integrations": await LoadIntegrationsAsync(); break;
            case "overrides": await LoadUsersAsync(); break;
            default:
                if (ViewModel.Requests.Count > 0) RenderQueue();
                await LoadQueueAsync(ViewModel.Requests.Count == 0);
                break;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loaded = false;
        _ready = false;
        base.OnNavigatedFrom(e);
    }

    private void SetActiveTab(string tab)
    {
        _activeTab = tab;
        QueuePanel.Visibility = tab == "queue" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        IntegrationsPanel.Visibility = tab == "integrations" ? Visibility.Visible : Visibility.Collapsed;
        OverridesPanel.Visibility = tab == "overrides" ? Visibility.Visible : Visibility.Collapsed;
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var muted = (Brush)Application.Current.Resources["TertiaryTextBrush"];
        foreach (var (button, key) in new[]
        {
            (QueueTabButton, "queue"), (SettingsTabButton, "settings"),
            (IntegrationsTabButton, "integrations"), (OverridesTabButton, "overrides")
        })
        {
            button.Foreground = key == tab ? accent : muted;
            button.BorderBrush = key == tab ? accent : new SolidColorBrush(Colors.Transparent);
            button.FontWeight = key == tab ? FontWeights.SemiBold : FontWeights.Normal;
        }
        ClearError();
    }

    private async void QueueTab_Click(object sender, RoutedEventArgs e) { SetActiveTab("queue"); await LoadQueueAsync(); }
    private async void SettingsTab_Click(object sender, RoutedEventArgs e) { SetActiveTab("settings"); await LoadSettingsAsync(); }
    private async void IntegrationsTab_Click(object sender, RoutedEventArgs e) { SetActiveTab("integrations"); await LoadIntegrationsAsync(); }
    private async void OverridesTab_Click(object sender, RoutedEventArgs e) { SetActiveTab("overrides"); await LoadUsersAsync(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadQueueAsync();
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) await LoadQueueAsync(); }

    private async Task LoadQueueAsync(bool showSkeleton = true)
    {
        if (showSkeleton) ShowQueueSkeletons();
        ViewModel.StatusFilter = SelectedTag(StatusFilterComboBox, "all");
        ViewModel.OutcomeFilter = SelectedTag(OutcomeFilterComboBox, "all");
        var queueTask = ViewModel.LoadCommand.ExecuteAsync(null);
        var usersTask = _users.Count == 0 ? _adminApi.GetUsersAsync() : null;
        if (usersTask != null) await Task.WhenAll(queueTask, usersTask); else await queueTask;
        if (usersTask != null) _users = await usersTask;
        RenderQueue();
    }

    private void ShowQueueSkeletons()
    {
        QueueSkeleton.Children.Clear();
        QueueSkeleton.Visibility = Visibility.Visible;
        for (var i = 0; i < 5; i++) QueueSkeleton.Children.Add(new Border
        {
            Height = 48, CornerRadius = new CornerRadius(8),
            Background = Brush("SurfaceBrush"), Opacity = i % 2 == 0 ? 0.55 : 0.38,
        });
    }

    private void RenderQueue()
    {
        QueueSkeleton.Visibility = Visibility.Collapsed;
        ShowError(ViewModel.ErrorMessage);
        RequestRows.Children.Clear();
        if (ViewModel.Requests.Count == 0)
        {
            RequestRows.Children.Add(new TextBlock
            {
                Text = "No requests match the current filters.", Padding = new Thickness(20, 28, 20, 28),
                HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("SecondaryTextBrush"),
            });
            return;
        }
        var first = true;
        foreach (var request in ViewModel.Requests)
        {
            if (!first) RequestRows.Children.Add(new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0) });
            first = false;
            RequestRows.Children.Add(BuildRequestRow(request));
        }
    }

    private FrameworkElement BuildRequestRow(MediaRequest request)
    {
        var row = new Grid { Padding = new Thickness(12, 9, 12, 9), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        var title = new StackPanel { Spacing = 4 };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var titleLink = new HyperlinkButton { Content = request.Title, Padding = new Thickness(0), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("PrimaryTextBrush") };
        titleLink.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(request.MediaType, request.TmdbId));
        titleLine.Children.Add(titleLink);
        titleLine.Children.Add(MakeBadge(FormatMediaType(request.MediaType), BadgeKind.Secondary));
        title.Children.Add(titleLine);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (request.Year is > 0) meta.Children.Add(Cell(request.Year.Value.ToString(), 11));
        meta.Children.Add(Cell($"TMDB {request.TmdbId}", 11));
        if (request.RequestedByUserId is int userId)
        {
            var username = _users.FirstOrDefault(user => user.Id == userId)?.Username ?? $"User {userId}";
            var userLink = new HyperlinkButton
            {
                Content = username,
                Padding = new Thickness(0),
                FontSize = 11,
                Foreground = Brush("TertiaryTextBrush"),
            };
            userLink.Click += (_, _) => Frame.Navigate(typeof(AdminUserDetailPage), userId);
            meta.Children.Add(userLink);
        }
        if (!string.IsNullOrWhiteSpace(request.LibraryContentId))
        {
            var contentId = request.LibraryContentId;
            var libraryLink = new HyperlinkButton
            {
                Padding = new Thickness(0),
                Foreground = Brush("TertiaryTextBrush"),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new FontIcon { Glyph = "\uE8F1", FontSize = 10 }, new TextBlock { Text = "Library", FontSize = 11 } },
                },
            };
            libraryLink.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(contentId);
            meta.Children.Add(libraryLink);
        }
        title.Children.Add(meta);
        if (!string.IsNullOrWhiteSpace(request.LastError)) title.Children.Add(new TextBlock { Text = request.LastError, FontSize = 11, Foreground = Brush("ErrorBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 430 });

        var requested = Cell(FormatDate(request.CreatedAt), 11);
        var status = MakeBadge(FormatLabel(request.Status), request.Status == "completed" ? BadgeKind.Primary : BadgeKind.Secondary);
        var outcome = MakeBadge(FormatLabel(request.Outcome), request.Outcome == "failed" ? BadgeKind.Error : BadgeKind.Secondary);
        var targets = BuildTargets(request);
        var actions = BuildActions(request);
        foreach (var (element, column) in new (FrameworkElement, int)[] { (title, 0), (requested, 1), (status, 2), (outcome, 3), (targets, 4), (actions, 5) }) { Grid.SetColumn(element, column); row.Children.Add(element); }
        return row;
    }

    private FrameworkElement BuildTargets(MediaRequest request)
    {
        if (request.Targets == null || request.Targets.Count == 0) return Cell("Not submitted", 11);
        var stack = new StackPanel { Spacing = 5 };
        if (request.IsAnime == true) stack.Children.Add(MakeBadge("Anime", BadgeKind.Secondary));
        foreach (var target in request.Targets)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(MakeBadge(target.Quality == "2160p" ? "2160p" : "1080p", BadgeKind.Outline));
            line.Children.Add(new TextBlock { Text = target.InstanceName ?? target.IntegrationKind ?? "Unknown", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(MakeBadge(FormatLabel(target.Status), target.Status == "failed" ? BadgeKind.Error : BadgeKind.Secondary));
            if (!string.IsNullOrWhiteSpace(target.ExternalStatus)) line.Children.Add(Cell(target.ExternalStatus, 10));
            stack.Children.Add(line);
            if (target.Status == "failed" && !string.IsNullOrWhiteSpace(target.LastError)) stack.Children.Add(new TextBlock { Text = $"⚠ {target.LastError}", FontSize = 10, Foreground = Brush("ErrorBrush"), TextWrapping = TextWrapping.Wrap });
        }
        return stack;
    }

    private FrameworkElement BuildActions(MediaRequest request)
    {
        var actions = new Grid { ColumnSpacing = 6, RowSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var approve = ActionButton("", "Approve", request.Status == "pending" && request.Outcome == "active");
        var decline = ActionButton("", "Decline", request.Status != "completed" && request.Outcome == "active");
        var retry = ActionButton("", "Retry", request.Outcome == "failed");
        approve.Click += async (_, _) => { await ViewModel.ApproveCommand.ExecuteAsync(request); RenderQueue(); };
        decline.Click += async (_, _) => await DeclineAsync(request);
        retry.Click += async (_, _) => { await ViewModel.RetryCommand.ExecuteAsync(request); RenderQueue(); };
        Grid.SetColumn(decline, 1);
        Grid.SetRow(retry, 1);
        actions.Children.Add(approve); actions.Children.Add(decline); actions.Children.Add(retry);
        return actions;
    }

    private async Task DeclineAsync(MediaRequest request)
    {
        var reason = new TextBox { PlaceholderText = "e.g. duplicate of an existing request", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 88 };
        var dialog = new ContentDialog { Title = "Decline request", Content = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = $"\"{request.Title}\" will be marked declined. Add an optional note for the requester.", TextWrapping = TextWrapping.Wrap }, reason } }, PrimaryButtonText = "Decline", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await _requestsApi.DeclineAsync(request.Id, string.IsNullOrWhiteSpace(reason.Text) ? null : reason.Text.Trim()); await LoadQueueAsync(); _toasts.Success("Request declined"); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _requestsApi.GetAdminRequestSettingsAsync();
            RequestsEnabledSwitch.IsOn = _settings.RequestsEnabled; AutoApprovalSwitch.IsOn = _settings.GlobalAutoApprovalEnabled;
            MaxRequestsBox.Value = _settings.GlobalMaxRequests; WindowDaysBox.Value = _settings.GlobalWindowDays; ForceDualQualitySwitch.IsOn = _settings.ForceDualQuality;
        }
        catch (Exception ex) { ShowError($"Request settings could not be loaded: {ex.Message}"); }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.RequestsEnabled = RequestsEnabledSwitch.IsOn; _settings.GlobalAutoApprovalEnabled = AutoApprovalSwitch.IsOn;
        _settings.GlobalMaxRequests = Math.Max(0, (int)(double.IsNaN(MaxRequestsBox.Value) ? 0 : MaxRequestsBox.Value));
        _settings.GlobalWindowDays = Math.Max(1, (int)(double.IsNaN(WindowDaysBox.Value) ? 1 : WindowDaysBox.Value));
        _settings.ForceDualQuality = ForceDualQualitySwitch.IsOn;
        try { _settings = await _requestsApi.UpdateAdminRequestSettingsAsync(_settings); _toasts.Success("Request settings saved"); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task LoadIntegrationsAsync()
    {
        try
        {
            var integrationsTask = _requestsApi.GetRequestIntegrationsAsync();
            var pluginsTask = _pluginsApi.GetInstallationsAsync();
            await Task.WhenAll(integrationsTask, pluginsTask);
            _integrations = (await integrationsTask).Integrations; _pluginInstallations = await pluginsTask;
            RenderIntegrationEditors();
        }
        catch (Exception ex) { ShowError($"Request integrations could not be loaded: {ex.Message}"); }
    }

    private void AddIntegration_Click(object sender, RoutedEventArgs e) => AddIntegrationEditor();

    private async Task DeleteIntegrationAsync(RequestIntegration integration)
    {
        var dialog = new ContentDialog { Title = "Delete connection", Content = $"\"{integration.Name}\" will be permanently removed. New requests will no longer route to this connection.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await _requestsApi.DeleteRequestIntegrationAsync(integration.Id); await LoadIntegrationsAsync(); _toasts.Success("Integration deleted"); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // WebUI-parity inline connection editor. Request integrations are plugin-
    // schema driven; a raw JSON dialog cannot represent dynamic root-folder,
    // quality-profile, tag, conditional, or exclusive controls.
    private void RenderIntegrationEditors()
    {
        IntegrationRows.Children.Clear();
        var routers = GetRequestRouterChoices();
        if (routers.Count == 0)
        {
            IntegrationRows.Children.Add(BuildEmptyPanel(
                "No request-router plugin installed",
                "Install a plugin that exposes the request_router.v1 capability before adding connections."));
            return;
        }
        if (_integrations.Count == 0)
        {
            IntegrationRows.Children.Add(BuildEmptyPanel(
                "No connections",
                "Add a connection and pick a plugin to route requests."));
            return;
        }

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < _integrations.Count; index++)
        {
            if (index % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var card = BuildIntegrationEditor(_integrations[index], routers);
            Grid.SetColumn(card, index % 2);
            Grid.SetRow(card, index / 2);
            grid.Children.Add(card);
        }
        IntegrationRows.Children.Add(grid);
    }

    private void AddIntegrationEditor()
    {
        var routers = GetRequestRouterChoices();
        if (routers.Count == 0) return;
        var first = routers[0];
        _integrations.Add(new RequestIntegration
        {
            Enabled = true,
            InstallationId = first.Installation.Id,
            CapabilityId = first.Capability.Id,
            PluginConfig = [],
        });
        RenderIntegrationEditors();
    }

    private List<RequestRouterChoice> GetRequestRouterChoices()
    {
        var result = new List<RequestRouterChoice>();
        foreach (var installation in _pluginInstallations)
            foreach (var capability in installation.Capabilities.Where(capability =>
                         capability.Type == "request_router.v1" || capability.Id == "request_router.v1"))
                result.Add(new RequestRouterChoice(installation, capability));
        return result;
    }

    private FrameworkElement BuildIntegrationEditor(RequestIntegration integration, List<RequestRouterChoice> routers)
    {
        integration.PluginConfig ??= [];
        var selected = routers.FirstOrDefault(choice =>
            choice.Installation.Id == integration.InstallationId &&
            choice.Capability.Id == integration.CapabilityId)
            ?? routers.FirstOrDefault(choice => choice.Installation.Id == integration.InstallationId)
            ?? (routers.Count == 1 ? routers[0] : null);
        if (selected is not null && integration.InstallationId is null)
        {
            integration.InstallationId = selected.Installation.Id;
            integration.CapabilityId = selected.Capability.Id;
        }

        var root = new StackPanel { Spacing = 16 };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(new FontIcon { Glyph = "\uEBC5", FontSize = 15, Foreground = Brush("AccentBrush") });
        titleRow.Children.Add(new TextBlock
        {
            Text = selected?.Capability.DisplayName ?? selected?.Installation.PluginId ?? "Connection",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (integration.HasApiKey == true) titleRow.Children.Add(MakeBadge("Key saved", BadgeKind.Secondary));
        if (string.IsNullOrEmpty(integration.Id)) titleRow.Children.Add(MakeBadge("New", BadgeKind.Outline));
        header.Children.Add(titleRow);

        var enabledRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var enabledLabel = Cell(integration.Enabled ? "Enabled" : "Disabled", 12);
        var enabled = new ToggleSwitch { IsOn = integration.Enabled, OnContent = "", OffContent = "" };
        enabled.Toggled += (_, _) =>
        {
            integration.Enabled = enabled.IsOn;
            enabledLabel.Text = enabled.IsOn ? "Enabled" : "Disabled";
        };
        enabledRow.Children.Add(enabledLabel);
        enabledRow.Children.Add(enabled);
        Grid.SetColumn(enabledRow, 1);
        header.Children.Add(enabledRow);
        root.Children.Add(header);

        var name = new TextBox { Text = integration.Name, PlaceholderText = "Connection name", Style = (Style)Resources["RequestFieldStyle"] };
        name.TextChanged += (_, _) => integration.Name = name.Text;
        var key = new PasswordBox
        {
            PlaceholderText = integration.HasApiKey == true ? "Leave blank to keep saved key" : "API key",
            PasswordRevealMode = PasswordRevealMode.Peek,
        };
        key.PasswordChanged += (_, _) => integration.ApiKeyRef = key.Password;
        var nameKeyGrid = new Grid { ColumnSpacing = 16 };
        nameKeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameKeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nameField = BuildField("Name", name);
        var keyField = BuildField("API key or setting key", key);
        Grid.SetColumn(keyField, 1);
        nameKeyGrid.Children.Add(nameField);
        nameKeyGrid.Children.Add(keyField);
        root.Children.Add(nameKeyGrid);

        var url = new TextBox { Text = integration.BaseUrl, PlaceholderText = "http://localhost:7878", Style = (Style)Resources["RequestFieldStyle"] };
        url.TextChanged += (_, _) => integration.BaseUrl = url.Text;
        root.Children.Add(BuildField("Base URL", url));

        var pluginPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in routers)
        {
            var label = $"{(string.IsNullOrWhiteSpace(choice.Capability.DisplayName) ? choice.Installation.PluginId : choice.Capability.DisplayName)} ({choice.Installation.PluginId})";
            pluginPicker.Items.Add(new ComboBoxItem { Content = label, Tag = choice });
        }
        pluginPicker.SelectedItem = pluginPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            item.Tag is RequestRouterChoice choice && choice.Installation.Id == selected?.Installation.Id && choice.Capability.Id == selected?.Capability.Id);
        var suppressPluginChange = true;
        pluginPicker.SelectionChanged += (_, _) =>
        {
            if (suppressPluginChange || pluginPicker.SelectedItem is not ComboBoxItem { Tag: RequestRouterChoice choice }) return;
            integration.InstallationId = choice.Installation.Id;
            integration.CapabilityId = choice.Capability.Id;
            integration.PluginConfig = [];
            RenderIntegrationEditors();
        };
        suppressPluginChange = false;
        root.Children.Add(BuildField("Plugin", pluginPicker));

        var schema = selected?.Capability.ConfigSchema?.FirstOrDefault();
        var descriptor = schema?.AdminForm;
        if (descriptor is not null)
        {
            var schemaHost = new ContentControl();
            var sectionOpen = new Dictionary<string, bool>();
            Dictionary<string, List<RequestSchemaOption>> options = [];
            string? optionsError = null;

            void RefreshSchema() => schemaHost.Content = BuildIntegrationSchema(
                integration, descriptor, options, optionsError, sectionOpen, RefreshSchema);

            RefreshSchema();
            root.Children.Add(schemaHost);
            _ = LoadIntegrationOptionsAsync(integration, options, message =>
            {
                optionsError = message;
                RefreshSchema();
            });
        }
        else if (selected is not null)
        {
            root.Children.Add(Cell("This plugin does not expose a connection configuration form.", 13));
        }
        else
        {
            root.Children.Add(Cell("Select a plugin to configure this connection.", 13));
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var isNew = string.IsNullOrEmpty(integration.Id);
        var save = new Button { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new FontIcon { Glyph = "\uE74E", FontSize = 14 }, new TextBlock { Text = isNew ? "Create connection" : "Save" } },
        };
        save.Click += async (_, _) => await SaveIntegrationAsync(integration, save, isNew);
        actions.Children.Add(save);
        var remove = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            Foreground = isNew ? Brush("SecondaryTextBrush") : Brush("ErrorBrush"),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        remove.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new FontIcon { Glyph = isNew ? "\uE711" : "\uE74D", FontSize = 14 }, new TextBlock { Text = isNew ? "Discard" : "Delete" } },
        };
        remove.Click += async (_, _) =>
        {
            if (isNew)
            {
                _integrations.Remove(integration);
                RenderIntegrationEditors();
            }
            else
            {
                await DeleteIntegrationAsync(integration);
            }
        };
        actions.Children.Add(remove);
        root.Children.Add(actions);

        return new Border
        {
            Background = Brush("CardBackgroundBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Child = root,
        };
    }

    private async Task LoadIntegrationOptionsAsync(
        RequestIntegration integration,
        Dictionary<string, List<RequestSchemaOption>> options,
        Action<string?> completed)
    {
        if (string.IsNullOrWhiteSpace(integration.BaseUrl) || integration.InstallationId is null ||
            string.IsNullOrWhiteSpace(integration.CapabilityId) ||
            (integration.HasApiKey != true && string.IsNullOrWhiteSpace(integration.ApiKeyRef)))
            return;
        try
        {
            var loaded = await _requestsApi.LoadRequestIntegrationOptionsAsync(
                string.IsNullOrEmpty(integration.Id) ? "new" : integration.Id,
                new LoadRequestIntegrationOptionsRequest
                {
                    BaseUrl = integration.BaseUrl,
                    ApiKeyRef = string.IsNullOrWhiteSpace(integration.ApiKeyRef) ? null : integration.ApiKeyRef,
                    CapabilityId = integration.CapabilityId,
                    InstallationId = integration.InstallationId,
                    PluginConfig = integration.PluginConfig,
                });
            options.Clear();
            foreach (var (key, values) in loaded) options[key] = values;
            completed(null);
        }
        catch
        {
            options.Clear();
            completed("Couldn't load options from the service — check the base URL and API key, then edit a field to retry.");
        }
    }

    private FrameworkElement BuildIntegrationSchema(
        RequestIntegration integration,
        PluginAdminForm descriptor,
        Dictionary<string, List<RequestSchemaOption>> dynamicOptions,
        string? optionsError,
        Dictionary<string, bool> sectionOpen,
        Action refresh)
    {
        integration.PluginConfig ??= [];
        var root = new StackPanel { Spacing = 16 };
        var fieldsByKey = descriptor.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var sectionKeys = new HashSet<string>(descriptor.Sections?.SelectMany(section => section.FieldKeys) ?? []);

        foreach (var field in descriptor.Fields.Where(field => !sectionKeys.Contains(field.Key)))
            if (ConditionsMatch(field.ShowWhen, integration.PluginConfig))
                root.Children.Add(BuildIntegrationSchemaField(integration, field, dynamicOptions, refresh));

        foreach (var section in descriptor.Sections ?? [])
        {
            if (!ConditionsMatch(section.ShowWhen, integration.PluginConfig)) continue;
            if (!sectionOpen.ContainsKey(section.Key)) sectionOpen[section.Key] = !section.CollapsedDefault;
            var sectionRoot = new StackPanel { Spacing = 12 };
            var sectionHeader = new Grid { ColumnSpacing = 8 };
            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel { Spacing = 2 };
            labels.Children.Add(new TextBlock { Text = section.Title, FontSize = 13, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(section.Description)) labels.Children.Add(Cell(section.Description, 11));
            sectionHeader.Children.Add(labels);
            if (section.Collapsible)
            {
                var toggle = new Button
                {
                    Content = sectionOpen[section.Key] ? "Hide" : "Show",
                    Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
                    BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    Padding = new Thickness(8, 3, 8, 3),
                    FontSize = 11,
                };
                toggle.Click += (_, _) => { sectionOpen[section.Key] = !sectionOpen[section.Key]; refresh(); };
                Grid.SetColumn(toggle, 1);
                sectionHeader.Children.Add(toggle);
            }
            sectionRoot.Children.Add(sectionHeader);
            if (!section.Collapsible || sectionOpen[section.Key])
            {
                foreach (var key in section.FieldKeys)
                    if (fieldsByKey.TryGetValue(key, out var field) && ConditionsMatch(field.ShowWhen, integration.PluginConfig))
                        sectionRoot.Children.Add(BuildIntegrationSchemaField(integration, field, dynamicOptions, refresh));
            }
            root.Children.Add(new Border
            {
                Background = Brush("SurfaceBrush"),
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Child = sectionRoot,
            });
        }

        if (!string.IsNullOrWhiteSpace(optionsError))
        {
            root.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(24, 239, 68, 68)),
                BorderBrush = Brush("ErrorBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10),
                Child = new TextBlock { Text = $"⚠ {optionsError}", FontSize = 11, Foreground = Brush("ErrorBrush"), TextWrapping = TextWrapping.Wrap },
            });
        }
        return root;
    }

    private FrameworkElement BuildIntegrationSchemaField(
        RequestIntegration integration,
        PluginAdminFormField field,
        Dictionary<string, List<RequestSchemaOption>> dynamicOptions,
        Action refresh)
    {
        integration.PluginConfig ??= [];
        var value = ConfigValue(integration.PluginConfig, field.Key) ?? field.DefaultValue;
        if (field.Control.Equals("SWITCH", StringComparison.OrdinalIgnoreCase))
        {
            var toggle = new ToggleSwitch { IsOn = AsBool(value), OnContent = "", OffContent = "", Width = 44, MinWidth = 44, VerticalAlignment = VerticalAlignment.Top };
            toggle.Toggled += (_, _) =>
            {
                integration.PluginConfig[field.Key] = toggle.IsOn;
                if (toggle.IsOn && !string.IsNullOrWhiteSpace(field.ExclusiveGroupField))
                    ApplyIntegrationExclusivity(integration, field);
                refresh();
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(toggle);
            var label = new StackPanel { Spacing = 2 };
            label.Children.Add(new TextBlock { Text = field.Label, FontSize = 13, FontWeight = FontWeights.Medium });
            if (!string.IsNullOrWhiteSpace(field.Description)) label.Children.Add(new TextBlock { Text = field.Description, FontSize = 11, Foreground = Brush("TertiaryTextBrush"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(label);
            return new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Child = row };
        }

        FrameworkElement editor;
        if (field.Control.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var options = field.DynamicOptions && dynamicOptions.TryGetValue(field.Key, out var loaded)
                ? loaded.Select(option => new PluginAdminFormFieldOption { Label = option.Label, Value = option.Value }).ToList()
                : field.Options ?? [];
            foreach (var option in options) combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            var current = ConfigString(value);
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, current));
            if (combo.SelectedItem is null && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
                if (combo.SelectedItem is ComboBoxItem { Tag: string first }) integration.PluginConfig[field.Key] = first;
            }
            var suppress = true;
            combo.SelectionChanged += (_, _) =>
            {
                if (suppress || combo.SelectedItem is not ComboBoxItem { Tag: string selected }) return;
                integration.PluginConfig[field.Key] = selected;
                refresh();
            };
            suppress = false;
            editor = combo;
        }
        else if (field.Control.Equals("MULTI_SELECT", StringComparison.OrdinalIgnoreCase))
        {
            var options = field.DynamicOptions && dynamicOptions.TryGetValue(field.Key, out var loaded)
                ? loaded.Select(option => new PluginAdminFormFieldOption { Label = option.Label, Value = option.Value }).ToList()
                : field.Options ?? [];
            var selected = ConfigStringList(value);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var option in options)
            {
                var button = new Button
                {
                    Content = option.Label,
                    Style = (Style)Application.Current.Resources[selected.Contains(option.Value) ? "AccentButtonStyle" : "SecondaryButtonStyle"],
                    Padding = new Thickness(8, 3, 8, 3),
                    FontSize = 11,
                };
                button.Click += (_, _) =>
                {
                    var next = ConfigStringList(ConfigValue(integration.PluginConfig, field.Key));
                    if (!next.Remove(option.Value)) next.Add(option.Value);
                    integration.PluginConfig[field.Key] = next;
                    refresh();
                };
                chips.Children.Add(button);
            }
            editor = chips;
        }
        else if (field.Control.Equals("NUMBER", StringComparison.OrdinalIgnoreCase))
        {
            var number = new NumberBox { Value = double.TryParse(ConfigString(value), out var parsed) ? parsed : double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            if (field.Validation?.HasMin == true && field.Validation.Min.HasValue) number.Minimum = field.Validation.Min.Value;
            if (field.Validation?.HasMax == true && field.Validation.Max.HasValue) number.Maximum = field.Validation.Max.Value;
            number.ValueChanged += (_, _) => integration.PluginConfig[field.Key] = number.Value;
            editor = number;
        }
        else if (field.Secret || field.Control.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase))
        {
            var password = new PasswordBox { PlaceholderText = field.Placeholder ?? "", PasswordRevealMode = PasswordRevealMode.Peek };
            password.PasswordChanged += (_, _) => integration.PluginConfig[field.Key] = password.Password;
            editor = password;
        }
        else
        {
            var multiline = field.Multiline || field.Control.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase);
            var text = new TextBox
            {
                Text = ConfigString(value),
                PlaceholderText = field.Placeholder ?? "",
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multiline ? Math.Max(96, (field.Rows ?? 4) * 22) : 0,
                Style = (Style)Resources["RequestFieldStyle"],
            };
            text.TextChanged += (_, _) => integration.PluginConfig[field.Key] = text.Text;
            editor = text;
        }
        return BuildField(field.Label, editor, field.Description);
    }

    private async Task SaveIntegrationAsync(RequestIntegration integration, Button saveButton, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(integration.Name) || string.IsNullOrWhiteSpace(integration.BaseUrl) ||
            integration.InstallationId is null || string.IsNullOrWhiteSpace(integration.CapabilityId) ||
            (integration.HasApiKey != true && string.IsNullOrWhiteSpace(integration.ApiKeyRef)))
        {
            ShowError("Name, Base URL, API key, and Plugin are required.");
            return;
        }
        saveButton.IsEnabled = false;
        try
        {
            var serviceKind = ConfigString(ConfigValue(integration.PluginConfig, "service_kind")).Trim().ToLowerInvariant();
            integration.SupportedMediaTypes = serviceKind switch
            {
                "radarr" => ["movie"],
                "sonarr" => ["series"],
                _ => integration.SupportedMediaTypes ?? [],
            };
            if (isNew) await _requestsApi.CreateRequestIntegrationAsync(integration);
            else await _requestsApi.UpdateRequestIntegrationAsync(integration.Id, integration);
            await LoadIntegrationsAsync();
            _toasts.Success(isNew ? "Connection created" : "Connection saved");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { saveButton.IsEnabled = true; }
    }

    private void ApplyIntegrationExclusivity(RequestIntegration changed, PluginAdminFormField field)
    {
        if (string.IsNullOrWhiteSpace(field.ExclusiveGroupField) || changed.PluginConfig is null) return;
        var groupValue = ConfigString(ConfigValue(changed.PluginConfig, field.ExclusiveGroupField));
        foreach (var sibling in _integrations.Where(item => !ReferenceEquals(item, changed) && item.InstallationId == changed.InstallationId))
        {
            if (sibling.PluginConfig is null ||
                ConfigString(ConfigValue(sibling.PluginConfig, field.ExclusiveGroupField)) != groupValue ||
                !AsBool(ConfigValue(sibling.PluginConfig, field.Key))) continue;
            sibling.PluginConfig[field.Key] = false;
        }
    }

    private static bool ConditionsMatch(List<PluginAdminFormCondition>? conditions, Dictionary<string, object?> values)
    {
        if (conditions is null || conditions.Count == 0) return true;
        return conditions.All(condition => condition.Equals.Any(expected =>
            string.Equals(ConfigString(ConfigValue(values, condition.Field)), expected, StringComparison.OrdinalIgnoreCase)));
    }

    private static object? ConfigValue(Dictionary<string, object?>? values, string key)
    {
        if (values is null || !values.TryGetValue(key, out var value)) return null;
        return value is JsonElement element ? JsonElementValue(element) : value;
    }

    private static object? JsonElementValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.Array => element.EnumerateArray().Select(item => item.ToString()).ToList(),
        JsonValueKind.Null => null,
        _ => element.ToString(),
    };

    private static string ConfigString(object? value) => value switch
    {
        null => "",
        bool flag => flag ? "true" : "false",
        _ => value.ToString() ?? "",
    };

    private static bool AsBool(object? value) => value is bool flag ? flag :
        bool.TryParse(ConfigString(value), out var parsed) && parsed;

    private static List<string> ConfigStringList(object? value) => value switch
    {
        IEnumerable<string> strings => strings.ToList(),
        JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray().Select(item => item.ToString()).ToList(),
        _ => [],
    };

    private FrameworkElement BuildField(string label, FrameworkElement editor, string? description = null)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.Medium });
        if (!string.IsNullOrWhiteSpace(description))
            stack.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = Brush("TertiaryTextBrush"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(editor);
        return stack;
    }

    private FrameworkElement BuildEmptyPanel(string title, string detail) => new Border
    {
        BorderBrush = Brush("BorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(24),
        Child = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = detail, FontSize = 12, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center },
            },
        },
    };

    private async Task LoadUsersAsync()
    {
        try
        {
            _users = await _adminApi.GetUsersAsync(); OverrideUserPicker.Items.Clear();
            foreach (var user in _users) OverrideUserPicker.Items.Add(new ComboBoxItem { Content = user.Username, Tag = user.Id });
            if (OverrideUserPicker.Items.Count > 0) OverrideUserPicker.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError($"Users could not be loaded: {ex.Message}"); }
    }

    private async void OverrideUserPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OverrideUserPicker.SelectedItem is not ComboBoxItem { Tag: int userId }) return;
        try
        {
            _selectedLimit = await _requestsApi.GetRequestUserLimitAsync(userId);
            SelectTag(LimitModePicker, _selectedLimit.LimitMode); SelectTag(ApprovalModePicker, _selectedLimit.ApprovalMode);
            OverrideMaxRequestsBox.Value = _selectedLimit.MaxRequests ?? 0; OverrideWindowDaysBox.Value = _selectedLimit.WindowDays ?? 1; UpdateOverrideCustomVisibility();
        }
        catch (Exception ex) { ShowError($"The selected user limit could not be loaded: {ex.Message}"); }
    }

    private void LimitModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOverrideCustomVisibility();
    private void UpdateOverrideCustomVisibility()
    {
        var custom = SelectedTag(LimitModePicker, "inherit") == "custom";
        OverrideMaxRequestsBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed; OverrideWindowDaysBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveOverride_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLimit == null || OverrideUserPicker.SelectedItem is not ComboBoxItem { Tag: int userId }) return;
        _selectedLimit.UserId = userId; _selectedLimit.LimitMode = SelectedTag(LimitModePicker, "inherit"); _selectedLimit.ApprovalMode = SelectedTag(ApprovalModePicker, "inherit");
        if (_selectedLimit.LimitMode == "custom") { _selectedLimit.MaxRequests = Math.Max(0, (int)OverrideMaxRequestsBox.Value); _selectedLimit.WindowDays = Math.Max(1, (int)OverrideWindowDaysBox.Value); } else { _selectedLimit.MaxRequests = null; _selectedLimit.WindowDays = null; }
        try { _selectedLimit = await _requestsApi.UpdateRequestUserLimitAsync(userId, _selectedLimit); _toasts.Success("User override saved"); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private Button ActionButton(string glyph, string text, bool enabled)
    {
        var button = new Button { Height = 30, Padding = new Thickness(9, 0, 9, 0), IsEnabled = enabled, Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new FontIcon { Glyph = glyph, FontSize = 13 }, new TextBlock { Text = text } } };
        return button;
    }

    private enum BadgeKind { Primary, Secondary, Outline, Error }
    private Border MakeBadge(string text, BadgeKind kind)
    {
        var accent = ((SolidColorBrush)Brush("AccentBrush")).Color; var error = ((SolidColorBrush)Brush("ErrorBrush")).Color;
        var color = kind == BadgeKind.Error ? error : kind == BadgeKind.Primary ? accent : ((SolidColorBrush)Brush("SecondaryTextBrush")).Color;
        return new Border { Background = kind == BadgeKind.Outline ? new SolidColorBrush(Colors.Transparent) : new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(62, color.R, color.G, color.B)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) } };
    }

    private TextBlock Cell(string text, double size) => new() { Text = text, FontSize = size, Foreground = Brush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private static string SelectedTag(ComboBox box, string fallback) => box.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : fallback;
    private static void SelectTag(ComboBox box, string value) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, value));
    private static string FormatMediaType(string value) => value.Equals("series", StringComparison.OrdinalIgnoreCase) ? "Series" : "Movie";
    private static string FormatLabel(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value[1..].Replace('_', ' ');
    private static string FormatDate(string value) => DateTime.TryParse(value, out var date) ? date.ToLocalTime().ToString("MMM d, yyyy") : value;
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private void ClearError() { ErrorText.Text = ""; ErrorText.Visibility = Visibility.Collapsed; }
    private void ShowError(string? error) { ErrorText.Text = error ?? ""; ErrorText.Visibility = string.IsNullOrWhiteSpace(error) ? Visibility.Collapsed : Visibility.Visible; }
    private sealed record RequestRouterChoice(PluginInstallation Installation, PluginCapability Capability);
}
