using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using T = SiloPlayer.Core.Services.Admin.AdminPluginsText;
using Ui = SiloPlayer.Views.Admin.AdminPluginsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPluginsPage
{
    private Grid? _detailGrid;
    private StackPanel? _detailMain;
    private StackPanel? _detailRail;

    /// <summary>Resolved when a link is clicked, so a launcher set after the page was drawn (a fixture, a test) is the one used.</summary>
    private Task<bool> OpenLink(Uri uri) => Launcher(uri);

    private static string When(DateTimeOffset? value) => value is { } date ? DateTimeDisplay.FormatDateTime(date) : "";

    private void BuildDetail(AdminPluginsState state)
    {
        var id = _route.Segment(0) ?? "";
        var installation = state.Installations.FirstOrDefault(p => p.PluginId == id);
        var repository = _route.Get("repository"); var version = _route.Get("version");
        var entry = state.Catalog.FirstOrDefault(e => e.PluginId == id && (repository == null || e.RepositoryId == repository) && (version == null || e.Version == version));
        var provisioning = state.Provisioning.FirstOrDefault(r => r.PluginId == id && r.State != "applied");
        var installedFailed = state.Failures.ContainsKey(AdminPluginsSection.Installed);
        if (installation != null)
        {
            _content.Children.Add(BackLink("/admin/plugins"));
            if (installedFailed) _content.Children.Add(StaleNote(state.Failures[AdminPluginsSection.Installed], "PluginDetailReload", AdminPluginsSection.Installed));
            BuildInstalled(state, installation, entry, provisioning); return;
        }
        if (!state.Ready(AdminPluginsSection.Installed) && !installedFailed) { _content.Children.Add(Text(T.T("web.admin.plugins.detail.loading"), 13, true)); return; }
        if (installedFailed)
        {
            _content.Children.Add(Unavailable(T.T("web.admin.plugins.detail.installationsErrorTitle"), T.T("web.admin.plugins.detail.installationsErrorDescription"), "PluginDetailRetry", () => ReloadSection(AdminPluginsSection.Installed))); return;
        }
        if (entry != null) { _content.Children.Add(BackLink("/admin/plugins?tab=catalog")); BuildPreview(state, entry, provisioning); return; }
        if (provisioning != null) { _content.Children.Add(BackLink("/admin/plugins")); _content.Children.Add(ProvisioningBlock(state, id)); return; }
        var catalogPending = !state.Ready(AdminPluginsSection.Catalog) && !state.Failures.ContainsKey(AdminPluginsSection.Catalog);
        var provisioningPending = !state.Ready(AdminPluginsSection.Provisioning) && !state.Failures.ContainsKey(AdminPluginsSection.Provisioning);
        if (catalogPending || provisioningPending) { _content.Children.Add(Text(T.T("web.admin.plugins.detail.loading"), 13, true)); return; }
        if (state.Failures.ContainsKey(AdminPluginsSection.Catalog))
        {
            _content.Children.Add(Unavailable(T.T("web.admin.plugins.detail.catalogErrorTitle"), T.T("web.admin.plugins.detail.catalogErrorDescription"), "PluginDetailRetry", () => ReloadSection(AdminPluginsSection.Catalog))); return;
        }
        if (state.Failures.ContainsKey(AdminPluginsSection.Provisioning))
        {
            _content.Children.Add(Unavailable(T.T("web.admin.plugins.provisioning.title"), T.T("web.admin.plugins.provisioning.loadError"), "PluginDetailRetry", () => ReloadSection(AdminPluginsSection.Provisioning))); return;
        }
        var missing = Stack(8); missing.Children.Add(Ui.Heading(T.T("web.admin.plugins.detail.notFoundTitle"))); missing.Children.Add(Text(T.T("web.admin.plugins.detail.notFoundDescription"), 13, true));
        missing.Children.Add(Ui.Command(T.T("web.admin.plugins.detail.allPlugins"), "PluginsAll", () => _ = RunAsync(() => GoAsync(new(AdminRoute.Plugins)))));
        _content.Children.Add(Panel(missing, 16, 20, border: true));
    }

    private FrameworkElement BackLink(string fallback)
    {
        // The board hands over the search and filters it was showing, so Back restores them.
        var target = fallback.StartsWith("/admin/plugins?tab=catalog", StringComparison.Ordinal)
            ? new AdminRouteRequest(AdminRoute.Plugins).WithQuery("tab", "catalog") : _boardReturn ?? new AdminRouteRequest(AdminRoute.Plugins);
        var back = Ui.Command("‹ " + T.T("web.admin.plugins.detail.back"), "PluginBack", () => { var route = target; _ = RunAsync(() => GoAsync(route)); });
        AutomationProperties.SetName(back, T.T("web.admin.plugins.detail.back")); back.HorizontalAlignment = HorizontalAlignment.Left;
        return back;
    }

    private FrameworkElement PageHeader(string name, IEnumerable<FrameworkElement> meta, IEnumerable<FrameworkElement> actions)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var monogram = Ui.Monogram(name, 48); monogram.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(monogram);
        var stack = Stack(8); var title = Ui.Heading(name, 1); title.FontSize = 28; title.TextWrapping = TextWrapping.Wrap; title.IsTextSelectionEnabled = true; stack.Children.Add(title);
        var line = new LogsWrapPanel { Spacing = 16, RowSpacing = 4 }; foreach (var item in meta) line.Children.Add(item); stack.Children.Add(line);
        var buttons = new LogsWrapPanel { Spacing = 10, RowSpacing = 8 }; foreach (var item in actions) buttons.Children.Add(item); stack.Children.Add(buttons);
        Grid.SetColumn(stack, 1); grid.Children.Add(stack);
        return grid;
    }

    private static TextBlock Meta(string text) => Text(text, 13.5, true);

    private void TierBanner(AdminPluginsState state, string tier)
    {
        var notice = P.TierNotice(tier); if (notice == null) return;
        _content.Children.Add(Ui.Banner(Ui.Rich(T.Runs("web.admin.plugins.detail.tierNotice", new { source = P.SourceLabel(tier), notice, b = "" }), null, 13), false));
    }

    private void BuildInstalled(AdminPluginsState state, AdminPlugin installation, AdminPlugin? entry, AdminPluginProvisionStatus? provisioning)
    {
        var controller = _controller!; var pin = AdminPluginPin.Of(installation);
        var tier = P.Tier(installation); var catalogFallback = tier == "unverified" ? null : entry;
        var presentation = installation.Presentation ?? entry?.Presentation;
        var name = P.Name(installation.PluginId, presentation); var status = P.Status(installation); var missing = P.MissingRequired(installation);
        var runtime = installation.Runtime ?? new AdminPluginRuntime(); var ownership = installation.EnvManaged;
        var updateVersion = P.PendingUpdate(installation); var policy = string.IsNullOrEmpty(installation.UpdatePolicy) ? "auto" : installation.UpdatePolicy!;
        var canRestart = runtime.Resident && installation.Enabled;
        var adminRoutes = installation.Routes.Where(route => route.Navigable && route.NavigationKind == "admin").ToArray();
        var idle = !state.Busy && !state.Loading;
        var publisher = presentation?.PublisherName?.Trim();
        var meta = new List<FrameworkElement>();
        if (!string.IsNullOrEmpty(publisher)) meta.Add(Meta(T.T("web.admin.plugins.detail.byPublisher", new { publisher })));
        meta.Add(Meta(T.T("web.admin.plugins.detail.version", new { version = installation.Version })));
        var jobs = P.CapabilityList(installation.Capabilities); if (jobs.Length > 0) meta.Add(Meta(jobs));
        meta.Add(Meta(P.SourceLabel(tier))); meta.Add(Ui.StatusLabel(status));
        var actions = new List<FrameworkElement>();
        // Without a repository there is nothing to check; with the repository disabled or the community catalog hidden,
        // checks are paused. Either way a stored policy does nothing, so it is not offered.
        if (!string.IsNullOrEmpty(ownership?.VersionPin)) actions.Add(Meta(T.T("web.admin.plugins.provisioning.versionPinned", new { version = ownership!.VersionPin })));
        else if (tier == "unverified") actions.Add(Meta(T.T("web.admin.plugins.detail.updatedByUpload")));
        else if (installation.UpdatesPaused) actions.Add(Meta(T.T("web.admin.plugins.detail.updatesPaused")));
        else actions.Add(PolicyChoice(pin, policy, idle));
        actions.Add(EnabledToggle(pin, installation.Enabled, idle));
        actions.Add(MoreMenu(installation, pin, updateVersion, canRestart, adminRoutes, runtime, idle, name, ownership != null));
        _content.Children.Add(PageHeader(name, meta, actions));
        if (ownership != null)
        {
            var managed = Stack(4); var title = Text(T.T("web.admin.plugins.provisioning.managed"), 13); title.FontWeight = Microsoft.UI.Text.FontWeights.Medium; managed.Children.Add(title);
            managed.Children.Add(Text(T.T("web.admin.plugins.provisioning.sourceManaged"), 13)); _content.Children.Add(Ui.Banner(managed, false));
        }
        if (provisioning != null) _content.Children.Add(ProvisioningBlock(state, installation.PluginId));
        if (installation.Enabled && (runtime.State == "failed" || runtime.Resident && runtime.State == "backoff")) _content.Children.Add(RuntimeBanner(installation, name, runtime, canRestart, pin, idle));
        if (installation.UpdateFailure is { } failure)
        {
            var failed = Stack(6);
            failed.Children.Add(Ui.Rich(T.Runs("web.admin.plugins.detail.updateFailed", new { name, b = "", version = failure.Version, reason = P.Redact(failure.Reason, 600) }), null, 13));
            failed.Children.Add(Text(T.T("web.admin.plugins.detail.updateFailedStays", new { version = installation.Version, date = When(failure.FailedAt) }), 13, true));
            _content.Children.Add(Ui.Banner(failed, true));
        }
        if (installation.Signature?.State == "invalid") _content.Children.Add(Ui.Banner(Ui.Rich(T.Runs("web.admin.plugins.signature.invalidBanner", new { name, b = "" }), null, 13), true));
        if (installation.Enabled && missing.Count > 0) _content.Children.Add(Ui.Banner(MissingConfig(name, missing), true));
        if (installation.UpdatesPaused)
        {
            var key = tier == "approved_community" ? "web.admin.plugins.detail.pausedCommunity" : !string.IsNullOrEmpty(installation.RepositoryName) ? "web.admin.plugins.detail.pausedRepository" : "web.admin.plugins.detail.pausedRepositoryUnnamed";
            var policyLabel = PolicyLabel(policy);
            _content.Children.Add(Ui.Banner(Ui.Rich(T.Runs(key, new { name, repository = installation.RepositoryName ?? "", policy = policyLabel ?? "", has_policy = policyLabel != null ? "yes" : "no", b = "", link = "" }),
                () => _ = RunAsync(() => GoAsync(new AdminRouteRequest(AdminRoute.Plugins).WithQuery("tab", "catalog"))), 13), false));
        }
        TierBanner(state, tier);
        _detailMain = Stack(14); _detailRail = Stack(14); _detailGrid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        _detailGrid.Children.Add(_detailMain); _detailGrid.Children.Add(_detailRail); _content.Children.Add(_detailGrid); LayoutDetail(_body.ActualWidth);
        BuildSettings(state, installation, pin, missing);
        BuildRail(installation.PluginId, presentation, installation, catalogFallback, tier);
    }

    private static string? PolicyLabel(string policy) => policy switch
    {
        "auto" => T.T("web.admin.plugins.detail.policyAuto"), "notify" => T.T("web.admin.plugins.detail.policyNotify"), "off" => T.T("web.admin.plugins.detail.policyOff"), _ => null,
    };

    private FrameworkElement PolicyChoice(AdminPluginPin pin, string policy, bool idle)
    {
        var options = new List<(string, string)> { ("auto", T.T("web.admin.plugins.detail.policyAuto")), ("notify", T.T("web.admin.plugins.detail.policyNotify")), ("off", T.T("web.admin.plugins.detail.policyOff")) };
        if (PolicyLabel(policy) == null) options.Add((policy, policy));
        var select = Select("PluginUpdatePolicy", options, 230); SetSelected(select, policy); select.IsEnabled = idle;
        AutomationProperties.SetName(select, T.T("web.admin.plugins.detail.updatesLabel"));
        select.SelectionChanged += (_, _) =>
        {
            if (_syncing || _controller is not { } controller) return;
            var value = Selected(select); if (value == policy || value is not ("auto" or "notify" or "off")) return;
            _ = RunAsync(async () =>
            {

                if (await controller.SetPolicyAsync(pin, value) != AdminPluginWriteOutcome.Done) RebuildNow();
            });
        };
        return select;
    }

    private FrameworkElement EnabledToggle(AdminPluginPin pin, bool enabled, bool idle)
    {
        var toggle = new ToggleSwitch { IsOn = enabled, OnContent = T.T("web.admin.plugins.detail.on"), OffContent = T.T("web.admin.plugins.detail.off"), MinWidth = 0, IsEnabled = idle };
        AutomationProperties.SetName(toggle, T.T("web.admin.plugins.detail.enabledLabel")); AutomationProperties.SetAutomationId(toggle, "PluginEnabled");
        toggle.Toggled += (_, _) =>
        {
            if (_syncing || _controller is not { } controller) return;
            var on = toggle.IsOn; if (on == enabled) return; // a control reporting its starting value changes nothing
            _ = RunAsync(async () =>
            {

                if (await controller.CommandAsync(pin, on ? AdminPluginCommand.Enable : AdminPluginCommand.Disable) != AdminPluginWriteOutcome.Done) RebuildNow();
            });
        };
        return toggle;
    }

    private FrameworkElement MoreMenu(AdminPlugin installation, AdminPluginPin pin, string? updateVersion, bool canRestart, AdminPluginRoute[] routes, AdminPluginRuntime runtime, bool idle, string name, bool managed)
    {
        var flyout = new MenuFlyout();
        if (canRestart)
        {
            var restart = new MenuFlyoutItem { Text = T.T("web.admin.plugins.detail.restart"), IsEnabled = idle && runtime.State != "starting" };
            AutomationProperties.SetAutomationId(restart, "PluginRestart"); restart.Click += (_, _) => _ = RunAsync(() => RestartAsync(pin)); flyout.Items.Add(restart);
        }
        if (updateVersion != null)
        {
            var update = new MenuFlyoutItem { Text = T.T("web.admin.plugins.detail.updateTo", new { version = updateVersion }), IsEnabled = idle };
            AutomationProperties.SetAutomationId(update, "PluginUpdate"); update.Click += (_, _) => _ = RunAsync(() => UpdateAsync(installation, pin, updateVersion, name)); flyout.Items.Add(update);
        }
        foreach (var route in routes)
        {
            // Plugin-contributed pages belong to Plugin Apps, which this build does not host: a real entry, disabled, with its reason.
            var item = new MenuFlyoutItem { Text = string.IsNullOrEmpty(route.NavigationLabel) ? route.Path : route.NavigationLabel, IsEnabled = false };
            ToolTipService.SetToolTip(item, "Plugin pages are not available in this app yet."); AutomationProperties.SetAutomationId(item, "PluginRoute_" + route.Id); flyout.Items.Add(item);
        }
        if (flyout.Items.Count > 0) flyout.Items.Add(new MenuFlyoutSeparator());
        var uninstall = new MenuFlyoutItem { Text = T.T("web.admin.plugins.detail.uninstallMenu"), IsEnabled = idle && !managed };
        AutomationProperties.SetAutomationId(uninstall, "PluginUninstall"); uninstall.Click += (_, _) => _ = RunAsync(() => UninstallAsync(pin, name)); flyout.Items.Add(uninstall);
        var more = new Button { Content = "⋯", Flyout = flyout, MinWidth = 32, Height = 32, Padding = new(8, 0, 8, 0), CornerRadius = new(10), Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
        AutomationProperties.SetName(more, T.T("web.admin.plugins.detail.moreActions")); AutomationProperties.SetAutomationId(more, "PluginMore");
        return more;
    }

    private async Task RestartAsync(AdminPluginPin pin)
    {
        if (_controller is not { } controller) return;
        if (await controller.CommandAsync(pin, AdminPluginCommand.Restart) != AdminPluginWriteOutcome.Done) RebuildNow();
    }

    /// <summary>Applies the recorded update. The confirmation names the version that was offered; a different recorded version sends nothing.</summary>
    private async Task UpdateAsync(AdminPlugin installation, AdminPluginPin pin, string version, string name)
    {
        var controller = _controller; if (controller == null) return;
        var tier = P.Tier(installation); var content = Stack(10);
        content.Children.Add(Text(T.T("web.admin.plugins.tile.confirmDescription", new { source = P.SourceLabel(tier), notice = P.TierNotice(tier) ?? "" }).Trim(), 14));
        content.Children.Add(Text(name + ": " + T.T("web.admin.plugins.detail.version", new { version = installation.Version }) + " → " + T.T("web.admin.plugins.detail.version", new { version }), 13, true));
        if (!await ConfirmAsync(T.T("web.admin.plugins.detail.updateTo", new { version }), content, T.T("web.admin.plugins.detail.updateTo", new { version }), "PluginsUpdateConfirmation")) return;
        if (await controller.CommandAsync(pin, AdminPluginCommand.Update) != AdminPluginWriteOutcome.Done) RebuildNow();
    }

    private async Task UninstallAsync(AdminPluginPin pin, string name)
    {
        var controller = _controller; if (controller == null) return;
        if (!await ConfirmAsync(T.T("web.admin.plugins.detail.uninstallTitle", new { name }), Text(T.T("web.admin.plugins.detail.uninstallDescription"), 14), T.T("web.admin.plugins.detail.uninstallConfirm"), "PluginsUninstallConfirmation", destructive: true)) return;
        var outcome = await controller.CommandAsync(pin, AdminPluginCommand.Uninstall);
        if (outcome == AdminPluginWriteOutcome.Done && _controller != null) { DiscardForms(); await GoAsync(new(AdminRoute.Plugins)); }
        else RebuildNow();
    }

    private FrameworkElement RuntimeBanner(AdminPlugin installation, string name, AdminPluginRuntime runtime, bool canRestart, AdminPluginPin pin, bool idle)
    {
        var body = Stack(8);
        if (runtime.Resident)
        {
            var count = T.Count(runtime.RestartCount, ("name", name), ("b", ""));
            if (runtime.State == "failed") body.Children.Add(Ui.Rich(T.Runs("web.admin.plugins.detail.runtimeFailed", count), null, 13));
            else if (runtime.State == "backoff" && runtime.NextRestartAt != null)
            { count["time"] = When(runtime.NextRestartAt); body.Children.Add(Ui.Rich(T.Runs("web.admin.plugins.detail.runtimeBackoffNext", count), null, 13)); }
            else body.Children.Add(Ui.Rich(T.Runs("web.admin.plugins.detail.runtimeBackoff", count), null, 13));
        }
        else body.Children.Add(Ui.Rich(T.Runs("web.admin.plugins.detail.startFailed", new { name, b = "" }), null, 13));
        if (!string.IsNullOrEmpty(runtime.LastError))
        {
            var error = Text(P.Redact(runtime.LastError, 2000), 12, mono: true); error.IsTextSelectionEnabled = true;
            body.Children.Add(Panel(error, 8, 10, border: true));
        }
        if (canRestart)
        {
            var restarting = _controller?.State is { Busy: true };
            var restart = Ui.Command(restarting ? T.T("web.admin.plugins.detail.restarting") : T.T("web.admin.plugins.detail.restartNow"), "PluginRestartNow", () => _ = RunAsync(() => RestartAsync(pin)));
            restart.IsEnabled = idle && runtime.State != "starting"; restart.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(restart);
        }
        return Ui.Banner(body, true);
    }

    private FrameworkElement MissingConfig(string name, IReadOnlyList<AdminPluginSchema> missing)
    {
        // "Finish setting up {name}. Add {fields}. ..." where each field name jumps to its settings panel.
        const string marker = "\u0001";
        var runs = T.Runs("web.admin.plugins.detail.missingConfig", new { name, b = "", fields = marker });
        var block = Text("", 13); block.IsTextSelectionEnabled = true;
        foreach (var run in runs)
        {
            var at = run.Text.IndexOf(marker, StringComparison.Ordinal);
            void Add(string text) { if (text.Length == 0) return; var inline = new Microsoft.UI.Xaml.Documents.Run { Text = text }; if (run.Bold) inline.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; block.Inlines.Add(inline); }
            if (at < 0) { Add(run.Text); continue; }
            Add(run.Text[..at]);
            for (var index = 0; index < missing.Count; index++)
            {
                if (index > 0) Add(index == missing.Count - 1 ? " and " : ", ");
                var schema = missing[index]; var link = new Microsoft.UI.Xaml.Documents.Hyperlink();
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = string.IsNullOrWhiteSpace(schema.Title) ? schema.Key : schema.Title.Trim() });
                link.Click += (_, _) => { if (_forms.Values.FirstOrDefault(form => form.SchemaKey == schema.Key) is { } form) form.FocusFirst(); };
                block.Inlines.Add(link);
            }
            Add(run.Text[(at + marker.Length)..]);
        }
        return block;
    }

    private void LayoutDetail(double width)
    {
        if (_detailGrid == null || _detailMain == null || _detailRail == null) return;
        _detailGrid.ColumnDefinitions.Clear(); _detailGrid.RowDefinitions.Clear();
        if (width <= 0 || width >= 1100)
        {
            _detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
            Grid.SetColumn(_detailMain, 0); Grid.SetRow(_detailMain, 0); Grid.SetColumn(_detailRail, 1); Grid.SetRow(_detailRail, 0);
        }
        else
        {
            _detailGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            _detailGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); _detailGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(_detailMain, 0); Grid.SetRow(_detailMain, 0); Grid.SetColumn(_detailRail, 0); Grid.SetRow(_detailRail, 1);
        }
    }

    // ── settings panels ────────────────────────────────────────────────────

    private void BuildSettings(AdminPluginsState state, AdminPlugin installation, AdminPluginPin pin, IReadOnlyList<AdminPluginSchema> missing)
    {
        var main = _detailMain!; var missingKeys = missing.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var auth = installation.Capabilities.Where(c => c.Type == "auth_provider.v1").ToArray();
        var tasks = installation.Capabilities.Where(c => c.Type == "scheduled_task.v1").ToArray();
        if (installation.GlobalConfigSchema.Count == 0 && auth.Length == 0 && tasks.Length == 0)
        {
            Ui.Card(main, T.T("web.admin.plugins.settings.title"), T.T("web.admin.plugins.settings.noneInstalled")); return;
        }
        foreach (var schema in installation.GlobalConfigSchema)
        {
            var body = Ui.Card(main, P.ConfigTitle(schema), string.IsNullOrEmpty(schema.Description) ? null : schema.Description, missingKeys.Contains(schema.Key) ? T.T("web.admin.plugins.settings.required") : null, "config-" + schema.Key);
            body.Children.Add(ConfigForm(state, installation, pin, schema));
        }
        if (auth.Length > 0) BuildAuth(state, installation, pin, auth);
        if (tasks.Length > 0) BuildTasks(state, installation, pin, tasks);
    }

    private FrameworkElement ConfigForm(AdminPluginsState state, AdminPlugin installation, AdminPluginPin pin, AdminPluginSchema schema)
    {
        var saved = installation.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key);
        List<string> managedFields = installation.EnvManaged != null && installation.EnvManaged.ConfigFields.TryGetValue(schema.Key, out var owned) ? owned : [];
        var key = installation.Id + "|" + installation.Version + "|" + schema.Key; _usedForms.Add(key);
        var draft = new AdminPluginConfigDraft(schema, saved, managedFields);
        if (_forms.TryGetValue(key, out var existing))
        {
            // The same entry, unchanged: the admin's draft, typed secrets included, carries across the redraw.
            if (existing.Fingerprint == draft.Fingerprint) { draft.Dispose(); return existing; }
            existing.Dispose(); _forms.Remove(key);
        }
        Func<AdminPluginConfigWrite, Task<AdminPluginCheck?>>? test = null;
        if (installation.ConnectionCheckSupported) test = async write => await (_controller?.TestConfigAsync(pin, write) ?? Task.FromResult<AdminPluginCheck?>(null));
        var view = new AdminPluginConfigView(draft, async write => await (_controller?.SaveConfigAsync(pin, write) ?? Task.FromResult(AdminPluginWriteOutcome.NotSent)), test);
        _forms[key] = view; return view;
    }

    private void BuildAuth(AdminPluginsState state, AdminPlugin installation, AdminPluginPin pin, AdminPluginCapabilityInfo[] capabilities)
    {
        var body = Ui.Card(_detailMain!, T.T("web.admin.plugins.settings.signInTitle"), T.T("web.admin.plugins.settings.signInDescription"));
        for (var index = 0; index < capabilities.Length; index++)
        {
            var capability = capabilities[index]; var binding = installation.AuthBindings.FirstOrDefault(b => b.CapabilityId == capability.Id);
            var label = string.IsNullOrEmpty(capability.DisplayName) ? capability.Id : capability.DisplayName; var locked = capability.EnvManaged?.Contains("enabled") == true; var order = index + 1;
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var texts = Stack(2); texts.Children.Add(Text(label, 14));
            if (!string.IsNullOrEmpty(capability.Description)) texts.Children.Add(Text(capability.Description, 13, true));
            if (binding?.CallbackPending == true) texts.Children.Add(Text(T.T("web.admin.plugins.provisioning.pending"), 12, true));
            if (locked) texts.Children.Add(Text(T.T("web.admin.plugins.provisioning.managed"), 12, true));
            row.Children.Add(texts);
            var toggle = new ToggleSwitch { IsOn = binding?.Enabled ?? false, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = !state.Busy && !state.Loading && !locked };
            AutomationProperties.SetName(toggle, T.T("web.admin.plugins.settings.offerSignIn", new { label })); AutomationProperties.SetAutomationId(toggle, "PluginAuth_" + capability.Id);
            toggle.Toggled += (_, _) =>
            {
                if (_syncing || _controller is not { } controller || toggle.IsOn == (binding?.Enabled ?? false)) return;
                var write = new AdminPluginAuthBindingWrite { CapabilityId = capability.Id, Enabled = toggle.IsOn, DisplayOrder = binding?.DisplayOrder ?? order, AutoProvision = binding?.AutoProvision ?? true, DefaultLogin = binding?.DefaultLogin ?? false };
                _ = RunAsync(async () => { if (await controller.SaveAuthBindingAsync(pin, write) != AdminPluginWriteOutcome.Done) RebuildNow(); });
            };
            Grid.SetColumn(toggle, 1); row.Children.Add(toggle); body.Children.Add(Panel(row, 10, 12, border: true));
        }
    }

    private void BuildTasks(AdminPluginsState state, AdminPlugin installation, AdminPluginPin pin, AdminPluginCapabilityInfo[] capabilities)
    {
        var body = Ui.Card(_detailMain!, T.T("web.admin.plugins.settings.tasksTitle"), T.T("web.admin.plugins.settings.tasksDescription"));
        foreach (var capability in capabilities)
        {
            var binding = installation.TaskBindings.FirstOrDefault(b => b.CapabilityId == capability.Id);
            var label = string.IsNullOrEmpty(capability.DisplayName) ? capability.Id : capability.DisplayName;
            var trigger = P.TaskTrigger(binding?.Trigger, capability.DefaultSchedule); var enabled = binding?.Enabled ?? true;
            var registered = installation.Enabled && enabled;
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var texts = Stack(4); texts.Children.Add(Text(label, 14));
            if (!string.IsNullOrEmpty(capability.Description)) texts.Children.Add(Text(capability.Description, 13, true));
            texts.Children.Add(Text(T.T("web.admin.plugins.settings.defaultSchedule", new { schedule = AdminTasksText.DescribeTrigger(trigger) }), 13, true));
            if (registered)
            {
                var route = new AdminRouteRequest(AdminRoute.ScheduledTasks) { Segments = [P.TaskKey(installation.Id, capability.Id)] };
                var open = Ui.Command(T.T("web.admin.plugins.settings.scheduleLink"), "PluginTaskHistory_" + capability.Id, () => _ = RunAsync(() => GoAsync(route))); open.HorizontalAlignment = HorizontalAlignment.Left; texts.Children.Add(open);
            }
            row.Children.Add(texts);
            var toggle = new ToggleSwitch { IsOn = enabled, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = !state.Busy && !state.Loading };
            AutomationProperties.SetName(toggle, T.T("web.admin.plugins.settings.runTask", new { label })); AutomationProperties.SetAutomationId(toggle, "PluginTask_" + capability.Id);
            toggle.Toggled += (_, _) =>
            {
                if (_syncing || _controller is not { } controller || toggle.IsOn == enabled) return;
                var on = toggle.IsOn;
                // Resend a trigger set through the API; with none, the task follows its manifest default.
                _ = RunAsync(async () => { if (await controller.SaveTaskBindingAsync(pin, capability.Id, on, binding?.Trigger ?? new Dictionary<string, JsonElement>()) != AdminPluginWriteOutcome.Done) RebuildNow(); });
            };
            Grid.SetColumn(toggle, 1); row.Children.Add(toggle); body.Children.Add(Panel(row, 10, 12, border: true));
        }
    }

    // ── catalog preview (not installed) ────────────────────────────────────

    private void BuildPreview(AdminPluginsState state, AdminPlugin entry, AdminPluginProvisionStatus? provisioning)
    {
        var name = P.Name(entry); var publisher = entry.Presentation?.PublisherName?.Trim();
        var meta = new List<FrameworkElement>();
        if (!string.IsNullOrEmpty(publisher)) meta.Add(Meta(T.T("web.admin.plugins.detail.byPublisher", new { publisher })));
        meta.Add(Meta(T.T("web.admin.plugins.detail.version", new { version = entry.Version })));
        var jobs = P.CapabilityList(entry.Capabilities); if (jobs.Length > 0) meta.Add(Meta(jobs));
        meta.Add(Meta(P.SourceLabel(entry.SourceKind))); meta.Add(Ui.StatusLabel(T.T("web.admin.plugins.detail.notInstalled"), AdminPluginTone.Muted));
        var installing = state.Busy && state.BusyKey == entry.PluginId;
        var install = AdminSettingsUi.Action(installing ? T.T("web.admin.plugins.detail.installing") : T.T("web.admin.plugins.detail.install"), "PluginDetailInstall", primary: true);
        install.IsEnabled = !state.Busy && !state.Loading; install.Click += (_, _) => _ = RunAsync(() => InstallAsync(entry));
        _content.Children.Add(PageHeader(name, meta, [install]));
        if (provisioning != null) _content.Children.Add(ProvisioningBlock(state, entry.PluginId));
        TierBanner(state, entry.SourceKind);
        _detailMain = Stack(14); _detailRail = Stack(14); _detailGrid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        _detailGrid.Children.Add(_detailMain); _detailGrid.Children.Add(_detailRail); _content.Children.Add(_detailGrid); LayoutDetail(_body.ActualWidth);
        if (entry.GlobalConfigSchema.Count == 0) Ui.Card(_detailMain, T.T("web.admin.plugins.settings.title"), T.T("web.admin.plugins.settings.nonePreview"));
        else
        {
            var body = Ui.Card(_detailMain, T.T("web.admin.plugins.settings.title"), T.T("web.admin.plugins.settings.previewDescription"));
            foreach (var schema in entry.GlobalConfigSchema)
            {
                var line = Stack(2); var title = new LogsWrapPanel { Spacing = 8 }; title.Children.Add(Text(P.ConfigTitle(schema), 14));
                if (schema.Required) title.Children.Add(Panel(Text(T.T("web.admin.plugins.settings.required"), 11), 6, 4, border: true));
                line.Children.Add(title); if (!string.IsNullOrEmpty(schema.Description)) line.Children.Add(Text(schema.Description, 13, true));
                body.Children.Add(line);
            }
        }
        BuildRail(entry.PluginId, entry.Presentation, null, entry, entry.SourceKind);
    }

    // ── side column ────────────────────────────────────────────────────────

    private static string RuntimeState(string state) => state switch
    {
        "stopped" => T.T("web.admin.plugins.rail.stateStopped"), "starting" => T.T("web.admin.plugins.rail.stateStarting"), "running" => T.T("web.admin.plugins.rail.stateRunning"),
        "backoff" => T.T("web.admin.plugins.rail.stateBackoff"), "failed" => T.T("web.admin.plugins.rail.stateFailed"), _ => state,
    };
    private void Fact(StackPanel facts, string label, FrameworkElement value)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(110) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Text(label, 13, true)); Grid.SetColumn(value, 1); row.Children.Add(value); facts.Children.Add(row);
    }

    private void BuildRail(string pluginId, AdminPluginInfo? presentation, AdminPlugin? installation, AdminPlugin? catalogEntry, string tier)
    {
        var rail = _detailRail!; AutomationProperties.SetName(rail, T.T("web.admin.plugins.rail.label"));
        var repositoryName = !string.IsNullOrEmpty(installation?.RepositoryName) ? installation.RepositoryName : catalogEntry?.RepositoryName;
        var repoUrl = !string.IsNullOrEmpty(installation?.RepoUrl) ? installation.RepoUrl : catalogEntry?.RepoUrl;
        if (!string.IsNullOrWhiteSpace(presentation?.SetupMarkdown)) RailCard(rail, T.T("web.admin.plugins.rail.setup")).Children.Add(Ui.Markdown(presentation.SetupMarkdown, OpenLink));
        if (!string.IsNullOrWhiteSpace(presentation?.DescriptionMarkdown)) RailCard(rail, T.T("web.admin.plugins.rail.about")).Children.Add(Ui.Markdown(presentation.DescriptionMarkdown, OpenLink));
        if (installation?.Runtime is { Resident: true } runtime)
        {
            var process = Stack(8); Fact(process, T.T("web.admin.plugins.rail.state"), Text(RuntimeState(runtime.State), 13));
            Fact(process, T.T("web.admin.plugins.rail.restarts"), Text(runtime.RestartCount.ToString(), 13));
            if (runtime.LastStartedAt != null) Fact(process, T.T("web.admin.plugins.rail.lastStarted"), Text(When(runtime.LastStartedAt), 13));
            if (runtime.NextRestartAt != null) Fact(process, T.T("web.admin.plugins.rail.nextRestart"), Text(When(runtime.NextRestartAt), 13));
            RailCard(rail, T.T("web.admin.plugins.rail.process")).Children.Add(process);
        }
        var facts = Stack(8);
        var id = Text(pluginId, 12.5, mono: true); id.IsTextSelectionEnabled = true; Fact(facts, T.T("web.admin.plugins.rail.pluginId"), id);
        var publisher = presentation?.PublisherName?.Trim();
        if (!string.IsNullOrEmpty(publisher))
            Fact(facts, T.T("web.admin.plugins.rail.publisher"), Ui.Link(publisher, presentation?.PublisherUrl, OpenLink, "PluginPublisher") ?? Text(publisher, 13));
        Fact(facts, T.T("web.admin.plugins.rail.source"), Text(P.SourceLabel(tier), 13));
        if (!string.IsNullOrEmpty(repositoryName) && tier is not ("silo" or "approved_community")) Fact(facts, T.T("web.admin.plugins.rail.repository"), Text(repositoryName, 13));
        Fact(facts, T.T("web.admin.plugins.rail.license"), Text(P.License(presentation?.LicenseSpdx), 13));
        if (installation?.Signature != null) Fact(facts, T.T("web.admin.plugins.signature.label"), SignatureFact(installation.Signature));
        if (installation?.CreatedAt is { } created) Fact(facts, T.T("web.admin.plugins.rail.installed"), Text(DateTimeDisplay.FormatDate(created, medium: true), 13));
        var details = RailCard(rail, T.T("web.admin.plugins.rail.details")); details.Children.Add(facts);
        var links = new LogsWrapPanel { Spacing = 16, RowSpacing = 6 };
        foreach (var (label, url) in P.ResourceLinks(presentation, repoUrl)) if (Ui.Link(label, url.ToString(), OpenLink, "PluginLink_" + label) is { } link) links.Children.Add(link);
        if (links.Children.Count > 0) details.Children.Add(links);
    }

    private static StackPanel RailCard(StackPanel rail, string title)
    {
        var body = Stack(10); var heading = Ui.Heading(title, 2, 14); body.Children.Add(heading); rail.Children.Add(Panel(body, 16, 16, border: true)); return body;
    }

    private FrameworkElement SignatureFact(AdminPluginSignature signature)
    {
        var body = Stack(2);
        switch (signature.State)
        {
            case "verified":
                var verifiedAt = signature.VerifiedAt != null ? T.T("web.admin.plugins.signature.verifiedAt", new { date = When(signature.VerifiedAt) }) : null;
                if (string.IsNullOrEmpty(signature.SourceRepo)) { body.Children.Add(Text(verifiedAt ?? T.T("web.admin.plugins.signature.verifiedNoRepository"), 13)); break; }
                var label = P.SignatureRepositoryLabel(signature.SourceRepo);
                var runs = T.Runs("web.admin.plugins.signature.verified", new { repository = label, link = "" });
                var line = Text("", 13); line.IsTextSelectionEnabled = true;
                foreach (var run in runs)
                {
                    if (run.Link && P.SafeLink(signature.SourceRepo) is { } uri)
                    {
                        var link = new Microsoft.UI.Xaml.Documents.Hyperlink(); link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = run.Text });
                        link.Click += async (_, _) => { try { await Launcher(uri); } catch (Exception) { } }; line.Inlines.Add(link);
                    }
                    else line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = run.Text });
                }
                body.Children.Add(line); if (verifiedAt != null) body.Children.Add(Text(verifiedAt, 12.5, true));
                break;
            case "invalid": var invalid = Text("⚠ " + T.T("web.admin.plugins.signature.invalid"), 13); invalid.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning); body.Children.Add(invalid); break;
            case "unsigned": body.Children.Add(Text(T.T("web.admin.plugins.signature.unsigned"), 13)); break;
            default: body.Children.Add(Text(T.T("web.admin.plugins.signature.unknown"), 13)); break;
        }
        return body;
    }
}
