namespace SiloPlayer.Tests;

public sealed class MatchItemDialogParitySourceTests
{
    [Fact]
    public void ManualMatchUsesCurrentSearchAndCandidateEvidenceContract()
    {
        var xaml = Read("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml");
        var code = Read("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml.cs");
        var models = Read("src", "SiloPlayer.Core", "Models", "MediaMaintenance", "MatchModels.cs");

        Assert.Contains("x:Name=\"CurrentTitleText\"", xaml);
        Assert.Contains("x:Name=\"TitleBox\"", xaml);
        Assert.Contains("x:Name=\"YearBox\"", xaml);
        Assert.Contains("x:Name=\"ImdbIdBox\"", xaml);
        Assert.Contains("x:Name=\"TmdbIdBox\"", xaml);
        Assert.Contains("x:Name=\"TvdbIdBox\"", xaml);
        Assert.Contains("x:Name=\"ProviderIdsPanel\"", xaml);
        Assert.Contains("x:Name=\"ApplyMatchButton\"", xaml);
        Assert.Contains("x:Name=\"ResultsSection\"", xaml);

        Assert.Contains("ProviderIds = providerIds", code);
        Assert.Contains("LibraryId = scope.LibraryId", code);
        Assert.Contains("candidate.TitleIsFallback", code);
        Assert.Contains("candidate.MatchedTitle", code);
        Assert.Contains("candidate.MatchScore", code);
        Assert.Contains("candidate.MatchReasons", code);
        Assert.Contains("sources agree", code);
        Assert.Contains("ComputeRootPath", code);
        Assert.Contains("await _maintenanceApi.ApplyMatchAsync", code);
        Assert.Contains("await _maintenanceApi.SearchMatchesAsync", code);
        Assert.Contains("public bool HasAppliedMatch", code);
        Assert.Contains("HasAppliedMatch = true;", code);
        Assert.Contains("ApplyMatchButton.Content = AppStrings.Get(\"web.item.dialog.applying\"", code);
        Assert.Contains("Match could not be applied", code);

        Assert.Contains("public Dictionary<string, string>? ProviderIds", models);
        Assert.Contains("public string? OriginalTitle", models);
        Assert.Contains("public bool TitleIsFallback", models);
        Assert.Contains("public double? MatchScore", models);
        Assert.Contains("public List<string> MatchReasons", models);
    }

    [Fact]
    public void DetailAndCardCallersRefreshOnlyAfterAnAppliedMatch()
    {
        var detail = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var menu = Read("src", "SiloPlayer", "Controls", "MediaItemMenu.cs");

        Assert.Contains("if (dialog.HasAppliedMatch)", detail);
        Assert.Contains("if (dialog.HasAppliedMatch)", menu);
        Assert.DoesNotContain("result == ContentDialogResult.Primary && dialog.SelectedCandidate != null", detail);
    }

    // Official #4646: Enter in any search field submits the same search as the button, under the same disabled conditions.
    // The behaviour itself is proven by the official-small-changes-2 native fixture.
    [Fact]
    public void EnterInEverySearchFieldRunsTheSearchOnlyWhileTheSearchButtonIsEnabled()
    {
        var xaml = Read("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml");
        var code = Read("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml.cs");

        foreach (var name in new[] { "TitleBox", "YearBox", "ImdbIdBox", "TmdbIdBox", "TvdbIdBox", "GenericTitleBox", "GenericYearBox" })
        {
            var start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, name);
            Assert.Contains("KeyDown=\"SearchField_KeyDown\"", xaml[start..xaml.IndexOf("/>", start, StringComparison.Ordinal)]);
        }
        Assert.Contains("providerBox.KeyDown += SearchField_KeyDown;", code);
        Assert.Contains("valueBox.KeyDown += SearchField_KeyDown;", code);
        var enter = code[code.IndexOf("private bool SearchFromEnter()", StringComparison.Ordinal)..];
        enter = enter[..enter.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("if (!SearchButton.IsEnabled) return false;", enter);
        Assert.Contains("SearchFromEnter();", code[code.IndexOf("private void SearchField_KeyDown", StringComparison.Ordinal)..]);
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
