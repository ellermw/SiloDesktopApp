namespace SiloPlayer.Core.Services.Admin;

public static class DiagnosticsFileTransfer
{
    public static async Task<bool> SaveAsync(MonitorAuthorityLease action, string destinationPath,
        Func<Stream, CancellationToken, Task<bool>> download, CancellationToken cancellationToken = default)
    {
        action.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(action.Token, cancellationToken);
        var target = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(target) ?? throw new ArgumentException("A destination directory is required.");
        var stem = Path.Combine(directory, ".silo-diagnostics-" + Guid.NewGuid().ToString("N"));
        var partial = stem + ".partial";
        var backup = stem + ".backup";
        var published = false;
        var replaced = false;
        var completed = false;
        try
        {
            await using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (!await download(destination, linked.Token).ConfigureAwait(false)) return false;
                action.ThrowIfRetired(); linked.Token.ThrowIfCancellationRequested();
                await destination.FlushAsync(linked.Token).ConfigureAwait(false);
                action.ThrowIfRetired();
            }
            action.ThrowIfRetired(); linked.Token.ThrowIfCancellationRequested();
            if (File.Exists(target)) { File.Replace(partial, target, backup); replaced = true; }
            else File.Move(partial, target);
            published = true;
            action.ThrowIfRetired(); linked.Token.ThrowIfCancellationRequested();
            completed = true;
            return true;
        }
        catch
        {
            if (published)
            {
                if (replaced) File.Replace(backup, target, null);
                else File.Delete(target);
            }
            throw;
        }
        finally
        {
            File.Delete(partial);
            if (completed && File.Exists(backup)) File.Delete(backup);
        }
    }
}
