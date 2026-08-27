using System.Collections.ObjectModel;
using System.Text.Json;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Reconciles a refreshed media result set in place so unchanged mounted cards
/// retain their object identity while changed, moved, inserted, and removed
/// results still notify the observable collection.
/// </summary>
public static class MediaItemCollectionReconciler
{
    public static bool Apply(
        ObservableCollection<MediaItem> current,
        IReadOnlyList<MediaItem> incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        var changed = false;
        for (var targetIndex = 0; targetIndex < incoming.Count; targetIndex++)
        {
            var next = incoming[targetIndex];
            if (targetIndex < current.Count && RenderEquivalent(current[targetIndex], next))
            {
                var mounted = current[targetIndex];
                if (RefreshVolatileArtwork(mounted, next))
                {
                    current[targetIndex] = mounted;
                    changed = true;
                }
                continue;
            }

            var equivalentIndex = FindIndex(current, targetIndex + 1, item => RenderEquivalent(item, next));
            if (equivalentIndex >= 0)
            {
                current.Move(equivalentIndex, targetIndex);
                RefreshVolatileArtwork(current[targetIndex], next);
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

    private static bool RefreshVolatileArtwork(MediaItem current, MediaItem incoming)
    {
        var changed = !string.Equals(current.PosterUrl, incoming.PosterUrl, StringComparison.Ordinal)
            || !string.Equals(current.BackdropUrl, incoming.BackdropUrl, StringComparison.Ordinal)
            || !string.Equals(current.LogoUrl, incoming.LogoUrl, StringComparison.Ordinal);
        current.PosterUrl = incoming.PosterUrl;
        current.BackdropUrl = incoming.BackdropUrl;
        current.LogoUrl = incoming.LogoUrl;
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
