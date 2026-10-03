using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Helpers;

/// <summary>The WebUI PasswordInput's explicit, keyboard-accessible reveal action.</summary>
public static class PasswordReveal
{
    public static Grid Wrap(PasswordBox input)
    {
        input.PasswordRevealMode = PasswordRevealMode.Hidden;
        input.Padding = new Thickness(input.Padding.Left, input.Padding.Top, 40, input.Padding.Bottom);
        var surface = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        surface.SetBinding(UIElement.VisibilityProperty, new Binding { Source = input, Path = new PropertyPath("Visibility"), Mode = BindingMode.OneWay });
        surface.Children.Add(input);
        var reveal = new Button
        {
            Width = 40, Height = 36, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Style = (Style)Application.Current.Resources["GhostButtonStyle"], CornerRadius = input.CornerRadius
        };
        void Update()
        {
            var visible = input.PasswordRevealMode == PasswordRevealMode.Visible;
            reveal.Content = Eye(visible, (Brush)Application.Current.Resources["SecondaryTextBrush"]);
            AutomationProperties.SetName(reveal, visible ? "Hide password" : "Show password");
            AutomationProperties.SetItemStatus(reveal, visible ? "Revealed" : "Masked");
        }
        reveal.Click += (_, _) =>
        {
            input.PasswordRevealMode = input.PasswordRevealMode == PasswordRevealMode.Visible ? PasswordRevealMode.Hidden : PasswordRevealMode.Visible;
            Update();
        };
        Update(); surface.Children.Add(reveal);
        return surface;
    }

    // Exact Lucide0.576 geometry, drawn as native strokes so theme brushes apply.
    // SvgImageSource currentColor does not inherit ImageIcon.Foreground here.
    private static Viewbox Eye(bool hidden, Brush foreground)
    {
        var data = hidden
            ? "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 M2 2l20 20"
            : "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0";
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>" );
        path.Stroke = foreground;
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        canvas.Children.Add(path);
        return new Viewbox { Width = 16, Height = 16, Child = canvas, IsHitTestVisible = false };
    }

    public static void WrapInParent(PasswordBox input, Panel parent)
    {
        var index = parent.Children.IndexOf(input);
        if (index < 0) throw new InvalidOperationException("PasswordInput is not in its declared auth form panel.");
        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, Wrap(input));
    }
}
