using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Settings;

public sealed class OnboardingFlow
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("tour_id")] public string TourId { get; set; } = "";
    [JsonPropertyName("steps")] public List<OnboardingStep> Steps { get; set; } = [];
}

public sealed class OnboardingStep
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("illustration")] public string? Illustration { get; set; }
    [JsonPropertyName("setting")] public OnboardingSettingSpec? Setting { get; set; }
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("action_label")] public string? ActionLabel { get; set; }
    [JsonPropertyName("links")] public List<OnboardingStepLink> Links { get; set; } = [];
}

public sealed class OnboardingSettingSpec
{
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("control")] public string Control { get; set; } = "";
    [JsonPropertyName("options")] public List<OnboardingSettingOption> Options { get; set; } = [];
    [JsonPropertyName("default")] public string? Default { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
}

public sealed class OnboardingSettingOption
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
}

public sealed class OnboardingStepLink
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}
