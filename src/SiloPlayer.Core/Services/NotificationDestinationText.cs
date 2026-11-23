using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The wording of a destination row and the destination-related inbox notices, as official silo-server
/// 3b2b32168 words them (PluginDestinationsSection.tsx, NotificationInbox.tsx, notificationReasonFields.ts).
/// Keys are the official catalog keys; the English fallback is the official text.
/// </summary>
public static class NotificationDestinationText
{
    public static string NextUpLabel() => AppStrings.Get("web.settings.notifications.reason.nextUp", fallback: "Next Up");
    public static string RequestsLabel() => AppStrings.Get("web.settings.notifications.reason.requests", fallback: "Requests and watchlist");

    /// <summary>"All reasons", the enabled reasons joined by " · ", or "No reasons selected".</summary>
    public static string ReasonsText(bool nextUp, bool requests)
    {
        var labels = new List<string>();
        if (nextUp) labels.Add(NextUpLabel());
        if (requests) labels.Add(RequestsLabel());
        return labels.Count == 2 ? AppStrings.Get("web.settings.notifications.webhooks.allReasons", fallback: "All reasons")
            : labels.Count > 0 ? string.Join(" · ", labels)
            : AppStrings.Get("web.settings.notifications.webhooks.noReasons", fallback: "No reasons selected");
    }

