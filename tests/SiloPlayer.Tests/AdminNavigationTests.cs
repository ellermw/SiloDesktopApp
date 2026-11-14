using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Upstream AdminSidebar.test.tsx / useAdminNavTopics.test.ts behaviours plus shell text.</summary>
public sealed class AdminNavigationTests
{
    [Fact]
    public void ActiveTopicReopensEvenAfterTheAdminClosedIt()
    {
        var stored = new Dictionary<string, bool> { ["logs"] = false };
        var state = new AdminNavTopicState(() => stored, value => stored = new(value), activeTopic: "logs");
        Assert.True(state.IsOpen("logs"));

        state.Toggle("logs");
        Assert.False(state.IsOpen("logs"));
        Assert.False(stored["logs"]);
        state.Arrive(null);
        state.Arrive("logs");
        Assert.True(state.IsOpen("logs"));
    }

    [Fact]
    public void ClosedTopicKeepsChoiceAwayFromItsPagesAndTogglesMerge()
    {
        var stored = new Dictionary<string, bool>();
        var state = new AdminNavTopicState(() => stored, value => stored = new(value), activeTopic: null);
        Assert.False(state.IsOpen("logs"));
        stored["other"] = true; // written by the drawer instance meanwhile
        state.Toggle("logs");
        Assert.True(stored["logs"]);
        Assert.True(stored["other"]);
    }

    [Theory]
    [InlineData(AdminRoute.Dashboard, "Dashboard", "Admin", null)]
    [InlineData(AdminRoute.Activity, "Activity", "Admin Activity", null)]
    [InlineData(AdminRoute.ServerLog, "Logs", "Admin Logs", "logs")]
    [InlineData(AdminRoute.ClientDiagnostics, "Client Diagnostics", "Admin Client Diagnostics", "logs")]
    [InlineData(AdminRoute.LoggingSettings, "Settings", "Admin Settings", "logs")]
    public void RouteTitlesAndTopics(AdminRoute route, string header, string tab, string? topic)
    {
        Assert.Equal(header, AdminNavigation.HeaderTitle(route));
        Assert.Equal(tab, AdminNavigation.TabTitle(route));
        Assert.Equal(topic, AdminNavigation.TopicOf(route));
    }

    [Fact]
    public void BuildCardFallsBackLikeTheSidebar()
    {
        Assert.Equal("loading...", AdminNavigation.BuildDisplay(null, loading: true, failed: false));
        Assert.Equal("load failed", AdminNavigation.BuildDisplay(null, loading: false, failed: true));
        Assert.Equal("dev build", AdminNavigation.BuildDisplay(new AdminIndicatorBuildInfo { Available = false }, false, false));
        Assert.Equal("1234 · v1.2", AdminNavigation.BuildDisplay(new AdminIndicatorBuildInfo { Available = true, BuildNumber = 1234, Display = "v1.2" }, false, false));
        Assert.Equal("v1.2", AdminNavigation.BuildDisplay(new AdminIndicatorBuildInfo { Available = true, Display = "v1.2" }, false, false));
        Assert.Equal("Built 2026-11-14T00:00:00Z", AdminNavigation.BuildTitle(new AdminIndicatorBuildInfo { BuiltAt = "2026-11-14T00:00:00Z" }));
    }

    [Fact]
    public void ScopedSearchOnlyReachesMonitorPages()
    {
        Assert.Equal(5, AdminNavigation.SearchPages.Count);
        Assert.Equal(AdminRoute.LoggingSettings, Assert.Single(AdminNavigation.Search("log retention")).Route);
        Assert.Equal(AdminRoute.Activity, Assert.Single(AdminNavigation.Search("transcode")).Route);
        Assert.Empty(AdminNavigation.Search("users"));
    }

    [Theory]
    [InlineData("back")]
    [InlineData("logo")]
    public void ReturnToAppConfirmsDirtyLoggingBeforeNavigationCanShowAnError(string button)
    {
        var sourceRoot = Path.Combine(TestRepository.Root, "src", "SiloPlayer");
        var sidebar = File.ReadAllText(Path.Combine(sourceRoot, "Views", "Admin", "AdminShellPage.cs"));
        Assert.Contains($"{button}.Click += (_, _) => App.MainWindowInstance?.NavigateToHome();", sidebar);

        var window = File.ReadAllText(Path.Combine(sourceRoot, "MainWindow.xaml.cs"));
        var start = window.IndexOf("public async void NavigateToHome()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = window.IndexOf("public bool TryEnterAuthenticatedPage", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var home = window[start..end];
        Assert.Contains("if (_adminNavigationPending) return;", home);
        Assert.Contains("shell.RequiresLeaveConfirmation && _adminAuthority?.IsAvailable == true", home);
        var confirmation = home.IndexOf("if (!await shell.CanLeaveAsync() || _windowClosed || !ReferenceEquals(ContentFrame.Content, shell)) return;", StringComparison.Ordinal);
        var approved = home.IndexOf("_adminNavigationApproved = true;", StringComparison.Ordinal);
        var navigate = home.IndexOf("_navigationService.Navigate<HomePage>()", StringComparison.Ordinal);
        var error = home.IndexOf("ShowPlaybackError(", StringComparison.Ordinal);
        Assert.True(confirmation >= 0 && approved > confirmation && navigate > approved && error > navigate,
            "Cancel must return before Frame navigation and ErrorState; Discard must bypass the Frame guard's queued dirty health.");
        Assert.Contains("NavView.SelectedItem = HomeNavItem;", home);
        Assert.Contains("finally { _adminNavigationApproved = false; _adminNavigationPending = false; }", home);
    }

    [Fact]
    public void IcuSubsetFormatsPluralsSelectsAndPound()
    {
        Assert.Equal("3 live", AdminText.T("web.admin.shell.sidebar.liveSessions", AdminText.Count(3)));
        Assert.Equal("Showing 1 of 1 stream", AdminActivityListState.ShowingText(1, 1));
        Assert.Equal("about 4 min left", AdminText.T("web.admin.downloads.preparation.remaining.minutes", new { minutes = 4 }));
        Assert.Equal("The message command was not applied: x",
            AdminText.T("web.admin.sessions.playback.failure.notApplied", new { action = "message", reason = "x" }));
        Assert.Equal("Server activity: 1 active",
            AdminText.T("web.admin.common.serverActivity.trigger.active", new { count = 1, count_text = "1" }));
        Assert.Equal("missing.key", AdminText.T("missing.key"));
    }
}
