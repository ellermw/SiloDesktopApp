using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using Windows.UI;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Page-local building blocks of the Home sections page: the official lucide icons it draws, the icon tiles of its rows and kind cards,
/// the pill strip that switches pages and the destructive button look. Nothing here is shared with another page.
/// </summary>
internal static class AdminHomeSectionsUi
{
    // lucide 0.576.0 (the version the official web client pins), one path per drawn element.
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["archive"] = "M3 3H21A1 1 0 0 1 22 4V7A1 1 0 0 1 21 8H3A1 1 0 0 1 2 7V4A1 1 0 0 1 3 3Z|M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8|M10 12h4",
        ["arrow-down-to-line"] = "M12 17V3|m6 11 6 6 6-6|M19 21H5",
        ["arrow-up-to-line"] = "M5 3h14|m18 13-6-6-6 6|M12 7v14",
        ["award"] = "m15.477 12.89 1.515 8.526a.5.5 0 0 1-.81.47l-3.58-2.687a1 1 0 0 0-1.197 0l-3.586 2.686a.5.5 0 0 1-.81-.469l1.514-8.526|M6 8a6 6 0 1 0 12 0a6 6 0 1 0 -12 0",
        ["bookmark"] = "M17 3a2 2 0 0 1 2 2v15a1 1 0 0 1-1.496.868l-4.512-2.578a2 2 0 0 0-1.984 0l-4.512 2.578A1 1 0 0 1 5 20V5a2 2 0 0 1 2-2z",
        ["cake"] = "M20 21v-8a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8|M4 16s.5-1 2-1 2.5 2 4 2 2.5-2 4-2 2.5 2 4 2 2-1 2-1|M2 21h20|M7 8v3|M12 8v3|M17 8v3|M7 4h.01|M12 4h.01|M17 4h.01",
        ["calendar-clock"] = "M16 14v2.2l1.6 1|M16 2v4|M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3.5|M3 10h5|M8 2v4|M10 16a6 6 0 1 0 12 0a6 6 0 1 0 -12 0",
        ["calendar-days"] = "M8 2v4|M16 2v4|M5 4H19A2 2 0 0 1 21 6V20A2 2 0 0 1 19 22H5A2 2 0 0 1 3 20V6A2 2 0 0 1 5 4Z|M3 10h18|M8 14h.01|M12 14h.01|M16 14h.01|M8 18h.01|M12 18h.01|M16 18h.01",
        ["calendar-plus"] = "M16 19h6|M16 2v4|M19 16v6|M21 12.598V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h8.5|M3 10h18|M8 2v4",
        ["check"] = "M20 6 9 17l-5-5",
        ["chevron-down"] = "m6 9 6 6 6-6",
        ["chevron-left"] = "m15 18-6-6 6-6",
        ["chevron-right"] = "m9 18 6-6-6-6",
        ["chevron-up"] = "m18 15-6-6-6 6",
        ["circle-alert"] = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0|M12 8L12 12|M12 16L12.01 16",
        ["clapperboard"] = "m12.296 3.464 3.02 3.956|M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3z|M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z|m6.18 5.276 3.1 3.899",
        ["copy"] = "M10 8H20A2 2 0 0 1 22 10V20A2 2 0 0 1 20 22H10A2 2 0 0 1 8 20V10A2 2 0 0 1 10 8Z|M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        ["dices"] = "M4 10H12A2 2 0 0 1 14 12V20A2 2 0 0 1 12 22H4A2 2 0 0 1 2 20V12A2 2 0 0 1 4 10Z|m17.92 14 3.5-3.5a2.24 2.24 0 0 0 0-3l-5-4.92a2.24 2.24 0 0 0-3 0L10 6|M6 18h.01|M10 14h.01|M15 6h.01|M18 9h.01",
        ["ellipsis"] = "M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M18 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M4 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0",
        ["eye-off"] = "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49|M14.084 14.158a3 3 0 0 1-4.242-4.242|M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143|m2 2 20 20",
        ["eye"] = "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0|M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0",
        ["flame"] = "M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4",
        ["gem"] = "M10.5 3 8 9l4 13 4-13-2.5-6|M17 3a2 2 0 0 1 1.6.8l3 4a2 2 0 0 1 .013 2.382l-7.99 10.986a2 2 0 0 1-3.247 0l-7.99-10.986A2 2 0 0 1 2.4 7.8l2.998-3.997A2 2 0 0 1 7 3z|M2 9h20",
        ["globe"] = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0|M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20|M2 12h20",
        ["grip-vertical"] = "M8 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M8 5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M8 19a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M14 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M14 5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0|M14 19a1 1 0 1 0 2 0a1 1 0 1 0 -2 0",
        ["headphones"] = "M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3",
        ["heart"] = "M2 9.5a5.5 5.5 0 0 1 9.591-3.676.56.56 0 0 0 .818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 .019L5 15c-1.5-1.5-3-3.2-3-5.5",
        ["history"] = "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8|M3 3v5h5|M12 7v5l4 2",
        ["house"] = "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8|M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        ["info"] = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0|M12 16v-4|M12 8h.01",
        ["layers"] = "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z|M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12|M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17",
        ["layout-grid"] = "M4 3H9A1 1 0 0 1 10 4V9A1 1 0 0 1 9 10H4A1 1 0 0 1 3 9V4A1 1 0 0 1 4 3Z|M15 3H20A1 1 0 0 1 21 4V9A1 1 0 0 1 20 10H15A1 1 0 0 1 14 9V4A1 1 0 0 1 15 3Z|M15 14H20A1 1 0 0 1 21 15V20A1 1 0 0 1 20 21H15A1 1 0 0 1 14 20V15A1 1 0 0 1 15 14Z|M4 14H9A1 1 0 0 1 10 15V20A1 1 0 0 1 9 21H4A1 1 0 0 1 3 20V15A1 1 0 0 1 4 14Z",
        ["list-filter"] = "M2 5h20|M6 12h12|M9 19h6",
        ["monitor-play"] = "M15.033 9.44a.647.647 0 0 1 0 1.12l-4.065 2.352a.645.645 0 0 1-.968-.56V7.648a.645.645 0 0 1 .967-.56z|M12 17v4|M8 21h8|M4 3H20A2 2 0 0 1 22 5V15A2 2 0 0 1 20 17H4A2 2 0 0 1 2 15V5A2 2 0 0 1 4 3Z",
        ["palette"] = "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z|M13 6.5a0.5 0.5 0 1 0 1 0a0.5 0.5 0 1 0 -1 0|M17 10.5a0.5 0.5 0 1 0 1 0a0.5 0.5 0 1 0 -1 0|M6 12.5a0.5 0.5 0 1 0 1 0a0.5 0.5 0 1 0 -1 0|M8 7.5a0.5 0.5 0 1 0 1 0a0.5 0.5 0 1 0 -1 0",
        ["pencil"] = "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z|m15 5 4 4",
        ["play"] = "M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z",
        ["plus"] = "M5 12h14|M12 5v14",
        ["rotate-ccw"] = "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8|M3 3v5h5",
        ["search"] = "m21 21-4.34-4.34|M3 11a8 8 0 1 0 16 0a8 8 0 1 0 -16 0",
        ["shuffle"] = "m18 14 4 4-4 4|m18 2 4 4-4 4|M2 18h1.973a4 4 0 0 0 3.3-1.7l5.454-8.6a4 4 0 0 1 3.3-1.7H22|M2 6h1.972a4 4 0 0 1 3.6 2.2|M22 18h-6.041a4 4 0 0 1-3.3-1.8l-.359-.45",
        ["skip-forward"] = "M21 4v16|M6.029 4.285A2 2 0 0 0 3 6v12a2 2 0 0 0 3.029 1.715l9.997-5.998a2 2 0 0 0 .003-3.432z",
        ["smile"] = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0|M8 14s1.5 2 4 2 4-2 4-2|M9 9L9.01 9|M15 9L15.01 9",
        ["sparkles"] = "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z|M20 2v4|M22 4h-4|M2 20a2 2 0 1 0 4 0a2 2 0 1 0 -4 0",
        ["square-check-big"] = "M21 10.656V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h12.344|m9 11 3 3L22 4",
        ["star-off"] = "m10.344 4.688 1.181-2.393a.53.53 0 0 1 .95 0l2.31 4.679a2.12 2.12 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.237 3.152|m17.945 17.945.43 2.505a.53.53 0 0 1-.771.56l-4.618-2.428a2.12 2.12 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.12 2.12 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a8 8 0 0 0 .4-.099|m2 2 20 20",
        ["star"] = "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z",
        ["target"] = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0|M6 12a6 6 0 1 0 12 0a6 6 0 1 0 -12 0|M10 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0",
        ["timer"] = "M10 2L14 2|M12 14L15 11|M4 14a8 8 0 1 0 16 0a8 8 0 1 0 -16 0",
        ["trash-2"] = "M10 11v6|M14 11v6|M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6|M3 6h18|M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
        ["trending-up"] = "M16 7h6v6|m22 7-8.5 8.5-5-5L2 17",
        ["tv"] = "m17 2-5 5-5-5|M4 7H20A2 2 0 0 1 22 9V20A2 2 0 0 1 20 22H4A2 2 0 0 1 2 20V9A2 2 0 0 1 4 7Z",
        ["users"] = "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2|M16 3.128a4 4 0 0 1 0 7.744|M22 21v-2a4 4 0 0 0-3-3.87|M5 7a4 4 0 1 0 8 0a4 4 0 1 0 -8 0",
        ["x"] = "M18 6 6 18|m6 6 12 12",
    };

    /// <summary>The official row-kind icons (rowIcons.ts KIND_ICONS); kinds not listed use their group's icon.</summary>
    private static readonly Dictionary<string, string> KindIcons = new(StringComparer.Ordinal)
    {
        ["continue_watching"] = "play", ["next_up"] = "skip-forward", ["next_in_series"] = "headphones", ["watchlist"] = "bookmark", ["favorites"] = "star",
        ["recently_added"] = "sparkles", ["recently_released"] = "calendar-clock", ["new_to_library"] = "calendar-plus", ["returning_shows"] = "tv",
        ["trending_on_server"] = "flame", ["most_watched"] = "trending-up", ["trending_discover"] = "globe", ["profile_activity_feed"] = "users",
        ["recommended_for_you"] = "heart", ["because_you_watched"] = "history", ["similar_users_liked"] = "users", ["taste_match"] = "target",
        ["mood_collection"] = "smile", ["seasonal_themed"] = "calendar-days", ["editorial_spotlight"] = "clapperboard", ["format_showcase"] = "monitor-play",
        ["hidden_gems"] = "gem", ["critically_acclaimed"] = "award", ["forgotten_favorites"] = "archive", ["genre_roulette"] = "dices", ["random"] = "shuffle",
        ["short_watches"] = "timer", ["anniversaries"] = "cake", ["award_winners"] = "award", ["collection"] = "layers", ["collection_posters"] = "layout-grid",
        ["custom_filter"] = "list-filter", ["genre"] = "list-filter",
    };
    private static readonly Dictionary<string, string> GroupIcons = new(StringComparer.Ordinal)
    { ["keep"] = "play", ["new"] = "sparkles", ["popular"] = "flame", ["picked"] = "heart", ["moods"] = "palette", ["collections"] = "layers" };
    /// <summary>The chart palette tints of the picker groups (app.css --chart-1..5).</summary>
    private static readonly Dictionary<string, Color> GroupTints = new(StringComparer.Ordinal)
    {
        ["keep"] = Color.FromArgb(255, 0x5F, 0x74, 0xEE), ["new"] = Color.FromArgb(255, 0x16, 0x9E, 0x88), ["popular"] = Color.FromArgb(255, 0xBF, 0x7F, 0x22),
        ["picked"] = Color.FromArgb(255, 0xC2, 0x57, 0x8F), ["moods"] = Color.FromArgb(255, 0x3F, 0x97, 0xCF), ["collections"] = Color.FromArgb(255, 0x90, 0xA0, 0xB5),
    };

    /// <summary>The destructive fill (--destructive-fill) and the darker steps it takes on hover and press.</summary>
    internal static readonly Color DestructiveFill = Color.FromArgb(255, 0xDC, 0x26, 0x26);
    internal static readonly Color DestructiveHover = Color.FromArgb(255, 0xB9, 0x1C, 0x1C);
    internal static readonly Color DestructivePressed = Color.FromArgb(255, 0x99, 0x1B, 0x1B);

    internal static Brush Solid(Color color, double opacity = 1) => AdminMonitorUi.Solid(color, opacity);
    internal static Brush Themed(string key) => U.Brush(key);

    internal static bool HasIcon(string name) => Paths.ContainsKey(name);
    internal static string KindIconName(string sectionType) => KindIcons.TryGetValue(sectionType, out var name) ? name : GroupIcons[AdminHomeSectionsGroup(sectionType)];
    private static string AdminHomeSectionsGroup(string sectionType) => SiloPlayer.Core.Services.Admin.AdminHomeSectionsKinds.Group(sectionType);
    internal static string GroupIconName(string group) => GroupIcons.TryGetValue(group, out var name) ? name : "layers";
    internal static Color GroupTint(string group) => GroupTints.TryGetValue(group, out var tint) ? tint : GroupTints["collections"];

    /// <summary>A lucide icon drawn as stroked paths in a square of <paramref name="size"/>.</summary>
    internal static FrameworkElement Icon(string name, double size, Brush stroke, double thickness = 2)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var data in Paths[name].Split('|'))
        {
            var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
                $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
            path.Stroke = stroke; path.StrokeThickness = thickness; canvas.Children.Add(path);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>The icon tile at the start of a row (PosterPeek IconTile): 48x42 under 640px, 74x50 above, rounded-xl, accent fill and a hairline ring.</summary>
    internal static Border RowArt(string sectionType, bool narrow, bool collapsed)
    {
        var tile = new Border
        {
            Width = narrow ? 48 : 74, Height = collapsed ? 30 : narrow ? 42 : 50, CornerRadius = new(12), BorderThickness = new(1), VerticalAlignment = VerticalAlignment.Center,
            Background = collapsed ? new SolidColorBrush(Colors.Transparent) : Themed("AccentBackgroundBrush"), BorderBrush = collapsed ? new SolidColorBrush(Colors.Transparent) : Themed("BorderBrush"),
        };
        var glyph = Icon(collapsed ? "eye-off" : KindIconName(sectionType), collapsed ? 17 : 20, Themed("SecondaryTextBrush"));
        glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center; tile.Child = glyph;
        return tile;
    }

    /// <summary>The 40px tinted icon tile of a picker card (KindCard).</summary>
    internal static Border KindTile(string sectionType, string group)
    {
        var tint = GroupTint(group);
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new(11), BorderThickness = new(1), BorderBrush = Solid(tint, .25), Background = Solid(tint, .15), VerticalAlignment = VerticalAlignment.Top,
        };
        var icon = Icon(KindIconName(sectionType), 19, Solid(tint)); icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        tile.Child = icon; return tile;
    }

    /// <summary>A rounded tag next to a row name (h-[22px], text-xs semibold, tinted fill and ring).</summary>
    internal static Border Tag(string text, Color tone, string? icon = null)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) line.Children.Add(Icon(icon, 12, Solid(tone)));
        line.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Solid(tone), VerticalAlignment = VerticalAlignment.Center });
        return new Border { Height = 22, CornerRadius = new(11), Padding = new(8, 0, 8, 0), BorderThickness = new(1), BorderBrush = Solid(tone, .30), Background = Solid(tone, .15), Child = line, VerticalAlignment = VerticalAlignment.Center };
    }

    private static ControlTemplate Template(string normal, string hover, string pressed, string foreground) => (ControlTemplate)XamlReader.Load($$"""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Root" Background="{{normal}}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
            CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
            <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
              <VisualState x:Name="Normal"/>
              <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Root.Background" Value="{{hover}}"/></VisualState.Setters></VisualState>
              <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Root.Background" Value="{{pressed}}"/></VisualState.Setters></VisualState>
              <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
            </VisualStateGroup></VisualStateManager.VisualStateGroups>
            <ContentPresenter Content="{TemplateBinding Content}" Foreground="{{foreground}}" FontSize="{TemplateBinding FontSize}" FontWeight="{TemplateBinding FontWeight}"
              HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
        </ControlTemplate>
        """);

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// The destructive button: the official --destructive-fill (#dc2626) under white text that darkens on hover and press. The shared
    /// destructive style lightens on hover, so this page keeps its own.
    /// </summary>
    internal static Style DestructiveButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, Template(Hex(DestructiveFill), Hex(DestructiveHover), Hex(DestructivePressed), "#FFFFFF")));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        return style;
    }

    /// <summary>The foreground of the primary fill (the accent's own ink).</summary>
    internal static readonly Brush PrimaryInk = new SolidColorBrush(Color.FromArgb(255, 21, 21, 26));

    /// <summary>An icon followed by its label, drawn in one ink.</summary>
    internal static FrameworkElement ActionContent(string label, string? icon, double fontSize, Brush foreground)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) content.Children.Add(Icon(icon, 16, foreground));
        content.Children.Add(new TextBlock { Text = label, FontSize = fontSize, FontWeight = FontWeights.Medium, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    /// <summary>An action button with an icon before its label, in the page's own look: outline, or the primary fill.</summary>
    internal static Button IconAction(string label, string id, string? icon, bool primary = false, double height = 32, double radius = 10, double fontSize = 14)
    {
        var button = U.Action(label, id, primary);
        button.Height = height; button.CornerRadius = new(radius); button.FontSize = fontSize;
        button.Content = ActionContent(label, icon, fontSize, primary ? PrimaryInk : Themed("PrimaryTextBrush")); AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>A ghost button around an icon, the size of an icon button.</summary>
    internal static Button GhostIcon(string icon, string name, string id, double size = 36, double radius = 10)
    {
        var button = U.Action("", id); button.Width = button.Height = size; button.MinHeight = 0; button.Padding = new(0); button.CornerRadius = new(radius);
        button.Content = Icon(icon, 16, Themed("SecondaryTextBrush")); button.BorderThickness = new(0); AutomationProperties.SetName(button, name);
        return button;
    }
}

/// <summary>
/// The page switcher: a row of pills, one pressed, that scrolls sideways when they do not fit (official PillSwitcher). Each pill is a
/// toggle button named after its page; choosing the pressed pill does nothing.
/// </summary>
internal sealed class AdminHomeSectionsPills : Grid
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(2, 4, 2, 4) };
    private readonly List<(object Key, ToggleButton Pill)> _pills = [];
    private object? _selected;
    private bool _enabled = true, _updating;
    /// <summary>Raised only for a choice the admin made; never when the strip is rebuilt or its selection is set by the page.</summary>
    internal event Action<object>? Chosen;

    internal AdminHomeSectionsPills()
    {
        // ScrollViewer is sealed: the strip is a grid around one.
        Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled, Content = _row, HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    internal object? Selected => _selected;
    internal int Count => _pills.Count;
    internal IEnumerable<ToggleButton> Pills => _pills.Select(entry => entry.Pill);

    internal bool PillsEnabled
    {
        get => _enabled;
        set { _enabled = value; foreach (var (_, pill) in _pills) pill.IsEnabled = value; }
    }

    internal void Set(IEnumerable<(object Key, string Label, string AutomationId, bool Home)> pages, object selected)
    {
        _updating = true;
        try
        {
            _row.Children.Clear(); _pills.Clear(); var first = true;
            foreach (var (key, label, id, home) in pages)
            {
                // A thin divider separates Home from the library pages.
                if (!first && _pills.Count == 1) _row.Children.Add(new Border { Width = 1, Height = 20, Margin = new(4, 0, 4, 0), Background = AdminHomeSectionsUi.Themed("BorderBrush"), VerticalAlignment = VerticalAlignment.Center });
                first = false;
                var pill = Pill(label, home); AutomationProperties.SetAutomationId(pill, id); AutomationProperties.SetName(pill, label);
                var captured = key;
                // Checked and Unchecked rather than Click, so a touch, a mouse and UI Automation's toggle all choose the same way. The strip's own changes are guarded.
                pill.Checked += (_, _) => { if (!_updating) Click(captured); };
                pill.Unchecked += (_, _) => { if (!_updating && _selected != null) Select(_selected); };
                pill.IsEnabled = _enabled; _row.Children.Add(pill); _pills.Add((key, pill));
            }
            Select(selected);
        }
        finally { _updating = false; }
    }

    internal void Select(object key)
    {
        _updating = true;
        try
        {
            _selected = key;
            foreach (var (pillKey, pill) in _pills) { var on = Equals(pillKey, key); pill.IsChecked = on; ApplyLook(pill, on); }
        }
        finally { _updating = false; }
    }

    private void Click(object key)
    {
        if (_updating) return;
        // A click on the pressed pill keeps it pressed.
        if (Equals(key, _selected)) { Select(key); return; }
        Chosen?.Invoke(key);
        // A toggle button flips itself; the page decides, so the strip shows what is still chosen until the page says otherwise.
        Select(_selected!);
    }

    private static ToggleButton Pill(string label, bool home)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (home) content.Children.Add(new ContentControl { Content = AdminHomeSectionsUi.Icon("house", 15, AdminHomeSectionsUi.Themed("SecondaryTextBrush")), Tag = "icon" });
        content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        var pill = new ToggleButton { Content = content, Height = 34, MinHeight = 0, MinWidth = 0, Padding = new(14, 0, 14, 0), CornerRadius = new(17), BorderThickness = new(1), UseSystemFocusVisuals = true };
        pill.Template = (ControlTemplate)XamlReader.Load("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
              <Border x:Name="Root" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.85"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.7"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Root.Opacity" Value="0.6"/></VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <ContentPresenter Content="{TemplateBinding Content}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        return pill;
    }

    private static void ApplyLook(ToggleButton pill, bool pressed)
    {
        pill.Background = pressed ? U.Accent : new SolidColorBrush(Colors.Transparent);
        pill.BorderBrush = pressed ? new SolidColorBrush(Colors.Transparent) : AdminHomeSectionsUi.Themed("BorderBrush");
        var foreground = pressed ? new SolidColorBrush(Color.FromArgb(255, 21, 21, 26)) : AdminHomeSectionsUi.Themed("PrimaryTextBrush");
        pill.Foreground = foreground;
        if (pill.Content is StackPanel panel)
            foreach (var child in panel.Children)
            {
                if (child is TextBlock text) text.Foreground = foreground;
                else if (child is ContentControl { Tag: "icon" } holder) holder.Content = AdminHomeSectionsUi.Icon("house", 15, foreground);
            }
    }
}
