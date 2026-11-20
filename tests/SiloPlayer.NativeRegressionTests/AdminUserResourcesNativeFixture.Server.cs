using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// A stateful synthetic Silo server for the Access Groups, Devices and History Import pages. Every value is obviously fake. Writes change the
/// state, so a page that re-reads shows what the "server" now holds; scripted failures cover refused, conflicting and lost answers.
/// </summary>
internal sealed class AdminUserResourcesFakeServer : HttpMessageHandler
{
    internal sealed record Call(string Method, string Path, string Query, string Body, string? IfMatch);
    internal sealed class Script
    {
        internal required string Method { get; init; }
        internal required string Path { get; init; }
        internal int Status { get; init; }
        internal string Body { get; init; } = "{}";
        /// <summary>The write reached the server and changed its state, but the answer was lost.</summary>
        internal bool ApplyThenDrop { get; init; }
        /// <summary>The request never reached the server.</summary>
        internal bool Drop { get; init; }
        internal int Times { get; set; } = 1;
    }

    internal readonly List<Call> Calls = [];
    internal readonly List<Script> Scripts = [];
    private int _nextGroup, _revision;
    private JsonArray _groups = [], _users = [], _libraries = [], _devices = [], _values = [], _sources = [], _mappings = [], _runs = [];
    private readonly Dictionary<string, JsonObject> _limits = [];
    private readonly Dictionary<string, int> _etags = new(StringComparer.Ordinal);
    private int _runPolls, _runSeq;
    internal bool EmptyFleet, EmptyGroups, NoToken;

    internal AdminUserResourcesFakeServer() => Reset();

