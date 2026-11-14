using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using Windows.Graphics.Imaging;

// Owner report, November 13: after the latest episode ended, "You've finished ..." and On Deck
// were right, but the finishing slot beside them stayed a large empty grey rectangle. The native
// preview is hidden at true EOF and the slot had nothing of its own to show.
//
// Cases (SILO_HUD_COMPLETION_CASE): finished-early-eof, finished-eof, finished-no-art, finished-stale.
// Each drives the real PlayerService/PlaybackManager/libmpv to an actual EOF and reads the rendered shell.
internal static partial class PlaybackHudCompletionNativeFixture
{
    private static readonly (byte R, byte G, byte B) Red = (208, 32, 40), Blue = (32, 56, 216), Green = (24, 176, 72);

    private static async Task PrepareFinishedPreview(Wire wire, string mode)
    {
        // A fresh path per run: the fixture's image cache outlives the process in its result directory.
        var run = "/backdrops/" + Guid.NewGuid().ToString("N");
        string Add(string name, HttpStatusCode status, byte[] body)
        {
            var path = $"{run}/{name}.png";
            wire.Backdrops[path] = (status, body);
            return path;
        }
        var red = Add("red", HttpStatusCode.OK, await SolidPng(Red));
        var blue = Add("blue", HttpStatusCode.OK, await SolidPng(Blue));
        var green = Add("green", HttpStatusCode.OK, await SolidPng(Green));
        switch (mode)
        {
            case "finished-early-eof":
            case "finished-eof":
                wire.BackdropFor["episode-1"] = red;
                break;
            case "finished-no-art":
                // episode-1 names no backdrop at all.
                wire.BackdropFor["episode-refused"] = Add("refused", HttpStatusCode.InternalServerError, []);
                wire.BackdropFor["episode-corrupt"] = Add("corrupt", HttpStatusCode.OK, Enumerable.Repeat((byte)0x5A, 4096).ToArray());
                break;
            case "finished-stale":
                wire.BackdropFor["episode-1"] = red;
                wire.BackdropFor["episode-2"] = blue;
                wire.BackdropFor["episode-3"] = green;
                wire.HeldBackdrops[red] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.HeldBackdrops[green] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                break;
            default:
                throw new InvalidOperationException("Unknown finished-preview case: " + mode);
        }
    }

    private static async Task VerifyFinishedPreview(string mode, PlayerService service, Wire wire, MainWindow window,
        FrameworkElement root, AuthService auth)
    {
        var screen = new FinishedScreen(service, window, root);
        switch (mode)
        {
            case "finished-early-eof": await VerifyEarlyPreviewToEnd(screen, service, window, root); break;
            case "finished-eof": await VerifyDirectEnd(screen, service, window, root); break;
            case "finished-no-art": await VerifyNoArtwork(screen, wire, root); break;
            default: await VerifyStaleOwners(screen, service, wire, window, root, auth); break;
        }
    }

