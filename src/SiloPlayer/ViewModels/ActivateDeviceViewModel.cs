using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

/// <summary>
/// The Activate device page: the signed-in person enters (or arrives with) the code a device shows,
/// checks it and approves or declines that device's sign-in. Mirrors the official WebUI page
/// (web/src/pages/ActivateDevice.tsx and QuickConnectApproval.tsx, silo-server 3b2b32168, #4652);
/// the state and message rules live in <see cref="DeviceActivation"/>.
/// </summary>
/// <remarks>
/// Every request is bound to the identity that opened the page and is cancelable; a reply that arrives
/// after an account or server switch, or for a code the person has since replaced, is dropped. An
/// approve or decline is sent once and never repeated automatically. Codes and tokens are never logged.
/// </remarks>
public partial class ActivateDeviceViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly AuthService _authService;
    private readonly SettingsApi _settingsApi;
    private readonly SiloApiClient _apiClient;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _watchCts;
    private CancellationTokenSource? _decisionCts;
    private CancellationTokenSource? _profilesCts;
    private DateTime _watchStartedUtc;
    // Bumped when the code in play changes: a decision or lookup that finishes for an earlier code is ignored.
    private int _requestVersion;
    private string? _profilesLoadedFor;

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

    /// <summary>The time "requested 5m ago" counts from; tests set it.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    [ObservableProperty]
    private string _serverName = "Silo";

    [ObservableProperty]
    private string? _loginBackgroundUrl;

    // ---- Code entry ----

    /// <summary>The code as typed, grouped 4+4 ("4821 7730").</summary>
    [ObservableProperty]
    private string _codeInput = "";

    /// <summary>The compact code in play. Empty until the person submits one, or arrives with one.</summary>
    [ObservableProperty]
    private string _activeCode = "";

    /// <summary>A verification-link token in play. Exclusive with <see cref="ActiveCode"/>; only one is sent.</summary>
    [ObservableProperty]
    private string _activeToken = "";

    // ---- State ----

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private bool _isActing;

    [ObservableProperty]
    private DeviceLoginLookupResponse? _details;

    [ObservableProperty]
    private ActivationLoadError _loadError;

    [ObservableProperty]
    private double? _retryAfterSeconds;

    [ObservableProperty]
    private bool _approvedHere;

    [ObservableProperty]
    private string? _approvedProfile;

    [ObservableProperty]
    private ActivationText? _actionError;

    // ---- Quick Connect approval ----

    public ObservableCollection<QuickConnectProfileChoice> QuickConnectProfiles { get; } = [];

    [ObservableProperty]
    private bool _quickConnectLoading;

    [ObservableProperty]
    private bool _quickConnectLoadFailed;

    [ObservableProperty]
    private bool _quickConnectActing;

    [ObservableProperty]
    private string? _quickConnectChosenId;

    [ObservableProperty]
    private string _quickConnectPin = "";

    [ObservableProperty]
    private ActivationText? _quickConnectError;

    // ---- Derived state ----

    public bool HasActiveRequest => !string.IsNullOrEmpty(ActiveCode) || !string.IsNullOrEmpty(ActiveToken);
    public bool IsSignedIn => _authService.IsLoggedIn;
    public bool IsImpersonating => _authService.CurrentUser?.Impersonation?.Active == true;
    public string? SignedInUsername => _authService.CurrentUser?.Username;
    public bool NeedsAccount => DeviceActivation.NeedsAccount(ActiveToken, ActiveCode);
    public bool IsQuickConnect => DeviceActivation.IsQuickConnect(Details);
    public bool IsRemotePlayback => DeviceActivation.IsRemotePlayback(Details);
    public bool IsPending => Details?.Status == "pending";

    public bool ShowCodeForm => !HasActiveRequest;
    public bool ShowLoading => HasActiveRequest && IsLoadingDetails;
    public bool ShowQuickConnectSignIn => HasActiveRequest && !IsLoadingDetails && NeedsAccount && !IsSignedIn;
    public bool ShowLoadError => HasActiveRequest && !IsLoadingDetails && !ShowQuickConnectSignIn && LoadError != ActivationLoadError.None;
    public bool ShowTryAgain => ShowLoadError && LoadError == ActivationLoadError.Failed;
    public bool ShowDetails => HasActiveRequest && !IsLoadingDetails && !ShowQuickConnectSignIn && LoadError == ActivationLoadError.None && Details != null;

    /// <summary>Whether the code box can be submitted: a whole device or Quick Connect code.</summary>
    public bool CanSubmitCode => DeviceActivation.IsCompleteCode(DeviceActivation.CompactCode(CodeInput));

    public bool ShowHeading => ShowDetails && Details!.Status != "expired";
    public bool ShowDeviceLine => ShowDetails && DeviceLineText.Length > 0;
    public bool ShowMatchCode => ShowDetails && IsPending && DisplayCode.Length > 0;
    public bool ShowRemotePlayback => ShowDetails && IsPending && IsRemotePlayback;
    public bool ShowRemoteDecline => ShowRemotePlayback && IsSignedIn && !IsImpersonating;
    public bool ShowSignedOut => ShowDetails && IsPending && !IsRemotePlayback && !IsSignedIn;
    public bool ShowImpersonation => ShowDetails && IsPending && !IsRemotePlayback && IsSignedIn && IsImpersonating;
    public bool ShowApproval => ShowDetails && IsPending && !IsRemotePlayback && IsSignedIn && !IsImpersonating;
    public bool ShowProfilesShared => ShowApproval && !IsQuickConnect;
    public bool ShowRequestedFrom => ShowApproval && !string.IsNullOrEmpty(Details?.IpAddressHint);
    public bool ShowActionError => (ShowApproval || ShowRemoteDecline) && ActionError != null;
    public bool ShowQuickConnectApproval => ShowApproval && IsQuickConnect;
    public bool ShowPlainApproval => ShowApproval && !IsQuickConnect;
    public bool ShowResult => ShowDetails && ResultText.Length > 0;

    /// <summary>A link carries its request in the token, so it offers manual entry only once that request expired.</summary>
    public bool CanEnterAnotherCode => ShowDetails && (string.IsNullOrEmpty(ActiveToken) || Details!.Status == "expired");
    public bool ShowEnterAnotherGhost => CanEnterAnotherCode && IsPending;
    public bool ShowEnterAnotherOutline => CanEnterAnotherCode && !IsPending;

    public bool ShowQuickConnectLoading => ShowQuickConnectApproval && QuickConnectLoading;
    public bool ShowQuickConnectLoadFailed => ShowQuickConnectApproval && !QuickConnectLoading && (QuickConnectLoadFailed || QuickConnectProfiles.Count == 0);
    public bool ShowQuickConnectChoices => ShowQuickConnectApproval && !QuickConnectLoading && !ShowQuickConnectLoadFailed;
    public bool ShowQuickConnectPin => ShowQuickConnectChoices && QuickConnectNeedsPin;
    public bool ShowQuickConnectError => ShowQuickConnectChoices && QuickConnectError != null;

    public string DisplayServerName => string.IsNullOrWhiteSpace(Details?.ServerName) ? ServerName : Details!.ServerName!;
    public string ServerHost => ServerOriginDisplay.Format(_authService.ConfiguredServerUrl);
    public string DisplayCode => Details == null ? "" : DeviceActivation.DisplayCode(string.IsNullOrEmpty(Details.UserCode) ? ActiveCode : Details.UserCode);
    public string SpokenCode => DeviceCodeText.Spoken(DisplayCode);
    public string DeviceDisplayName => Details?.DeviceName?.Trim() ?? "";
    public string HeadingText => Details == null ? "" : DeviceActivation.Heading(Details)?.Render() ?? "";
    public string DeviceLineText => Details == null ? "" : DeviceActivation.DeviceLine(Details, Clock());
    public string ResultText => Details == null ? "" : DeviceActivation.Result(Details, ApprovedHere, ApprovedProfile)?.Render() ?? "";
    public string ActionErrorText => ActionError?.Render() ?? "";
    public string QuickConnectErrorText => QuickConnectError?.Render() ?? "";
    public string ImpersonationText => DeviceActivation.Impersonation(SignedInUsername).Render();
    public string RequestedFromText => ActivationText.Of("web.auth.activate.requestedFrom", "Requested from {ip}", ("ip", Details?.IpAddressHint ?? "")).Render();

    public string LoadErrorText => LoadError switch
    {
        ActivationLoadError.NotFound => DeviceActivation.NotFound.Render(),
        ActivationLoadError.RateLimited => DeviceActivation.TooManyCodes(RetryAfterSeconds).Render(),
        ActivationLoadError.Impersonating => ImpersonationText,
        ActivationLoadError.Failed => DeviceActivation.LookupFailed.Render(),
        _ => "",
    };

    public string SignedOutText => ActivationText.Of("web.auth.activate.signInToApproveTv", "Sign in to {server} to approve this sign-in.", ("server", DisplayServerName)).Render();
    public string QuickConnectSignInText => ActivationText.Of("web.auth.activate.quickConnectSignIn",
        "Sign in to {server} to approve code {code} from your Jellyfin app.", ("server", DisplayServerName), ("code", ActiveCode)).Render();

    public string QuickConnectProfileLabel => DeviceDisplayName.Length > 0
        ? ActivationText.Of("web.auth.quickConnect.profileLabel", "Sign {device} in as", ("device", DeviceDisplayName)).Render()
        : ActivationText.Of("web.auth.quickConnect.profileLabelUnknown", "Sign this device in as").Render();

    public QuickConnectProfileChoice? QuickConnectSelected
    {
        get
        {
            var id = DeviceActivation.SelectedProfileId(QuickConnectChosenId, QuickConnectProfiles.Select(choice => choice.Profile).ToList(), _authService.SelectedProfileId);
            return QuickConnectProfiles.FirstOrDefault(choice => choice.Profile.Id == id);
        }
    }

    public bool QuickConnectNeedsPin => DeviceActivation.NeedsPin(QuickConnectSelected?.Profile, _authService.SelectedProfileId);
    public string QuickConnectPinLabel => ActivationText.Of("web.auth.quickConnect.pinLabel", "{profile} PIN", ("profile", QuickConnectSelected?.Profile.Name ?? "")).Render();
    public bool CanApproveQuickConnect => DeviceActivation.CanApproveQuickConnect(IsActing || QuickConnectActing, QuickConnectSelected?.Profile, QuickConnectNeedsPin, QuickConnectPin);
    public bool CanDecide => !IsActing && !QuickConnectActing;
    public string ApproveButtonText => IsActing || QuickConnectActing
        ? AppStrings.Get("web.auth.login.signingIn", fallback: "Signing in...")
        : AppStrings.Get("web.auth.activate.signInTv", fallback: "Approve sign-in");

    partial void OnCodeInputChanged(string value) { OnPropertyChanged(nameof(CanSubmitCode)); SubmitCodeCommand.NotifyCanExecuteChanged(); }
    partial void OnActiveCodeChanged(string value) => RaiseState();
    partial void OnActiveTokenChanged(string value) => RaiseState();
    partial void OnIsLoadingDetailsChanged(bool value) { RaiseState(); _ = EnsureQuickConnectProfilesAsync(); }
    partial void OnIsActingChanged(bool value) => RaiseState();
    partial void OnLoadErrorChanged(ActivationLoadError value) => RaiseState();
    partial void OnRetryAfterSecondsChanged(double? value) => RaiseState();
    partial void OnApprovedHereChanged(bool value) { RaiseState(); UpdateWatch(); }
    partial void OnApprovedProfileChanged(string? value) => RaiseState();
    partial void OnActionErrorChanged(ActivationText? value) => RaiseState();
    partial void OnQuickConnectLoadingChanged(bool value) => RaiseState();
    partial void OnQuickConnectLoadFailedChanged(bool value) => RaiseState();
    partial void OnQuickConnectActingChanged(bool value) => RaiseState();
    partial void OnQuickConnectChosenIdChanged(string? value) => RaiseState();
    partial void OnQuickConnectPinChanged(string value) => RaiseState();
    partial void OnQuickConnectErrorChanged(ActivationText? value) => RaiseState();
    partial void OnServerNameChanged(string value) => RaiseState();

    partial void OnDetailsChanged(DeviceLoginLookupResponse? value)
    {
        RaiseState();
        UpdateWatch();
        _ = EnsureQuickConnectProfilesAsync();
    }

    /// <summary>Every bound line is derived from the state, so one notification refreshes the page (also after a language change).</summary>
    private void RaiseState() => OnPropertyChanged(string.Empty);

    /// <summary>Re-reads every localized line after the interface language changes.</summary>
    public void RefreshLocalizedText() => RaiseState();

    /// <summary>Groups the typed code 4+4 the way the device shows it; Quick Connect codes stay six digits.</summary>
    public static string NormalizeCode(string value) => DeviceActivation.FormatCode(value);

    // ---- Starting a request ----

    /// <summary>Called by the page when it opens, with the verification-link token or code when it came from one.</summary>
    public async Task InitializeAsync(string? token, string? code)
    {
        BeginRequest();
        ActiveToken = token ?? "";
        ActiveCode = string.IsNullOrWhiteSpace(token) ? DeviceActivation.CompactCode(code) : "";
        CodeInput = DeviceActivation.FormatCode(ActiveCode);
        var branding = LoadBrandingAsync();
        if (HasActiveRequest)
            await Task.WhenAll(LoadDetailsAsync(), branding);
        else
            await branding;
    }

    /// <summary>Leaves the current request: stops its lookups, watch and decisions and clears everything it showed.</summary>
    private void BeginRequest()
    {
        _requestVersion++;
        CancelLoad();
        CancelOutstanding(ref _decisionCts);
        CancelOutstanding(ref _profilesCts);
        _profilesLoadedFor = null;
        Details = null;
        LoadError = ActivationLoadError.None;
        RetryAfterSeconds = null;
        ApprovedHere = false;
        ApprovedProfile = null;
        ActionError = null;
        IsActing = false;
        ResetQuickConnect();
    }

    private void ResetQuickConnect()
    {
        QuickConnectProfiles.Clear();
        QuickConnectChosenId = null;
        QuickConnectPin = "";
        QuickConnectError = null;
        QuickConnectLoading = false;
        QuickConnectLoadFailed = false;
        QuickConnectActing = false;
    }

    private static void CancelOutstanding(ref CancellationTokenSource? source)
    {
        var cts = Interlocked.Exchange(ref source, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanSubmitCode))]
    private async Task SubmitCodeAsync()
    {
        var compact = DeviceActivation.CompactCode(CodeInput);
        if (!DeviceActivation.IsCompleteCode(compact)) return;
        BeginRequest();
        ActiveToken = "";
        ActiveCode = compact;
        await LoadDetailsAsync();
    }

    [RelayCommand]
    private void ClearCode()
    {
        BeginRequest();
        ActiveCode = "";
        ActiveToken = "";
        CodeInput = "";
    }

    [RelayCommand]
    private Task RetryLookupAsync() => LoadDetailsAsync();

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

    // ---- Lookup ----

    /// <summary>
    /// Looks the request up. A signed-in person's lookup carries the session (a Quick Connect code is found
    /// only that way). <paramref name="keepOnError"/> leaves the last card on screen when a refresh fails,
    /// for the reloads after a decision and while watching. Returns the details it loaded, or null.
    /// </summary>
    public async Task<DeviceLoginLookupResponse?> LoadDetailsAsync(bool keepOnError = false)
    {
        // A six-digit Quick Connect code is looked up only once someone is signed in.
        if (!HasActiveRequest || (NeedsAccount && !IsSignedIn))
        {
            Details = null;
            LoadError = ActivationLoadError.None;
            return null;
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var token = string.IsNullOrEmpty(ActiveToken) ? null : ActiveToken;
        var code = string.IsNullOrEmpty(ActiveCode) ? null : ActiveCode;
        var owner = _apiClient.CaptureContext();
        var signedIn = IsSignedIn;
        if (!keepOnError) IsLoadingDetails = true;
        try
        {
            var details = signedIn
                ? await _authApi.DeviceLookupAsync(owner, token, code, cts.Token)
                : await _authApi.DeviceLookupAsync(token, code, cts.Token);
            // Only the newest lookup, for the identity that asked, updates the card.
            if (!ReferenceEquals(_loadCts, cts) || !_apiClient.IsCurrentContext(owner)) return null;
            Details = details;
            LoadError = ActivationLoadError.None;
            return details;
        }
        catch (OperationCanceledException)
        {
            // A newer lookup, a new code, or leaving the page owns the state.
            return null;
        }
        catch (Exception ex)
        {
            if (keepOnError || !ReferenceEquals(_loadCts, cts) || !_apiClient.IsCurrentContext(owner)) return null;
            Details = null;
            // A Quick Connect code spends the account's code budget.
            RetryAfterSeconds = (ex as ApiException)?.RetryAfterSeconds;
            LoadError = DeviceActivation.LoadErrorFor((ex as ApiException)?.StatusCode ?? 0);
            return null;
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
        CancelOutstanding(ref _loadCts);
        StopWatch();
        IsLoadingDetails = false;
    }

    /// <summary>Called when the page is left: nothing keeps running for it.</summary>
    public void Leave()
    {
        CancelLoad();
        CancelOutstanding(ref _decisionCts);
        CancelOutstanding(ref _profilesCts);
    }

    // ---- Watching ----

    // After approving here (or while a remote-playback request waits for its app) the page follows the
    // request until it settles. The server answers expired once the request's time passes, so this ends.
    private void UpdateWatch()
    {
        if (!DeviceActivation.ShouldWatch(Details, ApprovedHere))
        {
            StopWatch();
            return;
        }
        if (_watchCts != null) return;
        var cts = new CancellationTokenSource();
        _watchCts = cts;
        _watchStartedUtc = DateTime.UtcNow;
        _ = WatchAsync(cts);
    }

    private void StopWatch() => CancelOutstanding(ref _watchCts);

    private async Task WatchAsync(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested && ReferenceEquals(_watchCts, cts))
            {
                await Task.Delay(DeviceActivation.WatchDelay(DateTime.UtcNow - _watchStartedUtc), cts.Token);
                if (!ReferenceEquals(_watchCts, cts) || !DeviceActivation.ShouldWatch(Details, ApprovedHere)) return;
                // A failed lookup leaves the card as it is and the next one is scheduled.
                await LoadDetailsAsync(keepOnError: true);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped: the request settled, changed or the page was left.
        }
    }

    // ---- Decisions ----

    [RelayCommand]
    private Task ApproveAsync() => DecideAsync(approve: true);

    [RelayCommand]
    private Task DenyAsync() => DecideAsync(approve: false);

    private async Task DecideAsync(bool approve)
    {
        if (!HasActiveRequest || IsActing || QuickConnectActing || Details == null) return;

        var version = _requestVersion;
        var cts = new CancellationTokenSource();
        CancelOutstanding(ref _decisionCts);
        _decisionCts = cts;
        var owner = _apiClient.CaptureContext();
        var token = string.IsNullOrEmpty(ActiveToken) ? null : ActiveToken;
        var code = string.IsNullOrEmpty(ActiveCode) ? null : ActiveCode;
        var quickConnect = IsQuickConnect;
        bool Stale() => version != _requestVersion || cts.IsCancellationRequested || !_apiClient.IsCurrentContext(owner);

        IsActing = true;
        ActionError = null;
        // Every Quick Connect lookup spends the account's code budget, so a decision on one is followed by a
        // reload only when the request moved on.
        var reload = !quickConnect;
        ActivationText? unchangedError = null;
        try
        {
            // Sent once. A failure is shown for the person to retry; nothing resends it.
            await _authApi.DeviceDecideAsync(owner, approve, token, code, cts.Token);
            if (Stale()) return;
            if (approve)
            {
                // The reload below may fail; the watch still has to start.
                Details = WithStatus(Details, "approved");
                ApprovedHere = true;
            }
            else if (quickConnect)
            {
                Details = WithStatus(Details, "denied");
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (Stale()) return;
            var api = ex as ApiException;
            var plan = api != null
                ? DeviceActivation.PlanDecisionFailure(quickConnect, approve, api.StatusCode, api.ProblemType ?? api.ErrorCode, api.RetryAfterSeconds, SignedInUsername)
                : new DecisionFailurePlan(!quickConnect, DeviceActivation.DecisionFailed(approve), null);
            reload = plan.Reload;
            ActionError = plan.Error;
            unchangedError = plan.ErrorIfUnchanged;
        }
        finally
        {
            if (!Stale() && reload)
            {
                var current = await LoadDetailsAsync(keepOnError: true);
                if (!Stale() && unchangedError != null && (current == null || current.Status == "pending"))
                    ActionError = unchangedError;
            }
            if (version == _requestVersion)
            {
                if (ReferenceEquals(_decisionCts, cts)) _decisionCts = null;
                IsActing = false;
            }
            cts.Dispose();
        }
    }

    private static DeviceLoginLookupResponse WithStatus(DeviceLoginLookupResponse source, string status) => new()
    {
        Binding = source.Binding,
        Status = status,
        UserCode = source.UserCode,
        MatchCode = source.MatchCode,
        DeviceName = source.DeviceName,
        DevicePlatform = source.DevicePlatform,
        IpAddressHint = source.IpAddressHint,
        ExpiresAt = source.ExpiresAt,
        ClientPurpose = source.ClientPurpose,
        Temporary = source.Temporary,
        RequestedAt = source.RequestedAt,
        ServerName = source.ServerName,
    };

    // ---- Quick Connect: choose the profile and approve ----

    private async Task EnsureQuickConnectProfilesAsync()
    {
        if (!ShowQuickConnectApproval) return;
        var key = _requestVersion + ":" + (_authService.CurrentUser?.Id ?? "");
        if (_profilesLoadedFor == key) return;
        _profilesLoadedFor = key;
        CancelOutstanding(ref _profilesCts);
        var cts = _profilesCts = new CancellationTokenSource();
        var owner = _apiClient.CaptureContext();
        var version = _requestVersion;
        QuickConnectLoading = true;
        QuickConnectLoadFailed = false;
        try
        {
            var response = await _authApi.GetProfilesAsync(cts.Token);
            if (version != _requestVersion || cts.IsCancellationRequested || !_apiClient.IsCurrentContext(owner)) return;
            QuickConnectProfiles.Clear();
            foreach (var profile in response.Profiles) QuickConnectProfiles.Add(new QuickConnectProfileChoice(profile));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            if (version != _requestVersion || cts.IsCancellationRequested) return;
            QuickConnectLoadFailed = true;
            // A failed load can be tried again.
            _profilesLoadedFor = null;
        }
        finally
        {
            if (version == _requestVersion && ReferenceEquals(_profilesCts, cts))
            {
                QuickConnectLoading = false;
                MarkQuickConnectSelection();
            }
        }
    }

    [RelayCommand]
    private Task RetryProfilesAsync()
    {
        _profilesLoadedFor = null;
        return EnsureQuickConnectProfilesAsync();
    }

    /// <summary>Picks a profile; any PIN typed for the previous one is dropped.</summary>
    public void ChooseQuickConnectProfile(string profileId)
    {
        QuickConnectChosenId = profileId;
        QuickConnectPin = "";
        QuickConnectError = null;
        MarkQuickConnectSelection();
    }

    private void MarkQuickConnectSelection()
    {
        var selected = QuickConnectSelected?.Profile.Id;
        foreach (var choice in QuickConnectProfiles) choice.IsSelected = choice.Profile.Id == selected;
        RaiseState();
    }

    [RelayCommand]
    private async Task ApproveQuickConnectAsync()
    {
        var selected = QuickConnectSelected?.Profile;
        if (selected == null || !CanApproveQuickConnect || string.IsNullOrEmpty(ActiveCode)) return;

        var version = _requestVersion;
        var cts = new CancellationTokenSource();
        CancelOutstanding(ref _decisionCts);
        _decisionCts = cts;
        var owner = _apiClient.CaptureContext();
        var code = ActiveCode;
        var needsPin = QuickConnectNeedsPin;
        var pin = QuickConnectPin;
        bool Stale() => version != _requestVersion || cts.IsCancellationRequested || !_apiClient.IsCurrentContext(owner);

        QuickConnectActing = true;
        QuickConnectError = null;
        var moved = false;
        try
        {
            string? proof = null;
            if (needsPin)
            {
                VerifyPinResponse verified;
                try
                {
                    verified = await _authApi.VerifyPinAsync(owner, selected.Id, pin, cts.Token);
                }
                catch (ApiException ex) when (ex.StatusCode == 429)
                {
                    // A PIN lockout is the profile's own limit, not the code budget.
                    if (!Stale()) QuickConnectError = DeviceActivation.PinLockout(ex.RetryAfterSeconds);
                    return;
                }
                if (Stale()) return;
                if (!verified.Valid || string.IsNullOrWhiteSpace(verified.ProfileToken))
                {
                    QuickConnectError = DeviceActivation.WrongPin;
                    return;
                }
                proof = verified.ProfileToken;
            }

            // The profile this session already uses signs the request as itself; another one is captured for this request only.
            if (selected.Id == _authService.SelectedProfileId)
                await _authApi.ApproveQuickConnectAsync(owner, code, cts.Token);
            else
                await _authApi.ApproveQuickConnectAsync(_authApi.CaptureProfileAuthority(owner, selected.Id, proof), code, cts.Token);
            if (Stale()) return;

            Details = Details == null ? null : WithStatus(Details, "approved");
            ApprovedHere = true;
            ApprovedProfile = selected.Name;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException) when (!Stale())
        {
            QuickConnectError = DeviceActivation.SessionEnded;
        }
        catch (Exception ex)
        {
            if (Stale()) return;
            if (ex is not ApiException api)
            {
                QuickConnectError = DeviceActivation.ClassifyQuickConnectFailure(0, null, null).Error;
                return;
            }
            var (kind, error) = DeviceActivation.ClassifyQuickConnectFailure(api.StatusCode, api.ProblemType ?? api.ErrorCode, api.RetryAfterSeconds);
            if (kind == DecisionFailureKind.Moved) moved = true;
            else QuickConnectError = error;
        }
        finally
        {
            QuickConnectPin = "";
            if (version == _requestVersion)
            {
                if (ReferenceEquals(_decisionCts, cts)) _decisionCts = null;
                QuickConnectActing = false;
                // The request moved on (decided, withdrawn or expired); reload it.
                if (moved) _ = LoadDetailsAsync(keepOnError: true);
            }
            cts.Dispose();
        }
    }

    /// <summary>Signs out of this session so another account can approve; the page's code is not carried over.</summary>
    public Task SwitchAccountAsync() => _authService.LogoutAsync();
}

/// <summary>One profile in the Quick Connect profile list.</summary>
public sealed partial class QuickConnectProfileChoice(Profile profile) : ObservableObject
{
    public Profile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Hint => Profile.HasPin
        ? AppStrings.Get("web.auth.quickConnect.needsPin", fallback: "Needs the profile's PIN")
        : "";

    [ObservableProperty]
    private bool _isSelected;
}
