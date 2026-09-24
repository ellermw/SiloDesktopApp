using System.Text.Json;

namespace SiloPlayer.Core.Models.Catalog;

public sealed class EbookReaderProgress
{
    public string? ContentId { get; set; }
    public int FileId { get; set; }
    public string Location { get; set; } = "";
    public double Progress { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class EbookReaderProgressInput
{
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int FileId { get; set; }
    public string Location { get; set; } = "";
    public double Progress { get; set; }
}

public sealed class EbookReaderConfigEnvelope
{
    public string? ContentId { get; set; }
    public Dictionary<string, JsonElement> Config { get; set; } = [];
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class EbookReaderAnnotation
{
    [System.Text.Json.Serialization.JsonPropertyName("etag")]
    public string? ETag { get; set; }
    public string Id { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string Kind { get; set; } = "bookmark";
    public string? CfiRange { get; set; }
    public string? Location { get; set; }
    public string SelectedText { get; set; } = "";
    public string Note { get; set; } = "";
    public string Style { get; set; } = "";
    public string Color { get; set; } = "";
    public JsonElement? Metadata { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class EbookReaderAnnotationsEnvelope
{
    public List<EbookReaderAnnotation> Items { get; set; } = [];
}

public sealed class EbookReaderAnnotationInput
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = "bookmark";
    public string? CfiRange { get; set; }
    public string? Location { get; set; }
    public string SelectedText { get; set; } = "";
    public string Note { get; set; } = "";
    public string Style { get; set; } = "";
    public string Color { get; set; } = "";
    public Dictionary<string, object?>? Metadata { get; set; }
}