    // The owner's path: the early post-roll already shows the live episode beside "You've finished",
    // then true EOF hides the native window.
    private static async Task VerifyEarlyPreviewToEnd(FinishedScreen screen, PlayerService service, MainWindow window, FrameworkElement root)
    {
        var live = await screen.LivePreview("episode-1");
        Check(screen.Text("PlayingNextLabelText") == "FINISHED" && screen.Named("PlayingNextFinishedPanel").Visibility == Visibility.Visible,
            "The last episode's early post-roll is not the finished screen.");
        await Capture(root, "finished-early-live.png", service);
        // Decoded under the live preview (absent before the fix), so nothing has to load at EOF.
        var prepared = window.GetType().GetField("_finishingArt", Flags);
        if (prepared != null)
            await Until(() => prepared.GetValue(window) is { } art && (bool)art.GetType().GetProperty("Opened")!.GetValue(art)!,
                "The finishing artwork was not decoded while the episode was still playing.");

        var ended = await screen.TrueEnd();
        Check(screen.Art is null or { Opacity: 1, OpacityTransition: null },
            "Prepared finishing artwork did not take over when the native preview was hidden.");
        await screen.ExpectArt(Red, live, "True EOF left the finishing slot an empty rectangle instead of the finished title's artwork.");
        Check(ended.FinishedCountdown, "The finished screen lost its own closing countdown.");
        Check(screen.Art is { Opacity: 1, Source: not null }, "The finishing artwork is not the slot's own picture.");
        Check(screen.Text("PlayingNextFinishedHeading") == "You've finished Synthetic Show" && screen.Text("PlayingNextFinishingTitle") == "Finishing episode",
            "The ended screen lost its finished heading or the finished episode's caption.");
        Check(FinishedScreen.SameRect(screen.SlotRect(), live), "The finishing slot moved when the native preview gave way to its artwork.");
        await Capture(root, "finished-early-ended.png", service);

        // Responsive: compact puts the finishing picture above the finished text, wide beside it.
        window.AppWindow.ResizeClient(new(800, 700)); await Task.Delay(450);
        var compact = screen.SlotRect();
        await screen.ExpectArt(Red, compact, "The compact ended screen lost the finishing artwork.");
        Check(!IsWindowVisible(screen.Video), "A resize brought the ended native popup back over the ended screen.");
        var finishedTop = screen.Named("PlayingNextFinishedPanel").TransformToVisual(root).TransformPoint(new(0, 0)).Y;
        Check(compact.Y + compact.Height <= finishedTop - 8, "Compact finishing artwork covers the finished text and actions.");
        await Capture(root, "finished-early-ended-small.png", service);

        window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(450);
        var wide = screen.SlotRect();
        await screen.ExpectArt(Red, wide, "Returning to the wide layout lost the finishing artwork.");
        Check(!IsWindowVisible(screen.Video), "A resize brought the ended native popup back over the ended screen.");
        var columnLeft = screen.Named("PlayingNextScroller").TransformToVisual(root).TransformPoint(new(0, 0)).X;
        Check(wide.X + wide.Width <= columnLeft - 24, "Wide finishing artwork is not beside the finished column.");

        await screen.Dismiss();
        Check(screen.Art!.Source == null && Field(window, "_finishingArt") == null, "Closing the ended screen kept its finishing artwork alive.");
        Program.Log("PASS: early live preview to actual EOF shows the finished title's artwork in the same slot, wide and compact.");
    }

    // True EOF with Playing Next closed (the viewer returned to the episode), with and without a next episode.
    private static async Task VerifyDirectEnd(FinishedScreen screen, PlayerService service, MainWindow window, FrameworkElement root)
    {
        var live = await screen.LivePreview("episode-1");
        Call(service, "ReturnFromPostRollPreview");
        await Until(() => screen.Overlay.Visibility == Visibility.Collapsed, "Returning to the episode did not close early post-roll.");
        var ended = await screen.TrueEnd();
        await screen.ExpectArt(Red, live, "True EOF opened the finished screen with an empty finishing rectangle.");
        Check(ended.FinishedCountdown && screen.Text("PlayingNextLabelText") == "FINISHED" &&
            screen.Named("PlayingNextEpisodePanel").Visibility == Visibility.Collapsed,
            "The no-next ended screen is not the finished presentation.");
        await Capture(root, "finished-eof-ended.png", service);
        await screen.Dismiss();

        // With a next episode, the artwork sits beside the next-episode card and its actions.
        await screen.Play("episode-1");
        live = await screen.LivePreview("episode-1");
        // After the load's own (empty) episode lookup, as the post-roll case does.
        await Task.Delay(400);
        service.SetNextEpisodeHint("episode-1", "episode-2", "The next episode", "Synthetic Show", null, "Next episode overview",
            runtimeSeconds: 30, seasonNumber: 1, episodeNumber: 2);
        Call(service, "ReturnFromPostRollPreview");
        await Until(() => screen.Overlay.Visibility == Visibility.Collapsed, "Returning to the episode did not close early post-roll.");
        Check(service.HasNextEpisodeForCurrentPlayback, "The fixture's next-episode hint did not hold.");
        ended = await screen.TrueEnd();
        await screen.ExpectArt(Red, live, "True EOF with a next episode left the finishing slot empty.");
        var play = screen.Named("PlayingNextPlayNowButton");
        var hero = screen.Named("PlayingNextHero");
        var slot = screen.SlotRect();
        Check(ended.NextCountdown && screen.Text("PlayingNextLabelText") == "PLAYING NEXT" && play.ActualWidth > 0 && ((Control)play).IsEnabled,
            "The ended next-episode screen lost its countdown or Play Now.");
        Check(slot.X + slot.Width <= hero.TransformToVisual(root).TransformPoint(new(0, 0)).X - 24,
            "Finishing artwork overlaps the next-episode card.");
        Check(screen.Named("PlayingNextPoster") is Image { Source: null }, "The finished title's picture leaked into the next-episode card.");
        await Capture(root, "finished-eof-next.png", service);
        window.AppWindow.ResizeClient(new(800, 700)); await Task.Delay(450);
        slot = screen.SlotRect();
        await screen.ExpectArt(Red, slot, "The compact next-episode ended screen lost the finishing artwork.");
        Check(slot.Y + slot.Height <= hero.TransformToVisual(root).TransformPoint(new(0, 0)).Y - 8,
            "Compact finishing artwork covers the next-episode card.");
        Check(!IsWindowVisible(screen.Video), "A resize brought the ended native popup back over the next-episode actions.");
        await Capture(root, "finished-eof-next-small.png", service);
        foreach (var state in new[] { PlayerState.Minimized, PlayerState.PictureInPicture, PlayerState.Expanded })
        {
            service.SetState(state);
            service.FinishPostRollPreview();
            service.HandleWindowResize();
            Check(!IsWindowVisible(screen.Video), $"Resize re-showed the ended video in {state} state.");
            service.HandleWindowMinimized(false);
            Check(!IsWindowVisible(screen.Video), $"Owner restore re-showed the ended video in {state} state.");
        }
        Program.Log("PASS: actual EOF with Playing Next closed shows finishing artwork, finished and next-episode presentations.");
        Program.Log("PASS: ended native video stays hidden on resize/restore in mini-bar, PiP and expanded states.");
    }

