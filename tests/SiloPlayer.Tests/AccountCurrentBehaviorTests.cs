using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class AccountCurrentBehaviorTests
{
    [Theory]
    [InlineData(null, "Too many incorrect PINs. Try again later.")]
    [InlineData(0d, "Too many incorrect PINs. Try again in 1 minute.")]
    [InlineData(61d, "Too many incorrect PINs. Try again in 2 minutes.")]
    public void PinLockoutUsesServerRetryAfter(double? seconds, string expected)
        => Assert.Equal(expected, ProfilePinFeedback.Lockout(new ApiException("rate_limited", "private server detail", 429, retryAfterSeconds: seconds)));

    [Fact]
    public void OtherPinFailuresDoNotInventLockout()
        => Assert.Null(ProfilePinFeedback.Lockout(new ApiException("bad_request", "failure", 400)));

    [Fact]
    public void DeviceDefaultPrefersCurrentOwnProfileAndSelectionIncludesProfile()
    {
        UserDevice[] devices = [new() { ProfileId = "other", DeviceId = "current", IsCurrentDevice = true },
            new() { ProfileId = "own", DeviceId = "current", IsCurrentDevice = true }, new() { ProfileId = "own", DeviceId = "tv" }];
        Assert.Same(devices[1], DeviceSelection.Default(devices, "own"));
        Assert.NotEqual(DeviceSelection.Key(devices[0]), DeviceSelection.Key(devices[1]));
        Assert.Same(devices[2], DeviceSelection.Select(devices, DeviceSelection.Key(devices[2]), "own"));
    }

    [Fact]
    public void NonPrimaryAdminCannotImportIntoAnotherProfile()
        => Assert.Equal("own", HistoryImportScope.Target(new() { Role = "admin" }, new() { Id = "own", IsPrimary = false }, "other"));

    [Fact]
    public void DeviceInheritanceAndConstraintCopyPreserveStoredChoice()
    {
        var limited = new ContractEffectiveSettingEntry { Source = "profile_device", Value = JsonSerializer.SerializeToElement("1080p"),
            StoredValue = JsonSerializer.SerializeToElement("2160p"), Constrained = true, ConstraintKind = "maximum" };
        Assert.Contains("choice of 2160p", DeviceSettingDisplay.ConstraintExplanation(limited));
        Assert.Null(DeviceSettingDisplay.InheritedSource(limited, "your"));
        Assert.Equal("From Alex's profile", DeviceSettingDisplay.InheritedSource(new() { Source = "profile" }, "Alex's"));
        Assert.Equal("App default", DeviceSettingDisplay.InheritedSource(new() { Source = "default" }, "your"));
    }

    [Fact]
    public void SessionMetadataReadsCurrentWireFields()
    {
        var row = JsonSerializer.Deserialize<AuthSession>("""{"id":"one","current":true,"last_seen_at":"2026-10-09T12:00:00Z","device_name":"Silo Windows","device_platform":"Windows"}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        Assert.True(row.IsCurrent);
        Assert.Equal("Active now", LoginSessionDisplay.LastSeen(row.LastSeenAt, DateTimeOffset.Parse("2026-10-09T12:01:00Z")));
        Assert.Equal("Silo Windows", LoginSessionDisplay.Name(row));
    }

    [Fact]
    public async Task CurrentSessionSeparatedFromPageRowsIsRetainedAndRevokeDoesNotRefresh()
    {
        var handler = new SessionHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://silo.example.test");
        client.SetAccessToken("fixture-only");
        var api = new AuthApi(client);
        var page = await api.GetSessionsPageAsync();
        Assert.True(page.CurrentSession!.IsCurrent);
        Assert.Single(page.Sessions);
        Assert.True(page.Page!.HasMore);
        var refreshes = 0;
        client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => api.RevokeSessionAsync("other"));
        Assert.Equal(0, refreshes);
        Assert.Equal(1, handler.Deletes);
    }

    private sealed class SessionHandler : HttpMessageHandler
    {
        public int Deletes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Deletes++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"type":"https://silo.test/problems/session_expired","status":401,"detail":"Expired"}""") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"items":[{"id":"other","current":false}],"current_session":{"id":"current","current":true},"page":{"has_more":true,"next_cursor":"opaque+next"}}""") });
        }
    }
}
