using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

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
    public bool AccountsKnown { get; private set; }
    public string PublicUrl { get; private set; } = "";
    public string? ProviderError { get; private set; }
    public IReadOnlyDictionary<string, AdminSignInTestResult> TestResults { get; private set; } = new Dictionary<string, AdminSignInTestResult>();
    public int DirtyCount { get { lock (Gate) return (_form?.DirtyKeys.Count ?? 0) + _drafts.Sum(d => d.Value.Value.Count + d.Value.ClearSecrets.Count) + _auto.Count; } }
    public string Value(string key) { lock (Gate) return _form?.Get(key) ?? ""; }
    public string SavedValue(string key) { lock (Gate) return _form?.GetPersisted(key) ?? ""; }
    public bool Dirty(string key) { lock (Gate) return _form?.DirtyKeys.Contains(key) == true; }
    public bool AutoProvision(AdminSignInProvider provider) { lock (Gate) return _auto.GetValueOrDefault(provider.Id, provider.Binding?.AutoProvision ?? true); }
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
        Providers = []; Accounts = []; AccountCursor = null; Capabilities = null; Managed = []; PublicUrl = ""; Loaded = ProvidersKnown = AccountsKnown = false;
        ProviderError = null; TestResults = new Dictionary<string, AdminSignInTestResult>(); RestartKeys = new();
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
        var accounts = await api.AccountsAsync(owner).ConfigureAwait(false);
        Publish(owner, () => { Accounts = accounts.Items; AccountsKnown = true; AccountCursor = accounts.Page!.NextCursor; });
    });
    public Task MoreAccountsAsync() => RunAsync(async owner =>
    {
        if (AccountCursor == null || Accounts.Count >= 1000) return;
        var page = await api.AccountsAsync(owner, AccountCursor).ConfigureAwait(false);
        Publish(owner, () => { Accounts = Accounts.Concat(page.Items).DistinctBy(a => a.Id).Take(1000).ToArray(); AccountCursor = page.Page!.NextCursor; });
    });
    public void Set(string key, bool value)
    {
        lock (Gate)
        {
            if (!Current || Busy || ReviewRequired || !Loaded || Managed.Contains(key) || !Keys.Contains(key)) return;
            if (key == Local && !value && (!AccountsKnown || !ProvidersKnown || !Accounts.Any(BreakGlass) || !Providers.Any(p => !p.Network && p.Enabled && p.Binding?.Enabled == true)))
            { Message = !Accounts.Any(BreakGlass) ? AdminText.T("web.admin.settings.signIn.page.noBreakGlassError") : AdminText.T("web.admin.settings.signIn.page.noProviderError"); }
            else { _form!.Set(key, value ? "true" : "false"); Message = null; }
        }
        Notify();
    }
    private static bool BreakGlass(AdminAccessAccount a) => a.Role == "admin" && a.Enabled && a.BreakGlass && a.PasswordLogin;
    public void SetField(AdminSignInProvider p, AdminSignInSchema schema, AdminSignInField field, string text)
    {
        lock (Gate)
        {
            if (!Current || Busy || ReviewRequired || !Providers.Contains(p) || schema.AdminForm?.Fields.Contains(field) != true) return;
            try
            {
                var value = AdminSignInSchemaRules.Convert(schema, field, text); var key = (p.Id, schema.Key);
                if (!_drafts.TryGetValue(key, out var draft)) _drafts[key] = draft = new() { Key = schema.Key };
                var saved = AdminSignInSchemaRules.Values(p, schema)[field.Key];
                if (field.IsSecret ? string.IsNullOrEmpty(text) : JsonNode.DeepEquals(value, saved)) draft.Value.Remove(field.Key); else draft.Value[field.Key] = value;
                if (text.Length > 0) draft.ClearSecrets.Remove(field.Key);
                if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
                var results = TestResults.ToDictionary(pair => pair.Key, pair => pair.Value); results.Remove(p.Id); TestResults = results; Message = null;
            }
            catch (ArgumentException) { Message = "Enter a valid value for " + field.Label + "."; }
        }
        Notify();
    }
    public void ToggleClear(AdminSignInProvider p, AdminSignInSchema schema, AdminSignInField field)
    {
        lock (Gate)
        {
            if (!Current || Busy || ReviewRequired || !field.IsSecret || !Providers.Contains(p)) return;
            var key = (p.Id, schema.Key); if (!_drafts.TryGetValue(key, out var draft)) _drafts[key] = draft = new() { Key = schema.Key };
            draft.Value.Remove(field.Key);
            if (!draft.ClearSecrets.Remove(field.Key)) draft.ClearSecrets.Add(field.Key);
            if (draft.Value.Count == 0 && draft.ClearSecrets.Count == 0) _drafts.Remove(key);
        }
        Notify();
    }
    public void SetAutoProvision(AdminSignInProvider p, bool value)
    {
        lock (Gate) { if (!Current || Busy || ReviewRequired || !Providers.Contains(p)) return; if (value == (p.Binding?.AutoProvision ?? true)) _auto.Remove(p.Id); else _auto[p.Id] = value; } Notify();
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
        if (!confirmed || _form == null || DirtyCount == 0) return;
        // Freeze the full intent before awaiting. Inputs are disabled until completion.
        var configs = _drafts.Select(pair => (pair.Key.Provider, Schema: pair.Key.Schema, Write: AdminSignInSchemaRules.Build(Providers.Single(p => p.Id == pair.Key.Provider), Providers.Single(p => p.Id == pair.Key.Provider).GlobalConfigSchema.Single(s => s.Key == pair.Key.Schema), Values(Providers.Single(p => p.Id == pair.Key.Provider), Providers.Single(p => p.Id == pair.Key.Provider).GlobalConfigSchema.Single(s => s.Key == pair.Key.Schema)), pair.Value.ClearSecrets))).ToArray();
        var autos = _auto.ToArray();
        try
        {
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
            if (_form.DirtyKeys.Contains(Local) && _form.Get(Local) == "false")
            {
                var candidate = Accounts.FirstOrDefault(BreakGlass) ?? throw new AdminAccessException("break_glass_required", 409);
                var account = await api.AccountAsync(owner, candidate.Id).ConfigureAwait(false);
                var providers = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
                if (!BreakGlass(account)) throw new AdminAccessException("break_glass_required", 409);
                if (!providers.Any(p => !p.Network && p.Enabled && p.Binding?.Enabled == true)) throw new AdminAccessException("provider_already_enabled", 409);
            }
            var intent = _form.CaptureSave(Managed);
            try
            {
                if (intent.Values.Count > 0)
                {
                    var result = await settings.PutAsync(owner, intent.Values, intent.ETag, Keys, []).ConfigureAwait(false);
                    Publish(owner, () => _form?.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired)); store.Invalidate();
                }
            }
            finally { Publish(owner, () => _form?.EndSave(intent)); }
        }
        var refreshed = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
        Publish(owner, () => { Providers = refreshed; Message = "Sign-in settings saved."; });
    }, write: true);
    public Task SetBindingAsync(AdminSignInProvider captured, bool enabled, bool? autoProvision, bool confirmed) => RunAsync(async owner =>
    {
        if (!confirmed || Capabilities is not { Available: true, LiveProviderChanges: true } || !Providers.Contains(captured)) return;
        var providers = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
        var provider = providers.SingleOrDefault(p => p.Id == captured.Id && p.Auth?.Id == captured.Auth?.Id) ?? throw new AdminAccessException("not_found", 404);
        if (enabled && (!provider.Enabled || ProviderDirty(captured) || !provider.Network && (AdminSignInSchemaRules.Missing(provider).Count > 0 || providers.Any(p => !p.Network && p.Id != provider.Id && p.Enabled && p.Binding?.Enabled == true)))) throw new ArgumentException("Save setup and turn off the other provider first.");
        await api.SaveBindingAsync(owner, provider.Id, Binding(provider, enabled, autoProvision)).ConfigureAwait(false);
        var refreshed = await api.GetInstallationsAsync(owner).ConfigureAwait(false);
        Publish(owner, () => { Providers = refreshed; Message = enabled ? "Sign-in provider turned on." : "Sign-in provider turned off."; });
    }, write: true);
    public Task TestAsync(AdminSignInProvider captured, string? username = null) => RunAsync(async owner =>
    {
        if (!Providers.Contains(captured) || !captured.Enabled || Capabilities?.ConnectionTest != true || captured.Auth?.Metadata["connection_test"]?.ToString() != "true") return;
        var staged = _drafts.Where(d => d.Key.Provider == captured.Id).Select(d => AdminSignInSchemaRules.Build(captured, captured.GlobalConfigSchema.Single(s => s.Key == d.Key.Schema), Values(captured, captured.GlobalConfigSchema.Single(s => s.Key == d.Key.Schema)), d.Value.ClearSecrets)).ToList();
        var testRef = captured.Auth.Metadata["connection_test_username_field"]?.ToString()?.Split('.', 2);
        if (testRef is { Length: 2 } && username != null)
        {
            var schema = captured.GlobalConfigSchema.Single(s => s.Key == testRef[0]);
            var entry = staged.FirstOrDefault(s => s.Key == schema.Key);
            if (entry == null) { entry = AdminSignInSchemaRules.Build(captured, schema, Values(captured, schema), []); staged.Add(entry); }
            entry.Value[testRef[1]] = username; // test-only lookup is never part of a save draft.
        }
        try
        {
            var result = await api.TestAsync(owner, captured.Id, captured.Auth.Id, staged).ConfigureAwait(false);
            Publish(owner, () => { var results = TestResults.ToDictionary(pair => pair.Key, pair => pair.Value); results[captured.Id] = result; TestResults = results; });
        }
        finally { foreach (var entry in staged) entry.Value.Clear(); }
    });
    private static AdminSignInBinding Binding(AdminSignInProvider p, bool? enabled = null, bool? auto = null) => new()
    {
        CapabilityId = p.Auth!.Id, Enabled = enabled ?? p.Binding?.Enabled ?? false, AutoProvision = auto ?? p.Binding?.AutoProvision ?? true,
        DefaultLogin = !p.Network && (p.Binding?.DefaultLogin ?? false), DisplayOrder = p.Binding?.DisplayOrder ?? 1,
    };
}
