using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

internal static class IncrementalUpdateTransport
{
    internal static ReleaseVersion? ValidateAssets(AppRelease release)
    {
        var assets = release.Incremental ?? throw new InvalidDataException("The release has no incremental package.");
        if (!GitHubReleaseReader.TryReleaseIdentity(release.Tag, release.Tag.Contains('-'), out var tagged, out var semantic)
            || GitHubReleaseReader.Normalize(tagged) != GitHubReleaseReader.Normalize(release.Version)
            || (semantic?.IsPrerelease == true && !release.IsBeta)
            || (release.SemanticVersion is { } declared && declared.Text != semantic?.Text))
            throw new InvalidDataException("The incremental release identity is invalid.");
        var prefix = GitHubReleaseReader.RepositoryUrl + "/releases/download/" + release.Tag + "/";
        if (assets.ManifestUrl.AbsoluteUri != prefix + UpdateManifestValidation.ManifestName || assets.PackUrl.AbsoluteUri != prefix + UpdateManifestValidation.PackName ||
            assets.ManifestSize is <= 0 or > UpdateManifestValidation.MaximumManifestBytes || assets.PackSize is <= 0 or > UpdateManifestValidation.MaximumPackBytes ||
            !UpdateManifestValidation.IsHash(assets.ManifestSha256) || !UpdateManifestValidation.IsHash(assets.PackSha256))
            throw new InvalidDataException("The incremental asset identity is invalid.");
        return semantic;
    }

    internal static async Task<HttpResponseMessage> OpenAsync(HttpClient http, Uri uri, long? from, long? to, CancellationToken ct)
    {
        if (http.DefaultRequestHeaders.Authorization is not null) throw new InvalidDataException("Update transport must be unauthenticated.");
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("SiloDesktopApp-Updater");
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (from is not null) request.Headers.Range = new RangeHeaderValue(from, to);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            try
            {
                if (!Trusted(response.RequestMessage?.RequestUri ?? uri)) throw new InvalidDataException("Untrusted update download host.");
                new GitHubReleaseReader(http).ThrowIfRateLimited(response);
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                    return response;
                if (response.Headers.Location is not { } location || !Uri.TryCreate(uri, location, out var next) || !Trusted(next))
                    throw new InvalidDataException("The update redirected outside GitHub download hosts.");
                uri = next;
            }
            catch { response.Dispose(); throw; }
            response.Dispose();
        }
        throw new InvalidDataException("The update redirected too many times.");
    }

    private static bool Trusted(Uri uri) => uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.IsDefaultPort &&
        uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    internal static void ValidateBody(HttpResponseMessage response, long length)
    {
        if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) ||
            response.Content.Headers.ContentLength is { } size && size != length)
            throw new InvalidDataException("The update response has an unexpected encoding or length.");
    }

    internal static async Task<byte[]> FetchManifestAsync(HttpClient http, AppRelease release, CancellationToken ct)
    {
        var semantic = ValidateAssets(release); var assets = release.Incremental!;
        using var response = await OpenAsync(http, assets.ManifestUrl, null, null, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Manifest download failed.", null, response.StatusCode);
        ValidateBody(response, assets.ManifestSize);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var bytes = await UpdateManifestValidation.ReadBoundedAsync(stream, (int)assets.ManifestSize, ct).ConfigureAwait(false);
        if (bytes.Length != assets.ManifestSize || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(assets.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manifest checksum mismatch.");
        var manifest = UpdateManifestValidation.Parse(bytes, compatibilityProbe: true);
        if (manifest.Version != GitHubReleaseReader.Normalize(release.Version).ToString(4) || manifest.PackSize != assets.PackSize ||
            !manifest.PackSha256.Equals(assets.PackSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest release/pack identity mismatch.");
        if ((semantic?.IsPrerelease == true && manifest.ReleaseVersion is null)
            || (manifest.ReleaseVersion is not null && manifest.ReleaseVersion != semantic?.Text))
            throw new InvalidDataException("Manifest semantic release identity mismatch.");
        if (manifest.Schema != 1 || manifest.BootstrapProtocol != 1 || !manifest.BackwardCompatibleData)
            throw new UpdateMigrationRequiredException();
        return bytes;
    }
}
