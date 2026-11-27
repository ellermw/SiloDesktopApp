using SiloPlayer.Core.Localization;
using System.Text.Json.Nodes;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using static SiloPlayer.Views.Admin.AdminUsersUi;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private async Task ReadPreferencesAsync(long generation, string root)
    {
        await DocumentAsync("settings-capabilities", "/api/v2/settings/contract/capabilities", generation);
        if (!_workspace.IsCurrent(generation) || _loadErrors.ContainsKey("settings-capabilities") ||
            !AdminUsersRules.Available(Document("settings-capabilities"))) return;
        await Task.WhenAll(
            RowsAsync("settings", root + "/settings/values", generation, limit: 200),
            RowsAsync("profiles", root + "/profiles", generation, true),
            Feature("account_devices") ? RowsAsync("devices", root + "/devices", generation, true) : Task.CompletedTask,
            DocumentAsync("manifest", "/api/v2/settings/contract", generation));
        RequireGeneration(generation);
        var levels = PreferenceLevels();
        _settingIdentity = levels.FirstOrDefault(identity => AdminUserPreferences.LevelId(identity) == _location.Get("level")) ??
            levels.FirstOrDefault(identity => identity.Scope == "profile") ?? levels.FirstOrDefault();
        if (_settingIdentity?.Scope == "profile" && Feature("profile_sections"))
        {
            var path = root + "/profiles/" + AdminUsersApi.Segment(_settingIdentity.ProfileId!) + "/sections";
            await Task.WhenAll(RowsAsync("home-settings", path + "/settings?scope=home", generation, true),
                RowsAsync("home-overrides", path + "?scope=home", generation, true));
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
    // ── Rendering ─────────────────────────────────────────────────────────────────────────────────────────

    private string DeviceLevelName(string? deviceId) =>
        Rows("devices").FirstOrDefault(device => device.Text("device_id") == deviceId) is { } device ? device.Text("device_name", T("web.admin.users.detail.common.unnamedDevice")) : T("web.admin.users.detail.preferences.levels.deviceById", ("id", deviceId ?? ""));

    private string LevelName(AdminUserSettingIdentity identity) => identity.Scope switch
    {
        "account" => T("web.admin.users.detail.preferences.levels.accountWide"),
        "profile" => T("web.admin.users.detail.preferences.levels.allDevices"),
        "profile_client" => T("web.admin.users.detail.preferences.levels.clientApps", ("family", identity.ClientFamily ?? "web")),
        "profile_device" => DeviceLevelName(identity.DeviceId),
        "profile_library" => T("web.admin.users.detail.preferences.levels.libraryById", ("id", identity.LibraryId ?? "")),
        _ => T("web.admin.users.detail.preferences.levels.series", ("id", identity.SeriesId ?? "")),
    };

    private string LevelProfile(AdminUserSettingIdentity identity) =>
        identity.ProfileId == null ? "" : Rows("profiles").FirstOrDefault(row => row.Text("id") == identity.ProfileId)?.Text("name") is { Length: > 0 } name ? name : T("web.admin.users.detail.preferences.levels.unknownProfile");

    private string LevelGroup(AdminUserSettingIdentity identity) => identity.Scope == "account"
        ? T("web.admin.users.detail.preferences.levels.accountGroup") : T("web.admin.users.detail.preferences.levels.profileGroup", ("profile", LevelProfile(identity)));

    private int LevelCount(AdminUserSettingIdentity identity) => Rows("settings").Count(row => AdminUserSettingIdentity.FromRow(row) == identity);

    private static string SettingValue(JsonNode? value)
    {
        if (value == null) return T("admin.native.users.prefs.stored");
        if (value is JsonValue scalar && scalar.TryGetValue<bool>(out var flag)) return flag ? "On" : "Off";
        var text = value.Display();
        return text.Length > 80 ? text[..79] + "…" : text;
    }

    private FrameworkElement PreferencesTab()
    {
        if (!AdminUsersRules.Available(Document("settings-capabilities"))) return Message(T("admin.native.users.prefs.unavailable"));
        if (!_rows.ContainsKey("settings")) return Message(T("web.admin.users.detail.preferences.tab.loadFailed"));
        var levels = PreferenceLevels();
        if (_settingIdentity == null) return Message(T("web.admin.users.detail.preferences.tab.noProfiles"));
        var identity = _settingIdentity;
        void Choose(string id)
        {
            var chosen = levels.FirstOrDefault(level => AdminUserPreferences.LevelId(level) == id);
            if (chosen == null || chosen == _settingIdentity) return;
            _settingIdentity = chosen;
            Navigate(_review!.User.Text("id"), "preferences", AdminUserPreferences.LevelId(chosen));
        }
        var main = new StackPanel { Spacing = 16 };
        main.Children.Add(LevelCard(identity));
        if (_rows.GetValueOrDefault("settings") is { Complete: false } partial)
        {
            var more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
            more.Children.Add(Text(T("admin.native.users.prefs.partial"), 13, true));
            more.Children.Add(Action(T("admin.native.users.prefs.loadMore"), () => MoreAsync("settings"), partial.Items.Count < AdminUsersApi.MaxRows));
            main.Children.Add(more);
        }
        foreach (var unknown in StoredOnlyCards(identity)) main.Children.Add(unknown);
        if (identity.Scope == "profile" && Feature("profile_sections")) main.Children.Add(HomeSectionsCard(identity));

        var wideNav = (XamlRoot?.Size.Width ?? 1024) >= 761;
        if (!wideNav)
        {
            var select = Select(T("web.admin.users.detail.preferences.tab.levelSelectAria"),
                levels.Select(level => (AdminUserPreferences.LevelId(level), LevelProfile(level).Length > 0
                    ? T("web.admin.users.detail.preferences.tab.levelOption", ("profile", LevelProfile(level)), ("level", LevelName(level)), ("count", LevelCount(level)))
                    : T("web.admin.users.detail.preferences.tab.levelOptionNoProfile", ("level", LevelName(level)), ("count", LevelCount(level))))),
                AdminUserPreferences.LevelId(identity), Choose);
            var stack = new StackPanel { Spacing = 16 };
            stack.Children.Add(select); stack.Children.Add(main);
            return stack;
        }
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 208, MaxWidth = 288 }); grid.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        var nav = new StackPanel { Spacing = 0 };
        foreach (var group in levels.GroupBy(LevelGroup))
        {
            var title = Text(group.Key.ToUpper(CultureInfo.CurrentCulture), 11, true); title.FontWeight = FontWeights.SemiBold; title.CharacterSpacing = 80; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
            title.Margin = new(12, 10, 12, 6); nav.Children.Add(title);
            foreach (var level in group)
            {
                var active = level == identity;
                var top = level.Scope is "account" or "profile";
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(Truncated(LevelName(level), 14, medium: active));
                var count = Text(LevelCount(level).ToString(CultureInfo.CurrentCulture), 12, true); count.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(count, 1); row.Children.Add(count);
                var button = new Button
                {
                    Content = row, Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    MinHeight = 32, MinWidth = 0, Padding = new(top ? 12 : 28, 6, 12, 6), CornerRadius = new(8), Background = active ? Brush("AccentBackgroundBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                };
                AutomationProperties.SetName(button, LevelName(level) + " (" + LevelCount(level) + ")");
                AutomationProperties.SetAutomationId(button, "UsersLevel_" + AdminUserPreferences.LevelId(level));
                var id = AdminUserPreferences.LevelId(level);
                Wire(button, () => { Choose(id); return Task.CompletedTask; });
                nav.Children.Add(button);
            }
        }
        var navCard = Surface(new Border { Child = nav, Padding = new(8) });
        navCard.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(navCard, T("web.admin.users.detail.preferences.tab.levelsNavAria"));
        grid.Children.Add(navCard); Grid.SetColumn(main, 1); grid.Children.Add(main);
        return grid;
    }

    private FrameworkElement LevelCard(AdminUserSettingIdentity identity)
    {
        var profile = LevelProfile(identity);
        var title = identity.Scope == "account" ? LevelName(identity) : T("web.admin.users.detail.preferences.level.title", ("level", LevelName(identity)), ("profile", profile));
        var description = identity.Scope switch
        {
            "account" => T("web.admin.users.detail.preferences.level.descAccount"),
            "profile" => T("web.admin.users.detail.preferences.level.descProfile", ("profile", profile)),
            "profile_client" => T("web.admin.users.detail.preferences.level.descClient", ("profile", profile)),
            "profile_device" => T("web.admin.users.detail.preferences.level.descDevice", ("profile", profile)),
            "profile_library" => T("web.admin.users.detail.preferences.level.descLibrary", ("profile", profile)),
            _ => T("web.admin.users.detail.preferences.level.descSeries", ("profile", profile)),
        };
        var entries = Rows("settings").Where(row => AdminUserSettingIdentity.FromRow(row) == identity).ToList();
        var definitions = (Document("manifest")?["definitions"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var eligible = definitions.Where(definition => AdminUserPreferences.Supports(definition, identity)).ToList();
        var rows = new List<Border>();
        foreach (var definition in eligible.Where(definition => entries.Any(row => row.Text("key") == definition.Text("key"))))
        {
            var row = entries.First(item => item.Text("key") == definition.Text("key"));
            rows.Add(SettingRow(identity, definition, row, definition.Text("label", definition.Text("key"))));
        }
        UIElement body;
        if (rows.Count > 0) body = RowStack(rows);
        else
        {
            var empty = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            var heading = Medium(identity.Scope == "account" ? T("web.admin.users.detail.preferences.empty.accountTitle") : T("web.admin.users.detail.preferences.empty.profileTitle", ("profile", profile)), 14);
            heading.TextAlignment = TextAlignment.Center; empty.Children.Add(heading);
            var detail = Text(identity.Scope == "account" ? T("web.admin.users.detail.preferences.empty.accountDetail") : identity.Scope == "profile" ? T("web.admin.users.detail.preferences.empty.profileDetail") : T("web.admin.users.detail.preferences.empty.detail", ("profile", profile)), 13, true);
            detail.TextAlignment = TextAlignment.Center; empty.Children.Add(detail);
            body = new Border { Child = empty, Padding = new(Gutter, 40, Gutter, 40) };
        }
        var footer = new StackPanel { Spacing = 12 };
        var tools = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (Manage)
        {
            var addOptions = eligible.Where(definition => !entries.Any(row => row.Text("key") == definition.Text("key"))).ToList();
            var add = Select(T("admin.native.users.prefs.addSetting"), addOptions.Select(definition => (definition.Text("key"), definition.Text("label", definition.Text("key")))), "", _ => { }, 240);
            tools.Children.Add(add);
            tools.Children.Add(Wire(ActionButton(T("web.admin.users.detail.preferences.picker.addOne"), "plus", "OutlineButtonStyle", 32, T("admin.native.users.prefs.addSelected")),
                () => SettingAsync(identity, addOptions.First(definition => definition.Text("key") == LogsNativeUi.Selected(add)), null), addOptions.Count > 0 && _rows.GetValueOrDefault("settings")?.Complete == true));
            tools.Children.Add(Wire(ActionButton(T("admin.native.users.prefs.chooseLevel")), NewPreferenceLevelAsync));
        }
        footer.Children.Add(tools);
        footer.Children.Add(Text(T("admin.native.users.prefs.note"), 12, true));
        return DetailCard(title, description, null, body, footer, "UsersPreferencesLevel");
    }

    private Border SettingRow(AdminUserSettingIdentity identity, JsonObject definition, JsonObject row, string label)
    {
        var key = definition.Text("key");
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 2 };
        var name = Medium(label, 14); name.IsTextSelectionEnabled = true; copy.Children.Add(name);
        if (definition.Text("description").Length > 0) copy.Children.Add(Text(definition.Text("description"), 12, true));
        copy.Children.Add(Text(T("admin.native.users.prefs.afterReset", ("value", AdminUserPreferences.Inherited(definition, identity, Rows("settings")))), 12, true));
        grid.Children.Add(copy);
        var side = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var value = Mono(SettingValue(row["value"]), 12); value.MaxWidth = 224; value.TextTrimming = TextTrimming.CharacterEllipsis; value.TextWrapping = TextWrapping.NoWrap; value.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(value, SettingValue(row["value"]));
        side.Children.Add(value);
        side.Children.Add(Wire(ActionButton(T("admin.native.users.prefs.viewJson"), null, "GhostButtonStyle", 28, "View JSON · " + key), () => SettingJsonAsync(row)));
        if (Manage)
        {
            side.Children.Add(Wire(ActionButton(T("admin.native.users.prefs.edit"), null, "OutlineButtonStyle", 28, "Edit · " + key), () => SettingAsync(identity, definition, row)));
            side.Children.Add(Wire(IconAction("trash-2", T("web.admin.users.detail.preferences.row.removeAria", ("setting", label)), 28, 14), () => ResetSettingAsync(identity, row)));
        }
        if (_phone)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(side, 1); Grid.SetColumnSpan(side, 2);
        }
        else Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        return new Border { Child = grid, Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5) };
    }

    /// <summary>Stored rows the loaded manifest cannot edit: shown with their exact stored value and the same two actions.</summary>
    private IEnumerable<FrameworkElement> StoredOnlyCards(AdminUserSettingIdentity identity)
    {
        var definitions = (Document("manifest")?["definitions"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var eligible = definitions.Where(definition => AdminUserPreferences.Supports(definition, identity)).ToList();
        var entries = Rows("settings").Where(row => AdminUserSettingIdentity.FromRow(row) == identity);
        var rows = new List<Border>();
        foreach (var row in entries.Where(row => !eligible.Any(definition => definition.Text("key") == row.Text("key"))))
        {
            var side = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var value = Mono(SettingValue(row["value"]), 12); value.MaxWidth = 224; value.TextTrimming = TextTrimming.CharacterEllipsis; value.TextWrapping = TextWrapping.NoWrap; side.Children.Add(value);
            side.Children.Add(Wire(ActionButton(T("admin.native.users.prefs.viewStored"), null, "GhostButtonStyle", 28), () => SettingJsonAsync(row)));
            if (Manage) side.Children.Add(Wire(IconAction("trash-2", T("web.admin.users.detail.preferences.row.removeAria", ("setting", row.Text("key"))), 28, 14), () => ResetSettingAsync(identity, row)));
            rows.Add(ListRow(null, row.Text("key"), T("web.admin.users.detail.preferences.row.notInVersion"), side, mono: true));
        }
        if (rows.Count > 0) yield return DetailCard(T("admin.native.users.prefs.unknownCard"), null, null, RowStack(rows), null, "UsersPreferencesStored");
    }

    private FrameworkElement HomeSectionsCard(AdminUserSettingIdentity identity)
    {
        var profile = LevelProfile(identity);
        var title = T("web.admin.users.detail.preferences.home.title", ("profile", profile));
        if (!_rows.ContainsKey("home-settings") || !_rows.ContainsKey("home-overrides"))
            return DetailCard(title, null, null, Empty(T("web.admin.users.detail.preferences.home.loadFailed", ("profile", profile))), null, "UsersPreferencesHome");
        var rows = new List<Border>();
        foreach (var row in Rows("home-settings").OrderBy(row => row.Count("position")))
        {
            var id = row.Text("id");
            var side = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            if (Manage)
            {
                side.Children.Add(Wire(IconAction(row.Flag("hidden") ? "eye" : "eye-off", row.Flag("hidden") ? T("admin.native.users.prefs.show") : T("admin.native.users.prefs.hide"), 28, 14), () => ChangeHomeAsync(identity, id, !row.Flag("hidden"))));
                side.Children.Add(Wire(IconAction("arrow-up", T("admin.native.users.prefs.moveUp"), 28, 14), () => ChangeHomeAsync(identity, id, direction: -1)));
                side.Children.Add(Wire(IconAction("arrow-down", T("admin.native.users.prefs.moveDown"), 28, 14), () => ChangeHomeAsync(identity, id, direction: 1)));
            }
            var stateBadge = row.Flag("hidden") ? AdminLibrariesUi.Badge(T("admin.native.users.prefs.hidden"), AdminLibrariesUi.BadgeTone.Secondary) : AdminLibrariesUi.Badge(T("admin.native.users.prefs.shown"), AdminLibrariesUi.BadgeTone.Outline);
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            trailing.Children.Add(stateBadge); trailing.Children.Add(side);
            rows.Add(ListRow(null, row.Text("title", row.Text("default_title")), null, trailing));
        }
        UIElement? footer = null;
        if (Manage)
            footer = Wire(ActionButton(T("web.admin.users.detail.preferences.home.reset"), "rotate-cw", "OutlineButtonStyle", 32, T("admin.native.users.prefs.resetHome")), async () =>
            {
                var generation = _generation;
                var path = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(identity.ProfileId!) + "/sections?scope=home";
                var baseline = await ReadResourceAsync(path, generation);
                if (!JsonNode.DeepEquals(baseline.Body, Baselines("home-overrides").Single().Body)) throw new AdminUsersReviewRequiredException();
                await ConfirmAsync(new(T("web.admin.users.detail.preferences.home.resetTitle", ("profile", profile)), T("web.admin.users.detail.preferences.home.resetDescription", ("profile", profile)), T("web.admin.users.detail.preferences.home.resetConfirm")),
                    new(AdminUsersOperation.ResetSections, ResourceId: identity.ProfileId, Readbacks: [new(path, baseline.Body!)]), expectedGeneration: generation);
            });
        return DetailCard(title, T("web.admin.users.detail.preferences.home.description", ("profile", profile)), null, rows.Count > 0 ? RowStack(rows) : Empty(T("admin.native.users.noRecords")), footer, "UsersPreferencesHome");
    }

    private async Task NewPreferenceLevelAsync()
    {
        var profile = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var row in Rows("profiles")) profile.Items.Add(new ComboBoxItem { Content = row.Text("name"), Tag = row.Text("id") });
        profile.SelectedItem = profile.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _settingIdentity?.ProfileId) ?? profile.Items.OfType<ComboBoxItem>().FirstOrDefault();
        var scope = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (value, label) in new[] { ("profile_device", T("admin.native.users.prefs.levelDevice")), ("profile_client", T("admin.native.users.prefs.levelClient")), ("profile_library", T("admin.native.users.prefs.levelLibrary")), ("profile_series", T("admin.native.users.prefs.levelSeries")) })
            scope.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        scope.SelectedIndex = 0;
        var qualifier = new TextBox { MaxLength = 256 };
        var qualifierLabel = Text("", 14);
        void Describe() => qualifierLabel.Text = T((scope.SelectedItem as ComboBoxItem)?.Tag as string switch { "profile_client" => "admin.native.users.prefs.idClient", "profile_library" => "admin.native.users.prefs.idLibrary", "profile_series" => "admin.native.users.prefs.idSeries", _ => "admin.native.users.prefs.idDevice" });
        scope.SelectionChanged += (_, _) => Describe(); Describe();
        var error = AlertText();
        var panel = new StackPanel { Spacing = 16, MinWidth = 280 };
        panel.Children.Add(error);
        panel.Children.Add(AdminUsersFormKit.Field(T("admin.native.users.prefs.levelProfile"), profile));
        panel.Children.Add(AdminUsersFormKit.Field(T("admin.native.users.prefs.levelKind"), scope));
        var qualifierField = new StackPanel { Spacing = 6 }; qualifierField.Children.Add(qualifierLabel); qualifierField.Children.Add(qualifier);
        panel.Children.Add(qualifierField);
        var dialog = NewFormDialog(T("admin.native.users.prefs.chooseLevel"), panel, T("admin.native.users.prefs.choose"), AppStrings.Get("common.cancel", fallback: "Cancel"), 448);
        AdminUserSettingIdentity? selected = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var value = qualifier.Text.Trim();
                var profileId = (profile.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                selected = (scope.SelectedItem as ComboBoxItem)?.Tag as string switch
                {
                    "profile_device" => new("profile_device", profileId, DeviceId: value),
                    "profile_client" => new("profile_client", profileId, ClientFamily: value),
                    "profile_library" => new("profile_library", profileId, LibraryId: value),
                    _ => new("profile_series", profileId, SeriesId: value)
                };
                selected.Validate();
            }
            catch (ArgumentException) { Show(error, T("admin.native.users.prefs.idInvalid")); args.Cancel = true; qualifier.Focus(FocusState.Programmatic); }
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
        RequireGeneration(generation);
        if (FormBlocked()) { await LoadAsync(_location); return; }
        var baselines = await SettingReadbacksAsync(generation);
        var review = await FreshReviewAsync(generation);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var form = new AdminUsersSettingForm(definition, (row == null ? definition["default_value"] : row["value"])?.DeepClone());
        var label = definition.Text("label", definition.Text("key"));
        var spec = new FormDialogSpec
        {
            Title = label, Body = form.Content, Review = review, MaxWidth = 512, AutomationId = "SettingForm",
            Primary = form.RawJson ? T("web.admin.users.detail.preferences.json.save") : AppStrings.Get("common.save", fallback: "Save"), Saving = T("web.admin.users.detail.access.card.saving"),
            FailureText = T("admin.native.users.confirmFailed"), OnChanged = callback => form.Changed += callback,
            Build = _ => form.Read() is { Ok: true } read ? new AdminUsersWrite(AdminUsersOperation.SetSetting, new JsonObject { ["value"] = read.Value }, Identity: identity, SettingKey: definition.Text("key"), Readbacks: baselines) : null,
        };
        var result = await FormDialogAsync(spec, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }
    private async Task ResetSettingAsync(AdminUserSettingIdentity identity, JsonObject row)
    {
        var generation = _generation;
        var baselines = await SettingReadbacksAsync(generation);
        var definition = (Document("manifest")?["definitions"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(item => item.Text("key") == row.Text("key"));
        var setting = definition?.Text("label", row.Text("key")) ?? row.Text("key");
        var place = identity.Scope == "account" ? T("admin.native.users.prefs.placeAccount") : T("admin.native.users.prefs.placeLevel", ("level", LevelName(identity)), ("profile", LevelProfile(identity)));
        await ConfirmAsync(new(T("web.admin.users.detail.preferences.removal.titleDefault"), T("web.admin.users.detail.preferences.removal.toDefault", ("setting", setting), ("place", place)),
            T("web.admin.users.detail.preferences.removal.confirmDefault")), new(AdminUsersOperation.ResetSetting, Identity: identity, SettingKey: row.Text("key"), Readbacks: baselines), expectedGeneration: generation);
    }
    private async Task SettingJsonAsync(JsonObject row)
    {
        var content = new TextBox { Text = row.ToJsonString(new() { WriteIndented = true }), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 580 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = T("admin.native.users.prefs.storedTitle", ("key", row.Text("key"))), Content = content, CloseButtonText = T("admin.native.users.prefs.close") };
        _dialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { content.Text = ""; if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
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
        await QuickWriteAsync(new(AdminUsersOperation.SaveSections, body, identity.ProfileId, Readbacks: [new(root + "/settings?scope=home", settings.Body), new(root + "?scope=home", overrides.Body)]), T("web.admin.users.detail.preferences.home.saveFailed"), generation);
    }
}
