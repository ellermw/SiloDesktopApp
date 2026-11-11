using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using Windows.Foundation;

namespace SiloPlayer.Controls;

internal readonly record struct CardBadge(OverlayPosition Corner, string Text, string OverlayId, OverlayItemConfig Config, string Preset, double Scale,
    OverlayStyle? Style = null, string? ValueKey = null, CardOverlayService? Images = null);

internal sealed class CardBadgeHosts(Panel topLeft, Panel topRight, Panel bottomLeft, Panel bottomRight, Panel leftEdge, Panel rightEdge)
{
    private CardBadge[] _shown = [];
    private double _topLeftMaxWidth = double.NaN;
    private OverlayBottomRows? _bottomRows;
    private Border? _bottomBand;
    private Border? _topScrim;
    private bool _band;
    private string _preset = "classic";
    private Panel? _layoutOwner;
    private bool _observingMount;
    private ApiIdentityContext? _imageOwner;
    // Space kept under bottom-right badges for a host's own corner controls.
    private double _bottomRightClearance;

    internal static CardBadgeHosts Create(Grid parent, CardOverlayGeometry geometry, double progressClearance = 0, double bottomRightClearance = 0)
    {
        StackPanel Host(HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            var stack = new StackPanel { Spacing = geometry.StackGap, HorizontalAlignment = horizontal, VerticalAlignment = vertical,
                Margin = new Thickness(geometry.EdgeInset, geometry.EdgeInset, geometry.EdgeInset, geometry.EdgeInset + (vertical == VerticalAlignment.Bottom ? progressClearance : 0)), IsHitTestVisible = false };
            parent.Children.Add(stack);
            return stack;
        }
        return new(Host(HorizontalAlignment.Left, VerticalAlignment.Top), Host(HorizontalAlignment.Right, VerticalAlignment.Top),
            Host(HorizontalAlignment.Left, VerticalAlignment.Bottom), Host(HorizontalAlignment.Right, VerticalAlignment.Bottom),
            Host(HorizontalAlignment.Left, VerticalAlignment.Center), Host(HorizontalAlignment.Right, VerticalAlignment.Center))
            { _bottomRightClearance = bottomRightClearance };
    }

