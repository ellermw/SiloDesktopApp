using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

public static class AdminIntegrationPolicy
{
    public static bool IsProviderEndpoint(string? value) => value is { Length: > 0 and <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme is "http" or "https" && url.UserInfo == "" && url.Query == "" && url.Fragment == "";

    public static AdminJellyfinWebStatus ReconcileWebOperation(AdminJellyfinWebStatus observed, AdminJellyfinWebStatus? accepted) =>
        accepted is { OperationRunning: true } && (accepted.OperationId != observed.OperationId || accepted.Operation != observed.Operation ||
            (observed.OperationRunning ? observed.OperationState != "running" : observed.OperationState is not ("succeeded" or "failed"))) ? accepted : observed;

    public static bool CanToggleWeb(AdminJellyfinWebStatus? status, AdminIntegrationSettingsState state) =>
        state.Loaded && !state.Loading && !state.Saving && !state.Conflict && !state.OutcomeUnknown &&
        !state.ManagedByEnvironment.Contains("jellyfin_compat.web_enabled") && status is { ApiEnabled: true, Installed: true, OperationRunning: false };

    public static bool ProviderReady(IEnumerable<AdminAiProvider> providers, string id, string capability, string model) =>
        !string.IsNullOrWhiteSpace(model) && providers.Any(provider => provider.Id == id && provider.Capabilities.Contains(capability));

    public static bool CanReset(AdminRecommendationStatus? status) => status != null && !status.Embeddings.Running && !status.TasteProfiles.Running && !status.Recommendations.Running;
    public static bool CanInstall(AdminJellyfinWebStatus? status, AdminIntegrationSettingsState state) =>
        state.Loaded && !state.Loading && !state.Saving && !state.Conflict && !state.OutcomeUnknown &&
        status is { InstallerReady: true, OperationRunning: false } && !state.DirtyKeys.Any(key => key is "jellyfin_compat.web_version" or "jellyfin_compat.web_install_dir");

    public static bool IsAuthorizationUrl(Uri url) => url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps &&
        url.IdnHost.Equals("auth.openai.com", StringComparison.OrdinalIgnoreCase) && url.IsDefaultPort && url.UserInfo == "" && url.Fragment == "";

    public static bool IsCallbackEndpoint(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme == Uri.UriSchemeHttp && url.IsLoopback && url.UserInfo == "" && url.Fragment == "" && url.Query == "" && url.AbsolutePath == "/auth/callback";

    public static bool IsCallback(string value, string endpoint)
    {
        if (value.Length > 32768 || !IsCallbackEndpoint(endpoint) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url)) return false;
        var expected = new Uri(endpoint);
        if (url.Scheme != expected.Scheme || url.IdnHost != expected.IdnHost || url.Port != expected.Port || url.AbsolutePath != expected.AbsolutePath || url.UserInfo != "" || url.Fragment != "") return false;
        var query = url.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(part => part.Length == 2).ToArray();
        return query.Count(part => part[0] == "state" && part[1] != "") == 1 && query.Any(part => (part[0] == "code" || part[0] == "error") && part[1] != "");
    }

    /// <summary>The numeric AI tuning fields with their limits and the official page message for each (en.json web.admin.settings.ai.page.*Invalid).</summary>
    private static readonly (string Key, int Minimum, int Maximum, string Message)[] TuningChecks =
    [
        ("ai.max_concurrent_jobs", 1, int.MaxValue, "Max concurrent jobs must be a positive whole number."),
        ("subtitle_ai.batch_size", 1, int.MaxValue, "Subtitle batch size must be a positive whole number."),
        ("subtitle_ai.context_neighbors", 0, int.MaxValue, "Subtitle context lines must be zero or a positive whole number."),
        ("subtitle_ai.asr_chunk_seconds", 60, 600, "Transcription chunk length must be between 60 and 600 seconds."),
        ("subtitle_ai.transcribe_quota_jobs", 0, int.MaxValue, "Transcription limit must be zero or a positive whole number."),
    ];

    public static string? ValidateAi(AdminIntegrationSettingsState state, IEnumerable<AdminAiProvider> providers, AdminAiCapabilities? capabilities,
        AdminChatGptStatus? connection, IReadOnlyList<AdminChatGptModel> models)
    {
        string Value(string key, string fallback = "")
        {
            var value = state.Values.GetValueOrDefault(key, "");
            return value.Length > 0 || state.DirtyKeys.Contains(key) ? value : fallback;
        }
        bool Enabled(string key) => Value(key) == "true";
        var textDirty = state.DirtyKeys.Any(AdminIntegrationsSettings.TextKeys.Contains);
        var textFeatures = Enabled("subtitle_ai.enabled") || Enabled("metadata_ai.enabled");
        var chatGpt = Value("ai.auth_mode", "api_key") == "chatgpt";
        var cleared = !chatGpt && Value("ai.text_provider_id") == "";
        var textRequired = (textDirty && (!cleared || textFeatures)) || state.DirtyKeys.Any(key => (key is "subtitle_ai.enabled" or "metadata_ai.enabled") && Enabled(key));
        if (textRequired)
        {
            var ready = chatGpt ? connection?.Active is { Ready: true } && models.Any(model => model.Id == Value("ai.chat_model", "gpt-6-luna"))
                : ProviderReady(providers, Value("ai.text_provider_id"), "chat", Value("ai.chat_model", "gpt-6-luna"));
            if (!ready) return cleared ? "Turn off text features before removing their provider." : "Choose a connected text provider and available model.";
            if (capabilities != null && capabilities.Model == Value("ai.chat_model", "gpt-6-luna") && Value("ai.reasoning_effort") is { Length: > 0 } reasoning && !capabilities.ReasoningLevels.Contains(reasoning)) return "Choose a reasoning level supported by this model.";
        }
        if (Enabled("subtitle_ai.transcribe_enabled") && (state.DirtyKeys.Contains("subtitle_ai.transcribe_enabled") || state.DirtyKeys.Any(AdminIntegrationsSettings.SpeechKeys.Contains)) &&
            !ProviderReady(providers, Value("ai.speech_provider_id"), "speech", Value("ai.asr_model", "whisper-1"))) return "Choose a speech-capable provider and model before enabling transcription.";
        // Only a value this save sends is checked: an untouched field holds what the server has (official #4816). A field that was typed into and
        // erased is still sent, as empty, and is refused here. The messages are the official AI Services page strings.
        foreach (var (key, minimum, maximum, message) in TuningChecks)
        {
            if (!state.DirtyKeys.Contains(key)) continue;
            if (!int.TryParse(Value(key), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum)
                return message;
        }
        if (Value("metadata_ai.on_view", "off") is not ("off" or "button" or "auto")) return "Choose Off, Button, or Automatic for viewer translation.";
        if (Value("ai.auth_mode", "api_key") is not ("api_key" or "chatgpt")) return "Choose a supported authentication mode.";
        if (Value("ai.service_tier") is { Length: > 0 } tier && tier is not ("priority" or "ultrafast")) return "Choose a supported service tier.";
        if (textRequired && !chatGpt && providers.Any(provider => provider.Id == Value("ai.text_provider_id") && provider.Type == "openai") &&
            capabilities?.Model == Value("ai.chat_model", "gpt-6-luna") && Value("ai.service_tier") is { Length: > 0 } selectedTier && !capabilities.ServiceTiers.Contains(selectedTier)) return "Choose a service tier available for this model.";
        return null;
    }
}
