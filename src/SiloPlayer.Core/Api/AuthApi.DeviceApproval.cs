using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Api;

/// <summary>
/// The approver's side of a device sign-in (the Activate device page), bound to the
/// signed-in identity that opened the page: a reply that arrives after an account,
/// profile or server switch is dropped by the context check, never applied to the
/// next session. A decision is sent once and never replayed (no token-refresh retry).
/// </summary>
public partial class AuthApi
{
    private static string DeviceRequestPath(string? token, string? code)
        => string.IsNullOrEmpty(token)
            ? "/api/v2/auth/device?code=" + Uri.EscapeDataString(code ?? "")
            : "/api/v2/auth/device?token=" + Uri.EscapeDataString(token);

    /// <summary>
    /// Looks the request up with the signed-in session's bearer. A six-digit Jellyfin Quick Connect
    /// code is found only that way (and spends the account's code budget); an eight-character
    /// device code is found either way.
    /// </summary>
    public Task<DeviceLoginLookupResponse> DeviceLookupAsync(ApiRequestContext owner, string? token, string? code, CancellationToken ct = default)
        => client.SendRequestAsync<DeviceLoginLookupResponse>(owner, HttpMethod.Get, DeviceRequestPath(token, code), null, ct);

    /// <summary>Approves or declines a device sign-in as the signed-in account (only a login session decides).</summary>
    public Task DeviceDecideAsync(ApiRequestContext owner, bool approve, string? token, string? code, CancellationToken ct = default)
        => client.SendNoContentRequestWithoutRefreshAsync(owner, HttpMethod.Post,
            approve ? "/api/v2/auth/device/approve" : "/api/v2/auth/device/deny",
            new DeviceDecisionRequest { Token = string.IsNullOrEmpty(token) ? null : token, Code = string.IsNullOrEmpty(token) ? code : null }, ct);

    /// <summary>Approves a Quick Connect request for the profile this session is already using.</summary>
    public Task ApproveQuickConnectAsync(ApiRequestContext owner, string code, CancellationToken ct = default)
        => client.SendNoContentRequestWithoutRefreshAsync(owner, HttpMethod.Post, "/api/v2/auth/device/approve-handoff",
            new DeviceDecisionRequest { Code = code }, ct);

    /// <summary>Captures another household profile (with its PIN proof when it has one) for a Quick Connect approval.</summary>
    public CapturedProfileAuthority CaptureProfileAuthority(ApiRequestContext owner, string profileId, string? proof)
        => client.CaptureProfileAuthority(owner, profileId, proof);

    /// <summary>Approves a Quick Connect request for a profile other than the active one.</summary>
    public Task<DeviceDecisionResult> ApproveQuickConnectAsync(CapturedProfileAuthority authority, string code, CancellationToken ct = default)
        => client.SendCapturedProfileAsync<DeviceDecisionResult>(authority, HttpMethod.Post, "/api/v2/auth/device/approve-handoff",
            new DeviceDecisionRequest { Code = code }, ct);
}
