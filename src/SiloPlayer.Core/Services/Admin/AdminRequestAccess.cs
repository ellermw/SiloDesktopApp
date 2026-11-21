using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One layer's request approval and limit: an access group's own settings.</summary>
public sealed record AdminRequestLayer(string LimitMode, int? MaxRequests, int? WindowDays, string ApprovalMode)
{
    public static AdminRequestLayer From(JsonElement value) => new(value.Text("limit_mode", "inherit"), Whole(value, "max_requests"), Whole(value, "window_days"), value.Text("approval_mode", "inherit"));
    private static int? Whole(JsonElement value, string key) => value.Member(key) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var result) ? result : null;
}

public sealed record AdminRequestServerDefaults(bool RequestsEnabled, int GlobalMaxRequests, int GlobalWindowDays, bool GlobalAutoApproval)
{
    public static AdminRequestServerDefaults From(JsonElement value) => new(value.Flag("requests_enabled"), value.Number("global_max_requests"), value.Number("global_window_days"), value.Flag("global_auto_approval_enabled"));
}

/// <summary>Where a resolved request value comes from: <c>account</c>, <c>group</c> (with the group's name) or <c>server</c>.</summary>
public sealed record AdminRequestSource(string Kind, string Name = "");
public sealed record AdminRequestQuota(bool Unlimited, int Max, int Days);
public sealed record AdminRequestTerms(AdminRequestQuota Quota, AdminRequestSource QuotaSource, bool AutoApprove, AdminRequestSource ApprovalSource);
public sealed record AdminPolicyChange(string Label, string From, string To);

/// <summary>
/// Official 217ff25 <c>web/src/lib/requestAccess.ts</c> and <c>accessGroupPolicyChanges.ts</c> request rules: layers resolve first-to-last over the
/// server-wide defaults, as the server does. Text comes from the official catalog.
/// </summary>
public static class AdminRequestAccess
{
    private static readonly AdminRequestSource Server = new("server");
    private static string T(string key, params (string Name, object? Value)[] args) => AdminUserResourceText.T("web.admin.common.requestAccess." + key, args);
    private static int WindowDays(AdminRequestServerDefaults server) => server.GlobalWindowDays > 0 ? server.GlobalWindowDays : 7;

    public static AdminRequestTerms Resolve(IReadOnlyList<(AdminRequestSource Source, AdminRequestLayer Layer)> layers, AdminRequestServerDefaults server)
    {
        var quota = new AdminRequestQuota(false, server.GlobalMaxRequests, WindowDays(server)); var quotaSource = Server;
        foreach (var (source, layer) in layers)
        {
            if (layer.LimitMode is not ("custom" or "unlimited")) continue;
            quotaSource = source;
            quota = layer.LimitMode == "unlimited" ? new(true, 0, 0)
                : new(false, layer.MaxRequests ?? server.GlobalMaxRequests, layer.WindowDays is > 0 ? layer.WindowDays.Value : WindowDays(server));
            break;
        }
        var auto = server.GlobalAutoApproval; var approvalSource = Server;
        foreach (var (source, layer) in layers)
        {
            if (layer.ApprovalMode is not ("manual" or "auto")) continue;
            approvalSource = source; auto = layer.ApprovalMode == "auto"; break;
        }
        return new(quota, quotaSource, auto, approvalSource);
    }

    /// <summary>"Kids group", or a name that already says it, as "Default Group".</summary>
    public static string GroupLabel(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return T("unknownGroup");
        return Regex.IsMatch(trimmed, @"\bgroup$", RegexOptions.IgnoreCase) ? trimmed : T("group", ("name", trimmed));
    }

    public static string FormatQuota(AdminRequestQuota quota, string? source = null)
    {
        if (quota.Unlimited) return source == null ? T("noLimit") : T("noLimitFrom", ("source", source));
        var perWindow = T("perWindow", ("days", quota.Days), ("days_text", quota.Days));
        if (quota.Max == 0) return source == null ? T("noNewRequests", ("per_window", perWindow)) : T("noNewRequestsFrom", ("per_window", perWindow), ("source", source));
        return source == null ? T("quota", ("count", quota.Max), ("count_text", quota.Max), ("per_window", perWindow))
            : T("quotaFrom", ("count", quota.Max), ("count_text", quota.Max), ("per_window", perWindow), ("source", source));
    }
    public static string FormatApproval(bool autoApprove) => T(autoApprove ? "approvedAutomatically" : "adminApproves");
    public static string FormatSource(AdminRequestSource source) => source.Kind switch
    {
        "account" => T("sourceAccount"), "group" => T("sourceGroup", ("group", GroupLabel(source.Name))), _ => T("sourceServer"),
    };

