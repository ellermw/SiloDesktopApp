using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

/// <summary>
/// View model for the Calendar page. Mirrors the web UI's
/// <c>web/src/pages/Calendar.tsx</c> and <c>useCalendarWeek</c> hook:
/// a Mon–Sun week view with filter (all/favorites/watchlist), library scope,
/// and prev/next/today navigation.
/// </summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _librariesCts;
    private ApiRequestContext _context;

    public CalendarViewModel(CatalogApi catalogApi, SettingsService settingsService)
    {
        _catalogApi = catalogApi;
        _settingsService = settingsService;
        _context = catalogApi.CaptureContext();
        WeekStart = GetWeekStart(DateTime.Today);
        var stored = settingsService.Load().CalendarPreset;
        Filter = stored == "all" ? "everything" : stored is "following" or "trending" or "everything" ? stored : "following";
    }

    /// <summary>Days with events grouped by date (chronological).</summary>
    public ObservableCollection<CalendarDay> Days { get; } = [];

    /// <summary>All libraries visible to the current profile (for the library filter dropdown).</summary>
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>ISO date (YYYY-MM-DD) of the Monday of the currently viewed week.</summary>
    [ObservableProperty]
    private string _weekStart = "";

    /// <summary>Human-readable label like "Apr 6 – 12, 2026".</summary>
    [ObservableProperty]
    private string _weekRangeLabel = "";

    /// <summary>"following" | "trending" | "everything"</summary>
    [ObservableProperty]
    private string _filter = "following";

    /// <summary>Null = all libraries.</summary>
    [ObservableProperty]
    private int? _libraryId;

    /// <summary>True when the current week has no events (controls empty state).</summary>
    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasLoaded;

    public bool HasCurrentContext => _context == _catalogApi.CaptureContext();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var context = _catalogApi.CaptureContext();
        if (_context != context)
        {
            Interlocked.Exchange(ref _librariesCts, null)?.Cancel();
            _context = context;
            Libraries.Clear(); Days.Clear(); HasLoaded = false; IsEmpty = false; LibraryId = null;
        }
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, owner);
        previous?.Cancel();
        var ct = owner.Token;

        // Capture one immutable query key. Filter/week/library controls may be
        // changed again while the request is in flight; only this owner's
        // response is allowed to update the visible week.
        var requestedWeekStart = WeekStart;
        var requestedFilter = Filter;
        var requestedLibraryId = LibraryId;

        IsLoading = true;
        HasLoaded = false;
        ErrorMessage = null;
        try
        {
            // The optional filter lookup must not delay the independently
            // usable week. It owns its own cancellation and context lifetime.
            if (Libraries.Count == 0 && _librariesCts == null) _ = LoadLibrariesAsync(context);

            var requestedWeekRangeLabel = FormatWeekRangeLabel(requestedWeekStart);
            var end = AddDays(requestedWeekStart, 6);

            var resp = await _catalogApi.GetCalendarAsync(
                requestedWeekStart,
                end,
                requestedFilter,
                requestedLibraryId,
                GetViewerTimezone(),
                ct);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _loadCts), owner) || context != _catalogApi.CaptureContext()) return;

            WeekRangeLabel = requestedWeekRangeLabel;
            Days.Clear();
            foreach (var day in resp.Events ?? [])
                Days.Add(day);

            IsEmpty = Days.Count == 0;
            HasLoaded = true;
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // A newer calendar query owns the surface now.
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(Volatile.Read(ref _loadCts), owner) || context != _catalogApi.CaptureContext()) return;
            ErrorMessage = $"Failed to load calendar: {ex.Message}";
            IsEmpty = false;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, owner), owner))
                IsLoading = false;
            owner.Dispose();
        }
    }

    public void CancelLoad()
    {
        Interlocked.Exchange(ref _loadCts, null)?.Cancel();
        Interlocked.Exchange(ref _librariesCts, null)?.Cancel();
        IsLoading = false;
    }

    private async Task LoadLibrariesAsync(ApiRequestContext context)
    {
        var owner = new CancellationTokenSource(); _librariesCts = owner;
        try
        {
            var libraries = await _catalogApi.GetLibrariesAsync(owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_librariesCts, owner) || context != _catalogApi.CaptureContext()) return;
            foreach (var library in libraries) Libraries.Add(library);
        }
        catch (Exception) { /* Optional lookup; the week remains usable. */ }
        finally
        {
            Interlocked.CompareExchange(ref _librariesCts, null, owner);
            owner.Dispose();
        }
    }

    [RelayCommand]
    private Task PrevWeekAsync()
    {
        WeekStart = AddDays(WeekStart, -7);
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextWeekAsync()
    {
        WeekStart = AddDays(WeekStart, 7);
        return LoadAsync();
    }

    [RelayCommand]
    private Task TodayAsync()
    {
        var today = GetWeekStart(DateTime.Today);
        if (today == WeekStart) return Task.CompletedTask;
        WeekStart = today;
        return LoadAsync();
    }

    public Task SetFilterAsync(string filter)
    {
        filter = filter is "following" or "trending" or "everything" ? filter : "following";
        if (filter == Filter) return Task.CompletedTask;
        Filter = filter;
        var settings = _settingsService.Load();
        settings.CalendarPreset = filter;
        _settingsService.Save(settings);
        return LoadAsync();
    }

    public Task SetLibraryAsync(int? libraryId)
    {
        if (libraryId == LibraryId) return Task.CompletedTask;
        LibraryId = libraryId;
        return LoadAsync();
    }

    // ---------- Week math (mirrors F:\continuum-server\web\src\lib\calendarWeek.ts) ----------

    /// <summary>Returns the Monday (YYYY-MM-DD) for the week containing <paramref name="date"/>.</summary>
    public static string GetWeekStart(DateTime date)
    {
        // JS: 0=Sun, 1=Mon ... diff = day == 0 ? -6 : 1 - day
        int day = (int)date.DayOfWeek; // 0=Sun, 1=Mon ...
        int diff = day == 0 ? -6 : 1 - day;
        return date.AddDays(diff).ToString("yyyy-MM-dd");
    }

    public static string AddDays(string dateStr, int n)
    {
        var d = ParseDate(dateStr).AddDays(n);
        return d.ToString("yyyy-MM-dd");
    }

    public static bool IsToday(string dateStr)
        => dateStr == DateTime.Today.ToString("yyyy-MM-dd");

    public static List<string> GetWeekDays(string weekStart)
    {
        var d = ParseDate(weekStart);
        var list = new List<string>(7);
        for (int i = 0; i < 7; i++) list.Add(d.AddDays(i).ToString("yyyy-MM-dd"));
        return list;
    }

    public static string FormatDayHeading(string dateStr)
    {
        var d = ParseDate(dateStr);
        return $"{d:dddd}, {d:MMMM} {Ordinal(d.Day)}";
    }

    /// <summary>"Apr 6 – 12, 2026" (same month) or "Apr 28 – May 4, 2026" (spanning).</summary>
    public static string FormatWeekRangeLabel(string weekStart)
    {
        var start = ParseDate(weekStart);
        var end = start.AddDays(6);
        if (start.Month == end.Month && start.Year == end.Year)
            return $"{start:MMM} {start.Day} \u2013 {end.Day}, {start.Year}";
        return $"{start:MMM} {start.Day} \u2013 {end:MMM} {end.Day}, {end.Year}";
    }

    public static (string Label, int Day) FormatShortDay(string dateStr)
    {
        var d = ParseDate(dateStr);
        return (d.ToString("ddd"), d.Day);
    }

    private static DateTime ParseDate(string s)
        => DateTime.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string GetViewerTimezone()
    {
        var local = TimeZoneInfo.Local.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local, out var iana) &&
               !string.IsNullOrWhiteSpace(iana)
            ? iana
            : local;
    }

    private static string Ordinal(int n)
    {
        int v = n % 100;
        if (v is >= 11 and <= 13) return $"{n}th";
        return (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
    }
}
