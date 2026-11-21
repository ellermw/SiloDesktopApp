using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class AdminAppearanceOverlayTests
{
    private static readonly string Reference = new string('a', 64) + ".webp";
    private static string Receipt(string? name = null, int width = 300, string? url = null) => JsonSerializer.Serialize(new
    {
        @ref = Reference, name = name ?? "Badge", width, height = 120,
        url = url ?? "/api/v2/overlay-images/" + Reference,
    });

    // serializeOverlayPrefs(buildDefaultPrefs()) at official 66b836f82, written out from
    // web/src/lib/overlays/registry/{tech,ratings,metadata,ribbons}.ts in registry order.
    private const string OfficialBuiltIn = """{"version":2,"preset":"classic","order":[],"items":{"resolution":{"enabled":true,"position":"top-left"},"hdr":{"enabled":true,"position":"top-left"},"resolution_hdr":{"enabled":false,"position":"top-left"},"resolution_hdr_audio":{"enabled":false,"position":"top-left"},"audio":{"enabled":true,"position":"top-left"},"audio_channels":{"enabled":false,"position":"top-left"},"video_codec":{"enabled":false,"position":"top-left"},"container":{"enabled":false,"position":"bottom-left"},"aspect_ratio":{"enabled":false,"position":"bottom-right"},"release_type":{"enabled":true,"position":"bottom-left"},"edition":{"enabled":false,"position":"bottom-left"},"multi_audio":{"enabled":false,"position":"bottom-right"},"multi_sub":{"enabled":false,"position":"bottom-right"},"rating_imdb":{"enabled":false,"position":"left-edge"},"rating_tmdb":{"enabled":false,"position":"left-edge"},"rating_rt":{"enabled":false,"position":"left-edge"},"rating_rt_audience":{"enabled":false,"position":"left-edge"},"content_rating":{"enabled":false,"position":"bottom-right"},"advisory_age":{"enabled":false,"position":"bottom-right"},"year":{"enabled":false,"position":"bottom-left"},"runtime":{"enabled":false,"position":"bottom-left"},"original_language":{"enabled":false,"position":"bottom-left"},"studio":{"enabled":false,"position":"bottom-right"},"network":{"enabled":false,"position":"bottom-right"},"show_status":{"enabled":false,"position":"right-edge"},"request_status":{"enabled":true,"position":"top-left"},"availability":{"enabled":false,"position":"top-left"}}}""";

    [Fact]
    public void BuiltInsAreExactlyTheOfficialSerializedRegistryDefaults()
    {
        Assert.Equal(OfficialBuiltIn, CardOverlayService.BuiltInEditorDefaults);
        var defaults = CardOverlayService.ParseEditorDocument(CardOverlayService.BuiltInEditorDefaults);
        Assert.Equal(OverlayRegistry.All.Select(d => d.Id), defaults.Items.Keys);
        Assert.Equal(CardOverlayService.BuiltInEditorDefaults, CardOverlayService.SerializeEditorDocument(defaults));
        // Text is written as JSON.stringify writes it: "+" and non-ASCII stay as they are, so a document the web wrote comes back unchanged.
        var web = "{\"x_note\":\"DV HDR10+ é\"," + CardOverlayService.BuiltInEditorDefaults[1..];
        var written = CardOverlayService.SerializeEditorDocument(CardOverlayService.ParseEditorDocument(web));
        Assert.Contains("DV HDR10+ é", written); Assert.DoesNotContain("\\u002B", written); Assert.DoesNotContain("\\u00E9", written, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TimeSpan.FromSeconds(30), SiloPlayer.Core.Api.SiloApiClient.AdminAppearanceUploadDeadline(0));
        Assert.Equal(TimeSpan.FromSeconds(150), SiloPlayer.Core.Api.SiloApiClient.AdminAppearanceUploadDeadline(12 * 1024 * 1024));
        Assert.Equal(TimeSpan.FromSeconds(40), SiloPlayer.Core.Api.SiloApiClient.AdminAppearanceUploadDeadline(1));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(OfficialBuiltIn, true)]
    // The official parser drops what it does not understand, so the built-in document with an extra field is still built in.
    [InlineData("""{"version":2,"preset":"classic","order":[],"items":{},"future":1}""", true)]
    [InlineData("""{"version":2,"preset":"plate","order":[],"items":{}}""", false)]
    [InlineData("""{"version":2,"preset":"classic","order":[],"items":{"year":{"enabled":true}}}""", false)]
    [InlineData("""{"version":2,"preset":"classic","order":[],"items":{},"style":{"size":1.5}}""", false)]
    public void RestoreIsOfferedOnlyWhenTheStoredDefaultsDifferFromTheBuiltIns(string stored, bool builtIn)
        => Assert.Equal(builtIn, CardOverlayService.IsBuiltInEditorDocument(stored));

    [Fact]
    public void ItemEditsPreserveUnknownFieldsOpaqueValuesAndUnchangedPositions()
    {
        const string original = """
        {"version":7,"preset":"future","order":["new_badge","year"],"future":{"a":1},
         "items":{"new_badge":{"mode":3},"year":{"enabled":false,"position":"next-edge","accentColor":"future","future":42},
         "resolution":{"position":"top-right","valueImages":{"2160p":"old","future":42}}},"style":{"size":1,"future":true}}
        """;
        var draft = CardOverlayService.ParseEditorDocument(original);
        draft.Items["year"] = draft.Items["year"] with { Enabled = true };
        using var document = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft));
        var root = document.RootElement;
        Assert.Equal(7, root.GetProperty("version").GetInt32());
        Assert.Equal("future", root.GetProperty("preset").GetString());
        Assert.Equal(3, root.GetProperty("items").GetProperty("new_badge").GetProperty("mode").GetInt32());
        var year = root.GetProperty("items").GetProperty("year");
        Assert.True(year.GetProperty("enabled").GetBoolean());
        Assert.Equal("next-edge", year.GetProperty("position").GetString());
        Assert.Equal("future", year.GetProperty("accentColor").GetString());
        Assert.Equal(42, year.GetProperty("future").GetInt32());
        Assert.Equal("top-right", root.GetProperty("items").GetProperty("resolution").GetProperty("position").GetString());
        Assert.True(root.GetProperty("style").GetProperty("future").GetBoolean());
        // Every registry item the stored document lacked is written whole, as the official serializer does.
        Assert.Equal("left-edge", root.GetProperty("items").GetProperty("rating_imdb").GetProperty("position").GetString());
    }

    [Fact]
    public void RemovingLastValueImageAndResettingLookReallyRemoveTheStoredOverrides()
    {
        var original = $$$$"""{"version":2,"preset":"plate","items":{"resolution":{"image":"{{{{Reference}}}}","valueImages":{"2160p":"{{{{Reference}}}}"}}},"style":{"size":1.5,"future":true}}""";
        var draft = CardOverlayService.ParseEditorDocument(original);
        draft.Items["resolution"] = draft.Items["resolution"] with { Image = null, ValueImages = null };
        draft = draft with { Style = null };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft));
        Assert.False(doc.RootElement.TryGetProperty("style", out _));
        var resolution = doc.RootElement.GetProperty("items").GetProperty("resolution");
        Assert.False(resolution.TryGetProperty("image", out _));
        Assert.False(resolution.TryGetProperty("valueImages", out _));
        Assert.Equal("plate", doc.RootElement.GetProperty("preset").GetString());
    }

    [Fact]
    public void TheAdminDefaultIsWrittenWholeWhateverTheManifestRevision()
    {
        // defaults.card_overlays is stored as any JSON (internal/config NormalizeAdminSetting), and the
        // official page writes serializeOverlayPrefs(next) with edge positions, presets and style intact.
        var draft = CardOverlayService.ParseEditorDocument("");
        draft = draft with { Preset = "band", Style = new() { Size = 1.5 } };
        draft.Items["year"] = draft.Items["year"] with { Enabled = true, Position = OverlayPosition.LeftEdge };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft));
        var root = doc.RootElement;
        Assert.Equal("band", root.GetProperty("preset").GetString());
        Assert.Equal(1.5, root.GetProperty("style").GetProperty("size").GetDouble());
        Assert.Equal("left-edge", root.GetProperty("items").GetProperty("year").GetProperty("position").GetString());
        Assert.True(root.GetProperty("items").GetProperty("availability").TryGetProperty("enabled", out _));
        Assert.True(root.GetProperty("items").GetProperty("resolution_hdr_audio").TryGetProperty("enabled", out _));
    }

    [Fact]
    public void OnlyRatingSourcesGateWhichOverlaysTheAdminPageOffers()
    {
        var none = new HashSet<string>();
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_imdb", none));
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_tmdb", none));
        Assert.False(CardOverlayService.EditorOffersOverlay("rating_rt", none));
        Assert.False(CardOverlayService.EditorOffersOverlay("rating_rt_audience", new HashSet<string> { "rt_critic" }));
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_rt", new HashSet<string> { "rt_critic" }));
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_rt_audience", new HashSet<string> { "rt_audience" }));
        // isOverlayOffered ignores the manifest: availability and the combined badge are always offered.
        Assert.True(CardOverlayService.EditorOffersOverlay("availability", none));
        Assert.True(CardOverlayService.EditorOffersOverlay("resolution_hdr_audio", none));
    }

    [Fact]
    public void LegacyMigrationKeepsOpaqueItemValuesAndFutureBadges()
    {
        var draft = CardOverlayService.ParseEditorDocument("""{"year":{"enabled":false,"position":"future-edge","future":7},"future_badge":{"enabled":true,"position":"future-edge"}}""");
        draft.Items["year"] = draft.Items["year"] with { Enabled = true };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft));
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetProperty("year").GetProperty("enabled").GetBoolean());
        Assert.Equal("future-edge", items.GetProperty("year").GetProperty("position").GetString());
        Assert.Equal(7, items.GetProperty("year").GetProperty("future").GetInt32());
        Assert.True(items.GetProperty("future_badge").GetProperty("enabled").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void PassthroughOverlaysTheEditorDoesNotRenderSurviveAnEdit()
    {
        // Official PASSTHROUGH_IDS: legal in the contract, no registry entry, kept on a round trip.
        var draft = CardOverlayService.ParseEditorDocument("""{"version":2,"preset":"classic","order":["imdb_top_250"],"items":{"imdb_top_250":{"enabled":true,"position":"right-edge"},"rt_certified_fresh":{"enabled":false,"position":"left-edge"}}}""");
        draft.Items["year"] = draft.Items["year"] with { Enabled = true };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft));
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetProperty("imdb_top_250").GetProperty("enabled").GetBoolean());
        Assert.Equal("left-edge", items.GetProperty("rt_certified_fresh").GetProperty("position").GetString());
        Assert.Equal("imdb_top_250", doc.RootElement.GetProperty("order")[0].GetString());
    }

    [Fact]
    public void EditingStyleRetainsOpaqueFieldsAndClearingItsFinalKnownFieldDoesNotResurrectIt()
    {
        var draft = CardOverlayService.ParseEditorDocument("""{"version":2,"preset":"classic","items":{},"style":{"size":1.5,"uppercase":"future","future":{"a":1}}}""");
        draft = draft with { Style = draft.Style! with { Size = null } };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft));
        var style = doc.RootElement.GetProperty("style");
        Assert.False(style.TryGetProperty("size", out _));
        Assert.Equal("future", style.GetProperty("uppercase").GetString());
        Assert.Equal(1, style.GetProperty("future").GetProperty("a").GetInt32());
    }

    [Fact]
    public void TheShowSampleFindsAnImageTheOfficialEditorKeyedByTheCanonicalStatus()
    {
        // The official editor keys show status images "Returning" while the API sends "returning".
        var config = new OverlayItemConfig(true, OverlayPosition.RightEdge) { ValueImages = new Dictionary<string, string> { ["Returning"] = Reference } };
        Assert.Equal(Reference, OverlayRegistry.ResolveValueImage(config, "show_status", OverlayRegistry.ValueKey("show_status", new() { ShowStatus = "returning" })));
        Assert.Null(OverlayRegistry.ResolveValueImage(config, "network", "Returning".ToLowerInvariant()));
    }

    [Fact]
    public async Task UploadUsesExactMultipartContractAndCreatedReceipt()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/admin/overlay-images", request.RequestUri!.AbsolutePath);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            Assert.Equal(2, form.Count());
            var file = form.Single(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "file");
            Assert.Equal("image/png", file.Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 1, 2, 3 }, await file.ReadAsByteArrayAsync(ct));
            Assert.Equal("Badge", await form.Single(p => p != file).ReadAsStringAsync(ct));
            var result = AdminMonitorKit.Json(Receipt(), HttpStatusCode.Created);
            result.Headers.Location = new Uri("/api/v2/admin/overlay-images/" + Reference, UriKind.Relative);
            return result;
        };
        var image = await new AdminAppearanceApi(kit.Client).UploadOverlayImageAsync(lease, [1, 2, 3], "image/png", " Badge ");
        Assert.Equal(Reference, image.Ref);
        Assert.Equal(300, image.Width);
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(401)]
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task UploadNeverReplaysAndDoesNotAcceptGenericSuccess(int status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Receipt(), (HttpStatusCode)status));
        var error = await Record.ExceptionAsync(() => new AdminAppearanceApi(kit.Client).UploadOverlayImageAsync(lease, [1], "image/png", "Badge"));
        Assert.NotNull(error);
        Assert.Single(kit.Http.Requests);
        if (status != 401) Assert.IsType<AdminAppearanceOutcomeUnknownException>(error);
    }

    [Theory]
    [InlineData("image/svg+xml", 1, "Badge")]
    [InlineData("image/png", 4194305, "Badge")]
    [InlineData("image/png", 0, "Badge")]
    [InlineData("image/png", 1, " ")]
    public async Task InvalidUploadNeverSends(string mediaType, int bytes, string name)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminAppearanceApi(kit.Client).UploadOverlayImageAsync(lease, new byte[bytes], mediaType, name));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task NamesCountUnicodeCodePointsAndMalformedAcknowledgementsAreUnknown()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var name = string.Concat(Enumerable.Repeat("\U0001F3AC", 64));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Receipt(name, width: 601), HttpStatusCode.Created));
        await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => new AdminAppearanceApi(kit.Client).UploadOverlayImageAsync(lease, [1], "image/webp", name));
        Assert.Single(kit.Http.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminAppearanceApi(kit.Client).UploadOverlayImageAsync(lease, [1], "image/webp", name + "x"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task DeleteRequiresExact204AndValidReferenceWithoutReplay()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminAppearanceApi(kit.Client);
        await Assert.ThrowsAsync<ArgumentException>(() => api.DeleteOverlayImageAsync(lease, "../other"));
        Assert.Empty(kit.Http.Requests);
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => api.DeleteOverlayImageAsync(lease, Reference));
        Assert.Equal("DELETE /api/v2/admin/overlay-images/" + Reference, kit.Http.Requests.Single());
    }

    [Fact]
    public async Task Delete204AndConfigRefreshRemainSeparateCallsAndExposeUploadCapability()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : AdminMonitorKit.Json("""{"enabled":true,"images":[],"image_uploads_available":true}"""));
        var api = new AdminAppearanceApi(kit.Client);
        await api.DeleteOverlayImageAsync(lease, Reference);
        Assert.Single(kit.Http.Requests);
        var refreshed = await api.GetOverlayConfigAsync(lease);
        Assert.Empty(refreshed.Images);
        Assert.True(refreshed.ImageUploadsAvailable);
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.Equal("GET /api/v2/settings/overlay-config", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task OversizedOrUnrelatedUploadAcknowledgementsCannotBecomeSuccess()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminAppearanceApi(kit.Client);
        foreach (var body in new[] { new string(' ', 16 * 1024 + 1), Receipt(url: "https://outside.invalid/private"), Receipt(name: "Other image"), Receipt() })
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body, HttpStatusCode.Created));
            // No matching Location, over-limit content, wrong URL or wrong name
            // must all surface a sanitized unknown outcome, never a resend.
            var error = await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => api.UploadOverlayImageAsync(lease, [1], "image/png", "Badge"));
            Assert.DoesNotContain("outside.invalid", error.ToString());
        }
        Assert.Equal(4, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task AlreadyCancelledIntentDoesNotSendOrBecomeUnknown()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var api = new AdminAppearanceApi(kit.Client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UploadOverlayImageAsync(lease, [1], "image/png", "Badge", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.DeleteOverlayImageAsync(lease, Reference, cancellation.Token));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task LostRepliesAndLateAuthorityCannotReturnSuccessOrLeakServerDetails()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminAppearanceApi(kit.Client);
        kit.Http.Respond = (_, _) => throw new HttpRequestException("private-signed-location");
        var unknown = await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => api.UploadOverlayImageAsync(lease, [1], "image/png", "Badge"));
        Assert.DoesNotContain("private-signed", unknown.ToString());
        Assert.Single(kit.Http.Requests);
        kit.Http.Respond = (_, _) => { authority.Retire(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.DeleteOverlayImageAsync(lease, Reference));
        Assert.Equal(2, kit.Http.Requests.Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.DeleteOverlayImageAsync(lease, Reference));
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public void TheAppWideOverlayRefreshAfterASaveIsNotCancelledWithThePage()
    {
        // Leaving the page retires its lease. A refresh cancelled after its invalidation leaves the service
        // unloaded and every badge in the app as text, because only the announced change reloads them.
        var page = File.ReadAllText(Path.Combine(SourceRoot(), "src", "SiloPlayer", "Views", "Admin", "AdminAppearanceSettingsPage.cs"));
        Assert.Contains("_overlays.RefreshPreferencesAsync(CancellationToken.None)", page);
        Assert.DoesNotContain("RefreshPreferencesAsync(action.Token)", page);
    }

    // Bind the source assertion to this compilation's checkout even when the test DLL runs from external build outputs.
    private static string SourceRoot([CallerFilePath] string file = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
