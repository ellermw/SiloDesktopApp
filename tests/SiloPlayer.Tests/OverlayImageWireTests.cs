using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class OverlayImageWireTests
{
    [Theory]
    [InlineData(false, 26)]
    [InlineData(true, 24)]
    public async Task ImagesNeverLoadWithoutCapabilityAndRevision(bool supported, int revision)
    {
        using var wire = new Wire { Supports = supported, Revision = revision };
        var service = await wire.LoadAsync();
        Assert.False(service.SupportsImages);
        Assert.Empty(service.Images);
        Assert.Null(await service.GetImageAsync(wire.Ref));
        Assert.Equal(0, wire.ImageReads);
    }

    [Fact]
    public async Task LibraryIgnoresUntrustedUrlsAndParallelCardsShareOneWireRead()
    {
        using var wire = new Wire();
        var service = await wire.LoadAsync();
        Assert.Single(service.Images);
        var first = service.GetImageAsync(wire.Ref);
        var second = service.GetImageAsync(wire.Ref);
        Assert.Same(first, second);
        Assert.Equal(wire.Bytes, await first);
        Assert.Equal(1, wire.ImageReads);
        Assert.Equal("/api/v2/overlay-images/" + wire.Ref, wire.LastImagePath);
        Assert.True(wire.SawProfileHeader);
        Assert.Equal(wire.Bytes, await service.GetImageAsync(wire.Ref));
        Assert.Equal(1, wire.ImageReads);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("https://outside.invalid/image.webp")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.webp")]
    public async Task InvalidRefsNeverReachWire(string reference)
    {
        using var wire = new Wire(); var service = await wire.LoadAsync();
        Assert.Null(await service.GetImageAsync(reference)); Assert.Equal(0, wire.ImageReads);
        await Assert.ThrowsAsync<ArgumentException>(() => wire.Client.GetOverlayImageAsync(reference, wire.Client.CaptureContext()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unauthorized")]
    [InlineData("oversize")]
    [InlineData("chunked")]
    [InlineData("wrong-type")]
    [InlineData("wrong-hash")]
    public async Task UnavailableImagesFallBackWithoutBreakingPreferences(string failure)
    {
        using var wire = new Wire { Failure = failure }; var service = await wire.LoadAsync();
        Assert.Null(await service.GetImageAsync(wire.Ref));
        Assert.True(service.Enabled); Assert.NotNull(service.GetPrefs());
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("unavailable")]
    public async Task TransientFailureIsRetriedByTheNextReadWithoutAPreferenceRefresh(string failure)
    {
        using var wire = new Wire { Failure = failure }; var service = await wire.LoadAsync();
        Assert.Null(await service.GetImageAsync(wire.Ref));
        var failedReads = wire.ImageReads;
        wire.Failure = null;
        Assert.Equal(wire.Bytes, await service.GetImageAsync(wire.Ref));
        Assert.Equal(failedReads + 1, wire.ImageReads);
        Assert.Equal(wire.Bytes, await service.GetImageAsync(wire.Ref));
        Assert.Equal(failedReads + 1, wire.ImageReads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    [InlineData("wrong-hash")]
    public async Task DefinitiveServerAnswerStaysCached(string failure)
    {
        using var wire = new Wire { Failure = failure }; var service = await wire.LoadAsync();
        Assert.Null(await service.GetImageAsync(wire.Ref));
        var reads = wire.ImageReads;
        Assert.Null(await service.GetImageAsync(wire.Ref));
        Assert.Equal(reads, wire.ImageReads);
    }

    [Fact]
    public async Task ReconnectCatchUpKeepsImageOwnerAndImagesAvailable()
    {
        using var wire = new Wire(); var service = await wire.LoadAsync();
        var owner = service.CaptureImageOwner();
        Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _, transportCatchUp: true));
        Assert.True(service.IsCurrentImageOwner(owner));
        Assert.Equal(wire.Bytes, await service.GetImageAsync(wire.Ref));
        Assert.Equal(1, wire.ImageReads);
        wire.Client.SetProfile("next-fixture-profile", "next-synthetic-token");
        Assert.False(service.IsCurrentImageOwner(owner));
    }

    [Fact]
    public async Task ReconnectCatchUpDuringAHeldReadStillDeliversTheImage()
    {
        using var wire = new Wire(); var service = await wire.LoadAsync();
        wire.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = service.GetImageAsync(wire.Ref);
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _, transportCatchUp: true));
        wire.Hold.TrySetResult();
        Assert.Equal(wire.Bytes, await held);
        Assert.Equal(wire.Bytes, await service.GetImageAsync(wire.Ref));
        Assert.Equal(1, wire.ImageReads);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("server")]
    public async Task OwnerChangeRetiresHeldImageAndCache(string change)
    {
        using var wire = new Wire(); var service = await wire.LoadAsync();
        wire.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = service.GetImageAsync(wire.Ref);
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (change == "profile") wire.Client.SetProfile("next-fixture-profile", "next-synthetic-token");
        else if (change == "account") wire.Client.BeginAuthenticationSession("next-synthetic-account-token");
        else wire.Client.SetBaseUrl("https://next-fixture.invalid");
        wire.Hold.TrySetResult();
        Assert.Null(await old);
        Assert.Null(await service.GetImageAsync(wire.Ref));
        Assert.Equal(1, wire.ImageReads);
        wire.Hold = null;
        await service.RefreshPreferencesAsync();
        Assert.Equal(wire.Bytes, await service.GetImageAsync(wire.Ref));
        Assert.Equal(2, wire.ImageReads);
    }

    [Theory]
    [InlineData("resolution", "2160p")]
    [InlineData("resolution_hdr", "2160p DV HDR10")]
    [InlineData("resolution_hdr_audio", "2160p DV HDR10 Atmos")]
    [InlineData("original_language", "en")]
    [InlineData("show_status", "in_production")]
    public void ValueImagesMatchRawApiKeysRatherThanDisplayText(string id, string expected)
    {
        Assert.Equal(expected, OverlayRegistry.ValueKey(id, new OverlayData { Resolution = "2160p", Hdr = "DV HDR10", Audio = "Atmos", OriginalLanguage = "en", ShowStatus = "in_production" }));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    public async Task StyleAndImageEditsRespectRevisionWithoutDroppingOpaqueFields(int revision)
    {
        using var wire = new Wire { Revision = revision, Document = """{"version":2,"preset":"classic","future":7,"style":{"size":1.25,"futureLook":9},"items":{"resolution":{"enabled":true,"image":"stored.webp","valueImages":{"2160p":"old.webp","future":7},"futureItem":8}}}""" };
        var service = await wire.LoadAsync(); var draft = service.GetDocument();
        draft = draft with { Style = draft.Style! with { Size = 2 } };
        draft.Items["resolution"] = draft.Items["resolution"] with { Image = wire.Ref, ValueImages = new Dictionary<string, string> { ["2160p"] = wire.Ref } };
        if (revision < 25)
        {
            // The look and images cannot be written below revision 25. The save reports
            // that, and an edit the manifest does support still keeps every stored field.
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(draft));
            Assert.Contains("server manifest does not support", error.Message);
            Assert.Null(wire.Saved);
            draft = service.GetDocument();
            draft.Items["resolution"] = draft.Items["resolution"] with { Enabled = false };
        }
        await service.SaveAsync(draft);
        using var json = JsonDocument.Parse(wire.Saved!); var root = json.RootElement;
        Assert.Equal(7, root.GetProperty("future").GetInt32()); Assert.Equal(9, root.GetProperty("style").GetProperty("futureLook").GetInt32());
        Assert.Equal(revision >= 25 ? 2 : 1.25, root.GetProperty("style").GetProperty("size").GetDouble());
        var config = root.GetProperty("items").GetProperty("resolution");
        Assert.Equal(8, config.GetProperty("futureItem").GetInt32());
        Assert.Equal(revision >= 25, config.GetProperty("enabled").GetBoolean());
        Assert.Equal(revision >= 25 ? wire.Ref : "stored.webp", config.GetProperty("image").GetString());
        Assert.Equal(7, config.GetProperty("valueImages").GetProperty("future").GetInt32());
    }

    [Theory]
    [InlineData("style")]
    [InlineData("preset")]
    [InlineData("image")]
    [InlineData("value-image")]
    public async Task DraftFromANewerManifestIsNotReportedSavedOnceTheManifestCannotStoreIt(string edit)
    {
        using var wire = new Wire { Document = """{"version":2,"preset":"classic","style":{"size":1.25},"items":{"resolution":{"enabled":true,"image":"stored.webp"}}}""" };
        var service = await wire.LoadAsync(); var draft = service.GetDocument();
        if (edit == "style") draft = draft with { Style = draft.Style! with { Size = 2 } };
        else if (edit == "preset") draft = draft with { Preset = "band" };
        else draft.Items["resolution"] = edit == "image" ? draft.Items["resolution"] with { Image = wire.Ref }
            : draft.Items["resolution"] with { ValueImages = new Dictionary<string, string> { ["2160p"] = wire.Ref } };
        // The server now advertises revision 24, and reconnect catch-up makes the save ask again.
        wire.Revision = 24;
        Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _, transportCatchUp: true));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(draft));
        Assert.Contains("server manifest does not support", error.Message);
        Assert.Null(wire.Saved);
        Assert.Equal(24, service.ManifestRevision);
    }

    [Theory]
    [InlineData(" returning ")]
    [InlineData("Future State")]
    public void UnknownStatusImageKeyRetainsExactApiValue(string value)
        => Assert.Equal(value, OverlayRegistry.ValueKey("show_status", new OverlayData { ShowStatus = value }));

    [Theory]
    [InlineData(" returning ", "Returning")]
    [InlineData("IN_PRODUCTION", "Returning")]
    [InlineData(" Ended", "Ended")]
    [InlineData("   ", null)]
    public void StatusLabelNormalizesPaddingAndCaseWhileTheImageKeyStaysRaw(string value, string? label)
    {
        var definition = Assert.Single(OverlayRegistry.All, d => d.Id == "show_status");
        var data = new OverlayData { ShowStatus = value };
        Assert.Equal(label, definition.GetValue(data));
        Assert.Equal(value, OverlayRegistry.ValueKey("show_status", data));
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal byte[] Bytes = Encoding.ASCII.GetBytes("RIFF-overlay-fixture-WEBP");
        internal string Ref => Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant() + ".webp";
        internal bool Supports = true, SawProfileHeader;
        internal int Revision = 26, ImageReads;
        internal string? Failure, LastImagePath, Saved;
        internal string Document = """{"version":2,"preset":"plate","items":{}}""";
        internal TaskCompletionSource? Hold;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SiloApiClient Client = null!;
        internal async Task<CardOverlayService> LoadAsync()
        {
            Client = new(new HttpClient(this, false)); Client.SetBaseUrl("https://fixture.invalid"); Client.SetProfile("synthetic-profile", "synthetic-fixture-token");
            var service = new CardOverlayService(new SettingsApi(Client)); await service.EnsureLoadedAsync(); return service;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/v2/overlay-images/"))
            {
                ImageReads++; LastImagePath = path; SawProfileHeader = request.Headers.Contains("X-Profile-Token"); Entered.TrySetResult();
                if (Hold is not null) await Hold.Task;
                var response = new HttpResponseMessage(Failure == "missing" ? HttpStatusCode.NotFound : Failure == "unauthorized" ? HttpStatusCode.Unauthorized
                    : Failure == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
                var bytes = Failure is "oversize" or "chunked" ? new byte[SiloApiClient.MaxOverlayImageBytes + 1] : Failure == "wrong-hash" ? new byte[] { 0 } : Bytes;
                response.Content = Failure == "chunked" ? new StreamContent(new ChunkedStream(bytes)) : new ByteArrayContent(bytes);
                response.Content.Headers.ContentType = new(Failure == "wrong-type" ? "text/html" : "image/webp"); return response;
            }
            object body;
            if (request.Method == HttpMethod.Put)
            { using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Saved = payload.RootElement.GetProperty("value").GetRawText(); body = new { }; }
            else if (path.EndsWith("/contract/capabilities")) body = new { api_version = 1, manifest_revision = Revision, supports_overlay_images = Supports, supports_batched_effective = true };
            else if (path.EndsWith("/overlay-config")) body = new { enabled = true, images = new[] { new { @ref = Ref, name = "Fixture", width = 40, height = 20, url = "https://outside.invalid/never-fetch" }, new { @ref = "../invalid", name = "Invalid", width = 9999, height = 9999, url = "" } } };
            else if (path.EndsWith("/values/ui.card_overlays")) body = new { key = "ui.card_overlays", value = JsonSerializer.Deserialize<JsonElement>(Document), scope = "profile" };
            else if (path.EndsWith("/effective")) body = new { items = Array.Empty<object>() };
            else body = new { };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ChunkedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

