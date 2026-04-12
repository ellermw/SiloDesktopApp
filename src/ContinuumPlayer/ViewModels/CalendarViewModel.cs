using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels;

/// <summary>
/// View model for the Calendar page. Mirrors the web UI's
/// <c>web/src/pages/Calendar.tsx</c> and <c>useCalendarWeek</c> hook:
/// a Mon–Sun week view with filter (all/favorites/watchlist), library scope,
/// and prev/next/today navigation.
/// </summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;

    public CalendarViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
        WeekStart = GetWeekStart(DateTime.Today);
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

    /// <summary>"all" | "favorites" | "watchlist"</summary>
    [ObservableProperty]
    private string _filter = "all";

    /// <summary>Null = all libraries.</summary>
    [ObservableProperty]
    private int? _libraryId;

    /// <summary>True when the current week has no events (controls empty state).</summary>
    [ObservableProperty]
    private bool _isEmpty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // Populate libraries once (used to decide whether to show the library filter).
            if (Libraries.Count == 0)
            {
                try
                {
                    var libs = await _catalogApi.GetLibrariesAsync();
                    foreach (var l in libs)
                        Libraries.Add(l);
                }
                catch
                {
                    // Non-fatal: the calendar still works without the library filter.
                }
            }

            WeekRangeLabel = FormatWeekRangeLabel(WeekStart);
            var end = AddDays(WeekStart, 6);

            var resp = await _catalogApi.GetCalendarAsync(WeekStart, end, Filter, LibraryId);

            Days.Clear();
            foreach (var day in resp.Events ?? [])
                Days.Add(day);

            IsEmpty = Days.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load calendar: {ex.Message}";
            IsEmpty = false;
        }
        finally
        {
            IsLoading = false;
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
        if (filter == Filter) return Task.CompletedTask;
        Filter = filter;
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
