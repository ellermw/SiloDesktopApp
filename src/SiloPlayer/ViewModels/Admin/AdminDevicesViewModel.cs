using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminDevicesViewModel(AdminApi adminApi) : ObservableObject
{
    private CancellationTokenSource? _loadCts;
    private readonly List<AdminDeviceSummary> _allDevices = [];

    public ObservableCollection<AdminDeviceCardViewModel> Devices { get; } = [];
    public ObservableCollection<AdminDeviceGroupViewModel> DeviceGroups { get; } = [];
    public ObservableCollection<AdminDeviceProfileOption> Profiles { get; } = [];
    public ObservableCollection<AdminDeviceSettingRow> Settings { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDetailLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _overridesOnly;
    [ObservableProperty] private bool _showAllSettings = true;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _platformFilter = "All platforms";
    [ObservableProperty] private string _recencyFilter = "Any activity";
    [ObservableProperty] private string _groupBy = "user";
    [ObservableProperty] private string? _activeSavedView;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private AdminDeviceCardViewModel? _selectedDevice;
    [ObservableProperty] private AdminDeviceDetail? _detail;
    [ObservableProperty] private AdminDeviceProfileOption? _selectedProfile;
    public bool HasLoaded { get; private set; }

    public int TotalDevices => _allDevices.Count;
    public int TotalUsers => _allDevices.Select(d => d.UserId).Distinct().Count();
    public int TotalProfiles => _allDevices.Sum(d => d.ProfileCount);
    public int TotalOverrides => _allDevices.Sum(d => d.OverrideCount);
    public bool HasDevices => Devices.Count > 0;
    public bool HasAnyDevices => _allDevices.Count > 0;
    public bool ShowDeviceListEmpty => !IsLoading && !HasDevices;
    public string DeviceListEmptyTitle => HasAnyDevices ? "No devices match your filters" : "No devices seen yet";
    public string DeviceListEmptyDescription => HasAnyDevices ? "Try adjusting the search, scope, or filter facets." : "Devices appear after a client has reported profile-specific settings.";
    public bool IsDetailVisible => SelectedDevice is not null;
    public bool IsFleetVisible => SelectedDevice is null;
    public bool HasProfiles => Profiles.Count > 0;
    public bool IsSettingsScopeLocked => Detail?.OverrideCount == 0;
    public int AllSettingCount => AdminDeviceSettingDefinition.All.Count;
    public int SelectedProfileOverrideCount => Detail is null || SelectedProfile is null ? 0 : Detail.Settings.Count(setting => setting.ProfileId == SelectedProfile.ProfileId);
    public string ResultsLabel => $"{Devices.Count} {(Devices.Count == 1 ? "device" : "devices")}";
    public int DevicesWithOverrides => _allDevices.Count(d => d.OverrideCount > 0);
    private IEnumerable<AdminDeviceSummary> ScopedDevices => OverridesOnly
        ? _allDevices.Where(device => device.OverrideCount > 0)
        : _allDevices;
    public int AnomalyCount => ScopedDevices.Count(IsAnomalous);
    public int UpdatedWeekCount => ScopedDevices.Count(d => AgeInDays(d) < 7);
    public int HdrCapableCount => ScopedDevices.Count(d => DeviceHints(d).Contains("tv", StringComparison.OrdinalIgnoreCase) ||
        DeviceHints(d).Contains("appletv", StringComparison.OrdinalIgnoreCase) || DeviceHints(d).Contains("shield", StringComparison.OrdinalIgnoreCase));
    public int HeavyCustomizerCount => ScopedDevices.Count(d => d.OverrideCount >= 3);
    public int DormantCount => ScopedDevices.Count(d => AgeInDays(d) > 30);
    public int TvCount => ScopedDevices.Count(d => PlatformKind(d.DevicePlatform) == "TV");
    public int MobileCount => ScopedDevices.Count(d => PlatformKind(d.DevicePlatform) == "Mobile");
    public int TabletCount => ScopedDevices.Count(d => PlatformKind(d.DevicePlatform) == "Tablet");
    public int DesktopCount => ScopedDevices.Count(d => PlatformKind(d.DevicePlatform) == "Desktop");
    public int OtherCount => ScopedDevices.Count(d => PlatformKind(d.DevicePlatform) == "Other");
    public int NoOverrideCount => ScopedDevices.Count(d => d.OverrideCount == 0);
    public int OneTwoOverrideCount => ScopedDevices.Count(d => d.OverrideCount is >= 1 and <= 2);
    public int ThreeFiveOverrideCount => ScopedDevices.Count(d => d.OverrideCount is >= 3 and <= 5);
    public int SixPlusOverrideCount => ScopedDevices.Count(d => d.OverrideCount >= 6);
    public int DayCount => ScopedDevices.Count(d => RecencyBucket(d) == "<24h");
    public int WeekCount => ScopedDevices.Count(d => RecencyBucket(d) == "<7d");
    public int MonthCount => ScopedDevices.Count(d => RecencyBucket(d) == "<30d");
    public int OlderCount => ScopedDevices.Count(d => RecencyBucket(d) == ">30d");

    private readonly HashSet<string> _platformFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _overrideFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recencyFilters = new(StringComparer.OrdinalIgnoreCase);

    public void ApplySearchFilter() => ApplyFilters();
    partial void OnPlatformFilterChanged(string value) => ApplyFilters();
    partial void OnRecencyFilterChanged(string value) => ApplyFilters();
    partial void OnOverridesOnlyChanged(bool value) => ApplyFilters();
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowDeviceListEmpty));
    partial void OnShowAllSettingsChanged(bool value) => RebuildSettings();
    partial void OnGroupByChanged(string value) => ApplyFilters();
    partial void OnActiveSavedViewChanged(string? value) => ApplyFilters();

    public void SetGroupBy(string value) => GroupBy = value;
    public void SetSavedView(string? value) => ActiveSavedView = ActiveSavedView == value ? null : value;
    public void SetFacet(string category, string value, bool enabled)
    {
        var target = category switch
        {
            "platform" => _platformFilters,
            "override" => _overrideFilters,
            "recency" => _recencyFilters,
            _ => null,
        };
        if (target is null) return;
        if (enabled) target.Add(value); else target.Remove(value);
        ApplyFilters();
    }

    public void ClearFilters()
    {
        _platformFilters.Clear();
        _overrideFilters.Clear();
        _recencyFilters.Clear();
        ActiveSavedView = null;
        OverridesOnly = false;
        SearchText = "";
        ApplyFilters();
    }

    [RelayCommand]
    public async Task LoadAsync(bool force = false)
    {
        if (HasLoaded && !force) return;
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, owner);
        previous?.Cancel();
        previous?.Dispose();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var devices = await adminApi.GetDevicesAsync(owner.Token);
            owner.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, owner)) return;
            _allDevices.Clear();
            _allDevices.AddRange(devices);
            ApplyFilters();
            HasLoaded = true;
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_loadCts, owner)) ErrorMessage = ex.Message; }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, owner), owner))
                IsLoading = false;
            owner.Dispose();
        }
    }

    public async Task SelectDeviceAsync(AdminDeviceCardViewModel device)
    {
        SelectedDevice = device;
        ShowAllSettings = !OverridesOnly || device.Source.OverrideCount == 0;
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsFleetVisible));
        IsDetailLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            Detail = await adminApi.GetDeviceAsync(device.Source.UserId, device.Source.DeviceId);
            if (Detail.OverrideCount == 0) ShowAllSettings = true;
            Profiles.Clear();
            foreach (var profile in Detail.Profiles.OrderBy(p => p.ProfileName, StringComparer.CurrentCultureIgnoreCase))
                Profiles.Add(new AdminDeviceProfileOption(profile.ProfileId,
                    string.IsNullOrWhiteSpace(profile.ProfileName) ? "Unknown profile" : profile.ProfileName,
                    profile.OverrideCount));

            // A registered device can precede its first override. Keep the detail useful
            // even when the server does not yet have a named profile association.
            if (Profiles.Count == 0)
            {
                foreach (var profileId in Detail.Settings.Select(s => s.ProfileId).Distinct())
                    Profiles.Add(new AdminDeviceProfileOption(profileId, "Unknown profile", 0));
            }
            SelectedProfile = Profiles.FirstOrDefault();
            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(IsSettingsScopeLocked));
            OnPropertyChanged(nameof(SelectedProfileOverrideCount));
            RebuildSettings();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsDetailLoading = false; }
    }

    public async Task OpenDeviceAsync(int userId, string deviceId)
    {
        var source = _allDevices.FirstOrDefault(device => device.UserId == userId &&
            string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
        if (source is null)
        {
            ErrorMessage = "The selected device is no longer registered.";
            return;
        }
        await SelectDeviceAsync(new AdminDeviceCardViewModel(source));
    }

    public void CloseDetail()
    {
        SelectedDevice = null;
        Detail = null;
        SelectedProfile = null;
        Profiles.Clear();
        Settings.Clear();
        ErrorMessage = null;
        StatusMessage = null;
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsFleetVisible));
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(IsSettingsScopeLocked));
        OnPropertyChanged(nameof(SelectedProfileOverrideCount));
    }

    partial void OnSelectedProfileChanged(AdminDeviceProfileOption? value)
    {
        OnPropertyChanged(nameof(SelectedProfileOverrideCount));
        RebuildSettings();
    }

    public async Task SaveSettingAsync(AdminDeviceSettingRow row, string value)
    {
        if (Detail is null || SelectedProfile is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await adminApi.UpdateDeviceSettingAsync(Detail.UserId, SelectedProfile.ProfileId,
                Detail.DeviceId, row.Key, value);
            StatusMessage = $"{row.Label} updated.";
            await RefreshDetailAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task ResetSettingAsync(AdminDeviceSettingRow row)
    {
        if (Detail is null || SelectedProfile is null || !row.IsOverride || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await adminApi.DeleteDeviceSettingAsync(Detail.UserId, SelectedProfile.ProfileId,
                Detail.DeviceId, row.Key);
            StatusMessage = $"{row.Label} reset.";
            await RefreshDetailAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task SaveSettingValueOnlyAsync(AdminDeviceSettingRow row, string value)
    {
        if (Detail is null || SelectedProfile is null) return;
        await adminApi.UpdateDeviceSettingAsync(Detail.UserId, SelectedProfile.ProfileId, Detail.DeviceId, row.Key, value);
    }

    public async Task ResetSettingValueOnlyAsync(AdminDeviceSettingRow row)
    {
        if (Detail is null || SelectedProfile is null || !row.IsOverride) return;
        await adminApi.DeleteDeviceSettingAsync(Detail.UserId, SelectedProfile.ProfileId, Detail.DeviceId, row.Key);
    }

    public async Task RefreshSelectedDeviceAsync()
    {
        await RefreshDetailAsync();
        StatusMessage = "Subtitle appearance updated.";
    }

    public async Task ResetProfileAsync()
    {
        if (Detail is null || SelectedProfile is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await adminApi.DeleteAllDeviceSettingsAsync(Detail.UserId, SelectedProfile.ProfileId,
                Detail.DeviceId);
            StatusMessage = $"All overrides for {SelectedProfile.Name} were reset.";
            await RefreshDetailAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task RefreshDetailAsync()
    {
        if (Detail is null || SelectedProfile is null) return;
        var profileId = SelectedProfile.ProfileId;
        Detail = await adminApi.GetDeviceAsync(Detail.UserId, Detail.DeviceId);
        if (Detail.OverrideCount == 0) ShowAllSettings = true;
        Profiles.Clear();
        foreach (var profile in Detail.Profiles.OrderBy(p => p.ProfileName))
            Profiles.Add(new(profile.ProfileId,
                string.IsNullOrWhiteSpace(profile.ProfileName) ? "Unknown profile" : profile.ProfileName,
                profile.OverrideCount));
        SelectedProfile = Profiles.FirstOrDefault(p => p.ProfileId == profileId) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(IsSettingsScopeLocked));
        OnPropertyChanged(nameof(SelectedProfileOverrideCount));
        RebuildSettings();
        _ = LoadAsync(force: true);
    }

    private void ApplyFilters()
    {
        IEnumerable<AdminDeviceSummary> query = _allDevices;
        if (OverridesOnly) query = query.Where(d => d.OverrideCount > 0);
        query = ActiveSavedView switch
        {
            "anomalies" => query.Where(IsAnomalous),
            "recent" => query.Where(d => AgeInDays(d) < 7),
            "hdr" => query.Where(d => DeviceHints(d).Contains("tv", StringComparison.OrdinalIgnoreCase) ||
                                      DeviceHints(d).Contains("appletv", StringComparison.OrdinalIgnoreCase) ||
                                      DeviceHints(d).Contains("shield", StringComparison.OrdinalIgnoreCase)),
            "subtitle" => query.Where(d => d.OverrideCount >= 3),
            "dormant" => query.Where(d => AgeInDays(d) > 30),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(d => d.DeviceName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                d.DevicePlatform.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                d.Username.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                d.Email.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                d.DeviceId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                d.Profiles.Any(p => p.ProfileName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                                    p.ProfileId.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        if (PlatformFilter != "All platforms")
            query = query.Where(d => PlatformKind(d.DevicePlatform) == PlatformFilter);
        if (_platformFilters.Count > 0)
            query = query.Where(d => _platformFilters.Contains(PlatformKind(d.DevicePlatform)));
        if (_overrideFilters.Count > 0)
            query = query.Where(d => _overrideFilters.Contains(OverrideBucket(d.OverrideCount)));
        if (_recencyFilters.Count > 0)
            query = query.Where(d => _recencyFilters.Contains(RecencyBucket(d)));
        if (RecencyFilter != "Any activity")
        {
            int? days = RecencyFilter switch { "Last 24 hours" => 1, "Last 7 days" => 7, "Last 30 days" => 30, _ => null };
            if (days is { } window)
                query = query.Where(d => d.LastUpdated is { } when && DateTimeOffset.UtcNow - when < TimeSpan.FromDays(window));
        }
        Devices.Clear();
        foreach (var device in query.OrderByDescending(d => d.LastUpdated))
            Devices.Add(new(device, IsAnomalous(device)));
        RebuildGroups();
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasAnyDevices));
        OnPropertyChanged(nameof(ShowDeviceListEmpty));
        OnPropertyChanged(nameof(DeviceListEmptyTitle));
        OnPropertyChanged(nameof(DeviceListEmptyDescription));
        OnPropertyChanged(nameof(ResultsLabel));
        OnPropertyChanged(nameof(TotalDevices));
        OnPropertyChanged(nameof(TotalUsers));
        OnPropertyChanged(nameof(TotalProfiles));
        OnPropertyChanged(nameof(TotalOverrides));
        OnPropertyChanged(nameof(DevicesWithOverrides));
        OnPropertyChanged(nameof(AnomalyCount));
        OnPropertyChanged(nameof(UpdatedWeekCount));
        OnPropertyChanged(nameof(HdrCapableCount));
        OnPropertyChanged(nameof(HeavyCustomizerCount));
        OnPropertyChanged(nameof(DormantCount));
        OnPropertyChanged(nameof(TvCount));
        OnPropertyChanged(nameof(MobileCount));
        OnPropertyChanged(nameof(TabletCount));
        OnPropertyChanged(nameof(DesktopCount));
        OnPropertyChanged(nameof(OtherCount));
        OnPropertyChanged(nameof(NoOverrideCount));
        OnPropertyChanged(nameof(OneTwoOverrideCount));
        OnPropertyChanged(nameof(ThreeFiveOverrideCount));
        OnPropertyChanged(nameof(SixPlusOverrideCount));
        OnPropertyChanged(nameof(DayCount));
        OnPropertyChanged(nameof(WeekCount));
        OnPropertyChanged(nameof(MonthCount));
        OnPropertyChanged(nameof(OlderCount));
    }

    private void RebuildGroups()
    {
        var grouped = Devices.GroupBy(device => GroupBy switch
        {
            "platform" => device.PlatformKind,
            "activity" => RecencyBucket(device.Source) switch
            {
                "<24h" => "Last 24 hours",
                "<7d" => "This week",
                "<30d" => "This month",
                _ => "Older than 30 days",
            },
            _ => string.IsNullOrWhiteSpace(device.Source.Username)
                ? (!string.IsNullOrWhiteSpace(device.Source.Email) ? device.Source.Email : $"User {device.Source.UserId}")
                : device.Source.Username,
        });

        var order = GroupBy == "platform"
            ? new[] { "TV", "Mobile", "Tablet", "Desktop", "Other" }
            : GroupBy == "activity"
                ? new[] { "Last 24 hours", "This week", "This month", "Older than 30 days" }
                : [];

        var projected = grouped.Select(g => new AdminDeviceGroupViewModel(g.Key, g.ToList()));
        projected = order.Length == 0
            ? projected.OrderBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
            : projected.OrderBy(g => Array.IndexOf(order, g.Label));

        DeviceGroups.Clear();
        foreach (var group in projected) DeviceGroups.Add(group);
    }

    private bool IsAnomalous(AdminDeviceSummary device)
    {
        var platform = PlatformKind(device.DevicePlatform);
        var peerCounts = _allDevices
            .Where(candidate => PlatformKind(candidate.DevicePlatform) == platform)
            .Select(candidate => candidate.OverrideCount)
            .Order()
            .ToArray();
        var median = peerCounts.Length == 0 ? 0 : peerCounts[peerCounts.Length / 2];
        var outlier = device.OverrideCount >= 6 ||
                      (median > 0 && device.OverrideCount >= median * 2 && device.OverrideCount >= 4);
        var stale = device.OverrideCount > 0 && AgeInDays(device) > 30;
        return outlier || stale;
    }
    private static double AgeInDays(AdminDeviceSummary device) => device.LastUpdated is { } last
        ? Math.Max(0, (DateTimeOffset.UtcNow - last).TotalDays)
        : double.PositiveInfinity;
    private static string RecencyBucket(AdminDeviceSummary device) => AgeInDays(device) switch
    {
        < 1 => "<24h",
        < 7 => "<7d",
        < 30 => "<30d",
        _ => ">30d",
    };
    private static string OverrideBucket(int count) => count switch
    {
        0 => "none",
        <= 2 => "1-2",
        <= 5 => "3-5",
        _ => "6+",
    };
    private static string DeviceHints(AdminDeviceSummary device) => $"{device.DeviceName} {device.DevicePlatform} {device.DeviceId}";

    private void RebuildSettings()
    {
        Settings.Clear();
        if (Detail is null || SelectedProfile is null) return;
        var existing = Detail.Settings.Where(s => s.ProfileId == SelectedProfile.ProfileId)
            .ToDictionary(s => s.Key, StringComparer.Ordinal);
        foreach (var definition in AdminDeviceSettingDefinition.All)
        {
            existing.TryGetValue(definition.Key, out var setting);
            if (!ShowAllSettings && setting is null) continue;
            Settings.Add(new(definition, setting));
        }
    }

    public void Cancel() => Interlocked.Exchange(ref _loadCts, null)?.Cancel();

    public static string PlatformKind(string raw)
    {
        var p = raw.ToLowerInvariant();
        if (new[] { "tvos", "apple tv", "androidtv", "android tv", "roku", "firetv", "fire tv", "webos", "tizen" }.Any(p.Contains) ||
            System.Text.RegularExpressions.Regex.IsMatch(p, @"\btv\b")) return "TV";
        if (p.Contains("ipad") || p.Contains("tablet")) return "Tablet";
        if (new[] { "ios", "iphone", "android", "mobile", "phone" }.Any(p.Contains)) return "Mobile";
        if (new[] { "mac", "win", "linux", "desktop", "chrome", "safari", "firefox", "edge", "web" }.Any(p.Contains)) return "Desktop";
        return "Other";
    }
}

public sealed record AdminDeviceProfileOption(string ProfileId, string Name, int OverrideCount)
{
    public string DisplayName => $"{Name}  ·  {OverrideCount} {(OverrideCount == 1 ? "override" : "overrides")}";
}

public sealed class AdminDeviceGroupViewModel(string label, IReadOnlyList<AdminDeviceCardViewModel> devices)
{
    public string Label { get; } = label;
    public IReadOnlyList<AdminDeviceCardViewModel> Devices { get; } = devices;
    public string Meta => $"{Devices.Count}d · {Devices.Sum(d => d.Source.ProfileCount)}p · {Devices.Sum(d => d.Source.OverrideCount)}k";
    // Current WebUI starts every group collapsed, including one-device users.
    public bool IsExpanded => false;
}

public sealed class AdminDeviceCardViewModel(AdminDeviceSummary source, bool isAnomalous = false)
{
    public AdminDeviceSummary Source { get; } = source;
    public string Name => string.IsNullOrWhiteSpace(Source.DeviceName) ? "Unknown device" : Source.DeviceName;
    public string Platform => string.IsNullOrWhiteSpace(Source.DevicePlatform) ? "Unknown platform" : Source.DevicePlatform;
    public string PlatformKind => AdminDevicesViewModel.PlatformKind(Source.DevicePlatform);
    public string User => string.IsNullOrWhiteSpace(Source.Email) ? Source.Username : $"{Source.Username}  ·  {Source.Email}";
    public string DeviceId => Source.DeviceId;
    public string Activity => Source.LastUpdated is { } value ? TimeAgo.FormatShort(value.ToString("O")) : "Never";
    public string Overrides => $"{Source.OverrideCount} {(Source.OverrideCount == 1 ? "override" : "overrides")}";
    public string Profiles => $"{Source.ProfileCount} {(Source.ProfileCount == 1 ? "profile" : "profiles")}";
    public bool IsAnomalous { get; } = isAnomalous;
    public string Status => IsAnomalous ? "Anomaly" : "Normal";
    public string StatusDetail => IsAnomalous ? "override pattern needs review" : $"aligned with {PlatformKind.ToLowerInvariant()} fleet";
}

public sealed class AdminDeviceSettingRow(AdminDeviceSettingDefinition definition, AdminDeviceSetting? setting)
{
    public string Key => definition.Key;
    public string Label => definition.Label;
    public string Description => definition.Description;
    public string Control => definition.Control;
    public string Value => setting?.Value ?? definition.DefaultValue;
    public IReadOnlyList<AdminDeviceSettingOption> Options => definition.Options;
    public bool IsOverride => setting is not null;
    public string Scope => IsOverride ? "Device override" : "Inherited default";
    public string Updated => setting?.UpdatedAt is { } value ? TimeAgo.FormatShort(value.ToString("O")) : "";
}

public sealed record AdminDeviceSettingOption(string Value, string Label);
public sealed record AdminDeviceSettingDefinition(string Key, string Label, string Description,
    string Control, string DefaultValue, IReadOnlyList<AdminDeviceSettingOption> Options)
{
    private static AdminDeviceSettingDefinition D(string key, string label, string description,
        string control, string value, params AdminDeviceSettingOption[] options) => new(key, label, description, control, value, options);
    private static AdminDeviceSettingOption O(string value, string label) => new(value, label);

    public static IReadOnlyList<AdminDeviceSettingDefinition> All { get; } =
    [
        D("playback.preferred_quality", "Preferred quality", "Quality Silo should prefer on this device.", "select", "auto", O("auto","Auto"), O("original","Original quality"), O("2160p","2160p / 4K"), O("1080p","1080p"), O("720p","720p"), O("480p","480p")),
        D("playback.audio_language", "Preferred audio language", "Spoken language Silo should prefer first.", "text", ""),
        D("playback.auto_skip_intro", "Auto-skip intros", "Jump past detected intros automatically.", "switch", "false"),
        D("playback.auto_skip_credits", "Auto-skip credits", "Move through detected end credits automatically.", "switch", "false"),
        D("playback.auto_skip_recap", "Auto-skip recaps", "Skip detected recap segments automatically.", "switch", "false"),
        D("playback.auto_play_next_preview", "Start next episode at preview", "Advance when the next-episode preview begins.", "switch", "false"),
        D("playback.auto_play_next", "Auto-play next episode", "Start the next episode after the current one ends.", "switch", "true"),
        D("playback.next_up_prompt_seconds", "Next Up prompt", "When the Next Up screen appears.", "select", "30", O("0","At end"), O("10","10 seconds before end"), O("30","30 seconds before end"), O("60","1 minute before end"), O("120","2 minutes before end")),
        D("subtitle_appearance", "Subtitle appearance", "Subtitle appearance JSON for this device.", "json", "{}"),
        D("ui.remember_library_page_state", "Remember library pages", "Restore the last tab, sort, and filters.", "switch", "true"),
        D("player.hdr_enabled", "HDR enabled", "Allow HDR when the player and display support it.", "switch", "true"),
        D("player.dv_profile7_hdr10_fallback", "Profile 7 HDR10 fallback", "Use the HDR10 base layer for Dolby Vision Profile 7.", "switch", "false"),
        D("player.playback_speed", "Playback speed", "Default playback speed.", "select", "1", O("0.25","0.25x"), O("0.5","0.5x"), O("0.75","0.75x"), O("1","1x (Normal)"), O("1.25","1.25x"), O("1.5","1.5x"), O("1.75","1.75x"), O("2","2x"), O("2.5","2.5x"), O("3","3x")),
        D("player.audio_sync_ms", "Audio sync", "Audio timing offset in milliseconds.", "number", "0"),
        D("player.subtitle_sync_ms", "Subtitle sync", "Subtitle timing offset in milliseconds.", "number", "0"),
        D("player.video_gravity", "Video fit", "How video fills the screen.", "select", "fit", O("fit","Fit"), O("fill","Fill"), O("stretch","Stretch")),
        D("player.orientation_mode", "Orientation mode", "Whether playback rotates freely.", "select", "landscapeLocked", O("landscapeLocked","Landscape locked"), O("rotateFreely","Rotate freely")),
    ];
}
