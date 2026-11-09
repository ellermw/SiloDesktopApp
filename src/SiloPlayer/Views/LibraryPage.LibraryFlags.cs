using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage
{
    private long _libraryFlagsVisit;
    private long _libraryFlagsRead;
    private long _libraryFlagsAttemptedVisit = -1;
    private bool _libraryFlagsDirty;

    private void AttachLibraryFlagsEvents()
    {
        _libraryFlagsVisit++;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        catalog.LibraryCacheInvalidated -= LibraryFlagsChanged;
        catalog.LibraryCacheInvalidated += LibraryFlagsChanged;
    }

    private void DetachLibraryFlagsEvents()
    {
        _libraryFlagsVisit++; _libraryFlagsRead++;
        App.Services.GetRequiredService<CatalogApi>().LibraryCacheInvalidated -= LibraryFlagsChanged;
    }

    private static bool LibraryScopeChanged(Library? previous, Library current) => previous is null
        || previous.Id != current.Id || previous.Enabled != current.Enabled || previous.Type != current.Type;

    private void ShowLibraryUnavailable()
    {
        _libraryCatalogLoaded = false;
        ViewModel.CancelCatalogLoads();
        ViewModel.Library = null;
        ClearVirtualCards();
        LibraryUnavailableState.Visibility = Visibility.Visible;
        HeaderGrid.Visibility = FilterBar.Visibility = ActiveFiltersBar.Visibility = Visibility.Collapsed;
        LibraryContentArea.Visibility = RecommendedPanel.Visibility = CollectionsPanel.Visibility = AudiobookGroupsPanel.Visibility = Visibility.Collapsed;
    }

    private void LibraryFlagsChanged()
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
        var visit = _libraryFlagsVisit;
        var read = ++_libraryFlagsRead;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        try
        {
            // Read policy first. An unrelated invalidation or transient failure
            // must not retire a working catalog.
            var libraries = await catalog.GetLibrariesAsync(includeHidden: true);
            if (!_isNavigated || visit != _libraryFlagsVisit || read != _libraryFlagsRead || !catalog.IsCurrentContext(context)) return;
            _libraryFlagsDirty = false;
            var current = libraries.FirstOrDefault(x => x.Id == libraryId && x.Enabled);
            if (current is null) { ShowLibraryUnavailable(); return; }
            if (!LibraryScopeChanged(ViewModel.Library, current)) return;
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
            if (!_isNavigated || visit != _libraryFlagsVisit || read != _libraryFlagsRead || !catalog.IsCurrentContext(context)) return;
            // Only a reported failure counts as this visit's attempt; a canceled or
            // superseded read leaves the dirty policy for the Library tab to read.
            _libraryFlagsDirty = true;
            _libraryFlagsAttemptedVisit = visit;
            LocalLog.AppendException("catalog_errors.txt", ex, "library_flags_refresh");
            // Retain the previous window. Opening the Library tab must not repeat
            // this visit's failed read and toast; the next visit, a policy
            // invalidation or a deliberate current-tab retry reads again.
            App.Services.GetRequiredService<ToastService>().Error(ex);
        }
    }
}
