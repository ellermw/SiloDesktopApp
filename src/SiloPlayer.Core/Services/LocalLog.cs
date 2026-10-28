using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services;

public static partial class LocalLog
{
    public const long DefaultMaxBytes = 2 * 1024 * 1024;
    public const string LogDirectoryEnvironmentVariable = "SILOPLAYER_LOG_DIRECTORY";
    private static readonly object s_gate = new();
    private static readonly AsyncLocal<string?> s_operation = new();
    private static readonly AsyncLocal<string?> s_operationDirectory = new();

    public static string? CurrentOperationId => s_operation.Value;

    public static IDisposable BeginOperation(string operationId, string? logDirectory = null)
    {
        var previous = s_operation.Value;
        var previousDirectory = s_operationDirectory.Value;
        s_operation.Value = operationId;
        s_operationDirectory.Value = logDirectory ?? previousDirectory;
        return new OperationScope(previous, previousDirectory);
    }

    private sealed class OperationScope(string? previous, string? previousDirectory) : IDisposable
    {
        public void Dispose()
        {
            s_operation.Value = previous;
            s_operationDirectory.Value = previousDirectory;
        }
    }

    /// <summary>Records caught failures as well as fatal ones, without exception Data,
    /// request bodies, headers or signed URLs. Logging must never replace the failure.</summary>
    public static void AppendException(string fileName, Exception exception, string operation,
        string? operationId = null, string? logDirectory = null)
    {
        try
        {
            var lines = new List<string>
            {
                $"utc={DateTime.UtcNow:O} action={operationId ?? CurrentOperationId ?? Guid.NewGuid().ToString("N")} operation={RedactDiagnosticText(operation)}"
            };
            void Record(Exception failure, int depth)
            {
                if (depth > 16) { lines.Add("exception chain truncated at depth 16"); return; }
                var api = failure as Api.ApiException;
                lines.Add($"depth={depth} type={failure.GetType().FullName} hresult=0x{failure.HResult:X8}" +
                    (api == null ? "" : $" status={api.StatusCode} code={RedactDiagnosticText(api.ErrorCode)}"));
                lines.Add("message=" + RedactDiagnosticText(failure.Message));
                lines.Add("stack=" + RedactDiagnosticText(failure.StackTrace ?? "<not thrown>"));
                if (failure is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions.Take(32)) Record(inner, depth + 1);
                else if (failure.InnerException != null) Record(failure.InnerException, depth + 1);
            }
            Record(exception, 0);
            AppendLines(fileName, lines, logDirectory);
        }
        catch { }
    }

    public static string RedactDiagnosticText(string? value)
    {
        // Exception messages may contain a complete URL with an unknown signing
        // parameter. Remove the entire URL rather than guessing its secret keys.
        var text = RedactSensitiveData(DiagnosticUrlRegex().Replace(value ?? "", "<url redacted>"));
        text = DiagnosticAuthorizationRegex().Replace(text, "${prefix}<redacted>");
        text = DiagnosticSecretRegex().Replace(text, "${prefix}<redacted>");
        return text.Length > 32768 ? text[..32768] + " <truncated>" : text;
    }

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
            logDirectory ??= s_operationDirectory.Value ?? ResolveDefaultLogDirectory();

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
        => AppendTimestampedLines(fileName, messages.Select(message => (DateTime.Now, message)), logDirectory, maxBytes, archiveCount);

    /// <summary>Appends lines stamped with when they were recorded, for callers that queue before writing.</summary>
    public static void AppendTimestampedLines(
        string fileName,
        IEnumerable<(DateTime RecordedAt, string Message)> entries,
        string? logDirectory = null,
        long maxBytes = DefaultMaxBytes,
        int archiveCount = 2)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            fileName = Path.GetFileName(fileName);
            logDirectory ??= s_operationDirectory.Value ?? ResolveDefaultLogDirectory();

            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, fileName);

            var lines = entries
                .Select(entry => $"[{entry.RecordedAt:HH:mm:ss.fff}] {RedactSensitiveData(entry.Message)}{Environment.NewLine}")
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

        var redacted = PlaybackUrlRedactor.RedactSignedCredentials(value);
        redacted = BearerTokenRegex().Replace(redacted, "$1<redacted>");
        redacted = QuerySecretRegex().Replace(redacted, match =>
        {
            string name;
            try { name = Uri.UnescapeDataString(match.Groups["name"].Value).ToLowerInvariant(); }
            catch (UriFormatException) { return match.Value; }
            return name is "access_token" or "refresh_token" or "profile_token" or "room_token" or "token" or
                "st" or "dt" or "jwt" or "password" or "secret" or "api_key" or "apikey" or "key"
                ? match.Groups["prefix"].Value + "<redacted>" : match.Value;
        });
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

    [GeneratedRegex(@"(?<prefix>[?&](?<name>[^=&\s]+)=)[^&\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex(@"(?<prefix>""(?:access_token|refresh_token|profile_token|token|st|dt|jwt|password|secret|api_key|apikey|key)""\s*:\s*"")[^""]*(?<suffix>"")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticUrlRegex();

    [GeneratedRegex(@"(?<prefix>Authorization:\s*(?:Basic|Bearer)\s+)[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticAuthorizationRegex();

    [GeneratedRegex(@"(?<prefix>\b(?:access_token|refresh_token|profile_token|room_token|token|st|dt|jwt|password|secret|api_key|apikey)\s*[:=]\s*)[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticSecretRegex();
}
