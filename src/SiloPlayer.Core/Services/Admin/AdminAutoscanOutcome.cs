using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>What became of one Autoscan write.</summary>
public enum AdminAutoscanOutcome
{
    /// <summary>Nothing was sent: the page, account or server changed, or another change or read was in progress.</summary>
    Skipped,
    Succeeded,
    /// <summary>The server (or this client, before sending) definitely refused the change. Nothing was written; correct it and submit again.</summary>
    Refused,
    /// <summary>A timeout, transport failure or unusable answer after sending: the change may or may not have been applied.</summary>
    Unknown,
}

/// <summary>One write's outcome with the text the page shows for it.</summary>
public sealed record AdminAutoscanResult<T>(AdminAutoscanOutcome Outcome, T? Value = null, string? Message = null) where T : class
{
    public bool Succeeded => Outcome == AdminAutoscanOutcome.Succeeded;
    public static AdminAutoscanResult<T> Skipped { get; } = new(AdminAutoscanOutcome.Skipped);
}

public static class AdminAutoscanFailures
{
    /// <summary>Longest server text shown; applied after redaction.</summary>
    public const int MessageLimit = 300;

    /// <summary>A 4xx answer other than 408 is a definite refusal.</summary>
    public static bool IsRefusal(Exception error) => error is ApiException { StatusCode: >= 400 and < 500 and not 408 };

    /// <summary>
    /// The server's reason for a refusal. A schema failure says "see errors", so the
    /// first field's detail is used instead. Known secrets are replaced and the text is
    /// redacted before it is truncated, so a secret cannot survive by straddling the limit.
    /// </summary>
    public static string RefusalText(ApiException error, IEnumerable<string?>? secrets = null)
    {
        var detail = error.ProblemDetail;
        if (string.IsNullOrWhiteSpace(detail) || Regex.IsMatch(detail, "see errors", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var field = error.FieldErrors.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Detail));
            detail = field != null ? ApiProblemText.FieldDetail(field) : ApiProblemText.Display(error);
        }
        return Redact(detail, secrets);
    }

    public static string Redact(string? text, IEnumerable<string?>? secrets = null)
    {
        var value = text ?? "";
        foreach (var secret in secrets ?? [])
            if (!string.IsNullOrEmpty(secret) && secret.Length >= 4) value = value.Replace(secret, "<redacted>", StringComparison.Ordinal);
        value = LocalLog.RedactDiagnosticText(value).Trim();
        return value.Length > MessageLimit ? value[..MessageLimit] + "…" : value;
    }
}
