using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>A release check result and the validator that lets GitHub answer the next check with 304.</summary>
internal sealed record ReleaseCheck(AppRelease? Release, string? ETag);

/// <summary>GitHub limited unauthenticated checks from this network; retry no sooner than <see cref="RetryAfter"/>.</summary>
internal sealed class UpdateCheckRateLimitedException(System.Net.HttpStatusCode status, TimeSpan? retryAfter)
    : HttpRequestException(UserMessage, null, status)
{
    internal const string UserMessage = "GitHub is limiting update checks from this network. Silo will try again later.";
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

internal sealed class GitHubReleaseReader(HttpClient http, TimeProvider? clock = null)
{
    internal const long MaximumInstallerBytes = 512L * 1024 * 1024;
    internal const int MaximumMetadataBytes = 512 * 1024;
    internal const string InstallerName = "SiloInstaller-Windows-x64.exe";
    internal const string RepositoryUrl = "https://github.com/ellermw/SiloDesktopApp";

    public async Task<AppRelease?> ReadAsync(Version installed, CancellationToken ct)
        => (await ReadAsync(installed, previous: null, ct).ConfigureAwait(false)).Release;

    /// <summary>
    /// Unauthenticated GitHub API calls share 60 requests an hour per IP
    /// address. Conditional requests save metadata bytes; the primary-quota
    /// exemption for 304 requires authentication, which this client does not send.
    /// </summary>
    public async Task<ReleaseCheck> ReadAsync(Version installed, ReleaseCheck? previous, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/ellermw/SiloDesktopApp/releases/latest");
        request.Headers.UserAgent.ParseAdd("SiloDesktopApp/" + installed);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        if (previous?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && previous is not null)
            return previous;
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests && IsRateLimited(response))
            throw new UpdateCheckRateLimitedException(response.StatusCode, RetryDelay(response));
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaximumMetadataBytes)
            throw new InvalidDataException("Update metadata is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + read > MaximumMetadataBytes) throw new InvalidDataException("Update metadata is too large.");
            bytes.Write(buffer, 0, read);
        }
        return new ReleaseCheck(Parse(bytes.ToArray(), installed), response.Headers.ETag?.ToString());
    }

    private static bool IsRateLimited(HttpResponseMessage response)
        => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || response.Headers.RetryAfter is not null ||
            (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0");

    private TimeSpan? RetryDelay(HttpResponseMessage response)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (response.Headers.RetryAfter is { } retry)
            return retry.Delta ?? (retry.Date is { } date ? date - now : null);
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) &&
            long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds) - now; }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return null;
    }

    private static AppRelease? Parse(byte[] metadata, Version installed)
    {
        using var json = JsonDocument.Parse(metadata, new JsonDocumentOptions { MaxDepth = 24 });
        var root = json.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (tag.Length > 64 || !Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(?:\.\d+)?$", RegexOptions.CultureInvariant)
            || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new InvalidDataException("The release has an unsupported version.");
        version = Normalize(version);
        if (version <= Normalize(installed)) return null;
        var matches = root.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == InstallerName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The release has no unique Windows x64 installer.");
        var asset = matches[0];
        if (asset.GetProperty("state").GetString() != "uploaded") throw new InvalidDataException("The installer is not ready.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumInstallerBytes) throw new InvalidDataException("The installer has an invalid size.");
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71
            || !digest.AsSpan(7).ToArray().All(char.IsAsciiHexDigit))
            throw new InvalidDataException("The release does not provide a valid SHA256 checksum.");
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
            || url.AbsoluteUri != $"{RepositoryUrl}/releases/download/{tag}/{InstallerName}")
            throw new InvalidDataException("The installer URL is outside the expected release.");
        if (!DateTimeOffset.TryParse(root.GetProperty("published_at").GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var published)) throw new InvalidDataException("The release date is invalid.");
        var releaseNotes = root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "";
        if (releaseNotes.Length > 32768)
        {
            var length = char.IsHighSurrogate(releaseNotes[32767]) ? 32767 : 32768;
            releaseNotes = releaseNotes[..length] + "\n\nAdditional release notes are available on GitHub.";
        }
        var release = new AppRelease(version, tag, releaseNotes,
            published, url, digest[7..].ToLowerInvariant(), size);
        var manifestAssets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == UpdateManifestValidation.ManifestName).ToArray();
        var packAssets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == UpdateManifestValidation.PackName).ToArray();
        if (manifestAssets.Length != 0 || packAssets.Length != 0)
        {
            try
            {
                if (manifestAssets.Length != 1 || packAssets.Length != 1) throw new InvalidDataException("The incremental assets are incomplete or duplicated.");
                var manifest = ReadIncrementalAsset(manifestAssets[0], tag, UpdateManifestValidation.ManifestName, UpdateManifestValidation.MaximumManifestBytes);
                var pack = ReadIncrementalAsset(packAssets[0], tag, UpdateManifestValidation.PackName, UpdateManifestValidation.MaximumPackBytes);
                release = release with { Incremental = new(manifest.Url, manifest.Hash, manifest.Size, pack.Url, pack.Hash, pack.Size) };
            }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // Optional new assets cannot hide an otherwise valid full installer from legacy callers.
                // Incremental authority separately requires a complete validated pair below.
            }
        }
        return release;
    }

    private static (Uri Url, string Hash, long Size) ReadIncrementalAsset(JsonElement asset, string tag, string name, long maximum)
    {
        var digest = asset.GetProperty("digest").GetString() ?? "";
        var size = asset.GetProperty("size").GetInt64();
        if (asset.GetProperty("state").GetString() != "uploaded" || size <= 0 || size > maximum ||
            !digest.StartsWith("sha256:", StringComparison.Ordinal) || !UpdateManifestValidation.IsHash(digest[7..]) ||
            !Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) ||
            url.AbsoluteUri != $"{RepositoryUrl}/releases/download/{tag}/{name}")
            throw new InvalidDataException("The incremental asset identity is invalid.");
        return (url, digest[7..].ToLowerInvariant(), size);
    }

    internal void ThrowIfRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests && IsRateLimited(response))
            throw new UpdateCheckRateLimitedException(response.StatusCode, RetryDelay(response));
    }

    internal async Task<AppRelease> ReadTagAsync(string tag, CancellationToken ct)
    {
        if (tag.Length > 64 || !Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(?:\.\d+)?$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid release tag.");
        if (http.DefaultRequestHeaders.Authorization is not null) throw new InvalidDataException("Update authority must be unauthenticated.");
        var uri = new Uri("https://api.github.com/repos/ellermw/SiloDesktopApp/releases/tags/" + tag);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SiloDesktopApp-Updater");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri is { } final && final != uri) throw new InvalidDataException("Release authority redirected.");
        ThrowIfRateLimited(response);
        if (response.StatusCode != System.Net.HttpStatusCode.OK) throw new HttpRequestException("Could not establish release authority.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw new InvalidDataException("Update metadata is too large.");
        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var bytes = await UpdateManifestValidation.ReadBoundedAsync(body, MaximumMetadataBytes, ct).ConfigureAwait(false);
        var release = Parse(bytes, new Version(0, 0, 0, 0)) ?? throw new InvalidDataException("Release is not eligible for installation.");
        if (release.Tag != tag) throw new InvalidDataException("Release tag identity mismatch.");
        IncrementalUpdateTransport.ValidateAssets(release);
        return release;
    }

    internal static Version Normalize(Version version) => new(version.Major, version.Minor,
        Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
