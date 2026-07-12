using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

/// <summary>
/// ViewModel for the Activate Device page — mirrors the web ActivateDevice flow.
/// The approver (an already-signed-in user on this device) enters a short code
/// displayed on another device, reviews the pending request, and either approves
/// or denies the sign-in.
/// </summary>
public partial class ActivateDeviceViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly AuthService _authService;

    public ActivateDeviceViewModel(AuthApi authApi, AuthService authService)
    {
        _authApi = authApi;
        _authService = authService;
    }

    // ---- Code entry ----

    /// <summary>
    /// Raw code entered by the user. Updated via CodeInputChanged() to normalize
    /// into the "ABCD-EFGH" format as the user types.
    /// </summary>
    [ObservableProperty]
    private string _codeInput = "";

    /// <summary>
    /// The confirmed code used for lookup/approve/deny. Empty until the user
    /// submits the code form.
    /// </summary>
    [ObservableProperty]
    private string _activeCode = "";

    /// <summary>
    /// Deep-link token (browser_code), if the page was launched from a verification
    /// URI. Exclusive with <see cref="ActiveCode"/> — only one is sent to the server.
    /// </summary>
    [ObservableProperty]
    private string _activeToken = "";

    // ---- State ----

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private bool _isActing;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private DeviceLoginLookupResponse? _details;

    // ---- Derived visibility flags (computed from Details + state) ----

    public bool HasActiveRequest => !string.IsNullOrEmpty(ActiveCode) || !string.IsNullOrEmpty(ActiveToken);
    public bool ShowCodeForm => !HasActiveRequest;
    public bool ShowLoading => HasActiveRequest && IsLoadingDetails;
    public bool ShowNotFound => HasActiveRequest && !IsLoadingDetails && Details == null;
    public bool ShowDetails => HasActiveRequest && !IsLoadingDetails && Details != null;
    public bool IsSignedIn => _authService.IsLoggedIn;
    public bool IsPending => Details?.Status == "pending";
    public bool IsApproved => Details?.Status == "approved";
    public bool IsConsumed => Details?.Status == "consumed";
    public bool IsDenied => Details?.Status == "denied";
    public bool IsExpired => Details?.Status == "expired";
    public bool CanShowApproveButtons => ShowDetails && IsSignedIn && IsPending;
    public bool ShowSignInPrompt => ShowDetails && !IsSignedIn;
    public bool IsRemotePlaybackHandoff => string.Equals(Details?.ClientPurpose, "remote_playback", StringComparison.OrdinalIgnoreCase);
    public string ApprovalButtonText => IsRemotePlaybackHandoff ? "Approve playback handoff" : "Approve sign-in";
    public string RequestDescription => IsRemotePlaybackHandoff
        ? "This device wants to use your active profile for remote playback."
        : "Approve sign-in for this device.";

    public string? SignedInUsername => _authService.CurrentUser?.Username;

    partial void OnActiveCodeChanged(string value) => RaiseDerivedFlags();
    partial void OnActiveTokenChanged(string value) => RaiseDerivedFlags();
    partial void OnIsLoadingDetailsChanged(bool value) => RaiseDerivedFlags();
    partial void OnDetailsChanged(DeviceLoginLookupResponse? value) => RaiseDerivedFlags();

    private void RaiseDerivedFlags()
    {
        OnPropertyChanged(nameof(HasActiveRequest));
        OnPropertyChanged(nameof(ShowCodeForm));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowNotFound));
        OnPropertyChanged(nameof(ShowDetails));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsApproved));
        OnPropertyChanged(nameof(IsConsumed));
        OnPropertyChanged(nameof(IsDenied));
        OnPropertyChanged(nameof(IsExpired));
        OnPropertyChanged(nameof(CanShowApproveButtons));
        OnPropertyChanged(nameof(ShowSignInPrompt));
        OnPropertyChanged(nameof(IsRemotePlaybackHandoff));
        OnPropertyChanged(nameof(ApprovalButtonText));
        OnPropertyChanged(nameof(RequestDescription));
        OnPropertyChanged(nameof(SignedInUsername));
    }

    /// <summary>
    /// Normalizes the raw code to uppercase alphanumeric, max 8 chars, with a dash
    /// after the 4th character. Matches the web implementation in ActivateDevice.tsx.
    /// </summary>
    public static string NormalizeCode(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var clean = new System.Text.StringBuilder();
        foreach (var ch in value.ToUpperInvariant())
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
            {
                clean.Append(ch);
                if (clean.Length >= 8) break;
            }
        }
        var s = clean.ToString();
        return s.Length <= 4 ? s : $"{s.Substring(0, 4)}-{s.Substring(4)}";
    }

    [RelayCommand]
    private async Task SubmitCodeAsync()
    {
        var normalized = NormalizeCode(CodeInput);
        if (string.IsNullOrEmpty(normalized))
        {
            ErrorMessage = "Enter the code shown on your other device.";
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        ActiveToken = "";
        ActiveCode = normalized;
        await LoadDetailsAsync();
    }

    [RelayCommand]
    private void ClearCode()
    {
        ActiveCode = "";
        ActiveToken = "";
        Details = null;
        ErrorMessage = null;
        StatusMessage = null;
        CodeInput = "";
    }

    /// <summary>
    /// Called by the page when activated with token/code URI params.
    /// </summary>
    public async Task InitializeAsync(string? token, string? code)
    {
        ActiveToken = token ?? "";
        ActiveCode = code ?? "";
        if (!string.IsNullOrEmpty(ActiveCode))
        {
            CodeInput = ActiveCode;
        }
        if (HasActiveRequest)
        {
            await LoadDetailsAsync();
        }
    }

    public async Task LoadDetailsAsync()
    {
        if (!HasActiveRequest)
        {
            Details = null;
            return;
        }

        IsLoadingDetails = true;
        ErrorMessage = null;
        try
        {
            Details = await _authApi.DeviceLookupAsync(
                string.IsNullOrEmpty(ActiveToken) ? null : ActiveToken,
                string.IsNullOrEmpty(ActiveCode) ? null : ActiveCode);
        }
        catch (ApiException ex)
        {
            Details = null;
            ErrorMessage = ex.ErrorCode switch
            {
                "not_found" => "That sign-in request could not be found.",
                _ => ex.Message,
            };
        }
        catch (HttpRequestException)
        {
            Details = null;
            ErrorMessage = "Unable to reach the server. Check your connection.";
        }
        catch (Exception ex)
        {
            Details = null;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    [RelayCommand]
    private Task ApproveAsync() => DecideAsync(approve: true);

    [RelayCommand]
    private Task DenyAsync() => DecideAsync(approve: false);

    private async Task DecideAsync(bool approve)
    {
        if (!HasActiveRequest) return;

        IsActing = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var tokenArg = string.IsNullOrEmpty(ActiveToken) ? null : ActiveToken;
            var codeArg = string.IsNullOrEmpty(ActiveCode) ? null : ActiveCode;
            if (approve)
            {
                if (IsRemotePlaybackHandoff)
                    await _authApi.DeviceApproveHandoffAsync(tokenArg, codeArg);
                else
                    await _authApi.DeviceApproveAsync(tokenArg, codeArg);
                StatusMessage = IsRemotePlaybackHandoff
                    ? "Playback handoff approved. Continue on the other device."
                    : "Approved. Finish sign-in on the other device.";
            }
            else
            {
                await _authApi.DeviceDenyAsync(tokenArg, codeArg);
                StatusMessage = "Request denied.";
            }
            // Refresh details so the UI reflects the new status.
            await LoadDetailsAsync();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "unauthorized" => "You must be signed in to approve a device.",
                "not_found" => "That sign-in request could not be found.",
                "expired" => "This sign-in request has expired.",
                "consumed" => "This sign-in request has already been used.",
                "denied" => "This sign-in request has already been denied.",
                _ => ex.Message,
            };
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Unable to reach the server. Check your connection.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsActing = false;
        }
    }
}
