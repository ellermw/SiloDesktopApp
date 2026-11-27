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
        // ── Synthetic data for the layout screenshots (Rich) ───────────────────────────────────────────────────
        internal bool Rich;
        internal bool EmptyUsers;
        internal bool FailUsers;
        private static string Ago(TimeSpan span) => DateTimeOffset.UtcNow.Subtract(span).ToString("O");
        private static HttpResponseMessage Items(string items) => Json("{\"items\":[" + items + "],\"page\":{\"has_more\":false}}");
        private JsonObject RichTarget()
        {
            var target = Target.DeepClone().AsObject();
            target["access_group_id"] = "g1"; target["max_streams"] = 3; target["max_profiles"] = 5;
            target["created_at"] = "2026-03-14T09:30:00Z"; target["updated_at"] = Ago(TimeSpan.FromDays(9)); target["last_active_at"] = Ago(TimeSpan.FromHours(2));
            target["effective_policy"] = JsonNode.Parse("""{"max_profiles":5,"max_streams":3,"max_transcodes":1,"transcode_allowed":true,"audio_transcode_allowed":true,"max_playback_quality":"standard","max_remote_stream_bitrate_kbps":20000,"max_local_stream_bitrate_kbps":0,"download_allowed":true,"download_transcode_allowed":false,"requests_allowed":true,"permissions":["marker_edit"],"library_ids":null}""");
            return target;
        }
        private HttpResponseMessage? RichReply(HttpRequestMessage request, string path)
        {
            if (path == "/api/v2/admin/users")
            {
                if (FailUsers) throw new HttpRequestException("Synthetic users read failure.");
                if (EmptyUsers) return Items("");
                string User(string id, string name, string email, string role, bool owner, bool enabled, string? group, string created, string? active) =>
                    "{\"id\":\"" + id + "\",\"username\":\"" + name + "\",\"email\":\"" + email + "\",\"role\":\"" + role + "\",\"is_owner\":" + (owner ? "true" : "false") + ",\"enabled\":" + (enabled ? "true" : "false") +
                    ",\"password_login\":true,\"access_group_id\":" + (group == null ? "null" : "\"" + group + "\"") + ",\"created_at\":\"" + created + "\",\"last_active_at\":" + (active == null ? "null" : "\"" + active + "\"") + "}";
                return Items(string.Join(",",
                [
                    User("1", "Owner", "owner@example.test", "admin", true, true, null, "2026-01-08T10:00:00Z", Ago(TimeSpan.FromHours(1))),
                    RichTarget().ToJsonString(),
                    User("3", "Alice Johnson", "alice@example.test", "user", false, true, "g2", "2026-03-02T15:12:00Z", Ago(TimeSpan.FromDays(3))),
                    User("4", "Bob", "bob@example.test", "user", false, false, null, "2026-04-20T08:00:00Z", null),
                    User("5", "Carol Admin", "carol@example.test", "admin", false, true, null, "2026-02-11T19:45:00Z", Ago(TimeSpan.FromMinutes(20))),
                    User("6", "Dave", "dave@example.test", "user", false, true, "g1", "2026-09-05T12:00:00Z", null),
                    User("7", "Eleanor Vandenberghe-Wolfeschlegelstein", "eleanor.vandenberghe.wolfeschlegelstein@a-rather-long-example-domain.test", "user", false, true, "g2", "2026-05-30T07:30:00Z", Ago(TimeSpan.FromDays(45))),
                ]));
            }
            if (path == "/api/v2/admin/users/2") return Json(RichTarget().ToJsonString(), "\"account-" + Version + "\"");
            if (path == "/api/v2/admin/access-groups") return Items("{\"id\":\"g1\",\"name\":\"Family\"},{\"id\":\"g2\",\"name\":\"Guests\"}");
            if (path == "/api/v2/admin/invitations")
                return Items("{\"id\":\"inv1\",\"email\":\"newperson@example.test\",\"delivery\":\"email_sent\",\"role\":\"user\",\"status\":\"pending\",\"expires_at\":\"" + Ago(TimeSpan.FromDays(-5)) + "\",\"invited_by_name\":\"Owner\",\"created_at\":\"" + Ago(TimeSpan.FromDays(2)) + "\"}," +
                    "{\"id\":\"inv2\",\"email\":\"\",\"delivery\":\"link\",\"role\":\"user\",\"status\":\"pending\",\"note\":\"For the neighbours\",\"expires_at\":\"" + Ago(TimeSpan.FromDays(-3)) + "\",\"invited_by_name\":\"Owner\",\"created_at\":\"" + Ago(TimeSpan.FromDays(4)) + "\"}," +
                    "{\"id\":\"inv3\",\"email\":\"joined@example.test\",\"delivery\":\"email_unconfirmed\",\"role\":\"admin\",\"status\":\"accepted\",\"invited_by_name\":\"Owner\",\"created_at\":\"" + Ago(TimeSpan.FromDays(30)) + "\"}," +
                    "{\"id\":\"inv4\",\"email\":\"old@example.test\",\"delivery\":\"email_sent\",\"role\":\"user\",\"status\":\"expired\",\"created_at\":\"" + Ago(TimeSpan.FromDays(60)) + "\"}");
            if (path == "/api/v2/admin/invite-codes")
                return Items("{\"id\":\"c1\",\"code\":\"BETA2026\",\"label\":\"Beta testers\",\"enabled\":true,\"use_count\":3,\"max_uses\":10,\"created_at\":\"" + Ago(TimeSpan.FromDays(12)) + "\"}," +
                    "{\"id\":\"c2\",\"code\":\"FRIENDS\",\"label\":\"\",\"enabled\":false,\"use_count\":5,\"max_uses\":5,\"created_at\":\"" + Ago(TimeSpan.FromDays(40)) + "\"}");
            if (!path.StartsWith("/api/v2/admin/users/2/", StringComparison.Ordinal) && path != "/api/v2/admin/sessions" && path != "/api/v2/admin/playback-history" && !path.EndsWith("/usage", StringComparison.Ordinal)
                && path != "/api/v2/settings/contract" && !path.EndsWith("/plugins/installations", StringComparison.Ordinal)) return null;
            if (path.EndsWith("/profiles", StringComparison.Ordinal))
                return Items("{\"id\":\"p2\",\"name\":\"Primary\",\"is_primary\":true,\"is_child\":false,\"has_pin\":true,\"avatar\":null,\"last_seen_at\":\"" + Ago(TimeSpan.FromHours(2)) + "\"}," +
                    "{\"id\":\"p3\",\"name\":\"Kids\",\"is_primary\":false,\"is_child\":true,\"has_pin\":false,\"avatar\":null,\"last_seen_at\":\"" + Ago(TimeSpan.FromDays(3)) + "\"}," +
                    "{\"id\":\"p4\",\"name\":\"Guest room TV\",\"is_primary\":false,\"is_child\":false,\"has_pin\":true,\"avatar\":null}");
            if (path == "/api/v2/admin/sessions")
                return Items("{\"session_id\":\"s1\",\"profile_id\":\"p2\",\"profile_name\":\"Primary\",\"media_title\":\"Severance\",\"series_title\":\"Severance\",\"season_number\":2,\"episode_number\":4,\"client_label\":\"Silo for Windows\",\"client_ip\":\"192.168.1.20\",\"position_seconds\":1500,\"file_duration\":3300,\"play_method\":\"direct\",\"effective_play_method\":\"direct\"}");
            if (path == "/api/v2/admin/playback-history")
                return Items("{\"session_id\":\"h1\",\"media_item_id\":\"m1\",\"media_title\":\"The Strike\",\"series_title\":\"Severance\",\"season_number\":2,\"episode_number\":3,\"profile_id\":\"p2\",\"profile_name\":\"Primary\",\"play_method\":\"direct\",\"ended_at\":\"" + Ago(TimeSpan.FromHours(20)) + "\",\"end_position_seconds\":3200,\"duration_seconds\":3300,\"completed\":true}," +
                    "{\"session_id\":\"h2\",\"media_item_id\":\"m2\",\"media_title\":\"Dune: Part Two\",\"profile_id\":\"p3\",\"profile_name\":\"Kids\",\"play_method\":\"transcode\",\"ended_at\":\"" + Ago(TimeSpan.FromDays(2)) + "\",\"end_position_seconds\":1800,\"duration_seconds\":9960,\"completed\":false}," +
                    "{\"session_id\":\"h3\",\"media_item_id\":\"m3\",\"media_title\":\"Pilot\",\"series_title\":\"Andor\",\"season_number\":1,\"episode_number\":1,\"profile_id\":\"p2\",\"profile_name\":\"Primary\",\"play_method\":\"direct_stream\",\"ended_at\":\"" + Ago(TimeSpan.FromDays(5)) + "\",\"end_position_seconds\":2700,\"duration_seconds\":2800,\"completed\":true}," +
                    "{\"session_id\":\"h4\",\"media_item_id\":\"m4\",\"media_title\":\"Blade Runner 2049\",\"profile_id\":\"p2\",\"profile_name\":\"Primary\",\"play_method\":\"remux\",\"ended_at\":\"" + Ago(TimeSpan.FromDays(9)) + "\",\"end_position_seconds\":9000,\"duration_seconds\":9840,\"completed\":false}");
            if (path.EndsWith("/usage", StringComparison.Ordinal)) return Json("{\"requests_enabled\":true,\"allowed\":true,\"unlimited\":false,\"used\":3,\"max_requests\":10,\"window_days\":7}");
            if (path.EndsWith("/watch-summary", StringComparison.Ordinal)) return Json("{\"watched_seconds\":66000,\"plays\":42}");
            if (path.EndsWith("/ips", StringComparison.Ordinal))
                return Items("{\"client_ip\":\"192.168.1.20\",\"first_seen\":\"" + Ago(TimeSpan.FromDays(80)) + "\",\"last_seen\":\"" + Ago(TimeSpan.FromHours(2)) + "\",\"request_count\":18400,\"location\":\"local\"}," +
                    "{\"client_ip\":\"203.0.113.77\",\"first_seen\":\"" + Ago(TimeSpan.FromDays(2)) + "\",\"last_seen\":\"" + Ago(TimeSpan.FromDays(1)) + "\",\"request_count\":52,\"location\":\"remote\"}");
            if (path.EndsWith("/devices", StringComparison.Ordinal))
                return Items("{\"device_id\":\"device2\",\"device_name\":\"Pixel 8\",\"device_platform\":\"android\",\"last_seen_at\":\"" + Ago(TimeSpan.FromHours(3)) + "\",\"override_count\":2,\"profiles\":[{\"profile_id\":\"p2\"}]}," +
                    "{\"device_id\":\"device3\",\"device_name\":\"Living room TV\",\"device_platform\":\"tv\",\"last_seen_at\":\"" + Ago(TimeSpan.FromDays(4)) + "\",\"override_count\":0,\"profiles\":[{\"profile_id\":\"p3\"}]}");
            if (path.EndsWith("/downloads", StringComparison.Ordinal))
                return Items("{\"id\":\"d1\",\"device_id\":\"device3\",\"profile_id\":\"p3\",\"content_id\":\"movie1\",\"episode_id\":\"\",\"title\":\"Dune: Part Two\",\"status\":\"completed\",\"quality\":\"original\",\"file_size\":14200000000,\"created_at\":\"" + Ago(TimeSpan.FromDays(6)) + "\"}," +
                    "{\"id\":\"d2\",\"device_id\":\"device2\",\"profile_id\":\"p2\",\"content_id\":\"series1\",\"episode_id\":\"e1\",\"title\":\"Severance\",\"status\":\"downloading\",\"quality\":\"original\",\"file_size\":2100000000,\"created_at\":\"" + Ago(TimeSpan.FromDays(1)) + "\",\"episode\":{\"season_number\":2,\"episode_number\":4,\"title\":\"Woe's Hollow\"}}," +
                    "{\"id\":\"d3\",\"device_id\":\"device2\",\"profile_id\":\"p2\",\"content_id\":\"series1\",\"episode_id\":\"e2\",\"title\":\"Severance\",\"status\":\"ready\",\"quality\":\"original\",\"file_size\":2300000000,\"created_at\":\"" + Ago(TimeSpan.FromDays(1)) + "\",\"episode\":{\"season_number\":2,\"episode_number\":5,\"title\":\"Trojan's Horse\"}}," +
                    "{\"id\":\"d4\",\"device_id\":\"device2\",\"profile_id\":\"p2\",\"content_id\":\"movie2\",\"episode_id\":\"\",\"title\":\"Blade Runner 2049\",\"status\":\"failed\",\"quality\":\"1080p\",\"file_size\":9800000000,\"created_at\":\"" + Ago(TimeSpan.FromDays(12)) + "\"}");
            if (path.EndsWith("/download-subscriptions", StringComparison.Ordinal))
                return Items("{\"id\":\"sub1\",\"series_id\":\"series1\",\"series_title\":\"Severance\",\"profile_id\":\"p2\",\"device_id\":\"device2\",\"mode\":\"new_only\",\"delete_watched\":true,\"max_storage_bytes\":10737418240,\"active\":true,\"on_device\":3,\"in_progress\":1,\"removed_episodes\":0}");
            if (path.EndsWith("/identities", StringComparison.Ordinal))
                return Items("{\"id\":\"external1\",\"provider_name\":\"Synthetic provider\",\"external_subject\":\"issuer|user-2\",\"username\":\"target\",\"email\":\"target@example.test\",\"linked_at\":\"" + Ago(TimeSpan.FromDays(40)) + "\",\"last_sign_in_at\":\"" + Ago(TimeSpan.FromDays(2)) + "\",\"last_check_status\":\"ok\",\"last_checked_at\":\"" + Ago(TimeSpan.FromHours(6)) + "\"}");
            if (path.EndsWith("/login-sessions", StringComparison.Ordinal))
                return Items("{\"id\":\"ls1\",\"device_name\":\"" +
                    (LongSessionLabel ? "Silo for Windows in the downstairs living room by the hallway and office" : "Silo for Windows") +
                    "\",\"device_platform\":\"windows\",\"ip_address\":\"192.168.1.20\",\"created_at\":\"" + Ago(TimeSpan.FromDays(14)) + "\",\"expires_at\":\"" + Ago(TimeSpan.FromDays(-16)) + "\",\"last_seen_at\":\"" + Ago(TimeSpan.FromMinutes(40)) + "\",\"current\":false}," +
                    "{\"id\":\"ls2\",\"device_name\":\"Pixel 8\",\"device_platform\":\"android\",\"ip_address\":\"203.0.113.77\",\"created_at\":\"" + Ago(TimeSpan.FromDays(3)) + "\",\"expires_at\":\"" + Ago(TimeSpan.FromDays(-27)) + "\",\"last_seen_at\":\"" + Ago(TimeSpan.FromDays(1)) + "\",\"current\":false}");
            if (path.EndsWith("/settings/values", StringComparison.Ordinal))
                return Items("{\"key\":\"synthetic.setting\",\"scope\":\"profile\",\"profile_id\":\"p2\",\"value\":0,\"revision\":\"r1\"},{\"key\":\"player.autoplay\",\"scope\":\"profile\",\"profile_id\":\"p2\",\"value\":true,\"revision\":\"r2\"},{\"key\":\"player.skip_intro\",\"scope\":\"account\",\"value\":false,\"revision\":\"r3\"}");
            if (path == "/api/v2/settings/contract")
                return Json("{\"definitions\":[" +
                    "{\"key\":\"synthetic.setting\",\"label\":\"Synthetic setting\",\"description\":\"A synthetic whole number.\",\"category\":\"display\",\"introduced_in\":1,\"persistence\":\"remote\",\"allowed_scopes\":[\"account\",\"profile\",\"profile_device\",\"profile_client\",\"profile_library\",\"profile_series\"],\"value_schema\":{\"type\":\"integer\",\"nullable\":true,\"minimum\":0},\"default_value\":7,\"resolution_order\":[\"profile_device\",\"profile\",\"account\",\"default\"]}," +
                    "{\"key\":\"player.autoplay\",\"label\":\"Autoplay next episode\",\"description\":\"Start the next episode when one ends.\",\"category\":\"playback\",\"introduced_in\":1,\"persistence\":\"remote\",\"allowed_scopes\":[\"account\",\"profile\",\"profile_device\"],\"value_schema\":{\"type\":\"boolean\"},\"default_value\":false,\"resolution_order\":[\"profile_device\",\"profile\",\"account\",\"default\"]}," +
                    "{\"key\":\"player.skip_intro\",\"label\":\"Skip intros\",\"description\":\"Jump over intro markers automatically.\",\"category\":\"playback\",\"introduced_in\":1,\"persistence\":\"remote\",\"allowed_scopes\":[\"account\",\"profile\"],\"value_schema\":{\"type\":\"boolean\"},\"default_value\":true,\"resolution_order\":[\"profile\",\"account\",\"default\"]}]}");
            if (path.EndsWith("/sections/settings", StringComparison.Ordinal))
                return Items("{\"id\":\"home1\",\"title\":\"Continue watching\",\"position\":0,\"hidden\":false},{\"id\":\"home2\",\"title\":\"Recently added\",\"position\":1,\"hidden\":false},{\"id\":\"home3\",\"title\":\"Up next\",\"position\":2,\"hidden\":true}");
            return null;
        }
        internal bool LongSessionLabel;

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
            if (Rich && RichReply(request, path) is { } rich) return rich;
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
            await ScreenshotsAsync(window, users, server);
            await CallAsync(users, "LoadAsync", new AdminRouteRequest(AdminRoute.Users));
            await SettledAsync(() => Ready(users));
            Check(TabStops(users, "UsersTab_") == 3, "every list tab, selected or not, is a keyboard tab stop");
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
            await InvokeAsync(FindButton(users, "Add User")); await CloseDialogAsync(users, "Create user");
            await InvokeAsync(FindButton(users, "Target"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Segment(0) == "2");
            Check(TabStops(users, "UserTab_") == 7, "every detail tab, selected or not, is a keyboard tab stop");
            foreach (var tab in new[] { "profiles", "access", "sign-in", "activity", "downloads", "preferences", "overview" })
            {
                await InvokeAsync(FindButton(users, TabLabel(tab)));
                await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == tab);
                Check(!((Dictionary<string, string>)Field(users, "_loadErrors")!).Any(), tab + " has no synthetic read failures");
            }
            foreach (var action in new[] { "View as user", "Disable account", "Delete account…", "Password reset link", "Email password reset", "Set password" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            server.Target["role"] = "admin"; server.Version++;
            await InvokeAsync(FindButton(users, "Reload / review current server state")); await SettledAsync(() => Ready(users));
            await InvokeAsync(FindButton(users, "Make owner")); await CloseDialogAsync(users, "Transfer ownership");
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
            foreach (var action in new[] { "Configure break-glass", "Link external identity", "Unlink Synthetic provider", "Sign out all login sessions", "Sign out this login session" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            await InvokeAsync(FindButton(users, "Preferences")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "preferences");
            foreach (var action in new[] { "View JSON · synthetic.setting", "Edit · synthetic.setting", "Remove Synthetic setting", "Hide", "Reset this profile Home layout" })
            { await InvokeAsync(FindButton(users, action)); await CloseDialogAsync(users, action); }
            await InvokeAsync(FindButton(users, "Access & limits")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "access");
            await InvokeAsync(FindButton(users, "Edit Sign-in & role"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var editor = (ContentDialog)Field(users, "_dialog")!;
            var name = Descendants((DependencyObject)editor.Content).OfType<TextBox>().Single(box => box.Text == "Target"); name.Text = "Target Renamed";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null && server.Target.Text("username") == "Target Renamed");
            Check(server.Writes == 1, "one explicit guarded Apply produces exactly one synthetic mutation");
            server.LoseNextWriteResponse = true;
            await InvokeAsync(FindButton(users, "Edit Sign-in & role"));
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
            await InvokeAsync(FindButtons(users, "Edit profile").Last());
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var sparseProfileDialog = (ContentDialog)Field(users, "_dialog")!;
            Descendants((DependencyObject)sparseProfileDialog.Content).OfType<TextBox>().Single(box => box.Text == "Second").Text = "Second Renamed";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
            Check(server.ProfileUpdates.TryDequeue(out var profilePatch) && profilePatch.Count == 1 && profilePatch.Text("name") == "Second Renamed",
                "renaming sparse p3 submits only name, preserving absent library and playback policy fields");
            await InvokeAsync(FindButton(users, "Access & limits"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "access");
            await ContinuationRetryChecksAsync(users, server);
            await PreviewChecksAsync(users);
            var detachedAction = FindButton(users, "View as user");
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
            var retained = FindButton(users, "View as user");
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
    // ── Layout screenshots (synthetic data only) ───────────────────────────────────────────────────────

    private static void Resize(Window window, int width, int height)
    {
        var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(250);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync()));
        await encoder.FlushAsync();
        Program.Log("PASS: " + name + " captured (" + bitmap.PixelWidth + "x" + bitmap.PixelHeight + ")");
    }

    /// <summary>The Users page family at the widths the owner reviews, drawn from synthetic data and written under the run's result directory.</summary>
    private static async Task ScreenshotsAsync(Window window, AdminUsersPage users, Server server)
    {
        var original = window.AppWindow.Size;
        var body = (FrameworkElement)Field(users, "_body")!;
        server.Rich = true;
        // The page body has no background of its own (the shell draws it), and white text would vanish on a bare capture.
        var bodyBackground = (body as Panel)?.Background;
        if (body is Panel bodyPanel) bodyPanel.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        async Task Show(string? id, string tab)
        {
            var request = new AdminRouteRequest(AdminRoute.Users, new Dictionary<string, string> { ["tab"] = tab }) { Segments = id == null ? [] : [id] };
            await CallAsync(users, "LoadAsync", request);
            await SettledAsync(() => Ready(users));
            await Task.Delay(300);
        }
        async Task Shot(string name, int width, int height)
        {
            Resize(window, width, height);
            await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - width) < 2);
            await Task.Delay(500);
            foreach (var scroller in Descendants(body).OfType<ScrollViewer>().Where(item => item.Content is Grid))
            {
                var table = (Grid)scroller.Content;
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, name + "-" + width + " table fits its card without sideways scrolling");
                Program.Log("NOTE: " + name + "-" + width + " table: scroller " + scroller.ActualWidth.ToString("0.#") + ", grid.Width " + table.Width.ToString("0.#") + ", extent " + scroller.ExtentWidth.ToString("0.#") + ", viewport " + scroller.ViewportWidth.ToString("0.#") +
                    ", columns " + string.Join(" ", table.ColumnDefinitions.Select(column => column.ActualWidth.ToString("0") + "/" + column.MinWidth.ToString("0"))));
            }
            await CaptureAsync(body, name + "-" + width + ".png");
        }
        try
        {
            await Show(null, "users");
            foreach (var width in new[] { 1440, 1280, 1024, 800, 560 }) await Shot("users-list", width, width == 1440 ? 900 : width == 1024 ? 768 : 900);
            server.EmptyUsers = true; await Show(null, "users"); await Shot("users-list-empty", 1440, 900); server.EmptyUsers = false;
            server.FailUsers = true; await Show(null, "users"); await Shot("users-list-error", 1440, 900); server.FailUsers = false;
            await Show(null, "invitations"); await Shot("users-invitations", 1440, 900); await Shot("users-invitations", 560, 900);
            await Show(null, "invite-codes"); await Shot("users-invite-codes", 1440, 900); await Shot("users-invite-codes", 560, 900);
            foreach (var tab in new[] { "overview", "profiles", "access", "sign-in", "activity", "downloads", "preferences" })
            {
                await Show("2", tab);
                await Shot("users-detail-" + tab, 1440, 900);
                await Shot("users-detail-" + tab, 1024, 768);
                if (tab is "overview" or "access" or "downloads" or "sign-in") await Shot("users-detail-" + tab, 560, 900);
            }
            server.LongSessionLabel = true;
            foreach (var width in new[] { 1440, 560 })
            {
                Resize(window, width, 900);
                await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - width) < 2);
                await Show("2", "sign-in");
                var sessions = Descendants(body).OfType<Border>().Single(card =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(card) == "UsersSignInSessions");
                var sessionButton = Descendants(sessions).OfType<Button>().Single(button =>
                    button.Content is TextBlock label && label.Text.Contains("downstairs living room"));
                var sessionLabel = (TextBlock)sessionButton.Content;
                sessionLabel.FontSize = 28;
                foreach (var line in Descendants(sessions).OfType<TextBlock>().Where(line => line.Text.Contains("IP address at sign-in")))
                    line.FontSize = 24;
                sessions.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = false,
                    VerticalAlignmentRatio = width == 560 ? 0 : double.NaN
                });
                await Task.Delay(250);
                window.Content.UpdateLayout();
                var offset = sessionButton.TransformToVisual(sessions).TransformPoint(new(0, 0));
                Check(double.IsNaN(sessionButton.Height) && sessionLabel.TextWrapping == TextWrapping.Wrap &&
                      offset.X >= 0 && offset.Y >= 0 && offset.X + sessionButton.ActualWidth <= sessions.ActualWidth + 1 &&
                      offset.Y + sessionButton.ActualHeight <= sessions.ActualHeight + 1,
                      "Enlarged admin sign out action fits its session card");
                if (width == 560) Check(sessionLabel.ActualHeight > 36, "A long enlarged sign out label wraps on a narrow card");
                await CaptureAsync((FrameworkElement)window.Content, $"sess-admin-{width}-large.png");
            }
            server.LongSessionLabel = false;
            await Show("2", "overview"); await Shot("users-detail-overview", 800, 700);
            Check(Descendants(body).OfType<TextBlock>().Any(text => text.Text.StartsWith("Family group", StringComparison.Ordinal)), "Overview names the account's access group as the source of its limits");
        }
        finally
        {
            server.Rich = false;
            if (body is Panel restore) restore.Background = bodyBackground;
            window.AppWindow.Resize(original);
            await Task.Delay(300);
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
                // The page retires the button it was clicked on: it stays disabled, so neither a person nor UI Automation can press it again
                // (Invoke on a disabled button throws UIA_E_ELEMENTNOTENABLED). Enabling it by force then proves the handler refuses too.
                Check(retainedReturn is { IsEnabled: false }, kind + " return leaves its retired button disabled");
                retainedReturn!.IsEnabled = true;
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
        if (requireVisible && !button.IsLoaded && FlyoutOwners.TryGetValue(button, out var owner) && owner.Flyout is { } menu)
        {
            menu.ShowAt(owner);
            await SettledAsync(() => button.IsLoaded);
        }
        if (requireVisible) { button.StartBringIntoView(); await Task.Delay(50); Check(button.IsLoaded && button.IsEnabled, "requested U1 action is loaded and enabled"); }
        if (FrameworkElementAutomationPeer.CreatePeerForElement(button) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider peer)
            throw new InvalidOperationException("The U1 button has no invocation provider.");
        peer.Invoke(); await Task.Delay(50);
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, Button> FlyoutOwners = new();
    /// <summary>Every button under the root, then the rows of each menu a button opens (menu rows are real buttons, found without opening the menu).</summary>
    private static IEnumerable<Button> AllButtons(DependencyObject root)
    {
        // Scrollers and menus build their templates on the first layout pass; make that happen before the tree is searched.
        if (root is UIElement element) element.UpdateLayout();
        foreach (var button in Descendants(root).OfType<Button>().ToList())
        {
            yield return button;
            if (button.Flyout is Flyout { Content: DependencyObject content })
                foreach (var item in Descendants(content).OfType<Button>().ToList()) { FlyoutOwners.AddOrUpdate(item, button); yield return item; }
        }
    }
    /// <summary>The tab buttons of one strip (by automation id prefix) that keyboard Tab stops on.</summary>
    private static int TabStops(DependencyObject root, string prefix) => AllButtons(root).Count(button =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button).StartsWith(prefix, StringComparison.Ordinal) && button.IsTabStop && button.IsEnabled);
    private static bool Named(Button button, string label) => button.Content as string == label || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == label;
    private static Button? FindButton(DependencyObject root, string label) => AllButtons(root).FirstOrDefault(button => Named(button, label));
    private static IEnumerable<Button> FindButtons(DependencyObject root, string label) => AllButtons(root).Where(button => Named(button, label)).ToList();
    private static string TabLabel(string tab) => tab switch { "access" => "Access & limits", "sign-in" => "Sign-in", _ => char.ToUpperInvariant(tab[0]) + tab[1..] };
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
