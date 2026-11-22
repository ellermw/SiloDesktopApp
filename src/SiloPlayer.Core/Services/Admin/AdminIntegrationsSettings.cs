namespace SiloPlayer.Core.Services.Admin;

/// <summary>Official Configure settings owned by the AI, Recommendations and Compatibility leaves.</summary>
public static class AdminIntegrationsSettings
{
    public static IReadOnlyList<string> TextKeys { get; } = ["ai.text_provider_id", "ai.chat_model", "ai.auth_mode", "ai.reasoning_effort", "ai.service_tier"];
    public static IReadOnlyList<string> SpeechKeys { get; } = ["ai.speech_provider_id", "ai.asr_model"];
    public static IReadOnlyList<string> EmbeddingKeys { get; } = ["recommendations.embedding_provider_id", "recommendations.embedding_model"];
    public static IReadOnlyList<string> CompatibilityPatchKeys { get; } = Array.AsReadOnly(new[]
    {
        "jellyfin_compat.enabled", "jellyfin_compat.public_url", "jellyfin_compat.server_name", "jellyfin_compat.emulated_server_version",
        "jellyfin_compat.web_enabled", "jellyfin_compat.web_version", "jellyfin_compat.web_install_dir", "jellyfin_compat.web_dir"
    });
    public static IReadOnlyList<string> AiKeys { get; } = Array.AsReadOnly(new[]
    {
        "ai.text_provider_id", "ai.chat_model", "ai.auth_mode", "ai.reasoning_effort", "ai.service_tier",
        "ai.speech_provider_id", "ai.asr_model", "ai.max_concurrent_jobs", "subtitle_ai.enabled",
        "subtitle_ai.transcribe_enabled", "metadata_ai.enabled", "metadata_ai.on_view", "subtitle_ai.batch_size",
        "subtitle_ai.context_neighbors", "subtitle_ai.asr_chunk_seconds", "subtitle_ai.transcribe_quota_jobs",
        "subtitle_ai.transcribe_quota_period"
    });

    public static IReadOnlyList<string> RecommendationKeys { get; } = Array.AsReadOnly(new[]
    {
        "recommendations.enabled", "recommendations.embedding_provider_id", "recommendations.embedding_model",
        "recommendations.embeddings_cron", "recommendations.taste_profiles_cron", "recommendations.cowatch_cron",
        "recommendations.recommendations_cron", "recommendations.taste_decay_half_life_days", "recommendations.diversity_lambda",
        "recommendations.embedding_lock"
    });

    /// <summary>
    /// Official <c>jellyfin_compat.address_stream_grant_enabled</c> (server #4466): serve a credential-less static stream when a signed-in app at the same network address
    /// asked to play that file in the last two hours. Off by default. It is a plain settings write (the compatibility PATCH route does not carry it), and a security choice:
    /// it changes only by the admin's own action on its switch.
    /// </summary>
    public const string AddressStreamGrant = "jellyfin_compat.address_stream_grant_enabled";

    public static IReadOnlyList<string> CompatibilityKeys { get; } = Array.AsReadOnly(new[]
    {
        "jellyfin_compat.enabled", "jellyfin_compat.public_url", "jellyfin_compat.server_name", "jellyfin_compat.server_id",
        "jellyfin_compat.emulated_server_version", "jellyfin_compat.web_enabled", "jellyfin_compat.web_version",
        "jellyfin_compat.web_install_dir", "jellyfin_compat.web_dir", "jellyfin_compat.session_ttl", "jellyfin_compat.playback_session_ttl",
        AddressStreamGrant, "audiobookshelf_compat.enabled"
    });

    public static IReadOnlyList<string> ReadOnlyKeys { get; } = ["recommendations.embedding_lock", "jellyfin_compat.server_id", "jellyfin_compat.web_dir"];

    public static IReadOnlyList<string> AllKeys { get; } = Array.AsReadOnly(AiKeys.Concat(RecommendationKeys).Concat(CompatibilityKeys)
        .Distinct(StringComparer.Ordinal).ToArray());
    // AI provider keys are write-only in their dedicated endpoint, never in the effective-settings document.
    public static IReadOnlyList<string> Secrets { get; } = Array.Empty<string>();
}
