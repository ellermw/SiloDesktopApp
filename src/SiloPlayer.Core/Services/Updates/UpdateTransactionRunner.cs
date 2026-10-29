using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public sealed record UpdateTransactionRequest(string InstallationRoot, string SourceVersion, string Tag, bool Restart);

public sealed record UpdateTransactionResult(bool Succeeded, bool Activated, bool Confirmed,
    string? TransactionId, string? Version, Exception? Error = null);

/// <summary>
/// Platform boundary for the trusted worker. Authentication and independent release
/// authority remain mandatory; the caller supplies synchronous adapters, never cache code.
/// </summary>
public interface IUpdateTransactionEnvironment
{
    void ValidateCallerAndParent();
    IDisposable AcquireInstallationLock(CancellationToken ct);
    PreparedUpdatePackage FetchAuthoritativePackage(string tag, CancellationToken ct);
    void AuthorizeNewClient(string executable);
    void Send(UpdateHostMessage message, CancellationToken ct);
    UpdateHostMessage Receive(CancellationToken ct);
    void WaitForParentExit(CancellationToken ct);
    bool HasParentExited { get; }
    void VerifyNoOtherClient();
    bool MatchesReadyProcess(UpdateHostMessage message, string executable);
    // Uncertainty retains an old version. The Windows adapter supplies an
    // exact process inventory; callers lacking that capability never delete it.
    bool IsVersionInUse(string directory) => true;
    void RetireUnusedAuthorization(string versionDirectory) { }
}

/// <summary>
/// Runs on one worker entry thread because the installation lock is thread-affine.
/// Installation operations are real; process, transport, elevation and firewall
/// effects are supplied by the authenticated Windows host.
/// </summary>
public static class UpdateTransactionRunner
{
    public static UpdateTransactionResult Run(UpdateTransactionRequest request,
        IUpdateTransactionEnvironment environment, CancellationToken ct) => Run(request, environment, ct, TimeProvider.System);

