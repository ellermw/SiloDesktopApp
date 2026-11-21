using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>The server-wide Next Up defaults under the list (official AdminNextUpDefaultsCard): each control saves as it changes, one save at a time.</summary>
public sealed partial class AdminHomeSectionsPage
{
    private static readonly string[] DefaultKeys = ["defaults.ui.next_up_mode", "defaults.ui.next_up_new_releases_first"];
    private AdminSettingsSnapshot? _settings;
    private bool _settingsBusy, _settingsReview, _settingsLoadFailed;
    private string? _defaultsMessage;

    private async Task LoadDefaultsAsync()
    {
        var owner = _controller?.State.Owner; if (!_active || owner?.IsCurrent != true || _settingsBusy) return;
        _settingsBusy = true; _settings = null; _settingsLoadFailed = false; _defaultsMessage = null; RenderDefaults();
        try
        {
            var value = await _settingsApi.GetEffectiveAsync(owner, DefaultKeys, [], owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _settings = value; _settingsReview = false;
        }
        catch (Exception) { if (_active && owner.IsCurrent) _settingsLoadFailed = true; }
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
        if (_settings == null && _settingsBusy && !_settingsLoadFailed) return;
        if (_settings == null || _settingsReview)
        {
            _defaults.Children.Add(U.Alert(_defaultsMessage ?? (_settingsLoadFailed ? T("web.admin.homeRows.page.loadRowsFailed") : "Next Up defaults need to be reloaded and reviewed before another change.")));
            var retry = Action(T("web.home.rows.reloadRows"), "HomeSectionsDefaultsReload"); retry.IsEnabled = !_settingsBusy;
            retry.Click += async (_, _) => await LoadDefaultsAsync(); _defaults.Children.Add(retry); return;
        }
        var mode = ReadDefault("defaults.ui.next_up_mode", "combined");
        var choice = Select("HomeSectionsNextUpMode", [("combined", T("settingsManifest.ui.next_up_mode.option.combined")), ("separate", T("settingsManifest.ui.next_up_mode.option.separate"))], mode,
            async value => { if (value != mode) await SaveDefaultAsync(DefaultKeys[0], JsonSerializer.Serialize(value)); });
        choice.IsEnabled = !_settingsBusy; Field(_defaults, T("settingsManifest.ui.next_up_mode.label"), choice);
        _defaults.Children.Add(U.Text(T("settingsManifest.ui.next_up_mode.description"), 12, true));
        var newFirst = ReadDefault("defaults.ui.next_up_new_releases_first", "false") == "true";
        var first = Toggle("HomeSectionsNewEpisodesFirst", newFirst, async value => { if (value != newFirst) await SaveDefaultAsync(DefaultKeys[1], value ? "true" : "false"); });
        first.IsEnabled = !_settingsBusy; Field(_defaults, T("settingsManifest.ui.next_up_new_releases_first.label"), first);
        _defaults.Children.Add(U.Text(T("settingsManifest.ui.next_up_new_releases_first.description"), 12, true));
        if (_defaultsMessage != null) _defaults.Children.Add(U.Text(_defaultsMessage, 13));
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
        _settingsBusy = true; _defaultsMessage = null; RenderDefaults();
        try
        {
            var result = await _settingsApi.PutAsync(owner, new Dictionary<string, string> { [key] = value }, settings.ETag, DefaultKeys, [], owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _settings = new(result.Values, result.ETag);
            Toasts?.Success(T("web.home.preferences.adminSaved"));
            InvalidateHome();
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception ex)
        {
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            // A definite refusal leaves the saved value and lets the admin correct and retry; only a stale version or an unknown outcome asks for a reload.
            if (ex is AdminSettingsConflictException) { _settingsReview = true; _defaultsMessage = "Defaults changed elsewhere. Reload and review."; }
            else if (ex is AdminSettingsOutcomeUnknownException) { _settingsReview = true; _defaultsMessage = "The save outcome is unknown. Reload defaults and review. The request was not replayed."; }
            else _defaultsMessage = ex is ApiException api ? AdminHomeSectionsController.RefusalText(api) : ex.Message;
        }
        finally { _settingsBusy = false; if (_active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner)) RenderDefaults(); }
    }
}
