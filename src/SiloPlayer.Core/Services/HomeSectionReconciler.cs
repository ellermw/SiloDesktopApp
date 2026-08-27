using System.Text.Json;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

[Flags]
public enum HomeSectionChange
{
    None = 0,
    Metadata = 1,
    Items = 2,
}

/// <summary>
/// Applies a refreshed Home response without replacing unchanged card objects
/// or the ObservableCollection mounted by the ItemsRepeater.
/// </summary>
public static class HomeSectionReconciler
{
    /// <summary>
    /// Returns whether the hero's materialized item snapshot still matches the
    /// visible prefix of the reconciled featured section.
    /// </summary>
    public static bool IsHeroSnapshotCurrent(
        IList<MediaItem>? snapshot,
        IList<MediaItem>? source,
        int itemLimit)
    {
        var sourceCount = source?.Count ?? 0;
        var visibleCount = itemLimit > 0
            ? Math.Min(itemLimit, sourceCount)
            : sourceCount;
        if ((snapshot?.Count ?? 0) != visibleCount)
            return false;

        for (var index = 0; index < visibleCount; index++)
        {
            if (!ReferenceEquals(snapshot![index], source![index]))
                return false;
        }

        return true;
    }

    public static HomeSectionChange Apply(HomeSectionWithItems current, HomeSectionWithItems incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        var change = ApplyMetadata(current, incoming)
            ? HomeSectionChange.Metadata
            : HomeSectionChange.None;

        incoming.Items ??= [];
        if (ReconcileItems(current.Items, incoming.Items))
            change |= HomeSectionChange.Items;

        return change;
    }

    private static bool ApplyMetadata(HomeSectionWithItems current, HomeSectionWithItems incoming)
    {
        var changed = current.Id != incoming.Id
            || current.SectionType != incoming.SectionType
            || current.Title != incoming.Title
            || current.Featured != incoming.Featured
            || current.ItemLimit != incoming.ItemLimit
            || current.TotalCount != incoming.TotalCount
            || current.IsCustom != incoming.IsCustom
            || current.Customized != incoming.Customized
            || current.LoadFailed != incoming.LoadFailed
            || current.LoadCompleted != incoming.LoadCompleted;

        current.Id = incoming.Id;
        current.SectionType = incoming.SectionType;
        current.Title = incoming.Title;
        current.Featured = incoming.Featured;
        current.ItemLimit = incoming.ItemLimit;
        current.TotalCount = incoming.TotalCount;
        current.IsCustom = incoming.IsCustom;
        current.Customized = incoming.Customized;
        current.LoadFailed = incoming.LoadFailed;
        current.LoadCompleted = incoming.LoadCompleted;
        return changed;
    }

    private static bool ReconcileItems(
        System.Collections.ObjectModel.ObservableCollection<MediaItem> current,
        IReadOnlyList<MediaItem> incoming)
    {
        var changed = false;
        for (var targetIndex = 0; targetIndex < incoming.Count; targetIndex++)
        {
            var next = incoming[targetIndex];
            if (targetIndex < current.Count && RenderEquivalent(current[targetIndex], next))
                continue;

            var equivalentIndex = FindIndex(current, targetIndex + 1, item => RenderEquivalent(item, next));
            if (equivalentIndex >= 0)
            {
                current.Move(equivalentIndex, targetIndex);
                changed = true;
                continue;
            }

            var sameContentIndex = FindIndex(
                current,
                targetIndex,
                item => string.Equals(item.ContentId, next.ContentId, StringComparison.Ordinal));
            if (sameContentIndex >= 0)
            {
                if (sameContentIndex != targetIndex)
                    current.Move(sameContentIndex, targetIndex);
                current[targetIndex] = next;
            }
            else
            {
                current.Insert(targetIndex, next);
            }
            changed = true;
        }

        while (current.Count > incoming.Count)
        {
            current.RemoveAt(current.Count - 1);
            changed = true;
        }

        return changed;
    }

    private static int FindIndex(
        IReadOnlyList<MediaItem> items,
        int startIndex,
        Func<MediaItem, bool> predicate)
    {
        for (var i = Math.Max(0, startIndex); i < items.Count; i++)
            if (predicate(items[i]))
                return i;
        return -1;
    }

