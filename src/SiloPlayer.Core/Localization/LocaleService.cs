using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Localization;

public sealed class AppLocaleChangedEventArgs(AppLocale previous, AppLocale current) : EventArgs
{
    public AppLocale Previous { get; } = previous;
    public AppLocale Current { get; } = current;
    public bool LanguageChanged => Previous.Language != Current.Language;
    public bool DirectionChanged => Previous.IsRightToLeft != Current.IsRightToLeft;
}

/// <summary>
/// Owns the applied interface locale. <see cref="Create"/> loads catalogs (do
/// it off the UI thread); <see cref="Apply"/> swaps the immutable snapshot and
/// raises <see cref="Changed"/> on the calling thread, so the native shell
/// applies on its UI thread and every retained view refreshes from there.
/// A subscriber that fails is logged and does not stop the others.
/// </summary>
public sealed class LocaleService
{
    private static readonly AppLocaleResolution EnglishResolution =
        new("en", null, null, System.Globalization.CultureInfo.CurrentCulture.Name, false, false);
    private readonly object _gate = new();
    private long _generation;
    private AppLocale _current;

    public LocaleService()
    {
        _current = new AppLocale(EnglishResolution, TryLoad("en"), TryLoad("en"), 0);
    }

    /// <summary>The process's locale service; <see cref="AppStrings"/> reads from it.</summary>
    public static LocaleService Shared { get; } = new();

    public AppLocale Current => Volatile.Read(ref _current);

    public event EventHandler<AppLocaleChangedEventArgs>? Changed;

    /// <summary>
    /// Builds a locale for a resolution. A language whose catalog cannot load
    /// renders as English: English text, plural rules and direction.
    /// </summary>
    public AppLocale Create(AppLocaleResolution resolution)
    {
        var english = TryLoad("en");
        var catalog = resolution.Language == "en" ? english : TryLoad(resolution.Language);
        if (catalog == null)
        {
            resolution = resolution with { Language = "en", IsRightToLeft = false, LocalizedFormats = false };
            catalog = english;
        }
        return new AppLocale(resolution, catalog, english, Interlocked.Increment(ref _generation));
    }

    /// <summary>
    /// Applies <paramref name="next"/> unless it is older than the current
    /// locale (a late result from a superseded resolution) or presents
    /// identically. Returns whether the locale changed.
    /// </summary>
    public bool Apply(AppLocale next)
    {
        AppLocale previous;
        lock (_gate)
        {
            previous = _current;
            if (next.Generation < previous.Generation) return false;
            if (SamePresentation(previous, next))
            {
                // Keep the newer resolution (its stored tag) without a refresh.
                Volatile.Write(ref _current, next);
                return false;
            }
            Volatile.Write(ref _current, next);
        }
        if (Changed is { } listeners)
        {
            var change = new AppLocaleChangedEventArgs(previous, next);
            foreach (EventHandler<AppLocaleChangedEventArgs> listener in listeners.GetInvocationList())
                try { listener(this, change); }
                catch (Exception ex) { LocalLog.AppendException("settings_refresh.txt", ex, "app_locale_changed"); }
        }
        return true;
    }

    private static bool SamePresentation(AppLocale a, AppLocale b)
        => a.Language == b.Language && a.IsRightToLeft == b.IsRightToLeft && a.LocalizedFormats == b.LocalizedFormats
           && a.FormatCulture.Name == b.FormatCulture.Name;

    private static MessageCatalog? TryLoad(string language)
    {
        try { return MessageCatalog.Load(language); }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or System.Text.Json.JsonException
                                       or FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
