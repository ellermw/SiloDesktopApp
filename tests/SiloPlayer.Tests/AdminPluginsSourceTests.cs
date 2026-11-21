using System.Text.RegularExpressions;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

// Guards over the Plugins family's own sources: message keys that exist, no legacy API, one place that opens links.
public sealed class AdminPluginsSourceTests
{
    private static IEnumerable<string> FamilyFiles()
    {
        var root = TestRepository.Root;
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("AdminPlugin", StringComparison.Ordinal) || name == "SiloApiClient.AdminPlugins.cs"))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).ToArray();
    }

    [Fact]
    public void TheFamilyHasItsFiles() => Assert.True(FamilyFiles().Count() >= 14);

    [Fact]
    public void EveryAdminMessageKeyTheFamilyNamesExistsInItsOwnTable()
    {
        var pattern = new Regex("\"(web\\.admin\\.(?:plugins|errors|common|tasks)\\.[A-Za-z0-9_.]+)\"", RegexOptions.None, TimeSpan.FromSeconds(2));
        var missing = new List<string>();
        foreach (var file in FamilyFiles().Where(path => !path.EndsWith("AdminPluginsText.cs", StringComparison.Ordinal)))
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
                if (!AdminPluginsText.Has(match.Groups[1].Value) && !AdminText.Has(match.Groups[1].Value)) missing.Add(Path.GetFileName(file) + ": " + match.Groups[1].Value);
        Assert.Empty(missing);
    }

    [Fact]
    public void NoLegacyApiAndNoPluginTextIsEverParsedAsMarkup()
    {
        foreach (var file in FamilyFiles())
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("/api/v1", source);
            Assert.DoesNotContain("XamlReader.Load", source);
            Assert.DoesNotContain("HtmlText", source);
        }
    }

    [Fact]
    public void LinksOpenOnlyThroughTheLauncherSeamAndTheSafeLinkPolicy()
    {
        var offenders = FamilyFiles().Where(path => File.ReadAllText(path).Contains("LaunchUriAsync", StringComparison.Ordinal)).Select(Path.GetFileName).ToArray();
        Assert.Equal(["AdminPluginsPage.cs"], offenders);
        var ui = File.ReadAllText(FamilyFiles().Single(path => path.EndsWith("AdminPluginsUi.cs", StringComparison.Ordinal)));
        Assert.DoesNotContain("NavigateUri", ui);
    }

    [Fact]
    public void NothingTheFamilyHandlesIsEverLoggedOrWrittenToDisk()
    {
        foreach (var file in FamilyFiles().Where(path => path.Contains(Path.DirectorySeparatorChar + "Views" + Path.DirectorySeparatorChar) || path.Contains("Controller") || path.Contains("Draft")))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("LocalLog", source);
            Assert.DoesNotContain("File.Write", source);
            Assert.DoesNotContain("File.Create", source);
            Assert.DoesNotContain("Debug.Write", source);
        }
    }

    [Fact]
    public void TheUploadHasNoDeadlineOverTheWholeFileAndEveryRequestOfItCarriesItsOwnLimit()
    {
        var upload = File.ReadAllText(FamilyFiles().Single(path => path.EndsWith("AdminPluginsApi.Uploads.cs", StringComparison.Ordinal)));
        Assert.DoesNotContain("CancelAfter", upload);
        Assert.DoesNotContain("FromMinutes", upload);
        // Session, chunk, completion and the small single request each name the limit; the cleanup DELETE has its own short bound.
        Assert.Equal(4, Regex.Matches(upload, "requestLimit: UploadRequestLimit").Count);
        var transport = File.ReadAllText(FamilyFiles().Single(path => path.EndsWith("SiloApiClient.AdminPlugins.cs", StringComparison.Ordinal)));
        Assert.Contains("deadline.CancelAfter(requestLimit ??", transport);
    }

    [Fact]
    public void EveryPluginTextTheViewsShowGoesThroughTheOneTextGate()
    {
        // The raw fields that carry third-party text are read in the Core presentation helpers, not in a view.
        foreach (var file in FamilyFiles().Where(path => path.Contains(Path.DirectorySeparatorChar + "Views" + Path.DirectorySeparatorChar)))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("PublisherName?.Trim()", source); Assert.DoesNotContain("presentation?.DisplayName", source);
            Assert.DoesNotContain("Text(entry.Version", source); Assert.DoesNotContain("Text(repository.DisplayName", source);
        }
    }

    [Fact]
    public void TheSharedSchemaFormIsUntouchedByThisFamily()
    {
        var shared = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "ProviderSchemaForm.cs"));
        Assert.DoesNotContain("AdminPlugin", shared);
    }
}
