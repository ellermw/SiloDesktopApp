using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class PersonDetailPage : Page
{
    public PersonDetailViewModel ViewModel { get; }
    private bool _bioExpanded;

    public PersonDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<PersonDetailViewModel>();
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
        var bioBox = new TextBox { Text = person.Bio ?? "", PlaceholderText = "Biography", AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MinHeight = 100, MaxHeight = 200, CornerRadius = new CornerRadius(6), FontSize = 13 };
        var birthDateBox = new TextBox { Text = person.BirthDate ?? "", PlaceholderText = "YYYY-MM-DD", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var deathDateBox = new TextBox { Text = person.DeathDate ?? "", PlaceholderText = "YYYY-MM-DD (leave blank if alive)", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var birthplaceBox = new TextBox { Text = person.Birthplace ?? "", PlaceholderText = "Birthplace", CornerRadius = new CornerRadius(6), FontSize = 13 };

        var form = new StackPanel { Width = 480, Spacing = 14 };
        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"] });
            group.Children.Add(control);
            form.Children.Add(group);
        }
        AddField("Name", nameBox);
        AddField("Biography", bioBox);
        AddField("Birth Date", birthDateBox);
        AddField("Death Date", deathDateBox);
        AddField("Birthplace", birthplaceBox);

        var dialog = new ContentDialog
        {
            Title = "Edit Person Metadata",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = new ScrollViewer { Content = form, MaxHeight = 500 },
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                var adminApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.AdminApi>();
                ViewModel.Person = await adminApi.UpdatePersonAsync(person.Id, new Dictionary<string, object?>
                {
                    ["name"] = nameBox.Text.Trim(),
                    ["bio"] = string.IsNullOrWhiteSpace(bioBox.Text) ? null : bioBox.Text.Trim(),
                    ["birth_date"] = string.IsNullOrWhiteSpace(birthDateBox.Text) ? null : birthDateBox.Text.Trim(),
                    ["death_date"] = string.IsNullOrWhiteSpace(deathDateBox.Text) ? null : deathDateBox.Text.Trim(),
                    ["birthplace"] = string.IsNullOrWhiteSpace(birthplaceBox.Text) ? null : birthplaceBox.Text.Trim(),
                });
                UpdateUI();
                App.Services.GetRequiredService<ToastService>().Success("Person metadata saved.");
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = $"Failed to update: {ex.Message}";
            }
        }
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
