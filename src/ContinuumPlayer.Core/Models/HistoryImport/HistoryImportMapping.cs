namespace ContinuumPlayer.Core.Models.HistoryImport;

public class HistoryImportUserMapping
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalUserId { get; set; } = "";
    public string ExternalUsername { get; set; } = "";
    public int ContinuumUserId { get; set; }
    public string ContinuumUsername { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string? LastImportedAt { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class CreateHistoryImportMappingRequest
{
    public int SourceId { get; set; }
    public string ExternalUserId { get; set; } = "";
    public string ExternalUsername { get; set; } = "";
    public int ContinuumUserId { get; set; }
    public string ProfileId { get; set; } = "";
}

public class HistoryImportExternalUser
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Thumb { get; set; }
}

public class AdminHistoryImportBulkRunResult
{
    public List<HistoryImportRun> Runs { get; set; } = [];
}

