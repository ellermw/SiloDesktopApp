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

    private bool _catalogCardsAwaitingConfirmation;
    private bool _recommendedCardsAwaitingConfirmation;
    private SiloPlayer.Core.Api.ApiRequestContext? _recommendedUnconfirmedContext;
    private double _unconfirmedRecommendedOffset;

    private void HideUnconfirmedRecommendedRows()
    {
        var context = _stateSettings.CaptureContext();
        if (!_recommendedLoaded || _recommendedUnconfirmedContext == context) return;
        _recommendedUnconfirmedContext = context;
        if (!_recommendedCardsAwaitingConfirmation)
            _unconfirmedRecommendedOffset = RecommendedPanel.VerticalOffset;
        _recommendedCardsAwaitingConfirmation = true;
        // Retire old section/layout continuations before they can republish cards.
        ++_recommendationsVersion;
        CancelRecommendedRefresh();
        _recommendationsLoadCts?.Cancel();
        RecommendedPanel.IsHitTestVisible = false;
        RecommendedPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void ConfirmRecommendedRows()
    {
        if (!_recommendedCardsAwaitingConfirmation) return;
        _recommendedCardsAwaitingConfirmation = false;
        RecommendedPanel.IsHitTestVisible = true;
        RecommendedPanel.Visibility = _currentTab == "Recommended"
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        RecommendedPanel.UpdateLayout();
        RecommendedPanel.ChangeView(null, _unconfirmedRecommendedOffset, null, true);
    }

    private void FailUnconfirmedRecommendedRows()
    {
        if (!_recommendedCardsAwaitingConfirmation) return;
        // A failed authorized reread must not fall back to old sections or pins.
        ResetRecommendedContent();
        RecommendedPanel.Visibility = _currentTab == "Recommended"
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        RecommendedError.Text = "Recommendations could not be refreshed. Please retry.";
        RecommendedErrorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    /// <summary>
    /// A reconnect may have missed an access revocation. Hide cards immediately,
    /// even while playback defers the refresh, without changing the canvas extent.
    /// Keep their bindings/artwork for a successful, same-position refresh.
    /// </summary>
    public void HideUnconfirmedCatalogRows()
    {
        HideUnconfirmedRecommendedRows();
        _catalogCardsAwaitingConfirmation = true;
        _cardBindTimer?.Stop();
        _pendingCardBinds.Clear();
        _pendingCardBindSet.Clear();
        _lastRenderedStartIndex = _lastRenderedEndIndex = -1;
        foreach (var card in _libraryCardSlots)
            card.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public async Task CatchUpAfterReconnectAsync()
    {
        try
        {
            if (!_isNavigated) return;
            HideUnconfirmedCatalogRows();
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
            // A deferred catch-up remains pending; a failed one retires its rows
            // without collapsing the extent. The range loader can retry either.
            _lastRequestedStartIndex = _lastRequestedEndIndex = -1;
            ScheduleVisibleRangeLoad(force: true);
        }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "library_transport_catchup"); }
    }
}
