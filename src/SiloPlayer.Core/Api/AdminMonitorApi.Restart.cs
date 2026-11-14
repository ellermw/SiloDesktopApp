using SiloPlayer.Core.Services;
namespace SiloPlayer.Core.Api;

public sealed record AdminMonitorRestartResult(string Status, string Message, long NotifiedSessions);

public partial class AdminMonitorApi
{
    /// <summary>One confirmed restart intent, with no authentication replay or recaptured authority.</summary>
    public async Task<AdminMonitorRestartResult> RequestMonitorServerRestartAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        var result = await client.SendRequestWithoutRefreshAsync<AdminMonitorRestartResult>(
            lease.Context, HttpMethod.Post, "/api/v2/admin/server/restart", new { }, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (result.Status is not ("restart_requested" or "already_requested"))
            throw new InvalidDataException("Unsupported restart result.");
        return result;
    }
}