    /// <summary>What "Use the default" means for one field: the value and which layer supplies it.</summary>
    public static string DescribeInherited(string value, AdminRequestSource source, string? groupName)
    {
        if (source.Kind == "group") return T("inheritedFromGroup", ("group", GroupLabel(source.Name)), ("value", value));
        if (groupName != null) return T("inheritedViaGroup", ("group", GroupLabel(groupName)), ("value", value));
        return T("inheritedFromServer", ("value", value));
    }

    /// <summary>A short line for an access group card, e.g. "Admin approves · 3 per 7 days".</summary>
    public static string GroupSummary(AdminRequestLayer layer)
    {
        var parts = new List<string>();
        if (layer.ApprovalMode == "manual") parts.Add(T("summaryAdminApproves"));
        if (layer.ApprovalMode == "auto") parts.Add(T("summaryAutoApproves"));
        if (layer.LimitMode == "unlimited") parts.Add(T("summaryNoLimit"));
        if (layer.LimitMode == "custom" && layer.MaxRequests is { } max)
            parts.Add(layer.WindowDays is not > 0 ? T("summaryCount", ("count", max), ("count_text", max))
                : T("summaryPerWindow", ("days", layer.WindowDays.Value), ("days_text", layer.WindowDays.Value), ("max", max)));
        return string.Join(" · ", parts);
    }

    /// <summary>How the request limit and approval that members inherit change between two groups (null for no group).</summary>
    public static IReadOnlyList<AdminPolicyChange> TermsChanges(AdminRequestLayer? before, AdminRequestLayer? after, AdminRequestServerDefaults server)
    {
        AdminRequestTerms Terms(AdminRequestLayer? layer) => Resolve(layer == null ? [] : [(new AdminRequestSource("group", ""), layer)], server);
        var a = Terms(before); var b = Terms(after); var changes = new List<AdminPolicyChange>();
        var quotaFrom = FormatQuota(a.Quota); var quotaTo = FormatQuota(b.Quota);
        if (quotaFrom != quotaTo) changes.Add(new(AdminUserResourceText.T("web.admin.access.groups.changes.requestLimit"), quotaFrom, quotaTo));
        if (a.AutoApprove != b.AutoApprove) changes.Add(new(AdminUserResourceText.T("web.admin.access.groups.changes.requestApproval"), FormatApproval(a.AutoApprove), FormatApproval(b.AutoApprove)));
        return changes;
    }
}

/// <summary>An editor's staged approval and limit; the numbers stay text while typed. Official <c>RequestLimitDraft</c>.</summary>
public sealed record AdminRequestLimitDraft(string Approval, string Limit, string MaxRequests, string WindowDays)
{
    /// <summary>The editor offers no "blocked" mode: a stored one reads as inherit, as the official draft does.</summary>
    public static AdminRequestLimitDraft From(JsonElement layer)
    {
        string Choice(string key) => layer.Text(key, "inherit") is "blocked" ? "inherit" : layer.Text(key, "inherit");
        string Raw(string key) => layer.Member(key) is { ValueKind: JsonValueKind.Number } number ? number.GetRawText() : "";
        return new(Choice("approval_mode"), Choice("limit_mode"), Raw("max_requests"), Raw("window_days"));
    }

    /// <summary>How many staged edits differ from <paramref name="baseline"/>; the numbers count only for a custom limit.</summary>
    public int Changes(AdminRequestLimitDraft baseline)
    {
        var changes = 0;
        if (Approval != baseline.Approval) changes++;
        if (Limit != baseline.Limit) changes++;
        if (Limit == "custom")
        {
            if (MaxRequests != baseline.MaxRequests) changes++;
            if (WindowDays != baseline.WindowDays) changes++;
        }
        return changes;
    }

    public static int? WholeNumber(string text, int minimum)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(c => c is < '0' or > '9')) return null;
        return int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= minimum ? value : null;
    }
    public string? MaxRequestsError => Limit == "custom" && WholeNumber(MaxRequests, 0) == null ? AdminUserResourceText.T("web.admin.common.requestAccess.errorMaxRequests") : null;
    public string? WindowDaysError => Limit == "custom" && WholeNumber(WindowDays, 1) == null ? AdminUserResourceText.T("web.admin.common.requestAccess.errorWindowDays") : null;
    public bool HasErrors => MaxRequestsError != null || WindowDaysError != null;
    /// <summary>A custom limit of zero stops new requests without blocking the account.</summary>
    public bool IsZeroLimit => Limit == "custom" && WholeNumber(MaxRequests, 0) == 0;

    /// <summary>What a save sends; only for a draft without errors.</summary>
    public JsonObject Body()
    {
        var custom = Limit == "custom";
        return new()
        {
            ["limit_mode"] = Limit, ["approval_mode"] = Approval,
            ["max_requests"] = custom ? WholeNumber(MaxRequests, 0) : null, ["window_days"] = custom ? WholeNumber(WindowDays, 1) : null,
        };
    }
}
