namespace SiloPlayer.Core.Services;

public static class MediaItemActionPolicy
{
    public static bool CanAddToPersonalLists(string type) => type is not ("episode" or "season");
    public static bool CanResetProgress(string type, double? positionSeconds, bool supported)
        => supported && (type is "movie" or "episode") && positionSeconds > 0;
}
