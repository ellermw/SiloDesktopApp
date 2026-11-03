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
        foreach (var catchUp in new[] { false, true })
        foreach (var boundary in new[] { "watch", "start" })
        foreach (var initialState in new[] { PlayerState.Idle, PlayerState.Minimized })
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
                var published = typeof(EventChannelClient).GetMethod("PublishAccessInvalidation", Private)!
                    .Invoke(events, [scope.Client.CaptureContext(), null, catchUp]);
                Check(published is true, "Synthetic access/catch-up event was not published.");
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
                "Access/catch-up refresh silently abandoned the accepted movie start.");
            Program.Log($"PASS: box-set shell {(catchUp ? "catch-up" : "access")} during held {boundary} from {initialState} preserves visit and starts the movie.");
        }
        await scope.ResetAsync();
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
