using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminAppearanceApiTests
{
    [Fact]
    public async Task UploadUsesOneLeaseBoundMultipartAssignment()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        string? content = null;
        kit.Http.Respond = async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/v2/admin/branding/assets/wordmark", request.RequestUri!.AbsolutePath);
            Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
            content = await request.Content.ReadAsStringAsync(cancellation);
            return AdminMonitorKit.Json("""{"kind":"wordmark","ref":"fixture-ref","url":"/api/v2/branding/assets/wordmark?v=fixture-ref"}""");
        };
        var result = await new AdminAppearanceApi(kit.Client).UploadAssetAsync(lease, "wordmark", [1, 2, 3], "image/png");
        Assert.Equal("wordmark", result.Kind);
        Assert.Contains("file", content);
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    public async Task AssetWritesAreNeverReplayed(HttpStatusCode status, bool uncertain)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", status));
        var api = new AdminAppearanceApi(kit.Client);
        if (uncertain)
            await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => api.UploadAssetAsync(lease, "mark", [1], "image/png"));
        else
            await Assert.ThrowsAsync<ApiException>(() => api.UploadAssetAsync(lease, "mark", [1], "image/png"));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"mark\",\"ref\":\"fixture-ref\",\"url\":\"/api/v2/branding/assets/fixture-ref\"}")]
    [InlineData("{\"kind\":\"favicon\",\"ref\":\"fixture-ref\",\"url\":\"/api/v2/branding/assets/fixture-ref\"}")]
    [InlineData("{\"kind\":\"mark\",\"ref\":\"fixture-ref\",\"url\":\"https://other.invalid/image.png\"}")]
    public async Task UnprovenUploadAcknowledgementIsUnknown(string body)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
        await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => new AdminAppearanceApi(kit.Client)
            .UploadAssetAsync(lease, "mark", [1], "image/png"));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("wordmark_light", "image/png")]
    [InlineData("../mark", "image/png")]
    [InlineData("mark", "text/html")]
    public async Task InvalidAssetIntentNeverReachesTransport(string kind, string mediaType)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminAppearanceApi(kit.Client).UploadAssetAsync(lease, kind, [1], mediaType));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task DeleteRequiresNoContentAndNeverRetriesAnUnclearReply()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminAppearanceApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await api.DeleteAssetAsync(lease, "favicon");
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}"));
        await Assert.ThrowsAsync<AdminAppearanceOutcomeUnknownException>(() => api.DeleteAssetAsync(lease, "favicon"));
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.All(kit.Http.Requests, request => Assert.StartsWith("DELETE", request));
    }

    [Fact]
    public async Task RetiredOwnerCannotSendAnAssetWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        lease.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminAppearanceApi(kit.Client).UploadAssetAsync(lease, "mark", [1], "image/png"));
        Assert.Empty(kit.Http.Requests);
    }
}
