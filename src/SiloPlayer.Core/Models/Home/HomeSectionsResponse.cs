using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Home;
public class HomeSectionsResponse
{
    public List<HomeSectionWithItems> Sections { get; set; } = [];
}
public class HomeSectionWithItems : SiloPlayer.Core.Localization.IServerTitled, IJsonOnDeserialized
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    /// <summary>The server's title: the stored value pins, edits and saves use. Show <see cref="DisplayTitle"/>.</summary>
    public string Title { get; set; } = "";
    /// <summary>Optional <c>home.section.*</c> catalog key for <see cref="Title"/> while it is the server's own text.</summary>
    public string? TitleKey { get; set; }
    public Dictionary<string, string>? TitleArgs { get; set; }
    /// <summary>The title <see cref="TitleKey"/> arrived with; copies carry it with the key.</summary>
    [JsonIgnore] public string? KeyedTitle { get; set; }
    [JsonIgnore] public string DisplayTitle => SiloPlayer.Core.Localization.SectionTitles.Display(this);
    void IJsonOnDeserialized.OnDeserialized()
    {
        TitleKey = SiloPlayer.Core.Localization.SectionTitles.Normalize(TitleKey);
        KeyedTitle = TitleKey == null ? null : Title;
    }
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    // An uncapped read exhausts carousel completion, even when has_more
    // reports catalog matches beyond the recipe budget (reachable via Explore all).
    [JsonIgnore] public bool HasReadWhole { get; set; }
    [JsonIgnore] public bool CanComplete => HasMore && !HasReadWhole;
    [JsonIgnore] public int? ShownCount { get; set; }
    [JsonIgnore] public long CompletedOrder { get; set; }
    [JsonIgnore] public long? ReadLanguageGeneration { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
    [JsonIgnore]
    public bool LoadFailed { get; set; }
    /// <summary>
    /// True once the section-items request completed, including a successful
    /// empty response. This lets the client distinguish an empty configured
    /// row from a row that is still loading without removing the layout slot.
    /// </summary>
    [JsonIgnore]
    public bool LoadCompleted { get; set; }
    // ObservableCollection so in-place mutations (e.g. removing an item from
    // Continue Watching when it's marked watched) propagate to the UI without
    // requiring a section swap + full row rebuild.
    public ObservableCollection<MediaItem> Items { get; set; } = [];
}
public class MediaItem : IJsonOnDeserialized
{
    public SiloPlayer.Core.Models.Catalog.BoxSetSummary? BoxSet { get; set; }
    public int? AdvisoryAge { get; set; }
    public event EventHandler? ArtworkUrlsChanged;

    internal void NotifyArtworkUrlsChanged() => ArtworkUrlsChanged?.Invoke(this, EventArgs.Empty);

