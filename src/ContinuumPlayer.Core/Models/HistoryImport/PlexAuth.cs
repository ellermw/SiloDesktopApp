namespace ContinuumPlayer.Core.Models.HistoryImport;

public class PlexPinResponse
{
    public string SessionId { get; set; } = "";
    public string PinCode { get; set; } = "";
    public string AuthUrl { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}

public class PlexCheckRequest
{
    public string SessionId { get; set; } = "";
}

public class PlexCheckResponse
{
    public bool Authenticated { get; set; }
    public List<PlexServer>? Servers { get; set; }
}

public class PlexServer
{
    public string Name { get; set; } = "";
    public string ClientIdentifier { get; set; } = "";
    public bool Owned { get; set; }
    public bool HasRemoteUrl { get; set; }
    public bool HasLocalUrl { get; set; }
}
