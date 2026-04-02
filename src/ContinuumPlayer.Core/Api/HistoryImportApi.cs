using ContinuumPlayer.Core.Models.HistoryImport;

namespace ContinuumPlayer.Core.Api;

public class HistoryImportApi(ContinuumApiClient client)
{
    public Task<List<HistoryImportSource>> GetImportSourcesAsync(CancellationToken ct = default)
        => client.GetAsync<List<HistoryImportSource>>("/api/v1/history-imports/sources", ct);

    public Task<EmbyConnectLoginResponse> EmbyConnectLoginAsync(EmbyConnectLoginRequest request, CancellationToken ct = default)
        => client.PostAsync<EmbyConnectLoginResponse>("/api/v1/history-imports/emby-connect/login", request, ct);

    public Task<PlexPinResponse> PlexAuthPinAsync(CancellationToken ct = default)
        => client.PostAsync<PlexPinResponse>("/api/v1/history-imports/plex/auth/pin", new { }, ct);

    public Task<PlexCheckResponse> PlexAuthCheckAsync(PlexCheckRequest request, CancellationToken ct = default)
        => client.PostAsync<PlexCheckResponse>("/api/v1/history-imports/plex/auth/check", request, ct);

    public Task<HistoryImportRunsResponse> GetImportRunsAsync(CancellationToken ct = default)
        => client.GetAsync<HistoryImportRunsResponse>("/api/v1/history-imports/runs", ct);

    public Task<HistoryImportRun> CreateImportRunAsync(CreateHistoryImportRunRequest request, CancellationToken ct = default)
        => client.PostAsync<HistoryImportRun>("/api/v1/history-imports/runs", request, ct);

    public Task<HistoryImportRun> GetImportRunAsync(string id, CancellationToken ct = default)
        => client.GetAsync<HistoryImportRun>($"/api/v1/history-imports/runs/{Uri.EscapeDataString(id)}", ct);
}
