using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class Oct24HomeLayoutTransferTests
{
    [Fact]
    public async Task ExportDefaultPagesThenSameServerImportResetsChangedDestinationOverrides()
    {
        using var wire = new Wire(); var client = wire.Client(); var service = Service(client);
        var exported = await service.ExportAsync();
        Assert.Equal(2, exported.Pages.Count); Assert.All(exported.Pages, page => Assert.Empty(page.Overrides));
        wire.CustomRows = true;
        var plan = await service.PreviewAsync(HomeLayoutTransfer.Serialize(exported));
        Assert.Equal(2, plan.Pages.Count); Assert.Empty(plan.Skipped);
        await service.ApplyAsync(plan);
        Assert.Equal(2, service.AppliedPages); Assert.Equal(2, wire.Saved.Count);
        Assert.All(wire.Saved, saved => Assert.Equal(0, saved.GetProperty("overrides").GetArrayLength()));
    }

    [Fact]
    public async Task CrossServerEmptyPagesNeverResetTargetRows()
    {
        using var wire = new Wire(); var client = wire.Client(); var service = Service(client);
        var exported = await service.ExportAsync(); exported.ServerId = "other"; wire.CustomRows = true;
        var plan = await service.PreviewAsync(HomeLayoutTransfer.Serialize(exported));
        Assert.Empty(plan.Pages); await service.ApplyAsync(plan);
        Assert.Equal(0, service.AppliedPages); Assert.Empty(wire.Saved);
    }

    [Fact]
    public async Task RetiredPreviewCannotResetAnotherProfile()
    {
        using var wire = new Wire(); var client = wire.Client(); var service = Service(client);
        var exported = await service.ExportAsync();
        var plan = await service.PreviewAsync(HomeLayoutTransfer.Serialize(exported));
        client.SetProfile("other");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan));
        Assert.Empty(wire.Saved);
    }

    [Fact]
    public async Task FailedPageSaveReportsOnlyCompletedPagesAndStops()
    {
        using var wire = new Wire(); var client = wire.Client(); var service = Service(client);
        var exported = await service.ExportAsync();
        var plan = await service.PreviewAsync(HomeLayoutTransfer.Serialize(exported)); wire.FailSecondSave = true;
        await Assert.ThrowsAnyAsync<Exception>(() => service.ApplyAsync(plan));
        Assert.Equal(1, service.AppliedPages); Assert.Single(wire.Saved);
    }

    private static HomeLayoutTransferService Service(SiloApiClient client) => new(new SettingsApi(client), new CatalogApi(client), new CollectionsApi(client), new AuthApi(client));
    private sealed class Wire : HttpMessageHandler
    {
        public bool CustomRows, FailSecondSave; public List<JsonElement> Saved = [];
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://layout-oct24.fixture.invalid"); client.SetProfile("fixture"); return client; }
        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/system/identity") return Ok("{\"server_id\":\"same\"}");
            if (path == "/api/v2/user/libraries") return Ok("{\"items\":[{\"id\":1,\"name\":\"Movies\",\"type\":\"movie\"}]}");
            if (path == "/api/v2/profile/sections")
            {
                if (request.Method == HttpMethod.Get) return Ok(CustomRows ? "{\"items\":[{\"id\":\"custom\",\"is_user_added\":true,\"user_section_type\":\"recently_added\"}]}" : "{\"items\":[]}");
                if (FailSecondSave && Saved.Count == 1) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"unavailable\"}") };
                Saved.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone()); return new(HttpStatusCode.NoContent);
            }
            return Ok("{\"items\":[],\"settings\":[],\"categories\":[]}");
        }
    }
}
