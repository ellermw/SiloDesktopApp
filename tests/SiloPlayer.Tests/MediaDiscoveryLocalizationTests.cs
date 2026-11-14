using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;
using System.Text.Json;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Tests;

public sealed class MediaDiscoveryLocalizationTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Theory]
    [InlineData(null)]
    [InlineData("unknown.server.key")]
    [InlineData("home.section.becauseYouEnjoy")]
    public void MissingOrUnknownMetadataKeepsRawTitles(string? key)
    {
        var raw = "  Save فيلم · 東京  ";
        using var locale = AppStrings.UseForCurrentFlow(new LocaleService().Create(AppLocaleResolution.Resolve("ar", [], "en-US")));
        var row = JsonSerializer.Deserialize<RequestDiscoverRow>(JsonSerializer.Serialize(new { key = "custom", type = "tmdb_chart", title = raw, title_key = key }, Wire), Wire)!;
        Assert.Equal(raw, row.Title);
        Assert.Equal(raw, row.DisplayTitle);
        var section = new RequestDiscoverySection { Title = raw, TitleKey = row.TitleKey, TitleArgs = row.TitleArgs };
        Assert.Equal(raw, section.DisplayTitle);
        Assert.Equal(raw, JsonDocument.Parse(JsonSerializer.Serialize(row, Wire)).RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("display_title", JsonSerializer.Serialize(row, Wire));
    }

    [Fact]
    public void SectionAndLayoutDeserializeExplicitMetadataAndSerializeRawTitles()
    {
        const string json = """{"title":"Raw shelf","title_key":"home.section.becauseYouEnjoy","title_args":{"genres":"Save فيلم"}}""";
        var section = JsonSerializer.Deserialize<RequestDiscoverySection>(json, Wire)!;
        var row = JsonSerializer.Deserialize<RequestDiscoverRow>(json, Wire)!;
        foreach (var language in new[] { "de", "ar", "ja" })
        {
            using var locale = AppStrings.UseForCurrentFlow(new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US")));
            var expected = AppStrings.ServerText(row.TitleKey, row.TitleArgs, row.Title);
            Assert.Equal(expected, row.DisplayTitle);
            Assert.Equal(expected, section.DisplayTitle);
            Assert.NotEqual("Raw shelf", expected);
            Assert.Equal("Raw shelf", row.Title);
            Assert.Equal("Save فيلم", row.TitleArgs!["genres"]);
            Assert.Equal("Raw shelf", JsonDocument.Parse(JsonSerializer.Serialize(section, Wire)).RootElement.GetProperty("title").GetString());
        }
    }
    [Fact]
    public void OlderServerOmittedTitleMetadataPreservesExactRawBytes()
    {
        const string json = """{"title":"  Save فيلم · 東京  "}""";
        var section = JsonSerializer.Deserialize<RequestDiscoverySection>(json, Wire)!;
        var row = JsonSerializer.Deserialize<RequestDiscoverRow>(json, Wire)!;
        using var locale = AppStrings.UseForCurrentFlow(new LocaleService().Create(AppLocaleResolution.Resolve("ar", [], "en-US")));
        Assert.Null(section.TitleKey); Assert.Null(section.TitleArgs);
        Assert.Null(row.TitleKey); Assert.Null(row.TitleArgs);
        Assert.Equal("  Save فيلم · 東京  ", section.DisplayTitle);
        Assert.Equal(section.Title, row.DisplayTitle);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(section.Title), System.Text.Encoding.UTF8.GetBytes(row.Title));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerModelCarriesExplicitAndLegacyTitleMetadataWithoutPersistingDisplay(bool configured)
    {
        const string raw = "  Save فيلم · 東京  ";
        var section = new RequestDiscoverySection { Key = "trending_movies", Title = raw, TitleKey = "home.section.becauseYouEnjoy", TitleArgs = new() { ["genres"] = raw }, Results = [new() { TmdbId = 10, MediaType = "movie", Title = raw }] };
        var layout = new RequestDiscoveryResponse { Sections = [section], RowOrder = configured ? [new() { Key = section.Key, Type = "tmdb_chart", Title = raw, TitleKey = section.TitleKey, TitleArgs = section.TitleArgs }] : null };
        using var handler = new DiscoveryWire(JsonSerializer.Serialize(new { items = layout.Sections, row_order = layout.RowOrder }, Wire), configured);
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://title-fixture.invalid");
        var model = new RequestsViewModel(new RequestsApi(client));
        await model.LoadCommand.ExecuteAsync(null);
        var retainedRow = Assert.Single(model.DiscoveryRows.Where(row => row.Key == section.Key));
        var retainedSection = Assert.Single(model.DiscoverySections);
        Assert.Equal(section.TitleKey, retainedRow.TitleKey);
        Assert.Equal(raw, retainedRow.TitleArgs!["genres"]);
        if (!configured) Assert.NotSame(retainedSection.TitleArgs, retainedRow.TitleArgs);
        var wireBefore = JsonSerializer.Serialize(retainedSection, Wire);
        foreach (var language in new[] { "de", "ar", "ja" })
        {
            using var locale = AppStrings.UseForCurrentFlow(new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US")));
            Assert.Equal(retainedSection.DisplayTitle, retainedRow.DisplayTitle);
            Assert.NotEqual(raw, retainedRow.DisplayTitle);
            Assert.Equal(raw, retainedRow.Title);
            Assert.Equal(wireBefore, JsonSerializer.Serialize(retainedSection, Wire));
        }
    }

    private sealed class DiscoveryWire(string layout, bool configured) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/status") ? $"{{\"requests_enabled\":true,\"discover_rows_supported\":{(configured ? "true" : "false")}}}"
                : path.EndsWith("/discover") ? layout : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

}
