using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// The collection page's display text in the interface language. Sort and filter choices keep their wire tags; the
/// collection's title, description and owner name stay as the server sent them. A language change refreshes the
/// retained hero, filter bar and More actions menu.
/// </summary>
public sealed partial class CollectionBrowsePage
{
    private IDisposable? _localization;

    /// <summary>Sort labels by wire value. The combo's rows are rebuilt from captured English, so they are relabelled here.</summary>
    private static readonly IReadOnlyDictionary<string, string> SortKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [""] = "web.catalog.sort.collectionOrder",
        ["title"] = "catalog.sort.title",
        ["added_at"] = "catalog.sort.added",
        ["release_date"] = "catalog.sort.released",
        ["last_air_date"] = "web.common.sort.latestEpisodeAirDate",
        ["latest_episode_added"] = "web.common.sort.latestEpisodeAdded",
        ["year"] = "catalog.sort.year",
        ["content_rating"] = "web.common.sort.contentRating",
        ["runtime"] = "web.common.sort.duration",
        ["rating_imdb"] = "catalog.sort.imdbRating",
        ["rating_tmdb"] = "web.common.sort.tmdbRating",
        ["rating_rt_critic"] = "web.common.sort.rtCriticRating",
        ["rating_rt_audience"] = "web.common.sort.rtAudienceRating",
        ["resolution"] = "catalog.sort.resolution",
        ["bitrate"] = "web.common.sort.bitrate",
        ["author"] = "catalog.sort.author",
        ["narrator"] = "catalog.sort.narrator",
        ["series"] = "catalog.sort.series",
    };

    private void AttachLocalization()
    {
        // Accessible names are keyed here: an attached "Name" property cannot be set from XAML (it collides with the Name directive).
        Localized.SetName(CollectionShuffleButton, "web.catalog.page.shuffle");
        Localized.SetName(CollectionEditButton, "web.collections.pageActions.edit");
        Localized.SetName(CollectionAddToHomeButton, "web.collections.copy.addToMyHome");
        Localized.SetName(CollectionMoreButton, "web.collections.actions.moreActions");
        // Rows of a closed combo box never load, so the boxes carry their rows' keys (by wire tag).
        CollectionText.KeyItems(MediaScopeCombo, tag => tag switch
        {
            "" => "web.collections.rules.mediaAll", "video" => "web.collections.rules.mediaVideo", "movie" => "web.collections.rules.mediaMovies",
            "series" => "web.collections.rules.mediaSeries", "audiobook" => "web.collections.rules.mediaAudiobooks", "ebook" => "web.collections.rules.mediaEbooks",
            "manga" => "web.collections.rules.mediaManga", _ => null,
        });
        CollectionText.KeyItems(OrderCombo, tag => tag switch { "desc" => "web.catalog.sort.descending", "asc" => "web.catalog.sort.ascending", _ => null });
        _localization ??= AppLocalization.Register(this, () =>
        {
            LocalizeChrome();
            LocalizeSortChoices();
            if (_countKnown) UpdateCountDisplay();
            ApplyByline(_header);
            if (_hero != null) UpdateMoreMenu(_header.Access);
            if (_syncing) SetActionStatus(CollectionText.Get("web.collections.actions.syncing", "Syncing…"), error: false);
        });
    }

    private void LocalizeChrome()
    {
        // The eyebrow is set in capitals, as the WebUI's uppercase style does, in the interface language's own casing.
        var language = CultureInfo.GetCultureInfo(AppStrings.Language);
        CollectionEyebrow.Text = CollectionText.Get("collections.collection", "Collection").ToUpper(language);
        FiltersSheet.Title = CollectionText.Get("web.catalog.filters.heading", "Filters");
        UpdatePinButtonVisual();
    }

    private void LocalizeSortChoices()
    {
        foreach (var item in SortCombo.Items.OfType<ComboBoxItem>())
            if (item.Tag is string tag && SortKeys.TryGetValue(tag, out var key)
                && _sortChoices.FirstOrDefault(choice => choice.Value == tag)?.Label is string english)
                CollectionText.Relabel(SortCombo, item, CollectionText.Get(key, english));
    }

    /// <summary>"by Owner · Read-only" under someone else's collection, with the owner's name emphasised.</summary>
    private void ApplyByline(CollectionPageHeader header)
    {
        CollectionByline.Inlines.Clear();
        var owner = header.Access is { Kind: CollectionPageAccessKind.ReadOnly, OwnerName: { Length: > 0 } name } ? name : null;
        if (owner != null)
        {
            var text = CollectionText.Get("web.collections.pageActions.byline", $"by {owner} · Read-only", ("owner", owner));
            var at = text.IndexOf(owner, StringComparison.Ordinal);
            if (at < 0) CollectionByline.Inlines.Add(new Run { Text = text });
            else
            {
                if (at > 0) CollectionByline.Inlines.Add(new Run { Text = text[..at] });
                CollectionByline.Inlines.Add(new Run { Text = owner, FontWeight = FontWeights.Medium, Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
                if (at + owner.Length < text.Length) CollectionByline.Inlines.Add(new Run { Text = text[(at + owner.Length)..] });
            }
        }
        CollectionByline.Visibility = owner == null ? Visibility.Collapsed : Visibility.Visible;
    }
}
