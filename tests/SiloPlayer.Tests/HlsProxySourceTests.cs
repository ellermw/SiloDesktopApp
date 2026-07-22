namespace SiloPlayer.Tests;

public sealed class HlsProxySourceTests
{
    [Fact]
    public void SegmentBodiesHaveAnIdleDeadlineAfterResponseHeadersArrive()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer", "Services", "HlsProxy.cs"));

        Assert.Contains("UpstreamIdleTimeout = TimeSpan.FromSeconds(20)", source);
        Assert.Contains("CopyWithIdleTimeoutAsync(remoteStream, stream, ct)", source);
        Assert.Contains("ReadPlaylistWithIdleTimeoutAsync(response.Content, ct)", source);
        Assert.Contains("idleCts.CancelAfter(UpstreamIdleTimeout)", source);
        Assert.Contains("catch (OperationCanceledException) when (!ct.IsCancellationRequested)", source);
        Assert.Contains("No HLS media data arrived", source);
        Assert.DoesNotContain("remoteStream.CopyToAsync(stream, ct)", source);
    }

    [Fact]
    public void StartupAndSegmentRequestsRetryTransientCdnStatuses()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer", "Services", "HlsProxy.cs"));

        Assert.Contains("HttpStatusCode.RequestTimeout", source);
        Assert.Contains("HttpStatusCode.TooManyRequests", source);
        Assert.Contains("HttpStatusCode.BadGateway", source);
        Assert.Contains("HttpStatusCode.ServiceUnavailable", source);
        Assert.Contains("HttpStatusCode.GatewayTimeout", source);
    }

    [Fact]
    public void EveryHlsTransportBypassesStaleManifestCaches()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer", "Services", "HlsProxy.cs"));

        Assert.Contains("AddManifestCacheBuster(remoteManifestUri)", source);
        Assert.Contains("silo_v=", source);
        Assert.Contains("request.Headers.CacheControl", source);
        Assert.Contains("NoCache = true", source);
        Assert.Contains("NoStore = true", source);
        Assert.Contains("IsPlaylistUrl(request.RequestUri)", source);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathParts)}.");
    }
}
