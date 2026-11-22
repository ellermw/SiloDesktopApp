using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Tests.HiddenChangesWire;

namespace SiloPlayer.Tests;

/// <summary>
/// The Jellyfin compatibility settings: Jellyfin Web's switch-off rides with an API switch-off and is taken back by any other flip (official #4696), and the switch
/// for serving a credential-less stream from the same address (official server #4466) is off by default and changes only by the admin's own action. Synthetic values only.
/// </summary>
public sealed class AdminCompatibilitySettingsHiddenTests
{
    // ---------------------------------------------------------------- Compatibility

    private sealed class CompatHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminIntegrationSettingsController Controller;
        internal string Effective, Sensitive;
        internal CompatHarness(string effective = """{"jellyfin_compat.enabled":"true","jellyfin_compat.web_enabled":"true","jellyfin_compat.address_stream_grant_enabled":"false"}""",
            string sensitive = """{"configured":[],"managed_by_env":[]}""")
        {
            Effective = effective; Sensitive = sensitive;
            Authority = new(Kit.Client, Kit.Auth); var api = new AdminServerSettingsApi(Kit.Client);
            Store = new(api, Authority, AdminIntegrationsSettings.AllKeys, AdminIntegrationsSettings.Secrets);
            Controller = new(AdminIntegrationsSettings.CompatibilityKeys, api, Store, Authority, new AdminIntegrationsApi(Kit.Client));
            Kit.Http.Respond = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Patch) { var patched = AdminMonitorKit.Json("{}"); patched.Headers.ETag = new("\"two\""); return Task.FromResult(patched); }
                if (request.Method != HttpMethod.Get) return Task.FromResult(SavedAck());
                var response = AdminMonitorKit.Json(path.EndsWith("effective") ? Effective : path.EndsWith("restart-keys") ? """{"keys":[],"prefixes":[]}""" : Sensitive);
                response.Headers.ETag = new("\"one\""); return Task.FromResult(response);
            };
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string Grant = AdminIntegrationsSettings.AddressStreamGrant;

    [Fact]
    public async Task TheSameAddressStreamSwitchIsOffAndNothingIsStagedOrSentUntilTheAdminActs()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        Assert.Equal("jellyfin_compat.address_stream_grant_enabled", Grant);
        Assert.Contains(Grant, AdminIntegrationsSettings.CompatibilityKeys);
        Assert.DoesNotContain(Grant, AdminIntegrationsSettings.CompatibilityPatchKeys);
        Assert.Equal("false", h.Controller.State.Values[Grant]);
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    [Fact]
    public async Task AServerThatSendsNoValueForTheSwitchKeepsItOff()
    {
        using var h = new CompatHarness("""{"jellyfin_compat.enabled":"true"}"""); await h.Controller.OpenAsync();
        Assert.Equal("", h.Controller.State.Values[Grant]);
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    // A server from before official #4466 applied the same-address rule unconditionally and has no such switch: no row ("Off" would misstate it), and no write of a key it does not know.
    [Theory]
    [InlineData("""{"jellyfin_compat.enabled":"true"}""", false)]
    [InlineData("""{"jellyfin_compat.enabled":"true","jellyfin_compat.address_stream_grant_enabled":"false"}""", true)]
    [InlineData("""{"jellyfin_compat.enabled":"true","jellyfin_compat.address_stream_grant_enabled":"true"}""", true)]
    public async Task TheSameAddressSwitchHasARowOnlyWhenTheServerSentItsValue(string effective, bool shown)
    {
        using var h = new CompatHarness(effective); await h.Controller.OpenAsync();
        Assert.Equal(shown, AdminIntegrationsSettings.AddressStreamGrantShown(h.Controller.State.PersistedValues, h.Controller.State.DirtyKeys));
    }

    [Fact]
    public async Task TheSameAddressSwitchOfAServerThatDoesNotKnowTheKeyCannotBeStagedOrSent()
    {
        using var h = new CompatHarness("""{"jellyfin_compat.enabled":"true"}"""); await h.Controller.OpenAsync();
        h.Controller.Set(Grant, "true");
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values[Grant]);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    [Fact]
    public async Task ASentValueIsShownAndOnlyTheSwitchChangesIt()
    {
        using var h = new CompatHarness("""{"jellyfin_compat.address_stream_grant_enabled":"true"}"""); await h.Controller.OpenAsync();
        Assert.Equal("true", h.Controller.State.Values[Grant]);
        h.Controller.Set(Grant, "false");
        Assert.Equal([Grant], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        using var body = JsonDocument.Parse(SentBody(h.Kit));
        Assert.Equal("false", body.RootElement.GetProperty("values").GetProperty(Grant).GetString());
        Assert.True(AdminIntegrationsSettings.AddressStreamGrantShown(h.Controller.State.PersistedValues, h.Controller.State.DirtyKeys));
    }

    [Fact]
    public async Task TurningTheSameAddressStreamSwitchOnSendsOneSettingsWriteWithJustThatKey()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.Set(Grant, "true");
        Assert.Equal([Grant], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(["PUT /api/v2/admin/settings"], h.Kit.Http.Requests.Where(request => !request.StartsWith("GET", StringComparison.Ordinal)).ToArray());
        using var body = JsonDocument.Parse(SentBody(h.Kit));
        Assert.Equal("true", body.RootElement.GetProperty("values").GetProperty(Grant).GetString());
        Assert.Single(body.RootElement.GetProperty("values").EnumerateObject());
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task TheSameAddressStreamSwitchRidesTheSettingsWriteNotTheCompatibilityPatch()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.Set("jellyfin_compat.server_name", "Synthetic");
        h.Controller.Set(Grant, "true");
        await h.Controller.SaveAsync();
        var patchBody = h.Kit.Http.Bodies.Single(text => text.Contains("server_name", StringComparison.Ordinal));
        Assert.DoesNotContain("address_stream", patchBody, StringComparison.Ordinal);
        Assert.Contains(h.Kit.Http.Requests, request => request == "PATCH /api/v2/admin/jellyfin-compat/settings");
        Assert.Contains(h.Kit.Http.Requests, request => request == "PUT /api/v2/admin/settings");
    }

    [Fact]
    public async Task AManagedSameAddressStreamSwitchIsNeverStagedOrSent()
    {
        using var h = new CompatHarness(sensitive: $$$"""{"configured":[],"managed_by_env":["{{{Grant}}}"],"managed_by_env_vars":{"{{{Grant}}}":"SILO_SYNTHETIC"}}""");
        await h.Controller.OpenAsync();
        h.Controller.Set(Grant, "true");
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    [Fact]
    public async Task SwitchingTheApiOffStagesTheWebPlayerOffAndFlippingBackTakesItBack()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(["jellyfin_compat.enabled", "jellyfin_compat.web_enabled"], h.Controller.State.DirtyKeys.Order());
        h.Controller.SetCompatibilityApi(true);
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("true", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    // Official setJellyfinAPIEnabled("false") always sets the web player off; webOffAlone resets whatever web draft remains when no API switch-off is staged.
    private const string WebOffSaved = """{"jellyfin_compat.enabled":"true","jellyfin_compat.web_enabled":"false","jellyfin_compat.address_stream_grant_enabled":"false"}""";

    [Fact]
    public async Task SwitchingTheApiOffOverAWebPlayerDraftOfTrueStagesTheWebPlayerOffSoTheSaveNeverSendsOnWithOff()
    {
        using var h = new CompatHarness(WebOffSaved); await h.Controller.OpenAsync();
        h.Controller.Set("jellyfin_compat.web_enabled", "true");
        Assert.Equal(["jellyfin_compat.web_enabled"], h.Controller.State.DirtyKeys);
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal("false", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
        await h.Controller.SaveAsync();
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last(text => text.Contains("\"enabled\"", StringComparison.Ordinal)));
        Assert.Equal(JsonValueKind.False, body.RootElement.GetProperty("enabled").ValueKind);
        Assert.DoesNotContain("\"web_enabled\":true", string.Concat(h.Kit.Http.Bodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlippingTheApiBackAfterThatReturnsTheWebPlayerToItsSavedValueAsOfficialDoesAndTheEarlierDraftIsNotRestored()
    {
        using var h = new CompatHarness(WebOffSaved); await h.Controller.OpenAsync();
        h.Controller.Set("jellyfin_compat.web_enabled", "true");
        h.Controller.SetCompatibilityApi(false);
        h.Controller.SetCompatibilityApi(true);
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("false", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
    }

    [Fact]
    public async Task SwitchingTheApiOffOverASavedOnWebPlayerAndAnExplicitTrueDraftOfItsOwnStagesBothOffAndFlipBackTakesBothBack()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.Set("jellyfin_compat.web_enabled", "true");
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(["jellyfin_compat.enabled", "jellyfin_compat.web_enabled"], h.Controller.State.DirtyKeys.Order());
        Assert.Equal("false", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
        h.Controller.SetCompatibilityApi(true);
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ASecondApiSwitchOffKeepsTheWebPlayerOffRidingWithItAndFlipBackStillTakesItBack()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(false);
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(["jellyfin_compat.enabled", "jellyfin_compat.web_enabled"], h.Controller.State.DirtyKeys.Order());
        h.Controller.SetCompatibilityApi(true);
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AWebPlayerDraftOfTrueUnderAnApiThatTurnsOnAndOffAgainIsTakenBackLikeOfficial()
    {
        // Saved: API off, web off. The admin turns the API on, the web player on, then the API off again: no API switch-off is staged, so nothing is left.
        using var h = new CompatHarness("""{"jellyfin_compat.enabled":"false","jellyfin_compat.web_enabled":"false"}"""); await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(true);
        h.Controller.Set("jellyfin_compat.web_enabled", "true");
        h.Controller.SetCompatibilityApi(false);
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    [Fact]
    public async Task AManagedWebPlayerSwitchIsNeverStagedByTheApiSwitchOff()
    {
        using var h = new CompatHarness(sensitive: """{"configured":[],"managed_by_env":["jellyfin_compat.web_enabled"],"managed_by_env_vars":{"jellyfin_compat.web_enabled":"SILO_SYNTHETIC"}}""");
        await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(["jellyfin_compat.enabled"], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task SwitchingOffAnApiThatIsAlreadyOffStagesNothingForTheWebPlayer()
    {
        using var h = new CompatHarness("""{"jellyfin_compat.enabled":"false","jellyfin_compat.web_enabled":"false"}"""); await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(true);
        h.Controller.SetCompatibilityApi(false);
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    [Fact]
    public async Task AWebPlayerSwitchOffTheAdminChoseThemselvesSurvivesTheApiFlipping()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.Set("jellyfin_compat.web_enabled", "false");
        h.Controller.SetCompatibilityApi(false);
        h.Controller.SetCompatibilityApi(true);
        Assert.Equal(["jellyfin_compat.web_enabled"], h.Controller.State.DirtyKeys);
        Assert.Equal("false", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
    }

    [Fact]
    public async Task TheEnvironmentTakingTheApiSwitchOverTakesBackTheWebPlayerSwitchOffThatRodeWithIt()
    {
        using var h = new CompatHarness(); await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(2, h.Controller.State.DirtyKeys.Count);
        h.Sensitive = """{"configured":[],"managed_by_env":["jellyfin_compat.enabled"],"managed_by_env_vars":{"jellyfin_compat.enabled":"SILO_JELLYFIN_ENABLED"}}""";
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }
}
