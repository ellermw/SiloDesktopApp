using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class PersonDetailPage : Page
{
    public PersonDetailViewModel ViewModel { get; }
    private readonly UICustomizationService _uiCustomizationService;
    private bool _bioExpanded;
    private double _filmographyCardWidth = 178;
    private bool _isActive;
    private CancellationTokenSource? _photoCts;
    private string? _photoUrl;
    private string? _photoPersonId;

    public PersonDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<PersonDetailViewModel>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();

        FilmographyRepeater.ItemsSource = ViewModel.Filmography;

        ViewModel.Filmography.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateFilmographyState);
        };

        // Incremental filmography pagination — trigger LoadMore when the user
        // scrolls within 600px of the bottom. Matches webui IntersectionObserver
        // pattern, adapted for WinUI's ScrollViewer.
        ContentScroll.ViewChanged += ContentScroll_ViewChanged;

        // Sync the "Loading more…" indicator to IsLoadingMoreFilmography.
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PersonDetailViewModel.IsLoadingMoreFilmography))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isActive) return;
                    FilmographyLoadingMore.Visibility = ViewModel.IsLoadingMoreFilmography
                        ? Visibility.Visible : Visibility.Collapsed;
                });
            }
            else if (args.PropertyName == nameof(PersonDetailViewModel.Person))
            {
                DispatcherQueue.TryEnqueue(UpdateUI);
            }
        };
    }

    private void ContentScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        if (!ViewModel.FilmographyHasMore || ViewModel.IsLoadingMoreFilmography) return;

        var distanceFromBottom =
            ContentScroll.ExtentHeight - (ContentScroll.VerticalOffset + ContentScroll.ViewportHeight);
        if (distanceFromBottom < 600)
        {
            _ = ViewModel.LoadMoreFilmographyCommand.ExecuteAsync(null);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isActive = true;
        _photoUrl = null;
        _photoPersonId = null;
        PersonPhotoBorder.Child = InitialsText;
        _uiCustomizationService.Changed += UICustomization_Changed;

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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isActive = false;
        Interlocked.Exchange(ref _photoCts, null)?.Cancel();
        ViewModel.Cancel();
        _uiCustomizationService.Changed -= UICustomization_Changed;
        base.OnNavigatedFrom(e);
    }

    private void UpdateUI()
    {
        if (!_isActive) return;
        var person = ViewModel.Person;
        if (person == null) return;

        PersonName.Text = person.Name;
        if (App.MainWindowInstance is MainWindow mw)
            mw.SetDynamicTitle(person.Name);
        AgeText.Text = ViewModel.AgeDisplay;
        AgeBadge.Visibility = string.IsNullOrWhiteSpace(ViewModel.AgeDisplay)
            ? Visibility.Collapsed
            : Visibility.Visible;
        BirthDateText.Text = ViewModel.BirthDateDisplay;
        BirthDateBadge.Visibility = string.IsNullOrWhiteSpace(ViewModel.BirthDateDisplay)
            ? Visibility.Collapsed : Visibility.Visible;
        DeathDateText.Text = ViewModel.DeathDateDisplay;
        DeathDateBadge.Visibility = string.IsNullOrWhiteSpace(ViewModel.DeathDateDisplay)
            ? Visibility.Collapsed : Visibility.Visible;

        // Birthplace
        if (!string.IsNullOrEmpty(person.Birthplace))
        {
            BirthplaceText.Text = person.Birthplace;
            BirthplaceBadge.Visibility = Visibility.Visible;
        }
        else
        {
            BirthplaceBadge.Visibility = Visibility.Collapsed;
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

        // Load photo
        if (person.Id != _photoPersonId || person.PhotoUrl != _photoUrl)
        {
            Interlocked.Exchange(ref _photoCts, null)?.Cancel();
            if (person.Id != _photoPersonId || string.IsNullOrEmpty(person.PhotoUrl))
                PersonPhotoBorder.Child = InitialsText;
            _photoPersonId = person.Id;
            _photoUrl = person.PhotoUrl;
            if (!string.IsNullOrEmpty(person.PhotoUrl))
            {
                var cts = new CancellationTokenSource();
                _photoCts = cts;
                _ = LoadPersonPhotoAsync(person.Id, person.PhotoUrl, cts);
            }
        }

        UpdateFilmographyState();
        UpdateFilterTabStyles();
    }

    private void PersonDetailPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => ApplyResponsiveLayout(ActualWidth));

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var gutter = width >= 1024 ? 40d
            : width >= 640 ? 24d
            : 16d;
        PersonContentShell.Padding = new Thickness(gutter, width >= 640 ? 40 : 32, gutter, 48);
        PersonSkeletonShell.Padding = new Thickness(gutter, width >= 640 ? 40 : 32, gutter, 48);
        var photoWidth = width >= 640 ? 180d : 140d;
        PersonPhotoBorder.Width = photoWidth;
        PersonPhotoBorder.Height = photoWidth * 1.5;
        PersonName.FontSize = width >= 640 ? 30 : 24;

        var innerWidth = Math.Max(320, Math.Min(1400, width) - (gutter * 2));
        var columns = _uiCustomizationService.GetPosterColumnCount(innerWidth);
        _filmographyCardWidth = Math.Max(96, (innerWidth - ((columns - 1) * 12)) / columns);
        FilmographyGridLayout.MaximumRowsOrColumns = columns;
        FilmographyGridLayout.MinItemWidth = _filmographyCardWidth;
        FilmographyGridLayout.MinItemHeight = (_filmographyCardWidth * 1.5) + _uiCustomizationService.CardCaptionHeight;
        for (var index = 0; index < ViewModel.Filmography.Count; index++)
        {
            if (FilmographyRepeater.TryGetElement(index) is PosterCard card)
                card.SetCatalogGridLayout(_filmographyCardWidth);
        }
    }

    private void FilmographyRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is PosterCard card)
            card.SetCatalogGridLayout(_filmographyCardWidth);
    }

    private void UpdateFilmographyState()
    {
        if (!_isActive) return;
        int count = ViewModel.Filmography.Count;
        FilmographyEmptyText.Visibility = count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
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
        else
            nav.Navigate<HomePage>();
    }

    private async Task LoadPersonPhotoAsync(string personId, string photoUrl, CancellationTokenSource cts)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var apiClient = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
            var context = apiClient.CaptureContext();

            var bytes = await imageService.GetImageAsync(
                $"{context.BaseUrl}/{personId}", "person_photo", photoUrl, httpClient, cts.Token);

            if (bytes == null)
            {
                if (ReferenceEquals(_photoCts, cts)) _photoUrl = null; // Retry a failed photo on the next refresh.
                return;
            }
            if (!_isActive || !ReferenceEquals(_photoCts, cts) ||
                cts.IsCancellationRequested || !apiClient.IsCurrentContext(context)) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 200,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
            if (!_isActive || !ReferenceEquals(_photoCts, cts) ||
                cts.IsCancellationRequested || !apiClient.IsCurrentContext(context)) return;

            var image = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };

            PersonPhotoBorder.Child = image;
        }
        catch
        {
            if (ReferenceEquals(_photoCts, cts)) _photoUrl = null;
        }
        finally
        {
            Interlocked.CompareExchange(ref _photoCts, null, cts);
            cts.Dispose();
        }
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }
}
