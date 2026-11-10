using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdatePanelTests
{
    [Fact]
    public void Details_expand_and_collapse_only_while_the_update_is_in_progress()
    {
        var panel = new UpdatePanelModel();
        Assert.Equal(UpdatePanelPhase.Working, panel.Phase);
        Assert.False(panel.ShowsDetails);
        Assert.True(panel.ShowsDetailsToggle);
        Assert.Equal(("Details", UpdatePanelModel.ExpandGlyph), (panel.DetailsLabel, panel.DetailsGlyph));

        Assert.True(panel.ToggleDetails());
        Assert.True(panel.ShowsDetails);
        Assert.Equal(("Hide details", UpdatePanelModel.CollapseGlyph), (panel.DetailsLabel, panel.DetailsGlyph));

        Assert.False(panel.ToggleDetails());
        Assert.False(panel.ShowsDetails);
        Assert.Equal(UpdatePanelModel.ExpandGlyph, panel.DetailsGlyph);

        // Opened details stay out of the way of the Restart now / Later question...
        panel.ToggleDetails();
        panel.Ready();
        Assert.True(panel.ShowsChoice);
        Assert.False(panel.ShowsDetails);
        Assert.False(panel.ShowsDetailsToggle);
        Assert.False(panel.ShowsProgress);
        Assert.False(panel.ToggleDetails());
        // ...and come back as they were once Restart now continues the installation.
        panel.Restart();
        Assert.Equal(UpdatePanelPhase.Working, panel.Phase);
        Assert.True(panel.ShowsDetails);
        Assert.False(panel.ShowsChoice);
    }

    [Fact]
    public void Later_is_only_an_answer_to_the_ready_question()
    {
        var panel = new UpdatePanelModel();
        panel.Defer();
        Assert.Equal(UpdatePanelPhase.Working, panel.Phase);
        panel.Ready(); panel.Defer();
        Assert.Equal(UpdatePanelPhase.Deferred, panel.Phase);
        Assert.False(panel.ShowsChoice || panel.ShowsProgress || panel.ShowsDetailsToggle);
        panel.Restart();
        Assert.Equal(UpdatePanelPhase.Deferred, panel.Phase);
        Assert.Contains("next time you start Silo", UpdatePanelModel.DeferredText);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(0, 100, "0%")]
    [InlineData(629, 1000, "62%")]
    [InlineData(999, 1000, "99%")]
    [InlineData(1200, 1000, "100%")]
    [InlineData(-5, 1000, "0%")]
    public void Percent_is_shown_at_the_end_of_the_bar_only_for_known_progress(long completed, long total, string? expected)
        => Assert.Equal(expected, UpdatePanelModel.Percent(completed, total));

    [Fact]
    public void Panel_docks_to_the_window_bottom_right_and_grows_upward()
    {
        var work = new PanelRect(0, 0, 2560, 1400);
        var client = new PanelRect(200, 100, 1480, 820);
        var collapsed = UpdatePanelPlacement.DockBottomRight(client, 340, 112, 16, work);
        var expanded = UpdatePanelPlacement.DockBottomRight(client, 340, 240, 16, work);
        Assert.Equal((1480 - 16 - 340, 820 - 16 - 112), collapsed);
        Assert.Equal(collapsed.X, expanded.X);
        Assert.Equal(collapsed.Y + 112, expanded.Y + 240); // Same bottom edge.
        // Moving the window moves the panel with it.
        Assert.Equal((collapsed.X + 300, collapsed.Y - 50),
            UpdatePanelPlacement.DockBottomRight(client with { Left = 500, Right = 1780, Top = 50, Bottom = 770 }, 340, 112, 16, work));
    }

    [Fact]
    public void Panel_stays_inside_the_monitor_work_area()
    {
        var work = new PanelRect(-1920, 0, 0, 1040);
        // Window hanging below the taskbar and off the left edge of a left-hand monitor.
        Assert.Equal((-1920, 1040 - 112), UpdatePanelPlacement.DockBottomRight(new PanelRect(-2400, 300, -1900, 1300), 340, 112, 16, work));
    }

    [Fact]
    public void Panel_shows_only_while_Silo_is_in_front_and_no_video_plays_over_it()
    {
        Assert.True(UpdatePanelPlacement.ShouldShow(ownerAlive: true, ownerMinimized: false, playback: false, foregroundIsSilo: true, visibleNow: false));
        Assert.False(UpdatePanelPlacement.ShouldShow(true, false, false, foregroundIsSilo: false, visibleNow: true)); // Another app in front.
        Assert.False(UpdatePanelPlacement.ShouldShow(true, ownerMinimized: true, false, true, true));
        Assert.False(UpdatePanelPlacement.ShouldShow(true, false, playback: true, true, true)); // Fullscreen or windowed video.
        // Secure desktop during administrator approval: keep whatever is showing.
        Assert.True(UpdatePanelPlacement.ShouldShow(true, false, false, foregroundIsSilo: null, visibleNow: true));
        Assert.False(UpdatePanelPlacement.ShouldShow(true, false, false, foregroundIsSilo: null, visibleNow: false));
        // Silo closed to restart: the final status stays visible.
        Assert.True(UpdatePanelPlacement.ShouldShow(ownerAlive: false, true, true, false, false));
    }

    [Fact]
    public void Only_a_large_video_surface_counts_as_playback()
    {
        var monitor = new PanelRect(0, 0, 1920, 1080);
        var client = new PanelRect(100, 100, 1380, 820);
        Assert.True(UpdatePanelPlacement.IsPlaybackSurface(monitor, client, monitor)); // Fullscreen.
        Assert.True(UpdatePanelPlacement.IsPlaybackSurface(client, client, monitor)); // Windowed player.
        Assert.False(UpdatePanelPlacement.IsPlaybackSurface(new PanelRect(1000, 700, 1380, 820), client, monitor)); // Mini player.
    }

    [Fact]
    public void Host_and_client_can_exchange_the_restart_answer()
    {
        foreach (var kind in new[] { "restart", "later" })
        {
            using var stream = new MemoryStream();
            UpdateHostProtocol.WriteAsync(stream, new UpdateHostMessage(kind), default).GetAwaiter().GetResult();
            stream.Position = 0;
            Assert.Equal(kind, UpdateHostProtocol.ReadAsync(stream, default).GetAwaiter().GetResult().Kind);
        }
    }
}

