using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Ordered identities for sequenced administrator playback commands (pause,
/// resume, stop, message), ported from WebUI <c>adminPlaybackCommands.ts</c>.
/// <list type="bullet">
/// <item>A sequence is allocated per session from <c>max(previous + 1, now ms)</c>, so two
/// commands from this client never share one, even across restarts.</item>
/// <item>A stale refusal's <c>X-Silo-Latest-Sequence</c> raises the floor so the next
/// click allocates above the winning sequence.</item>
/// <item>An answer that leaves the outcome unknown (5xx or no response) keeps the
/// identity: repeating the same command, on the same session, under the same
/// authority intent, sends it again so the server replays its receipt. Any other
/// answer, a different command, or another profile allocates afresh.</item>
/// <item>Only the latest command per session may record an unknown outcome, and never
/// after another answer settled it.</item>
/// </list>
/// </summary>
public sealed class AdminActivityCommandLedger
{
    /// <summary>The contract's largest sequence (2^53-1).</summary>
    public const long MaxSequence = 9_007_199_254_740_991;

    private readonly Func<long> _nowMilliseconds;
    private readonly Func<string> _newCommandId;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _lastSequence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Intent, AdminActivityCommandIdentity Identity)> _unsettled = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string CommandId, bool Settled)> _latest = new(StringComparer.Ordinal);

    public AdminActivityCommandLedger(Func<long>? nowMilliseconds = null, Func<string>? newCommandId = null)
    {
        _nowMilliseconds = nowMilliseconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _newCommandId = newCommandId ?? (() => Guid.NewGuid().ToString("D"));
    }

    /// <summary>Allocates one fresh identity. A newer command supersedes an unsettled one.</summary>
    public AdminActivityCommandIdentity Allocate(string sessionId)
    {
        lock (_gate) return AllocateNoLock(sessionId);
    }

    private AdminActivityCommandIdentity AllocateNoLock(string sessionId)
    {
        var previous = _lastSequence.GetValueOrDefault(sessionId);
        var sequence = Math.Max(previous + 1, _nowMilliseconds());
        if (sequence > MaxSequence) throw new InvalidOperationException("The session's command sequence is exhausted.");
        _lastSequence[sessionId] = sequence;
        _unsettled.Remove(sessionId);
        return new AdminActivityCommandIdentity(_newCommandId(), sequence);
    }

    /// <summary>
    /// The identity to send for <paramref name="intent"/>: the unsettled identity
    /// of the same intent, otherwise a fresh one. Marks it as the session's latest.
    /// </summary>
    public AdminActivityCommandIdentity Begin(string sessionId, string intent)
    {
        lock (_gate)
        {
            var identity = _unsettled.TryGetValue(sessionId, out var unsettled) && unsettled.Intent == intent
                ? unsettled.Identity
                : AllocateNoLock(sessionId);
            if (!_latest.TryGetValue(sessionId, out var latest) || latest.CommandId != identity.CommandId)
                _latest[sessionId] = (identity.CommandId, false);
            return identity;
        }
    }

    /// <summary>Records that the answer left this command's outcome unknown.</summary>
    public void RecordOutcomeUnknown(string sessionId, string intent, AdminActivityCommandIdentity identity)
    {
        lock (_gate)
        {
            if (_latest.TryGetValue(sessionId, out var latest) && latest.CommandId == identity.CommandId && !latest.Settled)
                _unsettled[sessionId] = (intent, identity);
        }
    }

    /// <summary>Forgets a command whose outcome the server has answered.</summary>
    public void Settle(string sessionId, AdminActivityCommandIdentity identity)
    {
        lock (_gate)
        {
            if (_latest.TryGetValue(sessionId, out var latest) && latest.CommandId == identity.CommandId)
                _latest[sessionId] = (latest.CommandId, true);
            if (_unsettled.TryGetValue(sessionId, out var unsettled) && unsettled.Identity.CommandId == identity.CommandId)
                _unsettled.Remove(sessionId);
        }
    }

    /// <summary>Raises the allocation floor to a sequence the server reported as applied.</summary>
    public void ObserveLatestSequence(string sessionId, long latest)
    {
        if (latest < 1 || latest > MaxSequence) return;
        lock (_gate)
        {
            if (latest > _lastSequence.GetValueOrDefault(sessionId)) _lastSequence[sessionId] = latest;
        }
    }

    public bool HasUnsettled(string sessionId)
    {
        lock (_gate) return _unsettled.ContainsKey(sessionId);
    }

    /// <summary>Parses <c>X-Silo-Latest-Sequence</c>; null unless a positive integer within the contract bound.</summary>
    public static long? ParseLatestSequence(string? raw)
        => long.TryParse(raw?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
           && value is > 0 and <= MaxSequence ? value : null;
}
