using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official AdminAccessGroups list, create, editor, delete and member workflows at 217ff25. No dependency on the Users page.</summary>
public sealed partial class AdminAccessGroupsPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminUserResourcesApi _api;
    private readonly AdminUserResourceHost _host;
    private AdminRouteRequest _route = new(AdminRoute.AccessGroups);
    private IReadOnlyList<JsonElement> _groups = [], _libraries = [];
    private bool _available, _librariesReady;
    private int _visibleGroups = 60;
    private long _listRevision;
    private int _cardColumns = 1;
    private bool _cardWide;
    private readonly AdminLeaveGuard _leave;
    public AdminUserResourceController Session => _host.Session;
    public AdminAccessGroupsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        Id(this, "AdminAccessGroupsPage"); _api = App.Services.GetRequiredService<AdminUserResourcesApi>();
        _host = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        _host.StateChanged += UpdateActions;
        _leave = new(() => Session.Owner, () => Dirty, Discard);
        SizeChanged += (_, _) =>
        {
            var columns = ActualWidth >= 1280 ? 3 : ActualWidth >= 768 ? 2 : 1; var wide = ActualWidth >= 1024;
            if (columns == _cardColumns && wide == _cardWide) return; _cardColumns = columns; _cardWide = wide;
            if (_route.Segments.Count == 0 && _available && _groups.Count > 0) RenderList();
        };
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) _route = route; }
    public bool RequiresLeaveConfirmation => _leave.RequiresConfirmation;
    public Task<bool> CanLeaveAsync() => _leave.CanLeaveAsync(async _ => await _host.ConfirmAsync("Discard unsaved changes?", "Your changes have not been saved.", "Discard"));
    public void SuspendAdminPage() => _host.Suspend();
    private static string G(string key, params (string Name, object? Value)[] args) => T("web.admin.access.groups." + key, args);

    private void Clear()
    {
        _editor = _limit = null; _draft = _baseline = null; _limitDraft = _limitBase = null; _invalid.Clear(); _bitrateText.Clear(); _maxProfilesText = "";
        _downloadSwitches = default; _requestDefaults = default; _limitFailed = false; _memberWrites.Clear();
        _memberQuery = ""; _memberVisible = 10; _listRevision++; _memberNotice = ""; _memberFailures.Clear(); _moveTarget = "";
        _groups = _libraries = _accounts = []; _selected.Clear(); _addRetry.Clear(); _writes.Clear(); _available = _librariesReady = _accountsReady = _accountsFailed = false;
        _host.Recovery.Children.Clear();
    }

    private void SetContent(params UIElement[] elements)
    {
        _host.Content.Children.Clear(); _writes.Clear();
        foreach (var element in elements) _host.Content.Children.Add(element);
    }
    /// <summary>A failed required read: the failure line above says why; this says what failed and offers the reload.</summary>
    private void ShowLoadFailed(string text, bool back = false)
    {
        var panel = Stack(8); if (back) panel.Children.Add(Button(G("allGroups"), "GroupBack", () => AdminShellPage.Open(new(AdminRoute.AccessGroups))));
        panel.Children.Add(Text(text)); panel.Children.Add(Button(T("common.retry"), "GroupRetry", () => _ = _host.ReloadAsync()));
        SetContent(panel); _host.RaiseChanged();
    }

    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        SetContent(Text(G("loadingList")));
        var capabilities = await Session.ReadAsync("capabilities", owner => _api.GroupCapabilitiesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        if (capabilities.ValueKind != JsonValueKind.Object) { ShowLoadFailed(G("loadFailed"), _route.Segment(0) != null); return; }
        _available = capabilities.Flag("access_groups");
        if (!_available) { SetContent(LogsNativeUi.Header(G("title"), G("subtitle")), Text(G("unavailable"))); return; }
        var groups = await Session.ReadAsync("groups", owner => _api.ConfigurationAsync(owner, cursor => _api.ListGroupsAsync(owner, cursor)));
        if (!Session.IsCurrent(pageOwner)) return;
        if (groups == null) { ShowLoadFailed(G("loadFailed"), _route.Segment(0) != null); return; }
        _groups = groups.OrderBy(x => x.Text("name").ToLowerInvariant(), StringComparer.CurrentCulture).ThenBy(x => long.TryParse(x.Text("id"), out var id) ? id : long.MaxValue).ToArray();
        var selected = _route.Segment(0);
        if (selected == null) { RenderList(); return; }
        await LoadEditorAsync(pageOwner, selected);
    }

    // ---------- list ----------
    private void RenderList()
    {
        var revision = ++_listRevision; _writes.Clear();
        var elements = new List<UIElement> { LogsNativeUi.Header(G("title"), G("subtitle"), WriteButton(G("newGroup"), "GroupNew", () => _ = CreateAsync(), accent: true)) };
        if (_groups.Count == 0) elements.Add(Panel(Section(G("empty.title"), Text(G("empty.body")))));
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        for (var column = 0; column < _cardColumns; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var cardIndex = 0; var limitLabels = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        foreach (var group in _groups.Take(_visibleGroups))
        {
            var id = group.Text("id"); var card = Stack(10);
            // Official card: the name is one cut line from the lg breakpoint up and at most two lines below it; the default mark and the member count are never cut.
            var nameText = _cardWide ? Truncated(group.Text("name"), 16) : Clamped(group.Text("name"), 2, 16);
            var head = new NameWithMark(nameText, group.Flag("is_default") ? Chip(G("card.default")) : null) { HorizontalAlignment = HorizontalAlignment.Left };
            var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var title = Stack(2); title.Children.Add(head); if (group.Text("description") is { Length: > 0 } description) title.Children.Add(Clamped(description, 2, 12, true));
            var members = Chip(G("card.memberCount", ("count", group.Number("member_count")), ("count_text", group.Number("member_count")))); Grid.SetColumn(members, 1);
            top.Children.Add(title); top.Children.Add(members); card.Children.Add(top);
            var facts = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
            var libraries = group.Member("library_ids") is { ValueKind: JsonValueKind.Array } ids ? G("card.libraryCount", ("count", ids.GetArrayLength()), ("count_text", ids.GetArrayLength())) : G("card.allLibraries");
            foreach (var fact in new[] { libraries, G(group.Flag("download_allowed") ? "card.downloadsOn" : "card.downloadsOff"),
                G("card.streams", ("count", Math.Max(group.Number("max_streams"), 0)), ("count_text", Math.Max(group.Number("max_streams"), 0))), G(group.Flag("requests_allowed") ? "card.requestsOn" : "card.requestsOff") })
                facts.Children.Add(Chip(fact));
            if (group.Flag("requests_allowed")) { var terms = Chip(""); terms.Visibility = Visibility.Collapsed; facts.Children.Add(terms); limitLabels[id] = (TextBlock)terms.Child; }
            card.Children.Add(facts);
            var button = Id(new Button { Content = card, Padding = new(20), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new(16),
                Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new(1) }, "GroupCard_" + id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, AdminNameText.Label(group.Text("name"), 200));
            button.Click += (_, _) => AdminShellPage.Open(AdminRouteRequest.AccessGroup(id));
            if (cardIndex % _cardColumns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(button, cardIndex / _cardColumns); Grid.SetColumn(button, cardIndex % _cardColumns); cardIndex++; grid.Children.Add(button);
        }
        elements.Add(grid);
        if (_groups.Count > _visibleGroups) elements.Add(Button("Show more groups", "GroupMoreCards", () => { _visibleGroups += 60; RenderList(); }));
        SetContent([.. elements]); UpdateActions(); _ = LoadCardLimitsAsync(revision, limitLabels);
    }
    /// <summary>The group's own approval and limit, when it sets any, on its card. A failed read leaves the card without that chip; nothing else is affected.</summary>
    private async Task LoadCardLimitsAsync(long revision, IReadOnlyDictionary<string, TextBlock> labels)
    {
        var pageOwner = Session.Owner;
        foreach (var batch in labels.Keys.Chunk(4))
        {
            if (!Session.IsCurrent(pageOwner) || revision != _listRevision) return;
            await Task.WhenAll(batch.Select(async id =>
            {
                var limit = await Session.ReadAsync("card-limit-" + id, async owner => { try { return await _api.GetGroupLimitAsync(owner, id); } catch (ApiException) { return null; } });
                if (limit == null || !Session.IsCurrent(pageOwner) || revision != _listRevision) return;
                var summary = AdminRequestAccess.GroupSummary(AdminRequestLayer.From(limit.Value));
                if (summary.Length == 0) return;
                labels[id].Text = summary; if (labels[id].Parent is Border border) border.Visibility = Visibility.Visible;
            }));
        }
    }

    private async Task CreateAsync()
    {
        var owner = Session.Owner; var input = Input(G("create.nameLabel"), "NewGroupName", ""); input.PlaceholderText = G("create.placeholder");
        string? createdId = null;
        var created = await _host.SaveDialogAsync(G("newGroup"), input, G("create.submit"), () => !string.IsNullOrWhiteSpace(input.Text), async () =>
        {
            var written = false;
            await Session.WriteAsync(async lease => { var group = await _api.CreateGroupAsync(lease, input.Text); createdId = group.Text("id"); written = true; });
            return written;
        });
        // Do not pull the admin away if they moved on while the group was created.
        if (created && createdId != null && Session.IsCurrent(owner)) AdminShellPage.Open(AdminRouteRequest.AccessGroup(createdId));
    }

    private readonly List<Button> _writes = [];
    private Button WriteButton(string label, string id, Action command, bool accent = false)
    {
        var owner = Session.Owner;
        var button = Button(label, id, () => { if (Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview) command(); }, accent);
        _writes.Add(button); return button;
    }
}
