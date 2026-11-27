using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A captured rate-limit write: a deep copy of the draft and the validator it was edited against.</summary>
public sealed record AdminRateLimitIntent(AdminRateLimitValues Values, AdminRateLimitValues Baseline, string ETag)
{
    public override string ToString() => nameof(AdminRateLimitIntent);
}

/// <summary>
/// The page-owned rate-limit draft beside its hydrated server baseline. Dirty is structural equality, so
/// typing a value back clears it (#4309). The owning controller serializes access.
/// </summary>
public sealed class AdminRateLimitDraft(AdminRateLimitValues baseline, string etag)
{
    public AdminRateLimitValues Baseline { get; private set; } = baseline;
    public AdminRateLimitValues Values { get; private set; } = baseline;
    /// <summary>The config validator, or null when an acknowledged write has not been re-read yet.</summary>
    public string? ETag { get; private set; } = AdminValidators.RequireStrong(etag);
    public IReadOnlyList<string> DirtyPaths => Values.Differences(Baseline);
    public bool IsDirty => DirtyPaths.Count > 0;

    public void Set(string path, object value) => Values = Values.With(path, value);
    public void Discard() => Values = Baseline;
    public AdminRateLimitIntent Capture() =>
        new(Values, Baseline, ETag ?? throw new InvalidOperationException("Reload rate limit settings before editing."));

    /// <summary>The PATCH was acknowledged; its values are saved but the next validator is unknown until a re-read.</summary>
    public void Acknowledge(AdminRateLimitIntent intent) { Baseline = intent.Values; Values = intent.Values; ETag = null; }

    /// <summary>Adopts a fresh read as a clean baseline, dropping every edit.</summary>
    public void Replace(AdminRateLimitSnapshot snapshot)
    {
        Baseline = Values = snapshot.Config;
        ETag = AdminValidators.RequireStrong(snapshot.ETag);
    }

    /// <summary>
    /// Moves the draft onto a fresh read: only the fields the admin changed from the old baseline keep their
    /// edits, so concurrent server changes to untouched fields are adopted rather than overwritten.
    /// </summary>
    public void Rebase(AdminRateLimitSnapshot snapshot, AdminRateLimitValues? editedFrom = null)
    {
        var edited = Values.Differences(editedFrom ?? Baseline);
        var next = snapshot.Config;
        foreach (var path in edited)
            if (Values.HasField(path) && next.HasField(path)) next = next.With(path, Values[path]);
        Baseline = snapshot.Config; Values = next;
        ETag = AdminValidators.RequireStrong(snapshot.ETag);
    }
}
