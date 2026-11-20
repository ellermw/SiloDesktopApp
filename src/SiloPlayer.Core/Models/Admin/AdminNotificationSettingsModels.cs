namespace SiloPlayer.Core.Models.Admin;

public sealed record AdminNotificationTestResult(bool Ok, long DurationMs, int? HttpStatus, string? Message)
{
    public override string ToString() => nameof(AdminNotificationTestResult);
}
public sealed record AdminNotificationRelayRegistration(string DeploymentId, string? KeyPrefix, string? ExpiresAt, string? RelayRequestId);
public sealed record AdminNotificationChannel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string UrlHost { get; init; }
    public bool Enabled { get; init; }
    public IReadOnlyDictionary<string, bool> Events { get; init; } = new Dictionary<string, bool>();
    public int ConsecutiveFailures { get; init; }
    public string? DisabledReason { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public int? LastFailureStatus { get; init; }
    public string? LastFailureMessage { get; init; }
    public bool Failing => LastFailureAt != null && (LastSuccessAt == null || LastFailureAt > LastSuccessAt);
    public override string ToString() => nameof(AdminNotificationChannel);
}
public sealed record AdminNotificationChannelPage(IReadOnlyList<AdminNotificationChannel> Items, string? NextCursor);
/// <summary>The one-time signing secret is never part of a retained channel row or diagnostic text.</summary>
public sealed record AdminNotificationChannelReceipt(string Id, string? SigningSecret)
{
    public override string ToString() => nameof(AdminNotificationChannelReceipt);
}
