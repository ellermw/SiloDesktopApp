using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

/// <summary>The rules of the Access card editors (official pages/admin-users/detail/access/*) and the request-limit fields: what each save sends.</summary>
public sealed class AdminUserAccountEditTests
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject User(string extra = "") => Json("""
        {"id":"2","username":"alice","email":"alice@example.test","role":"user","is_owner":false,"enabled":true,"password_login":true,"access_group_id":"g1",
         "library_ids":null,"permissions":["marker_edit"],"max_playback_quality":null,"max_streams":null,"max_transcodes":null,"transcode_allowed":null,"audio_transcode_allowed":null,
         "max_remote_stream_bitrate_kbps":null,"max_local_stream_bitrate_kbps":null,"download_allowed":null,"download_transcode_allowed":null,"requests_allowed":null,"max_profiles":null,
         "effective_policy":{"library_ids":["1"],"max_playback_quality":"1080p","max_streams":2,"max_transcodes":1,"transcode_allowed":true,"audio_transcode_allowed":true,
          "max_remote_stream_bitrate_kbps":8000,"max_local_stream_bitrate_kbps":0,"download_allowed":true,"download_transcode_allowed":false,"requests_allowed":true,"max_profiles":3,"permissions":["marker_edit"]}}
        """.Replace("\"max_profiles\":null,", extra.Length == 0 ? "\"max_profiles\":null," : extra));

    private static readonly JsonObject Group = Json("""{"id":"g1","name":"Family","library_ids":["2"],"max_playback_quality":"2160p","max_streams":5,"max_transcodes":2,"transcode_allowed":false,"audio_transcode_allowed":true,"max_remote_stream_bitrate_kbps":4000,"max_local_stream_bitrate_kbps":0,"download_allowed":false,"download_transcode_allowed":true,"requests_allowed":false,"max_profiles":4,"allowed_permissions":["marker_edit"]}""");

    [Fact]
    public void AFieldThatInheritsShowsTheFreshestResolvedValueAndAnOverriddenOneShowsTheGroupOnlyValue()
    {
        var user = User("\"max_profiles\":null,"); user["max_streams"] = 9;
        var hints = AdminUserAccountEdit.InheritHints(user, "g1", [Group], null);
        Assert.Equal(5, hints.MaxStreams);            // overridden: what Default would be under the group
        Assert.Equal(1, hints.MaxTranscodes);         // inheriting: what the account resolves to now
        Assert.Equal(3, hints.MaxProfiles);
    }

    [Fact]
    public void ADifferentGroupPickedShowsThatGroupsValuesOnly()
    {
        var other = Json("""{"id":"g2","name":"Guests","max_streams":1,"max_profiles":1}""");
        var hints = AdminUserAccountEdit.InheritHints(User(), "g2", [Group, other], null);
        Assert.Equal(1, hints.MaxStreams);
        Assert.Equal(1, hints.MaxProfiles);
    }

    [Fact]
    public void AnAdminAndAnUngroupedAccountInheritTheServerDefaultsNotAGroup()
    {
        var defaults = Json("""{"admin":{"max_streams":0,"max_profiles":9},"ungrouped":{"max_streams":3,"max_profiles":2}}""");
        var admin = User(); admin["role"] = "admin"; admin["access_group_id"] = null; admin["max_profiles"] = 7;
        Assert.Equal(9, AdminUserAccountEdit.InheritHints(admin, "g1", [Group], defaults).MaxProfiles);
        var ungrouped = User(); ungrouped["access_group_id"] = null; ungrouped["max_profiles"] = 7;
        Assert.Equal(2, AdminUserAccountEdit.InheritHints(ungrouped, null, [Group], defaults).MaxProfiles);
    }

    [Fact]
    public void OnlyTheRowsThatChangedAreSentAndDefaultIsNull()
    {
        var user = User(); user["max_streams"] = 4; user["download_allowed"] = true;
        var draft = Json("""{"max_streams":null,"download_allowed":true,"download_transcode_allowed":false,"requests_allowed":null}""");
        var body = AdminUserAccountEdit.Diff(user, draft);
        Assert.Equal(["max_streams", "download_transcode_allowed"], body.Select(pair => pair.Key));
        Assert.Null(body["max_streams"]);
        Assert.False(body["download_transcode_allowed"]!.GetValue<bool>());
    }

    [Fact]
    public void VideoTranscodingSendsTheTwoFieldsTheOfficialCardSends()
    {
        Assert.Equal(((bool?)false, (long?)null), AdminUserVideoTranscoding.ToFields(new(AdminUserVideoTranscoding.Off)));
        Assert.Equal(((bool?)true, (long?)0), AdminUserVideoTranscoding.ToFields(new(AdminUserVideoTranscoding.Unlimited)));
        Assert.Equal(((bool?)true, (long?)3), AdminUserVideoTranscoding.ToFields(new(AdminUserVideoTranscoding.Limit, 3)));
        Assert.Equal(((bool?)null, (long?)null), AdminUserVideoTranscoding.ToFields(null));
        Assert.Equal(new AdminUserVideoTranscoding(AdminUserVideoTranscoding.Unlimited), AdminUserVideoTranscoding.FromEffective(true, 0));
        Assert.Equal(new AdminUserVideoTranscoding(AdminUserVideoTranscoding.Off), AdminUserVideoTranscoding.FromEffective(false, 4));
    }

    [Fact]
    public void QualityIsOneOfThreePresetsAndAnyIsTheEmptyString()
    {
        Assert.Equal("", AdminUserAccountEdit.QualityValue("any"));
        Assert.Equal("1080p", AdminUserAccountEdit.QualityValue("standard"));
        Assert.Equal("2160p", AdminUserAccountEdit.QualityValue("4k"));
        Assert.Equal("standard", AdminUserAccountEdit.QualityPreset("720p"));
        Assert.Equal("4k", AdminUserAccountEdit.QualityPreset("2160p"));
        Assert.Equal("any", AdminUserAccountEdit.QualityPreset(null));
    }

    [Fact]
    public void SignInSendsOnlyChangedRowsAndAnAdminLeavesItsGroup()
    {
        var user = User();
        Assert.Empty(AdminUserAccountEdit.SignInBody(user, "alice", "alice@example.test", "user", true, true, null));
        var body = AdminUserAccountEdit.SignInBody(user, "alice2", "alice@example.test", "admin", false, true, 5);
        Assert.Equal(["username", "role", "access_group_id", "enabled", "max_profiles"], body.Select(pair => pair.Key));
        Assert.Null(body["access_group_id"]);
        Assert.Equal(5, body["max_profiles"]!.GetValue<long>());
    }

    [Fact]
    public void AProfileLimitBackToDefaultSendsNullAndAnInvalidOneSendsNothing()
    {
        var user = User("\"max_profiles\":4,");
        var back = AdminUserAccountEdit.SignInBody(user, "alice", "alice@example.test", "user", true, true, null);
        Assert.True(back.ContainsKey("max_profiles")); Assert.Null(back["max_profiles"]);
        Assert.Empty(AdminUserAccountEdit.SignInBody(user, "alice", "alice@example.test", "user", true, false, null));
        Assert.True(AdminUserAccountEdit.Incomplete(true, false, true));
        Assert.False(AdminUserAccountEdit.Incomplete(true, false, false));
    }

    [Fact]
    public void LibraryAccessSendsTheGroupLibrariesAndPermissionsEachOnlyWhenChanged()
    {
        var user = User();
        Assert.Empty(AdminUserAccountEdit.LibraryBody(user, "g1", null, user["permissions"]));
        var body = AdminUserAccountEdit.LibraryBody(user, null, Json("""{"a":["3","1"]}""")["a"], AdminUserAccountEdit.SetPermission(user["permissions"], "metadata_curation", true));
        Assert.Null(body["access_group_id"]);
        Assert.Equal(new[] { "1", "3" }, body["library_ids"]!.AsArray().Select(node => node!.GetValue<string>()).OrderBy(x => x));
        Assert.Equal(["marker_edit", "metadata_curation"], body["permissions"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void AnAdminIsNeverGivenAGroupAndTheSameLibrariesInAnyOrderAreNotAChange()
    {
        var admin = User(); admin["role"] = "admin"; admin["access_group_id"] = null; admin["library_ids"] = Json("""{"a":["1","2"]}""")["a"]!.DeepClone();
        Assert.Empty(AdminUserAccountEdit.LibraryBody(admin, "g1", Json("""{"a":["2","1"]}""")["a"], admin["permissions"]));
    }

    [Fact]
    public void AGroupThatDoesNotAllowAPermissionLocksItAndAnAdminOrUngroupedAccountNeverIs()
    {
        var user = User();
        Assert.Equal((true, "Family"), AdminUserAccountEdit.PermissionLock(user, "g1", [Group], "metadata_curation"));
        Assert.Equal((false, "Family"), AdminUserAccountEdit.PermissionLock(user, "g1", [Group], "marker_edit"));
        Assert.False(AdminUserAccountEdit.PermissionLock(user, null, [Group], "metadata_curation").Locked);
        var admin = User(); admin["role"] = "admin";
        Assert.False(AdminUserAccountEdit.PermissionLock(admin, "g1", [Group], "metadata_curation").Locked);
    }

    [Fact]
    public void ARequestLimitIsValidWhenInheritedAndOtherwiseNeedsWholeNumbers()
    {
        Assert.True(new AdminRequestLimitDraft("inherit", "inherit", "", "").Valid);
        Assert.True(new AdminRequestLimitDraft("manual", "custom", "0", "7").Valid);
        Assert.True(new AdminRequestLimitDraft("manual", "custom", "0", "7").IsZero);
        Assert.False(new AdminRequestLimitDraft("manual", "custom", "", "7").MaxOk);
        Assert.False(new AdminRequestLimitDraft("manual", "custom", "5", "0").WindowOk);
        Assert.False(new AdminRequestLimitDraft("manual", "custom", "1.5", "7").Valid);
        Assert.True(new AdminRequestLimitDraft("manual", "unlimited", "", "").Valid);
    }

    [Fact]
    public void ARequestLimitSendsAllFourFieldsAndTheNumbersOnlyForACustomLimit()
    {
        var custom = new AdminRequestLimitDraft("auto", "custom", "12", "14").Body();
        Assert.Equal(["limit_mode", "approval_mode", "max_requests", "window_days"], custom.Select(pair => pair.Key));
        Assert.Equal(12, custom["max_requests"]!.GetValue<long>()); Assert.Equal(14, custom["window_days"]!.GetValue<long>());
        var inherit = new AdminRequestLimitDraft("manual", "unlimited", "12", "14").Body();
        Assert.Null(inherit["max_requests"]); Assert.Null(inherit["window_days"]);
        Assert.Equal("unlimited", inherit["limit_mode"]!.GetValue<string>());
    }

    [Fact]
    public void ALegacyBlockedRecordReadsAsInheritAndChangesCountOnlyWhatDiffers()
    {
        var saved = AdminRequestLimitDraft.From(Json("""{"limit_mode":"blocked","approval_mode":"blocked","max_requests":null,"window_days":null}"""));
        Assert.Equal(new AdminRequestLimitDraft("inherit", "inherit", "", ""), saved);
        var draft = new AdminRequestLimitDraft("auto", "custom", "3", "7");
        Assert.Equal(["approval_mode", "limit_mode", "max_requests", "window_days"], draft.Changes(saved));
        Assert.Equal(["approval_mode"], (draft with { Limit = "inherit" }).Changes(saved));
    }

    [Fact]
    public void InheritedRequestTermsComeFromTheGroupThenTheServer()
    {
        var settings = Json("""{"requests_enabled":true,"global_max_requests":10,"global_window_days":7,"global_auto_approval_enabled":false}""");
        var grouped = AdminRequestTermsResolver.Inherited(User(), Json("""{"limit_mode":"custom","max_requests":3,"window_days":2,"approval_mode":"auto"}"""), settings)!;
        Assert.True(grouped.QuotaFromGroup && grouped.ApprovalFromGroup && grouped.AutoApprove);
        Assert.Equal((3L, 2L), (grouped.Max, grouped.Days));
        var passes = AdminRequestTermsResolver.Inherited(User(), Json("""{"limit_mode":"inherit","approval_mode":"inherit","max_requests":null,"window_days":null}"""), settings)!;
        Assert.False(passes.QuotaFromGroup || passes.ApprovalFromGroup || passes.AutoApprove);
        Assert.Equal((10L, 7L), (passes.Max, passes.Days));
        Assert.Null(AdminRequestTermsResolver.Inherited(User(), null, settings));
        var admin = User(); admin["role"] = "admin";
        Assert.Equal(10L, AdminRequestTermsResolver.Inherited(admin, null, settings)!.Max);
    }
}
