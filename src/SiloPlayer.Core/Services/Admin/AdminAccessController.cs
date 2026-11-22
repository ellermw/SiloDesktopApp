using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One page lifetime, one in-flight operation, no queued writes or automatic replay.</summary>
public abstract class AdminAccessController : IDisposable
{
    /// <summary>Hard bound on accounts read for permission and break-glass decisions (the official page walks every page; this client stops here and treats a longer list as not fully known).</summary>
    public const int AccountCap = 1000;
    /// <summary>Hard bound on account pages requested by one walk (the cap in pages of 50): pages that only repeat accounts already read still end it, with the list not fully known.</summary>
    public const int AccountPageCap = AccountCap / 50;
    protected readonly object Gate = new();
    private readonly AdminAuthorityLifetime _lifetime;
    private bool _busy, _disposed;
    public event Action? Changed;
    public MonitorAuthorityLease? Owner => _lifetime.Owner;
    public bool Busy { get { lock (Gate) return _busy; } }
    public string? Message { get; protected set; }
    /// <summary>The reviewed resource may be stale; reading that one resource again clears it.</summary>
    public bool ReviewRequired { get; protected set; }
    /// <summary>
    /// The canonical list could not be confirmed after a create whose outcome is unknown, or whose follow-up list read failed.
    /// No create or row write is possible until the page-level reload has read the list again; opening, cancelling or saving a
    /// single-row editor never clears it.
    /// </summary>
    public bool ListReviewRequired { get; protected set; }
    public bool WritesBlocked => ReviewRequired || ListReviewRequired;
    public bool Current => Owner is { IsCurrent: true };
    protected AdminAccessController(AdminMonitorAuthority authority) { _lifetime = new(authority); _lifetime.Retired += Clear; }
    protected void Notify() => Changed?.Invoke();
    protected void Publish(MonitorAuthorityLease owner, Action action)
    {
        lock (Gate) { if (_disposed || !ReferenceEquals(Owner, owner) || !owner.IsCurrent) return; action(); }
        Notify();
    }
    protected async Task OpenPageAsync(Func<MonitorAuthorityLease, Task> load)
    {
        if (Busy || _disposed) return;
        _lifetime.Retire(); Clear(); _lifetime.Resume();
        try { _lifetime.Open(); } catch (OperationCanceledException) { Notify(); return; }
        await RunAsync(load, reconcile: true).ConfigureAwait(false);
    }
    /// <summary>Reads account pages from <paramref name="cursor"/> until the list ends or a hard cap (accounts or pages) is reached; a remaining cursor means the list is not fully known. A cursor the server repeats is an invalid list.</summary>
    protected static async Task<(IReadOnlyList<AdminAccessAccount> Items, string? Cursor)> ReadAccountPagesAsync(
        Func<string?, Task<AdminAccessPage<AdminAccessAccount>>> read, IReadOnlyList<AdminAccessAccount> seed, string? cursor, bool first)
    {
        var items = seed.ToList(); var cursors = new HashSet<string>(StringComparer.Ordinal);
        if (!first && cursor != null) cursors.Add(cursor);
        for (var pages = 0; pages < AccountPageCap && (first || cursor != null && items.Count < AccountCap); pages++)
        {
            var page = await read(first ? null : cursor).ConfigureAwait(false); first = false;
            foreach (var account in page.Items) if (items.All(a => a.Id != account.Id)) items.Add(account);
            cursor = page.Page!.NextCursor;
            // Accounts already read are skipped, so a cursor cycle (A, B, A...) never reaches the account cap: a cursor used before ends the walk as an invalid list.
            if (cursor != null && !cursors.Add(cursor)) throw new AdminAccessException("invalid_response");
        }
        return (items, cursor);
    }
    /// <param name="listWrite">The write creates a record: an unknown outcome leaves the canonical list stale until the page-level reload.</param>
    protected async Task RunAsync(Func<MonitorAuthorityLease, Task> work, bool write = false, bool reconcile = false, bool listWrite = false)
    {
        MonitorAuthorityLease owner;
        lock (Gate)
        {
            if (_disposed || _busy || Owner is not { IsCurrent: true } current || write && WritesBlocked) return;
            owner = current; _busy = true; Message = null;
        }
        Notify();
        try
        {
            owner.ThrowIfRetired(); await work(owner).ConfigureAwait(false); owner.ThrowIfRetired();
            if (reconcile) Publish(owner, () => ReviewRequired = false);
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Publish(owner, () =>
            {
                var review = write && RequiresReview(error);
                var message = error switch
                {
                    AdminAccessException safe => safe.Message,
                    AdminSettingsConflictException => "Settings changed on the server. Reload and review your changes before saving again.",
                    AdminSettingsOutcomeUnknownException => "The save outcome is unknown. Reload and review before saving again. This request was not resent.",
                    ApiException api => new AdminAccessException(api.ProblemType ?? api.ErrorCode, api.StatusCode).Message,
                    ArgumentException => "Review the entered values before continuing.",
                    _ => "The request could not be completed. Reload and review before continuing.",
                };
                Message = ReviewMessage(error, message, review);
                if (review) ReviewRequired = true;
                // A read that failed before or after nothing could have been created (a definite answer to a GET) does not make the list stale; anything else might have.
                if (write && listWrite && error is not ArgumentException && error is not AdminAccessException { Uncertain: false }) ListReviewRequired = true;
            });
        }
        finally { Publish(owner, () => _busy = false); }
    }
    /// <summary>
    /// Whether the failure of a write leaves the page's picture of the server in doubt, so that the reviewed resource must be read again before another write.
    /// The default: a rejected local value never reached the server, and a definite refusal changed nothing, so neither leaves anything to review; every other failure does.
    /// A page that can tell more precisely (which refusals leave nothing applied and the picture valid) overrides this for itself; other pages are unchanged.
    /// Called under the controller's lock.
    /// </summary>
    protected virtual bool RequiresReview(Exception error) => error is not ArgumentException && error is not AdminAccessException { Refusal: true };
    /// <summary>The text shown for a failed operation. The default is <paramref name="message"/> unchanged; a page may say why a reload is needed when <paramref name="review"/> is set. Called under the controller's lock.</summary>
    protected virtual string ReviewMessage(Exception error, string message, bool review) => message;
    protected abstract void Reset();
    private void Clear() { lock (Gate) { Reset(); _busy = false; Message = null; ReviewRequired = ListReviewRequired = false; } Notify(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { if (_disposed) return; Suspend(); _disposed = true; _lifetime.Dispose(); Changed = null; }
}
