using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Small presentation primitives for Manage › Libraries (WebUI shadcn Badge, ghost icon
/// Button, Switch setting card, CollapsibleDiagnosticsSection chrome) and the lucide
/// 0.576.0 icons the page uses. Icon paths are written as independent absolute subpaths.
/// </summary>
internal static class AdminLibrariesUi
{
    internal static string T(string key, params (string Name, object? Value)[] values) => AdminLibrariesText.T(key, values);

    /// <summary>WebUI <c>formatDateTime</c> (date, then time with seconds) through the shared preference formatter, so the user's date and time format settings apply.</summary>
    internal static string DateTimeText(DateTimeOffset value) =>
        SiloPlayer.Helpers.DateTimeDisplay.FormatDate(value) + ", " + SiloPlayer.Helpers.DateTimeDisplay.FormatTime(value, seconds: true);

    internal static Brush Brush(string key) => LogsNativeUi.Brush(key);
    internal static Brush Tint(string key, double alpha) => LogsNativeUi.Tint(key, alpha);
    internal static SolidColorBrush Solid(Color color, double alpha = 1) => AdminMonitorUi.Solid(color, alpha);
    internal static Color Destructive => AdminMonitorUi.Destructive;
    internal static Color Warning => AdminMonitorUi.Warning;
    internal static Color Success => AdminMonitorUi.Emerald400;
    internal static Color Amber => AdminMonitorUi.Amber500;
    internal static Color Primary => AdminMonitorUi.Primary;

    // Radii follow the WebUI theme radius (--radius .75rem, e130f262d app.css): rounded-sm 8, -md 10, -lg 12, -xl 16, -2xl 20.
    internal const double RadiusSm = 8, RadiusMd = 10, RadiusLg = 12, RadiusXl = 16, Radius2Xl = 20;

    /// <summary>
    /// The solid fill under white text on destructive badges and buttons: the official fixed <c>--destructive-fill</c> (#dc2626, 4.83:1 under
    /// white; added in #4700), from <see cref="AdminLibrariesPalette"/>. A plain computed property: it reads no theme resource, so it cannot throw from a static initializer.
    /// </summary>
    internal static Color DestructiveFill => ToColor(AdminLibrariesPalette.DestructiveFill);

    private static Color ToColor(AdminLibrariesPalette.Rgb color, byte alpha = 255) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Style? _destructiveStyle;
    private static Style? _stopStyle;

    /// <summary>
    /// The style for a named theme button style. <c>DestructiveButtonStyle</c> is this page's own: rest is the official fill, and hover and pressed are the
    /// fill at 90% (<c>hover:bg-destructive-fill/90</c>) instead of the shared template's lighter theme red, so white text keeps 4.5:1 in every state.
    /// The shared <c>DestructiveButtonStyle</c> is untouched.
    /// </summary>
    internal static Style ButtonStyle(string name) => name == "DestructiveButtonStyle" ? DestructiveStyle() : (Style)Application.Current.Resources[name];

    internal static Style DestructiveStyle()
    {
        if (_destructiveStyle != null) return _destructiveStyle;
        var hover = ToColor(AdminLibrariesPalette.DestructiveFill, AdminLibrariesPalette.AlphaByte(AdminLibrariesPalette.DestructiveHoverOpacity));
        var template = (ControlTemplate)XamlReader.Load(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
            "<Border x:Name='WebUiRoot' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' " +
            "CornerRadius='{TemplateBinding CornerRadius}' Padding='{TemplateBinding Padding}'>" +
            "<ContentPresenter x:Name='WebUiContent' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' Foreground='{TemplateBinding Foreground}' " +
            "FontFamily='{TemplateBinding FontFamily}' FontSize='{TemplateBinding FontSize}' FontWeight='{TemplateBinding FontWeight}' " +
            "HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>" +
            "<VisualStateManager.VisualStateGroups><VisualStateGroup x:Name='CommonStates'>" +
            "<VisualState x:Name='Normal'/>" +
            $"<VisualState x:Name='PointerOver'><VisualState.Setters><Setter Target='WebUiRoot.Background' Value='{ArgbHex(hover)}'/></VisualState.Setters></VisualState>" +
            $"<VisualState x:Name='Pressed'><VisualState.Setters><Setter Target='WebUiRoot.Background' Value='{ArgbHex(hover)}'/></VisualState.Setters></VisualState>" +
            "<VisualState x:Name='Disabled'><VisualState.Setters><Setter Target='WebUiRoot.Opacity' Value='0.5'/></VisualState.Setters></VisualState>" +
            "</VisualStateGroup></VisualStateManager.VisualStateGroups></Border></ControlTemplate>");
        var style = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["DestructiveButtonStyle"] };
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Solid(DestructiveFill)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Solid(ToColor(AdminLibrariesPalette.DestructiveForeground))));
        return _destructiveStyle = style;
    }

    private static string ArgbHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// The stop-task button (<c>StopTaskButton</c>: <c>h-5 w-5 rounded</c>, <c>hover:bg-destructive/10</c>, a 2 px focus ring). The button keeps whatever size it is given
    /// (44 px on touch, where only the hit area grows, <c>after:-inset-3</c>) but the hover, pressed and focus shape is always the 20 px square inside it.
    /// </summary>
    internal static Style StopStyle()
    {
        if (_stopStyle != null) return _stopStyle;
        var hover = ToColor(new AdminLibrariesPalette.Rgb(Destructive.R, Destructive.G, Destructive.B), AdminLibrariesPalette.AlphaByte(AdminLibrariesPalette.StopHoverOpacity));
        var ring = Brush("PrimaryTextBrush") is SolidColorBrush primary ? primary.Color : Colors.White;
        var template = (ControlTemplate)XamlReader.Load(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
            "<Grid x:Name='HitArea' Background='Transparent'>" +
            "<Border x:Name='StopShape' Width='20' Height='20' CornerRadius='4' BorderThickness='2' BorderBrush='Transparent' Background='Transparent' HorizontalAlignment='Center' VerticalAlignment='Center'>" +
            "<ContentPresenter x:Name='StopContent' Content='{TemplateBinding Content}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name='CommonStates'>" +
            "<VisualState x:Name='Normal'/>" +
            $"<VisualState x:Name='PointerOver'><VisualState.Setters><Setter Target='StopShape.Background' Value='{ArgbHex(hover)}'/></VisualState.Setters></VisualState>" +
            $"<VisualState x:Name='Pressed'><VisualState.Setters><Setter Target='StopShape.Background' Value='{ArgbHex(hover)}'/></VisualState.Setters></VisualState>" +
            "<VisualState x:Name='Disabled'><VisualState.Setters><Setter Target='StopShape.Opacity' Value='0.5'/></VisualState.Setters></VisualState>" +
            "</VisualStateGroup>" +
            "<VisualStateGroup x:Name='FocusStates'>" +
            "<VisualState x:Name='Unfocused'/><VisualState x:Name='PointerFocused'/>" +
            $"<VisualState x:Name='Focused'><VisualState.Setters><Setter Target='StopShape.BorderBrush' Value='{ArgbHex(ring)}'/></VisualState.Setters></VisualState>" +
            "</VisualStateGroup></VisualStateManager.VisualStateGroups></Grid></ControlTemplate>");
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, false));
        return _stopStyle = style;
    }

    private static string? PathData(string name) => name switch
    {
        "grip-vertical" => "M9 11a1 1 0 1 1 0 2 1 1 0 0 1 0-2z M9 4a1 1 0 1 1 0 2 1 1 0 0 1 0-2z M9 18a1 1 0 1 1 0 2 1 1 0 0 1 0-2z M15 11a1 1 0 1 1 0 2 1 1 0 0 1 0-2z M15 4a1 1 0 1 1 0 2 1 1 0 0 1 0-2z M15 18a1 1 0 1 1 0 2 1 1 0 0 1 0-2z",
        "refresh-cw" => "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5",
        "square" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
        "database-backup" => "M3 5a9 3 0 1 0 18 0a9 3 0 1 0-18 0 M3 12a9 3 0 0 0 5 2.69 M21 9.3V5 M3 5v14a9 3 0 0 0 6.47 2.88 M12 12v4h4 M13 20a5 5 0 0 0 9-3 4.5 4.5 0 0 0-4.5-4.5c-1.33 0-2.54.54-3.41 1.41L12 16",
        "hard-drive" => "M22 12H2 M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z M6 16h.01 M10 16h.01",
        "circle-check" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M9 12l2 2 4-4",
        "alert-triangle" => "M21.73 18l-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01",
        "pencil" => "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z M15 5l4 4",
        "trash-2" => "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M10 11v6 M14 11v6",
        "plus" => "M5 12h14 M12 5v14",
        "wrench" => "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
        "scan-line" => "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10",
        "chevron-down" => "M6 9l6 6 6-6",
        "chevron-up" => "M18 15l-6-6-6 6",
        "chevron-right" => "M9 18l6-6-6-6",
        "chevron-left" => "M15 18l-6-6 6-6",
        "chevrons-left" => "M11 17l-5-5 5-5 M18 17l-5-5 5-5",
        "chevrons-right" => "M6 17l5-5-5-5 M13 17l5-5-5-5",
        "ellipsis" => "M13 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M20 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M6 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0",
        "arrow-up" => "M5 12l7-7 7 7 M12 19V5",
        "arrow-down" => "M12 5v14 M19 12l-7 7-7-7",
        "folder-open" => "M6 14l1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2",
        "folder-search" => "M10.7 20H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v4.1 M21 21l-1.9-1.9 M20 17a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
        "search" => "M21 21l-4.34-4.34 M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
        "eye-off" => "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 M2 2l20 20",
        "undo-2" => "M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5 5.5 5.5 0 0 1-5.5 5.5H11",
        "unlink" => "M18.84 12.25l1.72-1.71h-.02a5.004 5.004 0 0 0-.12-7.07 5.006 5.006 0 0 0-6.95 0l-1.72 1.71 M5.17 11.75l-1.71 1.71a5.004 5.004 0 0 0 .12 7.07 5.006 5.006 0 0 0 6.95 0l1.71-1.71 M8 2v3 M2 8h3 M16 22v-3 M22 16h-3",
        "image" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M11 9a2 2 0 1 1-4 0 2 2 0 0 1 4 0 M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21",
        "x" => "M18 6L6 18 M6 6l12 12",
        "film" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M7 3v18 M3 7.5h4 M3 12h18 M3 16.5h4 M17 3v18 M17 7.5h4 M17 16.5h4",
        "tv" => "M17 2l-5 5-5-5 M4 7h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z",
        "layers" => "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12 M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17",
        "book-headphones" => "M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H19a1 1 0 0 1 1 1v18a1 1 0 0 1-1 1H6.5a1 1 0 0 1 0-5H20 M8 12v-2a4 4 0 0 1 8 0v2 M8 12h1.5a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-.5.5H9a1 1 0 0 1-1-1z M16 12h-1.5a.5.5 0 0 0-.5.5v2a.5.5 0 0 0 .5.5H15a1 1 0 0 0 1-1z",
        "book-open" => "M12 7v14 M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z",
        "book-marked" => "M10 2v8l3-3 3 3V2 M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H19a1 1 0 0 1 1 1v18a1 1 0 0 1-1 1H6.5a1 1 0 0 1 0-5H20",
        "podcast" => "M16.85 18.58a9 9 0 1 0-9.7 0 M8 14a5 5 0 1 1 8 0 M13 10a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M13 17a1 1 0 1 0-2 0l.5 4.5a.5.5 0 1 0 1 0z",
        "settings-2" => "M14 17H5 M19 7h-9 M20 17a3 3 0 1 1-6 0 3 3 0 0 1 6 0 M10 7a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
        "database" => "M3 5a9 3 0 1 0 18 0a9 3 0 1 0-18 0 M3 5v14a9 3 0 0 0 18 0V5 M3 12a9 3 0 0 0 18 0",
        "sliders-horizontal" => "M10 5H3 M12 19H3 M14 3v4 M16 17v4 M21 12h-9 M21 19h-5 M21 5h-7 M8 10v4 M8 12H3",
        "languages" => "M5 8l6 6 M4 14l6-6 2-3 M2 5h12 M7 2h1 M22 22l-5-10-5 10 M14 18h6",
        "check" => "M20 6L9 17l-5-5",
        "loader" => "M12 2v4 M16.2 7.8l2.9-2.9 M18 12h4 M16.2 16.2l2.9 2.9 M12 18v4 M4.9 19.1l2.9-2.9 M2 12h4 M4.9 4.9l2.9 2.9",
        _ => null,
    };

    /// <summary>A lucide stroke icon (24-unit grid, round caps).</summary>
    internal static FrameworkElement Icon(string name, double size = 16, Brush? foreground = null, bool fill = false)
    {
        var data = PathData(name);
        if (data == null) return WebUiIcon.Create(name, size, foreground);
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data +
            "' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        var brush = foreground ?? Brush("PrimaryTextBrush");
        path.Stroke = brush;
        if (fill) path.Fill = brush;
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>A spinning icon (rotation only; WebUI <c>animate-spin</c>).</summary>
    internal static FrameworkElement Spinning(string name, double size, Brush? foreground = null, double seconds = 1)
    {
        var icon = Icon(name, size, foreground);
        var transform = new RotateTransform { CenterX = size / 2, CenterY = size / 2 };
        icon.RenderTransform = transform;
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(seconds)), EnableDependentAnimation = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
        };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Angle");
        story.Children.Add(animation);
        icon.Loaded += (_, _) => story.Begin();
        icon.Unloaded += (_, _) => story.Stop();
        return icon;
    }

    /// <summary>
    /// A <c>border-dashed</c> frame (WinUI borders cannot dash): the child on a rounded fill with a dashed stroke over it.
    /// Browsers draw a 1 px dashed border with 3 px dashes and 3 px gaps.
    /// </summary>
    internal static FrameworkElement DashedFrame(UIElement child, double radius, Brush stroke, Brush? fill)
    {
        var grid = new Grid();
        grid.Children.Add(new Border { Child = child, CornerRadius = new(radius), Background = fill });
        var outline = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            RadiusX = Math.Max(0, radius - 0.5), RadiusY = Math.Max(0, radius - 0.5), Stroke = stroke, StrokeThickness = 1, Margin = new(0.5),
            StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false,
        };
        grid.Children.Add(outline);
        return grid;
    }

    /// <summary>
    /// CSS <c>flex flex-wrap gap-2</c> whose items are <c>flex-1 min-w-fit</c> (<c>basis-full</c> for <see cref="FullRow"/> items): lines break by each item's natural
    /// width, then every item on a line grows equally, never narrower than its own natural width. Used by the page header's actions below 1024 px.
    /// </summary>
    internal sealed class FlexWrapStretchPanel : Panel
    {
        internal double ColumnSpacing { get; set; } = 8;
        internal double RowSpacing { get; set; } = 8;
        internal HashSet<UIElement> FullRow { get; } = [];

        // Each child's natural width, taken from the one unconstrained measure. The second measure pass narrows or widens the children,
        // which overwrites DesiredSize, so both passes (and Arrange) read these instead.
        private readonly Dictionary<UIElement, double> _natural = [];

        private double Natural(UIElement child) => _natural.TryGetValue(child, out var width) ? width : child.DesiredSize.Width;

        private List<List<UIElement>> Lines(double available)
        {
            var lines = new List<List<UIElement>>();
            List<UIElement>? line = null;
            var used = 0d;
            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                var width = FullRow.Contains(child) ? available : Natural(child);
                if (line == null || FullRow.Contains(child) || line.Any(FullRow.Contains) || used + ColumnSpacing + width > available + 0.5)
                {
                    line = []; lines.Add(line); used = width;
                }
                else used += ColumnSpacing + width;
                line.Add(child);
            }
            return lines;
        }

        private static double[] Widths(IReadOnlyList<UIElement> line, double available, double spacing, Func<UIElement, double> natural)
        {
            var widths = new double[line.Count];
            var frozen = new bool[line.Count];
            var room = available - spacing * (line.Count - 1);
            for (var pass = 0; pass < line.Count; pass++)
            {
                var open = Enumerable.Range(0, line.Count).Where(index => !frozen[index]).ToArray();
                if (open.Length == 0) break;
                var share = room / open.Length;
                var violated = open.Where(index => natural(line[index]) > share).ToArray();
                if (violated.Length == 0) { foreach (var index in open) widths[index] = share; break; }
                foreach (var index in violated) { widths[index] = natural(line[index]); frozen[index] = true; room -= widths[index]; }
            }
            return widths;
        }

        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
        {
            _natural.Clear();
            foreach (var child in Children)
            {
                child.Measure(new(double.PositiveInfinity, availableSize.Height));
                if (child.Visibility != Visibility.Collapsed) _natural[child] = child.DesiredSize.Width;
            }
            var shown = Children.Where(child => child.Visibility != Visibility.Collapsed).ToList();
            if (shown.Count == 0) return new(0, 0);
            // Unbounded width: everything fits on one line (items at their natural width plus the gaps between them), so basis-full items do not force breaks.
            var unbounded = double.IsInfinity(availableSize.Width);
            var available = unbounded ? shown.Sum(Natural) + ColumnSpacing * (shown.Count - 1) : availableSize.Width;
            var lines = unbounded ? new List<List<UIElement>> { shown } : Lines(available);
            Func<UIElement, double> floor = unbounded ? (child => Natural(child)) : (child => FullRow.Contains(child) ? 0 : Natural(child));
            var height = 0d;
            foreach (var line in lines)
            {
                var widths = Widths(line, available, ColumnSpacing, floor);
                var lineHeight = 0d;
                for (var index = 0; index < line.Count; index++) { line[index].Measure(new(widths[index], availableSize.Height)); lineHeight = Math.Max(lineHeight, line[index].DesiredSize.Height); }
                height += lineHeight;
            }
            height += Math.Max(0, lines.Count - 1) * RowSpacing;
            return new(available, height);
        }

        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            var y = 0d;
            foreach (var line in Lines(finalSize.Width))
            {
                var widths = Widths(line, finalSize.Width, ColumnSpacing, child => FullRow.Contains(child) ? 0 : Natural(child));
                var x = 0d;
                var lineHeight = line.Max(child => child.DesiredSize.Height);
                for (var index = 0; index < line.Count; index++)
                {
                    line[index].Arrange(new(x, y, widths[index], lineHeight));
                    x += widths[index] + ColumnSpacing;
                }
                y += lineHeight + RowSpacing;
            }
            return finalSize;
        }
    }

    /// <summary>WebUI <c>CardActions</c> with <c>h-11 min-w-0 flex-1</c> buttons: one row of equal-width columns, one per child of <paramref name="source"/> (every child is kept, not only buttons).</summary>
    internal static Grid CardActionRow(StackPanel source)
    {
        var children = source.Children.ToArray();
        source.Children.Clear();
        var row = new Grid { ColumnSpacing = 8 };
        for (var index = 0; index < children.Length; index++)
        {
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 0 });
            if (children[index] is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Stretch;
            children[index].SetValue(Grid.ColumnProperty, index);
            row.Children.Add(children[index]);
        }
        return row;
    }

    /// <summary>A flyout presenter with a WebUI popover or menu radius (the scan queue popover is <c>rounded-xl</c>, 16 px; the card menu is <c>rounded-md</c>, 10 px).</summary>
    internal static Style RoundedPresenterStyle(double radius)
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(radius)));
        return style;
    }

    /// <summary>The focused element under <paramref name="root"/>, or null before the element joins a visual tree.</summary>
    internal static FrameworkElement? Focused(XamlRoot? root) =>
        root == null ? null : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) as FrameworkElement;

    internal static TextBlock Text(string text, double size = 14, bool muted = false, bool mono = false) => LogsNativeUi.Text(text, size, muted, mono);

    internal static TextBlock Strong(string text, double size = 14)
    {
        var block = Text(text, size);
        block.FontWeight = FontWeights.Medium;
        return block;
    }

    internal static TextBlock Mono(string text, double size = 12, bool muted = true)
    {
        var block = Text(text, size, muted, mono: true);
        block.FlowDirection = FlowDirection.LeftToRight;
        return block;
    }

    internal enum BadgeTone { Secondary, Outline, Destructive, Warning }

    /// <summary>shadcn Badge: text-xs (or <paramref name="fontSize"/>), rounded-md (10), px-2 py-0.5; destructive is the solid destructive fill under white text.</summary>
    internal static Border Badge(string text, BadgeTone tone = BadgeTone.Secondary, string? icon = null, string? tooltip = null, double fontSize = 12)
    {
        var foreground = tone switch
        {
            BadgeTone.Destructive => Solid(Colors.White),
            BadgeTone.Warning => Solid(Warning),
            BadgeTone.Outline => Brush("PrimaryTextBrush"),
            _ => Brush("SecondaryForegroundBrush"), // bg-secondary text-secondary-foreground
        };
        var label = new TextBlock { Text = text, FontSize = fontSize, FontWeight = FontWeights.Medium, Foreground = foreground, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        UIElement child = label;
        if (icon != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(Icon(icon, 12, foreground));
            row.Children.Add(label);
            child = row;
        }
        var badge = new Border
        {
            Child = child, CornerRadius = new(RadiusMd), Padding = new(8, 2, 8, 2), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Left,
            Background = tone switch
            {
                BadgeTone.Destructive => Solid(DestructiveFill),
                BadgeTone.Secondary => Brush("SecondaryBackgroundBrush"),
                _ => new SolidColorBrush(Colors.Transparent),
            },
            BorderBrush = tone switch
            {
                BadgeTone.Warning => Solid(Warning, 0.4),
                BadgeTone.Outline => Tint("BorderBrush", 1),
                _ => new SolidColorBrush(Colors.Transparent),
            },
        };
        if (tooltip != null) ToolTipService.SetToolTip(badge, tooltip);
        AutomationProperties.SetName(badge, tooltip == null ? text : text + ". " + tooltip);
        return badge;
    }

    /// <summary>A ghost icon button (WebUI <c>variant="ghost" size="icon"</c>); the accessible name is required.</summary>
    internal static Button IconButton(string icon, string name, string automationId, double size = 28, double iconSize = 12, Brush? foreground = null, bool fillIcon = false)
    {
        var button = new Button
        {
            Content = Icon(icon, iconSize, foreground, fillIcon), Width = size, Height = size, MinWidth = size, MinHeight = size, Padding = new(0),
            CornerRadius = new(RadiusMd), Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, automationId);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    /// <summary>
    /// A labeled button (<c>variant="outline" size="sm"</c> by default): text-sm medium, rounded-md (10), px-3 (px-2.5 when an icon is a direct child),
    /// with the icon <paramref name="iconSize"/> and <paramref name="gap"/> the page's button uses (gap-1.5 = 6, plus the me-1 some icons add).
    /// </summary>
    internal static Button Action(string label, string automationId, string? icon = null, string style = "OutlineButtonStyle", Brush? iconBrush = null, double height = 32,
        double iconSize = 14, double gap = 6, double fontSize = 14)
    {
        var button = new Button { MinHeight = height, Height = height, Padding = new(icon == null ? 12 : 10, 0, icon == null ? 12 : 10, 0), CornerRadius = new(RadiusMd), FontSize = fontSize, FontWeight = FontWeights.Medium };
        button.Style = ButtonStyle(style);
        // variant="destructive": the solid destructive fill under white text (#4700); this page's style keeps hover and pressed on that fill too.
        var destructive = style == "DestructiveButtonStyle";
        if (destructive) { button.Background = Solid(DestructiveFill); button.Foreground = Solid(Colors.White); }
        if (icon == null) button.Content = label;
        else
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = gap, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(Icon(icon, iconSize, iconBrush ?? (destructive ? Solid(Colors.White) : style == "AccentButtonStyle" ? Brush("AccentForegroundBrush") : null)));
            var text = new TextBlock { Text = label, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center };
            if (destructive) text.Foreground = Solid(Colors.White);
            content.Children.Add(text);
            button.Content = content;
        }
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    /// <summary>A borderless toggle that reports its state (WebUI Switch); the label is the setting card's title.</summary>
    internal static ToggleSwitch Switch(string name, string automationId, bool on)
    {
        var toggle = new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 40, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(toggle, name);
        AutomationProperties.SetAutomationId(toggle, automationId);
        return toggle;
    }

    /// <summary>WebUI <c>SettingCard</c>: title, description, optional footer and a trailing control in a bordered surface.</summary>
    internal static Border SettingCard(string title, string description, FrameworkElement control, UIElement? footer = null)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(Strong(title));
        var help = Text(description, 12, true); help.LineHeight = 19.5;
        text.Children.Add(help);
        if (footer != null) text.Children.Add(footer);
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border { Child = grid, Padding = new(14), CornerRadius = new(RadiusXl), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("SurfaceBrush", 0.9) };
    }

    internal static Border Surface(UIElement child, double radius = Radius2Xl, double padding = 16) => new()
    {
        Child = child, CornerRadius = new(radius), Padding = new(padding), Background = Tint("SurfaceBrush", 0.9), BorderThickness = new(0),
    };

    /// <summary>
    /// WebUI <c>CollapsibleDiagnosticsSection</c>: a full-width header button (icon tile, title,
    /// description, count or "—"/"!", chevron) with <c>aria-expanded</c>; the body shows only when open.
    /// </summary>
    internal sealed class DiagnosticsSection : StackPanel
    {
        private readonly Button _header;
        private readonly Border _iconTile = new() { Width = 32, Height = 32, CornerRadius = new(RadiusLg) };
        private readonly ContentControl _count = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly ContentControl _chevron = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly string _title;
        internal Border Body { get; } = new() { Padding = new(12, 0, 12, 12) };
        internal event Action<bool>? Toggled;
        internal bool IsOpen { get; private set; }
        private readonly StackPanel _embeddedHeading = new() { Spacing = 2, Visibility = Visibility.Collapsed };
        /// <summary>Shown inside a Catalog Health tile (<c>embedded</c>): a heading and description over an always-visible body, no collapsible header.</summary>
        internal bool Embedded { get; private set; }

        /// <summary>
        /// The Catalog Health layout (<c>embedded</c> in the official sections): a <c>text-sm font-semibold</c> heading and a
        /// <c>text-xs</c> description over the body, which is always shown; the tile above it is the only way to pick the section.
        /// </summary>
        internal void SetEmbedded()
        {
            if (Embedded) return;
            Embedded = true;
            _header.Visibility = Visibility.Collapsed;
            Spacing = 12; // space-y-3
            Body.Padding = new(0);
            Body.Visibility = Visibility.Visible;
            _embeddedHeading.Visibility = Visibility.Visible;
            _count.Content = null;
        }

        /// <summary>WebUI <c>px-4 lg:px-5</c>: 20 px of header padding from the 1024 px breakpoint.</summary>
        internal void SetWide(bool wide) => _header.Padding = new(wide ? 20 : 16, 16, wide ? 20 : 16, 16);

        internal DiagnosticsSection(string key, string title, string description)
        {
            _title = title;
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(_iconTile);
            var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var heading = Text(title, 14); heading.FontWeight = FontWeights.SemiBold; heading.CharacterSpacing = 25;
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            titles.Children.Add(heading);
            var help = Text(description, 12, true); help.LineHeight = 18;
            titles.Children.Add(help);
            Grid.SetColumn(titles, 1); row.Children.Add(titles);
            Grid.SetColumn(_count, 2); row.Children.Add(_count);
            Grid.SetColumn(_chevron, 3); row.Children.Add(_chevron);
            _header = new Button
            {
                Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new(16), MinHeight = 44, CornerRadius = new(Radius2Xl), BorderThickness = new(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            };
            AutomationProperties.SetAutomationId(_header, "LibrariesSection_" + key);
            _header.Click += (_, _) => Toggled?.Invoke(!IsOpen);
            var embeddedTitle = Text(title, 14); embeddedTitle.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetHeadingLevel(embeddedTitle, AutomationHeadingLevel.Level2);
            var embeddedHelp = Text(description, 12, true);
            _embeddedHeading.Children.Add(embeddedTitle); _embeddedHeading.Children.Add(embeddedHelp);
            AutomationProperties.SetAutomationId(_embeddedHeading, "LibrariesSectionHeading_" + key);
            Children.Add(_header);
            Children.Add(_embeddedHeading);
            Children.Add(Body);
            Body.Visibility = Visibility.Collapsed;
            SetOpen(false);
        }

        internal void SetIcon(string icon, Color tone, double tileAlpha = 0.10)
        {
            _iconTile.Background = Solid(tone, tileAlpha);
            _iconTile.Child = new Border { Child = Icon(icon, 16, Solid(tone)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        internal void SetMutedIcon(string icon)
        {
            _iconTile.Background = Tint("MutedBrush", 0.5);
            _iconTile.Child = new Border { Child = Icon(icon, 16, Brush("SecondaryTextBrush")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        internal void SetOpen(bool open)
        {
            IsOpen = open;
            Body.Visibility = open || Embedded ? Visibility.Visible : Visibility.Collapsed;
            _chevron.Content = Icon(open ? "chevron-down" : "chevron-right", 16, Brush("SecondaryTextBrush"));
            AutomationProperties.SetItemStatus(_header, open ? "expanded" : "collapsed");
            AutomationProperties.SetName(_header, _title);
            var peer = FrameworkElementAutomationPeer.FromElement(_header);
            peer?.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                open ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded, open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }

        /// <summary>The count: "—" while unknown (never read as zero), "!" after a load failure.</summary>
        internal void SetCount(long? count, bool error = false)
        {
            if (Embedded) return; // the count is on the tile
            string shown; string spoken;
            if (error) { shown = "!"; spoken = T("web.admin.common.diagnosticsSection.countError"); }
            else if (count is not { } value) { shown = "—"; spoken = T("web.admin.common.diagnosticsSection.countNotLoaded"); }
            else { shown = value.ToString("N0", Core.Localization.AppStrings.FormatCulture); spoken = shown; }
            if (IsOpen)
            {
                var badge = Badge(shown, error ? BadgeTone.Destructive : BadgeTone.Secondary, fontSize: 11);
                AutomationProperties.SetName(badge, spoken);
                _count.Content = badge;
            }
            else
            {
                var text = new TextBlock
                {
                    Text = shown, FontSize = 24, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = error ? Solid(Destructive) : count is null or 0 ? Brush("SecondaryTextBrush") : Brush("PrimaryTextBrush"),
                };
                AutomationProperties.SetName(text, spoken);
                _count.Content = text;
            }
            AutomationProperties.SetHelpText(_header, spoken);
        }
    }
}
