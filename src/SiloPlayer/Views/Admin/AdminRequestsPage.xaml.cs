using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
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

    public AdminRequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<AdminRequestsViewModel>();
        _requestsApi = App.Services.GetRequiredService<RequestsApi>();
        _adminApi = App.Services.GetRequiredService<AdminApi>();
        _pluginsApi = App.Services.GetRequiredService<PluginsApi>();
        _toasts = App.Services.GetRequiredService<ToastService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _ready = true;
        SetActiveTab("queue");
        await LoadQueueAsync();
    }

    private void SetActiveTab(string tab)
    {
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

    private async Task LoadQueueAsync()
    {
        ShowQueueSkeletons();
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
        var metaParts = new List<string>();
        if (request.Year is > 0) metaParts.Add(request.Year.Value.ToString());
        metaParts.Add($"TMDB {request.TmdbId}");
        if (request.RequestedByUserId.HasValue)
            metaParts.Add(_users.FirstOrDefault(user => user.Id == request.RequestedByUserId.Value)?.Username ?? $"User {request.RequestedByUserId}");
        if (!string.IsNullOrWhiteSpace(request.LibraryContentId)) metaParts.Add(" Library");
        title.Children.Add(new TextBlock { Text = string.Join("   ", metaParts), FontSize = 11, Foreground = Brush("TertiaryTextBrush") });
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
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var approve = ActionButton("", "Approve", request.Status == "pending" && request.Outcome == "active");
        var decline = ActionButton("", "Decline", request.Status != "completed" && request.Outcome == "active");
        var retry = ActionButton("", "Retry", request.Outcome == "failed");
        approve.Click += async (_, _) => { await ViewModel.ApproveCommand.ExecuteAsync(request); RenderQueue(); };
        decline.Click += async (_, _) => await DeclineAsync(request);
        retry.Click += async (_, _) => { await ViewModel.RetryCommand.ExecuteAsync(request); RenderQueue(); };
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
            RenderIntegrations();
        }
        catch (Exception ex) { ShowError($"Request integrations could not be loaded: {ex.Message}"); }
    }

    private void RenderIntegrations()
    {
        IntegrationRows.Children.Clear();
        if (_integrations.Count == 0) { IntegrationRows.Children.Add(Cell("No request integrations configured.", 13)); return; }
        foreach (var integration in _integrations)
        {
            var card = new Grid { ColumnSpacing = 12, Padding = new Thickness(16), Background = Brush("CardBackgroundBrush") };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Spacing = 5 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            heading.Children.Add(new TextBlock { Text = integration.Name, FontSize = 15, FontWeight = FontWeights.SemiBold }); heading.Children.Add(MakeBadge(integration.Enabled ? "Enabled" : "Disabled", integration.Enabled ? BadgeKind.Primary : BadgeKind.Secondary));
            info.Children.Add(heading); info.Children.Add(Cell(integration.BaseUrl, 12));
            info.Children.Add(Cell($"Router: {integration.InstallationId?.ToString() ?? "Not selected"}  ·  {integration.CapabilityId ?? "No capability"}", 11));
            if (!string.IsNullOrWhiteSpace(integration.LastCheckStatus)) info.Children.Add(Cell($"Last check: {integration.LastCheckStatus}", 11));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var edit = ActionButton("", "Edit", true); edit.Click += async (_, _) => await EditIntegrationAsync(integration);
            var delete = ActionButton("", "Delete", true); delete.Foreground = Brush("ErrorBrush"); delete.Click += async (_, _) => await DeleteIntegrationAsync(integration);
            actions.Children.Add(edit); actions.Children.Add(delete); Grid.SetColumn(actions, 1); card.Children.Add(info); card.Children.Add(actions);
            IntegrationRows.Children.Add(new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = card });
        }
    }

    private async void AddIntegration_Click(object sender, RoutedEventArgs e) => await EditIntegrationAsync(null);

    private async Task EditIntegrationAsync(RequestIntegration? existing)
    {
        var name = new TextBox { Header = "Name", Text = existing?.Name ?? "", Style = (Style)Resources["RequestFieldStyle"] };
        var url = new TextBox { Header = "Base URL", Text = existing?.BaseUrl ?? "", Style = (Style)Resources["RequestFieldStyle"] };
        var key = new PasswordBox { Header = existing?.HasApiKey == true ? "API key (leave blank to keep existing)" : "API key", PasswordRevealMode = PasswordRevealMode.Peek };
        var enabled = new ToggleSwitch { Header = "Enabled", IsOn = existing?.Enabled ?? true, OnContent = "", OffContent = "" };
        var router = new ComboBox { Header = "Request router plugin", MinWidth = 420 };
        foreach (var installation in _pluginInstallations)
            foreach (var capability in installation.Capabilities.Where(c => c.Type == "request_router.v1" || c.Id == "request_router.v1"))
                router.Items.Add(new ComboBoxItem { Content = $"{(string.IsNullOrWhiteSpace(capability.DisplayName) ? installation.PluginId : capability.DisplayName)} ({capability.Id})", Tag = new RouterChoice(installation.Id, capability.Id) });
        if (existing?.InstallationId is int installationId)
            router.SelectedItem = router.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is RouterChoice choice && choice.InstallationId == installationId && choice.CapabilityId == existing.CapabilityId);
        var config = new TextBox { Header = "Plugin configuration (JSON)", Text = existing?.PluginConfig == null ? "{}" : JsonSerializer.Serialize(existing.PluginConfig, new JsonSerializerOptions { WriteIndented = true }), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 120, FontFamily = new FontFamily("Consolas") };
        var panel = new StackPanel { Width = 520, Spacing = 12, Children = { name, enabled, router, url, key, config } };
        var dialog = new ContentDialog { Title = existing == null ? "Add Integration" : "Edit Integration", Content = panel, PrimaryButtonText = existing == null ? "Add Integration" : "Save Integration", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            if (router.SelectedItem is not ComboBoxItem { Tag: RouterChoice choice }) throw new InvalidOperationException("Select a request router plugin.");
            var pluginConfig = JsonSerializer.Deserialize<Dictionary<string, object?>>(config.Text) ?? new();
            var payload = existing ?? new RequestIntegration(); payload.Name = name.Text.Trim(); payload.Enabled = enabled.IsOn; payload.BaseUrl = url.Text.Trim(); payload.ApiKeyRef = key.Password.Trim(); payload.InstallationId = choice.InstallationId; payload.CapabilityId = choice.CapabilityId; payload.PluginConfig = pluginConfig;
            if (existing == null) await _requestsApi.CreateRequestIntegrationAsync(payload); else await _requestsApi.UpdateRequestIntegrationAsync(existing.Id, payload);
            await LoadIntegrationsAsync(); _toasts.Success(existing == null ? "Integration added" : "Integration saved");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task DeleteIntegrationAsync(RequestIntegration integration)
    {
        var dialog = new ContentDialog { Title = "Delete integration", Content = $"Delete \"{integration.Name}\"?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await _requestsApi.DeleteRequestIntegrationAsync(integration.Id); await LoadIntegrationsAsync(); _toasts.Success("Integration deleted"); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

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
    private sealed record RouterChoice(int InstallationId, string CapabilityId);
}