    public void Show(IReadOnlyList<CardBadge> badges)
    {
        if (!_observingMount)
        {
            _observingMount = true;
            bottomLeft.Loaded += (_, _) => bottomLeft.DispatcherQueue.TryEnqueue(() =>
            {
                if (bottomLeft.IsLoaded && _shown.Length > 0 && _bottomRows is null) Show(_shown);
            });
        }
        SetVisible(true);
        var imageOwner = badges.Select(badge => badge.Images).FirstOrDefault(images => images is not null)?.CaptureImageOwner();
        if (badges.SequenceEqual(_shown) && imageOwner == _imageOwner && topLeft.MaxWidth == _topLeftMaxWidth && _bottomRows?.Parent is not null) { Layout(); return; }
        Clear();
        _imageOwner = imageOwner;
        if (bottomLeft.Parent is Panel parent)
        {
            if (_layoutOwner is null)
            {
                _layoutOwner = parent;
                parent.SizeChanged += (_, _) => Layout();
                topLeft.SizeChanged += (_, _) => Layout();
            }
            _bottomRows ??= new OverlayBottomRows { VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            _bottomBand ??= new Border { VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            _topScrim ??= new Border { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            foreach (var element in new FrameworkElement[] { _topScrim, _bottomRows, _bottomBand })
                if (element.Parent is null)
                {
                    parent.Children.Insert(parent.Children.IndexOf(ReferenceEquals(element, _topScrim) ? topLeft : bottomLeft), element);
                    Grid.SetRow(element, Grid.GetRow(bottomLeft)); Grid.SetColumn(element, Grid.GetColumn(bottomLeft));
                }
        }
        _shown = [.. badges];
        var primary = badges.FirstOrDefault(badge => badge.Images is not null);
        if (primary.Preset is null) primary = badges.FirstOrDefault();
        _preset = primary.Preset ?? "classic";
        _band = _preset == "band";
        var look = OverlayAppearance.Resolve(_preset, primary.Style, null);
        var scale = badges.Count > 0 ? badges[0].Scale : 1;
        if (_bottomRows is not null) { _bottomRows.Gap = 8 * scale; _bottomRows.StackGap = look.StackGap * scale; }
        foreach (var host in new[] { topLeft, topRight, leftEdge, rightEdge }.OfType<StackPanel>()) host.Spacing = look.StackGap * scale;
        FrameworkElement Build(CardBadge badge) => PosterCard.BuildAdvancedBadge(badge.Text, badge.OverlayId, badge.Config with { Position = badge.Corner }, badge.Preset, badge.Scale, badge.Style, badge.ValueKey, badge.Images);
        foreach (var badge in badges.Where(badge => badge.Corner is not OverlayPosition.BottomLeft and not OverlayPosition.BottomRight && !(_band && badge.Corner == OverlayPosition.TopLeft)))
        {
            var element = Build(badge); var host = Host(badge.Corner);
            element.HorizontalAlignment = badge.Corner is OverlayPosition.TopRight or OverlayPosition.RightEdge ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            host.Children.Add(element);
        }
        var left = badges.Where(badge => badge.Corner == OverlayPosition.BottomLeft).ToArray();
        var right = badges.Where(badge => badge.Corner == OverlayPosition.BottomRight).ToArray();
        if (_band)
        {
            var top = new OverlayBandMarks { Gap = look.StackGap * scale };
            foreach (var badge in badges.Where(badge => badge.Corner == OverlayPosition.TopLeft)) top.Children.Add(BandUnit(Build(badge), top.Children.Count > 0, scale));
            if (top.Children.Count > 0) topLeft.Children.Add(top);
            if (_bottomBand is not null)
            {
                var marks = new OverlayBandMarks { Gap = look.StackGap * scale, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var badge in left.Concat(right)) marks.Children.Add(BandUnit(Build(badge), marks.Children.Count > 0, scale));
                _bottomBand.Child = marks; _bottomBand.Background = Scrim(false); _bottomBand.Padding = new Thickness(8 * scale, 24 * scale, 8 * scale, 8 * scale);
            }
            if (_topScrim is not null) _topScrim.Background = Scrim(true);
        }
        else if (_bottomRows is not null)
        {
            _bottomRows.LeftCount = left.Length;
            foreach (var badge in left.Concat(right)) _bottomRows.Children.Add(Build(badge));
        }
        else foreach (var badge in left.Concat(right)) Host(badge.Corner).Children.Add(Build(badge));
        _topLeftMaxWidth = topLeft.MaxWidth;
        Layout(); SetVisible(true);
    }

    private static FrameworkElement BandUnit(FrameworkElement badge, bool divider, double scale)
    {
        if (!divider) return badge;
        var unit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 * scale };
        unit.Children.Add(new Border { Width = scale, Height = 12 * scale, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(76, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
        unit.Children.Add(badge); return unit;
    }

    private static LinearGradientBrush Scrim(bool top) => new()
    {
        StartPoint = new Point(0, top ? 0 : 1), EndPoint = new Point(0, top ? 1 : 0),
        GradientStops = { new() { Color = Microsoft.UI.ColorHelper.FromArgb(217, 0, 0, 0), Offset = 0 }, new() { Color = Microsoft.UI.ColorHelper.FromArgb(153, 0, 0, 0), Offset = .55 }, new() { Color = Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0), Offset = 1 } },
    };

    private void Layout()
    {
        var width = (bottomLeft.Parent as FrameworkElement)?.ActualWidth ?? 0;
        var bottom = Math.Max(bottomLeft.Margin.Bottom, bottomRight.Margin.Bottom);
        // The shared rows lift only while a bottom-right badge would sit under those
        // controls; the band is a full-width strip and stays on its edge.
        if (!_band && _shown.Any(badge => badge.Corner == OverlayPosition.BottomRight)) bottom = Math.Max(bottom, _bottomRightClearance);
        if (_bottomRows is not null) { _bottomRows.Margin = new Thickness(bottomLeft.Margin.Left, 0, bottomRight.Margin.Right, bottom); _bottomRows.InvalidateMeasure(); }
        if (_bottomBand is not null) _bottomBand.Margin = new Thickness(0, 0, 0, Math.Max(0, bottom - bottomLeft.Margin.Left));
        if (_topScrim is not null) _topScrim.Height = Math.Max(0, topLeft.ActualHeight + topLeft.Margin.Top + 24 * (_shown.FirstOrDefault().Scale is > 0 ? _shown[0].Scale : 1));
        foreach (var host in new[] { topLeft, topRight, leftEdge, rightEdge })
        {
            var rail = host == leftEdge || host == rightEdge;
            var maximum = Math.Min(host.MaxWidth, width > 0 ? width * (rail ? .6 : 1) - (rail ? 0 : host.Margin.Left + host.Margin.Right) : host.MaxWidth);
            if (!double.IsFinite(maximum)) continue;
            foreach (var child in host.Children.OfType<FrameworkElement>()) OverlayBadgeMeasure.Limit(child, maximum);
        }
        if (_shown.Length > 0)
        {
            var inset = _preset is "minimal" or "band" ? topLeft.Margin.Left : 0;
            leftEdge.Margin = rightEdge.Margin = new Thickness(inset);
        }
    }

    public void Hide() => SetVisible(false);
    public void UpdateGeometry() => Layout();
    public void Clear()
    {
        foreach (var host in new[] { topLeft, topRight, bottomLeft, bottomRight, leftEdge, rightEdge }) host.Children.Clear();
        _bottomRows?.Children.Clear();
        if (_bottomBand is not null) _bottomBand.Child = null;
        _shown = [];
        SetVisible(false);
    }
    private void SetVisible(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var host in new[] { topLeft, topRight, bottomLeft, bottomRight, leftEdge, rightEdge }) host.Visibility = state;
        if (_bottomRows is not null) _bottomRows.Visibility = visible && !_band ? Visibility.Visible : Visibility.Collapsed;
        if (_bottomBand is not null) _bottomBand.Visibility = visible && _band && _bottomBand.Child is Panel { Children.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (_topScrim is not null) _topScrim.Visibility = visible && _band && topLeft.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private Panel Host(OverlayPosition position) => position switch
    {
        OverlayPosition.TopRight => topRight, OverlayPosition.BottomLeft => bottomLeft, OverlayPosition.BottomRight => bottomRight,
        OverlayPosition.LeftEdge => leftEdge, OverlayPosition.RightEdge => rightEdge, _ => topLeft,
    };

    internal static List<CardBadge> ForData(OverlayData data, CardOverlayService service, double scale, CardOverlayPrefs? document = null)
    {
        var prefs = document?.Items ?? service.GetPrefs();
        var result = new List<CardBadge>();
        if (prefs is null) return result;
        var ordered = document is null ? service.GetOrderedDefinitions() : document.Order.Concat(OverlayRegistry.All.Select(def => def.Id)).Distinct()
            .Select(id => OverlayRegistry.All.FirstOrDefault(def => def.Id == id)).OfType<OverlayDef>().Where(def => service.OffersOverlay(def.Id)).ToArray();
        var counts = new Dictionary<OverlayPosition, int>();
        foreach (var definition in ordered)
        {
            if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled || service.SuppressesStandalone(definition.Id, prefs)) continue;
            var value = definition.GetValue(data); if (string.IsNullOrWhiteSpace(value)) continue;
            var position = service.DisplayPosition(config.Position);
            var limit = position is OverlayPosition.LeftEdge or OverlayPosition.RightEdge ? 4 : 3;
            if (counts.GetValueOrDefault(position) >= limit) continue;
            result.Add(new(position, value, definition.Id, config, document?.Preset ?? service.Preset, scale, document is null ? service.Style : document.Style, OverlayRegistry.ValueKey(definition.Id, data), service));
            counts[position] = counts.GetValueOrDefault(position) + 1;
        }
        return result;
    }

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
            badges.Add(new(position, value, definition.Id, config, service.Preset, scale, service.Style, OverlayRegistry.ValueKey(definition.Id, data), service));
            cornerCounts[position] = cornerCounts.GetValueOrDefault(position) + 1;
        }
        return badges;
    }
}
