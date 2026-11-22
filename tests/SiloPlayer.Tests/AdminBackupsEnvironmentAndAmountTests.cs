using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Model = SiloPlayer.Core.Services.Admin.AdminBackupRestoreModel;

namespace SiloPlayer.Tests;

/// <summary>
/// Official Silo Server fd4282d0a (#4667, a selective restore keeps what the environment sets) and 5641447d6 (#4741, restore and backup wording
/// chosen for the exact number of items), read at 93794454b. Synthetic data only.
/// </summary>
public sealed class AdminBackupsEnvironmentAndAmountTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static AdminBackupRestoreSet Set(params string[] sections) => JsonSerializer.Deserialize<AdminBackupRestoreSet>(BackupJson.RestoreSet(string.Join(",", sections)), Json)!;

    private static string SettingJson(string key, string status, bool masked = false, bool restart = false, string backup = "b", string current = "a") =>
        "{\"key\":\"" + key + "\",\"class\":\"portable\",\"status\":\"" + status + "\",\"masked\":" + (masked ? "true" : "false") + ",\"backup_value\":\"" + backup +
        "\",\"current_value\":\"" + current + "\",\"restart_required\":" + (restart ? "true" : "false") + "}";

    private static AdminBackupRestoreSet SettingsPage(params string[] settings) => Set(BackupJson.Section("settings", BackupJson.Item("general", extra:
        ",\"settings\":[" + string.Join(",", settings) + "]")));

    // ── #4667: a setting the environment sets ────────────────────────

    [Fact]
    public void AManagedSettingKeepsItsCurrentValueAndSaysTheEnvironmentSetsIt()
    {
        var set = SettingsPage(SettingJson("general.server_name", "managed_by_environment", backup: "Backup name", current: "Env name"));
        AdminBackupValidation.Require(set, AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId)); // an additive status never fails the preview
        var values = Model.SettingValues(set.Sections![0].Items![0].Settings![0]);
        Assert.Equal("Env name (kept: environment)", values.Now);
        Assert.Equal("Backup name", values.Backup);
    }

    [Fact]
    public void AManagedMaskedSettingShowsSetAndNeverAValue()
    {
        var set = SettingsPage(SettingJson("smtp.password", "managed_by_environment", masked: true, backup: "", current: ""));
        var values = Model.SettingValues(set.Sections![0].Items![0].Settings![0]);
        Assert.Equal(AdminText.T("web.admin.backups.restore.setting.managedByEnvironment", new { value = AdminText.T("web.admin.backups.restore.setting.set") }), values.Now);
        Assert.Equal(AdminText.T("web.admin.backups.restore.setting.differentEncrypted"), values.Backup);
    }

    [Fact]
    public void TheReviewNamesTheSettingsTheEnvironmentKeeps()
    {
        var one = SettingsPage(SettingJson("general.server_name", "managed_by_environment"), SettingJson("general.other", "differs"));
        var selection = Model.Choose(Model.Index(one), new HashSet<string>(), ["settings:general"]).Selection;
        var caveat = Assert.Single(Model.Caveats(one, selection), item => item.Key.EndsWith(":environment", StringComparison.Ordinal));
        Assert.Equal("1 setting keeps its current value because this server's environment sets it, or part of its connection.", caveat.Text);

        var two = SettingsPage(SettingJson("smtp.host", "managed_by_environment"), SettingJson("smtp.port", "managed_by_environment"));
        selection = Model.Choose(Model.Index(two), new HashSet<string>(), ["settings:general"]).Selection;
        caveat = Assert.Single(Model.Caveats(two, selection), item => item.Key.EndsWith(":environment", StringComparison.Ordinal));
        Assert.Equal("2 settings keep their current values because this server's environment sets them, or part of their connection.", caveat.Text);

        var none = SettingsPage(SettingJson("general.other", "differs"));
        selection = Model.Choose(Model.Index(none), new HashSet<string>(), ["settings:general"]).Selection;
        Assert.DoesNotContain(Model.Caveats(none, selection), item => item.Key.EndsWith(":environment", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("differs", true, true)]
    [InlineData("cleared", true, true)]
    [InlineData("managed_by_environment", true, false)]
    [InlineData("kept_stricter", true, false)]
    [InlineData("unverified_backup", true, false)]
    [InlineData("needs_another_key", true, false)]
    [InlineData("differs", false, false)]
    public void OnlyASettingARestoreWritesCarriesTheRestartMarker(string status, bool restart, bool marked)
    {
        var setting = SettingsPage(SettingJson("general.port", status, restart: restart)).Sections![0].Items![0].Settings![0];
        Assert.Equal(marked, Model.ShowsRestartMarker(setting));
    }

    [Fact]
    public void AnItemThePreviewBlocksForTheEnvironmentReadsItsOfficialSentenceAndHeading()
    {
        var set = Set(BackupJson.Section("settings", BackupJson.Item("general", status: "differs", selectable: false, extra: ",\"reason\":\"managed_by_environment\"")));
        var item = set.Sections![0].Items![0];
        Assert.Equal(AdminRestoreItemState.Blocked, Model.State(item));
        Assert.Equal("This server's environment sets the settings that differ, or part of their connection, so this server keeps its current values.", Model.KnownReasonSentence("managed_by_environment"));
        var group = Assert.Single(Model.GroupSection(set.Sections[0].Items!).Blocked);
        Assert.Equal("1 can't be restored — this server's environment sets the settings that differ, or part of their connection", group.Title);
    }

    // ── #4741: wording for the exact number of items ─────────────────

    [Theory]
    [InlineData(0, "other")]
    [InlineData(1, "exactlyOne")]
    [InlineData(2, "exactlyTwo")]
    [InlineData(3, "other")]
    [InlineData(21, "other")]
    [InlineData(101, "other")]
    public void TheAmountOfAMessageIsTheExactCountNeverAPluralCategory(long count, string amount) =>
        Assert.Equal(amount, AdminBackupText.ExactAmount(count));

    [Theory]
    [InlineData(1, "Show it", "Hide it")]
    [InlineData(2, "Show them", "Hide them")]
    [InlineData(21, "Show them", "Hide them")]
    public void TheRestoreShowAndHideLabelsSelectOnTheExactAmount(long count, string show, string hide)
    {
        Assert.Equal(show, AdminText.T("web.admin.backups.restore.choose.showThem", AdminBackupText.Amount(count)));
        Assert.Equal(hide, AdminText.T("web.admin.backups.restore.choose.hideThem", AdminBackupText.Amount(count)));
    }

    [Fact]
    public void AutomaticallyChoosingOneOrSeveralRequirementsAgreesWithTheirNumber()
    {
        var set = Set(
            BackupJson.Section("libraries", BackupJson.Item("4", "library", "not_on_this_server", extra: ",\"library\":{\"type\":\"movies\",\"paths\":[\"/media/movies\"]}")),
            BackupJson.Section("collections", BackupJson.Item("c1", "collection", "not_on_this_server", requires: "\"libraries:4\"")),
            BackupJson.Section("home_sections", BackupJson.Item("section:9", "home_section", "not_on_this_server", requires: "\"collections:c1\"",
                extra: ",\"home_section\":{\"scope\":\"home\",\"section_type\":\"collection\"}")));
        var index = Model.Index(set);
        var one = Model.Choose(index, new HashSet<string>(), ["collections:c1"]);
        Assert.EndsWith(", which is needed by what you chose.", one.Notice);
        var two = Model.Choose(index, new HashSet<string>(), ["home_sections:section:9"]);
        Assert.EndsWith(", which are needed by what you chose.", two.Notice);

        var unchooseOne = Model.Unchoose(index, one.Selection, ["libraries:4"]);
        Assert.EndsWith(", which needs what you unchose.", unchooseOne.Notice);
        var unchooseTwo = Model.Unchoose(index, two.Selection, ["libraries:4"]);
        Assert.EndsWith(", which need what you unchose.", unchooseTwo.Notice);
    }

    [Theory]
    [InlineData("too_large", "x", 1, "its data is too large for the restore set")]
    [InlineData("too_large", "x", 2, "their data is too large for the restore set")]
    [InlineData("unreadable", "reading config: boom", 1, "its config couldn't be read")]
    [InlineData("unreadable", "reading config: boom", 3, "their config couldn't be read")]
    [InlineData("unreadable", "boom", 1, "its data couldn't be read")]
    [InlineData("unreadable", "boom", 2, "their data couldn't be read")]
    public void AnOmissionSummaryAgreesWithTheNumberOfOmittedItems(string reason, string cause, int count, string expected)
    {
        var omitted = Enumerable.Range(1, count).Select(index => new AdminBackupOmitted { Section = "libraries", Id = "i" + index, Label = "Item " + index, Reason = reason, Cause = cause }).ToArray();
        var group = Assert.Single(AdminBackupPresentation.GroupOmissions(omitted));
        Assert.Equal(expected, group.Summary);
    }

    [Fact]
    public void TheChangedStringsAreTheOfficialTextVerbatim()
    {
        var official = JsonDocument.Parse(OfficialAmountStrings).RootElement;
        Assert.Equal(7, official.EnumerateObject().Count());
        foreach (var property in official.EnumerateObject())
            Assert.Equal(Render(property.Value), AdminText.Pattern(property.Name));
    }

    private static string Render(JsonElement nodes) => string.Concat(nodes.EnumerateArray().Select(node =>
    {
        var value = node.GetProperty("value");
        switch (node.GetProperty("type").GetInt32())
        {
            case 0: return value.GetString()!;
            case 1: return "{" + value.GetString() + "}";
            case 5 or 6:
                var kind = node.GetProperty("type").GetInt32() == 6 ? "plural" : "select";
                return "{" + value.GetString() + ", " + kind + ", " + string.Join(" ", node.GetProperty("options").EnumerateObject()
                    .Select(option => option.Name + " {" + Render(option.Value.GetProperty("value")) + "}")) + "}";
            default: throw new InvalidDataException("unexpected message node " + node.GetProperty("type"));
        }
    }));

    // Silo-Server web/src/locales/en.json at 93794454b84aa818979bee19b8e192944395635a: the seven keys 5641447d6 moved from a plural on count to a select on amount.
    private const string OfficialAmountStrings = """
{"web.admin.backups.restore.choose.hideThem":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"Hide it"}]},"exactlyTwo":{"value":[{"type":0,"value":"Hide them"}]},"other":{"value":[{"type":0,"value":"Hide them"}]}}}],"web.admin.backups.restore.choose.showThem":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"Show it"}]},"exactlyTwo":{"value":[{"type":0,"value":"Show them"}]},"other":{"value":[{"type":0,"value":"Show them"}]}}}],"web.admin.backups.restore.notice.alsoChose":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"Also chose "},{"type":1,"value":"names"},{"type":0,"value":", which is needed by what you chose."}]},"exactlyTwo":{"value":[{"type":0,"value":"Also chose "},{"type":1,"value":"names"},{"type":0,"value":", which are needed by what you chose."}]},"other":{"value":[{"type":0,"value":"Also chose "},{"type":1,"value":"names"},{"type":0,"value":", which are needed by what you chose."}]}}}],"web.admin.backups.restore.notice.alsoUnchose":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"Also unchose "},{"type":1,"value":"names"},{"type":0,"value":", which needs what you unchose."}]},"exactlyTwo":{"value":[{"type":0,"value":"Also unchose "},{"type":1,"value":"names"},{"type":0,"value":", which need what you unchose."}]},"other":{"value":[{"type":0,"value":"Also unchose "},{"type":1,"value":"names"},{"type":0,"value":", which need what you unchose."}]}}}],"web.admin.backups.warnings.dataUnreadable":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"its data couldn't be read"}]},"exactlyTwo":{"value":[{"type":0,"value":"their data couldn't be read"}]},"other":{"value":[{"type":0,"value":"their data couldn't be read"}]}}}],"web.admin.backups.warnings.partUnreadable":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"its "},{"type":1,"value":"part"},{"type":0,"value":" couldn't be read"}]},"exactlyTwo":{"value":[{"type":0,"value":"their "},{"type":1,"value":"part"},{"type":0,"value":" couldn't be read"}]},"other":{"value":[{"type":0,"value":"their "},{"type":1,"value":"part"},{"type":0,"value":" couldn't be read"}]}}}],"web.admin.backups.warnings.tooLarge":[{"type":5,"value":"amount","options":{"exactlyOne":{"value":[{"type":0,"value":"its data is too large for the restore set"}]},"exactlyTwo":{"value":[{"type":0,"value":"their data is too large for the restore set"}]},"other":{"value":[{"type":0,"value":"their data is too large for the restore set"}]}}}]}
""";
}
