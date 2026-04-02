namespace ContinuumPlayer.Core.Models.HistoryImport;

public class CreateHistoryImportRunRequest
{
    public string ProfileId { get; set; } = "";
    public string Source { get; set; } = "";
    public string? ConnectSessionId { get; set; }
    public string? ServerId { get; set; }
    public int? SourceId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? JellyfinBaseUrl { get; set; }
    public string? JellyfinUsername { get; set; }
    public string? JellyfinPassword { get; set; }
    public string? PlexSessionId { get; set; }
    public string? PlexServerId { get; set; }
    public string? PlexBaseUrl { get; set; }
    public string? PlexToken { get; set; }
}
