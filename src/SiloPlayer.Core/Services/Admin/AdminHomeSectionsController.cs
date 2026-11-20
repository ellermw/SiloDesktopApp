using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminHomeSectionsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public AdminHomeSectionsSnapshot? Snapshot { get; init; }
    public AdminHomeSectionCapabilities? Capabilities { get; init; }
    public AdminHomeSectionCatalog? Catalog { get; init; }
    public bool Busy { get; init; }
    public bool ReviewRequired { get; init; }
    public bool OutcomeUnknown { get; init; }
    public string? Error { get; init; }
}

/// <summary>One writer, no queue or replay. Every continuation is fenced by the page owner and a request revision.</summary>
public sealed class AdminHomeSectionsController : IDisposable
{
    private readonly AdminHomeSectionsApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminHomeSectionsState _state = new();
    private long _revision;
    private bool _disposed;
    private AdminHomeSectionScope _scope = AdminHomeSectionScope.Home;
    public AdminHomeSectionsController(AdminHomeSectionsApi api, AdminMonitorAuthority authority)
    { _api = api; _lifetime = new(authority); _lifetime.Retired += Clear; }
    public AdminHomeSectionsState State { get { lock (_gate) return _state; } }
    public bool CanWrite { get { lock (_gate) return Writable(); } }
    public event Action? Changed;
    public event Action? Saved;
    private bool Writable() => !_disposed && _state.Owner is { IsCurrent: true } && _state.Snapshot != null &&
        _state.Capabilities?.Available == true && !_state.Busy && !_state.ReviewRequired;

