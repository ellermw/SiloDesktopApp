using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PrePlayAudioSelectionTests
{
    [Fact]
    public async Task ExplicitPickSavesCompleteSignatureAndAutoDeletesTheSameSeries()
    {
        using var wire = new Wire(); var client = Client(wire);
        var choice = new PrePlayAudioSelection(client, new CatalogApi(client));
        await choice.SelectAsync("series", 1, Track());
        Assert.Equal(1, choice.SelectedIndex);
        var saved = Assert.Single(wire.Requests);
        Assert.Equal(HttpMethod.Put, saved.Method);
        Assert.Equal("/api/v2/audio-prefs/series", saved.Path);
        Assert.Equal(1, saved.Body!.Value.GetProperty("audio_track_index").GetInt32());
        var signature = saved.Body.Value.GetProperty("track_signature");
        Assert.Equal("jpn", signature.GetProperty("language").GetString());
        Assert.Equal("Main", signature.GetProperty("title").GetString());
        Assert.Equal("Original", signature.GetProperty("embedded_title").GetString());
        Assert.Equal("flac", signature.GetProperty("codec").GetString());
        Assert.Equal("5.1", signature.GetProperty("layout").GetString());
        Assert.Equal(6, signature.GetProperty("channels").GetInt32());
        Assert.True(signature.GetProperty("default").GetBoolean());
        await choice.SelectAsync("series", null, null);
        Assert.Equal(HttpMethod.Delete, wire.Requests[1].Method);
        Assert.Null(choice.SelectedIndex);
    }

    [Fact]
    public async Task OverlappingWritesStayInClickOrderAndLatestFailureRollsBackToLastSavedChoice()
    {
        using var wire = new Wire { BlockFirst = true }; var client = Client(wire);
        var choice = new PrePlayAudioSelection(client, new CatalogApi(client));
        var first = choice.SelectAsync("movie", 1, Track());
        await wire.Entered.Task;
        var second = choice.SelectAsync("movie", null, null);
        var third = choice.SelectAsync("movie", 2, Track());
        Assert.Equal(2, choice.SelectedIndex);
        Assert.Single(wire.Requests);
        wire.Release.SetResult();
        await first;
        await Assert.ThrowsAsync<ApiException>(() => second);
        await Assert.ThrowsAsync<ApiException>(() => third);
        Assert.Equal(1, choice.SelectedIndex);
        Assert.Equal(new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Put }, wire.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task QueuedOldProfilePickNeverWritesUsingReplacementProfile()
    {
        using var wire = new Wire { BlockFirst = true }; var client = Client(wire);
        var choice = new PrePlayAudioSelection(client, new CatalogApi(client));
        var first = choice.SelectAsync("series", 1, Track());
        await wire.Entered.Task;
        var second = choice.SelectAsync("series", 2, Track());
        client.SetProfile("replacement");
        wire.Release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Single(wire.Requests);
        Assert.Null(choice.SelectedIndex);
    }

    [Fact]
    public async Task VersionResetPreventsLateFailureRepopulatingAnOldIndex()
    {
        using var wire = new Wire { BlockFirst = true, FailFirst = true }; var client = Client(wire);
        var choice = new PrePlayAudioSelection(client, new CatalogApi(client));
        var pending = choice.SelectAsync("series", 1, Track());
        await wire.Entered.Task;
        choice.Clear();
        wire.Release.SetResult();
        await Assert.ThrowsAsync<ApiException>(() => pending);
        Assert.Null(choice.SelectedIndex);
    }

    [Fact]
    public async Task CanceledQueuedPickDoesNotWriteOrRollbackANewerChoice()
    {
        using var wire = new Wire { BlockFirst = true }; var client = Client(wire);
        var choice = new PrePlayAudioSelection(client, new CatalogApi(client));
        var first = choice.SelectAsync("series", 1, Track());
        await wire.Entered.Task;
        using var cancellation = new CancellationTokenSource();
        var canceled = choice.SelectAsync("series", 2, Track(), cancellation.Token);
        cancellation.Cancel();
        wire.Release.SetResult();
        await first;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Single(wire.Requests);
        Assert.Equal(1, choice.SelectedIndex);
    }

    private static AudioTrackInfo Track() => new() { Language = "jpn", Title = "Main", EmbeddedTitle = "Original", Codec = "flac", Layout = "5.1", Channels = 6, Default = true };
    private static SiloApiClient Client(Wire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://tracks.invalid"); client.SetProfile("original"); return client;
    }
    private sealed class Wire : HttpMessageHandler
    {
        public bool BlockFirst, FailFirst;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(HttpMethod Method, string Path, JsonElement? Body)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            JsonElement? body = request.Content == null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            var ordinal = Requests.Count;
            if (BlockFirst && ordinal == 1) { Entered.SetResult(); await Release.Task.WaitAsync(cancellationToken); }
            var fail = FailFirst || BlockFirst && ordinal > 1;
            return new(fail ? HttpStatusCode.BadRequest : HttpStatusCode.NoContent)
            { Content = new StringContent(fail ? "{\"error\":{\"code\":\"fixture_refused\",\"message\":\"Refused\"}}" : "") };
        }
    }
}
