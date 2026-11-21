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
    private const string GroupsPath = "/api/v2/admin/access-groups";
    private static string ProblemBody(string detail, int status = 422) => $$"""{"type":"https://silo.example/docs/api/v2/problems/validation_failed","title":"Rejected","detail":"{{detail}}","status":{{status}}}""";

    private static async Task AccessGroupsAsync(Context c)
    {
        var s = c.Server;
        await ScenarioAsync(c, "groups: empty list, failed read with retry, populated list", async () =>
        {
            s.Reset(); s.EmptyGroups = true;
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.AccessGroups), "Access Groups (empty)");
            await Until(() => c.Frame.Content != null && All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && HasText(c.Groups, T("web.admin.access.groups.empty.title")), "empty list says there are no groups");
            s.Reset(); s.Fail("GET", "/api/v2/admin/users/capabilities", 500);
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.AccessGroups), "Access Groups (failed read)");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupRetry"), "a failed capability read shows what failed and a Retry");
            Check(Has(c.Groups, "ResourceReload"), "page-level Reload stays reachable after a failed read");
            Click(Find<Button>(c.Groups, "GroupRetry"));
            await Until(() => Has(c.Groups, "GroupCard_3"), "Retry loads the groups");
            await Until(() => HasText(c.Groups, T("web.admin.access.groups.card.default")), "the default group is marked");
            await Until(() => HasText(c.Groups, "Admin approves"), "a card shows its own request terms");
            await CaptureAsync(c.Groups, "admin-access-groups-list-wide.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(150);
            await CaptureAsync(c.Groups, "admin-access-groups-list-compact.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(150);
        });

        await ScenarioAsync(c, "groups: create refused then accepted, then a lost answer", async () =>
        {
            s.Reset();
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.AccessGroups), "Access Groups");
            await Until(() => Has(c.Groups, "GroupNew"), "list ready");
            Click(Find<Button>(c.Groups, "GroupNew")); await Until(() => Dialog(c.Frame) != null, "create dialog opened");
            var dialog = Dialog(c.Frame)!; await SetTextAsync(Find<TextBox>(dialog, "NewGroupName"), "Visitors");
            s.Fail("POST", GroupsPath, 422, ProblemBody("Name is already used."));
            Click(Primary(dialog)); await Until(() => HasText(dialog, "Name is already used."), "a refused create shows the server's detail in the dialog");
            Check(Dialog(c.Frame) != null && Find<TextBox>(dialog, "NewGroupName").Text == "Visitors", "the dialog and the typed name stay for a correction");
            Check(!c.Groups.Session.NeedsReview && Primary(dialog).IsEnabled, "a refusal never blocks the retry");
            Check(s.Count("POST", GroupsPath) == 1, "the refused create was sent once");
            await SetTextAsync(Find<TextBox>(dialog, "NewGroupName"), "Visitors 2"); Click(Primary(dialog));
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupName") && Find<TextBox>(c.Groups, "GroupName").Text == "Visitors 2", "an accepted create opens the new group's editor");
            Check(s.Count("POST", GroupsPath) == 2, "the retry was a new request");

            await c.OpenAsync(new AdminRouteRequest(AdminRoute.AccessGroups), "Access Groups again");
            await Until(() => Has(c.Groups, "GroupNew"), "list ready again");
            Click(Find<Button>(c.Groups, "GroupNew")); await Until(() => Dialog(c.Frame) != null, "create dialog opened again");
            dialog = Dialog(c.Frame)!; await SetTextAsync(Find<TextBox>(dialog, "NewGroupName"), "Lost answer"); s.LoseAnswer("POST", GroupsPath);
            Click(Primary(dialog)); await Until(() => HasText(dialog, "may have reached the server"), "a lost answer is reported as an unknown outcome");
            Check(c.Groups.Session.NeedsReview && !Primary(dialog).IsEnabled, "an unknown create blocks another attempt");
            Check(s.Count("POST", GroupsPath) == 3, "the lost create was not replayed");
            Click(Find<Button>(dialog, "DialogReload")); await GoneAsync(c, "reload closes the dialog");
            await Until(() => Has(c.Groups, "GroupCard_5"), "the reload finds the group the lost answer created");
        });

        await ScenarioAsync(c, "groups: editor untouched, draft normalization, request body", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupSave"), "editor rendered");
            Check(!Find<Button>(c.Groups, "GroupSave").IsEnabled, "an untouched editor cannot save");
            Check(s.Count("PUT", GroupsPath) == 0, "an untouched editor sends nothing");
            Check(Find<TextBox>(c.Groups, "GroupName").Text == "Guests", "the stored name is shown");
            Check(Find<TextBox>(c.Groups, "GroupRemoteBitrateCustom").Text == "1.5", "a stored custom bitrate shows in Mbps");
            await Until(() => Has(c.Groups, "GroupMember_9"), "members listed");
            await CaptureAsync(c.Groups, "admin-access-groups-editor-wide.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(150);
            await CaptureAsync(c.Groups, "admin-access-groups-editor-compact.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(150);

            await SetTextAsync(Find<TextBox>(c.Groups, "GroupName"), "Guests renamed");
            await Until(() => Find<Button>(c.Groups, "GroupSave").IsEnabled, "an edit enables Save");
            Click(Find<Button>(c.Groups, "GroupSave"));
            await Until(() => Has(c.Groups, "GroupCard_3"), "a successful save returns to the list");
            var put = s.Last("PUT", GroupsPath + "/3")!; var body = JsonNode.Parse(put.Body)!.AsObject();
            Check(body.Count == AdminAccessGroupBody.Keys.Count && AdminAccessGroupBody.Keys.All(body.ContainsKey), "the save sends exactly the official request fields");
            Check(body["max_remote_stream_bitrate_kbps"]!.GetValue<int>() == 1500 && body["library_ids"]![0]!.GetValue<string>() == "1", "untouched values keep their stored form");
            Check(put.IfMatch == "\"group3-0\"", "the save carries the validator it read");
        });

        await ScenarioAsync(c, "groups: refused save keeps the draft; conflict reload keeps it; lost answer blocks", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupSave"), "editor rendered");
            await SetTextAsync(Find<TextBox>(c.Groups, "GroupName"), "Mine"); s.Fail("PUT", GroupsPath + "/3", 422, ProblemBody("Name is already used."));
            Click(Find<Button>(c.Groups, "GroupSave")); await Until(() => HasText(c.Groups, "Name is already used."), "a refused save shows the server's detail beside Save");
            Check(Find<TextBox>(c.Groups, "GroupName").Text == "Mine" && !c.Groups.Session.NeedsReview && Find<Button>(c.Groups, "GroupSave").IsEnabled, "a refusal keeps the draft and the retry");
            // another admin changes the group
            s.TouchGroup("3"); Click(Find<Button>(c.Groups, "GroupSave"));
            await Until(() => c.Groups.Session.NeedsReview, "a stale validator requires a review");
            Check(!Find<Button>(c.Groups, "GroupSave").IsEnabled, "a conflict blocks repeated writes");
            Check(Find<TextBox>(c.Groups, "GroupName").Text == "Mine", "a conflict keeps the typed draft");
            Check(Has(c.Groups, "GroupReloadKeepDraft") && Find<Button>(c.Groups, "GroupReloadKeepDraft").IsEnabled, "the draft-keeping reload is reachable while the page is locked");
            Click(Find<Button>(c.Groups, "GroupReloadKeepDraft")); await Until(() => !c.Groups.Session.NeedsReview && Has(c.Groups, "GroupSave"), "reload clears the lock");
            Check(Find<TextBox>(c.Groups, "GroupName").Text == "Mine" && Find<Button>(c.Groups, "GroupSave").IsEnabled, "the reload kept the draft and refreshed the validator");
            Click(Find<Button>(c.Groups, "GroupSave")); await Until(() => Has(c.Groups, "GroupCard_3"), "the save now succeeds");
            Check(s.Last("PUT", GroupsPath + "/3")!.IfMatch == "\"group3-1\"", "the retry used the refreshed validator");

            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor again");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupSave"), "editor rendered again");
            var before = s.Count("PUT", GroupsPath + "/3"); await SetTextAsync(Find<TextBox>(c.Groups, "GroupName"), "Lost write"); s.LoseAnswer("PUT", GroupsPath + "/3");
            Click(Find<Button>(c.Groups, "GroupSave")); await Until(() => c.Groups.Session.NeedsReview, "a lost answer requires a review");
            Check(s.Count("PUT", GroupsPath + "/3") == before + 1 && !Find<Button>(c.Groups, "GroupSave").IsEnabled, "the lost write was sent once and cannot be repeated");
            Click(Find<Button>(c.Groups, "ResourceReload")); await Until(() => !c.Groups.Session.NeedsReview && Has(c.Groups, "GroupName") && Find<TextBox>(c.Groups, "GroupName").Text == "Lost write", "the reload shows what the server holds");
            Check(!Find<Button>(c.Groups, "GroupSave").IsEnabled, "after the reload the editor is clean");
        });

        await ScenarioAsync(c, "groups: request limit follow-up failure, bitrate, libraries", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupApproval"), "editor rendered with its request limit");
            await SetTextAsync(Find<TextBox>(c.Groups, "GroupName"), "Both"); SelectTag(Find<ComboBox>(c.Groups, "GroupApproval"), "auto");
            s.Fail("PUT", "/api/v2/admin/request-groups/3/limit", 422, ProblemBody("Limit rejected."));
            Click(Find<Button>(c.Groups, "GroupSave"));
            await Until(() => HasText(c.Groups, "The group was saved, but its request approval and limit were not."), "a failed second request says the first was saved");
            Check(HasText(c.Groups, "Limit rejected.") && !c.Groups.Session.NeedsReview, "the reason is the server's and the page stays usable");
            Check(s.Count("PUT", GroupsPath + "/3") == 1 && s.Count("PUT", "/request-groups/3/limit") == 1, "each request was sent once");
            Click(Find<Button>(c.Groups, "GroupSave")); await Until(() => Has(c.Groups, "GroupCard_3"), "the retry completes");
            Check(s.Count("PUT", GroupsPath + "/3") == 1 && s.Count("PUT", "/request-groups/3/limit") == 2, "the retry sent only the limit, never the saved group again");

            s.Reset();
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupRemoteBitrate"), "editor rendered");
            SelectTag(Find<ComboBox>(c.Groups, "GroupRemoteBitrate"), "custom"); await SetTextAsync(Find<TextBox>(c.Groups, "GroupRemoteBitrateCustom"), "abc");
            Check(!Find<Button>(c.Groups, "GroupSave").IsEnabled, "a bitrate that is not a number blocks Save");
            await SetTextAsync(Find<TextBox>(c.Groups, "GroupRemoteBitrateCustom"), "2.5"); Check(Find<Button>(c.Groups, "GroupSave").IsEnabled, "a valid custom bitrate enables Save");
            var all = Find<ToggleSwitch>(c.Groups, "GroupAllLibraries"); all.IsOn = true; all.IsOn = false;
            await Until(() => Has(c.Groups, "GroupLibrary_2"), "turning All libraries off lists every library");
            Check(Find<ToggleSwitch>(c.Groups, "GroupLibrary_1").IsOn && Find<ToggleSwitch>(c.Groups, "GroupLibrary_2").IsOn, "turning it off selects every library, as the official page does");
            Click(Find<Button>(c.Groups, "GroupSave")); await Until(() => Has(c.Groups, "GroupCard_3"), "saved");
            var body = JsonNode.Parse(s.Last("PUT", GroupsPath + "/3")!.Body)!.AsObject();
            Check(body["max_remote_stream_bitrate_kbps"]!.GetValue<int>() == 2500 && body["library_ids"]!.AsArray().Count == 2, "the body carries kbps and the chosen libraries");
        });

        await ScenarioAsync(c, "groups: delete confirmation, refusal, success", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.AccessGroup("2"), "Default group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupDelete"), "editor rendered");
            Check(!Find<Button>(c.Groups, "GroupDelete").IsEnabled, "the default group cannot be deleted");
            Check(!Find<ToggleSwitch>(c.Groups, "GroupDefault").IsEnabled, "the default switch is locked on the default group");
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupDelete"), "editor rendered");
            Click(Find<Button>(c.Groups, "GroupDelete")); await Until(() => Dialog(c.Frame) != null, "delete confirmation opened");
            Check(HasText(Dialog(c.Frame)!, "Family"), "the confirmation names where the members go");
            Dialog(c.Frame)!.Hide(); await GoneAsync(c, "confirmation cancelled"); Check(s.Count("DELETE", GroupsPath) == 0, "a cancelled delete sends nothing");
            Click(Find<Button>(c.Groups, "GroupDelete")); await Until(() => Dialog(c.Frame) != null, "confirmation opened again");
            s.Fail("DELETE", GroupsPath + "/3", 409, ProblemBody("This group is still in use.", 409)); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && HasText(c.Groups, "This group is still in use."), "a refused delete shows the server's detail and keeps the editor");
            Check(!c.Groups.Session.NeedsReview && Find<Button>(c.Groups, "GroupDelete").IsEnabled, "the admin can try again");
            Click(Find<Button>(c.Groups, "GroupDelete")); await Until(() => Dialog(c.Frame) != null, "confirmation opened a third time"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Has(c.Groups, "GroupCard_2") && !Has(c.Groups, "GroupCard_3"), "a successful delete returns to a list without the group");
            Check(s.Count("DELETE", GroupsPath + "/3") == 2, "each confirmed delete was sent once");
        });

        await ScenarioAsync(c, "groups: members move, refusal, add, lost answer", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupMember_9"), "members listed");
            Check(!Has(c.Groups, "GroupMember_7") && Has(c.Groups, "GroupMember_10"), "only this group's regular members are listed");
            Find<CheckBox>(c.Groups, "GroupMember_9").IsChecked = true; SelectTag(Find<ComboBox>(c.Groups, "GroupMoveTarget"), "2");
            Click(Find<Button>(c.Groups, "GroupMoveMembers")); await Until(() => Dialog(c.Frame) != null, "move confirmation opened");
            Check(HasText(Dialog(c.Frame)!, "Libraries") && HasText(Dialog(c.Frame)!, "Playback quality"), "the confirmation names what changes for the members");
            Click(Primary(Dialog(c.Frame)!)); await Until(() => Dialog(c.Frame) == null && !Has(c.Groups, "GroupMember_9"), "the member moved out");
            var move = s.Last("PUT", "/api/v2/admin/users/9")!; Check(JsonNode.Parse(move.Body)!["access_group_id"]!.GetValue<string>() == "2" && move.IfMatch == "\"user9-0\"", "the move sends only the group id with the account's validator");
            Check(HasText(c.Groups, "Moved 1 user to Family"), "the result is announced");

            Find<CheckBox>(c.Groups, "GroupMember_10").IsChecked = true; SelectTag(Find<ComboBox>(c.Groups, "GroupMoveTarget"), "2");
            Click(Find<Button>(c.Groups, "GroupMoveMembers")); await Until(() => Dialog(c.Frame) != null, "second move confirmation opened");
            s.Fail("PUT", "/api/v2/admin/users/10", 409, ProblemBody("This account is locked.", 409)); Click(Primary(Dialog(c.Frame)!));
            await Until(() => HasText(c.Groups, "This account is locked."), "a refused move is listed by account with the reason");
            Check(Has(c.Groups, "GroupMember_10") && Find<CheckBox>(c.Groups, "GroupMember_10").IsChecked == true, "the failed account stays selected for a retry");

            Click(Find<Button>(c.Groups, "GroupAddMembers")); await Until(() => Dialog(c.Frame) != null, "add users dialog opened");
            Find<CheckBox>(Dialog(c.Frame)!, "GroupAddUser_7").IsChecked = true; Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) != null && HasText(Dialog(c.Frame)!, "Libraries"), "adding opens the same confirmation"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && Has(c.Groups, "GroupMember_7"), "the added account is now a member");

            // two selected members; the first answer is lost, so the second is not attempted
            Find<CheckBox>(c.Groups, "GroupMember_10").IsChecked = true; Find<CheckBox>(c.Groups, "GroupMember_7").IsChecked = true; SelectTag(Find<ComboBox>(c.Groups, "GroupMoveTarget"), "2");
            Click(Find<Button>(c.Groups, "GroupMoveMembers")); await Until(() => Dialog(c.Frame) != null, "third move confirmation opened");
            s.LoseAnswer("PUT", "/api/v2/admin/users/7"); var users10 = s.Count("PUT", "/api/v2/admin/users/10"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => c.Groups.Session.NeedsReview && HasText(c.Groups, "not attempted"), "a lost answer stops the batch and lists the rest as not attempted");
            Check(s.Count("PUT", "/api/v2/admin/users/10") == users10 && s.Count("PUT", "/api/v2/admin/users/7") == 2, "nothing was replayed and the second account was never sent");
        });
    }
}
