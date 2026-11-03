using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyBoxSetAccessRefreshAsync()
    {
        await using var scope = new LifecycleScope();
        scope.Wire.BoxSetsEnabled = true;
        await scope.ShowAsync();
        foreach (var minimized in new[] { false, true })
        foreach (var handler in new[] { "OnAccessChanged", "OnReadCatchUpRequested" })
        {
            await scope.ResetAsync();
            scope.Window.ShowMainNavigation();
            scope.Wire.AcceptStart = true;
            if (minimized)
            {
                var outgoing = new PlaybackManager(scope.Playback, scope.Catalog, scope.Auth, scope.Client);
                outgoing.UseWatchDetail(new() { ContentId = "outgoing", Type = "movie", Versions = [new() { FileId = 42, Duration = 1200 }] });
                await outgoing.StartSessionAsync(42, 91);
                SetField(scope.Player, "_playbackManager", outgoing);
                typeof(PlayerService).GetProperty("ContentId")!.SetValue(scope.Player, "outgoing");
                scope.Player.SetState(PlayerState.Minimized);
                scope.Wire.Starts.Clear();
            }
            var navigation = scope.Services.GetRequiredService<NavigationService>();
            Check(navigation.NavigateImmediately(typeof(BoxSetPage), new BoxSetNavigationArgs("7", "73")), "Box-set access fixture could not enter its actual route.");
            await Until(() => scope.Frame.Content is BoxSetPage ready && ready.IsLoaded && ready.ViewModel.Detail != null);
            var page = (BoxSetPage)scope.Frame.Content;
            var play = Descendants<Button>(page).Single(button => button.Name == "BoxSetPlay");
            var cards = Descendants<PosterCard>(page).ToArray();
            var back = scope.Frame.BackStack.ToArray();
            var forward = scope.Frame.ForwardStack.ToArray();
            var unloaded = false; var cleared = false;
            page.Unloaded += (_, _) => unloaded = true;
            page.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(page.ViewModel.Detail) && page.ViewModel.Detail == null) cleared = true;
            };
            scope.Wire.HoldWatch = "boxset-film";
            Invoke(play);
            try
            {
                await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(scope.Player.State == (minimized ? PlayerState.Minimized : PlayerState.Idle), "Box-set start was not held before expansion.");
                var accepted = ((CancellationTokenSource)GetField(scope.Player, "_scopedViewerPlayCts")!).Token;
                var reads = scope.Wire.BoxSetReads;
                if (handler == "OnAccessChanged") scope.Client.InvalidateAccessContext();
                // Exercise the real shell callback and RefreshCurrentEntry, not just the model refresh.
                typeof(MainWindow).GetMethod(handler, Private)!.Invoke(scope.Window, [scope.Client.CaptureContext()]);
                await Until(() => scope.Wire.BoxSetReads > reads && page.ViewModel.IsCurrent && !page.ViewModel.IsLoading);
                Check(ReferenceEquals(scope.Frame.Content, page) && !unloaded && !cleared && !accepted.IsCancellationRequested,
                    handler + " retired accepted box-set playback or its mounted detail.");
                Check(Descendants<PosterCard>(page).SequenceEqual(cards) && ReferenceEquals(play, Descendants<Button>(page).Single(button => button.Name == "BoxSetPlay"))
                    && scope.Frame.BackStack.SequenceEqual(back) && scope.Frame.ForwardStack.SequenceEqual(forward),
                    handler + " replaced box-set controls or changed route history.");
                scope.Wire.ReleaseWatch.TrySetResult();
                await Until(() => play.IsEnabled && scope.Wire.Starts.Count == 1 && scope.Player.State == PlayerState.Expanded);
                Check(scope.Player.Manager?.CurrentSession != null && scope.Player.ErrorMessage == null
                    && scope.Wire.Watches.All(uri => uri.Query == "?library_id=73")
                    && scope.Wire.Starts.Single().GetProperty("profile_id").GetString() == "fixture",
                    handler + " did not complete the accepted box-set session under its viewer/library.");
                Program.Log($"PASS: box-set {handler} during held /watch in {(minimized ? "Minimized" : "Idle")} retains mounted route/controls/history and starts the accepted session.");
            }
            finally { scope.Wire.ReleaseWatch.TrySetResult(); }
        }
    }
}
