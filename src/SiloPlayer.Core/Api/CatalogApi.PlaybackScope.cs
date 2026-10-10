using System.Globalization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    public async Task<SeasonsResponse> GetSeasonsAsync(string seriesId, int? libraryId, CancellationToken ct = default)
        => new() { Seasons = (await ReadScopedAsync<Season>($"/api/v2/catalog/series/{Uri.EscapeDataString(seriesId)}/seasons", libraryId, ct)).Items };

    public async Task<EpisodesResponse> GetEpisodesAsync(string seriesId, int seasonNumber, int? libraryId, CancellationToken ct = default)
        => new() { Episodes = (await ReadScopedAsync<Episode>($"/api/v2/catalog/series/{Uri.EscapeDataString(seriesId)}/seasons/{seasonNumber}/episodes", libraryId, ct)).Items };

    public async Task<EpisodesResponse> GetItemEpisodesAsync(string seasonContentId, int? libraryId, CancellationToken ct = default)
        => new() { Episodes = (await ReadScopedAsync<Episode>($"/api/v2/catalog/items/{Uri.EscapeDataString(seasonContentId)}/episodes", libraryId, ct)).Items };

    private async Task<BrowseCollection<T>> ReadScopedAsync<T>(string path, int? libraryId, CancellationToken ct)
    {
        var context = client.CaptureContext();
        var result = await client.SendRequestAsync<BrowseCollection<T>>(context, HttpMethod.Get, LibraryScopedPath(path, libraryId), null, ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Catalog scope changed.", ct);
        return result;
    }

    private static string LibraryScopedPath(string path, int? libraryId)
        => libraryId.HasValue ? path + "?library_id=" + libraryId.Value.ToString(CultureInfo.InvariantCulture) : path;
}
