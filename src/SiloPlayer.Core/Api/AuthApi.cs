using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Sessions;

namespace SiloPlayer.Core.Api;

public partial class AuthApi(SiloApiClient client)
{
    public Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
        => LoginAsync(username, password, provider: null, ct);

    public Task<LoginResponse> LoginAsync(string username, string password, string? provider, CancellationToken ct = default)
        => LoginAsync(client.BaseUrl, username, password, provider, ct);

    public Task<LoginResponse> LoginAsync(string baseUrl, string username, string password, string? provider, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(baseUrl, "/api/v2/auth/login", new LoginRequest
        {
            Username = username,
            Password = password,
            Provider = string.IsNullOrWhiteSpace(provider) ? null : provider
        }, ct);

    public Task<RefreshResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => RefreshAsync(client.BaseUrl, refreshToken, ct);

    public Task<RefreshResponse> RefreshAsync(string baseUrl, string refreshToken, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<RefreshResponse>(baseUrl, "/api/v2/auth/refresh", new RefreshRequest { RefreshToken = refreshToken }, ct);

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.GetAsync<ProfilesResponse>("/api/v2/profiles", ct);

    public async Task<List<PlaybackSessionSummary>> GetHouseholdSessionsAsync(CancellationToken ct = default)
    {
        var response = await client.GetAsync<JsonObject>("/api/v2/profiles/household/sessions", ct);
        var result = new List<PlaybackSessionSummary>();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        foreach (var item in response["items"]?.AsArray() ?? throw new JsonException("Missing household sessions."))
        {
            var row = item?.AsObject() ?? throw new JsonException("Invalid household session.");
            // This UI model is also used by legacy admin reads. Adapt the v2 resource
            // name and string identifiers only at this verified v2 boundary.
            row["session_id"] = row["id"]?.DeepClone();
            result.Add(row.Deserialize<PlaybackSessionSummary>(options) ?? throw new JsonException("Invalid household session."));
        }
        return result;
    }

    public Task<VerifyPinResponse> VerifyPinAsync(string profileId, string pin, CancellationToken ct = default)
        => client.PostAsync<VerifyPinResponse>($"/api/v2/profiles/{Uri.EscapeDataString(profileId)}/verify-pin", new VerifyPinRequest { Pin = pin }, ct);

    public Task<Profile> CreateProfileAsync(string name, string? pin = null, bool isChild = false, CancellationToken ct = default)
        => CreateProfileAsync(new CreateProfileRequest
        {
            Name = name,
            Pin = string.IsNullOrWhiteSpace(pin) ? null : pin,
            IsChild = isChild,
        }, ct);

    public Task<Profile> CreateProfileAsync(CreateProfileRequest request, CancellationToken ct = default)
        => client.PostAsync<Profile>("/api/v2/profiles", ProfileBody(request, updating: false), ct);

    public Task DeleteProfileAsync(string profileId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/profiles/{Uri.EscapeDataString(profileId)}", ct);

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
        if (pin != null) body["pin"] = pin.Length == 0 ? null : pin;
        return client.PatchAsync<Profile>($"/api/v2/profiles/{Uri.EscapeDataString(profileId)}", body, ct);
    }

    public Task<Profile> UpdateProfileAsync(string profileId, CreateProfileRequest request, CancellationToken ct = default)
        => client.PatchAsync<Profile>($"/api/v2/profiles/{Uri.EscapeDataString(profileId)}", ProfileBody(request, updating: true), ct);

    private static Dictionary<string, object?> ProfileBody(CreateProfileRequest request, bool updating)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = request.Name,
            ["is_child"] = request.IsChild,
            ["library_restrictions_enabled"] = request.LibraryRestrictionsEnabled,
            ["allowed_library_ids"] = request.AllowedLibraryIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(),
        };
        if (!string.IsNullOrEmpty(request.MaxContentRating)) body["max_content_rating"] = request.MaxContentRating;
        else if (updating) body["max_content_rating"] = null;
        if (request.MaxAdvisoryAgeSupported)
        {
            if (request.MaxAdvisoryAge.HasValue || updating) body["max_advisory_age"] = request.MaxAdvisoryAge;
            if (request.RequireAdvisoryAgeSupported)
                body["require_advisory_age"] = request.MaxAdvisoryAge.HasValue && request.RequireAdvisoryAge;
        }
        if (request.Avatar != null)
            body["avatar"] = updating && request.Avatar.Length == 0 ? null : request.Avatar;
        if (!string.IsNullOrEmpty(request.Pin)) body["pin"] = request.Pin;
        else if (updating && request.Pin != null) body["pin"] = null;
        if (!string.IsNullOrEmpty(request.MaxPlaybackQuality)) body["max_playback_quality"] = request.MaxPlaybackQuality;
        else if (updating) body["max_playback_quality"] = null;
        return body;
    }

