using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Home;
namespace SiloPlayer.Core.Models.Catalog;

/// <summary>One collection card of listCatalogItemCollections: the SectionCollectionCard a row of collections carries.</summary>
public sealed class ItemCollectionCard
{
    public string Id { get; set; } = "";
    /// <summary><c>library</c> for a server collection, <c>user</c> for a personal one.</summary>
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Presigned, short-lived; empty when none.</summary>
    public string PosterUrl { get; set; } = "";
    public string? PosterThumbhash { get; set; }
    public bool PosterIsCollage { get; set; }
    public int ItemCount { get; set; }
    /// <summary>A server collection's library, for its page link.</summary>
    [JsonConverter(typeof(SiloPlayer.Core.Json.StringOrNumberJsonConverter))]
    public string? LibraryId { get; set; }
    /// <summary>A personal collection's owner.</summary>
    public string? CreatorProfileId { get; set; }
    [JsonIgnore] public bool IsPersonal => string.Equals(Source, "user", StringComparison.Ordinal);
    [JsonIgnore] public int? Library => int.TryParse(LibraryId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;
    [JsonIgnore] public string Key => Source + ":" + Id;

    /// <summary>True for another profile's shared collection, whose card names its owner.</summary>
    public bool NeedsOwnerName(string? currentProfileId)
        => IsPersonal && !string.IsNullOrEmpty(CreatorProfileId) && !string.IsNullOrEmpty(currentProfileId) && CreatorProfileId != currentProfileId;

    /// <summary>The owner's name for the card's "by Name" line, or a neutral label for a profile no longer listed; null when the card needs none.</summary>
    public string? OwnerName(string? currentProfileId, IReadOnlyDictionary<string, string>? names)
        => !NeedsOwnerName(currentProfileId) ? null
            : names != null && names.TryGetValue(CreatorProfileId!, out var name) ? name
            : SiloPlayer.Core.Localization.AppStrings.Get("web.collections.personalOwnership.anotherProfile", fallback: "Another profile");
}

/// <summary>
/// The collections holding a title, and, when only one does, up to six of its other titles
/// (<c>preview_items</c>), as the WebUI's title page shows them.
/// </summary>
public sealed class ItemCollections
{
    public List<ItemCollectionCard> Items { get; set; } = [];
    public List<MediaItem> PreviewItems { get; set; } = [];

    /// <summary>
    /// What the page shows: nothing for no collections, the collection's other titles under its name when
    /// exactly one collection holds the title and the server sent some, otherwise a card for each collection.
    /// </summary>
    public ItemCollectionsRowMode Mode
        => Items.Count == 0 ? ItemCollectionsRowMode.None
            : Items.Count == 1 && PreviewItems.Count > 0 ? ItemCollectionsRowMode.Preview
            : ItemCollectionsRowMode.AlsoIn;
}

public enum ItemCollectionsRowMode { None, AlsoIn, Preview }
