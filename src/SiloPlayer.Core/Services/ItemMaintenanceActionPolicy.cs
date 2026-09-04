namespace SiloPlayer.Core.Services;

public readonly record struct ItemMaintenanceActions(
    bool CanMatch,
    bool CanRefreshMetadata);

/// <summary>
/// Defines the complete media-maintenance surface retained by the desktop
/// client. Server administration remains in the WebUI.
/// </summary>
public static class ItemMaintenanceActionPolicy
{
    public static ItemMaintenanceActions Resolve(bool canCurateMetadata, string? itemType)
    {
        if (!canCurateMetadata)
            return default;

        var normalizedType = itemType?.Trim().ToLowerInvariant();
        return new ItemMaintenanceActions(
            CanMatch: normalizedType is "movie" or "series",
            CanRefreshMetadata: true);
    }
}
