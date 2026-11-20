using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private static readonly string[] DefaultKeys = ["defaults.ui.next_up_mode", "defaults.ui.next_up_new_releases_first"];
    private AdminSettingsSnapshot? _settings;
    private bool _settingsBusy, _settingsReview;
    private async Task LoadDefaultsAsync()
    {
        var owner = _controller?.State.Owner; if (!_active || owner?.IsCurrent != true || _settingsBusy) return;
        _settingsBusy = true; _settings = null; _defaults.Children.Clear(); _defaults.Children.Add(U.Text("Loading Next Up defaults…", 13, true));
        try
        {
            var value = await _settingsApi.GetEffectiveAsync(owner, DefaultKeys, [], owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _settings = value; _settingsReview = false;
        }
        catch (Exception) { if (_active && owner.IsCurrent) _settingsReview = true; }
        finally
        {
            _settingsBusy = false;
            if (_active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner)) RenderDefaults();
        }
    }
    private void RenderDefaults()
    {
        _defaults.Children.Clear(); _defaults.Children.Add(U.Text(T("web.home.preferences.adminHeading"), 16));
        _defaults.Children.Add(U.Text(T("web.home.preferences.adminHelp"), 13, true));
        if (_settings == null || _settingsReview)
        {
            _defaults.Children.Add(U.Text("Next Up defaults need to be reloaded and reviewed before another change.", 13));
            var retry = Action("Reload defaults", "HomeSectionsDefaultsReload"); retry.IsEnabled = !_settingsBusy;
            retry.Click += async (_, _) => await LoadDefaultsAsync(); _defaults.Children.Add(retry); return;
        }
        var mode = ReadDefault("defaults.ui.next_up_mode", "combined");
        var choice = Select("HomeSectionsNextUpMode", [("combined", T("settingsManifest.ui.next_up_mode.option.combined")), ("separate", T("settingsManifest.ui.next_up_mode.option.separate"))], mode,
            async value => await SaveDefaultAsync(DefaultKeys[0], JsonSerializer.Serialize(value)));
        choice.IsEnabled = !_settingsBusy; Field(_defaults, T("settingsManifest.ui.next_up_mode.label"), choice);
        _defaults.Children.Add(U.Text(T("settingsManifest.ui.next_up_mode.description"), 12, true));
        var first = Toggle("HomeSectionsNewEpisodesFirst", ReadDefault("defaults.ui.next_up_new_releases_first", "false") == "true",
            async value => await SaveDefaultAsync(DefaultKeys[1], value ? "true" : "false"));
        first.IsEnabled = !_settingsBusy; Field(_defaults, T("settingsManifest.ui.next_up_new_releases_first.label"), first);
        _defaults.Children.Add(U.Text(T("settingsManifest.ui.next_up_new_releases_first.description"), 12, true));
    }
    private string ReadDefault(string key, string fallback)
    {
        try { using var json = JsonDocument.Parse(_settings!.Values.GetValueOrDefault(key, "null")); return json.RootElement.ValueKind == JsonValueKind.String ? json.RootElement.GetString()! : json.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False ? json.RootElement.GetRawText() : fallback; }
        catch (JsonException) { return fallback; }
    }
    private async Task SaveDefaultAsync(string key, string value)
    {
        var owner = _controller?.State.Owner; var settings = _settings;
        if (!_active || owner?.IsCurrent != true || settings == null || _settingsBusy || _settingsReview) return;
        _settingsBusy = true; RenderDefaults();
        try
        {
            var result = await _settingsApi.PutAsync(owner, new Dictionary<string, string> { [key] = value }, settings.ETag, DefaultKeys, [], owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _settings = new(result.Values, result.ETag);
            InvalidateHome();
        }
        catch (Exception ex)
        {
            if (_active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner))
            { _settingsReview = true; _status.Text = ex is AdminSettingsConflictException ? "Defaults changed elsewhere. Reload and review." : "The save outcome is unknown. Reload defaults and review. The request was not replayed."; }
        }
        finally { _settingsBusy = false; if (_active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner)) RenderDefaults(); }
    }
}
