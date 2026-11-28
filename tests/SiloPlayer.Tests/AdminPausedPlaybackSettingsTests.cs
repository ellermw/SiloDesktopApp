using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Admin > Playback "Paused playback" (official silo-server #5180, PausedPlaybackGroup.tsx at c59798116):
/// playback.paused_session_timeout_minutes, an integer from 0 to 1440, default 15, 0 turns it off.
/// </summary>
public sealed class AdminPausedPlaybackSettingsTests
{
    private const string Key = "playback.paused_session_timeout_minutes";

    [Fact]
    public void TheFieldIsTheOfficialOneAndFollowsWatchBehaviorAsItsOwnGroup()
    {
        var fields = AdminPlaybackSettingsRules.Fields;
        var field = Assert.Single(fields, item => item.Key == Key);
        Assert.Equal(("pausedPlayback", "number", "15", 0, 1440, false), (field.Group, field.Kind, field.Default, field.Min, field.Max, field.Advanced));
        // Official KEYS order: WATCH_KEYS, then PAUSED_PLAYBACK_KEYS, then the rest.
        var keys = AdminPlaybackSettingsRules.Keys.ToList();
        Assert.Equal(keys.IndexOf("playback.min_resume_threshold") + 1, keys.IndexOf(Key));
        Assert.Equal("Paused playback", AdminPlaybackAdministrationText.T("web.admin.settings.playback.pausedPlayback.group"));
        Assert.Equal("End paused playback after", AdminPlaybackAdministrationText.T("web.admin.settings.playback." + field.Label));
        Assert.Equal("minutes", AdminPlaybackAdministrationText.T("web.admin.settings.playback.unitMinutes"));
        Assert.Equal("0 to 1440. Ends a video paused this long in a Silo app, freeing its stream and any transcode; playing it again continues from the same spot. " +
            "Watch Together, casting and audiobooks are not affected. 0 turns this off.",
            AdminPlaybackAdministrationText.T("web.admin.settings.playback." + field.Description));
    }

    [Fact]
    public void AnUnsavedSettingReadsAsTheDefaultFifteenMinutes()
    {
        Assert.Equal("15", AdminPlaybackSettingsRules.Get(new Dictionary<string, string>(), Key));
        Assert.Equal("15", AdminPlaybackSettingsRules.Get(new Dictionary<string, string> { [Key] = "" }, Key));
        Assert.Equal("0", AdminPlaybackSettingsRules.Get(new Dictionary<string, string> { [Key] = "0" }, Key));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("15", true)]
    [InlineData("1440", true)]
    [InlineData("-1", false)]
    [InlineData("1441", false)]
    [InlineData("15.5", false)]
    [InlineData("fifteen", false)]
    [InlineData("", false)]
    public void ValuesFromZeroToADayAreAccepted(string value, bool valid)
    {
        var errors = AdminPlaybackSettingsRules.Validate(new Dictionary<string, string> { [Key] = value }, [Key]);
        if (valid) Assert.Empty(errors);
        else Assert.Equal("Enter a whole number from 0 to 1440.", errors[Key]);
    }

    [Fact]
    public void TheSettingIsAlwaysShownAndSearchable()
    {
        Assert.True(AdminPlaybackSettingsRules.IsShown(Key, new Dictionary<string, string>(), false, false, null));
        Assert.Equal("Paused playback", AdminText.T("web.admin.settings.search.playback.setting.pausedPlayback"));
        Assert.Equal("End paused playback after (minutes)", AdminText.T("web.admin.settings.search.playback.setting.endPausedPlaybackAfter"));
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Services", "Admin", "AdminRouteCatalog.cs"));
        Assert.Contains("\"showInContinueWatchingAfter\", \"pausedPlayback\", \"endPausedPlaybackAfter\", \"transcoding\"", source);
    }

    [Fact]
    public async Task AnEditIsDirtyUntilSavedAndOnlyItIsWritten()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminPlaybackSettingsRules.Keys, []);
        using var controller = new AdminPlaybackSettingsController(api, store, authority, false);
        string? saved = null;
        kit.Http.Respond = async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                saved = await request.Content!.ReadAsStringAsync(ct);
                var ack = AdminMonitorKit.Json("{\"values\":{\"" + Key + "\":\"30\"},\"restart_required\":false}");
                ack.Headers.ETag = new("\"settings-2\""); return ack;
            }
            var response = AdminMonitorKit.Json(path.EndsWith("effective") ? "{\"" + Key + "\":\"15\",\"markers.mode\":\"both\"}"
                : path.EndsWith("restart-keys") ? "{\"keys\":[],\"prefixes\":[]}" : "{\"configured\":[],\"managed_by_env\":[]}");
            response.Headers.ETag = new("\"settings-1\""); return response;
        };
        await controller.OpenAsync();
        Assert.Equal("15", controller.State.Values[Key]);
        Assert.Empty(controller.State.DirtyKeys);

        controller.Set(Key, "1441");
        Assert.Contains(Key, controller.State.DirtyKeys);
        Assert.Equal("Enter a whole number from 0 to 1440.", controller.State.Errors[Key]);
        await controller.SaveAsync();
        Assert.Null(saved);

        controller.Set(Key, "15");
        Assert.Empty(controller.State.DirtyKeys);
        controller.Set(Key, "30");
        Assert.Equal(new[] { Key }, controller.State.DirtyKeys);
        Assert.Empty(controller.State.Errors);
        await controller.SaveAsync();
        Assert.Contains("\"" + Key + "\":\"30\"", saved);
        Assert.DoesNotContain("playback.watched_threshold", saved);
        Assert.Empty(controller.State.DirtyKeys);
        Assert.Equal("30", controller.State.Values[Key]);
    }
}
