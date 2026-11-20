using System.Reflection;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One run of rich text: the words and the tags (<c>b</c>, <c>code</c>, <c>hint</c>, ...) that wrap them.</summary>
public sealed record AdminPreparedRichRun(string Text, IReadOnlyList<string> Tags)
{
    public bool Has(string tag) => Tags.Contains(tag);
}

/// <summary>
/// Message text for Manage &gt; Downloads &gt; Prepared Files. The English patterns are the pinned official
/// <c>web.admin.downloads.*</c> catalog (<see cref="AdminPreparedFilesTextCatalog"/>, Silo Server 66b836f82);
/// keys the shared catalog owns (<c>common.*</c>, <c>web.common.*</c>) go through <see cref="AppStrings"/>.
/// </summary>
public static class AdminPreparedFilesText
{
    /// <summary>
    /// Desktop-only wording for states the WebUI leaves to the browser (an unknown write outcome, a blocked retry, a failed
    /// read that keeps stale data on screen). These keys are not part of the pinned WebUI catalog.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NativeEnglish = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["configure.native.preparedFiles.adminRequired"] = "Administrator access is required to manage prepared files.",
        ["configure.native.preparedFiles.backgroundFailed"] = "The latest refresh failed. The last data received is still shown.",
        ["configure.native.preparedFiles.blocked"] = "A change may or may not have been applied. Reload to read the current state before deleting or changing anything else.",
        ["configure.native.preparedFiles.dismiss"] = "Dismiss",
        ["configure.native.preparedFiles.entriesLoadError"] = "This device's downloads could not be read. Reload and try again.",
        ["configure.native.preparedFiles.listTruncated"] = "Showing the first {count} rows. Narrow the filters to see the rest.",
        ["configure.native.preparedFiles.linkUnavailable"] = "This page is not available in this build.",
        ["configure.native.preparedFiles.nodeStale"] = "This node changed since it was read. Reload the node and review it before saving again.",
        ["configure.native.preparedFiles.reload"] = "Reload",
        ["configure.native.preparedFiles.unknownOutcome"] = "The server did not confirm the result. The lists are being read again; check them before trying again.",
    };

    /// <summary>Text for a catalog key, filled from an anonymous object or dictionary of values.</summary>
    public static string T(string key, object? values = null)
    {
        var dictionary = ToDictionary(values);
        if (NativeEnglish.TryGetValue(key, out var native)) return AdminText.Format(native, dictionary);
        if (AdminPreparedFilesTextCatalog.English.TryGetValue(key, out var pattern)) return AdminText.Format(pattern, dictionary);
        if (AdminText.Has(key)) return AdminText.T(key, dictionary);
        return AppStrings.Get(key, dictionary, fallback: key);
    }

    /// <summary>True when the page owns the key (used by the catalog-coverage tests).</summary>
    public static bool Owns(string key) => AdminPreparedFilesTextCatalog.English.ContainsKey(key) || NativeEnglish.ContainsKey(key);

    public static IReadOnlyCollection<string> Keys => AdminPreparedFilesTextCatalog.English.Keys.Concat(NativeEnglish.Keys).ToArray();

    /// <summary>Plural values: <c>count</c> selects the form, <c>count_text</c> prints the number.</summary>
    public static IReadOnlyDictionary<string, object?> Count(long count, string? text = null) => new Dictionary<string, object?>
    {
        ["count"] = count,
        ["count_text"] = text ?? count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Text with the rich tags of a pattern split out, for the page to draw as bold, code or muted runs.</summary>
    public static IReadOnlyList<AdminPreparedRichRun> Rich(string key, object? values = null) => ParseRich(T(key, values));

    public static IReadOnlyList<AdminPreparedRichRun> ParseRich(string text)
    {
        var runs = new List<AdminPreparedRichRun>();
        var stack = new List<string>();
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf('<', position);
            if (open < 0) { Add(text[position..]); break; }
            if (open > position) Add(text[position..open]);
            var close = text.IndexOf('>', open);
            if (close < 0) { Add(text[open..]); break; }
            var tag = text[(open + 1)..close];
            if (tag.StartsWith('/'))
            {
                var name = tag[1..];
                var index = stack.LastIndexOf(name);
                if (index >= 0) stack.RemoveAt(index);
                else Add(text[open..(close + 1)]);
            }
            else if (tag.Length > 0 && tag.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) stack.Add(tag);
            else Add(text[open..(close + 1)]);
            position = close + 1;
        }
        return runs;

        void Add(string value)
        {
            if (value.Length > 0) runs.Add(new(value, stack.ToArray()));
        }
    }

    /// <summary>The words of a rich pattern with the tags removed.</summary>
    public static string Plain(string key, object? values = null) => string.Concat(Rich(key, values).Select(x => x.Text));

    public static string Tab(string tab) => tab switch
    {
        "preparation" => T("web.admin.downloads.tabs.preparation"),
        "files" => T("web.admin.downloads.tabs.files"),
        "devices" => T("web.admin.downloads.tabs.devices"),
        "history" => T("web.admin.downloads.tabs.history"),
        _ => T("web.admin.downloads.tabs.storage"),
    };

    private static IReadOnlyDictionary<string, object?> ToDictionary(object? values)
    {
        if (values is null) return new Dictionary<string, object?>();
        if (values is IReadOnlyDictionary<string, object?> ready) return ready;
        if (values is IDictionary<string, object?> dictionary) return new Dictionary<string, object?>(dictionary);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            result[property.Name] = property.GetValue(values);
        return result;
    }
}
