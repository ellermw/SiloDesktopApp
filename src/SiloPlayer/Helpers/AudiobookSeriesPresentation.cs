using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Helpers;

/// <summary>
/// Pure rules behind the audiobook library's Series cards (official WebUI
/// AudiobookGroupsView, #4397): one cover that falls through failed URLs, a
/// decorative run bar of the series' books and the profile's listening text.
/// </summary>
internal static class AudiobookSeriesPresentation
{
    /// <summary>Series longer than this draw one continuous bar instead of a segment per book.</summary>
    public const int MaxRunSegments = 12;

    /// <summary>
    /// The first cover not yet known to fail. Failed URLs, not a count, are
    /// tracked: a refetch with new (re-signed or repaired) URLs tries every
    /// cover again. Null means no cover is left and the initials tile shows.
    /// </summary>
    public static string? FirstUntriedCover(IEnumerable<string>? urls, IReadOnlySet<string> failed)
    {
        if (urls == null) return null;
        foreach (var url in urls)
        {
            if (!string.IsNullOrWhiteSpace(url) && !failed.Contains(url))
                return url;
        }
        return null;
    }

    /// <summary>
    /// The bar's counts, clamped as upstream: at least one book, finished no
    /// more than the total, in progress no more than the books left.
    /// </summary>
    public static AudiobookSeriesRun Run(int itemCount, int finishedCount, int inProgressCount)
    {
        var total = Math.Max(itemCount, 1);
        var finished = Math.Clamp(finishedCount, 0, total);
        var inProgress = Math.Clamp(inProgressCount, 0, total - finished);
        return new AudiobookSeriesRun(total, finished, inProgress);
    }

    public static AudiobookSeriesRun Run(AudiobookGroup group) => Run(group.ItemCount, group.FinishedCount, group.InProgressCount);

    /// <summary>
    /// Catalog keys and counts of the profile's listening across a group, in
    /// display order; empty before they start it. Every book finished reads
    /// "all finished"; otherwise finished and in-progress counts are listed
    /// together so the text says everything the decorative bar shows.
    /// </summary>
    public static IReadOnlyList<(string Key, int? Count)> ListeningParts(int itemCount, int finishedCount, int inProgressCount)
    {
        if (finishedCount > 0 && finishedCount == itemCount)
            return [("web.audiobooks.groups.statAllFinished", (int?)null)];
        var parts = new List<(string Key, int? Count)>(2);
        if (finishedCount > 0) parts.Add(("web.audiobooks.groups.statFinished", finishedCount));
        if (inProgressCount > 0) parts.Add(("web.audiobooks.groups.statInProgress", inProgressCount));
        return parts;
    }

    /// <summary>The listening text in the interface language, or null before the profile starts the group.</summary>
    public static string? Listening(AudiobookGroup group)
    {
        var parts = ListeningParts(group.ItemCount, group.FinishedCount, group.InProgressCount);
        if (parts.Count == 0) return null;
        return string.Join(" · ", parts.Select(part => part.Count is { } count
            ? AppStrings.Get(part.Key, new Dictionary<string, object?>
            {
                ["count"] = count,
                ["count_text"] = count.ToString("N0", AppStrings.FormatCulture),
            })
            : AppStrings.Get(part.Key)));
    }

    /// <summary>The group's book count ("12 books"), the first part of its totals line.</summary>
    public static string BooksText(AudiobookGroup group) => AppStrings.Get(
        "web.audiobooks.groups.statBooks",
        new Dictionary<string, object?>
        {
            ["count"] = group.ItemCount,
            ["count_text"] = group.ItemCount.ToString("N0", AppStrings.FormatCulture),
        });
}

/// <summary>
/// A series' length and the profile's listening as one bar. The server sends
/// counts, not positions, so the fill order is not the books' series order.
/// </summary>
internal readonly record struct AudiobookSeriesRun(int Total, int Finished, int InProgress)
{
    /// <summary>Past <see cref="AudiobookSeriesPresentation.MaxRunSegments"/> books the bar is one continuous track.</summary>
    public bool IsContinuous => Total > AudiobookSeriesPresentation.MaxRunSegments;

    /// <summary>Segments drawn: one per book, or one continuous track.</summary>
    public int SegmentCount => IsContinuous ? 1 : Total;

    /// <summary>A segment's fill: 1 for a finished book, 0.5 for one in progress, 0 otherwise.</summary>
    public double SegmentFill(int index)
        => index < Finished ? 1d : index < Finished + InProgress ? 0.5d : 0d;

    /// <summary>The continuous bar's finished share of its width (0–1).</summary>
    public double FinishedFraction => (double)Finished / Total;

    /// <summary>The continuous bar's in-progress share, half a book each as in the segmented bar.</summary>
    public double InProgressFraction => InProgress * 0.5d / Total;

    /// <summary>
    /// The continuous bar's filled share: upstream draws finished then in
    /// progress side by side in the same fill, so they read as one run.
    /// </summary>
    public double ContinuousFill => FinishedFraction + InProgressFraction;
}
