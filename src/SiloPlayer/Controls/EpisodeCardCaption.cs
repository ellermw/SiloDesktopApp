using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>The shared caption below detail grid and sibling-carousel stills.</summary>
public sealed class EpisodeCardCaption : StackPanel
{
    public Grid WatchedIndicator { get; }
    public TextBlock TitleText { get; }
    public TextBlock MetadataText { get; }
    public TextBlock OverviewText { get; }
    public Grid MetadataRow { get; }
    public EpisodeCaptionPresentation Presentation { get; }

    public EpisodeCardCaption(Episode episode, string? formattedAirDate, bool hideOverview,
        bool compactTv, bool current)
        : this(episode, formattedAirDate, hideOverview, compactTv, current, episode.UserData?.Played == true) { }

    public EpisodeCardCaption(Episode episode, string? formattedAirDate, bool hideOverview,
        bool compactTv, bool current, bool watched)
    {
        Name = "EpisodeCardCaption";
        Presentation = EpisodeCardPresentation.Caption(episode, formattedAirDate, hideOverview);
        Spacing = 4;
        Margin = new Thickness(0, 8, 0, 0);
        var muted = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        TitleText = new TextBlock
        {
            Text = Presentation.Title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources[current ? "AccentBrush" : "PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
        };
        AutomationProperties.SetName(TitleText, Presentation.SpokenTitle);
        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titleRow.Children.Add(TitleText);
        WatchedIndicator = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center,
            Visibility = watched ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(WatchedIndicator, "Watched");
        WatchedIndicator.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Stroke = muted, StrokeThickness = 1.5 });
        WatchedIndicator.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 8, Foreground = muted,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(WatchedIndicator, 1); titleRow.Children.Add(WatchedIndicator);
        Children.Add(titleRow);

        MetadataRow = new Grid { ColumnSpacing = 8 };
        MetadataRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        MetadataRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        MetadataRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        MetadataText = new TextBlock { Text = Presentation.Metadata, FontSize = 12, Foreground = muted,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        MetadataRow.Children.Add(MetadataText);
        if (Presentation.ShowUnreadable)
        {
            var notice = new TextBlock { Text = "⚠ Damaged file", FontSize = 11,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(notice, "Silo couldn't read this file. It appears to be empty or damaged.");
            Grid.SetColumn(notice, 1); MetadataRow.Children.Add(notice);
        }
        if (Presentation.ShowTranslation)
        {
            var translated = WebUiIcon.Create("languages", 14, muted);
            ToolTipService.SetToolTip(translated, "AI-translated overview");
            AutomationProperties.SetName(translated, "AI-translated overview");
            Grid.SetColumn(translated, 2); MetadataRow.Children.Add(translated);
        }
        MetadataRow.Visibility = compactTv || Presentation.Metadata.Length == 0 &&
            !Presentation.ShowUnreadable && !Presentation.ShowTranslation ? Visibility.Collapsed : Visibility.Visible;
        Children.Add(MetadataRow);
        OverviewText = new TextBlock
        {
            Text = Presentation.Overview, FontSize = 12, Foreground = muted,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3, LineHeight = 18, MinHeight = 54,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Visibility = compactTv || Presentation.Overview.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
        };
        Children.Add(OverviewText);
    }
}
