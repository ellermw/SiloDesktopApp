using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Tests;

public sealed class ProfileSectionsTests
{
    [Fact]
    public async Task StructuredOverrideFieldsArePreservedForRoundTrip()
    {
        var handler = new CaptureHandler("""
        {"items":[{"id":"custom-1","section_id":"","position":2,"hidden":false,"removed":false,"section_type":"","title":"","featured":true,"item_limit":12,"is_user_added":true,"user_section_type":"recipe","user_config":{"genre":"Drama"},"user_title":"Drama night"}]}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var response = await new SettingsApi(client).GetProfileSectionsAsync();
        var raw = Assert.Single(response.Overrides);

        Assert.Equal("custom-1", raw.Id);
        Assert.True(raw.IsUserAdded);
        Assert.Equal("recipe", raw.UserSectionType);
        Assert.Contains("Drama", raw.UserConfig);
    }

    [Fact]
    public async Task SaveWireIncludesUserRecipeFieldsInSnakeCase()
    {
        var handler = new CaptureHandler("{}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new SettingsApi(client).UpdateProfileSectionsAsync(new SaveOverridesRequest
        {
            Scope = "home",
            Overrides =
            [
                new SectionOverride
                {
                    Id = "custom-1",
                    IsUserAdded = true,
                    UserSectionType = "recipe",
                    UserTitle = "Drama night",
                    UserConfig = new Dictionary<string, object> { ["genre"] = "Drama" }
                }
            ]
        });

        using var body = JsonDocument.Parse(handler.LastBody!);
        var item = body.RootElement.GetProperty("overrides")[0];
        Assert.True(item.GetProperty("is_user_added").GetBoolean());
        Assert.Equal("recipe", item.GetProperty("user_section_type").GetString());
        Assert.Equal("Drama", item.GetProperty("user_config").GetProperty("genre").GetString());
    }

    [Fact]
    public async Task RecipeCatalogUsesCurrentGalleryContract()
    {
        var handler = new CaptureHandler("""
        {"categories":[{"category":"discovery","recipes":[{"type":"genre","category":"discovery","avoid_duplicates":true,"supports_rotation":false,"admin_only":false,"presets":[{"key":"drama","display_name":"Drama night","icon":"film","description_short":"Popular drama","default_params":{"genre":"Drama"}}]}]}]}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var catalog = await new SettingsApi(client).GetRecipeCatalogAsync();
        var definition = Assert.Single(catalog.Categories["discovery"]);
        var preset = Assert.Single(definition.Presets);

        Assert.Equal("genre", definition.Type);
        Assert.Equal("Drama night", preset.DisplayName);
        Assert.True(preset.DefaultParams.ContainsKey("genre"));
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson) };
        }
    }
}
