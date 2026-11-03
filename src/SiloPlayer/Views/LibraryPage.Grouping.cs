using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Messaging;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private CancellationTokenSource? _groupedRefreshCts;
    private long _groupedLibraryVisit;
    private long _libraryFlagsRead;

    private bool IsBoxSetLibrary => ViewModel.Library is { Enabled: true, BoxSetsEnabled: true, Type: "movie" or "mixed" };

    private void AttachGroupedLibraryEvents()
    {
        _groupedLibraryVisit++;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        catalog.LibraryCacheInvalidated -= GroupedLibraryFlagsChanged;
        catalog.LibraryCacheInvalidated += GroupedLibraryFlagsChanged;
    }

    private void DetachGroupedLibraryEvents()
    {
        _groupedLibraryVisit++; _libraryFlagsRead++;
        App.Services.GetRequiredService<CatalogApi>().LibraryCacheInvalidated -= GroupedLibraryFlagsChanged;
        _groupedRefreshCts?.Cancel(); _groupedRefreshCts?.Dispose(); _groupedRefreshCts = null;
    }

    private void QueueGroupedCatalogRefresh()
    {
        if (!_isNavigated || !IsBoxSetLibrary) return;
        _groupedRefreshCts?.Cancel(); _groupedRefreshCts?.Dispose();
        var owner = _groupedRefreshCts = new();
        var visit = _groupedLibraryVisit;
        var library = ViewModel.Library;
        var context = App.Services.GetRequiredService<CatalogApi>().CaptureContext();
        _ = RefreshAsync();
        async Task RefreshAsync()
        {
            try
            {
                await Task.Delay(200, owner.Token);
                if (!_isNavigated || visit != _groupedLibraryVisit || !ReferenceEquals(library, ViewModel.Library)
                    || !App.Services.GetRequiredService<CatalogApi>().IsCurrentContext(context)) return;
                var layout = GetGridLayout();
                var range = ClampRealizedRange(GetVisibleItemRange(layout), layout);
                await ViewModel.RefreshGroupedCatalogAsync(range.StartIndex, Math.Max(1, range.EndIndex - range.StartIndex + 1));
                if (_isNavigated && visit == _groupedLibraryVisit && !owner.IsCancellationRequested)
                {
                    QueueRenderVirtualGrid(force: true);
                    // The viewport may have moved during the read. Bypass the
                    // last-requested-range shortcut even if no ViewChanged fires.
                    ScheduleVisibleRangeLoad(force: true);
                }
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(_groupedRefreshCts, owner)) { _groupedRefreshCts = null; owner.Dispose(); } }
        }
    }

    private async Task RefreshGroupedAfterPlaybackAsync(PlaybackProgressUpdated message)
    {
        var visit = _groupedLibraryVisit;
        var libraryId = ViewModel.Library?.Id;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        try { await message.Persisted; }
        catch { /* A failed write still permits re-reading the accepted server state. */ }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isNavigated && visit == _groupedLibraryVisit && ViewModel.Library?.Id == libraryId && catalog.IsCurrentContext(context))
                QueueGroupedCatalogRefresh();
        });
    }

    private void GroupedLibraryFlagsChanged()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (!_isNavigated || _activeLibraryId is not {} libraryId) return;
            var visit = _groupedLibraryVisit;
            var read = ++_libraryFlagsRead;
            var catalog = App.Services.GetRequiredService<CatalogApi>();
            var context = catalog.CaptureContext();
            // Hide cached aggregates immediately; a failed policy refresh cannot
            // leave a disabled library's prior box-set presentation mounted.
            _groupedRefreshCts?.Cancel();
            _libraryCatalogLoaded = false;
            ViewModel.CancelCatalogLoads(); ClearVirtualCards();
            ViewModel.Library = null;
            try
            {
                var libraries = await catalog.GetLibrariesAsync();
                if (!_isNavigated || visit != _groupedLibraryVisit || read != _libraryFlagsRead || !catalog.IsCurrentContext(context)) return;
                ViewModel.Library = libraries.FirstOrDefault(x => x.Id == libraryId && x.Enabled);
                if (ViewModel.Library is not null && _currentTab == "Library")
                {
                    await EnsureLibraryCatalogLoadedAsync(); await FillViewportAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch { /* Leave the retired window hidden until a later successful library refresh. */ }
        });
    }
}
