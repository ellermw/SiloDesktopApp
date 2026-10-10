using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Owns a server shuffle separately from decoder/session recovery.</summary>
public sealed class ShufflePlaybackController(ShufflesApi api, SiloApiClient client)
{
    private long _revision;
    private long _readRevision;
    private long? _pendingMutationRevision;
    private readonly object _gate = new();
    private ApiRequestContext? _context;
    public string? Id { get; private set; }
    public Shuffle? Snapshot { get; private set; }
    public bool Exhausted { get; private set; }
    public bool IsActive
    {
        get { lock (_gate) return Id != null && _context is { } context && client.IsCurrentContext(context); }
    }

    public async Task<Shuffle> StartAsync(ShuffleScopeRequest scope, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        long revision;
        lock (_gate) revision = BeginMutation();
        try
        {
            var result = await api.CreateAsync(scope, context, ct);
            lock (_gate)
            {
                EnsureCurrent(revision, context, ct);
                _context = context; Id = result.Id; Snapshot = result; Exhausted = false;
                return result;
            }
        }
        finally { EndMutation(revision); }
    }

    public void Attach(string id)
    {
        lock (_gate)
        {
            if (IsActive && Id == id) return;
            Leave(); _context = client.CaptureContext(); Id = id;
        }
    }

    public MediaItem? NextFor(string? currentContentId)
    {
        lock (_gate) return IsActive && !Exhausted && Snapshot?.Next is { } next && next.ContentId != currentContentId ? next : null;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        string id; long revision, readRevision; ApiRequestContext context;
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            // An EOF can reopen post-roll while Advance is on the wire. Its
            // passive refresh must neither retire that write nor show a view
            // of the server from before the write completes.
            if (!IsActive || _pendingMutationRevision.HasValue) return;
            id = Id!; revision = _revision; readRevision = ++_readRevision; context = _context!.Value;
        }
        try
        {
            var result = await api.GetAsync(id, context, ct);
            lock (_gate)
            {
                EnsureReadCurrent(revision, readRevision, context, ct); Snapshot = result; Exhausted = false;
            }
        }
        catch (ApiException error) when (error.StatusCode == 409)
        {
            lock (_gate)
            {
                EnsureReadCurrent(revision, readRevision, context, ct); Snapshot = null; Exhausted = true;
            }
        }
        // Other failures retain the last pick; advance revalidates on server.
    }

    public async Task<Shuffle?> AdvanceAsync(string fromContentId, CancellationToken ct = default)
    {
        string id; long revision; ApiRequestContext context;
        lock (_gate)
        {
            if (!IsActive || NextFor(fromContentId) == null) return null;
            revision = BeginMutation(); context = _context!.Value; id = Id!;
        }
        try
        {
            var result = await api.AdvanceAsync(id, fromContentId, context, ct);
            lock (_gate)
            {
                EnsureCurrent(revision, context, ct); Snapshot = result; Exhausted = false;
                return result;
            }
        }
        finally { EndMutation(revision); }
    }

    public async Task PickAnotherAsync(CancellationToken ct = default)
    {
        string id, nextId; long revision; ApiRequestContext context;
        lock (_gate)
        {
            if (!IsActive || Snapshot == null || Exhausted) return;
            revision = BeginMutation(); context = _context!.Value; id = Id!; nextId = Snapshot.Next.ContentId;
        }
        try
        {
            var result = await api.SkipAsync(id, nextId, context, ct);
            lock (_gate) { EnsureCurrent(revision, context, ct); Snapshot = result; }
        }
        finally { EndMutation(revision); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        string? id; ApiRequestContext? context;
        lock (_gate) { id = Id; context = _context; Leave(); }
        if (id != null && context.HasValue && client.IsCurrentContext(context.Value))
            await api.DeleteAsync(id, context.Value, ct);
    }

    public void Leave()
    {
        lock (_gate)
        {
            ++_revision; ++_readRevision; _pendingMutationRevision = null;
            _context = null; Id = null; Snapshot = null; Exhausted = false;
        }
    }

    private long BeginMutation()
    {
        var revision = ++_revision;
        _pendingMutationRevision = revision;
        return revision;
    }

    private void EndMutation(long revision)
    {
        lock (_gate) if (_pendingMutationRevision == revision) _pendingMutationRevision = null;
    }

    private void EnsureReadCurrent(long revision, long readRevision, ApiRequestContext context, CancellationToken ct)
    {
        EnsureCurrent(revision, context, ct);
        if (readRevision != _readRevision) throw new OperationCanceledException("A newer shuffle read superseded this one.", ct);
    }

    private void EnsureCurrent(long revision, ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (revision != _revision || !client.IsCurrentContext(context))
            throw new OperationCanceledException("Shuffle authority or playback changed.", ct);
    }
}
