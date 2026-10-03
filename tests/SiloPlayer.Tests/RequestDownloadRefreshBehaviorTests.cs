using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RequestDownloadRefreshBehaviorTests
{
    [Fact]
    public async Task CompletedMineAndBrandsDoNotWaitForTheIndependentDiscoveryRead()
    {
        using var wire = new IndependentWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://requests-independent.invalid");
        var vm = new RequestsViewModel(new RequestsApi(client));
        var loading = vm.LoadCommand.ExecuteAsync(null);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (vm.IsLoading || vm.MyRequests.Count != 1 || vm.Genres.Count != 1)
            {
                Assert.True(DateTime.UtcNow < deadline, "Independently completed account/brand state remained hidden while discovery was pending.");
                await Task.Delay(10);
            }
            Assert.False(vm.IsLoading);
            Assert.Single(vm.MyRequests);
            Assert.Single(vm.Genres);
            Assert.True((bool)typeof(RequestsViewModel).GetProperty("IsLoadingDiscovery")!.GetValue(vm)!);
            Assert.False((bool)typeof(RequestsViewModel).GetProperty("IsLoadingMine")!.GetValue(vm)!);
            await vm.RefreshDownloadsAsync(CancellationToken.None);
            Assert.Equal(2, wire.MineReads);
        }
        finally { wire.Pending.SetResult("{\"items\":[]}"); await loading; }
    }

    private sealed class IndependentWire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<string> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int MineReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/mine")) MineReads++;
            if (path.EndsWith("/discover")) { Started.SetResult(); return new(HttpStatusCode.OK) { Content = new StringContent(await Pending.Task) }; }
            var json = path.EndsWith("/status") ? "{\"requests_enabled\":true}" : path.EndsWith("/mine") ? "{\"items\":[{\"id\":\"mine\",\"download\":{\"phase\":\"downloading\",\"percent\":42}}]}"
                : path.EndsWith("/genres") ? "{\"items\":[{\"slug\":\"drama\"}]}" : "{\"items\":[]}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
    [Fact]
    public async Task DownloadRefreshRetainsRowsOnFailureAndStopsAfterDownloadsFinish()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://requests-poll.invalid");
        var vm = new RequestsViewModel(new RequestsApi(client));
        await vm.LoadCommand.ExecuteAsync(null);
        var refresh = typeof(RequestsViewModel).GetMethod("RefreshDownloadsAsync");
        Assert.NotNull(refresh);
        var row = Assert.Single(vm.MyRequests); wire.Fail = true;
        await (Task)refresh.Invoke(vm, [CancellationToken.None])!;
        Assert.Same(row, Assert.Single(vm.MyRequests)); Assert.False(vm.IsLoading);
        Assert.Equal(2, wire.MineReads);
        wire.Fail = false; wire.Finished = true;
        await (Task)refresh.Invoke(vm, [CancellationToken.None])!;
        Assert.Null(Assert.Single(vm.MyRequests).Download);
        await (Task)refresh.Invoke(vm, [CancellationToken.None])!;
        Assert.Equal(3, wire.MineReads);
    }

    [Fact]
    public async Task TargetDownloadRefreshDoesNotOverlapOrPublishAfterNavigationCancellation()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://requests-poll.invalid");
        var vm = new RequestsViewModel(new RequestsApi(client));
        await vm.LoadCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.MyRequests); row.Download = null;
        row.Targets = [new() { Download = new() { Percent = 42 } }];
        var refresh = typeof(RequestsViewModel).GetMethod("RefreshDownloadsAsync");
        Assert.NotNull(refresh);
        wire.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var owner = new CancellationTokenSource();
        var first = (Task)refresh.Invoke(vm, [owner.Token])!;
        await (Task)refresh.Invoke(vm, [CancellationToken.None])!;
        Assert.Equal(2, wire.MineReads);
        owner.Cancel(); wire.Pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") });
        await first;
        Assert.Same(row, Assert.Single(vm.MyRequests));
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int MineReads; public bool Fail, Finished;
        public TaskCompletionSource<HttpResponseMessage>? Pending;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/status") ? "{\"requests_enabled\":true}" : "{\"items\":[]}";
            if (path.EndsWith("/mine"))
            {
                MineReads++;
                if (Pending != null) return Pending.Task;
                if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                json = "{\"items\":[{\"id\":\"r1\",\"title\":\"Fixture\",\"status\":\"processing\"" +
                    (Finished ? "" : ",\"download\":{\"percent\":42}") + "}]}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
