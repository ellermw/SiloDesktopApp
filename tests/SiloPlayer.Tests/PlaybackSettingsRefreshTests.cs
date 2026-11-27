using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class PlaybackSettingsRefreshTests
{
    private static readonly SettingsIdentityContext Owner = new(new(1, 1, "https://fixture.invalid", "selected"), "device", 1);

    [Fact]
    public async Task BurstUsesOneReadAndAppliesOnlyTheLatestResult()
    {
        var refresh = new PlaybackSettingsRefresh();
        var accepted = 0L;
        var reads = 0;
        var requests = Enumerable.Range(1, 8).Select(i => refresh.RequestAsync("selected", Owner, default,
            () => true, _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult(new ContractEffectiveSettingsResponse { Revision = i });
            }, Publish, values => accepted = values.Revision)).ToArray();
        await Task.WhenAll(requests);

        Assert.Equal(1, reads);
        Assert.Equal(8, accepted);
    }

    [Theory]
    [InlineData(false, "selected")]
    [InlineData(true, "another-profile")]
    public async Task IdleAndOtherProfileEventsDoNotRead(bool playing, string profile)
    {
        var refresh = new PlaybackSettingsRefresh();
        await refresh.RequestAsync(profile, Owner, default, () => playing,
            _ => throw new InvalidOperationException("Unexpected HTTP read"), Publish,
            _ => throw new InvalidOperationException("Unexpected publication"));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("file")]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("device")]
    public async Task AResponseIsDroppedIfItsOwnerChangesDuringTheRead(string changed)
    {
        var refresh = new PlaybackSettingsRefresh();
        var ticket = (Settings: Owner, Session: 1, File: 1);
        var current = ticket;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ContractEffectiveSettingsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = false;
        var request = refresh.RequestAsync("selected", Owner, default, () => ticket == current,
            _ => { entered.SetResult(); return response.Task; }, Publish, _ => applied = true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        current = changed switch
        {
            "session" => (Owner, 2, 1),
            "file" => (Owner, 1, 2),
            "profile" => (Owner with { Identity = Owner.Identity with { ProfileId = "elsewhere" } }, 1, 1),
            "account" => (Owner with { Identity = Owner.Identity with { AuthenticationGeneration = 2 } }, 1, 1),
            "device" => (Owner with { DeviceGeneration = 2 }, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        response.SetResult(new());
        await request;

        Assert.False(applied);
    }

    [Fact]
    public async Task FailedReadPreservesAcceptedSettings()
    {
        var refresh = new PlaybackSettingsRefresh();
        var accepted = 1L;
        await refresh.RequestAsync(null, Owner, default, () => true,
            _ => Task.FromException<ContractEffectiveSettingsResponse>(new HttpRequestException("fixture failure")),
            Publish, values => accepted = values.Revision);
        Assert.Equal(1, accepted);
    }

    [Fact]
    public async Task SupersedingAnInFlightReadCancelsItAndRejectsALateResponse()
    {
        var refresh = new PlaybackSettingsRefresh();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<ContractEffectiveSettingsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = 0L;
        void Apply(ContractEffectiveSettingsResponse values) => accepted = values.Revision;
        var first = refresh.RequestAsync("selected", Owner, default, () => true,
            ct => { entered.SetResult(ct); return response.Task; }, Publish, Apply);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await refresh.RequestAsync("selected", Owner, default, () => true,
            _ => Task.FromResult(new ContractEffectiveSettingsResponse { Revision = 2 }), Publish, Apply);
        Assert.True(token.IsCancellationRequested);
        response.SetResult(new() { Revision = 1 });
        await first;
        Assert.Equal(2, accepted);
    }

    private static Task Publish(Action apply, CancellationToken _) { apply(); return Task.CompletedTask; }
}
