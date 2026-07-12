namespace SiloPlayer.Core.Models.HistoryImport;

public class EmbyConnectLoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class EmbyConnectLoginResponse
{
    public string ConnectSessionId { get; set; } = "";
    public List<HistoryImportConnectServer> Servers { get; set; } = [];
    public string ExpiresAt { get; set; } = "";
}

public class HistoryImportConnectServer
{
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? SystemId { get; set; }
    public bool HasRemoteUrl { get; set; }
    public bool HasLocalAddress { get; set; }
}
