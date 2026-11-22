using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The official node form's exact omission/null semantics; proxy edits never clear hidden GPU overrides.</summary>
public sealed class AdminPlaybackNodeEditor
{
    public string Name { get; set; } = "";
    private string _url = "", _publicUrl = "";
    private bool _urlEdited, _publicUrlEdited;
    // A stored address this form cannot show (credentials, a query, no scheme) is left out of the form, so an untouched field must leave it alone instead of saving blank over it.
    private readonly bool _urlWithheld, _publicUrlWithheld;
    public string Url { get => _url; set { if (value != _url) { _url = value; _urlEdited = true; } } }
    public string PublicUrl { get => _publicUrl; set { if (value != _publicUrl) { _publicUrl = value; _publicUrlEdited = true; } } }
    public string Group { get; set; } = "";
    public string MaxJobs { get; set; } = "";
    public string MaxBandwidthMbps { get; set; } = "";
    public string HwAccelOverride { get; set; } = "inherit";
    public string HwDeviceOverride { get; set; } = "";
    public string SourceAccess { get; set; } = "local";
    public string Type { get; }
    public AdminPlaybackNode? Target { get; }
    /// <summary>A field the node declares from its own environment (official #4499): shown read-only and never part of a write.</summary>
    public bool IsLocked(string field) => Target?.IsEnvManaged(field) == true;
    /// <summary>The acceleration choices a node may be pinned to (inherit first): the server's hwAccelOverrideValues, which has no VideoToolbox unlike the cluster-wide playback.hw_accel.</summary>
    public static IReadOnlyList<string> HwAccelOverrideValues { get; } = Array.AsReadOnly(new[] { "inherit", "auto", "qsv", "vaapi", "nvenc", "none" });
    public AdminPlaybackNodeEditor(string type, AdminPlaybackNode? target, string group = "")
    {
        if (type is not ("proxy" or "transcode")) throw new ArgumentException("Invalid node type.");
        Type = type; Target = target;
        Name = target?.Name ?? ""; Group = target?.Group ?? group;
        _urlWithheld = target != null && !ValidUrl(target.Url ?? ""); _publicUrlWithheld = target != null && !string.IsNullOrEmpty(target.PublicUrl) && !ValidUrl(target.PublicUrl);
        _url = ValidUrl(target?.Url ?? "") ? target!.Url : ""; _publicUrl = ValidUrl(target?.PublicUrl ?? "") ? target!.PublicUrl! : "";
        MaxJobs = target?.MaxJobs?.ToString(CultureInfo.InvariantCulture) ?? "";
        MaxBandwidthMbps = target?.MaxBandwidthKbps is > 0 ? (target.MaxBandwidthKbps.Value / 1000m).ToString(CultureInfo.InvariantCulture) : "";
        // A stored empty override means inherit, as on the card; left as "" it matches no choice and every edit of the node would be refused.
        HwAccelOverride = string.IsNullOrWhiteSpace(target?.HwAccelOverride) ? "inherit" : target!.HwAccelOverride!; HwDeviceOverride = target?.HwDeviceOverride ?? "";
        SourceAccess = target?.SourceAccess ?? "local";
    }
    public IReadOnlyDictionary<string, object?> Capture(bool sourceAccessSupported)
    {
        // A locked field is neither validated nor sent: the node owns it, so whatever is stored stays, even a value this form would refuse.
        if (!IsLocked("name") && (string.IsNullOrWhiteSpace(Name) || Name.Length > 1024) || !IsLocked("group") && Group.Length > 1024) throw new ArgumentException("Enter a node name and a group of at most 1024 characters.");
        var keepUrl = IsLocked("url") || _urlWithheld && !_urlEdited; var keepPublicUrl = IsLocked("public_url") || _publicUrlWithheld && !_publicUrlEdited;
        if (!keepUrl && !ValidUrl(Url) || Type == "proxy" && !keepPublicUrl && PublicUrl.Length > 0 && !ValidUrl(PublicUrl)) throw new ArgumentException("Enter an HTTP or HTTPS address without credentials, a query or a fragment.");
        var values = new Dictionary<string, object?>();
        if (!IsLocked("name")) values["name"] = Name;
        if (!IsLocked("group")) values["group"] = Group.Trim();
        if (!IsLocked("max_jobs"))
            values["max_jobs"] = MaxJobs.Trim().Length == 0 ? 0 : int.TryParse(MaxJobs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : throw new ArgumentException("Enter a whole-number capacity, 0 or more.");
        // The egress limit is part of the body for every node type, as on the official form; a proxy's is the only one the form shows.
        if (!IsLocked("max_bandwidth_kbps"))
        {
            var bandwidth = MaxBandwidthMbps.Trim().Length == 0 ? 0m : decimal.TryParse(MaxBandwidthMbps.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mbps) && mbps >= 0 && mbps <= int.MaxValue / 1000m ? mbps : throw new ArgumentException("Enter an egress limit in Mbps, 0 or more.");
            values["max_bandwidth_kbps"] = (int)decimal.Round(bandwidth * 1000, 0, MidpointRounding.AwayFromZero);
        }
        if (!keepUrl) values["url"] = Url.Trim();
        if (Target == null) values["type"] = Type;
        if (Type == "proxy" && !keepPublicUrl && (Target != null || PublicUrl.Trim().Length > 0)) values["public_url"] = PublicUrl.Trim().Length == 0 ? null : PublicUrl.Trim();
        if (Type == "transcode" && Target != null)
        {
            if (!IsLocked("hw_accel_override"))
            {
                if (!HwAccelOverrideValues.Contains(HwAccelOverride)) throw new ArgumentException("Choose a hardware acceleration policy.");
                values["hw_accel_override"] = HwAccelOverride == "inherit" ? null : HwAccelOverride;
            }
            if (!IsLocked("hw_device_override"))
            {
                if (HwDeviceOverride.Length > 8192) throw new ArgumentException("The device list is too long.");
                var devices = AdminPlaybackSettingsRules.ParseDevices(HwDeviceOverride);
                values["hw_device_override"] = devices.Count == 0 ? null : string.Join(",", devices);
            }
        }
        if (Type == "transcode" && sourceAccessSupported && !IsLocked("source_access") && (Target != null || SourceAccess != "local"))
        {
            if (SourceAccess is not ("local" or "core")) throw new ArgumentException("Choose local mounts or core source access.");
            values["source_access"] = SourceAccess;
        }
        return values;
    }
    public static bool ValidUrl(string text) => text.Length <= 8192 && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var url)
        && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0;
    public override string ToString() => nameof(AdminPlaybackNodeEditor);
}
