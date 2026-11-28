using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The playback control lane's rules, as the official web player applies them
/// (usePlaybackRealtime.ts and VideoPlayer.tsx, silo-server #4392, #4394, #4554).
/// </summary>
public static class PlaybackControlRules
{
    // The last delay repeats. A ticket mint can fail for minutes while a
    // restarted server rebuilds the session, so the backoff keeps growing.
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>The longest a server's Retry-After can hold back the next attempt.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The wait before reconnect attempt <paramref name="attempt"/> (0-based), never shorter than a capped
    /// Retry-After or the wait the server's close code asks for (5 s after 4004 authority_unavailable).
    /// </summary>
    public static TimeSpan ReconnectDelay(int attempt, double? retryAfterSeconds = null, int? closeCode = null)
    {
        var backoff = ReconnectDelays[Math.Clamp(attempt, 0, ReconnectDelays.Length - 1)];
        var closeWait = RealtimeCloseCodes.MinimumReconnectDelay(closeCode);
        if (closeWait > backoff) backoff = closeWait;
        if (retryAfterSeconds is not { } seconds || double.IsNaN(seconds) || seconds <= 0) return backoff;
        var minimum = TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryAfter.TotalSeconds));
        return minimum > backoff ? minimum : backoff;
    }

    /// <summary>
    /// Whether a refused ticket mint ends the control lane for good. 410 says the
    /// session ended and will not be rebuilt; 401 means this sign-in is over.
    /// Every other refusal (403, 404, 409, or 503 while the server rebuilds the
    /// session) is retried with backoff.
    /// </summary>
    public static bool TicketRefusalEndsControl(int statusCode) => statusCode is 401 or 410;

    /// <summary>
    /// Whether the server's close ends the control lane: 4002 credential_invalid says the sign-in,
    /// account or profile behind it is gone, so the lane must not mint another ticket with it.
    /// </summary>
    public static bool CloseEndsControl(int? closeCode) => RealtimeCloseCodes.EndsCredential(closeCode);

    /// <summary>
    /// Whether a lane that connected and was then closed starts its backoff over. After 4004
    /// authority_unavailable (the server could not recheck the credential) it keeps backing off.
    /// </summary>
    public static bool CloseResetsBackoff(int? closeCode) => closeCode != RealtimeCloseCodes.AuthorityUnavailable;

    /// <summary>
    /// The notice for a stop or terminate command, or null when there is nothing
    /// to say. An administrator's stop always has one; its reason travels in the
    /// command envelope's top-level reason, a server message in the payload.
    /// </summary>
    public static string? EndNotice(string commandName, string? payloadTitle, string? payloadMessage, string? reason, string? issuedByKind)
    {
        var byAdmin = string.Equals(issuedByKind, "admin", StringComparison.Ordinal);
        var message = payloadMessage ?? (string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        if (!byAdmin && message == null) return null;
        var title = payloadTitle ?? (byAdmin
            ? AppStrings.Get("web.player.session.endedByAdmin", fallback: "An administrator ended this playback session.")
            : commandName == "terminate"
                ? AppStrings.Get("web.player.serverMessage.endedTitle", fallback: "Playback ended")
                : AppStrings.Get("web.player.serverMessage.stoppingTitle", fallback: "Playback stopping"));
        if (string.IsNullOrWhiteSpace(message)) return title;
        // The app's toast is one text: the title, then the reason.
        return title.Length > 0 && char.IsPunctuation(title[^1]) ? title + " " + message : title + ": " + message;
    }
}
