namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminHWAccelInfo
{
    public string Resolved { get; set; } = "none";
    public List<string> RenderDevices { get; set; } = [];
    public List<AdminRenderDeviceInfo> RenderDeviceDetails { get; set; } = [];
    public bool IntelDetected { get; set; }
    public string Source { get; set; } = "local";
    public string? NodeUrl { get; set; }
    public List<AdminNodeHWAccelInfo> Nodes { get; set; } = [];
}

public sealed class AdminRenderDeviceInfo
{
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class AdminNodeHWAccelInfo
{
    public string NodeUrl { get; set; } = "";
    public string? NodeName { get; set; }
    public string? Resolved { get; set; }
    public List<string> RenderDevices { get; set; } = [];
    public List<AdminRenderDeviceInfo> RenderDeviceDetails { get; set; } = [];
    public string? Error { get; set; }
}
