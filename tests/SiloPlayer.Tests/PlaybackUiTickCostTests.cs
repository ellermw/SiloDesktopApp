using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackUiTickCostTests
{
    [Fact]
    public void SecondGateAdmitsOneUpdatePerDisplayedSecond()
    {
        var gate = new PositionSecondGate();
        // 60 fps frames across just over two seconds.
        var admitted = Enumerable.Range(0, 130).Select(frame => frame / 60.0).Count(gate.ShouldUpdate);
        Assert.Equal(3, admitted);
    }

    [Fact]
    public void SecondGateAdmitsSeeksAndResets()
    {
        var gate = new PositionSecondGate();
        Assert.True(gate.ShouldUpdate(10.2));
        Assert.False(gate.ShouldUpdate(10.9));
        Assert.True(gate.ShouldUpdate(4.5));   // backward seek
        Assert.True(gate.ShouldUpdate(10.0));
        gate.Reset();
        Assert.True(gate.ShouldUpdate(10.5));
        Assert.True(gate.ShouldUpdate(double.NaN));
        Assert.False(gate.ShouldUpdate(double.NaN));
    }

    [Fact]
    public void PerFramePositionSubscribersAreGatedOrRemoved()
    {
        var detail = ReadRepoFile("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        Assert.Contains("if (_detailPositionGate.ShouldUpdate(position)) DispatcherQueue.TryEnqueue(UpdateActiveAudiobook);", detail);
        // Pause changes stay immediate.
        Assert.Contains("private void OnDetailPauseChanged(bool paused) => DispatcherQueue.TryEnqueue(UpdateActiveAudiobook);", detail);

        var hero = ReadRepoFile("src", "SiloPlayer", "Controls", "NowListeningHero.xaml.cs");
        Assert.Contains("if (_livePositionGate.ShouldUpdate(position)) DispatcherQueue.TryEnqueue(UpdateLiveState);", hero);

        var mini = ReadRepoFile("src", "SiloPlayer", "Controls", "MiniPlayerBar.xaml.cs");
        Assert.DoesNotContain("PositionChanged", mini);
    }

    [Fact]
    public void OverlayTickDoesNotWriteSessionProgressOrReformatTooltips()
    {
        var overlay = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs");
        Assert.DoesNotContain("Manager?.UpdatePosition(", overlay);
        Assert.DoesNotContain("$\"{label} (F)\"", overlay);
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
