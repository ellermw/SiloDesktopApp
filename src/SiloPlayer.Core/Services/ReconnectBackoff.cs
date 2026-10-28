namespace SiloPlayer.Core.Services;

/// <summary>
/// Reconnect delay policy for long-lived sockets. A connection that the server
/// accepts and then closes at once must keep backing off; only a connection
/// that stayed up long enough to be healthy starts the delay over.
/// </summary>
public static class ReconnectBackoff
{
    public static readonly TimeSpan HealthyConnection = TimeSpan.FromSeconds(30);

    public static int AfterConnection(int currentDelayMs, int initialDelayMs, TimeSpan connectedFor) =>
        connectedFor >= HealthyConnection ? initialDelayMs : currentDelayMs;
}
