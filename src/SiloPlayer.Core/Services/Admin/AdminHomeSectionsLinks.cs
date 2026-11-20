using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A collection the link's <c>?add=collection:library:&lt;id&gt;</c> names.</summary>
public sealed record AdminHomeSectionsLinkCollection(string Source, string Id);

/// <summary>What the Home sections page was linked with (217ff25 web/src/lib/homeRows/rowLinks.ts).</summary>
public sealed record AdminHomeSectionsLinks(AdminHomeSectionsLinkCollection? Add, bool AddInvalid, string? Edit, string? ReturnTo)
{
    public static readonly string[] ParameterNames = ["add", "edit", "return"];
    private static readonly Regex AddPattern = new("^collection:(library|user):(.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly string[] ReturnPrefixes = ["/admin/collections/", "/collections/"];

    /// <summary>The link parameters in <paramref name="query"/>, or null when there are none.</summary>
    public static AdminHomeSectionsLinks? Read(IReadOnlyDictionary<string, string>? query)
    {
        if (query == null || !ParameterNames.Any(query.ContainsKey)) return null;
        string? Get(string name) => query.TryGetValue(name, out var value) ? value : null;
        var raw = Get("add"); AdminHomeSectionsLinkCollection? add = null; var invalid = false;
        if (raw != null)
        {
            var match = AddPattern.Match(raw);
            // Admin rows show server collections only.
            if (!match.Success || match.Groups[1].Value != "library") invalid = true;
            else add = new(match.Groups[1].Value, match.Groups[2].Value);
        }
        var edit = Get("edit");
        return new(add, invalid, string.IsNullOrEmpty(edit) ? null : edit, SafeReturnPath(Get("return")));
    }

    private static bool Unsafe(string path) => path.Contains('\\') || path.Contains("//", StringComparison.Ordinal) || path.Any(char.IsControl) ||
        path.Split('/', '?', '#').Any(segment => segment is "." or "..");

    /// <summary>
    /// <c>?return=</c> only ever leads back to a collection page on this site: it must start with a collection prefix, and neither
    /// it nor its decoded form may hold a backslash, <c>//</c>, a control character or a <c>.</c>/<c>..</c> segment.
    /// </summary>
    public static string? SafeReturnPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || !ReturnPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal))) return null;
        var decoded = StrictDecode(value);
        return decoded == null || Unsafe(value) || Unsafe(decoded) ? null : value;
    }

    /// <summary>decodeURIComponent: every percent sign must start two hex digits and the bytes must be valid UTF-8, else null.</summary>
    private static string? StrictDecode(string value)
    {
        var text = new System.Text.StringBuilder(); var bytes = new List<byte>();
        var strict = new System.Text.UTF8Encoding(false, true);
        bool Flush()
        {
            if (bytes.Count == 0) return true;
            try { text.Append(strict.GetString(bytes.ToArray())); bytes.Clear(); return true; }
            catch (System.Text.DecoderFallbackException) { return false; }
        }
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') { if (!Flush()) return null; text.Append(value[i]); continue; }
            if (i + 2 >= value.Length) return null;
            if (!Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) return null;
            bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16)); i += 2;
        }
        return Flush() ? text.ToString() : null;
    }

    /// <summary>The query a page keeps when the links were consumed: <c>page</c> and anything unrelated.</summary>
    public static Dictionary<string, string>? WithoutLinks(IReadOnlyDictionary<string, string>? query)
    {
        var kept = query?.Where(pair => !ParameterNames.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return kept is { Count: > 0 } ? kept : null;
    }

    /// <summary>Every link parameter's value, to notice a new link. Empty when there is none.</summary>
    public static string Key(IReadOnlyDictionary<string, string>? query) =>
        query == null || !ParameterNames.Any(query.ContainsKey) ? "" : string.Join('\n', ParameterNames.Select(name => query.TryGetValue(name, out var value) ? value : ""));
}
