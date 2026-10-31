using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private AppUpdateService? _settingsUpdates;
    private bool _syncingUpdateSettings;

    private void InitializeAppUpdateSettings()
    {
        if (_settingsUpdates is not null) return;
        _settingsUpdates = App.Services.GetService<AppUpdateService>();
        if (_settingsUpdates is null) return;
        AppUpdatesSettingsCard.Visibility = Visibility.Visible;
        _settingsUpdates.Changed += OnUpdateSettingsState;
        SynchronizeUpdateSettings();
    }

    private void ReleaseAppUpdateSettings()
    {
        if (_settingsUpdates is not null) _settingsUpdates.Changed -= OnUpdateSettingsState;
        _settingsUpdates = null;
    }

    private void OnUpdateSettingsState(AppUpdateState state) => DispatcherQueue.TryEnqueue(SynchronizeUpdateSettings);
    private void SynchronizeUpdateSettings()
    {
        if (_settingsUpdates is null) return;
        _syncingUpdateSettings = true;
        try { AppUpdateSettingsModeCombo.SelectedIndex = (int)_settingsUpdates.Mode; AppUpdateSettingsChannelCombo.SelectedIndex = (int)_settingsUpdates.Channel; }
        finally { _syncingUpdateSettings = false; }
        // A missing/inconsistent stamped identity leaves only the numeric version.
        var installed = _settingsUpdates.InstalledReleaseVersion;
        AppUpdateInstalledVersionText.Text = installed is null ? $"Installed version {_settingsUpdates.InstalledDisplayVersion}"
            : $"Installed version {installed.Text} · {installed.Designation}";
        AppUpdateSettingsChannelCombo.IsEnabled = !_settingsUpdates.IsChangingChannel;
        AppUpdateSettingsBetaWarning.Visibility = _settingsUpdates.Channel == AppUpdateChannel.Beta || installed?.IsPrerelease == true ? Visibility.Visible : Visibility.Collapsed;
        var state = _settingsUpdates.State;
        AppUpdateSettingsStatus.Text = state.Error ?? (_settingsUpdates.IsChangingChannel ? "Switching update channel…" : state.IsChecking ? "Checking for updates…" : state.IsDownloading ? "Downloading update in the background…"
            : state.Release is null ? state.ChannelNotice ?? "No newer release is currently available."
            : state.RequiresFullInstaller ? "A full installer is required. View update details to review and confirm closing Silo."
            : $"{(state.ReadyInstaller is not null || state.PreparedPackage is not null ? "Update ready" : "Update available")}: {state.Release.DisplayVersion} · {(state.Release.IsBeta ? "Beta" : "Stable")}");
        AppUpdateSettingsStatus.Foreground = state.Release is not null ? Helpers.AppUpdateAppearance.Green()
            : (Brush)Application.Current.Resources["SecondaryTextBrush"];
    }

    private async void AppUpdateSettingsChannelChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingUpdateSettings || _settingsUpdates is not { } updates || AppUpdateSettingsChannelCombo.SelectedIndex < 0) return;
        var channel = (AppUpdateChannel)AppUpdateSettingsChannelCombo.SelectedIndex;
        var preferences = App.Services.GetRequiredService<SettingsService>(); var settings = preferences.Load();
        settings.UpdateChannel = channel; preferences.Save(settings);
        try { await updates.SetChannelAsync(channel); await updates.CheckAsync(); }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "settings_update_channel"); }
    }

    private void AppUpdateSettingsModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingUpdateSettings || _settingsUpdates is null || AppUpdateSettingsModeCombo.SelectedIndex < 0) return;
        var mode = (AppUpdateMode)AppUpdateSettingsModeCombo.SelectedIndex;
        var service = App.Services.GetRequiredService<SettingsService>(); var preferences = service.Load();
        preferences.UpdateMode = mode; service.Save(preferences); _settingsUpdates.SetMode(mode);
    }

    private async void AppUpdateSettingsDetails_Click(object sender, RoutedEventArgs args)
    {
        if (App.MainWindowInstance is { } window) await window.ShowAppUpdateDialogAsync(manualCheck: true);
    }
}
