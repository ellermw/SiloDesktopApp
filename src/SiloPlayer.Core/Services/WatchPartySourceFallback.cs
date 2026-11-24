using System.Security.Cryptography;
using System.Text;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>What became of one Watch Party source-fallback attempt.</summary>
public enum SourceFallbackOutcome
{
    /// <summary>Nothing was sent: the server does not advertise the feature.</summary>
    NotSent,
    /// <summary>The server replaced the shared source; the new selection starts like any other.</summary>
    Replaced,
    /// <summary>The server answered with the room unchanged (stale file or selection revision).</summary>
    Unchanged,
    /// <summary>The room moved to another selection while the request was pending; its reply is dropped.</summary>
    Moved,
    /// <summary>The member left the room, the room closed, or the account or profile changed; its reply is dropped.</summary>
    Abandoned,
    /// <summary>The request or the capability read failed. The original refusal stays.</summary>
    Failed,
}

public sealed record WatchPartySourceFallbackInput(
    string? RoomId,
    string? RoomToken,
    WatchTogetherRoomSnapshot? Room,
    string? ConnectionState,
    int? FailedFileId,
    string? Reason,
    string? PlaybackAttemptId);

public sealed record WatchPartySourceFallbackPlan(
    string Key,
    string RoomId,
    int SelectionRevision,
    int FailedFileId,
    string Reason);

/// <summary>
/// Decision rules for coordinated Watch Party source fallback, as the official WebUI applies them
/// (<c>WatchPage.tsx</c> fallbackKey/fallingBack, <c>watchTogetherSourceFallback.ts</c>,
/// <c>useWatchTogetherRoomConnection.ts</c> at silo-server 3b2b32168). Pure: no I/O, no clock.
/// </summary>
/// <remarks>
/// A client must send the request only for the room's selected file, only for one of the four
/// reasons, only while it is a connected member of a connected room, only when the server lists
/// <see cref="Feature"/>, and at most once per room, room proof, selection revision, file, playback
/// attempt and reason. It never retries on its own, and it never substitutes another file itself.
/// </remarks>
public static class WatchPartySourceFallback
{
    public const string Feature = "watch_party_source_fallback_v1";

    /// <summary>The four refusals another version of the same edition can solve (exact, case-sensitive).</summary>
    public static bool IsFallbackReason(string? reason)
        => reason is "no_alternate_version" or "hdr_transcode_unsupported"
            or "subtitle_conversion_unsupported" or "transcoding_disabled";

    /// <summary>Whether the playback capabilities list the fallback feature (exact, case-sensitive).</summary>
    public static bool Supports(IEnumerable<string>? features)
        => features?.Contains(Feature, StringComparer.Ordinal) == true;

    /// <summary>
    /// The request to send for a refused playback start, or null when none may be sent.
    /// Does not look at the server capability or at whether the key was already used.
    /// </summary>
    public static WatchPartySourceFallbackPlan? Plan(WatchPartySourceFallbackInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var room = input.Room;
        if (string.IsNullOrEmpty(input.RoomId) || string.IsNullOrEmpty(input.RoomToken) || room == null ||
            room.RoomId != input.RoomId ||
            input.ConnectionState != "connected" ||
            !IsFallbackReason(input.Reason) ||
            string.IsNullOrWhiteSpace(input.PlaybackAttemptId) ||
            input.FailedFileId is not > 0 ||
            room.SelectedFileId != input.FailedFileId ||
            room.SelectionRevision <= 0 ||
            !room.Members.Any(member => member.IsSelf && member.Connected))
            return null;
        return new WatchPartySourceFallbackPlan(
            Key: string.Join(':', input.RoomId, Fingerprint(input.RoomToken), room.SelectionRevision,
                input.FailedFileId.Value, input.PlaybackAttemptId, input.Reason),
            RoomId: input.RoomId,
            SelectionRevision: room.SelectionRevision,
            FailedFileId: input.FailedFileId.Value,
            Reason: input.Reason!);
    }

    /// <summary>
    /// How the room's answer compares with the room the request was about. <paramref name="current"/>
    /// is the room as it stands when the reply arrives.
    /// </summary>
    public static SourceFallbackOutcome ClassifyReply(
        WatchTogetherRoomSnapshot? current, WatchTogetherRoomSnapshot incoming, WatchPartySourceFallbackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(plan);
        if (current == null || current.RoomId != plan.RoomId || incoming.RoomId != plan.RoomId)
            return SourceFallbackOutcome.Abandoned;
        if (current.SelectionRevision != plan.SelectionRevision)
            return SourceFallbackOutcome.Moved;
        return incoming.SelectionRevision > plan.SelectionRevision
            ? SourceFallbackOutcome.Replaced
            : SourceFallbackOutcome.Unchanged;
    }

    /// <summary>
    /// Whether the HTTP answer may replace the room on screen. An older generation never does, and an
    /// equal one does not when a socket snapshot arrived after the request was sent (the socket keeps
    /// the member's attachment and readiness).
    /// </summary>
    public static bool ShouldAssignReply(
        WatchTogetherRoomSnapshot? current, WatchTogetherRoomSnapshot incoming, bool socketSnapshotSinceRequest)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (current == null || current.RoomId != incoming.RoomId) return false;
        if (current.Generation > incoming.Generation) return false;
        return !(socketSnapshotSinceRequest && current.Generation == incoming.Generation);
    }

    // The proof is a credential: the key keeps only a fingerprint of it.
    private static string Fingerprint(string roomToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(roomToken)))[..16];
}

/// <summary>Lets each fallback key through once; the first claim wins, later claims of the same key do not.</summary>
public sealed class WatchPartySourceFallbackGate
{
    private readonly object _gate = new();
    private readonly HashSet<string> _handled = new(StringComparer.Ordinal);

    public bool TryClaim(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate) return _handled.Add(key);
    }
}
