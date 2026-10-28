using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

internal sealed class AppUpdateDownloader(HttpClient http, string directory, Action<string>? log)
{
    public async Task<string> DownloadAsync(AppRelease release, Action<long> progress, CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetFullPath(directory), GitHubReleaseReader.Normalize(release.Version).ToString());
        EnsureOrdinaryPath(folder);
        Directory.CreateDirectory(folder);
        EnsureOrdinaryPath(folder);
        var ready = Path.Combine(folder, GitHubReleaseReader.InstallerName);
        var partial = ready + ".part";
        var identityFile = Path.Combine(folder, "package.identity");
        foreach (var path in new[] { ready, partial, identityFile }) EnsureOrdinaryPath(path);
        if (await VerifyAsync(ready, release, ct).ConfigureAwait(false)) return ready;
        if (File.Exists(ready)) File.Delete(ready);
        var identity = $"{release.Tag}\n{release.Size}\n{release.Sha256}";
        var matching = File.Exists(identityFile) && await File.ReadAllTextAsync(identityFile, ct).ConfigureAwait(false) == identity;
        if (!matching && File.Exists(partial)) File.Delete(partial);
        await File.WriteAllTextAsync(identityFile, identity, ct).ConfigureAwait(false);
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset >= release.Size)
        {
            if (offset == release.Size && await VerifyAsync(partial, release, ct).ConfigureAwait(false))
            { File.Move(partial, ready); return ready; }
            File.Delete(partial); offset = 0;
        }
        using var response = await OpenAsync(release.InstallerUrl, offset, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Installer download returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The installer response has an unexpected encoding.");
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range is null || range.Unit != "bytes" || range.From != offset || range.To != release.Size - 1 || range.Length != release.Size)
                throw new InvalidDataException("The installer range does not match the requested update.");
        }
        else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
        else throw new InvalidDataException("Unexpected installer response.");
        if (response.Content.Headers.ContentLength is { } size && size != release.Size - offset)
            throw new InvalidDataException("The installer response has an unexpected length.");
        await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append,
            FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var bytes = offset; var buffer = new byte[65536]; int count;
            progress(bytes);
            while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                if (bytes + count > release.Size) throw new InvalidDataException("The installer exceeds its advertised size.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                bytes += count; progress(bytes);
            }
            await output.FlushAsync(ct).ConfigureAwait(false);
            if (bytes != release.Size) throw new InvalidDataException("The installer download was incomplete.");
        }
        if (!await VerifyAsync(partial, release, ct).ConfigureAwait(false))
        {
            File.Delete(partial);
            throw new InvalidDataException("The installer checksum does not match the published release. Download it again.");
        }
        File.Move(partial, ready, overwrite: true);
        log?.Invoke($"download_verified version={release.Version} bytes={release.Size}");
        return ready;
    }

    public static Task<bool> VerifyAsync(string path, AppRelease release, CancellationToken ct)
        => Task.Run(() => VerifyCoreAsync(path, release, ct), ct);

    private static async Task<bool> VerifyCoreAsync(string path, AppRelease release, CancellationToken ct)
    {
        EnsureOrdinaryPath(path);
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != release.Size) return false;
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> OpenAsync(Uri uri, long offset, CancellationToken ct)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("SiloDesktopApp-Updater");
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            if (!TrustedDownloadHost(finalUri))
            {
                response.Dispose();
                throw new InvalidDataException("The installer response came from an untrusted download host.");
            }
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || !Uri.TryCreate(uri, location, out var next) || !TrustedDownloadHost(next))
                throw new InvalidDataException("The installer redirected outside GitHub's HTTPS download hosts.");
            uri = next;
        }
        throw new InvalidDataException("The installer redirected too many times.");
    }

    private static bool TrustedDownloadHost(Uri uri) => uri.Scheme == "https" && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0
        && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    internal static void EnsureOrdinaryPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The update directory cannot contain links or junctions.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
