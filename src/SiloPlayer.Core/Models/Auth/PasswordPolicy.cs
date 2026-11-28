using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Auth;

public sealed record PasswordPolicy
{
    public const int DefaultMinimumLength = 8;
    public const int DefaultMaximumBytes = 72;
    public static PasswordPolicy Default { get; } = new();

    [JsonPropertyName("minimum_length")]
    public int MinimumLength { get; init; } = DefaultMinimumLength;
    [JsonPropertyName("maximum_bytes")]
    public int MaximumBytes { get; init; } = DefaultMaximumBytes;
    [JsonPropertyName("require_uppercase")]
    public bool RequireUppercase { get; init; }
    [JsonPropertyName("require_special")]
    public bool RequireSpecial { get; init; }
}
