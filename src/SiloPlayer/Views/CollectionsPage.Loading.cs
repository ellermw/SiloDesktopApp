using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views;

public sealed partial class CollectionsPage
{
    private bool _wallActive;
    private bool _wallAdmissionQueued;
    private bool _wallLoading;
    private long _wallGeneration;

    private void ResetWallAdmission()
    {
        ++_wallGeneration;
        _wallAdmissionQueued = _wallLoading = false;
        _autoLoadedLibrary = null;
        ViewModel.CancelServerCollectionLoads();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _wallActive = false;
        ResetWallAdmission();
        base.OnNavigatedFrom(e);
    }

    private void UpdateWallLoadAction()
    {
        var show = CollectionsWall.ShowsServer(_show) && CollectionsWall.Picked(_show, ViewModel.ServerLibraries.ToArray()) == null;
        var busy = _wallLoading || ViewModel.IsLoadingServerLibraryRest;
        CollectionsLoadMore.Visibility = show && (busy || ViewModel.NextServerLibrary != null || CollectionsLoadMore.FocusState != FocusState.Unfocused)
            ? Visibility.Visible : Visibility.Collapsed;
        CollectionsLoadMore.Content = busy ? "Loading…" : ViewModel.NextServerLibrary == null ? "No more server collections to load" : "Load more server collections";
        AutomationProperties.SetItemStatus(CollectionsLoadMore, busy ? "Loading" : ViewModel.NextServerLibrary == null ? "Complete" : "");
        AutomationProperties.SetHelpText(CollectionsLoadMore, busy ? "Please wait while server collections load." : "");
        CollectionsLoadMore.Opacity = busy ? .5 : 1;
    }

    private void QueueWallAdmission()
    {
        if (!_wallActive || !IsLoaded || _wallAdmissionQueued) return;
        _wallAdmissionQueued = true;
        var generation = _wallGeneration;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _wallGeneration) return;
            _wallAdmissionQueued = false;
            if (!_wallActive || !IsLoaded || _wallLoading || ViewModel.IsLoadingServerLibraryRest
                || ViewModel.IsLoadingServerCollections || !ViewModel.ServerLoaded || !ViewModel.HasCurrentServerCollections
                || !CollectionsWall.ShowsServer(_show)) return;
            // A refresh re-reads the loaded rests first; admission is queued again when it ends.
            if (ViewModel.IsRefreshingServerCollections) return;
            var picked = CollectionsWall.Picked(_show, ViewModel.ServerLibraries.ToArray());
            if (picked != null)
            {
                if (_autoLoadedLibrary == picked.LibraryId || ViewModel.HasServerLibraryRest(picked.LibraryId)
                    || ViewModel.ServerLibraryError(picked.LibraryId) != null) return;
                _autoLoadedLibrary = picked.LibraryId;
                _ = LoadWallLibraryAsync(picked.LibraryId, false);
                return;
            }
            if (ViewModel.NextServerLibrary is not { } next || CollectionsScrollViewer.ViewportHeight <= 0
                || CollectionsLoadMore.ActualHeight <= 0 || CollectionsLoadMore.Visibility != Visibility.Visible) return;
            var bounds = CollectionsLoadMore.TransformToVisual(CollectionsScrollViewer)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, CollectionsLoadMore.ActualWidth, CollectionsLoadMore.ActualHeight));
            if (bounds.Top <= CollectionsScrollViewer.ViewportHeight + 600 && bounds.Bottom >= -600)
                _ = LoadWallLibraryAsync(next.LibraryId, false);
        });
    }

    private async void CollectionsLoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NextServerLibrary is { } next) await LoadWallLibraryAsync(next.LibraryId, true);
    }

    private async Task LoadWallLibraryAsync(int libraryId, bool moveFocus)
    {
        if (!_wallActive || !IsLoaded || _wallLoading || ViewModel.IsLoadingServerLibraryRest || !ViewModel.HasCurrentServerCollections) return;
        if (ViewModel.IsRefreshingServerCollections) return;
        var generation = _wallGeneration;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var context = client.CaptureContext();
        var before = ViewModel.GetServerCollectionCards().Select(card => card.Collection.Id).ToHashSet(StringComparer.Ordinal);
        bool Current() => generation == _wallGeneration && _wallActive && IsLoaded && client.IsCurrentContext(context);
        _wallLoading = true;
        UpdateWallLoadAction();
        try
        {
            await ViewModel.LoadServerLibraryAsync(libraryId, isCurrent: Current);
            if (!Current()) return;
            _wallLoading = false;
            BuildWall();
            if (moveFocus)
            {
                var added = ViewModel.GetServerCollectionCards().FirstOrDefault(card => !before.Contains(card.Collection.Id));
                var target = added == null ? DescendantButtons(ServerCollectionsRows).FirstOrDefault()
                    : DescendantButtons(CollectionsGrid).FirstOrDefault(button => AutomationProperties.GetName(button) == "Open " + added.Collection.Title);
                (target ?? CollectionsLoadMore).Focus(FocusState.Programmatic);
            }
        }
        finally
        {
            // A profile or access change retires this load without a reset, so its busy state ends here too.
            if (generation == _wallGeneration)
            {
                _wallLoading = false;
                UpdateWallLoadAction();
                if (Current()) QueueWallAdmission();
            }
        }
    }
}
