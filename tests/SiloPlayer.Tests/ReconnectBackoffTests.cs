using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void ShortLivedConnectionsKeepBackingOff()
    {
        Assert.Equal(8_000, ReconnectBackoff.AfterConnection(8_000, 1_000, TimeSpan.FromMilliseconds(40)));
        Assert.Equal(8_000, ReconnectBackoff.AfterConnection(8_000, 1_000, TimeSpan.FromSeconds(29)));
    }

    [Fact]
    public void HealthyConnectionStartsTheDelayOver()
    {
        Assert.Equal(1_000, ReconnectBackoff.AfterConnection(8_000, 1_000, TimeSpan.FromSeconds(30)));
        Assert.Equal(500, ReconnectBackoff.AfterConnection(5_000, 500, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void ConnectionHealthClockStartsAfterSocketNegotiation()
    {
        foreach (var parts in new[] {
            new[] { "src", "SiloPlayer.Core", "Services", "EventChannelClient.cs" },
            new[] { "src", "SiloPlayer", "ViewModels", "WatchTogetherViewModels.cs" } })
        {
            var source = ReadRepoFile(parts);
            var connected = source.IndexOf("await ws.ConnectAsync(", StringComparison.Ordinal);
            var mark = source.IndexOf("onConnected?.Invoke();", StringComparison.Ordinal);
            Assert.True(connected >= 0 && mark > connected,
                "A slow ticket/handshake must not count toward healthy connected uptime.");
            Assert.Contains("long? connectedAt = null;", source);
        }
    }

    [Fact]
    public void SocketLoopsUseTheSharedPolicy()
    {
        var events = ReadRepoFile("src", "SiloPlayer.Core", "Services", "EventChannelClient.cs");
        var room = ReadRepoFile("src", "SiloPlayer", "ViewModels", "WatchTogetherViewModels.cs");
        Assert.Contains("ReconnectBackoff.AfterConnection(backoffMs, 1000,", events);
        Assert.DoesNotContain("// Normal close — reset backoff and reconnect.", events);
        Assert.Contains("ReconnectBackoff.AfterConnection(backoffMs, 500,", room);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
