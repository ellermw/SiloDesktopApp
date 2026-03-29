using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class ProfileSelectViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly AuthService _authService;
    private readonly SettingsService _settingsService;

    public ProfileSelectViewModel(AuthApi authApi, AuthService authService, SettingsService settingsService)
    {
        _authApi = authApi;
        _authService = authService;
        _settingsService = settingsService;
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPinRequired;

    [ObservableProperty]
    private string _pin = "";

    [ObservableProperty]
    private string? _pinErrorMessage;

    /// <summary>
    /// Event raised when a profile is successfully selected (including PIN verification if needed).
    /// </summary>
    public event Action? ProfileSelected;

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _authApi.GetProfilesAsync();
            Profiles.Clear();
            foreach (var profile in response.Profiles)
            {
                Profiles.Add(profile);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectProfileAsync(Profile profile)
    {
        SelectedProfile = profile;
        ErrorMessage = null;

        if (profile.HasPin)
        {
            IsPinRequired = true;
            Pin = "";
            PinErrorMessage = null;
            return;
        }

        await ActivateProfileAsync(profile, null);
    }

    [RelayCommand]
    private async Task VerifyPinAsync()
    {
        if (SelectedProfile == null) return;

        if (string.IsNullOrWhiteSpace(Pin))
        {
            PinErrorMessage = "PIN is required.";
            return;
        }

        IsLoading = true;
        PinErrorMessage = null;

        try
        {
            var response = await _authApi.VerifyPinAsync(SelectedProfile.Id, Pin);
            if (response.Valid)
            {
                await ActivateProfileAsync(SelectedProfile, response.ProfileToken);
            }
            else
            {
                PinErrorMessage = "Incorrect PIN. Please try again.";
            }
        }
        catch (ApiException ex)
        {
            PinErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            PinErrorMessage = $"Failed to verify PIN: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelPin()
    {
        IsPinRequired = false;
        Pin = "";
        PinErrorMessage = null;
        SelectedProfile = null;
    }

    [RelayCommand]
    private async Task CreateProfileAsync(string name)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var profile = await _authApi.CreateProfileAsync(name);
            Profiles.Add(profile);
        }
        catch (ApiException ex)
        {
            ErrorMessage = $"Failed to create profile: {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ActivateProfileAsync(Profile profile, string? profileToken)
    {
        _authService.SelectProfile(profile.Id, profileToken);

        // Save last profile and user info
        var settings = _settingsService.Load();
        settings.LastProfileId = profile.Id;
        if (_authService.CurrentUser != null)
        {
            settings.LastUserRole = _authService.CurrentUser.Role;
            settings.LastUsername = _authService.CurrentUser.Username;
        }
        _settingsService.Save(settings);

        ProfileSelected?.Invoke();
        await Task.CompletedTask;
    }
}
