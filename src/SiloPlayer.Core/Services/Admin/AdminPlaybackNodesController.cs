using System.Collections.Immutable;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminPlaybackNodesState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Busy { get; init; }
    /// <summary>Ids of the nodes whose re-probe has not answered yet. Unlike <see cref="Busy"/>, it holds back nothing but another re-probe of the same node.</summary>
    public ImmutableHashSet<string> Reprobing { get; init; } = ImmutableHashSet<string>.Empty;
    public bool ReviewRequired { get; init; }
    public string? Error { get; init; }
    public string? Feedback { get; init; }
    public AdminPlaybackNode? CreatedNode { get; init; }
    public AdminPlaybackNodeCapabilities Capabilities { get; init; } = new();
    public AdminPlaybackNodeWindow Window { get; init; } = new(Array.Empty<AdminPlaybackNode>(), false);
    public override string ToString() => nameof(AdminPlaybackNodesState);
}

/// <summary>What became of an action: nothing was sent, the server refused it (so nothing changed), or it was sent and its outcome is not known.</summary>
public enum AdminPlaybackNodeOutcome { Done, NotSent, Refused, Unknown }

/// <summary>Thrown by an action's own guard before anything is sent (a capability the server does not offer).</summary>
internal sealed class AdminPlaybackNodeNotSentException : InvalidOperationException;

