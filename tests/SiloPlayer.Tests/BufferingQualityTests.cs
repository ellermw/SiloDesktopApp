using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class BufferingQualityTests
{
    private static BufferingQualityController Controller(string mode = "always", string quality = "auto")
    {
        var c = new BufferingQualityController(mode);
        c.Adopt(new("plan", "adoption", quality, "1080p", 8000,
            [new() { Label = "1080p", BitrateKbps = 8000 }, new() { Label = "720p", BitrateKbps = 4000 }]), 0);
        c.Observe(new(0, false, false, false, false, false, 0, null));
        return c;
    }

    [Fact] public void AutoLowersFromMeasuredStallsAndKeepsEstimateWithoutRelearning()
    {
        var c = Controller();
        c.Observe(new(11000, true, false, false, false, false, 0, 3000));
        var action = c.Observe(new(17001, true, false, false, false, false, 0, 3000));
        Assert.Equal("replan", action?.Kind);
        Assert.Equal(5600, action?.Replan?.BandwidthEstimateKbps);
        Assert.Equal("observed_rebuffer", action?.Replan?.BandwidthEstimateSource);
        Assert.Equal(3000, action?.Replan?.ObservedThroughputKbps);
        Assert.Equal(5600, c.KeptEstimateKbps);
    }

    [Theory] [InlineData("ask", "auto")] [InlineData("always", "1080p")]
    public void ConsentRequiredForAskOrFixedQuality(string mode, string quality)
    {
        var c = Controller(mode, quality);
        c.Observe(new(11000, true, false, false, false, false, 0, 3000));
        Assert.Equal("prompt", c.Observe(new(17001, true, false, false, false, false, 0, 3000))?.Kind);
        Assert.Equal("prompt", c.Pill);
        Assert.NotNull(c.Accept(18000));
    }

    [Theory] [InlineData(true,false,false,false,0)] [InlineData(false,true,false,false,0)]
    [InlineData(false,false,true,false,0)] [InlineData(false,false,false,true,0)] [InlineData(false,false,false,false,6)]
    public void PauseSeekOutageRoomAndAmpleBufferDoNotLower(bool pause,bool seek,bool outage,bool room,double cache)
    {
        var c = Controller();
        c.Observe(new(11000, true, pause, seek, outage, room, cache, null));
        Assert.Null(c.Observe(new(18000, true, pause, seek, outage, room, cache, null)));
        Assert.Null(c.KeptEstimateKbps);
        Assert.Equal("none", c.Summary.BufferingAction);
    }

    [Fact] public void StartupNeedsMeasuredInsufficientMediaAndNeverOnlyReports()
    {
        var c = new BufferingQualityController("always");
        c.Adopt(new("plan", "adoption", "auto", "1080p", 8000,
            [new() { Label = "720p", BitrateKbps = 4000 }]), 0);
        c.Observe(new(11000, true, false, false, false, false, 0, null));
        Assert.Null(c.Observe(new(18000, true, false, false, false, false, 0, null)));
        Assert.Equal("replan", c.Observe(new(18001, true, false, false, false, false, 0, 2000))?.Kind);
        c = Controller("never");
        c.Observe(new(11000, true, false, false, false, false, 0, 2000));
        Assert.Null(c.Observe(new(18000, true, false, false, false, false, 0, 2000)));
        Assert.Equal("ignored", c.Summary.BufferingAction);
    }

    [Fact] public void NativeMeterDiscardsPauseSeekSilenceAndReset()
    {
        var m = new NativeMediaThroughputMeter();
        Assert.Null(m.Observe(0, 1000, false, false));
        Assert.Null(m.Observe(1000, 2000, true, false));
        Assert.Null(m.Observe(2000, 3000, false, false));
        Assert.Equal(8000, m.Observe(3000, 1003000, false, false));
        Assert.Null(m.Observe(8000, 1003000, false, false));
        Assert.Null(m.Observe(9000, 10, false, true));
    }
    [Fact] public void TwoShortStallsTriggerAndUndoClearsTheLearnedLimit()
    {
        var c = Controller();
        c.Observe(new(11000,true,false,false,false,false,0,3000));
        Assert.Null(c.Observe(new(12500,false,false,false,false,false,0,3000)));
        c.Observe(new(13000,true,false,false,false,false,0,3000));
        Assert.Equal("replan", c.Observe(new(14500,false,false,false,false,false,0,3000))?.Kind);
        var summary = c.Stop(14600);
        Assert.Equal(2, summary.StallCount); Assert.Equal(3000, summary.StallTotalMs);
        c.Adopt(new("lower","lowered","auto","720p",4000,[new(){Label="360p",BitrateKbps=300}]),15000);
        Assert.Equal("undo",c.Pill);
        Assert.Equal("clear",c.Restore(16000)?.Replan?.LearnedLimit);
        Assert.Null(c.KeptEstimateKbps);
    }
    [Fact] public void LaterAutoAdoptionKeepsStepDownAndKeepAnswerAcrossSeekReplans()
    {
        var c=Controller();
        c.Observe(new(11000,true,false,false,false,false,0,3000));
        c.Observe(new(17000,true,false,false,false,false,0,3000));
        c.Stop(18000);
        c.Adopt(new("lower","lowered","auto","720p",4000,[new(){Label="360p",BitrateKbps=300}]),18001);
        c.Observe(new(34000,false,false,false,false,false,0,3000));
        c.Stop(34001);
        c.Adopt(new("seek","seek-adoption","auto","720p",4000,[new(){Label="360p",BitrateKbps=300}]),35000);
        Assert.Equal(5600,c.KeptEstimateKbps);
    }
    [Fact] public void StartupDeadlineOnlyLowersFromVerifiedInsufficientMedia()
    {
        var c=Controller();
        c.Observe(new(14000,true,false,false,false,false,0,null));
        Assert.Null(c.StartDeadlinePassed(15000));
        c.Observe(new(15001,true,false,false,false,false,0,3000));
        Assert.Equal("observed_rebuffer",c.StartDeadlinePassed(15002)?.Replan?.BandwidthEstimateSource);
    }
    [Fact] public void HiddenPromptTimerFreezesAndBackKeepsQuality()
    {
        var c=Controller("ask");
        c.Observe(new(11000,true,false,false,false,false,0,3000));
        c.Observe(new(17000,true,false,false,false,false,0,3000));
        c.Observe(new(40000,true,false,false,false,false,0,3000,true));
        Assert.Equal("prompt",c.Pill);
        c.Keep();
        Assert.Null(c.Observe(new(50000,true,false,false,false,false,0,3000)));
        Assert.Equal("kept",c.Summary.BufferingAction);
    }
    [Fact] public void StartupDeadlineWaitsForSixHealthySecondsAfterOutage()
    {
        var c=Controller();
        c.Observe(new(11000,true,false,false,true,false,0,null));
        c.Observe(new(12000,true,false,false,false,false,0,2000));
        Assert.Null(c.StartDeadlinePassed(12000));
        Assert.Null(c.StartDeadlinePassed(17999));
        Assert.Equal("replan",c.StartDeadlinePassed(18000)?.Kind);
    }
    [Fact] public void ShortPostRetryAbsenceDoesNotKeepRearmingTheStartupGrace()
    {
        var c=Controller();
        for(var at=11000;at<=18000;at+=1000)
        {
            var outage=NativeBufferingSignalPolicy.IsOutage(true,false,at%2000==0?1100:25);
            var action=c.Observe(new(at,true,false,false,outage,false,0,at%2000==0?null:500));
            if(at==17000)Assert.Equal("replan",action?.Kind);
        }
        Assert.Equal("lowered",c.Summary.BufferingAction);
        Assert.False(NativeBufferingSignalPolicy.IsOutage(true,false,14999,true));
        Assert.True(NativeBufferingSignalPolicy.IsOutage(true,false,15000,true));
        Assert.False(NativeBufferingSignalPolicy.IsOutage(false,true,5000));
    }

    [Fact]
    public void BufferingObservationReadsLibMpvBeforeTakingTheGateTheUiThreadShares()
    {
        // libmpv property reads may wait for a busy core; the UI thread takes
        // _bufferingGate for pill reads, key handling and Keep/obscure updates.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SiloPlayer.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "SiloPlayer", "Services", "PlayerService.BufferingQuality.cs"));
        var start = source.IndexOf("private void ObserveBufferingQuality()", StringComparison.Ordinal);
        var method = source[start..source.IndexOf("public void SetBufferingPillObscured(", start, StringComparison.Ordinal)];
        var gate = method.IndexOf("lock (_bufferingGate)", StringComparison.Ordinal);
        Assert.True(gate > 0);
        foreach (var read in new[] { "ReadMediaNetworkObservation()", "CaptureUserPausedState()", "CurrentMediaPosition", "IsBufferingForCache" })
        {
            var at = method.IndexOf(read, StringComparison.Ordinal);
            Assert.True(at >= 0 && at < gate, read + " must be read before the buffering gate.");
            Assert.Equal(-1, method.IndexOf(read, gate, StringComparison.Ordinal));
        }
        Assert.Contains("!ReferenceEquals(_mpv, mpv)", method[gate..]);
        // Reads outside the gate need their own ordering: a single sample at a
        // time, dropped when a plan was adopted or ended while libmpv was read.
        var singleFlight = method.IndexOf("Interlocked.Exchange(ref _bufferingSampling, 1) != 0", StringComparison.Ordinal);
        var planCapture = method.IndexOf("var plan = Interlocked.Read(ref _bufferingPlanGeneration);", StringComparison.Ordinal);
        Assert.True(singleFlight >= 0 && singleFlight < planCapture && planCapture < method.IndexOf("ReadMediaNetworkObservation()", StringComparison.Ordinal));
        Assert.Contains("plan != _bufferingPlanGeneration", method[gate..]);
        Assert.Contains("Volatile.Write(ref _bufferingSampling, 0);", method);
        var adopt = source[source.IndexOf("private void AdoptBufferingQualityPlan()", StringComparison.Ordinal)..];
        Assert.Contains("_bufferingPlanGeneration++;", adopt[..adopt.IndexOf("FlushBufferingSummaryCore();", StringComparison.Ordinal)]);
        var end = source[source.IndexOf("private void EndBufferingQuality()", StringComparison.Ordinal)..];
        Assert.Contains("_bufferingPlanGeneration++;", end[..end.IndexOf("SetConnectionMediaActive(false);", StringComparison.Ordinal)]);
    }
}
