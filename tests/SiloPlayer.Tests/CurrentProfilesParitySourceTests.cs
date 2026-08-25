using SiloPlayer.Core.Models.Auth;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class CurrentProfilesParitySourceTests
{
    [Fact]
    public void ProfileRequestCarriesCurrentAccessAndPlaybackFields()
    {
        var request = new CreateProfileRequest
        {
            Name = "Alex",
            Avatar = "preset:dicebear:identicon:cosmic-otter",
            IsChild = true,
            MaxContentRating = "PG",
            MaxPlaybackQuality = "1080p",
            LibraryRestrictionsEnabled = true,
            AllowedLibraryIds = [3, 7],
        };
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        Assert.Contains("\"avatar\":\"preset:dicebear:identicon:cosmic-otter\"", json);
        Assert.Contains("\"max_content_rating\":\"PG\"", json);
        Assert.Contains("\"max_playback_quality\":\"1080p\"", json);
        Assert.Contains("\"library_restrictions_enabled\":true", json);
        Assert.Contains("\"allowed_library_ids\":[3,7]", json);
        Assert.DoesNotContain("\"pin\"", json);
    }

    [Fact]
    public void ProfileEditorExposesEveryCurrentWebUiAccessControl()
    {
        var editor = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "ProfileEditorDialog.cs");

        Assert.Contains("Preset avatars", editor);
        Assert.Contains("Custom upload", editor);
        Assert.Contains("Kids profile", editor);
        Assert.Contains("Maximum content rating", editor);
        Assert.Contains("Maximum playback quality", editor);
        Assert.Contains("Restrict libraries", editor);
        Assert.Contains("UploadProfileAvatarAsync", editor);
        Assert.Contains("DeleteProfileAvatarAsync", editor);
        Assert.Contains("Choose at least one library.", editor);
    }

    [Fact]
    public void ProfileAvatarApiUsesCurrentReturningEndpoints()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AuthApi.cs");
        var client = ReadRepoFile("src", "SiloPlayer.Core", "Api", "SiloApiClient.cs");

        Assert.Contains("/avatar", api);
        Assert.Contains("PutMultipartAsync<Profile>", api);
        Assert.Contains("DeleteReturningAsync<Profile>", api);
        Assert.Contains("public async Task<T> PutMultipartAsync<T>", client);
    }

    [Fact]
    public void SettingsProfileRowsUseCurrentLabelsAndDoNotBlankDuringFetch()
    {
        var settings = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var loadStart = settings.IndexOf("private async Task LoadProfilesAsync", StringComparison.Ordinal);
        Assert.True(loadStart >= 0, "SettingsPage.xaml.cs no longer defines LoadProfilesAsync.");
        var loadEnd = settings.IndexOf("private static Border ProfileBadge", loadStart, StringComparison.Ordinal);
        Assert.True(loadEnd > loadStart);
        var load = settings[loadStart..loadEnd];

        var fetch = load.IndexOf("await authApi.GetProfilesAsync()", StringComparison.Ordinal);
        var clear = load.IndexOf("ProfileCardsPanel.Children.Clear()", StringComparison.Ordinal);
        Assert.True(fetch >= 0 && clear > fetch);
        Assert.Contains("Text = \"Current\"", load);
        Assert.Contains("new TextBlock { Text = \"Edit\" }", load);
        Assert.Contains("new TextBlock { Text = \"Delete\" }", load);
        Assert.Contains("profiles.Count <= 1", load);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
