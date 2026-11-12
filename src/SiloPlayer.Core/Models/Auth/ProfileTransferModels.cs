namespace SiloPlayer.Core.Models.Auth;

public sealed class ProfileExportCapability
{
    public string State { get; set; } = "";
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public int MaxImportBytes { get; set; }
    public List<string> DataTypes { get; set; } = [];
}

public sealed class ProfileImportData
{
    public string Type { get; set; } = "";
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int LeftOut { get; set; }
    public List<string> Notes { get; set; } = [];
}

public sealed class ProfileImportResult
{
    public bool SameServer { get; set; }
    public List<ProfileImportData> Data { get; set; } = [];
    public List<string> Unmatched { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
}
