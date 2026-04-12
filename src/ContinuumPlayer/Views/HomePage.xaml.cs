using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }
    private bool _eventsAttached;

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
        SmoothScrollHelper.Attach(ContentScrollViewer);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Sections.Count == 0 && ViewModel.FeaturedSections.Count == 0)
            {
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }

            BuildContent();

            if (!_eventsAttached)
            {
                _eventsAttached = true;
                ViewModel.FeaturedSections.CollectionChanged += OnSectionsChanged;
                ViewModel.Sections.CollectionChanged += OnSectionsChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
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
        var heroSection = ViewModel.FeaturedSections.FirstOrDefault(s => s.Items.Count > 0);
        if (heroSection != null)
        {
            var limit = heroSection.ItemLimit > 0 ? heroSection.ItemLimit : heroSection.Items.Count;
            var heroItems = heroSection.Items.Take(limit).ToList();
            HeroCarouselControl.ItemsSource = heroItems;
            HeroCarouselControl.Visibility = Visibility.Visible;
        }
        else
        {
            HeroCarouselControl.Visibility = Visibility.Collapsed;
        }

        // Non-featured sections as rows
        SectionsPanel.Children.Clear();
        var mainVm = App.Services.GetRequiredService<MainViewModel>();
        foreach (var section in ViewModel.Sections)
        {
            // F12: sections with empty Items are still loading (skeleton shown
            // by SectionRow). Include them so skeletons render.
            var row = new SectionRow { Section = section };

            // Wire "Explore all" for library-backed section types.
            var library = TryResolveLibrary(section, mainVm);
            if (library != null)
            {
                var lib = library; // capture for closure
                row.OnViewAll = () =>
                {
                    var nav = App.Services.GetRequiredService<NavigationService>();
                    nav.Navigate<LibraryPage>(lib);
                };
                row.Section = section; // re-set so UpdateSection picks up OnViewAll visibility
            }

            SectionsPanel.Children.Add(row);
        }

        // Empty state when the server returned zero sections.
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

    private async void WatchTonightButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new WatchTonightDialog { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Could not open Watch Tonight: {ex.Message}";
        }
    }

    private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => BuildContent());
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ShowUndoBanner))
        {
            DispatcherQueue.TryEnqueue(UpdateUndoBanner);
        }
    }

    // Section types that represent browseable library content. These are the
    // server-generated home sections that are scoped to a specific library and
    // make sense to "Explore all" by navigating to the full library page.
    private static readonly HashSet<string> BrowseableSectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "recently_added",
        "popular",
        "genre",
        "collection",
    };

    /// <summary>
    /// Attempt to resolve the <see cref="Library"/> that a home section belongs
    /// to. The server section ID typically follows the format
    /// <c>{section_type}_{library_id}</c> (e.g. "recently_added_1"). Returns
    /// null when the section type is not library-browseable or no matching
    /// library is found.
    /// </summary>
    private static Library? TryResolveLibrary(HomeSectionWithItems section, MainViewModel mainVm)
    {
        if (!BrowseableSectionTypes.Contains(section.SectionType))
            return null;

        // Try to extract a trailing numeric library ID from the section ID.
        // Expected format: "{type}_{library_id}" or "{type}:{library_id}".
        var id = section.Id;
        if (string.IsNullOrEmpty(id))
            return null;

        int lastSep = id.LastIndexOfAny(['_', ':']);
        if (lastSep < 0 || lastSep >= id.Length - 1)
            return null;

        if (!int.TryParse(id.AsSpan(lastSep + 1), out int libraryId))
            return null;

        foreach (var lib in mainVm.Libraries)
        {
            if (lib.Id == libraryId)
                return lib;
        }

        return null;
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