    public async Task OpenAsync(AdminHomeSectionScope? scope = null)
    {
        scope ??= AdminHomeSectionScope.Home; _ = scope.Query;
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _scope = scope; _revision++; _state = new() { Owner = owner }; }
        await ReloadAsync().ConfigureAwait(false);
    }

    public async Task ReloadAsync()
    {
        MonitorAuthorityLease owner; long revision;
        lock (_gate)
        {
            if (_disposed || _state.Busy || _state.Owner is not { IsCurrent: true } active) return;
            owner = active; revision = ++_revision; _state = _state with { Busy = true, Error = null };
        }
        Changed?.Invoke();
        try
        {
            var caps = await _api.CapabilitiesAsync(owner, owner.Token).ConfigureAwait(false);
            var snapshot = await _api.ReadAsync(owner, owner.Token, _scope).ConfigureAwait(false);
            // Catalog failure must not hide existing rows or prevent deleting a retired kind.
            AdminHomeSectionCatalog? catalog = null;
            try { catalog = await _api.CatalogAsync(owner, owner.Token).ConfigureAwait(false); }
            catch (Exception) when (owner.IsCurrent) { }
            Update(owner, revision, () => _state = new() { Owner = owner, Snapshot = snapshot, Capabilities = caps, Catalog = catalog });
        }
        catch (Exception ex) { Update(owner, revision, () => _state = _state with { Busy = false, Snapshot = null, Error = ReadError(ex) }); }
    }

    public async Task<AdminHomeSectionSession?> OpenEditAsync(string id)
    {
        MonitorAuthorityLease owner; long revision;
        lock (_gate)
        {
            if (!Writable()) return null;
            owner = _state.Owner!; revision = _revision;
        }
        var result = await _api.GetAsync(owner, id, owner.Token, _scope).ConfigureAwait(false);
        lock (_gate) return Current(owner, revision) ? result : null;
    }

    public Task<bool> SaveAsync(AdminHomeSectionSession? session, AdminHomeSectionDraft draft, IReadOnlyList<string>? extraLibraries = null) => WriteAsync(async (owner, snapshot, caps) =>
    {
        if (session == null)
        {
            if (extraLibraries is { Count: > 0 } && snapshot.Scope.LibraryId != null)
            {
                if (draft.Featured) throw new ArgumentException("New hero rows stay on their current page.");
                await _api.BulkCopyAsync(owner, draft, new[] { snapshot.Scope.LibraryId }.Concat(extraLibraries).Distinct().ToArray(), owner.Token).ConfigureAwait(false); return;
            }
            var position = snapshot.Rows.Count == 0 ? 0 : checked(snapshot.Rows.Max(row => row.Position) + 1);
            await _api.CreateAsync(owner, draft, position, caps.WebOnly, owner.Token, snapshot.Scope).ConfigureAwait(false);
        }
        else
        {
            if (!snapshot.Scope.Matches(session.Row)) throw new AdminHomeSectionsConflictException();
            await _api.SaveAsync(owner, session, draft, caps.WebOnly, owner.Token).ConfigureAwait(false);
        }
    });

    public Task<bool> QuickAsync(AdminHomeSection displayed, bool? shown = null, bool? hero = null) => WriteAsync(async (owner, _, caps) =>
    {
        var session = await _api.GetAsync(owner, displayed.Id, owner.Token, new(displayed.Scope, displayed.LibraryId)).ConfigureAwait(false);
        if (Fingerprint(session.Row) != Fingerprint(displayed)) throw new AdminHomeSectionsConflictException();
        var draft = AdminHomeSectionDraft.From(session.Row);
        if (shown != null) draft.Enabled = shown.Value;
        if (hero != null) { draft.Featured = hero.Value; if (!hero.Value) draft.WebOnly = false; }
        await _api.SaveAsync(owner, session, draft, caps.WebOnly, owner.Token).ConfigureAwait(false);
    });

    public Task<bool> ReorderAsync(AdminHomeSectionsSnapshot reviewed, IReadOnlyList<string> ids) => WriteAsync(async (owner, snapshot, _) =>
    {
        if (!ReferenceEquals(reviewed, snapshot)) throw new AdminHomeSectionsConflictException();
        await _api.ReorderAsync(owner, reviewed, ids, owner.Token).ConfigureAwait(false);
    });

    public Task<bool> DeleteAsync(IReadOnlyList<AdminHomeSectionSession> reviewed) => WriteAsync(async (owner, _, _) =>
    {
        if (reviewed.Count is < 1 or > 100 || reviewed.Select(row => row.Row.Id).Distinct().Count() != reviewed.Count)
            throw new ArgumentException("Select up to 100 rows.");
        foreach (var session in reviewed) await _api.DeleteAsync(owner, session, owner.Token).ConfigureAwait(false);
    });

    public Task<bool> CopyAsync(AdminHomeSectionSession session, IReadOnlyList<string> targetIds) => WriteAsync(async (owner, snapshot, _) =>
    {
        if (!ReferenceEquals(session.Owner, owner) || !snapshot.Scope.Matches(session.Row)) throw new MonitorAuthorityRetiredException();
        var fresh = await _api.GetAsync(owner, session.Row.Id, owner.Token, snapshot.Scope).ConfigureAwait(false);
        if (fresh.ETag != session.ETag) throw new AdminHomeSectionsConflictException();
        await _api.BulkCopyAsync(owner, AdminHomeSectionDraft.From(fresh.Row), targetIds, owner.Token).ConfigureAwait(false);
    });

    public Task<bool> RestoreAsync(AdminHomeSectionsSnapshot reviewed, bool resetProfiles, IReadOnlyList<AdminHomeSectionScope>? alsoPages = null) => WriteAsync(async (owner, snapshot, caps) =>
    {
        if (!ReferenceEquals(snapshot, reviewed)) throw new AdminHomeSectionsConflictException();
        if (resetProfiles && !caps.ResetProfiles) throw new ArgumentException("Profile reset is unavailable.");
        if (alsoPages is { Count: > 2000 } || alsoPages?.Any(page => page.Kind != "library") == true) throw new ArgumentException("Invalid library restore targets.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
        await _api.RestoreAsync(owner, reviewed, resetProfiles, deadline.Token).ConfigureAwait(false);
        if (alsoPages != null)
        {
            if (alsoPages.Count > 2000) throw new ArgumentException("Too many library pages.");
            foreach (var page in alsoPages.Distinct().Where(page => page != snapshot.Scope))
            {
                var other = await _api.ReadAsync(owner, deadline.Token, page).ConfigureAwait(false);
                await _api.RestoreAsync(owner, other, resetProfiles, deadline.Token).ConfigureAwait(false);
            }
        }
    });

    public async Task<IReadOnlyList<AdminHomeSectionSession>> PrepareDeletesAsync(IReadOnlyList<string> ids)
    {
        if (ids.Count is < 1 or > 100) throw new ArgumentException("Select up to 100 rows.");
        var list = new List<AdminHomeSectionSession>();
        foreach (var id in ids)
        {
            var session = await OpenEditAsync(id).ConfigureAwait(false);
            if (session == null) throw new MonitorAuthorityRetiredException(); list.Add(session);
        }
        return list;
    }

    public Task<bool> SetShownManyAsync(IReadOnlyList<AdminHomeSectionSession> sessions, bool shown) => WriteAsync(async (owner, snapshot, caps) =>
    {
        if (sessions.Count is < 1 or > 100) throw new ArgumentException("Select up to 100 rows.");
        if (sessions.Any(session => snapshot.Rows.FirstOrDefault(row => row.Id == session.Row.Id) is not { } row || Fingerprint(row) != Fingerprint(session.Row)))
            throw new AdminHomeSectionsConflictException();
        foreach (var session in sessions)
        {
            var draft = AdminHomeSectionDraft.From(session.Row); draft.Enabled = shown;
            await _api.SaveAsync(owner, session, draft, caps.WebOnly, owner.Token).ConfigureAwait(false);
        }
    });

    private async Task<bool> WriteAsync(Func<MonitorAuthorityLease, AdminHomeSectionsSnapshot, AdminHomeSectionCapabilities, Task> action)
    {
        MonitorAuthorityLease owner; AdminHomeSectionsSnapshot snapshot; AdminHomeSectionCapabilities caps; long revision;
        lock (_gate)
        {
            if (!Writable()) return false;
            owner = _state.Owner!; snapshot = _state.Snapshot!; caps = _state.Capabilities!;
            revision = ++_revision; _state = _state with { Busy = true, Error = null };
        }
        Changed?.Invoke();
        try
        {
            await action(owner, snapshot, caps).ConfigureAwait(false);
            var fresh = await _api.ReadAsync(owner, owner.Token, snapshot.Scope).ConfigureAwait(false);
            if (!Update(owner, revision, () => _state = _state with { Busy = false, Snapshot = fresh })) return false;
            Saved?.Invoke(); return true;
        }
        catch (Exception ex)
        {
            // Stop even a partially completed batch. A readback failure after a successful write is also uncertain.
            var unknown = ex is not (AdminHomeSectionsConflictException or ArgumentException) &&
                (ex is not ApiException httpError || httpError.StatusCode >= 500 || httpError.StatusCode == 408);
            Update(owner, revision, () => _state = _state with
            {
                Busy = false, ReviewRequired = true, OutcomeUnknown = unknown,
                Error = ex is AdminHomeSectionsConflictException ? ex.Message : ex is ArgumentException ? ex.Message :
                    ex is ApiException api && api.StatusCode is >= 400 and < 500 ? ReadError(ex) : new AdminHomeSectionsUnknownException().Message
            });
            return false;
        }
    }

    public static string Fingerprint(AdminHomeSection row) => JsonSerializer.Serialize(new
    { row.Id, row.Scope, row.LibraryId, row.Title, row.SectionType, row.Enabled, row.Featured, row.WebOnly, row.ItemLimit, row.Config });
    private static string ReadError(Exception ex) => ex is ApiException api ? api.StatusCode switch
    {
        401 => "Sign in again to manage Home Sections.", 403 => "Administrator permission is required.",
        404 => "The row is no longer available. Reload rows.", _ => "Unable to load or change Home rows. Reload and try again."
    } : ex is InvalidDataException ? "The server returned incomplete or inconsistent rows. Reload rows." : "Unable to load Home rows. Reload rows.";
    private bool Current(MonitorAuthorityLease owner, long revision) => !_disposed && owner.IsCurrent && ReferenceEquals(owner, _state.Owner) && revision == _revision;
    private bool Update(MonitorAuthorityLease owner, long revision, Action update)
    { lock (_gate) { if (!Current(owner, revision)) return false; update(); } Changed?.Invoke(); return true; }
    private void Clear() { lock (_gate) { _revision++; _state = new(); } Changed?.Invoke(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { lock (_gate) _disposed = true; _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
