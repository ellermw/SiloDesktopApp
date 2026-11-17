using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

/// <summary>
/// Server-side changes to the open item: another device's metadata save
/// arriving as a catalog event (official #4357 context), and a Refresh Metadata
/// job this page started (official #4355 and #4187).
/// </summary>
public sealed partial class ItemDetailPage
{
    // One per detail visit. Disposed at navigation, so a reload or follow that
    // outlives the visit can never land on another item or a later visit.
    private DetailMetadataWatch? _serverMetadataWatch;

    private void BeginServerMetadataWatch(string contentId)
    {
        EndServerMetadataWatch();
        if (App.Services.GetService<DetailMetadataChangeHub>() is not { } hub) return;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var identity = catalog.CaptureIdentityContext();
        DetailMetadataWatch? watch = null;
        // The pump waits a coalescing window before its first reload, so the
        // watch is assigned before the reload callback can run.
        watch = hub.Watch(contentId, () => catalog.IsCurrentIdentityContext(identity),
            (structural, token) => DispatchServerMetadataReloadAsync(watch, structural, token));
        _serverMetadataWatch = watch;
    }

    private void EndServerMetadataWatch()
    {
        var watch = _serverMetadataWatch;
        _serverMetadataWatch = null;
        watch?.Dispose();
    }

    // The watch calls this from a worker thread. All page state is read on the UI thread.
    private Task<bool> DispatchServerMetadataReloadAsync(DetailMetadataWatch? watch, bool structural, CancellationToken token)
    {
        if (watch == null || token.IsCancellationRequested) return Task.FromResult(true);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = DispatcherQueue;
        if (queue == null || !queue.TryEnqueue(async () =>
            {
                try { done.TrySetResult(await ApplyServerMetadataChangeAsync(watch, structural, token)); }
                catch (Exception ex) { done.TrySetException(ex); }
            }))
        {
            done.TrySetResult(true);
        }
        return done.Task;
    }

    /// <summary>Returns false to hold the reload until the page resumes it.</summary>
    private async Task<bool> ApplyServerMetadataChangeAsync(DetailMetadataWatch watch, bool structural, CancellationToken token)
    {
        var contentId = watch.ContentId;
        if (token.IsCancellationRequested || !ReferenceEquals(_serverMetadataWatch, watch)
            || !string.Equals(_currentContentId, contentId, StringComparison.Ordinal)) return true;
        // The first read may have been served before the change; resume after first paint.
        if (ViewModel.IsLoading || ViewModel.Item == null) return false;
        if (!IsCurrentDetail(contentId)) return true;
        if (IsServerMetadataReloadHeld(watch)) return false;

        var navigation = _navigationCts;
        var scroll = ContentScroll.VerticalOffset;
        var focused = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;
        var applied = await ViewModel.RefreshServerMetadataAsync(contentId, structural);
        if (token.IsCancellationRequested || !watch.IsActive || !ReferenceEquals(_serverMetadataWatch, watch)
            || !ReferenceEquals(_navigationCts, navigation)
            || !string.Equals(_currentContentId, contentId, StringComparison.Ordinal))
            return true;
        if (!applied) return false; // Keep failed or superseded reads pending, including a new load.
        if (!IsCurrentDetail(contentId)) return true;
        UpdateUI();
        // A complete refresh can add or replace files: reread the leaf's versions too.
        if (structural && _playableContentId is { } playable && ViewModel.Item?.Type is not ("series" or "season"))
        {
            RetirePrePlayTrackChoices();
            _ = LoadWatchDetailAsync(playable);
        }
        RestoreViewportAfterServerRefresh(navigation, scroll, focused);
        return true;
    }

    // Subordinate to the foreground: an open dialog (Edit Metadata, Match,
    // Refresh, ...) or a covering player holds the reload. A dialog's close
    // and the player leaving the page resume it; nothing polls.
    private bool IsServerMetadataReloadHeld(DetailMetadataWatch watch)
    {
        if (_playerService is { State: Services.PlayerState.Expanded or Services.PlayerState.Fullscreen }) return true;
        if (XamlRoot is not { } root) return true;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is not ContentDialog dialog) continue;
            void Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
            {
                sender.Closed -= Closed;
                watch.Resume();
            }
            dialog.Closed += Closed;
            return true;
        }
        return false;
    }

    private void RestoreViewportAfterServerRefresh(CancellationTokenSource? navigation, double scroll, Control? focused)
    {
        // After layout: the patched text can change the page's height.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (navigation is not { IsCancellationRequested: false } || !ReferenceEquals(_navigationCts, navigation)) return;
            if (Math.Abs(ContentScroll.VerticalOffset - scroll) > 0.5)
                ContentScroll.ChangeView(null, Math.Min(scroll, ContentScroll.ScrollableHeight), null, disableAnimation: true);
            // Only put back focus the refresh dropped; never take it from where the user moved it.
            if (focused is { IsLoaded: true } && XamlRoot is { } root && FocusManager.GetFocusedElement(root) == null)
                focused.Focus(FocusState.Programmatic);
        });
    }

    private async Task ShowRefreshMetadataDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        var api = App.Services.GetRequiredService<MediaMaintenanceApi>();
        var watch = _serverMetadataWatch;
        var dialog = new Controls.RefreshMetadataDialog(async mode =>
        {
            if (await Services.MetadataRefreshFlow.StartAsync(api, toast, item.ContentId, mode) is not { } start) return;
            _ = FollowMetadataRefreshAsync(item, mode, start, watch);
        })
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Follows the refresh job (bounded, see <see cref="MaintenanceJobFollower"/>), then
    /// drops the item's cached detail and reloads only the visit that started it.
    /// Runs on the UI thread; the wait itself does not hold it.
    /// </summary>
    private async Task FollowMetadataRefreshAsync(MediaItemDetail item, string mode, Services.MetadataRefreshStart start,
        DetailMetadataWatch? watch)
    {
        var result = await Services.MetadataRefreshFlow.FollowAsync(
            App.Services.GetRequiredService<MediaMaintenanceApi>(),
            App.Services.GetRequiredService<Services.ToastService>(),
            App.Services.GetService<ItemDetailPrefetchCache>(),
            start, item.ContentId, item.Type, item.SeriesId, mode);
        // Identity change, cancellation or a thrown read: nothing more. Every other outcome, including an
        // unknown completion, goes to the follow decision, which reloads without inferring a replacement.
        if (result == null) return;

        // Only the visit that started the refresh follows or reloads (#4187).
        var decision = MetadataRefreshFollow.Decide(result, item.ContentId, mode, watch);
        if (decision.Action == MetadataRefreshFollowAction.None || watch == null
            || !ReferenceEquals(_serverMetadataWatch, watch)
            || !string.Equals(_currentContentId, item.ContentId, StringComparison.Ordinal)) return;
        if (decision.Action == MetadataRefreshFollowAction.FollowReplacement && decision.ReplacementContentId is { } replacement)
        {
            App.Services.GetRequiredService<NavigationService>().NavigateReplacingCurrentEntry<ItemDetailPage>(
                MediaNavigationContext.Detail(replacement, ViewModel.LibraryId));
            return;
        }
        // Through the visit's watch: held while a dialog is open or the player covers the page.
        watch.Signal(decision.Structural);
    }
}
