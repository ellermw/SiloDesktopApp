using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class SubtitleProviderAvailabilityTests
{
    [Theory]
    [InlineData("{\"enabled\":true}", true)]
    [InlineData("{\"enabled\":false}", false)]
    [InlineData("{}", true)]
    public async Task OnlyExplicitDisabledStatusDisablesOnlineSearch(string json, bool expected)
    {
        using var http = new HttpClient(new PersonRefreshTests.Handler(_ => PersonRefreshTests.Json(json)));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        Assert.Equal(expected, await new PlaybackApi(client).CanSearchSubtitlesAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"enabled\":null}")]
    [InlineData("invalid json")]
    public async Task UnknownOrMalformedStatusFailsOpen(string json)
    {
        using var http = new HttpClient(new PersonRefreshTests.Handler(_ => PersonRefreshTests.Json(json)));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        Assert.True(await new PlaybackApi(client).CanSearchSubtitlesAsync());
    }

    [Fact]
    public async Task FailedStatusFailsOpenAndDisabledAvailabilityIsNeverCached()
    {
        var responses = new Queue<HttpResponseMessage>([
            PersonRefreshTests.Json("{\"enabled\":false}"),
            new(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"Unavailable\"}") },
            PersonRefreshTests.Json("{\"enabled\":true}")
        ]);
        using var http = new HttpClient(new PersonRefreshTests.Handler(_ => responses.Dequeue()));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        var api = new PlaybackApi(client);
        Assert.False(await api.CanSearchSubtitlesAsync());
        Assert.True(await api.CanSearchSubtitlesAsync());
        client.SetBaseUrl("https://other-server.test");
        Assert.True(await api.CanSearchSubtitlesAsync());
        Assert.Empty(responses);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("profile")]
    [InlineData("cancel")]
    public async Task LateDisabledResponseCannotApplyToAnotherContext(string change)
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new PersonRefreshTests.AsyncHandler(_ => response.Task));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        using var cts = new CancellationTokenSource();
        var availability = new PlaybackApi(client).CanSearchSubtitlesAsync(cts.Token);
        if (change == "server") client.SetBaseUrl("https://other-server.test");
        else if (change == "profile") client.SetProfile("another-profile");
        else cts.Cancel();
        response.SetResult(PersonRefreshTests.Json("{\"enabled\":false}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await availability);
    }
}
