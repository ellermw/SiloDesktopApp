using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Interface text for the viewer's Home rows that the generated catalog does not carry yet (official silo-server web.home.* messages up to
/// 3b2b32168). The interface catalog wins whenever it has the key; otherwise the official English fills in. Every other key falls through to the
/// page text the Sections admin page already pins (<see cref="AdminHomeSectionsText"/>), which holds the collection-row messages.
/// </summary>
public static class HomeRowText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // #4702: the tag that tells a profile which of its Home rows the server manages.
        ["web.home.rows.line.server"] = "Server",
        ["web.home.addRow.shows.serverNote"] = "Managed by this server. What it shows can\u2019t be changed here; add a row of your own to choose what it shows.",
    };

    public static string T(string key, object? values = null)
    {
        if (!English.TryGetValue(key, out var text)) return AdminHomeSectionsText.T(key, values);
        return AppStrings.Get(key, null, text);
    }
}
