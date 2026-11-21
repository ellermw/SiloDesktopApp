using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed class AdminApiKeysController(AdminApiKeysApi api, AdminMonitorAuthority authority) : AdminAccessController(authority)
{
    public AdminApiKeyCapabilities? Capabilities { get; private set; }
    public IReadOnlyList<AdminApiKey> Rows { get; private set; } = [];
    public IReadOnlyList<AdminAccessAccount> Accounts { get; private set; } = [];
    public string? NextCursor { get; private set; }
    public string? AccountCursor { get; private set; }
    public AdminApiKeyEditor? Editor { get; private set; }
    public bool Loaded { get; private set; }
    public bool AccountsLoaded { get; private set; }
    public IReadOnlyDictionary<string, string> RateHints { get; private set; } = new Dictionary<string, string>();
    public bool ViewerIsOwner => Accounts.Any(a => a.Id == Owner?.AccountId && a.IsOwner);
    /// <summary>Like the official page: nobody is offered actions until the accounts are read, and an owner not in the read list is offered unless known to be unmanageable (the server and a fresh read still decide every write).</summary>
    public bool CanManage(string id) => Current && AccountsLoaded && (Accounts.FirstOrDefault(a => a.Id == id) is not { } account || account.CanManage(Owner!.AccountId, ViewerIsOwner));
    protected override void Reset() { Capabilities = null; Rows = []; Accounts = []; NextCursor = AccountCursor = null; Editor = null; Loaded = AccountsLoaded = false; RateHints = new Dictionary<string, string>(); }
    public Task OpenAsync() => OpenPageAsync(async owner =>
    {
        var caps = await api.CapabilitiesAsync(owner).ConfigureAwait(false); Publish(owner, () => Capabilities = caps);
        if (!caps.Available) return;
        var rows = await api.ListAsync(owner).ConfigureAwait(false);
        Publish(owner, () => { Rows = rows.Items; NextCursor = rows.Page!.NextCursor; Loaded = true; });
        // Read the viewer explicitly: it need not be in the first account page. Then walk the remaining pages (hard cap) so
        // ownership and manageability do not depend on which page an account happens to be on.
        var viewer = await api.GetAccountAsync(owner, owner.AccountId).ConfigureAwait(false);
        var (accounts, cursor) = await ReadAccountPagesAsync(c => api.AccountsAsync(owner, c), [viewer], null, first: true).ConfigureAwait(false);
        Publish(owner, () => { Accounts = accounts; AccountCursor = cursor; AccountsLoaded = true; });
    });
    public Task MoreAsync() => RunAsync(async owner =>
    {
        var cursor = NextCursor; if (cursor == null) return;
        if (Rows.Count >= 1000) throw new AdminAccessException("list_limit");
        var page = await api.ListAsync(owner, cursor).ConfigureAwait(false);
        Publish(owner, () => { Rows = Rows.Concat(page.Items).DistinctBy(r => r.Id).Take(1000).ToArray(); NextCursor = page.Page!.NextCursor; });
    });
    public Task MoreAccountsAsync() => RunAsync(async owner =>
    {
        var start = AccountCursor; if (start == null || Accounts.Count >= AccountCap) return;
        var (accounts, cursor) = await ReadAccountPagesAsync(c => api.AccountsAsync(owner, c), Accounts, start, first: false).ConfigureAwait(false);
        Publish(owner, () => { Accounts = accounts; AccountCursor = cursor; });
    });
    public Task EditAsync(string id) => RunAsync(async owner =>
    {
        var editor = await api.GetAsync(owner, id).ConfigureAwait(false);
        await api.RequireManageAsync(owner, editor.Value.UserId).ConfigureAwait(false);
        try { var hints = await api.RateHintsAsync(owner).ConfigureAwait(false); Publish(owner, () => RateHints = hints); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch { /* Optional presentation hints never grant access or replace canonical key metadata. */ }
        Publish(owner, () => { Editor = editor; ReviewRequired = false; });
    });
    public void CloseEditor() { lock (Gate) { if (Busy) return; Editor = null; } Notify(); }
    public async Task<string?> CreateAsync(string label, string userId, IReadOnlyList<string> scopes, bool scoped, bool confirmed)
    {
        string? secret = null;
        await RunAsync(async owner =>
        {
            if (!confirmed || Capabilities is not { Available: true } || !CanManage(userId) || scoped && scopes.Count == 0 ||
                scopes.Any(s => !Capabilities.Scopes.Any(c => c.Name == s))) throw new ArgumentException("Review key access.");
            await api.RequireManageAsync(owner, userId).ConfigureAwait(false);
            var created = await api.CreateAsync(owner, label, userId, scoped ? scopes.Distinct().ToArray() : []).ConfigureAwait(false);
            // Never place a full key in list/editor state. Only the active create dialog consumes it.
            Publish(owner, () => { secret = created.TakeSecret(); Message = "API key created. Save it now; it will not be shown again."; });
            created.Key = "";
            // Reconcile with the server's canonical first page. The secret is already held by the caller, so a
            // failed refresh never discards it; it only blocks further writes until the page-level reload re-reads the list.
            await RefreshListAsync(owner, "API key created. Save it now; it will not be shown again. The key list could not be refreshed: reload keys.", alsoReview: true).ConfigureAwait(false);
        }, write: true, listWrite: true).ConfigureAwait(false);
        return Current ? secret : null;
    }
    /// <summary>Re-reads the canonical first page after a write that already succeeded. A failed read keeps the success message, never reports the write as failed and marks the list stale until the page-level reload.</summary>
    private async Task RefreshListAsync(MonitorAuthorityLease owner, string staleMessage, bool alsoReview)
    {
        try
        {
            var page = await api.ListAsync(owner).ConfigureAwait(false);
            Publish(owner, () => { Rows = page.Items; NextCursor = page.Page!.NextCursor; });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch { Publish(owner, () => { Message = staleMessage; ListReviewRequired = true; if (alsoReview) ReviewRequired = true; }); }
    }
    public Task ChangeAsync(AdminApiKeyEditor reviewed, string? tier, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || !ReferenceEquals(Editor, reviewed) || Capabilities is not { Available: true, GuardedConfiguration: true }) return;
        await api.RequireManageAsync(owner, reviewed.Value.UserId).ConfigureAwait(false);
        if (tier == null) await api.RevokeAsync(owner, reviewed).ConfigureAwait(false);
        else
        {
            if (!Capabilities.RateTiers.Contains(tier)) throw new ArgumentException("Unsupported tier.");
            await api.UpdateTierAsync(owner, reviewed, tier).ConfigureAwait(false);
        }
        var done = tier == null ? "API key revoked." : "Rate limit updated.";
        Publish(owner, () => { Editor = null; Message = done; });
        // The change is already persisted: a failed re-read must not turn it into a reported failure.
        await RefreshListAsync(owner, done + " The key list could not be refreshed: reload keys.", alsoReview: false).ConfigureAwait(false);
    }, write: true);
}
