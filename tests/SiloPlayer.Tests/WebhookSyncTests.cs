using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class WebhookSyncTests
{
    [Fact]
    public async Task RelativeReceiverUrlsAreResolvedForListCreateUpdateAndRotate()
    {
        const string receiver = "/api/v2/webhook-sync/webhooks/synthetic-receiver";
        const string absolute = "https://silo.test" + receiver;
        var connectionJson = "{\"id\":\"c1\",\"webhook_url\":\"" + receiver + "\"}";
        SiloApiClient Client(string json)
        {
            var client = new SiloApiClient(new HttpClient(new JsonHandler(json)));
            client.SetBaseUrl("https://silo.test");
            client.SetAccessToken("token");
            return client;
        }

        var listed = await new WebhookSyncApi(Client("{\"items\":[" + connectionJson + "],\"page\":{\"has_more\":false}}"))
            .GetConnectionsAsync();
        Assert.Equal(absolute, Assert.Single(listed).WebhookUrl);

        var created = await new WebhookSyncApi(Client("{\"connection\":" + connectionJson + ",\"webhook_url\":\"" + receiver + "\"}"))
            .CreateConnectionAsync(new() { ["provider"] = "plex", ["server_name"] = "Test", ["default_profile_id"] = "p1" });
        Assert.Equal(absolute, created.WebhookUrl);
        Assert.Equal(absolute, created.Connection.WebhookUrl);

        var updated = await new WebhookSyncApi(Client(connectionJson)).UpdateConnectionAsync("c1", new() { ["server_name"] = "Renamed" });
        Assert.Equal(absolute, updated.WebhookUrl);

        var rotated = await new WebhookSyncApi(Client("{\"webhook_url\":\"" + receiver + "\"}")).RotateWebhookAsync("c1");
        Assert.Equal(absolute, rotated.WebhookUrl);
    }

    [Fact]
    public async Task ConnectionsUseCurrentServerContract()
    {
        var handler = new JsonHandler("""
        {"items":[{"id":"c1","provider":"plex","server_id":"machine-1","server_name":"Living Room Plex","default_profile_id":"p1","webhook_url":"https://silo.test/api/v2/webhook-sync/webhooks/secret","user_count":2,"account_discovery_available":true,"last_webhook_received_at":"2026-07-11T12:00:00Z"}],"page":{"has_more":false}}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.test");
        client.SetAccessToken("token");

        var connection = Assert.Single(await new WebhookSyncApi(client).GetConnectionsAsync());

        Assert.Equal("machine-1", connection.ServerId);
        Assert.Equal("Living Room Plex", connection.ServerName);
        Assert.Equal("p1", connection.DefaultProfileId);
        Assert.Equal(2, connection.UserCount);
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
        Assert.Contains("UpdateProfileMappingsAsync", code);
        Assert.Contains("GetProfileMappingsAsync", code);
        Assert.Contains("GetEventsAsync", code);
        Assert.Contains("PlexBrowserAuthApi", code);
        Assert.Contains("WebhookEventUserLabel", code);
        Assert.Contains("ShowWebhookEventDetailAsync", code);
        Assert.DoesNotContain("Connection name (e.g. Living Room Plex)", code);

        var api = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "WebhookSyncApi.cs"));
        Assert.Contains("/profile-mappings", api);
        Assert.DoesNotContain("/actors", api);

        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        Assert.Contains("WebhookPlexSignInButton", xaml);
        Assert.Contains("WebhookEventSearchBox", xaml);
        Assert.Contains("Save mappings", xaml);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
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
