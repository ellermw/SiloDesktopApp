using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services;

public static partial class LocalLog
{
    public const long DefaultMaxBytes = 2 * 1024 * 1024;
    public const string LogDirectoryEnvironmentVariable = "SILOPLAYER_LOG_DIRECTORY";
    private static readonly object s_gate = new();

    public static void AppendLine(
        string fileName,
        string message,
        string? logDirectory = null,
        long maxBytes = DefaultMaxBytes,
        int archiveCount = 2)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            fileName = Path.GetFileName(fileName);
            logDirectory ??= ResolveDefaultLogDirectory();

            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, fileName);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {RedactSensitiveData(message)}{Environment.NewLine}";

            lock (s_gate)
            {
                RotateIfNeeded(path, maxBytes, archiveCount);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
        }
    }

    public static void AppendLines(
        string fileName,
        IEnumerable<string> messages,
        string? logDirectory = null,
        long maxBytes = DefaultMaxBytes,
        int archiveCount = 2)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            fileName = Path.GetFileName(fileName);
            logDirectory ??= ResolveDefaultLogDirectory();

            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, fileName);

            var lines = messages
                .Select(message => $"[{DateTime.Now:HH:mm:ss.fff}] {RedactSensitiveData(message)}{Environment.NewLine}")
                .ToArray();
            if (lines.Length == 0)
                return;

            lock (s_gate)
            {
                RotateIfNeeded(path, maxBytes, archiveCount);
                File.AppendAllText(path, string.Concat(lines));
            }
        }
        catch
        {
        }
    }

    public static string RedactSensitiveData(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var redacted = BearerTokenRegex().Replace(value, "$1<redacted>");
        redacted = QuerySecretRegex().Replace(redacted, "${prefix}<redacted>");
        redacted = JsonSecretRegex().Replace(redacted, "${prefix}<redacted>${suffix}");
        return redacted;
    }

    private static string ResolveDefaultLogDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(LogDirectoryEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.GetFullPath(overrideDirectory)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiloPlayer");
    }

    private static void RotateIfNeeded(string path, long maxBytes, int archiveCount)
    {
        if (maxBytes <= 0 || archiveCount <= 0 || !File.Exists(path))
            return;

        var file = new FileInfo(path);
        if (file.Length < maxBytes)
            return;

        for (var i = archiveCount; i >= 1; i--)
        {
            var archived = $"{path}.{i}";
            if (i == archiveCount)
            {
                TryDelete(archived);
                continue;
            }

            var next = $"{path}.{i + 1}";
            if (File.Exists(archived))
            {
                TryDelete(next);
                File.Move(archived, next);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    [GeneratedRegex(@"(Authorization:\s*Bearer\s+)[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?<prefix>[?&](?:access_token|refresh_token|profile_token|room_token|token|jwt|password|secret|api_key|apikey|key)=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex(@"(?<prefix>""(?:access_token|refresh_token|profile_token|token|jwt|password|secret|api_key|apikey|key)""\s*:\s*"")[^""]*(?<suffix>"")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretRegex();
}
