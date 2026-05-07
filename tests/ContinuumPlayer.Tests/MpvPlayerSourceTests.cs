namespace ContinuumPlayer.Tests;

public sealed class MpvPlayerSourceTests
{
    [Fact]
    public void EofReachedPropertyRaisesDedicatedEventWithoutTreatingItAsNaturalEnd()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("public event Action? EofReached;", source);
        Assert.Contains("EofReached?.Invoke();", source);

        var caseStart = source.IndexOf("case UD_EOF_REACHED:", StringComparison.Ordinal);
        var caseEnd = source.IndexOf("case UD_PAUSED_FOR_CACHE:", StringComparison.Ordinal);
        Assert.True(caseStart >= 0);
        Assert.True(caseEnd > caseStart);

        var eofCase = source[caseStart..caseEnd];
        Assert.DoesNotContain("PlaybackEnded?.Invoke();", eofCase);
    }

    [Fact]
    public void CommandFailureLoggingRedactsQuerySecrets()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("RedactCommandArgument", source);
        Assert.Contains("room_token", source);
        Assert.Contains("api_key", source);
        Assert.DoesNotContain(@"(?:token|access_token|refresh_token|profile_token)", source);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
