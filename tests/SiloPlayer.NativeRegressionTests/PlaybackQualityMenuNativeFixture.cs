using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

/// <summary>Actual OSC, native choice controls and replans with generated video and an isolated fake API.</summary>
internal static class PlaybackQualityMenuNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(Window window, string mode = "normal")
    {
        var configuredMode = Environment.GetEnvironmentVariable("SILO_NATIVE_QUALITY_CASE");
        if (!string.IsNullOrEmpty(configuredMode)) mode = configuredMode;
        using var foreground = new PlaybackAudioMenuNativeFixture.ForegroundOwnershipProbe(
            WinRT.Interop.WindowNative.GetWindowHandle(window), "quality-" + mode);
        window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer();
        var mediaPath = mode == "quality-load-retry" ? "/stream/direct/synthetic-quality.y4m" : "/video.y4m";
        using var wire = new Wire(media.Origin, mediaPath);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://native-quality.fixture.invalid"); api.SetProfile("synthetic");
        api.SetDeviceMetadata("quality-desktop", "Fixture", "Windows");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "quality-settings")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var versions = new List<FileVersion>
        {
            new() { FileId = 41, Resolution = "1080p", CodecVideo = "hevc", Duration = 60 },
            new() { FileId = 71, Resolution = "2160p", CodecVideo = "av1", Hdr = true, Duration = 60 },
        };
        manager.UseWatchDetail(new() { ContentId = "synthetic-quality", Type = "movie", Versions = versions });
        await manager.StartSessionAsync(41);
        using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(window)); video.Show();
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv);
        mpv.SetProperty("log-file", Path.Combine(Program.ResultDirectory, "quality-mpv.log"));
        mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-quality"); Property(service, "Versions", versions);
        Call(service, "WireMpvEvents");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retainedDirectCallbacks = new List<object>();
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        void Message(string[] args)
        {
            if (args.Length > 0 && args[0] == "silo-quality-fixture-ready") ready.TrySetResult();
            if (args.Length == 3 && args[0] == "silo-quality-fixture-state" && queries.TryRemove(args[1], out var response))
            { using var json = JsonDocument.Parse(args[2]); response.TrySetResult(json.RootElement.Clone()); }
        }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua"));
            var observed = Path.Combine(Program.ResultDirectory, "quality-observer.lua");
            await File.WriteAllTextAsync(observed, script + """

                mp.register_script_message('quality-fixture-query',function(id,kind)
                    compute_layout()
                    local r=state.layout[kind=='stats' and 'btn_stats' or 'btn_quality'] or {}
                    mp.commandv('script-message','silo-quality-fixture-state',id,require('mp.utils').format_json({x=r.cx or 0,y=r.cy or 0,w=r.w or 0,legacy=state.quality_menu_visible or false,pending=state.native_menu_request~=nil}))
                end)
                mp.commandv('script-message','silo-quality-fixture-ready')
                """);
            Command(mpv, "load-script", observed); await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + mediaPath); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog"); service.SetPaused(true);
            await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Generated video did not settle.");
            Call(service, "SendQualityInfoToOsc");
            if (mode is "version-subtitle-followup-failure" or "version-subtitle-followup-cancel" or "version-subtitle-followup-successor")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var originalSession = manager.CurrentSession;
                var originalFile = (long)Get(service, "_playbackFileGeneration")!;
                var originalError = service.ErrorMessage;
                wire.HoldReplacementStart = true;
                wire.ReplacementGrantsSubtitle = true;
                wire.HoldFailedReplan = true;
                wire.FailFirstReplanOnly = true;
                var versionMenu = await Open();
                Call(versionMenu, "Input", "home"); Call(versionMenu, "Input", "down"); Call(versionMenu, "Input", "activate");
                await wire.ReplacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // This accepted viewer intent is newer than the start request's
                // captured revision. The returned automatic grant needs a replan.
                manager.RecordLocalSubtitleSelection(null);
                wire.ReleaseReplacement.TrySetResult();
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!ReferenceEquals(manager.CurrentSession, originalSession),
                    "The fixture did not reach a post-adoption subtitle reconciliation.");
                object? successor = null;
                if (mode == "version-subtitle-followup-successor")
                    successor = Call(service, "BeginPlaybackBusy", true, (Action?)null);
                if (mode != "version-subtitle-followup-failure") service.SetWatchTogetherPlayback(true);
                wire.ReleaseReplan.TrySetResult();
                await wire.ReplanReplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (successor != null)
                {
                    await Task.Delay(250); await Query();
                    Require(service.IsLoading && (bool)Get(service, "_switchingContent")! &&
                        Call(service, "OwnsPlaybackBusy", successor) is true,
                        "A canceled replacement borrowed or cleared a successor's busy claim.");
                    Require(Call(service, "ReleasePlaybackBusy", successor, (Action?)null) != null,
                        "The actual successor could not release its own claim.");
                    Call(service, "SendSubtitleListToOsc");
                }
                await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")! &&
                    !(bool)Get(service, "_subtitleVersionSwitchActive")!,
                    "A failed/canceled post-adoption subtitle replan stranded its Version busy owner.");
                Require(Gate(service).CurrentCount == 1, "Version reconciliation leaked its native menu lease.");
                if (mode != "version-subtitle-followup-failure")
                {
                    Require((long)Get(service, "_playbackFileGeneration")! == originalFile &&
                        service.ErrorMessage == originalError && service.IsWatchTogetherPlayback && mpv.IsPaused,
                        "Canceled reconciliation loaded stale video, changed room pause or published a retired error.");
                }
                else
                {
                    Require((long)Get(service, "_playbackFileGeneration")! > originalFile ||
                        wire.Files.Count > 2 || Get(service, "_playbackTerminalState") != null,
                        "A post-adoption failure neither loaded/recovered its accepted replacement nor reached explicit terminal state.");
                }
                Program.Log($"PASS: actual native {mode} transfers its exact adopted response before the fallible follow-up; matched cleanup/recovery and successor isolation remain intact.");
                return;
            }
            if (mode == "quality-load-retry")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var originalOpen = Get(mpv, "_directOpen");
                Require(originalOpen != null, "Synthetic direct video did not register its native reader.");
                retainedDirectCallbacks.Add(originalOpen!);
                var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
                {
                    if (args.Exception is InvalidOperationException { Message: "Cannot register direct file reader." }) failed.TrySetResult();
                };
                var initialSession = manager.CurrentSession;
                AppDomain.CurrentDomain.FirstChanceException += observe;
                try
                {
                    Set(mpv, "_directOpen", null);
                    var selection = await Open(); SelectLastQuality(selection); Call(selection, "Input", "activate");
                    await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await Until(() => !service.IsLoading && !service.IsQualitySwitchActive && !(bool)Get(service, "_switchingContent")!,
                        "Quality load failure stranded its own busy state.");
                    Require(!ReferenceEquals(initialSession, manager.CurrentSession) && !string.IsNullOrEmpty(service.ErrorMessage),
                        "The controlled failure did not occur after real Quality decision adoption.");
                }
                finally
                {
                    AppDomain.CurrentDomain.FirstChanceException -= observe;
                    if (Get(mpv, "_directOpen") is { } attempted) retainedDirectCallbacks.Add(attempted);
                    Set(mpv, "_directOpen", originalOpen);
                }
                // No extra publication: reopening must use the failure path's new scope.
                var retry = await Open();
                Require(Gate(service).CurrentCount == 0 && mpv.IsPaused && service.IsPaused,
                    "Retry menu did not retain its lease and genuine pause intent.");
                Call(retry, "Input", "cancel");
                Require(Gate(service).CurrentCount == 1, "Retry menu leaked its lease.");
                Program.Log("PASS: actual accepted Quality synchronous native load failure republishes owned scopes and immediately reopens without another publication; genuine pause and lease cleanup remain intact.");
                return;
            }
            if (mode is "access-menu" or "picture-in-picture")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                if (mode == "picture-in-picture")
                {
                    Call(service, "RetireNativeMenus");
                    Property(service, "State", PlayerState.PictureInPicture);
                    Call(service, "SendQualityInfoToOsc");
                }
                var retained = await Open();
                var identity = api.CaptureIdentityContext(); var read = api.CaptureContext();
                Require(api.TryInvalidateAccessContext(read, out var refreshed) && refreshed != read && api.IsCurrentIdentityContext(identity),
                    "Fixture did not exercise a real access refresh with stable playback identity.");
                Call(retained, "Input", "down");
                await Task.Delay(150); await Query();
                Require(ReferenceEquals(Menu(service), retained) && Gate(service).CurrentCount == 0,
                    "Access refresh retired the open playback menu.");
                Call(retained, "Input", "cancel");
                var reopened = await Open();
                api.SetProfile("retired-playback-profile"); Call(reopened, "Input", "activate");
                await Until(() => Menu(service) == null, "A genuine profile change retained its menu.");
                Require(wire.Qualities.IsEmpty && Gate(service).CurrentCount == 1 && mpv.IsPaused,
                    "Retired profile dispatched a choice, leaked its lease or changed pause.");
                Call(service, "SendQualityInfoToOsc");
                var serverMenu = await Open();
                api.SetBaseUrl("https://retired-quality.fixture.invalid"); Call(serverMenu, "Input", "activate");
                await Until(() => Menu(service) == null, "A genuine server change retained its menu.");
                Require(wire.Qualities.IsEmpty && Gate(service).CurrentCount == 1 && mpv.IsPaused,
                    "Retired server dispatched a choice, leaked its lease or changed pause.");
                Program.Log($"PASS: actual {mode} native Quality retains access-refresh ownership, reopens without republication, and rejects genuine profile/server retirement; no physical PiP transition is asserted.");
                return;
            }
            if (mode is "legacy-access-version" or "legacy-access-quality")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var gate = (SemaphoreSlim)Get(service, "_transportRestartGate")!;
                var initialFile = (long)Get(service, "_playbackFileGeneration")!;
                var drainGeneration = (long)Get(service, "_seekRestartGeneration")!;
                await gate.WaitAsync();
                try
                {
                    var quality = mode == "legacy-access-quality";
                    mpv.SendScriptMessage(quality ? "silo-quality-select" : "silo-version-select", quality ? "720p" : "71");
                    await Until(() => (bool)Get(service, "_switchingContent")! &&
                        (long)Get(service, "_seekRestartGeneration")! > drainGeneration,
                        "Legacy selection did not enter its held transport drain.");
                    var identity = api.CaptureIdentityContext();
                    Require(api.TryInvalidateAccessContext(api.CaptureContext(), out _) && api.IsCurrentIdentityContext(identity),
                        "Legacy drain did not receive a stable-identity access refresh.");
                }
                finally { gate.Release(); }
                await Until(() => (long)Get(service, "_playbackFileGeneration")! > initialFile && !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                    "Access refresh discarded a legacy selection held before dispatch completion.");
                Require(mpv.IsPaused && service.IsPaused && !service.IsQualitySwitchActive,
                    "Legacy access-refresh selection lost pause or busy cleanup.");
                Program.Log($"PASS: actual OSC {mode} survives stable-identity access refresh during its real transport drain, loads the chosen file/quality and retains pause.");
                return;
            }
            if (mode is "access-version" or "access-quality")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var initialFile = (long)Get(service, "_playbackFileGeneration")!;
                wire.HoldReplacementStart = mode == "access-version";
                wire.HoldSuccessfulReplan = mode == "access-quality";
                var selection = await Open();
                if (mode == "access-version") { Call(selection, "Input", "home"); Call(selection, "Input", "down"); }
                else SelectLastQuality(selection);
                Call(selection, "Input", "activate");
                await (mode == "access-version" ? wire.ReplacementStarted.Task : wire.ReplanStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
                var identity = api.CaptureIdentityContext();
                Require(api.TryInvalidateAccessContext(api.CaptureContext(), out _) && api.IsCurrentIdentityContext(identity),
                    "Held switch did not receive a real stable-identity access refresh.");
                wire.ReleaseReplacement.TrySetResult(); wire.ReleaseReplan.TrySetResult();
                await Until(() => (long)Get(service, "_playbackFileGeneration")! > initialFile && !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                    "Access refresh discarded an accepted playback decision before loading it.");
                Require(mpv.IsPaused && service.IsPaused && Gate(service).CurrentCount == 1 &&
                    (mode == "access-version" ? manager.CurrentSession?.MediaFileId == 71 : service.SelectedQualityMenuId == "720p"),
                    "Access-refresh continuation lost its accepted selection, pause or menu lease.");
                Program.Log($"PASS: actual held native {mode} continues through access refresh and loads the accepted decision while retaining genuine pause and lease cleanup.");
                return;
            }
            if (mode is "version-invalid-url" or "quality-invalid-url")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var originalSession = manager.CurrentSession;
                var originalUrl = manager.StreamUrl;
                var originalFile = (long)Get(service, "_playbackFileGeneration")!;
                wire.InvalidDeliveryUrl = true;
                var invalidMenu = await Open();
                if (mode == "version-invalid-url")
                {
                    Call(invalidMenu, "Input", "home"); Call(invalidMenu, "Input", "down");
                }
                else
                {
                    Call(invalidMenu, "Input", "end");
                    if (((ListView)Get(invalidMenu, "_list")!).SelectedItem is ListViewItem { Tag: "speed" })
                        Call(invalidMenu, "Input", "up");
                }
                Call(invalidMenu, "Input", "activate");
                await Until(() => mode == "version-invalid-url" ? wire.Files.Count > 1 : !wire.Qualities.IsEmpty,
                    "Native menu did not request its malformed playback decision.");
                await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")! && !service.IsQualitySwitchActive,
                    "Invalid delivery URL stranded the menu's loading/switching owner.");
                Require(ReferenceEquals(manager.CurrentSession, originalSession) && manager.StreamUrl == originalUrl &&
                    (long)Get(service, "_playbackFileGeneration")! == originalFile,
                    "Invalid delivery URL partially adopted a session or loaded a new file.");
                Require(mpv.IsPaused && service.IsPaused && Gate(service).CurrentCount == 1,
                    "Invalid playback decision changed viewer pause or leaked the native menu lease.");
                Program.Log($"PASS: actual native {mode} preserves the accepted session/stream/file, releases busy state and retains genuine pause.");
                return;
            }
            if (mode == "version-room-race")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var originalSession = manager.CurrentSession;
                var originalFile = (long)Get(service, "_playbackFileGeneration")!;
                var originalError = service.ErrorMessage;
                wire.HoldReplacementStart = true;
                var versionMenu = await Open();
                Call(versionMenu, "Input", "home"); Call(versionMenu, "Input", "down"); Call(versionMenu, "Input", "activate");
                await wire.ReplacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                service.SetWatchTogetherPlayback(true);
                wire.ReleaseReplacement.TrySetResult();
                await wire.ReplacementReplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(300); await Query();
                await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                    "A retired solo version switch stranded busy state after joining the room.");
                Require((long)Get(service, "_playbackFileGeneration")! == originalFile && ReferenceEquals(manager.CurrentSession, originalSession),
                    "A solo version accepted before room attachment replaced the room's session/file afterward.");
                Require(service.IsWatchTogetherPlayback && mpv.IsPaused && service.ErrorMessage == originalError,
                    "A retired solo version switch changed room pause intent or published a stale error.");
                Program.Log("PASS: actual held native version replacement is canceled by same-file room attachment; late reply preserves the room session/file and pause.");
                return;
            }
            if (mode.StartsWith("legacy-", StringComparison.Ordinal))
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var transportGate = (SemaphoreSlim)Get(service, "_transportRestartGate")!;
                await transportGate.WaitAsync();
                var drainGeneration = (long)Get(service, "_seekRestartGeneration")!;
                var initialError = service.ErrorMessage;
                try
                {
                    var qualityAction = mode == "legacy-quality";
                    mpv.SendScriptMessage(qualityAction ? "silo-quality-select" : "silo-version-select", qualityAction ? "720p" : "71");
                    await Until(() => (bool)Get(service, "_switchingContent")! && (!qualityAction || service.IsQualitySwitchActive) &&
                        (long)Get(service, "_seekRestartGeneration")! > drainGeneration,
                        "Legacy dispatch did not enter its actual held transport drain.");
                    api.SetProfile("retired-during-legacy-drain");
                }
                finally { transportGate.Release(); }
                await Until(() => !(bool)Get(service, "_switchingContent")! && !service.IsLoading && !service.IsQualitySwitchActive,
                    "Retired legacy dispatch stranded its pre-dispatch busy claim.");
                Require(wire.Qualities.IsEmpty && wire.Files.Count == 1 && mpv.IsPaused && service.ErrorMessage == initialError,
                    "Retired legacy dispatch replanned, loaded a version or changed pause.");
                Program.Log($"PASS: actual OSC {mode} releases its pre-dispatch claim after profile retirement during the real transport drain, without stale replan/load.");
                return;
            }
            if (mode == "republish")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var retained = await Open();
                var publication = (long)Get(service, "_nativeMenuPublication")!;
                Call(service, "SendQualityInfoToOsc");
                await Until(() => (long)Get(service, "_nativeMenuPublication")! > publication, "Quality did not republish.");
                await Task.Delay(150); await Query();
                Require(ReferenceEquals(Menu(service), retained) && Gate(service).CurrentCount == 0,
                    "Unchanged media publication dismissed the current native Quality menu.");
                Call(retained, "Input", "cancel");
                Require(Gate(service).CurrentCount == 1 && mpv.IsPaused, "Retained Quality failed to release its lease or changed pause.");
                Program.Log("PASS: unchanged actual quality publication retains the same native menu, input lease and genuine pause.");
                return;
            }
            if (mode == "room-modes" || mode == "room-explanation")
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                var sameSession = manager.CurrentSession;
                var sameFile = (long)Get(service, "_playbackFileGeneration")!;
                var localMenu = await Open();
                service.SetWatchTogetherPlayback(true);
                if (mode == "room-modes")
                    await Until(() => Menu(service) == null, "Attaching this same file to a room retained enabled local version rows.");
                else Call(localMenu, "Input", "cancel");
                Call(service, "SendQualityInfoToOsc");
                var roomMenu = await Open();
                var roomList = (ListView)Get(roomMenu, "_list")!;
                Require(!roomList.Items.OfType<ListViewItem>().Any(row => row.Tag is string id && id.StartsWith("file:")),
                    "Room attachment retained local version actions.");
                const string explanation = "The room controls the source version. Streaming quality is your own choice.";
                var info = roomList.Items.OfType<ListViewItem>().Single(row => row.Tag is "room-version");
                var spoken = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(info);
                Require(spoken.Split(explanation, StringSplitOptions.None).Length == 2, "The room explanation is repeated in its native accessible row.");
                service.SetWatchTogetherPlayback(false);
                await Until(() => Menu(service) == null, "Leaving the room retained its locked version inventory.");
                var detachedMenu = await Open();
                Require(((ListView)Get(detachedMenu, "_list")!).Items.OfType<ListViewItem>().Any(row => row.Tag is "file:71" && row.IsEnabled),
                    "Leaving the room did not restore local version choices.");
                Require(ReferenceEquals(manager.CurrentSession, sameSession) && (long)Get(service, "_playbackFileGeneration")! == sameFile,
                    "The room permission fixture replaced the session or file.");
                Call(detachedMenu, "Input", "cancel");
                Program.Log("PASS: same-session/file room attachment and departure refresh native version permissions; its explanation appears once.");
                return;
            }
            if (mode.StartsWith("terminal", StringComparison.Ordinal))
            {
                await Until(() => Get(service, "_loadedMediaInitCts") == null, "Initial media publication did not settle.");
                await Open();
                var initialOwner = Get(service, "_nativeMenuOwner")!;
                Set(service, "_transportHasFirstOutput", true);
                Call(service, "EnterPlaybackTerminalState", new InvalidOperationException("Synthetic interrupted playback"),
                    0d, "quality-fixture", true, manager.CurrentSession);
                await Until(() => Menu(service) == null, "Terminal failure did not retire the existing menu.");
                if (mode != "terminal")
                {
                    var terminalFile = (long)Get(service, "_playbackFileGeneration")!;
                    Call(service, "OnScriptMessage", (object)(mode == "terminal-legacy-version"
                        ? new[] { "silo-version-select", "71" } : new[] { "silo-quality-select", "720p" }));
                    var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.DispatcherQueue.TryEnqueue(() => dispatched.TrySetResult());
                    await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await Task.Delay(250); await Query();
                    Require(Get(service, "_playbackTerminalState") != null && wire.Qualities.IsEmpty && wire.Files.Count == 1 &&
                        (long)Get(service, "_playbackFileGeneration")! == terminalFile && !service.IsLoading &&
                        !(bool)Get(service, "_switchingContent")! && !service.IsQualitySwitchActive,
                        "A delayed legacy selection replanned or claimed busy state above terminal Retry/Exit.");
                    Program.Log($"PASS: actual {mode} delivered after terminal failure leaves the accepted file, busy state and Retry/Exit intact.");
                    return;
                }
                Call(service, "SendQualityInfoToOsc");
                await Task.Delay(100);
                var currentOwner = Get(service, "_nativeMenuOwner") ?? initialOwner;
                var token = (string)currentOwner.GetType().GetProperty("Token")!.GetValue(currentOwner)!;
                Call(service, "OnScriptMessage", (object)new[] { "silo-native-quality-open", token, "999", "100", "100", "40", "40", "1280", "720" });
                await Task.Delay(200);
                Require(Menu(service) == null && Gate(service).CurrentCount == 1 && wire.Qualities.IsEmpty,
                    "A late publication/queued opener created native Quality above terminal Retry/Exit.");
                Program.Log("PASS: actual terminal transition retires the existing menu and rejects a late publication/queued Quality opener.");
                return;
            }
            if (mode is "successor-failure" or "successor-success")
            {
                using var cancellation = new CancellationTokenSource();
                var preparedTask = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession,
                    versions[0], manager.StreamUrl, 0d, CancellationToken.None)!;
                await preparedTask;
                Call(service, "ApplyPreparedTransport", preparedTask.GetType().GetProperty("Result")!.GetValue(preparedTask));
                wire.HoldFailedReplan = mode == "successor-failure";
                wire.HoldSuccessfulReplan = !wire.HoldFailedReplan;
                var initialFile = (long)Get(service, "_playbackFileGeneration")!;
                var initialError = service.ErrorMessage;
                var heldMenu = await Open(); Call(heldMenu, "Input", "end"); Call(heldMenu, "Input", "up");
                Require(((ListView)Get(heldMenu, "_list")!).SelectedItem is ListViewItem { Tag: "quality:720p" },
                    "Held successor case did not select the real server quality.");
                Call(heldMenu, "Input", "activate");
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                wire.HoldSuccessorReplan = true;
                var successor = (Task)Call(service, "RestartTransportForSeekAsync", 2d, false,
                    (long)Get(service, "_seekRestartGeneration")!, cancellation)!;
                await Until(() => ((SemaphoreSlim)Get(service, "_transportRestartGate")!).CurrentCount == 0,
                    "The real successor seek did not enter its transport gate.");
                wire.ReleaseReplan.TrySetResult();
                await wire.SuccessorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(500); await Query();
                Require(service.IsLoading && (bool)Get(service, "_switchingContent")! && !successor.IsCompleted,
                    "Quality completion cleared the held successor's loading/busy state.");
                Require((long)Get(service, "_playbackFileGeneration")! == initialFile && service.ErrorMessage == initialError,
                    "The superseded Quality attempt loaded media or published an error over its successor.");
                wire.ReleaseSuccessor.TrySetResult();
                await successor.WaitAsync(TimeSpan.FromSeconds(8));
                await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                    "The successor did not finish its own loading/busy lifecycle.");
                Require(mpv.IsPaused && service.IsPaused, "The successor changed genuine pause intent.");
                Program.Log($"PASS: actual native Quality {mode} retains a held same-owner seek's busy state, suppresses stale load/error, and allows its completion.");
                return;
            }
            if (mode == "release")
            {
                var claim = Call(service, "BeginPlaybackBusy", true, (Action?)null)!;
                var released = Call(service, "ReleasePlaybackBusy", claim, (Action?)null)!;
                Require(released != null && !service.IsLoading, "The current busy owner did not release.");
                var executed = 0;
                await QueuedRelease(() => executed++);
                Require(executed == 1, "A current cleanup receipt failed to execute its queued UI action.");
                var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.DispatcherQueue.TryEnqueue(() =>
                {
                    try { Call(service, "RunIfBusyReleaseCurrent", released, (Action)(() => executed++)); queued.TrySetResult(); }
                    catch (Exception error) { queued.TrySetException(error); }
                });
                // A successor can reuse every manager/session/file/token identity.
                // Even a repeated true assignment must retire old UI cleanup.
                Property(service, "IsLoading", true);
                await queued.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(executed == 1 && service.IsLoading, "Old queued UI cleanup ran after a successor loading assignment.");
                Property(service, "IsLoading", false); Call(service, "SetSwitchingContent", false);
                Program.Log("PASS: actual busy release receipt executes current queued cleanup and rejects a successor reusing the same playback identities.");
                return;

                async Task QueuedRelease(Action action)
                {
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.DispatcherQueue.TryEnqueue(() =>
                    {
                        try { Call(service, "RunIfBusyReleaseCurrent", released, action); done.TrySetResult(); }
                        catch (Exception error) { done.TrySetException(error); }
                    });
                    await done.Task.WaitAsync(TimeSpan.FromSeconds(4));
                }
            }
            if (mode == "retired")
            {
                wire.HoldFailedReplan = true;
                var initialFile = (long)Get(service, "_playbackFileGeneration")!;
                var pendingMenu = await Open(); Call(pendingMenu, "Input", "end"); Call(pendingMenu, "Input", "up");
                Require(((ListView)Get(pendingMenu, "_list")!).SelectedItem is ListViewItem { Tag: "quality:720p" },
                    "Retirement case did not select the real server quality.");
                Call(pendingMenu, "Input", "activate");
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(service.IsLoading && service.IsQualitySwitchActive, "The held quality action did not own busy state.");
                api.SetProfile("replacement"); wire.ReleaseReplan.TrySetResult();
                await wire.ReplanReplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => !service.IsLoading && !service.IsQualitySwitchActive && !(bool)Get(service, "_switchingContent")!,
                    "Retired quality replan stranded loading or switching state.");
                Require((long)Get(service, "_playbackFileGeneration")! == initialFile && mpv.IsPaused,
                    "Retired quality reply loaded a file or changed genuine pause.");
                Program.Log("PASS: actual held quality failure after profile retirement releases only its busy state, with no stale load or pause change.");
                return;
            }
            var menu = await Open();
            var list = (ListView)Get(menu, "_list")!;
            Require(list.Items.Count == 6 && list.Items.OfType<ListViewItem>().Count(row => row.Tag is string id && id != "speed") == 5
                && list.Items.OfType<ListViewItem>().Any(row => row.Tag is "speed" && row.IsEnabled),
                "Native settings omitted existing versions/server ladder/Auto or the Speed navigation row.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "quality-initial");
            Require(mpv.IsPaused && wire.Qualities.IsEmpty, "Opening Quality changed playback.");
            Require((await Query()).GetProperty("legacy").GetBoolean() == false, "Native and legacy quality overlap.");
            Call(menu, "Input", "end");
            Require(list.SelectedItem is ListViewItem { Tag: "speed" }, "End did not reach the Speed navigation row.");
            Call(menu, "Input", "up");
            Require(list.SelectedItem is ListViewItem { Tag: "quality:720p" }, "End did not reach the last server quality.");
            Call(menu, "Input", "activate");
            await Until(() => wire.Qualities.Contains("720p") && !service.IsLoading && !service.IsQualitySwitchActive,
                "Native quality did not finish the existing replan.");
            Require(mpv.IsPaused && service.IsPaused, "Quality replan resumed a genuinely paused viewer.");
            Require(service.SelectedQualityMenuId == "720p", "Accepted quality did not become current.");
            Require(Menu(service) == null && Gate(service).CurrentCount == 1, "Selection stranded the native lease.");
            Call(service, "StopPlaybackStallWatchdog");
            // A prior request may have fallen back to another accepted file.
            // Its Requested badge must not make the version impossible to retry.
            Set(service, "_requestedMediaFileId", 71);
            Call(service, "SendQualityInfoToOsc");

            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            Require(list.Items.OfType<ListViewItem>().Any(row => row.Tag is "quality:720p" &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(row).Contains("Current", StringComparison.Ordinal)),
                "Reopening Quality lost the accepted checkmark.");
            Call(menu, "Input", "home"); Call(menu, "Input", "down");
            Require(list.SelectedItem is ListViewItem { Tag: "file:71" }, "Version source ID was replaced by its row index.");
            service.SetPaused(false);
            await Until(() => !mpv.IsPaused && !service.IsPaused, "The viewer did not enter playing intent before selecting a version.");
            var versionLoad = (long)Get(service, "_playbackFileGeneration")!;
            Call(menu, "Input", "activate");
            await Until(() => wire.Files.Contains(71) && manager.CurrentSession?.MediaFileId == 71 &&
                (long)Get(service, "_playbackFileGeneration")! > versionLoad && !service.IsLoading,
                "Version selection did not use the existing replacement-session workflow.");
            await Until(() => !mpv.IsPaused && !service.IsPaused, "Version selection paused a playing viewer.");
            Call(service, "StopPlaybackStallWatchdog");

            Property(service, "IsWatchTogetherPlayback", true); Call(service, "SendQualityInfoToOsc");
            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            Require(!list.Items.OfType<ListViewItem>().Any(row => row.Tag is string id && id.StartsWith("file:")), "Room exposed source-version actions.");
            Require(list.Items.OfType<ListViewItem>().Any(row => row.Tag is "quality:720p" && row.IsEnabled), "Room lost viewer quality choices.");
            var retired = menu; api.SetProfile("replacement");
            var before = wire.Qualities.Count; Call(retired, "Input", "activate");
            await Until(() => Menu(service) == null, "Retired profile kept native Quality open.");
            Require(wire.Qualities.Count == before && Gate(service).CurrentCount == 1, "Retired profile dispatched quality or leaked its lease.");
            foreground.Observe("quality-complete");
            Program.Log("PASS: native Quality actual OSC/owned choices/server ladder, Auto and accepted selection, original file IDs, paused and playing replan intent, room version lock and stale-profile cleanup. Synthetic video/API only.");
        }
        finally
        {
            wire.ReleaseReplan.TrySetResult();
            wire.ReleaseSuccessor.TrySetResult();
            wire.ReleaseReplacement.TrySetResult();
            mpv.ScriptMessageReceived -= Message;
            if (Menu(service) is { } menu) Call(menu, "Input", "cancel");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_videoWindow", null); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            video.Dispose(); mpv.Dispose(); await Task.Delay(40); foreground.Complete();
            GC.KeepAlive(retainedDirectCallbacks);
        }

        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N"); var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = reply;
            try { mpv.SendScriptMessage("quality-fixture-query", id, "quality"); return await reply.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { queries.TryRemove(id, out _); }
        }
        void SelectLastQuality(object menu)
        {
            Call(menu, "Input", "end");
            if (((ListView)Get(menu, "_list")!).SelectedItem is ListViewItem { Tag: "speed" })
                Call(menu, "Input", "up");
        }
        async Task<object> Open()
        {
            mpv.SendScriptMessage("osc-show"); var geometry = await Query();
            Require(geometry.GetProperty("w").GetDouble() > 0, "Quality opener missing from actual OSC.");
            var x = geometry.GetProperty("x").ToString(); var y = geometry.GetProperty("y").ToString();
            mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
            mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
            await Until(() => Menu(service) is { } value && Get(value, "_list") is ListView { XamlRoot: not null },
                "Actual OSC Quality gesture produced no native menu.");
            var menu = Menu(service)!; var native = (Window)Get(menu, "_window")!;
            foreground.WatchMenu(native, WinRT.Interop.WindowNative.GetWindowHandle(native));
            foreground.Observe("quality-opened"); return menu;
        }
    }

    internal sealed class Wire(string origin, string mediaPath = "/video.y4m") : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Qualities = new();
        internal readonly ConcurrentQueue<int> Files = new();
        internal bool HoldFailedReplan;
        internal bool HoldSuccessfulReplan;
        internal bool HoldSuccessorReplan;
        internal bool HoldReplacementStart;
        internal bool InvalidDeliveryUrl;
        internal bool ReplacementGrantsSubtitle;
        internal bool FailFirstReplanOnly;
        internal readonly TaskCompletionSource ReplacementStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseReplacement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplacementReplied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SuccessorStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSuccessor = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanReplied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _file = 41;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "native-quality.fixture.invalid", "Quality fixture attempted external API networking.");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/settings/")) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1","plan_active_quality_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = json.RootElement;
            var quality = body.TryGetProperty("quality_preference", out var value) ? value.GetString() ?? "auto" : "auto";
            if (path.EndsWith("/start") && HoldReplacementStart)
            {
                ReplacementStarted.TrySetResult(); await ReleaseReplacement.Task; ReplacementReplied.TrySetResult();
            }
            if (path.EndsWith("/replan"))
            {
                Qualities.Enqueue(quality);
                if (HoldSuccessorReplan && Qualities.Count > 1)
                {
                    SuccessorStarted.TrySetResult(); await ReleaseSuccessor.Task;
                }
                else if (HoldFailedReplan && (!FailFirstReplanOnly || Qualities.Count == 1) || HoldSuccessfulReplan)
                {
                    ReplanStarted.TrySetResult(); await ReleaseReplan.Task; ReplanReplied.TrySetResult();
                    if (HoldFailedReplan)
                        return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":\"Synthetic retired quality failure\"}") };
                }
            }
            if (body.TryGetProperty("file_id", out var file) && int.TryParse(file.GetString(), out var requestedFile))
            { _file = requestedFile; Files.Enqueue(_file); }
            var grantSubtitle = ReplacementGrantsSubtitle && path.EndsWith("/start");
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "native-quality-fixture",
                server_features = new[] { "plan_active_quality_v1" },
                playback_plan = new
                {
                    protocol_version = 3, plan_id = "quality-plan-" + Qualities.Count + "-" + Files.Count,
                    plan_attempt_key = "synthetic", session_id = "native-quality-fixture", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = InvalidDeliveryUrl ? "" : origin + mediaPath },
                    timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = grantSubtitle ? (object)new { subtitle = new { id = "replacement-auto", index = 8 } } : new { },
                    subtitle = new { mode = grantSubtitle ? "sidecar" : "off", inventory = grantSubtitle
                        ? new object[] { new { track_id = "replacement-auto", combined_index = 8, language = "en", codec = "srt", source = "external", url = origin + "/replacement.srt" } }
                        : Array.Empty<object>() },
                    active_quality = quality == "auto" ? "original" : quality,
                    available_qualities = new[] { new { label = "1080p", display_name = "1080p", bitrate_kbps = 5000 }, new { label = "720p", display_name = "720p", bitrate_kbps = 2500 } },
                },
            }));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    private static object? Menu(PlayerService service) => Get(service, "_nativeMenu") ?? Get(service, "_nativeAudioMenu");
    private static SemaphoreSlim Gate(PlayerService service) => (SemaphoreSlim)Get(service, "_playbackDialogGate")!;
    private static object? Get(object target, string name)
    { for (var type = target.GetType(); type != null; type = type.BaseType) if (type.GetField(name, Private) is { } field) return field.GetValue(target); return null; }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Command(MpvPlayer player, params string[] args) => Require(Call(player, "Command", (object)args) is true, "Quality fixture native command failed.");
    private static void Require(bool success, string detail) { if (!success) throw new InvalidOperationException(detail); }
    private static async Task Until(Func<bool> ready, string failure)
    { var watch = Stopwatch.StartNew(); while (!ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(failure); await Task.Delay(20); } }
}
