using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

/// <summary>
/// A destination write whose reply is dropped: the page closed or the account or profile changed after it was sent, or
/// before it could be. Nothing is shown for it. A timeout under the same identity stays a plain OperationCanceledException.
/// </summary>
public sealed class NotificationDestinationWriteDroppedException(string message, Exception? inner = null) : OperationCanceledException(message, inner);

/// <summary>
/// Settings &gt; Notifications &gt; Other destinations (official silo-server 3b2b32168 #3550,
/// PluginDestinationsSection.tsx and notificationPluginDestinations.ts).
///
/// Everything is owned by the identity it was loaded under: a reply that arrives after an account or
/// profile switch is dropped. At most one write runs at a time, and a create, a test message, an update
/// or a delete is sent once: it never refreshes authentication and never retries.
/// </summary>
public sealed class NotificationPluginDestinationsState(NotificationsApi api) : ObservableObject
{
    private const int DefaultLimit = 10;
    private ApiRequestContext? _context;
    private CancellationTokenSource? _load;
    private int _writing;
    private bool _available;
    private int _max = DefaultLimit;
    private IReadOnlyList<NotificationPluginChannel> _channels = [];
    private IReadOnlyList<NotificationPluginDestination> _destinations = [];
    private bool _isLoading, _loadFailed, _isWriting;
    private string? _loadError, _writingId;
    private readonly Dictionary<string, NotificationWebhookTestResult> _tests = new(StringComparer.Ordinal);

