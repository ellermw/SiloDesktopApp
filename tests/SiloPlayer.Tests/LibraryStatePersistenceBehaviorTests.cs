using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LibraryStatePersistenceBehaviorTests
{
    [Fact]
    public async Task LibraryStateUsesVersionedSearchDocumentAndPreservesOtherLibraries()
    {
        using var wire = new Wire(); var client = wire.Client(); var store = new LibraryPageStateStore(new SettingsApi(client));
        Assert.Equal("?tab=collections", await store.ReadAsync(2));
        await store.WriteAsync(1, "?tab=library&groups%5B0%5D%5Brules%5D%5B0%5D%5Bfield%5D=year");
        Assert.NotNull(wire.Saved);
        Assert.Contains("scope=profile_device", wire.WritePath);
        var value = wire.Saved!.Value.GetProperty("value");
        Assert.Equal(1, value.GetProperty("version").GetInt32());
        Assert.Equal("?tab=collections", value.GetProperty("libraries").GetProperty("2").GetProperty("search").GetString());
        Assert.Contains("groups", value.GetProperty("libraries").GetProperty("1").GetProperty("search").GetString());
    }

    [Fact]
    public async Task DisabledRememberPreferenceDoesNotRestoreOrWriteState()
    {
        using var wire = new Wire { Remember = false }; var store = new LibraryPageStateStore(new SettingsApi(wire.Client()));
        Assert.Null(await store.ReadAsync(2)); await store.WriteAsync(1, "?tab=library"); Assert.Null(wire.Saved);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool Remember { get; set; } = true;
        public JsonElement? Saved { get; private set; }
        public string? WritePath { get; private set; }
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://library-state.invalid"); return client; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Put)
            {
                WritePath = request.RequestUri!.PathAndQuery; Saved = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
                return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = new object[] { new { key = "ui.remember_library_page_state", value = (object)Remember }, new { key = "ui.library_page_state", value = (object)new { version = 1, libraries = new Dictionary<string, object> { ["2"] = new { search = "?tab=collections" } } } } } })) };
        }
    }
}
