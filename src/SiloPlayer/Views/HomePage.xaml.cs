using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Json;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }
    private bool _eventsAttached;
    // Tracks whether the section panel has been populated for the current
    // data. When the user navigates away and back, skip rebuilding so we
    // don't spike the UI thread tearing down and rebuilding ~60 PosterCards.
    private bool _contentBuilt;
    private bool _isRefreshingLayout;
    private bool _layoutChangedWhileRefreshing;
    private string? _failedHeroSectionId;
    private int _lastRenderedRevision = -1;
    private readonly EventChannelClient _eventChannel;
    private readonly AuthService _authService;
    private IDisposable? _realtimeSubscription;

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
        _authService = App.Services.GetRequiredService<AuthService>();
        this.InitializeComponent();
        // Reuse the mounted home surface instead of reconstructing its hero,
        // section rows, and cards on every top-level navigation.
        NavigationCacheMode = NavigationCacheMode.Required;
        SizeChanged += HomePage_SizeChanged;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        App.RecordUiDiagnostic($"Home loaded built={_contentBuilt} revision={ViewModel.RenderRevision} rows={ViewModel.Sections.Count}");
        try
        {
            AttachViewModelEvents();
            AttachRealtimeEvents();
            ViewModel.SetActive(true);

            _layoutChangedWhileRefreshing = false;
            _isRefreshingLayout = true;
            await ViewModel.LoadCommand.ExecuteAsync(null);
            _isRefreshingLayout = false;

            if (!_contentBuilt ||
                _layoutChangedWhileRefreshing ||
                _lastRenderedRevision != ViewModel.RenderRevision)
            {
                BuildContent();
                _contentBuilt = true;
                _lastRenderedRevision = ViewModel.RenderRevision;
            }

            await UpdateTasteSeedBannerAsync();
        }
        catch (Exception ex)
        {
            _isRefreshingLayout = false;
            LocalLog.AppendLine("home_error.txt", $"page_load | {ex.GetType().Name}: {ex.Message}");
            ViewModel.ErrorMessage = "Unable to load the homepage";
        }
    }

    private void AttachViewModelEvents()
    {
        if (_eventsAttached) return;

        _eventsAttached = true;
        ViewModel.FeaturedSections.CollectionChanged += OnSectionsChanged;
        ViewModel.Sections.CollectionChanged += OnSectionsChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void AttachRealtimeEvents()
    {
        if (_realtimeSubscription != null) return;

        // Current WebUI subscribes these channels globally. Home responds by
        // invalidating mounted section queries and re-fetching them in place;
        // do the same without requiring a navigation round trip.
        _eventChannel.EventReceived += OnRealtimeEvent;
        _realtimeSubscription = _eventChannel.Subscribe("catalog", "user_state");
    }

    private void DetachRealtimeEvents()
    {
        _eventChannel.EventReceived -= OnRealtimeEvent;
        _realtimeSubscription?.Dispose();
        _realtimeSubscription = null;
    }

    private void OnRealtimeEvent(string channel, string eventName, JsonElement data)
    {
        if (string.Equals(channel, "catalog", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.QueueRealtimeRefresh(
                HomeRealtimeRefreshGate.ClassifyCatalogEvent(eventName, data));
            return;
        }

        if (!string.Equals(channel, "user_state", StringComparison.OrdinalIgnoreCase))
            return;

        if (data.TryGetProperty("profile_id", out var profileElement)
            && profileElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(profileElement.GetString())
            && !string.IsNullOrWhiteSpace(_authService.SelectedProfileId)
            && !string.Equals(profileElement.GetString(), _authService.SelectedProfileId, StringComparison.Ordinal))
        {
            return;
        }

        ViewModel.QueueRealtimeRefresh($"user_state:{eventName}");
    }

    private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSectionRowWidths(e.NewSize.Width);

        var ratio = e.NewSize.Width >= 1024 ? 0.60 : 0.50;
        var heroHeight = Math.Clamp(e.NewSize.Height * ratio, 350, 700);
        InitialHeroSkeleton.Height = heroHeight;
        HeroLoadingSkeleton.Height = heroHeight;
        HeroErrorPanel.Height = heroHeight;

        var gutter = e.NewSize.Width >= 1280 ? 48d
            : e.NewSize.Width >= 1024 ? 40d
            : e.NewSize.Width >= 640 ? 24d
            : 16d;
        var sectionPadding = new Thickness(gutter, 0, gutter, 0);
        LoadingSectionOne.Padding = sectionPadding;
        LoadingSectionTwo.Padding = sectionPadding;
        LoadingSectionThree.Padding = sectionPadding;
        EmptyHomeState.Margin = new Thickness(gutter, 0, gutter, 40);
        TasteSeedBanner.Margin = new Thickness(gutter, 0, gutter, 40);

        var skeletonWidth = e.NewSize.Width < 640 ? 130d
            : e.NewSize.Width < 1024 ? 150d
            : 178d;
        ResizeSkeletonPosters(LoadingSectionOne, skeletonWidth);
        ResizeSkeletonPosters(LoadingSectionTwo, skeletonWidth);
        ResizeSkeletonPosters(LoadingSectionThree, skeletonWidth);

        var isCompact = e.NewSize.Width < 640;
        TasteSeedBanner.Padding = isCompact
            ? new Thickness(20, 16, 20, 16)
            : new Thickness(24, 16, 24, 16);
        Grid.SetRow(TasteSeedActions, isCompact ? 1 : 0);
        Grid.SetColumn(TasteSeedActions, isCompact ? 0 : 2);
        Grid.SetColumnSpan(TasteSeedActions, isCompact ? 3 : 1);
        TasteSeedActions.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        TasteSeedTitle.FontSize = isCompact ? 14 : 16;
        TasteSeedDescription.FontSize = isCompact ? 12 : 14;

        HeroErrorPanel.Padding = new Thickness(
            e.NewSize.Width >= 1024 ? 48 : e.NewSize.Width >= 640 ? 24 : 16);
    }

    private static void ResizeSkeletonPosters(Panel panel, double width)
    {
        foreach (var poster in panel.Children.OfType<SkeletonPoster>())
            poster.SetResponsiveWidth(width);
    }

    private void BuildContent()
    {
        // Web parity (Home.tsx renderHeroSlot): the hero banner renders the FIRST
        // featured section only, capped to that section's item_limit. It does NOT
        // merge items from multiple featured sections.
        RefreshHero();

        // Non-featured sections as rows
        SectionsPanel.Children.Clear();
        foreach (var section in ViewModel.Sections)
        {
            // F12: sections with empty Items are still loading (skeleton shown
            // by SectionRow). Include them so skeletons render.
            var row = CreateSectionRow(section);

            SectionsPanel.Children.Add(row);
        }

        // Empty state matches the current WebUI and links to the per-profile
        // Home Screen editor.
        EmptyHomeState.Visibility = ViewModel.HasConfiguredSections
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateUndoBanner()
    {
        if (ViewModel.ShowUndoBanner)
        {
            UndoBannerText.Text = ViewModel.UndoMessage;
            UndoBanner.Visibility = Visibility.Visible;
        }
        else
        {
            UndoBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UndoDismissalCommand.ExecuteAsync(null);
        // Rebuild content to restore the item
        BuildContent();
    }

    private async Task UpdateTasteSeedBannerAsync()
    {
        TasteSeedBanner.Visibility = Visibility.Collapsed;
        var auth = App.Services.GetRequiredService<AuthService>();
        var profileId = auth.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId)) return;

        var settingsService = App.Services.GetRequiredService<SettingsService>();
        var settings = settingsService.Load();
        if (!settings.TasteSeedDismissedProfileIds.Contains(profileId, StringComparer.Ordinal)
            || settings.TasteSeedBannerDismissedProfileIds.Contains(profileId, StringComparer.Ordinal))
            return;

        try
        {
            var favorites = await App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>()
                .GetFavoritesAsync();
            TasteSeedBanner.Visibility = favorites.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch
        {
            // The prompt is optional. A temporary favorites failure must not
            // interfere with loading the home surface.
        }
    }

    private void PersonalizeHome_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(TasteSeedPage), true);

    private void DismissTasteSeed_Click(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<AuthService>();
        if (string.IsNullOrWhiteSpace(auth.SelectedProfileId)) return;
        var service = App.Services.GetRequiredService<SettingsService>();
        var settings = service.Load();
        if (!settings.TasteSeedBannerDismissedProfileIds.Contains(auth.SelectedProfileId, StringComparer.Ordinal))
            settings.TasteSeedBannerDismissedProfileIds.Add(auth.SelectedProfileId);
        service.Save(settings);
        TasteSeedBanner.Visibility = Visibility.Collapsed;
    }

    private void CustomizeHome_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(SettingsPage), "HomeScreen");

    private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        App.RecordUiDiagnostic($"Home sections action={e.Action} oldIndex={e.OldStartingIndex} newIndex={e.NewStartingIndex} rows={ViewModel.Sections.Count} refreshing={_isRefreshingLayout}");
        if (_isRefreshingLayout)
        {
            // A layout refresh clears and repopulates both collections. Fold
            // that burst into one rebuild so rows never tear down one-by-one.
            _layoutChangedWhileRefreshing = true;
            return;
        }

        // Incremental updates only. Before: any CollectionChanged event fired
        // BuildContent() which cleared and recreated every SectionRow. During
        // a home load this rebuilt ~15 rows up to 15 times on the UI thread,
        // freezing the app for seconds. Now we patch just the affected row.
        DispatcherQueue.TryEnqueue(() =>
        {
            bool isFeaturedCollection = ReferenceEquals(sender, ViewModel.FeaturedSections);

            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    if (e.NewItems != null)
                    {
                        foreach (HomeSectionWithItems updated in e.NewItems)
                        {
                            if (isFeaturedCollection)
                                RefreshHero();
                            else
                                UpdateSectionRow(updated);
                        }
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (HomeSectionWithItems removed in e.OldItems)
                        {
                            if (isFeaturedCollection)
                                RefreshHero();
                            else
                                RemoveSectionRow(removed.Id);
                        }
                    }
                    break;

                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        int? insertIndex = e.NewStartingIndex >= 0 ? e.NewStartingIndex : null;
                        foreach (HomeSectionWithItems added in e.NewItems)
                        {
                            if (isFeaturedCollection)
                                RefreshHero();
                            else
                            {
                                AddSectionRow(added, insertIndex);
                                if (insertIndex.HasValue)
                                    insertIndex++;
                            }
                        }
                    }
                    break;

                default:
                    // Reset / Move: fall back to a full rebuild.
                    BuildContent();
                    _contentBuilt = true;
                    _lastRenderedRevision = ViewModel.RenderRevision;
                    return;
            }

            // Empty-state visibility may need to flip on add/remove.
            EmptyHomeState.Visibility = ViewModel.HasConfiguredSections
                ? Visibility.Collapsed
                : Visibility.Visible;
            _lastRenderedRevision = ViewModel.RenderRevision;
        });
    }

    private void RefreshHero()
    {
        HeroCarouselControl.Visibility = Visibility.Collapsed;
        HeroLoadingSkeleton.Visibility = Visibility.Collapsed;
        HeroErrorPanel.Visibility = Visibility.Collapsed;
        _failedHeroSectionId = null;

        var heroSection = ViewModel.FeaturedSections.FirstOrDefault();
        var hasRenderableHero = heroSection != null
            && (!heroSection.LoadCompleted || heroSection.LoadFailed || heroSection.Items.Count > 0);
        ContentPanel.Padding = hasRenderableHero
            ? new Thickness(0, 8, 0, 8)
            : new Thickness(0, 24, 0, 8);
        if (heroSection == null) return;

        if (heroSection.LoadFailed)
        {
            _failedHeroSectionId = heroSection.Id;
            HeroErrorTitle.Text = heroSection.Title;
            HeroErrorPanel.Visibility = Visibility.Visible;
        }
        else if (heroSection.Items.Count > 0)
        {
            var limit = heroSection.ItemLimit > 0 ? heroSection.ItemLimit : heroSection.Items.Count;
            HeroCarouselControl.ItemsSource = heroSection.Items.Take(limit).ToList();
            HeroCarouselControl.Visibility = Visibility.Visible;
        }
        else if (!heroSection.LoadCompleted)
        {
            HeroLoadingSkeleton.Visibility = Visibility.Visible;
        }
    }

    private void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
            App.RecordUiDiagnostic($"Home scrolled offset={ContentScrollViewer.VerticalOffset:F0} extent={ContentScrollViewer.ExtentHeight:F0} viewport={ContentScrollViewer.ViewportHeight:F0}");
    }

    private void RetryHero_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_failedHeroSectionId))
            _ = ViewModel.RetrySectionAsync(_failedHeroSectionId);
    }

    private void UpdateSectionRow(HomeSectionWithItems updated)
    {
        foreach (var child in SectionsPanel.Children)
        {
            if (child is SectionRow row && row.Section?.Id == updated.Id)
            {
                row.Section = updated;
                ConfigureSectionNavigation(row, updated);
                return;
            }
        }
        // Row didn't exist yet (section arrived after initial build) — append it.
        AddSectionRow(updated, IndexOfSection(updated.Id));
    }

    private SectionRow CreateSectionRow(HomeSectionWithItems section)
    {
        var row = new SectionRow
        {
            Section = section,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ApplySectionRowWidth(row, ActualWidth);
        row.OnRetry = failed => _ = ViewModel.RetrySectionAsync(failed.Id);
        ConfigureSectionNavigation(row, section);
        return row;
    }

    private void ConfigureSectionNavigation(SectionRow row, HomeSectionWithItems section)
    {
        var itemLimit = section.ItemLimit > 0 ? section.ItemLimit : section.Items.Count;
        var hasKnownOverflow = itemLimit > 0 && section.TotalCount > itemLimit;

        if (SectionRow.IsBrowseSupported(section.SectionType)
            && hasKnownOverflow)
        {
            row.OnViewAll = () =>
            {
                var nav = App.Services.GetRequiredService<NavigationService>();
                nav.Navigate<CatalogPage>(new CatalogNavigation(
                    Source: "section",
                    Title: section.Title,
                    Scope: "home",
                    SectionId: section.Id));
            };
        }
        else
        {
            row.OnViewAll = null;
        }
    }

    private void AddSectionRow(HomeSectionWithItems section, int? preferredIndex = null)
    {
        var row = CreateSectionRow(section);
        var index = preferredIndex is >= 0
            ? Math.Min(preferredIndex.Value, SectionsPanel.Children.Count)
            : SectionsPanel.Children.Count;
        SectionsPanel.Children.Insert(index, row);
    }

    private void UpdateSectionRowWidths(double width)
    {
        foreach (var child in SectionsPanel.Children)
            if (child is SectionRow row)
                ApplySectionRowWidth(row, width);
    }

    private static void ApplySectionRowWidth(SectionRow row, double width)
    {
        if (double.IsNaN(width) || width <= 0)
            return;

        // SectionRow contains an internal horizontal carousel that can measure
        // wider than the viewport. In a vertical StackPanel that lets the row
        // become as wide as its cards. Pin the row to the visible page width;
        // the cards still overflow inside their own ScrollViewer.
        row.Width = width;
    }

    private void RemoveSectionRow(string sectionId)
    {
        for (int i = 0; i < SectionsPanel.Children.Count; i++)
        {
            if (SectionsPanel.Children[i] is SectionRow row && row.Section?.Id == sectionId)
            {
                SectionsPanel.Children.RemoveAt(i);
                return;
            }
        }
    }

    private int? IndexOfSection(string sectionId)
    {
        for (var i = 0; i < ViewModel.Sections.Count; i++)
        {
            if (ViewModel.Sections[i].Id == sectionId)
                return i;
        }
        return null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ShowUndoBanner))
        {
            DispatcherQueue.TryEnqueue(UpdateUndoBanner);
        }
        else if (e.PropertyName == nameof(ViewModel.HasConfiguredSections))
        {
            DispatcherQueue.TryEnqueue(() =>
                EmptyHomeState.Visibility = ViewModel.HasConfiguredSections
                    ? Visibility.Collapsed
                    : Visibility.Visible);
        }
        else if (e.PropertyName == nameof(ViewModel.RenderRevision))
        {
            var revision = ViewModel.RenderRevision;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isRefreshingLayout) return;

                var heroSection = ViewModel.FeaturedSections.FirstOrDefault();
                var itemLimit = heroSection?.ItemLimit ?? 0;
                if (!HomeSectionReconciler.IsHeroSnapshotCurrent(
                        HeroCarouselControl.ItemsSource,
                        heroSection?.Items,
                        itemLimit))
                {
                    RefreshHero();
                }

                _lastRenderedRevision = Math.Max(_lastRenderedRevision, revision);
            });
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        ViewModel.SetActive(false);
        DetachRealtimeEvents();

        if (_eventsAttached)
        {
            ViewModel.FeaturedSections.CollectionChanged -= OnSectionsChanged;
            ViewModel.Sections.CollectionChanged -= OnSectionsChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _eventsAttached = false;
        }
    }
}
