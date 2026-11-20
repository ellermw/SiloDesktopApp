using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Regression tests for the third review of Manage > Playback (Nodes and Playback settings). All transport data is synthetic.</summary>
public sealed class AdminPlaybackThirdReviewTests
{
    private static AdminPlaybackNode Node() => new() { Id = "synthetic-node", Name = "Node", Type = "transcode", Url = "https://worker.invalid", ConfigETag = "\"one\"", Enabled = true, Healthy = true };
    private static string Source(string file) => File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", file));

    // A stored empty override means inherit; it must not leave the editor without a choice and refuse every edit.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void AStoredEmptyAccelerationOverrideIsInheritAndDoesNotBlockTheEdit(string? stored)
    {
        var target = Node(); target.HwAccelOverride = stored;
        var editor = new AdminPlaybackNodeEditor("transcode", target) { Name = "Renamed" };
        Assert.Equal("inherit", editor.HwAccelOverride);
        var values = editor.Capture(true);
        Assert.Null(values["hw_accel_override"]); Assert.Equal("Renamed", values["name"]);
    }
    [Fact]
    public void AStoredOverrideTheServerKnowsIsKept()
    {
        var target = Node(); target.HwAccelOverride = "qsv";
        Assert.Equal("qsv", new AdminPlaybackNodeEditor("transcode", target).HwAccelOverride);
    }

    // A Go nil slice arrives as JSON null and replaces the model's empty default.
    [Fact]
    public async Task NullPhysicalGpuKeysFromTheServerBecomeAnEmptyList()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"synthetic-node\",\"name\":\"Node\",\"type\":\"transcode\",\"url\":\"https://worker.invalid\",\"physical_gpu_keys\":null},{\"id\":\"synthetic-other\",\"name\":\"Other\",\"type\":\"transcode\",\"url\":\"https://other.invalid\",\"physical_gpu_keys\":[\"gpu-1\"]}]}"));
        var window = await new AdminPlaybackAdministrationApi(kit.Client).ListNodesAsync(owner, default);
        Assert.Equal(2, window.Items.Count); Assert.NotNull(window.Items[0].PhysicalGpuKeys); Assert.Empty(window.Items[0].PhysicalGpuKeys);
        Assert.Equal(new[] { "gpu-1" }, window.Items[1].PhysicalGpuKeys);
    }
    [Fact]
    public void NodeCardsDoNotIntersectUnguardedGpuKeys()
    {
        var source = Source("AdminNodesPage.cs");
        Assert.DoesNotContain("PhysicalGpuKeys.Intersect(", source); Assert.Contains("(node.PhysicalGpuKeys ?? [])", source);
    }

    // An unconfirmed marker run is released only by a refresh that really read the task list.
    [Fact]
    public async Task SupportReportsWhetherTheTaskListWasActuallyRead()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var failTasks = true;
        kit.Http.Respond = (request, _) =>
        {
            if (failTasks && request.RequestUri!.AbsolutePath.EndsWith("/admin/tasks")) throw new HttpRequestException("synthetic timeout");
            return Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"key\":\"sync_markers\",\"state\":\"idle\"}]}"));
        };
        var api = new AdminPlaybackAdministrationApi(kit.Client);
        var unread = await api.SupportAsync(owner, default);
        Assert.False(unread.TasksRead); Assert.Empty(unread.Tasks);
        failTasks = false;
        var read = await api.SupportAsync(owner, default);
        Assert.True(read.TasksRead); Assert.Equal("sync_markers", Assert.Single(read.Tasks).Key);
    }
    [Fact]
    public void OnlyTheExplicitRefreshThatReadTheTasksReleasesAnUnconfirmedRun()
    {
        var page = Source("AdminPlaybackSettingsPage.cs"); var panel = Source("AdminPlaybackSettingsPanel.cs");
        // The hw_accel-triggered refresh is a plain support read; only the Refresh button raises the review.
        Assert.Contains("_panel.RefreshSupport += RefreshSupport;", page); Assert.Contains("_panel.ReviewSupport += ReviewSupport;", page);
        Assert.DoesNotContain("RefreshSupport += ReviewSupport", page);
        Assert.Contains("if (await RefreshSupport() && _active) _panel?.ReviewTaskOutcomes();", page); Assert.Contains("return support.TasksRead;", page);
        var click = panel[panel.IndexOf("_supportReload.Click +=", StringComparison.Ordinal)..]; click = click[..click.IndexOf('\n')];
        Assert.Contains("await ReviewSupport()", click); Assert.DoesNotContain("await RefreshSupport()", click);
    }

    // The one-time node key must not enter clipboard history or the cloud clipboard.
    [Fact]
    public void NodeKeyCopiesStayOutOfClipboardHistoryAndCloudSync()
    {
        var source = Source("AdminNodesPage.Dialogs.cs");
        Assert.Contains("Clipboard.SetContentWithOptions(", source); Assert.Contains("IsAllowedInHistory = false", source); Assert.Contains("IsRoamable = false", source);
        Assert.DoesNotContain("Clipboard.SetContent(", source);
    }

    // The 30 s background refresh must not disable the cards, and an action started during it waits instead of being dropped.
    [Fact]
    public void BackgroundNodeRefreshKeepsTheCardsUsableAndRestoresFocus()
    {
        var source = Source("AdminNodesPage.cs");
        Assert.Contains("var loading = state.Loading && _pollReload == null;", source);
        Assert.DoesNotContain("!state.Busy && !state.Loading && !state.ReviewRequired", source);
        Assert.Contains("FocusedListControl()", source); Assert.Contains("Refocus(id, focus)", source);
        Assert.Contains("await Settled(node)", source);
    }
}
