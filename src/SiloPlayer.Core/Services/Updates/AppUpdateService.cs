using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public sealed partial class AppUpdateService(HttpClient http, string cacheDirectory, Version currentVersion, Action<string>? log = null, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private AppUpdateState _state = new();
    private long _lastSuccessfulCheckStamp;
    public AppUpdateState State => Volatile.Read(ref _state);
    public string CacheDirectory => Path.GetFullPath(cacheDirectory);
    public event Action<AppUpdateState>? Changed;

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!await _operation.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            SetState(State with { IsChecking = true, Error = null });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var release = await new GitHubReleaseReader(http).ReadAsync(currentVersion, deadline.Token).ConfigureAwait(false);
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