    // No backdrop named, a refused download and an undecodable file: the rectangle leaves the screen.
    private static async Task VerifyNoArtwork(FinishedScreen screen, Wire wire, FrameworkElement root)
    {
        foreach (var (id, reason) in new[] { ("episode-1", "no backdrop"), ("episode-refused", "a refused backdrop"), ("episode-corrupt", "an undecodable backdrop") })
        {
            if (id != "episode-1") await screen.Play(id);
            var live = await screen.LivePreview(id);
            var columnLeft = screen.Named("PlayingNextScroller").TransformToVisual(root).TransformPoint(new(0, 0)).X;
            await screen.TrueEnd();
            await screen.ExpectNoRectangle(live, $"True EOF with {reason} left an empty finishing rectangle.");
            Check(screen.Named("PlayingNextFinishingPanel").Visibility == Visibility.Collapsed && screen.Art is { Opacity: 0 },
                $"The finishing slot with {reason} was hidden without leaving the layout.");
            Check(Math.Abs(screen.Named("PlayingNextScroller").TransformToVisual(root).TransformPoint(new(0, 0)).X - columnLeft) < 1 &&
                screen.Named("PlayingNextFinishedPanel").Visibility == Visibility.Visible && ((Control)screen.Named("PlayingNextCloseButton")).IsEnabled,
                $"Removing the finishing slot with {reason} disturbed the finished column or its actions.");
            await Capture(root, $"finished-no-art-{id}.png");
            await screen.Dismiss();
        }
        var refused = wire.BackdropRequests.Where(pair => pair.Key.EndsWith("/refused.png", StringComparison.Ordinal)).Sum(pair => pair.Value);
        Check(refused is >= 1 and <= 4, $"A refused finishing backdrop was requested {refused} times.");
        Program.Log("PASS: missing, refused and undecodable finishing artwork removes the rectangle and keeps live preview, layout and actions.");
    }

