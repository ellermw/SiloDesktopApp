using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    /// <summary>The access groups and libraries an editor lists: every page is read (the official form lists them all), within the same bound as the lists.</summary>
    private async Task LoadChoicesAsync(long generation, bool groups, bool libraries)
    {
        if (groups && Feature("access_groups"))
        {
            if (!_rows.ContainsKey("groups")) await RowsAsync("groups", "/api/v2/admin/access-groups", generation);
            RequireGeneration(generation);
            if (!_rows.ContainsKey("groups")) throw new InvalidOperationException(T("admin.native.users.choicesFailed"));
            while (_rows["groups"] is { Complete: false } state && state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9) { await MoreAsync("groups"); RequireGeneration(generation); }
        }
        if (libraries)
        {
            if (!_rows.ContainsKey("library-choices")) await RowsAsync("library-choices", "/api/v2/libraries", generation, true);
            RequireGeneration(generation);
            if (!_rows.ContainsKey("library-choices")) throw new InvalidOperationException(T("admin.native.users.choicesFailed"));
        }
    }

    private AccessEditorContext AccessContext(AdminUserReview review) =>
        new(review.Viewer, Policy, Feature("access_groups") ? Rows("groups") : [], Rows("library-choices"), Document("defaults"),
            _rows.ContainsKey("profiles") ? Rows("profiles").Count : null, Document("external-capabilities"));

    /// <summary>
    /// Opens one Access card as its official edit form: the account is read fresh, the form is built from it, and Save sends only the rows that changed under the
    /// account's own validator. The review step the old window offered is kept as the check every write makes (the account and the records it reads must be
    /// unchanged), and a change under the admin keeps their edits and offers "Reload and review".
    /// </summary>
    private async Task EditCardAsync(string title, AdminUsersOperation operation, bool groups, bool libraries, string automationId, Func<AccessEditorContext, AdminUserReview, AdminUsersCardEditor> make)
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = T("admin.native.users.refreshedNotice"); await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        await LoadChoicesAsync(generation, groups, libraries);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var editor = make(AccessContext(review), review);
        var spec = new FormDialogSpec
        {
            Title = title, Body = editor.Content, Review = review, AutomationId = automationId, MaxWidth = 512,
            Build = _ => editor.Validate() && editor.Body() is { Count: > 0 } body ? new AdminUsersWrite(operation, body) : null,
            CanSave = () => editor.CanSave, OnChanged = callback => editor.Changed += callback, Footnote = editor.Footnote,
            Reload = () => FreshReviewAsync(generation), Rebase = fresh => editor.Rebase(fresh.User),
        };
        var result = await FormDialogAsync(spec, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }

    private Task EditSignInAsync() => EditCardAsync(T("web.admin.users.detail.common.cardTitle.signin"), AdminUsersOperation.UpdateAccount, true, false, "AccessSignInForm",
        (context, review) => new SignInEditor(review.User, context));

    private Task EditLibraryAsync() => EditCardAsync(T("web.admin.users.detail.common.cardTitle.library"), AdminUsersOperation.UpdatePolicy, true, true, "AccessLibraryForm",
        (context, review) => new LibraryEditor(review.User, context));

    private Task EditPlaybackAsync() => EditCardAsync(T("web.admin.users.detail.common.cardTitle.playback"), AdminUsersOperation.UpdatePolicy, true, false, "AccessPlaybackForm",
        (context, review) => AccessCards.Playback(review.User, context, GroupNameOrNull(review.User)));

    private Task EditDownloadsAsync() => EditCardAsync(T("web.admin.users.detail.common.cardTitle.downloads"), AdminUsersOperation.UpdatePolicy, true, false, "AccessDownloadsForm",
        (context, review) => AccessCards.Downloads(review.User, context));

    private Task EditRequestsAsync() => EditCardAsync(T("web.admin.users.detail.common.cardTitle.requests"), AdminUsersOperation.UpdatePolicy, true, false, "AccessRequestsForm",
        (context, review) => AccessCards.Requests(review.User, context));

    private Task EditBreakGlassAsync() => EditCardAsync(T("web.admin.common.adminUserSignIn.breakGlass.title"), AdminUsersOperation.UpdateAccount, false, false, "BreakGlassForm",
        (context, review) => new BreakGlassEditor(review.User, context));

    private string? GroupNameOrNull(JsonObject user) => user.Text("role") != "admin" && user.Text("access_group_id").Length > 0
        ? Rows("groups").FirstOrDefault(group => group.Text("id") == user.Text("access_group_id"))?.Text("name") ?? "" : null;

    /// <summary>
    /// Request quota (official <c>RequestLimitFields</c> of the Requests card): the account's own approval and limit under the record's own validator. The limit record is
    /// read again with the account when the write is sent, so a limit another admin changed first is never overwritten.
    /// </summary>
    private async Task EditQuotaAsync()
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = T("admin.native.users.refreshedNotice"); await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        await LoadChoicesAsync(generation, true, false);
        var path = "/api/v2/admin/request-users/" + AdminUsersApi.Segment(review.User.Text("id")) + "/limit";
        var quota = await ReadResourceAsync(path, generation);
        var settings = await ReadResourceAsync("/api/v2/admin/request-settings", generation);
        var grouped = review.User.Text("role") != "admin" && review.User.Text("access_group_id").Length > 0;
        JsonObject? groupLimit = null;
        if (grouped) groupLimit = (await ReadResourceAsync("/api/v2/admin/request-groups/" + AdminUsersApi.Segment(review.User.Text("access_group_id")) + "/limit", generation)).Body;
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var inherited = AdminRequestTermsResolver.Inherited(review.User, groupLimit, settings.Body);
        var groupName = grouped ? GroupNameOrNull(review.User) : null;
        var seed = inherited is { Unlimited: false } ? (inherited.Max.ToString(), inherited.Days.ToString())
            : (settings.Body.Count("global_max_requests").ToString(), settings.Body.Count("global_window_days").ToString());
        var editor = new RequestLimitEditor(quota.Body!, AccessContext(review), inherited, grouped ? groupName ?? "" : null, seed.Item1, seed.Item2);
        var readbacks = new AdminUsersReadback(path, quota.Body!, quota.ETag, true);
        var spec = new FormDialogSpec
        {
            Title = T("web.admin.users.detail.common.cardTitle.requests"), Body = editor.Content, Review = review, AutomationId = "RequestQuotaForm", MaxWidth = 512,
            Build = _ => editor.Validate() && editor.Body() is { Count: > 0 } ? new AdminUsersWrite(AdminUsersOperation.Quota, editor.FullBody(), Readbacks: [readbacks]) : null,
            CanSave = () => editor.CanSave, OnChanged = callback => editor.Changed += callback, Footnote = editor.Footnote,
            Reload = async () =>
            {
                var fresh = await FreshReviewAsync(generation);
                quota = await ReadResourceAsync(path, generation);
                readbacks = new AdminUsersReadback(path, quota.Body!, quota.ETag, true);
                return fresh;
            },
            Rebase = _ => editor.Rebase(quota.Body!),
        };
        var result = await FormDialogAsync(spec, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }
}

