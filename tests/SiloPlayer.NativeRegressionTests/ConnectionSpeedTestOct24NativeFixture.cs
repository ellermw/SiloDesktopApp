using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class ConnectionSpeedTestOct24NativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var server = new ProbeServer(); using var http = SiloHttpClientFactory.CreateClient();
        var client = new SiloApiClient(http); client.SetBaseUrl(server.Origin); client.SetAccessToken("fixture-access"); client.SetProfile("fixture", "fixture-pin"); client.SetDeviceMetadata("fixture-device", "fixture", "windows");
        using var auth = new AuthService(client, new AuthApi(client));
        var settings = new SettingsApi(client); var api = new PlaybackApi(client); var catalog = new CatalogApi(client);
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "speed-fixture-settings"));
        var theme = new ThemeService(local, settings);
        theme.ApplyTheme("midnight-cinema");
        var vm = new SettingsViewModel(settings, catalog, new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, local, new AccessibilityService(local, theme));
        using var player = new PlayerService(api, catalog, auth, client, local, settings);
        var observer = PlaybackConnectionService.For(api);
        var fixtureWindow = (Window)typeof(RegressionApp).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Application.Current)!;
        var originalWindowSize = fixtureWindow.AppWindow.Size;
        slot.SetValue(null, new Services(original, api, vm, player));
        var control = new PlaybackConnectionSettingsControl { Width = 800, RequestedTheme = ElementTheme.Dark }; parent.Children.Add(control);
        try
        {
            await Until(() => vm.ConnectionSpeedSupported && !vm.ConnectionSettingsBusy);
            Check(vm.ConnectionReading == "Not measured yet", "The initial connection description differs from the WebUI.");
            Check(All<TextBlock>(control).Any(t => t.Text == "Connection speed"), "Connection speed label missing.");
            foreach (var width in new[] { 800d, 360d })
            {
                fixtureWindow.AppWindow.ResizeClient(new((int)width, 900));
                control.Width = width; control.UpdateLayout(); await Task.Delay(100);
                foreach (var button in All<Button>(control).Where(b => b.ActualWidth > 0))
                { var bounds = button.TransformToVisual(control).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight)); Check(bounds.X >= -1 && bounds.Right <= width + 1, "A visible connection action overflows the native width."); }
                if (width == 360)
                {
                    var mode = All<ComboBox>(control).Single();
                    Check(Math.Abs(mode.ActualWidth - width) < 2, $"The compact buffering selector does not fill its row (selector={mode.ActualWidth:0.##}, row={(mode.Parent as FrameworkElement)?.ActualWidth:0.##}, viewport={control.XamlRoot.Size.Width:0.##}, control={control.ActualWidth:0.##}).");
                    var speedLabel = All<TextBlock>(control).Single(t => t.Text == "Connection speed");
                    var speedRow = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(speedLabel));
                    Check(Math.Abs(speedRow.ActualWidth - width) < 2, $"The compact Connection body retains an empty title-column gutter (row={speedRow.ActualWidth:0.##}, control={control.ActualWidth:0.##}).");
                }
                await Capture(control, $"connection-idle-{width:0}.png");
            }
            fixtureWindow.AppWindow.ResizeClient(new(800, 900)); control.Width = 800; control.UpdateLayout(); await Task.Delay(80);
            Invoke(All<Button>(control).Single(b => Equals(b.Content, "Test speed")));
            await Until(() => vm.ConnectionTestRunning && server.MediaRequests == 1);
            Check(All<Button>(control).Any(b => Equals(b.Content, "Testing…") && !b.IsEnabled), "Testing state does not disable and relabel the button.");
            await Capture(control, "connection-testing.png");
            await Until(() => !vm.ConnectionTestRunning, 8);
            Check(server.Reports == 1 && server.ProxyIdentityFree && server.AccountIdentityPresent && server.NoPinGrant, "Probe redirect did not preserve account-only API authorization and credential-free media identity.");
            Check(server.Manual && server.RequestedSize == 1_000_000_000 && server.ReportedRate >= 100, "Test speed did not report an accurate-mode probe.");
            Check(vm.ConnectionReading.Contains("measured just now on", StringComparison.Ordinal) && vm.ConnectionBudget.Contains("Auto plays videos up to"), "Saved result or Auto budget was not shown.");
            Check(vm.ConnectionSettingsMessage.Length == 0, "Successful test adds a message absent from the WebUI.");
            await Capture(control, "connection-measured.png");
            var combo = All<ComboBox>(control).Single(); combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "never"));
            await Until(() => server.Writes == 1 && !vm.ConnectionSettingsBusy);
            Check(server.ProfileDefault && server.Clears == 1 && vm.BufferingQualityMode == "never", "Immediate buffering save did not write profile default and clear its shadowing device override.");
            Check(vm.LearnedConnectionLimit.Length == 0 && vm.ConnectionBudget.Length == 0, "Never mode showed a learned limit or its constrained Auto budget.");
            server.RateLimited = true;
            Invoke(All<Button>(control).Single(b => Equals(b.Content, "Test speed")));
            await Until(() => !vm.ConnectionTestRunning && vm.ConnectionSettingsMessage == "Try again in 2 seconds");
            await Capture(control, "connection-rate-limited.png");
            await Until(() => vm.ConnectionSettingsMessage.Length == 0, 4);
            server.RateLimited = false; server.RejectSave = true;
            await vm.TestConnectionSpeedAsync();
            Check(vm.ConnectionSettingsMessage == "Couldn't save the speed test. Try again.", "Save failure was presented as a measurement failure.");
            server.RejectSave = false;
            var before = server.MediaRequests; observer.SetMediaActive(true); await vm.TestConnectionSpeedAsync();
            Check(server.MediaRequests == before && vm.ConnectionSettingsMessage == "Stop playback before testing, then try again.", "Playback did not block a competing speed test.");
            observer.SetMediaActive(false);
            var reports = server.Reports;
            Invoke(All<Button>(control).Single(b => Equals(b.Content, "Test speed")));
            await Until(() => vm.ConnectionTestRunning && server.MediaRequests > before);
            parent.Children.Remove(control);
            await Until(() => !vm.ConnectionTestRunning);
            Check(server.Reports == reports, "Leaving settings saved a cancelled measurement.");
            await CheckNativeActivityLifecycleAsync(player, observer, server);
            server.RejectPreferenceRead = true;
            await vm.LoadConnectionSettingsAsync();
            Check(vm.ConnectionSpeedSupported && !vm.BufferingQualitySupported, "A failed optional buffering preference hid the supported connection test.");
            var savedBeforePreferenceFailure = server.Reports;
            await vm.TestConnectionSpeedAsync();
            Check(server.Reports == savedBeforePreferenceFailure + 1, "A failed optional preference left Test speed silently unavailable.");
            Program.Log("PASS: real hardened HTTP307 signed-probe redirect, account-only discovery, no identity/PIN leak to media host, accurate bytes/timing/manual report, saved connection reading+Auto budget, Testing state, profile-default immediate buffering save and override clearing, Never-mode hiding,429 expiration, distinct save failure, playback exclusion and settings-unload cancellation; native800/360 screenshots captured. No live server credentials or physical speed benchmark used.");
        }
        finally { observer.Retire(); parent.Children.Remove(control); fixtureWindow.AppWindow.Resize(originalWindowSize); slot.SetValue(null, original); }
    }
    private static async Task CheckNativeActivityLifecycleAsync(PlayerService player, PlaybackConnectionService observer, ProbeServer server)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        using var mpv = new SiloPlayer.Player.MpvPlayer();
        typeof(PlayerService).GetField("_mpv", flags)!.SetValue(player, mpv);
        typeof(PlayerService).GetField("<State>k__BackingField", flags)!.SetValue(player, PlayerState.Minimized);
        typeof(PlayerService).GetMethod("WireMpvEvents", flags)!.Invoke(player, null);
        bool Active() => (bool)typeof(PlaybackConnectionService).GetField("_mediaActive", flags)!.GetValue(observer)!;
        var pause = (Action<bool>)typeof(PlayerService).GetField("_mpvPauseHandler", flags)!.GetValue(player)!;
        var buffer = (Action<bool>)typeof(PlayerService).GetField("_mpvBufferingHandler", flags)!.GetValue(player)!;
        try
        {
            observer.SetMediaActive(true); pause(true);
            Check(!Active(), "A genuine native pause still blocks Test speed.");
            var mediaBefore = server.MediaRequests; var reportsBefore = server.Reports;
            var probe = observer.TestAsync("fixture-native-paused", false, "ethernet");
            await Until(() => server.MediaRequests > mediaBefore);
            pause(false); Check(Active(), "Resuming native playback did not reclaim the connection.");
            try { await probe; throw new InvalidOperationException("Resuming playback did not cancel the running speed test."); }
            catch (OperationCanceledException) { }
            Check(server.Reports == reportsBefore, "Native resume saved a cancelled speed test.");
            typeof(PlayerService).GetProperty(nameof(PlayerService.IsLoading))!.SetValue(player, true);
            pause(true); Check(Active(), "A native loading pause released the connection hold.");
            typeof(PlayerService).GetProperty(nameof(PlayerService.IsLoading))!.SetValue(player, false);
            typeof(PlayerService).GetField("_switchingContent", flags)!.SetValue(player, true);
            pause(true); Check(Active(), "A loading pause released the connection hold.");
            typeof(PlayerService).GetField("_switchingContent", flags)!.SetValue(player, false);
            buffer(true); Check(Active(), "A cache pause released the connection hold.");
            buffer(false); Check(!Active(), "A loaded paused player did not release its connection hold.");
            pause(false); player.SetState(PlayerState.Idle); Check(!Active(), "An idle native player retained its connection hold.");
        }
        finally
        {
            typeof(PlayerService).GetMethod("UnwireMpvEvents", flags)!.Invoke(player, null);
            typeof(PlayerService).GetField("_mpv", flags)!.SetValue(player, null);
            typeof(PlayerService).GetField("_switchingContent", flags)!.SetValue(player, false);
        }
    }
    private static async Task Capture(FrameworkElement control, string name) { control.UpdateLayout(); await Task.Delay(60); await MediaParityNativeFixture.CaptureAsync(control, name); }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> value, int seconds = 5) { var clock = Stopwatch.StartNew(); while (!value() && clock.Elapsed.TotalSeconds < seconds) await Task.Delay(20); Check(value(), "Native speed fixture timed out."); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    { if (node is T value) yield return value; for (var i=0; i<VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(node,i))) yield return child; }
    private sealed class Services(IServiceProvider original, PlaybackApi api, SettingsViewModel vm, PlayerService player) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(PlaybackApi) ? api : type == typeof(SettingsViewModel) ? vm : type == typeof(PlayerService) ? player : original.GetService(type); }

    private sealed class ProbeServer : IDisposable
    {
        private readonly HttpListener _listener = new(); private readonly CancellationTokenSource _stop = new();
        internal string Origin { get; }
        internal int Reports, MediaRequests, Writes, Clears, ReportedRate;
        internal long RequestedSize;
        internal bool Manual, ProxyIdentityFree = true, AccountIdentityPresent = true, NoPinGrant = true, ProfileDefault, RateLimited, RejectSave, RejectPreferenceRead;
        private string _mode = "always";
        internal ProbeServer()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Prefixes.Add(Origin.Replace("127.0.0.1", "localhost") + "/"); _listener.Start();
            _ = Task.Run(async () => { while (!_stop.IsCancellationRequested) { HttpListenerContext ctx; try { ctx = await _listener.GetContextAsync(); } catch when (_stop.IsCancellationRequested) { break; } _ = Serve(ctx); } });
        }
        private async Task Serve(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url!.AbsolutePath;
                if (path == "/api/v2/playback/bandwidth-probe")
                {
                    AccountIdentityPresent &= ctx.Request.Headers["Authorization"] != null && ctx.Request.Headers["X-Profile-Id"] != null && ctx.Request.Headers["X-Silo-Device-Id"] != null;
                    NoPinGrant &= ctx.Request.Headers["X-Profile-Token"] == null;
                    RequestedSize = long.Parse(ctx.Request.QueryString["size"]!);
                    ctx.Response.StatusCode = 307; ctx.Response.RedirectLocation = Origin.Replace("127.0.0.1", "localhost") + "/stream/bandwidth-probe/fixture?size=" + RequestedSize; return;
                }
                if (path == "/stream/bandwidth-probe/fixture")
                {
                    Interlocked.Increment(ref MediaRequests);
                    ProxyIdentityFree &= ctx.Request.Headers["Authorization"] == null && ctx.Request.Headers["X-Profile-Id"] == null && ctx.Request.Headers["X-Profile-Token"] == null && ctx.Request.Headers["X-Silo-Device-Id"] == null;
                    if (RateLimited) { ctx.Response.StatusCode = 429; ctx.Response.Headers["Retry-After"] = "2"; return; }
                    ctx.Response.ContentType = "application/octet-stream"; ctx.Response.ContentLength64 = 6 * 65536; ctx.Response.Headers["Cache-Control"] = "no-store";
                    var bytes = new byte[65536];
                    for (var i=0;i<6;i++) { await ctx.Response.OutputStream.WriteAsync(bytes, _stop.Token); await ctx.Response.OutputStream.FlushAsync(_stop.Token); await Task.Delay(300, _stop.Token); }
                    return;
                }
                if (path.EndsWith("/results"))
                {
                    using var json = await JsonDocument.ParseAsync(ctx.Request.InputStream); ReportedRate = json.RootElement.GetProperty("kbps").GetInt32(); Manual = json.RootElement.GetProperty("manual").GetBoolean();
                    if (RejectSave) { ctx.Response.StatusCode = 503; return; }
                    Interlocked.Increment(ref Reports); ctx.Response.StatusCode = 204; return;
                }
                if (path.Contains("/settings/values/playback.buffering_quality_mode"))
                {
                    if (ctx.Request.HttpMethod == "DELETE") { Interlocked.Increment(ref Clears); ctx.Response.StatusCode = 204; return; }
                    using var json = await JsonDocument.ParseAsync(ctx.Request.InputStream); _mode = json.RootElement.GetProperty("value").GetString()!; ProfileDefault = ctx.Request.QueryString["scope"] == "profile"; Interlocked.Increment(ref Writes);
                    await Json(ctx, new { }); return;
                }
                if (path == "/api/v2/playback/capabilities") { await Json(ctx, new { features = new[] { "connection_speed_v1", "buffering_quality_v1" }, bandwidth_probe_url = "/api/v2/playback/bandwidth-probe?media_path=true", bandwidth_probe_automatic = false }); return; }
                if (path == "/api/v2/settings/values/effective") { if (RejectPreferenceRead) { ctx.Response.StatusCode = 503; return; } await Json(ctx, new { items = new[] { new { key = "playback.buffering_quality_mode", value = _mode, source = Clears == 0 ? "profile_device" : "profile" } } }); return; }
                if (path == "/api/v2/playback/connection")
                {
                    await Json(ctx, new { connection = "local", auto_budget_kbps = 5000, probe = Reports == 0 ? null : (object)new { kbps = ReportedRate, measured_at = DateTimeOffset.UtcNow, transport = "ethernet", lower_bound = false }, learned_limit = new { kbps = 5600, observed_at = DateTimeOffset.UtcNow, expires_at = DateTimeOffset.UtcNow.AddHours(24) } }); return;
                }
                ctx.Response.StatusCode = 404;
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { }
            finally { ctx.Response.Close(); }
        }
        private static async Task Json(HttpListenerContext ctx, object value) { var bytes = JsonSerializer.SerializeToUtf8Bytes(value); ctx.Response.ContentType = "application/json"; ctx.Response.ContentLength64 = bytes.Length; await ctx.Response.OutputStream.WriteAsync(bytes); }
        public void Dispose() { _stop.Cancel(); _listener.Close(); _stop.Dispose(); }
    }
}

