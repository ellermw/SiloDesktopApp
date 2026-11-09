using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    /// <summary>
    /// The server closes the event channel every few minutes; each reconnect asks
    /// mounted routes to catch up. A loaded title grid or Recommended tab catches up
    /// in place instead of being remounted, which used to throw away the scroll
    /// position, the loaded windows and the letter rail. Collections and audiobook
    /// groups still take the ordinary remount.
    /// </summary>
    public bool CanPreserveOnReconnect => _isNavigated && _libraryStateApplied && ViewModel.Library is not null &&
        string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) &&
        (_currentTab == "Library" && _currentAudiobookAxis == "books" && _libraryCatalogLoaded && ViewModel.WindowStartIndex >= 0 ||
         _currentTab == "Recommended" && _recommendedLoaded);

    public async Task CatchUpAfterReconnectAsync()
    {
        try
        {
            if (!_isNavigated) return;
            if (_currentTab == "Recommended")
            {
                QueueRecommendedRefresh();
                return;
            }
            if (_currentTab != "Library" || _currentAudiobookAxis != "books") return;
            var layout = GetGridLayout();
            var range = ClampRealizedRange(GetVisibleItemRange(layout), layout);
            if (range.EndIndex < range.StartIndex) range = (Math.Max(0, ViewModel.WindowStartIndex), Math.Max(0, ViewModel.WindowStartIndex));
            var result = await ViewModel.RefreshInPlaceAsync(range.StartIndex, range.EndIndex - range.StartIndex + 1);
            if (!_isNavigated) return;
            LocalLog.AppendLine("catalog_queries.txt", $"library_transport_catchup result={result} first_row={_currentFirstRow} offset={_libraryVerticalOffset:0}");
            if (result == LibraryViewModel.CatalogRefreshResult.NotLoaded)
            {
                // Nothing was retained to keep: load the visible range the ordinary way.
                await EnsureLibraryCatalogLoadedAsync();
                await FillViewportAsync();
            }
            // Rows outside the reread range (or a deferred read) load on demand under
            // the new context, beside the retained cards.
            _lastRequestedStartIndex = _lastRequestedEndIndex = -1;
            ScheduleVisibleRangeLoad(force: true);
        }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "library_transport_catchup"); }
    }
}
