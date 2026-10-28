using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public sealed partial class AppUpdateService(HttpClient http, string cacheDirectory, Version currentVersion, Action<string>? log = null, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private AppUpdateState _state = new();
    private long _lastSuccessfulCheckStamp;
    private ReleaseCheck? _lastCheck;
    private DateTimeOffset? _rateLimitedUntil;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15), RetryInterval = TimeSpan.FromMinutes(5),
        MaximumRateLimitWait = TimeSpan.FromHours(6);
    public AppUpdateState State => Volatile.Read(ref _state);
    public string CacheDirectory => Path.GetFullPath(cacheDirectory);
    public event Action<AppUpdateState>? Changed;

    public async Task RunAutomaticChecksAsync(CancellationToken ct)
    {
        try
        {
            var clock = timeProvider ?? TimeProvider.System;
            await Task.Delay(TimeSpan.FromSeconds(5), clock, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                await CheckAsync(ct).ConfigureAwait(false);
                // A skipped check can overlap a manual request or download.
                // Wait for its result before choosing the next interval.
                await _operation.WaitAsync(ct).ConfigureAwait(false);
                TimeSpan delay;
                try { delay = NextCheckDelay(); }
                finally { _operation.Release(); }
                await Task.Delay(delay, clock, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    // A rate-limited network is not retried every five minutes: that spends the
    // shared hourly budget again. Wait for GitHub's reset, within sane bounds.
    private TimeSpan NextCheckDelay()
    {
        if (RemainingRateLimitDelay() is { } limited) return limited;
        if (State.Error is null) return CheckInterval;
        return RetryInterval;
    }

    private TimeSpan? RemainingRateLimitDelay()
    {
        if (_rateLimitedUntil is not { } until) return null;
        var remaining = until - (timeProvider ?? TimeProvider.System).GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!await _operation.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            // Enforce the deadline at dispatch as well as in the timer: a
            // manual check can learn a limit after that timer was scheduled.
            // Download progress/errors are separate from this network limit.
            if (RemainingRateLimitDelay() is not null)
            {
                SetState(State with { IsChecking = false, Error = "Could not check for updates. " + UpdateCheckRateLimitedException.UserMessage });
                log?.Invoke("check_deferred_rate_limit");
                return;
            }
            SetState(State with { IsChecking = true, Error = null });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var check = await new GitHubReleaseReader(http, timeProvider).ReadAsync(currentVersion, _lastCheck, deadline.Token).ConfigureAwait(false);
            _lastCheck = check; _rateLimitedUntil = null;
            var release = check.Release;
            Interlocked.Exchange(ref _lastSuccessfulCheckStamp, System.Diagnostics.Stopwatch.GetTimestamp());
            var same = release is not null && State.Release?.Version == release.Version && State.Release.Sha256 == release.Sha256;
            SetState(new AppUpdateState(release, ReadyInstaller: same ? State.ReadyInstaller : null));
            log?.Invoke($"check_ok version={release?.Version.ToString() ?? "current"}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetState(State with { IsChecking = false });
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _lastSuccessfulCheckStamp, 0);
            var limited = ex as UpdateCheckRateLimitedException;
            var wait = limited?.RetryAfter ?? RetryInterval;
            wait = wait < RetryInterval ? RetryInterval : wait > MaximumRateLimitWait ? MaximumRateLimitWait : wait;
            _rateLimitedUntil = limited is null ? null : (timeProvider ?? TimeProvider.System).GetUtcNow() + wait;
            log?.Invoke($"check_failed kind={ex.GetType().Name} status={(ex as HttpRequestException)?.StatusCode}");
            SetState(State with { IsChecking = false, Error = ex is OperationCanceledException
                ? "The update check timed out. Try again." : "Could not check for updates. " + ex.Message });
        }
        finally { _operation.Release(); }
    }

    private void SetState(AppUpdateState state, bool notify = true)
    {
        Volatile.Write(ref _state, state);
        if (!notify) return;
        NotifyChanged(state);
    }

    private void NotifyChanged(AppUpdateState state)
    {
        var subscribers = Changed;
        if (subscribers is null) return;
        foreach (Action<AppUpdateState> subscriber in subscribers.GetInvocationList())
            try { subscriber(state); }
            catch (Exception ex) { log?.Invoke("presentation_failed kind=" + ex.GetType().Name); }
    }
}
