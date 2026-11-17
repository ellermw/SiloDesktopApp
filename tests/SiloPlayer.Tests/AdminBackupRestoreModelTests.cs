using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Model = SiloPlayer.Core.Services.Admin.AdminBackupRestoreModel;

namespace SiloPlayer.Tests;

/// <summary>Official restoreModel.ts / restoreGroups.ts behaviour at 92ee051 on synthetic previews.</summary>
public sealed class AdminBackupRestoreModelTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static AdminBackupRestoreSet Set(params string[] sections) => JsonSerializer.Deserialize<AdminBackupRestoreSet>(BackupJson.RestoreSet(string.Join(",", sections)), Json)!;

    private static AdminBackupRestoreSet Library() => Set(
        BackupJson.Section("libraries", BackupJson.Item("4", "library", "not_on_this_server", extra: ",\"library\":{\"type\":\"movies\",\"paths\":[\"/media/movies\"]}")),
        BackupJson.Section("collections", BackupJson.Item("c1", "collection", "not_on_this_server", requires: "\"libraries:4\"")),
        BackupJson.Section("home_sections", BackupJson.Item("section:9", "home_section", "not_on_this_server", requires: "\"collections:c1\"",
            extra: ",\"home_section\":{\"scope\":\"home\",\"section_type\":\"collection\"}")));

    [Fact]
    public void ChoosingAnItemChoosesWhatItRequiresTransitivelyAndSaysSo()
    {
        var set = Library();
        var index = Model.Index(set);
        var change = Model.Choose(index, new HashSet<string>(), ["home_sections:section:9"]);
        Assert.Equal(new[] { "collections:c1", "home_sections:section:9", "libraries:4" }, change.Selection.OrderBy(key => key));
        Assert.NotNull(change.Notice);
        var unchosen = Model.Unchoose(index, change.Selection, ["libraries:4"]);
        Assert.Empty(unchosen.Selection);
        Assert.NotNull(unchosen.Notice);
    }

    [Fact]
    public void AnItemWhoseRequirementCannotBeChosenStaysOut()
    {
        var set = Set(BackupJson.Section("libraries", BackupJson.Item("4", "library", "folder_missing", selectable: false, extra: ",\"reason\":\"folder_missing\"")),
            BackupJson.Section("collections", BackupJson.Item("c1", "collection", "not_on_this_server", requires: "\"libraries:4\""), BackupJson.Item("c2", "collection", "not_on_this_server", requires: "\"libraries:99\"")));
        var index = Model.Index(set);
        var change = Model.Choose(index, new HashSet<string>(), ["collections:c1", "collections:c2"]);
        Assert.Empty(change.Selection);
        Assert.NotNull(change.Notice);
    }

    private static AdminBackupRestoreSet Accounts() => Set(BackupJson.Section("users",
        BackupJson.Account("u1", "new", BackupJson.Profile("p1", primary: true) + "," + BackupJson.Profile("p2")) + "," +
        BackupJson.Account("u2", "42", BackupJson.Profile("p3", target: "17"))));

    [Fact]
    public void ANewAccountRestoresItsPrimaryProfileWithAnyOther()
    {
        var set = Accounts();
        var index = Model.Index(set);
        var selection = Model.Choose(index, new HashSet<string>(), [Model.ProfileKey("u1", "p2")]).Selection;
        Assert.Contains("users:u1", selection); // a profile requires its account
        var account = index["users:u1"].Item;
        Assert.Equal(["p1", "p2"], Model.ChosenProfiles(account, selection).Select(profile => profile.Id));
        var request = Model.SelectionRequest(set, selection, Model.DefaultDataTypes);
        var user = Assert.Single(request.Users);
        Assert.Equal(("u1", "new"), (user.Account, user.Target));
        Assert.Equal(["p1", "p2"], user.Profiles.Select(profile => profile.Profile));
        // Preferences start off: only watch history is carried and chosen.
        Assert.All(user.Profiles, profile => Assert.Equal(["watch_history"], profile.DataTypes));
    }

    [Fact]
    public void TheRequestCarriesThePreviewTargetsAndEachSectionsIds()
    {
        var set = Set(BackupJson.Section("settings", BackupJson.Item("general")), BackupJson.Section("users",
            BackupJson.Account("u2", "42", BackupJson.Profile("p3", target: "17"))));
        var index = Model.Index(set);
        var selection = Model.Choose(index, new HashSet<string>(), Model.ToggleKeys("users", index["users:u2"].Item).Append("settings:general").ToArray()).Selection;
        var request = Model.SelectionRequest(set, selection, new HashSet<string> { "watch_history", "preferences" });
        Assert.Equal(["general"], request.Settings);
        var user = Assert.Single(request.Users);
        Assert.Equal(("u2", "42"), (user.Account, user.Target));
        Assert.Equal(("p3", "17"), (user.Profiles[0].Profile, user.Profiles[0].Target));
        Assert.Equal(["watch_history", "preferences"], user.Profiles[0].DataTypes);
    }

    [Fact]
    public void AReloadedPreviewKeepsOnlyWhatItStillOffers()
    {
        var first = Model.Index(Set(BackupJson.Section("settings", BackupJson.Item("general"), BackupJson.Item("email"))));
        var chosen = Model.Choose(first, new HashSet<string>(), ["settings:general", "settings:email"]).Selection;
        var reloaded = Model.Index(Set(BackupJson.Section("settings", BackupJson.Item("general"), BackupJson.Item("email", status: "on_this_server", selectable: false, extra: ",\"reason\":\"same\""))));
        Assert.Equal(["settings:general"], Model.Effective(reloaded, chosen));
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("missing")]
    [InlineData("new_requirement")]
    public void ReloadRemovesChoicesWithUnsatisfiedRequirementsTransitively(string change)
    {
        var chosen = Model.Choose(Model.Index(Library()), new HashSet<string>(), ["home_sections:section:9"]).Selection;
        var libraries = change == "missing" ? BackupJson.Section("libraries") : BackupJson.Section("libraries",
            BackupJson.Item("4", "library", selectable: change != "blocked"), BackupJson.Item("5", "library"));
        var reloaded = Set(libraries,
            BackupJson.Section("collections", BackupJson.Item("c1", "collection", requires: change == "new_requirement" ? "\"libraries:5\"" : "\"libraries:4\"")),
            BackupJson.Section("home_sections", BackupJson.Item("section:9", "home_section", requires: "\"collections:c1\"")));
        var effective = Model.Effective(Model.Index(reloaded), chosen);
        Assert.DoesNotContain("collections:c1", effective);
        Assert.DoesNotContain("home_sections:section:9", effective);
        Assert.DoesNotContain("libraries:5", effective); // Reload never silently adds a new dependency.
        var request = Model.SelectionRequest(reloaded, effective, Model.DefaultDataTypes);
        Assert.Empty(request.Collections);
        Assert.Empty(request.HomeSections);
    }

    [Fact]
    public void SectionsGroupBlockedReasonsLargestFirstAndCountStates()
    {
        var items = Set(BackupJson.Section("libraries",
            BackupJson.Item("1", "library", "folder_missing", false, extra: ",\"reason\":\"folder_missing\""),
            BackupJson.Item("2", "library", "folder_missing", false, extra: ",\"reason\":\"folder_missing\""),
            BackupJson.Item("3", "library", "ambiguous", false),
            BackupJson.Item("4", "library", "on_this_server", false, extra: ",\"reason\":\"same\""),
            BackupJson.Item("5", "library", "not_on_this_server"))).Sections![0].Items!;
        var groups = Model.GroupSection(items);
        Assert.Equal(["5"], groups.Restorable.Select(item => item.Id));
        Assert.Equal(["folder_missing", "status:ambiguous"], groups.Blocked.Select(group => group.Key));
        Assert.Equal(["4"], groups.Same.Select(item => item.Id));
        var counts = Model.CountStates(items);
        Assert.Equal(3, counts[AdminRestoreItemState.Blocked]);
        Assert.DoesNotContain("web.admin", Model.StateCountsText(counts));
        Assert.DoesNotContain("web.admin", groups.Blocked[0].Title);
    }

    [Fact]
    public void PlanAndCaveatsAreCatalogSentences()
    {
        var set = Set(BackupJson.Section("settings", BackupJson.Item("general", extra:
            ",\"restart_required\":true,\"settings\":[{\"key\":\"server.port\",\"class\":\"portable\",\"status\":\"differs\",\"masked\":false,\"backup_value\":\"1\",\"current_value\":\"2\",\"restart_required\":true}," +
            "{\"key\":\"smtp.password\",\"class\":\"external_service\",\"status\":\"needs_another_key\",\"masked\":true,\"backup_value\":\"\",\"current_value\":\"\",\"restart_required\":false}]")
            .Replace("\"restart_required\":false,\"external_service\"", "\"restart_required\":true,\"external_service\"")));
        var selection = Model.Choose(Model.Index(set), new HashSet<string>(), ["settings:general"]).Selection;
        var plan = Model.Plan(set, selection, Model.DefaultDataTypes);
        Assert.Equal("settings", Assert.Single(plan).Section);
        var caveats = Model.Caveats(set, selection);
        Assert.Contains(caveats, caveat => caveat.Key.EndsWith(":key", StringComparison.Ordinal));
        Assert.Contains(caveats, caveat => caveat.Key.EndsWith(":restart", StringComparison.Ordinal) && caveat.Text.Contains("server.port", StringComparison.Ordinal));
        Assert.All(plan.SelectMany(group => group.Sentences).Concat(caveats.Select(caveat => caveat.Text)), text => Assert.DoesNotContain("web.admin", text));
        var masked = set.Sections![0].Items![0].Settings![1];
        Assert.Equal(AdminText.T("web.admin.backups.restore.setting.otherKey"), Model.SettingValues(masked).Backup);
    }

    [Fact]
    public void ReportHeadlineAndRestartLinesUseOfficialText()
    {
        var headline = Model.ReportHeadline(new AdminBackupRestoreCounts { Restored = 2, Unchanged = 0, LeftOut = 1, Failed = 0 });
        Assert.DoesNotContain("web.admin", headline);
        Assert.NotEqual("web.admin.backups.restore.reportError.restoreSetChanged", Model.ReportErrorSentence("restore_set_changed"));
        Assert.NotEqual("web.admin.backups.restore.reason.overRestoreBudget", Model.ReasonSentence("over_restore_budget"));
    }
}
