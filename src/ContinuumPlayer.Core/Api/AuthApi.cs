using ContinuumPlayer.Core.Models.Auth;

namespace ContinuumPlayer.Core.Api;

public class AuthApi(ContinuumApiClient client)
{
    public Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
        => client.PostAsync<LoginResponse>("/api/v1/auth/login", new LoginRequest { Username = username, Password = password }, ct);

    public Task<RefreshResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => client.PostAsync<RefreshResponse>("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = refreshToken }, ct);

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.GetAsync<ProfilesResponse>("/api/v1/profiles", ct);

    public Task<VerifyPinResponse> VerifyPinAsync(string profileId, string pin, CancellationToken ct = default)
        => client.PostAsync<VerifyPinResponse>($"/api/v1/profiles/{profileId}/verify-pin", new VerifyPinRequest { Pin = pin }, ct);

    public Task<Profile> CreateProfileAsync(string name, CancellationToken ct = default)
        => client.PostAsync<Profile>("/api/v1/profiles", new CreateProfileRequest { Name = name }, ct);
}
