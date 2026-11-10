using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Services;

namespace SiloPlayer;

// "Later" in the update panel: Silo keeps running with the download ready, and the next
// start finishes the update through the same window, release authority and approval.
public sealed partial class MainWindow
{
    private DeferredAppUpdate? _deferredUpdate;
    // The release the update window has fully downloaded while it waits for an answer.
    private AppRelease? _windowedDownloadReady;

    private void DeferAppUpdate(AppRelease release)
    {
        if (_appUpdates is null) return;
        try
        {
            DeferredAppUpdate.Save(_appUpdates.CacheDirectory, release);
            LocalLog.AppendLine("updates.txt", "update_deferred version=" + release.Version);
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "update_deferral_save"); }
    }

    private void LoadDeferredAppUpdate()
    {
        if (_appUpdates is null || Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is not null) return;
        _deferredUpdate = DeferredAppUpdate.Read(_appUpdates.CacheDirectory);
        if (_deferredUpdate is not null) LocalLog.AppendLine("updates.txt", "deferred_update_found version=" + _deferredUpdate.Version);
    }

    /// <summary>Called on every update/playback change; starts the deferred update once a check allows it and Silo is idle.</summary>
    private void TryResumeDeferredAppUpdate()
    {
        if (_deferredUpdate is not { } deferred || _appUpdates is null || !RootGrid.IsLoaded) return;
        var state = _appUpdates.State;
        var action = _appUpdates.IsChangingChannel ? DeferredUpdateAction.Wait
            : deferred.Decide(_appUpdates.CurrentVersion, state, _appUpdates.LastCheckSucceeded);
        if (action == DeferredUpdateAction.Wait) return;
        if (action == DeferredUpdateAction.Resume && (_updateDialogBusy || _updateWindowOwnsUpdate || _appUpdateDialog is not null
            || _playerService.State != PlayerState.Idle || _playerService.IsLoading)) return;
        // One attempt per deferral: a declined approval or a failure returns to the normal update notice.
        _deferredUpdate = null;
        DeferredAppUpdate.Clear(_appUpdates.CacheDirectory);
        if (action == DeferredUpdateAction.Discard)
        {
            LocalLog.AppendLine("updates.txt", "deferred_update_retired version=" + deferred.Version);
            return;
        }
        _ = ResumeDeferredAppUpdateAsync(state.Release!);
    }

    private async Task ResumeDeferredAppUpdateAsync(AppRelease release)
    {
        var installer = App.Services.GetService<IAppUpdateInstaller>();
        if (_appUpdates is null || installer is not IWindowedAppUpdateInstaller windowed || installer.IsSupported != true
            || !_appUpdates.PreferIncrementalUpdates || RequiresManualFullInstaller(_appUpdates.State))
        {
            LocalLog.AppendLine("updates.txt", "deferred_update_unavailable");
            return;
        }
        _updateDialogBusy = true; RefreshUpdateDialog();
        using var consent = CancellationTokenSource.CreateLinkedTokenSource(_appUpdateLifetime!.Token);
        _manualUpdateConsent = consent;
        try
        {
            LocalLog.AppendLine("updates.txt", "deferred_update_resume version=" + release.Version);
            // The user already chose to update; the window does not ask again.
            await RunWindowedUpdateAsync(null, windowed, release, confirmRestart: false, consent.Token);
        }
        catch (OperationCanceledException) when (consent.IsCancellationRequested) { await _appUpdates.StopAsync(); }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "deferred_update_resume"); }
        finally { _manualUpdateConsent = null; _updateDialogBusy = false; RefreshUpdateDialog(); }
    }
}
