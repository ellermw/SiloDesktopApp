using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Official silo-server 3b2b32168: #3550 plugin-provided notification destinations and #4409 next-episode notifications.</summary>
public sealed class NotificationPluginDestinationTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

    // --- payloads ------------------------------------------------------------------------------

    private const string ChannelsJson = """
        {"items":[
          {"plugin_id":"ntfy","capability_id":"push","display_name":"ntfy","description":"Send to an ntfy topic","settings_form":{"fields":[
            {"key":"server","label":"Server","control":"text","type":"string","required":true,"secret":false,"default_value":"https://ntfy.sh","placeholder":"https://ntfy.sh"},
            {"key":"topic","label":"Topic","control":"text","type":"string","required":true,"secret":false,"min_length":3,"max_length":64},
            {"key":"token","label":"Access token","control":"password","type":"string","required":false,"secret":true},
            {"key":"port","label":"Port","control":"number","type":"integer","required":false,"secret":false,"minimum":1,"maximum":65535},
            {"key":"markdown","label":"Markdown","control":"switch","type":"boolean","required":false,"secret":false},
            {"key":"priority","label":"Priority","control":"select","type":"integer","required":false,"secret":false,"options":[{"label":"Low","value":2},{"label":"High","value":4}]}
          ]}},
          {"plugin_id":"pushover","capability_id":"app","display_name":"Pushover","description":"","settings_form":{"fields":[]}}
        ]}
        """;

    private static string Destination(string id, string name, string etag = "\"v1\"", bool available = true, string settings = """{"server":"https://ntfy.sh","topic":"silo","port":8080,"markdown":true,"priority":4}""",
        string secrets = """["token"]""", string? lastFailureAt = null, string? lastSuccessAt = null, string? lastFailureMessage = null, string? disabledReason = null)
        => $$"""
        {"id":"{{id}}","etag":{{Str(etag)}},"name":"{{name}}","plugin_id":"ntfy","capability_id":"push","channel_name":"ntfy","available":{{(available ? "true" : "false")}},"enabled":true,
         "notify_next_up":true,"notify_requests":false,"consecutive_failures":0,"created_at":"2026-11-20T00:00:00.000Z","disabled_reason":{{Str(disabledReason)}},
         "last_failure_at":{{Str(lastFailureAt)}},"last_failure_message":{{Str(lastFailureMessage)}},"last_success_at":{{Str(lastSuccessAt)}},
         "secret_settings_set":{{secrets}},"settings":{{settings}}}
        """;
    private static string Page(bool more, params string[] rows)
        => "{\"items\":[" + string.Join(",", rows) + "],\"page\":{\"has_more\":" + (more ? "true,\"next_cursor\":\"c/1\"" : "false") + "}}";
    private static string Str(string? value) => value == null ? "null" : JsonSerializer.Serialize(value);

    private static NotificationPluginChannel Ntfy() => JsonSerializer.Deserialize<NotificationPluginChannelCollection>(ChannelsJson, Wire)!.Items![0];
    private static NotificationPluginDestination Stored(string secrets = """["token"]""", string settings = """{"server":"https://ntfy.sh","topic":"silo","port":8080,"markdown":true,"priority":4}""")
        => JsonSerializer.Deserialize<NotificationPluginDestination>(Destination("d1", "Phone", secrets: secrets, settings: settings), Wire)!;

    // --- models and requests --------------------------------------------------------------------

    [Fact]
    public void CapabilityChannelsAndDestinationsReadTheOfficialShape()
    {
        var capability = JsonSerializer.Deserialize<NotificationCapability>("""{"plugin_destinations":{"available":true,"max_per_profile":3},"webhooks":{"available":true,"max_per_profile":10}}""", Wire)!;
        Assert.True(capability.PluginDestinations.Available); Assert.Equal(3, capability.PluginDestinations.MaxPerProfile);
        var channel = Ntfy();
        Assert.Equal("ntfy/push", channel.Key);
        Assert.Equal(6, channel.SettingsForm.Fields.Count);
        Assert.Equal(3, channel.SettingsForm.Fields[1].MinLength); Assert.Equal(65535, channel.SettingsForm.Fields[3].Maximum);
        Assert.Equal(4, channel.SettingsForm.Fields[5].Options![1].Value.GetInt32());
        var destination = Stored();
        Assert.Equal("\"v1\"", destination.ETag); Assert.Equal(["token"], destination.SecretSettingsSet);
        Assert.Equal(8080, destination.Settings["port"].GetInt32()); Assert.True(destination.NotifyNextUp); Assert.False(destination.NotifyRequests);
    }

    [Fact]
    public async Task DestinationListPagesBy100AndRefusesARepeatedCursorOrAMissingPage()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            queries.Add(request.RequestUri!.PathAndQuery);
            return queries.Count switch
            {
                1 => Json(Page(true, Destination("a", "A"))),
                2 => Json(Page(false, Destination("b", "B"))),
                _ => Json("""{"items":[]}"""),
            };
        }));
        var api = new NotificationsApi(Client(http));
        var rows = await api.GetPluginDestinationsAsync(api.RequestContext);
        Assert.Equal(["a", "b"], rows.Select(row => row.Id));
        Assert.Equal(["/api/v2/notifications/plugin-destinations?limit=100", "/api/v2/notifications/plugin-destinations?limit=100&cursor=c%2F1"], queries);
        using var missing = new HttpClient(new Handler(_ => Json("""{"items":[]}""")));
        var missingApi = new NotificationsApi(Client(missing));
        await Assert.ThrowsAsync<InvalidDataException>(() => missingApi.GetPluginDestinationsAsync(missingApi.RequestContext));

        using var repeating = new HttpClient(new Handler(_ => Json("""{"items":[],"page":{"has_more":true,"next_cursor":"same"}}""")));
        var repeat = new NotificationsApi(Client(repeating));
        await Assert.ThrowsAsync<InvalidDataException>(() => repeat.GetPluginDestinationsAsync(repeat.RequestContext));
    }

    [Fact]
    public async Task CreateSendsTheOfficialBodyOnceAndNeverRefreshesOrReplays()
    {
        string? body = null; var calls = 0; var refreshes = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++; body = await request.Content!.ReadAsStringAsync();
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/api/v2/notifications/plugin-destinations", request.RequestUri!.AbsolutePath);
            Assert.Empty(request.Headers.IfMatch);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
        }));
        var client = Client(http); client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        var api = new NotificationsApi(client);
        var form = new NotificationDestinationForm(Ntfy(), null);
        form.SetValue("topic", "family"); form.SetValue("token", "s3cret"); form.SetValue("port", "8081"); form.SetValue("markdown", true); form.SetValue("priority", "2");
        await Assert.ThrowsAsync<ApiException>(() => api.CreatePluginDestinationAsync(api.RequestContext, "ntfy", "push", "Phone", form.SettingsForCreate(), true, false));
        Assert.Equal(1, calls); Assert.Equal(0, refreshes);
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal(["plugin_id", "capability_id", "name", "settings", "notify_next_up", "notify_requests"], root.EnumerateObject().Select(p => p.Name));
        Assert.True(root.GetProperty("notify_next_up").GetBoolean()); Assert.False(root.GetProperty("notify_requests").GetBoolean());
        var settings = root.GetProperty("settings");
        Assert.Equal("https://ntfy.sh", settings.GetProperty("server").GetString());      // an untouched default goes out as shown
        Assert.Equal("family", settings.GetProperty("topic").GetString());
        Assert.Equal(8081, settings.GetProperty("port").GetInt64());                       // typed as the field declares
        Assert.True(settings.GetProperty("markdown").GetBoolean());
        Assert.Equal(2, settings.GetProperty("priority").GetInt64());
        Assert.Equal("s3cret", settings.GetProperty("token").GetString());
    }

    [Fact]
    public async Task UpdateSendsIfMatchAndOnlyWhatChangedAndRefusesAMissingValidatorBeforeSending()
    {
        string? body = null; string? ifMatch = null; var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++; body = await request.Content!.ReadAsStringAsync(); ifMatch = request.Headers.IfMatch.Single().ToString();
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/api/v2/notifications/plugin-destinations/d%2F1", request.RequestUri!.AbsolutePath.Replace("%2f", "%2F"));
            return Json(Destination("d/1", "Phone"));
        }));
        var api = new NotificationsApi(Client(http));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.UpdatePluginDestinationAsync(api.RequestContext, "d1", "", new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.UpdatePluginDestinationAsync(api.RequestContext, "d1", "*", new()));
        Assert.Equal(0, calls);
        await api.UpdatePluginDestinationAsync(api.RequestContext, "d/1", "\"v1\"", new() { ["enabled"] = false });
        Assert.Equal("\"v1\"", ifMatch); Assert.Equal("""{"enabled":false}""", body);
    }

    [Fact]
    public async Task DeleteAndTestAreSentOnceWithoutRefreshOrReplay()
    {
        var calls = new List<string>(); var refreshes = 0; string? ifMatch = null;
        using var http = new HttpClient(new Handler(request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Delete) ifMatch = request.Headers.IfMatch.Single().ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        }));
        var client = Client(http); client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        var api = new NotificationsApi(client);
        await Assert.ThrowsAsync<ApiException>(() => api.DeletePluginDestinationAsync(api.RequestContext, "d1", "\"v1\""));
        await Assert.ThrowsAsync<ApiException>(() => api.TestPluginDestinationAsync(api.RequestContext, "d1"));
        Assert.Equal(["DELETE /api/v2/notifications/plugin-destinations/d1", "POST /api/v2/notifications/plugin-destinations/d1/test"], calls);
        Assert.Equal("\"v1\"", ifMatch); Assert.Equal(0, refreshes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DeletePluginDestinationAsync(api.RequestContext, "d1", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DeletePluginDestinationAsync(api.RequestContext, "d1", "*"));
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task TestMessageReadsOkFalseAs200WithItsMessage()
    {
        using var http = new HttpClient(new Handler(_ => Json("""{"ok":false,"duration_ms":120,"message":"HTTP 401"}""")));
        var api = new NotificationsApi(Client(http));
        var result = await api.TestPluginDestinationAsync(api.RequestContext, "d1");
        Assert.False(result.Ok); Assert.Equal("HTTP 401", result.Message);
        Assert.Equal("Test failed: HTTP 401", NotificationDestinationText.TestText(result));
        Assert.Equal("Test sent", NotificationDestinationText.TestText(new() { Ok = true }));
        Assert.Equal("Test sent: delivered", NotificationDestinationText.TestText(new() { Ok = true, Message = "delivered" }));
    }

    [Fact]
    public async Task PreferencesAndWebhooksCarryOnlyNextUpAndRequests()
    {
        var bodies = new List<string>();
        using var http = new HttpClient(new Handler(async request =>
        {
            bodies.Add(request.RequestUri!.AbsolutePath + " " + await request.Content!.ReadAsStringAsync());
            return Json("""{"profile_id":"p","enabled":true,"notify_next_up":false}""");
        }));
        var api = new NotificationsApi(Client(http));
        await api.UpdatePreferencesAsync(new NotificationPreferences { Enabled = true, NotifyNextUp = false });
        Assert.Equal("""/api/v2/notifications/preferences {"enabled":true,"notify_next_up":false}""", bodies[0]);
        await api.CreateWebhookAsync(new NotificationWebhookInput { Name = "W", Url = "https://example.test/hook", NotifyNextUp = false, NotifyRequests = true });
        using var webhook = JsonDocument.Parse(bodies[1].Substring(bodies[1].IndexOf(' ') + 1));
        Assert.Equal(["name", "url", "notify_next_up", "notify_requests"], webhook.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("All reasons", new NotificationWebhook { NotifyNextUp = true, NotifyRequests = true }.EnabledReasonsText);
        Assert.Equal("Requests and watchlist", new NotificationWebhook { NotifyRequests = true }.EnabledReasonsText);
        Assert.Equal("No reasons selected", new NotificationWebhook().EnabledReasonsText);
    }

    // --- the dialog's rules ---------------------------------------------------------------------

    [Fact]
    public void FieldsRenderByControlAndAStoredSecretIsAWriteOnlyPasswordThatIsNoLongerRequired()
    {
        var channel = Ntfy();
        var add = new NotificationDestinationForm(channel, null).Fields;
        Assert.Equal([DestinationControl.Text, DestinationControl.Text, DestinationControl.Password, DestinationControl.Number, DestinationControl.Switch, DestinationControl.Select], add.Select(view => view.Control));
        Assert.True(add[0].Required); Assert.False(add[2].Required);
        // A setting saved as secret before a plugin upgrade unmarked it stays a password, blank keeps it, and it is not required.
        var edit = new NotificationDestinationForm(channel, Stored("""["topic","priority"]""", """{"server":"https://ntfy.sh","port":8080}""")).Fields;
        Assert.Equal(DestinationControl.Password, edit[1].Control); Assert.False(edit[1].Required);
        Assert.Equal(DestinationControl.Select, edit[5].Control);                      // a secret select keeps its options
        Assert.True(edit[5].Secret);
        var unknown = new NotificationPluginSettingsField { Control = "slider", Type = "boolean", Key = "k" };
        Assert.Equal(DestinationControl.Switch, NotificationDestinationForm.ControlFor(unknown));
        unknown.Type = "number"; Assert.Equal(DestinationControl.Number, NotificationDestinationForm.ControlFor(unknown));
        unknown.Type = "string"; Assert.Equal(DestinationControl.Text, NotificationDestinationForm.ControlFor(unknown));
    }

    [Fact]
    public void ClientChecksFollowTheOfficialMessages()
    {
        var form = new NotificationDestinationForm(Ntfy(), null);
        form.SetValue("server", ""); form.SetValue("topic", "ab"); form.SetValue("port", "70000");
        var errors = form.Validate();
        Assert.Equal("Server is required", errors["server"]);
        Assert.Equal("Topic must be at least 3 characters", errors["topic"]);
        Assert.Equal("Port must be ≤ 65535", errors["port"]);
        form.SetValue("topic", "abc"); form.SetValue("port", "x"); Assert.Equal("Port must be a number", form.Validate()["port"]);
        form.SetValue("port", "0"); Assert.Equal("Port must be ≥ 1", form.Validate()["port"]);
        form.SetValue("server", "https://ntfy.sh"); form.SetValue("port", "");
        Assert.Empty(form.Validate());
    }

    [Fact]
    public void AnUpdateSendsOnlyEditedSettingsNeverABlankStoredSecretAndClearsOnRequest()
    {
        var destination = Stored();
        var form = new NotificationDestinationForm(Ntfy(), destination);
        Assert.Null(form.SettingsForUpdate());                                          // a rename alone sends no settings member
        Assert.False(form.UpdateBody(" Renamed ").ContainsKey("settings"));
        Assert.Equal("Renamed", form.UpdateBody(" Renamed ")["name"]);

        form.SetValue("topic", "new-topic"); form.SetValue("token", "");                // a stored secret left blank stays as saved
        form.SetValue("port", ""); form.SetValue("markdown", false);                    // an emptied optional field is removed, not sent as ""
        var settings = form.SettingsForUpdate()!;
        Assert.Equal(["topic", "port", "markdown"], settings.Keys);
        Assert.Equal("new-topic", settings["topic"]); Assert.Null(settings["port"]); Assert.Equal(false, settings["markdown"]);

        form.SetValue("token", "fresh");                                                 // typing a value for the stored secret replaces it
        Assert.Equal("fresh", form.SettingsForUpdate()!["token"]);

        form.ToggleClearing("token");                                                    // Clear saved secret: sent in clear_secrets, never in settings
        Assert.DoesNotContain("token", form.SettingsForUpdate()!.Keys);
        Assert.Equal(["token"], (string[])form.UpdateBody("n")["clear_secrets"]!);
        form.ToggleClearing("token"); Assert.False(form.UpdateBody("n").ContainsKey("clear_secrets"));   // Keep saved secret
        form.ToggleClearing("token"); form.SetValue("topic", "again"); Assert.Contains("token", form.Clearing); // another field's edit leaves the clear
        form.SetValue("token", "typed"); Assert.DoesNotContain("token", form.Clearing);                          // typing for the cleared secret replaces it
        Assert.Equal(["token"], form.Clearable.Select(view => view.Key));                                        // only an optional stored secret can be cleared
    }

    [Fact]
    public void ReasonsAndNameGoOutOnTheBodyAndEmptyStringFieldsClearToNull()
    {
        var channel = new NotificationPluginChannel { PluginId = "p", CapabilityId = "c", SettingsForm = new() { Fields = [new() { Key = "note", Label = "Note", Control = "text", Type = "string" }] } };
        var destination = new NotificationPluginDestination { Id = "d", ETag = "\"e\"", PluginId = "p", CapabilityId = "c", NotifyNextUp = false, NotifyRequests = true, Settings = new() { ["note"] = JsonDocument.Parse("\"hello\"").RootElement.Clone() } };
        var form = new NotificationDestinationForm(channel, destination);
        form.SetValue("note", "");
        var body = form.UpdateBody("Name");
        Assert.Equal(["name", "settings", "notify_next_up", "notify_requests"], body.Keys);
        Assert.Null(((Dictionary<string, object?>)body["settings"]!)["note"]);
        Assert.Equal(false, body["notify_next_up"]); Assert.Equal(true, body["notify_requests"]);
    }

    [Fact]
    public void SwitchingTheKindStartsTheSettingsOverAndTakesTheChannelNameOnlyWhileTheNameIsEmpty()
    {
        var channels = JsonSerializer.Deserialize<NotificationPluginChannelCollection>(ChannelsJson, Wire)!.Items!;
        var form = new NotificationDestinationForm(channels[0], null);
        form.SetValue("topic", "x");
        form.ChooseChannel(channels[1], "My phone", out var kept);
        Assert.Equal("My phone", kept); Assert.Empty(form.Values); Assert.Empty(form.Fields);
        form.ChooseChannel(channels[0], "  ", out var named); Assert.Equal("ntfy", named);
        Assert.Equal(0, new NotificationDestinationForm(channels[1], null).SettingsForCreate().Count);
    }

    [Fact]
    public void FailedSavesLandUnderTheNameTheFieldsTheFormOrAToast()
    {
        static ApiException Problem(params ApiFieldError[] errors) => new("invalid", "Invalid request", 422) { FieldErrors = errors };
        Assert.Equal("taken", NotificationDestinationText.Classify(Problem(new ApiFieldError("body.name", "duplicate", "taken")), true).NameError);
        var fields = NotificationDestinationText.Classify(Problem(new ApiFieldError("body.settings.topic", "invalid", "bad topic"), new ApiFieldError("body.settings.topic", "other", "later")), true);
        Assert.Equal("bad topic", fields.FieldErrors!["topic"]); Assert.Null(fields.ToastMessage);
        var refusal = NotificationDestinationText.Classify(Problem(new ApiFieldError("body.settings", "invalid", "Plugin refused the token")), true);
        Assert.Equal("Plugin refused the token", refusal.SettingsRejection);
        var toast = NotificationDestinationText.Classify(Problem(new ApiFieldError("body.settings.topic", "invalid", "bad topic")), false);
        Assert.Equal("bad topic", toast.ToastMessage);                                  // a channel without a form falls back to a toast
        Assert.Equal("The destination couldn't be saved", NotificationDestinationText.Classify(new Exception(""), true).ToastMessage);
    }

    // --- the list's wording -------------------------------------------------------------------------

    [Fact]
    public void RowTextSaysWhatNotifiesWhenItLastSucceededOrFailedAndWhyItStopped()
    {
        var now = DateTimeOffset.Parse("2026-11-23T12:00:00Z");
        string Iso(TimeSpan ago) => (now - ago).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        Assert.Equal("All reasons", NotificationDestinationText.ReasonsText(true, true));
        Assert.Equal("Next Up", NotificationDestinationText.ReasonsText(true, false));
        Assert.Equal("No reasons selected", NotificationDestinationText.ReasonsText(false, false));
        Assert.Equal("3m ago", NotificationDestinationText.RelativeTime(Iso(TimeSpan.FromMinutes(3)), now));
        Assert.Equal("5h ago", NotificationDestinationText.RelativeTime(Iso(TimeSpan.FromHours(5)), now));
        Assert.Equal("9d ago", NotificationDestinationText.RelativeTime(Iso(TimeSpan.FromDays(9)), now));
        Assert.Equal("just now", NotificationDestinationText.RelativeTime(Iso(TimeSpan.FromSeconds(20)), now));
        Assert.Null(NotificationDestinationText.RelativeTime(null, now));

        var healthy = new NotificationPluginDestination { LastSuccessAt = Iso(TimeSpan.FromMinutes(3)) };
        Assert.Equal("Last success: 3m ago", NotificationDestinationText.LastSuccessText(healthy, now)); Assert.Null(NotificationDestinationText.WarningText(healthy, now));
        var failing = new NotificationPluginDestination { LastSuccessAt = Iso(TimeSpan.FromHours(2)), LastFailureAt = Iso(TimeSpan.FromMinutes(5)), LastFailureMessage = "HTTP 500" };
        Assert.Null(NotificationDestinationText.LastSuccessText(failing, now));
        Assert.Equal("Last failure 5m ago: HTTP 500", NotificationDestinationText.WarningText(failing, now));
        failing.LastFailureMessage = null; Assert.Equal("Last failure 5m ago: the plugin couldn't send", NotificationDestinationText.WarningText(failing, now));
        failing.LastFailureAt = null; failing.LastFailureMessage = "late"; Assert.Null(NotificationDestinationText.WarningText(failing, now));
        var recovered = new NotificationPluginDestination { LastSuccessAt = Iso(TimeSpan.FromMinutes(1)), LastFailureAt = Iso(TimeSpan.FromHours(1)) };
        Assert.False(NotificationDestinationText.IsFailing(recovered)); Assert.Null(NotificationDestinationText.WarningText(recovered, now));
        var stopped = new NotificationPluginDestination { DisabledReason = "too many failures", LastFailureAt = Iso(TimeSpan.FromMinutes(5)) };
        Assert.Equal("Disabled: too many failures", NotificationDestinationText.WarningText(stopped, now));
        Assert.Equal("Limit of 1 destination reached", NotificationDestinationText.LimitText(1));
        Assert.Equal("Limit of 10 destinations reached", NotificationDestinationText.LimitText(10));
    }

    // --- inbox ----------------------------------------------------------------------------------------

    [Fact]
    public void InboxKnowsWatchlistArrivalsAndStoppedDestinationNotices()
    {
        var watchlist = JsonSerializer.Deserialize<AppNotification>("""{"id":"w","type":"watchlist.available","series_title":"Severance","reason_flags":{"media_type":"series","tmdb_id":95396},"created_at":"2026-11-20T00:00:00Z"}""", Wire)!;
        Assert.Equal("Severance", watchlist.DisplayTitle); Assert.Equal("A series on your watchlist is now available", watchlist.Subtitle);
        watchlist.ReasonFlags.MediaType = "movie"; watchlist.SeriesTitle = null;
        Assert.Equal("Watchlist title available", watchlist.DisplayTitle); Assert.Equal("A movie on your watchlist is now available", watchlist.Subtitle);
        watchlist.ReasonFlags.MediaType = null; Assert.Equal("A title on your watchlist is now available", watchlist.Subtitle);
        var webhook = new AppNotification { Type = "webhook.auto_disabled" }; var destination = new AppNotification { Type = "plugin_destination.auto_disabled", SeriesId = "s" };
        Assert.Equal("A webhook stopped working", webhook.DisplayTitle); Assert.Equal("A notification destination stopped working", destination.DisplayTitle);
        Assert.Equal("Open notification settings to fix it.", webhook.Subtitle);
        Assert.True(NotificationNavigation.IsDestinationDisabledNotice(webhook)); Assert.True(NotificationNavigation.IsDestinationDisabledNotice(destination));
        Assert.False(NotificationNavigation.IsDestinationDisabledNotice(watchlist));
        // Neither is an episode, so neither groups with a series.
        var sections = NotificationInboxGroups.Group([webhook, destination]);
        Assert.Equal(2, sections.Single().Entries.Count);
    }

    [Fact]
    public void InboxScreensWordTheNewTypesAndFollowTheStoppedNoticeToSettings()
    {
        var shell = Source("src", "SiloPlayer", "Views", "NotificationsPage.ShellLocalization.cs");
        foreach (var key in new[] { "web.notifications.inbox.watchlistAvailable", "web.notifications.inbox.watchlistMovieAvailable", "web.notifications.inbox.watchlistSeriesAvailable",
            "web.notifications.inbox.watchlistTitleAvailable", "web.notifications.inbox.webhookStopped", "web.notifications.inbox.destinationStopped", "web.notifications.inbox.fixInSettings" })
            Assert.Contains(key, shell);
        var inbox = Source("src", "SiloPlayer", "Views", "NotificationsPage.Inbox.cs");
        Assert.Contains("IsDestinationDisabledNotice(notification)", inbox); Assert.Contains("Navigate<SettingsPage>(\"NotificationsSettings\")", inbox);
        Assert.Contains("web.notifications.inbox.emptyHintNextUp", Source("src", "SiloPlayer", "Views", "NotificationsPage.xaml"));
        var settings = Source("src", "SiloPlayer", "Controls", "NotificationSettingsControl.xaml");
        Assert.Contains("web.settings.notifications.episodes.nextUpDescription", settings); Assert.Contains("web.settings.notifications.destinations.title", settings);
        foreach (var removed in new[] { "notifications.preferences.favorites", "notifications.preferences.watchlist", "notifications.preferences.continueWatching" })
        {
            Assert.DoesNotContain(removed, settings); Assert.DoesNotContain(removed, Source("src", "SiloPlayer", "Views", "NotificationsPage.xaml"));
            Assert.DoesNotContain(removed, Source("src", "SiloPlayer", "Controls", "NotificationSeriesControl.xaml"));
        }
    }

    // --- state: ownership and the no-double-send rule ----------------------------------------------------

    private static NotificationCapability Capability(int max = 10) => JsonSerializer.Deserialize<NotificationCapability>("{\"plugin_destinations\":{\"available\":true,\"max_per_profile\":" + max + "}}", Wire)!;

    private static async Task<(NotificationPluginDestinationsState State, SiloApiClient Client, List<string> Calls, NotificationsApi Api)> Loaded(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> writes, int rows = 1, int max = 10)
    {
        var calls = new List<string>();
        var http = new HttpClient(new Handler(async request =>
        {
            var line = request.Method + " " + request.RequestUri!.AbsolutePath;
            calls.Add(line);
            if (request.Method == HttpMethod.Get && line.EndsWith("plugin-channels")) return Json(ChannelsJson);
            if (request.Method == HttpMethod.Get)
                return Json(Page(false, Enumerable.Range(1, rows).Select(index => Destination("d" + index, "Phone " + index)).ToArray()));
            return await writes(request, line);
        }));
        var client = Client(http); var api = new NotificationsApi(client);
        var state = new NotificationPluginDestinationsState(api);
        await state.LoadAsync(Capability(max), api.RequestContext);
        calls.Clear();
        return (state, client, calls, api);
    }

    [Fact]
    public async Task LoadReadsChannelsAndRowsAndKnowsTheLimit()
    {
        var (state, _, _, _) = await Loaded((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)), rows: 2, max: 2);
        Assert.True(state.Available); Assert.True(state.IsVisible); Assert.Equal(2, state.Channels.Count); Assert.Equal(2, state.Destinations.Count);
        Assert.True(state.AtLimit); Assert.False(state.CanAdd); Assert.Equal(2, state.MaxPerProfile);
        using var empty = new HttpClient(new Handler(_ => Json("""{"items":[],"page":{"has_more":false}}""")));
        var emptyApi = new NotificationsApi(Client(empty)); var hidden = new NotificationPluginDestinationsState(emptyApi);
        await hidden.LoadAsync(new NotificationCapability(), emptyApi.RequestContext);
        Assert.False(hidden.IsVisible);                                                  // no plugin kind and no leftover rows: the section is not shown
    }

    [Fact]
    public async Task ARemovedPluginsLeftoverRowsStayVisibleAndCannotBeTestedOrEdited()
    {
        using var http = new HttpClient(new Handler(_ => Json(Page(false, Destination("gone", "Old", available: false, settings: "{}")))));
        var api = new NotificationsApi(Client(http)); var state = new NotificationPluginDestinationsState(api);
        await state.LoadAsync(new NotificationCapability(), api.RequestContext);
        Assert.True(state.IsVisible); Assert.False(state.Available); Assert.Empty(state.Channels); Assert.False(state.Destinations.Single().Available);
        Assert.False(state.CanAdd);
    }

    [Fact]
    public async Task ASecondCreateWhileOneIsInFlightIsRefusedAndNothingIsSentTwice()
    {
        var release = new TaskCompletionSource(); var entered = new TaskCompletionSource();
        var (state, _, calls, _) = await Loaded(async (request, line) =>
        {
            entered.TrySetResult(); await release.Task;
            return Json(Destination("new", "Created"), HttpStatusCode.Created);
        });
        var form = new NotificationDestinationForm(state.Channels[0], null); form.SetValue("topic", "family");
        var first = state.CreateAsync(form, "Created"); await entered.Task;
        var second = await Assert.ThrowsAsync<InvalidOperationException>(() => state.CreateAsync(form, "Created"));
        Assert.Equal("Destination creation is already in progress.", second.Message);
        Assert.True(state.IsWriting);
        release.SetResult(); var created = await first;
        Assert.Equal("new", created.Id);
        Assert.Single(calls, call => call.StartsWith("POST"));
        Assert.False(state.IsWriting);
    }

    [Fact]
    public async Task ASecondTestOrDeleteWhileAWriteIsInFlightIsRefused()
    {
        var release = new TaskCompletionSource(); var entered = new TaskCompletionSource();
        var (state, _, calls, _) = await Loaded(async (request, line) =>
        {
            entered.TrySetResult(); await release.Task;
            return line.EndsWith("/test") ? Json("""{"ok":true,"duration_ms":4}""") : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var test = state.TestAsync("d1"); await entered.Task;
        Assert.Equal("A test message is already being sent.", (await Assert.ThrowsAsync<InvalidOperationException>(() => state.TestAsync("d1"))).Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.DeleteAsync("d1", "\"v1\""));
        release.SetResult(); Assert.True((await test).Ok);
        Assert.Equal(["POST /api/v2/notifications/plugin-destinations/d1/test"], calls.Where(call => !call.StartsWith("GET")));
        Assert.True(state.TestResults["d1"].Ok);
    }

    [Fact]
    public async Task DeleteUsesTheValidatorOfTheRowThePersonChoseAndReloadsTheList()
    {
        string? ifMatch = null;
        var (state, _, calls, _) = await Loaded((request, _) => { ifMatch = request.Headers.IfMatch.Single().ToString(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); });
        await state.DeleteAsync("d1", "\"chosen\"");
        Assert.Equal("\"chosen\"", ifMatch);
        Assert.Equal(["DELETE /api/v2/notifications/plugin-destinations/d1", "GET /api/v2/notifications/plugin-destinations"], calls);
    }

    [Fact]
    public async Task ARepliesAndWritesAfterAProfileSwitchAreDroppedWithoutSending()
    {
        var release = new TaskCompletionSource(); var entered = new TaskCompletionSource();
        var (state, client, calls, _) = await Loaded(async (request, line) =>
        {
            entered.TrySetResult(); await release.Task;
            return Json(Destination("new", "Created"), HttpStatusCode.Created);
        });
        var form = new NotificationDestinationForm(state.Channels[0], null); form.SetValue("topic", "family");
        var create = state.CreateAsync(form, "Created"); await entered.Task;
        client.SetProfile("replacement"); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => create);
        Assert.Equal("d1", state.Destinations.Single().Id);                              // the old identity's reply changed nothing
        calls.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.TestAsync("d1"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.DeleteAsync("d1", "\"v1\""));
        Assert.Empty(calls);                                                              // nothing is sent under the new identity from the old page
    }

    [Fact]
    public async Task ALoadThatFinishesAfterAProfileSwitchNeverReplacesTheNewIdentitysState()
    {
        var release = new TaskCompletionSource(); var entered = new TaskCompletionSource();
        using var http = new HttpClient(new Handler(async request =>
        {
            entered.TrySetResult(); await release.Task;
            return Json(Page(false, Destination("old", "Old identity")));
        }));
        var client = Client(http); var api = new NotificationsApi(client); var state = new NotificationPluginDestinationsState(api);
        var load = state.LoadAsync(new NotificationCapability(), api.RequestContext); await entered.Task;
        client.SetProfile("replacement"); release.SetResult(); await load;
        Assert.Empty(state.Destinations); Assert.False(state.LoadFailed);
    }

    [Fact]
    public async Task AWriteRefusedForAStaleOrMissingRowReloadsTheListAndIsNotSentAgain()
    {
        var status = HttpStatusCode.PreconditionFailed;
        var (state, _, calls, _) = await Loaded((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") }));
        var row = state.Destinations.Single();
        Assert.Equal(412, (await Assert.ThrowsAnyAsync<ApiException>(() => state.SetEnabledAsync(row, false))).StatusCode);
        Assert.Equal(["PUT /api/v2/notifications/plugin-destinations/d1", "GET /api/v2/notifications/plugin-destinations"], calls);
        calls.Clear(); status = HttpStatusCode.NotFound;
        await Assert.ThrowsAnyAsync<ApiException>(() => state.DeleteAsync("d1", row.ETag));
        Assert.Equal(["DELETE /api/v2/notifications/plugin-destinations/d1", "GET /api/v2/notifications/plugin-destinations"], calls);
        calls.Clear(); status = HttpStatusCode.UnprocessableEntity;                       // a refusal of what was sent leaves the rows as they are
        await Assert.ThrowsAnyAsync<ApiException>(() => state.SetEnabledAsync(row, false));
        Assert.Equal(["PUT /api/v2/notifications/plugin-destinations/d1"], calls);
        Assert.False(state.IsWriting);
    }

    [Fact]
    public async Task ATimeoutUnderTheSameIdentityIsAVisibleFailureThatReloadsTheListAndIsNotSentAgain()
    {
        var (state, _, calls, _) = await Loaded((_, _) => throw new TaskCanceledException("The request timed out."));
        var form = new NotificationDestinationForm(state.Channels[0], null); form.SetValue("topic", "family");
        var timeout = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.CreateAsync(form, "Created"));
        Assert.True(state.OwnsIdentity);                                                  // not an identity change: the caller must show it
        Assert.Equal(["POST /api/v2/notifications/plugin-destinations", "GET /api/v2/notifications/plugin-destinations"], calls);
        calls.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.DeleteAsync("d1", "\"v1\""));
        Assert.Equal(["DELETE /api/v2/notifications/plugin-destinations/d1", "GET /api/v2/notifications/plugin-destinations"], calls);
        Assert.False(state.IsWriting);
        Assert.Equal(NotificationDestinationText.OutcomeUnknown(), NotificationDestinationText.ErrorMessage(timeout, "Failed to update"));
        Assert.Equal(NotificationDestinationText.OutcomeUnknown(), NotificationDestinationText.Classify(timeout, true).ToastMessage);
    }

    [Fact]
    public void TheDiagnosticsLogNeverCarriesADestinationsSettings()
    {
        // A transport failure of a destination request is recorded as a fixed category (SiloApiClient.SendHttpAttemptAsync).
        var source = Source("src", "SiloPlayer.Core", "Api", "SiloApiClient.cs");
        Assert.Contains("error=notification_destination_transport_failed", source);
    }

    // --- helpers --------------------------------------------------------------------------------

    private static SiloApiClient Client(HttpClient http)
    {
        var client = new SiloApiClient(http); client.SetBaseUrl("https://destinations.invalid"); client.SetProfile("original"); return client;
    }
    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : this(request => Task.FromResult(send(request))) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
