namespace SiloPlayer.Core.Services;

/// <summary>
/// A protocol-v3 planning decision that intentionally contains no playable
/// route. Unlike a transport exception, its reason and retryability are part of
/// the server contract and must survive into the player UI.
/// </summary>
public sealed class PlaybackPlanTerminalException : InvalidOperationException
{
    public PlaybackPlanTerminalException(string reason, string? serverMessage, bool retryable)
        : base(BuildExceptionMessage(reason, serverMessage))
    {
        Reason = string.IsNullOrWhiteSpace(reason) ? "adaptation_unavailable" : reason.Trim();
        ServerMessage = string.IsNullOrWhiteSpace(serverMessage) ? null : serverMessage.Trim();
        Retryable = retryable;
    }

    public string Reason { get; }
    public string? ServerMessage { get; }
    public bool Retryable { get; }

    private static string BuildExceptionMessage(string reason, string? serverMessage)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "adaptation_unavailable" : reason.Trim();
        var detail = string.IsNullOrWhiteSpace(serverMessage)
            ? "The server could not find a compatible playback route."
            : serverMessage.Trim();
        return $"{detail} ({normalizedReason})";
    }
}
