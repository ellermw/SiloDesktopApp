using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Services;

/// <summary>Who is looking at Settings > API Keys: the acting profile and whether the account is a server admin's.</summary>
public sealed record PersonalApiKeysViewer(string ProfileId, string ProfileName, bool IsPrimary, bool IsAdminAccount);

public enum PersonalApiKeyCreateStatus
{
    /// <summary>The key exists; <see cref="PersonalApiKeyCreateResult.Secret"/> holds it, once.</summary>
    Created,
    /// <summary>The server refused with a client error; <see cref="PersonalApiKeyCreateResult.Message"/> says why. No key was made.</summary>
    Refused,
    /// <summary>The request may or may not have made a key. Nothing was resent.</summary>
    Uncertain,
    /// <summary>The account, profile or server changed, the page was left, or a create was already running. Nothing is shown.</summary>
    Dropped,
}

/// <summary>The outcome of one create. The secret is held here only until the caller shows it; it is never printed.</summary>
public sealed class PersonalApiKeyCreateResult(PersonalApiKeyCreateStatus status, string? message = null, string? secret = null, string? target = null)
{
    public PersonalApiKeyCreateStatus Status { get; } = status;
    public string? Message { get; } = message;
    public string? Secret { get; } = secret;
    /// <summary>What the key was made for: <see cref="PersonalApiKeysModel.WholeAccount"/> or a profile id.</summary>
    public string? Target { get; } = target;
    public override string ToString() => "PersonalApiKeyCreateResult " + Status;
}

public enum PersonalApiKeyRevokeStatus { Revoked, Failed, Dropped }

