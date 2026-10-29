using System.Reflection;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

[CollectionDefinition("LocalLog serialization", DisableParallelization = true)]
public sealed class LocalLogSerializationCollection { }

[Collection("LocalLog serialization")]
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
        var goFirst = new ManualResetEventSlim();
        var goLater = new ManualResetEventSlim();
        seam.SetValue(null, new Func<bool>(() => Environment.CurrentManagedThreadId == Volatile.Read(ref uiThreadId)));
        try
        {
            // Not disposed: on failure the worker may still be signalling them.
            var firstWritten = new ManualResetEventSlim();
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
            WaitForDeferredDrain();
            Assert.Equal(3, File.ReadAllLines(path).Length);
            Assert.Equal(["first", "second", "third"], File.ReadAllLines(path).Select(line => line[(line.IndexOf(']') + 2)..]));
        }
        finally
        {
            goFirst.Set();
            goLater.Set();
            uiThread?.Join(TimeSpan.FromSeconds(5));
            seam.SetValue(null, original);
            WaitForDeferredDrain();
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void LaterUiWriteCannotOvertakeAnAlreadyDequeuedWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-inflight-log-" + Guid.NewGuid().ToString("N"));
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var gate = typeof(LocalLog).GetField("s_gate", flags)!.GetValue(null)!;
        var seam = typeof(LocalLog).GetField("s_avoidBlockingWait", flags)!;
        var original = seam.GetValue(null);
        var uiThreadId = -1;
        seam.SetValue(null, new Func<bool>(() => Environment.CurrentManagedThreadId == Volatile.Read(ref uiThreadId)));
        try
        {
            Monitor.Enter(gate);
            try
            {
                // Only this caller gets the UI behavior; unrelated test workers remain synchronous.
                var first = new Thread(() =>
                {
                    Volatile.Write(ref uiThreadId, Environment.CurrentManagedThreadId);
                    LocalLog.AppendLine("order.txt", "first", directory);
                }) { IsBackground = true };
                first.Start();
                Assert.True(first.Join(TimeSpan.FromSeconds(3)));
                var queue = typeof(LocalLog).GetField("s_deferred", flags)!.GetValue(null)!;
                var isEmpty = queue.GetType().GetProperty("IsEmpty")!;
                Assert.True(SpinWait.SpinUntil(() => (bool)isEmpty.GetValue(queue)!, TimeSpan.FromSeconds(3)),
                    "The worker did not take the first line into flight.");

                // Reentrant ownership exposes the empty-queue bypass deterministically:
                // the earlier writer is waiting on this gate, while this caller can enter it.
                Volatile.Write(ref uiThreadId, Environment.CurrentManagedThreadId);
                LocalLog.AppendLine("order.txt", "second", directory);
                Assert.False(File.Exists(Path.Combine(directory, "order.txt")),
                    "A later UI write overtook the earlier in-flight line.");
            }
            finally { Monitor.Exit(gate); }

            WaitForDeferredDrain();
            Assert.Equal(["first", "second"], File.ReadAllLines(Path.Combine(directory, "order.txt"))
                .Select(line => line[(line.IndexOf(']') + 2)..]));
        }
        finally
        {
            seam.SetValue(null, original);
            WaitForDeferredDrain();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExplicitStaFlushWaitsUntilItsBatchIsWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-sta-flush-" + Guid.NewGuid().ToString("N"));
        var gate = typeof(LocalLog).GetField("s_gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var log = new QueuedLocalLog(directory);
        var queue = typeof(QueuedLocalLog).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(log)!;
        var isEmpty = queue.GetType().GetProperty("IsEmpty")!;
        using var started = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        Exception? failure = null;
        var sta = new Thread(() =>
        {
            try
            {
                Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                started.Set();
                log.Flush();
                Assert.EndsWith("queued event", Assert.Single(File.ReadAllLines(Path.Combine(directory, "flush.txt"))));
            }
            catch (Exception exception) { failure = exception; }
            finally { returned.Set(); }
        }) { IsBackground = true };
        sta.SetApartmentState(ApartmentState.STA);
        try
        {
            log.Enqueue("flush.txt", "queued event");
            Monitor.Enter(gate);
            try
            {
                sta.Start();
                Assert.True(started.Wait(TimeSpan.FromSeconds(3)));
                Assert.True(SpinWait.SpinUntil(() => (bool)isEmpty.GetValue(queue)!, TimeSpan.FromSeconds(3)),
                    "The STA flush did not dequeue its batch.");
                Assert.False(returned.Wait(TimeSpan.FromMilliseconds(200)),
                    "Explicit STA Flush returned before the blocked batch reached the file.");
            }
            finally { Monitor.Exit(gate); }
            Assert.True(sta.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
        }
        finally
        {
            if ((sta.ThreadState & ThreadState.Unstarted) == 0) sta.Join(TimeSpan.FromSeconds(5));
            WaitForDeferredDrain();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ActualStaAppendsCaptureTimestampRedactionOperationDirectoryAndRotation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-sta-capture-" + Guid.NewGuid().ToString("N"));
        var gate = typeof(LocalLog).GetField("s_gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Exception? failure = null;
        var sta = new Thread(() =>
        {
            try
            {
                Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                using (LocalLog.BeginOperation("sta-fixture", directory))
                {
                    var entries = new List<(DateTime, string)>
                    {
                        (new DateTime(2026, 10, 28, 10, 11, 12, 345), "first /media?token=fixture-secret")
                    };
                    LocalLog.AppendTimestampedLines("capture.txt", entries, maxBytes: 1, archiveCount: 1);
                    entries[0] = (DateTime.MinValue, "mutated after handoff");
                    LocalLog.AppendLine("capture.txt", "second", maxBytes: 1, archiveCount: 1);
                }
                Assert.Null(LocalLog.CurrentOperationId);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        sta.SetApartmentState(ApartmentState.STA);
        try
        {
            Monitor.Enter(gate);
            try
            {
                sta.Start();
                Assert.True(sta.Join(TimeSpan.FromSeconds(3)), "Ordinary STA appends waited on the disk gate.");
                Assert.Null(failure);
                Assert.False(File.Exists(Path.Combine(directory, "capture.txt")));
            }
            finally { Monitor.Exit(gate); }
            WaitForDeferredDrain();
            Assert.Equal("[10:11:12.345] first /media?token=<redacted>",
                Assert.Single(File.ReadAllLines(Path.Combine(directory, "capture.txt.1"))));
            Assert.EndsWith("second", Assert.Single(File.ReadAllLines(Path.Combine(directory, "capture.txt"))));
            Assert.False(File.Exists(Path.Combine(directory, "capture.txt.2")));
        }
        finally
        {
            if ((sta.ThreadState & ThreadState.Unstarted) == 0) sta.Join(TimeSpan.FromSeconds(5));
            WaitForDeferredDrain();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteThroughUiAppendsReachTheFileBeforeReturningWithoutWaitingOnTheWorker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-crash-log-" + Guid.NewGuid().ToString("N"));
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var gate = typeof(LocalLog).GetField("s_gate", flags)!.GetValue(null)!;
        var seam = typeof(LocalLog).GetField("s_avoidBlockingWait", flags)!;
        var original = seam.GetValue(null);
        var uiThreadId = -1;
        seam.SetValue(null, new Func<bool>(() => Environment.CurrentManagedThreadId == Volatile.Read(ref uiThreadId)));
        try
        {
            string[]? crashSeenByHandler = null;
            var ui = new Thread(() =>
            {
                Volatile.Write(ref uiThreadId, Environment.CurrentManagedThreadId);
                using (LocalLog.BeginWriteThrough())
                {
                    LocalLog.AppendLine("crash.txt", "fatal /media?token=fixture-secret", directory);
                    LocalLog.AppendException("crash.txt", new InvalidOperationException("boom"), "fixture", logDirectory: directory);
                    var crash = Path.Combine(directory, "crash.txt");
                    crashSeenByHandler = File.Exists(crash) ? File.ReadAllLines(crash) : [];
                }
                LocalLog.AppendLine("after.txt", "queued again", directory);
            }) { IsBackground = true };

            // The worker holds the gate for another file, as during a slow disk write.
            Monitor.Enter(gate);
            try
            {
                ui.Start();
                Assert.True(ui.Join(TimeSpan.FromSeconds(3)), "A write-through append waited behind the held log gate.");
                Assert.NotNull(crashSeenByHandler);
                Assert.NotEmpty(crashSeenByHandler!);
                Assert.EndsWith("fatal /media?token=<redacted>", crashSeenByHandler![0]);
                Assert.Contains(crashSeenByHandler, line => line.Contains("message=boom", StringComparison.Ordinal));
                Assert.DoesNotContain(crashSeenByHandler, line => line.Contains("fixture-secret", StringComparison.Ordinal));
                // The scope ended: ordinary UI appends hand off again.
                Assert.False(File.Exists(Path.Combine(directory, "after.txt")));
            }
            finally { Monitor.Exit(gate); }

            WaitForDeferredDrain();
            Assert.EndsWith("queued again", Assert.Single(File.ReadAllLines(Path.Combine(directory, "after.txt"))));
        }
        finally
        {
            seam.SetValue(null, original);
            WaitForDeferredDrain();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CrashHandlersWriteThroughAndWorkerThreadFailuresAreRecorded()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "SiloPlayer", "App.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        var app = File.ReadAllText(Path.Combine(root!.FullName, "src", "SiloPlayer", "App.xaml.cs"));

        var xaml = app.IndexOf("this.UnhandledException +=", StringComparison.Ordinal);
        var domain = app.IndexOf("AppDomain.CurrentDomain.UnhandledException +=", StringComparison.Ordinal);
        Assert.True(xaml >= 0 && domain >= 0);
        foreach (var start in new[] { xaml, domain })
        {
            var scope = app.IndexOf("LocalLog.BeginWriteThrough()", start, StringComparison.Ordinal);
            var firstWrite = app.IndexOf("LocalLog.Append", start, StringComparison.Ordinal);
            Assert.True(scope > start && scope < firstWrite, "A crash handler appends before entering write-through.");
        }
        Assert.Contains("\"AppDomain.UnhandledException\"", app);
    }

    private static void WaitForDeferredDrain()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var drain = typeof(LocalLog).GetField("s_drainingDeferred", flags)!;
        var queue = typeof(LocalLog).GetField("s_deferred", flags)!.GetValue(null)!;
        var isEmpty = queue.GetType().GetProperty("IsEmpty")!;
        Assert.True(SpinWait.SpinUntil(() => (int)drain.GetValue(null)! == 0 && (bool)isEmpty.GetValue(queue)!, TimeSpan.FromSeconds(5)),
            "The background log worker did not finish before fixture cleanup.");
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
