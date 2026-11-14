using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

internal static partial class SettingsLocalizationNativeFixture
{
    private static async Task ReviewNinth(StackPanel parent, Wire wire, SiloApiClient client)
    {
        var card = new LandscapeCard { Width = 340 };
        parent.Children.Add(card);
        try
        {
            foreach (var title in new[] { "", "   ", "Raw episode 東京" })
            {
                card.MediaItem = new MediaItem { ContentId = "missing-title", Type = "episode", SeriesTitle = "Raw series", SeasonNumber = 2, EpisodeNumber = 3, Title = title };
                foreach (var language in new[] { "en", "de", "ar", "ja" })
                {
                    Apply(language); await Settle(card);
                    var key = string.IsNullOrWhiteSpace(title) ? "web.home.continueWatching.episodeLabel" : "web.home.continueWatching.episodeLabelWithTitle";
                    var expected = AppStrings.Get(key, new Dictionary<string, object?> { ["season"] = 2, ["episode"] = 3, ["title"] = title });
                    Check(((TextBlock)card.FindName("SubtitleText")).Text == expected, "Episode context has a dangling separator or changed its raw title.");
                    Check(((TextBlock)card.FindName("TitleText")).Text == "Raw series", "Episode locale refresh changed the raw series name.");
                }
            }
        }
        finally { parent.Children.Remove(card); }
        foreach (var count in new[] { 0, 1, 2, 5, 11, 23 })
        {
            wire.RequestCount = count;
            var requests = new RequestsViewModel(new RequestsApi(client)); await requests.RefreshAsync();
            Check(requests.MyRequests.Count == count && requests.ErrorMessage == null, "Synthetic request load did not reach its successful count state.");
            foreach (var language in new[] { "en", "de", "ar", "ja" })
            {
                Apply(language); Invoke(requests, "RefreshLocalization");
                var expected = count == 0 ? "" : AppStrings.Get("web.catalog.page.resultCount", new Dictionary<string, object?> { ["count"] = count, ["shown"] = count.ToString("N0", AppStrings.FormatCulture) });
                Check(requests.StatusMessage == expected && !requests.StatusMessage.Contains("request(s)"), "Loaded Requests status lost catalog pluralization on refresh.");
                Check(requests.MyRequests.Count == count && requests.MyRequests.All(item => item.Title.StartsWith("Raw request ")), "Status refresh changed request rows.");
            }
        }
        wire.RequestCount = 0;
        var match = new MatchItemDialog("match-fixture") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(match, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(match);
                Check(((Grid)match.Title).Children.OfType<TextBlock>().Single().Text == AppStrings.Get("web.item.dialog.matchItemTitle"), "Open Match Item heading did not refresh.");
            }
        });
        var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = true }, () => true);
        var allowance = new AllowedTitlesSection(session, "secondary"); parent.Children.Add(allowance);
        try
        {
            wire.AllowedListsFail = true; await allowance.LoadAsync(); await Settle(allowance);
            var retryTitles = Field<Button>(allowance, "_titleRetry");
            var retryCollections = Field<Button>(allowance, "_collectionRetry");
            Check(retryTitles.Visibility == Visibility.Visible && retryCollections.Visibility == Visibility.Visible, "Allowance failure fixtures did not show both retry buttons.");
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(allowance);
                Check(Equals(retryTitles.Content, AppStrings.Get("common.tryAgain")) && Equals(retryCollections.Content, AppStrings.Get("common.tryAgain")), "Visible allowance retries did not translate.");
            }
            wire.AllowedListsFail = false; Click(retryTitles); Click(retryCollections);
            await Until(() => retryTitles.Visibility == Visibility.Collapsed && retryCollections.Visibility == Visibility.Collapsed);
        }
        finally { wire.AllowedListsFail = false; parent.Children.Remove(allowance); }
        var person = new PersonDetailViewModel(SiloPlayer.App.Services.GetRequiredService<PeopleApi>(), SiloPlayer.App.Services.GetRequiredService<CatalogApi>(), client);
        foreach (var birth in new string?[] { null, "", "invalid-birth", "2026-01-01", "1950-01-01" })
        foreach (var died in new[] { "2026-03-04", "raw-unknown-date" })
        {
            var source = new Person { Name = "Raw person", BirthDate = birth, DeathDate = died }; person.Person = source;
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); Invoke(person, "RefreshLocalization");
                var date = DateTime.TryParse(died, out var parsed) ? person.FormatPersonDate(parsed) : died;
                var ageKnown = birth == "1950-01-01" && died == "2026-03-04";
                var expected = ageKnown ? AppStrings.Get("web.person.died", new Dictionary<string, object?> { ["date"] = date, ["age"] = 76 }) : AppStrings.Get("web.item.editPerson.deathDate") + ": " + date;
                Check(person.DeathDateDisplay == expected, "Person death date retained English or invented an age.");
                Check(ReferenceEquals(source, person.Person) && source.Name == "Raw person" && source.DeathDate == died && source.BirthDate == birth, "Person locale refresh changed source metadata.");
            }
        }
        Apply("en");
        Program.Log("PASS episode empty/whitespace/raw titles, loaded Requests plural counts, open Match heading, real allowance failure/retries and unknown/zero/known-age death dates through en/de/ar/ja.");
    }
}
