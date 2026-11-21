using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official 217ff25 account-resource contracts. Synthetic values only; no Users page dependency.</summary>
public sealed class AdminUserResourcesTests
{
    private const string Group = """{"id":"2","name":"Family","is_default":false,"library_ids":null,"allowed_permissions":[],"max_profiles":5}""";
    private static JsonElement J(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static HttpResponseMessage Reply(string body, string? etag = "\"r1\"", HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(body, status);
        if (etag != null) response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    [Fact]
    public async Task GroupUpdateUsesDetailTagAndPreservesNullVersusEmptyPolicies()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (request, _) => { if (request.Method == HttpMethod.Put) Assert.Equal("\"r1\"", request.Headers.IfMatch.Single().ToString()); return Task.FromResult(Reply(Group)); };
        var editor = await api.GetGroupAsync(lease, "2");
        await api.SaveGroupAsync(lease, editor, J("""{"name":"Family","library_ids":null,"allowed_permissions":[],"max_profiles":5}"""));
        Assert.Equal(["GET /api/v2/admin/access-groups/2", "PUT /api/v2/admin/access-groups/2"], kit.Http.Requests);
        var body = J(kit.Http.Bodies.Last());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("library_ids").ValueKind);
        Assert.Equal(0, body.GetProperty("allowed_permissions").GetArrayLength());
    }

