namespace ContinuumPlayer.Core.Models.Admin;

public class UserIPEntry
{
    public string ClientIp { get; set; } = "";
    public string FirstSeen { get; set; } = "";
    public string LastSeen { get; set; } = "";
    public int RequestCount { get; set; }
}

public class IPUserEntry
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string FirstSeen { get; set; } = "";
    public string LastSeen { get; set; } = "";
    public int RequestCount { get; set; }
}
