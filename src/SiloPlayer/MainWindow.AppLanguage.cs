using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer;

/// <summary>
/// The window's half of the interface language: the shell's language tag and
/// direction follow the applied locale, and profile, sign-out and realtime
/// settings changes ask the coordinator for the owner's effective value.
/// </summary>
public sealed partial class MainWindow
{
    private IDisposable? _appLanguageShell;
    private AppLocaleCoordinator? _appLocaleCoordinator;
    private ApiRequestContext? _appLanguageRequestedOwner;
    private Task? _appLanguageRefresh;

    private void InitializeAppLanguage()
    {
        _appLocaleCoordinator = App.Services.GetService<AppLocaleCoordinator>();
        // NavView holds the shell and pages. The player overlay, mini player and
        // the native video window keep left-to-right geometry.
        _appLanguageShell = AppLocalization.AttachShell(RootGrid, NavView);
    }

    private void ReleaseAppLanguage()
    {
        _appLanguageShell?.Dispose();
        _appLanguageShell = null;
        _appLocaleCoordinator?.Retire();
    }

    /// <summary>A new account/profile owner reads its own setting; the same owner keeps the loaded value.</summary>
    private void RefreshAppLanguageForOwner()
    {
        if (_appLocaleCoordinator is not { } coordinator) return;
        var context = _apiClient.CaptureContext();
        if (context.ProfileId == null) { coordinator.Retire(); return; }
        if (coordinator.LoadedOwner is { } loaded && SameLanguageOwner(loaded, context)) return;
        // Profile and shell events often arrive together; one read per owner is enough.
        if (_appLanguageRequestedOwner is { } requested && SameLanguageOwner(requested, context)
            && _appLanguageRefresh is { IsCompleted: false }) return;
        coordinator.Retire();
        _appLanguageRequestedOwner = context;
        _appLanguageRefresh = RefreshAppLanguageAsync(coordinator);
    }

    private void RetireAppLanguageOwner() => _appLocaleCoordinator?.Retire();

    private void NotifyAppLanguageFromRealtime(string channel, string eventName, JsonElement data)
    {
        if (_appLocaleCoordinator is not { } coordinator
            || !AppLanguagePreference.IsRealtimeChange(channel, eventName, data, _authService.SelectedProfileId)) return;
        _ = RefreshAppLanguageAsync(coordinator);
    }

    private static async Task RefreshAppLanguageAsync(AppLocaleCoordinator coordinator)
    {
        try { await coordinator.RefreshAsync(); }
        catch (Exception ex) { LocalLog.AppendException("settings_refresh.txt", ex, "app_language_refresh"); }
    }

    private static bool SameLanguageOwner(ApiRequestContext a, ApiRequestContext b)
        => a.AuthenticationGeneration == b.AuthenticationGeneration && a.BaseUrl == b.BaseUrl && a.ProfileId == b.ProfileId;
}
