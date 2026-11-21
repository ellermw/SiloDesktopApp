using System.Text.Json;
using SiloPlayer.Core.Api;
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
    public static bool Has(string key) => English.ContainsKey(key);

    /// <summary>
    /// Wording for states the official pages do not have, so none of it is in the official catalog. Kept apart from it on purpose: the catalog stays
    /// an exact copy of the official messages.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NativeOnly = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The official admin subtitle editor shows only "Saving…", "Changes saved automatically" and "Changing a value creates an override";
        // a failed save or reset is a toast of the parent page, which the native editor has no equivalent of.
        ["admin.native.userResources.subtitleEditor.saveFailed"] = "The change was not saved.",
        ["admin.native.userResources.subtitleEditor.resetFailed"] = "The override could not be reset.",
        ["admin.native.userResources.subtitleEditor.resetting"] = "Resetting…",
        ["admin.native.userResources.subtitleEditor.retired"] = "This device changed while you were editing. Close this editor and open it again.",
        ["admin.native.userResources.subtitleEditor.writeBusy"] = "The change was not saved because another change is being saved. Wait for it to finish, then change the value again.",
    };

    public static string T(string key, params (string Name, object? Value)[] args)
    {
        if (NativeOnly.TryGetValue(key, out var nativeOnly)) return nativeOnly;
        var values = args.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        return English.TryGetValue(key, out var pattern) ? AdminLibrariesText.Plain(AdminText.Format(pattern, values)) : AppStrings.Get(key, values, fallback: key);
    }

    /// <summary>Whether a failed write leaves the page unable to trust what it shows until it is reloaded: an unknown outcome or a stale/missing validator.</summary>
    public static bool RequiresReload(Exception error) => Unwrap(error) is AdminResourceOutcomeUnknownException or ApiException { StatusCode: 412 or 428 };
    private static Exception Unwrap(Exception error) => error is AdminResourceFollowUpException { InnerException: { } inner } ? Unwrap(inner) : error;

    public static string Failure(Exception error) => error switch
    {
        AdminResourceFollowUpException { InnerException: { } inner } followUp => T(followUp.Key, ("reason", Failure(inner))),
        AdminResourceOutcomeUnknownException => "The change may have reached the server. Reload and review before trying again.",
        ApiException { StatusCode: 412 or 428 } => "This item changed since it was loaded, or its version could not be confirmed. Reload to review the current values; unsaved edits on this page are discarded by the reload.",
        ApiException { StatusCode: 401 } => "Sign in again, then reload.",
        ApiException { StatusCode: 403, ProblemType: "profile_verification_required" or "profile_unverified" } => "Verify the selected profile, then reload.",
        ApiException { StatusCode: 403, ProblemType: "demo_restricted" } => "This action is unavailable in demo mode.",
        ApiException { StatusCode: 403 } => "Administrator permission is required. Reload after access is restored.",
        ApiException { StatusCode: 404 } => "This resource no longer exists. Reload to review.",
        ApiException { StatusCode: 409 } api => Refusal(api, "The current resource state does not permit this action. Reload and review its source, target and active runs."),
        ApiException { StatusCode: 400 or 422 } api => Refusal(api, "The server rejected these values. Review the fields and selected identities."),
        ApiException { StatusCode: 429 } => "The server asked you to wait. Try again shortly.",
        ApiException { StatusCode: 501 } => "This capability is unavailable on the server.",
        ApiException { StatusCode: >= 400 and < 500 } api => Refusal(api, "The server refused this request. Review the selection and try again."),
        AdminResourceTooManyException => error.Message,
        ArgumentException or InvalidOperationException => error.Message,
        InvalidDataException => "The server returned incomplete or inconsistent data. Reload before making changes.",
        _ => "The request did not finish. Check the connection and reload.",
    };

    /// <summary>The server's own problem detail and field errors say what to correct; a refusal without safe text falls back to the generic wording.</summary>
    private static string Refusal(ApiException error, string fallback)
    {
        var shown = error.ProblemDetail is { Length: > 0 } ? ApiProblemText.Display(error) : "";
        var fields = error.FieldErrors.Where(field => !string.IsNullOrWhiteSpace(field.Detail)).Select(ApiProblemText.FieldDetail)
            .Where(text => text.Length > 0 && !string.Equals(text, shown, StringComparison.Ordinal)).Distinct().Take(3).ToArray();
        var text = string.Join(" ", new[] { shown }.Concat(fields).Where(part => part.Length > 0));
        return text.Length == 0 ? fallback : text;
    }
}