public sealed class DeferredAppUpdateTests : IDisposable
{
    private readonly string cache = Path.Combine(Path.GetTempPath(), "silo-deferred-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(cache, true); } catch (DirectoryNotFoundException) { } }

    private static AppRelease Release(string version, bool incremental = true) => new(Version.Parse(version), "v" + version, "", DateTimeOffset.UtcNow,
        new Uri("https://github.com/ellermw/SiloDesktopApp/releases/download/v" + version + "/SiloInstaller-Windows-x64.exe"), new string('a', 64), 1)
    {
        Incremental = incremental ? new IncrementalReleaseAssets(new Uri("https://github.com/m"), new string('b', 64), 1, new Uri("https://github.com/p"), new string('c', 64), 1) : null
    };

    [Fact]
    public void Later_survives_a_restart_of_Silo()
    {
        Assert.Null(DeferredAppUpdate.Read(cache));
        DeferredAppUpdate.Save(cache, Release("1.2.970"));
        var record = DeferredAppUpdate.Read(cache);
        Assert.Equal(new DeferredAppUpdate("v1.2.970", "1.2.970.0"), record);
        DeferredAppUpdate.Clear(cache);
        Assert.Null(DeferredAppUpdate.Read(cache));
    }

    [Fact]
    public void Next_start_waits_for_a_check_then_resumes_the_update()
    {
        DeferredAppUpdate.Save(cache, Release("1.2.970"));
        var record = DeferredAppUpdate.Read(cache)!;
        var installed = new Version(1, 2, 968, 0);
        // Startup: nothing checked yet, or a check that failed (offline) keeps the deferral.
        Assert.Equal(DeferredUpdateAction.Wait, record.Decide(installed, new AppUpdateState(), checkSucceeded: false));
        Assert.Equal(DeferredUpdateAction.Wait, record.Decide(installed, new AppUpdateState(IsChecking: true), false));
        Assert.Equal(DeferredUpdateAction.Wait, record.Decide(installed, new AppUpdateState(Error: "Could not check for updates."), false));
        Assert.Equal(DeferredUpdateAction.Wait, record.Decide(installed, new AppUpdateState(Release("1.2.970"), IsDownloading: true), true));
        // The check found the release: finish the update.
        Assert.Equal(DeferredUpdateAction.Resume, record.Decide(installed, new AppUpdateState(Release("1.2.970")), true));
        // A newer release on the same channel replaces it; the user already chose to update.
        Assert.Equal(DeferredUpdateAction.Resume, record.Decide(installed, new AppUpdateState(Release("1.2.975")), true));
    }

    [Fact]
    public void Deferral_retires_once_the_update_is_installed_or_no_longer_applies()
    {
        var record = new DeferredAppUpdate("v1.2.970", "1.2.970.0");
        // The new version is running (the update finished, e.g. installed when Silo closed).
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(new Version(1, 2, 970, 0), new AppUpdateState(), false));
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(new Version(1, 2, 971), new AppUpdateState(Release("1.2.975")), true));
        // A successful check found nothing to install (for example after a channel change).
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(new Version(1, 2, 968), new AppUpdateState(), checkSucceeded: true));
        // Only the full installer can apply it: that needs its own consent in the update dialog.
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(new Version(1, 2, 968), new AppUpdateState(Release("1.2.970", incremental: false)), true));
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(new Version(1, 2, 968), new AppUpdateState(Release("1.2.970")) { RequiresFullInstaller = true }, true));
    }

    [Fact]
    public void Later_on_one_channel_never_resumes_on_another()
    {
        DeferredAppUpdate.Save(cache, Release("1.2.970"), AppUpdateChannel.Stable);
        var record = DeferredAppUpdate.Read(cache)!;
        Assert.Equal(AppUpdateChannel.Stable, record.Channel);
        var installed = new Version(1, 2, 968, 0);
        // Switched to Beta before restarting: a beta release found there is not the deferred one.
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(installed, new AppUpdateState(Release("1.2.975")), true, AppUpdateChannel.Beta));
        Assert.Equal(DeferredUpdateAction.Discard, record.Decide(installed, new AppUpdateState(), false, AppUpdateChannel.Beta));
        Assert.Equal(DeferredUpdateAction.Resume, record.Decide(installed, new AppUpdateState(Release("1.2.970")), true, AppUpdateChannel.Stable));

        DeferredAppUpdate.Save(cache, Release("1.2.970"), AppUpdateChannel.Beta);
        var beta = DeferredAppUpdate.Read(cache)!;
        Assert.Equal(new DeferredAppUpdate("v1.2.970", "1.2.970.0", AppUpdateChannel.Beta), beta);
        Assert.Equal(DeferredUpdateAction.Discard, beta.Decide(installed, new AppUpdateState(Release("1.2.970")), true, AppUpdateChannel.Stable));
        Assert.Equal(DeferredUpdateAction.Resume, beta.Decide(installed, new AppUpdateState(Release("1.2.970")), true, AppUpdateChannel.Beta));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Tag\":\"1.2.970\",\"Version\":\"1.2.970.0\"}")]
    [InlineData("{\"Tag\":\"v1.2.970\",\"Version\":\"soon\"}")]
    [InlineData("{\"Tag\":\"v1.2.970\",\"Version\":\"1.2.970.0\",\"Channel\":7}")]
    public void A_damaged_record_is_removed_instead_of_resuming(string content)
    {
        Directory.CreateDirectory(cache);
        var path = Path.Combine(cache, DeferredAppUpdate.FileName);
        File.WriteAllText(path, content);
        Assert.Null(DeferredAppUpdate.Read(cache));
        Assert.False(File.Exists(path));
    }
}
