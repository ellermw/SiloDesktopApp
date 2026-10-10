using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Views.Dialogs;
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
    private string? _requestedPersonId;
    private string? _photoUrl;
    private string? _photoPersonId;

    public PersonDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<PersonDetailViewModel>();
        ViewModel.FormatPersonDate = date => DateTimeDisplay.FormatDate(new DateTimeOffset(date), medium: true);
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
            else if (args.PropertyName is nameof(PersonDetailViewModel.Person) or nameof(PersonDetailViewModel.ErrorMessage) or nameof(PersonDetailViewModel.IsLoading) or nameof(PersonDetailViewModel.IsRefreshing) or nameof(PersonDetailViewModel.IsLoadingFilmography))
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
        ViewModel.ActingAdmin = AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>());
        _photoUrl = null;
        _photoPersonId = null;
        PersonPhotoBorder.Child = InitialsText;
        _uiCustomizationService.Changed += UICustomization_Changed;

        // B33: PersonDetailPage now accepts a string ID; supports non-numeric
        // person IDs from third-party providers (matches WebUI cast/crew shape).
        if (e.Parameter is string personId && !string.IsNullOrEmpty(personId))
        {
            _requestedPersonId = personId;
            await ViewModel.LoadCommand.ExecuteAsync(personId);
            UpdateUI();
        }
        else if (e.Parameter is int legacyId && legacyId > 0)
        {
            // Back-compat for any caller still passing int
            _requestedPersonId = legacyId.ToString(CultureInfo.InvariantCulture);
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
        ContentScroll.Visibility = !ViewModel.IsLoading && person != null ? Visibility.Visible : Visibility.Collapsed;
        PersonUnavailableState.Visibility = !ViewModel.IsLoading && person == null && ViewModel.ErrorMessage != null ? Visibility.Visible : Visibility.Collapsed;
        var missing = ViewModel.ErrorMessage == "Person not found.";
        PersonUnavailableTitle.Text = missing ? "This person isn't available" : "Couldn't load this person";
        PersonUnavailableDescription.Text = missing ? "They may have been removed from the catalog, or the link may be wrong." : "Something went wrong while loading them. Try again in a moment.";
        PersonRetryButton.Visibility = missing ? Visibility.Collapsed : Visibility.Visible;
        PersonRetryButton.IsEnabled = !ViewModel.IsLoading;
        if (person == null) return;

        var isAdmin = AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>());
        PersonActions.Visibility = Visibility.Visible;
        EditPersonButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        RefreshPersonButton.IsEnabled = !ViewModel.IsRefreshing;
        RefreshPersonLabel.Text = ViewModel.IsRefreshing ? isAdmin ? "Refreshing..." : "Queueing..." : isAdmin ? "Refresh now" : "Refresh metadata";
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

        UpdateMetadataBadges();

        // Bio
        if (!string.IsNullOrEmpty(person.Bio))
        {
            // CSS white-space:normal collapses ASCII whitespace in the WebUI
            // biography. Preserve nonbreaking spaces and all meaningful text.
            BioText.Text = System.Text.RegularExpressions.Regex.Replace(person.Bio, "[ \\t\\r\\n\\f]+", " ").Trim();
            // Show "Show more" if bio is long enough to be truncated
            ShowMoreBioButton.Visibility = Visibility.Collapsed;
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

    private async void RetryPerson_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_requestedPersonId)) await ViewModel.LoadCommand.ExecuteAsync(_requestedPersonId);
    }

    private async void RefreshPerson_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Person == null) return;
        var personId = ViewModel.Person.Id;
        var isAdmin = AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>());
        RefreshPersonButton.IsEnabled = false;
        RefreshPersonLabel.Text = isAdmin ? "Refreshing..." : "Queueing...";
        try
        {
            await ViewModel.RefreshAsync(isAdmin);
            if (_isActive && ViewModel.Person?.Id == personId)
                App.Services.GetRequiredService<ToastService>().Success(isAdmin ? "Person metadata refreshed" : "Person refresh queued");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        finally { RefreshPersonButton.IsEnabled = true; UpdateUI(); }
    }

    private async void EditPerson_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Person == null || !AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<AuthService>())) return;
        var personId = ViewModel.Person.Id;
        var dialog = new EditPersonDialog(ViewModel.Person) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        if (dialog.HasSaved && _isActive && ViewModel.Person?.Id == personId) await ViewModel.LoadCommand.ExecuteAsync(personId);
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
        var viewport = WebUiViewport.Width(this, width);
        var outerGutter = viewport >= 1280 ? 48d : viewport >= 1024 ? 40d : viewport >= 640 ? 24d : 16d;
        var outerTop = viewport >= 1024 ? 32d : 16d;
        PersonViewportShell.Padding = new Thickness(outerGutter, outerTop, outerGutter, outerTop);
        var gutter = viewport >= 1024 ? 40d
            : viewport >= 640 ? 24d
            : 16d;
        PersonContentShell.Padding = new Thickness(gutter, viewport >= 640 ? 88 : 72, gutter, viewport >= 640 ? 32 : 24);
        BackButton.Margin = new Thickness(8, viewport >= 640 ? 24 : 16, 0, 0);
        PersonSkeletonShell.MaxWidth = 1400 + 2 * outerGutter;
        PersonSkeletonShell.Padding = new Thickness(gutter + outerGutter, outerTop + (viewport >= 640 ? 40 : 32), gutter + outerGutter, 48);
        PersonHeader.ColumnSpacing = viewport >= 1024 ? 32 : 24;
        PersonDivider.Margin = new Thickness(-gutter, viewport >= 640 ? 32 : 24, -gutter, viewport >= 640 ? 32 : 24);
        var photoWidth = viewport >= 640 ? 180d : 140d;
        PersonPhotoBorder.Width = photoWidth;
        PersonPhotoBorder.Height = photoWidth * 1.5;
        PersonName.FontSize = viewport >= 640 ? 30 : 24;
        PersonName.LineHeight = viewport >= 640 ? 36 : 32;
        var stacked = viewport < 1024;
        PersonHeader.RowSpacing = stacked ? 24 : 0;
        PersonDivider.Opacity = .1;
        PersonHeader.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        PersonHeader.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(PersonInfo, stacked ? 0 : 1);
        Grid.SetRow(PersonInfo, stacked ? 1 : 0);
        PersonSkeletonHeader.ColumnSpacing = PersonHeader.ColumnSpacing;
        PersonSkeletonHeader.RowSpacing = PersonHeader.RowSpacing;
        PersonSkeletonHeader.ColumnDefinitions[0].Width = PersonHeader.ColumnDefinitions[0].Width;
        PersonSkeletonHeader.ColumnDefinitions[1].Width = PersonHeader.ColumnDefinitions[1].Width;
        PersonSkeletonPhoto.Width = photoWidth;
        PersonSkeletonPhoto.Height = photoWidth * 1.5;
        Grid.SetColumn(PersonSkeletonInfo, stacked ? 0 : 1);
        Grid.SetRow(PersonSkeletonInfo, stacked ? 1 : 0);
        PersonPhotoBorder.HorizontalAlignment = HorizontalAlignment.Left;

        var innerWidth = Math.Max(96, Math.Min(1400, width - outerGutter * 2) - (gutter * 2));
        var columns = _uiCustomizationService.GetPosterColumnCount(innerWidth);
        var gap = _uiCustomizationService.CardPresentation.PosterSize == "large" ? 16d : 12d;
        FilmographyGridLayout.MinColumnSpacing = gap;
        FilmographyGridLayout.MinRowSpacing = gap;
        _filmographyCardWidth = Math.Max(96, (innerWidth - ((columns - 1) * gap)) / columns);
        FilmographySkeletonGrid.HorizontalSpacing = gap;
        FilmographySkeletonGrid.VerticalSpacing = gap;
        foreach (var placeholder in FilmographySkeletonGrid.Children.OfType<StackPanel>())
        {
            placeholder.Width = _filmographyCardWidth;
            ((SkeletonBox)placeholder.Children[0]).Height = _filmographyCardWidth * 1.5;
            ((SkeletonBox)placeholder.Children[1]).Width = _filmographyCardWidth * .75;
        }
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
        FilmographyEmptyText.Visibility = count == 0 && !ViewModel.IsLoading && !ViewModel.IsLoadingFilmography && ViewModel.ErrorMessage == null
            ? Visibility.Visible : Visibility.Collapsed;
        FilmographySkeletonGrid.Visibility = ViewModel.IsLoadingFilmography ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.IsLoadingFilmography && FilmographySkeletonGrid.Children.Count == 0)
        {
            for (var i = 0; i < 24; i++)
            {
                var placeholder = new StackPanel { Width = _filmographyCardWidth, Spacing = 8 };
                placeholder.Children.Add(new SkeletonBox { Height = _filmographyCardWidth * 1.5, CornerRadius = new CornerRadius(16) });
                placeholder.Children.Add(new SkeletonBox { Width = _filmographyCardWidth * .75, Height = 16, HorizontalAlignment = HorizontalAlignment.Left });
                FilmographySkeletonGrid.Children.Add(placeholder);
            }
        }
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

        var foreground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color;
        var border = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color;
        foreach (var button in new[] { FilterAllButton, FilterMoviesButton, FilterSeriesButton })
        {
            var selected = button.Tag?.ToString() == filter;
            button.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(selected ? (byte)51 : (byte)26, border.R, border.G, border.B));
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(selected
                ? Microsoft.UI.ColorHelper.FromArgb(26, foreground.R, foreground.G, foreground.B)
                : Microsoft.UI.Colors.Transparent);
        }
    }

    private void UpdateMetadataBadges()
    {
        // Person facts use the same theme-derived .metadata-badge as media facts.
        var foreground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color;
        var muted = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]).Color;
        var border = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color;
        var fillBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(20, foreground.R, foreground.G, foreground.B));
        var borderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(140, border.R, border.G, border.B));
        static byte Mix(byte first, byte second) => (byte)Math.Round(first * .72 + second * .28);
        var textBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255,
            Mix(foreground.R, muted.R), Mix(foreground.G, muted.G), Mix(foreground.B, muted.B)));
        foreach (var badge in new[] { BirthDateBadge, AgeBadge, DeathDateBadge, BirthplaceBadge })
        {
            badge.Background = fillBrush;
            badge.BorderBrush = borderBrush;
            var text = (TextBlock)badge.Child;
            text.Text = text.Text.ToUpperInvariant();
            text.Foreground = textBrush;
        }
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
