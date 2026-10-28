using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

public static class RequestDiscoveryPaging
{
    // An absent cursor uses ordinary numbered pages. An explicit cursor can
    // skip filtered pages or point beyond the end; never replace it with +1.
    public static int? Next(RequestDiscoverySection response, int current)
    {
        var pages = Math.Min(500, response.TotalPages);
        if (response.NextPage is int next) return next > current && next <= pages ? next : null;
        return current < pages ? current + 1 : null;
    }
}
