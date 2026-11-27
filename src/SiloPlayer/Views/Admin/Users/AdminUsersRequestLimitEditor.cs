using SiloPlayer.Controls;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Request quota (official <c>RequestLimitFields</c>): the approval and the limit of one account, each "use the default" or its own value, with what the default
/// resolves to under the box and the numbers of a custom limit. A limit of 0 stops new requests without blocking the account.
/// </summary>
internal sealed class RequestLimitEditor : AdminUsersCardEditor
{
    private readonly InheritedRequestTerms? _inherited;
    private readonly string? _groupName;
    private ComboBox _approval = null!, _limit = null!;
    private TextBox _max = null!, _window = null!;
    private TextBlock _approvalHint = null!, _limitHint = null!, _zeroHint = null!, _maxProblem = null!, _windowProblem = null!;
    private StackPanel _customRow = null!;
    private AdminRequestLimitDraft _draft = new("inherit", "inherit", "", "");
    private string _seedMax = "", _seedWindow = "";
    private bool _syncing, _legacy;

    internal RequestLimitEditor(JsonObject saved, AccessEditorContext context, InheritedRequestTerms? inherited, string? groupName, string seedMax, string seedWindow) : base(saved, context)
    {
        _inherited = inherited; _groupName = groupName; _seedMax = seedMax; _seedWindow = seedWindow;
        Labels["approval_mode"] = T("web.admin.common.requestLimits.approval");
        Labels["limit_mode"] = T("web.admin.common.requestLimits.limit"); Labels["max_requests"] = Labels["limit_mode"]; Labels["window_days"] = Labels["limit_mode"];
        Start();
    }

    private AdminRequestLimitDraft SavedDraft => AdminRequestLimitDraft.From(Saved);
    private string InheritLabel => _groupName != null ? T("web.admin.users.detail.access.requests.useGroupDefault") : T("web.admin.users.detail.access.requests.useServerDefault");

