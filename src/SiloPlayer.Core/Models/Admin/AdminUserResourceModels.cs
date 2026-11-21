using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>A canonical editor, bound to the exact authority and resource path that read its validator.</summary>
public sealed record AdminResourceDocument(JsonElement Value, string? ETag, string Path, long Epoch, string AccountId)
{
    internal AdminMonitorAuthority? Authority { get; init; }
    public bool CanWrite => AdminValidators.IsStrong(ETag);
    public void RequireOwner(MonitorAuthorityLease lease, string path)
    {
        lease.ThrowIfRetired();
        if (!ReferenceEquals(Authority, lease.Owner) || lease.Epoch != Epoch || lease.AccountId != AccountId || Path != path) throw new MonitorAuthorityRetiredException();
        AdminValidators.RequireStrong(ETag);
    }
    public override string ToString() => nameof(AdminResourceDocument);
}

public sealed record AdminResourcePage(IReadOnlyList<JsonElement> Items, string? NextCursor, int? Revision = null);
public sealed record AdminDeviceValues(IReadOnlyList<JsonElement> Items, int Revision);
public sealed record AdminImportReceipt(JsonElement Value, string Location, TimeSpan RetryAfter);
public sealed record AdminDeviceResetResult(int Completed, bool OutcomeUnknown, int? FailureStatus = null);

/// <summary>Safe scalar access for the bounded wire projections. Objects and arrays never become display strings.</summary>
public static class AdminResourceJson
{
    public static string Text(this JsonElement value, string key, string fallback = "") =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.String ? child.GetString() ?? fallback : fallback;
    public static bool Flag(this JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.True;
    public static int Number(this JsonElement value, string key, int fallback = 0) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) && child.TryGetInt32Safe(out var result) ? result : fallback;
    private static bool TryGetInt32Safe(this JsonElement value, out int result)
    {
        result = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
    }
    public static IReadOnlyList<JsonElement> Rows(this JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Array ? child.EnumerateArray().ToArray() : [];
    public static JsonElement? Member(this JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) ? child : null;
}
