using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MarkerProvenanceTests
{
    [Fact]
    public async Task ActualWatchReadRetainsCurrentWireRangeAndProvenance()
    {
        using var http = new HttpClient(new Wire());
        var client = new SiloApiClient(http); client.SetBaseUrl("https://marker-fixture.invalid");
        var detail = await new PlaybackApi(client).GetWatchDetailAsync("episode");
        var intro = detail.Versions.Single().Intro!;
        Assert.Equal(5, intro.Start); Assert.Equal(30, intro.End);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(intro, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        Assert.Equal("plugin", json.RootElement.GetProperty("source").GetString());
        Assert.Equal(.92, json.RootElement.GetProperty("confidence").GetDouble());
    }

    [Theory]
    [InlineData(null, "scanner", null, "Detected")]
    [InlineData(.915, "plugin", "plugin:7:introdb", "92% · TheIntroDB")]
    [InlineData(0d, "manual", null, "0% · Edited")]
    [InlineData(2d, "online", "new-provider", "100% · new-provider")]
    [InlineData(-1d, "online", null, "0% · Online")]
    [InlineData(null, null, "built_in", "Detected")]
    [InlineData(null, null, null, null)]
    public void LabelsMatchOfficialVocabulary(double? confidence, string? source, string? provider, string? expected)
        => Assert.Equal(expected, MarkerProvenance.Label(new() { Confidence = confidence, Source = source, Provider = provider }, true, true));

    [Fact]
    public void AbsentAndNonfiniteConfidenceStayAbsentAndFlagsAreIndependent()
    {
        Assert.Null(MarkerProvenance.Label(new() { Source = "scanner" }, true, false));
        Assert.Null(MarkerProvenance.Label(new() { Confidence = double.NaN }, true, false));
        Assert.Null(MarkerProvenance.Label(new() { Confidence = double.PositiveInfinity }, true, false));
        Assert.Null(MarkerProvenance.Label(new() { Confidence = .95, Source = "scanner" }, false, false));
        Assert.Equal("95%", MarkerProvenance.Label(new() { Confidence = .95, Source = "scanner" }, true, false));
    }

    [Fact]
    public void RepeatedOccurrenceAndLiveReplacementPreserveMetadataWithoutInventingConfidence()
    {
        var version = new FileVersion { MarkerSegments = [new() { Kind = "intro", StartSeconds = 30, EndSeconds = 40 }] };
        using var update = JsonDocument.Parse("""{"intro":{"start":0,"end":10,"source":"plugin","provider":"introdb","confidence":0.8,"algorithm":"v4"}}""");
        PlaybackMarkerUpdate.Apply(version, update.RootElement);
        var repeated = PlaybackMarkerRanges.Active(version.MarkerSegments, "intro", 35, version.Intro)!;
        Assert.Equal(30, repeated.Start); Assert.Equal("80% · TheIntroDB", MarkerProvenance.Label(repeated, true, true));
        using var edited = JsonDocument.Parse("""{"intro":{"start_seconds":0,"end_seconds":10,"source":"manual"}}""");
        PlaybackMarkerUpdate.Apply(version, edited.RootElement);
        Assert.Null(version.Intro!.Confidence); Assert.Equal("Edited", MarkerProvenance.Label(version.Intro, true, true));
        using var withdrawn = JsonDocument.Parse("""{"intro":null,"marker_segments":[]}""");
        PlaybackMarkerUpdate.Apply(version, withdrawn.RootElement);
        Assert.Null(version.Intro); Assert.Empty(version.MarkerSegments!);
    }

    [Theory]
    [InlineData(23, true, false)]
    [InlineData(24, false, false)]
    [InlineData(24, true, true)]
    public async Task CapabilityGatesEffectiveRequests(int revision, bool batched, bool expected)
    {
        using var handler = new SettingsWire(revision, batched); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://marker-settings.invalid");
        var preferences = await new SettingsApi(client).GetMarkerDetailsPreferencesAsync(client.CaptureContext());
        Assert.Equal(expected, preferences.Supported); Assert.Equal(expected ? 1 : 0, handler.EffectiveRequests);
        Assert.Equal(expected, preferences.ShowConfidence); Assert.Equal(expected, preferences.ShowSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredProfileNeverAcceptsDelayedSettings(bool holdEffective)
    {
        using var handler = new DelayedSettings(holdEffective); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://marker-owner.invalid"); client.SetProfile("before");
        var read = new SettingsApi(client).GetMarkerDetailsPreferencesAsync(client.CaptureContext());
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.SetProfile("after"); handler.Release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(holdEffective ? 1 : 0, handler.EffectiveReads);
    }

    private sealed class DelayedSettings(bool holdEffective) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int EffectiveReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var effective = request.RequestUri!.AbsolutePath.EndsWith("/effective");
            if (effective) EffectiveReads++;
            if (effective == holdEffective) { Started.SetResult(); await Release.Task; }
            return new(HttpStatusCode.OK) { Content = new StringContent(effective
                ? """{"items":[{"key":"playback.show_marker_confidence","value":true},{"key":"playback.show_marker_source","value":true}]}"""
                : """{"api_version":1,"manifest_revision":24,"supports_batched_effective":true,"supports_idempotent_writes":true}""") };
        }
    }

    private sealed class SettingsWire(int revision, bool batched) : HttpMessageHandler
    {
        internal int EffectiveRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("/capabilities")
                ? JsonSerializer.Serialize(new { api_version = 1, manifest_revision = revision, supports_batched_effective = batched, supports_idempotent_writes = true })
                : """{"items":[{"key":"playback.show_marker_confidence","value":true,"source":"profile"},{"key":"playback.show_marker_source","value":true,"source":"profile_device"}]}""";
            if (!request.RequestUri.AbsolutePath.EndsWith("/capabilities")) EffectiveRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"content_id":"episode","versions":[{"file_id":"7","intro":{"start_seconds":5,"end_seconds":30,"source":"plugin","provider":"plugin:3:introdb","confidence":0.92,"algorithm":"introdb:v4"}}]}""") });
    }
}
