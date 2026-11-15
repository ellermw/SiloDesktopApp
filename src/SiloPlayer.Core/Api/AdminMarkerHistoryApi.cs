using System.Globalization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Admin › Playback › Skip Markers: the read-only marker EDIT HISTORY
/// (official Silo e61187daaf72e8accad486eb2f632c753ae211d5:
/// <c>api/v2/markers.ts</c> <c>getAllMarkerHistory</c>,
/// <c>internal/apiv2/admin_catalog_marker_history.go</c>). One bounded read;
/// no marker writes, redetection, provider or contribution calls live here.
/// </summary>
public sealed class AdminMarkerHistoryApi(SiloApiClient client)
{
    /// <summary>Server cap (<c>limit maximum=100</c>).</summary>
    public const int MaxLimit = 100;

    /// <summary>The page's default count (<c>useState("50")</c>).</summary>
    public const int DefaultLimit = 50;

    /// <summary>The count selector (<c>LIMIT_OPTIONS</c>).</summary>
    public static IReadOnlyList<int> LimitOptions { get; } = [25, 50, 100];

    public static string HistoryPath(int limit) =>
        "/api/v2/admin/markers/history?limit=" + AdminPaging.ClampLimit(limit, MaxLimit).ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads the newest <paramref name="limit"/> edits under <paramref name="lease"/>.</summary>
    public async Task<IReadOnlyList<AdminMarkerEditRow>> ReadAsync(MonitorAuthorityLease lease, int limit = DefaultLimit, CancellationToken ct = default)
    {
        limit = AdminPaging.ClampLimit(limit, MaxLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        lease.ThrowIfRetired();
        var body = await client.SendRequestAsync<AdminMarkerHistoryWire?>(lease.Context, HttpMethod.Get, HistoryPath(limit), null, linked.Token)
            .ConfigureAwait(false);
        lease.ThrowIfRetired();
        linked.Token.ThrowIfCancellationRequested();
        return Validate(body, limit);
    }

    /// <summary>
    /// The history array must exist (the contract says never null), may not
    /// exceed the requested count, and each edit needs its id, file and time.
    /// </summary>
    public static IReadOnlyList<AdminMarkerEditRow> Validate(AdminMarkerHistoryWire? body, int limit)
    {
        if (body?.History is not { } history || history.Count > limit) throw new InvalidDataException("Invalid marker history.");
        var rows = new AdminMarkerEditRow[history.Count];
        for (var index = 0; index < history.Count; index++)
        {
            var edit = history[index];
            if (edit == null || string.IsNullOrEmpty(edit.Id) || string.IsNullOrEmpty(edit.MediaFileId) || edit.CreatedAt == null)
                throw new InvalidDataException("Invalid marker history.");
            rows[index] = new AdminMarkerEditRow(edit.Id, edit.MediaFileId, edit.ItemId, edit.ItemType, edit.MediaTitle, edit.FilePath,
                edit.Segment ?? "", edit.Action ?? "", Segment(edit.Before), Segment(edit.After), edit.UserId, edit.Username,
                edit.ImpersonatorUserId, edit.ImpersonatorUsername, edit.ApiKeyId, edit.RequestId, edit.ClientIp, edit.UserAgent, edit.CreatedAt);
        }
        return Array.AsReadOnly(rows);
    }

    private static AdminMarkerSegment? Segment(AdminMarkerSegmentWire? wire) =>
        wire == null ? null : new AdminMarkerSegment(wire.StartSeconds, wire.EndSeconds, wire.Source, wire.Provider, wire.Confidence, wire.Algorithm, wire.DetectedAt);
}
