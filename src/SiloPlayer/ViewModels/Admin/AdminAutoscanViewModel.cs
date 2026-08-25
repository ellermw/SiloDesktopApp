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
    private bool _activityLoaded;
    private bool _requestIntegrationsLoaded;
    private bool _librariesLoaded;
    public ObservableCollection<AutoscanSource> Sources { get; } = [];
    public ObservableCollection<AutoscanConnection> Connections { get; } = [];
    public ObservableCollection<AutoscanConnectionOption> SourceConnectionOptions { get; } = [];
    public ObservableCollection<AutoscanAvailableSource> AvailableSources { get; } = [];
    public ObservableCollection<AutoscanEvent> Events { get; } = [];
    public ObservableCollection<AutoscanScan> Scans { get; } = [];
    public ObservableCollection<AutoscanScan> ActiveScans { get; } = [];
    public ObservableCollection<AutoscanRunningPoll> RunningPolls { get; } = [];
    public ObservableCollection<RequestIntegration> RequestIntegrations { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isActivityLoading;
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
    public bool HasSources => !IsLoading && Sources.Count > 0;
    public bool ShowSourcesEmpty => !IsLoading && Sources.Count == 0;
    public bool HasConnections => !IsLoading && Connections.Count > 0;
    public bool ShowConnectionsEmpty => !IsLoading && Connections.Count == 0;
    public bool ShowScans => ActivityView == "Scans";
    public bool ShowPolls => !ShowScans;
    public bool HasActivityRows => ShowScans ? Scans.Count > 0 : Events.Count > 0;
    public bool ShowActivityEmpty => !IsActivityLoading && !HasActivityRows;
    public string ActivityEmptyMessage => IsActivityFiltered
        ? "No autoscan activity matches those filters."
        : ShowScans ? "No autoscan scans have been created yet." : "No autoscan polls have been recorded yet.";
    public string ActivityHistoryTitle => ShowScans ? "Scan history" : "Poll log";
    public string ActivityHistoryDescription => ShowScans
        ? "Real scan rows created by autoscan, searchable by path, library, source, status, or scan id."
        : "Diagnostic poll records from scan-source plugins.";
    public bool HasActiveScans => ActiveScans.Count > 0;
    public string EmptyQueueMessage => Status?.ActiveScans > 0
        ? "Queue counts are active, but live scan details have not arrived yet."
        : "No autoscan scans are queued or running.";
    public bool HasRunningPolls => RunningPolls.Count > 0;
    public string LatestPollDisplay => Status?.LatestEventAt is { } latest
        ? SiloPlayer.Helpers.DateTimeDisplay.FormatTime(latest)
        : "—";
    public bool IsActivityFiltered => !string.IsNullOrWhiteSpace(ActivityQuery) || ActivityStatus != "All statuses";
    public bool HasFeedback => IsLoading || !string.IsNullOrWhiteSpace(ErrorMessage) || !string.IsNullOrWhiteSpace(StatusMessage);
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasFeedback));
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(ShowSourcesEmpty));
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(ShowConnectionsEmpty));
    }
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasFeedback));
    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasFeedback));
    partial void OnIsActivityLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowActivityEmpty));
    partial void OnActivityViewChanged(string value) { ActivityStatus = "All statuses"; ActivityPage = 0; OnPropertyChanged(nameof(ShowScans)); OnPropertyChanged(nameof(ShowPolls)); OnPropertyChanged(nameof(HasActivityRows)); OnPropertyChanged(nameof(ShowActivityEmpty)); OnPropertyChanged(nameof(ActivityEmptyMessage)); OnPropertyChanged(nameof(ActivityHistoryTitle)); OnPropertyChanged(nameof(ActivityHistoryDescription)); NotifyActivityPaging(); }
    partial void OnActivityQueryChanged(string value) { OnPropertyChanged(nameof(IsActivityFiltered)); OnPropertyChanged(nameof(ActivityEmptyMessage)); }
    partial void OnActivityStatusChanged(string value) { OnPropertyChanged(nameof(IsActivityFiltered)); OnPropertyChanged(nameof(ActivityEmptyMessage)); }

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
            await Task.WhenAll(settingsTask, sourcesTask, connectionsTask, pluginsTask);
            if (!ReferenceEquals(_cts, owner)) return;
            var settings = settingsTask.Result;
            Enabled = settings.Enabled;
            DefaultPollIntervalSeconds = settings.DefaultPollIntervalSeconds;
            DebounceSeconds = settings.DebounceSeconds;
            Replace(Connections, connectionsTask.Result);
            RebuildSourceConnectionOptions();
            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(ShowConnectionsEmpty));
            Replace(AvailableSources, pluginsTask.Result);
            DecorateSources(sourcesTask.Result);
            Replace(Sources, sourcesTask.Result);
            OnPropertyChanged(nameof(HasSources));
            OnPropertyChanged(nameof(ShowSourcesEmpty));
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_cts, owner)) ErrorMessage = ex.Message; }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _cts, null, owner), owner)) IsLoading = false;
            owner.Dispose();
        }
    }

    public async Task EnsureActivityLoadedAsync()
    {
        if (_activityLoaded) return;
        IsActivityLoading = true;
        try
        {
            await RunBusyAsync(async () =>
            {
                var statusTask = adminApi.GetAutoscanStatusAsync();
                var eventsTask = adminApi.GetAutoscanEventsAsync("?limit=25&offset=0");
                var scansTask = adminApi.GetAutoscanScansAsync("?limit=25&offset=0");
                var librariesTask = EnsureLibrariesLoadedAsync();
                await Task.WhenAll(statusTask, eventsTask, scansTask, librariesTask);
                Status = statusTask.Result;
                OnPropertyChanged(nameof(LatestPollDisplay));
                OnPropertyChanged(nameof(EmptyQueueMessage));
                Replace(RunningPolls, Status.RunningPolls);
                OnPropertyChanged(nameof(HasRunningPolls));
                DecorateEvents(eventsTask.Result.Events);
                Replace(Events, eventsTask.Result.Events);
                EventTotal = eventsTask.Result.Total;
                DecorateScans(scansTask.Result.Scans);
                Replace(Scans, scansTask.Result.Scans);
                ScanTotal = scansTask.Result.Total;
                OnPropertyChanged(nameof(HasActivityRows));
                OnPropertyChanged(nameof(ShowActivityEmpty));
                OnPropertyChanged(nameof(ActivityEmptyMessage));
                NotifyActivityPaging();
                _activityLoaded = true;
            }, clearStatus: false);
        }
        finally { IsActivityLoading = false; }
    }

    public async Task EnsureRequestIntegrationsLoadedAsync()
    {
        if (_requestIntegrationsLoaded) return;
        var response = await requestsApi.GetRequestIntegrationsAsync();
        Replace(RequestIntegrations, response.Integrations.Where(IsArrIntegration));
        _requestIntegrationsLoaded = true;
    }

    public async Task EnsureLibrariesLoadedAsync()
    {
        if (_librariesLoaded) return;
        var libraries = await adminApi.GetAdminLibrariesAsync();
        Replace(Libraries, libraries.Where(library => library.Enabled));
        _librariesLoaded = true;
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
            RebuildSourceConnectionOptions();
            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(ShowConnectionsEmpty));
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
        => await RunBusyAsync(async () =>
        {
            await adminApi.DeleteAutoscanConnectionAsync(connection.Id);
            Connections.Remove(connection);
            RebuildSourceConnectionOptions();
            OnPropertyChanged(nameof(HasConnections));
            OnPropertyChanged(nameof(ShowConnectionsEmpty));
            StatusMessage = "Connection deleted.";
        });

    public async Task AddSourceAsync(AutoscanAvailableSource plugin, AutoscanConnection? connection, int? interval)
        => await RunBusyAsync(async () =>
        {
            var isWebhook = plugin.PluginId.Equals("silo.autoscan.arr-webhook", StringComparison.OrdinalIgnoreCase);
            var config = plugin.PluginId == "silo.autoscan.cephfs" || plugin.CapabilityId == "cephfs"
                ? new Dictionary<string, string> { ["exclusions"] = "*.partial\n*.tmp\n@eaDir\n#recycle\n.downloads\n.recyclebin\nvolumes" }
                : isWebhook ? new Dictionary<string, string> { ["webhook_provider"] = "auto" } : [];
            await adminApi.CreateAutoscanSourceAsync(new AutoscanSourceCreateInput { PluginId = plugin.PluginId, CapabilityId = plugin.CapabilityId, ConnectionId = isWebhook ? null : connection?.Id, Enabled = false, DeliveryMode = isWebhook ? "webhook" : "poll", PollIntervalSeconds = isWebhook ? null : interval, PathRewrites = [], SourceConfig = config });
            var sources = await adminApi.GetAutoscanSourcesAsync(); DecorateSources(sources); Replace(Sources, sources); StatusMessage = "Scan source added.";
            OnPropertyChanged(nameof(HasSources));
        });

    public async Task SaveSourceAsync(AutoscanSource source)
        => await RunBusyAsync(async () =>
        {
            var saved = await adminApi.UpdateAutoscanSourceAsync(source.Id, new AutoscanSourceInput { ConnectionId = source.ConnectionId, Enabled = source.Enabled, DeliveryMode = source.DeliveryMode, PollIntervalSeconds = source.PollIntervalSeconds, PathRewrites = source.PathRewrites, SourceConfig = source.SourceConfig, Label = source.Label });
            source.ConnectionId = saved.ConnectionId;
            source.Enabled = saved.Enabled;
            source.DeliveryMode = saved.DeliveryMode;
            source.PollIntervalSeconds = saved.PollIntervalSeconds;
            source.PathRewrites = saved.PathRewrites;
            source.SourceConfig = saved.SourceConfig;
            source.Label = saved.Label;
            source.LastRunAt = saved.LastRunAt;
            source.LastError = saved.LastError;
            source.WebhookConfigured = saved.WebhookConfigured;
            source.WebhookUrl = saved.WebhookUrl;
            source.WebhookSecretSuffix = saved.WebhookSecretSuffix;
            source.WebhookLastReceivedAt = saved.WebhookLastReceivedAt;
            source.WebhookLastErrorAt = saved.WebhookLastErrorAt;
            source.WebhookLastErrorMessage = saved.WebhookLastErrorMessage;
            DecorateSources([source]);
            var sourceIndex = Sources.IndexOf(source);
            if (sourceIndex >= 0) Sources[sourceIndex] = source;
            StatusMessage = "Scan source saved.";
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
            if (ActivityView == "Scans") { var scans = await adminApi.GetAutoscanScansAsync(query); DecorateScans(scans.Scans); Replace(Scans, scans.Scans); ScanTotal = scans.Total; }
            else { var events = await adminApi.GetAutoscanEventsAsync(query); DecorateEvents(events.Events); Replace(Events, events.Events); EventTotal = events.Total; }
            OnPropertyChanged(nameof(EmptyQueueMessage));
            OnPropertyChanged(nameof(HasActivityRows));
            OnPropertyChanged(nameof(ShowActivityEmpty));
            OnPropertyChanged(nameof(ActivityEmptyMessage));
            NotifyActivityPaging();
        }, clearStatus: false);

    public void ApplyActiveScanSnapshot(IEnumerable<AdminScanRun> scans)
    {
        var mapped = scans
            .Where(scan => string.Equals(scan.Trigger, "autoscan", StringComparison.OrdinalIgnoreCase)
                && scan.Status is "accepted" or "queued" or "running")
            .Select(MapActiveScan)
            .OrderBy(scan => scan.Status == "running" ? 0 : 1)
            .ThenBy(scan => scan.StartedAt ?? DateTimeOffset.MaxValue)
            .ToList();
        DecorateScans(mapped);
        Replace(ActiveScans, mapped);
        OnPropertyChanged(nameof(HasActiveScans));
        OnPropertyChanged(nameof(EmptyQueueMessage));
    }

    public async Task CancelActiveScansAsync(int libraryId)
        => await RunBusyAsync(async () =>
        {
            await adminApi.CancelLibraryScansAsync(libraryId);
            StatusMessage = "Scan cancellation requested.";
        });

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
    private void RebuildSourceConnectionOptions()
    {
        Replace(SourceConnectionOptions, new[] { new AutoscanConnectionOption { Id = null, Name = "— No connection —" } }
            .Concat(Connections.Select(connection => new AutoscanConnectionOption { Id = connection.Id, Name = connection.Name })));
    }
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
    private static AutoscanScan MapActiveScan(AdminScanRun scan)
    {
        static DateTimeOffset? Parse(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
        var progress = scan.Result?.Message;
        if (string.IsNullOrWhiteSpace(progress) && scan.Result is { TotalFiles: > 0 } result)
            progress = $"{result.FilesProcessed:N0} of {result.TotalFiles:N0} files";
        if (string.IsNullOrWhiteSpace(progress)) progress = string.IsNullOrWhiteSpace(scan.Trigger) ? "Autoscan" : scan.Trigger;
        return new AutoscanScan
        {
            Id = scan.Id,
            LibraryId = scan.LibraryId,
            Mode = scan.Mode,
            Path = scan.Path,
            Trigger = scan.Trigger,
            Status = scan.Status == "queued" ? "accepted" : scan.Status,
            ErrorMessage = scan.ErrorMessage,
            StartedAt = Parse(scan.StartedAt),
            CompletedAt = Parse(scan.CompletedAt),
            ProgressDisplay = progress,
        };
    }
    public void Cancel() => Interlocked.Exchange(ref _cts, null)?.Cancel();
}
