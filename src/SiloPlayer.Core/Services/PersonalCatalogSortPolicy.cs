namespace SiloPlayer.Core.Services;

/// <summary>
/// Defines the current WebUI sort presentation for profile-owned catalog
/// surfaces. Favorites and Watchlist default to their stored list order;
/// History remains a date-ordered activity surface.
/// </summary>
public static class PersonalCatalogSortPolicy
{
    public static bool SupportsSourceOrder(string? source) =>
        source is "favorites" or "watchlist";

    public static string DefaultSortLabel(string? source) =>
        SupportsSourceOrder(source) ? "List Order" : "Date Added";

    public static bool ShouldShowOrderSelector(string? source, string? sort) =>
        !SupportsSourceOrder(source) || !string.IsNullOrWhiteSpace(sort);
}
