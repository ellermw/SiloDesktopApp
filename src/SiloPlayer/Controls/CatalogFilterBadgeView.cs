using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>The secondary badge and separate remove action used by catalog filters.</summary>
public static class CatalogFilterBadgeView
{
    public static Border Build(string label, Action remove)
    {
        var foreground = (Brush)Application.Current.Resources["SecondaryForegroundBrush"];
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = label, FontFamily = new FontFamily("Outfit"), FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium, LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = foreground, VerticalAlignment = VerticalAlignment.Center,
        });
        var icon = (Viewbox)XamlReader.Load("<Viewbox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='12' Height='12'><Canvas Width='24' Height='24'><Path Data='M18 6 6 18 M6 6l12 12' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' /></Canvas></Viewbox>");
        ((Microsoft.UI.Xaml.Shapes.Path)((Canvas)icon.Child).Children[0]).Stroke = foreground;
        var button = new Button
        {
            Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(2),
            Margin = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(2),
            Content = icon, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, $"Remove {label}");
        button.Click += (_, _) => remove();
        button.PointerEntered += (_, _) => button.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
        button.PointerExited += (_, _) => button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        content.Children.Add(button);
        return new Border
        {
            Child = content, Tag = label, Background = (Brush)Application.Current.Resources["SecondaryBackgroundBrush"],
            Padding = new Thickness(8, 2, 4, 2), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2d / 3), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            UseLayoutRounding = false, VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
