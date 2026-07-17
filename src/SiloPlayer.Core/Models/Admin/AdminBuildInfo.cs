namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminBuildInfo
{
    public string Display { get; set; } = "";
    public string Revision { get; set; } = "";
    public bool Dirty { get; set; }
    public string VcsTime { get; set; } = "";
    public bool Available { get; set; }
}
