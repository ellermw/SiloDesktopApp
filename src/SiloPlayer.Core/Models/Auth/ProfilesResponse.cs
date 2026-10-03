namespace SiloPlayer.Core.Models.Auth;
public class ProfilesResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<Profile> Profiles { get; set; } = [];
    public bool AvatarUploadEnabled { get; set; }
    public bool MaxAdvisoryAgeSupported { get; set; }
    public bool RequireAdvisoryAgeSupported { get; set; }
}
public class Profile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? AvatarSource { get; set; }
    public bool HasPin { get; set; }
    public bool IsChild { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShowsPinBadge => HasPin && !IsChild;
    [System.Text.Json.Serialization.JsonIgnore]
    public string EditProfileAccessibleName => $"Edit profile {Name}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string SelectProfileAccessibleName => HasPin ? $"{Name} (PIN protected)" : Name;
    /// <summary>
    /// Upstream commit c3f2da5: household primary profile. First profile per
    /// user is auto-flagged; can manage sibling profiles without server-admin
    /// rights and is protected from direct deletion.
    /// </summary>
    public bool IsPrimary { get; set; }
    public string MaxContentRating { get; set; } = "";
    public int? MaxAdvisoryAge { get; set; }
    public bool RequireAdvisoryAge { get; set; }
    public string QualityPreference { get; set; } = "";
    public string Language { get; set; } = "";
    public string? PreferredMetadataLanguage { get; set; }
    public string SubtitleLanguage { get; set; } = "";
    public string SubtitleMode { get; set; } = "";
    public bool AutoSkipIntro { get; set; }
    public bool AutoSkipCredits { get; set; }
    public bool AutoSkipRecap { get; set; }
    public bool AutoPlayNextPreview { get; set; }
    public bool ShowForcedSubtitles { get; set; }
    public bool LibraryRestrictionsEnabled { get; set; }
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)]
    public List<int>? AllowedLibraryIds { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
