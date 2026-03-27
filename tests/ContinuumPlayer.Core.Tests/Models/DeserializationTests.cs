using System.Text.Json;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Tests.Models;

public class DeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Deserialize_LoginResponse()
    {
        var json = """
        {
            "access_token": "jwt-abc",
            "refresh_token": "jwt-xyz",
            "expires_in": 86400,
            "user": { "id": 1, "username": "mike", "email": "mike@example.com", "role": "admin" }
        }
        """;
        var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions)!;
        Assert.Equal("jwt-abc", result.AccessToken);
        Assert.Equal("jwt-xyz", result.RefreshToken);
        Assert.Equal(86400, result.ExpiresIn);
        Assert.Equal(1, result.User.Id);
        Assert.Equal("mike", result.User.Username);
        Assert.Equal("admin", result.User.Role);
    }

    [Fact]
    public void Deserialize_ProfilesResponse()
    {
        var json = """
        {
            "profiles": [{
                "id": "b49b0a6a-d185-4a98-b1de-753735f1919e",
                "name": "Mike",
                "has_pin": false,
                "is_child": false,
                "quality_preference": "auto",
                "subtitle_language": "en",
                "subtitle_mode": "always",
                "auto_skip_intro": true,
                "auto_skip_credits": true,
                "show_forced_subtitles": true,
                "library_restrictions_enabled": false,
                "allowed_library_ids": null,
                "max_playback_quality": "",
                "created_at": "2026-03-19T03:23:23Z",
                "updated_at": "2026-03-27T00:08:00Z"
            }]
        }
        """;
        var result = JsonSerializer.Deserialize<ProfilesResponse>(json, JsonOptions)!;
        Assert.Single(result.Profiles);
        Assert.Equal("Mike", result.Profiles[0].Name);
        Assert.False(result.Profiles[0].HasPin);
        Assert.True(result.Profiles[0].AutoSkipIntro);
    }

    [Fact]
    public void Deserialize_Library()
    {
        var json = """
        [
            {"id": 20, "name": "Movies", "type": "movies", "poster_url": "https://example.com/poster.jpg"},
            {"id": 19, "name": "TV Shows", "type": "series", "poster_url": "https://example.com/tv.jpg"}
        ]
        """;
        var result = JsonSerializer.Deserialize<List<Library>>(json, JsonOptions)!;
        Assert.Equal(2, result.Count);
        Assert.Equal("Movies", result[0].Name);
        Assert.Equal("movies", result[0].Type);
    }

    [Fact]
    public void Deserialize_HomeLayout()
    {
        var json = """
        {
            "sections": [
                { "id": "118166621620535300", "section_type": "collection", "title": "Popular Movies this Week", "featured": false, "item_limit": 50, "is_custom": false, "customized": false },
                { "id": "117998017897824260", "section_type": "continue_watching", "title": "Continue Watching", "featured": true, "item_limit": 20, "is_custom": false, "customized": true }
            ]
        }
        """;
        var result = JsonSerializer.Deserialize<HomeLayoutResponse>(json, JsonOptions)!;
        Assert.Equal(2, result.Sections.Count);
        Assert.False(result.Sections[0].Featured);
        Assert.True(result.Sections[1].Featured);
        Assert.Equal("continue_watching", result.Sections[1].SectionType);
    }

    [Fact]
    public void Deserialize_HomeSectionItem()
    {
        var json = """
        {
            "sections": [{
                "id": "118166621620535300",
                "section_type": "collection",
                "title": "Popular Movies this Week",
                "featured": false,
                "item_limit": 50,
                "total_count": 35,
                "is_custom": false,
                "customized": false,
                "items": [{
                    "content_id": "115561387572356099",
                    "type": "movie",
                    "title": "Peaky Blinders: The Immortal Man",
                    "year": 2026,
                    "genres": ["Crime", "Drama"],
                    "status": "matched",
                    "overview": "Tommy Shelby must choose.",
                    "poster_url": "https://example.com/poster.jpg",
                    "poster_thumbhash": "iBgGDQAbpriMVqroOGBLh5zfiPda",
                    "backdrop_url": "https://example.com/backdrop.jpg",
                    "backdrop_thumbhash": "xigCDIA2Un39QYi3V2dH/HeWTw==",
                    "logo_url": "https://example.com/logo.png",
                    "overlay_summary": { "resolution": "2160p", "audio": "EAC3", "release_type": "WEB-DL" },
                    "user_state": { "played": false, "is_favorite": false, "in_watchlist": false }
                }]
            }]
        }
        """;
        var result = JsonSerializer.Deserialize<HomeSectionsResponse>(json, JsonOptions)!;
        var item = result.Sections[0].Items[0];
        Assert.Equal("115561387572356099", item.ContentId);
        Assert.Equal("Peaky Blinders: The Immortal Man", item.Title);
        Assert.Equal(2026, item.Year);
        Assert.Equal("2160p", item.OverlaySummary.Resolution);
        Assert.False(item.UserState.Played);
    }

    [Fact]
    public void Deserialize_ApiError()
    {
        var json = """{"error": "unauthorized", "message": "Invalid token"}""";
        var result = JsonSerializer.Deserialize<ApiError>(json, JsonOptions)!;
        Assert.Equal("unauthorized", result.Error);
        Assert.Equal("Invalid token", result.Message);
    }
}
