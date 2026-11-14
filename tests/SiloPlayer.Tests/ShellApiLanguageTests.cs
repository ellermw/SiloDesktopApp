using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

public sealed class ShellApiLanguageTests
{
    [Fact]
    public async Task EachRequestUsesTheEffectiveLanguageWithoutChangingSharedClientDefaults()
    {
        var wire = new Wire();
        using var http = new HttpClient(wire);
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr");
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://shell-language.invalid");
        client.SetAccessToken("synthetic-access");
        client.SetProfile("owner-a");
        var owner = client.CaptureContext();
        foreach (var language in new[] { "de", "ar" })
        {
            using var locale = AppStrings.UseForCurrentFlow(LocaleService.Shared.Create(
                AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
            await client.GetAsync<object>("/api/v2/auth/me");
            await client.GetUnauthenticatedAsync<object>("/api/v2/branding");
            await client.PostUnauthenticatedAsync<object>("/api/v2/auth/login", new { username = "fixture" });
            await client.SendRawJsonRequestAsync<object>(owner, HttpMethod.Put, "/api/v2/settings/values/ui.app_language", "{}"u8.ToArray());
            await client.GetWithBearerAsync<object>(client.BaseUrl, "/api/v2/auth/me", "synthetic-other");
        }
        Assert.Equal(Enumerable.Repeat("de", 5).Concat(Enumerable.Repeat("ar", 5)), wire.Requests.Select(row => row.Language));
        Assert.All(wire.Requests, row => Assert.StartsWith("/api/v2/", row.Path));
        Assert.Equal("fr", Assert.Single(http.DefaultRequestHeaders.AcceptLanguage).Value);
        Assert.Equal(owner, client.CaptureContext());
        foreach (var offset in new[] { 0, 5 })
        {
            Assert.Equal("Bearer synthetic-access", wire.Requests[offset].Authorization);
            Assert.Equal("owner-a", wire.Requests[offset].Profile);
            Assert.Null(wire.Requests[offset + 1].Authorization);
            Assert.Null(wire.Requests[offset + 2].Authorization);
            Assert.Equal("owner-a", wire.Requests[offset + 3].Profile);
            Assert.Equal("Bearer synthetic-other", wire.Requests[offset + 4].Authorization);
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal List<(string Language, string Path, string? Authorization, string? Profile)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((string.Join(",", request.Headers.AcceptLanguage.Select(value => value.Value)),
                request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : null));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
