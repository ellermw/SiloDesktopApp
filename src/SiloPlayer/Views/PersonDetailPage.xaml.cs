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
        ViewModel.Cancel();
        _uiCustomizationService.Changed -= UICustomization_Changed;
        base.OnNavigatedFrom(e);
    }

    private void UpdateUI()
    {
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

        // Refresh metadata is available to all signed-in users (web parity);
        // admins trigger an immediate refresh, non-admins queue one.
        RefreshButton.Visibility = Visibility.Visible;
        if (RefreshButton.Content is StackPanel refreshStack && refreshStack.Children.Count >= 2 &&
            refreshStack.Children[1] is TextBlock refreshLabel)
        {
            refreshLabel.Text = ViewModel.IsAdmin ? "Refresh now" : "Refresh metadata";
        }
        ToolTipService.SetToolTip(RefreshButton, ViewModel.IsAdmin ? "Refresh now" : "Refresh metadata");

        // Edit metadata button (admin only)
        EditMetadataButton.Visibility = ViewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        // Load photo
        if (!string.IsNullOrEmpty(person.PhotoUrl))
        {
            _ = LoadPersonPhotoAsync(person.PhotoUrl, person.Name);
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRing.IsActive = true;
        RefreshRing.Visibility = Visibility.Visible;
        RefreshButton.IsEnabled = false;

        await ViewModel.RefreshMetadataCommand.ExecuteAsync(null);
        UpdateUI();

        var toast = App.Services.GetRequiredService<ToastService>();
        if (!string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)) toast.Error(ViewModel.ErrorMessage);
        else if (!string.IsNullOrWhiteSpace(ViewModel.StatusMessage)) toast.Success(ViewModel.StatusMessage);

        RefreshRing.IsActive = false;
        RefreshRing.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
    }

    private async void EditMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        var person = ViewModel.Person;
        if (person == null) return;

        var nameBox = new TextBox { Text = person.Name ?? "", PlaceholderText = "Name", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var bioBox = new TextBox { Text = person.Bio ?? "", PlaceholderText = "Bio", AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MinHeight = 128, MaxHeight = 240, CornerRadius = new CornerRadius(6), FontSize = 13 };
        var birthDateBox = new TextBox { Text = person.BirthDate ?? "", PlaceholderText = "YYYY-MM-DD", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var deathDateBox = new TextBox { Text = person.DeathDate ?? "", PlaceholderText = "YYYY-MM-DD (leave blank if alive)", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var birthplaceBox = new TextBox { Text = person.Birthplace ?? "", PlaceholderText = "Birthplace", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var homepageBox = new TextBox { Text = person.Homepage ?? "", PlaceholderText = "https://…", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var tmdbIdBox = new TextBox { Text = person.TmdbId ?? "", PlaceholderText = "TMDB ID", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var imdbIdBox = new TextBox { Text = person.ImdbId ?? "", PlaceholderText = "IMDb ID", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var tvdbIdBox = new TextBox { Text = person.TvdbId ?? "", PlaceholderText = "TVDB ID", CornerRadius = new CornerRadius(6), FontSize = 13 };

        var availableWidth = ActualWidth > 0 ? ActualWidth - 112 : 600;
        var formWidth = Math.Clamp(availableWidth, 300, 600);
        var compactForm = formWidth < 520;
        var form = new Grid
        {
            Width = formWidth,
            ColumnSpacing = 16,
            RowSpacing = 14,
        };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (!compactForm)
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nextRow = 0;
        void AddField(string label, FrameworkElement control, int column, bool span = false)
        {
            while (form.RowDefinitions.Count <= nextRow)
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            group.Children.Add(control);
            Grid.SetRow(group, nextRow);
            Grid.SetColumn(group, compactForm ? 0 : column);
            if (!compactForm && span) Grid.SetColumnSpan(group, 2);
            form.Children.Add(group);
            if (compactForm || span || column == 1) nextRow++;
        }
        AddField("Name", nameBox, 0, span: true);
        AddField("Bio", bioBox, 0, span: true);
        AddField("Birth Date", birthDateBox, 0);
        AddField("Death Date", deathDateBox, 1);
        AddField("Birthplace", birthplaceBox, 0);
        AddField("Homepage", homepageBox, 1);
        AddField("TMDB ID", tmdbIdBox, 0);
        AddField("IMDb ID", imdbIdBox, 1);
        AddField("TVDB ID", tvdbIdBox, 0);

        var dialog = new ContentDialog
        {
            Title = "Edit Person Metadata",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = new ScrollViewer { Content = form, MaxHeight = 500 },
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                static bool IsValidOptionalDate(string value)
                    => string.IsNullOrWhiteSpace(value) ||
                       DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                           DateTimeStyles.None, out DateOnly _);
                if (!IsValidOptionalDate(birthDateBox.Text) || !IsValidOptionalDate(deathDateBox.Text))
                {
                    args.Cancel = true;
                    App.Services.GetRequiredService<ToastService>()
                        .Error("Dates must use YYYY-MM-DD.");
                    return;
                }

                var changes = new Dictionary<string, object?>();
                static void AddStringChange(Dictionary<string, object?> target, string key, string current, string? original)
                {
                    if (!string.Equals(current, original ?? "", StringComparison.Ordinal))
                        target[key] = current;
                }
                static void AddDateChange(Dictionary<string, object?> target, string key, string current, string? original)
                {
                    if (!string.Equals(current, original ?? "", StringComparison.Ordinal))
                        target[key] = string.IsNullOrEmpty(current) ? null : current;
                }

                AddStringChange(changes, "name", nameBox.Text, person.Name);
                AddStringChange(changes, "bio", bioBox.Text, person.Bio);
                AddDateChange(changes, "birth_date", birthDateBox.Text, person.BirthDate);
                AddDateChange(changes, "death_date", deathDateBox.Text, person.DeathDate);
                AddStringChange(changes, "birthplace", birthplaceBox.Text, person.Birthplace);
                AddStringChange(changes, "homepage", homepageBox.Text, person.Homepage);
                AddStringChange(changes, "tmdb_id", tmdbIdBox.Text, person.TmdbId);
                AddStringChange(changes, "imdb_id", imdbIdBox.Text, person.ImdbId);
                AddStringChange(changes, "tvdb_id", tvdbIdBox.Text, person.TvdbId);

                if (changes.Count == 0)
                    return;

                var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                ViewModel.Person = await adminApi.UpdatePersonAsync(person.Id, changes);
                UpdateUI();
                App.Services.GetRequiredService<ToastService>().Success("Person metadata saved.");
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                App.Services.GetRequiredService<ToastService>().Error($"Failed to update: {ex.Message}");
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
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
