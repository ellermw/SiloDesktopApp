using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The embedding lock conflict the recommendations status reports. Since official #4442 the status sends <c>lock_conflict_key</c> and <c>lock_conflict_args</c> beside the
/// English <c>lock_conflict</c>, and the web shows the catalog's text for the key with the arguments filled (official <c>useServerText</c>); the server's English stays the
/// fallback when the key is not a lock-conflict text or an argument the text uses is missing. The texts are the official catalog's English values, as the rest of the native
/// Admin text is, at the pinned source <c>c90e406db71ee468bffdfa5733cd97d1d50b4019</c>.
/// </summary>
public static partial class AdminRecommendationLockConflict
{
    private static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.recommendations.embedding.lockConflictNoProvider"] = "No embedding provider is chosen. Choose a provider that serves \"{locked_model}\", or reset embeddings to use another model.",
        ["web.admin.common.connectionCheck.result.embeddingStorageMismatch"] = "Embeddings were stored at {stored} dimensions; this server stores {supported}. Reset embeddings.",
        ["web.admin.common.connectionCheck.result.embeddingModelMismatch"] = "Embeddings were created with model \"{locked_model}\", not \"{model}\". Reset embeddings before switching models.",
        ["web.admin.common.connectionCheck.result.embeddingDimensionsMismatch"] = "Embeddings were created with {locked}-dimension vectors, but the model now returns {current}. Reset embeddings before switching models.",
        ["web.admin.common.connectionCheck.result.embeddingLockOutOfRange"] = "The embedding lock records {locked}-dimension vectors; Silo stores 1 to {max}. Reset embeddings and use a model that returns at most {max} dimensions.",
    };

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    public static bool IsKnownKey(string? key) => key != null && Catalog.ContainsKey(key);

    /// <summary>The conflict text to show: the catalog's text for the status's key with its arguments, or the server's English text when that cannot be built.</summary>
    public static string Text(AdminRecommendationStatus status) => Text(status.LockConflictKey, status.LockConflictArgs, status.LockConflict);

    public static string Text(string? key, IReadOnlyDictionary<string, string>? arguments, string serverText)
    {
        if (key == null || !Catalog.TryGetValue(key, out var pattern)) return serverText;
        foreach (Match placeholder in Placeholder().Matches(pattern))
            if (arguments == null || !arguments.TryGetValue(placeholder.Groups[1].Value, out var value) || value.Length == 0) return serverText;
        return AdminText.Format(pattern, arguments!.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal));
    }
}
