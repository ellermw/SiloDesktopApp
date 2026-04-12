namespace ContinuumPlayer.Core.Models.Admin;

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "user";
    public bool Enabled { get; set; } = true;
    public List<int>? LibraryIds { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public int MaxStreams { get; set; }
    public int MaxTranscodes { get; set; }
    public int MaxProfiles { get; set; }
    public bool DownloadAllowed { get; set; }
    public bool DownloadTranscodeAllowed { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "user";
    public List<int>? LibraryIds { get; set; }
    public string? MaxPlaybackQuality { get; set; }
    public int? MaxStreams { get; set; }
    public int? MaxTranscodes { get; set; }
    public int? MaxProfiles { get; set; }
    public bool? DownloadAllowed { get; set; }
    public bool? DownloadTranscodeAllowed { get; set; }
}

public class UpdateUserRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public bool? Enabled { get; set; }
    public List<int>? LibraryIds { get; set; }
    public string? MaxPlaybackQuality { get; set; }
    public int? MaxStreams { get; set; }
    public int? MaxTranscodes { get; set; }
    public int? MaxProfiles { get; set; }
    public bool? DownloadAllowed { get; set; }
    public bool? DownloadTranscodeAllowed { get; set; }
}

public class AdminUserProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
