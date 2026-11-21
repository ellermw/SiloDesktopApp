using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

// The configuration form, presentation rules and untrusted-text handling, against official 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e.
public sealed class AdminPluginConfigDraftTests
{
    private static AdminPluginSchema Schema(string key, string json, AdminPluginForm? form = null) => new() { Key = key, JsonSchema = json, AdminForm = form };
    private static JsonElement J(object? value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void DeclaredStringStaysStringAndNumericClearIsExplicit()
    {
        using var draft = new AdminPluginConfigDraft(Schema("connection", """{"type":"object","properties":{"postal_code":{"type":"string"},"port":{"type":"integer"},"enabled":{"type":"boolean"}}}"""), null);
        var fields = draft.Form!.Fields;
        draft.Set(fields.Single(f => f.Key == "postal_code"), "00123"); draft.Set(fields.Single(f => f.Key == "port"), "");
        var body = draft.Build();
        Assert.Equal("00123", body.Value["postal_code"].GetString()); Assert.Equal(JsonValueKind.Null, body.Value["port"].ValueKind);
        Assert.False(body.Value["enabled"].GetBoolean());
    }

    [Fact]
    public void EmptiedStringFieldIsAnExplicitEmptyStringSoTheStoredValueIsCleared()
    {
        using var draft = new AdminPluginConfigDraft(Schema("c", """{"type":"object","properties":{"host":{"type":"string"}}}"""), new() { Value = new() { ["host"] = J("old.example.test") } });
        draft.Set(draft.Fields[0], "");
        Assert.Equal("", draft.Build().Value["host"].GetString());
    }

    [Fact]
    public void DraftStartsFromTheRawSavedValueAndIsNotDirtyUntilSomethingDiffers()
    {
        using var draft = new AdminPluginConfigDraft(Schema("c", """{"type":"object","properties":{"host":{"type":"string"},"on":{"type":"boolean"},"port":{"type":"integer"}}}"""),
            new() { Value = new() { ["host"] = J("  keep spaces  "), ["on"] = J(true), ["port"] = J(8080) } });
        Assert.False(draft.IsDirty);
        // WinUI reports a control's starting value as a change: setting the same value must never mark the form edited.
        foreach (var field in draft.Fields) draft.Set(field, draft.Get(field));
        Assert.False(draft.IsDirty);
        Assert.Equal("  keep spaces  ", draft.Build().Value["host"].GetString());
        draft.Set(draft.Fields[0], "other"); Assert.True(draft.IsDirty);
        draft.Set(draft.Fields[0], "  keep spaces  "); Assert.False(draft.IsDirty);
    }

    [Fact]
    public void StoredValueThatFailsAClientCheckStaysInTheFieldInsteadOfBeingBlanked()
    {
        var form = new AdminPluginForm { Fields = [new() { Key = "code", Label = "Code", Control = "TEXT", Validation = new() { Pattern = "^[A-Z]+$" } }] };
        using var draft = new AdminPluginConfigDraft(Schema("c", "{}", form), new() { Value = new() { ["code"] = J("lowercase-legacy") } });
        Assert.Equal("lowercase-legacy", draft.Get(draft.Fields[0]));
        Assert.Contains("code", draft.Errors().Keys);
    }

    [Fact]
    public void RedactedSecretNeverPopulatesAndClearRequiresExplicitChoice()
    {
        using var draft = new AdminPluginConfigDraft(Schema("secret", """{"type":"object","properties":{"password":{"type":"string","writeOnly":true}}}"""),
            new() { ConfiguredSecrets = ["password"], Value = new() { ["password"] = J("must-not-be-visible") } });
        var field = Assert.Single(draft.Fields);
        Assert.Equal("", draft.Get(field)); Assert.Empty(draft.Build().Value); Assert.False(draft.IsDirty);
        draft.ToggleClear("password"); Assert.Equal("password", Assert.Single(draft.Build().ClearSecrets)); Assert.True(draft.IsDirty);
        draft.Set(field, "synthetic-replacement"); Assert.Empty(draft.Build().ClearSecrets);
        Assert.Equal("synthetic-replacement", draft.Build().Value["password"].GetString());
        draft.Dispose(); Assert.False(draft.IsDirty); Assert.Empty(draft.Build().Value);
    }

    [Fact]
    public void SecretFieldNeverStartsFromASchemaDefault()
    {
        using var draft = new AdminPluginConfigDraft(Schema("s", """{"type":"object","properties":{"token":{"type":"string","format":"password","default":"leaked-default"}}}"""), null);
        Assert.Equal("", draft.Get(draft.Fields[0])); Assert.Empty(draft.Build().Value);
    }

    [Fact]
    public void RequiredSavedSecretCanBeKeptButCannotBeCleared()
    {
        using var draft = new AdminPluginConfigDraft(Schema("secret", """{"type":"object","required":["password"],"properties":{"password":{"type":"string","writeOnly":true}}}"""), new() { ConfiguredSecrets = ["password"] });
        draft.ToggleClear("password");
        Assert.Empty(draft.Build().ClearSecrets); Assert.Empty(draft.Build().Value); Assert.False(draft.CanClear("password"));
        Assert.Empty(draft.Errors());
    }

    [Fact]
    public void ManagedFieldsAreNeverSentNeverClearedAndCannotBeEdited()
    {
        using var draft = new AdminPluginConfigDraft(Schema("c", """{"type":"object","properties":{"host":{"type":"string"},"password":{"type":"string","writeOnly":true},"port":{"type":"integer"}}}"""),
            new() { ConfiguredSecrets = ["password"], Value = new() { ["host"] = J("managed.example.test"), ["port"] = J(1) } }, ["host", "password"]);
        draft.Set(draft.Fields.Single(f => f.Key == "host"), "attempted-change"); draft.ToggleClear("password");
        draft.Set(draft.Fields.Single(f => f.Key == "port"), "2");
        var body = draft.Build();
        Assert.Equal(["port"], body.Value.Keys.ToArray()); Assert.Empty(body.ClearSecrets);
        Assert.Equal("managed.example.test", draft.Get(draft.Fields.Single(f => f.Key == "host")));
        Assert.False(draft.AllManaged);
    }

    [Fact]
    public void AFormWhoseEveryFieldIsManagedOffersNoSave()
    {
        using var draft = new AdminPluginConfigDraft(Schema("c", """{"type":"object","properties":{"host":{"type":"string"}}}"""), null, ["host"]);
        Assert.True(draft.AllManaged);
    }

    [Fact]
    public void HiddenConditionalFieldsAreNotSentDoNotBlockSaveAndSectionsCanHideFields()
    {
        var form = new AdminPluginForm
        {
            Fields = [new() { Key = "mode" }, new() { Key = "endpoint", Required = true, ShowWhen = [new() { Field = "mode", Values = ["remote"] }] }, new() { Key = "region" }],
            Sections = [new() { Key = "advanced", FieldKeys = ["region"], ShowWhen = [new() { Field = "mode", Values = ["remote"] }] }],
        };
        using var draft = new AdminPluginConfigDraft(Schema("conditional", "{}", form), new() { Value = new() { ["region"] = J("old region") } });
        draft.Set(draft.Fields[0], "local");
        Assert.False(draft.Build().Value.ContainsKey("endpoint")); Assert.False(draft.Build().Value.ContainsKey("region"));
        Assert.Empty(draft.Errors()); // the required endpoint is hidden: an error nobody can see must not block Save
        draft.Set(draft.Fields[0], "remote");
        Assert.Contains("endpoint", draft.Errors().Keys); Assert.Equal("old region", draft.Build().Value["region"].GetString());
    }

    [Fact]
    public void ExplicitFormAddsMissingScalarFieldsAndUpgradesSecretMetadata()
    {
        using var draft = new AdminPluginConfigDraft(Schema("entry", """{"type":"object","properties":{"password":{"type":"string","format":"password"},"host":{"type":"string"}}}""",
            new() { Fields = [new() { Key = "password", Control = "TEXT" }] }), null);
        Assert.Equal(2, draft.Fields.Count); Assert.True(draft.Fields.Single(f => f.Key == "password").Secret);
    }

    [Fact]
    public void TypedMultiSelectUsesDeclaredItemsAndRetainsNumericLookingStrings()
    {
        var schema = Schema("lists", """{"type":"object","properties":{"numeric":{"type":"array","items":{"type":"integer"}},"strings":{"type":"array","items":{"type":"string"}}}}""",
            new() { Fields = [new() { Key = "numeric", Control = "MULTI_SELECT" }, new() { Key = "strings", Control = "MULTI_SELECT" }] });
        using var draft = new AdminPluginConfigDraft(schema, null); draft.Set(draft.Fields[0], new List<string> { "12", "3" }); draft.Set(draft.Fields[1], new List<string> { "001" });
        Assert.Equal(12, draft.Build().Value["numeric"][0].GetInt32()); Assert.Equal("001", draft.Build().Value["strings"][0].GetString());
    }

    [Fact]
    public void UntouchedMultiSelectGoesBackExactlyAsSaved()
    {
        var schema = Schema("lists", """{"type":"object","properties":{"ids":{"type":"array"}}}""", new() { Fields = [new() { Key = "ids", Control = "MULTI_SELECT" }] });
        using var draft = new AdminPluginConfigDraft(schema, new() { Value = new() { ["ids"] = J(new[] { 1, 2 }) } });
        var ids = draft.Build().Value["ids"]; Assert.Equal(JsonValueKind.Number, ids[0].ValueKind);
    }

    [Fact]
    public void ArrayItemTypeFollowsLocalRefsAndEnums()
    {
        var types = AdminPluginConfigDraft.ParseFieldTypes("""{"$defs":{"Id":{"type":"integer"}},"type":"object","properties":{"a":{"type":"array","items":{"$ref":"#/$defs/Id"}},"b":{"type":"array","items":{"enum":[true,false]}},"c":{"type":"array","items":{"type":["string","null"]}},"d":{"type":"array","items":{"enum":[1.5,2]}}}}""");
        Assert.Equal(AdminPluginFieldType.ArrayInteger, types["a"]); Assert.Equal(AdminPluginFieldType.ArrayBoolean, types["b"]);
        Assert.Equal(AdminPluginFieldType.Array, types["c"]); Assert.Equal(AdminPluginFieldType.ArrayNumber, types["d"]);
    }

    [Fact]
    public void UnsupportedObjectWithoutDescriptorHasExplicitUnavailableForm()
    {
        using var draft = new AdminPluginConfigDraft(Schema("complex", """{"type":"object","properties":{"nested":{"type":"object"}}}"""), null);
        Assert.Null(draft.Form); Assert.Empty(draft.Build().Value);
    }

    [Fact]
    public void ValidationUsesTheOfficialMessages()
    {
        var form = new AdminPluginForm { Fields = [
            new() { Key = "n", Label = "Port", Control = "NUMBER", Validation = new() { HasMin = true, Min = 1, HasMax = true, Max = 10 } },
            new() { Key = "s", Label = "Name", Control = "TEXT", Validation = new() { MinLength = 3, MaxLength = 5 } },
            new() { Key = "r", Label = "Req", Control = "TEXT", Required = true } ] };
        using var draft = new AdminPluginConfigDraft(Schema("c", "{}", form), null);
        draft.Set(draft.Fields[0], "x"); draft.Set(draft.Fields[1], "ab");
        var errors = draft.Errors();
        Assert.Equal("Port must be a number", errors["n"]); Assert.Equal("Name must be at least 3 characters", errors["s"]); Assert.Equal("Req is required", errors["r"]);
        draft.Set(draft.Fields[0], "11"); draft.Set(draft.Fields[1], "abcdef");
        Assert.Equal("Port must be ≤ 10", draft.Errors()["n"]); Assert.Equal("Name must be at most 5 characters", draft.Errors()["s"]);
    }

    [Fact]
    public void ANumberTooLargeOrNotFiniteNeverThrows()
    {
        var form = new AdminPluginForm { Fields = [new() { Key = "n", Label = "N", Control = "NUMBER" }] };
        using var draft = new AdminPluginConfigDraft(Schema("c", "{}", form), null);
        foreach (var text in new[] { "1e999", "NaN", "Infinity", "99999999999999999999999999", "-", "0x10" })
        {
            draft.Set(draft.Fields[0], text);
            _ = draft.Errors(); _ = draft.Build();
        }
    }

    // ── presentation ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://fixture.invalid/file?token=secret", true)]
    [InlineData("https://name:secret@fixture.invalid/file", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/secret", false)]
    [InlineData("ftp://fixture.invalid/", false)]
    [InlineData("https://fixture.invalid/support", true)]
    public void LinksOpenOnlyWhenHttpOrHttpsWithoutCredentials(string value, bool opens) => Assert.Equal(opens, AdminPluginPresentation.SafeLink(value) != null);

    [Fact]
    public void RedactionHappensBeforeTruncationSoACutNeverExposesASecret()
    {
        var text = new string('x', 390) + " password=synthetic-secret-tail more";
        var shown = AdminPluginPresentation.Redact(text, 400);
        Assert.DoesNotContain("synthetic", shown); Assert.True(shown.Length <= 400);
        var typed = AdminPluginPresentation.Redact("Failure at https://u:p@fixture.invalid/x?token=synthetic api_key: synthetic-key, secret value typed-secret", 400, ["typed-secret"]);
        Assert.DoesNotContain("synthetic", typed); Assert.DoesNotContain("typed-secret", typed); Assert.DoesNotContain("u:p@", typed);
    }

    [Theory]
    [InlineData("Authorization: Bearer synthetic-credential")]
    [InlineData("Authorization: Basic synthetic-credential==")]
    [InlineData("authorization=token synthetic-credential")]
    [InlineData("Proxy-Authorization: Digest synthetic-credential")]
    [InlineData("Authorization: Bearer token=synthetic-credential")]
    [InlineData("password is synthetic-credential")]
    [InlineData("the secret was synthetic-credential, try again")]
    [InlineData("password: basic-synthetic-credential")]
    public void ACredentialAfterAnAuthSchemeOrAFillerWordIsRemovedNotJustTheWordBeforeIt(string text)
    {
        var shown = AdminPluginPresentation.Redact("Upstream refused: " + text);
        Assert.DoesNotContain("synthetic-credential", shown); Assert.Contains("[redacted]", shown);
    }

    [Fact]
    public void ASchemeOrFillerWordWithNothingAfterItIsItselfTreatedAsTheValue()
    {
        Assert.Equal("Authorization: [redacted]", AdminPluginPresentation.Redact("Authorization: Bearer synthetic-credential"));
        Assert.Equal("Authorization: [redacted]", AdminPluginPresentation.Redact("Authorization: Bearer"));
        Assert.Equal("password [redacted]", AdminPluginPresentation.Redact("password is"));
    }

    [Fact]
    public void MarkdownIsPlainDataNeverMarkup()
    {
        var blocks = AdminPluginMarkdown.Parse("Intro <script>alert(1)</script> and **bold** `code` [site](https://fixture.invalid/x) [bad](javascript:alert(1)) snake_case_name\n\n- one\n- two\n  continued\n\n1. first\n2) second");
        Assert.Equal(3, blocks.Count);
        var paragraph = blocks[0].Items[0];
        Assert.Contains(paragraph, run => run is { Kind: AdminPluginInlineKind.Text } && run.Text.Contains("<script>"));
        Assert.Contains(paragraph, run => run is { Kind: AdminPluginInlineKind.Strong, Text: "bold" });
        var links = paragraph.Where(run => run.Kind == AdminPluginInlineKind.Link).ToArray();
        Assert.Equal(2, links.Length); Assert.NotNull(AdminPluginPresentation.SafeLink(links[0].Href)); Assert.Null(AdminPluginPresentation.SafeLink(links[1].Href));
        Assert.Contains("snake_case_name", string.Concat(paragraph.Select(run => run.Text)));
        Assert.True(blocks[1].IsList && !blocks[1].Ordered && blocks[1].Items.Count == 2); Assert.Contains("continued", string.Concat(blocks[1].Items[1].Select(r => r.Text)));
        Assert.True(blocks[2].IsList && blocks[2].Ordered);
    }

    [Fact]
    public void MarkdownSourceIsBounded() =>
        Assert.True(string.Concat(AdminPluginMarkdown.Parse(new string('a', 100_000)).SelectMany(b => b.Items).SelectMany(i => i).Select(r => r.Text)).Length <= AdminPluginMarkdown.MaxSourceLength);

    private static AdminPlugin Plugin(Action<AdminPlugin>? change = null)
    {
        var plugin = JsonSerializer.Deserialize<AdminPlugin>(AdminPluginsTests.Installation, AdminPluginsApi_Options)!;
        plugin.Runtime = new() { Resident = true, State = "running" }; change?.Invoke(plugin); return plugin;
    }
    private static System.Text.Json.JsonSerializerOptions AdminPluginsApi_Options => SiloPlayer.Core.Api.AdminPluginsApi.WireOptions;

    [Fact]
    public void StatusRanksProblemsFirstAndOffLastAndNeverFlagsAPluginThatIsOff()
    {
        var failed = Plugin(p => p.Runtime = new() { Resident = true, State = "failed" });
        var backoff = Plugin(p => p.Runtime = new() { Resident = true, State = "backoff", RestartCount = 3 });
        var setup = Plugin(p => p.GlobalConfigSchema = [new() { Key = "connection", Title = "Connection", Required = true, JsonSchema = "{}" }]);
        var healthy = Plugin(); var off = Plugin(p => { p.Enabled = false; p.Runtime = new() { Resident = true, State = "failed" }; });
        Assert.Equal([0, 1, 2, 3, 4], new[] { failed, backoff, setup, healthy, off }.Select(p => AdminPluginPresentation.Status(p).Rank).ToArray());
        Assert.False(AdminPluginPresentation.Status(off).Attention);
        Assert.Equal("Restarting (3)", AdminPluginPresentation.Status(backoff).Label);
        Assert.Equal("Needs setup", AdminPluginPresentation.Status(setup).Label); Assert.Equal("Missing: Connection", AdminPluginPresentation.Status(setup).Title);
    }

    [Fact]
    public void RequiredSettingIsSatisfiedByAnyNonBlankSavedValueIncludingFalseAndZero()
    {
        var plugin = Plugin(p => { p.GlobalConfigSchema = [new() { Key = "a", Required = true, JsonSchema = "{}" }, new() { Key = "b", Required = true, JsonSchema = "{}" }, new() { Key = "c", Required = true, JsonSchema = "{}" }]; });
        plugin.GlobalConfigs = [new() { Key = "a", Value = new() { ["x"] = J(false) } }, new() { Key = "b", Value = new() { ["x"] = J("  ") } }, new() { Key = "c", ConfiguredSecrets = ["token"] }];
        Assert.Equal(["b"], AdminPluginPresentation.MissingRequired(plugin).Select(s => s.Key).ToArray());
    }

    [Fact]
    public void PendingUpdateFollowsTheOfficialRules()
    {
        var plugin = Plugin(p => p.AvailableVersion = "2.0.0");
        Assert.Equal("2.0.0", AdminPluginPresentation.PendingUpdate(plugin));
        plugin.UpdatesPaused = true; Assert.Null(AdminPluginPresentation.PendingUpdate(plugin));
        plugin.UpdatesPaused = false; plugin.EnvManaged = new() { VersionPin = "1.0.0" }; Assert.Null(AdminPluginPresentation.PendingUpdate(plugin));
        plugin.EnvManaged = null; plugin.RepositoryId = null; Assert.Equal("unverified", AdminPluginPresentation.Tier(plugin)); Assert.Null(AdminPluginPresentation.PendingUpdate(plugin));
    }

    [Fact]
    public void DisplayNameDerivesFromTheIdWhenNoPresentationExists()
    {
        Assert.Equal("Fixture Provider", AdminPluginPresentation.Name("silo.fixture-provider", null));
        Assert.Equal("silo", AdminPluginPresentation.Name("silo", null));
        Assert.Equal("Shown", AdminPluginPresentation.Name("x", new AdminPluginInfo { DisplayName = " Shown " }));
    }

    [Fact]
    public void CatalogJobsAreOrderedAndPlumbingCapabilitiesGetNoChip()
    {
        AdminPluginCapabilityInfo C(string type) => new() { Type = type, Id = type };
        var jobs = AdminPluginPresentation.CatalogJobs([[C("auth_provider.v1"), C("metadata_provider.v1"), C("event_consumer.v1")], [C("http_routes.v1"), C("custom_thing.v1")]]);
        Assert.Equal(["metadata", "sign-in"], jobs.Select(j => j.Job).ToArray());
        Assert.Equal("Custom thing", AdminPluginPresentation.Kind("custom_thing.v1").Label);
        Assert.Equal("Web pages", AdminPluginPresentation.Kind("http_routes.v1").Label);
    }

    [Fact]
    public void SearchMatchesIdNameSummaryPublisherSourceAndCapabilityLabels()
    {
        var presentation = new AdminPluginInfo { DisplayName = "Fixture", Summary = "Finds posters", PublisherName = "Synthetic Labs" };
        var caps = new[] { new AdminPluginCapabilityInfo { Type = "image_resolver.v1", Id = "i", DisplayName = "Posters" } };
        foreach (var query in new[] { "fixture.plugin", "posters", "synthetic labs", "silo maintained", "artwork", " ARTWORK " })
            Assert.True(AdminPluginPresentation.Matches(query, "fixture.plugin", presentation, caps, "silo", null), query);
        Assert.False(AdminPluginPresentation.Matches("unrelated", "fixture.plugin", presentation, caps, "silo", null));
    }

    [Fact]
    public void SourceNoticesAndSignatureLabelsAreTheOfficialOnes()
    {
        Assert.Null(AdminPluginPresentation.TierNotice("silo"));
        Assert.StartsWith("Silo has not reviewed this plugin.", AdminPluginPresentation.TierNotice("external"));
        Assert.StartsWith("It isn't linked to any of your catalogs", AdminPluginPresentation.TierNotice("unverified"));
        Assert.Equal("owner/name", AdminPluginPresentation.SignatureRepositoryLabel("https://github.com/owner/name"));
        Assert.Equal("Not specified", AdminPluginPresentation.License("NOASSERTION"));
    }

    [Fact]
    public void PluginTaskTriggerPicksARunnableBindingThenTheDefaultThenStartup()
    {
        var runnable = new Dictionary<string, JsonElement> { ["type"] = J("interval"), ["interval_ms"] = J(7200000) };
        Assert.Equal("Every 2 hour(s)", AdminTasksText.DescribeTrigger(AdminPluginPresentation.TaskTrigger(runnable, null)));
        var broken = new Dictionary<string, JsonElement> { ["type"] = J("interval"), ["interval_ms"] = J("soon") };
        var fallback = new AdminPluginTrigger { Type = "daily", TimeOfDay = "03:30" };
        Assert.Equal("Daily at 03:30", AdminTasksText.DescribeTrigger(AdminPluginPresentation.TaskTrigger(broken, fallback)));
        Assert.Equal("On server startup", AdminTasksText.DescribeTrigger(AdminPluginPresentation.TaskTrigger(new Dictionary<string, JsonElement>(), null)));
        Assert.Equal("plugin:7:refresh:all", AdminPluginPresentation.TaskKey("7", "refresh:all"));
    }

    // ── text ───────────────────────────────────────────────────────────────

    [Fact]
    public void EveryOfficialPluginsMessageIsPresentAndFormats()
    {
        Assert.True(AdminPluginsText.Keys.Count(k => k.StartsWith("web.admin.plugins.", StringComparison.Ordinal)) >= 249);
        Assert.Equal("Plugins", AdminPluginsText.T("web.admin.plugins.page.title"));
        Assert.Equal("1 plugin", AdminPluginsText.T("web.admin.plugins.list.resultCount", AdminPluginsText.Count(1)));
        Assert.Equal("3 of 12 plugins", AdminPluginsText.T("web.admin.plugins.list.resultCountFiltered", AdminPluginsText.Count(12, ("shown", 3))));
        Assert.Equal("Restarting (2)", AdminPluginsText.T("web.admin.plugins.statusIndicator.restarting", new { restart_count = 2 }));
    }

    [Fact]
    public void MarkupInAPluginSuppliedValueNeverBecomesBoldOrALink()
    {
        var runs = AdminPluginsText.Runs("web.admin.plugins.detail.updateFailed", new { name = "Evil<b>Name</b><link>", b = "", version = "2.0", reason = "<link>click</link>" });
        Assert.Equal("Silo couldn't update Evil<b>Name</b><link> to 2.0. <link>click</link>", string.Concat(runs.Select(r => r.Text)));
        Assert.Single(runs, run => run.Bold); Assert.DoesNotContain(runs, run => run.Link);
    }

    // ── D1: typed values after a save ──────────────────────────────────────

    private static AdminPluginConfigDraft SecretDraft(out AdminPluginField secret, out AdminPluginField host)
    {
        var draft = new AdminPluginConfigDraft(Schema("connection", """{"type":"object","properties":{"host":{"type":"string"},"password":{"type":"string","writeOnly":true}}}"""),
            new() { ConfiguredSecrets = ["password"], Value = new() { ["host"] = J("fixture-host") } });
        secret = draft.Fields.Single(f => f.Key == "password"); host = draft.Fields.Single(f => f.Key == "host");
        return draft;
    }

    [Theory]
    [InlineData(AdminPluginWriteOutcome.Done, true)]
    [InlineData(AdminPluginWriteOutcome.Unknown, true)]
    [InlineData(AdminPluginWriteOutcome.Refused, false)]
    [InlineData(AdminPluginWriteOutcome.NotSent, false)]
    public void ASaveTheServerTookOrMayHaveTakenErasesWhatWasTypedAndOtherOutcomesKeepIt(AdminPluginWriteOutcome outcome, bool erased)
    {
        using var draft = SecretDraft(out var secret, out var host);
        draft.Set(secret, "synthetic-typed-secret"); draft.Set(host, "edited-host"); Assert.True(draft.IsDirty);
        // The page does exactly this with the outcome of the save.
        if (outcome.SettlesTypedValues()) draft.Dispose();
        if (erased)
        {
            Assert.False(draft.IsDirty); Assert.Empty(draft.Build().Value); Assert.Null(draft.Get(secret)); Assert.DoesNotContain("synthetic-typed-secret", string.Concat(draft.Build().Value.Values.Select(v => v.GetRawText())));
        }
        else
        {
            Assert.True(draft.IsDirty); Assert.Equal("synthetic-typed-secret", draft.Build().Value["password"].GetString()); Assert.Equal("edited-host", draft.Build().Value["host"].GetString());
        }
    }

    [Fact]
    public void ARereadThatReturnsTheSameSecretsAndValuesBuildsAFormThatEqualsTheOldOneSoItMustBeDiscardedNotReused()
    {
        // This is the case the page's reuse check cannot tell apart: the fingerprint is equal, but the old form still holds the typed secret.
        using var before = SecretDraft(out var secret, out _); using var after = SecretDraft(out _, out _);
        Assert.Equal(before.Fingerprint, after.Fingerprint);
        before.Set(secret, "synthetic-typed-secret");
        Assert.True(before.IsDirty); Assert.False(after.IsDirty); Assert.Empty(after.Build().Value.Where(p => p.Key == "password"));
    }

    // ── D2: stored values the form cannot show ─────────────────────────────

    private const string ArrayProperty = """{"type":"object","properties":{"tags":{"type":"array"}}}""";

    private static AdminPluginConfigDraft TagsDraft(string saved, string jsonSchema = ArrayProperty)
    {
        var schema = Schema("lists", jsonSchema, new() { Fields = [new() { Key = "tags", Control = "MULTI_SELECT", Options = [new() { Value = "a", Label = "A" }, new() { Value = "b", Label = "B" }] }] });
        return new AdminPluginConfigDraft(schema, new() { Value = new() { ["tags"] = JsonSerializer.Deserialize<JsonElement>(saved) } });
    }

    private static string[] Items(AdminPluginConfigDraft draft) => draft.Build().Value["tags"].EnumerateArray().Select(e => e.GetRawText()).ToArray();

    [Fact]
    public void TickingAnOptionAddsOnlyThatValueAndKeepsStoredValuesTheOptionsDoNotListInTheirOrder()
    {
        using var draft = TagsDraft("""["a","legacy"]""");
        var tags = draft.Fields[0];
        draft.SetOption(tags, "b", true);
        Assert.Equal(["\"a\"", "\"legacy\"", "\"b\""], Items(draft));
        draft.SetOption(tags, "a", false);
        Assert.Equal(["\"legacy\"", "\"b\""], Items(draft));
        draft.SetOption(tags, "b", false); draft.SetOption(tags, "b", false);
        Assert.Equal(["\"legacy\""], Items(draft));
        draft.SetOption(tags, "a", true); draft.SetOption(tags, "a", true);
        Assert.Equal(["\"legacy\"", "\"a\""], Items(draft));
    }

    [Fact]
    public void StoredNumbersAndOddItemsOfAChangedListGoBackAsStoredAndANewItemIsCoerced()
    {
        using var draft = TagsDraft("""[1,"legacy",2.5,"007"]""", "{}"); // no declared type: a new numeric-looking item becomes an integer
        draft.SetOption(draft.Fields[0], "2", true);
        Assert.Equal(["1", "\"legacy\"", "2.5", "\"007\"", "2"], Items(draft));
    }

    [Fact]
    public void ATypedListKeepsTheDeclaredItemTypeForNewItemsAndTheStoredTypeForOldOnes()
    {
        using var draft = TagsDraft("""["12","x"]""", """{"type":"object","properties":{"tags":{"type":"array","items":{"type":"integer"}}}}""");
        draft.SetOption(draft.Fields[0], "3", true);
        Assert.Equal(["\"12\"", "\"x\"", "3"], Items(draft));
    }

    [Fact]
    public void AnUntouchedListWithValuesOutsideTheOptionsGoesBackExactly()
    {
        using var draft = TagsDraft("""["legacy",3,"a"]""");
        Assert.False(draft.IsDirty); Assert.Equal(["\"legacy\"", "3", "\"a\""], Items(draft));
    }

    [Fact]
    public void ManagedMultiSelectIgnoresTicks()
    {
        var schema = Schema("lists", """{"type":"object","properties":{"tags":{"type":"array"}}}""", new() { Fields = [new() { Key = "tags", Control = "MULTI_SELECT" }] });
        using var draft = new AdminPluginConfigDraft(schema, new() { Value = new() { ["tags"] = J(new[] { "a" }) } }, ["tags"]);
        draft.SetOption(draft.Fields[0], "b", true); Assert.False(draft.IsDirty); Assert.DoesNotContain("tags", draft.Build().Value.Keys);
    }

    [Fact]
    public void EveryControlSendsBackAStoredValueItCannotShowUnlessTheAdminChangesThatField()
    {
        var form = new AdminPluginForm { Fields =
        [
            new() { Key = "kind", Label = "Kind", Control = "SELECT", Options = [new() { Value = "x", Label = "X" }] },
            new() { Key = "level", Label = "Level", Control = "SELECT", Options = [new() { Value = "1", Label = "One" }] },
            new() { Key = "port", Label = "Port", Control = "NUMBER", Validation = new() { HasMin = true, Min = 1, HasMax = true, Max = 10 } },
            new() { Key = "big", Label = "Big", Control = "NUMBER" },
            new() { Key = "name", Label = "Name", Control = "TEXT", Validation = new() { MaxLength = 5 } },
            new() { Key = "color", Label = "Color", Control = "COLOR_PICKER" },
            new() { Key = "rules", Label = "Rules", Control = "TEXT" },
            new() { Key = "other", Label = "Other", Control = "TEXT" },
        ] };
        var saved = new Dictionary<string, JsonElement>
        {
            ["kind"] = J("legacy-kind"), ["level"] = J(5), ["port"] = J(100), ["big"] = JsonSerializer.Deserialize<JsonElement>("12345678901234567890"),
            ["name"] = J("far too long for the limit"), ["color"] = JsonSerializer.Deserialize<JsonElement>("""{"r":1,"g":2}"""), ["rules"] = JsonSerializer.Deserialize<JsonElement>("""["one","two"]"""), ["other"] = J("o"),
        };
        using var draft = new AdminPluginConfigDraft(Schema("c", "{}", form), new() { Value = saved });
        var body = draft.Build().Value;
        foreach (var (key, raw) in saved) Assert.Equal(raw.GetRawText(), body[key].GetRawText());
        // Changing one field changes only that field.
        draft.Set(draft.Fields.Single(f => f.Key == "other"), "changed"); body = draft.Build().Value;
        foreach (var (key, raw) in saved.Where(p => p.Key != "other")) Assert.Equal(raw.GetRawText(), body[key].GetRawText());
        Assert.Equal("changed", body["other"].GetString());
        draft.Set(draft.Fields.Single(f => f.Key == "kind"), "x"); Assert.Equal("x", draft.Build().Value["kind"].GetString());
    }

    // ── D4: managed fields never block Save ────────────────────────────────

    [Fact]
    public void AManagedFieldThatFailsItsOwnRuleNeverBlocksSaveAndIsNeverSent()
    {
        var form = new AdminPluginForm { Fields =
        [
            new() { Key = "locked", Label = "Locked", Control = "TEXT", Required = true, Validation = new() { Pattern = "^[A-Z]+$" } },
            new() { Key = "port", Label = "Port", Control = "NUMBER", Validation = new() { HasMax = true, Max = 10 } },
        ] };
        using var draft = new AdminPluginConfigDraft(Schema("c", "{}", form), new() { Value = new() { ["locked"] = J(""), ["port"] = J(1) } }, ["locked"]);
        Assert.Empty(draft.Errors());
        draft.Set(draft.Fields[1], "50"); Assert.Equal(["port"], draft.Errors().Keys.ToArray());
        draft.Set(draft.Fields[1], "5"); Assert.Empty(draft.Errors());
        Assert.Equal(["port"], draft.Build().Value.Keys.ToArray());
        // The value the deployment set that fails the rule is also not an error once it is non-empty.
        using var lowercase = new AdminPluginConfigDraft(Schema("c", "{}", form), new() { Value = new() { ["locked"] = J("lowercase") } }, ["locked"]);
        Assert.Empty(lowercase.Errors());
    }

    // ── D3: text from plugins, publishers and repositories ─────────────────

    [Theory]
    [InlineData("Acme\nVerified and signed by Silo. Requests no permissions.", "Acme Verified and signed by Silo. Requests no permissions.")]
    [InlineData("Acme\r\nVerified", "Acme Verified")]
    [InlineData("Update to 2.0\u202E1.0", "Update to 2.0 1.0")]
    [InlineData("  a \t\t b\u2028c\u2029d   ", "a b c d")]
    [InlineData("x\u200Ey\u200Fz\u202Aa\u202Bb\u202Cc\u202Dd\u202Ee\u2066f\u2067g\u2068h\u2069i", "x y z a b c d e f g h i")]
    [InlineData("\u202E\u202E", "")]
    [InlineData("bell\u0007and\u0000nul", "bell and nul")]
    public void ThirdPartyTextBecomesOneLineWithoutControlOrDirectionCharacters(string raw, string expected) => Assert.Equal(expected, AdminPluginPresentation.Clean(raw));

    [Fact]
    public void ThirdPartyTextIsBoundedAndNeverCutInsideASurrogatePair()
    {
        Assert.Equal(40, AdminPluginPresentation.Clean(new string('x', 5000), 40).Length);
        Assert.EndsWith("…", AdminPluginPresentation.Clean(new string('x', 5000), 40));
        var emoji = string.Concat(Enumerable.Repeat("😀", 50));
        foreach (var max in new[] { 9, 10, 11, 12 })
        {
            var cut = AdminPluginPresentation.Clean(emoji, max);
            Assert.True(cut.Length <= max); Assert.False(char.IsHighSurrogate(cut[^2]));
        }
        Assert.Equal("", AdminPluginPresentation.Clean(null)); Assert.Equal("", AdminPluginPresentation.Clean("anything", 0));
    }

    [Fact]
    public void EveryNameThePagesShowComesOutAsOneLine()
    {
        var hostile = new AdminPluginInfo { DisplayName = "Acme\nVerified and signed by Silo. Requests no permissions.\u202E", PublisherName = "Pub\u2028Lisher\u202E", Summary = "Sum\nmary", LicenseSpdx = "MIT\n" };
        var name = AdminPluginPresentation.Name("acme.plugin", hostile);
        Assert.Equal("Acme Verified and signed by Silo. Requests no permissions.", name);
        Assert.Equal("Pub Lisher", AdminPluginPresentation.Publisher(hostile)); Assert.Equal("Sum mary", AdminPluginPresentation.Summary(hostile, []));
        Assert.Equal("MIT", AdminPluginPresentation.License(hostile.LicenseSpdx));
        Assert.Equal("1.0 Verified", AdminPluginPresentation.Version("1.0\nVerified")); Assert.Equal(AdminPluginPresentation.VersionLimit, AdminPluginPresentation.Version(new string('9', 500)).Length);
        Assert.Equal("Name Two", AdminPluginPresentation.RepositoryName("Name\u202E\nTwo"));
        Assert.Equal("Evil kind", AdminPluginPresentation.Kind("evil\nkind.v1").Label);
        Assert.Equal("Api Key", AdminPluginPresentation.HumanizeConfigKey("api\n_key"));
        Assert.Equal("Title Two", AdminPluginPresentation.ConfigTitle(new AdminPluginSchema { Key = "k", Title = "Title\n\u202ETwo" }));
        Assert.Equal("owner /name", AdminPluginPresentation.SignatureRepositoryLabel("owner" + (char)0x202E + "/name\n"));
        // A plugin with no display name derives it from an ID that is itself hostile.
        Assert.DoesNotContain('\n', AdminPluginPresentation.Name("silo.x\ny", null));
    }

    [Fact]
    public void ValuesInsertedIntoAMessageCarryNoLineBreakAndNoDirectionControl()
    {
        Assert.Equal("by Acme Verified", AdminPluginsText.T("web.admin.plugins.detail.byPublisher", new { publisher = "Acme\nVerified\u202E" }));
        Assert.Equal("Version 1.0 2.0", AdminPluginsText.T("web.admin.plugins.detail.version", new { version = "1.0\u2028\u20292.0" }));
        var runs = AdminPluginsText.Runs("web.admin.plugins.detail.updateFailed", new { name = "N\nX", b = "", version = "2.0\u202E", reason = "r\ns" });
        var shown = string.Concat(runs.Select(r => r.Text)); Assert.DoesNotContain('\n', shown); Assert.DoesNotContain((char)0x202E, shown);
    }

    [Fact]
    public void ServerAndPluginErrorTextIsOneLineAndADirectionControlCannotHideACredentialKeyword()
    {
        Assert.Equal("first second", AdminPluginPresentation.Redact("first\n\nsecond\u202E", 400));
        Assert.DoesNotContain("hunter2", AdminPluginPresentation.Redact("pass\u202Eword=hunter2", 400));
        Assert.DoesNotContain("hunter2", AdminPluginPresentation.Redact("api_\u200Ekey: hunter2", 400));
    }

    [Fact]
    public void ReadmeTextKeepsItsLineBreaksButNeverADirectionControl()
    {
        var blocks = AdminPluginMarkdown.Parse("First\u202E paragraph\n\nSecond\u2066 paragraph\u2029Third");
        Assert.True(blocks.Count >= 2);
        var text = string.Concat(blocks.SelectMany(b => b.Items).SelectMany(i => i).Select(r => r.Text));
        Assert.DoesNotContain((char)0x202E, text); Assert.DoesNotContain((char)0x2066, text); Assert.Contains("First", text);
        Assert.Equal(AdminPluginPresentation.StripDirectionControls("a\u200Eb\nc"), "ab\nc");
    }

    [Theory]
    [InlineData("plugin\u202Efdp.zip")]
    [InlineData("plugin\nname.zip")]
    [InlineData("plugin\u2028.zip")]
    public void AnUploadNameThatCouldReorderOrBreakTheConfirmationIsNotAccepted(string name) =>
        Assert.Equal(AdminPluginUploadCheck.InvalidName, AdminPluginsApi.CheckUpload(name, 10));
}