/// <summary>The row sets of the Playback, Downloads and Requests cards (official <c>PlaybackCard</c>, <c>DownloadsPolicyCard</c>, <c>RequestsCard</c>).</summary>
internal static class AccessCards
{
    private static (string Tag, string Label)[] AllowedOptions() =>
    [
        ("true", T("web.admin.users.detail.access.value.allowed")), ("false", T("web.admin.users.detail.access.value.notAllowed")),
    ];

    private static (string Tag, string Label)[] YesNoOptions() =>
    [
        ("true", T("web.admin.users.detail.access.value.yes")), ("false", T("web.admin.users.detail.access.value.no")),
    ];

    private static string? BoolTag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? (flag ? "true" : "false") : null;
    private static JsonNode? BoolNode(string tag) => JsonValue.Create(tag == "true");
    private static string? Tag(bool? value) => value == null ? null : value.Value ? "true" : "false";

    private static ChoiceAccessRow Allowed(PolicyRowsEditor editor, string label, string? description, string id, string field, JsonObject start, bool? inherited, bool enabled, (string Tag, string Label)[]? options = null) =>
        new(label, description, id, field, options ?? AllowedOptions(), start[field], BoolTag, BoolNode, Tag(inherited), editor.Touch, enabled);

    private static UserPolicyHints Hints(JsonObject user, AccessEditorContext context) =>
        AdminUserAccountEdit.InheritHints(user, user["access_group_id"]?.ToString(), context.Groups, context.Defaults);

