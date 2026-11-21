namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminApiKeyCapabilities
{
    public bool Available { get; set; }
    public bool GuardedConfiguration { get; set; }
    public List<AdminApiKeyScope> Scopes { get; set; } = [];
    public List<string> RateTiers { get; set; } = [];
}
public sealed class AdminApiKeyScope
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Audience { get; set; } = "";
}
public sealed class AdminApiKey
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Label { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string RateTier { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    public string? ProfileId { get; set; }
    public string CreatedAt { get; set; } = "";
    public string Username { get; set; } = "";
    public string? LastUsedAt { get; set; }
    public override string ToString() => nameof(AdminApiKey);
}
public sealed record AdminApiKeyEditor(AdminApiKey Value, string ETag)
{
    public override string ToString() => nameof(AdminApiKeyEditor);
}
public sealed class AdminApiKeyCreated
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string RateTier { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    public override string ToString() => nameof(AdminApiKeyCreated);
    public string TakeSecret() { var secret = Key; Key = ""; return secret; }
}
