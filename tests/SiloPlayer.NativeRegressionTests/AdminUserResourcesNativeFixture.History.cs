using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

internal static partial class AdminUserResourcesNativeFixture
{
    private const string ImportPath = "/api/v2/admin/history-imports";
    private static AdminRouteRequest SourceRoute(string id = "3") => new AdminRouteRequest(AdminRoute.HistoryImport).WithQuery("source", id);

    private static async Task HistoryImportAsync(Context c)
    {
        var s = c.Server;
        await ScenarioAsync(c, "history import: failed read with retry, loaded page, polling, captures", async () =>
        {
            s.Reset(); s.Fail("GET", ImportPath + "/capabilities", 500);
            await c.OpenAsync(SourceRoute(), "History Import (failed read)");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportRetry"), "a failed capability read offers a retry");
            Check(Has(c.History, "ResourceReload"), "page-level Reload stays reachable");
            Click(Find<Button>(c.History, "ImportRetry"));
            await Until(() => Has(c.History, "ImportDiscover") && Find<Button>(c.History, "ImportDiscover").IsEnabled, "the retry loads the configured source");
            await Until(() => HasText(c.History, "member-one"), "mapping rows name the Silo account, not an id");
            Check(Has(c.History, "ImportExpand_run1") && HasText(c.History, T("web.admin.historyImport.status.running")), "a running import is listed with its status");
            await CaptureAsync(c.History, "admin-history-import-wide.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(150);
            await CaptureAsync(c.History, "admin-history-import-compact.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(150);
            Click(Find<Button>(c.History, "ImportExpand_run0")); await Until(() => HasText(c.History, "Synthetic warning one") && HasText(c.History, "Synthetic Title"), "an expanded run shows its warnings and unmatched samples");
            await Until(() => !Has(c.History, "ImportCancel_run1") && HasText(c.History, T("web.admin.historyImport.status.completed")), "polling brings a running import to its terminal state", 20000);
        });

        await ScenarioAsync(c, "history import: add a Plex source, refusal keeps the dialog, secrets are cleared", async () =>
        {
            s.Reset();
            await c.OpenAsync(SourceRoute(), "History Import");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportAddSource"), "page ready");
            Click(Find<Button>(c.History, "ImportAddSource")); await Until(() => Dialog(c.Frame) != null, "add dialog opened");
            var dialog = Dialog(c.Frame)!; SelectTag(Find<ComboBox>(dialog, "ImportSourceType"), "plex");
            await Until(() => HasText(dialog, T("web.admin.historyImport.plexLimits.title")) && Has(dialog, "ImportPlexUser"), "choosing Plex shows what an admin import cannot read and the sign-in fields");
            await SetTextAsync(Find<TextBox>(dialog, "ImportSourceName"), "Fixture Plex"); await SetTextAsync(Find<TextBox>(dialog, "ImportSourceUrl"), "https://plex.invalid:32400"); await SetTextAsync(Find<TextBox>(dialog, "ImportPlexUser"), "viewer@example.invalid");
            await SetPasswordAsync(All(dialog).OfType<PasswordBox>().First(), "synthetic-password");
            s.Fail("POST", ImportPath + "/plex/login", 422, ProblemBody("Those details were not accepted."));
            Click(Primary(dialog)); await Until(() => HasText(dialog, "Those details were not accepted."), "a refused sign-in shows the server's detail in the dialog");
            Check(Dialog(c.Frame) != null && s.Count("POST", "/history-import-sources") == 0, "the dialog stays open and nothing was created");
            Check(All(dialog).OfType<PasswordBox>().All(x => x.Password.Length == 0), "the typed password is not retained after a failed attempt");
            await SetPasswordAsync(All(dialog).OfType<PasswordBox>().First(), "synthetic-password"); Click(Primary(dialog));
            await Until(() => Dialog(c.Frame) == null && s.Count("POST", "/history-import-sources") == 1, "an accepted sign-in creates the source once");
            var created = JsonNode.Parse(s.Last("POST", "/history-import-sources")!.Body)!.AsObject();
            Check(created["source_type"]!.GetValue<string>() == "plex" && created["sort_order"]!.GetValue<int>() == 0 && created["admin_token"]!.GetValue<string>() == "synthetic-plex-credential" && created["enabled"]!.GetValue<bool>(), "the create body has the official fields and the signed-in token");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportAddSource"), "the page reloaded");
            Check(All(c.History).OfType<PasswordBox>().All(x => x.Password.Length == 0), "no credential remains in the page");
        });

        await ScenarioAsync(c, "history import: edit and token dialogs, conflict, lost answer", async () =>
        {
            s.Reset();
            await c.OpenAsync(SourceRoute(), "History Import");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportEditSource") && Find<Button>(c.History, "ImportEditSource").IsEnabled, "page ready");
            Click(Find<Button>(c.History, "ImportEditSource")); await Until(() => Dialog(c.Frame) != null, "edit dialog opened");
            var dialog = Dialog(c.Frame)!; Check(Find<TextBox>(dialog, "ImportSourceName").Text == "Fixture Jellyfin" && !Find<ComboBox>(dialog, "ImportSourceType").IsEnabled, "the editor shows the selected source and its fixed type");
            Check(All(dialog).OfType<PasswordBox>().All(x => x.Password.Length == 0), "the saved credential is never shown");
            await SetTextAsync(Find<TextBox>(dialog, "ImportSourceName"), "Renamed source"); Click(Primary(dialog));
            await Until(() => Dialog(c.Frame) == null && s.Count("PUT", "/history-import-sources/3") == 1, "the edit is sent once");
            var put = JsonNode.Parse(s.Last("PUT", "/history-import-sources/3")!.Body)!.AsObject();
            Check(put["name"]!.GetValue<string>() == "Renamed source" && !put.ContainsKey("admin_token") && !put.ContainsKey("sort_order") && !put.ContainsKey("source_type"), "an edit without a new credential sends no credential and no immutable field");

            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportToken") && Find<Button>(c.History, "ImportToken").IsEnabled, "page reloaded");
            await Task.Delay(700); Click(Find<Button>(c.History, "ImportToken")); await Until(() => Dialog(c.Frame) != null, "token dialog opened");
            dialog = Dialog(c.Frame)!; await SetPasswordAsync(All(dialog).OfType<PasswordBox>().First(), "synthetic-key-one"); s.TouchSource("3");
            Click(Primary(dialog)); await Until(() => c.History.Session.NeedsReview && Has(dialog, "DialogReload"), "a stale validator locks the dialog and offers a reload");
            Check(!Primary(dialog).IsEnabled && s.Count("PUT", "/sources/3/token") == 1, "no second attempt is possible and nothing was repeated");
            Click(Find<Button>(dialog, "DialogReload")); await Until(() => Dialog(c.Frame) == null, "reload closes the dialog");
            await Until(() => !c.History.Session.NeedsReview && Has(c.History, "ImportToken") && Find<Button>(c.History, "ImportToken").IsEnabled, "the page reloaded");
            await Task.Delay(700); Click(Find<Button>(c.History, "ImportToken")); await Until(() => Dialog(c.Frame) != null, "token dialog opened again");
            await SetPasswordAsync(All(Dialog(c.Frame)!).OfType<PasswordBox>().First(), "synthetic-key-two"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && s.Count("PUT", "/sources/3/token") == 2, "the retry with the fresh validator is sent");
            Check(s.Last("PUT", "/sources/3/token")!.IfMatch == "\"source3-2\"", "the retry carries the refreshed validator");
        });

        await ScenarioAsync(c, "history import: discover and map users, refusal, run with lookback, lost answer, cancel, bulk", async () =>
        {
            s.Reset();
            await c.OpenAsync(SourceRoute(), "History Import");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportDiscover") && Find<Button>(c.History, "ImportDiscover").IsEnabled, "page ready");
            Click(Find<Button>(c.History, "ImportDiscover")); await Until(() => Dialog(c.Frame) != null && Has(Dialog(c.Frame)!, "ImportExternal_ext-2"), "discovery lists the source's unmapped users");
            var dialog = Dialog(c.Frame)!; Check(!Has(dialog, "ImportExternal_ext-1"), "an already mapped user is not offered");
            Click(Find<Button>(dialog, "ImportExternal_ext-2")); await Until(() => Has(dialog, "ImportSiloUser") && Find<ComboBox>(dialog, "ImportSiloUser").Visibility == Visibility.Visible, "choosing a user shows the mapping form");
            SelectTag(Find<ComboBox>(dialog, "ImportSiloUser"), "8"); await Until(() => Find<ComboBox>(dialog, "ImportSiloProfile").IsEnabled && Find<ComboBox>(dialog, "ImportSiloProfile").Items.Count > 0, "the account's profiles load");
            Find<ComboBox>(dialog, "ImportSiloProfile").SelectedIndex = 0;
            s.Fail("POST", ImportPath + "/mappings", 409, ProblemBody("That user is already mapped.", 409)); Click(Find<Button>(dialog, "ImportSaveMapping"));
            await Until(() => HasText(dialog, "That user is already mapped."), "a refused mapping shows the server's detail");
            Check(Dialog(c.Frame) != null && s.Count("POST", ImportPath + "/mappings") == 1, "the dialog stays and the refusal is not repeated");
            Click(Find<Button>(dialog, "ImportSaveMapping")); await Until(() => s.Count("POST", ImportPath + "/mappings") == 2 && !Has(dialog, "ImportExternal_ext-2"), "an accepted mapping leaves the dialog open for the next user");
            var mapping = JsonNode.Parse(s.Last("POST", ImportPath + "/mappings")!.Body)!.AsObject();
            Check(mapping["source_id"]!.GetValue<string>() == "3" && mapping["external_user_id"]!.GetValue<string>() == "ext-2" && mapping["silo_user_id"]!.GetValue<string>() == "8" && mapping["silo_profile_id"]!.GetValue<string>() == "p1", "the mapping body names source, external user, Silo user and profile");
            dialog.Hide(); await Until(() => Dialog(c.Frame) == null, "discovery dialog closed");
            await Until(() => Has(c.History, "ImportRun_10"), "the new mapping is listed");

            SelectTag(Find<ComboBox>(c.History, "ImportLookback"), "72");
            Click(Find<Button>(c.History, "ImportRun_9")); await Until(() => s.Count("POST", "/mappings/9/run") == 1 && Has(c.History, "ImportExpand_run2"), "starting an import adds the run");
            Check(JsonNode.Parse(s.Last("POST", "/mappings/9/run")!.Body)!["lookback_hours"]!.GetValue<int>() == 72, "the run body carries the lookback window");
            s.LoseAnswer("POST", ImportPath + "/mappings/9/run"); Click(Find<Button>(c.History, "ImportRun_9"));
            await Until(() => c.History.Session.NeedsReview, "a lost answer to a run is an unknown outcome");
            Check(s.Count("POST", "/mappings/9/run") == 2 && !Find<Button>(c.History, "ImportRun_9").IsEnabled, "the lost run was not replayed and cannot be repeated");
            Click(Find<Button>(c.History, "ResourceReload")); await Until(() => !c.History.Session.NeedsReview && Has(c.History, "ImportCancel_run2"), "the reload shows the active run");
            Click(Find<Button>(c.History, "ImportCancel_run2")); await Until(() => s.Count("POST", "/runs/run2/cancel") == 1 && !Has(c.History, "ImportCancel_run2") || HasText(c.History, T("web.admin.historyImport.status.canceling")), "cancel is sent once");
            Click(Find<Button>(c.History, "ImportBulk")); await Until(() => HasText(c.History, "Synthetic bulk failure"), "bulk import lists each outcome with its error");
        });

        await ScenarioAsync(c, "history import: delete source, then the empty state", async () =>
        {
            s.Reset();
            await c.OpenAsync(SourceRoute(), "History Import");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportDeleteSource") && Find<Button>(c.History, "ImportDeleteSource").IsEnabled, "page ready");
            Click(Find<Button>(c.History, "ImportDeleteSource")); await Until(() => Dialog(c.Frame) != null, "delete confirmation opened");
            Dialog(c.Frame)!.Hide(); await Until(() => Dialog(c.Frame) == null, "cancelled"); Check(s.Count("DELETE", "/history-import-sources/3") == 0, "a cancelled delete sends nothing");
            Click(Find<Button>(c.History, "ImportDeleteSource")); await Until(() => Dialog(c.Frame) != null, "delete confirmation opened again"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && HasText(c.History, T("web.admin.historyImport.sourceBar.emptyTitle")), "with no source left the page explains how to start");
            Check(s.Count("DELETE", "/history-import-sources/3") == 1, "the confirmed delete was sent once");
            c.Authority.Dispose();
            await Until(() => c.History.Session.Owner == null || All(c.History).OfType<Button>().Where(x => AutomationProperties.GetAutomationId(x) == "ImportAddSource").All(x => !x.IsEnabled), "retiring the authority disables retained actions");
        });
    }
}
