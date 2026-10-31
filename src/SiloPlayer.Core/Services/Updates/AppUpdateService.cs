using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public sealed partial class AppUpdateService(HttpClient http, string cacheDirectory, Version currentVersion, Action<string>? log = null, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private AppUpdateState _state = new();
    private long _lastSuccessfulCheckStamp;
    private ReleaseCheck? _lastCheck;
    private DateTimeOffset? _rateLimitedUntil;
    private readonly object _cooldownGate=new();
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15), RetryInterval = TimeSpan.FromMinutes(5),
        MaximumRateLimitWait = TimeSpan.FromHours(6);
    public AppUpdateState State => Volatile.Read(ref _state);
    public string CacheDirectory => Path.GetFullPath(cacheDirectory);
    public bool PreferIncrementalUpdates { get; init; }
    public string InstalledPayloadDirectory { get; init; } = AppContext.BaseDirectory;
    public string? InstallationRoot { get; init; }
    public void ApplyAuthorityCooldown(DateTimeOffset? until)
    {
        var now=(timeProvider??TimeProvider.System).GetUtcNow();
        if(until is null || until<=now)return;
        var bounded=until>now+MaximumRateLimitWait ? now+MaximumRateLimitWait : until.Value;
        lock(_cooldownGate)if(_rateLimitedUntil is null || _rateLimitedUntil<bounded)_rateLimitedUntil=bounded;
        SetState(State with{Error="Could not check for updates. "+UpdateCheckRateLimitedException.UserMessage});
    }
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
        lock(_cooldownGate)
        {
        if(InstallationRoot is { } root && UpdateAuthorityCooldown.Read(root,timeProvider) is { } external
            && (_rateLimitedUntil is null || _rateLimitedUntil<external))_rateLimitedUntil=external;
        if (_rateLimitedUntil is not { } until) return null;
        var remaining = until - (timeProvider ?? TimeProvider.System).GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!await _operation.WaitAsync(0, ct).ConfigureAwait(false)) return;
        var epoch = Volatile.Read(ref _channelEpoch);
        try
        {
            if (IsChangingChannel) return;
            if (InstalledReleaseVersion is { } identity && identity.GetWindowsVersion() != GitHubReleaseReader.Normalize(currentVersion))
                throw new InvalidDataException("The installed semantic and Windows versions disagree.");
            // Enforce the deadline at dispatch as well as in the timer: a
            // manual check can learn a limit after that timer was scheduled.
            // Download progress/errors are separate from this network limit.
            if (RemainingRateLimitDelay() is not null)
            {
                SetState(State with { IsChecking = false, Error = "Could not check for updates. " + UpdateCheckRateLimitedException.UserMessage }, expectedChannelEpoch: epoch);
                log?.Invoke("check_deferred_rate_limit");
                return;
            }
            SetState(State with { IsChecking = true, Error = null }, expectedChannelEpoch: epoch);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var check = await new GitHubReleaseReader(http, timeProvider).ReadForChannelAsync(currentVersion, InstalledReleaseVersion, Channel, _lastCheck, deadline.Token).ConfigureAwait(false);
            if (epoch != Volatile.Read(ref _channelEpoch)) return;
            _lastCheck = check; lock(_cooldownGate)_rateLimitedUntil = null;
            var release = check.Release;
            Interlocked.Exchange(ref _lastSuccessfulCheckStamp, System.Diagnostics.Stopwatch.GetTimestamp());
            var previous=State;
            var same = release is not null && previous.Release?.Version == release.Version && previous.Release.Tag == release.Tag && previous.Release.Sha256 == release.Sha256
                && previous.Release.Size == release.Size && previous.Release.InstallerUrl == release.InstallerUrl
                && previous.Release.HasInvalidIncrementalAssets == release.HasInvalidIncrementalAssets && previous.Release.Incremental == release.Incremental;
            SetState(new AppUpdateState(release, ReadyInstaller: same ? previous.ReadyInstaller : null)
                { PreparedPackage=same ? previous.PreparedPackage : null, PreparationProgress=same ? previous.PreparationProgress : null,
                    ChannelNotice=ChannelNotice(release), RequiresFullInstaller=same && previous.RequiresFullInstaller }, expectedChannelEpoch: epoch);
            log?.Invoke($"check_ok version={release?.Version.ToString() ?? "current"}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetState(State with { IsChecking = false }, expectedChannelEpoch: epoch);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _lastSuccessfulCheckStamp, 0);
            var limited = ex as UpdateCheckRateLimitedException;
            var wait = limited?.RetryAfter ?? RetryInterval;
            wait = wait < RetryInterval ? RetryInterval : wait > MaximumRateLimitWait ? MaximumRateLimitWait : wait;
            lock(_cooldownGate)_rateLimitedUntil = limited is null ? null : (timeProvider ?? TimeProvider.System).GetUtcNow() + wait;
            log?.Invoke($"check_failed kind={ex.GetType().Name} status={(ex as HttpRequestException)?.StatusCode}");
            SetState(State with { IsChecking = false, Error = ex is OperationCanceledException
                ? "The update check timed out. Try again." : "Could not check for updates. " + ex.Message }, expectedChannelEpoch: epoch);
        }
        finally { _operation.Release(); }
    }

    private void SetState(AppUpdateState state, bool notify = true, long? expectedChannelEpoch = null)
    {
        lock (_channelGate)
        {
            if (expectedChannelEpoch is { } epoch && epoch != _channelEpoch) return;
            Volatile.Write(ref _state, state);
        }
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
