namespace SiloPlayer.Core.Models.Admin;

/// <summary>The Meilisearch half of the search status (official <c>AdminCatalogSearchMeiliStatus</c>).</summary>
public sealed record AdminSearchMeilisearchStatus(
    bool Configured,
    bool Healthy,
    string CircuitState,
    string? LastFallback,
    bool SemanticEnabled,
    bool BinaryQuantized,
    double SemanticRatio,
    string Embedder,
    IReadOnlyList<string> IndexTypes)
{
    public override string ToString() => nameof(AdminSearchMeilisearchStatus);
}

/// <summary>The index state (official <c>AdminCatalogSearchIndexStateStatus</c>).</summary>
public sealed record AdminSearchIndexStatus(
    string ActiveIndexUid,
    int SchemaVersion,
    int ExpectedSchemaVersion,
    bool RebuildRequired,
    long DocumentCount,
    long VectorDocumentCount,
    long PendingEvents,
    long DeadLetteredEvents,
    DateTimeOffset? LastSyncAt)
{
    public override string ToString() => nameof(AdminSearchIndexStatus);
}

public sealed record AdminSearchTypeCoverage(string Type, long Eligible, long Vectorized, double CoverageRatio, bool Ready)
{
    public override string ToString() => nameof(AdminSearchTypeCoverage);
}

public sealed record AdminSearchSemanticCapability(bool Ok, string? Reason, string Embedder)
{
    public override string ToString() => nameof(AdminSearchSemanticCapability);
}

public sealed record AdminSearchSemanticStatus(
    bool Ready,
    string? DisabledReason,
    double CoverageRatio,
    DateTimeOffset? CoverageUpdatedAt,
    IReadOnlyList<AdminSearchTypeCoverage> PerType,
    AdminSearchSemanticCapability Capability)
{
    public override string ToString() => nameof(AdminSearchSemanticStatus);
}

/// <summary>
/// GET /api/v2/admin/catalog/search/status. Diagnostics only: nothing here is a setting, and the
/// server's own task links are not used (the page names the two maintenance tasks itself).
/// </summary>
public sealed record AdminSearchStatus(
    string ConfiguredProvider,
    string ActiveProvider,
    bool Degraded,
    string? DegradedReason,
    AdminSearchMeilisearchStatus Meilisearch,
    AdminSearchIndexStatus Index,
    AdminSearchSemanticStatus? Semantic)
{
    public override string ToString() => nameof(AdminSearchStatus);
}
