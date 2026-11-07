using SiloPlayer.Player;

namespace SiloPlayer.Tests;

// Busy native callers must not starve timing-sensitive tests running in parallel.
[Collection("Native streaming")]
public sealed class MpvTeardownRaceTests
{
    [Fact]
    public void FlagReadsDistinguishPlayingFromUnavailableAndRetiredPlayers()
    {
        using var player = new MpvPlayer();
        Assert.False(player.TryGetPropertyFlag("pause", out _));
        player.Initialize(16, 16);
        player.SetProperty("pause", "yes");
        Assert.True(player.TryGetPropertyFlag("pause", out var paused));
        Assert.True(paused);
        player.Play();
        Assert.True(player.TryGetPropertyFlag("pause", out paused));
        Assert.False(paused);
        Assert.False(player.TryGetPropertyFlag("silo-unknown-native-flag", out _));
        player.Dispose();
        Assert.False(player.TryGetPropertyFlag("pause", out _));
    }

    // UI timers and background playback tasks read and write mpv properties
    // while the player can be disposed. Without call leases, a caller that
    // passed the handle check could enter libmpv after mpv_terminate_destroy.
    [Fact]
    public async Task ConcurrentPropertyCallsDuringDisposeNeverReachADestroyedHandle()
    {
        for (var round = 0; round < 25; round++)
        {
            var player = new MpvPlayer();
            player.Initialize(16, 16);
            player.SetProperty("ao", "null");
            player.SetProperty("video", "no");

            using var stop = new CancellationTokenSource();
            var calls = 0L;
            var workers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        _ = player.GetPropertyDouble("volume");
                        _ = player.GetPropertyFlag("pause");
                        player.SetProperty("volume", worker % 2 == 0 ? "40" : "60");
                        _ = player.IsPositionCached(1);
                        _ = player.GetMute();
                        _ = player.ReadMediaNetworkObservation();
                        player.SendKeypress("ignored-key");
                    }
                    catch (ObjectDisposedException)
                    {
                        // Command-based calls report disposal instead of entering native code.
                    }
                    Interlocked.Increment(ref calls);
                }
            })).ToArray();

            await Task.Delay(15 + round % 5 * 5);
            player.Dispose();
            await Task.Delay(10);
            stop.Cancel();
            await Task.WhenAll(workers);

            Assert.True(Interlocked.Read(ref calls) > 0);
            Assert.Equal(0, player.GetPropertyDouble("volume"));
            Assert.False(player.GetPropertyFlag("pause"));
            player.SetProperty("volume", "10");
        }
    }
}
