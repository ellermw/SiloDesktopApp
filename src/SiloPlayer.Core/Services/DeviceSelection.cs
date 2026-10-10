using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public static class DeviceSelection
{
    public static string Key(UserDevice device) => $"{device.ProfileId}:{device.DeviceId}";
    public static UserDevice? Default(IReadOnlyList<UserDevice> devices, string? ownProfileId)
        => devices.FirstOrDefault(device => device.IsCurrentDevice && device.ProfileId == ownProfileId)
           ?? devices.FirstOrDefault(device => device.IsCurrentDevice) ?? devices.FirstOrDefault();
    public static UserDevice? Select(IReadOnlyList<UserDevice> devices, string? key, string? ownProfileId)
        => devices.FirstOrDefault(device => Key(device) == key) ?? Default(devices, ownProfileId);
}
