using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

internal static partial class AdminAutoscanSourcesNativeFixture
{
    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived { add { } remove { } }
        public event Action<string, string, JsonElement>? EventReceived { add { } remove { } }
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        public IDisposable Subscribe(params string[] channels) => new Subscription();
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
    private sealed class Server : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly List<AdminAutoscanSource> Sources = [new() { Id = "original", PluginId = "fixture.poll", CapabilityId = "poll", Label = "Original source", DeliveryMode = "poll", Enabled = true, ConnectionId = "connection-original", SourceConfig = new() { ["unknown"] = "retained" }, PathRewrites = [new() { From = "/remote/movies", To = "/media/movies" }] }];
        private readonly List<AdminAutoscanConnection> _connections = [new() { Id = "connection-original", Name = "Original server", Kind = "radarr", HasApiKey = true }];
        internal bool Enabled = true, FailSources;
        internal int DefaultInterval = 600, Debounce = 60, Rotations, ConnectionWrites;
        internal string? LastConnectionBody;
        private int _sequence;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath; Requests.Enqueue(request.Method + " " + request.RequestUri.PathAndQuery);
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Sources)
            {
                const string root = "/api/v2/admin/autoscan";
                if (path == root + "/settings")
                {
                    if (request.Method == HttpMethod.Put)
                    { var write = JsonSerializer.Deserialize<AdminAutoscanSettings>(body, Options)!; Enabled = write.Enabled; DefaultInterval = write.DefaultPollIntervalSeconds; Debounce = write.DebounceSeconds; return Json(new { settings = new { enabled = Enabled, default_poll_interval_seconds = DefaultInterval, debounce_seconds = Debounce }, reschedule_state = "applied" }); }
                    return Json(new { enabled = Enabled, default_poll_interval_seconds = DefaultInterval, debounce_seconds = Debounce });
                }
                if (path == root + "/scan-source-plugins") return Json(new { items = new AdminAutoscanAvailableSource[]
                {
                    new() { PluginId = "fixture.poll", CapabilityId = "poll", DisplayName = "Polling fixture", Descriptor = new() { DeliveryModes = ["poll"], Connection = "required", ConnectionKinds = ["radarr"], Summary = "Synthetic polling descriptor", ConfigForm = new() { Fields = [new() { Key = "flag", Label = "Synthetic flag", Control = "SWITCH", DefaultValue = false }, new() { Key = "roots", Label = "Synthetic roots", Control = "TEXTAREA", FillFrom = "library_paths_movie" }] } } },
                    new() { PluginId = "fixture.webhook", CapabilityId = "arr", DisplayName = "Webhook fixture", Descriptor = new() { DeliveryModes = ["webhook"], Connection = "none", ConnectionKinds = ["radarr"], Summary = "Synthetic webhook descriptor" } }
                }, page = new { has_more = false } });
                if (path == "/api/v2/libraries") return Json(new { items = new[] { new { id = "1", name = "Movies", type = "movies", paths = new[] { "/media/movies" }, enabled = true } }, page = new { has_more = false } });
                if (path == "/api/v2/admin/request-integrations") return Json(new { items = Array.Empty<object>(), page = new { has_more = false } });
                if (path == root + "/connections/test") return Json(new { ok = true, version = "synthetic" });
                if (path == root + "/connections")
                {
                    if (request.Method == HttpMethod.Get) return Json(new { items = _connections, page = new { has_more = false } });
                    ConnectionWrites++; LastConnectionBody = body;
                    var create = JsonSerializer.Deserialize<AdminAutoscanConnectionCreate>(body, Options)!;
                    var connection = new AdminAutoscanConnection { Id = "connection-new", Name = create.Name, Kind = create.Kind, HasApiKey = true }; _connections.Add(connection); return Json(connection, HttpStatusCode.Created);
                }
                if (path == root + "/trigger") return Json(new { key = "autoscan_poll", state = "running", execution_scope = "process" });
                if (path == root + "/sources" && request.Method == HttpMethod.Get)
                    return FailSources ? Json(new { }, HttpStatusCode.ServiceUnavailable) : Json(new { items = Sources, page = new { has_more = false } });
                if (path == root + "/sources" && request.Method == HttpMethod.Post)
                {
                    var write = JsonSerializer.Deserialize<AdminAutoscanSource>(body, Options)!;
                    write.Id = "created-" + ++_sequence; Sources.Add(write); return Json(write, HttpStatusCode.Created);
                }
                if (path.StartsWith(root + "/sources/", StringComparison.Ordinal))
                {
                    var parts = path[(root.Length + 9)..].Split('/'); var id = Uri.UnescapeDataString(parts[0]);
                    var source = Sources.SingleOrDefault(s => s.Id == id); if (source == null) return Json(new { }, HttpStatusCode.NotFound);
                    if (parts.Length > 1 && parts[1] == "rewrite-suggestions") return Json(new { proposed = new[] { new { from = "/remote/extras", to = "/media/movies/extras", match_depth = 2 } }, unmatched = Array.Empty<string>(), ambiguous = Array.Empty<object>(), covered = new[] { "/remote/movies" } });
                    if (parts.Length > 1 && parts[1] == "webhook")
                    {
                        if (parts.Length > 2 && parts[2] == "rotate") Rotations++;
                        source.WebhookConfigured = true; source.WebhookUrl = "/api/v1/autoscan/webhooks/synthetic-" + Rotations; return Json(source);
                    }
                    if (request.Method == HttpMethod.Delete) { Sources.Remove(source); return new(HttpStatusCode.NoContent); }
                    if (request.Method == HttpMethod.Put)
                    {
                        var write = JsonSerializer.Deserialize<AdminAutoscanSourceWrite>(body, Options)!;
                        source.Enabled = write.Enabled; source.Label = write.Label; source.ConnectionId = write.ConnectionId; source.PollIntervalSeconds = write.PollIntervalSeconds; source.PathRewrites = write.PathRewrites; source.SourceConfig = write.SourceConfig;
                        return Json(source);
                    }
                }
                // Shared shell background reads have no data in this synthetic server.
                return Json(new { items = Array.Empty<object>(), page = new { has_more = false } });
            }
        }
        private static HttpResponseMessage Json(object body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(JsonSerializer.Serialize(body, Options), Encoding.UTF8, "application/json") };
    }
}
