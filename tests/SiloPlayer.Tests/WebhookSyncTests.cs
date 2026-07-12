using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class WebhookSyncTests
{
    [Fact]
    public async Task ConnectionsUseCurrentServerContract()
    {
        var handler = new JsonHandler("""
        [{"id":"c1","provider":"plex","server_id":"machine-1","server_name":"Living Room Plex","default_profile_id":"p1","webhook_url":"https://silo.test/api/v1/webhook-sync/webhooks/secret","actor_count":2,"account_discovery_available":true,"last_webhook_received_at":"2026-07-11T12:00:00Z"}]
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.test");

        var connection = Assert.Single(await new WebhookSyncApi(client).GetConnectionsAsync());

        Assert.Equal("machine-1", connection.ServerId);
        Assert.Equal("Living Room Plex", connection.ServerName);
        Assert.Equal("p1", connection.DefaultProfileId);
        Assert.Equal(2, connection.ActorCount);
        Assert.True(connection.AccountDiscoveryAvailable);
    }

    [Fact]
    public void SettingsPageUsesRealWebhookSyncActionsInsteadOfPlaceholderCopy()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml.cs"));
        Assert.Contains("CreateConnectionAsync", code);
        Assert.Contains("UpdateConnectionAsync", code);
        Assert.Contains("RotateWebhookAsync", code);
        Assert.Contains("DeleteConnectionAsync", code);
        Assert.Contains("UpdateActorsAsync", code);
        Assert.Contains("GetEventsAsync", code);
        Assert.DoesNotContain("Connection name (e.g. Living Room Plex)", code);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
