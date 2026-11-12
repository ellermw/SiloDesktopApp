using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Shared Continue Watching / Next Up removal rules. Home rows tag each card with the row's
/// section type; the dedicated section page (<c>GET /api/v2/catalog?source=section</c>) carries the
/// server's per-card <c>item_source</c> (<c>in_progress</c> | <c>next_up</c>) and
/// <c>progress_updated_at</c>, which the WebUI turns into the same dismissal
/// (<c>homeDismissalForCard</c>).
/// </summary>
public static class HomeDismissalPolicy
{
    public const string ContinueWatching = "continue_watching";
    public const string NextUp = "next_up";

    /// <summary>
    /// The Home-row vocabulary for a section-browse card's server <c>item_source</c>, or null when
    /// the card belongs to a row that offers no removal.
    /// </summary>
    public static string? SurfaceForServerItemSource(string? serverItemSource) => serverItemSource switch
    {
        "in_progress" => ContinueWatching,
        NextUp => NextUp,
        _ => null,
    };

    /// <summary>
    /// A dedicated section card's <see cref="MediaItem.ItemSource"/>: the removal surface when the
    /// server marked the card, otherwise the page's own source so existing geometry is unchanged.
    /// </summary>
    public static string SectionCardItemSource(string pageSource, string? serverItemSource)
        => SurfaceForServerItemSource(serverItemSource) ?? pageSource;

    /// <summary>True when the card carries the identity its dismissal request needs.</summary>
    public static bool CanDismiss(MediaItem item) => item.ItemSource switch
    {
        ContinueWatching => !string.IsNullOrWhiteSpace(item.ProgressUpdatedAt),
        NextUp => !string.IsNullOrWhiteSpace(SeriesIdentity(item)),
        _ => false,
    };

    /// <summary>Only TV episodes and series dismiss a whole show; book series are unrelated.</summary>
    public static string? SeriesIdentity(MediaItem item) => item.Type switch
    {
        "episode" => item.SeriesId,
        "series" => item.ContentId,
        _ => null,
    };

    /// <summary>
    /// Dismissing an episode or series hides the whole show from both rows, so a dedicated page
    /// drops every card of that show; any other title drops only itself.
    /// </summary>
    public static bool IsRemovedBy(MediaItem item, string contentId, string? seriesId)
        => string.Equals(item.ContentId, contentId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(seriesId)
                && (string.Equals(item.SeriesId, seriesId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.ContentId, seriesId, StringComparison.OrdinalIgnoreCase)));
}
