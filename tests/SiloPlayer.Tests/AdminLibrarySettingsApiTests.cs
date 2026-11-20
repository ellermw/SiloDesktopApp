using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminLibrarySettingsApiTests
{
    [Fact]
    public async Task ReadsExactCapabilityAndRatingSourceContracts()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("capabilities", StringComparison.Ordinal)
            ? AdminMonitorKit.Json("""{"revision":"r1","state":"available","allowed":true,"plugin_declared_sources":true}""")
            : AdminMonitorKit.Json("""{"items":[{"source":"imdb","label":"IMDb","name":"IMDb","always_shown":true},{"source":"rt_critic","label":"Rotten Tomatoes critics","name":"RT","always_shown":false,"provider":"MDBList"}]}"""));
        var api = new AdminLibrarySettingsApi(kit.Client);

        Assert.True(await api.SupportsPluginRatingSourcesAsync(lease));
        var sources = await api.ListRatingSourcesAsync(lease);

        Assert.Equal(["GET /api/v2/admin/rating-sources/capabilities", "GET /api/v2/admin/rating-sources"], kit.Http.Requests);
        Assert.Equal(["imdb", "rt_critic"], sources.Select(source => source.Source));
        Assert.True(sources[0].AlwaysShown);
        Assert.Equal("MDBList", sources[1].Provider);
    }

    [Fact]
    public async Task RejectsMalformedOrUnboundedRatingSourceCollections()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminLibrarySettingsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"source":"","label":"x","name":"x","always_shown":false}]}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListRatingSourcesAsync(lease));
        var items = string.Join(',', Enumerable.Range(0, AdminLibrarySettingsApi.MaxRatingSources + 1)
            .Select(index => $$"""{"source":"s{{index}}","label":"L","name":"N","always_shown":false}"""));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{" + "\"items\":[" + items + "]}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListRatingSourcesAsync(lease));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task CapabilityFailuresAreSanitizedAndNeverRetried(HttpStatusCode status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"detail":"private fixture detail"}""", status));
        var error = await Assert.ThrowsAsync<ApiException>(() => new AdminLibrarySettingsApi(kit.Client).SupportsPluginRatingSourcesAsync(lease));
        Assert.DoesNotContain("private fixture detail", error.ToString());
        Assert.Single(kit.Http.Requests);
    }
}
