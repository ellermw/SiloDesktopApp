using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// The wall's display text in the interface language. <see cref="CollectionsWall"/> keeps its English values for
/// routing and tests; here the pill values and wall states choose catalog messages, and library and collection names stay
/// exactly as the server sent them. A language change rebuilds the retained wall.
/// </summary>
public sealed partial class CollectionsPage
{
    private AppLocale? _wallLocale;
    private IDisposable? _localization;

    private void AttachLocalization()
    {
        // Accessible names are keyed here: an attached "Name" property cannot be set from XAML (it collides with the Name directive).
        Localized.SetName(CollectionsFilterPills, "web.collections.page.filterLabel");
        _localization ??= AppLocalization.Register(this, () =>
        {
            UpdateWallLoadAction();
            // The first Loaded builds the wall itself; only a later language rebuilds it here.
            if (_wallLocale != null && !ReferenceEquals(_wallLocale, AppStrings.Current)) BuildWall();
        });
    }

    private void MarkWallLocale() => _wallLocale = AppStrings.Current;

    private static string PillLabel(CollectionsWall.Pill pill) => pill.Value switch
    {
        CollectionsWall.All => CollectionText.Get("web.collections.page.filterAll", pill.Label),
        CollectionsWall.Mine => CollectionText.Get("web.collections.page.filterMine", pill.Label),
        CollectionsWall.Shared => CollectionText.Get("web.collections.page.filterShared", pill.Label),
        CollectionsWall.Server => CollectionText.Get("web.collections.page.filterServer", pill.Label),
        // A library pill is the library's own name.
        _ => pill.Label,
    };

    private static string WallLabel(string show, ServerCollectionsLibrary? picked)
    {
        var english = CollectionsWall.Label(show, picked);
        return show switch
        {
            CollectionsWall.Mine => CollectionText.Get("web.collections.page.wallMine", english),
            CollectionsWall.Shared => CollectionText.Get("web.collections.page.wallShared", english),
            CollectionsWall.Server => CollectionText.Get("web.collections.page.wallServer", english),
            _ when picked != null => CollectionText.Get("web.collections.page.wallLibrary", english, ("library", picked.LibraryName)),
            _ => CollectionText.Get("web.collections.page.wallAll", english),
        };
    }

    /// <summary>What stopping sharing costs the other profiles, naming them as their profiles are named.</summary>
    private static string UnshareConsequence(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return CollectionText.Get("web.collections.copy.unshareConsequenceOthers", "Other profiles lose it, including rows they made from it.");
        var who = JoinNames(names);
        return CollectionText.Get("web.collections.copy.unshareConsequence",
            $"{who} {(names.Count == 1 ? "loses" : "lose")} it, including rows they made from it.", ("count", names.Count), ("names", who));
    }

    /// <summary>
    /// Profile names as one phrase. English keeps "A, B and C"; the catalog has no list conjunction for other
    /// languages, so they are separated by commas instead of mixing in an English "and".
    /// </summary>
    internal static string JoinNames(IReadOnlyList<string> names)
        => names.Count < 2 ? string.Concat(names)
            : AppStrings.Current.IsEnglish ? string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
            : string.Join(", ", names);

    /// <summary>The two empty-wall sentences are <see cref="CollectionsWall"/>'s named constants, not free text.</summary>
    private static string EmptyText(string empty) => empty switch
    {
        CollectionsWall.EmptyMine => CollectionText.Get("web.collections.page.emptyMine", empty),
        CollectionsWall.EmptyAll => CollectionText.Get("web.collections.page.emptyAll", empty),
        _ => empty,
    };
}
