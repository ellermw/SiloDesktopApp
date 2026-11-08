namespace SiloPlayer.Tests;

public sealed class PlaybackAudioTerminalOwnershipSourceTests
{
    private static string AudioSwitch()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task SwitchAudioTrackCoreAsync(", StringComparison.Ordinal);
        return source[start..source.IndexOf("private static bool IsBurnedInSubtitle(", start, StringComparison.Ordinal)];
    }

    [Fact]
    public void TerminalAudioCannotAdmitAnotherSelection()
    {
        var method = AudioSwitch();
        var admission = method[..method.IndexOf("var authority =", StringComparison.Ordinal)];
        Assert.Contains("if (_playbackTerminalState != null) return;", admission);
    }

    [Fact]
    public void TerminalAudioCannotPublishLateFailureOrReconcileSubtitles()
    {
        var method = AudioSwitch();
        var start = method.IndexOf("bool CurrentSwitch() =>", StringComparison.Ordinal);
        var predicate = method[start..method.IndexOf(';', start)];
        Assert.Contains("_playbackTerminalState == null", predicate);
        var failure = method[method.IndexOf("catch (Exception ex)", StringComparison.Ordinal)..];
        Assert.Contains("if (!CurrentSwitch()) return;", failure);
        var release = failure[failure.IndexOf("ReleasePlaybackBusy(busyOwner, () =>", StringComparison.Ordinal)..];
        var guard = release.IndexOf("if (!CurrentSwitch()) return;", StringComparison.Ordinal);
        var publish = release.IndexOf("ErrorMessage = detail;", StringComparison.Ordinal);
        Assert.True(guard >= 0 && publish > guard);
        Assert.Contains("if (reconcileSubtitles && CurrentSwitch())", failure);
        Assert.Contains("if (!CurrentSwitch()) ReleasePlaybackBusy(busyOwner);", failure);
    }
}
