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
        var subtitles = new SubtitleSearchDialog(42, "en", playerMode: false) { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(subtitles, async () =>
        {
            await Until(() => Field<bool>(subtitles, "_providerStatusLoaded"));
            Invoke(subtitles, "SearchButton_Click", subtitles, new RoutedEventArgs());
            var empty = (TextBlock)subtitles.FindName("EmptyStateText");
            await Until(() => empty.Visibility == Visibility.Visible);
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(subtitles);
                Check(empty.Text == AppStrings.Get("web.detail.subtitleSearch.noResults"), "Subtitle empty results did not refresh.");
            }
        });
        Apply("en");
        Program.Log("PASS review followups: region-only Settings refresh, allowed titles, retained person/refresh dialogs, capability search hints and real empty subtitle search; drafts preserved.");
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
