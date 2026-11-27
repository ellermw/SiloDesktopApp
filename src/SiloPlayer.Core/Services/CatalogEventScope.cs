using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The library a realtime <c>catalog</c> event belongs to. The server sends v2 string ids
/// ("12"); an older node sends the number itself. Anything else is unscoped, as in the
/// WebUI's catalogEventLibraryID.
/// </summary>
public static class CatalogEventScope
{
    public static int? LibraryId(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("library_id", out var value)) return null;
        int id;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetInt32(out id)) return null;
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            string? text;
            try { text = value.GetString(); }
            // JsonDocument can retain an escaped lone surrogate that GetString cannot decode.
            catch (InvalidOperationException) { return null; }
            if (string.IsNullOrEmpty(text) || text[0] is < '1' or > '9') return null;
            // Integer parsing alone ignores trailing NULs even with NumberStyles.None.
            foreach (var digit in text)
                if (digit is < '0' or > '9') return null;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return null;
        }
        else return null;
        return id > 0 ? id : null;
    }

    /// <summary>An unscoped event refreshes every library; a scoped one only its own.</summary>
    public static bool Refreshes(int? eventLibraryId, int openLibraryId) =>
        eventLibraryId is not { } scoped || scoped == openLibraryId;
}
