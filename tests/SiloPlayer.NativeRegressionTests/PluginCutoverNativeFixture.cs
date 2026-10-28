using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Views;

// Actual WebView2 navigation against an isolated loopback server. No user account,
// app data, installed-player process or production endpoint participates.
internal static class PluginCutoverNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        // The frozen pre-cutover app may serve its old entry page only in the
        // explicitly selected redirect-only baseline. Normal cutoff runs always
        // tombstone v1. The redirect probe then tests the real header boundary.
        var redirectOnly = Environment.GetEnvironmentVariable("SILO_PLUGIN_CUTOVER_CASE") == "redirect";
        await using var external = new Server(external: true);
        await using var server = new Server(allowLegacyPages: redirectOnly) { RedirectOrigin = external.Origin };
        external.ReturnOrigin = server.Origin;
        var api = new SiloApiClient(new HttpClient());
        api.SetBaseUrl(server.Origin);
        api.BeginAuthenticationSession("synthetic-access");
        api.SetProfile("synthetic-profile", "synthetic-proof");
        api.SetDeviceMetadata("synthetic-device", "Synthetic desktop", "Windows");
        var slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = slot.GetValue(null);
        using var services = new ServiceCollection().AddSingleton(api).BuildServiceProvider();
        var userData = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Program.ResultDirectory, "plugin-webview"));
        slot.SetValue(null, services);
        var frame = new Frame { Width = 800, Height = 600 };
        parent.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(PluginRoutePage), new PluginRoutePage.NavigationArgs(7, "/*", "Synthetic plugin"));
            var page = (PluginRoutePage)frame.Content;
            var web = (WebView2)page.FindName("PluginWebView");
            await Until(() => server.PageReads > 0 || server.LegacyReads > 0);
            await Until(() => server.ProbeReads > 0 || server.LegacyReads > 0);
            Check((redirectOnly || server.LegacyReads == 0) && server.ProbeReads > 0,
                $"Plugin launch still depends on the v1 bridge: legacy={server.LegacyReads}, v2Probe={server.ProbeReads}");
            await UntilScript(web, "document.body.dataset.ready === 'yes' && getComputedStyle(document.body).getPropertyValue('--cutover-ready').trim() === 'yes'");
            Check(server.AssetReads > 0 && server.StyleReads > 0 && server.ScriptReads > 0,
                "The real plugin document did not load relative JS/CSS and the v2 static asset mount");
            Check(server.Requests.Where(r => r.Path.StartsWith("/api/v2/", StringComparison.Ordinal)).All(r => r.OwnerMatches),
                "Plugin page or resources lost the captured bearer/profile/PIN authority");
            await CheckExternalRedirectAsync(web, page, server, external);
            if (redirectOnly) return;
            await web.ExecuteScriptAsync("document.getElementById('submit').click()");
            await Until(() => server.FormWrites == 1);
            await UntilScript(web, "document.body.dataset.submitted === 'yes'");
            Check(server.Requests.Any(r => r.Path.EndsWith("/submitted", StringComparison.Ordinal) && r.CredentialHeaders.Contains("Cookie", StringComparer.OrdinalIgnoreCase)),
                "Public-resource isolation destroyed the same-origin plugin session cookie");
            Program.Log("PASS: actual WebView v2 page, directory-relative JS/CSS/fetch, static asset and form succeed with v1 tombstones enabled.");

            await web.ExecuteScriptAsync("fetch('/api/v1/plugins/7/stale').then(r => document.body.dataset.legacyStatus = String(r.status))");
            await UntilScript(web, "document.body.dataset.legacyStatus === '410'");
            Check(server.LegacyReads == 0, "A plugin-owned absolute v1 fetch reached the retired server route");
            Check(((TextBlock)page.FindName("ErrorText")).Text.Contains("retired", StringComparison.OrdinalIgnoreCase),
                "Legacy plugin content was refused without explaining the required plugin update");

            api.SetProfile("new-owner", "new-proof");
            await web.ExecuteScriptAsync("fetch('./owner-check').then(r => document.body.dataset.ownerStatus = String(r.status))");
            await UntilScript(web, "document.body.dataset.ownerStatus === '403'");
            Check(server.OwnerReads == 0, "The old plugin document dispatched a request under the replacement profile");
            Check(((TextBlock)page.FindName("ErrorText")).Text.Contains("reopen", StringComparison.OrdinalIgnoreCase),
                "Retired plugin ownership did not explain how to reopen under the active profile");
            Program.Log("PASS: stale plugin v1 fetch is refused before network, and a changed owner cannot lend bearer/PIN authority to the old document.");
        }
        finally
        {
            frame.Navigate(typeof(Page));
            parent.Children.Remove(frame);
            slot.SetValue(null, original);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userData);
        }
    }

    private static async Task CheckExternalRedirectAsync(WebView2 web, PluginRoutePage page, Server server, Server external)
    {
        // Cookies are host scoped, not port scoped. A deliberately broad synthetic
        // session cookie makes a redirect to another loopback port a real leak
        // candidate even if Chromium strips Authorization by itself.
        await web.ExecuteScriptAsync("window.redirectProbe = new Image(); window.redirectProbe.onload = () => document.body.dataset.redirectReady = 'yes'; window.redirectProbe.src = './redirect-external';");
        await Until(() => external.Requests.Any(r => r.Path == "/external-target"));
        var redirected = external.Requests.Where(r => r.Path == "/external-target").ToArray();
        Check(redirected.All(r => r.CredentialHeaders.Length == 0),
            "Cross-origin plugin redirect leaked identity headers: " + string.Join(", ", redirected.SelectMany(r => r.CredentialHeaders).Distinct()));
        await UntilScript(web, "document.body.dataset.redirectReady === 'yes'");
        Program.Log("PASS: redirected public image loaded with no bearer/profile/PIN/device/cookie values on the external wire.");
        Check(!(await web.CoreWebView2.CookieManager.GetCookiesAsync(server.Origin)).Any(cookie => cookie.Name == "external_cookie"),
            "A public external response installed its cookie in the plugin browser jar");
        Check(server.Requests.Any(r => r.Path.EndsWith("/redirect-external", StringComparison.Ordinal) && r.OwnerMatches),
            "The redirect probe did not begin as a real authenticated same-origin plugin request");
        await web.ExecuteScriptAsync("window.externalStyle = document.createElement('link'); window.externalStyle.rel = 'stylesheet'; window.externalStyle.href = '" + external.Origin + "/a.css'; document.head.appendChild(window.externalStyle);");
        await UntilScript(web, "getComputedStyle(document.body).getPropertyValue('--external-css').trim() === 'yes'");
        await Until(() => external.Requests.Any(r => r.Path is "/nested/relative.png" or "/relative.png"));
        Check(external.Requests.Any(r => r.Path == "/nested/relative.png") && !external.Requests.Any(r => r.Path == "/relative.png"),
            "Redirected external stylesheet lost its final response URL for relative image resolution");
        await web.ExecuteScriptAsync("window.returnProbe = new Image(); window.returnProbe.onerror = () => document.body.dataset.returnBlocked = 'yes'; window.returnProbe.src = './redirect-return';");
        await UntilScript(web, "document.body.dataset.returnBlocked === 'yes'");
        Check(external.Requests.Any(r => r.Path == "/external-return") &&
            !server.Requests.Any(r => r.Path.EndsWith("/redirect-return-target", StringComparison.Ordinal)),
            "External resource redirect reentered the connected server instead of refusing it");
        await web.ExecuteScriptAsync("window.loopProbe = new Image(); window.loopProbe.onerror = () => document.body.dataset.loopBlocked = 'yes'; window.loopProbe.src = './redirect-loop';");
        await UntilScript(web, "document.body.dataset.loopBlocked === 'yes'");
        var loopReads = external.Requests.Count(r => r.Path == "/external-loop");
        Program.Log("TRACE: public redirect loop terminated after " + loopReads + " credential-free wire requests.");
        Check(loopReads > 1 && loopReads <= 21,
            "External public-resource redirect loop did not terminate within Chromium's finite redirect budget");
        Check(external.Requests.All(r => r.CredentialHeaders.Length == 0),
            "A later public-resource redirect hop received desktop identity or cookies");
        await web.ExecuteScriptAsync("window.location.href = '" + external.Origin + "/external-document'");
        await Task.Delay(180);
        Check(!external.Requests.Any(r => r.Path == "/external-document"),
            "External main-frame navigation escaped the plugin boundary");
        Check(web.CoreWebView2.Source.StartsWith(server.Origin + "/", StringComparison.Ordinal),
            "Cancelled external navigation replaced the active plugin document");
        Check(((TextBlock)page.FindName("ErrorText")).Text.Contains("stay on this server", StringComparison.OrdinalIgnoreCase),
            "Cancelled external navigation lost its actionable boundary message: " + ((TextBlock)page.FindName("ErrorText")).Text);
        Program.Log("PASS: actual two-origin public subresource loads without bearer/profile/PIN/device/cookies; foreign cookies, redirect-back, loops and external main navigation stay blocked.");
    }

    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(40);
        Check(ready(), "Plugin fixture timed out waiting for the real WebView request");
    }

    private static async Task UntilScript(WebView2 web, string expression)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await web.ExecuteScriptAsync(expression) == "true") return;
            await Task.Delay(40);
        }
        throw new InvalidOperationException("The real plugin document did not reach its expected state: " + expression);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed record Request(string Path, bool OwnerMatches, string[] CredentialHeaders);

    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly bool _external, _allowLegacyPages;
        internal readonly ConcurrentQueue<Request> Requests = new();
        internal int LegacyReads, PageReads, ProbeReads, ScriptReads, StyleReads, AssetReads, FormWrites, OwnerReads;
        internal string Origin { get; }
        internal string? RedirectOrigin { get; init; }
        internal string? ReturnOrigin { get; set; }
        internal Server(bool external = false, bool allowLegacyPages = false)
        {
            _external = external;
            _allowLegacyPages = allowLegacyPages;
            _listener.Start();
            Origin = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Loop();
        }
        private async Task Loop()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = Serve(connection);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        private async Task Serve(TcpClient connection)
        {
            using (connection)
            {
                try
                {
                    using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                    var start = (await reader.ReadLineAsync(_stop.Token))?.Split(' ');
                    if (start is not { Length: >= 2 }) return;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
                    }
                    if (headers.TryGetValue("Content-Length", out var size) && int.TryParse(size, out var length) && length > 0)
                    { var body = new char[length]; await reader.ReadBlockAsync(body.AsMemory(), _stop.Token); }
                    var originalPath = new Uri(Origin + start[1]).AbsolutePath;
                    var path = _allowLegacyPages && originalPath.StartsWith("/api/v1/plugins/7/", StringComparison.Ordinal)
                        ? originalPath.Replace("/api/v1/plugins/7/", "/api/v2/plugin-content/plugins/7/", StringComparison.Ordinal)
                        : originalPath;
                    var owner = headers.GetValueOrDefault("Authorization") == "Bearer synthetic-access" &&
                        headers.GetValueOrDefault("X-Profile-Id") == "synthetic-profile" &&
                        headers.GetValueOrDefault("X-Profile-Token") == "synthetic-proof";
                    var credentialNames = headers.Keys.Where(name => !string.IsNullOrWhiteSpace(headers[name]) && (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) || name.StartsWith("X-Profile-", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("X-Silo-Device-", StringComparison.OrdinalIgnoreCase))).ToArray();
                    Requests.Enqueue(new(originalPath, owner, credentialNames));
                    var status = 200;
                    var type = "text/html; charset=utf-8";
                    var text = "";
                    var extraHeaders = "";
                    if (_external)
                    {
                        extraHeaders = "Set-Cookie: external_cookie=synthetic-foreign; Path=/; HttpOnly\r\n";
                        if (path == "/external-return") { status = 302; extraHeaders += "Location: " + ReturnOrigin + "/api/v2/plugin-content/plugins/7/redirect-return-target\r\n"; }
                        else if (path == "/external-loop") { status = 302; extraHeaders += "Location: /external-loop\r\n"; }
                        else if (path == "/a.css") { status = 302; extraHeaders += "Location: /nested/b.css\r\n"; }
                        else if (path == "/nested/b.css") { type = "text/css"; text = "body { --external-css: yes; background-image: url(relative.png); }"; }
                        else { type = "image/svg+xml"; text = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><path d='M0 0h1v1H0z'/></svg>"; }
                    }
                    else if (path.StartsWith("/api/v1", StringComparison.Ordinal))
                    { Interlocked.Increment(ref LegacyReads); status = 410; text = "client_upgrade_required"; }
                    else if (!owner)
                    { status = 403; text = "Fixture profile proof required"; }
                    else if (path == "/api/v2/plugin-content/plugins/7/")
                    {
                        Interlocked.Increment(ref PageReads);
                        extraHeaders = "Set-Cookie: silo_plugin_access=synthetic-cookie; Path=/; HttpOnly; SameSite=Lax\r\n";
                        text = """<!doctype html><html><head><link rel="stylesheet" href="./style.css"><script defer src="./page.js"></script></head><body><img src="/api/v2/plugin-content/plugin-assets/7/pixel.svg"><form method="post" action="./submitted"><input name="value" value="synthetic"><button id="submit">Submit</button></form></body></html>""";
                    }
                    else if (path.EndsWith("/page.js", StringComparison.Ordinal))
                    { Interlocked.Increment(ref ScriptReads); type = "text/javascript"; text = "fetch('./probe').then(r => { if (r.ok) document.body.dataset.ready = 'yes'; });"; }
                    else if (path.EndsWith("/style.css", StringComparison.Ordinal))
                    { Interlocked.Increment(ref StyleReads); type = "text/css"; text = "body { --cutover-ready: yes; }"; }
                    else if (path.EndsWith("/probe", StringComparison.Ordinal))
                    { Interlocked.Increment(ref ProbeReads); text = "ready"; }
                    else if (path.EndsWith("/redirect-external", StringComparison.Ordinal))
                    { status = 302; extraHeaders = "Location: " + RedirectOrigin + "/external-target\r\n"; }
                    else if (path.EndsWith("/redirect-return", StringComparison.Ordinal))
                    { status = 302; extraHeaders = "Location: " + RedirectOrigin + "/external-return\r\n"; }
                    else if (path.EndsWith("/redirect-loop", StringComparison.Ordinal))
                    { status = 302; extraHeaders = "Location: " + RedirectOrigin + "/external-loop\r\n"; }
                    else if (path.EndsWith("/pixel.svg", StringComparison.Ordinal))
                    { Interlocked.Increment(ref AssetReads); type = "image/svg+xml"; text = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><path d='M0 0h1v1H0z'/></svg>"; }
                    else if (path.EndsWith("/submitted", StringComparison.Ordinal) && start[0] == "POST")
                    { Interlocked.Increment(ref FormWrites); text = "<html><body data-submitted='yes'>Submitted</body></html>"; }
                    else if (path.EndsWith("/owner-check", StringComparison.Ordinal))
                    { Interlocked.Increment(ref OwnerReads); text = "Unexpected owner dispatch"; }
                    else { status = 404; text = "Unknown fixture route"; }
                    var bytes = Encoding.UTF8.GetBytes(text);
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\n{extraHeaders}Cache-Control: no-store\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, _stop.Token);
                    await stream.WriteAsync(bytes, _stop.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _listener.Stop(); await _loop; _stop.Dispose(); }
    }
}
