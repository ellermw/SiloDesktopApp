using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Contract after official 4a1ee058e: the Processing settings were removed upstream (f42b93271). Synthetic
/// values only; the harness and helpers are the ones of the main Notifications test class.</summary>
public sealed partial class AdminNotificationSettingsTests
{
    private static readonly string[] RetiredProcessingKeys =
    [
        "notifications.release_events_enabled", "notifications.fanout_enabled", "notifications.fanout.settle_seconds",
        "notifications.fanout.max_event_age_hours", "notifications.retention.read_days", "notifications.retention.unread_days", "notifications.retention.event_days",
    ];

    [Fact]
    public async Task RetiredProcessingSettingsAreNotPageKeysAndCanNeitherBeStagedNorWritten()
    {
        foreach (var key in RetiredProcessingKeys)
        {
            Assert.DoesNotContain(key, AdminNotificationSettingsController.PageKeys);
            Assert.DoesNotContain(key, AdminNotificationSettingsController.Keys);
            Assert.Equal("", AdminNotificationSettingsController.Display(key, null));
        }
        using var h = new Harness(); await h.Controller.OpenAsync();
        foreach (var key in RetiredProcessingKeys) Assert.Throws<ArgumentException>(() => h.Controller.Set(key, "false"));
        Assert.Empty(h.Controller.State.DirtyKeys);
        // The wire layer refuses them too, so no draft or default can reach a request body.
        using var lease = h.Authority.Capture();
        var api = new AdminServerSettingsApi(h.Kit.Client);
        foreach (var key in RetiredProcessingKeys)
            await Assert.ThrowsAsync<ArgumentException>(() => api.PutAsync(lease, new Dictionary<string, string> { [key] = "false" }, "\"one\"", AdminNotificationSettingsController.Keys, AdminNotificationSettingsController.Secrets));
        Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task AnOlderServersAnswerStillHoldingTheRetiredKeysIsReadWithoutThemAndASaveNeverEchoesThem()
    {
        using var h = new Harness();
        h.Effective = h.Effective.TrimEnd('}') + ",\"notifications.release_events_enabled\":\"false\",\"notifications.fanout_enabled\":\"false\",\"notifications.fanout.settle_seconds\":\"45\",\"notifications.fanout.max_event_age_hours\":\"12\",\"notifications.retention.read_days\":\"7\",\"notifications.retention.unread_days\":\"8\",\"notifications.retention.event_days\":\"9\"}";
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.Loaded);
        foreach (var key in RetiredProcessingKeys) { Assert.DoesNotContain(key, h.Controller.State.Values.Keys); Assert.DoesNotContain(key, h.Controller.State.PersistedValues.Keys); }
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Write = _ => Task.FromResult(Saved("""{"notifications.ui_enabled":"false"}"""));
        h.Controller.Set("notifications.ui_enabled", "false");
        await h.Controller.SaveAsync();
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last());
        Assert.Equal(["notifications.ui_enabled"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        foreach (var key in RetiredProcessingKeys) Assert.DoesNotContain(key, h.Controller.State.Values.Keys);
    }

    [Fact]
    public async Task ANewServersAnswerWithoutTheRetiredKeysLoadsAndEveryRemainingPageKeyHasADisplayValue()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.Loaded); Assert.Null(h.Controller.State.Error);
        foreach (var key in AdminNotificationSettingsController.PageKeys) Assert.NotEqual("", AdminNotificationSettingsController.Display(key, null));
        // A saved page change carries only what was edited.
        h.Write = _ => Task.FromResult(Saved("""{"notifications.email.digest_hour":"9","notifications.server_channels.batch_seconds":"120"}"""));
        h.Controller.Set("notifications.email.digest_hour", "9"); h.Controller.Set("notifications.server_channels.batch_seconds", "120");
        await h.Controller.SaveAsync();
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last());
        Assert.Equal(["notifications.email.digest_hour", "notifications.server_channels.batch_seconds"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public void TheTextTableCarriesNoProcessingPausedOrDaysHoursMessages()
    {
        var keys = AdminNotificationText.Keys;
        Assert.DoesNotContain(keys, k => k.Contains(".processing.", StringComparison.Ordinal));
        Assert.DoesNotContain("web.admin.settings.notifications.pausedRecording", keys);
        Assert.DoesNotContain("web.admin.settings.notifications.pausedSending", keys);
        Assert.DoesNotContain("web.admin.settings.notifications.unit.days", keys);
        Assert.DoesNotContain("web.admin.settings.notifications.unit.hours", keys);
        Assert.Contains("web.admin.settings.notifications.unit.seconds", keys);
        Assert.Equal("seconds", AdminNotificationText.T("web.admin.settings.notifications.unit.seconds"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Plain name", "Plain name")]
    [InlineData("  spaced \t\r\n out  ", "spaced out")]
    [InlineData("a\u0000b\u0007c", "a b c")]
    [InlineData("line\u2028break\u2029para\u0085next", "line break para next")]
    [InlineData("gate\u202Egnp.exe", "gate gnp.exe")]
    [InlineData("iso\u2066late\u2069d\u200Fx\u061Cy", "iso late d x y")]
    [InlineData("zero\u200Bwidth\uFEFFjoin", "zero width join")]
    [InlineData("\u202E\u2066", "")]
    public void ThirdPartyNamesAreNeutralizedForConfirmations(string? input, string expected) => Assert.Equal(expected, AdminNotificationText.Plain(input));

    [Fact]
    public void ThirdPartyNamesAreBoundedWithoutSplittingAPairAndNeverContainControlOrBidiCharacters()
    {
        var long64 = new string('x', 200);
        var bounded = AdminNotificationText.Plain(long64, 64);
        Assert.Equal(new string('x', 64) + "…", bounded);
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 80));
        var cut = AdminNotificationText.Plain(emoji, 10);
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 10)) + "…", cut);
        foreach (var ch in AdminNotificationText.Plain("a\u202E\u2028\u0000\u200Bb\r\nc" + new string('z', 500)))
            Assert.False(char.IsControl(ch) || ch is '\u202E' or '\u2028' or '\u200B');
        Assert.Equal("exact", AdminNotificationText.Plain("exact", 5));
    }

    private const string TwoChannels = """{"items":[{"id":"c1","name":"Community","type":"generic","url_host":"example.test","enabled":true,"notify_new_movies":true,"notify_new_episodes":true,"notify_new_audiobooks":true,"notify_new_ebooks":true,"notify_request_submitted":false,"notify_request_approved":false,"notify_request_declined":false,"notify_request_fulfilled":false,"consecutive_failures":0},{"id":"c2","name":"Other","type":"discord","url_host":"discord.test","enabled":true,"notify_new_movies":true,"notify_new_episodes":true,"notify_new_audiobooks":true,"notify_new_ebooks":true,"notify_request_submitted":false,"notify_request_approved":false,"notify_request_declined":false,"notify_request_fulfilled":false,"consecutive_failures":0}],"page":{"has_more":false}}""";

    [Fact]
    public async Task ADeleteActsOnExactlyTheConfirmedChannelAndIsRefusedUnsentWhenItChangedMeanwhile()
    {
        using var h = new Harness { Channels = TwoChannels }; await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        var confirmed = h.Controller.State.Channels.Single(c => c.Id == "c1");
        // The server renamed the channel while the confirmation was open: the reload shows another name for the same id.
        h.Channels = TwoChannels.Replace("\"Community\"", "\"Renamed\"");
        await h.Controller.ReloadAsync(true);
        Assert.False(await h.Controller.DeleteChannelAsync(confirmed));
        Assert.Empty(h.Writes); Assert.Null(h.Controller.State.Error); Assert.False(h.Controller.State.OperationPending);
        var current = h.Controller.State.Channels.Single(c => c.Id == "c1");
        Assert.True(await h.Controller.DeleteChannelAsync(current));
        Assert.Equal(["DELETE /api/v2/admin/notifications/server-channels/c1"], h.Writes);
        // Another channel is never touched, and the same confirmation cannot be replayed once the outcome is unknown.
        h.Write = _ => throw new HttpRequestException("synthetic transport loss");
        var other = h.Controller.State.Channels.Single(c => c.Id == "c2");
        await h.Controller.DeleteChannelAsync(other);
        Assert.True(h.Controller.State.OperationUnknown);
        Assert.False(await h.Controller.DeleteChannelAsync(other));
        Assert.Equal(2, h.Writes.Length);
    }

    [Fact]
    public async Task ATestOrRotationOfAChangedChannelSendsNothing()
    {
        using var h = new Harness { Channels = TwoChannels }; await h.Controller.OpenAsync();
        var shown = h.Controller.State.Channels.Single(c => c.Id == "c1");
        h.Channels = TwoChannels.Replace("example.test", "elsewhere.test");
        await h.Controller.ReloadAsync(true);
        await h.Controller.TestChannelAsync(shown); await h.Controller.RotateChannelAsync(shown);
        Assert.Empty(h.Writes);
        h.Write = r => Task.FromResult(Reply(r.RequestUri!.AbsolutePath.EndsWith("test") ? """{"ok":true,"duration_ms":3}""" : """{"signing_secret":"synthetic-once"}"""));
        var fresh = h.Controller.State.Channels.Single(c => c.Id == "c1");
        await h.Controller.TestChannelAsync(fresh);
        Assert.Equal(["POST /api/v2/admin/notifications/server-channels/c1/test"], h.Writes);
    }

    [Fact]
    public async Task ClearingTheRelayOrDiscordReportsWhetherAnythingWasSent()
    {
        using var h = new Harness(); await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        Assert.True(await h.Controller.ClearRelayAsync());
        h.Write = _ => Task.FromResult(Saved("""{"discord.client_id":""}"""));
        Assert.True(await h.Controller.ClearDiscordAsync());
        h.Controller.Suspend();
        Assert.False(await h.Controller.ClearRelayAsync());
        Assert.False(await h.Controller.ClearDiscordAsync());
        Assert.False(await h.Controller.RegisterRelayAsync());
    }
}
