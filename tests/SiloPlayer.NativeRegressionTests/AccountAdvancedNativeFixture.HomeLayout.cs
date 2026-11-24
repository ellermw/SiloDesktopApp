using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

// Home layout import against official silo-server 3b2b32168 (#4648): the preview offers the server rows added since the
// export, offers nothing when the baseline cannot be matched or the file has no baseline, hides rows on a page the
// file left alone, and reports a page that failed to save.
internal static partial class AccountAdvancedNativeFixture
{
    private static async Task HomeLayoutBaselineAsync(Frame frame, SettingsPage page, SettingsViewModel vm, State wire)
    {
        wire.HomeWrites = 0; wire.SavedHomeOverrides = [];
        wire.BaselineRows = [("default-continue-watching", "Continue Watching", "tok-cw"), ("s-collections", "Collections", "tok-col"), ("s-hidden-gems", "Hidden Gems", "tok-hg")];
        frame.Width = 1440; await Layout(frame); Show(page, "HomeScreen");
        await Until(() => vm.CanEditHomeSections && !vm.IsLoadingHomeSections);
        var launch = (Button)page.FindName("HomeImportButton");
        static string File(string overrides, string? tokens)
            => $$"""{"format":"silo-home-layout","version":1,"exported_at":"2026-11-20T12:00:00.000Z","server_id":"fixture-server","libraries":[],"pages":[{"scope":"home","overrides":[{{overrides}}]{{(tokens == null ? "" : ",\"server_section_tokens\":" + tokens)}}}]}""";
        string[] Texts(ContentDialog dialog) => All<TextBlock>(dialog).Select(text => text.Text).ToArray();
        async Task<ContentDialog> OpenAsync(string json)
        {
            await Until(() => !(bool)Read(page, "_layoutTransferBusy")!);
            Invoke(page, "HomeImport_Click", launch, new RoutedEventArgs());
            await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog);
            var input = (ContentDialog)Read(page, "_layoutTransferPreview")!;
            await Until(() => All<Button>(input).Any(button => button.Name == "PrimaryButton"));
            All<TextBox>(input).Single(box => box.AcceptsReturn).Text = json; Click(All<Button>(input).Single(button => button.Name == "PrimaryButton"));
            await Until(() => Read(page, "_layoutTransferPreview") is ContentDialog next && !ReferenceEquals(next, input));
            var preview = (ContentDialog)Read(page, "_layoutTransferPreview")!;
            await Until(() => All<Button>(preview).Any(button => button.Name == "PrimaryButton"));
            await Until(() => All<TextBlock>(preview).Any(text => text.Text.Length > 0));
            return preview;
        }
        async Task CloseAsync(ContentDialog preview)
        {
            Click(All<Button>(preview).Single(button => button.Name == "CloseButton"));
            await Until(() => !(bool)Read(page, "_layoutTransferBusy")!);
        }
        async Task CaptureBothAsync(ContentDialog preview, string name)
        {
            foreach (var width in new[] { 1440, 800 })
            {
                frame.Width = width; await Layout(frame);
                await Capture(preview, $"home-layout-{name}-{width}.png");
            }
            frame.Width = 1440; await Layout(frame);
        }

        // 1. Two server rows were added since the export: offered and checked.
        var preview = await OpenAsync(File("", """["tok-cw"]"""));
        var boxes = All<CheckBox>(preview).ToArray();
        Check(Texts(preview).Contains("Hide server rows added since this export:"), "Preview did not offer to hide the rows added since the export");
        Check(boxes.Length == 2 && boxes.All(box => box.IsChecked == true) && boxes.Select(box => ((TextBlock)box.Content).Text).SequenceEqual(["Collections", "Hidden Gems"]),
            "Preview did not offer exactly the two rows added since the export, checked: " + string.Join("|", boxes.Select(box => ((TextBlock)box.Content).Text)));
        Check(Texts(preview).Contains("Replaces this profile's layout on: Home."), "Preview did not name the page it replaces");
        await CaptureBothAsync(preview, "added-rows");
        boxes[1].IsChecked = false;
        Check(preview.IsPrimaryButtonEnabled, "Leaving one row checked disabled Import");
        Click(All<Button>(preview).Single(button => button.Name == "PrimaryButton"));
        await Until(() => wire.HomeWrites == 1 && !(bool)Read(page, "_layoutTransferBusy")!);
        var overrides = wire.HomePayload!.Value.GetProperty("overrides").EnumerateArray().ToArray();
        Check(overrides.Length == 1 && overrides[0].GetProperty("section_id").GetString() == "s-collections" && overrides[0].GetProperty("hidden").GetBoolean(),
            "Import did not hide only the row left checked: " + wire.HomePayload);
        await Capture(frame, "home-layout-result-1440.png");

