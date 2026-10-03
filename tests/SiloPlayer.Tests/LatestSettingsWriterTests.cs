using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class LatestSettingsWriterTests
{
    [Fact]
    public async Task RapidChangesSaveOnlyTheNewestValue()
    {
        var values = new List<int>();
        var writer = new LatestSettingsWriter<int>((value, _) => { values.Add(value); return Task.CompletedTask; }, _ => { }, TimeSpan.FromMilliseconds(20));
        await Task.WhenAll(writer.SaveAsync(1, default), writer.SaveAsync(2, default), writer.SaveAsync(3, default));
        Assert.Equal(new[] { 3 }, values);
    }

    [Fact]
    public async Task FailureIsReportedLocallyAndLaterSaveRecovers()
    {
        Exception? last = null;
        var writer = new LatestSettingsWriter<int>((value, _) => value == 1 ? Task.FromException(new IOException("offline")) : Task.CompletedTask, error => last = error, TimeSpan.Zero);
        await writer.SaveAsync(1, default);
        Assert.IsType<IOException>(last);
        await writer.SaveAsync(2, default);
        Assert.Null(last);
    }

    [Fact]
    public async Task ChangesDuringDelayedSaveNeverOverlapAndSaveFinalValueLast()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new List<int>();
        int active = 0;
        var writer = new LatestSettingsWriter<int>(async (value, _) =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            values.Add(value);
            if (value == 1) { entered.SetResult(); await release.Task; }
            Interlocked.Decrement(ref active);
        }, _ => { }, TimeSpan.Zero);
        var first = writer.SaveAsync(1, default);
        await entered.Task;
        var second = writer.SaveAsync(2, default);
        var third = writer.SaveAsync(3, default);
        release.SetResult();
        await Task.WhenAll(first, second, third);
        Assert.Equal(new[] { 1, 3 }, values);
    }
}
