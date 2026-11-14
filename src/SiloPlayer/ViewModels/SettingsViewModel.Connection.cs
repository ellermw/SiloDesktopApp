using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

public partial class SettingsViewModel
{
    private ApiRequestContext? _bufferingSettingsOwner;
    private ApiRequestContext? _connectionSettingsOwner;
    private long _connectionSettingsRevision;
    [ObservableProperty] private string _bufferingQualityMode = "always";
    [ObservableProperty] private bool _connectionSettingsBusy;
    [ObservableProperty] private bool _bufferingQualitySupported;
    [ObservableProperty] private bool _connectionSpeedSupported;
    [ObservableProperty] private bool _connectionTestRunning;
    [ObservableProperty] private bool _connectionMetered;
    [ObservableProperty] private string _connectionBudget = "";
    [ObservableProperty] private string _connectionSettingsMessage = "";
    [ObservableProperty] private string _connectionReading = AppStrings.Get("web.settings.playbackConnection.notMeasured");
    [ObservableProperty] private string _learnedConnectionLimit = "";
    [ObservableProperty] private bool _bufferingDeviceOverride;
    public bool CanResetLearnedLimit { get; private set; }
    private PlaybackConnectionService ConnectionObserver => PlaybackConnectionService.For(App.Services.GetRequiredService<PlaybackApi>());
    private CancellationTokenSource? _connectionTestCancellation;
    private long _connectionMessageRevision;
    public void CancelConnectionSpeedTest() => _connectionTestCancellation?.Cancel();
    public void RefreshConnectionPresentation()
    {
        ApplyConnectionState(ConnectionObserver.State);
        var network = PlaybackConnectionNetwork.Capture(); SetConnectionNetwork(network.Metered, network.Transport);
    }
    public async Task LoadConnectionSettingsAsync(CancellationToken ct = default)
    {
        var revision = ++_connectionSettingsRevision;
        var context = _settingsApi.CaptureContext();
        ConnectionSettingsBusy = true; BufferingQualitySupported = ConnectionSpeedSupported = false; _bufferingSettingsOwner = _connectionSettingsOwner = null;
        ConnectionReading = AppStrings.Get("web.settings.playbackConnection.notMeasured"); LearnedConnectionLimit = ConnectionBudget = "";
        try
        {
            var api = App.Services.GetRequiredService<PlaybackApi>();
            var cap = await api.GetConnectionCapabilityAsync(api.CaptureConnectionOwner(), ct);
            if (!_settingsApi.IsCurrentContext(context) || revision != _connectionSettingsRevision) return;
            ConnectionSpeedSupported = cap.Features.Contains("connection_speed_v1");
            _connectionSettingsOwner = context;
            var network = PlaybackConnectionNetwork.Capture();
            try { await ConnectionObserver.RefreshAsync(network.Key, network.Metered, network.Transport, false, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* A connection read does not gate the buffering preference or a new test. */ }
            if (!_settingsApi.IsCurrentContext(context) || revision != _connectionSettingsRevision) return;
            ApplyConnectionState(ConnectionObserver.State);
            SetConnectionNetwork(network.Metered, network.Transport);
            if (!cap.Features.Contains("buffering_quality_v1")) { ConnectionSettingsMessage = "Connection quality controls require a newer server."; return; }
            var values = await _settingsApi.GetContractEffectiveSettingsAsync(context, ["playback.buffering_quality_mode"], ct: ct);
            if (!_settingsApi.IsCurrentContext(context) || revision != _connectionSettingsRevision) return;
            var entry = values.Settings.FirstOrDefault(e => e.Key == "playback.buffering_quality_mode");
            if (entry?.Value.ValueKind != System.Text.Json.JsonValueKind.String || entry.Value.GetString() is not ("never" or "ask" or "always"))
                throw new InvalidDataException("The buffering preference did not load.");
            BufferingQualityMode = entry.Value.GetString()!; BufferingDeviceOverride = entry.Source == "profile_device";
            _bufferingSettingsOwner = context; BufferingQualitySupported = true; ConnectionSettingsMessage = "";
            ApplyConnectionState(ConnectionObserver.State);
            SetConnectionNetwork(network.Metered, network.Transport);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_settingsApi.IsCurrentContext(context) && revision == _connectionSettingsRevision) ConnectionSettingsMessage = ex.DisplayMessage(); }
        finally { if (revision == _connectionSettingsRevision) ConnectionSettingsBusy = false; }
    }
    public async Task SaveBufferingQualityModeAsync(string mode, bool thisDevice)
    {
        if (_bufferingSettingsOwner is not { } owner || !_settingsApi.IsCurrentContext(owner) || ConnectionSettingsBusy || mode is not ("never" or "ask" or "always")) return;
        ConnectionSettingsBusy = true;
        string? failure = null;
        try
        {
            if (thisDevice) await _settingsApi.SetContractSettingValueAsync(owner, "playback.buffering_quality_mode", "profile_device", mode);
            else await _settingsApi.SaveProfileDefaultAsync(owner, "playback.buffering_quality_mode", mode, BufferingDeviceOverride);
        }
        catch (Exception ex) { failure = ex.DisplayMessage(); }
        finally
        {
            ConnectionSettingsBusy = false; await LoadConnectionSettingsAsync();
            if (failure != null && _settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = failure;
        }
    }
    public async Task TestConnectionSpeedAsync()
    {
        if (ConnectionSettingsBusy || !ConnectionSpeedSupported || _connectionSettingsOwner is not { } owner || !_settingsApi.IsCurrentContext(owner)) return;
        using var stop = new CancellationTokenSource(); _connectionTestCancellation = stop;
        ConnectionSettingsBusy = ConnectionTestRunning = true; ConnectionSettingsMessage = ""; ++_connectionMessageRevision;
        try
        {
            var network = PlaybackConnectionNetwork.Capture();
            SetConnectionNetwork(network.Metered, network.Transport);
            if (network.Metered) return;
            await ConnectionObserver.TestAsync(network.Key, network.Metered, network.Transport, stop.Token);
            if (_settingsApi.IsCurrentContext(owner)) ApplyConnectionState(ConnectionObserver.State);
        }
        catch (Exception ex)
        {
            if (!_settingsApi.IsCurrentContext(owner) || stop.IsCancellationRequested) return;
            var network = PlaybackConnectionNetwork.Capture(); SetConnectionNetwork(network.Metered, network.Transport);
            if (network.Metered) return;
            ConnectionSettingsMessage = ex switch
            {
                ApiException { StatusCode: 429 } rate => ApiProblemText.Display(rate),
                ConnectionProbeNotSavedException => AppStrings.Get("web.settings.playbackConnection.notSaved"),
                // Branching stays on the observer's stable English exception text; only the display translates.
                InvalidOperationException busy when busy.Message.StartsWith("Stop playback", StringComparison.Ordinal) => AppStrings.Get("web.settings.playbackConnection.stopPlayback"),
                InvalidOperationException busy when busy.Message.StartsWith("A speed test", StringComparison.Ordinal) => AppStrings.Get("web.settings.playbackConnection.busy", fallback: busy.Message),
                OperationCanceledException when ConnectionObserver.IsMediaActive => AppStrings.Get("web.settings.playbackConnection.stopPlayback"),
                _ => AppStrings.Get("web.settings.playbackConnection.testFailed"),
            };
            if (ex is ApiException { StatusCode: 429 } limited)
                _ = ClearConnectionMessageAsync(++_connectionMessageRevision, owner, limited.RetryAfterSeconds ?? 30);
        }
        finally { if (ReferenceEquals(_connectionTestCancellation, stop)) _connectionTestCancellation = null; ConnectionSettingsBusy = ConnectionTestRunning = false; }
    }
    public async Task ResetConnectionLimitAsync()
    {
        if (ConnectionSettingsBusy || _connectionSettingsOwner is not { } owner || !_settingsApi.IsCurrentContext(owner)) return;
        ConnectionSettingsBusy = true;
        try { await ConnectionObserver.ResetAsync(); if (_settingsApi.IsCurrentContext(owner)) { ApplyConnectionState(ConnectionObserver.State); ConnectionSettingsMessage = ""; } }
        catch (Exception) { if (_settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = AppStrings.Get("web.settings.playbackConnection.resetFailed"); }
        finally { ConnectionSettingsBusy = false; }
    }
    private void ApplyConnectionState(PlaybackConnectionState? state)
    {
        PlaybackConnectionSample? sample = state?.Probe ?? (PlaybackConnectionSample?)state?.PlaybackSample;
        var lower = ReferenceEquals(sample, state?.PlaybackSample) || sample is PlaybackConnectionProbe { LowerBound: true };
        var transport = sample is PlaybackConnectionProbe probe ? probe.Transport switch
        {
            "wifi" => AppStrings.Get("web.settings.playbackConnection.transport.wifi"),
            "ethernet" => AppStrings.Get("web.settings.playbackConnection.transport.ethernet"),
            "cellular" => AppStrings.Get("web.settings.playbackConnection.transport.cellular"),
            _ => AppStrings.Get("web.settings.playbackConnection.transport.unknown"),
        } : "";
        ConnectionReading = sample == null ? AppStrings.Get("web.settings.playbackConnection.notMeasured") : sample is PlaybackConnectionProbe
            ? AppStrings.Get("web.settings.playbackConnection.measured", Args(("rate", lower ? AppStrings.Get("web.settings.playbackConnection.fasterThan", Args(("rate", ConnectionRate(sample.Kbps)))) : ConnectionRate(sample.Kbps)),
                ("ago", MeasuredAgo(sample.MeasuredAt)), ("transport", transport)))
            : AppStrings.Get("web.settings.playbackConnection.seenDuringPlayback", Args(("rate", ConnectionRate(sample.Kbps)), ("ago", MeasuredAgo(sample.MeasuredAt))));
        var limit = state?.LearnedLimit is { } l && l.ExpiresAt > DateTimeOffset.UtcNow ? l : null;
        LearnedConnectionLimit = limit == null || BufferingQualityMode == "never" ? "" : AppStrings.Get("web.settings.playbackConnection.learnedLimit",
            Args(("rate", ConnectionRate(limit.Kbps)), ("date", DateTimeDisplay.FormatDate(limit.ObservedAt, medium: true))));
        ConnectionBudget = state?.AutoBudgetKbps is int budget && !(BufferingQualityMode == "never" && limit != null)
            ? AppStrings.Get("web.settings.playbackConnection.autoBudget", Args(("rate", ConnectionRate(budget)))) : "";
        CanResetLearnedLimit = limit != null; OnPropertyChanged(nameof(CanResetLearnedLimit));
    }
    private void SetConnectionNetwork(bool metered, string transport)
    {
        ConnectionMetered = metered;
        if (metered) ConnectionReading = transport == "cellular"
            ? AppStrings.Get("web.settings.playbackConnection.skippedCellular")
            : AppStrings.Get("web.settings.playbackConnection.skippedMetered") + " " +
                AppStrings.Get("web.settings.playbackConnection.meteredQualityApplies", fallback: "Metered quality and bitrate settings apply to playback.");
    }

    /// <summary>Re-reads the connection summary in the current interface language (retained settings view).</summary>
    public void RefreshConnectionLocale()
    {
        if (ConnectionMetered) SetConnectionNetwork(true, PlaybackConnectionNetwork.Capture().Transport);
        else ApplyConnectionState(ConnectionObserver.State);
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value);
    private async Task ClearConnectionMessageAsync(long revision, ApiRequestContext owner, double seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 86400)));
        if (revision == _connectionMessageRevision && _settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = "";
    }
    private static string ConnectionRate(int kbps) => kbps < 1000
        ? AppStrings.Get("web.common.format.bitrate.kbps", Args(("rate", Math.Max(0, kbps).ToString("N0", AppStrings.FormatCulture))))
        : AppStrings.Get("web.common.format.bitrate.mbps", Args(("rate", kbps < 10000 ? (Math.Floor(kbps / 100d) / 10).ToString("0.0", AppStrings.FormatCulture) : (kbps / 1000).ToString("N0", AppStrings.FormatCulture))));
    private static string MeasuredAgo(DateTimeOffset at)
    {
        var minutes = Math.Max(0, (int)(DateTimeOffset.UtcNow - at).TotalMinutes);
        if (minutes == 0) return AppStrings.Get("web.settings.playbackConnection.ago.justNow");
        var (count, key) = minutes < 60 ? (minutes, "minutes") : minutes < 1440 ? (minutes / 60, "hours") : (minutes / 1440, "days");
        return AppStrings.Get("web.settings.playbackConnection.ago." + key, Args(("count", count), ("count_text", count.ToString("N0", AppStrings.FormatCulture))));
    }
}
