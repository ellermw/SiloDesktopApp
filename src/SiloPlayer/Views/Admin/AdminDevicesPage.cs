using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Controls;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official fleet filters and per-account/device/profile descendants; canonical settings only.</summary>
public sealed partial class AdminDevicesPage : Page, IAdminPageSuspension
{
    private readonly AdminUserResourcesApi _api;
    private readonly AdminUserResourceHost _host;
    private AdminRouteRequest _route = new(AdminRoute.Devices);
    private IReadOnlyList<JsonElement> _fleet = [], _values = [];
    private IReadOnlySet<string> _anomalies = new HashSet<string>();
    private JsonElement _device;
    private string _search = "", _saved = "", _groupBy = "user", _profile = "";
    private bool _overridesOnly, _showAll = true;
    private int _revision;
    private long _detailRevision;
    private int _visibleDevices = 100;
    private readonly HashSet<string> _platforms = [], _ranges = [], _recency = [];
    private readonly StackPanel _rail = Stack(8), _list = Stack(8), _detail = Stack(16);
    private Grid? _panes;
    private SubtitleAppearanceDialog? _subtitleDialog;
    public AdminUserResourceController Session => _host.Session;
    public AdminDevicesPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled; Id(this, "AdminDevicesPage");
        _api = App.Services.GetRequiredService<AdminUserResourcesApi>();
        _host = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        SizeChanged += (_, _) => LayoutPanes();
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) _route = route; _profile = _route.Get("profile") ?? ""; }
    public void SuspendAdminPage() => _host.Suspend();
    private void Clear() { _detailRevision++; _subtitleDialog?.Hide(); _subtitleDialog = null; _fleet = _values = []; _anomalies = new HashSet<string>(); _device = default; _search = ""; _list.Children.Clear(); _detail.Children.Clear(); _rail.Children.Clear(); }
    private static string D(string key, params (string Name, object? Value)[] args) => T("web.admin.devices." + key, args);
    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        _host.Content.Children.Clear(); _host.Content.Children.Add(Text("Loading devices…"));
        var capabilities = await Session.ReadAsync("capabilities", owner => _api.DeviceCapabilitiesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        if (!capabilities.Flag("available")) { _host.Content.Children.Clear(); _host.Content.Children.Add(Text("Device administration is unavailable on this server.")); return; }
        var fleet = await Session.ReadAsync("fleet", owner => _api.ConfigurationAsync(owner, cursor => _api.ListDevicesAsync(owner, cursor)));
        if (fleet == null || !Session.IsCurrent(pageOwner)) return;
        _fleet = fleet; _anomalies = AdminDevicePresentation.Anomalies(fleet, DateTimeOffset.UtcNow); RenderFleet();
        if (_route.Segments.Count == 2) await LoadDetailAsync();
    }
    private void RenderFleet()
    {
        _host.Content.Children.Clear();
        var search = Input(D("search.placeholder"), "DeviceSearch", _search);
        search.TextChanged += (_, _) => { _search = search.Text; RenderList(); };
        _host.Content.Children.Add(LogsNativeUi.Header(D("title"), D("subtitle", ("shortcut", "Ctrl+K")), search));
        var pulse = Text($"{_fleet.Select(x => x.Text("user_id")).Distinct().Count()} users · {_fleet.Count} devices · {_fleet.Sum(x => x.Number("profile_count"))} profiles · {_fleet.Sum(x => x.Number("override_count"))} overrides · {_anomalies.Count} anomalies", 12, true);
        _host.Content.Children.Add(pulse);
        _host.Content.Children.Add(Select(D("groupBy.label"), [("user", D("groupBy.user")), ("platform", D("groupBy.platform")), ("activity", D("groupBy.activity"))], _groupBy, value => { _groupBy = value; RenderList(); }));
        _host.Content.Children.Add(Toggle(D("scope.withOverrides"), _overridesOnly, value => { _overridesOnly = value; _showAll = !value; RenderFilters(); RenderList(); if (_device.ValueKind == JsonValueKind.Object) RenderDetail(); }));
        if (_rail.Parent is Panel rp) rp.Children.Remove(_rail); if (_list.Parent is Panel lp) lp.Children.Remove(_list); if (_detail.Parent is Panel dp) dp.Children.Remove(_detail);
        _panes = new Grid { ColumnSpacing = 16, RowSpacing = 16 }; _panes.Children.Add(_rail); _panes.Children.Add(_list); _panes.Children.Add(_detail);
        _host.Content.Children.Add(_panes); RenderFilters(); RenderList(); LayoutPanes();
        if (_route.Segments.Count == 0) { _detail.Children.Clear(); _detail.Children.Add(Panel(Section(D("detail.emptyTitle"), Text(D("detail.emptyBody"), 12, true)))); }
    }
    private void LayoutPanes()
    {
        if (_panes == null) return; _panes.ColumnDefinitions.Clear(); _panes.RowDefinitions.Clear();
        var wide = ActualWidth >= 1050;
        if (wide)
        {
            _panes.ColumnDefinitions.Add(new() { Width = new(200) }); _panes.ColumnDefinitions.Add(new() { Width = new(300) }); _panes.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            Grid.SetRow(_rail, 0); Grid.SetRow(_list, 0); Grid.SetRow(_detail, 0); Grid.SetColumn(_rail, 0); Grid.SetColumn(_list, 1); Grid.SetColumn(_detail, 2);
        }
        else
        {
            for (var i = 0; i < 3; i++) _panes.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(_rail, 0); Grid.SetColumn(_list, 0); Grid.SetColumn(_detail, 0); Grid.SetRow(_rail, 0); Grid.SetRow(_list, 1); Grid.SetRow(_detail, 2);
        }
    }
    private void RenderFilters()
    {
        _rail.Children.Clear();
        _rail.Children.Add(Select(D("filters.savedViews"), [("", D("scope.allDevices")), ("anomalies", D("filters.anomalies")), ("recent", D("filters.recent")), ("hdr", D("filters.hdr")), ("subtitle", D("filters.heavyCustomizers")), ("dormant", D("filters.dormant"))], _saved, value => { _saved = value; RenderList(); }));
        AddFacet("Platform", _platforms, [("tv", "TV"), ("mobile", "Mobile"), ("tablet", "Tablet"), ("desktop", "Desktop"), ("unknown", "Unknown")], x => AdminDevicePresentation.Platform(x.Text("device_platform")));
        AddFacet("Overrides", _ranges, [("none", "None"), ("1-2", "1–2"), ("3-5", "3–5"), ("6+", "6+")], AdminDevicePresentation.OverrideRange);
        AddFacet("Last updated", _recency, [("<24h", D("filters.recency24h")), ("<7d", D("filters.recency7d")), ("<30d", D("filters.recency30d")), (">30d", D("filters.recencyOlder"))], x => AdminDevicePresentation.Recency(x, DateTimeOffset.UtcNow));
        _rail.Children.Add(Button(D("list.clearAll"), "DeviceClearFilters", () => { _search = _saved = ""; _platforms.Clear(); _ranges.Clear(); _recency.Clear(); _overridesOnly = false; RenderFleet(); }));
    }
    private void AddFacet(string title, HashSet<string> values, (string Value, string Label)[] options, Func<JsonElement, string> bucket)
    {
        _rail.Children.Add(Text(title, 12, true));
        foreach (var option in options)
        {
            var count = _fleet.Count(x => (!_overridesOnly || x.Number("override_count") > 0) && bucket(x) == option.Value);
            _rail.Children.Add(Toggle(option.Label + " · " + count, values.Contains(option.Value), value => { if (value) values.Add(option.Value); else values.Remove(option.Value); RenderList(); }));
        }
    }
    private void RenderList()
    {
        _list.Children.Clear(); var now = DateTimeOffset.UtcNow;
        var rows = _fleet.Where(row => (!_overridesOnly || row.Number("override_count") > 0) && (_saved == "anomalies" ? _anomalies.Contains(AdminDevicePresentation.DeviceKey(row)) : AdminDevicePresentation.SavedView(row, _saved, _fleet, now)) &&
            (_platforms.Count == 0 || _platforms.Contains(AdminDevicePresentation.Platform(row.Text("device_platform")))) && (_ranges.Count == 0 || _ranges.Contains(AdminDevicePresentation.OverrideRange(row))) &&
            (_recency.Count == 0 || _recency.Contains(AdminDevicePresentation.Recency(row, now))) &&
            string.Join(" ", row.Text("device_name"), row.Text("device_id"), row.Text("username"), row.Text("email"), row.Text("device_platform"), string.Join(" ", row.Rows("profiles").Select(x => x.Text("profile_name") + " " + x.Text("profile_id")))).Contains(_search.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _list.Children.Add(Text(D("list.countOfTotal", ("count", rows.Length), ("count_text", rows.Length), ("total", _fleet.Count)), 12, true));
        if (rows.Length == 0) { _list.Children.Add(Text(D(_fleet.Count == 0 ? "empty.noDevicesTitle" : "empty.noMatchTitle"))); _list.Children.Add(Text(D(_fleet.Count == 0 ? "empty.noDevicesBody" : "empty.noMatchBody"), 12, true)); return; }
        foreach (var group in rows.Take(_visibleDevices).GroupBy(row => _groupBy == "platform" ? AdminDevicePresentation.Platform(row.Text("device_platform")) : _groupBy == "activity" ? AdminDevicePresentation.Recency(row, now) : row.Text("username")))
        {
            _list.Children.Add(Text(group.Key, 14));
            foreach (var row in group)
            {
                var user = row.Text("user_id"); var device = row.Text("device_id");
                var label = row.Text("device_name", D("unnamedDevice")) + " · " + row.Text("device_platform") + "\n" + row.Number("override_count") + " overrides · " + row.Number("profile_count") + " profiles";
                var button = Button(label, "Device_" + user + "_" + device, () => AdminShellPage.Open(AdminRouteRequest.Device(user, device)));
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; _list.Children.Add(button);
            }
        }
        if (rows.Length > _visibleDevices) _list.Children.Add(Button("Show more devices", "DeviceMoreRows", () => { _visibleDevices += 100; RenderList(); }));
    }
    private async Task LoadDetailAsync()
    {
        var pageOwner = Session.Owner;
        var user = _route.Segment(0)!; var device = _route.Segment(1)!;
        _detail.Children.Clear(); _detail.Children.Add(Text("Loading device…"));
        var result = await Session.ReadAsync("device", owner => _api.GetDeviceAsync(owner, user, device));
        if (!Session.IsCurrent(pageOwner)) return;
        if (result.ValueKind != JsonValueKind.Object) { _detail.Children.Clear(); _detail.Children.Add(Text(D("detail.notFoundTitle"))); return; }
        _device = result;
        var values = await Session.ReadAsync("values", owner => _api.GetDeviceValuesAsync(owner, user));
        if (!Session.IsCurrent(pageOwner)) return;
        if (values == null) { _detail.Children.Clear(); _detail.Children.Add(Text(D("detail.loadErrorTitle"))); _detail.Children.Add(Text(D("detail.loadErrorBody"))); return; }
        _values = values.Items; _revision = values.Revision; RenderDetail();
    }
    private IReadOnlyList<JsonElement> Overrides => _values.Where(x => x.Text("scope") == "profile_device" && x.Text("device_id") == _device.Text("device_id")).ToArray();
    private void RenderDetail()
    {
        _detailRevision++;
        _detail.Children.Clear(); if (_device.ValueKind != JsonValueKind.Object) return;
        _detail.Children.Add(Text(_device.Text("device_name", D("unnamedDevice")), 20));
        _detail.Children.Add(Text(_device.Text("device_id") + " · " + _device.Text("device_platform") + " · " + _device.Text("username"), 12, true));
        var accountLink = Button(D("detail.openUser"), "DeviceOpenUser", () => AdminShellPage.Open(AdminRouteRequest.UserDetail(_device.Text("user_id"))));
        accountLink.IsEnabled = AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(_device.Text("user_id"))); _detail.Children.Add(accountLink);
        RenderConnections();
        var profiles = _device.Rows("profiles").Select(x => (Id: x.Text("profile_id", "unknown-profile"), Name: x.Text("profile_name", x.Text("profile_id"))))
            .Concat(Overrides.Select(x => (Id: x.Text("profile_id", "unknown-profile"), Name: x.Text("profile_name", x.Text("profile_id")))))
            .DistinctBy(x => x.Id).ToArray();
        _detail.Children.Add(Text($"{Overrides.Count} overrides · {profiles.Length} profiles", 12, true));
        if (_anomalies.Contains(AdminDevicePresentation.DeviceKey(_device))) _detail.Children.Add(Text(D("detail.anomalyBadge") + " · " + _device.Number("override_count") + " overrides · " + (AdminDevicePresentation.Age(_device, DateTimeOffset.UtcNow)?.ToString("N0") ?? "Unknown") + " days since update", 12));
        if (profiles.Length == 0) { _detail.Children.Add(Text(D("detail.noProfilesTitle"))); _detail.Children.Add(Text(D("detail.noProfilesBody"), 12, true)); return; }
        if (!profiles.Any(x => x.Id == _profile)) _profile = profiles[0].Id;
        _detail.Children.Add(Select("Profile", profiles.Select(x => (x.Id, x.Name)), _profile, value => { _profile = value; _route = _route.WithQuery("profile", value); AdminShellPage.TryReplace(this, _route); RenderDetail(); }));
        _detail.Children.Add(Toggle(D("settingsScope.allSettings"), _showAll || Overrides.Count == 0, value => { _showAll = value; RenderDetail(); }, Overrides.Count != 0));
        var currentProfile = _profile;
        var currentOverrides = Overrides.Where(x => x.Text("profile_id", "unknown-profile") == currentProfile).ToArray();
        var reset = DetailButton(D("resetProfile.confirm"), "DeviceResetProfile", currentProfile, () => _ = ResetProfileAsync(currentProfile, currentOverrides));
        reset.IsEnabled = currentOverrides.Length > 0 && currentProfile != "unknown-profile" && _device.Text("device_id").Length <= 128 && !Session.NeedsReview; _detail.Children.Add(reset);
        var definitions = AdminDevicePresentation.Definitions(_revision, _device.Text("device_platform"), currentOverrides.Select(x => x.Text("key"))).ToArray();
        var shown = definitions.Where(x => _showAll || Overrides.Count == 0 || currentOverrides.Any(row => row.Text("key") == x.Key)).ToArray();
        foreach (var group in shown.GroupBy(x => x.Group))
        {
            var section = Section(group.Key);
            foreach (var definition in group)
            {
                var existing = currentOverrides.FirstOrDefault(x => x.Text("key") == definition.Key);
                var inherited = AdminDevicePresentation.Inherited(definition, currentProfile, _values);
                var value = existing.Member("value") ?? inherited.Value;
                var row = Section(definition.Label, Text(definition.Description, 12, true), Text(existing.ValueKind == JsonValueKind.Object ? "Device override" : inherited.Source, 12, true));
                var canWrite = currentProfile != "unknown-profile" && _device.Text("device_id").Length <= 128 && !Session.NeedsReview;
                row.Children.Add(SettingControl(definition, currentProfile, value, canWrite));
                if (existing.ValueKind == JsonValueKind.Object)
                {
                    var key = definition.Key;
                    var resetOne = DetailButton(T("web.admin.common.deviceOverrides.reset"), "DeviceReset_" + key, currentProfile, () => _ = ResetOneAsync(currentProfile, key)); resetOne.IsEnabled = canWrite; row.Children.Add(resetOne);
                }
                section.Children.Add(Panel(row));
            }
            _detail.Children.Add(section);
        }
        // Unknown stored keys stay visible/resettable; never silently discard a newer server's override.
        foreach (var stored in currentOverrides.Where(x => definitions.All(d => d.Key != x.Text("key"))))
        {
            var key = stored.Text("key"); var row = Section(key, Text("Stored device override", 12, true));
            row.Children.Add(DetailButton("Edit JSON", "DeviceJson_" + key, currentProfile, () => _ = EditJsonAsync(currentProfile, key, stored.Member("value") ?? JsonSerializer.SerializeToElement((object?)null))));
            row.Children.Add(DetailButton(T("web.admin.common.deviceOverrides.reset"), "DeviceReset_" + key, currentProfile, () => _ = ResetOneAsync(currentProfile, key))); _detail.Children.Add(Panel(row));
        }
    }
    private UIElement SettingControl(DeviceSettingDefinition definition, string profile, JsonElement value, bool enabled)
    {
        var key = definition.Key; var owner = Session.Owner; var revision = _detailRevision;
        if (definition.Type == "boolean") return Toggle(definition.Label, value.ValueKind == JsonValueKind.True, next => { if (TargetCurrent(owner, revision, profile)) _ = SaveSettingAsync(profile, key, JsonSerializer.SerializeToElement(next)); }, enabled);
        if (definition.Options.Length > 0)
        {
            var select = Select(definition.Label, definition.Options.Select(x => (x.Value, x.Label)), DeviceSettingDefinition.Scalar(value), next =>
            {
                if (!TargetCurrent(owner, revision, profile)) return;
                var typed = definition.Type is "integer" or "number" ? JsonDocument.Parse(next).RootElement.Clone() : JsonSerializer.SerializeToElement(next);
                _ = SaveSettingAsync(profile, key, typed);
            }); select.IsEnabled = enabled; return select;
        }
        var edit = DetailButton(key == "playback.subtitle_appearance" ? T("web.admin.common.deviceOverrides.edit.customize") : definition.Type == "json" ? "Edit JSON" : "Edit", "DeviceEdit_" + key, profile,
            () => _ = key == "playback.subtitle_appearance" ? EditSubtitlesAsync(profile, value) : EditValueAsync(definition, profile, value));
        edit.IsEnabled = enabled; return edit;
    }
    private async Task EditValueAsync(DeviceSettingDefinition definition, string profile, JsonElement value)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        if (definition.Type is "json" or "object" or "array") { await EditJsonAsync(profile, definition.Key, value); return; }
        var input = Input(definition.Label, "DeviceSettingValue", DeviceSettingDefinition.Scalar(value));
        JsonElement typed = default;
        bool Valid()
        {
            if (definition.Type is "integer" or "number")
            {
                if (!double.TryParse(input.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < definition.Minimum || n > definition.Maximum || definition.Type == "integer" && n != Math.Truncate(n)) return false;
                typed = JsonSerializer.SerializeToElement(n); return true;
            }
            typed = input.Text.Length == 0 && definition.Schema.Flag("nullable") ? JsonSerializer.SerializeToElement((object?)null) : JsonSerializer.SerializeToElement(input.Text); return true;
        }
        if (await _host.DialogAsync(definition.Label, input, T("common.save"), Valid) && TargetCurrent(owner, revision, profile)) await SaveSettingAsync(profile, definition.Key, typed);
    }
    private async Task EditJsonAsync(string profile, string key, JsonElement value)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        var input = Input(D("jsonEditor.help"), "DeviceJsonValue", DeviceSettingDefinition.Scalar(value)); input.AcceptsReturn = true; input.MinHeight = 260; input.MinWidth = 320;
        JsonElement typed = default;
        bool Valid()
        {
            if (input.Text.Length > 64 * 1024) return false;
            if (AdminDevicePresentation.Definition(key)?.Type != "object") { typed = JsonSerializer.SerializeToElement(input.Text); return true; }
            try { using var parsed = JsonDocument.Parse(input.Text); typed = parsed.RootElement.Clone(); return true; } catch (JsonException) { return false; }
        }
        if (await _host.DialogAsync(key, input, D("jsonEditor.save"), Valid) && TargetCurrent(owner, revision, profile)) await SaveSettingAsync(profile, key, typed);
    }
    private async Task EditSubtitlesAsync(string profile, JsonElement value)
    {
        var owner = Session.Owner; var revision = _detailRevision; var user = _device.Text("user_id"); var device = _device.Text("device_id");
        if (_subtitleDialog != null) return;
        var dialog = new SubtitleAppearanceDialog { XamlRoot = XamlRoot, InitialValue = value.GetRawText(), ApplyToLocalPlayer = false,
            CanReset = Overrides.Any(x => x.Text("profile_id") == profile && x.Text("key") == "playback.subtitle_appearance") };
        dialog.SaveOverrideAsync = async raw =>
        {
            if (!TargetCurrent(owner, revision, profile)) throw new MonitorAuthorityRetiredException();
            if (!await Session.WriteAsync(lease => _api.SetDeviceSettingAsync(lease, user, profile, device, "playback.subtitle_appearance", JsonDocument.Parse(raw).RootElement.Clone()))) throw new InvalidOperationException("The override could not be saved. Reload and review.");
        };
        dialog.ResetOverrideAsync = async () =>
        {
            if (!TargetCurrent(owner, revision, profile)) throw new MonitorAuthorityRetiredException();
            if (!await Session.WriteAsync(lease => _api.DeleteDeviceSettingAsync(lease, user, profile, device, "playback.subtitle_appearance"))) throw new InvalidOperationException("The override could not be reset.");
        };
        void Retire() => DispatcherQueue.TryEnqueue(() => dialog.Hide());
        Session.Retired += Retire; _subtitleDialog = dialog;
        try { await dialog.ShowAsync(); } finally { Session.Retired -= Retire; dialog.InitialValue = null; if (ReferenceEquals(_subtitleDialog, dialog)) _subtitleDialog = null; }
        if (Session.IsCurrent(owner)) await LoadDetailAsync();
    }
    private async Task SaveSettingAsync(string profile, string key, JsonElement value)
    {
        if (profile != _profile || profile == "unknown-profile") return;
        var user = _device.Text("user_id"); var device = _device.Text("device_id");
        if (await Session.WriteAsync(lease => _api.SetDeviceSettingAsync(lease, user, profile, device, key, value))) await LoadDetailAsync();
    }
    private async Task ResetOneAsync(string profile, string key)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        var user = _device.Text("user_id"); var device = _device.Text("device_id");
        if (await _host.ConfirmAsync(D("resetSetting.title"), D("resetSetting.description"), D("resetSetting.confirm")) && TargetCurrent(owner, revision, profile) &&
            await Session.WriteAsync(lease => _api.DeleteDeviceSettingAsync(lease, user, profile, device, key))) await LoadDetailAsync();
    }
    private async Task ResetProfileAsync(string profile, IReadOnlyList<JsonElement> overrides)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        var user = _device.Text("user_id"); var device = _device.Text("device_id");
        var profileName = _device.Rows("profiles").FirstOrDefault(row => row.Text("profile_id") == profile).Text("profile_name", profile);
        if (!await _host.ConfirmAsync(D("resetProfile.title", ("name", profileName)), D("resetProfile.description"), D("resetProfile.confirm")) || !TargetCurrent(owner, revision, profile)) return;
        var completed = 0;
        var success = await Session.WriteAsync(async lease =>
        {
            var result = await _api.ResetDeviceProfileAsync(lease, user, profile, device, overrides.Select(x => x.Text("key")).ToArray());
            completed = result.Completed; if (result.OutcomeUnknown) throw new AdminResourceOutcomeUnknownException();
            if (result.FailureStatus is { } status) throw new ApiException("reset_stopped", "The reset stopped. Reload to review remaining overrides.", status);
        });
        if (success) await LoadDetailAsync();
        else _detail.Children.Add(Text($"Reset stopped after {completed} of {overrides.Count}. Reload and review the remaining overrides before trying again."));
    }
    private void RenderConnections()
    {
        var connections = _device.Rows("connections");
        if (connections.Count == 0) return;
        _detail.Children.Add(Text(C("title"), 16));
        foreach (var connection in connections.Take(100))
        {
            var name = connection.Text("connection");
            var title = name == "local" ? C("local") : name == "remote" ? C("remote") : name.StartsWith("provider:", StringComparison.Ordinal)
                ? C("provider", ("provider", name[9..])) : "Other connection";
            var panel = Section(title);
            if (connection.Member("probe") is { ValueKind: JsonValueKind.Object } probe)
            {
                var transport = probe.Text("transport") switch { "wifi" => "wifi", "ethernet" => "ethernet", "cellular" => "cellular", _ => "otherNetwork" };
                panel.Children.Add(Text(C("probe", ("lower_bound", probe.Flag("lower_bound") ? "yes" : "no"), ("rate", Rate(probe.Number("kbps"))), ("transport", C("transport." + transport)), ("measured", Relative(probe.Text("measured_at")))), 12, true));
            }
            if (connection.Member("playback_sample") is { ValueKind: JsonValueKind.Object } sample)
                panel.Children.Add(Text(C("playbackSample", ("rate", Rate(sample.Number("kbps"))), ("seen", Relative(sample.Text("measured_at")))), 12, true));
            if (connection.Member("learned_limit") is { ValueKind: JsonValueKind.Object } limit)
            {
                panel.Children.Add(Text(C("learnedLimit", ("rate", Rate(limit.Number("kbps"))), ("until", Date(limit.Text("expires_at")))), 12));
                var user = _device.Text("user_id"); var device = _device.Text("device_id"); var id = connection.Text("id");
                panel.Children.Add(DetailButton(C("clearLimit"), "DeviceResetLimit_" + id, null, () => _ = ResetLimitAsync(user, device, id)));
            }
            if (connection.Member("plan_summary") is { ValueKind: JsonValueKind.Object } plan && plan.Member("diagnostics") is { ValueKind: JsonValueKind.Object } diagnostics)
            {
                int.TryParse(diagnostics.Text("stall_count"), out var stalls);
                double.TryParse(diagnostics.Text("stall_total_ms"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var milliseconds);
                var seconds = double.IsFinite(milliseconds) ? Math.Max(0, Math.Round(milliseconds / 1000)) : 0;
                var action = diagnostics.Text("buffering_action") switch { "ignored" => "ignored", "lowered" => "lowered", "restored" => "restored", "accepted" => "accepted", "kept" => "kept", "timed_out" => "timedOut", "cant_lower" => "cantLower", _ => "none" };
                var summary = C("stalls", ("count", stalls), ("count_text", stalls)) + " · " + C("bufferingSeconds", ("count", seconds), ("count_text", seconds)) + " · " + C("bufferingAction." + action) + " · " + Relative(plan.Text("reported_at"));
                panel.Children.Add(Text(C("latestPlayback", ("summary", summary)), 12, true));
            }
            _detail.Children.Add(Panel(panel));
        }
        if (connections.Count > 100) _detail.Children.Add(Text("Showing the first 100 connection observations.", 12, true));
    }
    private static string C(string key, params (string Name, object? Value)[] args) => T("web.admin.common.deviceConnections." + key, args);
    private static string Rate(int kbps) => kbps >= 1000 ? (kbps / 1000d).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " Mbps" : kbps + " kbps";
    private static string Date(string value) => DateTimeOffset.TryParse(value, out var at) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDate(at) : "—";
    private static string Relative(string value) => DateTimeOffset.TryParse(value, out var at) && DateTimeOffset.UtcNow - at > TimeSpan.FromDays(30)
        ? Date(value) : AdminPlaybackAuditText.Relative(value, DateTimeOffset.UtcNow) ?? "—";
    private async Task ResetLimitAsync(string user, string device, string connection)
    {
        if (await Session.WriteAsync(lease => _api.ResetLearnedLimitAsync(lease, user, device, connection))) await LoadDetailAsync();
    }
    private bool TargetCurrent(MonitorAuthorityLease? owner, long revision, string? profile) => Session.IsCurrent(owner) && revision == _detailRevision && (profile == null || profile == _profile && profile != "unknown-profile" && _device.Text("device_id").Length <= 128);
    private Button DetailButton(string label, string id, string? profile, Action action)
    {
        var owner = Session.Owner; var revision = _detailRevision;
        return Button(label, id, () => { if (TargetCurrent(owner, revision, profile) && !Session.Busy && !Session.NeedsReview) action(); });
    }
}
