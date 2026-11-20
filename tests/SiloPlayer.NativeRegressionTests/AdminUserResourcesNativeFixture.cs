using System.Net;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>Real family registration, shell and Frame. Synthetic server only. Source-only authored; runtime unobserved.</summary>
internal static class AdminUserResourcesNativeFixture
{
    private static void Check(bool value, string label)
    {
        Program.Log((value ? "PASS: " : "FAIL: ") + label);
        if (!value) throw new InvalidOperationException(label);
    }
    private static async Task Until(Func<bool> condition, string label)
    {
        for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25);
        Check(condition(), label);
    }
    private static IEnumerable<DependencyObject> All(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in All(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        All(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static ContentDialog? Dialog(FrameworkElement root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
        .SelectMany(popup => All(popup.Child)).OfType<ContentDialog>().FirstOrDefault();
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight,
            96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal bool Conflict;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            Requests.Add(r.Method + " " + r.RequestUri.PathAndQuery);
            if (r.Method == HttpMethod.Put && Conflict)
                return Task.FromResult(Json("{\"title\":\"Changed\",\"status\":412}", HttpStatusCode.PreconditionFailed));
            var body = path switch
            {
                "/api/v2/admin/users/capabilities" => "{\"access_groups\":true}",
                "/api/v2/admin/devices/capabilities" => "{\"available\":true}",
                "/api/v2/admin/history-imports/capabilities" => "{\"available\":true,\"guarded_configuration\":true,\"durable_runs\":true,\"max_bulk_mappings\":200,\"played_lookback\":true,\"max_lookback_hours\":8760}",
                "/api/v2/admin/access-groups" => "{\"items\":[" + Group + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/access-groups/2" => Group,
                "/api/v2/admin/request-groups/2/limit" => "{\"group_id\":\"2\",\"limit_mode\":\"inherit\",\"approval_mode\":\"inherit\",\"max_requests\":null,\"window_days\":null}",
                "/api/v2/admin/request-settings" => "{\"requests_enabled\":true,\"global_max_requests\":10,\"global_window_days\":7,\"global_auto_approval_enabled\":false}",
                "/api/v2/admin/settings/effective" => "{\"download.enabled\":\"true\",\"download.transcode_enabled\":\"true\"}",
                "/api/v2/admin/users" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/libraries" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/devices" => "{\"items\":[" + Device + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/devices/7/desktop" => Device,
                "/api/v2/admin/users/7/settings/values" => "{\"revision\":29,\"items\":[{\"key\":\"playback.auto_play_next\",\"scope\":\"profile_device\",\"profile_id\":\"p7\",\"device_id\":\"desktop\",\"value\":false}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/history-import-sources" => "{\"items\":[" + Source + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/history-import-sources/3" => Source,
                "/api/v2/admin/history-imports/mappings" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/history-imports/runs" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/server/status" => "{\"restart_required\":false}",
                "/api/v2/admin/system/build" => "{\"display\":\"synthetic\",\"available\":true}",
                _ => "{}",
            };
            return Task.FromResult(Json(body));
        }
        private const string Group = """{"id":"2","name":"Family","description":"Synthetic household","library_ids":null,"allowed_permissions":null,"download_allowed":true,"download_transcode_allowed":true,"transcode_allowed":true,"audio_transcode_allowed":true,"requests_allowed":true,"max_streams":0,"max_transcodes":0,"max_profiles":5,"max_playback_quality":"original","max_remote_stream_bitrate_kbps":0,"max_local_stream_bitrate_kbps":0,"is_default":true,"member_count":0}""";
        private const string Device = """{"user_id":"7","username":"Fixture","email":"fixture@example.invalid","device_id":"desktop","device_name":"Study","device_platform":"Windows","override_count":1,"profile_count":1,"profiles":[{"profile_id":"p7","profile_name":"Primary","override_count":1}],"connections":[],"settings":[]}""";
        private const string Source = """{"id":"3","name":"Fixture Jellyfin","source_type":"jellyfin","base_url":"https://source.invalid","enabled":true,"has_admin_token":true,"needs_reconfiguration":false}""";
        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var result = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            result.Headers.TryAddWithoutValidation("ETag", "\"fixture-r1\"");
            return result;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "fixture-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        await using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService())
            .AddSingleton(new SettingsService(Program.ResultDirectory)).AddAdminUserResources().BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null); var previousContent = window.Content;
        var frame = new Frame();
        field.SetValue(null, services); window.Content = frame; navigation.Frame = frame;
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.AccessGroups)), "Access Groups enters through real registration");
            await Until(() => All(frame).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "GroupCard_2"), "group card list loaded");
            Click(Find<Button>(frame, "GroupCard_2"));
            await Until(() => All(frame).OfType<TextBox>().Any(box => AutomationProperties.GetAutomationId(box) == "GroupName"), "group card opens its real descendant");
            var group = All(frame).OfType<AdminAccessGroupsPage>().Single();
            await Until(() => All(group).OfType<TextBox>().Any(x => AutomationProperties.GetAutomationId(x) == "GroupName"), "group editor hydrated");
            Check(!Find<Button>(group, "GroupDelete").IsEnabled, "default group cannot be deleted");
            await Until(() => All(group).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "GroupAddMembers"), "member workflow loaded");
            await CaptureAsync(group, "admin-access-groups-wide.png");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(100);
            await CaptureAsync(group, "admin-access-groups-compact.png");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(100);
            var name = Find<TextBox>(group, "GroupName"); name.Text = "Changed";
            server.Conflict = true; Click(Find<Button>(group, "GroupSave"));
            await Until(() => group.Session?.NeedsReview == true, "412 retains draft and requires explicit review");
            Check(name.Text == "Changed", "conflict keeps typed group name");
            Check(!Find<Button>(group, "GroupSave").IsEnabled, "conflict blocks repeated write");
            // Discard explicitly before navigating so no unsaved dialog remains.
            Click(Find<Button>(group, "ResourceReload"));
            await Until(() => group.Session?.Busy == false && !group.RequiresLeaveConfirmation, "reload discards conflicted draft");
            Check(AdminShellPage.Open(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7")), "device descendant enters through registration");
            await Until(() => All(frame).OfType<AdminDevicesPage>().Any(), "devices page loaded");
            var devices = All(frame).OfType<AdminDevicesPage>().Single();
            await Until(() => All(devices).OfType<Button>().Any(x => AutomationProperties.GetAutomationId(x) == "DeviceResetProfile"), "canonical profile overrides rendered");
            Check(server.Requests.Any(x => x.StartsWith("GET /api/v2/admin/users/7/settings/values?", StringComparison.Ordinal)), "device detail reads canonical owner values");
            Check(!server.Requests.Any(x => x.Contains("/device-settings", StringComparison.Ordinal)), "no legacy override route");
            await CaptureAsync(All(frame).OfType<AdminDevicesPage>().Single(), "admin-devices-wide.png");
            var devicePage = All(frame).OfType<AdminDevicesPage>().Single();
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(100);
            await CaptureAsync(devicePage, "admin-devices-compact.png");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(100);
            var beforeReset = server.Requests.Count(x => x.StartsWith("DELETE ", StringComparison.Ordinal));
            Click(Find<Button>(devicePage, "DeviceResetProfile"));
            await Until(() => Dialog(frame) != null, "native profile reset confirmation opened");
            Check(Dialog(frame)!.Title.ToString()!.Contains("Reset", StringComparison.Ordinal), "profile reset confirmation is destructive and named");
            Dialog(frame)!.Hide(); await Until(() => Dialog(frame) == null, "profile reset confirmation fully closed");
            Check(server.Requests.Count(x => x.StartsWith("DELETE ", StringComparison.Ordinal)) == beforeReset, "canceled reset sends no writes");
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.HistoryImport).WithQuery("source", "3")), "History Import enters through registration");
            await Until(() => All(frame).OfType<AdminHistoryImportPage>().Any(), "history page loaded");
            var history = All(frame).OfType<AdminHistoryImportPage>().Single();
            await Until(() => All(history).OfType<Button>().Any(x => AutomationProperties.GetAutomationId(x) == "ImportDiscover" && x.IsEnabled), "configured source exposes discovery workflow");
            await CaptureAsync(history, "admin-history-import-wide.png");
            Click(Find<Button>(history, "ImportEditSource"));
            await Until(() => Dialog(frame) != null, "native history source editor opened");
            var editorDialog = Dialog(frame)!;
            Check(Find<TextBox>(editorDialog, "ImportSourceName").Text == "Fixture Jellyfin", "source editor hydrates exact selected source");
            Check(All(editorDialog).OfType<PasswordBox>().All(x => x.Password.Length == 0), "source credentials are write-only");
            editorDialog.Hide(); await Until(() => Dialog(frame) == null, "source dialog fully closed");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(100);
            await CaptureAsync(history, "admin-history-import-compact.png");
            Check(!services.GetServices<AdminPageRegistration>().Any(x => x.Route == AdminRoute.Users), "family does not register or reimplement pending Users page");
            authority.Dispose();
            await Until(() => history.Session.Owner == null || All(history).OfType<Button>().Where(x => AutomationProperties.GetAutomationId(x) == "ImportDiscover").All(x => !x.IsEnabled), "retirement revokes retained actions");
            frame.Navigate(typeof(Page));
            await Until(() => !history.IsLoaded, "history unload completes before service restoration");
            Program.Log("PASS: Admin user resources native fixture complete (synthetic/current DPI only).");
        }
        finally
        {
            var ownedPages = All(frame).OfType<Page>().Where(page => page is IAdminPageSuspension).ToArray();
            foreach (var page in ownedPages.OfType<IAdminPageSuspension>()) page.SuspendAdminPage();
            frame.Navigate(typeof(Page));
            await Until(() => ownedPages.All(page => !page.IsLoaded) && Dialog(frame) == null, "family pages and dialogs fully departed");
            window.Content = previousContent; field.SetValue(null, previous);
        }
    }
}
