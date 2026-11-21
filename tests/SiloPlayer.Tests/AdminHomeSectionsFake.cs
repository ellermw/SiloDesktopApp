using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace SiloPlayer.Tests;

/// <summary>A synthetic Home sections server for the Home sections tests: rows with strong per-row versions and a per-page order version.</summary>
internal sealed class AdminHomeSectionsFake
{
    public readonly Dictionary<string, JsonObject> Rows = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<string>> Orders = new(StringComparer.Ordinal) { ["home"] = [] };
    public readonly Dictionary<string, int> Versions = new(StringComparer.Ordinal);
    public readonly List<(string Method, string Path, string? IfMatch, string Body)> Writes = [];
    /// <summary>Return a response to replace the default for one request, or null to continue normally.</summary>
    public Func<HttpRequestMessage, string, HttpResponseMessage?>? Intercept { get; set; }
    public int OrderVersion = 1;
    public bool Capabilities = true;

    public static string Problem(int status, string detail, string type = "validation-failed", params (string Code, string Detail)[] errors) =>
        new JsonObject
        {
            ["type"] = "https://silo.test/docs/api/v2/problems/" + type, ["title"] = detail, ["status"] = status, ["detail"] = detail,
            ["errors"] = new JsonArray(errors.Select(error => (JsonNode)new JsonObject { ["location"] = "body.title", ["code"] = error.Code, ["detail"] = error.Detail }).ToArray())
        }.ToJsonString();

    public AdminHomeSectionsFake AddRow(string id, string title = "Row", string type = "recently_added", bool enabled = true, bool featured = false,
        string scope = "home", string? library = null, string config = "{}", int limit = 20, bool webOnly = false)
    {
        var key = scope == "home" ? "home" : "library:" + library;
        if (!Orders.ContainsKey(key)) Orders[key] = [];
        Rows[id] = JsonNode.Parse($$"""{"id":"{{id}}","scope":"{{scope}}","library_id":{{(library == null ? "null" : "\"" + library + "\"")}},"position":{{Orders[key].Count}},"section_type":"{{type}}","title":"{{title}}","featured":{{featured.ToString().ToLowerInvariant()}},"item_limit":{{limit}},"config":{{config}},"enabled":{{enabled.ToString().ToLowerInvariant()}},"web_only":{{webOnly.ToString().ToLowerInvariant()}}}""")!.AsObject();
        Orders[key].Add(id); Versions[id] = 1; return this;
    }

    private string RowTag(string id) => "\"" + id + "-" + Versions[id] + "\"";
    private string OrderTag => "\"order-" + OrderVersion + "\"";
    private static HttpResponseMessage Json(string body, string? tag = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, status >= HttpStatusCode.BadRequest ? "application/problem+json" : "application/json") };
        if (tag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(tag);
        return response;
    }

    private static string KeyOf(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query;
        if (!query.Contains("scope=library")) return "home";
        var marker = "library_id="; var at = query.IndexOf(marker, StringComparison.Ordinal);
        return "library:" + query[(at + marker.Length)..].Split('&')[0];
    }

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        var ifMatch = request.Headers.IfMatch.FirstOrDefault()?.ToString();
        if (request.Method != HttpMethod.Get) Writes.Add((request.Method.Method, request.RequestUri.PathAndQuery, ifMatch, body));
        if (Intercept?.Invoke(request, body) is { } intercepted) return intercepted;
        if (path == "/api/v2/admin/sections/capabilities") return Json($$"""{"available":{{Capabilities.ToString().ToLowerInvariant()}},"preview":true,"reset_profiles":true,"web_only":true}""");
        if (path == "/api/v2/sections/recipes") return Json("""{"categories":[]}""");
        var key = KeyOf(request); var order = Orders.TryGetValue(key, out var existing) ? existing : Orders[key] = [];
        var scopeText = key == "home" ? "home" : "library"; var libraryText = key == "home" ? "null" : "\"" + key[8..] + "\"";
        if (path == "/api/v2/admin/sections/order")
        {
            if (request.Method == HttpMethod.Put)
            {
                if (ifMatch != OrderTag) return Json(Problem(412, "stale", "precondition-failed"), status: HttpStatusCode.PreconditionFailed);
                order.Clear(); order.AddRange(JsonNode.Parse(body)!["ordered_ids"]!.AsArray().Select(node => node!.GetValue<string>())); OrderVersion++;
            }
            return Json($$"""{"scope":"{{scopeText}}","library_id":{{libraryText}},"ordered_ids":{{new JsonArray(order.Select(id => (JsonNode)id).ToArray()).ToJsonString()}}}""", OrderTag);
        }
        if (path == "/api/v2/admin/sections/defaults")
        {
            if (ifMatch != OrderTag) return Json(Problem(412, "stale", "precondition-failed"), status: HttpStatusCode.PreconditionFailed);
            OrderVersion++; return Json($$"""{"scope":"{{scopeText}}","library_id":{{libraryText}},"ordered_ids":[]}""", OrderTag);
        }
        if (path == "/api/v2/admin/sections/bulk")
        {
            var payload = JsonNode.Parse(body)!.AsObject(); var ids = payload["library_ids"]!.AsArray();
            OrderVersion++; return Json("{\"created\":" + ids.Count + "}");
        }
        if (path == "/api/v2/admin/sections")
        {
            if (request.Method == HttpMethod.Post)
            {
                var row = JsonNode.Parse(body)!.AsObject(); var id = "new" + (Rows.Count + 1);
                row["id"] = id; row["web_only"] ??= false; row["library_id"] ??= null;
                var target = row["scope"]!.GetValue<string>() == "home" ? "home" : "library:" + row["library_id"]!.GetValue<string>();
                (Orders.TryGetValue(target, out var list) ? list : Orders[target] = []).Add(id); Rows[id] = row; Versions[id] = 1; OrderVersion++;
                return Json(row.ToJsonString(), status: HttpStatusCode.Created);
            }
            return Json("{\"items\":[" + string.Join(",", order.Select(id => Rows[id].ToJsonString())) + "]}");
        }
        if (path.StartsWith("/api/v2/admin/sections/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path.Split('/').Last());
            if (!Rows.TryGetValue(id, out var row)) return Json(Problem(404, "gone", "not-found"), status: HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get) return Json(row.ToJsonString(), RowTag(id));
            if (ifMatch != RowTag(id)) return Json(Problem(412, "stale", "precondition-failed"), status: HttpStatusCode.PreconditionFailed);
            if (request.Method == HttpMethod.Delete)
            {
                Rows.Remove(id); foreach (var list in Orders.Values) list.Remove(id); OrderVersion++; return new(HttpStatusCode.NoContent);
            }
            foreach (var pair in JsonNode.Parse(body)!.AsObject()) row[pair.Key] = pair.Value?.DeepClone();
            Versions[id]++; OrderVersion++; return Json(row.ToJsonString(), RowTag(id));
        }
        return Json("{}");
    }
}
