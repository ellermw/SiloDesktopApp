using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
namespace SiloPlayer.Views;
public sealed partial class ItemDetailPage
{
    private string? _boxSetRowItem, _boxSetRowId, _boxSetRowLibrary;
    private void BoxSetLanguageChanged(long _) => RefreshBoxSetRow();
    private void RefreshBoxSetRow()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(RefreshBoxSetRow); return; }
        _ = ViewModel.RefreshBoxSetAsync();
    }
    private void BoxSetViewAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoxSet is {} set) App.Services.GetRequiredService<NavigationService>().Navigate<BoxSetPage>(new BoxSetNavigationArgs(set.Id, set.LibraryId));
    }
    private void BuildBoxSetRow()
    {
        if (ViewModel.BoxSet is not {} set || ViewModel.Item is not { Type: "movie" } item)
        {
            BoxSetMovies.Children.Clear(); BoxSetSection.Visibility = Visibility.Collapsed;
            _boxSetRowItem = _boxSetRowId = _boxSetRowLibrary = null;
            return;
        }
        var members = set.Items.Where(SiloPlayer.Core.Models.Catalog.BoxSetDetail.IsMovieMember).ToArray();
        var cards = BoxSetMovies.Children.OfType<Grid>().Select(panel => panel.Children.OfType<PosterCard>().Single()).ToArray();
        BoxSetTitle.Text = set.Title; BoxSetSection.Visibility = Visibility.Visible;
        if (_boxSetRowItem == item.ContentId && _boxSetRowId == set.Id && _boxSetRowLibrary == set.LibraryId
            && cards.Select(card => card.MediaItem?.ContentId).SequenceEqual(members.Select(movie => movie.ContentId)))
        {
            foreach (var (card, movie) in cards.Zip(members))
            {
                if (!ReferenceEquals(card.MediaItem, movie)) card.RefreshItem(movie);
                if (movie.ContentId == item.ContentId) AutomationProperties.SetName(card, movie.Title + ", This film");
            }
            return;
        }
        BoxSetMovies.Children.Clear();
        _boxSetRowItem = item.ContentId; _boxSetRowId = set.Id; _boxSetRowLibrary = set.LibraryId;
        var current = 0; var index = 0;
        foreach (var movie in set.Items)
        {
            if (!SiloPlayer.Core.Models.Catalog.BoxSetDetail.IsMovieMember(movie)) continue;
            var panel = new Grid();
            var card = new PosterCard { MediaItem = movie };
            // The scoped membership response owns every member action.
            card.Tag = new ItemDetailNavigationArgs(movie.ContentId, int.TryParse(set.LibraryId, out var library) ? library : null);
            panel.Children.Add(card);
            if (movie.ContentId == item.ContentId)
            {
                current = index;
                panel.Children.Add(new Border { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6), Padding = new Thickness(6, 3, 6, 3), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black), Child = SiloPlayer.Controls.MediaItemCardActions.Localize(new TextBlock { Text = "This film", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, text: "web.detail.boxSet.thisFilm") });
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
