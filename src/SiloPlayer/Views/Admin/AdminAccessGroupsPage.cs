using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official AdminAccessGroups + editor/member workflows at 217ff25. No dependency on the pending Users page.</summary>
public sealed partial class AdminAccessGroupsPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminUserResourcesApi _api;
    private readonly AdminUserResourceHost _host;
    private AdminRouteRequest _route = new(AdminRoute.AccessGroups);
    private AdminResourceDocument? _editor, _limit;
    private JsonObject? _draft, _limitDraft;
    private string _baseline = "", _limitBaseline = "";
    private IReadOnlyList<JsonElement> _groups = [], _libraries = [], _accounts = [];
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly List<Button> _writes = [];
    private bool _available, _librariesReady, _accountsReady;
    private bool _invalidNumber;
    private (bool DownloadsOff, bool ConversionOff) _downloadSwitches;
    private JsonElement _requestDefaults;
    private int _memberCount = 10;
    private int _visibleGroups = 60;
    private long _listRevision;
    private int _cardColumns = 1;
    private string _memberQuery = "";
    private readonly AdminLeaveGuard _leave;
    public AdminUserResourceController Session => _host.Session;
    public AdminAccessGroupsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        Id(this, "AdminAccessGroupsPage"); _api = App.Services.GetRequiredService<AdminUserResourcesApi>();
        _host = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        _host.StateChanged += UpdateActions;
        _leave = new(() => Session.Owner, () => Dirty, Discard);
        SizeChanged += (_, _) => { var columns = ActualWidth >= 1280 ? 3 : ActualWidth >= 768 ? 2 : 1; if (columns == _cardColumns) return; _cardColumns = columns; if (_route.Segments.Count == 0 && _available && _groups.Count > 0) RenderList(); };
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) _route = route; }
    private bool Dirty => _draft != null && (_invalidNumber || _draft.ToJsonString() != _baseline || _limitDraft?.ToJsonString() != _limitBaseline);
    public bool RequiresLeaveConfirmation => _leave.RequiresConfirmation;
    public Task<bool> CanLeaveAsync() => _leave.CanLeaveAsync(async _ => await _host.ConfirmAsync("Discard unsaved changes?", "Your changes have not been saved.", "Discard"));
    public void SuspendAdminPage() => _host.Suspend();
    private void Clear()
    {
        _editor = _limit = null; _draft = _limitDraft = null; _baseline = _limitBaseline = "";
        _downloadSwitches = default; _requestDefaults = default;
        _memberQuery = ""; _memberCount = 10; _listRevision++;
        _groups = _libraries = _accounts = []; _selected.Clear(); _writes.Clear(); _numberErrors.Clear(); _numberText.Clear(); _available = _librariesReady = _accountsReady = false; _invalidNumber = false;
    }
    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        _host.Content.Children.Clear(); _host.Content.Children.Add(Text(G("loadingList")));
        var capabilities = await Session.ReadAsync("capabilities", owner => _api.GroupCapabilitiesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        _available = capabilities.ValueKind == JsonValueKind.Object && capabilities.Flag("access_groups");
        if (!_available) { _host.Content.Children.Clear(); _host.Content.Children.Add(Text(G("unavailable"))); return; }
        var groups = await Session.ReadAsync("groups", owner => _api.ConfigurationAsync(owner, cursor => _api.ListGroupsAsync(owner, cursor)));
        if (groups == null || !Session.IsCurrent(pageOwner)) return;
        _groups = groups;
        var id = _route.Segment(0);
        if (id == null) { RenderList(); return; }
        var editor = await Session.ReadAsync("editor", lease => _api.GetGroupAsync(lease, id));
        if (editor == null || !Session.IsCurrent(pageOwner)) return; _editor = editor;
        var limit = await Session.ReadAsync("limit", lease => _api.GetGroupLimitAsync(lease, id));
        if (!Session.IsCurrent(pageOwner)) return; _limit = limit;
        var switches = await Session.ReadAsync("download-switches", lease => _api.DownloadSwitchesAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return; _downloadSwitches = switches;
        var requestDefaults = await Session.ReadAsync("request-defaults", lease => _api.RequestDefaultsAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return; _requestDefaults = requestDefaults;
        var libraries = await Session.ReadAsync("libraries", owner => _api.ListLibrariesAsync(owner));
        if (!Session.IsCurrent(pageOwner)) return;
        _librariesReady = libraries is { NextCursor: null }; _libraries = libraries?.Items ?? [];
        _draft = GroupBody(_editor.Value); _baseline = _draft.ToJsonString();
        _limitDraft = _limit == null ? null : LimitBody(_limit.Value); _limitBaseline = _limitDraft?.ToJsonString() ?? "";
        RenderEditor();
        var accounts = await Session.ReadAsync("members", owner => _api.ConfigurationAsync(owner, cursor => _api.ListAccountsAsync(owner, cursor)));
        if (!Session.IsCurrent(pageOwner)) return;
        _accounts = accounts ?? []; _accountsReady = accounts != null;
        if (Session.Owner is { IsCurrent: true } && _draft != null) RenderMembers();
    }
    private static string G(string key, params (string Name, object? Value)[] args) => T("web.admin.access.groups." + key, args);
    private void RenderList()
    {
        var revision = ++_listRevision;
        _host.Content.Children.Clear(); _writes.Clear();
        var add = WriteButton(G("newGroup"), "GroupNew", () => _ = CreateAsync());
        _host.Content.Children.Add(LogsNativeUi.Header(G("title"), G("subtitle"), add));
        if (_groups.Count == 0) _host.Content.Children.Add(Panel(Section(G("empty.title"), Text(G("empty.body")))));
        var list = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        for (var column = 0; column < _cardColumns; column++) list.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var cardIndex = 0;
        var limitLabels = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        foreach (var group in _groups.Take(_visibleGroups))
        {
            var id = group.Text("id");
            var card = Stack(8); card.Children.Add(Text(group.Text("name"), 18));
            if (group.Flag("is_default")) card.Children.Add(Text(G("card.default"), 12, true));
            if (group.Text("description") is { Length: > 0 } description) card.Children.Add(Text(description, 12, true));
            card.Children.Add(Text(G("card.memberCount", ("count", group.Number("member_count")), ("count_text", group.Number("member_count"))), 12));
            card.Children.Add(Text(group.Member("library_ids") is { ValueKind: JsonValueKind.Array } ids
                ? G("card.libraryCount", ("count", ids.GetArrayLength()), ("count_text", ids.GetArrayLength())) : G("card.allLibraries"), 12, true));
            card.Children.Add(Text(G(group.Flag("download_allowed") ? "card.downloadsOn" : "card.downloadsOff") + " · " +
                G(group.Flag("requests_allowed") ? "card.requestsOn" : "card.requestsOff") + " · " + G("card.streams", ("count", group.Number("max_streams")), ("count_text", group.Number("max_streams"))), 12, true));
            if (group.Flag("requests_allowed")) { var terms = Text("", 12, true); card.Children.Add(terms); limitLabels[id] = terms; }
            var button = Button(group.Text("name"), "GroupCard_" + id, () => AdminShellPage.Open(AdminRouteRequest.AccessGroup(id)));
            button.Content = card; button.Padding = new(20); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (cardIndex % _cardColumns == 0) list.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(button, cardIndex / _cardColumns); Grid.SetColumn(button, cardIndex % _cardColumns); cardIndex++; list.Children.Add(button);
        }
        _host.Content.Children.Add(list);
        if (_groups.Count > _visibleGroups) _host.Content.Children.Add(Button("Show more groups", "GroupMoreCards", () => { _visibleGroups += 60; RenderList(); }));
        UpdateActions(); _ = LoadCardLimitsAsync(revision, limitLabels);
    }
    private async Task LoadCardLimitsAsync(long revision, IReadOnlyDictionary<string, TextBlock> labels)
    {
        var pageOwner = Session.Owner;
        foreach (var batch in labels.Keys.Chunk(4))
        {
            if (!Session.IsCurrent(pageOwner) || revision != _listRevision) return;
            await Task.WhenAll(batch.Select(async id =>
            {
                var limit = await Session.ReadAsync("card-limit-" + id, async owner =>
                { try { return await _api.GetGroupLimitAsync(owner, id); } catch (ApiException) { return null; } });
                if (limit != null && Session.IsCurrent(pageOwner) && revision == _listRevision)
                {
                    var value = limit.Value; var parts = new List<string>();
                    if (value.Text("approval_mode") == "manual") parts.Add(T("web.admin.common.requestLimits.approvalManual"));
                    if (value.Text("approval_mode") == "auto") parts.Add(T("web.admin.common.requestLimits.approvalAuto"));
                    if (value.Text("limit_mode") == "unlimited") parts.Add(T("web.admin.common.requestLimits.limitNone"));
                    if (value.Text("limit_mode") == "custom") parts.Add(value.Number("max_requests") + " requests / " + value.Number("window_days") + " days");
                    labels[id].Text = string.Join(" · ", parts);
                }
            }));
        }
    }
    private async Task CreateAsync()
    {
        var owner = Session.Owner; var input = Input(G("create.nameLabel"), "NewGroupName", "");
        input.PlaceholderText = G("create.placeholder");
        if (!await _host.DialogAsync(G("newGroup"), input, G("create.submit"), () => !string.IsNullOrWhiteSpace(input.Text))) return;
        JsonElement created = default;
        if (await Session.WriteAsync(async lease => created = await _api.CreateGroupAsync(lease, input.Text)) && Session.IsCurrent(owner))
            AdminShellPage.Open(AdminRouteRequest.AccessGroup(created.Text("id")));
    }
    private readonly StackPanel _members = Stack();
    private void RenderEditor()
    {
        if (_editor == null || _draft == null) return;
        _numberErrors.Clear(); _invalidNumber = false;
        _host.Content.Children.Clear(); _writes.Clear();
        _host.Content.Children.Add(Button(G("allGroups"), "GroupBack", () => AdminShellPage.Open(new(AdminRoute.AccessGroups))));
        _host.Content.Children.Add(Panel(Section(G("title"),
            Field("name", "field.name", "GroupName"), Field("description", "field.description", "GroupDescription"),
            Toggle(G("default.label"), B("is_default"), value => Set("is_default", value), !_editor.Value.Flag("is_default")),
            Text(G(_editor.Value.Flag("is_default") ? "default.descriptionLocked" : "default.description"), 12, true))));
        var playback = Section(G("playback.heading"));
        var allLibraries = Toggle(G("card.allLibraries"), _draft["library_ids"] == null, value => { _draft["library_ids"] = value ? null : new JsonArray(); UpdateActions(); RenderEditor(); });
        allLibraries.IsEnabled = _librariesReady; playback.Children.Add(allLibraries);
        if (!_librariesReady) playback.Children.Add(Text("Libraries could not be loaded completely. Reload before changing library access.", 12));
        if (_draft["library_ids"] is JsonArray selectedLibraries)
            foreach (var library in _libraries)
            {
                var id = library.Text("id"); var check = Toggle(library.Text("name"), selectedLibraries.Any(x => x?.GetValue<string>() == id), value =>
                { var existing = selectedLibraries.FirstOrDefault(x => x?.GetValue<string>() == id); if (value && existing == null) selectedLibraries.Add(id); else if (!value && existing != null) selectedLibraries.Remove(existing); UpdateActions(); });
                check.IsEnabled = _librariesReady; playback.Children.Add(check);
            }
        var quality = S("max_playback_quality");
        playback.Children.Add(Select(G("playback.quality"), [("", T("web.profiles.helpers.quality.any") + " · " + T("web.profiles.helpers.quality.anyDescription")), ("1080p", T("web.profiles.helpers.quality.standard") + " · " + T("web.profiles.helpers.quality.standardDescription")), ("2160p", "4K · " + T("web.profiles.helpers.quality.fourKDescription"))], quality, value => Set("max_playback_quality", value)));
        playback.Children.Add(NumberField("max_remote_stream_bitrate_kbps", "playback.remoteBitrate"));
        playback.Children.Add(Text(G("playback.remoteBitrateHint"), 12, true));
        playback.Children.Add(NumberField("max_local_stream_bitrate_kbps", "playback.localBitrate"));
        playback.Children.Add(Text(G("playback.localBitrateHint"), 12, true));
        _host.Content.Children.Add(Panel(playback));
        _host.Content.Children.Add(Panel(Section(G("downloads.heading"),
            Toggle(G("downloads.allow"), B("download_allowed"), value => { Set("download_allowed", value); RenderEditor(); }),
            Text(G("downloads.allowDescription"), 12, true),
            Toggle(G("downloads.transcoded"), B("download_transcode_allowed"), value => Set("download_transcode_allowed", value), B("download_allowed")),
            Text(G("downloads.transcodedDescription"), 12, true))));
        if (_downloadSwitches.DownloadsOff || _downloadSwitches.ConversionOff)
        {
            _host.Content.Children.Add(Text(T(_downloadSwitches.DownloadsOff ? "web.admin.common.downloadAccess.serverOff" : "web.admin.common.downloadAccess.conversionOff"), 12, true));
            var settings = Button("Settings → Downloads", "GroupDownloadSettings", () => AdminShellPage.Open(new(AdminRoute.DownloadSettings)));
            settings.IsEnabled = AdminShellPage.CanOpen(new(AdminRoute.DownloadSettings)); _host.Content.Children.Add(settings);
        }
        RenderRequestLimit();
        _host.Content.Children.Add(Panel(Section(G("streams.heading"), NumberField("max_streams", "streams.max"), NumberField("max_transcodes", "streams.maxTranscodes"),
            Text(G("streams.zeroUnlimited"), 12, true),
            Toggle(G("streams.videoTranscode"), B("transcode_allowed"), value => Set("transcode_allowed", value)), Text(G("streams.videoTranscodeDescription"), 12, true),
            Toggle(G("streams.audioTranscode"), B("audio_transcode_allowed"), value => Set("audio_transcode_allowed", value)), Text(G("streams.audioTranscodeDescription"), 12, true))));
        _host.Content.Children.Add(Panel(Section(G("profiles.heading"), NumberField("max_profiles", "profiles.max", 1), Text(G("profiles.help"), 12, true))));
        var permissions = Section(G("permissions.heading"), Text(G("permissions.help"), 12, true),
            Toggle(G("permissions.allowAll"), _draft["allowed_permissions"] == null, value => { _draft["allowed_permissions"] = value ? null : new JsonArray("metadata_curation", "marker_edit"); UpdateActions(); RenderEditor(); }));
        if (_draft["allowed_permissions"] is JsonArray mask)
            foreach (var (permission, key) in new[] { ("metadata_curation", "metadataCuration"), ("marker_edit", "markerEdit") })
                permissions.Children.Add(Toggle(G("permissions." + key), mask.Any(x => x?.GetValue<string>() == permission), value =>
                { var existing = mask.FirstOrDefault(x => x?.GetValue<string>() == permission); if (value && existing == null) mask.Add(permission); else if (!value && existing != null) mask.Remove(existing); UpdateActions(); }));
        _host.Content.Children.Add(Panel(permissions));
        // Detach the reusable member region before assigning it to a new parent.
        if (_members.Parent is Panel old) old.Children.Remove(_members);
        _host.Content.Children.Add(_members); RenderMembers();
        var commands = Stack(8);
        commands.Children.Add(WriteButton(G("save"), "GroupSave", () => _ = SaveAsync()));
        commands.Children.Add(Button("Discard", "GroupDiscard", Discard));
        var delete = WriteButton(G("delete.button"), "GroupDelete", () => _ = DeleteAsync());
        delete.Tag = "delete"; commands.Children.Add(delete);
        if (_editor.Value.Flag("is_default")) commands.Children.Add(Text(G("delete.defaultLocked"), 12, true));
        _host.Content.Children.Add(commands); UpdateActions();
    }
    private void RenderRequestLimit()
    {
        var section = Section(G("requests.heading"), Text(G("requests.intro"), 12, true),
            Toggle(G("requests.allow"), B("requests_allowed"), value => Set("requests_allowed", value)), Text(G("requests.allowDescription"), 12, true));
        if (_limitDraft == null) section.Children.Add(Text(G("requests.loadFailed"), 12));
        else
        {
            section.Children.Add(Select(T("web.admin.common.requestLimits.approval"), [("inherit", G("requests.inheritLabel")), ("manual", T("web.admin.common.requestLimits.approvalManual")), ("auto", T("web.admin.common.requestLimits.approvalAuto"))], LS("approval_mode"), value => { _limitDraft["approval_mode"] = value; UpdateActions(); }));
            section.Children.Add(Select(T("web.admin.common.requestLimits.limit"), [("inherit", G("requests.inheritLabel")), ("unlimited", T("web.admin.common.requestLimits.limitNone")), ("custom", T("web.admin.common.requestLimits.limitCustom"))], LS("limit_mode"), value => { _limitDraft["limit_mode"] = value; UpdateActions(); RenderEditor(); }));
            if (LS("limit_mode") == "custom")
            {
                section.Children.Add(LimitNumber("max_requests", T("web.admin.common.requestLimits.maxRequestsLabel"), 0)); section.Children.Add(LimitNumber("window_days", T("web.admin.common.requestLimits.windowDaysLabel"), 1));
                if (_limitDraft["max_requests"]?.ToString() == "0") section.Children.Add(Text(T("web.admin.common.requestLimits.zeroHint", ("subject", "group")), 12, true));
            }
            if (_requestDefaults.ValueKind == JsonValueKind.Object)
                section.Children.Add(Text("Server default · " + RequestTerms(_requestDefaults, null), 12, true));
        }
        _host.Content.Children.Add(Panel(section));
    }
    private Button WriteButton(string label, string id, Action command)
    {
        var owner = Session.Owner;
        var button = Button(label, id, () => { if (Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview) command(); });
        _writes.Add(button); return button;
    }
    private void UpdateActions()
    {
        foreach (var button in _writes)
        {
            var save = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "GroupSave";
            button.IsEnabled = _available && Session.Owner is { IsCurrent: true } && !Session.Busy && !Session.NeedsReview &&
                (!save || _editor?.CanWrite == true && _limit?.CanWrite == true && !_invalidNumber && Dirty) &&
                (button.Tag as string != "delete" || _editor is { CanWrite: true } && !_editor.Value.Flag("is_default"));
        }
    }
    private TextBox Field(string key, string label, string id)
    {
        var draft = _draft; var owner = Session.Owner;
        var input = Input(G(label), id, S(key)); input.TextChanged += (_, _) => { if (!Session.IsCurrent(owner) || !ReferenceEquals(draft, _draft)) return; draft![key] = input.Text.Replace('\r', ' ').Replace('\n', ' '); UpdateActions(); }; return input;
    }
    private readonly Dictionary<string, bool> _numberErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _numberText = new(StringComparer.Ordinal);
    private TextBox NumberField(string key, string label, int minimum = 0) => IntegerInput(key, G(label), _draft!, minimum);
    private TextBox LimitNumber(string key, string label, int minimum) => IntegerInput(key, label, _limitDraft!, minimum);
    private TextBox IntegerInput(string key, string label, JsonObject body, int minimum)
    {
        var owner = Session.Owner;
        var input = Input(label, key, _numberText.GetValueOrDefault(key) ?? body[key]?.ToString() ?? "");
        void Validate()
        {
            if (!Session.IsCurrent(owner) || !ReferenceEquals(body, _draft) && !ReferenceEquals(body, _limitDraft)) return;
            _numberText[key] = input.Text;
            var valid = int.TryParse(input.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= minimum;
            _numberErrors[key] = !valid; _invalidNumber = _numberErrors.Values.Any(x => x);
            if (valid) body[key] = n; UpdateActions();
        }
        input.TextChanged += (_, _) => Validate(); Validate(); return input;
    }
    private void Set(string key, bool value) { _draft![key] = value; UpdateActions(); }
    private void Set(string key, string value) { _draft![key] = value; UpdateActions(); }
    private string S(string key) => _draft?[key]?.GetValue<string>() ?? "";
    private bool B(string key) => _draft?[key]?.GetValue<bool>() == true;
    private string LS(string key) => _limitDraft?[key]?.GetValue<string>() ?? "";
    private static JsonObject GroupBody(JsonElement value)
    {
        var result = new JsonObject();
        foreach (var key in new[] { "name", "description", "library_ids", "max_playback_quality", "download_allowed", "download_transcode_allowed", "transcode_allowed", "audio_transcode_allowed", "max_streams", "max_transcodes", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps", "max_profiles", "allowed_permissions", "requests_allowed", "is_default" })
            result[key] = value.Member(key) is { } child ? JsonNode.Parse(child.GetRawText()) : null;
        result["max_playback_quality"] = value.Text("max_playback_quality") switch { "standard" or "480p" or "720p" or "1080p" => "1080p", "4k" or "uhd" or "2160p" or "4320p" => "2160p", _ => "" };
        result["download_transcode_allowed"] = value.Flag("download_allowed") && value.Flag("download_transcode_allowed");
        return result;
    }
    private static JsonObject LimitBody(JsonElement value)
    {
        var result = new JsonObject(); foreach (var key in new[] { "limit_mode", "max_requests", "window_days", "approval_mode" }) result[key] = value.Member(key) is { } child ? JsonNode.Parse(child.GetRawText()) : null; return result;
    }
    private void Discard()
    {
        if (_editor == null || Session.Busy) return;
        _draft = GroupBody(_editor.Value); _baseline = _draft.ToJsonString(); _limitDraft = _limit == null ? null : LimitBody(_limit.Value);
        _limitBaseline = _limitDraft?.ToJsonString() ?? ""; _numberErrors.Clear(); _numberText.Clear(); _invalidNumber = false; RenderEditor();
    }
    private async Task SaveAsync()
    {
        if (_editor == null || _draft == null || _limit == null || _limitDraft == null || _invalidNumber) return;
        var editor = _editor; var limit = _limit; var owner = Session.Owner;
        var body = (JsonObject)_draft.DeepClone(); body["name"] = S("name").Trim(); body["description"] = S("description").Trim();
        body["download_transcode_allowed"] = B("download_allowed") && B("download_transcode_allowed");
        var limitBody = (JsonObject)_limitDraft.DeepClone();
        if (limitBody["limit_mode"]?.GetValue<string>() != "custom") { limitBody["max_requests"] = null; limitBody["window_days"] = null; }
        if (await Session.WriteAsync(async lease =>
        {
            if (body.ToJsonString() != _baseline)
            {
                var saved = await _api.SaveGroupAsync(lease, editor, JsonSerializer.SerializeToElement(body));
                if (!Session.IsCurrent(owner)) return;
                _editor = saved; _baseline = body.ToJsonString(); // A later limit failure never resends this group write.
            }
            if (limitBody.ToJsonString() != _limitBaseline)
            {
                var saved = await _api.SaveGroupLimitAsync(lease, limit, JsonSerializer.SerializeToElement(limitBody));
                if (!Session.IsCurrent(owner)) return; _limit = saved; _limitBaseline = limitBody.ToJsonString();
            }
        })) { _draft = body; _limitDraft = limitBody; AdminShellPage.Open(new(AdminRoute.AccessGroups)); }
    }
    private async Task DeleteAsync()
    {
        var editor = _editor; if (editor == null || editor.Value.Flag("is_default")) return;
        var group = editor.Value; var row = _groups.FirstOrDefault(x => x.Text("id") == group.Text("id"));
        var destination = _groups.FirstOrDefault(x => x.Flag("is_default") && x.Text("id") != group.Text("id")).Text("name");
        if (!await _host.ConfirmAsync(G("delete.title", ("name", group.Text("name"))), G("delete.description", ("members", row.ValueKind == JsonValueKind.Undefined ? "unknown" : row.Number("member_count") == 0 ? "none" : "some"), ("count", row.Number("member_count")), ("count_text", row.Number("member_count")), ("destination", destination.Length > 0 ? "named" : "unnamed"), ("default_group", destination)), G("delete.confirm"))) return;
        if (await Session.WriteAsync(lease => _api.DeleteGroupAsync(lease, editor))) { _draft = null; AdminShellPage.Open(new(AdminRoute.AccessGroups)); }
    }

    private void RenderMembers()
    {
        _members.Children.Clear();
        if (_editor == null) return;
        var groupId = _editor.Value.Text("id"); var regular = _accounts.Where(x => x.Text("role") != "admin").ToArray();
        var members = regular.Where(x => x.Text("access_group_id") == groupId).OrderBy(x => x.Text("username"), StringComparer.CurrentCultureIgnoreCase).ToArray();
        _members.Children.Add(Text(G("members.heading"), 16)); _members.Children.Add(Text(G("members.manageHelp"), 12, true));
        if (!_accountsReady) { _members.Children.Add(Text(G("members.loading"), 12)); return; }
        _members.Children.Add(Button(G("members.addUsers"), "GroupAddMembers", () => _ = AddMembersAsync(regular.Where(x => x.Text("access_group_id") != groupId).ToArray())));
        if (members.Length == 0) _members.Children.Add(Text(G("members.empty"), 12, true));
        if (members.Length > 10)
        {
            var search = Input(G("members.search"), "GroupMemberSearch", _memberQuery); search.TextChanged += (_, _) => { _memberQuery = search.Text; RenderMemberRows(); }; _members.Children.Add(search);
        }
        _memberRows = Stack(8); _members.Children.Add(_memberRows); RenderMemberRows();
        var target = "";
        _members.Children.Add(Select(G("members.moveTarget"), _groups.Where(x => x.Text("id") != groupId).Select(x => (x.Text("id"), x.Text("name"))), "", value => target = value));
        _members.Children.Add(Button(G("members.moveSelected"), "GroupMoveMembers", () => _ = MoveAsync(members.Where(x => _selected.Contains(x.Text("id"))).ToArray(), target)));
    }
    private StackPanel? _memberRows;
    private void RenderMemberRows()
    {
        if (_memberRows == null || _editor == null) return;
        _memberRows.Children.Clear();
        var members = _accounts.Where(x => x.Text("role") != "admin" && x.Text("access_group_id") == _editor.Value.Text("id"))
            .Where(x => (x.Text("username") + " " + x.Text("email")).Contains(_memberQuery.Trim(), StringComparison.CurrentCultureIgnoreCase)).OrderBy(x => x.Text("username")).ToArray();
        foreach (var member in members.Take(_memberCount))
        {
            var id = member.Text("id"); _memberRows.Children.Add(Toggle(member.Text("username") + " · " + member.Text("email"), _selected.Contains(id), value => { if (value) _selected.Add(id); else _selected.Remove(id); }));
        }
        if (members.Length > _memberCount) _memberRows.Children.Add(Button("Show more", "GroupMoreMembers", () => { _memberCount += 10; RenderMemberRows(); }));
        var hidden = _selected.Count(id => members.Take(_memberCount).All(x => x.Text("id") != id));
        if (hidden > 0) _memberRows.Children.Add(Text(G("members.selectedHidden", ("count", hidden)), 12, true));
    }
    private async Task AddMembersAsync(IReadOnlyList<JsonElement> eligible)
    {
        if (_editor == null) return;
        var selected = new HashSet<string>(StringComparer.Ordinal); var panel = Stack(8); var rows = Stack(4); var query = ""; var count = 50;
        var search = Input(G("addUsers.search"), "GroupAddSearch", ""); panel.Children.Add(search); panel.Children.Add(rows);
        void Render()
        {
            rows.Children.Clear(); var matches = eligible.Where(x => (x.Text("username") + " " + x.Text("email")).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            foreach (var row in matches.Take(count)) { var id = row.Text("id"); rows.Children.Add(Toggle(row.Text("username"), selected.Contains(id), value => { if (value) selected.Add(id); else selected.Remove(id); })); }
            if (matches.Length > count) rows.Children.Add(Button("Show more", "GroupAddMore", () => { count += 50; Render(); }));
            if (matches.Length == 0) rows.Children.Add(Text(G(eligible.Count == 0 ? "addUsers.none" : "addUsers.noMatches")));
        }
        search.TextChanged += (_, _) => { query = search.Text.Trim(); Render(); }; Render();
        if (await _host.DialogAsync(G("addUsers.title", ("group", _editor.Value.Text("name"))), new ScrollViewer { Content = panel, MaxHeight = 500 }, G("addUsers.addSelected"), () => selected.Count > 0))
            await MoveAsync(eligible.Where(x => selected.Contains(x.Text("id"))).ToArray(), _editor.Value.Text("id"));
    }
    private async Task MoveAsync(IReadOnlyList<JsonElement> members, string targetId)
    {
        var pageOwner = Session.Owner;
        if (members.Count == 0 || targetId.Length == 0 || !_accountsReady || Session.Busy || Session.NeedsReview) return;
        var target = await Session.ReadAsync("move-target", owner => _api.GetGroupAsync(owner, targetId));
        if (target == null || !Session.IsCurrent(pageOwner)) return;
        var summary = Stack(8); summary.Children.Add(Text(G("moveConfirm.description")));
        var sourceEditors = new Dictionary<string, AdminResourceDocument>(StringComparer.Ordinal);
        var sourceLimits = new Dictionary<string, AdminResourceDocument>(StringComparer.Ordinal);
        var targetLimit = await Session.ReadAsync("move-target-limit", owner => _api.GetGroupLimitAsync(owner, targetId));
        var defaults = await Session.ReadAsync("move-defaults", owner => _api.RequestDefaultsAsync(owner));
        // Display all inherited policy fields in the confirmation, including request terms; account overrides remain untouched.
        var keys = new[] { "library_ids", "max_playback_quality", "download_allowed", "download_transcode_allowed", "transcode_allowed", "audio_transcode_allowed", "max_streams", "max_transcodes", "max_profiles", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps", "allowed_permissions", "requests_allowed" };
        foreach (var sourceId in members.Select(x => x.Text("access_group_id")).Distinct())
        {
            var source = JsonSerializer.Deserialize<JsonElement>("""{"library_ids":null,"max_playback_quality":"","download_allowed":true,"download_transcode_allowed":false,"transcode_allowed":true,"audio_transcode_allowed":true,"max_streams":0,"max_transcodes":0,"max_profiles":5,"max_remote_stream_bitrate_kbps":0,"max_local_stream_bitrate_kbps":0,"allowed_permissions":null,"requests_allowed":true}""");
            AdminResourceDocument? sourceLimit = null;
            if (sourceId.Length > 0)
            {
                var freshSource = await Session.ReadAsync("move-source-" + sourceId, owner => _api.GetGroupAsync(owner, sourceId));
                if (freshSource == null || !Session.IsCurrent(pageOwner)) return;
                sourceEditors[sourceId] = freshSource; source = freshSource.Value;
                sourceLimit = await Session.ReadAsync("move-source-limit-" + sourceId, owner => _api.GetGroupLimitAsync(owner, sourceId));
                if (sourceLimit != null) sourceLimits[sourceId] = sourceLimit;
            }
            summary.Children.Add(Text(source.Text("name", G("addUsers.noGroup")) + " → " + target.Value.Text("name"), 16));
            foreach (var key in keys)
                if (source.Member(key)?.GetRawText() != target.Value.Member(key)?.GetRawText()) summary.Children.Add(Text(PolicyLabel(key) + ": " + PolicyText(key, source.Member(key)) + " → " + PolicyText(key, target.Value.Member(key)), 12));
            summary.Children.Add(Text(defaults.ValueKind != JsonValueKind.Object || targetLimit == null || sourceId.Length > 0 && sourceLimit == null
                ? G("moveConfirm.termsFailed") : "Requests: " + RequestTerms(defaults, sourceLimit?.Value) + " → " + RequestTerms(defaults, targetLimit.Value), 12));
        }
        if (!await _host.DialogAsync(G("moveConfirm.title", ("count", members.Count), ("group", target.Value.Text("name"))), new ScrollViewer { Content = summary, MaxHeight = 500 }, G("moveConfirm.move"), () => true)) return;
        if (!Session.IsCurrent(pageOwner)) return;
        var failures = new List<string>();
        foreach (var member in members)
        {
            if (!Session.IsCurrent(pageOwner)) break;
            var success = await Session.WriteAsync(async lease =>
            {
                var currentTarget = await _api.GetGroupAsync(lease, targetId);
                if (currentTarget.ETag != target.ETag) throw new InvalidOperationException("The destination group changed. Review the move again.");
                if (targetLimit != null && (await _api.GetGroupLimitAsync(lease, targetId)).ETag != targetLimit.ETag) throw new InvalidOperationException("The destination request terms changed. Review the move again.");
                var sourceId = member.Text("access_group_id");
                if (sourceEditors.TryGetValue(sourceId, out var sourceEditor) && (await _api.GetGroupAsync(lease, sourceId)).ETag != sourceEditor.ETag) throw new InvalidOperationException("The source group changed. Review the move again.");
                if (sourceLimits.TryGetValue(sourceId, out var sourceLimit) && (await _api.GetGroupLimitAsync(lease, sourceId)).ETag != sourceLimit.ETag) throw new InvalidOperationException("The source request terms changed. Review the move again.");
                var fresh = await _api.GetMemberAsync(lease, member.Text("id")); await _api.MoveMemberAsync(lease, member, fresh, targetId);
            });
            if (success) _selected.Remove(member.Text("id")); else { failures.Add(member.Text("username")); if (Session.NeedsReview || Session.Owner is not { IsCurrent: true }) break; }
        }
        if (failures.Count > 0) _members.Children.Add(Text("Could not move: " + string.Join(", ", failures) + ". " + Session.Error));
        else { _accounts = await Session.ReadAsync("members", owner => _api.ConfigurationAsync(owner, cursor => _api.ListAccountsAsync(owner, cursor))) ?? []; RenderMembers(); }
    }
    private static string PolicyLabel(string key) => G("changes." + (key switch { "library_ids" => "libraries", "max_playback_quality" => "playbackQuality", "download_allowed" => "downloads", "download_transcode_allowed" => "convertedDownloads", "transcode_allowed" => "videoTranscoding", "audio_transcode_allowed" => "audioTranscoding", "max_streams" => "concurrentStreams", "max_transcodes" => "concurrentTranscodes", "max_profiles" => "householdProfiles", "max_remote_stream_bitrate_kbps" => "remoteBitrate", "max_local_stream_bitrate_kbps" => "localBitrate", "allowed_permissions" => "permissions", _ => "requests" }));
    private string PolicyText(string key, JsonElement? value)
    {
        if (value == null) return "Unknown";
        if (value.Value.ValueKind == JsonValueKind.Null) return G(key == "library_ids" ? "card.allLibraries" : "changes.allPermissions");
        if (value.Value.ValueKind == JsonValueKind.Array)
        {
            var rows = value.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (rows.Length == 0) return G(key == "library_ids" ? "changes.noLibraries" : "changes.noPermissions");
            return string.Join(", ", rows.Select(id => key == "library_ids" ? _libraries.FirstOrDefault(x => x.Text("id") == id).Text("name", "#" + id) : id == "metadata_curation" ? G("permissions.metadataCuration") : id == "marker_edit" ? G("permissions.markerEdit") : id));
        }
        if (value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) return G(value.Value.ValueKind == JsonValueKind.True ? "changes.allowed" : "changes.notAllowed");
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.GetInt32() == 0 && key != "max_profiles") return G("changes.unlimited");
        return value.Value.ToString();
    }
    private static string RequestTerms(JsonElement defaults, JsonElement? group)
    {
        var mode = group?.Text("limit_mode") ?? "inherit"; var approval = group?.Text("approval_mode") ?? "inherit";
        var auto = approval == "auto" || approval == "inherit" && defaults.Flag("global_auto_approval_enabled");
        var quota = mode == "unlimited" ? T("web.admin.common.requestLimits.limitNone") : (mode == "custom" ? group!.Value.Number("max_requests", defaults.Number("global_max_requests")) : defaults.Number("global_max_requests")) + " / " +
            (mode == "custom" ? group!.Value.Number("window_days", Math.Max(1, defaults.Number("global_window_days", 7))) : Math.Max(1, defaults.Number("global_window_days", 7))) + " days";
        return T(auto ? "web.admin.common.requestLimits.approvalAuto" : "web.admin.common.requestLimits.approvalManual") + "; " + quota;
    }
}
