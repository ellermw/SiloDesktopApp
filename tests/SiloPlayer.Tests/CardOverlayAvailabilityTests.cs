using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// The series/season availability ribbon (official Silo-Server #2454): registry entry at settings
/// revision 29, the optional <c>availability</c> field of catalog items and seasons, and its
/// placement, accent and text.
/// </summary>
public sealed class CardOverlayAvailabilityTests
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private const string EnabledAtTopLeft =
        """{"version":2,"preset":"classic","order":[],"items":{"availability":{"enabled":true,"position":"top-left"}}}""";
    private const string EmptyDocument = """{"version":2,"preset":"classic","items":{}}""";

    // ── Registry entry ────────────────────────────────────────────────────

    [Fact]
    public void AvailabilityIsOffByDefaultInTheTopLeftCorner()
    {
        var definition = Assert.Single(OverlayRegistry.All, d => d.Id == "availability");
        Assert.False(definition.DefaultEnabled);
        Assert.Equal(OverlayPosition.TopLeft, definition.DefaultPosition);
        var config = OverlayRegistry.DefaultPrefs["availability"];
        Assert.False(config.Enabled);
        Assert.Equal(OverlayPosition.TopLeft, config.Position);
    }

    // ── Manifest handling: older, newer, missing ──────────────────────────

    [Theory]
    [InlineData(25, false)]
    [InlineData(28, false)]
    [InlineData(29, true)]
    [InlineData(30, true)]
    [InlineData(40, true)]
    public async Task AvailabilityIsOfferedFromSettingsRevision29(int revision, bool offered)
    {
        using var wire = new OverlayWire(revision, EnabledAtTopLeft);
        var service = await wire.LoadAsync();
        Assert.Equal(offered, service.OffersOverlay("availability"));
        Assert.Equal(offered, service.GetOrderedDefinitions().Any(d => d.Id == "availability"));
    }

    [Fact]
    public async Task ServerBelow29HidesAStoredEnabledAvailabilityBadge()
    {
        using var wire = new OverlayWire(28, EnabledAtTopLeft);
        var service = await wire.LoadAsync();
        // The stored document keeps the entry, but no card or settings row draws it.
        Assert.True(service.GetDocument().Items["availability"].Enabled);
        Assert.DoesNotContain(service.GetOrderedDefinitions(), d => d.Id == "availability");
    }

    [Fact]
    public async Task MissingManifestBeforeTheFirstLoadOffersNothing()
    {
        using var wire = new OverlayWire(29, EnabledAtTopLeft);
        var service = wire.CreateUnloaded();
        Assert.False(service.IsLoaded);
        Assert.Equal(0, service.ManifestRevision);
        Assert.False(service.OffersOverlay("availability"));
        await service.EnsureLoadedAsync();
        Assert.True(service.OffersOverlay("availability"));
    }

    [Theory]
    [InlineData(28)]
    [InlineData(29)]
    public async Task UnavailableManifestOffersProvisionallyAndSaveReportsAnUnsupportedServer(int revision)
    {
        using var wire = new OverlayWire(revision, EmptyDocument) { FailCapabilities = true };
        var service = await wire.LoadAsync();
        Assert.True(service.OffersOverlay("availability"));
        var draft = service.GetDocument();
        draft.Items["availability"] = draft.Items["availability"] with { Enabled = true };
        wire.FailCapabilities = false;
        if (revision < 29)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(draft));
            Assert.Contains("server manifest does not support", error.Message);
            Assert.Contains("Availability", error.Message);
            Assert.Null(wire.Saved);
            Assert.False(service.GetDocument().Items["availability"].Enabled);
        }
        else
        {
            await service.SaveAsync(draft);
            using var saved = JsonDocument.Parse(wire.Saved!);
            Assert.True(saved.RootElement.GetProperty("items").GetProperty("availability").GetProperty("enabled").GetBoolean());
        }
    }

    [Theory]
    [InlineData(28, false)]
    [InlineData(29, true)]
    public async Task SaveWritesAvailabilityOnlyToAServerThatAcceptsIt(int revision, bool written)
    {
        using var wire = new OverlayWire(revision, EmptyDocument);
        var service = await wire.LoadAsync();
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        var items = saved.RootElement.GetProperty("items");
        Assert.Equal(written, items.TryGetProperty("availability", out var badge));
        if (!written) return;
        Assert.False(badge.GetProperty("enabled").GetBoolean());
        Assert.Equal("top-left", badge.GetProperty("position").GetString());
        Assert.Contains("availability", service.GetOrderedDefinitions().Select(d => d.Id));
    }

    [Fact]
    public async Task OlderManifestSavePreservesAnAlreadyStoredAvailabilityEntry()
    {
        using var wire = new OverlayWire(28, EnabledAtTopLeft);
        var service = await wire.LoadAsync();
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        var badge = saved.RootElement.GetProperty("items").GetProperty("availability");
        Assert.True(badge.GetProperty("enabled").GetBoolean());
        Assert.Equal("top-left", badge.GetProperty("position").GetString());
    }

    [Fact]
    public async Task AvailabilityPositionAndAccentRoundTripThroughASave()
    {
        using var wire = new OverlayWire(29, """{"version":2,"preset":"classic","items":{"availability":{"enabled":true,"position":"left-edge","accentColor":"#123456","showIcon":true}}}""");
        var service = await wire.LoadAsync();
        var item = service.GetDocument().Items["availability"];
        Assert.Equal(OverlayPosition.LeftEdge, item.Position);
        Assert.Equal("#123456", item.AccentColor);
        Assert.True(item.ShowIcon);
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        var badge = saved.RootElement.GetProperty("items").GetProperty("availability");
        Assert.Equal("left-edge", badge.GetProperty("position").GetString());
        Assert.Equal("#123456", badge.GetProperty("accentColor").GetString());
        Assert.True(badge.GetProperty("showIcon").GetBoolean());
    }

    // ── Data: missing, partial, complete ──────────────────────────────────

    [Theory]
    [InlineData(null, null, OverlayAvailabilityState.None, null)]
    [InlineData(2, null, OverlayAvailabilityState.None, null)]
    [InlineData(null, 3, OverlayAvailabilityState.None, null)]
    [InlineData(0, 0, OverlayAvailabilityState.None, null)]
    [InlineData(2, 0, OverlayAvailabilityState.None, null)]
    [InlineData(2, -1, OverlayAvailabilityState.None, null)]
    [InlineData(0, 3, OverlayAvailabilityState.Partial, "0/3")]
    [InlineData(2, 3, OverlayAvailabilityState.Partial, "2/3")]
    [InlineData(11, 12, OverlayAvailabilityState.Partial, "11/12")]
    [InlineData(3, 3, OverlayAvailabilityState.Complete, "Complete")]
    [InlineData(1, 1, OverlayAvailabilityState.Complete, "Complete")]
    [InlineData(4, 3, OverlayAvailabilityState.Complete, "Complete")]
    public void AvailabilityDataGivesNoBadgePartialOrComplete(int? have, int? total, OverlayAvailabilityState state, string? text)
    {
        var data = new OverlayData { AvailabilityHave = have, AvailabilityTotal = total };
        var definition = Assert.Single(OverlayRegistry.All, d => d.Id == "availability");
        Assert.Equal(state, OverlayRegistry.AvailabilityState(data));
        Assert.Equal(text, definition.GetValue(data));
        Assert.Equal(state == OverlayAvailabilityState.Complete, OverlayRegistry.IsAvailabilityComplete(definition.GetValue(data)));
    }

    [Fact]
    public void AvailabilityHasNoValueImageKey()
    {
        // The WebUI's availability badge takes no per-value images.
        Assert.Null(OverlayRegistry.ValueKey("availability", new OverlayData { AvailabilityHave = 3, AvailabilityTotal = 3 }));
        Assert.Null(OverlayRegistry.ValueKey("availability", new OverlayData { AvailabilityHave = 2, AvailabilityTotal = 3 }));
    }

    [Fact]
    public void OtherBadgesStayEmptyForAvailabilityOnlyData()
    {
        var data = new OverlayData { AvailabilityHave = 2, AvailabilityTotal = 3 };
        foreach (var definition in OverlayRegistry.All.Where(d => d.Id != "availability"))
            Assert.Null(definition.GetValue(data));
    }

    [Theory]
    [InlineData("Complete", OverlayRegistry.AvailabilityCompleteAccent)]
    [InlineData("2/3", OverlayRegistry.AvailabilityPartialAccent)]
    [InlineData("0/3", OverlayRegistry.AvailabilityPartialAccent)]
    public void CompleteUsesTheCompleteAccentAndPartialTheAttentionAccent(string text, string accent)
    {
        Assert.Equal(accent, OverlayRegistry.DataAccent("availability", text));
        Assert.Null(OverlayRegistry.DataAccent("year", text));
        Assert.Null(OverlayRegistry.DataAccent("request_status", text));
    }

    [Fact]
    public void AccentsMatchTheWebUi()
    {
        Assert.Equal("#21c452", OverlayRegistry.AvailabilityCompleteAccent);
        Assert.Equal("#f59e0b", OverlayRegistry.AvailabilityPartialAccent);
    }

    // ── Wire: the optional API field ──────────────────────────────────────

    [Fact]
    public void CatalogItemReadsAvailabilityHaveAndTotal()
    {
        var item = JsonSerializer.Deserialize<MediaItem>("""{"content_id":"s1","type":"series","availability":{"have":2,"total":3}}""", SnakeCase)!;
        Assert.Equal(2, item.Availability!.Have);
        Assert.Equal(3, item.Availability.Total);
        var data = OverlayData.FromMediaItem(item);
        Assert.Equal(2, data.AvailabilityHave);
        Assert.Equal(3, data.AvailabilityTotal);
        Assert.Equal(OverlayAvailabilityState.Partial, OverlayRegistry.AvailabilityState(data));
    }

    [Theory]
    [InlineData("""{"content_id":"s1","type":"series"}""")]
    [InlineData("""{"content_id":"s1","type":"series","availability":null}""")]
    [InlineData("""{"content_id":"m1","type":"movie","title":"Film"}""")]
    public void MissingAvailabilityFieldMeansNoRibbon(string json)
    {
        var item = JsonSerializer.Deserialize<MediaItem>(json, SnakeCase)!;
        Assert.Null(item.Availability);
        var data = OverlayData.FromMediaItem(item);
        Assert.Null(data.AvailabilityHave);
        Assert.Null(data.AvailabilityTotal);
        Assert.Equal(OverlayAvailabilityState.None, OverlayRegistry.AvailabilityState(data));
        Assert.Null(Assert.Single(OverlayRegistry.All, d => d.Id == "availability").GetValue(data));
    }

    [Fact]
    public void PartialObjectWithoutTotalIsNoRibbon()
    {
        var item = JsonSerializer.Deserialize<MediaItem>("""{"content_id":"s1","type":"series","availability":{"have":2}}""", SnakeCase)!;
        Assert.Equal(OverlayAvailabilityState.None, OverlayRegistry.AvailabilityState(OverlayData.FromMediaItem(item)));
    }

    [Fact]
    public void SeasonReadsItsOwnEpisodeCounts()
    {
        var response = JsonSerializer.Deserialize<SeasonsResponse>("""
            {"seasons":[
              {"content_id":"a","season_number":1,"episode_count":10,"availability":{"have":10,"total":10}},
              {"content_id":"b","season_number":2,"episode_count":8,"availability":{"have":5,"total":8}},
              {"content_id":"c","season_number":0,"episode_count":2}]}
            """, SnakeCase)!;
        Assert.Equal("Complete", Text(response.Seasons[0]));
        Assert.Equal("5/8", Text(response.Seasons[1]));
        Assert.Null(response.Seasons[2].Availability);
        Assert.Null(Text(response.Seasons[2]));

        static string? Text(Season season) =>
            Assert.Single(OverlayRegistry.All, d => d.Id == "availability").GetValue(OverlayData.FromSeason(season));
    }

    [Fact]
    public void AvailabilityChangesReplaceMountedCards()
    {
        MediaItem Item(int have) => JsonSerializer.Deserialize<MediaItem>(
            $$$$"""{"content_id":"s1","type":"series","availability":{"have":{{{{have}}}},"total":3}}""", SnakeCase)!;
        var current = new System.Collections.ObjectModel.ObservableCollection<MediaItem>([Item(1)]);
        Assert.False(MediaItemCollectionReconciler.Apply(current, [Item(1)]));
        Assert.True(MediaItemCollectionReconciler.Apply(current, [Item(2)]));
        Assert.Equal(2, current[0].Availability!.Have);
    }

    // ── Text: English and shipped translations ────────────────────────────

    [Fact]
    public void EnglishTextUsesTheShippedMessages()
    {
        using var scope = AppStrings.UseForCurrentFlow(Locale("en", "en-US"));
        Assert.Equal("Complete", OverlayRegistry.LocalizeAvailability("Complete"));
        Assert.Equal("2/3", OverlayRegistry.LocalizeAvailability("2/3"));
        Assert.Equal("0/12", OverlayRegistry.LocalizeAvailability("0/12"));
        // Text the badge did not produce is shown as it came.
        Assert.Equal("Other", OverlayRegistry.LocalizeAvailability("Other"));
        Assert.Equal("-1/3", OverlayRegistry.LocalizeAvailability("-1/3"));
    }

    [Fact]
    public void FrenchCompleteFollowsTheInterfaceLanguage()
    {
        using var scope = AppStrings.UseForCurrentFlow(Locale("fr", "fr-FR"));
        Assert.Equal("Complète", OverlayRegistry.LocalizeAvailability("Complete"));
        Assert.Equal("2/3", OverlayRegistry.LocalizeAvailability("2/3"));
    }

    // ── RTL: corners are positions on the artwork ─────────────────────────

    [Theory]
    [InlineData("top-left", OverlayPosition.TopLeft)]
    [InlineData("left-edge", OverlayPosition.LeftEdge)]
    [InlineData("right-edge", OverlayPosition.RightEdge)]
    [InlineData("bottom-left", OverlayPosition.BottomLeft)]
    [InlineData("bottom-right", OverlayPosition.BottomRight)]
    public async Task RightToLeftLanguageDoesNotMirrorTheStoredCorner(string stored, OverlayPosition expected)
    {
        var arabic = Locale("ar", "ar-SA");
        Assert.True(arabic.IsRightToLeft);
        using var wire = new OverlayWire(29, $$$$"""{"version":2,"preset":"classic","items":{"availability":{"enabled":true,"position":"{{{{stored}}}}"}}}""");
        var service = await wire.LoadAsync();
        using var scope = AppStrings.UseForCurrentFlow(arabic);
        Assert.Equal(expected, service.GetDocument().Items["availability"].Position);
        Assert.Equal(expected, service.DisplayPosition(expected));
        await service.SaveAsync(service.GetDocument());
        using var saved = JsonDocument.Parse(wire.Saved!);
        Assert.Equal(stored, saved.RootElement.GetProperty("items").GetProperty("availability").GetProperty("position").GetString());
    }

    [Fact]
    public void ArabicCompleteAndFractionReadInTheInterfaceLanguage()
    {
        var arabic = Locale("ar", "ar-SA");
        Assert.True(arabic.IsRightToLeft);
        using var scope = AppStrings.UseForCurrentFlow(arabic);
        Assert.Equal("مكتمل", OverlayRegistry.LocalizeAvailability("Complete"));
        var fraction = OverlayRegistry.LocalizeAvailability("2/3");
        Assert.Contains("/", fraction);
        Assert.NotEqual("web.settings.overlays.badge.availability.value.fraction", fraction);
    }

    [Fact]
    public void BadgeHostsLayOutLeftToRightInsideARightToLeftPage()
    {
        var hosts = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "CardBadges.cs"));
        Assert.Contains("foreach (var host in new[] { topLeft, topRight, bottomLeft, bottomRight, leftEdge, rightEdge }) KeepLeftToRight(host);", hosts);
        Assert.Contains("_bottomRows ??= KeepLeftToRight(", hosts);
        Assert.Contains("_bottomBand ??= KeepLeftToRight(", hosts);
        Assert.Contains("_topScrim ??= KeepLeftToRight(", hosts);
        Assert.Contains("element.FlowDirection = FlowDirection.LeftToRight", hosts);
        Assert.Contains("marks.FlowDirection = FlowDirection.LeftToRight", hosts);
        Assert.Contains("Gap = look.StackGap * scale, FlowDirection = FlowDirection.LeftToRight", hosts);
    }

    // ── Progress bar: bottom corners clear it, top corners do not move ───

    [Fact]
    public void ProgressBarClearanceLiftsOnlyWhenABarIsShown()
    {
        Assert.Equal(0, CardOverlayGeometry.ProgressClearance(false));
        Assert.Equal(CardOverlayGeometry.ProgressBarClearance, CardOverlayGeometry.ProgressClearance(true));
        Assert.Equal(8, CardOverlayGeometry.ProgressBarClearance);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(150)]
    [InlineData(185)]
    [InlineData(240)]
    [InlineData(300)]
    public void BottomBadgesSitAboveTheSeasonProgressTrack(double cardWidth)
    {
        var geometry = CardOverlayGeometry.ForPoster(cardWidth);
        var track = MediaCardProgressGeometry.Calculate(cardWidth, .5, episodeCard: false);
        const double trackHeight = 3;
        var badgeBottom = geometry.EdgeInset + CardOverlayGeometry.ProgressClearance(true);
        Assert.True(badgeBottom >= track.BottomInset + trackHeight,
            $"bottom badges at {badgeBottom} overlap the progress track top at {track.BottomInset + trackHeight}");
        Assert.True(geometry.EdgeInset + CardOverlayGeometry.ProgressClearance(false) < badgeBottom);
    }

    [Fact]
    public void AvailabilityStaysTopLeftBesideAProgressBarWithoutChangingTheItem()
    {
        var item = JsonSerializer.Deserialize<MediaItem>("""
            {"content_id":"s1","type":"series","availability":{"have":1,"total":2},
             "position_seconds":120,"duration_seconds":2400,"user_state":{"played":false}}
            """, SnakeCase)!;
        Assert.Equal(120d, item.PositionSeconds);
        var data = OverlayData.FromMediaItem(item);
        Assert.Equal("1/2", Assert.Single(OverlayRegistry.All, d => d.Id == "availability").GetValue(data));
        Assert.Equal(OverlayPosition.TopLeft, OverlayRegistry.DefaultPrefs["availability"].Position);
    }

    [Fact]
    public void SeasonCardsTakeTheirBadgeFromTheSeasonListWithoutAnotherRead()
    {
        var page = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        Assert.Contains("AddSeasonAvailabilityBadge(posterHost, season, cardWidth, progressVisible, overlays);", page);
        Assert.Contains("OverlayData.FromSeason(season)", page);
        Assert.Contains("CardBadgeHosts.Create(posterHost, geometry, CardOverlayGeometry.ProgressClearance(progressVisible))", page);
        var start = page.IndexOf("private void AddSeasonAvailabilityBadge", StringComparison.Ordinal);
        var end = page.IndexOf("private async Task RebuildSeasonCardsAfterOverlaysLoadAsync", StringComparison.Ordinal);
        Assert.InRange(start, 1, end - 1);
        var body = page[start..end];
        Assert.DoesNotContain("Api", body);
        Assert.DoesNotContain("await", body);
    }

    // ── Settings ──────────────────────────────────────────────────────────

    [Fact]
    public void SettingsOffersAvailabilityUnderRibbonsWithTheShippedKeys()
    {
        var page = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "SettingsPage.xaml.cs"));
        Assert.Contains("[\"show_status\", \"request_status\", \"availability\"]", page);
        Assert.Contains("\"resolution_hdr_audio\" or \"availability\" => _cardOverlayService.OffersOverlay(id)", page);
        Assert.Contains("web.settings.overlays.badge.availability.label", page);
        Assert.Contains("web.settings.overlays.badge.availability.description", page);
        foreach (var key in new[] { "label", "description", "value.complete" })
            Assert.NotEqual("web.settings.overlays.badge.availability." + key, AppStrings.English("web.settings.overlays.badge.availability." + key));
        Assert.Equal("2/3", AppStrings.English("web.settings.overlays.badge.availability.value.fraction",
            new Dictionary<string, object?> { ["have"] = 2, ["total"] = 3 }));
    }

    private static AppLocale Locale(string tag, string regionalCulture) =>
        LocaleService.Shared.Create(AppLocaleResolution.Resolve(tag, [], regionalCulture));

    private sealed class OverlayWire(int revision, string document) : HttpMessageHandler
    {
        public string? Saved;
        public bool FailCapabilities;
        public int Revision = revision;
        public SiloApiClient Client { get; private set; } = null!;

        public CardOverlayService CreateUnloaded()
        {
            Client = new SiloApiClient(new HttpClient(this, disposeHandler: false));
            Client.SetBaseUrl("https://fixture.invalid");
            return new CardOverlayService(new SettingsApi(Client));
        }

        public async Task<CardOverlayService> LoadAsync()
        {
            var service = CreateUnloaded();
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
            else if (path.EndsWith("/contract/capabilities"))
            {
                if (FailCapabilities)
                    return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                body = new { api_version = 1, manifest_revision = Revision, supports_batched_effective = true };
            }
            else if (path.EndsWith("/overlay-config")) body = new { enabled = true };
            else if (path.EndsWith("/values/ui.card_overlays"))
                body = new { key = "ui.card_overlays", value = JsonSerializer.Deserialize<JsonElement>(document) };
            else if (path.EndsWith("/values/effective")) body = new { items = Array.Empty<object>() };
            else throw new InvalidOperationException("Unexpected fixture route " + path);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
}
