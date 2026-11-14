using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

/// <summary>
/// The collection editor's display text in the interface language. Names, descriptions, links, sort and schedule
/// values stay exactly as typed or saved; a generated chart name is always English because it is saved. A language
/// change rewords the retained editor without reading anything again.
/// </summary>
public sealed partial class CollectionEditorPage
{
    private TextBlock? _descriptionLabel;
    private SiloPlayer.Core.Localization.AppLocale? _wordedLocale;
    private IDisposable? _localization;

    /// <summary>Sort labels by field. The default-sort rows are rebuilt from captured English, so they are relabelled here.</summary>
    private static readonly IReadOnlyDictionary<string, string> EditorSortKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [""] = "web.collections.fields.listOrder",
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
        ["progress"] = "web.common.sort.progress",
        ["last_watched_at"] = "web.common.sort.dateViewed",
        ["play_count"] = "web.common.sort.plays",
        ["author"] = "catalog.sort.author",
        ["narrator"] = "catalog.sort.narrator",
        ["series"] = "catalog.sort.series",
    };

    /// <summary>TMDB chart presets: catalog key suffix and the English name a new chart collection is saved under.</summary>
    private static readonly IReadOnlyDictionary<string, string> ChartKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["trending"] = "Trending", ["popular"] = "Popular", ["top_rated"] = "TopRated", ["now_playing"] = "NowPlaying",
        ["upcoming"] = "Upcoming", ["airing_today"] = "AiringToday", ["on_the_air"] = "OnTheAir",
    };

    /// <summary>
    /// The English chart name a generated collection name starts with. It is saved data, so it never follows the
    /// interface language (the shown chart cards are translated separately).
    /// </summary>
    internal static string ChartNameEnglish(string? preset)
        => preset != null && ChartKeys.TryGetValue(preset, out var key)
            ? AppStrings.English("web.collections.tmdbSources.chart" + key, fallback: preset)
            : preset ?? "";

    private void AttachLocalization()
    {
        // Accessible names are keyed here: an attached "Name" property cannot be set from XAML (it collides with the Name directive).
        Localized.SetName(BackButton, "web.shell.pageBack.goBack");
        Localized.SetName(NotFoundBackButton, "web.shell.pageBack.goBack");
        Localized.SetName(ImportedPosterPreview, "web.collections.copy.slotPoster");
        // Rows of a closed combo box never load, so the boxes carry their rows' keys (by wire tag).
        CollectionText.KeyItems(WatchFilterCombo, tag => tag is "all" or "unwatched" or "watched" ? "web.collections.displayFilters.watch." + tag : null);
        CollectionText.KeyItems(MediaFilterCombo, tag => tag switch { "all" => "web.collections.displayFilters.media.all", "movie" => "web.collections.displayFilters.media.movie", "series" => "web.collections.displayFilters.media.series", _ => null });
        // The editor is worded in this language as it is built; only a later language re-words it.
        _wordedLocale = SiloPlayer.Core.Localization.AppStrings.Current;
        _localization ??= AppLocalization.Register(this, () =>
        {
            if (ReferenceEquals(_wordedLocale, SiloPlayer.Core.Localization.AppStrings.Current)) return;
            _wordedLocale = SiloPlayer.Core.Localization.AppStrings.Current;
            // Nothing is built until the editor has configured its current layout.
            if (_wherePanel.Child == null) return;
            _lastHeaderState = null; _lastLookState = null;
            UpdateCurrentEditor();
            UpdateSmartLibraryLabel();
            BuildPreviewItemsUI();
            BuildManualItemsUI();
            RenderUsageRows();
            RebuildSyncedChart();
            if (_newSynced) RenderSyncedSource();
            // Rebuilding the saved list's section re-parents shared controls; like a reload, it runs after the current layout pass.
            else if (ViewModel.IsImportedCollection && ViewModel.LoadedCollection != null)
                DispatcherQueue.TryEnqueue(() => { if (_editorActive && ViewModel.IsImportedCollection && ViewModel.LoadedCollection != null) BuildSavedSyncedContents(false); });
            if (_syncedOrderGrid != null) RefreshImportedSortControls();
            UpdateSyncedMatchLabel();
            AutomationLabels();
        });
    }

    private void AutomationLabels()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ImportedRemovePosterButton,
            CollectionText.Get("web.collections.fields.removeArtwork", "Remove poster", ("slot", "poster")));
    }

    private string SourceTitles() => CollectionText.Titles(ViewModel.SourceItemCount);

    private static string KeptFieldsEnglish(string key) => key switch
    {
        "web.collections.copy.keptNameAndDescription" => "Kept your name and description",
        "web.collections.copy.keptName" => "Kept your name",
        _ => "Kept your description",
    };

    /// <summary>The catalog's sentence stops before the native hint to go back, which stays English.</summary>
    private static string SyncedCheckFailedText()
        => CollectionText.Get("web.collections.copy.syncedCheckFailed", "Couldn't check whether Synced lists are on.") + " Return to collections and try again.";

    /// <summary>What a blank max and a profile's own sort mean. Two catalog sentences, reworded on a language change.</summary>
    private TextBlock SyncedOrderHelpText()
    {
        var help = new TextBlock { FontSize = 13, LineHeight = 19.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CurrentBrush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap };
        void Word() => help.Text = CollectionText.Get("web.collections.copy.syncedOrderHelp", "Blank takes up to 10,000 titles from an MDBList, or the first 20 from a TMDB chart.")
            + " " + CollectionText.Get("web.collections.copy.orderHelp", "A profile that picks its own sort while browsing keeps that choice.");
        Word();
        AppLocalization.Register(help, Word);
        return help;
    }

    /// <summary>Relabels the default-sort rows by their wire value, keeping the selection and any unknown saved sort.</summary>
    private void LocalizeImportedSortChoices()
    {
        foreach (var item in ImportedDefaultSortCombo.Items.OfType<ComboBoxItem>().ToArray())
        {
            var tag = item.Tag as string ?? "";
            if (!EditorSortKeys.TryGetValue(tag.Split(':', 2)[0], out var key)) continue;
            var english = _importedSortChoices.FirstOrDefault(choice => choice.Value == tag)?.Label as string ?? (tag.Length == 0 ? "List order" : null);
            if (english == null) continue;
            CollectionText.Relabel(ImportedDefaultSortCombo, item, CollectionText.Get(key, english));
        }
    }
}
