using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>What the last write left behind. Only <see cref="Conflict"/> and <see cref="Unknown"/> block further writes.</summary>
public enum AdminHomeSectionsFailure
{
    None,
    /// <summary>A definite refusal (4xx): nothing changed, the admin corrects and retries.</summary>
    Refused,
    /// <summary>The row or page moved on (412/404, or it no longer matches what was shown). Reload and review.</summary>
    Conflict,
    /// <summary>No usable answer after sending (timeout, transport, 5xx, malformed). The server state must be re-read.</summary>
    Unknown,
    /// <summary>A read failed.</summary>
    Load
}

public sealed record AdminHomeSectionsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public AdminHomeSectionsSnapshot? Snapshot { get; init; }
    public AdminHomeSectionCapabilities? Capabilities { get; init; }
    public AdminHomeSectionCatalog? Catalog { get; init; }
    public bool CatalogFailed { get; init; }
    public bool Busy { get; init; }
    public bool ReviewRequired { get; init; }
    public bool OutcomeUnknown { get; init; }
    public AdminHomeSectionsFailure Failure { get; init; }
    public string? Error { get; init; }
    /// <summary>Rows the last successful create added (new ids after the refresh), for highlighting and link follow-ups.</summary>
    public IReadOnlyList<string> NewIds { get; init; } = [];
}

