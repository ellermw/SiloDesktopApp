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
    private long _connectionSettingsRevision;
    [ObservableProperty] private string _bufferingQualityMode = "always";
    [ObservableProperty] private bool _connectionSettingsBusy;
    [ObservableProperty] private bool _bufferingQualitySupported;
    [ObservableProperty] private string _connectionSettingsMessage = "";
    [ObservableProperty] private string _connectionReading = "No recent connection reading";
    [ObservableProperty] private string _learnedConnectionLimit = "";
    [ObservableProperty] private bool _bufferingDeviceOverride;
    public bool CanResetLearnedLimit { get; private set; }
    private PlaybackConnectionService ConnectionObserver => PlaybackConnectionService.For(App.Services.GetRequiredService<PlaybackApi>());
    public async Task LoadConnectionSettingsAsync(CancellationToken ct = default)
    {
        var revision = ++_connectionSettingsRevision;
        var context = _settingsApi.CaptureContext();
        ConnectionSettingsBusy = true; BufferingQualitySupported = false; _bufferingSettingsOwner = null;
        ConnectionReading = "No recent connection reading"; LearnedConnectionLimit = "";
        try
        {
            var api = App.Services.GetRequiredService<PlaybackApi>();
            var cap = await api.GetConnectionCapabilityAsync(api.CaptureConnectionOwner(), ct);
            if (!_settingsApi.IsCurrentContext(context) || revision != _connectionSettingsRevision) return;
            if (!cap.Features.Contains("buffering_quality_v1")) { ConnectionSettingsMessage = "Connection quality controls require a newer server."; return; }
            var values = await _settingsApi.GetContractEffectiveSettingsAsync(context, ["playback.buffering_quality_mode"], ct: ct);
            if (!_settingsApi.IsCurrentContext(context) || revision != _connectionSettingsRevision) return;
            var entry = values.Settings.FirstOrDefault(e => e.Key == "playback.buffering_quality_mode");
            if (entry?.Value.ValueKind != System.Text.Json.JsonValueKind.String || entry.Value.GetString() is not ("never" or "ask" or "always"))
                throw new InvalidDataException("The buffering preference did not load.");
            BufferingQualityMode = entry.Value.GetString()!; BufferingDeviceOverride = entry.Source == "profile_device";
            _bufferingSettingsOwner = context; BufferingQualitySupported = true; ConnectionSettingsMessage = "";
            var network = PlaybackConnectionNetwork.Capture();
            await ConnectionObserver.RefreshAsync(network.Key, network.Metered, network.Transport, false, ct);
            if (!_settingsApi.IsCurrentContext(context) || revision != _connectionSettingsRevision) return;
            ApplyConnectionState(ConnectionObserver.State);
            if (network.Metered) ConnectionSettingsMessage = "Speed tests are unavailable on a metered connection.";
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
        if (ConnectionSettingsBusy || _bufferingSettingsOwner is not { } owner || !_settingsApi.IsCurrentContext(owner)) return;
        ConnectionSettingsBusy = true;
        try
        {
            var network = PlaybackConnectionNetwork.Capture();
            await ConnectionObserver.TestAsync(network.Key, network.Metered, network.Transport);
            if (_settingsApi.IsCurrentContext(owner)) { ApplyConnectionState(ConnectionObserver.State); ConnectionSettingsMessage = "Connection speed measured."; }
        }
        catch (Exception ex) { if (_settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = ex is OperationCanceledException ? "The test stopped because playback or the connection changed." : ex.Message; }
        finally { ConnectionSettingsBusy = false; }
    }
    public async Task ResetConnectionLimitAsync()
    {
        if (ConnectionSettingsBusy || _bufferingSettingsOwner is not { } owner || !_settingsApi.IsCurrentContext(owner)) return;
        ConnectionSettingsBusy = true;
        try { await ConnectionObserver.ResetAsync(); if (_settingsApi.IsCurrentContext(owner)) { ApplyConnectionState(ConnectionObserver.State); ConnectionSettingsMessage = "Learned limit reset."; } }
        catch (Exception ex) { if (_settingsApi.IsCurrentContext(owner)) ConnectionSettingsMessage = ex.Message; }
        finally { ConnectionSettingsBusy = false; }
    }
    private void ApplyConnectionState(PlaybackConnectionState? state)
    {
        PlaybackConnectionSample? sample = state?.Probe ?? (PlaybackConnectionSample?)state?.PlaybackSample;
        var lower = ReferenceEquals(sample, state?.PlaybackSample) || sample is PlaybackConnectionProbe { LowerBound: true };
        var transport = sample is PlaybackConnectionProbe probe ? $" · {probe.Transport}" : " · playback sample";
        ConnectionReading = sample == null ? "No recent connection reading" : $"{(lower ? "At least " : "")}{sample.Kbps / 1000d:0.#} Mbps{transport} · measured {sample.MeasuredAt.ToLocalTime():g}";
        var limit = state?.LearnedLimit is { } l && l.ExpiresAt > DateTimeOffset.UtcNow ? l : null;
        LearnedConnectionLimit = limit == null ? "No learned connection limit" : $"Saved limit after buffering: {limit.Kbps / 1000d:0.#} Mbps · expires {limit.ExpiresAt.ToLocalTime():g}{(BufferingQualityMode == "never" ? " · ignored while keeping quality" : "")}";
        CanResetLearnedLimit = limit != null; OnPropertyChanged(nameof(CanResetLearnedLimit));
    }
}
