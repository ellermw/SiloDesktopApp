namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminServerRestartResponse
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int NotifiedSessions { get; set; }
}
