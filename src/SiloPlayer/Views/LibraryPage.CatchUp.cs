using SiloPlayer.Core.Services;
using SiloPlayer.Core.Api;
using Microsoft.UI.Xaml;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    // All scan state is owned by the UI thread. An event never cancels a read;
    // a fixed batch window and one dirty bit bound both storms and trailing work.
    private DispatcherTimer? _catalogContentTimer;
    private bool _catalogContentDirty, _catalogContentBatchReady;
    private bool _catalogContentRetryUsed;
    private ApiRequestContext? _catalogContentContext;
    private int? _catalogContentLibrary;
    private long _catalogContentEpoch, _catalogContentOwner;

    private void QueueCatalogContentRefresh(ApiRequestContext context, int? libraryId)
    {
        if (!_isNavigated || context != _stateSettings.CaptureContext() || ViewModel.Library is not { } library ||
            libraryId is { } scoped && scoped != library.Id) return;
        _catalogContentContext = context;
        _catalogContentLibrary = library.Id;
        _catalogContentRetryUsed = false;
        _catalogContentDirty = true;
        WakeCatalogContentRefresh();
    }

    private void WakeCatalogContentRefresh()
    {
        if (!_isNavigated || !IsLoaded || !_catalogContentDirty) return;
        if (_catalogContentContext != _stateSettings.CaptureContext() || _catalogContentLibrary != ViewModel.Library?.Id)
        {
            _catalogContentTimer?.Stop();
            _catalogContentDirty = _catalogContentBatchReady = false;
            return;
        }
        if (_currentTab != "Library" || _currentAudiobookAxis != "books" || !_libraryCatalogLoaded) return;
        if (_catalogContentBatchReady) { TryStartCatalogContentRefresh(); return; }
        _catalogContentTimer ??= CreateCatalogContentTimer();
        // Do not restart the two-second window when another event arrives.
        if (!_catalogContentTimer.IsEnabled) _catalogContentTimer.Start();
    }

    private DispatcherTimer CreateCatalogContentTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += CatalogContentTimer_Tick;
        return timer;
    }

    private void CatalogContentTimer_Tick(object? sender, object e)
    {
        _catalogContentTimer?.Stop();
        if (!_catalogContentDirty || !_isNavigated || !IsLoaded) return;
        _catalogContentBatchReady = true;
        TryStartCatalogContentRefresh();
    }

    private void TryStartCatalogContentRefresh()
    {
        if (!_catalogContentDirty || !_catalogContentBatchReady || _catalogContentOwner != 0 || !_isNavigated || !IsLoaded ||
            _currentTab != "Library" || _currentAudiobookAxis != "books" || !_libraryCatalogLoaded) return;
        if (_catalogContentContext != _stateSettings.CaptureContext() || _catalogContentLibrary != ViewModel.Library?.Id)
        {
            _catalogContentDirty = _catalogContentBatchReady = false;
            return;
        }
        // The native landing owns the viewport after the seek's HTTP read ends.
        if (ViewModel.IsLoading || ViewModel.IsCatalogRefreshInProgress || ViewModel.IsLetterJumpInProgress || _queuedLetterJumpVersion != 0 ||
            _pendingLibraryLetterCommit is not null || _pendingLibraryScroll is { Owner: not null }) return;
        _catalogContentDirty = _catalogContentBatchReady = false;
        var owner = _catalogContentOwner = ++_catalogContentEpoch;
        var visit = _libraryVisit;
        var context = _catalogContentContext.Value;
        var library = _catalogContentLibrary;
        _ = RefreshCatalogContentAsync(owner, visit, context, library);
    }

    private async Task RefreshCatalogContentAsync(long owner, long visit, ApiRequestContext context, int? library)
    {
        try
        {
            var layout = GetGridLayout();
            var range = ClampRealizedRange(GetVisibleItemRange(layout), layout);
            var result = await ViewModel.RefreshInPlaceAsync(range.StartIndex, Math.Max(1, range.EndIndex - range.StartIndex + 1),
                kind: LibraryViewModel.CatalogRefreshKind.ContentChanged);
            if (_catalogContentOwner != owner || visit != _libraryVisit || !_isNavigated ||
                context != _stateSettings.CaptureContext() || library != ViewModel.Library?.Id) return;
            LocalLog.AppendLine("catalog_queries.txt", $"library_content_refresh result={result} first_row={_currentFirstRow}");
            QueueRenderVirtualGrid(force: true);
            // Do not consume the final scan event just because a seek canceled
            // its read or the server failed/churned. Allow one delayed retry per
            // event batch; new events already own their own trailing batch.
            if ((result is LibraryViewModel.CatalogRefreshResult.Deferred or LibraryViewModel.CatalogRefreshResult.Failed) &&
                !_catalogContentDirty && !_catalogContentRetryUsed)
            {
                _catalogContentRetryUsed = true;
                _catalogContentDirty = true;
                _catalogContentBatchReady = false;
            }
        }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "library_content_refresh"); }
        finally
        {
            // An old visit must never release a new visit's running owner.
            if (_catalogContentOwner == owner && visit == _libraryVisit)
            {
                _catalogContentOwner = 0;
                // Let foreground callers joined to this read finish their
                // continuations before a trailing batch claims the query.
                DispatcherQueue.TryEnqueue(WakeCatalogContentRefresh);
            }
        }
    }

    private void StopCatalogContentRefresh(bool retainDirty = true)
    {
        _catalogContentTimer?.Stop();
        if (retainDirty && _catalogContentOwner != 0) _catalogContentDirty = true;
        if (!retainDirty)
        {
            _catalogContentDirty = false;
            _catalogContentRetryUsed = false;
        }
        _catalogContentBatchReady = false;
        ++_catalogContentEpoch;
        _catalogContentOwner = 0;
    }

    private void OnLibraryAccessChanged(ApiRequestContext context)
    {
        var visit = _libraryVisit;
        void Retire()
        {
            var current = _stateSettings.CaptureContext();
            // A following transport reconnect must not swallow an explicit
            // revocation before its UI dispatch. New access/identity still wins.
            if (!_isNavigated || visit != _libraryVisit ||
                (context with { RequestContextGeneration = current.RequestContextGeneration }) != current) return;
            StopCatalogContentRefresh(retainDirty: false);
            HideUnconfirmedCatalogRows();
            // Cancel actual query work, including a content refresh's shared task.
            // A canceled RefreshInPlace waiter alone would leave that work alive.
            ViewModel.CancelCatalogLoads();
        }
        if (DispatcherQueue.HasThreadAccess) Retire();
        else DispatcherQueue.TryEnqueue(Retire);
    }

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
    // The request context whose reads last authorized the Recommended rows,
    // tracked apart from the catalog grid, which may never have been loaded.
    private SiloPlayer.Core.Api.ApiRequestContext? _recommendedRowsContext;
    // Events that arrived during a confirming reread; they get one follow-up read.
    private bool _recommendedRefreshAgain;
    private double _unconfirmedRecommendedOffset;

    /// <summary>A cached page may have missed a reconnect while another route was mounted.</summary>
    private bool RecommendedRowsRequireConfirmation =>
        _recommendedLoaded && _recommendedRowsContext != _stateSettings.CaptureContext();

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
        // This reread replaced the layout, so an earlier load failure no longer applies.
        RecommendedError.Text = "";
        RecommendedErrorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
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
            // A deferred catch-up may remain pending; a failed or churned one retires
            // its unconfirmed rows without collapsing the extent. The range loader
            // retries a pending one and otherwise reads the viewport the ordinary way.
            _lastRequestedStartIndex = _lastRequestedEndIndex = -1;
            ScheduleVisibleRangeLoad(force: true);
            _ = RetryDeferredCatchUpAsync(++_catchUpRetryVersion);
            WakeCatalogContentRefresh();
        }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "library_transport_catchup"); }
    }

    private long _catchUpRetryVersion;
    private long _runningCatchUpRetryVersion;

    /// <summary>
    /// Any pending catch-up needs a retry owner, not only one the shell started:
    /// a reconnect behind expanded playback or a deferred viewport read has none.
    /// </summary>
    private void EnsureDeferredCatchUpRetry()
    {
        if (_runningCatchUpRetryVersion != 0 && _runningCatchUpRetryVersion == _catchUpRetryVersion) return;
        _ = RetryDeferredCatchUpAsync(++_catchUpRetryVersion);
    }

    /// <summary>
    /// A newer seek or a metadata-language change can defer the catch-up with the
    /// grid still quarantined. Retry with backoff until it settles instead of waiting
    /// for the next scroll or reconnect; held rows stay hidden until confirmed.
    /// </summary>
    private async Task RetryDeferredCatchUpAsync(long version)
    {
        _runningCatchUpRetryVersion = version;
        try
        {
            for (var delay = 1000; ; delay = Math.Min(delay * 2, 30000))
            {
                await Task.Delay(delay);
                if (version != _catchUpRetryVersion || !_isNavigated || _currentTab != "Library" ||
                    _currentAudiobookAxis != "books" || !ViewModel.RequiresCatalogConfirmation)
                    return;
                LocalLog.AppendLine("catalog_queries.txt", $"library_transport_catchup_retry delay_ms={delay}");
                _lastRequestedStartIndex = _lastRequestedEndIndex = -1;
                ScheduleVisibleRangeLoad(force: true);
            }
        }
        catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "library_transport_catchup_retry"); }
        finally
        {
            if (_runningCatchUpRetryVersion == version) _runningCatchUpRetryVersion = 0;
        }
    }
}
