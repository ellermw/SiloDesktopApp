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
        Assert.False(AdminAutoscanSourceDraft.ValidMappings([new() { From = "/source", To = "" }], false, false, false));
        Assert.True(AdminAutoscanSourceDraft.ValidMappings([], webhook: true, hadMappings: false, creating: false));
        Assert.False(AdminAutoscanSourceDraft.ValidMappings([], webhook: true, hadMappings: true, creating: false));
        Assert.True(AdminAutoscanSourceDraft.ValidMappings([new() { From = " /source ", To = " /target " }], true, false, true));
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
