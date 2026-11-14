using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiloPlayer.Tests;

public sealed class LogsNativeSyntaxTests
{
    [Theory]
    [InlineData("src/SiloPlayer/Views/Admin/AdminLogsPage.cs")]
    [InlineData("src/SiloPlayer/Views/Admin/AdminDiagnosticsPage.cs")]
    [InlineData("src/SiloPlayer/Views/Admin/AdminLoggingSettingsPage.cs")]
    [InlineData("src/SiloPlayer/Views/Admin/LogsNativeUi.cs")]
    [InlineData("tests/SiloPlayer.NativeRegressionTests/AdminMonitorLogsNativeFixture.cs")]
    [InlineData("tests/SiloPlayer.NativeRegressionTests/AdminMonitorLogsStateCoverage.cs")]
    public void NativeSourceParsesWithoutBuildingOrLaunchingThePlayer(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../.."));
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, relativePath)), new(LanguageVersion.Preview), path: relativePath);
        Assert.Empty(syntax.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
}
