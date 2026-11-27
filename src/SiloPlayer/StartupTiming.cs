using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer;

/// <summary>One best-effort timing record for the primary window, including its first WinUI-rendered frame.</summary>
internal sealed class StartupTiming
{
    private readonly record struct Measurements(long Constructor, long Created, long Activated, long FirstFrame,
        long RestoreStart, long RestoreEnd, long HomeFrame);

    private readonly long _main = Stopwatch.GetTimestamp();
    private readonly long _mainUtc = DateTime.UtcNow.Ticks;
    private readonly string? _launcherStart = Environment.GetEnvironmentVariable(SideBySideInstallation.LaunchStartEnvironmentVariable);
    private Window? _window;
    private long _constructor, _created, _activated, _firstFrame, _restoreStart, _restoreEnd, _homeFrame;
    private bool _restoreResolved, _waitingForHome, _watchingHome;

    internal static StartupTiming? Current { get; private set; }

    internal static void Start()
    {
        Current = new StartupTiming();
        if (Current._launcherStart is not null)
            Environment.SetEnvironmentVariable(SideBySideInstallation.LaunchStartEnvironmentVariable, null);
    }

    internal void AppConstructing() => _constructor = Stopwatch.GetTimestamp();

    internal void WindowCreated(Window window)
    {
        _created = Stopwatch.GetTimestamp();
        _window = window;
        window.Closed += OnWindowClosed;
        CompositionTarget.Rendered += OnFirstFrame;
        _ = FinishAfterDeadlineAsync(window.DispatcherQueue);
    }

    internal void WindowActivated()
    {
        _activated = Stopwatch.GetTimestamp();
        TryComplete();
    }

    internal void RestoreStarted() => _restoreStart = Stopwatch.GetTimestamp();

    internal void RestoreFinished(bool waitingForHome)
    {
        if (_restoreStart != 0) _restoreEnd = Stopwatch.GetTimestamp();
        _restoreResolved = true;
        _waitingForHome = waitingForHome;
        TryComplete();
    }

    internal void ObserveHomeContent()
    {
        if (_watchingHome || _homeFrame != 0) return;
        _watchingHome = true;
        CompositionTarget.Rendered += OnHomeFrame;
    }

    private void OnFirstFrame(object? sender, object args)
    {
        CompositionTarget.Rendered -= OnFirstFrame;
        _firstFrame = Stopwatch.GetTimestamp();
        TryComplete();
    }

    private void OnHomeFrame(object? sender, object args)
    {
        CompositionTarget.Rendered -= OnHomeFrame;
        _watchingHome = false;
        _homeFrame = Stopwatch.GetTimestamp();
        TryComplete();
    }

    private void TryComplete()
    {
        if (_activated != 0 && _firstFrame != 0 && _restoreResolved && (!_waitingForHome || _homeFrame != 0))
            Complete();
    }

    private async Task FinishAfterDeadlineAsync(DispatcherQueue dispatcher)
    {
        await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        dispatcher.TryEnqueue(Complete);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => Complete();

    private void Complete()
    {
        if (!ReferenceEquals(Current, this)) return;
        Current = null;
        if (_firstFrame == 0) CompositionTarget.Rendered -= OnFirstFrame;
        if (_watchingHome) CompositionTarget.Rendered -= OnHomeFrame;
        if (_window is { } window) window.Closed -= OnWindowClosed;
        _window = null;
        var measurements = new Measurements(_constructor, _created, _activated, _firstFrame, _restoreStart, _restoreEnd, _homeFrame);
        _ = Task.Run(() => Write(measurements));
    }

    private void Write(Measurements measurements)
    {
        long startUtc;
        try
        {
            using var process = Process.GetCurrentProcess();
            startUtc = process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            startUtc = _mainUtc;
        }

        var mainMs = Math.Max(0, (_mainUtc - startUtc) / (double)TimeSpan.TicksPerMillisecond);
        string Offset(long timestamp) => timestamp == 0 ? "-" :
            (mainMs + Stopwatch.GetElapsedTime(_main, timestamp).TotalMilliseconds).ToString("F0", CultureInfo.InvariantCulture);
        var launcherMs = long.TryParse(_launcherStart, NumberStyles.None, CultureInfo.InvariantCulture, out var launcherStart)
            && launcherStart > 0 && launcherStart <= startUtc && startUtc - launcherStart <= TimeSpan.FromMinutes(2).Ticks
            ? ((startUtc - launcherStart) / (double)TimeSpan.TicksPerMillisecond).ToString("F0", CultureInfo.InvariantCulture)
            : "-";
        LocalLog.AppendLine("startup_timing.txt",
            $"launcher_to_app_ms={launcherMs} main_ms={Offset(_main)} app_ctor_ms={Offset(measurements.Constructor)} " +
            $"window_created_ms={Offset(measurements.Created)} activated_ms={Offset(measurements.Activated)} first_frame_ms={Offset(measurements.FirstFrame)} " +
            $"restore_start_ms={Offset(measurements.RestoreStart)} restore_end_ms={Offset(measurements.RestoreEnd)} home_frame_ms={Offset(measurements.HomeFrame)}",
            maxBytes: 256 * 1024, archiveCount: 1);
    }
}
