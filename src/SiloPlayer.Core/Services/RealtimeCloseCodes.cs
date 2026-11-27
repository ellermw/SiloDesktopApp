namespace SiloPlayer.Core.Services;

/// <summary>
/// Close codes the server's v2 sockets (events, admin logs, playback control, Watch Together) end a
/// connection with when it ends it because of the credential the socket was opened with. Each code
/// says what the client does next: official Silo Server <c>internal/api/handlers/socket_close.go</c>
/// and <c>docs/realtime-api.md</c> "Close codes" (#4556, <c>f31842a6f</c>). Before that change the
/// server dropped these connections without a close frame, so a client saw 1006.
/// </summary>
public static class RealtimeCloseCodes
{
    /// <summary><c>access_changed</c>: the credential holds but the access the ticket was minted under
    /// changed. Refetch access-dependent state, mint a new ticket and reconnect at once.</summary>
    public const int AccessChanged = 4001;

    /// <summary><c>credential_invalid</c>: the API key, account, login session or profile is gone or
    /// disabled. Final (a failed lookup never sends it): do not reconnect with the same credential.</summary>
    public const int CredentialInvalid = 4002;

    /// <summary><c>lifetime_ended</c>: the connection reached <c>max_connection_seconds</c>, or the access
    /// token it was opened under expired first. Mint a new ticket and reconnect.</summary>
    public const int LifetimeEnded = 4003;

    /// <summary><c>authority_unavailable</c>: the server could not recheck the credential. Nothing is known
    /// to have changed: reconnect with backoff, with the same credential.</summary>
    public const int AuthorityUnavailable = 4004;

    /// <summary>The official events client's wait after 4002, once it has asked for the account again (a
    /// revoked session fails that and signs out); it keeps a lingering session from looping.</summary>
    public static readonly TimeSpan CredentialInvalidRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>The official events client's wait after 4004.</summary>
    public static readonly TimeSpan AuthorityUnavailableRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>True when the server said the socket's credential is gone, so the socket must not mint
    /// another ticket with it.</summary>
    public static bool EndsCredential(int? closeCode) => closeCode == CredentialInvalid;

    /// <summary>The shortest wait before reconnecting after a server close; zero when the code sets none.</summary>
    public static TimeSpan MinimumReconnectDelay(int? closeCode) => closeCode switch
    {
        CredentialInvalid => CredentialInvalidRetryDelay,
        AuthorityUnavailable => AuthorityUnavailableRetryDelay,
        _ => TimeSpan.Zero,
    };
}