    private static bool RenderEquivalent(MediaItem left, MediaItem right)
    {
        return left.ContentId == right.ContentId
            && left.Type == right.Type
            && left.Title == right.Title
            && left.Year == right.Year
            && left.Runtime == right.Runtime
            && left.Genres.SequenceEqual(right.Genres)
            && left.Studios.SequenceEqual(right.Studios)
            && left.Networks.SequenceEqual(right.Networks)
            && left.ContentRating == right.ContentRating
            && left.Status == right.Status
            && left.ShowStatus == right.ShowStatus
            && left.Overview == right.Overview
            && SameArtwork(left.PosterUrl, right.PosterUrl)
            && left.PosterThumbhash == right.PosterThumbhash
            && SameArtwork(left.BackdropUrl, right.BackdropUrl)
            && left.BackdropThumbhash == right.BackdropThumbhash
            && SameArtwork(left.LogoUrl, right.LogoUrl)
            && left.AddedAt == right.AddedAt
            && left.ReleaseDate == right.ReleaseDate
            && left.LastAirDate == right.LastAirDate
            && SameOverlay(left.OverlaySummary, right.OverlaySummary)
            && SameSortMetrics(left.SortMetrics, right.SortMetrics)
            && SameUserState(left.UserState, right.UserState)
            && left.SeriesId == right.SeriesId
            && left.SeriesTitle == right.SeriesTitle
            && left.SeasonNumber == right.SeasonNumber
            && left.EpisodeNumber == right.EpisodeNumber
            && left.RatingImdb == right.RatingImdb
            && left.RatingTmdb == right.RatingTmdb
            && left.RatingRtCritic == right.RatingRtCritic
            && left.RatingRtAudience == right.RatingRtAudience
            && left.OriginalLanguage == right.OriginalLanguage
            && left.PositionSeconds == right.PositionSeconds
            && left.DurationSeconds == right.DurationSeconds
            && left.ProgressUpdatedAt == right.ProgressUpdatedAt
            && left.ItemSource == right.ItemSource
            && left.MangaChapterCount == right.MangaChapterCount
            && left.MangaVolumeCount == right.MangaVolumeCount
            && (left.Badges ?? []).SequenceEqual(right.Badges ?? [])
            && SameUpcomingEvent(left.UpcomingEvent, right.UpcomingEvent)
            && JsonSerializer.Serialize(left.Audiobook) == JsonSerializer.Serialize(right.Audiobook)
            && JsonSerializer.Serialize(left.Ebook) == JsonSerializer.Serialize(right.Ebook);
    }

    private static bool SameArtwork(string? left, string? right) =>
        StripQueryAndFragment(left ?? "") == StripQueryAndFragment(right ?? "");

    private static bool SameUserState(UserState? left, UserState? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null
            && left.Played == right.Played
            && left.IsFavorite == right.IsFavorite
            && left.InWatchlist == right.InWatchlist);

    private static bool SameOverlay(OverlaySummary? left, OverlaySummary? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null
            && left.Resolution == right.Resolution
            && left.Hdr == right.Hdr
            && left.Audio == right.Audio
            && left.AudioChannels == right.AudioChannels
            && left.VideoCodec == right.VideoCodec
            && left.Container == right.Container
            && left.AspectRatio == right.AspectRatio
            && left.ReleaseType == right.ReleaseType
            && left.Edition == right.Edition
            && left.MultiAudio == right.MultiAudio
            && left.MultiSub == right.MultiSub);

    private static bool SameSortMetrics(BrowseItemSortMetrics? left, BrowseItemSortMetrics? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null
            && left.ReleaseDate == right.ReleaseDate
            && left.RuntimeMinutes == right.RuntimeMinutes
            && left.Resolution == right.Resolution
            && left.BitrateKbps == right.BitrateKbps
            && left.ProgressRatio == right.ProgressRatio
            && left.ViewedAt == right.ViewedAt
            && left.PlayCount == right.PlayCount
            && left.Author == right.Author
            && left.Narrator == right.Narrator
            && left.SeriesName == right.SeriesName);

    private static bool SameUpcomingEvent(
        SectionItemUpcomingEvent? left,
        SectionItemUpcomingEvent? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null
            && left.Type == right.Type
            && left.AirDate == right.AirDate
            && left.AirTime == right.AirTime
            && left.AirAt == right.AirAt
            && left.AirTimezone == right.AirTimezone
            && left.LocalAirDate == right.LocalAirDate
            && left.EpisodeTitle == right.EpisodeTitle
            && left.SeasonNumber == right.SeasonNumber
            && left.EpisodeNumber == right.EpisodeNumber
            && left.Badges.SequenceEqual(right.Badges));

    private static string StripQueryAndFragment(string url)
    {
        var queryIndex = url.IndexOfAny(['?', '#']);
        return queryIndex >= 0 ? url[..queryIndex] : url;
    }
}
