using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

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

            ViewModel.FeaturedSections.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() => BuildContent());
            ViewModel.Sections.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() => BuildContent());
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void BuildContent()
    {
        // Populate hero carousel with featured section items
        var featuredItems = new List<MediaItem>();
        foreach (var section in ViewModel.FeaturedSections)
        {
            foreach (var item in section.Items)
                featuredItems.Add(item);
        }

        if (featuredItems.Count > 0)
        {
            HeroCarouselControl.ItemsSource = featuredItems;
            HeroCarouselControl.Visibility = Visibility.Visible;
        }
        else
        {
            HeroCarouselControl.Visibility = Visibility.Collapsed;
        }

        // Non-featured sections as rows
        SectionsPanel.Children.Clear();
        foreach (var section in ViewModel.Sections)
        {
            if (section.Items.Count == 0) continue;
            SectionsPanel.Children.Add(new SectionRow { Section = section });
        }
    }
}
