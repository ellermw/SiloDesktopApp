using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly HomeApi _homeApi;

    public HomeViewModel(HomeApi homeApi)
    {
        _homeApi = homeApi;
    }

    public ObservableCollection<HomeSectionWithItems> FeaturedSections { get; } = [];
    public ObservableCollection<HomeSectionWithItems> Sections { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _homeApi.GetSectionsAsync();

            FeaturedSections.Clear();
            Sections.Clear();

            foreach (var section in response.Sections)
            {
                if (section.Featured)
                    FeaturedSections.Add(section);
                else
                    Sections.Add(section);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
