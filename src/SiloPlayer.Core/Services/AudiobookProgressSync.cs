namespace SiloPlayer.Core.Services;

public static class AudiobookProgressSync
{
    public static Dictionary<string, object?> Create(string contentId, double positionSeconds, double durationSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        if (!double.IsFinite(positionSeconds) || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        return new()
        {
            ["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["media_item_id"] = contentId,
                    ["position_ms"] = checked((long)Math.Floor(Math.Clamp(positionSeconds, 0, durationSeconds) * 1000)),
                    ["duration_ms"] = checked((long)Math.Floor(durationSeconds * 1000)),
                    ["force_overwrite"] = true,
                },
            },
        };
    }
}
