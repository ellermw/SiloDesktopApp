namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminUserSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class AdminUserSettingsResponse
{
    public List<AdminUserSetting> Settings { get; set; } = [];
}

public sealed class AdminUserDeviceSettingsResponse
{
    public List<AdminDeviceSetting> Settings { get; set; } = [];
}
