using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminBackupUploadStep { Choose, Uploading, Checking, Opened, Failed }

/// <summary>Why an upload stopped (official UploadFailure kinds).</summary>
public sealed record AdminBackupUploadFailure(string Kind, string? Detail = null, string? Reason = null, int Part = 0, long PartCount = 0, long SizeBytes = 0, long LimitBytes = 0);

/// <summary>What the dialog shows for a failure (official FailureCopy).</summary>
public sealed record AdminBackupUploadFailureCopy(string Title, string Body, string? Command, bool Resumable, string? Detail);

public sealed record AdminBackupUploadPhase
{
    public AdminBackupUploadStep Step { get; init; }
    public AdminBackupUpload? Upload { get; init; }
    public AdminBackupUploadFailure? Failure { get; init; }
    /// <summary>The part to continue from; the parts before it arrived.</summary>
    public int From { get; init; }
    public string? FileName { get; init; }
    public long FileSize { get; init; }
    /// <summary>The encrypted-file notice key while an age file uploads.</summary>
    public string? Notice { get; init; }
    public override string ToString() => nameof(AdminBackupUploadPhase);
}

/// <summary>
/// One "Upload a backup" dialog session (official UploadBackupDialog.tsx and uploadModel.ts):
/// sniff the file, open an upload once, send parts in order from a caller-owned seekable
/// stream through one bounded part buffer (at most 16 MiB), each part up to three times
/// only for failures that a resend can fix (the part PUT is naturally idempotent), then
/// complete once. "Try again" continues from the failed part; "Check again" re-sends only
/// the idempotent completion. Creating an upload is never resent. Closing stops sending;
/// an unfinished upload is deleted under the same lease, a checked one is kept. The
/// session holds one action lease; authority retirement ends it silently.
/// </summary>
public sealed class AdminBackupUploadSession : IDisposable
{
    internal const int PartAttempts = 3;
    private const int SniffBytes = 512;
    private readonly AdminBackupsApi _api;
    private readonly MonitorAuthorityLease _lease;
    private readonly long _maxBytes;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly object _gate = new();
    private CancellationTokenSource _run = new();
    private Stream? _file;
    private AdminBackupUploadPhase _phase = new();
    private bool _closed;

    public AdminBackupUploadSession(AdminBackupsApi api, MonitorAuthorityLease lease, long maxBytes, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        _api = api; _lease = lease; _maxBytes = Math.Min(maxBytes, AdminBackupIdentifiers.MaxUploadBytes);
        _wait = wait ?? ((delay, token) => Task.Delay(delay, token));
    }

    public event Action? Changed;
    public AdminBackupUploadPhase Phase { get { lock (_gate) return _phase; } }

    private void Set(AdminBackupUploadPhase phase, CancellationToken token)
    {
        lock (_gate) { if (_closed || token.IsCancellationRequested || !_lease.IsCurrent) return; _phase = phase; }
        Changed?.Invoke();
    }

    private void Fail(AdminBackupUploadFailure failure, AdminBackupUpload? upload, int from, CancellationToken token) =>
        Set(Phase with { Step = AdminBackupUploadStep.Failed, Failure = failure, Upload = upload, From = from }, token);

