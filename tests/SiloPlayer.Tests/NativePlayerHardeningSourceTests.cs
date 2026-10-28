namespace SiloPlayer.Tests;

public sealed class NativePlayerHardeningSourceTests
{
    [Fact]
    public void ReleaseBuildsLoadOnlyTheShippedOscScript()
    {
        var player = ReadRepoFile("src", "SiloPlayer.Player", "MpvPlayer.cs").ReplaceLineEndings("\n");
        var fallback = player.IndexOf("oscPath = Path.Combine(exeDir, \"..\", \"..\", \"..\", \"..\"", StringComparison.Ordinal);
        Assert.True(fallback > 0);
        var guard = player.LastIndexOf("#if DEBUG", fallback, StringComparison.Ordinal);
        var end = player.IndexOf("#endif", fallback, StringComparison.Ordinal);
        Assert.True(guard > 0 && end > fallback && player.IndexOf("#endif", guard, StringComparison.Ordinal) == end,
            "The source-tree OSC fallback must be compiled only into DEBUG builds.");
    }

    [Fact]
    public void NativeStreamCallbacksContainTheirFailures()
    {
        var bridge = ReadRepoFile("src", "SiloPlayer.Player", "MpvPlayer.DirectStream.cs").ReplaceLineEndings("\n");
        foreach (var callback in new[] { "DirectReadCallback", "DirectSeekCallback", "DirectSizeCallback", "DirectCloseCallback", "DirectCancelCallback" })
        {
            var start = bridge.IndexOf($"{callback} = ", StringComparison.Ordinal);
            Assert.True(start > 0, callback);
            var end = bridge.IndexOf("};", start, StringComparison.Ordinal);
            Assert.Contains("catch", bridge[start..end]);
        }
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
