using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class NotificationsRecoveryBehaviorTests
{
    [Fact]
    public async Task FailedMarkAllKeepsErrorAndRequiresExplicitReload()
    {
        using var wire = new Wire(); var vm = wire.ViewModel();
        Assert.False(vm.CanMarkAllRead);
        await vm.LoadCommand.ExecuteAsync(null); Assert.True(vm.CanMarkAllRead);
        await vm.MarkAllReadCommand.ExecuteAsync(null);
        Assert.Contains("Failed to mark", vm.ErrorMessage);
        Assert.False(vm.CanMarkAllRead);
        Assert.Equal(1, wire.ListReads);
        await vm.ReloadAsync(); Assert.Null(vm.ErrorMessage); Assert.True(vm.CanMarkAllRead);
        Assert.Equal(2, wire.ListReads);
    }

    [Fact]
    public async Task MarkAllDisablesWhileMutationIsPending()
    {
        using var wire = new Wire { DelayMutation = true }; var vm = wire.ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        var pending = vm.MarkAllReadCommand.ExecuteAsync(null);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsMarkingAllRead); Assert.False(vm.CanMarkAllRead);
        wire.Complete.SetResult(new(HttpStatusCode.NoContent));
        await pending; Assert.False(vm.IsMarkingAllRead);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int ListReads;
        public bool DelayMutation;
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NotificationsViewModel ViewModel() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://notifications-fixture.invalid"); return new(new NotificationsApi(client)); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/read-all")) { Started.TrySetResult(true); return DelayMutation ? Complete.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":\"fixture_error\"}") }); }
            var json = path.EndsWith("/unread-count") ? "{\"count\":1}" : path.EndsWith("/preferences") ? "{\"enabled\":true}"
                : "{\"items\":[{\"id\":\"one\"}],\"read_cutoff\":\"fixture-cutoff\"}";
            if (path.EndsWith("/notifications")) ListReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
