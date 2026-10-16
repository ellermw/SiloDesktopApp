using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class EpisodeSpoilerTests
{
    [Theory]
    [InlineData(false, false, 0, true)]
    [InlineData(true, false, 0, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, false, 1, false)]
    public void OnlyUnstartedEpisodesAreUnwatched(bool played, bool inProgress, double position, bool expected)
        => Assert.Equal(expected, EpisodeSpoilerPolicy.IsUnwatched(played, inProgress, position));

    [Fact]
    public void MissingStateIsUnwatchedAndUnknownStillProvenanceIsProtected()
    {
        Assert.True(EpisodeSpoilerPolicy.IsUnwatched(null, null, null));
        Assert.True(EpisodeSpoilerPolicy.HideImage("episode", null, null, null, null, true));
        Assert.False(EpisodeSpoilerPolicy.HideImage("episode", false, null, null, null, true));
        Assert.False(EpisodeSpoilerPolicy.HideImage("movie", true, null, null, null, true));
        Assert.False(EpisodeSpoilerPolicy.HideImage("episode", true, false, true, null, true));
    }

    [Fact]
    public async Task OldManifestSkipsUnsupportedEffectiveRead()
    {
        var wire = new Wire { Revision = 16 };
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://example.test");
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client));
        await prefs.EnsureLoadedAsync();
        Assert.False(prefs.HideImages); Assert.False(prefs.HideOverviews); Assert.Equal(0, wire.EffectiveReads);
    }

    [Fact]
    public async Task ValuesAreProfileScopedAndSharedAcrossConcurrentCardLoads()
    {
        var wire = new Wire();
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://example.test"); client.SetProfile("one");
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => prefs.EnsureLoadedAsync()));
        Assert.True(prefs.HideImages); Assert.True(prefs.HideOverviews); Assert.Equal(1, wire.EffectiveReads);
        client.SetProfile("two");
        Assert.False(prefs.HideImages); Assert.False(prefs.HideOverviews);
        wire.Hide = false; await prefs.EnsureLoadedAsync();
        Assert.False(prefs.HideImages); Assert.Equal(2, wire.EffectiveReads);
    }

    [Fact]
    public async Task RetiredProfilePreferenceCompletionCannotReplaceCurrentValues()
    {
        var wire = new Wire { Hold = true };
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://example.test"); client.SetProfile("one");
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client));
        var load = prefs.EnsureLoadedAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.SetProfile("two"); wire.Release.TrySetResult(); await load;
        Assert.False(prefs.HideImages); Assert.False(prefs.HideOverviews);
    }

    [Fact]
    public async Task InvalidationKeepsProtectionUntilReplacementValuesArrive()
    {
        var wire = new Wire();
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://example.test");
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client));
        await prefs.EnsureLoadedAsync();
        wire.Hold = true; prefs.Invalidate();
        var refresh = prefs.EnsureLoadedAsync();
        Assert.True(prefs.HideImages);
        wire.Release.TrySetResult(); await refresh;
        wire.Revision = 16; prefs.Invalidate(); await prefs.EnsureLoadedAsync();
        Assert.False(prefs.HideImages);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Revision = 17; public bool Hide = true; public bool Hold; public int EffectiveReads;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.EndsWith("capabilities")) json = "{\"api_version\":1,\"manifest_revision\":" + Revision + ",\"supports_batched_effective\":true}";
            else
            {
                EffectiveReads++; Started.TrySetResult(); if (Hold) await Release.Task;
                var value = Hide ? "true" : "false";
                json = "{\"items\":[{\"key\":\"catalog.hide_unwatched_episode_images\",\"value\":" + value + "},{\"key\":\"catalog.hide_unwatched_episode_overviews\",\"value\":" + value + "}]}";
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
