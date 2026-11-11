using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class CardOverlayRevisionTests
{
    [Theory]
    [InlineData("plate")]
    [InlineData("band")]
    [InlineData("future-preset")]
    public async Task EditingKnownBadgePreservesNewPresetAndOpaqueServerFields(string preset)
    {
        var original = $$$$"""
        {"version":2,"preset":"{{{{preset}}}}","style":{"size":1.5,"background":"#12345680","future":7},
         "future_root":{"keep":true},"order":["future_badge","year"],"items":{
         "year":{"enabled":false,"position":"left-edge","image":"badge.webp","valueImages":{"2026":"value.webp"},"future":9},
         "future_badge":{"enabled":true,"position":"right-edge","image":"unknown.webp","future":11}}}
        """;
        using var wire = new OverlayWire(26, original);
        var service = await wire.LoadAsync();
        var prefs = service.GetDocument();
        Assert.Equal(preset, prefs.Preset);
        Assert.Equal("left-edge", CardOverlayService.FormatPosition(prefs.Items["year"].Position));
        prefs.Items["year"] = prefs.Items["year"] with { Enabled = true };
        await service.SaveAsync(prefs);
        using var saved = JsonDocument.Parse(wire.Saved!);
        using var source = JsonDocument.Parse(original);
        var root = saved.RootElement;
        Assert.Equal(preset, root.GetProperty("preset").GetString());
        Assert.Equal(source.RootElement.GetProperty("style").GetRawText(), root.GetProperty("style").GetRawText());
        Assert.True(root.GetProperty("future_root").GetProperty("keep").GetBoolean());
        var year = root.GetProperty("items").GetProperty("year");
        Assert.True(year.GetProperty("enabled").GetBoolean());
        Assert.Equal("left-edge", year.GetProperty("position").GetString());
        Assert.Equal("badge.webp", year.GetProperty("image").GetString());
        Assert.Equal("value.webp", year.GetProperty("valueImages").GetProperty("2026").GetString());
        Assert.Equal(9, year.GetProperty("future").GetInt32());
        Assert.Equal(source.RootElement.GetProperty("items").GetProperty("future_badge").GetRawText(),
            root.GetProperty("items").GetProperty("future_badge").GetRawText());
        Assert.Equal("future_badge", root.GetProperty("order")[0].GetString());
    }

    [Theory]
    [InlineData(24, "top-right", "top-right")]
    [InlineData(25, "top-right", "right-edge")]
    [InlineData(25, "left-edge", "left-edge")]
    [InlineData(25, "right-edge", "right-edge")]
    [InlineData(24, "left-edge", "top-left")]
    [InlineData(24, "right-edge", "top-right")]
    public async Task PositionSemanticsFollowTheAdvertisedManifest(int revision, string stored, string expected)
    {
        using var wire = new OverlayWire(revision, $$$$"""
            {"version":2,"preset":"classic","order":[],"items":{"year":{"enabled":true,"position":"{{{{stored}}}}"}}}
            """);
        var service = await wire.LoadAsync();
        Assert.Equal(expected, CardOverlayService.FormatPosition(service.GetDocument().Items["year"].Position));
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        Assert.Equal(expected, saved.RootElement.GetProperty("items").GetProperty("year").GetProperty("position").GetString());
        Assert.Equal(revision >= 25 ? "right-edge" : "top-right",
            CardOverlayService.FormatPosition(service.GetDocument().Items["rating_imdb"].Position));
    }

    [Theory]
    [InlineData(12, false, false, false)]
    [InlineData(14, true, false, false)]
    [InlineData(25, true, true, false)]
    [InlineData(26, true, true, true)]
    public async Task SaveDoesNotInventBadgeIdsUnsupportedByServer(int revision, bool age, bool request, bool combined)
    {
        using var wire = new OverlayWire(revision, "{\"version\":2,\"preset\":\"classic\",\"items\":{}}");
        var service = await wire.LoadAsync();
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        var items = saved.RootElement.GetProperty("items");
        Assert.Equal(age, items.TryGetProperty("advisory_age", out _));
        Assert.Equal(request, items.TryGetProperty("request_status", out _));
        Assert.Equal(combined, items.TryGetProperty("resolution_hdr_audio", out _));
        Assert.Equal(combined, service.GetOrderedDefinitions().Any(d => d.Id == "resolution_hdr_audio"));
    }

    [Fact]
    public async Task OlderManifestSavePreservesAlreadyStoredNewBadge()
    {
        using var wire = new OverlayWire(25, """
            {"version":2,"preset":"classic","items":{"resolution_hdr_audio":{"enabled":true,"position":"left-edge","image":"keep.webp"}}}
            """);
        var service = await wire.LoadAsync();
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        var badge = saved.RootElement.GetProperty("items").GetProperty("resolution_hdr_audio");
        Assert.True(badge.GetProperty("enabled").GetBoolean());
        Assert.Equal("keep.webp", badge.GetProperty("image").GetString());
        Assert.Equal("left-edge", badge.GetProperty("position").GetString());
    }

    [Fact]
    public async Task OlderManifestDoesNotIntroduceNewPresetOrUnsupportedOrderEntry()
    {
        using var wire = new OverlayWire(24, "{\"version\":2,\"preset\":\"classic\",\"items\":{}}");
        var service = await wire.LoadAsync();
        var draft = service.GetDocument() with { Preset = "plate", Order = ["resolution_hdr_audio", "year"] };
        await service.SaveAsync(draft);
        using var saved = JsonDocument.Parse(wire.Saved!);
        Assert.Equal("classic", saved.RootElement.GetProperty("preset").GetString());
        Assert.Equal(new[] { "year" }, saved.RootElement.GetProperty("order").EnumerateArray().Select(id => id.GetString()));
        Assert.DoesNotContain("resolution_hdr_audio", saved.RootElement.GetProperty("items").EnumerateObject().Select(item => item.Name));
    }

    [Fact]
    public void UnsupportedStoredCombinedBadgeDoesNotHideStandaloneBadges()
    {
        var prefs = OverlayRegistry.DefaultPrefs;
        prefs["resolution_hdr_audio"] = new(true, OverlayPosition.TopLeft);
        Assert.False(OverlayRegistry.SuppressesStandaloneOverlays("audio", prefs, 25));
        Assert.False(OverlayRegistry.SuppressesStandaloneOverlays("resolution", prefs, 25));
    }

    [Theory]
    [InlineData("2160p", "DV HDR10", "Atmos", "4K DV Atmos")]
    [InlineData("1080p", "HDR10+", "TrueHD", "1080p HDR10+ TrueHD")]
    [InlineData(null, null, "Atmos", "Atmos")]
    [InlineData(null, "HLG", null, "HDR")]
    [InlineData(null, null, null, null)]
    public void CombinedTechBadgeFormatsEveryAvailablePart(string? resolution, string? hdr, string? audio, string? expected)
    {
        var definition = Assert.Single(OverlayRegistry.All, d => d.Id == "resolution_hdr_audio");
        Assert.Equal(expected, definition.GetValue(new OverlayData { Resolution = resolution, Hdr = hdr, Audio = audio }));
    }

    [Theory]
    [InlineData("resolution", true)]
    [InlineData("hdr", true)]
    [InlineData("audio", true)]
    [InlineData("resolution_hdr", true)]
    [InlineData("year", false)]
    public void CombinedTechBadgeSuppressesRepeatedPartsWithoutChangingPreferences(string id, bool suppressed)
    {
        var prefs = OverlayRegistry.DefaultPrefs;
        prefs["resolution_hdr_audio"] = new(true, OverlayPosition.TopLeft);
        Assert.Equal(suppressed, OverlayRegistry.SuppressesStandaloneOverlays(id, prefs));
        prefs["resolution_hdr_audio"] = new(false, OverlayPosition.TopLeft);
        prefs["resolution_hdr"] = new(true, OverlayPosition.TopLeft);
        Assert.Equal(id is "resolution" or "hdr", OverlayRegistry.SuppressesStandaloneOverlays(id, prefs));
        Assert.True(prefs["resolution"].Enabled);
        Assert.True(prefs["audio"].Enabled);
    }

    [Theory]
    [InlineData(true, 0, 0, "Watched")]
    [InlineData(true, 2, 3, "Watched")]
    [InlineData(false, 1, 3, "3 episodes left")]
    [InlineData(false, 1, 1, "1 episode left")]
    [InlineData(false, 0, 5, null)]
    [InlineData(false, 1, 0, null)]
    [InlineData(false, -1, 2, null)]
    public void PosterWatchStatusUsesCatalogCountsAndPlayedTakesPrecedence(bool played, int watched, int left, string? expected)
    {
        var state = JsonSerializer.Deserialize<UserState>($$$$"""{"played":{{{{played.ToString().ToLowerInvariant()}}}},"watched_count":{{{{watched}}}},"unplayed_count":{{{{left}}}}}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Equal(expected, CardWatchStatus.AccessibleName(state));
        Assert.Null(CardWatchStatus.AccessibleName(null));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void CatalogCountChangesReplaceMountedCards(int watched, int remaining)
    {
        MediaItem Item(int count, int left) => JsonSerializer.Deserialize<MediaItem>($$$$"""
            {"content_id":"series","type":"series","user_state":{"played":false,"watched_count":{{{{count}}}},"unplayed_count":{{{{left}}}}}}
            """, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        var current = new ObservableCollection<MediaItem>([Item(1, 3)]);
        Assert.True(MediaItemCollectionReconciler.Apply(current, [Item(watched, remaining)]));
        Assert.False(MediaItemCollectionReconciler.Apply(current, [Item(watched, remaining)]));
    }

    [Fact]
    public async Task FailedCapabilitiesDoNotDowngradeStoredPositionsOrHideBadges()
    {
        using var wire = new OverlayWire(26, """
        {"version":2,"preset":"classic","items":{"year":{"enabled":true,"position":"left-edge"},"request_status":{"enabled":true,"position":"right-edge"}}}
        """);
        wire.FailCapabilities = true;
        var service = await wire.LoadAsync();
        Assert.Equal(OverlayPosition.LeftEdge, service.GetDocument().Items["year"].Position);
        Assert.Contains(service.GetOrderedDefinitions(), d => d.Id == "request_status");
        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveAsync(service.GetDocument()));
        Assert.Null(wire.Saved);
        wire.FailCapabilities = false;
        await service.SaveAsync(service.GetDocument());
        Assert.Contains("left-edge", wire.Saved);
    }

    [Fact]
    public void UnwatchClearsSeriesCountsEvenWhenAlreadyUnwatched()
    {
        var item = new MediaItem { UserState = new() { Played = false, WatchedCount = 2, UnplayedCount = 5 } };
        Assert.True(MediaItemStateUpdater.SetWatched(item, false));
        Assert.Equal(0, item.UserState.WatchedCount);
        Assert.Equal(0, item.UserState.UnplayedCount);
    }

    [Fact]
    public async Task WatchedToggleClearsCountsAndRestoresThemOnRejectedWrite()
    {
        var item = new MediaItem { UserState = new() { Played = false, WatchedCount = 2, UnplayedCount = 5 } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaItemActionExecutor.ToggleWatchedAsync(item, _ =>
        {
            Assert.Equal(0, item.UserState.WatchedCount);
            Assert.Equal(0, item.UserState.UnplayedCount);
            throw new InvalidOperationException("Rejected");
        }));
        Assert.False(item.UserState.Played);
        Assert.Equal(2, item.UserState.WatchedCount);
        Assert.Equal(5, item.UserState.UnplayedCount);
    }

    private sealed class OverlayWire(int revision, string document) : HttpMessageHandler
    {
        public string? Saved;
        public bool FailCapabilities;
        public async Task<CardOverlayService> LoadAsync()
        {
            var api = new SiloApiClient(new HttpClient(this, disposeHandler: false));
            api.SetBaseUrl("https://fixture.invalid");
            var service = new CardOverlayService(new SettingsApi(api));
            await service.EnsureLoadedAsync();
            return service;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Saved = payload.RootElement.GetProperty("value").GetRawText();
                body = new { };
            }
            else if (path.EndsWith("/contract/capabilities") && FailCapabilities)
                return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            else if (path.EndsWith("/contract/capabilities"))
                body = new { api_version = 1, manifest_revision = revision, supports_batched_effective = true };
            else if (path.EndsWith("/overlay-config")) body = new { enabled = true };
            else if (path.EndsWith("/values/ui.card_overlays"))
                body = new { key = "ui.card_overlays", value = JsonSerializer.Deserialize<JsonElement>(document) };
            else if (path.EndsWith("/values/effective")) body = new { items = Array.Empty<object>() };
            else throw new InvalidOperationException("Unexpected fixture route " + path);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
}
