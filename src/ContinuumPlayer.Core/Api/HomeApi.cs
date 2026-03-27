using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class HomeApi(ContinuumApiClient client)
{
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>("/api/v1/home/layout", ct);

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v1/home/sections", ct);
}
