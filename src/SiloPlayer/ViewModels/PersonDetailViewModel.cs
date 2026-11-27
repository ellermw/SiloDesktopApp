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
    private bool _retainedRefreshPending;
    private KeptRefresh? _keptRefresh;
    private int _visitGeneration;

    private sealed class KeptRefresh(CancellationToken token, bool appendRequested)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool AppendRequested { get; set; } = appendRequested;
        public bool FilterRequested { get; set; }
    }

    public bool FilmographyWindowLoaded { get; private set; }

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
    private DetailFailureKind _failure;

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
    public Func<DateTime, string> FormatPersonDate { get; set; } = date => date.ToString("D", SiloPlayer.Core.Localization.AppStrings.FormatCulture);

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
            return age > 0 ? SiloPlayer.Core.Localization.AppStrings.Get("web.person.age", new Dictionary<string, object?> { ["age"] = age }) : "";
        }
    }

    public string DatesDisplay => string.Join(" · ", new[] { BirthDateDisplay, DeathDateDisplay }.Where(value => value.Length > 0));

    public string BirthDateDisplay
    {
        get
        {
            if (Person == null || string.IsNullOrEmpty(Person.BirthDate)) return "";
            return SiloPlayer.Core.Localization.AppStrings.Get("web.person.born", new Dictionary<string, object?>
            { ["date"] = DateTime.TryParse(Person.BirthDate, out var birth) ? FormatPersonDate(birth) : Person.BirthDate });
        }
    }

    public string DeathDateDisplay
    {
        get
        {
            if (Person == null || string.IsNullOrEmpty(Person.DeathDate)) return "";
            var age = ComputeAge(Person.BirthDate, Person.DeathDate);
            var date = DateTime.TryParse(Person.DeathDate, out var death) ? FormatPersonDate(death) : Person.DeathDate;
            return age > 0 ? SiloPlayer.Core.Localization.AppStrings.Get("web.person.died", new Dictionary<string, object?>
            { ["date"] = date, ["age"] = age })
                : SiloPlayer.Core.Localization.AppStrings.Get("web.item.editPerson.deathDate") + ": " + date;
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
        Person = null;
        Failure = DetailFailureKind.None;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        SelectedTypeFilter = "all";
        try
        {
            var personTask = _peopleApi.GetPersonAsync(personId, cts.Token);
            // Filmography owns its loading/error state independently. Its
            // handler observes failures even if the person request fails first.
            var filmographyTask = LoadFilmographyAsync(personId, "all");
            var person = await personTask;
            if (!ReferenceEquals(_pageCts, cts) || cts.IsCancellationRequested || !_apiClient.IsCurrentContext(context)) return;
            IsLoading = false;
            await PublishReadPersonAsync(person, cts.Token);
            await filmographyTask;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_pageCts, cts) || !_apiClient.IsCurrentContext(context)) return;
            CancelFilmographyLoad();
            if (Unavailable(ex)) ShowUnavailable(ex);
            else SetLocalizedMessage(nameof(ErrorMessage), () => $"Failed to load person: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
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
        Failure = DetailFailureKind.None;
        Person = person;
        OnPropertyChanged(nameof(AgeDisplay)); OnPropertyChanged(nameof(DatesDisplay));
        OnPropertyChanged(nameof(BirthDateDisplay)); OnPropertyChanged(nameof(DeathDateDisplay));
        ResumeMetadataObservation();
    }

    public void ResumeMetadataObservation()
    {
        if (Person is not { } person || _refreshCts != null) return;
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
                SetLocalizedMessage(nameof(ErrorMessage), () => $"Could not refresh person: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
        }
    }

    private async Task ObservePersonRefreshAsync(Person initial, CancellationTokenSource cts)
    {
        var context = _apiClient.CaptureContext();
        try
        {
            await foreach (var person in _peopleApi.ObservePersonRefreshAsync(initial, cts.Token, queueIfIncomplete: false, failOnUnavailable: true))
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
        catch (Exception ex) when (Unavailable(ex))
        {
            if (ReferenceEquals(_refreshCts, cts) && !cts.IsCancellationRequested && _apiClient.IsCurrentContext(context))
            {
                _pageCts?.Cancel();
                CancelFilmographyLoad();
                ShowUnavailable(ex);
            }
        }
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

    /// <summary>Rereads the mounted window. Only a committed snapshot renews freshness.</summary>
    public async Task<long?> RefreshInPlaceAsync(Func<CancellationToken, Task>? waitForWrites = null,
        Func<long>? mutationRevision = null, bool resumePaging = false)
    {
        if (Person is not { } shown) return null;
        var personId = shown.Id;
        var typeFilter = SelectedTypeFilter;
        var context = _apiClient.CaptureContext();
        resumePaging |= IsLoadingMoreFilmography;
        CancelFilmographyLoad();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _filmographyCts = cts;
        var owner = _keptRefresh = new KeptRefresh(token, resumePaging);
        _retainedRefreshPending = true;
        var committed = false;
        bool Current() => ReferenceEquals(_filmographyCts, cts) && !token.IsCancellationRequested
            && _apiClient.IsCurrentContext(context) && Person?.Id == personId && SelectedTypeFilter == typeFilter;

        async Task<T> ReadAsync<T>(Func<Task<T>> read)
        {
            try { return await read(); }
            catch (Exception ex) when (Unavailable(ex))
            {
                // A denial from either endpoint clears the window without waiting for the sibling.
                if (Current()) { ShowUnavailable(ex); cts.Cancel(); }
                throw;
            }
        }

        async Task<(List<MediaItem> Items, int Total)> ReadFilmographyAsync()
        {
            var type = typeFilter == "all" ? null : typeFilter;
            var target = Math.Max(FilmographyPageSize, Filmography.Count);
            var items = new List<MediaItem>();
            var total = 0;
            for (var offset = 0; offset < target; offset += FilmographyPageSize)
            {
                if (!Current()) throw new OperationCanceledException();
                var page = await _catalogApi.GetPersonFilmographyAsync(personId, type, limit: FilmographyPageSize, offset: offset, token);
                total = page.Total;
                items.AddRange(page.Items);
                if (page.Items.Count < FilmographyPageSize || items.Count >= total) break;
            }
            return (items, total);
        }

        try
        {
            while (Current())
            {
                var revision = mutationRevision?.Invoke() ?? 0;
                if (waitForWrites != null) await waitForWrites(token);
                if (!Current()) return null;
                if (revision != (mutationRevision?.Invoke() ?? 0)) continue;
                var personTask = ReadAsync(() => _peopleApi.GetPersonAsync(personId, token));
                var filmographyTask = ReadAsync(ReadFilmographyAsync);
                await Task.WhenAll(personTask, filmographyTask);
                if (!Current()) return null;
                if (revision != (mutationRevision?.Invoke() ?? 0)) continue;
                var (items, total) = await filmographyTask;
                PublishPerson(await personTask);
                MediaItemCollectionReconciler.Apply(Filmography, items.DistinctBy(item => item.ContentId, StringComparer.Ordinal).ToList());
                FilmographyTotal = total;
                FilmographyWindowLoaded = true;
                _filmographyPagingBlocked = _retainedRefreshPending = false;
                SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
                OnPropertyChanged(nameof(FilmographyHasMore));
                committed = true;
                return revision;
            }
            return null;
        }
        catch (Exception) { return null; } // Transient failure or cancellation does not renew freshness.
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _filmographyCts, null, cts), cts))
            {
                _keptRefresh = null;
                if (committed && owner.AppendRequested && !owner.FilterRequested) _ = LoadMoreFilmographyAsync();
            }
            owner.Settled.TrySetResult();
        }
    }

    private static bool Unavailable(Exception ex) =>
        DetailFailurePolicy.Classify(ex) is DetailFailureKind.AccessDenied or DetailFailureKind.NotFound;

    private void ShowUnavailable(Exception ex)
    {
        CancelRefresh();
        CancelRefreshRequest();
        Failure = DetailFailurePolicy.Classify(ex);
        IsLoading = false;
        Person = null;
        FilmographyWindowLoaded = false;
        Filmography.Clear(); FilmographyTotal = 0;
        OnPropertyChanged(nameof(FilmographyHasMore));
        SetLocalizedMessage(nameof(ErrorMessage), () => ex is ApiException problem
            ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message, value => ErrorMessage = value);
    }

    private void CancelRefreshRequest()
    {
        Interlocked.Exchange(ref _refreshRequestCts, null)?.Cancel();
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task FilterAsync(string type)
    {
        if (Person is not { } person) return;
        if (_keptRefresh is { } refresh)
        {
            var context = _apiClient.CaptureContext();
            var visit = _visitGeneration;
            refresh.FilterRequested = true;
            try { await refresh.Settled.Task.WaitAsync(refresh.Token); }
            catch (OperationCanceledException) { return; }
            if (visit != _visitGeneration || refresh.Token.IsCancellationRequested || Person?.Id != person.Id || !_apiClient.IsCurrentContext(context)) return;
        }
        SelectedTypeFilter = type;
        await LoadFilmographyAsync(person.Id, type);
    }

    private async Task LoadFilmographyAsync(string personId, string typeFilter)
    {
        var cts = new CancellationTokenSource();
        var context = _apiClient.CaptureContext();
        CancelFilmographyLoad();
        _filmographyCts = cts;
        FilmographyWindowLoaded = false;
        IsLoadingFilmography = true;
        _filmographyPagingBlocked = false;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        Filmography.Clear(); FilmographyTotal = 0;
        try
        {
            var type = typeFilter == "all" ? null : typeFilter;
            var response = await _catalogApi.GetPersonFilmographyAsync(
                personId, type, limit: FilmographyPageSize, offset: 0, cts.Token);
            if (ReferenceEquals(_filmographyCts, cts) && _apiClient.IsCurrentContext(context)) ApplyFilmography(response);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_filmographyCts, cts) || !_apiClient.IsCurrentContext(context)) return;
            if (Unavailable(ex)) { _pageCts?.Cancel(); ShowUnavailable(ex); }
            else SetLocalizedMessage(nameof(ErrorMessage), () => $"Could not load filmography: {(ex is ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
        }
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
        if (_keptRefresh is { } refresh) { refresh.AppendRequested = true; return; }
        if (_retainedRefreshPending || IsLoadingFilmography || IsLoadingMoreFilmography || !FilmographyHasMore || Person == null) return;

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
                SetLocalizedMessage(nameof(ErrorMessage), () => "Could not load more filmography. Reload to try again.", value => ErrorMessage = value);
            }
            OnPropertyChanged(nameof(FilmographyHasMore));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_filmographyCts, cts) && _apiClient.IsCurrentContext(context))
            {
                if (Unavailable(ex)) { ShowUnavailable(ex); return; }
                _filmographyPagingBlocked = true;
                SetLocalizedMessage(nameof(ErrorMessage), () => $"Could not load more filmography: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
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
        ++_visitGeneration;
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
        _keptRefresh = null;
        _retainedRefreshPending = false;
        IsLoadingMoreFilmography = false;
        IsLoadingFilmography = false;
    }

    private void ApplyFilmography(CatalogResponse response)
    {
        Filmography.Clear();
        FilmographyTotal = response.Total;
        foreach (var item in response.Items) Filmography.Add(item);
        FilmographyWindowLoaded = true;
        OnPropertyChanged(nameof(FilmographyHasMore));
    }
}
