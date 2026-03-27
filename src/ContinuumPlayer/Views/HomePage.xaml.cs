using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Sections.Count == 0 && ViewModel.FeaturedSections.Count == 0)
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }

        BuildContent();

        // Observe changes for rebuilds
        ViewModel.FeaturedSections.CollectionChanged += (_, _) => BuildContent();
        ViewModel.Sections.CollectionChanged += (_, _) => BuildContent();
    }

    private void BuildContent()
    {
        // Flatten featured sections into a single list for the carousel
        var featuredItems = new List<MediaItem>();
        foreach (var section in ViewModel.FeaturedSections)
        {
            foreach (var item in section.Items)
            {
                featuredItems.Add(item);
            }
        }

        HeroCarouselControl.ItemsSource = featuredItems.Count > 0 ? featuredItems : null;
        HeroCarouselControl.Visibility = featuredItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Build section rows
        SectionsPanel.Children.Clear();
        foreach (var section in ViewModel.Sections)
        {
            if (section.Items.Count == 0) continue;

            var sectionRow = new SectionRow
            {
                Section = section
            };
            SectionsPanel.Children.Add(sectionRow);
        }
    }
}
