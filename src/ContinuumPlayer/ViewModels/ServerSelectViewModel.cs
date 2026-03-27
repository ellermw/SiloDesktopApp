using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class ServerSelectViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    public ServerSelectViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadServers();
    }

    public ObservableCollection<ServerEntry> Servers { get; } = [];

    [ObservableProperty]
    private string _newServerUrl = "";

    [ObservableProperty]
    private string _newServerName = "";

    [ObservableProperty]
    private ServerEntry? _selectedServer;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isAddingServer;

    [RelayCommand]
    private void ShowAddServer()
    {
        IsAddingServer = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CancelAddServer()
    {
        IsAddingServer = false;
        NewServerUrl = "";
        NewServerName = "";
        ErrorMessage = null;
    }

    [RelayCommand]
    private void AddServer()
    {
        if (string.IsNullOrWhiteSpace(NewServerUrl))
        {
            ErrorMessage = "Server URL is required.";
            return;
        }

        var url = NewServerUrl.Trim().TrimEnd('/');
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            url = "https://" + url;
        }

        var name = string.IsNullOrWhiteSpace(NewServerName) ? url : NewServerName.Trim();

        try
        {
            _settingsService.AddServer(url, name);
            LoadServers();
            NewServerUrl = "";
            NewServerName = "";
            IsAddingServer = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add server: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveServer(ServerEntry server)
    {
        try
        {
            _settingsService.RemoveServer(server.Url);
            LoadServers();
            if (SelectedServer?.Url == server.Url)
                SelectedServer = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove server: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectServer(ServerEntry server)
    {
        SelectedServer = server;
        _settingsService.UpdateLastUsed(server.Url);
    }

    private void LoadServers()
    {
        var settings = _settingsService.Load();
        Servers.Clear();
        foreach (var server in settings.Servers.OrderByDescending(s => s.LastUsed))
        {
            Servers.Add(server);
        }
    }
}