    public Task<Profile> UploadProfileAvatarAsync(
        string profileId,
        string fileName,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default)
        => client.PutMultipartAsync<Profile>(
            $"/api/v2/profiles/{Uri.EscapeDataString(profileId)}/avatar",
            "avatar",
            fileName,
            fileBytes,
            contentType,
            ct);

    public Task<Profile> DeleteProfileAvatarAsync(string profileId, CancellationToken ct = default)
        => client.DeleteReturningAsync<Profile>($"/api/v2/profiles/{Uri.EscapeDataString(profileId)}/avatar", ct);

    // ===== Signup =====

    public Task<LoginResponse> SignupAsync(SignupRequest request, CancellationToken ct = default)
        => SignupAsync(client.BaseUrl, request, ct);

    public Task<LoginResponse> SignupAsync(string baseUrl, SignupRequest request, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(baseUrl, "/api/v2/auth/signup", request, ct);

    public Task<SignupStatusResponse> GetSignupStatusAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<SignupStatusResponse>("/api/v2/auth/signup", ct);

    // ===== Emailed invitations =====

    public Task<InvitationLookupResponse> GetInvitationAsync(string token, CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<InvitationLookupResponse>(
            $"/api/v2/invitations/{Uri.EscapeDataString(token)}", ct);

    public Task<InvitationAcceptanceResponse> AcceptInvitationAsync(string token, string password, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<InvitationAcceptanceResponse>(
            $"/api/v2/invitations/{Uri.EscapeDataString(token)}/accept",
            new AcceptInvitationRequest { Password = password },
            ct);

    // ===== Setup =====

    public Task<LoginResponse> SetupAsync(SetupRequest request, CancellationToken ct = default)
        => SetupAsync(client.BaseUrl, request, ct);

    public Task<LoginResponse> SetupAsync(string baseUrl, SetupRequest request, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<LoginResponse>(baseUrl, "/api/v2/auth/setup", request, ct);

    public Task<SetupStatusResponse> GetSetupStatusAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<SetupStatusResponse>("/api/v2/system/setup", ct);

    // ===== Auth Providers =====

    public async Task<List<AuthProvider>> GetAuthProvidersAsync(CancellationToken ct = default)
        => (await client.GetUnauthenticatedAsync<AuthProvidersResponse>("/api/v2/auth/providers", ct)).Providers;

    public Task<ServerIdentity> GetServerIdentityAsync(string serverBase, CancellationToken ct = default)
    {
        if (client.BaseUrl != serverBase) throw new OperationCanceledException("The selected server changed.");
        return client.GetUnauthenticatedAsync<ServerIdentity>("/api/v2/system/identity", ct);
    }

    public async Task<Uri> StartOAuthAsync(int installationId, CancellationToken ct = default)
    {
        if (installationId <= 0)
            throw new ArgumentOutOfRangeException(nameof(installationId));
        if (string.IsNullOrWhiteSpace(client.BaseUrl))
            throw new InvalidOperationException("Server URL is not configured.");

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{client.BaseUrl}/api/v2/auth/oauth/{installationId}/init");
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
        => client.PostUnauthenticatedAsync<OAuthCompleteResponse>(baseUrl, "/api/v2/auth/oauth/complete",
            new Dictionary<string, object?> { ["code"] = code }, ct);

    public Task<OAuthCompleteResponse> CompleteNativeOAuthAsync(string baseUrl, string code, string codeVerifier, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<OAuthCompleteResponse>(baseUrl, "/api/v2/auth/oauth/complete",
            new Dictionary<string, object?> { ["code"] = code, ["code_verifier"] = codeVerifier }, ct);

    public Task DeviceCancelAsync(string baseUrl, string deviceCode, CancellationToken ct = default)
        => client.PostUnauthenticatedNoContentAsync(baseUrl, "/api/v2/auth/device/cancel", new { device_code = deviceCode }, ct);

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
        => DeviceStartAsync(client.BaseUrl, deviceName, devicePlatform, ct);

    public Task<DeviceLoginStartResponse> DeviceStartAsync(string baseUrl, string? deviceName, string? devicePlatform, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<DeviceLoginStartResponse>(baseUrl, "/api/v2/auth/device/start",
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
        return client.GetUnauthenticatedAsync<DeviceLoginLookupResponse>($"/api/v2/auth/device{query}", ct);
    }

    public Task<DeviceLoginPollResponse> DevicePollAsync(string deviceCode, CancellationToken ct = default)
        => DevicePollAsync(client.BaseUrl, deviceCode, ct);

    public Task<DeviceLoginPollResponse> DevicePollAsync(string baseUrl, string deviceCode, CancellationToken ct = default)
        => client.PostUnauthenticatedAsync<DeviceLoginPollResponse>(baseUrl, "/api/v2/auth/device/poll",
            new Dictionary<string, object?> { ["device_code"] = deviceCode }, ct);

    public Task<DeviceLoginCapabilityResponse> GetDeviceCapabilityAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<DeviceLoginCapabilityResponse>("/api/v2/auth/device/capability", ct);

    public Task DeviceApproveAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v2/auth/device/approve",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    public Task DeviceApproveHandoffAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v2/auth/device/approve-handoff",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    public Task DeviceDenyAsync(string? token, string? code, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v2/auth/device/deny",
            new DeviceDecisionRequest { Token = token, Code = code }, ct);

    // ===== Session Management =====

    public Task LogoutAsync(CancellationToken ct = default)
        => client.PostNoContentWithoutRefreshAsync("/api/v2/auth/logout", new Dictionary<string, object?>(), ct);

    public Task LogoutAsync(string baseUrl, string accessToken, CancellationToken ct = default)
        => client.PostNoContentWithBearerAsync(
            baseUrl,
            "/api/v2/auth/logout",
            accessToken,
            new Dictionary<string, object?>(),
            ct);

    public Task<UserInfo> GetMeAsync(CancellationToken ct = default)
        => client.GetAsync<UserInfo>("/api/v2/account/me", ct);

    public Task<UserInfo> GetMeAsync(string baseUrl, string accessToken, CancellationToken ct = default)
        => client.GetWithBearerAsync<UserInfo>(baseUrl, "/api/v2/account/me", accessToken, ct);

    public async Task<AuthSessionsResponse> GetSessionsAsync(CancellationToken ct = default)
        => new() { Sessions = await client.GetAllItemsAsync<AuthSession>("/api/v2/auth/sessions?limit=100", ct) };

    public Task<LoginSessionCapabilities> GetLoginSessionCapabilitiesAsync(CancellationToken ct = default)
        => client.GetAsync<LoginSessionCapabilities>("/api/v2/auth/sessions/capabilities", ct);

    public async Task<AuthSessionsResponse> GetSessionsPageAsync(string? cursor = null, CancellationToken ct = default)
    {
        var page = await client.GetAsync<AuthSessionsResponse>("/api/v2/auth/sessions?limit=50" +
            (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), ct);
        if (page.Page == null || page.Page.HasMore && string.IsNullOrWhiteSpace(page.Page.NextCursor))
            throw new InvalidDataException("Invalid session pagination. Reload the list.");
        return page;
    }

    public Task RevokeSessionAsync(string id, CancellationToken ct = default)
        => RevokeSessionAsync(client.CaptureContext(), id, ct);

    public Task RevokeSessionAsync(ApiRequestContext context, string id, CancellationToken ct = default)
        => client.SendNoContentRequestWithoutRefreshAsync(context, HttpMethod.Delete,
            $"/api/v2/auth/sessions/{Uri.EscapeDataString(id)}", null, ct);
}
