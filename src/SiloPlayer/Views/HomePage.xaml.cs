using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
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
    private string? _failedHeroSectionId;

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Sections.Count == 0 && ViewModel.FeaturedSections.Count == 0)
            {
                await ViewModel.LoadCommand.ExecuteAsync(null);
                _contentBuilt = false; // fresh data → rebuild
            }

            if (!_contentBuilt)
            {
                BuildContent();
                _contentBuilt = true;
            }

            if (!_eventsAttached)
            {
                _eventsAttached = true;
                ViewModel.FeaturedSections.CollectionChanged += OnSectionsChanged;
                ViewModel.Sections.CollectionChanged += OnSectionsChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            await UpdateTasteSeedBannerAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
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
            var row = new SectionRow { Section = section };
            row.OnRetry = failed => _ = ViewModel.RetrySectionAsync(failed.Id);

            // Browse the exact stored home recipe. Opening an inferred library
            // would silently lose custom filters and section ordering.
            if (SectionRow.IsBrowseSupported(section.SectionType)
                && section.TotalCount > section.ItemLimit)
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

            SectionsPanel.Children.Add(row);
        }

        // Empty state matches the current WebUI and links to the per-profile
        // Home Screen editor.
        bool hasSections = ViewModel.FeaturedSections.Count > 0 || ViewModel.Sections.Count > 0;
        EmptyHomeState.Visibility = hasSections ? Visibility.Collapsed : Visibility.Visible;
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
        => Frame.Navigate(typeof(SettingsPage), "Home");

    private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
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
                        foreach (HomeSectionWithItems added in e.NewItems)
                        {
                            if (isFeaturedCollection)
                                RefreshHero();
                            else
                                AddSectionRow(added);
                        }
                    }
                    break;

                default:
                    // Reset / Move: fall back to a full rebuild.
                    BuildContent();
                    _contentBuilt = true;
                    return;
            }

            // Empty-state visibility may need to flip on add/remove.
            bool hasSections = ViewModel.FeaturedSections.Count > 0 || ViewModel.Sections.Count > 0;
            EmptyHomeState.Visibility = hasSections ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private void RefreshHero()
    {
        HeroCarouselControl.Visibility = Visibility.Collapsed;
        HeroLoadingSkeleton.Visibility = Visibility.Collapsed;
        HeroErrorPanel.Visibility = Visibility.Collapsed;
        _failedHeroSectionId = null;

        var heroSection = ViewModel.FeaturedSections.FirstOrDefault();
        ContentPanel.Padding = heroSection == null
            ? new Thickness(0, 24, 0, 8)
            : new Thickness(0, 8, 0, 8);
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
        else
        {
            HeroLoadingSkeleton.Visibility = Visibility.Visible;
        }
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
                return;
            }
        }
        // Row didn't exist yet (section arrived after initial build) — append it.
        AddSectionRow(updated);
    }

    private void AddSectionRow(HomeSectionWithItems section)
    {
        var row = new SectionRow { Section = section };
        row.OnRetry = failed => _ = ViewModel.RetrySectionAsync(failed.Id);
        if (SectionRow.IsBrowseSupported(section.SectionType)
            && section.TotalCount > section.ItemLimit)
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
        SectionsPanel.Children.Add(row);
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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ShowUndoBanner))
        {
            DispatcherQueue.TryEnqueue(UpdateUndoBanner);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (_eventsAttached)
        {
            ViewModel.FeaturedSections.CollectionChanged -= OnSectionsChanged;
            ViewModel.Sections.CollectionChanged -= OnSectionsChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _eventsAttached = false;
        }
    }
}
