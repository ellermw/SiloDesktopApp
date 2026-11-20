using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Prepared Files must be reachable through the production Admin registration, shell resources and surface policy.</summary>
public sealed class AdminPreparedFilesRegistrationTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static string[] FamilySources() => Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*PreparedFiles*", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(Root(), "src"), "AdminPreparationPresentation.cs", SearchOption.AllDirectories))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .ToArray();

    [Fact]
    public void RegistrationOwnsOnlyThePreparedFilesRouteAndIsAddedToTheApp()
    {
        var registration = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/Views/Admin/AdminPreparedFilesRegistration.cs"));
        Assert.Contains("AddAdminPreparedFiles", registration, StringComparison.Ordinal);
        Assert.Contains("new AdminPageRegistration(AdminRoute.PreparedFiles, typeof(AdminPreparedFilesPage))", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("AdminRoute.DownloadSettings", registration, StringComparison.Ordinal);
        Assert.Contains("services.AddAdminPreparedFiles();", File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer/App.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRouteIsAnOfficialLeafNotListedAsUnsupportedAndItsResourcesArePolicyAndInstallerListed()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/downloads?tab=files", out var resolution));
        Assert.Equal(AdminRoute.PreparedFiles, resolution.Request?.Route);
        Assert.NotEqual(AdminRouteResolutionKind.Unsupported, resolution.Kind);
        Assert.Contains("'Views/Admin/AdminPreparedFilesPage.xbf'", File.ReadAllText(Path.Combine(Root(), "installer/build.ps1")), StringComparison.Ordinal);
        var policy = File.ReadAllText(Path.Combine(Root(), "docs/parity/admin-monitor-surfaces.json"));
        foreach (var path in FamilySources().Select(p => Path.GetRelativePath(Root(), p).Replace('\\', '/')))
            Assert.Contains(path, policy, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNativeFixtureIsDispatchedWithItsOwnCompletionLine()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "tests/SiloPlayer.NativeRegressionTests/Program.cs"));
        var at = program.IndexOf("\"admin-prepared-files\")", StringComparison.Ordinal);
        Assert.True(at > 0);
        var next = program.IndexOf("SILO_NATIVE_TEST_ONLY", at, StringComparison.Ordinal);
        var block = program[at..(next < 0 ? program.Length : next)];
        Assert.Contains("AdminPreparedFilesNativeFixture.RunAsync(_window)", block, StringComparison.Ordinal);
        Assert.Contains("Program.Log(\"PASS: native regression run completed.\"); Exit(); return;", block, StringComparison.Ordinal);
    }

    [Fact]
    public void FamilySourceNeverTouchesLocalFilesLogsPageDataOrUsesARetiredApi()
    {
        foreach (var path in FamilySources().Where(p => p.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(path);
            var name = Path.GetFileName(path);
            Assert.DoesNotContain("/api/v1", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.IO.File", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Delete", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.Delete", source, StringComparison.Ordinal);
            Assert.False(source.Contains("LocalLog", StringComparison.Ordinal) || source.Contains("Debug.Write", StringComparison.Ordinal) || source.Contains("Console.Write", StringComparison.Ordinal),
                name + " must not write page data to a log");
            Assert.DoesNotContain("ToLocalTime().ToString", source, StringComparison.Ordinal);
            Assert.DoesNotContain("LocalDateTime.ToString", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ActionsSendOnceAndNeverRetryAuthenticationOrReplayAWrite()
    {
        var client = File.ReadAllText(Path.Combine(Root(), "src/SiloPlayer.Core/Api/SiloApiClient.AdminPreparedFiles.cs"));
        Assert.Contains("allowTokenRefresh: method == HttpMethod.Get", client, StringComparison.Ordinal);
    }
    [Fact]
    public void EveryMessageKeyTheFamilyReferencesExistsInThePinnedCatalog()
    {
        var prefixes = new Dictionary<string, string>
        {
            ["PrepKey"] = "web.admin.downloads.preparation", ["FilesKey"] = "web.admin.downloads.preparedFiles", ["DevKey"] = "web.admin.downloads.deviceCopies",
            ["HistKey"] = "web.admin.downloads.history", ["LocKey"] = "web.admin.downloads.location", ["K"] = "web.admin.downloads.preparation",
            ["P"] = "web.admin.downloads.preparedFiles.",
        };
        var missing = new List<string>();
        foreach (var path in FamilySources().Where(p => p.EndsWith(".cs", StringComparison.Ordinal) && !p.EndsWith("TextCatalog.cs", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(path);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "\"((?:web\.admin\.downloads|configure\.native\.preparedFiles|web\.admin\.nodes\.hooks|web\.admin\.errors\.node)[A-Za-z0-9_.]*[A-Za-z0-9_])\""))
                if (!AdminPreparedFilesText.Owns(m.Groups[1].Value)) missing.Add(Path.GetFileName(path) + ": " + m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "\b(PrepKey|FilesKey|DevKey|HistKey|LocKey)\s*\+\s*\"(\.[A-Za-z0-9_.]*[A-Za-z0-9_])\""))
                if (!AdminPreparedFilesText.Owns(prefixes[m.Groups[1].Value] + m.Groups[2].Value)) missing.Add(Path.GetFileName(path) + ": " + prefixes[m.Groups[1].Value] + m.Groups[2].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source, "\$\"\{K\}(\.[A-Za-z0-9_.]*[A-Za-z0-9_])\""))
                if (!AdminPreparedFilesText.Owns(prefixes["K"] + m.Groups[1].Value)) missing.Add(Path.GetFileName(path) + ": " + prefixes["K"] + m.Groups[1].Value);
        }
        Assert.Empty(missing);
    }
}