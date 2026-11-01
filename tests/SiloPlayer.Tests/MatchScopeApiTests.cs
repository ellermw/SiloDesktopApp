using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;

namespace SiloPlayer.Tests;

public sealed class MatchScopeApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAndApplySerializeTheSameCapturedLibrary(bool apply)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http); var context = client.CaptureContext(); var api = new MediaMaintenanceApi(client);
        if (apply) await api.ApplyMatchAsync(context, "item/1", new ItemMatchApplyRequest { LibraryId = 7, ProviderIds = new() { ["tmdb"] = "42" } });
        else await api.SearchMatchesAsync(context, "item/1", new ItemMatchSearchRequest { LibraryId = 7 });
        Assert.Equal("7", wire.Body.GetProperty("library_id").GetString()); Assert.Contains("item%2F1/match/", wire.Path);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DepartedProfileCannotDispatchUsingANewProfilesAuthority(bool apply)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http); var context = client.CaptureContext(); client.SetProfile("other"); var api = new MediaMaintenanceApi(client);
        if (apply) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ApplyMatchAsync(context, "item", new()));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SearchMatchesAsync(context, "item", new()));
        Assert.Equal(0, wire.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedSearchAndApplyRejectAResponseAfterProfileDeparture(bool apply)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http); var context = client.CaptureContext(); var api = new MediaMaintenanceApi(client); wire.After = () => client.SetProfile("other");
        if (apply) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ApplyMatchAsync(context, "item", new()));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SearchMatchesAsync(context, "item", new()));
        Assert.Equal(1, wire.Calls);
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://scope-fixture.invalid"); return client; }
    private sealed class Wire : HttpMessageHandler
    {
        public JsonElement Body; public string Path = ""; public int Calls; public Action? After;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Path = request.RequestUri!.AbsolutePath; Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)); After?.Invoke();
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":[],\"content_id\":\"target\"}") };
        }
    }
}
