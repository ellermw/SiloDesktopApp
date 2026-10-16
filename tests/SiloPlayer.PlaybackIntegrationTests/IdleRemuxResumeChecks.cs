using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class IdleRemuxResumeChecks
{
    internal static async Task RunAsync(string results)
    {
        await CheckAsync(results, remote: false, pauseDuringReplan: true);
        await CheckAsync(results, remote: true, pauseDuringReplan: true);
        await CheckAsync(results, remote: true, pauseDuringReplan: true, toggleDuringReplan: true);
        Console.WriteLine("PASS published PlayerService: actual SiloCast play/pause/toggle preserve held-replan intent and a second idle resume; local/remote canonical reanchor and paused reload clocks survive FileLoaded.");
    }

    private static async Task CheckAsync(string results, bool remote, bool pauseDuringReplan, bool toggleDuringReplan = false)
    {
        await using var origin = new WaveOrigin();
        using var wire = new Wire(origin.Url);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl(remote ? "https://idle-remote.fixture.invalid" : "https://idle-local.fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api), credentialStore: null); auth.SelectProfile("fixture-profile");
        using var localWire = new Wire(origin.Url); using var localHttp = new HttpClient(localWire);
        var localApi = new SiloApiClient(localHttp); localApi.SetBaseUrl("https://idle-local.fixture.invalid");
        using var localAuth = new AuthService(localApi, new AuthApi(localApi), credentialStore: null);
        var serviceApi = remote ? localApi : api; var serviceAuth = remote ? localAuth : auth;
        using var service = new PlayerService(new PlaybackApi(serviceApi), new CatalogApi(serviceApi), serviceAuth,
            serviceApi, new SettingsService(Path.Combine(results, "idle-resume-" + remote)), new SettingsApi(serviceApi));
        if (remote) Field(service, "_activeAuthority", new PlaybackAuthorityScope(api, auth));
        var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), auth, api);
        var session = await manager.StartSessionAsync(42, forceStartPosition: true);
        var player = new MpvPlayer(); player.Initialize(16, 16); player.SetProperty("ao", "null"); player.SetProperty("video", "no");
        Field(service, "_mpv", player); Field(service, "_playbackManager", manager);
        Field(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan(session));
        Field(service, "_canSeekAnywhere", false); Field(service, "_timelineOffsetSeconds", 431.25d); Field(service, "_transportDurationSeconds", (double?)3600d);
        Property(service, "Versions", new List<FileVersion> { new() { FileId = 42, Duration = 3600 } });
        Property(service, "State", PlayerState.Expanded); Property(service, "IsPaused", true); Property(service, "ContentId", "fixture-title");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ContentLoaded += () => loaded.TrySetResult();
        Call(service, "WireMpvEvents");
        var idle = (ProgressiveRemuxIdleResume)GetField(service, "_idleRemuxResume")!;
        idle.ObservePause(true, false, DateTimeOffset.UtcNow.AddSeconds(-65));
        if (remote) await service.ApplySiloCastControlAsync(new JsonObject { ["name"] = "play" });
        else service.SetPaused(false);
        await wire.ReplanEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using (var request = JsonDocument.Parse(wire.ReplanBody!))
        {
            if (request.RootElement.GetProperty("operation").GetString() != "seek_reanchor"
                || Math.Abs(request.RootElement.GetProperty("position_seconds").GetDouble() - 431.25) > .001)
                throw new InvalidOperationException("Idle resume did not reanchor at the canonical paused position.");
        }
        if (pauseDuringReplan)
        {
            if (remote) await service.ApplySiloCastControlAsync(new JsonObject { ["name"] = toggleDuringReplan ? "play_pause" : "pause" });
            else service.SetPaused(true);
            if (!idle.DesiredPaused)
                throw new InvalidOperationException("Actual SiloCast control bypassed pending replan pause intent.");
        }
        wire.ReplanRelease.TrySetResult();
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (service.IsPaused != pauseDuringReplan)
            throw new InvalidOperationException("A pending control's pause intent was lost at actual FileLoaded.");
        if (Math.Abs((double)GetField(service, "_timelineOffsetSeconds")! - 431.25) > .001 || wire.Replans != 1)
            throw new InvalidOperationException("Idle resume duplicated its replan or lost its anchored timeline.");
        if (remote && localWire.Replans != 0)
            throw new InvalidOperationException("Remote idle resume replanned under desktop account authority.");
        if (pauseDuringReplan && GetField(idle, "_pausedSince") == null)
            throw new InvalidOperationException("A restored paused native load did not seed its next idle window.");
        if (remote && pauseDuringReplan)
        {
            var secondLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ContentLoaded += () => secondLoaded.TrySetResult();
            // Advance only the recorded idle time. Do not synthesize another
            // pause event: mpv stayed paused across the first native load.
            Field(idle, "_pausedSince", (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(-65));
            await service.ApplySiloCastControlAsync(new JsonObject { ["name"] = toggleDuringReplan ? "play_pause" : "play" });
            await secondLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (wire.Replans != 2 || service.IsPaused)
                throw new InvalidOperationException("The second long-pause SiloCast resume skipped its reanchor or lost resume intent.");
        }
        if (!remote && pauseDuringReplan)
        {
            // Match an ordinary paused seek/quality reload, where the native
            // player is already paused and need not emit another pause event.
            var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.ContentLoaded += () => reloaded.TrySetResult();
            var preparing = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!,
                new FileVersion { FileId = 42, Duration = 3600 }, origin.Url, 431.25d, CancellationToken.None)!;
            await preparing;
            var prepared = preparing.GetType().GetProperty("Result")!.GetValue(preparing)!;
            Field(service, "_switchingContent", true);
            Call(service, "ApplyPreparedTransport", prepared);
            Call(service, "BeginMpvLoad", prepared, true);
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!service.IsPaused || GetField(idle, "_pausedSince") == null)
                throw new InvalidOperationException("An ordinary paused reload did not commit the next idle window.");
        }
        await Task.Delay(100);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var i=0;i<100;i++) { if (predicate()) return; await Task.Delay(20); }
        throw new InvalidOperationException("Idle remux resume did not reach the prepared native load.");
    }
    private static object? GetField(object target,string name) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);
    private static void Field(object target,string name,object? value) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    private static void Property(object target,string name,object? value) => target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(target,value);
    private static object? Call(object target,string name,params object?[] args) => target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);

    internal sealed class Wire(string streamUrl) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource ReplanEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? ReplanBody; internal int Replans;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/capabilities"))return Json("""{"allowed":true,"state":"available","installation_id":"11111111-1111-4111-8111-111111111111","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");
            if(path.EndsWith("/replan"))
            {
                ReplanBody=await request.Content!.ReadAsStringAsync(ct); Interlocked.Increment(ref Replans);
                ReplanEntered.TrySetResult(); await ReplanRelease.Task.WaitAsync(ct);
                using var replan = JsonDocument.Parse(ReplanBody);
                var position = replan.RootElement.GetProperty("position_seconds").GetDouble();
                return Json(Decision(streamUrl + "?start=" + position.ToString(System.Globalization.CultureInfo.InvariantCulture),position,2));
            }
            if(path.EndsWith("/start"))return Json(Decision(streamUrl,0,1));
            if(path.EndsWith("/progress"))return Json("""{"outcome":"applied"}""");
            return Json("""{"outcome":"stopped"}""");
        }
        private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        private static string Decision(string url,double position,int number)=>$$$"""
            {"protocol_version":3,"outcome":"playable","session_id":"idle-session","playback_plan":{
            "protocol_version":3,"plan_id":"plan:{{{number:x32}}}","plan_attempt_key":"v3:{{{number:x16}}}","session_id":"idle-session",
            "delivery":"server_remux_progressive","stream":{"url":"{{{url}}}","protocol":"http_progressive","headers":{}},
            "timeline":{"source_start_seconds":{{{position.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"stream_origin_seconds":0,"player_start_seconds":0,"timeline_offset_seconds":{{{position.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"can_seek_anywhere":false},
            "selected_tracks":{"audio":{"id":"file:42:audio:0","index":0}},"effective_recipe":{"video_codec":"copy","audio_codec":"mp3"},
            "subtitle":{"mode":"off","inventory":[]},"available_qualities":[{"label":"original","preserves_source":true}],"degradation_warnings":[],
            "requested_media_file_id":42,"effective_media_file_id":42,"source":{"media_file_id":42,"duration_seconds":3600}
            }
            }
            """;
    }

    private sealed class WaveOrigin : IAsyncDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource stop=new();readonly Task accept;
        public string Url{get;}
        public WaveOrigin(){listener.Start();Url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/stream/remux/idle-fixture";accept=Accept();}
        async Task Accept(){try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);_ = Serve(client);}}catch(OperationCanceledException){}}
        async Task Serve(TcpClient client)
        {
            using(client)try
            {
                using var stream=client.GetStream();using var reader=new StreamReader(stream,leaveOpen:true);
                while(await reader.ReadLineAsync(stop.Token) is {Length:>0}){}
                var bytes=new byte[44+192000*3];using var writer=new BinaryWriter(new MemoryStream(bytes));
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(bytes.Length-8);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(48000);writer.Write(192000);writer.Write((short)4);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(bytes.Length-44);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"),stop.Token);
                await stream.WriteAsync(bytes,stop.Token);
            }catch(Exception)when(stop.IsCancellationRequested){}catch(IOException){}
        }
        public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await accept;stop.Dispose();}
    }
}
