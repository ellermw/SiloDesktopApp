using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Core.Api;

/// <summary>
/// The server sends avatar_url as a URI reference: bundled presets arrive as
/// root-relative paths such as /profile-avatars/avatar-3.svg. Profiles leave
/// the API layer with absolute URLs so every view can load them.
/// </summary>
internal static class ProfileAvatarResolution
{
    public static async Task<Profile> WithResolvedAvatar(this SiloApiClient client, Task<Profile> request)
    {
        var profile = await request.ConfigureAwait(false);
        profile.AvatarUrl = client.ResolveServerUrl(profile.AvatarUrl);
        return profile;
    }

    public static async Task<ProfilesResponse> WithResolvedAvatars(this SiloApiClient client, Task<ProfilesResponse> request)
    {
        var response = await request.ConfigureAwait(false);
        foreach (var profile in response.Profiles)
            profile.AvatarUrl = client.ResolveServerUrl(profile.AvatarUrl);
        return response;
    }
}
