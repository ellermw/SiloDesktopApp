using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services;

/// <summary>Why the server ended a playback session for good (official player-fetch.ts PlaybackSessionEndReason, silo-server #5180).</summary>
public enum PlaybackSessionEndReason
{
    /// <summary>410 playback_session_ended: an administrator ended it, or any other stopped attempt.</summary>
    Ended,
    /// <summary>410 playback_session_paused_timeout: it stayed paused past the administrator's timeout.</summary>
    PausedTimeout,
}

/// <summary>A session request learned the server ended the session; the caller leaves instead of retrying or recovering.</summary>
public sealed class PlaybackSessionEndedException(string sessionId, PlaybackSessionEndReason reason, Exception? inner = null)
    : Exception("The server ended this playback session.", inner)
{
    public string SessionId { get; } = sessionId;
    public PlaybackSessionEndReason Reason { get; } = reason;
}

/// <summary>
/// The paused-session timeout's client contract (official protocol v3 §6.3, silo-server #5180 and #5225): which starts
/// advertise paused_timeout_v1, which answers end a session, and the notice the player shows when it leaves.
/// </summary>
public static class PlaybackSessionEndRules
{
    public const string PausedTimeoutFeature = "paused_timeout_v1";
    public const string PausedTimeoutCode = "playback_session_paused_timeout";
    public const string EndedCode = "playback_session_ended";

    /// <summary>
    /// The reason a failed session request says the session is over, or null when it does not: only a 410 naming one of the
    /// two problem types. A network failure, a 404 (expired or restarted server) or any other refusal keeps normal recovery.
    /// </summary>
    public static PlaybackSessionEndReason? Classify(Exception? error) => error switch
    {
        PlaybackSessionEndedException ended => ended.Reason,
        ApiException { StatusCode: 410 } api when Names(api, PausedTimeoutCode) => PlaybackSessionEndReason.PausedTimeout,
        ApiException { StatusCode: 410 } api when Names(api, EndedCode) => PlaybackSessionEndReason.Ended,
        _ => null,
    };

    /// <summary>
    /// Whether a refused control-ticket mint makes the player leave. Every 410 stops minting, but only the paused timeout's
    /// answer is reported: the server answers playback_session_ended for every stopped attempt, an expiry included.
    /// </summary>
    public static bool TicketRefusalEndsPlayback(Exception? error) => error is ApiException { StatusCode: 410 } && Classify(error) == PlaybackSessionEndReason.PausedTimeout;

    /// <summary>
    /// Whether a start advertises paused_timeout_v1: only a video the viewer watches here, which leaves on the paused
    /// timeout's answer. The official players leave it off Cast (a receiver keeps playing for its sender) and audiobook
    /// starts, and audio-only files are never ended this way.
    /// </summary>
    public static bool AdvertisesPausedTimeout(bool isAudiobook, bool isCastReceiver, bool hasVideo) => hasVideo && !isAudiobook && !isCastReceiver;

    /// <summary>The app toast shown when the player leaves an ended session.</summary>
    public static string Notice(PlaybackSessionEndReason reason) => reason == PlaybackSessionEndReason.PausedTimeout
        ? AppStrings.Get("web.player.session.endedPausedTimeout", fallback: "Playback stopped because it was paused for too long.")
        : AppStrings.Get("web.player.session.endedByAdmin", fallback: "An administrator ended this playback session.");

    private static bool Names(ApiException error, string code) =>
        string.Equals(error.ErrorCode, code, StringComparison.Ordinal) || string.Equals(error.ProblemType, code, StringComparison.Ordinal);
}
