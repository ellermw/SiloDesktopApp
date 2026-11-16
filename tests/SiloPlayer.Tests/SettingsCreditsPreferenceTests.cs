using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class SettingsCreditsPreferenceTests
{
    [Fact]
    public async Task PlaybackPreferenceReadKeepsItsCapturedProfileAndDeviceAcrossCatchUp()
    {
        using var handler = new PreferenceHandler();
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var api = new SettingsApi(client);
        var owner = api.CaptureSettingsIdentityContext();
        var read = api.GetContractEffectiveSettingsAsync(owner, ["playback.auto_play_next", "playback.auto_play_next"]);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
        handler.Release.TrySetResult();

        var result = await read;

        Assert.Equal("/api/v2/settings/values/effective?keys=playback.auto_play_next&device_id=desktop%2Fone&profile_id=profile%2Fone", handler.Path);
        Assert.Equal("profile/one", handler.Profile);
        Assert.False(result.Settings.Single().Value.GetBoolean());
        Assert.True(api.IsCurrentSettingsIdentityContext(owner));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("device")]
    [InlineData("server")]
    public async Task PlaybackPreferenceResponseCannotPublishAfterItsOwnerRetires(string change)
    {
        using var handler = new PreferenceHandler();
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var api = new SettingsApi(client);
        var owner = api.CaptureSettingsIdentityContext();
        var read = api.GetContractEffectiveSettingsAsync(owner, ["playback.auto_play_next"]);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Retire(client, change);
        handler.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("device")]
    [InlineData("server")]
    public async Task RetiredPlaybackPreferenceOwnerCannotDispatchARead(string change)
    {
        using var handler = new PreferenceHandler();
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var api = new SettingsApi(client);
        var owner = api.CaptureSettingsIdentityContext();
        Retire(client, change);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetContractEffectiveSettingsAsync(owner, ["playback.auto_play_next"]));
        Assert.Equal(0, handler.Requests);
    }

    private static SiloApiClient CreateClient(HttpClient http)
    {
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://silo.example.test");
        client.SetProfile("profile/one");
        client.SetDeviceMetadata("desktop/one", "Desktop", "windows");
        return client;
    }

    private static void Retire(SiloApiClient client, string change)
    {
        switch (change)
        {
            case "profile": client.SetProfile("profile/two"); break;
            case "device": client.SetDeviceMetadata("desktop/two", "Other desktop", "windows"); break;
            case "server": client.SetBaseUrl("https://other.example.test"); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private sealed class PreferenceHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Path { get; private set; }
        public string? Profile { get; private set; }
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Path = request.RequestUri!.PathAndQuery;
            Profile = request.Headers.GetValues("X-Profile-Id").Single();
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[{\"key\":\"playback.auto_play_next\",\"value\":false}]}")
            };
        }
    }
}
