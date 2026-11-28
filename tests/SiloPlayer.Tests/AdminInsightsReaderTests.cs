using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The per-tab Insights read (official <c>useAdminInsights*</c>, c59798116): fresh for 45 seconds, previous result kept while another key
/// loads (tabs only), a failed refresh keeps the data, nothing outlives its authority.
/// </summary>
public sealed class AdminInsightsReaderTests : IDisposable
{
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly DashboardManualTime _time = new();

    public AdminInsightsReaderTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private sealed record Result(string Name);

    private AdminInsightsReader<Result> Reader(bool keepPrevious = true) => new(_authority.Capture(), keepPrevious, _time);

    private static Func<CancellationToken, Task<Result>> Returns(string name, List<string>? log = null)
        => _ => { log?.Add(name); return Task.FromResult(new Result(name)); };

    [Fact]
    public async Task AReadIsFreshForFortyFiveSecondsAndThenReadAgain()
    {
        var reads = new List<string>();
        using var reader = Reader();
        await reader.Request("a", Returns("one", reads));
        Assert.Equal("one", reader.View.Data!.Name);
        Assert.False(reader.View.IsPlaceholder);

        _time.Advance(TimeSpan.FromSeconds(44));
        await reader.Request("a", Returns("two", reads));
        Assert.Equal(["one"], reads);

        _time.Advance(TimeSpan.FromSeconds(2));
        await reader.Request("a", Returns("two", reads));
        Assert.Equal(["one", "two"], reads);
        Assert.Equal("two", reader.View.Data!.Name);
    }

    [Fact]
    public async Task RefreshReadsAgainWhateverTheAgeAndKeepsTheResultOnScreenWhileItRuns()
    {
        var gate = new TaskCompletionSource<Result>();
        using var reader = Reader();
        await reader.Request("a", Returns("one"));
        var refresh = reader.Request("a", _ => gate.Task, force: true);
        var during = reader.View;
        Assert.Equal("one", during.Data!.Name);
        Assert.False(during.IsPlaceholder);
        Assert.True(during.IsFetching);
        gate.SetResult(new Result("two"));
        await refresh;
        Assert.Equal("two", reader.View.Data!.Name);
        Assert.False(reader.View.IsFetching);
    }

    [Fact]
    public async Task ATabKeepsThePreviousResultAsAPlaceholderWhileAnotherKeyLoads()
    {
        var gate = new TaskCompletionSource<Result>();
        using var reader = Reader(keepPrevious: true);
        await reader.Request("plays", Returns("by plays"));
        var pending = reader.Request("watch_time", _ => gate.Task);
        var view = reader.View;
        Assert.Equal("by plays", view.Data!.Name);
        Assert.True(view.IsPlaceholder);
        Assert.False(view.IsLoading);
        gate.SetResult(new Result("by watch time"));
        await pending;
        Assert.Equal("by watch time", reader.View.Data!.Name);
        Assert.False(reader.View.IsPlaceholder);

        // Going back to a key already read shows it at once, with no placeholder and no read.
        var reads = new List<string>();
        await reader.Request("plays", Returns("again", reads));
        Assert.Empty(reads);
        Assert.Equal("by plays", reader.View.Data!.Name);
    }

    [Fact]
    public async Task APanelNeverShowsAnotherSubjectsNumbersWhileItLoads()
    {
        var gate = new TaskCompletionSource<Result>();
        using var reader = Reader(keepPrevious: false);
        await reader.Request("title 1", Returns("title 1"));
        var pending = reader.Request("title 2", _ => gate.Task);
        var view = reader.View;
        Assert.Null(view.Data);
        Assert.True(view.IsLoading);
        gate.SetResult(new Result("title 2"));
        await pending;
        Assert.Equal("title 2", reader.View.Data!.Name);
    }

    [Fact]
    public async Task AFailedReadOfANewKeyShowsTheErrorAndNoPlaceholder()
    {
        using var reader = Reader();
        await reader.Request("a", Returns("one"));
        await reader.Request("b", _ => Task.FromException<Result>(new InvalidOperationException("The server refused.")));
        var view = reader.View;
        Assert.Null(view.Data);
        Assert.True(view.ShowsError);
        Assert.Equal("The server refused.", view.Error!.Message);
        Assert.False(view.IsLoading);
    }

    [Fact]
    public async Task AFailedRefreshKeepsTheLastDataAndReportsTheError()
    {
        using var reader = Reader();
        await reader.Request("a", Returns("one"));
        await reader.Request("a", _ => Task.FromException<Result>(new HttpRequestException("offline")), force: true);
        var view = reader.View;
        Assert.Equal("one", view.Data!.Name);
        Assert.False(view.ShowsError);
        Assert.Equal("offline", view.RefreshError!.Message);

        // The next successful read clears it.
        await reader.Request("a", Returns("three"), force: true);
        Assert.Null(reader.View.RefreshError);
        Assert.Equal("three", reader.View.Data!.Name);
    }

