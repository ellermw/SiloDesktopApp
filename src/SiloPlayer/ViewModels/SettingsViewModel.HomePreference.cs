using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class SettingsViewModel
{
    private NewEpisodesFirstSnapshot? _newEpisodesSnapshot;
    private CancellationTokenSource? _newEpisodesOperation;
    private bool _applyingNewEpisodes;
    private bool? _newEpisodesRetryValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeNewEpisodesFirst))]
    [NotifyPropertyChangedFor(nameof(NewEpisodesPreferenceVisible))]
    private bool _newEpisodesFirstSupported;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeNewEpisodesFirst))]
    private bool _newEpisodesPreferenceLoaded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeNewEpisodesFirst))]
    private bool _isNewEpisodesPreferencePending;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewEpisodesPreferenceVisible))]
    private string? _newEpisodesPreferenceError;
    [ObservableProperty] private string _newEpisodesPreferenceSource = "default";
    [ObservableProperty] private bool _showNewEpisodesFirst;

    public bool CanChangeNewEpisodesFirst => NewEpisodesFirstSupported && NewEpisodesPreferenceLoaded && !IsNewEpisodesPreferencePending;
    public bool NewEpisodesPreferenceVisible => NewEpisodesFirstSupported || NewEpisodesPreferenceError != null;

    partial void OnShowNewEpisodesFirstChanged(bool value)
    {
        if (_applyingNewEpisodes) return;
        if (!CanChangeNewEpisodesFirst) { ApplyNewEpisodesValue(_newEpisodesSnapshot?.Value ?? false); return; }
        _ = SaveNewEpisodesFirstAsync(value);
    }

    private void ApplyNewEpisodesValue(bool value)
    {
        _applyingNewEpisodes = true;
        try { ShowNewEpisodesFirst = value; }
        finally { _applyingNewEpisodes = false; }
    }

    private CancellationTokenSource BeginNewEpisodesOperation()
    {
        var previous = _newEpisodesOperation;
        _newEpisodesOperation = new();
        previous?.Cancel(); previous?.Dispose();
        IsNewEpisodesPreferencePending = true; NewEpisodesPreferenceError = null;
        return _newEpisodesOperation;
    }

    private bool IsNewEpisodesOwner(CancellationTokenSource operation, ApiRequestContext context) =>
        ReferenceEquals(_newEpisodesOperation, operation) && !operation.IsCancellationRequested && _settingsApi.IsCurrentContext(context);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReloadNewEpisodesPreferenceAsync()
    {
        var context = _settingsApi.CaptureContext();
        var operation = BeginNewEpisodesOperation();
        _newEpisodesSnapshot = null; _newEpisodesRetryValue = null;
        NewEpisodesFirstSupported = false; NewEpisodesPreferenceLoaded = false;
        ApplyNewEpisodesValue(false);
        try
        {
            var snapshot = await new NewEpisodesFirstPreference(_settingsApi).LoadAsync(context, operation.Token);
            if (!IsNewEpisodesOwner(operation, context)) return;
            ApplyNewEpisodesSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (!IsNewEpisodesOwner(operation, context)) { }
        catch (Exception)
        {
            if (IsNewEpisodesOwner(operation, context)) NewEpisodesPreferenceError = "Home preferences didn't load. Reload to try again.";
        }
        finally { if (IsNewEpisodesOwner(operation, context)) IsNewEpisodesPreferencePending = false; }
    }

    private void ApplyNewEpisodesSnapshot(NewEpisodesFirstSnapshot snapshot)
    {
        _newEpisodesSnapshot = snapshot;
        NewEpisodesFirstSupported = snapshot.Supported;
        NewEpisodesPreferenceSource = snapshot.Source;
        ApplyNewEpisodesValue(snapshot.Value);
        NewEpisodesPreferenceLoaded = true;
    }

    private async Task SaveNewEpisodesFirstAsync(bool value)
    {
        var snapshot = _newEpisodesSnapshot;
        if (snapshot == null || !_settingsApi.IsCurrentContext(snapshot.Context) || IsNewEpisodesPreferencePending) return;
        var operation = BeginNewEpisodesOperation();
        var committed = false;
        try
        {
            var preference = new NewEpisodesFirstPreference(_settingsApi);
            await preference.SaveAsync(snapshot, value, operation.Token);
            if (!IsNewEpisodesOwner(operation, snapshot.Context)) return;
            committed = true;
            _newEpisodesRetryValue = null;
            // Publish once when the mutation is accepted. A subsequent read
            // failure retries resolution only and must not reissue the write.
            WeakReferenceMessenger.Default.Send(new HomePreferenceChanged(snapshot.Context));
            var resolved = await preference.LoadAsync(snapshot.Context, operation.Token);
            if (IsNewEpisodesOwner(operation, snapshot.Context)) ApplyNewEpisodesSnapshot(resolved);
        }
        catch (OperationCanceledException) when (!IsNewEpisodesOwner(operation, snapshot.Context)) { }
        catch (Exception)
        {
            if (!IsNewEpisodesOwner(operation, snapshot.Context)) return;
            if (!committed)
            {
                ApplyNewEpisodesValue(snapshot.Value); _newEpisodesRetryValue = value;
                NewEpisodesPreferenceError = "Failed to save Home preference. Try again.";
            }
            else
            {
                NewEpisodesPreferenceLoaded = false;
                NewEpisodesPreferenceError = "Saved, but Home preferences didn't reload. Reload to try again.";
            }
        }
        finally { if (IsNewEpisodesOwner(operation, snapshot.Context)) IsNewEpisodesPreferencePending = false; }
    }

    [RelayCommand]
    private Task RetryNewEpisodesPreferenceAsync() => _newEpisodesRetryValue is { } value
        ? SaveNewEpisodesFirstAsync(value) : ReloadNewEpisodesPreferenceAsync();

    public void CloseNewEpisodesPreference()
    {
        var previous = _newEpisodesOperation; _newEpisodesOperation = null;
        previous?.Cancel(); previous?.Dispose();
        _newEpisodesSnapshot = null; _newEpisodesRetryValue = null;
        NewEpisodesPreferenceLoaded = false; NewEpisodesFirstSupported = false;
        IsNewEpisodesPreferencePending = false; NewEpisodesPreferenceError = null;
    }
}
