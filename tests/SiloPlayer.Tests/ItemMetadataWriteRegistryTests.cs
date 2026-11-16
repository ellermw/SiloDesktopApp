using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ItemMetadataWriteRegistryTests
{
    private static readonly ApiIdentityContext Owner = new(1, 1, "https://server.invalid", "profile-a");
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<MediaItemDetail> Fresh(CancellationToken _) => Task.FromResult(new MediaItemDetail { ContentId = "item", Title = "Fresh", LockedFields = [10] });

    [Theory]
    [InlineData("image")]
    [InlineData("reset")]
    public async Task CloseAndReopenWaitsForWriteAndRefetch(string kind)
    {
        var registry = new ItemMetadataWriteRegistry();
        using var firstDialog = new CancellationTokenSource();
        var write = Gate<bool>(); var refetch = Gate<MediaItemDetail>();
        CancellationToken operation = default;
        var running = registry.RunAsync(Owner, "item", async ct => { operation = ct; await write.Task.WaitAsync(ct); },
            ct => refetch.Task.WaitAsync(ct), firstDialog.Token);
        firstDialog.Cancel();
        Assert.False(operation.IsCancellationRequested);
        Assert.True(registry.IsPending(Owner, "item"));
        using var reopened = new CancellationTokenSource();
        var observer = registry.WaitForIdleAsync(Owner, "item", reopened.Token);
        Assert.False(observer.IsCompleted);
        write.SetResult(true);
        Assert.True(registry.IsPending(Owner, "item"));
        Assert.False(observer.IsCompleted);
        var fresh = await Fresh(default); fresh.Title = kind;
        refetch.SetResult(fresh);
        await running;
        Assert.Same(fresh, await observer);
        Assert.False(registry.IsPending(Owner, "item"));
    }

    [Fact]
    public async Task ClosingOneObserverDoesNotReleaseOrCancelAWrite()
    {
        var registry = new ItemMetadataWriteRegistry(); var gate = Gate<bool>();
        var running = registry.RunAsync(Owner, "item", ct => gate.Task.WaitAsync(ct), Fresh, default);
        using var closed = new CancellationTokenSource();
        var observer = registry.WaitForIdleAsync(Owner, "item", closed.Token); closed.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer);
        Assert.True(registry.IsPending(Owner, "item"));
        var reopened = registry.WaitForIdleAsync(Owner, "item", default);
        gate.SetResult(true); await running;
        Assert.Equal(new[] { 10 }, (await reopened)!.LockedFields);
    }

    [Fact]
    public async Task ProfileServerAndAccountGenerationsIsolateEntries()
    {
        var registry = new ItemMetadataWriteRegistry(); var gate = Gate<bool>();
        var running = registry.RunAsync(Owner, "item", ct => gate.Task.WaitAsync(ct), Fresh, default);
        foreach (var other in new[] { Owner with { ProfileId = "profile-b" }, Owner with { BaseUrl = "https://other.invalid" },
            Owner with { AuthenticationGeneration = 2 }, Owner with { IdentityGeneration = 2 } })
        {
            Assert.False(registry.IsPending(other, "item"));
            Assert.Null(await registry.WaitForIdleAsync(other, "item", default));
        }
        Assert.False(registry.IsPending(Owner, "other-item"));
        gate.SetResult(true); await running;
    }

    [Fact]
    public async Task OverlappingWritesReleaseOnlyAfterBothRefetches()
    {
        var registry = new ItemMetadataWriteRegistry(); var first = Gate<bool>(); var second = Gate<bool>();
        var a = registry.RunAsync(Owner, "item", ct => first.Task.WaitAsync(ct), Fresh, default);
        var b = registry.RunAsync(Owner, "item", ct => second.Task.WaitAsync(ct), Fresh, default);
        var reopened = registry.WaitForIdleAsync(Owner, "item", default);
        first.SetResult(true); await a;
        Assert.True(registry.IsPending(Owner, "item")); Assert.False(reopened.IsCompleted);
        second.SetResult(true); await b; await reopened;
        Assert.False(registry.IsPending(Owner, "item"));
    }

    [Fact]
    public async Task FailedWriteStillReconcilesBeforeReleasingObservers()
    {
        var registry = new ItemMetadataWriteRegistry(); var read = Gate<MediaItemDetail>();
        var running = registry.RunAsync(Owner, "item", _ => Task.FromException(new InvalidOperationException("write")),
            ct => read.Task.WaitAsync(ct), default);
        var reopened = registry.WaitForIdleAsync(Owner, "item", default);
        Assert.True(registry.IsPending(Owner, "item"));
        read.SetResult(await Fresh(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => running);
        Assert.NotNull(await reopened); Assert.False(registry.IsPending(Owner, "item"));
    }

    [Fact]
    public async Task SuccessfulWriteWithFailedRefetchRemainsSuccessfulAndRequiresRetry()
    {
        var registry = new ItemMetadataWriteRegistry(); var write = Gate<bool>();
        var writes = 0;
        var running = registry.RunAsync(Owner, "item", async ct => { await write.Task.WaitAsync(ct); writes++; },
            _ => Task.FromException<MediaItemDetail>(new HttpRequestException("refetch")), default);
        var observer = registry.WaitForIdleAsync(Owner, "item", default);
        write.SetResult(true);
        Assert.Null(await running);
        Assert.Null(await observer);
        Assert.Equal(1, writes);
        Assert.False(registry.IsPending(Owner, "item"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderRefetchFinishingLastCannotReplaceLatestCommit(bool olderReadFails)
    {
        var registry = new ItemMetadataWriteRegistry();
        var oldRead = Gate<MediaItemDetail>();
        var a = registry.RunAsync(Owner, "item", _ => Task.CompletedTask, ct => oldRead.Task.WaitAsync(ct), default);
        var newest = await Fresh(default); newest.Title = "Latest commit"; newest.LockedFields = [3, 10];
        var b = registry.RunAsync(Owner, "item", _ => Task.CompletedTask, _ => Task.FromResult(newest), default);
        Assert.Same(newest, await b);
        var observer = registry.WaitForIdleAsync(Owner, "item", default);
        if (olderReadFails) oldRead.SetException(new HttpRequestException("old read"));
        else oldRead.SetResult(new MediaItemDetail { ContentId = "item", Title = "Before latest commit", LockedFields = [3] });
        await a;
        Assert.Same(newest, await observer);
    }

    [Fact]
    public async Task FailedLatestRefetchCannotExposeAnEarlierCommitsLocks()
    {
        var registry = new ItemMetadataWriteRegistry(); var secondWrite = Gate<bool>(); var oldRead = Gate<MediaItemDetail>();
        var a = registry.RunAsync(Owner, "item", _ => Task.CompletedTask, ct => oldRead.Task.WaitAsync(ct), default);
        var b = registry.RunAsync(Owner, "item", ct => secondWrite.Task.WaitAsync(ct),
            _ => Task.FromException<MediaItemDetail>(new HttpRequestException("latest read")), default);
        var observer = registry.WaitForIdleAsync(Owner, "item", default);
        oldRead.SetResult(await Fresh(default)); await a;
        secondWrite.SetResult(true);
        Assert.Null(await b);
        Assert.Null(await observer);
    }

    [Fact]
    public async Task AuthorityRetirementCancelsBackgroundWorkAndReleasesEntry()
    {
        var registry = new ItemMetadataWriteRegistry(); using var authority = new CancellationTokenSource();
        var running = registry.RunAsync(Owner, "item", ct => Task.Delay(Timeout.Infinite, ct), Fresh, default, authority.Token);
        authority.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.False(registry.IsPending(Owner, "item"));
    }

    [Fact]
    public async Task RevisionDetectsAWriteThatSettledBetweenFetchAndDialogOpen()
    {
        var registry = new ItemMetadataWriteRegistry();
        var openingRevision = registry.Revision;
        var gate = Gate<bool>();
        var running = registry.RunAsync(Owner, "item", ct => gate.Task.WaitAsync(ct), Fresh, default);
        var pendingRevision = registry.Revision;
        Assert.NotEqual(openingRevision, pendingRevision);
        gate.SetResult(true); await running;
        Assert.NotEqual(pendingRevision, registry.Revision);
        Assert.False(registry.IsPending(Owner, "item"));
    }

    [Fact]
    public async Task AlreadyClosedDialogCannotStartAWrite()
    {
        var registry = new ItemMetadataWriteRegistry(); using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.RunAsync(Owner, "item",
            _ => throw new InvalidOperationException("Must not write"), Fresh, lifetime.Token));
        Assert.False(registry.IsPending(Owner, "item"));
    }
}
