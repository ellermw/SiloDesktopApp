namespace SiloPlayer.Core.Services;

public sealed record EpisodeNavigationTarget(
    string ContentId,
    string? Title,
    string? SeriesTitle,
    string? PosterUrl,
    string? Overview,
    string? AirDate,
    int RuntimeSeconds,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);

public sealed record EpisodeNavigationSnapshot(
    string? OwnerContentId,
    string? PreviousContentId,
    EpisodeNavigationTarget? Next)
{
    public static EpisodeNavigationSnapshot Empty { get; } = new(null, null, null);
}

/// <summary>
/// Keeps episode navigation bound to the content item that produced it.
/// PlayerService reuses one mpv instance across files, so an explicit owner
/// prevents a completed asynchronous lookup from publishing episode controls
/// into a newer movie or unrelated episode session.
/// </summary>
public sealed class EpisodeNavigationState
{
    private readonly object _gate = new();
    private EpisodeNavigationSnapshot _snapshot = EpisodeNavigationSnapshot.Empty;
    private EpisodeNavigationSnapshot _pendingSnapshot = EpisodeNavigationSnapshot.Empty;
    private string? _activeContentId;

    public EpisodeNavigationSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public bool IsOwnedBy(string? contentId)
        => !string.IsNullOrWhiteSpace(contentId) &&
           string.Equals(Volatile.Read(ref _activeContentId), contentId, StringComparison.Ordinal) &&
           string.Equals(Snapshot.OwnerContentId, contentId, StringComparison.Ordinal);

    public bool HasNextFor(string? contentId)
        => IsOwnedBy(contentId) && Snapshot.Next is not null;

    /// <summary>
    /// Starts a new content scope. A preloaded hint for this exact content is
    /// retained; every other episode-navigation payload is discarded.
    /// </summary>
    public void PrepareFor(string contentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        lock (_gate)
        {
            _activeContentId = contentId;
            var prepared = string.Equals(
                _pendingSnapshot.OwnerContentId,
                contentId,
                StringComparison.Ordinal)
                ? _pendingSnapshot
                : EpisodeNavigationSnapshot.Empty;
            _pendingSnapshot = EpisodeNavigationSnapshot.Empty;
            Volatile.Write(ref _snapshot, prepared);
        }
    }

    public void SetHint(string ownerContentId, EpisodeNavigationTarget? next)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerContentId);
        lock (_gate)
        {
            var hint = new EpisodeNavigationSnapshot(ownerContentId, PreviousContentId: null, next);
            if (string.Equals(_activeContentId, ownerContentId, StringComparison.Ordinal))
                Volatile.Write(ref _snapshot, hint);
            else
                _pendingSnapshot = hint;
        }
    }

    public bool TrySetResolved(
        string ownerContentId,
        string? previousContentId,
        EpisodeNavigationTarget? next)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerContentId);
        lock (_gate)
        {
            if (!string.Equals(_activeContentId, ownerContentId, StringComparison.Ordinal))
                return false;

            Volatile.Write(
                ref _snapshot,
                new EpisodeNavigationSnapshot(ownerContentId, previousContentId, next));
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _activeContentId = null;
            _pendingSnapshot = EpisodeNavigationSnapshot.Empty;
            Volatile.Write(ref _snapshot, EpisodeNavigationSnapshot.Empty);
        }
    }
}
