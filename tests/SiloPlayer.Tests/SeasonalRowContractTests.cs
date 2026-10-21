using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SeasonalRowContractTests
{
    [Fact]
    public async Task SeasonalCatalogReadsServerOrderedDatesVibesAndLimitUnderSelectedProfile()
    {
        using var wire = new Wire("""{"themes":[{"key":"easter","name":"Easter","when":"Spring","dates_editable":true,"vibes":[{"key":"family","name":"Family","title":"Family Easter","description":"Together","list_url":"https://mdblist.com/lists/family/easter"}]}],"max_custom_holidays":7}""");
        var client = new SiloApiClient(new HttpClient(wire));
        client.SetBaseUrl("https://seasonal.invalid"); client.SetProfile("seasonal-profile");
        var catalog = await new HomeApi(client).GetSeasonalThemesAsync();
        Assert.Equal("/api/v2/sections/seasonal-themes", wire.Uri!.AbsolutePath);
        Assert.Equal("seasonal-profile", wire.Profile);
        Assert.Equal(7, catalog.MaxCustomHolidays);
        var theme = Assert.Single(catalog.Themes);
        Assert.Equal("easter", theme.Key); Assert.True(theme.DatesEditable);
        Assert.Equal("Family Easter", Assert.Single(theme.Vibes).Title);
        Assert.Equal("https://mdblist.com/lists/family/easter", theme.Vibes[0].ListUrl);
    }

    [Theory]
    [InlineData(null, "home")]
    [InlineData(32, "library")]
    public async Task SectionCatalogRequestRetainsAnyRowSourceAndCursor(int? libraryId, string scope)
    {
        using var wire = new Wire("""{"items":[],"total":200,"total_exact":false,"page":{"has_more":true,"next_cursor":"next-page"}}""");
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://rows.invalid");
        await new CatalogApi(client).GetCatalogAsync(libraryId, source: "section", scope: scope, sectionId: "system-next-up", nextCursor: "signed-row-cursor");
        var query = QueryHelpers.ParseQuery(wire.Uri!.Query);
        Assert.Equal("section", query["source"].ToString());
        Assert.Equal(scope, query["scope"].ToString());
        Assert.Equal("system-next-up", query["section_id"].ToString());
        Assert.Equal("signed-row-cursor", query["cursor"].ToString());
        Assert.False(query.ContainsKey("seek"));
        if (libraryId.HasValue) Assert.Equal(libraryId.Value.ToString(), query["library_id"].ToString());
        else Assert.False(query.ContainsKey("library_id"));
    }

    [Fact]
    public void CustomOnlyLeapWindowSerializesCurrentWireFieldsAndKeepsUnknownConfig()
    {
        var draft = new Dictionary<string, object> { ["enabled_themes"] = Array.Empty<string>(), ["future"] = true };
        SeasonalRowConfiguration.Set(draft, "custom_holidays", new[] { new SeasonalCustomHoliday
        { Name = "Leap night", Start = "02-29", End = "01-02", ListUrl = "https://mdblist.com/lists/family/leap", Title = "Leap picks" } });
        Assert.Null(SeasonalRowConfiguration.Validate(draft));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(draft));
        var holiday = json.RootElement.GetProperty("custom_holidays")[0];
        Assert.Equal("https://mdblist.com/lists/family/leap", holiday.GetProperty("list_url").GetString());
        Assert.Equal("02-29", holiday.GetProperty("start").GetString());
        Assert.True(json.RootElement.GetProperty("future").GetBoolean());
        Assert.Equal("Leap picks", Assert.Single(SeasonalRowConfiguration.Read(draft, "custom_holidays", new List<SeasonalCustomHoliday>())).Title);
    }

    [Theory]
    [InlineData("https://@mdblist.com/lists/a/b")]
    [InlineData("https://mdblist.com:445/lists/a/b")]
    [InlineData("https://mdblist.com.invalid/lists/a/b")]
    [InlineData("https://mdblist.com/users/a")]
    public void InvalidCustomListCannotSave(string url)
    {
        var draft = new Dictionary<string, object>();
        SeasonalRowConfiguration.Set(draft, "custom_holidays", new[] { new SeasonalCustomHoliday { Name = "Day", Start = "07-04", End = "07-04", ListUrl = url } });
        Assert.Contains("MDBList", SeasonalRowConfiguration.Validate(draft));
    }

    [Fact]
    public void PartialAndImpossibleDatesCannotSaveButBlankOverridesUseDefault()
    {
        var draft = new Dictionary<string, object> { ["enabled_themes"] = new[] { "halloween" } };
        SeasonalRowConfiguration.Set(draft, "theme_dates", new Dictionary<string, SeasonalDates> { ["halloween"] = new() { Start = "02-30", End = "" } });
        Assert.Contains("both dates", SeasonalRowConfiguration.Validate(draft));
        draft.Remove("theme_dates"); Assert.Null(SeasonalRowConfiguration.Validate(draft));
    }

    private sealed class Wire(string response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Profile { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Profile = request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}
