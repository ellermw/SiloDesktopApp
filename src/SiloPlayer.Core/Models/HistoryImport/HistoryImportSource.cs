namespace SiloPlayer.Core.Models.HistoryImport;

public class HistoryImportSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? SystemId { get; set; }
    public bool Enabled { get; set; }
    public int SortOrder { get; set; }
    public bool HasAdminToken { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CreateHistoryImportSourceRequest
{
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? SystemId { get; set; }
    public bool Enabled { get; set; }
    public int SortOrder { get; set; }
    public string? AdminToken { get; set; }
}

public class PlexAdminLoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class PlexAdminLoginResponse
{
    public string Token { get; set; } = "";
}

public class UpdateHistoryImportSourceRequest
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public string? SystemId { get; set; }
    public bool? Enabled { get; set; }
    public int? SortOrder { get; set; }
}
