using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin.Users;

/// <summary>What an account's request approval and limit resolve to without its own record: its group's, else the server's (official <c>resolveRequestTerms</c> over the group layer).</summary>
public sealed record InheritedRequestTerms(bool AutoApprove, bool ApprovalFromGroup, bool Unlimited, long Max, long Days, bool QuotaFromGroup);

/// <summary>
/// The request-limit editor's draft (official <c>RequestLimitDraft</c>, <c>RequestLimitFields</c>): the approval, the limit and the numbers of a custom one,
/// kept as text while typed. Blocking is not offered here: that is the requests switch on the account.
/// </summary>
public sealed partial record AdminRequestLimitDraft(string Approval, string Limit, string MaxRequests, string WindowDays)
{
    public static AdminRequestLimitDraft From(JsonObject layer) => new(
        layer.Text("approval_mode") is "blocked" or "" ? "inherit" : layer.Text("approval_mode"),
        layer.Text("limit_mode") is "blocked" or "" ? "inherit" : layer.Text("limit_mode"),
        layer["max_requests"] is JsonValue max && UserJson.TryInteger(max, out var maxRequests) ? maxRequests.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
        layer["window_days"] is JsonValue window && UserJson.TryInteger(window, out var days) ? days.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");

    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    /// <summary>A whole number of at least <paramref name="min"/>, or null.</summary>
    public static long? Whole(string text, long min) => Digits().IsMatch(text.Trim()) && long.TryParse(text.Trim(), out var value) && value >= min ? value : null;

    public bool Custom => Limit == "custom";
    public bool MaxOk => !Custom || Whole(MaxRequests, 0) != null;
    public bool WindowOk => !Custom || Whole(WindowDays, 1) != null;
    public bool Valid => MaxOk && WindowOk;
    /// <summary>A custom limit of zero: nothing new can be requested, which is not a block.</summary>
    public bool IsZero => Custom && Whole(MaxRequests, 0) == 0;

    /// <summary>The body a save sends; call it only for a draft that is valid.</summary>
    public JsonObject Body() => new()
    {
        ["limit_mode"] = Limit, ["approval_mode"] = Approval,
        ["max_requests"] = Custom ? Whole(MaxRequests, 0) : null, ["window_days"] = Custom ? Whole(WindowDays, 1) : null,
    };

    /// <summary>How many staged edits differ from <paramref name="saved"/>; the numbers count only for a custom limit.</summary>
    public IReadOnlyList<string> Changes(AdminRequestLimitDraft saved)
    {
        var changed = new List<string>();
        if (Approval != saved.Approval) changed.Add("approval_mode");
        if (Limit != saved.Limit) changed.Add("limit_mode");
        if (Custom && MaxRequests != saved.MaxRequests) changed.Add("max_requests");
        if (Custom && WindowDays != saved.WindowDays) changed.Add("window_days");
        return changed;
    }
}

public static class AdminRequestTermsResolver
{
    /// <summary>The terms an account inherits: its group's own layer (none for an admin), else the server settings. Null while the group's or the server's record is not known.</summary>
    public static InheritedRequestTerms? Inherited(JsonObject user, JsonObject? groupLimit, JsonObject? settings)
    {
        if (settings == null) return null;
        var grouped = user.Text("role") != "admin" && user.Text("access_group_id").Length > 0;
        if (grouped && groupLimit == null) return null;
        var window = Math.Max(1, settings.Count("global_window_days") > 0 ? settings.Count("global_window_days") : 7);
        var max = settings.Count("global_max_requests");
        var unlimited = false;
        var quotaGroup = false;
        if (grouped && groupLimit!.Text("limit_mode") is "custom" or "unlimited")
        {
            quotaGroup = true;
            if (groupLimit.Text("limit_mode") == "unlimited") unlimited = true;
            else
            {
                if (groupLimit["max_requests"] != null) max = groupLimit.Count("max_requests");
                if (groupLimit.Count("window_days") > 0) window = groupLimit.Count("window_days");
            }
        }
        var auto = settings.Flag("global_auto_approval_enabled");
        var approvalGroup = false;
        if (grouped && groupLimit!.Text("approval_mode") is "manual" or "auto") { approvalGroup = true; auto = groupLimit.Text("approval_mode") == "auto"; }
        return new(auto, approvalGroup, unlimited, max, window, quotaGroup);
    }
}
