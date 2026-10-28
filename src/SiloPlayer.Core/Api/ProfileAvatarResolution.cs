using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Api;

/// <summary>
/// The server sends avatar_url as a URI reference: bundled presets arrive as
/// root-relative paths such as /profile-avatars/avatar-3.svg. Profiles leave
/// the API layer with absolute URLs so every view can load them.
/// </summary>
internal static class ProfileAvatarResolution
{
    public static async Task<Profile> WithResolvedAvatar(this SiloApiClient client, Func<Task<Profile>> request)
    {
        var owner = client.CaptureContext();
        var profile = await request().ConfigureAwait(false);
        if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Profile response authority changed.");
        profile.AvatarUrl = SiloApiClient.ResolveServerUrl(profile.AvatarUrl, owner.BaseUrl);
        return profile;
    }

    public static async Task<ProfilesResponse> WithResolvedAvatars(this SiloApiClient client, Func<Task<ProfilesResponse>> request)
    {
        var owner = client.CaptureContext();
        var response = await request().ConfigureAwait(false);
        if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Profile response authority changed.");
        foreach (var profile in response.Profiles)
            profile.AvatarUrl = SiloApiClient.ResolveServerUrl(profile.AvatarUrl, owner.BaseUrl);
        return response;
    }
}
