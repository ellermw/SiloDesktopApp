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
    public async Task SubtitleReloadRebasesCanonicalStateAndRetainsTheCredentialDraft()
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
        draft.RearmAfterReload(reloaded);

        Assert.Equal("retained-secret", draft.ApiKey);
        Assert.False(draft.Enabled);
        Assert.False(draft.SaveAttempted);
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
        Assert.Equal(1003, result.Message.Length);
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
}