        // 2. The export's tokens match nothing the profile sees now: nothing is offered and the file imports as before.
        wire.BaselineReads = 0;
        preview = await OpenAsync(File("""{"section_id":"default-continue-watching","position":1},{"is_user_added":true,"user_section_type":"collection","user_title":"Family picks","user_config":{"user_collection_id":"c-private"}}""", """["tok-from-another-key"]"""));
        Check(wire.BaselineReads == 1 && All<CheckBox>(preview).Count() == 0 && !Texts(preview).Any(text => text.Contains("Hide server rows")), "An unmatched baseline still offered rows to hide");
        Check(Texts(preview).Any(text => text.Contains("Family picks (Home): Uses a collection this profile can't see here")), "Skipped row was not listed with the official reason");
        Check(preview.IsPrimaryButtonEnabled, "An unmatched baseline blocked the rest of the file");
        await CaptureBothAsync(preview, "baseline-unmatched");
        await CloseAsync(preview);

        // 3. An export from before tokens existed: no baseline request, no rows offered.
        wire.BaselineReads = 0;
        preview = await OpenAsync(File("""{"section_id":"default-continue-watching","position":0,"hidden":true}""", null));
        Check(wire.BaselineReads == 0 && All<CheckBox>(preview).Count() == 0 && preview.IsPrimaryButtonEnabled, "A file without a baseline read one or offered rows");
        await CaptureBothAsync(preview, "older-file");
        await CloseAsync(preview);

        // 4. The file's only row was skipped, so the page is left alone and only the checked rows are hidden on it.
        wire.SavedHomeOverrides = [JsonSerializer.SerializeToElement(new { id = "saved-custom", is_user_added = true, user_section_type = "recently_added", user_title = "Mine" })];
        preview = await OpenAsync(File("""{"is_user_added":true,"user_section_type":"collection","user_title":"Family picks","user_config":{"user_collection_id":"c-private"}}""", """["tok-cw"]"""));
        Check(Texts(preview).Contains("Hides selected rows on: Home without changing your other rows there."), "A hide-only page did not say it keeps the other rows");
        await CaptureBothAsync(preview, "hide-only");
        foreach (var box in All<CheckBox>(preview)) box.IsChecked = false;
        Check(!preview.IsPrimaryButtonEnabled && Texts(preview).Contains("Nothing in this file applies to this profile on this server."), "Unchecking every row did not leave nothing to import");
        foreach (var box in All<CheckBox>(preview)) box.IsChecked = true;
        wire.HomeStatus = HttpStatusCode.UnprocessableEntity;
        Click(All<Button>(preview).Single(button => button.Name == "PrimaryButton"));
        await Until(() => wire.HomeWrites == 2 && preview.IsPrimaryButtonEnabled && Texts(preview).Any(text => text.Contains("Fixture Home rejection")));
        Check(Texts(preview).Any(text => text.StartsWith("Some parts didn't import: Home (") && text.Contains("Fixture Home rejection")), "A rejected page was not reported by name: " + string.Join("|", Texts(preview)));
        Check(wire.HomePayload!.Value.GetProperty("overrides").EnumerateArray().Any(row => row.TryGetProperty("id", out var id) && id.GetString() == "saved-custom"), "Hiding rows dropped the page's saved overrides");
        await CaptureBothAsync(preview, "partial-failure");
        wire.HomeStatus = HttpStatusCode.OK;
        await CloseAsync(preview);
        Program.Log("PASS actual Home layout import: rows added since the export offered and checked, only checked rows hidden, unmatched and tokenless files import as before, hide-only page keeps its overrides, rejected page named");
    }

    internal sealed partial class State
    {
        internal List<(string Id, string Title, string Token)> BaselineRows = [];
        internal int BaselineReads;
        internal Task<HttpResponseMessage>? HandleLayoutBaseline(string path)
        {
            if (path != "/api/v2/profile/sections/baseline") return null;
            BaselineReads++;
            return Task.FromResult(Reply(new
            {
                section_tokens = BaselineRows.Select(row => row.Token),
                visible_sections = BaselineRows.Select(row => new { id = row.Id, section_type = "recently_added", title = row.Title, token = row.Token }),
            }));
        }
    }
}
