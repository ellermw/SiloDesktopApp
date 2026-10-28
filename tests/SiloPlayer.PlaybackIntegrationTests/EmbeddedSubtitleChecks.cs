using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class EmbeddedSubtitleChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(string results)
    {
        CheckAvailableProfileFallback(results);
        Console.WriteLine("PASS published playback: available cached subtitle preference, explicit server false and absent-profile defaults override an unloaded transient view model correctly.");
        await RunCueCoverageAsync(results);
    }

    private static void CheckAvailableProfileFallback(string results)
    {
        var slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = slot.GetValue(null);
        try
        {
            slot.SetValue(null, new UnloadedSettingsProvider());
            foreach (var (profileForced, effectiveForced, expected) in new (bool?, bool?, int)[]
                { (true, null, 0), (true, false, -1), (false, null, -1), (null, null, 0) })
            {
                using var http = new HttpClient(new RejectNetwork());
                var api = new SiloApiClient(http); api.SetBaseUrl("https://subtitle-preference.fixture.invalid");
                using var auth = new AuthService(api, new(api));
                auth.SelectProfile("synthetic-profile", profile: profileForced is bool value
                    ? new Profile { Id = "synthetic-profile", Language = "en", SubtitleLanguage = "en", SubtitleMode = "auto", ShowForcedSubtitles = value } : null);
                using var service = CreateService(api, auth, results);
                var session = new PlaybackStartResponse { SubtitleUrls = [new() { Index = 0, Source = "embedded", Codec = "subrip", Language = "en", Forced = true }] };
                var version = new FileVersion { AudioTracks = [new() { Language = "en", Default = true }] };
                var watch = new WatchDetailResponse { EffectiveSubtitleMode = "auto", EffectiveSubtitleLanguage = "en", EffectiveShowForcedSubtitles = effectiveForced };
                Call(service, "ResolveInitialSubtitleSelection", watch, version, session, 0, null, null);
                if ((int?)Field(service, "_pendingInitialServerSubtitleIndex") != expected)
                    throw new InvalidOperationException("Saved/default subtitle fallback selected the wrong forced track.");
            }
        }
        finally { slot.SetValue(null, previous); }
    }

    internal static async Task RunCueCoverageAsync(string results)
    {
        var media = Path.Combine(results, "subtitle-long-video.y4m");
        Directory.CreateDirectory(results);
        using (var output = File.Create(media))
        {
            output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W16 H16 F1:1 Ip A1:1 C420jpeg\n"));
            var frame = Enumerable.Repeat((byte)16, 256).Concat(Enumerable.Repeat((byte)128, 128)).ToArray();
            for (var index = 0; index < 960; index++) { output.Write("FRAME\n"u8); output.Write(frame); }
        }
        foreach (var kind in new[] { PlaybackTransportKind.RemuxProgressive, PlaybackTransportKind.RemuxHls, PlaybackTransportKind.TranscodeHls })
        {
            var reads = 0;
            var bounded = false;
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var origin = builder.Build();
            origin.Run(async context =>
            {
                Interlocked.Increment(ref reads);
                bounded |= context.Request.Query.ContainsKey("position") || context.Request.Query.ContainsKey("duration");
                context.Response.ContentType = "text/vtt";
                await context.Response.WriteAsync("WEBVTT\n\n00:00:10.000 --> 00:00:18.000\nEARLY_CUE\n\n" +
                    (bounded ? "" : "00:15:00.000 --> 00:15:08.000\nLATE_CUE\n\n"));
            });
            await origin.StartAsync();
            var address = origin.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient(new RejectNetwork());
            var api = new SiloApiClient(http); api.SetBaseUrl(address);
            using var auth = new AuthService(api, new(api));
            using var service = CreateService(api, auth, results);
            using var manager = new PlaybackManager(new(api), new(api), auth, api);
            var track = new SubtitleTrackInfo { Index = 0, Source = "embedded", Codec = "subrip", Language = "en", Label = "Synthetic captions", Url = "/subtitles.vtt" };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, new PlaybackStartResponse { PlayMethod = "remux", SubtitleUrls = [track] });
            Set(service, "_playbackManager", manager);
            Set(service, "_activeTransportPlan", new PlaybackTransportPlan(kind == PlaybackTransportKind.TranscodeHls ? PlaybackSemanticMethod.Transcode : PlaybackSemanticMethod.Remux, kind, false));
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null");
            Set(service, "_mpv", mpv);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            mpv.FileLoaded += () => loaded.TrySetResult();
            mpv.LoadFile(media); mpv.Play();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Call(service, "SelectSubtitleTrack", track);
            if (bounded) throw new InvalidOperationException("Embedded sidecar was requested with a finite position/duration window.");
            mpv.Seek(902);
            await Until(() => ((string?)Call(mpv, "ReadUpscalingProperty", "sub-text"))?.Contains("LATE_CUE") == true);
            mpv.Seek(12);
            await Until(() => ((string?)Call(mpv, "ReadUpscalingProperty", "sub-text"))?.Contains("EARLY_CUE") == true);
            Call(service, "SelectSubtitleTrack", track);
            if (reads != 1) throw new InvalidOperationException("Selecting a loaded sidecar fetched it again.");
            Call(service, "LoadSubtitles");
            if (((System.Collections.IDictionary)Field(service, "_loadedExternalSubtitleSids")!).Count != 0)
                throw new InvalidOperationException("File-load subtitle initialization retained the old SID cache.");
            await origin.StopAsync();
        }
        Console.WriteLine("PASS published playback: remux/HLS/transcode sidecars request complete tracks, retain cues beyond10 minutes and after backward seek, reuse SIDs and clear the file-load cache.");
    }

    private static PlayerService CreateService(SiloApiClient api, AuthService auth, string results)
        => new(new(api), new(api), auth, api, new SettingsService(Path.Combine(results, "subtitle-settings")), new(api));
    private static object? Call(object target, string name, params object?[] args)
        => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(25);
        if (!ready()) throw new TimeoutException("Native subtitle cue did not appear after seek.");
    }
    private sealed class UnloadedSettingsProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.FullName == "SiloPlayer.ViewModels.SettingsViewModel"
            ? RuntimeHelpers.GetUninitializedObject(serviceType) : null;
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("Subtitle checks must not call a remote API.");
    }
}
