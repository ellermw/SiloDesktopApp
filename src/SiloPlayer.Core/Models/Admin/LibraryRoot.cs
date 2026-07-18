using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public class LibraryRoot
{
    [JsonPropertyName("library_id")]
    public int LibraryId { get; set; }
    [JsonPropertyName("library_name")]
    public string LibraryName { get; set; } = "";
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = "";
    public string State { get; set; } = ""; // "resolved" | "ambiguous"
    [JsonPropertyName("inferred_type")]
    public string InferredType { get; set; } = "";
    [JsonPropertyName("type_confidence")]
    public string TypeConfidence { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    [JsonPropertyName("tmdb_id")]
    public string? TmdbId { get; set; }
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }
    [JsonPropertyName("tvdb_id")]
    public string? TvdbId { get; set; }
    [JsonPropertyName("observed_file_count")]
    public int ObservedFileCount { get; set; }
    [JsonPropertyName("sample_file_path")]
    public string? SampleFilePath { get; set; }
    [JsonPropertyName("evidence_json")]
    public Dictionary<string, object>? EvidenceJson { get; set; }
    [JsonPropertyName("override_source")]
    public string? OverrideSource { get; set; }
    [JsonPropertyName("first_seen_at")]
    public string FirstSeenAt { get; set; } = "";
    [JsonPropertyName("last_seen_at")]
    public string LastSeenAt { get; set; } = "";
    [JsonPropertyName("active_override")]
    public LibraryRootOverride? ActiveOverride { get; set; }
    [JsonPropertyName("content_id")]
    public string? ContentId { get; set; }
}

public class LibraryRootOverride
{
    [JsonPropertyName("forced_type")]
    public string? ForcedType { get; set; }
    [JsonPropertyName("forced_title")]
    public string? ForcedTitle { get; set; }
    [JsonPropertyName("forced_year")]
    public int? ForcedYear { get; set; }
    [JsonPropertyName("forced_tmdb_id")]
    public string? ForcedTmdbId { get; set; }
    [JsonPropertyName("forced_imdb_id")]
    public string? ForcedImdbId { get; set; }
    [JsonPropertyName("forced_tvdb_id")]
    public string? ForcedTvdbId { get; set; }
    public string? Note { get; set; }
}

public class LibraryRootsResponse
{
    public List<LibraryRoot> Items { get; set; } = [];
    public int Total { get; set; }
}

public class UpsertLibraryRootOverrideRequest
{
    [JsonPropertyName("library_id")]
    public int LibraryId { get; set; }
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = "";
    [JsonPropertyName("forced_type")]
    public string? ForcedType { get; set; }
    [JsonPropertyName("forced_title")]
    public string? ForcedTitle { get; set; }
    [JsonPropertyName("forced_year")]
    public int? ForcedYear { get; set; }
    [JsonPropertyName("forced_tmdb_id")]
    public string? ForcedTmdbId { get; set; }
    [JsonPropertyName("forced_imdb_id")]
    public string? ForcedImdbId { get; set; }
    [JsonPropertyName("forced_tvdb_id")]
    public string? ForcedTvdbId { get; set; }
    public string? Note { get; set; }
}

public class DeleteLibraryRootOverrideRequest
{
    [JsonPropertyName("library_id")]
    public int LibraryId { get; set; }
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = "";
}
