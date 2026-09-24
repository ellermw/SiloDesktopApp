namespace SiloPlayer.Core.Models.MediaMaintenance;

public sealed class MetadataRefreshReceipt
{
    public string Id { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}
