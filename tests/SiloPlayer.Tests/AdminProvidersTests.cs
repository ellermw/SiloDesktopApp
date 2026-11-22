using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official ProvidersSettings contract at Silo Server 217ff25.</summary>
public sealed class AdminProvidersTests
{
    [Fact]
    public async Task SubtitleCredentialWriteUsesCanonicalETagOnceAndNeverEchoesSecret()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        string? ifMatch = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                ifMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                return Task.FromResult(AdminMonitorKit.Json("""{"saved_revision":"5","local_apply":"applied","local_applied_revision":"5"}"""));
            }
            var response = AdminMonitorKit.Json("""{"provider_name":"subdl","enabled":false,"has_api_key":false,"has_credentials":false}""");
            response.Headers.ETag = new("\"subtitle-v4\"");
            return Task.FromResult(response);
        };
        using var lease = authority.Capture();

        var editor = await api.GetSubtitleProviderAsync(lease, "subdl");
        var saved = await api.SaveSubtitleProviderAsync(lease, editor,
            new AdminSubtitleProviderChange { Enabled = true, ApiKey = "synthetic-secret" });

        Assert.Equal("\"subtitle-v4\"", ifMatch);
        Assert.Equal("5", saved.SavedRevision);
        Assert.Equal(AdminSubtitleLocalApply.Applied, saved.LocalApply);
        Assert.DoesNotContain("synthetic-secret", editor.ToString(), StringComparison.Ordinal);
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SubtitleConflictRetainsDraftAndCannotReplayTheWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
                return Task.FromResult(AdminMonitorKit.Json("""{"type":"precondition_failed","status":412}""", HttpStatusCode.PreconditionFailed));
            var response = AdminMonitorKit.Json("""{"provider_name":"subsource","enabled":true,"has_api_key":true,"has_credentials":false}""");
            response.Headers.ETag = new("\"subtitle-v8\"");
            return Task.FromResult(response);
        };
        using var lease = authority.Capture();
        var editor = await api.GetSubtitleProviderAsync(lease, "subsource");
        var draft = new AdminSubtitleProviderDraft(editor.Provider, editor.Enabled, editor.HasApiKey, editor.HasCredentials);
        draft.SetApiKey("retained-secret");

        await Assert.ThrowsAsync<AdminProviderConflictException>(() =>
            api.SaveSubtitleProviderAsync(lease, editor, draft.CaptureChange()));

        Assert.Equal("retained-secret", draft.ApiKey);
        Assert.True(draft.SaveAttempted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            api.SaveSubtitleProviderAsync(lease, editor, draft.CaptureChange()));
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SubtitleTestRedactsEveryTypedCredentialFromProviderText()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(
            """{"success":false,"error":"account synthetic-user rejected synthetic-password"}"""));
        using var lease = authority.Capture();

        var result = await api.TestSubtitleProviderAsync(lease, "opensubtitles", new AdminSubtitleProviderChange
        {
            Enabled = true, Username = "synthetic-user", Password = "synthetic-password",
        });

        Assert.False(result.Success);
        Assert.DoesNotContain("synthetic-user", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-password", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubtitleReloadDiscardsTheDraftAndFollowsTheServerAsTheOfficialTileDoes()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        var enabled = true;
        kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json(JsonSerializer.Serialize(new
            {
                provider_name = "subdl", enabled, has_api_key = true, has_credentials = false,
            }));
            response.Headers.ETag = new(enabled ? "\"subtitle-v1\"" : "\"subtitle-v2\"");
            return Task.FromResult(response);
        };
        using var lease = authority.Capture();
        var initial = await api.GetSubtitleProviderAsync(lease, "subdl");
        var draft = new AdminSubtitleProviderDraft(initial.Provider, initial.Enabled, initial.HasApiKey, initial.HasCredentials);
        draft.SetApiKey("retained-secret");
        _ = draft.CaptureChange();

        enabled = false;
        var reloaded = await api.GetSubtitleProviderAsync(lease, "subdl");
        draft.ResetToSavedConfiguration(reloaded);

        Assert.Equal("", draft.ApiKey);
        Assert.False(draft.Dirty);
        Assert.False(draft.Enabled);
        Assert.False(draft.SaveAttempted);
        Assert.Null(draft.StagedChange().ApiKey);
    }

    [Fact]
    public async Task PluginInventoryIsBoundedPagedAndClassifiesWatchAndMetadataProviders()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.Query.Contains("cursor=next", StringComparison.Ordinal)
            ? AdminMonitorKit.Json("""{"items":[{"id":"9","plugin_id":"silo.tmdb","enabled":true,"capabilities":[{"id":"metadata","type":"metadata_provider.v1","display_name":"TMDB","config_schema":[]}],"global_config_schema":[],"global_configs":[]}],"page":{"has_more":false}}""")
            : AdminMonitorKit.Json("""{"items":[{"id":"7","plugin_id":"silo.anilist","enabled":true,"capabilities":[{"id":"watch","type":"watch_sync_provider.v1","display_name":"AniList","config_schema":[]}],"global_config_schema":[{"key":"account","title":"Account","description":"","json_schema":"{}","required":true}],"global_configs":[]}],"page":{"has_more":true,"next_cursor":"next"}}"""));
        using var lease = authority.Capture();

        var installations = await api.ListPluginInstallationsAsync(lease);
        var tiles = AdminProviderPresentation.PluginTiles(installations);

        Assert.Equal(2, installations.Count);
        Assert.False(Assert.Single(tiles.Where(tile => tile.Kind == AdminPluginProviderKind.Watch)).ConfigurationReady);
        Assert.True(Assert.Single(tiles.Where(tile => tile.Kind == AdminPluginProviderKind.Metadata)).ConfigurationReady);
        Assert.Equal(2, kit.Http.Requests.Count(request => request.StartsWith("GET /api/v2/admin/plugins/installations", StringComparison.Ordinal)));
    }

    [Fact]
    public void MarkerDraftSendsChangedFieldsOnlyAndConvertsPercentToWireRange()
    {
        var provider = new AdminMarkerProvider
        {
            Provider = "plugin:6:introdb", IsSubmitter = true, FetchEnabled = true, FetchPriority = 10,
            FetchMinConfidence = .4, ContributeEnabled = true, ContributeAutoLocal = true,
            ContributeMinConfidence = .9500000000000001,
        };
        var draft = new AdminMarkerProviderDraft(provider);
        Assert.Empty(draft.CapturePatch());

        draft.FetchPriority = "5";
        draft.FetchMinimumPercent = "30";
        var patch = draft.CapturePatch();

        Assert.Equal(2, patch.Count);
        Assert.Equal(5L, patch["fetch_priority"]);
        Assert.Equal(.3, (double)patch["fetch_min_confidence"]!, 8);
        Assert.DoesNotContain("contribute_enabled", patch.Keys);
        Assert.DoesNotContain("contribute_min_confidence", patch.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("1e999")]
    public void MarkerDraftRejectsInvalidConfidenceWithoutLosingIt(string value)
    {
        var draft = new AdminMarkerProviderDraft(new AdminMarkerProvider { Provider = "p", IsSubmitter = true });
        draft.ContributeMinimumPercent = value;
        Assert.False(draft.IsValid);
        Assert.Equal(value, draft.ContributeMinimumPercent);
        Assert.Throws<InvalidOperationException>(() => draft.CapturePatch());
    }

    [Fact]
    public void MarkerDraftRebasesAReconciledServerValueWithoutLosingEditedFields()
    {
        var draft = new AdminMarkerProviderDraft(new AdminMarkerProvider
        {
            Provider = "p", IsSubmitter = true, FetchEnabled = true, FetchPriority = 10,
            FetchMinConfidence = .4, ContributeEnabled = true, ContributeAutoLocal = true, ContributeMinConfidence = .95,
        });
        draft.FetchPriority = "5";
        var rebased = draft.Rebase(new AdminMarkerProvider
        {
            Provider = "p", IsSubmitter = true, FetchEnabled = false, FetchPriority = 20,
            FetchMinConfidence = .6, ContributeEnabled = false, ContributeAutoLocal = false, ContributeMinConfidence = .8,
        });

        var patch = rebased.CapturePatch();
        Assert.Single(patch);
        Assert.Equal(5L, patch["fetch_priority"]);
    }

    [Fact]
    public async Task MDBListCheckAcceptsOnlyItsDeclaredSecretAndRedactsAnEcho()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new
        {
            success = false,
            message = "key synthetic-key was refused " + new string('x', 1100),
        })));
        using var lease = authority.Capture();

        var result = await api.CheckConnectionAsync(lease, "mdblist",
            new Dictionary<string, string> { ["mdblist.api_key"] = "synthetic-key" }, ["mdblist.api_key"]);

        Assert.False(result.Success);
        Assert.Contains(kit.Http.Requests, request => request.StartsWith(
            "POST /api/v2/admin/settings/check/mdblist", StringComparison.Ordinal));
        Assert.DoesNotContain("synthetic-key", result.Message, StringComparison.Ordinal);
        Assert.Equal(501, result.Message.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => api.CheckConnectionAsync(lease, "mdblist",
            new Dictionary<string, string> { ["redis.url"] = "redis://wrong" }, ["redis.url"]));
    }

    [Theory]
    [InlineData("api_key")]
    [InlineData("username")]
    [InlineData("password")]
    public async Task SubtitleTestRedactsCredentialsBeforeApplyingTheDisplayLimit(string credentialField)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        const string credential = "synthetic-credential-crossover";
        var change = new AdminSubtitleProviderChange { Enabled = true };
        switch (credentialField)
        {
            case "api_key": change.ApiKey = credential; break;
            case "username": change.Username = credential; break;
            case "password": change.Password = credential; break;
        }
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new
        {
            success = false,
            error = new string('x', 995) + credential + new string('z', 100),
        })));
        using var lease = authority.Capture();

        var result = await api.TestSubtitleProviderAsync(lease, "opensubtitles", change);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain(credential[..5], result.Error, StringComparison.Ordinal);
        Assert.True(result.Error.Length <= 1003);
    }

    [Fact]
    public void ProviderModelsNeverRenderCredentialMaterial()
    {
        var change = new AdminSubtitleProviderChange { Enabled = true, ApiKey = "never-print", Username = "person", Password = "never-print-password" };
        Assert.Equal(nameof(AdminSubtitleProviderChange), change.ToString());
        Assert.DoesNotContain("never-print", JsonSerializer.Serialize(new { text = change.ToString() }));
    }

    [Theory]
    [InlineData("subdl")]
    [InlineData("opensubtitles")]
    public async Task SubtitleSaveAndTestOmitCredentialsThatWereLeftBlank(string provider)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        var bodies = new List<string>();
        kit.Http.Respond = async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var response = AdminMonitorKit.Json(JsonSerializer.Serialize(new
                {
                    provider_name = provider, enabled = true, has_api_key = true, has_credentials = true,
                }));
                response.Headers.ETag = new("\"subtitle-v4\"");
                return response;
            }
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return request.Method == HttpMethod.Put
                ? AdminMonitorKit.Json("""{"saved_revision":"5","local_apply":"applied","local_applied_revision":"5"}""")
                : AdminMonitorKit.Json("""{"success":true}""");
        };
        using var lease = authority.Capture();
        var editor = await api.GetSubtitleProviderAsync(lease, provider);
        var draft = new AdminSubtitleProviderDraft(editor.Provider, editor.Enabled, editor.HasApiKey, editor.HasCredentials);
        // Only the enabled state is changed; whitespace in a key or username box is not a typed credential.
        draft.SetEnabled(false);
        draft.SetApiKey("  ");
        draft.SetUsername(" ");

        await api.TestSubtitleProviderAsync(lease, provider, draft.StagedChange());
        await api.SaveSubtitleProviderAsync(lease, editor, draft.CaptureChange());

        Assert.Equal(2, bodies.Count);
        foreach (var body in bodies)
        {
            using var sent = JsonDocument.Parse(body);
            Assert.False(sent.RootElement.GetProperty("enabled").GetBoolean());
            Assert.False(sent.RootElement.TryGetProperty("api_key", out _));
            Assert.False(sent.RootElement.TryGetProperty("username", out _));
            Assert.False(sent.RootElement.TryGetProperty("password", out _));
            Assert.False(sent.RootElement.TryGetProperty("clear_credentials", out _));
        }
    }

    [Fact]
    public void SubtitleStagedChangeCarriesOnlyTheCredentialsThatWereTyped()
    {
        var account = new AdminSubtitleProviderDraft("opensubtitles", enabled: true, hasApiKey: false, hasCredentials: true);
        account.SetPassword("synthetic-password");
        var passwordOnly = account.StagedChange();
        Assert.Null(passwordOnly.Username);
        Assert.Equal("synthetic-password", passwordOnly.Password);
        Assert.Null(passwordOnly.ApiKey);

        account.SetUsername("synthetic-user");
        var both = account.CaptureChange();
        Assert.Equal("synthetic-user", both.Username);
        Assert.Equal("synthetic-password", both.Password);

        var key = new AdminSubtitleProviderDraft("subdl", enabled: true, hasApiKey: true, hasCredentials: false);
        Assert.Null(key.StagedChange().ApiKey);
        key.SetApiKey("synthetic-key");
        Assert.Equal("synthetic-key", key.StagedChange().ApiKey);
        Assert.False(key.SaveAttempted);

        var cleared = key.CaptureChange(clear: true);
        Assert.True(cleared.ClearCredentials);
        Assert.Null(cleared.ApiKey);
    }

    [Theory]
    [InlineData(.55, "55")]
    [InlineData(.57, "57")]
    [InlineData(.07, "7")]
    [InlineData(.29, "29")]
    [InlineData(.125, "12.5")]
    [InlineData(.9500000000000001, "95")]
    [InlineData(0d, "0")]
    [InlineData(1d, "100")]
    public void MarkerConfidenceIsShownWithoutFloatingPointNoiseAndUnchangedTextKeepsTheSavedValue(double saved, string shown)
    {
        var draft = new AdminMarkerProviderDraft(new AdminMarkerProvider
        {
            Provider = "p", IsSubmitter = true, FetchPriority = 10, FetchMinConfidence = saved, ContributeMinConfidence = saved,
        });

        Assert.Equal(shown, draft.FetchMinimumPercent);
        Assert.Equal(shown, draft.ContributeMinimumPercent);
        Assert.True(draft.IsValid);
        Assert.False(draft.AdvancedDirty);
        Assert.False(draft.Dirty);
        Assert.Empty(draft.CapturePatch());

        // Another edit never drags the untouched confidence along as a drifted value.
        draft.FetchPriority = "5";
        Assert.Equal(["fetch_priority"], draft.CapturePatch().Keys);
    }

    [Fact]
    public void MarkerConfidenceTypedAsAPercentageIsShownAsTypedAfterTheServerAcknowledgesIt()
    {
        var draft = new AdminMarkerProviderDraft(MarkerProvider());
        draft.FetchMinimumPercent = "55";
        var patch = draft.BeginSave();
        var sent = (double)Assert.Single(patch).Value!;
        Assert.Equal(.55, sent);

        var acknowledged = MarkerProvider();
        acknowledged.FetchMinConfidence = sent;
        var next = draft.Rebase(acknowledged);

        Assert.Equal("55", next.FetchMinimumPercent);
        Assert.False(next.Dirty);
    }

    [Fact]
    public void ProviderTestResultsRemainScopedToTheTileThatRanThem()
    {
        var subtitle = new AdminSubtitleProviderTestResult { Success = true };
        var marker = new AdminMarkerProviderValidation { Valid = true };
        var state = new AdminProvidersState
        {
            SubtitleTestProvider = "subdl", SubtitleTest = subtitle,
            MarkerTestProvider = "plugin:6:introdb", MarkerTest = marker,
        };

        Assert.Same(subtitle, state.SubtitleTestFor("subdl"));
        Assert.Null(state.SubtitleTestFor("subsource"));
        Assert.Same(marker, state.MarkerTestFor("plugin:6:introdb"));
        Assert.Null(state.MarkerTestFor("plugin:7:other"));
    }

    public static TheoryData<string, AdminSubtitleSaveFailure> SubtitleFailureKinds => new()
    {
        { "400", AdminSubtitleSaveFailure.Refused },
        { "409", AdminSubtitleSaveFailure.Refused },
        { "412", AdminSubtitleSaveFailure.Conflict },
        { "500", AdminSubtitleSaveFailure.OutcomeUnknown },
        { "503", AdminSubtitleSaveFailure.OutcomeUnknown },
        { "408", AdminSubtitleSaveFailure.OutcomeUnknown },
        { "lost", AdminSubtitleSaveFailure.OutcomeUnknown },
        { "timeout", AdminSubtitleSaveFailure.OutcomeUnknown },
        { "unusable-acknowledgement", AdminSubtitleSaveFailure.OutcomeUnknown },
    };

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> SubtitleFailure(string kind) => _ => kind switch
    {
        "lost" => throw new HttpRequestException("synthetic lost answer"),
        "timeout" => throw new TaskCanceledException("synthetic timeout"),
        "unusable-acknowledgement" => Task.FromResult(AdminMonitorKit.Json("""{"saved_revision":"0","local_apply":"applied"}""")),
        _ => Task.FromResult(AdminMonitorKit.Json("""{"type":"synthetic"}""", (HttpStatusCode)int.Parse(kind))),
    };

    /// <summary>Owner decision: the official tile. After ANY failed save the tile is "save not confirmed" and its own Reload alone recovers it.</summary>
    [Theory]
    [MemberData(nameof(SubtitleFailureKinds))]
    public async Task AFailedSubtitleSaveLeavesTheTileNotConfirmedAndItsOwnReloadAloneReArmsIt(string answer, AdminSubtitleSaveFailure kind)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var other = await h.Controller.GetSubtitleEditorAsync("subsource");
        var editor = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.NotNull(other); Assert.NotNull(editor);
        var draft = new AdminSubtitleProviderDraft("subdl", editor!.Enabled, editor.HasApiKey, editor.HasCredentials);
        draft.SetApiKey("synthetic-secret");

        // A second tile is already not confirmed; nothing below may touch it.
        h.Write = SubtitleFailure("503");
        Assert.False(await h.Controller.SaveSubtitleAsync(other!, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.Equal(AdminSubtitleSaveFailure.OutcomeUnknown, h.Controller.State.SubtitleSaveFailures["subsource"]);

        h.Write = SubtitleFailure(answer);
        Assert.False(await h.Controller.SaveSubtitleAsync(editor, draft.CaptureChange()));
        var state = h.Controller.State;
        Assert.Equal(kind, state.SubtitleSaveFailures["subdl"]);
        Assert.True(state.SubtitleSaveNotConfirmed("subdl"));
        Assert.True(draft.SaveAttempted);
        Assert.Equal("synthetic-secret", draft.ApiKey); // retained and shown disabled until Reload, as official
        Assert.Equal(2, h.Writes.Length);

        // Save is disabled: a second attempt for that provider sends nothing at all.
        h.Write = _ => throw new InvalidOperationException("must not be sent");
        Assert.False(await h.Controller.SaveSubtitleAsync(editor, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.Equal(2, h.Writes.Length);

        // The tile's Reload: exactly one read of that provider, no write, no list read.
        var before = h.Kit.Http.Requests.ToArray();
        var reloaded = await h.Controller.GetSubtitleEditorAsync("subdl");
        var added = h.Kit.Http.Requests.Skip(before.Length).ToArray();
        Assert.Single(added);
        Assert.EndsWith("/api/v2/admin/subtitle-providers/subdl", added[0].Split(' ')[1], StringComparison.Ordinal);
        Assert.StartsWith("GET", added[0], StringComparison.Ordinal);
        Assert.NotNull(reloaded);
        draft.ResetToSavedConfiguration(reloaded!);

        Assert.Equal("", draft.ApiKey);
        Assert.False(draft.Dirty);
        Assert.False(draft.SaveAttempted);
        Assert.False(h.Controller.State.SubtitleSaveNotConfirmed("subdl"));
        // The other tile is untouched by reading this provider.
        Assert.Equal(AdminSubtitleSaveFailure.OutcomeUnknown, h.Controller.State.SubtitleSaveFailures["subsource"]);

        // Save is re-armed with the fresh editor, sends once, and the first failure was never replayed.
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"saved_revision":"7","local_apply":"applied","local_applied_revision":"7"}"""));
        draft.SetApiKey("synthetic-second-secret");
        Assert.True(await h.Controller.SaveSubtitleAsync(reloaded!, draft.CaptureChange()));
        Assert.Equal(3, h.Writes.Length);
    }

    [Fact]
    public async Task ThePageListReadNeitherSettlesATileNorKeepsAProviderThatNoLongerExists()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = SubtitleFailure("503");
        var gone = await h.Controller.GetSubtitleEditorAsync("subsource"); // not in the harness list
        var listed = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.False(await h.Controller.SaveSubtitleAsync(gone!, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.False(await h.Controller.SaveSubtitleAsync(listed!, new AdminSubtitleProviderChange { Enabled = true }));

        await h.Controller.ReloadAsync(preserveDraft: true);

        Assert.True(h.Controller.State.SubtitleSaveNotConfirmed("subdl"));
        Assert.False(h.Controller.State.SubtitleSaveNotConfirmed("subsource"));
        Assert.Equal(2, h.Writes.Length);
    }

    [Fact]
    public void AnUnconfirmedTileDoesNotBlockTheOthersAndTheStateNamesNoCredential()
    {
        var state = new AdminProvidersState
        {
            SubtitleSaveFailures = new Dictionary<string, AdminSubtitleSaveFailure> { ["subdl"] = AdminSubtitleSaveFailure.Refused },
        };
        Assert.True(state.SubtitleSaveNotConfirmed("subdl"));
        Assert.False(state.SubtitleSaveNotConfirmed("subsource"));
        Assert.False(state.ReloadRequired); // a definite refusal needs no page-level alert
        Assert.Equal(nameof(AdminProvidersState), state.ToString());
    }

    [Theory]
    [InlineData("OpenSubtitles", "OpenSubtitles")]
    [InlineData("line one\nline two\u2028three\u2029four", "line one line two three four")]
    [InlineData("a\u202Eevil\u202Cb\u2066c\u2069d\u200Fe\u061Cf", "aevilbcdef")]
    [InlineData("  tab\there\u0000 now  ", "tab here now")]
    [InlineData("\u0645\u200D\u0646", "\u0645\u200D\u0646")] // a joiner that joins characters stays
    [InlineData("", "")]
    public void DialogTextNeutralizesControlLineAndDirectionCharactersAndKeepsJoiners(string input, string expected) =>
        Assert.Equal(expected, AdminProviderPresentation.DialogText(input));

    [Fact]
    public void DialogTextIsBoundedAndNeverCutsASurrogatePair()
    {
        var text = AdminProviderPresentation.DialogText(new string('x', 63) + "\U0001F600" + "tail", 64);
        Assert.Equal(new string('x', 63) + "…", text);
        Assert.DoesNotContain(text, character => char.IsSurrogate(character));
        Assert.Equal(65, AdminProviderPresentation.DialogText(new string('y', 500)).Length);
    }

    private const string SavedOk = """{"saved_revision":"7","local_apply":"applied","local_applied_revision":"7"}""";
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> Ok(string json = SavedOk) => _ => Task.FromResult(AdminMonitorKit.Json(json));

    /// <summary>Finding 1 (controller half): one provider's failed save never blocks another provider's Save or Disconnect.</summary>
    [Theory]
    [InlineData("412")]
    [InlineData("503")]
    [InlineData("lost")]
    public async Task AnotherProvidersFailedSaveNeverBlocksThisProvidersSaveOrDisconnect(string answer)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var failing = await h.Controller.GetSubtitleEditorAsync("subsource");
        h.Write = SubtitleFailure(answer);
        Assert.False(await h.Controller.SaveSubtitleAsync(failing!, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.True(h.Controller.State.SubtitleSaveNotConfirmed("subsource"));

        h.Write = Ok();
        var save = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.True(await h.Controller.SaveSubtitleAsync(save!, new AdminSubtitleProviderChange { Enabled = true, ApiKey = "synthetic-key" }));
        Assert.Equal(2, h.Writes.Length);
        Assert.EndsWith("/subtitle-providers/subdl", h.Writes[1].Split(' ')[1], StringComparison.Ordinal);

        var disconnect = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.True(await h.Controller.SaveSubtitleAsync(disconnect!, new AdminSubtitleProviderChange { Enabled = false, ClearCredentials = true }));
        Assert.Equal(3, h.Writes.Length);
        Assert.True(h.Controller.State.SubtitleSaveNotConfirmed("subsource"));
        Assert.False(h.Controller.State.SubtitleSaveNotConfirmed("subdl"));
    }

    /// <summary>
    /// Finding 1 (page half): the page reads only its own provider's state. The page-wide flags are derived from every provider, so a
    /// Save or Disconnect click that tested them did nothing, and said nothing, on every other tile.
    /// </summary>
    [Fact]
    public void ThePageNeverReadsThePageWideSubtitleFlagsAndEveryRefusedClickSaysWhy()
    {
        var views = Path.Combine(Root(), "src/SiloPlayer/Views/Admin");
        foreach (var file in new[] { "AdminProvidersPage.Cards.cs", "AdminProvidersPage.cs" })
        {
            var text = File.ReadAllText(Path.Combine(views, file));
            Assert.DoesNotContain("SubtitleConflict", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SubtitleOutcomeUnknown", text, StringComparison.Ordinal);
        }
        // A control drawn enabled acts or says why: every early return in the subtitle Save handler carries a message.
        var cards = File.ReadAllText(Path.Combine(views, "AdminProvidersPage.Cards.cs"));
        var start = cards.IndexOf("private async Task SaveSubtitleAsync(", StringComparison.Ordinal);
        var end = cards.IndexOf("var change = draft.CaptureChange", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        foreach (var line in cards[start..end].Split('\n').Where(line => line.Contains("return;", StringComparison.Ordinal)))
            Assert.True(line.Contains("NotifyProvider(", StringComparison.Ordinal) || line.Contains("controller == null", StringComparison.Ordinal),
                "A refused Save click must say why: " + line.Trim());
    }

    [Fact]
    public void TheMarkerTileReloadReadsTheMarkerScopeOnlyAndNeverTheSubtitleTiles()
    {
        var cards = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Views/Admin/AdminProvidersPage.Cards.cs"));
        var start = cards.IndexOf("\"ProvidersMarkerReload_\"", StringComparison.Ordinal);
        var end = cards.IndexOf("actions.Children.Add(reload);", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        Assert.DoesNotContain("ReloadPageAsync", cards[start..end], StringComparison.Ordinal);
        Assert.Contains("_controller.ReloadAsync(preserveDraft: true)", cards[start..end], StringComparison.Ordinal);
    }

    /// <summary>Finding 2: a read that fails leaves its tile "could not be loaded"; only a successful read of that provider ends it.</summary>
    [Theory]
    [InlineData("503")]
    [InlineData("lost")]
    public async Task AFailedSubtitleReadMarksOnlyThatTileNotLoadedAndALaterReadNeverClearsIt(string answer)
    {
        using var h = new Harness { ListSubsource = true };
        await h.Controller.OpenAsync();
        var failing = await h.Controller.GetSubtitleEditorAsync("subsource");
        h.Write = SubtitleFailure("503");
        Assert.False(await h.Controller.SaveSubtitleAsync(failing!, new AdminSubtitleProviderChange { Enabled = true }));

        h.ProviderRead = request => request.RequestUri!.AbsolutePath.EndsWith("/subsource", StringComparison.Ordinal)
            ? SubtitleFailure(answer)(request) : null;
        var writes = h.Writes.Length;
        Assert.Null(await h.Controller.GetSubtitleEditorAsync("subsource"));
        var state = h.Controller.State;
        Assert.True(state.SubtitleLoadFailed("subsource"));
        Assert.True(state.SubtitleSaveNotConfirmed("subsource")); // a failed read does not settle a tile
        Assert.Null(state.ReadingProvider); Assert.Null(state.BusyProvider);
        Assert.StartsWith("Could not load provider configuration", state.ProviderFeedback, StringComparison.Ordinal);

        // Another tile's read starts by clearing the page line; it never clears this tile's own failure.
        Assert.NotNull(await h.Controller.GetSubtitleEditorAsync("subdl"));
        Assert.True(h.Controller.State.SubtitleLoadFailed("subsource"));
        Assert.False(h.Controller.State.SubtitleLoadFailed("subdl"));
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(h.Controller.State.SubtitleLoadFailed("subsource")); // nor does the page's list read

        // Reload again fails again; then it succeeds and re-arms the tile.
        Assert.Null(await h.Controller.GetSubtitleEditorAsync("subsource"));
        Assert.True(h.Controller.State.SubtitleLoadFailed("subsource"));
        h.ProviderRead = null;
        var recovered = await h.Controller.GetSubtitleEditorAsync("subsource");
        Assert.NotNull(recovered);
        Assert.False(h.Controller.State.SubtitleLoadFailed("subsource"));
        Assert.False(h.Controller.State.SubtitleSaveNotConfirmed("subsource"));
        Assert.Equal(writes, h.Writes.Length); // reads wrote nothing
        h.Write = Ok();
        Assert.True(await h.Controller.SaveSubtitleAsync(recovered!, new AdminSubtitleProviderChange { Enabled = true }));
    }

    /// <summary>Finding 6: reading a provider is not saving it.</summary>
    [Fact]
    public async Task ReadingAProviderIsNeverReportedAsSavingIt()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.ProviderRead = _ => gate.Task;
        var read = h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.Equal("subdl", h.Controller.State.BusyProvider);
        Assert.Equal("subdl", h.Controller.State.ReadingProvider);
        h.ProviderRead = null;
        gate.SetResult(Etag(AdminMonitorKit.Json("""{"provider_name":"subdl","enabled":true,"has_api_key":true,"has_credentials":false}""")));
        var editor = await read;
        Assert.NotNull(editor);
        Assert.Null(h.Controller.State.BusyProvider); Assert.Null(h.Controller.State.ReadingProvider);

        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        var save = h.Controller.SaveSubtitleAsync(editor!, new AdminSubtitleProviderChange { Enabled = false });
        Assert.Equal("subdl", h.Controller.State.BusyProvider);
        Assert.Null(h.Controller.State.ReadingProvider); // a write in flight is saving
        pending.SetResult(AdminMonitorKit.Json(SavedOk));
        Assert.True(await save);
    }

    private static HttpResponseMessage Etag(HttpResponseMessage response) { response.Headers.ETag = new("\"one\""); return response; }

    /// <summary>Finding 7: a test result is cleared by a successful save only; a failed attempt leaves it standing.</summary>
    [Fact]
    public async Task ASubtitleTestResultSurvivesAFailedSaveAndIsClearedOnlyByASuccessfulOne()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = Ok("""{"success":true}""");
        await h.Controller.TestSubtitleAsync("subdl", new AdminSubtitleProviderChange { Enabled = true });
        Assert.NotNull(h.Controller.State.SubtitleTestFor("subdl"));

        var editor = await h.Controller.GetSubtitleEditorAsync("subdl");
        h.Write = SubtitleFailure("503");
        Assert.False(await h.Controller.SaveSubtitleAsync(editor!, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.NotNull(h.Controller.State.SubtitleTestFor("subdl")); // official: cleared on success only

        var again = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.NotNull(h.Controller.State.SubtitleTestFor("subdl")); // and a read does not touch it
        h.Write = Ok();
        Assert.True(await h.Controller.SaveSubtitleAsync(again!, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.Null(h.Controller.State.SubtitleTestFor("subdl"));
    }

    /// <summary>Finding 8: a successful save leaves the tile on the official success line until that provider is read again.</summary>
    [Theory]
    [InlineData("""{"saved_revision":"7","local_apply":"applied","local_applied_revision":"7"}""", "Settings saved and applied on this server. Other servers may still use older settings.")]
    [InlineData("""{"saved_revision":"7","local_apply":"applied","local_applied_revision":"6"}""", "Settings saved. This server applied a different saved revision; reload to review the current configuration.")]
    [InlineData("""{"saved_revision":"7","local_apply":"not_configured"}""", "Settings saved, but not applied on this server. Other servers may still use older settings.")]
    [InlineData("""{"saved_revision":"7","local_apply":"failed"}""", "Settings saved, but not applied on this server. Other servers may still use older settings.")]
    public async Task ASuccessfulSubtitleSaveKeepsTheOfficialMessageUntilTheProviderIsReadAgain(string acknowledgement, string message)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = Ok(acknowledgement);
        var editor = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.True(await h.Controller.SaveSubtitleAsync(editor!, new AdminSubtitleProviderChange { Enabled = true }));
        Assert.Equal(message, h.Controller.State.SubtitleSaveMessage("subdl"));
        Assert.Null(h.Controller.State.SubtitleSaveMessage("subsource"));
        Assert.True(string.IsNullOrEmpty(h.Controller.State.ProviderFeedback));

        // The page's list read leaves it; only reading that provider ends it.
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.Equal(message, h.Controller.State.SubtitleSaveMessage("subdl"));
        Assert.NotNull(await h.Controller.GetSubtitleEditorAsync("subdl"));
        Assert.Null(h.Controller.State.SubtitleSaveMessage("subdl"));
    }

    [Fact]
    public async Task ASavedDraftIsClearedKeepsTheSavedSwitchAndStaysOffUntilReloaded()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var editor = (await h.Controller.GetSubtitleEditorAsync("subdl"))!;
        var draft = new AdminSubtitleProviderDraft(editor.Provider, editor.Enabled, editor.HasApiKey, editor.HasCredentials);
        draft.SetApiKey("synthetic-secret"); draft.SetEnabled(false);
        var change = draft.CaptureChange();

        draft.ResetAfterSave(change);

        Assert.Equal("", draft.ApiKey);
        Assert.False(draft.Dirty);
        Assert.False(draft.Enabled);
        Assert.True(draft.SaveAttempted); // the spent ETag: nothing more is sent until Reload
        Assert.Throws<InvalidOperationException>(() => draft.CaptureChange());
        draft.ResetToSavedConfiguration(editor);
        Assert.False(draft.SaveAttempted);
    }

    /// <summary>Finding 9a: an authority change retires every per-provider state with the controller's own state.</summary>
    [Fact]
    public async Task AnAuthorityChangeRetiresEveryPerProviderState()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var failing = await h.Controller.GetSubtitleEditorAsync("subsource");
        h.Write = SubtitleFailure("412");
        Assert.False(await h.Controller.SaveSubtitleAsync(failing!, new AdminSubtitleProviderChange { Enabled = true }));
        var saved = await h.Controller.GetSubtitleEditorAsync("subdl");
        h.Write = Ok();
        Assert.True(await h.Controller.SaveSubtitleAsync(saved!, new AdminSubtitleProviderChange { Enabled = true }));
        h.ProviderRead = request => request.RequestUri!.AbsolutePath.EndsWith("/subsource", StringComparison.Ordinal) ? SubtitleFailure("503")(request) : null;
        Assert.Null(await h.Controller.GetSubtitleEditorAsync("subsource"));
        var before = h.Controller.State;
        Assert.True(before.SubtitleSaveNotConfirmed("subsource") && before.SubtitleLoadFailed("subsource") && before.SubtitleSaveMessage("subdl") != null && before.ReloadRequired);

        h.Authority.Retire();

        var retired = h.Controller.State;
        Assert.Null(retired.Owner);
        Assert.Empty(retired.SubtitleSaveFailures); Assert.Empty(retired.SubtitleLoadFailures); Assert.Empty(retired.SubtitleSaveMessages);
        Assert.False(retired.ReloadRequired);
        h.ProviderRead = null;
        await h.Controller.OpenAsync();
        var reopened = h.Controller.State;
        Assert.NotNull(reopened.Owner);
        Assert.Empty(reopened.SubtitleSaveFailures); Assert.Empty(reopened.SubtitleLoadFailures); Assert.Empty(reopened.SubtitleSaveMessages);
        Assert.False(reopened.SubtitleSaveNotConfirmed("subsource"));
    }

    /// <summary>Finding 9b: a read that finishes across an authority change hands back no editor.</summary>
    [Fact]
    public async Task AReadThatFinishesAfterTheAuthorityChangedReturnsNoEditorAndSetsNothing()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.ProviderRead = _ => gate.Task;
        var read = h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.Equal("subdl", h.Controller.State.ReadingProvider);

        h.Authority.Retire();
        gate.SetResult(Etag(AdminMonitorKit.Json("""{"provider_name":"subdl","enabled":true,"has_api_key":true,"has_credentials":false}""")));

        Assert.Null(await read);
        Assert.Null(h.Controller.State.Owner);
        Assert.Null(h.Controller.State.ReadingProvider); Assert.Null(h.Controller.State.BusyProvider);
        Assert.Empty(h.Controller.State.SubtitleSaveFailures); Assert.Empty(h.Controller.State.SubtitleLoadFailures);
    }

    /// <summary>Finding 9b (second line): should a stale editor reach Save anyway, nothing is sent and the tile says "not confirmed".</summary>
    [Fact]
    public async Task AnEditorFromAnOlderAuthorityLeavesTheTileNotConfirmedAndSendsNothing()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var stale = await h.Controller.GetSubtitleEditorAsync("subdl");
        h.Authority.Retire();
        await h.Controller.OpenAsync();
        h.Write = Ok();

        Assert.False(await h.Controller.SaveSubtitleAsync(stale!, new AdminSubtitleProviderChange { Enabled = true }));

        Assert.Empty(h.Writes);
        Assert.True(h.Controller.State.SubtitleSaveNotConfirmed("subdl"));
        Assert.Null(h.Controller.State.BusyProvider);
        // Reading that provider again under the current authority recovers it.
        Assert.NotNull(await h.Controller.GetSubtitleEditorAsync("subdl"));
        Assert.False(h.Controller.State.SubtitleSaveNotConfirmed("subdl"));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminProvidersController Controller;
        internal string MarkerProvidersJson = MarkerJson(priority: 10);
        internal string MarkerListStatus = "ok";
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        /// <summary>Answers a GET of one subtitle provider; null from it means the default answer.</summary>
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>?>? ProviderRead;
        /// <summary>Lists "subsource" beside "subdl" in the page's provider list.</summary>
        internal bool ListSubsource;
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            var settings = new AdminServerSettingsApi(Kit.Client);
            var store = new AdminServerSettingsStore(settings, Authority, AdminProvidersController.Keys, AdminProvidersController.Secrets);
            Controller = new AdminProvidersController(new AdminProvidersApi(Kit.Client), settings, store, Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Write?.Invoke(request) ?? Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/subtitle-providers/") && ProviderRead?.Invoke(request) is { } custom) return custom;
                HttpResponseMessage response =
                    path.EndsWith("/subtitle-providers") ? AdminMonitorKit.Json(ListSubsource
                        ? """{"providers":[{"provider_name":"subdl","enabled":true,"has_api_key":true,"has_credentials":false},{"provider_name":"subsource","enabled":true,"has_api_key":true,"has_credentials":false}]}"""
                        : """{"providers":[{"provider_name":"subdl","enabled":true,"has_api_key":true,"has_credentials":false}]}""")
                    : path.Contains("/subtitle-providers/") ? AdminMonitorKit.Json(JsonSerializer.Serialize(new { provider_name = path[(path.LastIndexOf('/') + 1)..], enabled = true, has_api_key = true, has_credentials = false }))
                    : path.EndsWith("/markers/providers") ? (MarkerListStatus == "ok" ? AdminMonitorKit.Json(MarkerProvidersJson) : AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable))
                    : path.EndsWith("/plugins/installations") ? AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}""")
                    : path.EndsWith("effective") ? AdminMonitorKit.Json("""{"markers.mode":"both"}""")
                    : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json("""{"configured":["mdblist.api_key"],"managed_by_env":[]}""")
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new("\"one\"");
                return Task.FromResult(response);
            };
        }
        internal string[] Writes => Kit.Http.Requests.Where(r => !r.StartsWith("GET")).ToArray();
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static string MarkerJson(int priority) =>
        $$"""{"providers":[{"provider":"plugin:6:introdb","display_name":"IntroDB","source_type":"plugin","plugin_id":"silo.introdb","plugin_installation_id":"6","is_submitter":true,"fetch_enabled":true,"fetch_priority":{{priority}},"fetch_min_confidence":0.4,"contribute_enabled":true,"contribute_auto_local":true,"contribute_min_confidence":0.95,"default_contribute_enabled":false}]}""";

    private static AdminMarkerProvider MarkerProvider(long priority = 10) => new()
    {
        Provider = "plugin:6:introdb", IsSubmitter = true, FetchEnabled = true, FetchPriority = priority,
        FetchMinConfidence = .4, ContributeEnabled = true, ContributeAutoLocal = true, ContributeMinConfidence = .95,
    };

    [Fact]
    public void MarkerDraftForANonSubmitterWithStoredSharingOpensCleanAndNeverWritesContributeFields()
    {
        // The official tile compares against the raw saved values; the submitter gate is display-only.
        var saved = new AdminMarkerProvider
        {
            Provider = "plugin:6:introdb", IsSubmitter = false, FetchEnabled = true, FetchPriority = 10,
            FetchMinConfidence = .4, ContributeEnabled = true, ContributeAutoLocal = true, ContributeMinConfidence = .95,
        };
        var draft = new AdminMarkerProviderDraft(saved);
        Assert.False(draft.Dirty);
        Assert.Empty(draft.CapturePatch());

        draft.FetchEnabled = false;

        Assert.Equal(["fetch_enabled"], draft.CapturePatch().Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MarkerDraftWithSharingOffAndStoredAutomaticSharingOpensCleanAndNeverWritesContributeFields(bool submitter)
    {
        // The server can hold contribute_auto_local=true under contribute_enabled=false; that is not an edit.
        var saved = new AdminMarkerProvider
        {
            Provider = "plugin:6:introdb", IsSubmitter = submitter, FetchEnabled = true, FetchPriority = 10,
            FetchMinConfidence = .4, ContributeEnabled = false, ContributeAutoLocal = true, ContributeMinConfidence = .95,
        };
        var draft = new AdminMarkerProviderDraft(saved);
        Assert.False(draft.Dirty);
        Assert.Empty(draft.CapturePatch());

        draft.FetchPriority = "5";

        Assert.Equal(["fetch_priority"], draft.CapturePatch().Keys);

        // The page's sharing toggle is what switches automatic sharing off, and only then is it written.
        draft.ContributeAutoLocal = false;

        var patch = draft.CapturePatch();
        Assert.Equal(2, patch.Count);
        Assert.False((bool)patch["contribute_auto_local"]!);
        Assert.DoesNotContain("contribute_enabled", patch.Keys);
    }

    [Fact]
    public async Task ErasedMdblistKeyIsNotADraftSoTestChecksTheSavedKey()
    {
        using var h = new Harness();
        string? body = null;
        h.Write = async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return AdminMonitorKit.Json("""{"success":true,"message":"ok"}""");
        };
        await h.Controller.OpenAsync();
        h.Controller.SetMdblistApiKey("synthetic-typed-key");
        Assert.Contains("mdblist.api_key", h.Controller.State.DirtyKeys);

        h.Controller.SetMdblistApiKey("");
        Assert.DoesNotContain("mdblist.api_key", h.Controller.State.DirtyKeys);
        await h.Controller.TestMdblistAsync();

        Assert.Equal(["POST /api/v2/admin/settings/check/mdblist"], h.Writes);
        using var sent = JsonDocument.Parse(body!);
        Assert.Equal(0, sent.RootElement.GetProperty("dirty_keys").GetArrayLength());
        Assert.DoesNotContain("mdblist.api_key", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedDisconnectLeavesNoStagedClearForALaterTest()
    {
        using var h = new Harness();
        var bodies = new List<string>();
        h.Write = async request =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return request.Method == HttpMethod.Put
                ? AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError)
                : AdminMonitorKit.Json("""{"success":true,"message":"ok"}""");
        };
        await h.Controller.OpenAsync();

        await h.Controller.ClearMdblistAsync(h.Controller.State.Owner!);

        Assert.Contains("mdblist.api_key", h.Controller.State.Configured);
        Assert.DoesNotContain("mdblist.api_key", h.Controller.State.DirtyKeys);
        await h.Controller.TestMdblistAsync();
        using var sent = JsonDocument.Parse(bodies[^1]);
        Assert.Equal(0, sent.RootElement.GetProperty("dirty_keys").GetArrayLength());
    }

    [Fact]
    public async Task DisconnectConfirmedForAReplacedAuthorityNeverWritesTheNewAuthority()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var confirmedOwner = h.Controller.State.Owner;
        Assert.NotNull(confirmedOwner);
        Assert.Contains("mdblist.api_key", h.Controller.State.Configured);

        // The server or account is replaced while the confirmation is open, and the page reopens for the new one.
        h.Authority.Retire();
        await h.Controller.OpenAsync();
        Assert.NotSame(confirmedOwner, h.Controller.State.Owner);

        await h.Controller.ClearMdblistAsync(confirmedOwner!);

        Assert.Empty(h.Writes);
        Assert.Contains("mdblist.api_key", h.Controller.State.Configured);
    }

    [Fact]
    public async Task DisconnectConfirmedForTheCurrentAuthoritySendsTheOneClearWrite()
    {
        using var h = new Harness();
        h.Write = request =>
        {
            var response = AdminMonitorKit.Json("""{"values":{"mdblist.api_key":""},"restart_required":false}""");
            response.Headers.ETag = new("\"two\"");
            return Task.FromResult(response);
        };
        await h.Controller.OpenAsync();

        await h.Controller.ClearMdblistAsync(h.Controller.State.Owner!);

        Assert.Equal(["PUT /api/v2/admin/settings"], h.Writes);
        Assert.DoesNotContain("mdblist.api_key", h.Controller.State.Configured);
    }

    [Fact]
    public async Task SubtitleEditorFromAReplacedAuthorityIsRefusedBeforeAnyWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put) return Task.FromResult(AdminMonitorKit.Json("""{"saved_revision":"5","local_apply":"applied","local_applied_revision":"5"}"""));
            var response = AdminMonitorKit.Json("""{"provider_name":"subdl","enabled":true,"has_api_key":true,"has_credentials":false}""");
            response.Headers.ETag = new("\"subtitle-v4\"");
            return Task.FromResult(response);
        };
        using var first = authority.Capture();
        var editor = await api.GetSubtitleProviderAsync(first, "subdl");
        authority.Retire();
        using var second = authority.Capture();

        await Assert.ThrowsAsync<AdminProviderAuthorityChangedException>(() => api.SaveSubtitleProviderAsync(second, editor,
            new AdminSubtitleProviderChange { Enabled = false, ClearCredentials = true }));

        Assert.Empty(kit.Http.Requests.Where(request => request.StartsWith("PUT", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MarkerUnknownOutcomeKeepsReloadReachableAndReloadReconcilesIt()
    {
        using var h = new Harness();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
        await h.Controller.OpenAsync();
        Assert.False(h.Controller.State.ReloadRequired);

        Assert.False(await h.Controller.SaveMarkerAsync("plugin:6:introdb", new Dictionary<string, object?> { ["fetch_priority"] = 5L }));

        Assert.True(h.Controller.State.MarkerOutcomeUnknown);
        Assert.True(h.Controller.State.ReloadRequired);
        // Nothing is replayed while the outcome is unknown.
        Assert.False(await h.Controller.SaveMarkerAsync("plugin:6:introdb", new Dictionary<string, object?> { ["fetch_priority"] = 5L }));
        Assert.Single(h.Writes);

        // The marker list cannot be read: the unknown outcome must stay unresolved and Reload must stay reachable.
        h.MarkerListStatus = "fail";
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(h.Controller.State.MarkerOutcomeUnknown && h.Controller.State.ReloadRequired);

        h.MarkerListStatus = "ok"; h.MarkerProvidersJson = MarkerJson(priority: 5);
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.MarkerOutcomeUnknown);
        Assert.False(h.Controller.State.ReloadRequired);
        Assert.Equal(5, Assert.Single(h.Controller.State.MarkerProviders).FetchPriority);
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task SubtitleEditorOpenedWhileAnotherSaveIsPendingIsRefusedAndNeverSettlesItsOutcome()
    {
        using var h = new Harness();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        await h.Controller.OpenAsync();
        var editor = await h.Controller.GetSubtitleEditorAsync("subdl");
        Assert.NotNull(editor);
        Assert.Null(h.Controller.State.BusyProvider);

        var save = h.Controller.SaveSubtitleAsync(editor!, new AdminSubtitleProviderChange { Enabled = false });
        Assert.Equal("subdl", h.Controller.State.BusyProvider);

        // Another tile is opened while the write is in flight: the read is refused and the write stays the busy owner.
        Assert.Null(await h.Controller.GetSubtitleEditorAsync("subsource"));
        Assert.Equal("subdl", h.Controller.State.BusyProvider);
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.DoesNotContain(h.Kit.Http.Requests, request => request.Contains("/subtitle-providers/subsource", StringComparison.Ordinal));
        Assert.Single(h.Kit.Http.Requests.Where(request => request.EndsWith("/subtitle-providers", StringComparison.Ordinal)));

        pending.SetResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
        Assert.False(await save);
        Assert.Null(h.Controller.State.BusyProvider);
        Assert.True(h.Controller.State.SubtitleOutcomeUnknown);

        // Reading another provider's editor says nothing about the unknown write.
        Assert.NotNull(await h.Controller.GetSubtitleEditorAsync("subsource"));
        Assert.True(h.Controller.State.SubtitleOutcomeUnknown && h.Controller.State.ReloadRequired);

        // The official tile is settled by reading that provider again (its own Reload), not by the page's list read.
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(h.Controller.State.SubtitleSaveNotConfirmed("subdl") && h.Controller.State.ReloadRequired);
        Assert.NotNull(await h.Controller.GetSubtitleEditorAsync("subdl"));
        Assert.False(h.Controller.State.SubtitleOutcomeUnknown);
        Assert.False(h.Controller.State.ReloadRequired);
        Assert.Single(h.Writes);
    }

    [Fact]
    public void MarkerEditsTypedWhileASaveIsPendingSurviveTheAcknowledgement()
    {
        var draft = new AdminMarkerProviderDraft(MarkerProvider(priority: 10));
        draft.FetchPriority = "5";
        var sent = draft.BeginSave();
        Assert.Equal(5L, sent["fetch_priority"]);
        Assert.True(draft.SaveInFlight);
        Assert.Throws<InvalidOperationException>(() => draft.BeginSave());

        draft.FetchPriority = "10";          // back to the old saved value while the request is pending
        draft.FetchMinimumPercent = "30";    // a new field edited while pending
        var next = draft.Rebase(MarkerProvider(priority: 5));

        Assert.False(next.SaveInFlight);
        Assert.Equal("10", next.FetchPriority);
        Assert.Equal("30", next.FetchMinimumPercent);
        var patch = next.CapturePatch();
        Assert.Equal(10L, patch["fetch_priority"]);
        Assert.Equal(.3, (double)patch["fetch_min_confidence"]!, 8);
    }

    [Fact]
    public void MarkerFieldsUneditedAfterTheSendTakeTheServerValueAndAFailedSendKeepsTheDraft()
    {
        var draft = new AdminMarkerProviderDraft(MarkerProvider(priority: 10));
        draft.FetchPriority = "5";
        draft.BeginSave();
        var accepted = draft.Rebase(MarkerProvider(priority: 5));
        Assert.Equal("5", accepted.FetchPriority);
        Assert.Empty(accepted.CapturePatch());
        Assert.False(accepted.Dirty);

        var failed = new AdminMarkerProviderDraft(MarkerProvider(priority: 10)) { FetchPriority = "7" };
        failed.BeginSave();
        failed.FetchEnabled = false;
        failed.AbandonSave();
        Assert.False(failed.SaveInFlight);
        Assert.Equal("7", failed.FetchPriority);
        Assert.False(failed.FetchEnabled);
        Assert.Equal(2, failed.CapturePatch().Count);
    }

    [Fact]
    public async Task MarkerAndSubtitleTestTextIsRedactedBeforeItIsTruncated()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminProvidersApi(kit.Client);
        using var lease = authority.Capture();
        const string key = "synthetic-subtitle-key-abcdefghij";
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new
        {
            success = false,
            error = new string('x', 990) + key + new string('z', 50),
        })));

        var subtitle = await api.TestSubtitleProviderAsync(lease, "subdl", new AdminSubtitleProviderChange { Enabled = true, ApiKey = key });

        Assert.DoesNotContain("synthetic", subtitle.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghij", subtitle.Error, StringComparison.Ordinal);
        Assert.Contains("<redacted>", subtitle.Error, StringComparison.Ordinal);

        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new
        {
            valid = false,
            error = new string('x', 980) + " token=synthetic-marker-token-abcdefghij " + new string('z', 50),
        })));
        var marker = await api.ValidateMarkerProviderAsync(lease, "plugin:6:introdb");
        Assert.DoesNotContain("synthetic", marker.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghij", marker.Error, StringComparison.Ordinal);
    }
}
