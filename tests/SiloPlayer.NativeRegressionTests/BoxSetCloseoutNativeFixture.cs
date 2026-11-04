using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static partial class BoxSetNativeFixture
{
    private static async Task RunCloseoutAsync(StackPanel parent, CatalogApi catalog, SiloApiClient api,
        Wire wire, NavigationService navigation, string scenario)
    {
        if (scenario == "play-reentry")
        {
            await EpisodeQuickPlayNativeFixture.VerifyBoxSetPlayReentryAsync();
            return;
        }
        if (scenario == "artwork-identity")
        {
            const string poster = "AAAAAAAAAAAAAAAAAAAAAA==", backdrop = "AQEBAQEBAQEBAQEBAQEBAQ==";
            const string caller = "AgICAgICAgICAgICAgICAg==";
            (string? CallerUrl, string? CallerHash, string? PosterUrl, string? PosterHash, string? BackdropUrl, string? BackdropHash, string? ExpectedUrl, string? ExpectedHash)[] cases =
            [
                ("https://boxsets-native.invalid/caller", caller, "https://boxsets-native.invalid/poster", poster, null, null, "https://boxsets-native.invalid/caller", caller),
                (null, caller, "https://boxsets-native.invalid/poster", poster, null, null, "https://boxsets-native.invalid/poster", poster),
                (null, caller, null, poster, "https://boxsets-native.invalid/backdrop", backdrop, "https://boxsets-native.invalid/backdrop", backdrop),
                ("https://boxsets-native.invalid/poster", null, "https://boxsets-native.invalid/poster", poster, null, null, "https://boxsets-native.invalid/poster", poster),
                ("https://boxsets-native.invalid/caller", null, "https://boxsets-native.invalid/poster", poster, null, null, "https://boxsets-native.invalid/caller", null),
                (null, null, null, poster, "https://boxsets-native.invalid/backdrop", null, "https://boxsets-native.invalid/backdrop", null),
                (null, caller, null, poster, "https://boxsets-native.invalid/backdrop", backdrop, "https://boxsets-native.invalid/backdrop", backdrop)
            ];
            var aggregate = new BoxSetCard();
            parent.Children.Add(aggregate);
            try
            {
                await DrainAsync(parent);
                foreach (var row in cases)
                {
                    aggregate.SetSummary(new() { Id = "7", LibraryId = "12", Title = "Synthetic artwork",
                        PosterUrl = row.PosterUrl, PosterThumbhash = row.PosterHash,
                        BackdropUrl = row.BackdropUrl, BackdropThumbhash = row.BackdropHash }, row.CallerUrl, row.CallerHash);
                    typeof(BoxSetCard).GetMethod("CancelArtworkLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(aggregate, null);
                    parent.UpdateLayout();
                    var actualUrl = (string?)typeof(BoxSetCard).GetField("_artworkUrl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(aggregate);
                    var fallback = Descendants(aggregate).OfType<DefaultArtwork>().Single();
                    Check(actualUrl == row.ExpectedUrl && fallback.Thumbhash == row.ExpectedHash,
                        "aggregate artwork URL and actual fallback thumbhash must name the same selected image branch");
                }
                Program.Log("PASS:7 actual aggregate caller/poster/backdrop placeholder identity and same-URL refresh cases.");
            }
            finally
            {
                typeof(BoxSetCard).GetMethod("CancelArtworkLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(aggregate, null);
                parent.Children.Remove(aggregate);
            }
            return;
        }
        if (scenario == "aggregate-tap")
        {
            var calls = 0;
            navigation.NavigationRequestHandler = (type, args) =>
            {
                Check(type == typeof(BoxSetPage) && args is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "aggregate tap destination");
                calls++; return true;
            };
            var card = new PosterCard { MediaItem = new MediaItem { Type = "box_set", ContentId = "box_set:7",
                BoxSet = new BoxSetSummary { Id = "7", LibraryId = "12", Title = "Synthetic collection", MovieCount = 2 } } };
            parent.Children.Add(card);
            try
            {
                await DrainAsync(parent);
                var aggregate = (BoxSetCard)card.Content;
                // Replay the actual control's native tap handler and Click,
                // then its parent's bubble stage only if the event is unhandled.
                // This is controlled routed-stage coverage, not physical input.
                var tapped = new TappedRoutedEventArgs();
                typeof(BoxSetCard).GetMethod("OnTapped", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(aggregate, [tapped]);
                if (calls == 0)
                    ((IInvokeProvider)new ButtonAutomationPeer(aggregate).GetPattern(PatternInterface.Invoke)).Invoke();
                await DrainAsync(parent);
                if (!tapped.Handled)
                    typeof(PosterCard).GetMethod("OnCardTapped", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, [card, tapped]);
                Check(calls == 1 && tapped.Handled, "one aggregate tap must navigate exactly once and stop its parent bubble");
                Program.Log("PASS: controlled actual aggregate Click/Tapped and parent bubble stages navigate once.");
            }
            finally { parent.Children.Remove(card); }
            return;
        }
        Check(scenario is "access-denied" or "initial-save" or "initial-save-access" or "initial-save-retired", "known closeout scenario");
        var page = new BoxSetPage(catalog, api) { Width = 960, Height = 700 };
        parent.Children.Add(page);
        try
        {
            await DrainAsync(parent);
            if (scenario == "access-denied")
            {
                wire.Status = HttpStatusCode.Forbidden;
                await page.ViewModel.LoadAsync("7", "12"); await DrainAsync(parent);
                Check(page.ViewModel.Failure == DetailFailureKind.AccessDenied && page.ViewModel.Detail == null &&
                    Descendants(page).OfType<TextBlock>().Any(item => item.Text == "Box set unavailable") &&
                    Descendants(page).OfType<TextBlock>().Any(item => item.Text == DetailFailurePolicy.Message(DetailFailureKind.AccessDenied)) &&
                    !Descendants(page).OfType<Button>().Any(item => item.Content as string == "Try again"),
                    "403 renders profile-unavailable explanation without a futile retry");
                wire.Status = HttpStatusCode.ServiceUnavailable;
                await page.ViewModel.RefreshAsync(); await DrainAsync(parent);
                Check(Descendants(page).OfType<TextBlock>().Any(item => item.Text == "Could not load box set") &&
                    Descendants(page).OfType<Button>().Any(item => item.Content as string == "Try again"),
                    "transient errors remain retryable");
                wire.Status = HttpStatusCode.OK; await page.ViewModel.RefreshAsync();
                Check(page.ViewModel.Detail != null && page.ViewModel.Failure == DetailFailureKind.None, "authorized retry recovers");
            }
            else
            {
                wire.Hold = true;
                var initial = page.ViewModel.LoadAsync("7", "12");
                await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                page.Receive(new PlaybackProgressUpdated("second", 100, 100, true) { Persisted = saved.Task });
                await DrainAsync(parent);
                if (scenario == "initial-save-retired") { parent.Children.Remove(page); await DrainAsync(parent); }
                wire.Hold = false; wire.Release.TrySetResult(); await initial;
                if (scenario != "initial-save-retired")
                    Check(page.ViewModel.Detail?.PlayedCount == 1, "initial read precedes pending playback persistence");
                var reads = wire.Reads;
                if (scenario == "initial-save-access") api.InvalidateAccessContext();
                wire.Played = 3; saved.TrySetResult();
                await DrainAsync(parent);
                if (scenario != "initial-save-retired")
                {
                    for (var i = 0; i < 100 && (wire.Reads == reads || page.ViewModel.Detail?.PlayedCount != 3); i++) await Task.Delay(10);
                    Check(wire.Reads > reads && page.ViewModel.Detail?.PlayedCount == 3,
                        "playback invalidation received before initial membership must requery after persistence");
                }
                else
                {
                    await DrainAsync(parent);
                    Check(wire.Reads == reads && page.ViewModel.Detail == null, "retired visit must not refresh after old playback persistence");
                }
            }
            Program.Log("PASS: actual dedicated box-set closeout " + scenario + ".");
        }
        finally { wire.Release.TrySetResult(); page.ViewModel.Cancel(); parent.Children.Remove(page); }
    }

    private static async Task VerifyInitialSurfaceInvalidationsAsync(StackPanel parent, CatalogApi catalog, SiloApiClient api, Wire wire)
    {
        foreach (var kind in new[] { MediaSurfaceChangeKind.WatchedMarked, MediaSurfaceChangeKind.WatchedCleared, MediaSurfaceChangeKind.ItemMetadataRefreshed })
        {
            var page = new BoxSetPage(catalog, api) { Width = 960, Height = 700 };
            parent.Children.Add(page);
            try
            {
                await DrainAsync(parent);
                for (var i = 0; i < 100 && !page.IsLoaded; i++) await Task.Delay(10);
                Check(page.IsLoaded, "box-set page is active before initial-read invalidation");
                wire.Played = 1; wire.Hold = true;
                wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var initial = page.ViewModel.LoadAsync("7", "12");
                await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Check(page.ViewModel.IsLoading && page.ViewModel.Detail == null, "initial membership is unknown while delivery is held");
                var reads = wire.Reads;
                wire.Played = kind == MediaSurfaceChangeKind.WatchedCleared ? 0 : 3;
                page.Receive(new MediaSurfaceChanged(kind, "second", context: api.CaptureContext()));
                for (var i = 0; i < 100 && wire.Reads == reads; i++) await Task.Delay(10);
                Check(wire.Reads > reads, kind + " during the initial read must request a post-mutation snapshot");
                wire.Hold = false; wire.Release.TrySetResult(); await initial;
                for (var i = 0; i < 100 && page.ViewModel.IsLoading; i++) await Task.Delay(10);
                await DrainAsync(parent); page.UpdateLayout();
                Check(page.ViewModel.Detail?.PlayedCount == wire.Played &&
                    Descendants(page).OfType<TextBlock>().Any(text => text.Text.Contains(wire.Played + " / 3 watched")),
                    kind + " must render the refreshed count rather than the held initial snapshot");
                var settledReads = wire.Reads;
                page.Receive(new MediaSurfaceChanged(kind, "unrelated", context: api.CaptureContext()));
                await DrainAsync(parent);
                Check(wire.Reads == settledReads, "known non-members must not trigger a refresh after initial loading");
            }
            finally
            {
                wire.Hold = false; wire.Release.TrySetResult(); wire.Played = 1;
                page.ViewModel.Cancel(); parent.Children.Remove(page); await DrainAsync(parent);
            }
        }
        Program.Log("PASS: watched/cleared/metadata invalidations during held initial box-set membership replace stale snapshots and refresh visible counts.");
    }

    private static Task DrainAsync(StackPanel parent)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(parent.DispatcherQueue.TryEnqueue(() => done.TrySetResult()), "fixture dispatcher accepts completion barrier");
        return done.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
