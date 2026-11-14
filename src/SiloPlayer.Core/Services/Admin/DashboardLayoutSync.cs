using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Instant-paint copy of an account's arrangement, scoped by server origin and account.</summary>
public interface IDashboardLayoutCache
{
    string? Read(string scope);
    void Write(string scope, string document);
    void Clear(string scope);
}

/// <summary>Server writes the layout controller needs; implemented over <see cref="AdminMonitorApi"/>.</summary>
public interface IDashboardLayoutServer
{
    Task<DashboardLayoutRead> ReadAsync(MonitorAuthorityLease lease, CancellationToken ct);
    Task<string> SaveAsync(MonitorAuthorityLease lease, string document, string expectedETag, CancellationToken ct);
    Task ResetAsync(MonitorAuthorityLease lease, CancellationToken ct);
}

public sealed class AdminMonitorDashboardLayoutServer(AdminMonitorApi api) : IDashboardLayoutServer
{
    public Task<DashboardLayoutRead> ReadAsync(MonitorAuthorityLease lease, CancellationToken ct) => api.GetDashboardLayoutAsync(lease, null, ct);
    public Task<string> SaveAsync(MonitorAuthorityLease lease, string document, string expectedETag, CancellationToken ct)
        => api.SaveDashboardLayoutAsync(lease, document, expectedETag, ct);
    public Task ResetAsync(MonitorAuthorityLease lease, CancellationToken ct) => api.ResetDashboardLayoutAsync(lease, ct);
}

/// <summary>In-memory cache for tests and hosts without a profile directory.</summary>
public sealed class DashboardMemoryLayoutCache : IDashboardLayoutCache
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public string? Read(string scope) { lock (_values) return _values.GetValueOrDefault(scope); }
    public void Write(string scope, string document) { lock (_values) _values[scope] = document; }
    public void Clear(string scope) { lock (_values) _values.Remove(scope); }
}

/// <summary>
/// One JSON file of hashed scopes to layout documents. Only arrangements are
/// stored: never credentials, profile proofs or server data.
/// </summary>
public sealed class DashboardFileLayoutCache(string path) : IDashboardLayoutCache
{
    private readonly object _gate = new();

    public string? Read(string scope)
    {
        lock (_gate) return Load().GetValueOrDefault(Hash(scope));
    }

    public void Write(string scope, string document)
    {
        lock (_gate)
        {
            var all = Load();
            all[Hash(scope)] = document;
            Save(all);
        }
    }

    public void Clear(string scope)
    {
        lock (_gate)
        {
            var all = Load();
            if (all.Remove(Hash(scope))) Save(all);
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(path)) return new(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, string> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Storage can be unavailable; the arrangement still works in memory.
        }
    }

    private static string Hash(string scope)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
}

/// <summary>
/// Owns the admin's widget arrangement for one captured authority lease.
/// The cache paints instantly; the server row is the source of truth and is
/// adopted once, never over an edit made in this session. Edits debounce into
/// a single 800ms write carrying the validator of the read they started from;
/// only an acknowledged write advances that validator. Writes and resets run
/// one at a time in order, so a reset can never be resurrected by an older
/// save. A failed or unconfirmed write blocks further server writes until an
/// explicit reload; local edits are kept. Nothing transfers to another lease.
/// </summary>
public sealed class DashboardLayoutSync : IDisposable
{
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(800);

    private readonly MonitorAuthorityLease _lease;
    private readonly IDashboardLayoutServer _server;
    private readonly IDashboardLayoutCache _cache;
    private readonly string _scope;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly TaskScheduler _scheduler = SynchronizationContext.Current != null
        ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Default;
    private Task _writeChain = Task.CompletedTask;
    private ITimer? _debounce;
    private bool _settled;
    private bool _edited;
    private bool _blocked;
    private bool _resetPending;
    private bool _disposed;
    private bool _departing;
    private long _editSequence;
    private string? _baseline;
    private SaveIntent? _pending;
    private SaveIntent? _inFlight;

    private sealed record SaveIntent(string Document, string ETag);

    public DashboardLayoutSync(MonitorAuthorityLease lease, IDashboardLayoutServer server, IDashboardLayoutCache cache,
        string serverOrigin, TimeProvider? time = null)
    {
        _lease = lease;
        _server = server;
        _cache = cache;
        _time = time ?? TimeProvider.System;
        _scope = Scope(serverOrigin, lease.AccountId);
        Entries = ReadCache() ?? DashboardRegistry.DefaultLayout;
    }

    public static string Scope(string serverOrigin, string accountId)
        => (serverOrigin ?? "").TrimEnd('/').ToLowerInvariant() + "|" + accountId;

