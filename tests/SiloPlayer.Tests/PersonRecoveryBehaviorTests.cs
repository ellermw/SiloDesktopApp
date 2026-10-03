using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class PersonRecoveryBehaviorTests
{
    [Fact]
    public async Task MissingViewReadOutranksCachedPersonAndClearsOldFilmography()
    {
        using var wire = new Wire { PersonStatus = HttpStatusCode.NotFound }; var vm = wire.Model();
        vm.Person = new Person { Id = "person-fixture", Name = "Cached person" };
        vm.Filmography.Add(new() { ContentId = "stale-film" });
        await vm.LoadCommand.ExecuteAsync("person-fixture");
        Assert.Null(vm.Person); Assert.Empty(vm.Filmography); Assert.Equal("Person not found.", vm.ErrorMessage);
        vm.Cancel();
    }

    [Fact]
    public async Task FilmographyFailureDoesNotTurnSuccessfulPersonReadIntoMissingPerson()
    {
        using var wire = new Wire { FilmographyStatus = HttpStatusCode.NotFound }; var vm = wire.Model();
        await vm.LoadCommand.ExecuteAsync("person-fixture");
        Assert.Equal("Fixture person", vm.Person?.Name);
        Assert.NotNull(vm.ErrorMessage); Assert.NotEqual("Person not found.", vm.ErrorMessage);
        Assert.False(vm.IsLoading); vm.Cancel();
    }

    [Fact]
    public async Task TemporaryPersonReadFailureKeepsCachedPersonAndRetryRecovers()
    {
        using var wire = new Wire { PersonStatus = HttpStatusCode.ServiceUnavailable }; var vm = wire.Model();
        var cached = new Person { Id = "person-fixture", Name = "Cached person" }; vm.Person = cached;
        await vm.LoadCommand.ExecuteAsync("person-fixture"); Assert.Same(cached, vm.Person); Assert.NotNull(vm.ErrorMessage);
        wire.PersonStatus = HttpStatusCode.OK; await vm.LoadCommand.ExecuteAsync("person-fixture");
        Assert.Equal("Fixture person", vm.Person?.Name); Assert.Null(vm.ErrorMessage); vm.Cancel();
    }

    private sealed class Wire : HttpMessageHandler
    {
        public HttpStatusCode PersonStatus = HttpStatusCode.OK;
        public HttpStatusCode FilmographyStatus = HttpStatusCode.OK;
        public PersonDetailViewModel Model()
        {
            var api = new SiloApiClient(new HttpClient(this)); api.SetBaseUrl("https://person-recovery.invalid");
            return new(new PeopleApi(api), new CatalogApi(api), api);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var person = request.RequestUri!.AbsolutePath.StartsWith("/api/v2/catalog/people/");
            var status = person ? PersonStatus : FilmographyStatus;
            var body = status != HttpStatusCode.OK ? "{\"message\":\"Fixture unavailable\"}" : person
                ? "{\"id\":\"person-fixture\",\"name\":\"Fixture person\",\"bio\":\"Complete biography\",\"birth_date\":\"1980-01-01\",\"birthplace\":\"Fixture\"}"
                : "{\"items\":[],\"page\":{\"has_more\":false}}";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