/// <summary>
/// State and rules of the official WebUI's Settings > API Keys page (silo-server web/src/pages/settings/ApiKeysSettings.tsx):
/// the scopes capability, the paged list, the limit, creating and revoking. Every read and write belongs to the account
/// and profile that started it, so a reply that arrives after a switch is dropped, and a create is sent exactly once.
/// </summary>
public sealed class PersonalApiKeysModel(
    ApiKeysApi api,
    SiloApiClient client,
    Func<CancellationToken, Task<IReadOnlyList<Profile>>> loadProfiles)
{
    /// <summary>The "Acts as" choice that is not tied to one profile.</summary>
    public const string WholeAccount = "account";

    private readonly List<PersonalApiKey> _rows = [];
    private readonly HashSet<string> _cursors = [];
    private IReadOnlyList<Profile> _profiles = [];
    private PersonalApiKeyScopes? _capability;
    private PersonalApiKeysViewer? _viewer;
    private ApiIdentityContext _owner;
    private CancellationTokenSource? _cts;
    private string? _nextCursor;
    private int _revision;
    private bool _creating;
    private bool _fetchingNext;
    private bool _listLoaded;

    public event Action? Changed;

    /// <summary>True until both the capability and the first page have settled; the page shows only "Loading API keys…" then.</summary>
    public bool IsLoading { get; private set; }
    public bool CapabilityFailed { get; private set; }
    /// <summary>The list (first page, a later page or a reload) failed; it shows "Couldn't load your API keys." in place of the rows.</summary>
    public bool ListFailed { get; private set; }
    public PersonalApiKeyScopes? Capability => _capability;
    public IReadOnlyList<PersonalApiKey> Rows => _rows;
    public IReadOnlyList<Profile> Profiles => _profiles;
    public PersonalApiKeysViewer? Viewer => _viewer;
    public bool HasNextPage => _nextCursor != null;
    public bool IsFetchingNextPage => _fetchingNext;
    public bool IsCreating => _creating;

    /// <summary>Whether this server lets accounts and profiles create keys.</summary>
    public bool Enabled => _capability is { Available: true, UserKeysEnabled: true };

    /// <summary>Creating needs a loaded list: the list reloads after a create, and one with nothing loaded would show its loading state in place of the dialog that holds the new key.</summary>
    public bool ListLoaded => _listLoaded;

    public long MaxKeys => _capability?.MaxUserKeys ?? 0;

    public IReadOnlyList<PersonalApiKeyScope> UserScopes
        => (_capability?.Scopes ?? []).Where(scope => scope.Audience == "user").ToList();

    /// <summary>
    /// The primary profile sees every key on the account, so its count is the account's; another profile learns the limit
    /// from the server's refusal. An admin's own account keys are not held to the limit, so the server decides for an admin.
    /// </summary>
    public bool AtLimit => _viewer is { IsAdminAccount: false, IsPrimary: true } && !HasNextPage && MaxKeys > 0 && _rows.Count >= MaxKeys;

    public bool CanCreate => Enabled && _listLoaded && !AtLimit && _viewer is { ProfileId.Length: > 0 };

    /// <summary>"Whole account" is a choice only for the primary profile; every other profile's key is its own.</summary>
    public string DefaultTarget => _viewer is { IsPrimary: true } ? WholeAccount : _viewer?.ProfileId ?? WholeAccount;

    /// <summary>The name shown for a profile id; the id itself when the list does not hold it.</summary>
    public string ProfileName(string id)
        => _profiles.FirstOrDefault(profile => profile.Id == id)?.Name ?? (_viewer?.ProfileId == id ? _viewer.ProfileName : id);

    /// <summary>Leaves the page: running requests are cancelled and their replies dropped.</summary>
    public void Deactivate()
    {
        _revision++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _fetchingNext = false;
        IsLoading = false;
    }

    /// <summary>Opens the page: reads the scopes, the first page of keys and the profile names for <paramref name="viewer"/>.</summary>
    public async Task LoadAsync(PersonalApiKeysViewer viewer)
    {
        Deactivate();
        var revision = _revision;
        var previousOwner = _owner;
        var owner = _owner = client.CaptureIdentityContext();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        // The last page's rows stay on screen while a visit to the same account and profile reloads them.
        if (_viewer?.ProfileId != viewer.ProfileId || _capability == null || previousOwner != owner)
        {
            _rows.Clear(); _cursors.Clear(); _nextCursor = null; _listLoaded = false; _capability = null; _profiles = [];
        }
        _viewer = viewer;
        IsLoading = !_listLoaded || _capability == null;
        CapabilityFailed = false; ListFailed = false;
        Changed?.Invoke();
        bool Current() => revision == _revision && client.IsCurrentIdentityContext(owner);

        var scopes = Settle(() => api.GetScopesAsync(owner, ct));
        var page = Settle(() => api.GetPageAsync(owner, null, ct));
        var profiles = Settle(() => loadProfiles(ct));
        await Task.WhenAll(scopes, page, profiles);
        if (!Current()) return;
        if (scopes.Result.Value is { } capability) _capability = capability; else { _capability = null; CapabilityFailed = true; }
        if (page.Result.Value is { } first) ReplaceRows(first); else ListFailed = true;
        if (profiles.Result.Value is { } names) _profiles = names;
        IsLoading = false;
        Changed?.Invoke();
    }

    /// <summary>Reads the next page. A failure shows the load-failed text; Load more tries again.</summary>
    public async Task LoadMoreAsync()
    {
        if (_nextCursor == null || _fetchingNext || IsLoading || _viewer == null) return;
        var revision = _revision;
        var owner = _owner;
        var cursor = _nextCursor;
        var ct = _cts?.Token ?? CancellationToken.None;
        _fetchingNext = true;
        Changed?.Invoke();
        bool Current() => revision == _revision && client.IsCurrentIdentityContext(owner);
        var settled = await Settle(() => api.GetPageAsync(owner, cursor, ct));
        if (!Current()) return;
        _fetchingNext = false;
        if (settled.Value is { } page)
        {
            _cursors.Add(cursor);
            foreach (var key in page.Items) AddRow(key);
            // A cursor already used would page forever.
            _nextCursor = page.NextCursor != null && !_cursors.Contains(page.NextCursor) ? page.NextCursor : null;
            ListFailed = false;
        }
        else ListFailed = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Makes a key and returns it once. Sent exactly once: no token-refresh replay, no retry. A refusal below 500 says why;
    /// anything else (a server error, a lost connection, an unreadable answer) may still have made a key and is
    /// <see cref="PersonalApiKeyCreateStatus.Uncertain"/>.
    /// </summary>
    public async Task<PersonalApiKeyCreateResult> CreateAsync(string label, IReadOnlyList<string> scopes, string target)
    {
        if (_creating || _viewer == null || !CanCreate || scopes.Count == 0) return new(PersonalApiKeyCreateStatus.Dropped);
        var revision = _revision;
        var owner = _owner;
        var ct = _cts?.Token ?? CancellationToken.None;
        bool Current() => revision == _revision && client.IsCurrentIdentityContext(owner);
        if (!Current()) return new(PersonalApiKeyCreateStatus.Dropped);
        _creating = true;
        Changed?.Invoke();
        PersonalApiKeyCreateResult result;
        try
        {
            var created = await api.CreateAsync(owner, label, scopes, target == WholeAccount ? null : target, ct);
            result = string.IsNullOrEmpty(created.Key)
                ? new(PersonalApiKeyCreateStatus.Uncertain)
                : new(PersonalApiKeyCreateStatus.Created, secret: created.Key, target: target);
            created.Key = "";
        }
        catch (ApiException error) when (error.StatusCode < 500)
        {
            result = new(PersonalApiKeyCreateStatus.Refused, ApiProblemText.Display(error));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !Current())
        {
            result = new(PersonalApiKeyCreateStatus.Dropped);
        }
        catch (Exception error) when (error is ApiException or HttpRequestException or JsonException or IOException or TimeoutException or OperationCanceledException)
        {
            result = new(PersonalApiKeyCreateStatus.Uncertain);
        }
        _creating = false;
        if (!Current()) return new(PersonalApiKeyCreateStatus.Dropped);
        Changed?.Invoke();
        // The list reloads whatever happened: an uncertain create may have made the key.
        _ = ReloadAsync(revision, owner);
        return result;
    }

    public async Task<PersonalApiKeyRevokeStatus> RevokeAsync(string id)
    {
        if (_viewer == null) return PersonalApiKeyRevokeStatus.Dropped;
        var revision = _revision;
        var owner = _owner;
        var ct = _cts?.Token ?? CancellationToken.None;
        bool Current() => revision == _revision && client.IsCurrentIdentityContext(owner);
        if (!Current()) return PersonalApiKeyRevokeStatus.Dropped;
        var status = PersonalApiKeyRevokeStatus.Revoked;
        try { await api.RevokeAsync(owner, id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !Current()) { return PersonalApiKeyRevokeStatus.Dropped; }
        catch (Exception error) when (error is ApiException or HttpRequestException or JsonException or IOException or TimeoutException or OperationCanceledException)
        { status = PersonalApiKeyRevokeStatus.Failed; }
        if (!Current()) return PersonalApiKeyRevokeStatus.Dropped;
        _ = ReloadAsync(revision, owner);
        return status;
    }

    /// <summary>Reads the first page again (after a create or a revoke); the rows stay until it answers.</summary>
    private async Task ReloadAsync(int revision, ApiIdentityContext owner)
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        bool Current() => revision == _revision && client.IsCurrentIdentityContext(owner);
        var settled = await Settle(() => api.GetPageAsync(owner, null, ct));
        if (!Current()) return;
        if (settled.Value is { } first) ReplaceRows(first); else ListFailed = true;
        Changed?.Invoke();
    }

    private void ReplaceRows(PersonalApiKeyPage first)
    {
        _rows.Clear(); _cursors.Clear();
        foreach (var key in first.Items) AddRow(key);
        _nextCursor = first.NextCursor;
        _listLoaded = true;
        ListFailed = false;
    }

    private void AddRow(PersonalApiKey key)
    {
        var index = _rows.FindIndex(row => row.Id == key.Id);
        if (index >= 0) _rows[index] = key; else _rows.Add(key);
    }

    private readonly record struct Settled<T>(T? Value) where T : class;

    /// <summary>Runs a read to its end; a failure (or a cancel) is a missing value, never an exception.</summary>
    private static async Task<Settled<T>> Settle<T>(Func<Task<T>> read) where T : class
    {
        try { return new(await read()); }
        catch (Exception error) when (error is ApiException or HttpRequestException or JsonException or IOException or TimeoutException or OperationCanceledException)
        { return new(null); }
    }
}
