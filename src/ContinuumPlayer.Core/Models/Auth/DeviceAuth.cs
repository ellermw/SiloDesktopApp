namespace ContinuumPlayer.Core.Models.Auth;

/// <summary>
/// Response from <c>POST /api/v1/auth/device/start</c> — kicks off a device login
/// flow where device A displays a short code that the user enters on device B
/// (this client, when acting as the approver).
/// </summary>
public class DeviceLoginStartResponse
{
    public string DeviceCode { get; set; } = "";
    public string UserCode { get; set; } = "";
    public string MatchCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string VerificationUriComplete { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
    public string DeviceName { get; set; } = "";
    public string DevicePlatform { get; set; } = "";
}

/// <summary>
/// Response from <c>GET /api/v1/auth/device?token=|code=</c> — describes a pending
/// device login request so the approver can review the device details before
/// approving or denying.
/// </summary>
public class DeviceLoginLookupResponse
{
    /// <summary>One of: pending, approved, denied, expired, consumed.</summary>
    public string Status { get; set; } = "";
    public string? UserCode { get; set; }
    public string? MatchCode { get; set; }
    public string? DeviceName { get; set; }
    public string? DevicePlatform { get; set; }
    public string? IpAddressHint { get; set; }
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// Request body for <c>POST /api/v1/auth/device/approve</c> and
/// <c>POST /api/v1/auth/device/deny</c>. Exactly one of Token (browser_code)
/// or Code (user_code) should be provided — matches the webui payload.
/// </summary>
public class DeviceDecisionRequest
{
    public string? Token { get; set; }
    public string? Code { get; set; }
}

/// <summary>
/// Response from <c>POST /api/v1/auth/device/poll</c> — the originating device
/// polls this to collect its tokens once an approver has accepted the request.
/// Included here for completeness even though the Desktop player is the approver.
/// </summary>
public class DeviceLoginPollResponse
{
    public string Status { get; set; } = "";
    public int PollAfter { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int? ExpiresIn { get; set; }
    public UserInfo? User { get; set; }
}
