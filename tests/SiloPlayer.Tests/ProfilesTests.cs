using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class ProfilesTests
{
    [Fact]
    public async Task ProfileListUsesCurrentAvatarAndAccessContract()
    {
        var handler = new JsonHandler("""
        {"avatar_upload_enabled":true,"items":[{"id":"p1","name":"Alex","avatar":"dicebear:identicon:alex","avatar_url":"https://example.test/avatar.png","avatar_source":"preset","has_pin":true,"is_child":false,"is_primary":true,"max_content_rating":"R","quality_preference":"original","language":"en","preferred_metadata_language":"en","subtitle_language":"en","subtitle_mode":"auto","auto_skip_intro":true,"auto_skip_credits":false,"library_restrictions_enabled":true,"allowed_library_ids":["2","4"],"max_playback_quality":"2160p","created_at":"","updated_at":""}]}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var response = await new AuthApi(client).GetProfilesAsync();
        var profile = Assert.Single(response.Profiles);

        Assert.True(response.AvatarUploadEnabled);
        Assert.Equal("https://example.test/avatar.png", profile.AvatarUrl);
        Assert.True(profile.IsPrimary);
        Assert.Equal(new[] { 2, 4 }, profile.AllowedLibraryIds);
        Assert.Equal("R", profile.MaxContentRating);
        Assert.Equal("en", profile.Language);
        Assert.Equal("en", profile.PreferredMetadataLanguage);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
