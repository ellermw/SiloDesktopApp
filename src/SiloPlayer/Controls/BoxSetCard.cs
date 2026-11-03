using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Converters;
namespace SiloPlayer.Controls;

/// <summary>Aggregate destination with no title mutation or quick-play surface.</summary>
public sealed class BoxSetCard : Button
{
    private Grid? _artworkStack;
    private StackPanel? _panel;
    private TextBlock? _title, _subtitle;
    public BoxSetSummary? Summary { get; private set; }
    public event Action<BoxSetSummary>? Invoked;
    public BoxSetCard() { Width = 170; Padding = new Thickness(0); Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); Click += (_, _) => { if (Summary is {} set) Invoked?.Invoke(set); }; }
    /// <summary>Fit the shared aggregate presentation inside a virtual catalog slot.</summary>
    public void SetCatalogLayout(double width, double posterHeight, double height)
    {
        Width = width; Height = height; MinWidth = 0; Padding = new Thickness(0); BorderThickness = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Top;
        if (_artworkStack is null || _panel is null || _title is null || _subtitle is null) return;
        _artworkStack.Height = Math.Max(1, posterHeight - 8); _artworkStack.Width = Math.Max(1, width - 8);
        _panel.Spacing = 4;
        _title.FontSize = 14; _title.MaxLines = 1; _title.TextWrapping = TextWrapping.NoWrap; _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle.FontSize = 11; _subtitle.MaxLines = 1; _subtitle.TextWrapping = TextWrapping.NoWrap; _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
    }
    public void SetSummary(BoxSetSummary set, string? posterUrl = null, string? posterThumbhash = null)
    {
        Summary = set;
        var panel = _panel = new StackPanel { Spacing = 6 };
        var stack = _artworkStack = new Grid { Height = 245, Margin = new Thickness(0, 8, 8, 0) };
        stack.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.DimGray), CornerRadius = new CornerRadius(9), Margin = new Thickness(8, -8, -8, 8) });
        stack.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Gray), CornerRadius = new CornerRadius(9), Margin = new Thickness(4, -4, -4, 4) });
        var face = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray) };
        var fallback = new DefaultArtwork();
        fallback.Reset("movie", posterThumbhash ?? set.PosterThumbhash);
        _ = fallback.ShowThumbhashAsync();
        face.Children.Add(fallback);
        var artworkUrl = !string.IsNullOrWhiteSpace(posterUrl) ? posterUrl : !string.IsNullOrWhiteSpace(set.PosterUrl) ? set.PosterUrl : set.BackdropUrl;
        if (!string.IsNullOrWhiteSpace(artworkUrl))
            face.Children.Add(new Image { Source = new UrlToImageSourceConverter().Convert(artworkUrl, typeof(Microsoft.UI.Xaml.Media.ImageSource), null!, "") as Microsoft.UI.Xaml.Media.ImageSource, Stretch = Stretch.UniformToFill });
        face.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = "Box set" } });
        face.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = new TextBlock { Text = $"{set.MovieCount} films" } });
        stack.Children.Add(face); panel.Children.Add(stack);
        var progress = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, set.MovieCount), Value = Math.Clamp(set.PlayedCount, 0, Math.Max(0, set.MovieCount)), Height = 4 };
        AutomationProperties.SetName(progress, "Films watched");
        AutomationProperties.SetHelpText(progress, set.ProgressText);
        panel.Children.Add(progress);
        panel.Children.Add(_title = new TextBlock { Text = set.Title, TextWrapping = TextWrapping.Wrap, MaxLines = 2, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(_subtitle = new TextBlock { Text = set.PlayedCount > 0 ? set.ProgressText : set.Years, TextWrapping = TextWrapping.Wrap });
        Content = panel;
        AutomationProperties.SetName(this, $"{set.Title}, Box set, {set.MovieCount} films, {set.ProgressText}");
    }
}