    public bool Available { get => _available; private set => SetProperty(ref _available, value); }
    public int MaxPerProfile { get => _max; private set => SetProperty(ref _max, value); }
    public IReadOnlyList<NotificationPluginChannel> Channels { get => _channels; private set => SetProperty(ref _channels, value); }
    public IReadOnlyList<NotificationPluginDestination> Destinations { get => _destinations; private set => SetProperty(ref _destinations, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool LoadFailed { get => _loadFailed; private set => SetProperty(ref _loadFailed, value); }
    public string? LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }
    /// <summary>A create, update, delete or test message is in flight.</summary>
    public bool IsWriting { get => _isWriting; private set => SetProperty(ref _isWriting, value); }
    /// <summary>The destination (or "new") the running write is for.</summary>
    public string? WritingId { get => _writingId; private set => SetProperty(ref _writingId, value); }
    public ApiRequestContext? Context => _context;
    /// <summary>The list still belongs to the signed-in identity: false once the page closed or the account or profile changed.</summary>
    public bool OwnsIdentity => _context is { } context && api.IsCurrentContext(context);
    public IReadOnlyDictionary<string, NotificationWebhookTestResult> TestResults => _tests;

    /// <summary>Shown while an enabled plugin offers a kind, or the profile still has destinations from one that was removed.</summary>
    public bool IsVisible => Available || Destinations.Count > 0;
    public bool AtLimit => Destinations.Count >= MaxPerProfile;
    public bool CanAdd => Channels.Count > 0 && !AtLimit && !LoadFailed;

    /// <summary>
    /// Reads the capability's destination limits, then the channels (only while the capability is available)
    /// and the profile's destinations. A failure keeps nothing from the previous identity.
    /// </summary>
    public async Task LoadAsync(NotificationCapability capability, ApiRequestContext context, CancellationToken ct = default)
    {
        _load?.Cancel(); _load?.Dispose();
        var load = _load = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _context = context;
        Available = capability.PluginDestinations?.Available == true;
        MaxPerProfile = capability.PluginDestinations is { MaxPerProfile: > 0 } limits ? limits.MaxPerProfile : DefaultLimit;
        IsLoading = true; LoadFailed = false; LoadError = null;
        try
        {
            var channels = Available ? await api.GetPluginChannelsAsync(context, load.Token) : [];
            var destinations = await api.GetPluginDestinationsAsync(context, load.Token);
            if (!Current(context, load)) return;
            Channels = channels; Destinations = destinations; PruneTests();
        }
        catch (OperationCanceledException) when (!Current(context, load)) { }
        catch (Exception ex)
        {
            if (!Current(context, load)) return;
            Channels = []; Destinations = []; LoadFailed = true;
            LoadError = ex is ApiException problem ? ApiProblemText.Display(problem) : ex.Message;
        }
        finally { if (ReferenceEquals(_load, load)) IsLoading = false; }
    }

    /// <summary>Re-reads the destination list only, as after a save; a list that cannot be read leaves the previous rows and says so.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        if (_context is not { } context) return;
        var load = _load;
        try
        {
            var destinations = await api.GetPluginDestinationsAsync(context, ct);
            if (!api.IsCurrentContext(context) || !ReferenceEquals(load, _load)) return;
            Destinations = destinations; PruneTests(); LoadFailed = false; LoadError = null;
        }
        catch (OperationCanceledException) when (!api.IsCurrentContext(context) || ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!api.IsCurrentContext(context)) return;
            LoadError = ex is ApiException problem ? ApiProblemText.Display(problem) : ex.Message;
        }
    }

    /// <summary>Drops everything (the page closed or the identity changed) and cancels the load in flight.</summary>
    public void Close()
    {
        _load?.Cancel(); _load?.Dispose(); _load = null; _context = null;
        Channels = []; Destinations = []; _tests.Clear(); LoadFailed = false; LoadError = null; IsLoading = false;
    }

    // --- writes --------------------------------------------------------------------------------

    public async Task<NotificationPluginDestination> CreateAsync(NotificationDestinationForm form, string name, CancellationToken ct = default)
    {
        var channel = form.Channel ?? throw new InvalidOperationException("Choose a destination kind first.");
        var settings = form.SettingsForCreate();
        return await WriteThenReloadAsync("new", AppStrings.Get("web.settings.notifications.destinations.createInProgress", fallback: "Destination creation is already in progress."),
            context => api.CreatePluginDestinationAsync(context, channel.PluginId, channel.CapabilityId, name.Trim(), settings, form.NotifyNextUp, form.NotifyRequests, ct), ct);
    }

    public async Task<NotificationPluginDestination> UpdateAsync(NotificationPluginDestination destination, NotificationDestinationForm form, string name, CancellationToken ct = default)
    {
        var body = form.UpdateBody(name);
        return await WriteThenReloadAsync(destination.Id, BusyText, context => api.UpdatePluginDestinationAsync(context, destination.Id, destination.ETag, body, ct), ct);
    }

    /// <summary>The row's switch: sends only <c>enabled</c>.</summary>
    public async Task<NotificationPluginDestination> SetEnabledAsync(NotificationPluginDestination destination, bool enabled, CancellationToken ct = default)
    {
        return await WriteThenReloadAsync(destination.Id, BusyText, context => api.UpdatePluginDestinationAsync(context, destination.Id, destination.ETag,
            new Dictionary<string, object?> { ["enabled"] = enabled }, ct), ct);
    }

    /// <summary>Deletes the row as it was when the person chose to delete it; a newer reload cannot swap in a newer validator.</summary>
    public async Task DeleteAsync(string id, string etag, CancellationToken ct = default)
    {
        await WriteThenReloadAsync(id, BusyText, async context => { await api.DeletePluginDestinationAsync(context, id, etag, ct); _tests.Remove(id); return true; }, ct);
    }

    public async Task<NotificationWebhookTestResult> TestAsync(string id, CancellationToken ct = default)
    {
        var result = await WriteAsync(id, AppStrings.Get("web.settings.notifications.destinations.testInProgress", fallback: "A test message is already being sent."),
            context => api.TestPluginDestinationAsync(context, id, ct));
        if (_context is { } current && api.IsCurrentContext(current)) { _tests[id] = result; OnPropertyChanged(nameof(TestResults)); }
        return result;
    }

    private static string BusyText => "A destination change is already in progress.";

    /// <summary>
    /// A write, then a re-read of the list. A write that failed is followed by the same re-read when the rows on screen
    /// can no longer be trusted: the server no longer has the row or its validator (404, 409, 412, 428), or the request got
    /// no answer under the same identity, so the server may have applied it. The write itself is never sent again.
    /// </summary>
    private async Task<T> WriteThenReloadAsync<T>(string id, string busyMessage, Func<ApiRequestContext, Task<T>> send, CancellationToken ct)
    {
        var sent = _context;
        T result;
        try { result = await WriteAsync(id, busyMessage, send); }
        catch (Exception ex) when (LeavesRowsInDoubt(ex, ct))
        {
            await ReloadAsync(ct);
            // The identity can change during the re-read too; the unanswered write is then no longer this page's to report.
            if (ex is OperationCanceledException && !(sent is { } context && Owns(context)))
                throw new NotificationDestinationWriteDroppedException("Notification context changed.", ex);
            throw;
        }
        await ReloadAsync(ct);
        return result;
    }

    private static bool LeavesRowsInDoubt(Exception error, CancellationToken ct) => error switch
    {
        ApiException { StatusCode: 404 or 409 or 412 or 428 } => true,
        // A timeout is an OperationCanceledException too; one from a caller's cancellation or an identity change is not in doubt.
        NotificationDestinationWriteDroppedException => false,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };

    private async Task<T> WriteAsync<T>(string id, string busyMessage, Func<ApiRequestContext, Task<T>> send)
    {
        var context = _context ?? throw new NotificationDestinationWriteDroppedException("Notification destinations are not loaded.");
        if (!api.IsCurrentContext(context)) throw new NotificationDestinationWriteDroppedException("Notification context changed.");
        if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0) throw new InvalidOperationException(busyMessage);
        IsWriting = true; WritingId = id;
        try { return await send(context); }
        // Judged against the identity the write was sent under: by now a load may have put the next identity's context in _context.
        catch (OperationCanceledException ex) when (!Owns(context)) { throw new NotificationDestinationWriteDroppedException("Notification context changed.", ex); }
        finally { Volatile.Write(ref _writing, 0); IsWriting = false; WritingId = null; }
    }

    /// <summary>The page still shows the identity a write was sent under, and that identity is still the signed-in one.</summary>
    private bool Owns(ApiRequestContext context) => _context == context && api.IsCurrentContext(context);

    private bool Current(ApiRequestContext context, CancellationTokenSource load)
        => ReferenceEquals(_load, load) && !load.IsCancellationRequested && _context == context && api.IsCurrentContext(context);

    private void PruneTests()
    {
        foreach (var id in _tests.Keys.Except(Destinations.Select(row => row.Id)).ToArray()) _tests.Remove(id);
    }
}
