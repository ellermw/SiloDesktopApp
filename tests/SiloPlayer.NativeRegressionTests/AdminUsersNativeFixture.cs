using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

internal static class AdminUsersNativeFixture
{
    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived { add { } remove { } }
        public event Action<string, string, JsonElement>? EventReceived { add { } remove { } }
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) => new Release();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        private sealed class Release : IDisposable { public void Dispose() { } }
    }
    private sealed class Server : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<bool?> PasswordRequirements = new();
        internal readonly ConcurrentQueue<JsonObject> ProfileUpdates = new();
        internal readonly JsonObject Target = JsonNode.Parse("""{"id":"2","username":"Target","email":"target@example.test","role":"user","is_owner":false,"enabled":true,"password_login":true,"password_change_required":false,"break_glass":false,"access_group_id":null,"max_profiles":null,"effective_policy":{"max_profiles":3,"max_streams":0,"max_transcodes":0,"transcode_allowed":true,"permissions":[],"library_ids":null},"created_at":"2026-11-16T00:00:00Z"}""")!.AsObject();
        internal int Version = 1;
        internal int Writes;
        internal bool LoseNextWriteResponse;
        internal int AvatarReads;
        internal bool FailNextFixturePage;
        internal int FixturePageCalls;
        private const string Capabilities = """{"available":true,"allowed":true,"state":"available","guarded_configuration":true,"access_groups":true,"default_profile":true,"profile_limits":true,"profile_management":true,"profile_sections":true,"profile_avatars":true,"account_devices":true,"account_downloads":true,"login_sessions":true,"password_reset_link":true,"password_reset_email":true,"ownership_transfer":true,"exact_identity_filter":true,"policy_defaults":true,"request_usage":true,"watch_summary":true,"admin_identities":true,"break_glass":true,"admin_management":true,"email_delivery":true,"profileless":true,"client_selected_code":true,"manifest_revision":30,"scopes":["account","profile","profile_device","profile_client","profile_library","profile_series"],"client_families":["tv","mobile","tablet","desktop","web"]}""";
        private const string Profiles = """{"items":[{"id":"p2","name":"Primary","is_primary":true,"is_child":false,"has_pin":true,"avatar":null},{"id":"p3","name":"Second","is_primary":false,"is_child":false,"has_pin":false,"avatar":null}]}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(request.Method + " " + request.RequestUri.PathAndQuery);
            if (path.EndsWith("/svg", StringComparison.Ordinal) || path.StartsWith("/profile-avatars/", StringComparison.Ordinal))
            {
                Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie"), "avatar previews never carry desktop account headers");
                AvatarReads++;
                return new(HttpStatusCode.OK) { Content = new StringContent("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"128\" height=\"128\"/></svg>", Encoding.UTF8, "image/svg+xml") };
            }
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                if (request.Method == HttpMethod.Put && path == "/api/v2/admin/users/2")
                {
                    Check(request.Headers.IfMatch.Single().ToString() == "\"account-" + Version + "\"", "account mutation carries its reviewed strong validator");
                    var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                    if (patch.ContainsKey("password"))
                    {
                        PasswordRequirements.Enqueue(patch["require_password_change"]?.GetValue<bool>());
                        Target["password_change_required"] = patch["require_password_change"]?.GetValue<bool>() ?? false;
                    }
                    foreach (var pair in patch)
                        if (pair.Key is not ("password" or "require_password_change")) Target[pair.Key] = pair.Value?.DeepClone();
                    Version++;
                    if (LoseNextWriteResponse)
                    {
                        LoseNextWriteResponse = false;
                        throw new HttpRequestException("Synthetic response lost after applying account update.");
                    }
                    return new(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Patch && path == "/api/v2/admin/users/2/profiles/p3")
                {
                    ProfileUpdates.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                    return new(HttpStatusCode.NoContent);
                }
                throw new InvalidOperationException("Unexpected synthetic U1 mutation route.");
            }
            if (path == "/api/v2/admin/fixture-pages")
            {
                FixturePageCalls++;
                if (FailNextFixturePage) { FailNextFixturePage = false; throw new HttpRequestException("Synthetic continuation failure."); }
                var cursor = request.RequestUri.Query.Split('&').FirstOrDefault(part => part.StartsWith("cursor=", StringComparison.Ordinal));
                var index = cursor == null ? 0 : int.Parse(cursor[7..]);
                return Json(new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = index.ToString() }),
                    ["page"] = new JsonObject { ["has_more"] = true, ["next_cursor"] = (index + 1).ToString() } }.ToJsonString());
            }
            if (path.EndsWith("/capabilities", StringComparison.Ordinal)) return Json(Capabilities);
            if (path == "/api/v2/admin/users/1") return Json("""{"id":"1","username":"Owner","role":"admin","is_owner":true,"enabled":true,"password_login":true}""", "\"owner-1\"");
            if (path == "/api/v2/admin/users/2") return Json(Target.ToJsonString(), "\"account-" + Version + "\"");
            if (path == "/api/v2/admin/users") return Json(new JsonObject { ["items"] = new JsonArray(Target.DeepClone()), ["page"] = new JsonObject { ["has_more"] = false } }.ToJsonString());
            if (path == "/api/v2/profiles") return Json("""{"items":[],"max_advisory_age_supported":true,"require_advisory_age_supported":true,"requests_allowed_supported":true}""");
            if (path.EndsWith("/profiles", StringComparison.Ordinal)) return Json(Profiles);
            if (path.EndsWith("/profiles/p2", StringComparison.Ordinal)) return Json("""{"id":"p2","name":"Primary","is_primary":true,"is_child":false,"has_pin":true,"avatar":null,"allowed_library_ids":[],"library_restrictions_enabled":false,"requests_allowed":true,"max_advisory_age":null,"require_advisory_age":false}""");
            if (path.EndsWith("/profiles/p3", StringComparison.Ordinal)) return Json("""{"id":"p3","name":"Second","is_primary":false,"is_child":false,"has_pin":false,"avatar":null}""");
            if (path.EndsWith("/sections/settings", StringComparison.Ordinal)) return Json("""{"items":[{"id":"home1","title":"Continue watching","position":0,"hidden":false}]}""");
            if (path.EndsWith("/settings/values", StringComparison.Ordinal)) return Json("""{"items":[{"key":"synthetic.setting","scope":"profile","profile_id":"p2","value":0,"revision":"r1"}],"page":{"has_more":false}}""");
            if (path == "/api/v2/settings/contract") return Json("""{"definitions":[{"key":"synthetic.setting","label":"Synthetic setting","category":"display","introduced_in":1,"persistence":"remote","allowed_scopes":["account","profile","profile_device","profile_client","profile_library","profile_series"],"value_schema":{"type":"integer","nullable":true,"minimum":0},"default_value":7,"resolution_order":["profile_device","profile","account","default"]}]}""");
            if (path.EndsWith("/identities", StringComparison.Ordinal)) return Json("""{"items":[{"id":"external1","provider_name":"Synthetic provider","external_subject":"Issuer|Exact"}]}""");
            if (path.EndsWith("/plugins/installations", StringComparison.Ordinal)) return Json("""{"items":[{"id":"provider1","plugin_id":"synthetic","metadata":{"name":"Synthetic provider"},"capabilities":[{"id":"auth1","type":"auth_provider.v1"}],"auth_bindings":[{"capability_id":"auth1"}]}],"page":{"has_more":false}}""");
            if (path.EndsWith("/login-sessions", StringComparison.Ordinal)) return Json("""{"items":[{"id":"session1","device_name":"Synthetic desktop","current":false}],"page":{"has_more":false}}""");
            if (path.EndsWith("/limit", StringComparison.Ordinal)) return Json("""{"user_id":"2","limit_mode":"inherit","max_requests":null,"window_days":null,"approval_mode":"inherit"}""", "\"quota-1\"");
            if (path.EndsWith("/request-settings", StringComparison.Ordinal)) return Json("""{"requests_enabled":true,"global_max_requests":10,"global_window_days":7,"global_auto_approval_enabled":false}""");
            if (path.EndsWith("/policy-defaults", StringComparison.Ordinal)) return Json("""{"admin":{"max_profiles":3},"ungrouped":{"max_profiles":3}}""");
            if (path.EndsWith("/devices", StringComparison.Ordinal)) return Json("""{"items":[{"device_id":"device2","device_name":"Synthetic phone","device_platform":"android","profiles":[{"profile_id":"p2"}]}]}""");
            if (path.EndsWith("/sections", StringComparison.Ordinal) || path == "/api/v2/libraries") return Json("""{"items":[]}""");
            if (path.EndsWith("/summary", StringComparison.Ordinal) || path.EndsWith("/watch-summary", StringComparison.Ordinal) || path.EndsWith("/usage", StringComparison.Ordinal)) return Json("{}");
            return Json("""{"items":[],"page":{"has_more":false}}""");
        }
        private static HttpResponseMessage Json(string content, string? tag = null)
        {
            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
            if (tag != null) reply.Headers.ETag = new EntityTagHeaderValue(tag);
            return reply;
        }
    }
    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Owner", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var monitor = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(monitor, authority, new Realtime());
        var navigation = new NavigationService();
        var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitor).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory)).AddAdminUsers()
            .AddSingleton(new AdminUserAvatarImages(http));
        await using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<AdminPageRegistration>().Single();
        Check(registration.Route == AdminRoute.Users && registration.PageType == typeof(AdminUsersPage), "production Users registration exposes only its own leaf");
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousServices = servicesField.GetValue(null); var previousContent = window.Content;
        servicesField.SetValue(null, provider);
        try
        {
            var outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(((Microsoft.UI.Xaml.Markup.IXamlMetadataProvider)Application.Current).GetXamlType(typeof(AdminUsersPage)) != null, "Users has generated WinUI navigation activation metadata");
            Check(AdminShellPage.CanOpen(new(AdminRoute.Users)), "U1 registration opens the real Users leaf");
            Check(AdminShellPage.Open(new(AdminRoute.Users)), "Users enters the real Admin shell");
            await SettledAsync(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)Field(shell, "_frame")!;
            await SettledAsync(() => inner.Content is AdminUsersPage page && Ready(page));
            var users = (AdminUsersPage)inner.Content;
            foreach (var tab in new[] { ("Invitations", "invitations"), ("Invite Codes", "invite-codes"), ("Users", "users") })
            {
                await InvokeAsync(FindButton(users, tab.Item1));
                await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == tab.Item2);
                Check(ReferenceEquals(inner.Content, users), tab.Item1 + " replaces route state without remounting");
                if (tab.Item2 != "users")
                {
                    await InvokeAsync(FindButton(users, tab.Item2 == "invitations" ? "Create invitation" : "Create invite code"));
                    await CloseDialogAsync(users, tab.Item1 + " creation");
                }
            }
            await InvokeAsync(FindButton(users, "Create user")); await CloseDialogAsync(users, "Create user");
            await InvokeAsync(FindButton(users, "Open Target"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Segment(0) == "2");
            foreach (var tab in new[] { "profiles", "access", "sign-in", "activity", "downloads", "preferences", "overview" })
            {
                await InvokeAsync(FindButton(users, char.ToUpperInvariant(tab[0]) + tab[1..]));
                await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == tab);
                Check(!((Dictionary<string, string>)Field(users, "_loadErrors")!).Any(), tab + " has no synthetic read failures");
            }
            foreach (var action in new[] { "View as", "Disable account", "Delete account", "Password reset link", "Email password reset", "Set password" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            server.Target["role"] = "admin"; server.Version++;
            await InvokeAsync(FindButton(users, "Reload / review current server state")); await SettledAsync(() => Ready(users));
            await InvokeAsync(FindButton(users, "Transfer ownership")); await CloseDialogAsync(users, "Transfer ownership");
            await InvokeAsync(FindButton(users, "Profiles")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "profiles");
            foreach (var action in new[] { "Add profile", "Edit profile", "Change PIN", "Remove PIN", "Delete profile" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            await InvokeAsync(FindButton(users, "Add profile"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var avatarDialog = (ContentDialog)Field(users, "_dialog")!;
            await InvokeAsync(FindButton(avatarDialog, "Load graphical previews"));
            await SettledAsync(() => Descendants(avatarDialog).OfType<TextBlock>().Any(text => text.Text.StartsWith("Graphical previews loaded.", StringComparison.Ordinal)));
            var avatarGrid = Descendants(avatarDialog).OfType<GridView>().Single();
            avatarGrid.SelectedItem = avatarGrid.Items[1];
            Check(Descendants(avatarDialog).OfType<Image>().Any(image => image.Width == 96 && image.Source != null), "avatar selection has a live graphical preview");
            Check(server.AvatarReads == 26, "one visible avatar batch loads only eighteen current-style and eight legacy presets");
            await CloseDialogAsync(users, "Avatar preview");
            await InvokeAsync(FindButton(users, "Sign-in")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "sign-in");
            foreach (var action in new[] { "Configure break-glass", "Link external identity", "Unlink identity", "Sign out all login sessions", "Sign out this login session" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            await InvokeAsync(FindButton(users, "Preferences")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "preferences");
            foreach (var action in new[] { "View JSON · synthetic.setting", "Edit · synthetic.setting", "Reset only this identity", "Hide", "Reset this profile Home layout" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            await InvokeAsync(FindButton(users, "Access")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "access");
            await InvokeAsync(FindButton(users, "Edit account"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var editor = (ContentDialog)Field(users, "_dialog")!;
            var name = Descendants((DependencyObject)editor.Content).OfType<TextBox>().Single(box => box.Text == "Target"); name.Text = "Target Renamed";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null && server.Target.Text("username") == "Target Renamed");
            Check(server.Writes == 1, "one explicit guarded Apply produces exactly one synthetic mutation");
            server.LoseNextWriteResponse = true;
            await InvokeAsync(FindButton(users, "Edit account"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            editor = (ContentDialog)Field(users, "_dialog")!;
            name = Descendants((DependencyObject)editor.Content).OfType<TextBox>().Single(box => box.Text == "Target Renamed");
            name.Text = "Applied Without Response";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            var workspace = (AdminUsersWorkspace)Field(users, "_workspace")!;
            await SettledAsync(() => workspace.OutcomeUnknown && editor.IsSecondaryButtonEnabled);
            Check(server.Writes == 2 && !editor.IsPrimaryButtonEnabled, "unknown account outcome blocks the retained Apply");
            await InvokeAsync(DialogButton(window, "SecondaryButton"));
            await SettledAsync(() => editor.IsSecondaryButtonEnabled);
            Check(!editor.IsPrimaryButtonEnabled && workspace.NeedsReview && server.Writes == 2, "fresh readback never unlocks or replays an unknown write");
            await CloseDialogAsync(users, "Unknown outcome reconciliation");
            await InvokeAsync(FindButton(users, "Reload / review current server state"));
            await SettledAsync(() => Ready(users));
            Check(server.Target.Text("username") == "Applied Without Response" && server.Writes == 2, "explicit reload reconciles applied state without another write");
            foreach (var requirePasswordChange in new[] { true, false })
            {
                var writesBeforePassword = server.Writes;
                await InvokeAsync(FindButton(users, "Set password"));
                await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                var passwordDialog = (ContentDialog)Field(users, "_dialog")!;
                var password = Descendants((DependencyObject)passwordDialog.Content).OfType<PasswordBox>().Single();
                var requirement = Descendants((DependencyObject)passwordDialog.Content).OfType<ComboBox>().Single();
                Check((requirement.SelectedItem as ComboBoxItem)?.Tag is JsonValue initialRequirement && initialRequirement.GetValue<bool>(), "Set password initially selects temporary password");
                if (!requirePasswordChange)
                    requirement.SelectedItem = requirement.Items.OfType<ComboBoxItem>().Single(item => item.Tag is JsonValue value && !value.GetValue<bool>());
                foreach (var invalidPassword in new[] { "", "short" })
                {
                    password.Password = invalidPassword;
                    await InvokeAsync(DialogButton(window, "PrimaryButton"));
                    await SettledAsync(() => passwordDialog.IsPrimaryButtonEnabled && passwordDialog.IsSecondaryButtonEnabled &&
                        Descendants((DependencyObject)passwordDialog.Content).OfType<TextBlock>().Any(text => text.Text == "password: invalid length."));
                    Check(ReferenceEquals(Field(users, "_dialog"), passwordDialog) && server.Writes == writesBeforePassword && server.PasswordRequirements.IsEmpty,
                        "blank or short password keeps the editor open with a validation error and no mutation, temporary=" + requirePasswordChange);
                }
                password.Password = "Synthetic-Fixture-Password-1!";
                await InvokeAsync(DialogButton(window, "PrimaryButton"));
                await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
                Check(server.PasswordRequirements.TryDequeue(out var sentRequirement) && sentRequirement == requirePasswordChange,
                    requirePasswordChange ? "unchanged temporary-password selection is submitted explicitly" : "explicit permanent-password selection is submitted as false");
                Check(server.Writes == writesBeforePassword + 1 && server.Target.Flag("password_change_required") == requirePasswordChange,
                    "one guarded password submission preserves the selected server requirement");
            }
            await InvokeAsync(FindButton(users, "Profiles"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "profiles");
            await InvokeAsync(Descendants(users).OfType<Button>().Last(button => button.Content as string == "Edit profile"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var sparseProfileDialog = (ContentDialog)Field(users, "_dialog")!;
            Descendants((DependencyObject)sparseProfileDialog.Content).OfType<TextBox>().Single(box => box.Text == "Second").Text = "Second Renamed";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
            Check(server.ProfileUpdates.TryDequeue(out var profilePatch) && profilePatch.Count == 1 && profilePatch.Text("name") == "Second Renamed",
                "renaming sparse p3 submits only name, preserving absent library and playback policy fields");
            await InvokeAsync(FindButton(users, "Access"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "access");
            await ContinuationRetryChecksAsync(users, server);
            await PreviewChecksAsync(users);
            var detachedAction = FindButton(users, "Set password");
            var detachedWorkspace = (AdminUsersWorkspace)Field(users, "_workspace")!;
            var detachedGeneration = detachedWorkspace.Generation;
            outer.Navigate(typeof(Page));
            await SettledAsync(() => !users.IsLoaded);
            Check(!detachedWorkspace.IsCurrent(detachedGeneration), "leaving the Admin shell retires the Users lifetime");
            var detachedRequests = server.Requests.Count;
            await InvokeAsync(detachedAction, false);
            Check(server.Requests.Count == detachedRequests, "unloaded controls cannot issue reads or writes");
            Check(AdminShellPage.Open(new(AdminRoute.Users) { Segments = ["2"] }), "Users can reopen through normal shell navigation after unloading");
            await SettledAsync(() => outer.Content is AdminShellPage);
            shell = (AdminShellPage)outer.Content;
            inner = (Frame)Field(shell, "_frame")!;
            await SettledAsync(() => inner.Content is AdminUsersPage current && Ready(current));
            users = (AdminUsersPage)inner.Content;
            Check(!ReferenceEquals(workspace, Field(users, "_workspace")), "reopened Users owns a fresh authority workspace");
            var retained = FindButton(users, "Set password");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Owner", Role = "user" });
            await SettledAsync(() => Field(users, "_review") == null);
            Check(!Descendants(users).OfType<TextBlock>().Any(text => text.Text.Contains("Applied Without Response", StringComparison.Ordinal)), "authority retirement removes account data");
            var count = server.Requests.Count;
            if (retained != null) await InvokeAsync(retained, false);
            Check(server.Requests.Count == count, "retired rendered action cannot start another request");
        }
        finally
        {
            try { await RestoreContentAsync(window, previousContent); }
            finally { servicesField.SetValue(null, previousServices); }
        }
    }
    private static Task CallAsync(AdminUsersPage page, string method, params object?[] args) =>
        (Task)typeof(AdminUsersPage).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, args)!;
    private static async Task ContinuationRetryChecksAsync(AdminUsersPage page, Server server)
    {
        var generation = (long)Field(page, "_generation")!;
        await CallAsync(page, "RowsAsync", "retry-pages", "/api/v2/admin/fixture-pages", generation, false, 100);
        for (var index = 0; index < 8; index++) await CallAsync(page, "MoreAsync", "retry-pages");
        var rows = (System.Collections.IDictionary)Field(page, "_rows")!;
        var state = rows["retry-pages"]!;
        var seen = (HashSet<string>)state.GetType().GetProperty("Seen")!.GetValue(state)!;
        Check(seen.Count == 8, "eight accepted continuations consume eight page slots");
        server.FailNextFixturePage = true;
        await CallAsync(page, "MoreAsync", "retry-pages");
        Check(seen.Count == 8 && !seen.Contains("9"), "failed ninth continuation preserves cursor and page budget");
        await CallAsync(page, "MoreAsync", "retry-pages");
        Check(seen.Count == 9 && seen.Contains("9") && server.FixturePageCalls == 11, "retry sends and adopts the same ninth continuation exactly once");
        await CallAsync(page, "MoreAsync", "retry-pages");
        Check(server.FixturePageCalls == 11, "accepted-page bound still blocks a tenth continuation");
    }
    private sealed class PreviewServer : HttpMessageHandler
    {
        internal readonly List<string> Requests = new();
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>> Read = _ => Task.FromResult(AdminMonitorReply("{}"));
        internal Func<Task<HttpResponseMessage>> Logout = () => Task.FromResult(AdminMonitorReply("{}"));
        internal CancellationToken ReadToken;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Post) return Logout();
            ReadToken = token;
            return Read(request);
        }
    }
    private static HttpResponseMessage AdminMonitorReply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static AdminUserViewAs AdoptPreview(AdminUsersPage page, PreviewServer server)
    {
        var workspace = (AdminUsersWorkspace)Field(page, "_workspace")!;
        var generation = workspace.Generation;
        var preview = new AdminUserViewAs("https://fixture.invalid/silo", "2",
            JsonNode.Parse("""{"access_token":"synthetic-target","user":{"id":"2"}}""")!.AsObject(), workspace.Token, () => workspace.IsCurrent(generation), server);
        typeof(AdminUsersPage).GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, preview);
        typeof(AdminUsersPage).GetField("_previewName", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "Target");
        return preview;
    }
    private static async Task StartActionAsync(AdminUsersPage page, string label, Func<Task> run)
    {
        var button = (Button)typeof(AdminUsersPage).GetMethod("Action", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(page, [label, run, true, false, false])!;
        ((StackPanel)Field(page, "_body")!).Children.Add(button);
        await SettledAsync(() => button.IsLoaded);
        await InvokeAsync(button);
    }
    private static async Task PreviewInterruptionChecksAsync(AdminUsersPage page)
    {
        foreach (var kind in new[] { "profiles", "catalog", "detail" })
        {
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logout = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new PreviewServer { Read = _ => pending.Task, Logout = () => logout.Task };
            using var preview = AdoptPreview(page, server);
            var path = kind == "profiles" ? "/api/v2/profiles" : kind == "catalog" ? "/api/v2/catalog" : "/api/v2/catalog/items/3";
            try
            {
                await StartActionAsync(page, "Start held scoped read", async () =>
                {
                    try { await CallAsync(page, "PreviewReadAsync", path, kind, null, null); }
                    finally { completed.TrySetResult(); }
                });
                Check((bool)Field(page, "_actionBusy")! && !completed.Task.IsCompleted, kind + " read holds the production action gate");
                var retainedReturn = FindButton(page, "Return to admin");
                await InvokeAsync(retainedReturn);
                await SettledAsync(() => Field(page, "_preview") == null && server.Requests.Contains("POST /silo/api/v2/auth/logout"));
                Check(server.ReadToken.IsCancellationRequested && !pending.Task.IsCompleted,
                    kind + " return cancels the pending read and starts independent logout without awaiting it");
                if (kind == "profiles")
                {
                    pending.SetException(new TaskCanceledException("Late scoped cancellation."));
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check((bool)Field(page, "_actionBusy")!, "interrupted action cannot clear busy state during logout");
                }
                logout.SetResult(AdminMonitorReply("{}"));
                await SettledAsync(() => Ready(page) && !(bool)Field(page, "_actionBusy")!);
                if (kind != "profiles")
                {
                    var adminWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        await StartActionAsync(page, "Hold restored admin action", () => adminWork.Task);
                        var notice = ((TextBlock)Field(page, "_notice")!).Text;
                        pending.SetException(new HttpRequestException("Late scoped failure."));
                        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Check((bool)Field(page, "_actionBusy")! && ((TextBlock)Field(page, "_notice")!).Text == notice && FindButton(page, "Return to admin") == null,
                            kind + " late completion cannot alter the restored admin page or release its new action gate");
                    }
                    finally { adminWork.TrySetResult(); }
                    await SettledAsync(() => !(bool)Field(page, "_actionBusy")!);
                }
                await InvokeAsync(retainedReturn, false);
                Check(server.Requests.Count(request => request == "POST /silo/api/v2/auth/logout") == 1,
                    kind + " return revokes once and detached return cannot replay");
            }
            finally
            {
                pending.TrySetResult(AdminMonitorReply("{}"));
                logout.TrySetResult(AdminMonitorReply("{}"));
            }
        }
    }
    private static async Task PreviewChecksAsync(AdminUsersPage page)
    {
        await PreviewInterruptionChecksAsync(page);
        foreach (var timeout in new[] { false, true })
        {
            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new PreviewServer { Read = _ => pending.Task };
            using var preview = AdoptPreview(page, server);
            var read = CallAsync(page, "PreviewReadAsync", "/api/v2/profiles", "profiles", null, null);
            Check(FindButton(page, "Return to admin") != null && Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("VIEWING AS Target", StringComparison.Ordinal)),
                "temporary-session banner and return exist before initial read completes");
            pending.SetException(timeout ? new TaskCanceledException("Synthetic timeout.") : new HttpRequestException("Synthetic initial read failure."));
            await read;
            Check(FindButton(page, "Return to admin") != null && FindButton(page, "Retry scoped read") != null && ((TextBlock)Field(page, "_notice")!).Text.Length > 0,
                "initial preview failure or timeout retains explicit error, retry and return controls");
            server.Read = _ => Task.FromResult(AdminMonitorReply("""{"items":[],"page":{"has_more":false}}"""));
            await InvokeAsync(FindButton(page, "Retry scoped read"));
            Check(FindButton(page, "Return to admin") != null && FindButton(page, "Retry scoped read") == null, "initial preview can be retried without losing return");
            await InvokeAsync(FindButton(page, "Return to admin"));
            await SettledAsync(() => Ready(page) && Field(page, "_preview") == null && !(bool)Field(page, "_actionBusy")!);
            Check(server.Requests.Count(request => request == "POST /silo/api/v2/auth/logout") == 1, "return after failed initial preview revokes the temporary sub-path session once");
        }
        var failNext = false; var repeat = false;
        var pages = new PreviewServer();
        pages.Read = request =>
        {
            if (failNext) { failNext = false; throw new HttpRequestException("Synthetic scoped continuation failure."); }
            var query = request.RequestUri!.Query.TrimStart('?').Split('&');
            Check(query.Count(part => part.StartsWith("limit=", StringComparison.Ordinal)) == 1 && query.Count(part => part.StartsWith("cursor=", StringComparison.Ordinal)) <= 1,
                "scoped pages retain one limit and at most one cursor");
            var cursor = query.FirstOrDefault(part => part.StartsWith("cursor=", StringComparison.Ordinal));
            var index = cursor == null ? 0 : int.Parse(cursor[7..]);
            var next = repeat && index == 2 ? 1 : index + 1;
            return Task.FromResult(AdminMonitorReply(new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = index.ToString() }),
                ["page"] = new JsonObject { ["has_more"] = true, ["next_cursor"] = next.ToString() } }.ToJsonString()));
        };
        using var pagedPreview = AdoptPreview(page, pages);
        await CallAsync(page, "PreviewReadAsync", "/api/v2/catalog?q=fixture", "catalog", null, null);
        for (var index = 0; index < 8; index++) await InvokeAsync(FindButton(page, "Next scoped page (replaces retained rows)"));
        failNext = true;
        await InvokeAsync(FindButton(page, "Next scoped page (replaces retained rows)"));
        await InvokeAsync(FindButton(page, "Retry scoped read"));
        Check(FindButton(page, "Next scoped page (replaces retained rows)") is { IsEnabled: false } && pages.Requests.Count == 11,
            "scoped retry preserves the ninth slot and accepted continuations stop at nine");
        repeat = true;
        await CallAsync(page, "PreviewReadAsync", "/api/v2/catalog?q=fixture", "catalog", null, null);
        await InvokeAsync(FindButton(page, "Next scoped page (replaces retained rows)"));
        await InvokeAsync(FindButton(page, "Next scoped page (replaces retained rows)"));
        Check(((TextBlock)Field(page, "_notice")!).Text.Contains("Repeated scoped continuation", StringComparison.Ordinal), "one collection walk rejects a cursor repeated across different pages");
        await InvokeAsync(FindButton(page, "Return to admin"));
        await SettledAsync(() => Ready(page) && Field(page, "_preview") == null && !(bool)Field(page, "_actionBusy")!);
    }
    private static async Task RestoreContentAsync(Window window, UIElement previousContent)
    {
        if (ReferenceEquals(window.Content, previousContent)) return;
        if (window.Content is not DependencyObject root) { window.Content = previousContent; return; }
        foreach (var page in Descendants(root).OfType<AdminUsersPage>())
        {
            if (Field(page, "_dialog") is not ContentDialog { IsLoaded: true } dialog) continue;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => closed.TrySetResult();
            dialog.Closed += OnClosed;
            try { dialog.Hide(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { dialog.Closed -= OnClosed; }
        }
        var barriers = new Dictionary<FrameworkElement, (TaskCompletionSource Completion, RoutedEventHandler Handler)>();
        foreach (var element in Descendants(root).OfType<FrameworkElement>().Where(element => element.IsLoaded && element is Page or Frame))
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler handler = (_, _) => completion.TrySetResult();
            barriers.Add(element, (completion, handler));
            element.Unloaded += handler;
        }
        try
        {
            window.Content = previousContent;
            await Task.WhenAll(barriers.Values.Select(barrier => barrier.Completion.Task)).WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var element in barriers.Keys) Check(true, "fixture teardown observed actual " + element.GetType().Name + ".Unloaded");
        }
        finally { foreach (var barrier in barriers) barrier.Key.Unloaded -= barrier.Value.Handler; }
    }
    private static bool Ready(AdminUsersPage page) => page.IsLoaded && !(bool)Field(page, "_loading")! && Field(page, "_review") != null;
    private static async Task CloseDialogAsync(AdminUsersPage page, string label)
    {
        await SettledAsync(() => Field(page, "_dialog") is ContentDialog);
        var dialog = (ContentDialog)Field(page, "_dialog")!;
        Check(dialog.XamlRoot == page.XamlRoot && dialog.Content != null, label + " opens a real route-bound dialog");
        dialog.Hide();
        await SettledAsync(() => Field(page, "_dialog") == null && !(bool)Field(page, "_actionBusy")!);
    }
    private static void Check(bool condition, string message)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + message);
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task SettledAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++) { if (condition()) return; await Task.Delay(25); }
        throw new TimeoutException("The synthetic U1 state did not settle within five seconds.");
    }
    private static async Task InvokeAsync(Button? button, bool requireVisible = true)
    {
        if (button == null) throw new InvalidOperationException("Expected U1 button was not rendered.");
        if (requireVisible) { button.StartBringIntoView(); await Task.Delay(50); Check(button.IsLoaded && button.IsEnabled, "requested U1 action is loaded and enabled"); }
        if (FrameworkElementAutomationPeer.CreatePeerForElement(button) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider peer)
            throw new InvalidOperationException("The U1 button has no invocation provider.");
        peer.Invoke(); await Task.Delay(50);
    }
    private static Button? FindButton(DependencyObject root, string label) => Descendants(root).OfType<Button>().FirstOrDefault(button => button.Content as string == label);
    private static Button? DialogButton(Window window, string name) => window.Content?.XamlRoot is { } root
        ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(popup => popup.Child != null).SelectMany(popup => Descendants(popup.Child!)).OfType<Button>().FirstOrDefault(button => button.Name == name) : null;
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
