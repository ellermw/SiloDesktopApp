using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public enum AdminPluginUploadCheck { Accepted, Empty, TooLarge, InvalidName }

public sealed partial class AdminPluginsApi
{
    /// <summary>
    /// What is checked before a chosen file is opened. The server takes a zip archive or a plugin binary, so there is no
    /// extension rule (the official page has none); size is the server's own 256 MiB limit and the name must be sendable.
    /// </summary>
    public static AdminPluginUploadCheck CheckUpload(string? filename, long size) =>
        size <= 0 ? AdminPluginUploadCheck.Empty
        : size > MaxUploadBytes ? AdminPluginUploadCheck.TooLarge
        : string.IsNullOrWhiteSpace(filename) || filename.Length > 512 || SiloPlayer.Core.Services.Admin.AdminPluginPresentation.HasHiddenControls(filename) ? AdminPluginUploadCheck.InvalidName
        : AdminPluginUploadCheck.Accepted;

    /// <summary>The limit of each upload request (session, one chunk, completion); null is the transport's own bound for a write. Tests set it short.</summary>
    internal TimeSpan? UploadRequestLimit { get; init; }

    /// <summary>Streams one selected file. Session creation, chunks and completion are each sent once; completion is never replayed.</summary>
    public async Task<AdminPlugin> UploadAsync(MonitorAuthorityLease lease, Stream source, string filename, long size,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        // No overall deadline: how long a large file takes depends on the link, and the official upload has none either. The upload
        // ends when it is done, when the caller cancels it, or when the authority is retired (the page left, the server or account
        // changed). Every request below carries its own limit (AdminPluginSendAsync), and a chunk is at most 512 KiB, so a stalled
        // request ends by itself while a slow but steady link finishes.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct); ct = cancellation.Token;
        if (CheckUpload(filename, size) != AdminPluginUploadCheck.Accepted) throw new ArgumentException("Choose a plugin file of at most 256 MiB.");
        const int defaultChunk = 512 * 1024;
        if (size <= defaultChunk)
        {
            var buffer = new byte[(int)size]; await source.ReadExactlyAsync(buffer, ct).ConfigureAwait(false); lease.ThrowIfRetired();
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(buffer); part.Headers.ContentType = new("application/octet-stream"); form.Add(part, "archive", filename);
            try
            {
                var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Post, Root + "uploads", expected: HttpStatusCode.Created, content: form, ct: ct, requestLimit: UploadRequestLimit).ConfigureAwait(false)).Body;
                ValidatePlugin(row, true); progress?.Report(100); return row;
            }
            finally { Array.Clear(buffer); }
        }
        var chunkSize = defaultChunk;
        var initialPosition = source.CanSeek ? source.Position : 0;
        while (true)
        {
            string? uploadId = null;
            var completedChunks = 0;
            try
            {
                var session = (await client.AdminPluginSendAsync<AdminPluginUploadSession>(lease, HttpMethod.Post, Root + "uploads/chunked",
                    new { filename, size_bytes = size, chunk_size = chunkSize }, expected: HttpStatusCode.Created, ct: ct, requestLimit: UploadRequestLimit).ConfigureAwait(false)).Body;
                uploadId = session.UploadId;
                ValidateSession(session, size, chunkSize, 0, 0);
                var buffer = new byte[chunkSize];
                try
                {
                    long sent = 0;
                    for (var index = 0; index < session.TotalChunks; index++)
                    {
                        lease.ThrowIfRetired();
                        var length = (int)Math.Min(chunkSize, size - sent);
                        await source.ReadExactlyAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
                        lease.ThrowIfRetired();
                        using var chunk = new ByteArrayContent(buffer, 0, length); chunk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        var received = (await client.AdminPluginSendAsync<AdminPluginUploadSession>(lease, HttpMethod.Put,
                            Root + "uploads/chunked/" + Uri.EscapeDataString(uploadId) + "/chunks/" + index, content: chunk, ct: ct, requestLimit: UploadRequestLimit).ConfigureAwait(false)).Body;
                        sent += length; ValidateSession(received, size, chunkSize, index + 1, sent);
                        if (received.UploadId != uploadId) throw Invalid();
                        completedChunks++; progress?.Report((int)(sent * 100 / size));
                    }
                    // Once completion is sent, any failure is reconciled from installations; never reopen/replay this install.
                    var completing = uploadId; uploadId = null;
                    var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Post,
                        Root + "uploads/chunked/" + Uri.EscapeDataString(completing) + "/complete", expected: HttpStatusCode.Created, ct: ct, requestLimit: UploadRequestLimit).ConfigureAwait(false)).Body;
                    ValidatePlugin(row, true); return row;
                }
                finally { Array.Clear(buffer); }
            }
            catch (ApiException ex) when (ex.StatusCode == 413 && uploadId != null && completedChunks == 0 && chunkSize > 128 * 1024 && source.CanSeek)
            {
                source.Position = initialPosition; chunkSize /= 2;
            }
            finally
            {
                if (uploadId != null && lease.IsCurrent)
                {
                    try
                    {
                        // Cancellation cleanup has its own short bound, still under the captured authority.
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Delete, Root + "uploads/chunked/" + Uri.EscapeDataString(uploadId),
                            expected: HttpStatusCode.NoContent, ct: cleanup.Token).ConfigureAwait(false);
                    }
                    catch { /* Session expiry is the server's fallback; do not mask the original result. */ }
                }
            }
        }
    }
    private static void ValidateSession(AdminPluginUploadSession s, long size, int chunk, int received, long bytes)
    {
        if (string.IsNullOrWhiteSpace(s.UploadId) || s.UploadId.Length > 128 || s.SizeBytes != size || s.ChunkSize != chunk ||
            s.TotalChunks != (size + chunk - 1) / chunk || s.ReceivedChunks != received || s.ReceivedBytes != bytes) throw Invalid();
    }
}
