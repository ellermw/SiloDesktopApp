using System.Text.Json;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AccountSessionRestrictionTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Theory]
    [InlineData("""{"available":true}""", """{"can_unlink":true}""", true)]
    [InlineData("""{"available":true,"revoke_other_sessions":true}""", """{"can_link":true,"can_unlink":true}""", true)]
    [InlineData("""{"available":true,"revoke_other_sessions":false}""", """{"can_link":false,"can_unlink":true}""", false)]
    public void OptionalPermissionsDefaultToAllowed(string sessionsJson, string identitiesJson, bool allowed)
    {
        var sessions = JsonSerializer.Deserialize<LoginSessionCapabilities>(sessionsJson, Wire)!;
        var identities = JsonSerializer.Deserialize<AccountIdentityCollection>(identitiesJson, Wire)!;
        Assert.True(sessions.Available);
        Assert.Equal(allowed, sessions.RevokeOtherSessions);
        Assert.Equal(allowed, identities.CanLink);
        Assert.True(identities.CanUnlink);
    }

    [Fact]
    public void DeviceCodePlaceholdersAreRedactedOnlyForOtherSessionsWithMatchingDates()
    {
        const string list = """
            {"current_session":{"id":"own","current":true,"device_name":"This app","created_at":"2026-11-26T00:00:00Z","expires_at":"2026-12-26T00:00:00Z"},
             "items":[
                {"id":"other","current":false,"device_name":"","ip_address":"","last_seen_at":null,"created_at":"2026-11-26T00:00:00Z","expires_at":"2026-12-26T00:00:00Z"},
                {"id":"named","device_name":"Other device","created_at":"2026-11-26T00:00:00Z","expires_at":"2026-12-26T00:00:00Z"},
                {"id":"registered","device_id":"registered-device","created_at":"2026-11-26T00:00:00Z","expires_at":"2026-12-26T00:00:00Z"},
                {"id":"anonymous","created_at":"2026-11-25T00:00:00Z","expires_at":"2026-12-25T00:00:00Z"}]}
            """;
        var result = JsonSerializer.Deserialize<AuthSessionsResponse>(list, Wire)!;
        var own = Assert.IsType<AuthSession>(result.CurrentSession);
        Assert.True(LoginSessionDisplay.HasRedactedDetails(result.Sessions[0], own));
        Assert.Equal("other", result.Sessions[0].Id);
        Assert.False(LoginSessionDisplay.HasRedactedDetails(own, own));
        Assert.False(LoginSessionDisplay.HasRedactedDetails(result.Sessions[0], null));
        Assert.All(result.Sessions.Skip(1), session => Assert.False(LoginSessionDisplay.HasRedactedDetails(session, own)));
    }
}
