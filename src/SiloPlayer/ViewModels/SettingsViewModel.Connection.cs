using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

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
    [ObservableProperty] private string _connectionReading = "Not measured yet";
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
        ConnectionReading = "Not measured yet"; LearnedConnectionLimit = ConnectionBudget = "";
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
        catch (Exception ex) { if (_settingsApi.IsCurrentContext(context) && revision == _connectionSettingsRevision) ConnectionSettingsMessage = ex.Message; }
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
        catch (Exception ex) { failure = ex.Message; }
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
                ApiException { StatusCode: 429 } rate => rate.Message,
                ConnectionProbeNotSavedException => "Couldn't save the speed test. Try again.",
                InvalidOperationException busy when busy.Message.StartsWith("Stop playback", StringComparison.Ordinal) => "Stop playback before testing, then try again.",
                InvalidOperationException busy when busy.Message.StartsWith("A speed test", StringComparison.Ordinal) => busy.Message,
                OperationCanceledException when ConnectionObserver.IsMediaActive => "Stop playback before testing, then try again.",
                _ => "Couldn't measure the connection speed. Try again.",
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
        catch (Exception) { if (_settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = "Couldn't reset the limit. Try again."; }
        finally { ConnectionSettingsBusy = false; }
    }
    private void ApplyConnectionState(PlaybackConnectionState? state)
    {
        PlaybackConnectionSample? sample = state?.Probe ?? (PlaybackConnectionSample?)state?.PlaybackSample;
        var lower = ReferenceEquals(sample, state?.PlaybackSample) || sample is PlaybackConnectionProbe { LowerBound: true };
        var transport = sample is PlaybackConnectionProbe probe ? probe.Transport switch { "wifi" => "Wi-Fi", "ethernet" => "Ethernet", "cellular" => "cellular", _ => "this connection" } : "";
        ConnectionReading = sample == null ? "Not measured yet" : sample is PlaybackConnectionProbe
            ? $"{(lower ? "Faster than " : "")}{ConnectionRate(sample.Kbps)} · measured {MeasuredAgo(sample.MeasuredAt)} on {transport}"
            : $"Faster than {ConnectionRate(sample.Kbps)} · seen during playback {MeasuredAgo(sample.MeasuredAt)}";
        var limit = state?.LearnedLimit is { } l && l.ExpiresAt > DateTimeOffset.UtcNow ? l : null;
        LearnedConnectionLimit = limit == null || BufferingQualityMode == "never" ? "" : $"Limited to {ConnectionRate(limit.Kbps)} after buffering on {limit.ObservedAt.ToLocalTime():MMM d}";
        ConnectionBudget = state?.AutoBudgetKbps is int budget && !(BufferingQualityMode == "never" && limit != null)
            ? $"Auto plays videos up to {ConnectionRate(budget)} without converting them." : "";
        CanResetLearnedLimit = limit != null; OnPropertyChanged(nameof(CanResetLearnedLimit));
    }
    private void SetConnectionNetwork(bool metered, string transport)
    {
        ConnectionMetered = metered;
        if (metered) ConnectionReading = transport == "cellular"
            ? "Speed tests are skipped on cellular, and the Cellular quality settings apply."
            : "Speed tests are skipped on metered networks.";
    }
    private async Task ClearConnectionMessageAsync(long revision, ApiRequestContext owner, double seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 86400)));
        if (revision == _connectionMessageRevision && _settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = "";
    }
    private static string ConnectionRate(int kbps) => kbps < 1000 ? $"{Math.Max(0, kbps)} kbps" : kbps < 10000 ? $"{Math.Floor(kbps / 100d) / 10:0.0} Mbps" : $"{kbps / 1000} Mbps";
    private static string MeasuredAgo(DateTimeOffset at)
    {
        var minutes = Math.Max(0, (int)(DateTimeOffset.UtcNow - at).TotalMinutes);
        if (minutes == 0) return "just now";
        var (count, unit) = minutes < 60 ? (minutes, "minute") : minutes < 1440 ? (minutes / 60, "hour") : (minutes / 1440, "day");
        return $"{count} {unit}{(count == 1 ? "" : "s")} ago";
    }
}
