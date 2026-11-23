using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
namespace SiloPlayer.Views;

/// <summary>
/// The "Also in" row of a movie or series page (silo-server #4528, ItemCollectionsRow). The collections come from the
/// view model's own cancellable read; this paints the answer. Several collections show a card for each under "Also in";
/// exactly one with titles to preview shows that collection's other titles under its name with "Explore all".
/// Nothing shows while the read is pending, when no collection holds the title, or when the first read fails.
/// </summary>
public sealed partial class ItemDetailPage
{
    private ItemCollections? _alsoInShown;
    private CancellationTokenSource? _alsoInPaint;

    private void ClearAlsoInRow()
    {
        _alsoInPaint?.Cancel(); _alsoInPaint = null; _alsoInShown = null;
        AlsoInPanel.Children.Clear();
        AlsoInExploreAll.Visibility = Visibility.Collapsed;
        AlsoInSection.Visibility = Visibility.Collapsed;
    }

    private void BuildAlsoInRow()
    {
        var answer = ViewModel.AlsoIn;
        if (answer == null || answer.Mode == ItemCollectionsRowMode.None || ViewModel.Item is not { Type: "movie" or "series" })
        { ClearAlsoInRow(); return; }
        if (ReferenceEquals(answer, _alsoInShown)) return;
        // A newer answer repaints the row in place: the old cards stay until the new ones replace them.
        _alsoInPaint?.Cancel();
        _alsoInShown = answer;
        _ = PaintAlsoInRowAsync(answer);
    }

    private async Task PaintAlsoInRowAsync(ItemCollections answer)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var identity = client.CaptureIdentityContext();
        var paint = _alsoInPaint = new CancellationTokenSource();
        // The view model keeps an answer only for the title it was read for and drops it on leaving, so the answer still
        // being its own covers both. The page being loaded and the item object are not conditions: a prefetched page gets
        // its answer before it is in the tree, and a reload of the same title replaces the item and keeps the answer.
        bool Current() => !paint.IsCancellationRequested && ReferenceEquals(ViewModel.AlsoIn, answer) && client.IsCurrentIdentityContext(identity);
        // A paint that gives up painted nothing, so its answer is not the one shown; a newer paint or a clear has already said so.
        void GiveUp() { if (ReferenceEquals(_alsoInPaint, paint)) _alsoInShown = null; }
        var me = App.Services.GetRequiredService<AuthService>().SelectedProfileId;
        Dictionary<string, string>? owners = null;
        // Another profile's shared collection carries its owner's name; the names come from the login's profile list.
        if (answer.Mode == ItemCollectionsRowMode.AlsoIn && answer.Items.Any(card => card.NeedsOwnerName(me)))
        {
            try
            {
                var profiles = await App.Services.GetRequiredService<AuthApi>().GetProfilesAsync(paint.Token);
                owners = profiles.Profiles.GroupBy(profile => profile.Id).ToDictionary(group => group.Key, group => group.First().Name);
            }
            catch (OperationCanceledException) { GiveUp(); return; }
            catch { /* An unreadable profile list leaves the neutral "Another profile" label. */ }
        }
        if (!Current()) { GiveUp(); return; }

        AlsoInPanel.Children.Clear();
        if (answer.Mode == ItemCollectionsRowMode.Preview)
        {
            var only = answer.Items[0];
            AlsoInTitle.Text = only.Title;
            AlsoInExploreAll.Tag = only;
            AlsoInExploreAll.Visibility = Visibility.Visible;
            AutomationProperties.SetName(AlsoInExploreAll, AppStrings.Get("web.catalog.carousel.exploreAllLabel",
                new Dictionary<string, object?> { ["title"] = only.Title }, "Explore all " + only.Title));
            foreach (var title in answer.PreviewItems)
            {
                var panel = new Grid();
                var card = new PosterCard { MediaItem = title };
                // The card opens the title in the collection's library, as the WebUI's card does.
                card.Tag = new ItemDetailNavigationArgs(title.ContentId, only.Library);
                panel.Children.Add(card);
                AlsoInPanel.Children.Add(panel);
            }
        }
        else
        {
            AlsoInTitle.Text = AppStrings.Get("web.detail.sections.alsoIn", fallback: "Also in");
            AlsoInExploreAll.Tag = null;
            AlsoInExploreAll.Visibility = Visibility.Collapsed;
            foreach (var collection in answer.Items)
                AlsoInPanel.Children.Add(new CollectionPosterTile(collection, collection.OwnerName(me, owners)) { Tag = collection });
        }
        AlsoInSection.Visibility = Visibility.Visible;
        UpdateResponsiveLayout(ActualWidth);
    }

    private void AlsoInExploreAll_Click(object sender, RoutedEventArgs e)
    {
        if (AlsoInExploreAll.Tag is ItemCollectionCard collection) CollectionPosterTile.Open(collection);
    }

    /// <summary>Sizes the collection cards like the "More Like This" cards beside them.</summary>
    private void ApplyAlsoInLayout(double cardWidth, double viewport)
    {
        AlsoInPanel.Spacing = viewport >= 1024 ? 20 : 16;
        foreach (var tile in AlsoInPanel.Children.OfType<CollectionPosterTile>()) tile.SetLayout(cardWidth);
    }
}
