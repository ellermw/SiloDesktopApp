using Microsoft.UI.Xaml.Navigation;

namespace ContinuumPlayer.Views;

public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string pageName)
        {
            PageTitle.Text = pageName;
            PageSubtitle.Text = $"{pageName} is coming soon.";

            // Set contextual icon based on page name
            PageIcon.Glyph = pageName switch
            {
                "Search" => "\uE721",
                "Recommendations" => "\uE735",
                "Favorites" => "\uE734",
                "Watchlist" => "\uE8B7",
                "Collections" => "\uE8FD",
                "History" => "\uE81C",
                _ => "\uE946"
            };
        }
    }
}
