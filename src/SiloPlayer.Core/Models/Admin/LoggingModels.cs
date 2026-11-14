using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Models.Admin;

public sealed record LoggingSettingsSnapshot(IReadOnlyDictionary<string, string> Values, string ETag);
public sealed record LoggingSaveIntent(IReadOnlyDictionary<string, string> Values, string ETag, IReadOnlyDictionary<string, long> Versions);

public sealed record LoggingSettingsResult
{
    public Dictionary<string, string> Values { get; init; } = [];
    public bool RestartRequired { get; init; }
    public string[] RestartRequiredKeys { get; init; } = [];
}

public sealed record LoggingRestartKeys
{
    public string[] Keys { get; init; } = [];
    public string[] Prefixes { get; init; } = [];
    public bool Has(string key) => Keys.Contains(key, StringComparer.Ordinal) || Prefixes.Any(prefix => !string.IsNullOrEmpty(prefix) && key.StartsWith(prefix, StringComparison.Ordinal));
}

public sealed record LoggingSettingValue
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public bool RestartRequired { get; init; }
}

public sealed class LoggingSettingsConflictException(string? currentETag)
    : ApiException("precondition_failed", "Settings changed on the server. Reload settings to reconcile your changes before saving.", 412)
{
    public string? CurrentETag { get; } = currentETag;
}
