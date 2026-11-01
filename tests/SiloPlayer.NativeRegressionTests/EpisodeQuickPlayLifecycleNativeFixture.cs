using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class EpisodeQuickPlayNativeFixture
{
    internal static async Task VerifyReviewAsync(StackPanel parent)
    {
        await using var scope = new LifecycleScope(); await scope.ShowAsync();
        var failures = new List<string>();
        foreach (var (name, run) in new (string, Func<Task>)[]
        {
            ("transport owner", () => VerifyTransportOwnerAsync(scope)),
            ("scoped watch freshness", () => VerifyScopedWatchFreshnessAsync(scope)),
            ("access refresh ownership", () => VerifyAccessRefreshAsync(scope)),
            ("superseded starts retain player state", () => VerifySupersededStateAsync(scope)),
            ("watch cancellation and resize/navigation", () => VerifyCancellationAsync(scope)),
            ("profile ownership across preparation awaits", () => VerifyPreparationOwnerAsync(scope)),
            ("newer viewer click", () => VerifyNewerIntentAsync(scope)),
            ("canceled queued successor", () => VerifyCanceledSuccessorAsync(scope)),
            ("loading overlay ownership", () => VerifyLoadingOverlayOwnershipAsync(scope)),
            ("attached room authority", () => VerifyRoomIntentAsync(scope)),
            ("rewatch accessible label", () => VerifyRewatchLabelAsync(parent)),
            ("hover/focus union", () => VerifyFocusUnionAsync(parent)),
        })
        {
            try { await run(); Program.Log("PASS: review regression " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Program.Log("REVIEW RED: " + name + ": " + ex); }
            finally { await scope.ResetAsync(); }
        }
        Check(failures.Count == 0, string.Join(" | ", failures));
    }

    private static async Task VerifyTransportOwnerAsync(LifecycleScope scope)
    {
        scope.Wire.AcceptStart = true;
        var manager = new PlaybackManager(scope.Playback, scope.Catalog, scope.Auth, scope.Client);
        manager.UseWatchDetail(new() { ContentId = "outgoing", Type = "episode", Versions = [new() { FileId = 42, Duration = 1200 }] });
        await manager.StartSessionAsync(42, 91);
        scope.Wire.AcceptStart = false;
        SetField(scope.Player, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(scope.Player, "outgoing");
        typeof(PlayerService).GetMethod("SetState", Private)!.Invoke(scope.Player, [PlayerState.Minimized]);
        var gate = (SemaphoreSlim)GetField(scope.Player, "_transportRestartGate")!;
        await gate.WaitAsync();
        var pending = scope.Player.PlayAsync("departed", expectedContext: scope.Client.CaptureContext());
        try
        {
            await Task.Delay(60);
            Check((bool)GetField(scope.Player, "_switchingContent")!, "Outgoing EOF is unguarded while the transport drain waits.");
            typeof(PlayerService).GetMethod("HandleMpvEndSignal", Private)!.Invoke(scope.Player, ["end-file"]);
            await Task.Delay(40);
            Check(scope.Player.State == PlayerState.Minimized && ReferenceEquals(scope.Player.Manager, manager) && scope.Wire.Stops == 0,
                "Outgoing EOF closed playback while the successor was draining transport.");
            scope.Auth.SelectProfile("replacement", profile: new() { Id = "replacement", Name = "Replacement fixture" });
        }
        finally { gate.Release(); }
        await pending;
        Check(scope.Wire.Watches.Count == 0 && scope.Wire.Stops == 0 && ReferenceEquals(scope.Player.Manager, manager) &&
            !(bool)GetField(scope.Player, "_switchingContent")!,
            "Releasing the held transport drain retired outgoing playback or prepared work after the initiating profile departed.");
        lock (scope.Wire.Requests) Check(scope.Wire.Requests.All(request => request.Profile != "replacement"), "Transport drain issued a request under the replacement profile.");
    }

    private static async Task VerifyScopedWatchFreshnessAsync(LifecycleScope scope)
    {
        var freshness = (WatchDetailFreshness<WatchDetailResponse>)GetField(scope.Player, "_watchDetailFreshness")!;
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        freshness.TrackFinalSave("same-episode", saved.Task);
        var replay = scope.Player.PlayAsync("same-episode", expectedContext: scope.Client.CaptureContext());
        try
        {
            await Task.Delay(80);
            Check(scope.Wire.Watches.Count == 0 && !replay.IsCompleted, "Scoped /watch bypassed the pending final-save barrier.");
            scope.Wire.ResumePosition = 137;
            saved.TrySetResult();
            await replay;
            Check(scope.Wire.Starts.Single().GetProperty("start_position").GetDouble() == 137,
                "Scoped replay did not use the final saved position.");
        }
        finally { saved.TrySetResult(); }

        await scope.ResetAsync();
        scope.Wire.HoldWatch = "mutated-episode";
        var pending = scope.Player.PlayAsync("mutated-episode", expectedContext: scope.Client.CaptureContext());
        await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        ((UserStateRevision)GetField(scope.Player, "_userStateRevision")!).Advance("mutated-episode");
        scope.Wire.ReleaseWatch.TrySetResult();
        await pending;
        Check(scope.Wire.Watches.Count == 2 && scope.Wire.Starts.Count == 1,
            "Scoped /watch consumed a response invalidated by a racing progress/watched mutation.");
    }

    private static async Task VerifyAccessRefreshAsync(LifecycleScope scope)
    {
        var page = scope.MountSeries("rendered-before-refresh"); await Task.Delay(80); page.UpdateLayout();
        var play = ResumeButton((Panel)page.FindName("EpisodesPanel"));
        scope.Client.InvalidateAccessContext();
        Invoke(play);
        await Until(() => scope.Wire.Starts.Count == 1 && play.IsEnabled);
        Check(scope.Wire.Watches.Count == 1, "An access refresh made a rendered Play button inert.");

        await scope.ResetAsync();
        scope.Wire.AcceptStart = true;
        page = scope.MountSeries("refresh-at-expanded"); await Task.Delay(80); page.UpdateLayout();
        play = ResumeButton((Panel)page.FindName("EpisodesPanel"));
        var refreshed = false;
        void OnStateChanged(PlayerState state)
        {
            if (state != PlayerState.Expanded) return;
            refreshed = true;
            scope.Client.InvalidateAccessContext();
        }
        scope.Player.StateChanged += OnStateChanged;
        try
        {
            Invoke(play);
            await Until(() => play.IsEnabled && scope.Wire.Starts.Count == 1);
            Check(refreshed && scope.Player.State == PlayerState.Expanded && scope.Player.Manager?.CurrentSession != null &&
                GetField(scope.Player, "_activeTransportPlan") != null,
                "An access-only refresh at Expanded silently abandoned the accepted scoped session.");
        }
        finally { scope.Player.StateChanged -= OnStateChanged; }

        await scope.ResetAsync();
        scope.Wire.AcceptStart = true;
        page = scope.MountSeries("refresh-at-queued-gate"); await Task.Delay(60); page.UpdateLayout();
        play = ResumeButton((Panel)page.FindName("EpisodesPanel"));
        var gate = (SemaphoreSlim)GetField(scope.Player, "_authoritySwitchGate")!;
        await gate.WaitAsync();
        try
        {
            Invoke(play);
            await Until(() => !play.IsEnabled);
            scope.Client.InvalidateAccessContext();
        }
        finally { gate.Release(); }
        await Until(() => play.IsEnabled);
        Check(scope.Wire.Starts.Count == 1 && scope.Player.State == PlayerState.Expanded && scope.Player.ErrorMessage == null,
            "An access refresh at the queued authority gate dropped accepted playback intent.");

        foreach (var boundary in new[] { "watch", "capability", "settings" })
        {
            foreach (var outgoing in new[] { false, true })
            {
                await scope.ResetAsync();
                scope.Wire.AcceptStart = true;
                if (outgoing)
                {
                    var manager = new PlaybackManager(scope.Playback, scope.Catalog, scope.Auth, scope.Client);
                    manager.UseWatchDetail(new() { ContentId = "outgoing", Type = "episode", Versions = [new() { FileId = 42, Duration = 1200 }] });
                    await manager.StartSessionAsync(42, 91);
                    SetField(scope.Player, "_playbackManager", manager);
                    typeof(PlayerService).GetProperty("ContentId")!.SetValue(scope.Player, "outgoing");
                    typeof(PlayerService).GetMethod("SetState", Private)!.Invoke(scope.Player, [PlayerState.Minimized]);
                    scope.Wire.Starts.Clear();
                }
                if (boundary == "watch") scope.Wire.HoldWatch = "refresh-during-preparation";
                else if (boundary == "capability") scope.Wire.HoldCapability = true;
                else scope.Wire.HoldSettings = true;
                page = scope.MountSeries("refresh-during-preparation"); await Task.Delay(60); page.UpdateLayout();
                play = ResumeButton((Panel)page.FindName("EpisodesPanel"));
                Invoke(play);
                try
                {
                    await (boundary == "watch" ? scope.Wire.WatchStarted.Task : scope.Wire.BoundaryStarted.Task).WaitAsync(TimeSpan.FromSeconds(3));
                    scope.Client.InvalidateAccessContext();
                }
                finally { scope.Wire.ReleaseWatch.TrySetResult(); scope.Wire.ReleaseBoundary.TrySetResult(); }
                await Until(() => play.IsEnabled);
                Check(scope.Wire.Starts.Count == 1 && scope.Player.State == PlayerState.Expanded &&
                    scope.Player.ErrorMessage == null && scope.Player.Manager?.CurrentSession != null &&
                    GetField(scope.Player, "_activeTransportPlan") != null,
                    $"An access-only refresh during {boundary} failed accepted playback (outgoing={outgoing}).");
                Check(scope.Wire.Watches.Count == (boundary == "watch" ? 2 : 1),
                    $"The retired {boundary} preparation did not retry its read with a fresh context.");
                Check(scope.Wire.Stops == (outgoing ? 1 : 0), "Access refresh stopped more than the outgoing session.");
            }
        }

        // Repeated refreshes must stop retrying with a useful message rather
        // than exposing internal context/owner exception strings.
        await scope.ResetAsync();
        scope.Wire.HoldWatch = "repeated-access-refresh";
        page = scope.MountSeries(scope.Wire.HoldWatch); await Task.Delay(60); page.UpdateLayout();
        play = ResumeButton((Panel)page.FindName("EpisodesPanel"));
        Invoke(play);
        try
        {
            await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var releaseFirst = scope.Wire.ReleaseWatch;
            scope.Wire.WatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Wire.ReleaseWatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Client.InvalidateAccessContext();
            releaseFirst.TrySetResult();
            await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            scope.Client.InvalidateAccessContext();
        }
        finally { scope.Wire.ReleaseWatch.TrySetResult(); }
        await Until(() => play.IsEnabled);
        Check(scope.Wire.Watches.Count == 2 && scope.Wire.Starts.Count == 0 && !scope.Player.IsLoading &&
            scope.Player.ErrorMessage == "Your access changed while preparing playback. Please try again.",
            "Repeated access refreshes leaked internal owner errors or retried without a bound.");
    }

    private static async Task VerifySupersededStateAsync(LifecycleScope scope)
    {
        foreach (var scoped in new[] { false, true })
        {
            await scope.ResetAsync();
            typeof(PlayerService).GetMethod("SetState", Private)!.Invoke(scope.Player, [PlayerState.Minimized]);
            var states = new List<PlayerState>();
            void OnStateChanged(PlayerState state) => states.Add(state);
            scope.Player.StateChanged += OnStateChanged;
            scope.Wire.HoldWatch = "superseded-a";
            var first = scope.Player.PlayAsync("superseded-a", expectedContext: scoped ? scope.Client.CaptureContext() : null);
            await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var releaseFirst = scope.Wire.ReleaseWatch;
            scope.Wire.HoldWatch = "successor-b";
            scope.Wire.WatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Wire.ReleaseWatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // Reserve the authority gate ahead of B, so A can finish cleanup
            // while B is accepted but cannot yet re-arm the EOF guard in core.
            var authorityGate = (SemaphoreSlim)GetField(scope.Player, "_authoritySwitchGate")!;
            var handoff = authorityGate.WaitAsync();
            var second = scope.Player.PlayAsync("successor-b", expectedContext: scoped ? scope.Client.CaptureContext() : null);
            var handoffReleased = false;
            try
            {
                await handoff.WaitAsync(TimeSpan.FromSeconds(3));
                await first;
                Check((bool)GetField(scope.Player, "_switchingContent")!,
                    "Superseded cleanup cleared the outgoing EOF guard before its successor entered core.");
                typeof(PlayerService).GetMethod("HandleMpvEndSignal", Private)!.Invoke(scope.Player, ["end-file"]);
                await Task.Delay(40);
                Check(!states.Contains(PlayerState.Idle) && scope.Player.State == PlayerState.Minimized,
                    "An outgoing EOF closed playback between superseded cleanup and successor core.");
                authorityGate.Release();
                handoffReleased = true;
                await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await first;
                Check(!states.Contains(PlayerState.Idle) && scope.Player.State == PlayerState.Minimized && scope.Player.ContentId == "successor-b",
                    "A superseded start emitted global Idle or cleared the successor's playback identity.");
            }
            finally
            {
                if (!handoffReleased) _ = handoff.ContinueWith(_ => authorityGate.Release(), TaskScheduler.Default);
                releaseFirst.TrySetResult(); scope.Wire.ReleaseWatch.TrySetResult();
                await Task.WhenAll(first, second);
                scope.Player.StateChanged -= OnStateChanged;
            }
        }
    }

    private static async Task VerifyCancellationAsync(LifecycleScope scope)
    {
        foreach (var departure in new[] { "resize", "navigation" })
        {
            scope.Wire.HoldWatch = "pending-" + departure;
            scope.Wire.WatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.Wire.ReleaseWatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var page = scope.MountSeries(scope.Wire.HoldWatch);
            await Task.Delay(80); page.UpdateLayout();
            var button = ResumeButton((Panel)page.FindName("EpisodesPanel"));
            Invoke(button); await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Until(() => scope.Player.IsLoading && scope.Overlay.Visibility == Visibility.Visible);
            if (departure == "resize")
            {
                page.Width = 520;
                scope.Window.AppWindow.ResizeClient(new(650, 900)); await Task.Delay(60);
                typeof(ItemDetailPage).GetMethod("BuildEpisodeRows", Private)!.Invoke(page, null);
                page.UpdateLayout();
                await Task.Delay(80);
                Check(!ReferenceEquals(button, ResumeButton((Panel)page.FindName("EpisodesPanel"))) &&
                    !button.IsEnabled && scope.Player.IsLoading && scope.Overlay.Visibility == Visibility.Visible && scope.Wire.Starts.Count == 0,
                    "A cosmetic grid rebuild canceled the accepted start instead of retaining its loading owner.");
                var startsBeforeRelease = scope.Wire.Starts.Count;
                scope.Wire.ReleaseWatch.TrySetResult();
                await Until(() => button.IsEnabled);
                Check(scope.Wire.Starts.Count == startsBeforeRelease + 1 && scope.Player.ContentId == "pending-resize",
                    "The accepted start did not reach session creation after the grid rebuild.");
            }
            else
            {
                scope.Frame.Navigate(typeof(Page));
                await Until(() => button.IsEnabled);
                await Task.Delay(80);
                Check(!scope.Player.IsLoading && !(bool)GetField(scope.Player, "_switchingContent")! && scope.Overlay.Visibility == Visibility.Collapsed,
                    "Navigation departure stranded loading/switching state or the actual shell overlay.");
            }
            scope.Wire.ReleaseWatch.TrySetResult(); scope.Wire.HoldWatch = null;
            await scope.Player.PlayAsync("successor-" + departure, expectedContext: scope.Client.CaptureContext());
            Check(scope.Wire.Starts.Count > 0 && !scope.Player.IsLoading, "Cancellation prevented a subsequent authorized playback request.");
            scope.Window.HideLoadingOverlay(GetField(scope.Window, "_loadingOverlayOwner")); await Task.Delay(40);
        }
    }

    private static async Task VerifyNewerIntentAsync(LifecycleScope scope)
    {
        scope.Wire.HoldWatch = "slow-a";
        var page = scope.MountSeries("slow-a", "new-b"); await Task.Delay(80); page.UpdateLayout();
        var buttons = Descendants<Button>((Panel)page.FindName("EpisodesPanel")).Where(button => AutomationProperties.GetName(button).StartsWith("Resume ")).ToArray();
        Invoke(buttons[0]); await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var releaseOld = scope.Wire.ReleaseWatch;
        scope.Wire.HoldWatch = "new-b";
        scope.Wire.WatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Wire.ReleaseWatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(buttons[1]); await Task.Delay(180);
        var newerEntered = scope.Wire.Watches.Any(uri => uri.AbsolutePath.EndsWith("/new-b"));
        var successorLoading = scope.Player.IsLoading && scope.Overlay.Visibility == Visibility.Visible;
        releaseOld.TrySetResult();
        scope.Wire.ReleaseWatch.TrySetResult();
        await Until(() => buttons.All(button => button.IsEnabled));
        await Task.Delay(40);
        Check(newerEntered && successorLoading && scope.Overlay.Visibility == Visibility.Visible && scope.Wire.Starts.Count == 1 && scope.Player.ContentId == "new-b",
            "The newer card click waited for stale playback startup or older cleanup hid its loading/error surface.");
    }

    private static async Task VerifyPreparationOwnerAsync(LifecycleScope scope)
    {
        foreach (var boundary in new[] { "watch", "capability", "settings", "start" })
        {
            await scope.ResetAsync();
            if (boundary == "watch") scope.Wire.HoldWatch = "pending-owner";
            else if (boundary == "capability") scope.Wire.HoldCapability = true;
            else if (boundary == "settings") scope.Wire.HoldSettings = true;
            else { scope.Wire.HoldStart = true; scope.Wire.AcceptStart = true; }
            var page = scope.MountSeries("pending-owner"); await Task.Delay(60); page.UpdateLayout();
            var play = ResumeButton((Panel)page.FindName("EpisodesPanel")); Invoke(play);
            await (boundary == "watch" ? scope.Wire.WatchStarted.Task : scope.Wire.BoundaryStarted.Task).WaitAsync(TimeSpan.FromSeconds(3));
            scope.Auth.SelectProfile("departed-profile", profile: new() { Id = "departed-profile", Name = "Replacement" });
            scope.Wire.ReleaseWatch.TrySetResult(); scope.Wire.ReleaseBoundary.TrySetResult();
            await Until(() => play.IsEnabled); await Task.Delay(60);
            Check(!scope.Player.IsLoading && !(bool)GetField(scope.Player, "_switchingContent")! && scope.Overlay.Visibility == Visibility.Collapsed &&
                GetField(scope.Player, "_activeTransportPlan") == null && scope.Player.State == PlayerState.Idle,
                $"Departed owner at {boundary} left loading/error/native startup state behind.");
            Check(scope.Wire.Watches.Count == 1 && scope.Wire.Starts.Count == (boundary == "start" ? 1 : 0),
                $"Departed owner at {boundary} issued replacement-profile playback preparation.");
            lock (scope.Wire.Requests) Check(scope.Wire.Requests.All(request => request.Profile != "departed-profile"), $"The {boundary} continuation issued requests for the replacement profile.");
            Program.Log("PASS: captured API owner survives the actual held " + boundary + " boundary.");
        }
    }

    private static async Task VerifyCanceledSuccessorAsync(LifecycleScope scope)
    {
        scope.Wire.HoldWatch = "abandoned-start";
        var page = scope.MountSeries("abandoned-start"); await Task.Delay(60); page.UpdateLayout();
        var play = ResumeButton((Panel)page.FindName("EpisodesPanel")); Invoke(play);
        await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var departed = new CancellationTokenSource(); departed.Cancel();
        try { await scope.Player.PlayAsync("canceled-successor", cancellationToken: departed.Token); }
        catch (OperationCanceledException) { }
        await Until(() => play.IsEnabled); await Task.Delay(60);
        Check(!scope.Player.IsLoading && scope.Overlay.Visibility == Visibility.Collapsed && scope.Wire.Starts.Count == 0,
            "A canceled queued successor prevented the retired request from hiding its own loading surface.");
        CheckAbandonedHandoff(scope);

        foreach (var departure in new[] { "cancellation", "profile" })
        {
            await scope.ResetAsync();
            scope.Wire.AcceptStart = true;
            var manager = new PlaybackManager(scope.Playback, scope.Catalog, scope.Auth, scope.Client);
            manager.UseWatchDetail(new() { ContentId = "outgoing", Type = "episode", Versions = [new() { FileId = 42, Duration = 1200 }] });
            await manager.StartSessionAsync(42, 91);
            scope.Wire.AcceptStart = false;
            scope.Wire.Starts.Clear();
            SetField(scope.Player, "_playbackManager", manager);
            SetField(scope.Player, "_activeTransportPlan", PlaybackTransportPlanner.Plan(manager.CurrentSession!));
            typeof(PlayerService).GetProperty("ContentId")!.SetValue(scope.Player, "outgoing");
            typeof(PlayerService).GetMethod("SetState", Private)!.Invoke(scope.Player, [PlayerState.Minimized]);

            scope.Wire.HoldWatch = "abandoned-scoped-start";
            var identity = scope.Client.CaptureIdentityContext();
            var first = scope.Player.PlayAsync(scope.Wire.HoldWatch, libraryId: 73, expectedIdentityContext: identity);
            await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var gate = (SemaphoreSlim)GetField(scope.Player, "_authoritySwitchGate")!;
            var handoff = gate.WaitAsync();
            using var successorLifetime = new CancellationTokenSource();
            var second = scope.Player.PlayAsync("abandoned-scoped-successor", cancellationToken: successorLifetime.Token,
                expectedIdentityContext: identity);
            var handoffReleased = false;
            try
            {
                await handoff.WaitAsync(TimeSpan.FromSeconds(3));
                await first;
                Check(scope.Player.State == PlayerState.Minimized && scope.Player.Manager == null &&
                    (bool)GetField(scope.Player, "_switchingContent")!,
                    "The scoped successor did not reserve a hand-off after retiring outgoing playback.");
                if (departure == "cancellation") successorLifetime.Cancel();
                else scope.Auth.SelectProfile("departed-successor", profile: new() { Id = "departed-successor", Name = "Replacement" });
                gate.Release();
                handoffReleased = true;
                await second;
                await Task.Delay(60);
                Check(scope.Wire.Stops == 1 && scope.Wire.Starts.Count == 0 && scope.Wire.Watches.Count == 1 &&
                    !scope.Player.IsLoading && scope.Overlay.Visibility == Visibility.Collapsed,
                    $"The abandoned scoped successor ({departure}) changed sessions or stranded its loading surface.");
                CheckAbandonedHandoff(scope);
            }
            finally
            {
                if (!handoffReleased) _ = handoff.ContinueWith(_ => gate.Release(), TaskScheduler.Default);
                successorLifetime.Cancel();
                scope.Wire.ReleaseWatch.TrySetResult();
                await Task.WhenAll(first, second);
            }
        }
    }

    private static async Task VerifyLoadingOverlayOwnershipAsync(LifecycleScope scope)
    {
        // Owner-less compatibility calls may release only their own surface.
        scope.Window.ShowLoadingOverlay();
        await Until(() => scope.Overlay.Visibility == Visibility.Visible);
        scope.Window.HideLoadingOverlay();
        await Until(() => scope.Overlay.Visibility == Visibility.Collapsed);
        var firstOwner = new object();
        var successorOwner = new object();
        scope.Window.ShowLoadingOverlay(firstOwner);
        scope.Window.HideLoadingOverlay();
        scope.Window.ShowLoadingOverlay();
        await Task.Delay(40);
        Check(scope.Overlay.Visibility == Visibility.Visible && ReferenceEquals(GetField(scope.Window, "_loadingOverlayOwner"), firstOwner),
            "An owner-less call hid or took ownership of a scoped loading surface.");
        scope.Window.ShowLoadingOverlay(successorOwner);
        scope.Window.HideLoadingOverlay(firstOwner);
        await Task.Delay(40);
        Check(scope.Overlay.Visibility == Visibility.Visible && ReferenceEquals(GetField(scope.Window, "_loadingOverlayOwner"), successorOwner),
            "Retired cleanup hid the successor's loading surface.");
        scope.Window.ShowPlaybackError("Successor error", "Keep this error visible.");
        scope.Window.HideLoadingOverlay(successorOwner);
        scope.Window.HideLoadingOverlay();
        scope.Window.ShowLoadingOverlay();
        await Task.Delay(40);
        var errorState = (FrameworkElement)((FrameworkElement)scope.Window.Content).FindName("ErrorState");
        Check(scope.Overlay.Visibility == Visibility.Visible && errorState.Visibility == Visibility.Visible,
            "A stale or owner-less loading call replaced the successor's error surface.");
        await scope.ResetAsync();

        // Capture the production native callback without leaving the synthetic
        // player subscribed to asynchronous native events in other cases.
        typeof(PlayerService).GetMethod("WireMpvEvents", Private)!.Invoke(scope.Player, null);
        var restarted = (Action)GetField(scope.Player, "_mpvPlaybackRestartedHandler")!;
        typeof(PlayerService).GetMethod("UnwireMpvEvents", Private)!.Invoke(scope.Player, null);
        foreach (var successorError in new[] { false, true })
        {
            await scope.ResetAsync();
            using var lifetime = new CancellationTokenSource();
            var outgoingOwner = new object();
            SetField(scope.Player, "_mpvLoadingOverlayOwner", outgoingOwner);
            scope.Window.ShowLoadingOverlay(outgoingOwner);
            scope.Wire.HoldWatch = "scoped-overlay-owner";
            var pending = scope.Player.PlayAsync(scope.Wire.HoldWatch, cancellationToken: lifetime.Token,
                expectedIdentityContext: scope.Client.CaptureIdentityContext());
            try
            {
                await scope.Wire.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Until(() => scope.Overlay.Visibility == Visibility.Visible);
                var requestOwner = GetField(scope.Player, "_playbackCts");
                restarted(); // The outgoing file produces its delayed first frame.
                scope.Window.ShowLoadingOverlay();
                await Task.Delay(40);
                Check(scope.Player.IsLoading && scope.Overlay.Visibility == Visibility.Visible &&
                    ReferenceEquals(GetField(scope.Window, "_loadingOverlayOwner"), requestOwner),
                    "Outgoing first output or an owner-less Show stole the pending quick play's loading surface.");
                if (successorError) scope.Window.ShowPlaybackError("Successor error", "Keep this error visible.");
                lifetime.Cancel();
                await pending;
                await Task.Delay(40);
                Check(!scope.Player.IsLoading && scope.Player.State == PlayerState.Idle &&
                    scope.Overlay.Visibility == (successorError ? Visibility.Visible : Visibility.Collapsed) &&
                    (!successorError || errorState.Visibility == Visibility.Visible),
                    "Canceled quick play stranded loading over Idle or hid a successor's error surface.");
            }
            finally
            {
                lifetime.Cancel();
                scope.Wire.ReleaseWatch.TrySetResult();
                await pending;
            }
        }

        await scope.ResetAsync();
        scope.Wire.AcceptStart = true;
        await scope.Player.PlayAsync("native-overlay-owner", expectedIdentityContext: scope.Client.CaptureIdentityContext());
        await Until(() => scope.Overlay.Visibility == Visibility.Visible);
        var nativeOwner = GetField(scope.Player, "_playbackCts");
        Check(nativeOwner != null && ReferenceEquals(GetField(scope.Player, "_mpvLoadingOverlayOwner"), nativeOwner) &&
            ReferenceEquals(GetField(scope.Window, "_loadingOverlayOwner"), nativeOwner),
            "BeginMpvLoad replaced the preparation surface's owner.");
        restarted();
        await Until(() => scope.Overlay.Visibility == Visibility.Collapsed);
        scope.Window.ShowLoadingOverlay(nativeOwner);
        await Until(() => scope.Overlay.Visibility == Visibility.Visible);
        typeof(PlayerService).GetMethod("EnterPlaybackTerminalState", Private)!.Invoke(scope.Player,
            [new InvalidOperationException("Fixture interruption"), 91d, "fixture", true]);
        await Until(() => scope.Overlay.Visibility == Visibility.Collapsed);
        scope.Window.ShowLoadingOverlay(nativeOwner);
        await Until(() => scope.Overlay.Visibility == Visibility.Visible);
        await scope.Player.CloseAsync();
        await Until(() => scope.Overlay.Visibility == Visibility.Collapsed);
    }

    private static void CheckAbandonedHandoff(LifecycleScope scope)
    {
        Check(scope.Player.State == PlayerState.Idle && scope.Player.ContentId == null && scope.Player.ActiveLibraryId == null &&
            scope.Player.Manager == null && !(bool)GetField(scope.Player, "_switchingContent")! &&
            GetField(scope.Player, "_activeTransportPlan") == null && !PlaybackConnectionService.For(scope.Playback).IsMediaActive,
            "An abandoned successor stranded the retired content identity, EOF guard, player state or media-active flag.");
    }

    private static async Task VerifyRoomIntentAsync(LifecycleScope scope)
    {
        scope.Wire.AcceptStart = true;
        var manager = new PlaybackManager(scope.Playback, scope.Catalog, scope.Auth, scope.Client);
        manager.UseWatchDetail(new() { ContentId = "room-a", Type = "episode", Versions = [new() { FileId = 42, Duration = 1200 }] });
        await manager.StartSessionAsync(42, 91);
        scope.Wire.AcceptStart = false;
        SetField(scope.Player, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(scope.Player, "room-a");
        using var room = new WatchTogetherRoomViewModel(scope.Playback, scope.Client)
        {
            RoomId = "fixture-room", RoomToken = "synthetic-room",
            Room = new() { RoomId = "fixture-room", Phase = "playing", PlaybackState = "paused", SelectedContentId = "room-a",
                SelfCanManageRoom = false, SelfCanControlTransport = false, SelectionRevision = 4 },
            ConnectionState = "connected",
        };
        var coordinator = scope.Services.GetRequiredService<WatchTogetherCoordinator>(); coordinator.SetActiveRoom(room);
        Check(room.AttachedSessionId == manager.SessionId && scope.Player.IsWatchTogetherPlayback, "Fixture did not obtain a real coordinator attachment.");
        var starts = scope.Wire.Starts.Count;
        var navigation = scope.Services.GetRequiredService<NavigationService>(); var previousFrame = navigation.Frame;
        // Exercise the actual card/API/controller boundary without opening a
        // room socket; room-page navigation itself is covered by existing fixtures.
        navigation.Frame = null;
        async Task Click(string id)
        {
            var page = scope.MountSeries(id); await Task.Delay(60); page.UpdateLayout();
            var button = ResumeButton((Panel)page.FindName("EpisodesPanel")); Invoke(button); await Until(() => button.IsEnabled);
        }
        await Click("room-a");
        Check(room.NoticeMessage?.Contains("Only the host", StringComparison.Ordinal) == true && scope.Player.IsPaused,
            "Guest Play on the attached title bypassed the room controller's permission/pause boundary.");
        await Click("room-b");
        Check(scope.Wire.Starts.Count == starts && scope.Wire.Stops == 0 && scope.Player.ContentId == "room-a" &&
            room.AttachedSessionId == manager.SessionId && ReferenceEquals(coordinator.ActiveRoom, room),
            "An unrelated card locally replaced a real attached room session.");
        Check(scope.Wire.Suggestions == 1, "Guest episode intent did not enter the existing server-authorized suggestion path.");
        room.Room = new() { RoomId = "fixture-room", Phase = "playing", SelectedContentId = "room-a", SelectionRevision = 4, SelfCanManageRoom = true, SelfCanControlTransport = true };
        await Click("room-b");
        Check(scope.Wire.Suggestions == 2 && scope.Wire.Starts.Count == starts && scope.Wire.Stops == 0,
            "Host playing-room intent bypassed the existing detail suggestion path.");
        room.Room = new() { RoomId = "fixture-room", Phase = "lobby", SelectedContentId = "room-a", SelectionRevision = 4, SelfCanManageRoom = true };
        await Click("room-b");
        Check(scope.Wire.Stages.Count == 1 && scope.Wire.Stages[0].GetProperty("library_id").GetString() == "73" && room.Room.SelectedContentId == "room-b",
            "Host lobby intent did not stage the captured library through the existing authorized room API.");
        room.Room = new() { RoomId = "fixture-room", Phase = "lobby", SelectedContentId = "room-b", SelectionRevision = 4, SelectionMode = "vote", SelfCanManageRoom = true };
        await Click("room-b");
        Check(scope.Wire.Stages.Count == 1 && scope.Wire.Suggestions == 3, "Vote mode bypassed server suggestions through host staging.");

        room.Room = new() { RoomId = "fixture-room", Phase = "lobby", SelectedContentId = "lobby-original", SelectionRevision = 4, SelfCanManageRoom = true };
        // The authoritative playing->lobby snapshot legitimately stops A.
        // The pending card below must not cause another stop or local start.
        await scope.Player.CloseAsync();
        var stopsBeforeCanceled = scope.Wire.Stops;
        var original = room.Room;
        scope.Wire.HoldStage = true;
        var pendingPage = scope.MountSeries("cancelled-room-c"); await Task.Delay(60); pendingPage.UpdateLayout();
        var pending = ResumeButton((Panel)pendingPage.FindName("EpisodesPanel")); Invoke(pending);
        await scope.Wire.BoundaryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        scope.Frame.Navigate(typeof(Page)); await Until(() => pending.IsEnabled);
        scope.Wire.ReleaseBoundary.TrySetResult();
        Check(ReferenceEquals(room.Room, original) && scope.Wire.Starts.Count == starts && scope.Wire.Stops == stopsBeforeCanceled,
            "A departed room-card callback applied staged state or changed local playback.");
        navigation.Frame = previousFrame;
        coordinator.ClearActiveRoom();
    }

    private static async Task VerifyRewatchLabelAsync(StackPanel parent)
    {
        using var scope = new Fixture();
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        page.ViewModel.Item = new() { ContentId = "series", Type = "series" };
        parent.Children.Add(page);
        try
        {
            foreach (var duration in new[] { 0d, 1200d })
            {
                var card = (FrameworkElement)typeof(ItemDetailPage).GetMethod("CreateEpisodeCard", Private)!.Invoke(page,
                    [new Episode { ContentId = "rewatch", Title = "Rewatch", Files = [new() { FileId = 42 }],
                        UserData = new() { Played = true, PositionSeconds = 91, DurationSeconds = duration } }, false])!;
                parent.Children.Add(card); await Task.Delay(40);
                Check(Descendants<Button>(card).Any(button => AutomationProperties.GetName(button).StartsWith("Resume ")),
                    "Positive rewatch progress has a Play label despite the normal resume request.");
                parent.Children.Remove(card);
            }
        }
        finally { parent.Children.Remove(page); }
    }

    private static async Task VerifyFocusUnionAsync(StackPanel parent)
    {
        using var scope = new Fixture();
        var page = new ItemDetailPage { Width = 900, Height = 720 };
        page.ViewModel.Item = new() { ContentId = "series", Type = "series" };
        var card = (Grid)typeof(ItemDetailPage).GetMethod("CreateEpisodeCard", Private)!.Invoke(page,
            [new Episode { ContentId = "focus", Title = "Focus", Files = [new() { FileId = 42 }] }, false])!;
        card.Width = 220;
        var outside = new Button { Content = "Outside fixture action" };
        parent.Children.Add(page); parent.Children.Add(card); parent.Children.Add(outside);
        try
        {
            await Task.Delay(60); card.UpdateLayout();
            var layer = card.Children.OfType<Grid>().Single();
            var exit = layer.GetType().GetMethod("OnOwnerPointerExited", Private);
            var enter = layer.GetType().GetMethod("OnOwnerPointerEntered", Private);
            Check(exit != null && enter != null, "The episode action layer lacks the combined pointer/focus visibility owner.");
            var play = Descendants<Button>(layer).Single(button => AutomationProperties.GetName(button).StartsWith("Play "));
            play.Focus(FocusState.Keyboard); await Task.Delay(30);
            exit!.Invoke(layer, [card, null]);
            Check(layer.Opacity == 1 && layer.IsHitTestVisible, "Pointer exit hid the actual keyboard-focused Play action.");
            enter!.Invoke(layer, [card, null]); outside.Focus(FocusState.Keyboard); await Task.Delay(30);
            Check(layer.Opacity == 1 && layer.IsHitTestVisible, "Focus departure hid actual actions while the pointer remained within the card.");
            exit.Invoke(layer, [card, null]); await Task.Delay(30);
            Check(layer.Opacity == 0 && !layer.IsHitTestVisible, "Actions did not hide after both hover and focus departed.");
        }
        finally { parent.Children.Remove(page); parent.Children.Remove(card); parent.Children.Remove(outside); }
    }

    private static Button ResumeButton(DependencyObject host) => Descendants<Button>(host).First(button => AutomationProperties.GetName(button).StartsWith("Resume "));
    private static object? GetField(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
    private static void SetField(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);

    private sealed class LifecycleScope : IAsyncDisposable
    {
        private readonly FieldInfo _slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previousServices;
        private readonly object? _previousWindow;
        internal Wire Wire { get; } = new();
        internal SiloApiClient Client { get; }
        internal AuthService Auth { get; }
        internal PlaybackApi Playback { get; }
        internal CatalogApi Catalog { get; }
        internal ServiceProvider Services { get; }
        internal PlayerService Player { get; }
        internal MainWindow Window { get; }
        internal Frame Frame => (Frame)((FrameworkElement)Window.Content).FindName("ContentFrame");
        internal FrameworkElement Overlay => (FrameworkElement)((FrameworkElement)Window.Content).FindName("LoadingOverlay");
        internal LifecycleScope()
        {
            _previousServices = _slot.GetValue(null); _previousWindow = typeof(App).GetProperty(nameof(App.MainWindowInstance))!.GetValue(null);
            var http = new HttpClient(Wire); Client = new(http); Client.SetBaseUrl("https://quickplay.fixture.invalid");
            Auth = new(Client, new AuthApi(Client), credentialStore: null); Auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            Auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
            Playback = new(Client); Catalog = new(Client);
            var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "lifecycle-" + Guid.NewGuid().ToString("N")));
            var preferences = settings.Load(); preferences.SiloCastEnabled = false; settings.Save(preferences);
            Player = new(Playback, Catalog, Auth, Client, settings, new SettingsApi(Client));
            Services = new ServiceCollection().AddSingleton(Client).AddSingleton(Auth).AddSingleton(http).AddSingleton(settings)
                .AddSingleton(Playback).AddSingleton(Catalog).AddSingleton(Player)
                .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<RequestsApi>().AddSingleton<MediaMaintenanceApi>()
                .AddSingleton<NotificationsApi>().AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>()
                .AddSingleton<CollectionsApi>().AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
                .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<CardOverlayService>()
                .AddSingleton<UICustomizationService>().AddSingleton<ToastService>().AddSingleton<EpisodeSpoilerPreferences>()
                .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>().AddSingleton<WatchTogetherCoordinator>()
                .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "lifecycle-images-" + Guid.NewGuid().ToString("N"))))
                .AddSingleton<ItemDetailPrefetchCache>().AddTransient<ItemDetailViewModel>().AddTransient<WatchTogetherRoomViewModel>().BuildServiceProvider();
            _slot.SetValue(null, Services); Window = new(); typeof(App).GetProperty(nameof(App.MainWindowInstance))!.SetValue(null, Window);
            var nav = (NavigationView)((FrameworkElement)Window.Content).FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", Private)!.CreateDelegate(typeof(RoutedEventHandler), Window);
        }
        internal async Task ShowAsync()
        {
            Window.AppWindow.Move(new(-20000, -20000)); Window.AppWindow.ResizeClient(new(1100, 900)); Window.AppWindow.Show(false);
            await Task.Delay(80);
            await Services.GetRequiredService<EpisodeSpoilerPreferences>().EnsureLoadedAsync();
            await Services.GetRequiredService<UICustomizationService>().EnsureLoadedAsync();
            EnsureNativeResolver(); var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); SetField(Player, "_mpv", mpv);
        }
        internal ItemDetailPage MountSeries(params string[] episodes)
        {
            var page = new ItemDetailPage { Width = 900, Height = 720 };
            page.ViewModel.Item = new() { ContentId = "series", Type = "series" };
            typeof(ItemDetailViewModel).GetProperty("LibraryId")!.SetValue(page.ViewModel, 73);
            SetField(page, "_currentContentId", "series"); SetField(page, "_navigationCts", new CancellationTokenSource());
            foreach (var id in episodes) page.ViewModel.Episodes.Add(new() { ContentId = id, Title = id,
                Files = [new() { FileId = 42 }], UserData = new() { PositionSeconds = 91, DurationSeconds = 1200 } });
            Frame.Content = page;
            ((FrameworkElement)page.FindName("ContentScroll")).Visibility = Visibility.Visible;
            typeof(ItemDetailPage).GetMethod("BuildEpisodeRows", Private)!.Invoke(page, null);
            return page;
        }
        internal async Task ResetAsync()
        {
            Wire.ReleaseWatch.TrySetResult(); Wire.ReleaseBoundary.TrySetResult();
            Frame.Navigate(typeof(Page)); Services.GetRequiredService<WatchTogetherCoordinator>().ClearActiveRoom();
            Services.GetRequiredService<NavigationService>().Frame = Frame;
            await Player.CloseAsync();
            Wire.HoldWatch = null; Wire.HoldCapability = Wire.HoldSettings = Wire.HoldStart = Wire.HoldStage = Wire.AcceptStart = false;
            Wire.ResumePosition = 91;
            Wire.WatchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); Wire.ReleaseWatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Wire.BoundaryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); Wire.ReleaseBoundary = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Wire.Watches.Clear(); Wire.Starts.Clear(); Wire.Stages.Clear(); Wire.Requests.Clear(); Wire.Stops = Wire.Suggestions = 0;
            Auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
            await Services.GetRequiredService<EpisodeSpoilerPreferences>().EnsureLoadedAsync();
            await Services.GetRequiredService<UICustomizationService>().EnsureLoadedAsync();
            Window.AppWindow.ResizeClient(new(1100, 900)); Window.HideLoadingOverlay(GetField(Window, "_loadingOverlayOwner")); await Task.Delay(30);
        }
        public async ValueTask DisposeAsync()
        {
            Wire.ReleaseWatch.TrySetResult(); Wire.ReleaseBoundary.TrySetResult(); Frame.Navigate(typeof(Page));
            Services.GetRequiredService<WatchTogetherCoordinator>().ClearActiveRoom();
            await Player.CloseAsync();
            await Services.GetRequiredService<SiloPlayer.Services.SiloCast.SiloCastReceiverService>().ShutdownAsync();
            // Drain queued shell/player paint while the native window and its
            // provider are still alive, as the existing startup shell fixture does.
            await Task.Delay(100);
            SetField(Window, "_receiverShutdownComplete", true); Window.Close(); await Task.Delay(100);
            await Services.DisposeAsync();
            typeof(App).GetProperty(nameof(App.MainWindowInstance))!.SetValue(null, _previousWindow); _slot.SetValue(null, _previousServices);
        }
    }
    private static bool _nativeResolverSet;
    private static void EnsureNativeResolver()
    {
        if (_nativeResolverSet) return;
        _nativeResolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(MpvPlayer).Assembly, (name, _, _) => name == "libmpv-2.dll"
            ? NativeLibrary.Load(Path.Combine(Program.AppDirectory, name)) : IntPtr.Zero);
    }
}
