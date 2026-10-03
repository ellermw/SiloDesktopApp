using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

public static class RequestDiscoveryPaging
{
    // Restricted viewers may skip provider pages with no accessible results.
    public static int? Next(RequestDiscoverySection response, int current)
        => response.NextPage is int next && next > current && next <= Math.Min(500, response.TotalPages)
            ? next : null;
}
