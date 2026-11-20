using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A canonical read and its strong validator, bound to the authority that read it. Read data is never the mutable draft.</summary>
public sealed class AdminRequestDocument
{
    private readonly JsonObject _value;
    private readonly long _epoch;
    private readonly string _account;
    private readonly AdminMonitorAuthority _authority;
    internal AdminRequestDocument(JsonObject value, string etag, string resource, MonitorAuthorityLease owner)
    {
        _value = (JsonObject)value.DeepClone(); ETag = etag; Resource = resource; _epoch = owner.Epoch; _account = owner.AccountId; _authority = owner.Owner;
    }
    public JsonObject Value => (JsonObject)_value.DeepClone();
    public string ETag { get; }
    internal string Resource { get; }
    internal void RequireOwner(MonitorAuthorityLease owner)
    {
        owner.ThrowIfRetired();
        if (!ReferenceEquals(owner.Owner, _authority) || owner.Epoch != _epoch || owner.AccountId != _account) throw new MonitorAuthorityRetiredException();
        AdminValidators.RequireStrong(ETag);
    }
}

public sealed class AdminRequestRoutes
{
    private readonly long _epoch;
    private readonly string _account;
    private readonly AdminMonitorAuthority _authority;
    internal AdminRequestRoutes(IReadOnlyList<AdminRequestDocument> rows, MonitorAuthorityLease owner) { Rows = rows; _epoch = owner.Epoch; _account = owner.AccountId; _authority = owner.Owner; }
    public IReadOnlyList<AdminRequestDocument> Rows { get; }
    internal void RequireOwner(MonitorAuthorityLease owner) { owner.ThrowIfRetired(); if (!ReferenceEquals(owner.Owner, _authority) || owner.Epoch != _epoch || owner.AccountId != _account) throw new MonitorAuthorityRetiredException(); }
}

