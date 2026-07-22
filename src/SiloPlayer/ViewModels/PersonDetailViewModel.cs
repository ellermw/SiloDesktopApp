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
    private readonly AdminApi _adminApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;

    private CancellationTokenSource? _pageCts;
    private CancellationTokenSource? _filmographyCts;

    public PersonDetailViewModel(PeopleApi peopleApi, AdminApi adminApi, CatalogApi catalogApi, AuthService authService)
    {
        _peopleApi = peopleApi;
        _adminApi = adminApi;
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
    private string? _statusMessage;

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

            if (!string.IsNullOrEmpty(Person.DeathDate)) return "";
            return age > 0 ? $"{age} years old" : "";
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
                var age = ComputeAge(Person.BirthDate, Person.DeathDate);
                if (DateTime.TryParse(Person.DeathDate, out var death))
                    parts.Add($"Died {death:MMMM d, yyyy}{(age > 0 ? $" (age {age})" : "")}");
                else
                    parts.Add($"Died {Person.DeathDate}");
            }

            return string.Join(" \u00B7 ", parts);
        }
    }

    public string BirthDateDisplay
    {
        get
        {
            if (Person == null || string.IsNullOrEmpty(Person.BirthDate)) return "";
            return DateTime.TryParse(Person.BirthDate, out var birth)
                ? $"Born {birth:MMMM d, yyyy}"
                : $"Born {Person.BirthDate}";
        }
    }

    public string DeathDateDisplay
    {
        get
        {
            if (Person == null || string.IsNullOrEmpty(Person.DeathDate)) return "";
            var age = ComputeAge(Person.BirthDate, Person.DeathDate);
            var label = DateTime.TryParse(Person.DeathDate, out var death)
                ? $"Died {death:MMMM d, yyyy}"
                : $"Died {Person.DeathDate}";
            return age > 0 ? $"{label} (age {age})" : label;
        }
    }

    private static int ComputeAge(string? birthDate, string? endDate)
    {
        if (!DateTime.TryParse(birthDate, out var birth) || !DateTime.TryParse(endDate, out var end)) return 0;
        var age = end.Year - birth.Year;
        if (end.DayOfYear < birth.DayOfYear) age--;
        return age;
    }

    // B33: personId is a string end-to-end.
    [RelayCommand]
    private async Task LoadAsync(string personId)
    {
        if (string.IsNullOrEmpty(personId)) return;

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pageCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        CancelFilmographyLoad();

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        SelectedTypeFilter = "all";

        try
        {
            var personTask = _peopleApi.GetPersonAsync(personId, cts.Token);
            var filmographyTask = _catalogApi.GetPersonFilmographyAsync(
                personId, null, limit: FilmographyPageSize, offset: 0, cts.Token);
            await Task.WhenAll(personTask, filmographyTask);
            if (!ReferenceEquals(_pageCts, cts)) return;

            Person = personTask.Result;
            ApplyFilmography(filmographyTask.Result);
            OnPropertyChanged(nameof(AgeDisplay));
            OnPropertyChanged(nameof(DatesDisplay));
            OnPropertyChanged(nameof(BirthDateDisplay));
            OnPropertyChanged(nameof(DeathDateDisplay));
            OnPropertyChanged(nameof(IsAdmin));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_pageCts, cts)) ErrorMessage = $"Failed to load person: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _pageCts, null, cts), cts))
                IsLoading = false;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private async Task FilterAsync(string type)
    {
        if (Person == null) return;
        SelectedTypeFilter = type;
        await LoadFilmographyAsync(Person.Id, type);
    }

    [RelayCommand]
    private async Task RefreshMetadataAsync()
    {
        if (Person == null || IsRefreshing) return;

        IsRefreshing = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            if (IsAdmin)
            {
                Person = await _adminApi.RefreshPersonAsync(Person.Id);
                StatusMessage = "Person metadata refreshed.";
            }
            else
            {
                await _peopleApi.RefreshPersonAsync(Person.Id);
                StatusMessage = "Person refresh queued.";
            }
            OnPropertyChanged(nameof(AgeDisplay));
            OnPropertyChanged(nameof(DatesDisplay));
            OnPropertyChanged(nameof(BirthDateDisplay));
            OnPropertyChanged(nameof(DeathDateDisplay));
        }
        catch (Exception ex) { ErrorMessage = $"Refresh failed: {ex.Message}"; }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task LoadFilmographyAsync(string personId, string typeFilter)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _filmographyCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoadingMoreFilmography = true;
        try
        {
            var type = typeFilter == "all" ? null : typeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                personId, type, limit: FilmographyPageSize, offset: 0, cts.Token);
            if (ReferenceEquals(_filmographyCts, cts)) ApplyFilmography(response);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_filmographyCts, cts)) ErrorMessage = ex.Message; }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _filmographyCts, null, cts), cts))
                IsLoadingMoreFilmography = false;
            cts.Dispose();
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

        var personId = Person.Id;
        var typeFilter = SelectedTypeFilter;
        var offset = Filmography.Count;
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _filmographyCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoadingMoreFilmography = true;
        try
        {
            var type = typeFilter == "all" ? null : typeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                personId, type, limit: FilmographyPageSize, offset: offset, cts.Token);
            if (!ReferenceEquals(_filmographyCts, cts)) return;
            foreach (var item in response.Items)
                Filmography.Add(item);
            FilmographyTotal = response.Total;
            OnPropertyChanged(nameof(FilmographyHasMore));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_filmographyCts, cts)) ErrorMessage = ex.Message; }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _filmographyCts, null, cts), cts))
                IsLoadingMoreFilmography = false;
            cts.Dispose();
        }
    }

    public void Cancel()
    {
        var page = Interlocked.Exchange(ref _pageCts, null);
        page?.Cancel();
        page?.Dispose();
        CancelFilmographyLoad();
        IsLoading = false;
    }

    private void CancelFilmographyLoad()
    {
        var filmography = Interlocked.Exchange(ref _filmographyCts, null);
        filmography?.Cancel();
        filmography?.Dispose();
        IsLoadingMoreFilmography = false;
    }

    private void ApplyFilmography(CatalogResponse response)
    {
        Filmography.Clear();
        FilmographyTotal = response.Total;
        foreach (var item in response.Items) Filmography.Add(item);
        OnPropertyChanged(nameof(FilmographyHasMore));
    }
}