    public static UpdateTransactionResult Run(UpdateTransactionRequest request,
        IUpdateTransactionEnvironment environment, CancellationToken ct, TimeProvider timeProvider)
    {
        IDisposable? installationLock = null;
        StagedUpdate? staged = null;
        var activated = false;
        var confirmed = false;
        DateTimeOffset? cooldownUntil = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            environment.ValidateCallerAndParent();
            installationLock = environment.AcquireInstallationLock(ct);
            var state = SideBySideInstallation.ReadState(request.InstallationRoot)
                ?? throw new InvalidOperationException("A full installer migration is required.");
            _ = SideBySideInstallation.VerifyInstalledClientFile(request.InstallationRoot,
                request.SourceVersion, "Updater/SiloPlayer.UpdateHost.exe");
            if (state.ActiveVersion != request.SourceVersion)
            {
                // A real, authenticated previous client can be the launcher's
                // recovery from an unready pending version. A user-writable
                // receipt alone cannot grant this privileged rollback.
                if (state.TransactionId is { } failed && state.PendingVersion is not null &&
                    state.PreviousVersion == request.SourceVersion)
                {
                    if (environment.IsVersionInUse(Path.Combine(request.InstallationRoot, "versions", state.ActiveVersion)))
                        throw new IOException("The pending Silo version is still in use or could not be inspected.");
                    SideBySideInstallation.Rollback(request.InstallationRoot, failed);
                    TryCleanup(request.InstallationRoot, environment);
                    state = SideBySideInstallation.ReadState(request.InstallationRoot)!;
                }
                else throw new InvalidDataException("Silo is running a displaced version.");
            }
            // The authenticated, already-interactive current client can reconcile
            // the prior automatic update without requiring a new readiness launch.
            if (state.TransactionId is { } pending)
            {
                SideBySideInstallation.Confirm(request.InstallationRoot, pending);
                TryCleanup(request.InstallationRoot, environment, retireAuthorization: true);
            }
            SideBySideInstallation.RecoverInterruptedTransaction(request.InstallationRoot);

            // A real broker acknowledgement is required before files are staged.
            // Setting a local Boolean does not prove the handoff window exists.
            var visible = environment.Receive(ct);
            if (visible.Kind != "visible" || visible.TransactionId is not null)
                throw new InvalidDataException("The update progress window is not ready.");
            environment.Send(new("progress", Text: "Verifying release authority…"), ct);
            cooldownUntil = UpdateAuthorityCooldown.Read(request.InstallationRoot, timeProvider);
            if (cooldownUntil is not null)
                throw new InvalidOperationException(UpdateCheckRateLimitedException.UserMessage);
            var package = environment.FetchAuthoritativePackage(request.Tag, ct);
            staged = SideBySideInstallation.Stage(request.InstallationRoot, package,
                progress => environment.Send(new("progress", Text: progress.Stage,
                    Completed: progress.CompletedBytes, Total: progress.TotalBytes), ct), ct);
            environment.AuthorizeNewClient(Path.Combine(staged.VersionDirectory, "SiloPlayer.exe"));
            environment.Send(new("staged", staged.TransactionId, staged.Version), ct);

            ct.ThrowIfCancellationRequested();
            var close = environment.Receive(ct);
            if (close.Kind != "close" || close.TransactionId != staged.TransactionId)
                throw new OperationCanceledException("The update was deferred before restart.");
            environment.WaitForParentExit(ct);
            ct.ThrowIfCancellationRequested();
            if (!environment.HasParentExited)
                throw new InvalidOperationException("The original Silo process is still running.");
            environment.VerifyNoOtherClient();
            SideBySideInstallation.Activate(staged);
            activated = true;
            installationLock.Dispose();
            installationLock = null;
            // New client launch is solely the unelevated broker's responsibility.
            // Its startup must observe an unlocked canonical installation gate.
            environment.Send(new("activated", staged.TransactionId, staged.Version), ct);
            if (!request.Restart)
                return new(true, true, false, staged.TransactionId, staged.Version);

            var ready = environment.Receive(ct);
            var image = Path.Combine(staged.VersionDirectory, "SiloPlayer.exe");
            if (ready.Kind != "ready" || ready.TransactionId != staged.TransactionId ||
                !environment.MatchesReadyProcess(ready, image))
                throw new InvalidDataException("The new client has not confirmed local startup.");
            installationLock = environment.AcquireInstallationLock(ct);
            _ = SideBySideInstallation.VerifyInstalledClientFile(request.InstallationRoot, staged.Version, "SiloPlayer.exe");
            SideBySideInstallation.Confirm(request.InstallationRoot, staged.TransactionId);
            confirmed = true;
            TryCleanup(request.InstallationRoot, environment, retireAuthorization: true);
            environment.Send(new("confirmed", staged.TransactionId, staged.Version), ct);
            return new(true, true, true, staged.TransactionId, staged.Version);
        }
        catch (Exception ex)
        {
            if (ex is UpdateCheckRateLimitedException limited && installationLock is not null)
            {
                try { cooldownUntil = UpdateAuthorityCooldown.Record(request.InstallationRoot, limited.RetryAfter, timeProvider); }
                catch { cooldownUntil = UpdateAuthorityCooldown.BoundedDeadline(limited.RetryAfter, timeProvider); }
            }
            if (installationLock is not null && !activated)
            {
                try { SideBySideInstallation.RecoverInterruptedTransaction(request.InstallationRoot); }
                catch { /* The journal remains available for a privileged retry. */ }
            }
            // After activation, leave pending recovery intact. A slow or still-live
            // client is never killed or rolled back based on a transport timeout.
            try
            {
                environment.Send(new("failed", staged?.TransactionId,
                    cooldownUntil is not null
                        ? UpdateCheckRateLimitedException.UserMessage
                        : activated
                        ? "The new Silo has not confirmed startup with this window. Your previous version is preserved. Open Silo again to recover if startup exited."
                        : ex is OperationCanceledException
                        ? "The update was canceled. Silo can continue using its current version."
                        : "The update could not finish. Your previous version is preserved. Retry from Silo or use the full installer.",
                    CooldownUntil: cooldownUntil),
                    CancellationToken.None);
            }
            catch { }
            return new(false, activated, confirmed, staged?.TransactionId, staged?.Version, ex);
        }
        finally { installationLock?.Dispose(); }
    }

    private static void TryCleanup(string root, IUpdateTransactionEnvironment environment, bool retireAuthorization = false)
    {
        try
        {
            var versions = Path.Combine(SideBySideInstallation.CanonicalRoot(root), "versions");
            SideBySideInstallation.CleanupOwnedVersions(root, directory =>
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
                var version = Path.GetFileName(full);
                if (!string.Equals(Path.GetDirectoryName(full), versions, StringComparison.OrdinalIgnoreCase) ||
                    !Version.TryParse(version, out var parsed) || parsed.Revision < 0 || parsed.ToString(4) != version)
                    return true;
                if (environment.IsVersionInUse(full)) return true;
                if (retireAuthorization)
                {
                    try
                    {
                        // Do not retire rules for a foreign normalized directory
                        // merely because its name resembles an old version.
                        _ = SideBySideInstallation.VerifyInstalledClientFile(root, version, "SiloPlayer.exe");
                        environment.RetireUnusedAuthorization(full);
                    }
                    catch { return true; }
                }
                return false;
            });
        }
        catch { /* Deferred cleanup cannot undo a verified activation/readiness. */ }
    }
}
