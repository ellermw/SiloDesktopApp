using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>24-unit stroke icons used by the corresponding WebUI controls.</summary>
public static class WebUiIcon
{
    public static ImageIcon Navigation(string name, double size = 18, Brush? foreground = null)
        => new() { Width = size, Height = size, Foreground = foreground ?? (Brush)Application.Current.Resources["PrimaryTextBrush"],
            Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri($"ms-appx:///Assets/Icons/{name}.svg")) };
    public static Viewbox Create(string name, double size = 16, Brush? foreground = null)
    {
        if (name is "lock-keyhole" or "lock-keyhole-open" or "x" or "settings" or "circle-user" or "log-out" or "chevron-right" or "chevron-down" or "bell" or "bell-off" or "folder" or "copy" or "hourglass" or "clock" or "external-link")
            return new Viewbox { Width = size, Height = size, Child = Navigation(name, 24, foreground), IsHitTestVisible = false };
        var data = name switch
        {
            "tv" => "M17 2l-5 5-5-5 M4 7h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z",
            "languages" => "m5 8 6 6 m4 14 6-6 2-3 M2 5h12 M7 2h1 m22 22-5-10-5 10 M14 18h6",
            "film" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M7 3v18 M3 7.5h4 M3 12h18 M3 16.5h4 M17 3v18 M17 7.5h4 M17 16.5h4",
            "info" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 16v-4 M12 8h.01",
            "plus" => "M12 5v14 M5 12h14",
            "arrow-right" => "M5 12h14 M12 5l7 7-7 7",
            "check" => "M20 6 9 17l-5-5",
            "heart" => "M2 9.5a5.5 5.5 0 0 1 9.591-3.676.56.56 0 0 0 .818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 .019L5 15c-1.5-1.5-3-3.2-3-5.5",
            "sparkles" => "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z M20 2v4 M22 4h-4 M6 20a2 2 0 1 1-4 0 2 2 0 0 1 4 0",
            "map" => "M14.106 5.553a2 2 0 0 0 1.788 0l3.659-1.83A1 1 0 0 1 21 4.619v12.764a1 1 0 0 1-.553.894l-4.553 2.277a2 2 0 0 1-1.788 0l-4.212-2.106a2 2 0 0 0-1.788 0l-3.659 1.83A1 1 0 0 1 3 19.381V6.618a1 1 0 0 1 .553-.894l4.553-2.277a2 2 0 0 1 1.788 0z M15 5.764v15 M9 3.236v15",
            "back" => "M15 18l-6-6 6-6",
            "bookmark" or "bookmark-check" => "M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z",
            "library" => "M3 3v18 M7 3v18 M11 3v18 M16 3l5 18",
            "search" => "M21 21l-4.34-4.34 M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
            "check-check" => "M18 6 7 17l-5-5 m20-2-7.5 7.5L13 16",
            "settings-2" => "M14 17H5 M19 7h-9 M20 17a3 3 0 1 1-6 0 3 3 0 0 1 6 0 M10 7a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
            _ => "M12 5v14 M5 12h14"
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>" );
        path.Stroke = foreground ?? (Brush)Application.Current.Resources["PrimaryTextBrush"];
        canvas.Children.Add(path);
        if (name == "bookmark-check")
        {
            var check = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M9 10l2 2 4-4' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>" );
            check.Stroke = path.Stroke; canvas.Children.Add(check);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
