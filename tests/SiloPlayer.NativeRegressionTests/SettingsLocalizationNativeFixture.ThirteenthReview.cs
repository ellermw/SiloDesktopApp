using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;
using SiloPlayer.Views.Dialogs;

internal static partial class SettingsLocalizationNativeFixture
{
    private static readonly string[] ReviewLanguages13 = ["de", "ar", "ja", "en"];
    private static async Task ReviewThirteenth(StackPanel parent, Wire wire)
    {
        wire.ReviewThirteenthCase = true;
        try
        {
            var refresh = new RefreshMetadataDialog(_ => Task.CompletedTask) { XamlRoot = parent.XamlRoot };
            await OpenReviewDialog(refresh, async () =>
            {
                await Settle(refresh);
                var close = Descendants(refresh).OfType<Button>().Single(button => Localized.GetName(button) == "web.ui.close");
                foreach (var language in ReviewLanguages13)
                {
                    Apply(language); await Settle(refresh);
                    Check(AutomationProperties.GetName(close) == AppStrings.Get("web.ui.close") && Equals(ToolTipService.GetToolTip(close), AppStrings.Get("web.ui.close")), "Refresh metadata close name/tooltip did not refresh: " + language + "; name=" + AutomationProperties.GetName(close) + "; tip=" + ToolTipService.GetToolTip(close) + "; loaded=" + close.IsLoaded + "; tooltip-key=" + Localized.GetToolTip(close) + "; runtime=" + typeof(RefreshMetadataDialog).Assembly.Location);
                }
            });
            await ReviewImageGallery13(parent, wire);
            await ReviewCollectionPending13(parent, wire);
            await ReviewHistoryPaging13(parent, wire);
            await ReviewHomeRowCommands13(parent);
        }
        finally { wire.ReviewThirteenthCase = false; Apply("en"); await Settle(parent); }
        Program.Log("PASS thirteenth review: live metadata close/gallery controls, held collection write, history loading/retry/recovery and actual Home-row add/edit forms localize en/de/ar/ja without lost drafts/selections or extra API work.");
    }