/// <summary>One page owner, serialized reads/writes; captured row targets never change under a confirmation.</summary>
public sealed class AdminPlaybackNodesController : IDisposable
{
    private readonly AdminPlaybackAdministrationApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private const string Refused = "The server refused this node action. Review permissions, capabilities and values before trying again.";
    private AdminPlaybackNodesState _state = new();
    private bool _disposed;
    public AdminPlaybackNodesController(AdminPlaybackAdministrationApi api, AdminMonitorAuthority authority)
    {
        _api = api; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    public AdminPlaybackNodesState State { get { lock (_gate) return _state; } }
    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); } catch (OperationCanceledException) { Clear(); return; }
        Update(owner, () => _state = new() { Owner = owner });
        await ReloadAsync().ConfigureAwait(false);
    }
    public async Task ReloadAsync()
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (owner)
        {
            lock (_gate)
            {
                if (_disposed || !_lifetime.IsCurrent(owner) || _state.Busy || _state.Loading) return;
                _state = _state with { Loading = true };
            }
            Changed?.Invoke();
            var caps = ReadCapabilities(owner);
            try
            {
                var window = await _api.ListNodesAsync(owner, owner.Token).ConfigureAwait(false);
                var capabilities = await caps.ConfigureAwait(false);
                Update(owner, () => _state = _state with { Loading = false, Loaded = true, Window = window, Capabilities = capabilities, Error = null, ReviewRequired = false });
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { }
            catch
            {
                // Like the official page, a failed background refresh keeps the rows on screen and keeps polling. Controls stay blocked only when nothing was ever loaded or a write's outcome is still unknown.
                Update(owner, () => _state = _state with
                {
                    Loading = false, ReviewRequired = _state.ReviewRequired || !_state.Loaded,
                    Error = _state.Loaded && !_state.ReviewRequired ? "Nodes could not be refreshed. Showing the last loaded data." : "Nodes could not be loaded. Reload before editing.",
                });
            }
        }
    }
    private async Task<AdminPlaybackNodeCapabilities> ReadCapabilities(MonitorAuthorityLease owner)
    {
        try { return await _api.CapabilitiesAsync(owner, owner.Token).ConfigureAwait(false); }
        catch { return new(); } // Unsupported/failed discovery cannot enable newer actions.
    }
    public Task<bool> CreateAsync(IReadOnlyDictionary<string, object?> values) => Execute(null, async owner =>
    {
        if (values.GetValueOrDefault("source_access") is "core" && !State.Capabilities.SourceAccess) throw new AdminPlaybackNodeNotSentException();
        var saved = await _api.CreateNodeAsync(owner, values, owner.Token).ConfigureAwait(false);
        Update(owner, () => _state = _state with { CreatedNode = saved });
        try { await _api.CommandAsync(owner, saved, "check", owner.Token).ConfigureAwait(false); } catch { /* Stored; health sweep still follows. */ }
        return AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.saved");
    });
    /// <summary>The note the official page shows when a deployment-managed node's delete is pressed: the node is the deployment's, not this page's.</summary>
    public static string DeploymentManagedNote => AdminPlaybackAdministrationText.T("web.admin.nodes.unit.deleteDeploymentManaged");
    /// <summary>
    /// An action the page does not offer is never sent. It is reported through the feedback line and changes nothing else: no busy state, no review lock, and an error
    /// that is already showing (a refusal's reason, a failed refresh and its Retry) stays.
    /// </summary>
    private Task<AdminPlaybackNodeOutcome> NotOffered(string note)
    {
        lock (_gate) { if (_disposed) return Task.FromResult(AdminPlaybackNodeOutcome.NotSent); _state = _state with { Feedback = note }; }
        Changed?.Invoke(); return Task.FromResult(AdminPlaybackNodeOutcome.NotSent);
    }
    public async Task<bool> UpdateAsync(AdminPlaybackNode node, IReadOnlyDictionary<string, object?> values)
        => await (values.Keys.Any(node.IsEnvManaged) ? NotOffered(AdminPlaybackAdministrationText.T("web.admin.nodes.form.envManaged")) : Run(node, async owner =>
    {
        if (values.ContainsKey("source_access") && !State.Capabilities.SourceAccess) throw new AdminPlaybackNodeNotSentException();
        await _api.UpdateNodeAsync(owner, node, values, owner.Token).ConfigureAwait(false);
        return AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.saved");
    })) == AdminPlaybackNodeOutcome.Done;
    public async Task<bool> DeleteAsync(AdminPlaybackNode node) => await (node.IsDeploymentManaged ? NotOffered(DeploymentManagedNote) : Run(node, async owner =>
    {
        await _api.DeleteNodeAsync(owner, node, owner.Token).ConfigureAwait(false);
        return AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.deleted");
    })) == AdminPlaybackNodeOutcome.Done;
    public Task<bool> CommandAsync(AdminPlaybackNode node, string command) => command == "reprobe" ? ReprobeAsync(node) : Execute(node, async owner =>
    {
        if (command is "drain" or "resume" && !(State.Capabilities.Available && State.Capabilities.Drain)) throw new AdminPlaybackNodeNotSentException();
        var result = await _api.CommandAsync(owner, node, command, owner.Token).ConfigureAwait(false);
        return command switch
        {
            "check" => result.HealthPersisted != true ? AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.health.notStored")
                : AdminPlaybackAdministrationText.T(result.Healthy == true ? "web.admin.nodes.hooks.nodes.health.healthy" : "web.admin.nodes.hooks.nodes.health.unhealthy", new { name = AdminPlaybackNodeText.Plain(node.Name) }),
            _ => AdminPlaybackAdministrationText.T(result.DrainRequested == true ? "web.admin.nodes.hooks.nodes.drain.draining" : "web.admin.nodes.hooks.nodes.drain.resumed", new { name = AdminPlaybackNodeText.Plain(node.Name) }),
        };
    });
    // A re-probe answers only after the node has probed its hardware, which can take minutes, and it changes no configuration. So it is pending for its own node only and never Busy:
    // other actions, the refresh and leaving the page go on meanwhile. The page refreshes the rows when it ends, and an unconfirmed outcome is reported for that refresh to settle instead of blocking writes.
    private async Task<bool> ReprobeAsync(AdminPlaybackNode node)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return false; }
        using (owner)
        {
            lock (_gate)
            {
                if (_disposed || !_lifetime.IsCurrent(owner) || !_state.Loaded || _state.Loading || _state.Busy || _state.ReviewRequired || _state.Reprobing.Contains(node.Id)) return false;
                if (!_state.Window.Items.Any(row => row.Id == node.Id && row.ConfigETag == node.ConfigETag)) return false;
                _state = _state with { Reprobing = _state.Reprobing.Add(node.Id), Error = null, Feedback = null };
            }
            Changed?.Invoke();
            string? feedback = null, refusal = null; var acknowledged = false;
            try
            {
                var result = await _api.CommandAsync(owner, node, "reprobe", owner.Token).ConfigureAwait(false);
                acknowledged = true;
                // Official describeReprobeOutcome: whether the driver work landed, against the hash stored before the call.
                feedback = AdminPlaybackNodePresentation.ReprobeOutcome(node, result).Message;
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { return false; }
            catch (ApiException error) when (error.StatusCode is >= 400 and < 500 && error.StatusCode != 408) { refusal = AdminPlaybackNodeText.Reason(error, Refused); }
            catch { feedback = AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.commandUnconfirmed"); }
            // Another action may have run meanwhile: a write whose outcome is unknown keeps its own message until the admin reloads.
            Update(owner, () => _state = _state with { Reprobing = _state.Reprobing.Remove(node.Id), Error = refusal != null && !_state.ReviewRequired ? refusal : _state.Error, Feedback = feedback ?? _state.Feedback });
            return acknowledged && _lifetime.IsCurrent(owner);
        }
    }
    public async Task<bool> IssueKeyAsync(AdminPlaybackNode node, Action<string> reveal) => await IssueKeyOutcomeAsync(node, reveal) == AdminPlaybackNodeOutcome.Done;
    /// <summary>Like <see cref="IssueKeyAsync"/>, and says whether a request was sent at all: a key dialog tells "nothing was sent" apart from "sent, outcome unknown".</summary>
    public Task<AdminPlaybackNodeOutcome> IssueKeyOutcomeAsync(AdminPlaybackNode node, Action<string> reveal) => node.IsDeploymentManaged ? NotOffered(AdminPlaybackAdministrationText.T("web.admin.nodes.key.deploymentManaged")) : Run(node, async owner =>
    {
        if (!(State.Capabilities.Available && State.Capabilities.NodeKeys)) throw new AdminPlaybackNodeNotSentException();
        await _api.IssueKeyAsync(owner, node, key => { if (_lifetime.IsCurrent(owner)) reveal(key); }, owner.Token).ConfigureAwait(false);
        return "";
    });
    private async Task<bool> Execute(AdminPlaybackNode? target, Func<MonitorAuthorityLease, Task<string>> operation) => await Run(target, operation).ConfigureAwait(false) == AdminPlaybackNodeOutcome.Done;
    private async Task<AdminPlaybackNodeOutcome> Run(AdminPlaybackNode? target, Func<MonitorAuthorityLease, Task<string>> operation)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return AdminPlaybackNodeOutcome.NotSent; }
        using (owner)
        {
            // Nothing is sent when the page is busy, not loaded, awaiting a review, or when the row changed under the open confirmation.
            lock (_gate)
            {
                if (_disposed || !_lifetime.IsCurrent(owner) || !_state.Loaded || _state.Loading || _state.Busy || _state.ReviewRequired) return AdminPlaybackNodeOutcome.NotSent;
                if (target != null && !_state.Window.Items.Any(row => row.Id == target.Id && row.ConfigETag == target.ConfigETag)) return AdminPlaybackNodeOutcome.NotSent;
                _state = _state with { Busy = true, Error = null, Feedback = null };
            }
            Changed?.Invoke();
            try
            {
                var feedback = await operation(owner).ConfigureAwait(false);
                if (!_lifetime.IsCurrent(owner)) return AdminPlaybackNodeOutcome.Unknown;
                Update(owner, () => _state = _state with { Busy = false, Feedback = feedback });
                await ReloadAsync().ConfigureAwait(false); return _lifetime.IsCurrent(owner) ? AdminPlaybackNodeOutcome.Done : AdminPlaybackNodeOutcome.Unknown;
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { return AdminPlaybackNodeOutcome.Unknown; }
            catch (AdminSettingsConflictException)
            {
                Update(owner, () => _state = _state with { Busy = false, ReviewRequired = true, Error = "This node changed on the server. Close the dialog, reload and reopen it to review the current configuration." });
                return AdminPlaybackNodeOutcome.Refused;
            }
            catch (AdminPlaybackNodeNotSentException)
            {
                // The action's own guard refused before any request: nothing changed and nothing is left to review.
                Update(owner, () => _state = _state with { Busy = false, Error = Refused });
                return AdminPlaybackNodeOutcome.NotSent;
            }
            catch (AdminPlaybackNodeActionNotOfferedException)
            {
                Update(owner, () => _state = _state with { Busy = false, Error = Refused });
                return AdminPlaybackNodeOutcome.NotSent;
            }
            catch (ApiException error) when (error.StatusCode is >= 400 and < 500 && error.StatusCode != 408)
            {
                // A definite refusal: nothing was changed, the admin's draft stays, and the server's reason is shown.
                var reason = AdminPlaybackNodeText.Reason(error, Refused);
                Update(owner, () => _state = _state with { Busy = false, Error = reason });
                return AdminPlaybackNodeOutcome.Refused;
            }
            catch
            {
                Update(owner, () => _state = _state with { Busy = false, ReviewRequired = true, Error = AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.changeUnconfirmed") });
                return AdminPlaybackNodeOutcome.Unknown;
            }
        }
    }
    private void Update(MonitorAuthorityLease owner, Action update) { lock (_gate) { if (_disposed || !_lifetime.IsCurrent(owner)) return; update(); } Changed?.Invoke(); }
    private void Clear() { lock (_gate) _state = new(); Changed?.Invoke(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { lock (_gate) _disposed = true; _lifetime.Dispose(); Clear(); Changed = null; }
}
