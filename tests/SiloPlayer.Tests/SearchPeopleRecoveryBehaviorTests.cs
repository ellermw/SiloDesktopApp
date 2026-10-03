using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class SearchPeopleRecoveryBehaviorTests
{
    [Fact]
    public async Task PeopleFailureShowsIndependentRetryAndRetryKeepsCatalogResults()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = new SearchViewModel(new CatalogApi(client), new PeopleApi(client), new RequestsApi(client), new SettingsApi(client)) { Query = "Heat" };
        await vm.SearchCommand.ExecuteAsync(null);
        await Task.Delay(50);
        Assert.NotNull(vm.PeopleError);
        Assert.Single(vm.Results);
        wire.FailPeople = false;
        await vm.RetryPeopleAsync();
        Assert.Null(vm.PeopleError);
        Assert.Equal("person-1", Assert.Single(vm.PeopleResults).Id);
        Assert.Equal(1, wire.CatalogRequests);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool FailPeople { get; set; } = true;
        public int CatalogRequests { get; private set; }
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://people-recovery.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var status = HttpStatusCode.OK;
            var body = "{\"requests_enabled\":false}";
            if (path == "/api/v2/catalog/search/capabilities") body = "{\"people_media_scope\":true}";
            if (path == "/api/v2/catalog") { CatalogRequests++; body = "{\"items\":[{\"content_id\":\"movie-1\",\"title\":\"Heat\"}],\"page\":{\"has_more\":false}}"; }
            if (path == "/api/v2/catalog/people") { status = FailPeople ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK; body = FailPeople ? "{\"message\":\"Unavailable\"}" : "{\"items\":[{\"id\":\"person-1\",\"name\":\"Al Pacino\"}]}"; }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
