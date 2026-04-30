namespace ContinuumPlayer.Tests;

public sealed class ApiPayloadSourceTests
{
    [Fact]
    public void PlaybackMutationBodiesUseTrimSafeDictionaries()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer.Core",
            "Api",
            "PlaybackApi.cs"));

        Assert.DoesNotContain("new { position", source);
        Assert.DoesNotContain("new { audio_track_index", source);
        Assert.DoesNotContain("new { progress_updated_at", source);
        Assert.DoesNotContain("new { media_file_id", source);
        Assert.Contains("""["is_paused"] = isPaused""", source);
        Assert.Contains("""["audio_track_index"] = trackIndex""", source);
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
