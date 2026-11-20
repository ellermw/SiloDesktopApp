using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanSourceDraftTests
{
    [Theory]
    [InlineData("", true, null)]
    [InlineData(" 42 ", true, 42)]
    [InlineData("1.0", true, 1)]
    [InlineData("1e2", true, 100)]
    [InlineData("0", false, null)]
    [InlineData("-1", false, null)]
    [InlineData("2147483648", false, null)]
    [InlineData("1.5", false, null)]
    public void IntervalIsBlankOrPositiveInt32(string text, bool valid, int? seconds)
    {
        Assert.Equal(valid, AdminAutoscanSourceDraft.TryInterval(text, out var result));
        Assert.Equal(seconds, result);
    }

    [Fact]
    public void WholeSourceTogglePreservesUneditedFields()
    {
        var source = new AdminAutoscanSource
        {
            Enabled = true, DeliveryMode = "poll", ConnectionId = "connection", PollIntervalSeconds = 30,
            Label = "label", SourceConfig = new() { ["key"] = "value" },
            PathRewrites = [new() { From = "/remote", To = "/local" }],
        };
        var body = AdminAutoscanSourceDraft.FromSource(source, false);
        Assert.False(body.Enabled);
        Assert.Equal("connection", body.ConnectionId);
        Assert.Equal(30, body.PollIntervalSeconds);
        Assert.Equal("value", body.SourceConfig["key"]);
        Assert.Equal("/local", Assert.Single(body.PathRewrites).To);
        body.SourceConfig["key"] = "changed";
        body.PathRewrites[0].To = "changed";
        Assert.Equal("value", source.SourceConfig["key"]);
        Assert.Equal("/local", source.PathRewrites[0].To);
    }

    [Fact]
    public void NewWebhookRequiresACompleteMappingAndIncompleteRowsAreRejected()
    {
        Assert.False(AdminAutoscanSourceDraft.ValidMappings([], webhook: true, hadMappings: false, creating: true));
        // Which problem it is decides the message: a half-filled row is "incomplete", no complete row at all is "required".
        Assert.Equal("mappingRequired", AdminAutoscanSourceDraft.MappingProblem([], webhook: true, hadMappings: false, creating: true));
        Assert.Equal("mappingRequired", AdminAutoscanSourceDraft.MappingProblem([new() { To = "/seeded" }], webhook: true, hadMappings: false, creating: true));
        Assert.Equal("mappingRequired", AdminAutoscanSourceDraft.MappingProblem([new() { From = "/source" }], webhook: true, hadMappings: false, creating: true));
        Assert.Equal("mappingRequired", AdminAutoscanSourceDraft.MappingProblem([], webhook: true, hadMappings: true, creating: false));
        Assert.Equal("mappingIncomplete", AdminAutoscanSourceDraft.MappingProblem([new() { From = "/source" }], webhook: true, hadMappings: true, creating: false));
        Assert.Equal("mappingIncomplete", AdminAutoscanSourceDraft.MappingProblem([new() { From = "/source" }], webhook: false, hadMappings: false, creating: false));
        Assert.Null(AdminAutoscanSourceDraft.MappingProblem([], webhook: false, hadMappings: true, creating: false));
        using (var webhook = new AdminAutoscanEditorDraft(null, new AdminAutoscanDescriptor { DeliveryModes = ["webhook"], Connection = "none" }))
            Assert.Equal(AdminAutoscanText.T("sourceDialog.problem.mappingRequired"), webhook.Validate());
        // With no config form the editor shows none of the stored keys, so changing "Sent by" sends every one of them back.
        var stored = new AdminAutoscanSource { Id = "s", DeliveryMode = "webhook", SourceConfig = new() { ["webhook_provider"] = "sonarr", ["other"] = "kept", ["empty"] = "" }, PathRewrites = [new() { From = "/a", To = "/b" }] };
        using var formless = new AdminAutoscanEditorDraft(stored, new AdminAutoscanDescriptor { DeliveryModes = ["webhook"], Connection = "none" });
        formless.Config["webhook_provider"] = "radarr"; formless.ConfigDirty = true;
        var sent = formless.Build(stored).SourceConfig;
        Assert.Equal(3, sent.Count); Assert.Equal("radarr", sent["webhook_provider"]); Assert.Equal("kept", sent["other"]); Assert.Equal("", sent["empty"]);
        Assert.False(AdminAutoscanSourceDraft.ValidMappings([new() { From = "/source", To = "" }], false, false, false));
        Assert.True(AdminAutoscanSourceDraft.ValidMappings([], webhook: true, hadMappings: false, creating: false));
        Assert.False(AdminAutoscanSourceDraft.ValidMappings([], webhook: true, hadMappings: true, creating: false));
        Assert.True(AdminAutoscanSourceDraft.ValidMappings([new() { From = " /source ", To = " /target " }], true, false, true));
        // Seeded rows with only the Silo side do not block a new webhook source that has one complete mapping.
        Assert.True(AdminAutoscanSourceDraft.ValidMappings([new() { From = "/source", To = "/target" }, new() { To = "/seeded" }], webhook: true, hadMappings: false, creating: true));
        Assert.False(AdminAutoscanSourceDraft.ValidMappings([new() { From = "/source", To = "/target" }, new() { To = "/seeded" }], webhook: true, hadMappings: true, creating: false));
    }

    [Fact]
    public void DescriptorControlsDeliveryAndEligibleConnections()
    {
        var descriptor = new AdminAutoscanDescriptor { DeliveryModes = ["webhook", "poll"], Connection = "required", ConnectionKinds = ["sonarr"] };
        Assert.Equal("poll", AdminAutoscanSourceDraft.DefaultDelivery(descriptor));
        Assert.True(AdminAutoscanSourceDraft.ConnectionRequired(descriptor, "poll"));
        Assert.False(AdminAutoscanSourceDraft.ConnectionRequired(descriptor, "webhook"));
        Assert.True(AdminAutoscanSourceDraft.MatchesConnection(descriptor, "sonarr"));
        Assert.False(AdminAutoscanSourceDraft.MatchesConnection(descriptor, "radarr"));
    }
}
