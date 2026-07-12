using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public class RateLimitConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "memory";

    [JsonPropertyName("global_requests_per_second")]
    public int GlobalRequestsPerSecond { get; set; }

    [JsonPropertyName("ip_requests_per_second")]
    public int IpRequestsPerSecond { get; set; }

    [JsonPropertyName("ip_requests_per_minute")]
    public int IpRequestsPerMinute { get; set; }

    [JsonPropertyName("ip_burst")]
    public int IpBurst { get; set; }

    [JsonPropertyName("tiers")]
    public Dictionary<string, RateLimitTierConfig> Tiers { get; set; } = new();

    [JsonPropertyName("auth_endpoints")]
    public Dictionary<string, RateLimitAuthEndpointConfig> AuthEndpoints { get; set; } = new();
}

public class RateLimitTierConfig
{
    [JsonPropertyName("requests_per_second")]
    public int RequestsPerSecond { get; set; }

    [JsonPropertyName("requests_per_minute")]
    public int RequestsPerMinute { get; set; }

    [JsonPropertyName("burst")]
    public int Burst { get; set; }
}

public class RateLimitAuthEndpointConfig
{
    [JsonPropertyName("requests_per_minute")]
    public int RequestsPerMinute { get; set; }

    [JsonPropertyName("burst")]
    public int Burst { get; set; }
}
