using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public sealed partial class AppUpdateService
{
    private readonly object _transferGate = new();
    private CancellationTokenSource? _transferCancellation;
    private Task _transfer = Task.CompletedTask;
    private bool _automaticTransfer;
    private bool _playbackActive;
    public AppUpdateMode Mode { get; private set; }
    public void CleanupInstalledPackages()
    {
        try
        {
            var root = Path.GetFullPath(cacheDirectory); AppUpdateDownloader.EnsureOrdinaryPath(root);
            if (!Directory.Exists(root)) return;
            foreach (var folder in Directory.EnumerateDirectories(root).Take(32))
            {
                if (!Version.TryParse(Path.GetFileName(folder), out var version) || GitHubReleaseReader.Normalize(version) > GitHubReleaseReader.Normalize(currentVersion)) continue;
                try
                {
                    AppUpdateDownloader.EnsureOrdinaryPath(folder);
                    foreach (var path in Directory.EnumerateFiles(folder).Take(64))
                    {
                        var name = Path.GetFileName(path);
                        if (name != GitHubReleaseReader.InstallerName && name != GitHubReleaseReader.InstallerName + ".part" && name != "package.identity"
                            && !System.Text.RegularExpressions.Regex.IsMatch(name, @"^(?:UpdateOnExit-[a-f0-9]{32}\.ps1|close-[a-f0-9]{32}\.confirmed)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) continue;
                        try { AppUpdateDownloader.EnsureOrdinaryPath(path); File.Delete(path); }
                        catch (IOException) { /* An exiting helper may still hold the package. Retry on next launch. */ }
                    }
                    if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder, recursive: false);
                }
                catch (Exception ex) { log?.Invoke("cache_cleanup_deferred kind=" + ex.GetType().Name); }
            }
        }
        catch (Exception ex) { log?.Invoke("cache_cleanup_failed kind=" + ex.GetType().Name); }
    }

    public void SetMode(AppUpdateMode mode)
    {
        lock (_transferGate)
        {
            Mode = Enum.IsDefined(mode) ? mode : AppUpdateMode.NotifyOnly;
            if (Mode != AppUpdateMode.Automatic && _automaticTransfer) _transferCancellation?.Cancel();
        }
        NotifyChanged(State);
    }

    public void SetPlaybackActive(bool active)
    {
        lock (_transferGate)
        {
            _playbackActive = active;
            if (active && _automaticTransfer) _transferCancellation?.Cancel();
        }
    }

    public Task DownloadAsync(bool automatic = false, CancellationToken ct = default)
    {
        lock (_transferGate)
        {
            if (!_transfer.IsCompleted)
            {
                if (!automatic) _automaticTransfer = false;
                return _transfer;
            }
            if (automatic && (Mode != AppUpdateMode.Automatic || _playbackActive)) return Task.CompletedTask;
            _automaticTransfer = automatic;
            return _transfer = DownloadCoreAsync(ct);
        }
    }

    private async Task DownloadCoreAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), timeProvider ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        lock (_transferGate) _transferCancellation = deadline;
        var entered = false;
        try
        {
            // Wait out a running check (bounded by its own deadline) instead of
            // returning: Update now and the automatic transfer otherwise do
            // nothing, with no error, whenever they overlap a check.
            await _operation.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            var release = State.Release;
            if (release is null) return;
            if(RemainingRateLimitDelay() is not null) throw new InvalidOperationException(UpdateCheckRateLimitedException.UserMessage);
            SetState(State with { IsDownloading = true, Error = null });
            log?.Invoke($"download_start version={release.Version} automatic={_automaticTransfer}");
            if(PreferIncrementalUpdates)
            {
                if(release.Incremental is null) throw new InvalidDataException("This release requires a full installer migration. Open the release page to install it manually.");
                var package=await new IncrementalUpdateClient(http,cacheDirectory,InstalledPayloadDirectory,log).PrepareAsync(release,
                    progress=>SetState(State with {PreparationProgress=progress,DownloadedBytes=progress.CompletedBytes}),deadline.Token).ConfigureAwait(false);
                SetState(State with {IsDownloading=false,PreparedPackage=package,ReadyInstaller=null,Error=null});
                return;
            }
            var lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
            var path = await new AppUpdateDownloader(http, cacheDirectory, log).DownloadAsync(release,
                bytes =>
                {
                    var notify = bytes == release.Size || System.Diagnostics.Stopwatch.GetElapsedTime(lastProgress) >= TimeSpan.FromMilliseconds(250);
                    SetState(State with { DownloadedBytes = bytes }, notify);
                    if (notify) lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
                }, deadline.Token).ConfigureAwait(false);
            SetState(State with { IsDownloading = false, DownloadedBytes = release.Size, ReadyInstaller = path, Error = null });
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            log?.Invoke("download_timed_out");
            SetState(State with { IsDownloading = false, Error = "The update download timed out. Try again to resume it." });
        }
        catch (OperationCanceledException)
        {
            log?.Invoke("download_deferred_or_canceled");
            SetState(State with { IsDownloading = false });
        }
        catch (Exception ex)
        {
            log?.Invoke($"download_failed kind={ex.GetType().Name} status={(ex as HttpRequestException)?.StatusCode}");
            if(ex is UpdateCheckRateLimitedException limited)
            {
                var wait=limited.RetryAfter ?? RetryInterval;
                wait=wait<RetryInterval ? RetryInterval : wait>MaximumRateLimitWait ? MaximumRateLimitWait : wait;
                lock(_cooldownGate)_rateLimitedUntil=(timeProvider??TimeProvider.System).GetUtcNow()+wait;
            }
            SetState(State with { IsDownloading = false, ReadyInstaller = null, PreparedPackage=null, Error = "Could not download the update. " + ex.Message });
        }
        finally
        {
            lock (_transferGate) _transferCancellation = null;
            if (entered) _operation.Release();
        }
    }

    public async Task StopAsync()
    {
        Task transfer;
        lock (_transferGate) { _transferCancellation?.Cancel(); transfer = _transfer; }
        try { await transfer.ConfigureAwait(false); } catch (OperationCanceledException) { }
    }

    public async Task<bool> ReverifyReadyAsync(CancellationToken ct = default)
    {
        var state = State;
        if (state.Release is null || (state.ReadyInstaller is null && state.PreparedPackage is null)) return false;
        try
        {
            if(state.PreparedPackage is { } prepared)
            {
                if(state.Release.Incremental is { } assets && prepared.ManifestSha256.Equals(assets.ManifestSha256,StringComparison.OrdinalIgnoreCase)
                    && await new IncrementalUpdateClient(http,cacheDirectory,InstalledPayloadDirectory,log).VerifyPreparedAsync(prepared,ct).ConfigureAwait(false))return true;
            }
            else if (await AppUpdateDownloader.VerifyAsync(state.ReadyInstaller!, state.Release, ct).ConfigureAwait(false)) return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log?.Invoke("ready_verification_failed kind=" + ex.GetType().Name); }
        SetState(State with { ReadyInstaller = null, PreparedPackage=null, Error = "The downloaded update could not be verified. Download it again." });
        return false;
    }

    public bool ShouldInstallOnExit => Mode == AppUpdateMode.Automatic && State.Release is not null
        && RemainingRateLimitDelay() is null
        && (State.ReadyInstaller is not null || State.PreparedPackage is not null) && Interlocked.Read(ref _lastSuccessfulCheckStamp) is > 0 and var checkedAt
        && System.Diagnostics.Stopwatch.GetElapsedTime(checkedAt) < TimeSpan.FromHours(24);
}
