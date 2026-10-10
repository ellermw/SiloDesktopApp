using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private CancellationTokenSource? _shuffleLaunch;
    private async void LibraryShuffle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isNavigated || ViewModel.Library == null || _shuffleLaunch != null) return;
        var id = ViewModel.Library.Id;
        var owner = _shuffleLaunch = new CancellationTokenSource();
        LibraryShuffleButton.IsEnabled = false;
        try { await App.Services.GetRequiredService<PlayerService>().StartShuffleAsync(new ShuffleScopeRequest("library", id.ToString(System.Globalization.CultureInfo.InvariantCulture)), owner.Token); }
        finally { if (ReferenceEquals(owner, _shuffleLaunch)) { _shuffleLaunch = null; LibraryShuffleButton.IsEnabled = true; } owner.Dispose(); }
    }

    private void UpdateShuffleHeader()
    {
        var type = ViewModel.Library?.Type?.Trim().ToLowerInvariant();
        LibraryShuffleButton.Visibility = type is "movie" or "movies" or "series" or "tv" or "show" or "tvshows" or "mixed" ? Visibility.Visible : Visibility.Collapsed;
        var icon = new FontIcon { Glyph = "\uE8B1", FontSize = 16 };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon } };
        if (ActualWidth >= 640) content.Children.Add(new TextBlock { Text = "Shuffle", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        LibraryShuffleButton.Content = content;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LibraryShuffleButton, "Shuffle");
    }
}
