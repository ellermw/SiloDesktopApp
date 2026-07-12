namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminDevicesResponse
{
    public List<AdminDeviceSummary> Devices { get; set; } = [];
}

public sealed class AdminDeviceProfileSummary
{
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public int OverrideCount { get; set; }
    public DateTimeOffset? LastUpdated { get; set; }
}

public class AdminDeviceSummary
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DevicePlatform { get; set; } = "";
    public int OverrideCount { get; set; }
    public int ProfileCount { get; set; }
    public List<AdminDeviceProfileSummary> Profiles { get; set; } = [];
    public DateTimeOffset? LastUpdated { get; set; }
}

public sealed class AdminDeviceSetting
{
    public int UserId { get; set; }
    public string ProfileId { get; set; } = "";
    public string? ProfileName { get; set; }
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DevicePlatform { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AdminDeviceDetail : AdminDeviceSummary
{
    public List<AdminDeviceSetting> Settings { get; set; } = [];
}
