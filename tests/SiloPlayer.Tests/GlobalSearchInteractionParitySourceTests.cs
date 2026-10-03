namespace SiloPlayer.Tests;

public sealed class GlobalSearchInteractionParitySourceTests
{
    [Fact]
    public void PreviewAndRequestResultsUseFocusableActions()
    {
        var source = Read("src", "SiloPlayer", "Controls", "GlobalSearchDialog.xaml.cs");

        Assert.Contains("private FrameworkElement BuildResultRow", source);
        Assert.Contains("item.Type is not (\"movie\" or \"episode\")", source);
        Assert.Contains("AutomationProperties.SetName(play,", source);
        Assert.Contains("PlayAsync(item.ContentId)", source);
        Assert.Contains("private Button BuildRequestRow", source);
        Assert.Contains("AutomationProperties.SetName(row,", source);
        Assert.Contains("row.Click += (_, _) => PickResult(index)", source);
        Assert.DoesNotContain("row.Tapped += (_, _) => PickResult(index)", source);
    }

    [Fact]
    public void KeyboardSelectionTracksAllResultGroupsByIdentity()
    {
        var source = Read("src", "SiloPlayer", "Controls", "GlobalSearchDialog.xaml.cs");

        Assert.Contains("Button { Tag: int resultIndex, Content: Border surface }", source);
        Assert.Contains("resultIndex == _selection.Index", source);
        Assert.Contains("_selection.Replace", source);
        Assert.Contains("_peopleResults.Select", source);
        Assert.Contains("_requestResults.Select", source);
        Assert.DoesNotContain("ResultsPanel.Children[_selectedIndex]", source);
    }

    [Fact]
    public void LocalPreviewResultsAreNotBlockedByOptionalDiscoveryAndClosingCancelsWork()
    {
        var source = Read("src", "SiloPlayer", "Controls", "GlobalSearchDialog.xaml.cs");

        Assert.DoesNotContain("await Task.WhenAll(catalogTask, requestsTask)", source);
        Assert.Contains("var response = await catalogTask;", source);
        Assert.Contains("_ = PublishRequestResultsAsync(requestsTask, query, cts);", source);
        Assert.True(
            source.IndexOf("Render();", source.IndexOf("var response = await catalogTask;", StringComparison.Ordinal), StringComparison.Ordinal)
            < source.IndexOf("_ = PublishRequestResultsAsync(requestsTask, query, cts);", StringComparison.Ordinal));
        Assert.Contains("this.Closed += OnClosed;", source);
        Assert.Contains("_debounceTimer?.Stop();", source);
        Assert.Contains("Interlocked.Exchange(ref _searchCts, null)", source);
        Assert.Contains("owner.Cancel();", source);
        Assert.Contains("owner.Dispose();", source);
    }

    private static string Read(params string[] parts)
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
