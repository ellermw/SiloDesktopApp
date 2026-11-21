using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Real shell/Frame/row/WebView, with synthetic admin-only installations and a loopback plugin.</summary>
internal static class AdminPluginAppsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        await using var server = new PluginServer();
        using var inventory = new Inventory();
        using var http = new HttpClient(inventory);
        var client = new SiloApiClient(http);
        client.SetBaseUrl(server.Origin);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        await using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(new AdminPluginAppsApi(client))
            .AddSingleton(navigation).AddSingleton(new SettingsService(Program.ResultDirectory)).BuildServiceProvider();
        var slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = slot.GetValue(null);
        var previousData = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Program.ResultDirectory, "admin-plugin-webview"));
        slot.SetValue(null, services);
        var outer = new Frame { Width = 1280, Height = 800 };
        navigation.Frame = outer;
        parent.Children.Add(outer);
        var pages = new List<FrameworkElement>();
        try
        {
            Check(((Microsoft.UI.Xaml.Markup.IXamlMetadataProvider)Application.Current).GetXamlType(typeof(PluginRoutePage)) != null,
                "the reused production plugin page has real WinUI activation metadata");
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Activity)), "registered production Admin entry opens");
            var shell = (AdminShellPage)outer.Content;
            pages.Add(shell);
            await Until(() => Rows(shell).Length == 2);
            var outgoing = Rows(shell).Single(button => ((AdminPluginApp)button.Tag).InstallationId == 7);
            Check(((AdminPluginApp)outgoing.Tag).Label == "Synthetic Custom Admin", "server label is preserved verbatim");
            Check(((AdminPluginApp)outgoing.Tag).RoutePath == "/custom/*", "server route pattern is preserved");
            Check(Rows(shell).All(button => ((AdminPluginApp)button.Tag).Icon == "puzzle"), "official Puzzle icons accompany Admin routes");
            Check(!Descendants(shell).OfType<TextBlock>().Any(text => text.Text is "Disabled Admin" or "User App"), "disabled and user-only routes stay out of Admin");
            Check(inventory.UserSettingsReads == 0, "Admin-only installations are reachable without the user settings endpoint");
            await Capture(outer, "admin-plugin-apps-desktop.png");

            outer.Width = 800;
            await Task.Delay(100);
            InvokePrivate(shell, "OpenDrawer");
            await Until(() => Rows(shell).Length == 2);
            Check(Rows(shell).All(button => button.MinHeight >= 44), "mobile drawer exposes touch-sized Plugin Apps rows");
            await Capture(outer, "admin-plugin-apps-drawer.png");
            InvokePrivate(shell, "CloseDrawer");
            outer.Width = 1280;
            await Until(() => Rows(shell).Length == 2);

            var search = (Task)InvokePrivate(shell, "ShowSearchAsync")!;
            await Until(() => Field<Grid?>(shell, "_searchOverlay") != null);
            var overlay = Field<Grid>(shell, "_searchOverlay");
            Descendants(overlay).OfType<TextBox>().Single().Text = "synthetic-admin-only";
            await Until(() => Rows(overlay).Length == 1);
            Check(Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == "Plugin Apps"), "scoped search groups the custom app under Plugin Apps");
            Field<Action>(shell, "_closeSearch")();
            await search;
            Click(Rows(shell).Single(button => ((AdminPluginApp)button.Tag).InstallationId == 7));
            await Until(() => outer.Content is PluginRoutePage);
            var page = (PluginRoutePage)outer.Content;
            pages.Add(page);
            var web = (WebView2)page.FindName("PluginWebView");
            await UntilScript(web, "document.body.dataset.ready === 'yes'");
            Check(web.CoreWebView2.Profile.IsInPrivateModeEnabled && web.CoreWebView2.Profile.ProfileName == "SiloAdminPluginApps",
                "Admin content uses its own InPrivate profile rather than changing the user plugin profile");
            Check(server.Requests.Any(request => request.Path == "/api/v2/plugin-content/plugins/7/custom/probe"), "real plugin script can fetch its own dynamic endpoint");
            Check(server.Requests.Any(request => request.Path == "/api/v2/plugin-content/plugin-assets/7/pixel.svg"), "installation-scoped assets load through the real host");
            Check(server.Requests.All(request => request.Owner && !request.Cookie), "each proxied request uses captured authority and never the browser cookie jar");
            Check(new Uri(web.CoreWebView2.Source).Host.EndsWith(".invalid", StringComparison.Ordinal), "plugin script runs on a virtual origin rather than the server origin");
            await web.ExecuteScriptAsync("fetch('/api/v2/admin/users').then(() => document.body.dataset.escape='unexpected').catch(() => document.body.dataset.escape='blocked')");
            await UntilScript(web, "document.body.dataset.escape === 'blocked'");
            Check(!server.Requests.Any(request => request.Path == "/api/v2/admin/users"), "server API escape cannot dispatch credentials");
            await web.ExecuteScriptAsync("fetch('" + server.Origin + "/outside').catch(() => document.body.dataset.foreign='blocked')");
            await UntilScript(web, "document.body.dataset.foreign === 'blocked'");
            Check(!server.Requests.Any(request => request.Path == "/outside"), "real-server origin cannot bypass the virtual mount");
            await web.ExecuteScriptAsync("window.location.href='" + server.Origin + "/outside'");
            await Until(() => ((TextBlock)page.FindName("ErrorText")).Text.Contains("installation", StringComparison.OrdinalIgnoreCase));
            Check(!server.Requests.Any(request => request.Path == "/outside"), "top-level origin escape is cancelled with its boundary explanation intact");
            await web.ExecuteScriptAsync("document.getElementById('submit').click()");
            await UntilScript(web, "document.body.dataset.submitted === 'yes'");
            Check(server.Writes == 1, "plugin form is sent once and returns within the scoped host");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await Until(() => web.Visibility == Visibility.Collapsed);
            Check(((TextBlock)page.FindName("ErrorText")).Text.Contains("authority", StringComparison.OrdinalIgnoreCase), "live role loss retires the open host with a recovery message");
            Program.Log("PASS: reachable Admin shell/search/drawer rows, virtual-origin plugin scripts/assets/form, credential isolation and live host retirement.");

            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Activity)), "Admin can be reopened after a fresh grant");
            var nextShell = (AdminShellPage)outer.Content;
            pages.Add(nextShell);
            await Until(() => Rows(nextShell).Length == 2);
            var detached = Rows(nextShell)[0];
            var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            inventory.HoldNext = gate;
            var before = inventory.Reads;
            auth.SelectProfile("p2", null, new Profile { Id = "p2", Name = "Second Primary", IsPrimary = true });
            await Until(() => inventory.Reads > before && Rows(nextShell).Length == 0);
            Click(detached);
            await Task.Delay(75);
            Check(ReferenceEquals(outer.Content, nextShell), "a detached row cannot navigate after authority retirement");
            inventory.Label = "Replacement Admin App";
            auth.SelectProfile("p3", null, new Profile { Id = "p3", Name = "Third Primary", IsPrimary = true });
            await Until(() => Rows(nextShell).Any(button => ((AdminPluginApp)button.Tag).Label == "Replacement Admin App"));
            gate.SetResult(Inventory.Json(Inventory.Body("Retired Admin App")));
            await Task.Delay(150);
            Check(!Rows(nextShell).Any(button => ((AdminPluginApp)button.Tag).Label == "Retired Admin App"), "a delayed retired inventory cannot replace the current group");
            Click(outgoing);
            await Task.Delay(75);
            Check(ReferenceEquals(outer.Content, nextShell), "a departed shell's detached row cannot borrow a replacement grant");

            inventory.Fail = true;
            authority.Retire();
            await Until(() => Descendants(nextShell).OfType<Button>().Any(button => Equals(button.Tag, "AdminPluginAppsRetry")));
            inventory.Fail = false;
            Click(Descendants(nextShell).OfType<Button>().Single(button => Equals(button.Tag, "AdminPluginAppsRetry")));
            await Until(() => Rows(nextShell).Length == 2);
            Check(inventory.UserSettingsReads == 0, "retry still uses the Admin installation inventory");
            Program.Log("PASS: actual shell clears retired rows, rejects detached clicks, discards delayed reads, recaptures and retries bounded inventory.");
        }
        finally
        {
            // Close/unload pages before restoring the global provider or WebView environment.
            outer.Navigate(typeof(Page));
            outer.BackStack.Clear(); outer.ForwardStack.Clear();
            parent.Children.Remove(outer);
            await Until(() => pages.All(page => !page.IsLoaded) && !outer.IsLoaded);
            navigation.Frame = null;
            slot.SetValue(null, original);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", previousData);
        }
    }

    private static Button[] Rows(DependencyObject root) => Descendants(root).OfType<Button>().Where(button => button.Tag is AdminPluginApp).ToArray();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? InvokePrivate(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Program.Log("PASS: " + message); }
    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < end) await Task.Delay(30);
        if (!ready()) throw new TimeoutException("Admin Plugin Apps fixture did not reach its expected state.");
    }
    private static async Task UntilScript(WebView2 web, string expression)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end)
        {
            if (web.CoreWebView2 != null && await web.ExecuteScriptAsync(expression) == "true") return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Admin plugin script state: " + expression);
    }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(150);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private sealed class Inventory : HttpMessageHandler
    {
        internal int Reads, UserSettingsReads;
        internal bool Fail;
        internal string Label = "Synthetic Custom Admin";
        internal TaskCompletionSource<HttpResponseMessage>? HoldNext;
        internal static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        internal static string Body(string label) => System.Text.Json.JsonSerializer.Serialize(new
        {
            items = new object[] {
                new { id = "7", plugin_id = "synthetic-admin-only", enabled = true, routes = new[] { new { path = "/custom/*", navigable = true, navigation_kind = "admin", navigation_label = label }, new { path = "/user", navigable = true, navigation_kind = "user", navigation_label = "User App" } } },
                new { id = "8", plugin_id = "second-admin-only", enabled = true, routes = new[] { new { path = "/tools", navigable = true, navigation_kind = "admin", navigation_label = "Second Admin" } } },
                new { id = "9", plugin_id = "disabled", enabled = false, routes = new[] { new { path = "/", navigable = true, navigation_kind = "admin", navigation_label = "Disabled Admin" } } },
            }, page = new { has_more = false },
        });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/settings/plugins") Interlocked.Increment(ref UserSettingsReads);
            if (request.RequestUri.AbsolutePath == "/api/v2/admin/plugins/installations")
            {
                Interlocked.Increment(ref Reads);
                var hold = Interlocked.Exchange(ref HoldNext, null);
                if (hold != null) return hold.Task; // Deliberately ignores cancellation to test stale publication.
                return Task.FromResult(Fail ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") } : Json(Body(Label)));
            }
            return Task.FromResult(Json("{}"));
        }
    }

    private sealed record Request(string Path, bool Owner, bool Cookie);
    private sealed class PluginServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly ConcurrentBag<Task> _connections = [];
        internal readonly ConcurrentQueue<Request> Requests = new();
        internal int Writes;
        internal string Origin { get; }
        internal PluginServer()
        {
            _listener.Start(); Origin = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Loop();
        }
        private async Task Loop()
        {
            try { while (!_stop.IsCancellationRequested) _connections.Add(Serve(await _listener.AcceptTcpClientAsync(_stop.Token))); }
            catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
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
                    { var colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                    if (headers.TryGetValue("Content-Length", out var size) && int.TryParse(size, out var length) && length > 0)
                        await reader.ReadBlockAsync(new char[length].AsMemory(), _stop.Token);
                    var path = new Uri(Origin + start[1]).AbsolutePath;
                    var owner = headers.GetValueOrDefault("Authorization") == "Bearer synthetic-access" && headers.GetValueOrDefault("X-Profile-Id") == "p1";
                    Requests.Enqueue(new(path, owner, headers.ContainsKey("Cookie")));
                    var status = owner ? 200 : 403;
                    var type = "text/html; charset=utf-8";
                    var extra = "Set-Cookie: synthetic_plugin_cookie=not-for-browser; Path=/; HttpOnly\r\n";
                    var text = "";
                    if (path == "/api/v2/plugin-content/plugins/7/custom")
                    { status = 308; extra += "Location: /api/v2/plugin-content/plugins/7/custom/\r\n"; }
                    else if (path == "/api/v2/plugin-content/plugins/7/custom/")
                        text = """<!doctype html><html><head><script src="./page.js" defer></script></head><body><h1>Synthetic Admin Plugin</h1><img src="/api/v2/plugin-content/plugin-assets/7/pixel.svg"><form method="post" action="./submitted"><input name="value" value="synthetic"><button id="submit">Submit once</button></form></body></html>""";
                    else if (path.EndsWith("/page.js", StringComparison.Ordinal))
                    { type = "text/javascript"; text = "fetch('./probe').then(r => { if(r.ok) document.body.dataset.ready='yes'; });"; }
                    else if (path.EndsWith("/probe", StringComparison.Ordinal)) text = "ready";
                    else if (path.EndsWith("/pixel.svg", StringComparison.Ordinal))
                    { type = "image/svg+xml"; text = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"; }
                    else if (path.EndsWith("/submitted", StringComparison.Ordinal) && start[0] == "POST")
                    { Interlocked.Increment(ref Writes); text = "<html><body data-submitted='yes'>Submitted</body></html>"; }
                    else { status = 404; text = "Outside fixture contract"; }
                    var body = Encoding.UTF8.GetBytes(text);
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n{extra}Connection: close\r\n\r\n");
                    await stream.WriteAsync(response, _stop.Token); await stream.WriteAsync(body, _stop.Token);
                }
                catch (IOException) { } catch (OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_connections.ToArray()); _stop.Dispose(); }
    }
}
