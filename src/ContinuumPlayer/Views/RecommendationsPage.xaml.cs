using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class RecommendationsPage : Page
{
    public RecommendationsViewModel ViewModel { get; }
    private bool _eventsAttached;

    public RecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsAttached)
        {
            _eventsAttached = true;
            ViewModel.Rows.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(BuildRows);
            };
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ViewModel.TasteProfile))
                {
                    DispatcherQueue.TryEnqueue(BuildTasteProfile);
                }
            };
        }

        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildTasteProfile();
        BuildRows();
    }

    private void BuildTasteProfile()
    {
        var profile = ViewModel.TasteProfile;
        if (profile == null)
        {
            TasteProfilePanel.Visibility = Visibility.Collapsed;
            return;
        }

        // Web parity: only show the panel if at least one of top_genres or
        // favorite_directors is populated.
        var hasGenres = profile.Genres != null && profile.Genres.Count > 0;
        var hasDirectors = profile.FavoriteDirectors != null && profile.FavoriteDirectors.Count > 0;

        if (!hasGenres && !hasDirectors)
        {
            TasteProfilePanel.Visibility = Visibility.Collapsed;
            return;
        }

        TasteProfilePanel.Visibility = Visibility.Visible;

        // Web uses signal_counts total; desktop model doesn't expose that so we
        // surface a simple item count instead.
        var totalSignals = (profile.Genres?.Count ?? 0) + (profile.FavoriteDirectors?.Count ?? 0)
            + (profile.FavoriteActors?.Count ?? 0) + (profile.Keywords?.Count ?? 0);
        TasteSignalCountText.Text = totalSignals > 0
            ? $"{totalSignals} signal{(totalSignals == 1 ? "" : "s")}"
            : "";

        // Top genres pills
        TopGenresList.Children.Clear();
        if (hasGenres)
        {
            TopGenresPanel.Visibility = Visibility.Visible;
            foreach (var genre in profile.Genres!)
            {
                TopGenresList.Children.Add(CreateGenrePill(genre.Name));
            }
        }
        else
        {
            TopGenresPanel.Visibility = Visibility.Collapsed;
        }

        // Favorite directors pills
        FavoriteDirectorsList.Children.Clear();
        if (hasDirectors)
        {
            FavoriteDirectorsPanel.Visibility = Visibility.Visible;
            foreach (var director in profile.FavoriteDirectors!)
            {
                FavoriteDirectorsList.Children.Add(CreateDirectorPill(director.Name));
            }
        }
        else
        {
            FavoriteDirectorsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private Border CreateGenrePill(string text)
    {
        return new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 2, 10, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"]
            }
        };
    }

    private Border CreateDirectorPill(string text)
    {
        return new Border
        {
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 2, 10, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"]
            }
        };
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();

        if (ViewModel.Rows.Count == 0)
        {
            UpdateEmptyState();
            return;
        }

        foreach (var row in ViewModel.Rows)
        {
            if (row.Items.Count == 0) continue;

            var section = new HomeSectionWithItems
            {
                Title = row.Label,
                Items = new List<MediaItem>(row.Items)
            };

            RowsPanel.Children.Add(new SectionRow { Section = section });
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool hasContent = ViewModel.Rows.Count > 0;
        EmptyState.Visibility = !hasContent && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