    /// <summary>Starts with a seekable stream the session now owns; it is disposed when the session ends or another file is chosen.</summary>
    public async Task StartAsync(string fileName, Stream file)
    {
        if (!file.CanRead || !file.CanSeek) throw new ArgumentException("A seekable file is required.", nameof(file));
        CancellationToken token;
        lock (_gate)
        {
            if (_closed) { file.Dispose(); return; }
            _run.Cancel(); _run.Dispose(); _run = CancellationTokenSource.CreateLinkedTokenSource(_lease.Token);
            token = _run.Token;
            _file?.Dispose(); _file = file;
            _phase = new() { Step = AdminBackupUploadStep.Uploading, FileName = Path.GetFileName(fileName), FileSize = file.Length };
        }
        Changed?.Invoke();
        var size = file.Length;
        if (size > _maxBytes || size < 1)
        {
            Fail(new("too_large", SizeBytes: size, LimitBytes: _maxBytes), null, 0, token);
            return;
        }
        string sniffed;
        try
        {
            var head = new byte[SniffBytes];
            file.Seek(0, SeekOrigin.Begin);
            var read = await ReadExactlyUpTo(file, head, token).ConfigureAwait(false);
            sniffed = Present.SniffRestoreSet(head.AsSpan(0, read));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Fail(new("error", AdminText.T("configure.native.backups.fileUnreadable")), null, 0, token);
            return;
        }
        if (token.IsCancellationRequested) return;
        if (sniffed == "not_a_restore_set") { Fail(new("rejected", "", "not_a_restore_set"), null, 0, token); return; }
        Set(Phase with { Notice = sniffed == "encrypted" ? "web.admin.backups.upload.encryptedNotice" : null }, token);
        AdminBackupUpload upload;
        try { upload = await _api.CreateUploadAsync(_lease, size, null, null, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) { Fail(FailureOf(error, "create", size), null, 0, token); return; }
        if (token.IsCancellationRequested)
        {
            // Closed while the upload was being opened: remove it rather than leave it for the sweep.
            await DeleteQuietly(upload.Id!).ConfigureAwait(false);
            return;
        }
        await RunAsync(upload, 0, token).ConfigureAwait(false);
    }

    /// <summary>"Try again" / "Check again": continues the same upload from where it stopped.</summary>
    public Task ResumeAsync()
    {
        CancellationToken token;
        AdminBackupUploadPhase phase;
        lock (_gate)
        {
            phase = _phase;
            if (_closed || phase.Step != AdminBackupUploadStep.Failed || phase.Upload == null || phase.Failure == null || !CopyOf(phase.Failure).Resumable) return Task.CompletedTask;
            _run.Cancel(); _run.Dispose(); _run = CancellationTokenSource.CreateLinkedTokenSource(_lease.Token);
            token = _run.Token;
        }
        return RunAsync(phase.Upload, phase.From, token);
    }

    private async Task RunAsync(AdminBackupUpload upload, int from, CancellationToken token)
    {
        var file = _file;
        if (file == null) return;
        Set(Phase with { Step = AdminBackupUploadStep.Uploading, Upload = upload, Failure = null, From = from }, token);
        var current = upload;
        var buffer = new byte[(int)Math.Min(upload.PartSize!.Value, upload.SizeBytes!.Value)];
        for (var part = from; part < upload.PartCount; part++)
        {
            var length = AdminBackupUploadMath.PartLength(upload, part);
            try
            {
                file.Seek(AdminBackupUploadMath.PartOffset(upload, part), SeekOrigin.Begin);
                if (await ReadExactlyUpTo(file, buffer.AsMemory(0, length), token).ConfigureAwait(false) != length) throw new IOException("The file changed while uploading.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Fail(new("error", AdminText.T("configure.native.backups.fileUnreadable")), current, part, token);
                return;
            }
            for (var attempt = 1; ; attempt++)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    current = await _api.PutPartAsync(_lease, upload, part, buffer.AsMemory(0, length), token).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    if (attempt >= PartAttempts || !Retryable(error))
                    {
                        Fail(Retryable(error) ? new("part_failed", Part: part, PartCount: upload.PartCount!.Value) : FailureOf(error, "part", upload.SizeBytes!.Value), current, part, token);
                        return;
                    }
                    try { await _wait(RetryDelay(attempt), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
            Set(Phase with { Upload = current }, token);
        }
        Set(Phase with { Step = AdminBackupUploadStep.Checking, Upload = current }, token);
        try
        {
            var checkedUpload = await _api.CompleteUploadAsync(_lease, upload.Id!, token).ConfigureAwait(false);
            if (checkedUpload.Status == "complete") Set(Phase with { Step = AdminBackupUploadStep.Opened, Upload = checkedUpload }, token);
            else if (checkedUpload.Rejection is { } rejection)
                Fail(new("rejected", AdminBackupValidation.SafeServerText(rejection.Message ?? ""), rejection.Reason), checkedUpload, 0, token);
            else Fail(new("error", AdminText.T("web.admin.backups.upload.couldntOpen")), checkedUpload, 0, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fail(FailureOf(error, "complete", upload.SizeBytes!.Value), current, (int)upload.PartCount!.Value, token); }
    }

    private static async Task<int> ReadExactlyUpTo(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    /// <summary>Whether sending again could help: no answer, a timeout, a server error, or a part still being received (409).</summary>
    public static bool Retryable(Exception error) => error switch
    {
        MonitorAuthorityRetiredException => false,
        AdminBackupOutcomeUnknownException => true,
        ApiException api => api.StatusCode is 408 or 409 or >= 500,
        _ => true,
    };

    public static TimeSpan RetryDelay(int attempt) => TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt - 1));

    /// <summary>Official failureOfError.</summary>
    public AdminBackupUploadFailure FailureOf(Exception error, string stage, long size)
    {
        if (stage == "complete" && Retryable(error) && !(error is ApiException { StatusCode: 409, ProblemType: "conflict" }))
            return new("check_failed", error is ApiException api ? Present.ErrorMessage(api) : AdminText.T("web.admin.backups.upload.noAnswerShort"));
        if (error is ApiException problem)
        {
            if (problem.StatusCode == 413 && stage == "create") return new("too_large", SizeBytes: size, LimitBytes: _maxBytes);
            if (problem.ProblemType == "upload_limit_reached") return new("too_many");
            if (stage == "complete" && problem.StatusCode == 409 && problem.ProblemType == "conflict") return new("busy");
            return new("error", Present.ErrorMessage(problem));
        }
        return new("error", AdminText.T("web.admin.backups.upload.noAnswer"));
    }

    /// <summary>Official failureCopy.</summary>
    public static AdminBackupUploadFailureCopy CopyOf(AdminBackupUploadFailure failure)
    {
        static string T(string key, object? values = null) => AdminText.T(key, values);
        switch (failure.Kind)
        {
            case "too_large":
                return new(T("web.admin.backups.upload.failure.tooLargeTitle"), T("web.admin.backups.upload.failure.tooLargeBody",
                    new { limit = Present.LimitLabel(failure.LimitBytes), size = Present.LimitLabel(failure.SizeBytes) }), null, false, null);
            case "too_many": return new(T("web.admin.backups.upload.failure.tooManyTitle"), T("web.admin.backups.upload.failure.tooManyBody"), null, false, null);
            case "busy": return new(T("web.admin.backups.upload.failure.busyTitle"), T("web.admin.backups.upload.failure.busyBody"), null, true, null);
            case "part_failed":
                return new(T("web.admin.backups.upload.failure.stoppedTitle"), T("web.admin.backups.upload.failure.partFailedBody",
                    new { part = failure.Part + 1, part_count = failure.PartCount }), null, true, null);
            case "check_failed":
                return new(T("web.admin.backups.upload.failure.checkStoppedTitle"), T("web.admin.backups.upload.failure.checkStoppedBody", new { detail = failure.Detail }), null, true, null);
            case "rejected":
                (string? Title, string? Body, string? Command) copy = failure.Reason switch
                {
                    "not_a_restore_set" => ("web.admin.backups.upload.rejected.notRestoreSetTitle", "web.admin.backups.upload.rejected.notRestoreSetBody", null),
                    "newer_silo" => ("web.admin.backups.upload.rejected.newerSiloTitle", "web.admin.backups.upload.rejected.newerSiloBody", null),
                    "encrypted" => ("web.admin.backups.upload.rejected.encryptedTitle", "web.admin.backups.upload.rejected.encryptedBody", Present.UploadDecryptCommand),
                    "checksum_mismatch" => ("web.admin.backups.upload.rejected.checksumTitle", "web.admin.backups.upload.rejected.checksumBody", null),
                    _ => (null, null, null),
                };
                var detail = string.IsNullOrEmpty(failure.Detail) ? null : failure.Detail;
                if (failure.Reason == "encrypted" && detail != null) return new(T(copy.Title!), detail, null, false, null);
                return new(copy.Title == null ? "" : T(copy.Title), copy.Body == null ? "" : T(copy.Body), copy.Command, false, detail);
            default: return new(T("web.admin.backups.upload.failure.stoppedTitle"), failure.Detail ?? "", null, false, null);
        }
    }

    /// <summary>
    /// Cancel / Choose another: stops sending and deletes what arrived, unless the upload is checked or being
    /// checked (the server finishes that check and the list shows it).
    /// </summary>
    public async Task DiscardAsync()
    {
        AdminBackupUploadPhase phase;
        lock (_gate)
        {
            if (_closed) return;
            phase = _phase;
            _run.Cancel();
            _phase = new();
        }
        Changed?.Invoke();
        var upload = phase.Step is AdminBackupUploadStep.Uploading or AdminBackupUploadStep.Checking or AdminBackupUploadStep.Failed ? phase.Upload : null;
        var finished = upload?.Status == "complete" || phase.Step == AdminBackupUploadStep.Checking;
        if (upload != null && !finished) await DeleteQuietly(upload.Id!).ConfigureAwait(false);
    }

    private async Task DeleteQuietly(string uploadId)
    {
        if (!_lease.IsCurrent) return;
        try { await _api.DeleteUploadAsync(_lease, uploadId).ConfigureAwait(false); }
        catch { /* The sweep deletes an unfinished upload 24 hours after its last part. */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true; _run.Cancel(); _run.Dispose(); _file?.Dispose(); _file = null;
        }
        _lease.Dispose(); Changed = null;
    }
}
