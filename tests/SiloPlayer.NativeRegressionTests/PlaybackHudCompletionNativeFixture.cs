using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static partial class PlaybackHudCompletionNativeFixture
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync()
    {
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer();
        using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://hud-completion.fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "local"));
        var local = settings.Load(); local.SiloCastEnabled = false; settings.Save(local);
        var slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var main = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var original = slot.GetValue(null); var originalMain = main.GetValue(null);
        await using var provider = new ServiceCollection().AddSingleton(api).AddSingleton(auth).AddSingleton(settings).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<HomeApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        slot.SetValue(null, provider);
        var service = provider.GetRequiredService<PlayerService>();
        MainWindow? window = null;
        try
        {
            window = new MainWindow(); main.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", Flags)!.CreateDelegate(typeof(RoutedEventHandler), window);
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1440, 900)); window.AppWindow.Show(false);
            await Task.Delay(150);
            var mode = Environment.GetEnvironmentVariable("SILO_HUD_COMPLETION_CASE") ?? "postroll";
            wire.HasEpisodes=mode=="metadata";
            await service.PlayAsync("episode-1", fromStart: true);
            await Until(() => service.Mpv != null && !service.IsLoading && service.WatchDetail?.ContentId == "episode-1" && (bool)Field(service,"_transportHasFirstOutput")!, "Actual PlayAsync did not load the first episode.");
            service.Mpv!.SetProperty("ao", "null"); service.Mpv.SetProperty("loop-file", "inf");
            Call(service, "StopPlaybackStallWatchdog");
            await LoadProductionOsc(service.Mpv);
            Call(service,"SendMarkersToOsc"); await WaitForOscOwner(service);
            if (mode == "postroll")
            {
                service.SetNextEpisodeHint("episode-1", "episode-2", "The next episode", "Synthetic Show", null, "Next episode overview", runtimeSeconds: 30, seasonNumber: 1, episodeNumber: 2);
                var position = service.Mpv.Position;
                Call(window, "OnShowPlayingNextRequested", false);
                await Task.Delay(400);
                var video = (MpvVideoWindow)Field(service,"_videoWindow")!;
                var hero = (FrameworkElement)root.FindName("PlayingNextHero");
                Check(hero.ActualHeight<=Math.Min(root.XamlRoot.Size.Height*.3,300)+1,"Next episode artwork exceeds the official 30vh/300px cap.");
                var heroPoint = hero.TransformToVisual(root).TransformPoint(new(0,0));
                GetWindowRect(video.Hwnd, out var bounds);
                var clientOrigin = new Point(); ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(window), ref clientOrigin);
                var scale = root.XamlRoot.RasterizationScale;
                Check(bounds.Right <= clientOrigin.X + heroPoint.X * scale - 24, "Finishing video is not beside the next episode.");
                Check(bounds.Right-bounds.Left >= 500*scale && bounds.Top > clientOrigin.Y+100*scale, "Finishing video is still the obsolete corner thumbnail.");
                Check(IsWindowVisible(video.Hwnd) && service.Mpv.Position>position && !service.Mpv.IsPaused, "Post-roll interrupted live finishing playback.");
                await Capture(root,"postroll-large.png",service);
                Program.Log("TRACE: resizing the live post-roll composition.");
                window.AppWindow.ResizeClient(new(800,700)); Program.Log("TRACE: resize returned."); await Task.Delay(300);
                GetWindowRect(video.Hwnd,out bounds);
                heroPoint=hero.TransformToVisual(root).TransformPoint(new(0,0));
                Check(bounds.Bottom<=clientOrigin.Y+heroPoint.Y*scale-8, "Compact finishing preview covers next-item actions or artwork.");
                await Capture(root,"postroll-small.png",service);
                var play=(FrameworkElement)root.FindName("PlayingNextPlayNowButton"); play.StartBringIntoView(); await Task.Delay(250);
                var playPoint=play.TransformToVisual(root).TransformPoint(new(0,0));
                Check(playPoint.Y>=bounds.Bottom-clientOrigin.Y && playPoint.Y+play.ActualHeight<=root.ActualHeight,"Compact next action cannot be brought into view clear of the live HWND.");
                await Capture(root,"postroll-small-actions.png",service);
                Call(window,"OnShowPlayingNextRequested",true); await Task.Delay(100);
                Check(!IsWindowVisible(video.Hwnd), "EOF left the native popup over the next-item actions.");
                await Capture(root,"postroll-ended.png",service);
            }
            else if(mode=="reload-next")
            {
                service.SetNextEpisodeHint(service.ContentId!,"episode-2","Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:2);
                var starts=wire.Starts;
                var load=(long)Field(service,"_fileLoadGeneration")!;
                var play=(long)Field(service,"_playRequestGeneration")!;
                Call(service,"SetSwitchingContent",true);
                typeof(PlayerService).GetProperty(nameof(PlayerService.IsLoading))!.SetValue(service,true);
                typeof(PlayerService).GetField("_fileLoadGeneration",Flags)!.SetValue(service,load+1);
                Call(service,"OnScriptMessage",(object)new[]{"silo-next-episode","viewer",service.ContentId!,load.ToString(),play.ToString()});
                await Until(()=>wire.Starts==starts+1 && service.ContentId=="episode-2" && !service.IsLoading,
                    "A current viewer Next intent was blocked by a continuing transport reload.");
                service.SetNextEpisodeHint(service.ContentId!,"episode-3","Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:3);
                starts=wire.Starts; load=(long)Field(service,"_fileLoadGeneration")!; play=(long)Field(service,"_playRequestGeneration")!;
                typeof(PlayerService).GetField("_playRequestGeneration",Flags)!.SetValue(service,play+1);
                Call(service,"OnScriptMessage",(object)new[]{"silo-next-episode","viewer",service.ContentId!,load.ToString(),play.ToString()});
                await Task.Delay(250);
                Check(wire.Starts==starts,"Outgoing Lua intent overrode a newer playback preparation.");
            }
            else if(mode=="stale-timer")
            {
                service.SetNextEpisodeHint(service.ContentId!,"episode-2","Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:2);
                Call(window,"OnShowPlayingNextRequested",true); await Task.Delay(120);
                var timer=Field(window,"_playingNextTimer");
                Check(timer!=null,"Ended screen did not start its real countdown.");
                Call(window,"StopPlayingNextCountdown");
                typeof(MainWindow).GetField("_playingNextRemaining",Flags)!.SetValue(window,1);
                var starts=wire.Starts;
                Call(window,"PlayingNextTimer_Tick",timer,new object()); await Task.Delay(300);
                Check(wire.Starts==starts,"A retired ended timer advanced the current episode.");
            }
            else if(mode=="stale-wire")
            {
                service.SetNextEpisodeHint(service.ContentId!,"episode-2","Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:2);
                var starts=wire.Starts;
                var play=(long)Field(service,"_playRequestGeneration")!;
                Call(service,"OnScriptMessage",(object)new[]{"silo-next-episode","automatic","retired-episode","1",play.ToString()});
                await Task.Delay(200);
                Check(wire.Starts==starts,"Late message from an outgoing native load advanced current playback.");
                var load=(long)Field(service,"_fileLoadGeneration")!;
                Call(service,"OnScriptMessage",(object)new[]{"silo-next-episode","automatic",service.ContentId!, (load-1).ToString(System.Globalization.CultureInfo.InvariantCulture),play.ToString()});
                await Task.Delay(200); Check(wire.Starts==starts,"Retired load of the same content advanced its successor.");
            }
            else if(mode=="stale-credits")
            {
                service.SetNextEpisodeHint(service.ContentId!,"episode-2","Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:2);
                Call(service,"OnScriptMessage",(object)new[]{"silo-next-episode","automatic"});
                // Accepted viewer preparation increments this production owner
                // before the dispatcher can deliver the old Lua intent.
                var generation=(long)Field(service,"_playRequestGeneration")!;
                typeof(PlayerService).GetField("_playRequestGeneration",Flags)!.SetValue(service,generation+1);
                var starts=wire.Starts; await Task.Delay(300);
                Check(wire.Starts==starts,"Retired credits intent advanced a newer preparation owner.");
            }
            else if(mode=="metadata")
            {
                await (Task)Call(service,"AutoDetectNextEpisodeAsync",CancellationToken.None)!;
                var data=await QueryOsc(service.Mpv!);
                Check(data.GetProperty("next_title").GetString()=="Raw episode title","Credits title duplicated the formatted season/episode navigation label.");
            }
            else if(mode=="still") await VerifyStill(service,wire,provider.GetRequiredService<EpisodeSpoilerPreferences>());
            else if(mode=="cover") await VerifyCover(service);
            else await VerifyAutoplay(service, wire, api, window);
            Program.Log("PASS: HUD_COMPLETION_"+mode+" actual PlaybackManager/PlayerService and native controls.");
        }
        finally
        {
            if (service.State != PlayerState.Idle) await service.CloseAsync();
            Call(service,"ResetFailedMpvInitialization");
            window?.Close(); await Task.Delay(100); main.SetValue(null,originalMain); slot.SetValue(null,original);
        }
    }

    private static async Task VerifyAutoplay(PlayerService service, Wire wire, SiloApiClient api, MainWindow window)
    {
        service.SetStillWatchingThreshold(1);
        void Hint(string next) => service.SetNextEpisodeHint(service.ContentId!,next,"Next","Synthetic Show",null,null,seasonNumber:1,episodeNumber:2);
        async Task Loaded(string id) { await Until(()=>service.ContentId==id && !service.IsLoading && service.Mpv?.GetPropertyFlag("idle-active")==false && (bool)Field(service,"_transportHasFirstOutput")!,"Successor did not finish loading: "+id); Call(service,"StopPlaybackStallWatchdog"); await WaitForOscOwner(service); }
        Hint("episode-2");
        await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic); await Loaded("episode-2");
        Check(service.IsStillWatchingGated,"Automatic successor reset the unattended streak.");
        Hint("episode-3"); var starts=wire.Starts;
        Call(service,"PublishCreditsEpisodeDetail",false); service.Mpv!.SendScriptMessage("completion-expire-credits"); await Task.Delay(150);
        Check(wire.Starts==starts && service.ContentId=="episode-2","Gated credits advanced through PlayerService.");
        await service.ContinuePlayingNextAsync(); await Loaded("episode-3");
        Check(!service.IsStillWatchingGated,"Viewer Play Now did not reset protection.");
        Hint("episode-4");
        Call(service,"PublishCreditsEpisodeDetail",false); service.Mpv!.SendScriptMessage("completion-expire-credits"); await Loaded("episode-4");
        Check(service.IsStillWatchingGated,"Credits intent did not count through the real service/manager.");
        service.SetPaused(true); Check(service.IsStillWatchingGated,"Internal pause was mistaken for viewer input.");
        service.RequestUserPauseToggle(); Check(!service.IsStillWatchingGated,"Viewer pause did not reset protection.");
        service.SetPaused(false); Hint("episode-5");
        await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic); await Loaded("episode-5");
        Hint("episode-6"); starts=wire.Starts;
        service.Mpv!.SetProperty("loop-file","no"); service.Mpv.Seek(29.8);
        await Until(()=>service.IsPostRollVideoEnded,"Actual native EOF never reached the ended screen.");
        Check(service.IsStillWatchingGated && Field(window,"_playingNextTimer")==null && wire.Starts==starts,"Gated native EOF started a countdown or successor.");
        await service.ContinuePlayingNextAsync(); await Loaded("episode-6");
        service.SetStillWatchingThreshold(0); Hint("episode-7");
        service.Mpv!.SetProperty("loop-file","no"); service.Mpv.Seek(29.8);
        await Until(()=>Field(window,"_playingNextTimer")!=null && service.IsPostRollVideoEnded,"Ungated native EOF did not start the real ended timer.");
        var timer=Field(window,"_playingNextTimer");
        typeof(MainWindow).GetField("_playingNextRemaining",Flags)!.SetValue(window,1);
        Call(window,"PlayingNextTimer_Tick",timer,new object()); await Loaded("episode-7");
        starts=wire.Starts; Hint("episode-8"); Call(window,"PlayingNextTimer_Tick",timer,new object()); await Task.Delay(150);
        Check(wire.Starts==starts,"Old EOF callback double-advanced the successor.");
        service.SetWatchTogetherPlayback(true); starts=wire.Starts;
        await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic); await service.ContinuePlayingNextAsync();
        Check(wire.Starts==starts && !service.IsStillWatchingGated,"Watch Party advanced or counted locally.");
        service.SetWatchTogetherPlayback(false);
        wire.FailStart=true; await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic); wire.FailStart=false;
        Check(wire.Starts==starts+1 && !string.IsNullOrEmpty(service.ErrorMessage),"Failed automatic plan lost its production error or retried silently.");
        await service.PlayAsync("manual-recovery",fromStart:true); await Loaded("manual-recovery");
        service.SetStillWatchingThreshold(1);
        await service.StartShuffleAsync(new("library","7")); await Loaded("shuffle-1");
        Check(!service.IsStillWatchingGated,"Viewer shuffle start retained an unattended streak.");
        await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic); await Loaded("shuffle-2");
        Check(service.IsStillWatchingGated && wire.Advances==1,"Automatic shuffle pick did not count exactly once.");
        starts=wire.Starts; await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic);
        Check(wire.Starts==starts && wire.Advances==1,"Gated shuffle advanced on the server.");
        await service.ContinuePlayingNextAsync(); await Loaded("shuffle-3"); Check(!service.IsStillWatchingGated,"Manual shuffle advance did not reset protection.");
        wire.FailAdvance=true; await service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic); wire.FailAdvance=false;
        Check(service.ContentId=="shuffle-3" && service.ErrorMessage?.Contains("shuffle")==true,"Failed shuffle did not retain current media and feedback.");
        await service.ContinuePlayingNextAsync(); await Loaded("shuffle-4");
        wire.HoldAdvance=true; starts=wire.Starts;
        var held=service.ContinuePlayingNextAsync(PlaybackStartTrigger.Automatic);
        await wire.AdvanceEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await service.CloseAsync(); wire.AdvanceRelease.TrySetResult(); await held;
        Check(wire.Starts==starts && service.State==PlayerState.Idle,"Retired shuffle result started playback after close.");
        Check(wire.WatchQueries.Count>0 && wire.WatchQueries.All(q=>System.Text.RegularExpressions.Regex.IsMatch(q,@"[?&]backdrop_width=\d+")),"A real watch request omitted the display-sized backdrop query.");
        Program.Log("PASS: native manual/credits/EOF/default/off/gate/reset/failure/shuffle/retirement/WatchParty wiring with actual PlaybackManager HTTP plans and libmpv.");
    }

    private static async Task VerifyCover(PlayerService service)
    {
        var mpv=service.Mpv!;
        Call(SiloPlayer.App.MainWindowInstance!,"OnPostRollReturnRequested"); await Task.Delay(100);
        ((MpvVideoWindow)Field(service,"_videoWindow")!).MatchParentPosition();
        mpv.SetProperty("pause","yes"); mpv.SendScriptMessage("osc-set-post-roll","false"); mpv.SendScriptMessage("osc-set-visibility","true"); mpv.SendScriptMessage("osc-set-loading","true");
        var type=typeof(PlayerService).GetNestedType("LoadingCoverOwner",BindingFlags.NonPublic)!;
        var owner=Activator.CreateInstance(type,Flags|BindingFlags.Public,null,new object[]{"fixture-cover",service.ContentId!,new CancellationTokenSource()},null)!;
        typeof(PlayerService).GetField("_loadingCover",Flags)!.SetValue(service,owner);
        GetWindowRect(((MpvVideoWindow)Field(service,"_videoWindow")!).Hwnd,out var coverBounds);
        type.GetField("FrameWidth")!.SetValue(owner,coverBounds.Right-coverBounds.Left); type.GetField("FrameHeight")!.SetValue(owner,coverBounds.Bottom-coverBounds.Top);
        var publish=typeof(PlayerService).GetMethod("PublishLoadingCoverAsync",Flags)!;
        Task Publish(byte[] pixels,bool fade)
        {
            object?[] args=publish.GetParameters().Length==7 ? [owner,pixels,160,90,640,CancellationToken.None,fade] : [owner,pixels,160,90,640,CancellationToken.None];
            return (Task)publish.Invoke(service,args)!;
        }
        var placeholder=new byte[160*90*4]; var target=new byte[placeholder.Length];
        for(var i=0;i<160*90;i++) { placeholder[i*4+2]=100; placeholder[i*4+3]=255; target[i*4]=240;target[i*4+3]=255; }
        await Publish(placeholder,false); await Task.Delay(80);
        var clock=Stopwatch.StartNew(); var fadeTask=Publish(target,true);
        await Task.Delay(180); await Shot("cover-fade-mid.png");
        await fadeTask; clock.Stop(); await Task.Delay(80); await Shot("cover-fade-final.png");
        var probe=await QueryOsc(mpv); Check(probe.GetProperty("cover").GetBoolean(),"Actual OSC did not display the host cover.");
        var mid=await ReadPixel("cover-fade-mid.png"); var final=await ReadPixel("cover-fade-final.png");
        Check(final.B>35 && final.R<5 && mid.R>final.R && mid.B<final.B,"Native cover pixels did not interpolate from the placeholder into the image.");
        Check(clock.ElapsedMilliseconds>=480,"OSC image swapped immediately instead of fading over 500ms.");
        var files=(System.Collections.ICollection)type.GetField("Files")!.GetValue(owner)!;
        Check(files.Count==2,"Cover retained an unbounded sequence of bitmap files.");
        var frames=type.GetField("PublishedFrames"); Check(frames!=null && (int)frames.GetValue(owner)!>=5,"Host did not publish intermediate fade frames.");
        Call(service,"RetireLoadingCover"); await Task.Delay(150);
        Check(type.GetField("Pixels")!.GetValue(owner)==null && type.GetField("Composed")!.GetValue(owner)==null,"Retired cover retained full-size buffers.");
        mpv.SendScriptMessage("osc-set-loading","false"); await Shot("cover-retired.png");
        Program.Log("PASS: actual host/OSC 500ms cover fade, two reusable raw files, intermediate native image and retired buffers.");
        async Task<(byte R,byte B)> ReadPixel(string name)
        {
            var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(Program.ResultDirectory,name));
            using var stream=await file.OpenReadAsync(); var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var pixels=(await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,new(),Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var i=((int)(decoder.PixelHeight/4)*(int)decoder.PixelWidth+(int)decoder.PixelWidth/4)*4; return(pixels[i+2],pixels[i]);
        }
        async Task Shot(string name)
        {
            var command=typeof(MpvPlayer).GetMethod("Command",Flags)!;
            Check(command.Invoke(mpv,[(object)new[]{"screenshot-to-file",Path.Combine(Program.ResultDirectory,name),"window"}]) is true,"Native cover screenshot failed."); await Task.Delay(20);
        }
    }

    private static async Task Capture(FrameworkElement element,string name,PlayerService? service=null)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var buffer=await bitmap.GetPixelsAsync();
        var pixels=buffer.ToArray();
        if(service!=null) await CompositeVideo(element,bitmap.PixelWidth,bitmap.PixelHeight,pixels,service,name);
        using var output=File.Create(Path.Combine(Program.ResultDirectory,name));
        using var random=output.AsRandomAccessStream();
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,random);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels); await encoder.FlushAsync();
    }
    private static async Task Until(Func<bool> predicate,string message) { var until=DateTime.UtcNow.AddSeconds(12); while(!predicate()&&DateTime.UtcNow<until) await Task.Delay(40); Check(predicate(),message); }
    private static void Check(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static object? Field(object target,string name)=>target.GetType().GetField(name,Flags)!.GetValue(target);
    private static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Flags)!.Invoke(target,args);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd,ref Point point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        internal int Stops, Starts, Advances;
        internal bool FailStart, FailAdvance, HoldAdvance, HasEpisodes;
        internal bool HideImages=true, HoldStill;
        internal byte[] StillPng=[];
        internal readonly TaskCompletionSource StillEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),StillRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> WatchQueries=[];
        internal readonly TaskCompletionSource AdvanceEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),AdvanceRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<double> Progress = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "hud-completion.fixture.invalid", "Unexpected fixture origin.");
            var path = request.RequestUri.AbsolutePath;
            if(path.EndsWith(".png"))
            {
                if(HoldStill) { StillEntered.TrySetResult(); await StillRelease.Task; }
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(StillPng)};
            }
            if(path=="/api/v2/settings/contract/capabilities") return Json(new {api_version=1,manifest_revision=17,supports_batched_effective=true});
            if(path=="/api/v2/settings/values/effective") return Json(new {items=new[]{new {key="catalog.hide_unwatched_episode_images",value=HideImages}}});
            if (path.EndsWith("/capabilities") && path.Contains("/playback/")) return Json(new { allowed = true, state = "available", installation_id = "fixture-install", protocol_versions = new[] { 3 }, features = new[] { "fixed_media_file_v1" } });
            if(path.Contains("/shuffles"))
            {
                if(request.Method==HttpMethod.Delete) return Json(new{});
                if(path.EndsWith("/advance"))
                {
                    Advances++;
                    if(HoldAdvance) { AdvanceEntered.TrySetResult(); await AdvanceRelease.Task; }
                    if(FailAdvance) { Advances--; return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{}")} ; }
                }
                return Json(new {id="fixture-shuffle",scope=new{kind="library",id="7",title="Synthetic library"},
                    current=new{content_id="shuffle-"+(Advances+1),type="episode",title="Shuffle episode"},
                    next=new{content_id="shuffle-"+(Advances+2),type="episode",title="Next shuffle episode"}});
            }
            if(HasEpisodes && path.EndsWith("/series/series/seasons")) return Json(new {items=new[]{new {season_number=1}}});
            if(HasEpisodes && path.EndsWith("/series/series/seasons/1/episodes")) return Json(new {items=new[]{
                new{content_id="episode-1",season_number=1,episode_number=1,title="Finishing episode"},
                new{content_id="episode-2",season_number=1,episode_number=2,title="Raw episode title"}}});
            if (path.Contains("/watch/"))
            {
                WatchQueries.Add(request.RequestUri.Query);
                var id=path.Split('/').Last();
                return Json(new { content_id=id,type="episode",title="Finishing episode",series_id="series",series_title="Synthetic Show",season_number=1,episode_number=1,
                    versions=new[] {new {file_id=42,duration=30,container="y4m",codec_video="rawvideo"}} });
            }
            if (path.EndsWith("/progress"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Progress.Add(json.RootElement.GetProperty("position").GetDouble()); return Json(new { outcome = "applied" });
            }
            if (request.Method == HttpMethod.Delete) { Interlocked.Increment(ref Stops); return Json(new { outcome = "stopped" }); }
            if (path.EndsWith("/start"))
            {
                ++Starts;
                if(FailStart) return new(HttpStatusCode.Forbidden){Content=new StringContent("{\"code\":\"denied\"}")};
                return Json(new { protocol_version = 3, outcome = "playable", session_id = "fixture-session", playback_plan = new { plan_id = "fixture-plan", plan_attempt_key = "fixture-key", session_id = "fixture-session", effective_media_file_id = 42, delivery = "original_http", active_quality = "original", stream = new { url = origin + "/video.y4m", protocol = "http_progressive" }, timeline = new { can_seek_anywhere = true }, source = new { bitrate_kbps = 1536, duration_seconds = 30 }, selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } }, subtitle = new { mode = "off", inventory = Array.Empty<object>() } } });
            }
            return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), profiles = Array.Empty<object>(), sections = Array.Empty<object>() });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