    internal void Reset()
    {
        Calls.Clear(); Scripts.Clear(); _etags.Clear(); _runPolls = 0; _runSeq = 1; EmptyFleet = EmptyGroups = NoToken = false; _nextGroup = 4; _revision = 29;
        _groups = Arr("""
            [{"id":"2","name":"Family","description":"Synthetic household","library_ids":null,"max_playback_quality":"","download_allowed":true,"download_transcode_allowed":true,"transcode_allowed":true,"audio_transcode_allowed":true,
              "max_streams":0,"max_transcodes":0,"max_remote_stream_bitrate_kbps":0,"max_local_stream_bitrate_kbps":0,"max_profiles":5,"allowed_permissions":null,"requests_allowed":true,"is_default":true,"created_at":"2026-01-01T00:00:00.000Z","updated_at":"2026-01-01T00:00:00.000Z"},
             {"id":"3","name":"Guests","description":"","library_ids":["1"],"max_playback_quality":"720p","download_allowed":false,"download_transcode_allowed":false,"transcode_allowed":true,"audio_transcode_allowed":true,
              "max_streams":1,"max_transcodes":1,"max_remote_stream_bitrate_kbps":1500,"max_local_stream_bitrate_kbps":0,"max_profiles":2,"allowed_permissions":["marker_edit"],"requests_allowed":true,"is_default":false,"created_at":"2026-01-01T00:00:00.000Z","updated_at":"2026-01-01T00:00:00.000Z"}]
            """);
        _limits.Clear();
        _limits["2"] = Obj("""{"group_id":"2","limit_mode":"inherit","approval_mode":"inherit","max_requests":null,"window_days":null}""");
        _limits["3"] = Obj("""{"group_id":"3","limit_mode":"custom","approval_mode":"manual","max_requests":3,"window_days":7}""");
        _users = Arr("""
            [{"id":"1","username":"fixture-admin","email":"admin@example.invalid","role":"admin","access_group_id":null,"requests_allowed":null},
             {"id":"7","username":"member-one","email":"member-one@example.invalid","role":"user","access_group_id":"2","requests_allowed":null},
             {"id":"8","username":"member-two","email":"member-two@example.invalid","role":"user","access_group_id":"2","requests_allowed":null},
             {"id":"9","username":"guest-one","email":"guest-one@example.invalid","role":"user","access_group_id":"3","requests_allowed":null},
             {"id":"10","username":"guest-two","email":"guest-two@example.invalid","role":"user","access_group_id":"3","requests_allowed":null},
             {"id":"11","username":"ungrouped-user","email":"","role":"user","access_group_id":null,"requests_allowed":null}]
            """);
        _libraries = Arr("""[{"id":"1","name":"Movies","type":"movie","enabled":true},{"id":"2","name":"Shows","type":"show","enabled":false}]""");
        var recent = DateTimeOffset.UtcNow.AddHours(-2).ToString("O"); var old = DateTimeOffset.UtcNow.AddDays(-80).ToString("O");
        _devices = Arr($$"""
            [{"user_id":"7","username":"member-one","email":"member-one@example.invalid","device_id":"desktop","device_name":"Study PC","device_platform":"Windows","override_count":3,"profile_count":1,"last_updated":"{{recent}}","profiles":[{"profile_id":"p7","profile_name":"Alex","last_updated":"{{recent}}"}],"connections":[],"settings":[]},
             {"user_id":"7","username":"member-one","email":"member-one@example.invalid","device_id":"phone","device_name":"Pocket Phone","device_platform":"iOS","override_count":6,"profile_count":2,"last_updated":"{{recent}}","profiles":[{"profile_id":"p7","profile_name":"Alex","last_updated":"{{recent}}"},{"profile_id":"p8","profile_name":"Sam","last_updated":"{{recent}}"}],"connections":[],"settings":[]},
             {"user_id":"8","username":"member-two","email":"member-two@example.invalid","device_id":"tv","device_name":"Living Room TV","device_platform":"Apple TV","override_count":1,"profile_count":1,"last_updated":"{{old}}","profiles":[{"profile_id":"p9","profile_name":"Kai","last_updated":"{{old}}"}],"connections":[],"settings":[]}]
            """);
        _values = Arr("""
            [{"key":"player.hdr_enabled","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":false,"updated_at":"2026-11-01T00:00:00Z"},
             {"key":"player.dv_profile7_hdr10_fallback","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":true,"updated_at":"2026-11-01T00:00:00Z"},
             {"key":"player.audio_sync_ms","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":4500,"updated_at":"2026-11-01T00:00:00Z"},
             {"key":"playback.auto_play_next","scope":"profile","profile_id":"p7","value":false,"updated_at":"2026-11-01T00:00:00Z"}]
            """);
        _sources = Arr("""[{"id":"3","name":"Fixture Jellyfin","source_type":"jellyfin","base_url":"https://source.invalid","system_id":"","enabled":true,"has_admin_token":true,"needs_reconfiguration":false,"sort_order":0}]""");
        _mappings = Arr("""[{"id":"9","source_id":"3","external_user_id":"ext-1","external_user_name":"Synthetic Viewer","silo_user_id":"7","silo_profile_id":"p7"}]""");
        _runs = Arr("""
            [{"id":"run1","mapping_id":"9","user_id":"7","profile_id":"p7","source_type":"jellyfin","connection_mode":"admin_token","status":"running","terminal":false,"cancelable":true,"fetched":10,"matched":8,"unmatched":2,"progress_updated":3,"history_created":4,"watchlist_added":0,"favorites_imported":1,"skipped":0,"warnings":[],"unmatched_samples":[],"created_at":"2026-11-20T10:00:00Z"},
             {"id":"run0","mapping_id":"9","user_id":"7","profile_id":"p7","source_type":"jellyfin","connection_mode":"admin_token","status":"completed","terminal":true,"cancelable":false,"fetched":5,"matched":4,"unmatched":1,"progress_updated":1,"history_created":2,"watchlist_added":0,"favorites_imported":0,"skipped":0,"warnings":["Synthetic warning one"],"unmatched_samples":[{"title":"Synthetic Title","year":1999,"reason":"no match"}],"created_at":"2026-11-19T10:00:00Z","played_since":"2026-11-18T10:00:00Z"}]
            """);
    }
    internal static JsonArray Arr(string json) => JsonNode.Parse(json)!.AsArray();
    internal static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();
    internal JsonObject Group(string id) => _groups.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == id);
    internal JsonObject User(string id) => _users.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == id);
    internal JsonObject Source(string id) => _sources.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == id);
    internal JsonArray Values => _values;
    internal JsonArray Mappings => _mappings;
    internal JsonArray Runs => _runs;
    internal JsonArray Groups => _groups;
    internal JsonArray Sources => _sources;
    internal JsonObject Limit(string id) => _limits[id];
    internal void TouchGroup(string id) => Bump("group" + id);
    internal void TouchSource(string id) => Bump("source" + id);
    internal int Count(string method, string pathContains) => Calls.Count(x => x.Method == method && x.Path.Contains(pathContains, StringComparison.Ordinal));
    internal Call? Last(string method, string pathContains) => Calls.LastOrDefault(x => x.Method == method && x.Path.Contains(pathContains, StringComparison.Ordinal));
    internal void Fail(string method, string path, int status, string body = "{}", int times = 1) => Scripts.Add(new() { Method = method, Path = path, Status = status, Body = body, Times = times });
    internal void LoseAnswer(string method, string path) => Scripts.Add(new() { Method = method, Path = path, ApplyThenDrop = true });
    internal void DropRequest(string method, string path, int times = 1) => Scripts.Add(new() { Method = method, Path = path, Drop = true, Times = times });

    private string Tag(string key) { _etags.TryGetValue(key, out var n); return "\"" + key + "-" + n + "\""; }
    private void Bump(string key) { _etags.TryGetValue(key, out var n); _etags[key] = n + 1; }

    private static HttpResponseMessage Json(JsonNode? body, HttpStatusCode status = HttpStatusCode.OK, string? etag = null, string? location = null, int? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body?.ToJsonString() ?? "", Encoding.UTF8, "application/json") };
        if (etag != null) response.Headers.TryAddWithoutValidation("ETag", etag);
        if (location != null) response.Headers.Location = new Uri(location, UriKind.Relative);
        if (retryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter.Value.ToString());
        return response;
    }
    private static HttpResponseMessage Problem(HttpStatusCode status, string title, string detail, string? type = null) => Json(Obj($$"""{"type":"https://silo.example/docs/api/v2/problems/{{type ?? "validation_failed"}}","title":"{{title}}","detail":"{{detail}}","status":{{(int)status}}}"""), status);
    private static JsonObject Page(JsonArray items, int? revision = null) { var result = new JsonObject { ["items"] = items, ["page"] = new JsonObject { ["has_more"] = false } }; if (revision != null) result["revision"] = revision; return result; }
    private static JsonObject Clone(JsonObject value) => (JsonObject)value.DeepClone();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath; var query = request.RequestUri.Query;
        var call = new Call(request.Method.Method, path, query, body, request.Headers.IfMatch.FirstOrDefault()?.ToString()); Calls.Add(call);
        var script = Scripts.FirstOrDefault(x => x.Method == call.Method && path.StartsWith(x.Path, StringComparison.Ordinal) && x.Times > 0);
        if (script != null)
        {
            script.Times--;
            if (script.Drop) throw new HttpRequestException("synthetic transport failure");
            if (script.ApplyThenDrop) { Route(call); throw new HttpRequestException("synthetic lost answer"); }
            return script.Status == 422 || script.Status == 400 ? Json(JsonNode.Parse(script.Body), (HttpStatusCode)script.Status) : Json(JsonNode.Parse(script.Body), (HttpStatusCode)script.Status);
        }
        return Route(call);
    }

    private HttpResponseMessage Route(Call call)
    {
        var path = call.Path; var method = call.Method;
        if (!path.StartsWith("/api/v2/", StringComparison.Ordinal)) return Json(Obj("{}"));
        var seg = path["/api/v2/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var q = System.Web.HttpUtility.ParseQueryString(call.Query);
        JsonNode? payload = call.Body.Length > 0 ? JsonNode.Parse(call.Body) : null;
        bool Is(string m, params string[] parts) => method == m && seg.Length == parts.Length && parts.Zip(seg).All(x => x.First == "*" || x.First == x.Second);
        string Id(int index) => Uri.UnescapeDataString(seg[index]);

        // ----- shared reads -----
        if (Is("GET", "libraries")) return Json(Page(Clone2(_libraries)));
        if (Is("GET", "admin", "settings", "effective")) return Json(Obj("""{"download.enabled":"false","download.transcode_enabled":"true"}"""), etag: "\"settings-1\"");
        if (Is("GET", "admin", "request-settings")) return Json(Obj("""{"requests_enabled":true,"global_max_requests":10,"global_window_days":7,"global_auto_approval_enabled":false}"""));
        if (Is("GET", "admin", "users", "capabilities")) return Json(Obj("""{"access_groups":true}"""));
        if (Is("GET", "admin", "users")) return Json(Page(Clone2(_users)));
        if (Is("GET", "admin", "users", "*"))
        {
            var user = _users.OfType<JsonObject>().FirstOrDefault(x => x["id"]!.GetValue<string>() == Id(2)); if (user == null) return Problem(HttpStatusCode.NotFound, "Not found", "No such account.", "not_found");
            return Json(Clone(user), etag: Tag("user" + Id(2)));
        }
        if (Is("PUT", "admin", "users", "*"))
        {
            var user = _users.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == Id(2));
            if (call.IfMatch != Tag("user" + Id(2))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The account changed.", "precondition_failed");
            user["access_group_id"] = payload!["access_group_id"]?.GetValue<string>(); Bump("user" + Id(2)); return Json(Clone(user), etag: Tag("user" + Id(2)));
        }
        if (Is("GET", "admin", "users", "*", "profiles"))
            return Json(Page(Id(2) == "7" ? Arr("""[{"id":"p7","name":"Alex"},{"id":"p8","name":"Sam"}]""") : Arr("""[{"id":"p1","name":"Primary"}]""")));

        // ----- access groups -----
        if (Is("GET", "admin", "access-groups")) return Json(EmptyGroups ? Page([]) : Page(new JsonArray(_groups.OfType<JsonObject>().Select(group => { var item = Clone(group); item["member_count"] = _users.OfType<JsonObject>().Count(x => x["access_group_id"]?.GetValue<string>() == group["id"]!.GetValue<string>() && x["role"]!.GetValue<string>() != "admin"); return (JsonNode?)item; }).ToArray())));
        if (Is("POST", "admin", "access-groups"))
        {
            var id = (_nextGroup++).ToString(); var group = Clone(Group("2")); group["id"] = id; group["name"] = payload!["name"]!.GetValue<string>(); group["is_default"] = false; _groups.Add(group);
            _limits[id] = Obj($$"""{"group_id":"{{id}}","limit_mode":"inherit","approval_mode":"inherit","max_requests":null,"window_days":null}"""); return Json(Clone(group), HttpStatusCode.Created, location: "/api/v2/admin/access-groups/" + id);
        }
        if (Is("GET", "admin", "access-groups", "*")) { var group = _groups.OfType<JsonObject>().FirstOrDefault(x => x["id"]!.GetValue<string>() == Id(2)); return group == null ? Problem(HttpStatusCode.NotFound, "Not found", "No such group.", "not_found") : Json(Clone(group), etag: Tag("group" + Id(2))); }
        if (Is("PUT", "admin", "access-groups", "*"))
        {
            if (call.IfMatch != Tag("group" + Id(2))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The group changed.", "precondition_failed");
            var group = Group(Id(2)); foreach (var pair in payload!.AsObject()) group[pair.Key] = pair.Value?.DeepClone();
            if (group["is_default"]!.GetValue<bool>()) foreach (var other in _groups.OfType<JsonObject>().Where(x => x != group)) other["is_default"] = false;
            Bump("group" + Id(2)); return Json(Clone(group), etag: Tag("group" + Id(2)));
        }
        if (Is("DELETE", "admin", "access-groups", "*"))
        {
            if (call.IfMatch != Tag("group" + Id(2))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The group changed.", "precondition_failed");
            var group = Group(Id(2)); foreach (var user in _users.OfType<JsonObject>().Where(x => x["access_group_id"]?.GetValue<string>() == Id(2))) user["access_group_id"] = "2"; _groups.Remove(group); return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (Is("GET", "admin", "request-groups", "*", "limit")) return Json(Clone(_limits[Id(2)]), etag: Tag("limit" + Id(2)));
        if (Is("PUT", "admin", "request-groups", "*", "limit"))
        {
            if (call.IfMatch != Tag("limit" + Id(2))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The limit changed.", "precondition_failed");
            var limit = _limits[Id(2)]; foreach (var pair in payload!.AsObject()) limit[pair.Key] = pair.Value?.DeepClone(); Bump("limit" + Id(2)); return Json(Clone(limit), etag: Tag("limit" + Id(2)));
        }

        // ----- devices -----
        if (Is("GET", "admin", "devices", "capabilities")) return Json(Obj("""{"available":true}"""));
        if (Is("GET", "admin", "devices")) return Json(EmptyFleet ? Page([]) : Page(Clone2(_devices)));
        if (Is("GET", "admin", "devices", "*", "*"))
        {
            var device = _devices.OfType<JsonObject>().FirstOrDefault(x => x["user_id"]!.GetValue<string>() == Id(2) && x["device_id"]!.GetValue<string>() == Id(3));
            return device == null ? Problem(HttpStatusCode.NotFound, "Not found", "No such device.", "not_found") : Json(Clone(device));
        }
        if (Is("GET", "admin", "users", "*", "settings", "values")) return Json(Page(Clone2(_values), _revision));
        if (Is("PUT", "admin", "users", "*", "settings", "values", "*"))
        {
            var row = new JsonObject { ["key"] = Id(5), ["scope"] = "profile_device", ["profile_id"] = q["profile_id"], ["device_id"] = q["device_id"], ["value"] = payload!["value"]?.DeepClone(), ["updated_at"] = "2026-11-20T00:00:00Z" };
            var existing = _values.OfType<JsonObject>().FirstOrDefault(x => x["key"]!.GetValue<string>() == row["key"]!.GetValue<string>() && x["scope"]!.GetValue<string>() == "profile_device" && x["profile_id"]?.GetValue<string>() == q["profile_id"] && x["device_id"]?.GetValue<string>() == q["device_id"]);
            if (existing != null) _values.Remove(existing); _values.Add(row); return Json(Clone(row));
        }
        if (Is("DELETE", "admin", "users", "*", "settings", "values", "*"))
        {
            var existing = _values.OfType<JsonObject>().FirstOrDefault(x => x["key"]!.GetValue<string>() == Id(5) && x["scope"]!.GetValue<string>() == "profile_device" && x["profile_id"]?.GetValue<string>() == q["profile_id"] && x["device_id"]?.GetValue<string>() == q["device_id"]);
            if (existing == null) return Problem(HttpStatusCode.NotFound, "Not found", "No such override.", "not_found"); _values.Remove(existing); return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        // ----- history import -----
        if (Is("GET", "admin", "history-imports", "capabilities")) return Json(Obj("""{"available":true,"guarded_configuration":true,"durable_runs":true,"max_bulk_mappings":200,"played_lookback":true,"max_lookback_hours":8760,"revision":"1","state":"available","allowed":true}"""));
        if (Is("GET", "admin", "history-import-sources")) return Json(Page(Clone2(_sources)));
        if (Is("POST", "admin", "history-import-sources"))
        {
            var id = "4"; var source = Obj($$"""{"id":"{{id}}","name":"x","source_type":"jellyfin","base_url":"https://x.invalid","system_id":"","enabled":true,"has_admin_token":false,"needs_reconfiguration":false,"sort_order":0}""");
            foreach (var pair in payload!.AsObject()) if (pair.Key != "admin_token") source[pair.Key] = pair.Value?.DeepClone(); source["has_admin_token"] = payload["admin_token"] != null; _sources.Add(source); return Json(Clone(source), HttpStatusCode.Created, Tag("source" + id), "/api/v2/admin/history-import-sources/" + id);
        }
        if (Is("GET", "admin", "history-import-sources", "*")) { var source = _sources.OfType<JsonObject>().FirstOrDefault(x => x["id"]!.GetValue<string>() == Id(2)); return source == null ? Problem(HttpStatusCode.NotFound, "Not found", "No such source.", "not_found") : Json(Clone(source), etag: Tag("source" + Id(2))); }
        if (Is("PUT", "admin", "history-import-sources", "*"))
        {
            if (call.IfMatch != Tag("source" + Id(2))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The source changed.", "precondition_failed");
            var source = Source(Id(2)); foreach (var pair in payload!.AsObject()) { if (pair.Key == "admin_token") source["has_admin_token"] = pair.Value!.GetValue<string>().Length > 0; else source[pair.Key] = pair.Value?.DeepClone(); }
            Bump("source" + Id(2)); return Json(Clone(source), etag: Tag("source" + Id(2)));
        }
        if (Is("DELETE", "admin", "history-import-sources", "*")) { if (call.IfMatch != Tag("source" + Id(2))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The source changed.", "precondition_failed"); _sources.Remove(Source(Id(2))); return new HttpResponseMessage(HttpStatusCode.NoContent); }
        if (Is("PUT", "admin", "history-imports", "sources", "*", "token"))
        {
            if (call.IfMatch != Tag("source" + Id(3))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The source changed.", "precondition_failed");
            var source = Source(Id(3)); source["has_admin_token"] = true; Bump("source" + Id(3)); return Json(Clone(source), etag: Tag("source" + Id(3)));
        }
        if (Is("DELETE", "admin", "history-imports", "sources", "*", "token"))
        {
            if (call.IfMatch != Tag("source" + Id(3))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The source changed.", "precondition_failed");
            Source(Id(3))["has_admin_token"] = false; Bump("source" + Id(3)); return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (Is("POST", "admin", "history-imports", "plex", "login")) return Json(Obj("""{"token":"synthetic-plex-credential"}"""));
        if (Is("GET", "admin", "history-imports", "mappings")) return Json(Page(Clone2(_mappings)));
        if (Is("GET", "admin", "history-imports", "mappings", "*")) { var mapping = _mappings.OfType<JsonObject>().FirstOrDefault(x => x["id"]!.GetValue<string>() == Id(3)); return mapping == null ? Problem(HttpStatusCode.NotFound, "Not found", "No such mapping.", "not_found") : Json(Clone(mapping), etag: Tag("mapping" + Id(3))); }
        if (Is("POST", "admin", "history-imports", "mappings"))
        {
            var mapping = Clone(payload!.AsObject()); mapping["id"] = "10"; _mappings.Add(mapping); return Json(Clone(mapping), HttpStatusCode.Created, Tag("mapping10"));
        }
        if (Is("DELETE", "admin", "history-imports", "mappings", "*")) { if (call.IfMatch != Tag("mapping" + Id(3))) return Problem(HttpStatusCode.PreconditionFailed, "Changed", "The mapping changed.", "precondition_failed"); _mappings.Remove(_mappings.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == Id(3))); return new HttpResponseMessage(HttpStatusCode.NoContent); }
        if (Is("GET", "admin", "history-imports", "sources", "*", "users")) return Json(Page(Arr("""[{"id":"ext-1","name":"Synthetic Viewer"},{"id":"ext-2","name":"Another Viewer"},{"id":"ext-3","name":"Third Viewer"}]""")));
        if (Is("GET", "admin", "history-imports", "runs")) return Json(Page(Clone2(_runs)));
        if (Is("GET", "admin", "history-imports", "runs", "*"))
        {
            var run = _runs.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == Id(3));
            if (Id(3) == "run1" && ++_runPolls >= 1) { run["status"] = "completed"; run["terminal"] = true; run["cancelable"] = false; }
            return Json(Clone(run), location: "/api/v2/admin/history-imports/runs/" + Id(3), retryAfter: 1);
        }
        if (Is("POST", "admin", "history-imports", "mappings", "*", "run"))
        {
            var runId = "run" + (++_runSeq); var run = Obj($$"""{"id":"{{runId}}","mapping_id":"{{Id(3)}}","user_id":"7","profile_id":"p7","source_type":"jellyfin","connection_mode":"admin_token","status":"queued","terminal":false,"cancelable":true,"fetched":0,"matched":0,"unmatched":0,"progress_updated":0,"history_created":0,"watchlist_added":0,"favorites_imported":0,"skipped":0,"warnings":[],"unmatched_samples":[],"created_at":"2026-11-20T11:00:00Z"}""");
            _runs.Insert(0, run); return Json(Clone(run), HttpStatusCode.Accepted, location: "/api/v2/admin/history-imports/runs/" + runId, retryAfter: 5);
        }
        if (Is("POST", "admin", "history-imports", "runs", "*", "cancel"))
        {
            var run = _runs.OfType<JsonObject>().First(x => x["id"]!.GetValue<string>() == Id(3)); run["status"] = "canceling"; run["cancelable"] = false;
            return Json(Clone(run), HttpStatusCode.Accepted, location: "/api/v2/admin/history-imports/runs/" + Id(3), retryAfter: 5);
        }
        if (Is("POST", "admin", "history-imports", "sources", "*", "bulk-run"))
        {
            var run = Obj("""{"id":"run3","mapping_id":"9","user_id":"7","profile_id":"p7","source_type":"jellyfin","connection_mode":"admin_token","status":"queued","terminal":false,"cancelable":true,"fetched":0,"matched":0,"unmatched":0,"progress_updated":0,"history_created":0,"watchlist_added":0,"favorites_imported":0,"skipped":0,"warnings":[],"unmatched_samples":[],"created_at":"2026-11-20T11:00:00Z"}""");
            return Json(Obj($$"""{"accepted":1,"active":0,"failed":1,"outcomes":[{"mapping_id":"9","status":"accepted","location":"/api/v2/admin/history-imports/runs/run3","run":{{run.ToJsonString()}}},{"mapping_id":"10","status":"failed","error":"Synthetic bulk failure"}]}"""));
        }
        return Problem(HttpStatusCode.NotFound, "Not found", "Unknown synthetic route " + method + " " + path, "not_found");
    }
    private static JsonArray Clone2(JsonArray array) => new(array.Select(x => x?.DeepClone()).ToArray());
}
