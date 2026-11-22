using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;

namespace SiloPlayer.Views.Admin;

/// <summary>The Lucide glyphs the Nodes page needs that the shared Admin icon set does not draw (it would show a plus instead): resume, key and info.</summary>
internal static class AdminPlaybackNodeIcons
{
    private static string? Path(string name) => name switch
    {
        "circle-play" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M10 8l6 4-6 4z",
        "key-round" => "M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z M16.5 7.5h.01",
        "info" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 16v-4 M12 8h.01",
        _ => null,
    };
    internal static Viewbox Icon(string name, double size, Brush foreground)
    {
        if (Path(name) is not { } data) return AdminMonitorUi.Icon(name, size, foreground);
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground; path.StrokeThickness = 2; canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
