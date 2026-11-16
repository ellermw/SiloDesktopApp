using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Configure &gt; Server &gt; Search wire operations (official <c>internal/apiv2/admin_catalog_search.go</c>,
/// <c>admin_settings_checks.go</c>), kept separate from the shared settings API. Every
/// call is bound to a captured admin lease, has a finite deadline and a response bound, and is sent once: there is
/// no authentication replay and no automatic retry of a command.
/// </summary>
public sealed class AdminSearchApi(SiloApiClient client)
{
    public const int MaxResponseBytes = 1024 * 1024;
    /// <summary>Indexed entity kinds and per-type coverage rows are a handful; more is treated as an invalid response.</summary>
    public const int MaxListed = 64;
    private const int MaxTextLength = 2000;
    private static readonly TimeSpan StatusDeadline = TimeSpan.FromSeconds(20);
    // The server probes Meilisearch (health, then an index/key check) before answering.
    private static readonly TimeSpan CheckDeadline = TimeSpan.FromSeconds(30);

    public async Task<AdminSearchStatus> GetStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, StatusDeadline);
        StatusWire body;
        try { body = (await client.MonitorLogsSendAsync<StatusWire>(lease, HttpMethod.Get, "/api/v2/admin/catalog/search/status", ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw Sanitize(error); }
        return Validate(body);
    }

    /// <summary>
    /// One synchronous Meilisearch check. <paramref name="values"/> carries the form's value for every Meilisearch key and
    /// <paramref name="dirtyKeys"/> says which of them are staged edits (the server uses its saved value for the rest, including
    /// the API key). A typed API key and the URL's credentials are removed from the returned message.
    /// </summary>
    public async Task<AdminSettingsConnectionResult> CheckMeilisearchAsync(MonitorAuthorityLease lease, IReadOnlyDictionary<string, string> values,
        IReadOnlyCollection<string> dirtyKeys, CancellationToken ct = default)
    {
        var allowed = AdminSearchSettingsRules.MeilisearchKeys;
        if (values.Keys.Any(key => !allowed.Contains(key)) || dirtyKeys.Any(key => !values.ContainsKey(key)))
            throw new ArgumentException("Invalid Meilisearch connection check values.", nameof(values));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CheckDeadline);
        ApiResponse<CheckWire> response;
        try
        {
            response = await client.MonitorLogsSendAsync<CheckWire>(lease, HttpMethod.Post, "/api/v2/admin/settings/check/meilisearch",
                new Dictionary<string, object?> { ["values"] = values, ["dirty_keys"] = dirtyKeys }, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new InvalidDataException("Connection check failed."); }
        if (response.Body.Success == null || response.Body.Message == null) throw new InvalidDataException("Invalid connection result.");
        var message = LocalLog.RedactDiagnosticText(response.Body.Message);
        var secrets = new List<string>();
        if (values.TryGetValue(AdminSearchSettingsRules.MeilisearchApiKey, out var key) && key.Length > 0) secrets.Add(key);
        if (values.TryGetValue(AdminSearchSettingsRules.MeilisearchUrl, out var url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            // Servers may echo credentials separately, either escaped or decoded.
            secrets.Add(uri.UserInfo);
            secrets.AddRange(uri.UserInfo.Split(':', 2));
            secrets.AddRange(secrets.ToArray().Select(Uri.UnescapeDataString));
        }
        foreach (var component in secrets.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
            message = message.Replace(component, "<redacted>", StringComparison.Ordinal);
        if (message.Length > MaxTextLength) message = message[..MaxTextLength] + "…";
        return new(response.Body.Success.Value, message);
    }

    private static AdminSearchStatus Validate(StatusWire body)
    {
        if (body.Meilisearch is not { } meili || body.Index is not { } index || body.ConfiguredProvider == null || body.ActiveProvider == null)
            throw new InvalidDataException("Invalid search status.");
        var indexTypes = meili.IndexTypes ?? [];
        if (indexTypes.Length > MaxListed) throw new InvalidDataException("Invalid search status.");
        AdminSearchSemanticStatus? semantic = null;
        if (body.Semantic is { } wire)
        {
            var perType = wire.PerType ?? [];
            if (perType.Length > MaxListed || perType.Any(entry => entry == null || string.IsNullOrEmpty(entry.Type))) throw new InvalidDataException("Invalid search status.");
            semantic = new(wire.Ready == true, SafeDiagnostic(wire.DisabledReason), wire.VectorCoverageRatio,
                wire.CoverageUpdatedAt, Array.AsReadOnly(perType.Select(entry => new AdminSearchTypeCoverage(Bound(entry!.Type)!, entry.Eligible, entry.Vectorized, entry.VectorCoverageRatio, entry.Ready)).ToArray()),
                new(wire.Capability?.Ok == true, SafeDiagnostic(wire.Capability?.Reason), Bound(wire.Capability?.Embedder) ?? ""));
        }
        return new(Bound(body.ConfiguredProvider)!, Bound(body.ActiveProvider)!, body.Degraded, SafeDiagnostic(body.DegradedReason),
            new(meili.Configured, meili.Healthy, Bound(meili.CircuitState) ?? "", SafeDiagnostic(meili.LastFallback), meili.SemanticEnabled, meili.BinaryQuantized,
                meili.SemanticRatio, Bound(meili.Embedder) ?? "", Array.AsReadOnly(indexTypes.Where(value => !string.IsNullOrEmpty(value)).Select(value => Bound(value)!).ToArray())),
            new(Bound(index.ActiveIndexUid) ?? "", index.SchemaVersion, index.ExpectedSchemaVersion, index.RebuildRequired, index.DocumentCount,
                index.VectorDocumentCount, index.PendingEvents, index.DeadLetteredEvents, index.LastSyncAt), semantic);
    }

    private static string? SafeDiagnostic(string? value) => value switch
    {
        null or "" => null,
        "timeout" or "semantic search disabled" or "search index rebuild required"
            or "Meilisearch index has not been built; using Postgres search"
            or "Search index rebuild required; using Meilisearch keyword search"
            or "A newer server version owns the Meilisearch index; using Postgres search on this node"
            or "Meilisearch index schema mismatch; using Postgres search"
            or "Meilisearch index state is unavailable; using Postgres search"
            or "Meilisearch is unavailable; using Postgres search" => value,
        _ => "Additional provider diagnostics are withheld.",
    };

    /// <summary>Bounded display text: an over-long metadata string ends with a visible ellipsis.</summary>
    private static string? Bound(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= MaxTextLength ? value : value[..MaxTextLength] + "…";
    }

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }

    private static string SafeCode(string? code) => code is "permission_denied" or "profile_unverified" or "profile_verification_required"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "not_found" or "capability_disabled"
        or "capability_not_configured" or "capability_unsupported" or "rate_limited" or "authentication_required"
        ? code : "unknown";
    // Problem detail is server text; the page shows its own message instead.
    private static ApiException Sanitize(ApiException error) => new(SafeCode(error.ErrorCode), "Search request failed.", error.StatusCode)
    {
        ProblemType = SafeCode(error.ProblemType) is var code && code != "unknown" ? code : null
    };

    private sealed record StatusWire
    {
        public string? ConfiguredProvider { get; init; }
        public string? ActiveProvider { get; init; }
        public bool Degraded { get; init; }
        public string? DegradedReason { get; init; }
        public MeiliWire? Meilisearch { get; init; }
        public IndexWire? Index { get; init; }
        public SemanticWire? Semantic { get; init; }
    }
    private sealed record MeiliWire
    {
        public bool Configured { get; init; }
        public bool Healthy { get; init; }
        public string? CircuitState { get; init; }
        public string? LastFallback { get; init; }
        public string[]? IndexTypes { get; init; }
        public bool SemanticEnabled { get; init; }
        public bool BinaryQuantized { get; init; }
        public double SemanticRatio { get; init; }
        public string? Embedder { get; init; }
    }
    private sealed record IndexWire
    {
        public string? ActiveIndexUid { get; init; }
        public int SchemaVersion { get; init; }
        public int ExpectedSchemaVersion { get; init; }
        public bool RebuildRequired { get; init; }
        public long DocumentCount { get; init; }
        public long VectorDocumentCount { get; init; }
        public long PendingEvents { get; init; }
        public long DeadLetteredEvents { get; init; }
        public DateTimeOffset? LastSyncAt { get; init; }
    }
    private sealed record SemanticWire
    {
        public bool? Ready { get; init; }
        public string? DisabledReason { get; init; }
        public double VectorCoverageRatio { get; init; }
        public DateTimeOffset? CoverageUpdatedAt { get; init; }
        public CoverageWire?[]? PerType { get; init; }
        public CapabilityWire? Capability { get; init; }
    }
    private sealed record CoverageWire { public string? Type { get; init; } public long Eligible { get; init; } public long Vectorized { get; init; } public double VectorCoverageRatio { get; init; } public bool Ready { get; init; } }
    private sealed record CapabilityWire { public bool? Ok { get; init; } public string? Reason { get; init; } public string? Embedder { get; init; } }
    private sealed record CheckWire { public bool? Success { get; init; } public string? Message { get; init; } }
}
