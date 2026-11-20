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
    public bool ReviewRequired { get; init; }
    public string? Error { get; init; }
    public string? Feedback { get; init; }
    public AdminPlaybackNode? CreatedNode { get; init; }
    public AdminPlaybackNodeCapabilities Capabilities { get; init; } = new();
    public AdminPlaybackNodeWindow Window { get; init; } = new(Array.Empty<AdminPlaybackNode>(), false);
    public override string ToString() => nameof(AdminPlaybackNodesState);
}

/// <summary>One page owner, serialized reads/writes; captured row targets never change under a confirmation.</summary>
public sealed class AdminPlaybackNodesController : IDisposable
{
    private readonly AdminPlaybackAdministrationApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
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
            catch { Update(owner, () => _state = _state with { Loading = false, ReviewRequired = true, Error = "Nodes could not be loaded. Reload before editing." }); }
        }
    }
    private async Task<AdminPlaybackNodeCapabilities> ReadCapabilities(MonitorAuthorityLease owner)
    {
        try { return await _api.CapabilitiesAsync(owner, owner.Token).ConfigureAwait(false); }
        catch { return new(); } // Unsupported/failed discovery cannot enable newer actions.
    }
    public Task<bool> CreateAsync(IReadOnlyDictionary<string, object?> values) => Execute(null, async owner =>
    {
        if (values.GetValueOrDefault("source_access") is "core" && !State.Capabilities.SourceAccess) throw new InvalidOperationException();
        var saved = await _api.CreateNodeAsync(owner, values, owner.Token).ConfigureAwait(false);
        Update(owner, () => _state = _state with { CreatedNode = saved });
        try { await _api.CommandAsync(owner, saved, "check", owner.Token).ConfigureAwait(false); } catch { /* Stored; health sweep still follows. */ }
        return AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.saved");
    });
    public Task<bool> UpdateAsync(AdminPlaybackNode node, IReadOnlyDictionary<string, object?> values) => Execute(node, async owner =>
    {
        if (values.ContainsKey("source_access") && !State.Capabilities.SourceAccess) throw new InvalidOperationException();
        await _api.UpdateNodeAsync(owner, node, values, owner.Token).ConfigureAwait(false);
        return AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.saved");
    });
    public Task<bool> DeleteAsync(AdminPlaybackNode node) => Execute(node, async owner =>
    {
        await _api.DeleteNodeAsync(owner, node, owner.Token).ConfigureAwait(false);
        return AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.deleted");
    });
    public Task<bool> CommandAsync(AdminPlaybackNode node, string command) => Execute(node, async owner =>
    {
        if (command is "drain" or "resume" && !(State.Capabilities.Available && State.Capabilities.Drain)) throw new InvalidOperationException();
        var result = await _api.CommandAsync(owner, node, command, owner.Token).ConfigureAwait(false);
        return command switch
        {
            "check" => result.HealthPersisted != true ? AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.health.notStored")
                : AdminPlaybackAdministrationText.T(result.Healthy == true ? "web.admin.nodes.hooks.nodes.health.healthy" : "web.admin.nodes.hooks.nodes.health.unhealthy", new { name = node.Name }),
            "reprobe" => result.Status == "ok" && result.CapabilitiesRefreshed == true ? "The node re-probed its hardware and the server refreshed its inventory." : "The node re-probe did not confirm a refreshed inventory. Review its current report.",
            _ => AdminPlaybackAdministrationText.T(result.DrainRequested == true ? "web.admin.nodes.hooks.nodes.drain.draining" : "web.admin.nodes.hooks.nodes.drain.resumed", new { name = node.Name }),
        };
    });
    public Task<bool> IssueKeyAsync(AdminPlaybackNode node, Action<string> reveal) => Execute(node, async owner =>
    {
        if (!(State.Capabilities.Available && State.Capabilities.NodeKeys)) throw new InvalidOperationException();
        await _api.IssueKeyAsync(owner, node, key => { if (_lifetime.IsCurrent(owner)) reveal(key); }, owner.Token).ConfigureAwait(false);
        return "";
    });
    private async Task<bool> Execute(AdminPlaybackNode? target, Func<MonitorAuthorityLease, Task<string>> operation)
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return false; }
        using (owner)
        {
            lock (_gate)
            {
                if (_disposed || !_lifetime.IsCurrent(owner) || !_state.Loaded || _state.Loading || _state.Busy || _state.ReviewRequired) return false;
                if (target != null && !_state.Window.Items.Any(row => row.Id == target.Id && row.ConfigETag == target.ConfigETag)) return false;
                _state = _state with { Busy = true, Error = null, Feedback = null };
            }
            Changed?.Invoke();
            try
            {
                var feedback = await operation(owner).ConfigureAwait(false);
                if (!_lifetime.IsCurrent(owner)) return false;
                Update(owner, () => _state = _state with { Busy = false, Feedback = feedback });
                await ReloadAsync().ConfigureAwait(false); return _lifetime.IsCurrent(owner);
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { return false; }
            catch (AdminSettingsConflictException)
            {
                Update(owner, () => _state = _state with { Busy = false, ReviewRequired = true, Error = "This node changed on the server. Close the dialog, reload and reopen it to review the current configuration." });
            }
            catch (ApiException error) when (error.StatusCode is >= 400 and < 500 && error.StatusCode != 408)
            {
                Update(owner, () => _state = _state with { Busy = false, Error = "The server refused this node action. Review permissions, capabilities and values before trying again." });
            }
            catch
            {
                Update(owner, () => _state = _state with { Busy = false, ReviewRequired = true, Error = AdminPlaybackAdministrationText.T("web.admin.nodes.hooks.nodes.changeUnconfirmed") });
            }
            return false;
        }
    }
    private void Update(MonitorAuthorityLease owner, Action update) { lock (_gate) { if (_disposed || !_lifetime.IsCurrent(owner)) return; update(); } Changed?.Invoke(); }
    private void Clear() { lock (_gate) _state = new(); Changed?.Invoke(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    public void Dispose() { lock (_gate) _disposed = true; _lifetime.Dispose(); Clear(); Changed = null; }
}
