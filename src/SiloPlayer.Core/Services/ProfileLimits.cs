namespace SiloPlayer.Core.Services;

public static class ProfileLimits
{
    public static bool CanAdd(int count, int? maximum) => maximum is null or < 1 || count < maximum;
    public static bool CanSwitch(int count, int? maximum, bool hasPin = false) => hasPin || count > 1 || CanAdd(count, maximum);
    public static string Message(int maximum) => $"This account can have {maximum} {(maximum == 1 ? "profile" : "profiles")}. An administrator can raise the limit.";
}
