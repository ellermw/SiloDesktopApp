using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class RecommendationsPage : Page
{
    public RecommendationsViewModel ViewModel { get; }
    private bool _eventsAttached;
    private bool _rowsBuildQueued;

    public RecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationsViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsAttached)
        {
            _eventsAttached = true;
            ViewModel.Rows.CollectionChanged += (_, _) =>
            {
                // Rows are replaced as a batch while IsLoading is true. The
                // IsLoading transition below queues one rebuild after the batch
                // instead of reconstructing every carousel twice.
                if (!ViewModel.IsLoading)
                    QueueRowsBuild();
            };
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ViewModel.TasteProfile))
                {
                    DispatcherQueue.TryEnqueue(BuildTasteProfile);
                }
                else if (args.PropertyName is nameof(ViewModel.IsLoading) or nameof(ViewModel.ErrorMessage))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!ViewModel.IsLoading)
                            QueueRowsBuild();
                        UpdatePageState();
                    });
                }
            };
        }

        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildTasteProfile();
        QueueRowsBuild();
    }

    private void QueueRowsBuild()
    {
        if (_rowsBuildQueued) return;
        _rowsBuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rowsBuildQueued = false;
            BuildRows();
        });
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
        var hasGenres = profile.TopGenres.Count > 0;
        var hasDirectors = profile.FavoriteDirectors.Count > 0;

        if (!hasGenres && !hasDirectors)
        {
            TasteProfilePanel.Visibility = Visibility.Collapsed;
            return;
        }

        TasteProfilePanel.Visibility = Visibility.Visible;

        var totalSignals = profile.SignalCounts.Values.Sum();
        TasteSignalCountText.Text = totalSignals > 0
            ? $"{totalSignals} signal{(totalSignals == 1 ? "" : "s")}"
            : "";

        // Top genres pills
        TopGenresList.Children.Clear();
        if (hasGenres)
        {
            TopGenresPanel.Visibility = Visibility.Visible;
            foreach (var genre in profile.TopGenres)
            {
                TopGenresList.Children.Add(CreateGenrePill(genre));
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
            foreach (var director in profile.FavoriteDirectors)
            {
                FavoriteDirectorsList.Children.Add(CreateDirectorPill(director));
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
                FontSize = 12,
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
                FontSize = 12,
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
                SectionType = row.Type,
                Items = new ObservableCollection<MediaItem>(row.Items)
            };

            var sectionRow = new SectionRow();
            if (!string.IsNullOrWhiteSpace(row.SectionKind))
            {
                sectionRow.UseTitleViewLink = true;
                sectionRow.OnViewAll = () =>
                {
                    var nav = App.Services.GetRequiredService<NavigationService>();
                    nav.Navigate<RecommendationSectionPage>(
                        new RecommendationSectionNavigationArgs(row.SectionKind, row.SectionKey, row.Label));
                };
            }

            sectionRow.Section = section;
            RowsPanel.Children.Add(sectionRow);
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        UpdatePageState();
    }

    private void UpdatePageState()
    {
        var loaded = !ViewModel.IsLoading;
        var hasError = loaded && !string.IsNullOrWhiteSpace(ViewModel.ErrorMessage);
        var hasContent = loaded && !hasError && ViewModel.Rows.Any(row => row.Items.Count > 0);

        ErrorState.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        RowsPanel.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = loaded && !hasError && !hasContent
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var pageWidth = ActualWidth > 0 ? ActualWidth : width;
        var gutter = pageWidth < 640 ? 16d
            : pageWidth < 1024 ? 24d
            : pageWidth < 1280 ? 40d
            : 48d;

        HeaderGrid.Margin = new Thickness(gutter, 24, gutter, 16);
        LoadingState.Padding = new Thickness(gutter, 2, gutter, 24);
        HeaderTitleText.FontSize = pageWidth < 640 ? 24 : 30;

        var stackHeader = pageWidth < 640;
        Grid.SetRow(TasteProfileLoadingPanel, stackHeader ? 1 : 0);
        Grid.SetColumn(TasteProfileLoadingPanel, stackHeader ? 0 : 1);
        Grid.SetRow(TasteProfilePanel, stackHeader ? 1 : 0);
        Grid.SetColumn(TasteProfilePanel, stackHeader ? 0 : 1);

        var profileMargin = stackHeader ? new Thickness(0, 24, 0, 0) : new Thickness(24, 0, 0, 0);
        TasteProfileLoadingPanel.Margin = profileMargin;
        TasteProfilePanel.Margin = profileMargin;
        TasteProfileLoadingPanel.HorizontalAlignment = stackHeader
            ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        TasteProfilePanel.HorizontalAlignment = stackHeader
            ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        TasteProfilePanel.MaxWidth = stackHeader ? Math.Max(260, pageWidth - gutter * 2) : 360;
    }
}
