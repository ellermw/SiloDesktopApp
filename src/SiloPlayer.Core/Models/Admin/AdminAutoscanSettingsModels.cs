using System.Globalization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Models.Admin;

public sealed record AdminAutoscanDefaults(bool Enabled, int DefaultPollIntervalSeconds, int DebounceSeconds);
public sealed record AdminAutoscanSettingsReceipt(AdminAutoscanDefaults Settings, string RescheduleState);
public sealed record AdminAutoscanSavedConnection(string Id, string Name, string Kind, string? BaseUrl, string? RequestIntegrationId, bool HasApiKey);
public sealed record AdminAutoscanRequestServer(string Id, string Name, string Kind);
public sealed record AdminAutoscanConnectionCheck(bool Ok, string? Version);

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

    public Dictionary<string, string> SaveBody(IReadOnlyList<AdminAutoscanRequestServer> servers)
    {
        if (!CanSave) throw new ArgumentException("Complete the connection fields.");
        if (Reuse)
        {
            var linked = servers.FirstOrDefault(server => server.Id == IntegrationId);
            return new() { ["name"] = linked?.Name ?? IntegrationId, ["kind"] = linked?.Kind ?? "sonarr", ["request_integration_id"] = IntegrationId };
        }
        if (Kind is not ("sonarr" or "radarr")) throw new ArgumentException("Select Sonarr or Radarr.");
        if (!IsSafeUrl(BaseUrl.Trim())) throw new ArgumentException("Enter an HTTP or HTTPS base URL without embedded credentials, query or fragment.");
        if (Name.Trim().Length > 512 || BaseUrl.Length > 4096 || ApiKey.Length > 8192) throw new ArgumentException("Connection field is too long.");
        var body = new Dictionary<string, string> { ["name"] = Name.Trim(), ["kind"] = Kind, ["base_url"] = BaseUrl.Trim() };
        if (ApiKey.Trim().Length > 0) body["api_key_ref"] = ApiKey.Trim();
        return body;
    }

    public Dictionary<string, string> TestBody()
    {
        if (!CanTest) throw new ArgumentException("Complete the connection fields.");
        if (Id != null && (Reuse || BaseUrl.Trim().Length == 0 || ApiKey.Trim().Length == 0)) return new() { ["connection_id"] = Id };
        if (Reuse) return new() { ["request_integration_id"] = IntegrationId };
        if (!IsSafeUrl(BaseUrl.Trim())) throw new ArgumentException("Enter an HTTP or HTTPS base URL without embedded credentials, query or fragment.");
        var body = new Dictionary<string, string> { ["base_url"] = BaseUrl.Trim() };
        if (ApiKey.Trim().Length > 0) body["api_key_ref"] = ApiKey.Trim();
        return body;
    }

    public static bool IsSafeUrl(string value) => value.Length <= 4096 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
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
    public AdminAutoscanConnectionCheck? TestResult { get; init; }
    public string? TestError { get; init; }
    public string? Error { get; init; }
    public string? ConnectionsError { get; init; }
    public string? ServersError { get; init; }
    public bool NeedsReview { get; init; }
    public bool OutcomeUnknown { get; init; }
    public string? RescheduleState { get; init; }
    public string? Notice { get; init; }
    public bool Dirty => Saved != null && (Interval != Saved.DefaultPollIntervalSeconds.ToString(CultureInfo.InvariantCulture)
        || Debounce != Saved.DebounceSeconds.ToString(CultureInfo.InvariantCulture));
    public bool CanWrite => Owner?.IsCurrent == true && !Loading && !Busy && !Testing && !NeedsReview;
}

