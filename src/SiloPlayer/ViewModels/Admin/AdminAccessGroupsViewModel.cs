using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminAccessGroupsViewModel(AdminApi adminApi) : ObservableObject
{
    public const string MetadataCurationPermission = "metadata_curation";
    public const string MarkerEditPermission = "marker_edit";

    private CancellationTokenSource? _loadCts;

    public ObservableCollection<AccessGroupCardViewModel> Groups { get; } = [];
    public ObservableCollection<AccessGroupLibraryOptionViewModel> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private bool _hasGroups;
    [ObservableProperty] private bool _showEmptyState;
    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsDefaultLocked))]
    [NotifyPropertyChangedFor(nameof(CanChangeDefault))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private AccessGroupCardViewModel? _selectedGroup;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectSpecificLibraries))]
    private bool _allLibraries = true;

    [ObservableProperty] private string _qualityPreset = "any";
    [ObservableProperty] private bool _downloadAllowed;
    [ObservableProperty] private bool _downloadTranscodeAllowed;
    [ObservableProperty] private bool _requestsAllowed;
    [ObservableProperty] private double _maxStreams;
    [ObservableProperty] private double _maxTranscodes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectSpecificPermissions))]
    private bool _allPermissions = true;

    [ObservableProperty] private bool _metadataCurationAllowed = true;
    [ObservableProperty] private bool _markerEditingAllowed = true;

    [ObservableProperty] private bool _isDefault;

    public bool IsListVisible => SelectedGroup is null;
    public bool IsEditorVisible => SelectedGroup is not null;
    public bool SelectSpecificLibraries => !AllLibraries;
    public bool SelectSpecificPermissions => !AllPermissions;
    public bool IsDefaultLocked => SelectedGroup?.Source.IsDefault == true;
    public bool CanChangeDefault => SelectedGroup is not null && !IsDefaultLocked;
    public bool CanDelete => SelectedGroup is not null && !IsDefaultLocked;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, ownerCts);
        previous?.Cancel();
        previous?.Dispose();

        IsLoading = true;
        ErrorMessage = null;
        ShowEmptyState = false;
        try
        {
            var groupsTask = adminApi.GetAccessGroupsAsync(ownerCts.Token);
            var librariesTask = adminApi.GetAdminLibrariesAsync(ownerCts.Token);
            await Task.WhenAll(groupsTask, librariesTask);
            ownerCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, ownerCts))
                return;

            var selectedId = SelectedGroup?.Id;
            Groups.Clear();
            foreach (var group in groupsTask.Result.OrderByDescending(group => group.IsDefault)
                         .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase))
                Groups.Add(new AccessGroupCardViewModel(group));

            Libraries.Clear();
            foreach (var library in librariesTask.Result.OrderBy(library => library.Name,
                         StringComparer.CurrentCultureIgnoreCase))
                Libraries.Add(new AccessGroupLibraryOptionViewModel(library.Id, library.Name));

            HasGroups = Groups.Count > 0;
            ShowEmptyState = Groups.Count == 0 && !IsCreating;

            if (selectedId is long id)
            {
                var refreshed = Groups.FirstOrDefault(group => group.Id == id);
                if (refreshed is not null)
                    SelectGroup(refreshed);
                else
                    SelectedGroup = null;
            }
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, ownerCts))
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, ownerCts), ownerCts))
                IsLoading = false;
            ownerCts.Dispose();
        }
    }

    public void BeginCreate()
    {
        IsCreating = true;
        NewGroupName = "";
        ShowEmptyState = false;
        ErrorMessage = null;
    }

    public void CancelCreate()
    {
        IsCreating = false;
        NewGroupName = "";
        ShowEmptyState = Groups.Count == 0;
    }

    [RelayCommand]
    public async Task CreateAsync()
    {
        var trimmed = NewGroupName.Trim();
        if (trimmed.Length == 0 || IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var created = await adminApi.CreateAccessGroupAsync(trimmed);
            IsCreating = false;
            NewGroupName = "";
            await LoadAsync();
            var card = Groups.FirstOrDefault(group => group.Id == created.Id);
            if (card is not null)
                SelectGroup(card);
            StatusMessage = "Access group created.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectGroup(AccessGroupCardViewModel group)
    {
        SelectedGroup = group;
        var source = group.Source;
        Name = source.Name;
        Description = source.Description;
        AllLibraries = source.LibraryIds is null;
        foreach (var library in Libraries)
            library.IsSelected = source.LibraryIds is null || source.LibraryIds.Contains(library.Id);
        QualityPreset = PlaybackQualityPresetFromValue(source.MaxPlaybackQuality);
        DownloadAllowed = source.DownloadAllowed;
        DownloadTranscodeAllowed = source.DownloadTranscodeAllowed;
        RequestsAllowed = source.RequestsAllowed;
        MaxStreams = Math.Max(0, source.MaxStreams);
        MaxTranscodes = Math.Max(0, source.MaxTranscodes);
        AllPermissions = source.AllowedPermissions is null;
        MetadataCurationAllowed = source.AllowedPermissions is null ||
                                  source.AllowedPermissions.Contains(MetadataCurationPermission);
        MarkerEditingAllowed = source.AllowedPermissions is null ||
                               source.AllowedPermissions.Contains(MarkerEditPermission);
        IsDefault = source.IsDefault;
        ErrorMessage = null;
        StatusMessage = null;
    }

    public void CloseEditor()
    {
        SelectedGroup = null;
        ErrorMessage = null;
        StatusMessage = null;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (SelectedGroup is null || IsBusy)
            return;

        var trimmedName = Name.Trim();
        if (trimmedName.Length == 0)
        {
            ErrorMessage = "Name is required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var request = new UpdateAccessGroupRequest
            {
                Name = trimmedName,
                Description = Description.Trim(),
                LibraryIds = AllLibraries
                    ? null
                    : Libraries.Where(library => library.IsSelected).Select(library => library.Id).ToList(),
                MaxPlaybackQuality = PlaybackQualityValueFromPreset(QualityPreset),
                DownloadAllowed = DownloadAllowed,
                DownloadTranscodeAllowed = DownloadAllowed && DownloadTranscodeAllowed,
                MaxStreams = NormalizeLimit(MaxStreams),
                MaxTranscodes = NormalizeLimit(MaxTranscodes),
                AllowedPermissions = AllPermissions
                    ? null
                    : BuildAllowedPermissions(),
                RequestsAllowed = RequestsAllowed,
                IsDefault = IsDefault,
            };

            var id = SelectedGroup.Id;
            await adminApi.UpdateAccessGroupAsync(id, request);
            await LoadAsync();
            StatusMessage = "Access group saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedGroup is null || !CanDelete || IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await adminApi.DeleteAccessGroupAsync(SelectedGroup.Id);
            SelectedGroup = null;
            await LoadAsync();
            StatusMessage = "Access group deleted.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoading = false;
    }

    private List<string> BuildAllowedPermissions()
    {
        var permissions = new List<string>(2);
        if (MetadataCurationAllowed)
            permissions.Add(MetadataCurationPermission);
        if (MarkerEditingAllowed)
            permissions.Add(MarkerEditPermission);
        return permissions;
    }

    private static int NormalizeLimit(double value) =>
        double.IsFinite(value) && value > 0
            ? (int)Math.Min(int.MaxValue, Math.Floor(value))
            : 0;

    private static string PlaybackQualityPresetFromValue(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "2160p" or "4320p" or "4k" or "uhd" => "4k",
            "480p" or "720p" or "1080p" or "standard" => "standard",
            _ => "any",
        };

    private static string PlaybackQualityValueFromPreset(string preset) => preset switch
    {
        "standard" => "1080p",
        "4k" => "2160p",
        _ => "",
    };
}

public sealed class AccessGroupCardViewModel(AccessGroup source)
{
    public AccessGroup Source { get; } = source;
    public long Id => Source.Id;
    public string Name => Source.Name;
    public string Description => Source.Description;
    public bool IsDefault => Source.IsDefault;
    public string MemberLabel => $"{Source.MemberCount} {(Source.MemberCount == 1 ? "member" : "members")}";
    public string LibraryLabel => Source.LibraryIds is null
        ? "All libraries"
        : $"{Source.LibraryIds.Count} {(Source.LibraryIds.Count == 1 ? "library" : "libraries")}";
    public string DownloadLabel => Source.DownloadAllowed ? "Downloads on" : "No downloads";
    public string StreamLabel => Source.MaxStreams > 0
        ? $"{Source.MaxStreams} {(Source.MaxStreams == 1 ? "stream" : "streams")}" : "Unlimited streams";
    public string RequestLabel => Source.RequestsAllowed ? "Requests on" : "No requests";
}

public partial class AccessGroupLibraryOptionViewModel(int id, string name) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isSelected;
}
