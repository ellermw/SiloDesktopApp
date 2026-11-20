using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Regression tests for the two review findings on Manage > Playback (official silo-server 217ff25).</summary>
public sealed class AdminPlaybackReviewFixTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
    private static AdminPlaybackTask TaskOf(string json) => JsonSerializer.Deserialize<AdminPlaybackTask>(json, Wire)!;

    [Fact]
    public async Task RejectedEnabledUpdateLeavesTheSavedNodeEnabledSoTheSwitchCanReturnToIt()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put) return Task.FromResult(AdminMonitorKit.Json("{\"type\":\"validation_failed\",\"status\":422}", HttpStatusCode.UnprocessableEntity));
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.EndsWith("capabilities") ? AdminMonitorKit.Json("{\"allowed\":true,\"state\":\"available\"}")
                : AdminMonitorKit.Json("{\"items\":[{\"id\":\"synthetic-node\",\"name\":\"Node\",\"type\":\"proxy\",\"url\":\"https://proxy.invalid\",\"enabled\":true,\"healthy\":true,\"config_etag\":\"\\\"one\\\"\"}]}"));
        };
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        var node = Assert.Single(controller.State.Window.Items); var rendered = controller.State.Window;
        Assert.True(node.Enabled);
        Assert.False(await controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = false }));
        // The rejection reports failure, keeps the saved window (enabled) and does not demand a review: the page restores the switch from it.
        Assert.False(controller.State.Busy); Assert.False(controller.State.ReviewRequired); Assert.NotNull(controller.State.Error);
        Assert.Same(rendered, controller.State.Window); Assert.True(Assert.Single(controller.State.Window.Items).Enabled);
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT")));
    }

    [Fact]
    public void NodesPageRestoresTheSwitchWhenTheUpdateIsNotSaved()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminNodesPage.cs"));
        var start = source.IndexOf("enabled.Toggled +=", StringComparison.Ordinal); Assert.True(start > 0);
        var handler = source[start..source.IndexOf("actions.Children.Add(enabled)", start, StringComparison.Ordinal)];
        Assert.Contains("UpdateAsync(node", handler); Assert.Contains("enabled.IsOn = node.Enabled", handler);
        Assert.Contains("if (saved", handler);
    }

    [Fact]
    public void NeverRunTaskShowsNoBadgeAndTheNeverRunLine()
    {
        var task = TaskOf("{\"key\":\"sync_markers\",\"name\":\"Sync\",\"state\":\"idle\"}");
        Assert.Null(AdminPlaybackMarkerTaskStatus.StatusLabel(task));
        Assert.Equal("Last run: Never run", AdminPlaybackMarkerTaskStatus.ResultText(task));
        Assert.Equal("Last run: Never run", AdminPlaybackMarkerTaskStatus.ResultText(null));
    }

    [Theory]
    [InlineData("completed", "completed", AdminPlaybackMarkerTaskStatus.BadgeTone.Secondary)]
    [InlineData("failed", "failed", AdminPlaybackMarkerTaskStatus.BadgeTone.Destructive)]
    [InlineData("cancelled", "cancelled", AdminPlaybackMarkerTaskStatus.BadgeTone.Outline)]
    [InlineData("paused-by-server", "paused-by-server", AdminPlaybackMarkerTaskStatus.BadgeTone.Secondary)]
    public void LastExecutionStatusIsRenderedAsTheOfficialBadge(string status, string label, AdminPlaybackMarkerTaskStatus.BadgeTone tone)
    {
        var task = TaskOf("{\"key\":\"detect_intro_markers\",\"state\":\"idle\",\"last_execution\":{\"status\":\"" + status + "\",\"completed_at\":\"2026-11-01T10:00:00Z\",\"task_key\":\"detect_intro_markers\"}}");
        Assert.Equal(label, AdminPlaybackMarkerTaskStatus.StatusLabel(task)); Assert.Equal(tone, AdminPlaybackMarkerTaskStatus.Tone(task));
        Assert.StartsWith("Last run: ", AdminPlaybackMarkerTaskStatus.ResultText(task)); Assert.NotEqual("Last run: Never run", AdminPlaybackMarkerTaskStatus.ResultText(task));
    }

    [Fact]
    public void FailedRunShowsTheServerErrorAsTheBadgeTooltip()
    {
        var task = TaskOf("{\"key\":\"sync_markers\",\"last_execution\":{\"status\":\"failed\",\"completed_at\":\"2026-11-01T10:00:00Z\",\"error_message\":\"  Provider unreachable  \"}}");
        Assert.Equal("Provider unreachable", AdminPlaybackMarkerTaskStatus.ErrorText(task));
        Assert.Null(AdminPlaybackMarkerTaskStatus.ErrorText(TaskOf("{\"key\":\"sync_markers\",\"last_execution\":{\"status\":\"completed\",\"error_message\":\"  \"}}")));
    }

    [Fact]
    public void ContributeRunWithFailedItemsIsCompletedWithErrorsAndSummarisesTheResult()
    {
        var task = TaskOf("{\"key\":\"contribute_markers\",\"last_execution\":{\"status\":\"completed\",\"completed_at\":\"2026-11-01T10:00:00Z\",\"result_data\":{\"submitted\":1200,\"skipped\":3,\"invalid\":0,\"failed\":2}}}");
        Assert.True(AdminPlaybackMarkerTaskStatus.CompletedWithErrors(task));
        Assert.Equal("Completed with errors", AdminPlaybackMarkerTaskStatus.StatusLabel(task)); Assert.Equal(AdminPlaybackMarkerTaskStatus.BadgeTone.Destructive, AdminPlaybackMarkerTaskStatus.Tone(task));
        Assert.Equal("Last result: 1,200 submitted, 3 skipped, 2 failed", AdminPlaybackMarkerTaskStatus.ResultText(task).Replace(" ", " "));
    }

    [Fact]
    public void RateLimitedContributeRunAndZeroFailuresFollowTheOfficialText()
    {
        var task = TaskOf("{\"key\":\"contribute_markers\",\"last_execution\":{\"status\":\"completed\",\"completed_at\":\"2026-11-01T10:00:00Z\",\"result_data\":{\"submitted\":5,\"invalid\":4,\"failed\":0,\"retry_after_seconds\":30}}}");
        Assert.False(AdminPlaybackMarkerTaskStatus.CompletedWithErrors(task)); Assert.Equal("completed", AdminPlaybackMarkerTaskStatus.StatusLabel(task));
        Assert.Equal("Last result: 5 submitted, 4 refused by the provider, 0 failed. Provider rate limit reached during this run.", AdminPlaybackMarkerTaskStatus.ResultText(task));
    }

    [Fact]
    public void OnlyContributeRunsUseResultDataAndOthersShowTheLastRunDate()
    {
        var sync = TaskOf("{\"key\":\"sync_markers\",\"last_execution\":{\"status\":\"completed\",\"completed_at\":\"2026-11-01T10:00:00Z\",\"result_data\":{\"failed\":9}}}");
        Assert.False(AdminPlaybackMarkerTaskStatus.CompletedWithErrors(sync)); Assert.StartsWith("Last run: ", AdminPlaybackMarkerTaskStatus.ResultText(sync));
        var emptyContribute = TaskOf("{\"key\":\"contribute_markers\",\"last_execution\":{\"status\":\"completed\",\"completed_at\":\"2026-11-01T10:00:00Z\",\"result_data\":{}}}");
        Assert.StartsWith("Last run: ", AdminPlaybackMarkerTaskStatus.ResultText(emptyContribute));
    }

    [Fact]
    public void SettingsPanelRendersTheLastExecutionBadgeFromTheSharedStatusRules()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminPlaybackSettingsPanel.Conditions.cs"));
        Assert.Contains("Status.StatusLabel(task)", source); Assert.Contains("StatusBadge(task", source); Assert.Contains("PlaybackTaskStatus_", source); Assert.Contains("Status.ResultText(task)", source);
    }
}
