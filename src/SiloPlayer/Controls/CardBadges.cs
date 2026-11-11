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
internal sealed class CardBadgeHosts(Panel topLeft, Panel topRight, Panel bottomLeft, Panel bottomRight, Panel leftEdge, Panel rightEdge)
{
    private CardBadge[] _shown = [];
    private double _topLeftMaxWidth = double.NaN;

    public void Show(IReadOnlyList<CardBadge> badges)
    {
        SetVisible(true);
        if (badges.SequenceEqual(_shown) && ShownCount == _shown.Length && topLeft.MaxWidth == _topLeftMaxWidth) return;
        topLeft.Children.Clear();
        topRight.Children.Clear();
        bottomLeft.Children.Clear();
        bottomRight.Children.Clear();
        leftEdge.Children.Clear();
        rightEdge.Children.Clear();
        foreach (var badge in badges)
        {
            var host = Host(badge.Corner);
            var element = PosterCard.BuildBadge(badge.Text, badge.OverlayId, badge.Config, badge.Preset, badge.Scale);
            element.MaxWidth = host.MaxWidth;
            // A horizontal StackPanel otherwise measures its text with infinite
            // width, including under the reserved watch-status corner.
            if (double.IsFinite(host.MaxWidth) && element.Child is StackPanel content)
            {
                var iconWidth = content.Children.OfType<FontIcon>().Sum(icon => icon.Width + content.Spacing);
                foreach (var text in content.Children.OfType<TextBlock>())
                    text.MaxWidth = Math.Max(0, host.MaxWidth - element.Padding.Left - element.Padding.Right
                        - element.BorderThickness.Left - element.BorderThickness.Right - iconWidth);
            }
            host.Children.Add(element);
        }
        _shown = [.. badges];
        _topLeftMaxWidth = topLeft.MaxWidth;
    }

    /// <summary>Hides the badges of a card whose overlays are deferred; the same item shows them again shortly.</summary>
    public void Hide() => SetVisible(false);

    /// <summary>Releases the badges of a card being reset to a placeholder, so no previous item's badges outlive its binding.</summary>
    public void Clear()
    {
        topLeft.Children.Clear();
        topRight.Children.Clear();
        bottomLeft.Children.Clear();
        bottomRight.Children.Clear();
        leftEdge.Children.Clear();
        rightEdge.Children.Clear();
        _shown = [];
    }

    private void SetVisible(bool visible)
    {
        var visibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (topLeft.Visibility == visibility) return;
        topLeft.Visibility = topRight.Visibility = bottomLeft.Visibility = bottomRight.Visibility = leftEdge.Visibility = rightEdge.Visibility = visibility;
    }

    private int ShownCount => topLeft.Children.Count + topRight.Children.Count + bottomLeft.Children.Count + bottomRight.Children.Count + leftEdge.Children.Count + rightEdge.Children.Count;

    private Panel Host(OverlayPosition corner) => corner switch
    {
        OverlayPosition.TopRight => topRight,
        OverlayPosition.BottomLeft => bottomLeft,
        OverlayPosition.BottomRight => bottomRight,
        OverlayPosition.LeftEdge => leftEdge,
        OverlayPosition.RightEdge => rightEdge,
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
                    badges.Add(new(service.DisplayPosition(OverlayPosition.TopRight), string.Join(" · ", counts), "manga_counts", new OverlayItemConfig(true, service.DisplayPosition(OverlayPosition.TopRight)), service.Preset, scale));
                return badges;
            }
        }

        // Upcoming rows carry server-computed premiere/finale badges that are
        // independent of the user's generic overlay preferences.
        if (upcoming && item.UpcomingEvent is { Badges.Count: > 0 } upcomingEvent)
        {
            foreach (var badge in upcomingEvent.Badges.Take(3))
            {
                var label = SiloPlayer.Core.Services.UpcomingBadgePresentation.Label(badge);
                badges.Add(new(OverlayPosition.TopLeft, label, "upcoming_event", new OverlayItemConfig(true, OverlayPosition.TopLeft), "classic", scale));
            }
        }

        var prefs = service.GetPrefs();
        if (prefs == null) return badges; // Admin kill switch engaged → no preference badges.

        var data = OverlayData.FromMediaItem(item);
        var cornerCounts = badges.GroupBy(b => b.Corner).ToDictionary(g => g.Key, g => g.Count());
        foreach (var definition in service.GetOrderedDefinitions())
        {
            if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
            if (service.SuppressesStandalone(definition.Id, prefs)) continue;
            var value = definition.GetValue(data);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var position = service.DisplayPosition(config.Position);
            var limit = position is OverlayPosition.LeftEdge or OverlayPosition.RightEdge ? 4 : 3;
            if (cornerCounts.GetValueOrDefault(position) >= limit) continue;
            badges.Add(new(position, value, definition.Id, config, service.Preset, scale));
            cornerCounts[position] = cornerCounts.GetValueOrDefault(position) + 1;
        }
        return badges;
    }
}
