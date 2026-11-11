using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ProfilesMetadataParityTests
{
    [Theory]
    [InlineData(1, null, true, true)]
    [InlineData(1, 1, false, false)]
    [InlineData(2, 2, false, true)]
    [InlineData(3, 2, false, true)]
    [InlineData(1, 2, true, true)]
    public void LimitsPreserveMissingLimitAndMultipleProfileSwitch(int count, int? max, bool add, bool change)
    {
        Assert.Equal(add, ProfileLimits.CanAdd(count, max));
        Assert.Equal(change, ProfileLimits.CanSwitch(count, max));
        Assert.True(ProfileLimits.CanSwitch(count, max, hasPin: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryAndFirstProfileAlwaysSerializeFalseEmptyEvenWithStaleDraft(bool update)
    {
        using var wire = new Wire(); var api = new AuthApi(Client(wire));
        var draft = new CreateProfileRequest { IsPrimary = true, Name = "Entered", IsChild = true,
            LibraryRestrictionsEnabled = true, AllowedLibraryIds = [4, 7], Pin = "1234" };
        if (update) await api.UpdateProfileAsync("primary", draft); else await api.CreateProfileAsync(draft);
        Assert.False(wire.Body.GetProperty("library_restrictions_enabled").GetBoolean());
        Assert.Empty(wire.Body.GetProperty("allowed_library_ids").EnumerateArray());
        Assert.Equal("Entered", wire.Body.GetProperty("name").GetString());
        Assert.Equal("1234", wire.Body.GetProperty("pin").GetString());
        Assert.True(wire.Body.GetProperty("is_child").GetBoolean());
        draft.IsPrimary = false;
        await api.CreateProfileAsync(draft);
        Assert.True(wire.Body.GetProperty("library_restrictions_enabled").GetBoolean());
        Assert.Equal(new[] { "4", "7" }, wire.Body.GetProperty("allowed_library_ids").EnumerateArray().Select(v => v.GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ListReadsOptionalLimit(int? max)
    {
        using var wire = new Wire { Json = max.HasValue ? $"{{\"items\":[],\"max_profiles\":{max}}}" : "{\"items\":[]}" };
        Assert.Equal(max, (await new AuthApi(Client(wire)).GetProfilesAsync()).MaxProfiles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    public async Task HintsUseExactOptionalLibraryAndCapturedOwner(int? library)
    {
        using var wire = new Wire { Json = "{\"title\":\"Folder title\",\"year\":1999,\"provider_ids\":{\"imdb\":\"tt42\"}}" };
        var client = Client(wire); var api = new MediaMaintenanceApi(client); var owner = client.CaptureContext();
        var hints = await api.GetMatchHintsAsync(owner, "item/1", library);
        Assert.Equal("Folder title", hints.Title); Assert.Equal(1999, hints.Year); Assert.Equal("tt42", hints.ProviderIds?["imdb"]);
        Assert.Equal("/api/v2/admin/items/item%2F1/match/hints" + (library.HasValue ? "?library_id=7" : ""), wire.Path);
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetMatchHintsAsync(owner, "item", library));
        Assert.Equal(1, wire.Calls);
    }

    [Fact]
    public async Task HintResponseFromDepartedOwnerIsRejected()
    {
        using var wire = new Wire(); var client = Client(wire); var owner = client.CaptureContext();
        wire.After = () => client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMaintenanceApi(client).GetMatchHintsAsync(owner, "item", null));
    }

    private static SiloApiClient Client(Wire wire) { var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://profiles-metadata.fixture.invalid"); return client; }
    private sealed class Wire : HttpMessageHandler
    {
        internal string Json = "{}", Path = ""; internal JsonElement Body; internal int Calls; internal Action? After;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Path = request.RequestUri!.PathAndQuery;
            if (request.Content != null) Body = JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsStringAsync(ct));
            After?.Invoke(); return new(HttpStatusCode.OK) { Content = new StringContent(Json) };
        }
    }
}
