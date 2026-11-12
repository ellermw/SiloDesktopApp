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
}
