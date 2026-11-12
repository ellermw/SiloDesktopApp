using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateStagingGateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestartOrLaterCannotBeOfferedBeforeWorkerInstallationFinishes(bool restart)
    {
        var installed = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var choices = 0;
        var reports = new List<string>();
        var transaction = Guid.NewGuid().ToString("N");
        var pending = UpdateStagingGate.PrepareAndChooseAsync(
            ct => ++reads == 1 ? Task.FromResult(new UpdateHostMessage("progress", Text:"Installing alongside current version")) : installed.Task.WaitAsync(ct),
            (frame, _) => { reports.Add(frame.Text!); return Task.CompletedTask; },
            (frame, _) => { Assert.Equal(transaction, frame.TransactionId); choices++; return Task.FromResult(restart); },
            default);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, choices);
        Assert.Single(reports);
        installed.SetResult(new("staged", transaction, "1.2.974.0"));
        var result = await pending;
        Assert.Equal(restart, result.Restart);
        Assert.Equal(transaction, result.Staged.TransactionId);
        Assert.Equal(1, choices);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("downloaded")]
    [InlineData("staged")]
    public async Task FailureOrIncompleteStageNeverOffersAReadyChoice(string kind)
    {
        var offered = false;
        var error = await Record.ExceptionAsync(() => UpdateStagingGate.PrepareAndChooseAsync(
            _ => Task.FromResult(new UpdateHostMessage(kind)),
            (_, _) => Task.CompletedTask,
            (_, _) => { offered = true; return Task.FromResult(true); }, default));
        Assert.IsType(kind == "failed" ? typeof(IOException) : typeof(InvalidDataException), error);
        Assert.False(offered);
    }

    [Fact]
    public async Task CancellationWhileStagingCannotOfferRestart()
    {
        using var cancellation = new CancellationTokenSource();
        var held = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offered = false;
        var pending = UpdateStagingGate.PrepareAndChooseAsync(
            ct => held.Task.WaitAsync(ct), (_, _) => Task.CompletedTask,
            (_, _) => { offered = true; return Task.FromResult(true); }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(offered);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosingSiloWhileTheChoiceIsOpenIsAbandonmentNotAFailureOrAnAnswer(bool pipeBroke)
    {
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = UpdateStagingGate.AnswerOrAbandonAsync(choice.Task, client.Task, default);
        Assert.False(pending.IsCompleted);
        // Silo's exit closes its pipe; any frame it wrote instead still means it stopped waiting.
        if (pipeBroke) client.SetException(new EndOfStreamException());
        else client.SetResult(new("cancel"));
        Assert.Null(await pending);
        Assert.False(choice.Task.IsCompleted);
        _ = client.Task.Exception;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerIsReturnedWhileSiloStaysConnected(bool restart)
    {
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = UpdateStagingGate.AnswerOrAbandonAsync(choice.Task, client.Task, default);
        Assert.False(pending.IsCompleted);
        choice.SetResult(restart);
        Assert.Equal<bool?>(restart, await pending);
    }

    [Fact]
    public async Task CancellationWhileTheChoiceIsOpenIsNeitherAnAnswerNorAbandonment()
    {
        using var cancellation = new CancellationTokenSource();
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = UpdateStagingGate.AnswerOrAbandonAsync(choice.Task, client.Task, cancellation.Token);
        // The broker's token cancels its client read too; that must not be mistaken for Silo closing.
        cancellation.Cancel();
        client.SetCanceled(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosingSiloDuringTheApprovalWaitIsNoticedBeforeTheWorkerReportsAnything(bool pipeBroke)
    {
        var client = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.False(UpdateStagingGate.ClientLeft(null));
        Assert.False(UpdateStagingGate.ClientLeft(client.Task));
        if (pipeBroke) client.SetException(new EndOfStreamException());
        else client.SetResult(new("cancel"));
        Assert.True(UpdateStagingGate.ClientLeft(client.Task));
        // Nothing more is written to a Silo that has gone.
        var writes = 0;
        Assert.False(await UpdateStagingGate.ForwardOrAbandonAsync(client.Task, () => { writes++; return Task.CompletedTask; }));
        Assert.Equal(0, writes);
        _ = client.Task.Exception;
    }

    [Theory]
    [InlineData(true)]  // Silo's pipe closed during the administrator approval, before any worker frame.
    [InlineData(false)] // The pipe breaks under a forwarded progress frame.
    public async Task ClosingSiloWhileTheWorkerInstallsLetsStagingFinishWithoutFailingOrAsking(bool duringApproval)
    {
        var client = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (duringApproval) client.SetException(new EndOfStreamException());
        var transaction = Guid.NewGuid().ToString("N");
        var worker = new Queue<UpdateHostMessage>(new UpdateHostMessage[]
        {
            new("progress", Text: "Verifying release authority…"),
            new("progress", Text: "Installing alongside current version"),
            new("staged", transaction, "1.2.974.0"),
        });
        var abandoned = false;
        var asked = false;
        var writes = 0;
        // The broker's wiring: stop writing to Silo once it has gone, and answer for it instead of asking.
        var result = await UpdateStagingGate.PrepareAndChooseAsync(
            _ => Task.FromResult(worker.Dequeue()),
            async (_, _) =>
            {
                if (!abandoned) abandoned = !await UpdateStagingGate.ForwardOrAbandonAsync(client.Task,
                    () => { writes++; return Task.FromException(new IOException("Pipe is broken.")); });
            },
            (_, _) =>
            {
                abandoned |= UpdateStagingGate.ClientLeft(client.Task);
                if (abandoned) return Task.FromResult(false);
                asked = true;
                return Task.FromResult(true);
            },
            default);
        // The worker reached "staged" and is answered with defer, exactly as Later would.
        Assert.True(abandoned);
        Assert.False(asked);
        Assert.False(result.Restart);
        Assert.Equal(transaction, result.Staged.TransactionId);
        Assert.Equal(duringApproval ? 0 : 1, writes);
        Assert.Empty(worker);
        _ = client.Task.Exception;
    }

    [Fact]
    public async Task ProgressReachesAConnectedSiloAndOtherWriteFailuresRemainFailures()
    {
        var client = new TaskCompletionSource<UpdateHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        Assert.True(await UpdateStagingGate.ForwardOrAbandonAsync(client.Task, () => { writes++; return Task.CompletedTask; }));
        Assert.Equal(1, writes);
        // A broker that does not watch Silo keeps its broken pipe as a failure.
        await Assert.ThrowsAsync<IOException>(() => UpdateStagingGate.ForwardOrAbandonAsync(null,
            () => Task.FromException(new IOException("Pipe is broken."))));
        // The broker's own cancellation is neither Silo leaving nor a delivered frame.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        client.SetCanceled(cancellation.Token);
        Assert.False(UpdateStagingGate.ClientLeft(client.Task));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateStagingGate.ForwardOrAbandonAsync(client.Task,
            () => Task.FromCanceled(cancellation.Token)));
    }
}