public static class AdminRequestJson
{
    public static string Text(JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue value) return "";
        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    }
    public static bool Flag(JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
    public static int Number(JsonNode? node, string key, int fallback = 0) => node?[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;
    public static double FloatingNumber(JsonNode? node, string key, double fallback) => node?[key] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : fallback;
    public static JsonArray Items(JsonNode? node, string key = "items") => node?[key] as JsonArray ?? [];
    public static JsonObject Pick(JsonObject source, params string[] names)
    {
        var body = new JsonObject(); foreach (var name in names) if (source.ContainsKey(name)) body[name] = source[name]?.DeepClone(); return body;
    }
}

public static class AdminRequestRules
{
    public static readonly IReadOnlyList<string> Views = ["needs_approval", "in_progress", "failed", "done"];
    public static readonly IReadOnlyList<string> RowTypes = ["tmdb_chart", "tmdb_discover", "mdblist", "tmdb_list", "studios", "networks", "genres"];
    public static string View(JsonNode request)
    {
        var outcome = AdminRequestJson.Text(request, "outcome");
        if (outcome == "failed") return "failed";
        if (outcome is "declined" or "cancelled") return "done";
        return AdminRequestJson.Text(request, "status") switch { "pending" => "needs_approval", "completed" => "done", _ => "in_progress" };
    }
    public static IReadOnlyList<string> Actions(JsonNode request)
    {
        var status = AdminRequestJson.Text(request, "status");
        var withdrawable = AdminRequestJson.Text(request, "outcome") == "active" && (status == "pending" ||
            status == "approved" && AdminRequestJson.Items(request, "targets").All(target => AdminRequestJson.Text(target, "status") == "failed"));
        return View(request) switch { "needs_approval" when withdrawable => ["approve", "decline"], "in_progress" when withdrawable => ["cancel"], "failed" => ["retry", "cancel"], _ => [] };
    }
    public static JsonObject RowBody(JsonObject row) => AdminRequestJson.Pick(row, "id", "type", "title", "enabled", "limit", "hide_available", "chart", "discover", "list");
    public static JsonObject WriteBody(string resource, JsonObject draft)
    {
        if (resource == "request-settings")
        {
            var body = AdminRequestJson.Pick(draft, "requests_enabled", "global_max_requests", "global_window_days", "global_auto_approval_enabled", "force_dual_quality", "watchlist_requests");
            if (AdminRequestJson.Number(body, "global_max_requests", -1) < 0 || AdminRequestJson.Number(body, "global_window_days") < 1) throw new ArgumentException("Use a request limit of 0 or more and a window of at least 1 day.");
            return body;
        }
        if (resource == "request-routing")
        {
            if (AdminRequestJson.Text(draft, "mode") is not ("standard" or "advanced")) throw new ArgumentException("Choose Standard or Advanced.");
            return AdminRequestJson.Pick(draft, "mode");
        }
        if (resource == "request-discover/layout")
        {
            var rows = draft["rows"] as JsonArray ?? throw new ArgumentException("Supply the whole layout.");
            if (rows.Count > 30) throw new ArgumentException("Discover holds at most 30 rows.");
            var output = new JsonArray(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in rows)
            {
                var row = RowBody(node as JsonObject ?? throw new ArgumentException("Invalid Discover row."));
                var id = AdminRequestJson.Text(row, "id");
                if (id.Length > 0 && !ids.Add(id)) throw new ArgumentException("Each row may appear only once.");
                if (!RowTypes.Contains(AdminRequestJson.Text(row, "type")) || AdminRequestJson.Text(row, "title").Trim().Length is < 1 or > 80 || AdminRequestJson.Number(row, "limit") is < 0 or > 100)
                    throw new ArgumentException("Review the row's name, kind and size.");
                if (row["discover"] is JsonObject filter && AdminRequestJson.Items(filter, "watch_provider_ids").Count > 0 && AdminRequestJson.Text(filter, "watch_region").Length != 2)
                    throw new ArgumentException("Choose the streaming services' region.");
                output.Add(row);
            }
            return new JsonObject { ["rows"] = output };
        }
        if (resource.StartsWith("request-integrations/", StringComparison.Ordinal))
        {
            var body = AdminRequestJson.Pick(draft, "name", "enabled", "base_url", "api_key_ref", "capability_id", "installation_id", "supported_media_types", "plugin_config");
            if (string.IsNullOrWhiteSpace(AdminRequestJson.Text(body, "name")) || string.IsNullOrWhiteSpace(AdminRequestJson.Text(body, "capability_id")) || string.IsNullOrWhiteSpace(AdminRequestJson.Text(body, "installation_id"))) throw new ArgumentException("Name the server and choose its installed type.");
            if (string.IsNullOrWhiteSpace(AdminRequestJson.Text(body, "api_key_ref"))) body.Remove("api_key_ref");
            var url = AdminRequestJson.Text(body, "base_url").Trim(); if (url.Length > 0 && !url.Contains("://", StringComparison.Ordinal)) url = "http://" + url;
            body["base_url"] = url;
            return body;
        }
        if (resource.StartsWith("request-routes/", StringComparison.Ordinal))
        {
            var body = AdminRequestJson.Pick(draft, "media_type", "name", "enabled", "conditions", "hd", "uhd", "skip_uhd");
            if (AdminRequestJson.Text(body, "name").Length > 100) throw new ArgumentException("Rule names hold at most 100 characters.");
            return body;
        }
        throw new ArgumentException("Unknown Requests editor.");
    }

    public static string Error(Exception error, bool write = false) => error switch
    {
        ApiException { StatusCode: 412 } => "This was changed by another admin. Reload to review the current values before saving again.",
        ApiException { StatusCode: 401 } => "Sign in again, then reload.",
        ApiException { StatusCode: 403 } => "Permission to manage requests is required. Reload after access is restored.",
        ApiException { StatusCode: 409 } => "The request or its routing changed. Reload and review its current state.",
        ApiException { StatusCode: 422 } => "The server rejected one or more values. Review the marked fields before trying again.",
        ApiException { StatusCode: 429 } => "The server asked you to wait. Try again shortly.",
        ApiException { StatusCode: 404 or 501 or 503 } when !write => "Request administration is unavailable on this server. Reload to try again.",
        ArgumentException => error.Message,
        _ when write => "The result was not confirmed. Reload and review the current state before another attempt. This action was not replayed.",
        _ => "Request administration could not be loaded. Check the connection and try again.",
    };
}

public sealed record AdminRequestBulkFailure(string Id, string Title, string Message);
public sealed record AdminRequestBulkProgress(int Total, int Done, IReadOnlyList<AdminRequestBulkFailure> Failures);

/// <summary>Four workers, one attempt per selected row, no implicit retry of failures or unknown outcomes.</summary>
public sealed class AdminRequestsController(AdminRequestsApi api)
{
    public async Task<AdminRequestBulkProgress> BulkAsync(MonitorAuthorityLease owner, IReadOnlyList<JsonObject> rows, string action,
        string? reason, Action<AdminRequestBulkProgress>? progress = null, CancellationToken ct = default)
    {
        if (action is not ("approve" or "decline") || rows.Count > AdminRequestsApi.RetainedQueueLimit ||
            rows.Select(row => AdminRequestJson.Text(row, "id")).Distinct().Count() != rows.Count || rows.Any(row => !AdminRequestRules.Actions(row).Contains(action)))
            throw new ArgumentException("Select pending requests that can still be approved or declined.");
        var gate = new object(); var next = 0; var done = 0; var failures = new List<AdminRequestBulkFailure>();
        async Task Worker()
        {
            while (true)
            {
                JsonObject row;
                lock (gate) { if (next >= rows.Count || ct.IsCancellationRequested || !owner.IsCurrent) return; row = rows[next++]; }
                try { await api.ActionAsync(owner, AdminRequestJson.Text(row, "id"), action, reason, ct).ConfigureAwait(false); }
                catch (Exception error) { lock (gate) failures.Add(new(AdminRequestJson.Text(row, "id"), AdminRequestJson.Text(row, "title"), AdminRequestRules.Error(error, true))); }
                lock (gate) { done++; if (owner.IsCurrent && !ct.IsCancellationRequested) progress?.Invoke(new(rows.Count, done, failures.ToArray())); }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, Math.Min(4, rows.Count)).Select(_ => Worker())).ConfigureAwait(false);
        owner.ThrowIfRetired(); ct.ThrowIfCancellationRequested();
        return new(rows.Count, done, failures.ToArray());
    }
}
