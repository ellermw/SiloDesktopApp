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
    public void SettingsOnlyOffersEpisodeSpoilersWhenTheDetailReaderSupportsThem()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs"));
        Assert.Contains("EpisodeSpoilersSupported = capability.ApiVersion == 1 && capability.Revision >= 17 && capability.SupportsBatchedEffective;", source);
    }

    [Theory]
    [InlineData(1, 16, false, false)]
    [InlineData(1, 16, true, false)]
    [InlineData(1, 17, false, false)]
    [InlineData(1, 17, true, true)]
    [InlineData(1, 18, false, false)]
    [InlineData(1, 18, true, true)]
    [InlineData(0, 17, true, false)]
    [InlineData(2, 17, true, false)]
    public async Task CapabilityMatrixOnlyReadsSupportedEpisodeSpoilerPreferences(int apiVersion, int revision, bool supportsBatchedEffective, bool supported)
    {
        using var wire = new Wire { ApiVersion = apiVersion, Revision = revision, SupportsBatchedEffective = supportsBatchedEffective };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test");
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client));
        await prefs.EnsureLoadedAsync();
        Assert.True(prefs.IsLoadedForCurrentContext);
        Assert.Equal(supported, prefs.HideImages);
        Assert.Equal(supported, prefs.HideOverviews);
        Assert.Equal(supported, prefs.ShouldHideOverviews);
        Assert.Equal(supported ? 1 : 0, wire.EffectiveReads);
        Assert.Equal(1, wire.CapabilityReads);
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

    public static IEnumerable<object[]> OptionalReadFailures()
    {
        foreach (var failure in new[] { "404", "503", "timeout", "network", "malformed", "null" })
        foreach (var effective in new[] { false, true })
        foreach (var hide in new[] { false, true })
            yield return new object[] { failure, effective, hide };
    }

    [Theory]
    [MemberData(nameof(OptionalReadFailures))]
    public async Task FailedOptionalReadShowsDefaultAndRecoversInSameContext(string failure, bool effective, bool hide)
    {
        using var wire = new Wire { Failure = failure, FailEffective = effective, Hide = hide };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test");
        var clock = new Clock();
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client), clock);
        var changes = 0; prefs.Changed += (_, _) => changes++;
        Assert.True(prefs.ShouldHideOverviews);
        await prefs.EnsureLoadedAsync();
        Assert.False(prefs.IsLoadedForCurrentContext);
        Assert.False(prefs.ShouldHideOverviews);
        Assert.Equal(1, changes);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => prefs.EnsureLoadedAsync()));
        Assert.Equal(1, wire.CapabilityReads);

        clock.Advance(TimeSpan.FromSeconds(1)); wire.Failure = null; wire.Hold = true;
        var recovery = prefs.EnsureLoadedAsync();
        Assert.False(prefs.ShouldHideOverviews); // A retry keeps the failed optional read's visible default.
        var consumers = Enumerable.Range(0, 20).Select(_ => prefs.EnsureLoadedAsync()).ToArray();
        wire.Release.TrySetResult();
        await Task.WhenAll(consumers.Append(recovery));
        Assert.True(prefs.IsLoadedForCurrentContext);
        Assert.Equal(hide, prefs.ShouldHideOverviews);
        Assert.Equal(hide, prefs.HideOverviews);
        Assert.Equal(2, changes);
        Assert.Equal(2, wire.CapabilityReads);
    }

    [Theory]
    [MemberData(nameof(OptionalReadFailures))]
    public async Task FailedReloadKeepsKnownPreferenceUntilSuccessfulRecovery(string failure, bool effective, bool hide)
    {
        using var wire = new Wire { Hide = hide };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test");
        var clock = new Clock();
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client), clock);
        await prefs.EnsureLoadedAsync();
        wire.Failure = failure; wire.FailEffective = effective; wire.Hide = !hide;
        prefs.Invalidate();
        Assert.True(prefs.ShouldHideOverviews);
        await prefs.EnsureLoadedAsync();
        Assert.False(prefs.IsLoadedForCurrentContext);
        Assert.Equal(hide, prefs.ShouldHideOverviews);
        clock.Advance(TimeSpan.FromSeconds(1)); wire.Failure = null;
        await prefs.EnsureLoadedAsync();
        Assert.True(prefs.IsLoadedForCurrentContext);
        Assert.Equal(!hide, prefs.ShouldHideOverviews);
    }

    [Theory]
    [InlineData("access", true)] [InlineData("same-profile", true)]
    [InlineData("profile", false)] [InlineData("authentication", false)] [InlineData("server", false)]
    public async Task FailedNewScopeKeepsKnownHideOnlyForSameIdentity(string change, bool expected)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test"); client.SetProfile("one");
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client));
        await prefs.EnsureLoadedAsync();
        if (change == "access") client.InvalidateAccessContext();
        else if (change == "same-profile") client.SetProfile("one");
        else if (change == "profile") client.SetProfile("two");
        else if (change == "authentication") { client.BeginAuthenticationSession(null); client.SetProfile("one"); }
        else { client.SetBaseUrl("https://other.test"); client.SetProfile("one"); }
        wire.Failure = "503";
        Assert.True(prefs.ShouldHideOverviews);
        Assert.Equal(expected, prefs.HideImages);
        Assert.Equal(expected, prefs.HideOverviews);
        await prefs.EnsureLoadedAsync();
        Assert.Equal(expected, prefs.ShouldHideOverviews);
        Assert.Equal(expected, prefs.HideImages);
        Assert.Equal(expected, prefs.HideOverviews);
    }

    [Fact]
    public async Task PersistentFailuresUseCappedBackoffWithoutRepeatedNotifications()
    {
        using var wire = new Wire { Failure = "503" }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test");
        var clock = new Clock();
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client), clock);
        var changes = 0; prefs.Changed += (_, _) => changes++;
        await prefs.EnsureLoadedAsync();
        foreach (var delay in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            var reads = wire.CapabilityReads;
            clock.Advance(TimeSpan.FromSeconds(delay).Subtract(TimeSpan.FromMilliseconds(1)));
            await prefs.EnsureLoadedAsync();
            Assert.Equal(reads, wire.CapabilityReads);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await prefs.EnsureLoadedAsync();
            Assert.Equal(reads + 1, wire.CapabilityReads);
            Assert.Equal(1, changes);
            Assert.False(prefs.ShouldHideOverviews);
        }
    }

    [Fact]
    public async Task HeldFailingRetryKeepsVisibleStateAndDoesNotNotify()
    {
        using var wire = new Wire { Failure = "503", FailEffective = true };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test");
        var clock = new Clock();
        using var prefs = new EpisodeSpoilerPreferences(new SettingsApi(client), clock);
        var changes = 0; prefs.Changed += (_, _) => changes++;
        await prefs.EnsureLoadedAsync();
        Assert.False(prefs.ShouldHideOverviews);
        Assert.Equal(1, changes);
        clock.Advance(TimeSpan.FromSeconds(1)); wire.Hold = true;
        var retry = prefs.EnsureLoadedAsync();
        Assert.False(retry.IsCompleted);
        Assert.False(prefs.ShouldHideOverviews);
        Assert.Equal(1, changes);
        wire.Release.TrySetResult(); await retry;
        Assert.False(prefs.ShouldHideOverviews);
        Assert.Equal(1, changes);
        Assert.Equal(2, wire.EffectiveReads);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int ApiVersion = 1, Revision = 17; public bool SupportsBatchedEffective = true;
        public bool Hide = true; public bool Hold; public int EffectiveReads, CapabilityReads;
        public string? Failure; public bool FailEffective;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.EndsWith("capabilities"))
            {
                CapabilityReads++;
                if (!FailEffective && Failure != null) return FailedResponse();
                json = "{\"api_version\":" + ApiVersion + ",\"manifest_revision\":" + Revision +
                    ",\"supports_batched_effective\":" + (SupportsBatchedEffective ? "true" : "false") + "}";
            }
            else
            {
                EffectiveReads++;
                Started.TrySetResult(); if (Hold) await Release.Task;
                if (FailEffective && Failure != null) return FailedResponse();
                var value = Hide ? "true" : "false";
                json = "{\"items\":[{\"key\":\"catalog.hide_unwatched_episode_images\",\"value\":" + value + "},{\"key\":\"catalog.hide_unwatched_episode_overviews\",\"value\":" + value + "}]}";
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
        private HttpResponseMessage FailedResponse() => Failure switch
        {
            "404" => new(HttpStatusCode.NotFound),
            "503" => new(HttpStatusCode.ServiceUnavailable),
            "timeout" => throw new TaskCanceledException("Synthetic settings timeout"),
            "network" => throw new HttpRequestException("Synthetic settings network failure"),
            _ => new(HttpStatusCode.OK) { Content = new StringContent(Failure == "null" ? "null" : "{") }
        };
    }
}
