namespace SiloPlayer.Tests;

public sealed class MatchItemDialogParitySourceTests
{
    [Fact]
    public void ManualMatchUsesCurrentSearchAndCandidateEvidenceContract()
    {
        var xaml = Read("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml");
        var code = Read("src", "SiloPlayer", "Controls", "MatchItemDialog.xaml.cs");
        var models = Read("src", "SiloPlayer.Core", "Models", "Admin", "MatchSearch.cs");

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
        Assert.Contains("LibraryId = _libraryId", code);
        Assert.Contains("candidate.TitleIsFallback", code);
        Assert.Contains("candidate.MatchedTitle", code);
        Assert.Contains("candidate.MatchScore", code);
        Assert.Contains("candidate.MatchReasons", code);
        Assert.Contains("sources agree", code);
        Assert.Contains("ComputeRootPath", code);
        Assert.Contains("await _adminApi.MatchApplyAsync", code);
        Assert.Contains("ApplyMatchButton.Content = \"Applying...\"", code);
        Assert.Contains("Match could not be applied", code);

        Assert.Contains("public Dictionary<string, string>? ProviderIds", models);
        Assert.Contains("public string? OriginalTitle", models);
        Assert.Contains("public bool TitleIsFallback", models);
        Assert.Contains("public double? MatchScore", models);
        Assert.Contains("public List<string> MatchReasons", models);
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
