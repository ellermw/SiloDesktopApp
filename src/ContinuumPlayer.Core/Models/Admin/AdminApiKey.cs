namespace ContinuumPlayer.Core.Models.Admin;

public class AdminAPIKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string RateTier { get; set; } = "standard";
    public string CreatedAt { get; set; } = "";
    public string? LastUsedAt { get; set; }
}

public class AdminCreateAPIKeyRequest
{
    public string Label { get; set; } = "";
    public int? UserId { get; set; }
}
