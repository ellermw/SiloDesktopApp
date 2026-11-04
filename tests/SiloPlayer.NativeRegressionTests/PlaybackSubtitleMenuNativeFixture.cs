using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

internal static partial class PlaybackAudioMenuNativeFixture
{
    internal static async Task RunSubtitleAsync(Window owner, bool failureCase = false)
    {
        using var foreground = new ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(owner));
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new MediaServer();
        using var wire = new Wire(media.Origin, "/video.y4m");
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://native-audio.fixture.invalid"); api.SetProfile("synthetic");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api); var settings = new SettingsApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "native-subtitle-settings")), settings);
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "synthetic-video", Type = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        foreground.Observe("subtitle-video", video.Hwnd, "video");
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv); mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-video");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        Call(service, "WireMpvEvents");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
        try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        finally { mpv.FileLoaded -= Loaded; }
        Call(service, "StopPlaybackStallWatchdog"); service.SetPaused(true);
        await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Subtitle video did not settle.");
        var session = manager.CurrentSession!;
        session.SubtitleUrls = Enumerable.Range(0, 70).Select(i => new SubtitleTrackInfo
        {
            Index = 1000 + i * 3, MediaFileId = 42, Language = i == 36 ? "ja" : "en",
            Source = "downloaded", Codec = "vtt", TrackId = "subtitle-" + i,
            Label = i == 69 ? "Long subtitle label 日本語 " + new string('x', 150) : "Subtitle source " + i,
            Forced = i == 0, HearingImpaired = i == 1,
            Url = media.Origin + "/fixture-subtitle.vtt",
        }).ToList();
        session.SubtitleUrls[36].SyncKey = "stored-1036";
        session.SubtitleUrls[37].Url = media.Origin + "/missing-subtitle.vtt";
        session.SubtitleUrls[38].Url = media.Origin + "/held-subtitle.vtt";
        session.SubtitleUrls[0].Unplayable = true;
        session.SubtitleUrls[0].Url = "";
        session.SubtitleUrls[1].Url = "";
        wire.EnableSubtitleSync = true;
        Set(service, "_subtitlePreferences", new SubtitlePreferenceSnapshot(api.CaptureContext(), new SubtitleAppearance(), "default", 0, "default", false));
        Set(service, "_subtitlePreferenceAuthority", service.ActiveAuthority);
        Call(service, "SendSubtitleListToOsc");
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "subtitle-exact-failed-sidecar")
            {
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1108, true)!;
                var acceptedSid = mpv.GetPropertyDouble("sid");
                Require(acceptedSid > 0 && service.ActiveSubtitleServerIndex == 1108, "Prior subtitle did not execute.");
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1111, true)!;
                Program.Log($"Exact failed-sidecar: accepted=1108 requested=1111 current={service.ActiveSubtitleServerIndex} sid={mpv.GetPropertyDouble("sid")} priorSid={acceptedSid} saved={string.Join(',',wire.SubtitleSavedIndices)}");
                Require(service.ActiveSubtitleServerIndex == 1108 && mpv.GetPropertyDouble("sid") == acceptedSid && !wire.SubtitleSavedIndices.Contains(1111),
                    "Failed1111 sidecar replaced executed1108 identity/preference.");
                Program.Log("PASS: exact failed-sidecar preserves executed1108/nativeSID/preference after1111 HTTP404.");
                return;
            }
            var method = typeof(PlayerService).GetMethod("HandleNativeMenuOpenAsync", Private);
            Require(method != null, "Native Subtitle does not have a shared production menu entry point.");
            var menu = await Open(); var list = (ListView)Get(menu, "_list")!;
            Require(list.Items.Count >= 71, "Native Subtitle omitted Off or original source inventory.");
            var actualRows = ((System.Collections.IEnumerable)Get(menu, "_choices")!).Cast<object>().ToArray();
            foreach (var index in new[] { 1000, 1003 })
            {
                var disabled = actualRows.Single(row => row.GetType().GetProperty("Id")!.GetValue(row)?.ToString() == index.ToString());
                Require(!(bool)disabled.GetType().GetProperty("IsEnabled")!.GetValue(disabled)! &&
                    !string.IsNullOrWhiteSpace(disabled.GetType().GetProperty("DisabledReason")!.GetValue(disabled)?.ToString()),
                    "Full unavailable or route-less subtitle inventory was omitted or remained actionable.");
            }
            list.SelectedIndex = 1; Call(menu, "Input", "activate");
            Require(Menu(service) != null && service.ActiveSubtitleServerIndex < 0 && !wire.SubtitleSavedIndices.Any(),
                "A disabled actual Subtitle row dispatched or persisted a choice.");
            var root = (Grid)Get(menu, "_root")!;
            Require(Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Add subtitles"), "Subtitle upload/search handoff is unreachable.");
            Require(Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Subtitle appearance"), "Subtitle appearance handoff is unreachable.");
            Require(Descendants<Button>(root).Where(button => AutomationProperties.GetName(button).StartsWith("Subtitle delay", StringComparison.Ordinal)).All(button => !button.IsEnabled), "Delay should be disabled while Off.");
            Call(menu, "Input", "end");
            Require(list.SelectedIndex == list.Items.Count - 1 && Descendants<ScrollViewer>(list).Any(scroll => scroll.VerticalOffset > 0), "Subtitle End did not scroll to the last original track.");
            var lastContainer = list.Items[list.SelectedIndex];
            var choiceType = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuChoice")!;
            var originalChoices = ((System.Collections.IEnumerable)Get(menu, "_choices")!).Cast<object>().ToArray();
            var updated = Array.CreateInstance(choiceType, originalChoices.Length);
            for (var i = 0; i < originalChoices.Length; i++) updated.SetValue(originalChoices[i], i);
            var lastChoice = originalChoices[^1];
            object? Value(string name) => choiceType.GetProperty(name)!.GetValue(lastChoice);
            updated.SetValue(Activator.CreateInstance(choiceType, [Value("Id"), Value("Title"), "Live updated status", Value("Badges"), Value("IsCurrent"), true, "", true, false]), originalChoices.Length - 1);
            Call(menu, "UpdateChoices", updated);
            Require(ReferenceEquals(lastContainer, list.Items[list.SelectedIndex]), "Live menu status replaced the focused native row.");
            var originalArray = Array.CreateInstance(choiceType, originalChoices.Length);
            for (var i = 0; i < originalChoices.Length; i++) originalArray.SetValue(originalChoices[i], i);
            Call(menu, "UpdateChoices", originalArray);
            await Capture(menu, "subtitle-end.png");
            foreach (var size in new[] { (460, 700), (900, 540) })
            {
                owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2)); video.SynchronizePosition(); await Task.Delay(120);
                await Capture(menu, "subtitle-" + size.Item1 + ".png");
                Require(GetWindowRect(video.Hwnd, out var surface) && GetWindowRect(MenuHwnd(menu), out var bounds) && bounds.Left >= surface.Left && bounds.Right <= surface.Right && bounds.Top >= surface.Top && bounds.Bottom <= surface.Bottom, "Subtitle escaped its owner.");
            }
            Call(menu, "Input", "cancel"); Require(Gate(service).CurrentCount == 1 && mpv.IsPaused, "Subtitle dismissal changed playback or stranded its gate.");
            // Actual downloaded subtitle selection keeps the original server index.
            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            list.SelectedIndex = 37;
            Call(menu, "Input", "activate");
            await Until(() => service.ActiveSubtitleServerIndex == session.SubtitleUrls[36].Index && mpv.GetPropertyDouble("sid") > 0, "Native Subtitle did not dispatch the actual server identity through existing sidecar selection.");
            Require(mpv.IsPaused, "Subtitle selection lost genuine paused intent.");
            Program.Log("PASS: native Subtitle original server identity selected actual downloaded cues without losing genuine pause.");
            if (failureCase)
            {
                var accepted = session.SubtitleUrls[36].Index;
                var failed = session.SubtitleUrls[37].Index;
                var acceptedSid = mpv.GetPropertyDouble("sid");
                menu = await Open(); list = (ListView)Get(menu, "_list")!; list.SelectedIndex = 38;
                Call(menu, "Input", "activate");
                await Until(() => Get(service, "_nativeSubtitlePending") == null, "Failed Subtitle selection did not finish.");
                Program.Log($"Subtitle failure observation: acceptedIndex={accepted}, requestedIndex={failed}, serviceIndex={service.ActiveSubtitleServerIndex}, beforeSID={acceptedSid}, afterSID={mpv.GetPropertyDouble("sid")}, savedIndices={string.Join(',', wire.SubtitleSavedIndices)}.");
                Require(service.ActiveSubtitleServerIndex == accepted && mpv.GetPropertyDouble("sid") == acceptedSid && !wire.SubtitleSavedIndices.Contains(failed),
                    "Failed sidecar replaced accepted current/preference despite unchanged native SID; Subtitle delivery requires reconciled PR75/77 backend outcomes.");
                return;
            }
            var acceptedSidBeforePending = mpv.GetPropertyDouble("sid");
            menu = await Open(); list = (ListView)Get(menu, "_list")!; list.SelectedIndex = 39;
            Call(menu, "Input", "activate");
            await media.SubtitleReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            menu = await Open();
            var pendingRows = ((System.Collections.IEnumerable)Get(menu, "_choices")!).Cast<object>().ToArray();
            bool RowFlag(int id, string flag)
            {
                var row = pendingRows.Single(row => row.GetType().GetProperty("Id")!.GetValue(row)?.ToString() == id.ToString());
                return (bool)row.GetType().GetProperty(flag)!.GetValue(row)!;
            }
            Require(RowFlag(1108, "IsCurrent") && !RowFlag(1114, "IsCurrent") && RowFlag(1114, "IsPending") &&
                mpv.GetPropertyDouble("sid") == acceptedSidBeforePending && !wire.SubtitleSavedIndices.Contains(1114),
                "Native pending presentation replaced the executed checkmark, SID or preference before sidecar completion.");
            Call(menu, "Input", "cancel"); media.ReleaseSubtitleRead.TrySetResult();
            await Until(() => Get(service, "_nativeSubtitlePending") == null && service.ActiveSubtitleServerIndex == 1114 && wire.SubtitleSavedIndices.Contains(1114),
                "Current held Subtitle selection did not execute after its sidecar became ready.");
            await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 1108, true)!;
            Program.Log("PASS: actual unavailable rows remain inert; held native selection shows executed/current separately from pending until real SID execution.");
            menu = await Open(); root = (Grid)Get(menu, "_root")!;
            var later = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Subtitle delay 100ms later");
            Require(later.IsEnabled, "Delay did not enable for current subtitles.");
            Call(service, "InvokeNativeSubtitleAction", "later");
            await Until(() => service.SubtitleOffsetMs == 100, "Native Subtitle delay did not delegate to service preferences.");
            Require(ReferenceEquals(Menu(service), menu), "Delay adjustment closed the menu.");
            await Until(() => Descendants<TextBlock>(root).Any(text => text.Text == "+100 ms"), "Subtitle delay did not update live.");
            Call(service, "InvokeNativeSubtitleAction", "reset-delay");
            await Until(() => service.SubtitleOffsetMs == 0, "Subtitle delay reset did not execute.");
            await Until(() => Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Sync subtitles to audio" && button.Visibility == Visibility.Visible && button.IsEnabled), "Active stored subtitle lost its sync action.");
            Call(service, "InvokeNativeSubtitleAction", "sync");
            await wire.SubtitleSyncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Sync subtitles to audio" && !button.IsEnabled), "Sync pending did not disable its actual native action.");
            Call(service, "InvokeNativeSubtitleAction", "sync");
            Require(wire.SubtitleSyncStarts == 1 && ReferenceEquals(Menu(service), menu), "Pending sync duplicated a request or dismissed the menu.");
            wire.ReleaseSubtitleSync.TrySetResult();
            await Until(() => Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Reset subtitle timing" && button.IsEnabled && button.Visibility == Visibility.Visible), "Reset timing did not recover after sync completion.");
            Call(service, "InvokeNativeSubtitleAction", "reset-timing");
            await Until(() => wire.SubtitleTimingResets == 1, "Native Reset timing did not use the existing ETag/API action.");
            Require(ReferenceEquals(Menu(service), menu) && mpv.IsPaused, "Subtitle timing actions changed genuine pause or dismissed the menu.");
            Program.Log("PASS: actual native Subtitle live delay and held sync/reset ETag actions stay open, reject duplicate sync and preserve pause.");
            api.SetProfile("replacement"); await Until(() => Menu(service) == null, "Profile replacement stranded Subtitle host.");
            Require(Gate(service).CurrentCount == 1, "Profile replacement stranded Subtitle lease.");
            api.SetProfile("synthetic"); Call(service, "SendSubtitleListToOsc");
            session.SubtitleUrls = [];
            Call(service, "SendSubtitleListToOsc"); menu = await Open();
            Require(((ListView)Get(menu, "_list")!).Items.Count == 1, "Empty Subtitle inventory lost the Off action.");
            Call(menu, "Input", "cancel");
            // Shared host navigation must skip disabled endpoints in either direction.
            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            var endpoints = Array.CreateInstance(choiceType, 3);
            for (var i = 0; i < 3; i++) endpoints.SetValue(Activator.CreateInstance(choiceType,
                [i.ToString(), "Endpoint " + i, "", Array.Empty<string>(), false, i == 1, i == 1 ? "" : "Unavailable", true, false]), i);
            Call(menu, "UpdateChoices", endpoints); Call(menu, "Input", "end");
            Require(list.SelectedIndex == 1, "End did not skip a disabled last row.");
            Call(menu, "Input", "home"); Require(list.SelectedIndex == 1, "Home did not skip a disabled first row.");
            Call(menu, "Input", "cancel");
            Program.Log("PASS: actual native Subtitle choices/Off, original server indices, long-list scrolling, fixed actions, live delay, pause, empty inventory and profile retirement.");
        }
        finally
        {
            media.ReleaseSubtitleRead.TrySetResult();
            if (Menu(service) is { } menu) Call(menu, "Input", "cancel");
            Call(service, "StopPlaybackStallWatchdog");
            video.Hide(); await service.CloseAsync(); foreground.Complete();
        }

        async Task<object> Open()
        {
            var current = Get(service, "_nativeMenuOwner")!;
            var token = (string)current.GetType().GetProperty("Token")!.GetValue(current)!;
            var sequence = ++_subtitleSequence;
            Call(service, "OnScriptMessage", (object)new[] { "silo-native-subtitle-open", token, sequence.ToString(), "1000", "620", "40", "40", "1280", "720" });
            await Until(() => Menu(service) is { } shown && ((ListView)Get(shown, "_list")!).XamlRoot != null, "Native Subtitle was not shown.");
            return Menu(service)!;
        }
    }
    private static int _subtitleSequence;
}
