using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersPresentationTests
{
    [Theory]
    [InlineData("completed", "on_device", true, true)]
    [InlineData("completed", "requested", true, false)]
    [InlineData("ready", "requested", true, true)]
    [InlineData("downloading", "requested", true, true)]
    [InlineData("ready", "in_progress", true, false)]
    [InlineData("preparing", "in_progress", false, true)]
    [InlineData("ready", "in_progress", false, true)]
    [InlineData("downloading", "in_progress", false, true)]
    [InlineData("failed", "failed", false, true)]
    [InlineData("revoked", "revoked", true, true)]
    [InlineData("future_status", "", false, true)]
    public void AndroidRequestedIsNotProofOfStorage(string status, string filter, bool android, bool expected) =>
        Assert.Equal(expected, AdminUsersPresentation.MatchesDownload(new JsonObject { ["status"] = status }, filter, android));

    [Theory]
    [InlineData("direct_stream", "transcode", "transcode", true, false)]
    [InlineData("transcode", "direct", "direct", false, true)]
    [InlineData("", "", "direct", true, false)]
    [InlineData("", "", "transcode", false, true)]
    [InlineData("", "direct", "transcode", false, false)]
    public void AudioTranscodeDoesNotBecomeVideoUsage(string effective, string video, string legacy, bool audio, bool expected) =>
        Assert.Equal(expected, AdminUsersPresentation.IsVideoTranscode(new JsonObject { ["effective_play_method"] = effective, ["video_decision"] = video, ["play_method"] = legacy, ["transcode_audio"] = audio }));

    [Fact]
    public void ReorderingCannotUnhideAnInheritedHiddenSection()
    {
        var settings = JsonNode.Parse("""[{"id":"shown","position":0,"hidden":false},{"id":"hidden","position":1,"hidden":true}]""")!.AsArray();
        var result = AdminUserHomeSections.Change(settings, new JsonArray(), "hidden", direction: -1);
        Assert.True(result.OfType<JsonObject>().Single(row => row.Text("section_id") == "hidden").Flag("hidden"));
        Assert.Equal(0, result.OfType<JsonObject>().Single(row => row.Text("section_id") == "hidden").Count("position"));
    }

    [Theory]
    [InlineData("e", -1)]
    [InlineData("d", 1)]
    public void ReorderingSparsePositionsPreservesUnrelatedSectionsAndRemovedOverrides(string sectionId, int direction)
    {
        var settings = JsonNode.Parse("""[{"id":"c","position":2,"hidden":true},{"id":"d","position":3,"hidden":false},{"id":"e","position":4,"hidden":false}]""")!.AsArray();
        var saved = JsonNode.Parse("""[{"id":"removed-a","section_id":"a","position":0,"removed":true,"config":{"keep":1}},{"id":"override-c","section_id":"c","position":2,"hidden":true,"user_title":"Custom C","config":{"custom":7}}]""")!.AsArray();
        var originalSettings = settings.ToJsonString();
        var originalSaved = saved.ToJsonString();

        var result = AdminUserHomeSections.Change(settings, saved, sectionId, direction: direction);

        var active = result.OfType<JsonObject>().Where(row => !row.Flag("removed")).OrderBy(row => row.Count("position")).ToArray();
        Assert.Equal(new[] { "c", "e", "d" }, active.Select(row => row.Text("section_id")));
        Assert.Equal(new[] { 0L, 1L, 2L }, active.Select(row => row.Count("position")));
        Assert.True(active[0].Flag("hidden"));
        Assert.Equal("Custom C", active[0].Text("user_title"));
        Assert.Equal(7, active[0]["config"].Count("custom"));
        Assert.True(JsonNode.DeepEquals(saved[0], result.OfType<JsonObject>().Single(row => row.Text("id") == "removed-a")));
        Assert.Equal(originalSettings, settings.ToJsonString());
        Assert.Equal(originalSaved, saved.ToJsonString());
    }

    [Fact]
    public void HomeWritesStripReadOnlyDatesWithoutLosingSavedOverrides()
    {
        var settings = JsonNode.Parse("""[{"id":"system","position":0,"hidden":false}]""")!.AsArray();
        var saved = JsonNode.Parse("""[{"id":"override","section_id":"system","hidden":false,"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-02T00:00:00Z","config":{"custom":1}}]""")!.AsArray();
        var result = AdminUserHomeSections.Change(settings, saved, "system", hidden: true);
        var row = Assert.Single(result.OfType<JsonObject>());
        Assert.False(row.ContainsKey("created_at")); Assert.False(row.ContainsKey("updated_at"));
        Assert.Equal(1, row["config"].Count("custom")); Assert.True(row.Flag("hidden"));
    }

    [Theory]
    [InlineData("ui.disabled_library_ids", false)]
    [InlineData("ui.disabled_library_ids", true)]
    [InlineData("ui.library_order", false)]
    [InlineData("ui.library_order", true)]
    public void SettingsDocumentValuesAcceptArraysWithoutRelaxingMutationObjects(string key, bool nullable)
    {
        var definition = new JsonObject
        {
            ["key"] = key,
            ["value_schema"] = new JsonObject { ["type"] = "object", ["nullable"] = nullable }
        };
        var original = definition.ToJsonString();
        var valueDefinition = AdminUserPreferences.ValueDefinition(definition);
        foreach (var json in new[] { "[\"3\"]", "[]", "{}" })
            AdminUsersContract.ValidateValue("value", valueDefinition, JsonNode.Parse(json));
        foreach (var json in new[] { "\"3\"", "3", "true" })
            Assert.Throws<ArgumentException>(() => AdminUsersContract.ValidateValue("value", valueDefinition, JsonNode.Parse(json)));
        if (nullable) AdminUsersContract.ValidateValue("value", valueDefinition, null);
        else Assert.Throws<ArgumentException>(() => AdminUsersContract.ValidateValue("value", valueDefinition, null));
        Assert.Equal(original, definition.ToJsonString());
        Assert.Throws<ArgumentException>(() => AdminUsersContract.ValidateValue("config",
            AdminUsersContract.Schema("SectionOverrideWrite")["properties"]!["config"]!.AsObject(), new JsonArray("3")));
    }

    [Fact]
    public void ExplicitInheritedNullIsNotTheDefaultValue()
    {
        var definition = JsonNode.Parse("""{"key":"synthetic","default_value":9,"resolution_order":["profile_device","profile","account","default"]}""")!.AsObject();
        var rows = new[] { JsonNode.Parse("""{"key":"synthetic","scope":"profile","profile_id":"p2","value":null}""")!.AsObject() };
        Assert.Contains("null (explicit)", AdminUserPreferences.Inherited(definition, new("profile_device", "p2", DeviceId: "dev"), rows));
        rows[0]["value"] = 0;
        Assert.Contains("profile: 0", AdminUserPreferences.Inherited(definition, new("profile_device", "p2", DeviceId: "dev"), rows));
    }
}
