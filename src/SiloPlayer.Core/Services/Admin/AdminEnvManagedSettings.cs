using System.Collections.ObjectModel;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Which settings the server's environment fixes, and the variable that sets each one: the sensitive-status
/// answer's <c>managed_by_env</c> list together with its <c>managed_by_env_vars</c> map (official
/// <c>useSettingsForm.envVar</c>). A key counts as managed when either names it, so a server that sends only
/// the list keeps working; its notes then carry no variable name.
/// </summary>
public sealed partial class AdminEnvManagedSettings
{
    /// <summary>The longest variable name shown; a longer name is treated as not named.</summary>
    public const int MaxNameLength = 128;

    // Every official variable name is a plain identifier (internal/config/env_settings.go). A name with any other character
    // (a control, a line or paragraph separator, a bidirectional override, a zero-width character, a space or a comma) is not
    // shown: it could reorder or hide text in the note, so the key is shown with the unnamed note instead.
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex NamePattern();

    public static AdminEnvManagedSettings None { get; } = new([], null);

    private readonly HashSet<string> _keys;
    private readonly Dictionary<string, string> _names;

    /// <param name="listed">The <c>managed_by_env</c> list.</param>
    /// <param name="names">The <c>managed_by_env_vars</c> map, or null from a server that does not send it.
    /// A key present in the map is managed even when its name is unusable.</param>
    public AdminEnvManagedSettings(IEnumerable<string> listed, IEnumerable<KeyValuePair<string, string?>>? names)
    {
        _keys = new(StringComparer.Ordinal);
        _names = new(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var key in listed) if (_keys.Add(key)) ordered.Add(key);
        foreach (var (key, raw) in names ?? [])
        {
            if (_keys.Add(key)) ordered.Add(key);
            if (Normalize(raw) is { } name) _names[key] = name;
        }
        Keys = Array.AsReadOnly(ordered.ToArray());
        Names = new ReadOnlyDictionary<string, string>(_names);
    }

    /// <summary>Every managed key, in the order the server listed them, then any only the map named.</summary>
    public IReadOnlyCollection<string> Keys { get; }
    /// <summary>The variable named for each key that has a usable name.</summary>
    public IReadOnlyDictionary<string, string> Names { get; }

    public bool IsManaged(string key) => _keys.Contains(key);

    /// <summary>
    /// The environment a page uses after a status read. A read that answered replaces the list; a read that failed keeps
    /// <paramref name="lastKnown"/> (the official <c>useSettingsForm</c> keeps the last good list on a refetch error), so a key that
    /// was managed stays locked and out of the save. A page opens with <see cref="None"/>, so nothing carries over from another
    /// server or account: opening a page starts a new state.
    /// </summary>
    public static AdminEnvManagedSettings Resolve(AdminSettingsSensitiveStatus? status, AdminEnvManagedSettings lastKnown) => status?.Environment ?? lastKnown;

    /// <summary>The variable that sets <paramref name="key"/>, or null when the key is not managed or the server did not name it.</summary>
    public string? VariableName(string key) => _names.GetValueOrDefault(key);

    /// <summary>
    /// The row note for a managed key, or null for an editable one: the official "Set by {env} in the server's
    /// environment." with the variable's name, or the official field message "Set by the environment. Change it there
    /// and restart the affected service." when the server named no variable.
    /// </summary>
    public string? Note(string key) => IsManaged(key) ? NoteFor(VariableName(key)) : null;

    public static string NoteFor(string? variableName) => string.IsNullOrWhiteSpace(variableName)
        ? AdminText.T("error.field.managed_by_environment")
        : AdminText.T("web.admin.settings.fields.managedByEnv", new { env = variableName });

    /// <summary>The same answer limited to the keys one page owns.</summary>
    public AdminEnvManagedSettings Restrict(IEnumerable<string> pageKeys)
    {
        var allowed = new HashSet<string>(pageKeys, StringComparer.Ordinal);
        return new(Keys.Where(allowed.Contains), _names.Where(entry => allowed.Contains(entry.Key)).Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)));
    }

    internal static string? Normalize(string? raw)
    {
        return raw != null && NamePattern().IsMatch(raw) ? raw : null;
    }

    public override string ToString() => nameof(AdminEnvManagedSettings);
}
