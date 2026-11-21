using System.Globalization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Models.Admin;

// Official source: Silo-Server/silo-server 66b836f82 (web/src/pages/admin-settings/AutoscanSettings.tsx,
// web/src/pages/admin/autoscan/ConnectionsPanel.tsx, internal/apiv2/admin_autoscan_*.go).
public sealed record AdminAutoscanDefaults(bool Enabled, int DefaultPollIntervalSeconds, int DebounceSeconds);
public sealed record AdminAutoscanSettingsReceipt(AdminAutoscanDefaults Settings, string RescheduleState);
public sealed record AdminAutoscanSavedConnection(string Id, string Name, string Kind, string? BaseUrl, string? RequestIntegrationId, bool HasApiKey);
public sealed record AdminAutoscanRequestServer(string Id, string Name, string Kind);
/// <summary>Advisory check result; <see cref="Error"/> is the server's already-redacted, bounded text.</summary>
public sealed record AdminAutoscanConnectionCheckResult(bool Ok, string? Version, string? Error = null);

/// <summary>Server limits (internal/apiv2/admin_autoscan_connection_create.go).</summary>
public static class AdminAutoscanLimits
{
    public const int Name = 1024, Kind = 256, BaseUrl = 8192, ApiKey = 8192, Id = 256;
    public const int MaxInterval = int.MaxValue, MaxDebounce = int.MaxValue;
}

/// <summary>The official <c>Number(value) || fallback</c> coercion of the two polling inputs.</summary>
public static class AdminAutoscanNumber
{
    public static string Coerce(string? text, int fallback)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || value == 0)
            return fallback.ToString(CultureInfo.InvariantCulture);
        if (double.IsInfinity(value)) return value > 0 ? "Infinity" : "-Infinity";
        // JavaScript prints integral values below 1e21 without an exponent.
        return Math.Abs(value) < 1e21 && value == Math.Floor(value) ? value.ToString("F0", CultureInfo.InvariantCulture) : value.ToString("R", CultureInfo.InvariantCulture);
    }
}

public sealed record AdminAutoscanConnectionEditor
{
    public string? Id { get; init; }
    public bool Reuse { get; init; }
    public string IntegrationId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "sonarr";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public bool HasApiKey { get; init; }
    public bool CanSave => Reuse ? !string.IsNullOrWhiteSpace(IntegrationId) : !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(BaseUrl) && (!string.IsNullOrWhiteSpace(ApiKey) || HasApiKey);
    public bool CanTest => Reuse ? IntegrationId.Length > 0 || Id != null : BaseUrl.Trim().Length > 0 || Id != null;
    public override string ToString() => nameof(AdminAutoscanConnectionEditor);

    /// <summary>
    /// The official create/update body. A linked connection sends no URL or key; an own connection sends
    /// the key only when one was typed, so an untouched key means "keep" and there is no clear operation.
    /// </summary>
    public Dictionary<string, string> SaveBody(IReadOnlyList<AdminAutoscanRequestServer> servers)
    {
        if (!CanSave) throw new ArgumentException("Complete the connection fields.");
        if (Reuse)
        {
            var linked = servers.FirstOrDefault(server => server.Id == IntegrationId);
            // A saved link whose Requests server is no longer listed keeps its own saved name and kind.
            var name = linked?.Name ?? (Name.Trim().Length > 0 ? Name.Trim() : IntegrationId);
            var kind = linked?.Kind ?? (Kind.Trim().Length > 0 ? Kind.Trim() : "sonarr");
            if (IntegrationId.Length > AdminAutoscanLimits.Id || name.Length > AdminAutoscanLimits.Name || kind.Length > AdminAutoscanLimits.Kind)
                throw new ArgumentException("Connection field is too long.");
            return new() { ["name"] = name, ["kind"] = kind, ["request_integration_id"] = IntegrationId };
        }
        var trimmedKind = Kind.Trim();
        if (trimmedKind.Length == 0) throw new ArgumentException("Select Sonarr or Radarr.");
        if (Name.Trim().Length > AdminAutoscanLimits.Name || BaseUrl.Trim().Length > AdminAutoscanLimits.BaseUrl
            || ApiKey.Trim().Length > AdminAutoscanLimits.ApiKey || trimmedKind.Length > AdminAutoscanLimits.Kind)
            throw new ArgumentException("Connection field is too long.");
        var body = new Dictionary<string, string> { ["name"] = Name.Trim(), ["kind"] = trimmedKind, ["base_url"] = BaseUrl.Trim() };
        if (ApiKey.Trim().Length > 0) body["api_key_ref"] = ApiKey.Trim();
        return body;
    }

    /// <summary>The official advisory-check branches (ConnectionsPanel handleTest).</summary>
    public Dictionary<string, string> TestBody()
    {
        if (!CanTest) throw new ArgumentException("Complete the connection fields.");
        if (Id != null && (Reuse || BaseUrl.Trim().Length == 0 || ApiKey.Trim().Length == 0)) return new() { ["connection_id"] = Id };
        if (Reuse) return new() { ["request_integration_id"] = IntegrationId };
        if (BaseUrl.Trim().Length > AdminAutoscanLimits.BaseUrl || ApiKey.Trim().Length > AdminAutoscanLimits.ApiKey)
            throw new ArgumentException("Connection field is too long.");
        var body = new Dictionary<string, string> { ["base_url"] = BaseUrl.Trim() };
        if (ApiKey.Trim().Length > 0) body["api_key_ref"] = ApiKey.Trim();
        return body;
    }

    /// <summary>
    /// A saved base URL for the table. Embedded credentials, query strings and fragments are never displayed;
    /// the editor still starts from the raw saved value so an unrelated edit does not blank or rewrite it.
    /// </summary>
    public static string DisplayUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            && uri.Scheme is "http" or "https")
            return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path);
        return value;
    }
}

public sealed record AdminAutoscanSettingsState
{
    public override string ToString() => nameof(AdminAutoscanSettingsState);
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Busy { get; init; }
    public bool Testing { get; init; }
    public AdminAutoscanDefaults? Saved { get; init; }
    public string Interval { get; init; } = "";
    public string Debounce { get; init; } = "";
    public bool ConnectionsLoaded { get; init; }
    public bool ServersLoaded { get; init; }
    public IReadOnlyList<AdminAutoscanSavedConnection> Connections { get; init; } = [];
    public IReadOnlyList<AdminAutoscanRequestServer> Servers { get; init; } = [];
    public AdminAutoscanConnectionEditor? Editor { get; init; }
    public AdminAutoscanSavedConnection? DeleteTarget { get; init; }
    public AdminAutoscanConnectionCheckResult? TestResult { get; init; }
    public string? TestError { get; init; }
    /// <summary>The newest write's refusal (retry after correcting) or unknown-outcome text.</summary>
    public string? Error { get; init; }
    public string? SettingsError { get; init; }
    public string? ConnectionsError { get; init; }
    public string? ServersError { get; init; }
    /// <summary>A write was not confirmed (or its target changed): writes stay blocked until Reload re-reads the server.</summary>
    public bool NeedsReview { get; init; }
    public bool OutcomeUnknown { get; init; }
    public string? RescheduleState { get; init; }
    public string? Notice { get; init; }
    public bool Dirty => Saved != null && (Interval != Saved.DefaultPollIntervalSeconds.ToString(CultureInfo.InvariantCulture)
        || Debounce != Saved.DebounceSeconds.ToString(CultureInfo.InvariantCulture));
    public bool CanWrite => Owner?.IsCurrent == true && !Loading && !Busy && !Testing && !NeedsReview;
}
