using System.Runtime.CompilerServices;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

internal static class TestLogIsolation
{
    [ModuleInitializer]
    internal static void Configure()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SiloPlayer.Tests",
            $"run-{Environment.ProcessId}");
        Environment.SetEnvironmentVariable(
            LocalLog.LogDirectoryEnvironmentVariable,
            directory);
    }
}
