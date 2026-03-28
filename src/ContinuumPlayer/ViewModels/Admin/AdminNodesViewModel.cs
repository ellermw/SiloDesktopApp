using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminNodesViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminNodesViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<StreamNode> ProxyNodes { get; } = [];
    public ObservableCollection<StreamNode> TranscodeNodes { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var nodes = await _adminApi.GetNodesAsync();
            ProxyNodes.Clear();
            TranscodeNodes.Clear();
            foreach (var n in nodes)
            {
                if (n.Type == "proxy")
                    ProxyNodes.Add(n);
                else
                    TranscodeNodes.Add(n);
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Create Node =====

    [RelayCommand]
    public async Task CreateNodeAsync(CreateNodeRequest request)
    {
        try
        {
            await _adminApi.CreateNodeAsync(request);
            StatusMessage = $"Node \"{request.Name}\" created.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Update Node =====

    [RelayCommand]
    public async Task UpdateNodeAsync((int Id, string Name, string Url) args)
    {
        try
        {
            await _adminApi.UpdateNodeAsync(args.Id, new { name = args.Name, url = args.Url });
            StatusMessage = "Node updated.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Delete Node =====

    [RelayCommand]
    public async Task DeleteNodeAsync(int id)
    {
        try
        {
            await _adminApi.DeleteNodeAsync(id);
            StatusMessage = "Node deleted.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Check Health =====

    [RelayCommand]
    public async Task<CheckNodeResponse?> CheckHealthAsync(int id)
    {
        try
        {
            var result = await _adminApi.CheckNodeAsync(id);
            StatusMessage = result.Healthy
                ? "Node is healthy."
                : $"Node is unhealthy: {result.Message}";
            await LoadAsync();
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    // ===== Toggle Node =====

    [RelayCommand]
    public async Task ToggleNodeAsync(int id)
    {
        // Find the node in either collection
        var node = ProxyNodes.FirstOrDefault(n => n.Id == id)
                ?? TranscodeNodes.FirstOrDefault(n => n.Id == id);
        if (node == null) return;

        bool newEnabled = !node.Enabled;
        try
        {
            await _adminApi.UpdateNodeAsync(id, new { enabled = newEnabled });
            StatusMessage = newEnabled ? "Node enabled." : "Node disabled.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
