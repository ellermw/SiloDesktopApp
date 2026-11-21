using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAccessGroupsPage
{
    private AdminResourceDocument? _editor, _limit;
    private JsonObject? _draft, _baseline;
    private AdminRequestLimitDraft? _limitDraft, _limitBase;
    private readonly HashSet<string> _invalid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bitrateText = new(StringComparer.Ordinal);
    private string _maxProfilesText = "";
    private (bool DownloadsOff, bool ConversionOff) _downloadSwitches;
    private JsonElement _requestDefaults;
    private bool _limitFailed;
    // Rebuilt for every render: an element taken out of an old tree can still belong to it, so none is reused.
    private StackPanel _libraryBox = Stack(8), _downloadsBox = Stack(10), _requestsBox = Stack(12), _permissionsBox = Stack(10), _members = Stack(10);
    private TextBlock? _saveMessage;
    private Button? _save, _discard, _delete;

    private int LimitChanges => _limitDraft != null && _limitBase != null ? _limitDraft.Changes(_limitBase) : 0;
    /// <summary>An invalid limit box is an edit too: the stored group always holds a valid one.</summary>
    private bool Dirty => _draft != null && _baseline != null && (_invalid.Count > 0 || !AdminAccessGroupBody.Same(AdminAccessGroupBody.ForSave(_draft), _baseline) || LimitChanges > 0);

    private async Task LoadEditorAsync(MonitorAuthorityLease? pageOwner, string id)
    {
        var editor = await Session.ReadAsync("editor", lease => _api.GetGroupAsync(lease, id));
        if (!Session.IsCurrent(pageOwner)) return;
        if (editor == null) { ShowLoadFailed(G("groupLoadFailed"), back: true); return; }
        _editor = editor;
        await LoadLimitAsync(pageOwner, id);
        if (!Session.IsCurrent(pageOwner)) return;
        _downloadSwitches = await Session.ReadAsync("download-switches", lease => _api.DownloadSwitchesAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return;
        _requestDefaults = await Session.ReadAsync("request-defaults", lease => _api.RequestDefaultsAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return;
        var libraries = await Session.ReadAsync("libraries", lease => _api.ListLibrariesAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return;
        _librariesReady = libraries is { NextCursor: null }; _libraries = libraries?.Items ?? [];
        ResetDrafts(); RenderEditor();
        await LoadMembersAsync(pageOwner);
    }
    private async Task LoadLimitAsync(MonitorAuthorityLease? pageOwner, string id)
    {
        var limit = await Session.ReadAsync("limit", lease => _api.GetGroupLimitAsync(lease, id));
        if (!Session.IsCurrent(pageOwner)) return;
        _limit = limit; _limitFailed = limit == null;
    }
    private void ResetDrafts()
    {
        if (_editor == null) return;
        _baseline = AdminAccessGroupBody.Stored(_editor.Value); _draft = (JsonObject)_baseline.DeepClone(); _invalid.Clear(); _bitrateText.Clear();
        _maxProfilesText = _draft["max_profiles"]?.ToJsonString() ?? "";
        _limitBase = _limit == null ? null : AdminRequestLimitDraft.From(_limit.Value); _limitDraft = _limitBase;
    }
    private void Discard()
    {
        if (_editor == null || Session.Busy) return;
        ResetDrafts(); RenderEditor(); _ = RenderMembersIfReady();
    }
    private Task RenderMembersIfReady() { RenderMembers(); return Task.CompletedTask; }

    private void Edit(string key, JsonNode? value) { if (_draft == null) return; _draft[key] = value; UpdateActions(); }
    private bool Flag(string key) => _draft?[key]?.GetValue<bool>() == true;
    private string Raw(string key) => _draft?[key]?.ToJsonString() ?? "";

    private void RenderEditor()
    {
        if (_editor == null || _draft == null) return;
        _libraryBox = Stack(8); _downloadsBox = Stack(10); _requestsBox = Stack(12); _permissionsBox = Stack(10); _members = Stack(10); _memberRows = null;
        var group = _editor.Value; var isDefault = group.Flag("is_default"); _writes.Clear();
        var elements = new List<UIElement> { Button(G("allGroups"), "GroupBack", () => AdminShellPage.Open(new(AdminRoute.AccessGroups))) };

        var name = Input(G("field.name"), "GroupName", _draft["name"]?.GetValue<string>() ?? ""); name.TextChanged += (_, _) => Edit("name", name.Text);
        var description = Input(G("field.description"), "GroupDescription", _draft["description"]?.GetValue<string>() ?? ""); description.PlaceholderText = G("field.descriptionPlaceholder");
        description.TextChanged += (_, _) => Edit("description", description.Text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '));
        var identity = Stack(12); identity.Children.Add(Columns(name, description));
        identity.Children.Add(ToggleRow(G("default.label"), G(isDefault ? "default.descriptionLocked" : "default.description"), Flag("is_default"), value => Edit("is_default", value), !isDefault, "GroupDefault"));
        elements.Add(Panel(identity));

        var playback = Stack(12); playback.Children.Add(Text(G("playback.heading"), 16));
        playback.Children.Add(_libraryBox); RenderLibraries();
        playback.Children.Add(QualityField());
        playback.Children.Add(Columns(BitrateField("GroupRemoteBitrate", G("playback.remoteBitrate"), G("playback.remoteBitrateHint"), "max_remote_stream_bitrate_kbps"),
            BitrateField("GroupLocalBitrate", G("playback.localBitrate"), G("playback.localBitrateHint"), "max_local_stream_bitrate_kbps")));
        elements.Add(Panel(playback));

        var downloads = Stack(10); downloads.Children.Add(Text(G("downloads.heading"), 16)); downloads.Children.Add(_downloadsBox); RenderDownloads();
        elements.Add(Panel(downloads));

        var requests = Stack(12); requests.Children.Add(_requestsBox); RenderRequests();
        elements.Add(Panel(requests));

        var streams = Stack(12); streams.Children.Add(Text(G("streams.heading"), 16));
        streams.Children.Add(Columns(LimitField("GroupStreams", G("streams.max"), "max_streams"), LimitField("GroupTranscodes", G("streams.maxTranscodes"), "max_transcodes")));
        streams.Children.Add(Text(G("streams.zeroUnlimited"), 12, true));
        streams.Children.Add(ToggleRow(G("streams.videoTranscode"), G("streams.videoTranscodeDescription"), Flag("transcode_allowed"), value => Edit("transcode_allowed", value), true, "GroupVideoTranscode"));
        streams.Children.Add(ToggleRow(G("streams.audioTranscode"), G("streams.audioTranscodeDescription"), Flag("audio_transcode_allowed"), value => Edit("audio_transcode_allowed", value), true, "GroupAudioTranscode"));
        elements.Add(Panel(streams));

        var profiles = Stack(8); profiles.Children.Add(Text(G("profiles.heading"), 16));
        var profileBox = Input(G("profiles.max"), "max_profiles", _maxProfilesText); var profileHint = Text(G("profiles.atLeastOne"), 12, true);
        profileBox.TextChanged += (_, _) =>
        {
            _maxProfilesText = profileBox.Text; var parsed = AdminRequestLimitDraft.WholeNumber(profileBox.Text, 1);
            if (parsed == null) _invalid.Add("max_profiles"); else { _invalid.Remove("max_profiles"); Edit("max_profiles", parsed.Value); }
            profileHint.Foreground = Brush(parsed == null ? "ErrorBrush" : "SecondaryTextBrush"); UpdateActions();
        };
        profiles.Children.Add(profileBox); profiles.Children.Add(profileHint); profiles.Children.Add(Text(G("profiles.help"), 12, true));
        elements.Add(Panel(profiles));

        var permissions = Stack(10); permissions.Children.Add(Text(G("permissions.heading"), 16)); permissions.Children.Add(Text(G("permissions.help"), 12, true)); permissions.Children.Add(_permissionsBox); RenderPermissions();
        elements.Add(Panel(permissions));

        _members.Children.Clear();
        elements.Add(Panel(_members)); RenderMembers();

        _saveMessage = Text("", 12); _saveMessage.Foreground = Brush("ErrorBrush");
        _delete = WriteButton(G("delete.button"), "GroupDelete", () => _ = DeleteAsync()); _delete.Foreground = Brush("ErrorBrush");
        _discard = WriteButton("Discard", "GroupDiscard", Discard); _save = WriteButton(G("save"), "GroupSave", () => _ = SaveAsync(), accent: true);
        var commands = new Grid { ColumnSpacing = 8 }; commands.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); commands.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); commands.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var deleteArea = Stack(4); deleteArea.Children.Add(_delete); if (isDefault) deleteArea.Children.Add(Text(G("delete.defaultLocked"), 12, true));
        deleteArea.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(_discard, 1); Grid.SetColumn(_save, 2);
        commands.Children.Add(deleteArea); commands.Children.Add(_discard); commands.Children.Add(_save);
        elements.Add(_saveMessage); elements.Add(commands);

        SetContent([.. elements]);
        _host.Recovery.Children.Clear(); _host.Recovery.Children.Add(Text(G("conflict.message")));
        _host.Recovery.Children.Add(Button(G("conflict.reload"), "GroupReloadKeepDraft", () => _ = ReloadKeepingDraftAsync()));
        UpdateActions();
    }

    // ---------- fields ----------
    private FrameworkElement QualityField()
    {
        (string Value, string Label)[] options =
        [
            ("any", G("playback.qualityOption", ("label", T("web.profiles.helpers.quality.any")), ("description", T("web.profiles.helpers.quality.anyDescription")))),
            ("standard", G("playback.qualityOption", ("label", T("web.profiles.helpers.quality.standard")), ("description", T("web.profiles.helpers.quality.standardDescription")))),
            ("4k", G("playback.qualityOption", ("label", "4K"), ("description", T("web.profiles.helpers.quality.fourKDescription")))),
        ];
        var select = Select(G("playback.quality"), options, AdminAccessGroupBody.QualityPreset(_draft!["max_playback_quality"]?.GetValue<string>()), preset => Edit("max_playback_quality", AdminAccessGroupBody.QualityValue(preset)));
        select.MaxWidth = 460; select.HorizontalAlignment = HorizontalAlignment.Left; Id(select, "GroupQuality"); return select;
    }

    /// <summary>A whole-number limit (0 is unlimited). Typing that is not a number is not accepted; clearing the box reads as 0. A stored value stays as stored until edited.</summary>
    private FrameworkElement LimitField(string id, string label, string key)
    {
        var box = Input(label, id, Raw(key)); var last = box.Text; var syncing = false;
        box.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var raw = box.Text;
            if (raw.Length == 0) { syncing = true; box.Text = "0"; box.SelectAll(); syncing = false; last = "0"; Edit(key, 0); return; }
            if (raw.Any(c => c is < '0' or > '9') || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) { syncing = true; box.Text = last; box.SelectionStart = last.Length; syncing = false; return; }
            last = raw; Edit(key, number);
        };
        return box;
    }

    /// <summary>Unlimited, a preset, or a custom Mbps value stored in kbps. A half-typed custom value is an unsaved edit that blocks Save, never a new cap.</summary>
    private FrameworkElement BitrateField(string id, string label, string hint, string key)
    {
        var panel = Stack(6); int? stored = _draft![key] is JsonValue value && value.TryGetValue<int>(out var kbps) ? kbps : null;
        var choice = AdminStreamBitrate.Choice(stored);
        if (_bitrateText.TryGetValue(key, out var typed) && _invalid.Contains(key)) choice = AdminStreamBitrate.Custom;
        var options = new List<(string Value, string Label)> { (AdminStreamBitrate.Unlimited, AdminStreamBitrate.Format(0)) };
        options.AddRange(AdminStreamBitrate.PresetsKbps.Select(preset => (preset.ToString(CultureInfo.InvariantCulture), AdminStreamBitrate.Format(preset))));
        options.Add((AdminStreamBitrate.Custom, T("web.player.bitrateLimit.custom")));
        var select = Select(label, options, choice ?? "", null); Id(select, id);
        var custom = Id(new TextBox { PlaceholderText = T("web.player.bitrateLimit.customPlaceholder"), MinWidth = 120, Text = typed ?? (choice == AdminStreamBitrate.Custom && stored is { } current ? AdminStreamBitrate.MbpsText(current) : "") }, id + "Custom");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(custom, T("web.player.bitrateLimit.customAria", ("label", label)));
        var unit = Text(T("web.player.bitrateLimit.unit"), 12, true); var message = Text("", 12, true);
        var row = Inline(8); row.Children.Add(custom); row.Children.Add(unit);
        void Refresh()
        {
            var isCustom = (select.SelectedItem as ComboBoxItem)?.Tag as string == AdminStreamBitrate.Custom;
            row.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            var parsed = AdminStreamBitrate.ParseMbps(custom.Text);
            message.Text = !isCustom ? "" : parsed == null ? T("web.player.bitrateLimit.invalid", ("places", 3), ("min", 0)) : AdminStreamBitrate.IsLow(parsed.Value) ? T("web.player.bitrateLimit.lowWarning", ("mbps", AdminStreamBitrate.LowKbps / 1000)) : "";
            message.Foreground = Brush(isCustom && parsed == null ? "ErrorBrush" : isCustom ? "WarningBrush" : "SecondaryTextBrush");
        }
        void Draft(string raw)
        {
            _bitrateText[key] = raw; var parsed = AdminStreamBitrate.ParseMbps(raw);
            if (parsed == null) _invalid.Add(key); else { _invalid.Remove(key); Edit(key, parsed.Value); }
            Refresh(); UpdateActions();
        }
        select.SelectionChanged += (_, _) =>
        {
            if ((select.SelectedItem as ComboBoxItem)?.Tag is not string chosen) return;
            if (chosen != AdminStreamBitrate.Custom) { _invalid.Remove(key); _bitrateText.Remove(key); Edit(key, int.Parse(chosen, CultureInfo.InvariantCulture)); Refresh(); return; }
            // Start from the current cap so picking Custom alone changes nothing.
            var seed = _draft[key] is JsonValue v && v.TryGetValue<int>(out var now) && now > 0 ? AdminStreamBitrate.MbpsText(now) : "";
            custom.Text = seed; Draft(seed);
        };
        custom.TextChanged += (_, _) => Draft(custom.Text);
        panel.Children.Add(select); panel.Children.Add(row); panel.Children.Add(message); panel.Children.Add(Text(hint, 12, true)); Refresh();
        return panel;
    }

    private void RenderLibraries()
    {
        _libraryBox.Children.Clear(); var all = _draft!["library_ids"] == null;
        _libraryBox.Children.Add(Text(T("web.profiles.libraryAccess.label"), 14));
        var allToggle = ToggleRow(T("web.profiles.libraryAccess.allLibraries"), "", all, value =>
        {
            Edit("library_ids", value ? null : new JsonArray(_libraries.Select(library => (JsonNode?)JsonValue.Create(library.Text("id"))).ToArray()));
            RenderLibraries();
        }, _librariesReady, "GroupAllLibraries");
        _libraryBox.Children.Add(allToggle);
        if (!_librariesReady) _libraryBox.Children.Add(Text("Libraries could not be loaded completely. Reload before changing library access.", 12));
        if (all || _draft["library_ids"] is not JsonArray selected) return;
        if (_libraries.Count == 0 && selected.Count == 0) _libraryBox.Children.Add(Text(T("web.profiles.noLibraries"), 12, true));
        void Toggle(string id, bool on)
        {
            var existing = selected.FirstOrDefault(x => x?.GetValue<string>() == id);
            if (on && existing == null) selected.Add(id); else if (!on && existing != null) selected.Remove(existing);
            UpdateActions();
        }
        foreach (var library in _libraries)
        {
            var id = library.Text("id");
            var detail = library.Text("type") + (library.Member("enabled") is { ValueKind: JsonValueKind.False } ? " · " + T("web.profiles.libraryAccess.disabled") : "");
            _libraryBox.Children.Add(ToggleRow(library.Text("name"), detail, selected.Any(x => x?.GetValue<string>() == id), on => Toggle(id, on), _librariesReady, "GroupLibrary_" + id));
        }
        // A stored library id this list does not know stays visible so it can be removed, never silently dropped.
        foreach (var unknown in selected.Select(x => x?.GetValue<string>() ?? "").Where(id => id.Length > 0 && _libraries.All(library => library.Text("id") != id)).ToArray())
            _libraryBox.Children.Add(ToggleRow(G("changes.libraryId", ("id", unknown)), "", true, on => Toggle(unknown, on), true, "GroupLibrary_" + unknown));
    }

    private void RenderDownloads()
    {
        _downloadsBox.Children.Clear();
        if (_downloadSwitches.DownloadsOff || _downloadSwitches.ConversionOff)
        {
            _downloadsBox.Children.Add(Text(T(_downloadSwitches.DownloadsOff ? "web.admin.common.downloadAccess.serverOff" : "web.admin.common.downloadAccess.conversionOff"), 12, true));
            if (AdminShellPage.CanOpen(new(AdminRoute.DownloadSettings))) _downloadsBox.Children.Add(Link("Settings › Downloads", "GroupDownloadSettings", () => AdminShellPage.Open(new(AdminRoute.DownloadSettings))));
        }
        _downloadsBox.Children.Add(ToggleRow(G("downloads.allow"), G("downloads.allowDescription"), Flag("download_allowed"), value => { Edit("download_allowed", value); RenderDownloads(); }, true, "GroupDownloads"));
        _downloadsBox.Children.Add(ToggleRow(G("downloads.transcoded"), G("downloads.transcodedDescription"), Flag("download_transcode_allowed"), value => Edit("download_transcode_allowed", value), Flag("download_allowed"), "GroupConvertedDownloads"));
    }

    private void RenderPermissions()
    {
        _permissionsBox.Children.Clear(); var all = _draft!["allowed_permissions"] == null;
        _permissionsBox.Children.Add(ToggleRow(G("permissions.allowAll"), G("permissions.allowAllLabel"), all, value =>
        {
            Edit("allowed_permissions", value ? null : new JsonArray(JsonValue.Create(AdminAccessGroupBody.MetadataCuration), JsonValue.Create(AdminAccessGroupBody.MarkerEdit))); RenderPermissions();
        }, true, "GroupAllPermissions"));
        if (_draft["allowed_permissions"] is not JsonArray mask) return;
        foreach (var (permission, label, description) in new[] { (AdminAccessGroupBody.MetadataCuration, "metadataCuration", "metadataCurationDescription"), (AdminAccessGroupBody.MarkerEdit, "markerEdit", "markerEditDescription") })
            _permissionsBox.Children.Add(ToggleRow(G("permissions." + label), G("permissions." + description), mask.Any(x => x?.GetValue<string>() == permission), on =>
            {
                var existing = mask.FirstOrDefault(x => x?.GetValue<string>() == permission);
                if (on && existing == null) mask.Add(permission); else if (!on && existing != null) mask.Remove(existing);
                UpdateActions();
            }, true, "GroupPermission_" + permission));
        // A stored permission this build does not know stays as stored; it is never offered, and never dropped by an unrelated edit.
    }

    private void RenderRequests()
    {
        _requestsBox.Children.Clear();
        _requestsBox.Children.Add(Text(G("requests.heading"), 16)); _requestsBox.Children.Add(Text(G("requests.intro"), 12, true));
        if (AdminShellPage.CanOpen(new(AdminRoute.RequestSettings))) _requestsBox.Children.Add(Link("Settings › Requests", "GroupRequestSettings", () => AdminShellPage.Open(new(AdminRoute.RequestSettings))));
        _requestsBox.Children.Add(ToggleRow(G("requests.allow"), G("requests.allowDescription"), Flag("requests_allowed"), value => Edit("requests_allowed", value), true, "GroupRequests"));
        if (_limitFailed || _limitDraft == null)
        {
            _requestsBox.Children.Add(Text(G("requests.loadFailed"), 12)); _requestsBox.Children.Add(Button(T("common.retry"), "GroupRetryLimit", () => _ = ReloadLimitAsync()));
            return;
        }
        var draft = _limitDraft; var server = _requestDefaults.ValueKind == JsonValueKind.Object ? AdminRequestServerDefaults.From(_requestDefaults) : null;
        var terms = server == null ? null : AdminRequestAccess.Resolve([], server);
        void Change(AdminRequestLimitDraft next, bool rebuild) { _limitDraft = next; UpdateActions(); if (rebuild) RenderRequests(); }
        var approval = Select(T("web.admin.common.requestLimits.approval"), [("inherit", G("requests.inheritLabel")), ("manual", T("web.admin.common.requestLimits.approvalManual")), ("auto", T("web.admin.common.requestLimits.approvalAuto"))], draft.Approval,
            value => Change(_limitDraft! with { Approval = value }, true)); Id(approval, "GroupApproval");
        _requestsBox.Children.Add(approval);
        if (draft.Approval == "inherit" && terms != null) _requestsBox.Children.Add(Text(AdminRequestAccess.DescribeInherited(AdminRequestAccess.FormatApproval(terms.AutoApprove), terms.ApprovalSource, null), 12, true));
        var limit = Select(T("web.admin.common.requestLimits.limit"), [("inherit", G("requests.inheritLabel")), ("custom", T("web.admin.common.requestLimits.limitCustom")), ("unlimited", T("web.admin.common.requestLimits.limitNone"))], draft.Limit,
            value =>
            {
                var current = _limitDraft!; var seeded = value == "custom" && current.MaxRequests.Length == 0 && current.WindowDays.Length == 0 && terms is { Quota.Unlimited: false }
                    ? current with { Limit = value, MaxRequests = terms.Quota.Max.ToString(CultureInfo.InvariantCulture), WindowDays = terms.Quota.Days.ToString(CultureInfo.InvariantCulture) } : current with { Limit = value };
                Change(seeded, true);
            }); Id(limit, "GroupLimit");
        _requestsBox.Children.Add(limit);
        if (draft.Limit == "inherit" && terms != null) _requestsBox.Children.Add(Text(AdminRequestAccess.DescribeInherited(AdminRequestAccess.FormatQuota(terms.Quota), terms.QuotaSource, null), 12, true));
        if (draft.Limit == "custom")
        {
            var max = Input(T("web.admin.common.requestLimits.maxRequestsLabel"), "GroupMaxRequests", draft.MaxRequests); var window = Input(T("web.admin.common.requestLimits.windowDaysLabel"), "GroupWindowDays", draft.WindowDays);
            var maxError = Text("", 12); maxError.Foreground = Brush("ErrorBrush"); var windowError = Text("", 12); windowError.Foreground = Brush("ErrorBrush"); var zero = Text(T("web.admin.common.requestLimits.zeroHint", ("subject", "group")), 12, true);
            void Validate()
            {
                var current = _limitDraft!; maxError.Text = current.MaxRequestsError ?? ""; windowError.Text = current.WindowDaysError ?? ""; zero.Visibility = current.IsZeroLimit ? Visibility.Visible : Visibility.Collapsed;
            }
            max.TextChanged += (_, _) => { Change(_limitDraft! with { MaxRequests = max.Text }, false); Validate(); };
            window.TextChanged += (_, _) => { Change(_limitDraft! with { WindowDays = window.Text }, false); Validate(); };
            _requestsBox.Children.Add(Columns(max, window)); _requestsBox.Children.Add(maxError); _requestsBox.Children.Add(windowError); _requestsBox.Children.Add(zero); Validate();
        }
    }
    private async Task ReloadLimitAsync()
    {
        if (_editor == null || Session.Busy) return; var owner = Session.Owner; var id = _editor.Value.Text("id");
        var limit = await Session.ReadAsync("limit", lease => _api.GetGroupLimitAsync(lease, id));
        if (!Session.IsCurrent(owner)) return;
        if (limit == null) { _limitFailed = true; RenderRequests(); return; }
        _limit = limit; _limitFailed = false; _limitBase = AdminRequestLimitDraft.From(limit.Value); _limitDraft = _limitBase; RenderRequests(); UpdateActions();
    }

    // ---------- actions ----------
    private void UpdateActions()
    {
        var canWrite = Session.Owner is { IsCurrent: true } && !Session.Busy && !Session.NeedsReview;
        var limitBlocks = _limitDraft is { HasErrors: true } && LimitChanges > 0;
        foreach (var button in _writes.Concat(_memberWrites)) button.IsEnabled = canWrite && (button.Tag is not Func<bool> extra || extra());
        if (_save != null) _save.IsEnabled = canWrite && _editor?.CanWrite == true && Dirty && _invalid.Count == 0 && !limitBlocks && (_limitDraft == null || _limit?.CanWrite == true || LimitChanges == 0);
        if (_discard != null) _discard.IsEnabled = canWrite && Dirty;
        if (_delete != null) _delete.IsEnabled = canWrite && _editor is { CanWrite: true } && !_editor.Value.Flag("is_default");
        if (_saveMessage != null) _saveMessage.Text = Session.Error ?? "";
    }

    private async Task SaveAsync()
    {
        if (_editor == null || _draft == null || _invalid.Count > 0) return;
        var editor = _editor; var limit = _limit; var owner = Session.Owner;
        var body = AdminAccessGroupBody.ForSave(_draft); var groupChanged = !AdminAccessGroupBody.Same(body, _baseline!);
        var limitDraft = _limitDraft; var limitChanged = limit != null && limitDraft != null && LimitChanges > 0;
        if (limitChanged && limitDraft!.HasErrors) return;
        if (!groupChanged && !limitChanged) return;
        var ok = await Session.WriteAsync(async lease =>
        {
            if (groupChanged)
            {
                var saved = await _api.SaveGroupAsync(lease, editor, JsonSerializer.SerializeToElement(body));
                if (!Session.IsCurrent(owner)) return;
                _editor = saved; _baseline = AdminAccessGroupBody.Stored(saved.Value); // a retry after the limit failed leaves the group alone
            }
            if (!limitChanged) return;
            try
            {
                var savedLimit = await _api.SaveGroupLimitAsync(lease, limit!, JsonSerializer.SerializeToElement(limitDraft!.Body()));
                if (!Session.IsCurrent(owner)) return;
                _limit = savedLimit; _limitBase = AdminRequestLimitDraft.From(savedLimit.Value); _limitDraft = _limitBase;
            }
            catch (Exception error) when (error is not OperationCanceledException and not MonitorAuthorityRetiredException)
            {
                throw new AdminResourceFollowUpException(groupChanged ? "web.admin.access.groups.limitNotSavedAfterGroup" : "web.admin.access.groups.limitNotSaved", error);
            }
        });
        if (!Session.IsCurrent(owner)) return;
        if (ok) { _draft = (JsonObject)_baseline!.DeepClone(); AdminShellPage.Open(new(AdminRoute.AccessGroups)); }
        else UpdateActions();
    }

    private async Task DeleteAsync()
    {
        var editor = _editor; if (editor == null || editor.Value.Flag("is_default")) return; var owner = Session.Owner; var groups = _groups;
        var group = editor.Value; var row = _groups.FirstOrDefault(x => x.Text("id") == group.Text("id"));
        var destination = _groups.FirstOrDefault(x => x.Flag("is_default") && x.Text("id") != group.Text("id")).Text("name");
        var count = row.ValueKind == JsonValueKind.Object ? row.Number("member_count") : 0;
        var description = G("delete.description", ("members", row.ValueKind != JsonValueKind.Object ? "unknown" : count == 0 ? "none" : "some"), ("count", count), ("count_text", count),
            ("destination", destination.Length > 0 ? "named" : "unnamed"), ("default_group", Safe(destination)));
        if (!await _host.ConfirmAsync(G("delete.title", ("name", Safe(group.Text("name")))), description, G("delete.confirm"), destructive: true, stillCurrent: () => ReferenceEquals(_editor, editor) && ReferenceEquals(_groups, groups)) || !Session.IsCurrent(owner)) return;
        if (await Session.WriteAsync(lease => _api.DeleteGroupAsync(lease, editor)) && Session.IsCurrent(owner)) { _draft = (JsonObject)_baseline!.DeepClone(); _limitDraft = _limitBase; AdminShellPage.Open(new(AdminRoute.AccessGroups)); }
        else if (Session.IsCurrent(owner)) UpdateActions();
    }

    /// <summary>The official "Reload current group": the typed draft is kept and compared with the newest stored group; only the validators are refreshed.</summary>
    private async Task ReloadKeepingDraftAsync()
    {
        if (_editor == null || Session.Busy) return; var id = _editor.Value.Text("id");
        try { Session.BeginReview(); } catch (InvalidOperationException) { return; }
        var owner = Session.Owner;
        var editor = await Session.ReadAsync("editor", lease => _api.GetGroupAsync(lease, id));
        if (!Session.IsCurrent(owner)) return;
        if (editor == null) { _host.RaiseChanged(); return; }
        _editor = editor; _baseline = AdminAccessGroupBody.Stored(editor.Value);
        var limit = await Session.ReadAsync("limit", lease => _api.GetGroupLimitAsync(lease, id));
        if (!Session.IsCurrent(owner)) return;
        if (limit != null) { _limit = limit; _limitFailed = false; _limitBase = AdminRequestLimitDraft.From(limit.Value); }
        RenderEditor(); await LoadMembersAsync(owner); _host.RaiseChanged();
    }
}
