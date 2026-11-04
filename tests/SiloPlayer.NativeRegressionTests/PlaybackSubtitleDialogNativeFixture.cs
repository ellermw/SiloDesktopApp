using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

internal static partial class PlaybackAudioMenuNativeFixture
{
    internal static async Task RunSubtitleDialogsAsync(Window outer)
    {
        using var foreground = new ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(outer), "subtitle-dialogs");
        // Reuse the established synthetic shell, including isolated settings,
        // disabled receiver and detached account startup. No installed app starts.
        var scopeType = typeof(EpisodeQuickPlayNativeFixture).GetNestedType("LifecycleScope", BindingFlags.NonPublic)!;
        var scope = Activator.CreateInstance(scopeType, nonPublic: true)!;
        T Scope<T>(string name) => (T)scopeType.GetProperty(name, Private | BindingFlags.Public)!.GetValue(scope)!;
        var window = Scope<MainWindow>("Window");
        var service = Scope<PlayerService>("Player");
        var api = Scope<SiloApiClient>("Client");
        var auth = Scope<AuthService>("Auth");
        var wire = Scope<object>("Wire");
        using var media = new MediaServer();
        using var manager = new PlaybackManager(Scope<PlaybackApi>("Playback"), Scope<CatalogApi>("Catalog"), auth, api);
        using var video = new MpvVideoWindow();
        using var mpv = new MpvPlayer();
        Action<string[]>? boundaryObserver = null;
        try
        {
            Set(wire, "AcceptStart", true);
            Set(wire, "SubtitleAiEnabled", true);
            window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1100, 800));
            window.AppWindow.Show(false);
            await Task.Delay(150);
            manager.UseWatchDetail(new() { ContentId = "subtitle-dialog", Type = "movie", Versions = [Version()] });
            await manager.StartSessionAsync(42);
            video.Create(WinRT.Interop.WindowNative.GetWindowHandle(window)); video.Show();
            mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv);
            mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
            Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager);
            Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "subtitle-dialog");
            Property(service, "Versions", manager.WatchDetail!.Versions);
            Call(service, "WireMpvEvents");
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult();
            mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog");
            await Until(() => !service.IsLoading, "Subtitle shell file did not settle.");
            service.SetPaused(true); await Until(() => mpv.IsPaused, "Subtitle shell pause did not settle.");
            manager.CurrentSession!.SubtitleUrls = [new()
            {
                Index = 1108, MediaFileId = 42, TrackId = "synthetic-subtitle", Language = "en",
                Source = "downloaded", Codec = "vtt", Label = "Synthetic English", Url = media.Origin + "/fixture-subtitle.vtt",
            }];
            Call(service, "SendSubtitleListToOsc");
            await (Task)Call(service, "SendSubtitleAiAvailabilityToOscAsync", CancellationToken.None)!;
            long sequence = 0;
            async Task Open()
            {
                Call(service, "PublishNativeMenuScopes");
                var owner = Get(service, "_nativeMenuOwner")!;
                var token = (string)owner.GetType().GetProperty("Token")!.GetValue(owner)!;
                var requestType = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuRequest")!;
                var anchorType = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuAnchor")!;
                var request = Activator.CreateInstance(requestType, [token, ++sequence, Activator.CreateInstance(anchorType, [.1d, .8d, .15d, .9d])])!;
                requestType.GetProperty("Kind", Private | BindingFlags.Public)!.SetValue(request, "subtitle");
                await (Task)Call(service, "HandleNativeMenuOpenAsync", request)!;
                Require(Menu(service) != null, "Actual Subtitle caller did not open for dialog handoff.");
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_DIALOG_CASE") == "retired-handoff")
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var completed = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                boundaryObserver = message =>
                {
                    if (message.FirstOrDefault() == "silo-retired-dialog-ready") ready.TrySetResult();
                    if (message.FirstOrDefault() == "silo-retired-dialog-completed") completed.TrySetResult(message);
                };
                mpv.ScriptMessageReceived += boundaryObserver;
                var probe = Path.Combine(Program.ResultDirectory, "retired-dialog-probe.lua");
                await File.WriteAllTextAsync(probe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-subtitle-dialog-closed', function(token, sequence, restore)
                        if token then mp.commandv('script-message','silo-retired-dialog-completed',token,sequence,restore) end
                    end)
                    mp.commandv('script-message','silo-retired-dialog-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                await Open();
                var request = Get(service, "_nativeMenuRequest")!;
                var token = request.GetType().GetProperty("OwnerToken")!.GetValue(request)?.ToString();
                var sequenceValue = request.GetType().GetProperty("Sequence")!.GetValue(request)?.ToString();
                var menuWindow = (Window)Get(Menu(service)!, "_window")!;
                var retiredInClose = false;
                menuWindow.Closed += (_, _) => { retiredInClose = true; api.SetProfile("retired-inside-close"); };
                Call(service, "InvokeNativeSubtitleAction", "add");
                Require(retiredInClose, "Native menu close callback did not exercise the controlled retirement boundary.");
                var completion = await completed.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(completion.Length == 4 && completion[1] == token && completion[2] == sequenceValue && completion[3] == "false" &&
                    Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                    "Retirement during menu close skipped scoped dialog completion or opened a stale modal.");
                Program.Log("PASS: context retirement inside native menu close still completes the exact dialog handoff and releases input without stale focus.");
                return;
            }
            foreach (var action in new[] { "add", "appearance", "ai" })
            {
                await Open();
                Call(service, "InvokeNativeSubtitleAction", action);
                await Until(() => Get(service, "_nativeSubtitleDialog") is ContentDialog,
                    "Subtitle " + action + " did not reach its actual authorized dialog.");
                var dialog = (ContentDialog)Get(service, "_nativeSubtitleDialog")!;
                await Until(() => dialog.IsLoaded && dialog.IsEnabled, "Background dialog did not restore its actual controls.");
                Require(Menu(service) == null && Gate(service).CurrentCount == 0,
                    "Native menu did not release its lease before the modal acquired it.");
                Require(dialog.XamlRoot != null && !ReferenceEquals(dialog.XamlRoot, ((FrameworkElement)window.Content).XamlRoot),
                    "Subtitle dialog lost its supplied player-host XamlRoot.");
                Require(mpv.IsPaused, "Subtitle modal opening changed genuine pause intent.");
                if (action == "ai")
                    Require(!(bool)dialog.GetType().GetProperty("FocusOnOpen")!.GetValue(dialog)!,
                        "Background AI dialog requested initial programmatic focus.");
                dialog.Hide();
                await Until(() => Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                    "Subtitle modal completion stranded the owned gate.");
                Require(mpv.IsPaused, "Subtitle modal completion changed genuine pause intent.");
                Program.Log("PASS: actual native Subtitle " + action + " handoff closes menu, acquires owned modal, retains pause and releases lease.");
            }
            await Open();
            Call(service, "InvokeNativeSubtitleAction", "add");
            await Until(() => Get(service, "_nativeSubtitleDialog") is ContentDialog,
                "Profile-retirement Search dialog did not open.");
            api.SetProfile("replacement-profile");
            Require(!(bool)typeof(PlayerService).GetProperty("NativeSubtitleAiAvailable", Private)!.GetValue(service)!,
                "AI capability from the previous profile remained available in the replacement context.");
            await Until(() => Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                "Profile replacement left a native Subtitle dialog or its lease alive.");
            Program.Log("PASS: profile replacement retires the actual native Subtitle modal without reviving its opener.");
        }
        finally
        {
            if (boundaryObserver != null) mpv.ScriptMessageReceived -= boundaryObserver;
            Call(service, "UnwireMpvEvents"); Call(service, "CancelPendingLoadedMediaInitialization");
            Call(service, "CancelPendingFileLoadTimeout"); Call(service, "StopPlaybackStallWatchdog");
            Call(service, "RetireNativeMenus"); Call(service, "StopSubtitleSync");
            Set(service, "_mpv", null); Set(service, "_videoWindow", null);
            await ((IAsyncDisposable)scope).DisposeAsync();
            await Task.Delay(120);
            foreground.Complete();
        }
    }
}
