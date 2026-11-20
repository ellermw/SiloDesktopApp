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
    private readonly AdminUserResourcesApi dapi;
    private readonly AdminUserResourceHost dhost;
    private AdminRouteRequest droute = new(AdminRoute.AccessGroups);
    private IReadOnlyList<JsonElement> dgroups = [], dlibraries = [];
    private bool davailable, dlibrariesReady;
    private int dvisibleGroups = 60;
    private long dlistRevision;
    private int dcardColumns = 1;
    private readonly AdminLeaveGuard dleave;
    public AdminUserResourceController Session => dhost.Session;
    public AdminAccessGroupsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        Id(this, "AdminAccessGroupsPage"); dapi = App.Services.GetRequiredService<AdminUserResourcesApi>();
        dhost = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        dhost.StateChanged += UpdateActions;
        dleave = new(() => Session.Owner, () => Dirty, Discard);
        SizeChanged += (d, d) => { var columns = ActualWidth >= 1280 ? 3 : ActualWidth >= 768 ? 2 : 1; if (columns == dcardColumns) return; dcardColumns = columns; if (droute.Segments.Count == 0 && davailable && dgroups.Count > 0) RenderList(); };
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) droute = route; }
    public bool RequiresLeaveConfirmation => dleave.RequiresConfirmation;
    public Task<bool> CanLeaveAsync() => dleave.CanLeaveAsync(async d => await dhost.ConfirmAsync("Discard unsaved changes?", "Your changes have not been saved.", "Discard"));
    public void SuspendAdminPage() => dhost.Suspend();
    private static string G(string key, params (string Name, object? Value)[] args) => T("web.admin.access.groups." + key, args);

    private void Clear()
    {
        deditor = dlimit = null; ddraft = dbaseline = null; dlimitDraft = dlimitBase = null; dinvalid.Clear(); dbitrateText.Clear(); dmaxProfilesText = "";
        ddownloadSwitches = default; drequestDefaults = default; dlimitRequested = false;
        dmemberQuery = ""; dmemberVisible = 10; dlistRevision++; dmemberNotice = ""; dmemberFailures.Clear(); dmoveTarget = "";
        dgroups = dlibraries = daccounts = []; dselected.Clear(); daddRetry.Clear(); dwrites.Clear(); davailable = dlibrariesReady = daccountsReady = daccountsFailed = false;
        dhost.Recovery.Children.Clear();
    }

    private void SetContent(params UIElement[] elements)
    {
        dhost.Content.Children.Clear(); dwrites.Clear();
        foreach (var element in elements) dhost.Content.Children.Add(element);
    }
    /// <summary>A failed required read: the failure line above says why; this says what failed and offers the reload.</summary>
    private void ShowLoadFailed(string text, bool back = false)
    {
        var panel = Stack(8); if (back) panel.Children.Add(Button(G("allGroups"), "GroupBack", () => AdminShellPage.Open(new(AdminRoute.AccessGroups))));
        panel.Children.Add(Text(text)); panel.Children.Add(Button(T("common.retry"), "GroupRetry", () => d = dhost.ReloadAsync()));
        SetContent(panel); dhost.RaiseChanged();
    }

    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        SetContent(Text(G("loadingList")));
        var capabilities = await Session.ReadAsync("capabilities", owner => dapi.GroupCapabilitiesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        if (capabilities.ValueKind != JsonValueKind.Object) { ShowLoadFailed(G("loadFailed"), droute.Segment(0) != null); return; }
        davailable = capabilities.Flag("accessdgroups");
        if (!davailable) { SetContent(LogsNativeUi.Header(G("title"), G("subtitle")), Text(G("unavailable"))); return; }
        var groups = await Session.ReadAsync("groups", owner => dapi.ConfigurationAsync(owner, cursor => dapi.ListGroupsAsync(owner, cursor)));
        if (!Session.IsCurrent(pageOwner)) return;
        if (groups == null) { ShowLoadFailed(G("loadFailed"), droute.Segment(0) != null); return; }
        dgroups = groups.OrderBy(x => x.Text("name").ToLowerInvariant(), StringComparer.CurrentCulture).ThenBy(x => long.TryParse(x.Text("id"), out var id) ? id : long.MaxValue).ToArray();
        var selected = droute.Segment(0);
        if (selected == null) { RenderList(); return; }
        await LoadEditorAsync(pageOwner, selected);
    }

    // ---------- list ----------
    private void RenderList()
    {
        var revision = ++dlistRevision; dwrites.Clear();
        var elements = new List<UIElement> { LogsNativeUi.Header(G("title"), G("subtitle"), WriteButton(G("newGroup"), "GroupNew", () => d = CreateAsync(), accent: true)) };
        if (dgroups.Count == 0) elements.Add(Panel(Section(G("empty.title"), Text(G("empty.body")))));
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        for (var column = 0; column < dcardColumns; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var cardIndex = 0; var limitLabels = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        foreach (var group in dgroups.Take(dvisibleGroups))
        {
            var id = group.Text("id"); var card = Stack(10);
            var head = Inline(8); head.Children.Add(Text(group.Text("name"), 16)); if (group.Flag("isddefault")) head.Children.Add(Chip(G("card.default")));
            var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var title = Stack(2); title.Children.Add(head); if (group.Text("description") is { Length: > 0 } description) title.Children.Add(Text(description, 12, true));
            var members = Chip(G("card.memberCount", ("count", group.Number("memberdcount")), ("countdtext", group.Number("memberdcount")))); Grid.SetColumn(members, 1);
            top.Children.Add(title); top.Children.Add(members); card.Children.Add(top);
            var facts = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
            var libraries = group.Member("librarydids") is { ValueKind: JsonValueKind.Array } ids ? G("card.libraryCount", ("count", ids.GetArrayLength()), ("countdtext", ids.GetArrayLength())) : G("card.allLibraries");
            foreach (var fact in new[] { libraries, G(group.Flag("downloaddallowed") ? "card.downloadsOn" : "card.downloadsOff"),
                G("card.streams", ("count", Math.Max(group.Number("maxdstreams"), 0)), ("countdtext", Math.Max(group.Number("maxdstreams"), 0))), G(group.Flag("requestsdallowed") ? "card.requestsOn" : "card.requestsOff") })
                facts.Children.Add(Chip(fact));
            if (group.Flag("requestsdallowed")) { var terms = Chip(""); terms.Visibility = Visibility.Collapsed; facts.Children.Add(terms); limitLabels[id] = (TextBlock)terms.Child; }
            card.Children.Add(facts);
            var button = Id(new Button { Content = card, Padding = new(20), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new(16),
                Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new(1) }, "GroupCardd" + id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, group.Text("name"));
            button.Click += (d, d) => AdminShellPage.Open(AdminRouteRequest.AccessGroup(id));
            if (cardIndex % dcardColumns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(button, cardIndex / dcardColumns); Grid.SetColumn(button, cardIndex % dcardColumns); cardIndex++; grid.Children.Add(button);
        }
        elements.Add(grid);
        if (dgroups.Count > dvisibleGroups) elements.Add(Button("Show more groups", "GroupMoreCards", () => { dvisibleGroups += 60; RenderList(); }));
        SetContent([.. elements]); UpdateActions(); d = LoadCardLimitsAsync(revision, limitLabels);
    }
    /// <summary>The group's own approval and limit, when it sets any, on its card. A failed read leaves the card without that chip; nothing else is affected.</summary>
    private async Task LoadCardLimitsAsync(long revision, IReadOnlyDictionary<string, TextBlock> labels)
    {
        var pageOwner = Session.Owner;
        foreach (var batch in labels.Keys.Chunk(4))
        {
            if (!Session.IsCurrent(pageOwner) || revision != dlistRevision) return;
            await Task.WhenAll(batch.Select(async id =>
            {
                var limit = await Session.ReadAsync("card-limit-" + id, async owner => { try { return await dapi.GetGroupLimitAsync(owner, id); } catch (ApiException) { return null; } });
                if (limit == null || !Session.IsCurrent(pageOwner) || revision != dlistRevision) return;
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
        var created = await dhost.SaveDialogAsync(G("newGroup"), input, G("create.submit"), () => !string.IsNullOrWhiteSpace(input.Text), async () =>
        {
            var written = false;
            await Session.WriteAsync(async lease => { var group = await dapi.CreateGroupAsync(lease, input.Text); createdId = group.Text("id"); written = true; });
            return written;
        });
        // Do not pull the admin away if they moved on while the group was created.
        if (created && createdId != null && Session.IsCurrent(owner)) AdminShellPage.Open(AdminRouteRequest.AccessGroup(createdId));
    }

    private readonly List<Button> dwrites = [];
    private Button WriteButton(string label, string id, Action command, bool accent = false)
    {
        var owner = Session.Owner;
        var button = Button(label, id, () => { if (Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview) command(); }, accent);
        dwrites.Add(button); return button;
    }
}
