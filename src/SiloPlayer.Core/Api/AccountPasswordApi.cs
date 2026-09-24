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
        => client.SendNoContentRequestAsync(context, HttpMethod.Post, "/api/v2/account/password",
            new { current_password = currentPassword, new_password = newPassword }, ct);
}
