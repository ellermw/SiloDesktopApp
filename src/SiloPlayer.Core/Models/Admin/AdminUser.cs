namespace SiloPlayer.Core.Models.Admin;

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "user";
    public List<string> Permissions { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public List<int>? LibraryIds { get; set; }
    public long? AccessGroupId { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public int MaxStreams { get; set; }
    public int MaxTranscodes { get; set; }
    public bool TranscodeAllowed { get; set; } = true;
    public bool AudioTranscodeAllowed { get; set; } = true;
    public int MaxProfiles { get; set; }
    public bool DownloadAllowed { get; set; }
    public bool DownloadTranscodeAllowed { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? LastActiveAt { get; set; }
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
    public bool? TranscodeAllowed { get; set; }
    public bool? AudioTranscodeAllowed { get; set; }
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
    public List<string>? Permissions { get; set; }
    public List<int>? LibraryIds { get; set; }
    public bool LibraryIdsSpecified { get; set; }
    public long? AccessGroupId { get; set; }
    public bool AccessGroupIdSpecified { get; set; }
    public string? MaxPlaybackQuality { get; set; }
    public int? MaxStreams { get; set; }
    public int? MaxTranscodes { get; set; }
    public bool? TranscodeAllowed { get; set; }
    public bool? AudioTranscodeAllowed { get; set; }
    public int? MaxProfiles { get; set; }
    public bool? DownloadAllowed { get; set; }
    public bool? DownloadTranscodeAllowed { get; set; }
}

public class AdminUserProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
