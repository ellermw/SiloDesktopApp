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

    public Task DeleteProfileAsync(string profileId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}", ct);

    /// <summary>
    /// Updates a profile's name and optional PIN. Pass an empty string for
    /// <paramref name="pin"/> to clear the existing PIN (webui parity with
    /// the "Remove PIN" toggle in the profile editor). Pass null to leave it
    /// unchanged.
    /// </summary>
    public Task<Profile> UpdateProfileAsync(string profileId, string name, string? pin, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
        };
        // Send pin explicitly so server can tell "clear" ("") from "unchanged" (missing).
        if (pin != null) body["pin"] = pin;
        return client.PutAsync<Profile>($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}", body, ct);
    }

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

    // ===== Device Login (approver-side flow) =====
    //
    // Used by the Activate Device page. Device A calls /device/start, displays a
    // short user_code (e.g. "ABCD-EFGH") to the user, then polls /device/poll until
    // an approver signs in on device B, calls /device (lookup) to review the pending
    // request, and confirms with /device/approve or /device/deny.
    //
    // This client acts as the approver: lookup + approve/deny. DeviceStart/Poll are
    // included for completeness so the same AuthApi covers the whole flow.

    public Task<DeviceLoginStartResponse> DeviceStartAsync(string? deviceName = null, string? devicePlatform = null, CancellationToken ct = default)
        => client.PostAsync<DeviceLoginStartResponse>("/api/v1/auth/device/start",
            new { device_name = deviceName ?? "", device_platform = devicePlatform ?? "" }, ct);

    /// <summary>
    /// Look up a pending device login request. Exactly one of <paramref name="token"/>
    /// (browser_code from a deep link) or <paramref name="code"/> (user-typed short code)
    /// should be provided. Mirrors the web ActivateDevice query params.
    /// </summary>
    public Task<DeviceLoginLookupResponse> DeviceLookupAsync(string? token, string? code, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(token)) qs.Add($"token={Uri.EscapeDataString(token)}");
        else if (!string.IsNullOrEmpty(code)) qs.Add($"code={Uri.EscapeDataString(code)}");
        var query = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return client.GetAsync<DeviceLoginLookupResponse>($"/api/v1/auth/device{query}", ct);
    }

    public Task<DeviceLoginPollResponse> DevicePollAsync(string deviceCode, CancellationToken ct = default)
        => client.PostAsync<DeviceLoginPollResponse>("/api/v1/auth/device/poll",
            new { device_code = deviceCode }, ct);

    public Task DeviceApproveAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/auth/device/approve",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    public Task DeviceDenyAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/auth/device/deny",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

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
