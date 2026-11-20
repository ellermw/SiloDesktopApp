using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using J = SiloPlayer.Core.Services.Admin.AdminRequestJson;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminRequestsPage
{
    private AdminRequestDocument? _general;
    private RequestForm? _generalDraft;
    private AdminRequestDocument? _routing;
    private AdminRequestRoutes? _routes;
    private IReadOnlyList<AdminRequestDocument> _servers = [];
    private JsonArray _plugins = new();
    private string? _serverLoadError, _pluginLoadError;
    private readonly StackPanel _settingsServers = Stack();
    private readonly StackPanel _settingsRouting = Stack();
    private async Task BuildSettingsAsync(MonitorAuthorityLease owner, long generation)
    {
        Header(S("web.admin.settings.requests.page.title"), null, Button(S("web.admin.settings.requests.page.queue"), "AdminRequestQueueLink", () => NavigateAsync(AdminRoute.RequestQueue)));
        var generalHost = Stack(U.Text("Loading…", 14, true)); _content.Children.Add(Card(generalHost));
        _settingsServers.Children.Clear(); _settingsRouting.Children.Clear();
        _content.Children.Add(_settingsServers);
        if (J.Flag(_capabilities, "routing")) _content.Children.Add(_settingsRouting);
        try
        {
            var general = await _api.GetSettingsAsync(owner);
            if (!Current(owner) || generation != _generation) return;
            _general = general; _generalDraft = new(general.Value); var form = _generalDraft;
            form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.general.label"), 20));
            form.Flag("requests_enabled", S("web.admin.settings.requests.general.allowRequests"), "AdminRequestsEnabled", S("web.admin.settings.requests.general.allowRequestsDescription"));
            var approval = Select("AdminRequestsApproval", [("true", S("web.admin.settings.requests.general.approvalAuto")), ("false", S("web.admin.settings.requests.general.approvalAdmin"))], J.Flag(form.Data, "global_auto_approval_enabled") ? "true" : "false");
            form.Panel.Children.Add(Field(S("web.admin.settings.requests.general.approval"), approval, S("web.admin.settings.requests.general.overridableDefault")));
            approval.SelectionChanged += (_, _) => form.NotifyChange();
            form.Readers.Add(() => form.Data["global_auto_approval_enabled"] = Selected(approval) == "true");
            form.Number("global_max_requests", S("web.admin.settings.requests.general.requestLimit"), "AdminRequestsLimit", 0, int.MaxValue, false);
            form.Panel.Children.Add(U.Text(S("web.admin.settings.requests.general.requestLimitDescription"), 12, true));
            form.Number("global_window_days", S("web.admin.settings.requests.general.limitWindow"), "AdminRequestsWindow", 1, int.MaxValue, false);
            if (form.Data.ContainsKey("watchlist_requests")) form.Flag("watchlist_requests", S("web.admin.settings.requests.general.watchlist"), "AdminRequestsWatchlist", S("web.admin.settings.requests.general.watchlistDescription"));
            form.Flag("force_dual_quality", S("web.admin.settings.requests.general.forceDual"), "AdminRequestsForceDual", S("web.admin.settings.requests.general.forceDualDescription"));
            var access = Button("Access groups", "AdminRequestsAccessGroups", () => NavigateAsync(AdminRoute.AccessGroups));
            access.IsEnabled = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.AccessGroups);
            form.Panel.Children.Add(access);
            var locked = false; var saving = false; var editor = new ContentControl { Content = form.Panel, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var save = Button(S("common.save"), "AdminRequestsGeneralSave", async () =>
            {
                if (locked || saving || _lifetime == null || _pendingWriteReview != null) return;
                using var action = _lifetime.CaptureAction(); saving = true; _busy = true; editor.IsEnabled = false;
                try
                {
                    var draft = form.Read();
                    var saved = await _api.SaveDocumentAsync(action, general, draft); action.ThrowIfRetired();
                    if (!Current(owner)) return; _general = saved; _dirty = false; Notice(""); await ReloadAsync();
                }
                catch (Exception error)
                {
                    if (Current(owner)) { Notice(AdminRequestRules.Error(error, true)); if (error is ApiException { StatusCode: 422 } validation) MarkValidationErrors(form.Panel, validation); locked = error is not ArgumentException && error is not ApiException { StatusCode: 422 }; }
                }
                finally { saving = false; if (Current(owner)) { _busy = false; editor.IsEnabled = true; } }
            }, true);
            var discard = Button("Discard", "AdminRequestsGeneralDiscard", async () => { _dirty = false; await ReloadAsync(); });
            var reload = Button("Reload and review current values", "AdminRequestsGeneralReload", async () =>
            {
                if (_dirty && !await ConfirmAsync("Discard unsaved changes?", "Reloading discards this draft and reads the current server values.", "Reload")) return;
                _dirty = false; await ReloadAsync();
            });
            form.Panel.Children.Add(reload); _saveBar.Child = Row(save, discard);
            generalHost.Children.Clear(); generalHost.Children.Add(editor);
            form.Changed += () => { if (Current(owner) && !saving) _dirty = true; };
        }
        catch (Exception error) { if (Current(owner) && generation == _generation) { generalHost.Children.Clear(); generalHost.Children.Add(U.Text(AdminRequestRules.Error(error), 13)); generalHost.Children.Add(Button("Try again", "AdminRequestsGeneralRetry", ReloadAsync)); } }
        if (!Current(owner) || generation != _generation) return;
        _serverLoadError = _pluginLoadError = null;
        try { var servers = await _api.ListIntegrationsAsync(owner); if (!Current(owner) || generation != _generation) return; _servers = servers; }
        catch (Exception error) { if (Current(owner)) _serverLoadError = AdminRequestRules.Error(error); }
        try { var plugins = await _api.ListPluginsAsync(owner); if (!Current(owner) || generation != _generation) return; _plugins = plugins; }
        catch (Exception error) { if (Current(owner)) _pluginLoadError = AdminRequestRules.Error(error); }
        if (!Current(owner) || generation != _generation) return;
        RenderServers();
        if (!J.Flag(_capabilities, "routing")) return;
        try
        {
            var routing = await _api.GetRoutingAsync(owner); var routes = await _api.ListRoutesAsync(owner);
            if (!Current(owner) || generation != _generation) return;
            _routing = routing; _routes = routes; RenderRouting();
        }
        catch (Exception error) { if (Current(owner) && generation == _generation) { _settingsRouting.Children.Add(U.Text(AdminRequestRules.Error(error), 13)); _settingsRouting.Children.Add(Button("Try again", "AdminRequestsRoutingRetry", RefreshConfigurationAsync)); } }
        try { var users = await _api.ListUsersAsync(owner); if (Current(owner) && generation == _generation) _users = users; } catch (Exception) { }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
