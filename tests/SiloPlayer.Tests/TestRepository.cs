using System.Runtime.CompilerServices;

namespace SiloPlayer.Tests;

internal static class TestRepository
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot([CallerFilePath] string sourceFile = "")
    {
        // The compiled-in source path always names the checkout the tests were built from, even when the build
        // output lives outside it (an --artifacts-path on another drive); the output walk is the fallback for
        // binaries run on a machine that no longer has the sources at that path.
        foreach (var start in new[] { Path.GetDirectoryName(sourceFile), AppContext.BaseDirectory })
            for (var directory = start is { Length: > 0 } ? new DirectoryInfo(start) : null; directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                    return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate the SiloPlayer solution from the test source or output directory.");
    }
}
