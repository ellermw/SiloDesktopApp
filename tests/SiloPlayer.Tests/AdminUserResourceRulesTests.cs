using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official 217ff25 rules for access groups, request terms and device overrides. Synthetic values only.</summary>
public sealed class AdminUserResourceRulesTests
{
    private static JsonElement J(string value) => JsonDocument.Parse(value).RootElement.Clone();

    [Theory]
    [InlineData("8", 8000)] [InlineData("1.5", 1500)] [InlineData("1,5", 1500)] [InlineData(".25", 250)] [InlineData(" 12 ", 12000)] [InlineData("0.001", 1)]
    public void MbpsParsesOnlyWhatTheApiCanStoreExactly(string text, int kbps) => Assert.Equal(kbps, AdminStreamBitrate.ParseMbps(text));

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("0.")] [InlineData("abc")] [InlineData("1.2345")] [InlineData("-3")] [InlineData("1e3")] [InlineData("99999999999999999999")] [InlineData("2200000")]
    public void MbpsRefusesEmptyZeroFractionalKbpsAndOverflow(string text) => Assert.Null(AdminStreamBitrate.ParseMbps(text));

    [Fact]
    public void BitrateChoiceNamesUnlimitedPresetOrCustomAndNeverInventsOne()
    {
        Assert.Null(AdminStreamBitrate.Choice(null));
        Assert.Equal("0", AdminStreamBitrate.Choice(0));
        Assert.Equal("8000", AdminStreamBitrate.Choice(8000));
        Assert.Equal("custom", AdminStreamBitrate.Choice(1500));
        Assert.Equal("1.5", AdminStreamBitrate.MbpsText(1500));
        Assert.True(AdminStreamBitrate.IsLow(999)); Assert.False(AdminStreamBitrate.IsLow(0)); Assert.False(AdminStreamBitrate.IsLow(1000));
        Assert.Equal("Unlimited", AdminStreamBitrate.Format(0)); Assert.Equal("8 Mbps", AdminStreamBitrate.Format(8000));
    }

    [Fact]
    public void ExactlyTheOfficialQualityPresetsRoundTripAndEverythingElseReadsAsAny()
    {
        Assert.Equal("standard", AdminAccessGroupBody.QualityPreset("720p")); Assert.Equal("standard", AdminAccessGroupBody.QualityPreset(" Standard "));
        Assert.Equal("4k", AdminAccessGroupBody.QualityPreset("UHD")); Assert.Equal("any", AdminAccessGroupBody.QualityPreset("surprise"));
        Assert.Equal("1080p", AdminAccessGroupBody.QualityValue("standard")); Assert.Equal("2160p", AdminAccessGroupBody.QualityValue("4k")); Assert.Equal("", AdminAccessGroupBody.QualityValue("any"));
    }

    private const string Stored = """
        {"id":"2","name":" Family ","description":" Home ","library_ids":["3","1"],"max_playback_quality":"720p","download_allowed":false,"download_transcode_allowed":true,
         "transcode_allowed":true,"audio_transcode_allowed":false,"max_streams":-1,"max_transcodes":2,"max_remote_stream_bitrate_kbps":1500,"max_local_stream_bitrate_kbps":0,
         "max_profiles":5,"allowed_permissions":["marker_edit","metadata_curation"],"requests_allowed":true,"is_default":false,"created_at":"x","updated_at":"y"}
        """;

    [Fact]
    public void AnUntouchedDraftEqualsTheStoredGroupAndKeepsValuesThatFailClientChecks()
    {
        var stored = AdminAccessGroupBody.Stored(J(Stored));
        Assert.Equal(AdminAccessGroupBody.Keys.Count, stored.Count);
        Assert.Equal("Family", stored["name"]!.GetValue<string>()); Assert.Equal("1080p", stored["max_playback_quality"]!.GetValue<string>());
        Assert.False(stored["download_transcode_allowed"]!.GetValue<bool>()); // a disabled download cannot keep a transcoded-download flag
        Assert.Equal(-1, stored["max_streams"]!.GetValue<int>()); // raw stored value stays, never blanked to zero
        Assert.True(AdminAccessGroupBody.Same(AdminAccessGroupBody.ForSave(stored), AdminAccessGroupBody.Stored(J(Stored))));
        Assert.False(stored.ContainsKey("id")); Assert.False(stored.ContainsKey("updated_at")); // only the official request fields are ever sent
    }

    [Fact]
    public void ReorderingLibrariesOrPermissionsIsNotAnEditButChangingThemIs()
    {
        var a = AdminAccessGroupBody.Stored(J(Stored)); var b = AdminAccessGroupBody.Stored(J(Stored));
        b["library_ids"] = new JsonArray("1", "3"); b["allowed_permissions"] = new JsonArray("metadata_curation", "marker_edit");
        Assert.True(AdminAccessGroupBody.Same(a, b));
        b["library_ids"] = new JsonArray("1"); Assert.False(AdminAccessGroupBody.Same(a, b));
        b["library_ids"] = null; Assert.False(AdminAccessGroupBody.Same(a, b)); // null (all libraries) differs from a list
        var empty = AdminAccessGroupBody.Stored(J(Stored)); empty["allowed_permissions"] = new JsonArray();
        Assert.False(AdminAccessGroupBody.Same(a, empty)); // none differs from all
    }

    [Fact]
    public void SaveBodyIsJsonWithOnlyOfficialFieldsAndNullVersusEmptyPreserved()
    {
        var draft = AdminAccessGroupBody.Stored(J(Stored)); draft["library_ids"] = null; draft["allowed_permissions"] = new JsonArray(); draft["name"] = " Kids ";
        var element = JsonSerializer.SerializeToElement(AdminAccessGroupBody.ForSave(draft));
        var names = element.EnumerateObject().Select(x => x.Name).ToArray();
        Assert.Equal(AdminAccessGroupBody.Keys.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.Null, element.GetProperty("library_ids").ValueKind);
        Assert.Equal(JsonValueKind.Array, element.GetProperty("allowed_permissions").ValueKind);
        Assert.Equal("Kids", element.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Number, element.GetProperty("max_profiles").ValueKind);
    }

    [Fact]
    public void MovingFromNoGroupListsWhatTheMemberLosesAndEqualPoliciesListNothing()
    {
        var target = J("""{"library_ids":["3"],"max_playback_quality":"2160p","download_allowed":false,"download_transcode_allowed":false,"transcode_allowed":true,"audio_transcode_allowed":true, "max_streams":2,"max_transcodes":0,"max_remote_stream_bitrate_kbps":8000,"max_local_stream_bitrate_kbps":0,"max_profiles":3,"allowed_permissions":["marker_edit"],"requests_allowed":true}""");
        var changes = AdminAccessGroupBody.PolicyChanges(AdminAccessGroupBody.NoGroupPolicy, target, new Dictionary<string, string> { ["3"] = "Movies" });
        var byLabel = changes.ToDictionary(x => x.Label);
        Assert.Equal("All libraries", byLabel["Libraries"].From); Assert.Equal("Movies", byLabel["Libraries"].To);
        Assert.Equal("Unlimited", byLabel["Concurrent streams"].From); Assert.Equal("2", byLabel["Concurrent streams"].To);
        Assert.Equal("8 Mbps", byLabel["Remote stream bitrate"].To);
        Assert.DoesNotContain("Requests", byLabel.Keys); Assert.DoesNotContain("Local stream bitrate", byLabel.Keys);
        Assert.Empty(AdminAccessGroupBody.PolicyChanges(target, target, new Dictionary<string, string>()));
    }

    private static readonly AdminRequestServerDefaults Server = new(true, 10, 7, false);
    [Fact]
    public void RequestTermsResolveGroupLayerOverServerAndNameTheSource()
    {
        var group = new AdminRequestSource("group", "Kids");
        var terms = AdminRequestAccess.Resolve([(group, new AdminRequestLayer("custom", 3, null, "auto"))], Server);
        Assert.Equal(new AdminRequestQuota(false, 3, 7), terms.Quota); // a missing window comes from the server
        Assert.True(terms.AutoApprove); Assert.Equal(group, terms.QuotaSource);
        var inherited = AdminRequestAccess.Resolve([(group, new AdminRequestLayer("inherit", null, null, "inherit"))], Server);
        Assert.Equal(new AdminRequestQuota(false, 10, 7), inherited.Quota); Assert.False(inherited.AutoApprove); Assert.Equal("server", inherited.QuotaSource.Kind);
        Assert.True(AdminRequestAccess.Resolve([(group, new AdminRequestLayer("unlimited", null, null, "manual"))], Server).Quota.Unlimited);
        Assert.Equal("3 requests per 7 days", AdminRequestAccess.FormatQuota(terms.Quota));
        Assert.Equal("no new requests (0 per day)", AdminRequestAccess.FormatQuota(new(false, 0, 1)));
        Assert.Equal("an admin approves", AdminRequestAccess.FormatApproval(false));
        Assert.Equal("Kids group", AdminRequestAccess.GroupLabel("Kids")); Assert.Equal("Default Group", AdminRequestAccess.GroupLabel("Default Group"));
    }

    [Fact]
    public void RequestTermsChangesReportOnlyWhatMembersSee()
    {
        var strict = new AdminRequestLayer("custom", 1, 1, "manual"); var open = new AdminRequestLayer("unlimited", null, null, "auto");
        var changes = AdminRequestAccess.TermsChanges(strict, open, Server);
        Assert.Equal(["Request limit", "Request approval"], changes.Select(x => x.Label).ToArray());
        Assert.Empty(AdminRequestAccess.TermsChanges(strict, strict, Server));
        Assert.Equal("Admin approves · 1 per day", AdminRequestAccess.GroupSummary(strict));
    }

    [Fact]
    public void RequestLimitDraftReadsBlockedAsInheritCountsOnlyRealEditsAndSendsExactBody()
    {
        var baseline = AdminRequestLimitDraft.From(J("""{"limit_mode":"blocked","approval_mode":"manual","max_requests":null,"window_days":null}"""));
        Assert.Equal("inherit", baseline.Limit); Assert.Equal("manual", baseline.Approval);
        Assert.Equal(0, baseline.Changes(baseline));
        var custom = baseline with { Limit = "custom", MaxRequests = "", WindowDays = "" };
        Assert.True(custom.HasErrors); Assert.NotNull(custom.MaxRequestsError); Assert.NotNull(custom.WindowDaysError);
        Assert.Equal(1, (baseline with { Approval = "auto" }).Changes(baseline));
        Assert.Equal(0, (baseline with { MaxRequests = "9" }).Changes(baseline)); // the numbers count only for a custom limit
        var ok = custom with { MaxRequests = "0", WindowDays = "7" };
        Assert.False(ok.HasErrors); Assert.True(ok.IsZeroLimit);
        var body = JsonSerializer.SerializeToElement(ok.Body());
        Assert.Equal(new[] { "limit_mode", "approval_mode", "max_requests", "window_days" }.Order().ToArray(), body.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(0, body.GetProperty("max_requests").GetInt32()); Assert.Equal(7, body.GetProperty("window_days").GetInt32());
        var inherit = JsonSerializer.SerializeToElement((ok with { Limit = "inherit" }).Body());
        Assert.Equal(JsonValueKind.Null, inherit.GetProperty("max_requests").ValueKind); Assert.Equal(JsonValueKind.Null, inherit.GetProperty("window_days").ValueKind);
        Assert.Null(AdminRequestLimitDraft.WholeNumber("99999999999", 0)); Assert.Null(AdminRequestLimitDraft.WholeNumber("1.5", 0)); Assert.Null(AdminRequestLimitDraft.WholeNumber("0", 1));
    }
}
