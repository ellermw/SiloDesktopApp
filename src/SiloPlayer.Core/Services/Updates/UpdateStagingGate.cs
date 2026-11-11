namespace SiloPlayer.Core.Services.Updates;

/// <summary>Restart consent is offered only after the authenticated worker has finished staging.</summary>
public static class UpdateStagingGate
{
    public static async Task<(UpdateHostMessage Staged, bool Restart)> PrepareAndChooseAsync(
        Func<CancellationToken, Task<UpdateHostMessage>> receiveWorker,
        Func<UpdateHostMessage, CancellationToken, Task> reportProgress,
        Func<UpdateHostMessage, CancellationToken, Task<bool>> chooseRestart,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var frame = await receiveWorker(ct);
            if (frame.Kind == "progress") { await reportProgress(frame, ct); continue; }
            if (frame.Kind == "failed")
            {
                await reportProgress(frame, ct); // Preserve worker cooldown/error metadata for the client.
                throw new IOException(frame.Text ?? "Update preparation failed.");
            }
            if (frame.Kind != "staged" || !Guid.TryParseExact(frame.TransactionId, "N", out _) || !Version.TryParse(frame.Text, out _))
                throw new InvalidDataException("The worker did not confirm a staged installation.");
            ct.ThrowIfCancellationRequested();
            return (frame, await chooseRestart(frame, ct));
        }
    }
}
