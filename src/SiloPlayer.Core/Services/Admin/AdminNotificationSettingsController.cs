using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminNotificationSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loaded { get; init; }
    public bool Loading { get; init; }
    public bool Saving { get; init; }
    public bool SavingDiscord { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool OperationPending { get; init; }
    public string? ActiveOperation { get; init; }
    public string? ActiveTarget { get; init; }
    public bool OperationUnknown { get; init; }
    public bool SensitiveStatusError { get; init; }
    public bool ChannelsLoading { get; init; }
    public bool ChannelsLoaded { get; init; }
    public string? ChannelsError { get; init; }
    public string? NextCursor { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> Configured { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    /// <summary>The same keys with the variable that sets each; drives the row notes.</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public IReadOnlyList<AdminNotificationChannel> Channels { get; init; } = [];
    public string RelayUrl { get; init; } = "https://push.siloserver.org";
    public bool RelayUrlDirty { get; init; }
    public AdminNotificationRelayRegistration? RelayRegistration { get; init; }
    public AdminNotificationTestResult? TestResult { get; init; }
    public AdminNotificationTestResult? DiscordTestResult { get; init; }
    public IReadOnlyDictionary<string, AdminNotificationTestResult> ChannelTestResults { get; init; } = new Dictionary<string, AdminNotificationTestResult>();
    public string? TestTarget { get; init; }
    public bool MailReady => PersistedValues.GetValueOrDefault("email.enabled") == "true" &&
        !string.IsNullOrWhiteSpace(PersistedValues.GetValueOrDefault("email.smtp_host")) && !string.IsNullOrWhiteSpace(PersistedValues.GetValueOrDefault("email.from_address"));
    public bool DiscordReady => !SensitiveStatusError && !string.IsNullOrWhiteSpace(PersistedValues.GetValueOrDefault("discord.client_id")) && Configured.Contains("discord.client_secret") && Configured.Contains("discord.bot_token");
    public bool Dirty => DirtyKeys.Count > 0 || RelayUrlDirty;
    public bool AccessBlocked => Failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;
    public override string ToString() => nameof(AdminNotificationSettingsState);
}

/// <summary>Page authority owns every draft/read/operation. Discord saves a separate subset of the
/// guarded form; relay registration owns its URL. Nothing replays on conflict or an uncertain result.</summary>
public sealed class AdminNotificationSettingsController : IDisposable
{
    public static IReadOnlyList<string> PageKeys { get; } = Array.AsReadOnly(new[]
    {
        "notifications.release_events_enabled", "notifications.fanout_enabled", "notifications.ui_enabled", "notifications.webhooks_enabled", "notifications.web_push_enabled",
        "notifications.apple_push_delivery_enabled", "notifications.android_push_delivery_enabled", "notifications.fanout.settle_seconds", "notifications.fanout.max_event_age_hours",
        "notifications.webhooks.max_per_profile", "notifications.webhooks.deliveries_per_minute_per_profile", "notifications.webhooks.allow_private_destinations",
        "notifications.email_enabled", "notifications.email.allow_per_episode", "notifications.email.digest_hour",
        "notifications.discord_enabled", "notifications.discord.allow_per_episode", "notifications.discord.poster_mode", "notifications.discord.digest_hour",
        "notifications.server_channels_enabled", "notifications.server_channels.batch_seconds", "notifications.server_channels.mention_requesters",
        "notifications.retention.read_days", "notifications.retention.unread_days", "notifications.retention.event_days",
    });
    public static IReadOnlyList<string> DiscordKeys { get; } = Array.AsReadOnly(new[] { "discord.client_id", "discord.client_secret", "discord.bot_token" });
    public static IReadOnlyList<string> ReadOnlyKeys { get; } = Array.AsReadOnly(new[]
    {
        "notifications.push_relay_url", "notifications.push_relay_deployment_id", "notifications.push_relay_expires_at", "notifications.push_relay_key_prefix", "notifications.push_relay_reregistration_required",
        "email.enabled", "email.smtp_host", "email.from_address", "notifications.push_relay_api_key",
    });
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(new[] { "discord.client_secret", "discord.bot_token", "notifications.push_relay_api_key" });
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(PageKeys.Concat(DiscordKeys).Concat(ReadOnlyKeys).ToArray());
    private readonly AdminServerSettingsApi _settings;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminNotificationSettingsApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly HashSet<string> _cursors = new(StringComparer.Ordinal);
    private AdminSettingsForm? _form;
    private AdminNotificationSettingsState _state = new();
    private string? _relayDraft, _signingSecret;
    private bool _disposed;
    public AdminNotificationSettingsController(AdminServerSettingsApi settings, AdminServerSettingsStore store, AdminNotificationSettingsApi api, AdminMonitorAuthority authority)
    {
        _settings = settings; _store = store; _api = api; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminNotificationSettingsState State { get { lock (_gate) return _state; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); } catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _form?.Discard(); _form = null; _relayDraft = _signingSecret = null; _cursors.Clear(); _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke();
        // Entering the page must see current readiness, including changes made on Configure > Email.
        _store.Invalidate();
        await Task.WhenAll(LoadSettings(owner, false), LoadChannels(owner, false)).ConfigureAwait(false);
    }
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (owner)
        {
            lock (_gate) { if (_state.Loading || _state.Saving || _state.OperationPending || _state.ChannelsLoading) return; }
            Update(owner, () => { _state = _state with { Loading = true, Error = null, Failure = null }; if (!preserveDraft) _relayDraft = null; _signingSecret = null; });
            _store.Invalidate();
            await Task.WhenAll(LoadSettings(owner, preserveDraft), LoadChannels(owner, false)).ConfigureAwait(false);
            Update(owner, () => { if (_state.Loaded && _state.Error == null && _state.ChannelsLoaded && _state.ChannelsError == null) _state = _state with { OperationUnknown = false }; });
        }
    }
    private async Task LoadSettings(MonitorAuthorityLease owner, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner); var sensitive = _store.GetSensitiveStatusAsync(owner); var restart = _store.GetRestartKeysAsync(owner);
        AdminSettingsSensitiveStatus? status = null; LoggingRestartKeys restartKeys = new(); var sensitiveError = false;
        try { status = await sensitive.ConfigureAwait(false); } catch (OperationCanceledException) when (!owner.IsCurrent) { return; } catch { sensitiveError = true; }
        try { restartKeys = await restart.ConfigureAwait(false); } catch (OperationCanceledException) when (!owner.IsCurrent) { return; } catch { }
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (_form == null) _form = new(Keys, Secrets, snapshot.Values, snapshot.ETag); else _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                if (sensitiveError) foreach (var key in DiscordKeys) _form.Reset(key);
                // A key the environment sets now loses its draft (a typed Discord secret included), and is never sent.
                var environment = status?.Environment ?? AdminEnvManagedSettings.None;
                _form.ApplyEnvironment(environment);
                _state = _state with { Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, Failure = null,
                    SensitiveStatusError = sensitiveError, Configured = status?.Configured ?? [], ManagedByEnv = environment.Keys, EnvManaged = environment, RestartKeys = restartKeys };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { Update(owner, () => _state = _state with { Loading = false, Loaded = false, Error = "Could not load notification settings. Reload to try again.", Failure = AdminFailures.Classify(error, owner) }); }
    }
    public async Task LoadMoreChannelsAsync()
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (owner)
        {
            lock (_gate) { if (!CanOperate(owner)) return; }
            await LoadChannels(owner, true).ConfigureAwait(false);
        }
    }
    private async Task LoadChannels(MonitorAuthorityLease owner, bool append)
    {
        string? cursor;
        lock (_gate)
        {
            if (!_lifetime.IsCurrent(owner) || _state.ChannelsLoading || append && (!_state.ChannelsLoaded || _state.NextCursor == null || _state.Channels.Count >= 1000)) return;
            cursor = append ? _state.NextCursor : null;
            if (!append) _cursors.Clear();
            _state = _state with { ChannelsLoading = true, ChannelsError = null };
        }
        Changed?.Invoke();
        try
        {
            var page = await _api.GetChannelsAsync(owner, cursor).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (page.NextCursor != null && !_cursors.Add(page.NextCursor)) throw new InvalidDataException("Repeated channel continuation.");
                var rows = append ? _state.Channels.Concat(page.Items).ToArray() : page.Items.ToArray();
                if (rows.Length > 1000 || rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new InvalidDataException("Invalid channel continuation.");
                _state = _state with { Channels = Array.AsReadOnly(rows), NextCursor = page.NextCursor, ChannelsLoading = false, ChannelsLoaded = true, ChannelsError = null,
                    ChannelTestResults = new System.Collections.ObjectModel.ReadOnlyDictionary<string, AdminNotificationTestResult>(_state.ChannelTestResults.Where(p => rows.Any(r => r.Id == p.Key)).ToDictionary(p => p.Key, p => p.Value)) };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { Update(owner, () => _state = _state with { ChannelsLoading = false, ChannelsLoaded = false, ChannelsError = "Could not load server channels. Reload to try again." }); }
    }
    public static string Display(string key, string? raw)
    {
        if (!string.IsNullOrEmpty(raw)) return raw;
        return key switch
        {
            "notifications.email.digest_hour" or "notifications.discord.digest_hour" => "8",
            "notifications.fanout.settle_seconds" => "30", "notifications.fanout.max_event_age_hours" => "72",
            "notifications.webhooks.max_per_profile" => "10", "notifications.webhooks.deliveries_per_minute_per_profile" => "60",
            "notifications.server_channels.batch_seconds" => "300", "notifications.retention.read_days" => "90",
            "notifications.retention.unread_days" => "180", "notifications.retention.event_days" => "30", "notifications.discord.poster_mode" => "provider",
            "notifications.webhooks_enabled" or "notifications.discord_enabled" or "notifications.webhooks.allow_private_destinations" or "notifications.server_channels.mention_requesters" => "false",
            "notifications.apple_push_delivery_enabled" or "notifications.android_push_delivery_enabled" => "false",
            _ when PageKeys.Contains(key) => "true", _ => "",
        };
    }
    public void Set(string key, string value)
    {
        if (!PageKeys.Contains(key) && !DiscordKeys.Contains(key)) throw new ArgumentException("Setting is not editable on this page.", nameof(key));
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || !_state.Loaded || _state.Loading || _state.AccessBlocked || _state.ManagedByEnv.Contains(key) ||
                DiscordKeys.Contains(key) && (_state.Saving || _state.SensitiveStatusError)) return;
            if (Secrets.Contains(key) && value.Length == 0 && _state.Configured.Contains(key)) _form.Reset(key);
            else if (!Secrets.Contains(key) && value == Display(key, _form.GetPersisted(key)) && !_state.Saving) _form.Reset(key);
            else _form.Set(key, value);
        });
    }
    public void SetRelayUrl(string url)
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () => { if (_state.Loaded && !_state.Loading && !_state.AccessBlocked) _relayDraft = url; });
    }
    public void Discard()
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () => { if (!_state.Saving && !_state.OperationPending) { _form?.Discard(); _relayDraft = null; } });
    }
    /// <summary>The foot-of-page Discard affects the page settings, while Discord and relay keep
    /// their independently owned setup drafts. Navigation Discard clears the entire page.</summary>
    public void DiscardPage()
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () => { if (!_state.Saving && !_state.OperationPending && _form != null) foreach (var key in PageKeys) _form.Reset(key); });
    }
    public Task SaveAsync() => Save(false);
    public Task SaveDiscordAsync() => Save(true);
    public Task ClearDiscordAsync() => Save(true, clear: true);
    private async Task Save(bool discord, bool clear = false)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (owner)
        {
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!CanOperate(owner) || _form == null || discord && _state.SensitiveStatusError) return;
                if (clear)
                {
                    if (DiscordKeys.Any(_state.ManagedByEnv.Contains)) return;
                    foreach (var key in DiscordKeys) _form.Set(key, "", true);
                }
                var selected = discord ? DiscordKeys : PageKeys;
                intent = _form.CaptureSave(Keys.Where(k => !selected.Contains(k) || _state.ManagedByEnv.Contains(k)));
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { Saving = true, SavingDiscord = discord, Error = null, Failure = null }; Publish();
            }
            Changed?.Invoke();
            try
            {
                var result = await _settings.PutAsync(owner, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var savedKeys = intent.Values.Keys.ToArray();
                Update(owner, () =>
                {
                    var configured = _state.Configured.ToHashSet(StringComparer.Ordinal);
                    foreach (var key in Secrets) if (intent.Values.TryGetValue(key, out var secret)) { if (secret.Length > 0) configured.Add(key); else configured.Remove(key); }
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false, SavingDiscord = false, Configured = configured.ToArray(), TestResult = discord ? null : _state.TestResult, DiscordTestResult = discord ? null : _state.DiscordTestResult };
                });
                if (_lifetime.IsCurrent(owner)) { _store.Invalidate(); NotifySaved(savedKeys); }
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { }
            catch (Exception error)
            {
                // A clear that was not acknowledged must not stay staged for a later Discord Save.
                Update(owner, () => { _form?.EndSave(intent); if (clear && _form != null) foreach (var key in DiscordKeys) _form.Reset(key);
                    _state = _state with { Saving = false, SavingDiscord = false,
                    Conflict = error is AdminSettingsConflictException, OutcomeUnknown = error is AdminSettingsOutcomeUnknownException,
                    Error = error is AdminSettingsConflictException or AdminSettingsOutcomeUnknownException ? error.Message : "Could not save notification settings.", Failure = AdminFailures.Classify(error, owner) }; });
            }
        }
    }
    private bool CanOperate(MonitorAuthorityLease owner) => _lifetime.IsCurrent(owner) && _state.Loaded && !_state.Loading && !_state.ChannelsLoading && !_state.Saving && !_state.OperationPending &&
        !_state.Conflict && !_state.OutcomeUnknown && !_state.OperationUnknown && !_state.AccessBlocked;

    public Task TestDiscordAsync() => Operate(async owner =>
    {
        var result = await _api.TestDiscordAsync(owner).ConfigureAwait(false);
        Update(owner, () => _state = _state with { TestTarget = "discord", TestResult = result, DiscordTestResult = result });
    }, requireDiscord: true, kind: "discord-test");
    public Task RegisterRelayAsync()
    {
        // Capture the submitted draft before the await; registration never overwrites newer typing.
        string submitted; lock (_gate) submitted = _state.RelayUrl.Trim();
        return Operate(async owner =>
        {
            var result = await _api.RegisterRelayAsync(owner, submitted).ConfigureAwait(false);
            Update(owner, () => { if ((_relayDraft ?? _state.RelayUrl).Trim() == submitted) _relayDraft = null; _state = _state with { RelayRegistration = result }; });
        }, refreshSettings: true, relay: true, kind: "relay-register");
    }
    public Task ClearRelayAsync() => Operate(async owner =>
    {
        await _api.ClearRelayAsync(owner).ConfigureAwait(false);
        Update(owner, () => _state = _state with { RelayRegistration = null });
    }, refreshSettings: true, relay: true, kind: "relay-clear");
    /// <summary>False when the command was refused and nothing was sent.</summary>
    public Task<bool> SaveChannelAsync(string? id, Dictionary<string, object> input)
    {
        var captured = new Dictionary<string, object>(input, StringComparer.Ordinal);
        return Operate(async owner =>
        {
            var receipt = await _api.SaveChannelAsync(owner, id, captured).ConfigureAwait(false);
            Update(owner, () => _signingSecret = receipt.SigningSecret);
        }, id: id, refreshChannels: true, kind: "channel-save");
    }
    public Task TestChannelAsync(string id) => Operate(async owner =>
    {
        var result = await _api.TestChannelAsync(owner, id).ConfigureAwait(false);
        Update(owner, () =>
        {
            var results = new Dictionary<string, AdminNotificationTestResult>(_state.ChannelTestResults) { [id] = result };
            _state = _state with { TestTarget = id, TestResult = result, ChannelTestResults = new System.Collections.ObjectModel.ReadOnlyDictionary<string, AdminNotificationTestResult>(results) };
        });
    }, id: id, kind: "channel-test");
    public Task RotateChannelAsync(string id) => Operate(async owner =>
    {
        var secret = await _api.RotateChannelAsync(owner, id).ConfigureAwait(false); Update(owner, () => _signingSecret = secret);
    }, id: id, genericOnly: true, refreshChannels: true, kind: "channel-rotate");
    public Task DeleteChannelAsync(string id) => Operate(owner => _api.DeleteChannelAsync(owner, id), id: id, refreshChannels: true, kind: "channel-delete");
    private async Task<bool> Operate(Func<MonitorAuthorityLease, Task> operation, string? id = null, bool requireDiscord = false, bool genericOnly = false, bool refreshChannels = false, bool refreshSettings = false, bool relay = false, string? kind = null)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return false; }
        using (owner)
        {
            lock (_gate)
            {
                if (!CanOperate(owner) || requireDiscord && (!_state.DiscordReady || _state.DirtyKeys.Any(DiscordKeys.Contains))) return false;
                if (refreshChannels && !_state.ChannelsLoaded) return false;
                if (id != null && (!_state.ChannelsLoaded || !_state.Channels.Any(c => c.Id == id && (!genericOnly || c.Type == "generic")))) return false;
                if (relay && (_state.SensitiveStatusError || ReadOnlyKeys.Where(k => k.StartsWith("notifications.push_relay_", StringComparison.Ordinal)).Any(_state.ManagedByEnv.Contains))) return false;
                _signingSecret = null;
                _state = _state with { OperationPending = true, ActiveOperation = kind, ActiveTarget = id, Error = null, Failure = null, TestResult = null, TestTarget = null,
                    DiscordTestResult = kind == "discord-test" ? null : _state.DiscordTestResult,
                    RelayRegistration = relay ? null : _state.RelayRegistration };
            }
            Changed?.Invoke();
            try
            {
                await operation(owner).ConfigureAwait(false);
                if (refreshSettings) { _store.Invalidate(); await LoadSettings(owner, true).ConfigureAwait(false); }
                if (refreshChannels) await LoadChannels(owner, false).ConfigureAwait(false);
                Update(owner, () => _state = _state with { OperationPending = false, ActiveOperation = null, ActiveTarget = null });
                if (refreshSettings && _lifetime.IsCurrent(owner)) NotifySaved([]);
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { }
            catch (Exception error)
            {
                Update(owner, () => _state = _state with { OperationPending = false, ActiveOperation = null, ActiveTarget = null, OperationUnknown = error is AdminNotificationOperationUnknownException,
                    Error = error is AdminNotificationOperationUnknownException ? error.Message : error is ArgumentException ? "Check the notification setup fields." : "Notification operation failed. Reload to review the server state.", Failure = AdminFailures.Classify(error, owner) });
            }
            return true;
        }
    }
    /// <summary>Whether a minted signing secret still waits for the current owner to take and show it.</summary>
    public bool HasSigningSecret { get { lock (_gate) return _signingSecret != null && _state.Owner?.IsCurrent == true; } }
    public string? TakeSigningSecret()
    {
        lock (_gate) { var result = _state.Owner?.IsCurrent == true ? _signingSecret : null; _signingSecret = null; return result; }
    }
    /// <summary>The write whose answer a retirement would drop, or null. A delivery test changes no
    /// server configuration, so its dropped result is not carried.</summary>
    public static string? InterruptedWrite(AdminNotificationSettingsState state) =>
        state.Saving ? "save" : state.OperationPending && state.ActiveOperation is not ("discord-test" or "channel-test") ? state.ActiveOperation ?? "operation" : null;
    /// <summary>Carries what a host hide retired into the fresh owner. A write that may have reached the
    /// server blocks commands like any unknown outcome until an explicit successful Reload.</summary>
    public void RestoreInterrupted(string? write, bool credentials)
    {
        var owner = _lifetime.Owner; if (owner == null || write == null && !credentials) return;
        var error = write == null ? "" : "The window was hidden before the server answered, so the outcome is unknown. Reload and review before taking another action.";
        if (write is "channel-save" or "channel-rotate") error += " If a destination was created or its secret was rotated, the signing secret was not shown. Rotate the secret to get a new one.";
        if (credentials) error += (error.Length > 0 ? " " : "") + "Credentials that were typed but not saved were cleared when the window was hidden. Enter them again.";
        Update(owner, () =>
        {
            if (!_state.Loaded || _state.Loading) return;
            _state = _state with { OutcomeUnknown = _state.OutcomeUnknown || write == "save", OperationUnknown = _state.OperationUnknown || write is not (null or "save"), Error = error };
        });
    }
    private void NotifySaved(IReadOnlyCollection<string> keys)
    {
        if (Saved is not { } handlers) return;
        foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList()) try { handler(keys); } catch { }
    }
    private void Publish()
    {
        if (_form == null) return;
        var relay = _form.Get("notifications.push_relay_url"); if (relay.Length == 0) relay = "https://push.siloserver.org";
        _state = _state with
        {
            Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(k => k, _form.Get, StringComparer.Ordinal)),
            PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.Except(Secrets).ToDictionary(k => k, _form.GetPersisted, StringComparer.Ordinal)),
            DirtyKeys = _form.DirtyKeys, RelayUrl = _relayDraft ?? relay, RelayUrlDirty = _relayDraft != null && _relayDraft != relay,
        };
    }
    private void Update(MonitorAuthorityLease owner, Action action)
    {
        lock (_gate) { if (_disposed || !_lifetime.IsCurrent(owner)) return; action(); Publish(); }
        Changed?.Invoke();
    }
    private void Clear()
    {
        lock (_gate) { _form?.Discard(); _form = null; _relayDraft = _signingSecret = null; _cursors.Clear(); _state = new(); }
        Changed?.Invoke();
    }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
