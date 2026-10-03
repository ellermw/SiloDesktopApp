using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class PersonRefreshBehaviorTests
{
    [Fact]
    public async Task SlowAutomaticAdministratorRefreshDoesNotHideSuccessfullyReadPerson()
    {
        using var wire = new Wire { IncompleteRead = true, Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var vm = wire.Model(); vm.ActingAdmin = true;
        var load = vm.LoadCommand.ExecuteAsync("person-fixture");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Equal("Incomplete person", vm.Person?.Name);
            Assert.False(vm.IsLoading);
            Assert.True(vm.IsRefreshing);
        }
        finally { wire.Delay.SetResult(); await load; vm.Cancel(); }
    }
    [Fact]
    public async Task ExplicitViewerRefreshForIncompletePersonQueuesOnlyOnce()
    {
        using var wire = new Wire(); var vm = wire.Model();
        vm.Person = new Person { Id = "person-fixture", Name = "Incomplete person" };
        await vm.RefreshAsync(false);
        Assert.Equal(["/api/v2/catalog/people/person-fixture/refresh"], wire.Posts);
        vm.Cancel();
    }

    [Fact]
    public async Task IncompletePersonViewUsesAdministratorRefreshWhenActingAsAdmin()
    {
        using var wire = new Wire { IncompleteRead = true }; var vm = wire.Model(); vm.ActingAdmin = true;
        await vm.LoadCommand.ExecuteAsync("person-fixture");
        Assert.Equal(["/api/v2/admin/people/person-fixture/refresh"], wire.Posts);
        Assert.Equal("Updated person", vm.Person?.Name); vm.Cancel();
    }
    [Fact]
    public async Task AdministratorRefreshWaitsForProviderAndPublishesReturnedPerson()
    {
        using var wire = new Wire(); var vm = wire.Model();
        vm.Person = new Person { Id = "person-fixture", Name = "Cached person" };
        await vm.RefreshAsync(true);
        Assert.Equal(["/api/v2/admin/people/person-fixture/refresh"], wire.Posts);
        Assert.Equal("Updated person", vm.Person?.Name); vm.Cancel();
    }

    [Fact]
    public async Task ViewerRefreshQueuesWorkWithoutDiscardingDisplayedPerson()
    {
        using var wire = new Wire(); var vm = wire.Model();
        var cached = new Person { Id = "person-fixture", Name = "Cached person", Bio = "Complete", BirthDate = "1980-01-01", PhotoUrl = "https://person-refresh.invalid/photo" }; vm.Person = cached;
        await vm.RefreshAsync(false);
        Assert.Equal(["/api/v2/catalog/people/person-fixture/refresh"], wire.Posts);
        Assert.Same(cached, vm.Person); vm.Cancel();
    }

    [Fact]
    public async Task NavigationDuringAdministratorRefreshRejectsItsLateResponse()
    {
        using var wire = new Wire { Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var vm = wire.Model();
        vm.Person = new Person { Id = "person-fixture", Name = "Cached person" };
        var pending = vm.RefreshAsync(true); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.Cancel(); wire.Delay.SetResult();
        try { await pending; } catch (OperationCanceledException) { }
        Assert.Equal("Cached person", vm.Person?.Name);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> Posts { get; } = [];
        public TaskCompletionSource? Delay;
        public bool IncompleteRead;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PersonDetailViewModel Model()
        {
            var api = new SiloApiClient(new HttpClient(this)); api.SetBaseUrl("https://person-refresh.invalid");
            return new(new PeopleApi(api), new CatalogApi(api), api);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Post)
                return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.StartsWith("/api/v2/catalog/people/") ? IncompleteRead ? "{\"id\":\"person-fixture\",\"name\":\"Incomplete person\"}" : "{\"id\":\"person-fixture\",\"name\":\"Read person\",\"bio\":\"Complete\",\"birth_date\":\"1980-01-01\",\"photo_url\":\"https://person-refresh.invalid/photo\"}" : "{\"items\":[],\"page\":{\"has_more\":false}}") };
            Posts.Add(request.RequestUri!.AbsolutePath); Started.TrySetResult();
            if (Delay != null) await Delay.Task;
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.StartsWith("/api/v2/admin/")
                ? "{\"id\":\"person-fixture\",\"name\":\"Updated person\",\"bio\":\"Complete\",\"birth_date\":\"1980-01-01\",\"photo_url\":\"https://person-refresh.invalid/photo\"}"
                : "{\"queued\":true}") };
        }
    }
}
