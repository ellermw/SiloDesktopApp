namespace SiloPlayer.Tests;

internal static class TestRepository
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate the SiloPlayer solution from the test output directory.");
    }
}