    protected override void Render(JsonObject start)
    {
        _legacy = Saved.Text("limit_mode") == "blocked" || Saved.Text("approval_mode") == "blocked";
        if (_legacy)
        {
            // A limit saved as blocked is a setting the editors no longer offer; resetting it to the default is its own action.
            var note = new StackPanel { Spacing = 6 };
            var title = Text(T("web.admin.users.detail.access.requests.legacyTitle"), 14); title.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            note.Children.Add(title); note.Children.Add(Text(T("web.admin.users.detail.access.requests.legacyBody"), 12, true));
            Root.Children.Add(new Border
            {
                Child = note, Padding = new(12, 10, 12, 10), CornerRadius = new(8), BorderThickness = new(1),
                Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.10), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.4),
            });
            return;
        }
        _draft = AdminRequestLimitDraft.From(start);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        _approval = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(_approval, "RequestApproval"); AutomationProperties.SetName(_approval, Labels["approval_mode"]);
        _approval.Items.Add(new ComboBoxItem { Content = InheritLabel, Tag = "inherit" });
        _approval.Items.Add(new ComboBoxItem { Content = T("web.admin.common.requestLimits.approvalManual"), Tag = "manual" });
        _approval.Items.Add(new ComboBoxItem { Content = T("web.admin.common.requestLimits.approvalAuto"), Tag = "auto" });
        _approvalHint = Text("", 12, true);
        grid.Children.Add(Field(Labels["approval_mode"], _approval, _approvalHint));

        _limit = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(_limit, "RequestLimit"); AutomationProperties.SetName(_limit, Labels["limit_mode"]);
        _limit.Items.Add(new ComboBoxItem { Content = InheritLabel, Tag = "inherit" });
        _limit.Items.Add(new ComboBoxItem { Content = T("web.admin.common.requestLimits.limitCustom"), Tag = "custom" });
        _limit.Items.Add(new ComboBoxItem { Content = T("web.admin.common.requestLimits.limitNone"), Tag = "unlimited" });
        _max = new TextBox { Width = 80, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
        _window = new TextBox { Width = 80, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
        AutomationProperties.SetName(_max, T("web.admin.common.requestLimits.maxRequestsLabel")); AutomationProperties.SetAutomationId(_max, "RequestLimitMax");
        AutomationProperties.SetName(_window, T("web.admin.common.requestLimits.windowDaysLabel")); AutomationProperties.SetAutomationId(_window, "RequestLimitWindow");
        _customRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _customRow.Children.Add(_max);
        _customRow.Children.Add(new TextBlock { Text = "requests per", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("SecondaryTextBrush"), FontSize = 14 });
        _customRow.Children.Add(_window);
        _customRow.Children.Add(new TextBlock { Text = "days", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("SecondaryTextBrush"), FontSize = 14 });
        _limitHint = Text("", 12, true); _zeroHint = Text(T("web.admin.common.requestLimits.zeroHint", ("subject", "account")), 12, true);
        _maxProblem = Problem(); _windowProblem = Problem();
        _maxProblem.HorizontalAlignment = _windowProblem.HorizontalAlignment = HorizontalAlignment.Left; _maxProblem.TextAlignment = _windowProblem.TextAlignment = TextAlignment.Left;
        var limit = Field(Labels["limit_mode"], _limit, _customRow, _limitHint, _zeroHint, _maxProblem, _windowProblem);
        Grid.SetColumn(limit, 1);
        grid.Children.Add(limit);
        AdaptiveColumns(grid);
        Root.Children.Add(grid);

        _syncing = true;
        Select(_approval, _draft.Approval); Select(_limit, _draft.Limit); _max.Text = _draft.MaxRequests; _window.Text = _draft.WindowDays;
        _syncing = false;
        _approval.SelectionChanged += (_, _) => { if (!_syncing && _approval.SelectedItem is ComboBoxItem { Tag: string tag }) { _draft = _draft with { Approval = tag }; Update(); } };
        _limit.SelectionChanged += (_, _) =>
        {
            if (_syncing || _limit.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            // Custom with nothing typed starts from the inherited limit, else the server's own.
            _draft = tag == "custom" && _draft.MaxRequests.Length == 0 && _draft.WindowDays.Length == 0
                ? _draft with { Limit = tag, MaxRequests = _seedMax, WindowDays = _seedWindow } : _draft with { Limit = tag };
            _syncing = true; _max.Text = _draft.MaxRequests; _window.Text = _draft.WindowDays; _syncing = false;
            Update();
        };
        _max.TextChanged += (_, _) => { if (!_syncing) { _draft = _draft with { MaxRequests = _max.Text }; Update(); } };
        _window.TextChanged += (_, _) => { if (!_syncing) { _draft = _draft with { WindowDays = _window.Text }; Update(); } };
        Describe();
    }

    private static void Select(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == tag);

    private void Update() { Describe(); Raise(); }

    private string Group(bool fromGroup, string value) => fromGroup
        ? T("web.admin.common.requestAccess.inheritedFromGroup", ("group", GroupLabel()), ("value", value))
        : _groupName != null ? T("web.admin.common.requestAccess.inheritedViaGroup", ("group", GroupLabel()), ("value", value)) : T("web.admin.common.requestAccess.inheritedFromServer", ("value", value));

    private string GroupLabel() => string.IsNullOrWhiteSpace(_groupName) ? T("web.admin.common.requestAccess.unknownGroup")
        : _groupName!.Trim().EndsWith("group", StringComparison.OrdinalIgnoreCase) ? _groupName.Trim() : T("web.admin.common.requestAccess.group", ("name", _groupName.Trim()));

    private static string PerWindow(long days) => T("web.admin.common.requestAccess.perWindow", ("days", days), ("days_text", days));

    private static string Quota(InheritedRequestTerms terms) => terms.Unlimited ? T("web.admin.common.requestAccess.noLimit")
        : terms.Max == 0 ? T("web.admin.common.requestAccess.noNewRequests", ("per_window", PerWindow(terms.Days)))
        : T("web.admin.common.requestAccess.quota", ("count", terms.Max), ("count_text", terms.Max), ("per_window", PerWindow(terms.Days)));

    private void Describe()
    {
        var approvalHint = _draft.Approval == "inherit" && _inherited != null
            ? Group(_inherited.ApprovalFromGroup, _inherited.AutoApprove ? T("web.admin.common.requestAccess.approvedAutomatically") : T("web.admin.common.requestAccess.adminApproves")) : "";
        var limitHint = _draft.Limit == "inherit" && _inherited != null ? Group(_inherited.QuotaFromGroup, Quota(_inherited)) : "";
        Say(_approvalHint, approvalHint, false); Say(_limitHint, limitHint, false);
        _customRow.Visibility = _draft.Custom ? Visibility.Visible : Visibility.Collapsed;
        _zeroHint.Visibility = _draft.IsZero ? Visibility.Visible : Visibility.Collapsed;
        Say(_maxProblem, _draft.Custom && !_draft.MaxOk ? T("web.admin.common.requestAccess.errorMaxRequests") : null);
        Say(_windowProblem, _draft.Custom && !_draft.WindowOk ? T("web.admin.common.requestAccess.errorWindowDays") : null);
    }

    private static void Say(TextBlock block, string text, bool _) { block.Text = text; block.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }

    protected override JsonObject Collect()
    {
        var body = new JsonObject();
        if (_legacy || _approval == null || !_draft.Valid) return body;
        foreach (var key in _draft.Changes(SavedDraft))
        {
            var value = _draft.Body()[key];
            body[key] = value?.DeepClone();
        }
        return body;
    }

    /// <summary>What a save sends: all four fields of the record, as the server expects, once something changed.</summary>
    internal JsonObject FullBody() => _draft.Body();

    protected override bool HasIncomplete() => !_legacy && _approval != null && !_draft.Valid;
    internal override bool Validate() => _draft.Valid;
}
