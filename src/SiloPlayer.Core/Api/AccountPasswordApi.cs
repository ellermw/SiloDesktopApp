using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Api;

public sealed class AccountPasswordApi(SiloApiClient client)
{
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);

    public Task<AccountPasswordCapability> GetCapabilityAsync(ApiRequestContext context, CancellationToken ct = default)
        => client.SendRequestAsync<AccountPasswordCapability>(context, HttpMethod.Get,
            "/api/v2/account/password/capability", null, ct);

    public Task ChangePasswordAsync(ApiRequestContext context, string currentPassword, string newPassword,
        CancellationToken ct = default)
        => ChangePasswordAsync(context, currentPassword, newPassword, false, ct);

    // Require both arguments so legacy four-argument calls with default remain unambiguous.
    public Task ChangePasswordAsync(ApiRequestContext context, string currentPassword, string newPassword,
        bool signOutOtherSessions, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["current_password"] = currentPassword,
            ["new_password"] = newPassword
        };
        // Older servers reject this member even when it is false.
        if (signOutOtherSessions) body["sign_out_other_sessions"] = true;
        return client.SendNoContentRequestAsync(context, HttpMethod.Post, "/api/v2/account/password", body, ct);
    }
}
