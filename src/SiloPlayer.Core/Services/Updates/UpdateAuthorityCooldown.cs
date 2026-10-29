using System.Text.Json;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>
/// The finite worker persists GitHub's bounded absolute cooldown in its protected
/// installation metadata. Record is called under the installation mutation lease;
/// Read never writes and is safe for the unelevated client.
/// </summary>
public static class UpdateAuthorityCooldown
{
    internal sealed record StoredCooldown(int Schema, DateTimeOffset RecordedAt, DateTimeOffset Until);
    private static readonly TimeSpan Minimum = TimeSpan.FromMinutes(5), Maximum = TimeSpan.FromHours(6);

    public static DateTimeOffset Record(string root, TimeSpan? retryAfter, TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var now = clock.GetUtcNow();
        var until = BoundedDeadline(retryAfter, now);
        if (Read(root, clock) is { } existing && existing > until) until = existing;
        SideBySideInstallation.WriteAuthorityCooldown(root, new(1, now, until));
        return until;
    }

    public static DateTimeOffset? Read(string root, TimeProvider? timeProvider = null)
    {
        try
        {
            var record = SideBySideInstallation.ReadAuthorityCooldown(root);
            if (record is null) return null;
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
            var duration = record.Until - record.RecordedAt;
            return record.Schema == 1 && record.RecordedAt <= now && record.Until > now &&
                duration >= Minimum && duration <= Maximum && record.Until - now <= Maximum
                    ? record.Until : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        { return null; }
    }

    internal static DateTimeOffset BoundedDeadline(TimeSpan? retryAfter, TimeProvider clock) =>
        BoundedDeadline(retryAfter, clock.GetUtcNow());

    private static DateTimeOffset BoundedDeadline(TimeSpan? retryAfter, DateTimeOffset now)
    {
        var delay = retryAfter ?? Minimum;
        if (delay < Minimum) delay = Minimum;
        if (delay > Maximum) delay = Maximum;
        return now + delay;
    }
}

public static partial class SideBySideInstallation
{
    private const string AuthorityCooldownFileName = "github-cooldown.json";
    internal static UpdateAuthorityCooldown.StoredCooldown? ReadAuthorityCooldown(string root)
    {
        var path = Metadata(CanonicalRoot(root), AuthorityCooldownFileName);
        if (!File.Exists(path)) return null;
        using var parents = LockDirectories(Path.GetDirectoryName(path)!);
        using var input = OpenFile(path, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 1024) return null;
        return JsonSerializer.Deserialize<UpdateAuthorityCooldown.StoredCooldown>(input, JsonOptions);
    }
    internal static void WriteAuthorityCooldown(string root, UpdateAuthorityCooldown.StoredCooldown record)
    {
        root = CanonicalRoot(root);
        if (ReadState(root) is null) throw new InvalidOperationException("An initialized Silo installation is required.");
        WriteRecord(Metadata(root, AuthorityCooldownFileName), record);
    }
}
