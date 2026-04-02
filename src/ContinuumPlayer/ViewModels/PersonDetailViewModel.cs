using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class PersonDetailViewModel : ObservableObject
{
    private readonly PeopleApi _peopleApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;

    public PersonDetailViewModel(PeopleApi peopleApi, CatalogApi catalogApi, AuthService authService)
    {
        _peopleApi = peopleApi;
        _catalogApi = catalogApi;
        _authService = authService;
    }

    [ObservableProperty]
    private Person? _person;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _selectedTypeFilter = "all";

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private int _filmographyTotal;

    public ObservableCollection<MediaItem> Filmography { get; } = [];

    public bool IsAdmin => _authService.CurrentUser?.Role == "admin";

    public string AgeDisplay
    {
        get
        {
            if (Person == null || string.IsNullOrEmpty(Person.BirthDate)) return "";

            if (!DateTime.TryParse(Person.BirthDate, out var birth)) return "";

            var endDate = !string.IsNullOrEmpty(Person.DeathDate) && DateTime.TryParse(Person.DeathDate, out var death)
                ? death
                : DateTime.Today;

            int age = endDate.Year - birth.Year;
            if (endDate.DayOfYear < birth.DayOfYear) age--;

            return age > 0 ? $"({age})" : "";
        }
    }

    public string DatesDisplay
    {
        get
        {
            if (Person == null) return "";

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(Person.BirthDate))
            {
                if (DateTime.TryParse(Person.BirthDate, out var birth))
                    parts.Add($"Born {birth:MMMM d, yyyy}");
                else
                    parts.Add($"Born {Person.BirthDate}");
            }

            if (!string.IsNullOrEmpty(Person.DeathDate))
            {
                if (DateTime.TryParse(Person.DeathDate, out var death))
                    parts.Add($"Died {death:MMMM d, yyyy}");
                else
                    parts.Add($"Died {Person.DeathDate}");
            }

            return string.Join(" \u00B7 ", parts);
        }
    }

    [RelayCommand]
    private async Task LoadAsync(int personId)
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        Filmography.Clear();
        SelectedTypeFilter = "all";

        try
        {
            Person = await _peopleApi.GetPersonAsync(personId);
            OnPropertyChanged(nameof(AgeDisplay));
            OnPropertyChanged(nameof(DatesDisplay));
            OnPropertyChanged(nameof(IsAdmin));

            await LoadFilmographyAsync(personId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load person: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task FilterAsync(string type)
    {
        if (Person == null) return;
        SelectedTypeFilter = type;
        Filmography.Clear();
        await LoadFilmographyAsync(Person.Id);
    }

    [RelayCommand]
    private async Task RefreshMetadataAsync()
    {
        if (Person == null || IsRefreshing) return;

        IsRefreshing = true;
        try
        {
            await _peopleApi.RefreshPersonAsync(Person.Id);

            // Reload person data after refresh
            Person = await _peopleApi.GetPersonAsync(Person.Id);
            OnPropertyChanged(nameof(AgeDisplay));
            OnPropertyChanged(nameof(DatesDisplay));
        }
        catch
        {
            // Refresh failure is non-fatal
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task LoadFilmographyAsync(int personId)
    {
        try
        {
            var type = SelectedTypeFilter == "all" ? null : SelectedTypeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(personId, type);
            Filmography.Clear();
            FilmographyTotal = response.Total;
            foreach (var item in response.Items)
                Filmography.Add(item);
        }
        catch
        {
            // Filmography load failure is non-fatal
        }
    }
}