    public string ContentId { get; set; } = "";
    /// <summary>Server-authorized playable target; may differ from the browse item.</summary>
    public string? PlayContentId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public int Runtime { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Studios { get; set; } = [];
    public List<string> Networks { get; set; } = [];
    public string? ContentRating { get; set; }
    public string Status { get; set; } = "";
    public string? ShowStatus { get; set; }
    public string Overview { get; set; } = "";
    public string? PendingTranslationLanguage { get; set; }
    public List<string>? MachineTranslatedFields { get; set; }
    public bool? PosterIsEpisodeStill { get; set; }
    public bool? BackdropIsEpisodeStill { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public string? LogoUrl { get; set; }
    public string? AddedAt { get; set; }
    public string? ReleaseDate { get; set; }
    public string? LastAirDate { get; set; }
    public OverlaySummary? OverlaySummary { get; set; }
    public BrowseItemSortMetrics? SortMetrics { get; set; }
    public UserState? UserState { get; set; }
    public MediaItemWatchCounts? UserData { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public double? RatingImdb { get; set; }
    /// <summary>TMDB rating on a 0-10 scale. Used by the card overlay system.</summary>
    public double? RatingTmdb { get; set; }
    /// <summary>Rotten Tomatoes critic score on a 0-100 scale.</summary>
    public int? RatingRtCritic { get; set; }
    /// <summary>Rotten Tomatoes audience score on a 0-100 scale.</summary>
    public int? RatingRtAudience { get; set; }
    /// <summary>ISO 639-1 original language code (e.g. "en", "fr"). Rendered
    /// uppercase as a card overlay badge when enabled in user prefs.</summary>
    public string? OriginalLanguage { get; set; }
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public string? ProgressUpdatedAt { get; set; }
    public string? ItemSource { get; set; }
    public AudiobookDetailExtension? Audiobook { get; set; }
    public EbookDetailExtension? Ebook { get; set; }
    public int MangaChapterCount { get; set; }
    public int MangaVolumeCount { get; set; }
    /// <summary>Server-attached badges (e.g. "season_premiere") shown as pills on
    /// section item cards. Source: HomeSectionItem.badges[] on the server.</summary>
    public List<string>? Badges { get; set; }
    public SectionItemUpcomingEvent? UpcomingEvent { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // The pinned catalog fixture puts counts in user_data. Normalize them
        // into the mutable card state after both objects have been read, so
        // JSON property order cannot affect status, reconciliation or rollback.
        // Retain user_state counts when user_data omits them for compatibility.
        if (UserData is not { } counts || (counts.WatchedCount is null && counts.UnplayedCount is null)) return;
        UserState ??= new();
        if (counts.WatchedCount is { } watched) UserState.WatchedCount = watched;
        if (counts.UnplayedCount is { } unplayed) UserState.UnplayedCount = unplayed;
    }
}

public sealed class MediaItemWatchCounts
{
    public int? WatchedCount { get; set; }
    public int? UnplayedCount { get; set; }
}

public class SectionItemUpcomingEvent
{
    public string Type { get; set; } = "";
    public string AirDate { get; set; } = "";
    public string? AirTime { get; set; }
    public string? AirAt { get; set; }
    public string? AirTimezone { get; set; }
    public string? LocalAirDate { get; set; }
    public string? EpisodeTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public List<string> Badges { get; set; } = [];
}

public class BrowseItemSortMetrics
{
    public string? ReleaseDate { get; set; }
    public int? RuntimeMinutes { get; set; }
    public string? Resolution { get; set; }
    public int? BitrateKbps { get; set; }
    public double? ProgressRatio { get; set; }
    public string? ViewedAt { get; set; }
    public int? PlayCount { get; set; }
    public string? Author { get; set; }
    public string? Narrator { get; set; }
    public string? SeriesName { get; set; }
}

public class AudiobookPerson
{
    public string? PersonId { get; set; }
    public string Name { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? PhotoThumbhash { get; set; }
}

public class AudiobookRelatedItem
{
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string? PosterUrl { get; set; }
    public double? SeriesIndex { get; set; }
}

public class AudiobookSeriesGroup
{
    public string? Name { get; set; }
    public List<AudiobookRelatedItem> Entries { get; set; } = [];
}

public class AudiobookNarration
{
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public List<string> Narrators { get; set; } = [];
}

public class AudiobookRelatedItems
{
    public List<AudiobookRelatedItem> AlsoByAuthor { get; set; } = [];
    public List<AudiobookRelatedItem> Similar { get; set; } = [];
}

public class AudiobookDetailExtension
{
    public List<AudiobookPerson> Authors { get; set; } = [];
    public List<AudiobookPerson> Narrators { get; set; } = [];
    public string? Publisher { get; set; }
    public int TotalDurationSeconds { get; set; }
    public AudiobookSeriesGroup? Series { get; set; }
    public List<AudiobookNarration> OtherNarrations { get; set; } = [];
    public AudiobookRelatedItems Related { get; set; } = new();
}

public class EbookDetailExtension
{
    public List<AudiobookPerson> Authors { get; set; } = [];
    public string? Publisher { get; set; }
    public AudiobookSeriesGroup? Series { get; set; }
    public AudiobookRelatedItems Related { get; set; } = new();
}

public class OverlaySummary
{
    public string Resolution { get; set; } = "";
    /// <summary>HDR / Dolby Vision format string (e.g. "HDR10", "DV HDR10").</summary>
    public string Hdr { get; set; } = "";
    public string Audio { get; set; } = "";
    public string AudioChannels { get; set; } = "";
    public string VideoCodec { get; set; } = "";
    public string Container { get; set; } = "";
    public string AspectRatio { get; set; } = "";
    public string ReleaseType { get; set; } = "";
    public string Edition { get; set; } = "";
    public bool MultiAudio { get; set; }
    public bool MultiSub { get; set; }
}
public class UserState
{
    public bool Played { get; set; }
    public int WatchedCount { get; set; }
    public int UnplayedCount { get; set; }
    public bool IsInProgress { get; set; }
    public bool IsFavorite { get; set; }
    public bool InWatchlist { get; set; }
    public bool NotInterested { get; set; }
}
