using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The last provider change that succeeded (turned on or off, or account creation changed), so its provider can say so in its own words.</summary>
public sealed record AdminSignInChange(string ProviderId, bool Enabled, bool? AutoProvision);

public sealed class AdminSignInController(AdminSignInApi api, AdminServerSettingsApi settings, AdminServerSettingsStore store, AdminMonitorAuthority authority) : AdminAccessController(authority)
{
    public const string Local = "auth.local_password_login", EmailMatch = "auth.email_auto_match", UserKeys = "auth.user_api_keys";
    public static IReadOnlyList<string> Keys { get; } = [Local, "signup.enabled", "password_reset.self_service_enabled", EmailMatch, UserKeys];
    private AdminSettingsForm? _form;
    private readonly Dictionary<(string Provider, string Schema), AdminSignInConfigWrite> _drafts = [];
    private readonly Dictionary<string, bool> _auto = [];
    public AdminSignInCapabilities? Capabilities { get; private set; }
    public IReadOnlyList<AdminSignInProvider> Providers { get; private set; } = [];
    public IReadOnlyList<AdminAccessAccount> Accounts { get; private set; } = [];
    public string? AccountCursor { get; private set; }
    public IReadOnlyCollection<string> Managed { get; private set; } = [];
    public LoggingRestartKeys RestartKeys { get; private set; } = new();
    public bool Loaded { get; private set; }
    public bool ProvidersKnown { get; private set; }
    private bool _accountsRead;
    // What the current write learned about the page's picture of the server (see RequiresReview). Reset when a write begins; read and written under Gate.
    private bool _applied, _secretErased;
    private string? _why, _note;
    /// <summary>The whole account list was read (every page, within the hard cap). Like the official page, a list that is not fully known does not gate the break-glass check here: the server refuses with 409 break_glass_required.</summary>
    public bool AccountsKnown => _accountsRead && AccountCursor == null;
    public string PublicUrl { get; private set; } = "";
    public string? ProviderError { get; private set; }
    public IReadOnlyDictionary<string, AdminSignInTestResult> TestResults { get; private set; } = new Dictionary<string, AdminSignInTestResult>();
    /// <summary>Why a connection test did not run, per provider (official: the test step's inline error); cleared by the next test or edit.</summary>
    public IReadOnlyDictionary<string, string> TestFailures { get; private set; } = new Dictionary<string, string>();
    public AdminSignInChange? LastChange { get; private set; }
    public int DirtyCount { get { lock (Gate) return (_form?.DirtyKeys.Count ?? 0) + _drafts.Sum(d => d.Value.Value.Count + d.Value.ClearSecrets.Count) + _auto.Count; } }
    public string Value(string key) { lock (Gate) return _form?.Get(key) ?? ""; }
    public string SavedValue(string key) { lock (Gate) return _form?.GetPersisted(key) ?? ""; }
    public bool Dirty(string key) { lock (Gate) return _form?.DirtyKeys.Contains(key) == true; }
    public bool AutoProvision(AdminSignInProvider provider) { lock (Gate) return provider.BindingFieldManaged("auto_provision") ? provider.Binding?.AutoProvision ?? true : _auto.GetValueOrDefault(provider.Id, provider.Binding?.AutoProvision ?? true); }
    public bool ProviderDirty(AdminSignInProvider provider) { lock (Gate) return _auto.ContainsKey(provider.Id) || _drafts.Keys.Any(k => k.Provider == provider.Id); }
    public bool FieldDirty(AdminSignInProvider provider, string schema, string field) { lock (Gate) return _drafts.TryGetValue((provider.Id, schema), out var draft) && (draft.Value.ContainsKey(field) || draft.ClearSecrets.Contains(field)); }
    public JsonObject Values(AdminSignInProvider provider, AdminSignInSchema schema)
    {
        lock (Gate)
        {
            var values = AdminSignInSchemaRules.Values(provider, schema);
            if (_drafts.TryGetValue((provider.Id, schema.Key), out var draft)) foreach (var (key, value) in draft.Value) values[key] = value?.DeepClone();
            return values;
        }
    }
    public bool Clearing(AdminSignInProvider p, string schema, string key) { lock (Gate) return _drafts.GetValueOrDefault((p.Id, schema))?.ClearSecrets.Contains(key) == true; }
    protected override void Reset()
    {
        foreach (var draft in _drafts.Values) draft.Value.Clear(); _drafts.Clear(); _auto.Clear(); _form?.Discard(); _form = null;
        Providers = []; Accounts = []; AccountCursor = null; Capabilities = null; Managed = []; PublicUrl = ""; Loaded = ProvidersKnown = _accountsRead = false;
        ProviderError = null; TestResults = new Dictionary<string, AdminSignInTestResult>(); TestFailures = new Dictionary<string, string>(); LastChange = null; RestartKeys = new();
        _applied = _secretErased = false; _why = _note = null;
    }
    private void BeginWrite() { lock (Gate) { _applied = _secretErased = false; _why = _note = null; } }
    /// <summary>The page's own check refused before anything was sent and a fresh read showed that the page's list is out of date: the refusal is definite and the page stays editable, but the message says so.</summary>
    private void Note(string note) { lock (Gate) _note = note; }
    private void Applied() { lock (Gate) _applied = true; }
    private void Stale(string why) { lock (Gate) _why = why; }
    /// <summary>A read that a write depends on: when it fails, the page's picture of the server could not be confirmed, so the page waits for a reload.</summary>
    private async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try { return await read().ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException) { Stale("The current state could not be read before writing."); throw; }
    }
    private static AdminSignInSchema? SchemaOf(AdminSignInProvider provider, string key) => provider.GlobalConfigSchema.FirstOrDefault(s => s.Key == key);
    /// <summary>
    /// Whether the failure of a write leaves the page unable to trust its picture of the server, so that it waits for a reload (and erases a typed secret, which can no longer be used).
    /// Decided from what actually happened. Nothing was applied and the draft is still valid for: a value rejected locally; a definite refusal by the server (422 validation_failed,
    /// 409 provider_already_enabled, 409 break_glass_required, 429); the page's own break-glass and provider checks when they sent nothing (even when a fresh read showed the page's
    /// list is out of date: the message says so, and every save reads it again). The page waits for a reload when something was already applied (it shows the older values),
    /// when a read the write relied on failed, when the deployment now owns what the page offered to change (managed_by_environment), on a conflict, and on an unknown outcome or
    /// any other failure. Only this page decides this; the other access pages keep the shared rule.
    /// </summary>
    protected override bool RequiresReview(Exception error)
    {
        lock (Gate)
        {
            if (_applied || _why != null) return true;
            return error switch
            {
                ArgumentException => false,
                AdminAccessException { Uncertain: false } refused when refused.Code is "validation_failed" or "provider_already_enabled" or "break_glass_required" or "provider_required" or "rate_limited" || refused.Status == 429 => false,
                // The settings write refused its values (nothing was applied, the form is intact).
                ApiException { StatusCode: 422 } => false,
                _ => true,
            };
        }
    }
    protected override string ReviewMessage(Exception error, string message, bool review)
    {
        if (!review) { lock (Gate) return _note == null ? message : message + " " + _note; }
        lock (Gate)
        {
            // An unknown outcome and a settings conflict already say that the page must be reloaded and why.
            if (error is AdminAccessException { Uncertain: true } or AdminSettingsOutcomeUnknownException || error is AdminSettingsConflictException && !_applied) return message;
            var why = _applied ? "Part of this change was already saved, so this page shows older values."
                : _why ?? (error as AdminAccessException)?.Code switch
                {
                    "managed_by_environment" => "The environment now manages something this page showed as editable.",
                    "conflict" => "The resource changed on the server.",
                    _ => null,
                };
            if (why == null) return message;
            return message + " " + why + " Reload the page to read the current state." + (_secretErased ? " A typed secret was cleared: type it again afterwards." : "");
        }
    }
    public Task OpenAsync() => OpenPageAsync(async owner =>
    {
        var snapshot = await settings.GetEffectiveAsync(owner, Keys.Append("server.public_url"), []).ConfigureAwait(false);
        var status = await settings.GetSensitiveStatusAsync(owner).ConfigureAwait(false);
        var restart = await settings.GetRestartKeysAsync(owner).ConfigureAwait(false);
        Publish(owner, () => { _form = new(Keys, [], snapshot.Values, snapshot.ETag); Managed = status.ManagedByEnv; PublicUrl = snapshot.Values.GetValueOrDefault("server.public_url", ""); RestartKeys = restart; Loaded = true; });
        try
        {
            var caps = await api.CapabilitiesAsync(owner).ConfigureAwait(false);
            Publish(owner, () => Capabilities = caps);
            if (caps.Available)
            {
                var providers = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
                Publish(owner, () => { Providers = providers; ProvidersKnown = true; });
            }
            else Publish(owner, () => { Providers = []; ProvidersKnown = true; });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch { Publish(owner, () => ProviderError = "External sign-in could not be loaded. Reload before changing provider-dependent settings."); }
        // Walk every account page (hard cap): the break-glass admin may be on any page.
        var (accounts, cursor) = await ReadAccountPagesAsync(c => api.AccountsAsync(owner, c), [], null, first: true).ConfigureAwait(false);
        Publish(owner, () => { Accounts = accounts; _accountsRead = true; AccountCursor = cursor; });
    });
    public Task MoreAccountsAsync() => RunAsync(async owner =>
    {
        var start = AccountCursor; if (start == null || Accounts.Count >= AccountCap) return;
        var (accounts, cursor) = await ReadAccountPagesAsync(c => api.AccountsAsync(owner, c), Accounts, start, first: false).ConfigureAwait(false);
        Publish(owner, () => { Accounts = accounts; AccountCursor = cursor; });
    });
    public void Set(string key, bool value)
    {
        lock (Gate)
        {
            if (!Current || Busy || ReviewRequired || !Loaded || Managed.Contains(key) || !Keys.Contains(key)) return;
            // As on the official page, "no break-glass admin" needs the whole account list; when it is not fully known the server decides
            // (409 break_glass_required on save). A provider list that failed to load still blocks turning passwords off here.
            var noBreakGlass = AccountsKnown && !Accounts.Any(BreakGlass);
            var noProvider = !ProvidersKnown || !Providers.Any(p => !p.Network && p.Enabled && p.Binding?.Enabled == true);
            if (key == Local && !value && (noBreakGlass || noProvider))
            { Message = noBreakGlass ? AdminText.T("web.admin.settings.signIn.page.noBreakGlassError") : ProvidersKnown ? AdminText.T("web.admin.settings.signIn.page.noProviderError") : ProviderError ?? AdminText.T("web.admin.settings.signIn.page.noProviderError"); }
            else { _form!.Set(key, value ? "true" : "false"); Message = null; }
        }
        Notify();
    }
    /// <summary>How many listed break-glass candidates a save reads again before leaving the decision to the server.</summary>
    private const int BreakGlassChecks = 10;
    private static bool BreakGlass(AdminAccessAccount a) => a.Role == "admin" && a.Enabled && a.BreakGlass && a.PasswordLogin;
    public void SetField(AdminSignInProvider p, AdminSignInSchema schema, AdminSignInField field, string text)
    {
        lock (Gate)
        {
            if (!Current || Busy || ReviewRequired || !Providers.Contains(p) || schema.AdminForm?.Fields.Contains(field) != true || p.ConfigFieldManaged(schema.Key, field.Key)) return;
            try
            {
                var value = AdminSignInSchemaRules.Convert(schema, field, text); var key = (p.Id, schema.Key);
                if (!_drafts.TryGetValue(key, out var draft)) _drafts[key] = draft = new() { Key = schema.Key };
                var saved = AdminSignInSchemaRules.Values(p, schema)[field.Key];
                if (field.IsSecret ? string.IsNullOrEmpty(text) : JsonNode.DeepEquals(value, saved)) draft.Value.Remove(field.Key); else draft.Value[field.Key] = value;
                if (text.Length > 0) draft.ClearSecrets.Remove(field.Key);
                if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
                var results = TestResults.ToDictionary(pair => pair.Key, pair => pair.Value); results.Remove(p.Id); TestResults = results; TestFailures = TestFailures.Where(f => f.Key != p.Id).ToDictionary(f => f.Key, f => f.Value); Message = null;
            }
            catch (ArgumentException) { Message = "Enter a valid value for " + field.Label + "."; }
        }
        Notify();
    }
    public void ToggleClear(AdminSignInProvider p, AdminSignInSchema schema, AdminSignInField field)
    {
        lock (Gate)
        {
            if (!Current || Busy || ReviewRequired || !field.IsSecret || !Providers.Contains(p) || p.ConfigFieldManaged(schema.Key, field.Key)) return;
            var key = (p.Id, schema.Key); if (!_drafts.TryGetValue(key, out var draft)) _drafts[key] = draft = new() { Key = schema.Key };
            draft.Value.Remove(field.Key);
            if (!draft.ClearSecrets.Remove(field.Key)) draft.ClearSecrets.Add(field.Key);
            if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
        }
        Notify();
    }
    public void SetAutoProvision(AdminSignInProvider p, bool value)
    {
        lock (Gate) { if (!Current || Busy || ReviewRequired || !Providers.Contains(p) || p.BindingFieldManaged("auto_provision")) return; if (value == (p.Binding?.AutoProvision ?? true)) _auto.Remove(p.Id); else _auto[p.Id] = value; } Notify();
    }
    public void Discard()
    {
        lock (Gate)
        {
            if (!Current || Busy) return; _form?.Discard(); foreach (var draft in _drafts.Values) draft.Value.Clear(); _drafts.Clear(); _auto.Clear();
            TestResults = new Dictionary<string, AdminSignInTestResult>();
        }
        Notify();
    }
    public Task SaveAsync(bool confirmed) => RunAsync(async owner =>
    {
        try { await SaveCoreAsync(owner, confirmed).ConfigureAwait(false); }
        // When the page must be read again (RequiresReview: an unknown outcome, a conflict, a managed field, a write that already landed, a read that failed) it blocks further writes
        // until the reload, and a typed SECRET is erased because it can no longer be used; the visible draft stays on screen until the admin reloads or discards. A definite refusal
        // that applied nothing (validation, provider already enabled, break glass, rate limit) leaves the page editable: the draft and the typed secret stay, so the admin can
        // correct a value and save again.
        catch (Exception error) when (error is not OperationCanceledException)
        { Publish(owner, () => { if (RequiresReview(error) && EraseTypedSecrets()) _secretErased = true; }); throw; }
    }, write: true);
    /// <summary>Drops every typed secret value from the staged drafts (a staged "clear" is not a typed value and stays). Called under the controller's publish.</summary>
    private bool EraseTypedSecrets()
    {
        var erased = false;
        foreach (var key in _drafts.Keys.ToArray())
        {
            var schema = Providers.FirstOrDefault(p => p.Id == key.Provider)?.GlobalConfigSchema.FirstOrDefault(s => s.Key == key.Schema); var draft = _drafts[key];
            foreach (var field in schema?.AdminForm?.Fields.Where(f => f.IsSecret) ?? []) erased |= draft.Value.Remove(field.Key);
            if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
        }
        return erased;
    }
    private async Task SaveCoreAsync(MonitorAuthorityLease owner, bool confirmed)
    {
        if (!confirmed || _form == null || DirtyCount == 0) return;
        BeginWrite();
        // Freeze the full intent before awaiting. Inputs are disabled until completion. An entry the plugin no longer lists has nothing to send (a refresh drops such drafts).
        var frozen = new List<(string Provider, string Schema, AdminSignInConfigWrite Write)>();
        try
        {
            foreach (var pair in _drafts.ToArray())
            {
                var provider = Providers.FirstOrDefault(p => p.Id == pair.Key.Provider); var schema = provider == null ? null : SchemaOf(provider, pair.Key.Schema);
                if (provider == null || schema == null) continue;
                frozen.Add((pair.Key.Provider, pair.Key.Schema, AdminSignInSchemaRules.Build(provider, schema, Values(provider, schema), pair.Value.ClearSecrets)));
            }
        }
        catch { foreach (var item in frozen) item.Write.Value.Clear(); throw; }
        var configs = frozen.ToArray();
        var autos = _auto.Where(pair => Providers.FirstOrDefault(p => p.Id == pair.Key)?.BindingFieldManaged("auto_provision") != true).ToArray();
        try
        {
            if (_form.DirtyKeys.Contains(Local) && _form.Get(Local) == "false")
            {
                // These read-only checks run before the first config or binding write, so a refusal here changes nothing. With the whole list known and no
                // break-glass admin, or a candidate that no longer qualifies, refuse. With a list that is not fully known (longer than the cap, or unread)
                // the server's own transactional check decides, as it does for the official page.
                var candidates = Accounts.Where(BreakGlass).ToArray();
                // The page's own check, from what it shows: nothing was sent and nothing is stale, so the page stays editable (turn Silo passwords back on, or add the account).
                if (candidates.Length == 0 && AccountsKnown) throw new AdminAccessException("break_glass_required", 409);
                // Each listed candidate is read again (bounded) until one still qualifies: one demoted elsewhere does not hide another that still does.
                var stale = 0;
                foreach (var candidate in candidates.Take(BreakGlassChecks))
                {
                    var account = await ReadAsync(() => api.AccountAsync(owner, candidate.Id)).ConfigureAwait(false);
                    if (BreakGlass(account) || !AccountsKnown) break;
                    stale++;
                }
                // Every account the page lists no longer qualifies. Nothing was sent, the refusal is definite and the draft is still valid: the page stays editable and says its list is out of date.
                // Past the bound, the server's own transactional check decides.
                if (candidates.Length > 0 && stale == candidates.Length) { Note("The account list on this page is out of date: reload the page to see the current one."); throw new AdminAccessException("break_glass_required", 409); }
                var providers = await ReadAsync(() => api.GetInstallationsAsync(owner)).ConfigureAwait(false);
                if (!providers.Any(p => !p.Network && p.Enabled && p.Binding?.Enabled == true)) { Note("The provider list on this page is out of date: reload the page to see the current one."); throw new AdminAccessException("provider_required", 409); }
            }
            foreach (var item in configs)
            {
                await api.SaveConfigAsync(owner, item.Provider, item.Write).ConfigureAwait(false);
                Applied();
                Publish(owner, () => { if (_drafts.Remove((item.Provider, item.Schema), out var draft)) draft.Value.Clear(); });
            }
        }
        finally { foreach (var item in configs) item.Write.Value.Clear(); }
        foreach (var (id, value) in autos)
        {
            var fresh = (await ReadAsync(() => api.GetInstallationsAsync(owner)).ConfigureAwait(false)).FirstOrDefault(p => p.Id == id) ?? throw new AdminAccessException("not_found", 404);
            await api.SaveBindingAsync(owner, id, Binding(fresh, auto: value)).ConfigureAwait(false);
            Applied();
            Publish(owner, () => _auto.Remove(id));
        }
        if (_form.DirtyKeys.Count > 0)
        {
            var intent = _form.CaptureSave(Managed);
            try
            {
                if (intent.Values.Count > 0)
                {
                    var result = await settings.PutAsync(owner, intent.Values, intent.ETag, Keys, []).ConfigureAwait(false);
                    Applied();
                    Publish(owner, () => _form?.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired)); store.Invalidate();
                }
            }
            // The server's transactional check is authoritative (409 break_glass_required, the only 409 a settings write documents): say so, as the official page does.
            catch (ApiException refused) when (refused.StatusCode == 409 && intent.Values.ContainsKey(Local)) { throw new AdminAccessException("break_glass_required", 409); }
            finally { Publish(owner, () => _form?.EndSave(intent)); }
        }
        // Everything is already persisted: a failed re-read must not turn it into a reported failure.
        await RefreshProvidersAsync(owner, "Sign-in settings saved.", configs.Length > 0 || autos.Length > 0).ConfigureAwait(false);
    }
    /// <summary>The deployment can claim a field while an edit to it is staged: that edit is dropped at once and releasing the field later does not bring it back (official dropManagedEdits). Called under the controller's publish.</summary>
    private void PruneManagedDrafts()
    {
        foreach (var provider in Providers)
        {
            if (provider.BindingFieldManaged("auto_provision")) _auto.Remove(provider.Id);
            foreach (var key in _drafts.Keys.Where(k => k.Provider == provider.Id).ToArray())
            {
                // An entry the plugin no longer lists has nothing left to save.
                if (SchemaOf(provider, key.Schema) == null) { _drafts.Remove(key, out var gone); gone?.Value.Clear(); continue; }
                var managed = provider.ManagedConfigFields(key.Schema); var draft = _drafts[key];
                foreach (var field in managed) { draft.Value.Remove(field); draft.ClearSecrets.Remove(field); }
                if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
            }
        }
    }
    /// <summary>
    /// Re-reads the providers after a write that already succeeded. A failed read keeps the success message and never reports the write as failed.
    /// When that write changed provider state the list is then unknown, as after an open that could not load it: no pre-write value is shown or
    /// saved again, and only provider-dependent settings wait for the page-level reload.
    /// </summary>
    private async Task RefreshProvidersAsync(MonitorAuthorityLease owner, string done, bool providersChanged)
    {
        try
        {
            var refreshed = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
            Publish(owner, () =>
            {
                Providers = refreshed; Message = done; PruneManagedDrafts();
                if (Capabilities is { Available: true }) { ProvidersKnown = true; ProviderError = null; }
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch
        {
            Publish(owner, () =>
            {
                Message = done; if (!providersChanged) return;
                // Edits staged against the list that is no longer known go with it.
                foreach (var draft in _drafts.Values) draft.Value.Clear(); _drafts.Clear(); _auto.Clear();
                Providers = []; ProvidersKnown = false; TestResults = new Dictionary<string, AdminSignInTestResult>();
                ProviderError = "The provider list could not be refreshed. Reload before changing provider-dependent settings.";
                Message = done + " The provider list could not be refreshed: reload to see the current provider state.";
            });
        }
    }
    /// <summary>The four binding fields a write restates, as a read shows them (a missing row or field reads as the server's defaults).</summary>
    private static (bool Enabled, bool Auto, bool DefaultLogin, int Order) RowOf(AdminSignInProvider p) => (p.Binding?.Enabled == true, p.Binding?.AutoProvision ?? true, p.Binding?.DefaultLogin ?? false, p.Binding?.DisplayOrder ?? 1);
    /// <summary>
    /// Changes ONE field of a provider's binding. With <paramref name="autoProvision"/> null the admin is turning the binding on or off (<paramref name="enabled"/> is the new value);
    /// otherwise the admin is changing only "Create accounts on first sign-in" and <paramref name="enabled"/> is ignored. The binding is read again first and the write restates every field
    /// the admin did not touch from that fresh read, as the official page does. If another session changed one of those fields since this page drew the provider, nothing is written:
    /// the admin is told it changed elsewhere and the page shows the fresh state, so a switch can never put back a value that someone else changed.
    /// </summary>
    public Task SetBindingAsync(AdminSignInProvider captured, bool enabled, bool? autoProvision, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || Capabilities is not { Available: true, LiveProviderChanges: true } || !Providers.Contains(captured)) return;
        BeginWrite();
        Publish(owner, () => LastChange = null);
        var providers = await ReadAsync(() => api.GetInstallationsAsync(owner)).ConfigureAwait(false);
        var provider = providers.FirstOrDefault(p => p.Id == captured.Id && p.Auth?.Id == captured.Auth?.Id) ?? throw new AdminAccessException("not_found", 404);
        var touchesAuto = autoProvision != null; var shown = RowOf(captured); var now = RowOf(provider);
        if (touchesAuto && now.Enabled != shown.Enabled || !touchesAuto && now.Auto != shown.Auto || now.DefaultLogin != shown.DefaultLogin || now.Order != shown.Order)
        {
            Publish(owner, () => { Providers = providers; PruneManagedDrafts(); Message = AdminText.T("signIn.native.changedElsewhere"); });
            return;
        }
        var wantEnabled = touchesAuto ? now.Enabled : enabled;
        // The deployment owns this field: nothing is sent (the server would refuse a changed value with 422 managed_by_environment). Judged on the fresh read, not the page's older copy.
        if (touchesAuto ? autoProvision != now.Auto && provider.BindingFieldManaged("auto_provision") : wantEnabled != now.Enabled && provider.BindingFieldManaged("enabled"))
            throw new AdminAccessException("managed_by_environment", 422);
        if (!touchesAuto && enabled && (!provider.Enabled || ProviderDirty(captured) || !provider.Network && (AdminSignInSchemaRules.Missing(provider).Count > 0 || providers.Any(p => !p.Network && p.Id != provider.Id && p.Enabled && p.Binding?.Enabled == true)))) throw new ArgumentException("Save setup and turn off the other provider first.");
        await api.SaveBindingAsync(owner, provider.Id, Binding(provider, wantEnabled, autoProvision)).ConfigureAwait(false);
        Applied();
        Publish(owner, () => LastChange = new(provider.Id, wantEnabled, autoProvision));
        await RefreshProvidersAsync(owner, wantEnabled ? "Sign-in provider turned on." : "Sign-in provider turned off.", providersChanged: true).ConfigureAwait(false);
    }, write: true);
    public Task TestAsync(AdminSignInProvider captured, string? username = null) => RunAsync(async owner =>
    {
        if (!Providers.Contains(captured) || !captured.Enabled || Capabilities?.ConnectionTest != true || captured.Auth?.Metadata["connection_test"]?.ToString() != "true") return;
        var staged = new List<AdminSignInConfigWrite>();
        try
        {
            foreach (var draft in _drafts.Where(d => d.Key.Provider == captured.Id).ToArray())
                if (SchemaOf(captured, draft.Key.Schema) is { } draftSchema) staged.Add(AdminSignInSchemaRules.Build(captured, draftSchema, Values(captured, draftSchema), draft.Value.ClearSecrets));
        }
        catch { foreach (var entry in staged) entry.Value.Clear(); throw; }
        var testRef = captured.Auth.Metadata["connection_test_username_field"]?.ToString()?.Split('.', 2);
        // A test-only look-up user the deployment owns is the saved one: the page shows it read-only and sends nothing for it.
        if (testRef is { Length: 2 } && username != null && !captured.ConfigFieldManaged(testRef[0], testRef[1]) && SchemaOf(captured, testRef[0]) is { } schema)
        {
            var entry = staged.FirstOrDefault(s => s.Key == schema.Key);
            if (entry == null) { entry = AdminSignInSchemaRules.Build(captured, schema, Values(captured, schema), []); staged.Add(entry); }
            entry.Value[testRef[1]] = username; // test-only lookup is never part of a save draft.
        }
        Publish(owner, () => { TestFailures = TestFailures.Where(f => f.Key != captured.Id).ToDictionary(f => f.Key, f => f.Value); var results = TestResults.ToDictionary(pair => pair.Key, pair => pair.Value); results.Remove(captured.Id); TestResults = results; });
        try
        {
            var result = await api.TestAsync(owner, captured.Id, captured.Auth.Id, staged).ConfigureAwait(false);
            Publish(owner, () => { var results = TestResults.ToDictionary(pair => pair.Key, pair => pair.Value); results[captured.Id] = result; TestResults = results; });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Only the shared client's own wording or the official fallback is kept: no server text, and nothing typed, reaches the screen.
            var text = error is AdminAccessException safe ? safe.Message : AdminText.T("web.admin.settings.signIn.provider.test.errorFallback");
            Publish(owner, () => { var failures = TestFailures.ToDictionary(f => f.Key, f => f.Value); failures[captured.Id] = text; TestFailures = failures; });
            throw;
        }
        finally { foreach (var entry in staged) entry.Value.Clear(); }
    });
    private static AdminSignInBinding Binding(AdminSignInProvider p, bool? enabled = null, bool? auto = null) => new()
    {
        CapabilityId = p.Auth!.Id, Enabled = enabled ?? p.Binding?.Enabled ?? false, AutoProvision = auto ?? p.Binding?.AutoProvision ?? true,
        // The saved row is written back as it is (official signInBindingWrite): a field the deployment owns is never given a value of the admin's.
        DefaultLogin = p.Binding?.DefaultLogin ?? false, DisplayOrder = p.Binding?.DisplayOrder ?? 1,
    };
}
