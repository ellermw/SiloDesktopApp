using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminAutoscanViewModel(AdminApi adminApi, RequestsApi requestsApi) : ObservableObject
{
    private CancellationTokenSource? _cts;
    public ObservableCollection<AutoscanSource> Sources { get; } = [];
    public ObservableCollection<AutoscanConnection> Connections { get; } = [];
    public ObservableCollection<AutoscanAvailableSource> AvailableSources { get; } = [];
    public ObservableCollection<AutoscanEvent> Events { get; } = [];
    public ObservableCollection<AutoscanScan> Scans { get; } = [];
    public ObservableCollection<AutoscanScan> ActiveScans { get; } = [];
    public ObservableCollection<AutoscanRunningPoll> RunningPolls { get; } = [];
    public ObservableCollection<RequestIntegration> RequestIntegrations { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private double _defaultPollIntervalSeconds = 300;
    [ObservableProperty] private double _debounceSeconds = 10;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private AutoscanStatus? _status;
    [ObservableProperty] private int _eventTotal;
    [ObservableProperty] private int _scanTotal;
    [ObservableProperty] private string _activityView = "Scans";
    [ObservableProperty] private string _activityQuery = "";
    [ObservableProperty] private string _activityStatus = "All statuses";
    [ObservableProperty] private int _activityPage;
    [ObservableProperty] private int _activityPageSize = 25;
    public bool CanGoBack => ActivityPage > 0;
    public bool CanGoForward => (ActivityPage + 1) * ActivityPageSize < (ActivityView == "Scans" ? ScanTotal : EventTotal);
    public int ActivityTotal => ActivityView == "Scans" ? ScanTotal : EventTotal;
    public string ActivityPageSummary => ActivityTotal == 0
        ? $"No {ActivityView.ToLowerInvariant()} found"
        : $"Showing {ActivityPage * ActivityPageSize + 1:N0}–{Math.Min((ActivityPage + 1) * ActivityPageSize, ActivityTotal):N0} of {ActivityTotal:N0} {ActivityView.ToLowerInvariant()}";
    public bool HasSources => Sources.Count > 0;
    public bool ShowScans => ActivityView == "Scans";
    public bool ShowPolls => !ShowScans;
    public string ActivityHistoryTitle => ShowScans ? "Scan history" : "Poll log";
    public string ActivityHistoryDescription => ShowScans
        ? "Real scan rows created by autoscan, searchable by path, library, source, status, or scan id."
        : "Diagnostic poll records from scan-source plugins.";
    public bool HasActiveScans => ActiveScans.Count > 0;
    public bool HasRunningPolls => RunningPolls.Count > 0;
    public string LatestPollDisplay => Status?.LatestEventAt?.ToLocalTime().ToString("hh:mm tt") ?? "—";
    public bool IsActivityFiltered => !string.IsNullOrWhiteSpace(ActivityQuery) || ActivityStatus != "All statuses";
    public bool HasFeedback => IsLoading || !string.IsNullOrWhiteSpace(ErrorMessage) || !string.IsNullOrWhiteSpace(StatusMessage);
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasFeedback));
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasFeedback));
    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasFeedback));
    partial void OnActivityViewChanged(string value) { ActivityStatus = "All statuses"; ActivityPage = 0; OnPropertyChanged(nameof(ShowScans)); OnPropertyChanged(nameof(ShowPolls)); OnPropertyChanged(nameof(ActivityHistoryTitle)); OnPropertyChanged(nameof(ActivityHistoryDescription)); NotifyActivityPaging(); }
    partial void OnActivityQueryChanged(string value) => OnPropertyChanged(nameof(IsActivityFiltered));
    partial void OnActivityStatusChanged(string value) => OnPropertyChanged(nameof(IsActivityFiltered));

    public async Task LoadAsync()
    {
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, owner);
        previous?.Cancel(); previous?.Dispose();
        IsLoading = true; ErrorMessage = null;
        try
        {
            var settingsTask = adminApi.GetAutoscanSettingsAsync(owner.Token);
            var sourcesTask = adminApi.GetAutoscanSourcesAsync(owner.Token);
            var connectionsTask = adminApi.GetAutoscanConnectionsAsync(owner.Token);
            var pluginsTask = adminApi.GetAvailableAutoscanSourcesAsync(owner.Token);
            var statusTask = adminApi.GetAutoscanStatusAsync(owner.Token);
            var eventsTask = adminApi.GetAutoscanEventsAsync("?limit=50&offset=0", owner.Token);
            var scansTask = adminApi.GetAutoscanScansAsync("?limit=50&offset=0", owner.Token);
            var integrationsTask = requestsApi.GetRequestIntegrationsAsync(owner.Token);
            var librariesTask = adminApi.GetAdminLibrariesAsync(owner.Token);
            await Task.WhenAll(settingsTask, sourcesTask, connectionsTask, pluginsTask, statusTask, eventsTask, scansTask, integrationsTask, librariesTask);
            if (!ReferenceEquals(_cts, owner)) return;
            var settings = settingsTask.Result;
            Enabled = settings.Enabled;
            DefaultPollIntervalSeconds = settings.DefaultPollIntervalSeconds;
            DebounceSeconds = settings.DebounceSeconds;
            Replace(Connections, connectionsTask.Result);
            Replace(AvailableSources, pluginsTask.Result);
            Replace(Libraries, librariesTask.Result.Where(library => library.Enabled));
            DecorateSources(sourcesTask.Result);
            Replace(Sources, sourcesTask.Result);
            OnPropertyChanged(nameof(HasSources));
            Status = statusTask.Result;
            OnPropertyChanged(nameof(LatestPollDisplay));
            Replace(RunningPolls, Status.RunningPolls); OnPropertyChanged(nameof(HasRunningPolls));
            DecorateEvents(eventsTask.Result.Events);
            Replace(Events, eventsTask.Result.Events); EventTotal = eventsTask.Result.Total;
            DecorateScans(scansTask.Result.Scans);
            Replace(Scans, scansTask.Result.Scans); ScanTotal = scansTask.Result.Total; UpdateActiveScans();
            Replace(RequestIntegrations, integrationsTask.Result.Integrations.Where(IsArrIntegration));
            NotifyActivityPaging();
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_cts, owner)) ErrorMessage = ex.Message; }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _cts, null, owner), owner)) IsLoading = false;
            owner.Dispose();
        }
    }

    public async Task SaveSettingsAsync(bool? enabled = null)
    {
        await RunBusyAsync(async () =>
        {
            var saved = await adminApi.UpdateAutoscanSettingsAsync(new AutoscanSettings
            {
                Enabled = enabled ?? Enabled,
                DefaultPollIntervalSeconds = Math.Max(1, (int)DefaultPollIntervalSeconds),
                DebounceSeconds = Math.Max(0, (int)DebounceSeconds),
            });
            Enabled = saved.Enabled; DefaultPollIntervalSeconds = saved.DefaultPollIntervalSeconds; DebounceSeconds = saved.DebounceSeconds;
            StatusMessage = "Autoscan settings saved.";
        });
    }

    public async Task TriggerAsync() => await RunBusyAsync(async () =>
    {
        await adminApi.TriggerAutoscanAsync(); StatusMessage = "Autoscan triggered."; await RefreshActivityAsync();
    });

    public async Task SaveConnectionAsync(AutoscanConnection? existing, AutoscanConnectionInput input)
        => await RunBusyAsync(async () =>
        {
            if (existing is null) await adminApi.CreateAutoscanConnectionAsync(input);
            else await adminApi.UpdateAutoscanConnectionAsync(existing.Id, input);
            Replace(Connections, await adminApi.GetAutoscanConnectionsAsync());
            DecorateSources(Sources);
            StatusMessage = existing is null ? "Connection created." : "Connection saved.";
        });

    public async Task<AutoscanConnectionTestResult?> TestConnectionAsync(object input)
    {
        AutoscanConnectionTestResult? result = null;
        await RunBusyAsync(async () => result = await adminApi.TestAutoscanConnectionAsync(input));
        return result;
    }

    public async Task DeleteConnectionAsync(AutoscanConnection connection)
        => await RunBusyAsync(async () => { await adminApi.DeleteAutoscanConnectionAsync(connection.Id); Connections.Remove(connection); StatusMessage = "Connection deleted."; });

    public async Task AddSourceAsync(AutoscanAvailableSource plugin, AutoscanConnection? connection, int? interval)
        => await RunBusyAsync(async () =>
        {
            var config = plugin.PluginId == "silo.autoscan.cephfs" || plugin.CapabilityId == "cephfs"
                ? new Dictionary<string, string> { ["exclusions"] = "*.partial\n*.tmp\n@eaDir\n#recycle\n.downloads\n.recyclebin\nvolumes" }
                : [];
            await adminApi.CreateAutoscanSourceAsync(new AutoscanSourceCreateInput { PluginId = plugin.PluginId, CapabilityId = plugin.CapabilityId, ConnectionId = connection?.Id, Enabled = true, PollIntervalSeconds = interval, PathRewrites = [], SourceConfig = config });
            var sources = await adminApi.GetAutoscanSourcesAsync(); DecorateSources(sources); Replace(Sources, sources); StatusMessage = "Scan source added.";
            OnPropertyChanged(nameof(HasSources));
        });

    public async Task SaveSourceAsync(AutoscanSource source)
        => await RunBusyAsync(async () =>
        {
            await adminApi.UpdateAutoscanSourceAsync(source.Id, new AutoscanSourceInput { ConnectionId = source.ConnectionId, Enabled = source.Enabled, DeliveryMode = source.DeliveryMode, PollIntervalSeconds = source.PollIntervalSeconds, PathRewrites = source.PathRewrites, SourceConfig = source.SourceConfig, Label = source.Label });
            var sources = await adminApi.GetAutoscanSourcesAsync(); DecorateSources(sources); Replace(Sources, sources); StatusMessage = "Scan source saved.";
            OnPropertyChanged(nameof(HasSources));
        });

    public async Task DeleteSourceAsync(AutoscanSource source)
        => await RunBusyAsync(async () => { await adminApi.DeleteAutoscanSourceAsync(source.Id); Sources.Remove(source); OnPropertyChanged(nameof(HasSources)); StatusMessage = "Scan source deleted."; });

    public Task<AutoscanRewriteSuggestions> GetRewriteSuggestionsAsync(AutoscanSource source)
        => adminApi.GetAutoscanRewriteSuggestionsAsync(source.Id);

    public async Task CreateWebhookAsync(AutoscanSource source)
        => await RunBusyAsync(async () => { await adminApi.CreateAutoscanWebhookAsync(source.Id); var sources = await adminApi.GetAutoscanSourcesAsync(); DecorateSources(sources); Replace(Sources, sources); StatusMessage = "Webhook URL created."; });
    public async Task RotateWebhookAsync(AutoscanSource source)
        => await RunBusyAsync(async () => { await adminApi.RotateAutoscanWebhookAsync(source.Id); var sources = await adminApi.GetAutoscanSourcesAsync(); DecorateSources(sources); Replace(Sources, sources); StatusMessage = "Webhook URL rotated. Update the sending service."; });
    public async Task DeleteWebhookAsync(AutoscanSource source)
        => await RunBusyAsync(async () => { await adminApi.DeleteAutoscanWebhookAsync(source.Id); var sources = await adminApi.GetAutoscanSourcesAsync(); DecorateSources(sources); Replace(Sources, sources); StatusMessage = "Webhook URL deleted."; });

    public async Task RefreshActivityAsync()
        => await RunBusyAsync(async () =>
        {
            Status = await adminApi.GetAutoscanStatusAsync(); OnPropertyChanged(nameof(LatestPollDisplay)); Replace(RunningPolls, Status.RunningPolls); OnPropertyChanged(nameof(HasRunningPolls));
            var query = BuildActivityQuery();
            if (ActivityView == "Scans") { var scans = await adminApi.GetAutoscanScansAsync(query); DecorateScans(scans.Scans); Replace(Scans, scans.Scans); ScanTotal = scans.Total; UpdateActiveScans(); }
            else { var events = await adminApi.GetAutoscanEventsAsync(query); DecorateEvents(events.Events); Replace(Events, events.Events); EventTotal = events.Total; }
            NotifyActivityPaging();
        }, clearStatus: false);

    public async Task ChangeActivityPageAsync(int delta)
    {
        ActivityPage = Math.Max(0, ActivityPage + delta); await RefreshActivityAsync();
    }
    public async Task ApplyActivityFiltersAsync()
    {
        ActivityPage = 0; await RefreshActivityAsync();
    }
    public async Task SetActivityPageSizeAsync(int pageSize)
    {
        ActivityPageSize = pageSize;
        ActivityPage = 0;
        await RefreshActivityAsync();
    }
    public async Task GoToActivityPageAsync(int page)
    {
        var lastPage = Math.Max(0, (int)Math.Ceiling(ActivityTotal / (double)ActivityPageSize) - 1);
        ActivityPage = Math.Clamp(page, 0, lastPage);
        await RefreshActivityAsync();
    }
    public async Task ResetActivityFiltersAsync()
    {
        ActivityQuery = "";
        ActivityStatus = "All statuses";
        ActivityPage = 0;
        await RefreshActivityAsync();
    }
    private string BuildActivityQuery()
    {
        var parts = new List<string> { $"limit={ActivityPageSize}", $"offset={ActivityPage * ActivityPageSize}" };
        if (!string.IsNullOrWhiteSpace(ActivityQuery)) parts.Add("q=" + Uri.EscapeDataString(ActivityQuery.Trim()));
        if (ActivityStatus != "All statuses") parts.Add("status=" + Uri.EscapeDataString(ActivityStatus.ToLowerInvariant().Replace("queued", "accepted")));
        return "?" + string.Join("&", parts);
    }

    private static bool IsArrIntegration(RequestIntegration integration)
    {
        if (integration.PluginConfig?.TryGetValue("service_kind", out var raw) != true || raw is null) return false;
        var kind = raw is System.Text.Json.JsonElement element ? element.GetString() : raw.ToString();
        return kind is "sonarr" or "radarr";
    }

    public static string IntegrationKind(RequestIntegration integration)
    {
        if (integration.PluginConfig?.TryGetValue("service_kind", out var raw) != true || raw is null) return "sonarr";
        return raw is System.Text.Json.JsonElement element ? element.GetString() ?? "sonarr" : raw.ToString() ?? "sonarr";
    }

    private async Task RunBusyAsync(Func<Task> work, bool clearStatus = true)
    {
        if (IsBusy) return;
        IsBusy = true; ErrorMessage = null; if (clearStatus) StatusMessage = null;
        try { await work(); } catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    { target.Clear(); foreach (var value in values) target.Add(value); }
    private void DecorateSources(IEnumerable<AutoscanSource> sources)
    {
        var pluginNames = AvailableSources.ToDictionary(p => $"{p.PluginId}:{p.CapabilityId}", p => p.DisplayName, StringComparer.OrdinalIgnoreCase);
        var connectionNames = Connections.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            source.SourceDisplayName = !string.IsNullOrWhiteSpace(source.Label)
                ? source.Label
                : pluginNames.GetValueOrDefault($"{source.PluginId}:{source.CapabilityId}")
                    ?? (source.IsCephFs ? "CephFS" : source.CapabilityId);
            source.ConnectionDisplay = string.IsNullOrWhiteSpace(source.ConnectionId)
                ? "— No connection —"
                : connectionNames.GetValueOrDefault(source.ConnectionId) ?? source.ConnectionId;
            source.PollIntervalHelp = $"Floor only - values below the global default ({Math.Max(1, (int)DefaultPollIntervalSeconds)}s) have no effect.";
        }
    }
    private void DecorateScans(IEnumerable<AutoscanScan> scans)
    {
        var libraryNames = Libraries.ToDictionary(library => library.Id, library => library.Name);
        var sourceNames = Sources.ToDictionary(source => source.Id, source => source.SourceDisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var scan in scans)
        {
            scan.LibraryDisplayName = libraryNames.GetValueOrDefault(scan.LibraryId) ?? $"Library {scan.LibraryId}";
            scan.SourceDisplayName = scan.SourceId is not null && sourceNames.TryGetValue(scan.SourceId, out var sourceName)
                ? sourceName
                : scan.CapabilityId ?? scan.PluginId ?? "—";
        }
    }
    private void DecorateEvents(IEnumerable<AutoscanEvent> events)
    {
        var sourceNames = Sources.ToDictionary(source => source.Id, source => source.SourceDisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var evt in events)
            evt.SourceDisplayName = evt.SourceId is not null && sourceNames.TryGetValue(evt.SourceId, out var sourceName)
                ? sourceName
                : evt.CapabilityId;
    }
    private void NotifyActivityPaging()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(ActivityTotal));
        OnPropertyChanged(nameof(ActivityPageSummary));
    }
    private void UpdateActiveScans()
    {
        Replace(ActiveScans, Scans.Where(scan => scan.Status is "accepted" or "running"));
        OnPropertyChanged(nameof(HasActiveScans));
    }
    public void Cancel() => Interlocked.Exchange(ref _cts, null)?.Cancel();
}
