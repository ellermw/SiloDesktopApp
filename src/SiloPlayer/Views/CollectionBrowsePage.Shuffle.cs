using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class CollectionBrowsePage
{
    private CancellationTokenSource? _shuffleLaunch;
    private async void CollectionShuffle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isNavigated || _currentArgs is not { } args || _total < 1 || _shuffleLaunch != null) return;
        var owner = _shuffleLaunch = new CancellationTokenSource();
        CollectionShuffleButton.IsEnabled = false;
        try { await App.Services.GetRequiredService<PlayerService>().StartShuffleAsync(new ShuffleScopeRequest(args.IsUserCollection ? "user_collection" : "library_collection", args.CollectionId), owner.Token); }
        finally { if (ReferenceEquals(owner, _shuffleLaunch)) { _shuffleLaunch = null; CollectionShuffleButton.IsEnabled = true; } owner.Dispose(); }
    }
}
