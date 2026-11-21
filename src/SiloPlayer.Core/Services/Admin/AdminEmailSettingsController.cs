using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Mailbox shape check from the official WebUI <c>lib/email.ts</c>: one "@" with text
/// before it, no whitespace, and a domain whose last dot has text on both sides, or a
/// bracketed dotted IPv4 / bare IPv6 literal. The server remains the authority.
/// </summary>
public static partial class AdminEmailAddress
{
    [GeneratedRegex(@"^(25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])){3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4();
    [GeneratedRegex(@"^[0-9a-fA-F:]*:[0-9a-fA-F:.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6();

    /// <summary>JavaScript <c>String.prototype.trim</c>: Unicode white space plus BOM.</summary>
    public static string Trim(string? value) => (value ?? "").Trim().Trim('﻿').Trim();

    public static bool IsValid(string? value)
    {
        var trimmed = Trim(value);
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at != trimmed.IndexOf('@') || trimmed.Any(char.IsWhiteSpace)) return false;
        var domain = trimmed[(at + 1)..];
        if (domain.StartsWith('[')) return IsDomainLiteral(domain);
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    private static bool IsDomainLiteral(string domain)
    {
        if (!domain.EndsWith(']')) return false;
        var inner = domain[1..^1];
        return Ipv4().IsMatch(inner) || (inner.Length > 1 && Ipv6().IsMatch(inner));
    }
}

public enum AdminEmailTestOutcome { Delivered, Rejected, InvalidRecipient, RequestFailed, Unknown }

/// <summary>Result of one explicit Send test command. <see cref="Message"/> is redacted server text for a rejected test.</summary>
public sealed record AdminEmailTestFeedback(AdminEmailTestOutcome Outcome, long DurationMs = 0, string? Message = null, AdminFailureKind? Failure = null, string? ErrorCode = null)
{
    public override string ToString() => nameof(AdminEmailTestFeedback);
}

public sealed record AdminEmailSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    /// <summary>Saved-password presence could not be read; the credential editor stays protected until a retry succeeds.</summary>
    public bool SensitiveStatusError { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>Canonical acknowledged nonsecret values, separate from newer staged edits.</summary>
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public bool PasswordConfigured { get; init; }
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    /// <summary>The same keys with the variable that sets each; drives the row notes.</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public bool TestPending { get; init; }
    public AdminEmailTestFeedback? TestResult { get; init; }
    public override string ToString() => nameof(AdminEmailSettingsState);
}

/// <summary>Page-owned Email settings state. Reuses the shared settings form/store/API. Events may arrive on any thread.</summary>
public sealed class AdminEmailSettingsController : IDisposable
{
    public const string Password = "email.smtp_password";
    public const string Security = "email.smtp_security";
    /// <summary>Displayed when no security mode is saved; selecting it does not stage an edit.</summary>
    public const string DefaultSecurity = "starttls";
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { "email.enabled", "email.from_address", "email.from_name", "email.smtp_host", "email.smtp_port", Security, "email.smtp_username", Password });
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(new[] { Password });
    private readonly AdminServerSettingsApi _api;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminEmailSettingsState _state = new();
    private bool _disposed;

    public AdminEmailSettingsController(AdminServerSettingsApi api, AdminServerSettingsStore store, AdminMonitorAuthority authority)
    {
        _api = api; _store = store; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    /// <summary>Raised after an acknowledged save so the shell can refresh restart status.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminEmailSettingsState State { get { lock (_gate) return _state; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _form?.Discard(); _form = null; _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke();
        await LoadAsync(owner, false, false).ConfigureAwait(false);
    }

    /// <summary>Explicit reconciliation; also the retry for a failed saved-password status read.</summary>
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; }
            _store.Invalidate();
            Update(action, () => _state = _state with { Loading = true, Error = null });
            await LoadAsync(action, true, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(MonitorAuthorityLease owner, bool reload, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner);
        var sensitive = _store.GetSensitiveStatusAsync(owner);
        var restart = _store.GetRestartKeysAsync(owner);
        AdminSettingsSensitiveStatus? status = null;
        LoggingRestartKeys restartKeys = new();
        var sensitiveError = false;
        try { status = await sensitive.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { sensitiveError = true; }
        try { restartKeys = await restart.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { }
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (reload && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(Keys, Secrets, snapshot.Values, snapshot.ETag);
                // Without presence the page cannot tell keep from clear; never keep a credential draft across that gap.
                if (sensitiveError) _form.Reset(Password);
                // A key the environment sets now loses its draft (a typed password included), and is never sent.
                var environment = status?.Environment ?? AdminEnvManagedSettings.None;
                _form.ApplyEnvironment(environment);
                _state = _state with
                {
                    Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ErrorCode = null, Failure = null,
                    SensitiveStatusError = sensitiveError, PasswordConfigured = status?.Configured.Contains(Password) == true,
                    ManagedByEnv = environment.Keys, EnvManaged = environment, RestartKeys = restartKeys,
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Try again.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner) }); }
    }

    private bool CanEdit(string key) => _form != null && _state.Loaded && !_state.Loading && !_state.ManagedByEnv.Contains(key) &&
        (key != Password || (!_state.Saving && !_state.SensitiveStatusError));

    public void Set(string key, string value)
    {
        if (key == Password) { SetPassword(value); return; }
        Edit(key, form =>
        {
            // The select shows STARTTLS for an empty saved value; choosing it again is not an edit.
            if (key == Security && !_state.Saving && value == DisplaySecurity(form.GetPersisted(key))) form.Reset(key);
            else form.Set(key, value);
        });
    }

    /// <summary>Typing stages a replacement. Emptying the input keeps a saved password; with none saved the empty value stays staged.</summary>
    public void SetPassword(string value) => Edit(Password, form =>
    {
        if (value.Length == 0 && _state.PasswordConfigured) form.Reset(Password);
        else form.Set(Password, value);
    });
    /// <summary>Explicitly stages erasing the saved password (for relays that need no sign-in).</summary>
    public void ClearPassword() => Edit(Password, form => { if (_state.PasswordConfigured) form.Set(Password, "", stage: true); });
    public void KeepPassword() => Edit(Password, form => form.Reset(Password));
    public static string DisplaySecurity(string? value) => string.IsNullOrEmpty(value) ? DefaultSecurity : value;

    private void Edit(string key, Action<AdminSettingsForm> edit)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (CanEdit(key)) edit(_form!); });
    }

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Saving) _form?.Discard(); });
    }

    public async Task SaveAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Loading || _state.Saving || _state.Conflict || _state.OutcomeUnknown) return;
                var excluded = _state.SensitiveStatusError ? _state.ManagedByEnv.Append(Password) : _state.ManagedByEnv;
                intent = _form.CaptureSave(excluded);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { Saving = true, Error = null, ErrorCode = null, Failure = null };
            }
            Changed?.Invoke();
            string[]? savedKeys = null;
            try
            {
                var result = await _api.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var keys = intent.Values.Keys.ToArray();
                bool? passwordPresent = intent.Values.TryGetValue(Password, out var submitted) ? submitted.Length > 0 : null;
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false, PasswordConfigured = passwordPresent ?? _state.PasswordConfigured };
                });
                if (_lifetime.IsCurrent(action)) { _store.Invalidate(); savedKeys = keys; }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; }); }
            catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; }); }
            catch (Exception error) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "Failed to update settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) }; }); }
            if (savedKeys != null && _lifetime.IsCurrent(action) && Saved is { } handlers)
                foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                    try { handler(savedKeys); } catch { /* An observer cannot change an acknowledged write. */ }
        }
    }

    /// <summary>
    /// One explicit test through the server's SAVED settings; staged edits are never sent.
    /// The recipient is captured and trimmed here. Duplicate activation is ignored while
    /// pending, and an unknown outcome is reported rather than retried.
    /// </summary>
    public async Task SendTestAsync(string recipient)
    {
        var to = AdminEmailAddress.Trim(recipient);
        if (to.Length == 0) return;
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            bool send;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || !_state.Loaded || _state.TestPending) return;
                // Malformed input is reported before any request, as in the official row.
                send = AdminEmailAddress.IsValid(to);
                _state = send ? _state with { TestPending = true, TestResult = null } : _state with { TestResult = new(AdminEmailTestOutcome.InvalidRecipient) };
            }
            Changed?.Invoke();
            if (!send) return;
            AdminEmailTestFeedback feedback;
            try
            {
                var result = await _api.SendTestEmailAsync(action, to).ConfigureAwait(false);
                feedback = result.Ok ? new(AdminEmailTestOutcome.Delivered, result.DurationMs) : new(AdminEmailTestOutcome.Rejected, result.DurationMs, result.Message);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminEmailTestOutcomeUnknownException) { feedback = new(AdminEmailTestOutcome.Unknown); }
            catch (Exception error) { feedback = new(AdminEmailTestOutcome.RequestFailed, Failure: AdminFailures.Classify(error, action), ErrorCode: error is ApiException api ? api.ProblemType ?? api.ErrorCode : null); }
            Update(action, () => _state = _state with { TestPending = false, TestResult = feedback });
        }
    }

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { _form?.Discard(); _form = null; _state = new(); }
            else if (_form != null) _state = _state with
            {
                Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
                PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.Except(Secrets, StringComparer.Ordinal).ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
                DirtyKeys = Array.AsReadOnly(_form.DirtyKeys.ToArray()), RestartRequired = _form.RestartRequired,
            };
        }
        Changed?.Invoke();
    }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _state = new(); } Changed?.Invoke(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
