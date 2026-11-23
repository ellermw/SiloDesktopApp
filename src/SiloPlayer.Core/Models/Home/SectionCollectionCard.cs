namespace SiloPlayer.Core.Models.Home;

/// <summary>
/// One collection on a row of collections (section_type <c>collection_posters</c>): the server's SectionCollectionCard.
/// The row's section read answers its cards here and has no items.
/// </summary>
public sealed class SectionCollectionCard
{
    public string Id { get; set; } = "";
    /// <summary><c>library</c> for a server collection, <c>user</c> for a personal one.</summary>
    public string Source { get; set; } = "library";
    public string Title { get; set; } = "";
    /// <summary>Presigned and short-lived; empty when the collection has no poster.</summary>
    public string PosterUrl { get; set; } = "";
    /// <summary>A refresh renewed <see cref="PosterUrl"/> on this mounted card, so a poster that never arrived can be read again.</summary>
    public event EventHandler? PosterUrlChanged;
    internal void NotifyPosterUrlChanged() => PosterUrlChanged?.Invoke(this, EventArgs.Empty);
    public string? PosterThumbhash { get; set; }
    public bool PosterIsCollage { get; set; }
    /// <summary>The titles the acting profile sees in the collection.</summary>
    public int ItemCount { get; set; }
    /// <summary>A server collection's library, for its page link.</summary>
    public int? LibraryId { get; set; }
    /// <summary>A personal collection's owner.</summary>
    public string? CreatorProfileId { get; set; }

    public bool IsPersonal => string.Equals(Source, "user", StringComparison.Ordinal);

    /// <summary>The card's identity inside a row: a server and a personal collection may share an id.</summary>
    public string Key => Source + ":" + Id;

    /// <summary>
    /// The owner's name belongs under the card of another profile's shared collection only (the WebUI's CollectionsRow):
    /// a personal collection with a known creator who is not the viewing profile.
    /// </summary>
    public bool IsAnotherProfilesCollection(string? viewingProfileId)
        => IsPersonal && !string.IsNullOrEmpty(viewingProfileId) && !string.IsNullOrEmpty(CreatorProfileId)
           && !string.Equals(CreatorProfileId, viewingProfileId, StringComparison.Ordinal);
}
