using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

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

    [ObservableProperty]
    private bool _isLoadingMoreFilmography;

    public ObservableCollection<MediaItem> Filmography { get; } = [];

    /// <summary>Page size for filmography pagination (matches webui 60).</summary>
    private const int FilmographyPageSize = 60;

    /// <summary>Whether there are more filmography items to fetch.</summary>
    public bool FilmographyHasMore => Filmography.Count < FilmographyTotal;

    public bool IsAdmin => AuthorizationPolicy.IsActingAdmin(_authService);

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

    // B33: personId is a string end-to-end.
    [RelayCommand]
    private async Task LoadAsync(string personId)
    {
        if (IsLoading || string.IsNullOrEmpty(personId)) return;

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

    private async Task LoadFilmographyAsync(string personId)
    {
        try
        {
            var type = SelectedTypeFilter == "all" ? null : SelectedTypeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                personId, type, limit: FilmographyPageSize, offset: 0);
            Filmography.Clear();
            FilmographyTotal = response.Total;
            foreach (var item in response.Items)
                Filmography.Add(item);
            OnPropertyChanged(nameof(FilmographyHasMore));
        }
        catch
        {
            // Filmography load failure is non-fatal
        }
    }

    /// <summary>
    /// Incrementally fetches the next page of filmography items when the user
    /// scrolls near the bottom of the grid. Guarded against concurrent fetches
    /// and no-ops once <see cref="FilmographyHasMore"/> is false.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreFilmographyAsync()
    {
        if (IsLoadingMoreFilmography || !FilmographyHasMore || Person == null) return;

        IsLoadingMoreFilmography = true;
        try
        {
            var type = SelectedTypeFilter == "all" ? null : SelectedTypeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                Person.Id, type, limit: FilmographyPageSize, offset: Filmography.Count);
            foreach (var item in response.Items)
                Filmography.Add(item);
            OnPropertyChanged(nameof(FilmographyHasMore));
        }
        catch { /* non-fatal */ }
        finally
        {
            IsLoadingMoreFilmography = false;
        }
    }
}