    // Results that arrive after their owner has gone: a closed screen, a newer playback, another profile.
    private static async Task VerifyStaleOwners(FinishedScreen screen, PlayerService service, Wire wire, MainWindow window,
        FrameworkElement root, AuthService auth)
    {
        var red = wire.BackdropFor["episode-1"]; var green = wire.BackdropFor["episode-3"];
        var live = await screen.LivePreview("episode-1");
        await screen.TrueEnd();
        Check(wire.BackdropRequests.GetValueOrDefault(red) >= 1, "The held backdrop was never requested.");
        await screen.ExpectNoRectangle(live, "True EOF showed an empty finishing rectangle while its artwork was still loading.");
        await Capture(root, "finished-stale-pending.png");

        // Closed while loading: the late picture is dropped.
        await screen.Dismiss();
        wire.HeldBackdrops[red].TrySetResult(); await Task.Delay(700);
        Check(screen.Art is { Source: null } && Field(window, "_finishingArt") == null, "A closed ended screen adopted its late finishing artwork.");

        await screen.Play("episode-2");
        live = await screen.LivePreview("episode-2");
        Check(screen.Art is { Opacity: 0 }, "Finishing artwork was revealed while the episode was still playing.");
        await screen.TrueEnd();
        await screen.ExpectArt(Blue, live, "The successor's ended screen did not show its own finishing artwork.");
        await Capture(root, "finished-stale-successor.png");

        // A newer playback under the open ended screen: the shown picture leaves at once.
        var next = service.PlayAsync("episode-3", fromStart: true);
        await Until(() => service.ContentId == "episode-3", "The newer playback never became current.");
        await Until(() => screen.Art is { Source: null } or { Opacity: 0 }, "The previous episode's artwork stayed on screen after playback changed.");
        Check(Field(window, "_finishingArt") == null, "The previous episode's artwork stayed prepared after playback changed.");
        await next; await screen.Loaded("episode-3");
        live = await screen.LivePreview("episode-3");
        await screen.TrueEnd();
        await screen.ExpectNoRectangle(live, "The newer playback's ended screen showed a rectangle while its artwork was held.");
        Check(screen.Art is { Opacity: 0 }, "A previous episode's artwork was resurrected for the newer playback.");

        // Another profile takes over while the picture is still loading: it never appears.
        auth.SelectProfile("second", profile: new() { Id = "second", Name = "Second" });
        wire.HeldBackdrops[green].TrySetResult(); await Task.Delay(700);
        Check(screen.Art is { Source: null } or { Opacity: 0 } && Field(window, "_finishingArt") == null,
            "Finishing artwork loaded for one profile was adopted after the profile changed.");
        if (screen.Overlay.Visibility == Visibility.Visible)
        {
            var sample = await FinishedScreen.Render(root);
            Check(!FinishedScreen.Near(sample(live.X + live.Width / 2, live.Y + live.Height / 2), Green, 40),
                "Finishing artwork loaded for one profile was drawn after the profile changed.");
        }
        await Capture(root, "finished-stale-profile.png");
        Program.Log("PASS: late finishing artwork for a closed screen, an earlier playback and an earlier profile is never shown.");
    }

