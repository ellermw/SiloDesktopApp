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
        // An unknown outcome (a timeout or transport failure after sending, or an answer that cannot be read) blocks further writes until the state is read again; nothing typed
        // stays staged for it. A definite refusal keeps the draft so the admin can correct it.
        catch (Exception error) when (error is not OperationCanceledException && error is not ArgumentException && error is not AdminAccessException { Uncertain: false })
        { Publish(owner, EraseTypedSecrets); throw; }
    }, write: true);
    /// <summary>Drops every typed secret value from the staged drafts (a staged "clear" is not a typed value and stays). Called under the controller's publish.</summary>
    private void EraseTypedSecrets()
    {
        foreach (var key in _drafts.Keys.ToArray())
        {
            var schema = Providers.FirstOrDefault(p => p.Id == key.Provider)?.GlobalConfigSchema.FirstOrDefault(s => s.Key == key.Schema); var draft = _drafts[key];
            foreach (var field in schema?.AdminForm?.Fields.Where(f => f.IsSecret) ?? []) draft.Value.Remove(field.Key);
            if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
        }
    }
    private async Task SaveCoreAsync(MonitorAuthorityLease owner, bool confirmed)
    {
        if (!confirmed || _form == null || DirtyCount == 0) return;
        // Freeze the full intent before awaiting. Inputs are disabled until completion.
        var configs = _drafts.Select(pair => (pair.Key.Provider, Schema: pair.Key.Schema, Write: AdminSignInSchemaRules.Build(Providers.Single(p => p.Id == pair.Key.Provider), Providers.Single(p => p.Id == pair.Key.Provider).GlobalConfigSchema.Single(s => s.Key == pair.Key.Schema), Values(Providers.Single(p => p.Id == pair.Key.Provider), Providers.Single(p => p.Id == pair.Key.Provider).GlobalConfigSchema.Single(s => s.Key == pair.Key.Schema)), pair.Value.ClearSecrets))).ToArray();
        var autos = _auto.Where(pair => Providers.FirstOrDefault(p => p.Id == pair.Key)?.BindingFieldManaged("auto_provision") != true).ToArray();
        try
        {
            if (_form.DirtyKeys.Contains(Local) && _form.Get(Local) == "false")
            {
                // These read-only checks run before the first config or binding write, so a refusal here changes nothing. With the whole list known and no
                // break-glass admin, or a candidate that no longer qualifies, refuse. With a list that is not fully known (longer than the cap, or unread)
                // the server's own transactional check decides, as it does for the official page.
                var candidate = Accounts.FirstOrDefault(BreakGlass);
                if (candidate == null && AccountsKnown) throw new AdminAccessException("break_glass_required", 409);
                if (candidate != null)
                {
                    var account = await api.AccountAsync(owner, candidate.Id).ConfigureAwait(false);
                    if (!BreakGlass(account) && AccountsKnown) throw new AdminAccessException("break_glass_required", 409);
                }
                var providers = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
                if (!providers.Any(p => !p.Network && p.Enabled && p.Binding?.Enabled == true)) throw new AdminAccessException("provider_required", 409);
            }
            foreach (var item in configs)
            {
                await api.SaveConfigAsync(owner, item.Provider, item.Write).ConfigureAwait(false);
                Publish(owner, () => { if (_drafts.Remove((item.Provider, item.Schema), out var draft)) draft.Value.Clear(); });
            }
        }
        finally { foreach (var item in configs) item.Write.Value.Clear(); }
        foreach (var (id, value) in autos)
        {
            var fresh = (await api.GetInstallationsAsync(owner).ConfigureAwait(false)).SingleOrDefault(p => p.Id == id) ?? throw new AdminAccessException("not_found", 404);
            await api.SaveBindingAsync(owner, id, Binding(fresh, auto: value)).ConfigureAwait(false);
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
    public Task SetBindingAsync(AdminSignInProvider captured, bool enabled, bool? autoProvision, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || Capabilities is not { Available: true, LiveProviderChanges: true } || !Providers.Contains(captured)) return;
        Publish(owner, () => LastChange = null);
        var providers = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
        var provider = providers.SingleOrDefault(p => p.Id == captured.Id && p.Auth?.Id == captured.Auth?.Id) ?? throw new AdminAccessException("not_found", 404);
        // The deployment owns this field: nothing is sent (the server would refuse a changed value with 422 managed_by_environment). Judged on the fresh read, not the page's older copy.
        var changesAuto = autoProvision is { } wanted && wanted != (provider.Binding?.AutoProvision ?? true);
        if (changesAuto && provider.BindingFieldManaged("auto_provision") || !changesAuto && enabled != (provider.Binding?.Enabled == true) && provider.BindingFieldManaged("enabled"))
            throw new AdminAccessException("managed_by_environment", 422);
        if (enabled && (!provider.Enabled || ProviderDirty(captured) || !provider.Network && (AdminSignInSchemaRules.Missing(provider).Count > 0 || providers.Any(p => !p.Network && p.Id != provider.Id && p.Enabled && p.Binding?.Enabled == true)))) throw new ArgumentException("Save setup and turn off the other provider first.");
        await api.SaveBindingAsync(owner, provider.Id, Binding(provider, enabled, autoProvision)).ConfigureAwait(false);
        Publish(owner, () => LastChange = new(provider.Id, enabled, autoProvision));
        await RefreshProvidersAsync(owner, enabled ? "Sign-in provider turned on." : "Sign-in provider turned off.", providersChanged: true).ConfigureAwait(false);
    }, write: true);
    public Task TestAsync(AdminSignInProvider captured, string? username = null) => RunAsync(async owner =>
    {
        if (!Providers.Contains(captured) || !captured.Enabled || Capabilities?.ConnectionTest != true || captured.Auth?.Metadata["connection_test"]?.ToString() != "true") return;
        var staged = _drafts.Where(d => d.Key.Provider == captured.Id).Select(d => AdminSignInSchemaRules.Build(captured, captured.GlobalConfigSchema.Single(s => s.Key == d.Key.Schema), Values(captured, captured.GlobalConfigSchema.Single(s => s.Key == d.Key.Schema)), d.Value.ClearSecrets)).ToList();
        var testRef = captured.Auth.Metadata["connection_test_username_field"]?.ToString()?.Split('.', 2);
        // A test-only look-up user the deployment owns is the saved one: the page shows it read-only and sends nothing for it.
        if (testRef is { Length: 2 } && username != null && !captured.ConfigFieldManaged(testRef[0], testRef[1]))
        {
            var schema = captured.GlobalConfigSchema.Single(s => s.Key == testRef[0]);
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
