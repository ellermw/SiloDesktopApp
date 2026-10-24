using System.Net;
using System.Text;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public class PersonalListMembershipTests
{
    [Fact]
    public async Task Capable_server_reads_membership_without_legacy_404_probes()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://membership.fixture.invalid");
        var api = new CatalogApi(client);
        Assert.False(await api.GetFavoriteItemAsync("absent"));
        Assert.True(await api.GetWatchlistItemAsync("member"));
        Assert.Single(wire.Paths.Where(p => p == "/api/v2/capabilities/personal-lists"));
        Assert.DoesNotContain(wire.Paths, p => p.EndsWith("/absent") || p.EndsWith("/member"));
    }

    [Theory]
    [InlineData(404)] [InlineData(405)] [InlineData(501)] [InlineData(200)]
    public async Task Older_server_keeps_legacy_semantics(int status)
    {
        using var wire = new Wire { CapabilityStatus = status, Supported = false }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://membership.fixture.invalid");
        var api = new CatalogApi(client);
        Assert.False(await api.GetFavoriteItemAsync("absent"));
        Assert.True(await api.GetWatchlistItemAsync("member"));
        Assert.DoesNotContain(wire.Paths, p => p.EndsWith("/membership"));
    }

    [Fact]
    public async Task Capability_failure_is_not_cached_as_unsupported_and_inaccessible_item_does_not_probe_legacy()
    {
        using var wire = new Wire { CapabilityStatus = 503 }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://membership.fixture.invalid");
        var api = new CatalogApi(client);
        await Assert.ThrowsAsync<ApiException>(() => api.GetFavoriteItemAsync("absent"));
        wire.CapabilityStatus = 200;
        Assert.False(await api.GetFavoriteItemAsync("absent"));
        await Assert.ThrowsAsync<ApiException>(() => api.GetFavoriteItemAsync("inaccessible"));
        Assert.DoesNotContain("/api/v2/favorites/inaccessible", wire.Paths);
    }

    [Fact]
    public async Task Profile_switch_retires_capability_reply_and_captures_new_owner()
    {
        using var wire = new Wire { HoldCapability = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://membership.fixture.invalid"); client.SetProfile("alpha");
        var api = new CatalogApi(client); var pending = api.GetFavoriteItemAsync("member");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        client.SetProfile("beta"); wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        wire.HoldCapability = false;
        Assert.True(await api.GetFavoriteItemAsync("member"));
        Assert.Equal(2, wire.Paths.Count(p => p == "/api/v2/capabilities/personal-lists"));
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int CapabilityStatus = 200;
        public bool Supported = true, HoldCapability;
        public List<string> Paths = [];
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            if (path == "/api/v2/capabilities/personal-lists")
            {
                Started.TrySetResult(); if (HoldCapability) await Release.Task.WaitAsync(ct);
                return Reply(CapabilityStatus, "{\"membership_supported\":" + Supported.ToString().ToLowerInvariant() + "}");
            }
            if (path.EndsWith("/inaccessible/membership")) return Reply(404, "{}");
            if (path.EndsWith("/membership"))
                return Reply(200, "{\"item_id\":\"" + (path.Contains("/member/") ? "member" : "absent") + "\",\"is_member\":" + path.Contains("/member/").ToString().ToLowerInvariant() + "}");
            return Reply(path.EndsWith("/absent") ? 404 : 200, "{}");
        }
        private static HttpResponseMessage Reply(int status, string json) => new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
