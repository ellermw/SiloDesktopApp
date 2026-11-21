using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using T = SiloPlayer.Core.Services.Admin.AdminPluginsText;
using Ui = SiloPlayer.Views.Admin.AdminPluginsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPluginsPage
{
    private StackPanel? _boardList;
    private TextBox? _search;
    private LogsWrapPanel? _chipHost;
    private TextBlock? _count;
    private readonly List<(Grid Grid, List<FrameworkElement> Tiles)> _tileGrids = [];
    private int _columns = 1;
    private int _shown = TilePage;

    private static readonly (string Kind, string Title)[] Groups =
    [
        ("silo", "web.admin.plugins.catalog.groupSilo"), ("approved_community", "web.admin.plugins.catalog.groupApprovedCommunity"), ("external", "web.admin.plugins.catalog.groupExternal"),
    ];

    private void BuildBoard(AdminPluginsState state)
    {
        _tileGrids.Clear(); _boardList = null; _search = null; _chipHost = null; _count = null; _shown = TilePage;
        _content.Children.Add(ProvisioningBlock(state));
        if (!state.Ready(AdminPluginsSection.Installed))
        {
            if (state.Failures.ContainsKey(AdminPluginsSection.Installed))
                _content.Children.Add(Unavailable(T.T("web.admin.plugins.detail.installationsErrorTitle"), T.T("web.admin.plugins.detail.installationsErrorDescription"), "PluginsRetry", ReloadAll, back: false));
            else _content.Children.Add(Text(T.T("web.admin.plugins.page.loading"), 13, true));
            return;
        }
        if (state.Failures.TryGetValue(AdminPluginsSection.Installed, out var stale))
            _content.Children.Add(StaleNote(stale, "PluginsInstalledReload", AdminPluginsSection.Installed));
        var tabs = new LogsWrapPanel { Spacing = 8 };
        string Labeled(string label, int count) => count > 0 ? label + " " + count : label;
        void Tab(string key, string text, int count)
        {
            var button = AdminSettingsUi.Action(Labeled(text, count), key == "catalog" ? "PluginsCatalogTab" : "PluginsInstalledTab", primary: _tab == key, pill: true);
            AutomationProperties.SetHelpText(button, _tab == key ? "Selected" : "");
            button.Click += (_, _) => ChangeTab(key); tabs.Children.Add(button);
        }
        Tab("installed", T.T("web.admin.plugins.page.installedTab"), state.Installations.Count);
        Tab("catalog", T.T("web.admin.plugins.page.catalogTab"), state.Catalog.Count);
        _content.Children.Add(tabs);
        if (_tab == "catalog") BuildCatalogTab(state); else BuildInstalledTab(state);
    }

    private FrameworkElement StaleNote(string text, string id, AdminPluginsSection section)
    {
        var row = new LogsWrapPanel { Spacing = 8 };
        var message = Text(text, 13); message.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning); AutomationProperties.SetLiveSetting(message, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        row.Children.Add(message);
        var reload = Ui.Command(T.T("common.retry"), id, () => ReloadSection(section)); reload.IsEnabled = _controller?.State.Loading != true; row.Children.Add(reload);
        return row;
    }

    private void ChangeTab(string tab)
    {
        if (_tab == tab) return;
        _tab = tab; ReplaceQuery("tab", tab == "catalog" ? "catalog" : null); RebuildNow();
    }

    private FrameworkElement EmptyState(string title, string description)
    {
        var body = Stack(6); body.HorizontalAlignment = HorizontalAlignment.Center;
        var heading = Text(title, 14); heading.FontWeight = Microsoft.UI.Text.FontWeights.Medium; heading.HorizontalAlignment = HorizontalAlignment.Center; heading.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center;
        var detail = Text(description, 12, true); detail.HorizontalAlignment = HorizontalAlignment.Center; detail.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center;
        body.Children.Add(heading); body.Children.Add(detail);
        return Panel(body, 12, 40);
    }

    // ── search and filters ─────────────────────────────────────────────────

    private string QueryKey => _tab == "catalog" ? "catalog_q" : "installed_q";

    private FrameworkElement Toolbar(string placeholder)
    {
        var bar = new LogsWrapPanel { Spacing = 10, RowSpacing = 10 };
        var text = _route.Get(QueryKey) ?? "";
        var search = Input(placeholder, "PluginsSearch", 300); search.MaxWidth = 480; search.Text = text; _search = search;
        AutomationProperties.SetName(search, placeholder);
        search.TextChanged += (_, _) =>
        {
            if (_syncing || _closed) return;
            _shown = TilePage; ReplaceQuery(QueryKey, search.Text); RenderList();
        };
        bar.Children.Add(search);
        var clear = Ui.Command(T.T("web.admin.plugins.list.clearSearch"), "PluginsClearSearch", () => { search.Text = ""; });
        clear.Content = "×"; AutomationProperties.SetName(clear, T.T("web.admin.plugins.list.clearSearch")); clear.MinWidth = 32; clear.Padding = new(0);
        bar.Children.Add(clear);
        _chipHost = new LogsWrapPanel { Spacing = 6, RowSpacing = 6 }; bar.Children.Add(_chipHost);
        _count = Text("", 12, true); _count.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetLiveSetting(_count, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        bar.Children.Add(_count);
        return bar;
    }

    private void Chip(string value, string label, int? count, bool active, string id, Action choose)
    {
        var button = AdminSettingsUi.Action(count is { } n ? label + " " + n : label, id, primary: active, pill: true);
        AutomationProperties.SetHelpText(button, active ? "Selected filter" : "Apply filter");
        AutomationProperties.SetName(button, count is { } c ? label + ", " + c : label);
        button.Click += (_, _) => choose(); _chipHost!.Children.Add(button);
    }

    private void SetCount(int shown, int total)
    {
        if (_count == null) return;
        _count.Text = shown == total ? T.T("web.admin.plugins.list.resultCount", T.Count(total)) : T.T("web.admin.plugins.list.resultCountFiltered", T.Count(total, ("shown", shown)));
    }

    // ── installed tab ──────────────────────────────────────────────────────

    private void BuildInstalledTab(AdminPluginsState state)
    {
        if (state.Installations.Count == 0)
        {
            _content.Children.Add(EmptyState(T.T("web.admin.plugins.installed.emptyTitle"), T.T("web.admin.plugins.installed.emptyDescription"))); return;
        }
        _content.Children.Add(Toolbar(T.T("web.admin.plugins.installed.search")));
        _boardList = Stack(14); AutomationProperties.SetAutomationId(_boardList, "PluginsTiles"); _content.Children.Add(_boardList);
        RenderList();
    }

    private AdminPluginInfo? PresentationFor(AdminPluginsState state, AdminPlugin installation) =>
        installation.Presentation ?? state.Catalog.LastOrDefault(entry => entry.PluginId == installation.PluginId)?.Presentation;

    private void RenderList()
    {
        var controller = _controller;
        if (_boardList == null || controller == null || _chipHost == null) return;
        var state = controller.State; var query = _search?.Text ?? _route.Get(QueryKey) ?? "";
        _boardList.Children.Clear(); _chipHost.Children.Clear(); _tileGrids.Clear();
        if (_tab == "catalog") RenderCatalogList(state, query); else RenderInstalledList(state, query);
    }

    private void RenderInstalledList(AdminPluginsState state, string query)
    {
        var filter = P.ParseFilter(_route.Get("installed_filter"));
        var searched = state.Installations.Where(plugin => P.Matches(query, plugin.PluginId, PresentationFor(state, plugin), plugin.Capabilities, P.Tier(plugin), plugin.RepositoryName)).ToList();
        var visible = P.SortInstalled(searched.Where(plugin => P.MatchesFilter(plugin, filter)), plugin => PresentationFor(state, plugin));
        void Filter(AdminPluginFilter value, string label, int count) => Chip(P.FilterKey(value), label, count, filter == value, "PluginsChip_" + P.FilterKey(value),
            () => { ReplaceQuery("installed_filter", value == AdminPluginFilter.All ? null : P.FilterKey(value)); _shown = TilePage; RenderList(); });
        Filter(AdminPluginFilter.All, T.T("web.admin.plugins.list.filterAll"), searched.Count);
        Filter(AdminPluginFilter.Attention, T.T("web.admin.plugins.installed.filterAttention"), searched.Count(p => P.MatchesFilter(p, AdminPluginFilter.Attention)));
        Filter(AdminPluginFilter.Update, T.T("web.admin.plugins.installed.filterUpdate"), searched.Count(p => P.MatchesFilter(p, AdminPluginFilter.Update)));
        Filter(AdminPluginFilter.Off, T.T("web.admin.plugins.installed.filterOff"), searched.Count(p => P.MatchesFilter(p, AdminPluginFilter.Off)));
        SetCount(visible.Count, state.Installations.Count);
        if (visible.Count == 0)
        {
            _boardList.Children.Add(NoMatches(T.T("web.admin.plugins.installed.noMatches"), () => { ReplaceQuery("installed_q", null); ReplaceQuery("installed_filter", null); if (_search != null) _search.Text = ""; _shown = TilePage; RenderList(); }));
            return;
        }
        AddTiles(visible.Take(_shown).Select(plugin => InstalledTile(state, plugin)).ToList());
        MoreButton(visible.Count);
    }

    private FrameworkElement NoMatches(string label, Action clear)
    {
        var body = Stack(4); body.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Text(label, 14); text.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(text);
        var button = Ui.Command(T.T("web.admin.plugins.list.clearFilters"), "PluginsClearFilters", clear); button.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(button);
        AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        return Panel(body, 12, 32);
    }

    /// <summary>Tiles beyond the first page wait behind one button, so the page never creates thousands of cards at once.</summary>
    private void MoreButton(int total)
    {
        if (_shown >= total) return;
        var more = Ui.Command("+" + Math.Min(TilePage, total - _shown), "PluginsShowMore", () => { _shown += TilePage; RenderList(); });
        AutomationProperties.SetName(more, "Show more plugins"); _boardList!.Children.Add(more);
    }

    private void AddTiles(List<FrameworkElement> tiles)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var tile in tiles) grid.Children.Add(tile);
        _tileGrids.Add((grid, tiles)); LayoutGrid(grid, tiles); _boardList!.Children.Add(grid);
    }

    private void LayoutTiles(double width)
    {
        var columns = width >= 1000 ? 4 : width >= 760 ? 3 : width >= 500 ? 2 : 1;
        if (columns == _columns) return;
        _columns = columns; foreach (var (grid, tiles) in _tileGrids) LayoutGrid(grid, tiles);
    }

    private void LayoutGrid(Grid grid, List<FrameworkElement> tiles)
    {
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var column = 0; column < _columns; column++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < tiles.Count; index++)
        {
            if (index % _columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(tiles[index], index % _columns); Grid.SetRow(tiles[index], index / _columns);
        }
    }

    // ── tiles ──────────────────────────────────────────────────────────────

    private FrameworkElement TileCard(FrameworkElement content, bool attention, bool dimmed)
    {
        var card = new Border
        {
            CornerRadius = new(12), BorderThickness = new(1), Padding = new(14, 14, 14, 0), Child = content, MinWidth = 0,
            Background = dimmed ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) : Tint("SurfaceBrush", 0.9),
            BorderBrush = attention ? AdminMonitorUi.Solid(AdminMonitorUi.Warning, 0.4) : Tint("BorderBrush", 0.72),
        };
        if (dimmed) card.Opacity = 0.85;
        return card;
    }

    private static FrameworkElement TileHead(string name, string subline, Action? open = null)
    {
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(Ui.Monogram(name));
        var titles = Stack(0); titles.VerticalAlignment = VerticalAlignment.Center;
        var title = Text(name, 14); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
        var sub = Text(subline, 12, true); sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.TextWrapping = TextWrapping.NoWrap;
        if (open != null)
        {
            // The name is the keyboard target of a tile that also holds an Install button.
            var link = AdminMonitorUi.Plain(title, name); link.HorizontalAlignment = HorizontalAlignment.Left; link.HorizontalContentAlignment = HorizontalAlignment.Left;
            link.Click += (_, _) => open(); link.Tapped += (_, args) => args.Handled = true; titles.Children.Add(link);
        }
        else titles.Children.Add(title);
        titles.Children.Add(sub); Grid.SetColumn(titles, 1); head.Children.Add(titles);
        return head;
    }
    private static TextBlock TileSummary(string text)
    {
        var summary = Text(text, 13, true); summary.MaxLines = 2; summary.TextTrimming = TextTrimming.CharacterEllipsis; summary.MinHeight = 36;
        return summary;
    }

    private static FrameworkElement TileFooter(FrameworkElement left, FrameworkElement? right)
    {
        var footer = new Grid { ColumnSpacing = 8, MinHeight = 40, BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.72), Padding = new(0, 8, 0, 8) };
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        left.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(left);
        if (right != null) { right.VerticalAlignment = VerticalAlignment.Center; right.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(right, 1); footer.Children.Add(right); }
        return footer;
    }

    private static FrameworkElement Capabilities(IEnumerable<AdminPluginCapabilityInfo> capabilities)
    {
        var labels = P.PrimaryKinds(capabilities).Select(kind => kind.Label).ToArray();
        var text = Text(string.Join(" · ", labels), 12, true); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap;
        if (labels.Length > 0) ToolTipService.SetToolTip(text, string.Join(", ", labels));
        return text;
    }

    private FrameworkElement InstalledTile(AdminPluginsState state, AdminPlugin plugin)
    {
        var presentation = PresentationFor(state, plugin); var name = P.Name(plugin.PluginId, presentation); var status = P.Status(plugin);
        var update = P.PendingUpdate(plugin); var signature = plugin.Signature;
        var content = Stack(10);
        content.Children.Add(TileHead(name, T.T("web.admin.plugins.tile.installedSubline", new { version = plugin.Version, source = P.SourceLabel(P.Tier(plugin)) })));
        content.Children.Add(TileSummary(P.Summary(presentation, plugin.Capabilities)));
        var left = new LogsWrapPanel { Spacing = 8 }; left.Children.Add(Ui.StatusLabel(status));
        if (plugin.EnvManaged != null) left.Children.Add(Text(T.T("web.admin.plugins.provisioning.managed"), 12, true));
        if (signature?.State == "verified" && !string.IsNullOrEmpty(signature.SourceRepo))
        {
            var verified = T.T("web.admin.plugins.signature.tileVerified", new { repository = P.SignatureRepositoryLabel(signature.SourceRepo) });
            var mark = Text("✓", 13, true); AutomationProperties.SetName(mark, verified); ToolTipService.SetToolTip(mark, verified); left.Children.Add(mark);
        }
        FrameworkElement? right;
        if (signature?.State == "invalid") { right = Text("⚠ " + T.T("web.admin.plugins.signature.tileInvalid"), 12); ((TextBlock)right).Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning); }
        else if (plugin.UpdateFailure is { } failure)
        {
            var failed = Text("⚠ " + T.T("web.admin.plugins.tile.updateFailed", new { version = failure.Version }), 12); failed.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning);
            failed.TextTrimming = TextTrimming.CharacterEllipsis; failed.TextWrapping = TextWrapping.NoWrap; ToolTipService.SetToolTip(failed, P.Redact(failure.Reason, 400)); right = failed;
        }
        else if (update != null) right = Panel(Text(T.T("web.admin.plugins.tile.updateAvailable", new { version = update }), 12), 6, 6);
        else if (plugin.UpdatesPaused) right = Text(T.T("web.admin.plugins.tile.updatesPaused"), 12, true);
        else right = Capabilities(plugin.Capabilities);
        content.Children.Add(TileFooter(left, right));
        // The whole tile is one keyboard and pointer target; the footer carries no controls of its own.
        var open = AdminMonitorUi.Plain(content, name, new Thickness(0)); open.HorizontalAlignment = HorizontalAlignment.Stretch; open.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(open, "PluginOpen_" + plugin.PluginId);
        open.Click += (_, _) => OpenDetail(plugin.PluginId, null);
        return TileCard(open, status.Attention, !plugin.Enabled);
    }

    private void OpenDetail(string pluginId, AdminPlugin? catalogEntry)
    {
        _boardReturn = _route with { Segments = [] };
        var route = new AdminRouteRequest(AdminRoute.Plugins) { Segments = [pluginId] };
        if (catalogEntry != null) route = route.WithQuery("repository", catalogEntry.RepositoryId).WithQuery("version", catalogEntry.Version);
        _ = RunAsync(() => GoAsync(route));
    }

    // ── catalog tab ────────────────────────────────────────────────────────

    private void BuildCatalogTab(AdminPluginsState state)
    {
        BuildCommunityControl(state);
        if (state.Failures.TryGetValue(AdminPluginsSection.Catalog, out var failure)) _content.Children.Add(StaleNote(failure, "PluginsCatalogReload", AdminPluginsSection.Catalog));
        if (state.Catalog.Count == 0 && !state.Failures.ContainsKey(AdminPluginsSection.Catalog))
            _content.Children.Add(EmptyState(T.T("web.admin.plugins.catalog.emptyTitle"), T.T("web.admin.plugins.catalog.emptyDescription")));
        else if (state.Catalog.Count > 0)
        {
            _content.Children.Add(Toolbar(T.T("web.admin.plugins.catalog.search")));
            _boardList = Stack(18); AutomationProperties.SetAutomationId(_boardList, "PluginsCatalog"); _content.Children.Add(_boardList);
            RenderList();
        }
        var panels = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        panels.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); panels.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var upload = BuildUploadPanel(); var repositories = BuildRepositoriesPanel(state);
        Grid.SetColumn(repositories, 1); panels.Children.Add(upload); panels.Children.Add(repositories);
        _sourcePanels = panels; _sourcePanelParts = (upload, repositories); LayoutSources(_body.ActualWidth);
        _content.Children.Add(panels);
    }

    private Grid? _sourcePanels;
    private (FrameworkElement Upload, FrameworkElement Repositories) _sourcePanelParts;

    private void LayoutSources(double width)
    {
        if (_sourcePanels == null) return;
        var wide = width <= 0 || width >= 1000;
        _sourcePanels.ColumnDefinitions.Clear(); _sourcePanels.RowDefinitions.Clear();
        _sourcePanels.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (wide) { _sourcePanels.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); Grid.SetColumn(_sourcePanelParts.Upload, 0); Grid.SetColumn(_sourcePanelParts.Repositories, 1); Grid.SetRow(_sourcePanelParts.Repositories, 0); }
        else
        {
            _sourcePanels.RowDefinitions.Add(new() { Height = GridLength.Auto }); _sourcePanels.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumn(_sourcePanelParts.Upload, 0); Grid.SetColumn(_sourcePanelParts.Repositories, 0); Grid.SetRow(_sourcePanelParts.Repositories, 1);
        }
    }

    private void RenderCatalogList(AdminPluginsState state, string query)
    {
        var job = _route.Get("catalog_job") ?? "";
        var jobs = P.CatalogJobs(state.Catalog.Select(entry => entry.Capabilities));
        var activeJob = jobs.Any(kind => kind.Job == job) ? job : "";
        var installedIds = state.Installations.Select(p => p.PluginId).ToHashSet(StringComparer.Ordinal);
        var searched = state.Catalog.Where(entry => P.Matches(query, entry.PluginId, entry.Presentation, entry.Capabilities, entry.SourceKind, entry.RepositoryName)).ToList();
        var visible = activeJob.Length > 0 ? searched.Where(entry => P.HasJob(entry.Capabilities, activeJob)).ToList() : searched;
        if (jobs.Count > 1)
        {
            Chip("", T.T("web.admin.plugins.list.filterAll"), null, activeJob.Length == 0, "PluginsJob_all", () => { ReplaceQuery("catalog_job", null); _shown = TilePage; RenderList(); });
            foreach (var kind in jobs) Chip(kind.Job, kind.Label, null, activeJob == kind.Job, "PluginsJob_" + kind.Job, () => { ReplaceQuery("catalog_job", kind.Job); _shown = TilePage; RenderList(); });
        }
        SetCount(visible.Count, state.Catalog.Count);
        var groups = Groups.Select(group => (group.Title, Entries: visible.Where(entry => entry.SourceKind == group.Kind)
            .OrderBy(entry => installedIds.Contains(entry.PluginId) ? 1 : 0).ThenBy(entry => P.Name(entry), Comparer<string>.Create((a, b) => string.Compare(a, b, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace)))
            .ToList())).Where(group => group.Entries.Count > 0).ToList();
        if (groups.Count == 0)
        {
            _boardList!.Children.Add(NoMatches(T.T("web.admin.plugins.catalog.noMatches"), () => { ReplaceQuery("catalog_q", null); ReplaceQuery("catalog_job", null); if (_search != null) _search.Text = ""; _shown = TilePage; RenderList(); }));
            return;
        }
        var budget = _shown;
        foreach (var group in groups)
        {
            if (budget <= 0) break;
            var section = Stack(10); var heading = new LogsWrapPanel { Spacing = 8 };
            heading.Children.Add(Ui.Heading(T.T(group.Title))); heading.Children.Add(Text(group.Entries.Count.ToString(), 13, true));
            AutomationProperties.SetName(section, T.T(group.Title)); section.Children.Add(heading);
            var tiles = group.Entries.Take(budget).Select(entry => CatalogTile(state, entry, installedIds.Contains(entry.PluginId))).ToList(); budget -= tiles.Count;
            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            foreach (var tile in tiles) grid.Children.Add(tile);
            _tileGrids.Add((grid, tiles)); LayoutGrid(grid, tiles); section.Children.Add(grid); _boardList!.Children.Add(section);
        }
        MoreButton(groups.Sum(group => group.Entries.Count));
    }

    private FrameworkElement CatalogTile(AdminPluginsState state, AdminPlugin entry, bool installed)
    {
        var name = P.Name(entry); var publisher = entry.Presentation?.PublisherName?.Trim();
        var content = Stack(10);
        // The group heading already names the tier.
        content.Children.Add(TileHead(name, string.IsNullOrEmpty(publisher) ? entry.Version : T.T("web.admin.plugins.tile.catalogSubline", new { publisher, version = entry.Version }), () => OpenDetail(entry.PluginId, entry)));
        content.Children.Add(TileSummary(P.Summary(entry.Presentation, entry.Capabilities)));
        FrameworkElement right;
        if (installed) right = Text("✓ " + T.T("web.admin.plugins.tile.installed"), 12, true);
        else
        {
            var installing = state.Busy && state.BusyKey == entry.PluginId;
            var install = AdminSettingsUi.Action(installing ? T.T("web.admin.plugins.tile.installing") : T.T("web.admin.plugins.tile.install"), "PluginInstall_" + entry.PluginId);
            AutomationProperties.SetName(install, T.T("web.admin.plugins.tile.installLabel", new { name }));
            install.IsEnabled = !state.Busy && !state.Loading && state.Ready(AdminPluginsSection.Installed);
            install.Click += (_, _) => _ = RunAsync(() => InstallAsync(entry));
            install.Tapped += (_, args) => args.Handled = true; right = install;
        }
        content.Children.Add(TileFooter(Capabilities(entry.Capabilities), right));
        var card = TileCard(content, false, installed);
        // Pointer users can click anywhere else on the card; the Install button and the name keep their own targets.
        card.Tapped += (_, args) => { if (!args.Handled) OpenDetail(entry.PluginId, entry); };
        AutomationProperties.SetAutomationId(card, "PluginTile_" + entry.PluginId);
        return card;
    }
    /// <summary>Installs the catalog version the admin confirms. Communities and external sources carry their notice here, since the tile skips the plugin page.</summary>
    private async Task InstallAsync(AdminPlugin entry)
    {
        var controller = _controller; if (controller == null) return;
        var pin = AdminPluginPin.Of(entry); var name = P.Name(entry); var notice = P.TierNotice(entry.SourceKind);
        var content = Stack(10);
        content.Children.Add(Text(T.T("web.admin.plugins.tile.confirmDescription", new { source = P.SourceLabel(entry.SourceKind), notice = notice ?? "" }).Trim(), 14));
        var facts = Stack(2);
        if (entry.Presentation?.PublisherName?.Trim() is { Length: > 0 } publisher) facts.Children.Add(Text(T.T("web.admin.plugins.detail.byPublisher", new { publisher }), 13, true));
        facts.Children.Add(Text(T.T("web.admin.plugins.detail.version", new { version = entry.Version }), 13, true));
        if (!string.IsNullOrEmpty(entry.RepositoryName) && entry.SourceKind == "external") facts.Children.Add(Text(T.T("web.admin.plugins.rail.repository") + ": " + entry.RepositoryName, 13, true));
        var kinds = P.CapabilityList(entry.Capabilities); if (kinds.Length > 0) facts.Children.Add(Text(kinds, 13, true));
        content.Children.Add(facts);
        if (!await ConfirmAsync(T.T("web.admin.plugins.tile.confirmTitle", new { name }), content, T.T("web.admin.plugins.tile.confirmInstall"), "PluginsInstallConfirmation")) return;
        var outcome = await controller.InstallAsync(pin);
        if (outcome != AdminPluginWriteOutcome.Done) RebuildNow();
    }

    // ── approved community catalog ─────────────────────────────────────────

    private void BuildCommunityControl(AdminPluginsState state)
    {
        if (state.Failures.TryGetValue(AdminPluginsSection.CatalogSettings, out var failure))
            _content.Children.Add(StaleNote(failure, "PluginsSettingsReload", AdminPluginsSection.CatalogSettings));
        var settings = state.CatalogSettings; if (settings == null) return;
        var status = state.CatalogStatus; var body = Stack(8);
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var texts = Stack(2); var label = Text(T.T("web.admin.plugins.communityCatalog.includeLabel"), 14); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; texts.Children.Add(label);
        texts.Children.Add(Text(T.T("web.admin.plugins.communityCatalog.description"), 13, true));
        if (status is { MigratedPluginCount: > 0 }) texts.Children.Add(Text(T.T("web.admin.plugins.communityCatalog.migrated", T.Count(status.MigratedPluginCount)), 13, true));
        row.Children.Add(texts);
        var toggle = new ToggleSwitch { IsOn = settings.Value.IncludeApprovedCommunityPlugins, OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = settings.CanWrite && !state.CatalogReviewRequired && !state.Busy && !state.Loading };
        AutomationProperties.SetName(toggle, T.T("web.admin.plugins.communityCatalog.includeLabel")); AutomationProperties.SetAutomationId(toggle, "PluginsCommunity");
        Grid.SetColumn(toggle, 1); row.Children.Add(toggle); body.Children.Add(row);
        if (state.CatalogReviewRequired)
        {
            // A conflicting change needs the settings read again before another edit; the way to do that stays on screen.
            var reload = Ui.Command(T.T("common.retry"), "PluginsSettingsReload", () => ReloadSection(AdminPluginsSection.CatalogSettings));
            reload.HorizontalAlignment = HorizontalAlignment.Left; reload.IsEnabled = !state.Loading; body.Children.Add(reload);
        }
        var saved = settings.Value.IncludeApprovedCommunityPlugins;
        toggle.Toggled += (_, _) => { if (!_syncing) _ = RunAsync(() => SetCommunityAsync(settings, toggle.IsOn, saved)); };
        _content.Children.Add(Panel(body, 12, 16, border: true));
    }

    private async Task SetCommunityAsync(AdminVersioned<AdminPluginCatalogSettings> reviewed, bool include, bool saved)
    {
        var controller = _controller; if (controller == null || include == saved) return;

        var installedCommunity = controller.State.CatalogStatus?.InstalledCommunityPluginCount ?? 0;
        if (!include && installedCommunity > 0 &&
            !await ConfirmAsync(T.T("web.admin.plugins.communityCatalog.hideTitle"), Text(T.T("web.admin.plugins.communityCatalog.hideDescription", T.Count(installedCommunity)), 14), T.T("web.admin.plugins.communityCatalog.hideConfirm"), "PluginsCommunityConfirmation"))
        { RebuildNow(); return; }
        var outcome = await controller.SetCommunityAsync(reviewed, include);
        if (outcome != AdminPluginWriteOutcome.Done) RebuildNow();
    }
}
