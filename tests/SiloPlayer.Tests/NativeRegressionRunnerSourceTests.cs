namespace SiloPlayer.Tests;

public sealed class NativeRegressionRunnerSourceTests
{
    [Fact]
    public void RunnerForwardsSelectorsWithoutADuplicatedAllowlist()
    {
        var runner = ReadNativeSource("run.ps1");
        Assert.Contains("[string] $Only", runner);
        Assert.DoesNotContain("ValidateSet", runner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$env:SILO_NATIVE_TEST_ONLY = $Only", runner);
    }

    [Fact]
    public void DispatcherRejectsUnknownNonemptySelectorsBeforeTheDefaultSuite()
    {
        var source = ReadNativeSource("Program.cs");
        var guard = source.IndexOf("var unknownGroup = Environment.GetEnvironmentVariable(\"SILO_NATIVE_TEST_ONLY\");", StringComparison.Ordinal);
        var defaultSuite = source.IndexOf("await HomeRefreshPreservesCards(parent);", StringComparison.Ordinal);
        Assert.True(guard >= 0 && defaultSuite > guard);
        var validation = source[guard..defaultSuite];
        Assert.Contains("if (!string.IsNullOrEmpty(unknownGroup))", validation);
        Assert.Contains("throw new InvalidOperationException($\"Unknown native test group: {unknownGroup}\");", validation);
        var failure = source.IndexOf("catch (Exception ex)", defaultSuite, StringComparison.Ordinal);
        var cleanup = source.IndexOf("finally", failure, StringComparison.Ordinal);
        Assert.Contains("Environment.ExitCode = 1;", source[failure..cleanup]);
    }

    private static string ReadNativeSource(string fileName)
        => File.ReadAllText(Path.Combine(TestRepository.Root, "tests", "SiloPlayer.NativeRegressionTests", fileName));
}
