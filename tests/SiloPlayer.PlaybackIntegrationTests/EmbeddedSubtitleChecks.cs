using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
                using var manager = new PlaybackManager(new(api), new(api), auth, api);
                typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
                Set(service, "_playbackManager", manager);
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
            // Use the selection owner so remote bytes are prepared before the
            // local-only executor runs; keep the complete-track cue assertions.
            if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)!).WaitAsync(TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("The complete sidecar could not be prepared and selected.");
            if (bounded) throw new InvalidOperationException("Embedded sidecar was requested with a finite position/duration window.");
            mpv.Seek(902);
            await Until(() => ((string?)Call(mpv, "ReadUpscalingProperty", "sub-text"))?.Contains("LATE_CUE") == true);
            mpv.Seek(12);
            await Until(() => ((string?)Call(mpv, "ReadUpscalingProperty", "sub-text"))?.Contains("EARLY_CUE") == true);
            if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)!).WaitAsync(TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("The loaded sidecar could not be selected again.");
            if (reads != 1) throw new InvalidOperationException("Selecting a loaded sidecar fetched it again.");
            Call(service, "LoadSubtitles");
            if (((System.Collections.IDictionary)Field(service, "_loadedExternalSubtitleSids")!).Count != 0)
                throw new InvalidOperationException("File-load subtitle initialization retained the old SID cache.");
            await origin.StopAsync();
        }
        Console.WriteLine("PASS published playback: remux/HLS/transcode sidecars request complete tracks, retain cues beyond10 minutes and after backward seek, reuse SIDs and clear the file-load cache.");
        await RunReloadCoverageAsync(results, media);
    }

    private static async Task RunReloadCoverageAsync(string results, string media)
    {
        var probe = Path.Combine(results, "subtitle-sync-notice-probe.lua");
        await File.WriteAllTextAsync(probe, """
            local mp = require 'mp'
            local notices, barrier = 0, 0
            mp.register_script_message('osc-subtitle-sync-notice', function(payload)
                if payload ~= '{}' then
                    notices = notices + 1
                    mp.set_property_number('user-data/subtitle-sync-notices', notices)
                end
            end)
            mp.register_script_message('subtitle-sync-fixture-barrier', function()
                barrier = barrier + 1
                mp.set_property_number('user-data/subtitle-sync-barrier', barrier)
            end)
            mp.set_property_number('user-data/subtitle-sync-notices', 0)
            mp.set_property_number('user-data/subtitle-sync-probe-ready', 1)
            """);
        foreach (var trackId in new string?[] { "downloaded-track-9", null })
        {
            var reads = 0;
            var busy = false;
            var marker = "ORIGINAL";
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var origin = builder.Build();
            origin.Run(async context =>
            {
                Interlocked.Increment(ref reads);
                if (Volatile.Read(ref busy))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers["Retry-After"] = "60";
                    return;
                }
                context.Response.ContentType = "text/vtt";
                var text = Volatile.Read(ref marker);
                var start = text == "CORRECTED" ? "12" : "10";
                await context.Response.WriteAsync($"WEBVTT\n\n00:00:{start}.000 --> 00:00:18.000\n{text}\n\n");
            });
            await origin.StartAsync();
            var address = origin.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var syncWire = new ReloadSyncWire();
            using var http = new HttpClient(syncWire);
            var api = new SiloApiClient(http); api.SetBaseUrl(address);
            using var auth = new AuthService(api, new(api));
            using var service = CreateService(api, auth, results);
            using var manager = new PlaybackManager(new(api), new(api), auth, api);
            var track = new SubtitleTrackInfo { Index = 0, Id = 9, TrackId = trackId, SyncKey = "stored-9",
                Source = "downloaded", Codec = "webvtt", Language = "en", Label = "Downloaded captions", Url = "/captions.vtt" };
            var session = new PlaybackStartResponse { SessionId = "reload-fixture", MediaFileId = 42, PlayMethod = "remux", SubtitleUrls = [track] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            // SessionId has its own backing field; setting only CurrentSession
            // leaves SyncCurrent false and makes every timing observation a no-op.
            Set(manager, "_sessionId", session.SessionId);
            manager.RecordLocalSubtitleSelection(track.Index);
            Set(service, "_playbackManager", manager);
            Set(service, "_activeTransportPlan", new PlaybackTransportPlan(PlaybackSemanticMethod.Remux, PlaybackTransportKind.RemuxProgressive, false));
            using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null");
            Set(service, "_mpv", mpv);
            Call(mpv, "Command", (object)new[] { "load-script", probe });
            await Until(() => mpv.GetPropertyDouble("user-data/subtitle-sync-probe-ready") == 1);
            async Task<double> NoticeCountAsync()
            {
                var before = mpv.GetPropertyDouble("user-data/subtitle-sync-barrier");
                mpv.SendScriptMessage("subtitle-sync-fixture-barrier");
                await Until(() => mpv.GetPropertyDouble("user-data/subtitle-sync-barrier") > before);
                return mpv.GetPropertyDouble("user-data/subtitle-sync-notices");
            }

            // Own a stable sync lifecycle without starting background inventory
            // requests. Observations below still use the actual sync apply path.
            var syncOwner = new CancellationTokenSource();
            Set(service, "_subtitleSyncCts", syncOwner);
            Set(service, "_subtitleSyncSession", session.SessionId);
            Set(service, "_subtitleSyncFile", session.MediaFileId);
            Set(service, "_subtitleSyncOwner", Field(service, "_activeAuthority")!);
            Set(service, "_subtitleSyncAuthority", api.CaptureContext());
            Call(service, "ApplySubtitleSyncState", new SubtitleSyncState { Key = track.SyncKey, MediaFileId = 42 }, syncOwner.Token);
            if (((System.Collections.IDictionary)Field(service, "_subtitleSyncEntries")!).Count != 1)
                throw new InvalidOperationException("The fixture did not establish a current sync owner.");

            await LoadNativeFileAsync(mpv, media);
            if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)!).WaitAsync(TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("Downloaded subtitle was not initially applied.");
            var cache = (System.Collections.Generic.IDictionary<int, string>)Field(service, "_prefetchedSubtitleFiles")!;
            var cacheGate = Field(service, "_subtitleTrackStateLock")!;
            string? CachedPath()
            {
                lock (cacheGate) return cache.TryGetValue(track.Index, out var path) ? path : null;
            }

            async Task CheckBusyReloadAsync(string expected, bool newSession = false)
            {
                var path = CachedPath() ?? throw new InvalidOperationException("Applied subtitle has no local bytes.");
                var before = Volatile.Read(ref reads);
                var notices = await NoticeCountAsync();
                Volatile.Write(ref busy, true);
                // This is the applied-byte handoff used by same-file audio,
                // quality and recovery loads, followed by a real mpv file load.
                Call(service, "PrepareAppliedSubtitleFileForReload");
                if (Field(service, "_preparedSubtitleReload") == null)
                    throw new InvalidOperationException("Stable downloaded identity did not prepare its applied bytes.");
                if (newSession)
                {
                    session = new PlaybackStartResponse { SessionId = session.SessionId + "-replacement", MediaFileId = session.MediaFileId,
                        PlayMethod = session.PlayMethod, SubtitleUrls = session.SubtitleUrls };
                    typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
                    Set(manager, "_sessionId", session.SessionId);
                }
                await LoadNativeFileAsync(mpv, media);
                Call(service, "LoadSubtitles");
                if (newSession)
                {
                    syncOwner = (CancellationTokenSource)Field(service, "_subtitleSyncCts")!;
                    // LoadSubtitles starts inventory with an empty revision
                    // snapshot. OSC then starts a per-key placeholder read.
                    await syncWire.InventoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(8));
                    Call(service, "SendSubtitleListToOsc");
                    await Until(() =>
                    {
                        lock (Field(service, "_subtitleSyncGate")!)
                        {
                            var entry = ((System.Collections.IDictionary)Field(service, "_subtitleSyncEntries")!)[track.SyncKey];
                            return entry != null && !(bool)entry.GetType().GetField("Reading")!.GetValue(entry)! &&
                                ((SubtitleSyncState)entry.GetType().GetField("State")!.GetValue(entry)!).Timing.OffsetMs == 2000;
                        }
                    });
                    syncWire.InventoryRelease.TrySetResult();
                    await Until(() => Field(service, "_subtitleSyncCapability") != null);
                }
                if (CachedPath() != path || !File.Exists(path) || Field(service, "_preparedSubtitleReload") != null)
                    throw new InvalidOperationException("Reload did not consume its local subtitle carry exactly once.");
                if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)!).WaitAsync(TimeSpan.FromSeconds(8)))
                    throw new InvalidOperationException("Busy reload lost the already applied downloaded subtitle.");
                if (Volatile.Read(ref reads) != before || service.ActiveSubtitleServerIndex != track.Index ||
                    !mpv.GetSubtitleTrackIds(externalOnly: true).Contains((int)Math.Round(mpv.GetPropertyDouble("sid"))))
                    throw new InvalidOperationException("Same-file reload re-read the busy URL or turned captions off.");
                mpv.Seek(13);
                await Until(() => ((string?)Call(mpv, "ReadUpscalingProperty", "sub-text"))?.Contains(expected) == true);
                if (await NoticeCountAsync() != notices)
                    throw new InvalidOperationException("Reloading already corrected cues sent a false timing notice.");
                Volatile.Write(ref busy, false);
            }

            await CheckBusyReloadAsync("ORIGINAL");
            foreach (var (text, offset) in new[] { ("CORRECTED", 2000), ("RESET", 0) })
            {
                var oldPath = CachedPath() ?? throw new InvalidOperationException("Applied subtitle has no local bytes.");
                Call(service, "PrepareAppliedSubtitleFileForReload");
                Volatile.Write(ref marker, text);
                Call(service, "ApplySubtitleSyncState", new SubtitleSyncState
                {
                    Key = track.SyncKey, MediaFileId = 42, Source = "downloaded", Format = "vtt",
                    Timing = new SubtitleTiming { OffsetMs = offset },
                }, syncOwner.Token);
                if (Field(service, "_preparedSubtitleReload") != null || CachedPath() == oldPath)
                    throw new InvalidOperationException("Accepted timing retained stale prefetched bytes or a pending carry.");
                await Until(() =>
                {
                    lock (cacheGate) return cache.TryGetValue(track.Index, out var corrected) && corrected != oldPath &&
                        File.Exists(corrected) && File.ReadAllText(corrected).Contains(text);
                });
                await CheckBusyReloadAsync(text, newSession: offset != 0);
            }
            // A timing change while A is inactive must retire A's native SID,
            // without disturbing B. Returning to A must fetch corrected bytes.
            var stalePath = CachedPath() ?? throw new InvalidOperationException("Applied subtitle has no local bytes.");
            var staleSid = (int)Math.Round(mpv.GetPropertyDouble("sid"));
            Call(service, "PrepareAppliedSubtitleFileForReload");
            if (Field(service, "_preparedSubtitleReload") == null)
                throw new InvalidOperationException("Inactive timing fixture did not prepare A's carry.");
            var other = new SubtitleTrackInfo { Index = 1, Source = "external", Codec = "webvtt", Language = "fr",
                Label = "Other captions", Url = "/other.vtt" };
            session.SubtitleUrls.Add(other);
            if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", other.Index, false)!).WaitAsync(TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("The other sidecar could not be selected.");
            var retainedSid = (int)Math.Round(mpv.GetPropertyDouble("sid"));
            var beforeRead = Volatile.Read(ref reads);
            Volatile.Write(ref marker, "INACTIVE_CORRECTION");
            Call(service, "ApplySubtitleSyncState", new SubtitleSyncState { Key = track.SyncKey, MediaFileId = 42,
                Source = "downloaded", Format = "vtt", Timing = new SubtitleTiming { OffsetMs = 3000 } }, syncOwner.Token);
            if (Field(service, "_preparedSubtitleReload") != null || CachedPath() != null || File.Exists(stalePath) ||
                ((System.Collections.IDictionary)Field(service, "_loadedExternalSubtitleSids")!).Contains(track.Index) ||
                mpv.GetSubtitleTrackIds(externalOnly: true).Contains(staleSid) ||
                (int)Math.Round(mpv.GetPropertyDouble("sid")) != retainedSid || service.ActiveSubtitleServerIndex != other.Index ||
                Volatile.Read(ref reads) != beforeRead)
                throw new InvalidOperationException("Inactive timing change retained stale cues or disturbed the active sidecar.");
            if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", track.Index, false)!).WaitAsync(TimeSpan.FromSeconds(8)) ||
                Volatile.Read(ref reads) != beforeRead + 1)
                throw new InvalidOperationException("Reselecting the inactive sidecar did not fetch corrected cues exactly once.");
            await CheckBusyReloadAsync("INACTIVE_CORRECTION");

            // Refresh/download appends TrackId-less rows without a replan. A
            // same-session native reload must still execute a granted Off.
            session.SelectedSubtitleTrackId = null;
            Call(manager, "ResetLocalTrackIntent");
            if (manager.SubtitleSelectionForLoad != -1)
                throw new InvalidOperationException("A downloaded row overrode the plan's Off selection.");
            await LoadNativeFileAsync(mpv, media);
            Call(service, "LoadSubtitles");
            if (!await ((Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", manager.SubtitleSelectionForLoad, false)!).WaitAsync(TimeSpan.FromSeconds(8)) ||
                service.ActiveSubtitleServerIndex != -1 || mpv.GetPropertyDouble("sid") > 0)
                throw new InvalidOperationException("Same-session reload enabled native captions despite a granted Off.");
            await origin.StopAsync();
        }
        Console.WriteLine("PASS published playback: downloaded identities survive busy and new-session reloads without false timing notices; timing changes retire inactive SIDs and stale carries; granted Off stays off.");
    }

    private static async Task LoadNativeFileAsync(MpvPlayer mpv, string media)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded() => loaded.TrySetResult();
        mpv.FileLoaded += OnLoaded;
        try
        {
            mpv.LoadFile(media); mpv.Play();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        finally { mpv.FileLoaded -= OnLoaded; }
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
    private sealed class ReloadSyncWire : HttpMessageHandler
    {
        public readonly TaskCompletionSource InventoryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource InventoryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var state = new SubtitleSyncState { Key = "stored-9", MediaFileId = 42, Source = "downloaded", Format = "vtt",
                Timing = new SubtitleTiming { OffsetMs = 2000 } };
            object body;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/v2/subtitles/sync/status": body = new SubtitleSyncCapability { State = "available", External = true }; break;
                case "/api/v2/subtitles/42/sync":
                    InventoryEntered.TrySetResult();
                    await InventoryRelease.Task.WaitAsync(ct);
                    body = new SubtitleSyncInventory { Subtitles = [state] }; break;
                case "/api/v2/subtitles/42/sync/stored-9": body = new SubtitleSyncResponse { Subtitle = state }; break;
                default: throw new InvalidOperationException("Unexpected sync fixture request: " + request.RequestUri.AbsolutePath);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("Subtitle checks must not call a remote API.");
    }
}
