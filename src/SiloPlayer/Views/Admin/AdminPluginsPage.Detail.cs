using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPluginsPage
{
    private Grid? _detailGrid;
    private StackPanel? _detailMain;
    private StackPanel? _detailRail;
    private void BuildDetail(AdminPluginsState state)
    {
        var back = _route with { Segments = [] }; back = back.WithQuery("repository", null).WithQuery("version", null);
        var navigation = new LogsWrapPanel { Spacing = 12 };
        navigation.Children.Add(Command("‹ Plugins", "PluginBack", () => _ = GoAsync(back)));
        navigation.Children.Add(Command("Refresh", "PluginDetailRefresh", () => _ = RefreshDetailAsync())); _content.Children.Add(navigation);
        var id = _route.Segment(0);
        var installed = state.Installations.FirstOrDefault(p => p.PluginId == id);
        var catalog = state.Catalog.FirstOrDefault(p => p.PluginId == id && (_route.Get("repository") == null || p.RepositoryId == _route.Get("repository")) && (_route.Get("version") == null || p.Version == _route.Get("version")));
        var plugin = installed ?? catalog;
        if (state.Errors.ContainsKey("Installed")) { _content.Children.Add(Heading("Couldn't load plugins")); _content.Children.Add(Text("Silo couldn't read the installed plugins. Try again in a moment.")); return; }
        if (plugin == null)
        {
            _content.Children.Add(Heading(state.Errors.ContainsKey("Catalog") ? "Couldn't load the plugin catalog" : "Plugin not found"));
            _content.Children.Add(Text(state.Errors.ContainsKey("Catalog") ? "This plugin isn't installed, and Silo couldn't read the catalog to look it up." : "This plugin isn't installed and isn't in any catalog Silo can see.")); return;
        }
        var presentation = plugin.Presentation ?? (installed?.RepositoryId != null ? catalog?.Presentation : null);
        var name = presentation?.DisplayName.Trim() is { Length: > 0 } display ? display : P.Name(plugin);
        _content.Children.Add(Heading(name, 1));
        _content.Children.Add(Text((string.IsNullOrWhiteSpace(presentation?.PublisherName) ? "" : "by " + P.SafeText(presentation.PublisherName) + " · ") + "Version " + P.SafeText(plugin.Version) + " · " + P.Source(P.Tier(plugin)) + " · " + (installed == null ? "Not installed" : P.Status(plugin)), 13, true));
        var actions = new LogsWrapPanel { Spacing = 10, RowSpacing = 10 };
        if (installed == null) actions.Children.Add(Command("Install", "PluginDetailInstall", () => _ = InstallAsync(plugin)));
        else
        {
            var toggle = new ToggleSwitch { Header = "Enabled", IsOn = installed.Enabled };
            AutomationProperties.SetName(toggle, "Enabled"); AutomationProperties.SetAutomationId(toggle, "PluginEnabled");
            toggle.Toggled += async (_, _) =>
            {
                var controller = _controller; if (controller == null || _syncing) return;
                var command = toggle.IsOn ? AdminPluginCommand.Enable : AdminPluginCommand.Disable;
                if (!await CanLeaveAsync() || !controller.IsCurrent(installed)) { _syncing = true; toggle.IsOn = installed.Enabled; _syncing = false; return; }
                await controller.InstallationCommandAsync(installed, command);
            };
            actions.Children.Add(toggle);
            if (installed.RepositoryId == null) actions.Children.Add(Text("Updated by upload", 13, true));
            else if (installed.UpdatesPaused) actions.Children.Add(Text("Updates paused", 13, true));
            else
            {
                var policy = Select("PluginUpdatePolicy", [("auto", "Update automatically"), ("notify", "Ask before updating"), ("off", "Don't check for updates")], 230);
                AutomationProperties.SetName(policy, "Updates"); SetSelected(policy, installed.UpdatePolicy);
                policy.SelectionChanged += async (_, _) =>
                {
                    var controller = _controller;
                    if (_syncing || controller == null) return;
                    var command = Selected(policy) switch { "auto" => AdminPluginCommand.PolicyAuto, "notify" => AdminPluginCommand.PolicyNotify, "off" => AdminPluginCommand.PolicyOff, _ => (AdminPluginCommand?)null };
                    if (command == null) return;
                    if (!await CanLeaveAsync()) { _syncing = true; SetSelected(policy, installed.UpdatePolicy); _syncing = false; return; }
                    await controller.InstallationCommandAsync(installed, command.Value);
                };
                actions.Children.Add(policy);
            }
            if (P.HasUpdate(installed)) actions.Children.Add(Command("Update to " + P.SafeText(installed.AvailableVersion), "PluginUpdate", () => _ = RunDetailCommandAsync(installed, AdminPluginCommand.Update)));
            if (installed.Enabled && installed.Runtime is { Resident: true })
            {
                var restart = Command("Restart", "PluginRestart", () => _ = RunDetailCommandAsync(installed, AdminPluginCommand.Restart)); restart.IsEnabled = installed.Runtime.State != "starting"; actions.Children.Add(restart);
            }
            actions.Children.Add(Command("Uninstall...", "PluginUninstall", () => _ = RunDetailCommandAsync(installed, AdminPluginCommand.Uninstall)));
        }
        _content.Children.Add(actions);
        if (installed?.Enabled == true && installed.Runtime is { } runtime && (runtime.State == "failed" || runtime.Resident && runtime.State == "backoff"))
        {
            var diagnostic = Section(_content, runtime.State == "failed" ? "Failed" : "Waiting to restart");
            diagnostic.Children.Add(Text(P.SafeText(runtime.LastError), 13));
            diagnostic.Children.Add(Text("Restarts: " + runtime.RestartCount + (runtime.NextRestartAt is { } next ? " · Next restart: " + next.ToLocalTime().ToString("g") : ""), 12, true));
        }
        if (installed?.UpdateFailure is { } failure)
        {
            var failed = Section(_content, "Update to " + P.SafeText(failure.Version) + " failed", P.SafeText(failure.Reason));
            failed.Children.Add(Text("It stays on version " + P.SafeText(installed.Version) + ". Last tried " + failure.FailedAt?.ToLocalTime().ToString("g") + ".", 13, true));
        }
        if (installed?.Signature?.State == "invalid") _content.Children.Add(Text("The signature that came with this version doesn't prove which repository built it. The server log has the details."));
        if (installed is { Enabled: true } && P.Missing(installed) is { Count: > 0 } missing)
        {
            var needs = Section(_content, "Needs setup", "Add the required settings. Until then it can't do its job.");
            foreach (var schema in missing) needs.Children.Add(Command(string.IsNullOrWhiteSpace(schema.Title) ? schema.Key : schema.Title, "PluginMissing_" + schema.Key,
                () => _forms.FirstOrDefault(f => f.SchemaKey == schema.Key)?.FocusFirst()));
        }
        if (installed?.UpdatesPaused == true)
        {
            var paused = Section(_content, "Updates paused", installed.SourceKind == "approved_community" ? "Approved community plugins are turned off, so Silo doesn't look for new versions of this plugin." : "Its repository is disabled, so Silo doesn't look for new versions of this plugin.");
            paused.Children.Add(Command("Catalog", "PluginPausedCatalog", () => _ = GoAsync(new(AdminRoute.Plugins, new Dictionary<string, string> { ["tab"] = "catalog" }))));
        }
        var notice = TierNotice(plugin); if (notice.Length > 0) _content.Children.Add(Panel(Text(notice, 13, true), 12, 16));
        _detailMain = Stack(14); _detailRail = Stack(14); _detailGrid = new Grid { ColumnSpacing = 18, RowSpacing = 18 };
        _detailGrid.Children.Add(_detailMain); _detailGrid.Children.Add(_detailRail); _content.Children.Add(_detailGrid); LayoutDetail(ActualWidth);
        if (plugin.GlobalConfigSchema.Count == 0 && !plugin.Capabilities.Any(c => c.Type is "auth_provider.v1" or "scheduled_task.v1"))
            Section(_detailMain, "Settings", installed == null ? "This plugin has no settings. It works as soon as it's installed." : "This plugin has no settings. It works as soon as it's installed and turned on.");
        foreach (var schema in plugin.GlobalConfigSchema)
        {
            var section = Section(_detailMain, string.IsNullOrWhiteSpace(schema.Title) ? schema.Key : schema.Title, schema.Description);
            if (schema.Required) section.Children.Add(Text("Required", 12, true));
            if (installed == null) { section.Children.Add(Text("You can change these after installing.", 13, true)); continue; }
            var view = new AdminPluginConfigView(schema, installed.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key),
                async (write, test) =>
                {
                    if (_controller is not { } controller || !controller.IsCurrent(installed)) return false;
                    if (!test && _forms.Any(f => f.SchemaKey != schema.Key && f.IsDirty) &&
                        !await ConfirmAsync("Save this section?", "Unsaved changes in other sections will be discarded when this plugin reloads.", "Save config", () => controller.IsCurrent(installed))) return false;
                    return test ? await controller.TestConfigAsync(installed, write) : await controller.SaveConfigAsync(installed, write);
                });
            _forms.Add(view); section.Children.Add(view);
        }
        if (installed != null) BuildBindings(installed, _detailMain);
        BuildRail(plugin, presentation, _detailRail);
    }
    private async Task RefreshDetailAsync() { if (_controller is { } controller && await CanLeaveAsync()) await controller.ReloadAsync(); }
    private void LayoutDetail(double width)
    {
        if (_detailGrid == null || _detailMain == null || _detailRail == null) return;
        _detailGrid.ColumnDefinitions.Clear(); _detailGrid.RowDefinitions.Clear();
        if (width >= 1100)
        {
            _detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
            Grid.SetColumn(_detailMain, 0); Grid.SetRow(_detailMain, 0); Grid.SetColumn(_detailRail, 1); Grid.SetRow(_detailRail, 0);
        }
        else
        {
            _detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _detailGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); _detailGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(_detailMain, 0); Grid.SetRow(_detailMain, 0); Grid.SetColumn(_detailRail, 0); Grid.SetRow(_detailRail, 1);
        }
    }
    private async Task RunDetailCommandAsync(AdminPlugin row, AdminPluginCommand command)
    {
        var controller = _controller;
        if (controller == null || !await CanLeaveAsync()) return;
        if (command == AdminPluginCommand.Uninstall && !await ConfirmAsync("Uninstall " + P.SafeText(P.Name(row)) + "?", "Silo will stop the plugin, then remove its installation, configuration, and installed files. This cannot be undone.", "Uninstall plugin", () => controller.IsCurrent(row))) return;
        if (command == AdminPluginCommand.Update && !await ConfirmAsync("Update to " + P.SafeText(row.AvailableVersion) + "?", "Silo will fetch and replace this installation with its recorded available version.", "Update", () => controller.IsCurrent(row))) return;
        if (await controller.InstallationCommandAsync(row, command) && command == AdminPluginCommand.Uninstall && controller.Owner is { IsCurrent: true })
            await GoAsync(new(AdminRoute.Plugins));
    }
    private void BuildBindings(AdminPlugin plugin, StackPanel parent)
    {
        var auth = plugin.Capabilities.Where(c => c.Type == "auth_provider.v1").ToArray();
        if (auth.Length > 0)
        {
            var section = Section(parent, "Sign-in", "Changes apply at once. Test the connection and choose the sign-in options on Settings → Sign-in.");
            for (var i = 0; i < auth.Length; i++)
            {
                var capability = auth[i]; var existing = plugin.AuthBindings.FirstOrDefault(b => b.CapabilityId == capability.Id);
                var index = i; var label = string.IsNullOrWhiteSpace(capability.DisplayName) ? capability.Id : capability.DisplayName;
                var toggle = new ToggleSwitch { Header = P.SafeText(label), IsOn = existing?.Enabled ?? false }; AutomationProperties.SetName(toggle, "Offer " + P.SafeText(label) + " on the sign-in screen"); AutomationProperties.SetAutomationId(toggle, "PluginAuth_" + capability.Id);
                toggle.Toggled += async (_, _) =>
                {
                    if (_syncing) return;
                    var value = toggle.IsOn; var controller = _controller;
                    if (controller == null || !await CanLeaveAsync()) { _syncing = true; toggle.IsOn = existing?.Enabled ?? false; _syncing = false; return; }
                    await controller.AuthBindingAsync(plugin, new() { CapabilityId = capability.Id, Enabled = value, DisplayOrder = existing?.DisplayOrder ?? index + 1, AutoProvision = existing?.AutoProvision ?? true, DefaultLogin = existing?.DefaultLogin ?? false });
                };
                section.Children.Add(toggle); if (capability.Description.Length > 0) section.Children.Add(Text(P.SafeText(capability.Description), 13, true));
            }
        }
        var tasks = plugin.Capabilities.Where(c => c.Type == "scheduled_task.v1").ToArray();
        if (tasks.Length > 0)
        {
            var section = Section(parent, "Scheduled tasks", "Tasks are registered when the server starts, so turning one on or off takes effect after a restart. Once a task exists, change its schedule on its task page.");
            foreach (var capability in tasks)
            {
                var existing = plugin.TaskBindings.FirstOrDefault(b => b.CapabilityId == capability.Id); var label = string.IsNullOrWhiteSpace(capability.DisplayName) ? capability.Id : capability.DisplayName;
                var toggle = new ToggleSwitch { Header = P.SafeText(label), IsOn = existing?.Enabled ?? true };
                AutomationProperties.SetName(toggle, "Run " + P.SafeText(label)); AutomationProperties.SetAutomationId(toggle, "PluginTask_" + capability.Id);
                toggle.Toggled += async (_, _) =>
                {
                    if (_syncing) return; var value = toggle.IsOn; var controller = _controller;
                    if (controller == null || !await CanLeaveAsync()) { _syncing = true; toggle.IsOn = existing?.Enabled ?? true; _syncing = false; return; }
                    await controller.TaskBindingAsync(plugin, new() { CapabilityId = capability.Id, Enabled = value, Trigger = existing?.Trigger ?? new() { ["type"] = JsonSerializer.SerializeToElement("startup") } });
                };
                section.Children.Add(toggle); section.Children.Add(Text(P.SafeText(capability.Description), 13, true));
                section.Children.Add(Text("Default schedule: " + DescribeTrigger(existing?.Trigger), 13, true));
                var route = new AdminRouteRequest(AdminRoute.ScheduledTasks) { Segments = ["plugin:" + plugin.Id + ":" + capability.Id] };
                if (plugin.Enabled && (existing?.Enabled ?? true) && AdminShellPage.CanOpen(route)) section.Children.Add(Command("Schedule and history", "PluginTaskHistory_" + capability.Id, () => _ = GoAsync(route)));
            }
        }
    }
    private static string DescribeTrigger(Dictionary<string, JsonElement>? trigger)
    {
        if (trigger == null || !trigger.TryGetValue("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString())) return "At startup";
        return type.GetString() switch { "startup" => "At startup", "interval" => "Every " + (trigger.TryGetValue("interval_ms", out var ms) ? ms.ToString() : "0") + " ms", "daily" => "Daily " + (trigger.TryGetValue("time_of_day", out var time) ? time.ToString() : ""), "weekly" => "Weekly", _ => P.SafeText(type.GetString()) };
    }
    private void BuildRail(AdminPlugin plugin, AdminPluginInfo? presentation, StackPanel parent)
    {
        if (!string.IsNullOrWhiteSpace(presentation?.SetupMarkdown)) Section(parent, "Setup").Children.Add(PluginMarkdown(presentation.SetupMarkdown));
        if (!string.IsNullOrWhiteSpace(presentation?.DescriptionMarkdown)) Section(parent, "About").Children.Add(PluginMarkdown(presentation.DescriptionMarkdown));
        if (plugin.Runtime is { Resident: true } runtime)
        {
            var process = Section(parent, "Process"); process.Children.Add(Text("State: " + runtime.State)); process.Children.Add(Text("Restarts: " + runtime.RestartCount, 13, true));
            if (runtime.LastStartedAt is { } started) process.Children.Add(Text("Last started: " + started.ToLocalTime().ToString("g"), 13, true));
            if (runtime.NextRestartAt is { } next) process.Children.Add(Text("Next restart: " + next.ToLocalTime().ToString("g"), 13, true));
        }
        var details = Section(parent, "Details");
        details.Children.Add(Text("Plugin ID: " + P.SafeText(plugin.PluginId), 13)); details.Children.Add(Text("Source: " + P.Source(P.Tier(plugin)), 13));
        if (presentation?.PublisherName.Length > 0) details.Children.Add(Text("Publisher: " + P.SafeText(presentation.PublisherName), 13));
        if (plugin.RepositoryName.Length > 0 && plugin.SourceKind == "external") details.Children.Add(Text("Repository: " + P.SafeText(plugin.RepositoryName), 13));
        details.Children.Add(Text("License: " + P.SafeText(string.IsNullOrWhiteSpace(presentation?.LicenseSpdx) ? "Not specified" : presentation.LicenseSpdx), 13));
        if (plugin.Signature is { } signature)
        {
            details.Children.Add(Text("Release signature: " + (signature.State switch { "verified" => "Verified", "invalid" => "Failed the check", "unsigned" => "Not signed", _ => "Not checked" }), 13));
            if (signature.State == "verified") { AddLink(details, "Built from", signature.SourceRepo); if (signature.VerifiedAt is { } verified) details.Children.Add(Text("Verified " + verified.ToLocalTime().ToString("g"), 12, true)); }
        }
        if (plugin.CreatedAt is { } created) details.Children.Add(Text("Installed: " + created.ToLocalTime().ToString("d"), 13));
        AddLink(details, "Source code", presentation?.SourceUrl.Length > 0 ? presentation.SourceUrl : plugin.RepoUrl); AddLink(details, "Changelog", presentation?.ChangelogUrl); AddLink(details, "Support", presentation?.SupportUrl);
        // The pinned management API has no grant/revoke endpoint: show exactly the declared capabilities/subscriptions, never fabricate permission writes.
        if (plugin.Capabilities.Count > 0)
        {
            var capabilities = Section(parent, "Capabilities");
            foreach (var capability in plugin.Capabilities)
            {
                capabilities.Children.Add(Text(P.SafeText(string.IsNullOrWhiteSpace(capability.DisplayName) ? Job(capability.Type).Label : capability.DisplayName), 13));
                if (capability.Description.Length > 0) capabilities.Children.Add(Text(P.SafeText(capability.Description), 12, true));
                if (capability.Subscriptions.Count > 0) capabilities.Children.Add(Text("Subscriptions: " + P.SafeText(string.Join(", ", capability.Subscriptions)), 12, true));
            }
        }
    }
    private static void AddLink(StackPanel parent, string label, string? raw)
    {
        if (P.SafeLink(raw) is not { } uri) return;
        var link = new HyperlinkButton { Content = label, NavigateUri = uri, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(link, label); parent.Children.Add(link);
    }
    private static FrameworkElement PluginMarkdown(string source)
    {
        // Native text rendering, without HTML, remote images, scripts or signed link expansion.
        var body = Stack(8); var inCode = false;
        foreach (var line in P.SafeText(source).Replace("\r", "").Split('\n').Take(300))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; continue; }
            if (line.Length == 0) continue;
            var heading = !inCode && line.StartsWith('#');
            var text = heading ? line.TrimStart('#', ' ') : line.StartsWith("- ", StringComparison.Ordinal) ? "• " + line[2..] : line;
            text = text.Replace("**", "").Replace("`", "");
            var block = Text(text, heading ? 15 : 13, !heading, mono: inCode); block.IsTextSelectionEnabled = true;
            if (heading) block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; body.Children.Add(block);
        }
        return body;
    }
}
