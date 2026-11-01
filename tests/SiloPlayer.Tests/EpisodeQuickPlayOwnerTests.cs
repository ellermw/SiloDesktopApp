using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

public sealed class EpisodeQuickPlayOwnerTests
{
    [Fact]
    public async Task CapturedStartOwnerCannotBeRecapturedAsTheReplacementProfile()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://quickplay-owner.invalid"); client.SetProfile("profile-a");
        var owner = client.CaptureIdentityContext(); client.SetProfile("profile-b");
        var api = new PlaybackApi(client);
        var pending = api.StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42, ProfileId = "profile-a" },
            expectedIdentityContext: owner);
        var error = await Record.ExceptionAsync(() => pending);
        Assert.Empty(wire.Requests);
        Assert.IsAssignableFrom<OperationCanceledException>(error);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"allowed":true,"state":"available","installation_id":"fixture","protocol_versions":[3],"features":[]}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
        }
    }
}
