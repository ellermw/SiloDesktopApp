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
    public static Viewbox Create(string name, double size = 16, Brush? foreground = null, double strokeThickness = 2)
    {
        if (name is "lock-keyhole" or "lock-keyhole-open" or "x" or "settings" or "circle-user" or "log-out" or "chevron-right" or "chevron-down" or "bell" or "bell-off" or "folder" or "copy" or "hourglass" or "clock" or "external-link")
            return new Viewbox { Width = size, Height = size, Child = Navigation(name, 24, foreground), IsHitTestVisible = false };
        var data = name switch
        {
            "lock" => "M6 11h12a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M8 11V7a4 4 0 0 1 8 0v4",
            "pencil" => "m18 2 4 4-14 14-6 2 2-6z M15 5l4 4",
            "eye" => "M2 12s3-7 10-7 10 7 10 7-3 7-10 7S2 12 2 12z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
            "tv" => "M17 2l-5 5-5-5 M4 7h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z",
            "headphones" => "M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V12a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3",
            "book" => "M4 19.5A2.5 2.5 0 0 1 6.5 17H20 M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z",
            "languages" => "m5 8 6 6 m4 14 6-6 2-3 M2 5h12 M7 2h1 m22 22-5-10-5 10 M14 18h6",
            "film" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M7 3v18 M3 7.5h4 M3 12h18 M3 16.5h4 M17 3v18 M17 7.5h4 M17 16.5h4",
            "info" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 16v-4 M12 8h.01",
            "circle-alert" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 8v4 M12 16h.01",
            "plus" => "M12 5v14 M5 12h14",
            "skip-forward" => "m4 4 12 8-12 8z M20 4v16",
            "list-end" => "M4 5h16 M4 9h16 M4 13h5 M4 17h5 m12-4 4 4-4 4 M13 17h8",
            "trash-2" => "M3 6h18 M19 6v14a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6 M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2 M10 10v7 M14 10v7",
            "compass" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 m-5.76-4.24-1.804 5.411a2 2 0 0 1-1.265 1.265L7.76 16.24l1.804-5.411a2 2 0 0 1 1.265-1.265z",
            "ellipsis" => "M13 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M20 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M6 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0",
            // Lucide0.576.0 nodes, including each closed circle rather than filled dots.
            "users" => "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M16 3.128a4 4 0 0 1 0 7.744 M22 21v-2a4 4 0 0 0-3-3.87 M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
            "user" => "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
            "list-ordered" => "M11 5h10 M11 12h10 M11 19h10 M4 4h1v5 M4 9h2 M6.5 20H3.4c0-1 2.6-1.925 2.6-3.5a1.5 1.5 0 0 0-2.6-1.02",
            "wand-sparkles" => "m21.64 3.64-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72 m14 7 3 3 M5 6v4 M19 14v4 M10 2v2 M7 8H3 M21 16h-4 M11 3H9",
            "grip-vertical" => "M10 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M10 5a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M10 19a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M16 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M16 5a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M16 19a1 1 0 1 1-2 0 1 1 0 0 1 2 0",
            "arrow-right" => "M5 12h14 M12 5l7 7-7 7",
            "check" => "M20 6 9 17l-5-5",
            "heart" => "M2 9.5a5.5 5.5 0 0 1 9.591-3.676.56.56 0 0 0 .818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 .019L5 15c-1.5-1.5-3-3.2-3-5.5",
            "sparkles" => "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z M20 2v4 M22 4h-4 M6 20a2 2 0 1 1-4 0 2 2 0 0 1 4 0",
            "map" => "M14.106 5.553a2 2 0 0 0 1.788 0l3.659-1.83A1 1 0 0 1 21 4.619v12.764a1 1 0 0 1-.553.894l-4.553 2.277a2 2 0 0 1-1.788 0l-4.212-2.106a2 2 0 0 0-1.788 0l-3.659 1.83A1 1 0 0 1 3 19.381V6.618a1 1 0 0 1 .553-.894l4.553-2.277a2 2 0 0 1 1.788 0z M15 5.764v15 M9 3.236v15",
            "back" => "M15 18l-6-6 6-6",
            "bookmark" or "bookmark-check" => "M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z",
            "library" => "M3 3v18 M7 3v18 M11 3v18 M16 3l5 18",
            "search" => "M21 21l-4.34-4.34 M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
            "refresh-cw" => "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5",
            "rotate-ccw" => "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5",
            "check-check" => "M18 6 7 17l-5-5 m20-2-7.5 7.5L13 16",
            "settings-2" => "M14 17H5 M19 7h-9 M20 17a3 3 0 1 1-6 0 3 3 0 0 1 6 0 M10 7a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
            _ => "M12 5v14 M5 12h14"
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>" );
        path.Stroke = foreground ?? (Brush)Application.Current.Resources["PrimaryTextBrush"];
        path.StrokeThickness = strokeThickness;
        canvas.Children.Add(path);
        if (name == "bookmark-check")
        {
            var check = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M9 10l2 2 4-4' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>" );
            check.Stroke = path.Stroke; canvas.Children.Add(check);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