public enum AdminHomeSectionsRowOutcome { Applied, Skipped, Changed, Failed, Unknown, NotAttempted }
public sealed record AdminHomeSectionsRowResult(string Id, string Title, AdminHomeSectionsRowOutcome Outcome, string? Message = null);
public sealed record AdminHomeSectionsBatchResult(IReadOnlyList<AdminHomeSectionsRowResult> Rows, bool Completed)
{
    public IReadOnlyList<string> ChangedIds => Rows.Where(row => row.Outcome == AdminHomeSectionsRowOutcome.Applied).Select(row => row.Id).ToArray();
    public IReadOnlyList<AdminHomeSectionsRowResult> Failures => Rows.Where(row => row.Outcome is AdminHomeSectionsRowOutcome.Changed or AdminHomeSectionsRowOutcome.Failed or AdminHomeSectionsRowOutcome.Unknown or AdminHomeSectionsRowOutcome.NotAttempted).ToArray();
}
public sealed record AdminHomeSectionsDeletePlan(IReadOnlyList<AdminHomeSectionSession> Sessions, IReadOnlyList<string> AlreadyGone);
public sealed record AdminHomeSectionsDeleteResult(IReadOnlyList<string> DeletedIds, IReadOnlyList<string> FailedIds, string? FirstError, bool Conflict, bool Unknown)
{
    public int Deleted => DeletedIds.Count;
}
public sealed record AdminHomeSectionsPageFailure(string LibraryId, bool Unknown, string Message);
public sealed record AdminHomeSectionsRestoreResult(bool PrimaryRestored, IReadOnlyList<AdminHomeSectionsPageFailure> Failures);

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
            owner = active; revision = ++_revision; _state = _state with { Busy = true, Error = null, Failure = AdminHomeSectionsFailure.None, NewIds = [] };
        }
        Changed?.Invoke();
        try
        {
            var caps = await _api.CapabilitiesAsync(owner, owner.Token).ConfigureAwait(false);
            var snapshot = await _api.ReadAsync(owner, owner.Token, _scope).ConfigureAwait(false);
            // Catalog failure must not hide existing rows or prevent deleting a retired kind.
            AdminHomeSectionCatalog? catalog = null; var catalogFailed = false;
            try { catalog = await _api.CatalogAsync(owner, owner.Token).ConfigureAwait(false); }
            catch (Exception) when (owner.IsCurrent) { catalogFailed = true; }
            Update(owner, revision, () => _state = new() { Owner = owner, Snapshot = snapshot, Capabilities = caps, Catalog = catalog, CatalogFailed = catalogFailed });
        }
        catch (Exception ex) { Update(owner, revision, () => _state = _state with { Busy = false, Snapshot = null, Failure = AdminHomeSectionsFailure.Load, Error = ReadError(ex) }); }
    }

    /// <summary>Reads the row catalog again without touching the page's rows.</summary>
    public async Task ReloadCatalogAsync()
    {
        MonitorAuthorityLease owner; long revision;
        lock (_gate)
        {
            if (_disposed || _state.Busy || _state.Owner is not { IsCurrent: true } active) return;
            owner = active; revision = _revision;
        }
        try
        {
            var catalog = await _api.CatalogAsync(owner, owner.Token).ConfigureAwait(false);
            Update(owner, revision, () => _state = _state with { Catalog = catalog, CatalogFailed = false });
        }
        catch (Exception) when (owner.IsCurrent) { Update(owner, revision, () => _state = _state with { CatalogFailed = true }); }
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

    /// <summary>Adds a row at the bottom of the page (or one bulk create when copies are chosen), or saves over the reviewed row version.</summary>
    public Task<bool> SaveAsync(AdminHomeSectionSession? session, AdminHomeSectionDraft draft, IReadOnlyList<string>? extraLibraries = null)
    {
        var bulk = false;
        return WriteAsync(async (owner, snapshot, caps) =>
    {
        if (session == null)
        {
            if (extraLibraries is { Count: > 0 } && snapshot.Scope.LibraryId != null)
            {
                if (draft.Featured) throw new ArgumentException("New hero rows stay on their current page.");
                bulk = true;
                await _api.BulkCopyAsync(owner, draft, new[] { snapshot.Scope.LibraryId }.Concat(extraLibraries).Distinct().ToArray(), owner.Token).ConfigureAwait(false);
                return;
            }
            var position = snapshot.Rows.Count == 0 ? 0 : checked(snapshot.Rows.Max(row => row.Position) + 1);
            await _api.CreateAsync(owner, draft, position, caps.WebOnly, owner.Token, snapshot.Scope).ConfigureAwait(false);
        }
        else
        {
            if (!snapshot.Scope.Matches(session.Row)) throw new AdminHomeSectionsConflictException();
            await _api.SaveAsync(owner, session, draft, caps.WebOnly, owner.Token).ConfigureAwait(false);
        }
        }, captureNewIds: true, hint: () => bulk ? OtherPagesHint : null);
    }
    private const string OtherPagesHint = "Check the other library pages too: the rows may have been added there.";

    /// <summary>Turns a row on or off, or makes it the hero banner or not. Sends only that field, over the version just read.</summary>
    public Task<bool> QuickAsync(AdminHomeSection displayed, bool? shown = null, bool? hero = null) => WriteAsync(async (owner, snapshot, _) =>
    {
        if (!snapshot.Scope.Matches(displayed) || snapshot.Rows.All(row => row.Id != displayed.Id)) throw new AdminHomeSectionsConflictException();
        var session = await _api.GetAsync(owner, displayed.Id, owner.Token, snapshot.Scope).ConfigureAwait(false);
        if (Fingerprint(session.Row) != Fingerprint(displayed)) throw new AdminHomeSectionsConflictException();
        await _api.QuickPatchAsync(owner, session, shown, hero, owner.Token).ConfigureAwait(false);
    });

    public Task<bool> ReorderAsync(AdminHomeSectionsSnapshot reviewed, IReadOnlyList<string> ids) => WriteAsync(async (owner, snapshot, _) =>
    {
        if (!ReferenceEquals(reviewed, snapshot)) throw new AdminHomeSectionsConflictException();
        await _api.ReorderAsync(owner, reviewed, ids, owner.Token).ConfigureAwait(false);
    });

    /// <summary>Deletes the reviewed rows one at a time, each over the version read before confirmation. A refused or stale row is left and reported; an unknown outcome stops the run.</summary>
    public async Task<AdminHomeSectionsDeleteResult> DeleteAsync(AdminHomeSectionsDeletePlan plan, Action<int, int>? progress = null)
    {
        var sessions = plan.Sessions;
        if (sessions.Count + plan.AlreadyGone.Count is < 1 or > 100 || sessions.Select(row => row.Row.Id).Distinct().Count() != sessions.Count)
            throw new ArgumentException("Select up to 100 rows.");
        var deleted = new List<string>(plan.AlreadyGone); var failed = new List<string>(); string? first = null; var conflict = false; var unknown = false;
        var ok = await WriteAsync(async (owner, _, _) =>
        {
            var processed = plan.AlreadyGone.Count; var total = sessions.Count + plan.AlreadyGone.Count;
            foreach (var session in sessions)
            {
                progress?.Invoke(processed, total);
                processed++;
                if (unknown) { failed.Add(session.Row.Id); continue; }
                try { await _api.DeleteAsync(owner, session, owner.Token).ConfigureAwait(false); deleted.Add(session.Row.Id); }
                catch (ApiException ex) when (ex.StatusCode == 404) { deleted.Add(session.Row.Id); }
                catch (AdminHomeSectionsConflictException) { failed.Add(session.Row.Id); conflict = true; first ??= "These sections changed. Reload and review your changes before trying again."; }
                catch (ApiException ex) when (ex.StatusCode is >= 400 and < 500 && ex.StatusCode != 408) { failed.Add(session.Row.Id); first ??= RefusalText(ex); }
                catch (Exception ex) when (ex is not OperationCanceledException || owner.IsCurrent) { failed.Add(session.Row.Id); unknown = true; first ??= new AdminHomeSectionsUnknownException().Message; }
            }
            if (unknown) throw new AdminHomeSectionsUnknownException();
        }, partialSuccess: () => deleted.Count > plan.AlreadyGone.Count).ConfigureAwait(false);
        if (!ok && !unknown && failed.Count == 0) failed.AddRange(sessions.Select(row => row.Row.Id).Where(id => !deleted.Contains(id)));
        return new(deleted, failed, first ?? (ok ? null : State.Error), conflict, unknown);
    }

    /// <summary>Copies an existing row to other library pages from the row the server holds, when it is still the row that was shown.</summary>
    public Task<bool> CopyAsync(AdminHomeSectionSession session, IReadOnlyList<string> targetIds) => WriteAsync(async (owner, snapshot, _) =>
    {
        if (!ReferenceEquals(session.Owner, owner) || !snapshot.Scope.Matches(session.Row)) throw new MonitorAuthorityRetiredException();
        var fresh = await _api.GetAsync(owner, session.Row.Id, owner.Token, snapshot.Scope).ConfigureAwait(false);
        if (fresh.ETag != session.ETag) throw new AdminHomeSectionsConflictException();
        var draft = AdminHomeSectionDraft.From(fresh.Row);
        if (!draft.CanCopy) throw new ArgumentException("This row can't be added to other libraries.");
        await _api.BulkCopyAsync(owner, draft, targetIds, owner.Token).ConfigureAwait(false);
    }, hint: () => "Check the other library pages too: the copies may have been added there.");

    /// <summary>Restores this page over the reviewed version, then each other library page over the version it has when its turn comes.</summary>
    public async Task<AdminHomeSectionsRestoreResult> RestoreAsync(AdminHomeSectionsSnapshot reviewed, bool resetProfiles, IReadOnlyList<AdminHomeSectionScope>? alsoPages = null)
    {
        var failures = new List<AdminHomeSectionsPageFailure>();
        var primary = false;
        await WriteAsync(async (owner, snapshot, caps) =>
        {
            if (!ReferenceEquals(snapshot, reviewed)) throw new AdminHomeSectionsConflictException();
            if (resetProfiles && !caps.ResetProfiles) throw new ArgumentException("Profile reset is unavailable.");
            if (alsoPages is { Count: > 2000 } || alsoPages?.Any(page => page.Kind != "library") == true) throw new ArgumentException("Invalid library restore targets.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); deadline.CancelAfter(TimeSpan.FromSeconds(90));
            await _api.RestoreAsync(owner, reviewed, resetProfiles, deadline.Token).ConfigureAwait(false);
            primary = true;
            var stopped = false;
            foreach (var page in (alsoPages ?? []).Distinct().Where(page => page != snapshot.Scope))
            {
                var id = page.LibraryId ?? "";
                if (stopped) { failures.Add(new(id, false, "Not restored: an earlier page could not be confirmed.")); continue; }
                string etag;
                try { etag = (await _api.ReadOrderAsync(owner, page, deadline.Token).ConfigureAwait(false)).ETag!; }
                catch (Exception ex) when (owner.IsCurrent && ex is not OperationCanceledException || ex is OperationCanceledException && owner.IsCurrent && !deadline.IsCancellationRequested)
                { failures.Add(new(id, false, ex is ApiException api ? RefusalText(api) : "Its version could not be read.")); continue; }
                try { await _api.RestorePageAsync(owner, page, etag, resetProfiles, deadline.Token).ConfigureAwait(false); }
                catch (AdminHomeSectionsConflictException) { failures.Add(new(id, false, "It changed while restoring.")); }
                catch (ApiException ex) when (ex.StatusCode is >= 400 and < 500 && ex.StatusCode != 408) { failures.Add(new(id, false, RefusalText(ex))); }
                catch (Exception ex) when (owner.IsCurrent || ex is not OperationCanceledException) { failures.Add(new(id, true, "")); stopped = true; }
            }
            if (stopped) throw new AdminHomeSectionsUnknownException();
        }, partialSuccess: () => primary, hint: () => "Check the library pages too: some may have been restored.").ConfigureAwait(false);
        return new(primary, failures);
    }

    public async Task<AdminHomeSectionsDeletePlan> PrepareDeletesAsync(IReadOnlyList<string> ids)
    {
        if (ids.Count is < 1 or > 100) throw new ArgumentException("Select up to 100 rows.");
        var list = new List<AdminHomeSectionSession>(); var gone = new List<string>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var session = await OpenEditAsync(id).ConfigureAwait(false);
                if (session == null) throw new MonitorAuthorityRetiredException(); list.Add(session);
            }
            catch (ApiException ex) when (ex.StatusCode == 404) { gone.Add(id); }
        }
        return new(list, gone);
    }

    /// <summary>
    /// Turns the selected rows on or off. Each row is read and checked against the page first, then written over the version just read.
    /// A refused or stale row is left as it was and reported; an unknown outcome stops the run and locks until the page is reloaded.
    /// </summary>
    public async Task<AdminHomeSectionsBatchResult?> SetShownManyAsync(IReadOnlyList<string> ids, bool shown)
    {
        if (ids.Count is < 1 or > 100) throw new ArgumentException("Select up to 100 rows.");
        var results = new List<AdminHomeSectionsRowResult>();
        var ok = await WriteAsync(async (owner, snapshot, _) =>
        {
            var stopped = false;
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                var onScreen = snapshot.Rows.FirstOrDefault(row => row.Id == id);
                var title = onScreen?.Title ?? id;
                if (stopped) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.NotAttempted)); continue; }
                if (onScreen == null) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Changed)); continue; }
                try
                {
                    var session = await _api.GetAsync(owner, id, owner.Token, snapshot.Scope).ConfigureAwait(false);
                    if (Fingerprint(session.Row) != Fingerprint(onScreen)) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Changed)); continue; }
                    if (onScreen.Enabled == shown) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Skipped)); continue; }
                    await _api.QuickPatchAsync(owner, session, shown, null, owner.Token).ConfigureAwait(false);
                    results.Add(new(id, title, AdminHomeSectionsRowOutcome.Applied));
                }
                catch (AdminHomeSectionsConflictException) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Changed)); }
                catch (ApiException ex) when (ex.StatusCode is 404 or 412) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Changed)); }
                catch (ApiException ex) when (ex.StatusCode is >= 400 and < 500 && ex.StatusCode != 408) { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Failed, RefusalText(ex))); }
                catch (Exception ex) when (ex is not OperationCanceledException || owner.IsCurrent)
                { results.Add(new(id, title, AdminHomeSectionsRowOutcome.Unknown, new AdminHomeSectionsUnknownException().Message)); stopped = true; }
            }
            if (stopped) throw new AdminHomeSectionsUnknownException();
        }, partialSuccess: () => results.Any(row => row.Outcome == AdminHomeSectionsRowOutcome.Applied)).ConfigureAwait(false);
        return results.Count == 0 && !ok ? null : new(results, ok);
    }

    /// <summary>
    /// Runs one guarded write and reads the page again. A write that succeeded but cannot be confirmed by the readback is unknown, never a refusal.
    /// <paramref name="partialSuccess"/> says whether an action that threw had already changed something; <paramref name="hint"/> adds
    /// where else to look after an unknown outcome (a multi-page write).
    /// </summary>
    private async Task<bool> WriteAsync(Func<MonitorAuthorityLease, AdminHomeSectionsSnapshot, AdminHomeSectionCapabilities, Task> action,
        bool captureNewIds = false, Func<bool>? partialSuccess = null, Func<string?>? hint = null)
    {
        MonitorAuthorityLease owner; AdminHomeSectionsSnapshot snapshot; AdminHomeSectionCapabilities caps; long revision;
        lock (_gate)
        {
            if (!Writable()) return false;
            owner = _state.Owner!; snapshot = _state.Snapshot!; caps = _state.Capabilities!;
            revision = ++_revision; _state = _state with { Busy = true, Error = null, Failure = AdminHomeSectionsFailure.None, NewIds = [] };
        }
        Changed?.Invoke();
        Exception? failure = null;
        try { await action(owner, snapshot, caps).ConfigureAwait(false); }
        catch (Exception ex) { failure = ex; }
        if (!owner.IsCurrent) { Update(owner, revision, () => { }); return false; }
        var kind = failure == null ? AdminHomeSectionsFailure.None : Classify(failure);
        var wrote = failure == null || partialSuccess?.Invoke() == true;
        AdminHomeSectionsSnapshot? fresh = null; var readFailed = false;
        if (wrote || kind == AdminHomeSectionsFailure.Unknown)
        {
            try { fresh = await _api.ReadAsync(owner, owner.Token, snapshot.Scope).ConfigureAwait(false); }
            catch (Exception) when (owner.IsCurrent) { readFailed = true; }
            catch (Exception) { return false; }
        }
        if (kind == AdminHomeSectionsFailure.None && !readFailed && fresh != null)
        {
            var added = captureNewIds ? fresh.Rows.Select(row => row.Id).Except(snapshot.Rows.Select(row => row.Id)).ToArray() : [];
            if (!Update(owner, revision, () => _state = _state with { Busy = false, Snapshot = fresh, NewIds = added })) return false;
            Saved?.Invoke(); return true;
        }
        // The write may have changed something even though it failed or could not be confirmed.
        if (readFailed && kind is AdminHomeSectionsFailure.None or AdminHomeSectionsFailure.Refused) kind = AdminHomeSectionsFailure.Unknown;
        var message = kind == AdminHomeSectionsFailure.Unknown
            ? new AdminHomeSectionsUnknownException().Message + (hint?.Invoke() is { } extra ? " " + extra : "")
            : MessageFor(failure!, kind);
        Update(owner, revision, () => _state = _state with
        {
            Busy = false, Failure = kind, Error = message,
            ReviewRequired = kind is AdminHomeSectionsFailure.Conflict or AdminHomeSectionsFailure.Unknown, OutcomeUnknown = kind == AdminHomeSectionsFailure.Unknown,
            // A partly applied run with a good readback shows the rows as they are now; the lock still asks for review.
            Snapshot = fresh ?? _state.Snapshot
        });
        if (wrote) Saved?.Invoke();
        return false;
    }

    private static AdminHomeSectionsFailure Classify(Exception ex) => ex switch
    {
        AdminHomeSectionsConflictException => AdminHomeSectionsFailure.Conflict,
        ApiException { StatusCode: 412 or 404 } => AdminHomeSectionsFailure.Conflict,
        ArgumentException => AdminHomeSectionsFailure.Refused,
        ApiException { StatusCode: >= 400 and < 500 } api when api.StatusCode != 408 => AdminHomeSectionsFailure.Refused,
        _ => AdminHomeSectionsFailure.Unknown
    };

    private static string MessageFor(Exception ex, AdminHomeSectionsFailure kind) => ex switch
    {
        AdminHomeSectionsConflictException conflict => conflict.Message,
        ApiException { StatusCode: 404 } => "The row is no longer available. Reload rows.",
        ApiException { StatusCode: 412 } => new AdminHomeSectionsConflictException().Message,
        ArgumentException argument => argument.Message,
        ApiException api => RefusalText(api),
        _ => kind == AdminHomeSectionsFailure.Unknown ? new AdminHomeSectionsUnknownException().Message : ex.Message
    };

    /// <summary>The server's own problem text and field errors, shown so the admin can correct and retry.</summary>
    public static string RefusalText(ApiException ex)
    {
        if (ex.StatusCode == 401) return "Sign in again to manage Home Sections.";
        if (ex.StatusCode == 403) return "Administrator permission is required.";
        var text = ApiProblemText.Display(ex);
        var fields = ex.FieldErrors.Select(field => ApiProblemText.FieldDetail(field)).Where(detail => !string.IsNullOrWhiteSpace(detail)).Distinct().ToArray();
        return fields.Length == 0 || fields.All(detail => text.Contains(detail, StringComparison.Ordinal)) ? text : text + " " + string.Join(" ", fields);
    }

    public static string Fingerprint(AdminHomeSection row) => JsonSerializer.Serialize(new
    { row.Id, row.Scope, row.LibraryId, row.Title, row.SectionType, row.Enabled, row.Featured, row.WebOnly, row.ItemLimit, row.Config });
    private static string ReadError(Exception ex) => ex switch
    {
        ApiException { StatusCode: 401 } => "Sign in again to manage Home Sections.",
        ApiException { StatusCode: 403 } => "Administrator permission is required.",
        ApiException api when !string.IsNullOrWhiteSpace(ApiProblemText.Display(api)) => ApiProblemText.Display(api),
        InvalidDataException => AdminHomeSectionsText.T("web.common.catalog.sections.changedWhileLoading"),
        _ => AdminHomeSectionsText.T("web.admin.homeRows.page.loadRowsFailed")
    };
    private bool Current(MonitorAuthorityLease owner, long revision) => !_disposed && owner.IsCurrent && ReferenceEquals(owner, _state.Owner) && revision == _revision;
    private bool Update(MonitorAuthorityLease owner, long revision, Action update)
    { lock (_gate) { if (!Current(owner, revision)) return false; update(); } Changed?.Invoke(); return true; }
    private void Clear() { lock (_gate) { _revision++; _state = new(); } Changed?.Invoke(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { lock (_gate) _disposed = true; _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
