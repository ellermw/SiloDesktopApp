namespace SiloPlayer.Tests;

public sealed class CalendarCurrentParitySourceTests
{
    [Fact]
    public void LatestWeekFilterOrLibrarySelectionOwnsTheVisibleCalendar()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "CalendarViewModel.cs");

        Assert.Contains("Interlocked.Exchange(ref _loadCts, owner)", source);
        Assert.Contains("previous?.Cancel()", source);
        Assert.Contains("var requestedWeekStart = WeekStart", source);
        Assert.Contains("var requestedFilter = Filter", source);
        Assert.Contains("var requestedLibraryId = LibraryId", source);
        Assert.Contains("ReferenceEquals(Volatile.Read(ref _loadCts), owner)", source);
        Assert.DoesNotContain("if (IsLoading) return", source);
    }

    [Fact]
    public void LeavingTheCachedCalendarCancelsItsNetworkWork()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "CalendarPage.xaml.cs");
        Assert.Contains("ViewModel.CancelLoad();", page);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
