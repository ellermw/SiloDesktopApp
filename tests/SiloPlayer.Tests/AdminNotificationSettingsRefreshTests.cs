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
        Assert.Empty(h.Controller.State.DirtyKeys);
        // The wire answer really holds them (so this cannot pass on an answer that never had them), and the projection the page reads drops them.
        using (var lease = h.Authority.Capture())
        {
            var snapshot = await new AdminServerSettingsApi(h.Kit.Client).GetEffectiveAsync(lease, AdminNotificationSettingsController.Keys, AdminNotificationSettingsController.Secrets);
            Assert.Contains("notifications.ui_enabled", snapshot.Values.Keys);
            foreach (var key in RetiredProcessingKeys) Assert.DoesNotContain(key, snapshot.Values.Keys);
        }
        // The server echoes them in its answer to a write as well; nothing of that reaches the next request.
        h.Write = _ => Task.FromResult(Saved("""{"notifications.ui_enabled":"false","notifications.fanout_enabled":"false","notifications.retention.read_days":"7"}"""));
        h.Controller.Set("notifications.ui_enabled", "false");
        await h.Controller.SaveAsync();
        using (var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last()))
            Assert.Equal(["notifications.ui_enabled"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        h.Controller.Set("notifications.email.digest_hour", "9");
        await h.Controller.SaveAsync();
        using (var second = JsonDocument.Parse(h.Kit.Http.Bodies.Last()))
            Assert.Equal(["notifications.email.digest_hour"], second.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        // No request body of the whole session names a retired key, and none is a dirty or shown value.
        Assert.Equal(2, h.Writes.Length);
        foreach (var sent in h.Kit.Http.Bodies) foreach (var key in RetiredProcessingKeys) Assert.DoesNotContain(key, sent, StringComparison.Ordinal);
        Assert.Empty(h.Controller.State.DirtyKeys.Where(k => RetiredProcessingKeys.Contains(k)));
    }

    [Fact]
    public async Task AnOlderServerThatPausedItsPipelineIsReportedReadOnlyAndTheSwitchesAreStillNeverStagedOrSent()
    {
        // Production's shared store reads the two pipeline switches next to the page keys (AdminConfigureServices); they are still not form keys.
        foreach (var key in AdminNotificationSettingsController.RetiredPauseKeys) { Assert.Contains(key, RetiredProcessingKeys); Assert.DoesNotContain(key, AdminNotificationSettingsController.Keys); }
        var storeKeys = AdminNotificationSettingsController.Keys.Concat(AdminNotificationSettingsController.RetiredPauseKeys).ToArray();
        using var h = new Harness(storeKeys);
        var current = h.Effective;
        h.Effective = current.TrimEnd('}') + ",\"notifications.release_events_enabled\":\"true\",\"notifications.fanout_enabled\":\"false\",\"notifications.fanout.settle_seconds\":\"45\"}";
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State is { Loaded: true, RecordingPaused: false, SendingPaused: true });
        foreach (var key in RetiredProcessingKeys)
        {
            Assert.DoesNotContain(key, h.Controller.State.Values.Keys); Assert.DoesNotContain(key, h.Controller.State.PersistedValues.Keys);
            Assert.Throws<ArgumentException>(() => h.Controller.Set(key, "true"));
        }
        // A save carries only what was edited, and an answer that echoes the switch changes neither the form nor what is reported.
        h.Write = _ => Task.FromResult(Saved("""{"notifications.ui_enabled":"false","notifications.fanout_enabled":"false"}"""));
        h.Controller.Set("notifications.ui_enabled", "false");
        await h.Controller.SaveAsync();
        using (var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last()))
            Assert.Equal(["notifications.ui_enabled"], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        foreach (var sent in h.Kit.Http.Bodies) foreach (var key in RetiredProcessingKeys) Assert.DoesNotContain(key, sent, StringComparison.Ordinal);
        Assert.True(h.Controller.State is { SendingPaused: true, RecordingPaused: false }); Assert.Empty(h.Controller.State.DirtyKeys);
        // Recording off as well (a JSON boolean reads the same as the string).
        h.Effective = current.TrimEnd('}') + ",\"notifications.release_events_enabled\":false,\"notifications.fanout_enabled\":\"false\"}";
        await h.Controller.ReloadAsync(false);
        Assert.True(h.Controller.State is { Loaded: true, RecordingPaused: true, SendingPaused: true });
        // Both on, and a current server that no longer has them: nothing is reported.
        h.Effective = current.TrimEnd('}') + ",\"notifications.release_events_enabled\":\"true\",\"notifications.fanout_enabled\":\"true\"}";
        await h.Controller.ReloadAsync(false);
        Assert.True(h.Controller.State is { Loaded: true, RecordingPaused: false, SendingPaused: false });
        h.Effective = current;
        await h.Controller.ReloadAsync(false);
        Assert.True(h.Controller.State is { Loaded: true, RecordingPaused: false, SendingPaused: false });
        Assert.Single(h.Writes);
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
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645", "\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 family", "\U0001F468\u200D\U0001F469\u200D\U0001F467 family")]
    [InlineData("a \u200D b", "a b")]
    [InlineData("\u200Dx", "x")]
    [InlineData("x\u200C", "x")]
    [InlineData("x\u200D\u200Cy", "x\u200Dy")]
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

    [Fact]
    public void ZeroWidthJoinersAreKeptWhereTheyJoinAndEveryOtherInvisibleOrDirectionalCharacterIsNeutralized()
    {
        // Kept: joiner and non-joiner inside a sequence.
        Assert.Equal("\U0001F469\u200D\U0001F4BB", AdminNotificationText.Plain("\U0001F469\u200D\U0001F4BB"));
        Assert.Equal("a\u200Cb", AdminNotificationText.Plain("a\u200Cb"));
        // Direction controls, other format characters, control characters, line and paragraph separators: a space each, never kept.
        var neutralized = new List<int> { 0x200E, 0x200F, 0x061C, 0x200B, 0x2060, 0xFEFF, 0x00AD, 0x180E, 0x2028, 0x2029, 0x0085, 0x0000, 0x0007, 0x001F, 0x007F, 0x0080, 0x009F };
        neutralized.AddRange(Enumerable.Range(0x202A, 5)); neutralized.AddRange(Enumerable.Range(0x2066, 4));
        foreach (var code in neutralized) Assert.Equal("a b", AdminNotificationText.Plain("a" + char.ConvertFromUtf32(code) + "b"));
        // A joiner after a neutralized character joins nothing and is dropped.
        Assert.Equal("a b", AdminNotificationText.Plain("a‮‍b"));
        Assert.Equal("x\u200Dy", AdminNotificationText.Plain("x\u200D\u200D\u200D\u200Dy"));
    }

    [Fact]
    public void JoinersCountTowardTheBoundAndNeverEndTheBoundedText()
    {
        var text = "x" + string.Concat(Enumerable.Repeat("\u200Dy", 100));
        var bounded = AdminNotificationText.Plain(text, 10);
        Assert.EndsWith("…", bounded); Assert.True(bounded.Length <= 11); Assert.NotEqual('\u200D', bounded[^2]);
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
        // The page tells the admin when a refused command sent nothing, so a refusal is reported, not swallowed.
        Assert.False(h.Controller.IsShown(shown));
        Assert.False(await h.Controller.TestChannelAsync(shown)); Assert.False(await h.Controller.RotateChannelAsync(shown));
        Assert.Empty(h.Writes);
        h.Write = r => Task.FromResult(Reply(r.RequestUri!.AbsolutePath.EndsWith("test") ? """{"ok":true,"duration_ms":3}""" : """{"signing_secret":"synthetic-once"}"""));
        var fresh = h.Controller.State.Channels.Single(c => c.Id == "c1");
        Assert.True(h.Controller.IsShown(fresh));
        Assert.True(await h.Controller.TestChannelAsync(fresh));
        Assert.Equal(["POST /api/v2/admin/notifications/server-channels/c1/test"], h.Writes);
    }

    private const string DiscordEffective = """{"notifications.ui_enabled":"true","discord.client_id":"app","email.enabled":"true","email.smtp_host":"smtp.example.test","email.from_address":"silo@example.test"}""";
    private static string[] BodyKeys(Harness h) { using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last()); return body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name).Order().ToArray(); }
    private static string[] BodyValues(Harness h) { using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last()); return body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Value.GetString()!).Distinct().ToArray(); }

    [Fact]
    public async Task DiscordClearWithNoManagedKeyClearsAllThreeAsBefore()
    {
        using var h = new Harness { Effective = DiscordEffective }; await h.Controller.OpenAsync();
        Assert.Equal(["discord.bot_token", "discord.client_id", "discord.client_secret"], AdminNotificationSettingsController.ClearableDiscordKeys(h.Controller.State).Order());
        Assert.True(await h.Controller.ClearDiscordAsync());
        Assert.Equal(["discord.bot_token", "discord.client_id", "discord.client_secret"], BodyKeys(h)); Assert.Equal([""], BodyValues(h));
    }

    [Fact]
    public async Task DiscordClearWithOneManagedKeyClearsTheEditableOnesAndNeverSendsTheManagedKey()
    {
        // Official clearAll: Object.fromEntries(editableKeys.map(key => [key, ""])). The managed secret is not in the body.
        using var h = new Harness { Effective = DiscordEffective, Sensitive = """{"configured":["discord.client_secret","discord.bot_token"],"managed_by_env":["discord.client_secret"]}""" };
        await h.Controller.OpenAsync();
        Assert.Equal(["discord.bot_token", "discord.client_id"], AdminNotificationSettingsController.ClearableDiscordKeys(h.Controller.State).Order());
        // A draft typed for an editable secret before the clear does not survive as a value other than the clear.
        h.Controller.Set("discord.bot_token", "synthetic-typed");
        Assert.True(await h.Controller.ClearDiscordAsync());
        Assert.Equal(["discord.bot_token", "discord.client_id"], BodyKeys(h)); Assert.Equal([""], BodyValues(h));
        Assert.DoesNotContain("discord.client_secret", h.Kit.Http.Bodies.Last(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-typed", h.Kit.Http.Bodies.Last(), StringComparison.Ordinal);
        // The managed one is still stored (the environment owns it); the editable ones are gone and Clear is no longer offered.
        Assert.Contains("discord.client_secret", h.Controller.State.Configured); Assert.DoesNotContain("discord.bot_token", h.Controller.State.Configured);
        Assert.Empty(AdminNotificationSettingsController.ClearableDiscordKeys(h.Controller.State));
        Assert.False(await h.Controller.ClearDiscordAsync()); Assert.Single(h.Writes);
    }

    [Fact]
    public async Task DiscordClearWithEveryKeyManagedIsNotOfferedAndSendsNothing()
    {
        using var h = new Harness { Effective = DiscordEffective, Sensitive = """{"configured":["discord.client_secret","discord.bot_token"],"managed_by_env":["discord.client_id","discord.client_secret","discord.bot_token"]}""" };
        await h.Controller.OpenAsync();
        Assert.Empty(AdminNotificationSettingsController.ClearableDiscordKeys(h.Controller.State));
        Assert.False(await h.Controller.ClearDiscordAsync()); Assert.Empty(h.Writes); Assert.Null(h.Controller.State.Error);
    }

    [Fact]
    public async Task DiscordClearWhenNoEditableKeyIsStoredIsNotOfferedAndSendsNothing()
    {
        // Only the managed secret is stored; the client id is empty and the token is not configured.
        using var h = new Harness { Effective = DiscordEffective.Replace("\"discord.client_id\":\"app\"", "\"discord.client_id\":\"\""), Sensitive = """{"configured":["discord.client_secret"],"managed_by_env":["discord.client_secret"]}""" };
        await h.Controller.OpenAsync();
        Assert.Empty(AdminNotificationSettingsController.ClearableDiscordKeys(h.Controller.State));
        Assert.False(await h.Controller.ClearDiscordAsync()); Assert.Empty(h.Writes);
        // Nothing stored at all, nothing managed: also nothing to clear.
        using var empty = new Harness { Effective = DiscordEffective.Replace("\"discord.client_id\":\"app\"", "\"discord.client_id\":\"  \""), Sensitive = """{"configured":[],"managed_by_env":[]}""" };
        await empty.Controller.OpenAsync();
        Assert.Empty(AdminNotificationSettingsController.ClearableDiscordKeys(empty.Controller.State));
        Assert.False(await empty.Controller.ClearDiscordAsync()); Assert.Empty(empty.Writes);
    }

    [Fact]
    public async Task ADiscordSaveIsAcknowledgedOnlyWhenThatWriteWasAnsweredForTheOwnerThatSentIt()
    {
        using var h = new Harness { Effective = DiscordEffective }; await h.Controller.OpenAsync();
        Assert.False(await h.Controller.SaveDiscordAsync());                         // nothing staged: nothing sent
        h.Controller.Set("discord.bot_token", "synthetic-one"); h.Write = _ => Task.FromResult(Saved("""{"discord.client_id":"app"}"""));
        Assert.True(await h.Controller.SaveDiscordAsync());                          // answered
        foreach (var status in new[] { HttpStatusCode.PreconditionFailed, HttpStatusCode.ServiceUnavailable, HttpStatusCode.UnprocessableEntity })
        {
            await h.Controller.ReloadAsync(true);
            h.Controller.Set("discord.bot_token", "synthetic-two");
            h.Write = _ => Task.FromResult(Reply("""{"type":"precondition_failed","title":"Failed","status":412}""", status));
            Assert.False(await h.Controller.SaveDiscordAsync());                     // refused or unknown: no "saved"
        }
    }

    [Fact]
    public async Task ADiscordSaveWhoseOwnerWasRetiredWhileItWasInFlightIsNotAcknowledged()
    {
        using var h = new Harness { Effective = DiscordEffective }; await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set("discord.bot_token", "synthetic-held");
        var save = h.Controller.SaveDiscordAsync();
        Assert.True(h.Controller.State.Saving);
        // The account or server changes while the write is on the wire.
        h.Controller.Suspend();
        pending.SetResult(Saved("""{"discord.client_id":"app"}"""));
        Assert.False(await save);
        // The fresh state looks like a finished save (no error, not saving, nothing dirty): the old toast check read that as success.
        Assert.True(h.Controller.State is { Error: null, Saving: false }); Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Single(h.Writes);
        // A reopened page does not inherit an acknowledgement either.
        await h.Controller.OpenAsync();
        Assert.False(await h.Controller.SaveDiscordAsync()); Assert.Single(h.Writes);
    }

    [Fact]
    public async Task ADiscordClearWhoseOwnerWasRetiredWhileItWasInFlightReportsOnlyThatItWasSent()
    {
        using var h = new Harness { Effective = DiscordEffective }; await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        var clear = h.Controller.ClearDiscordAsync();
        var owner = h.Controller.State.Owner!;
        h.Controller.Suspend(); pending.SetResult(Saved("""{"discord.client_id":""}"""));
        Assert.True(await clear);                    // it was sent; the page confirms the owner is still current before saying anything
        Assert.False(owner.IsCurrent);
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
