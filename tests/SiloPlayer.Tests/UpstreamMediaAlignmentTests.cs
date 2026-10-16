using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class UpstreamMediaAlignmentTests
{
    [Theory]
    [InlineData("eac3", "Dolby Digital Plus + Dolby Atmos", "DD+ Atmos")]
    [InlineData("truehd", "Dolby Atmos", "TrueHD Atmos")]
    [InlineData("ac3", "AC3 5.1 (from TrueHD Atmos)", "AC3")]
    [InlineData("eac3", "Atmospheric", "EAC3")]
    public void AudioEvidenceUsesCompatibleFamilyAndWholeAtmosWord(string codec, string evidence, string expected)
        => Assert.Equal(expected, VersionRanking.MapAudioLabel(codec, evidence));

    [Fact]
    public void DefaultVersionRetainsTrackEvidence()
    {
        var plain = new FileVersion { FileId = 1, Resolution = "1080p", CodecAudio = "eac3" };
        var atmos = new FileVersion { FileId = 2, Resolution = "1080p", CodecAudio = "eac3", AudioTracks = [new() { Codec = "eac3", Profile = "Dolby Atmos" }] };
        Assert.Same(plain, VersionRanking.SelectDefaultVersion([plain, atmos], null, null));
        var lossless = new FileVersion { FileId = 3, Resolution = "1080p", CodecAudio = "truehd" };
        Assert.Same(lossless, VersionRanking.SelectDefaultVersion([atmos, lossless], null, null));
        Assert.Equal("TrueHD", VersionRanking.PickBestAttributes([atmos, lossless], null)!.AudioLabel);
        Assert.Equal("DD+ Atmos", VersionRanking.PickBestAttributes([plain, atmos], null)!.AudioLabel);
    }

    [Theory]
    [InlineData("episode", false)] [InlineData("season", false)] [InlineData("movie", true)] [InlineData("series", true)]
    public void PersonalListsOnlyOfferAddForTitles(string type, bool expected)
        => Assert.Equal(expected, MediaItemActionPolicy.CanAddToPersonalLists(type));

    [Fact]
    public async Task ResetOnlyClearsResumeAfterSuccessPreservingWatchedAndDuration()
    {
        var item = new MediaItem { ContentId = "movie", Type = "movie", PositionSeconds = 42, DurationSeconds = 90, UserState = new() { Played = true, IsFavorite = true } };
        await MediaItemActionExecutor.ResetProgressAsync(item, () => Task.CompletedTask);
        Assert.Equal(0, item.PositionSeconds);
        Assert.Equal(90, item.DurationSeconds);
        Assert.True(item.UserState.Played);
        Assert.True(item.UserState.IsFavorite);
        item.PositionSeconds = 12;
        await Assert.ThrowsAsync<IOException>(() => MediaItemActionExecutor.ResetProgressAsync(item, () => throw new IOException()));
        Assert.Equal(12, item.PositionSeconds);
    }

    [Fact]
    public async Task ResetDeleteNeverRefreshesAndReplays()
    {
        var handler = new Handler(HttpStatusCode.Unauthorized);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        var refreshed = false;
        client.SetTokenRefresher(_ => { refreshed = true; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => new CatalogApi(client).ClearResumePositionAsync("movie/a"));
        Assert.False(refreshed);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Contains("/api/v2/progress/movie%2Fa/position", handler.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OldServerDoesNotAdvertiseReset()
    {
        var client = new SiloApiClient(new HttpClient(new Handler(HttpStatusCode.NotFound)));
        client.SetBaseUrl("https://example.test");
        Assert.False((await new CatalogApi(client).GetProgressCapabilitiesAsync()).ClearResumePosition);
    }

    [Fact]
    public void CrewGroupsCapOnlyShownLeadAndRemoveWriterDuplicates()
    {
        var crew = new List<CrewMember>();
        for (var i = 1; i <= 3; i++) { crew.Add(new() { PersonId = i.ToString(), Name = "Person " + i, Job = "Director" }); crew.Add(new() { PersonId = i.ToString(), Name = "Person " + i, Job = "Writer" }); }
        var groups = CastCrewPresentation.BuildGroups("movie", crew, []);
        Assert.Equal(2, groups[0].Members.Count);
        Assert.Single(groups[1].Members);
        Assert.Equal("3", groups[1].Members[0].PersonId);
        Assert.Equal("Writer", groups[1].Members[0].Character);
    }

    [Fact]
    public async Task EpisodeRatingsAndFieldSpecificProvenanceSurviveWireRead()
    {
        const string payload = "{\"items\":[{\"content_id\":\"ep\",\"ratings\":[{\"source\":\"imdb\",\"name\":\"IMDb\",\"score\":8.2,\"display\":\"8.2\"}],\"pending_translation_language\":\"fr\",\"machine_translated_fields\":[\"tagline\"]}]}";
        var client = new SiloApiClient(new HttpClient(new Handler(HttpStatusCode.OK, payload)));
        client.SetBaseUrl("https://example.test");
        var ep = Assert.Single((await new CatalogApi(client).GetItemEpisodesAsync("season")).Episodes);
        Assert.Equal("8.2", Assert.Single(ep.Ratings).Display);
        Assert.Equal("fr", ep.PendingTranslationLanguage);
        Assert.False(MachineTranslationPresentation.HasField(ep.MachineTranslatedFields, "overview"));
        Assert.True(MachineTranslationPresentation.HasField(ep.MachineTranslatedFields, "tagline"));
    }

    [Theory]
    [InlineData("movie", 12, true, true)]
    [InlineData("episode", 12, true, true)]
    [InlineData("audiobook", 12, true, false)]
    [InlineData("season", 12, true, false)]
    [InlineData("movie", 0, true, false)]
    [InlineData("movie", 12, false, false)]
    public void ResetEligibilityRequiresSupportedLeafWithResume(string type, double position, bool supported, bool expected)
        => Assert.Equal(expected, MediaItemActionPolicy.CanResetProgress(type, position, supported));

    [Fact]
    public void SeriesRetainsAllCreatorsAndFallsBackToTwoDirectors()
    {
        var creators = Enumerable.Range(1, 5).Select(i => new CrewMember { Name = "Creator " + i, Job = "Creator" }).ToList();
        var directors = Enumerable.Range(1, 4).Select(i => new CrewMember { Name = "Director " + i, Job = "Director" }).ToList();
        Assert.Equal(5, CastCrewPresentation.BuildGroups("series", creators.Concat(directors), [])[0].Members.Count);
        var fallback = CastCrewPresentation.BuildGroups("series", directors, []);
        Assert.Equal("Director", fallback[0].Label);
        Assert.Equal(2, fallback[0].Members.Count);
    }

    private sealed class Handler(HttpStatusCode status, string payload = "{\"error\":{\"code\":\"unsupported\",\"message\":\"Unavailable\"}}") : HttpMessageHandler
    {
        public int Calls; public HttpMethod? Method; public string Path = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Method = request.Method; Path = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
