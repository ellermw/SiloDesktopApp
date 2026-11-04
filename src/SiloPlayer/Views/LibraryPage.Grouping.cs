using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private CancellationTokenSource? _groupedRefreshCts;
    private long _groupedLibraryVisit;
    private long _libraryFlagsRead;
    private bool _groupedCatalogDirty;
    private long _groupedStructureRequested, _groupedStructureApplied;
    private bool _libraryFlagsDirty;
    private long _nextGroupedCatalogRefreshAt;
    private const int GroupedCatalogRefreshCooldownMilliseconds = 5000;

    private bool IsBoxSetLibrary => ViewModel.Library is { Enabled: true, BoxSetsEnabled: true, Type: "movie" or "movies" or "mixed" };
    private bool CanRefreshGroupedGrid => _isNavigated && IsBoxSetLibrary && _currentTab == "Library"
        && _currentAudiobookAxis == "books" && _libraryCatalogLoaded && LibraryContentArea.Visibility == Visibility.Visible;

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
        CancelGroupedCatalogRefresh();
    }

    private void CancelGroupedCatalogRefresh()
    {
        if (_groupedRefreshCts is null) return;
        _groupedCatalogDirty = true;
        var pending = _groupedRefreshCts; _groupedRefreshCts = null;
        pending.Cancel(); pending.Dispose();
    }

    private void QueueGroupedCatalogRefresh(bool structural = true)
    {
        if (!IsBoxSetLibrary) return;
        if (structural) _groupedStructureRequested++;
        _groupedCatalogDirty = true;
        if (!CanRefreshGroupedGrid || _groupedRefreshCts is not null) return;
        var owner = _groupedRefreshCts = new();
        var token = owner.Token;
        var visit = _groupedLibraryVisit;
        var library = ViewModel.Library;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        _ = RefreshAsync();
        bool Current() => !token.IsCancellationRequested && CanRefreshGroupedGrid && visit == _groupedLibraryVisit
            && ReferenceEquals(library, ViewModel.Library) && catalog.IsCurrentContext(context);
        async Task RefreshAsync()
        {
            try
            {
                // Coalesce a scan burst, including events arriving between reads.
                // Keep a trailing refresh but bound network reads to one per cooldown.
                while (Current() && _groupedCatalogDirty)
                {
                    var delay = Math.Clamp(_nextGroupedCatalogRefreshAt - Environment.TickCount64,
                        500, GroupedCatalogRefreshCooldownMilliseconds);
                    await Task.Delay((int)delay, token);
                    if (!Current()) break;
                    if (ViewModel.IsLetterJumpInProgress) { await Task.Delay(100, token); continue; }
                    _groupedCatalogDirty = false;
                    var structuralRevision = _groupedStructureRequested;
                    var requiresStructure = structuralRevision != _groupedStructureApplied;
                    _nextGroupedCatalogRefreshAt = Environment.TickCount64 + GroupedCatalogRefreshCooldownMilliseconds;
                    var layout = GetGridLayout();
                    var range = ClampRealizedRange(GetVisibleItemRange(layout), layout);
                    var result = await ViewModel.RefreshGroupedCatalogForChangeAsync(range.StartIndex,
                        Math.Max(1, range.EndIndex - range.StartIndex + 1), token, stateOnly: !requiresStructure);
                    if (result != LibraryViewModel.GroupedRefreshResult.Completed) _groupedCatalogDirty = true;
                    if (!Current() || result == LibraryViewModel.GroupedRefreshResult.Failed) break;
                    if (result == LibraryViewModel.GroupedRefreshResult.Completed)
                    {
                        _groupedStructureApplied = structuralRevision;
                        QueueRenderVirtualGrid(force: true);
                        ScheduleVisibleRangeLoad(force: true);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_groupedRefreshCts, owner)) { _groupedRefreshCts = null; owner.Dispose(); }
            }
        }
    }

    private async Task RefreshGroupedAfterPlaybackAsync(PlaybackProgressUpdated message)
    {
        var visit = _groupedLibraryVisit;
        var libraryId = ViewModel.Library?.Id;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var owner = catalog.CaptureIdentityContext();
        try { await message.Persisted; }
        catch { /* A failed write still permits re-reading the accepted server state. */ }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isNavigated && visit == _groupedLibraryVisit && ViewModel.Library?.Id == libraryId && catalog.IsCurrentIdentityContext(owner))
                QueueGroupedCatalogRefresh(structural: false);
        });
    }

    private static bool LibraryScopeChanged(Library? previous, Library current) => previous is null
        || previous.Id != current.Id || previous.Enabled != current.Enabled
        || previous.BoxSetsEnabled != current.BoxSetsEnabled || previous.Type != current.Type;

    private void ShowLibraryUnavailable()
    {
        _libraryCatalogLoaded = false;
        CancelGroupedCatalogRefresh();
        ViewModel.CancelCatalogLoads();
        ViewModel.Library = null;
        ClearVirtualCards();
        LibraryUnavailableState.Visibility = Visibility.Visible;
        HeaderGrid.Visibility = FilterBar.Visibility = ActiveFiltersBar.Visibility = Visibility.Collapsed;
        LibraryContentArea.Visibility = RecommendedPanel.Visibility = CollectionsPanel.Visibility = AudiobookGroupsPanel.Visibility = Visibility.Collapsed;
    }

    private void GroupedLibraryFlagsChanged()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            _libraryFlagsDirty = true;
            await RefreshLibraryFlagsAsync(reloadVisible: true);
        });
    }

    private async Task RefreshLibraryFlagsAsync(bool reloadVisible = false)
    {
        if (!_isNavigated || _activeLibraryId is not {} libraryId) return;
        var visit = _groupedLibraryVisit;
        var read = ++_libraryFlagsRead;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        try
        {
            // Read policy first. An unrelated invalidation or transient failure
            // must not retire a working catalog (including non-movie libraries).
            var libraries = await catalog.GetLibrariesAsync(includeHidden: true);
            if (!_isNavigated || visit != _groupedLibraryVisit || read != _libraryFlagsRead || !catalog.IsCurrentContext(context)) return;
            _libraryFlagsDirty = false;
            var current = libraries.FirstOrDefault(x => x.Id == libraryId && x.Enabled);
            if (current is null) { ShowLibraryUnavailable(); return; }
            if (!LibraryScopeChanged(ViewModel.Library, current)) return;
            CancelGroupedCatalogRefresh();
            _libraryCatalogLoaded = false;
            ViewModel.Library = current;
            ClearVirtualCards();
            LibraryUnavailableState.Visibility = Visibility.Collapsed;
            ResetRecommendedContent();
            _collectionsLoaded = false;
            if (!reloadVisible) return;
            HeaderGrid.Visibility = Visibility.Visible;
            ShowTab(_currentTab);
            if (_currentTab == "Library" && _currentAudiobookAxis == "books")
            {
                await EnsureLibraryCatalogLoadedAsync(); await FillViewportAsync();
            }
            else if (_currentTab == "Recommended") await LoadRecommendationsAsync();
            else if (_currentTab == "Collections") await LoadCollectionsAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_isNavigated || visit != _groupedLibraryVisit || read != _libraryFlagsRead || !catalog.IsCurrentContext(context)) return;
            _libraryFlagsDirty = true;
            LocalLog.AppendException("catalog_errors.txt", ex, "library_flags_refresh");
            // Retain the previous window and retry when the Library tab is
            // selected again, on the next visit or on policy invalidation.
            App.Services.GetRequiredService<ToastService>().Error(ex);
        }
    }
}
