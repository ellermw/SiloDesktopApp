using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>One overlay badge a card shows: what it says, where, and how it is styled.</summary>
internal readonly record struct CardBadge(OverlayPosition Corner, string Text, string OverlayId, OverlayItemConfig Config, string Preset, double Scale);

/// <summary>
/// The four corner badge panels of a card. Cards are bound again each time they scroll back into
/// view and on every Home or Library refresh; rebuilding identical badges there created a dozen
/// XAML objects per card, and that churn drove the expensive full garbage collections that froze
/// the UI for half a second or more. Badges are rebuilt only when what they show changes.
/// </summary>
internal sealed class CardBadgeHosts(Panel topLeft, Panel topRight, Panel bottomLeft, Panel bottomRight)
{
    private CardBadge[] _shown = [];

    public void Show(IReadOnlyList<CardBadge> badges)
    {
        SetVisible(true);
        if (badges.SequenceEqual(_shown) && ShownCount == _shown.Length) return;
        topLeft.Children.Clear();
        topRight.Children.Clear();
        bottomLeft.Children.Clear();
        bottomRight.Children.Clear();
        foreach (var badge in badges)
            Host(badge.Corner).Children.Add(PosterCard.BuildBadge(badge.Text, badge.OverlayId, badge.Config, badge.Preset, badge.Scale));
        _shown = [.. badges];
    }

    /// <summary>Hides the badges of a card being reset or recycled; the next item often shows the same ones.</summary>
    public void Hide() => SetVisible(false);

    private void SetVisible(bool visible)
    {
        var visibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (topLeft.Visibility == visibility) return;
        topLeft.Visibility = topRight.Visibility = bottomLeft.Visibility = bottomRight.Visibility = visibility;
    }

    private int ShownCount => topLeft.Children.Count + topRight.Children.Count + bottomLeft.Children.Count + bottomRight.Children.Count;

    private Panel Host(OverlayPosition corner) => corner switch
    {
        OverlayPosition.TopRight => topRight,
        OverlayPosition.BottomLeft => bottomLeft,
        OverlayPosition.BottomRight => bottomRight,
        _ => topLeft,
    };

    /// <summary>
    /// The badges a media card shows: scan status and manga counts replace everything else,
    /// upcoming premiere/finale badges come first when <paramref name="upcoming"/> is set, then
    /// the user's overlay preferences (at most three per corner).
    /// </summary>
    public static List<CardBadge> For(MediaItem item, CardOverlayService service, double scale, bool statusAndManga, bool upcoming)
    {
        var badges = new List<CardBadge>();
        if (statusAndManga)
        {
            if (item.Status is "pending" or "unmatched" or "ambiguous")
            {
                var label = item.Status switch { "pending" => "SCANNING", "unmatched" => "UNMATCHED", _ => "AMBIGUOUS" };
                badges.Add(new(OverlayPosition.TopLeft, label, "status", new OverlayItemConfig(true, OverlayPosition.TopLeft), "classic", scale));
                return badges;
            }
            if (item.Type == "manga")
            {
                if (!string.IsNullOrWhiteSpace(item.ShowStatus))
                    badges.Add(new(OverlayPosition.TopLeft, item.ShowStatus, "show_status", new OverlayItemConfig(true, OverlayPosition.TopLeft), service.Preset, scale));
                var counts = new List<string>();
                if (item.MangaVolumeCount > 0) counts.Add($"{item.MangaVolumeCount} Vol");
                if (item.MangaChapterCount > 0) counts.Add($"{item.MangaChapterCount} Ch");
                if (counts.Count > 0)
                    badges.Add(new(OverlayPosition.TopRight, string.Join(" · ", counts), "manga_counts", new OverlayItemConfig(true, OverlayPosition.TopRight), service.Preset, scale));
                return badges;
            }
        }

        // Upcoming rows carry server-computed premiere/finale badges that are
        // independent of the user's generic overlay preferences.
        if (upcoming && item.UpcomingEvent is { Badges.Count: > 0 } upcomingEvent)
        {
            foreach (var badge in upcomingEvent.Badges)
            {
                var label = badge switch
                {
                    "series_premiere" => "Premiere",
                    "season_premiere" => "New Season",
                    "finale" => "Finale",
                    _ => badge,
                };
                badges.Add(new(OverlayPosition.TopLeft, label, "upcoming_event", new OverlayItemConfig(true, OverlayPosition.TopLeft), "classic", scale));
            }
        }

        var prefs = service.GetPrefs();
        if (prefs == null) return badges; // Admin kill switch engaged → no preference badges.

        var data = OverlayData.FromMediaItem(item);
        var cornerCounts = new Dictionary<OverlayPosition, int>();
        foreach (var definition in service.GetOrderedDefinitions())
        {
            if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
            if (OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, prefs)) continue;
            var value = definition.GetValue(data);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (cornerCounts.GetValueOrDefault(config.Position) >= 3) continue;
            badges.Add(new(config.Position, value, definition.Id, config, service.Preset, scale));
            cornerCounts[config.Position] = cornerCounts.GetValueOrDefault(config.Position) + 1;
        }
        return badges;
    }
}
