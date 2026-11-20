using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The link's 15-minute window passed (or the server no longer honors its signature): ask for fresh links.</summary>
public sealed class AdminBackupLinkExpiredException() : Exception("The download link expired. Get new links and try again.");

/// <summary>The downloaded bytes were not the object the backup's manifest records; nothing was saved.</summary>
public sealed class AdminBackupDownloadMismatchException() : Exception("The downloaded file does not match the backup's checksum. Nothing was saved.");

/// <summary>What one finished download verified.</summary>
public sealed record AdminBackupDownloadResult(long Bytes, bool HashVerified);

/// <summary>
/// Saves one signed backup object. The request goes through its own transport with no
/// account, profile or device headers, no cookies and no automatic redirects. Only the
/// exact server-relative signed path for this backup and object, or an absolute https
/// presigned URL, is accepted. Bytes stream through a bounded buffer (never the whole
/// object in memory) into an operation-owned partial file that replaces the destination
/// atomically only after the exact size, and SHA-256 where the manifest records one,
/// verify. On any failure the existing destination file is kept and the partial removed.
/// Link URLs are never logged or placed in exception messages.
/// </summary>
public sealed class AdminBackupSignedDownload : IDisposable
{
    private const int BufferSize = 128 * 1024;
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan ExpirySlack = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _now;