    internal static PolicyRowsEditor Playback(JsonObject user, AccessEditorContext context, string? groupName)
    {
        var description = groupName != null
            ? T("web.admin.users.detail.access.playback.editDescriptionGroup", ("group", string.IsNullOrEmpty(groupName) ? "#" + user.Text("access_group_id") : groupName))
            : T("web.admin.users.detail.access.playback.editDescriptionServer");
        var labels = new Dictionary<string, string>
        {
            ["max_playback_quality"] = T("web.admin.users.detail.access.playback.maxQuality"), ["max_streams"] = T("web.admin.users.detail.access.playback.streams"),
            ["transcode_allowed"] = T("web.admin.users.detail.access.playback.video"), ["max_transcodes"] = T("web.admin.users.detail.access.playback.video"),
            ["audio_transcode_allowed"] = T("web.admin.users.detail.access.playback.audio"), ["max_remote_stream_bitrate_kbps"] = T("web.admin.users.detail.access.playback.remoteBitrate"),
            ["max_local_stream_bitrate_kbps"] = T("web.admin.users.detail.access.playback.localBitrate"),
        };
        return new PolicyRowsEditor(user, context, description, labels, (editor, ctx, start) =>
        {
            var hints = Hints(editor.Current, ctx);
            var enabled = ctx.PolicyAllowed;
            var quality = new (string Tag, string Label)[]
            {
                ("any", T("web.admin.users.detail.access.value.anyQuality")), ("standard", "1080p"), ("4k", "4K"),
            };
            var qualityRow = new ChoiceAccessRow(labels["max_playback_quality"], null, "AccessQuality", "max_playback_quality", quality, start["max_playback_quality"],
                node => AdminUserAccountEdit.QualityPreset(node?.ToString()), tag => JsonValue.Create(AdminUserAccountEdit.QualityValue(tag)),
                hints.MaxPlaybackQualityKnown ? AdminUserAccountEdit.QualityPreset(hints.MaxPlaybackQuality) : null, editor.Touch, enabled);
            var streams = new NumberAccessRow(labels["max_streams"], null, "AccessStreams", "max_streams", 0, start["max_streams"], hints.MaxStreams,
                value => value == 0 ? T("web.admin.users.detail.access.value.unlimited") : value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                T("web.admin.users.detail.access.playback.streamsHint"), T("web.admin.users.detail.access.playback.streamsInvalid"), editor.Touch, enabled);
            var effective = editor.Current["effective_policy"] as JsonObject;
            // A custom row shows what applies now, which also covers an overridden count under an inherited switch.
            AdminUserVideoTranscoding? video = start["transcode_allowed"] == null && start["max_transcodes"] == null ? null
                : AdminUserVideoTranscoding.FromEffective(start["transcode_allowed"] is JsonValue allowed && allowed.TryGetValue<bool>(out var flag) ? flag : effective.Flag("transcode_allowed"),
                    start["max_transcodes"] is JsonValue count && UserJson.TryInteger(count, out var max) ? max : effective.Count("max_transcodes"));
            var inheritedVideo = hints.TranscodeAllowed != null && hints.MaxTranscodes != null ? AdminUserVideoTranscoding.FromEffective(hints.TranscodeAllowed.Value, hints.MaxTranscodes.Value) : null;
            var videoRow = new VideoAccessRow(labels["transcode_allowed"], "AccessVideo", video, inheritedVideo, editor.Touch, enabled);
            var audio = Allowed(editor, labels["audio_transcode_allowed"], null, "AccessAudio", "audio_transcode_allowed", start, hints.AudioTranscodeAllowed, enabled);
            var remote = new BitrateAccessRow(labels["max_remote_stream_bitrate_kbps"], T("web.admin.users.detail.access.playback.remoteDescription"), "AccessRemote", "max_remote_stream_bitrate_kbps",
                start["max_remote_stream_bitrate_kbps"], hints.MaxRemoteStreamBitrateKbps, editor.Touch, enabled);
            var local = new BitrateAccessRow(labels["max_local_stream_bitrate_kbps"], null, "AccessLocal", "max_local_stream_bitrate_kbps",
                start["max_local_stream_bitrate_kbps"], hints.MaxLocalStreamBitrateKbps, editor.Touch, enabled);
            return [qualityRow, streams, videoRow, audio, remote, local];
        });
    }

    internal static PolicyRowsEditor Downloads(JsonObject user, AccessEditorContext context)
    {
        var labels = new Dictionary<string, string>
        {
            ["download_allowed"] = T("web.admin.users.detail.access.downloads.offline"), ["download_transcode_allowed"] = T("web.admin.users.detail.access.downloads.serverPrepared"),
        };
        return new PolicyRowsEditor(user, context, null, labels, (editor, ctx, start) =>
        {
            var hints = Hints(editor.Current, ctx);
            return
            [
                Allowed(editor, labels["download_allowed"], null, "AccessDownload", "download_allowed", start, hints.DownloadAllowed, ctx.PolicyAllowed),
                Allowed(editor, labels["download_transcode_allowed"], T("web.admin.users.detail.access.downloads.serverPreparedDescription"), "AccessServerPrepared", "download_transcode_allowed", start,
                    hints.DownloadTranscodeAllowed, ctx.PolicyAllowed),
            ];
        });
    }

    internal static PolicyRowsEditor Requests(JsonObject user, AccessEditorContext context)
    {
        var labels = new Dictionary<string, string> { ["requests_allowed"] = T("web.admin.users.detail.access.requests.canRequest") };
        return new PolicyRowsEditor(user, context, null, labels, (editor, ctx, start) =>
        {
            var hints = Hints(editor.Current, ctx);
            return [Allowed(editor, labels["requests_allowed"], null, "AccessRequests", "requests_allowed", start, hints.RequestsAllowed, ctx.PolicyAllowed, YesNoOptions())];
        });
    }

    private static string T(string key, params (string Name, object? Value)[] args) => AdminUsersUi.T(key, args);
}
