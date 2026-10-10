using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Models.Home;
using Windows.System;

namespace SiloPlayer.Views;

public sealed partial class CollectionEditorPage
{
    private bool _manualSearchOpen = true;
    private int _manualSearchHighlight;

    private async Task HandleManualTitleKeyAsync(VirtualKey key)
    {
        var count = ViewModel.SearchResults.Count;
        if (key == VirtualKey.Escape) { _manualSearchOpen = false; UpdateManualContents(ActualWidth < 640); return; }
        if (key is VirtualKey.Up or VirtualKey.Down)
        {
            _manualSearchOpen = true;
            if (count > 0)
            {
                _manualSearchHighlight = (_manualSearchHighlight + (key == VirtualKey.Down ? 1 : -1) + count) % count;
                BuildSearchResultsUI();
                SearchResultsPanel.Children[_manualSearchHighlight].StartBringIntoView();
            }
            return;
        }
        if (key == VirtualKey.Enter && _manualSearchOpen && count > 0)
            await AddSearchedTitleAsync(ViewModel.SearchResults[Math.Clamp(_manualSearchHighlight, 0, count - 1)]);
    }

    private bool TitleAlreadyAdded(MediaItem item) => ViewModel.ManualItems.Any(m => m.MediaItemId == item.ContentId || m.ContentId == item.ContentId);
    private async Task AddSearchedTitleAsync(MediaItem item)
    {
        if (TitleAlreadyAdded(item) || ViewModel.IsManualMutationPending) return;
        await ViewModel.AddManualItemCommand.ExecuteAsync(item);
        BuildSearchResultsUI();
    }

    private Border BuildSearchResultRow(MediaItem item)
    {
        var added = TitleAlreadyAdded(item);
        var row = new Grid { ColumnSpacing = 12, Padding = new(10, 8, 10, 8) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var poster = new Border { Width = 36, Height = 54, CornerRadius = new(6), Background = CurrentBrush("SurfaceBrush") };
        if (!string.IsNullOrWhiteSpace(item.PosterUrl)) poster.Child = new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new UrlToImageSourceConverter().Convert(item.PosterUrl, typeof(ImageSource), null!, "") };
        row.Children.Add(poster);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = item.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var type = item.Type switch { "movie" => "Movie", "series" => "Show", "season" => "Season", "episode" => "Episode", _ => item.Type };
        copy.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { item.Year > 0 ? item.Year.ToString() : null, type }.Where(v => !string.IsNullOrEmpty(v))), FontSize = 12.5, Foreground = CurrentBrush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var action = new Button { Content = added ? "In this collection" : "Add", FontSize = 13, MinWidth = 0, MinHeight = 0, Padding = new(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"], VerticalAlignment = VerticalAlignment.Center, IsEnabled = !added && !ViewModel.IsManualMutationPending };
        action.Click += async (_, _) => await AddSearchedTitleAsync(item);
        Grid.SetColumn(action, 2); row.Children.Add(action);
        var border = new Border { Child = row, CornerRadius = new(10), Background = ViewModel.SearchResults.IndexOf(item) == _manualSearchHighlight ? CurrentBrush("SurfaceBrush") : null };
        AutomationProperties.SetName(border, item.Title + " · " + type); AutomationProperties.SetHelpText(border, added ? "In this collection" : "Add");
        border.Tapped += async (_, args) =>
        {
            for (var source = args.OriginalSource as DependencyObject; source != null && source != border; source = VisualTreeHelper.GetParent(source))
                if (source is Button) return;
            await AddSearchedTitleAsync(item);
        };
        return border;
    }
}
