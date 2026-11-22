namespace SiloPlayer.Core.Localization;

/// <summary>
/// The label for a catalog item's kind (<c>movie</c>, <c>ebook</c>, ...), which the server sends as English data. Mirrors the
/// official WebUI's <c>itemTypeLabel</c>: a kind with a catalog label is shown in the interface language, and any other kind
/// is shown as the server sent it.
/// </summary>
public static class ItemTypeLabels
{
    /// <summary>The catalog key naming a kind, or null when the catalog has no label for it. Kinds are matched exactly, as the server spells them.</summary>
    public static string? KeyFor(string? type) => type switch
    {
        "movie" => "web.search.typeMovie",
        "series" => "web.search.typeSeries",
        "season" => "web.search.typeSeason",
        "episode" => "web.search.typeEpisode",
        "ebook" => "web.search.typeEbook",
        "audiobook" => "web.search.typeAudiobook",
        "manga" => "web.search.typeManga",
        _ => null,
    };

    /// <summary>The kind in the interface language, or the kind itself when the catalog has no label for it.</summary>
    public static string Label(string type) => KeyFor(type) is { } key ? AppStrings.Get(key) : type;
}
