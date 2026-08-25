using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminNodeToggleOperationTests
{
    [Fact]
    public async Task FailedServerUpdateKeepsPreviousStateAndSkipsReload()
    {
        var reloadCalled = false;

        var result = await AdminNodeToggleOperation.ExecuteAsync(
            currentEnabled: false,
            updateAsync: _ => throw new InvalidOperationException("update failed"),
            reloadAsync: () =>
            {
                reloadCalled = true;
                return Task.CompletedTask;
            });

        Assert.False(result.UpdateSucceeded);
        Assert.False(result.Enabled);
        Assert.Equal("update failed", result.UpdateError);
        Assert.Null(result.ReloadError);
        Assert.False(reloadCalled);
    }

    [Fact]
    public async Task SuccessfulServerUpdateWithFailedReloadPreservesChangedState()
    {
        bool? requestedEnabled = null;

        var result = await AdminNodeToggleOperation.ExecuteAsync(
            currentEnabled: false,
            updateAsync: enabled =>
            {
                requestedEnabled = enabled;
                return Task.CompletedTask;
            },
            reloadAsync: () => throw new InvalidOperationException("reload failed"));

        Assert.True(requestedEnabled);
        Assert.True(result.UpdateSucceeded);
        Assert.True(result.Enabled);
        Assert.Null(result.UpdateError);
        Assert.Equal("reload failed", result.ReloadError);
    }

    [Fact]
    public async Task SuccessfulServerUpdateAndReloadReturnsCleanSuccess()
    {
        var reloadCalled = false;

        var result = await AdminNodeToggleOperation.ExecuteAsync(
            currentEnabled: true,
            updateAsync: enabled =>
            {
                Assert.False(enabled);
                return Task.CompletedTask;
            },
            reloadAsync: () =>
            {
                reloadCalled = true;
                return Task.CompletedTask;
            });

        Assert.True(result.UpdateSucceeded);
        Assert.False(result.Enabled);
        Assert.Null(result.UpdateError);
        Assert.Null(result.ReloadError);
        Assert.True(reloadCalled);
    }
}
