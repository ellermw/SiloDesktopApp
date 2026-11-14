using SiloPlayer.Core.Localization;
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
    private int _pinRevision;
    private CancellationTokenSource? _managementCts;
    private ApiRequestContext _managementOwner;
    private Profile? _managementPrimary;
    private bool _managementAdding;
    private ProfilesResponse _capabilities = new();
    public ProfileAddSession? AddSession { get; private set; }
    public Profile? PinProfile => _managementPrimary ?? SelectedProfile;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseProfileCards))]
    private bool _isManagementBusy;
    public bool CanUseProfileCards => !IsManagementBusy;
    public event Action? ManageProfilesSelected;

    public async Task BeginManagementAsync(bool adding)
    {
        CancelManagement();
        if (adding && !ProfileLimits.CanAdd(Profiles.Count, _capabilities.MaxProfiles)) return;
        ++_pinRevision;
        ErrorMessage = null; PinErrorMessage = null; Pin = "";
        var primary = Profiles.FirstOrDefault(profile => profile.IsPrimary);
        if (primary == null)
        { ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.profiles.picker.primaryMissing", fallback: "Couldn't find the primary profile needed to manage profiles.")); return; }
        _managementOwner = _authApi.CaptureContext();
        _managementCts = new(); _managementPrimary = primary; _managementAdding = adding;
        IsManagementBusy = true;
        if (primary.HasPin) { IsPinRequired = true; return; }
        await ContinueManagementAsync(null);
    }

    private async Task ContinueManagementAsync(string? proof)
    {
        var cts = _managementCts; var primary = _managementPrimary;
        if (cts == null || primary == null) return;
        bool Current() => ReferenceEquals(cts, _managementCts) && !cts.IsCancellationRequested
            && _authApi.IsCurrentContext(_managementOwner);
        try
        {
            if (!Current()) return;
            var authority = _authApi.CapturePrimaryAuthority(_managementOwner, primary, proof);
            if (!_managementAdding)
            {
                // Management deliberately changes the browsing identity and uses a
                // dedicated destination: no Home/favorites-onboarding transition.
                IsPinRequired = false; Pin = "";
                _authService.SelectProfile(primary.Id, proof, primary);
                _catalogApi.InvalidateLibraryCache();
                PersistProfile(primary);
                CancelManagement();
                ManageProfilesSelected?.Invoke();
                return;
            }
            var libraries = await _catalogApi.GetLibrariesAsync(authority, cts.Token);
            if (!Current()) return;
            AddSession = new(_authApi, authority, libraries, _capabilities, cts.Token);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception)
        {
            if (Current()) ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.profiles.picker.addFormFailed", fallback: "Couldn't open the new profile form. Try again."));
            if (Current()) CancelManagement();
        }
        finally
        {
            if (ReferenceEquals(cts, _managementCts) && AddSession == null && !IsPinRequired) IsManagementBusy = false;
        }
    }

    public async Task FinishAddAsync(ProfileAddSession session, bool saved)
    {
        if (!ReferenceEquals(AddSession, session)) return;
        // Closing after an upload failure keeps the already accepted profile.
        // Refresh only while this editor still belongs to the mounted picker and
        // original account; page departure retires AddSession before this point.
        if (saved && session.IsCurrent || session.CreatedProfile != null && session.IsOwnerCurrent)
            await LoadProfilesAsync();
        if (ReferenceEquals(AddSession, session)) CancelManagement();
    }

    public void CancelManagement()
    {
        var cts = Interlocked.Exchange(ref _managementCts, null);
        cts?.Cancel(); cts?.Dispose();
        AddSession = null; _managementPrimary = null;
        IsManagementBusy = false; IsPinRequired = false; Pin = ""; PinErrorMessage = null;
    }

    public ProfileSelectViewModel(AuthApi authApi, SettingsApi settingsApi, CatalogApi catalogApi, AuthService authService, SettingsService settingsService)
    {
        _authApi = authApi;
        _settingsApi = settingsApi;
        _catalogApi = catalogApi;
        _authService = authService;
        _settingsService = settingsService;
        Profiles.CollectionChanged += (_, _) =>
        {
            PickerCards.Clear();
            foreach (var profile in Profiles) PickerCards.Add(new(profile));
            if (ProfileLimits.CanAdd(Profiles.Count, _capabilities.MaxProfiles)) PickerCards.Add(new(null));
        };
    }

    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<ProfilePickerCard> PickerCards { get; } = [];

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
        var owner = _authApi.CaptureContext();

        try
        {
            var response = await _authApi.GetProfilesAsync(cts.Token);
            if (!ReferenceEquals(_profilesCts, cts) || cts.IsCancellationRequested || !_authApi.IsCurrentContext(owner)) return;
            _capabilities = response;
            Profiles.Clear();
            foreach (var profile in response.Profiles)
            {
                Profiles.Add(profile);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_profilesCts, cts) && _authApi.IsCurrentContext(owner)) ErrorMessage = ShellText(nameof(ErrorMessage), () => $"Failed to load profiles: {ShellProblem(ex, ex.Message)}");
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
        CancelManagement();
        ++_pinRevision;
        SelectedProfile = profile;
        ErrorMessage = null;

        if (profile.HasPin)
        {
            IsPinRequired = true;
            Pin = "";
            PinErrorMessage = null; IsPinLockedOut = false;
            return;
        }

        await ActivateProfileAsync(profile, null);
    }

    [RelayCommand]
    private async Task VerifyPinAsync()
    {
        IsPinLockedOut = false;
        if (_managementPrimary != null)
        { await VerifyManagementPinAsync(); return; }
        if (SelectedProfile == null) return;

        if (string.IsNullOrWhiteSpace(Pin))
        {
            PinErrorMessage = ShellText(nameof(PinErrorMessage), () => "PIN is required.");
            return;
        }

        IsLoading = true;
        PinErrorMessage = null;
        var profile = SelectedProfile;
        var revision = ++_pinRevision;
        var context = _settingsApi.CaptureContext();
        bool IsCurrent() => revision == _pinRevision && ReferenceEquals(SelectedProfile, profile)
            && _settingsApi.IsCurrentContext(context);

        try
        {
            var response = await _authApi.VerifyPinAsync(profile.Id, Pin);
            if (!IsCurrent()) return;
            if (response.Valid && !string.IsNullOrWhiteSpace(response.ProfileToken))
            {
                IsPinRequired = false;
                Pin = "";
                await ActivateProfileAsync(profile, response.ProfileToken);
            }
            else
            {
                PinErrorMessage = ShellText(nameof(PinErrorMessage), () => AppStrings.Get("web.profiles.pin.incorrect", fallback: "Incorrect PIN"));
                Pin = "";
            }
        }
        catch (ApiException ex)
        {
            if (!IsCurrent()) return;
            PinErrorMessage = ShellText(nameof(PinErrorMessage), () => PinLockout(ex) ?? ShellProblem(ex, AppStrings.Get("web.profiles.pin.verifyFailed", fallback: "Verification failed")));
            if (ex.StatusCode == 429) Pin = "";
        }
        catch (Exception)
        {
            if (!IsCurrent()) return;
            PinErrorMessage = ShellText(nameof(PinErrorMessage), () => AppStrings.Get("web.profiles.pin.verifyFailed", fallback: "Verification failed"));
        }
        finally
        {
            if (revision == _pinRevision) IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelPin()
    {
        CancelManagement();
        ++_pinRevision;
        IsLoading = false;
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
        var context = _settingsApi.CaptureContext(); var revision = ++_pinRevision;
        bool Current() => revision == _pinRevision && _settingsApi.IsCurrentContext(context);

        try
        {
            var wasEmpty = Profiles.Count == 0;
            var profile = await _authApi.CreateProfileAsync(request.Name, request.Pin, request.IsChild);
            if (!Current()) return;
            Profiles.Add(profile);
            if (wasEmpty)
            {
                if (string.IsNullOrWhiteSpace(request.Pin))
                {
                    await ActivateProfileAsync(profile, null);
                }
                else
                {
                    try
                    {
                        var verification = await _authApi.VerifyPinAsync(profile.Id, request.Pin);
                        if (!Current()) return;
                        if (verification.Valid && !string.IsNullOrWhiteSpace(verification.ProfileToken))
                            await ActivateProfileAsync(profile, verification.ProfileToken);
                        else
                            ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.profiles.picker.createdPinFailed"));
                    }
                    catch (Exception ex)
                    {
                        if (!Current()) return;
                        ErrorMessage = ShellText(nameof(ErrorMessage), () => PinLockout(ex) is { } lockout
                            ? AppStrings.Get("web.profiles.picker.createdLockout", new Dictionary<string, object?> { ["lockout"] = lockout }) : ShellProblem(ex, AppStrings.Get("web.profiles.picker.createdPinFailed")));
                    }
                }
            }
        }
        catch (ApiException ex)
        {
            if (Current()) ErrorMessage = ShellText(nameof(ErrorMessage), () => $"{AppStrings.Get("web.setup.account.createProfileFailed", fallback: "Failed to create profile")}: {ShellProblem(ex, ex.Message)}");
        }
        catch (Exception ex)
        {
            if (Current()) ErrorMessage = ShellText(nameof(ErrorMessage), () => $"{AppStrings.Get("web.setup.account.createProfileFailed", fallback: "Failed to create profile")}: {ShellProblem(ex, ex.Message)}");
        }
        finally
        {
            if (revision == _pinRevision) IsLoading = false;
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
            ErrorMessage = ShellText(nameof(ErrorMessage), () => $"Failed to update profile: {ShellProblem(ex, ex.Message)}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ShellText(nameof(ErrorMessage), () => $"Failed to update profile: {ShellProblem(ex, ex.Message)}");
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
            ErrorMessage = ShellText(nameof(ErrorMessage), () => $"Failed to delete profile: {ShellProblem(ex, ex.Message)}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ShellText(nameof(ErrorMessage), () => $"Failed to delete profile: {ShellProblem(ex, ex.Message)}");
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

        PersistProfile(profile);
        var settings = _settingsService.Load();

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
        CancelManagement();
        ++_pinRevision;
        var cts = Interlocked.Exchange(ref _profilesCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoading = false;
    }

    private void PersistProfile(Profile profile)
    {
        var settings = _settingsService.Load(); settings.LastProfileId = profile.Id;
        if (_authService.CurrentUser != null)
        { settings.LastUserRole = _authService.CurrentUser.Role; settings.LastUsername = _authService.CurrentUser.Username; }
        _settingsService.Save(settings);
    }

    private async Task VerifyManagementPinAsync()
    {
        if (string.IsNullOrWhiteSpace(Pin)) { PinErrorMessage = ShellText(nameof(PinErrorMessage), () => "PIN is required."); return; }
        var cts = _managementCts; var profile = _managementPrimary; var owner = _managementOwner;
        if (cts == null || profile == null) return;
        var revision = ++_pinRevision;
        bool Current() => ReferenceEquals(cts, _managementCts) && !cts.IsCancellationRequested
            && revision == _pinRevision && _authApi.IsCurrentContext(owner);
        PinErrorMessage = null;
        try
        {
            var result = await _authApi.VerifyPinAsync(owner, profile.Id, Pin, cts.Token);
            if (!Current()) return;
            Pin = "";
            if (!result.Valid || string.IsNullOrWhiteSpace(result.ProfileToken))
            { PinErrorMessage = ShellText(nameof(PinErrorMessage), () => AppStrings.Get("web.profiles.pin.incorrect", fallback: "Incorrect PIN")); return; }
            IsPinRequired = false;
            await ContinueManagementAsync(result.ProfileToken);
        }
        catch (OperationCanceledException) when (!Current()) { }
        catch (Exception ex)
        {
            if (!Current()) return;
            Pin = ""; PinErrorMessage = ShellText(nameof(PinErrorMessage), () => PinLockout(ex) ?? ShellProblem(ex, "Verification failed. Try again."));
        }
    }
}

public sealed class ProfilePickerCard(Profile? profile) : ObservableObject
{
    public Profile? Profile { get; } = profile;
    public string AccessibleName => Profile is { HasPin: true } profile
        ? AppStrings.Get("web.profiles.picker.pinProtected", new Dictionary<string, object?> { ["name"] = profile.Name }, profile.SelectProfileAccessibleName)
        : Profile?.Name ?? AppStrings.Get("web.profiles.picker.add");
    public void RefreshLocalizedText() => OnPropertyChanged(nameof(AccessibleName));
    public bool IsAdd => Profile == null;
    public bool IsProfile => Profile != null;
}

public partial class ProfileSelectViewModel
{
    private readonly Dictionary<string, (Func<string> Render, string Last)> _shellText = [];

    private string ShellText(string property, Func<string> render)
    {
        var text = render();
        _shellText[property] = (render, text);
        return text;
    }

    private static string ShellProblem(Exception exception, string english)
        => exception is ApiException problem ? ApiProblemText.Display(problem, english) : english;

    public bool IsPinLockedOut { get; private set; }

    private string? PinLockout(Exception exception)
    {
        if (exception is not ApiException { StatusCode: 429 } refusal) return null;
        IsPinLockedOut = true;
        if (refusal.RetryAfterSeconds is not { } seconds || !double.IsFinite(seconds)) return AppStrings.Get("web.profiles.pin.lockedLater");
        var minutes = Math.Max(1, Math.Ceiling(seconds / 60));
        return AppStrings.Get("web.profiles.pin.lockedMinutes", new Dictionary<string, object?>
        { ["count"] = minutes, ["count_text"] = minutes.ToString("0", AppStrings.FormatCulture) },
            SiloPlayer.Core.Services.ProfilePinFeedback.Lockout(exception));
    }

    public void RefreshLocalizedText()
    {
        foreach (var card in PickerCards) card.RefreshLocalizedText();
        foreach (var (property, text) in _shellText.ToArray())
        {
            switch (property)
            {
                case nameof(ErrorMessage) when ErrorMessage == text.Last: ErrorMessage = ShellText(property, text.Render); break;
                case nameof(PinErrorMessage) when PinErrorMessage == text.Last: PinErrorMessage = ShellText(property, text.Render); break;
                default: _shellText.Remove(property); break;
            }
        }
    }
}
