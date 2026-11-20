using System.Net;
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

    [Fact]
    public void BuiltInsComeFromTheOfficialRegistryIncludingEdgeAndAdvisoryDefaults()
    {
        var defaults = CardOverlayService.ParseEditorDocument(CardOverlayService.BuiltInEditorDefaults);
        Assert.Equal(OverlayRegistry.All.Select(d => d.Id), defaults.Items.Keys);
        Assert.Equal(OverlayPosition.LeftEdge, defaults.Items["rating_imdb"].Position);
        Assert.Equal(OverlayPosition.RightEdge, defaults.Items["show_status"].Position);
        Assert.Equal(OverlayPosition.BottomRight, defaults.Items["advisory_age"].Position);
        Assert.Equal("classic", defaults.Preset);
        Assert.Empty(defaults.Order);
        Assert.Equal(CardOverlayService.BuiltInEditorDefaults, CardOverlayService.SerializeEditorDocument(defaults, 29));
    }

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
        using var document = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft, 29));
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
    }

    [Fact]
    public void RemovingLastValueImageAndResettingLookReallyRemoveTheStoredOverrides()
    {
        var original = $$"""{"version":2,"preset":"plate","items":{"resolution":{"image":"{{Reference}}","valueImages":{"2160p":"{{Reference}}"}}},"style":{"size":1.5,"future":true}}""";
        var draft = CardOverlayService.ParseEditorDocument(original);
        draft.Items["resolution"] = draft.Items["resolution"] with { Image = null, ValueImages = new Dictionary<string, string>() };
        draft = draft with { Style = null };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft, 29));
        Assert.False(doc.RootElement.TryGetProperty("style", out _));
        var resolution = doc.RootElement.GetProperty("items").GetProperty("resolution");
        Assert.False(resolution.TryGetProperty("image", out _));
        Assert.False(resolution.TryGetProperty("valueImages", out _));
    }

    [Fact]
    public void OldManifestPreservesStoredNewerFieldsWithoutInventingUnsupportedOnes()
    {
        var draft = CardOverlayService.ParseEditorDocument("""{"version":2,"preset":"band","items":{"year":{"enabled":false},"availability":{"enabled":true}},"style":{"size":1.5}}""");
        draft.Items["year"] = draft.Items["year"] with { Enabled = true };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft, 24));
        Assert.Equal("band", doc.RootElement.GetProperty("preset").GetString());
        Assert.Equal(1.5, doc.RootElement.GetProperty("style").GetProperty("size").GetDouble());
        Assert.True(doc.RootElement.GetProperty("items").GetProperty("availability").GetProperty("enabled").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("items").TryGetProperty("resolution_hdr_audio", out _));
    }

    [Fact]
    public void FreshDefaultsForOldManifestContainOnlySupportedPositionsIdsAndFields()
    {
        using var doc = JsonDocument.Parse(CardOverlayService.BuiltInEditorDefaultsForRevision(24));
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("style", out _));
        var items = root.GetProperty("items");
        Assert.False(items.TryGetProperty("availability", out _));
        Assert.False(items.TryGetProperty("resolution_hdr_audio", out _));
        Assert.All(items.EnumerateObject(), pair => Assert.Contains(pair.Value.GetProperty("position").GetString(), new[] { "top-left", "top-right", "bottom-left", "bottom-right" }));
        Assert.Equal("top-left", items.GetProperty("rating_imdb").GetProperty("position").GetString());
    }

    [Theory]
    [InlineData("returning series", "Returning")]
    [InlineData("planned", "Upcoming")]
    [InlineData("canceled", "Cancelled")]
    public void ValueImageKeysUseTheOfficialCanonicalShowStatus(string value, string key)
        => Assert.Equal(key, OverlayRegistry.ValueKey("show_status", new() { ShowStatus = value }));

    [Fact]
    public void RatingAndManifestGatesMatchThePinnedRegistry()
    {
        var none = new HashSet<string>();
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_imdb", 29, none));
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_tmdb", 29, none));
        Assert.False(CardOverlayService.EditorOffersOverlay("rating_rt", 29, none));
        Assert.True(CardOverlayService.EditorOffersOverlay("rating_rt", 29, new HashSet<string> { "rt_critic" }));
        Assert.False(CardOverlayService.EditorOffersOverlay("resolution_hdr_audio", 25, none));
        Assert.False(CardOverlayService.EditorOffersOverlay("availability", 28, none));
        Assert.True(CardOverlayService.EditorOffersOverlay("availability", 29, none));
    }

    [Fact]
    public void LegacyMigrationKeepsOpaqueItemValuesAndFutureBadges()
    {
        var draft = CardOverlayService.ParseEditorDocument("""{"year":{"enabled":false,"position":"future-edge","future":7},"future_badge":{"enabled":true,"position":"future-edge"}}""");
        draft.Items["year"] = draft.Items["year"] with { Enabled = true };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft, 29));
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetProperty("year").GetProperty("enabled").GetBoolean());
        Assert.Equal("future-edge", items.GetProperty("year").GetProperty("position").GetString());
        Assert.Equal(7, items.GetProperty("year").GetProperty("future").GetInt32());
        Assert.True(items.GetProperty("future_badge").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void EditingStyleRetainsOpaqueFieldsAndClearingItsFinalKnownFieldDoesNotResurrectIt()
    {
        var draft = CardOverlayService.ParseEditorDocument("""{"version":2,"preset":"classic","items":{},"style":{"size":1.5,"uppercase":"future","future":{"a":1}}}""");
        draft = draft with { Style = draft.Style! with { Size = null } };
        using var doc = JsonDocument.Parse(CardOverlayService.SerializeEditorDocument(draft, 29));
        var style = doc.RootElement.GetProperty("style");
        Assert.False(style.TryGetProperty("size", out _));
        Assert.Equal("future", style.GetProperty("uppercase").GetString());
        Assert.Equal(1, style.GetProperty("future").GetProperty("a").GetInt32());
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
}
