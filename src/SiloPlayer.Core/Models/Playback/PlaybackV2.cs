namespace SiloPlayer.Core.Models.Playback;

public sealed class PlaybackAcceptedSample
{
    public long Sequence { get; set; }
    public double Position { get; set; }
    public bool IsPaused { get; set; }
}

public sealed class PlaybackMutationReceipt
{
    public string Outcome { get; set; } = "";
    public PlaybackAcceptedSample? Accepted { get; set; }
    public string? StopId { get; set; }
    public string? HistoryId { get; set; }
}

public sealed class PlaybackControlTicket
{
    public string Ticket { get; set; } = "";
    public string Protocol { get; set; } = "";
    public int ExpiresIn { get; set; }
    public int MaxConnectionSeconds { get; set; }
}
