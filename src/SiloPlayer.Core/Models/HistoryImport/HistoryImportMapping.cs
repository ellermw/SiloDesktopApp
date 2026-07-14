using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.HistoryImport;

public class HistoryImportUserMapping
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string ExternalUserId { get; set; } = "";
    [JsonPropertyName("external_user_name")]
    public string ExternalUsername { get; set; } = "";
    [JsonPropertyName("silo_user_id")]
    public int ContinuumUserId { get; set; }
    [JsonPropertyName("silo_username")]
    public string ContinuumUsername { get; set; } = "";
    [JsonPropertyName("silo_profile_id")]
    public string ProfileId { get; set; } = "";
    [JsonPropertyName("silo_profile_name")]
    public string ProfileName { get; set; } = "";
    public string? LastImportedAt { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class CreateHistoryImportMappingRequest
{
    public int SourceId { get; set; }
    public string ExternalUserId { get; set; } = "";
    [JsonPropertyName("external_user_name")]
    public string ExternalUsername { get; set; } = "";
    [JsonPropertyName("silo_user_id")]
    public int ContinuumUserId { get; set; }
    [JsonPropertyName("silo_profile_id")]
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
