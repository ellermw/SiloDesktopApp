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
        Assert.Contains("CancelImageLoads();", page);
    }

    [Fact]
    public void CalendarPosterWorkIsBoundedAndCanceledWhenCardsAreReplaced()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "CalendarPage.xaml.cs");

        Assert.Contains("new(4)", page);
        Assert.Contains("RenewImageLoadScope();", page);
        Assert.Contains("PosterLoadThrottle.RunAsync", page);
        Assert.Contains("ct.ThrowIfCancellationRequested();", page);
        Assert.DoesNotContain("Task.Run(async ()", page);
    }

    [Fact]
    public void NarrowCalendarScrollMatchesCurrentWebUiHeaderAutoHideContract()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Views", "CalendarPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "CalendarPage.xaml.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("ViewChanged=\"ContentScrollViewer_ViewChanged\"", markup);
        Assert.Contains("Math.Abs(delta) <= 4", page);
        Assert.Contains("delta > 0 && y > 80", page);
        Assert.Contains("SetMobileHeaderHidden(true)", page);
        Assert.Contains("SetMobileHeaderHidden(false)", page);
        Assert.Contains("new System.Numerics.Vector3(0, -96, 0)", shell);
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
