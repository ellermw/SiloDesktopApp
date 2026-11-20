using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminPluginsApi
{
    /// <summary>Streams one selected file. Session creation, chunks and completion are each sent once; completion is never replayed.</summary>
    public async Task<AdminPlugin> UploadAsync(MonitorAuthorityLease lease, Stream source, string filename, long size,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10)); ct = deadline.Token;
        if (size is <= 0 or > MaxUploadBytes || string.IsNullOrWhiteSpace(filename) || filename.Length > 512 || filename.Any(char.IsControl))
            throw new ArgumentException("Choose a plugin archive of at most 256 MiB.");
        const int defaultChunk = 512 * 1024;
        if (size <= defaultChunk)
        {
            var buffer = new byte[(int)size]; await source.ReadExactlyAsync(buffer, ct).ConfigureAwait(false); lease.ThrowIfRetired();
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(buffer); part.Headers.ContentType = new("application/octet-stream"); form.Add(part, "archive", filename);
            try
            {
                var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Post, Root + "uploads", expected: HttpStatusCode.Created, content: form, ct: ct).ConfigureAwait(false)).Body;
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
                    new { filename, size_bytes = size, chunk_size = chunkSize }, expected: HttpStatusCode.Created, ct: ct).ConfigureAwait(false)).Body;
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
                            Root + "uploads/chunked/" + Uri.EscapeDataString(uploadId) + "/chunks/" + index, content: chunk, ct: ct).ConfigureAwait(false)).Body;
                        sent += length; ValidateSession(received, size, chunkSize, index + 1, sent);
                        if (received.UploadId != uploadId) throw Invalid();
                        completedChunks++; progress?.Report((int)(sent * 100 / size));
                    }
                    // Once completion is sent, any failure is reconciled from installations; never reopen/replay this install.
                    var completing = uploadId; uploadId = null;
                    var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Post,
                        Root + "uploads/chunked/" + Uri.EscapeDataString(completing) + "/complete", expected: HttpStatusCode.Created, ct: ct).ConfigureAwait(false)).Body;
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
