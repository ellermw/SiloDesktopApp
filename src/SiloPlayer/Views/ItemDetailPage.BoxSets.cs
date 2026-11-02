using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
namespace SiloPlayer.Views;
public sealed partial class ItemDetailPage
{
    private void BoxSetLanguageChanged(long _) => DispatcherQueue.TryEnqueue(RefreshBoxSetRow);
    private async void RefreshBoxSetRow() => await ViewModel.RefreshBoxSetAsync();
    private void BoxSetViewAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoxSet is {} set) App.Services.GetRequiredService<NavigationService>().Navigate<BoxSetPage>(new BoxSetNavigationArgs(set.Id, set.LibraryId));
    }
    private void BuildBoxSetRow()
    {
        BoxSetMovies.Children.Clear(); BoxSetSection.Visibility = Visibility.Collapsed;
        if (ViewModel.BoxSet is not {} set || ViewModel.Item is not { Type: "movie" } item) return;
        foreach (var section in new FrameworkElement[] { CastSection, ExtrasSection, TrailersSection, BoxSetSection, SimilarSection, MediaLocationsSection })
        { DetailContentPanel.Children.Remove(section); DetailContentPanel.Children.Add(section); }
        BoxSetTitle.Text = set.Title; BoxSetSection.Visibility = Visibility.Visible;
        var current = 0; var index = 0;
        foreach (var movie in set.Items)
        {
            if (!SiloPlayer.Core.Models.Catalog.BoxSetDetail.IsMovieMember(movie)) continue;
            var panel = new Grid();
            var card = new PosterCard { MediaItem = movie };
            // The returned set's library owns every member action, even from an unscoped detail.
            card.Tag = new ItemDetailNavigationArgs(movie.ContentId, int.TryParse(set.LibraryId, out var library) ? library : null);
            panel.Children.Add(card);
            if (movie.ContentId == item.ContentId)
            {
                current = index;
                panel.Children.Add(new Border { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6), Padding = new Thickness(6, 3, 6, 3), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black), Child = new TextBlock { Text = "This film", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } });
                AutomationProperties.SetName(card, movie.Title + ", This film");
            }
            BoxSetMovies.Children.Add(panel); index++;
        }
        var target = current;
        BoxSetScroller.Loaded += Position;
        void Position(object sender, RoutedEventArgs e)
        {
            BoxSetScroller.Loaded -= Position;
            if (ReferenceEquals(ViewModel.BoxSet, set) && BoxSetMovies.Children.Count > target)
            { BoxSetMovies.UpdateLayout(); var child = (FrameworkElement)BoxSetMovies.Children[target]; BoxSetScroller.ChangeView(target * (child.ActualWidth + 16), null, null, true); }
        }
        if (BoxSetScroller.IsLoaded) Position(BoxSetScroller, new RoutedEventArgs());
    }
}