    public AdminBackupSignedDownload(HttpMessageHandler? handler = null, Func<DateTimeOffset>? now = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15), PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Clear();
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The absolute URL a link may be fetched from, or null when it is not one this backup's
    /// object can come from: a relative link must be exactly
    /// <c>/api/v2/admin/backups/{id}/objects/{name}?exp=…&amp;sig=…</c> on the signed-in server;
    /// an absolute one must be https (or http only when the server itself is http) without user
    /// information or a fragment.
    /// </summary>
    public static Uri? ResolveLink(string serverBaseUrl, string backupId, AdminBackupDownloadLink link)
    {
        if (!AdminBackupIdentifiers.IsBackupId(backupId) || link.Name == null || !AdminBackupIdentifiers.ObjectNames.Contains(link.Name) || string.IsNullOrEmpty(link.Url)) return null;
        if (!Uri.TryCreate(serverBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var server) || server.Scheme is not ("https" or "http")) return null;
        var url = link.Url;
        if (url.Any(char.IsControl) || url.Contains('\\') || url.Contains('#')) return null;
        if (url.StartsWith('/'))
        {
            if (url.StartsWith("//", StringComparison.Ordinal)) return null;
            var expectedPath = "/api/v2/admin/backups/" + Uri.EscapeDataString(backupId) + "/objects/" + Uri.EscapeDataString(link.Name);
            var query = url.IndexOf('?');
            if (query < 0 || url[..query] != expectedPath) return null;
            var parameters = url[(query + 1)..].Split('&');
            if (parameters.Length != 2 || !parameters.Any(part => part.StartsWith("exp=", StringComparison.Ordinal) && part.Length > 4 && part[4..].All(char.IsAsciiDigit)) ||
                !parameters.Any(part => part.StartsWith("sig=", StringComparison.Ordinal) && part.Length > 4 && part[4..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                return null;
            // Resolve against the server's origin plus any path prefix it is served under.
            var prefix = server.AbsolutePath.TrimEnd('/');
            return Uri.TryCreate(server.GetLeftPart(UriPartial.Authority) + prefix + url, UriKind.Absolute, out var resolved) ? resolved : null;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute) || !string.IsNullOrEmpty(absolute.UserInfo) || !string.IsNullOrEmpty(absolute.Fragment)) return null;
        if (absolute.Scheme == "https") return absolute;
        return absolute.Scheme == "http" && server.Scheme == "http" ? absolute : null;
    }

    /// <summary>
    /// What a link must deliver: the exact size and SHA-256 the backup's manifest records for a
    /// stored object, the exact size for <c>manifest.json</c>, or at most the stored size for an
    /// encrypted backup's restore set, which the server decrypts on the way and verified itself.
    /// Null when the link names nothing this backup holds.
    /// </summary>
    public static (long MaxBytes, bool Exact, string? Sha256)? Expectation(AdminBackup backup, AdminBackupDownloadLink link, string scope)
    {
        var size = link.SizeBytes ?? -1;
        if (size < 0 || link.Name == null) return null;
        if (scope == "restore-set")
        {
            if (link.Name != "restore-set.tar") return null;
            if (backup.Encrypted == true)
                return backup.Objects!.Any(entry => entry.Name is "restore-set.tar.age" or "restore-set.tar" && entry.SizeBytes == size) ? (size, false, null) : null;
        }
        if (scope == "full" && link.Name == "manifest.json") return (size, true, null);
        var stored = backup.Objects!.FirstOrDefault(entry => entry.Name == link.Name);
        return stored is { SizeBytes: { } storedSize, Sha256: { } hash } && storedSize == size ? (size, true, hash.ToLowerInvariant()) : null;
    }

    public async Task<AdminBackupDownloadResult> SaveAsync(MonitorAuthorityLease action, AdminBackup backup, AdminBackupDownloadLink link, string scope,
        string destinationPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        action.ThrowIfRetired();
        if (link.ExpiresAt is not { } expires || expires - ExpirySlack <= _now()) throw new AdminBackupLinkExpiredException();
        var target = ResolveLink(action.Context.BaseUrl, backup.Id!, link) ?? throw new InvalidDataException("The download link is not one this backup's file can come from.");
        var expected = Expectation(backup, link, scope) ?? throw new InvalidDataException("The download link names a file this backup does not hold.");
        var hashed = false;
        long written = 0;
        var saved = await DiagnosticsFileTransfer.SaveAsync(action, destinationPath, async (destination, token) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(token);
            // A body-inclusive cap: ten minutes plus the object at 128 KiB/s, with a stall timeout below.
            overall.CancelAfter(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(expected.MaxBytes / (128d * 1024)));
            HttpResponseMessage response;
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(overall.Token))
            {
                connect.CancelAfter(StallTimeout);
                try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connect.Token).ConfigureAwait(false); }
                catch (HttpRequestException) { throw new IOException("The download did not start."); }
            }
            using (response)
            {
                action.ThrowIfRetired();
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone) throw new AdminBackupLinkExpiredException();
                if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("The download link redirected; redirects are not followed.");
                if (response.StatusCode != HttpStatusCode.OK) throw new IOException("The download failed.");
                var length = response.Content.Headers.ContentLength;
                if (length is { } announced && (expected.Exact ? announced != expected.MaxBytes : announced > expected.MaxBytes))
                    throw new AdminBackupDownloadMismatchException();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var source = await response.Content.ReadAsStreamAsync(overall.Token).ConfigureAwait(false);
                var buffer = new byte[BufferSize];
                while (true)
                {
                    int read;
                    using (var stall = CancellationTokenSource.CreateLinkedTokenSource(overall.Token))
                    {
                        stall.CancelAfter(StallTimeout);
                        try { read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false); }
                        catch (HttpRequestException) { throw new IOException("The download was cut short."); }
                    }
                    action.ThrowIfRetired();
                    if (read == 0) break;
                    written += read;
                    if (written > expected.MaxBytes) throw new AdminBackupDownloadMismatchException();
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    progress?.Report(written);
                }
                if (expected.Exact && written != expected.MaxBytes) throw new AdminBackupDownloadMismatchException();
                if (length is { } total && written != total) throw new AdminBackupDownloadMismatchException();
                if (expected.Sha256 != null)
                {
                    if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), expected.Sha256, StringComparison.Ordinal))
                        throw new AdminBackupDownloadMismatchException();
                    hashed = true;
                }
                return true;
            }
        }, ct).ConfigureAwait(false);
        if (!saved) throw new IOException("The download did not finish.");
        return new(written, hashed);
    }

    public void Dispose() => _http.Dispose();
}
