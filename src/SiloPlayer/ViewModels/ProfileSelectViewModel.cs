using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class ProfileSelectViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly SettingsApi _settingsApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _profilesCts;
    private CancellationTokenSource? _activationCts;

    public ProfileSelectViewModel(AuthApi authApi, SettingsApi settingsApi, CatalogApi catalogApi, AuthService authService, SettingsService settingsService)
    {
        _authApi = authApi;
        _settingsApi = settingsApi;
        _catalogApi = catalogApi;
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

    public bool ShouldShowTasteSeed { get; private set; }

    /// <summary>
    /// Event raised when a profile is successfully selected (including PIN verification if needed).
    /// </summary>
    public event Action? ProfileSelected;
    public event Action? TasteSeedRequired;

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _profilesCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _authApi.GetProfilesAsync(cts.Token);
            if (!ReferenceEquals(_profilesCts, cts)) return;
            Profiles.Clear();
            foreach (var profile in response.Profiles)
            {
                Profiles.Add(profile);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_profilesCts, cts)) ErrorMessage = $"Failed to load profiles: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _profilesCts, null, cts), cts))
                IsLoading = false;
            cts.Dispose();
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
    private async Task CreateProfileAsync(CreateProfileRequest request)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var wasEmpty = Profiles.Count == 0;
            var profile = await _authApi.CreateProfileAsync(request.Name, request.Pin, request.IsChild);
            Profiles.Add(profile);
            if (wasEmpty)
            {
                if (string.IsNullOrWhiteSpace(request.Pin))
                {
                    await ActivateProfileAsync(profile, null);
                }
                else
                {
                    var verification = await _authApi.VerifyPinAsync(profile.Id, request.Pin);
                    if (verification.Valid)
                        await ActivateProfileAsync(profile, verification.ProfileToken);
                    else
                        ErrorMessage = "Profile created, but PIN verification failed.";
                }
            }
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

    /// <summary>
    /// Updates an existing profile via PUT /profiles/{id}. Accepts a dictionary of fields
    /// to change (name, pin, is_child, etc.) — only fields in the dict are sent.
    /// </summary>
    public async Task UpdateProfileAsync(string profileId, Dictionary<string, object?> updates)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var updated = await _settingsApi.UpdateProfileAsync(profileId, updates);
            // Replace the matching profile in the observable collection so the UI reflects changes
            for (int i = 0; i < Profiles.Count; i++)
            {
                if (Profiles[i].Id == profileId)
                {
                    Profiles[i] = updated;
                    break;
                }
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = $"Failed to update profile: {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DeleteProfileAsync(Profile profile)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _authApi.DeleteProfileAsync(profile.Id);
            Profiles.Remove(profile);
            if (SelectedProfile?.Id == profile.Id)
                SelectedProfile = null;
            if (_authService.SelectedProfileId == profile.Id)
                _authService.ClearSelectedProfile();
        }
        catch (ApiException ex)
        {
            ErrorMessage = $"Failed to delete profile: {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ActivateProfileAsync(Profile profile, string? profileToken)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _activationCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        _authService.SelectProfile(profile.Id, profileToken, profile);
        _catalogApi.InvalidateLibraryCache();

        // Save last profile and user info
        var settings = _settingsService.Load();
        settings.LastProfileId = profile.Id;
        if (_authService.CurrentUser != null)
        {
            settings.LastUserRole = _authService.CurrentUser.Role;
            settings.LastUsername = _authService.CurrentUser.Username;
        }
        _settingsService.Save(settings);

        // Enter Home immediately while the optional favorites gate resolves.
        // This mirrors the WebUI, which renders Home while that query is pending.
        ShouldShowTasteSeed = false;
        ProfileSelected?.Invoke();

        if (!settings.TasteSeedDismissedProfileIds.Contains(profile.Id, StringComparer.Ordinal))
        {
            try
            {
                var favorites = await _catalogApi.GetFavoritesAsync(cts.Token);
                if (ReferenceEquals(_activationCts, cts) && favorites.Items.Count == 0)
                {
                    ShouldShowTasteSeed = true;
                    TasteSeedRequired?.Invoke();
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch
            {
                // Onboarding is optional. A temporary favorites failure must not
                // prevent the profile from entering the application.
            }
        }
        Interlocked.CompareExchange(ref _activationCts, null, cts);
        cts.Dispose();
    }

    public void CancelProfileLoad()
    {
        var cts = Interlocked.Exchange(ref _profilesCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoading = false;
    }
}
