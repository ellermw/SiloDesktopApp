using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The official node form's exact omission/null semantics; proxy edits never clear hidden GPU overrides.</summary>
public sealed class AdminPlaybackNodeEditor
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string PublicUrl { get; set; } = "";
    public string Group { get; set; } = "";
    public string MaxJobs { get; set; } = "";
    public string MaxBandwidthMbps { get; set; } = "";
    public string HwAccelOverride { get; set; } = "inherit";
    public string HwDeviceOverride { get; set; } = "";
    public string SourceAccess { get; set; } = "local";
    public string Type { get; }
    public AdminPlaybackNode? Target { get; }
    public AdminPlaybackNodeEditor(string type, AdminPlaybackNode? target, string group = "")
    {
        if (type is not ("proxy" or "transcode")) throw new ArgumentException("Invalid node type.");
        Type = type; Target = target;
        Name = target?.Name ?? ""; Url = ValidUrl(target?.Url ?? "") ? target!.Url : ""; PublicUrl = ValidUrl(target?.PublicUrl ?? "") ? target!.PublicUrl! : ""; Group = target?.Group ?? group;
        MaxJobs = target?.MaxJobs?.ToString(CultureInfo.InvariantCulture) ?? "";
        MaxBandwidthMbps = target?.MaxBandwidthKbps is > 0 ? (target.MaxBandwidthKbps.Value / 1000m).ToString(CultureInfo.InvariantCulture) : "";
        HwAccelOverride = target?.HwAccelOverride ?? "inherit"; HwDeviceOverride = target?.HwDeviceOverride ?? "";
        SourceAccess = target?.SourceAccess ?? "local";
    }
    public IReadOnlyDictionary<string, object?> Capture(bool sourceAccessSupported)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 1024 || Group.Length > 1024) throw new ArgumentException("Enter a node name and a group of at most 1024 characters.");
        if (!ValidUrl(Url) || Type == "proxy" && PublicUrl.Length > 0 && !ValidUrl(PublicUrl)) throw new ArgumentException("Enter an HTTP or HTTPS address without credentials, a query or a fragment.");
        var jobs = MaxJobs.Trim().Length == 0 ? 0 : int.TryParse(MaxJobs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : throw new ArgumentException("Enter a whole-number capacity, 0 or more.");
        var bandwidth = MaxBandwidthMbps.Trim().Length == 0 ? 0m : decimal.TryParse(MaxBandwidthMbps, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mbps) && mbps >= 0 && mbps * 1000 <= int.MaxValue ? mbps : throw new ArgumentException("Enter an egress limit in Mbps, 0 or more.");
        var values = new Dictionary<string, object?> { ["name"] = Name, ["url"] = Url.Trim(), ["group"] = Group.Trim(), ["max_jobs"] = jobs, ["max_bandwidth_kbps"] = (int)decimal.Round(bandwidth * 1000, 0, MidpointRounding.AwayFromZero) };
        if (Target == null) values["type"] = Type;
        if (Type == "proxy" && (Target != null || PublicUrl.Trim().Length > 0)) values["public_url"] = PublicUrl.Trim().Length == 0 ? null : PublicUrl.Trim();
        if (Type == "transcode" && Target != null)
        {
            if (!new[] { "inherit", "auto", "qsv", "vaapi", "nvenc", "videotoolbox", "none" }.Contains(HwAccelOverride)) throw new ArgumentException("Choose a hardware acceleration policy.");
            if (HwDeviceOverride.Length > 8192) throw new ArgumentException("The device list is too long.");
            values["hw_accel_override"] = HwAccelOverride == "inherit" ? null : HwAccelOverride;
            var devices = AdminPlaybackSettingsRules.ParseDevices(HwDeviceOverride);
            values["hw_device_override"] = devices.Count == 0 ? null : string.Join(",", devices);
        }
        if (Type == "transcode" && sourceAccessSupported && (Target != null || SourceAccess != "local"))
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
