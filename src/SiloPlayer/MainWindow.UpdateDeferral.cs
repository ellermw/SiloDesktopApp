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

    // The helper closes after Later is acknowledged. Keep its confirmation in
    // Silo, and show it only after the next-start record has been saved/read back.
    private bool DeferAppUpdateWithNotice(AppRelease release)
    {
        if (!DeferAppUpdate(release)) return false;
        App.Services.GetRequiredService<ToastService>().Info(UpdatePanelModel.DeferredText);
        return true;
    }

    /// <summary>Records Later and reads it back; false when the next start would not find it.</summary>
    private bool DeferAppUpdate(AppRelease release)
    {
        if (_appUpdates is null) return false;
        try
        {
            DeferredAppUpdate.Save(_appUpdates.CacheDirectory, release, _appUpdates.Channel);
            if (DeferredAppUpdate.Read(_appUpdates.CacheDirectory)?.Tag != release.Tag) throw new IOException("The deferred update record could not be read back.");
            LocalLog.AppendLine("updates.txt", "update_deferred version=" + release.Version);
            return true;
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "update_deferral_save"); return false; }
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
            : deferred.Decide(_appUpdates.CurrentVersion, state, _appUpdates.LastCheckSucceeded, _appUpdates.Channel);
        if (action == DeferredUpdateAction.Wait) return;
        if (action == DeferredUpdateAction.Resume && (_updateDialogBusy || _updateWindowOwnsUpdate || _appUpdateDialog is not null
            || _playerService.State != PlayerState.Idle || _playerService.IsLoading)) return;
        _deferredUpdate = null;
        if (action == DeferredUpdateAction.Discard)
        {
            DeferredAppUpdate.Clear(_appUpdates.CacheDirectory);
            LocalLog.AppendLine("updates.txt", "deferred_update_retired version=" + deferred.Version);
            return;
        }
        // The record stays on disk until this attempt ends, so closing Silo during the resume keeps it for the next start.
        _ = ResumeDeferredAppUpdateAsync(state.Release!);
    }

    private async Task ResumeDeferredAppUpdateAsync(AppRelease release)
    {
        var installer = App.Services.GetService<IAppUpdateInstaller>();
        if (_appUpdates is null || installer is not IWindowedAppUpdateInstaller windowed || installer.IsSupported != true
            || !_appUpdates.PreferIncrementalUpdates || RequiresManualFullInstaller(_appUpdates.State))
        {
            if (_appUpdates is not null) DeferredAppUpdate.Clear(_appUpdates.CacheDirectory);
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
        finally
        {
            // One attempt per deferral: a declined approval or a failure returns to the normal update notice.
            // A staged update already retired it. Only an abandoned resume (Silo closing) keeps it for the next start.
            if (consent.IsCancellationRequested) LocalLog.AppendLine("updates.txt", "deferred_update_kept version=" + release.Version);
            else DeferredAppUpdate.Clear(_appUpdates.CacheDirectory);
            _manualUpdateConsent = null; _updateDialogBusy = false; RefreshUpdateDialog();
        }
    }
}
