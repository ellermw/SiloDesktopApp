using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

// Load the actual published service without launching Silo, opening a window,
// connecting to a server, or reading the user's settings.
var results = Path.Combine(Path.GetTempPath(), "silo-playback-integration-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable(LocalLog.LogDirectoryEnvironmentVariable, results);
using var http = new HttpClient(new RejectNetworkHandler());
var api = new SiloApiClient(http);
api.SetBaseUrl("https://fixture.invalid");
var service = new PlayerService(new PlaybackApi(api), new CatalogApi(api),
    new AuthService(api, new AuthApi(api)), api, new SettingsService(results), new SettingsApi(api));
var prepare = typeof(PlayerService).GetMethod("PreparePlaybackTransportAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
var proxy = typeof(PlayerService).GetField("_directStreamProxy", BindingFlags.NonPublic | BindingFlags.Instance)!;
var hls = typeof(PlayerService).GetField("_hlsProxy", BindingFlags.NonPublic | BindingFlags.Instance)!;
try
{
    var session = new PlaybackStartResponse
    {
        ProtocolVersion = 3, SessionId = "fixture-session", MediaFileId = 42,
        PlayMethod = "direct", StreamUrl = "https://fixture.invalid/stream/direct/fixture-signed-token",
        CanSeekAnywhere = true, PlayerStartSeconds = 0, Position = 0, DurationSeconds = 7200
    };
    foreach (var position in new[] { 0, 687.3, 3600.0 })
    {
        var prepared = await Prepare(session, position);
        Require((string)Get(prepared, "LocalUrl") == session.StreamUrl, "Direct signed transport still uses a relay.");
        Require(proxy.GetValue(service) == null && hls.GetValue(service) == null, "Native direct preparation created a proxy.");
        Require((double)Get(prepared, "MpvLoadStartSeconds") == position, "Direct recovery reused the stale plan start position.");
        Require((double)Get(prepared, "TimelineOffsetSeconds") == 0, "Direct media timeline changed.");
    }
    Console.WriteLine("PASS published PlayerService: signed direct transport bypasses both proxies; initial/resume/recovery positions preserved.");

    session.PlayMethod = "remux";
    session.CanSeekAnywhere = false;
    session.StreamUrl = "https://fixture.invalid/stream/remux/fixture-signed-token";
    session.TimelineOffsetSeconds = 687.3;
    var remux = await Prepare(session, 687.3);
    Require(((string)Get(remux, "LocalUrl")).StartsWith("http://127.0.0.1:"), "Sequential remux lost its transport.");
    Require((double)Get(remux, "MpvLoadStartSeconds") == 0, "Remux incorrectly seeks by absolute media time.");
    Require((double)Get(remux, "TimelineOffsetSeconds") == 687.3, "Remux timeline was changed.");
    Console.WriteLine("PASS published PlayerService: sequential remux keeps its zero-based playback timeline.");

    session.PlayMethod = "direct";
    session.CanSeekAnywhere = true;
    session.StreamUrl = "https://fixture.invalid/api/v2/stream/fixture-session";
    session.TimelineOffsetSeconds = 0;
    var integrated = await Prepare(session, 120);
    Require(proxy.GetValue(service) != null, "Account-authenticated legacy transport lost its refreshing bearer relay.");
    Require((double)Get(integrated, "MpvLoadStartSeconds") == 120, "Legacy direct resume position changed.");
    Console.WriteLine("PASS published PlayerService: account-token transport retains authentication handling.");

    var enterTerminal = typeof(PlayerService).GetMethod("EnterPlaybackTerminalState", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var terminalState = typeof(PlayerService).GetField("_playbackTerminalState", BindingFlags.NonPublic | BindingFlags.Instance)!;
    enterTerminal.Invoke(service, [new PlaybackPlanTerminalException("direct_recovery_exhausted", "fixture", true), 687.3, "buffering-stalled", false]);
    Require((string)Get(terminalState.GetValue(service)!, "Trigger") == "viewer-retry", "Exhausted direct recovery cannot start a fresh attempt.");
    enterTerminal.Invoke(service, [new PlaybackPlanTerminalException("transcode_start_failed", "fixture", true), 687.3, "playback-error", false]);
    Require((string)Get(terminalState.GetValue(service)!, "Trigger") == "playback-error", "Non-direct retry lost its original replan/quality path.");
    Console.WriteLine("PASS published PlayerService: only exhausted direct recovery starts a fresh attempt; other retry routes are preserved.");

    var failure = typeof(PlayerService).GetMethod("HandleMpvFailure", BindingFlags.Instance | BindingFlags.NonPublic)!;
    foreach (var loading in new[] { true, false })
    {
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("IsAudiobook")!.SetValue(service, true);
        typeof(PlayerService).GetProperty("IsPaused")!.SetValue(service, false);
        SetField("_switchingContent", loading);
        SetField("_prematureEofRecoveryActive", false);
        SetField("_consecutiveFileLoadTimeouts", 0);
        failure.Invoke(service, ["fixture direct read failure", true]);
        Require((bool)GetField("_prematureEofRecoveryActive"), "Direct failure did not begin recovery.");
        Require(!service.IsPaused, "Failed audiobook stream was marked completed/paused.");
        Require(service.State == PlayerState.Expanded, "Transport failure closed the listening surface.");
    }
    typeof(PlayerService).GetMethod("FlushLogs", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    var trace = File.ReadAllText(Path.Combine(results, "state_trace.txt"));
    Require(trace.Contains("Stream recovery skipped (direct-transport-error): player/session unavailable"), "Direct stream failure lost its recovery classification.");
    Require(!trace.Contains("Audiobook complete") && !trace.Contains("continuing file"), "Transport failure advanced an audiobook.");
    Console.WriteLine("PASS published PlayerService: direct transport failures recover during startup and audiobook playback without completing/advancing content.");

    SetField("_switchingContent", true);
    SetField("_consecutiveFileLoadTimeouts", 1);
    failure.Invoke(service, ["fixture repeated startup transport failure", true]);
    Require((string)Get(terminalState.GetValue(service)!, "Trigger") == "direct-transport-error", "Viewer retry of a startup transport failure could escalate direct play to remux.");
    Require(PlaybackRecoveryPolicy.CanReloadCurrentDirectSession(PlaybackTransportKind.DirectProgressive,
        (string)Get(terminalState.GetValue(service)!, "Trigger")), "Startup terminal retry lost direct recovery policy.");
    Console.WriteLine("PASS published PlayerService: repeated startup transport failure preserves direct recovery when the viewer retries.");

    foreach (var closeCode in new[] { 1013, 4001 })
    {
        await new SiloPlayer.Tests.EventAccessTransportTests().EventSocketReconnectKeepsActivePlaybackProgressAlive(closeCode);
        Console.WriteLine($"PASS published playback: notification socket close {closeCode} retires stale reads while preserving session/progress/stop without a playback reconnect.");
    }
    foreach (var identityChange in new[] { "profile", "profile-return", "server", "server-return", "login", "logout", "pin-revoked" })
    {
        await new SiloPlayer.Tests.PlaybackApiV2Tests().RealIdentityChangesRejectOldPlaybackBeforeSendingProgress(identityChange);
        Console.WriteLine($"PASS published playback: {identityChange} still rejects old-session progress before HTTP dispatch.");
    }
}
finally
{
    foreach (var name in new[] { "StopDirectStreamProxy", "StopHlsProxy" })
        typeof(PlayerService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null);
}

async Task<object> Prepare(PlaybackStartResponse session, double position)
{
    var task = (Task)prepare.Invoke(service,
        [session, new FileVersion { FileId = 42 }, session.StreamUrl, position, CancellationToken.None])!;
    await task;
    return task.GetType().GetProperty("Result")!.GetValue(task)!;
}
static object Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
object GetField(string name) => typeof(PlayerService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
void SetField(string name, object value) => typeof(PlayerService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, value);
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

sealed class RejectNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        throw new InvalidOperationException("This integration test must not make remote requests.");
}
