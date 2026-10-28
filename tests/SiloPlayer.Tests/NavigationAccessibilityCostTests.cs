using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NavigationAccessibilityCostTests
{
    [Theory]
    [InlineData("default", "default", false, false)]
    [InlineData(null, null, false, false)]
    [InlineData("large", "default", false, true)]
    [InlineData("x-large", "default", false, true)]
    [InlineData("default", "strong", false, true)]
    // Returning to default must still restore pages scaled earlier in the session.
    [InlineData("default", "default", true, true)]
    public void TreeWalkRunsOnlyWhenTypographyIsOrWasModified(string? scale, string? weight, bool previouslyModified, bool expected)
    {
        Assert.Equal(expected, AccessibilityTypographyPolicy.RequiresTreeWalk(scale, weight, previouslyModified));
    }

    [Fact]
    public void RouteNavigationDoesNotPersistSettingsOrReapplyTheme()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Services", "AccessibilityService.cs").ReplaceLineEndings("\n");
        var applySaved = MethodBody(source, "public void ApplySaved(");

        Assert.DoesNotContain("Save(", applySaved);
        Assert.DoesNotContain("ApplyTheme(", applySaved);
        Assert.DoesNotContain("Apply(settings", applySaved);
        Assert.Contains("AccessibilityTypographyPolicy.RequiresTreeWalk(", applySaved);

        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");
        Assert.Contains("GetRequiredService<AccessibilityService>().ApplySaved(ContentFrame.Content as DependencyObject)", window);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException("Unbalanced method body: " + signature);
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
