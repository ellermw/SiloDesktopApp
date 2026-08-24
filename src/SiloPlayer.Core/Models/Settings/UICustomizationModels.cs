using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Settings;

public static class UICustomizationSettingKeys
{
    public const string PrimaryMenu = "nav.primary_menu";
    public const string Shortcuts = "nav.shortcuts";
    public const string CardPresentation = "ui.card_presentation";
}

public sealed class CardPresentation
{
    [JsonPropertyName("poster_size")] public string PosterSize { get; set; } = "standard";
    [JsonPropertyName("caption")] public string Caption { get; set; } = "title_metadata";

    public static CardPresentation Default => new();

    public CardPresentation Normalize() => new()
    {
        PosterSize = PosterSize is "compact" or "standard" or "large" ? PosterSize : "standard",
        Caption = Caption is "title_metadata" or "title" or "artwork" ? Caption : "title_metadata",
    };
}

public sealed class PrimaryMenuDocument
{
    [JsonPropertyName("items")] public List<PrimaryMenuItem> Items { get; set; } = [];
}

public sealed class NavigationShortcutDocument
{
    [JsonPropertyName("items")] public List<PrimaryMenuItem> Items { get; set; } = [];
}

public sealed class PrimaryMenuItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("destination")] public string? Destination { get; set; }
    [JsonPropertyName("library_id")] public int? LibraryId { get; set; }
    [JsonPropertyName("section_id")] public string? SectionId { get; set; }
    [JsonPropertyName("collection_id")] public string? CollectionId { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }

    public string SemanticKey => Type switch
    {
        "builtin" => $"builtin:{Destination}",
        "library" => $"library:{LibraryId}",
        "section" => $"section:{LibraryId}:{SectionId}",
        "collection" => $"collection:{LibraryId?.ToString() ?? "global"}:{CollectionId}",
        _ => $"invalid:{Type}:{Label}",
    };

    public PrimaryMenuItem Clone() => new()
    {
        Type = Type,
        Destination = Destination,
        LibraryId = LibraryId,
        SectionId = SectionId,
        CollectionId = CollectionId,
        Label = Label,
    };

    public static PrimaryMenuItem Builtin(string destination) => new()
    {
        Type = "builtin",
        Destination = destination,
    };
}