    private static async Task<byte[]> SolidPng((byte R, byte G, byte B) color)
    {
        const int width = 320, height = 180;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        using var stream = new MemoryStream();
        using var random = stream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, random);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        return stream.ToArray();
    }

    private sealed class FinishedScreen(PlayerService service, MainWindow window, FrameworkElement root)
    {
        internal readonly FrameworkElement Overlay = (FrameworkElement)root.FindName("PlayingNextOverlay");
        private readonly FrameworkElement _slot = (FrameworkElement)root.FindName("PlayingNextFinishingSlot");

        /// <summary>The slot's own picture; absent before the fix.</summary>
        internal Image? Art => root.FindName("PlayingNextFinishingArt") as Image;
        internal nint Video => ((MpvVideoWindow)Field(service, "_videoWindow")!).Hwnd;
        internal FrameworkElement Named(string name) => (FrameworkElement)root.FindName(name);
        internal string Text(string name) => ((TextBlock)root.FindName(name)).Text;

        internal Windows.Foundation.Rect SlotRect()
        {
            var point = _slot.TransformToVisual(root).TransformPoint(new(0, 0));
            return new(point.X, point.Y, _slot.ActualWidth, _slot.ActualHeight);
        }

        internal static bool SameRect(Windows.Foundation.Rect a, Windows.Foundation.Rect b) =>
            Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;

        internal static bool Near((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, int tolerance) =>
            Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

        /// <summary>The shell as WinUI actually rendered it, sampled in root coordinates.</summary>
        internal static async Task<Func<double, double, (byte R, byte G, byte B)>> Render(FrameworkElement root)
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            double scaleX = width / root.ActualWidth, scaleY = height / root.ActualHeight;
            return (x, y) =>
            {
                var i = (Math.Clamp((int)Math.Round(y * scaleY), 0, height - 1) * width + Math.Clamp((int)Math.Round(x * scaleX), 0, width - 1)) * 4;
                return (pixels[i + 2], pixels[i + 1], pixels[i]);
            };
        }

        internal async Task Loaded(string id)
        {
            await Until(() => service.Mpv != null && !service.IsLoading && service.WatchDetail?.ContentId == id &&
                service.Mpv.GetPropertyFlag("idle-active") == false && (bool)Field(service, "_transportHasFirstOutput")!,
                "Playback did not load: " + id);
            service.Mpv!.SetProperty("ao", "null"); service.Mpv.SetProperty("loop-file", "inf");
            Call(service, "StopPlaybackStallWatchdog");
        }

        internal async Task Play(string id)
        {
            await service.PlayAsync(id, fromStart: true);
            await Loaded(id);
        }

        /// <summary>The service's own early post-roll: the episode keeps playing in the slot.</summary>
        internal async Task<Windows.Foundation.Rect> LivePreview(string id)
        {
            await Until(() => Overlay.Visibility == Visibility.Visible && service.ContentId == id && service.IsPostRollActive &&
                !service.IsPostRollVideoEnded && IsWindowVisible(Video) && _slot.ActualWidth > 0,
                "The service did not enter its live early post-roll for " + id);
            Check(Named("PlayingNextFinishingPanel").Visibility == Visibility.Visible, "The live finishing preview lost its slot.");
            var origin = new Point(); ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(window), ref origin);
            var scale = root.XamlRoot.RasterizationScale;
            bool Covers()
            {
                var rect = SlotRect(); GetWindowRect(Video, out var bounds);
                return Math.Abs(bounds.Left - (origin.X + rect.X * scale)) <= 3 && Math.Abs(bounds.Top - (origin.Y + rect.Y * scale)) <= 3 &&
                    Math.Abs(bounds.Right - bounds.Left - rect.Width * scale) <= 3 && Math.Abs(bounds.Bottom - bounds.Top - rect.Height * scale) <= 3;
            }
            await Until(Covers, "The live finishing video does not sit over its slot.");
            var position = service.Mpv!.Position;
            await Until(() => service.Mpv.Position != position && !service.Mpv.IsPaused, "Early post-roll interrupted live finishing playback.");
            Check(Art is null or { Opacity: 0 }, "Finishing artwork was revealed while the episode was still playing.");
            return SlotRect();
        }

        /// <summary>Plays to the file's real end, then holds the ended screen open for inspection.</summary>
        internal async Task<(bool FinishedCountdown, bool NextCountdown)> TrueEnd()
        {
            service.Mpv!.SetProperty("loop-file", "no"); service.Mpv.Seek(29.8);
            await Until(() => service.IsPostRollVideoEnded && Overlay.Visibility == Visibility.Visible && !IsWindowVisible(Video),
                "Actual native EOF did not reach the ended screen with the native popup hidden.");
            var finished = (bool)Field(window, "_playingNextFinishedCountdown")!;
            var timer = Field(window, "_playingNextTimer") != null;
            Call(window, "CancelFinishedAutoClose"); Call(window, "StopPlayingNextCountdown");
            return (finished, timer && !finished);
        }

        internal async Task ExpectArt((byte R, byte G, byte B) color, Windows.Foundation.Rect rect, string message)
        {
            var until = DateTime.UtcNow.AddSeconds(6);
            (byte R, byte G, byte B) seen = default;
            while (DateTime.UtcNow < until)
            {
                var sample = await Render(root);
                seen = sample(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                // The picture fills the slot: its centre and each inner corner.
                if (Near(seen, color, 24) &&
                    Near(sample(rect.X + rect.Width * .08, rect.Y + rect.Height * .12), color, 24) &&
                    Near(sample(rect.X + rect.Width * .92, rect.Y + rect.Height * .88), color, 24)) return;
                await Task.Delay(60);
            }
            throw new InvalidOperationException($"{message} Slot centre rendered #{seen.R:X2}{seen.G:X2}{seen.B:X2}.");
        }

        /// <summary>Where the slot was, the wide ended screen now looks like the screen beside it.</summary>
        internal async Task ExpectNoRectangle(Windows.Foundation.Rect rect, string message)
        {
            var until = DateTime.UtcNow.AddSeconds(4);
            (byte R, byte G, byte B) seen = default, beside = default;
            while (DateTime.UtcNow < until)
            {
                var sample = await Render(root);
                seen = sample(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                beside = sample(rect.X / 2, rect.Y + rect.Height / 2);
                if (Near(seen, beside, 5) && Near(sample(rect.X + rect.Width * .08, rect.Y + rect.Height * .12), beside, 5)) return;
                await Task.Delay(60);
            }
            throw new InvalidOperationException(
                $"{message} Slot centre rendered #{seen.R:X2}{seen.G:X2}{seen.B:X2} beside #{beside.R:X2}{beside.G:X2}{beside.B:X2}.");
        }

        internal async Task Dismiss()
        {
            Call(window, "DismissPlayingNext");
            Check(Overlay.Visibility == Visibility.Collapsed, "Close did not dismiss the ended screen.");
            await Until(() => service.State == PlayerState.Idle, "Dismissing the ended screen did not close playback.");
        }
    }
}