    private static async Task ReviewImageGallery13(StackPanel parent, Wire wire)
    {
        foreach (var mode in new[] { "cards", "empty", "failed", "season" })
        {
            wire.ImageCase = mode == "season" ? "empty" : mode;
            var dialog = new EditMetadataDialog(new() { ContentId = "review-images", Type = mode == "season" ? "season" : "movie", Title = "Raw item 日本語" }) { XamlRoot = parent.XamlRoot };
            await OpenReviewDialog(dialog, async () =>
            {
                Invoke(dialog, "ShowSection", "images"); await Settle(dialog);
                var images = Field<Dictionary<string, StackPanel>>(dialog, "_sections")["images"];
                await Until(() => Descendants(images).OfType<TextBlock>().Any(text => Localized.GetText(text) == (mode == "failed" ? "web.item.images.loadFailed" : mode == "cards" ? "web.item.images.current" : "web.item.images.noImages")));
                var input = Field<Dictionary<string, TextBox>>(dialog, "_textInputs")["title"]; input.Text = "Unsaved image draft";
                var cards = Descendants(images).OfType<Button>().Where(button => button.Tag is "fixture-current" or "fixture-new").ToArray();
                var apply = images.Children.OfType<Button>().Single();
                var reads = wire.ImageReads;
                if (mode == "cards") Click(cards.Single(card => Equals(card.Tag, "fixture-new")));
                foreach (var language in ReviewLanguages13)
                {
                    Apply(language); await Settle(dialog);
                    foreach (var key in new[] { "immediateNotice", mode == "season" ? "seasonNotice" : mode == "failed" ? "loadFailed" : mode == "cards" ? "current" : "noImages" })
                        Check(Descendants(images).OfType<TextBlock>().Any(text => text.Text == AppStrings.Get("web.item.images." + key)), "Gallery label did not refresh: " + key + "/" + language);
                    foreach (var tab in Descendants(images).OfType<Button>().Where(button => button.Tag is "poster" or "backdrop" or "logo"))
                        Check(Equals(tab.Content, AppStrings.Get("web.item.images." + (tab.Tag is "poster" ? "posters" : tab.Tag is "backdrop" ? "backdrops" : "logos"))), "Image tab stayed in old language.");
                    var toggle = Descendants(images).OfType<ToggleButton>().Single();
                    Check(Equals(toggle.Content, AppStrings.Get("web.item.images.textless")), "Textless toggle did not refresh.");
                    Check(input.Text == "Unsaved image draft" && wire.ImageReads == reads, "Image locale update lost draft or reloaded data.");
                    if (mode == "cards")
                    {
                        Check(cards.SequenceEqual(Descendants(images).OfType<Button>().Where(button => button.Tag is "fixture-current" or "fixture-new")), "Image cards rebuilt on locale update.");
                        Check(apply.IsEnabled && Equals(apply.Content, AppStrings.Get("web.item.images.applyPoster")), "Image selection/apply label did not survive locale update: " + language + "; enabled=" + apply.IsEnabled + "; content=" + apply.Content + "; expected=" + AppStrings.Get("web.item.images.applyPoster"));
                        var expectedWarning = AppStrings.Get("web.item.images.providerErrors", new Dictionary<string, object?> { ["providers"] = "TMDB" });
                        Check(Descendants(images).OfType<TextBlock>().Any(text => text.Text == expectedWarning), "Provider warning did not refresh.");
                    }
                }
                if (mode == "cards")
                {
                    wire.ImageWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        Click(apply); await Until(() => wire.ImageWrites > 0 && Field<bool>(dialog, "_imagePending"));
                        foreach (var language in ReviewLanguages13)
                        {
                            Apply(language); await Settle(dialog);
                            Check(Equals(apply.Content, AppStrings.Get("web.item.dialog.applying")) && !apply.IsEnabled, "Applying label/state did not refresh.");
                            Check(wire.ImageWrites == 1 && wire.ImageReads == reads, "Locale change dispatched extra image work.");
                        }
                    }
                    finally { wire.ImageWrite.TrySetResult(new(HttpStatusCode.ServiceUnavailable)); }
                    await Until(() => !Field<bool>(dialog, "_imagePending"));
                    Check(apply.IsEnabled && Equals(apply.Content, AppStrings.Get("web.item.images.applyPoster")), "Failed apply lost selected image or command.");
                    ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)new Microsoft.UI.Xaml.Automation.Peers.ToggleButtonAutomationPeer(Descendants(images).OfType<ToggleButton>().Single()).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle(); await Settle(dialog);
                    foreach (var language in ReviewLanguages13)
                    {
                        Apply(language); await Settle(dialog);
                        Check(Descendants(images).OfType<TextBlock>().Any(text => text.Text == AppStrings.Get("web.item.images.noTextless")), "Textless empty-state did not refresh.");
                    }
                }
            });
        }
    }

    private static async Task ReviewCollectionPending13(StackPanel parent, Wire wire)
    {
        var dialog = new AddToCollectionDialog("review-title", "Raw item 日本語") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(dialog, async () =>
        {
            var groups = (StackPanel)dialog.FindName("GroupsPanel"); await Until(() => groups.Children.OfType<Button>().Any());
            Click(groups.Children.OfType<Button>().Single());
            wire.CollectionWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Click(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
                await Until(() => wire.CollectionWrites == 1);
                foreach (var language in ReviewLanguages13)
                {
                    Apply(language); await Settle(dialog);
                    Check(((TextBlock)dialog.FindName("StatusText")).Text == AppStrings.Get("web.collections.manual.saving") && !dialog.IsPrimaryButtonEnabled, "Collection pending status/state did not refresh.");
                    Check(wire.CollectionWrites == 1 && groups.Children.OfType<Button>().Count() == 1, "Locale update changed collection selection or write count.");
                }
            }
            finally { wire.CollectionWrite.TrySetResult(new(HttpStatusCode.ServiceUnavailable)); }
            await Until(() => dialog.IsPrimaryButtonEnabled);
        });
    }

    private static async Task ReviewHistoryPaging13(StackPanel parent, Wire wire)
    {
        wire.HistoryReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new PlayHistoryDialog("review-history") { XamlRoot = parent.XamlRoot };
        await OpenReviewDialog(dialog, async () =>
        {
            var more = Field<Button>(dialog, "_more"); await Until(() => wire.HistoryReads > 0);
            foreach (var language in ReviewLanguages13)
            {
                Apply(language); await Settle(dialog);
                Check(Equals(more.Content, AppStrings.Get("web.common.ui.loadMore.loading")) && !more.IsEnabled, "Pending history paging label/state did not refresh.");
            }
            wire.HistoryReply.TrySetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"first\"}}") }); wire.HistoryReply = null;
            await Until(() => !Field<bool>(dialog, "_loading"));
            foreach (var language in ReviewLanguages13)
            {
                Apply(language); await Settle(dialog);
                Check(Equals(more.Content, AppStrings.Get("web.common.ui.loadMore.button")), "History Load more did not refresh.");
            }
            wire.HistoryFails = true; Click(more); await Until(() => !Field<bool>(dialog, "_loading") && Field<bool>(dialog, "_retry"));
            foreach (var language in ReviewLanguages13)
            {
                Apply(language); await Settle(dialog);
                Check(Equals(more.Content, AppStrings.Get("common.retry")) && more.IsEnabled, "History retry label/state did not refresh.");
            }
            wire.HistoryFails = false; Click(more); await Until(() => !Field<bool>(dialog, "_loading") && !Field<bool>(dialog, "_retry"));
            Check(Equals(more.Content, AppStrings.Get("web.common.ui.loadMore.button")) && wire.HistoryReads == 3, "Successful retry did not restore Load more or dispatched extra reads.");
        });
    }

    private static async Task ReviewHomeRowCommands13(StackPanel parent)
    {
        foreach (var editing in new[] { false, true })
        {
            var section = editing ? new SettingsSectionEntry { Id = "raw-row", SectionType = "recently_added", Title = "Raw row", IsCustom = true } : null;
            var pending = RecipeGalleryDialog.ShowEditorAsync(parent.XamlRoot, new RecipeCatalogResponse(), section);
            Grid? form = null;
            await Until(() => (form = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => Descendants(popup.Child)).OfType<Grid>().SingleOrDefault(grid => grid.Name == "HomeRowForm")) != null);
            try
            {
                await Settle(form!);
                await Until(() => Descendants(form!).OfType<TextBox>().Any(box => Localized.GetHeader(box) == "web.home.addRow.form.rowName"));
                var name = Descendants(form!).OfType<TextBox>().Single(box => Localized.GetHeader(box) == "web.home.addRow.form.rowName"); name.Text = "Unsaved raw row 日本語";
                var header = form!.Children.OfType<TextBlock>().First();
                var buttons = Descendants(form).OfType<Button>().Where(button => button.Parent is StackPanel panel && panel.Children.Count == 2 && panel.Children.All(child => child is Button)).ToArray();
                var save = buttons.Single(button => Localized.GetContent(button) != "common.cancel");
                foreach (var language in ReviewLanguages13)
                {
                    Apply(language); await Settle(form);
                    Check(header.Text == AppStrings.Get(editing ? "web.home.addRow.editRow" : "web.home.addRow.addRow"), "Home-row heading did not refresh.");
                    Check(Equals(save.Content, AppStrings.Get(editing ? "web.home.addRow.save" : "web.home.addRow.addRow")), "Home-row command did not refresh.");
                    Check(name.Text == "Unsaved raw row 日本語", "Home-row locale update discarded draft.");
                }
                Click(save);
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Check(result?.Title == "Unsaved raw row 日本語", "Home-row result changed raw saved title.");
            }
            finally
            {
                if (!pending.IsCompleted)
                {
                    var cancel = Descendants(form!).OfType<Button>().Single(button => Localized.GetContent(button) == "common.cancel"); Click(cancel); await pending;
                }
            }
        }
    }
}
