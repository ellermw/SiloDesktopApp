using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Plugins;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminPluginsViewModel : ObservableObject
{
    private readonly PluginsApi _pluginsApi;

    public AdminPluginsViewModel(PluginsApi pluginsApi)
    {
        _pluginsApi = pluginsApi;
    }

    public ObservableCollection<PluginInstallation> Installations { get; } = [];
    public ObservableCollection<PluginCatalogEntry> CatalogEntries { get; } = [];
    public ObservableCollection<PluginRepository> Repositories { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _selectedTab = "installed";
    [ObservableProperty] private PluginCatalogSettings? _catalogSettings;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var installTask = _pluginsApi.GetInstallationsAsync();
            var catalogTask = _pluginsApi.GetCatalogAsync();
            var reposTask = _pluginsApi.GetRepositoriesAsync();
            var settingsTask = _pluginsApi.GetCatalogSettingsAsync();
            await Task.WhenAll(installTask, catalogTask, reposTask, settingsTask);

            Installations.Clear();
            foreach (var i in installTask.Result) Installations.Add(i);

            CatalogEntries.Clear();
            foreach (var c in catalogTask.Result) CatalogEntries.Add(c);

            Repositories.Clear();
            foreach (var r in reposTask.Result) Repositories.Add(r);
            CatalogSettings = settingsTask.Result;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    public async Task SetApprovedCommunityCatalogAsync(bool included)
    {
        try
        {
            CatalogSettings = await _pluginsApi.UpdateCatalogSettingsAsync(included);
            StatusMessage = included
                ? "Approved community plugins are visible in the catalog."
                : "Approved community plugins are hidden and their updates are paused.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task ToggleRepositoryAsync(PluginRepository repository)
    {
        if (repository.Managed) return;
        try
        {
            await _pluginsApi.UpdateRepositoryAsync(repository.Id, new UpdatePluginRepositoryRequest
            {
                Enabled = !repository.Enabled,
            });
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Install Plugin =====

    [RelayCommand]
    public async Task InstallPluginAsync(PluginCatalogEntry entry)
    {
        try
        {
            var request = new InstallPluginRequest
            {
                RepositoryId = entry.RepositoryId,
                PluginId = entry.PluginId,
                Version = entry.Version
            };
            await _pluginsApi.InstallPluginAsync(request);
            StatusMessage = $"Plugin \"{entry.PluginId}\" installed.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Update Plugin =====

    [RelayCommand]
    public async Task UpdatePluginAsync(int id)
    {
        try
        {
            await _pluginsApi.UpdatePluginAsync(id);
            StatusMessage = "Plugin updated.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Enable/Disable =====

    [RelayCommand]
    public async Task TogglePluginAsync(PluginInstallation plugin)
    {
        try
        {
            await _pluginsApi.UpdateInstallationAsync(plugin.Id, new UpdatePluginInstallationRequest
            {
                Enabled = !plugin.Enabled
            });
            StatusMessage = plugin.Enabled ? "Plugin disabled." : "Plugin enabled.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Delete =====

    [RelayCommand]
    public async Task DeletePluginAsync(int id)
    {
        try
        {
            await _pluginsApi.DeleteInstallationAsync(id);
            StatusMessage = "Plugin deleted.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Add Repository =====

    public async Task<PluginRepository?> AddRepositoryAsync(CreatePluginRepositoryRequest request)
    {
        try
        {
            var repo = await _pluginsApi.CreateRepositoryAsync(request);
            StatusMessage = $"Repository \"{repo.DisplayName}\" added.";
            await LoadAsync();
            return repo;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    // ===== Delete Repository =====

    [RelayCommand]
    public async Task DeleteRepositoryAsync(int id)
    {
        try
        {
            await _pluginsApi.DeleteRepositoryAsync(id);
            StatusMessage = "Repository deleted.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Save Global Config =====

    public async Task SaveGlobalConfigAsync(int installationId, SavePluginConfigRequest request)
    {
        try
        {
            await _pluginsApi.SaveGlobalConfigAsync(installationId, request);
            StatusMessage = "Configuration saved.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
