using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Controls;

/// <summary>Official RatingEntry marks: provider text and server display, with TMDB's approved SVG only.</summary>
public static class DisplayRatingEntry
{
    public static FrameworkElement Create(DisplayRating rating, bool small = false, Brush? foreground = null)
    {
        foreground ??= (Brush)Application.Current.Resources["PrimaryTextBrush"];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (rating.Source == "tmdb")
        {
            var height = small ? 8d : 10d;
            var mark = new Image { Height = height, Width = height * 273.42 / 35.52, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center, Source = new SvgImageSource(new Uri("ms-appx:///Assets/Ratings/tmdb-logo.svg")) };
            AutomationProperties.SetName(mark, rating.Name); row.Children.Add(mark);
        }
        else
        {
            row.Children.Add(new TextBlock { Text = rating.Name, FontSize = small ? 12 : 13,
                LineHeight = small ? 16 : 19.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                FontWeight = FontWeights.SemiBold, CharacterSpacing = -25, Opacity = .75,
                Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        }
        row.Children.Add(new TextBlock { Text = rating.Display, FontSize = small ? 14 : 15,
            LineHeight = small ? 20 : 22.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            FontWeight = FontWeights.Bold, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(row, $"{rating.Name} {rating.Display}");
        return row;
    }
}
