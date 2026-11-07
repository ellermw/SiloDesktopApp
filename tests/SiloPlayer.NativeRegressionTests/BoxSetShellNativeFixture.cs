using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyBoxSetShellRefreshAsync()
    {
        await using var scope = new LifecycleScope(); await scope.ShowAsync();
        var navigation = scope.Services.GetRequiredService<NavigationService>();
        var events = scope.Services.GetRequiredService<EventChannelClient>();
        // Keep shell decoration synthetic and avoid starting a real event socket/prewarm.
        SetField(events, "_reconnectSuppressed", true);
        SetField(scope.Window, "_playerPrewarmed", true);
        SetField(scope.Window, "_hydratedShellKey", typeof(MainWindow).GetMethod("GetAuthenticatedShellKey", Private)!.Invoke(scope.Window, null));
        var nav = (NavigationView)((FrameworkElement)scope.Window.Content).FindName("NavView");
        nav.IsPaneVisible = true;
        foreach (var invalidation in new[] { "access", "catch-up", "language" })
        foreach (var boundary in new[] { "watch", "start" })
        foreach (var initialState in invalidation == "language" ? new[] { PlayerState.Idle } : new[] { PlayerState.Idle, PlayerState.Minimized })
        {
            await scope.ResetAsync();
            scope.Player.SetState(initialState);
            Check(navigation.Navigate<BoxSetPage>(new BoxSetNavigationArgs("7", "73")), "Box-set shell route was rejected.");
            await Until(() => scope.Frame.Content is BoxSetPage { IsLoaded: true, ViewModel.Detail: not null, ViewModel.IsLoading: false });
            var page = (BoxSetPage)scope.Frame.Content;
            var play = Descendants<Button>(page).Single(x => x.Name == "BoxSetPlay");
            var members = Descendants<PosterCard>(page).ToArray();
            var lifetime = ((CancellationTokenSource)GetField(page.ViewModel, "_playLifetime")!).Token;
            var backCount = scope.Frame.BackStack.Count;
            var reads = scope.Wire.BoxSetReads;
            scope.Wire.AcceptStart = true;
            scope.Wire.HoldWatch = boundary == "watch" ? "boxset-film" : null;
            scope.Wire.HoldStart = boundary == "start";
            Invoke(play);
            try
            {
                await (boundary == "watch" ? scope.Wire.WatchStarted.Task : scope.Wire.BoundaryStarted.Task).WaitAsync(TimeSpan.FromSeconds(3));
                Check(scope.Player.State is PlayerState.Idle or PlayerState.Minimized, "Fixture missed the pre-Expanded refresh window.");
                Check(members[0].Focus(FocusState.Keyboard), "Member focus could not be established before shell refresh.");
                var scroll = Descendants<ScrollViewer>(page).First();
                var offset = scroll.VerticalOffset;
                // Exercise the event publisher and MainWindow subscribers, not direct catalog invalidation.
                if (invalidation == "language")
                {
                    typeof(MainWindow).GetMethod("EnsureNotificationSubscription", Private)!
                        .Invoke(scope.Window, [GetField(scope.Window, "_hydratedShellKey")]);
                    var generation = scope.Client.LocalizedReads.Generation;
                    using var socket = new ClientWebSocket();
                    await (Task)typeof(EventChannelClient).GetMethod("DispatchFrame", Private)!.Invoke(events,
                        [socket, """{"type":"event","channel":"user_settings","event":"user_settings.changed","data":{"profile_id":"fixture","key":"catalog.metadata_language"}}""", CancellationToken.None])!;
                    Check(scope.Client.LocalizedReads.Generation == generation + 1, "Shell did not publish the realtime language invalidation.");
                    // The page's read can finish before the shell's 100 ms debounce.
                    // Keep playback held until that shell refresh has actually completed.
                    await Until(() => GetField(scope.Window, "_localizedRefreshCts") != null);
                    await Until(() => GetField(scope.Window, "_localizedRefreshCts") == null);
                    Check(!(bool)GetField(scope.Window, "_localizedRefreshPending")! && scope.Player.State == PlayerState.Idle,
                        "Localized shell refresh did not complete during the held Idle start.");
                }
                else
                {
                    var published = typeof(EventChannelClient).GetMethod("PublishAccessInvalidation", Private)!
                        .Invoke(events, [scope.Client.CaptureContext(), null, invalidation == "catch-up"]);
                    Check(published is true, "Synthetic access/catch-up event was not published.");
                }
                await Until(() => scope.Wire.BoxSetReads > reads && !page.ViewModel.IsLoading);
                Check(ReferenceEquals(scope.Frame.Content, page) && scope.Frame.BackStack.Count == backCount
                    && ReferenceEquals(play, Descendants<Button>(page).Single(x => x.Name == "BoxSetPlay"))
                    && Descendants<PosterCard>(page).SequenceEqual(members)
                    && members[0].FocusState == FocusState.Keyboard && scroll.VerticalOffset == offset
                    && !lifetime.IsCancellationRequested, "Shell refresh replaced the box-set visit, cards, focus or accepted start.");
            }
            finally { scope.Wire.ReleaseWatch.TrySetResult(); scope.Wire.ReleaseBoundary.TrySetResult(); }
            await Until(() => !scope.Player.IsLoading && scope.Player.State == PlayerState.Expanded);
            Check(scope.Player.Manager?.CurrentSession != null && scope.Player.ContentId == "boxset-film"
                && scope.Wire.Starts.Count == 1 && !lifetime.IsCancellationRequested,
                "Shell refresh silently abandoned the accepted movie start.");
            Program.Log($"PASS: box-set shell {invalidation} during held {boundary} from {initialState} preserves visit and starts the movie.");
        }
        await VerifyMovieMembershipAccessRefreshAsync(scope);
        await VerifyBoxSetPendingSaveActivationAsync(scope);
        await scope.ResetAsync();
    }

    private static async Task VerifyMovieMembershipAccessRefreshAsync(LifecycleScope scope)
    {
        await scope.ResetAsync();
        var navigation = scope.Services.GetRequiredService<NavigationService>();
        Check(navigation.Navigate<ItemDetailPage>(new ItemDetailNavigationArgs("boxset-film", 73)), "Movie route was rejected.");
        await Until(() => scope.Frame.Content is ItemDetailPage { IsLoaded: true, ViewModel.BoxSet: not null } arriving
            && Descendants<PosterCard>((DependencyObject)arriving.FindName("BoxSetMovies")).Any());
        var page = (ItemDetailPage)scope.Frame.Content;
        var row = (DependencyObject)page.FindName("BoxSetMovies");
        var cards = Descendants<PosterCard>(row).ToArray();
        var visible = page.ViewModel.BoxSet;
        Check(cards[0].Focus(FocusState.Keyboard), "Movie membership card could not receive keyboard focus.");
        scope.Wire.HoldMembership = true;
        scope.Wire.MembershipStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Wire.ReleaseMembership = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            scope.Client.InvalidateAccessContext(); scope.Catalog.InvalidateLibraryCache();
            await scope.Wire.MembershipStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // Let queued row renders run while the replacement response is held.
            await Task.Delay(60);
            Check(ReferenceEquals(page.ViewModel.BoxSet, visible)
                && Descendants<PosterCard>(row).SequenceEqual(cards) && cards[0].FocusState == FocusState.Keyboard
                && ((FrameworkElement)page.FindName("BoxSetSection")).Visibility == Visibility.Visible,
                "An access revision unmounted the movie membership row or lost keyboard focus.");
            scope.Wire.BoxSetPlayed = true; scope.Wire.HoldMembership = false; scope.Wire.ReleaseMembership.TrySetResult();
            await Until(() => page.ViewModel.BoxSet?.PlayedCount == 1 && cards[0].MediaItem?.UserState?.Played == true);
            Check(Descendants<PosterCard>(row).SequenceEqual(cards) && cards[0].FocusState == FocusState.Keyboard,
                "The replacement membership read rebuilt the movie row instead of updating its cards.");
            Program.Log("PASS: same-viewer access revision preserves the mounted movie membership row and keyboard focus during a held read.");
        }
        finally { scope.Wire.HoldMembership = false; scope.Wire.ReleaseMembership.TrySetResult(); scope.Wire.BoxSetPlayed = false; }
    }

    private static async Task VerifyBoxSetPendingSaveActivationAsync(LifecycleScope scope)
    {
        var navigation = scope.Services.GetRequiredService<NavigationService>();
        var events = scope.Services.GetRequiredService<EventChannelClient>();
        foreach (var destination in new[] { "detail", "view-all" })
        {
            await scope.ResetAsync(); scope.Wire.BoxSetPlayed = false;
            Check(navigation.Navigate<ItemDetailPage>(new ItemDetailNavigationArgs("boxset-film", 73)), "Movie route was rejected.");
            await Until(() => scope.Frame.Content is ItemDetailPage { IsLoaded: true, ViewModel.BoxSet: not null } arriving
                && Descendants<PosterCard>((DependencyObject)arriving.FindName("BoxSetMovies")).Any());
            var original = (ItemDetailPage)scope.Frame.Content;
            // Attach a real synthetic movie session without loading remote media.
            scope.Wire.AcceptStart = true;
            var manager = new PlaybackManager(scope.Playback, scope.Catalog, scope.Auth, scope.Client);
            manager.UseWatchDetail(new() { ContentId = "boxset-film", Type = "movie", Versions = [new() { FileId = 42, Duration = 1200 }] });
            await manager.StartSessionAsync(42, 1190);
            SetField(scope.Player, "_playbackManager", manager);
            typeof(PlayerService).GetProperty("ContentId")!.SetValue(scope.Player, "boxset-film");
            typeof(PlayerService).GetProperty("Position")!.SetValue(scope.Player, 1190d);
            typeof(PlayerService).GetProperty("Duration")!.SetValue(scope.Player, 1200d);
            scope.Player.SetState(PlayerState.Expanded);
            var published = typeof(EventChannelClient).GetMethod("PublishAccessInvalidation", Private)!
                .Invoke(events, [scope.Client.CaptureContext(), null, true]);
            Check(published is true, "Catch-up event was not published.");
            await Until(() => (bool)GetField(scope.Window, "_pendingAccessRefresh")!);
            scope.Wire.HoldStop = true;
            scope.Wire.StopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Wire.ReleaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await scope.Player.CloseAsync();
                await scope.Wire.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(!scope.Player.PendingProgressSave.IsCompleted, "Fixture did not hold the final stop write.");
                await Until(() => scope.Frame.Content is ItemDetailPage { IsLoaded: true, ViewModel.BoxSet: not null } replacement
                    && !ReferenceEquals(replacement, original)
                    && Descendants<PosterCard>((DependencyObject)replacement.FindName("BoxSetMovies")).Any());
                var page = (ItemDetailPage)scope.Frame.Content;
                Check(page.ViewModel.BoxSet!.PlayedCount == 0, "Replacement detail did not read pre-save state.");
                if (destination == "view-all")
                {
                    var viewAll = Descendants<Button>((DependencyObject)page.FindName("BoxSetSection")).Single(x => x.Content as string == "View all");
                    Invoke(viewAll);
                    await Until(() => scope.Frame.Content is BoxSetPage { IsLoaded: true, ViewModel.Detail: not null, ViewModel.IsLoading: false });
                    var setPage = (BoxSetPage)scope.Frame.Content;
                    Check(setPage.ViewModel.Detail!.PlayedCount == 0, "View all did not first read pre-save state.");
                    var reads = scope.Wire.BoxSetReads;
                    scope.Wire.ReleaseStop.TrySetResult(); await scope.Player.PendingProgressSave;
                    await Until(() => scope.Wire.BoxSetReads > reads && setPage.ViewModel.Detail?.PlayedCount == 1
                        && Descendants<PosterCard>(setPage).All(x => x.MediaItem?.UserState?.Played == true));
                    Check(Descendants<TextBlock>(setPage).Any(x => x.Text.Contains("1 / 1 watched")), "View all retained pre-save counts.");
                }
                else
                {
                    var reads = scope.Wire.MembershipReads;
                    scope.Wire.ReleaseStop.TrySetResult(); await scope.Player.PendingProgressSave;
                    await Until(() => scope.Wire.MembershipReads > reads && page.ViewModel.BoxSet?.PlayedCount == 1
                        && Descendants<PosterCard>((DependencyObject)page.FindName("BoxSetMovies")).All(x => x.MediaItem?.UserState?.Played == true));
                }
                Program.Log($"PASS: pending catch-up at Idle replaces movie detail; {destination} first activation re-reads membership after the held stop write.");
            }
            finally
            {
                scope.Wire.ReleaseStop.TrySetResult(); await scope.Player.PendingProgressSave;
                scope.Wire.HoldStop = false; scope.Wire.BoxSetPlayed = false;
            }
        }
    }

    internal static async Task VerifyBoxSetPointerAsync()
    {
        await using var scope = new LifecycleScope(); await scope.ShowAsync();
        var navigation = scope.Services.GetRequiredService<NavigationService>();
        var card = new PosterCard
        {
            Width = 170, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            MediaItem = new() { Type = "box_set", ContentId = "box_set:7", BoxSet = new() { Id = "7", LibraryId = "73", Title = "Pointer collection", MovieCount = 1 } },
        };
        scope.Frame.Content = new Page { Content = card };
        var navigations = 0; var bubbledTaps = 0;
        navigation.Navigated += (_, e) => { if (e.SourcePageType == typeof(BoxSetPage)) navigations++; };
        card.Tapped += (_, _) => bubbledTaps++;
        // Unlike InvokeProvider, real mouse input exercises Button.Click plus routed Tapped.
        BoxSetGetCursorPos(out var originalCursor);
        try
        {
            scope.Window.AppWindow.Move(new(40, 40)); scope.Window.AppWindow.ResizeClient(new(900, 700)); scope.Window.Activate();
            await Task.Delay(150); ((FrameworkElement)scope.Window.Content).UpdateLayout();
            var aggregate = (BoxSetCard)card.Content;
            Check(aggregate.ActualWidth > 0 && aggregate.ActualHeight > 0, "Pointer aggregate did not realize.");
            var root = (FrameworkElement)scope.Window.Content;
            var point = aggregate.TransformToVisual(root).TransformPoint(new(aggregate.ActualWidth / 2, aggregate.ActualHeight / 2));
            var screen = new BoxSetPoint { X = (int)(point.X * root.XamlRoot.RasterizationScale), Y = (int)(point.Y * root.XamlRoot.RasterizationScale) };
            Check(BoxSetClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(scope.Window), ref screen)
                && BoxSetSetCursorPos(screen.X, screen.Y), "Could not target the owned aggregate window.");
            var reads = scope.Wire.BoxSetReads; var backCount = scope.Frame.BackStack.Count;
            BoxSetMouseEvent(0x0002, 0, 0, 0, UIntPtr.Zero); // left down
            await Task.Delay(40);
            BoxSetMouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero); // left up
            await Until(() => scope.Frame.Content is BoxSetPage { IsLoaded: true, ViewModel.Detail: not null, ViewModel.IsLoading: false });
            await Task.Delay(100);
            Check(navigations == 1 && scope.Wire.BoxSetReads == reads + 1 && scope.Frame.BackStack.Count == backCount + 1
                && bubbledTaps == 0, "One aggregate pointer tap navigated/loaded twice or escaped to PosterCard.");
            Program.Log("PASS: actual aggregate mouse tap is consumed by BoxSetCard and opens/loads exactly one box-set page.");
        }
        finally
        {
            BoxSetMouseEvent(0x0004, 0, 0, 0, UIntPtr.Zero);
            BoxSetSetCursorPos(originalCursor.X, originalCursor.Y);
            scope.Window.AppWindow.Move(new(-20000, -20000));
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BoxSetPoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool BoxSetGetCursorPos(out BoxSetPoint point);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")] private static extern bool BoxSetClientToScreen(IntPtr window, ref BoxSetPoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool BoxSetSetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void BoxSetMouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