    /// <summary>"3m ago", "2h ago", "5d ago" or "just now"; null when there is no usable instant.</summary>
    public static string? RelativeTime(string? iso, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var then)) return null;
        var elapsed = (now ?? DateTimeOffset.UtcNow) - then;
        var minutes = Round(elapsed.TotalMinutes);
        if (minutes < 1) return AppStrings.Get("web.common.date.justNow", fallback: "just now");
        if (minutes < 60) return Ago("minutesAgo", minutes, "m");
        var hours = Round(elapsed.TotalHours);
        if (hours < 24) return Ago("hoursAgo", hours, "h");
        return Ago("daysAgo", Round(elapsed.TotalDays), "d");
    }

    private static long Round(double value) => (long)Math.Floor(value + 0.5);

    private static string Ago(string key, long count, string unit) => AppStrings.Get("web.common.date." + key,
        new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString(AppStrings.FormatCulture) }, count + unit + " ago");

    /// <summary>The newest outcome is a failure: it happened, and no later success did.</summary>
    public static bool IsFailing(NotificationPluginDestination destination)
    {
        if (destination.LastFailureAt == null) return false;
        if (destination.LastSuccessAt == null) return true;
        return DateTimeOffset.TryParse(destination.LastFailureAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var failure)
            && DateTimeOffset.TryParse(destination.LastSuccessAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var success)
            ? failure > success : string.CompareOrdinal(destination.LastFailureAt, destination.LastSuccessAt) > 0;
    }

    public static string? LastSuccessText(NotificationPluginDestination destination, DateTimeOffset? now = null)
        => RelativeTime(destination.LastSuccessAt, now) is { } when && !IsFailing(destination)
            ? AppStrings.Get("web.settings.notifications.webhooks.lastSuccess", new Dictionary<string, object?> { ["when"] = when }, "Last success: " + when) : null;

    /// <summary>The amber line: the reason the destination was turned off, or its last failure.</summary>
    public static string? WarningText(NotificationPluginDestination destination, DateTimeOffset? now = null)
    {
        if (!IsFailing(destination) && string.IsNullOrEmpty(destination.DisabledReason)) return null;
        if (!string.IsNullOrEmpty(destination.DisabledReason))
            return AppStrings.Get("web.settings.notifications.webhooks.disabledReason", new Dictionary<string, object?> { ["reason"] = destination.DisabledReason }, "Disabled: " + destination.DisabledReason);
        var when = RelativeTime(destination.LastFailureAt, now);
        var message = destination.LastFailureMessage;
        if (!string.IsNullOrEmpty(message))
            return when != null
                ? AppStrings.Get("web.settings.notifications.destinations.lastFailureAt", new Dictionary<string, object?> { ["when"] = when, ["message"] = message }, $"Last failure {when}: {message}")
                : AppStrings.Get("web.settings.notifications.destinations.lastFailure", new Dictionary<string, object?> { ["message"] = message }, "Last failure: " + message);
        return when != null
            ? AppStrings.Get("web.settings.notifications.destinations.lastFailureAtUnknown", new Dictionary<string, object?> { ["when"] = when }, $"Last failure {when}: the plugin couldn't send")
            : AppStrings.Get("web.settings.notifications.destinations.lastFailureUnknown", fallback: "Last failure: the plugin couldn't send");
    }

    public static string TestText(NotificationWebhookTestResult result)
    {
        var outcome = result.Ok ? "ok" : "failed";
        return string.IsNullOrEmpty(result.Message)
            ? AppStrings.Get("web.settings.notifications.destinations.testResult", new Dictionary<string, object?> { ["outcome"] = outcome }, result.Ok ? "Test sent" : "Test failed")
            : AppStrings.Get("web.settings.notifications.destinations.testResultDetail", new Dictionary<string, object?> { ["outcome"] = outcome, ["message"] = result.Message }, (result.Ok ? "Test sent: " : "Test failed: ") + result.Message);
    }

    public static string LimitText(int max) => AppStrings.Get("web.settings.notifications.destinations.limit",
        new Dictionary<string, object?> { ["count"] = max, ["count_text"] = max.ToString(AppStrings.FormatCulture) },
        max == 1 ? $"Limit of {max} destination reached" : $"Limit of {max} destinations reached");

    /// <summary>The plugin's or server's own reason for a refusal, else the exception text, else the fallback.</summary>
    public static string ErrorMessage(Exception error, string fallback)
        => error is Api.ApiException api
            ? api.FieldErrors.Count > 0 && !string.IsNullOrEmpty(api.FieldErrors[0].Detail) ? api.FieldErrors[0].Detail! : string.IsNullOrEmpty(api.Message) ? fallback : api.Message
            : string.IsNullOrEmpty(error.Message) ? fallback : error.Message;

    /// <summary>The plugin's refusal of the settings as a whole (location body.settings), shown as sent under the fields.</summary>
    public static string? SettingsRejection(Exception error)
        => error is Api.ApiException api
            ? api.FieldErrors.FirstOrDefault(item => item.Location == "body.settings")?.Detail is { Length: > 0 } detail ? detail : null
            : null;

    /// <summary>The server's field errors at body.settings.&lt;key&gt;, by key, the first per key; null when none names a setting.</summary>
    public static Dictionary<string, string>? SettingsFieldErrors(Exception error)
    {
        if (error is not Api.ApiException api) return null;
        const string prefix = "body.settings.";
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in api.FieldErrors)
        {
            if (item.Location is not { } location || !location.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var key = location[prefix.Length..];
            if (key.Length > 0) errors.TryAdd(key, ApiProblemText.FieldDetail(item));
        }
        return errors.Count > 0 ? errors : null;
    }

    /// <summary>The server's error on the destination's name, such as one already in use.</summary>
    public static string? NameError(Exception error)
        => error is Api.ApiException api && api.FieldErrors.FirstOrDefault(item => item.Location == "body.name") is { } item
            ? ApiProblemText.FieldDetail(item) : null;

    /// <summary>Where a failed add or save is shown: under the name, under its fields, under the form, or as a toast.</summary>
    public static DestinationSaveFailure Classify(Exception error, bool hasForm)
    {
        if (NameError(error) is { } name) return new(name, null, null, null);
        var perField = hasForm ? SettingsFieldErrors(error) : null;
        var rejection = hasForm ? SettingsRejection(error) : null;
        return perField == null && rejection == null
            ? new(null, null, null, ErrorMessage(error, AppStrings.Get("web.settings.notifications.destinations.saveFailed", fallback: "The destination couldn't be saved")))
            : new(null, perField, rejection, null);
    }
}

public sealed record DestinationSaveFailure(string? NameError, Dictionary<string, string>? FieldErrors, string? SettingsRejection, string? ToastMessage);
