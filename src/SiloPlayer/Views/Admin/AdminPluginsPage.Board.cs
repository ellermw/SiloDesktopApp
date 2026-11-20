using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPluginsPage
{
    private void BuildBoard(AdminPluginsState state)
    {
        _syncing = true;
        var tabs = new LogsWrapPanel { Spacing = 12 };
        tabs.Children.Add(Command("Installed (" + state.Installations.Count + ")", "PluginsInstalledTab", () => ChangeTab("installed")));
        tabs.Children.Add(Command("Catalog (" + state.Catalog.Count + ")", "PluginsCatalogTab", () => ChangeTab("catalog")));
        _content.Children.Add(tabs);
        if (_tab == "catalog") BuildCommunity(state);
        var filters = new LogsWrapPanel { Spacing = 10, RowSpacing = 10 };
        _search = Input(_tab == "catalog" ? "Search the plugin catalog" : "Search installed plugins", "PluginsSearch", 300);
        _search.MaxWidth = 480; _search.MinWidth = 120; _search.Width = double.NaN; _search.HorizontalAlignment = HorizontalAlignment.Stretch;
        var searchHost = new Grid { MinWidth = 140, MaxWidth = 320, Width = Math.Max(140, Math.Min(320, ActualWidth - 48)) }; searchHost.Children.Add(_search); filters.Children.Add(searchHost);
        _search.Text = _route.Get(_tab == "catalog" ? "catalog_q" : "installed_q") ?? "";
        _search.TextChanged += (_, _) => { if (_syncing) return; _page = 0; ReplaceQuery(_tab == "catalog" ? "catalog_q" : "installed_q", _search.Text); RenderList(); };
        if (_tab == "installed")
        {
            _filter = Select("PluginsFilter", [("all", "All"), ("attention", "Needs attention"), ("update", "Update available"), ("off", "Off")], 200);
            AutomationProperties.SetName(_filter, "Filter installed plugins"); SetSelected(_filter, _route.Get("installed_filter") ?? "all");
            _filter.SelectionChanged += (_, _) => { if (_syncing) return; _page = 0; ReplaceQuery("installed_filter", Selected(_filter)); RenderList(); };
            _job = null;
        }
        else
        {
            _filter = null;
            var jobs = state.Catalog.SelectMany(p => p.Capabilities).Select(c => Job(c.Type)).Where(j => j.Primary).DistinctBy(j => j.Key).ToArray();
            _job = Select("PluginsJob", new[] { ("", "All") }.Concat(jobs.Select(j => (j.Key, j.Label))), 200);
            AutomationProperties.SetName(_job, "Filter by what plugins do"); SetSelected(_job, jobs.Any(j => j.Key == _route.Get("catalog_job")) ? _route.Get("catalog_job") : "");
            _job.SelectionChanged += (_, _) => { if (_syncing) return; _page = 0; ReplaceQuery("catalog_job", Selected(_job)); RenderList(); };
        }
        filters.Children.Add(Command("Clear search and filters", "PluginsClearFilters", () =>
        {
            _syncing = true; _search.Text = ""; if (_filter != null) _filter.SelectedIndex = 0; if (_job != null) _job.SelectedIndex = 0; _syncing = false; _page = 0;
            ReplaceQuery(_tab == "catalog" ? "catalog_q" : "installed_q", null); ReplaceQuery(_tab == "catalog" ? "catalog_job" : "installed_filter", null); RenderList();
        }));
        _chipHost = new LogsWrapPanel { Spacing = 6, RowSpacing = 6 }; filters.Children.Add(_chipHost);
        _content.Children.Add(filters); _listHost = Stack(14); AutomationProperties.SetAutomationId(_listHost, "PluginsTiles"); _content.Children.Add(_listHost);
        if (_tab == "catalog") { BuildUpload(); BuildRepositories(state); }
        _syncing = false; RenderList();
    }
    private void ChangeTab(string tab)
    {
        _tab = tab; ReplaceQuery("tab", tab == "catalog" ? "catalog" : null); _page = 0; _rendered = null; Render();
    }
    private void RenderList()
    {
        if (_listHost == null || _controller == null) return;
        var state = _controller.State; var catalog = _tab == "catalog"; var source = catalog ? state.Catalog : state.Installations;
        _listHost.Children.Clear();
        if (state.Errors.ContainsKey(catalog ? "Catalog" : "Installed")) return; // A failed read is not an empty inventory.
        var query = _search?.Text ?? ""; var filter = _filter == null ? "all" : Selected(_filter); var job = _job == null ? "" : Selected(_job);
        if (_chipHost != null)
        {
            _chipHost.Children.Clear();
            var choices = catalog && _job != null ? _job.Items.Cast<ComboBoxItem>().Select(i => ((string)i.Tag, i.Content?.ToString() ?? "")).ToArray()
                : new[] { ("all", "All"), ("attention", "Needs attention"), ("update", "Update available"), ("off", "Off") };
            if (!catalog || choices.Length > 2)
            foreach (var (value, label) in choices)
            {
                var count = catalog ? "" : " (" + source.Count(p => P.Matches(p, query, value)) + ")";
                var active = catalog ? value == job : value == filter;
                var button = U.Action(label + count, "PluginsChip_" + value, primary: active, pill: true);
                AutomationProperties.SetHelpText(button, active ? "Selected filter" : "Apply filter");
                button.Click += (_, _) => { var selector = catalog ? _job : _filter; if (selector != null) SetSelected(selector, value); };
                _chipHost.Children.Add(button);
            }
        }
        var selected = source.Where(p => P.Matches(p, query, catalog ? "all" : filter) && (!catalog || job.Length == 0 || p.Capabilities.Any(c => Job(c.Type).Key == job)));
        var installed = state.Installations.Select(p => p.PluginId).ToHashSet(StringComparer.Ordinal);
        var visible = catalog ? selected.OrderBy(p => p.SourceKind switch { "silo" => 0, "approved_community" => 1, _ => 2 }).ThenBy(p => installed.Contains(p.PluginId)).ThenBy(P.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()
            : selected.OrderBy(P.Rank).ThenBy(P.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (source.Count == 0)
        {
            var empty = Section(_listHost, catalog ? "No plugins available" : "No plugins installed", catalog ? "Add a repository or upload a plugin file." : "Browse the Catalog tab to find and install plugins.");
            return;
        }
        var count = Text(visible.Length == source.Count ? source.Count + " plugins" : visible.Length + " of " + source.Count + " plugins", 12, true);
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite); _listHost.Children.Add(count);
        if (visible.Length == 0) { _listHost.Children.Add(Text(catalog ? "No catalog plugins match" : "No installed plugins match")); return; }
        _page = Math.Clamp(_page, 0, Math.Max(0, (visible.Length - 1) / _pageSize));
        var slice = visible.Skip(_page * _pageSize).Take(_pageSize).ToArray();
        if (catalog)
        {
            foreach (var group in slice.GroupBy(p => p.SourceKind))
            {
                _listHost.Children.Add(Heading(group.Key switch { "silo" => "Made by Silo", "approved_community" => "Approved community", _ => "Other sources" }));
                AddTiles(group.ToArray(), installed, true);
            }
        }
        else AddTiles(slice, installed, false);
        var paging = new LogsWrapPanel { Spacing = 10 };
        var previous = Command("Previous", "PluginsPrevious", () => { _page--; RenderList(); }); previous.IsEnabled = _page > 0;
        var next = Command("Next", "PluginsNext", () => { _page++; RenderList(); }); next.IsEnabled = (_page + 1) * _pageSize < visible.Length;
        paging.Children.Add(previous); paging.Children.Add(Text("Page " + (_page + 1) + " of " + ((visible.Length + _pageSize - 1) / _pageSize), 13, true)); paging.Children.Add(next);
        var sizes = Select("PluginsPageSize", [("25", "25 per page"), ("50", "50 per page"), ("100", "100 per page")], 145); AutomationProperties.SetName(sizes, "Plugins per page"); SetSelected(sizes, _pageSize.ToString());
        sizes.SelectionChanged += (_, _) => { if (int.TryParse(Selected(sizes), out var size)) { _pageSize = size; _page = 0; RenderList(); } }; paging.Children.Add(sizes);
        _listHost.Children.Add(paging);
    }
    private void AddTiles(AdminPlugin[] rows, HashSet<string> installed, bool catalog)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var column = 0; column < _columns; column++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows.Length; index++)
        {
            if (index % _columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var plugin = rows[index]; var card = Stack(10);
            var title = U.Action(P.SafeText(P.Name(plugin)), "PluginOpen_" + plugin.PluginId); title.Height = double.NaN; title.HorizontalAlignment = HorizontalAlignment.Stretch; title.HorizontalContentAlignment = HorizontalAlignment.Left;
            title.Click += async (_, _) => await OpenPluginAsync(plugin, catalog); card.Children.Add(title);
            card.Children.Add(Text("Version " + P.SafeText(plugin.Version) + " · " + P.Source(P.Tier(plugin)), 12, true));
            card.Children.Add(Text(P.SafeText(plugin.Presentation?.Summary ?? plugin.Capabilities.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Description))?.Description ?? "No description provided."), 13, true));
            card.Children.Add(Text(string.Join(" · ", plugin.Capabilities.Select(c => Job(c.Type)).Where(j => j.Primary).DistinctBy(j => j.Key).Select(j => j.Label)), 12, true));
            if (!catalog)
            {
                card.Children.Add(Text(P.Status(plugin)));
                if (P.HasUpdate(plugin)) card.Children.Add(Text(P.SafeText(plugin.AvailableVersion) + " available", 12));
                if (plugin.UpdatesPaused) card.Children.Add(Text("Updates paused", 12, true));
                if (plugin.UpdateFailure != null) card.Children.Add(Text("Update to " + P.SafeText(plugin.UpdateFailure.Version) + " failed", 12));
                if (plugin.Signature?.State == "invalid") card.Children.Add(Text("Signature failed", 12));
            }
            else if (installed.Contains(plugin.PluginId)) card.Children.Add(Text("Installed", 12));
            else
            {
                var install = Command("Install", "PluginInstall_" + plugin.PluginId, () => _ = InstallAsync(plugin));
                install.IsEnabled = !_controller!.State.Errors.ContainsKey("Installed"); card.Children.Add(install);
            }
            var border = Panel(card, 16, 16); Grid.SetColumn(border, index % _columns); Grid.SetRow(border, index / _columns); grid.Children.Add(border);
        }
        _listHost!.Children.Add(grid);
    }
    private Task OpenPluginAsync(AdminPlugin plugin, bool catalog)
    {
        var route = _route with { Segments = [plugin.PluginId] };
        if (catalog) route = route.WithQuery("repository", plugin.RepositoryId).WithQuery("version", plugin.Version);
        return GoAsync(route);
    }
    private async Task InstallAsync(AdminPlugin plugin)
    {
        var controller = _controller;
        if (controller == null || controller.State.Errors.ContainsKey("Installed")) return;
        if (await ConfirmAsync("Install " + P.SafeText(P.Name(plugin)) + "?", "Install version " + P.SafeText(plugin.Version) + " from " + P.Source(plugin.SourceKind) + ". " + TierNotice(plugin), "Install plugin", () => controller.IsCatalogCurrent(plugin)))
            await controller.InstallAsync(plugin);
    }
    private void BuildCommunity(AdminPluginsState state)
    {
        var section = Section(_content, "Include approved community plugins", "Reviewed by Silo maintainers to work as described and be safe for their documented use. These plugins remain maintained and supported by community contributors.");
        if (state.CatalogStatus is { } status)
            section.Children.Add(Text(status.ApprovedCommunityPluginCount + " approved · " + status.InstalledCommunityPluginCount + " installed" + (status.MigratedPluginCount > 0 ? " · " + status.MigratedPluginCount + " migrated" : ""), 12, true));
        var settings = state.CatalogSettings;
        var toggle = new ToggleSwitch { Header = "Include approved community plugins", IsOn = settings?.Value.IncludeApprovedCommunityPlugins == true, IsEnabled = settings != null && !state.CatalogReviewRequired };
        AutomationProperties.SetAutomationId(toggle, "PluginsCommunity"); AutomationProperties.SetName(toggle, "Include approved community plugins");
        toggle.Toggled += async (_, _) =>
        {
            if (_syncing || settings == null || _controller == null) return;
            var value = toggle.IsOn; var controller = _controller;
            if (!value && !await ConfirmAsync("Hide approved community plugins?", "Installed community plugins keep working, but their updates pause until you include approved community plugins again.", "Hide and pause updates", () => ReferenceEquals(settings, controller.State.CatalogSettings)))
            { _syncing = true; toggle.IsOn = settings.Value.IncludeApprovedCommunityPlugins; _syncing = false; return; }
            await controller.SetCommunityAsync(settings, value);
            if (!controller.State.Loading && ReferenceEquals(settings, controller.State.CatalogSettings)) { _syncing = true; toggle.IsOn = settings.Value.IncludeApprovedCommunityPlugins; _syncing = false; }
        };
        section.Children.Add(toggle);
        if (state.CatalogStatus?.CommunityUpdatesPaused == true) section.Children.Add(Text("Updates paused", 13));
    }
    private static (string Key, string Label, bool Primary) Job(string type) => type switch
    {
        "metadata_provider.v1" => ("metadata", "Metadata", true), "image_resolver.v1" => ("artwork", "Artwork", true),
        "marker_provider.v1" => ("markers", "Markers", true), "scan_source.v1" => ("scanning", "Scanning", true),
        "request_router.v1" => ("requests", "Requests", true), "watch_sync_provider.v1" => ("watch-sync", "Watch sync", true),
        "auth_provider.v1" => ("sign-in", "Sign-in", true), "scheduled_task.v1" => ("tasks", "Scheduled tasks", true),
        "media_analyzer.v1" => ("analysis", "Media analysis", true), "audiobook_backend.v1" => ("audiobooks", "Audiobooks", true),
        "ebook_backend.v1" => ("ebooks", "Ebooks", true), "network_access_provider.v1" => ("network", "Network access", true),
        "event_consumer.v1" => ("events", "Events", false), "http_handler.v1" => ("web-pages", "Web pages", false), _ => (type, type, true),
    };
    private static string TierNotice(AdminPlugin p) => P.Tier(p) switch
    {
        "silo" => "", "approved_community" => "Reviewed by Silo maintainers to work as described and be safe for its documented use. Its author maintains and supports it, not the Silo project.",
        "unverified" => "It isn't linked to any of your catalogs, so Silo can't verify it or check it for updates. Only run it if you trust its source. To update it, upload the new version.",
        _ => "Silo has not reviewed this plugin. It comes from a repository you added, so only run it if you trust its source.",
    };
}
