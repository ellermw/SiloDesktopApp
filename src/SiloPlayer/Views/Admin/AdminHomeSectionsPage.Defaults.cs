using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
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

    /// <summary>The card (AdminNextUpDefaultsCard): a surface panel, rounded-[22px], with its heading and one divided row per setting.</summary>
    private void RenderDefaults()
    {
        // Every path below adds the card, and the panel shows with it: the card is built after the page's last Render, which would otherwise leave it collapsed.
        _defaults.Children.Clear(); _defaults.Visibility = Visibility.Visible;
        var card = new StackPanel();
        void Divider() => card.Children.Add(new Border { Height = 1, Background = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6) });
        FrameworkElement Section(UIElement content, double top = 20, double bottom = 20) => new Border { Padding = new(0, top, 0, bottom), Child = content };
        var heading = new StackPanel { Spacing = 4 };
        var title = U.Text(T("web.home.preferences.adminHeading"), 14); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        heading.Children.Add(title); heading.Children.Add(U.Text(T("web.home.preferences.adminHelp"), 13, true));
        card.Children.Add(Section(heading));
        if (_settings == null && _settingsBusy && !_settingsLoadFailed) { _defaults.Children.Add(AdminMonitorUi.Panel(card, new(22, 0, 22, 0), 22)); return; }
        if (_settings == null || _settingsReview)
        {
            Divider();
            var failure = new StackPanel { Spacing = 12 };
            failure.Children.Add(U.Alert(_defaultsMessage ?? (_settingsLoadFailed ? T("web.admin.homeRows.page.loadRowsFailed") : AdminHomeSectionsText.T("native.homeSections.defaultsReview"))));
            var retry = Action(T("web.home.rows.reloadRows"), "HomeSectionsDefaultsReload"); retry.IsEnabled = !_settingsBusy; retry.HorizontalAlignment = HorizontalAlignment.Left;
            retry.Click += async (_, _) => await LoadDefaultsAsync(); failure.Children.Add(retry);
            card.Children.Add(Section(failure)); _defaults.Children.Add(AdminMonitorUi.Panel(card, new(22, 0, 22, 0), 22)); return;
        }
        FrameworkElement Setting(string label, string help, FrameworkElement control, bool controlBelow)
        {
            var text = new StackPanel { Spacing = 4 }; var name = U.Text(label, 14); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; text.Children.Add(name); text.Children.Add(U.Text(help, 13, true));
            AutomationProperties.SetName(control, label); AutomationProperties.SetHelpText(control, help);
            var line = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            line.RowDefinitions.Add(new() { Height = GridLength.Auto }); line.RowDefinitions.Add(new() { Height = GridLength.Auto });
            line.Children.Add(text); control.VerticalAlignment = VerticalAlignment.Center;
            if (controlBelow) { Grid.SetRow(control, 1); Grid.SetColumnSpan(control, 2); control.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumnSpan(text, 2); }
            else { Grid.SetColumn(control, 1); control.HorizontalAlignment = HorizontalAlignment.Right; }
            line.Children.Add(control); return Section(line);
        }
        var mode = ReadDefault("defaults.ui.next_up_mode", "combined");
        var choice = Select("HomeSectionsNextUpMode", [("combined", T("settingsManifest.ui.next_up_mode.option.combined")), ("separate", T("settingsManifest.ui.next_up_mode.option.separate"))], mode,
            async value => { if (value != mode) await SaveDefaultAsync(DefaultKeys[0], JsonSerializer.Serialize(value)); });
        choice.IsEnabled = !_settingsBusy; if (!_phone) choice.Width = 240;
        Divider(); card.Children.Add(Setting(T("settingsManifest.ui.next_up_mode.label"), T("settingsManifest.ui.next_up_mode.description"), choice, _phone));
        var newFirst = ReadDefault("defaults.ui.next_up_new_releases_first", "false") == "true";
        var first = Toggle("HomeSectionsNewEpisodesFirst", newFirst, async value => { if (value != newFirst) await SaveDefaultAsync(DefaultKeys[1], value ? "true" : "false"); });
        first.IsEnabled = !_settingsBusy; first.MinWidth = 0;
        Divider(); card.Children.Add(Setting(T("settingsManifest.ui.next_up_new_releases_first.label"), T("settingsManifest.ui.next_up_new_releases_first.description"), first, false));
        if (_defaultsMessage != null) { Divider(); card.Children.Add(Section(U.Text(_defaultsMessage, 13), 12, 12)); }
        _defaults.Children.Add(AdminMonitorUi.Panel(card, new(22, 0, 22, 0), 22));
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
            if (ex is AdminSettingsConflictException) { _settingsReview = true; _defaultsMessage = AdminHomeSectionsText.T("native.homeSections.defaultsChanged"); }
            else if (ex is AdminSettingsOutcomeUnknownException) { _settingsReview = true; _defaultsMessage = AdminHomeSectionsText.T("native.homeSections.defaultsUnknown"); }
            else _defaultsMessage = ex is ApiException api ? AdminHomeSectionsController.RefusalText(api) : ex.Message;
        }
        finally { _settingsBusy = false; if (_active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner)) RenderDefaults(); }
    }
}
