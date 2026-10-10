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
    private readonly SiloApiClient _apiClient;

    private CancellationTokenSource? _pageCts;
    private CancellationTokenSource? _filmographyCts;
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _refreshRequestCts;
    private string? _autoRefreshRequestedPersonId;
    private bool _filmographyPagingBlocked;

    public PersonDetailViewModel(PeopleApi peopleApi, CatalogApi catalogApi, SiloApiClient apiClient)
    {
        _peopleApi = peopleApi;
        _catalogApi = catalogApi;
        _apiClient = apiClient;
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
    private int _filmographyTotal;

    [ObservableProperty]
    private bool _isLoadingMoreFilmography;

    [ObservableProperty]
    private bool _isLoadingFilmography;

    [ObservableProperty]
    private bool _isRefreshing;

    public ObservableCollection<MediaItem> Filmography { get; } = [];
    public bool ActingAdmin { get; set; }
    public Func<DateTime, string> FormatPersonDate { get; set; } = date => date.ToString("MMM d, yyyy");

    /// <summary>Page size for filmography pagination (matches webui 60).</summary>
    private const int FilmographyPageSize = 60;

    /// <summary>Whether there are more filmography items to fetch.</summary>
    public bool FilmographyHasMore => !_filmographyPagingBlocked && Filmography.Count < FilmographyTotal;

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
            if (endDate.Month < birth.Month || (endDate.Month == birth.Month && endDate.Day < birth.Day)) age--;

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
                    parts.Add($"Born {FormatPersonDate(birth)}");
                else
                    parts.Add($"Born {Person.BirthDate}");
            }

            if (!string.IsNullOrEmpty(Person.DeathDate))
            {
                var age = ComputeAge(Person.BirthDate, Person.DeathDate);
                if (DateTime.TryParse(Person.DeathDate, out var death))
                    parts.Add($"Died {FormatPersonDate(death)}{(age > 0 ? $" (age {age})" : "")}");
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
                ? $"Born {FormatPersonDate(birth)}"
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
                ? $"Died {FormatPersonDate(death)}"
                : $"Died {Person.DeathDate}";
            return age > 0 ? $"{label} (age {age})" : label;
        }
    }

    private static int ComputeAge(string? birthDate, string? endDate)
    {
        if (!DateTime.TryParse(birthDate, out var birth) || !DateTime.TryParse(endDate, out var end)) return 0;
        var age = end.Year - birth.Year;
        if (end.Month < birth.Month || (end.Month == birth.Month && end.Day < birth.Day)) age--;
        return age;
    }

    // B33: personId is a string end-to-end.
    [RelayCommand]
    private async Task LoadAsync(string personId)
    {
        if (string.IsNullOrEmpty(personId)) return;

        var cts = new CancellationTokenSource();
        var context = _apiClient.CaptureContext();
        var previous = Interlocked.Exchange(ref _pageCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        CancelFilmographyLoad();
        CancelRefresh();
        CancelRefreshRequest();

        IsLoading = true;
        ErrorMessage = null;
        SelectedTypeFilter = "all";
        try
        {
            var personTask = _peopleApi.GetPersonAsync(personId, cts.Token);
            // Filmography owns its loading/error state independently. Its
            // handler observes failures even if the person request fails first.
            var filmographyTask = LoadFilmographyAsync(personId, "all");
            var person = await personTask;
            if (!ReferenceEquals(_pageCts, cts) || !_apiClient.IsCurrentContext(context)) return;
            IsLoading = false;
            await PublishReadPersonAsync(person, cts.Token);
            await filmographyTask;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_pageCts, cts) || !_apiClient.IsCurrentContext(context)) return;
            CancelFilmographyLoad();
            if (ex is ApiException { StatusCode: 404 })
            {
                Person = null;
                Filmography.Clear(); FilmographyTotal = 0;
                ErrorMessage = "Person not found.";
            }
            else ErrorMessage = $"Failed to load person: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _pageCts, null, cts), cts))
                IsLoading = false;
            cts.Dispose();
        }
    }

    private void PublishPerson(Person person)
    {
        CancelRefresh();
        Person = person;
        OnPropertyChanged(nameof(AgeDisplay)); OnPropertyChanged(nameof(DatesDisplay));
        OnPropertyChanged(nameof(BirthDateDisplay)); OnPropertyChanged(nameof(DeathDateDisplay));
        var refreshCts = new CancellationTokenSource(); _refreshCts = refreshCts;
        _ = ObservePersonRefreshAsync(person, refreshCts);
    }

    private async Task PublishReadPersonAsync(Person person, CancellationToken ct)
    {
        PublishPerson(person);
        if (_autoRefreshRequestedPersonId == person.Id ||
            (!string.IsNullOrEmpty(person.Bio) && !string.IsNullOrEmpty(person.PhotoUrl) && !string.IsNullOrEmpty(person.BirthDate))) return;
        _autoRefreshRequestedPersonId = person.Id;
        var context = _apiClient.CaptureContext();
        try { await RefreshAsync(ActingAdmin, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && _apiClient.IsCurrentContext(context) && Person?.Id == person.Id)
                ErrorMessage = $"Could not refresh person: {ex.Message}";
        }
    }

    private async Task ObservePersonRefreshAsync(Person initial, CancellationTokenSource cts)
    {
        try
        {
            await foreach (var person in _peopleApi.ObservePersonRefreshAsync(initial, cts.Token, queueIfIncomplete: false))
            {
                if (!ReferenceEquals(_refreshCts, cts) || cts.IsCancellationRequested) return;
                Person = person;
                OnPropertyChanged(nameof(AgeDisplay));
                OnPropertyChanged(nameof(DatesDisplay));
                OnPropertyChanged(nameof(BirthDateDisplay));
                OnPropertyChanged(nameof(DeathDateDisplay));
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.CompareExchange(ref _refreshCts, null, cts);
            cts.Dispose();
        }
    }

    private void CancelRefresh()
        => Interlocked.Exchange(ref _refreshCts, null)?.Cancel();

    public async Task RefreshAsync(bool actingAdmin, CancellationToken ct = default)
    {
        if (Person is not { } displayed) return;
        var context = _apiClient.CaptureContext();
        var owner = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = Interlocked.Exchange(ref _refreshRequestCts, owner);
        previous?.Cancel();
        IsRefreshing = true;
        try
        {
            if (actingAdmin)
            {
                var refreshed = await _peopleApi.AdminRefreshPersonAsync(displayed.Id, owner.Token);
                if (!ReferenceEquals(_refreshRequestCts, owner) || owner.IsCancellationRequested || !_apiClient.IsCurrentContext(context) || Person?.Id != displayed.Id) return;
                PublishPerson(refreshed);
            }
            else
            {
                await _peopleApi.RefreshPersonAsync(displayed.Id, owner.Token);
                if (!ReferenceEquals(_refreshRequestCts, owner) || owner.IsCancellationRequested || !_apiClient.IsCurrentContext(context) || Person?.Id != displayed.Id) return;
                // The queued response contains no replacement metadata. Keep
                // the displayed person and follow provider/photo work by reads.
                CancelRefresh();
                var observation = new CancellationTokenSource(); _refreshCts = observation;
                _ = ObservePersonRefreshAsync(displayed, observation);
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshRequestCts, null, owner), owner)) IsRefreshing = false;
            owner.Dispose();
        }
    }

    private void CancelRefreshRequest()
    {
        Interlocked.Exchange(ref _refreshRequestCts, null)?.Cancel();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task FilterAsync(string type)
    {
        if (Person == null) return;
        SelectedTypeFilter = type;
        await LoadFilmographyAsync(Person.Id, type);
    }

    private async Task LoadFilmographyAsync(string personId, string typeFilter)
    {
        var cts = new CancellationTokenSource();
        var context = _apiClient.CaptureContext();
        var previous = Interlocked.Exchange(ref _filmographyCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoadingFilmography = true;
        _filmographyPagingBlocked = false;
        ErrorMessage = null;
        Filmography.Clear(); FilmographyTotal = 0;
        try
        {
            var type = typeFilter == "all" ? null : typeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                personId, type, limit: FilmographyPageSize, offset: 0, cts.Token);
            if (ReferenceEquals(_filmographyCts, cts) && _apiClient.IsCurrentContext(context)) ApplyFilmography(response);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { if (ReferenceEquals(_filmographyCts, cts) && _apiClient.IsCurrentContext(context)) ErrorMessage = $"Could not load filmography: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _filmographyCts, null, cts), cts))
                IsLoadingFilmography = false;
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
        if (IsLoadingFilmography || IsLoadingMoreFilmography || !FilmographyHasMore || Person == null) return;

        var personId = Person.Id;
        var typeFilter = SelectedTypeFilter;
        var offset = Filmography.Count;
        var cts = new CancellationTokenSource();
        var context = _apiClient.CaptureContext();
        var previous = Interlocked.Exchange(ref _filmographyCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        IsLoadingMoreFilmography = true;
        try
        {
            var type = typeFilter == "all" ? null : typeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                personId, type, limit: FilmographyPageSize, offset: offset, cts.Token);
            if (!ReferenceEquals(_filmographyCts, cts) || !_apiClient.IsCurrentContext(context)) return;
            var ids = Filmography.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in response.Items)
                if (ids.Add(item.ContentId)) Filmography.Add(item);
            FilmographyTotal = response.Total;
            if (Filmography.Count == offset && Filmography.Count < FilmographyTotal)
            {
                _filmographyPagingBlocked = true;
                ErrorMessage = "Could not load more filmography. Reload to try again.";
            }
            OnPropertyChanged(nameof(FilmographyHasMore));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_filmographyCts, cts) && _apiClient.IsCurrentContext(context))
            {
                _filmographyPagingBlocked = true;
                ErrorMessage = $"Could not load more filmography: {ex.Message}";
                OnPropertyChanged(nameof(FilmographyHasMore));
            }
        }
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
        CancelRefresh();
        CancelRefreshRequest();
        _autoRefreshRequestedPersonId = null;
        IsLoading = false;
    }

    private void CancelFilmographyLoad()
    {
        var filmography = Interlocked.Exchange(ref _filmographyCts, null);
        filmography?.Cancel();
        filmography?.Dispose();
        IsLoadingMoreFilmography = false;
        IsLoadingFilmography = false;
    }

    private void ApplyFilmography(CatalogResponse response)
    {
        Filmography.Clear();
        FilmographyTotal = response.Total;
        foreach (var item in response.Items) Filmography.Add(item);
        OnPropertyChanged(nameof(FilmographyHasMore));
    }
}
