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
        if (!_operation.Wait(0)) return;
        try
        {
            var root = Path.GetFullPath(cacheDirectory); AppUpdateDownloader.EnsureOrdinaryPath(root);
            if (!Directory.Exists(root)) return;
            var pending = PendingInstallation();
            IncrementalUpdateClient.CleanupCache(root, State.Release?.Incremental?.ManifestSha256, log, currentVersion, pending.Version);
            PruneUpdateRecords(root, pending.TransactionId, (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, log);
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
        finally { _operation.Release(); }
    }

    // The activated update still waiting for its first confirmed start. Its package and success marker
    // stay until then. When the state cannot be read, keep the running version's package.
    private (string? Version, string? TransactionId) PendingInstallation()
    {
        if (InstallationRoot is not { } root) return (null, null);
        try { return SideBySideInstallation.ReadState(root) is { } state ? (state.PendingVersion, state.TransactionId) : (null, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        { return (GitHubReleaseReader.Normalize(currentVersion).ToString(4), null); }
    }

    // Records the full-installer helper and the boot observation leave in the update folder. Nothing reads
    // an Inno log or an orphaned result temp after its run (keep the newest log for diagnosis), install.log
    // is a diagnostic history kept to its recent end, and a success marker only stops a repeated toast
    // while its transaction is still pending.
    internal static void PruneUpdateRecords(string root, string? pendingTransaction, DateTime utcNow, Action<string>? log)
    {
        static void Delete(string path)
        {
            try { AppUpdateDownloader.EnsureOrdinaryPath(path); File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        try
        {
            foreach (var file in Directory.GetFiles(root, "result-*.tmp"))
                if (ResultTemporary().IsMatch(Path.GetFileName(file)) && utcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1)) Delete(file);
            foreach (var file in Directory.GetFiles(root, "installer-*.log").Where(path => InstallerLog().IsMatch(Path.GetFileName(path)))
                         .OrderByDescending(File.GetLastWriteTimeUtc).Skip(1)) Delete(file);
            TrimInstallLog(Path.Combine(root, "install.log"));
            var boots = Path.Combine(root, "boots");
            AppUpdateDownloader.EnsureOrdinaryPath(boots);
            if (!Directory.Exists(boots)) return;
            foreach (var directory in Directory.GetDirectories(boots).Where(path => UpdateManifestValidation.IsHash(Path.GetFileName(path))))
            foreach (var marker in Directory.GetFiles(directory, "success-*.shown"))
                if (SuccessMarker().Match(Path.GetFileName(marker)) is { Success: true } match &&
                    !match.Groups[1].Value.Equals(pendingTransaction, StringComparison.OrdinalIgnoreCase)) Delete(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { log?.Invoke("update_record_cleanup_deferred kind=" + ex.GetType().Name); }
    }

    private const int InstallLogLimit = 64 * 1024, InstallLogKept = 16 * 1024;
    private static void TrimInstallLog(string path)
    {
        try
        {
            AppUpdateDownloader.EnsureOrdinaryPath(path);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= InstallLogLimit) return;
            var tail = new byte[InstallLogKept];
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                input.Seek(-InstallLogKept, SeekOrigin.End);
                input.ReadExactly(tail);
            }
            var start = Array.IndexOf(tail, (byte)'\n') + 1;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, tail[start..]);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^result-[0-9a-f]{32}\\.tmp$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ResultTemporary();
    [System.Text.RegularExpressions.GeneratedRegex("^installer-[0-9a-f]{32}\\.log$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex InstallerLog();
    [System.Text.RegularExpressions.GeneratedRegex("^success-([0-9a-f]{32})\\.shown$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SuccessMarker();

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
            if (IsChangingChannel) return Task.CompletedTask;
            if (!_transfer.IsCompleted)
            {
                if (!automatic)
                {
                    _automaticTransfer = false;
                    return CompleteManualDownloadAsync(_transfer, Volatile.Read(ref _channelEpoch), ct);
                }
                return _transfer;
            }
            if (automatic && (Mode != AppUpdateMode.Automatic || _playbackActive || State.RequiresFullInstaller)) return Task.CompletedTask;
            _automaticTransfer = automatic;
            return _transfer = DownloadCoreAsync(ct);
        }
    }

    private async Task CompleteManualDownloadAsync(Task transfer, long epoch, CancellationToken ct)
    {
        await transfer.WaitAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        lock (_transferGate)
        {
            // The automatic transfer may already have decided to defer its full
            // fallback before the manual request promoted it. Finish that request,
            // but never carry consent across a channel change or retry a failure.
            var state = State;
            if (epoch != Volatile.Read(ref _channelEpoch) || IsChangingChannel || !state.RequiresFullInstaller
                || state.ReadyInstaller is not null || state.PreparedPackage is not null || state.Error is not null) return;
            transfer = DownloadAsync(ct: ct);
        }
        await transfer.ConfigureAwait(false);
    }

    private async Task DownloadCoreAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15), timeProvider ?? TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        lock (_transferGate) _transferCancellation = deadline;
        var entered = false;
        var epoch = Volatile.Read(ref _channelEpoch);
        try
        {
            // Wait out a running check (bounded by its own deadline) instead of
            // returning: Update now and the automatic transfer otherwise do
            // nothing, with no error, whenever they overlap a check.
            await _operation.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            if (IsChangingChannel || epoch != Volatile.Read(ref _channelEpoch)) return;
            var release = State.Release;
            if (release is null) return;
            if(RemainingRateLimitDelay() is not null) throw new InvalidOperationException(UpdateCheckRateLimitedException.UserMessage);
            SetState(State with { IsDownloading = true, Error = null }, expectedChannelEpoch: epoch);
            log?.Invoke($"download_start version={release.Version} automatic={_automaticTransfer}");
            if(PreferIncrementalUpdates)
            {
                if (release.HasInvalidIncrementalAssets) throw new InvalidDataException("The incremental release metadata is invalid.");
                // A verified migration decision belongs to this release and is retired
                // with it. Re-clicks must reuse the full installer, not retry the manifest.
                if (release.Incremental is not null && !State.RequiresFullInstaller)
                {
                    try
                    {
                        var client = new IncrementalUpdateClient(http, cacheDirectory, InstalledPayloadDirectory, log);
                        var semantic = IncrementalUpdateTransport.ValidateAssets(release);
                        var assets = release.Incremental;
                        // Re-clicks verify the ready payload once, without fetching the
                        // manifest or walking the pack/object preparation caches again.
                        if (State.PreparedPackage is { } prepared
                            && prepared.ManifestSha256.Equals(assets.ManifestSha256, StringComparison.OrdinalIgnoreCase)
                            && prepared.Manifest.Version == GitHubReleaseReader.Normalize(release.Version).ToString(4)
                            && prepared.Manifest.PackSize == assets.PackSize
                            && prepared.Manifest.PackSha256.Equals(assets.PackSha256, StringComparison.OrdinalIgnoreCase)
                            && (semantic?.IsPrerelease != true || prepared.Manifest.ReleaseVersion is not null)
                            && (semantic is null || prepared.Manifest.ReleaseVersion is null || prepared.Manifest.ReleaseVersion == semantic.Text)
                            && await client.VerifyPreparedAsync(prepared, deadline.Token).ConfigureAwait(false))
                        {
                            SetState(State with {IsDownloading=false,ReadyInstaller=null,RequiresFullInstaller=false,Error=null}, expectedChannelEpoch: epoch);
                            return;
                        }
                        var package=await client.PrepareAsync(release,
                            progress=>SetState(State with {PreparationProgress=progress,DownloadedBytes=progress.CompletedBytes}, expectedChannelEpoch: epoch),deadline.Token).ConfigureAwait(false);
                        SetState(State with {IsDownloading=false,PreparedPackage=package,ReadyInstaller=null,RequiresFullInstaller=false,Error=null}, expectedChannelEpoch: epoch);
                        return;
                    }
                    catch (UpdateMigrationRequiredException) { log?.Invoke("verified_full_migration_required"); }
                }
                // A missing or independently verified incompatible protocol can use
                // the separately hashed installer. Corruption never takes this path.
                bool deferFullInstaller;
                lock (_transferGate) deferFullInstaller = _automaticTransfer;
                SetState(State with {RequiresFullInstaller=true,PreparedPackage=null,PreparationProgress=null,DownloadedBytes=0,IsDownloading=!deferFullInstaller}, expectedChannelEpoch: epoch);
                if (deferFullInstaller) return;
            }
            var lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
            var path = await new AppUpdateDownloader(http, cacheDirectory, log).DownloadAsync(release,
                bytes =>
                {
                    var notify = bytes == release.Size || System.Diagnostics.Stopwatch.GetElapsedTime(lastProgress) >= TimeSpan.FromMilliseconds(250);
                    SetState(State with { DownloadedBytes = bytes }, notify, epoch);
                    if (notify) lastProgress = System.Diagnostics.Stopwatch.GetTimestamp();
                }, deadline.Token).ConfigureAwait(false);
            SetState(State with { IsDownloading = false, DownloadedBytes = release.Size, ReadyInstaller = path, Error = null }, expectedChannelEpoch: epoch);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            log?.Invoke("download_timed_out");
            SetState(State with { IsDownloading = false, Error = "The update download timed out. Try again to resume it." }, expectedChannelEpoch: epoch);
        }
        catch (OperationCanceledException)
        {
            log?.Invoke("download_deferred_or_canceled");
            SetState(State with { IsDownloading = false }, expectedChannelEpoch: epoch);
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
            SetState(State with { IsDownloading = false, ReadyInstaller = null, PreparedPackage=null, Error = "Could not download the update. " + ex.Message }, expectedChannelEpoch: epoch);
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
        var epoch = Volatile.Read(ref _channelEpoch);
        if (IsChangingChannel) return false;
        var state = State;
        if (state.Release is null) return false;
        if (state.ReadyInstaller is null && state.PreparedPackage is null)
        {
            SetState(state with { Error = state.Error ?? "The update is not ready. Download it again to continue." }, expectedChannelEpoch: epoch);
            return false;
        }
        try
        {
            if(state.PreparedPackage is { } prepared)
            {
                if(state.Release.Incremental is { } assets && prepared.ManifestSha256.Equals(assets.ManifestSha256,StringComparison.OrdinalIgnoreCase)
                    && await new IncrementalUpdateClient(http,cacheDirectory,InstalledPayloadDirectory,log).VerifyPreparedAsync(prepared,ct).ConfigureAwait(false))return epoch == Volatile.Read(ref _channelEpoch) && !IsChangingChannel;
            }
            else if (await AppUpdateDownloader.VerifyAsync(state.ReadyInstaller!, state.Release, ct).ConfigureAwait(false)) return epoch == Volatile.Read(ref _channelEpoch) && !IsChangingChannel;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log?.Invoke("ready_verification_failed kind=" + ex.GetType().Name); }
        SetState(State with { ReadyInstaller = null, PreparedPackage=null, Error = "The downloaded update could not be verified. Download it again." }, expectedChannelEpoch: epoch);
        return false;
    }

    public bool ShouldInstallOnExit => !IsChangingChannel && !State.RequiresFullInstaller && Mode == AppUpdateMode.Automatic && State.Release is not null
        && RemainingRateLimitDelay() is null
        && (State.ReadyInstaller is not null || State.PreparedPackage is not null) && Interlocked.Read(ref _lastSuccessfulCheckStamp) is > 0 and var checkedAt
        && System.Diagnostics.Stopwatch.GetElapsedTime(checkedAt) < TimeSpan.FromHours(24);
}
