using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Official English copy with normal translation lookup when the pinned key is bundled.</summary>
public static class AdminPreparedFilesText
{
    public static string T(string key, string fallback)
    {
        var value = AppStrings.Get(key);
        return value == key ? fallback : value;
    }

    public static string Tab(string tab) => tab switch
    {
        "storage" => T("web.admin.downloads.tabs.storage", "Storage"),
        "preparation" => T("web.admin.downloads.tabs.preparation", "Preparation"),
        "files" => T("web.admin.downloads.tabs.files", "Prepared files"),
        "devices" => T("web.admin.downloads.tabs.devices", "Device copies"),
        "history" => T("web.admin.downloads.tabs.history", "History"),
        _ => T("web.admin.downloads.tabs.storage", "Storage"),
    };
}
