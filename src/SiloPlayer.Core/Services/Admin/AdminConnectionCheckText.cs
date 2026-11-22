using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The official WebUI's English text for the answers of the AI model checks (text model, speech to text, embeddings), keyed the way the
/// server names them: the check answer's <c>message_key</c> (<c>web.admin.common.connectionCheck.result.*</c>), with its
/// <c>message_args</c> filling the placeholders. Copied verbatim from <c>web/src/locales/en.json</c> at upstream
/// c90e406db71ee468bffdfa5733cd97d1d50b4019, which carries the failure reasons added with silo-server #5082 for the text model check
/// (a rejected key, an unknown model, a billing or rate limit, an unreachable endpoint, a timeout and so on).
/// </summary>
public static partial class AdminConnectionCheckText
{
    public const string Prefix = "web.admin.common.connectionCheck.result.";

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["chatGPTSignInRequired"] = "Sign in with ChatGPT in AI Services settings.",
        ["embeddingConnected"] = "Embedding connection successful. The model returns {dimensions} dimensions.",
        ["embeddingDimensionsMismatch"] = "Embeddings were created with {locked}-dimension vectors, but the model now returns {current}. Reset embeddings before switching models.",
        ["embeddingLockOutOfRange"] = "The embedding lock records {locked}-dimension vectors; Silo stores 1 to {max}. Reset embeddings and use a model that returns at most {max} dimensions.",
        ["embeddingLockUnreadable"] = "The stored embedding lock cannot be read. Reset embeddings to clear it.",
        ["embeddingModelMismatch"] = "Embeddings were created with model \"{locked_model}\", not \"{model}\". Reset embeddings before switching models.",
        ["embeddingNoVector"] = "The embedding provider returned no vector for the test text.",
        ["embeddingStorageMismatch"] = "Embeddings were stored at {stored} dimensions; this server stores {supported}. Reset embeddings.",
        ["embeddingTooManyDimensions"] = "The model returns {dimensions} dimensions; Silo stores at most {max}.",
        ["failed"] = "Connection check failed. Verify the submitted settings and provider availability.",
        ["speechConnected"] = "Speech-to-text connection successful: timestamped speech received.",
        ["speechForbidden"] = "Speech-to-text access was denied. Check your API key permissions and allowed providers.",
        ["speechNoTimestamps"] = "Speech-to-text model does not return timestamped segments, which Silo needs to make subtitles. Choose a model that supports segment timestamps, such as whisper-1 on OpenAI.",
        ["speechNotFound"] = "Speech-to-text endpoint or model is unavailable. Check the provider's base URL, the model, and allowed providers.",
        ["speechPaymentRequired"] = "Speech-to-text provider requires payment. Check your provider balance and spending limit.",
        ["speechQuotaExhausted"] = "Speech-to-text provider quota is exhausted. Check your provider balance and limits.",
        ["speechRateLimited"] = "Speech-to-text provider is rate limiting requests. Try again later.",
        ["speechTimedOut"] = "Speech-to-text connection check timed out. Check provider availability and try again.",
        ["speechUnauthorized"] = "Speech-to-text authentication failed. Replace the provider's API key in AI Services.",
        ["speechUnavailable"] = "Speech-to-text provider is temporarily unavailable. Try again later.",
        ["textAIBilling"] = "Text AI provider has no available credit, or a usage or spending limit was reached. Check the provider balance and limits, or ChatGPT usage settings.",
        ["textAIConnected"] = "Text AI connection successful.",
        ["textAIFailed"] = "Text AI connection failed. Check the model, reasoning level, service tier, account connection, and provider usage limits.",
        ["textAIForbidden"] = "Text AI access was denied. Check that the API key or ChatGPT account may use this model.",
        ["textAINotFound"] = "Text AI endpoint or model is unavailable. Check the provider's base URL and the model.",
        ["textAIPlanIneligible"] = "The connected ChatGPT account can't share its plan usage. Check ChatGPT usage settings, or connect another account.",
        ["textAIRateLimited"] = "Text AI provider is rate limiting requests. Try again later.",
        ["textAIRefused"] = "Text AI provider refused the check request. Check the model, the reasoning level, and the account's usage settings. The server log has the provider's error.",
        ["textAIRejected"] = "Text AI provider rejected the check request (status {status}). Check the model, reasoning level, and service tier. The server log has the provider's error.",
        ["textAITimedOut"] = "Text AI connection check timed out. Check provider availability and try again.",
        ["textAIUnauthorized"] = "Text AI provider rejected the credentials. Replace the provider's API key, or sign in with ChatGPT again.",
        ["textAIUnavailable"] = "Text AI provider is temporarily unavailable. Try again later.",
        ["textAIUnreachable"] = "Silo could not reach the text AI provider. Check the provider's base URL and that this server can connect to it.",
        ["textAIUnusableResponse"] = "Text model returned a response Silo cannot use. Choose another model.",
    };

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    /// <summary>The catalog keys this helper owns (with <see cref="Prefix"/>), for catalog-coverage tests.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = English.Keys.Select(key => Prefix + key).ToArray();

    public static bool Has(string? key) => key != null && key.StartsWith(Prefix, StringComparison.Ordinal) && English.ContainsKey(key[Prefix.Length..]);

    /// <summary>
    /// The text of one check answer (official <c>serverText</c>): the catalog's text for <paramref name="key"/> when this catalog has it and
    /// <paramref name="args"/> fill every placeholder it prints, otherwise the server's own <paramref name="message"/> exactly as sent. A server that
    /// sends no key (an older one) keeps its message, which is how the generic text model failure still reads there.
    /// </summary>
    public static string Resolve(string? key, IReadOnlyDictionary<string, string>? args, string message)
    {
        if (!Has(key)) return message;
        var pattern = English[key![Prefix.Length..]];
        foreach (Match placeholder in Placeholder().Matches(pattern))
            if (args == null || !args.TryGetValue(placeholder.Groups[1].Value, out var value) || value == null) return message;
        return Placeholder().Replace(pattern, match => args![match.Groups[1].Value]);
    }
}
