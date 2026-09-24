using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.HistoryImport;

namespace SiloPlayer.Core.Services;

public static class HistoryImportStart
{
    public static async Task<HistoryImportRun> CreateAsync(HistoryImportApi api,
        CreateHistoryImportRunRequest request, Action<string> consumeConnectSession, CancellationToken ct = default)
    {
        // Run creation consumes a Connect authorization on the server. Preserve
        // credentials on failure, and identify the submitted session even if the
        // user has already signed in again while this request was in flight.
        var session = request.Source == "emby" ? request.ConnectSessionId : null;
        var run = await api.CreateImportRunAsync(request, ct);
        if (!string.IsNullOrEmpty(session)) consumeConnectSession(session);
        return run;
    }
}
