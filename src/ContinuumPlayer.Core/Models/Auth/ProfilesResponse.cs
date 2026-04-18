namespace ContinuumPlayer.Core.Models.Auth;
public class ProfilesResponse
{
    public List<Profile> Profiles { get; set; } = [];
}
public class Profile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool HasPin { get; set; }
    public bool IsChild { get; set; }
    /// <summary>
    /// Upstream commit c3f2da5: household primary profile. First profile per
    /// user is auto-flagged; can manage sibling profiles without server-admin
    /// rights and is protected from direct deletion.
    /// </summary>
    public bool IsPrimary { get; set; }
    public string QualityPreference { get; set; } = "";
    public string SubtitleLanguage { get; set; } = "";
    public string SubtitleMode { get; set; } = "";
    public bool AutoSkipIntro { get; set; }
    public bool AutoSkipCredits { get; set; }
    public bool ShowForcedSubtitles { get; set; }
    public bool LibraryRestrictionsEnabled { get; set; }
    public List<int>? AllowedLibraryIds { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
