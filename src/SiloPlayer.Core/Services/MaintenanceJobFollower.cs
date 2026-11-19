using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;

namespace SiloPlayer.Core.Services;

public enum MaintenanceJobWaitOutcome
{
    Succeeded,
    Failed,
    Canceled,
    /// <summary>The bounded wait ran out, or the server asked to be read again only after it.</summary>
    TimedOut,
    /// <summary>The account, profile or server that started the wait changed. Nothing failed.</summary>
    Stale,
}

/// <param name="Job">The last job read, when there was one.</param>
/// <param name="Error">The read failure that ended the wait, for <see cref="MaintenanceJobWaitOutcome.Failed"/>.</param>
public sealed record MaintenanceJobWaitResult(MaintenanceJobWaitOutcome Outcome, MaintenanceJobStatus? Job = null, Exception? Error = null);

/// <summary>
/// Follows one admin job until it finishes, with a bounded wait (official WebUI
/// #4355, <c>pollAdminJobUntilTerminal</c>). A read that may succeed later (a
/// network error, 408, 429, 5xx, or a request retired by a transport catch-up)
/// does not end the wait: it backs off exponentially up to
/// <see cref="MaxFailureBackoff"/>, and never retries before the server's
/// <c>Retry-After</c>. A running job is read again after
/// <see cref="PollInterval"/> or the server's <c>Retry-After</c> poll hint,
/// whichever is longer. A delay that would pass the deadline ends the wait as
/// <see cref="MaintenanceJobWaitOutcome.TimedOut"/> without reading early.
/// The identity check runs before and after every read, failed or not, so a
/// read that settles after a sign-out or account switch ends the wait as
/// <see cref="MaintenanceJobWaitOutcome.Stale"/>.
/// </summary>
public sealed class MaintenanceJobFollower
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultMaxFailureBackoff = TimeSpan.FromSeconds(30);

    private readonly Func<CancellationToken, Task<MaintenanceJobRead>> _read;
    private readonly Func<bool> _isIdentityCurrent;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;

    public MaintenanceJobFollower(
        Func<CancellationToken, Task<MaintenanceJobRead>> read,
        Func<bool> isIdentityCurrent,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _isIdentityCurrent = isIdentityCurrent ?? throw new ArgumentNullException(nameof(isIdentityCurrent));
        _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;
    public TimeSpan MaxWait { get; init; } = DefaultMaxWait;
    public TimeSpan MaxFailureBackoff { get; init; } = DefaultMaxFailureBackoff;

    /// <summary>Waits for the job. Throws <see cref="OperationCanceledException"/> only for <paramref name="cancellationToken"/>.</summary>
    public async Task<MaintenanceJobWaitResult> WaitAsync(CancellationToken cancellationToken = default)
    {
        var deadline = _now() + MaxWait;
        var failures = 0;
        MaintenanceJobStatus? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isIdentityCurrent()) return new(MaintenanceJobWaitOutcome.Stale, last);

            MaintenanceJobRead? read = null;
            Exception? error = null;
            try { read = await _read(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { error = ex; }

            // Either way, a read that settled after the session changed is the old session's.
            if (!_isIdentityCurrent()) return new(MaintenanceJobWaitOutcome.Stale, read?.Job ?? last);

            double waitSeconds;
            if (error != null || read == null)
            {
                error ??= new InvalidOperationException("The job read returned nothing.");
                if (!IsTransient(error)) return new(MaintenanceJobWaitOutcome.Failed, last, error);
                failures++;
                var backoff = Math.Min(Math.Pow(2, failures), MaxFailureBackoff.TotalSeconds);
                waitSeconds = Math.Max(backoff, RetryAfterSeconds(error) ?? 0);
            }
            else
            {
                failures = 0;
                last = read.Job;
                if (last.Succeeded) return new(MaintenanceJobWaitOutcome.Succeeded, last);
                if (last.Canceled) return new(MaintenanceJobWaitOutcome.Canceled, last);
                if (last.Failed) return new(MaintenanceJobWaitOutcome.Failed, last);
                waitSeconds = Math.Max(PollInterval.TotalSeconds, read.RetryAfterSeconds ?? 0);
            }

            // Compare in seconds: a huge Retry-After must not overflow a TimeSpan,
            // and must not be shortened into an early read either.
            var remaining = (deadline - _now()).TotalSeconds;
            if (!double.IsFinite(waitSeconds) || waitSeconds > remaining)
                return new(MaintenanceJobWaitOutcome.TimedOut, last, error);
            await _delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Whether a failed job read may succeed if sent again later (official <c>isTransientV2Error</c>).</summary>
    public static bool IsTransient(Exception error) => error switch
    {
        ApiException api => api.StatusCode is 0 or 408 or 429 || api.StatusCode >= 500,
        HttpRequestException or TimeoutException or IOException => true,
        // An HttpClient timeout, or a request retired by a transport catch-up while
        // the identity stayed current. The caller's own cancellation is rethrown first.
        OperationCanceledException => true,
        _ => false,
    };

    private static double? RetryAfterSeconds(Exception error)
        => error is ApiException { RetryAfterSeconds: { } seconds } && !double.IsNaN(seconds) ? Math.Max(0, seconds) : null;
}
