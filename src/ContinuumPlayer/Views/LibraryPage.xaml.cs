using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Library library)
        {
            LibraryTitle.Text = library.Name;
        }
    }
}
