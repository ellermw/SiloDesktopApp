using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class RelaunchWindowPlacementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    // Primary 1920x1080 with a bottom taskbar; a 2560x1440 monitor to its left at negative coordinates.
    private static readonly DisplayMonitor Primary = new(@"\\.\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1032));
    private static readonly DisplayMonitor Left = new(@"\\.\DISPLAY2", new(-2560, -200, 0, 1240), new(-2560, -200, 0, 1192));

    private static SavedWindowPlacement OnLeft(ScreenRect normal, bool maximized = false, DateTimeOffset? saved = null) =>
        new(Left.DeviceName, Left.Bounds, Left.WorkArea, normal, maximized, saved ?? Now.AddSeconds(-40));

    [Fact]
    public void RestoresTheSameFrameAndMaximizedStateOnTheMonitorItWasOn()
    {
        var normal = ScreenRect.FromSize(-2200, 0, 1400, 900);
        var target = RelaunchWindowPlacement.Resolve(OnLeft(normal, maximized: true), [Primary, Left], Now);
        Assert.NotNull(target);
        Assert.Equal(normal, target.Bounds);
        Assert.True(target.Maximized);

        target = RelaunchWindowPlacement.Resolve(OnLeft(normal), [Left, Primary], Now);
        Assert.Equal(new WindowPlacementTarget(normal, false), target);
    }

    [Fact]
    public void FallsBackToNormalStartupPlacementWhenThatMonitorIsDisconnected()
    {
        var saved = OnLeft(ScreenRect.FromSize(-2200, 0, 1400, 900), maximized: true);
        Assert.Null(RelaunchWindowPlacement.FindMonitor(saved, [Primary]));
        Assert.Null(RelaunchWindowPlacement.Resolve(saved, [Primary], Now));
        Assert.Null(RelaunchWindowPlacement.Resolve(saved, [], Now));
    }

    [Fact]
    public void FindsTheSameArrangementAfterWindowsRenumbersItsMonitors()
    {
        var renamed = Left with { DeviceName = @"\\.\DISPLAY5" };
        var normal = ScreenRect.FromSize(-2200, 0, 1400, 900);
        Assert.Same(renamed, RelaunchWindowPlacement.FindMonitor(OnLeft(normal), [Primary, renamed]));
        Assert.Equal(normal, RelaunchWindowPlacement.Resolve(OnLeft(normal), [Primary, renamed], Now)!.Bounds);
        // Geometry wins over a name that now belongs to another monitor.
        var impostor = Primary with { DeviceName = Left.DeviceName };
        Assert.Same(renamed, RelaunchWindowPlacement.FindMonitor(OnLeft(normal), [impostor, renamed]));
    }

    [Fact]
    public void KeepsTheWindowOnTheSameMonitorAndInsideItsWorkAreaAfterAResolutionChange()
    {
        var smaller = Left with { Bounds = new(-1920, -200, 0, 880), WorkArea = new(-1920, -200, 0, 832) };
        var target = RelaunchWindowPlacement.Resolve(OnLeft(ScreenRect.FromSize(-2200, 0, 2400, 1300)), [Primary, smaller], Now)!;
        Assert.True(smaller.WorkArea.Contains(target.Bounds));
        Assert.Equal(smaller.WorkArea.Width, target.Bounds.Width);
        Assert.Equal(smaller.WorkArea.Height, target.Bounds.Height);

        // The offset from the work area's corner survives a monitor that moved.
        var moved = Left with { Bounds = new(1920, 0, 4480, 1440), WorkArea = new(1920, 0, 4480, 1392) };
        target = RelaunchWindowPlacement.Resolve(OnLeft(ScreenRect.FromSize(-2400, -100, 800, 600)), [Primary, moved], Now)!;
        Assert.Equal(ScreenRect.FromSize(2080, 100, 800, 600), target.Bounds);
    }

    [Fact]
    public void KeepsSnappedFramesWhoseInvisibleBordersCrossTheWorkAreaEdge()
    {
        // A window snapped to the left half reports its invisible resize borders outside the work area.
        var snapped = new ScreenRect(-2567, -200, -1273, 1199);
        Assert.Equal(snapped, RelaunchWindowPlacement.Resolve(OnLeft(snapped), [Primary, Left], Now)!.Bounds);
        // A frame mostly off the monitor is pulled back so its title bar can be reached.
        var offscreen = ScreenRect.FromSize(-600, 900, 1000, 700);
        var fitted = RelaunchWindowPlacement.Resolve(OnLeft(offscreen), [Primary, Left], Now)!.Bounds;
        Assert.True(Left.WorkArea.Contains(fitted));
        Assert.Equal((1000, 700), (fitted.Width, fitted.Height));
    }

    [Fact]
    public void CentresAMaximizedWindowsRestoreFrameLeftOnAnotherMonitor()
    {
        // Maximized on the left monitor after Win+Shift+Arrow from the primary; its restore frame is still there.
        var target = RelaunchWindowPlacement.Resolve(OnLeft(ScreenRect.FromSize(100, 100, 1200, 800), maximized: true), [Primary, Left], Now)!;
        Assert.True(target.Maximized);
        Assert.Equal(ScreenRect.FromSize(-1880, 96, 1200, 800), target.Bounds);
        Assert.True(Left.WorkArea.Contains(target.Bounds));
    }

    [Fact]
    public void IgnoresMissingStaleFutureAndInvalidRecords()
    {
        var normal = ScreenRect.FromSize(-2200, 0, 1400, 900);
        Assert.Null(RelaunchWindowPlacement.Resolve(null, [Primary, Left], Now));
        Assert.Null(RelaunchWindowPlacement.Resolve(OnLeft(normal, saved: Now - RelaunchWindowPlacement.MaximumAge - TimeSpan.FromSeconds(1)), [Primary, Left], Now));
        Assert.NotNull(RelaunchWindowPlacement.Resolve(OnLeft(normal, saved: Now - RelaunchWindowPlacement.MaximumAge), [Primary, Left], Now));
        Assert.Null(RelaunchWindowPlacement.Resolve(OnLeft(normal, saved: Now.AddMinutes(5)), [Primary, Left], Now));
        Assert.Null(RelaunchWindowPlacement.Resolve(OnLeft(new(-2200, 0, -2200, 900)), [Primary, Left], Now));
        Assert.Null(RelaunchWindowPlacement.Resolve(OnLeft(normal) with { MonitorDevice = "" }, [Primary, Left], Now));
    }

    [Fact]
    public void SavedPlacementIsTakenOnceAndBadFilesAreDropped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-relaunch-placement-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saved = OnLeft(ScreenRect.FromSize(-2200, 0, 1400, 900), maximized: true);
            RelaunchWindowPlacement.Save(directory, saved);
            Assert.Equal(new[] { RelaunchWindowPlacement.FileName }, Directory.GetFiles(directory).Select(f => Path.GetFileName(f)));
            Assert.Equal(saved, RelaunchWindowPlacement.Take(directory));
            Assert.Null(RelaunchWindowPlacement.Take(directory));

            var path = Path.Combine(directory, RelaunchWindowPlacement.FileName);
            File.WriteAllText(path, "{not json");
            Assert.Null(RelaunchWindowPlacement.Take(directory));
            Assert.False(File.Exists(path));
            File.WriteAllText(path, new string(' ', 8192));
            Assert.Null(RelaunchWindowPlacement.Take(directory));
            Assert.False(File.Exists(path));

            RelaunchWindowPlacement.Save(directory, saved);
            RelaunchWindowPlacement.Discard(directory);
            Assert.Null(RelaunchWindowPlacement.Take(directory));
            RelaunchWindowPlacement.Discard(directory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ManualUpdateCloseSavesThePlacementAndStartupRestoresItBeforeTheWindowIsShown()
    {
        var updates = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "MainWindow.Updates.cs"));
        var exit = updates[updates.IndexOf("private async Task<bool> PrepareAppUpdateExitAsync()", StringComparison.Ordinal)..];
        exit = exit[..exit.IndexOf("private void DetachAppUpdates()", StringComparison.Ordinal)];
        var save = exit.IndexOf("if(_manualUpdateRequested){SaveRelaunchPlacement();placementSaved=true;}", StringComparison.Ordinal);
        Assert.True(save > exit.IndexOf("await _playerService.AllPendingProgressSaves", StringComparison.Ordinal));
        // Saved before either handoff (small update window or full installer) lets this process exit.
        Assert.True(save < exit.IndexOf("stagedInstaller.ArmNormalClose()", StringComparison.Ordinal));
        Assert.True(save < exit.IndexOf("await installer.ScheduleAsync", StringComparison.Ordinal));
        // A deferred update keeps Silo open and drops the record.
        var deferred = exit[exit.IndexOf("if(allowClose)", StringComparison.Ordinal)..];
        Assert.Contains("if(placementSaved)DiscardRelaunchPlacement();", deferred[deferred.IndexOf("else", StringComparison.Ordinal)..]);

        var app = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "App.xaml.cs"));
        var apply = app.IndexOf("MainWindowInstance.ApplyRelaunchPlacement()", StringComparison.Ordinal);
        var activateFromArgument = app.IndexOf("MainWindowInstance.ActivateFromArgument(e.Arguments)", StringComparison.Ordinal);
        var activate = app.IndexOf("_window.Activate();", StringComparison.Ordinal);
        // Non-empty updater arguments also activate the window, so placement must precede that path too.
        Assert.True(apply > 0 && apply < activateFromArgument);
        Assert.True(activateFromArgument < activate);

        var window = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var changed = window[window.IndexOf("private void OnAppWindowChanged", StringComparison.Ordinal)..];
        Assert.Contains("TrackNormalWindowBounds();", changed[..changed.IndexOf("private async void OnWindowClosing", StringComparison.Ordinal)]);
    }

    [Fact]
    public void StartupReappliesAndChecksTheObservedFrameAfterFirstLayoutBeforeMaximizing()
    {
        var placement = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "MainWindow.RelaunchPlacement.cs"));
        var apply = placement[placement.IndexOf("internal void ApplyRelaunchPlacement()", StringComparison.Ordinal)..];
        apply = apply[..apply.IndexOf("private void OnRelaunchPlacementLoaded", StringComparison.Ordinal)];
        Assert.Contains("RestoreRelaunchBounds(target.Bounds);", apply);
        Assert.Contains("RootGrid.Loaded += OnRelaunchPlacementLoaded;", apply);

        var loaded = placement[placement.IndexOf("private void OnRelaunchPlacementLoaded", StringComparison.Ordinal)..];
        loaded = loaded[..loaded.IndexOf("private void RestoreRelaunchBounds", StringComparison.Ordinal)];
        // A one-shot callback repairs first-layout changes, then maximizes the corrected restore frame.
        var detach = loaded.IndexOf("RootGrid.Loaded -= OnRelaunchPlacementLoaded;", StringComparison.Ordinal);
        var restore = loaded.IndexOf("RestoreRelaunchBounds(target.Bounds);", StringComparison.Ordinal);
        var maximize = loaded.IndexOf("if (target.Maximized) MaximizeAfterRelaunch();", StringComparison.Ordinal);
        Assert.True(detach >= 0 && restore > detach && maximize > restore);
        Assert.Contains("_pendingRelaunchPlacement = null;", loaded);

        var bounds = placement[placement.IndexOf("private void RestoreRelaunchBounds", StringComparison.Ordinal)..];
        bounds = bounds[..bounds.IndexOf("private void MaximizeAfterRelaunch", StringComparison.Ordinal)];
        var move = bounds.IndexOf("AppWindow.MoveAndResize(rect);", StringComparison.Ordinal);
        var observed = bounds.IndexOf("var observed = ScreenRect.FromSize(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);", StringComparison.Ordinal);
        var retry = bounds.IndexOf("if (observed != bounds) AppWindow.MoveAndResize(rect);", StringComparison.Ordinal);
        var track = bounds.IndexOf("TrackNormalWindowBounds();", StringComparison.Ordinal);
        Assert.True(move >= 0 && observed > move && retry > observed && track > retry);
        // TrackNormalWindowBounds reads the actual native frame, not the requested rectangle.
        Assert.DoesNotContain("_lastNormalWindowBounds = bounds;", placement);
        Assert.Contains("_lastNormalWindowBounds = ScreenRect.FromSize(AppWindow.Position.X, AppWindow.Position.Y, size.Width, size.Height);", placement);
    }
}
