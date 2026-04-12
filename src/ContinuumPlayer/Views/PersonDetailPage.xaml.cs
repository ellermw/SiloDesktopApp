using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class PersonDetailPage : Page
{
    public PersonDetailViewModel ViewModel { get; }
    private bool _bioExpanded;

    public PersonDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<PersonDetailViewModel>();
        this.InitializeComponent();
        SmoothScrollHelper.Attach(ContentScroll);

        FilmographyRepeater.ItemsSource = ViewModel.Filmography;

        ViewModel.Filmography.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateFilmographyState);
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // B33: PersonDetailPage now accepts a string ID; supports non-numeric
        // person IDs from third-party providers (matches WebUI cast/crew shape).
        if (e.Parameter is string personId && !string.IsNullOrEmpty(personId))
        {
            await ViewModel.LoadCommand.ExecuteAsync(personId);
            UpdateUI();
        }
        else if (e.Parameter is int legacyId && legacyId > 0)
        {
            // Back-compat for any caller still passing int
            await ViewModel.LoadCommand.ExecuteAsync(legacyId.ToString());
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        var person = ViewModel.Person;
        if (person == null) return;

        PersonName.Text = person.Name;
        AgeText.Text = ViewModel.AgeDisplay;
        DatesText.Text = ViewModel.DatesDisplay;

        // Birthplace
        if (!string.IsNullOrEmpty(person.Birthplace))
        {
            BirthplaceText.Text = person.Birthplace;
            BirthplaceText.Visibility = Visibility.Visible;
        }
        else
        {
            BirthplaceText.Visibility = Visibility.Collapsed;
        }

        // Bio
        if (!string.IsNullOrEmpty(person.Bio))
        {
            BioText.Text = person.Bio;
            // Show "Show more" if bio is long enough to be truncated
            ShowMoreBioButton.Visibility = person.Bio.Length > 300
                ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            BioText.Text = "";
            ShowMoreBioButton.Visibility = Visibility.Collapsed;
        }

        // Initials fallback for photo
        InitialsText.Text = GetInitials(person.Name);

        // Refresh metadata is available to all signed-in users (web parity);
        // admins trigger an immediate refresh, non-admins queue one.
        RefreshButton.Visibility = Visibility.Visible;
        if (RefreshButton.Content is StackPanel refreshStack && refreshStack.Children.Count >= 2 &&
            refreshStack.Children[1] is TextBlock refreshLabel)
        {
            refreshLabel.Text = ViewModel.IsAdmin ? "Refresh now" : "Refresh metadata";
        }
        ToolTipService.SetToolTip(RefreshButton, ViewModel.IsAdmin ? "Refresh now" : "Refresh metadata");

        // Load photo
        if (!string.IsNullOrEmpty(person.PhotoUrl))
        {
            _ = LoadPersonPhotoAsync(person.PhotoUrl, person.Name);
        }

        UpdateFilmographyState();
        UpdateFilterTabStyles();
    }

    private void UpdateFilmographyState()
    {
        int count = ViewModel.Filmography.Count;
        FilmographyEmptyText.Visibility = count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
        FilmographyCountText.Text = ViewModel.FilmographyTotal > 0
            ? $"{ViewModel.FilmographyTotal} {(ViewModel.FilmographyTotal == 1 ? "title" : "titles")}"
            : "";
    }

    private void UpdateFilterTabStyles()
    {
        var filter = ViewModel.SelectedTypeFilter;
        FilterAllButton.Style = filter == "all"
            ? (Style)Resources["FilterTabActiveStyle"]
            : (Style)Resources["FilterTabStyle"];
        FilterMoviesButton.Style = filter == "movie"
            ? (Style)Resources["FilterTabActiveStyle"]
            : (Style)Resources["FilterTabStyle"];
        FilterSeriesButton.Style = filter == "series"
            ? (Style)Resources["FilterTabActiveStyle"]
            : (Style)Resources["FilterTabStyle"];
    }

    private async void FilterTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string type) return;
        await ViewModel.FilterCommand.ExecuteAsync(type);
        UpdateFilterTabStyles();
        UpdateFilmographyState();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRing.IsActive = true;
        RefreshRing.Visibility = Visibility.Visible;
        RefreshButton.IsEnabled = false;

        await ViewModel.RefreshMetadataCommand.ExecuteAsync(null);
        UpdateUI();

        RefreshRing.IsActive = false;
        RefreshRing.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
    }

    private void ShowMoreBio_Click(object sender, RoutedEventArgs e)
    {
        _bioExpanded = !_bioExpanded;
        BioText.MaxLines = _bioExpanded ? 0 : 8;
        ShowMoreBioText.Text = _bioExpanded ? "Show less" : "Show more";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    private async Task LoadPersonPhotoAsync(string photoUrl, string name)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                name, "person_photo", photoUrl, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 200,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            var image = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };

            PersonPhotoBorder.Child = image;
        }
        catch { }
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }
}