    public IReadOnlyList<DashboardLayoutEntry> Entries { get; private set; }
    public bool IsCustomizing { get; private set; }
    public bool ServerSaveBlocked { get; private set; }
    public bool IsLoaded => _settled;
    public IReadOnlyList<DashboardWidgetDefinition> HiddenWidgets => DashboardLayoutDocument.Hidden(Entries);

    /// <summary>Raised after Entries, IsCustomizing or ServerSaveBlocked change.</summary>
    public event Action? Changed;
    /// <summary>A write's outcome is unknown or refused; local edits are retained.</summary>
    public event Action? SaveUnconfirmed;
    public event Action? ResetUnconfirmed;

    /// <summary>For tests: the in-order write chain.</summary>
    public Task PendingWrites { get { lock (_gate) return _writeChain; } }

    public void SetCustomizing(bool customizing)
    {
        if (IsCustomizing == customizing) return;
        IsCustomizing = customizing;
        RaiseChanged();
    }

    /// <summary>Reads the server layout once and adopts or migrates it.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        DashboardLayoutRead read;
        try
        {
            read = await _server.ReadAsync(_lease, ct);
        }
        catch (Exception) when (!_lease.IsCurrent || ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Without a validator every server write stays blocked until a reload succeeds.
            return;
        }
        if (_disposed || !_lease.IsCurrent || read.Value == null) return;
        lock (_gate)
        {
            if (_settled) return;
            _settled = true;
            _baseline = read.Value.ETag;
            if (_edited) return;
        }
        var server = DashboardLayoutDocument.Sanitize(read.Value.Layout);
        if (server != null)
        {
            Entries = server;
            WriteCache(server);
            RaiseChanged();
            return;
        }
        // No server layout yet: hand this account's cached arrangement up once.
        var local = ReadCache();
        if (local == null) return;
        lock (_gate) _pending = new SaveIntent(DashboardLayoutDocument.Serialize(local), _baseline!);
        Flush();
    }

    public void MoveWidget(string id, string? beforeId) => Update(entries => DashboardLayoutDocument.Move(entries, id, beforeId));
    public void ResizeWidget(string id, int? span, int? rows) => Update(entries => DashboardLayoutDocument.Resize(entries, id, span, rows));
    public void SetWidgetRange(string id, DashboardWidgetRange range) => Update(entries => DashboardLayoutDocument.SetRange(entries, id, range));
    public void RemoveWidget(string id) => Update(entries => DashboardLayoutDocument.Remove(entries, id));
    public void AddWidget(string id, string? beforeId = null) => Update(entries => DashboardLayoutDocument.Add(entries, id, beforeId));

    /// <summary>Restores defaults locally and deletes the server layout after any write already queued.</summary>
    public void ResetLayout()
    {
        if (_disposed || _departing || !_lease.IsCurrent) return;
        lock (_gate)
        {
            CancelPending_NoLock();
            _editSequence++;
            _baseline = null;
            _blocked = true;
            _edited = true;
            _settled = true;
            _resetPending = true;
        }
        ServerSaveBlocked = true;
        ClearCache();
        Entries = DashboardRegistry.DefaultLayout;
        RaiseChanged();
        Enqueue(async () =>
        {
            try
            {
                await _server.ResetAsync(_lease, _lease.Token);
            }
            catch (Exception)
            {
                if (!_disposed && _lease.IsCurrent) ResetUnconfirmed?.Invoke();
            }
            finally
            {
                lock (_gate) _resetPending = false;
            }
        });
    }

    /// <summary>Explicitly re-reads the server layout, discarding local edits, and resumes saving.</summary>
    public async Task ReloadServerLayoutAsync(CancellationToken ct = default)
    {
        long sequence;
        lock (_gate)
        {
            if (_disposed || !_lease.IsCurrent || _inFlight != null || _resetPending) return;
            sequence = _editSequence;
        }
        DashboardLayoutRead read;
        try
        {
            read = await _server.ReadAsync(_lease, ct);
        }
        catch (Exception)
        {
            return;
        }
        if (_disposed || !_lease.IsCurrent || read.Value == null || string.IsNullOrEmpty(read.Value.ETag)) return;
        IReadOnlyList<DashboardLayoutEntry> loaded;
        lock (_gate)
        {
            if (_editSequence != sequence) return;
            CancelPending_NoLock();
            loaded = DashboardLayoutDocument.Sanitize(read.Value.Layout) ?? DashboardRegistry.DefaultLayout;
            _baseline = read.Value.ETag;
            _blocked = false;
            _settled = true;
            _edited = false;
        }
        Entries = loaded;
        WriteCache(loaded);
        ServerSaveBlocked = false;
        RaiseChanged();
    }

    /// <summary>Sends a queued write now (page departure). Only under the still-valid lease.</summary>
    public void FlushNow()
    {
        if (_disposed || !_lease.IsCurrent) return;
        Flush();
    }

    private void Update(Func<IReadOnlyList<DashboardLayoutEntry>, IReadOnlyList<DashboardLayoutEntry>> updater)
    {
        if (_disposed || _departing || !_lease.IsCurrent) return;
        var next = updater(Entries);
        if (ReferenceEquals(next, Entries)) return;
        Entries = next;
        lock (_gate)
        {
            _edited = true;
            _editSequence++;
        }
        WriteCache(next);
        ScheduleSave(next);
        RaiseChanged();
    }

    private void ScheduleSave(IReadOnlyList<DashboardLayoutEntry> next)
    {
        lock (_gate)
        {
            if (_baseline == null || _blocked)
            {
                if (!ServerSaveBlocked) ServerSaveBlocked = true;
                return;
            }
            _pending = new SaveIntent(DashboardLayoutDocument.Serialize(next), _baseline);
            _debounce?.Dispose();
            _debounce = _time.CreateTimer(_ => OnDebounceElapsed(), null, SaveDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed()
    {
        void Run()
        {
            if (_disposed || !_lease.IsCurrent) return;
            Flush();
        }
        if (_context != null) _context.Post(_ => Run(), null);
        else Run();
    }

    private void Flush()
    {
        SaveIntent intent;
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = null;
            if (_inFlight != null || _blocked || _pending == null) return;
            intent = _pending;
            _pending = null;
            _inFlight = intent;
        }
        Enqueue(async () =>
        {
            string committed;
            try
            {
                committed = await _server.SaveAsync(_lease, intent.Document, intent.ETag, _lease.Token);
            }
            catch (Exception)
            {
                bool owned;
                lock (_gate)
                {
                    owned = ReferenceEquals(_inFlight, intent);
                    if (owned) _inFlight = null;
                }
                if (!owned || _disposed || !_lease.IsCurrent) return;
                lock (_gate) _blocked = true;
                ServerSaveBlocked = true;
                RaiseChanged();
                SaveUnconfirmed?.Invoke();
                return;
            }
            var followUp = false;
            lock (_gate)
            {
                if (!ReferenceEquals(_inFlight, intent)) return;
                _inFlight = null;
                // Only this acknowledged write advances retained edits.
                if (!_blocked && _baseline == intent.ETag)
                {
                    _baseline = committed;
                    if (_pending != null && _pending.ETag == intent.ETag) _pending = _pending with { ETag = committed };
                    followUp = true;
                }
            }
            if (followUp && _lease.IsCurrent) Flush();
        });
    }

    private void Enqueue(Func<Task> work)
    {
        lock (_gate)
        {
            _writeChain = _writeChain.ContinueWith(_ => work(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, _scheduler).Unwrap();
        }
    }

    private void CancelPending_NoLock()
    {
        _debounce?.Dispose();
        _debounce = null;
        _pending = null;
    }

    private IReadOnlyList<DashboardLayoutEntry>? ReadCache()
    {
        try { return DashboardLayoutDocument.Sanitize(_cache.Read(_scope)); }
        catch (Exception) { return null; }
    }

    private void WriteCache(IReadOnlyList<DashboardLayoutEntry> entries)
    {
        try { _cache.Write(_scope, DashboardLayoutDocument.Serialize(entries)); } catch (Exception) { }
    }

    private void ClearCache()
    {
        try { _cache.Clear(_scope); } catch (Exception) { }
    }

    private void RaiseChanged()
    {
        if (!_disposed) Changed?.Invoke();
    }

    /// <summary>
    /// Page departure: sends the queued edit under the still-valid lease and keeps
    /// a newer edit queued behind an in-flight write until that write acknowledges
    /// its validator, then retires. A retired lease sends nothing.
    /// </summary>
    public async Task DepartAsync()
    {
        if (_disposed) return;
        _departing = true;
        FlushNow();
        while (true)
        {
            Task chain;
            lock (_gate) chain = _writeChain;
            try { await chain; } catch (Exception) { }
            lock (_gate)
            {
                if (ReferenceEquals(_writeChain, chain)) break;
            }
        }
        Dispose();
    }

    /// <summary>Retires this arrangement owner: timers stop and queued edits are dropped, never transferred.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPending_NoLock();
        }
        Changed = null;
        SaveUnconfirmed = null;
        ResetUnconfirmed = null;
    }
}
