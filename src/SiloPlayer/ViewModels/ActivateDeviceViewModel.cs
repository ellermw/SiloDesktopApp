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
    private readonly SettingsApi _settingsApi;
    private readonly SiloApiClient _apiClient;
    private CancellationTokenSource? _loadCts;

    public ActivateDeviceViewModel(
        AuthApi authApi,
        AuthService authService,
        SettingsApi settingsApi,
        SiloApiClient apiClient)
    {
        _authApi = authApi;
        _authService = authService;
        _settingsApi = settingsApi;
        _apiClient = apiClient;
    }

    [ObservableProperty]
    private string _serverName = "Silo";

    [ObservableProperty]
    private string? _loginBackgroundUrl;

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
    public bool CanEnterAnotherCode => ShowDetails && string.IsNullOrEmpty(ActiveToken);
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
    public string DeviceDisplayName => string.IsNullOrWhiteSpace(Details?.DeviceName)
        ? "This device"
        : Details.DeviceName;

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
        OnPropertyChanged(nameof(CanEnterAnotherCode));
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
        OnPropertyChanged(nameof(DeviceDisplayName));
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
        CancelLoad();
        Details = null;
        ErrorMessage = null;
        StatusMessage = null;
        ActiveToken = token ?? "";
        ActiveCode = string.IsNullOrWhiteSpace(token) ? NormalizeCode(code ?? "") : "";
        if (!string.IsNullOrEmpty(ActiveCode))
        {
            CodeInput = ActiveCode;
        }
        var brandingTask = LoadBrandingAsync();
        if (HasActiveRequest)
        {
            await Task.WhenAll(LoadDetailsAsync(), brandingTask);
        }
        else
        {
            await brandingTask;
        }
    }

    private async Task LoadBrandingAsync()
    {
        try
        {
            var branding = await _settingsApi.GetServerBrandingAsync();
            ServerName = string.IsNullOrWhiteSpace(branding.ServerName) ? "Silo" : branding.ServerName;
            LoginBackgroundUrl = _apiClient.ResolveServerUrl(branding.LoginBackgroundUrl);
        }
        catch
        {
            ServerName = "Silo";
            LoginBackgroundUrl = null;
        }
    }

    public async Task LoadDetailsAsync()
    {
        if (!HasActiveRequest)
        {
            Details = null;
            return;
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var token = string.IsNullOrEmpty(ActiveToken) ? null : ActiveToken;
        var code = string.IsNullOrEmpty(ActiveCode) ? null : ActiveCode;
        IsLoadingDetails = true;
        ErrorMessage = null;
        try
        {
            var details = await _authApi.DeviceLookupAsync(token, code, cts.Token);
            if (ReferenceEquals(_loadCts, cts))
                Details = details;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A newer lookup owns the page state.
        }
        catch (ApiException ex)
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                Details = null;
                ErrorMessage = ex.ErrorCode switch
                {
                    "not_found" => "That sign-in request could not be found.",
                    _ => ex.Message,
                };
            }
        }
        catch (HttpRequestException)
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                Details = null;
                ErrorMessage = "Unable to reach the server. Check your connection.";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                Details = null;
                ErrorMessage = ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, cts), cts))
                IsLoadingDetails = false;
            cts.Dispose();
        }
    }

    public void CancelLoad()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoadingDetails = false;
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
