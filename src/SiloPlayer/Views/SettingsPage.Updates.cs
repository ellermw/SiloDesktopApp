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
        try { AppUpdateSettingsModeCombo.SelectedIndex = (int)_settingsUpdates.Mode; }
        finally { _syncingUpdateSettings = false; }
        AppUpdateInstalledVersionText.Text = "Installed version " + typeof(App).Assembly.GetName().Version!.ToString(3);
        var state = _settingsUpdates.State;
        AppUpdateSettingsStatus.Text = state.Error ?? (state.IsChecking ? "Checking for updates…" : state.IsDownloading ? "Downloading update in the background…"
            : state.Release is null ? "No newer release is currently available." : state.ReadyInstaller is not null ? "Update ready; view the changelog or update now."
            : "Update available: " + state.Release.Version.ToString(3));
        AppUpdateSettingsStatus.Foreground = state.Release is not null ? Helpers.AppUpdateAppearance.Green()
            : (Brush)Application.Current.Resources["SecondaryTextBrush"];
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
