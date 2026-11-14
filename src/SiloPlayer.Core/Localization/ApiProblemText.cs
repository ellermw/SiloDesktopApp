using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// Presentation of v2 Problem Details (docs/architecture/i18n.md "Problem details").
/// In English the server's English text shows exactly as sent. In a translated
/// interface the catalog's <c>error.field.&lt;code&gt;</c> for the first field
/// error shows, then <c>error.&lt;type&gt;</c>, then the server's text. Machine
/// handling keeps using <see cref="ApiException.ErrorCode"/>, status, retry and
/// field codes; never branch on the text this returns.
/// </summary>
public static class ApiProblemText
{
    /// <summary>The display text for a failed request; <paramref name="english"/> overrides the server text when a screen words it itself.</summary>
    public static string Display(ApiException exception, string? english = null)
        => Display(exception.ProblemType, exception.FieldErrors, english ?? exception.Message);

    public static string Display(string? problemType, IReadOnlyList<ApiFieldError>? fieldErrors, string english)
    {
        var text = string.IsNullOrEmpty(problemType) ? english : AppStrings.ServerText("error." + problemType, null, english);
        var code = fieldErrors is { Count: > 0 } ? fieldErrors[0].Code : null;
        return string.IsNullOrEmpty(code) ? text : AppStrings.ServerText("error.field." + code, null, text);
    }

    /// <summary>One field error's text for display next to its field.</summary>
    public static string FieldDetail(ApiFieldError error)
        => AppStrings.ServerText("error.field." + error.Code, null, error.Detail ?? "");
}
