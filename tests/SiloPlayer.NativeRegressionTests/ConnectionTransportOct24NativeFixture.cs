using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class ConnectionTransportOct24NativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var media=new MediaServer(); using var wire=new Wire(media.Origin); using var http=new HttpClient(wire);
        var api=new SiloApiClient(http); api.SetBaseUrl("https://connection-transport-fixture.invalid");
        using var auth=new AuthService(api,new AuthApi(api)); auth.SetTokens("fixture-access","fixture-refresh",3600);
        auth.SetCurrentUser(new(){Id="fixture",Username="Fixture",Role="user"}); auth.SelectProfile("fixture",profile:new(){Id="fixture",Name="Fixture",IsPrimary=true});
        var settings=new SettingsService(Path.Combine(Program.ResultDirectory,"connection-transport-settings"));
        var local=settings.Load(); local.SiloCastEnabled=false; settings.Save(local);
        var slot=typeof(App).GetField("_services",BindingFlags.NonPublic|BindingFlags.Static)!;
        var main=typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var original=slot.GetValue(null); var originalMain=main.GetValue(null);
        await using var provider=new ServiceCollection().AddSingleton(api).AddSingleton(auth).AddSingleton(settings).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory,"connection-transport-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        slot.SetValue(null,provider);
        MainWindow? window=null; MpvPlayer? mpv=null;
        using var traceStop=new CancellationTokenSource();Task? traceTask=null;var phase="setup";var traceClock=Stopwatch.StartNew();
        var disposedReads=new ConcurrentQueue<object>();
        void DisposedRead(object? sender,FirstChanceExceptionEventArgs args)
        {
            if(args.Exception is not ObjectDisposedException error||disposedReads.Count>=64)return;
            var stack=error.StackTrace??"";
            if(stack.Contains("DirectHttpReader",StringComparison.Ordinal)||stack.Contains("System.Net.Http.",StringComparison.Ordinal))
                disposedReads.Enqueue(new{elapsed=traceClock.ElapsedMilliseconds,phase,error.ObjectName,stack});
        }
        AppDomain.CurrentDomain.FirstChanceException+=DisposedRead;
        var service=provider.GetRequiredService<PlayerService>(); var playback=provider.GetRequiredService<PlaybackApi>();
        using var manager=new PlaybackManager(playback,provider.GetRequiredService<CatalogApi>(),auth,api){SourceVersionPinned=false};
        try
        {
            window=new MainWindow(); main.SetValue(null,window);
            var root=(FrameworkElement)window.Content;
            var nav=(NavigationView)root.FindName("NavView");
            nav.Loaded-=(RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded",Flags)!.CreateDelegate(typeof(RoutedEventHandler),window);
            window.AppWindow.Move(new(-20000,-20000)); window.AppWindow.ResizeClient(new(900,700)); window.AppWindow.Show(false);
            await Task.Delay(120);
            manager.UseWatchDetail(new(){ContentId="fixture-movie",Type="movie",Versions=[Version(42,1536),Version(43,512)]});
            await manager.StartSessionAsync(42);
            var nativeMpv=NativeLibrary.Load(Path.Combine(Program.AppDirectory,"libmpv-2.dll"));
            NativeLibrary.SetDllImportResolver(typeof(MpvPlayer).Assembly,(name,_,_)=>name.Equals("libmpv-2.dll",StringComparison.OrdinalIgnoreCase)?nativeMpv:0);
            mpv=new MpvPlayer(); mpv.Initialize(16,16); mpv.SetProperty("ao","null"); mpv.SetProperty("video","no");
            var oscReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var oscKeeps=0;var oscBacks=0;
            mpv.ScriptMessageReceived+=args=>{if(args.Length==0)return;if(args[0]=="silo-buffering-obscured")oscReady.TrySetResult();if(args[0]=="silo-buffering-keep")Interlocked.Increment(ref oscKeeps);if(args[0]=="silo-escape-unhandled")Interlocked.Increment(ref oscBacks);};
            Call(mpv,"Command",(object)new[]{"load-script",Path.Combine(Program.AppDirectory,"libs","mpv","scripts","silo-osc.lua")});
            await oscReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Set(service,"_mpv",mpv); Set(service,"_playbackManager",manager);
            Property(service,"State",PlayerState.Expanded); Property(service,"ContentId","fixture-movie"); Property(service,"Versions",manager.WatchDetail!.Versions);
            Call(service,"WireMpvEvents");
            var preparedType=typeof(PlayerService).GetNestedType("PreparedPlaybackTransport",BindingFlags.NonPublic)!;
            var url=media.Origin+"/stream/direct/slow";
            var prepared=Activator.CreateInstance(preparedType,Flags|BindingFlags.Public,null,
                [PlaybackTransportPlanner.Plan("direct",url),url,0d,0d,0d,(double?)90d,true],null)!;
            Call(service,"ApplyPreparedTransport",prepared); Call(service,"BeginMpvLoad",prepared,false);
            phase="initial-load";
            traceTask=Task.Run(async()=>
            {
                using var trace=new StreamWriter(Path.Combine(Program.ResultDirectory,"connection-observations.jsonl")){AutoFlush=true};
                while(!traceStop.IsCancellationRequested)
                {
                    try{await trace.WriteLineAsync(JsonSerializer.Serialize(Observation(service,mpv,media,phase,traceClock.ElapsedMilliseconds,wire.Replans.Count),new JsonSerializerOptions{NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));}
                    catch(Exception ex){await trace.WriteLineAsync(JsonSerializer.Serialize(new{elapsed=traceClock.ElapsedMilliseconds,phase,snapshotError=ex.GetType().Name}));}
                    try{await Task.Delay(250,traceStop.Token);}catch(OperationCanceledException){break;}
                }
            });
            await Until(()=>mpv.ReadMediaNetworkObservation().Bytes is >0,8,"Native media reader received no real bytes.");
            var first=mpv.ReadMediaNetworkObservation();
            Check(first.Bytes>0,"Signed direct media did not use the native HTTP counter.");
            await Until(()=>!service.IsLoading,10,"Real WAV media did not load.");
            phase="intentional-pause";service.SetPaused(true); await Task.Delay(1200); Call(service,"ObserveBufferingQuality");
            Check(wire.Replans.Count==0&&mpv.GetPropertyFlag("pause"),"Intentional native pause generated buffering recovery.");
            service.SetPaused(false);
            phase="intentional-seek";service.RequestUserSeek(10); await Task.Delay(250); Call(service,"ObserveBufferingQuality");
            Check(wire.Replans.Count==0,"Intentional native seek generated buffering recovery.");
            phase="outage";media.Silent=true;
            await Task.Delay(4600); Call(service,"ObserveBufferingQuality");
            var silent=mpv.ReadMediaNetworkObservation();
            Check(silent.SinceLastByteMs>=4000,"Loopback outage did not stop actual media bytes.");
            Check(wire.Replans.Count==0,"A silent native outage was treated as a slow connection.");
            media.Silent=false;
            // Guard checks and quality detection have independent native loads.
            // The seek/outage exercise must not spend the next load's HTTP
            // progress budget or leave its decoder waiting for seek preroll.
            if(Environment.GetEnvironmentVariable("SILO_OCT24_CONNECTION_ORIGINAL_FLOW")!="1")
            {
                phase="guard-teardown";
                Set(service,"_switchingContent",true);
                Call(service,"EndBufferingQuality");Call(service,"StopPlaybackStallWatchdog");
                Call(service,"CancelPendingFileLoadTimeout");mpv.Stop();await manager.StopSessionAsync();
                media.QualityPhase=true;await manager.StartSessionAsync(42);
                phase="quality-load";Call(service,"ApplyPreparedTransport",prepared);Call(service,"BeginMpvLoad",prepared,false);
                await Until(()=>!service.IsLoading,8,"Fresh quality scenario did not load its bootstrapped native media.");
                Program.Log("TRACE: fresh native quality replay uses a one-MiB WAV bootstrap, natural HTTP responses and the real five-second reader progress deadline.");
            }
            else Program.Log("TRACE: original uncapped native transport replay, including the preserved HTTP cancellation race.");
            phase="healthy-trickle";
            var observed=Stopwatch.StartNew();
            await Until(()=>wire.Replans.Count>0,35,"Actual trickling media never reached native adaptive recovery.");
            var replan=wire.Replans.Last();
            Check(replan.GetProperty("operation").GetString()=="quality_change"&&replan.GetProperty("quality_preference").GetString()=="auto","Actual native buffering recovery changed fixed intent or operation.");
            Check(replan.GetProperty("bandwidth_estimate_source").GetString()=="observed_rebuffer","Native lowering did not identify its measured cause.");
            var measured=replan.GetProperty("observed_throughput_kbps").GetInt32();
            Check(measured is >=100 and <1536,"Native observed throughput was invented or not below the delivered source.");
            Check(replan.GetProperty("bandwidth_estimate_kbps").GetInt32()==1075,"Native Auto step-down estimate differs from the protocol.");
            await Until(()=>manager.CurrentSession?.MediaFileId==43&&!service.IsLoading,8,"Lowered native media was not adopted and loaded.");
            Check(mpv.ReadMediaNetworkObservation().Bytes is >0,"Lowered native media did not open its reader.");
            await Until(()=>service.BufferingQualityPill=="undo",3,"Native lowering did not offer restore.");
            Call(mpv,"Command",(object)new[]{"keypress","ESC"});
            await Until(()=>oscKeeps>0&&service.BufferingQualityPill==null,3,"Actual OSC Escape did not dismiss Undo.");
            Check(oscBacks==0,"OSC Escape fell through while dismissing Undo.");
            Call(mpv,"Command",(object)new[]{"keypress","ESC"});
            await Until(()=>oscBacks>0,3,"OSC Escape did not return to native fullscreen handling after the pill closed.");
            Call(service,"EndBufferingQuality");
            await Until(()=>wire.Summaries.Count>=2,3,"Adopted native plans did not produce summaries.");
            Check(wire.Summaries.Any(s=>s.GetProperty("diagnostics").TryGetProperty("throughput_kbps",out _)),"Actual native media measurement never reached plan summary.");
            var prompt=new BufferingQualityController("ask");prompt.Adopt(new("prompt","prompt","1080p","1080p",1536,[new(){Label="360p",BitrateKbps=512}]),0);
            prompt.Observe(new(0,false,false,false,false,false,0,null));prompt.Observe(new(11000,true,false,false,false,false,0,500));prompt.Observe(new(18000,true,false,false,false,false,0,500));
            Set(service,"_bufferingController",prompt);Property(service,"State",PlayerState.Minimized);
            var frame=(Frame)root.FindName("ContentFrame");var content=frame.Content;var history=frame.BackStackDepth;
            Check((bool)Call(window,"TryHandleShellBack")!&&prompt.Pill==null&&ReferenceEquals(content,frame.Content)&&frame.BackStackDepth==history,"Minimized controller Back navigated instead of keeping quality.");
            Program.Log($"PASS: real libmpv signed-direct loopback media bytes/cache drive native observation, pause/seek/outage exclusion and Auto quality_change ({measured}kbps observed), lower-source adoption, Undo/Back and per-plan telemetry. Recovery observed after{observed.Elapsed.TotalSeconds:0.0}s healthy trickling; no physical network/hardware or historic buffering repair claimed.");
        }
        finally
        {
            traceStop.Cancel();if(traceTask!=null)await traceTask;
            media.Silent=false;
            Call(service,"StopPlaybackStallWatchdog"); Call(service,"UnwireMpvEvents");
            Set(service,"_mpv",null); Set(service,"_playbackManager",null); mpv?.Dispose();
            AppDomain.CurrentDomain.FirstChanceException-=DisposedRead;
            await File.WriteAllLinesAsync(Path.Combine(Program.ResultDirectory,"connection-disposed-reader.jsonl"),disposedReads.Select(item=>JsonSerializer.Serialize(item)));
            if(window!=null){window.Close();await Task.Delay(120);} main.SetValue(null,originalMain); slot.SetValue(null,original);
        }
    }
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Flags)!.Invoke(target,args);
    private static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Flags)!.SetValue(target,value);
    private static void Property(object target,string name,object value)=>target.GetType().GetProperty(name)!.SetValue(target,value);
    private static object? Field(object target,string name)=>target.GetType().GetField(name,Flags)!.GetValue(target);
    private static object Observation(PlayerService service,MpvPlayer mpv,MediaServer media,string phase,long elapsed,int replans)
    {
        var native=mpv.ReadMediaNetworkObservation();
        lock(Field(service,"_bufferingGate")!)
        {
            var controller=Field(service,"_bufferingController") as BufferingQualityController;
            return new{elapsed,phase,position=mpv.Position,paused=mpv.IsPaused,waiting=mpv.IsBufferingForCache,service.IsLoading,
                native.Bytes,native.InputBytesPerSecond,buffered=double.IsFinite(native.BufferedSeconds)?(double?)native.BufferedSeconds:null,
                native.SinceLastByteMs,native.TransportFailing,native.Seeking,replans,serverBytes=media.SentBytes,serverRequests=media.Requests,
                plan=controller?.Plan?.PlanId,adoption=controller?.Plan?.AdoptionId,controller?.Mode,controller?.Pill,
                summary=controller?.Summary,observed=controller==null?null:Field(controller,"_lastObservation"),
                grace=controller==null?null:Field(controller,"_grace"),stall=controller==null?null:Field(controller,"_stall"),
                outage=controller==null?null:Field(controller,"_outage"),outageEndedAt=controller==null?null:Field(controller,"_outageEndedAt"),
                pending=controller==null?null:Field(controller,"_pendingAt"),actionRunning=Field(service,"_bufferingActionRunning")};
        }
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Until(Func<bool> predicate,int seconds,string message){var watch=Stopwatch.StartNew();while(!predicate()&&watch.Elapsed.TotalSeconds<seconds)await Task.Delay(50);Check(predicate(),message);}
    private static FileVersion Version(int file,int bitrate)=>new(){FileId=file,Duration=90,Bitrate=bitrate,Container="wav",CodecAudio="pcm_s16le",AudioTracks=[new(){Language="eng",Default=true}]};
    private sealed class MediaServer:IDisposable
    {
        private readonly HttpListener _listener=new(); private readonly CancellationTokenSource _stop=new();
        private readonly byte[] _slow=Wave(48000),_fast=Wave(16000);
        internal string Origin{get;} internal volatile bool Silent,QualityPhase;
        internal long SentBytes;internal int Requests;
        internal MediaServer()
        {
            var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
            Origin=$"http://127.0.0.1:{port}";_listener.Prefixes.Add(Origin+"/");_listener.Start();
            _=Task.Run(async()=>{while(!_stop.IsCancellationRequested){HttpListenerContext context;try{context=await _listener.GetContextAsync();}catch when(_stop.IsCancellationRequested){break;}_=Serve(context);}});
        }
        private async Task Serve(HttpListenerContext context)
        {
            Interlocked.Increment(ref Requests);
            try
            {
                var slow=context.Request.Url!.AbsolutePath.EndsWith("slow");var bytes=slow?_slow:_fast;
                long from=0,to=bytes.Length-1;
                if(context.Request.Headers["Range"] is string range)
                {var pair=range[6..].Split('-');from=long.Parse(pair[0]);if(pair.Length>1&&long.TryParse(pair[1],out var end))to=Math.Min(to,end);context.Response.StatusCode=206;context.Response.Headers["Content-Range"]=$"bytes {from}-{to}/{bytes.Length}";}
                context.Response.ContentType="audio/wav";context.Response.ContentLength64=to-from+1;context.Response.Headers["Accept-Ranges"]="bytes";context.Response.Headers["ETag"]="\"fixture-wave\"";
                var bootstrapped=false;
                for(var at=from;at<=to&&!_stop.IsCancellationRequested;)
                {
                    while(Silent&&slow)await Task.Delay(50,_stop.Token);
                    var bootstrap=slow&&QualityPhase&&from==0&&!bootstrapped;
                    var count=(int)Math.Min(bootstrap?1048576:slow?8192:262144,to-at+1);bootstrapped|=bootstrap;
                    await context.Response.OutputStream.WriteAsync(bytes.AsMemory((int)at,count),_stop.Token);await context.Response.OutputStream.FlushAsync(_stop.Token);at+=count;Interlocked.Add(ref SentBytes,count);
                    if(slow)await Task.Delay(82,_stop.Token);
                }
            }
            catch(Exception ex)when(ex is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException){}
            finally{context.Response.Close();}
        }
        private static byte[] Wave(int rate)
        {
            var size=90*rate*4;using var stream=new MemoryStream(size+44);using var writer=new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(size+36);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(rate);writer.Write(rate*4);writer.Write((short)4);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(size);writer.Write(new byte[size]);return stream.ToArray();
        }
        public void Dispose(){_stop.Cancel();_listener.Close();_stop.Dispose();}
    }
    private sealed class Wire(string origin):HttpMessageHandler
    {
        private int _attempt;
        internal List<JsonElement> Replans=[],Summaries=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Check(request.RequestUri!.Host=="connection-transport-fixture.invalid","Unexpected fixture API origin.");var path=request.RequestUri.AbsolutePath;
            if(path.EndsWith("/capabilities")&&path.Contains("/playback/"))return Json(new{allowed=true,state="available",installation_id="fixture-install",protocol_versions=new[]{3},features=new[]{"buffering_quality_v1","connection_speed_v1","playback_plan_summary_v1","fixed_media_file_v1","plan_active_quality_v1"},bandwidth_probe_automatic=false});
            if(path=="/api/v2/settings/values/effective")return Json(new{items=new[]{new{key="playback.buffering_quality_mode",value="always",source="profile"}}});
            if(path.EndsWith("/progress"))return Json(new{outcome="applied"});
            if(request.Method==HttpMethod.Delete)return Json(new{outcome="stopped"});
            if(path.EndsWith("/route-events")){using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));if(body.RootElement.GetProperty("event").GetString()=="plan_summary")Summaries.Add(body.RootElement.Clone());return new(HttpStatusCode.Accepted);}
            if(path.EndsWith("/start")||path.EndsWith("/replan"))
            {
                var lowered=path.EndsWith("/replan");if(lowered){using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Replans.Add(body.RootElement.Clone());}else ++_attempt;
                var file=lowered?43:42;var session="fixture-session-"+_attempt;return Json(new{protocol_version=3,outcome="playable",session_id=session,playback_plan=new{plan_id=$"plan-{file}-{_attempt}",plan_attempt_key="key-"+file,session_id=session,effective_media_file_id=file,delivery="original_http",active_quality=lowered?"360p":"1080p",stream=new{url=origin+"/stream/direct/"+(lowered?"fast":"slow"),protocol="http_progressive"},timeline=new{can_seek_anywhere=true},source=new{bitrate_kbps=lowered?512:1536,duration_seconds=90},selected_tracks=new{audio=new{id=$"file:{file}:audio:0",index=0}},available_qualities=new[]{new{label="1080p",bitrate_kbps=1536},new{label="360p",bitrate_kbps=512}},subtitle=new{mode="off",inventory=Array.Empty<object>()}}});
            }
            return Json(new{items=Array.Empty<object>(),settings=Array.Empty<object>(),profiles=Array.Empty<object>(),sections=Array.Empty<object>(),providers=Array.Empty<object>(),devices=Array.Empty<object>()});
        }
        private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    }
}
