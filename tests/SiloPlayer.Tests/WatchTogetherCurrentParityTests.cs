using System.Net;
using System.Text;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class WatchTogetherCurrentParityTests
{
    [Fact]
    public async Task RoomSnapshot_DeserializesCurrentAdditiveMembers()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "room": {
                    "room_id":"room-1",
                    "code":"ABCD1234",
                    "phase":"lobby",
                    "selection_mode":"vote",
                    "member_count":2,
                    "members":[
                      {"user_id":1,"profile_id":"p1","display_name":"Host","is_host":true,"is_self":true,"connected":true},
                      {"user_id":2,"profile_id":"p2","display_name":"Guest","is_host":false,"is_self":false,"connected":false}
                    ]
                  }
                }
                """, Encoding.UTF8, "application/json"),
        }));
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        client.SetAccessToken("token");

        var response = await new PlaybackApi(client).GetWatchTogetherRoomAsync("room-1", "room-token");

        Assert.Equal(2, response.Room.Members.Count);
        Assert.Equal("Host (you)", response.Room.Members[0].DisplayLabel);
        Assert.True(response.Room.Members[0].IsHost);
        Assert.False(response.Room.Members[1].Connected);
    }

    [Fact]
    public void NativeRoomPage_CoversCurrentRoomActionsAndDiscoveryLayout()
    {
        var xaml = File.ReadAllText(SourcePath("src", "SiloPlayer", "Views", "WatchTogetherRoomPage.xaml"));
        var code = File.ReadAllText(SourcePath("src", "SiloPlayer", "Views", "WatchTogetherRoomPage.xaml.cs"));

        Assert.Contains("MembersWrapPanel", xaml);
        Assert.Contains("LeaveButton_Click", xaml);
        Assert.Contains("EndButton_Click", xaml);
        Assert.Contains("CandidateBackdrop", xaml);
        Assert.Contains("UniformGridLayout MinItemWidth=\"145\"", xaml);
        Assert.Contains("HostSearchBox_TextChanged", xaml);
        Assert.Contains("End watch party?", code);
        Assert.Contains("inviteUri.ToString()", code);
        Assert.Contains("No suggestions yet — search for something to add.", code);
    }

    private static string SourcePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "SiloPlayer")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. parts]);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
