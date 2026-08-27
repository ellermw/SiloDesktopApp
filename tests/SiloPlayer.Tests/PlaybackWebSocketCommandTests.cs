using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackWebSocketCommandTests
{
    [Fact]
    public void PlanInvalidationRequiresBothPlanIdAndReason()
    {
        var valid = Payload("""{"plan_id":"plan:1","reason":"video_copy_unsafe"}""");
        var missingPlan = Payload("""{"reason":"video_copy_unsafe"}""");
        var missingReason = Payload("""{"plan_id":"plan:1"}""");

        Assert.True(PlaybackPlanInvalidation.TryCreate(valid, out var invalidation));
        Assert.Equal("plan:1", invalidation.PlanId);
        Assert.Equal("video_copy_unsafe", invalidation.Reason);
        Assert.False(PlaybackPlanInvalidation.TryCreate(missingPlan, out _));
        Assert.False(PlaybackPlanInvalidation.TryCreate(missingReason, out _));
    }

    [Fact]
    public void PlaybackSocketAdvertisesPlanInvalidationCapability()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer", "Services", "PlaybackWebSocket.cs"));

        Assert.Contains("\"plan_invalidated\"", source);
    }

    private static Dictionary<string, JsonElement> Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException();
    }
}
