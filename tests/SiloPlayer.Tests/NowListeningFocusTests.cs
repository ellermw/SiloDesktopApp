using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

/// <summary>
/// Official Silo Server #4296 (385d90755): Now Listening makes the covered app
/// inert, keeps and starts focus inside, and returns focus on collapse.
/// </summary>
public sealed class NowListeningFocusTests
{
    private sealed class FakeElement(string name, bool enabled = true, int view = 0)
    {
        public string Name { get; } = name;
        public bool Enabled { get; set; } = enabled;
        public int View { get; set; } = view;
    }

    private const int Raw = 2;

    private static void MakeInert(ReversibleInertness scope, FakeElement element)
    {
        scope.Set(element, e => e.View, (e, v) => e.View = v, Raw);
        scope.Set(element, e => e.Enabled, (e, v) => e.Enabled = v, false);
    }

    [Fact]
    public void ApplyMakesTargetsInertAndRestoreReturnsTheExactOriginals()
    {
        var nav = new FakeElement("nav", enabled: true, view: 0);
        var activity = new FakeElement("activity", enabled: true, view: 1);
        var scope = new ReversibleInertness();

        Assert.True(scope.Begin());
        MakeInert(scope, nav);
        MakeInert(scope, activity);

        Assert.True(scope.IsApplied);
        Assert.Equal(4, scope.Count);
        Assert.All(new[] { nav, activity }, element => Assert.False(element.Enabled));
        Assert.All(new[] { nav, activity }, element => Assert.Equal(Raw, element.View));

        Assert.Equal(0, scope.Restore());
        Assert.False(scope.IsApplied);
        Assert.Equal(0, scope.Count);
        Assert.True(nav.Enabled);
        Assert.Equal(0, nav.View);
        Assert.True(activity.Enabled);
        Assert.Equal(1, activity.View);
    }

    [Fact]
    public void TargetsAlreadyInertBelongToAnotherOwnerAndAreNotRestored()
    {
        var disabled = new FakeElement("disabled", enabled: false, view: Raw);
        var scope = new ReversibleInertness();
        scope.Begin();

        Assert.False(scope.Set(disabled, e => e.Enabled, (e, v) => e.Enabled = v, false));
        Assert.False(scope.Set(disabled, e => e.View, (e, v) => e.View = v, Raw));
        Assert.Equal(0, scope.Count);

        scope.Restore();
        Assert.False(disabled.Enabled);
        Assert.Equal(Raw, disabled.View);
    }

    [Fact]
    public void RestoreNeverOverwritesAValueAnotherOwnerWroteWhileOpen()
    {
        var element = new FakeElement("toast", enabled: true, view: 0);
        var scope = new ReversibleInertness();
        scope.Begin();
        MakeInert(scope, element);

        element.View = 1; // another owner changed the view while Now Listening was open
        scope.Restore();

        Assert.Equal(1, element.View);
        Assert.True(element.Enabled);
    }

    [Fact]
    public void RepeatedOpenIsANoOpAndTheScopeIsReusableAfterRestore()
    {
        var element = new FakeElement("nav");
        var scope = new ReversibleInertness();

        Assert.Equal(0, scope.Restore());   // closing while closed changes nothing
        Assert.Throws<InvalidOperationException>(() => scope.Set(element, e => e.Enabled, (e, v) => e.Enabled = v, false));
        Assert.True(element.Enabled);

        for (var round = 0; round < 3; round++)
        {
            Assert.True(scope.Begin());
            MakeInert(scope, element);
            Assert.False(scope.Begin());     // repeated expansion keeps the first scope
            Assert.Equal(2, scope.Count);
            Assert.False(element.Enabled);
            scope.Restore();
            Assert.True(element.Enabled);
            Assert.Equal(0, element.View);
        }
    }

    [Fact]
    public void RestoreContinuesPastAFailingWriteAndReportsIt()
    {
        var first = new FakeElement("first");
        var second = new FakeElement("second");
        var scope = new ReversibleInertness();
        scope.Begin();
        MakeInert(scope, first);
        scope.Set(second, e => e.Enabled, (e, v) =>
        {
            if (v) throw new InvalidOperationException("element gone");
            e.Enabled = v;
        }, false);

        Assert.Equal(1, scope.Restore());
        Assert.True(first.Enabled);
        Assert.Equal(0, first.View);
        Assert.False(scope.IsApplied);
        Assert.Equal(0, scope.Count);
    }

    [Theory]
    // MainWindow: NavView/frame (z0, earlier), activity button (z40), Playing Next (z50),
    // toasts and the update button (z0, later) sit beneath Now Listening (z55, index 4).
    [InlineData(0, 0, true)]
    [InlineData(40, 1, true)]
    [InlineData(50, 7, true)]
    [InlineData(0, 8, true)]
    [InlineData(55, 3, true)]
    // The mini player (z60) and buffering pill (z65) draw above it and stay live.
    [InlineData(60, 5, false)]
    [InlineData(65, 6, false)]
    [InlineData(55, 9, false)]
    [InlineData(55, 4, false)]
    public void OnlySiblingsBeneathNowListeningAreCovered(int zIndex, int index, bool covered)
        => Assert.Equal(covered, ReversibleInertness.IsCovered(zIndex, index, overlayZIndex: 55, overlayIndex: 4));

