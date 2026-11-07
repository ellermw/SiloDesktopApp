using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
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
        await VerifyBoxSetAbandonmentAsync(scope);
        await VerifyBoxSetDepartureAsync(scope);
    }

    internal static async Task VerifyBoxSetPlayReentryAsync()
    {
        await using var scope = new LifecycleScope();
        scope.Wire.BoxSetsEnabled = true;
        await scope.ShowAsync();
        await VerifyBoxSetDepartureAsync(scope, reentryOnly: true);
    }

    private static async Task VerifyBoxSetDepartureAsync(LifecycleScope scope, bool reentryOnly = false)
    {
        foreach (var boundary in reentryOnly ? new[] { "revalidation-return" } : new[] { "authority", "request", "watch", "revalidation-return" })
        {
            await scope.ResetAsync(); scope.Window.ShowMainNavigation(); scope.Wire.AcceptStart = true;
            var navigation = scope.Services.GetRequiredService<NavigationService>();
            Check(navigation.NavigateImmediately(typeof(BoxSetPage), new BoxSetNavigationArgs("7", "73")), "Could not enter box-set departure route.");
            await Until(() => scope.Frame.Content is BoxSetPage ready && ready.IsLoaded && ready.ViewModel.Detail != null);
            var page = (BoxSetPage)scope.Frame.Content;
            var play = Descendants<Button>(page).Single(button => button.Name == "BoxSetPlay");
            var toasts = Descendants<ToastContainer>((Microsoft.UI.Xaml.DependencyObject)scope.Window.Content).Single();
            var stack = (StackPanel)toasts.FindName("ToastStack"); stack.Children.Clear();
            SemaphoreSlim? held = null;
            try
            {
                if (boundary is "authority" or "request")
                {
                    held = (SemaphoreSlim)GetField(scope.Player, boundary == "authority" ? "_authoritySwitchGate" : "_playRequestGate")!;
                    await held.WaitAsync();
                }
                if (boundary == "watch") scope.Wire.HoldWatch = "boxset-film";
                if (boundary == "revalidation-return")
                {
                    scope.Client.InvalidateAccessContext(); scope.Wire.HoldBoxSet = true;
                }
                Invoke(play);
                if (boundary == "watch") await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                else if (boundary == "revalidation-return") await scope.Wire.BoundaryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                else await Until(() => !play.IsEnabled && GetField(scope.Player, boundary == "request" ? "_playRequestCts" : "_scopedViewerPlayCts") != null);
                scope.Frame.Navigate(typeof(Page));
                await Until(() => !page.IsLoaded);
                if (boundary == "revalidation-return")
                {
                    // Production box-set pages are uncached. A late Required
                    // assignment cannot enable WinUI caching outside a constructor.
                    // Keep the original read held until the real return route loads.
                    scope.Wire.HoldBoxSet = false; scope.Frame.GoBack();
                    try { await Until(() => scope.Frame.Content is BoxSetPage returned && returned.IsLoaded && returned.ViewModel.Detail != null && !returned.ViewModel.IsLoading); }
                    catch
                    {
                        Program.Log($"TRACE: return same={ReferenceEquals(scope.Frame.Content, page)} oldLoaded={page.IsLoaded} frame={scope.Frame.Content?.GetType().Name} oldDetail={page.ViewModel.Detail != null} oldLoading={page.ViewModel.IsLoading} oldFailure={page.ViewModel.Failure} reads={scope.Wire.BoxSetReads} oldPending={GetField(page, "_playPending")}.");
                        throw;
                    }
                    var returnedPage = (BoxSetPage)scope.Frame.Content;
                    Check(!ReferenceEquals(returnedPage, page) && returnedPage.NavigationCacheMode == Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled,
                        "The production uncached box-set return must load a new page.");
                    Check(!scope.Wire.ReleaseBoundary.Task.IsCompleted,
                        "The outgoing Play request must still be held while the new page returns.");
                    Check(!(bool)GetField(returnedPage, "_playPending")! && Descendants<Button>(returnedPage).Single(button => button.Name == "BoxSetPlay").IsEnabled,
                        "A retired visit's held Play must not keep the returned page's current Play disabled.");
                }
            }
            finally
            {
                held?.Release(); scope.Wire.ReleaseWatch.TrySetResult(); scope.Wire.ReleaseBoundary.TrySetResult(); scope.Wire.HoldBoxSet = false;
            }
            await Until(() => !(bool)GetField(page, "_playPending")!);
            await Task.Delay(80);
            Check(stack.Children.Count == 0 && scope.Wire.Starts.Count == 0,
                "Leaving during " + boundary + " must retire box-set Play without any error toast or playback start.");
            Program.Log("PASS: actual box-set departure during " + boundary + " is silent.");
        }
    }

    private static async Task VerifyBoxSetAbandonmentAsync(LifecycleScope scope)
    {
        foreach (var boundary in new[] { "authority-superseded", "request-superseded", "watch-superseded", "close", "room-authority", "room-request" })
        {
            await scope.ResetAsync(); scope.Window.ShowMainNavigation(); scope.Wire.AcceptStart = true;
            var navigation = scope.Services.GetRequiredService<NavigationService>();
            Check(navigation.NavigateImmediately(typeof(BoxSetPage), new BoxSetNavigationArgs("7", "73")), "Could not enter box-set feedback route.");
            await Until(() => scope.Frame.Content is BoxSetPage ready && ready.IsLoaded && ready.ViewModel.Detail != null);
            var page = (BoxSetPage)scope.Frame.Content;
            var play = Descendants<Button>(page).Single(button => button.Name == "BoxSetPlay");
            var toasts = Descendants<ToastContainer>((Microsoft.UI.Xaml.DependencyObject)scope.Window.Content).Single();
            ((StackPanel)toasts.FindName("ToastStack")).Children.Clear();
            var coordinator = scope.Services.GetRequiredService<WatchTogetherCoordinator>();
            SemaphoreSlim? held = null; Task? successor = null;
            try
            {
                if (boundary is "authority-superseded" or "close" or "room-authority")
                    held = (SemaphoreSlim)GetField(scope.Player, "_authoritySwitchGate")!;
                if (boundary is "request-superseded" or "room-request")
                    held = (SemaphoreSlim)GetField(scope.Player, "_playRequestGate")!;
                if (held != null) await held.WaitAsync();
                if (boundary == "watch-superseded") scope.Wire.HoldWatch = "boxset-film";
                Invoke(play);
                if (boundary == "watch-superseded") await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                else await Until(() => !play.IsEnabled && GetField(scope.Player,
                    boundary is "request-superseded" or "room-request" ? "_playRequestCts" : "_scopedViewerPlayCts") != null);
                if (boundary.EndsWith("superseded"))
                    successor = scope.Player.PlayAsync("newer-intent", expectedIdentityContext: scope.Client.CaptureIdentityContext());
                else if (boundary == "close") await scope.Player.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
                else coordinator.SetActiveRoom(new WatchTogetherRoomViewModel(scope.Playback, scope.Client)
                {
                    RoomId = "joined-room", RoomToken = "synthetic-proof",
                    Room = new() { RoomId = "joined-room", Phase = "lobby", SelectedContentId = "room-film" },
                });
            }
            finally
            {
                held?.Release(); scope.Wire.ReleaseWatch.TrySetResult();
                if (successor != null) await successor.WaitAsync(TimeSpan.FromSeconds(3));
            }
            await Until(() => play.IsEnabled && Descendants<TextBlock>(toasts).Any(text =>
                text.Text.Contains("Playback was interrupted") && text.Text.Contains("try again")));
            Check(scope.Wire.Starts.Count == (successor == null ? 0 : 1)
                && scope.Player.ContentId != "boxset-film", "Abandoned box-set primary action started its retired movie.");
            if (boundary.StartsWith("room"))
                Check(coordinator.ActiveRoom?.RoomId == "joined-room", "Abandoned standalone entry replaced the newly active room.");
            Program.Log("PASS: actual box-set Play reports an actionable toast after " + boundary + ".");
        }
    }
}
