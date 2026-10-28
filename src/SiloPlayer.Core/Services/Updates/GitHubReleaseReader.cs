using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

internal sealed class GitHubReleaseReader(HttpClient http)
{
    internal const long MaximumInstallerBytes = 512L * 1024 * 1024;
    internal const int MaximumMetadataBytes = 512 * 1024;
    internal const string InstallerName = "SiloInstaller-Windows-x64.exe";
    internal const string RepositoryUrl = "https://github.com/ellermw/SiloDesktopApp";

    public async Task<AppRelease?> ReadAsync(Version installed, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/ellermw/SiloDesktopApp/releases/latest");
        request.Headers.UserAgent.ParseAdd("SiloDesktopApp/" + installed);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
        using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
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
        return new AppRelease(version, tag, releaseNotes,
            published, url, digest[7..].ToLowerInvariant(), size);
    }

    internal static Version Normalize(Version version) => new(version.Major, version.Minor,
        Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