    [Theory]
    [InlineData(null)] [InlineData("W/\"r1\"")] [InlineData("*")]
    public async Task MissingOrWeakDetailTagAllowsReadButNeverWrite(string? tag)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(Group, tag));
        var editor = await api.GetGroupAsync(lease, "2");
        Assert.False(editor.CanWrite);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.DeleteGroupAsync(lease, editor));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(409)] [InlineData(412)] [InlineData(428)] [InlineData(422)] [InlineData(429)]
    public async Task GuardFailuresAreDistinctAndNeverReplay(int status)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(Group));
        var editor = await api.GetGroupAsync(lease, "2");
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"title":"Rejected","detail":"synthetic token https://source.invalid/x","status":400}""", null, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeleteGroupAsync(lease, editor));
        Assert.Equal(status, error.StatusCode); Assert.DoesNotContain("source.invalid", error.ToString());
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task LostCreateIsUnknownAndSentOnce()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => throw new HttpRequestException("synthetic lost answer");
        await Assert.ThrowsAsync<AdminResourceOutcomeUnknownException>(() => api.CreateGroupAsync(lease, "Family"));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":true}}")]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":false,\"next_cursor\":\"unexpected\"}}")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[{\"id\":\"2\"},{\"id\":\"2\"}],\"page\":{\"has_more\":false}}")]
    public async Task InvalidCollectionIsNotPublishedAsEmptyOrComplete(string body)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(body));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListGroupsAsync(lease));
    }

    [Theory]
    [InlineData("{\"items\":[{\"id\":\"1\"},{\"id\":\"2\"}],\"page\":{\"has_more\":false}}")]
    [InlineData("{\"items\":[{\"id\":\"1\"},{\"id\":\"2\"}],\"page\":null}")]
    [InlineData("{\"items\":[{\"id\":\"1\"},{\"id\":\"2\"}]}")]
    public async Task UnpagedLibraryListIsCompleteWithOrWithoutItsPageMember(string body)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(body));
        var libraries = await api.ListLibrariesAsync(lease);
        Assert.Equal(["1", "2"], libraries.Items.Select(x => x.Text("id"))); Assert.Null(libraries.NextCursor);
        Assert.Equal("GET /api/v2/libraries", kit.Http.Requests.Single());
    }

    [Theory]
    [InlineData("{\"page\":{\"has_more\":false}}")]
    [InlineData("{\"items\":[{\"id\":\"1\"}],\"page\":{}}")]
    [InlineData("{\"items\":[{\"id\":\"1\"}],\"page\":[]}")]
    [InlineData("{\"items\":[{\"id\":\"1\"},{\"id\":\"1\"}]}")]
    public async Task MalformedLibraryListIsNotPublishedAsComplete(string body)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(body));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListLibrariesAsync(lease));
    }

    [Fact]
    public async Task LibraryListThatSaysMoreFollowsKeepsItsContinuationAndIsNeverComplete()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"items":[{"id":"1"}],"page":{"has_more":true,"next_cursor":"next"}}"""));
        Assert.Equal("next", (await api.ListLibrariesAsync(lease)).NextCursor);
    }

    [Fact]
    public async Task GroupPageIsUserDrivenAndDoesNotWalkTheFleet()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"items":[{"id":"2"}],"page":{"has_more":true,"next_cursor":"next/+"}}"""));
        var page = await api.ListGroupsAsync(lease);
        Assert.Equal("next/+", page.NextCursor); Assert.Single(kit.Http.Requests);
        Assert.Equal("GET /api/v2/admin/access-groups?limit=100", kit.Http.Requests.Single());
    }

    [Fact]
    public async Task DetailIdentityCannotBeSubstituted()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(Group.Replace("\"2\"", "\"3\"")));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetGroupAsync(lease, "2"));
    }

    [Fact]
    public async Task EditorCannotBeReboundToAnotherAuthority()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var old = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply(Group));
        var editor = await api.GetGroupAsync(old, "2");
        kit.Auth.SelectProfile("p2", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p2", IsPrimary = true });
        using var replacement = authority.Capture();
        await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => api.DeleteGroupAsync(replacement, editor));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task CanonicalDeviceWriteBindsUserProfileDeviceKeyAndTypedValue()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"key":"playback.autoplay","scope":"profile_device","profile_id":"p/2","device_id":"desk/+","value":false}"""));
        await api.SetDeviceSettingAsync(lease, "7", "p/2", "desk/+", "playback.autoplay", J("false"));
        Assert.Equal("PUT /api/v2/admin/users/7/settings/values/playback.autoplay?scope=profile_device&profile_id=p%2F2&device_id=desk%2F%2B", kit.Http.Requests.Single());
        Assert.Equal(JsonValueKind.False, J(kit.Http.Bodies.Single()).GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task ADeviceIdThatCannotBeAUrlSegmentDoesNotFailTheFleet()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"items":[{"user_id":"7","device_id":"desk"},{"user_id":"7","device_id":".."},{"user_id":"7","device_id":""},{"user_id":"8","device_id":"desk"}],"page":{"has_more":false}}"""));
        Assert.Equal(4, (await api.ListDevicesAsync(lease)).Items.Count);
        // The same device twice is still refused, and such an id still cannot be put in a URL.
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"items":[{"user_id":"7","device_id":".."},{"user_id":"7","device_id":".."}],"page":{"has_more":false}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListDevicesAsync(lease));
        await Assert.ThrowsAsync<ArgumentException>(() => api.GetDeviceAsync(lease, "7", ".."));
    }

    [Fact]
    public async Task DeviceDetailChecksCompositeIdentityAndIgnoresLegacyOverrides()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"user_id":"8","device_id":"desk","profiles":[],"settings":[]}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetDeviceAsync(lease, "7", "desk"));
    }

    [Fact]
    public async Task ResetProfileStopsAfterUnknownWithoutReplayingEarlierKeys()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => kit.Http.Requests.Count == 1 ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)) : throw new HttpRequestException();
        var result = await api.ResetDeviceProfileAsync(lease, "7", "p1", "desk", ["a", "b", "c"]);
        Assert.Equal(1, result.Completed); Assert.True(result.OutcomeUnknown); Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Theory]
    [InlineData("emby", 24)] [InlineData("plex", 24)] [InlineData("jellyfin", 0)] [InlineData("jellyfin", 8761)]
    public void LookbackOnlyAppliesToSupportedJellyfinWindows(string type, int hours) =>
        Assert.Throws<ArgumentException>(() => AdminUserResourcesApi.RunBody(type, hours, true, 8760));

    [Fact]
    public async Task RunRequiresExactLocalStatusLocationAndNeverClaimsCompletion()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => { var r = Reply("""{"id":"run1","status":"queued","terminal":false,"cancelable":true}""", null, HttpStatusCode.Accepted); r.Headers.Location = new Uri("/api/v2/admin/history-imports/runs/run1", UriKind.Relative); r.Headers.TryAddWithoutValidation("Retry-After", "7"); return Task.FromResult(r); };
        var result = await api.StartImportAsync(lease, "9", "jellyfin", 72, true, 8760);
        Assert.False(result.Value.Flag("terminal")); Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
        Assert.Equal(72, J(kit.Http.Bodies.Single()).GetProperty("lookback_hours").GetInt32());
    }

    [Theory]
    [InlineData(null)] [InlineData("https://attacker.invalid/run1")] [InlineData("/api/v2/admin/history-imports/runs/other")]
    public async Task InvalidRunReceiptIsUnknownNotAReplay(string? location)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client);
        kit.Http.Respond = (_, _) => { var r = Reply("""{"id":"run1","status":"queued","terminal":false,"cancelable":true}""", null, HttpStatusCode.Accepted); if (location != null) r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute); return Task.FromResult(r); };
        await Assert.ThrowsAsync<AdminResourceOutcomeUnknownException>(() => api.StartImportAsync(lease, "9", "emby", null, false, 0));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task SourceAddressChangeRequiresReplacementOrExplicitClearCredential()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminUserResourcesApi(kit.Client); kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"id":"2","name":"Fixture","source_type":"emby","base_url":"https://source.invalid","has_admin_token":true}"""));
        var source = await api.GetImportSourceAsync(lease, "2");
        await Assert.ThrowsAsync<ArgumentException>(() => api.SaveImportSourceAsync(lease, source, J("""{"name":"Fixture","base_url":"https://other.invalid","enabled":true}""")));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task RetiredResponseDoesNotPublish()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        var pending = new AdminUserResourcesApi(kit.Client).GetGroupAsync(lease, "2");
        kit.Client.SetBaseUrl("https://replacement.invalid"); gate.SetResult(Reply(Group));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
