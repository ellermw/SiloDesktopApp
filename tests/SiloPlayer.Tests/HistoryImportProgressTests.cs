using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class HistoryImportProgressTests
{
    [Theory]
    [InlineData("queued")]
    [InlineData("running")]
    [InlineData("completed")]
    public void SkippedMatchesAreNotCountedTwice(string status)
    {
        var run = new HistoryImportRun { Status = status, Fetched = 100, Matched = 60, Unmatched = 10, Skipped = 40 };
        Assert.Equal(70, run.Processed);
        Assert.Equal(70, run.ProgressPercent);
        Assert.Equal(40, run.Skipped);
    }

    [Fact]
    public void EmptyProgressIsFiniteAndZero()
    {
        Assert.Equal(0, new HistoryImportRun().ProgressPercent);
    }

    [Theory]
    [InlineData("emby", "session", true, true)]
    [InlineData("emby", "session", false, false)]
    [InlineData("emby", null, true, false)]
    [InlineData("plex", null, true, false)]
    [InlineData("jellyfin", null, true, false)]
    public async Task OnlySuccessfulConnectRunCreationConsumesAuthorization(string source, string? session, bool success, bool consumes)
    {
        var client = new SiloApiClient(new HttpClient(new Handler(success)));
        client.SetBaseUrl("https://silo.example");
        string? consumed = null;
        var request = new CreateHistoryImportRunRequest { Source = source, ConnectSessionId = session };
        var task = HistoryImportStart.CreateAsync(new HistoryImportApi(client), request, value => consumed = value);
        if (success) await task;
        else await Assert.ThrowsAsync<ApiException>(() => task);
        Assert.Equal(consumes ? session : null, consumed);
    }

    private sealed class Handler(bool success) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(success ? HttpStatusCode.Created : HttpStatusCode.BadGateway)
            { Content = new StringContent(success ? "{\"id\":\"run-1\"}" : "{\"detail\":\"Unavailable\"}") });
    }
}
