namespace SiloPlayer.Core.Localization;

/// <summary>
/// A server-written title's optional catalog key (catalog-api.md "Section titles").
/// The title itself stays the stored value used for editing, saving and pins;
/// only display goes through the key. <see cref="KeyedTitle"/> is the title the
/// key arrived with, so a title renamed afterwards shows as typed.
/// </summary>
public interface IServerTitled
{
    string Title { get; }
    string? TitleKey { get; }
    Dictionary<string, string>? TitleArgs { get; }
    string? KeyedTitle { get; }
}

public static class SectionTitles
{
    /// <summary>The title to show: the key's translation while the title is still the one it was keyed with.</summary>
    public static string Display(string title, string? key, IReadOnlyDictionary<string, string>? arguments, string? keyedTitle)
        => !string.IsNullOrEmpty(key) && (keyedTitle == null || string.Equals(keyedTitle, title, StringComparison.Ordinal))
            ? AppStrings.ServerText(key, arguments, title)
            : title;

    public static string Display(IServerTitled titled) => Display(titled.Title, titled.TitleKey, titled.TitleArgs, titled.KeyedTitle);

    /// <summary>An empty key counts as none.</summary>
    public static string? Normalize(string? key) => string.IsNullOrEmpty(key) ? null : key;

    /// <summary>An independent copy of server title arguments, so a copied row never shares a mutable map.</summary>
    public static Dictionary<string, string>? CopyArguments(IReadOnlyDictionary<string, string>? arguments)
        => arguments == null ? null : new Dictionary<string, string>(arguments, StringComparer.Ordinal);

    public static bool SameArguments(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (a == null || b == null) return (a?.Count ?? 0) == 0 && (b?.Count ?? 0) == 0;
        return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }
}
