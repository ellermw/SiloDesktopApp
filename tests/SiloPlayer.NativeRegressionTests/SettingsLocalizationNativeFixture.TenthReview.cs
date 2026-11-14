using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Services;
using SiloPlayer.Views.Dialogs;

internal static partial class SettingsLocalizationNativeFixture
{
    private static async Task ReviewTenth(StackPanel parent, Wire wire, SiloApiClient client)
    {
        Apply("en");
        var match = new MatchItemDialog("match-fixture") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(match, async () =>
        {
            var candidates = new[]
            {
                new MatchCandidate { Title = "Raw native", MatchedTitle = "Raw fallback", TitleIsFallback = true, MatchScore = 93.5 },
                new MatchCandidate { Title = "Raw title", OriginalTitle = "Raw original", MatchedTitle = "Raw alias", MatchScore = 87.5 },
            };
            var panel = (StackPanel)match.FindName("ResultsPanel");
            var rows = candidates.Select(candidate => (FrameworkElement)Invoke(match, "BuildCandidateRow", candidate, default(ItemLibraryScope.Scope), 0)!).ToArray();
            foreach (var row in rows) panel.Children.Add(row);
            ((FrameworkElement)match.FindName("ResultsSection")).Visibility = Visibility.Visible;
            typeof(MatchItemDialog).GetField("_selectedCandidate", Private)!.SetValue(match, candidates[1]);
            foreach (var language in new[] { "de", "ar", "ja", "en" })
            {
                Apply(language); await Settle(match);
                var texts = Descendants(match).OfType<TextBlock>().Select(text => text.Text).ToArray();
                foreach (var (key, value, argument) in new[] { ("nativeTitle", "Raw native", "title"), ("originalTitle", "Raw original", "title"), ("matchedAlias", "Raw alias", "title"), ("score", 93.5.ToString("F1", AppStrings.FormatCulture), "score") })
                    Check(texts.Contains(AppStrings.Get("web.item.match." + key, new Dictionary<string, object?> { [argument] = value })), "Retained candidate metadata did not refresh: " + key);
                Check(panel.Children.SequenceEqual(rows) && ReferenceEquals(match.SelectedCandidate, candidates[1]), "Locale refresh replaced candidates or selection.");
            }
        });
        var person = new EditPersonDialog(new() { Id = "fixture-person", Name = "Raw person" }) { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(person, async () =>
        {
            var inputs = Field<Dictionary<string, TextBox>>(person, "_inputs"); inputs["name"].Text = "Unsaved person";
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(person);
                Check(person.PrimaryButtonText == AppStrings.Get("common.save") && person.CloseButtonText == AppStrings.Get("common.cancel"), "Person commands did not refresh.");
                Check(inputs["name"].Text == "Unsaved person", "Person refresh lost draft.");
            }
            wire.PersonWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Click(Descendants(person).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
                await Until(() => Field<bool>(person, "_pending"));
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); await Settle(person);
                    Check(person.PrimaryButtonText == AppStrings.Get("web.item.dialog.saving") && !person.IsPrimaryButtonEnabled, "Person locale change lost saving state.");
                    Check(inputs["name"].Text == "Unsaved person", "Person save locale refresh lost draft.");
                }
            }
            finally { wire.PersonWrite.TrySetResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)); wire.PersonWrite = null; }
            await Until(() => !Field<bool>(person, "_pending"));
            Check(person.PrimaryButtonText == AppStrings.Get("common.save"), "Person failure did not restore localized Save command.");
        });
        var session = new TitleAllowanceSession(client, new() { Id = "primary", IsPrimary = true }, () => true);
        var allowance = new AllowTitleDialog(session, "raw-item", "Raw title 日本語") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(allowance, async () =>
        {
            foreach (var language in new[] { "de", "ar", "ja", "en" })
            {
                Apply(language); await Settle(allowance);
                Check(Equals(allowance.Title, AppStrings.Get("web.profiles.allow.title", new Dictionary<string, object?> { ["name"] = "Raw title 日本語" })), "Allowance title lost parameterized catalog formatting.");
            }
        });
        Apply("en");
        var ctor = typeof(ProfileEditorDialog).GetConstructors(Private).Single();
        var editor = ctor.Invoke([parent.XamlRoot, new SiloPlayer.Core.Models.Auth.Profile { Id = "secondary", Name = "Raw profile" }, Array.Empty<Library>(), false, new AuthApi(client), true, true, false]);
        var profileDialog = Field<ContentDialog>(editor, "_dialog");
        await OpenReviewDialog(profileDialog, async () =>
        {
            var name = Field<TextBox>(editor, "_nameBox"); name.Text = "Unsaved profile";
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Settle(profileDialog);
                foreach (var key in new[] { "presetsHint", "moreOptions", "uploadsUnavailable", "uploadsUnavailableHint" })
                    RequireText((DependencyObject)profileDialog.Content, "web.profiles.editor." + key, "");
                Check(name.Text == "Unsaved profile", "Generated profile label refresh lost name draft.");
                Check(Descendants(profileDialog).OfType<Button>().Any(button => AutomationProperties.GetName(button) == AppStrings.Get("web.profiles.editor.moreOptions")), "More options accessible name did not refresh.");
            }
        });
        Apply("en"); wire.TranslationAvailable = true;
        var metadata = new EditMetadataDialog(new() { ContentId = "movie", Type = "movie", Title = "Raw movie" }) { XamlRoot = parent.XamlRoot };
        try
        {
            await OpenReviewDialog(metadata, async () =>
            {
                await Until(() => Descendants(metadata).OfType<ComboBox>().Any(box => Equals(box.Header, AppStrings.Get("web.item.translate.language"))));
                var languageBox = Descendants(metadata).OfType<ComboBox>().Single(box => Equals(box.Header, AppStrings.Get("web.item.translate.language")));
                var force = Descendants(metadata).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == AppStrings.Get("web.item.translate.force"));
                var translate = Descendants(metadata).OfType<Button>().Single(button => Equals(button.Content, AppStrings.Get("web.item.translate.translate")));
                languageBox.SelectedIndex = 2; var selected = languageBox.SelectedItem; force.IsOn = true;
                foreach (var busy in new[] { false, true })
                {
                    if (busy) { Click(translate); await Until(() => !translate.IsEnabled); }
                    foreach (var language in new[] { "de", "ar", "ja", "en" })
                    {
                        Apply(language); await Settle(metadata);
                        Check(Equals(languageBox.Header, AppStrings.Get("web.item.translate.language")) && languageBox.PlaceholderText == AppStrings.Get("web.item.translate.selectLanguage"), "AI language controls did not refresh.");
                        Check(AutomationProperties.GetName(force) == AppStrings.Get("web.item.translate.force"), "AI force accessible name did not refresh.");
                        RequireText(metadata, "web.item.translate.force", "");
                        Check(Equals(translate.Content, AppStrings.Get(busy ? "web.item.translate.translating" : "web.item.translate.translate")), "AI command lost localized busy state.");
                        Check(ReferenceEquals(languageBox.SelectedItem, selected) && force.IsOn && translate.IsEnabled != busy, "AI locale refresh changed selection, force or busy state.");
                    }
                }
                wire.TranslationComplete = true; await Until(() => translate.IsEnabled);
            });
        }
        finally { wire.TranslationAvailable = false; wire.TranslationComplete = false; }
        Apply("en");
        Program.Log("PASS retained candidate metadata/selection, person commands/draft, parameterized allowance title, generated profile labels/draft and idle/busy AI translation controls through en/de/ar/ja.");
    }
}
