using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static partial class SettingsLocalizationNativeFixture
{
    private static async Task ReviewFollowups(StackPanel parent, SiloApiClient client)
    {
        var page = new SettingsPage(); parent.Children.Add(page);
        try
        {
            Apply("de-DE"); await Settle(page);
            Invoke(page, "UpdateDateTimePreview");
            var preview = (TextBlock)page.FindName("DateTimePreviewText");
            preview.Text = "stale regional preview";
            Apply("de-CH"); await Settle(page);
            Check(AppStrings.Language == "de" && preview.Text != "stale regional preview",
                "Settings did not refresh its date/time preview for a region-only change.");
        }
        finally { parent.Children.Remove(page); }

        await ReviewHistoryAndMissingStates(parent);

        var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = true }, () => true);
        var allowance = new AllowedTitlesSection(session, "secondary"); parent.Children.Add(allowance);
        try
        {
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(allowance);
                RequireText(allowance, "web.profiles.allowedTitles.heading", "Allowed titles");
                RequireText(allowance, "web.profiles.allowedTitles.collectionsHeading", "Allowed collections");
                Check(Field<TextBox>(allowance, "_search").PlaceholderText == AppStrings.Get("web.profiles.allowedTitles.searchPlaceholder"),
                    "Allowance search hint did not refresh.");
            }
        }
        finally { parent.Children.Remove(allowance); }

        var person = new EditPersonDialog(new() { Name = "Raw person", Bio = "Raw bio" }) { XamlRoot = parent.XamlRoot };
        var add = new AddToCollectionDialog("fixture-item", "Raw title") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(add, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(add);
                Check(add.PrimaryButtonText == AppStrings.Get("web.collections.manual.add"), "Add-to-collection primary action did not refresh.");
            }
        });
        var split = new SplitVersionsDialog(new() { ContentId = "fixture-split", Title = "Raw split title", Type = "movie" }, null) { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(split, async () =>
        {
            var titleInput = Field<TextBox>(split, "_title");
            titleInput.Text = "Unsaved split title";
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(split);
                foreach (var (field, placeholder, name) in new[] {
                    ("_title", "web.item.dialog.fieldTitle", "web.item.split.searchTitle"),
                    ("_year", "web.item.dialog.fieldYear", "web.item.split.searchYear"),
                    ("_tmdb", "web.item.dialog.tmdbId", "web.item.dialog.tmdbId"),
                    ("_imdb", "web.item.split.imdbPlaceholder", "web.item.dialog.imdbId"),
                    ("_tvdb", "web.item.dialog.tvdbId", "web.item.dialog.tvdbId") })
                {
                    var input = Field<TextBox>(split, field);
                    Check(input.PlaceholderText == AppStrings.Get(placeholder) &&
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(input) == AppStrings.Get(name),
                        $"Split field did not refresh: {field}; locale={AppStrings.Language}; loaded={input.IsLoaded}; placeholder={input.PlaceholderText}; expected={AppStrings.Get(placeholder)}; name={Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(input)}; expectedName={AppStrings.Get(name)}");
                }
                Check(titleInput.Text == "Unsaved split title", "Split locale refresh discarded a draft.");
            }
        });
        await OpenReviewDialog(person, async () =>
        {
            var inputs = Field<Dictionary<string, TextBox>>(person, "_inputs");
            inputs["name"].Text = "Unsaved person";
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(person);
                Check(Descendants((DependencyObject)person.Content).OfType<TextBlock>()
                    .Any(label => label.Text == AppStrings.Get("web.item.editPerson.name")), "Person name label did not refresh.");
                RequireText((DependencyObject)person.Content, "web.item.editPerson.birthDate", "Birth Date");
                Check(inputs["name"].Text == "Unsaved person" && inputs["bio"].Text == "Raw bio", "Person editor lost a draft.");
            }
        });
        var refresh = new RefreshMetadataDialog(_ => Task.CompletedTask) { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(refresh, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(refresh);
                RequireText((DependencyObject)refresh.Title, "web.item.dialog.refreshMetadataTitle", "Refresh Metadata");
                RequireText((DependencyObject)refresh.Content, "web.item.refresh.quickTitle", "Quick Refresh");
                RequireText((DependencyObject)refresh.Content, "web.item.refresh.completeBody", "Clear the current match");
                Check(Equals(Field<Button>(refresh, "_cancelButton").Content, AppStrings.Get("common.cancel")), "Refresh Cancel did not update.");
            }
        });
        var search = new GlobalSearchDialog { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(search, async () =>
        {
            await Field<Task>(search, "_scopeLoadTask");
            foreach (var enabled in new[] { true, false })
            {
                typeof(GlobalSearchDialog).GetField("_requestDiscoveryEnabled", Private)!.SetValue(search, enabled);
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(search);
                    Check(((TextBox)search.FindName("SearchBox")).PlaceholderText == AppStrings.Get(enabled
                        ? "web.search.placeholderRequest" : "web.search.placeholderLibrary"), "Search capability hint did not survive locale refresh.");
                }
            }
        });
        foreach (var playerMode in new[] { false, true })
        {
            var subtitles = new SubtitleSearchDialog(42, "en", playerMode) { XamlRoot = parent.XamlRoot };
            await OpenReviewDialog(subtitles, async () =>
            {
                await Until(() => Field<bool>(subtitles, "_providerStatusLoaded"));
                Invoke(subtitles, "SearchButton_Click", subtitles, new RoutedEventArgs());
                var empty = (TextBlock)subtitles.FindName("EmptyStateText");
                await Until(() => empty.Visibility == Visibility.Visible);
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(subtitles);
                    Check(empty.Text == AppStrings.Get(playerMode ? "web.detail.subtitles.noneAvailable" : "web.detail.subtitleSearch.noResults"), "Subtitle empty results did not refresh in playerMode=" + playerMode);
                }
            });
        }
        await ReviewSubtitleColorsAndWatchlistFailure(parent);
        await ReviewVisibleSubtitleResults(parent);
        Program.Log("PASS review followups: region-only Settings refresh, allowed titles, retained person/refresh dialogs, capability search hints and real empty subtitle search; drafts preserved.");
    }

    private static async Task ToggleRows(StackPanel parent)
    {
        Apply("en");
        var page = new SettingsPage(); parent.Children.Add(page);
        try
        {
            foreach (var (section, keys) in new (string, string[])[]
            {
                ("TitlePages", ["settingsManifest.ui.theme_music_enabled.label", "settingsManifest.ui.theme_music_loop.label",
                    "settingsManifest.catalog.hide_unwatched_episode_images.label", "settingsManifest.catalog.hide_unwatched_episode_overviews.label"]),
                ("Playback", ["settingsManifest.playback.show_marker_confidence.label", "settingsManifest.playback.show_marker_source.label"]),
            })
            {
                page.SelectSettingsSection(section); await Settle(page);
                var rows = LogicalToggleDescendants((DependencyObject)page.FindName(section + "Panel"))
                    .OfType<Grid>().Where(row => Equals(row.Tag, "settings-value-row")).ToArray();
                foreach (var language in new[] { "en", "de", "ar", "ja" })
                {
                    Apply(language); await Settle(page);
                    foreach (var toggle in rows.SelectMany(row => row.Children.OfType<ToggleSwitch>()))
                        Check(toggle.IsLoaded && toggle.Header is null && Localized.GetHeader(toggle) is null,
                            "Converted Settings toggle restored its header after Loaded or locale refresh.");
                    foreach (var key in keys)
                    {
                        var row = rows.Single(candidate => candidate.Children.OfType<TextBlock>()
                            .Any(label => Localized.GetText(label) == key));
                        var label = row.Children.OfType<TextBlock>().Single();
                        var toggle = row.Children.OfType<ToggleSwitch>().Single();
                        var expected = AppStrings.Get(key);
                        Check(expected != key && label.Text == expected, "Converted Settings row label did not follow " + language + ": " + key);
                        Check(Localized.GetName(toggle) == key && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(toggle) == expected,
                            "Converted Settings toggle accessible name did not follow " + language + ": " + key);
                    }
                }
            }
        }
        finally { parent.Children.Remove(page); Apply("en"); await Task.Delay(80); }
        Program.Log("PASS converted Settings toggles keep null headers and localize their row labels and accessible names after Loaded and locale changes.");
    }

    private static IEnumerable<DependencyObject> LogicalToggleDescendants(DependencyObject owner)
    {
        IEnumerable<DependencyObject> children = owner switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => [child],
            ContentControl { Content: DependencyObject child } => [child],
            _ => [],
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in LogicalToggleDescendants(child)) yield return descendant;
        }
    }

    private static async Task ReviewVisibleSubtitleResults(StackPanel parent)
    {
        var dialog = new SubtitleSearchDialog(42, "en", playerMode: false) { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(dialog, async () =>
        {
            var result = new SubtitleSearchResult { Provider = "subdl", SubtitleId = "fixture", Language = "de", Format = "srt",
                Score = 87, Downloads = 1234, HearingImpaired = true, ReleaseName = "Raw release\nRaw alternate" };
            var row = (FrameworkElement)typeof(SubtitleSearchDialog).GetMethod("BuildResultRow", Private)!.Invoke(dialog, [result])!;
            var results = (ListView)dialog.FindName("ResultsList"); results.Items.Add(row); await Settle(dialog);
            var buttons = Descendants(row).OfType<Button>().ToArray();
            var expand = buttons.Single(button => button.Content is string);
            Click(expand); await Settle(dialog);
            var download = buttons.Single(button => button.Content is StackPanel);
            download.IsEnabled = false; results.IsEnabled = false;
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(dialog);
                Check(ReferenceEquals(results.Items[0], row) && !download.IsEnabled && !results.IsEnabled, "Subtitle refresh replaced a row or cleared busy state.");
                var text = Descendants(row).OfType<TextBlock>().Select(label => label.Text).ToArray();
                foreach (var expected in new[] { AppStrings.Get("subtitleSearch.hearingImpaired"), PlayerService.LanguageCodeToName("de"), AppStrings.Get("web.detail.actions.download"),
                    AppStrings.Get("web.detail.subtitleSearch.downloadCount", new Dictionary<string, object?> { ["count"] = 1234, ["count_text"] = 1234.ToString("N0", AppStrings.FormatCulture) }), "Raw release", "Raw alternate" })
                    Check(text.Contains(expected), "Retained subtitle result did not refresh/preserve: " + expected);
                Check(Equals(expand.Content, AppStrings.Get("web.detail.subtitleSearch.collapse")), "Subtitle variants lost expansion state.");
                Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(download) == AppStrings.Get("web.detail.actions.download") + " Raw release", "Subtitle download accessible name did not refresh.");
            }
        });
        Program.Log("PASS retained subtitle result labels, metadata, expansion and disabled download state through de/ar/ja.");
    }

    private static async Task ReviewSubtitleColorsAndWatchlistFailure(StackPanel parent)
    {
        var appearance = new SubtitleAppearanceDialog { XamlRoot = parent.XamlRoot, ApplyToLocalPlayer = false,
            InitialValue = "{}", SaveOverrideAsync = _ => Task.CompletedTask };
        await OpenReviewDialog(appearance, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(appearance);
                foreach (var (hostName, prefix, keys) in new[] {
                    ("FontColorSwatchHost", "web.settings.subtitleOptions.fontColor.", new[] { "white", "yellow", "green", "cyan", "magenta", "red", "blue", "gray", "black" }),
                    ("BgColorSwatchHost", "web.settings.subtitleOptions.backgroundColor.", new[] { "black", "darkGray", "navy", "darkRed", "darkGreen" }) })
                {
                    var buttons = ((Panel)appearance.FindName(hostName)).Children.OfType<Button>().ToArray();
                    Check(buttons.Length == keys.Length, "Subtitle palette coverage changed.");
                    for (var index = 0; index < buttons.Length; index++)
                    {
                        var expected = AppStrings.Get(prefix + keys[index]);
                        Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(buttons[index]) == expected,
                            "Subtitle swatch accessible name did not translate: " + keys[index]);
                        Check(Equals(ToolTipService.GetToolTip(buttons[index]), expected), "Subtitle swatch tooltip did not translate: " + keys[index]);
                    }
                }
            }
        });
        using var wire = new ReviewWatchlistFailureWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://watchlist-failure.fixture.invalid");
        var model = new SiloPlayer.ViewModels.WatchlistViewModel(new CatalogApi(client), new RequestsApi(client));
        await model.LoadExternalTitlesAsync();
        foreach (var language in new[] { "de", "ar", "ja" })
        {
            Apply(language); Invoke(model, "RefreshLocalization");
            Check(model.ExternalTitlesError == AppStrings.Get("web.library.watchlist.loadFailed") + " " + AppStrings.Get("error.dependency_unavailable"),
                "Retained external-watchlist error prefix/problem did not translate: " + model.ExternalTitlesError);
        }
        Check(wire.Reads == 1, "Watchlist error refresh reloaded the server.");
        Program.Log("PASS subtitle palettes and retained watchlist failure prefix/problem through de/ar/ja.");
    }

    private sealed class ReviewWatchlistFailureWire : HttpMessageHandler
    {
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "watchlist-failure.fixture.invalid", "External watchlist failure fixture request blocked.");
            Reads++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("""{"type":"https://silo.invalid/docs/api/v2/problems/dependency_unavailable","detail":"Raw service failure"}""", System.Text.Encoding.UTF8, "application/problem+json") });
        }
    }

    private static async Task ReviewHistoryAndMissingStates(StackPanel parent)
    {
        var history = new HistoryPage();
        var browse = new RequestBrowsePage();
        var watchlist = new WatchlistPage();
        parent.Children.Add(browse); parent.Children.Add(watchlist);
        try
        {
            await Settle(watchlist);
            ((ComboBox)watchlist.FindName("WatchlistScope")).SelectedIndex = 1;
            await (Task)typeof(WatchlistPage).GetMethod("LoadExternalAsync", Private)!.Invoke(watchlist, null)!;
            Check(!watchlist.ViewModel.ExternalTitlesSupported && watchlist.ViewModel.ExternalTitlesError == null,
                "Fixture did not exercise unsupported external watchlist capabilities.");
            var browseState = Field<RequestBrowseSession>(browse, "_browse");
            await browseState.ResetAsync((_, _) => throw new ApiException("not_found", "Raw missing category", 404));
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(browse); await Settle(watchlist);
                Check(((TextBlock)browse.FindName("LoadingText")).Text == AppStrings.Get("web.requests.discoverSection.notFound"), "Missing browse category did not refresh.");
                Check(((TextBlock)watchlist.FindName("ExternalStatusText")).Text == AppStrings.Get("error.capability_unsupported"), "Unsupported watchlist message did not refresh.");
                foreach (var completed in new[] { true, false })
                {
                    var card = (Border)typeof(HistoryPage).GetMethod("CreateHistoryCard", Private)!.Invoke(history,
                        [new SiloPlayer.ViewModels.HistoryDisplayItem { ContentId = "fixture-history", Title = "Raw history title", Type = "movie", Completed = completed }])!;
                    parent.Children.Add(card);
                    try
                    {
                        await Settle(card);
                        Check(Descendants(card).OfType<TextBlock>().Any(text => text.Text == AppStrings.Get(completed ? "web.detail.actions.play" : "web.detail.actions.resume")), "History primary action did not translate.");
                        Check(Descendants(card).OfType<TextBlock>().Any(text => text.Text == "Raw history title"), "History title data changed.");
                    }
                    finally { parent.Children.Remove(card); }
                }
            }
        }
        finally { parent.Children.Remove(browse); parent.Children.Remove(watchlist); }
        var dialog = new PlayHistoryDialog("fixture-history") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(dialog, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(dialog);
                Check(Equals(dialog.Title, AppStrings.Get("web.item.menu.viewPlayHistory")), "Open play-history title did not refresh.");
            }
        });
        Program.Log("PASS review states: actual history actions, 404 browse, unsupported watchlist and open play-history dialog in de/ar/ja.");
    }

    private static async Task OpenReviewDialog(ContentDialog dialog, Func<Task> verify)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Opened += (_, _) => opened.TrySetResult();
        var showing = dialog.ShowAsync();
        try { await opened.Task.WaitAsync(TimeSpan.FromSeconds(8)); await verify(); }
        finally { dialog.Hide(); await showing; }
    }
}
