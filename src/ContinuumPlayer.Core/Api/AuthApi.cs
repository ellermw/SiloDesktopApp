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

    // ===== Signup =====

    public Task<LoginResponse> SignupAsync(SignupRequest request, CancellationToken ct = default)
        => client.PostAsync<LoginResponse>("/api/v1/auth/signup", request, ct);

    public Task<SignupStatusResponse> GetSignupStatusAsync(CancellationToken ct = default)
        => client.GetAsync<SignupStatusResponse>("/api/v1/auth/signup", ct);

    // ===== Setup =====

    public Task<LoginResponse> SetupAsync(SetupRequest request, CancellationToken ct = default)
        => client.PostAsync<LoginResponse>("/api/v1/auth/setup", request, ct);

    public Task<SetupStatusResponse> GetSetupStatusAsync(CancellationToken ct = default)
        => client.GetAsync<SetupStatusResponse>("/api/v1/auth/setup", ct);

    // ===== Auth Providers =====

    public Task<AuthProvidersResponse> GetAuthProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<AuthProvidersResponse>("/api/v1/auth/providers", ct);

    // ===== Session Management =====

    public Task LogoutAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/auth/logout", new { }, ct);

    public Task<UserInfo> GetMeAsync(CancellationToken ct = default)
        => client.GetAsync<UserInfo>("/api/v1/auth/me", ct);

    public Task<AuthSessionsResponse> GetSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<AuthSessionsResponse>("/api/v1/auth/sessions", ct);

    public Task RevokeSessionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/auth/sessions/{Uri.EscapeDataString(id)}", ct);
}
