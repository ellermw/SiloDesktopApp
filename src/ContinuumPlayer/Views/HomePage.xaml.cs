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
        foreach (var section in ViewModel.Sections)
        {
            if (section.Items.Count == 0) continue;
            SectionsPanel.Children.Add(new SectionRow { Section = section });
        }
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
