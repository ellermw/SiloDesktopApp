using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Model = SiloPlayer.Core.Services.Admin.AdminBackupRestoreModel;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// Backups against official Silo Server 8d253adbf: the restore change codes, the account labels, the paused schedule line, the plain-text rules
/// for third-party names, and the strings that changed since the page was first built. Synthetic data only.
/// </summary>
public sealed class AdminBackupsRefreshTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly AdminBackupRestoreSourceRef Source = AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId);

    private static AdminBackupRestoreSet Parse(params string[] sections) => JsonSerializer.Deserialize<AdminBackupRestoreSet>(BackupJson.RestoreSet(string.Join(",", sections)), Json)!;

    private static AdminBackupRestoreItem Section(string changes, string extra = "")
    {
        var set = Parse(BackupJson.Section("home_sections", BackupJson.Item("section:9", "home_section", "differs",
            extra: ",\"home_section\":{\"scope\":\"home\",\"section_type\":\"recently_released\",\"local_id\":\"7\",\"changes\":[" + changes + "]" + extra + "}")));
        AdminBackupValidation.Require(set, Source);
        return set.Sections![0].Items![0];
    }

    private static AdminBackupRestoreItem Template(string changes)
    {
        var set = Parse(BackupJson.Section("collections", BackupJson.Item("t1", "template_collection", "differs",
            extra: ",\"collection\":{\"library_ids\":[],\"items\":0,\"missing_items\":0,\"unknown_items\":0,\"local_id\":\"3\",\"template_id\":\"x\",\"changes\":[" + changes + "]}")));
        AdminBackupValidation.Require(set, Source);
        return set.Sections![0].Items![0];
    }

    // ── Change codes ─────────────────────────────────────────────────

    public static TheoryData<string, string> OfficialChangeWords => new()
    {
        { "type", "Type" }, { "title", "Title" }, { "title_translation", "Title translation" }, { "position", "Position" }, { "featured", "Featured" },
        { "item_limit", "Item limit" }, { "enabled", "Enabled" }, { "web_only", "Web only" }, { "collection_replaced", "Collection replaced" },
        { "configuration", "Configuration" }, { "description", "Description" }, { "visibility", "Visibility" }, { "sort_order", "Sort order" },
        { "item_sort", "Item sort" }, { "source", "Source" }, { "sync_schedule", "Sync schedule" }, { "poster", "Poster" }, { "backdrop", "Backdrop" },
        { "libraries", "Libraries" },
    };

    [Theory]
    [MemberData(nameof(OfficialChangeWords))]
    public void EveryOfficialChangeCodeShowsItsEnglishWordWhetherASectionOrATemplateCollectionSendsIt(string code, string word)
    {
        Assert.Equal(word, Model.ChangeLabel(Section("\"" + code + "\""), code));
        Assert.Equal(word, Model.ChangeLabel(Template("\"" + code + "\""), code));
    }

    [Theory]
    [InlineData(1, "1 collection item")]
    [InlineData(2, "2 collection items")]
    [InlineData(37, "37 collection items")]
    public void TheCollectionItemsCodeTakesItsCountFromTheHomeSection(long missing, string expected)
    {
        var item = Section("\"title\",\"collection_items\"", ",\"missing_collection_items\":" + missing);
        Assert.Equal(expected, Model.ChangeLabel(item, "collection_items"));
        Assert.Equal(["Title", expected], Model.Changes(item).Select(change => Model.ChangeLabel(item, change)));
    }

    [Fact]
    public void ACollectionItemsCodeWithNoCountReadsZeroLikeTheOfficialClient()
    {
        Assert.Equal("0 collection items", Model.ChangeLabel(Section("\"collection_items\""), "collection_items"));
        // A template collection carries no count at all.
        Assert.Equal("0 collection items", Model.ChangeLabel(Template("\"collection_items\""), "collection_items"));
    }

    [Theory]
    [InlineData("future_code", "future_code")]
    [InlineData("item limit", "item limit")]            // an older server's English words
    [InlineData("3 collection items", "3 collection items")]
    [InlineData("title translation", "title translation")]
    [InlineData("toString", "toString")]
    [InlineData("", "")]
    public void ACodeThisClientDoesNotKnowShowsAsSentNeverAsAKey(string sent, string shown)
    {
        var item = Section("\"" + sent + "\"");
        Assert.Equal(shown, Model.ChangeLabel(item, sent));
        Assert.DoesNotContain("web.admin.backups", Model.ChangeLabel(item, sent), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCodeCannotFakeALineOrReorderTheText()
    {
        var shown = Model.ChangeLabel(Section("\"x\""), "line\none \u202Eevil\u2069\u0007");
        Assert.Equal("line one evil", shown);
    }

    // ── Contract ─────────────────────────────────────────────────────

    [Fact]
    public void TheNewFormIsReadAndTheOlderFormIsStillAccepted()
    {
        var current = Section("\"title\",\"collection_items\"", ",\"missing_collection_items\":4");
        Assert.Equal(4, current.HomeSection!.MissingCollectionItems);
        Assert.Equal(["title", "collection_items"], current.HomeSection.Changes);
        // A server before the change codes sends English words and no count: still a valid answer, shown as sent.
        var older = Section("\"title\",\"item limit\",\"3 collection items\"");
        Assert.Null(older.HomeSection!.MissingCollectionItems);
        Assert.Equal(["Title", "item limit", "3 collection items"], Model.Changes(older).Select(change => Model.ChangeLabel(older, change)));
    }

    [Fact]
    public void AMalformedChangeListIsRefusedWhileNewCodesAreNot()
    {
        Assert.Throws<InvalidDataException>(() => Section("\"title\",null"));
        Assert.Throws<InvalidDataException>(() => Template("null"));
        Assert.Throws<InvalidDataException>(() => Section("\"title\"", ",\"missing_collection_items\":-1"));
        Assert.Equal("future_code", Model.ChangeLabel(Section("\"future_code\""), "future_code"));
    }

    [Fact]
    public void ARestoreSetWithoutChangesOrCountsStillValidates()
    {
        var item = Section("");
        Assert.Empty(item.HomeSection!.Changes!);
        Assert.Empty(Model.Changes(item));
    }

    // ── Accounts ─────────────────────────────────────────────────────

    private static AdminBackupRestoreItem Account(string label, string username, string email, string? localUsername, string target = "42")
    {
        var set = Parse(BackupJson.Section("users", BackupJson.Item("u1", "account", "differs", extra:
            ",\"account\":{\"username\":" + JsonSerializer.Serialize(username) + ",\"email\":" + JsonSerializer.Serialize(email) + ",\"role\":\"user\",\"admin\":false,\"match\":\"username_email\",\"target\":\"" + target + "\"" +
            (localUsername == null ? "" : ",\"local_username\":" + JsonSerializer.Serialize(localUsername)) + ",\"policy_differences\":[],\"libraries_left_out\":0,\"profile_room\":3,\"profiles\":[]}")));
        var item = set.Sections![0].Items![0] with { Label = label };
        return item;
    }

    [Fact]
    public void AnAccountLabeledByItsEmailShowsTheEmailOnce()
    {
        // An invited account's username is its address, and an account with no username is labeled by its email.
        var invited = Account("a@example.test", "a@example.test", "a@example.test", null);
        Assert.Equal("", Model.AccountContact(invited, null));
        var byEmail = Account("b@example.test", "", "b@example.test", null);
        Assert.Equal("", Model.AccountContact(byEmail, null));
        var named = Account("Alice", "alice", "alice@example.test", null);
        Assert.Equal("alice · alice@example.test", Model.AccountContact(named, null));
        Assert.Equal("1 play to add · alice · alice@example.test", Model.AccountContact(named, "1 play to add"));
        var sameUsername = Account("alice", "alice", "alice@example.test", null);
        Assert.Equal("alice@example.test", Model.AccountContact(sameUsername, null));
    }

    [Fact]
    public void AMergeTargetWithNoUsernameIsNamedByItsEmailAndAnOlderServerFallsBack()
    {
        // The server now sends the matched account's email in local_username when it has no username.
        Assert.Equal("sam@example.test", Model.MergeDestination(Account("Sam", "sam-old", "sam@example.test", "sam@example.test")));
        // An older server leaves local_username empty: the backup's own username, else the row's name.
        Assert.Equal("sam-old", Model.MergeDestination(Account("Sam", "sam-old", "sam@example.test", null)));
        Assert.Equal("Sam", Model.MergeDestination(Account("Sam", "", "sam@example.test", "")));
    }

    // ── Rows ─────────────────────────────────────────────────────────

    [Fact]
    public void ASectionRowNamesItsKindWithTheHomeRowCatalogNotAGuessedCapitalization()
    {
        var set = Parse(BackupJson.Section("home_sections", BackupJson.Item("section:9", "home_section", "differs",
            extra: ",\"home_section\":{\"scope\":\"home\",\"section_type\":\"recently_released\",\"local_id\":\"7\"}"),
            BackupJson.Item("section:10", "home_section", "differs", extra: ",\"home_section\":{\"scope\":\"home\",\"section_type\":\"from_the_future\"}")));
        var names = Model.LibraryNames(set, []);
        var items = set.Sections![0].Items!;
        Assert.Equal("New releases · replaces the section here", Model.RowSecondary(items[0], names));
        // A kind this client has no name for reads "Row", never its raw type key.
        Assert.DoesNotContain("from_the_future", Model.RowSecondary(items[1], names), StringComparison.OrdinalIgnoreCase);
    }

    // ── Plain text for third-party names ─────────────────────────────

    [Theory]
    [InlineData("Movies", "Movies")]
    [InlineData("  padded  ", "padded")]
    [InlineData("two\nlines\r\nand\ttabs", "two lines and tabs")]
    [InlineData("para\u2029graph\u2028sep\u0085next", "para graph sep next")]
    [InlineData("rlo \u202Egnp.exe\u202C end", "rlo gnp.exe end")]
    [InlineData("iso \u2066a\u2069 \u2067b\u2069 \u2068c\u2069 \u200Ex\u200Fy\u061Cz", "iso a b c xyz")]
    [InlineData("bell\u0007nul\u0000esc\u001B", "bellnulesc")]
    [InlineData("zero‍width joined 👩‍👩‍👧", "zero‍width joined 👩‍👩‍👧")]
    public void ThirdPartyTextIsPlainDisplayText(string value, string expected) => Assert.Equal(expected, AdminBackupText.Plain(value));

    [Fact]
    public void PlainTextIsBoundedWithoutSplittingACharacterAndNullIsEmpty()
    {
        Assert.Equal("", AdminBackupText.Plain(null));
        var bounded = AdminBackupText.Plain(new string('a', 500));
        Assert.Equal(AdminBackupText.PlainLimit + 1, bounded.Length);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
        var emoji = AdminBackupText.Plain(string.Concat(Enumerable.Repeat("😀", 150)));
        Assert.True(emoji.EnumerateRunes().All(rune => rune.Value is 0x1F600 or '…'));
        Assert.Equal("a…", AdminBackupText.Plain("abc", 1));
        Assert.Equal("abc", AdminBackupText.Plain("abc", 3));
        // A lone surrogate is not valid text.
        Assert.Equal("a\uFFFDb", AdminBackupText.Plain("a\uD800b"));
    }

    [Fact]
    public void ServerMessagesAreNeutralizedBeforeTheyAreRedactedAndBounded()
    {
        var safe = AdminBackupValidation.SafeServerText("first line\nsecond \u202Eline\u2069 secret-value-123 " + new string('x', 600), "secret-value-123");
        Assert.DoesNotContain("\n", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value-123", safe, StringComparison.Ordinal);
        Assert.Contains("first line second line", safe, StringComparison.Ordinal);
        Assert.True(safe.Length <= 501);
    }

    private static string[] SentencesAndCaveats(AdminBackupRestoreSet set, params string[] keys)
    {
        var selection = Model.Choose(Model.Index(set), new HashSet<string>(), keys).Selection;
        var plan = Model.Plan(set, selection, Model.DefaultDataTypes).SelectMany(group => group.Sentences);
        return plan.Concat(Model.Caveats(set, selection).Select(caveat => caveat.Label + ": " + caveat.Text)).ToArray();
    }

    [Fact]
    public void ReviewSentencesAndCaveatsNeverCarryControlOrDirectionCharactersFromNames()
    {
        const string evil = "Movies\n\u202Egnp.exe\u2029 extra";
        var set = Parse(
            BackupJson.Section("libraries", BackupJson.Item("4", "library", "not_on_this_server", extra: ",\"library\":{\"type\":\"movies\",\"paths\":[\"/media\"]}")),
            BackupJson.Section("users", BackupJson.Account("u1", "new", BackupJson.Profile("p1", primary: true))));
        var libraries = set.Sections![0].Items![0] with { Label = evil, MissingBlobs = ["a"] };
        var account = set.Sections[1].Items![0] with { Label = evil };
        var edited = set with { Sections = [set.Sections[0] with { Items = [libraries] }, set.Sections[1] with { Items = [account with { Account = account.Account! with
            { Profiles = [account.Account.Profiles![0] with { Name = evil, NoLibraryAccess = true }] } }] }] };
        var lines = SentencesAndCaveats(edited, "libraries:4", Model.ProfileKey("u1", "p1"));
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.False(line.Any(character => char.IsControl(character))));
        Assert.All(lines, line => Assert.False(line.Any(character => character is '\u202E' or '\u2029' or '\u2028' or '\u2066' or '\u2069')));
        Assert.Contains(lines, line => line.Contains("Movies gnp.exe extra", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUploadedBackupsServerNameIsPlainTextInItsLabel()
    {
        var upload = JsonSerializer.Deserialize<AdminBackupUpload>(BackupJson.Upload("complete", extra: ",\"digest\":\"" + BackupJson.Digest + "\",\"source\":{\"server_id\":\"s-1\",\"server_name\":\"Home\\n\\u202Eevil\",\"silo_version\":\"1.0.0\",\"created_at\":\"2026-11-15T04:00:00.000Z\"}"), Json)!;
        var label = Present.UploadLabel(upload);
        Assert.Contains("Home evil", label, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", label, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", label, StringComparison.Ordinal);
    }

    // ── Paused schedule, destination, toggles ────────────────────────

    private static AdminBackupWarning Warning(string code) => new() { Code = code, Message = "synthetic" };

    [Theory]
    [InlineData(true, "no_recent_backup", false)]
    [InlineData(false, "no_recent_backup", true)]
    [InlineData(true, "sqlite_user_store", true)]
    [InlineData(true, "key_not_confirmed", true)]
    [InlineData(true, "no_backups_at_destination", false)]
    public void BackupsArePausedWithoutStorageOnSqliteOrWithAnUnconfirmedKey(bool configured, string code, bool paused) =>
        Assert.Equal(paused, Present.BackupsPaused(configured, [Warning(code)]));

    [Fact]
    public void APausedServerForecastsNoNextRunButStillSaysItsBackupsAreOff()
    {
        var now = DateTimeOffset.Parse("2026-11-16T12:00:00Z");
        var next = now.AddHours(16);
        Assert.NotNull(Present.SchedulePhrases(now.AddDays(-1), true, paused: false, busy: false, next, now).Next);
        Assert.Null(Present.SchedulePhrases(now.AddDays(-1), true, paused: true, busy: false, next, now).Next);
        Assert.Equal(AdminText.T("web.admin.backups.schedule.off"), Present.SchedulePhrases(now.AddDays(-1), false, paused: true, busy: false, next, now).Next);
        Assert.Null(Present.SchedulePhrases(null, true, paused: true, busy: false, null, now).Next);
    }

    [Theory]
    [InlineData(0, "other")]
    [InlineData(1, "exactlyOne")]
    [InlineData(2, "exactlyTwo")]
    [InlineData(3, "other")]
    public void ToggleLabelsPickTheirWordingForTheExactNumberOfItems(int count, string amount)
    {
        var values = new Dictionary<string, object?> { ["amount"] = amount };
        var singular = count == 1;
        Assert.Equal(singular ? "Show the file" : "Show the files", AdminText.T("web.admin.backups.warnings.showFiles", values));
        Assert.Equal(singular ? "Hide the account" : "Hide the accounts", AdminText.T("web.admin.backups.warnings.hideAccounts", values));
        Assert.Equal(singular ? "Show file" : "Show files", AdminText.T("web.admin.backups.warnings.showGroupFiles", values));
    }

    [Theory]
    [InlineData(1, "Show it", "Hide it")]
    [InlineData(2, "Show them", "Hide them")]
    [InlineData(0, "Show them", "Hide them")]
    public void RestoreToggleLabelsAgreeWithTheNumberOfItemsTheyCover(long count, string show, string hide)
    {
        Assert.Equal(show, AdminText.T("web.admin.backups.restore.choose.showThem", AdminBackupText.Counted(count)));
        Assert.Equal(hide, AdminText.T("web.admin.backups.restore.choose.hideThem", AdminBackupText.Counted(count)));
    }

    [Fact]
    public void BlockedGroupTitlesUseTheSingularFormForOneItem()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("1 can't be restored — its library isn't on this server", AdminText.T("web.admin.backups.restore.blocked.libraryNotMapped", AdminBackupText.Counted(1)));
            Assert.Equal("2 can't be restored — their library isn't on this server", AdminText.T("web.admin.backups.restore.blocked.libraryNotMapped", AdminBackupText.Counted(2)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ── The official strings ─────────────────────────────────────────

    [Fact]
    public void EveryBackupsStringThatChangedSinceThePinIsInTheCatalogVerbatim()
    {
        Assert.Equal(51, AdminBackupsOfficialStrings.ChangedSincePin.Length);
        foreach (var (key, pattern, _) in AdminBackupsOfficialStrings.ChangedSincePin)
        {
            Assert.True(AdminText.Has(key), key);
            Assert.Equal(pattern, AdminText.Pattern(key));
        }
        Assert.Equal(25, AdminBackupsOfficialStrings.ChangedSincePin.Count(entry => entry.Added));
    }

    [Fact]
    public void EveryChangeCodeHasAnOfficialLabelInTheCatalog()
    {
        foreach (var code in new[] { "type", "title", "title_translation", "position", "featured", "item_limit", "enabled", "web_only", "collection_replaced", "configuration",
                     "description", "visibility", "sort_order", "item_sort", "source", "sync_schedule", "poster", "backdrop", "libraries" })
            Assert.True(AdminText.Has("web.admin.backups.restore.rows.change." + Model.Camel(code)), code);
        Assert.True(AdminText.Has("web.admin.backups.restore.rows.change.collectionItems"));
    }
}
