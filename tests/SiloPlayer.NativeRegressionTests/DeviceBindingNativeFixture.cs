using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class DeviceBindingNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Wire.Server);
        var api = new AuthApi(client); using var auth = new AuthService(client, api);
        var settingsApi = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "device-binding-settings"));
        var vm = new LoginViewModel(auth, api, settingsApi, client);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(api)
            .AddSingleton(vm).AddSingleton(new NavigationService()).AddSingleton(new ThemeService(settings, settingsApi)).BuildServiceProvider();
        field.SetValue(null, services);
        LoginPage? page = null;
        try
        {
            await vm.LoadAuthInfoCommand.ExecuteAsync(null);
            page = new LoginPage { Width = 900, Height = 800 }; parent.Children.Add(page);
            await Task.Delay(100); page.UpdateLayout();
            Button Button(string label) => All<Button>(page).Single(b => Equals(b.Content, label));
            async Task Start()
            {
                await Until(() => Button("Show QR code").IsEnabled);
                var capabilities = wire.Capabilities;
                Click(Button("Show QR code"));
                await Until(() => wire.Capabilities > capabilities);
                await Until(() => !vm.IsStartingDeviceLogin);
            }
            void Reset() { vm.CancelDeviceLogin(true); wire.PollGate?.TrySetResult(Reply(new { status = "pending" })); wire.PollGate = null; wire.PollFailure = null; }
            var succeeded = 0; vm.LoginSucceeded += () => ++succeeded;

            wire.PollGate = Gate(); await Start(); await Until(() => wire.Polls.Count == 1);
            Check(wire.Capabilities == 1 && wire.Starts.Count == 1 && wire.Starts[0].Binding is { Length: 43 }, "Normal login did not negotiate and send binding");
            VerifyPair(wire.Starts[0].Binding!, wire.Polls[0].Secret);
            Check(vm.DeviceQrImage is not null && vm.FormattedDeviceCode == "1234 5678" && vm.DeviceStatusMessage.Contains("Waiting"), "Native QR/code/waiting entrypoint did not render");
            Check(vm.DeviceBindingNotice is null, "Bound setup showed the unbound notice");
            Check(!All<TextBlock>(page).Any(t => t.Text.Contains(wire.Polls[0].Secret!)), "Binding secret appeared on the native login page");
            var oldSecret = wire.Polls[0].Secret;
            wire.PollGate.TrySetResult(Reply(new { status = "pending", opened = true })); wire.PollGate = Gate();
            await Until(() => wire.Polls.Count >= 2);
            Check(wire.Polls[1].Secret == oldSecret, "Poll retry rotated the setup secret");
            wire.PollGate.TrySetResult(Reject("temporary_failure", HttpStatusCode.ServiceUnavailable)); wire.PollGate = Gate();
            await Until(() => wire.Polls.Count >= 3);
            Check(wire.Polls[2].Secret == oldSecret, "Transient poll failure lost binding");
            Click(Button("Start over")); await Until(() => vm.DeviceSession is null);
            var abandoned = wire.PollGate; wire.PollGate = Gate();
            await Start(); await Until(() => wire.Polls.Count >= 4);
            Check(wire.Starts[1].Binding != wire.Starts[0].Binding && wire.Polls[3].Secret != oldSecret, "Replacement setup reused secret");
            abandoned.TrySetResult(Approved()); await Task.Delay(80);
            Check(auth.CurrentUser is null && succeeded == 0 && vm.DeviceSession?.DeviceCode == "device-2", "Canceled poll applied tokens to replacement");
            wire.PollGate.TrySetResult(Reject("device_binding_mismatch", HttpStatusCode.Forbidden, wire.Polls[3].Secret)); wire.PollGate = null;
            await Until(() => vm.DeviceSession is null);
            Check(vm.ErrorMessage == "Phone sign-in could not verify this setup. Start a new code to try again." && auth.CurrentUser is null,
                "Binding refusal is not a distinct sanitized terminal result");
            var count = wire.Polls.Count; await Task.Delay(1150); Check(wire.Polls.Count == count, "Binding refusal retried or downgraded");
            Program.Log("PASS native bound login negotiation, QR/code, stable retries, rotation, canceled success and sanitized binding refusal");

            foreach (var status in new[] { HttpStatusCode.RequestTimeout, (HttpStatusCode)425, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
            {
                Reset(); wire.PollGate = Gate(); var pollsBefore = wire.Polls.Count;
                await Start(); await Until(() => wire.Polls.Count > pollsBefore);
                var session = vm.DeviceSession; var secret = wire.Polls[^1].Secret;
                var startsBefore = wire.Starts.Count; var pollsAfterStart = wire.Polls.Count;
                var failure = Reject("slow_down", status);
                failure.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                wire.PollGate.TrySetResult(failure); wire.PollGate = Gate();
                await Until(() => wire.Polls.Count > pollsAfterStart);
                // Allow timer granularity without accepting the one-second poll interval.
                Check(elapsed.Elapsed >= TimeSpan.FromMilliseconds(1900), "Transient poll retry ignored Retry-After");
                Check(ReferenceEquals(vm.DeviceSession, session) && wire.Starts.Count == startsBefore &&
                    wire.Polls[^1].Secret == secret && vm.ErrorMessage is null && auth.CurrentUser is null,
                    "Transient HTTP poll failure retired or changed the bound setup");
            }
            Program.Log("PASS native 408/425/429/503 poll retries honor Retry-After and retain the same bound setup");

            Reset(); wire.PollGate = Gate(); var pacedPolls = wire.Polls.Count;
            await Start(); await Until(() => wire.Polls.Count > pacedPolls);
            var pacedSession = vm.DeviceSession; var pacedSecret = wire.Polls[^1].Secret; pacedPolls = wire.Polls.Count;
            var paced = System.Diagnostics.Stopwatch.StartNew();
            wire.PollGate.TrySetResult(Reply(new { status = "pending", poll_after = 2 })); wire.PollGate = Gate();
            await Until(() => wire.Polls.Count > pacedPolls);
            // Allow timer granularity without accepting the one-second start interval.
            Check(paced.Elapsed >= TimeSpan.FromMilliseconds(1900), "Pending poll ignored the server's poll_after pacing");
            Check(ReferenceEquals(vm.DeviceSession, pacedSession) && wire.Polls[^1].Secret == pacedSecret && vm.ErrorMessage is null,
                "Paced pending poll retired or changed the bound setup");
            // A year exceeds what Task.Delay accepts; the hint must be bounded, not fatal.
            var oversized = Reject("slow_down", HttpStatusCode.TooManyRequests);
            oversized.Headers.RetryAfter = new(TimeSpan.FromDays(365));
            wire.PollGate.TrySetResult(oversized); wire.PollGate = Gate();
            await Task.Delay(300);
            Check(pacedSession is not null && ReferenceEquals(vm.DeviceSession, pacedSession) && vm.ErrorMessage is null && auth.CurrentUser is null,
                "Oversized Retry-After retired the bound setup");
            Program.Log("PASS native pending polls honor poll_after and an oversized Retry-After retains the bound setup");

            Reset(); wire.PollFailure = "timeout"; wire.PollGate = Gate();
            var timeoutPolls = wire.Polls.Count; await Start();
            await Until(() => wire.Polls.Count >= timeoutPolls + 2);
            Check(wire.Polls[^1].Secret == wire.Polls[^2].Secret && vm.DeviceSession is not null, "HTTP timeout retired or changed the bound poll owner");
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity })
            {
                wire.PollGate.TrySetResult(Reject("validation_failed", status, wire.Polls[^1].Secret)); wire.PollGate = null;
                await Until(() => vm.DeviceSession is null);
                Check(vm.ErrorMessage == "Phone sign-in failed. Start a new code to try again." && auth.CurrentUser is null,
                    "Bound poll refusal leaked details, retried or applied a session");
                Reset(); wire.PollGate = Gate(); await Start(); await Until(() => vm.IsDevicePolling);
            }
            Reset(); count = wire.Polls.Count;
            Program.Log("PASS native HTTP timeout retains binding and 401/403/404/422 poll refusals terminate without downgrade or detail leakage");

            foreach (var mode in new[] { "negative", "absent", "legacy404", "legacy405" })
            {
                Reset(); wire.CapabilityMode = mode; wire.PollGate = Gate(); await Start();
                await Until(() => wire.Polls.Count > count); count = wire.Polls.Count;
                Check(wire.Starts[^1].Binding is null && wire.Polls[^1].Secret is null, "Legacy capability flow sent binding fields");
                Check(vm.DeviceBindingNotice is { Length: > 0 } notice && All<TextBlock>(page).Any(t => t.Text == notice && t.Visibility == Visibility.Visible),
                    "Unbound setup did not tell the user it is not bound");
            }
            foreach (var mode in new[] { "failure", "transport", "timeout", "malformed" })
            {
                Reset(); wire.CapabilityMode = mode; var starts = wire.Starts.Count;
                await Start(); Check(wire.Starts.Count == starts && vm.DeviceSession is null && vm.ErrorMessage is not null,
                    "Capability failure silently started an unbound request");
            }
            Program.Log("PASS native capability false/absent/404/405 compatibility and 5xx/transport/malformed failure policy");

            Reset(); wire.CapabilityMode = "positive";
            client.SetBaseUrl("http://device-binding-fixture.invalid");
            string? previousHttpSecret = null;
            for (var retry = 0; retry < 2; ++retry)
            {
                Reset(); wire.PollGate = Gate();
                var httpStarts = wire.Starts.Count; var httpPolls = wire.Polls.Count;
                await Start(); await Until(() => wire.Polls.Count > httpPolls);
                Check(wire.Starts.Count == httpStarts + 1 && wire.Polls.Count == httpPolls + 1 &&
                    vm.DeviceSession is not null && vm.DeviceQrImage is not null && vm.IsDevicePolling && vm.ErrorMessage is null,
                    "Plain HTTP server did not retain phone sign-in");
                VerifyPair(wire.Starts[^1].Binding!, wire.Polls[^1].Secret);
                Check(wire.Polls[^1].Secret != previousHttpSecret, "Replacement HTTP setup reused the binding secret");
                previousHttpSecret = wire.Polls[^1].Secret;
            }
            Reset(); client.SetBaseUrl(Wire.Server);
            Program.Log("PASS native plain HTTP phone sign-in retains binding, QR/code and setup rotation without downgrade");

            Reset(); wire.CapabilityMode = "positive"; wire.CapabilityGate = Gate();
            Click(Button("Show QR code")); await Until(() => vm.IsStartingDeviceLogin);
            vm.CancelDeviceLogin(true); var before = wire.Starts.Count;
            wire.CapabilityGate.TrySetResult(Reply(new { session_binding = true })); wire.CapabilityGate = null;
            await Task.Delay(100); Check(wire.Starts.Count == before, "Canceled capability response started a request");

            wire.CapabilityGate = Gate();
            var staleCapability = vm.StartDeviceLoginAsync(); await Until(() => vm.IsStartingDeviceLogin);
            client.SetBaseUrl("https://replacement-device.invalid"); client.SetBaseUrl(Wire.Server);
            wire.CapabilityGate.TrySetResult(Reply(new { session_binding = true })); wire.CapabilityGate = null;
            await staleCapability;
            Check(wire.Starts.Count == before && vm.DeviceSession is null, "Server ABA during negotiation sent a stale bound start");

            wire.StartGate = Gate(); var starting = vm.StartDeviceLoginAsync(); await Until(() => wire.Starts.Count > before);
            vm.CancelDeviceLogin(true); var lateStart = wire.StartGate; wire.StartGate = null;
            wire.PollGate = Gate(); await Start(); var replacement = vm.DeviceSession;
            lateStart.TrySetResult(Session("abandoned-start")); await starting;
            // HttpClient may discard a canceled response before its device code
            // reaches the VM; in that case cancellation by that code is impossible.
            Check(ReferenceEquals(vm.DeviceSession, replacement) && !wire.Polls.Any(p => p.Code == "abandoned-start"), "Late canceled start replaced or polled under the current owner");
            Reset();

            wire.PollGate = Gate(); await Start(); var latePoll = wire.PollGate;
            client.SetBaseUrl("https://replacement-device.invalid"); client.SetBaseUrl(Wire.Server);
            latePoll.TrySetResult(Approved()); wire.PollGate = null;
            await Task.Delay(100); Check(auth.CurrentUser is null && succeeded == 0, "Server ABA accepted a bound response");
            Reset();
            wire.PollGate = Gate(); await Start(); latePoll = wire.PollGate;
            auth.SetTokens("fixture-replacement-access", "fixture-replacement-refresh", 3600);
            latePoll.TrySetResult(Approved()); wire.PollGate = null;
            await Task.Delay(100); Check(auth.CurrentUser is null && succeeded == 0, "Changed authentication owner accepted a bound response");
            Reset();
            foreach (var terminal in new[] { "denied", "expired", "consumed", "canceled" })
            {
                wire.Terminal = terminal; await Start(); await Until(() => vm.DeviceSession is null);
                Check(vm.ErrorMessage == (terminal == "denied" ? "Approval was denied. Start a new code to try again." : "This code is no longer valid. Start a new one."), "Denial/expiry copy changed");
            }
            wire.Terminal = null; wire.PollGate = Gate(); await Start();
            wire.PollGate.TrySetResult(Approved()); wire.PollGate = null;
            await Until(() => succeeded == 1);
            Check(auth.CurrentUser?.Id == "binding-user" && wire.Refreshes == 0 && !wire.Authenticated, "Bound normal login did not complete exactly one public bootstrap session");
            Program.Log("PASS native canceled capability, late canceled start, server ABA, denial/expiry and successful bound account login");
        }
        finally
        {
            vm.CancelAuthFlows(); wire.CapabilityGate?.TrySetCanceled(); wire.StartGate?.TrySetCanceled(); wire.PollGate?.TrySetCanceled();
            if (page is not null) parent.Children.Remove(page); field.SetValue(null, previous);
        }
    }

    private static void VerifyPair(string hash, string? secret)
    {
        Check(secret is { Length: 43 } && !secret.Contains('='), "Poll secret encoding is not unpadded base64url");
        var raw = Convert.FromBase64String(secret!.Replace('-', '+').Replace('_', '/') + "=");
        Check(raw.Length == 32 && Convert.ToBase64String(SHA256.HashData(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_') == hash,
            "Start hash does not match 32-byte poll secret");
    }
    private static TaskCompletionSource<HttpResponseMessage> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static HttpResponseMessage Reply(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Reject(string code, HttpStatusCode status, string? echo = null) => Reply(new { type = "https://siloserver.org/docs/api/v2/problems/" + code, detail = echo ?? "Fixture refusal" }, status);
    private static HttpResponseMessage Approved() => Reply(new { status = "approved", tokens = new { access_token = "fixture-binding-access", refresh_token = "fixture-binding-refresh", expires_in = 86400, user = new { id = "binding-user", username = "fixture", role = "user" } } });
    private static HttpResponseMessage Session(string code) => Reply(new { device_code = code, user_code = "12345678", verification_uri = Wire.Server + "/activate", verification_uri_complete = Wire.Server + "/activate?code=12345678", interval = 1 });
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static async Task Until(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(6); while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("Device binding boundary did not complete"); await Task.Delay(15); } }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T own) yield return own; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Wire : HttpMessageHandler
    {
        internal const string Server = "https://device-binding-fixture.invalid";
        internal string CapabilityMode = "positive";
        internal string? PollFailure, Terminal;
        internal int Capabilities, Refreshes;
        internal bool Authenticated;
        internal readonly List<(string Code, string? Binding)> Starts = [];
        internal readonly List<(string Code, string? Secret)> Polls = [];
        internal readonly List<string> Canceled = [];
        internal TaskCompletionSource<HttpResponseMessage>? CapabilityGate, StartGate, PollGate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/v2/auth/device"))
            {
                Authenticated |= request.Headers.Authorization is not null || request.Headers.Contains("X-Profile-Token");
                Check(request.RequestUri.Host == new Uri(Server).Host, "Device bootstrap changed server");
            }
            if (path.EndsWith("/device/capability"))
            {
                ++Capabilities;
                if (CapabilityGate is { } gate) return await gate.Task;
                return CapabilityMode switch
                {
                    "negative" => Reply(new { session_binding = false }), "absent" => Reply(new { protocol_versions = new[] { 2 } }),
                    "legacy404" => Reject("not_found", HttpStatusCode.NotFound), "legacy405" => Reject("not_allowed", HttpStatusCode.MethodNotAllowed),
                    "failure" => Reject("server_error", HttpStatusCode.ServiceUnavailable), "transport" => throw new HttpRequestException("Synthetic offline"), "timeout" => throw new TaskCanceledException("Synthetic HTTP timeout"),
                    "malformed" => Reply(new { session_binding = "true" }), _ => Reply(new { session_binding = true })
                };
            }
            if (path.EndsWith("/device/start"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = json.RootElement;
                Check(body.EnumerateObject().Select(p => p.Name).Order().SequenceEqual((body.TryGetProperty("binding", out var binding) ? new[] { "binding", "device_name", "device_platform" } : new[] { "device_name", "device_platform" }).Order()), "Start body contains unexpected fields");
                var code = "device-" + (Starts.Count + 1); Starts.Add((code, binding.ValueKind == JsonValueKind.String ? binding.GetString() : null));
                return StartGate is { } gate ? await gate.Task : Session(code);
            }
            if (path.EndsWith("/device/poll"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = json.RootElement;
                Check(body.EnumerateObject().Count() == (body.TryGetProperty("binding_secret", out var secret) ? 2 : 1), "Poll body contains unexpected fields");
                Polls.Add((body.GetProperty("device_code").GetString()!, secret.ValueKind == JsonValueKind.String ? secret.GetString() : null));
                if (PollFailure == "timeout") { PollFailure = null; throw new TaskCanceledException("Synthetic HTTP timeout"); }
                return PollGate is { } gate ? await gate.Task : Terminal is { } terminal ? Reply(new { status = terminal }) : Reply(new { status = "pending" });
            }
            if (path.EndsWith("/device/cancel")) { using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Canceled.Add(json.RootElement.GetProperty("device_code").GetString()!); return new(HttpStatusCode.NoContent); }
            if (path == "/api/v2/auth/refresh") { ++Refreshes; throw new InvalidOperationException("Unexpected refresh"); }
            if (path == "/api/v2/auth/providers") return Reply(new { items = new[] { new { id = "local", display_name = "Password", mode = "credentials" } }, password_login = true });
            if (path == "/api/v2/theme/branding") return Reply(new { server_name = "Fixture Silo" });
            if (path == "/api/v2/capabilities/password-reset") return Reply(new { state = "unavailable" });
            if (path == "/api/v2/auth/signup") return Reply(new { enabled = false });
            return Reply(new { items = Array.Empty<object>() });
        }
    }
}
