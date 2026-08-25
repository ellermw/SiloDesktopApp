using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Api;

public class AuthApi(SiloApiClient client)
{
    public Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
        => LoginAsync(username, password, provider: null, ct);

    public Task<LoginResponse> LoginAsync(string username, string password, string? provider, CancellationToken ct = default)
        => LoginAsync(client.BaseUrl, username, password, provider, ct);

    public Task<LoginResponse> LoginAsync(string baseUrl, string username, string password, string? provider, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(baseUrl, "/api/v1/auth/login", new LoginRequest
        {
            Username = username,
            Password = password,
            Provider = string.IsNullOrWhiteSpace(provider) ? null : provider
        }, ct);

    public Task<RefreshResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => RefreshAsync(client.BaseUrl, refreshToken, ct);

    public Task<RefreshResponse> RefreshAsync(string baseUrl, string refreshToken, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<RefreshResponse>(baseUrl, "/api/v1/auth/refresh", new RefreshRequest { RefreshToken = refreshToken }, ct);

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.GetAsync<ProfilesResponse>("/api/v1/profiles", ct);

    public Task<List<AdminSession>> GetHouseholdSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminSession>>("/api/v1/profiles/household/sessions", ct);

    public Task<VerifyPinResponse> VerifyPinAsync(string profileId, string pin, CancellationToken ct = default)
        => client.PostAsync<VerifyPinResponse>($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}/verify-pin", new VerifyPinRequest { Pin = pin }, ct);

    public Task<Profile> CreateProfileAsync(string name, string? pin = null, bool isChild = false, CancellationToken ct = default)
        => CreateProfileAsync(new CreateProfileRequest
        {
            Name = name,
            Pin = string.IsNullOrWhiteSpace(pin) ? null : pin,
            IsChild = isChild,
        }, ct);

    public Task<Profile> CreateProfileAsync(CreateProfileRequest request, CancellationToken ct = default)
        => client.PostAsync<Profile>("/api/v1/profiles", request, ct);

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

    public Task<Profile> UpdateProfileAsync(string profileId, CreateProfileRequest request, CancellationToken ct = default)
        => client.PutAsync<Profile>($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}", request, ct);

    public Task<Profile> UploadProfileAvatarAsync(
        string profileId,
        string fileName,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default)
        => client.PutMultipartAsync<Profile>(
            $"/api/v1/profiles/{Uri.EscapeDataString(profileId)}/avatar",
            "avatar",
            fileName,
            fileBytes,
            contentType,
            ct);

    public Task<Profile> DeleteProfileAvatarAsync(string profileId, CancellationToken ct = default)
        => client.DeleteReturningAsync<Profile>($"/api/v1/profiles/{Uri.EscapeDataString(profileId)}/avatar", ct);

    // ===== Signup =====

    public Task<LoginResponse> SignupAsync(SignupRequest request, CancellationToken ct = default)
        => SignupAsync(client.BaseUrl, request, ct);

    public Task<LoginResponse> SignupAsync(string baseUrl, SignupRequest request, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(baseUrl, "/api/v1/auth/signup", request, ct);

    public Task<SignupStatusResponse> GetSignupStatusAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<SignupStatusResponse>("/api/v1/auth/signup", ct);

    // ===== Emailed invitations =====

    public Task<InvitationLookupResponse> GetInvitationAsync(string token, CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<InvitationLookupResponse>(
            $"/api/v1/invitations/{Uri.EscapeDataString(token)}", ct);

    public Task<LoginResponse> AcceptInvitationAsync(string token, string password, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(
            $"/api/v1/invitations/{Uri.EscapeDataString(token)}/accept",
            new AcceptInvitationRequest { Password = password },
            ct);

    // ===== Setup =====

    public Task<LoginResponse> SetupAsync(SetupRequest request, CancellationToken ct = default)
        => SetupAsync(client.BaseUrl, request, ct);

    public Task<LoginResponse> SetupAsync(string baseUrl, SetupRequest request, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(baseUrl, "/api/v1/auth/setup", request, ct);

    public Task<SetupStatusResponse> GetSetupStatusAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<SetupStatusResponse>("/api/v1/auth/setup", ct);

    // ===== Auth Providers =====

    public Task<List<AuthProvider>> GetAuthProvidersAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<List<AuthProvider>>("/api/v1/auth/providers", ct);

    public async Task<Uri> StartOAuthAsync(int installationId, CancellationToken ct = default)
    {
        if (installationId <= 0)
            throw new ArgumentOutOfRangeException(nameof(installationId));
        if (string.IsNullOrWhiteSpace(client.BaseUrl))
            throw new InvalidOperationException("Server URL is not configured.");

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{client.BaseUrl}/api/v1/auth/oauth/{installationId}/init");
        using var response = await http.SendAsync(request, ct);

        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
        {
            var location = response.Headers.Location;
            return location.IsAbsoluteUri ? location : new Uri(new Uri(client.BaseUrl), location);
        }

        var message = await response.Content.ReadAsStringAsync(ct);
        throw new ApiException(
            "oauth_init_failed",
            string.IsNullOrWhiteSpace(message) ? "Failed to start OAuth sign-in." : message.Trim(),
            (int)response.StatusCode);
    }

    public Task<OAuthCompleteResponse> CompleteOAuthAsync(string code, CancellationToken ct = default)
        => CompleteOAuthAsync(client.BaseUrl, code, ct);

    public Task<OAuthCompleteResponse> CompleteOAuthAsync(string baseUrl, string code, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<OAuthCompleteResponse>(baseUrl, "/api/v1/auth/oauth/complete",
            new Dictionary<string, object?> { ["code"] = code }, ct);

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
        => client.PostUnauthenticatedAsync<DeviceLoginStartResponse>("/api/v1/auth/device/start",
            new Dictionary<string, object?>
            {
                ["device_name"] = deviceName ?? "",
                ["device_platform"] = devicePlatform ?? "",
            }, ct);

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
        => client.PostUnauthenticatedAsync<DeviceLoginPollResponse>("/api/v1/auth/device/poll",
            new Dictionary<string, object?> { ["device_code"] = deviceCode }, ct);

    public Task<DeviceLoginCapabilityResponse> GetDeviceCapabilityAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<DeviceLoginCapabilityResponse>("/api/v1/auth/device/capability", ct);

    public Task DeviceApproveAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/auth/device/approve",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    public Task DeviceApproveHandoffAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/auth/device/approve-handoff",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    public Task DeviceDenyAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/auth/device/deny",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    // ===== Session Management =====

    public Task LogoutAsync(CancellationToken ct = default)
        => client.PostNoContentWithoutRefreshAsync("/api/v1/auth/logout", new Dictionary<string, object?>(), ct);

    /// <summary>
    /// Revokes only the active impersonated session. Automatic 401 refresh is
    /// deliberately disabled: a revoked impersonation token must never cause the
    /// client to discard the separately preserved administrator session.
    /// </summary>
    public Task EndImpersonationAsync(CancellationToken ct = default)
        => client.PostNoContentWithoutRefreshAsync(
            "/api/v1/auth/impersonation/end",
            new Dictionary<string, object?>(),
            ct);

    public Task LogoutAsync(string baseUrl, string accessToken, CancellationToken ct = default)
        => client.PostNoContentWithBearerAsync(
            baseUrl,
            "/api/v1/auth/logout",
            accessToken,
            new Dictionary<string, object?>(),
            ct);

    public Task<UserInfo> GetMeAsync(CancellationToken ct = default)
        => client.GetAsync<UserInfo>("/api/v1/auth/me", ct);

    public Task<UserInfo> GetMeAsync(string baseUrl, string accessToken, CancellationToken ct = default)
        => client.GetWithBearerAsync<UserInfo>(baseUrl, "/api/v1/auth/me", accessToken, ct);

    public Task<AuthSessionsResponse> GetSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<AuthSessionsResponse>("/api/v1/auth/sessions", ct);

    public Task RevokeSessionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/auth/sessions/{Uri.EscapeDataString(id)}", ct);
}
