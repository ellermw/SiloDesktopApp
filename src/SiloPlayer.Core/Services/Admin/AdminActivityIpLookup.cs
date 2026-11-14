using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminIpLookupView { Idle, Searching, LoadError, Empty, Rows }

/// <summary>
/// The Activity page's IP Lookup (WebUI <c>useIPUsers</c>): paged history for one
/// IP, retaining good pages when a later page fails, refusing repeated cursors,
/// and dropping any result whose lease retired or whose lookup was replaced.
/// </summary>
public sealed class AdminActivityIpLookup(AdminMonitorApi api)
{
    private readonly object _gate = new();
    private readonly List<AdminActivityIpUser> _rows = [];
    private readonly List<string?> _cursors = [];
    private long _version;

    public string ActiveIp { get; private set; } = "";
    public IReadOnlyList<AdminActivityIpUser> Rows { get { lock (_gate) return _rows.ToArray(); } }
    public bool IsLoading { get; private set; }
    public bool IsFetchingNextPage { get; private set; }
    public bool HasError { get; private set; }
    public string? NextCursor { get; private set; }
    public bool HasNextPage => NextCursor != null;

    public event Action? Changed;

    public AdminIpLookupView View
    {
        get
        {
            if (ActiveIp.Length == 0) return AdminIpLookupView.Idle;
            if (IsLoading) return AdminIpLookupView.Searching;
            lock (_gate)
            {
                if (HasError && _rows.Count == 0) return AdminIpLookupView.LoadError;
                return _rows.Count == 0 ? AdminIpLookupView.Empty : AdminIpLookupView.Rows;
            }
        }
    }

    /// <summary>Starts (or restarts) a lookup; an empty IP clears it.</summary>
    public Task StartAsync(MonitorAuthorityLease lease, string ip)
    {
        long version;
        lock (_gate)
        {
            version = ++_version;
            ActiveIp = ip.Trim();
            _rows.Clear();
            _cursors.Clear();
            NextCursor = null;
            HasError = false;
            IsFetchingNextPage = false;
            IsLoading = ActiveIp.Length > 0;
        }
        Changed?.Invoke();
        return ActiveIp.Length == 0 ? Task.CompletedTask : ReadAsync(lease, version, null);
    }

    /// <summary>Reload history: resets to the first page of the same IP.</summary>
    public Task RestartAsync(MonitorAuthorityLease lease) => StartAsync(lease, ActiveIp);

    public Task LoadMoreAsync(MonitorAuthorityLease lease)
    {
        long version;
        string? cursor;
        lock (_gate)
        {
            if (NextCursor == null || IsFetchingNextPage || IsLoading) return Task.CompletedTask;
            version = _version;
            cursor = NextCursor;
            IsFetchingNextPage = true;
            HasError = false;
        }
        Changed?.Invoke();
        return ReadAsync(lease, version, cursor);
    }

    /// <summary>Forgets everything, as on authority retirement or unload.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _version++;
            ActiveIp = "";
            _rows.Clear();
            _cursors.Clear();
            NextCursor = null;
            HasError = false;
            IsLoading = IsFetchingNextPage = false;
        }
        Changed?.Invoke();
    }

    private async Task ReadAsync(MonitorAuthorityLease lease, long version, string? cursor)
    {
        try
        {
            string?[] prior;
            lock (_gate) prior = [.. _cursors, cursor];
            var page = await api.LookupActivityIpUsersAsync(lease, ActiveIp, cursor, prior).ConfigureAwait(false);
            lock (_gate)
            {
                if (version != _version || !lease.IsCurrent) return;
                _cursors.Add(cursor);
                _rows.AddRange(page.Items);
                NextCursor = page.NextCursor;
                IsLoading = IsFetchingNextPage = false;
            }
        }
        catch (OperationCanceledException) when (!lease.IsCurrent)
        {
            return;
        }
        catch (Exception)
        {
            lock (_gate)
            {
                if (version != _version || !lease.IsCurrent) return;
                HasError = true;
                IsLoading = IsFetchingNextPage = false;
            }
        }
        Changed?.Invoke();
    }
}
