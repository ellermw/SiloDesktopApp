using SiloPlayer.Core.Models.HistoryImport;

namespace SiloPlayer.Core.Api;

public class HistoryImportApi(SiloApiClient client)
{
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public Task<List<HistoryImportSource>> GetImportSourcesAsync(CancellationToken ct = default)
        => client.GetAllItemsAsync<HistoryImportSource>("/api/v2/history-imports/sources", ct);

    public Task<EmbyConnectLoginResponse> EmbyConnectLoginAsync(EmbyConnectLoginRequest request, CancellationToken ct = default)
        => client.PostAsync<EmbyConnectLoginResponse>("/api/v2/history-imports/emby-connect/login", request, ct);

    public Task<PlexPinResponse> PlexAuthPinAsync(CancellationToken ct = default)
        => client.SendRequestAsync<PlexPinResponse>(HttpMethod.Post, "/api/v2/history-imports/plex/auth/pin", null, null, ct);

    public Task<PlexCheckResponse> PlexAuthCheckAsync(PlexCheckRequest request, CancellationToken ct = default)
        => client.PostAsync<PlexCheckResponse>("/api/v2/history-imports/plex/auth/check", request, ct);

    public async Task<List<HistoryImportRun>> GetImportRunsAsync(int limit = 20, CancellationToken ct = default)
        => (await client.GetAsync<ApiCollectionPage<HistoryImportRun>>($"/api/v2/history-imports/runs?limit={Math.Clamp(limit, 1, 200)}", ct)).Items;

    public Task<HistoryImportRun> CreateImportRunAsync(CreateHistoryImportRunRequest request, CancellationToken ct = default)
        => client.PostAsync<HistoryImportRun>("/api/v2/history-imports/runs", V2Json.Body(request), ct);

    public Task<HistoryImportRun> GetImportRunAsync(string id, CancellationToken ct = default)
        => client.GetAsync<HistoryImportRun>($"/api/v2/history-imports/runs/{Uri.EscapeDataString(id)}", ct);
}
