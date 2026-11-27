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
using Microsoft.UI.Xaml.Automation;
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
        internal readonly ConcurrentQueue<string> CreatedBodies = new();
        internal readonly ConcurrentQueue<string> ResetBodies = new();
        internal readonly ConcurrentQueue<JsonObject> ProfileUpdates = new();
        internal readonly ConcurrentQueue<JsonObject> AccountBodies = new();
        internal readonly ConcurrentQueue<JsonObject> QuotaBodies = new();
        internal readonly ConcurrentQueue<JsonObject> CodeBodies = new(), InvitationBodies = new(), ProfileCreates = new(), IdentityBodies = new();
        internal readonly JsonObject Target = JsonNode.Parse("""{"id":"2","username":"Target","email":"target@example.test","role":"user","is_owner":false,"enabled":true,"password_login":true,"password_change_required":false,"break_glass":false,"access_group_id":null,"max_profiles":null,"effective_policy":{"max_profiles":3,"max_streams":0,"max_transcodes":0,"transcode_allowed":true,"permissions":[],"library_ids":null},"created_at":"2026-11-16T00:00:00Z"}""")!.AsObject();
        internal int Version = 1;
        internal int Writes;
        internal bool LoseNextWriteResponse;
        internal int AvatarReads;
        internal bool FailNextFixturePage;
        internal int FixturePageCalls;
        internal TaskCompletionSource? HoldWatchSummary;
        private const string Capabilities = """{"available":true,"allowed":true,"state":"available","guarded_configuration":true,"access_groups":true,"default_profile":true,"profile_limits":true,"profile_management":true,"profile_sections":true,"profile_avatars":true,"account_devices":true,"account_downloads":true,"login_sessions":true,"password_reset_link":true,"password_reset_email":true,"ownership_transfer":true,"exact_identity_filter":true,"policy_defaults":true,"request_usage":true,"watch_summary":true,"admin_identities":true,"break_glass":true,"admin_management":true,"email_delivery":true,"profileless":true,"client_selected_code":true,"manifest_revision":30,"scopes":["account","profile","profile_device","profile_client","profile_library","profile_series"],"client_families":["tv","mobile","tablet","desktop","web"]}""";
        private const string Profiles = """{"items":[{"id":"p2","name":"Primary","is_primary":true,"is_child":false,"has_pin":true,"avatar":null},{"id":"p3","name":"Second","is_primary":false,"is_child":false,"has_pin":false,"avatar":null}]}""";
        // ── Synthetic data for the layout screenshots (Rich) ───────────────────────────────────────────────────
        internal bool Rich;
        internal bool EmptyUsers;
        internal bool MoreUsers;
        internal readonly ConcurrentQueue<string> IdentityQueries = new();
        internal bool FailUsers;
        private static string Ago(TimeSpan span) => DateTimeOffset.UtcNow.Subtract(span).ToString("O");
        private readonly string ProfileSeenA = Ago(TimeSpan.FromHours(2)), ProfileSeenB = Ago(TimeSpan.FromDays(3));
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
                if (request.RequestUri!.Query.Contains("identity=", StringComparison.Ordinal))
                {
                    var asked = Uri.UnescapeDataString(request.RequestUri.Query);
                    IdentityQueries.Enqueue(asked);
                    return Items(asked.Contains("zed.remote@example.test", StringComparison.Ordinal) ? User("8", "Zed Remote", "zed.remote@example.test", "user", false, true, null, "2026-06-01T10:00:00Z", null) : "");
                }
                var everyone = string.Join(",",
                [
                    User("1", "Owner", "owner@example.test", "admin", true, true, null, "2026-01-08T10:00:00Z", Ago(TimeSpan.FromHours(1))),
                    RichTarget().ToJsonString(),
                    User("3", "Alice Johnson", "alice@example.test", "user", false, true, "g2", "2026-03-02T15:12:00Z", Ago(TimeSpan.FromDays(3))),
                    User("4", "Bob", "bob@example.test", "user", false, false, null, "2026-04-20T08:00:00Z", null),
                    User("5", "Carol Admin", "carol@example.test", "admin", false, true, null, "2026-02-11T19:45:00Z", Ago(TimeSpan.FromMinutes(20))),
                    User("6", "Dave", "dave@example.test", "user", false, true, "g1", "2026-09-05T12:00:00Z", null),
                    User("7", "Eleanor Vandenberghe-Wolfeschlegelstein", "eleanor.vandenberghe.wolfeschlegelstein@a-rather-long-example-domain.test", "user", false, true, "g2", "2026-05-30T07:30:00Z", Ago(TimeSpan.FromDays(45))),
                ]);
                return MoreUsers ? Json("{\"items\":[" + everyone + "],\"page\":{\"has_more\":true,\"next_cursor\":\"c2\"}}") : Items(everyone);
            }
            if (path == "/api/v2/admin/users/2") return Json(RichTarget().ToJsonString(), "\"account-" + Version + "\"");
            if (path == "/api/v2/libraries") return Items("{\"id\":\"l1\",\"name\":\"Movies\",\"type\":\"movie\",\"enabled\":true},{\"id\":\"l2\",\"name\":\"TV Shows\",\"type\":\"series\",\"enabled\":true},{\"id\":\"l3\",\"name\":\"Music\",\"type\":\"music\",\"enabled\":false}");
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
                return Items("{\"id\":\"p2\",\"name\":\"Primary\",\"is_primary\":true,\"is_child\":false,\"has_pin\":true,\"avatar\":null,\"last_seen_at\":\"" + ProfileSeenA + "\"}," +
                    "{\"id\":\"p3\",\"name\":\"Kids\",\"is_primary\":false,\"is_child\":true,\"has_pin\":false,\"avatar\":null,\"last_seen_at\":\"" + ProfileSeenB + "\"}," +
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
            if (path == "/api/v2/admin/users/2/watch-summary" && HoldWatchSummary is { } summary)
                await summary.Task.WaitAsync(token);
            if (path.EndsWith("/svg", StringComparison.Ordinal) || path.StartsWith("/profile-avatars/", StringComparison.Ordinal))
            {
                Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie"), "avatar previews never carry desktop account headers");
                AvatarReads++;
                return new(HttpStatusCode.OK) { Content = new StringContent("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"128\" height=\"128\"/></svg>", Encoding.UTF8, "image/svg+xml") };
            }
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/users/2/password-reset")
            {
                var resetBody = await request.Content!.ReadAsStringAsync(token);
                ResetBodies.Enqueue(resetBody);
                var link = JsonNode.Parse(resetBody)!["delivery"]!.GetValue<string>() == "link";
                return new(HttpStatusCode.OK) { Content = new StringContent(link ? """{"delivery":"link","reset_url":"https://silo.example.test/reset-password?token=fixture-token-0123456789abcdef","expires_at":"2026-11-27T10:00:00Z"}"""
                    : """{"delivery":"email","delivery_status":"sent","expires_at":"2026-11-27T10:00:00Z"}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/users")
            {
                CreatedBodies.Enqueue(await request.Content!.ReadAsStringAsync(token));
                return new(HttpStatusCode.Created) { Content = new StringContent("""{"id":"9","username":"fixture-new-user"}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/invite-codes")
            {
                CodeBodies.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                return new(HttpStatusCode.Created) { Content = new StringContent("""{"id":"c9","code":"FIXTURE1"}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/invitations")
            {
                InvitationBodies.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                return new(HttpStatusCode.Created) { Content = new StringContent("""{"invitation":{"id":"inv9","email":"fixture-invite@example.test"},"delivery_status":"sent","claim_url":"https://silo.example.test/invite/fixture-token-0123456789"}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/users/2/profiles")
            {
                ProfileCreates.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                return new(HttpStatusCode.Created) { Content = new StringContent("""{"id":"p9","name":"Fixture Profile"}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/users/2/identities")
            {
                IdentityBodies.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                return new(HttpStatusCode.Created) { Content = new StringContent("""{"id":"external9"}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                if (request.Method == HttpMethod.Put && path == "/api/v2/admin/users/2")
                {
                    Check(request.Headers.IfMatch.Single().ToString() == "\"account-" + Version + "\"", "account mutation carries its reviewed strong validator");
                    var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                    AccountBodies.Enqueue(patch.Copy());
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
                if (request.Method == HttpMethod.Put && path == "/api/v2/admin/request-users/2/limit")
                {
                    Check(request.Headers.IfMatch.Single().ToString() == "\"quota-1\"", "the request limit carries its own reviewed validator");
                    QuotaBodies.Enqueue(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
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
            if (path == "/api/v2/auth/password-policy")
                return Json("""{"minimum_length":12,"maximum_bytes":72,"require_uppercase":true,"require_special":true}""");
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
            .AddSingleton(new AdminUserAvatarImages(http)).AddSingleton(new AuthApi(client));
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
                if (tab.Item2 == "invitations")
                {
                    await InvokeAsync(FindButton(users, "Create invitation"));
                    await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                    var inviteDialog = (ContentDialog)Field(users, "_dialog")!;
                    ExpectedPrimary = "Send invite";
                    await CheckAccessFormAsync(window, inviteDialog, "Invite someone", "invite-create", "How should they get it?", "Create link", "Send email", "Role", "Access group", "Library Access", "Create their first profile");
                    Descendants((DependencyObject)inviteDialog.Content).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == "InvitationEmail").Text = "fixture-invite@example.test";
                    await InvokeAsync(DialogButton(window, "PrimaryButton"));
                    await SettledAsync(() => Texts(inviteDialog).Any(text => text.StartsWith("Email sent to ", StringComparison.Ordinal)) && inviteDialog.PrimaryButtonText == "Done");
                    Check(Texts(inviteDialog).Any(text => text.StartsWith("https://silo.example.test/invite/")) && !Texts(inviteDialog).Any(text => text.Contains("Private result")), "the invitation window says what happened and shows the link");
                    Check(server.InvitationBodies.TryDequeue(out var invitationBody) && invitationBody.Text("delivery") == "email" && invitationBody.Text("email") == "fixture-invite@example.test" && invitationBody.Text("role") == "user" && invitationBody.Flag("create_profile") && invitationBody.Flag("show_tour")
                        && !invitationBody.ContainsKey("access_group_id") && !invitationBody.ContainsKey("library_ids") && !invitationBody.ContainsKey("note"), "the invitation sends the delivery, the address, the role and the two switches, and inherits the rest");
                    await CapturePasswordDialogAsync(window, inviteDialog, "invite-created");
                    await InvokeAsync(DialogButton(window, "PrimaryButton"));
                    await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
                }
                else if (tab.Item2 == "invite-codes")
                {
                    await InvokeAsync(FindButton(users, "Create invite code"));
                    await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                    var codeDialog = (ContentDialog)Field(users, "_dialog")!;
                    ExpectedPrimary = "Create";
                    await CheckAccessFormAsync(window, codeDialog, "Create Invite Code", "code-create", "Code", "Label", "Max uses");
                    await InvokeAsync(DialogButton(window, "PrimaryButton"));
                    await SettledAsync(() => Texts(codeDialog).Contains("Fill in this field."));
                    Check(server.CodeBodies.IsEmpty, "an empty code is refused at its field before anything is sent");
                    Descendants((DependencyObject)codeDialog.Content).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == "InviteCode").Text = "FIXTURE1";
                    Descendants((DependencyObject)codeDialog.Content).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == "InviteUses").Text = "0";
                    await InvokeAsync(DialogButton(window, "PrimaryButton"));
                    await SettledAsync(() => Texts(codeDialog).Contains("Max uses must be a positive number"));
                    Descendants((DependencyObject)codeDialog.Content).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == "InviteUses").Text = "5";
                    await InvokeAsync(DialogButton(window, "PrimaryButton"));
                    await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
                    Check(server.CodeBodies.TryDequeue(out var codeBody) && codeBody.Text("code") == "FIXTURE1" && codeBody.Text("label") == "" && codeBody.Count("max_uses") == 5 && codeBody.Count == 3, "Create invite code sends the code, the label and the number of uses");
                }
            }
            await InvokeAsync(FindButton(users, "Add User"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var createUserDialog = (ContentDialog)Field(users, "_dialog")!;
            await SettledAsync(() => Descendants((DependencyObject)createUserDialog.Content).OfType<TextBlock>()
                .Any(text => text.Text.StartsWith("At least 12 characters.")));
            Program.Log("PASS: Create user shows the live password policy");
            Check(createUserDialog.PrimaryButtonText == "Save" && createUserDialog.CloseButtonText == "Cancel" && string.IsNullOrEmpty(createUserDialog.SecondaryButtonText),
                "Create user ends in Save and Cancel, not Apply and Read back");
            TextBox CreateBox(string id) => Descendants((DependencyObject)createUserDialog.Content).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == id);
            CreateBox("CreateUserUsername").Text = "fixture-new-user";
            CreateBox("CreateUserEmail").Text = "fixture-new-user@example.test";
            var writesBeforeCreate = server.Writes;
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Descendants((DependencyObject)createUserDialog.Content).OfType<TextBlock>()
                .Any(text => text.Text == "Use at least 12 characters."));
            Check(server.Writes == writesBeforeCreate && createUserDialog.IsPrimaryButtonEnabled,
                "Create user places the password error at its field before sending");
            var createText = Descendants((DependencyObject)createUserDialog.Content).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Check(createText.Contains("Username") && createText.Contains("Email") && createText.Contains("Password") && createText.Contains("Role") && createText.Contains("Require change at first sign-in")
                && createText.Contains("Create a default profile"), "Create user uses the official labels");
            Check(!createText.Any(text => text.Contains("Set an explicit value") || text.Contains("Access Group Id") || text.Contains("Require Password Change")), "Create user shows no raw field names");
            Check(!Descendants((DependencyObject)createUserDialog.Content).OfType<ComboBox>().SelectMany(box => box.Items.OfType<ComboBoxItem>()).Any(item => item.Content is "true" or "false"),
                "Create user has no drop-down holding the words true and false");
            await CheckDialogFieldsInsideAsync(window, createUserDialog, "Create user", "account", "access", "limits");
            await CapturePasswordDialogAsync(window, createUserDialog, "user-create-account");
            await InvokeAsync(FindDialogButton(createUserDialog, "CreateUserTab_access"));
            await CapturePasswordDialogAsync(window, createUserDialog, "user-create-access");
            await InvokeAsync(FindDialogButton(createUserDialog, "CreateUserTab_limits"));
            await CapturePasswordDialogAsync(window, createUserDialog, "user-create-limits");
            // A real save: the typed account, the permission switches and one overridden limit; everything else inherits and is not sent.
            T CreateControl<T>(string id) where T : FrameworkElement => Descendants((DependencyObject)createUserDialog.Content).OfType<T>().First(element => AutomationProperties.GetAutomationId(element) == id);
            CreateControl<PasswordBox>("CreateUserPassword").Password = "Synthetic-Fixture-Password-1!";
            CreateControl<ToggleSwitch>("CreateUserMetadataCuration").IsOn = true;
            CreateControl<ToggleSwitch>("CreateUser_maxStreamsOverride").IsOn = true;
            CreateControl<TextBox>("CreateUser_maxStreams").Text = "2";
            CreateControl<ToggleSwitch>("CreateUserRequireChange").IsOn = true;
            await SettledAsync(() => createUserDialog.IsPrimaryButtonEnabled);
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Field(users, "_dialog") == null && Ready(users));
            Check(server.CreatedBodies.TryDequeue(out var createdBody), "Save sends one create request");
            var created = JsonNode.Parse(createdBody!)!.AsObject();
            Check(created["username"]?.GetValue<string>() == "fixture-new-user" && created["email"]?.GetValue<string>() == "fixture-new-user@example.test" && created["role"]?.GetValue<string>() == "user"
                && created["password"]?.GetValue<string>() == "Synthetic-Fixture-Password-1!" && created["require_password_change"]?.GetValue<bool>() == true && created["create_default_profile"]?.GetValue<bool>() == true,
                "the create request carries the typed account fields");
            Check(created["permissions"]!.AsArray().Select(node => node!.GetValue<string>()).Order().SequenceEqual(["marker_edit", "metadata_curation"]), "the create request carries the permission switches (Marker Editing starts on)");
            Check(created["max_streams"]?.GetValue<long>() == 2 && !created.ContainsKey("max_transcodes") && !created.ContainsKey("download_allowed") && !created.ContainsKey("library_ids") && !created.ContainsKey("max_playback_quality"),
                "only the overridden limit is sent; everything that inherits is left out");
            await InvokeAsync(FindButton(users, "Add User"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            createUserDialog = (ContentDialog)Field(users, "_dialog")!;
            await CloseDialogAsync(users, "Create user");
            int ReadCount(string path) => server.Requests.Count(request => request.Split('?')[0] == "GET " + path);
            var listReads = ReadCount("/api/v2/admin/users");
            var summary = server.HoldWatchSummary = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await InvokeAsync(FindButton(users, "Target"));
                await SettledAsync(() => ReadCount("/api/v2/admin/users/2/watch-summary") > 0 &&
                    Descendants(users).OfType<TextBlock>().Any(text => text.Text == "Second"));
                Check(!Ready(users), "overview renders profiles while the watch-summary read is still in flight");
            }
            finally
            {
                server.HoldWatchSummary = null;
                summary.TrySetResult();
            }
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Segment(0) == "2");
            Check(TabStops(users, "UserTab_") == 7, "every detail tab, selected or not, is a keyboard tab stop");
            var accountReads = ReadCount("/api/v2/admin/users/2");
            var profileReads = ReadCount("/api/v2/admin/users/2/profiles");
            await InvokeAsync(FindButton(users, "Profiles"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "profiles");
            Check(ReadCount("/api/v2/admin/users/2") == accountReads &&
                ReadCount("/api/v2/admin/users/2/profiles") == profileReads, "Profiles shares the fresh account review and overview profile rows");
            await InvokeAsync(FindButton(users, "Users"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Segment(0) == null);
            Check(ReadCount("/api/v2/admin/users") == listReads && FindButton(users, "Target") != null, "returning to Users restores the fresh list without a read");
            await InvokeAsync(FindButton(users, "Target"));
            await SettledAsync(() => Ready(users) && shell.CurrentRequest.Segment(0) == "2");
            Check(ReadCount("/api/v2/admin/users/2") == accountReads, "returning to the fresh account keeps its review");
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
            await Task.Delay(600);
            var readsBefore = server.AvatarReads;
            await InvokeAsync(FindButton(users, "Add profile"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            var avatarDialog = (ContentDialog)Field(users, "_dialog")!;
            ExpectedPrimary = "Save profile";
            try { await SettledAsync(() => server.AvatarReads - readsBefore >= 26); } catch (TimeoutException) { Check(false, "the avatar previews load as the picker is shown (read " + (server.AvatarReads - readsBefore) + " of 26)"); }
            await CheckAccessFormAsync(window, avatarDialog, "New profile", "profile-new", "Profile", "Access", "Name", "PIN (optional)", "Kids profile", "Maximum content rating", "Maximum playback quality", "Restrict libraries", "Preset avatars");
            await Task.Delay(500);
            var avatarGrid = Descendants(avatarDialog).OfType<GridView>().Single();
            avatarGrid.SelectedItem = avatarGrid.Items[1];
            await Task.Delay(200);
            Check(Descendants(avatarDialog).OfType<Grid>().Where(grid => grid.Width == 92).SelectMany(grid => Descendants(grid).OfType<Image>()).Any(image => image.Source != null), "avatar selection has a live graphical preview");
            Check(server.AvatarReads - readsBefore == 26, "one visible avatar batch loads only eighteen current-style and eight legacy presets (read " + (server.AvatarReads - readsBefore) + ")");
            // A real add: a name only; everything the form owns is sent, nothing else.
            Descendants((DependencyObject)avatarDialog.Content).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == "ProfileName").Text = "Fixture Profile";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
            Check(server.ProfileCreates.TryDequeue(out var profileCreate) && profileCreate.Text("name") == "Fixture Profile" && !profileCreate.ContainsKey("pin") && !profileCreate.ContainsKey("max_content_rating") && profileCreate.Count("max_profiles") == 0
                && !profileCreate.Flag("library_restrictions_enabled") && profileCreate["allowed_library_ids"] is JsonArray { Count: 0 }, "Add profile sends the name, the kids and library settings, and what was chosen");
            Check(profileCreate.ContainsKey("avatar") && profileCreate.Text("avatar").StartsWith("preset:", StringComparison.Ordinal), "Add profile sends the preset that was picked");
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
            await CheckAccessFormAsync(window, editor, "Sign-in & role", "access-signin", "Username", "Email", "Role", "Can sign in", "Profiles allowed");
            var name = Descendants((DependencyObject)editor.Content).OfType<TextBox>().Single(box => box.Text == "Target"); name.Text = "Target Renamed";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null && server.Target.Text("username") == "Target Renamed");
            Check(server.Writes == 1, "one Save produces exactly one synthetic mutation");
            Check(server.AccountBodies.TryDequeue(out var renameBody) && renameBody.Count == 1 && renameBody.Text("username") == "Target Renamed", "Sign-in & role sends only the row that changed");
            server.LoseNextWriteResponse = true;
            await InvokeAsync(FindButton(users, "Edit Sign-in & role"));
            await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
            editor = (ContentDialog)Field(users, "_dialog")!;
            name = Descendants((DependencyObject)editor.Content).OfType<TextBox>().Single(box => box.Text == "Target Renamed");
            name.Text = "Applied Without Response";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            var workspace = (AdminUsersWorkspace)Field(users, "_workspace")!;
            await SettledAsync(() => workspace.OutcomeUnknown && !editor.IsPrimaryButtonEnabled);
            Check(server.Writes == 2, "an unknown account outcome is not sent a second time from the same window");
            Check(Texts(editor).Any(text => text.Contains("couldn't confirm that this change was saved", StringComparison.Ordinal)) && !Texts(editor).Any(text => text.Contains("Read back", StringComparison.Ordinal) || text.Contains("Apply", StringComparison.Ordinal)),
                "the window says it could not confirm the change in plain words");
            await CloseDialogAsync(users, "Unknown outcome reconciliation");
            await SettledAsync(() => Ready(users));
            Check(server.Target.Text("username") == "Applied Without Response" && server.Writes == 2, "closing the window reads the account again and reconciles the applied state without another write");
            while (server.AccountBodies.TryDequeue(out _)) { }
            foreach (var requirePasswordChange in new[] { true, false })
            {
                var writesBeforePassword = server.Writes;
                await InvokeAsync(FindButton(users, "Set password"));
                await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                var passwordDialog = (ContentDialog)Field(users, "_dialog")!;
                var dialogTexts = () => Descendants((DependencyObject)passwordDialog.Content).OfType<TextBlock>().Select(text => text.Text).ToArray();
                var password = Descendants((DependencyObject)passwordDialog.Content).OfType<PasswordBox>().Single();
                var mustChange = Descendants((DependencyObject)passwordDialog.Content).OfType<CheckBox>().Single(box => AutomationProperties.GetAutomationId(box) == "PasswordResetMustChange");
                Check(passwordDialog.Title is string dialogTitle && dialogTitle.StartsWith("Reset password for ", StringComparison.Ordinal) && dialogTexts().Contains("Set a temporary password") && dialogTexts().Contains("Email a reset link") && dialogTexts().Contains("Create a link to share"),
                    "Set password opens the official reset dialog with its three choices (title " + passwordDialog.Title + ": " + string.Join(" / ", dialogTexts()) + ")");
                Check(passwordDialog.PrimaryButtonText == "Set password" && passwordDialog.CloseButtonText == "Cancel" && string.IsNullOrEmpty(passwordDialog.SecondaryButtonText),
                    "the reset dialog ends in Set password and Cancel, not Apply and Read back");
                Check(mustChange.IsChecked == true, "the temporary password must be changed at the next sign-in by default");
                if (!requirePasswordChange) mustChange.IsChecked = false;
                Check(dialogTexts().Any(text => text.StartsWith("At least 12 characters and no more than")), "Set password shows the live password policy");
                foreach (var invalidPassword in new[] { "", "short" })
                {
                    password.Password = invalidPassword;
                    await SettledAsync(() => !passwordDialog.IsPrimaryButtonEnabled);
                    Check(ReferenceEquals(Field(users, "_dialog"), passwordDialog) && server.Writes == writesBeforePassword && server.PasswordRequirements.IsEmpty,
                        "a blank or short password cannot be set and sends nothing, must-change=" + requirePasswordChange);
                }
                if (requirePasswordChange) await CheckDialogFieldsInsideAsync(window, passwordDialog, "Reset password");
                if (requirePasswordChange) await CapturePasswordDialogAsync(window, passwordDialog, "user-reset-password");
                password.Password = "Synthetic-Fixture-Password-1!";
                await SettledAsync(() => passwordDialog.IsPrimaryButtonEnabled);
                await InvokeAsync(DialogButton(window, "PrimaryButton"));
                await SettledAsync(() => dialogTexts().Any(text => text.StartsWith("Password set. ") && text.Contains("is signed out everywhere.")));
                Check(passwordDialog.PrimaryButtonText == "Done" && dialogTexts().Any(text => text.Contains(requirePasswordChange ? "They must choose a new one" : "signed out everywhere.")), "the dialog says the password was set and offers Done");
                if (requirePasswordChange) await CapturePasswordDialogAsync(window, passwordDialog, "user-reset-password-done");
                await InvokeAsync(DialogButton(window, "PrimaryButton"));
                await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
                Check(server.PasswordRequirements.TryDequeue(out var sentRequirement) && sentRequirement == (requirePasswordChange ? true : null),
                    requirePasswordChange ? "a temporary password is sent with require_password_change true" : "a permanent password sends no require_password_change (the official form omits it)");
                Check(server.Writes == writesBeforePassword + 1 && server.Target.Flag("password_change_required") == requirePasswordChange,
                    "one guarded password submission preserves the selected server requirement");
            }
            foreach (var method in new[] { "link", "email" })
            {
                await InvokeAsync(FindButton(users, "Set password"));
                await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                var resetDialog = (ContentDialog)Field(users, "_dialog")!;
                var resetTexts = () => Descendants((DependencyObject)resetDialog.Content).OfType<TextBlock>().Select(text => text.Text).ToArray();
                var option = Descendants((DependencyObject)resetDialog.Content).OfType<RadioButton>().First(radio => AutomationProperties.GetAutomationId(radio) == "PasswordResetOption_" + method);
                option.IsChecked = true;
                await SettledAsync(() => resetDialog.PrimaryButtonText == (method == "link" ? "Create link" : "Email link") && resetDialog.IsPrimaryButtonEnabled);
                await InvokeAsync(DialogButton(window, "PrimaryButton"));
                if (method == "link")
                {
                    await SettledAsync(() => resetTexts().Any(text => text.StartsWith("https://")));
                    Check(resetTexts().Any(text => text.StartsWith("Share this link only with ")) && Descendants((DependencyObject)resetDialog.Content).OfType<Button>().Any(button => button.Content?.ToString() == "Copy link" || AutomationProperties.GetName(button) == "Copy link"),
                        "Create link shows the link, who to share it with and a Copy link button");
                    await CapturePasswordDialogAsync(window, resetDialog, "user-reset-link");
                }
                else
                {
                    await SettledAsync(() => resetTexts().Any(text => text.StartsWith("Sent a reset link to ")));
                    Check(resetDialog.PrimaryButtonText == "Done", "Email link says it sent the link and offers Done");
                }
                await InvokeAsync(DialogButton(window, "PrimaryButton"));
                await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
                Check(server.ResetBodies.TryDequeue(out var resetBody) && JsonNode.Parse(resetBody)!["delivery"]!.GetValue<string>() == method, "the " + method + " reset is sent with that delivery and nothing else changes");
            }
            // The other Access cards: each is the official form, whole inside its window at 1440 and 560, captured, and sends only the row that changed.
            async Task<ContentDialog> OpenCardAsync(string button)
            {
                await InvokeAsync(FindButton(users, button));
                await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                return (ContentDialog)Field(users, "_dialog")!;
            }
            async Task SaveCardAsync()
            {
                await InvokeAsync(DialogButton(window, "PrimaryButton"));
                await SettledAsync(() => Ready(users) && Field(users, "_dialog") == null);
            }
            T Control<T>(ContentDialog dialog, string id) where T : FrameworkElement => Descendants((DependencyObject)dialog.Content).OfType<T>().First(element => AutomationProperties.GetAutomationId(element) == id);
            var cardWrites = server.Writes;
            while (server.AccountBodies.TryDequeue(out _)) { }
            var libraryDialog = await OpenCardAsync("Edit Library access");
            await CheckAccessFormAsync(window, libraryDialog, "Library access", "access-library", "Libraries", "Marker editing", "Metadata curation");
            Control<ToggleSwitch>(libraryDialog, "Access_metadata_curation").IsOn = true;
            await SaveCardAsync();
            server.AccountBodies.TryDequeue(out var libraryBody);
            Check(libraryBody != null && libraryBody.Count == 1 && libraryBody["permissions"] is JsonArray sentPermissions && sentPermissions.Select(node => node!.GetValue<string>()).SequenceEqual(["metadata_curation"]),
                "Library access sends only the permission list that changed (sent " + libraryBody?.ToJsonString() + ")");
            var playbackDialog = await OpenCardAsync("Edit Playback & streaming");
            await CheckAccessFormAsync(window, playbackDialog, "Playback & streaming", "access-playback", "Max quality", "Simultaneous streams", "Video transcoding", "Audio-only transcoding", "Bitrate cap, remote", "Bitrate cap, local");
            await InvokeAsync(FindDialogButton(playbackDialog, "AccessStreamsCustom"));
            Control<TextBox>(playbackDialog, "AccessStreamsValue").Text = "2";
            await SaveCardAsync();
            Check(server.AccountBodies.TryDequeue(out var streamsBody) && streamsBody.Count == 1 && streamsBody["max_streams"]!.GetValue<long>() == 2, "Playback sends only the streams limit it set; every Default row sends nothing");
            var downloadsDialog = await OpenCardAsync("Edit Downloads");
            await CheckAccessFormAsync(window, downloadsDialog, "Downloads", "access-downloads", "Offline downloads", "Server-prepared downloads");
            await InvokeAsync(FindDialogButton(downloadsDialog, "AccessDownloadCustom"));
            Control<ComboBox>(downloadsDialog, "AccessDownloadValue").SelectedIndex = 1;
            await SaveCardAsync();
            Check(server.AccountBodies.TryDequeue(out var downloadBody) && downloadBody.Count == 1 && !downloadBody["download_allowed"]!.GetValue<bool>(), "Downloads sends Not allowed as a real false for the one row set to Custom");
            var requestsDialog = await OpenCardAsync("Edit Requests");
            await CheckAccessFormAsync(window, requestsDialog, "Requests", "access-requests", "Can request media");
            await InvokeAsync(FindDialogButton(requestsDialog, "AccessRequestsCustom"));
            Control<ComboBox>(requestsDialog, "AccessRequestsValue").SelectedIndex = 1;
            await SaveCardAsync();
            Check(server.AccountBodies.TryDequeue(out var requestsBody) && requestsBody.Count == 1 && !requestsBody["requests_allowed"]!.GetValue<bool>(), "Requests sends the one switch it set");
            var quotaDialog = await OpenCardAsync("Edit limit");
            await CheckAccessFormAsync(window, quotaDialog, "Request quota", "access-quota", "Approval", "Limit");
            Control<ComboBox>(quotaDialog, "RequestApproval").SelectedIndex = 2;
            Control<ComboBox>(quotaDialog, "RequestLimit").SelectedIndex = 1;
            Control<TextBox>(quotaDialog, "RequestLimitMax").Text = "5"; Control<TextBox>(quotaDialog, "RequestLimitWindow").Text = "14";
            await SaveCardAsync();
            Check(server.QuotaBodies.TryDequeue(out var quotaBody) && quotaBody.Text("approval_mode") == "auto" && quotaBody.Text("limit_mode") == "custom" && quotaBody.Count("max_requests") == 5 && quotaBody.Count("window_days") == 14 && quotaBody.Count == 4,
                "Request quota sends the four fields of the record under its own validator");
            Check(server.Writes == cardWrites + 5, "each card Save was exactly one synthetic mutation");
            await InvokeAsync(FindButton(users, "Sign-in")); await SettledAsync(() => Ready(users) && shell.CurrentRequest.Get("tab") == "sign-in");
            var glassDialog = await OpenCardAsync("Configure break-glass");
            await CheckAccessFormAsync(window, glassDialog, "Break-glass", "access-breakglass", "Break-glass account");
            Control<ToggleSwitch>(glassDialog, "BreakGlassSwitch").IsOn = true;
            await SaveCardAsync();
            Check(server.AccountBodies.TryDequeue(out var glassBody) && glassBody.Count == 1 && glassBody.Flag("break_glass"), "Break-glass sends only the switch");
            var identityDialog = await OpenCardAsync("Link external identity");
            ExpectedPrimary = "Connect";
            await CheckAccessFormAsync(window, identityDialog, "Connect a sign-in identity", "identity-link", "Provider", "Subject", "Username", "Email", "Name");
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Texts(identityDialog).Contains("Enter the provider's subject for this person."));
            Check(server.IdentityBodies.IsEmpty, "an empty subject is refused before anything is sent");
            Control<TextBox>(identityDialog, "IdentitySubject").Text = "  issuer|user-2  ";
            await SaveCardAsync();
            Check(server.IdentityBodies.TryDequeue(out var identityBody) && identityBody.Count == 2 && identityBody.Text("installation_id") == "provider1" && identityBody.Text("external_subject") == "issuer|user-2", "Link identity sends the provider and the exact subject only");
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
            var pinDialog = await OpenCardAsync("Set PIN");
            ExpectedPrimary = "Save PIN";
            await CheckAccessFormAsync(window, pinDialog, "Set PIN", "profile-pin", "New PIN");
            Control<PasswordBox>(pinDialog, "ProfileNewPin").Password = "12";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await SettledAsync(() => Texts(pinDialog).Contains("PIN must be exactly 4 digits."));
            Check(server.ProfileUpdates.IsEmpty, "a short PIN is refused at its field before anything is sent");
            Control<PasswordBox>(pinDialog, "ProfileNewPin").Password = "1234";
            await SaveCardAsync();
            Check(server.ProfileUpdates.TryDequeue(out var pinPatch) && pinPatch.Count == 1 && pinPatch.Text("pin") == "1234", "Set PIN sends only the PIN");
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

    /// <summary>
    /// Every input in a dialog (a text box, password box or drop-down that is shown) lies inside the dialog's content area with its whole border, at 1440 and 560 px,
    /// on each tab: a form wider than the area it scrolls in would cut off the right edge of its fields.
    /// </summary>
    private static string ExpectedPrimary = "Save";
    private static string[] Texts(ContentDialog dialog) => Descendants((DependencyObject)dialog.Content).OfType<TextBlock>().Select(text => text.Text).ToArray();

    /// <summary>A rebuilt Users form: official labels, Save and Cancel, no developer wording, every input whole inside the window at 1440 and 560, and captures of both.</summary>
    private static async Task CheckAccessFormAsync(Window window, ContentDialog dialog, string label, string capture, params string[] labels)
    {
        var texts = Texts(dialog);
        foreach (var expected in labels) Check(texts.Contains(expected), label + " shows the official label \"" + expected + "\" (found: " + string.Join(" / ", texts.Take(40)) + ")");
        Check(dialog.PrimaryButtonText == ExpectedPrimary && dialog.CloseButtonText == "Cancel" && string.IsNullOrEmpty(dialog.SecondaryButtonText), label + " ends in " + ExpectedPrimary + " and Cancel, not Apply and Read back"); ExpectedPrimary = "Save";
        Check(!texts.Any(text => text.Contains("Set an explicit value") || text.Contains("Read back") || text.Contains("Apply") || text.Contains("Inherit (null")), label + " shows no raw wording");
        Check(!Descendants((DependencyObject)dialog.Content).OfType<ComboBox>().SelectMany(box => box.Items.OfType<ComboBoxItem>()).Any(item => item.Content is "true" or "false"), label + " has no drop-down holding the words true and false");
        await CheckDialogFieldsInsideAsync(window, dialog, label);
        await CapturePasswordDialogAsync(window, dialog, capture);
    }

    private static async Task CheckDialogFieldsInsideAsync(Window window, ContentDialog dialog, string label, params string[] tabs)
    {
        var scroller = (ScrollViewer)dialog.Content;
        var original = window.AppWindow.Size;
        var problems = new List<string>();
        try
        {
            foreach (var width in new[] { 1440, 560 })
            {
                Resize(window, width, 900);
                await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - width) < 2);
                await Task.Delay(500);
                foreach (var tab in tabs.Length == 0 ? [""] : tabs)
                {
                    if (tab.Length > 0) { await InvokeAsync(FindDialogButton(dialog, "CreateUserTab_" + tab)); await Task.Delay(300); }
                    scroller.UpdateLayout();
                    var inputs = 0;
                    foreach (var input in Descendants(scroller).OfType<Control>().Where(control => control is TextBox or PasswordBox or ComboBox or CheckBox or RadioButton or ToggleSwitch or Button))
                    {
                        var shown = input.ActualWidth > 0 && input.ActualHeight > 0;
                        for (DependencyObject? parent = input; shown && parent != null && !ReferenceEquals(parent, scroller); parent = VisualTreeHelper.GetParent(parent))
                            if (parent is UIElement { Visibility: Visibility.Collapsed }) shown = false;
                        if (!shown) continue;
                        inputs++;
                        var origin = input.TransformToVisual(scroller).TransformPoint(new Windows.Foundation.Point(0, 0));
                        var right = origin.X + input.ActualWidth;
                        if (origin.X < -0.5 || right > scroller.ViewportWidth + 0.5)
                            problems.Add($"[{width}] {(tab.Length > 0 ? tab + " " : "")}{AutomationProperties.GetAutomationId(input)} spans {origin.X:0.#}..{right:0.#} of {scroller.ViewportWidth:0.#}");
                    }
                    Check(inputs > 0, $"{label} [{width}] {tab} shows inputs to check");
                }
            }
        }
        finally { window.AppWindow.Resize(original); }
        if (tabs.Length > 0) { await InvokeAsync(FindDialogButton(dialog, "CreateUserTab_" + tabs[0])); await Task.Delay(200); }
        Check(problems.Count == 0, label + " keeps every input whole inside its content area" + (problems.Count == 0 ? "" : ": " + string.Join("; ", problems)));
    }

    private static async Task CapturePasswordDialogAsync(Window window, ContentDialog dialog, string name)
    {
        var original = window.AppWindow.Size;
        try
        {
            foreach (var width in new[] { 1440, 560 })
            {
                Resize(window, width, 900);
                await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - width) < 2);
                await CaptureAsync(dialog, $"{name}-{width}.png");
                if (dialog.Content is ScrollViewer { ScrollableHeight: > 8 } scroller)
                {
                    var top = scroller.VerticalOffset;
                    scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(400);
                    await CaptureAsync(dialog, $"{name}-lower-{width}.png");
                    scroller.ChangeView(null, top, null, true); await Task.Delay(200);
                }
            }
        }
        finally { window.AppWindow.Resize(original); }
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
            if (name == "users-list")
            {
                var tableShown = Descendants(body).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "UsersSort_username");
                Check(tableShown == (width >= 1280), name + "-" + width + (width >= 1280 ? " shows the table" : " shows the compact rows"));
                Check(Descendants(body).OfType<TextBox>().Count(box => box.ActualWidth > 0) == 1 && !Descendants(body).OfType<Button>().Any(button => (button.Content as string ?? AutomationProperties.GetName(button)).Contains("exact identity", StringComparison.OrdinalIgnoreCase)),
                    name + "-" + width + " has one search box and no exact-identity box");
            }
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
            foreach (var width in new[] { 1440, 1280, 1279, 1100, 1024, 800, 560 }) await Shot("users-list", width, width == 1440 ? 900 : width == 1024 ? 768 : 900);
            // One search box that also asks the server's exact filter for an account that is not in the loaded pages (debounced, one answer per search).
            server.MoreUsers = true; await Show(null, "users");
            Resize(window, 1440, 900); await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - 1440) < 2); await Task.Delay(400);
            bool Listed(string id) => Descendants(body).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "UsersOpen_" + id);
            var search = Descendants(body).OfType<TextBox>().First(box => box.ActualWidth > 0);
            while (server.IdentityQueries.TryDequeue(out _)) { }
            search.Text = "zed.remote@exam"; await Task.Delay(100); search.Text = "zed.remote@example.test";
            await SettledAsync(() => Listed("8"));
            Check(server.IdentityQueries.Count == 1 && server.IdentityQueries.TryPeek(out var firstQuery) && firstQuery.Contains("identity=zed.remote@example.test", StringComparison.Ordinal),
                "typing past the debounce asks the server once, for the full text only");
            Check(Listed("8") && !Listed("3"), "the found account is listed and nothing else");
            search.Text = "nobody@example.test";
            await SettledAsync(() => !Listed("8"));
            Check(!Listed("8"), "another search never shows the earlier search's result");
            await SettledAsync(() => server.IdentityQueries.Count == 2);
            await Task.Delay(300);
            Check(!Listed("8") && Descendants(body).OfType<TextBlock>().Any(text => text.Text == AdminUsersText.T("web.admin.users.list.noMatches")), "a search with no answer shows the official empty text");
            search.Text = "alice";
            await Task.Delay(700);
            Check(Listed("3") && server.IdentityQueries.Count == 2, "text that matches a loaded account asks the server nothing");
            search.Text = "";
            server.MoreUsers = false;
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
            // The Access card forms over the rich account (a group, overrides, libraries), captured wide and narrow.
            await Show("2", "access");
            foreach (var (button, capture) in new[] { ("Edit Sign-in & role", "rich-access-signin"), ("Edit Library access", "rich-access-library"), ("Edit Playback & streaming", "rich-access-playback"),
                ("Edit Downloads", "rich-access-downloads"), ("Edit Requests", "rich-access-requests"), ("Edit limit", "rich-access-quota") })
            {
                Resize(window, 1440, 900);
                await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - 1440) < 2);
                await Task.Delay(600);
                await InvokeAsync(FindButton(users, button));
                await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                var shown = (ContentDialog)Field(users, "_dialog")!;
                await Task.Delay(400);
                await CapturePasswordDialogAsync(window, shown, capture);
                shown.Hide();
                await SettledAsync(() => Field(users, "_dialog") == null && !(bool)Field(users, "_actionBusy")!);
            }
            // The profile, invitation, invite-code and identity forms over the rich data, captured wide and narrow.
            async Task Capture(string button, string capture, Func<ContentDialog, Task>? prepare = null)
            {
                Resize(window, 1440, 900);
                await SettledAsync(() => Math.Abs((window.Content.XamlRoot?.Size.Width ?? 0) - 1440) < 2);
                await Task.Delay(600);
                await InvokeAsync(FindButton(users, button));
                await SettledAsync(() => Field(users, "_dialog") is ContentDialog);
                var shown = (ContentDialog)Field(users, "_dialog")!;
                await Task.Delay(900);
                if (prepare != null) await prepare(shown);
                await CapturePasswordDialogAsync(window, shown, capture);
                shown.Hide();
                await SettledAsync(() => Field(users, "_dialog") == null && !(bool)Field(users, "_actionBusy")!);
            }
            await Show("2", "profiles");
            await Capture("Add profile", "rich-profile-new");
            await Capture("Edit profile", "rich-profile-edit");
            await Capture("Change PIN", "rich-profile-pin");
            await Show("2", "sign-in");
            await Capture("Link external identity", "rich-identity");
            await Show(null, "invitations");
            await Capture("Create invitation", "rich-invitation");
            await Show(null, "invite-codes");
            await Capture("Create invite code", "rich-code-create");
            await Capture("Edit", "rich-code-edit");
            await Capture("Top up invite code BETA2026", "rich-code-topup");
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
        await CallAsync(page, "RowsAsync", "retry-pages", "/api/v2/admin/fixture-pages", generation, false, 100, null);
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
        if (requireVisible) { button.StartBringIntoView(); await Task.Delay(50); Check(button.IsLoaded && button.IsEnabled, "requested U1 action is loaded and enabled (" + AutomationProperties.GetName(button) + " loaded=" + button.IsLoaded + " enabled=" + button.IsEnabled + ")"); }
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
    private static Button FindDialogButton(ContentDialog dialog, string automationId) =>
        Descendants((DependencyObject)dialog.Content).OfType<Button>().First(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == automationId);
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
