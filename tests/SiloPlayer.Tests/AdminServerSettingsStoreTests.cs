using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminServerSettingsStoreTests
{
    [Fact]
    public async Task EffectiveReadsAreSingleFlightAndRetirementDropsCache()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        using var store = new AdminServerSettingsStore(new(kit.Client), authority, ["branding.server_name"], ["redis.url"]);
        var first = store.GetEffectiveAsync(lease);
        var second = store.GetEffectiveAsync(lease);
        var response = AdminMonitorKit.Json("""{"branding.server_name":"Silo"}""");
        response.Headers.ETag = new("\"one\"");
        pending.SetResult(response);
        await Task.WhenAll(first, second);
        Assert.Single(kit.Http.Requests);
        await store.GetEffectiveAsync(lease);
        Assert.Single(kit.Http.Requests);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.GetEffectiveAsync(lease, canceled.Token));
        }
        authority.Retire();
        using var replacement = authority.Capture();
        kit.Http.Respond = (_, _) => { var next = AdminMonitorKit.Json("{}"); next.Headers.ETag = new("\"two\""); return Task.FromResult(next); };
        Assert.Equal("\"two\"", (await store.GetEffectiveAsync(replacement)).ETag);
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task OneDepartingReaderDoesNotCancelAnotherReader()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var firstOwner = authority.Capture();
        using var secondOwner = authority.Capture();
        using var store = new AdminServerSettingsStore(new(kit.Client), authority, ["branding.server_name"], []);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, token) => pending.Task.WaitAsync(token);
        var first = store.GetEffectiveAsync(firstOwner);
        var second = store.GetEffectiveAsync(secondOwner);
        firstOwner.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var response = AdminMonitorKit.Json("{}"); response.Headers.ETag = new("\"one\"");
        pending.SetResult(response);
        Assert.Equal("\"one\"", (await second).ETag);
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task LastDepartingReaderCancelsTheRequest()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var owner = authority.Capture();
        using var store = new AdminServerSettingsStore(new(kit.Client), authority, ["branding.server_name"], []);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { canceled.SetResult(); }
            return AdminMonitorKit.Json("{}");
        };
        var read = store.GetEffectiveAsync(owner);
        await started.Task;
        owner.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(kit.Http.Requests);
    }
}
