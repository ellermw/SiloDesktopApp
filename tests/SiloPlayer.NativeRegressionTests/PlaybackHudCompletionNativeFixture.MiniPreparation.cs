using Microsoft.UI.Xaml;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Services;

internal static partial class PlaybackHudCompletionNativeFixture
{
    private sealed class PreparationGate
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static async Task VerifyMiniPreparation(PlayerService service, Wire wire, SiloApiClient api,
        MainWindow window, string mode)
    {
        if (mode is "mini-prepare-expanded" or "mini-prepare-fullscreen" or "mini-prepare-pip")
        {
            await VerifyReplacementSurfacePreparation(service, wire, window, mode);
            return;
        }
        var video = (MpvVideoWindow)Field(service, "_videoWindow")!;
        Call(service, "OnScriptMessage", (object)new[] { "silo-minimize" });
        await Until(() => service.State == PlayerState.Minimized && IsWindowVisible(video.Hwnd),
            "The actual native down-arrow command did not show mini playback.");
        GetWindowRect(video.Hwnd, out var originalMini);
        Program.Log($"TRACE mini-before-prepare: {originalMini.Left},{originalMini.Top} {originalMini.Right-originalMini.Left}x{originalMini.Bottom-originalMini.Top}");
        var pendingGate = new PreparationGate();
        var successorGate = new PreparationGate();
        wire.HeldPreparations.Add("episode-2", pendingGate);
        wire.HeldPreparations.Add("episode-3", successorGate);
        using var cancellation = new CancellationTokenSource();
        var pending = service.PlayAsync("episode-2", fromStart: true, cancellationToken: cancellation.Token);
        try
        {
            await pendingGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(12));
            await Until(() => ((FrameworkElement)window.Content).FindName("LoadingOverlay") is FrameworkElement { Visibility: Visibility.Visible },
                "Replacement did not reach its real Preparing Playback surface.");
            if (mode == "mini-prepare-resize")
            {
                video.Hide();
                await QueueMiniPreparationCallbacks(service, window);
            }
            Check(!IsWindowVisible(video.Hwnd),
                "Preparing Playback left the native mini-player HWND visible at the outgoing bottom-left video location: " + mode);
            await QueueMiniPreparationCallbacks(service, window);
            Check(!IsWindowVisible(video.Hwnd), "Queued mini resize/restore callbacks revived the preparing native popup.");
            if (mode == "mini-prepare-native")
            {
                video.Show();
                Check(!IsWindowVisible(video.Hwnd), "Native Show bypassed preparation ownership.");
                video.PositionAt(originalMini.Left, originalMini.Top, 200, 112);
                Check(!IsWindowVisible(video.Hwnd), "Native mini positioning bypassed preparation ownership.");
                video.EnterPictureInPicture();
                Check(!IsWindowVisible(video.Hwnd), "Native PiP positioning bypassed preparation ownership.");
                video.EnterFullscreen(activate: false);
                await Task.Delay(350);
                Check(!IsWindowVisible(video.Hwnd), "Delayed native fullscreen positioning bypassed preparation ownership.");
                video.ExitFullscreen();
                video.ExitPictureInPicture();
            }
            if (mode == "mini-prepare-failure") wire.FailStart = true;
            if (mode == "mini-prepare-cancel")
            {
                cancellation.Cancel();
                try { await pending; } catch (OperationCanceledException) { }
                pendingGate.Release.TrySetResult();
            }
            else if (mode == "mini-prepare-newer")
            {
                var retiredOwner = Field(service, "_playbackCts");
                var successor = service.PlayAsync("episode-3", fromStart: true, expectedIdentityContext: api.CaptureIdentityContext());
                await successorGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(12));
                pendingGate.Release.TrySetResult();
                try { await pending; } catch (OperationCanceledException) { }
                Call(video, "ReleaseVisibility", retiredOwner);
                video.PositionAt(originalMini.Left, originalMini.Top, 200, 112);
                await QueueMiniPreparationCallbacks(service, window);
                Check(!IsWindowVisible(video.Hwnd) && service.IsLoading,
                    "Retired preparation exposed or released the newer owner's loading surface.");
                successorGate.Release.TrySetResult();
                await successor;
            }
            else
            {
                pendingGate.Release.TrySetResult();
                await pending;
            }
            if (mode is "mini-prepare-cancel" or "mini-prepare-failure")
            {
                await Until(() => service.State == PlayerState.Idle && !service.IsLoading,
                    "Failed/canceled replacement did not retire playback.");
                Check(Field(video, "_visibilityOwner") == null,
                    "Failed/canceled preparation retained native visibility ownership.");
                await QueueMiniPreparationCallbacks(service, window);
                Check(!IsWindowVisible(video.Hwnd), "Failed/canceled preparation revived the old mini HWND.");
                wire.FailStart = false;
                successorGate.Release.TrySetResult();
                await service.PlayAsync("episode-3", fromStart: true);
            }
            var expectedContent = mode is "mini-prepare-cancel" or "mini-prepare-failure" or "mini-prepare-newer" ? "episode-3" : "episode-2";
            await Until(() => service.ContentId == expectedContent && !service.IsLoading &&
                service.Mpv?.GetPropertyFlag("idle-active") == false && (bool)Field(service, "_transportHasFirstOutput")!,
                "Replacement did not resume actual native playback.");
            Check(service.State == PlayerState.Expanded && IsWindowVisible(video.Hwnd),
                "Prepared replacement did not restore the expanded native surface.");
            GetWindowRect(video.Hwnd, out var expanded);
            Check(expanded.Right - expanded.Left > originalMini.Right - originalMini.Left,
                "Replacement retained the outgoing mini rectangle.");
            Call(service, "OnScriptMessage", (object)new[] { "silo-minimize" });
            await Until(() => service.State == PlayerState.Minimized && IsWindowVisible(video.Hwnd),
                "Replacement could not return to native mini playback.");
            await QueueMiniPreparationCallbacks(service, window);
            GetWindowRect(video.Hwnd, out var resumedMini);
            Program.Log($"TRACE mini-resumed: {resumedMini.Left},{resumedMini.Top} {resumedMini.Right-resumedMini.Left}x{resumedMini.Bottom-resumedMini.Top}");
            Check(resumedMini.Left == originalMini.Left && resumedMini.Top == originalMini.Top &&
                resumedMini.Right == originalMini.Right && resumedMini.Bottom == originalMini.Bottom,
                "Resumed mini playback lost its owner-relative position.");
            var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            GetWindowRect(ownerHwnd, out var previousOwner);
            window.AppWindow.Move(new(previousOwner.Left + 120, previousOwner.Top + 80));
            await QueueMiniPreparationCallbacks(service, window);
            GetWindowRect(ownerHwnd, out var movedOwner);
            GetWindowRect(video.Hwnd, out var movedMini);
            Check(movedMini.Left - resumedMini.Left == movedOwner.Left - previousOwner.Left &&
                movedMini.Top - resumedMini.Top == movedOwner.Top - previousOwner.Top &&
                movedMini.Right - movedMini.Left == resumedMini.Right - resumedMini.Left &&
                movedMini.Bottom - movedMini.Top == resumedMini.Bottom - resumedMini.Top,
                "Resumed native mini playback did not follow the moved owner window.");
            if (mode == "mini-prepare-newer")
                await VerifyExpandedPreparationHandoff(service, wire, api, video);
            Program.Log("PASS: MINI_PREPARATION native visibility, queued callbacks, ownership and resumed positioning: " + mode);
        }
        finally
        {
            pendingGate.Release.TrySetResult();
            successorGate.Release.TrySetResult();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    private static async Task VerifyExpandedPreparationHandoff(PlayerService service, Wire wire, SiloApiClient api,
        MpvVideoWindow video)
    {
        var pendingGate = new PreparationGate();
        var successorGate = new PreparationGate();
        wire.HeldPreparations.Add("episode-4", pendingGate);
        wire.HeldPreparations.Add("episode-5", successorGate);
        var pending = service.PlayAsync("episode-4", fromStart: true);
        Task successor = Task.CompletedTask;
        try
        {
            await pendingGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(12));
            Check(service.State == PlayerState.Minimized && !IsWindowVisible(video.Hwnd),
                "Held mini preparation did not suppress the native surface.");
            service.Expand();
            Check(service.State == PlayerState.Expanded && !IsWindowVisible(video.Hwnd),
                "Expanding a held preparation bypassed native visibility suppression.");
            successor = service.PlayAsync("episode-5", fromStart: true,
                expectedIdentityContext: api.CaptureIdentityContext());
            pendingGate.Release.TrySetResult();
            try { await pending; } catch (OperationCanceledException) { }
            await successorGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(12));
            Check(service.State == PlayerState.Expanded && Field(video, "_visibilityOwner") == null,
                "Retired mini preparation retained visibility ownership across an expanded successor.");
            successorGate.Release.TrySetResult();
            await successor;
            await Until(() => service.ContentId == "episode-5" && !service.IsLoading &&
                service.Mpv?.GetPropertyFlag("idle-active") == false && (bool)Field(service, "_transportHasFirstOutput")!,
                "Expanded successor did not resume native playback.");
            Check(service.State == PlayerState.Expanded && IsWindowVisible(video.Hwnd),
                "Retired mini preparation left the expanded successor's native surface hidden.");
        }
        finally
        {
            pendingGate.Release.TrySetResult();
            successorGate.Release.TrySetResult();
            try { await pending; } catch (OperationCanceledException) { }
            try { await successor; } catch (OperationCanceledException) { }
        }
    }

    private static async Task VerifyReplacementSurfacePreparation(PlayerService service, Wire wire, MainWindow window, string mode)
    {
        var video = (MpvVideoWindow)Field(service, "_videoWindow")!;
        if (mode == "mini-prepare-fullscreen") service.EnterFullscreen();
        if (mode == "mini-prepare-pip") service.EnterPictureInPicture();
        var hideOutgoing = mode == "mini-prepare-pip";
        var expectedState = hideOutgoing ? PlayerState.PictureInPicture : mode == "mini-prepare-fullscreen" ? PlayerState.Fullscreen : PlayerState.Expanded;
        await Until(() => service.State == expectedState && IsWindowVisible(video.Hwnd), "Outgoing large video surface was not visible");
        var gate = new PreparationGate();
        wire.HeldPreparations.Add("episode-2", gate);
        var pending = service.PlayAsync("episode-2", fromStart: true);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(12));
            Check(service.IsLoading, "Replacement did not reach preparation");
            Check(IsWindowVisible(video.Hwnd) != hideOutgoing, "Incorrect outgoing visibility during preparation for " + expectedState);
            await QueueMiniPreparationCallbacks(service, window);
            Check(IsWindowVisible(video.Hwnd) != hideOutgoing, "Resize/restore changed outgoing visibility during preparation for " + expectedState);
            gate.Release.TrySetResult();
            await pending;
            await Until(() => !service.IsLoading && IsWindowVisible(video.Hwnd) && service.ContentId == "episode-2",
                "Large-surface replacement did not resume playback");
            Program.Log("PASS: MINI_PREPARATION retained outgoing large surface: " + mode);
        }
        finally
        {
            gate.Release.TrySetResult();
            await pending;
        }
    }
    private static async Task QueueMiniPreparationCallbacks(PlayerService service, MainWindow window)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(window.DispatcherQueue.TryEnqueue(() =>
        {
            service.HandleWindowResize();
            service.HandleWindowMinimized(true);
            service.HandleWindowMinimized(false);
            completed.TrySetResult();
        }), "Could not queue actual owner resize/minimize callbacks.");
        await completed.Task;
        await Task.Delay(250);
    }
}