    [Fact]
    public void PlayerStateChangesCaptureTheOpenerAndResyncTheScopeOnEveryPath()
    {
        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");
        var method = MethodBody(window, "private void OnPlayerStateChanged(PlayerState state)");
        var capture = method.IndexOf("var nowListeningOpener = state is PlayerState.Expanded or PlayerState.Fullscreen && _playerService.IsAudiobook", StringComparison.Ordinal);
        var switchStart = method.IndexOf("switch (state)", StringComparison.Ordinal);
        var collapseMini = method.IndexOf("MiniPlayerBarControl.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        var minimizedCase = method.IndexOf("case PlayerState.Minimized:", StringComparison.Ordinal);
        var sync = method.IndexOf("SyncNowListeningFocusScope(state, nowListeningOpener);", StringComparison.Ordinal);
        var chrome = method.IndexOf("UpdateAdminChrome();", StringComparison.Ordinal);

        Assert.True(capture >= 0 && capture < switchStart && capture < collapseMini, "The opener is captured before the mini player collapses.");
        Assert.True(sync > minimizedCase && sync < chrome, "The scope resyncs after every state case settles Now Listening.");
        Assert.Equal(1, Count(method, "SyncNowListeningFocusScope("));

        var focus = ReadRepoFile("src", "SiloPlayer", "MainWindow.NowListeningFocus.cs");
        Assert.Contains("if (AudiobookNowListeningControl.Visibility == Visibility.Visible)", focus);
        Assert.Contains("_nowListeningFocus.Open(RootGrid, AudiobookNowListeningControl, opener,", focus);
        Assert.Contains("AudiobookNowListeningControl.FocusCollapseButton);", focus);
        Assert.Contains("_nowListeningFocus.Close(AudiobookNowListeningControl,", focus);
        Assert.Contains("returnFocus: state is PlayerState.Minimized or PlayerState.Idle,", focus);
        Assert.Contains("preferred: MiniPlayerBarControl.FocusNowListeningOpener,", focus);
        Assert.Contains("fallbackScope: ContentFrame);", focus);
        Assert.Contains("_nowListeningFocus.IsOpen || Content?.XamlRoot is not { } root", focus);
    }

    [Fact]
    public void ScopeAppliesRawViewAndDisablesCoveredControlsWithoutHandlers()
    {
        var scope = ReadRepoFile("src", "SiloPlayer", "Helpers", "NowListeningFocusScope.cs");
        Assert.Contains("if (!_inertness.Begin()) return false;", scope);
        Assert.Contains("ReversibleInertness.IsCovered(Canvas.GetZIndex(element), index, overlayZIndex, overlayIndex)", scope);
        Assert.Contains("AutomationProperties.SetAccessibilityView(target, value)", scope);
        Assert.Contains("AccessibilityView.Raw);", scope);
        Assert.Contains("if (element is Control control)", scope);
        Assert.Contains("(target, value) => target.IsEnabled = value, false);", scope);
        Assert.Contains("_inertness.Restore();", scope);
        Assert.Contains("if (generation == _generation) focus(true);", scope);
        Assert.Contains("new WeakReference<DependencyObject>(opener)", scope);
        Assert.DoesNotContain("+=", scope);
    }

    [Fact]
    public void NowListeningCyclesTabAndExposesItsBackAndOpenerFocusTargets()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Controls", "AudiobookNowListening.xaml");
        Assert.Contains("TabFocusNavigation=\"Cycle\"", xaml);
        var back = xaml.IndexOf("x:Name=\"CollapseButton\"", StringComparison.Ordinal);
        Assert.True(back >= 0);
        Assert.Contains("Click=\"Collapse_Click\"", xaml[back..xaml.IndexOf("</Button>", back, StringComparison.Ordinal)]);
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "AudiobookNowListening.xaml.cs");
        Assert.Contains("public bool FocusCollapseButton() =>", code);
        Assert.Contains("NowListeningFocusScope.TryFocus(CollapseButton)", code);

        var mini = ReadRepoFile("src", "SiloPlayer", "Controls", "MiniPlayerBar.xaml.cs");
        Assert.Contains("public bool FocusNowListeningOpener() =>", mini);
        Assert.Contains("NowListeningFocusScope.TryFocus(AudiobookCoverButton)", mini);
        var miniXaml = ReadRepoFile("src", "SiloPlayer", "Controls", "MiniPlayerBar.xaml");
        var cover = miniXaml.IndexOf("x:Name=\"AudiobookCoverButton\"", StringComparison.Ordinal);
        Assert.True(cover >= 0);
        Assert.Contains("Click=\"Expand_Click\"", miniXaml[cover..miniXaml.IndexOf('>', cover)]);
    }

    [Fact]
    public void FocusContainmentAddsNoPageMotion()
    {
        foreach (var source in new[]
                 {
                     ReadRepoFile("src", "SiloPlayer", "MainWindow.NowListeningFocus.cs"),
                     ReadRepoFile("src", "SiloPlayer", "Helpers", "NowListeningFocusScope.cs"),
                     ReadRepoFile("src", "SiloPlayer", "Helpers", "ReversibleInertness.cs"),
                 })
        {
            Assert.DoesNotContain("Storyboard", source);
            Assert.DoesNotContain("Transition", source);
            Assert.DoesNotContain("Animation", source);
        }
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = source.IndexOf(value, StringComparison.Ordinal); index >= 0; index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[open..(index + 1)];
        }
        throw new InvalidOperationException($"No closing brace for {signature}.");
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
