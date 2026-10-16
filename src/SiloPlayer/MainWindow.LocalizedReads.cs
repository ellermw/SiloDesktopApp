using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;
using SiloPlayer.Services;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _localizedRefreshCts;
    private bool _localizedRefreshPending;

    private void OnLocalizedReadsInvalidated(long generation)
    {
        var identity = _apiClient.CaptureIdentityContext();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _apiClient.LocalizedReads.Generation || !_apiClient.IsCurrentIdentityContext(identity) || !CanExposeAuthenticatedNavigation) return;
            App.Services.GetRequiredService<ItemDetailPrefetchCache>().Clear();
            _navigationService.InvalidateLocalizedPageCaches();
            _localizedRefreshPending = true;
            var owner = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _localizedRefreshCts, owner);
            previous?.Cancel();
            previous?.Dispose();
            _ = RefreshLocalizedPageAsync(generation, identity, owner);
        });
    }

    private async Task RefreshLocalizedPageAsync(long generation, Core.Api.ApiIdentityContext identity, CancellationTokenSource owner)
    {
        try
        {
            // A local save and its realtime echo share the same mounted refresh.
            await Task.Delay(100, owner.Token);
            if (owner.IsCancellationRequested || generation != _apiClient.LocalizedReads.Generation
                || !_apiClient.IsCurrentIdentityContext(identity)) return;
            if (_playerService.State is not PlayerState.Idle) return;
            _localizedRefreshPending = false;
            switch (ContentFrame.Content)
            {
                case HomePage home:
                    home.ViewModel.InvalidateLocalizedReads();
                    break;
                case LibraryPage library:
                    await library.RefreshLocalizedContentAsync();
                    break;
                case SettingsPage:
                    // Keep the control and its just-saved preference mounted.
                    break;
                case Page:
                    _navigationService.RefreshCurrentEntry();
                    break;
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "localized_page_refresh"); }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _localizedRefreshCts, null, owner), owner)) owner.Dispose();
        }
    }
}
