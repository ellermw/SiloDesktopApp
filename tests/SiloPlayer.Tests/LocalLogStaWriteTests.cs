using System.Reflection;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LocalLogStaWriteTests
{
    [Fact]
    public void UiThreadWritesDoNotWaitBehindAnotherThreadsDiskWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-sta-log-" + Guid.NewGuid().ToString("N"));
        var gate = typeof(LocalLog).GetField("s_gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var seam = typeof(LocalLog).GetField("s_avoidBlockingWait", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = seam.GetValue(null);
        var uiThreadId = -1;
        Thread? uiThread = null;
        seam.SetValue(null, new Func<bool>(() => Environment.CurrentManagedThreadId == Volatile.Read(ref uiThreadId)));
        try
        {
            // Not disposed: on failure the worker may still be signalling them.
            var goFirst = new ManualResetEventSlim();
            var firstWritten = new ManualResetEventSlim();
            var goLater = new ManualResetEventSlim();
            var laterWritten = new ManualResetEventSlim();
            uiThread = new Thread(() =>
            {
                Volatile.Write(ref uiThreadId, Environment.CurrentManagedThreadId);
                goFirst.Wait();
                LocalLog.AppendLine("ui.txt", "first", directory);
                LocalLog.AppendLines("ui.txt", ["second"], directory);
                firstWritten.Set();
                goLater.Wait();
                LocalLog.AppendLine("ui.txt", "third", directory);
                laterWritten.Set();
            }) { IsBackground = true };
            uiThread.Start();

            Monitor.Enter(gate);
            try
            {
                goFirst.Set();
                // A blocked UI-thread write would not return while the gate is held.
                Assert.True(firstWritten.Wait(TimeSpan.FromSeconds(2)), "A UI-thread log write waited behind the held log gate.");
                Assert.False(File.Exists(Path.Combine(directory, "ui.txt")));
            }
            finally { Monitor.Exit(gate); goFirst.Set(); }
            // Uncontended now, yet it must stay behind the lines still queued.
            goLater.Set();
            Assert.True(laterWritten.Wait(TimeSpan.FromSeconds(3)));

            Assert.True(uiThread.Join(TimeSpan.FromSeconds(3)));
            var path = Path.Combine(directory, "ui.txt");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllLines(path).Length == 3, TimeSpan.FromSeconds(5)), "Deferred lines were not written.");
            Assert.Equal(["first", "second", "third"], File.ReadAllLines(path).Select(line => line[(line.IndexOf(']') + 2)..]));
        }
        finally
        {
            uiThread?.Join(TimeSpan.FromSeconds(5));
            seam.SetValue(null, original);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void OtherThreadsStillWriteBeforeReturning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-sta-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            LocalLog.AppendLine("worker.txt", "immediate", directory);
            Assert.EndsWith("immediate", Assert.Single(File.ReadAllLines(Path.Combine(directory, "worker.txt"))));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
