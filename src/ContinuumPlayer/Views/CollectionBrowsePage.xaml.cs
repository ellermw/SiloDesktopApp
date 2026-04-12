using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.Views;

/// <summary>
/// Browse view for a collection (user-defined or library-discovered).
/// B40 + B41: before this, clicking a collection card opened the editor or
/// mutated LibraryPage in place. This page mirrors the webui
/// <c>/catalog?source=user_collection&amp;collection_id=X</c> route — a
/// standalone grid of items with a back button.
/// </summary>
public sealed partial class CollectionBrowsePage : Page
{
    private readonly CatalogApi _catalogApi;

    /// <summary>Parameter passed via NavigationService.Navigate.</summary>
    public sealed class NavArgs
    {
        public string CollectionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        /// <summary>true = user_collection, false = library_collection.</summary>
        public bool IsUserCollection { get; set; }
    }

    public CollectionBrowsePage()
    {
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not NavArgs args) return;

        TitleText.Text = args.Title;
        SubtitleText.Text = args.Subtitle ?? (args.IsUserCollection ? "User collection" : "Library collection");

        ShowLoading();
        try
        {
            var resp = args.IsUserCollection
                ? await _catalogApi.BrowseUserCollectionAsync(args.CollectionId)
                : await _catalogApi.BrowseLibraryCollectionAsync(args.CollectionId);

            var items = resp.Items ?? new List<MediaItem>();
            if (items.Count == 0)
            {
                ShowEmpty();
                return;
            }

            PosterRepeater.ItemsSource = items;
            ItemCountText.Text = items.Count.ToString();
            ItemCountLabel.Text = items.Count == 1 ? "item" : "items";
            CountPanel.Visibility = Visibility.Visible;
            ShowContent();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.Frame?.CanGoBack == true)
        {
            nav.Frame.GoBack();
        }
        else
        {
            nav.Navigate<CollectionsPage>();
        }
    }

    private void ShowLoading()
    {
        LoadingRing.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowContent()
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowEmpty()
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingRing.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Text = $"Could not load collection: {message}";
        ErrorText.Visibility = Visibility.Visible;
    }
}
