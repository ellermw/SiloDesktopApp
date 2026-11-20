using System.Text.Json;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Exact official 217ff25 en.json ICU messages; native-only limits/failure wording stays explicit.</summary>
public static class AdminUserResourceText
{
    private static readonly IReadOnlyDictionary<string, string> English = Load();
    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = typeof(AdminUserResourceText).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Admin.admin-user-resources.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static string T(string key, params (string Name, object? Value)[] args)
    {
        var values = args.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        return English.TryGetValue(key, out var pattern) ? AdminLibrariesText.Plain(AdminText.Format(pattern, values)) : AppStrings.Get(key, values, fallback: key);
    }
    public static string Failure(Exception error) => error switch
    {
        Api.AdminResourceOutcomeUnknownException => "The change may have reached the server. Reload and review before trying again.",
        Api.ApiException { StatusCode: 412 or 428 } => "The configuration changed or has no current validator. Your draft is kept. Reload to review before saving.",
        Api.ApiException { StatusCode: 401 } => "Sign in again, then reload.",
        Api.ApiException { StatusCode: 403, ProblemType: "profile_verification_required" or "profile_unverified" } => "Verify the selected profile, then reload.",
        Api.ApiException { StatusCode: 403, ProblemType: "demo_restricted" } => "This action is unavailable in demo mode.",
        Api.ApiException { StatusCode: 403 } => "Administrator permission is required. Reload after access is restored.",
        Api.ApiException { StatusCode: 404 } => "This resource no longer exists. Reload to review.",
        Api.ApiException { StatusCode: 409 } => "The current resource state does not permit this action. Reload and review its source, target and active runs.",
        Api.ApiException { StatusCode: 400 or 422 } => "The server rejected these values. Review the fields and selected identities.",
        Api.ApiException { StatusCode: 429 } => "The server asked you to wait. Try again shortly.",
        Api.ApiException { StatusCode: 501 } => "This capability is unavailable on the server.",
        ArgumentException or InvalidOperationException => error.Message,
        InvalidDataException => "The server returned incomplete or inconsistent data. Reload before making changes.",
        _ => "The request did not finish. Check the connection and reload.",
    };
}