    [Fact]
    public async Task AFailedKeyIsNotReadAgainUntilRetryOrItsFreshnessLapses()
    {
        var attempts = 0;
        Func<CancellationToken, Task<Result>> failing = _ => { attempts++; return Task.FromException<Result>(new InvalidOperationException("no")); };
        using var reader = Reader();
        await reader.Request("a", failing);
        await reader.Request("a", failing);
        Assert.Equal(1, attempts);
        await reader.Request("a", failing, force: true);
        Assert.Equal(2, attempts);
        _time.Advance(TimeSpan.FromSeconds(46));
        await reader.Request("a", failing);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ATimeoutIsAnErrorNotASilentCancellation()
    {
        using var reader = Reader();
        await reader.Request("a", _ => Task.FromException<Result>(new TaskCanceledException("The Insights read timed out.", new TimeoutException())));
        Assert.True(reader.View.ShowsError);
        Assert.Equal("The Insights read timed out.", reader.View.Error!.Message);
    }

    [Fact]
    public async Task PendingReadsAndCachedEntriesStayBoundedAndOnlyTheLatestOverflowRuns()
    {
        using var reader = Reader();
        var gates = Enumerable.Range(0, 32).Select(_ => new TaskCompletionSource<Result>()).ToArray();
        var started = 0;
        var held = gates.Select((gate, index) => reader.Request(index.ToString(), _ => { started++; return gate.Task; })).ToArray();
        var skipped = reader.Request("skipped", _ => { started++; return Task.FromResult(new Result("skipped")); });
        var latest = new TaskCompletionSource<Result>();
        var waiting = reader.Request("latest", _ => { started++; return latest.Task; });
        Assert.Equal(32, started);
        Assert.Equal(32, reader.EntryCount);
        Assert.True(skipped.IsCompletedSuccessfully);
        Assert.False(waiting.IsCompleted);
        Assert.True(reader.View.IsLoading);

        gates[0].SetResult(new Result("0"));
        await held[0].WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(33, started);
        Assert.Equal(32, reader.EntryCount);
        latest.SetResult(new Result("latest"));
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("latest", reader.View.Data!.Name);

        for (var index = 1; index < gates.Length; index++)
        {
            gates[index].SetResult(new Result(index.ToString()));
            await held[index].WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(32, reader.EntryCount);
    }

    [Fact]
    public async Task ASupersededReadNeverOverwritesTheNewerOne()
    {
        var first = new TaskCompletionSource<Result>();
        using var reader = Reader();
        var older = reader.Request("a", ct => first.Task.WaitAsync(ct));
        await reader.Request("a", Returns("newer"), force: true);
        first.TrySetResult(new Result("older"));
        await older;
        Assert.Equal("newer", reader.View.Data!.Name);
    }

    [Fact]
    public async Task UnmountCancelsPendingWorkButKeepsTheFreshResultForAReturn()
    {
        using var reader = Reader();
        await reader.Request("cached", Returns("cached"));
        var held = new TaskCompletionSource<Result>();
        CancellationToken token = default;
        var read = reader.Request("other", ct => { token = ct; return held.Task; });
        reader.Suspend();
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(AdminInsightsReadView<Result>.Idle, reader.View);

        var reads = new List<string>();
        await reader.Request("cached", Returns("unexpected", reads));
        held.SetResult(new Result("discard"));
        await read.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(reads);
        Assert.Equal("cached", reader.View.Data!.Name);
        Assert.Equal(1, reader.EntryCount);
    }

    [Fact]
    public async Task NothingIsPublishedAfterTheAuthorityRetires()
    {
        var gate = new TaskCompletionSource<Result>();
        using var reader = Reader();
        var changes = 0;
        reader.Changed += () => changes++;
        var pending = reader.Request("a", _ => gate.Task);
        var before = changes;
        _kit.SetRole("user");
        gate.SetResult(new Result("from the previous account"));
        await pending;
        Assert.Equal(before, changes);
        Assert.Null(reader.View.Data);

        // A retired lease reads nothing at all.
        var reads = new List<string>();
        await reader.Request("b", Returns("b", reads));
        Assert.Empty(reads);
    }

    [Fact]
    public async Task SuspendAfterARetiredRefreshCompletesDoesNotCancelItsDisposedSource()
    {
        using var reader = Reader();
        await reader.Request("a", Returns("cached"));
        var gate = new TaskCompletionSource<Result>();
        var refresh = reader.Request("a", _ => gate.Task, force: true);

        _authority.Retire();
        gate.SetResult(new Result("retired refresh"));
        await refresh;

        reader.Suspend();
        Assert.Null(reader.Key);
        Assert.Equal(AdminInsightsReadView<Result>.Idle, reader.View);
    }

    [Fact]
    public async Task ChangedIsRaisedForTheRequestedKeyOnly()
    {
        var gate = new TaskCompletionSource<Result>();
        using var reader = Reader();
        var changes = 0;
        reader.Changed += () => changes++;
        var first = reader.Request("a", _ => gate.Task);
        await reader.Request("b", Returns("b"));
        var after = changes;
        gate.SetResult(new Result("a"));
        await first;
        Assert.Equal(after, changes);
        Assert.Equal("b", reader.View.Data!.Name);
    }

    [Fact]
    public async Task AskingAgainForTheSameFreshOrLoadingKeyRaisesNothingSoADrawingCallerCannotLoopOnItself()
    {
        var gate = new TaskCompletionSource<Result>();
        using var reader = Reader(keepPrevious: false);
        var changes = 0;
        reader.Changed += () => changes++;
        var first = reader.Request("a", _ => gate.Task);
        Assert.Equal(1, changes);
        // Still loading: asking again joins the read and says nothing.
        await Task.WhenAny(reader.Request("a", _ => gate.Task), Task.Delay(10));
        Assert.Equal(1, changes);
        gate.SetResult(new Result("a"));
        await first;
        var settled = changes;
        // Fresh: asking again, as every redraw of the title panel does, says nothing either.
        for (var i = 0; i < 5; i++) await reader.Request("a", Returns("again"));
        Assert.Equal(settled, changes);
        Assert.Equal("a", reader.View.Data!.Name);
        // Another key is a change.
        await reader.Request("b", Returns("b"));
        Assert.True(changes > settled);
    }
}
