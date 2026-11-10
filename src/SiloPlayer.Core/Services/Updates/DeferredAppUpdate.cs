using System.Text.Json;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public enum DeferredUpdateAction { Wait, Resume, Discard }

/// <summary>
/// "Later" in the update panel: the downloaded update finishes on the next start. Per-user
/// cache only; it records consent, never grants authority. Installation still goes through the
/// normal window, release authority and administrator approval. The consent belongs to the
/// channel it was given on; another channel never resumes it.
/// </summary>
public sealed record DeferredAppUpdate(string Tag, string Version, AppUpdateChannel Channel = AppUpdateChannel.Stable)
{
    public const string FileName = "deferred-update.json";

    public static void Save(string cacheDirectory, AppRelease release, AppUpdateChannel channel = AppUpdateChannel.Stable)
    {
        var path = PathIn(cacheDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AppUpdateDownloader.EnsureOrdinaryPath(path);
        var record = new DeferredAppUpdate(release.Tag, GitHubReleaseReader.Normalize(release.Version).ToString(4), channel);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record));
        File.Move(temporary, path, overwrite: true);
    }

    public static DeferredAppUpdate? Read(string cacheDirectory)
    {
        var path = PathIn(cacheDirectory);
        try
        {
            AppUpdateDownloader.EnsureOrdinaryPath(path);
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 4096) return null;
            var record = JsonSerializer.Deserialize<DeferredAppUpdate>(File.ReadAllText(path));
            if (record is { Tag.Length: > 1 and <= 64 } && record.Tag.StartsWith('v') && System.Version.TryParse(record.Version, out _)
                && Enum.IsDefined(record.Channel)) return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        Clear(cacheDirectory);
        return null;
    }

    public static void Clear(string cacheDirectory)
    {
        try { var path = PathIn(cacheDirectory); AppUpdateDownloader.EnsureOrdinaryPath(path); File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
    }

    /// <summary>
    /// Resume once a check has found the deferred release as an incremental update; discard when
    /// this client already is that version (the update finished), when a complete check found no
    /// update or another release (that one needs its own consent), when only the full installer
    /// can apply it, or when the channel changed since Later (a channel change retires prepared
    /// offers). Otherwise wait for the next check.
    /// </summary>
    public DeferredUpdateAction Decide(Version installed, AppUpdateState state, bool checkSucceeded, AppUpdateChannel channel = AppUpdateChannel.Stable)
    {
        if (channel != Channel || !System.Version.TryParse(Version, out var deferred)
            || GitHubReleaseReader.Normalize(deferred) <= GitHubReleaseReader.Normalize(installed)) return DeferredUpdateAction.Discard;
        if (state.IsChecking || state.IsDownloading) return DeferredUpdateAction.Wait;
        if (state.Release is null) return checkSucceeded && state.Error is null ? DeferredUpdateAction.Discard : DeferredUpdateAction.Wait;
        if (state.Release.Tag != Tag || GitHubReleaseReader.Normalize(state.Release.Version) != GitHubReleaseReader.Normalize(deferred))
            return checkSucceeded && state.Error is null ? DeferredUpdateAction.Discard : DeferredUpdateAction.Wait;
        if (state.RequiresFullInstaller || state.Release.Incremental is null) return DeferredUpdateAction.Discard;
        return DeferredUpdateAction.Resume;
    }

    private static string PathIn(string cacheDirectory) => Path.Combine(Path.GetFullPath(cacheDirectory), FileName);
}
