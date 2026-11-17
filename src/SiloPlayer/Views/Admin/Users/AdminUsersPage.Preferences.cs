using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private async Task ReadPreferencesAsync(long generation, string root)
    {
        await DocumentAsync("settings-capabilities", "/api/v2/settings/contract/capabilities", generation);
        if (!AdminUsersRules.Available(Document("settings-capabilities"))) return;
        await RowsAsync("settings", root + "/settings/values", generation, limit: 200);
        await RowsAsync("profiles", root + "/profiles", generation, true);
        if (Feature("account_devices")) await RowsAsync("devices", root + "/devices", generation, true);
        await DocumentAsync("manifest", "/api/v2/settings/contract", generation);
        RequireGeneration(generation);
        var levels = PreferenceLevels();
        _settingIdentity = levels.FirstOrDefault(identity => AdminUserPreferences.LevelId(identity) == _location.Get("level")) ??
            levels.FirstOrDefault(identity => identity.Scope == "profile") ?? levels.FirstOrDefault();
        if (_settingIdentity?.Scope == "profile" && Feature("profile_sections"))
        {
            var path = root + "/profiles/" + AdminUsersApi.Segment(_settingIdentity.ProfileId!) + "/sections";
            await RowsAsync("home-settings", path + "/settings?scope=home", generation, true);
            await RowsAsync("home-overrides", path + "?scope=home", generation, true);
        }
    }
    private List<AdminUserSettingIdentity> PreferenceLevels()
    {
        var levels = new List<AdminUserSettingIdentity> { new("account") };
        foreach (var profile in Rows("profiles"))
        {
            var profileId = profile.Text("id"); levels.Add(new("profile", profileId));
            foreach (var family in AdminUserSettingIdentity.Families) levels.Add(new("profile_client", profileId, ClientFamily: family));
            foreach (var device in Rows("devices").Where(device => (device["profiles"] as JsonArray)?.OfType<JsonObject>().Any(row => row.Text("profile_id") == profileId) == true))
                levels.Add(new("profile_device", profileId, DeviceId: device.Text("device_id")));
        }
        foreach (var row in Rows("settings"))
        {
            var identity = AdminUserSettingIdentity.FromRow(row);
            if (!levels.Contains(identity)) levels.Add(identity);
        }
        if (_settingIdentity != null && (_settingIdentity.Scope == "account" || Rows("profiles").Any(row => row.Text("id") == _settingIdentity.ProfileId)) && !levels.Contains(_settingIdentity)) levels.Add(_settingIdentity);
        var scopes = Document("settings-capabilities")?["scopes"] as JsonArray;
        return levels.Where(identity => scopes?.Any(scope => scope?.GetValue<string>() == identity.Scope) == true).ToList();
    }
    private void RenderPreferences()
    {
        if (!AdminUsersRules.Available(Document("settings-capabilities"))) { _body.Children.Add(Text("Preferences are unavailable for this authority/server.")); return; }
        var levels = PreferenceLevels();
        if (_settingIdentity == null) { _body.Children.Add(Text("No supported storage identity.")); return; }
        var picker = Choice("Storage level — exactly one identity", levels.Select(identity =>
        {
            var name = Rows("profiles").FirstOrDefault(row => row.Text("id") == identity.ProfileId)?.Text("name");
            return (AdminUserPreferences.LevelId(identity), (name == null ? "" : name + " · ") + identity);
        }), AdminUserPreferences.LevelId(_settingIdentity));
        picker.SelectionChanged += (_, _) =>
        {
            var identity = levels.FirstOrDefault(level => AdminUserPreferences.LevelId(level) == Selected(picker));
            if (identity == null || identity == _settingIdentity) return;
            _settingIdentity = identity;
            Navigate(_review!.User.Text("id"), "preferences", AdminUserPreferences.LevelId(identity));
        };
        _body.Children.Add(picker);
        _body.Children.Add(Text("Client-family order: TV / mobile / tablet / desktop / web. Every edit/reset includes all displayed identity qualifiers. An explicit null value is distinct from removing the stored row."));
        if (Manage) _body.Children.Add(Action("Choose a new device/library/series level", NewPreferenceLevelAsync));
        var identity = _settingIdentity;
        var entries = Rows("settings").Where(row => AdminUserSettingIdentity.FromRow(row) == identity).ToList();
        var definitions = (Document("manifest")?["definitions"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var eligible = definitions.Where(definition => AdminUserPreferences.Supports(definition, identity)).ToList();
        if (Manage)
        {
            var addOptions = eligible.Where(definition => !entries.Any(row => row.Text("key") == definition.Text("key"))).ToList();
            var add = Choice("Add setting", addOptions.Select(definition => (definition.Text("key"), definition.Text("label", definition.Text("key")))));
            _body.Children.Add(add);
            _body.Children.Add(Action("Add selected setting", () => SettingAsync(identity, addOptions.First(definition => definition.Text("key") == Selected(add)), null), addOptions.Count > 0 && _rows.GetValueOrDefault("settings")?.Complete == true));
        }
        foreach (var category in eligible.GroupBy(definition => definition.Text("category")))
        {
            var content = Panel();
            foreach (var definition in category)
            {
                var row = entries.FirstOrDefault(item => item.Text("key") == definition.Text("key"));
                var line = Panel(4);
                line.Children.Add(Text(definition.Text("label", definition.Text("key")), 16));
                line.Children.Add(Text(definition.Text("description")));
                line.Children.Add(Text(row == null ? "Inherited · " + AdminUserPreferences.Inherited(definition, identity, Rows("settings")) : "Explicit: " + (row["value"]?.Display() ?? "null (stored)")));
                line.Children.Add(Text("After reset: " + AdminUserPreferences.Inherited(definition, identity, Rows("settings"))));
                if (row != null) line.Children.Add(Action("View JSON · " + definition.Text("key"), () => SettingJsonAsync(row)));
                if (Manage && row != null)
                {
                    line.Children.Add(Action("Edit · " + definition.Text("key"), () => SettingAsync(identity, definition, row)));
                    line.Children.Add(Action("Reset only this identity", () => ResetSettingAsync(identity, row)));
                }
                content.Children.Add(line);
            }
            _body.Children.Add(Card(Label(category.Key), content));
        }
        foreach (var row in entries.Where(row => !eligible.Any(definition => definition.Text("key") == row.Text("key"))))
        {
            var unknown = Panel(); unknown.Children.Add(Text(row.Text("key") + ": " + (row["value"]?.Display() ?? "null (stored)")));
            unknown.Children.Add(Action("View stored JSON", () => SettingJsonAsync(row)));
            if (Manage) unknown.Children.Add(Action("Reset exact stored identity", () => ResetSettingAsync(identity, row)));
            _body.Children.Add(Card("Stored setting not editable in this manifest", unknown));
        }
        if (_rows.GetValueOrDefault("settings") is { Complete: false } partial)
        {
            _body.Children.Add(Text("Partial settings: inherited labels may omit unloaded overrides. Add/edit/reset remains blocked until a complete bounded baseline is loaded."));
            _body.Children.Add(Action("Load next settings page", () => MoreAsync("settings"), partial.Items.Count < AdminUsersApi.MaxRows));
        }
        if (identity.Scope == "profile" && Feature("profile_sections")) RenderHomeSections(identity);
    }
    private async Task NewPreferenceLevelAsync()
    {
        var profile = Choice("Account profile", Rows("profiles").Select(row => (row.Text("id"), row.Text("name"))), _settingIdentity?.ProfileId);
        var scope = Choice("Storage identity", new[] { "profile_device", "profile_client", "profile_library", "profile_series" }.Select(value => (value, Label(value))));
        var qualifier = new TextBox { Header = "Exact device/library/series ID, or client family (tv/mobile/tablet/desktop/web)", MaxLength = 256 };
        var error = Text(""); var panel = Panel(); panel.Children.Add(profile); panel.Children.Add(scope); panel.Children.Add(qualifier); panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Choose an exact preference identity", Content = panel, PrimaryButtonText = "Choose", CloseButtonText = "Cancel" };
        AdminUserSettingIdentity? selected = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var value = qualifier.Text.Trim();
                selected = Selected(scope) switch
                {
                    "profile_device" => new("profile_device", Selected(profile), DeviceId: value),
                    "profile_client" => new("profile_client", Selected(profile), ClientFamily: value),
                    "profile_library" => new("profile_library", Selected(profile), LibraryId: value),
                    _ => new("profile_series", Selected(profile), SeriesId: value)
                };
                selected.Validate();
            }
            catch (ArgumentException exception) { error.Text = exception.Message; args.Cancel = true; }
        };
        _dialog = dialog; var generation = _generation;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && selected != null && _workspace.IsCurrent(generation))
            { _settingIdentity = selected; Navigate(_review!.User.Text("id"), "preferences", AdminUserPreferences.LevelId(selected)); }
        }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    private async Task<List<AdminUsersReadback>> SettingReadbacksAsync(long generation)
    {
        if (_rows.GetValueOrDefault("settings") is not { Complete: true } state) throw new InvalidOperationException("Load a complete bounded settings snapshot before editing.");
        var root = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/settings/values";
        var result = new List<AdminUsersReadback>();
        foreach (var baseline in state.Baselines)
        {
            var doc = await ReadResourceAsync(baseline.Path, generation);
            if (!JsonNode.DeepEquals(doc.Body, baseline.Body)) throw new AdminUsersReviewRequiredException();
            result.Add(new(baseline.Path, doc.Body!));
        }
        return result;
    }
    private async Task SettingAsync(AdminUserSettingIdentity identity, JsonObject definition, JsonObject? row)
    {
        var generation = _generation;
        var baselines = await SettingReadbacksAsync(generation);
        var valueDefinition = AdminUserPreferences.ValueDefinition(definition);
        var schema = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("value"), ["properties"] = new JsonObject { ["value"] = valueDefinition } };
        var initial = new JsonObject { ["value"] = (row == null ? definition["default_value"] : row["value"])?.DeepClone() };
        var form = new AdminUsersForm(schema, initial, true);
        var review = await FreshReviewAsync(generation);
        var result = await MutationDialogAsync("Save " + definition.Text("label", definition.Text("key")) + " · " + identity, form.Content, review,
            () => new(AdminUsersOperation.SetSetting, form.Read(), Identity: identity, SettingKey: definition.Text("key"), Readbacks: baselines), false, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }
    private async Task ResetSettingAsync(AdminUserSettingIdentity identity, JsonObject row)
    {
        var generation = _generation;
        var baselines = await SettingReadbacksAsync(generation);
        await ConfirmAsync("Reset " + row.Text("key"), "Remove only the stored row at " + identity + ". Other profiles/devices/libraries/series are untouched.",
            new(AdminUsersOperation.ResetSetting, Identity: identity, SettingKey: row.Text("key"), Readbacks: baselines), expectedGeneration: generation);
    }
    private async Task SettingJsonAsync(JsonObject row)
    {
        var content = new TextBox { Text = row.ToJsonString(new() { WriteIndented = true }), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 580 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Exact stored setting / identity", Content = content, CloseButtonText = "Close" };
        _dialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { content.Text = ""; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    private void RenderHomeSections(AdminUserSettingIdentity identity)
    {
        var panel = Panel();
        if (!_rows.ContainsKey("home-settings") || !_rows.ContainsKey("home-overrides")) { panel.Children.Add(Text("Home Sections not loaded.")); _body.Children.Add(Card("Profile Home Sections", panel)); return; }
        foreach (var row in Rows("home-settings").OrderBy(row => row.Count("position")))
        {
            panel.Children.Add(Text(row.Text("title", row.Text("default_title")) + " · " + (row.Flag("hidden") ? "Hidden" : "Shown")));
            if (!Manage) continue;
            var actions = Inline();
            actions.Children.Add(Action(row.Flag("hidden") ? "Show" : "Hide", () => ChangeHomeAsync(identity, row.Text("id"), !row.Flag("hidden"))));
            actions.Children.Add(Action("Move up", () => ChangeHomeAsync(identity, row.Text("id"), direction: -1)));
            actions.Children.Add(Action("Move down", () => ChangeHomeAsync(identity, row.Text("id"), direction: 1)));
            panel.Children.Add(actions);
        }
        if (Manage) panel.Children.Add(Action("Reset this profile Home layout", async () =>
        {
            var generation = _generation;
            var path = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(identity.ProfileId!) + "/sections?scope=home";
            var baseline = await ReadResourceAsync(path, generation);
            if (!JsonNode.DeepEquals(baseline.Body, Baselines("home-overrides").Single().Body)) throw new AdminUsersReviewRequiredException();
            await ConfirmAsync("Reset Home Sections", "Only this profile home-page override set is removed. Other profiles and library-page layouts are untouched.",
                new(AdminUsersOperation.ResetSections, ResourceId: identity.ProfileId, Readbacks: [new(path, baseline.Body!)]), expectedGeneration: generation);
        }));
        _body.Children.Add(Card("Profile Home Sections", panel));
    }
    private async Task ChangeHomeAsync(AdminUserSettingIdentity identity, string sectionId, bool? hidden = null, int direction = 0)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var root = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(identity.ProfileId!) + "/sections";
        var settings = await ReadResourceAsync(root + "/settings?scope=home", generation);
        var overrides = await ReadResourceAsync(root + "?scope=home", generation);
        if (!JsonNode.DeepEquals(settings.Body!.Items(), new JsonArray(Rows("home-settings").Select(row => (JsonNode)row.Copy()).ToArray())) ||
            !JsonNode.DeepEquals(overrides.Body!.Items(), new JsonArray(Rows("home-overrides").Select(row => (JsonNode)row.Copy()).ToArray()))) throw new AdminUsersReviewRequiredException();
        var body = new JsonObject { ["overrides"] = AdminUserHomeSections.Change(settings.Body!.Items(), overrides.Body!.Items(), sectionId, hidden, direction) };
        await ConfirmAsync("Save profile Home layout", "Review the exact profile and home-page change. Unchanged and removed overrides are retained.",
            new(AdminUsersOperation.SaveSections, body, identity.ProfileId, Readbacks: [new(root + "/settings?scope=home", settings.Body), new(root + "?scope=home", overrides.Body)]), expectedGeneration: generation);
    }
}
